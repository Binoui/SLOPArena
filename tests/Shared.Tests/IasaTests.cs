using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// IASA early-out (issue #124): attack stages can declare an IasaTicks unlock point.
/// From that tick on, any ability input interrupts the recovery; before it the full
/// ADR-0014 lock applies; with no input the move completes at its normal duration.
/// IasaTicks = 0 (default) preserves the full-lock behavior — no existing ability
/// changes until a stage authors the field.
/// </summary>
public class IasaTests
{
    // Slot1: 21-tick lock, unlocks at tick 16 (5 early). Lunge 3.
    private const ushort Slot1Duration = 21;
    private const ushort Slot1Iasa = 16;
    private const float Slot1Lunge = 3f;
    // Slot2: longer move with a stronger lunge — the interrupt target.
    private const ushort Slot2Duration = 30;
    private const float Slot2Lunge = 5f;

    private static readonly CharacterDefinition Def = MakeIasaDef(iasa: true);

    private static CookedSlotDefinition TestSlot(
        int ordinal,
        string id,
        ushort durationTicks,
        ushort iasaTicks,
        params CookedTimelineOperation[] operations)
        => new(
            ordinal,
            id,
            false,
            "Test",
            "Test",
            "icon.test",
            AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None,
            0,
            false,
            false,
            new CookedTimeline(new[]
            {
                new CookedStage(
                    durationTicks,
                    iasaTicks,
                    0,
                    0,
                    0,
                    System.Array.Empty<string>(),
                    operations),
            }));

    private static CookedTimelineOperation[] VelocityOperations(float forwardSpeed, ushort ticks = 10)
        => Enumerable.Range(0, ticks)
            .Select(tick => (CookedTimelineOperation)new CookedSetVelocityOperation(
                (ushort)tick,
                AuthoringUnit.MetersPerSecond,
                AuthoringVelocityMode.Absolute,
                0f,
                0f,
                forwardSpeed))
            .ToArray();

