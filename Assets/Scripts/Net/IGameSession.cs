// 화면(Presentation)과 서버 사이의 이음새.
// UI 는 이 이벤트만 안다 — AI 대전(AiSession, HTTP)이든 멀티 방(RoomSession, 웹소켓, 5단계)이든 같은 UI 가 돈다.
using System;
using UnityEngine;

namespace Streams.Net
{
    public static class Seats
    {
        public const string Me = "me";
        public const string Ai = "ai";
    }

    public readonly struct SessionOptions
    {
        public readonly int Level; // 1~5

        public SessionOptions(int level) => Level = level;
    }

    public readonly struct SessionInfo
    {
        public readonly string GameId;
        public readonly int Level;
        public readonly string LevelName;

        public SessionInfo(string gameId, int level, string levelName)
        {
            GameId = gameId;
            Level = level;
            LevelName = levelName;
        }
    }

    public readonly struct GameResult
    {
        public readonly int MyScore;
        public readonly int AiScore;

        public GameResult(int myScore, int aiScore)
        {
            MyScore = myScore;
            AiScore = aiScore;
        }
    }

    public interface IGameSession
    {
        /// <summary>판이 시작됨.</summary>
        event Action<SessionInfo> Started;

        /// <summary>이번에 놓을 카드. turn = 지금까지 놓은 카드 수 (0~19).</summary>
        event Action<int, int> CardDealt; // (card, turn)

        /// <summary>다른 자리(AI·상대)가 카드를 놓음. 내 수는 UI 가 먼저 그리므로 오지 않는다.</summary>
        event Action<string, int, int> Placed; // (seat, slot, card)

        /// <summary>서버가 계산한 점수. 화면에는 항상 이 값을 쓴다.</summary>
        event Action<int, int> ScoresChanged; // (myScore, aiScore)

        event Action<GameResult> Finished;

        Awaitable StartAsync(SessionOptions options);

        /// <summary>이번 카드를 slot 에 놓는다. thinkMs = 카드를 보여 준 순간부터 걸린 시간.
        /// 서버가 거절하면 StreamsApiException — UI 는 놓은 카드를 되돌린다.</summary>
        Awaitable PlaceAsync(int slot, int thinkMs);
    }
}
