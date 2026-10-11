using System;
using System.IO;
using System.Linq;
using Xunit;
using SlopArena.MoveDataReport;

namespace SlopArena.Shared.Tests;

public sealed class ComboRouteReportTests
{
    private const ulong AttackerId = ComboRouteReport.AttackerId;
    private const ulong DefenderId = ComboRouteReport.DefenderId;

    [Theory]
    [InlineData("base", 9)]
    [InlineData("floor30", 25)]
    public void Execute_UsesContextTuningForHitstopLaunch(string profile, ushort expectedStun)
    {
        var kit = CreateKit((0, HitTimeline(new[] { 0 }, duration: 20, baseKnockback: 20f)));
        var context = ContextFor(kit, TuningProfiles.Get(profile));
        var replay = CreateReplay(context, 16, 101.6f, (0, "ground.1"));

        var outcome = ComboRouteReport.Execute(context, replay, captureFrames: true).Outcome;

        Assert.Single(outcome.Contacts);
        Assert.Equal(expectedStun, outcome.Frames.First(frame => frame.Defender.Hitstun > 0).Defender.Hitstun);
    }

    [Fact]
    public void Execute_RejectsReplayFromDifferentTuningContext()
    {
        var kit = CreateKit((0, HitTimeline(new[] { 0 }, duration: 20, baseKnockback: 20f)));
        var baseline = ContextFor(kit);
        var candidate = ContextFor(kit, TuningProfiles.Get("floor30"));
        var replay = CreateReplay(baseline, 16, 101.6f, (0, "ground.1"));
        string expected = ComboRouteReport.OutcomeHash(ComboRouteReport.Execute(baseline, replay).Outcome);

        Assert.Throws<InvalidDataException>(() => ComboRouteReport.Execute(candidate, replay));
        Assert.Equal(expected, ComboRouteReport.OutcomeHash(ComboRouteReport.Execute(baseline, replay).Outcome));
    }

    [Fact]
    public void Execute_ReportsMovingNormalActivationAndItsContactProvenance()
    {
        var kit = CreateKit((0, HitTimeline(new[] { 0 }, duration: 30, radius: 2.5f,
            moving: true)));
        var context = ContextFor(kit);
        var replay = CreateReplay(context, 30, 102.5f, (0, "ground.1"));
        var moving = replay.InitialAttacker;
        moving.VZ = 6f;
        replay.InitialAttacker = moving;

        var outcome = ComboRouteReport.Execute(context, replay).Outcome;

        Assert.True(outcome.RouteSucceeded);
        var action = Assert.Single(outcome.Actions.Where(x => x.EntityId == AttackerId));
        Assert.Equal("accepted", action.Status);
        Assert.Equal("ground.1", action.ActualSlot);
        Assert.True(action.ActivationId > 0);
        Assert.Equal(1, action.Contacts);
        var contact = Assert.Single(outcome.Contacts);
        Assert.Equal(AttackerId, contact.Attacker);
        Assert.Equal(DefenderId, contact.Defender);
        Assert.Equal(action.ActivationId, contact.ActivationId);
        Assert.Equal("ground.1", contact.Slot);
        Assert.False(contact.Blocked);
        Assert.True(contact.Damage > 0f);
        Assert.True(outcome.FinalAttacker.Z > replay.InitialAttacker.PZ,
            "The authored lunge must move the attacker through the real simulation.");
    }

    [Fact]
    public void Execute_GroundedInputCannotSatisfyAnIntendedAerialAction()
    {
        var kit = CreateKit(
            (0, HitTimeline(new[] { 0 }, duration: 20, damage: 4f)),
            (8, HitTimeline(new[] { 0 }, duration: 20, damage: 9f)));
        var context = ContextFor(kit);
        var replay = CreateReplay(context, 24, 101.6f, (0, "air.1"));

        var outcome = ComboRouteReport.Execute(context, replay).Outcome;

        var action = Assert.Single(outcome.Actions.Where(x => x.EntityId == AttackerId));
        Assert.Equal("variant-mismatch", action.Status);
        Assert.Equal("air.1", action.ExpectedSlot);
        Assert.Equal("ground.1", action.ActualSlot);
        Assert.False(outcome.RouteSucceeded);
        var contact = Assert.Single(outcome.Contacts);
        Assert.Equal("ground.1", contact.Slot);
        Assert.Equal(4f, contact.Damage);
    }

