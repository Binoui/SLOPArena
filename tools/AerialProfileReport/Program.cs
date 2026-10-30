using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.AerialProfileReport;

/// <summary>
/// Experiment-only aerial playstyle sweep. It owns no gameplay policy: every policy calls the
/// real HeuristicBotPolicy first, then applies one narrowly-scoped input wrapper.
/// </summary>
internal static class Program
{
    private const int Stocks = 3;
    private const int MaxTicks = 10800;
    private const int AttributionWindowTicks = 90;
    private const int NormalReactionDelayTicks = 18;
    private static readonly int[] DefaultSeeds = { 42, 43, 44, 45, 46 };
    private static readonly CharacterClass[] AdmittedCharacters =
    {
        CharacterClass.Manki,
        CharacterClass.FightGuy,
        CharacterClass.Wibou,
        CharacterClass.Bonk,
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };

    private enum PolicyKind
    {
        Current,
        JumpIn,
        Grounded,
    }

    private sealed class Scenario
    {
        public string Id = "";
        public PolicyKind A;
        public PolicyKind B;
    }

    private static readonly Scenario[] Scenarios =
    {
        new() { Id = "current-baseline", A = PolicyKind.Current, B = PolicyKind.Current },
        new() { Id = "jump-in-vs-grounded", A = PolicyKind.JumpIn, B = PolicyKind.Grounded },
        new() { Id = "jump-in-mirror", A = PolicyKind.JumpIn, B = PolicyKind.JumpIn },
    };

    private sealed class MatchReport
    {
        public string Schema = "sloparena.aerial-profile.match.v1";
        public string Character = "";
        public string PackageId = "";
        public string SourceHash = "";
        public string CookedContentHash = "";
        public string PackageHash = "";
        public string Scenario = "";
        public string PolicyA = "";
        public string PolicyB = "";
        public int Seed;
        public bool SideSwapped;
        public int Stocks;
        public int MaxTicks;
        public string Difficulty = "NORMAL";
        public int ReactionDelayTicks;
        public MatchRecord Simulation = null!;
        public List<TickTrace> Trace = new();
        public List<JumpObservation> Jumps = new();
        public List<AerialAttack> AerialAttacks = new();
        public List<ActivationConnection> Activations = new();
        public List<JumpInWindow> JumpInWindows = new();
        public string DeterminismFingerprint = "";
    }

    private sealed class TickTrace
    {
        public int Tick;
        public ulong EntityId;
        public string Policy = "";
        public InputState Input;
        public StateTrace Pre = null!;
        public StateTrace Post = null!;
        public bool EmergencyRecoveryInput;
        public bool VoluntaryJumpInput;
        public bool ApproachFromDelayedObservation;
        public bool WrapperJumpOverride;
        public bool WrapperJumpSuppressed;
        public bool DelayedObservationAvailable;
        public float HorizontalSpeed;
        public string SpeedBucket = "";
        public string JumpPhase = "";
    }

    private sealed class StateTrace
    {
        public float PX, PY, PZ;
        public float VX, VY, VZ;
        public bool IsGrounded;
        public byte JumpsLeft;
        public ActionState State;
        public byte AttackSlot;
        public byte AttackSequence;
        public byte Deaths;
        public ushort DamagePercent;
        public ushort HitstunTicks;
        public ushort HitstopTicks;
        public ushort AnimLockTicks;
        public ushort LandingLagTicks;
    }

    private sealed class JumpObservation
    {
        public ulong EntityId;
        public string Policy = "";
        public int InputTick;
        public int TakeoffTick = -1;
        public int LandingTick = -1;
        public bool InputGrounded;
        public bool EmergencyRecovery;
        public bool Voluntary;
        public bool Approach;
        public bool SuccessfulTakeoff;
        public string Outcome = "rejected-or-not-yet-taken-off";
    }

    private sealed class AerialAttack
    {
        public ulong EntityId;
        public string Policy = "";
        public byte ActiveSlot;
        public string MoveId = "";
        public string MoveName = "";
        public ulong ActivationId;
        public int StartTick;
        public bool Connected;
        public string Intent = "ambiguous";
        public bool AuthoredRecoveryMove;
        public bool AuthoredDamage;
        public float StartSpeed;
        public string SpeedBucket = "";
        public string JumpPhase = "";
        public List<string> HitRelations = new();
        public List<int> HitTicks = new();
    }

    private sealed class ActivationConnection
    {
        public ulong ActivationId;
        public ulong EntityId;
        public byte ActiveSlot;
        public string MoveId = "";
        public string MoveName = "";
        public bool Air;
        public string Intent = "ambiguous";
        public bool AuthoredRecoveryMove;
        public bool AuthoredDamage;
        public bool Accepted;
        public bool Connected;
        public int StartTick = -1;
        public List<ulong> Targets = new();
        public List<int> HitTicks = new();
        public List<string> HitRelations = new();
    }

    private sealed class JumpInWindow
    {
        public ulong JumperEntityId;
        public ulong TargetEntityId;
        public string JumperPolicy = "";
        public string TargetPolicy = "";
        public int JumpInputTick;
        public int TakeoffTick;
        public int StartTick;
        public int EndTick;
        public int WindowTicks;
        public float JumperDamageDealt;
        public float JumperDamageTaken;
        public float TargetDamageDealt;
        public float TargetDamageTaken;
        public int JumperHitsDealt;
        public int JumperHitsTaken;
        public int TargetHitsDealt;
        public int TargetHitsTaken;
        public int JumperDeathsInWindow;
        public int TargetDeathsInWindow;
        public int KillerMatchesInWindow;
        public string Attribution = "temporal association only; not causal attribution";
    }

    private sealed class RosterProvenance
    {
        public string Character = "";
        public string PackageId = "";
        public string Version = "";
        public string SourceHash = "";
        public string CookedContentHash = "";
        public string PackageHash = "";
        public bool CookedPackageLoaded;
        public bool BakedAnimationLoaded;
        public int BakedBoneCount;
        public int BakedAnimationCount;
    }

    private sealed class Metadata
    {
        public string Schema = "sloparena.aerial-profile.metadata.v1";
        public string GeneratedAtUtc = "";
        public string[] Arguments = Array.Empty<string>();
        public string[] Seeds = Array.Empty<string>();
        public string Difficulty = "NORMAL";
        public int TickRateHz = 60;
        public string ExposureRule = "entity_minutes = authoritative ticks / (60 ticks/s * 60 s/min); 10800 ticks = 3 minutes per entity.";
        public int ReactionDelayTicks;
        public int Stocks;
        public int MaxTicks;
        public int AttributionWindowTicks;
        public string AttributionRule = "For each successful voluntary grounded approach jump, inspect [takeoff, takeoff+90] clipped to the match; this is temporal association only, not causation.";
        public string TemporalAggregationRule = "Aggregate temporal damage/hits/deaths by unioning overlapping jump-in windows per entity before policy assignment.";
        public string ApproachRule = "Jump-in means a grounded voluntary jump whose final/base policy planar input is toward the delayed target (dot >= 0.5); non-approach voluntary jumps remain recorded but do not open a jump-in window.";
        public string AerialClassificationRule = "Recovery when the admitted cooked slot is marked IsRecoveryMove; offensive when it is not recovery and authored hitbox/projectile/capability damage is positive; ambiguous otherwise, retained rather than discarded.";
        public string AirRelationRule = "For an accepted aerial activation hit, air-to-air means the target was airborne in the pre-tick authoritative state at hit resolution; air-to-ground means grounded; all relations remain in raw telemetry.";
        public string SpeedBuckets = "0-1,1-3,3-6,6+ m/s";
        public string JumpPhaseBuckets = "grounded,jump-squat,rise,apex-or-float,fall";
        public string[] Policies = Array.Empty<string>();
        public RosterProvenance[] Roster = Array.Empty<RosterProvenance>();
        public object Arena = null!;
    }

