#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared;

namespace SlopArena.AerialApproachReport;

internal static partial class Program
{
    private const int SlideFlowMaxTicks = 600;
    private const int SlideFlowMaxOffset = 72;

    private sealed class SlideFlowResult
    {
        public string Tool { get; init; } = "AerialApproachReport";
        public int SchemaVersion { get; init; } = 1;
        public string GeneratedAtUtc { get; init; } = "";
        public string EvidenceClass { get; init; } = "finite measured scenarios; not balance proof";
        public SlideFlowParameters Parameters { get; init; } = new();
        public List<SlideFlowProvenance> AdmittedRoster { get; } = new();
        public SlideFlowSummary Summary { get; init; } = new();
        public object Followups { get; set; } = new Dictionary<string, object>();
        public List<string> Limitations { get; } = new();
    }

    private sealed class SlideFlowParameters
    {
        public string[] Characters { get; init; } = Array.Empty<string>();
        public string[] AerialNormals { get; init; } = new[] { "air.1", "air.3", "air.4" };
        public string[] Controls { get; init; } = new[] { "no-air" };
        public string[] HopModes { get; init; } = new[] { "full-hop", "short-hop" };
        public string[] FastFallModes { get; init; } = new[] { "first-descending", "descending-plus-six", "none" };
        public string[] Supports { get; init; } = new[] { "floor-0", "platform-2" };
        public string[] DownLandingModes { get; init; } = new[] { "held-through-landing", "released-before-landing" };
        public string[] Directions { get; init; } = new[] { "+X", "+Z", "normalized-diagonal", "opposite-facing", "platform-edge" };
        public int MaxOffset { get; init; } = SlideFlowMaxOffset;
        public int SlideDeceleration { get; init; } = 2;
        public string FallProfile { get; init; } = "current";
        public float BraceMultiplier { get; init; } = 0.9f;
        public int MaxTicks { get; init; } = SlideFlowMaxTicks;
        public string OffsetBasis { get; init; } = "offset 0 is the first simulation tick after the measured real takeoff; no-air controls use one offset only";
        public string TargetBasis { get; init; } = "declared support and direction targets are selected before simulation; endpoints are never used to select targets";
        public string DenominatorBasis { get; init; } = "every emitted scenario is a measured real ServerSimulation run; accepted actions, landings, misses, and 600-tick timeouts remain separate denominators";
    }

    private sealed class SlideFlowProvenance
    {
        public string Selector { get; init; } = "";
        public string PackageId { get; init; } = "";
        public MatchContentIdentity OriginalIdentity { get; init; } = null!;
        public bool ExperimentalFallOverride { get; init; }
        public float OriginalMaxFallSpeed { get; init; }
        public float OriginalFastFallSpeed { get; init; }
        public float EffectiveMaxFallSpeed { get; init; }
        public float EffectiveFastFallSpeed { get; init; }
        public string EffectiveValues { get; init; } = "";
    }

