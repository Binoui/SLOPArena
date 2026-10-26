using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Aerial landing lag + auto-cancel windows (issue #125): an air stage can declare
/// LandingLagTicks — landing while the aerial is active applies that no-input/no-movement
/// lock unless the landing frame falls inside an auto-cancel window (stage-elapsed
/// &lt;= AutoCancelBeforeTicks or &gt;= AutoCancelAfterTicks → no lag, act immediately).
/// All-zero fields preserve the pre-issue behavior (landing never locks).
///
/// Physics used by the scenarios: the test aerial runs 40 ticks. With no lunge operations,
/// the initial -10 m/s VY remains unchanged and the character falls straight down during
/// the zero-gravity float window. A start height of 1.4 m lands at stage-elapsed 8
/// (mid active window → lag), 4.7 m lands at stage-elapsed 28 (inside the 6..32 mid window →
/// lag; with the late window at 26 the same landing auto-cancels). The lock outlives the
/// aerial (18 ticks from a t27 landing expires at t45, 6 ticks after the move ends at t39),
/// which is what makes the post-move input blocks below discriminate the lag from the move's
/// own anim lock.
/// </summary>
public class LandingLagTests
{
    private const ushort AirSlot1Duration = 40;
    private const ushort AirSlot1Lag = 18;
    private const ushort AirSlot1Before = 6;
    private const ushort AirSlot1After = 32;
    /// <summary>Fall speed for the scenarios: -10 m/s at 60 Hz = 1/6 m per tick.</summary>
    private const float FallSpeed = -10f;

    private static CookedSlotDefinition AirSlot1(
        ushort landingLagTicks,
        ushort autoCancelBeforeTicks,
        ushort autoCancelAfterTicks,
        float lunge)
        => new(
            7,
            "air.1",
            true,
            "Test Aerial",
            "Test Aerial",
            "icon.test",
            AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None,
            0,
            false,
            false,
            new CookedTimeline(new[]
            {
                new CookedStage(
                    AirSlot1Duration,
                    0,
                    landingLagTicks,
                    autoCancelBeforeTicks,
                    autoCancelAfterTicks,
                    Array.Empty<string>(),
                    LungeOperations(lunge)),
            }));

    private static CookedTimelineOperation[] LungeOperations(float speed)
        => speed == 0f
            ? Array.Empty<CookedTimelineOperation>()
            : Enumerable.Range(0, 7)
                .Select(tick => (CookedTimelineOperation)new CookedSetVelocityOperation(
                    (ushort)tick,
                    AuthoringUnit.MetersPerSecond,
                    AuthoringVelocityMode.Absolute,
                    0f,
                    0f,
                    speed))
                .ToArray();

    private static CharacterDefinition MakeDef(
        ushort lag,
        ushort before,
        ushort after,
        float lunge = 0f,
        float airFloatGravity = 0f)
    {
        var def = TestHelpers.EngineDef;
        if (airFloatGravity != 0f)
            def = def with { Movement = def.Movement with { AirFloatGravity = airFloatGravity } };
        var slots = def.CookedSlots!.ToArray();
        slots[7] = AirSlot1(lag, before, after, lunge);
        def = def with { CookedSlots = slots };
        return def;
    }

    /// <summary>Mid-window landing: lag 18, auto-cancel 6..32, no lunge (clean vertical fall).</summary>
    private static readonly CharacterDefinition LagDef = MakeDef(AirSlot1Lag, AirSlot1Before, AirSlot1After);
    /// <summary>Late-window landing: auto-cancel from 26, so a stage-elapsed-28 landing is clean.</summary>
    private static readonly CharacterDefinition CleanLateDef = MakeDef(AirSlot1Lag, AirSlot1Before, 26);
    /// <summary>Default-off control: every field zero — current behavior, landing never locks.</summary>
    private static readonly CharacterDefinition ZeroDef = MakeDef(0, 0, 0);
    /// <summary>
    /// Freeze control: the cooked aerial writes 4 m/s forward velocity through tick 6,
    /// resetting VY each time, so full float gravity is needed for the fall to resume.
    /// Starting 0.4 m up lands at stage-elapsed 13 with residual VZ = 4 drift still live;
    /// the lock must zero it the instant it applies.
    /// </summary>
    private static readonly CharacterDefinition FreezeDef =
        MakeDef(AirSlot1Lag, AirSlot1Before, AirSlot1After, lunge: 4f, airFloatGravity: 36f);

    private static float Gpy => TestHelpers.GroundPY(LagDef);

    /// <summary>Airborne falling start: heightAbove above ground, straight down, no horizontal input.</summary>
    private static CharacterState FallingStart(float heightAbove)
        => TestHelpers.PlayerState() with { PY = Gpy + heightAbove, VY = FallSpeed, IsGrounded = false };

    /// <summary>Run a scenario manually, returning the state after each tick.</summary>
    private static List<CharacterState> Run(CharacterDefinition def, float heightAbove,
        Func<int, InputState> inputFor)
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, FallingStart(heightAbove));
        var states = new List<CharacterState>();
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { { 1, inputFor(tick) } });
            states.Add(sim.GetState(1));
        }
        return states;
    }

    private static InputState Slot1At(int tick) => tick == 0
        ? new InputState { ActiveSlot = AbilitySlots.Slot1 }
        : default;

    // ── Goldens: clean-land vs lag-land on the same aerial ──

    /// <summary>
    /// The canonical cooked air.1 slot carries its own landing metadata. A mid-window
    /// landing must apply that stage's LandingLagTicks through CookedTimelineAbility.
    /// </summary>
    [Fact]
    public void AirSlot1_LandingMidWindow_AppliesLandingLag()
    {
        var def = MakeDef(AirSlot1Lag, AirSlot1Before, AirSlot1After);
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, FallingStart(4.7f));
        var states = new List<CharacterState>();
        for (int tick = 0; tick < 60; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
                { { 1, tick == 0 ? new InputState { ActiveSlot = AbilitySlots.Slot1 } : default } });
            states.Add(sim.GetState(1));
        }

        // 4.7 m start lands at stage-elapsed ~28 (mid window 6..32) → landing lag must apply.
        bool appliedLag = false;
        foreach (var s in states)
            if (s.IsGrounded && s.LandingLagTicks > 0) { appliedLag = true; break; }
        Assert.True(appliedLag,
            "Cooked air.1 should apply its stage landing lag on a mid-window landing");
    }


    // ── Mechanics: the lock itself ──

    /// <summary>
    /// Landing at stage-elapsed 8 (1.4 m start) with no lunge: the lag applies (full 18
    /// ticks) and the character is planted — VX pinned to 0 and PX constant through the
    /// lock. The lock counts down one per tick and expires at t25.
    /// </summary>
    [Fact]
    public void LandingMidAerial_AppliesLag_FreezesMovement()
    {
        var states = Run(LagDef, 1.4f, Slot1At);

        // Landed at t7 (elapsed 8 — mid window), full 18-tick lock applied.
        Assert.Equal((ushort)18, states[7].LandingLagTicks);
        Assert.True(states[7].IsGrounded);

        // Lock counts down 18 ticks: still live at t24, expired at t25.
        // (No-movement is asserted in LandingMidAerial_LagFreezesResidualLungeVelocity,
        // where a live velocity exists at touchdown — this scenario has none.)
        Assert.Equal((ushort)1, states[24].LandingLagTicks);
        Assert.Equal((ushort)0, states[25].LandingLagTicks);
    }

    /// <summary>
    /// The lock suppresses movement even when velocity is live at touchdown: the lunging
    /// FreezeDef still carries its VZ = 4 lunge drift (facing +Z) when it lands at
    /// stage-elapsed 13 — the freeze zeroes it the instant the lock applies, so the
    /// character does not slide through the lock the way it would with no lag.
    /// </summary>
    [Fact]
    public void LandingMidAerial_LagFreezesResidualLungeVelocity()
    {
        var states = Run(FreezeDef, 0.4f, Slot1At);

        // Landed at t12 (elapsed 13 — mid window), full 18-tick lock applied.
        Assert.True(states[12].IsGrounded);
        Assert.Equal((ushort)18, states[12].LandingLagTicks);

        // The lunge drift was live through the fall; the freeze zeroes it at touchdown and
        // pins the position for the whole lock (no friction runs during Attacking, so an
        // unfrozen VZ = 4 would slide the character across the floor).
        Assert.True(states[11].PZ > states[0].PZ); // drifted while airborne
        Assert.Equal(0f, states[12].VZ);           // frozen at the landing tick
        Assert.Equal(0f, states[20].VZ);           // and through the rest of the lock
        Assert.Equal(states[12].PZ, states[20].PZ);

        // 18 ticks from t12 → expired at the start of t30.
        Assert.Equal((ushort)1, states[29].LandingLagTicks);
        Assert.Equal((ushort)0, states[30].LandingLagTicks);
    }

    /// <summary>
    /// The lock is a hard no-input window once the aerial's own anim lock is gone: the
    /// aerial ends at t39, the lag (applied t27) holds until t45 — so jump/dodge presses at
    /// t41/t42 are dropped, and a jump press after the expiry (t46) goes through.
    /// </summary>
    [Fact]
    public void LandingMidAerial_LagBlocksInputsAfterAerialEnds()
    {
        var states = Run(LagDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Slot1 },
            41 => new InputState { Jump = true },
            42 => new InputState { ShieldPressed = true },
            46 => new InputState { Jump = true },
            _ => default,
        });

        // Aerial ended t39; the lag is still live at t41/t42 and drops both presses.
        Assert.Equal(ActionState.Idle, states[42].State);
        Assert.True(states[42].LandingLagTicks > 0);

        // Lag expired at the start of t45: the t46 jump goes through.
        Assert.Equal(ActionState.JumpSquat, states[47].State);
    }

    /// <summary>
    /// All-zero fields = pre-issue behavior: the same landing applies no lock and a jump
    /// press right after the aerial ends goes through immediately.
    /// </summary>
    [Fact]
    public void LandingMidAerial_ZeroFields_NoLag_CurrentBehavior()
    {
        var states = Run(ZeroDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Slot1 },
            41 => new InputState { Jump = true },
            _ => default,
        });

        Assert.True(states[27].IsGrounded);
        Assert.Equal((ushort)0, states[27].LandingLagTicks);
        Assert.Equal(ActionState.JumpSquat, states[42].State); // jump at t41 (aerial ended t39)
    }

    /// <summary>
    /// Early auto-cancel window: pressing the aerial 5 ticks before landing (stage-elapsed 5
    /// at touchdown, &lt;= AutoCancelBeforeTicks 6) skips the lock entirely AND ends the move
    /// on the landing frame — the player acts immediately instead of riding the recovery.
    /// </summary>
    [Fact]
    public void LandingInAutoCancelEarlyWindow_NoLag_ActsImmediately()
    {
        var states = Run(LagDef, 4.7f, tick => tick == 23
            ? new InputState { ActiveSlot = AbilitySlots.Slot1 }
            : default);

        Assert.True(states[27].IsGrounded);
        Assert.Equal((ushort)0, states[27].LandingLagTicks); // elapsed 5 ≤ 6 → auto-cancel
        Assert.Equal(ActionState.Idle, states[28].State);    // the aerial ended on landing
        Assert.Equal((byte)0, states[28].AttackSlot);
    }

    /// <summary>
    /// Late auto-cancel window: a stage-elapsed-28 landing with AutoCancelAfterTicks 26
    /// skips the lock AND ends the aerial on the landing frame — a jump press three ticks
    /// after landing (t30) goes through immediately, while the mid-window landing would
    /// still be attack-locked at that tick.
    /// </summary>
    [Fact]
    public void LandingInAutoCancelLateWindow_NoLag_ActsImmediately()
    {
        var states = Run(CleanLateDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Slot1 },
            30 => new InputState { Jump = true },
            _ => default,
        });

        Assert.True(states[27].IsGrounded);
        Assert.Equal((ushort)0, states[27].LandingLagTicks); // elapsed 28 ≥ 26 → auto-cancel
        Assert.Equal(ActionState.Idle, states[28].State);    // the aerial ended on landing
        Assert.Equal(ActionState.JumpSquat, states[31].State); // act immediately (t30 jump)
    }

    /// <summary>
    /// Ability inputs are hard-blocked inside the lock and never buffered through it: a
    /// Slot3 press at t41 (lag live, anim lock gone) is dropped — no second attack follows,
    /// and no buffered re-trigger fires when the lag expires.
    /// </summary>
    [Fact]
    public void AbilityInputDuringLag_IsDropped_NotBuffered()
    {
        var states = Run(LagDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Slot1 },
            41 => new InputState { ActiveSlot = AbilitySlots.Slot3 },
            _ => default,
        });

        Assert.True(states[41].LandingLagTicks > 0);
        Assert.Equal(ActionState.Idle, states[45].State);     // dropped — still Idle mid-lag
        Assert.Equal((byte)0, states[45].AttackSlot);
        Assert.Equal(ActionState.Idle, states[50].State);     // and nothing fired post-lag
        Assert.Equal((byte)0, states[50].AttackSlot);
    }

    /// <summary>
    /// "No movement" holds after the aerial has ended too (the lock outlives the move): the
    /// stick cannot steer while lagged; the instant the lag expires the same stick walks.
    /// </summary>
    [Fact]
    public void MoveInputDuringLag_NoWalk_UntilLagExpires()
    {
        var states = Run(LagDef, 4.7f, tick => tick switch
        {
            0 => new InputState { ActiveSlot = AbilitySlots.Slot1 },
            >= 40 => new InputState { MoveX = 1f },
            _ => default,
        });

        // Aerial ended t39 → Idle, but the lag (live t28..44) still plants the character.
        Assert.Equal(ActionState.Idle, states[42].State);
        Assert.True(states[42].LandingLagTicks > 0);
        Assert.Equal(states[41].PX, states[44].PX); // stick held through the lock: planted
        Assert.True(states[50].PX > states[44].PX); // lag expired (t45): the stick walks
    }

    [Fact]
    public void MoveInputAfterLandingLagUnlock_StartsAtRunSpeed()
    {
        var state = FallingStart(4.7f);
        // Preserve an existing ground direction so this is specifically a recovery exit,
        // not the ordinary standstill Rush kick-off.
        state.LastDirX = 1f;
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        sim.RegisterEntity(1, LagDef, state);

        var states = new List<CharacterState>();
        for (int tick = 0; tick <= 45; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0
                    ? new InputState { ActiveSlot = AbilitySlots.Slot1, MoveX = 1f }
                    : new InputState { MoveX = 1f },
            });
            states.Add(sim.GetState(1));
        }

        // The held stick cannot bypass landing lag: it remains planted until the lock
        // expires (landing is t27, lock is live through t44).
        Assert.True(states[44].LandingLagTicks > 0);
        Assert.Equal(states[27].PX, states[44].PX);
        Assert.Equal(0f, states[44].VX);

        // The first unlocked tick starts at cruise speed, rather than ramping from zero.
        Assert.Equal((ushort)0, states[45].LandingLagTicks);
        TestHelpers.AssertNear(LagDef.Movement.RunSpeed, states[45].VX, 0.1f);
        Assert.Equal(ActionState.Run, states[45].State);
    }

    /// <summary>
    /// Being hit ends the commitment: ApplyKnockback (the hitstun choke point) clears the
    /// lock so a stale LandingLagTicks cannot re-lock the victim after hitstun resolves.
    /// </summary>
    [Fact]
    public void Hitstun_ClearsLandingLag()
    {
        var s = TestHelpers.PlayerState() with { LandingLagTicks = 18 };
        Simulation.ApplyKnockback(ref s, 1f, 0f, 45, 10f, 5f, 0f, 20, 100f);
        Assert.Equal((ushort)0, s.LandingLagTicks);
        Assert.Equal(ActionState.Hitstun, s.State);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(18, 26, true)]
    [InlineData(18, 32, false)]
    public void DownLanding_UsesResolvedAerialCommitment_WithoutBankingSlide(int lag, int autoAfter, bool slide)
    {
        var def = MakeDef((ushort)lag, 6, (ushort)autoAfter);
        var sim = TestHelpers.MakeSim();
        var initial = FallingStart(4.7f);
        initial.PX = initial.PZ = 50;
        initial.VX = def.Movement.RunSpeed;
        sim.RegisterEntity(1, def, initial);
        CharacterState landed = default;
        for (int tick = 0; tick < 100; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new()
            {
                Down = true,
                ActiveSlot = tick == 0 ? AbilitySlots.Slot1 : (byte)0,
            } });
            landed = sim.GetState(1);
            if (sim.LastTickTouchdowns.Contains(1UL)) break;
        }
        Assert.True(landed.IsGrounded);
        Assert.Equal(slide, landed.State == ActionState.Sliding);
        Assert.Null(sim.GetActiveAbility(1));
        if (!slide)
        {
            Assert.True(landed.LandingLagTicks > 0);
            for (int i = 0; i < 60; i++)
            {
                sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { Down = true } });
                Assert.NotEqual(ActionState.Sliding, sim.GetState(1).State);
            }
            Assert.Equal(ActionState.Crouching, sim.GetState(1).State);
        }
    }
}
