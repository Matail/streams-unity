// 멀티 방 화면 (친구와 대전, docs/streams-unity-plan.md 5단계):
// 닉네임 → 방 만들기 · 코드로 들어가기 → 대기실 → 판 (상대 보드 여러 줄, 20초) → 결과 → 대기실.
// 서버 메시지는 RoomSession 이벤트로 받는다 (Update 에서 Poll). 판 화면은 AI 대전과 같은 것을 쓴다.
// 연결이 끊기면 같은 자리로 다시 붙는다 — 방 코드 · 자리 토큰을 PlayerPrefs 에 두어 새로고침해도 이어진다.
using System;
using System.Collections.Generic;
using Streams.Core;
using Streams.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Streams.Presentation
{
    public sealed partial class GameController
    {
        const string RoomKey = "streams.room", NameKey = "streams.name";
        static readonly float[] Backoff = { 1, 2, 4, 8, 15 }; // 다시 붙기 간격 (초)
        // 무작위 닉네임 — 폰트 서브셋(KS X 1001 2350자) 안의 글자만
        static readonly string[] NameA = { "파란", "빨간", "노란", "초록", "하얀", "작은", "빠른", "느린", "졸린", "용감한" };
        static readonly string[] NameB = { "고래", "여우", "토끼", "곰", "부엉이", "다람쥐", "거북", "수달", "너구리", "참새" };

        ApiClient api;
        RoomTicket roomTicket; // 들어간 방 (없으면 AI 대전). 다시 붙을 때도 이 표를 쓴다
        RoomSession room;      // 지금 연결 (끊긴 뒤 다시 붙는 동안엔 닫힌 것일 수 있다)
        int reconnects;
        float reconnectAt = -1, deadlineAt;
        bool roomBusy;
        readonly HashSet<int> placedSeats = new HashSet<int>(); // 이번 카드를 놓은 자리
        readonly Dictionary<int, Opponent> opponents = new Dictionary<int, Opponent>();

        sealed class Opponent
        {
            public string Name;
            public int Score;
            public Text Label;
            public BoardView View;
            public readonly Board Board = new Board();
        }

        GameObject multiPanel, lobbyPanel;
        RectTransform oppList;
        InputField nameInput, codeInput;
        Text multiError, lobbyCode, lobbySeats, lobbyNote, timerText;
        string lobbyError; // 대기실에 보일 오류 (없으면 상태 안내)
        Button startButton, resumeButton;

        bool IsRoom => roomTicket != null;
        RoomState RoomNow => room?.State;
        bool MyAuto => IsRoom && RoomNow != null && RoomNow.phase == "playing" && SeatOf(RoomNow, mySeat)?.auto == true;

        static RoomSeat SeatOf(RoomState st, int seat)
        {
            foreach (var s in st.seats) if (s.seat == seat) return s;
            return null;
        }

        // ── 들어가기 · 나가기 · 다시 붙기 ──────────────────────────────

        void ShowMultiPanel()
        {
            startPanel.SetActive(false);
            multiError.text = "";
            if (string.IsNullOrEmpty(nameInput.text)) nameInput.text = PlayerPrefs.GetString(NameKey, RandomName());
            multiPanel.SetActive(true);
        }

        static string RandomName() =>
            $"{NameA[UnityEngine.Random.Range(0, NameA.Length)]} {NameB[UnityEngine.Random.Range(0, NameB.Length)]}";

        /// <summary>닉네임: 앞뒤 공백을 지우고 1~12자 (서버와 같은 규칙). 틀리면 null.</summary>
        string CleanName()
        {
            var name = (nameInput.text ?? "").Trim();
            return name.Length >= 1 && name.Length <= 12 ? name : null;
        }

        async void CreateRoom()
        {
            var name = CleanName();
            if (name == null) { multiError.text = "닉네임을 1~12자로 적어 주세요"; return; }
            if (roomBusy) return;
            roomBusy = true;
            multiError.text = "방을 만드는 중…";
            try
            {
                Enter(await RoomSession.CreateAsync(api, name), name);
            }
            catch (StreamsApiException e)
            {
                Debug.LogWarning(e.Message);
                multiError.text = e.Status == 429 ? "방을 너무 많이 만들었어요. 잠시 뒤 다시 해 주세요." : "방을 만들지 못했어요. 잠시 뒤 다시 해 주세요.";
            }
            roomBusy = false;
        }

        async void JoinRoom()
        {
            var name = CleanName();
            if (name == null) { multiError.text = "닉네임을 1~12자로 적어 주세요"; return; }
            var code = (codeInput.text ?? "").Trim().ToUpperInvariant();
            if (code.Length != 6) { multiError.text = "방 코드 6자를 적어 주세요"; return; }
            if (roomBusy) return;
            roomBusy = true;
            multiError.text = "들어가는 중…";
            try
            {
                Enter(await RoomSession.JoinAsync(api, code, name), name);
            }
            catch (StreamsApiException e)
            {
                Debug.LogWarning(e.Message);
                multiError.text = e.Error switch
                {
                    "room not found" => "그런 방이 없어요. 코드를 확인해 주세요.",
                    "room full" => "방이 꽉 찼어요 (8명).",
                    "game in progress" => "판이 진행 중이에요. 끝난 뒤에 들어올 수 있어요.",
                    _ => "들어가지 못했어요. 잠시 뒤 다시 해 주세요.",
                };
            }
            roomBusy = false;
        }

        void Enter(RoomTicket ticket, string name)
        {
            PlayerPrefs.SetString(NameKey, name);
            RecordAbandon();
            tutorial = false;
            tutView.Hide();
            playing = false;
            Sound.Music();
            roomTicket = ticket;
            PlayerPrefs.SetString(RoomKey, JsonUtility.ToJson(ticket));
            PlayerPrefs.Save();
            reconnects = 0;
            Connect();
            ShowLobby();
        }

        /// <summary>저장된 방이 있으면 같은 자리로 다시 붙는다 (Awake). 없어진 방이면 서버가 4001/4004 로 닫는다.</summary>
        void ResumeRoom()
        {
            var saved = PlayerPrefs.GetString(RoomKey, "");
            if (saved.Length == 0) return;
            roomTicket = JsonUtility.FromJson<RoomTicket>(saved);
            Connect();
            ShowLobby();
        }

        void Connect()
        {
            room = new RoomSession(ClientInfo.BaseUrl, roomTicket);
            session = room;
            Bind(room);
            var r = room;
            r.StateChanged += st => { if (r == room) OnRoomState(st); };
            r.SeatPlaced += seat => { if (r != room) return; placedSeats.Add(seat); dirty = true; };
            r.TimerStarted += ms => { if (r == room) deadlineAt = Time.realtimeSinceStartup + ms / 1000f; };
            r.ErrorReceived += code =>
            {
                if (r != room || code == "already_placed") return;
                lobbyError = ErrorText(code);
                RenderLobby();
            };
            r.Disconnected += code => { if (r == room) OnRoomClosed(code); };
        }

        static string ErrorText(string code) => code switch
        {
            "not_host" => "방장만 시작할 수 있어요",
            "need_players" => "2명 이상 모여야 시작할 수 있어요",
            "bad_phase" => "지금은 할 수 없어요",
            _ => "요청을 처리하지 못했어요",
        };

        /// <summary>방을 나간다 (판 도중이면 그 판은 AI 가 끝까지 대신 둔다).</summary>
        void LeaveRoom()
        {
            if (!IsRoom) return;
            room?.Leave();
            room?.Close();
            DropRoom(true);
        }

        void DropRoom(bool forget)
        {
            if (forget)
            {
                PlayerPrefs.DeleteKey(RoomKey);
                PlayerPrefs.Save();
            }
            roomTicket = null;
            room = null;
            session = ai;
            reconnectAt = -1;
            playing = false;
            card = 0;
            lobbyPanel.SetActive(false);
            multiPanel.SetActive(false);
            ClearOpponents();
            dirty = true;
        }

        void OnRoomClosed(int code)
        {
            switch (code)
            {
                case 4000: // 다른 창이 이 자리로 들어감 — 저장된 표는 그 창이 쓴다
                    DropRoom(false);
                    ShowStart("다른 창에서 이 방에 들어갔어요.");
                    return;
                case 4001:
                case 4004:
                    DropRoom(true);
                    ShowStart("방이 없어졌어요.");
                    return;
                default:
                    reconnectAt = Time.realtimeSinceStartup + Backoff[Mathf.Min(reconnects, Backoff.Length - 1)];
                    reconnects++;
                    hintText.text = lobbyError = "연결이 끊겼어요. 다시 붙는 중…";
                    RenderLobby();
                    return;
            }
        }

        void ShowStart(string error)
        {
            endPanel.SetActive(false);
            startPanel.SetActive(true);
            startError.text = error;
            startError.gameObject.SetActive(true);
        }

        /// <summary>매 프레임: 받은 메시지 처리, 다시 붙기, 남은 시간.</summary>
        void UpdateRoom()
        {
            if (!IsRoom) return;
            room?.Poll();
            if (reconnectAt >= 0 && Time.realtimeSinceStartup >= reconnectAt)
            {
                reconnectAt = -1;
                Connect();
            }
            if (timerText.gameObject.activeSelf)
            {
                int left = Mathf.Clamp(Mathf.CeilToInt(deadlineAt - Time.realtimeSinceStartup), 0, 20);
                var t = $"{left}";
                if (timerText.text != t) timerText.text = t;
            }
        }

        // ── 서버 상태 → 화면 ──────────────────────────────────────

        void OnRoomState(RoomState st)
        {
            if (reconnects > 0) lobbyError = null; // 다시 붙음
            reconnects = 0;
            mySeat = st.you;
            foreach (var s in st.seats)
                if (opponents.TryGetValue(s.seat, out var o)) o.Name = s.name;
            if (st.phase == "lobby" && !lobbyPanel.activeSelf && !endPanel.activeSelf) ShowLobby();
            RenderLobby();
            dirty = true;
        }

        /// <summary>판 시작 (또는 판 도중에 붙음) — 보드를 서버 상태로 다시 채운다.</summary>
        void OnRoomStarted()
        {
            var st = RoomNow;
            myBoard.Reset();
            Array.Clear(seen, 0, seen.Length);
            placedSeats.Clear();
            placedTurn = -1;
            pendingSlot = -1;
            ClearOpponents();
            foreach (var s in st.seats)
            {
                if (s.placed) placedSeats.Add(s.seat);
                if (s.seat == mySeat)
                {
                    for (int i = 0; i < Rules.Slots; i++)
                        if (s.board[i] != 0) myBoard.Place(i, s.board[i]);
                    myScoreText.text = s.score.ToString();
                    continue;
                }
                var o = AddOpponent(s.seat, s.name);
                for (int i = 0; i < Rules.Slots; i++)
                    if (s.board[i] != 0) o.Board.Place(i, s.board[i]);
                o.Score = s.score;
            }
            if (st.mySlot >= 0)
            {
                // 이번 카드는 이미 놓았다 (다시 붙음) — 다음 카드를 기다린다
                myBoard.Place(st.mySlot, st.card);
                card = st.card;
                turn = placedTurn = st.turn;
            }
            // 나온 카드 = 내 보드의 카드 (모두가 같은 카드를 받으므로). 이번 카드는 CardDealt 가 센다
            foreach (var v in myBoard.Cells) if (v != 0) seen[v]++;
            playing = true;
            hintText.text = "내 보드의 빈칸을 눌러 놓으세요";
            startPanel.SetActive(false);
            multiPanel.SetActive(false);
            lobbyPanel.SetActive(false);
            endPanel.SetActive(false);
            startError.gameObject.SetActive(false);
        }

        void OnRoomCard()
        {
            placedSeats.Clear();
            foreach (var o in opponents.Values) o.View.Highlight(-1);
        }

        void OpponentPlaced(int seat, int slot, int c)
        {
            if (!opponents.TryGetValue(seat, out var o) || !o.Board.IsEmpty(slot)) return;
            o.Board.Place(slot, c);
            o.View.Highlight(slot);
        }

        void OpponentScore(int seat, int score)
        {
            if (opponents.TryGetValue(seat, out var o)) o.Score = score;
        }

        void ShowRoomResult(GameResult r)
        {
            var me = r.Mine;
            bool tied = false;
            var order = new List<Standing>(r.Standings);
            order.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Seat.CompareTo(b.Seat));
            var lines = new List<string>();
            foreach (var s in order)
            {
                if (s.Seat != me.Seat && s.Rank == me.Rank) tied = true;
                var name = s.Seat == mySeat ? $"{NameOf(s.Seat)} (나)" : NameOf(s.Seat);
                lines.Add($"{s.Rank}위  {name}  {s.Score}점");
            }
            resultTitle.text = me.Rank == 1 ? (tied ? "공동 1위!" : "1위!") : $"{me.Rank}위";
            resultScore.fontSize = 26;
            resultScore.text = string.Join("\n", lines);
            againButton.gameObject.SetActive(true);
            againButton.GetComponentInChildren<Text>().text = "대기실로";
            endPanel.SetActive(true);
            Sound.Play(me.Rank == 1 ? Sfx.Win : Sfx.Lose);
        }

        void ShowLobby()
        {
            startPanel.SetActive(false);
            multiPanel.SetActive(false);
            endPanel.SetActive(false);
            lobbyError = null;
            lobbyPanel.SetActive(true);
            RenderLobby();
        }

        void RenderLobby()
        {
            if (!IsRoom) return;
            lobbyCode.text = roomTicket.code;
            var st = RoomNow;
            if (st == null)
            {
                lobbySeats.text = "연결 중…";
                startButton.gameObject.SetActive(false);
                return;
            }
            var lines = new List<string>();
            int online = 0;
            foreach (var s in st.seats)
            {
                if (s.online) online++;
                var tags = (s.seat == st.host ? "★ " : "") + s.name + (s.seat == st.you ? " (나)" : "") + (s.online ? "" : " (연결 끊김)");
                lines.Add(tags);
            }
            lobbySeats.text = string.Join("\n", lines);
            bool host = st.host == st.you;
            startButton.gameObject.SetActive(host && st.phase != "playing");
            startButton.interactable = online >= 2;
            lobbyNote.color = lobbyError != null ? Ui.Red : Ui.Muted;
            lobbyNote.text = lobbyError ?? (st.phase == "playing" ? "판이 진행 중이에요"
                : host ? (online >= 2 ? "모두 모이면 시작을 눌러 주세요" : "친구에게 방 코드를 알려 주세요")
                : "방장이 시작하길 기다리는 중");
        }

        async void StartRoom()
        {
            if (room == null || roomBusy) return;
            roomBusy = true;
            lobbyError = null;
            try
            {
                await room.StartAsync(default);
            }
            catch (StreamsApiException e)
            {
                lobbyError = e.Status == 0 ? "연결이 끊겼어요" : ErrorText(e.Error);
                RenderLobby();
            }
            roomBusy = false;
        }

        /// <summary>판 화면의 멀티 부분 (Render 에서 부른다).</summary>
        void RenderRoom()
        {
            oppList.gameObject.SetActive(IsRoom);
            bool live = IsRoom && playing;
            timerText.gameObject.SetActive(live && placedTurn != turn && !MyAuto);
            resumeButton.gameObject.SetActive(live && MyAuto);
            if (!IsRoom) return;

            levelText.text = "ROOM";
            levelNameText.text = roomTicket.code;
            modeText.text = "MULTI";
            agentSubText.text = $"상대 {opponents.Count}명";
            levelsLabel.text = "방 나가기";
            aiScoreLabel.text = "TOP";
            int best = 0;
            foreach (var o in opponents.Values) best = Mathf.Max(best, o.Score);
            aiScoreText.text = best.ToString();

            var st = RoomNow;
            foreach (var kv in opponents)
            {
                var o = kv.Value;
                var s = st != null ? SeatOf(st, kv.Key) : null;
                var status = s == null ? "" : !s.online ? " · 끊김" : s.auto ? " · AI" : "";
                o.Label.text = $"{o.Name}  {o.Score}{status}";
                o.Label.color = placedSeats.Contains(kv.Key) && playing ? Ui.Gold : s != null && !s.online ? Ui.Muted : Ui.Ink;
                o.View.Render(o.Board, false);
            }

            if (!playing) return;
            if (MyAuto) hintText.text = "AI가 대신 두는 중이에요";
            else if (placedTurn == turn)
            {
                int total = opponents.Count + 1;
                hintText.text = $"다른 사람을 기다리는 중 ({Mathf.Min(placedSeats.Count, total)}/{total})";
            }
            else if (hintText.text.Length == 0 || hintText.text.StartsWith("다른 사람") || hintText.text.StartsWith("AI가") || hintText.text.StartsWith("연결"))
                hintText.text = "내 보드의 빈칸을 눌러 놓으세요";
        }

        // ── 상대 보드 줄 ─────────────────────────────────────────

        Opponent AddOpponent(int seat, string name)
        {
            var row = Ui.Rect($"Seat{seat}", oppList);
            Ui.Layout<HorizontalLayoutGroup>(row, 12);
            Ui.Size(row, 0, 34);
            var label = Ui.Label("Name", row, name, 22, Ui.Ink, TextAnchor.MiddleRight);
            Ui.Size(label, 240, 34);
            // 보드는 반 크기로: 1×20 카드를 0.5배
            float w = Rules.Slots * BoardView.CardW + (Rules.Slots - 1) * BoardView.Gap, h = BoardView.CardH;
            var holder = Ui.Rect("Board", row);
            Ui.Size(holder, w / 2, h / 2);
            var view = new BoardView("Cards", holder, null);
            var cards = (RectTransform)holder.GetChild(0);
            cards.anchorMin = cards.anchorMax = cards.pivot = new Vector2(0, 0.5f);
            cards.anchoredPosition = Vector2.zero;
            cards.sizeDelta = new Vector2(w, h);
            cards.localScale = new Vector3(0.5f, 0.5f, 1);
            var o = new Opponent { Name = name, Label = label, View = view };
            opponents[seat] = o;
            return o;
        }

        void ClearOpponents()
        {
            foreach (var o in opponents.Values) Destroy(o.Label.transform.parent.gameObject);
            opponents.Clear();
        }

        // ── 화면 만들기 ──────────────────────────────────────────

        /// <summary>판 화면에 붙는 것: 위쪽 상대 줄들, 카드 아래 남은 시간 · 돌아가기.</summary>
        void BuildRoomCenter(RectTransform area, RectTransform current)
        {
            oppList = Ui.Rect("Opponents", area);
            oppList.anchorMin = oppList.anchorMax = oppList.pivot = new Vector2(0.5f, 1f);
            oppList.anchoredPosition = new Vector2(0, -6);
            oppList.sizeDelta = new Vector2(1024, 290);
            Ui.Layout<VerticalLayoutGroup>(oppList, 4, TextAnchor.UpperCenter).childForceExpandWidth = true;
            oppList.gameObject.SetActive(false);

            timerText = Ui.PixelLabel("Timer", current, "", 32, Ui.Gold);
            timerText.gameObject.SetActive(false);
            resumeButton = Styled(Ui.Button("Resume", current, "돌아가기 — 내가 둘게요", 22, () => room?.Resume()), Ui.Blue);
            Ui.Size(resumeButton, 0, 48);
            resumeButton.gameObject.SetActive(false);
        }

        void BuildRoomPanels(Transform parent)
        {
            // 친구와 대전: 닉네임, 방 만들기, 코드로 들어가기
            var dim = Ui.Box("MultiPanel", parent, new Color(0, 0, 0, 0.7f));
            Ui.Fill(dim.rectTransform);
            multiPanel = dim.gameObject;
            var box = Panel("Box", dim.transform);
            box.rectTransform.sizeDelta = new Vector2(640, 700);
            var col = Column(box.rectTransform, 14, 36);
            Ui.Label("Title", col, "친구와 대전", 48, Ui.Gold);
            Ui.Label("NameLabel", col, "닉네임", 22, Ui.Muted);
            nameInput = Ui.Input("Name", col, "1~12자", 26, 12);
            Ui.Size(nameInput, 0, 56);
            Ui.Size(Styled(Ui.Button("Create", col, "방 만들기", 22, CreateRoom), Ui.Blue), 0, 64);
            Ui.Label("CodeLabel", col, "방 코드로 들어가기", 22, Ui.Muted);
            codeInput = Ui.Input("Code", col, "6자 코드", 26, 6);
            codeInput.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : '\0';
            Ui.Size(codeInput, 0, 56);
            Ui.Size(Styled(Ui.Button("Join", col, "들어가기", 22, JoinRoom), Ui.Red), 0, 64);
            multiError = Ui.Label("Error", col, "", 22, Ui.Red);
            Ui.Size(Styled(Ui.Button("Back", col, "뒤로", 22, () =>
            {
                multiPanel.SetActive(false);
                startPanel.SetActive(true);
            }), new Color(0.3f, 0.34f, 0.42f)), 0, 56);
            multiPanel.SetActive(false);

            // 대기실: 방 코드, 들어온 사람, 시작 (방장)
            dim = Ui.Box("LobbyPanel", parent, new Color(0, 0, 0, 0.7f));
            Ui.Fill(dim.rectTransform);
            lobbyPanel = dim.gameObject;
            box = Panel("Box", dim.transform);
            box.rectTransform.sizeDelta = new Vector2(640, 720);
            col = Column(box.rectTransform, 12, 36);
            Ui.Label("Title", col, "대기실 · 방 코드", 33, Ui.Gold);
            lobbyCode = Ui.PixelLabel("Code", col, "", 96, Ui.Ink);
            Ui.Size(lobbyCode, 0, 110);
            lobbySeats = Ui.Label("Seats", col, "", 26, Ui.Ink, TextAnchor.UpperCenter);
            lobbySeats.lineSpacing = 1.15f;
            Ui.Size(lobbySeats, 0, 250);
            lobbyNote = Ui.Label("Note", col, "", 22, Ui.Muted);
            startButton = Styled(Ui.Button("Start", col, "시작", 22, StartRoom), Ui.Blue);
            Ui.Size(startButton, 0, 64);
            Ui.Size(Styled(Ui.Button("Leave", col, "방 나가기", 22, () =>
            {
                LeaveRoom();
                startPanel.SetActive(true);
                Render();
            }), Ui.Red), 0, 64);
            lobbyPanel.SetActive(false);
        }
    }
}
