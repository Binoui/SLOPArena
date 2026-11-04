using System.Globalization;
using SlopArena.ComboFinder;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("ComboFinder search --attacker pkg --victim pkg --stage name --damage pct [--seed 1 --beam 30 --max 660 --patience 25 --seg 6 --route A,a1 --layouts all|spawn,east,west,north,south --a x,z --v x,z] --out dir\nComboFinder sweep --attackers a,b --victims c,d --stages s1,s2 --damages 40,60 [--seeds 1,2 --layouts all --top N] --out dir\nComboFinder cameras combo.json --orientation portrait|landscape\nComboFinder verify combo.json");
        return 0;
    }
    string Arg(string key, string? fallback = null)
    {
        int i = Array.IndexOf(args, key);
        if (i < 0) return fallback ?? throw new ArgumentException($"Missing {key}");
        if (i + 1 == args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException($"Missing value for {key}");
        return args[i + 1];
    }
    int Number(string key, int fallback, bool positive = true)
    {
        int n = int.Parse(Arg(key, fallback.ToString()));
        if (positive && n <= 0 || key == "--patience" && n < 0) throw new ArgumentException($"{key} must be positive");
        return n;
    }
    string[] List(string key, string? fallback = null) => Arg(key, fallback).Split(',', StringSplitOptions.RemoveEmptyEntries);
    float[]? Position(string key)
    {
        var raw = Arg(key, ""); if (raw == "") return null;
        var p = raw.Split(',').Select(float.Parse).ToArray();
        if (p.Length != 2 || p.Any(x => !float.IsFinite(x))) throw new ArgumentException($"{key} needs finite x,z");
        return p;
    }
    SearchOptions Options(string a, string v, string stage, ushort damage, int seed) => new(a, v, stage, damage, seed,
        Number("--beam", 30), Number("--max", 660), Number("--patience", 25, false), Number("--seg", 6),
        List("--route", ""), Position("--a"), Position("--v"));
    // Explicit --a/--v is one custom layout; otherwise every stage layout named by --layouts (default: all).
    List<ComboFile> FindAll(SearchOptions o)
    {
        var found = new List<ComboFile>();
        if (o.AttackerPosition != null || o.VictimPosition != null)
        {
            if (Search.Find(o with { Layout = "custom" }, Arg("--out")) is { } custom) found.Add(custom);
            return found;
        }
        var wanted = List("--layouts", "all");
        foreach (var layout in new Content(o.Stage).Layouts())
            if (wanted.Contains("all") || wanted.Contains(layout.Name))
                if (Search.Find(o with { Layout = layout.Name, AttackerPosition = layout.Attacker, VictimPosition = layout.Victim }, Arg("--out")) is { } combo)
                    found.Add(combo);
        return found;
    }
    switch (args[0])
    {
        case "search":
            var combos = FindAll(Options(Arg("--attacker"), Arg("--victim"), Arg("--stage"), ushort.Parse(Arg("--damage")), Number("--seed", 1, false)));
            foreach (var c in combos.OrderByDescending(c => c.Search.Score).ThenBy(c => c.Id, StringComparer.Ordinal))
                Console.WriteLine($"{c.Search.Score:F1} | {c.Id} | {c.Summary}");
            return combos.Count == 0 ? 1 : 0;
        case "sweep":
            var found = new List<ComboFile>();
            foreach (var stage in List("--stages"))
                foreach (var a in List("--attackers"))
                    foreach (var v in List("--victims"))
                        foreach (var damage in List("--damages").Select(ushort.Parse))
                            foreach (var seed in List("--seeds", "1").Select(int.Parse))
                            {
                                if (a == v) continue;
                                found.AddRange(FindAll(Options(a, v, stage, damage, seed)));
                            }
            Console.WriteLine("score | seconds | summary");
            foreach (var c in found.OrderByDescending(c => c.Search.Score).ThenBy(c => c.Id, StringComparer.Ordinal).Take(Number("--top", int.MaxValue)))
                Console.WriteLine($"{c.Search.Score:F1} | {c.Timeline.Ticks / 60.0:F2} | {c.Summary}");
            Console.WriteLine($"Found {found.Count} combos");
            return found.Count == 0 ? 1 : 0;
        case "verify":
        case "cameras":
            if (args.Length < 2) throw new ArgumentException("Missing combo path");
            var file = Json.Read(args[1]);
            var stdout = Console.Out;
            string result;
            try
            {
                Console.SetOut(Console.Error);
                var content = new Content(file.Stage);
                if (args[0] == "cameras") result = Json.Write(Cameras.Plan(content, file, Arg("--orientation")));
                else { var trace = Replay.Run(content, file); var (score, why) = Scoring.Score(trace); var (travel, planted) = Scoring.Movement(trace); result = $"PASS {file.Id}: content, moves, ticks, swings, taken and KO match\nscore={score:F1} travel={travel:F1}m planted={planted} {why}\n"; }
            }
            finally { Console.SetOut(stdout); }
            Console.Write(result);
            return 0;
        default: throw new ArgumentException($"Unknown command {args[0]}");
    }
}
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or FormatException or System.Text.Json.JsonException or InvalidOperationException)
{
    Console.Error.WriteLine("FAIL " + ex.Message); return 2;
}
