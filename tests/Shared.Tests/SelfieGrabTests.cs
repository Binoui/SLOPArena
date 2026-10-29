using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// slop.ability.command-grab.v1 (ADR-0051, Nyx's Selfie Grab) on an engine fixture: a lunge, the capture, a hold with
/// the victim Grabbed in the command-hold phase, and a release with an authored launch. Values are fixture values.
/// </summary>
public sealed class SelfieGrabTests
{
    private const ulong Nyx = 1, Dummy = 2, Third = 3;
    private const ushort CaptureTick = 10, CaptureTicks = 5, ReleaseTick = 52, Duration = 76, Iasa = 68;
    private const float LaunchDamage = 9f;
    private const float X = 100f, Z = 100f;
    private static readonly InputState Press = new() { ActiveSlot = AbilitySlots.R, TargetEntityId = (byte)Dummy };

    internal static CookedCommandGrabCapabilityParameters Parameters => Grab;

    private static readonly CookedCommandGrabCapabilityParameters Grab = new(10, 12f, 1f, CaptureTick,
        new CookedHitbox(AuthoringHitboxShape.Sphere, 0.7f, 0f, 0f, 0.8f, 0f, 0f, 0f, null, null, 0f, 0f, 0f, 0f, 0,
            CaptureTicks, true, 0),
        ReleaseTick, new CookedGrabLaunch(LaunchDamage, 40f, 6f, 40f, 20));

