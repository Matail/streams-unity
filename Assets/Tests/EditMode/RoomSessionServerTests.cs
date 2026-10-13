// 멀티 방을 실제 서버에 붙여 두 자리로 한 판 (RoomSocket + RoomSession).
// 서버가 있어야 해서 평소엔 돌지 않는다 (Explicit). 서버 주소는 환경 변수 STREAMS_TEST_SERVER, 없으면 로컬 wrangler dev:
//   matail.party 리포에서 npx wrangler dev → Test Runner 에서 이 테스트를 골라 Run Selected
// 에디터의 ClientInfo.BaseUrl(PlayerPrefs)은 쓰지 않는다 — 배포 서버를 가리키고 있으면 테스트 방이 거기 생긴다.
// 20초 시간 초과를 한 번 기다려서 30초쯤 걸린다.
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Streams.Core;
using Streams.Net;
using UnityEngine;
using UnityEngine.TestTools;

[Explicit, Category("Server")]
public class RoomSessionServerTests
{
    static readonly string Server = System.Environment.GetEnvironmentVariable("STREAMS_TEST_SERVER") ?? "http://localhost:8787";
    static readonly List<RoomSession> live = new List<RoomSession>();

    /// <summary>한 프레임 쉰다 — 그동안 받은 메시지를 이벤트로 낸다.</summary>
    static object Frame()
    {
        foreach (var s in live) s.Poll();
        return null;
    }

    sealed class Player
    {
        public RoomSession Session;
        public readonly Board Board = new Board();
        public int Card = -1, Turn = -1;
        public RoomState State;
        public GameResult? Result;
        public readonly Dictionary<int, int> Scores = new Dictionary<int, int>();
        public readonly List<(int seat, int slot, int card)> Placed = new List<(int, int, int)>();
        public readonly List<int> Closed = new List<int>();

        public Player(RoomSession s)
        {
            Session = s;
            live.Add(s);
            s.StateChanged += st => State = st;
            s.CardDealt += (c, t) => { Card = c; Turn = t; };
            s.Placed += (seat, slot, c) =>
            {
                Placed.Add((seat, slot, c));
                if (seat == s.MySeat) Board.Place(slot, c); // AI 가 대신 둔 내 수
            };
            s.ScoreChanged += (seat, score) => Scores[seat] = score;
            s.Finished += r => Result = r;
            s.Disconnected += code =>
            {
                Closed.Add(code);
                Debug.Log($"seat {s.MySeat} closed {code}");
            };
        }

        public int FirstEmpty()
        {
            int slot = 0;
            while (!Board.IsEmpty(slot)) slot++;
            return slot;
        }
    }

    static IEnumerator Until(System.Func<bool> done, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!done())
        {
            if (Time.realtimeSinceStartup > end) Assert.Fail($"timeout: {what}");
            yield return Frame();
        }
    }

    static IEnumerator Wait(Awaitable a)
    {
        while (!a.IsCompleted) yield return Frame();
        a.GetAwaiter().GetResult();
    }

    static IEnumerator Wait<T>(Awaitable<T> a, System.Action<T> got)
    {
        var w = a.GetAwaiter();
        while (!w.IsCompleted) yield return Frame();
        got(w.GetResult());
    }

    [TearDown]
    public void Close()
    {
        foreach (var s in live) s.Close();
        live.Clear();
    }

    [UnityTest]
    public IEnumerator TwoSeatsPlayFullGame()
    {
        var api = new ApiClient(Server);
        RoomTicket ta = null, tb = null;
        yield return Wait(RoomSession.CreateAsync(api, "에이"), t => ta = t);
        yield return Wait(RoomSession.JoinAsync(api, ta.code.ToLowerInvariant(), "비"), t => tb = t);
        Assert.AreEqual(1, tb.seat);

        var a = new Player(new RoomSession(Server, ta));
        var b = new Player(new RoomSession(Server, tb));
        yield return Until(() => a.State != null && a.State.seats.Length == 2 && a.State.seats[0].online && a.State.seats[1].online, 10, "both online");
        Assert.AreEqual("lobby", a.State.phase);
        Assert.AreEqual(0, a.State.host);
        Assert.AreEqual("비", a.State.seats[1].name);

        // 방장이 아니면 시작 못 함
        var notHost = b.Session.StartAsync(new SessionOptions(0));
        while (!notHost.IsCompleted) yield return Frame();
        var ex = Assert.Throws<StreamsApiException>(() => notHost.GetAwaiter().GetResult());
        Assert.AreEqual("not_host", ex.Error);

        yield return Wait(a.Session.StartAsync(new SessionOptions(0)));
        yield return Until(() => a.Turn == 0 && b.Turn == 0, 10, "first card");
        Assert.AreEqual(a.Card, b.Card);

        for (int turn = 0; turn < Rules.Slots; turn++)
        {
            yield return Until(() => a.Turn == turn && b.Turn == turn, 30, $"card {turn}");
            float next = Time.realtimeSinceStartup + 0.15f; // 서버는 연결당 초당 10개까지 받는다
            yield return Until(() => Time.realtimeSinceStartup >= next, 1, "pace");
            int card = a.Card;
            int slotA = a.FirstEmpty();
            a.Board.Place(slotA, card);
            yield return Wait(a.Session.PlaceAsync(slotA, 300));

            if (turn == 0)
            {
                // 이미 놓았는데 다른 칸으로 다시 → already_placed
                var again = a.Session.PlaceAsync((slotA + 1) % Rules.Slots, 300);
                while (!again.IsCompleted) yield return Frame();
                Assert.AreEqual("already_placed", Assert.Throws<StreamsApiException>(() => again.GetAwaiter().GetResult()).Error);
            }

            if (turn == 3)
            {
                // B 가 안 둔다 → 20초 뒤 AI 가 B 자리에 두고, B 도 자기 수를 받는다
                int before = b.Placed.Count;
                yield return Until(() => b.Placed.Count > before, 26, "timeout auto move");
                Assert.AreEqual(b.Session.MySeat, b.Placed[b.Placed.Count - 1].seat);
            }
            else
            {
                int slotB = b.FirstEmpty();
                b.Board.Place(slotB, card);
                yield return Wait(b.Session.PlaceAsync(slotB, 300));
            }
            if (turn < Rules.Slots - 1) yield return Until(() => a.Turn == turn + 1, 10, $"reveal {turn}");
        }

        yield return Until(() => a.Result.HasValue && b.Result.HasValue, 10, "finished");
        // A 는 B 의 수 20개를 받았고, 내 보드 점수가 서버 점수와 같다
        Assert.AreEqual(Rules.Slots, a.Placed.FindAll(p => p.seat == b.Session.MySeat).Count);
        Assert.AreEqual(a.Board.Score, a.Scores[a.Session.MySeat]);
        Assert.AreEqual(b.Board.Score, b.Scores[b.Session.MySeat]);
        Assert.AreEqual(a.Board.Score, a.Result.Value.Mine.Score);
        Assert.AreEqual(b.Board.Score, b.Result.Value.Mine.Score);
        Debug.Log($"room {ta.code}: A {a.Board.Score} (rank {a.Result.Value.Mine.Rank}), B {b.Board.Score} (rank {b.Result.Value.Mine.Rank})");

        a.Session.Leave();
        b.Session.Leave();
        yield return Until(() => a.Closed.Count > 0 && b.Closed.Count > 0, 10, "closed");
    }
}
