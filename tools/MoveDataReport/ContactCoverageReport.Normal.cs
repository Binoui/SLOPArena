#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;
using SlopArena.Shared;

namespace SlopArena.MoveDataReport;

internal static partial class ContactCoverageReport
{
    private const ulong NormalAttackerId = 1;
    private const ulong NormalVictimId = 100;
    private const float NormalOriginX = 100f;
    private const float NormalOriginZ = 100f;
    private const float NormalPhaseThreshold = 0.05f;
    private static readonly string[] NormalJumpStyles = { "neutral", "neutral-drift", "running" };

    internal sealed record CoveragePosition(float X, float Y, float Z);

    internal sealed class NormalCoverageOptions
    {
        public IReadOnlyList<SlotAddress> Slots { get; init; } = CanonicalSlotProjection.All
            .Where(x => x.InputLabel is "1" or "2" or "3")
            .OrderBy(x => x.Ordinal).ToArray();
        public IReadOnlyList<float> X { get; init; } = new[] { -1f, 0f, 1f };
        public IReadOnlyList<float> Y { get; init; } = new[] { 0f, 1f, 2f };
        public IReadOnlyList<float> Z { get; init; } = new[] { 0.75f, 1.5f, 2.25f };
        public IReadOnlyList<CoveragePosition> ExtraPositions { get; init; } = Array.Empty<CoveragePosition>();

        public int MaxTicks { get; init; } = Program.MaxTicks;
    }

    internal sealed class NormalCoverageReportData
    {
        public int SchemaVersion { get; init; } = 1;
        public string Experiment { get; init; } = "normals";
        public int ProtocolVersion { get; init; } = 1;
        public int TickRateHz { get; init; } = 60;
        public CoverageIdentityData Attacker { get; init; } = new();
        public CoverageIdentityData Victim { get; init; } = new();
        public NormalCoverageSettings Settings { get; init; } = new();
        public NormalArenaData Arena { get; init; } = new();
        public List<NormalCoveragePosition> Positions { get; } = new();
        public List<NormalCoverageMove> Moves { get; } = new();
        public List<NormalJumpReference> JumpReferences { get; } = new();
        public List<NormalCoverageSample> Samples { get; } = new();
        public NormalCoverageTotals Totals { get; set; } = new();
        public List<NormalCoverageSummary> Summaries { get; } = new();
        public List<string> Cautions { get; } = new();
    }

    internal sealed class NormalCoverageSettings
    {
        public float[] X { get; init; } = Array.Empty<float>();
        public float[] Y { get; init; } = Array.Empty<float>();
        public float[] Z { get; init; } = Array.Empty<float>();
        public List<CoveragePosition> ExtraPositions { get; } = new();
        public string[] SelectedSlots { get; init; } = Array.Empty<string>();
        public int MaxTicks { get; init; }
        public int PreparationTicks { get; init; } = 30;
        public string JumpHoldPolicy { get; init; } = "JumpHeld starts on the jump edge, remains held through the observed takeoff tick, then releases on the first post-takeoff tick";
        public string InputPolicy { get; init; } = "neutral edge at tick 0; neutral-drift begins +Z input after observed takeoff; running holds MoveY=1 for 30 preparation ticks and through flight";
        public string CoordinateConvention { get; init; } = "positions are target feet/offsets at the reference instant; +X is right and +Z is forward for yaw-zero";
        public string TickConvention { get; init; } = "tick 0 is the first simulation call; reference is the post-state at measured takeoff; ground press is tick 0";
        public string ReferenceConvention { get; init; } = "air targets register at the post-takeoff reference state and then fall under ordinary Shared physics; no target is registered during preparation";
        public string AimAndFacing { get; init; } = "attacker facing yaw 0, target facing PI, aim yaw/pitch 0, target lock and face snap disabled";
        public string InitialStatePolicy { get; init; } = "idle, ready resources, zero damage, neutral velocity, and no recovery/dash/fast-fall/second-jump edges";
        public string NeutralTargetPolicy { get; init; } = "target receives default input only; soft-target identity may be populated by Shared and is not an active lock";
        public string ToolAssemblySha256 { get; init; } = "";
        public string SimulationAssemblySha256 { get; init; } = "";
    }