    /// <summary>Fresh engine movement/body with synthetic cooked timelines.</summary>
    private static CharacterDefinition MakeIasaDef(bool iasa)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = TestSlot(0, "ground.1", Slot1Duration, iasa ? Slot1Iasa : (ushort)0, VelocityOperations(Slot1Lunge));
        slots[1] = TestSlot(1, "ground.2", Slot2Duration, 0, VelocityOperations(Slot2Lunge));
        def.CookedSlots = slots;
        return def;
    }

    private static CharacterDefinition MakeGroundRecoveryDef()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = TestSlot(0, "ground.1", Slot1Duration, 0);
        def.CookedSlots = slots;
        return def;
    }

    private static CharacterDefinition MakeIasaHitboxDef()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = TestSlot(
            0,
            "ground.1",
            Slot1Duration,
            Slot1Iasa,
            new CookedSpawnHitboxOperation(
                0,
                AuthoringUnit.Meters,
                new CookedHitbox(
                    AuthoringHitboxShape.Sphere,
                    1.2f,
                    0f,
                    0.4f,
                    1f,
                    0f,
                    0f,
                    0f,
                    null,
                    null,
                    2f,
                    15f,
                    2f,
                    1.5f,
                    20,
                    30,
                    true,
                    0)));
        def.CookedSlots = slots;
        return def;
    }


    [Fact]
    public void GroundAttackNaturalEnd_HeldMovementStartsAtRunSpeed()
    {
        var def = MakeGroundRecoveryDef();
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(50f, 50f) with
        {
            PY = TestHelpers.GroundPY(def),
        });

        // Establish a held direction before the attack. The attack then clears velocity
        // while preserving the direction, which distinguishes a fresh recovery from a
        // normal standstill Rush kick-off.
        for (int tick = 0; tick < 20; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(moveY: 1f) });
        var beforeAttack = sim.GetState(1);
        TestHelpers.AssertNear(def.Movement.RunSpeed, beforeAttack.VZ, 0.1f);

        var states = new List<CharacterState>();
        for (int tick = 0; tick <= Slot1Duration + 1; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = TestHelpers.Input(
                    activeSlot: tick == 0 ? AbilitySlots.Slot1 : (byte)0,
                    moveY: 1f),
            });
            states.Add(sim.GetState(1));
        }

        // Held movement cannot bypass the grounded attack lock or move the character early.
        Assert.Equal(ActionState.Attacking, states[Slot1Duration - 2].State);
        Assert.Equal(beforeAttack.PZ, states[Slot1Duration - 2].PZ);
        Assert.Equal(0f, states[Slot1Duration - 2].VZ);

        // Natural completion unlocks on this tick; the following held-input tick must start
        // at the configured cruise speed, not accelerate up from zero.
        Assert.Equal(ActionState.Idle, states[Slot1Duration - 1].State);
        Assert.Equal(0f, states[Slot1Duration - 1].VZ);
        Assert.Equal(ActionState.Run, states[Slot1Duration].State);
        TestHelpers.AssertNear(def.Movement.RunSpeed, states[Slot1Duration].VZ, 0.1f);
    }
    [Fact]
    public void IasaUnlock_RequiresAttackingStateAndActiveSlot()
    {
        var state = new CharacterState
        {
            State = ActionState.Attacking,
            AttackSlot = AbilitySlots.Slot1,
            AttackElapsedTicks = Slot1Iasa,
            IsGrounded = true,
        };
        Assert.True(Simulation.IsIasaUnlocked(state, Def));

        state.State = ActionState.Hitstun;
        state.AnimLockTicks = Slot1Duration;
        Assert.False(Simulation.IsIasaUnlocked(state, Def));

        state.State = ActionState.Attacking;
        state.AttackSlot = AbilitySlots.None;
        Assert.False(Simulation.IsIasaUnlocked(state, Def));
    }


    private static float Gpy => TestHelpers.GroundPY(Def);

    /// <summary>
    /// Press Slot2 exactly at the IASA tick: Slot1's recovery is interrupted and the
    /// Slot2 move starts immediately (its lunge velocity + AttackSlot are captured mid-move).
    /// </summary>
    [Fact]
    public void Iasa_ActAtUnlock_InterruptsIntoSlot2()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        TestHelpers.TickN(sim, TestHelpers.Input(AbilitySlots.Slot1), 1);
        TestHelpers.TickDefault(sim, Slot1Iasa - 1);
        var interrupted = TestHelpers.TickN(sim, TestHelpers.Input(AbilitySlots.Slot2), 1);
        Assert.Equal(ActionState.Attacking, interrupted.State);
        Assert.Equal(AbilitySlots.Slot2, interrupted.AttackSlot);
        Assert.Equal((byte)2, interrupted.AttackSequence);
        TestHelpers.AssertNear(Slot2Lunge, interrupted.VZ, 0.001f);
        TestHelpers.TickDefault(sim, Slot2Duration);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void Iasa_SameSlotRestart_ResetsAttackClockAndSequence()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });

        for (int tick = 0; tick <= Slot1Iasa; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 || tick == Slot1Iasa
                    ? TestHelpers.Input(AbilitySlots.Slot1)
                    : default,
            });
        }

        var restarted = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, restarted.State);
        Assert.Equal(AbilitySlots.Slot1, restarted.AttackSlot);
        Assert.Equal((byte)0, restarted.ComboStage);
        Assert.Equal((byte)2, restarted.AttackSequence);
        Assert.InRange(restarted.AttackElapsedTicks, (ushort)1, Slot1Iasa);
    }

    /// <summary>
    /// Press Slot2 two ticks BEFORE the IASA tick: the old lock applies, Slot1 keeps
    /// running (press is outside the 6-tick buffer window, so it is dropped entirely)
    /// and no second attack follows.
    /// </summary>
    [Fact]
    public void Iasa_LockedBeforeUnlock_SameAttackContinues()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        for (int tick = 0; tick < Slot1Iasa; tick++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                    : tick == Slot1Iasa - 2 ? TestHelpers.Input(AbilitySlots.Slot2)
                    : default,
            });
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
        TestHelpers.TickDefault(sim, Slot1Duration - Slot1Iasa);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Equal((byte)1, sim.GetState(1).AttackSequence);
    }

    /// <summary>
    /// No input at all: the IASA unlock never cuts the animation — Slot1 still runs
    /// its full 21-tick duration (snapshot one tick before the end) and returns to Idle.
    /// </summary>
    [Fact]
    public void Iasa_NoInput_MoveCompletesAtFullDuration()
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        TestHelpers.TickN(sim, TestHelpers.Input(AbilitySlots.Slot1), 1);
        TestHelpers.TickDefault(sim, Slot1Duration - 2);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal(AbilitySlots.Slot1, sim.GetState(1).AttackSlot);
        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    /// <summary>
    /// Slot1 connects a hit at t10 (damage 2 → 8-tick attacker freeze per ADR-0012),
    /// with a very early IASA unlock (t4) so the unlock is live during the freeze.
    /// The hitstop hard-block: a Slot2 press at t14 (IASA-unlocked, attacker mid-freeze)
    /// must NOT interrupt. After the freeze expires (t18), the same press does interrupt.
    /// </summary>
    [Fact]
    public void Iasa_DoesNotInterruptDuringAttackerHitstop_ButDoesAfterFreeze()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = TestSlot(
            0,
            "ground.1",
            40,
            4,
            new CookedSpawnHitboxOperation(
                10,
                AuthoringUnit.Meters,
                new CookedHitbox(
                    AuthoringHitboxShape.Sphere,
                    1.2f,
                    0f,
                    0.4f,
                    1f,
                    0f,
                    0f,
                    0f,
                    null,
                    null,
                    2f,
                    15f,
                    2f,
                    1.5f,
                    20,
                    4,
                    true,
                    0)));
        slots[1] = TestSlot(1, "ground.2", Slot2Duration, 0, VelocityOperations(Slot2Lunge));
        def.CookedSlots = slots;

        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var target = TestHelpers.EngineDef;
        sim.RegisterEntity(100, target,
            TestHelpers.NpcState() with { PX = 0, PZ = 1.2f, PY = TestHelpers.GroundPY(target) });

        // Slot1 at t0; Slot2 press at t14: past the IASA unlock (elapsed 10 >= 4) but
        // the attacker is still frozen by its own connecting hit (freeze t10..t17).
        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 14; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                : t == 14 ? TestHelpers.Input(AbilitySlots.Slot2)
                : default;
            inputs[100] = default;
            sim.Tick(inputs);
        }

        var frozen = sim.GetState(1);
        Assert.True(frozen.HitstopTicks > 0, "test window: attacker must still be mid-freeze");
        Assert.Equal(AbilitySlots.Slot1, frozen.AttackSlot);   // hitstop hard-blocks the press

        // Continue; fresh Slot2 press at t19 (freeze expired at t17) — the unlock is still
        // live (the freeze paused the elapsed clock, it did not cancel the attack).
        for (int t = 15; t <= 19; t++)
        {
            inputs[1] = t == 19 ? TestHelpers.Input(AbilitySlots.Slot2) : default;
            inputs[100] = default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        Assert.Equal((byte)0, after.HitstopTicks);
        Assert.Equal(AbilitySlots.Slot2, after.AttackSlot);  // interrupt fires once unfrozen
    }

    /// <summary>
    /// Engine-capability control: with IasaTicks = 0 the press at the would-be unlock
    /// tick does NOT interrupt — Slot1 keeps running on its original clock and the
    /// Slot2 press goes to the standard input buffer (fires at lock expiry).
    /// </summary>
    [Fact]
    public void IasaTicksZero_KeepsFullLock_PressAtUnlockTickIsNotAnInterrupt()
    {
        var def = MakeIasaDef(iasa: false);
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });

        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 16; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                : t == 16 ? TestHelpers.Input(AbilitySlots.Slot2)
                : default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        // No interrupt: Slot1 is still running on its original clock...
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);
        Assert.Equal((ushort)17, after.AttackElapsedTicks); // an interrupt would reset to 1
        // ...and the press was buffered for the normal lock expiry, not consumed early.
        Assert.Equal(AbilitySlots.Slot2, after.BufferedSlot);
    }

    /// <summary>
    /// Counterpart of IasaTicksZero: with IasaTicks = 16 the same Slot2 press at tick 16
    /// is consumed by the interrupt and the Slot2 move starts immediately.
    /// </summary>
    [Fact]
    public void IasaTicksSet_PressAtUnlockTick_ConsumedByInterrupt()
    {
        var def = MakeIasaDef(iasa: true);
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });

        var inputs = new Dictionary<ulong, InputState>();
        for (int t = 0; t <= 16; t++)
        {
            inputs[1] = t == 0 ? TestHelpers.Input(AbilitySlots.Slot1)
                : t == 16 ? TestHelpers.Input(AbilitySlots.Slot2)
                : default;
            sim.Tick(inputs);
        }

        var after = sim.GetState(1);
        Assert.Equal(AbilitySlots.Slot2, after.AttackSlot);   // new move took over
        Assert.Equal((ushort)1, after.AttackElapsedTicks);    // new attack, tick 1
        Assert.Equal((byte)0, after.BufferedSlot);            // press consumed, nothing buffered
    }

    [Fact]
    public void Iasa_GroundedNormal_HeldMovementCancelsAtExactUnlock()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        float startZ = sim.GetState(1).PZ;
        var states = new List<CharacterState>();

        for (int tick = 0; tick <= Slot1Iasa; tick++)
        {
            bool holdDirection = tick >= Slot1Iasa - 2;
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0
                    ? TestHelpers.Input(AbilitySlots.Slot1, moveY: holdDirection ? 1f : 0f)
                    : TestHelpers.Input(moveY: holdDirection ? 1f : 0f),
            });
            states.Add(sim.GetState(1));
        }

        Assert.Equal(ActionState.Attacking, states[Slot1Iasa - 1].State);
        Assert.Equal(AbilitySlots.Slot1, states[Slot1Iasa - 1].AttackSlot);
        Assert.Equal(ActionState.Run, states[Slot1Iasa].State);
        Assert.Equal((byte)0, states[Slot1Iasa].AttackSlot);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.True(states[Slot1Iasa].PZ > startZ);
    }

    [Fact]
    public void Iasa_GroundedNormal_ZeroIasaAndAirborneMovesDoNotMovementCancel()
    {
        var grounded = TestHelpers.MakeSim(TestHelpers.TestArena());
        var noIasaDef = MakeIasaDef(iasa: false);
        grounded.RegisterEntity(1, noIasaDef, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(noIasaDef) });
        for (int tick = 0; tick <= Slot1Iasa; tick++)
            grounded.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0
                    ? TestHelpers.Input(AbilitySlots.Slot1)
                    : TestHelpers.Input(moveY: 1f),
            });
        Assert.Equal(ActionState.Attacking, grounded.GetState(1).State);
        Assert.NotNull(grounded.GetActiveAbility(1));

        var airborne = TestHelpers.MakeSim(TestHelpers.TestArena());
        airborne.RegisterEntity(1, Def, TestHelpers.PlayerState() with
        {
            PY = Gpy + 3f,
            IsGrounded = false,
        });
        for (int tick = 0; tick <= 4; tick++)
            airborne.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0
                    ? TestHelpers.Input(AbilitySlots.Slot1)
                    : TestHelpers.Input(moveY: 1f),
            });
        Assert.Equal(ActionState.Attacking, airborne.GetState(1).State);
        Assert.NotNull(airborne.GetActiveAbility(1));
        Assert.False(airborne.GetState(1).IsGrounded);
    }

    [Fact]
    public void Iasa_GroundedNormal_ExplicitRejectedAttackWinsOverMovementCancel()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState() with { PY = Gpy };
        state.SetCooldown(AbilitySlots.Slot2, 100);
        sim.RegisterEntity(1, Def, state);

        for (int tick = 0; tick <= Slot1Iasa; tick++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0
                    ? TestHelpers.Input(AbilitySlots.Slot1)
                    : tick == Slot1Iasa
                        ? TestHelpers.Input(AbilitySlots.Slot2, moveY: 1f)
                        : default,
            });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);
        Assert.NotNull(sim.GetActiveAbility(1));
        Assert.Equal(AbilitySlots.Slot2, after.BufferedSlot);
    }

    [Fact]
    public void Iasa_GroundedNormal_HitstopStillBlocksHeldMovement()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, Def, TestHelpers.PlayerState() with { PY = Gpy });
        for (int tick = 0; tick < Slot1Iasa; tick++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? TestHelpers.Input(AbilitySlots.Slot1) : default,
            });

        var locked = sim.GetState(1);
        locked.HitstopTicks = 2;
        sim.SetState(1, locked);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(moveY: 1f) });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);
        Assert.NotNull(sim.GetActiveAbility(1));
    }

    [Fact]
    public void Iasa_GroundedNormal_CancelsOwnedHitboxesBeforeTheyCanDamage()
    {
        var def = MakeIasaHitboxDef();
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        var targetDef = TestHelpers.EngineDef;
        sim.RegisterEntity(100, targetDef,
            TestHelpers.NpcState() with
            {
                PX = 0f,
                PZ = 6f,
                PY = TestHelpers.GroundPY(targetDef),
            });

        for (int tick = 0; tick <= Slot1Iasa; tick++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0
                    ? TestHelpers.Input(AbilitySlots.Slot1)
                    : TestHelpers.Input(moveY: 1f),
                [100] = default,
            });

        Assert.Null(sim.GetActiveAbility(1));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == 1);

        var target = sim.GetState(100);
        target.PZ = 0.75f;
        sim.SetState(100, target);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = default,
            [100] = default,
        });
        Assert.Empty(sim.LastTickHits);
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("ability")]
    [InlineData("hitstun")]
    [InlineData("shield")]
    [InlineData("death")]
    [InlineData("snapshot")]
    public void CancellationClearsMeleeAndPreservesLaunchedProjectiles(string exit)
    {
        var def = MakeIasaHitboxDef();
        var slots = def.CookedSlots!.ToArray();
        slots[0] = TestSlot(0, "ground.1", Slot1Duration, Slot1Iasa,
            slots[0].Timeline.Stages[0].Operations.Single(),
            new CookedSpawnProjectileOperation(0, AuthoringUnit.MetersPerSecond,
                new CookedProjectile(0, 4, 0, 10, 0, .2f, 5, 30, 2, 3, 10, 60)));
        def.CookedSlots = slots;
        var arena = TestHelpers.TestArena();
        arena.KillTop = 20;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        TestHelpers.TickN(sim, TestHelpers.Input(AbilitySlots.Slot1), Slot1Iasa);
        Assert.Equal(2, sim.Resolver.GetActiveHitboxes().Count);
        var activationId = sim.GetLastActivationId(1);

        switch (exit)
        {
            case "movement":
                sim.Tick(new() { [1] = TestHelpers.Input(moveY: 1) });
                break;
            case "ability":
                sim.Tick(new() { [1] = TestHelpers.Input(AbilitySlots.Slot2) });
                Assert.NotEqual(activationId, sim.GetLastActivationId(1));
                break;
            case "shield":
                sim.Tick(new() { [1] = new InputState { ShieldHeld = true } });
                Assert.Equal(ActionState.Shielding, sim.GetState(1).State);
                break;
            case "hitstun":
                sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 20 });
                sim.Tick(new());
                break;
            case "death":
                sim.SetState(1, sim.GetState(1) with { PY = 25, IsGrounded = false });
                sim.Tick(new());
                Assert.Single(sim.LastTickDeaths);
                break;
            case "snapshot":
                var snapshot = TestHelpers.PlayerState() with { PY = 5, State = ActionState.Hitstun, HitstunTicks = 20 };
                sim.ApplyAuthoritativeState(1, snapshot);
                Assert.Equal(snapshot, sim.GetState(1));
                break;
        }

        if (exit == "snapshot")
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        else
        {
            var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes());
            Assert.Equal(activationId, projectile.ActivationId);
            Assert.Equal(5f, projectile.Damage);
            Assert.False(projectile.CancelWithAbility);
        }
    }

}
