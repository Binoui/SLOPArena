#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared;

namespace SlopArena.MoveDataReport;

internal static partial class ContactCoverageReport
{
    internal const string BaselineProfile = "baseline";
    internal const string CandidateProfile = "candidate";
    private const float OriginX = 100f;
    private const float OriginZ = 100f;
    private const int TimingMaxPressTick = 60;

    internal sealed class CoverageOptions
    {
        public IReadOnlyList<SlotAddress> Slots { get; }
        public float GridStep { get; }
        public float CandidateAirScale { get; }
        public CoverageOptions(IReadOnlyList<SlotAddress> slots, float gridStep = 0.5f, float candidateAirScale = 0.8f)
        {
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            GridStep = gridStep;
            CandidateAirScale = candidateAirScale;
        }
    }

    internal sealed class CoverageContext
    {
        public MatchContentEntry SourceAttacker { get; }
        public MatchContentEntry SourceVictim { get; }
        public MatchContentEntry Attacker { get; }
        public MatchContentEntry Victim { get; }
        public CharacterDefinition AttackerDefinition => Attacker.Definition;
        public CharacterDefinition VictimDefinition => Victim.Definition;
        public BakedAnimationData? AttackerBaked => Attacker.BakedAnimation;
        public BakedAnimationData? VictimBaked => Victim.BakedAnimation;
        public float AirScale { get; }
        public string Profile => AirScale == 1f ? BaselineProfile : CandidateProfile;
        public ArenaDefinition Arena { get; }

        internal CoverageContext(MatchContentEntry sourceAttacker, MatchContentEntry sourceVictim,
            MatchContentEntry attacker, MatchContentEntry victim, float airScale, ArenaDefinition arena)
        {
            SourceAttacker = sourceAttacker;
            SourceVictim = sourceVictim;
            Attacker = attacker;
            Victim = victim;
            AirScale = airScale;
            Arena = arena;
        }
    }

    internal sealed record CoverageSampleSpec(string ScenarioId, float VictimRelativeX, float VictimRelativeZ, int PressTick);

    internal sealed class CoverageStateSnapshot
    {
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public float PositionZ { get; init; }
        public float CapsuleCenterY { get; init; }
        public float VelocityX { get; init; }
        public float VelocityY { get; init; }
        public float VelocityZ { get; init; }
        public bool IsGrounded { get; init; }
        public string ActionState { get; init; } = "Idle";
        public float FacingYawDegrees { get; init; }
    }

    internal sealed class CoverageContactRecord
    {
        public int Tick { get; init; }
        public int TicksAfterPress { get; init; }
        public float Damage { get; init; }
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public float PositionZ { get; init; }
        public CoverageStateSnapshot AttackerState { get; init; } = new();
        public CoverageStateSnapshot VictimState { get; init; } = new();
    }

    internal sealed class CoverageSampleResult
    {
        public string ScenarioId { get; init; } = "";
        public string SlotId { get; init; } = "";
        public string Profile { get; init; } = BaselineProfile;
        public bool IsAirborneSlot { get; init; }
        public float InitialRelativeX { get; init; }
        public float InitialRelativeZ { get; init; }
        public int PressTick { get; init; }
        public string Outcome { get; set; } = "unavailable";
        public string? Reason { get; set; }
        public int? ActivationTick { get; set; }
        public float? ActivationFacingYawDegrees { get; set; }
        public bool? AirborneAtStart { get; set; }
        public int? CompletionTick { get; set; }
        public bool LandedBeforeContact { get; set; }
        public float TargetDamageBefore { get; init; }
        public float? TargetDamageAfter { get; set; }
        public CoverageStateSnapshot? PrePressAttacker { get; set; }
        public CoverageStateSnapshot? PrePressVictim { get; set; }
        public CoverageContactRecord? FirstContact { get; set; }
        [JsonIgnore] public CoverageStateSnapshot InitialAttacker { get; init; } = new();
        [JsonIgnore] public CoverageStateSnapshot InitialVictim { get; init; } = new();
        [JsonIgnore] public int? FirstContactTick => FirstContact?.Tick;
        [JsonIgnore] public float? FirstContactDamage => FirstContact?.Damage;

        internal CoverageSampleResult() { }

        internal CoverageSampleResult(string scenarioId, SlotAddress slot, string profile, CoverageSampleSpec sample,
            CoverageStateSnapshot initialAttacker, CoverageStateSnapshot initialVictim, float targetDamageBefore)
        {
            ScenarioId = scenarioId;
            SlotId = slot.Id;
            Profile = profile;
            IsAirborneSlot = slot.IsAirborne;
            InitialRelativeX = sample.VictimRelativeX;
            InitialRelativeZ = sample.VictimRelativeZ;
            PressTick = sample.PressTick;
            InitialAttacker = initialAttacker;
            InitialVictim = initialVictim;
            TargetDamageBefore = targetDamageBefore;
        }
    }

    internal sealed class CoverageTimingInterval
    {
        public string ScenarioId { get; init; } = "";
        public string Profile { get; init; } = BaselineProfile;
        public float LaneX { get; init; }
        public float LaneZ { get; init; }
        public int StartPressTick { get; init; }
        public int EndPressTick { get; init; }
        public int SuccessfulPressTicks { get; init; }
        public int LongestContiguousTicks { get; init; }
        public float NominalMilliseconds { get; init; }
        public bool BoundaryClipped { get; init; }
        [JsonIgnore] public int Count => EndPressTick - StartPressTick + 1;
    }

