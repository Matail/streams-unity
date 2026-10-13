// 멀티 방: 방 만들기 · 들어가기는 HTTP, 판은 웹소켓 (docs/streams-api.md "멀티 (방)", 원본은 matail.party 리포).
// 서버 메시지를 IGameSession 이벤트로 옮기고, 방에만 있는 것(대기실 · 누가 놓았나 · 남은 시간)은 따로 낸다.
// 판 상태는 서버(DO)에만 있다. 여기는 받은 것을 전할 뿐 규칙을 다시 계산하지 않는다.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Streams.Net
{
    /// <summary>방 상태 (서버 state 메시지 그대로).</summary>
    [Serializable]
    public class RoomState
    {
        public string code;
        public string phase;    // lobby · playing · finished
        public int you;
        public int host;
        public int turn;        // playing 이 아니면 -1
        public int card;        // playing 이 아니면 0
        public int timeLeftMs;
        public int mySlot;      // 이번 카드를 내가 이미 놓은 칸, 아니면 -1
        public RoomSeat[] seats;
    }

    [Serializable]
    public class RoomSeat
    {
        public int seat;
        public string name;
        public bool online;
        public bool auto;       // AI 가 대신 두는 중
        public bool placed;     // 이번 카드를 놓음
        public int score;
        public int rank;        // finished 일 때 등수
        public int[] board;     // 공개된 카드만
    }

    public sealed class RoomSession : IGameSession
    {
        public event Action<SessionInfo> Started;
        public event Action<int, int> CardDealt;
        public event Action<int, int, int> Placed;
        public event Action<int, int> ScoreChanged;
        public event Action<GameResult> Finished;

        /// <summary>대기실 · 자리 목록이 바뀜 (들어옴 · 나감 · 연결 · 끊김 · AI 대신 둠 · 방장).</summary>
        public event Action<RoomState> StateChanged;
        /// <summary>누가 이번 카드를 놓음 (칸은 공개 때까지 비밀).</summary>
        public event Action<int> SeatPlaced;
        /// <summary>이번 카드의 남은 시간 (ms). 받은 순간부터 센다.</summary>
        public event Action<int> TimerStarted;
        /// <summary>서버가 요청을 거절 (error.code).</summary>
        public event Action<string> ErrorReceived;
        /// <summary>연결이 닫힘 (닫는 코드: 4000 다른 탭, 4001 토큰, 4004 방 없음, 1006 끊김).</summary>
        public event Action<int> Disconnected;

        public string Code => ticket.code;
        public int MySeat => ticket.seat;
        public RoomState State { get; private set; }

        readonly RoomTicket ticket;
        readonly RoomSocket socket;
        int dealtTurn = -1; // 이미 CardDealt 를 낸 턴 (시작 때 state 와 card 가 같은 카드를 알린다)
        AwaitableCompletionSource pendingStart, pendingPlace;

        public static Awaitable<RoomTicket> CreateAsync(ApiClient api, string name) =>
            api.PostAsync<RoomRequest, RoomTicket>("/rooms", new RoomRequest { name = name, playerId = ClientInfo.PlayerId });

        public static Awaitable<RoomTicket> JoinAsync(ApiClient api, string code, string name) =>
            api.PostAsync<RoomRequest, RoomTicket>($"/rooms/{Uri.EscapeDataString(code)}/join", new RoomRequest { name = name, playerId = ClientInfo.PlayerId });

        /// <summary>방에 웹소켓으로 붙는다. baseUrl 은 HTTP 주소 (http→ws, https→wss).</summary>
        public RoomSession(string baseUrl, RoomTicket ticket)
        {
            this.ticket = ticket;
            var ws = (baseUrl.StartsWith("https:") ? "wss:" + baseUrl.Substring(6) : "ws:" + baseUrl.Substring(5)).TrimEnd('/');
            socket = new RoomSocket($"{ws}/api/streams/rooms/{ticket.code}/ws");
            socket.Opened += () => socket.Send(JsonUtility.ToJson(new Hello { seatToken = ticket.seatToken }));
            socket.Message += OnMessage;
            socket.Closed += code =>
            {
                Fail(ref pendingStart, 0, "disconnected");
                Fail(ref pendingPlace, 0, "disconnected");
                Disconnected?.Invoke(code);
            };
        }

        /// <summary>방장이 판을 시작한다 (level 은 쓰지 않음). 거절되면 StreamsApiException(400, 오류 코드).</summary>
        public Awaitable StartAsync(SessionOptions options)
        {
            if (pendingStart != null) throw new InvalidOperationException("start already pending");
            if (!socket.IsOpen) throw new StreamsApiException(0, "disconnected");
            pendingStart = new AwaitableCompletionSource();
            var wait = pendingStart.Awaitable;
            socket.Send("{\"t\":\"start\"}");
            return wait;
        }

        /// <summary>이번 카드를 놓는다. 서버가 placed 로 확인하면 끝난다. 거절되면 StreamsApiException(400, 오류 코드).</summary>
        public Awaitable PlaceAsync(int slot, int thinkMs)
        {
            if (pendingPlace != null) throw new InvalidOperationException("previous move still pending");
            if (!socket.IsOpen) throw new StreamsApiException(0, "disconnected");
            pendingPlace = new AwaitableCompletionSource();
            var wait = pendingPlace.Awaitable;
            socket.Send($"{{\"t\":\"place\",\"turn\":{dealtTurn},\"slot\":{slot},\"thinkMs\":{Math.Max(0, thinkMs)}}}");
            return wait;
        }

        /// <summary>AI 가 대신 두는 중일 때 "돌아가기" — 다음 카드부터 직접 둔다.</summary>
        public void Resume() => socket.Send("{\"t\":\"resume\"}");

        /// <summary>방을 나간다. 판 도중이면 그 판은 AI 가 끝까지 대신 둔다.</summary>
        public void Leave() => socket.Send("{\"t\":\"leave\"}");

        /// <summary>받은 메시지를 이벤트로 낸다. 매 프레임 메인 스레드에서 부른다 (GameController.Update).</summary>
        public void Poll() => socket.Poll();

        /// <summary>연결만 끊는다 (자리는 남아 hello 로 돌아올 수 있다).</summary>
        public void Close() => socket.Close();

        // ── 서버 메시지 ──

        [Serializable] class Hello { public string t = "hello"; public string seatToken; }

        /// <summary>모든 서버 메시지의 합집합. JsonUtility 는 없는 필드를 기본값으로 둔다.</summary>
        [Serializable]
        class ServerMessage
        {
            public string t;
            public string code;     // state: 방 코드, error: 오류 코드
            public string phase;
            public int you, host, turn, card, timeLeftMs, mySlot, seat;
            public RoomSeat[] seats;
        }

        [Serializable]
        class Reveal
        {
            public RevealSeat[] seats;
        }

        [Serializable]
        class RevealSeat
        {
            public int seat, slot, score;
            public bool auto;
        }

        void OnMessage(string text)
        {
            if (text == "pong") return;
            ServerMessage m;
            try
            {
                m = JsonUtility.FromJson<ServerMessage>(text);
            }
            catch (ArgumentException)
            {
                Debug.LogWarning($"room: bad message {text}");
                return;
            }
            switch (m.t)
            {
                case "state": OnState(text); break;
                case "card": Deal(m.card, m.turn, m.timeLeftMs); break;
                case "placed":
                    if (m.seat == MySeat) Done(ref pendingPlace);
                    SeatPlaced?.Invoke(m.seat);
                    break;
                case "reveal":
                    foreach (var s in JsonUtility.FromJson<Reveal>(text).seats)
                    {
                        // 내 수는 UI 가 먼저 그렸다 — 시간이 지나 AI 가 대신 둔 내 수만 알린다
                        if (s.seat != MySeat || s.auto) Placed?.Invoke(s.seat, s.slot, m.card);
                        ScoreChanged?.Invoke(s.seat, s.score);
                    }
                    break;
                case "finished":
                    // 서버는 끝날 때 state 를 따로 보내지 않는다 — 들고 있는 상태를 끝난 판으로 맞춘다
                    var standings = new List<Standing>();
                    foreach (var s in m.seats)
                    {
                        standings.Add(new Standing(s.seat, s.score, s.rank));
                        if (State == null) continue;
                        foreach (var mine in State.seats)
                            if (mine.seat == s.seat) { mine.score = s.score; mine.rank = s.rank; mine.placed = false; }
                    }
                    if (State != null)
                    {
                        State.phase = "finished";
                        State.turn = -1;
                        State.card = 0;
                        State.mySlot = -1;
                        StateChanged?.Invoke(State);
                    }
                    Finished?.Invoke(new GameResult(standings, MySeat));
                    break;
                case "error":
                    Fail(ref pendingStart, 400, m.code);
                    Fail(ref pendingPlace, 400, m.code);
                    ErrorReceived?.Invoke(m.code);
                    break;
            }
        }

        void OnState(string text)
        {
            var prev = State?.phase;
            State = JsonUtility.FromJson<RoomState>(text);
            StateChanged?.Invoke(State);
            if (State.phase != "playing" || prev == "playing") return;

            // 판이 시작됨 (또는 판 도중에 붙음)
            dealtTurn = -1;
            var players = new List<PlayerInfo>();
            foreach (var s in State.seats) players.Add(new PlayerInfo(s.seat, s.name, false));
            Done(ref pendingStart);
            Started?.Invoke(new SessionInfo(State.code, 0, "멀티", MySeat, players));
            foreach (var s in State.seats) ScoreChanged?.Invoke(s.seat, s.score);
            if (State.mySlot < 0) Deal(State.card, State.turn, State.timeLeftMs);
            else dealtTurn = State.turn; // 이번 카드는 이미 놓았다 — 다음 card 를 기다린다
        }

        void Deal(int card, int turn, int timeLeftMs)
        {
            // 시작 때 state 는 마감을 정하기 전에 와서 남은 시간이 0 일 수 있다 — 같은 카드의 card 가 오면 시간만 고친다
            if (turn != dealtTurn)
            {
                dealtTurn = turn;
                CardDealt?.Invoke(card, turn);
            }
            TimerStarted?.Invoke(timeLeftMs);
        }

        static void Done(ref AwaitableCompletionSource pending)
        {
            var p = pending;
            pending = null;
            p?.SetResult();
        }

        static void Fail(ref AwaitableCompletionSource pending, long status, string error)
        {
            var p = pending;
            pending = null;
            p?.SetException(new StreamsApiException(status, error));
        }
    }
}
