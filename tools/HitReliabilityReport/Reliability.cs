using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.HitReliabilityReport;

/// <summary>One normal's measurements against standing roster targets.</summary>
internal sealed class MoveReliability
{
    public string Slot = "";
    public string Name = "";
    public bool Air;
    public ushort DurationTicks;
    public int BakedFrames;
    /// <summary>Baked 60 Hz animation frames played per gameplay tick (1 = authored speed).</summary>
    public float PlaybackSpeed;
    public int ActiveTicks;
    /// <summary>Active ticks whose hitboxes touch a standing medium target <see cref="Reliability.ContactProbeMeters"/> ahead.</summary>
    public int ContactTicksAtProbe;
    public List<ReachBand> Reach = new();
    public List<VerticalBand> Vertical = new();
    public List<SamplingRow> Sampling = new();
    /// <summary>Ground moves with several damaging hitboxes: where around the attacker each one can land.</summary>
    public List<HitCoverage> Hits = new();
    public List<string> Flags = new();
}

/// <summary>Contiguous starting distances (metres) at which a ground normal connects with an idle target.</summary>
internal sealed class ReachBand
{
    public string Target = "";
    public float BearingDegrees;
    public List<(float From, float To)> Intervals = new();
    public float? Nearest => Intervals.Count == 0 ? null : Intervals[0].From;
    public float? Farthest => Intervals.Count == 0 ? null : Intervals[^1].To;
}

/// <summary>Target feet heights, relative to the attacker's feet, that an aerial's active ticks can touch.</summary>
internal sealed class VerticalBand
{
    public string Target = "";
    public float? Lowest;
    public float? Highest;
}

/// <summary>Positions a bone or weapon hitbox visibly sweeps between ticks but never tests.</summary>
internal sealed class SamplingRow
{
    public int Event;
    public string Anchor = "";
    public float Radius;
    public float MaxTipStepPerTick;
    public float TargetFeetHeight;
    public int SweptPositions;
    public int TestedPositions;
    public double LostShare => SweptPositions == 0 ? 0 : (SweptPositions - TestedPositions) / (double)SweptPositions;
}

/// <summary>Bearings around the attacker at which one hit of a multi-hit ground move touches a standing target.</summary>
internal sealed class HitCoverage
{
    public int Event;
    public int FirstTick;
    public int LastTick;
    public float Damage;
    public float Radius;
    /// <summary>Per target and distance: bearings (of <see cref="Reliability.CoverageBearings"/>) any tested pose touches.</summary>
    public List<(string Target, float Distance, int Bearings)> Touched = new();
}

internal static class Reliability
{
    internal const float ContactProbeMeters = 1f;
    internal const float FastPlayback = 1.4f;
    internal const float PointBlankMeters = 0.45f;
    internal const float LowReachMeters = -0.5f;
    internal const double SamplingLossFlag = 0.1;
    /// <summary>Ignore sampling loss measured over too few swept positions to mean anything.</summary>
    internal const int MinSweptForFlag = 20;
    private const float Origin = 100f;
    private const int SubSteps = 8;
    /// <summary>Clip samples per tick for the reference swing path in <see cref="SamplingLoss"/>.</summary>
    private const int PathSamplesPerTick = 16;
    /// <summary>Evenly spaced bearings (15° apart) at which <see cref="Coverage"/> places a standing target.</summary>
    internal const int CoverageBearings = 24;
    internal static readonly float[] CoverageMeters = { 1f, 1.5f };
    internal static readonly byte[] NormalWires = { AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4 };
    /// <summary>Canonical slot keys and their input wire values, in report order.</summary>
    private static readonly (string Key, byte Wire)[] SlotKeys =
    {
        ("1", AbilitySlots.Slot1), ("2", AbilitySlots.Slot2), ("3", AbilitySlots.Slot3), ("4", AbilitySlots.Slot4),
        ("A", AbilitySlots.A), ("E", AbilitySlots.E), ("R", AbilitySlots.R), ("F", AbilitySlots.F),
    };

