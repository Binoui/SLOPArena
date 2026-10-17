using System;

namespace SlopArena.Shared;

/// <summary>
/// Issue #151 — deterministic authored hitbox reach chart geometry. The pure complement
/// to the MoveDataReport tool's empirical heat spots: for every collected normal, resolve
/// the real sim hitbox volumes over the active frames and answer "what does this move
/// cover, and where are the gaps in the kit?".
///
/// Geometry source of truth is <see cref="HitboxGeometry.ResolvePositions"/> — the exact
/// function <c>ServerAbility.SpawnHitbox</c> uses — with the character standing at the
/// grounded origin frame: PX = 0, PY = CapsuleHeight/2, PZ = 0, FacingYaw = 0 (forward is
/// +Z). Attack frames are resolved at AttackElapsedTicks = TriggerTick + t.
/// </summary>
public static class MoveReach
{
    /// <summary>Height sampling step for <see cref="BandExtent"/>, metres.</summary>
    public const float CellSize = 0.05f;

    /// <summary>Subdivisions of the capsule axis for <see cref="ExtentAt"/> (32 → extent
    /// error &lt; 1 cm for ≤ 3 m capsules).</summary>
    private const int AxisSamples = 32;

    /// <summary>One world-space capsule sample: the hitbox's start/end endpoints at one
    /// active tick, plus its radius. A sphere is a degenerate capsule (endpoints coincide).</summary>
    public readonly record struct CapsuleSample(int Tick, float X0, float Y0, float Z0,
        float X1, float Y1, float Z1, float Radius);

    /// <summary>
    /// Resolve one HitboxEvent's capsule for every tick of its active window (min 1 tick) in
    /// the grounded origin frame via <see cref="HitboxGeometry.ResolvePositions"/>.
    /// <paramref name="index"/> is the tick offset; <c>sample.Tick</c> = TriggerTick + offset.
    /// </summary>
    /// <param name="slot">Ability slot index (0-based, <see cref="CharacterDefinition.GetSlotAbility"/>).</param>
    /// <param name="airborne">True when the ability is an air slot — selects the air spec's stage duration.</param>
    public static CapsuleSample[] SampleHit(CharacterDefinition def, in HitboxEvent evt,
        byte slot, bool airborne, string[]? animationNames, byte animIndex, BakedAnimationData? baked)
    {
        int ticks = Math.Max(1, (int)evt.DurationTicks);
        var samples = new CapsuleSample[ticks];
        float py = def.CapsuleHeight * 0.5f;
        // AttackElapsedTicks counts from the activation; TriggerTick counts from its stage.
        int stageStart = 0;
        var spec = def.GetSlotAbility(slot, airborne);
        if (spec != null)
            for (int i = 0; i < animIndex && i < spec.Stages.Length; i++)
                stageStart += spec.Stages[i].DurationTicks;
        for (int t = 0; t < ticks; t++)
        {
            var s = new CharacterState
            {
                PX = 0f, PY = py, PZ = 0f, FacingYaw = 0f,
                AttackElapsedTicks = (ushort)(stageStart + evt.TriggerTick + t),
            };
            HitboxGeometry.ResolvePositions(s, evt, baked, def, animationNames, animIndex, slot, airborne,
                out float wx, out float wy, out float wz,
                out float wex, out float wey, out float wez);
            samples[t] = new CapsuleSample(evt.TriggerTick + t,
                wx, wy, wz, wex, wey, wez, evt.Radius);
        }
        return samples;
    }

    /// <summary>
    /// Min/max forward (Z) extent of the capsule union at world height y. Null when no
    /// capsule reaches that height. For each capsule, sample the axis at
    /// <see cref="AxisSamples"/>+1 points (s = i/AxisSamples), and for each point with
    /// |y − y(s)| ≤ r take z(s) ± sqrt(r² − (y−y(s))²); min of (z−sqrt), max of (z+sqrt).
    /// X is free (side-view projection).
    /// </summary>
    public static (float MinZ, float MaxZ)? ExtentAt(ReadOnlySpan<CapsuleSample> samples, float y)
    {
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        bool any = false;
        foreach (var c in samples)
        {
            float dx = c.X1 - c.X0, dy = c.Y1 - c.Y0, dz = c.Z1 - c.Z0;
            float lenSq = dx * dx + dy * dy + dz * dz;
            for (int i = 0; i <= AxisSamples; i++)
            {
                float s = (float)i / AxisSamples;
                float yAt = c.Y0 + dy * s;
                float d = y - yAt;
                float abs = d < 0f ? -d : d;
                if (abs > c.Radius) continue;
                float zAt = c.Z0 + dz * s;
                float r2 = c.Radius * c.Radius - d * d;
                float half = r2 > 0f ? MathF.Sqrt(r2) : 0f;
                float lo = zAt - half, hi = zAt + half;
                if (lo < minZ) minZ = lo;
                if (hi > maxZ) maxZ = hi;
                any = true;
            }
        }
        return any ? (minZ, maxZ) : null;
    }

    /// <summary>
    /// Min/max forward extent across the band [yMin, yMax]: min of MinZ / max of MaxZ over
    /// <see cref="ExtentAt"/> at <see cref="CellSize"/> steps (yMin, yMin+step, …, ≤ yMax).
    /// Null when every step is null.
    /// </summary>
    public static (float MinZ, float MaxZ)? BandExtent(ReadOnlySpan<CapsuleSample> samples,
        float yMin, float yMax, float step = CellSize)
    {
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        bool any = false;
        int n = (int)Math.Ceiling((yMax - yMin) / step);
        for (int i = 0; i <= n; i++)
        {
            float y = MathF.Min(yMin + i * step, yMax);
            var ext = ExtentAt(samples, y);
            if (ext == null) continue;
            if (ext.Value.MinZ < minZ) minZ = ext.Value.MinZ;
            if (ext.Value.MaxZ > maxZ) maxZ = ext.Value.MaxZ;
            any = true;
        }
        return any ? (minZ, maxZ) : null;
    }
}
