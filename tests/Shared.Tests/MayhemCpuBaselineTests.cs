using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SlopArena.Shared.AI;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemCpuBaselineTests
{
    [Fact]
    public void TodayPatternsStayWithinFivePercentagePoints()
    {
        var lines = new List<string> { "difficulty,channel,passed,total" };
        foreach (var difficulty in Enum.GetValues<CpuDifficulty>())
        foreach (var entry in MayhemChannels.All)
        {
            int passed = 0, total = 0;
            for (uint sample = 0; sample < 10; sample++)
            {
                uint seed = 101 + sample * 7919;
                var sim = MayhemWorld.NewShow(seed, roster: MayhemWorld.Four);
                var plan = MayhemWorld.Plan(sim);
                var cpus = sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(difficulty));
                var airings = (MayhemAiring[])typeof(MayhemShowPlan).GetField("_airings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(plan)!;
                airings[0] = entry.Id == MayhemChannels.PrimeTime
                    ? new RemixAiring(PrimePairs.BreakOutDodge, new(MayhemChannels.BreakOut, new(1), MayhemMath.ChannelSeed(seed, 0, 0)),
                        new(MayhemChannels.Dodge, new(1), MayhemMath.ChannelSeed(seed, 0, 1)))
                    : new SoloAiring(new(entry.Id, new(1), MayhemMath.ChannelSeed(seed, 0, 0)));
                var calls = (ushort[])typeof(MayhemShowPlan).GetField("_callTicks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(plan)!;
                var plays = (ushort[])typeof(MayhemShowPlan).GetField("_playTicks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(plan)!;
                var starts = (uint[])typeof(MayhemShowPlan).GetField("_callStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(plan)!;
                for (int e = 1; e < starts.Length; e++) starts[e] += 10000;
                calls[0] = airings[0].Timing.CallTicks;
                plays[0] = airings[0].Timing.PlayTicks;
                while (sim.CurrentTick < plan.Deadline(0))
                {
                    MayhemWorld.StepCpus(sim, cpus);
                    foreach (var e in sim.LastTickMayhemEvents)
                    {
                        if (e.Kind is not (MayhemEventKind.Pass or MayhemEventKind.Fail)) continue;
                        total++;
                        if (e.Kind == MayhemEventKind.Pass) passed++;
                    }
                }
            }
            Assert.True(total > 0);
            lines.Add($"{difficulty},{entry.Id.Value},{passed},{total}");
        }
        string path = Path.Combine(MayhemWorld.Root, "tests/Shared.Tests/Fixtures/mayhem-cpu-baseline.csv");
        if (Environment.GetEnvironmentVariable("MAYHEM_RECORD_BASELINE") == "1")
        {
            var existing = File.Exists(path) ? File.ReadAllLines(path).Skip(1).ToArray() : Array.Empty<string>();
            var keys = existing.Select(l => string.Join(",", l.Split(',').Take(2))).ToHashSet();
            File.WriteAllLines(path, new[] { lines[0] }.Concat(existing).Concat(lines.Skip(1).Where(l => !keys.Contains(string.Join(",", l.Split(',').Take(2))))));
        }
        var expected = File.ReadAllLines(path).Skip(1).Select(l => l.Split(',')).ToDictionary(p => (p[0], p[1]));
        foreach (string line in lines.Skip(1))
        {
            var p = line.Split(',');
            var old = expected[(p[0], p[1])];
            decimal rate = decimal.Parse(p[2], CultureInfo.InvariantCulture) / decimal.Parse(p[3], CultureInfo.InvariantCulture);
            decimal baseline = decimal.Parse(old[2], CultureInfo.InvariantCulture) / decimal.Parse(old[3], CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(rate - baseline) <= .05m, $"{p[0]} channel {p[1]} {rate:P1} differs from {baseline:P1}");
        }
    }
}
