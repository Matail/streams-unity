// Streams.Core 가 서버 규칙(rules.json)과 같은 답을 내는지 확인. 틀리면 exit 1.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Streams.Core;

var path = args.Length > 0 ? args[0] : Path.Combine("Assets", "Tests", "EditMode", "Data", "rules.json");
using var doc = JsonDocument.Parse(File.ReadAllText(path));
var root = doc.RootElement;
int[] Ints(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();

int fail = 0;
var rules = root.GetProperty("rules");
if (rules.GetProperty("slots").GetInt32() != Rules.Slots) { Console.Error.WriteLine("slots 다름"); fail++; }
if (!Ints(rules.GetProperty("pool")).SequenceEqual(Rules.Pool)) { Console.Error.WriteLine("pool 다름"); fail++; }
if (!Ints(rules.GetProperty("scoreTable")).SequenceEqual(Rules.ScoreTable)) { Console.Error.WriteLine("scoreTable 다름"); fail++; }

int n = 0;
foreach (var c in root.GetProperty("cases").EnumerateArray())
{
    n++;
    var board = Ints(c.GetProperty("board"));
    var runs = Ints(c.GetProperty("runs"));
    var score = c.GetProperty("score").GetInt32();
    var gotRuns = Rules.RunLengths(board);
    var gotScore = Rules.Score(board);
    if (!gotRuns.SequenceEqual(runs) || gotScore != score)
    {
        fail++;
        if (fail <= 10)
            Console.Error.WriteLine($"#{n} [{string.Join(",", board)}] 기대 runs=[{string.Join(",", runs)}] score={score}, 실제 runs=[{string.Join(",", gotRuns)}] score={gotScore}");
    }
}
Console.WriteLine(fail == 0 ? $"규칙 일치 ({n}개)" : $"규칙 불일치 {fail}개 / {n}개");
return fail == 0 ? 0 : 1;
