using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MankiKitTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef!;
    private static readonly CharacterDefinition CombatDef = TestHelpers.CombatDef;
    private static readonly float Gpy = TestHelpers.MankiGroundPY;

    // ══════════════════════════════════════════════════════════════════
    //  BAZOOKA (R, slot 4, activeSlot=5)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bazooka_PressFiresAtTickSix_RegardlessOfHold(bool held)
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.PlayerState() with { PY = Gpy });
        for (int tick = 0; tick < 6; tick++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0, IsAiming = held } });
            var state = sim.GetState(1);
            Assert.Equal(ActionState.Attacking, state.State);
            Assert.False(state.IsAiming);
            Assert.Equal((byte)1, state.ComboStage);
            if (tick < 5) Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }
        var rocket = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(15f, rocket.Gravity);
        Assert.Equal((ushort)60, rocket.DurationTicks);
        Assert.Equal(30f, rocket.VZ, 4);
        Assert.True(rocket.Explosion!.Value.CanHitOwner);
        Assert.Equal(4f, rocket.Explosion.Value.OwnerDamage);
    }

    [Fact]
    public void Bazooka_FiringContinuesAfterAirLanding()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.PlayerState() with
            { PY = Gpy + .4f, IsGrounded = false, VY = -5f });
        for (int tick = 0; tick < 6; tick++)
            sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0 } });
        Assert.True(sim.GetState(1).IsGrounded);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.False(sim.GetState(1).IsAiming);
        Assert.Single(sim.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)73)]
    public void Bazooka_CloseExplosion_KeepsFourSelfDamage(ushort startingDamage)
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState(20f, 20f) with
            { PY = Gpy, DamagePercent = startingDamage });
        sim.RegisterEntity(100, CombatDef, TestHelpers.NpcState(20f, 22f) with
            { PY = TestHelpers.GroundPY(CombatDef) });
        bool selfContact = false;
        for (int tick = 0; tick < 30; tick++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = tick == 0 ? AbilitySlots.R : (byte)0, TargetEntityId = 100 }, [100] = default });
            selfContact |= sim.LastTickHits.Exists(h => h.OwnerEntityId == 1 && h.TargetEntityId == 1);
        }
        Assert.True(selfContact);
        Assert.Equal((ushort)(startingDamage + 4), sim.GetState(1).DamagePercent);
    }

    // ══════════════════════════════════════════════════════════════════
    //  JETPACK BOOST (E, slot 3, activeSlot=4)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Jetpack_IgnitionReplacesDownwardVelocityWithVerticalLaunch()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with
        {
            PY = Gpy + 2f,
            IsGrounded = false,
            VY = -8f,
            AirTimeTicks = Def.Movement.FloatWindowTicks,
        };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);

        var after = sim.GetState(1);
        Assert.Equal(8f, after.VY);
        Assert.False(after.IsGrounded);
        Assert.Equal(Def.Movement.FloatWindowTicks, after.AirTimeTicks);
    }

    [Fact]
    public void Jetpack_DiagonalInputNormalizesToHorizontalSpeedCap()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 1f, moveY: 1f);

        var after = sim.GetState(1);
        var expected = 12f / MathF.Sqrt(2f);
        Assert.Equal(expected, after.VX, 3);
        Assert.Equal(expected, after.VZ, 3);
        Assert.Equal(12f, MathF.Sqrt(after.VX * after.VX + after.VZ * after.VZ), 3);
    }

    [Fact]
    public void Jetpack_NeutralInputFliesAlongFacing()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);

        var after = sim.GetState(1);
        Assert.Equal(0f, after.VX);
        Assert.Equal(12f, after.VZ);
        Assert.Equal(8f, after.VY);
    }

    [Fact]
    public void Jetpack_SteersOnOppositeStickAndResumesAirDriftAfterFuel()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 1f, moveY: 0f);
        var launch = sim.GetState(1);
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, new InputState { MoveX = -1f } } });
        Assert.Equal(-launch.VX, sim.GetState(1).VX);

        for (int i = 0; i < 150 && sim.GetActiveAbility(1) != null; i++)
            sim.Tick(new() { { 1, default } });

        var apex = sim.GetState(1);
        Assert.Equal(ActionState.Idle, apex.State);
        Assert.Equal((byte)0, apex.AttackSlot);
        float beforeDrift = apex.VX;
        sim.Tick(new() { { 1, new InputState { MoveX = -1f } } });
        Assert.NotEqual(beforeDrift, sim.GetState(1).VX);
    }

    [Fact]
    public void Jetpack_IgnitionHitboxDamagesAndLaunchesNearbyNpc()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var player = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(CombatDef) };
        var npc = TestHelpers.NpcState(z: 0.75f) with { PY = TestHelpers.GroundPY(CombatDef) };
        sim.RegisterEntity(1, CombatDef, player);
        sim.RegisterEntity(100, CombatDef, npc);

        bool contacted = false, hitstop = false, launched = false;
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new()
            {
                [1] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.E } : default,
                [100] = default,
            });
            foreach (var hit in sim.LastTickHits)
                contacted |= hit.OwnerEntityId == 1 && hit.TargetEntityId == 100 && !hit.Blocked;
            var victim = sim.GetState(100);
            hitstop |= contacted && victim.HitstopTicks > 0;
            launched |= hitstop && victim.HitstopTicks == 0 && !victim.IsGrounded
                && victim.KVY > 0f && victim.PY > npc.PY;
        }

        Assert.True(contacted);
        Assert.Equal((ushort)10, sim.GetState(100).DamagePercent);
        Assert.True(hitstop, "Ignition contact must freeze the victim before launch.");
        Assert.True(launched, "Ignition must launch the victim upward after hitstop.");
    }

    [Fact]
    public void Jetpack_FuelCompletionStartsCooldownAndRejectsUntilReady()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        TestHelpers.RegisterPlayer(sim, Def, state);

        TickJetpack(sim, 3, moveX: 0f, moveY: 0f);
        for (int i = 0; i < 150 && sim.GetActiveAbility(1) != null; i++)
            sim.Tick(new() { { 1, default } });

        var completed = sim.GetState(1);
        Assert.Equal((ushort)210, completed.Cooldown3);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) } });
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Equal((byte)0, sim.GetState(1).AttackSlot);

        for (int i = 0; i < 210; i++)
            sim.Tick(new() { { 1, default } });
        Assert.Equal((ushort)0, sim.GetState(1).Cooldown3);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) } });
        Assert.Equal((byte)AbilitySlots.E, sim.GetState(1).AttackSlot);
    }

    private static void TickJetpack(ServerSimulation sim, int ticks, float moveX, float moveY)
    {
        for (int i = 0; i < ticks; i++)
        {
            var input = new InputState { MoveX = moveX, MoveY = moveY };
            if (i == 0) input.ActiveSlot = AbilitySlots.E;
            sim.Tick(new() { { 1, input } });
        }
    }
    // Reserved wire selector 2 toggles targeting and cannot start a move.

    [Fact]
    public void ReservedSecondWireSelector_DoesNotStartAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = Gpy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // The reserved selector must not dispatch an attack.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 2 } } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State); // never entered an attack
        Assert.Equal((byte)0, s.AttackSlot);
        Assert.Equal((ushort)0, s.DamagePercent);
    }
}