    /// <summary>Wire values for comma-separated slot keys (1–4, A, E, R, F); null if a key is unknown or none is given.</summary>
    internal static byte[]? ParseSlots(string text)
    {
        var wires = new List<byte>();
        foreach (string key in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = SlotKeys.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match.Key == null) return null;
            if (!wires.Contains(match.Wire)) wires.Add(match.Wire);
        }
        return wires.Count == 0 ? null : wires.ToArray();
    }

    private static string SlotKey(byte wire) => SlotKeys.First(x => x.Wire == wire).Key;

    internal static ArenaDefinition FlatArena() => new()
    {
        Name = "hit-reliability-flat", KillHeight = -1000, KillTop = 1000,
        KillMinX = -1000, KillMaxX = 1000, KillMinZ = -1000, KillMaxZ = 1000,
        SpawnPoints = new[] { new SpawnPoint { X = Origin, Z = Origin } },
        Heightmap = new ArenaHeightmap { Width = 256, Height = 256, CellSize = 1, Data = new float[256 * 256] },
    };

    internal static List<MoveReliability> Measure(MatchContentEntry attacker,
        IReadOnlyList<MatchContentEntry> targets, ArenaDefinition arena, IReadOnlyList<byte>? wires = null)
    {
        var volumes = targets.Select(t => (t, Volumes(t, arena))).ToList();
        var medium = volumes[0].Item2;
        var moves = new List<MoveReliability>();
        foreach (bool air in new[] { false, true })
        foreach (byte wire in wires ?? NormalWires)
        {
            var spec = attacker.Definition.GetSlotAbility(wire - 1, air);
            if (spec == null || spec.Stages.Length == 0) continue;
            var move = Describe(attacker, spec, wire, air);
            var samples = SampleActive(attacker, spec, wire, air);
            var probe = Shift(medium, 0f, 0f, ContactProbeMeters);
            move.ContactTicksAtProbe = samples.Count(tick => tick.Any(h => Touches(h, probe)));
            foreach (var (target, targetVolumes) in volumes)
            {
                if (!air)
                    foreach (float bearing in new[] { 0f, 30f })
                        move.Reach.Add(ReachAgainst(attacker, wire, target, bearing, arena));
                else
                    move.Vertical.Add(VerticalAgainst(samples, target.Definition.Class.ToString(), targetVolumes));
            }
            move.Sampling.AddRange(SamplingLoss(attacker, spec, wire, air, medium));
            if (!air) move.Hits.AddRange(Coverage(attacker, spec, wire, volumes));
            Flag(move);
            moves.Add(move);
        }
        return moves;
    }

    private static MoveReliability Describe(MatchContentEntry attacker, AbilitySpec spec, byte wire, bool air)
    {
        var stage = spec.Stages[0];
        string animation = spec.AnimationNames is { Length: > 0 } names ? names[0] : "";
        int frames = attacker.BakedAnimation?.FrameCountFor(animation) ?? 0;
        var events = stage.HitboxEvents ?? Array.Empty<HitboxEvent>();
        var active = new HashSet<int>();
        foreach (var evt in events.Where(x => x.Damage > 0f))
            for (int t = evt.TriggerTick; t < evt.TriggerTick + Math.Max((ushort)1, evt.DurationTicks); t++)
                active.Add(t);
        return new MoveReliability
        {
            Slot = (air ? "air." : "ground.") + SlotKey(wire),
            Name = spec.Name,
            Air = air,
            DurationTicks = stage.DurationTicks,
            BakedFrames = frames,
            PlaybackSpeed = frames > 0 && stage.DurationTicks > 0 ? frames / (float)stage.DurationTicks : 0f,
            ActiveTicks = active.Count,
        };
    }

    internal static void Flag(MoveReliability move)
    {
        string F(FormattableString text) => FormattableString.Invariant(text);
        if (move.PlaybackSpeed > FastPlayback)
            move.Flags.Add(F($"clip plays {move.PlaybackSpeed:F2}× authored speed"));
        foreach (var band in move.Reach.Where(x => x.BearingDegrees == 0f && x.Nearest > PointBlankMeters))
            move.Flags.Add(F($"misses {band.Target} closer than {band.Nearest:F2} m"));
        foreach (var band in move.Vertical.Where(x => x.Lowest == null || x.Lowest > LowReachMeters))
        {
            if (band.Lowest == null) move.Flags.Add($"never touches a standing {band.Target}");
            else if (band.Lowest > -0.05f) move.Flags.Add(F($"cannot reach below its feet vs {band.Target} (lowest target feet {band.Lowest:+0.0;-0.0;0} m)"));
            else move.Flags.Add(F($"reaches only {-band.Lowest:F1} m below its feet vs {band.Target}"));
        }
        foreach (var row in move.Sampling.Where(x => x.LostShare > SamplingLossFlag && x.SweptPositions >= MinSweptForFlag))
            move.Flags.Add(F($"{row.LostShare * 100:F0}% of swept positions untested (target feet {row.TargetFeetHeight:+0.0;-0.0;0} m)"));
        if (move.ActiveTicks > 0 && move.ContactTicksAtProbe == 0 && !move.Air)
            move.Flags.Add(F($"no active tick touches a target {ContactProbeMeters:F0} m ahead"));
        foreach (string target in move.Hits.SelectMany(x => x.Touched).Select(x => x.Target).Distinct())
        {
            var never = move.Hits.Where(h => h.Touched.Where(x => x.Target == target).All(x => x.Bearings == 0)).ToList();
            if (never.Count > 0)
                move.Flags.Add(F($"{never.Count} of {move.Hits.Count} hits ({string.Join(", ", never.Select(h => $"t{h.FirstTick}"))}) never touch a standing {target} {CoverageMeters[0]:0.#}–{CoverageMeters[^1]:0.#} m away"));
        }
    }

    /// <summary>Real-sim starting distances (0.30–4.50 m, 5 cm) that connect against an idle target.</summary>
    internal static ReachBand ReachAgainst(MatchContentEntry attacker, byte wire, MatchContentEntry target,
        float bearing, ArenaDefinition arena)
    {
        var hits = new List<float>();
        for (int i = 0; i <= 84; i++)
        {
            float distance = MathF.Round(0.30f + i * 0.05f, 2);
            if (ConnectsOnGround(attacker, wire, target, distance, bearing, arena)) hits.Add(distance);
        }
        return new ReachBand { Target = target.Definition.Class.ToString(), BearingDegrees = bearing, Intervals = Intervals(hits, 0.05f) };
    }

    private static bool ConnectsOnGround(MatchContentEntry attacker, byte wire, MatchContentEntry target,
        float distance, float bearing, ArenaDefinition arena)
    {
        var sim = new ServerSimulation(arena) { NoCooldownsEntityId = 1 };
        sim.SetTick(0);
        float yaw = bearing * MathF.PI / 180f;
        sim.RegisterEntity(1, attacker.Definition, new CharacterState
        {
            PX = Origin, PY = attacker.Definition.CapsuleHeight / 2, PZ = Origin,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2,
        }, attacker.BakedAnimation);
        sim.RegisterEntity(2, target.Definition, new CharacterState
        {
            PX = Origin + MathF.Sin(yaw) * distance, PY = target.Definition.CapsuleHeight / 2,
            PZ = Origin + MathF.Cos(yaw) * distance, FacingYaw = MathF.PI + yaw, AimYaw = MathF.PI + yaw,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2,
        }, target.BakedAnimation);
        short facing = (short)MathF.Round(MathF.IEEERemainder(180f + bearing, 360f) * 100f);
        var inputs = new Dictionary<ulong, InputState>(2);
        for (int tick = 0; tick < 160; tick++)
        {
            inputs[1] = tick == 0 ? new InputState { ActiveSlot = wire } : default;
            inputs[2] = new InputState { FacingYaw = facing, AimYaw = facing };
            sim.Tick(inputs);
            foreach (var hit in sim.LastTickHits)
                if (hit.OwnerEntityId == 1 && hit.TargetEntityId == 2 && !hit.Blocked && hit.Damage > 0f)
                    return true;
        }
        return false;
    }

    /// <summary>Feet heights −3…+2 m (10 cm) at which any active tick touches the target within 3 m.</summary>
    internal static VerticalBand VerticalAgainst(List<List<Hitbox>> samples, string target,
        IReadOnlyList<SpellResolver.EntityData> volumes)
    {
        var heights = new List<float>();
        for (int i = 0; i <= 50; i++)
        {
            float dy = MathF.Round(-3f + i * 0.1f, 1);
            bool touched = false;
            for (float d = 0f; d <= 3.01f && !touched; d += 0.1f)
                for (float bearing = -40f; bearing <= 40.1f && !touched; bearing += 10f)
                {
                    float yaw = bearing * MathF.PI / 180f;
                    var placed = Shift(volumes, MathF.Sin(yaw) * d, dy, MathF.Cos(yaw) * d);
                    touched = samples.Any(tick => tick.Any(h => Touches(h, placed)));
                }
            if (touched) heights.Add(dy);
        }
        return new VerticalBand
        {
            Target = target,
            Lowest = heights.Count == 0 ? null : heights.Min(),
            Highest = heights.Count == 0 ? null : heights.Max(),
        };
    }

    /// <summary>
    /// For each bone or weapon hitbox: grid positions (0.5–3 m ahead, ±40°) whose target the
    /// swing passes through between consecutive active ticks (the clip sampled 16 times per
    /// tick) versus positions the engine tests: each tick's pose plus its swept poses.
    /// </summary>
    internal static List<SamplingRow> SamplingLoss(MatchContentEntry attacker, AbilitySpec spec, byte wire, bool air,
        IReadOnlyList<SpellResolver.EntityData> medium)
    {
        var rows = new List<SamplingRow>();
        var events = spec.Stages[0].HitboxEvents ?? Array.Empty<HitboxEvent>();
        for (int e = 0; e < events.Length; e++)
        {
            var evt = events[e];
            if (evt.BoneName == null || evt.Damage <= 0f) continue;
            var (ticks, engine, path) = SweepEvent(attacker, spec, wire, air, evt);
            float maxStep = 0f;
            for (int i = 1; i < ticks.Count; i++)
                maxStep = Math.Max(maxStep, Distance(ticks[i].EndX, ticks[i].EndY, ticks[i].EndZ,
                    ticks[i - 1].EndX, ticks[i - 1].EndY, ticks[i - 1].EndZ));
            foreach (float dy in air ? new[] { 0f, -1f, -1.5f } : new[] { 0f })
            {
                var row = new SamplingRow
                {
                    Event = e, Anchor = evt.EndBoneName == null ? evt.BoneName : $"{evt.BoneName}→{evt.EndBoneName}",
                    Radius = evt.Radius, MaxTipStepPerTick = maxStep, TargetFeetHeight = dy,
                };
                for (float d = 0.5f; d <= 3.01f; d += 0.1f)
                    for (float bearing = -40f; bearing <= 40.1f; bearing += 10f)
                    {
                        float yaw = bearing * MathF.PI / 180f;
                        var placed = Shift(medium, MathF.Sin(yaw) * d, dy, MathF.Cos(yaw) * d);
                        bool tested = engine.Any(h => Touches(h, placed));
                        bool swept = tested || path.Any(h => Touches(h, placed));
                        if (swept) row.SweptPositions++;
                        if (tested) row.TestedPositions++;
                    }
                rows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>
    /// One hitbox over its active ticks, attacker fixed at the origin: each tick's pose, the poses the
    /// engine tests (each tick's pose plus the swept poses between ticks), and the clip sampled
    /// <see cref="PathSamplesPerTick"/> times per tick as the reference swing path.
    /// </summary>
    private static (List<Hitbox> Ticks, List<Hitbox> Engine, List<Hitbox> Path) SweepEvent(
        MatchContentEntry attacker, AbilitySpec spec, byte wire, bool air, HitboxEvent evt)
    {
        var ticks = new List<Hitbox>();
        var engine = new List<Hitbox>();
        var path = new List<Hitbox>();
        HitboxSweepOrigin? origin = null;
        for (int t = evt.TriggerTick; t < evt.TriggerTick + Math.Max((ushort)1, evt.DurationTicks); t++)
        {
            var hitbox = Resolve(attacker, spec, wire, air, evt, t);
            ticks.Add(hitbox);
            engine.Add(hitbox);
            var owner = OriginState(attacker, t);
            var pose = new HitboxPose { X = hitbox.X, Y = hitbox.Y, Z = hitbox.Z, EndX = hitbox.EndX, EndY = hitbox.EndY, EndZ = hitbox.EndZ };
            if (origin is { } from)
            {
                for (int k = 1; k < PathSamplesPerTick; k++)
                    path.Add(ResolveAt(attacker, spec, wire, air, evt, owner, t - 1 + k / (float)PathSamplesPerTick));
                var sweep = HitboxGeometry.SweepPoses(evt, attacker.BakedAnimation, attacker.Definition,
                    spec.AnimationNames, 0, (byte)(wire - 1), air, in from, in owner, in pose);
                if (sweep != null)
                    foreach (var swept in sweep)
                    {
                        var probe = hitbox;
                        probe.X = swept.X; probe.Y = swept.Y; probe.Z = swept.Z;
                        probe.EndX = hitbox.Shape == HitboxShape.Capsule ? swept.EndX : swept.X;
                        probe.EndY = hitbox.Shape == HitboxShape.Capsule ? swept.EndY : swept.Y;
                        probe.EndZ = hitbox.Shape == HitboxShape.Capsule ? swept.EndZ : swept.Z;
                        engine.Add(probe);
                    }
            }
            origin = new HitboxSweepOrigin
            {
                PX = owner.PX, PY = owner.PY, PZ = owner.PZ,
                AttackElapsedTicks = owner.AttackElapsedTicks, Pose = pose,
            };
        }
        return (ticks, engine, path);
    }

    /// <summary>
    /// For a ground move with several damaging hitboxes: the bearings at which each one touches a standing
    /// target <see cref="CoverageMeters"/> away, from the poses the engine tests, with the attacker fixed and
    /// the target never knocked away.
    /// </summary>
    internal static List<HitCoverage> Coverage(MatchContentEntry attacker, AbilitySpec spec, byte wire,
        IReadOnlyList<(MatchContentEntry Target, List<SpellResolver.EntityData> Volumes)> targets)
    {
        var rows = new List<HitCoverage>();
        var events = spec.Stages[0].HitboxEvents ?? Array.Empty<HitboxEvent>();
        if (events.Count(x => x.Damage > 0f) < 2) return rows;
        for (int e = 0; e < events.Length; e++)
        {
            var evt = events[e];
            if (evt.Damage <= 0f) continue;
            var tested = SweepEvent(attacker, spec, wire, false, evt).Engine;
            var row = new HitCoverage
            {
                Event = e, FirstTick = evt.TriggerTick, LastTick = evt.TriggerTick + Math.Max((ushort)1, evt.DurationTicks) - 1,
                Damage = evt.Damage, Radius = evt.Radius,
            };
            foreach (var (target, volumes) in targets)
                foreach (float distance in CoverageMeters)
                    row.Touched.Add((target.Definition.Class.ToString(), distance, BearingsTouched(tested, volumes, distance)));
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>How many of <see cref="CoverageBearings"/> evenly spaced bearings place the target where a tested pose touches it.</summary>
    internal static int BearingsTouched(IReadOnlyList<Hitbox> tested, IReadOnlyList<SpellResolver.EntityData> volumes, float distance)
    {
        int count = 0;
        for (int i = 0; i < CoverageBearings; i++)
        {
            float yaw = i * 2f * MathF.PI / CoverageBearings;
            var placed = Shift(volumes, MathF.Sin(yaw) * distance, 0f, MathF.Cos(yaw) * distance);
            if (tested.Any(h => Touches(h, placed))) count++;
        }
        return count;
    }

    internal static bool Swept(IReadOnlyList<Hitbox> ticks, IReadOnlyList<SpellResolver.EntityData> volumes)
    {
        for (int i = 0; i + 1 < ticks.Count; i++)
            for (int k = 1; k < SubSteps; k++)
                if (Touches(Lerp(ticks[i], ticks[i + 1], k / (float)SubSteps), volumes))
                    return true;
        return false;
    }

    internal static List<(float From, float To)> Intervals(IReadOnlyList<float> values, float step)
    {
        var result = new List<(float From, float To)>();
        foreach (float value in values.OrderBy(x => x))
        {
            if (result.Count > 0 && value - result[^1].To <= step * 1.5f) result[^1] = (result[^1].From, value);
            else result.Add((value, value));
        }
        return result;
    }

    /// <summary>Active ticks' tested hitboxes in the grounded origin frame (attacker at the origin, facing +Z).</summary>
    private static List<List<Hitbox>> SampleActive(MatchContentEntry attacker, AbilitySpec spec, byte wire, bool air)
    {
        var events = (spec.Stages[0].HitboxEvents ?? Array.Empty<HitboxEvent>()).Where(x => x.Damage > 0f).ToArray();
        if (events.Length == 0) return new List<List<Hitbox>>();
        int first = events.Min(x => x.TriggerTick);
        int last = events.Max(x => x.TriggerTick + Math.Max((ushort)1, x.DurationTicks) - 1);
        var ticks = new List<List<Hitbox>>();
        for (int t = first; t <= last; t++)
            ticks.Add(events.Where(x => t >= x.TriggerTick && t < x.TriggerTick + Math.Max((ushort)1, x.DurationTicks))
                .Select(x => Resolve(attacker, spec, wire, air, x, t)).ToList());
        return ticks;
    }

    /// <summary>The attacker standing at the origin, facing +Z, <paramref name="tick"/> ticks into the attack.</summary>
    private static CharacterState OriginState(MatchContentEntry attacker, int tick) => new()
    {
        PX = Origin, PY = attacker.Definition.CapsuleHeight * 0.5f, PZ = Origin,
        AttackElapsedTicks = (ushort)tick,
    };

    /// <summary>The hitbox at a fractional attack time, for the reference swing path.</summary>
    private static Hitbox ResolveAt(MatchContentEntry attacker, AbilitySpec spec, byte wire, bool air, HitboxEvent evt,
        in CharacterState state, float elapsedTicks)
    {
        HitboxGeometry.ResolvePositionsAt(state, evt, attacker.BakedAnimation, attacker.Definition, spec.AnimationNames,
            0, (byte)(wire - 1), air, elapsedTicks, out float x, out float y, out float z, out float ex, out float ey, out float ez);
        bool capsule = evt.Shape == HitboxShape.Capsule || evt.EndBoneName != null;
        return new Hitbox
        {
            X = x, Y = y, Z = z, EndX = capsule ? ex : x, EndY = capsule ? ey : y, EndZ = capsule ? ez : z,
            Radius = evt.Radius, Shape = capsule ? HitboxShape.Capsule : HitboxShape.Sphere,
        };
    }

    private static Hitbox Resolve(MatchContentEntry attacker, AbilitySpec spec, byte wire, bool air, HitboxEvent evt, int tick)
    {
        var state = OriginState(attacker, tick);
        HitboxGeometry.ResolvePositions(state, evt, attacker.BakedAnimation, attacker.Definition, spec.AnimationNames,
            0, (byte)(wire - 1), air, out float x, out float y, out float z, out float ex, out float ey, out float ez);
        bool capsule = evt.Shape == HitboxShape.Capsule || evt.EndBoneName != null;
        return new Hitbox
        {
            X = x, Y = y, Z = z, EndX = capsule ? ex : x, EndY = capsule ? ey : y, EndZ = capsule ? ez : z,
            Radius = evt.Radius, Shape = capsule ? HitboxShape.Capsule : HitboxShape.Sphere,
        };
    }

    /// <summary>The target's contact volumes standing idle at the origin, facing the attacker (−Z).</summary>
    internal static List<SpellResolver.EntityData> Volumes(MatchContentEntry target, ArenaDefinition arena)
    {
        var sim = new ServerSimulation(arena);
        sim.SetTick(0);
        sim.RegisterEntity(2, target.Definition, new CharacterState
        {
            PX = Origin, PY = target.Definition.CapsuleHeight / 2, PZ = Origin, FacingYaw = MathF.PI, AimYaw = MathF.PI,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2,
        }, target.BakedAnimation);
        sim.Tick(new Dictionary<ulong, InputState> { [2] = new InputState { FacingYaw = 18000, AimYaw = 18000 } });
        return sim.LastTickAttackEntities.Where(x => x.Id == 2 && !x.ShieldSurface).ToList();
    }

    private static List<SpellResolver.EntityData> Shift(IReadOnlyList<SpellResolver.EntityData> volumes, float dx, float dy, float dz)
        => volumes.Select(v =>
        {
            v.PosX += dx; v.EndX += dx; v.PosY += dy; v.EndY += dy; v.PosZ += dz; v.EndZ += dz;
            return v;
        }).ToList();

    private static bool Touches(in Hitbox hitbox, IReadOnlyList<SpellResolver.EntityData> volumes)
    {
        foreach (var volume in volumes)
            if (SpellResolver.SurfaceGap(in hitbox, volume, out _, out _, out _) <= 0f) return true;
        return false;
    }

    private static Hitbox Lerp(in Hitbox a, in Hitbox b, float s) => new()
    {
        X = a.X + (b.X - a.X) * s, Y = a.Y + (b.Y - a.Y) * s, Z = a.Z + (b.Z - a.Z) * s,
        EndX = a.EndX + (b.EndX - a.EndX) * s, EndY = a.EndY + (b.EndY - a.EndY) * s, EndZ = a.EndZ + (b.EndZ - a.EndZ) * s,
        Radius = a.Radius, Shape = a.Shape,
    };

    private static float Distance(float x0, float y0, float z0, float x1, float y1, float z1)
        => MathF.Sqrt((x0 - x1) * (x0 - x1) + (y0 - y1) * (y0 - y1) + (z0 - z1) * (z0 - z1));
}
