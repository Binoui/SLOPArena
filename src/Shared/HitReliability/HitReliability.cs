using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public static class HitReliability
{
    public const float ContactProbeMeters = 1f;
    internal const float FastPlayback = 1.4f;
    internal const float PointBlankMeters = 0.45f;
    internal const float LowReachMeters = -0.5f;
    internal const double SamplingLossFlag = 0.1;
    /// <summary>Ignore sampling loss measured over too few swept positions to mean anything.</summary>
    internal const int MinSweptForFlag = 20;
    private const float Origin = 100f;
    internal const float OriginMeters = Origin;
    /// <summary>Clip samples per tick for the reference swing path in <see cref="SamplingLoss"/>.</summary>
    private const int PathSamplesPerTick = 16;
    /// <summary>Evenly spaced bearings (15° apart) at which <see cref="Coverage"/> places a standing target.</summary>
    public const int CoverageBearings = 24;
    internal static readonly float[] CoverageMeters = { 1f, 1.5f };
    internal static ArenaDefinition FlatArena() => new()
    {
        Name = "hit-reliability-flat", KillHeight = -1000, KillTop = 1000,
        KillMinX = -1000, KillMaxX = 1000, KillMinZ = -1000, KillMaxZ = 1000,
        SpawnPoints = new[] { new SpawnPoint { X = Origin, Z = Origin } },
        Heightmap = new ArenaHeightmap { Width = 256, Height = 256, CellSize = 1, Data = new float[256 * 256] },
    };

    public static MoveReliability? Measure(CharacterDefinition attacker, BakedAnimationData? poses, SlotAddress slot,
        HitReliabilityTargets targets)
    {
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (targets == null) throw new ArgumentNullException(nameof(targets));
        byte wire = CanonicalSlotProjection.ToWire(slot);
        bool air = slot.IsAirborne;
        var arena = FlatArena();
        var medium = targets.Medium.Volumes;
        var spec = attacker.GetCookedSlotAbility(wire, air);
        if (spec == null || spec.Timeline.Stages.Count == 0) return null;
        var move = Describe(attacker, poses, spec, wire, air);
        var samples = SampleActive(attacker, poses, spec, wire, air);
        var probe = Shift(medium, 0f, 0f, ContactProbeMeters);
        move.ContactTicksAtProbe = samples.Count(tick => tick.Any(h => Touches(h, probe)));
        foreach (var target in targets.All)
        {
            if (!air)
                foreach (float bearing in new[] { 0f, 30f })
                    move.Reach.Add(ReachAgainst(attacker, poses, wire, target, bearing, arena));
            else
                move.Vertical.Add(VerticalAgainst(samples, target.Name, target.Volumes));
        }
        move.Sampling.AddRange(SamplingLoss(attacker, poses, spec, wire, air, medium));
        if (!air) move.Hits.AddRange(Coverage(attacker, poses, spec, wire, targets));
        if (!air)
            foreach (var target in targets.All)
                move.Arc.Add(ArcAgainst(samples, target.Name, target.Volumes));
        int firstActive = (spec.Timeline.Stages[0].ReadHitboxes() ?? Array.Empty<HitboxEvent>()).Where(x => x.Damage > 0f)
            .Select(x => (int)x.TriggerTick).DefaultIfEmpty(0).Min();
        for (int i = 0; i < samples.Count; i++)
            foreach (var h in samples[i])
                move.ActiveHitboxes.Add(new[] { firstActive + i, h.X - Origin, h.Y, h.Z - Origin, h.EndX - Origin, h.EndY, h.EndZ - Origin, h.Radius });
        move.Swing.AddRange(MeasureSwing(attacker, poses, spec, wire, air));
        Flag(move);
        return move;
    }

    private static MoveReliability Describe(CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire, bool air)
    {
        var stage = spec.Timeline.Stages[0];
        string animation = spec.ClipCount > 0 ? spec.Clip(0).AnimationId : "";
        int frames = poses?.FrameCountFor(animation) ?? 0;
        var events = stage.ReadHitboxes() ?? Array.Empty<HitboxEvent>();
        var active = new HashSet<int>();
        foreach (var evt in events.Where(x => x.Damage > 0f))
            for (int t = evt.TriggerTick; t < evt.TriggerTick + Math.Max((ushort)1, evt.DurationTicks); t++)
                active.Add(t);
        return new MoveReliability
        {
            Slot = (CanonicalSlotProjection.TryGet(wire, air, out var address) ? address.Id : throw new ArgumentException(nameof(wire))),
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
        foreach (var swing in move.Swing)
            move.Flags.AddRange(swing.Flags);
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

    internal static List<SwingRow> MeasureSwing(CharacterDefinition attacker, BakedAnimationData? poses,
        CookedSlotDefinition spec, byte wire, bool air)
    {
        var rows = new List<SwingRow>();
        var stage = spec.Timeline.Stages[0];
        if (poses == null || spec.ClipCount == 0 || poses.FrameCountFor(spec.Clip(0).AnimationId) == 0) return rows;
        var events = stage.ReadHitboxes() ?? Array.Empty<HitboxEvent>();
        for (int e = 0; e < events.Length; e++)
        {
            var evt = events[e];
            if (evt.Damage <= 0f || evt.BoneName == null ||
                (evt.EndBoneName == null && evt.Shape != HitboxShape.Capsule) ||
                poses.FindBoneIndex(evt.BoneName) < 0 ||
                (evt.EndBoneName != null && poses.FindBoneIndex(evt.EndBoneName) < 0)) continue;
            int first = evt.TriggerTick;
            int last = first + Math.Max((ushort)1, evt.DurationTicks) - 1;
            int end = Math.Min(stage.DurationTicks, last + 15);
            var row = new SwingRow
            {
                Event = e, Anchor = evt.EndBoneName == null ? evt.BoneName : $"{evt.BoneName}→{evt.EndBoneName}",
                FirstTick = first, LastTick = last, SampledThroughTick = end,
                PrePeakTick = Math.Max(1, first - 7), InPeakTick = first + 1,
            };
            double total = 0, inside = 0;
            var previous = Resolve(attacker, poses, spec, wire, air, evt, 0);
            // A point has no segment direction. Do not diagnose a sphere or zero-length capsule as a parked swing.
            if (Distance(previous.X, previous.Y, previous.Z, previous.EndX, previous.EndY, previous.EndZ) < 1e-6f) continue;
            for (int t = 1; t <= end; t++)
            {
                var current = Resolve(attacker, poses, spec, wire, air, evt, t);
                float ax = previous.EndX - previous.X, ay = previous.EndY - previous.Y, az = previous.EndZ - previous.Z;
                float bx = current.EndX - current.X, by = current.EndY - current.Y, bz = current.EndZ - current.Z;
                float length = MathF.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
                if (length < 1e-12f) { row = null; break; }
                float rotation = MathF.Acos(Math.Clamp((ax * bx + ay * by + az * bz) / length, -1f, 1f)) * 180f / MathF.PI;
                float tipStep = Distance(previous.EndX, previous.EndY, previous.EndZ, current.EndX, current.EndY, current.EndZ);
                total += rotation;
                if (t >= first - 7 && t <= first && rotation > row.PrePeakDegrees)
                { row.PrePeakTick = t; row.PrePeakDegrees = rotation; }
                if (t > first && t <= last)
                {
                    inside += rotation;
                    if (rotation > row.InPeakDegrees) { row.InPeakTick = t; row.InPeakDegrees = rotation; }
                    if (rotation < 5f && tipStep < 0.15f) row.ParkedTicks++;
                }
                previous = current;
            }
            if (row == null) continue;
            row.InsideShare = total > 0 ? inside / total : 0;
            if (row.PrePeakDegrees > 1.5f * row.InPeakDegrees && row.PrePeakDegrees >= 20f)
                row.Flags.Add(FormattableString.Invariant($"swing peaks before the hitbox (t{row.PrePeakTick}, {row.PrePeakDegrees:0.#}°/tick)"));
            if (row.ParkedTicks >= (row.ActiveTicks + 2) / 3)
                row.Flags.Add($"{row.ParkedTicks} of {row.ActiveTicks} active ticks parked");
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Real-sim starting distances (0.30–4.50 m, 5 cm) that connect against an idle target.</summary>
    internal static ReachBand ReachAgainst(CharacterDefinition attacker, BakedAnimationData? poses, byte wire, HitReliabilityTarget target,
        float bearing, ArenaDefinition arena)
    {
        var hits = new List<float>();
        for (int i = 0; i <= 84; i++)
        {
            float distance = MathF.Round(0.30f + i * 0.05f, 2);
            if (ConnectsOnGround(attacker, poses, wire, target, distance, bearing, arena)) hits.Add(distance);
        }
        return new ReachBand { Target = target.Name, BearingDegrees = bearing, Intervals = Intervals(hits, 0.05f) };
    }

    private static bool ConnectsOnGround(CharacterDefinition attacker, BakedAnimationData? poses, byte wire, HitReliabilityTarget target,
        float distance, float bearing, ArenaDefinition arena)
    {
        var sim = new ServerSimulation(arena) { NoCooldownsEntityId = 1 };
        sim.SetTick(0);
        float yaw = bearing * MathF.PI / 180f;
        sim.RegisterEntity(1, attacker, new CharacterState
        {
            PX = Origin, PY = attacker.CapsuleHeight / 2, PZ = Origin,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2,
            Form = new FormState { Active = attacker.Form },
        }, poses);
        sim.RegisterEntity(2, target.Definition, new CharacterState
        {
            PX = Origin + MathF.Sin(yaw) * distance, PY = target.Definition.CapsuleHeight / 2,
            PZ = Origin + MathF.Cos(yaw) * distance, FacingYaw = MathF.PI + yaw, AimYaw = MathF.PI + yaw,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = 2,
        }, target.Poses);
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

    internal static readonly float[] ArcDistances = Enumerable.Range(0, 25).Select(i => -2.5f + i * 0.25f).ToArray();
    internal static readonly float[] ArcHeights = Enumerable.Range(0, 13).Select(i => 3f - i * 0.25f).ToArray();

    /// <summary>The target, idle on the attacker's facing line, at every <see cref="ArcDistances"/> (behind to ahead)
    /// and feet height <see cref="ArcHeights"/> (highest first): touched by any active tick.</summary>
    internal static ArcMap ArcAgainst(List<List<Hitbox>> samples, string target, IReadOnlyList<SpellResolver.EntityData> volumes)
    {
        var map = new ArcMap { Target = target, Distances = ArcDistances.ToList(), Heights = ArcHeights.ToList() };
        foreach (float dy in ArcHeights)
            map.Rows.Add(new string(ArcDistances.Select(d =>
            {
                var placed = Shift(volumes, 0f, dy, d);
                return samples.Any(tick => tick.Any(h => Touches(h, placed))) ? '#' : '.';
            }).ToArray()));
        return map;
    }

    /// <summary>
    /// For each bone or weapon hitbox: grid positions (0.5–3 m ahead, ±40°) whose target the
    /// swing passes through between consecutive active ticks (the clip sampled 16 times per
    /// tick) versus positions the engine tests: each tick's pose plus its swept poses.
    /// </summary>
    internal static List<SamplingRow> SamplingLoss(CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire, bool air,
        IReadOnlyList<SpellResolver.EntityData> medium)
    {
        var rows = new List<SamplingRow>();
        var events = spec.Timeline.Stages[0].ReadHitboxes() ?? Array.Empty<HitboxEvent>();
        for (int e = 0; e < events.Length; e++)
        {
            var evt = events[e];
            if (evt.BoneName == null || evt.Damage <= 0f) continue;
            var (ticks, engine, path) = SweepEvent(attacker, poses, spec, wire, air, evt);
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
        CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire, bool air, HitboxEvent evt)
    {
        var ticks = new List<Hitbox>();
        var engine = new List<Hitbox>();
        var path = new List<Hitbox>();
        HitboxSweepOrigin? origin = null;
        for (int t = evt.TriggerTick; t < evt.TriggerTick + Math.Max((ushort)1, evt.DurationTicks); t++)
        {
            var hitbox = Resolve(attacker, poses, spec, wire, air, evt, t);
            ticks.Add(hitbox);
            engine.Add(hitbox);
            var owner = OriginState(attacker, poses, t);
            var pose = new HitboxPose { X = hitbox.X, Y = hitbox.Y, Z = hitbox.Z, EndX = hitbox.EndX, EndY = hitbox.EndY, EndZ = hitbox.EndZ };
            if (origin is { } from)
            {
                for (int k = 1; k < PathSamplesPerTick; k++)
                    path.Add(ResolveAt(attacker, poses, spec, wire, air, evt, owner, t - 1 + k / (float)PathSamplesPerTick));
                var sweep = HitboxGeometry.SweepPoses(evt, poses, attacker, attacker.GetCookedSlotAbility(wire, air), 0, in from, in owner, in pose);
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
    internal static List<HitCoverage> Coverage(CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire,
        HitReliabilityTargets targets)
    {
        var rows = new List<HitCoverage>();
        var events = spec.Timeline.Stages[0].ReadHitboxes() ?? Array.Empty<HitboxEvent>();
        if (events.Count(x => x.Damage > 0f) < 2) return rows;
        for (int e = 0; e < events.Length; e++)
        {
            var evt = events[e];
            if (evt.Damage <= 0f) continue;
            var tested = SweepEvent(attacker, poses, spec, wire, false, evt).Engine;
            var row = new HitCoverage
            {
                Event = e, FirstTick = evt.TriggerTick, LastTick = evt.TriggerTick + Math.Max((ushort)1, evt.DurationTicks) - 1,
                Damage = evt.Damage, Radius = evt.Radius,
            };
            foreach (var target in targets.All)
                foreach (float distance in CoverageMeters)
                    row.Touched.Add((target.Name, distance, BearingsTouched(tested, target.Volumes, distance)));
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
    private static List<List<Hitbox>> SampleActive(CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire, bool air)
    {
        var events = (spec.Timeline.Stages[0].ReadHitboxes() ?? Array.Empty<HitboxEvent>()).Where(x => x.Damage > 0f).ToArray();
        if (events.Length == 0) return new List<List<Hitbox>>();
        int first = events.Min(x => x.TriggerTick);
        int last = events.Max(x => x.TriggerTick + Math.Max((ushort)1, x.DurationTicks) - 1);
        var ticks = new List<List<Hitbox>>();
        for (int t = first; t <= last; t++)
            ticks.Add(events.Where(x => t >= x.TriggerTick && t < x.TriggerTick + Math.Max((ushort)1, x.DurationTicks))
                .Select(x => Resolve(attacker, poses, spec, wire, air, x, t)).ToList());
        return ticks;
    }

    /// <summary>The attacker standing at the origin, facing +Z, <paramref name="tick"/> ticks into the attack.</summary>
    private static CharacterState OriginState(CharacterDefinition attacker, BakedAnimationData? poses, int tick) => new()
    {
        PX = Origin, PY = attacker.CapsuleHeight * 0.5f, PZ = Origin,
        AttackElapsedTicks = (ushort)tick,
        Form = new FormState { Active = attacker.Form },
    };

    /// <summary>The hitbox at a fractional attack time, for the reference swing path.</summary>
    private static Hitbox ResolveAt(CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire, bool air, HitboxEvent evt,
        in CharacterState state, float elapsedTicks)
    {
        HitboxGeometry.ResolvePositionsAt(state, evt, poses, attacker, attacker.GetCookedSlotAbility(wire, air), 0, elapsedTicks, out float x, out float y, out float z, out float ex, out float ey, out float ez);
        bool capsule = evt.Shape == HitboxShape.Capsule || evt.EndBoneName != null;
        return new Hitbox
        {
            X = x, Y = y, Z = z, EndX = capsule ? ex : x, EndY = capsule ? ey : y, EndZ = capsule ? ez : z,
            Radius = evt.Radius, Shape = capsule ? HitboxShape.Capsule : HitboxShape.Sphere,
        };
    }

    private static Hitbox Resolve(CharacterDefinition attacker, BakedAnimationData? poses, CookedSlotDefinition spec, byte wire, bool air, HitboxEvent evt, int tick)
    {
        var state = OriginState(attacker, poses, tick);
        HitboxGeometry.ResolvePositions(state, evt, poses, attacker, attacker.GetCookedSlotAbility(wire, air), 0, out float x, out float y, out float z, out float ex, out float ey, out float ez);
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

    private static float Distance(float x0, float y0, float z0, float x1, float y1, float z1)
        => MathF.Sqrt((x0 - x1) * (x0 - x1) + (y0 - y1) * (y0 - y1) + (z0 - z1) * (z0 - z1));
}
