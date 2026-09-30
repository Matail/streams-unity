// 20칸 보드 한 줄 (1×20 카드). 내 보드는 누를 수 있고, AI 보드는 보기만.
using System;
using Streams.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Streams.Presentation
{
    sealed class BoardView
    {
        public const float CardW = 48, CardH = 64, Gap = 3; // 카드 스프라이트 원본 크기 (1배)

        readonly Image[] slots = new Image[Rules.Slots];
        readonly Text[] labels = new Text[Rules.Slots];
        readonly Button[] buttons;
        int highlighted = -1;

        /// <param name="onLeave">커서가 칸을 떠날 때 (칸, 머문 ms). 내 보드만 — 망설임 기록용.</param>
        public BoardView(string name, Transform parent, Action<int> onClick, Action<int, int> onLeave = null)
        {
            var root = Ui.Rect(name, parent);
            Ui.Layout<HorizontalLayoutGroup>(root, Gap);
            Ui.Size(root, Rules.Slots * CardW + (Rules.Slots - 1) * Gap, CardH);

            if (onClick != null) buttons = new Button[Rules.Slots];
            for (int i = 0; i < Rules.Slots; i++)
            {
                int slot = i;
                slots[i] = Ui.Picture($"Slot{i}", root, "card", Ui.EmptySlot);
                Ui.Size(slots[i], CardW, CardH);
                labels[i] = Ui.PixelLabel("Label", slots[i].transform, "", 32, Ui.CardInk);
                Ui.Fill(labels[i].rectTransform);
                if (onClick != null)
                {
                    buttons[i] = slots[i].gameObject.AddComponent<Button>();
                    var colors = buttons[i].colors;
                    colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f);
                    colors.disabledColor = Color.white;
                    buttons[i].colors = colors;
                    buttons[i].onClick.AddListener(() => onClick(slot));
                }
                if (onLeave != null)
                {
                    var hover = slots[i].gameObject.AddComponent<SlotHover>();
                    hover.Left = ms => onLeave(slot, ms);
                }
            }
        }

        /// <summary>커서가 칸 위에 머문 시간을 잰다. 터치 화면에선 hover 가 없어 부르지 않는다.</summary>
        sealed class SlotHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            public Action<int> Left;
            float enteredAt = -1;

            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => enteredAt = Time.unscaledTime;

            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
            {
                if (enteredAt < 0) return;
                Left?.Invoke(Mathf.RoundToInt((Time.unscaledTime - enteredAt) * 1000f));
                enteredAt = -1;
            }
        }

        public void Render(Board board, bool interactable)
        {
            for (int i = 0; i < Rules.Slots; i++)
            {
                int v = board[i];
                labels[i].text = v == 0 ? "" : v.ToString();
                labels[i].color = CardColor(v);
                slots[i].color = v == 0 ? Ui.EmptySlot : i == highlighted ? Ui.Gold : Color.white;
                if (buttons != null) buttons[i].interactable = interactable && v == 0;
            }
        }

        /// <summary>방금 놓인 칸 강조 (AI 의 마지막 수).</summary>
        public void Highlight(int slot) => highlighted = slot;

        /// <summary>숫자 색: 1~10 파랑, 11~20 검정, 21~30 빨강 — 카드 크기를 한눈에.</summary>
        public static Color CardColor(int v) => v <= 10 ? Ui.Blue : v <= 20 ? Ui.CardInk : Ui.Red;
    }
}