    internal sealed class CoverageScenarioDefinition
    {
        public string Id { get; init; } = "";
        public string[] SlotKinds { get; init; } = Array.Empty<string>();
        public float AttackerFeetY { get; init; }
        public float VictimFeetY { get; init; }
        public float AttackerVelocityX { get; init; }
        public float AttackerVelocityY { get; init; }
        public float AttackerVelocityZ { get; init; }
        public float VictimVelocityX { get; init; }
        public float VictimVelocityY { get; init; }
        public float VictimVelocityZ { get; init; }
        public float AttackerMoveX { get; init; }
        public float AttackerMoveY { get; init; }
        public float VictimMoveX { get; init; }
        public float VictimMoveY { get; init; }
        public string InitialMovement { get; init; } = "neutral";

        public bool Allows(SlotAddress slot)
            => SlotKinds.Length == 0 || SlotKinds.Contains(slot.IsAirborne ? "air" : "ground", StringComparer.Ordinal);
    }

    internal sealed class CoverageMoveData
    {
        public string Attacker { get; init; } = "";
        public string Slot { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public int Ordinal { get; init; }
        public int AuthoredTimelineDurationTicks { get; init; }
        public bool? HasAuthoredDamagingOperation { get; init; }
        public List<CoverageMapData> Maps { get; } = new();
        public List<CoverageTimingData> Timing { get; } = new();
    }

    internal sealed class CoverageMapCell
    {
        public float RelativeX { get; init; }
        public float RelativeZ { get; init; }
        public CoverageSampleResult Baseline { get; init; } = new();
        public CoverageSampleResult Candidate { get; init; } = new();
        public string Comparison { get; init; } = "not-comparable";
    }

    internal sealed class CoverageMapData
    {
        public string ScenarioId { get; init; } = "";
        public List<CoverageMapCell> Cells { get; } = new();
        public int BothHit { get; set; }
        public int Gained { get; set; }
        public int Lost { get; set; }
        public int BothMiss { get; set; }
        public int NotComparable { get; set; }
        public bool EdgeHit { get; set; }
    }

    internal sealed class CoverageTimingLane
    {
        public float RelativeX { get; init; }
        public float RelativeZ { get; init; }
        public List<CoverageSampleResult> BaselineSamples { get; } = new();
        public List<CoverageSampleResult> CandidateSamples { get; } = new();
        public List<CoverageTimingInterval> BaselineIntervals { get; } = new();
        public List<CoverageTimingInterval> CandidateIntervals { get; } = new();
    }

    internal sealed class CoverageTimingData
    {
        public string ScenarioId { get; init; } = "";
        public List<CoverageTimingLane> Lanes { get; } = new();
    }

    internal sealed class CoverageInitialStateTemplate
    {
        public string Attacker { get; init; } = "";
        public string Slot { get; init; } = "";
        public string ScenarioId { get; init; } = "";
        public string Profile { get; init; } = "";
        public CoverageStateSnapshot AttackerState { get; init; } = new();
        public CoverageStateSnapshot VictimState { get; init; } = new();
        public string VictimPositionNote { get; init; } = "victim X/Z is supplied by each sample";
    }

    internal sealed class CoverageReportData
    {
        public int SchemaVersion { get; init; } = 1;
        public int TickRateHz { get; init; } = 60;
        public List<CoverageIdentityData> Attackers { get; } = new();
        public CoverageIdentityData Victim { get; init; } = new();
        public List<CoverageScenarioDefinition> Scenarios { get; } = new();
        public List<CoverageInitialStateTemplate> InitialStates { get; } = new();
        public CoverageGridData Grid { get; init; } = new();
        public CoverageTimingBounds Timing { get; init; } = new();
        public List<CoverageProfileData> Profiles { get; } = new();
        public List<CoverageMoveData> Moves { get; } = new();
        public List<string> Cautions { get; } = new();
        public string TargetingPolicy { get; init; } = "automatic closest-enemy selection and authored stage tracking are enabled; target input is zero";
    }

    internal sealed class CoverageIdentityData
    {
        public string Selector { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public MatchContentIdentity Identity { get; init; } = null!;
        public CoverageMovementData EffectiveMovement { get; init; } = new();
        public string OverrideNote { get; init; } = "source admitted content";
    }

    internal sealed class CoverageMovementData
    {
        public float RunSpeed { get; init; }
        public float AirSpeedMax { get; init; }
        public float Gravity { get; init; }
        public float AirFloatGravity { get; init; }
        public float AirAcceleration { get; init; }
        public float GroundAcceleration { get; init; }
        public float CapsuleHeight { get; init; }
    }

    internal sealed class CoverageGridData
    {
        public float MinX { get; init; } = -4f;
        public float MaxX { get; init; } = 4f;
        public float MinZ { get; init; } = -4f;
        public float MaxZ { get; init; } = 6f;
        public float Step { get; init; } = 0.5f;
        public string CoordinateNote { get; init; } = "victim starting feet relative to attacker origin; +X right, +Z forward; attacker origin is (0,0)";
    }

    internal sealed class CoverageTimingBounds
    {
        public int MinPressTick { get; init; }
        public int MaxPressTick { get; init; } = TimingMaxPressTick;
        public string MillisecondsNote { get; init; } = "nominal discrete ticks × 1000 / 60; not a reaction-time recommendation";
    }

    internal sealed class CoverageProfileData
    {
        public string Id { get; init; } = "";
        public float RequestedAirSpeedMultiplier { get; init; }
        public string OverrideKind { get; init; } = "tool-local hypothetical override";
        public List<CoverageIdentityData> EffectiveCharacters { get; } = new();
    }