    private static CharacterDefinition Def()
    {
        var body = TestHelpers.EngineDef with
        {
            CaptureGeometry = new CookedCaptureGeometry(1.8f, 0.68f, 1.15f, 0.57f, new CaptureAnchor(0f, 0.57f, 0.25f),
                new CaptureAnchor(0f, 0.57f, 0.39f)),
        };
        var slots = body.CookedSlots!.ToArray();
        Assert.True(CanonicalSlotProjection.TryGet(AbilitySlots.R, false, out var r));
        slots[r.Ordinal] = new CookedSlotDefinition(r.Ordinal, r.Id, false, "Selfie Grab", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(Duration, Iasa, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                {
                    new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, CharacterPackageCompiler.CommandGrabCapabilityId,
                        CharacterPackageCompiler.CommandGrabCapabilityVersion, Grab),
                }),
            }));
        return body with { CookedSlots = slots };
    }

    private static ServerSimulation Sim(float dummyDistance = 3f, bool dummyGrabs = false, bool third = false)
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var def = Def();
        var me = TestHelpers.PlayerState(X, Z);
        me.EntityId = Nyx;
        me.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(Nyx, def, me);
        var dummy = TestHelpers.NpcState(X, Z + dummyDistance);
        dummy.EntityId = Dummy;
        dummy.PY = TestHelpers.GroundPY(def);
        dummy.FacingYaw = MathF.PI;
        sim.RegisterEntity(Dummy, dummyGrabs ? def : TestHelpers.EngineDef, dummy);
        if (third)
        {
            var other = TestHelpers.NpcState(X + 4f, Z);
            other.EntityId = Third;
            other.PY = TestHelpers.GroundPY(def);
            sim.RegisterEntity(Third, TestHelpers.EngineDef, other);
        }
        return sim;
    }

    private static void Step(ServerSimulation sim, InputState nyx = default, InputState dummy = default,
        List<SpellResolver.HitResult>? hits = null)
    {
        sim.Tick(new Dictionary<ulong, InputState> { [Nyx] = nyx, [Dummy] = dummy });
        hits?.AddRange(sim.LastTickHits);
    }

    private static uint Caught(ServerSimulation sim, InputState dummy = default)
    {
        Step(sim, Press, dummy);
        uint pressed = sim.CurrentTick;
        for (int tick = 0; tick < CaptureTick + CaptureTicks && sim.GetState(Nyx).InteractionId == 0; tick++)
            Step(sim, dummy: dummy);
        Assert.NotEqual(0ul, sim.GetState(Nyx).InteractionId);
        return pressed;
    }

    [Fact]
    public void TheCaptureTakesAShieldingFighterAndHoldsItGrabbed()
    {
        var sim = Sim();
        var shield = new InputState { ShieldHeld = true };
        for (int tick = 0; tick < 3; tick++) Step(sim, dummy: shield);
        Assert.Equal(ActionState.Shielding, sim.GetState(Dummy).State);
        uint pressed = Caught(sim, shield);
        Assert.Equal(pressed + CaptureTick - 1, sim.CurrentTick);

        var her = sim.GetState(Nyx);
        var victim = sim.GetState(Dummy);
        Assert.Equal(ActionState.Attacking, her.State);
        Assert.Equal(ActionState.Grabbed, victim.State);
        Assert.Equal(her.InteractionId, victim.InteractionId);
        Assert.Equal((byte)DefenseInteractionPhase.CommandHold, her.InteractionPhase);
        Assert.Equal((byte)DefenseInteractionPhase.CommandHold, victim.InteractionPhase);
        Assert.Equal((ushort)0, victim.DamagePercent);

        for (int tick = 0; tick < 20; tick++) Step(sim, dummy: shield with { Jump = true, MoveX = 1f });
        victim = sim.GetState(Dummy);
        Assert.Equal(ActionState.Grabbed, victim.State);
        Assert.InRange(victim.PZ - sim.GetState(Nyx).PZ, 0.3f, 0.5f);
    }

    [Fact]
    public void TwoCommandGrabsCatchingEachOtherOnTheSameTickClash()
    {
        var sim = Sim(dummyDistance: 2.4f, dummyGrabs: true);
        Step(sim, Press, Press with { TargetEntityId = (byte)Nyx });
        for (int tick = 0; tick < CaptureTick + CaptureTicks + 2; tick++) Step(sim);
        Assert.Equal(0ul, sim.GetState(Nyx).InteractionId);
        Assert.Equal(0ul, sim.GetState(Dummy).InteractionId);
        Assert.Equal(ActionState.Attacking, sim.GetState(Nyx).State);
        Assert.Equal(ActionState.Attacking, sim.GetState(Dummy).State);
    }

    [Fact]
    public void AnOrdinaryGrabActiveOnTheCaptureTickClashes()
    {
        var sim = Sim(dummyGrabs: true);
        Step(sim, Press);
        for (int tick = 1; tick < CaptureTick - 1; tick++) Step(sim);
        var dummy = sim.GetState(Dummy);
        dummy.State = ActionState.GrabAttempt;
        dummy.InteractionPhase = (byte)DefenseInteractionPhase.Attempt;
        dummy.StateTicks = DefenseConfig.GrabWhiffRecoveryTicks + DefenseConfig.GrabActiveTicks;
        dummy.CapturedYaw = -18000;
        sim.SetState(Dummy, dummy);
        Step(sim);
        Step(sim);
        Assert.Equal(0ul, sim.GetState(Nyx).InteractionId);
        Assert.Equal(0ul, sim.GetState(Dummy).InteractionId);
        Assert.NotEqual(ActionState.Grabbed, sim.GetState(Dummy).State);
        Assert.NotEqual(ActionState.Grabbed, sim.GetState(Nyx).State);
        Assert.True(sim.GetState(Dummy).StateTicks <= DefenseConfig.GrabClashRecoveryTicks, "the ordinary grab took the clash recovery");
    }

    [Fact]
    public void AWhiffLeavesTheRestOfTheStageAsRecovery()
    {
        var sim = Sim(dummyDistance: 7f);
        Step(sim, Press);
        uint pressed = sim.CurrentTick;
        while (sim.GetActiveAbility(Nyx) != null && !Simulation.IsIasaUnlocked(sim.GetState(Nyx), sim.GetActiveMove(Nyx)))
            Step(sim);
        Assert.Equal(pressed + Iasa - 1, sim.CurrentTick);
        Assert.Equal(0ul, sim.GetState(Nyx).InteractionId);
        Assert.Equal(ActionState.Idle, sim.GetState(Dummy).State);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == Nyx);
    }

    [Fact]
    public void TheReleaseEndsThePairAndLandsTheAuthoredLaunch()
    {
        var sim = Sim();
        uint pressed = Caught(sim);
        var hits = new List<SpellResolver.HitResult>();
        while (sim.CurrentTick < pressed + ReleaseTick - 2) Step(sim, hits: hits);
        Assert.Empty(hits);
        Assert.Equal(ActionState.Grabbed, sim.GetState(Dummy).State);
        Step(sim);
        Assert.Equal(0ul, sim.GetState(Nyx).InteractionId);
        var launch = Assert.Single(sim.LastTickHits);
        Assert.Equal(Nyx, launch.OwnerEntityId);
        Assert.Equal(Dummy, launch.TargetEntityId);
        Assert.Equal(LaunchDamage, launch.Damage);
        Assert.True(launch.HitstopTicks > 0);
        Assert.Equal(launch.HitstopTicks, sim.GetState(Nyx).HitstopTicks);
        Assert.Equal((ushort)LaunchDamage, sim.GetState(Dummy).DamagePercent);
        for (int tick = 0; tick <= launch.HitstopTicks; tick++) Step(sim);
        var victim = sim.GetState(Dummy);
        Assert.Equal(ActionState.Hitstun, victim.State);
        Assert.True(victim.KVZ > 0f && victim.KVY > 0f, "launched up and away along her facing");
        Assert.True(sim.GetActiveAbility(Nyx) is Abilities.CookedTimelineAbility { Connected: true });
    }

    [Fact]
    public void AThirdPartyHitOnHerBreaksTheHold()
    {
        var sim = Sim(third: true);
        Caught(sim);
        var her = sim.GetState(Nyx);
        sim.Resolver.Spawn(new Hitbox
        {
            X = her.PX, Y = her.PY, Z = her.PZ, Radius = .5f, Shape = HitboxShape.Sphere, Damage = 6f,
            BaseKnockback = 6f, KnockbackAngle = 45, StunTicks = 10, DurationTicks = 1, OwnerId = Third,
        });
        Step(sim);
        Step(sim);
        Assert.Equal(0ul, sim.GetState(Nyx).InteractionId);
        Assert.Equal(0ul, sim.GetState(Dummy).InteractionId);
        Assert.NotEqual(ActionState.Grabbed, sim.GetState(Dummy).State);
    }

    [Fact]
    public void ARestoreMidHoldMatchesTheContinuousRunThroughTheRelease()
    {
        var continuous = Sim();
        Caught(continuous);
        for (int tick = 0; tick < 10; tick++) Step(continuous);
        var restored = Sim();
        var snapshot = continuous.CaptureSnapshot();
        restored.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(restored.CaptureSnapshot()));
        var a = new List<SpellResolver.HitResult>();
        var b = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 60; tick++)
        {
            Step(continuous, hits: a);
            Step(restored, hits: b);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()),
                $"diverged {tick + 1} ticks after the restore");
        }
        Assert.Single(b);
        Assert.Equal(a.Select(h => (h.MatchTick, h.Damage)), b.Select(h => (h.MatchTick, h.Damage)));
    }
}