    [Fact]
    public void Execute_RejectedFollowUpCannotBorrowLaterContactsFromMultiHitStarter()
    {
        var kit = CreateKit(
            (0, HitTimeline(new[] { 0, 8 }, duration: 18, stunTicks: 0)),
            (1, HitTimeline(new[] { 0 }, duration: 4, stunTicks: 0)));
        var context = ContextFor(kit);
        var replay = CreateReplay(context, 28, 101.6f,
            (0, "ground.1"), (1, "ground.2"));

        var outcome = ComboRouteReport.Execute(context, replay).Outcome;

        var actions = outcome.Actions.Where(x => x.EntityId == AttackerId).OrderBy(x => x.Tick).ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Equal("accepted", actions[0].Status);
        Assert.Equal("rejected", actions[1].Status);
        Assert.Equal(0ul, actions[1].ActivationId);
        Assert.Equal(0, actions[1].Contacts);
        Assert.Equal(2, actions[0].Contacts);
        Assert.NotEqual(0ul, actions[0].ActivationId);
        Assert.Equal(2, outcome.Contacts.Count);
        Assert.All(outcome.Contacts, contact => Assert.Equal(actions[0].ActivationId, contact.ActivationId));
        Assert.Equal(1, outcome.ConnectedActions);
        Assert.False(outcome.RouteSucceeded);
    }

    [Theory]
    [InlineData(true, "true", false)]
    [InlineData(false, "pressure", true)]
    public void Execute_SeparatesUninterruptedTrueFromActionableGapPressure(
        bool firstHitLocks, string verdict, bool expectGap)
    {
        var kit = CreateKit(
            (0, HitTimeline(new[] { 0 }, duration: 4, stunTicks: firstHitLocks ? (ushort)1 : (ushort)0,
                baseKnockback: 60f)),
            (1, HitTimeline(new[] { 0 }, duration: 4, stunTicks: 0, radius: 8f)));
        var context = ContextFor(kit);
        const int followupTick = 20;
        var replay = CreateReplay(context, 26, 101.6f,
            (0, "ground.1"), (followupTick, "ground.2"));

        var outcome = ComboRouteReport.Execute(context, replay).Outcome;

        var actions = outcome.Actions.Where(x => x.EntityId == AttackerId).OrderBy(x => x.Tick).ToArray();
        Assert.Equal(2, actions.Length);
        Assert.All(actions, action => Assert.Equal("accepted", action.Status));
        Assert.All(actions, action => Assert.True(action.ActivationId > 0));
        Assert.NotEqual(actions[0].ActivationId, actions[1].ActivationId);
        Assert.Equal(1, actions[0].Contacts);
        Assert.Equal(1, actions[1].Contacts);
        Assert.Equal(verdict, outcome.Verdict);
        Assert.Equal(expectGap, outcome.GapTicks.Count > 0);
        if (expectGap)
        {
            Assert.Contains(followupTick, outcome.DefenderActionTicks);
            Assert.Contains(followupTick, outcome.GapTicks);
        }
        else
        {
            Assert.Empty(outcome.GapTicks);
        }
    }

