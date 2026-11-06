using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>SafeStep tuning. Provisional.</summary>
public static class SafeStepTuning
{
    /// <summary>Room kept from a blast's edge beyond the capsule.</summary>
    public const float Clearance = .8f;
    /// <summary>How far ahead a step toward the goal is checked against blasts.</summary>
    public const float LookAhead = 1f;
    /// <summary>Escape candidates sit on rings this far apart around the CPU.</summary>
    public const float EscapeStep = .6f;
    public const int EscapeRings = 7;
    /// <summary>A ring taller than this needs a second jump.</summary>
    public const float SingleJumpHalfHeight = .5f;
    public const int JumpLeadTicks = 14, JumpHoldTicks = 14;
    public const int TallLeadTicks = 48, TallSecondPressTicks = 32;
    /// <summary>The CPU stops moving this long before a ring's jump window, so the ring meets it where it planned.</summary>
    public const int SettleTicks = 30;
    /// <summary>A slipping CPU notices each bomb this long after its launch.</summary>
    public const int SlipLateTicks = 24;
}

/// <summary>
/// A remix CPU's footwork. It keeps the goal child's destination and action, plants its feet and jumps each hazard ring
/// on time, and steps around blasts, reading only published cues. A slip skips the first ring wave and notices bombs late.
/// </summary>
internal static class SafeStep
{
    private const int Directions = 16, DetourTurns = 3;
    /// <summary>How far a wall-free line must run for the CPU to count as free of its cage.</summary>
    private const float FreeRun = 4.5f;

    /// <summary>Whether blocking walls stop every straight line out of this point.</summary>
    internal static bool Boxed(IReadOnlyList<RuntimeSolid> walls, float x, float z, float radius)
    {
        if (walls.Count == 0) return false;
        for (int i = 0; i < Directions; i++)
        {
            float yaw = i * (MathF.PI * 2f / Directions);
            if (!RuntimeSolidSet.Crosses(walls, x, z, x + MathF.Sin(yaw) * FreeRun, z + MathF.Cos(yaw) * FreeRun, radius)) return false;
        }
        return true;
    }

    internal static MayhemCpuIntent Around(in MayhemCpuIntent goal, ChannelScene threats, in CpuProbe probe)
    {
        var self = probe.Self.State;
        float radius = probe.Self.Definition.CapsuleRadius;
        float x = self.PX, z = self.PZ;
        var ring = RingStep(threats.Rings, probe, radius);
        if (ring.Press is { } token) return new(CpuAction.Jump, x, z, PressToken: token);
        bool moving = goal.Action == CpuAction.Move;
        float destX = moving ? goal.X : x, destZ = moving ? goal.Z : z;
        var stage = probe.Frame.Stage;
        var dangers = Dangers(threats.Bombs, probe, radius);
        if (dangers.Count > 0 && MayhemMath.Exposed(x, z, dangers))
        {
            if (Holds(probe.Planned, stage, threats.Solids, radius, x, z, dangers)) return probe.Planned;
            var (ex, ez) = Escape(stage, threats.Solids, radius, x, z, destX, destZ, dangers);
            return new(CpuAction.Move, ex, ez);
        }
        if (ring.Settle && moving) return new(CpuAction.Brake, x, z);
        float distance = MathF.Sqrt(MayhemMath.DistanceSquared(x, z, destX, destZ));
        if (dangers.Count == 0 || !moving || distance < 1e-3f) return goal;
        float ux = (destX - x) / distance, uz = (destZ - z) / distance, look = MathF.Min(SafeStepTuning.LookAhead, distance);
        if (!MayhemMath.Exposed(x + ux * look, z + uz * look, dangers)) return goal;
        if (MayhemMath.Exposed(destX, destZ, dangers)) return new(CpuAction.Brake, x, z);
        for (int turn = 1; turn <= DetourTurns; turn++)
            for (int side = 1; side >= -1; side -= 2)
            {
                float angle = side * turn * MathF.PI / 6f, cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                float rx = ux * cos - uz * sin, rz = ux * sin + uz * cos;
                float px = x + rx * SafeStepTuning.LookAhead * 2, pz = z + rz * SafeStepTuning.LookAhead * 2;
                if (MayhemMath.OnDeck(stage, px, pz, MayhemShowTuning.PlacementMargin)
                    && !MayhemMath.Exposed(x + rx * look, z + rz * look, dangers) && !RuntimeSolidSet.Crosses(threats.Solids, x, z, px, pz, radius))
                    return new(CpuAction.Move, px, pz);
            }
        return new(CpuAction.Brake, x, z);
    }