    private static readonly CoverageScenarioDefinition[] ScenarioTable =
    {
        new() { Id = "passive-low", SlotKinds = Array.Empty<string>(), AttackerFeetY = 0, VictimFeetY = 0, InitialMovement = "both neutral" },
        new() { Id = "passive-mid", SlotKinds = Array.Empty<string>(), AttackerFeetY = 0, VictimFeetY = 1, InitialMovement = "both neutral" },
        new() { Id = "passive-high", SlotKinds = Array.Empty<string>(), AttackerFeetY = 0, VictimFeetY = 2, InitialMovement = "both neutral" },
        new() { Id = "target-cross-right", SlotKinds = Array.Empty<string>(), AttackerFeetY = 0, VictimFeetY = 0, InitialMovement = "victim +X at maximum profile speed, held +X", },
        new() { Id = "target-cross-left", SlotKinds = Array.Empty<string>(), AttackerFeetY = 0, VictimFeetY = 0, InitialMovement = "victim -X at maximum profile speed, held -X", },
        new() { Id = "target-approach", SlotKinds = new[] { "ground" }, AttackerFeetY = 0, VictimFeetY = 0, VictimVelocityZ = -1, VictimMoveY = -1, InitialMovement = "victim approaches at RunSpeed, held -Z" },
        new() { Id = "target-retreat", SlotKinds = new[] { "ground" }, AttackerFeetY = 0, VictimFeetY = 0, VictimVelocityZ = 1, VictimMoveY = 1, InitialMovement = "victim retreats at RunSpeed, held +Z" },
        new() { Id = "target-descend", SlotKinds = new[] { "ground" }, AttackerFeetY = 0, VictimFeetY = 2, VictimVelocityY = -4, InitialMovement = "victim descends at -4 m/s, horizontal neutral" },
        new() { Id = "attacker-drift", SlotKinds = new[] { "air" }, AttackerFeetY = 2, VictimFeetY = 0, AttackerVelocityZ = 1, AttackerMoveY = 1, InitialMovement = "attacker drifts at AirSpeedMax, held +Z" },
    };

    internal static int Run(string[] args)
    {
        try
        {
            var parsed = ParseArgs(args);
            if (parsed.Experiment == "normals")
                return RunNormals(parsed);
            string[] selectors = parsed.Character == "all" ? new[] { "fightguy", "manki", "wibou", "bonk" } : new[] { parsed.Character };
            var attackers = selectors.Select(Program.ResolveEntry).ToArray();
            var victim = Program.ResolveEntry(parsed.Victim);
            var arena = Program.NoRespawn(Program.BuildArena());
            var options = new CoverageOptions(parsed.Slots, parsed.Step, parsed.AirScale);
            var report = Build(attackers, victim, options, arena);

            string? json = parsed.JsonPath;
            string? html = parsed.HtmlPath;
            if (json == null && html == null)
            {
                string basePath = Path.Combine("artifacts", "contact-coverage", parsed.Character);
                json = basePath + ".json";
                html = basePath + ".html";
            }
            if (json != null && html != null && string.Equals(Path.GetFullPath(json), Path.GetFullPath(html), StringComparison.Ordinal))
                throw new CoverageArgumentException("--json and --html destinations must be distinct");
            if (json != null) WriteFile(json, ToJson(report));
            if (html != null) WriteFile(html, ContactCoverageHtml.ToHtml(report));
            Console.Error.WriteLine($"coverage: attackers={attackers.Length}, slots={parsed.Slots.Count}, maps={report.Moves.Sum(x => x.Maps.Count)}, samples={report.Moves.Sum(x => x.Maps.Sum(m => m.Cells.Count) * 2)}");
            if (json != null) Console.Error.WriteLine($"wrote {json}");
            if (html != null) Console.Error.WriteLine($"wrote {html}");
            return 0;
        }
        catch (CoverageArgumentException ex)
        {
            Console.Error.WriteLine($"coverage argument error: {ex.Message}");
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"coverage argument error: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"coverage content error: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"coverage output error: {ex.Message}");
            return 1;
        }
    }

    private static int RunNormals(ParsedArgs parsed)
    {
        if (parsed.Character == "all")
            throw new CoverageArgumentException("normal experiment requires exactly one attacker (all is not valid)");
        var attacker = Program.ResolveEntry(parsed.Character);
        var victim = Program.ResolveEntry(parsed.Victim);
        var options = new NormalCoverageOptions
        {
            Slots = parsed.Slots,
            X = parsed.X,
            Y = parsed.Y,
            Z = parsed.Z,
            ExtraPositions = parsed.ExtraPositions,
            MaxTicks = parsed.MaxTicks,
        };
        string basePath = Path.Combine("artifacts", "normal-coverage", $"{parsed.Character}-vs-{parsed.Victim}");
        string json = parsed.JsonPath ?? basePath + ".json";
        string html = parsed.HtmlPath ?? basePath + ".html";
        ValidateOutputPath(json, "--json");
        ValidateOutputPath(html, "--html");
        if (string.Equals(Path.GetFullPath(json), Path.GetFullPath(html), StringComparison.Ordinal))
            throw new CoverageArgumentException("--json and --html destinations must be distinct");
        var report = BuildNormals(attacker, victim, options);
        WriteFile(json, ToJson(report));
        WriteFile(html, ContactCoverageHtml.ToHtml(report));
        Console.Error.WriteLine($"coverage normals: attacker={parsed.Character}, victim={parsed.Victim}, slots={parsed.Slots.Count}, positions={report.Positions.Count}, samples={report.Samples.Count}");
        Console.Error.WriteLine($"wrote {json}");
        Console.Error.WriteLine($"wrote {html}");
        return 0;
    }

    private static void ValidateOutputPath(string path, string option)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new CoverageArgumentException($"{option} destination must be nonempty");
        _ = Path.GetFullPath(path);
    }

