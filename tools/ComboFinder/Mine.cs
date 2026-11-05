using System.Collections.Concurrent;
using System.Diagnostics;
using SlopArena.Shared.AI;
namespace SlopArena.ComboFinder;

/// <summary>
/// CPU-against-CPU mining: play many seeded fights between two Hard CPUs, score each fight from the attacker's side with
/// the same taste score as search, and export the best eligible strings as ordinary combo files. One fight is one pass
/// of the simulation, where search re-simulates every branch from tick zero, and the attacker moves like the CPU does.
/// </summary>
static class Mine
{
    internal const int MaxTicks = 900;
    internal static List<ComboFile> Run(SearchOptions o, int fights, int keep, CpuDifficulty victimDifficulty, string output)
    {
        // Photo mode starts a capture at tick 900 at the latest, so a longer fight could never be checked or filmed.
        o = o with { Max = Math.Min(o.Max, MaxTicks) };
        var watch = Stopwatch.StartNew();
        var content = new Content(o.Stage);
        var fighters = content.Fighters(o);
        var found = new ConcurrentBag<(double score, int seed)>();
        long ticks = 0;
        Parallel.For(1, fights + 1, seed =>
        {
            var runner = new Runner(content, fighters[0], fighters[1], seed, o.Seg) { VictimDifficulty = victimDifficulty };
            var r = runner.Run(new(), o.Max, false, cpuAttackerSeed: seed * 7919 + 1);
            Interlocked.Add(ref ticks, r.Ticks);
            if (r.VictimKo < 0 || r.AttackerKo >= 0 || !Search.Eligible(Replay.Timeline(r))) return;
            found.Add((Scoring.Score(r).score, seed));
        });
        var best = found.OrderByDescending(f => f.score).ThenBy(f => f.seed).Take(keep).ToList();
        Console.Error.WriteLine($"mine {o.Attacker}-{o.Victim} {o.Stage} {o.Layout}: {fights} fights, {found.Count} eligible KOs, {ticks / watch.Elapsed.TotalSeconds:F0} ticks/s, {watch.Elapsed.TotalSeconds:F1}s");
        var combos = new List<ComboFile>();
        foreach (var (score, seed) in best)
        {
            var runner = new Runner(content, fighters[0], fighters[1], seed, o.Seg) { VictimDifficulty = victimDifficulty };
            var trace = runner.Run(new(), o.Max, true, cpuAttackerSeed: seed * 7919 + 1);
            combos.Add(Search.Export(o with { Seed = seed, Beam = 0 }, content, fighters, trace, score, null, output));
        }
        return combos;
    }
}
