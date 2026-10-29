using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using F = SlopArena.Shared.Tests.BashieShellFixture;
using static SlopArena.Shared.Tests.BashieShellTests;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Wave Pool (ADR-0050): the whistle puts her on the ride, and the wave that rides with her catches each fighter it
/// meets once, carries them at her speed (Swept) and lets them go with that speed when the ride ends by any path.
/// </summary>
public sealed class BashieWaveTests
{
    [Fact]
    public void FightersTheWaveMeetsAreCarriedAtItsSpeed()
    {
        var sim = Sim(dummy: Dummy2(0f, 5f));
        RideOn(sim);
        var hits = RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        var catchHit = Assert.Single(hits);
        Assert.True(catchHit.OwnerWindow);
        Assert.Equal(Bashie, catchHit.OwnerEntityId);
        Assert.Equal(F.WaveDamage, sim.GetState(Dummy).DamagePercent);
        for (int i = 0; i < 10; i++)
        {
            Step(sim);
            var victim = sim.GetState(Dummy);
            Assert.Equal(ActionState.Swept, victim.State);
            Assert.Equal(State(sim).VX, victim.VX, 3);
            Assert.Equal(State(sim).VZ, victim.VZ, 3);
            Assert.DoesNotContain(sim.LastTickHits, h => h.TargetEntityId == Dummy);
        }
        Assert.Equal(F.RideSpeed, State(sim).VZ, 3);
    }

    [Fact]
    public void TheRideClockEndsAndReleasesThemWithItsSpeed()
    {
        var sim = Sim(dummy: Dummy2(0f, 3f));
        RideOn(sim);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        int ticks = 0;
        while (State(sim).State == ActionState.Shell && ticks++ < F.RideTicks + 2) Step(sim);
        Assert.InRange(ticks, F.RideTicks - 2, F.RideTicks + 1);
        Assert.Equal(ActionState.Idle, State(sim).State);
        Assert.Empty(Windows(sim));
        var released = Released(sim, Dummy);
        Assert.InRange(released.VZ, F.RideSpeed * .8f, F.RideSpeed + .01f);
    }

    [Fact]
    public void JumpingOffKeepsHerSpeedAndReleasesEveryone()
    {
        var sim = Sim(dummy: Dummy2(0f, 3f));
        RideOn(sim);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        Step(sim, new InputState { Jump = true });
        Assert.Empty(Windows(sim));
        var released = Released(sim, Dummy);
        Assert.InRange(released.VZ, F.RideSpeed * .9f, F.RideSpeed + .01f);
        int ticks = 0;
        while (State(sim).IsGrounded && ticks++ < 12) Step(sim);
        Assert.False(State(sim).IsGrounded);
        Assert.InRange(State(sim).VZ, F.RideSpeed - .5f, F.RideSpeed + .01f);
    }

    [Fact]
    public void TheWaveBreaksAtTheLipAndCarriedFightersFlyOff()
    {
        const float lip = 8f;
        var sim = Sim(dummy: Dummy2(0f, 3f), arena: Floor(ledgeZ: lip));
        RideOn(sim);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        int ticks = 0;
        while (State(sim).State == ActionState.Shell && ticks++ < F.RideTicks) Step(sim);
        var her = State(sim);
        Assert.Equal(ActionState.Idle, her.State);
        Assert.True(her.IsGrounded, "she stays on the lip");
        Assert.True(her.PZ < lip, $"she stops before the lip ({her.PZ})");
        Assert.Equal(0f, Speed(her));
        var victim = sim.GetState(Dummy);
        Assert.True(victim.PZ > lip - .5f, $"the carried fighter went on toward the edge ({victim.PZ})");
        Assert.InRange(victim.VZ, F.RideSpeed * .8f, F.RideSpeed + .01f);
    }

    [Fact]
    public void AHeadOnWallPopsHerUpAndReleasesThem()
    {
        var sim = Sim(dummy: Dummy2(0f, 3f), arena: Floor(wallZ: 9f));
        RideOn(sim);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        int ticks = 0;
        while (State(sim).State == ActionState.Shell && ticks++ < F.RideTicks) Step(sim);
        Assert.True(ticks < F.RideTicks, "the wall ended the ride");
        Assert.Equal(ActionState.Idle, State(sim).State);
        Assert.True(State(sim).VZ < 0f, "she bounces back");
        Assert.NotEqual(ActionState.Swept, Released(sim, Dummy).State);
    }