    private sealed class SlideFlowSummary
    {
        public int ScenarioCount { get; set; }
        public int ExpectedScenarioCount { get; init; } = 4 * 2 * 5 * 2 * 2 * 3 * (4 * (SlideFlowMaxOffset + 1) + 1);
        public int TimeoutCount { get; set; }
        public int LandingCount { get; set; }
        public int AcceptedActionCount { get; set; }
        public int TouchdownCount { get; set; }
        public int SlideTickCount { get; set; }
        public int CrouchTickCount { get; set; }
        public int FastFallActivationCount { get; set; }
        public int NoAirControlCount { get; set; }
        public int AerialControlCount { get; set; }
        public int AerialRequestCount { get; set; }
        public int AerialAcceptedCount { get; set; }
        public int RecoveryControlScenarioCount { get; set; }
        public int RecoveryControlSuppressedFastFallCount { get; set; }
        public Dictionary<string, int> RecoveryAdmissionReasons { get; } = new(StringComparer.Ordinal);
        public int RecoveryOwnedNegativeControlCount { get; set; }
        public Dictionary<string, int> AdmissionReasons { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ScenarioCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, float> FirstActionLatencyTicks { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FirstActionLatencySamples { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> SlideWindows { get; } = new(StringComparer.Ordinal);
        public string[] Invariants { get; init; } = new[]
        {
            "scenario tick count never exceeds 600",
            "touchdown is read from ServerSimulation.LastTickTouchdowns",
            "action acceptance is read from ServerSimulation.LastTickAcceptedActions",
            "Down admission reasons are read from ServerSimulation.LastTickDownAdmissions",
            "timeout is reported when no touchdown occurs by tick 599",
            "this finite sweep is measured evidence, not a balance claim",
        };
    }

    private sealed record SlideDirection(string Id, float X, float Z, bool Edge);

    private sealed class SlideRunMetrics
    {
        public int TickCount;
        public int? TakeoffTick;
        public int? FirstDescendingTick;
        public int? LandingTick;
        public int? FirstActionTick;
        public int? FirstGroundActionOpportunityTick;
        public int SlideTicks;
        public int CrouchTicks;
        public int FastFallActivations;
        public int AcceptedActions;
        public int AerialRequests;
        public int AerialAccepted;
        public int Touchdowns;
        public Dictionary<string, int> AdmissionReasons = new(StringComparer.Ordinal);
        public bool Timeout;
        public bool RecoveryOwnedNegativeControl;
    }

    private static SlideFlowResult BuildSlideFlow(Options options)
    {
        if (options.MaxOffset != DefaultMaxOffset && options.MaxOffset != SlideFlowMaxOffset)
            throw new ArgumentException("--slide-flow requires the full offset sweep 0 through 72");

        var entries = Characters.Select(ResolveEntry).ToArray();
        foreach (var entry in entries) RequireAdmittedCooked(entry);
        var tuning = new DownActionTuning(options.SlideDeceleration, options.BraceMultiplier);
        Directory.CreateDirectory(options.OutDirectory);
        var result = new SlideFlowResult
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Parameters = new SlideFlowParameters { Characters = Characters, SlideDeceleration = options.SlideDeceleration, FallProfile = options.FallProfile, BraceMultiplier = options.BraceMultiplier },
            Summary = new SlideFlowSummary(),
        };
        result.Limitations.AddRange(new[]
        {
            "The report is a finite deterministic sweep, not balance proof or a player hit-probability estimate.",
            "Every scenario uses the admitted immutable package and a real authoritative ServerSimulation.",
            "Aerial offsets are measured from each scenario's actual takeoff transition; no endpoint is used to choose a target.",
            "No-air controls are emitted once per declared control combination, not duplicated for 73 meaningless attack offsets.",
            "A recovery-owned action is a negative control; it must not be relabeled as a movement failure.",
            "Previous-fall runs use a fresh runtime adapter definition only; package identities and catalog artifacts are not mutated.",
        });
        var effectiveDefinitions = entries.ToDictionary(x => x.Identity.PackageId, x => SlideDefinition(x, options.FallProfile), StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var movement = entry.Definition.Movement;
            var effective = effectiveDefinitions[entry.Identity.PackageId];
            result.AdmittedRoster.Add(new SlideFlowProvenance
            {
                Selector = entry.LegacySelector?.ToString().ToLowerInvariant() ?? entry.Identity.PackageId,
                PackageId = entry.Identity.PackageId,
                OriginalIdentity = entry.Identity,
                ExperimentalFallOverride = options.FallProfile == "previous",
                OriginalMaxFallSpeed = movement.MaxFallSpeed,
                OriginalFastFallSpeed = movement.FastFallSpeed,
                EffectiveMaxFallSpeed = effective.Movement.MaxFallSpeed,
                EffectiveFastFallSpeed = effective.Movement.FastFallSpeed,
                EffectiveValues = $"maxFallSpeed={effective.Movement.MaxFallSpeed.ToString("0.###", CultureInfo.InvariantCulture)};fastFallSpeed={effective.Movement.FastFallSpeed.ToString("0.###", CultureInfo.InvariantCulture)}",
            });
        }
        var flowArenas = new Dictionary<(float Support, bool Edge), ArenaDefinition>
        {
            [(0f, false)] = SlideArena(0f, false),
            [(0f, true)] = SlideArena(0f, true),
            [(2f, false)] = SlideArena(2f, false),
            [(2f, true)] = SlideArena(2f, true),
        };

        using (var traces = new StreamWriter(Path.Combine(options.OutDirectory, "slide-traces.csv"), false, new System.Text.UTF8Encoding(false)))
        using (var approaches = new StreamWriter(Path.Combine(options.OutDirectory, "aerial-slide-approaches.csv"), false, new System.Text.UTF8Encoding(false)))
        {
            WriteSlideTraceHeader(traces);
            WriteSlideApproachHeader(approaches);
            var directions = new[]
            {
                new SlideDirection("+X", 1f, 0f, false),
                new SlideDirection("+Z", 0f, 1f, false),
                new SlideDirection("normalized-diagonal", 1f / MathF.Sqrt(2f), 1f / MathF.Sqrt(2f), false),
                new SlideDirection("opposite-facing", 0f, -1f, false),
                new SlideDirection("platform-edge", 0f, 1f, true),
            };
            var slots = CanonicalAerialSlots().ToArray();
            foreach (var entry in entries)
            foreach (float supportHeight in new[] { 0f, 2f })
            foreach (var direction in directions)
            foreach (bool shortHop in new[] { false, true })
            foreach (bool releasedBeforeLanding in new[] { false, true })
            foreach (string fastFallMode in new[] { "first-descending", "descending-plus-six", "none" })
            {
                var arena = flowArenas[(supportHeight, direction.Edge)];
                foreach (var slot in slots)
                {
                    for (int offset = 0; offset <= SlideFlowMaxOffset; offset++)
                    {
                        string scenario = ScenarioId(entry, slot.Id, supportHeight, direction.Id, shortHop, releasedBeforeLanding, fastFallMode, offset);
                        var metrics = RunSlideScenario(traces, approaches, scenario, entry, effectiveDefinitions[entry.Identity.PackageId], arena, tuning, slot, offset, supportHeight, direction, shortHop, releasedBeforeLanding, fastFallMode, options.Assert);
                        AddMetrics(result.Summary, metrics, entry.Identity.PackageId, "aerial", slot.Id, supportHeight, direction.Id, fastFallMode);
                    }
                }
                string noAirScenario = ScenarioId(entry, "no-air", supportHeight, direction.Id, shortHop, releasedBeforeLanding, fastFallMode, 0);
                var noAir = RunSlideScenario(traces, approaches, noAirScenario, entry, effectiveDefinitions[entry.Identity.PackageId], arena, tuning, null, 0, supportHeight, direction, shortHop, releasedBeforeLanding, fastFallMode, options.Assert);
                AddMetrics(result.Summary, noAir, entry.Identity.PackageId, "no-air", "no-air", supportHeight, direction.Id, fastFallMode);
            }
            foreach (var entry in entries)
            {
                if (!TryGetRecoveryAirSlot(entry, out var recoverySlot, out byte recoveryWire))
                    continue;
                result.Summary.RecoveryControlScenarioCount++;
                bool suppressed = RunRecoveryNegativeControl(traces, entry, effectiveDefinitions[entry.Identity.PackageId], flowArenas[(0f, false)], tuning, recoverySlot, recoveryWire, result.Summary.RecoveryAdmissionReasons, options.Assert);
                if (suppressed) result.Summary.RecoveryControlSuppressedFastFallCount++;
                result.Summary.RecoveryOwnedNegativeControlCount++;
            }
            if (result.Summary.RecoveryControlScenarioCount == 0)
                result.Limitations.Add("No admitted air recovery slot was present; recovery-owned negative control is unavailable for this roster.");
            if (options.Assert && result.Summary.ScenarioCount != result.Summary.ExpectedScenarioCount)
                throw new InvalidDataException($"slide-flow assert: scenario count {result.Summary.ScenarioCount} != expected {result.Summary.ExpectedScenarioCount}");
            result.Followups = WriteSlideFollowupsAndCombat(options.OutDirectory, traces, entries, tuning, options.FallProfile, options.Assert);
        }
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        File.WriteAllText(Path.Combine(options.OutDirectory, "slide-flow.json"), JsonSerializer.Serialize(result, jsonOptions));
        return result;
    }

    private static string ScenarioId(MatchContentEntry entry, string slot, float supportHeight, string direction, bool shortHop, bool released, string ff, int offset)
        => string.Join('|', entry.Identity.PackageId, slot, supportHeight.ToString("0", CultureInfo.InvariantCulture), direction, shortHop ? "short" : "full", released ? "released" : "held", ff, offset.ToString(CultureInfo.InvariantCulture));

    private static SlideRunMetrics RunSlideScenario(StreamWriter traces, StreamWriter approaches, string scenario,
        MatchContentEntry entry, CharacterDefinition effective, ArenaDefinition arena, DownActionTuning tuning, SlotAddress? slot,
        int offset, float supportHeight, SlideDirection direction, bool shortHop, bool releasedBeforeLanding,
        string fastFallMode, bool assert)
    {
        var metrics = new SlideRunMetrics();
        var sim = new ServerSimulation(arena, downActionTuning: tuning);
        float facing = direction.Id == "opposite-facing" ? MathF.PI : MathF.Atan2(direction.X, direction.Z);
        var initial = InitialState(effective, 0f, 0f, true, facing);
        initial.PY = supportHeight + effective.CapsuleHeight * 0.5f;
        sim.RegisterEntity(AttackerId, effective, initial, entry.BakedAnimation);
        byte wire = slot.HasValue ? WireSlot(slot.Value.InputLabel) : (byte)0;
        var inputs = new Dictionary<ulong, InputState>();
        int? firstDescendingTick = null;
        bool landingObserved = false;
        for (int tick = 0; tick < SlideFlowMaxTicks; tick++)
        {
            metrics.TickCount = tick + 1;
            var before = sim.GetState(AttackerId);
            if (!before.IsGrounded && before.VY < 0f && !firstDescendingTick.HasValue)
            {
                firstDescendingTick = tick;
                metrics.FirstDescendingTick = tick;
            }
            bool edge = fastFallMode switch
            {
                "first-descending" => firstDescendingTick.HasValue && tick == firstDescendingTick.Value,
                "descending-plus-six" => firstDescendingTick.HasValue && tick == firstDescendingTick.Value + 6,
                _ => false,
            };
            bool aboutToLand = !before.IsGrounded && before.VY <= 0f
                && before.PY + (before.VY - effective.Movement.Gravity * Simulation.TickDt) * Simulation.TickDt
                    <= supportHeight + effective.CapsuleHeight * 0.5f + 0.04f;
            bool down = firstDescendingTick.HasValue
                && (!releasedBeforeLanding || (!aboutToLand && !landingObserved));
            bool attack = slot.HasValue && !landingObserved && !before.IsGrounded
                && metrics.TakeoffTick.HasValue && tick == metrics.TakeoffTick.Value + offset + 1;
            if (attack) metrics.AerialRequests++;
            float moveX = landingObserved ? 0f : direction.X;
            float moveY = landingObserved ? 0f : direction.Z;
            var input = new InputState
            {
                MoveX = moveX,
                MoveY = moveY,
                Jump = tick == 0,
                JumpHeld = !shortHop || tick == 0,
                Down = down,
                DownPressed = edge,
                ActiveSlot = attack ? wire : (byte)0,
                FacingYaw = (short)Math.Clamp((int)MathF.Round(facing * 18000f / MathF.PI), short.MinValue, short.MaxValue),
                AimYaw = (short)Math.Clamp((int)MathF.Round(facing * 18000f / MathF.PI), short.MinValue, short.MaxValue),
            };
            string actionRequest = attack ? $"air-normal={slot!.Value.Id}" : "";
            if (edge) actionRequest += (attack ? ";" : "") + $"fast-fall={fastFallMode}";
            if (input.Jump) actionRequest = shortHop ? "short-hop-press" : "full-hop-press";
            sim.SetTick((uint)tick);
            inputs[AttackerId] = input;
            sim.Tick(inputs);
            var after = sim.GetState(AttackerId);
            bool accepted = sim.LastTickAcceptedActions.Contains(AttackerId);
            bool touchdown = sim.LastTickTouchdowns.Contains(AttackerId);
            if (!metrics.TakeoffTick.HasValue && before.IsGrounded && !after.IsGrounded)
                metrics.TakeoffTick = tick;
            if (touchdown && !metrics.LandingTick.HasValue)
            {
                metrics.LandingTick = tick;
                metrics.Touchdowns++;
                landingObserved = true;
            }
            if (accepted)
            {
                metrics.AcceptedActions++;
                if (attack) metrics.AerialAccepted++;
                if (metrics.TakeoffTick.HasValue && tick > metrics.TakeoffTick.Value)
                    metrics.FirstActionTick ??= tick;
            }
            if (landingObserved && before.IsGrounded
                && sim.LastTickOrdinaryActionOpportunities.Contains(AttackerId))
                metrics.FirstGroundActionOpportunityTick ??= tick;
            if (after.State == ActionState.Sliding) metrics.SlideTicks++;
            if (after.State == ActionState.Crouching) metrics.CrouchTicks++;
            if (after.IsFastFalling && !before.IsFastFalling) metrics.FastFallActivations++;
            if (sim.LastTickDownAdmissions.TryGetValue(AttackerId, out var admission))
            {
                string reason = admission.ToString();
                metrics.AdmissionReasons[reason] = metrics.AdmissionReasons.TryGetValue(reason, out int reasonCount) ? reasonCount + 1 : 1;
            }
            if (slot.HasValue && effective.GetCookedSlotAbility(wire, airborne: true)?.IsRecoveryMove == true && accepted)
                metrics.RecoveryOwnedNegativeControl = true;
            WriteSlideTrace(traces, scenario, tick, input, after, sim, AttackerId, actionRequest);
            if (landingObserved && after.IsGrounded && after.VX == 0f && after.VZ == 0f
                && sim.GetActiveAbility(AttackerId) == null && after.AnimLockTicks == 0 && after.LandingLagTicks == 0
                && (after.State == ActionState.Idle || after.CrouchSettled))
                break;
        }
        metrics.Timeout = !metrics.LandingTick.HasValue;
        WriteSlideApproach(approaches, scenario, entry, slot, supportHeight, direction, shortHop, releasedBeforeLanding, fastFallMode, offset, metrics);
        if (assert && metrics.TickCount > SlideFlowMaxTicks)
            throw new InvalidDataException("slide-flow assert: scenario exceeded 600-tick limit");
        return metrics;
    }
    private static bool TryGetRecoveryAirSlot(MatchContentEntry entry, out SlotAddress slot, out byte wire)
    {
        foreach (string label in new[] { "1", "3", "4", "A", "E", "R", "F" })
        {
            if (!CanonicalSlotProjection.TryGet(true, label, out var candidate)) continue;
            byte candidateWire = AirWireSlot(label);
            if (entry.Definition.GetCookedSlotAbility(candidateWire, true)?.IsRecoveryMove == true)
            {
                slot = candidate;
                wire = candidateWire;
                return true;
            }
        }
        slot = default;
        wire = AbilitySlots.None;
        return false;
    }

    private static byte AirWireSlot(string label)
        => CanonicalSlotProjection.TryGet(true, label, out var address)
            ? CanonicalSlotProjection.ToWire(address)
            : throw new ArgumentException($"unknown air slot label '{label}'");

    private static bool RunRecoveryNegativeControl(StreamWriter traces, MatchContentEntry entry,
        CharacterDefinition effective, ArenaDefinition arena, DownActionTuning tuning, SlotAddress slot, byte wire,
        Dictionary<string, int> reasons, bool assert)
    {
        const ulong id = 1;
        var sim = new ServerSimulation(arena, downActionTuning: tuning);
        var initial = InitialState(effective, 0f, 0f, false, 0f);
        initial.PY = effective.CapsuleHeight * 0.5f + 2f;
        initial.VY = -2f;
        sim.RegisterEntity(id, effective, initial, entry.BakedAnimation);
        bool ownerObserved = false;
        bool fastFallActivated = false;
        for (int tick = 0; tick < 120; tick++)
        {
            var input = new InputState
            {
                Down = true,
                DownPressed = tick == 0,
                ActiveSlot = tick == 0 ? wire : (byte)0,
            };
            sim.SetTick((uint)tick);
            sim.Tick(new Dictionary<ulong, InputState> { [id] = input });
            var after = sim.GetState(id);
            if (sim.LastTickDownAdmissions.TryGetValue(id, out var reason))
            {
                string text = reason.ToString();
                reasons[text] = reasons.TryGetValue(text, out int count) ? count + 1 : 1;
            }
            ownerObserved |= sim.GetActiveAbility(id)?.OwnsVerticalMotion == true;
            fastFallActivated |= after.IsFastFalling;
            WriteSlideTrace(traces, $"recovery-negative/{entry.Identity.PackageId}/{slot.Id}", tick, input, after, sim, id, tick == 0 ? "recovery-owner+down-edge" : "");
            if (ownerObserved && sim.GetActiveAbility(id) == null && tick > 30)
                break;
        }
        bool suppressed = ownerObserved && !fastFallActivated;
        if (assert && !suppressed)
            throw new InvalidDataException($"slide-flow assert: recovery/vertical owner did not suppress fast-fall for {entry.Identity.PackageId}/{slot.Id}");
        return suppressed;
    }

    private static void AddMetrics(SlideFlowSummary summary, SlideRunMetrics metrics, string character, string control, string slot, float support, string direction, string fastFallMode)
    {
        summary.ScenarioCount++;
        if (metrics.Timeout) summary.TimeoutCount++;
        if (metrics.LandingTick.HasValue) summary.LandingCount++;
        summary.AcceptedActionCount += metrics.AcceptedActions;
        summary.TouchdownCount += metrics.Touchdowns;
        summary.AerialRequestCount += metrics.AerialRequests;
        summary.AerialAcceptedCount += metrics.AerialAccepted;
        summary.SlideTickCount += metrics.SlideTicks;
        foreach (var pair in metrics.AdmissionReasons)
            summary.AdmissionReasons[pair.Key] = summary.AdmissionReasons.TryGetValue(pair.Key, out int priorReason) ? priorReason + pair.Value : pair.Value;
        summary.CrouchTickCount += metrics.CrouchTicks;
        summary.FastFallActivationCount += metrics.FastFallActivations;
        if (control == "no-air") summary.NoAirControlCount++; else summary.AerialControlCount++;
        if (metrics.RecoveryOwnedNegativeControl) summary.RecoveryOwnedNegativeControlCount++;
        string key = control + ":" + character + ":" + slot;
        summary.ScenarioCounts[key] = summary.ScenarioCounts.TryGetValue(key, out int count) ? count + 1 : 1;
        if (metrics.FirstActionTick.HasValue && metrics.TakeoffTick.HasValue)
        {
            float latency = metrics.FirstActionTick.Value - metrics.TakeoffTick.Value;
            int samples = summary.FirstActionLatencySamples.TryGetValue(key, out int countSoFar) ? countSoFar + 1 : 1;
            summary.FirstActionLatencySamples[key] = samples;
            summary.FirstActionLatencyTicks[key] = summary.FirstActionLatencyTicks.TryGetValue(key, out float prior)
                ? prior + (latency - prior) / samples : latency;
        }
        summary.SlideWindows[key] = summary.SlideWindows.TryGetValue(key, out int window) ? window + metrics.SlideTicks : metrics.SlideTicks;
    }

    private static CharacterDefinition SlideDefinition(MatchContentEntry entry, string fallProfile)
    {
        if (fallProfile == "current") return entry.Definition;
        if (fallProfile != "previous") throw new ArgumentException("fall profile must be current or previous");
        if (entry.CookedCharacterPackage == null) throw new InvalidDataException($"{entry.Identity.PackageId}: previous fall profile requires an admitted cooked package");
        var result = CookedCharacterRuntimeAdapter.ToCharacterDefinition(entry.CookedCharacterPackage, entry.LegacySelector ?? CharacterClass.None);
        var movement = result.Movement;
        string package = entry.Identity.PackageId.ToLowerInvariant();
        (movement.MaxFallSpeed, movement.FastFallSpeed) = package switch
        {
            "fightguy" => (48f, 58f),
            "manki" => (45f, 54f),
            "bonk" => (48f, 58f),
            "wibou" => (48f, 58f),
            _ => throw new InvalidDataException($"{entry.Identity.PackageId}: no verified previous fall profile")
        };
        return result with { Movement = movement };
    }

    private static ArenaDefinition SlideArena(float supportHeight, bool edge = false)
    {
        const int size = 256;
        var data = Enumerable.Repeat(supportHeight, size * size).ToArray();
        if (edge)
        {
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
                if (MathF.Abs(x - size / 2) > 12 || MathF.Abs(z - size / 2) > 12)
                    data[z * size + x] = float.MinValue;
        }
        return new ArenaDefinition
        {
            Name = edge ? "slide-flow-platform-edge" : "slide-flow-support",
            DisplayName = edge ? "declared platform edge" : "declared support",
            KillHeight = -1000f,
            MinX = -128f,
            MaxX = 128f,
            MinZ = -128f,
            MaxZ = 128f,
            SpawnPoints = new[] { new SpawnPoint { X = 0f, Y = supportHeight, Z = 0f } },
            Heightmap = new ArenaHeightmap { Data = data, Width = size, Height = size, CellSize = 1f, OriginX = -128f, OriginZ = -128f },
            CollisionTriangles = Array.Empty<CollisionTriangle>(),
        };
    }

    private static void WriteSlideTraceHeader(StreamWriter writer)
        => writer.WriteLine("scenario,tick,moveX,moveY,down,downPressed,jump,jumpHeld,activeSlot,state,grounded,px,py,pz,vx,vy,vz,xzSpeed,isFastFalling,jumpFromSlide,slideAttackCarryActive,crouchSettled,queuedCrouchBrace,jumpsLeft,airDodgesLeft,attackSlot,attackElapsedTicks,animLockTicks,landingLagTicks,hitstopTicks,hitstunTicks,actionRequest,actionAccepted,genuineTouchdown,downAdmissionReason,rushTicks,dashCooldownTicks,burstCooldownTicks,chargeStockSpent,chargeStockRegenTicks,airTimeTicks,normalCooldownTicks");

    private static void WriteSlideTrace(StreamWriter writer, string scenario, int tick, InputState input, CharacterState state, ServerSimulation sim, ulong entityId, string actionRequest)
    {
        string reason = sim.LastTickDownAdmissions.TryGetValue(entityId, out var admission) ? admission.ToString() : "";
        bool accepted = sim.LastTickAcceptedActions.Contains(entityId);
        bool touchdown = sim.LastTickTouchdowns.Contains(entityId);
        float speed = MathF.Sqrt(state.VX * state.VX + state.VZ * state.VZ);
        writer.WriteLine(string.Join(',', Fields(
            scenario, tick, input.MoveX.ToString("0.###", CultureInfo.InvariantCulture), input.MoveY.ToString("0.###", CultureInfo.InvariantCulture),
            input.Down, input.DownPressed, input.Jump, input.JumpHeld, input.ActiveSlot, state.State, state.IsGrounded,
            state.PX.ToString("0.###", CultureInfo.InvariantCulture), state.PY.ToString("0.###", CultureInfo.InvariantCulture), state.PZ.ToString("0.###", CultureInfo.InvariantCulture),
            state.VX.ToString("0.###", CultureInfo.InvariantCulture), state.VY.ToString("0.###", CultureInfo.InvariantCulture), state.VZ.ToString("0.###", CultureInfo.InvariantCulture), speed.ToString("0.###", CultureInfo.InvariantCulture),
            state.IsFastFalling, state.JumpTakeoff == JumpTakeoff.Slide, state.SlideAttackCarryActive, state.CrouchSettled, state.QueuedCrouchBrace,
            state.JumpsLeft, state.AirDodgesLeft, state.AttackSlot, state.AttackElapsedTicks, state.AnimLockTicks, state.LandingLagTicks,
            state.HitstopTicks, state.HitstunTicks, actionRequest, accepted, touchdown, reason,
            state.RushTicks, state.ChargeStockSpent,
            state.ChargeStockRegenTicks, state.AirTimeTicks, state.Cooldown0)));
    }

    private static void WriteSlideApproachHeader(StreamWriter writer)
        => writer.WriteLine("scenario,character,slot,targetSupport,targetDirection,hopMode,downLandingMode,fastFallMode,pressOffset,takeoffTick,firstDescendingTick,landingTick,firstActionTick,firstActionLatencyTicks,aerialRequested,aerialAccepted,slideTicks,crouchTicks,fastFallActivations,acceptedActions,touchdowns,timeout,recoveryOwnedNegativeControl,firstGroundActionOpportunityTick,landingToActionOpportunityTicks");

    private static void WriteSlideApproach(StreamWriter writer, string scenario, MatchContentEntry entry, SlotAddress? slot, float supportHeight, SlideDirection direction, bool shortHop, bool released, string fastFallMode, int offset, SlideRunMetrics metrics)
    {
        string slotName = slot?.Id ?? "no-air";
        string hop = shortHop ? "short-hop" : "full-hop";
        string landing = released ? "released-before-landing" : "held-through-landing";
        int? latency = metrics.FirstActionTick.HasValue && metrics.TakeoffTick.HasValue ? metrics.FirstActionTick.Value - metrics.TakeoffTick.Value : null;
        writer.WriteLine(string.Join(',', Fields(scenario, entry.Identity.PackageId, slotName, supportHeight.ToString("0.###", CultureInfo.InvariantCulture), direction.Id, hop, landing, fastFallMode, offset,
            metrics.TakeoffTick, metrics.FirstDescendingTick, metrics.LandingTick, metrics.FirstActionTick, latency, metrics.AerialRequests, metrics.AerialAccepted, metrics.SlideTicks, metrics.CrouchTicks, metrics.FastFallActivations, metrics.AcceptedActions, metrics.Touchdowns, metrics.Timeout, metrics.RecoveryOwnedNegativeControl,
            metrics.FirstGroundActionOpportunityTick, metrics.FirstGroundActionOpportunityTick - metrics.LandingTick)));
    }
}
