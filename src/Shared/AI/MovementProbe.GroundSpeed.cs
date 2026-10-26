using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

public static partial class MovementProbe
{
    public sealed record GroundTapMetrics(int HeldTicks, float HeldDistance, float TotalDistance, int StopTicks,
        MovementSample[] Curve);

    public sealed record GroundSpeedMetrics(string Character, GroundSpeedModel Model, float RunSpeed,
        float HalfTiltSpeed, int TimeToRunTicks, GroundTapMetrics Tap6, GroundTapMetrics Tap12,
        GroundTapMetrics HalfTiltTap6, GroundTapMetrics HalfTiltTap12, StopMetrics WalkStop, StopMetrics RunStop,
        MovementSample[] RunCurve, MovementSample[] HalfTiltCurve);

    /// <summary>Samples include standstill at tick zero and the state after every actual Shared tick.</summary>
    public static GroundSpeedMetrics MeasureGroundSpeed(CharacterDefinition def, ArenaDefinition arena,
        GroundSpeedTuning tuning)
    {
        var run = GroundSpeedCurve(def, arena, tuning, 1f, 60);
        var half = GroundSpeedCurve(def, arena, tuning, 0.5f, 60);
        int reached = Array.FindIndex(run, s => s.Tick > 0 && s.Speed >= def.Movement.RunSpeed * 0.999f);
        if (reached < 1) throw new InvalidOperationException("Run probe did not reach cruise within 60 ticks.");
        return new GroundSpeedMetrics(def.DisplayName, tuning.Model, def.Movement.RunSpeed,
            half[60].Speed, reached,
            Tap(def, arena, tuning, 1f, 6), Tap(def, arena, tuning, 1f, 12),
            Tap(def, arena, tuning, 0.5f, 6), Tap(def, arena, tuning, 0.5f, 12),
            Stop(half, 60), Stop(run, 60), run, half);
    }

    public sealed record GroundReversalMetrics(float Keep, float FirstReverseSpeed, int RecoveryTicks,
        float DisplacementToCruise, MovementSample[] Curve);

    public sealed record ProgressiveMetrics(string Character, GroundSpeedTuning Tuning, float RunSpeed,
        int TimeToRunTicks, float RampDistance, GroundTapMetrics Tap6, GroundTapMetrics Tap12,
        GroundTapMetrics Tap18, StopMetrics RunStop, GroundReversalMetrics Reverse100,
        GroundReversalMetrics Reverse50, MovementSample[] RunCurve);

    public static ProgressiveMetrics MeasureProgressive(CharacterDefinition def, ArenaDefinition arena,
        GroundSpeedTuning tuning)
    {
        if (tuning.Model != GroundSpeedModel.Progressive) throw new ArgumentException("Progressive tuning required.", nameof(tuning));
        var run = GroundSpeedCurve(def, arena, tuning, 1f, 60);
        int reached = Array.FindIndex(run, s => s.Tick > 0 && s.Speed >= def.Movement.RunSpeed);
        if (reached < 1) throw new InvalidOperationException("Progressive did not reach full speed.");
        return new ProgressiveMetrics(def.DisplayName, tuning, def.Movement.RunSpeed, reached, run[reached].PosX,
            Tap(def, arena, tuning, 1f, 6), Tap(def, arena, tuning, 1f, 12), Tap(def, arena, tuning, 1f, 18),
            Stop(run, 60), Reverse(1f), Reverse(0.5f), run);

        GroundReversalMetrics Reverse(float keep)
        {
            var selected = GroundSpeedTuning.Progressive(tuning.RampTicks, tuning.Curve, keep, tuning.StopTicks, tuning.StartRatio);
            var full = SampleGroundSpeed(def, arena, selected, tick => new InputState { MoveX = tick <= 60 ? 1f : -1f }, 90);
            var curve = new MovementSample[31];
            for (int tick = 0; tick < curve.Length; tick++)
                curve[tick] = full[tick + 60] with { Tick = tick, PosX = full[tick + 60].PosX - full[60].PosX };
            if (curve[1].PosX >= 0f) throw new InvalidOperationException("Reversal did not flip on its first tick.");
            int recovery = Array.FindIndex(curve, s => s.Tick > 0 && s.Speed >= def.Movement.RunSpeed);
            if (recovery < 1) throw new InvalidOperationException("Reversal did not recover cruise speed.");
            return new GroundReversalMetrics(keep, curve[1].Speed, recovery, curve[recovery].PosX, curve);
        }
    }

    private static GroundTapMetrics Tap(CharacterDefinition def, ArenaDefinition arena,
        GroundSpeedTuning tuning, float tilt, int heldTicks)
    {
        var curve = GroundSpeedCurve(def, arena, tuning, tilt, heldTicks);
        var stop = Stop(curve, heldTicks);
        return new GroundTapMetrics(heldTicks, curve[heldTicks].PosX,
            curve[heldTicks].PosX + stop.StopDistance, stop.StopTicks, curve);
    }

    private static StopMetrics Stop(MovementSample[] curve, int heldTicks)
    {
        int stopped = Array.FindIndex(curve, s => s.Tick > heldTicks && s.Speed <= 0.001f);
        if (stopped < 0) throw new InvalidOperationException("Release probe did not stop within its tick budget.");
        return new StopMetrics(stopped - heldTicks, curve[stopped].PosX - curve[heldTicks].PosX, curve);
    }

    private static MovementSample[] GroundSpeedCurve(CharacterDefinition def, ArenaDefinition arena,
        GroundSpeedTuning tuning, float tilt, int heldTicks)
        => SampleGroundSpeed(def, arena, tuning,
            tick => tick <= heldTicks ? new InputState { MoveX = tilt } : default, heldTicks + 60);

    private static MovementSample[] SampleGroundSpeed(CharacterDefinition def, ArenaDefinition arena,
        GroundSpeedTuning tuning, Func<int, InputState> inputAt, int ticks)
    {
        var sim = new ServerSimulation(arena, groundSpeedTuning: tuning);
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