    [Fact]
    public void ReplayJson_RetainsBothCompleteStreamsAndReplaysTheSameOutcome()
    {
        var kit = CreateKit((0, HitTimeline(new[] { 0 }, duration: 20, stunTicks: 40)));
        var context = ContextFor(kit);
        var attackerInputs = new InputState[16];
        var defenderInputs = new InputState[16];
        attackerInputs[6] = DetailedInput(moveX: 0.25f, moveY: -0.5f);
        defenderInputs[6] = DetailedInput(moveX: -0.375f, moveY: 0.625f);
        var replay = CreateReplay(context, 16, 101.6f, new[] { (0, "ground.1") },
            attackerInputs, defenderInputs);
        var expected = ComboRouteReport.Execute(context, replay).Outcome;
        replay.ExpectedOutcomeHash = ComboRouteReport.OutcomeHash(expected);
        string path = WriteTempReplay(ComboRouteReport.ToJson(replay));
        try
        {
            var restored = ComboRouteReport.ReadReplay(path);
            Assert.Equal(replay.AttackerInputs, restored.AttackerInputs);
            Assert.Equal(replay.DefenderInputs, restored.DefenderInputs);

            var actual = ComboRouteReport.Execute(context, restored).Outcome;
            Assert.Equal(replay.ExpectedOutcomeHash, ComboRouteReport.OutcomeHash(actual));
            Assert.Equal(expected.Actions.Select(x => (x.EntityId, x.Tick, x.ExpectedSlot, x.ActualSlot,
                x.ActivationId, x.Status, x.Contacts, x.Damage)),
                actual.Actions.Select(x => (x.EntityId, x.Tick, x.ExpectedSlot, x.ActualSlot,
                    x.ActivationId, x.Status, x.Contacts, x.Damage)));
            Assert.Equal(expected.Contacts, actual.Contacts);
            Assert.Equal(expected.DefenderActionTicks, actual.DefenderActionTicks);
            Assert.Equal(expected.GapTicks, actual.GapTicks);
            Assert.Equal(expected.Damage, actual.Damage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplayJson_RejectsContentIdentityMismatchBeforeSimulationOutput()
    {
        var kit = CreateKit((0, HitTimeline(new[] { 0 }, duration: 20)));
        var context = ContextFor(kit);
        var replay = CreateReplay(context, 8, 101.6f, (0, "ground.1"));
        replay.AttackerContent = replay.AttackerContent with
        {
            PackageId = replay.AttackerContent.PackageId + "-mismatch",
        };
        string path = WriteTempReplay(ComboRouteReport.ToJson(replay));
        try
        {
            var restored = ComboRouteReport.ReadReplay(path);
            Assert.Throws<InvalidDataException>(() => ComboRouteReport.Execute(context, restored));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Execute_GeneratedDiPreservesBothFightersCompleteInputStreams()
    {
        var kit = CreateKit((0, HitTimeline(new[] { 0 }, duration: 20, stunTicks: 60)));
        var context = ContextFor(kit);
        var attackerInputs = new InputState[8];
        var defenderInputs = new InputState[8];
        defenderInputs[1] = DetailedInput(moveX: 0.25f, moveY: -0.5f);
        for (int tick = 2; tick < defenderInputs.Length; tick++)
            defenderInputs[tick] = DetailedInput(moveX: -0.375f, moveY: 0.625f);
        var replay = CreateReplay(context, 8, 101.6f, new[] { (0, "ground.1") },
            attackerInputs, defenderInputs);
        var originalAttacker = (InputState[])replay.AttackerInputs.Clone();
        var originalDefender = (InputState[])replay.DefenderInputs.Clone();

        var execution = ComboRouteReport.Execute(context, replay, generatedDi: "away", captureFrames: true);
        var generated = Assert.IsType<InputState[]>(execution.GeneratedDefenderInputs);

        Assert.Equal(originalAttacker, replay.AttackerInputs);
        Assert.Equal(originalDefender, replay.DefenderInputs);
        Assert.Equal(generated.Length, execution.Outcome.Frames.Count);
        for (int tick = 0; tick < generated.Length; tick++)
        {
            Assert.Equal(WithoutMovement(originalDefender[tick]), WithoutMovement(generated[tick]));
            Assert.Equal(0f, generated[tick].MoveX);
            bool influenced = tick > 0
                && (execution.Outcome.Frames[tick - 1].Defender.Hitstop > 0
                    || execution.Outcome.Frames[tick - 1].Defender.Hitstun > 0);
            Assert.Equal(influenced ? 1f : 0f, generated[tick].MoveY);
        }
    }

    [Fact]
    public void Search_ReportsCandidateAndMeasurementBudgetExhaustionWithReplayableWitness()
    {
        var context = ContextFor(SearchKit());
        var options = SearchOptionsFor(budget: 1, measurementBudget: 1, diDirections: new[] { "away" });

        var report = ComboRouteReport.Search(context, options);

        Assert.Equal(1, report.CandidatesRun);
        Assert.True(report.BudgetExhausted);
        Assert.Equal(1, report.MeasurementsRun);
        Assert.True(report.MeasurementBudgetExhausted);
        var witness = Assert.Single(report.Witnesses);
        Assert.True(witness.Outcome.RouteSucceeded);
        Assert.NotNull(witness.Replay.ExpectedOutcomeHash);
        var replayed = ComboRouteReport.Execute(context, witness.Replay).Outcome;
        Assert.Equal(witness.Replay.ExpectedOutcomeHash, ComboRouteReport.OutcomeHash(replayed));
    }

    [Fact]
    public void Search_TimingAndFixedVersusConditionedDiUseMeasuredSimulationStreams()
    {
        var context = ContextFor(SearchKit());
        var options = SearchOptionsFor(budget: 4, measurementBudget: 8,
            diDirections: new[] { "away", "neutral" });

        var report = ComboRouteReport.Search(context, options);

        Assert.InRange(report.CandidatesRun, 1, options.Budget);
        Assert.InRange(report.MeasurementsRun, 1, options.MeasurementBudget);
        var witness = Assert.Single(report.Witnesses);
        Assert.True(witness.Outcome.RouteSucceeded);
        Assert.All(witness.Timing, measurement =>
        {
            Assert.True(measurement.Complete);
            var sample = Assert.Single(measurement.Samples);
            Assert.Equal(measurement.BaselineTick, sample.Tick);
            Assert.True(sample.Success);
            Assert.True(sample.Damage > 0f);
            Assert.True(measurement.BoundaryClipped);
        });

        var fixedRoutes = report.FixedRouteDi.ToDictionary(comparison => comparison.Di);
        Assert.Equal(new[] { "away", "neutral" }, fixedRoutes.Keys.OrderBy(x => x, StringComparer.Ordinal));
        var away = Assert.IsType<ComboRouteReport.Replay>(fixedRoutes["away"].Replay);
        var neutral = Assert.IsType<ComboRouteReport.Replay>(fixedRoutes["neutral"].Replay);
        Assert.True(fixedRoutes["away"].Complete);
        Assert.True(fixedRoutes["neutral"].Complete);
        Assert.Equal(witness.Replay.AttackerInputs, away.AttackerInputs);
        Assert.Equal(witness.Replay.AttackerInputs, neutral.AttackerInputs);
        Assert.Contains(Enumerable.Range(0, away.DefenderInputs.Length),
            tick => away.DefenderInputs[tick].MoveY != neutral.DefenderInputs[tick].MoveY);
        Assert.Equal(options.DiDirections.OrderBy(x => x, StringComparer.Ordinal),
            report.DiConditionedBest.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains(report.DiConditionedBest.Values, id => id != null);
        foreach (var (direction, witnessId) in report.DiConditionedBest)
        {
            if (witnessId == null) continue;
            var selected = Assert.Single(report.Witnesses, candidate => candidate.Id == witnessId);
            Assert.Equal(direction, selected.Di);
        }
    }

    [Fact]
    public void Search_DelayingSameSlotFollowUpMeasuresItsOwnAcceptanceWindow()
    {
        var timeline = HitTimeline(new[] { 0 }, duration: 40, radius: 2.5f);
        var context = ContextFor(CreateKit((0, timeline), (8, timeline)));
        var options = SearchOptionsFor(1200, 200, new[] { "neutral" });
        options.Horizon = 150;
        options.TimingRadius = 2;
        var report = ComboRouteReport.Search(context, options);
        var witness = Assert.Single(report.Witnesses);
        int index = witness.Replay.Actions.FindIndex(1, action => action.Slot == witness.Replay.Actions[0].Slot);
        Assert.InRange(index, 1, witness.Replay.Actions.Count - 1);
        var window = Assert.Single(witness.Timing, measurement => measurement.ActionIndex == index);
        Assert.True(Assert.Single(window.Samples, sample => sample.Tick == window.BaselineTick).Success);
        Assert.False(Assert.Single(window.Samples, sample => sample.Tick == window.BaselineTick - 1).Success);
        Assert.Contains(window.Intervals, interval => interval.StartTick <= window.BaselineTick
            && interval.EndTick >= window.BaselineTick);
        Assert.DoesNotContain(window.Intervals, interval => interval.StartTick <= window.BaselineTick - 1
            && interval.EndTick >= window.BaselineTick - 1);
        var early = ComboRouteReport.CopyReplay(witness.Replay);
        early.AttackerInputs[window.BaselineTick].ActiveSlot = 0;
        early.AttackerInputs[window.BaselineTick - 1].ActiveSlot = ComboRouteReport.WireSlot(early.Actions[index].Slot);
        early.Actions[index].Tick--;
        var outcome = ComboRouteReport.Execute(context, early).Outcome;
        Assert.True(outcome.Actions[0].Contacts > 0);
        var rejected = Assert.Single(outcome.Actions, action => action.Tick == early.Actions[index].Tick);
        Assert.Equal("rejected", rejected.Status);
        Assert.Equal(0, rejected.Contacts);
        Assert.False(outcome.RouteSucceeded);
    }

    [Fact]
    public void Command_InvalidSearchBudgetDoesNotCreateOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"combo-route-invalid-{Guid.NewGuid():N}");
        string output = Path.Combine(directory, "report.json");
        try
        {
            Assert.Equal(1, ComboRouteReport.Run(new[] { "--route-search", "--budget", "0", "--out", output }));
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Execute_ExplicitJumpFastFallAndLandingFollowUpRemainAuthoritative()
    {
        var timeline = HitTimeline(new[] { 0 }, duration: 20, radius: 3f, stunTicks: 0);
        var context = ContextFor(CreateKit((0, timeline), (8, timeline)));
        var baseline = CreateReplay(context, 150, 101.6f, (0, "ground.1"), (50, "air.1"));
        baseline.AttackerInputs[40].Jump = true;
        for (int tick = 40; tick < 48; tick++) baseline.AttackerInputs[tick].JumpHeld = true;
        var ordinary = ComboRouteReport.Execute(context, baseline, captureFrames: true).Outcome;
        Assert.True(ordinary.RouteSucceeded);
        int descending = ordinary.Frames.First(frame => frame.Tick > 50 && !frame.Attacker.Grounded
            && frame.Attacker.Vy < 0f && frame.Attacker.Hitstop == 0).Tick + 1;
        var fastFall = ComboRouteReport.CopyReplay(baseline);
        fastFall.AttackerInputs[descending].DownPressed = true;
        for (int tick = descending; tick < 150; tick++) fastFall.AttackerInputs[tick].Down = true;
        var falling = ComboRouteReport.Execute(context, fastFall).Outcome;
        Assert.Contains(descending, falling.FastFallTicks);
        int landing = falling.LandingTicks.First(tick => tick > descending);
        Assert.True(landing < ordinary.LandingTicks.First(tick => tick > descending));
        int follow = landing + 1;
        fastFall.AttackerInputs[follow].ActiveSlot = AbilitySlots.Slot1;
        fastFall.Actions.Add(new ComboRouteReport.PlannedAction { Tick = follow, Slot = "ground.1" });
        var outcome = ComboRouteReport.Execute(context, fastFall).Outcome;
        Assert.True(outcome.RouteSucceeded);
        var action = Assert.Single(outcome.Actions, attempt => attempt.Tick == follow);
        Assert.Equal("ground.1", action.ActualSlot);
        Assert.Equal(1, action.Contacts);
        Assert.Contains(outcome.Contacts, contact => contact.ActivationId == action.ActivationId);
    }

    [Fact]
    public void Replay_SelectsNonFirstWitnessAndRejectsUnknownSelection()
    {
        var context = ContextFor(CreateKit(
            (0, HitTimeline(new[] { 0 }, 20, damage: 4f)),
            (1, HitTimeline(new[] { 0 }, 20, damage: 9f))));
        var first = CreateReplay(context, 24, 101.6f, (0, "ground.1"));
        var second = CreateReplay(context, 24, 101.6f, (0, "ground.2"));
        first.Id = "first";
        second.Id = "second";
        var firstOutcome = ComboRouteReport.Execute(context, first).Outcome;
        var secondOutcome = ComboRouteReport.Execute(context, second).Outcome;
        first.ExpectedOutcomeHash = ComboRouteReport.OutcomeHash(firstOutcome);
        second.ExpectedOutcomeHash = ComboRouteReport.OutcomeHash(secondOutcome);
        var report = new ComboRouteReport.SearchReport
        {
            Witnesses = new()
            {
                new() { Id = first.Id, Replay = first, Outcome = firstOutcome },
                new() { Id = second.Id, Replay = second, Outcome = secondOutcome },
            },
        };
        string path = WriteTempReplay(ComboRouteReport.ToJson(report));
        try
        {
            var selected = ComboRouteReport.ReadReplay(path, "second");
            Assert.Equal("second", selected.Id);
            Assert.Equal(9f, ComboRouteReport.Execute(context, selected).Outcome.Damage);
            Assert.Throws<InvalidDataException>(() => ComboRouteReport.ReadReplay(path, "missing"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replay_StandaloneFormsRejectConflictingWitnessId(bool wrapped)
    {
        var context = ContextFor(CreateKit((0, HitTimeline(new[] { 0 }, 20))));
        var replay = CreateReplay(context, 24, 101.6f, (0, "ground.1"));
        replay.Id = "only-route";
        var outcome = ComboRouteReport.Execute(context, replay).Outcome;
        string json = wrapped ? ComboRouteReport.ToJson(new ComboRouteReport.ReplayReport { Replay = replay, Outcome = outcome })
            : ComboRouteReport.ToJson(replay);
        string path = WriteTempReplay(json);
        try
        {
            Assert.Equal("only-route", ComboRouteReport.ReadReplay(path, "only-route").Id);
            Assert.Throws<InvalidDataException>(() => ComboRouteReport.ReadReplay(path, "different-route"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Search_VisitsRequestedStartingPercentAndDistancePairs()
    {
        var context = ContextFor(CreateKit((0, HitTimeline(new[] { 0 }, 40, radius: 3f))));
        var options = SearchOptionsFor(400, 128, new[] { "neutral" });
        options.Percents = new[] { 0, 60 };
        options.Distances = new[] { 0.8f, 1.4f };
        options.Keep = 32;
        var report = ComboRouteReport.Search(context, options);
        foreach (int percent in options.Percents)
        foreach (float distance in options.Distances)
        {
            Assert.Contains(report.Witnesses, witness => witness.Replay.InitialDefender.DamagePercent == percent
                && MathF.Abs(witness.Replay.InitialDefender.PZ - witness.Replay.InitialAttacker.PZ - distance) < .0001f);
            Assert.Contains(report.ConditionAttempts, condition =>
            {
                var fields = condition.Key.Split(';');
                return fields[0] == "starter" && fields[1] == $"pct={percent}"
                    && MathF.Abs(float.Parse(fields[2]["distance=".Length..],
                        System.Globalization.CultureInfo.InvariantCulture) - distance) < .0001f;
            });
        }
    }

    private static MatchContentEntry SearchKit()
    {
        var timeline = HitTimeline(new[] { 0 }, duration: 40, stunTicks: 120, radius: 2.5f);
        return CreateKit(
            (0, timeline), (1, timeline), (2, timeline), (3, timeline),
            (8, timeline), (9, timeline), (10, timeline), (11, timeline));
    }

    private static ComboRouteReport.SearchOptions SearchOptionsFor(int budget, int measurementBudget,
        string[] diDirections)
        => new()
        {
            Budget = budget,
            MeasurementBudget = measurementBudget,
            Horizon = 30,
            Keep = 1,
            PrefixWidth = 1,
            TimingRadius = 0,
            Percents = new[] { 0 },
            Distances = new[] { 0.8f },
            DiDirections = diDirections,
        };

    private static MatchContentEntry CreateKit(params (int Index, CookedTimeline Timeline)[] authored)
    {
        var source = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var slots = Enumerable.Range(0, 16).Select(EmptySlot).ToArray();
        foreach (var (index, timeline) in authored)
            slots[index] = MakeSlot(index, timeline);
        var definition = TestHelpers.EngineDef;
        definition.Class = source.Definition.Class;
        definition.CookedSlots = slots;
        return new MatchContentEntry(source.Handle, source.LegacySelector, source.Identity,
            "route test fixture", definition);
    }

    private static CookedSlotDefinition EmptySlot(int index)
        => MakeSlot(index, new CookedTimeline(new[]
        {
            new CookedStage(60, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
        }));

    private static CookedSlotDefinition MakeSlot(int index, CookedTimeline timeline)
    {
        string id = index switch
        {
            0 => "ground.1", 1 => "ground.2", 2 => "ground.3", 3 => "ground.4",
            4 => "ground.A", 5 => "ground.E", 6 => "ground.R", 7 => "ground.F",
            8 => "air.1", 9 => "air.2", 10 => "air.3", 11 => "air.4",
            12 => "air.A", 13 => "air.E", 14 => "air.R", 15 => "air.F",
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        return new CookedSlotDefinition(index, id, index >= 8, "Route test move", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, timeline);
    }

    private static CookedTimeline HitTimeline(int[] hitTicks, ushort duration, ushort stunTicks = 20,
        float damage = 5f, float radius = 1.5f, bool moving = false, float baseKnockback = 0f)
    {
        var operations = new System.Collections.Generic.List<CookedTimelineOperation>();
        if (moving)
            operations.Add(new CookedForwardLungeOperation(0, AuthoringUnit.MetersPerSecond, 6f, 12));
        foreach (int tick in hitTicks)
            operations.Add(new CookedSpawnHitboxOperation((ushort)tick, AuthoringUnit.Meters, new CookedHitbox(
                AuthoringHitboxShape.Sphere, radius, 0f, 0f, 0.5f, 0f, 0f, 0f,
                null, null, damage, 0f, baseKnockback, 0f, stunTicks, 1, true, 0)));
        return new CookedTimeline(new[]
        {
            new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(), operations),
        });
    }

    private static ComboRouteReport.Context ContextFor(MatchContentEntry kit, CombatTuning? tuning = null)
        => new(kit, kit, TestHelpers.TestArena(), tuning);

    private static ComboRouteReport.Replay CreateReplay(ComboRouteReport.Context context, int horizon,
        float defenderZ, params (int Tick, string Slot)[] actions)
        => CreateReplay(context, horizon, defenderZ, actions, new InputState[horizon], new InputState[horizon]);

    private static ComboRouteReport.Replay CreateReplay(ComboRouteReport.Context context, int horizon,
        float defenderZ, (int Tick, string Slot)[] actions, InputState[] attackerInputs, InputState[] defenderInputs)
    {
        Assert.Equal(horizon, attackerInputs.Length);
        Assert.Equal(horizon, defenderInputs.Length);
        foreach (var action in actions)
        {
            var input = attackerInputs[action.Tick];
            input.ActiveSlot = ComboRouteReport.WireSlot(action.Slot);
            attackerInputs[action.Tick] = input;
        }
        return ComboRouteReport.CreateReplay(context, InitialState(context.Attacker, AttackerId, 100f, 100f, 0f),
            InitialState(context.Defender, DefenderId, 100f, defenderZ, MathF.PI), attackerInputs, defenderInputs,
            actions.Select(action => new ComboRouteReport.PlannedAction { Tick = action.Tick, Slot = action.Slot }));
    }

    private static CharacterState InitialState(MatchContentEntry entry, ulong entityId, float x, float z, float facing)
        => new()
        {
            EntityId = entityId,
            PX = x,
            PY = entry.Definition.CapsuleHeight * 0.5f,
            PZ = z,
            FacingYaw = facing,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = entry.Definition.Movement.MaxJumps,
            AirDodgesLeft = 1,
        };

    private static InputState DetailedInput(float moveX, float moveY)
        => new()
        {
            Up = true, Down = true, DownPressed = true, Left = true, Right = true,
            Jump = true, Dash = true, Burst = true, ShieldHeld = true, ShieldPressed = true,
            GrabPressed = true, JumpHeld = true, FaceToCamera = true, ToggleLock = true,
            RetargetPressed = true, LockMode = TargetLockMode.OnHit, MoveX = moveX, MoveY = moveY,
            IsAiming = true, FacingYaw = 1234, AimYaw = -4321, AimDistance = 789,
            AimPitch = 456, TargetEntityId = 17, WarpTargetX = 12.5f, WarpTargetZ = -7.25f,
            WarpSpeed = 4.5f, WarpAttackRange = 2.25f,
        };

    private static InputState WithoutMovement(InputState input)
    {
        input.MoveX = 0f;
        input.MoveY = 0f;
        return input;
    }

    private static string WriteTempReplay(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"combo-route-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
