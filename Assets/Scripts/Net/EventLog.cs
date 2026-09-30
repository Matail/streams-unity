// 행동 기록 — POST /api/streams/events (docs/streams-api.md, my-site 원본).
// 턴 기록으로 못 담는 것(칸 위 망설임, 판 포기, 재대전, 세션)을 모았다가 한 번에 보낸다.
// 분석용이라 잃어도 되는 기록: 실패하면 버리고, 게임 진행을 막지 않는다.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Streams.Net
{
    public sealed class EventLog
    {
        const int MaxPerRequest = 50; // 서버 한도

        [Serializable]
        class Item
        {
            public string gameId; // 판과 상관없으면 "" (서버가 null 로 저장)
            public int seq;
            public string type;
            public long ts;
            public string data;   // JSON 문자열 — JsonUtility 는 자유 형식 객체를 못 만든다
        }

        [Serializable]
        class Batch
        {
            public string sessionId;
            public string playerId;
            public List<Item> events;
        }

        [Serializable]
        class Accepted
        {
            public int accepted;
        }

        readonly ApiClient api;
        readonly string sessionId = Guid.NewGuid().ToString();
        readonly List<Item> buffer = new List<Item>();
        int seq;
        bool sending;

        public EventLog(ApiClient api) => this.api = api;

        /// <summary>기록을 버퍼에 넣는다. data 는 JSON 객체 문자열 (없으면 null).</summary>
        public void Add(string type, string gameId = null, string data = null)
        {
            buffer.Add(new Item
            {
                gameId = gameId ?? "",
                seq = seq++,
                type = type,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                data = data ?? "",
            });
        }

        /// <summary>모인 기록을 보낸다 (보내는 중이면 다음 차례로 미룬다).</summary>
        public async void Flush()
        {
            if (sending || buffer.Count == 0) return;
            sending = true;
            int n = Math.Min(MaxPerRequest, buffer.Count);
            var batch = new Batch { sessionId = sessionId, playerId = ClientInfo.PlayerId, events = buffer.GetRange(0, n) };
            buffer.RemoveRange(0, n);
            try
            {
                await api.PostAsync<Batch, Accepted>("/events", batch);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"STREAMS: 행동 기록 {n}개를 보내지 못해 버림 ({e.Message})");
            }
            sending = false;
        }
    }
}
