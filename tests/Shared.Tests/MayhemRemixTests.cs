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

/// <summary>PRIME TIME! remixes: the pair rows, routing between the two children, the view and the certification.</summary>
public class MayhemRemixTests
{
    private readonly ITestOutputHelper _output;
    public MayhemRemixTests(ITestOutputHelper output) => _output = output;

    private const int SweepSeeds = 12;
    private static readonly int CarefulDelay = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard).ReactionDelayTicks;

    /// <summary>
    /// Kits too slow for a goal twist, whatever the hazard does. Their runs are still played and tabled, never hidden.
    /// Salamander's first normal lands its second wall hit 102 ticks after the swing; with the Hard delay it leaves a
    /// solo Double cage at play tick 322 of 364 at tier three, so no hazard phase fits before its own cage bomb.
    /// </summary>
    private static readonly (string Fighter, ChannelId Goal, TwistId Twist, string Why)[] KitWaivers =
    {
        ("salamander", MayhemChannels.BreakOut, new(3), "leaves a solo Double cage at 322 of 364 play ticks"),
    };

    private static bool Waived(string fighter, PrimePairEntry pair, TwistId goal)
        => KitWaivers.Any(w => w.Fighter == fighter && w.Goal == pair.Goal && w.Twist == goal);

    [Fact]
    public void TheListStartsWithTheClassicAndSpotOverRings()
    {
        Assert.Equal(new[] { (MayhemChannels.BreakOut, MayhemChannels.Dodge), (MayhemChannels.Spot, MayhemChannels.Jump), (MayhemChannels.Smile, MayhemChannels.Jump) },
            PrimePairs.All.Select(p => (p.Goal, p.Hazard)));
        Assert.Equal(new[] { "mayhem.pair.break-out-dodge", "mayhem.pair.spot-jump", "mayhem.pair.smile-jump" }, PrimePairs.All.Select(p => p.CopyKey));
        Assert.Equal(140, PrimePairs.Find(PrimePairs.BreakOutDodge).HazardLeadIn);
        Assert.Empty(MayhemChannels.Find(MayhemChannels.PrimeTime).Twists);
    }

    [Fact]
    public void RegistryLoadRejectsRowsThatAreNotAGoalAndAnAdmittedHazard()
    {
        PrimePairEntry Row(byte id, ChannelId goal, ChannelId hazard, string key)
            => new(new(id), goal, hazard, key, new(90, 360), 0, (IMayhemChannel g, IMayhemChannel h, in CpuProbe probe) => MayhemCpuIntent.Wait);
        var good = Row(9, MayhemChannels.Spot, MayhemChannels.Dodge, "mayhem.pair.spot-dodge");
        Assert.Single(PrimePairs.Validated(new[] { good }));
        foreach (var bad in new[]
        {
            new[] { Row(9, MayhemChannels.Spot, MayhemChannels.NoRunning, "mayhem.pair.spot-no-running") },
            new[] { Row(9, MayhemChannels.Dodge, MayhemChannels.Dodge, "mayhem.pair.dodge-dodge") },
            new[] { Row(9, MayhemChannels.PrimeTime, MayhemChannels.Dodge, "mayhem.pair.prime-time-dodge") },
            new[] { Row(9, MayhemChannels.Spot, MayhemChannels.Dodge, "mayhem.pair.spotlight-dodge") },
            new[] { good, Row(10, MayhemChannels.Spot, MayhemChannels.Dodge, "mayhem.pair.spot-dodge") },
            new[] { good, Row(9, MayhemChannels.BreakOut, MayhemChannels.Jump, "mayhem.pair.break-out-jump") },
        })
            Assert.Throws<InvalidOperationException>(() => PrimePairs.Validated(bad));
    }

    [Fact]
    public void AtMostOneChildOfEveryPairPublishesSolidsOrTargets()
    {
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        var arena = MayhemWorld.Arena();
        var seats = Enumerable.Range(0, 4).Select(i =>
        {
            var spawn = MatchFormat.MankiMayhem.StartFor(arena, i, 4);
            return new ChannelSeat((byte)i, (ulong)i + 1, new MayhemPose(spawn.X, .85f, spawn.Z, spawn.Yaw), .4f, 1.7f);
        }).ToArray();
        bool PublishesObjects(ChannelId id) => MayhemChannels.Find(id).Twists.Any(t =>
        {
            var scene = MayhemChannels.Find(id).Create(new ChannelSetup(new SoloAiring(new(id, t.Id, 5)), 0, 0, SpeedTier.Three,
                100, 190, 490, stage, seats)).Observe();
            return scene.Solids.Count > 0 || scene.Targets.Count > 0;
        });
        Assert.True(PublishesObjects(MayhemChannels.BreakOut));
        Assert.True(PublishesObjects(MayhemChannels.BreakBoards));
        foreach (var pair in PrimePairs.All)
            Assert.False(PublishesObjects(pair.Goal) && PublishesObjects(pair.Hazard), $"{pair.CopyKey} has two object producers");
    }

    [Fact]
    public void HostContactsRouteBySourceLane()
    {
        var (sim, episode) = MayhemWorld.Remix(PrimePairs.SpotJump, new(1), new(1), count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        void Body(byte lane) => sim.MayhemShow!.Contact(new MayhemContact(MayhemContactKind.HazardBody, lane, 0,
            ServerSimulation.MayhemHostEntityId, 1, 0, MayhemHazards.Activation(episode, lane, 0)), sim.CurrentTick, sim);
        Body(0);
        Assert.Null(MayhemWorld.View(sim).Contestants[0].Failure);
        Body(1);
        Assert.Equal((FailCause.Hazard, SplatStyle.Flattened), (MayhemWorld.View(sim).Contestants[0].Failure!.Value.Cause,
            MayhemWorld.View(sim).Contestants[0].Failure!.Value.Style));
    }

    [Fact]
    public void ObjectHitsGoToTheWallsOwnerAndABlastWrecksItOnce()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.PrimeTime, SpeedTier.Three, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        var show = sim.MayhemShow!;
        var wall = MayhemWorld.View(sim).Scene.Solids.First(w => w.CageEntityId == 1 && w.HitsLeft > 1);
        int Left(ulong id) => MayhemWorld.View(sim).Scene.Solids.Single(w => w.EntityId == id).HitsLeft;
        show.Contact(new MayhemContact(MayhemContactKind.SolidHit, 1, 0, 1, wall.EntityId, -1, 901), sim.CurrentTick, sim);
        Assert.Equal(wall.HitsLeft - 1, Left(wall.EntityId));
        int before = show.Events.Count;
        show.Contact(new MayhemContact(MayhemContactKind.SolidHit, 0, 0, 1, MayhemEvent.FirstTargetEntityId, -1, 902), sim.CurrentTick, sim);
        Assert.Equal(before, show.Events.Count);
        show.Contact(new MayhemContact(MayhemContactKind.HostBlast, 0, 0, ServerSimulation.MayhemHostEntityId, wall.EntityId, -1,
            MayhemHazards.Activation(episode, 0, 0)), sim.CurrentTick, sim);
        Assert.Equal(0, Left(wall.EntityId));
        Assert.Single(show.Events.Skip(before), e => e.Kind == MayhemEventKind.SolidBroken && e.Subject == wall.EntityId);

        var (spot, spotEpisode) = MayhemWorld.Remix(PrimePairs.SpotJump, new(1), new(1), count: 2);
        MayhemDirectorTests.RunTo(spot, MayhemWorld.Plan(spot).PlayStart(spotEpisode) + 2);
        int start = spot.MayhemShow!.Events.Count;
        spot.MayhemShow.Contact(new MayhemContact(MayhemContactKind.SolidHit, 0, 0, 1, RuntimeSolidSet.FirstEntityId, -1, 903), spot.CurrentTick, spot);
        Assert.Equal(start, spot.MayhemShow.Events.Count);
    }

    [Fact]
    public void TheViewShowsThePairBothTwistsAndBothChildrensCues()
    {
        var (sim, episode) = MayhemWorld.Remix(PrimePairs.SpotJump, new(3), new(2));
        MayhemWorld.Step(sim);
        var view = MayhemWorld.View(sim);
        var remix = Assert.IsType<RemixAiring>(view.Airing);
        Assert.Equal(MayhemChannels.PrimeTime, view.Channel!.Id);
        Assert.Equal("mayhem.pair.spot-jump", remix.PairCopyKey);
        Assert.Equal("mayhem.twist.spot.musical", remix.GoalTwistCopyKey);
        Assert.Equal("mayhem.twist.jump.crossfire", remix.HazardTwistCopyKey);
        Assert.All(view.Scene.Zones, z => Assert.Equal(0, z.Lane));
        Assert.All(view.Scene.Rings, r => Assert.Equal(1, r.Lane));
        Assert.NotEmpty(view.Scene.Zones);
        Assert.NotEmpty(view.Scene.Rings);
        Assert.Equal(MayhemWorld.Plan(sim).PlayStart(episode) + JumpTuning.FirstSlamTicks, view.Scene.Rings.Min(r => r.SlamTick));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BombRainInsideTheClassicDropsSeveralRowsWithAMovingGap(byte goalTwist)
    {
        var (sim, episode) = MayhemWorld.Remix(PrimePairs.BreakOutDodge, new(goalTwist), new(2));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        var rows = MayhemWorld.View(sim).Scene.Bombs.Where(b => b.Lane == 1).GroupBy(b => b.LaunchTick).OrderBy(g => g.Key).ToArray();
        Assert.True(rows.Length >= 2, $"{rows.Length} row(s) of Bomb rain");
        var gaps = rows.Select(row => Enumerable.Range(0, DodgeTuning.RainColumns).Single(column => row.All(b =>
            MathF.Abs(b.Landing.X - (MayhemMath.CenterX(stage) + (column - DodgeTuning.RainColumns / 2) * DodgeTuning.RainSpacing)) > .01f))).ToArray();
        for (int i = 1; i < gaps.Length; i++) Assert.NotEqual(gaps[i - 1], gaps[i]);
        Assert.All(rows, row => Assert.True(row.Key + DodgeTuning.FlightTicks[(int)SpeedTier.Three] + MayhemHazardTuning.BlastTicks + 1
            < MayhemWorld.Plan(sim).Deadline(episode), "a row outlives the deadline"));
    }

    [Fact]
    public void RestoreRejectsAChildStateOnTheWrongLane()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.PrimeTime, SpeedTier.Three, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 5);
        var w = new SnapshotWriter(); sim.MayhemShow!.WriteSnapshot(w);
        var bytes = w.ToArray();
        int goalLane = 4 + 1 + 16 + 2 + 2 * (4 + 1 + 16) + 2 + 1;
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        new MayhemShow(stage).ReadSnapshot(new SnapshotReader(bytes), new ulong[] { 1, 2 }, sim.CurrentTick);
        bytes[goalLane] = 1;
        Assert.Throws<InvalidDataException>(() => new MayhemShow(stage).ReadSnapshot(new SnapshotReader(bytes), new ulong[] { 1, 2 }, sim.CurrentTick));
    }

    private static IEnumerable<(uint Seed, int Episode)> CertificationSeeds(PrimePairEntry pair, TwistId goal, TwistId hazard)
    {
        if (pair.Id.Value > 2) return MayhemWorld.SeedsWithRemix(pair.Id, goal, hazard, SweepSeeds);
        return File.ReadLines(Path.Combine(MayhemWorld.Root, "tests/Shared.Tests/Fixtures/mayhem-u3-certification.csv")).Skip(1)
            .Select(line => line.Split(','))
            .Where(p => byte.Parse(p[0]) == pair.Id.Value && byte.Parse(p[1]) == goal.Value && byte.Parse(p[2]) == hazard.Value)
            .Select(p => (uint.Parse(p[3]), int.Parse(p[4])));
    }

    private static RemixAiring Draw(PrimePairEntry pair, TwistId goal, TwistId hazard, uint seed, int episode)
        => new(pair.Id, new(pair.Goal, goal, MayhemMath.ChannelSeed(seed, episode, 0)),
            new(pair.Hazard, hazard, MayhemMath.ChannelSeed(seed, episode, 1)));

    private sealed record Run(int Passed, int Total, int HazardFailures, string? Problem);

    /// <summary>One remix airing with careful CPUs: no slips, the Hard reaction delay.</summary>
    private static Run Careful(PrimePairEntry pair, uint seed, int episode, string[] roster, int count = 4, RemixAiring? pin = null)
    {
        var sim = MayhemWorld.NewShow(seed, count: count, roster: roster);
        if (pin != null) MayhemWorld.Pin(sim, episode, pin);
        MayhemWorld.SkipToCall(sim, episode);
        MayhemWorld.Step(sim);
        uint deadline = MayhemWorld.Plan(sim).Deadline(episode);
        var scene = MayhemWorld.View(sim).Scene;
        string? late = scene.Bombs.Any(b => b.ImpactTick + MayhemHazardTuning.BlastTicks + 1 >= deadline)
            || scene.Rings.Any(r => r.SlamTick + r.LifetimeTicks >= deadline) ? "a threat outlives the deadline" : null;
        var cpus = sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(CarefulDelay, 0));
        var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
        var rows = MayhemWorld.View(sim).Contestants;
        int hazard = rows.Count(r => r.Failure is { Cause: FailCause.Hazard or FailCause.Fell or FailCause.Glitched });
        bool contest = MayhemChannels.Find(pair.Goal).Contest == ChannelContest.Shared ? passed.All(p => p) : passed.Any(p => p);
        string? problem = late ?? (hazard > 0 || !contest
            ? string.Join(", ", rows.Where(r => r.Failure != null).Select(r => $"{roster[r.Seat % roster.Length]} {r.Failure!.Value.Cause}@{r.Failure.Value.Tick - MayhemWorld.Plan(sim).PlayStart(episode)}"))
            : null);
        return new Run(passed.Count(p => p), passed.Length, hazard, problem);
    }

    /// <summary>Every classic case the interrogate saw toast a careful CPU: seed, boss episode, contestants and goal twist, all with DODGE! Aimed.</summary>
    public static TheoryData<uint, int, int, byte> FoundClassicCases() => new()
    {
        { 19, 9, 2, 2 }, { 51, 4, 2, 2 }, { 55, 14, 2, 1 }, { 68, 14, 2, 3 }, { 68, 14, 3, 3 },
        { 68, 14, 4, 3 }, { 69, 14, 2, 2 }, { 81, 9, 2, 3 }, { 98, 14, 2, 2 }, { 124, 9, 4, 3 },
    };

    /// <summary>Each case airs as it did when found, whatever the plan now draws for that seed.</summary>
    [Theory]
    [MemberData(nameof(FoundClassicCases))]
    public void CarefulCpusSurviveEveryClassicCaseTheInterrogateFound(uint seed, int episode, int count, byte goalTwist)
    {
        var pin = MayhemWorld.RemixAt(seed, episode, PrimePairs.BreakOutDodge, new(goalTwist), new(1));
        var run = Careful(PrimePairs.Find(PrimePairs.BreakOutDodge), seed, episode, MayhemWorld.Four, count, pin);
        Assert.True(run.Problem == null, $"{pin.GoalTwistCopyKey} + {pin.HazardTwistCopyKey}, {count} seats: {run.Problem}");
    }

    /// <summary>
    /// Not fixed, and past the sweep's seeds. Wibou flees one aimed bomb, the next covers that point, and the fresh way out
    /// turns back across the first landing. Solo DODGE! Aimed passes these seeds. This fails once the CPU survives them;
    /// then move the rows to <see cref="FoundClassicCases"/>.
    /// </summary>
    [Theory]
    [InlineData(107u, 4)]
    [InlineData(113u, 14)]
    public void TheSqueezeBetweenTwoAimedBombsStillToastsWibou(uint seed, int episode)
    {
        var pin = MayhemWorld.RemixAt(seed, episode, PrimePairs.BreakOutDodge, new(3), new(1));
        Assert.Equal(1, Careful(PrimePairs.Find(PrimePairs.BreakOutDodge), seed, episode, MayhemWorld.Four, 3, pin).HazardFailures);
    }

    public static TheoryData<byte, int> PairsByCast()
    {
        var data = new TheoryData<byte, int>();
        foreach (var pair in PrimePairs.All)
            foreach (int count in new[] { 2, 3, 4 })
                data.Add(pair.Id.Value, count);
        return data;
    }

    /// <summary>
    /// The evidence gate: every goal twist with every hazard twist at tier three, with two, three and four contestants on
    /// <see cref="SweepSeeds"/> seeds each with the fixed roster, and at four seats once per cooked roster fighter.
    /// Careful CPUs never fail to a hazard and the goal's contest holds.
    /// </summary>
    [Theory]
    [MemberData(nameof(PairsByCast))]
    public void CarefulCpusPassEveryTwistCombinationOfTheRow(byte pairId, int count)
    {
        var pair = PrimePairs.Find(new(pairId));
        var table = new StringBuilder($"### {pair.CopyKey}, {count} contestants\n\n| Goal twist | Hazard twist | Fixed roster passed | Hazard failures |{(count == 4 ? " Roster fighters passed | Hazard failures |" : "")}\n| --- | --- | ---: | ---: |{(count == 4 ? " ---: | ---: |" : "")}\n");
        var problems = new List<string>();
        var waived = new List<string>();
        var fighters = count == 4 ? ((IEnumerable<object[]>)RosterFighters.All()).Select(row => (string)row[0]).ToArray() : Array.Empty<string>();
        foreach (var (_, goal, hazard) in MayhemWorld.RemixCombinations().Where(c => c.Pair.Id == pair.Id))
        {
            var seeds = CertificationSeeds(pair, goal, hazard).ToArray();
            Assert.Equal(SweepSeeds, seeds.Length);
            var fixedRuns = seeds.Select(s => (Seed: $"{s.Seed} episode {s.Episode + 1}", Run: Careful(pair, s.Seed, s.Episode, MayhemWorld.Four, count, Draw(pair, goal, hazard, s.Seed, s.Episode)))).ToArray();
            var fighterRuns = fighters.Select(f => (Fighter: f, Run: Careful(pair, seeds[0].Seed, seeds[0].Episode, new[] { f }, pin: Draw(pair, goal, hazard, seeds[0].Seed, seeds[0].Episode)))).ToArray();
            foreach (var (seed, run) in fixedRuns.Where(r => r.Run.Problem != null))
                problems.Add($"{Combo(pair, goal, hazard)} seed {seed}: {run.Problem}");
            foreach (var (fighter, run) in fighterRuns.Where(r => r.Run.Problem != null))
                (Waived(fighter, pair, goal) ? waived : problems).Add($"{Combo(pair, goal, hazard)} all {fighter}: {run.Problem}");
            table.AppendLine($"| {Twist(pair.Goal, goal)} | {Twist(pair.Hazard, hazard)} | {fixedRuns.Sum(r => r.Run.Passed)}/{fixedRuns.Sum(r => r.Run.Total)} | {fixedRuns.Sum(r => r.Run.HazardFailures)} |"
                + (count == 4 ? $" {fighterRuns.Sum(r => r.Run.Passed)}/{fighterRuns.Sum(r => r.Run.Total)} | {fighterRuns.Sum(r => r.Run.HazardFailures)} |" : ""));
        }
        if (count == 4)
            foreach (var waiver in KitWaivers.Where(w => w.Goal == pair.Goal && !waived.Any(line => line.Contains($"all {w.Fighter}:") && line.Contains(Twist(w.Goal, w.Twist)))))
                problems.Add($"waiver for {waiver.Fighter} on {Twist(waiver.Goal, waiver.Twist)} is no longer needed");
        table.AppendLine($"\nFixed roster {string.Join(", ", MayhemWorld.Four.Take(count))}, {SweepSeeds} seeds per combination."
            + (count == 4 ? $" Then each of {string.Join(", ", fighters)} as all four seats on the first seed." : "")
            + $" Careful CPUs: no slips, {CarefulDelay}-tick reaction delay, tier three.");
        if (waived.Count > 0) table.AppendLine("\nWaived (kit too slow for the goal twist):\n\n" + string.Join("\n", waived.Select(p => $"- {p}")));
        if (problems.Count > 0) table.AppendLine("\nProblems:\n\n" + string.Join("\n", problems.Select(p => $"- {p}")));
        string text = table.ToString();
        _output.WriteLine(text);
        string dir = Path.Combine(MayhemWorld.Root, "tmp", "mayhem-remix");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"certification-{pair.CopyKey}-{count}.md"), text);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void SlippingEasyCpusFailSomeRemixAirings()
    {
        foreach (var pair in PrimePairs.All)
        {
            int failed = 0, total = 0;
            foreach (var (_, goal, hazard) in MayhemWorld.RemixCombinations().Where(c => c.Pair.Id == pair.Id).Take(3))
            {
                var (sim, episode) = MayhemWorld.Remix(pair.Id, goal, hazard, roster: MayhemWorld.Four);
                var cpus = sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(CpuDifficulty.Easy));
                var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
                failed += passed.Count(p => !p);
                total += passed.Length;
            }
            _output.WriteLine($"{pair.CopyKey}: Easy CPUs failed {failed}/{total}");
            Assert.True(failed > 0, $"{pair.CopyKey}: Easy CPUs never fail");
        }
    }

    private static string Twist(ChannelId channel, TwistId twist) => MayhemChannels.Find(channel).Twist(twist).CopyKey;
    private static string Combo(PrimePairEntry pair, TwistId goal, TwistId hazard) => $"{pair.CopyKey} {Twist(pair.Goal, goal)} + {Twist(pair.Hazard, hazard)}";
}
