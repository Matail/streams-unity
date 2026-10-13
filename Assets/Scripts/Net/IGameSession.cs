// 화면(Presentation)과 서버 사이의 이음새.
// UI 는 이 이벤트만 안다 — AI 대전(AiSession, HTTP)이든 멀티 방(RoomSession, 웹소켓, 5단계)이든 같은 UI 가 돈다.
// 자리는 번호로 부른다: 멀티 방은 0~7 (방 안에서 바뀌지 않음), AI 대전은 나 0 · AI 1.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Streams.Net
{
    public readonly struct SessionOptions
    {
        public readonly int Level; // 1~5

        public SessionOptions(int level) => Level = level;
    }

    public readonly struct PlayerInfo
    {
        public readonly int Seat;
        public readonly string Name;
        public readonly bool IsAi;

        public PlayerInfo(int seat, string name, bool isAi)
        {
            Seat = seat;
            Name = name;
            IsAi = isAi;
        }
    }

    public readonly struct SessionInfo
    {
        public readonly string GameId;
        public readonly int Level;
        public readonly string LevelName;
        public readonly int MySeat;
        public readonly IReadOnlyList<PlayerInfo> Players; // 나 포함, 자리 번호 순

        public SessionInfo(string gameId, int level, string levelName, int mySeat, IReadOnlyList<PlayerInfo> players)
        {
            GameId = gameId;
            Level = level;
            LevelName = levelName;
            MySeat = mySeat;
            Players = players;
        }
    }

    public readonly struct Standing
    {
        public readonly int Seat;
        public readonly int Score;
        public readonly int Rank; // 1부터, 같은 점수는 같은 등수 (1, 1, 3)

        public Standing(int seat, int score, int rank)
        {
            Seat = seat;
            Score = score;
            Rank = rank;
        }
    }

    public readonly struct GameResult
    {
        public readonly IReadOnlyList<Standing> Standings; // 자리 번호 순
        public readonly int MySeat;

        public GameResult(IReadOnlyList<Standing> standings, int mySeat)
        {
            Standings = standings;
            MySeat = mySeat;
        }

        public Standing Mine
        {
            get
            {
                foreach (var s in Standings) if (s.Seat == MySeat) return s;
                throw new InvalidOperationException("my seat is not in the result");
            }
        }

        /// <summary>자리별 점수로 등수를 매긴다.</summary>
        public static GameResult FromScores(IReadOnlyList<(int seat, int score)> scores, int mySeat)
        {
            var standings = new Standing[scores.Count];
            for (int i = 0; i < scores.Count; i++)
            {
                int rank = 1;
                foreach (var other in scores) if (other.score > scores[i].score) rank++;
                standings[i] = new Standing(scores[i].seat, scores[i].score, rank);
            }
            return new GameResult(standings, mySeat);
        }
    }

    public interface IGameSession
    {
        /// <summary>판이 시작됨.</summary>
        event Action<SessionInfo> Started;

        /// <summary>이번에 놓을 카드. turn = 지금까지 놓은 카드 수 (0~19).</summary>
        event Action<int, int> CardDealt; // (card, turn)

        /// <summary>다른 자리(AI·상대)가 카드를 놓음. 내 수는 UI 가 먼저 그리므로 오지 않는다.</summary>
        event Action<int, int, int> Placed; // (seat, slot, card)

        /// <summary>서버가 계산한 자리 하나의 점수. 화면에는 항상 이 값을 쓴다.</summary>
        event Action<int, int> ScoreChanged; // (seat, score)

        event Action<GameResult> Finished;

        Awaitable StartAsync(SessionOptions options);

        /// <summary>이번 카드를 slot 에 놓는다. thinkMs = 카드를 보여 준 순간부터 걸린 시간.
        /// 서버가 거절하면 StreamsApiException — UI 는 놓은 카드를 되돌린다.</summary>
        Awaitable PlaceAsync(int slot, int thinkMs);
    }
}