    private sealed class AggregateCounter
    {
        public int JumpInputs;
        public int VoluntaryJumpInputs;
        public int EmergencyRecoveryInputs;
        public int SuccessfulTakeoffs;
        public int AerialAccepted;
        public int AerialOffensive;
        public int AerialRecovery;
        public int AerialAmbiguous;
        public int AirToGroundHits;
        public int AirToAirHits;
        public float DamageDealt;
        public float DamageTaken;
        public int Deaths;
        public float TemporalDamageDealt;
        public float TemporalDamageTaken;
        public int TemporalHitsDealt;
        public int TemporalHitsTaken;
        public int TemporalDeaths;
        public int AerialConnectedActivations;
        public int AerialOffensiveConnectedActivations;
        public int SelfHitEvents;
        public int TemporalSelfHitEvents;
        public float TemporalSelfHitDamage;
        public float SelfHitDamage;

        public void Add(AggregateCounter other)
        {
            JumpInputs += other.JumpInputs;
            VoluntaryJumpInputs += other.VoluntaryJumpInputs;
            EmergencyRecoveryInputs += other.EmergencyRecoveryInputs;
            SuccessfulTakeoffs += other.SuccessfulTakeoffs;
            AerialAccepted += other.AerialAccepted;
            AerialOffensive += other.AerialOffensive;
            AerialRecovery += other.AerialRecovery;
            AerialAmbiguous += other.AerialAmbiguous;
            AirToGroundHits += other.AirToGroundHits;
            AirToAirHits += other.AirToAirHits;
            DamageDealt += other.DamageDealt;
            DamageTaken += other.DamageTaken;
            Deaths += other.Deaths;
            TemporalDamageDealt += other.TemporalDamageDealt;
            TemporalDamageTaken += other.TemporalDamageTaken;
            TemporalHitsDealt += other.TemporalHitsDealt;
            TemporalHitsTaken += other.TemporalHitsTaken;
            TemporalDeaths += other.TemporalDeaths;
            AerialConnectedActivations += other.AerialConnectedActivations;
            AerialOffensiveConnectedActivations += other.AerialOffensiveConnectedActivations;
            SelfHitEvents += other.SelfHitEvents;
            SelfHitDamage += other.SelfHitDamage;
            TemporalSelfHitEvents += other.TemporalSelfHitEvents;
            TemporalSelfHitDamage += other.TemporalSelfHitDamage;
        }
    }
    private readonly struct TickRange
    {
        public TickRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; }
        public int End { get; }
    }

    private sealed class TemporalTotals
    {
        public int ExposureTicks;
        public float DamageDealt;
        public float DamageTaken;
        public int HitsDealt;
        public int HitsTaken;
        public int Deaths;
        public int SelfHitEvents;
        public float SelfHitDamage;
    }

    private sealed class AggregateRow
    {
        public string Character = "";
        public string Scenario = "";
        public string PolicyPair = "";
        public string Policy = "";
        public string SourceHash = "";
        public string CookedContentHash = "";
        public string PackageHash = "";
        public int Matches;
        public int SideSwappedMatches;
        public int TimedOutMatches;
        public long DurationTicks;
        public AggregateCounter Counter = new();
        public double EntityMinutes;
        public double TemporalUnionEntityMinutes;
    }

    private sealed class AggregateAccumulator
    {
        public readonly string Character;
        public readonly string Scenario;
        public readonly string PolicyPair;
        private readonly Dictionary<string, AggregateRow> _rows = new(StringComparer.Ordinal);

        public AggregateAccumulator(string character, Scenario scenario)
        {
            Character = character;
            Scenario = scenario.Id;
            PolicyPair = $"{Name(scenario.A)}-vs-{Name(scenario.B)}";
            Get(Name(scenario.A));
            Get(Name(scenario.B));
        }

        private AggregateRow Get(string policy)
        {
            if (!_rows.TryGetValue(policy, out var row))
            {
                row = new AggregateRow
                {
                    Character = Character,
                    Scenario = Scenario,
                    PolicyPair = PolicyPair,
                    Policy = policy,
                };
                _rows.Add(policy, row);
            }
            return row;
        }

        public void Add(MatchReport report)
        {
            var rows = _rows.Values.ToArray();
            foreach (var row in rows)
            {
                row.Matches++;
                if (report.SideSwapped) row.SideSwappedMatches++;
                if (report.Simulation.TimedOut) row.TimedOutMatches++;
                row.DurationTicks += report.Simulation.DurationTicks;
                int entityCount = 0;
                if (report.PolicyA == row.Policy) entityCount++;
                if (report.PolicyB == row.Policy) entityCount++;
                row.EntityMinutes += report.Simulation.DurationTicks * entityCount / 3600d;
                if (row.SourceHash.Length == 0)
                {
                    row.SourceHash = report.SourceHash;
                    row.CookedContentHash = report.CookedContentHash;
                    row.PackageHash = report.PackageHash;
                }
            }

            var counters = new Dictionary<string, AggregateCounter>(StringComparer.Ordinal);
            foreach (var policy in new[] { report.PolicyA, report.PolicyB })
                counters[policy] = new AggregateCounter();
            foreach (var trace in report.Trace)
            {
                if (!counters.TryGetValue(trace.Policy, out var counter)) continue;
                if (trace.Input.Jump) counter.JumpInputs++;
                if (trace.VoluntaryJumpInput) counter.VoluntaryJumpInputs++;
                if (trace.EmergencyRecoveryInput && trace.Input.Jump) counter.EmergencyRecoveryInputs++;
            }
            foreach (var jump in report.Jumps)
            {
                if (!counters.TryGetValue(jump.Policy, out var counter)) continue;
                if (jump.SuccessfulTakeoff) counter.SuccessfulTakeoffs++;
            }
            foreach (var attack in report.AerialAttacks)
            {
                if (!counters.TryGetValue(attack.Policy, out var counter)) continue;
                counter.AerialAccepted++;
                switch (attack.Intent)
                {
                    case "offensive": counter.AerialOffensive++; break;
                    case "recovery": counter.AerialRecovery++; break;
                    default: counter.AerialAmbiguous++; break;
                }
                foreach (var relation in attack.HitRelations)
                {
                    if (relation == "air-to-ground") counter.AirToGroundHits++;
                    else if (relation == "air-to-air") counter.AirToAirHits++;
                }
            }
            foreach (var activation in report.Activations)
            {
                if (!activation.Accepted || !activation.Air || !activation.Connected) continue;
                if (!counters.TryGetValue(PolicyForEntity(report, activation.EntityId), out var counter)) continue;
                counter.AerialConnectedActivations++;
                if (activation.Intent == "offensive")
                    counter.AerialOffensiveConnectedActivations++;
            }
            foreach (var hit in report.Simulation.Hits)
            {
                if (hit.Attacker == hit.Target
                    && counters.TryGetValue(PolicyForEntity(report, hit.Attacker), out var selfHit))
                {
                    selfHit.SelfHitEvents++;
                    selfHit.SelfHitDamage += hit.Damage;
                }
                if (counters.TryGetValue(PolicyForEntity(report, hit.Attacker), out var attacker))
                    attacker.DamageDealt += hit.Damage;
                if (counters.TryGetValue(PolicyForEntity(report, hit.Target), out var target))
                    target.DamageTaken += hit.Damage;
            }
            foreach (var death in report.Simulation.Deaths)
                if (counters.TryGetValue(PolicyForEntity(report, death.EntityId), out var counter))
                    counter.Deaths++;

            foreach (var (entity, temporal) in BuildUnionTemporal(report))
            {
                string policy = PolicyForEntity(report, entity);
                if (!counters.TryGetValue(policy, out var counter)) continue;
                counter.TemporalDamageDealt += temporal.DamageDealt;
                counter.TemporalDamageTaken += temporal.DamageTaken;
                counter.TemporalHitsDealt += temporal.HitsDealt;
                counter.TemporalHitsTaken += temporal.HitsTaken;
                counter.TemporalDeaths += temporal.Deaths;
                Get(policy).TemporalUnionEntityMinutes += temporal.ExposureTicks / 3600d;
                counter.TemporalSelfHitEvents += temporal.SelfHitEvents;
                counter.TemporalSelfHitDamage += temporal.SelfHitDamage;
            }
            foreach (var (policy, counter) in counters)
                Get(policy).Counter.Add(counter);
        }

        public IEnumerable<AggregateRow> Rows => _rows.Values.OrderBy(x => x.Policy, StringComparer.Ordinal);
    }

    private sealed class DelayedTargetHistory
    {
        private readonly CharacterState[] _states = new CharacterState[NormalReactionDelayTicks + 1];
        private int _count;
        private int _write;

        public void Observe(in CharacterState state)
        {
            _states[_write] = state;
            _write = (_write + 1) % _states.Length;
            if (_count < _states.Length) _count++;
        }

        public bool TryGet(out CharacterState state)
        {
            if (_count <= NormalReactionDelayTicks)
            {
                state = default;
                return false;
            }
            int index = _write - 1 - NormalReactionDelayTicks;
            if (index < 0) index += _states.Length;
            state = _states[index];
            return true;
        }

        public void Reset()
        {
            Array.Clear(_states, 0, _states.Length);
            _count = 0;
            _write = 0;
        }
    }

    private sealed class PolicyController
    {
        private readonly HeuristicBotPolicy _basePolicy = new();
        private readonly DelayedTargetHistory _delayedTarget = new();
        private readonly PolicyKind _kind;
        public bool LastApproach { get; private set; }
        public bool LastDelayedObservationAvailable { get; private set; }
        public bool LastJumpOverride { get; private set; }
        public bool LastJumpSuppressed { get; private set; }

        public PolicyController(PolicyKind kind) => _kind = kind;

        public InputState Decide(in CharacterState self, in CharacterState target,
            CharacterDefinition def, Random rng, BotMemory memory, in ArenaDefinition arena,
            BakedAnimationData? baked, CookedSlotDefinition? activeMove, CookedSlotDefinition? targetMove)
        {
            _delayedTarget.Observe(target);
            LastDelayedObservationAvailable = _delayedTarget.TryGet(out var delayedTarget);
            _basePolicy.ObserveOpponent(memory, target, def, baked, targetMove);
            memory.ChooseOpponent(self);
            var baseInput = _basePolicy.Decide(self, def, rng, memory, arena, baked, activeMove: activeMove);
            LastApproach = LastDelayedObservationAvailable && IsApproach(self, delayedTarget, baseInput);
            LastJumpOverride = false;
            LastJumpSuppressed = false;

            if (_kind == PolicyKind.Current)
                return baseInput;

            if (_kind == PolicyKind.JumpIn && self.IsGrounded
                && !PolicyHelpers.IsEmergencyRecovery(self, def, in arena)
                && baseInput.ActiveSlot == 0 && !baseInput.Jump
                && !baseInput.IsAiming && LastApproach)
            {
                // Preserve the base approach vector and all normal facing/aim fields. The only
                // experiment input is a real jump edge plus a held key through jump squat.
                baseInput.Jump = true;
                baseInput.JumpHeld = true;
                LastJumpOverride = true;
                return baseInput;
            }
            if (_kind == PolicyKind.Grounded && self.IsGrounded && baseInput.Jump
                && !PolicyHelpers.IsEmergencyRecovery(self, def, in arena))
            {
                baseInput.Jump = false;
                baseInput.JumpHeld = false;
                LastJumpSuppressed = true;
            }
            return baseInput;
        }

        public void Reset()
        {
            _delayedTarget.Reset();
            LastApproach = false;
            LastDelayedObservationAvailable = false;
            LastJumpOverride = false;
            LastJumpSuppressed = false;
        }

        private static bool IsApproach(in CharacterState self, in CharacterState target, in InputState input)
        {
            float dx = target.PX - self.PX;
            float dz = target.PZ - self.PZ;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            float move = MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY);
            return distance > 0.001f && move > 0.1f
                && (input.MoveX * dx + input.MoveY * dz) / (move * distance) >= 0.5f;
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            string output = ParseArg(args, "--out") ?? Path.Combine("artifacts", "aerial-profile-report");
            int[] seeds = ParseSeeds(ParseArg(args, "--seeds"));
            bool selfCheck = HasFlag(args, "--selfcheck");
            Directory.CreateDirectory(Path.GetFullPath(output));
            if (MaxTicks != 3 * 60 * 60)
                throw new InvalidOperationException("exposure unit selfcheck failed: MaxTicks must represent 3 minutes at 60 Hz");

            var arena = BuildKillArena();
            var roster = AdmittedCharacters.Select(ResolveProvenance).ToArray();
            var metadata = new Metadata
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Arguments = args,
                Seeds = seeds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(),
                Difficulty = BotDifficultyProfile.DisplayName(CpuDifficulty.Normal),
                ReactionDelayTicks = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks,
                Stocks = Stocks,
                MaxTicks = MaxTicks,
                AttributionWindowTicks = AttributionWindowTicks,
                Policies = new[]
                {
                    "current: exactly delegates to HeuristicBotPolicy.Decide",
                    "jump-in: delegates, then adds Jump+JumpHeld only to delayed-target-directed grounded approaches",
                    "grounded: delegates, then removes voluntary grounded Jump/JumpHeld; emergency recovery is preserved",
                },
                Roster = roster,
                Arena = new
                {
                    name = arena.Name,
                    displayName = arena.DisplayName,
                    killHeight = arena.KillHeight,
                    minX = arena.MinX,
                    maxX = arena.MaxX,
                    minZ = arena.MinZ,
                    maxZ = arena.MaxZ,
                    heightmapWidth = arena.Heightmap.Width,
                    heightmapHeight = arena.Heightmap.Height,
                    heightmapCellSize = arena.Heightmap.CellSize,
                },
            };
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(metadata, JsonOptions));

            var accumulators = new Dictionary<string, AggregateAccumulator>(StringComparer.Ordinal);
            bool deterministicChecked = false;
            foreach (var provenance in roster)
            {
                var entry = BuiltInContentResolver.Resolve(ToCharacterClass(provenance.Character));
                foreach (var scenario in Scenarios)
                {
                    var accumulator = new AggregateAccumulator(provenance.Character, scenario);
                    accumulators[$"{provenance.Character}/{scenario.Id}"] = accumulator;
                    foreach (bool sideSwapped in new[] { false, true })
                    {
                        foreach (int seed in seeds)
                        {
                            var report = RunMatch(entry, scenario, seed, sideSwapped, arena);
                            if (selfCheck)
                            {
                                ValidateSelfCheck(report);
                                if (!deterministicChecked)
                                {
                                    var replay = RunMatch(entry, scenario, seed, sideSwapped, arena);
                                    if (!string.Equals(report.DeterminismFingerprint, replay.DeterminismFingerprint, StringComparison.Ordinal))
                                        throw new InvalidOperationException("determinism selfcheck failed: repeated seed produced a different trace fingerprint");
                                    deterministicChecked = true;
                                }
                            }
                            accumulator.Add(report);
                            string filename = $"{provenance.Character}-{scenario.Id}-{(sideSwapped ? "swapped" : "normal")}-seed{seed}.json";
                            File.WriteAllText(Path.Combine(output, filename), JsonSerializer.Serialize(report, JsonOptions));
                            Console.WriteLine($"{provenance.Character} {scenario.Id} {(sideSwapped ? "swapped" : "normal")} seed={seed}: ticks={report.Simulation.DurationTicks} jumps={report.Jumps.Count} takeoffs={report.Jumps.Count(x => x.SuccessfulTakeoff)} aerial={report.AerialAttacks.Count}");
                        }
                    }
                }
            }

            var rows = accumulators.Values.SelectMany(x => x.Rows).ToArray();
            File.WriteAllText(Path.Combine(output, "aggregate.csv"), BuildAggregateCsv(rows));
            Console.WriteLine($"Wrote {rows.Length} aggregate rows and {roster.Length * Scenarios.Length * seeds.Length * 2} per-match JSON files to {Path.GetFullPath(output)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AerialProfileReport failed: {ex.Message}");
            return 2;
        }
    }

    private static MatchReport RunMatch(MatchContentEntry entry, Scenario scenario, int seed,
        bool sideSwapped, ArenaDefinition arena)
    {
        var def = entry.Definition;
        var baked = entry.BakedAnimation;
        if (entry.CookedCharacterPackage == null || baked == null)
            throw new InvalidDataException($"{entry.Identity.PackageId}: cooked definition and baked animation are required");

        var rule = new StockMatchRule((byte)Stocks);
        var sim = new ServerSimulation(arena, rule);
        float groundY = def.CapsuleHeight * 0.5f;
        RegisterBot(sim, def, SelfPlayMatch.EntityA, sideSwapped ? 12f : -12f, groundY, baked, sideSwapped);
        RegisterBot(sim, def, SelfPlayMatch.EntityB, sideSwapped ? -12f : 12f, groundY, baked, sideSwapped);

        var rng = new Random(seed);
        var memA = NewMemory();
        var memB = NewMemory();
        var controllerA = new PolicyController(sideSwapped ? scenario.B : scenario.A);
        var controllerB = new PolicyController(sideSwapped ? scenario.A : scenario.B);
        var recorder = new MatchRecorder();
        var trace = new List<TickTrace>();
        var jumps = new List<JumpObservation>();
        var pendingJumps = new Dictionary<ulong, List<JumpObservation>>
        {
            [SelfPlayMatch.EntityA] = new(),
            [SelfPlayMatch.EntityB] = new(),
        };
        var inputs = new Dictionary<ulong, InputState>();
        byte deathsA = 0, deathsB = 0;
        int tick = 0;
        for (; tick < MaxTicks; tick++)
        {
            var preA = sim.GetState(SelfPlayMatch.EntityA);
            var preB = sim.GetState(SelfPlayMatch.EntityB);
            var inputA = controllerA.Decide(preA, preB, def, rng, memA, arena, baked, sim.GetActiveMove(SelfPlayMatch.EntityA)?.Slot, sim.GetActiveMove(SelfPlayMatch.EntityB)?.Slot);
            var inputB = controllerB.Decide(preB, preA, def, rng, memB, arena, baked, sim.GetActiveMove(SelfPlayMatch.EntityB)?.Slot, sim.GetActiveMove(SelfPlayMatch.EntityA)?.Slot);
            inputs[SelfPlayMatch.EntityA] = inputA;
            inputs[SelfPlayMatch.EntityB] = inputB;
            RecordInputTrace(trace, tick, SelfPlayMatch.EntityA, PolicyName(sideSwapped ? scenario.B : scenario.A), preA, inputA, controllerA, def, arena);
            RecordInputTrace(trace, tick, SelfPlayMatch.EntityB, PolicyName(sideSwapped ? scenario.A : scenario.B), preB, inputB, controllerB, def, arena);
            TrackJumpInput(jumps, pendingJumps[SelfPlayMatch.EntityA], SelfPlayMatch.EntityA, PolicyName(sideSwapped ? scenario.B : scenario.A), tick, preA, inputA, controllerA, def, arena);
            TrackJumpInput(jumps, pendingJumps[SelfPlayMatch.EntityB], SelfPlayMatch.EntityB, PolicyName(sideSwapped ? scenario.A : scenario.B), tick, preB, inputB, controllerB, def, arena);

            recorder.RecordInputs(tick, inputs);
            recorder.RecordPresses(sim, tick, inputs, def);
            sim.Tick(inputs);
            var postA = sim.GetState(SelfPlayMatch.EntityA);
            var postB = sim.GetState(SelfPlayMatch.EntityB);
            CompleteJumpTransitions(pendingJumps[SelfPlayMatch.EntityA], tick, preA, postA, sim.LastTickDeaths);
            CompleteJumpTransitions(pendingJumps[SelfPlayMatch.EntityB], tick, preB, postB, sim.LastTickDeaths);
            SetPostTrace(trace, tick, SelfPlayMatch.EntityA, postA);
            SetPostTrace(trace, tick, SelfPlayMatch.EntityB, postB);
            recorder.RecordTick(sim, tick, inputs, def);

            bool respawnedA = postA.Deaths > deathsA;
            bool respawnedB = postB.Deaths > deathsB;
            if (respawnedA)
            {
                deathsA = postA.Deaths;
                memA.Reset();
                memA.Difficulty = CpuDifficulty.Normal;
                controllerA.Reset();
            }
            if (respawnedB)
            {
                deathsB = postB.Deaths;
                memB.Reset();
                memB.Difficulty = CpuDifficulty.Normal;
                controllerB.Reset();
            }
            foreach (var hit in sim.LastTickHits)
            {
                if (!respawnedA && hit.TargetEntityId == SelfPlayMatch.EntityB)
                    memA.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, postB, hit.HitstopTicks);
                if (!respawnedB && hit.TargetEntityId == SelfPlayMatch.EntityA)
                    memB.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, postA, hit.HitstopTicks);
            }
            var outcome = rule.Evaluate(sim.GetAllStates());
            if (outcome.IsEnded)
            {
                tick++;
                return FinishMatch(entry, scenario, seed, sideSwapped, recorder, sim, tick, trace, jumps);
            }
        }
        return FinishMatch(entry, scenario, seed, sideSwapped, recorder, sim, tick, trace, jumps);
    }

    private static MatchReport FinishMatch(MatchContentEntry entry, Scenario scenario, int seed,
        bool sideSwapped, MatchRecorder recorder, ServerSimulation sim, int tick,
        List<TickTrace> trace, List<JumpObservation> jumps)
    {
        var outcome = new StockMatchRule((byte)Stocks).Evaluate(sim.GetAllStates());
        var record = recorder.Finish(tick, seed, outcome);
        record.TimedOut = tick >= MaxTicks && !outcome.IsEnded;
        var finalA = sim.GetState(SelfPlayMatch.EntityA);
        var finalB = sim.GetState(SelfPlayMatch.EntityB);
        record.Entity1Deaths = finalA.Deaths;
        record.Entity2Deaths = finalB.Deaths;
        record.Entity1Damage = finalA.DamagePercent;
        record.Entity2Damage = finalB.DamagePercent;
        var report = new MatchReport
        {
            Character = entry.Identity.PackageId,
            PackageId = entry.Identity.PackageId,
            SourceHash = entry.Identity.SourceHash,
            CookedContentHash = entry.Identity.CookedContentHash,
            PackageHash = entry.Identity.PackageHash,
            Scenario = scenario.Id,
            PolicyA = PolicyName(sideSwapped ? scenario.B : scenario.A),
            PolicyB = PolicyName(sideSwapped ? scenario.A : scenario.B),
            Seed = seed,
            SideSwapped = sideSwapped,
            Stocks = Stocks,
            MaxTicks = MaxTicks,
            Difficulty = BotDifficultyProfile.DisplayName(CpuDifficulty.Normal),
            ReactionDelayTicks = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks,
            Simulation = record,
            Trace = trace,
            Jumps = jumps,
        };
        report.AerialAttacks = BuildAerialAttacks(entry.Definition, report);
        report.Activations = BuildActivations(entry.Definition, report);
        report.JumpInWindows = BuildJumpInWindows(report);
        report.DeterminismFingerprint = Fingerprint(report);
        return report;
    }

    private static BotMemory NewMemory() => new() { Difficulty = CpuDifficulty.Normal };

    private static void RegisterBot(ServerSimulation sim, CharacterDefinition def, ulong id,
        float x, float py, BakedAnimationData baked, bool sideSwapped)
    {
        var state = new CharacterState
        {
            EntityId = id,
            PX = x,
            PY = py,
            PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
            FacingYaw = sideSwapped
                ? (id == SelfPlayMatch.EntityA ? MathF.PI : 0f)
                : (id == SelfPlayMatch.EntityA ? 0f : MathF.PI),
            MatchState = MatchState.Playing,
        };
        sim.RegisterEntity(id, def, state, baked);
        sim.SetRespawnPosition(id, x, py, 0f, state.FacingYaw);
    }

    private static void RecordInputTrace(List<TickTrace> trace, int tick, ulong id, string policy,
        in CharacterState pre, in InputState input, PolicyController controller,
        CharacterDefinition def, in ArenaDefinition arena)
    {
        bool emergency = input.Jump && PolicyHelpers.IsEmergencyRecovery(pre, def, in arena);
        bool voluntary = input.Jump && pre.IsGrounded && !emergency;
        var state = new TickTrace
        {
            Tick = tick,
            EntityId = id,
            Policy = policy,
            Input = input,
            Pre = Snapshot(pre),
            Post = null!,
            EmergencyRecoveryInput = emergency,
            VoluntaryJumpInput = voluntary,
            ApproachFromDelayedObservation = controller.LastApproach,
            DelayedObservationAvailable = controller.LastDelayedObservationAvailable,
            WrapperJumpOverride = controller.LastJumpOverride,
            WrapperJumpSuppressed = controller.LastJumpSuppressed,
            HorizontalSpeed = MathF.Sqrt(pre.VX * pre.VX + pre.VZ * pre.VZ),
            SpeedBucket = SpeedBucket(MathF.Sqrt(pre.VX * pre.VX + pre.VZ * pre.VZ)),
            JumpPhase = JumpPhase(pre),
        };
        trace.Add(state);
    }

    private static void SetPostTrace(List<TickTrace> trace, int tick, ulong id, in CharacterState post)
    {
        for (int i = trace.Count - 1; i >= 0; i--)
            if (trace[i].Tick == tick && trace[i].EntityId == id)
            {
                trace[i].Post = Snapshot(post);
                return;
            }
        throw new InvalidOperationException("missing pre-tick trace");
    }

    private static void TrackJumpInput(List<JumpObservation> all, List<JumpObservation> pending,
        ulong id, string policy, int tick, in CharacterState pre, in InputState input,
        PolicyController controller, CharacterDefinition def, in ArenaDefinition arena)
    {
        if (!input.Jump) return;
        bool emergency = PolicyHelpers.IsEmergencyRecovery(pre, def, in arena);
        var jump = new JumpObservation
        {
            EntityId = id,
            Policy = policy,
            InputTick = tick,
            InputGrounded = pre.IsGrounded,
            EmergencyRecovery = emergency,
            Voluntary = pre.IsGrounded && !emergency,
            Approach = controller.LastApproach,
        };
        pending.Add(jump);
        all.Add(jump);
    }

    private static void CompleteJumpTransitions(List<JumpObservation> pending, int tick,
        in CharacterState pre, in CharacterState post, IReadOnlyList<ServerSimulation.DeathEvent> deaths)
    {
        byte postDeaths = post.Deaths;
        ulong postEntityId = post.EntityId;
        bool died = postDeaths > pre.Deaths || deaths.Any(x => x.EntityId == postEntityId);
        foreach (var jump in pending)
        {
            if (jump.TakeoffTick < 0 && !died && !post.IsGrounded
                && (pre.IsGrounded || post.JumpsLeft < pre.JumpsLeft))
            {
                jump.TakeoffTick = tick;
                jump.SuccessfulTakeoff = true;
                jump.Outcome = "successful-takeoff";
            }
            if (jump.SuccessfulTakeoff && jump.LandingTick < 0 && !died
                && !pre.IsGrounded && post.IsGrounded)
            {
                jump.LandingTick = tick;
                jump.Outcome = "landed";
            }
        }
        pending.RemoveAll(x => x.LandingTick >= 0 || x.InputTick + 120 < tick);
    }

    private static List<AerialAttack> BuildAerialAttacks(CharacterDefinition def, MatchReport report)
    {
        var byTrace = report.Trace.ToDictionary(x => (x.Tick, x.EntityId));
        var result = new List<AerialAttack>();
        foreach (var swing in report.Simulation.Swings)
        {
            if (!swing.Accepted || !swing.Air) continue;
            var trace = byTrace.TryGetValue((swing.StartTick, swing.Attacker), out var found) ? found : null;
            var move = MoveIdentity(def, swing.ActiveSlot, air: true);
            bool authoredRecovery, authoredDamage;
            string intent = ClassifyAerial(def, swing.ActiveSlot, out authoredRecovery, out authoredDamage);
            var attack = new AerialAttack
            {
                EntityId = swing.Attacker,
                Policy = PolicyForEntity(report, swing.Attacker),
                ActiveSlot = swing.ActiveSlot,
                MoveId = move.Id,
                MoveName = move.Name,
                ActivationId = swing.ActivationId,
                StartTick = swing.StartTick,
                Connected = swing.Connected,
                Intent = intent,
                AuthoredRecoveryMove = authoredRecovery,
                AuthoredDamage = authoredDamage,
                StartSpeed = trace?.HorizontalSpeed ?? 0f,
                SpeedBucket = trace?.SpeedBucket ?? SpeedBucket(0f),
                JumpPhase = trace?.JumpPhase ?? "unknown",
            };
            foreach (var hit in report.Simulation.Hits.Where(x => x.ActivationId == swing.ActivationId))
            {
                attack.HitTicks.Add(hit.Tick);
                attack.HitRelations.Add(AirRelation(hit, report));
            }
            result.Add(attack);
        }
        return result;
    }

    private static List<ActivationConnection> BuildActivations(CharacterDefinition def, MatchReport report)
    {
        var result = new Dictionary<ulong, ActivationConnection>();
        foreach (var swing in report.Simulation.Swings.Where(x => x.Accepted && x.ActivationId != 0))
        {
            if (!result.TryGetValue(swing.ActivationId, out var activation))
            {
                var move = MoveIdentity(def, swing.ActiveSlot, swing.Air);
                string intent = ClassifyAerial(def, swing.ActiveSlot, out bool recovery, out bool damage);
                activation = new ActivationConnection
                {
                    ActivationId = swing.ActivationId,
                    EntityId = swing.Attacker,
                    ActiveSlot = swing.ActiveSlot,
                    MoveId = move.Id,
                    MoveName = move.Name,
                    Air = swing.Air,
                    Intent = intent,
                    AuthoredRecoveryMove = recovery,
                    AuthoredDamage = damage,
                    Accepted = true,
                    StartTick = swing.StartTick,
                };
                result.Add(swing.ActivationId, activation);
            }
        }
        foreach (var hit in report.Simulation.Hits.Where(x => x.ActivationId != 0))
        {
            if (!result.TryGetValue(hit.ActivationId, out var activation))
            {
                var move = MoveIdentity(def, hit.AttackSlot, hit.Air);
                string intent = ClassifyAerial(def, hit.AttackSlot, out bool recovery, out bool damage);
                activation = new ActivationConnection
                {
                    ActivationId = hit.ActivationId,
                    EntityId = hit.Attacker,
                    ActiveSlot = hit.AttackSlot,
                    MoveId = move.Id,
                    MoveName = move.Name,
                    Air = hit.Air,
                    Intent = intent,
                    AuthoredRecoveryMove = recovery,
                    AuthoredDamage = damage,
                };
                result.Add(hit.ActivationId, activation);
            }
            activation.Connected = true;
            if (!activation.Targets.Contains(hit.Target)) activation.Targets.Add(hit.Target);
            activation.HitTicks.Add(hit.Tick);
            activation.HitRelations.Add(AirRelation(hit, report));
        }
        return result.Values.OrderBy(x => x.StartTick < 0 ? int.MaxValue : x.StartTick).ThenBy(x => x.ActivationId).ToList();
    }

    private static List<JumpInWindow> BuildJumpInWindows(MatchReport report)
    {
        var result = new List<JumpInWindow>();
        foreach (var jump in report.Jumps.Where(x => x.Voluntary && x.Approach && x.SuccessfulTakeoff))
        {
            int start = jump.TakeoffTick;
            int end = Math.Min(report.Simulation.DurationTicks - 1, start + AttributionWindowTicks);
            if (end < start) continue;
            ulong target = jump.EntityId == SelfPlayMatch.EntityA ? SelfPlayMatch.EntityB : SelfPlayMatch.EntityA;
            var window = new JumpInWindow
            {
                JumperEntityId = jump.EntityId,
                TargetEntityId = target,
                JumperPolicy = jump.Policy,
                TargetPolicy = PolicyForEntity(report, target),
                JumpInputTick = jump.InputTick,
                TakeoffTick = jump.TakeoffTick,
                StartTick = start,
                EndTick = end,
                WindowTicks = end - start + 1,
            };
            foreach (var hit in report.Simulation.Hits.Where(x => x.Tick >= start && x.Tick <= end))
            {
                if (hit.Attacker == jump.EntityId)
                {
                    window.JumperDamageDealt += hit.Damage;
                    window.JumperHitsDealt++;
                    window.TargetDamageTaken += hit.Damage;
                    window.TargetHitsTaken++;
                }
                else if (hit.Target == jump.EntityId)
                {
                    window.JumperDamageTaken += hit.Damage;
                    window.JumperHitsTaken++;
                    window.TargetDamageDealt += hit.Damage;
                    window.TargetHitsDealt++;
                }
            }
            foreach (var death in report.Simulation.Deaths.Where(x => x.Tick >= (uint)start && x.Tick <= (uint)end))
            {
                if (death.EntityId == jump.EntityId) window.JumperDeathsInWindow++;
                if (death.EntityId == target) window.TargetDeathsInWindow++;
                if (death.EntityId == target && death.KillerEntityId == jump.EntityId)
                    window.KillerMatchesInWindow++;
            }
            result.Add(window);
        }
        return result;
    }
    private static Dictionary<ulong, TemporalTotals> BuildUnionTemporal(MatchReport report)
    {
        var intervalsByEntity = new Dictionary<ulong, List<TickRange>>();
        void AddInterval(ulong entity, int start, int end)
        {
            if (!intervalsByEntity.TryGetValue(entity, out var intervals))
            {
                intervals = new List<TickRange>();
                intervalsByEntity.Add(entity, intervals);
            }
            intervals.Add(new TickRange(start, end));
        }

        foreach (var window in report.JumpInWindows)
        {
            AddInterval(window.JumperEntityId, window.StartTick, window.EndTick);
            AddInterval(window.TargetEntityId, window.StartTick, window.EndTick);
        }

        var result = new Dictionary<ulong, TemporalTotals>();
        foreach (var (entity, intervals) in intervalsByEntity)
        {
            var merged = MergeRanges(intervals);
            var total = new TemporalTotals();
            total.ExposureTicks = merged.Sum(x => x.End - x.Start + 1);
            foreach (var hit in report.Simulation.Hits)
            {
                if (!ContainsTick(merged, hit.Tick)) continue;
                if (hit.Attacker == entity && hit.Target == entity)
                {
                    total.SelfHitEvents++;
                    total.SelfHitDamage += hit.Damage;
                }
                if (hit.Attacker == entity)
                {
                    total.DamageDealt += hit.Damage;
                    total.HitsDealt++;
                }
                if (hit.Target == entity)
                {
                    total.DamageTaken += hit.Damage;
                    total.HitsTaken++;
                }
            }
            foreach (var death in report.Simulation.Deaths)
            {
                if (death.EntityId == entity && ContainsTick(merged, (int)death.Tick))
                    total.Deaths++;
            }
            result.Add(entity, total);
        }
        return result;
    }

    private static List<TickRange> MergeRanges(List<TickRange> ranges)
    {
        ranges.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        var merged = new List<TickRange>(ranges.Count);
        foreach (var range in ranges)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End + 1)
            {
                merged.Add(range);
                continue;
            }
            var last = merged[^1];
            if (range.End > last.End)
                merged[^1] = new TickRange(last.Start, range.End);
        }
        return merged;
    }

    private static bool ContainsTick(IReadOnlyList<TickRange> ranges, int tick)
    {
        foreach (var range in ranges)
        {
            if (tick < range.Start) return false;
            if (tick <= range.End) return true;
        }
        return false;
    }


    private static string AirRelation(HitEvent hit, MatchReport report)
    {
        var target = report.Trace.FirstOrDefault(x => x.Tick == hit.Tick && x.EntityId == hit.Target);
        if (!hit.Air) return target?.Pre.IsGrounded == true ? "ground-to-ground" : "ground-to-air";
        return target?.Pre.IsGrounded == true ? "air-to-ground" : "air-to-air";
    }

    private static string ClassifyAerial(CharacterDefinition def, byte slot, out bool recovery, out bool damage)
    {
        var cooked = def.GetCookedSlotAbility(slot, airborne: true);
        recovery = cooked?.IsRecoveryMove == true;
        damage = cooked?.Timeline.Stages.SelectMany(x => x.Operations).Any(HasDamage) == true;
        if (recovery) return "recovery";
        if (damage) return "offensive";
        return "ambiguous";
    }

    private static bool HasDamage(CookedTimelineOperation operation)
    {
        return operation switch
        {
            CookedSpawnHitboxOperation x => x.Hitbox.Damage > 0f,
            CookedSpawnProjectileOperation x => x.Projectile.Damage > 0f,
            CookedStartCapabilityOperation x => x.Parameters switch
            {
                CookedCycloneKickCapabilityParameters p => p.Damage > 0f,
                CookedTargetedLeapCapabilityParameters p => p.Hitbox.Damage > 0f,
                CookedMankiRoundBombCapabilityParameters p => p.ExplosionDamage > 0f,
                CookedMankiJetpackBoostCapabilityParameters p => p.ExplosionDamage > 0f,
                CookedMankiBazookaCapabilityParameters p => p.Damage > 0f,
                _ => false,
            },
            _ => false,
        };
    }

    private static (string Id, string Name) MoveIdentity(CharacterDefinition def, byte slot, bool air)
    {
        var cooked = def.GetCookedSlotAbility(slot, air);
        if (cooked != null) return (cooked.Id, cooked.Name);
        return ("unknown", "unknown");
    }

    private static StateTrace Snapshot(in CharacterState s) => new()
    {
        PX = s.PX,
        PY = s.PY,
        PZ = s.PZ,
        VX = s.VX,
        VY = s.VY,
        VZ = s.VZ,
        IsGrounded = s.IsGrounded,
        JumpsLeft = s.JumpsLeft,
        State = s.State,
        AttackSlot = s.AttackSlot,
        AttackSequence = s.AttackSequence,
        Deaths = s.Deaths,
        DamagePercent = s.DamagePercent,
        HitstunTicks = s.HitstunTicks,
        HitstopTicks = s.HitstopTicks,
        AnimLockTicks = s.AnimLockTicks,
        LandingLagTicks = s.LandingLagTicks,
    };

    private static string JumpPhase(in CharacterState state)
    {
        if (state.IsGrounded) return state.State == ActionState.JumpSquat ? "jump-squat" : "grounded";
        if (state.VY > 0.5f) return "rise";
        if (state.VY < -0.5f) return "fall";
        return "apex-or-float";
    }

    private static string SpeedBucket(float speed)
        => speed < 1f ? "0-1" : speed < 3f ? "1-3" : speed < 6f ? "3-6" : "6+";

    private static void ValidateSelfCheck(MatchReport report)
    {
        if (report.ReactionDelayTicks != NormalReactionDelayTicks)
            throw new InvalidOperationException("Normal reaction delay changed in experiment metadata");
        var traces = report.Trace.ToDictionary(x => (x.Tick, x.EntityId));
        foreach (var jump in report.Jumps)
        {
            if (!traces.TryGetValue((jump.InputTick, jump.EntityId), out var inputTrace) || !inputTrace.Input.Jump)
                throw new InvalidOperationException("jump telemetry does not point to a Jump input");
            if (jump.SuccessfulTakeoff)
            {
                if (jump.TakeoffTick < 0 || !traces.TryGetValue((jump.TakeoffTick, jump.EntityId), out var takeoff))
                    throw new InvalidOperationException("successful jump has no takeoff trace");
                if (takeoff.Post.IsGrounded)
                    throw new InvalidOperationException("successful jump did not actually leave the ground");
            }
        }
        foreach (var window in report.JumpInWindows)
        {
            if (window.StartTick != window.TakeoffTick || window.EndTick < window.StartTick || window.WindowTicks <= 0)
                throw new InvalidOperationException("invalid jump-in attribution window");
            if (!report.Jumps.Any(x => x.EntityId == window.JumperEntityId && x.TakeoffTick == window.TakeoffTick && x.SuccessfulTakeoff))
                throw new InvalidOperationException("attribution window has no successful takeoff");
            if (!string.Equals(window.Attribution, "temporal association only; not causal attribution", StringComparison.Ordinal))
                throw new InvalidOperationException("attribution window lost its non-causal label");
        }
        foreach (var activation in report.Activations)
        {
            if (activation.Connected && activation.ActivationId == 0)
                throw new InvalidOperationException("connected activation has no server identity");
        }
    }

    private static string Fingerprint(MatchReport report)
    {
        var payload = new
        {
            report.Character,
            report.PackageId,
            report.Scenario,
            report.PolicyA,
            report.PolicyB,
            report.Seed,
            report.SideSwapped,
            Simulation = report.Simulation,
            report.Trace,
            report.Jumps,
            report.AerialAttacks,
            report.Activations,
            report.JumpInWindows,
        };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static RosterProvenance ResolveProvenance(CharacterClass selector)
    {
        var entry = BuiltInContentResolver.Resolve(selector);
        if (entry.CookedCharacterPackage == null || entry.BakedAnimation == null)
            throw new InvalidDataException($"{selector}: resolver did not return cooked roster+baked data");
        return new RosterProvenance
        {
            Character = entry.Identity.PackageId,
            PackageId = entry.Identity.PackageId,
            Version = entry.Identity.Version,
            SourceHash = entry.Identity.SourceHash,
            CookedContentHash = entry.Identity.CookedContentHash,
            PackageHash = entry.Identity.PackageHash,
            CookedPackageLoaded = true,
            BakedAnimationLoaded = true,
            BakedBoneCount = entry.BakedAnimation.BoneNames?.Count ?? 0,
            BakedAnimationCount = entry.BakedAnimation.Animations?.Count ?? 0,
        };
    }

    private static CharacterClass ToCharacterClass(string packageId)
        => packageId.ToLowerInvariant() switch
        {
            "manki" => CharacterClass.Manki,
            "fightguy" => CharacterClass.FightGuy,
            "wibou" => CharacterClass.Wibou,
            "bonk" => CharacterClass.Bonk,
            _ => throw new InvalidDataException($"unknown admitted package '{packageId}'"),
        };

    private static string BuildAggregateCsv(IEnumerable<AggregateRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("character,scenario,policy_pair,policy,source_hash,cooked_content_hash,package_hash,difficulty,reaction_delay_ticks,stocks,max_ticks,matches,side_swapped_matches,timed_out_matches,avg_duration_ticks,entity_minutes,temporal_union_entity_minutes,jump_inputs,voluntary_jump_inputs,emergency_recovery_jump_inputs,successful_takeoffs,aerial_accepted,aerial_connected_activations,aerial_offensive_connected_activations,aerial_offensive,aerial_recovery,aerial_ambiguous,air_to_ground_hits,air_to_air_hits,damage_dealt,damage_taken,deaths,self_hit_events,self_hit_damage,temporal_damage_dealt,temporal_damage_taken,temporal_hits_dealt,temporal_hits_taken,temporal_deaths,temporal_self_hit_events,temporal_self_hit_damage");
        foreach (var row in rows.OrderBy(x => x.Character, StringComparer.Ordinal).ThenBy(x => x.Scenario, StringComparer.Ordinal).ThenBy(x => x.Policy, StringComparer.Ordinal))
        {
            var c = row.Counter;
            string[] fields =
            {
                row.Character,
                row.Scenario,
                row.PolicyPair,
                row.Policy,
                row.SourceHash,
                row.CookedContentHash,
                row.PackageHash,
                BotDifficultyProfile.DisplayName(CpuDifficulty.Normal),
                BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks.ToString(CultureInfo.InvariantCulture),
                Stocks.ToString(CultureInfo.InvariantCulture),
                MaxTicks.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                row.SideSwappedMatches.ToString(CultureInfo.InvariantCulture),
                row.TimedOutMatches.ToString(CultureInfo.InvariantCulture),
                (row.Matches == 0 ? 0d : row.DurationTicks / (double)row.Matches).ToString("0.###", CultureInfo.InvariantCulture),
                row.EntityMinutes.ToString("0.###", CultureInfo.InvariantCulture),
                row.TemporalUnionEntityMinutes.ToString("0.###", CultureInfo.InvariantCulture),
                c.JumpInputs.ToString(CultureInfo.InvariantCulture),
                c.VoluntaryJumpInputs.ToString(CultureInfo.InvariantCulture),
                c.EmergencyRecoveryInputs.ToString(CultureInfo.InvariantCulture),
                c.SuccessfulTakeoffs.ToString(CultureInfo.InvariantCulture),
                c.AerialAccepted.ToString(CultureInfo.InvariantCulture),
                c.AerialConnectedActivations.ToString(CultureInfo.InvariantCulture),
                c.AerialOffensiveConnectedActivations.ToString(CultureInfo.InvariantCulture),
                c.AerialOffensive.ToString(CultureInfo.InvariantCulture),
                c.AerialRecovery.ToString(CultureInfo.InvariantCulture),
                c.AerialAmbiguous.ToString(CultureInfo.InvariantCulture),
                c.AirToGroundHits.ToString(CultureInfo.InvariantCulture),
                c.AirToAirHits.ToString(CultureInfo.InvariantCulture),
                c.DamageDealt.ToString("0.###", CultureInfo.InvariantCulture),
                c.DamageTaken.ToString("0.###", CultureInfo.InvariantCulture),
                c.Deaths.ToString(CultureInfo.InvariantCulture),
                c.SelfHitEvents.ToString(CultureInfo.InvariantCulture),
                c.SelfHitDamage.ToString("0.###", CultureInfo.InvariantCulture),
                c.TemporalDamageDealt.ToString("0.###", CultureInfo.InvariantCulture),
                c.TemporalDamageTaken.ToString("0.###", CultureInfo.InvariantCulture),
                c.TemporalHitsDealt.ToString(CultureInfo.InvariantCulture),
                c.TemporalHitsTaken.ToString(CultureInfo.InvariantCulture),
                c.TemporalDeaths.ToString(CultureInfo.InvariantCulture),
                c.TemporalSelfHitEvents.ToString(CultureInfo.InvariantCulture),
                c.TemporalSelfHitDamage.ToString("0.###", CultureInfo.InvariantCulture),
            };
            sb.AppendLine(string.Join(",", fields.Select(Csv).ToArray()));
        }
        return sb.ToString();
    }

    private static string Csv(string value)
        => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string PolicyForEntity(MatchReport report, ulong entity)
        => entity == SelfPlayMatch.EntityA ? report.PolicyA : report.PolicyB;

    private static string PolicyName(PolicyKind kind) => kind.ToString().ToLowerInvariant();
    private static string Name(PolicyKind kind) => PolicyName(kind);

    private static int[] ParseSeeds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (int[])DefaultSeeds.Clone();
        var values = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (values.Length == 0) throw new ArgumentException("--seeds requires at least one integer seed");
        return values;
    }

    private static string? ParseArg(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool HasFlag(string[] args, string key) => Array.IndexOf(args, key) >= 0;

    private static ArenaDefinition BuildKillArena()
    {
        const int width = 60, height = 60;
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy (Crossroads-style 60x60)",
            KillHeight = -10f,
            MinX = -30f,
            MaxX = 30f,
            MinZ = -30f,
            MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0f, Y = 0f, Z = 0f, Yaw = 0f } },
            Heightmap = new ArenaHeightmap
            {
                Data = new float[width * height],
                Width = width,
                Height = height,
                CellSize = 1f,
                OriginX = -30f,
                OriginZ = -30f,
            },
        };
    }

    private static class PolicyHelpers
    {
        public static bool IsEmergencyRecovery(in CharacterState state, CharacterDefinition def, in ArenaDefinition arena)
        {
            if (state.State == ActionState.LedgeHang) return true;
            if (!(arena.MaxX > arena.MinX && arena.MaxZ > arena.MinZ)) return false;
            if (state.PX < arena.MinX - 0.35f || state.PX > arena.MaxX + 0.35f
                || state.PZ < arena.MinZ - 0.35f || state.PZ > arena.MaxZ + 0.35f)
                return true;
            // Grounded policy only needs to preserve explicit ledge/recovery states. A
            // grounded state without support will be transitioned by the authoritative sim;
            // do not call the Shared-internal ledge helper from this standalone tool.
            return false;
        }
    }
}
