using System.Collections.Concurrent;
using System.Diagnostics;
namespace SlopArena.ComboFinder;

sealed record Candidate(List<string> Plan, double Score, string Why, int Ko)
{
    internal string Key => string.Join(",", Plan);
}
static class Search
{
    internal static ComboFile? Find(SearchOptions o, string output)
    {
        var watch = Stopwatch.StartNew();
        var content = new Content(o.Stage);
        var fighters = content.Fighters(o);
        var runner = new Runner(content, fighters[0], fighters[1], o.Seed, o.Seg);
        string[] route = o.Route ?? Array.Empty<string>();
        var validRoute = new HashSet<string> { "1", "2", "3", "g1", "g2", "g3", "a1", "a2", "a3", "A", "E", "R", "F", "beeline", "juggle" };
        if (route.Any(token => !validRoute.Contains(token))) throw new ArgumentException("Invalid route token");
        var actions = route.Length == 0 ? Runner.actions : Runner.moveActions.Concat(route.SelectMany(Runner.PressesFor)).Distinct().ToList();
        var beam = new List<Candidate> { new(new(), 0, "", -1) };
        Candidate? bestDone = null, bestPartial = null;
        int firstKoSeg = -1;
        for (int seg = 0; seg * o.Seg < o.Max && beam.Count > 0; seg++)
        {
            var candidates = new ConcurrentBag<Candidate>();
            var work = beam.SelectMany(n => actions.Select(a => n.Plan.Append(a).ToList())).ToList();
            Parallel.ForEach(work, plan =>
            {
                var r = runner.Run(plan, Math.Min(36, o.Max - plan.Count * o.Seg), false);
                var (score, why) = Scoring.Score(r);
                int ko = r.VictimKo;
                if (route.Length > 0)
                {
                    var rs = Scoring.RouteScore(r, route); score = rs.score; why = rs.why;
                    if (ko >= 0 && rs.progress != 1) ko = -2;
                }
                if (ko >= 0 && (r.AttackerKo >= 0 || !Eligible(Replay.Timeline(r)))) ko = -2;
                candidates.Add(new(plan, score, why, ko));
            });
            var ordered = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
            var done = ordered.FirstOrDefault(c => c.Ko >= 0);
            if (done != null && (bestDone == null || done.Score > bestDone.Score || done.Score == bestDone.Score && string.CompareOrdinal(done.Key, bestDone.Key) < 0)) bestDone = done;
            beam = ordered.Where(c => c.Ko == -1).GroupBy(c => c.Why)
                .Select(g => g.OrderBy(c => c.Plan.Count(p => p != "idle")).ThenBy(c => c.Key, StringComparer.Ordinal).First())
                .OrderByDescending(c => c.Score).ThenBy(c => c.Key, StringComparer.Ordinal).Take(o.Beam).ToList();
            if (beam.Count > 0 && (bestPartial == null || beam[0].Score > bestPartial.Score || beam[0].Score == bestPartial.Score && string.CompareOrdinal(beam[0].Key, bestPartial.Key) < 0)) bestPartial = beam[0];
            if (bestDone != null && firstKoSeg < 0) firstKoSeg = seg;
            if (seg % 10 == 0) Console.Error.WriteLine($"segment={seg} best={beam.FirstOrDefault()?.Score:F1} KO={bestDone?.Score:F1} elapsed={watch.Elapsed.TotalSeconds:F1}s");
            if (bestDone != null && seg - firstKoSeg >= o.Patience) break;
        }
        var best = bestDone ?? bestPartial;
        if (best == null) { Console.WriteLine("No surviving candidates"); return null; }
        var trace = runner.Run(best.Plan, Math.Min(36, o.Max - best.Plan.Count * o.Seg), true);
        var timeline = Replay.Timeline(trace);
        string summary = Replay.Summary(o, timeline);
        Console.WriteLine($"{(bestDone == null ? "PARTIAL" : "BEST")} score={best.Score:F1} elapsed={watch.Elapsed.TotalSeconds:F2}s {summary}");
        Console.WriteLine(best.Why);
        if (bestDone == null)
        {
            foreach (var m in timeline.Moves) Console.WriteLine($"landed tick={m.Tick} lastTick={m.LastTick} {m.Label} damage={m.Damage}");
            return null;
        }
        fighters[0] = fighters[0] with { Tape = Replay.Tape(trace.TapeA) };
        fighters[1] = fighters[1] with { Tape = Replay.Tape(trace.TapeV) };
        string id = $"{o.Attacker}-{o.Victim}-{o.Stage}-{o.Damage}-s{o.Seed}";
        var combo = new ComboFile(1, id, o.Stage, content.Stamp(fighters), new(o.Seed, o.Beam, "Hard", best.Score, route.Length == 0 ? null : route), fighters, timeline, summary);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, id + ".json"), Json.Write(combo));
        return combo;
    }
    static bool Eligible(Timeline t)
    {
        var str = t.Moves.Where(m => m.Tick >= t.String.From && m.LastTick <= t.String.To).ToArray();
        bool Normal(LandedMove m) => m.Label.Length == 2 && m.Label[0] is 'a' or 'g';
        if (t.Taken != 0 || t.Ko < 0 || str.Length == 0 || t.Moves.Any(m => m.Label.StartsWith("slot")) || t.String.To != t.Moves[^1].LastTick || t.Ko - str[^1].LastTick >= 120 || str.Count(Normal) * 2 <= str.Length) return false;
        int setups = 0;
        for (int i = 1; i < str.Length - 1; i++)
            if (!Normal(str[i]) && !(i + 2 < str.Length && Normal(str[i + 1]) && Normal(str[i + 2]) && setups++ == 0)) return false;
        return true;
    }
}
