// /api/streams 요청·응답 모양. 계약: docs/streams-api.md (원본은 my-site 리포).
// JsonUtility 로 직렬화하므로 공개 필드 + [Serializable]. 응답에 없는 필드는 기본값(0/false/null).
using System;

namespace Streams.Net
{
    [Serializable]
    public class StartRequest
    {
        public int level;
        public string playerId;
    }

    [Serializable]
    public class StartResponse
    {
        public string gameId;
        public string token;
        public int level;
        public string levelName;
        public int turn;
        public int card;
    }

    [Serializable]
    public class MoveRequest
    {
        public string token;
        public int slot;
        public int thinkMs;
    }

    [Serializable]
    public class MoveResponse
    {
        public string token;
        public int aiSlot;
        public int turn;
        public int playerScore;
        public int aiScore;
        public int nextCard;   // finished 이면 0
        public bool finished;
    }

    [Serializable]
    public class ErrorResponse
    {
        public string error;
    }

    public sealed class StreamsApiException : Exception
    {
        /// <summary>HTTP 상태. 0 = 서버에 닿지 못함 (네트워크).</summary>
        public readonly long Status;
        public readonly string Error;

        public StreamsApiException(long status, string error)
            : base($"STREAMS API {status}: {error}")
        {
            Status = status;
            Error = error;
        }

        /// <summary>같은 요청을 그대로 다시 보내도 되는 실패 (네트워크, 5xx).</summary>
        public bool Retryable => Status == 0 || Status >= 500;
    }
}
