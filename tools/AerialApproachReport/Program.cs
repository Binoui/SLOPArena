#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared;

namespace SlopArena.AerialApproachReport;

internal static partial class Program
{
    private const ulong AttackerId = 1;
    private const ulong TargetId = 100;
    private const float OriginX = 100f;
    private const float OriginZ = 100f;
    private const int DefaultMaxOffset = 72;
    private const int MaxTicks = 150;
    private const float LowInputMagnitude = 0f;
    private const int RunupTicks = 30;

    private static readonly string[] Characters = { "fightguy", "manki", "wibou", "bonk" };
    private static readonly float[] GridX = { -1f, -0.5f, 0f, 0.5f, 1f };
    private static readonly float[] GridZ = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f };
    private static readonly float[] TargetDistanceGrid = { 0.75f, 1.25f, 1.75f, 2.25f };

    private sealed record Options(string OutDirectory, int MaxOffset, bool Assert, bool SlideFlow, int SlideDeceleration, string FallProfile, float BraceMultiplier);
    private sealed record JumpTiming(int TakeoffTick, int ApexTick, int LandingTick, float RunupDistance);
    private sealed record Approach(string Id, float X, float Z, string Description);
    private sealed record Motion(string Id, string Description, bool Moving);
    private sealed record Speed(string Id, string Description, float InputMagnitude, int Runup);
    private sealed record TargetSize(string Id, MatchContentEntry Entry, string SelectionBasis);

    private sealed class RemovedHitbox
    {
        public int Tick { get; init; }
        public Hitbox Hitbox { get; init; }
    }

    private sealed class RunCapture
    {
        public CastRow Row { get; init; } = new();
        public List<RemovedHitbox> Removed { get; } = new();
        public Dictionary<string, int> ContactOccurrences { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Report
    {
        public int SchemaVersion { get; init; } = 1;
        public int TickRateHz { get; init; } = 60;
        public string GeneratedAtUtc { get; init; } = "";
        public ReportParameters Parameters { get; init; } = new();
        public List<Provenance> AdmittedRoster { get; } = new();
        public List<TimingReference> JumpTiming { get; } = new();
        public List<CastRow> Casts { get; } = new();
        public List<PressWindow> SuccessfulPressWindows { get; } = new();
        public List<BonkGridCell> BonkGroundedCanonical3Grid { get; } = new();
        public ReportSummary Summary { get; init; } = new();
        public BonkAuthoringEvidence BonkGroundedCanonical3Authoring { get; set; } = new();
        public List<string> Limitations { get; } = new();
    }
    private sealed class BonkAuthoringEvidence
    {
        public string CanonicalSlot { get; init; } = "ground.3";
        public byte ActualWireSlot { get; init; }
        public string ActualCookedSlot { get; init; } = "";
        public int OperationCount { get; init; }
        public List<string> HitboxIdentities { get; init; } = new();
    }

    private sealed class ReportParameters
    {
        public string Tool { get; init; } = "AerialApproachReport";
        public string[] Characters { get; init; } = Array.Empty<string>();
        public string[] AerialNormals { get; init; } = new[] { "air.1", "air.2", "air.3", "air.4" };
        public string[] TargetModes { get; init; } = new[] { "stationary", "fixed-path" };
        public string[] Approaches { get; init; } = new[] { "straight", "oblique" };
        public string[] HorizontalSpeeds { get; init; } = new[] { "low", "running" };
        public string[] PressPhases { get; init; } = new[] { "rising", "apex", "falling" };
        public Dictionary<string, string> ApproachVectors { get; init; } = new(StringComparer.Ordinal)
        {
            ["straight"] = "x=0,z=1; target fixed-path vector is x=0,z=1",
            ["oblique"] = "x=0.5,z=0.8660254; target fixed-path vector is x=-0.8660254,z=0.5",
        };
        public Dictionary<string, string> HorizontalSpeedInputs { get; init; } = new(StringComparer.Ordinal)
        {
            ["low"] = "air input magnitude=0; runupTicks=0",
            ["running"] = "air input magnitude=1; runupTicks=30",
        };
        public int MaxPressOffset { get; init; }
        public int MaxSimulationTicks { get; init; } = MaxTicks;
        public int RunupTicks { get; init; } = Program.RunupTicks;
        public string PressOffsetBasis { get; init; } = "offset 0 is the first simulation tick after the real takeoff transition; phase labels use an independent real jump timing probe";
        public string FixedPathBasis { get; init; } = "target receives a constant normalized server movement input on the perpendicular crossing vector starting at attacker takeoff; its authoritative resulting path is recorded, not prescribed";
        public float[] TargetDistances { get; init; } = TargetDistanceGrid;
        public string TargetInitialPositionBasis { get; init; } = "target world start is offset by the attacker's measured run-up distance so target-minus-attacker placement equals each grid distance at takeoff; fixed-path movement begins after that sample";
        public string CoordinateBasis { get; init; } = "relative positions are target minus attacker in world X/Y/Z; +Z is yaw-zero forward";
        public string TargetSelectionBasis { get; init; } = "small and large are selected from the admitted four-character roster by capsule radius × height";
    }

    private sealed class Provenance
    {
        public string Selector { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public MatchContentIdentity Identity { get; init; } = null!;
        public bool HasCookedPackage { get; init; }
        public bool HasBakedAnimation { get; init; }
        public float CapsuleRadius { get; init; }
        public float CapsuleHeight { get; init; }
    }

    private sealed class TimingReference
    {
        public string Character { get; init; } = "";
        public string HorizontalSpeed { get; init; } = "";
        public int RunupTicks { get; init; }
        public int TakeoffTick { get; init; }
        public int ApexTick { get; init; }
        public int LandingTick { get; init; }
        public float RunupDistance { get; init; }
        public int ApexOffset { get; init; }
        public int AirtimeTicks { get; init; }
        public string InputDescription { get; init; } = "";
    }

    private sealed class CastRow
    {
        public string Character { get; init; } = "";
        public string Slot { get; init; } = "";
        public string ActualCookedSlot { get; init; } = "";
        public string TargetCharacter { get; init; } = "";
        public string TargetSize { get; init; } = "";
        public float TargetDistance { get; init; }
        public string TargetMode { get; init; } = "";
        public string Approach { get; init; } = "";
        public string HorizontalSpeed { get; init; } = "";
        public string AttackerSourceHash { get; init; } = "";
        public string AttackerCookedContentHash { get; init; } = "";
        public string AttackerPackageHash { get; init; } = "";
        public string TargetSourceHash { get; init; } = "";
        public string TargetCookedContentHash { get; init; } = "";
        public string TargetPackageHash { get; init; } = "";
        public int PressOffset { get; init; }
        public int PressTick { get; init; }
        public string PressPhase { get; init; } = "";
        public string ObservedPressPhase { get; set; } = "";
        public bool Accepted { get; set; }
        public string? RejectionReason { get; set; }
        public bool LandedBeforePress { get; set; }
        public bool LandedWithoutHit { get; set; }
        public int? FirstActiveFrame { get; set; }
        public int? FirstHitFrame { get; set; }
        public int? FirstHitTicksAfterPress { get; set; }
        public int ActiveFrameCount { get; set; }
        public Vector3Data? PressToFirstActiveDisplacement { get; set; }
        public StateSnapshot? AttackerAtPress { get; set; }
        public StateSnapshot? TargetAtPress { get; set; }
        public float ActualHorizontalSpeedAtPress { get; set; }
        public List<ActiveFrameObservation> ActiveFrames { get; } = new();
        public List<ContactObservation> Contacts { get; } = new();
    }

    private sealed class ActiveFrameObservation
    {
        public int Tick { get; init; }
        public int TicksAfterPress { get; init; }
        public int AttackElapsedTicks { get; init; }
        public StateSnapshot Attacker { get; init; } = new();
        public StateSnapshot Target { get; init; } = new();
        public Vector3Data RelativePosition { get; init; } = new();
        public Vector3Data RelativeVelocity { get; init; } = new();
        public string[] HitboxIdentities { get; init; } = Array.Empty<string>();
    }

    private sealed class ContactObservation
    {
        public int Frame { get; init; }
        public int TicksAfterPress { get; init; }
        public string ContactIdentity { get; init; } = "resolver-slot-only";
        public string PartClassification { get; init; } = "insufficient-geometry-provenance";
        public string Sequence { get; init; } = "first";
        public float Damage { get; init; }
        public Vector3Data ContactPoint { get; init; } = new();
        public StateSnapshot Attacker { get; init; } = new();
        public StateSnapshot Target { get; init; } = new();
    }

    private sealed class StateSnapshot
    {
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public float PositionZ { get; init; }
        public float VelocityX { get; init; }
        public float VelocityY { get; init; }
        public float VelocityZ { get; init; }
        public bool IsGrounded { get; init; }
        public string State { get; init; } = "Idle";
        public float FacingYawDegrees { get; init; }
    }

    private sealed class Vector3Data
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
    }
    private sealed class PressWindow
    {
        public string Character { get; init; } = "";
        public string Slot { get; init; } = "";
        public string TargetCharacter { get; init; } = "";
        public string TargetSize { get; init; } = "";
        public float TargetDistance { get; init; }
        public string TargetMode { get; init; } = "";
        public string Approach { get; init; } = "";
        public string HorizontalSpeed { get; init; } = "";
        public string PressPhase { get; init; } = "";
        public string AttackerPackageHash { get; init; } = "";
        public string AttackerCookedContentHash { get; init; } = "";
        public string TargetPackageHash { get; init; } = "";
        public string TargetCookedContentHash { get; init; } = "";
        public int StartPressOffset { get; init; }
        public int EndPressOffset { get; init; }
        public int SuccessfulPressTicks { get; init; }
        public bool BoundaryClipped { get; init; }
    }

    private sealed class BonkGridCell
    {
        public string Character { get; init; } = "bonk";
        public string Slot { get; init; } = "ground.3";
        public byte ActualWireSlot { get; init; }
        public string ActualCookedSlot { get; init; } = "";
        public string CharacterPackageHash { get; init; } = "";
        public string CharacterCookedContentHash { get; init; } = "";
        public string TargetPackageHash { get; init; } = "";
        public string TargetCookedContentHash { get; init; } = "";
        public string TargetCharacter { get; init; } = "";
        public string TargetSize { get; init; } = "";
        public float RelativeX { get; init; }
        public float RelativeZ { get; init; }
        public bool Accepted { get; set; }
        public string? RejectionReason { get; set; }
        public int? FirstHitFrame { get; set; }
        public List<ContactObservation> Contacts { get; } = new();
    }

    private sealed class ReportSummary
    {
        public int CastCount { get; set; }
        public int AcceptedCastCount { get; set; }
        public int RejectedCastCount { get; set; }
        public int LandedBeforePressCount { get; set; }
        public int LandedWithoutHitCount { get; set; }
        public int HitCastCount { get; set; }
        public float? HitRateAmongAcceptedCasts { get; set; }
        public int BonkGridCellCount { get; set; }
        public int BonkGridHitCellCount { get; set; }
    }

    public static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            if (options.SlideFlow)
            {
                var flow = BuildSlideFlow(options);
                Console.WriteLine($"slide-flow: scenarios={flow.Summary.ScenarioCount} timeouts={flow.Summary.TimeoutCount} landings={flow.Summary.LandingCount} accepted={flow.Summary.AcceptedActionCount}");
                Console.WriteLine($"wrote {Path.GetFullPath(options.OutDirectory)}/slide-flow.json, slide-traces.csv, aerial-slide-approaches.csv, slide-followups.csv, slide-targets.csv");
                return 0;
            }
            var report = BuildReport(options);
            if (options.Assert) SelfCheck(report);
            WriteOutputs(report, options.OutDirectory);
            Console.WriteLine($"aerial: casts={report.Summary.CastCount} accepted={report.Summary.AcceptedCastCount} hits={report.Summary.HitCastCount} accepted-hit-rate={Format(report.Summary.HitRateAmongAcceptedCasts)}");
            Console.WriteLine($"bonk g3 grid: cells={report.Summary.BonkGridCellCount} hit-cells={report.Summary.BonkGridHitCellCount}");
            Console.WriteLine($"wrote {Path.GetFullPath(options.OutDirectory)}/report.json, casts.csv, windows.csv, bonk-g3-grid.csv");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"aerial argument error: {ex.Message}");
            return 2;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"aerial content error: {ex.Message}");
            return 1;
        }
    }

    private static Options ParseArgs(string[] args)
    {
        string? output = null;
        int maxOffset = DefaultMaxOffset;
        bool check = false;
        bool slideFlow = false;
        int slideDeceleration = 2;
        string fallProfile = "current";
        float braceMultiplier = 0.9f;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assert": check = true; break;
                case "--slide-flow": slideFlow = true; break;
                case "--out":
                    if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("--out requires a directory");
                    output = args[i];
                    break;
                case "--max-offset":
                    if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out maxOffset) || maxOffset < 0 || maxOffset > 120)
                        throw new ArgumentException("--max-offset must be an integer from 0 through 120");
                    break;
                case "--slide-deceleration":
                    if (++i >= args.Length || (args[i] != "2" && args[i] != "3"))
                        throw new ArgumentException("--slide-deceleration must be 2 or 3");
                    slideDeceleration = int.Parse(args[i], CultureInfo.InvariantCulture);
                    break;
                case "--fall-profile":
                    if (++i >= args.Length || (args[i] != "current" && args[i] != "previous"))
                        throw new ArgumentException("--fall-profile must be current or previous");
                    fallProfile = args[i];
                    break;
                case "--brace-multiplier":
                    if (++i >= args.Length || (args[i] != "0.9" && args[i] != "1"))
                        throw new ArgumentException("--brace-multiplier must be 0.9 or 1");
                    braceMultiplier = float.Parse(args[i], CultureInfo.InvariantCulture);
                    break;
                default: throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        return new Options(Path.GetFullPath(output ?? (slideFlow ? Path.Combine("artifacts", "slide-flow") : Path.Combine("artifacts", "aerial-approach"))),
            maxOffset, check, slideFlow, slideDeceleration, fallProfile, braceMultiplier);
    }

    private static Report BuildReport(Options options)
    {
        var entries = Characters.Select(ResolveEntry).ToArray();
        foreach (var entry in entries)
            RequireAdmittedCooked(entry);
        var sizes = SelectTargetSizes(entries);
        var report = new Report
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Parameters = new ReportParameters { Characters = Characters, MaxPressOffset = options.MaxOffset },
        };
        report.Limitations.AddRange(new[]
        {
            "This is a finite deterministic evidence sweep, not a player hit probability estimate.",
            "Accepted casts are denominators for aerial hit rate; rejected and landed-before-press attempts are reported separately and excluded.",
            "Low speed is an explicit zero-air-drift baseline because Shared normalizes nonzero stick input; running is a measured 30-tick full-speed run-up plus air drift.",
            "Target distance is swept at 0.75, 1.25, 1.75, and 2.25 m; 0.75 m is the nearest non-overlapping baseline for the admitted capsule radii.",
            "Target world start is offset by the measured attacker run-up so target-minus-attacker placement equals each grid distance at takeoff; fixed-path movement begins after that sample to avoid run-up placement confounding.",
            "Resolver HitResult exposes activation/slot and contact point, but not an authored hitbox-event id. Part identity uses actual active/removed Hitbox.SourceEvent geometry when available; otherwise the report says resolver-slot-only.",
            "No causal miss label is emitted when recorded geometry does not establish one.",
            "Bonk ground.3 contact sequence is actual LastTickHits; first/later is per distinct observed contact identity, not a balance classification.",
        });
        foreach (var entry in entries) report.AdmittedRoster.Add(ProvenanceOf(entry));

        var approaches = new[]
        {
            new Approach("straight", 0f, 1f, "attacker and target path on +Z/-Z"),
            new Approach("oblique", 0.5f, MathF.Sqrt(3f) * 0.5f, "attacker and target path at 30 degrees from +Z"),
        };
        var motions = new[]
        {
            new Motion("stationary", "target neutral input", false),
            new Motion("fixed-path", "target holds a constant perpendicular crossing path", true),
        };
        var speeds = new[]
        {
            new Speed("low", "zero air-drift baseline", LowInputMagnitude, 0),
            new Speed("running", "30-tick run-up, then full approach input", 1f, RunupTicks),
        };
        foreach (var entry in entries)
        foreach (var speed in speeds)
        {
            var timing = MeasureJump(entry, speed, approaches[0]);
            report.JumpTiming.Add(new TimingReference
            {
                Character = entry.Identity.PackageId,
                HorizontalSpeed = speed.Id,
                RunupTicks = speed.Runup,
                TakeoffTick = timing.TakeoffTick,
                ApexTick = timing.ApexTick,
                LandingTick = timing.LandingTick,
                RunupDistance = timing.RunupDistance,
                ApexOffset = timing.ApexTick - timing.TakeoffTick,
                AirtimeTicks = timing.LandingTick - timing.TakeoffTick,
                InputDescription = speed.Description,
            });
            foreach (var size in sizes)
            foreach (var motion in motions)
            foreach (var approach in approaches)
            foreach (float targetDistance in TargetDistanceGrid)
            foreach (var slot in CanonicalAerialSlots())
            for (int offset = 0; offset <= options.MaxOffset; offset++)
            {
                string phase = PhaseForOffset(offset, timing);
                foreach (var targetSize in new[] { size })
                {
                    var row = RunAerialCast(entry, slot, targetSize, motion, approach, speed, timing, targetDistance, offset, phase);
                    report.Casts.Add(row);
                }
            }
        }

        report.SuccessfulPressWindows.AddRange(BuildWindows(report.Casts));
        var bonk = entries.Single(x => x.Identity.PackageId.Contains("bonk", StringComparison.OrdinalIgnoreCase) || x.LegacySelector == CharacterClass.Bonk);
        if (!CanonicalSlotProjection.TryGet(false, "3", out var g3)) throw new InvalidDataException("canonical ground.3 projection is unavailable");
        var g3Cooked = RequireCookedSlot(bonk, g3);
        var authoredHitboxes = g3Cooked.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<CookedSpawnHitboxOperation>().ToArray();
        report.BonkGroundedCanonical3Authoring = new BonkAuthoringEvidence
        {
            ActualWireSlot = WireSlot(g3.InputLabel),
            ActualCookedSlot = g3Cooked.Id,
            OperationCount = authoredHitboxes.Length,
            HitboxIdentities = authoredHitboxes.Select(op => CookedIdentity(op.Tick, op.Hitbox)).ToList(),
        };
        foreach (var size in sizes)
        foreach (float x in GridX)
        foreach (float z in GridZ)
        {
            var cell = RunBonkGridCell(bonk, g3, g3Cooked, size, x, z);
            report.BonkGroundedCanonical3Grid.Add(cell);
        }
        report.Summary.CastCount = report.Casts.Count;
        report.Summary.AcceptedCastCount = report.Casts.Count(x => x.Accepted);
        report.Summary.RejectedCastCount = report.Casts.Count(x => !x.Accepted);
        report.Summary.LandedBeforePressCount = report.Casts.Count(x => x.LandedBeforePress);
        report.Summary.LandedWithoutHitCount = report.Casts.Count(x => x.LandedWithoutHit);
        report.Summary.HitCastCount = report.Casts.Count(x => x.FirstHitFrame.HasValue);
        report.Summary.HitRateAmongAcceptedCasts = report.Summary.AcceptedCastCount == 0 ? null : (float)report.Summary.HitCastCount / report.Summary.AcceptedCastCount;
        report.Summary.BonkGridCellCount = report.BonkGroundedCanonical3Grid.Count;
        report.Summary.BonkGridHitCellCount = report.BonkGroundedCanonical3Grid.Count(x => x.FirstHitFrame.HasValue);
        return report;
    }

    private static IEnumerable<SlotAddress> CanonicalAerialSlots()
    {
        for (int i = 1; i <= 4; i++)
            if (!CanonicalSlotProjection.TryGet(true, i.ToString(CultureInfo.InvariantCulture), out var slot))
                throw new InvalidDataException($"canonical air.{i} projection is unavailable");
            else yield return slot;
    }

    private static MatchContentEntry ResolveEntry(string selector)
        => selector switch
        {
            "fightguy" => BuiltInContentResolver.Resolve(CharacterClass.FightGuy),
            "manki" => BuiltInContentResolver.Resolve(CharacterClass.Manki),
            "wibou" => BuiltInContentResolver.Resolve(CharacterClass.Wibou),
            "bonk" => BuiltInContentResolver.Resolve(CharacterClass.Bonk),
            _ => throw new InvalidDataException($"unknown admitted selector '{selector}'"),
        };

    private static void RequireAdmittedCooked(MatchContentEntry entry)
    {
        if (entry.CookedCharacterPackage == null || entry.BakedAnimation == null)
            throw new InvalidDataException($"{entry.Identity.PackageId} is not an admitted cooked package with baked poses");
        foreach (var slot in CanonicalAerialSlots()) _ = RequireCookedSlot(entry, slot);
    }

    private static CookedSlotDefinition RequireCookedSlot(MatchContentEntry entry, SlotAddress slot)
    {
        byte wire = WireSlot(slot.InputLabel);
        var cooked = entry.Definition.GetCookedSlotAbility(wire, slot.IsAirborne);
        if (cooked == null || !string.Equals(cooked.Id, slot.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"{entry.Identity.PackageId}: cooked slot '{slot.Id}' is missing or mismatched");
        return cooked;
    }

    private static byte WireSlot(string label)
        => CanonicalSlotProjection.TryGet(false, label, out var address) && address.Ordinal < 4
            ? CanonicalSlotProjection.ToWire(address) : throw new ArgumentException($"normal label '{label}' is not canonical");

    private static Provenance ProvenanceOf(MatchContentEntry entry)
        => new()
        {
            Selector = entry.LegacySelector?.ToString().ToLowerInvariant() ?? entry.Identity.PackageId,
            DisplayName = entry.DisplayName,
            Identity = entry.Identity,
            HasCookedPackage = entry.CookedCharacterPackage != null,
            HasBakedAnimation = entry.BakedAnimation != null,
            CapsuleRadius = entry.Definition.CapsuleRadius,
            CapsuleHeight = entry.Definition.CapsuleHeight,
        };

    private static TargetSize[] SelectTargetSizes(MatchContentEntry[] entries)
    {
        var ordered = entries.OrderBy(x => x.Definition.CapsuleRadius * x.Definition.CapsuleHeight).ThenBy(x => x.Identity.PackageId, StringComparer.Ordinal).ToArray();
        return new[]
        {
            new TargetSize("small", ordered[0], "minimum admitted capsule radius × height"),
            new TargetSize("large", ordered[^1], "maximum admitted capsule radius × height"),
        };
    }

    private static JumpTiming MeasureJump(MatchContentEntry entry, Speed speed, Approach approach)
    {
        var arena = BuildArena();
        var sim = new ServerSimulation(arena);
        var def = entry.Definition;
        sim.RegisterEntity(AttackerId, def, InitialState(def, OriginX, OriginZ, true, MathF.Atan2(approach.X, approach.Z)), entry.BakedAnimation);
        var inputs = new Dictionary<ulong, InputState>();
        int takeoff = -1, apex = -1, landing = -1;
        float runupDistance = 0f;
        float highest = float.MinValue;
        for (int tick = 0; tick < MaxTicks; tick++)
        {
            inputs[AttackerId] = MovementInput(approach.X, approach.Z, speed.InputMagnitude, tick == speed.Runup, tick >= speed.Runup && tick < speed.Runup + 10);
            sim.Tick(inputs);
            var state = sim.GetState(AttackerId);
            if (takeoff < 0 && !state.IsGrounded)
            {
                takeoff = tick;
                float dx = state.PX - OriginX, dz = state.PZ - OriginZ;
                runupDistance = MathF.Sqrt(dx * dx + dz * dz);
            }
            if (takeoff >= 0 && state.PY > highest) { highest = state.PY; apex = tick; }
            if (takeoff >= 0 && landing < 0 && state.IsGrounded) { landing = tick; break; }
        }
        if (takeoff < 0 || apex < 0 || landing < 0) throw new InvalidDataException($"{entry.Identity.PackageId}: jump timing probe did not take off/apex/land");
        return new JumpTiming(takeoff, apex, landing, runupDistance);
    }

    private static CastRow RunAerialCast(MatchContentEntry attackerEntry, SlotAddress slot, TargetSize targetSize,
        Motion motion, Approach approach, Speed speed, JumpTiming timing, float targetDistance, int offset, string phase)
    {
        var arena = BuildArena();
        var attackerDef = attackerEntry.Definition;
        var targetEntry = targetSize.Entry;
        var targetDef = targetEntry.Definition;
        byte wire = WireSlot(slot.InputLabel);
        int pressTick = timing.TakeoffTick + offset + 1;
        var row = new CastRow
        {
            Character = attackerEntry.Identity.PackageId,
            Slot = slot.Id,
            ActualCookedSlot = RequireCookedSlot(attackerEntry, slot).Id,
            TargetCharacter = targetEntry.Identity.PackageId,
            TargetSize = targetSize.Id,
            TargetDistance = targetDistance,
            TargetMode = motion.Id,
            Approach = approach.Id,
            HorizontalSpeed = speed.Id,
            AttackerSourceHash = attackerEntry.Identity.SourceHash,
            AttackerCookedContentHash = attackerEntry.Identity.CookedContentHash,
            AttackerPackageHash = attackerEntry.Identity.PackageHash,
            TargetSourceHash = targetEntry.Identity.SourceHash,
            TargetCookedContentHash = targetEntry.Identity.CookedContentHash,
            TargetPackageHash = targetEntry.Identity.PackageHash,
            PressOffset = offset,
            PressTick = pressTick,
            PressPhase = phase,
        };
        var sim = new ServerSimulation(arena);
        float targetX = OriginX + approach.X * (targetDistance + timing.RunupDistance);
        float targetZ = OriginZ + approach.Z * (targetDistance + timing.RunupDistance);
        sim.RegisterEntity(AttackerId, attackerDef, InitialState(attackerDef, OriginX, OriginZ, true, MathF.Atan2(approach.X, approach.Z)), attackerEntry.BakedAnimation);
        sim.RegisterEntity(TargetId, targetDef, InitialState(targetDef, targetX, targetZ, true, MathF.Atan2(-approach.X, -approach.Z)), targetEntry.BakedAnimation);
        var capture = new RunCapture { Row = row };
        int currentTick = -1;
        sim.Resolver.OnHitboxRemoved = (hb, _, _, _) => capture.Removed.Add(new RemovedHitbox { Tick = currentTick, Hitbox = hb });
        var inputs = new Dictionary<ulong, InputState>();
        ulong activation = 0;
        bool hadLanding = false;
        for (int tick = 0; tick < MaxTicks; tick++)
        {
            currentTick = tick;
            var beforeA = sim.GetState(AttackerId);
            var beforeT = sim.GetState(TargetId);
            if (tick == pressTick)
            {
                row.AttackerAtPress = Snapshot(beforeA, attackerDef);
                row.TargetAtPress = Snapshot(beforeT, targetDef);
                row.ActualHorizontalSpeedAtPress = HorizontalSpeed(beforeA);
                row.ObservedPressPhase = ObservedPhase(beforeA, offset, timing);
            }
            var attackerInput = tick < speed.Runup ? MovementInput(approach.X, approach.Z, 1f, false, false) : MovementInput(approach.X, approach.Z, speed.InputMagnitude, tick == speed.Runup, tick >= speed.Runup && tick < speed.Runup + 10);
            if (speed.Runup == 0 && tick == 0) attackerInput = MovementInput(approach.X, approach.Z, speed.InputMagnitude, true, true);
            if (tick == pressTick) attackerInput.ActiveSlot = wire;
            var targetInput = motion.Moving && tick >= timing.TakeoffTick + 1 ? MovementInput(-approach.Z, approach.X, 1f, false, false) : default;
            inputs[AttackerId] = attackerInput;
            inputs[TargetId] = targetInput;
            sim.Tick(inputs);
            var afterA = sim.GetState(AttackerId);
            var afterT = sim.GetState(TargetId);
            if (tick == pressTick)
            {
                activation = sim.GetLastActivationId(AttackerId);
                var activeAbility = sim.GetActiveAbility(AttackerId);
                row.Accepted = activeAbility != null && afterA.AttackSlot == wire && activeAbility.AirborneAtStart;
                if (!row.Accepted)
                {
                    row.LandedBeforePress = beforeA.IsGrounded || afterA.IsGrounded;
                    row.RejectionReason = row.LandedBeforePress ? "landed-before-press" : "activation-rejected";
                }
            }
            if (row.Accepted && !hadLanding && afterA.IsGrounded && tick >= pressTick)
            {
                hadLanding = true;
                if (!row.FirstHitFrame.HasValue) row.LandedWithoutHit = true;
            }
            if (!row.Accepted) continue;
            var actualBoxes = ContactCandidates(sim, capture.Removed, tick, activation);
            if (actualBoxes.Count > 0)
            {
                row.ActiveFrameCount++;
                if (!row.FirstActiveFrame.HasValue)
                {
                    row.FirstActiveFrame = tick;
                    var press = row.AttackerAtPress!;
                    row.PressToFirstActiveDisplacement = new Vector3Data { X = afterA.PX - WorldX(press.PositionX), Y = afterA.PY - (press.PositionY + attackerDef.CapsuleHeight * 0.5f), Z = afterA.PZ - WorldZ(press.PositionZ) };
                }
                row.ActiveFrames.Add(new ActiveFrameObservation
                {
                    Tick = tick,
                    TicksAfterPress = tick - pressTick,
                    AttackElapsedTicks = afterA.AttackElapsedTicks,
                    Attacker = Snapshot(afterA, attackerDef),
                    Target = Snapshot(afterT, targetDef),
                    RelativePosition = Relative(afterT, afterA),
                    RelativeVelocity = RelativeVelocity(afterT, afterA),
                    HitboxIdentities = actualBoxes.Select(Identity).Distinct(StringComparer.Ordinal).ToArray(),
                });
            }
            foreach (var hit in sim.LastTickHits.Where(x => x.OwnerEntityId == AttackerId && x.TargetEntityId == TargetId && x.Damage > 0f))
            {
                row.FirstHitFrame ??= tick;
                row.FirstHitTicksAfterPress ??= tick - pressTick;
                string identity = ResolveContactIdentity(hit, actualBoxes);
                string sequence = capture.ContactOccurrences.TryGetValue(identity, out int count) && count > 0 ? "later" : "first";
                capture.ContactOccurrences[identity] = capture.ContactOccurrences.TryGetValue(identity, out count) ? count + 1 : 1;
                row.Contacts.Add(new ContactObservation
                {
                    Frame = tick,
                    TicksAfterPress = tick - pressTick,
                    ContactIdentity = identity,
                    PartClassification = identity == "resolver-slot-only" ? "insufficient-geometry-provenance" : "actual SourceEvent geometry match",
                    Sequence = sequence,
                    Damage = hit.Damage,
                    ContactPoint = new Vector3Data { X = hit.HitX, Y = hit.HitY, Z = hit.HitZ },
                    Attacker = Snapshot(afterA, attackerDef),
                    Target = Snapshot(afterT, targetDef),
                });
            }
        }
        return row;
    }

    private static BonkGridCell RunBonkGridCell(MatchContentEntry bonk, SlotAddress slot, CookedSlotDefinition cooked,
        TargetSize targetSize, float x, float z)
    {
        var arena = BuildArena();
        var target = targetSize.Entry;
        var sim = new ServerSimulation(arena);
        var bonkDef = bonk.Definition;
        sim.RegisterEntity(AttackerId, bonkDef, InitialState(bonkDef, OriginX, OriginZ, true, 0f), bonk.BakedAnimation);
        sim.RegisterEntity(TargetId, target.Definition, InitialState(target.Definition, OriginX + x, OriginZ + z, true, MathF.PI), target.BakedAnimation);
        var cell = new BonkGridCell { ActualWireSlot = WireSlot(slot.InputLabel), ActualCookedSlot = cooked.Id, CharacterPackageHash = bonk.Identity.PackageHash, CharacterCookedContentHash = bonk.Identity.CookedContentHash, TargetCharacter = target.Identity.PackageId, TargetSize = targetSize.Id, TargetPackageHash = target.Identity.PackageHash, TargetCookedContentHash = target.Identity.CookedContentHash, RelativeX = x, RelativeZ = z };
        var capture = new RunCapture();
        int currentTick = -1;
        sim.Resolver.OnHitboxRemoved = (hb, _, _, _) => capture.Removed.Add(new RemovedHitbox { Tick = currentTick, Hitbox = hb });
        var inputs = new Dictionary<ulong, InputState>();
        ulong activation = 0;
        bool accepted = false;
        for (int tick = 0; tick < 100; tick++)
        {
            currentTick = tick;
            inputs[AttackerId] = tick == 0 ? new InputState { ActiveSlot = WireSlot(slot.InputLabel) } : default;
            inputs[TargetId] = default;
            sim.Tick(inputs);
            var afterA = sim.GetState(AttackerId);
            var afterT = sim.GetState(TargetId);
            if (tick == 0)
            {
                activation = sim.GetLastActivationId(AttackerId);
                accepted = sim.GetActiveAbility(AttackerId) != null && afterA.AttackSlot == WireSlot(slot.InputLabel) && afterA.IsGrounded;
            }
            var boxes = ContactCandidates(sim, capture.Removed, tick, activation);
            foreach (var hit in sim.LastTickHits.Where(h => h.OwnerEntityId == AttackerId && h.TargetEntityId == TargetId && h.Damage > 0f))
            {
                cell.FirstHitFrame ??= tick;
                string identity = ResolveContactIdentity(hit, boxes);
                string sequence = capture.ContactOccurrences.TryGetValue(identity, out int count) && count > 0 ? "later" : "first";
                capture.ContactOccurrences[identity] = capture.ContactOccurrences.TryGetValue(identity, out count) ? count + 1 : 1;
                cell.Contacts.Add(new ContactObservation
                {
                    Frame = tick,
                    TicksAfterPress = tick,
                    ContactIdentity = identity,
                    PartClassification = identity == "resolver-slot-only" ? "insufficient-geometry-provenance" : "actual SourceEvent geometry match",
                    Sequence = sequence,
                    Damage = hit.Damage,
                    ContactPoint = new Vector3Data { X = hit.HitX, Y = hit.HitY, Z = hit.HitZ },
                    Attacker = Snapshot(afterA, bonkDef),
                    Target = Snapshot(afterT, target.Definition),
                });
            }
            if (accepted && cell.Contacts.Count > 0 && cell.Contacts[^1].Frame > 0 && sim.GetActiveAbility(AttackerId) == null) break;
        }
        cell.Accepted = accepted;
        cell.RejectionReason = accepted ? null : "activation-rejected";
        return cell;
    }

    private static List<Hitbox> ContactCandidates(ServerSimulation sim, List<RemovedHitbox> removed, int tick, ulong activation)
    {
        var result = sim.Resolver.GetActiveHitboxes().Where(x => x.OwnerId == AttackerId && x.ActivationId == activation).ToList();
        result.AddRange(removed.Where(x => x.Tick == tick && x.Hitbox.OwnerId == AttackerId && x.Hitbox.ActivationId == activation).Select(x => x.Hitbox));
        return result;
    }

    private static string ResolveContactIdentity(SpellResolver.HitResult hit, List<Hitbox> candidates)
    {
        if (candidates.Count == 0) return "resolver-slot-only";
        Hitbox? best = null;
        float bestDistance = float.MaxValue;
        foreach (var box in candidates)
        {
            float distance = PointSegmentDistance(hit.HitX, hit.HitY, hit.HitZ, box.X, box.Y, box.Z, box.EndX, box.EndY, box.EndZ);
            if (distance < bestDistance) { bestDistance = distance; best = box; }
        }
        return best.HasValue ? Identity(best.Value) : "resolver-slot-only";
    }

    private static string CookedIdentity(ushort tick, CookedHitbox box)
    {
        string group = box.HitGroup == 0 ? "hit-group:none" : $"hit-group:{box.HitGroup}";
        string start = string.IsNullOrEmpty(box.StartBoneId) ? "offset" : box.StartBoneId;
        string end = string.IsNullOrEmpty(box.EndBoneId) ? "point" : box.EndBoneId;
        return $"{group}|{start}->{end}|trigger:{tick}|radius:{box.Radius.ToString("0.###", CultureInfo.InvariantCulture)}";
    }

    private static string Identity(Hitbox box)
    {
        var evt = box.SourceEvent;
        string group = evt.HitGroup == 0 ? "hit-group:none" : $"hit-group:{evt.HitGroup}";
        string start = string.IsNullOrEmpty(evt.BoneName) ? "offset" : evt.BoneName;
        string end = string.IsNullOrEmpty(evt.EndBoneName) ? "point" : evt.EndBoneName;
        return $"{group}|{start}->{end}|trigger:{evt.TriggerTick}|radius:{evt.Radius.ToString("0.###", CultureInfo.InvariantCulture)}";
    }

    private static float PointSegmentDistance(float px, float py, float pz, float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az;
        float lengthSq = dx * dx + dy * dy + dz * dz;
        float t = lengthSq > 0.000001f ? ((px - ax) * dx + (py - ay) * dy + (pz - az) * dz) / lengthSq : 0f;
        t = Math.Clamp(t, 0f, 1f);
        float ex = ax + t * dx - px, ey = ay + t * dy - py, ez = az + t * dz - pz;
        return MathF.Sqrt(ex * ex + ey * ey + ez * ez);
    }

    private static PressWindow[] BuildWindows(IEnumerable<CastRow> casts)
    {
        var windows = new List<PressWindow>();
        foreach (var group in casts.Where(x => x.Accepted && x.FirstHitFrame.HasValue).GroupBy(Key))
        {
            var ordered = group.OrderBy(x => x.PressOffset).ToArray();
            for (int i = 0; i < ordered.Length;)
            {
                int start = ordered[i].PressOffset, end = start, count = 1, j = i + 1;
                while (j < ordered.Length && ordered[j].PressOffset == end + 1) { end = ordered[j].PressOffset; count++; j++; }
                windows.Add(new PressWindow
                {
                    Character = group.Key.Character,
                    Slot = group.Key.Slot,
                    TargetCharacter = group.Key.TargetCharacter,
                    TargetSize = group.Key.TargetSize,
                    TargetDistance = group.Key.TargetDistance,
                    TargetMode = group.Key.TargetMode,
                    Approach = group.Key.Approach,
                    HorizontalSpeed = group.Key.HorizontalSpeed,
                    PressPhase = group.Key.PressPhase,
                    AttackerPackageHash = ordered[0].AttackerPackageHash,
                    AttackerCookedContentHash = ordered[0].AttackerCookedContentHash,
                    TargetPackageHash = ordered[0].TargetPackageHash,
                    TargetCookedContentHash = ordered[0].TargetCookedContentHash,
                    StartPressOffset = start,
                    EndPressOffset = end,
                    SuccessfulPressTicks = count,
                    BoundaryClipped = start == 0 || end == group.Max(x => x.PressOffset),
                });
                i = j;
            }
        }
        return windows.OrderBy(x => x.Character).ThenBy(x => x.Slot).ThenBy(x => x.TargetCharacter).ThenBy(x => x.TargetSize).ThenBy(x => x.TargetDistance).ThenBy(x => x.TargetMode).ThenBy(x => x.Approach).ThenBy(x => x.HorizontalSpeed).ThenBy(x => x.StartPressOffset).ToArray();
    }

    private static (string Character, string Slot, string TargetCharacter, string TargetSize, float TargetDistance, string TargetMode, string Approach, string HorizontalSpeed, string PressPhase) Key(CastRow x)
        => (x.Character, x.Slot, x.TargetCharacter, x.TargetSize, x.TargetDistance, x.TargetMode, x.Approach, x.HorizontalSpeed, x.PressPhase);

    private static string PhaseForOffset(int offset, JumpTiming timing)
    {
        int apexOffset = timing.ApexTick - timing.TakeoffTick;
        return offset < apexOffset ? "rising" : offset == apexOffset ? "apex" : "falling";
    }

    private static string ObservedPhase(CharacterState state, int offset, JumpTiming timing)
        => state.IsGrounded ? "grounded" : state.VY > 0.05f ? "rising" : state.VY < -0.05f ? "falling" : "apex-band";

    private static InputState MovementInput(float x, float z, float magnitude, bool jump, bool jumpHeld)
        => new() { MoveX = x * magnitude, MoveY = z * magnitude, Jump = jump, JumpHeld = jumpHeld };

    private static CharacterState InitialState(CharacterDefinition def, float x, float z, bool grounded, float facing, float vx = 0f, float vz = 0f)
        => new()
        {
            PX = x, PY = def.CapsuleHeight * 0.5f, PZ = z, VX = vx, VZ = vz,
            IsGrounded = grounded, State = ActionState.Idle, JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1, AirTimeTicks = grounded ? (ushort)0 : def.Movement.FloatWindowTicks,
            FacingYaw = facing, AimYaw = facing,
        };

    private static StateSnapshot Snapshot(CharacterState state, CharacterDefinition def)
        => new()
        {
            PositionX = state.PX - OriginX, PositionY = state.PY - def.CapsuleHeight * 0.5f, PositionZ = state.PZ - OriginZ,
            VelocityX = state.VX, VelocityY = state.VY, VelocityZ = state.VZ, IsGrounded = state.IsGrounded,
            State = state.State.ToString(), FacingYawDegrees = state.FacingYaw * 180f / MathF.PI,
        };

    private static Vector3Data Relative(CharacterState target, CharacterState attacker)
        => new() { X = target.PX - attacker.PX, Y = target.PY - attacker.PY, Z = target.PZ - attacker.PZ };

    private static Vector3Data RelativeVelocity(CharacterState target, CharacterState attacker)
        => new() { X = target.VX - attacker.VX, Y = target.VY - attacker.VY, Z = target.VZ - attacker.VZ };

    private static float HorizontalSpeed(CharacterState state) => MathF.Sqrt(state.VX * state.VX + state.VZ * state.VZ);
    private static float WorldX(float snapshotRelativeX) => snapshotRelativeX + OriginX;
    private static float WorldZ(float snapshotRelativeZ) => snapshotRelativeZ + OriginZ;

    private static ArenaDefinition BuildArena()
        => new()
        {
            Name = "aerial-approach-flat",
            KillHeight = -1000f,
            SpawnPoints = new[] { new SpawnPoint() },
            Heightmap = new ArenaHeightmap { Data = new float[200 * 200], Width = 200, Height = 200, CellSize = 1f },
        };

    private static void SelfCheck(Report report)
    {
        if (report.Casts.Count == 0) throw new InvalidDataException("self-check: no aerial casts");
        foreach (var cast in report.Casts)
        {
            if (!cast.Accepted && cast.FirstHitFrame.HasValue) throw new InvalidDataException("self-check: rejected cast has a hit");
            if (cast.FirstHitFrame.HasValue && !cast.Accepted) throw new InvalidDataException("self-check: hit is not accepted-only");
            if (cast.FirstHitFrame.HasValue && !cast.FirstHitTicksAfterPress.HasValue) throw new InvalidDataException("self-check: hit lacks press-relative frame");
            if (cast.FirstActiveFrame.HasValue && !cast.Accepted) throw new InvalidDataException("self-check: active frame is not an accepted cast");
            if (cast.LandedBeforePress && cast.Accepted) throw new InvalidDataException("self-check: accepted cast landed before press");
            if (cast.Contacts.Count > 0 && !cast.FirstHitFrame.HasValue) throw new InvalidDataException("self-check: contact lacks first-hit frame");
        }
        foreach (var window in report.SuccessfulPressWindows)
            if (window.StartPressOffset > window.EndPressOffset || window.SuccessfulPressTicks != window.EndPressOffset - window.StartPressOffset + 1)
                throw new InvalidDataException("self-check: invalid contiguous window");
        if (report.Summary.HitRateAmongAcceptedCasts.HasValue && report.Summary.HitRateAmongAcceptedCasts.Value > 1f)
            throw new InvalidDataException("self-check: accepted-only hit rate exceeds one");
        Console.WriteLine("self-check: accepted-cast/hit correlation and contiguous windows passed");
    }

    private static void WriteOutputs(Report report, string directory)
    {
        Directory.CreateDirectory(directory);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, jsonOptions));
        File.WriteAllText(Path.Combine(directory, "casts.csv"), CastCsv(report.Casts));
        File.WriteAllText(Path.Combine(directory, "windows.csv"), WindowCsv(report.SuccessfulPressWindows));
        File.WriteAllText(Path.Combine(directory, "bonk-g3-grid.csv"), GridCsv(report.BonkGroundedCanonical3Grid));
    }

    private static string CastCsv(IEnumerable<CastRow> rows)
    {
        var sb = new StringBuilder("character,slot,actualCookedSlot,targetCharacter,targetSize,targetDistance,targetMode,approach,horizontalSpeed,attackerSourceHash,attackerCookedContentHash,attackerPackageHash,targetSourceHash,targetCookedContentHash,targetPackageHash,pressOffset,pressTick,pressPhase,observedPressPhase,accepted,rejectionReason,landedBeforePress,landedWithoutHit,firstActiveFrame,firstHitFrame,firstHitTicksAfterPress,activeFrameCount,actualHorizontalSpeedAtPress,contactCount\n");
        foreach (var x in rows) sb.AppendLine(string.Join(',', Fields(x.Character, x.Slot, x.ActualCookedSlot, x.TargetCharacter, x.TargetSize, x.TargetDistance.ToString("0.###", CultureInfo.InvariantCulture), x.TargetMode, x.Approach, x.HorizontalSpeed, x.AttackerSourceHash, x.AttackerCookedContentHash, x.AttackerPackageHash, x.TargetSourceHash, x.TargetCookedContentHash, x.TargetPackageHash, x.PressOffset, x.PressTick, x.PressPhase, x.ObservedPressPhase, x.Accepted, x.RejectionReason, x.LandedBeforePress, x.LandedWithoutHit, x.FirstActiveFrame, x.FirstHitFrame, x.FirstHitTicksAfterPress, x.ActiveFrameCount, x.ActualHorizontalSpeedAtPress.ToString("0.###", CultureInfo.InvariantCulture), x.Contacts.Count)));
        return sb.ToString();
    }

    private static string WindowCsv(IEnumerable<PressWindow> rows)
    {
        var sb = new StringBuilder("character,slot,targetCharacter,targetSize,targetDistance,targetMode,approach,horizontalSpeed,pressPhase,attackerPackageHash,attackerCookedContentHash,targetPackageHash,targetCookedContentHash,startPressOffset,endPressOffset,successfulPressTicks,boundaryClipped\n");
        foreach (var x in rows) sb.AppendLine(string.Join(',', Fields(x.Character, x.Slot, x.TargetCharacter, x.TargetSize, x.TargetDistance.ToString("0.###", CultureInfo.InvariantCulture), x.TargetMode, x.Approach, x.HorizontalSpeed, x.PressPhase, x.AttackerPackageHash, x.AttackerCookedContentHash, x.TargetPackageHash, x.TargetCookedContentHash, x.StartPressOffset, x.EndPressOffset, x.SuccessfulPressTicks, x.BoundaryClipped)));
        return sb.ToString();
    }

    private static string GridCsv(IEnumerable<BonkGridCell> rows)
    {
        var sb = new StringBuilder("character,slot,actualWireSlot,actualCookedSlot,characterPackageHash,characterCookedContentHash,targetCharacter,targetSize,targetPackageHash,targetCookedContentHash,relativeX,relativeZ,accepted,rejectionReason,firstHitFrame,contactCount,contactIdentities,contactSequences\n");
        foreach (var x in rows) sb.AppendLine(string.Join(',', Fields(x.Character, x.Slot, x.ActualWireSlot, x.ActualCookedSlot, x.CharacterPackageHash, x.CharacterCookedContentHash, x.TargetCharacter, x.TargetSize, x.TargetPackageHash, x.TargetCookedContentHash, x.RelativeX.ToString("0.###", CultureInfo.InvariantCulture), x.RelativeZ.ToString("0.###", CultureInfo.InvariantCulture), x.Accepted, x.RejectionReason, x.FirstHitFrame, x.Contacts.Count, string.Join(';', x.Contacts.Select(c => c.ContactIdentity)), string.Join(';', x.Contacts.Select(c => c.Sequence)))));
        return sb.ToString();
    }

    private static IEnumerable<string> Fields(params object?[] values) => values.Select(x => Csv(x?.ToString() ?? ""));
    private static string Csv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\n') ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : value;
    private static string Format(float? value) => value.HasValue ? value.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a";
}
