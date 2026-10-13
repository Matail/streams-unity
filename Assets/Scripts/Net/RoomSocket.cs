// 멀티 방 웹소켓 하나. WebGL 은 브라우저 WebSocket(Plugins/WebGL/StreamsSocket.jslib),
// 그 밖(에디터 · 데스크톱)은 ClientWebSocket — System.Net.WebSockets 는 WebGL 에서 안 돈다.
// 받은 것은 큐에 모았다가, 쓰는 쪽이 매 프레임 Poll() 을 부르면 메인 스레드에서 이벤트로 낸다.
// (숨은 비동기 루프를 두지 않는다 — 에디터 모드에서는 Awaitable.NextFrameAsync 가 이어지지 않는다.)
// 큐 항목: "o" 열림, "m<본문>" 메시지, "c<코드>" 닫힘.
using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Streams.Net
{
    public sealed class RoomSocket
    {
        const float PingSeconds = 25f; // 서버가 DO 를 깨우지 않고 "pong" 으로 답한다

        public event Action Opened;
        public event Action<string> Message;
        public event Action<int> Closed; // 닫는 코드 (1006 = 비정상 끊김)

        public bool IsOpen { get; private set; }
        bool closed;
        float pingAt;

        public RoomSocket(string url) => Open(url);

        /// <summary>받은 것을 이벤트로 낸다. 매 프레임 메인 스레드에서 부른다.</summary>
        public void Poll()
        {
            if (closed) return;
            for (var e = Next(); e != null; e = Next())
            {
                switch (e[0])
                {
                    case 'o':
                        IsOpen = true;
                        pingAt = Time.realtimeSinceStartup + PingSeconds;
                        Opened?.Invoke();
                        break;
                    case 'm':
                        Message?.Invoke(e.Substring(1));
                        break;
                    case 'c':
                        IsOpen = false;
                        closed = true;
                        Closed?.Invoke(int.TryParse(e.Substring(1), out var code) ? code : 1006);
                        return;
                }
            }
            if (IsOpen && Time.realtimeSinceStartup >= pingAt)
            {
                pingAt = Time.realtimeSinceStartup + PingSeconds;
                Send("ping");
            }
        }

        public void Send(string text)
        {
            if (IsOpen) SendRaw(text);
        }

        public void Close(int code = 1000)
        {
            if (!closed) CloseRaw(code);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int StreamsSocketOpen(string url);
        [DllImport("__Internal")] static extern void StreamsSocketSend(int id, string text);
        [DllImport("__Internal")] static extern void StreamsSocketClose(int id, int code);
        [DllImport("__Internal")] static extern string StreamsSocketPoll(int id);

        int id;

        void Open(string url) => id = StreamsSocketOpen(url);
        string Next() => StreamsSocketPoll(id);
        void SendRaw(string text) => StreamsSocketSend(id, text);
        void CloseRaw(int code) => StreamsSocketClose(id, code);
#else
        readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        readonly ClientWebSocket ws = new ClientWebSocket();
        Task sending = Task.CompletedTask; // ClientWebSocket 은 보내기를 겹쳐 부를 수 없다 — 줄 세운다

        void Open(string url) => _ = Receive(new Uri(url));
        string Next() => queue.TryDequeue(out var e) ? e : null;

        async Task Receive(Uri uri)
        {
            try
            {
                await ws.ConnectAsync(uri, CancellationToken.None).ConfigureAwait(false);
                queue.Enqueue("o");
                var buf = new byte[4096];
                using var text = new MemoryStream();
                while (true)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        int code = (int)(ws.CloseStatus ?? (WebSocketCloseStatus)1006);
                        if (ws.State == WebSocketState.CloseReceived)
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
                        queue.Enqueue("c" + code);
                        return;
                    }
                    text.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    queue.Enqueue("m" + Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length));
                    text.SetLength(0);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"room socket: {e.Message}");
                queue.Enqueue("c1006");
            }
        }

        void SendRaw(string text)
        {
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
            sending = sending.ContinueWith(_ => ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)).Unwrap();
        }

        void CloseRaw(int code)
        {
            sending = sending.ContinueWith(_ => ws.State == WebSocketState.Open
                ? ws.CloseOutputAsync((WebSocketCloseStatus)code, "", CancellationToken.None)
                : Task.CompletedTask).Unwrap();
        }
#endif
    }
}
