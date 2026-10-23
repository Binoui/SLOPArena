using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Closest-approach telemetry measures the hitboxes the resolver actually tested against the
/// swing's reference target, in the attacker's facing frame.
/// </summary>
public sealed class SwingContactGeometryTests
{
    private const float Reach = 1f;
    private const float HitboxRadius = 0.3f;
    private const ushort ActiveTicks = 6;
    private const float Origin = 50f;

    /// <summary>EngineDef with ground.1 = one sphere hitbox <see cref="Reach"/> ahead of the chest.</summary>
    private static CharacterDefinition ProbeDef()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        var slot = slots[0];
        var timeline = new CookedTimeline(new[]
        {
            new CookedStage(30, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
            {
                new CookedSpawnHitboxOperation(4, AuthoringUnit.Meters,
                    new CookedHitbox(AuthoringHitboxShape.Sphere, HitboxRadius, 0, 0, Reach, 0, 0, 0,
                        null, null, 5, 30, 4, 10, 10, ActiveTicks, true, 0)),
            }),
        });
        slots[0] = new CookedSlotDefinition(
            slot.Ordinal, slot.Id, slot.IsAir, slot.Name, slot.Description, slot.IconId,
            slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove,
            slot.PreserveMomentumOnStart, timeline, slot.ChargePool, slot.AimMovement,
            slot.AimAnimationId, slot.AllowSlideCarry);
        def = def with { CookedSlots = slots };
        return def;
    }

    private static SwingRecord RunSwing(float targetDx, float targetDz, float attackerYaw = 0f,
        bool targetShields = false, ulong selectedTarget = 0)
    {
        var def = ProbeDef();
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        float py = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(Origin, Origin)
            with { PY = py, FacingYaw = attackerYaw, TargetEntityId = selectedTarget });
        sim.RegisterEntity(100, def, TestHelpers.NpcState(Origin + targetDx, Origin + targetDz) with { PY = py });

        var recorder = new MatchRecorder();
        for (int tick = 0; tick < 50; tick++)
        {
            var inputs = new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.Slot1 } : default,
                [100] = targetShields
                    ? new InputState { ShieldHeld = true, ShieldPressed = tick == 0 }
                    : default,
            };
            recorder.RecordPresses(sim, tick, inputs, def);
            sim.Tick(inputs);
            recorder.RecordTick(sim, tick, inputs, def);
        }
        var swing = Assert.Single(recorder.Record.Swings);
        Assert.True(swing.Accepted);
        return swing;
    }

    [Fact]
    public void ShortWhiff_RecordsGapAheadOfTheHitbox()
    {
        var swing = RunSwing(0f, 3f);

        Assert.False(swing.Connected);
        Assert.False(swing.Blocked);
        Assert.Equal(100ul, swing.ReferenceTarget);
        Assert.True(swing.ReferenceByProximity);
        Assert.Equal(ActiveTicks, swing.HitboxTicks);
        Assert.True(swing.HasClosestApproach);
        // Hitbox surface reaches Reach + radius; the target body is a 0.3 m capsule at 3 m.
        TestHelpers.AssertNear(3f - Reach - HitboxRadius - 0.3f, swing.ClosestGap, 0.02f);
        TestHelpers.AssertNear(swing.ClosestGap, swing.MissForward, 0.02f);
        TestHelpers.AssertNear(0f, swing.MissSide, 0.02f);
        TestHelpers.AssertNear(0f, swing.MissUp, 0.02f);
        TestHelpers.AssertNear(3f, swing.ClosestRelForward, 0.02f);
        Assert.True(swing.TargetGroundedAtStart);
    }

    [Fact]
    public void QuarterTurnAttacker_ReportsTargetAheadAlongPositiveX()
    {
        var swing = RunSwing(3f, 0f, attackerYaw: MathF.PI / 2f);

        Assert.False(swing.Connected);
        Assert.True(swing.MissForward > 1f, $"forward miss {swing.MissForward}");
        TestHelpers.AssertNear(0f, swing.MissSide, 0.02f);
        TestHelpers.AssertNear(3f, swing.ClosestRelForward, 0.02f);
        TestHelpers.AssertNear(swing.MissForward, swing.MissWorldX, 0.02f);
    }

    [Fact]
    public void Contact_CountsDamagingHitAndOverlap()
    {
        var swing = RunSwing(0f, 1.2f);

        Assert.True(swing.Connected);
        Assert.Equal(1, swing.DamagingContacts);
        Assert.True(swing.HasClosestApproach);
        Assert.True(swing.ClosestGap <= 0f, $"gap {swing.ClosestGap}");
        Assert.Equal(0f, swing.MissForward);
    }

    [Fact]
    public void ShieldContact_MarksBlockedWithoutConnection()
    {
        var swing = RunSwing(0f, 1.5f, targetShields: true);

        Assert.True(swing.Blocked);
        Assert.False(swing.Connected);
        Assert.Equal(0, swing.DamagingContacts);
    }

    [Fact]
    public void SelectedTarget_IsReferenceEvenWhenAnotherFighterIsCloser()
    {
        var def = ProbeDef();
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        float py = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(Origin, Origin) with { PY = py, TargetEntityId = 101 });
        sim.RegisterEntity(100, def, TestHelpers.NpcState(Origin, Origin + 2f) with { PY = py });
        var far = TestHelpers.NpcState(Origin, Origin + 6f) with { PY = py, EntityId = 101 };
        sim.RegisterEntity(101, def, far);

        var recorder = new MatchRecorder();
        var press = new Dictionary<ulong, InputState> { [1] = new InputState { ActiveSlot = AbilitySlots.Slot1 } };
        recorder.RecordPresses(sim, 0, press, def);

        var swing = Assert.Single(recorder.Record.Swings);
        Assert.Equal(101ul, swing.ReferenceTarget);
        Assert.False(swing.ReferenceByProximity);
    }

    [Fact]
    public void NoFighterInRange_LeavesSwingWithoutReference()
    {
        var swing = RunSwing(0f, MatchRecorder.ReferenceSearchRadius + 3f);

        Assert.Equal(0ul, swing.ReferenceTarget);
        Assert.False(swing.HasClosestApproach);
        Assert.Equal(ActiveTicks, swing.HitboxTicks);
    }
}
