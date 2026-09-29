// 튜토리얼: 서버 없이 고정된 한 판을 두며 상황에 맞는 코칭을 받는다.
// 흐름·덱·문구는 웹 원작(github.com/Koojunyeong/streams game.html)을 옮긴 것.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Streams.Presentation
{
    static class Tutorial
    {
        /// <summary>모든 플레이어가 같은 판을 배우도록 고정된 20장.</summary>
        public static readonly int[] Deck = { 15, 8, 22, 15, 3, 27, 12, 19, 6, 24, 11, 16, 4, 29, 17, 9, 20, 13, 7, 25 };

        public static readonly (string title, string body)[] Onboarding =
        {
            ("현재 카드", "화면 가운데 숫자가 이번 턴에 배치할 카드입니다. 플레이어와 AI는 같은 카드를 동시에 받습니다."),
            ("보드 위 20칸", "왼쪽에서 오른쪽으로 갈수록 숫자가 커지게 만드는 것이 목표입니다. 한 번 배치한 카드는 다시 뺄 수 없습니다."),
            ("점수표", "연속된 오름차순 줄이 길수록 점수가 커집니다. 지금은 한 줄을 길게 만드는 감각을 익히는 연습입니다."),
        };

        public enum Tip { Center, Gaps, Same, Range }

        public static (string title, string body) Coach(Tip tip) => tip switch
        {
            Tip.Center => ("가운데 근처에 두면 시작이 편해집니다",
                "첫 카드는 보드 정중앙 근처에 두는 편이 좋습니다. 작은 수와 큰 수가 들어올 자리를 양쪽에 남겨둘 수 있기 때문입니다."),
            Tip.Gaps => ("초반에는 빈칸을 남겨두세요",
                "카드를 바로 옆 칸에 촘촘히 붙이면 중간 숫자가 들어올 자리가 사라집니다. 11~20대 숫자를 끼워 넣을 공간을 남겨두는 것이 안전합니다."),
            Tip.Same => ("같은 숫자도 흐름이 이어집니다",
                "11~20 숫자는 중복으로 들어 있습니다. 같은 숫자가 다시 나와도 오름차순 줄은 끊기지 않으니, 자연스럽게 같은 흐름 옆에 두어도 됩니다."),
            _ => ("왼쪽보다 커야 하고, 오른쪽보다 작아야 합니다",
                "이 칸이 안전하려면 왼쪽 기준값보다 커야 하고 오른쪽 기준값보다 작아야 합니다. 범위를 벗어나면 스트림이 즉시 끊어집니다."),
        };
    }

    /// <summary>튜토리얼 말풍선. 온보딩은 대상 주변만 밝게 비추고(스포트라이트), 코칭은 말풍선만 띄운다.</summary>
    sealed class TutorialView
    {
        readonly RectTransform root, bubble;
        readonly Image[] dims = new Image[4]; // 위·아래·왼쪽·오른쪽 — 가운데 구멍이 스포트라이트
        readonly Text kicker, title, body, step, nextLabel;
        readonly GameObject skip;
        Action onNext, onSkip;

        /// <summary>말풍선이 떠 있는 동안은 카드를 놓을 수 없다.</summary>
        public bool Showing => root.gameObject.activeSelf;

        public TutorialView(Transform canvas)
        {
            root = Ui.Fill(Ui.Rect("Tutorial", canvas));
            for (int i = 0; i < dims.Length; i++) dims[i] = Ui.Box("Dim", root, new Color(0.02f, 0.03f, 0.06f, 0.78f));

            bubble = Ui.Picture("Bubble", root, "panel", Ui.Panel).rectTransform;
            bubble.sizeDelta = new Vector2(780, 300);
            var col = Ui.Fill(Ui.Rect("Column", bubble));
            var v = Ui.Layout<VerticalLayoutGroup>(col, 10, TextAnchor.UpperLeft);
            v.padding = new RectOffset(32, 32, 28, 24);
            v.childForceExpandWidth = true;
            kicker = Ui.PixelLabel("Kicker", col, "", 16, Ui.Gold, TextAnchor.MiddleLeft);
            title = Ui.Label("Title", col, "", 33, Ui.Ink, TextAnchor.MiddleLeft);
            body = Ui.Label("Body", col, "", 22, Ui.Ink, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.2f;
            Ui.Size(body, 0, 110);

            var actions = Ui.Rect("Actions", col);
            Ui.Size(actions, 0, 52);
            Ui.Layout<HorizontalLayoutGroup>(actions, 12, TextAnchor.MiddleRight);
            step = Ui.PixelLabel("Step", actions, "", 16, Ui.Muted, TextAnchor.MiddleLeft);
            Ui.Size(step, 360, 52);
            var s = Ui.Button("Skip", actions, "건너뛰기", 22, () => onSkip?.Invoke());
            Ui.Size(s, 130, 52);
            skip = s.gameObject;
            var n = Ui.Button("Next", actions, "", 22, () => onNext?.Invoke());
            n.image.sprite = Ui.Art("panel");
            if (n.image.sprite != null) n.image.type = Image.Type.Sliced;
            n.image.color = Ui.Blue;
            Ui.Size(n, 150, 52);
            nextLabel = n.GetComponentInChildren<Text>();

            root.gameObject.SetActive(false);
        }

        /// <summary>온보딩 한 단계: target 만 밝게 두고 그 아래(모자라면 위)에 말풍선.</summary>
        public void Spotlight(RectTransform target, int index, int count, string t, string b, Action next, Action skipAll)
        {
            Canvas.ForceUpdateCanvases();
            var (min, max) = Normalized(target, 12);
            SetDim(0, new Vector2(0, max.y), Vector2.one);
            SetDim(1, Vector2.zero, new Vector2(1, min.y));
            SetDim(2, new Vector2(0, min.y), new Vector2(min.x, max.y));
            SetDim(3, new Vector2(max.x, min.y), new Vector2(1, max.y));

            bool below = min.y > 0.4f;
            float cx = Mathf.Clamp((min.x + max.x) / 2, 0.2f, 0.8f);
            Place(new Vector2(cx, below ? min.y : max.y), new Vector2(0.5f, below ? 1 : 0), new Vector2(0, below ? -16 : 16));
            Show($"STEP {index + 1} OF {count}", t, b, $"{index + 1} / {count}", index == count - 1 ? "시작하기" : "다음", true, next, skipAll);
        }

        /// <summary>코칭 말풍선 (어둡게 하지 않음). 닫으면 done.</summary>
        public void Coach(string t, string b, Action done)
        {
            foreach (var d in dims) d.gameObject.SetActive(false);
            // 튜토리얼엔 AI 보드가 비어 있으니 그 자리(위쪽)에 — 이번 카드와 내 보드를 가리지 않게
            Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40));
            Show("COACH", t, b, "", "확인", false, done, null);
        }

        public void Hide() => root.gameObject.SetActive(false);

        void Show(string k, string t, string b, string s, string nextText, bool canSkip, Action then, Action skipped)
        {
            kicker.text = k;
            title.text = t;
            body.text = b;
            step.text = s;
            nextLabel.text = nextText;
            skip.SetActive(canSkip);
            onNext = () => { Hide(); then?.Invoke(); };
            onSkip = () => { Hide(); skipped?.Invoke(); };
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

        void Place(Vector2 anchor, Vector2 pivot, Vector2 offset)
        {
            bubble.anchorMin = bubble.anchorMax = anchor;
            bubble.pivot = pivot;
            bubble.anchoredPosition = offset;
        }

        void SetDim(int i, Vector2 min, Vector2 max)
        {
            var rt = dims[i].rectTransform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            dims[i].gameObject.SetActive(true);
        }

        /// <summary>target 의 화면 영역을 캔버스 비율 좌표(0~1)로. pad 는 캔버스 픽셀.</summary>
        (Vector2 min, Vector2 max) Normalized(RectTransform target, float pad)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var size = root.rect.size;
            Vector2 a = (Vector2)root.InverseTransformPoint(corners[0]) - root.rect.min - Vector2.one * pad;
            Vector2 b = (Vector2)root.InverseTransformPoint(corners[2]) - root.rect.min + Vector2.one * pad;
            return (Vector2.Max(Vector2.zero, a / size), Vector2.Min(Vector2.one, b / size));
        }
    }
}
