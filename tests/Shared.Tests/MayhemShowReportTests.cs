using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Shared.AI;
using SlopArena.Shared.Mayhem;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>Seeded four-CPU shows at each difficulty: every show reaches Ended with a valid outcome, and the run is reported.</summary>
public class MayhemShowReportTests
{
    private readonly ITestOutputHelper _output;
    public MayhemShowReportTests(ITestOutputHelper output) => _output = output;

    private static readonly CharacterClass[][] Casts =
    {
        new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk },
        new[] { CharacterClass.Bashie, CharacterClass.Nyx, CharacterClass.Salamander, CharacterClass.FightGuy },
    };

    [Fact]
    public void TenShowsAtEachDifficultyReachEndedWithValidOutcomesAndReport()
    {
        var allReports = new StringBuilder("# Mayhem headless shows by difficulty\n\n");
        foreach (var difficulty in new[] { CpuDifficulty.Easy, CpuDifficulty.Normal, CpuDifficulty.Hard })
        {
            int sharedWins = 0;
            var passes = new Dictionary<(string Row, SpeedTier Tier), (int Pass, int Total)>();
            var remixes = new StringBuilder("| Seed | Act | Pair | Goal twist | Hazard twist | Passed |\n| ---: | ---: | --- | --- | --- | ---: |\n");
            var causes = new Dictionary<(ChannelId, FailCause), int>();
            var report = new StringBuilder();
            report.AppendLine($"## {difficulty}, ten seeded four-CPU shows");
            report.AppendLine();
            report.AppendLine("| Seed | Cast | Show ticks | Ratings | Winner |");
            report.AppendLine("| ---: | --- | ---: | --- | --- |");
            var schedules = new StringBuilder();
            schedules.AppendLine("| Seed | Episode | Channel | Twist |\n| ---: | ---: | --- | --- |");
            for (int show = 0; show < 10; show++)
            {
                uint seed = (uint)(101 + show * 7919);
                var cast = Casts[show % Casts.Length];
                var sim = MayhemWorld.NewShow(seed, roster: cast);
                var plan = MayhemWorld.Plan(sim);
                var dealt = new HashSet<ChannelId>();
                Assert.Equal(MayhemChannels.GetHim, plan.ChannelAt(14));
                ChannelId previous = ChannelId.None;
                for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
                {
                    var airing = plan.AiringAt(episode);
                    schedules.AppendLine(airing is RemixAiring r
                        ? $"| {seed} | {episode + 1} | {r.PairCopyKey} | {r.GoalTwistCopyKey} + {r.HazardTwistCopyKey} |"
                        : $"| {seed} | {episode + 1} | {MayhemChannels.Find(airing.Channel).CopyKey} | {((SoloAiring)airing).TwistCopyKey} |");
                    if (!MayhemShowPlan.IsBoss(episode))
                    {
                        Assert.True(dealt.Add(airing.Channel));
                        Assert.NotEqual(previous, airing.Channel);
                        previous = airing.Channel;
                    }
                }
                var cpus = sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(difficulty));
                var bossPasses = new int[MayhemShowTuning.Acts];
                while (!sim.EvaluateMatch().IsEnded && sim.CurrentTick < plan.EndTick + 10)
                {
                    MayhemWorld.StepCpus(sim, cpus);
                    foreach (var e in sim.LastTickMayhemEvents)
                    {
                        if (e.Kind is not (MayhemEventKind.Pass or MayhemEventKind.Fail)) continue;
                        var aired = plan.AiringAt(e.Episode);
                        var key = (aired is RemixAiring remix ? remix.PairCopyKey : ((SoloAiring)aired).TwistCopyKey, MayhemShowPlan.TierOf(e.Episode));
                        passes.TryGetValue(key, out var tally);
                        passes[key] = (tally.Pass + (e.Kind == MayhemEventKind.Pass ? 1 : 0), tally.Total + 1);
                        if (e.Kind == MayhemEventKind.Pass && MayhemShowPlan.IsBoss(e.Episode)) bossPasses[MayhemShowPlan.ActOf(e.Episode)]++;
                        if (e.Kind == MayhemEventKind.Fail)
                        {
                            var cause = (aired.Channel, e.FailCause);
                            causes[cause] = causes.TryGetValue(cause, out int n) ? n + 1 : 1;
                        }
                    }
                }
                for (int act = 0; act < MayhemShowTuning.Acts - 1; act++)
                {
                    var r = (RemixAiring)plan.AiringAt(act * MayhemShowTuning.EpisodesPerAct + MayhemShowTuning.RegularPerAct);
                    remixes.AppendLine($"| {seed} | {act + 1} | {r.PairCopyKey} | {r.GoalTwistCopyKey} | {r.HazardTwistCopyKey} | {bossPasses[act]}/{cast.Length} |");
                }
                var outcome = sim.EvaluateMatch();
                Assert.True(outcome.IsEnded, $"seed {seed} did not end");
                Assert.Equal(plan.EndTick, sim.CurrentTick);
                var view = MayhemWorld.View(sim);
                Assert.Equal(MayhemPhase.Ended, view.Cursor.Phase);
                int best = view.Contestants.Max(c => c.Ratings);
                var winners = view.Contestants.Where(c => c.Ratings == best).Select(c => c.EntityId).ToArray();
                if (outcome.IsSharedVictory) sharedWins++;
                Assert.Equal(winners.Length > 1, outcome.IsSharedVictory);
                Assert.Equal(winners.Length == 1 ? winners[0] : 0UL, outcome.WinnerEntityId);
                Assert.All(view.Contestants, c => Assert.Equal(MayhemShowPlan.Ratings(c.PassMask), c.Ratings));
                var result = MayhemRunResult.From(sim);
                var entries = result.Players.Select(p => new MatchResultEntry(p.EntityId, result.PlaceFor(p.EntityId), 0, 0)).ToArray();
                var packet = new MatchResultPacket(sim.CurrentTick, outcome.IsSharedVictory, entries, mayhem: result);
                Assert.Equal(result.Players.Count, packet.Mayhem!.Players.Count);
                Assert.All(sim.GetAllStates().Values, s => Assert.Equal(0, s.DamagePercent));
                string ratings = string.Join(" ", view.Contestants.Select(c => $"{cast[c.Seat]}:{c.Ratings}"));
                string winner = winners.Length == 1 ? cast[sim.MayhemShow.SeatOf(winners[0])].ToString() : $"tie ({winners.Length})";
                report.AppendLine($"| {seed} | {string.Join(", ", cast)} | {sim.CurrentTick} | {ratings} | {winner} |");
            }
            report.AppendLine();
            report.AppendLine("Ratings: regular pass 1, boss pass 2; maximum 18.");
            report.AppendLine();
            report.AppendLine("### Pass rate by channel and tier");
            report.AppendLine();
            report.AppendLine("| Channel | Tier 1 | Tier 2 | Tier 3 |");
            report.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (string row in MayhemChannels.All.SelectMany(e => e.Twists).Select(t => t.CopyKey).Concat(PrimePairs.All.Select(p => p.CopyKey)))
            {
                string Cell(SpeedTier tier) => passes.TryGetValue((row, tier), out var t) && t.Total > 0
                    ? $"{100.0 * t.Pass / t.Total:F0}% ({t.Pass}/{t.Total})" : "—";
                report.AppendLine($"| {row} | {Cell(SpeedTier.One)} | {Cell(SpeedTier.Two)} | {Cell(SpeedTier.Three)} |");
            }
            report.AppendLine();
            report.AppendLine("### PRIME TIME! remixes in acts one and two");
            report.AppendLine();
            report.Append(remixes);
            report.AppendLine();
            report.AppendLine("### Failure causes");
            report.AppendLine();
            foreach (var ((channel, cause), count) in causes.OrderBy(c => c.Key.Item1.Value).ThenBy(c => c.Key.Item2))
                report.AppendLine($"- {MayhemChannels.Find(channel).CopyKey}: {cause} × {count}");
            int total = passes.Values.Sum(t => t.Total), passed = passes.Values.Sum(t => t.Pass);
            report.AppendLine($"\nOverall pass rate {passed}/{total} ({100.0 * passed / total:F1}%). Shared wins {sharedWins}/10 ({sharedWins * 10}%).");
            if (difficulty == CpuDifficulty.Normal) Assert.True((double)passed / total < .90, "Normal CPUs must lose channels");
            allReports.AppendLine(report.ToString());
            allReports.AppendLine(schedules.ToString());
        }
        string text = allReports.ToString();
        _output.WriteLine(text);
        string dir = Path.Combine(MayhemWorld.Root, "tmp", "mayhem-channels-step1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "headless-shows.md"), text);
    }
}