    [Fact]
    public void ShieldsAreCarried()
    {
        var sim = Sim(dummy: Dummy2(0f, 4f));
        var shield = new Dictionary<ulong, InputState> { [Dummy] = new InputState { ShieldHeld = true } };
        Step(sim, default, shield);
        Assert.Equal(ActionState.Shielding, sim.GetState(Dummy).State);
        RideOn(sim, others: shield);
        var hits = RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept, shield);
        Assert.DoesNotContain(hits, h => h.Blocked);
        Assert.Equal(0, sim.GetState(Dummy).BlockStunTicks);
    }

    [Fact]
    public void AnyHitOnHerEndsTheRideAndReleasesEveryone()
    {
        var sim = Sim(dummy: Dummy2(0f, 3f));
        RideOn(sim);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        HitWithThird(sim, Bashie);
        Step(sim);
        Assert.NotEqual(ActionState.Shell, State(sim).State);
        Step(sim);
        Assert.Empty(Windows(sim));
        Assert.NotEqual(ActionState.Swept, Released(sim, Dummy).State);
    }

    [Fact]
    public void AThirdPartyHitFreesACarriedFighterWithItsOwnLaunch()
    {
        var sim = Sim(dummy: Dummy2(0f, 3f));
        RideOn(sim);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        HitWithThird(sim, Dummy, angle: 90, knockback: 40f);
        bool launched = false;
        for (int i = 0; i < 15; i++)
        {
            Step(sim);
            var victim = sim.GetState(Dummy);
            Assert.NotEqual(ActionState.Swept, victim.State);
            launched |= victim.State == ActionState.Hitstun && (victim.KVY > 0f || victim.VY > 0f);
        }
        Assert.True(launched, "the third party's launch stands");
        Assert.Equal(ActionState.Shell, State(sim).State);

        // A hit too light to launch still lets the carried fighter go.
        var light = Sim(dummy: Dummy2(0f, 3f));
        RideOn(light);
        RunUntil(light, () => light.GetState(Dummy).State == ActionState.Swept);
        HitWithThird(light, Dummy);
        Step(light);
        Assert.NotEqual(ActionState.Swept, light.GetState(Dummy).State);
        for (int i = 0; i < 4; i++) { Step(light); Assert.NotEqual(ActionState.Swept, light.GetState(Dummy).State); }
    }

    [Fact]
    public void AnInvincibleFighterIsNotCarriedUntilItsInvincibilityEnds()
    {
        int catchTick = CatchTick(invincibility: 0);
        Assert.True(catchTick > 0);
        Assert.Equal(-1, CatchTick(invincibility: 600));
        int late = CatchTick(invincibility: (ushort)(catchTick + 2));
        Assert.InRange(late, catchTick + 1, catchTick + 4);
    }

    [Fact]
    public void ASecondWaveDoesNotStealACarriedFighter()
    {
        var sim = Sim(dummy: Dummy2(0f, 4f));
        var other = Standing(0f, 12f);
        other.EntityId = Third;
        other.FacingYaw = MathF.PI;
        sim.RegisterEntity(Third, F.Definition, other);
        Step(sim, Press(AbilitySlots.F), new Dictionary<ulong, InputState> { [Third] = Press(AbilitySlots.F) });
        for (int i = 0; i < F.WhistleTicks + 2; i++) Step(sim);
        Assert.Equal(ActionState.Shell, State(sim).State);
        Assert.Equal(ActionState.Shell, sim.GetState(Third).State);
        RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
        Assert.True(sim.GetState(Dummy).VZ > 0f, "her wave caught it first");
        for (int i = 0; i < 8 && State(sim).State == ActionState.Shell; i++)
        {
            Step(sim);
            if (sim.GetState(Dummy).State == ActionState.Swept && State(sim).State == ActionState.Shell)
                Assert.True(sim.GetState(Dummy).VZ > 0f, $"still hers {i}");
        }
    }

    [Fact]
    public void AirFHangsThenCoastsAndStartsTheClockOnLanding()
    {
        var sim = Sim(Airborne(2f));
        Step(sim, Press(AbilitySlots.F));
        float fell = 2f - (State(sim).PY - GroundY);
        for (int i = 0; i < F.WhistleTicks + 1 && State(sim).State != ActionState.Shell; i++) Step(sim);
        Assert.Equal(ActionState.Shell, State(sim).State);
        Assert.False(State(sim).IsGrounded);
        Assert.True(State(sim).PY - GroundY > 1.5f, "the whistle hangs her");
        Assert.Equal(0, State(sim).StateTicks);
        Assert.Equal(F.RideSpeed, State(sim).VZ, 3);
        int ticks = 0;
        while (!State(sim).IsGrounded && ticks++ < 120) Step(sim);
        Assert.Equal(ActionState.Shell, State(sim).State);
        Assert.InRange(State(sim).StateTicks, F.RideTicks - 1, F.RideTicks);
        Assert.Equal(F.RideSpeed, State(sim).VZ, 3);
        Assert.True(fell < .1f);
    }

    [Fact]
    public void AHitDuringTheWhistleGivesNoWave()
    {
        var sim = Sim();
        Step(sim, Press(AbilitySlots.F));
        for (int i = 0; i < 5; i++) Step(sim);
        HitWithThird(sim, Bashie);
        for (int i = 0; i < F.WhistleTicks + 10; i++)
        {
            Step(sim);
            Assert.NotEqual(ActionState.Shell, State(sim).State);
            Assert.Empty(Windows(sim));
        }
        Assert.True(State(sim).GetCooldown(AbilitySlots.F) > 0, "the interrupted whistle still costs its cooldown");
    }

    [Fact]
    public void TheWaveHeadsForTheTargetAtTheBlowElseFacing()
    {
        var alone = Sim();
        RideOn(alone);
        Assert.Equal(0f, State(alone).FacingYaw, 4);

        var aimed = Sim(dummy: Dummy2(3f, 4f));
        RideOn(aimed);
        Assert.Equal(MathF.Atan2(3f, 4f), State(aimed).FacingYaw, 3);
        Assert.Equal(MathF.Sin(MathF.Atan2(3f, 4f)) * F.RideSpeed, State(aimed).VX, 3);
        // With no turn rate the heading holds while the target moves away.
        var sideways = new Dictionary<ulong, InputState> { [Dummy] = new InputState { MoveX = -1f } };
        float heading = State(aimed).FacingYaw;
        for (int i = 0; i < 10; i++) Step(aimed, default, sideways);
        Assert.Equal(heading, State(aimed).FacingYaw, 4);
    }

    [Fact]
    public void TheRideTurnsTowardItsTargetAtMostItsTurnRate()
    {
        const float degreesPerSecond = 90f;
        var sim = Sim(dummy: Dummy2(-4f, 4f), def: F.Turning(degreesPerSecond));
        RideOn(sim);
        var sideways = new Dictionary<ulong, InputState> { [Dummy] = new InputState { MoveX = 1f } };
        float maxStep = degreesPerSecond * MathF.PI / 180f * Simulation.TickDt;
        float before = State(sim).FacingYaw;
        bool turned = false;
        for (int i = 0; i < 12 && State(sim).State == ActionState.Shell; i++)
        {
            Step(sim, default, sideways);
            float after = State(sim).FacingYaw;
            float delta = MathF.Abs(MathF.IEEERemainder(after - before, 2f * MathF.PI));
            Assert.True(delta <= maxStep + 1e-4f, $"turned {delta} > {maxStep}");
            turned |= delta > 1e-4f;
            Assert.Equal(F.RideSpeed, Speed(State(sim)), 2);
            before = after;
        }
        Assert.True(turned, "the ride follows a target that moves");
    }

    [Fact]
    public void ACarriedFighterCannotActUntilReleased()
    {
        foreach (var input in new[]
        {
            new InputState { ActiveSlot = AbilitySlots.Slot1 }, new InputState { Jump = true },
            new InputState { ShieldHeld = true }, new InputState { ActiveSlot = AbilitySlots.R },
        })
        {
            var sim = Sim(dummy: Dummy2(0f, 3f));
            RideOn(sim);
            RunUntil(sim, () => sim.GetState(Dummy).State == ActionState.Swept);
            var held = new Dictionary<ulong, InputState> { [Dummy] = input };
            for (int i = 0; i < 6; i++)
            {
                Step(sim, default, held);
                Assert.Equal(ActionState.Swept, sim.GetState(Dummy).State);
            }
        }
    }

    // ── helpers ──

    /// <summary>Steps until the condition holds and returns the wave's contacts on the way.</summary>
    private static List<SpellResolver.HitResult> RunUntil(ServerSimulation sim, Func<bool> done,
        Dictionary<ulong, InputState>? others = null, int maxTicks = 40)
    {
        var hits = new List<SpellResolver.HitResult>();
        for (int i = 0; i < maxTicks && !done(); i++)
        {
            Step(sim, default, others);
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == Bashie && h.OwnerWindow));
        }
        Assert.True(done(), "reached");
        return hits;
    }

    /// <summary>Steps until the fighter is no longer Swept; returns its first free state.</summary>
    private static CharacterState Released(ServerSimulation sim, ulong id)
    {
        for (int i = 0; i < Shell.SweptLeaseTicks + 2 && sim.GetState(id).State == ActionState.Swept; i++) Step(sim);
        var free = sim.GetState(id);
        Assert.NotEqual(ActionState.Swept, free.State);
        return free;
    }

    private static void HitWithThird(ServerSimulation sim, ulong target, sbyte angle = 30, float knockback = 8f)
    {
        var victim = sim.GetState(target);
        sim.Resolver.Spawn(new Hitbox
        {
            X = victim.PX, Y = victim.PY, Z = victim.PZ, EndX = victim.PX, EndY = victim.PY, EndZ = victim.PZ,
            Radius = 1f, Shape = HitboxShape.Sphere, Damage = 5f, BaseKnockback = knockback, KnockbackAngle = angle,
            StunTicks = 20, DurationTicks = 1, OwnerId = Third, AttackSlot = AbilitySlots.Slot1,
            HitEntities = new HashSet<ulong> { target == Bashie ? Dummy : Bashie },
        });
    }

    /// <summary>The tick after the whistle on which a dummy with this invincibility is caught, or -1.</summary>
    private static int CatchTick(ushort invincibility)
    {
        var dummy = Dummy2(0f, 5f);
        dummy.InvincibilityTicks = invincibility;
        var sim = Sim(dummy: dummy);
        Step(sim, Press(AbilitySlots.F));
        for (int tick = 2; tick <= F.WhistleTicks + 20; tick++)
        {
            Step(sim);
            if (sim.GetState(Dummy).State == ActionState.Swept) return tick;
        }
        return -1;
    }
}