    internal static CoverageContext PrepareContext(MatchContentEntry attacker, MatchContentEntry victim, float airScale, ArenaDefinition arena)
    {
        if (!float.IsFinite(airScale) || airScale <= 0f || airScale > 1f)
            throw new ArgumentOutOfRangeException(nameof(airScale), "air-scale must be finite, greater than 0, and at most 1");
        var attackerDefinition = attacker.Definition;
        var victimDefinition = victim.Definition;
        if (airScale != 1f)
        {
            var movement = attackerDefinition.Movement;
            movement.AirSpeedMax *= airScale;
            attackerDefinition = attackerDefinition with { Movement = movement };
            movement = victimDefinition.Movement;
            movement.AirSpeedMax *= airScale;
            victimDefinition = victimDefinition with { Movement = movement };
        }
        var isolatedAttacker = new MatchContentEntry(attacker.Handle, attacker.LegacySelector, attacker.Identity,
            attacker.DisplayName, attackerDefinition, attacker.BakedAnimation, attacker.CookedCharacterPackage);
        var isolatedVictim = new MatchContentEntry(victim.Handle, victim.LegacySelector, victim.Identity,
            victim.DisplayName, victimDefinition, victim.BakedAnimation, victim.CookedCharacterPackage);
        return new CoverageContext(attacker, victim, isolatedAttacker, isolatedVictim, airScale, arena);
    }

    internal static CoverageSampleResult RunSample(CoverageContext context, SlotAddress slot, CoverageSampleSpec sample)
    {
        var scenario = ScenarioTable.SingleOrDefault(x => x.Id == sample.ScenarioId)
            ?? throw new ArgumentException($"unknown coverage scenario '{sample.ScenarioId}'", nameof(sample));
        if (!scenario.Allows(slot))
            return Unavailable(sample, slot, context.Profile, "wrong-locomotion-state");
        bool isAir = slot.IsAirborne;
        float attackerFeetY = scenario.AttackerFeetY == 0f && isAir ? 2f : scenario.AttackerFeetY;
        float victimFeetY = scenario.Id.StartsWith("target-cross-", StringComparison.Ordinal)
            ? (isAir ? 2f : 0f) : scenario.VictimFeetY;
        var movement = ScenarioMovement(scenario, context, isAir);
        var attacker = InitialState(context.AttackerDefinition, attackerFeetY, OriginX, OriginZ,
            movement.AttackerVX, movement.AttackerVY, movement.AttackerVZ, grounded: !isAir, facing: 0f);
        var victim = InitialState(context.VictimDefinition, victimFeetY, OriginX + sample.VictimRelativeX,
            OriginZ + sample.VictimRelativeZ, movement.VictimVX, movement.VictimVY, movement.VictimVZ,
            grounded: victimFeetY == 0f, facing: MathF.PI);
        var result = new CoverageSampleResult(sample.ScenarioId, slot, context.Profile, sample,
            Snapshot(attacker, context.AttackerDefinition), Snapshot(victim, context.VictimDefinition), victim.DamagePercent);
        var sim = new ServerSimulation(context.Arena);
        sim.RegisterEntity(1, context.AttackerDefinition, attacker, context.AttackerBaked);
        sim.RegisterEntity(100, context.VictimDefinition, victim, context.VictimBaked);
        var inputs = new Dictionary<ulong, InputState> { [1] = default, [100] = default };
        bool accepted = false;
        int lastTick = sample.PressTick + Program.MaxTicks - 1;

        for (int tick = 0; tick <= lastTick; tick++)
        {
            var beforeAttacker = sim.GetState(1);
            var beforeVictim = sim.GetState(100);
            var attackerInput = new InputState { MoveX = movement.AttackerMoveX, MoveY = movement.AttackerMoveY, AimYaw = 0 };
            var victimInput = new InputState { MoveX = movement.VictimMoveX, MoveY = movement.VictimMoveY, AimYaw = 0 };
            if (tick == sample.PressTick)
            {
                if (beforeAttacker.IsGrounded == slot.IsAirborne)
                {
                    result.Outcome = "unavailable";
                    result.Reason = "wrong-locomotion-state";
                    result.CompletionTick = tick;
                    result.PrePressAttacker = Snapshot(beforeAttacker, context.AttackerDefinition);
                    result.PrePressVictim = Snapshot(beforeVictim, context.VictimDefinition);
                    return result;
                }
                result.PrePressAttacker = Snapshot(beforeAttacker, context.AttackerDefinition);
                result.PrePressVictim = Snapshot(beforeVictim, context.VictimDefinition);
                attackerInput.ActiveSlot = Program.SlotByte(int.Parse(slot.InputLabel, CultureInfo.InvariantCulture));
            }
            inputs[1] = attackerInput;
            inputs[100] = victimInput;
            sim.Tick(inputs);
            var afterAttacker = sim.GetState(1);
            var afterVictim = sim.GetState(100);
            if (slot.IsAirborne && afterAttacker.IsGrounded)
                result.LandedBeforeContact = true;
            var hits = sim.LastTickHits.Where(x => x.OwnerEntityId == 1 && x.TargetEntityId == 100 && x.Damage > 0f).ToArray();
            if (hits.Length > 0)
            {
                accepted = true;
                result.ActivationTick ??= sample.PressTick;
                result.ActivationFacingYawDegrees ??= afterAttacker.FacingYaw * 180f / MathF.PI;
                result.AirborneAtStart ??= slot.IsAirborne;
                result.Outcome = "hit";
                result.CompletionTick = tick;
                result.TargetDamageAfter = afterVictim.DamagePercent;
                var first = hits[0];
                result.FirstContact = new CoverageContactRecord
                {
                    Tick = tick,
                    TicksAfterPress = tick - sample.PressTick,
                    Damage = first.Damage,
                    PositionX = first.HitX - OriginX,
                    PositionY = first.HitY,
                    PositionZ = first.HitZ - OriginZ,
                    AttackerState = Snapshot(afterAttacker, context.AttackerDefinition),
                    VictimState = Snapshot(afterVictim, context.VictimDefinition),
                };
                return result;
            }

            if (tick == sample.PressTick)
            {
                var active = sim.GetActiveAbility(1);
                if (active != null && active.Slot + 1 == Program.SlotByte(int.Parse(slot.InputLabel, CultureInfo.InvariantCulture))
                    && active.AirborneAtStart == slot.IsAirborne)
                {
                    accepted = true;
                    result.ActivationTick = tick;
                    result.ActivationFacingYawDegrees = afterAttacker.FacingYaw * 180f / MathF.PI;
                    result.AirborneAtStart = active.AirborneAtStart;
                }
                else if (active == null && !sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1))
                {
                    result.Outcome = "unavailable";
                    result.Reason = "activation-rejected";
                    result.CompletionTick = tick;
                    return result;
                }
            }

            if (accepted && sim.GetActiveAbility(1) == null && !sim.Resolver.GetActiveHitboxes().Any(x => x.OwnerId == 1))
            {
                result.Outcome = "miss";
                result.Reason = null;
                result.CompletionTick = tick;
                result.TargetDamageAfter = afterVictim.DamagePercent;
                return result;
            }
            if (accepted && !slot.IsAirborne && !afterAttacker.IsGrounded)
                result.LandedBeforeContact = true;
        }

