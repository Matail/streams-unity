// 판 결과 등수: 같은 점수는 같은 등수 (서버 room.ts 의 finished 와 같은 규칙).
using NUnit.Framework;
using Streams.Net;

public class GameResultTests
{
    [Test]
    public void RanksWithTies()
    {
        var r = GameResult.FromScores(new[] { (0, 30), (1, 47), (3, 30), (5, 12) }, 3);
        CollectionAssert.AreEqual(new[] { 2, 1, 2, 4 }, new[] { r.Standings[0].Rank, r.Standings[1].Rank, r.Standings[2].Rank, r.Standings[3].Rank });
        Assert.AreEqual(3, r.Mine.Seat);
        Assert.AreEqual(30, r.Mine.Score);
    }

    [Test]
    public void AiGameDraw()
    {
        var r = GameResult.FromScores(new[] { (AiSession.MySeat, 41), (AiSession.AiSeat, 41) }, AiSession.MySeat);
        Assert.AreEqual(1, r.Mine.Rank);
        Assert.AreEqual(1, r.Standings[1].Rank);
    }
}
