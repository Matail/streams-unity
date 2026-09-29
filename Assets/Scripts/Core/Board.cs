using System;
using System.Collections.Generic;

namespace Streams.Core
{
    /// <summary>한 사람(또는 AI)의 20칸 보드. 0 = 빈칸.</summary>
    public sealed class Board
    {
        readonly int[] cells = new int[Rules.Slots];

        public IReadOnlyList<int> Cells => cells;
        public int this[int slot] => cells[slot];
        public int Score => Rules.Score(cells);

        public bool IsEmpty(int slot) => Rules.IsEmptySlot(cells, slot);

        public void Place(int slot, int card)
        {
            if (!IsEmpty(slot)) throw new InvalidOperationException($"slot {slot} is not empty");
            if (card < Rules.MinCard || card > Rules.MaxCard) throw new ArgumentOutOfRangeException(nameof(card));
            cells[slot] = card;
        }

        /// <summary>서버가 거절한 수를 되돌릴 때.</summary>
        public void Clear(int slot) => cells[slot] = 0;

        public void Reset() => Array.Clear(cells, 0, cells.Length);

        /// <summary>이 칸에 놓으면 점수가 몇이 되는지 (미리보기).</summary>
        public int ScoreIfPlaced(int slot, int card)
        {
            var copy = (int[])cells.Clone();
            copy[slot] = card;
            return Rules.Score(copy);
        }
    }
}