        result.Outcome = accepted ? "truncated" : "unavailable";
        result.Reason = accepted ? "max-ticks-cap" : "activation-rejected";
        result.CompletionTick = lastTick;
        return result;
    }

    internal static CoverageReportData Build(IReadOnlyList<MatchContentEntry> attackers, MatchContentEntry victim, CoverageOptions options)
        => Build(attackers, victim, options, Program.NoRespawn(Program.BuildArena()));

    private static CoverageReportData Build(IReadOnlyList<MatchContentEntry> attackers, MatchContentEntry victim, CoverageOptions options, ArenaDefinition arena)
    {
        if (attackers == null || attackers.Count == 0) throw new ArgumentException("at least one attacker is required", nameof(attackers));
        if (!float.IsFinite(options.GridStep) || options.GridStep <= 0f) throw new ArgumentOutOfRangeException(nameof(options.GridStep));
        if (!float.IsFinite(options.CandidateAirScale) || options.CandidateAirScale <= 0f || options.CandidateAirScale > 1f)
            throw new ArgumentOutOfRangeException(nameof(options.CandidateAirScale));
        var report = new CoverageReportData
        {
            Victim = Identity(victim, 1f, "common victim; profile-local isolated copy"),
            Grid = new CoverageGridData { Step = options.GridStep },
            Timing = new CoverageTimingBounds { MinPressTick = 0, MaxPressTick = TimingMaxPressTick },
        };
        report.Scenarios.AddRange(ScenarioTable);
        report.Cautions.AddRange(new[]
        {
            "sampled contact is not human hit probability",
            "passive airborne states fall naturally",
            "no authored per-hit identity is available from SpellResolver.HitResult",
            "baseline/candidate movement changes are tool-local hypothetical overrides; source hashes remain admitted-content identities",
        });
        foreach (var entry in attackers)
            report.Attackers.Add(Identity(entry, 1f, "source admitted content"));
        foreach (var entry in attackers)
        {
            var baseline = PrepareContext(entry, victim, 1f, arena);
            var candidate = PrepareContext(entry, victim, options.CandidateAirScale, arena);
            var moveEntries = options.Slots.Select(slot => (slot, cooked: RequireCookedSlot(entry, slot))).ToArray();
            var moveData = new Dictionary<string, CoverageMoveData>(StringComparer.Ordinal);
            foreach (var (slot, cooked) in moveEntries)
            {
                var move = new CoverageMoveData
                {
                    Attacker = entry.Identity.PackageId,
                    Slot = slot.Id,
                    Name = cooked.Name,
                    Description = cooked.Description,
                    Ordinal = slot.Ordinal,
                    AuthoredTimelineDurationTicks = cooked.Timeline.Stages.Sum(x => x.DurationTicks),
                    HasAuthoredDamagingOperation = AuthoredDamage(cooked),
                };
                moveData.Add(slot.Id, move);
                foreach (var scenario in ScenarioTable.Where(x => x.Allows(slot)))
                {
                    var map = new CoverageMapData { ScenarioId = scenario.Id };
                    foreach (float z in GridValues(-4f, 6f, options.GridStep))
                    foreach (float x in GridValues(-4f, 4f, options.GridStep))
                    {
                        var spec = new CoverageSampleSpec(scenario.Id, x, z, 0);
                        var b = RunSample(baseline, slot, spec);
                        var c = RunSample(candidate, slot, spec);
                        if (map.Cells.Count == 0)
                        {
                            report.InitialStates.Add(Template(entry.Identity.PackageId, slot.Id, scenario.Id, BaselineProfile, b));
                            report.InitialStates.Add(Template(entry.Identity.PackageId, slot.Id, scenario.Id, CandidateProfile, c));
                        }
                        b.PrePressAttacker = null;
                        b.PrePressVictim = null;
                        c.PrePressAttacker = null;
                        c.PrePressVictim = null;
                        string comparison = Compare(b, c);
                        map.Cells.Add(new CoverageMapCell { RelativeX = x, RelativeZ = z, Baseline = b, Candidate = c, Comparison = comparison });
                        switch (comparison)
                        {
                            case "both-hit": map.BothHit++; break;
                            case "gained": map.Gained++; break;
                            case "lost": map.Lost++; break;
                            case "both-miss": map.BothMiss++; break;
                            default: map.NotComparable++; break;
                        }
                        if ((b.Outcome == "hit" || c.Outcome == "hit") && (Math.Abs(x - 4f) < 0.0001f || Math.Abs(x + 4f) < 0.0001f || Math.Abs(z - 6f) < 0.0001f || Math.Abs(z + 4f) < 0.0001f))
                            map.EdgeHit = true;
                    }
                    move.Maps.Add(map);
                    if (!scenario.Id.StartsWith("passive-", StringComparison.Ordinal))
                        move.Timing.Add(BuildTiming(entry, victim, baseline, candidate, slot, scenario));
                }
                report.Moves.Add(move);
            }
            report.Profiles.Add(Profile(BaselineProfile, 1f, baseline, baseline));
            report.Profiles.Add(Profile(CandidateProfile, options.CandidateAirScale, candidate, candidate));
        }
        return report;
    }

    private static CoverageTimingData BuildTiming(MatchContentEntry attacker, MatchContentEntry victim,
        CoverageContext baseline, CoverageContext candidate, SlotAddress slot, CoverageScenarioDefinition scenario)
    {
        var timing = new CoverageTimingData { ScenarioId = scenario.Id };
        foreach (var (x, z) in TimingLanes(scenario.Id))
        {
            var lane = new CoverageTimingLane { RelativeX = x, RelativeZ = z };
            for (int press = 0; press <= TimingMaxPressTick; press++)
            {
                var sample = new CoverageSampleSpec(scenario.Id, x, z, press);
                lane.BaselineSamples.Add(RunSample(baseline, slot, sample));
                lane.CandidateSamples.Add(RunSample(candidate, slot, sample));
            }
            lane.BaselineIntervals.AddRange(FindHitWindows(lane.BaselineSamples));
            lane.CandidateIntervals.AddRange(FindHitWindows(lane.CandidateSamples));
            timing.Lanes.Add(lane);
        }
        return timing;
    }

    internal static CoverageTimingInterval[] FindHitWindows(IReadOnlyList<CoverageSampleResult> samples)
    {
        if (samples == null || samples.Count == 0) return Array.Empty<CoverageTimingInterval>();
        var result = new List<CoverageTimingInterval>();
        foreach (var group in samples.GroupBy(x => (x.ScenarioId, x.Profile, x.InitialRelativeX, x.InitialRelativeZ)))
        {
            var ordered = group.OrderBy(x => x.PressTick).ToArray();
            int i = 0;
            while (i < ordered.Length)
            {
                if (ordered[i].Outcome != "hit") { i++; continue; }
                int start = ordered[i].PressTick;
                int end = start;
                int count = 1;
                int j = i + 1;
                while (j < ordered.Length && ordered[j].Outcome == "hit" && ordered[j].PressTick == end + 1)
                {
                    end = ordered[j].PressTick;
                    count++;
                    j++;
                }
                result.Add(new CoverageTimingInterval
                {
                    ScenarioId = group.Key.ScenarioId,
                    Profile = group.Key.Profile,
                    LaneX = group.Key.InitialRelativeX,
                    LaneZ = group.Key.InitialRelativeZ,
                    StartPressTick = start,
                    EndPressTick = end,
                    SuccessfulPressTicks = count,
                    LongestContiguousTicks = count,
                    NominalMilliseconds = count * 1000f / 60f,
                    BoundaryClipped = start == 0 || end == TimingMaxPressTick,
                });
                i = j;
            }
        }
        return result.OrderBy(x => x.LaneZ).ThenBy(x => x.LaneX).ThenBy(x => x.StartPressTick).ToArray();
    }

    internal static string ToJson(CoverageReportData report)
        => JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
        });

    private static ParsedArgs ParseArgs(string[] args)
    {
        string character = "all", victim = "fightguy", experiment = "dense";
        var slots = new List<SlotAddress>();
        float step = 0.5f, airScale = 0.8f;
        IReadOnlyList<float> x = new[] { -1f, 0f, 1f };
        IReadOnlyList<float> y = new[] { 0f, 1f, 2f };
        IReadOnlyList<float> z = new[] { 0.75f, 1.5f, 2.25f };
        IReadOnlyList<CoveragePosition> extras = Array.Empty<CoveragePosition>();
        int maxTicks = Program.MaxTicks;
        string? json = null, html = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool positional = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--coverage") continue;
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (positional) throw new CoverageArgumentException("only one attacker selector is allowed");
                character = arg.ToLowerInvariant(); positional = true; continue;
            }
            string key = arg.ToLowerInvariant();
            if (key is "--reach" or "--truecombos" or "--kbm" or "--pcts" or "--example" or "--out")
                throw new CoverageArgumentException($"{arg} is not valid with --coverage");
            if (key is not ("--victim" or "--slots" or "--step" or "--air-scale" or "--json" or "--html"
                or "--experiment" or "--x" or "--y" or "--z" or "--extra"))
                throw new CoverageArgumentException($"unknown option '{arg}'");
            if (!seen.Add(key)) throw new CoverageArgumentException($"duplicate option '{arg}'");
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new CoverageArgumentException($"missing value for {arg}");
            string value = args[i];
            switch (key)
            {
                case "--victim": victim = value.ToLowerInvariant(); break;
                case "--slots": slots = ParseSlots(value); break;
                case "--experiment":
                    experiment = value.ToLowerInvariant();
                    if (experiment is not ("dense" or "normals"))
                        throw new CoverageArgumentException($"unknown coverage experiment '{value}' (expected dense or normals)");
                    break;
                case "--step":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out step) || step is not (0.25f or 0.5f or 1f))
                        throw new CoverageArgumentException("--step must be 0.25, 0.5, or 1");
                    break;
                case "--air-scale":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out airScale) || !float.IsFinite(airScale) || airScale <= 0f || airScale > 1f)
                        throw new CoverageArgumentException("--air-scale must be finite, greater than 0, and at most 1");
                    break;
                case "--x": x = ParseNormalAxis(value, "X", false); break;
                case "--y": y = ParseNormalAxis(value, "Y", true); break;
                case "--z": z = ParseNormalAxis(value, "Z", false); break;
                case "--extra": extras = ParseNormalExtras(value); break;
                case "--json": json = value; break;
                case "--html": html = value; break;
            }
        }
        if (character is not ("all" or "fightguy" or "manki" or "wibou" or "bonk")) throw new CoverageArgumentException($"unknown character: {character} (expected all, fightguy, manki, wibou, bonk)");
        if (victim is not ("fightguy" or "manki" or "wibou" or "bonk")) throw new CoverageArgumentException($"unknown victim: {victim}");
        if (slots.Count == 0) slots.AddRange(CanonicalSlotProjection.All.Where(x => x.InputLabel is "1" or "2" or "3"));
        if (experiment == "normals")
        {
            if (character == "all") throw new CoverageArgumentException("normal experiment requires exactly one attacker (all is not valid)");
            if (seen.Contains("--step") || seen.Contains("--air-scale"))
                throw new CoverageArgumentException("--step and --air-scale are not valid with --experiment normals");
        }
        else if (seen.Contains("--x") || seen.Contains("--y") || seen.Contains("--z") || seen.Contains("--extra"))
            throw new CoverageArgumentException("--x, --y, --z, and --extra are only valid with --experiment normals");
        return new ParsedArgs(character, victim, experiment, slots.OrderBy(x => x.Ordinal).ToArray(), step, airScale,
            x, y, z, extras, maxTicks, json, html);
    }

    private static List<SlotAddress> ParseSlots(string value)
    {
        var result = new List<SlotAddress>();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string id = raw.Trim();
            if (!CanonicalSlotProjection.TryGet(id, out var slot) || slot.InputLabel is not ("1" or "2" or "3"))
                throw new CoverageArgumentException($"unknown normal slot '{id}'");
            if (!result.Contains(slot)) result.Add(slot);
        }
        if (result.Count == 0) throw new CoverageArgumentException("--slots requires at least one canonical normal slot");
        return result;
    }

    private static CookedSlotDefinition RequireCookedSlot(MatchContentEntry entry, SlotAddress slot)
    {
        var cooked = entry.Definition.GetCookedSlotAbility(Program.SlotByte(int.Parse(slot.InputLabel, CultureInfo.InvariantCulture)), slot.IsAirborne);
        if (cooked == null || !string.Equals(cooked.Id, slot.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"{entry.Identity.PackageId}: admitted cooked slot '{slot.Id}' is missing or resolves inconsistently");
        return cooked;
    }

    private static bool? AuthoredDamage(CookedSlotDefinition slot)
    {
        bool capability = false, damage = false;
        foreach (var op in slot.Timeline.Stages.SelectMany(x => x.Operations))
        {
            if (op is CookedSpawnHitboxOperation h && h.Hitbox.Damage > 0f) damage = true;
            else if (op is CookedSpawnProjectileOperation p && p.Projectile.Damage > 0f) damage = true;
            else if (op is CookedStartCapabilityOperation) capability = true;
        }
        return capability && !damage ? null : damage;
    }

    private static string Compare(CoverageSampleResult baseline, CoverageSampleResult candidate)
    {
        if (baseline.Outcome == "hit" && candidate.Outcome == "hit") return "both-hit";
        if (baseline.Outcome == "miss" && candidate.Outcome == "miss") return "both-miss";
        if (baseline.Outcome != "hit" && candidate.Outcome == "hit") return baseline.Outcome == "miss" ? "gained" : "not-comparable";
        if (baseline.Outcome == "hit" && candidate.Outcome != "hit") return candidate.Outcome == "miss" ? "lost" : "not-comparable";
        return baseline.Outcome == "miss" && candidate.Outcome == "miss" ? "both-miss" : "not-comparable";
    }

    private static CoverageSampleResult Unavailable(CoverageSampleSpec sample, SlotAddress slot, string profile, string reason)
        => new CoverageSampleResult(sample.ScenarioId, slot, profile, sample, new(), new(), 0f) { Outcome = "unavailable", Reason = reason };

    private static (float AttackerVX, float AttackerVY, float AttackerVZ,
        float VictimVX, float VictimVY, float VictimVZ,
        float AttackerMoveX, float AttackerMoveY, float VictimMoveX, float VictimMoveY)
        ScenarioMovement(CoverageScenarioDefinition scenario, CoverageContext context, bool airborne)
    {
        float run = context.VictimDefinition.Movement.RunSpeed;
        float air = context.VictimDefinition.Movement.AirSpeedMax;
        float attackerAir = context.AttackerDefinition.Movement.AirSpeedMax;
        return scenario.Id switch
        {
            "target-cross-right" => (0, 0, 0, airborne ? air : run, 0, 0, 0, 0, 1, 0),
            "target-cross-left" => (0, 0, 0, airborne ? -air : -run, 0, 0, 0, 0, -1, 0),
            "target-approach" => (0, 0, 0, 0, 0, -run, 0, 0, 0, -1),
            "target-retreat" => (0, 0, 0, 0, 0, run, 0, 0, 0, 1),
            "target-descend" => (0, 0, 0, 0, -4, 0, 0, 0, 0, 0),
            "attacker-drift" => (0, 0, attackerAir, 0, 0, 0, 0, 1, 0, 0),
            _ => (0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        };
    }

    private static CharacterState InitialState(CharacterDefinition def, float feetY, float x, float z,
        float vx, float vy, float vz, bool grounded, float facing)
        => new()
        {
            PX = x, PY = feetY + def.CapsuleHeight * 0.5f, PZ = z,
            VX = vx, VY = vy, VZ = vz, IsGrounded = grounded,
            State = ActionState.Idle, JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1, AirTimeTicks = grounded ? (ushort)0 : def.Movement.FloatWindowTicks,
            FacingYaw = facing, AimYaw = 0f, AimPitch = 0f, TargetEntityId = 0,
            LockOn = false, DamagePercent = 0,
        };

    private static CoverageInitialStateTemplate Template(string attacker, string slot, string scenario,
        string profile, CoverageSampleResult sample)
        => new()
        {
            Attacker = attacker,
            Slot = slot,
            ScenarioId = scenario,
            Profile = profile,
            AttackerState = sample.InitialAttacker,
            VictimState = new CoverageStateSnapshot
            {
                PositionX = 0f,
                PositionY = sample.InitialVictim.PositionY,
                PositionZ = 0f,
                CapsuleCenterY = sample.InitialVictim.CapsuleCenterY,
                VelocityX = sample.InitialVictim.VelocityX,
                VelocityY = sample.InitialVictim.VelocityY,
                VelocityZ = sample.InitialVictim.VelocityZ,
                IsGrounded = sample.InitialVictim.IsGrounded,
                ActionState = sample.InitialVictim.ActionState,
                FacingYawDegrees = sample.InitialVictim.FacingYawDegrees,
            },
        };
    private static CoverageStateSnapshot Snapshot(CharacterState s, CharacterDefinition def)
        => new()
        {
            PositionX = s.PX - OriginX, PositionY = s.PY - def.CapsuleHeight * 0.5f, PositionZ = s.PZ - OriginZ,
            CapsuleCenterY = s.PY, VelocityX = s.VX, VelocityY = s.VY, VelocityZ = s.VZ,
            IsGrounded = s.IsGrounded, ActionState = s.State.ToString(), FacingYawDegrees = s.FacingYaw * 180f / MathF.PI,
        };

    private static IEnumerable<float> GridValues(float min, float max, float step)
    {
        int count = (int)MathF.Round((max - min) / step);
        for (int i = 0; i <= count; i++) yield return min + i * step;
    }

    private static IEnumerable<(float X, float Z)> TimingLanes(string scenario)
        => scenario switch
        {
            "target-cross-right" => new[] { ( -3f, .5f), (-3f, 1f), (-3f, 1.5f), (-3f, 2f), (-3f, 3f) },
            "target-cross-left" => new[] { ( 3f, .5f), (3f, 1f), (3f, 1.5f), (3f, 2f), (3f, 3f) },
            "target-approach" => new[] { (-1f, 4f), (-.5f, 4f), (0f, 4f), (.5f, 4f), (1f, 4f) },
            "target-retreat" => new[] { (-1f, .5f), (-.5f, .5f), (0f, .5f), (.5f, .5f), (1f, .5f) },
            _ => new[] { (0f, .5f), (0f, 1f), (0f, 1.5f), (0f, 2f), (0f, 3f) },
        };

    private static CoverageIdentityData Identity(MatchContentEntry entry, float scale, string note)
        => Identity(entry, entry.Definition, note);

    private static CoverageIdentityData Identity(MatchContentEntry entry, CharacterDefinition def, string note)
        => new()
        {
            Selector = entry.LegacySelector?.ToString().ToLowerInvariant() ?? entry.Identity.PackageId,
            DisplayName = entry.DisplayName,
            Identity = entry.Identity,
            OverrideNote = note,
            EffectiveMovement = Movement(def),
        };

    private static CoverageMovementData Movement(CharacterDefinition def)
        => new()
        {
            RunSpeed = def.Movement.RunSpeed,
            AirSpeedMax = def.Movement.AirSpeedMax,
            Gravity = def.Movement.Gravity,
            AirFloatGravity = def.Movement.AirFloatGravity,
            AirAcceleration = def.Movement.AirAccelStick + def.Movement.AirAccelBase,
            GroundAcceleration = def.Movement.RunAccelerationA + def.Movement.RunAccelerationB,
            CapsuleHeight = def.CapsuleHeight,
        };

    private static CoverageProfileData Profile(string id, float scale, CoverageContext attacker, CoverageContext victim)
        => new()
        {
            Id = id,
            RequestedAirSpeedMultiplier = scale,
            EffectiveCharacters =
            {
                Identity(attacker.Attacker, attacker.AttackerDefinition,
                    id == CandidateProfile ? "tool-local hypothetical AirSpeedMax override" : "admitted source movement"),
                Identity(victim.Victim, victim.VictimDefinition,
                    id == CandidateProfile ? "tool-local hypothetical AirSpeedMax override" : "admitted source movement"),
            },
        };

    private static void WriteFile(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    private sealed record ParsedArgs(string Character, string Victim, string Experiment, IReadOnlyList<SlotAddress> Slots,
        float Step, float AirScale, IReadOnlyList<float> X, IReadOnlyList<float> Y, IReadOnlyList<float> Z,
        IReadOnlyList<CoveragePosition> ExtraPositions, int MaxTicks, string? JsonPath, string? HtmlPath);
    private sealed class CoverageArgumentException : Exception { internal CoverageArgumentException(string message) : base(message) { } }
}
