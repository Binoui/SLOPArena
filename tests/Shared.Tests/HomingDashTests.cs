using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>The universal homing dash (ADR-0042): float, homing travel, arrival window or punishable miss.</summary>
public sealed class HomingDashTests
{
    private static readonly CharacterDefinition Def = TestHelpers.EngineDef;
    private static readonly float Arrival = Def.CapsuleRadius * 2f + HomingDashConfig.ArrivalGap;
    private static readonly InputState Dash = new() { DashPressed = true };

    [Fact]
    public void FloatsInPlaceThenDashesAndArrivesBesideTheTarget()
    {
        var sim = Duel(8f);
        float startZ = sim.GetState(1).PZ;
        Tick(sim, Dash);
        Assert.Equal(ActionState.HomingDashFloat, sim.GetState(1).State);

        int floatTicks = 1;
        while (sim.GetState(1).State == ActionState.HomingDashFloat && floatTicks < 60)
        {
            Assert.Equal(startZ, sim.GetState(1).PZ);
            Tick(sim);
            floatTicks++;
        }
        Assert.InRange(floatTicks, HomingDashConfig.FloatTicks, HomingDashConfig.FloatTicks + 1);

        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 60), "the dash arrives");
        Assert.InRange(Gap(sim), Arrival - 0.01f, Arrival + 0.06f);
        Assert.InRange(sim.GetState(1).FacingYaw, -0.01f, 0.01f);
        Assert.True(sim.GetState(1).HomingDashCooldownTicks > 0);

        for (int i = 0; i < HomingDashConfig.ArriveWindowTicks; i++)
            Tick(sim);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void ArrivalWindowIsCancelledByAnAttack()
    {
        var sim = Duel(6f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 80));
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
    }

    [Fact]
    public void AnAttackFromTheArrivalWindowOutlastsTheWindow()
    {
        ushort duration = (ushort)(HomingDashConfig.ArriveWindowTicks + 20);
        var def = TestHelpers.WithMove(Def, AbilitySlots.Slot4, false, "Slow big normal", duration);
        var sim = Duel(6f, def);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 80));
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot4 });
        for (int i = 0; i < HomingDashConfig.ArriveWindowTicks + 5; i++)
        {
            Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
            Tick(sim);
        }
    }

    [Fact]
    public void AnAttackPressedMidDashStartsOnArrival()
    {
        var sim = Duel(8f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDash, 20));
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        Assert.Equal(ActionState.HomingDash, sim.GetState(1).State);
        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 60), "the press does not cut the dash short");
        Tick(sim);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
    }

    [Fact]
    public void AMissDropsTheMidDashPress()
    {
        var sim = Duel(19.5f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDash, 20));
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        Assert.True(RunUntil(sim, ActionState.HomingDashMiss, 80));
        for (int i = 0; i < HomingDashConfig.MissRecoveryTicks + 20; i++)
        {
            Assert.NotEqual(ActionState.Attacking, sim.GetState(1).State);
            Tick(sim);
        }
    }

    [Fact]
    public void AHitDropsTheMidDashPress()
    {
        var sim = Duel(10f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDash, 20));
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        var dasher = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = dasher.PX, Y = dasher.PY, Z = dasher.PZ + 0.6f,
            Radius = 0.8f, Damage = 6, BaseKnockback = 30, StunTicks = 20, DurationTicks = 2, OwnerId = 100,
        });
        Assert.True(RunUntil(sim, ActionState.Hitstun, 10));
        for (int i = 0; i < 180; i++)
        {
            Assert.NotEqual(ActionState.Attacking, sim.GetState(1).State);
            Tick(sim);
        }
    }

    [Fact]
    public void RunningOutOfTravelIsALockedPunishableMiss()
    {
        var sim = Duel(19.5f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDashMiss, 80), "the dash runs out short of the target");
        var miss = sim.GetState(1);
        Assert.True(miss.AnimLockTicks > 0);
        Assert.Equal(0f, miss.VX);
        Assert.Equal(0f, miss.VZ);

        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1, Jump = true });
        Assert.Equal(ActionState.HomingDashMiss, sim.GetState(1).State);
        Assert.True(sim.GetState(1).IsGrounded);
    }

    [Fact]
    public void AShorterFighterStaysOnTheFloorDashingAtATallerGroundedTarget()
    {
        var shortDef = Def with { CapsuleHeight = Def.CapsuleHeight - 0.2f };
        var sim = TestHelpers.MakeSim();
        var dasher = TestHelpers.PlayerState(50f, 50f);
        dasher.PY = TestHelpers.GroundPY(shortDef);
        sim.RegisterEntity(1, shortDef, dasher);
        var target = Grounded(TestHelpers.NpcState(50f, 58f));
        target.FacingYaw = MathF.PI;
        sim.RegisterEntity(100, Def, target);
        Tick(sim);
        Tick(sim, Dash);
        for (int i = 0; i < 80 && sim.GetState(1).State != ActionState.HomingDashArrive; i++)
        {
            Assert.True(sim.GetState(1).IsGrounded, $"left the floor in {sim.GetState(1).State}");
            Tick(sim);
        }
        Assert.Equal(ActionState.HomingDashArrive, sim.GetState(1).State);
        Assert.True(sim.GetState(1).IsGrounded, "a grounded arrival leaves the follow-up a ground normal");
    }

    [Theory]
    [InlineData(3f)]
    [InlineData(6.2f)]
    [InlineData(9.1f)]
    [InlineData(12.3f)]
    public void EveryDistanceArrivesOnTheSameGap(float distance)
    {
        var sim = Duel(distance);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 80));
        Assert.InRange(Gap(sim), Arrival - 0.01f, Arrival + 0.06f);
    }

    [Fact]
    public void AGroundedDashStaysOnTheFloorUnderAJump()
    {
        var sim = Duel(9f);
        Tick(sim, Dash, new InputState { Jump = true, JumpHeld = true });
        bool arrived = false;
        for (int i = 0; i < 60 && !arrived; i++)
        {
            Tick(sim, default, new InputState { JumpHeld = true });
            Assert.True(sim.GetState(1).IsGrounded, $"left the floor in {sim.GetState(1).State}");
            arrived = sim.GetState(1).State == ActionState.HomingDashArrive;
        }
        Assert.True(arrived, "the dash arrives under the jump");
        Assert.False(sim.GetState(100).IsGrounded);
    }

    [Fact]
    public void AnAirDashAtAGroundedTargetArrivesJustAboveItsFeet()
    {
        var sim = Duel(10f);
        Lift(sim, 1, 2f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 80));
        var dasher = sim.GetState(1);
        Assert.False(dasher.IsGrounded, "an air dash arrives in the air, so the follow-up is an aerial");
        float aboveFeet = dasher.PY - sim.GetState(100).PY;
        float lift = Def.CapsuleHeight * HomingDashConfig.AirAimLift;
        Assert.InRange(aboveFeet, lift - 0.01f, lift + 0.5f);
    }

    [Fact]
    public void AnAirborneArrivalHoversBeforeGravityReturns()
    {
        var sim = Duel(6f);
        // Fresh float windows keep both fighters hanging at 3 m while the dash flies across.
        Lift(sim, 100, 3f);
        Lift(sim, 1, 3f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDashArrive, 80));
        float arrivalY = sim.GetState(1).PY;
        float arrivalZ = sim.GetState(1).PZ;
        Assert.False(sim.GetState(1).IsGrounded);
        Assert.True(sim.GetState(1).VZ > 0f, "an airborne arrival glides on instead of stopping dead");
        for (int i = 1; i < HomingDashConfig.ArriveHoverTicks; i++)
        {
            Tick(sim);
            Assert.True(MathF.Abs(sim.GetState(1).PY - arrivalY) < 0.05f, $"tick {i}: {sim.GetState(1).PY} vs {arrivalY} vy {sim.GetState(1).VY}");
        }
        float glide = sim.GetState(1).PZ - arrivalZ;
        Assert.InRange(glide, 0.15f, HomingDashConfig.ArriveCarrySpeed * HomingDashConfig.ArriveHoverTicks * Simulation.TickDt);
        for (int i = 0; i < 4; i++)
            Tick(sim);
        Assert.True(sim.GetState(1).PY < arrivalY, "gravity returns after the hover");
    }

    [Fact]
    public void WithoutATargetTheDashFliesAlongFacingAndMisses()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, Grounded(TestHelpers.PlayerState(50f, 50f)));
        Tick(sim, Dash);
        Assert.Equal(0UL, sim.GetState(1).HomingDashTargetId);
        Assert.True(RunUntil(sim, ActionState.HomingDashMiss, 80));
        float travelled = sim.GetState(1).PZ - 50f;
        float range = HomingDashConfig.Speed * HomingDashConfig.MaxTravelTicks * Simulation.TickDt;
        Assert.InRange(travelled, range - 0.5f, range + 0.5f);
        Assert.InRange(sim.GetState(1).PX, 49.99f, 50.01f);
    }

    [Fact]
    public void TheAirDashIsSpentUntilLanding()
    {
        var sim = TestHelpers.MakeSim();
        sim.NoCooldownsEntityId = 1;
        var airborne = TestHelpers.PlayerState(50f, 50f);
        airborne.PY = TestHelpers.GroundPY(Def) + 12f;
        airborne.IsGrounded = false;
        sim.RegisterEntity(1, Def, airborne);
        Tick(sim, Dash);
        Assert.Equal(ActionState.HomingDashFloat, sim.GetState(1).State);
        Assert.True(RunUntil(sim, ActionState.Idle, 120));
        Assert.False(sim.GetState(1).IsGrounded);

        Tick(sim, Dash);
        Assert.NotEqual(ActionState.HomingDashFloat, sim.GetState(1).State);

        for (int i = 0; i < 240 && !sim.GetState(1).IsGrounded; i++)
            Tick(sim);
        Assert.True(sim.GetState(1).IsGrounded);
        Assert.False(sim.GetState(1).HomingDashAirUsed);
        Tick(sim);
        Tick(sim, Dash);
        Assert.Equal(ActionState.HomingDashFloat, sim.GetState(1).State);
    }

    [Fact]
    public void TheCooldownBlocksASecondDashUntilItExpires()
    {
        var sim = Duel(6f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.Idle, 120));
        Tick(sim, Dash);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);

        for (int i = 0; i < HomingDashConfig.CooldownTicks && sim.GetState(1).HomingDashCooldownTicks > 0; i++)
            Tick(sim);
        Tick(sim, Dash);
        Assert.Equal(ActionState.HomingDashFloat, sim.GetState(1).State);
    }

    [Fact]
    public void AHitboxStopsTheDash()
    {
        var sim = Duel(10f);
        Tick(sim, Dash);
        Assert.True(RunUntil(sim, ActionState.HomingDash, 20));
        var dasher = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = dasher.PX, Y = dasher.PY, Z = dasher.PZ + 0.6f,
            Radius = 0.8f, Damage = 6, BaseKnockback = 30, StunTicks = 20, DurationTicks = 2, OwnerId = 100,
        });
        Assert.True(RunUntil(sim, ActionState.Hitstun, 10), "a hitbox beats the dash");
        Assert.True(sim.GetState(1).DamagePercent > 0);
    }

    [Fact]
    public void AnAttackBlocksTheDashUntilItsIasaWindow()
    {
        var def = TestHelpers.WithMove(Def, AbilitySlots.Slot1, false, "Long jab", 40, iasa: 20);
        var sim = Duel(8f, def);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        for (int i = 0; i < 5; i++)
            Tick(sim);
        Tick(sim, Dash);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);

        while (!Simulation.IsIasaUnlocked(sim.GetState(1), sim.GetActiveMove(1)))
            Tick(sim);
        Tick(sim, Dash);
        Assert.Equal(ActionState.HomingDashFloat, sim.GetState(1).State);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Fact]
    public void TheDashStartsOutOfShield()
    {
        var sim = Duel(8f);
        var shield = new InputState { ShieldHeld = true, ShieldPressed = true };
        Tick(sim, shield);
        Tick(sim, new InputState { ShieldHeld = true });
        Assert.Equal(ActionState.Shielding, sim.GetState(1).State);
        Tick(sim, new InputState { ShieldHeld = true, DashPressed = true });
        Assert.Equal(ActionState.HomingDashFloat, sim.GetState(1).State);
    }

    [Fact]
    public void HitstunRefusesTheDash()
    {
        var sim = Duel(8f);
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            X = victim.PX, Y = victim.PY, Z = victim.PZ,
            Radius = 0.6f, Damage = 6, BaseKnockback = 30, StunTicks = 30, DurationTicks = 1, OwnerId = 100,
        });
        Assert.True(RunUntil(sim, ActionState.Hitstun, 10));
        Tick(sim, Dash);
        Assert.NotEqual(ActionState.HomingDashFloat, sim.GetState(1).State);
        Assert.Equal(0, sim.GetState(1).HomingDashCooldownTicks);
    }

    [Fact]
    public void RestoringASnapshotMidDashMatchesTheContinuousRun()
    {
        var continuous = Duel(12f);
        Tick(continuous, Dash);
        Assert.True(RunUntil(continuous, ActionState.HomingDash, 20));
        for (int i = 0; i < 4; i++)
            Tick(continuous);
        var snapshot = continuous.CaptureSnapshot();

        var restored = Duel(12f);
        restored.RestoreSnapshot(snapshot);
        for (int i = 0; i < 30; i++)
        {
            Tick(continuous);
            Tick(restored);
        }
        Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
    }

    private static ServerSimulation Duel(float distance, CharacterDefinition? def = null)
    {
        def ??= Def;
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, Grounded(TestHelpers.PlayerState(50f, 50f)));
        var target = Grounded(TestHelpers.NpcState(50f, 50f + distance));
        target.FacingYaw = MathF.PI;
        sim.RegisterEntity(100, Def, target);
        // One idle tick lets the soft target resolve before the dash reads it.
        Tick(sim);
        Assert.Equal(100UL, sim.GetState(1).TargetEntityId);
        return sim;
    }

    private static void Lift(ServerSimulation sim, ulong id, float height)
    {
        var state = sim.GetState(id);
        state.PY += height;
        state.IsGrounded = false;
        state.AirTimeTicks = 0;
        sim.SetState(id, state);
    }

    private static CharacterState Grounded(CharacterState state)
    {
        state.PY = TestHelpers.GroundPY(Def);
        return state;
    }

    private static void Tick(ServerSimulation sim, InputState dasher = default, InputState target = default)
    {
        var inputs = new Dictionary<ulong, InputState> { [1] = dasher };
        if (sim.GetAllStates().ContainsKey(100)) inputs[100] = target;
        sim.Tick(inputs);
    }

    private static bool RunUntil(ServerSimulation sim, ActionState state, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (sim.GetState(1).State == state) return true;
            Tick(sim);
        }
        return sim.GetState(1).State == state;
    }

    private static float Gap(ServerSimulation sim)
    {
        var a = sim.GetState(1);
        var b = sim.GetState(100);
        float dx = b.PX - a.PX, dy = b.PY - a.PY, dz = b.PZ - a.PZ;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