    /// <summary>
    /// A press when a ring is due where the CPU will stand once its queued inputs play out, else whether to stand still
    /// for one. Rings slammed together share one press; a tall ring takes two. A press planned for the frozen call would
    /// be spent before play, so the CPU stands still until its press lands in play.
    /// </summary>
    private static (ushort? Press, bool Settle) RingStep(IReadOnlyList<RingCue> rings, in CpuProbe probe, float radius)
    {
        if (rings.Count == 0) return (null, false);
        var self = probe.Self.State;
        float ahead = (probe.Seen - probe.Tick) / 60f;
        float x = self.PX + self.VX * ahead, z = self.PZ + self.VZ * ahead;
        uint first = uint.MaxValue;
        foreach (var ring in rings) first = Math.Min(first, ring.SlamTick);
        bool settle = false;
        foreach (var ring in rings)
        {
            if (probe.Slip && ring.SlamTick == first) continue;
            float distance = MathF.Sqrt(MayhemMath.DistanceSquared(x, z, ring.Center.X, ring.Center.Z)) - radius - ring.HalfWidth;
            float arrival = ring.SlamTick + MathF.Max(0f, distance) / ring.Speed * 60f;
            if (arrival > ring.SlamTick + ring.LifetimeTicks) continue;
            float until = arrival - probe.Seen;
            bool tall = ring.HalfHeight > SafeStepTuning.SingleJumpHalfHeight;
            int lead = tall ? SafeStepTuning.TallLeadTicks : SafeStepTuning.JumpLeadTicks;
            int last = tall ? -SafeStepTuning.JumpHoldTicks : SafeStepTuning.JumpLeadTicks - SafeStepTuning.JumpHoldTicks;
            if (until <= lead && until >= last && probe.Seen < probe.PlayStart) settle = true;
            else if (until <= lead && until >= last)
            {
                uint press = tall && until <= SafeStepTuning.TallLeadTicks - SafeStepTuning.TallSecondPressTicks ? 1u : 0u;
                return ((ushort)((ring.SlamTick << 1 | press) + 1), false);
            }
            settle |= until > lead && until <= lead + SafeStepTuning.SettleTicks;
        }
        return (null, settle);
    }

    /// <summary>Launched blasts that last past the planned tick, grown by the capsule and clearance.</summary>
    private static List<MayhemDanger> Dangers(IReadOnlyList<BombCue> bombs, in CpuProbe probe, float radius)
    {
        var dangers = new List<MayhemDanger>();
        foreach (var bomb in bombs)
        {
            if (!bomb.Launched || probe.Seen > bomb.ImpactTick + MayhemHazardTuning.BlastTicks + 1) continue;
            if (probe.Slip && probe.Tick < bomb.LaunchTick + SafeStepTuning.SlipLateTicks) continue;
            dangers.Add(new MayhemDanger(bomb.Landing.X, bomb.Landing.Z, bomb.BlastRadius + radius + SafeStepTuning.Clearance));
        }
        return dangers;
    }

    /// <summary>
    /// Whether the move planned last tick still holds as the way out: its point stays safe, on deck and past no wall.
    /// Remaking the choice every tick flips between equal ways out, and the reaction delay plays each flip out under the bomb.
    /// </summary>
    private static bool Holds(in MayhemCpuIntent planned, MankiMayhemDefinition stage, IReadOnlyList<RuntimeSolid> walls, float radius,
        float x, float z, IReadOnlyList<MayhemDanger> dangers)
        => planned.Action == CpuAction.Move && MayhemMath.Clearance(planned.X, planned.Z, dangers) >= 0f
            && MayhemMath.OnDeck(stage, planned.X, planned.Z, MayhemShowTuning.PlacementMargin)
            && !RuntimeSolidSet.Crosses(walls, x, z, planned.X, planned.Z, radius);

    /// <summary>
    /// The nearest ring of candidates with a safe point in a straight line past no wall, and on it the point closest to
    /// the destination; with none safe, the reachable candidate deepest out of every blast.
    /// </summary>
    private static (float X, float Z) Escape(MankiMayhemDefinition stage, IReadOnlyList<RuntimeSolid> walls, float radius,
        float x, float z, float destX, float destZ, IReadOnlyList<MayhemDanger> dangers)
    {
        float deepX = x, deepZ = z, deep = MayhemMath.Clearance(x, z, dangers);
        for (int ring = 1; ring <= SafeStepTuning.EscapeRings; ring++)
        {
            float bestX = 0, bestZ = 0, best = float.MaxValue;
            for (int i = 0; i < Directions; i++)
            {
                float yaw = i * (MathF.PI * 2f / Directions);
                float px = x + MathF.Sin(yaw) * SafeStepTuning.EscapeStep * ring, pz = z + MathF.Cos(yaw) * SafeStepTuning.EscapeStep * ring;
                if (!MayhemMath.OnDeck(stage, px, pz, MayhemShowTuning.PlacementMargin) || RuntimeSolidSet.Crosses(walls, x, z, px, pz, radius)) continue;
                float clearance = MayhemMath.Clearance(px, pz, dangers);
                if (clearance > deep) { deep = clearance; deepX = px; deepZ = pz; }
                if (clearance < 0f) continue;
                float score = MayhemMath.DistanceSquared(px, pz, destX, destZ);
                if (score < best) { best = score; bestX = px; bestZ = pz; }
            }
            if (best < float.MaxValue) return (bestX, bestZ);
        }
        return (deepX, deepZ);
    }
}
