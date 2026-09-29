// 20칸 보드 한 줄 (10칸 × 2줄). 내 보드는 누를 수 있고, AI 보드는 보기만.
using System;
using Streams.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Streams.Presentation
{
    sealed class BoardView
    {
        readonly Image[] slots = new Image[Rules.Slots];
        readonly Text[] labels = new Text[Rules.Slots];
        readonly Button[] buttons;
        int highlighted = -1;

        public BoardView(string name, Transform parent, float cell, Action<int> onClick)
        {
            var root = Ui.Rect(name, parent);
            var grid = root.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(6, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;
            grid.childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(root, 10 * cell + 9 * 6, 2 * cell + 6);

            if (onClick != null) buttons = new Button[Rules.Slots];
            for (int i = 0; i < Rules.Slots; i++)
            {
                int slot = i;
                if (onClick != null)
                {
                    buttons[i] = Ui.Button($"Slot{i}", root, "", Mathf.RoundToInt(cell * 0.42f), () => onClick(slot));
                    slots[i] = buttons[i].GetComponent<Image>();
                    labels[i] = buttons[i].GetComponentInChildren<Text>();
                }
                else
                {
                    slots[i] = Ui.Box($"Slot{i}", root, Ui.Slot);
                    labels[i] = Ui.Label("Label", slots[i].transform, "", Mathf.RoundToInt(cell * 0.42f), Ui.Ink);
                    Ui.Fill(labels[i].rectTransform);
                }
            }
        }

        public void Render(Board board, bool interactable)
        {
            for (int i = 0; i < Rules.Slots; i++)
            {
                int v = board[i];
                labels[i].text = v == 0 ? "" : v.ToString();
                slots[i].color = i == highlighted ? Ui.Accent : v == 0 ? Ui.Slot : Ui.SlotFilled;
                labels[i].color = i == highlighted ? Ui.Bg : Ui.Ink;
                if (buttons != null) buttons[i].interactable = interactable && v == 0;
            }
        }

        /// <summary>방금 놓인 칸 강조 (AI 의 마지막 수).</summary>
        public void Highlight(int slot) => highlighted = slot;
    }
}
