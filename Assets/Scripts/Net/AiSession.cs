// AI 대전: POST /api/streams/start, /move. 게임 상태는 서버가 암호화한 token 으로 들고 다닌다.
using System;
using Streams.Core;
using UnityEngine;

namespace Streams.Net
{
    public sealed class AiSession : IGameSession
    {
        public const int MySeat = 0, AiSeat = 1;

        public event Action<SessionInfo> Started;
        public event Action<int, int> CardDealt;
        public event Action<int, int, int> Placed;
        public event Action<int, int> ScoreChanged;
        public event Action<GameResult> Finished;

        readonly ApiClient api;
        string token;
        int card;
        int turn;
        bool busy;
        readonly Board myBoard = new Board(); // 잘못된 칸을 서버에 보내기 전에 거르는 용도

        public AiSession(ApiClient api) => this.api = api;

        public bool IsPlaying => token != null && turn < Rules.Slots;

        public async Awaitable StartAsync(SessionOptions options)
        {
            var res = await api.PostAsync<StartRequest, StartResponse>("/start",
                new StartRequest { level = options.Level, playerId = ClientInfo.PlayerId });
            token = res.token;
            card = res.card;
            turn = res.turn;
            myBoard.Reset();
            var players = new[] { new PlayerInfo(MySeat, "나", false), new PlayerInfo(AiSeat, $"AI({res.levelName})", true) };
            Started?.Invoke(new SessionInfo(res.gameId, res.level, res.levelName, MySeat, players));
            ScoreChanged?.Invoke(MySeat, 0);
            ScoreChanged?.Invoke(AiSeat, 0);
            CardDealt?.Invoke(card, turn);
        }

        public async Awaitable PlaceAsync(int slot, int thinkMs)
        {
            if (!IsPlaying) throw new InvalidOperationException("no game in progress");
            if (busy) throw new InvalidOperationException("previous move still pending");
            if (!myBoard.IsEmpty(slot)) throw new StreamsApiException(400, "invalid slot");

            busy = true;
            int placed = card;
            try
            {
                var res = await api.PostAsync<MoveRequest, MoveResponse>("/move",
                    new MoveRequest { token = token, slot = slot, thinkMs = Math.Max(0, thinkMs) });
                token = res.token;
                turn = res.turn;
                myBoard.Place(slot, placed);
                Placed?.Invoke(AiSeat, res.aiSlot, placed);
                ScoreChanged?.Invoke(MySeat, res.playerScore);
                ScoreChanged?.Invoke(AiSeat, res.aiScore);
                if (res.finished)
                {
                    Finished?.Invoke(GameResult.FromScores(new[] { (MySeat, res.playerScore), (AiSeat, res.aiScore) }, MySeat));
                }
                else
                {
                    card = res.nextCard;
                    CardDealt?.Invoke(card, turn);
                }
            }
            finally
            {
                busy = false;
            }
        }
    }
}
