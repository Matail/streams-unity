// 멀티 방 웹소켓 (WebGL). C# 쪽은 Assets/Scripts/Net/RoomSocket.cs.
// 받은 것은 연결마다 큐에 쌓고, C# 이 매 프레임 StreamsSocketPoll 로 하나씩 꺼낸다 (콜백 없이).
// 큐 항목: "o" 열림, "m<본문>" 메시지, "c<코드>" 닫힘.
var StreamsSocketLib = {
  $SS: { next: 1, socks: {} },

  StreamsSocketOpen: function (urlPtr) {
    var id = SS.next++;
    var s = { q: [], ws: null };
    SS.socks[id] = s;
    try {
      s.ws = new WebSocket(UTF8ToString(urlPtr));
      s.ws.onopen = function () { s.q.push("o"); };
      s.ws.onmessage = function (e) { if (typeof e.data === "string") s.q.push("m" + e.data); };
      s.ws.onclose = function (e) { s.q.push("c" + e.code); };
    } catch (e) {
      s.q.push("c1006");
    }
    return id;
  },

  StreamsSocketSend: function (id, textPtr) {
    var s = SS.socks[id];
    if (s && s.ws && s.ws.readyState === 1) s.ws.send(UTF8ToString(textPtr));
  },

  StreamsSocketClose: function (id, code) {
    var s = SS.socks[id];
    if (s && s.ws && s.ws.readyState < 2) s.ws.close(code);
  },

  // 다음 항목 (없으면 null). 닫힘을 꺼내면 연결을 잊는다
  StreamsSocketPoll: function (id) {
    var s = SS.socks[id];
    if (!s || s.q.length === 0) return 0;
    var str = s.q.shift();
    if (str[0] === "c") delete SS.socks[id];
    var size = lengthBytesUTF8(str) + 1;
    var buf = _malloc(size);
    stringToUTF8(str, buf, size);
    return buf;
  },
};

autoAddDeps(StreamsSocketLib, "$SS");
mergeInto(LibraryManager.library, StreamsSocketLib);
