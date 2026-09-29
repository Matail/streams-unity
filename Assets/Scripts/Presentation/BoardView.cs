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

        public BoardView(string name, Transform parent, Action<int> onClick)
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
