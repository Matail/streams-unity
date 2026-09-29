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
        static readonly (int level, string name, int avg)[] Levels =
        {
            (1, "입문", 22), (2, "보통", 33), (3, "숙련", 39), (4, "고수", 47), (5, "마스터", 49),
        };

        IGameSession session;
        readonly Board myBoard = new Board();
        readonly Board aiBoard = new Board();
        int card, turn, level;
        string levelName = "-";
        bool busy, playing;
        float cardShownAt;

        BoardView myView, aiView;
        Text turnText, myScoreText, aiScoreText, levelText, cardText, hintText, resultTitle, resultScore, startError;
        GameObject startPanel, endPanel;

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
                cardShownAt = Time.realtimeSinceStartup;
            };
            session.Placed += (seat, slot, c) =>
            {
                if (seat != Seats.Ai) return;
                aiBoard.Place(slot, c);
                aiView.Highlight(slot);
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
                resultScore.text = $"나 {r.MyScore}점 · AI({levelName}) {r.AiScore}점";
                endPanel.SetActive(true);
            };
            Build();
            Render();
        }

        async void StartGame(int lv)
        {
            if (busy) return;
            busy = true;
            startError.gameObject.SetActive(false);
            try
            {
                myBoard.Reset();
                aiBoard.Reset();
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
            if (busy || !playing || !myBoard.IsEmpty(slot)) return;
            busy = true;
            int placed = card;
            int thinkMs = Mathf.RoundToInt((Time.realtimeSinceStartup - cardShownAt) * 1000f);
            myBoard.Place(slot, placed); // 먼저 그린다 — 서버가 거절하면 되돌림
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
            cardText.text = card > 0 && playing ? card.ToString() : "–";
            turnText.text = $"{Mathf.Min(turn + 1, Rules.Slots)} / {Rules.Slots}";
            levelText.text = levelName;
        }

        // ── 화면 만들기 ────────────────────────────────────────────

        void Build()
        {
            var canvas = Ui.Canvas("StreamsCanvas");
            canvas.transform.SetParent(transform, false);
            var bg = Ui.Box("Background", canvas.transform, Ui.Bg);
            Ui.Fill(bg.rectTransform);

            var table = Ui.Fill(Ui.Rect("Table", canvas.transform));
            var col = Ui.Layout<VerticalLayoutGroup>(table, 18);
            col.padding = new RectOffset(24, 24, 24, 24);

            // HUD
            var hud = Ui.Rect("Hud", table);
            Ui.Layout<HorizontalLayoutGroup>(hud, 48);
            turnText = Stat(hud, "TURN");
            myScoreText = Stat(hud, "YOU");
            aiScoreText = Stat(hud, "AI");
            levelText = Stat(hud, "LEVEL");

            // 이번 카드
            Ui.Label("CardLabel", table, "이번 카드", 20, Ui.Muted);
            var cardBox = Ui.Box("Card", table, Ui.Accent);
            Ui.Size(cardBox, 110, 150);
            cardText = Ui.Label("Value", cardBox.transform, "–", 64, Ui.Bg);
            Ui.Fill(cardText.rectTransform);

            hintText = Ui.Label("Hint", table, "난이도를 골라 시작하세요", 20, Ui.Muted);

            Ui.Label("MyLabel", table, "내 보드", 18, Ui.Muted);
            myView = new BoardView("MyBoard", table, 62, Place);
            Ui.Label("AiLabel", table, "AI 보드", 18, Ui.Muted);
            aiView = new BoardView("AiBoard", table, 40, null);

            BuildStartPanel(canvas.transform);
            BuildEndPanel(canvas.transform);
        }

        Text Stat(Transform parent, string label)
        {
            var box = Ui.Rect(label, parent);
            Ui.Layout<VerticalLayoutGroup>(box, 2);
            Ui.Label("Label", box, label, 16, Ui.Muted);
            return Ui.Label("Value", box, "-", 30, Ui.Ink);
        }

        void BuildStartPanel(Transform parent)
        {
            var dim = Ui.Box("StartPanel", parent, new Color(0, 0, 0, 0.75f));
            Ui.Fill(dim.rectTransform);
            startPanel = dim.gameObject;
            var col = Ui.Fill(Ui.Rect("Column", dim.transform));
            Ui.Layout<VerticalLayoutGroup>(col, 16);
            Ui.Label("Title", col, "STREAMS", 56, Ui.Accent);
            Ui.Label("Sub", col, "같은 카드를 받는 AI와 겨뤄요. 난이도를 고르세요.", 20, Ui.Muted);
            foreach (var (lv, name, avg) in Levels)
            {
                var b = Ui.Button($"Level{lv}", col, $"{name}   ·   AI 평균 {avg}점", 22, () => StartGame(lv));
                Ui.Size(b, 360, 56);
            }
            startError = Ui.Label("Error", col, "", 18, new Color32(0xF5, 0x7A, 0x57, 0xFF));
            startError.gameObject.SetActive(false);
        }

        void BuildEndPanel(Transform parent)
        {
            var dim = Ui.Box("EndPanel", parent, new Color(0, 0, 0, 0.75f));
            Ui.Fill(dim.rectTransform);
            endPanel = dim.gameObject;
            var col = Ui.Fill(Ui.Rect("Column", dim.transform));
            Ui.Layout<VerticalLayoutGroup>(col, 16);
            resultTitle = Ui.Label("Title", col, "", 56, Ui.Accent);
            resultScore = Ui.Label("Score", col, "", 24, Ui.Ink);
            Ui.Size(Ui.Button("Again", col, "한 판 더", 22, () => StartGame(level)), 280, 56);
            Ui.Size(Ui.Button("Levels", col, "난이도 바꾸기", 22, () =>
            {
                endPanel.SetActive(false);
                startPanel.SetActive(true);
            }), 280, 56);
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
