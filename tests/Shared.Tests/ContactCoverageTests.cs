using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.MoveDataReport;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ContactCoverageTests
{
    private static readonly ArenaDefinition Arena = Program.NoRespawn(Program.BuildArena());

    [Fact]
    public void GroundOne_ReportsSyntheticContactAndDistantMiss()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var slot));
        var context = ContactCoverageReport.PrepareContext(entry, entry, 1f, Arena);

        var hit = ContactCoverageReport.RunSample(context, slot, new("passive-low", 0f, 0f, 0));
        var miss = ContactCoverageReport.RunSample(context, slot, new("passive-low", 4f, 6f, 0));
        var oracleHit = RunOracle(entry, entry, 0f, 0f, slot);
        var oracleMiss = RunOracle(entry, entry, 4f, 6f, slot);

        Assert.Equal("hit", hit.Outcome);
        Assert.NotNull(hit.FirstContact);
        Assert.True(hit.FirstContact!.Tick >= 0);
        Assert.Equal(oracleHit.Tick, hit.FirstContact.Tick);
        Assert.Equal(oracleHit.Damage, hit.FirstContact.Damage);
        Assert.Equal("miss", miss.Outcome);
        Assert.Equal(-1, oracleMiss.Tick);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Wibou)]
    public void AuthoredGroundOne_ReportMatchesSimulationWithoutPinningTuning(CharacterClass character)
    {
        var entry = BuiltInContentResolver.Resolve(character);
        var victim = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var slot));
        var context = ContactCoverageReport.PrepareContext(entry, victim, 1f, Arena);
        foreach (var (x, z) in new[] { (0f, 0f), (4f, 6f) })
        {
            var sample = ContactCoverageReport.RunSample(context, slot, new("passive-low", x, z, 0));
            var oracle = RunOracle(entry, victim, x, z, slot);
            Assert.Equal(oracle.Tick < 0 ? "miss" : "hit", sample.Outcome);
            if (oracle.Tick < 0)
                Assert.Null(sample.FirstContact);
            else
            {
                Assert.NotNull(sample.FirstContact);
                Assert.Equal(oracle.Tick, sample.FirstContact!.Tick);
                Assert.Equal(oracle.Damage, sample.FirstContact.Damage);
            }
        }
    }

    [Fact]
    public void AirOne_DoesNotSubstituteGroundMoveAfterLanding()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("air.1", out var slot));
        var context = ContactCoverageReport.PrepareContext(entry, entry, 1f, Arena);
        var accepted = ContactCoverageReport.RunSample(context, slot, new("passive-low", 0f, 0f, 0));
        var delayed = ContactCoverageReport.RunSample(context, slot, new("attacker-drift", 4f, 6f, 60));

        Assert.Equal("hit", accepted.Outcome);
        Assert.True(accepted.AirborneAtStart);
        Assert.Equal("unavailable", delayed.Outcome);
        Assert.Equal("wrong-locomotion-state", delayed.Reason);
    }

    [Fact]
    public void CandidateOverride_IsolatedAndScaleOneIsExactControl()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("air.1", out var slot));
        var baseline = ContactCoverageReport.PrepareContext(entry, entry, 1f, Arena);
        var candidate = ContactCoverageReport.PrepareContext(entry, entry, .8f, Arena);
        var baselineBefore = ContactCoverageReport.RunSample(baseline, slot, new("attacker-drift", 4f, 6f, 6));
        var candidateResult = ContactCoverageReport.RunSample(candidate, slot, new("attacker-drift", 4f, 6f, 6));
        var baselineAfter = ContactCoverageReport.RunSample(baseline, slot, new("attacker-drift", 4f, 6f, 6));
        var scaleOne = ContactCoverageReport.PrepareContext(entry, entry, 1f, Arena);
        var control = ContactCoverageReport.RunSample(scaleOne, slot, new("attacker-drift", 4f, 6f, 6));

        Assert.Equal(baselineBefore.Outcome, baselineAfter.Outcome);
        Assert.Equal(baselineBefore.PrePressAttacker!.PositionZ, baselineAfter.PrePressAttacker!.PositionZ);
        Assert.Equal(baselineBefore.PrePressAttacker.VelocityZ, baselineAfter.PrePressAttacker.VelocityZ);
        Assert.Equal(baselineBefore.PrePressAttacker.PositionZ, control.PrePressAttacker!.PositionZ);
        Assert.True(candidateResult.PrePressAttacker!.PositionZ < baselineBefore.PrePressAttacker.PositionZ);
        Assert.True(candidateResult.PrePressAttacker.VelocityZ < baselineBefore.PrePressAttacker.VelocityZ);
        Assert.Equal(baselineBefore.PrePressAttacker.PositionY, candidateResult.PrePressAttacker.PositionY);
        Assert.Equal(baselineBefore.PrePressAttacker.VelocityY, candidateResult.PrePressAttacker.VelocityY);
        Assert.False(candidateResult.PrePressAttacker.IsGrounded);
    }

    [Fact]
    public void HitWindows_DoNotBridgeSeparatedRunsOrUnavailableSamples()
    {
        var samples = Enumerable.Range(0, 8).Select(t => new CoverageSampleResultForTest(t, t is 0 or 1 or 4 or 5 ? "hit" : t == 2 ? "unavailable" : "miss")).ToArray();
        var intervals = ContactCoverageReport.FindHitWindows(samples.Select(x => x.Result).ToArray());
        Assert.Equal(2, intervals.Length);
        Assert.Equal((0, 1), (intervals[0].StartPressTick, intervals[0].EndPressTick));
        Assert.Equal((4, 5), (intervals[1].StartPressTick, intervals[1].EndPressTick));
        Assert.Equal(new[] { 2, 2 }, intervals.Select(x => x.SuccessfulPressTicks).ToArray());
    }

    [Fact]
    public void Normals_GroundedHitAndWhiffUseRealActivationEvidence()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var slot));
        var report = ContactCoverageReport.BuildNormals(entry, entry, new ContactCoverageReport.NormalCoverageOptions
        {
            Slots = new[] { slot },
            X = new[] { 0f, 4f },
            Y = new[] { 0f },
            Z = new[] { 0f, 6f },
        });
        var hit = report.Samples.Single(x => x.PositionIndex == 0);
        var miss = report.Samples.Single(x => x.PositionIndex == 3);
        Assert.Equal("hit", hit.Outcome);
        Assert.True(hit.ActivationId > 0);
        Assert.Equal(slot.Id, hit.CanonicalSlot);
        Assert.NotNull(hit.FirstContact);
        Assert.True(hit.FirstContact!.Damage > 0);
        Assert.Equal(hit.FirstContact.Damage, hit.TargetDamageAfter - hit.TargetDamageBefore);
        Assert.Equal("miss", miss.Outcome);
        Assert.Equal("effects-complete", miss.CompletionReason);
    }

    [Fact]
    public void Normals_MeasureRealJumpStylesAndMatchedReferences()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("air.1", out var slot));
        var report = ContactCoverageReport.BuildNormals(entry, entry, new ContactCoverageReport.NormalCoverageOptions
        {
            Slots = new[] { slot },
            X = new[] { 4f },
            Y = new[] { 2f },
            Z = new[] { 6f },
        });
        var neutral = report.JumpReferences.Single(x => x.JumpStyle == "neutral");
        var drift = report.JumpReferences.Single(x => x.JumpStyle == "neutral-drift");
        var running = report.JumpReferences.Single(x => x.JumpStyle == "running");
        Assert.Equal(neutral.TakeoffTick, drift.TakeoffTick);
        Assert.Equal(neutral.LandingTick, drift.LandingTick);
        Assert.True(running.TakeoffAfter!.VelocityZ > 0);
        Assert.True(neutral.TakeoffBefore!.IsGrounded);
        Assert.False(neutral.TakeoffAfter!.IsGrounded);
        foreach (var reference in report.JumpReferences)
        {
            var rows = report.Samples.Where(x => x.JumpStyle == reference.JumpStyle).ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(reference.TakeoffTick + 1, rows.Single(x => x.Checkpoint == "immediate").ScheduledPressTick);
            Assert.Equal(reference.TakeoffTick + Math.Max(1, reference.FlightTicks / 3),
                rows.Single(x => x.Checkpoint == "early").ScheduledPressTick);
            Assert.Equal(reference.TakeoffTick + Math.Max(1, 2 * reference.FlightTicks / 3),
                rows.Single(x => x.Checkpoint == "late").ScheduledPressTick);
        }
        Assert.True(report.Samples.Single(x => x.JumpStyle == "neutral-drift" && x.Checkpoint == "late").PrePressAttacker!.PositionZ > 0);
        Assert.True(report.Samples.Single(x => x.JumpStyle == "running" && x.Checkpoint == "late").PrePressAttacker!.PositionZ > 0);
        Assert.Equal(2f, report.Samples[0].ReferenceVictim!.PositionY, 5);
    }

    [Fact]
    public void Normals_EmptyAirSlotRemainsUnavailableInCompleteMatrix()
    {
        var attacker = EmptyAirSlotEntry();
        var victim = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var ground));
        Assert.True(CanonicalSlotProjection.TryGet("air.2", out var air));
        var report = ContactCoverageReport.BuildNormals(attacker, victim, new ContactCoverageReport.NormalCoverageOptions
        {
            Slots = new[] { ground, air },
            X = new[] { 0f },
            Y = new[] { 0f },
            Z = new[] { 0.75f },
            ExtraPositions = new[] { new ContactCoverageReport.CoveragePosition(0f, 0f, 0f), new ContactCoverageReport.CoveragePosition(0.5f, 1f, 2.75f) },
        });
        Assert.Equal(3, report.Positions.Count);
        Assert.Equal(30, report.Samples.Count);
        Assert.Equal(27, report.Samples.Count(x => x.SlotId == "air.2" && x.Outcome == "unavailable" && x.Reason == "empty-normal"));
        Assert.All(report.Samples.Where(x => x.SlotId == "air.2"), x =>
        {
            Assert.False(x.AttackInputSent);
            Assert.Null(x.ActivationId);
        });
    }

    [Fact]
    public void Normals_MaxTickBudgetDistinguishesTruncatedAndCompletedSlices()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var slot));
        var shortReport = ContactCoverageReport.BuildNormals(entry, entry, new ContactCoverageReport.NormalCoverageOptions
        {
            Slots = new[] { slot }, X = new[] { 4f }, Y = new[] { 0f }, Z = new[] { 6f }, MaxTicks = 1,
        });
        var fullReport = ContactCoverageReport.BuildNormals(entry, entry, new ContactCoverageReport.NormalCoverageOptions
        {
            Slots = new[] { slot }, X = new[] { 4f }, Y = new[] { 0f }, Z = new[] { 6f },
        });
        Assert.Equal("truncated", shortReport.Samples.Single().Outcome);
        Assert.Equal("miss", fullReport.Samples.Single().Outcome);
        Assert.Equal(1, shortReport.Totals.Truncated);
        Assert.Null(shortReport.Summaries.Single().ConnectionFraction);
        Assert.Equal(0f, fullReport.Summaries.Single().ConnectionFraction);
    }

    [Fact]
    public void Normals_PostLandingPressIsRejectedWithoutGroundSubstitution()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("air.1", out var slot));
        var reference = ContactCoverageReport.MeasureNormalJump(entry, Arena, "neutral");
        var sample = ContactCoverageReport.RunNormalSample(entry, entry, Arena, slot,
            new ContactCoverageReport.CoveragePosition(0f, 0f, 0.75f), 0, "neutral", "late",
            reference, reference.LandingTick + 1, 1);
        Assert.Equal("unavailable", sample.Outcome);
        Assert.Equal("wrong-locomotion-state", sample.Reason);
        Assert.True(sample.FirstLandingTick.HasValue);
        Assert.False(sample.AttackInputSent);
        Assert.Null(sample.ActivationId);
    }

    [Fact]
    public void Normals_RepeatedSerializationIsDeterministic()
    {
        var entry = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var slot));
        ContactCoverageReport.NormalCoverageReportData Build() => ContactCoverageReport.BuildNormals(entry, entry,
            new ContactCoverageReport.NormalCoverageOptions
            {
                Slots = new[] { slot }, X = new[] { 0f, 4f }, Y = new[] { 0f }, Z = new[] { 0f, 6f },
            });
        Assert.Equal(ContactCoverageReport.ToJson(Build()), ContactCoverageReport.ToJson(Build()));
    }

    [Fact]
    public void Normals_MixedSlotsKeepIndependentLifecycleDenominators()
    {
        var attacker = EmptyAirSlotEntry();
        var victim = EngineEntry();
        Assert.True(CanonicalSlotProjection.TryGet("ground.1", out var ground1));
        Assert.True(CanonicalSlotProjection.TryGet("ground.2", out var ground3));
        Assert.True(CanonicalSlotProjection.TryGet("air.2", out var air3));
        var report = ContactCoverageReport.BuildNormals(attacker, victim, new ContactCoverageReport.NormalCoverageOptions
        {
            Slots = new[] { ground1, ground3, air3 },
            X = new[] { 0f, 4f },
            Y = new[] { 0f },
            Z = new[] { 0f, 6f },
            MaxTicks = 60,
        });
        Assert.Equal("miss", report.Samples.Single(x => x.SlotId == "ground.1" && x.PositionIndex == 3).Outcome);
        Assert.Equal("truncated", report.Samples.Single(x => x.SlotId == "ground.2" && x.PositionIndex == 3).Outcome);
        Assert.All(report.Samples.Where(x => x.SlotId == "air.2"), x => Assert.Equal("unavailable", x.Outcome));
        Assert.Equal(4, report.Summaries.Single(x => x.SlotId == "ground.1").CompletedAttempts);
        Assert.Equal(4, report.Summaries.Single(x => x.SlotId == "ground.2").Truncated + report.Summaries.Single(x => x.SlotId == "ground.2").CompletedAttempts);
        Assert.All(report.Summaries.Where(x => x.SlotId == "air.2"), x => Assert.Null(x.ConnectionFraction));
    }

    [Theory]
    [InlineData("--slots", "ground.A")]
    [InlineData("--x", "NaN")]
    [InlineData("--y", "-1")]
    [InlineData("--air-scale", "1")]
    public void Normals_RejectInvalidCliArguments(string option, string value)
    {
        int exit = ContactCoverageReport.Run(new[] { "fightguy", "--coverage", "--experiment", "normals", option, value });
        Assert.Equal(2, exit);
    }

    [Fact]
    public void Normals_RejectOutOfArenaPositionBeforeWriting()
    {
        int exit = ContactCoverageReport.Run(new[]
        {
            "fightguy", "--coverage", "--experiment", "normals", "--slots", "ground.1",
            "--x", "1000", "--y", "0", "--z", "0.75",
            "--json", "artifacts/normal-coverage/should-not-write.json",
            "--html", "artifacts/normal-coverage/should-not-write.html",
        });
        Assert.Equal(2, exit);
    }


    private static MatchContentEntry EmptyAirSlotEntry() => EngineEntry();

    private static MatchContentEntry EngineEntry()
    {
        var source = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var definition = TestHelpers.EngineDef;
        definition = definition with { Class = source.Definition.Class };
        var slots = definition.CookedSlots!.ToArray();
        slots[0] = NormalSlot(0, "ground.1", 30);
        slots[1] = NormalSlot(1, "ground.2", 120);
        slots[7] = NormalSlot(7, "air.1", 30);
        definition = definition with { CookedSlots = slots };
        return new MatchContentEntry(source.Handle, source.LegacySelector, source.Identity,
            "engine report fixture", definition, SyntheticBake(), source.CookedCharacterPackage);
    }

    private static CookedSlotDefinition NormalSlot(int ordinal, string id, ushort duration)
    {
        var hitbox = new CookedHitbox(AuthoringHitboxShape.Sphere, 3f, 0f, 0f, 0.5f, 0f, 0f, 0f,
            null, null, 5f, 0f, 0f, 0f, 20, 1, true, 0);
        return new CookedSlotDefinition(ordinal, id, ordinal >= CanonicalSlotProjection.VariantSlotCount, "Engine report fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[]
                    {
                        new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters, hitbox),
                    }),
            }));
    }
    private static BakedAnimationData SyntheticBake()
    {
        var bytes = new List<byte>(System.Text.Encoding.ASCII.GetBytes("SKEL"));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(1u));
        byte[] bone = System.Text.Encoding.UTF8.GetBytes("fixture");
        bytes.AddRange(BitConverter.GetBytes((uint)bone.Length));
        bytes.AddRange(bone);
        byte[] animation = System.Text.Encoding.UTF8.GetBytes("fixture");
        bytes.AddRange(BitConverter.GetBytes((uint)animation.Length));
        bytes.AddRange(animation);
        bytes.AddRange(BitConverter.GetBytes(1u));
        bytes.AddRange(BitConverter.GetBytes(0f));
        bytes.AddRange(BitConverter.GetBytes(0f));
        bytes.AddRange(BitConverter.GetBytes(0f));
        return BakedAnimationData.LoadFromBin(bytes.ToArray());
    }

    private static (int Tick, float Damage) RunOracle(MatchContentEntry attacker, MatchContentEntry victim, float x, float z, SlotAddress slot)
    {
        var sim = new ServerSimulation(Arena);
        var a = new CharacterState { PX = 100f, PY = attacker.Definition.CapsuleHeight * .5f, PZ = 100f, IsGrounded = true, State = ActionState.Idle, JumpsLeft = attacker.Definition.Movement.MaxJumps, AirDodgesLeft = 1, FacingYaw = 0f };
        var v = new CharacterState { PX = 100f + x, PY = victim.Definition.CapsuleHeight * .5f, PZ = 100f + z, IsGrounded = true, State = ActionState.Idle, JumpsLeft = victim.Definition.Movement.MaxJumps, AirDodgesLeft = 1, FacingYaw = MathF.PI };
        sim.RegisterEntity(1, attacker.Definition, a, attacker.BakedAnimation);
        sim.RegisterEntity(100, victim.Definition, v, victim.BakedAnimation);
        var input = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = Program.SlotByte(int.Parse(slot.InputLabel)) }, [100] = default };
        for (int tick = 0; tick < Program.MaxTicks; tick++)
        {
            if (tick > 0) input[1] = default;
            sim.Tick(input);
            var hit = sim.LastTickHits.SingleOrDefault(x => x.OwnerEntityId == 1 && x.TargetEntityId == 100 && x.Damage > 0f);
            if (hit.Damage > 0f) return (tick, hit.Damage);
        }
        return (-1, 0f);
    }

    private sealed class CoverageSampleResultForTest
    {
        internal ContactCoverageReport.CoverageSampleResult Result { get; }
        internal CoverageSampleResultForTest(int tick, string outcome)
        {
            Result = new ContactCoverageReport.CoverageSampleResult
            {
                ScenarioId = "test",
                Profile = "baseline",
                InitialRelativeX = 0,
                InitialRelativeZ = 0,
                PressTick = tick,
                Outcome = outcome,
            };
        }
    }
}
