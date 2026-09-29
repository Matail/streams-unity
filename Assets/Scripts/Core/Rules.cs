// STREAMS 규칙. 서버 원본은 Matail/my-site 의 worker/streams/game.ts — 이 파일은 그걸 옮긴 것.
// 점수는 항상 서버 값을 화면에 쓴다. 여기 계산은 미리보기(놓기 전 점수 변화 표시 등)용.
// 정답지: Assets/Tests/EditMode/Data/rules.json (my-site 의 worker/streams/testdata/rules.json 복사본)
using System.Collections.Generic;

namespace Streams.Core
{
    public static class Rules
    {
        public const int Slots = 20;
        public const int MinCard = 1;
        public const int MaxCard = 30;

        /// <summary>구간 길이 → 점수. 인덱스가 길이 (0~20).</summary>
        public static readonly IReadOnlyList<int> ScoreTable = new[]
        {
            0, 0, 1, 3, 5, 7, 9, 11, 15, 20, 25, 30, 35, 40, 50, 60, 70, 85, 100, 150, 300,
        };

        /// <summary>40장 풀: 1~10 한 장, 11~20 두 장, 21~30 한 장씩.</summary>
        public static readonly IReadOnlyList<int> Pool = BuildPool();

        static int[] BuildPool()
        {
            var p = new List<int>(40);
            for (int v = MinCard; v <= MaxCard; v++)
            {
                p.Add(v);
                if (v >= 11 && v <= 20) p.Add(v);
            }
            return p.ToArray();
        }

        /// <summary>빈칸(0)은 건너뛰고 왼쪽부터 읽어, 작거나 같은 숫자로 이어지는 구간들의 길이.</summary>
        public static List<int> RunLengths(IReadOnlyList<int> board)
        {
            var runs = new List<int>();
            int prev = 0, len = 0;
            for (int i = 0; i < board.Count; i++)
            {
                int v = board[i];
                if (v == 0) continue;
                if (len > 0 && prev <= v) len++;
                else
                {
                    if (len > 0) runs.Add(len);
                    len = 1;
                }
                prev = v;
            }
            if (len > 0) runs.Add(len);
            return runs;
        }

        public static int Score(IReadOnlyList<int> board)
        {
            int s = 0;
            foreach (var r in RunLengths(board)) s += ScoreTable[r];
            return s;
        }

        public static bool IsEmptySlot(IReadOnlyList<int> board, int slot) =>
            slot >= 0 && slot < board.Count && board[slot] == 0;
    }
}
