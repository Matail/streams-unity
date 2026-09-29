// Streams.Core 가 서버 규칙과 같은 답을 내는지. 정답지는 my-site 의 worker/streams/testdata/rules.json 복사본.
// Unity 없이도 같은 검사를 한다: Tools/CoreCheck (CI).
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Streams.Core;
using UnityEngine;

public class RulesVectorTests
{
    [Serializable] class RulesDoc { public RulesDef rules; public Case[] cases; }
    [Serializable] class RulesDef { public int slots; public int[] pool; public int[] scoreTable; }
    [Serializable] class Case { public string note; public int[] board; public int[] runs; public int score; }

    static RulesDoc Load() =>
        JsonUtility.FromJson<RulesDoc>(File.ReadAllText(Path.Combine(Application.dataPath, "Tests/EditMode/Data/rules.json")));

    [Test]
    public void ConstantsMatchServer()
    {
        var doc = Load();
        Assert.AreEqual(doc.rules.slots, Rules.Slots);
        CollectionAssert.AreEqual(doc.rules.pool, Rules.Pool.ToArray());
        CollectionAssert.AreEqual(doc.rules.scoreTable, Rules.ScoreTable.ToArray());
    }

    [Test]
    public void ScoresMatchServer()
    {
        var doc = Load();
        Assert.Greater(doc.cases.Length, 500);
        foreach (var c in doc.cases)
        {
            var label = $"{c.note} [{string.Join(",", c.board)}]";
            CollectionAssert.AreEqual(c.runs, Rules.RunLengths(c.board), label);
            Assert.AreEqual(c.score, Rules.Score(c.board), label);
        }
    }

    [Test]
    public void BoardRejectsFilledSlot()
    {
        var b = new Board();
        b.Place(3, 12);
        Assert.Throws<InvalidOperationException>(() => b.Place(3, 5));
        Assert.AreEqual(1, b.ScoreIfPlaced(4, 13)); // 12,13 → 길이 2 → 1점
    }
}