    private static IReadOnlyList<float> ParseNormalAxis(string value, string axisName, bool rejectNegative)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new CoverageArgumentException($"--{axisName.ToLowerInvariant()} requires at least one value");
        var result = new List<float>();
        foreach (string token in value.Split(',', StringSplitOptions.None))
        {
            string trimmed = token.Trim();
            if (trimmed.Length == 0 || !float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                || !float.IsFinite(parsed))
                throw new CoverageArgumentException($"--{axisName.ToLowerInvariant()} contains malformed or non-finite value '{trimmed}'");
            if (rejectNegative && parsed < 0f)
                throw new CoverageArgumentException($"--{axisName.ToLowerInvariant()} Y values must be >= 0");
            if (!result.Contains(parsed)) result.Add(parsed);
        }
        return result;
    }

    private static IReadOnlyList<CoveragePosition> ParseNormalExtras(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new CoverageArgumentException("--extra requires at least one X,Y,Z triple");
        var result = new List<CoveragePosition>();
        foreach (string rawTriple in value.Split(';', StringSplitOptions.None))
        {
            string[] tokens = rawTriple.Split(',', StringSplitOptions.None);
            if (tokens.Length != 3)
                throw new CoverageArgumentException($"--extra contains malformed triple '{rawTriple}'");
            var values = new float[3];
            for (int i = 0; i < values.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0 || !float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                    || !float.IsFinite(values[i]))
                    throw new CoverageArgumentException($"--extra contains malformed or non-finite value '{token}'");
            }
            if (values[1] < 0f)
                throw new CoverageArgumentException("--extra Y values must be >= 0");
            result.Add(new CoveragePosition(values[0], values[1], values[2]));
        }
        return result;
    }

    internal sealed class NormalArenaData
    {
        public string Name { get; init; } = "";
        public string Intent { get; init; } = "no-respawn diagnostic arena; blast bounds are disabled for collection";
        public float FloorHeight { get; init; }
        public float HeightmapOriginX { get; init; }
        public float HeightmapOriginZ { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public float CellSize { get; init; }
        public float ReportOriginX { get; init; } = NormalOriginX;
        public float ReportOriginY { get; init; }
        public float ReportOriginZ { get; init; } = NormalOriginZ;
        public float EffectiveKillHeight { get; init; }
        public float EffectiveKillTop { get; init; }
        public float EffectiveKillMinX { get; init; }
        public float EffectiveKillMaxX { get; init; }
        public float EffectiveKillMinZ { get; init; }
        public float EffectiveKillMaxZ { get; init; }
    }

    internal sealed class NormalCoveragePosition
    {
        public int Index { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public bool IsExtra { get; init; }
    }

    internal sealed class NormalCoverageMove
    {
        public string SlotId { get; init; } = "";
        public int Ordinal { get; init; }
        public bool IsAirborne { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string? SupportReason { get; init; }
        public int AuthoredTimelineDurationTicks { get; init; }
    }

    internal sealed class NormalCheckpoint
    {
        public string Label { get; init; } = "";
        public int OffsetTicks { get; init; }
        public int AbsoluteTick { get; init; }
    }

    internal sealed class NormalJumpReference
    {
        public string ReferenceId { get; init; } = "";
        public string Attacker { get; init; } = "";
        public string JumpStyle { get; init; } = "";
        public int RunupTicks { get; init; }
        public int JumpEdgeTick { get; init; }
        public int TakeoffTick { get; init; }
        public int ApexTick { get; init; }
        public int LandingTick { get; init; }
        public int FlightTicks { get; init; }
        public CoverageStateSnapshot? TakeoffBefore { get; init; }
        public CoverageStateSnapshot? TakeoffAfter { get; init; }
        public CoverageStateSnapshot Apex { get; init; } = new();
        public CoverageStateSnapshot Landing { get; init; } = new();
        public List<NormalCheckpoint> Checkpoints { get; } = new();
        public List<string> CoincidentCheckpoints { get; } = new();
        public float TakeoffWorldX { get; init; }
        public float TakeoffWorldZ { get; init; }
    }

    internal sealed class NormalVector3Data
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
    }

    internal sealed class NormalCoverageSample
    {
        public string SlotId { get; init; } = "";
        public int PositionIndex { get; init; }
        public float StartingY { get; init; }
        public string? JumpStyle { get; init; }
        public string? Checkpoint { get; init; }
        public int ScheduledPressTick { get; init; }
        public int? ActualPressTick { get; set; }
        public bool AttackInputSent { get; set; }
        public string? ReferenceId { get; init; }
        public int ReferenceTick { get; init; }
        public CoverageStateSnapshot InitialAttacker { get; init; } = new();
        public CoverageStateSnapshot? BeforeTakeoffAttacker { get; set; }
        public CoverageStateSnapshot? AfterTakeoffAttacker { get; set; }
        public CoverageStateSnapshot? ReferenceAttacker { get; set; }
        public CoverageStateSnapshot? ReferenceVictim { get; set; }
        public NormalVector3Data? ReferenceTargetMinusAttacker { get; set; }
        public bool InitialBodyOverlap { get; set; }
        public CoverageStateSnapshot? PrePressAttacker { get; set; }
        public CoverageStateSnapshot? PrePressVictim { get; set; }
        public NormalVector3Data? PrePressTargetMinusAttacker { get; set; }
        public string? ObservedPressPhase { get; set; }
        public string Outcome { get; set; } = "unavailable";
        public string? Reason { get; set; }
        public ulong? ActivationId { get; set; }
        public byte? WireSlot { get; set; }
        public string? CanonicalSlot { get; set; }
        public bool? AirborneAtStart { get; set; }
        public int? ActivationTick { get; set; }
        public CoverageContactRecord? FirstContact { get; set; }
        public NormalVector3Data? FirstContactRelative { get; set; }
        public int? FirstLandingTick { get; set; }
        public CoverageStateSnapshot? FirstLandingState { get; set; }
        public bool LandedBeforePress { get; set; }
        public bool LandedBeforeContact { get; set; }
        public bool ContactOnLandingTick { get; set; }
        public int? CompletionTick { get; set; }
        public string? CompletionReason { get; set; }
        public float TargetDamageBefore { get; init; }
        public float? TargetDamageAfter { get; set; }
        public CoverageStateSnapshot? FinalAttacker { get; set; }
        public CoverageStateSnapshot? FinalVictim { get; set; }
    }

    internal sealed class NormalCoverageTotals
    {
        public int Hits { get; init; }
        public int Misses { get; init; }
        public int Unavailable { get; init; }
        public int Truncated { get; init; }
    }

    internal sealed class NormalCoverageSummary
    {
        public string SlotId { get; init; } = "";
        public float StartingY { get; init; }
        public string? JumpStyle { get; init; }
        public string? Checkpoint { get; init; }
        public int Hits { get; init; }
        public int Misses { get; init; }
        public int Unavailable { get; init; }
        public int Truncated { get; init; }
        public int CompletedAttempts { get; init; }
        public float? ConnectionFraction { get; init; }
    }

    internal static NormalCoverageReportData BuildNormals(
        MatchContentEntry attacker, MatchContentEntry victim, NormalCoverageOptions options)
    {
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (victim == null) throw new ArgumentNullException(nameof(victim));
        if (options == null) throw new ArgumentNullException(nameof(options));
        ValidateNormalOptions(options);
        RequireNormalContent(attacker);
        RequireNormalContent(victim);
        var arena = Program.NoRespawn(Program.BuildArena());
        var selected = options.Slots.ToArray();
        var positions = ResolvePositions(options);
        var references = new Dictionary<string, NormalJumpReference>(StringComparer.Ordinal);
        if (selected.Any(x => x.IsAirborne))
        {
            foreach (string style in NormalJumpStyles)
            {
                var reference = MeasureNormalJump(attacker, arena, style);
                references.Add(style, reference);
                foreach (var position in positions)
                    ValidateNormalPosition(arena, attacker.Definition, victim.Definition, reference, position);
            }
        }
        else
        {
            foreach (var position in positions)
                ValidateGroundPosition(arena, attacker.Definition, victim.Definition, position);
        }

        var report = new NormalCoverageReportData
        {
            Attacker = Identity(attacker, 1f, "source admitted content"),
            Victim = Identity(victim, 1f, "source admitted content"),
            Settings = BuildNormalSettings(options, selected, arena),
            Arena = BuildNormalArena(arena),
        };
        report.Positions.AddRange(positions.Select((p, i) => new NormalCoveragePosition
        {
            Index = i, X = p.X, Y = p.Y, Z = p.Z, IsExtra = IsExtraPosition(options, p),
        }));
        report.JumpReferences.AddRange(references.Values);
        report.Moves.AddRange(selected.Select(slot =>
        {
            var cooked = RequireNormalCookedSlot(attacker, slot);
            return new NormalCoverageMove
            {
                SlotId = slot.Id,
                Ordinal = slot.Ordinal,
                IsAirborne = slot.IsAirborne,
                Name = cooked.Name,
                Description = cooked.Description,
                SupportReason = NormalSupportReason(cooked),
                AuthoredTimelineDurationTicks = cooked.Timeline.Stages.Sum(x => x.DurationTicks),
            };
        }));
        report.Cautions.AddRange(new[]
        {
            "sampled contact is not player accuracy, balance ranking, or a hit-rate claim",
            "elevated targets fall under ordinary Shared physics after the aerial reference instant",
            "immediate, early, and late are three labelled flight-fraction checkpoints, not a successful timing interval",
            "final states and null observations describe what the capped real simulation observed; no missing state is fabricated",
        });

        foreach (var slot in selected)
        {
            var cooked = RequireNormalCookedSlot(attacker, slot);
            foreach (var position in report.Positions)
            {
                if (!slot.IsAirborne)
                {
                    report.Samples.Add(RunNormalSample(attacker, victim, arena, slot,
                        new CoveragePosition(position.X, position.Y, position.Z), position.Index,
                        null, null, null, 0, options.MaxTicks));
                    continue;
                }
                foreach (string style in NormalJumpStyles)
                foreach (var checkpoint in references[style].Checkpoints)
                    report.Samples.Add(RunNormalSample(attacker, victim, arena, slot,
                        new CoveragePosition(position.X, position.Y, position.Z), position.Index,
                        style, checkpoint.Label, references[style], checkpoint.AbsoluteTick, options.MaxTicks));
            }
            _ = cooked;
        }
        report.Totals = BuildTotals(report.Samples);
        report.Summaries.AddRange(BuildSummaries(report.Samples));
        return report;
    }

    internal static string ToJson(NormalCoverageReportData report)
        => JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
        });

    internal static NormalJumpReference MeasureNormalJump(MatchContentEntry attacker, ArenaDefinition arena, string jumpStyle)
    {
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (!NormalJumpStyles.Contains(jumpStyle, StringComparer.Ordinal))
            throw new ArgumentException($"unknown normal jump style '{jumpStyle}'", nameof(jumpStyle));
        var sim = new ServerSimulation(arena);
        var initial = InitialState(attacker.Definition, 0f, NormalOriginX, NormalOriginZ, 0f, 0f, 0f, true, 0f);
        sim.RegisterEntity(NormalAttackerId, attacker.Definition, initial, attacker.BakedAnimation);
        int jumpEdge = jumpStyle == "running" ? 30 : 0;
        int takeoff = -1, apex = -1, landing = -1;
        float highest = float.MinValue;
        CoverageStateSnapshot? takeoffBefore = null;
        CoverageStateSnapshot? takeoffAfter = null;
        CoverageStateSnapshot? apexState = null;
        CoverageStateSnapshot? landingState = null;
        float takeoffX = NormalOriginX, takeoffZ = NormalOriginZ;
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < Program.MaxTicks; tick++)
        {
            var before = sim.GetState(NormalAttackerId);
            inputs[NormalAttackerId] = NormalJumpInput(jumpStyle, tick, jumpEdge, takeoff, 0);
            sim.Tick(inputs);
            var after = sim.GetState(NormalAttackerId);
            if (takeoff < 0 && before.IsGrounded && !after.IsGrounded)
            {
                takeoff = tick;
                takeoffBefore = Snapshot(before, attacker.Definition);
                takeoffAfter = Snapshot(after, attacker.Definition);
                takeoffX = after.PX;
                takeoffZ = after.PZ;
            }
            if (takeoff >= 0 && after.PY > highest)
            {
                highest = after.PY;
                apex = tick;
                apexState = Snapshot(after, attacker.Definition);
            }
            if (takeoff >= 0 && tick > takeoff && after.IsGrounded)
            {
                landing = tick;
                landingState = Snapshot(after, attacker.Definition);
                break;
            }
        }
        if (takeoff < 0 || apex < 0 || landing < 0 || takeoffBefore == null || takeoffAfter == null || landingState == null)
            throw new InvalidDataException($"{attacker.Identity.PackageId}: normal jump '{jumpStyle}' did not take off, apex, and land within cap {Program.MaxTicks}");
        int flightTicks = landing - takeoff;
        int early = Math.Max(1, flightTicks / 3);
        int late = Math.Max(1, (2 * flightTicks) / 3);
        var reference = new NormalJumpReference
        {
            ReferenceId = $"{attacker.Identity.PackageId}:{jumpStyle}",
            Attacker = attacker.Identity.PackageId,
            JumpStyle = jumpStyle,
            RunupTicks = jumpEdge,
            JumpEdgeTick = jumpEdge,
            TakeoffTick = takeoff,
            ApexTick = apex,
            LandingTick = landing,
            FlightTicks = flightTicks,
            TakeoffBefore = takeoffBefore,
            TakeoffAfter = takeoffAfter,
            Apex = apexState ?? new CoverageStateSnapshot(),
            Landing = landingState,
            TakeoffWorldX = takeoffX,
            TakeoffWorldZ = takeoffZ,
        };
        reference.Checkpoints.Add(new NormalCheckpoint { Label = "immediate", OffsetTicks = 1, AbsoluteTick = takeoff + 1 });
        reference.Checkpoints.Add(new NormalCheckpoint { Label = "early", OffsetTicks = early, AbsoluteTick = takeoff + early });
        reference.Checkpoints.Add(new NormalCheckpoint { Label = "late", OffsetTicks = late, AbsoluteTick = takeoff + late });
        var coincident = reference.Checkpoints.GroupBy(x => x.AbsoluteTick).Where(x => x.Count() > 1);
        foreach (var group in coincident)
            reference.CoincidentCheckpoints.Add(string.Join(",", group.Select(x => x.Label)));
        return reference;
    }

    internal static NormalCoverageSample RunNormalSample(
        MatchContentEntry attacker, MatchContentEntry victim, ArenaDefinition arena, SlotAddress slot,
        CoveragePosition position, int positionIndex, string? jumpStyle, string? checkpoint,
        NormalJumpReference? reference, int pressTick, int maxTicks)
    {
        if (attacker == null) throw new ArgumentNullException(nameof(attacker));
        if (victim == null) throw new ArgumentNullException(nameof(victim));
        if (position == null) throw new ArgumentNullException(nameof(position));
        ValidateNormalOptions(new NormalCoverageOptions { Slots = new[] { slot }, MaxTicks = maxTicks });
        var cooked = RequireNormalCookedSlot(attacker, slot);
        bool isUnsupported = cooked.Timeline.Stages.SelectMany(x => x.Operations).Any(x => x is CookedStartCapabilityOperation);
        bool isEmpty = cooked.Timeline.Stages.SelectMany(x => x.Operations).Count() == 0;
        if (slot.IsAirborne && (reference == null || jumpStyle == null || checkpoint == null))
            throw new ArgumentException("aerial normal samples require jump style, checkpoint, and reference");
        if (!slot.IsAirborne && (reference != null || jumpStyle != null || checkpoint != null || pressTick != 0))
            throw new ArgumentException("ground normal samples require null jump metadata and press tick 0");
        if (pressTick < 0 || maxTicks < 1 || maxTicks > Program.MaxTicks)
            throw new ArgumentOutOfRangeException(nameof(maxTicks));
        if (slot.IsAirborne && !string.Equals(reference!.JumpStyle, jumpStyle, StringComparison.Ordinal))
            throw new ArgumentException("jump style does not match reference");

        var initialAttackerState = InitialState(attacker.Definition, 0f, NormalOriginX, NormalOriginZ, 0f, 0f, 0f, true, 0f);
        var result = new NormalCoverageSample
        {
            SlotId = slot.Id,
            CanonicalSlot = slot.Id,
            PositionIndex = positionIndex,
            StartingY = position.Y,
            JumpStyle = jumpStyle,
            Checkpoint = checkpoint,
            ScheduledPressTick = pressTick,
            ReferenceId = reference?.ReferenceId,
            ReferenceTick = slot.IsAirborne ? reference!.TakeoffTick : 0,
            InitialAttacker = Snapshot(initialAttackerState, attacker.Definition),
            TargetDamageBefore = 0f,
        };
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(NormalAttackerId, attacker.Definition, initialAttackerState, attacker.BakedAnimation);
        var inputs = new Dictionary<ulong, InputState>();
        bool targetRegistered = false;
        int takeoff = -1;
        CoverageStateSnapshot? targetReference = null;
        bool accepted = false;
        ulong activationBeforePress = 0;
        CoverageStateSnapshot? lastAttacker = result.InitialAttacker;
        CoverageStateSnapshot? lastVictim = null;
        int lastLandingTick = -1;
        CoverageStateSnapshot? lastLandingState = null;
        byte wireSlot = Program.SlotByte(int.Parse(slot.InputLabel, CultureInfo.InvariantCulture));

        if (!slot.IsAirborne)
        {
            var victimState = InitialState(victim.Definition, position.Y, NormalOriginX + position.X, NormalOriginZ + position.Z,
                0f, 0f, 0f, position.Y == 0f, MathF.PI);
            sim.RegisterEntity(NormalVictimId, victim.Definition, victimState, victim.BakedAnimation);
            targetRegistered = true;
            targetReference = Snapshot(victimState, victim.Definition);
            result.ReferenceAttacker = result.InitialAttacker;
            result.ReferenceVictim = targetReference;
            result.ReferenceTargetMinusAttacker = RelativeVector(victimState, initialAttackerState);
            result.InitialBodyOverlap = BodyCapsulesOverlap(initialAttackerState, attacker.Definition, victimState, victim.Definition);
        }

        for (int tick = 0; tick <= pressTick + maxTicks - 1; tick++)
        {
            var beforeAttacker = sim.GetState(NormalAttackerId);
            CharacterState beforeVictim = targetRegistered ? sim.GetState(NormalVictimId) : default;
            if (slot.IsAirborne && takeoff < 0 && beforeAttacker.IsGrounded == false)
                throw new InvalidDataException($"{attacker.Identity.PackageId}: normal sample '{slot.Id}' began airborne before measured takeoff for {jumpStyle}");

            var attackerInput = slot.IsAirborne
                ? NormalJumpInput(jumpStyle!, tick, reference!.JumpEdgeTick, takeoff, tick == pressTick ? wireSlot : (byte)0)
                : new InputState { ActiveSlot = tick == pressTick ? wireSlot : (byte)0 };
            if (tick == pressTick)
            {
                result.PrePressAttacker = Snapshot(beforeAttacker, attacker.Definition);
                if (targetRegistered)
                {
                    result.PrePressVictim = Snapshot(beforeVictim, victim.Definition);
                    result.PrePressTargetMinusAttacker = RelativeVector(beforeVictim, beforeAttacker);
                    result.ObservedPressPhase = ObservedPhase(beforeAttacker);
                }
                else
                {
                    result.ObservedPressPhase = ObservedPhase(beforeAttacker);
                }
                result.LandedBeforePress = lastLandingTick >= 0;
                activationBeforePress = sim.GetLastActivationId(NormalAttackerId);
                if (beforeAttacker.IsGrounded != !slot.IsAirborne)
                {
                    result.Outcome = "unavailable";
                    result.Reason = "wrong-locomotion-state";
                    result.CompletionTick = tick;
                    result.CompletionReason = result.Reason;
                    result.FinalAttacker = result.PrePressAttacker;
                    result.FinalVictim = result.PrePressVictim;
                    return result;
                }
                if (isEmpty || isUnsupported)
                {
                    result.AttackInputSent = false;
                    result.Outcome = "unavailable";
                    result.Reason = isEmpty ? "empty-normal" : "unsupported-contact-accounting";
                    result.CompletionTick = tick;
                    result.CompletionReason = result.Reason;
                    result.FinalAttacker = result.PrePressAttacker;
                    result.FinalVictim = result.PrePressVictim;
                    return result;
                }
                result.AttackInputSent = true;
                result.ActualPressTick = tick;
            }
            inputs[NormalAttackerId] = attackerInput;
            if (targetRegistered) inputs[NormalVictimId] = default;
            sim.Tick(inputs);
            var afterAttacker = sim.GetState(NormalAttackerId);
            CharacterState afterVictim = targetRegistered ? sim.GetState(NormalVictimId) : default;
            lastAttacker = Snapshot(afterAttacker, attacker.Definition);
            if (targetRegistered) lastVictim = Snapshot(afterVictim, victim.Definition);

            if (slot.IsAirborne && takeoff < 0 && beforeAttacker.IsGrounded && !afterAttacker.IsGrounded)
            {
                takeoff = tick;
                result.BeforeTakeoffAttacker = Snapshot(beforeAttacker, attacker.Definition);
                result.AfterTakeoffAttacker = Snapshot(afterAttacker, attacker.Definition);
                if (reference!.TakeoffTick != tick || !StatesMatch(reference.TakeoffAfter, result.AfterTakeoffAttacker))
                    throw new InvalidDataException($"{attacker.Identity.PackageId}: normal sample '{slot.Id}' {jumpStyle} takeoff did not match reference tick/state");
                var victimState = InitialState(victim.Definition, position.Y, afterAttacker.PX + position.X, afterAttacker.PZ + position.Z,
                    0f, 0f, 0f, position.Y == 0f, MathF.PI);
                sim.RegisterEntity(NormalVictimId, victim.Definition, victimState, victim.BakedAnimation);
                targetRegistered = true;
                targetReference = Snapshot(victimState, victim.Definition);
                result.ReferenceAttacker = result.AfterTakeoffAttacker;
                result.ReferenceVictim = targetReference;
                result.ReferenceTargetMinusAttacker = RelativeVector(victimState, afterAttacker);
                result.InitialBodyOverlap = BodyCapsulesOverlap(afterAttacker, attacker.Definition, victimState, victim.Definition);
            }
            if (slot.IsAirborne && takeoff < 0 && tick >= reference!.TakeoffTick)
                throw new InvalidDataException($"{attacker.Identity.PackageId}: normal sample '{slot.Id}' {jumpStyle} failed to take off at reference tick {reference.TakeoffTick}");

            if (slot.IsAirborne && takeoff >= 0 && lastLandingTick < 0 && afterAttacker.IsGrounded && tick > takeoff)
            {
                lastLandingTick = tick;
                lastLandingState = Snapshot(afterAttacker, attacker.Definition);
                result.FirstLandingTick = tick;
                result.FirstLandingState = lastLandingState;
            }
            if (tick < pressTick) continue;
            if (tick == pressTick)
            {
                ulong activation = sim.GetLastActivationId(NormalAttackerId);
                var active = sim.GetActiveAbility(NormalAttackerId);
                bool activationAccepted = activation != 0 && activation != activationBeforePress;
                bool identityMatches = active == null || (active.Slot + 1 == wireSlot && active.AirborneAtStart == slot.IsAirborne);
                if (!activationAccepted || !identityMatches)
                {
                    result.Outcome = "unavailable";
                    result.Reason = "activation-rejected";
                    result.CompletionTick = tick;
                    result.CompletionReason = result.Reason;
                    result.FinalAttacker = lastAttacker;
                    result.FinalVictim = lastVictim;
                    return result;
                }
                accepted = true;
                result.ActivationId = activation;
                result.WireSlot = wireSlot;
                result.AirborneAtStart = active?.AirborneAtStart ?? slot.IsAirborne;
                result.ActivationTick = tick;
            }
            if (!accepted || !targetRegistered) continue;

            var activeHitboxes = sim.Resolver.GetActiveHitboxes();
            var hit = sim.LastTickHits.FirstOrDefault(x => x.OwnerEntityId == NormalAttackerId
                && x.TargetEntityId == NormalVictimId
                && x.AttackSlot == wireSlot
                && x.ActivationId == result.ActivationId!.Value
                && x.Airborne == slot.IsAirborne
                && x.Damage > 0f);
            if (hit.Damage > 0f)
            {
                result.Outcome = "hit";
                result.Reason = null;
                result.CompletionTick = tick;
                result.CompletionReason = "first-damaging-contact";
                result.TargetDamageAfter = afterVictim.DamagePercent;
                result.LandedBeforeContact = lastLandingTick >= 0;
                result.ContactOnLandingTick = lastLandingTick == tick;
                result.FirstContactRelative = RelativeVector(afterVictim, afterAttacker);
                result.FirstContact = new CoverageContactRecord
                {
                    Tick = tick,
                    TicksAfterPress = tick - pressTick,
                    Damage = hit.Damage,
                    PositionX = hit.HitX - NormalOriginX,
                    PositionY = hit.HitY,
                    PositionZ = hit.HitZ - NormalOriginZ,
                    AttackerState = Snapshot(afterAttacker, attacker.Definition),
                    VictimState = Snapshot(afterVictim, victim.Definition),
                };
                result.FinalAttacker = lastAttacker;
                result.FinalVictim = lastVictim;
                return result;
            }
            bool lingering = activeHitboxes.Any(x => x.Active && x.OwnerId == NormalAttackerId
                && x.AttackSlot == wireSlot && x.ActivationId == result.ActivationId!.Value
                && x.ActivationAirborne == slot.IsAirborne);
            if (sim.GetActiveAbility(NormalAttackerId) == null && !lingering)
            {
                result.Outcome = "miss";
                result.Reason = null;
                result.CompletionTick = tick;
                result.CompletionReason = "effects-complete";
                result.TargetDamageAfter = afterVictim.DamagePercent;
                result.FinalAttacker = lastAttacker;
                result.FinalVictim = lastVictim;
                return result;
            }
        }

        result.Outcome = accepted ? "truncated" : "unavailable";
        result.Reason = accepted ? "max-ticks-cap" : "activation-rejected";
        result.CompletionTick = pressTick + maxTicks - 1;
        result.CompletionReason = result.Reason;
        result.TargetDamageAfter = targetRegistered ? sim.GetState(NormalVictimId).DamagePercent : null;
        result.FinalAttacker = lastAttacker;
        result.FinalVictim = lastVictim ?? (targetRegistered ? Snapshot(sim.GetState(NormalVictimId), victim.Definition) : null);
        return result;
    }
    private static IEnumerable<NormalCoverageSummary> BuildSummaries(IEnumerable<NormalCoverageSample> samples)
        => samples.GroupBy(x => (x.SlotId, x.StartingY, x.JumpStyle, x.Checkpoint))
            .Select(group =>
            {
                var rows = group.ToArray();
                int completed = rows.Count(x => x.Outcome is "hit" or "miss");
                return new NormalCoverageSummary
                {
                    SlotId = group.Key.SlotId,
                    StartingY = group.Key.StartingY,
                    JumpStyle = group.Key.JumpStyle,
                    Checkpoint = group.Key.Checkpoint,
                    Hits = rows.Count(x => x.Outcome == "hit"),
                    Misses = rows.Count(x => x.Outcome == "miss"),
                    Unavailable = rows.Count(x => x.Outcome == "unavailable"),
                    Truncated = rows.Count(x => x.Outcome == "truncated"),
                    CompletedAttempts = completed,
                    ConnectionFraction = completed == 0 ? null : rows.Count(x => x.Outcome == "hit") / (float)completed,
                };
            });

    private static void ValidateNormalOptions(NormalCoverageOptions options)
    {
        if (options.Slots == null || options.Slots.Count == 0) throw new ArgumentException("at least one canonical normal slot is required", nameof(options));
        if (options.X == null || options.Y == null || options.Z == null || options.ExtraPositions == null)
            throw new ArgumentException("normal axes and extras are required", nameof(options));
        if (options.X.Count == 0 || options.Y.Count == 0 || options.Z.Count == 0)
            throw new ArgumentException("normal axes cannot be empty", nameof(options));
        if (options.MaxTicks < 1 || options.MaxTicks > Program.MaxTicks)
            throw new ArgumentOutOfRangeException(nameof(options.MaxTicks), "max ticks must be between 1 and Program.MaxTicks");
        foreach (var axis in new[] { options.X, options.Y, options.Z })
        foreach (float value in axis)
            ValidateNormalFloat(value, "axis", value < 0f && ReferenceEquals(axis, options.Y));
        foreach (var extra in options.ExtraPositions)
        {
            ValidateNormalFloat(extra.X, "extra", false);
            ValidateNormalFloat(extra.Y, "extra", true);
            ValidateNormalFloat(extra.Z, "extra", false);
        }
        foreach (var provided in options.Slots)
        {
            if (!CanonicalSlotProjection.TryGet(provided.Id, out var canonical) || canonical != provided
                || provided.InputLabel is not ("1" or "2" or "3"))
                throw new ArgumentException($"slot '{provided.Id}' is not an exact canonical normal address", nameof(options));
        }
        long matrix;
        try { matrix = checked((long)options.X.Count * options.Y.Count * options.Z.Count); }
        catch (OverflowException) { throw new ArgumentException("normal position matrix size overflows Int32", nameof(options)); }
        if (matrix > int.MaxValue - options.ExtraPositions.Count)
            throw new ArgumentException("normal position matrix size overflows Int32", nameof(options));
    }

    private static void ValidateNormalFloat(float value, string source, bool rejectNegative)
    {
        if (!float.IsFinite(value)) throw new ArgumentException($"{source} coordinate must be finite");
        if (rejectNegative && value < 0f) throw new ArgumentException($"{source} Y must be >= 0");
    }

    private static List<CoveragePosition> ResolvePositions(NormalCoverageOptions options)
    {
        var result = new List<CoveragePosition>();
        var seen = new HashSet<(float X, float Y, float Z)>();
        foreach (float y in options.Y)
        foreach (float z in options.Z)
        foreach (float x in options.X)
        {
            var position = new CoveragePosition(x, y, z);
            if (seen.Add((x, y, z))) result.Add(position);
        }
        foreach (var extra in options.ExtraPositions)
            if (seen.Add((extra.X, extra.Y, extra.Z))) result.Add(extra);
        return result;
    }

    private static bool IsExtraPosition(NormalCoverageOptions options, CoveragePosition position)
        => !options.X.Contains(position.X) || !options.Y.Contains(position.Y) || !options.Z.Contains(position.Z)
            || !options.X.Any(x => x == position.X) || !options.Y.Any(y => y == position.Y) || !options.Z.Any(z => z == position.Z);

    private static void ValidateGroundPosition(ArenaDefinition arena, CharacterDefinition attacker, CharacterDefinition victim, CoveragePosition position)
    {
        var reference = new NormalJumpReference { TakeoffWorldX = NormalOriginX, TakeoffWorldZ = NormalOriginZ };
        ValidateNormalPosition(arena, attacker, victim, reference, position);
    }

    private static void ValidateNormalPosition(ArenaDefinition arena, CharacterDefinition attacker, CharacterDefinition victim,
        NormalJumpReference reference, CoveragePosition position)
    {
        float attackerX = reference.TakeoffWorldX;
        float attackerZ = reference.TakeoffWorldZ;
        float victimX = attackerX + position.X;
        float victimZ = attackerZ + position.Z;
        float minX = arena.Heightmap.OriginX;
        float maxX = arena.Heightmap.OriginX + (arena.Heightmap.Width - 1) * arena.Heightmap.CellSize;
        float minZ = arena.Heightmap.OriginZ;
        float maxZ = arena.Heightmap.OriginZ + (arena.Heightmap.Height - 1) * arena.Heightmap.CellSize;
        if (!FootprintInside(attackerX, attackerZ, attacker.CapsuleRadius, minX, maxX, minZ, maxZ)
            || !FootprintInside(victimX, victimZ, victim.CapsuleRadius, minX, maxX, minZ, maxZ))
            throw new CoverageArgumentException($"normal position ({position.X.ToString(CultureInfo.InvariantCulture)},{position.Y.ToString(CultureInfo.InvariantCulture)},{position.Z.ToString(CultureInfo.InvariantCulture)}) is outside diagnostic arena for reference ({attackerX.ToString(CultureInfo.InvariantCulture)},{attackerZ.ToString(CultureInfo.InvariantCulture)})");
        float victimCenterY = position.Y + victim.CapsuleHeight * 0.5f;
        if (!float.IsFinite(victimCenterY) || victimCenterY < arena.KillHeight || victimCenterY > arena.KillTop
            || victimX < arena.KillMinX || victimX > arena.KillMaxX || victimZ < arena.KillMinZ || victimZ > arena.KillMaxZ)
            throw new CoverageArgumentException($"normal position ({position.X.ToString(CultureInfo.InvariantCulture)},{position.Y.ToString(CultureInfo.InvariantCulture)},{position.Z.ToString(CultureInfo.InvariantCulture)}) is outside effective blast bounds");
    }

    private static bool FootprintInside(float x, float z, float radius, float minX, float maxX, float minZ, float maxZ)
        => x - radius >= minX && x + radius <= maxX && z - radius >= minZ && z + radius <= maxZ;

    private static void RequireNormalContent(MatchContentEntry entry)
    {
        if (entry.CookedCharacterPackage == null || entry.BakedAnimation == null)
            throw new InvalidDataException($"{entry.Identity.PackageId}: normal coverage requires admitted cooked package and deterministic baked poses");
    }

    private static CookedSlotDefinition RequireNormalCookedSlot(MatchContentEntry entry, SlotAddress slot)
    {
        var cooked = RequireCookedSlot(entry, slot);
        if (cooked.Ordinal != slot.Ordinal || cooked.IsAir != slot.IsAirborne || !string.Equals(cooked.Id, slot.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"{entry.Identity.PackageId}: cooked slot '{slot.Id}' has inconsistent canonical id, ordinal, or air mode");
        if (cooked.Timeline == null || cooked.Timeline.Stages == null || cooked.Timeline.Stages.Count == 0)
            throw new InvalidDataException($"{entry.Identity.PackageId}: cooked slot '{slot.Id}' has no valid timeline");
        return cooked;
    }

    private static string? NormalSupportReason(CookedSlotDefinition cooked)
    {
        if (cooked.Timeline.Stages.SelectMany(x => x.Operations).Any(x => x is CookedStartCapabilityOperation))
            return "unsupported-contact-accounting";
        if (cooked.Timeline.Stages.SelectMany(x => x.Operations).Count() == 0)
            return "empty-normal";
        return null;
    }

    private static NormalCoverageSettings BuildNormalSettings(NormalCoverageOptions options, IReadOnlyList<SlotAddress> slots, ArenaDefinition arena)
    {
        return new NormalCoverageSettings
        {
            X = options.X.ToArray(),
            Y = options.Y.ToArray(),
            Z = options.Z.ToArray(),
            SelectedSlots = slots.Select(x => x.Id).ToArray(),
            MaxTicks = options.MaxTicks,
            ToolAssemblySha256 = AssemblySha256(typeof(Program).Assembly),
            SimulationAssemblySha256 = AssemblySha256(typeof(ServerSimulation).Assembly),
        }.WithExtras(options.ExtraPositions);
    }

    private static NormalCoverageSettings WithExtras(this NormalCoverageSettings settings, IReadOnlyList<CoveragePosition> extras)
    {
        settings.ExtraPositions.AddRange(extras);
        return settings;
    }

    private static NormalArenaData BuildNormalArena(ArenaDefinition arena)
        => new()
        {
            Name = arena.Name,
            FloorHeight = 0f,
            HeightmapOriginX = arena.Heightmap.OriginX,
            HeightmapOriginZ = arena.Heightmap.OriginZ,
            Width = arena.Heightmap.Width,
            Height = arena.Heightmap.Height,
            CellSize = arena.Heightmap.CellSize,
            EffectiveKillHeight = arena.KillHeight,
            EffectiveKillTop = arena.KillTop,
            EffectiveKillMinX = arena.KillMinX,
            EffectiveKillMaxX = arena.KillMaxX,
            EffectiveKillMinZ = arena.KillMinZ,
            EffectiveKillMaxZ = arena.KillMaxZ,
        };

    private static string AssemblySha256(Assembly assembly)
    {
        string location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            throw new InvalidDataException($"cannot read required assembly file for {assembly.GetName().Name}");
        using var stream = new FileStream(location, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static InputState NormalJumpInput(string jumpStyle, int tick, int jumpEdge, int takeoff, byte activeSlot)
    {
        bool held = tick >= jumpEdge && (takeoff < 0 || tick == takeoff);
        bool drift = jumpStyle == "neutral-drift" && takeoff >= 0;
        bool running = jumpStyle == "running";
        return new InputState
        {
            MoveX = 0f,
            MoveY = drift || running ? 1f : 0f,
            Jump = tick == jumpEdge,
            JumpHeld = held,
            ActiveSlot = activeSlot,
            AimYaw = 0,
            AimPitch = 0,
            IsAiming = false,
            TargetEntityId = 0,
        };
    }

    private static string ObservedPhase(CharacterState state)
        => state.IsGrounded ? "grounded" : state.VY > NormalPhaseThreshold ? "rising" : state.VY < -NormalPhaseThreshold ? "falling" : "apex-band";

    private static bool StatesMatch(CoverageStateSnapshot? expected, CoverageStateSnapshot? actual)
        => expected != null && actual != null
            && expected.IsGrounded == actual.IsGrounded
            && MathF.Abs(expected.PositionX - actual.PositionX) < 0.0001f
            && MathF.Abs(expected.PositionY - actual.PositionY) < 0.0001f
            && MathF.Abs(expected.PositionZ - actual.PositionZ) < 0.0001f
            && MathF.Abs(expected.VelocityX - actual.VelocityX) < 0.0001f
            && MathF.Abs(expected.VelocityY - actual.VelocityY) < 0.0001f
            && MathF.Abs(expected.VelocityZ - actual.VelocityZ) < 0.0001f;

    private static NormalVector3Data RelativeVector(CharacterState target, CharacterState attacker)
        => new() { X = target.PX - attacker.PX, Y = target.PY - attacker.PY, Z = target.PZ - attacker.PZ };

    private static NormalVector3Data RelativeVector(CoverageStateSnapshot target, CoverageStateSnapshot attacker)
        => new() { X = target.PositionX - attacker.PositionX,
            Y = target.CapsuleCenterY - attacker.CapsuleCenterY, Z = target.PositionZ - attacker.PositionZ };

    private static bool BodyCapsulesOverlap(CharacterState attacker, CharacterDefinition attackerDef,
        CharacterState victim, CharacterDefinition victimDef)
    {
        float dx = victim.PX - attacker.PX, dz = victim.PZ - attacker.PZ;
        float horizontal = dx * dx + dz * dz;
        float attackerHalfSpine = MathF.Max(0f, attackerDef.CapsuleHeight * 0.5f - attackerDef.CapsuleRadius);
        float victimHalfSpine = MathF.Max(0f, victimDef.CapsuleHeight * 0.5f - victimDef.CapsuleRadius);
        float aMin = attacker.PY - attackerHalfSpine, aMax = attacker.PY + attackerHalfSpine;
        float vMin = victim.PY - victimHalfSpine, vMax = victim.PY + victimHalfSpine;
        float gap = aMax < vMin ? vMin - aMax : vMax < aMin ? aMin - vMax : 0f;
        float radius = attackerDef.CapsuleRadius + victimDef.CapsuleRadius;
        return horizontal + gap * gap < radius * radius;
    }

    private static NormalCoverageTotals BuildTotals(IEnumerable<NormalCoverageSample> samples)
    {
        var rows = samples.ToArray();
        return new NormalCoverageTotals
        {
            Hits = rows.Count(x => x.Outcome == "hit"),
            Misses = rows.Count(x => x.Outcome == "miss"),
            Unavailable = rows.Count(x => x.Outcome == "unavailable"),
            Truncated = rows.Count(x => x.Outcome == "truncated"),
        };
    }

}
