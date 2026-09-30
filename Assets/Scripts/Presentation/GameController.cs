// STREAMS AI 대전 화면. 어떤 씬이든 로드되면 스스로 만들어진다 (GameBootstrap).
// 흐름은 웹 버전(my-site public/games/streams/script.js)과 같다: 난이도 고르기 → 20턴 → 결과.
using System;
using Streams.Core;
using Streams.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Streams.Presentation
{
    public sealed class GameController : MonoBehaviour
    {
        static readonly string[] Levels = { "입문", "보통", "숙련", "고수", "마스터" }; // 난이도 1~5

        IGameSession session;
        readonly Board myBoard = new Board();
        readonly Board aiBoard = new Board();
        int card, turn, level;
        string levelName = "-";
        bool busy, playing;
        float cardShownAt;

        // 이번 판에 나온 카드 수 (값별) — 오른쪽 "남은 카드" 창
        readonly int[] seen = new int[Rules.MaxCard + 1];

        BoardView myView, aiView;
        Text turnText, myScoreText, aiScoreText, levelText, levelNameText, agentSubText, cardText, hintText,
            deckText, runsText, resultTitle, resultScore, startError;
        Image cardImage;
        readonly Image[] trackCells = new Image[Rules.MaxCard + 1];
        readonly Text[] trackCounts = new Text[Rules.MaxCard + 1];
        GameObject startPanel, endPanel;
        Button againButton; // 튜토리얼 결과에선 숨긴다

        // 튜토리얼 (서버 없이 고정 덱으로 혼자 둔다)
        bool tutorial;
        TutorialView tutView;
        readonly System.Collections.Generic.HashSet<Tutorial.Tip> coached = new System.Collections.Generic.HashSet<Tutorial.Tip>();
        RectTransform myRow, scoreTable;

        void Awake()
        {
            session = new AiSession(new ApiClient(ClientInfo.BaseUrl));
            session.Started += info =>
            {
                level = info.Level;
                levelName = info.LevelName;
            };
            session.CardDealt += (c, t) =>
            {
                card = c;
                turn = t;
                seen[c]++;
                cardShownAt = Time.realtimeSinceStartup;
            };
            session.Placed += (seat, slot, c) =>
            {
                if (seat != Seats.Ai) return;
                aiBoard.Place(slot, c);
                aiView.Highlight(slot);
                Sound.Play(Sfx.Ai);
            };
            session.ScoresChanged += (me, ai) =>
            {
                myScoreText.text = me.ToString();
                aiScoreText.text = ai.ToString();
            };
            session.Finished += r =>
            {
                playing = false;
                card = 0;
                turn = Rules.Slots;
                resultTitle.text = r.MyScore > r.AiScore ? "승리!" : r.MyScore < r.AiScore ? "패배" : "무승부";
                resultScore.fontSize = 33;
                againButton.gameObject.SetActive(true);
                resultScore.text = $"나 {r.MyScore}점 · AI({levelName}) {r.AiScore}점";
                endPanel.SetActive(true);
                Sound.Play(r.MyScore >= r.AiScore ? Sfx.Win : Sfx.Lose);
            };
            Build();
            Render();
        }

        async void StartGame(int lv)
        {
            if (busy) return;
            busy = true;
            tutorial = false;
            tutView.Hide();
            Sound.Music(); // 브라우저는 첫 클릭 전엔 소리를 막으므로 여기서 시작
            startError.gameObject.SetActive(false);
            try
            {
                myBoard.Reset();
                aiBoard.Reset();
                Array.Clear(seen, 0, seen.Length);
                aiView.Highlight(-1);
                await session.StartAsync(new SessionOptions(lv));
                playing = true;
                startPanel.SetActive(false);
                endPanel.SetActive(false);
                hintText.text = "내 보드의 빈칸을 눌러 놓으세요";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                startError.text = "게임을 시작하지 못했어요. 잠시 뒤 다시 눌러 주세요.";
                startError.gameObject.SetActive(true);
            }
            busy = false;
            Render();
        }

        async void Place(int slot)
        {
            if (tutorial)
            {
                PlaceTutorial(slot);
                return;
            }
            if (busy || !playing || !myBoard.IsEmpty(slot)) return;
            busy = true;
            int placed = card;
            int thinkMs = Mathf.RoundToInt((Time.realtimeSinceStartup - cardShownAt) * 1000f);
            myBoard.Place(slot, placed); // 먼저 그린다 — 서버가 거절하면 되돌림
            Sound.Play(Sfx.Place);
            hintText.text = "AI가 생각하는 중…";
            Render();
            try
            {
                await session.PlaceAsync(slot, thinkMs);
                if (playing) hintText.text = "내 보드의 빈칸을 눌러 놓으세요";
                else hintText.text = "게임 끝";
            }
            catch (StreamsApiException e) when (e.Status == 404)
            {
                Debug.LogWarning(e.Message);
                playing = false;
                hintText.text = "판이 사라졌어요. 새 판을 시작해 주세요.";
                startPanel.SetActive(true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                myBoard.Clear(slot);
                hintText.text = "연결이 끊겼어요. 다시 눌러 주세요.";
            }
            busy = false;
            Render();
        }

        void Render()
        {
            myView.Render(myBoard, playing && !busy);
            aiView.Render(aiBoard, false);
            bool showCard = card > 0 && playing;
            cardText.text = showCard ? card.ToString() : "";
            cardText.color = BoardView.CardColor(card);
            cardImage.color = showCard ? Color.white : Ui.EmptySlot;
            turnText.text = $"{Mathf.Min(turn + 1, Rules.Slots)}/{Rules.Slots}";
            levelText.text = tutorial ? "TUTORIAL" : level > 0 ? $"LEVEL {level}" : "LEVEL -";
            levelNameText.text = levelName;
            agentSubText.text = $"AI · {levelName}";

            int dealt = 0;
            for (int v = Rules.MinCard; v <= Rules.MaxCard; v++)
            {
                int left = PoolCount(v) - seen[v];
                dealt += seen[v];
                trackCounts[v].text = left > 0 ? $"x{left}" : "";
                trackCells[v].color = left > 0 ? Color.white : new Color(1f, 1f, 1f, 0.18f);
            }
            deckText.text = (Rules.Pool.Count - dealt).ToString();

            var runs = Rules.RunLengths(myBoard.Cells);
            runsText.text = runs.Count == 0 ? "-" : string.Join(" ", runs) + $"\n= {myBoard.Score} PTS";
        }

        // ── 튜토리얼 ───────────────────────────────────────────────

        void StartTutorial()
        {
            if (busy) return;
            Sound.Music();
            tutorial = true;
            playing = true;
            myBoard.Reset();
            aiBoard.Reset();
            Array.Clear(seen, 0, seen.Length);
            coached.Clear();
            aiView.Highlight(-1);
            level = 0;
            levelName = "혼자 연습";
            turn = 0;
            DealTutorial();
            myScoreText.text = "0";
            aiScoreText.text = "-";
            startPanel.SetActive(false);
            endPanel.SetActive(false);
            hintText.text = "카드를 배치하세요";
            Render();
            ShowOnboarding(0);
        }

        void DealTutorial()
        {
            card = Tutorial.Deck[turn];
            seen[card]++;
        }

        void ShowOnboarding(int i)
        {
            if (i >= Tutorial.Onboarding.Length)
            {
                PreRoundCoach();
                return;
            }
            var targets = new[] { cardImage.rectTransform, myRow, scoreTable };
            var (t, b) = Tutorial.Onboarding[i];
            tutView.Spotlight(targets[i], i, Tutorial.Onboarding.Length, t, b, () => ShowOnboarding(i + 1), PreRoundCoach);
        }

        /// <summary>팁마다 한 번만 보여 준다. 보여 줬으면 true.</summary>
        bool Coach(Tutorial.Tip tip, Action done = null)
        {
            if (!coached.Add(tip)) return false;
            var (t, b) = Tutorial.Coach(tip);
            tutView.Coach(t, b, done);
            return true;
        }

        /// <summary>카드를 놓기 전 상황별 코칭 (원작의 maybePreRoundCoach).</summary>
        void PreRoundCoach()
        {
            if (!playing) return;
            if (turn == 0 && Coach(Tutorial.Tip.Center)) return;
            for (int i = 0; i < Rules.Slots; i++)
                if (myBoard[i] == card && Coach(Tutorial.Tip.Same)) return;
            if (turn == 2 && LongestAdjacent() >= 2) Coach(Tutorial.Tip.Gaps);
        }

        /// <summary>빈칸 없이 붙어 있는 카드 수의 최댓값.</summary>
        int LongestAdjacent()
        {
            int best = 0, run = 0;
            for (int i = 0; i < Rules.Slots; i++)
            {
                run = myBoard.IsEmpty(i) ? 0 : run + 1;
                best = Mathf.Max(best, run);
            }
            return best;
        }

        void PlaceTutorial(int slot)
        {
            if (!playing || !myBoard.IsEmpty(slot) || tutView.Showing) return;

            // 오름차순을 깨는 자리면 처음 한 번은 멈추고 설명한다
            int left = 0, right = Rules.MaxCard + 1;
            for (int l = slot - 1; l >= 0; l--) if (!myBoard.IsEmpty(l)) { left = myBoard[l]; break; }
            for (int r = slot + 1; r < Rules.Slots; r++) if (!myBoard.IsEmpty(r)) { right = myBoard[r]; break; }
            if ((left > card || card > right) &&
                Coach(Tutorial.Tip.Range, () => hintText.text = "다른 칸도 비교해보고 다시 골라보세요"))
                return;

            myBoard.Place(slot, card);
            Sound.Play(Sfx.Place);
            myScoreText.text = myBoard.Score.ToString();
            hintText.text = "카드를 배치하세요";
            turn++;
            if (turn >= Rules.Slots)
            {
                playing = false;
                card = 0;
                Render();
                ShowTutorialResult();
                return;
            }
            DealTutorial();
            Render();
            PreRoundCoach();
        }

        /// <summary>최종 점수를 구간별로 풀어서 보여 준다 (원작의 showScoreDemo).</summary>
        void ShowTutorialResult()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var len in Rules.RunLengths(myBoard.Cells)) parts.Add($"{len}연속 {Rules.ScoreTable[len]}점");
            resultTitle.text = "튜토리얼 완료";
            resultScore.fontSize = 22; // 구간이 많으면 길어진다
            againButton.gameObject.SetActive(false);
            resultScore.text = $"{string.Join(" + ", parts)}\n= 합계 {myBoard.Score}점";
            endPanel.SetActive(true);
            Sound.Play(Sfx.Win);
        }

        static int PoolCount(int v)
        {
            int n = 0;
            foreach (var c in Rules.Pool) if (c == v) n++;
            return n;
        }

        // ── 화면 만들기 (1920×1080 기준, 탑뷰 카드 테이블) ─────────────────
        //  왼쪽: 난이도·점수·캐릭터·모드   가운데: AI 보드 / 이번 카드 / 내 보드   오른쪽: 판 정보

        void Build()
        {
            var canvas = Ui.Canvas("StreamsCanvas");
            canvas.transform.SetParent(transform, false);
            var bg = Ui.Picture("Background", canvas.transform, "bg", Color.white);
            if (bg.sprite == null) bg.color = Ui.Bg;
            Ui.Fill(bg.rectTransform);

            BuildLeft(canvas.transform);
            BuildRight(canvas.transform);
            BuildCenter(canvas.transform);
            BuildStartPanel(canvas.transform);
            BuildEndPanel(canvas.transform);
            tutView = new TutorialView(canvas.transform);
        }

        void BuildLeft(Transform parent)
        {
            var side = Panel("Left", parent);
            Side(side.rectTransform, 0f, 24, 464);
            var col = Column(side.rectTransform, 16, 20, TextAnchor.UpperCenter);

            var banner = Ui.Picture("Banner", col, "panel", Ui.Gold);
            Ui.Size(banner, 0, 110);
            var bc = Column(banner.rectTransform, 2, 10);
            levelText = Ui.PixelLabel("Level", bc, "LEVEL -", 48, Ui.Ink);
            levelNameText = Ui.Label("LevelName", bc, "-", 22, Ui.CardInk);

            // 점수: 나(파랑) vs AI(빨강)
            var score = Ui.Rect("Score", col);
            Ui.Size(score, 0, 150);
            var sr = Ui.Layout<HorizontalLayoutGroup>(score, 12);
            sr.childForceExpandWidth = sr.childForceExpandHeight = true;
            myScoreText = ScoreBox(score, "YOU", Ui.Blue);
            aiScoreText = ScoreBox(score, "AI", Ui.Red);

            // 캐릭터
            var chars = Ui.Rect("Characters", col);
            Ui.Size(chars, 0, 270);
            var cr = Ui.Layout<HorizontalLayoutGroup>(chars, 12);
            cr.childForceExpandWidth = cr.childForceExpandHeight = true;
            Character(chars, "Player", "player", "YOU", Ui.Blue);
            agentSubText = Character(chars, "Agent", "agent", "AGENT", Ui.Red);

            // 모드 — 멀티플레이(5단계)가 들어오면 방 코드·상대 정보가 여기에 붙는다
            var mode = Ui.Picture("Mode", col, "panel", new Color(0.3f, 0.34f, 0.42f));
            Ui.Size(mode, 0, 80);
            var mc = Column(mode.rectTransform, 0, 8);
            Ui.PixelLabel("Label", mc, "MODE", 16, Ui.Muted);
            Ui.PixelLabel("Value", mc, "VS AI", 32, Ui.Ink);

            var levels = Styled(Ui.Button("Levels", col, "난이도 바꾸기", 22, () =>
            {
                tutView.Hide();
                endPanel.SetActive(false);
                startPanel.SetActive(true);
            }), Ui.Red);
            Ui.Size(levels, 0, 64);

            // 소리: 켜고 끄기 + 전체 볼륨 (배경음악·효과음 모두)
            var sound = Ui.Picture("Sound", col, "panel", new Color(0.3f, 0.34f, 0.42f));
            Ui.Size(sound, 0, 72);
            // 난이도 창·튜토리얼 말풍선이 화면을 덮어도 소리는 언제든 만질 수 있게 그 위에 그린다
            var soundLayer = sound.gameObject.AddComponent<Canvas>();
            soundLayer.overrideSorting = true;
            soundLayer.sortingOrder = 10;
            sound.gameObject.AddComponent<GraphicRaycaster>();
            var sr2 = Ui.Fill(Ui.Rect("Row", sound.transform));
            sr2.offsetMin = new Vector2(20, 0);
            sr2.offsetMax = new Vector2(-20, 0);
            Ui.Layout<HorizontalLayoutGroup>(sr2, 14);
            Ui.Size(Ui.PixelLabel("Label", sr2, "SOUND", 16, Ui.Muted), 64, 40);
            Button toggle = null;
            toggle = Styled(Ui.Button("Toggle", sr2, "", 22, () =>
            {
                Sound.Muted = !Sound.Muted;
                RenderSoundToggle(toggle);
            }), Ui.Blue);
            Ui.Size(toggle, 80, 40);
            RenderSoundToggle(toggle);
            Ui.Size(Ui.Slider("Volume", sr2, Sound.Volume, Ui.Gold, v => Sound.Volume = v), 190, 16);
        }

        static void RenderSoundToggle(Button b)
        {
            b.GetComponentInChildren<Text>().text = Sound.Muted ? "OFF" : "ON";
            b.image.color = Sound.Muted ? new Color(0.45f, 0.47f, 0.52f) : Ui.Blue;
        }

        void BuildRight(Transform parent)
        {
            var side = Panel("Right", parent);
            Side(side.rectTransform, 1f, 24, 384);
            var col = Column(side.rectTransform, 14, 20, TextAnchor.UpperCenter);

            // 턴·덱
            var top = Ui.Rect("TurnDeck", col);
            Ui.Size(top, 0, 120);
            var tr = Ui.Layout<HorizontalLayoutGroup>(top, 16);
            tr.childForceExpandWidth = tr.childForceExpandHeight = true;
            var turnPanel = Ui.Picture("Turn", top, "panel", Ui.Blue);
            Ui.Size(turnPanel, 150, 0);
            var turnBox = Column(turnPanel.rectTransform, 0, 10);
            Ui.PixelLabel("Label", turnBox, "TURN", 16, Ui.Ink);
            turnText = Ui.PixelLabel("Value", turnBox, "1/20", 48, Ui.Ink);
            var deckBox = Ui.Rect("Deck", top);
            Ui.Layout<HorizontalLayoutGroup>(deckBox, 10);
            var count = Ui.Rect("Count", deckBox);
            Ui.Size(count, 80, 96);
            var dc = Column(count, 0, 0);
            Ui.PixelLabel("Label", dc, "DECK", 16, Ui.Muted);
            deckText = Ui.PixelLabel("Value", dc, "40", 48, Ui.Ink);

            // 남은 카드 1~30 (풀 40장 - 이번 판에 나온 카드)
            Ui.Label("TrackTitle", col, "남은 카드", 22, Ui.Muted);
            var grid = Ui.Rect("Tracker", col);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(56, 50);
            g.spacing = new Vector2(6, 6);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 5;
            g.childAlignment = TextAnchor.UpperCenter;
            Ui.Size(grid, 0, 6 * 50 + 5 * 6);
            for (int v = Rules.MinCard; v <= Rules.MaxCard; v++)
            {
                trackCells[v] = Ui.Picture($"Card{v}", grid, "card", Color.white);
                var n = Ui.PixelLabel("Value", trackCells[v].transform, v.ToString(), 32, BoardView.CardColor(v));
                Ui.Fill(n.rectTransform);
                trackCounts[v] = Ui.PixelLabel("Count", trackCells[v].transform, "", 16, Ui.CardInk, TextAnchor.LowerRight);
                Ui.Fill(trackCounts[v].rectTransform).offsetMax = new Vector2(-6, 0);
                trackCounts[v].rectTransform.offsetMin = new Vector2(0, 4);
            }

            // 내 구간 (점수 = 구간 길이별 점수 합)
            Ui.Label("RunsTitle", col, "내 구간", 22, Ui.Muted);
            runsText = Ui.PixelLabel("Runs", col, "-", 32, Ui.Ink);
            Ui.Size(runsText, 0, 80);

            // 점수표: 구간 길이 → 점수 (2연속부터)
            Ui.Label("TableTitle", col, "점수표", 22, Ui.Muted);
            scoreTable = Ui.Rect("ScoreTable", col);
            var st = scoreTable.gameObject.AddComponent<GridLayoutGroup>();
            st.cellSize = new Vector2(72, 30);
            st.spacing = new Vector2(6, 4);
            st.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            st.constraintCount = 4;
            st.childAlignment = TextAnchor.UpperCenter;
            Ui.Size(scoreTable, 0, 5 * 30 + 4 * 4);
            for (int len = 2; len <= Rules.Slots; len++)
            {
                int pts = Rules.ScoreTable[len];
                var cell = Ui.Picture($"Len{len}", scoreTable, "panel", new Color(0.3f, 0.34f, 0.42f));
                var t = Ui.PixelLabel("Value", cell.transform, $"{len}:{pts}", 16, pts >= 100 ? Ui.Gold : Ui.Ink);
                Ui.Fill(t.rectTransform);
            }
        }

        void BuildCenter(Transform parent)
        {
            // 가운데 영역: 양쪽 패널 사이
            var area = Ui.Fill(Ui.Rect("Center", parent));
            area.offsetMin = new Vector2(488, 24);
            area.offsetMax = new Vector2(-408, -24);

            var ai = Row(area, "AiRow", 1f, -110, "AGENT", Ui.Red);
            aiView = new BoardView("AiBoard", ai, null);

            var mid = Ui.Rect("Current", area);
            mid.anchorMin = mid.anchorMax = mid.pivot = new Vector2(0.5f, 0.5f);
            mid.sizeDelta = new Vector2(600, 380);
            var mc = Column(mid, 14, 0);
            Ui.PixelLabel("Label", mc, "CARD", 32, Ui.Ink);
            cardImage = Ui.Picture("Card", mc, "card", Color.white);
            cardImage.preserveAspect = true;
            Ui.Size(cardImage, 144, 192);
            cardText = Ui.PixelLabel("Value", cardImage.transform, "", 96, Ui.CardInk);
            Ui.Fill(cardText.rectTransform);
            hintText = Ui.Label("Hint", mc, "난이도를 골라 시작하세요", 22, Ui.Ink);

            myRow = Row(area, "MyRow", 0f, 110, "YOU", Ui.Blue);
            myView = new BoardView("MyBoard", myRow, Place);
        }

        // ── 작은 조립 도우미 ───────────────────────────────────────

        static Image Panel(string name, Transform parent) => Ui.Picture(name, parent, "panel", Ui.Panel);

        static Button Styled(Button b, Color color)
        {
            b.image.sprite = Ui.Art("panel");
            if (b.image.sprite != null) b.image.type = Image.Type.Sliced;
            b.image.color = color;
            return b;
        }

        /// <summary>화면 왼쪽(x=0) 또는 오른쪽(x=1) 에 붙는 세로 패널.</summary>
        static void Side(RectTransform rt, float x, float margin, float width)
        {
            rt.anchorMin = new Vector2(x, 0);
            rt.anchorMax = new Vector2(x, 1);
            rt.pivot = new Vector2(x, 0.5f);
            rt.anchoredPosition = new Vector2(x == 0 ? margin : -margin, 0);
            rt.sizeDelta = new Vector2(width - margin, -2 * margin);
        }

        /// <summary>부모를 꽉 채우는 세로 줄 (자식 폭을 부모에 맞춤).</summary>
        static RectTransform Column(RectTransform parent, float spacing, int padding, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var col = Ui.Fill(Ui.Rect("Column", parent));
            var v = Ui.Layout<VerticalLayoutGroup>(col, spacing, align);
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childForceExpandWidth = true;
            return col;
        }

        /// <summary>가운데 영역 위(y=1)·아래(y=0)의 보드 한 줄: 이름표 + 1×20 카드.</summary>
        static RectTransform Row(RectTransform area, string name, float y, float offset, string label, Color color)
        {
            var row = Ui.Rect(name, area);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, y);
            row.anchoredPosition = new Vector2(0, offset);
            row.sizeDelta = new Vector2(1024, 112);
            Ui.Layout<VerticalLayoutGroup>(row, 8);
            var tag = Ui.Picture("Tag", row, "panel", color);
            Ui.Size(tag, 180, 40);
            var t = Ui.PixelLabel("Label", tag.transform, label, 32, Ui.Ink);
            Ui.Fill(t.rectTransform);
            return row;
        }

        static Text ScoreBox(Transform parent, string label, Color color)
        {
            var box = Ui.Picture(label, parent, "panel", color);
            var col = Column(box.rectTransform, 0, 12);
            Ui.PixelLabel("Label", col, label, 32, Ui.Ink);
            return Ui.PixelLabel("Value", col, "0", 64, Ui.Ink);
        }

        /// <summary>캐릭터 초상 + 이름. 아래 작은 줄(Text)을 돌려준다.</summary>
        static Text Character(Transform parent, string name, string art, string label, Color color)
        {
            var box = Ui.Picture(name, parent, "panel", new Color(0.35f, 0.4f, 0.5f));
            var col = Column(box.rectTransform, 4, 10);
            var portrait = Ui.Picture("Portrait", col, art, Color.white);
            portrait.preserveAspect = true;
            Ui.Size(portrait, 160, 160);
            Ui.PixelLabel("Name", col, label, 32, color);
            return Ui.Label("Sub", col, label == "YOU" ? "플레이어" : "AI", 22, Ui.Muted);
        }

        void BuildStartPanel(Transform parent)
        {
            var dim = Ui.Box("StartPanel", parent, new Color(0, 0, 0, 0.7f));
            Ui.Fill(dim.rectTransform);
            startPanel = dim.gameObject;
            var box = Panel("Box", dim.transform);
            box.rectTransform.sizeDelta = new Vector2(640, 680);
            var col = Column(box.rectTransform, 14, 36);
            Ui.Label("Title", col, "The STREAMS +", 66, Ui.Gold);
            for (int i = 0; i < Levels.Length; i++)
            {
                int lv = i + 1;
                var b = Styled(Ui.Button($"Level{lv}", col, Levels[i], 22, () => StartGame(lv)),
                    Color.Lerp(Ui.Blue, Ui.Red, i / 4f));
                Ui.Size(b, 0, 64);
            }
            Ui.Size(Styled(Ui.Button("Tutorial", col, "튜토리얼", 22, StartTutorial), Ui.Gold), 0, 64);
            startError = Ui.Label("Error", col, "", 22, Ui.Red);
            startError.gameObject.SetActive(false);
        }

        void BuildEndPanel(Transform parent)
        {
            var dim = Ui.Box("EndPanel", parent, new Color(0, 0, 0, 0.7f));
            Ui.Fill(dim.rectTransform);
            endPanel = dim.gameObject;
            var box = Panel("Box", dim.transform);
            box.rectTransform.sizeDelta = new Vector2(640, 460);
            var col = Column(box.rectTransform, 16, 36);
            resultTitle = Ui.Label("Title", col, "", 66, Ui.Gold);
            resultScore = Ui.Label("Score", col, "", 33, Ui.Ink);
            resultScore.horizontalOverflow = HorizontalWrapMode.Wrap;
            againButton = Styled(Ui.Button("Again", col, "한 판 더", 22, () => StartGame(level)), Ui.Blue);
            Ui.Size(againButton, 0, 64);
            Ui.Size(Styled(Ui.Button("Levels", col, "난이도 바꾸기", 22, () =>
            {
                endPanel.SetActive(false);
                startPanel.SetActive(true);
            }), Ui.Red), 0, 64);
            endPanel.SetActive(false);
        }
    }

    static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<GameController>() != null) return;
            var go = new GameObject("STREAMS");
            go.AddComponent<GameController>();
        }
    }
}
