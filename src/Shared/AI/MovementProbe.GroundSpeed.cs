using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

public static partial class MovementProbe
{
    public sealed record GroundTapMetrics(int HeldTicks, float HeldDistance, float TotalDistance, int StopTicks,
        MovementSample[] Curve);

    public sealed record GroundReversalMetrics(float FirstReverseSpeed, int RecoveryTicks,
        float DisplacementToCruise, MovementSample[] Curve);

    public sealed record WalkMetrics(float Speed, GroundTapMetrics Tap1, GroundTapMetrics Tap6,
        StopMetrics Stop, int RunToWalkBrakeTicks);

    public sealed record GroundSpeedMetrics(string Character, float RunSpeed, float FirstTickSpeed,
        int TimeToRunTicks, float RampDistance, GroundTapMetrics Tap6, GroundTapMetrics Tap12,
        GroundTapMetrics Tap18, StopMetrics RunStop, GroundReversalMetrics Reversal,
        WalkMetrics Walk, MovementSample[] RunCurve, MovementSample[] HalfTiltCurve);

    /// <summary>Tick zero is standstill. Each later sample follows one completed Shared tick.</summary>
    public static GroundSpeedMetrics MeasureGroundSpeed(CharacterDefinition def, ArenaDefinition arena)
    {
        var run = GroundSpeedCurve(def, arena, 1f, 60);
        var half = GroundSpeedCurve(def, arena, 0.5f, 60);
        int reached = Array.FindIndex(run, s => s.Tick > 0 && s.Speed >= def.Movement.RunSpeed);
        if (reached < 1) throw new InvalidOperationException("Run probe did not reach full speed.");
        var reverse = SampleGroundSpeed(def, arena,
            tick => new InputState { MoveX = tick <= 60 ? 1f : -1f }, 90);
        var curve = new MovementSample[31];
        for (int tick = 0; tick < curve.Length; tick++)
            curve[tick] = reverse[tick + 60] with { Tick = tick, PosX = reverse[tick + 60].PosX - reverse[60].PosX };
        if (curve[1].PosX >= 0f) throw new InvalidOperationException("Reversal did not flip on its first tick.");
        int recovery = Array.FindIndex(curve, s => s.Tick > 0 && s.Speed >= def.Movement.RunSpeed);
        if (recovery < 1) throw new InvalidOperationException("Reversal did not recover cruise speed.");
        var walk = GroundSpeedCurve(def, arena, 1f, 60, walk: true);
        var runToWalk = SampleGroundSpeed(def, arena,
            tick => new InputState { MoveX = 1f, Walk = tick > 60 }, 90);
        int walkReached = Array.FindIndex(runToWalk,
            s => s.Tick > 60 && s.Speed <= walk[1].Speed * 1.000001f);
        if (walkReached < 0) throw new InvalidOperationException("Run-to-walk probe did not reach walking speed.");
        var walkMetrics = new WalkMetrics(walk[1].Speed, Tap(1, true), Tap(6, true), Stop(walk, 60), walkReached - 60);
        return new GroundSpeedMetrics(def.DisplayName, def.Movement.RunSpeed, run[1].Speed,
            reached, run[reached].PosX, Tap(6), Tap(12), Tap(18), Stop(run, 60),
            new GroundReversalMetrics(curve[1].Speed, recovery, curve[recovery].PosX, curve), walkMetrics, run, half);

        GroundTapMetrics Tap(int heldTicks, bool walking = false)
        {
            var tap = GroundSpeedCurve(def, arena, 1f, heldTicks, walking);
            var stop = Stop(tap, heldTicks);
            return new GroundTapMetrics(heldTicks, tap[heldTicks].PosX,
                tap[heldTicks].PosX + stop.StopDistance, stop.StopTicks, tap);
        }
    }

    private static StopMetrics Stop(MovementSample[] curve, int heldTicks)
    {
        int stopped = Array.FindIndex(curve, s => s.Tick > heldTicks && s.Speed <= 0.001f);
        if (stopped < 0) throw new InvalidOperationException("Release probe did not stop within its tick budget.");
        return new StopMetrics(stopped - heldTicks, curve[stopped].PosX - curve[heldTicks].PosX, curve);
    }

    private static MovementSample[] GroundSpeedCurve(CharacterDefinition def, ArenaDefinition arena,
        float tilt, int heldTicks, bool walk = false)
        => SampleGroundSpeed(def, arena,
            tick => tick <= heldTicks ? new InputState { MoveX = tilt, Walk = walk } : default, heldTicks + 60);

    private static MovementSample[] SampleGroundSpeed(CharacterDefinition def, ArenaDefinition arena,
        Func<int, InputState> inputAt, int ticks)
    {
        var sim = new ServerSimulation(arena);
        var state = new CharacterState
        {
            PY = def.CapsuleHeight * 0.5f, IsGrounded = true, State = ActionState.Idle,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
        };
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        var samples = new MovementSample[ticks + 1];
        samples[0] = new MovementSample(0, 0f, state.PY, 0f, 0f, true, ActionState.Idle);
        for (int tick = 1; tick < samples.Length; tick++)
        {
            inputs[1] = inputAt(tick);
            sim.Tick(inputs);
            state = sim.GetState(1);
            if (!state.IsGrounded || state.Deaths > 0)
                throw new InvalidOperationException("Ground-speed probe left the floor or died. Use a sufficiently large flat arena.");
            samples[tick] = new MovementSample(tick, state.PX, state.PY,
                MathF.Sqrt(state.VX * state.VX + state.VZ * state.VZ), state.VY, state.IsGrounded, state.State);
        }
        return samples;
    }
}
