using System;
using System.Collections.Generic;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Property-based / fuzz tests for ServerSimulation.
///
/// Uses FsCheck to run a single deep trace (500 ticks) with random inputs,
/// asserting structural invariants after every tick.
///
/// On failure, FsCheck prints the seed for reproduction:
///   "Falsifiable, with seed: 12345"
/// Pass that seed to the test's PositiveInt parameter to replay.
/// </summary>
public class SimulationInvariantTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static readonly float GroundPy = TestHelpers.GroundPY(Def);

    [Property(MaxTest = 1, EndSize = 500)]
    public void Tick_DeepFuzz(PositiveInt seed)
    {
        var rng = new Random(seed.Item);
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var pState = TestHelpers.PlayerState();
        pState.PY = GroundPy;
        sim.RegisterEntity(1, Def, pState);

        var nState = TestHelpers.NpcState(3f, 0f);
        nState.PY = GroundPy;
        sim.RegisterEntity(100, Def, nState);

        ulong[] entityIds = [1, 100];

        for (int tick = 0; tick < 500; tick++)
        {
            var inputs = new Dictionary<ulong, InputState>();

            foreach (var eid in entityIds)
                inputs[eid] = RandomInput(rng);

            // Tick must not throw
            sim.Tick(inputs);

            foreach (var eid in entityIds)
            {
                var state = sim.GetState(eid);

                // ── Structural invariants ──

                Assert.True(
                    Enum.IsDefined(typeof(ActionState), state.State),
                    $"Tick {tick}, entity {eid}: invalid ActionState {state.State}");

                Assert.InRange(state.DamagePercent, 0, 999);

                Assert.True(
                    state.AirTimeTicks == 0 || state.AirTimeTicks < ushort.MaxValue,
                    $"Tick {tick}, entity {eid}: AirTimeTicks={state.AirTimeTicks} at overflow cap");

                // Positions must be finite (no NaN / Inf drift)
                Assert.True(
                    float.IsFinite(state.PX) &&
                    float.IsFinite(state.PY) &&
                    float.IsFinite(state.PZ),
                    $"Tick {tick}, entity {eid}: non-finite position ({state.PX}, {state.PY}, {state.PZ})");

                // Velocity must be finite
                Assert.True(
                    float.IsFinite(state.VX) &&
                    float.IsFinite(state.VY) &&
                    float.IsFinite(state.VZ),
                    $"Tick {tick}, entity {eid}: non-finite velocity ({state.VX}, {state.VY}, {state.VZ})");

                // ── Stuck-state detection ──

                Assert.True(
                    state.HitstunTicks <= 60,
                    $"Tick {tick}, entity {eid}: HitstunTicks={state.HitstunTicks} > 60 (stuck)");
                Assert.True(
                    state.HitstopTicks <= 24,
                    $"Tick {tick}, entity {eid}: HitstopTicks={state.HitstopTicks} > 24 (stuck)");

                // ── Respawn integrity ──

                // Entity below kill height should always have been respawned
                // (Deaths incremented, position set to spawn)
                if (state.PY < arena.KillHeight)
                    Assert.True(
                        state.Deaths > 0,
                        $"Tick {tick}, entity {eid}: below KillHeight={arena.KillHeight} but Deaths=0");
            }
        }
    }

    /// <summary>
    /// Generate a random valid InputState from the given RNG.
    /// </summary>
    private static InputState RandomInput(Random rng)
    {
        // Prefer no input most ticks (generates realistic idle-heavy traces)
        var input = new InputState
        {
            // Continuous movement: always generate move vector
            MoveX = (float)(rng.NextDouble() * 2.0 - 1.0),   // [-1, 1]
            MoveY = (float)(rng.NextDouble() * 2.0 - 1.0),   // [-1, 1]

            // Digital inputs: low probability per tick
            Up = rng.Next(4) == 0,
            Down = rng.Next(4) == 0,
            DownPressed = rng.Next(8) == 0,
            Left = rng.Next(4) == 0,
            Right = rng.Next(4) == 0,
            Jump = rng.Next(8) == 0,

            // Abilities: ~15% chance to press a slot (uniform 1-6)
            ActiveSlot = rng.Next(7) == 0
                ? (byte)rng.Next(1, 7)
                : (byte)0,

            // Aiming: ~10% chance
            IsAiming = rng.Next(10) == 0,
        };

        // Always set aiming fields (they're read even when not aiming)
        input.FacingYaw = (short)rng.Next(-18000, 18001);
        input.AimYaw = (short)rng.Next(-18000, 18001);
        input.AimDistance = (ushort)rng.Next(0, 6501);
        input.AimPitch = (short)rng.Next(-9000, 9001);

        // Target: half the time target NPC, half the time none
        input.TargetEntityId = rng.Next(2) == 0 ? (byte)100 : (byte)0;

        return input;
    }
}
