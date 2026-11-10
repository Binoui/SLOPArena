using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemFinalPairTrialsTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(6)]
    public void CandidateOnlyJoinsTheDeckWhenEveryCarefulCombinationCertifies(ushort goalId)
    {
        var goalEntry = MayhemChannels.Find(new(goalId));
        var hazardEntry = MayhemChannels.Find(MayhemChannels.Jump);
        var row = new PrimePairEntry(new(200), goalEntry.Id, hazardEntry.Id,
            $"mayhem.pair.{goalEntry.CopyKey[15..]}-jump", new(90, 480), JumpTuning.FirstSlamTicks,
            (IMayhemChannel goal, IMayhemChannel hazard, in CpuProbe probe) => SafeStep.Around(goal.Cpu(probe), hazard.Observe(), probe));
        var report = new StringBuilder($"# {row.CopyKey} candidate certification\n\nCareful CPUs, 12-tick delay, no slips, tier three, seeds 1 through 12 per twist combination and cast size. Fixed cast FightGuy, Manki, Wibou, Sir Bonk.\n\n| Goal | Hazard | Cast | Passes | Hazards | Problems |\n| --- | --- | ---: | ---: | ---: | ---: |\n");
        var problems = new List<string>(); int totalRuns = 0;
        foreach (var g in goalEntry.Twists)
        foreach (var h in hazardEntry.Twists)
        foreach (int count in new[] { 2, 3, 4 })
        {
            int passes = 0, hazards = 0, failures = 0;
            for (uint seed = 1; seed <= 12; seed++)
            {
                var sim = MayhemWorld.NewShow(seed, count: count, roster: MayhemWorld.Four);
                int episode = 4;
                MayhemWorld.Pin(sim, episode, new SoloAiring(new(MayhemChannels.Bonk, new(1), seed)));
                MayhemWorld.SkipToCall(sim, episode); MayhemWorld.Step(sim);
                var plan = MayhemWorld.Plan(sim);
                var seats = sim.MayhemShow!.Roster.Select((id, s) => new ChannelSeat((byte)s, id, MayhemPose.Of(sim.GetState(id)),
                    sim.GetDefinition(id).CapsuleRadius, sim.GetDefinition(id).CapsuleHeight)).ToArray();
                var setup = new ChannelSetup(new SoloAiring(new(goalEntry.Id, g.Id, MayhemMath.ChannelSeed(seed, episode, 0))), episode, 0, SpeedTier.Three,
                    plan.CallStart(episode), plan.PlayStart(episode), plan.Deadline(episode), MayhemWorld.Arena().Arcade!.Mayhem!, seats);
                var goal = goalEntry.Create(setup);
                var hazard = JumpChannel.Start(setup with { Airing = new SoloAiring(new(hazardEntry.Id, h.Id, MayhemMath.ChannelSeed(seed, episode, 1))), Lane = 1 });
                var candidate = (IMayhemChannel)Activator.CreateInstance(typeof(PrimeTimeChannel), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { row, goal, hazard }, null)!;
                if (PrimePairs.All.FirstOrDefault(p => p.Goal == row.Goal && p.Hazard == row.Hazard) is { } admitted)
                {
                    sim = MayhemWorld.NewShow(seed, count: count, roster: MayhemWorld.Four);
                    MayhemWorld.Pin(sim, episode, MayhemWorld.RemixAt(seed, episode, admitted.Id, g.Id, h.Id));
                    MayhemWorld.SkipToCall(sim, episode);
                }
                else typeof(MayhemShow).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sim.MayhemShow, candidate);
                var cpus = sim.MayhemShow.Roster.ToDictionary(id => id, _ => new MayhemCpu(12, 0));
                var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
                int hurt = MayhemWorld.View(sim).Contestants.Count(c => c.Failure is { Cause: FailCause.Hazard or FailCause.Fell });
                passes += passed.Count(p => p); hazards += hurt; totalRuns++;
                if (hurt == 0 && passed.All(p => p)) continue;
                failures++;
                problems.Add($"{g.CopyKey} + {h.CopyKey}, {count} seats seed {seed}. " + string.Join(", ",
                    MayhemWorld.View(sim).Contestants.Where(c => c.Failure != null).Select(c => $"{c.Seat} {c.Failure!.Value.Cause}@{c.Failure.Value.Tick - setup.PlayStart}")));
            }
            report.AppendLine($"| {g.CopyKey} | {h.CopyKey} | {count} | {passes}/{count * 12} | {hazards} | {failures} |");
        }
        report.AppendLine($"\n{totalRuns} airings. {(problems.Count == 0 ? "Certified" : "Rejected")}.\n");
        foreach (string problem in problems) report.AppendLine($"- {problem}");
        string dir = Path.Combine(MayhemWorld.Root, "tmp/u69"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"candidate-{goalId}-jump.md"), report.ToString());
        Assert.Equal(problems.Count == 0, PrimePairs.All.Any(p => p.Goal == row.Goal && p.Hazard == row.Hazard));
    }
}
