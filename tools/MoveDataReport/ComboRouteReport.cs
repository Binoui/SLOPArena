using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared;

namespace SlopArena.MoveDataReport;

/// <summary>Explicit-input experiments. No chase policy or copied gameplay admission rules.</summary>
internal static partial class ComboRouteReport
{
    internal const ulong AttackerId = 1, DefenderId = 100;
    internal const int MaxHorizon = 600, MaxBudget = 100000;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IncludeFields = true,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly string SharedHash = AssemblyHash(typeof(Simulation).Assembly.Location);
    private static readonly string ToolHash = AssemblyHash(typeof(ComboRouteReport).Assembly.Location);

    internal sealed record SimulationIdentity(string SharedHash, string ToolHash, float KnockbackScale,
        float HitstunCoefficient, float HitstunBonus);
    internal sealed class Context
    {
        public MatchContentEntry Attacker { get; }
        public MatchContentEntry Defender { get; }
        public ArenaDefinition Arena { get; }
        public SimulationIdentity Identity { get; }
        public CombatTuning CombatTuning { get; }
        public Context(MatchContentEntry attacker, MatchContentEntry? defender = null, ArenaDefinition? arena = null,
            CombatTuning? combatTuning = null)
        {
            Attacker = attacker;
            Defender = defender ?? attacker;
            Arena = arena ?? Program.BuildArena();
            ValidateHeightmap(Arena.Heightmap);
            CombatTuning = combatTuning ?? SlopArena.Shared.CombatTuning.Default;
            Identity = CurrentSimulationIdentity(CombatTuning);
        }
        public static Context FightGuy() => new(Program.ResolveEntry("fightguy"));
    }

    internal sealed class SearchOptions
    {
        public int Budget { get; set; } = 3000;
        public int MeasurementBudget { get; set; } = 1000;
        public int Horizon { get; set; } = 240;
        public int Keep { get; set; } = 6;
        public int PrefixWidth { get; set; } = 8;
        public int TimingRadius { get; set; } = 4;
        public int[] Percents { get; set; } = new[] { 0, 30, 60 };
        public float[] Distances { get; set; } = new[] { 0.8f, 1.2f, 1.6f };
        public string[] DiDirections { get; set; } = new[] { "neutral", "in", "away", "left", "right" };
        public void Validate()
        {
            if (Budget < 1 || Budget > MaxBudget || MeasurementBudget < 1 || MeasurementBudget > MaxBudget)
                throw new ArgumentException($"Budgets must be in [1, {MaxBudget}].");
            if (Horizon < 30 || Horizon > MaxHorizon || Keep < 1 || Keep > 32 || PrefixWidth < 1 || PrefixWidth > 32)
                throw new ArgumentException($"Horizon must be [30, {MaxHorizon}]; keep/prefix-width must be [1, 32].");
            if (TimingRadius < 0 || TimingRadius > 20 || Percents.Length == 0 || Percents.Length > 16
                || Percents.Any(p => p < 0 || p > 999) || Distances.Length == 0 || Distances.Length > 16
                || Distances.Any(d => !float.IsFinite(d) || d < 0.1f || d > 10f))
                throw new ArgumentException("Invalid timing radius, damage buckets or distances.");
            if (DiDirections.Length == 0 || DiDirections.Length > 9 || DiDirections.Distinct().Count() != DiDirections.Length)
                throw new ArgumentException("Provide 1–9 distinct DI directions.");
            foreach (string direction in DiDirections) DiVector(direction);
        }
    }

    internal sealed class PlannedAction
    {
        public int Tick { get; set; }
        public string Slot { get; set; } = "ground.1";
    }
    internal sealed class Replay
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "route";
        public MatchContentIdentity AttackerContent { get; set; } = null!;
        public MatchContentIdentity DefenderContent { get; set; } = null!;
        public SimulationIdentity Simulation { get; set; } = null!;
        public string Rule { get; set; } = "stock-3-stop-on-death";
        [JsonConverter(typeof(FlatArenaConverter))]
        public ArenaDefinition Arena { get; set; }
        public CharacterState InitialAttacker { get; set; }
        public CharacterState InitialDefender { get; set; }
        public InputState[] AttackerInputs { get; set; } = Array.Empty<InputState>();
        public InputState[] DefenderInputs { get; set; } = Array.Empty<InputState>();
        public List<PlannedAction> Actions { get; set; } = new();
        public string? ExpectedOutcomeHash { get; set; }
    }

    // The declared experiment arena is flat. Encode its surface once, not 40,000
    // identical samples in every witness and robustness comparison.
    internal sealed record FlatArenaData(ArenaDefinition Definition, float SurfaceHeight);
    internal sealed class FlatArenaConverter : JsonConverter<ArenaDefinition>
    {
        public override ArenaDefinition Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var data = JsonSerializer.Deserialize<FlatArenaData>(ref reader, options)
                ?? throw new JsonException("Missing flat arena definition.");
            var arena = data.Definition;
            var map = arena.Heightmap;
            if (map.Width < 2 || map.Height < 2 || map.Width > 1000 || map.Height > 1000
                || !float.IsFinite(data.SurfaceHeight) || arena.CollisionTriangles?.Length > 0)
                throw new JsonException("Invalid flat experiment arena.");
            map.Data = new float[map.Width * map.Height];
            if (data.SurfaceHeight != 0f) Array.Fill(map.Data, data.SurfaceHeight);
            arena.Heightmap = map;
            ValidateHeightmap(map);
            return arena;
        }
        public override void Write(Utf8JsonWriter writer, ArenaDefinition arena, JsonSerializerOptions options)
        {
            var map = arena.Heightmap;
            ValidateHeightmap(map);
            float height = map.Data[0];
            if (arena.CollisionTriangles?.Length > 0 || map.Data.Any(value => value != height))
                throw new JsonException("Route replay currently supports only the declared flat arena.");
            map.Data = Array.Empty<float>();
            arena.Heightmap = map;
            JsonSerializer.Serialize(writer, new FlatArenaData(arena, height), options);
        }
    }
    internal sealed class ActionObservation
    {
        public ulong EntityId { get; set; }
        public int Tick { get; set; }
        public string? ExpectedSlot { get; set; }
        public byte RequestedWireSlot { get; set; }
        public string? ActualSlot { get; set; }
        public ulong ActivationId { get; set; }
        public string Status { get; set; } = "rejected";
        public int Contacts { get; set; }
        public float Damage { get; set; }
    }
    internal sealed record Contact(int Tick, uint MatchTick, ulong Attacker, ulong Defender,
        ulong ActivationId, string Slot, float Damage, bool Blocked);
    internal sealed record StateSample(float X, float Y, float Z, float Vx, float Vy, float Vz,
        bool Grounded, string State, ushort Hitstop, ushort Hitstun, ushort LandingLag, bool FastFall, ushort AnimLock = 0);
    internal sealed record Frame(int Tick, StateSample Attacker, StateSample Defender, bool DefenderCanAct);
    internal sealed record Death(int Tick, ulong EntityId);
    internal sealed class Outcome
    {
        public int ObservedTicks { get; set; }
        public bool RouteSucceeded { get; set; }
        public string Verdict { get; set; } = "no-contact";
        public int ConnectedActions { get; set; }
        public int ContactCount { get; set; }
        public float Damage { get; set; }
        public int? FirstContactTick { get; set; }
        public int? LastContactTick { get; set; }
        public int ConversionTicks { get; set; }
        public int LongestNoActionTicks { get; set; }
        public bool NoActionWindowClipped { get; set; }
        public StateSample FinalAttacker { get; set; } = null!;
        public StateSample FinalDefender { get; set; } = null!;
        public List<ActionObservation> Actions { get; set; } = new();
        public List<Contact> Contacts { get; set; } = new();
        public List<int> DefenderActionTicks { get; set; } = new();
        public List<int> GapTicks { get; set; } = new();
        public List<int> AttackerActionTicks { get; set; } = new();
        public List<int> TakeoffTicks { get; set; } = new();
        public List<int> LandingTicks { get; set; } = new();
        public List<int> FastFallTicks { get; set; } = new();
        public List<Death> Deaths { get; set; } = new();
        public List<Frame> Frames { get; set; } = new();
    }
    internal sealed class Execution
    {
        public Outcome Outcome { get; init; } = null!;
        public InputState[]? GeneratedDefenderInputs { get; init; }
    }
    internal sealed record TimingSample(int Tick, bool Success, string Reason, float Damage);
    internal sealed record TimingInterval(int StartTick, int EndTick);
    internal sealed class TimingMeasurement
    {
        public int ActionIndex { get; set; }
        public int BaselineTick { get; set; }
        public bool Complete { get; set; }
        public bool BoundaryClipped { get; set; }
        public List<TimingSample> Samples { get; set; } = new();
        public List<TimingInterval> Intervals { get; set; } = new();
    }
    internal sealed class Witness
    {
        public string Id { get; set; } = "";
        public string Di { get; set; } = "neutral";
        public string Grammar { get; set; } = "";
        public Replay Replay { get; set; } = null!;
        public Outcome Outcome { get; set; } = null!;
        public List<TimingMeasurement> Timing { get; set; } = new();
    }
    internal sealed class DiComparison
    {
        public string WitnessId { get; set; } = "";
        public string Di { get; set; } = "neutral";
        public bool Complete { get; set; }
        public Replay? Replay { get; set; }
        public Outcome? Outcome { get; set; }
    }
    internal sealed class SearchReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string Meaning { get; set; } = "Best found in the declared sampled grammar; not optimality, impossibility or balance.";
        public SearchOptions Options { get; set; } = null!;
        public string[] Grammar { get; set; } = Array.Empty<string>();
        public string[] Limitations { get; set; } = Array.Empty<string>();
        public int CandidatesRun { get; set; }
        public int MeasurementsRun { get; set; }
        public bool BudgetExhausted { get; set; }
        public bool MeasurementBudgetExhausted { get; set; }
        public Dictionary<string, int> FailureCounts { get; set; } = new();
        public Dictionary<string, int> GrammarAttempts { get; set; } = new();
        public Dictionary<string, int> ConditionAttempts { get; set; } = new();
        public List<Witness> Witnesses { get; set; } = new();
        public List<DiComparison> FixedRouteDi { get; set; } = new();
        public Dictionary<string, string?> DiConditionedBest { get; set; } = new();
    }
    internal sealed class ReplayReport
    {
        public Replay Replay { get; set; } = null!;
        public Outcome Outcome { get; set; } = null!;
        public bool? MatchesExpected { get; set; }
    }

    internal static Replay CreateReplay(Context context, CharacterState attacker, CharacterState defender,
        InputState[] attackerInputs, InputState[] defenderInputs, IEnumerable<PlannedAction> actions, string id = "route")
        => new()
        {
            Id = id, AttackerContent = context.Attacker.Identity, DefenderContent = context.Defender.Identity,
            Simulation = context.Identity, Arena = context.Arena, InitialAttacker = attacker, InitialDefender = defender,
            AttackerInputs = attackerInputs, DefenderInputs = defenderInputs,
            Actions = actions.Select(a => new PlannedAction { Tick = a.Tick, Slot = a.Slot }).ToList(),
        };

    internal static Replay CopyReplay(Replay source, bool copyInputs = true) => new()
    {
        SchemaVersion = source.SchemaVersion, Id = source.Id, AttackerContent = source.AttackerContent,
        DefenderContent = source.DefenderContent, Simulation = source.Simulation, Rule = source.Rule,
        Arena = source.Arena, InitialAttacker = source.InitialAttacker, InitialDefender = source.InitialDefender,
        AttackerInputs = copyInputs ? (InputState[])source.AttackerInputs.Clone() : source.AttackerInputs,
        DefenderInputs = copyInputs ? (InputState[])source.DefenderInputs.Clone() : source.DefenderInputs,
        Actions = source.Actions.Select(a => new PlannedAction { Tick = a.Tick, Slot = a.Slot }).ToList(),
    };

    internal static Execution Execute(Context context, Replay replay, string? generatedDi = null, bool captureFrames = false)
    {
        ValidateReplay(context, replay);
        var di = generatedDi == null ? (0f, 0f) : DiVector(generatedDi);
        var generated = generatedDi == null ? null : new InputState[replay.DefenderInputs.Length];
        var sim = new ServerSimulation(replay.Arena, combatTuning: context.CombatTuning);
        sim.RegisterEntity(AttackerId, context.Attacker.Definition, replay.InitialAttacker, context.Attacker.BakedAnimation);
        sim.RegisterEntity(DefenderId, context.Defender.Definition, replay.InitialDefender, context.Defender.BakedAnimation);
        var inputs = new Dictionary<ulong, InputState>(2);
        var outcome = new Outcome();
        var byActivation = new Dictionary<ulong, ActionObservation>();
        int actionIndex = 0;
        int noActionRun = 0;
        var lastAttacker = replay.InitialAttacker;
        var lastDefender = replay.InitialDefender;
        for (int tick = 0; tick < replay.AttackerInputs.Length; tick++)
        {
            var attackerInput = replay.AttackerInputs[tick];
            var defenderInput = replay.DefenderInputs[tick];
            var beforeAttacker = sim.GetState(AttackerId);
            var beforeDefender = sim.GetState(DefenderId);
            if (generated != null)
            {
                bool influence = beforeDefender.HitstopTicks > 0 || beforeDefender.HitstunTicks > 0;
                defenderInput.MoveX = influence ? di.Item1 : 0f;
                defenderInput.MoveY = influence ? di.Item2 : 0f;
                generated[tick] = defenderInput;
            }
            ulong previousAttacker = sim.GetLastActivationId(AttackerId);
            ulong previousDefender = sim.GetLastActivationId(DefenderId);
            inputs[AttackerId] = attackerInput;
            inputs[DefenderId] = defenderInput;
            sim.Tick(inputs);
            string? expected = null;
            if (actionIndex < replay.Actions.Count && replay.Actions[actionIndex].Tick == tick)
                expected = replay.Actions[actionIndex++].Slot;
            ObserveAction(sim, AttackerId, tick, attackerInput.ActiveSlot, previousAttacker, expected, outcome, byActivation);
            ObserveAction(sim, DefenderId, tick, defenderInput.ActiveSlot, previousDefender, null, outcome, byActivation);
            bool canAct = sim.LastTickOrdinaryActionOpportunities.Contains(DefenderId);
            if (canAct) outcome.DefenderActionTicks.Add(tick);
            if (sim.LastTickOrdinaryActionOpportunities.Contains(AttackerId)) outcome.AttackerActionTicks.Add(tick);
            foreach (var hit in sim.LastTickHits)
            {
                var contact = new Contact(tick, hit.MatchTick, hit.OwnerEntityId, hit.TargetEntityId,
                    hit.ActivationId, CanonicalSlot(hit.AttackSlot, hit.Airborne), hit.Damage, hit.Blocked);
                outcome.Contacts.Add(contact);
                if (hit.Blocked || hit.Damage <= 0 || hit.OwnerEntityId != AttackerId || hit.TargetEntityId != DefenderId)
                    continue;
                outcome.ContactCount++;
                outcome.Damage += hit.Damage;
                outcome.FirstContactTick ??= tick;
                outcome.LastContactTick = tick;
                if (byActivation.TryGetValue(hit.ActivationId, out var action))
                {
                    action.Contacts++;
                    action.Damage += hit.Damage;
                    if (action.ActualSlot == null)
                    {
                        action.ActualSlot = contact.Slot;
                        action.Status = action.ExpectedSlot == null || action.ExpectedSlot == contact.Slot
                            ? "accepted" : "variant-mismatch";
                    }
                }
            }
            if (outcome.FirstContactTick.HasValue && tick > outcome.FirstContactTick.Value)
            {
                noActionRun = canAct ? 0 : noActionRun + 1;
                outcome.LongestNoActionTicks = Math.Max(outcome.LongestNoActionTicks, noActionRun);
            }
            var a = sim.GetState(AttackerId);
            var d = sim.GetState(DefenderId);
            if (beforeAttacker.IsGrounded && !a.IsGrounded) outcome.TakeoffTicks.Add(tick);
            if (!beforeAttacker.IsGrounded && a.IsGrounded) outcome.LandingTicks.Add(tick);
            if (!beforeAttacker.IsFastFalling && a.IsFastFalling) outcome.FastFallTicks.Add(tick);
            if (captureFrames) outcome.Frames.Add(new Frame(tick, Snapshot(a), Snapshot(d), canAct));
            foreach (var death in sim.LastTickDeaths)
                outcome.Deaths.Add(new Death(tick, death.EntityId));
            outcome.ObservedTicks = tick + 1;
            lastAttacker = a;
            lastDefender = d;
            if (sim.LastTickDeaths.Count > 0) break;
        }
        outcome.FinalAttacker = Snapshot(lastAttacker);
        outcome.FinalDefender = Snapshot(lastDefender);
        for (; actionIndex < replay.Actions.Count; actionIndex++)
        {
            var action = replay.Actions[actionIndex];
            outcome.Actions.Add(new ActionObservation { EntityId = AttackerId, Tick = action.Tick,
                ExpectedSlot = action.Slot, RequestedWireSlot = WireSlot(action.Slot), Status = "not-reached" });
        }
        if (outcome.FirstContactTick.HasValue)
        {
            outcome.ConversionTicks = outcome.LastContactTick!.Value - outcome.FirstContactTick.Value;
            foreach (int tick in outcome.DefenderActionTicks)
                if (tick > outcome.FirstContactTick.Value && tick <= outcome.LastContactTick.Value) outcome.GapTicks.Add(tick);
        }
        outcome.NoActionWindowClipped = noActionRun > 0 && outcome.ObservedTicks == replay.AttackerInputs.Length;
        outcome.ConnectedActions = outcome.Actions.Count(a => a.EntityId == AttackerId && a.Contacts > 0);
        outcome.RouteSucceeded = replay.Actions.Count > 0 && outcome.Actions.Where(a => a.EntityId == AttackerId)
            .All(a => a.Status == "accepted" && a.Contacts > 0);
        outcome.Verdict = outcome.ContactCount == 0 ? "no-contact" : outcome.ConnectedActions < 2 ? "single-activation"
            : outcome.GapTicks.Count == 0 ? "true" : "pressure";
        return new Execution { Outcome = outcome, GeneratedDefenderInputs = generated };
    }

    private static void ObserveAction(ServerSimulation sim, ulong id, int tick, byte requested, ulong previous,
        string? expected, Outcome outcome, Dictionary<ulong, ActionObservation> byActivation)
    {
        ulong activation = sim.GetLastActivationId(id);
        bool accepted = activation != previous && sim.LastTickAcceptedActions.Contains(id);
        if (requested == 0 && !accepted) return;
        var action = new ActionObservation { EntityId = id, Tick = tick, ExpectedSlot = expected,
            RequestedWireSlot = requested, ActivationId = accepted ? activation : 0 };
        if (accepted)
        {
            var ability = sim.GetActiveAbility(id);
            if (ability != null && ability.ActivationId == activation)
                action.ActualSlot = CanonicalSlot((byte)(ability.Slot + 1), ability.AirborneAtStart);
            else
                foreach (var hit in sim.LastTickHits)
                    if (hit.OwnerEntityId == id && hit.ActivationId == activation)
                    { action.ActualSlot = CanonicalSlot(hit.AttackSlot, hit.Airborne); break; }
            action.Status = action.ActualSlot == null ? "accepted-unobserved-variant"
                : expected == null || expected == action.ActualSlot ? "accepted" : "variant-mismatch";
            byActivation[activation] = action;
        }
        outcome.Actions.Add(action);
    }

    internal static StateSample Snapshot(in CharacterState state) => new(state.PX, state.PY, state.PZ,
        state.VX, state.VY, state.VZ, state.IsGrounded, state.State.ToString(), state.HitstopTicks,
        state.HitstunTicks, state.LandingLagTicks, state.IsFastFalling, state.AnimLockTicks);
    internal static byte WireSlot(string canonical) => canonical switch
    {
        "ground.1" or "air.1" => AbilitySlots.Slot1,
        "ground.3" or "air.3" => AbilitySlots.Slot3,
        "ground.4" or "air.4" => AbilitySlots.Slot4,
        _ => throw new ArgumentException($"Unsupported normal slot '{canonical}'."),
    };
    private static string CanonicalSlot(byte wire, bool air)
    {
        string number = wire switch
        {
            AbilitySlots.Slot1 => "1", AbilitySlots.Slot3 => "3", AbilitySlots.Slot4 => "4",
            AbilitySlots.A => "A", AbilitySlots.E => "E", AbilitySlots.R => "R", AbilitySlots.F => "F", _ => $"wire-{wire}",
        };
        return (air ? "air." : "ground.") + number;
    }
    internal static (float, float) DiVector(string direction) => direction switch
    {
        "neutral" => (0f, 0f), "in" => (0f, -1f), "away" => (0f, 1f), "left" => (-1f, 0f), "right" => (1f, 0f),
        "in-left" => (-0.70710677f, -0.70710677f), "in-right" => (0.70710677f, -0.70710677f),
        "away-left" => (-0.70710677f, 0.70710677f), "away-right" => (0.70710677f, 0.70710677f),
        _ => throw new ArgumentException($"Unknown DI direction '{direction}'."),
    };

    internal static void ValidateReplay(Context context, Replay replay)
    {
        if (replay.SchemaVersion != 1 || replay.Rule != "stock-3-stop-on-death") throw new InvalidDataException("Unsupported replay schema/rule.");
        if (replay.AttackerContent != context.Attacker.Identity || replay.DefenderContent != context.Defender.Identity
            || replay.Simulation != context.Identity)
            throw new InvalidDataException("Replay content or simulation build/tuning identity differs from the current context.");
        if (replay.AttackerInputs == null || replay.DefenderInputs == null || replay.Actions == null
            || replay.AttackerInputs.Length < 1 || replay.AttackerInputs.Length > MaxHorizon
            || replay.AttackerInputs.Length != replay.DefenderInputs.Length)
            throw new InvalidDataException("Replay requires equal, complete bounded input streams for both fighters.");
        var map = replay.Arena.Heightmap;
        if (map.Width < 2 || map.Height < 2 || map.Width > 1000 || map.Height > 1000 || map.Data == null
            || map.Data.Length != map.Width * map.Height || !float.IsFinite(map.CellSize) || map.CellSize <= 0f)
            throw new InvalidDataException("Invalid replay heightmap.");
        if (!ReferenceEquals(map.Data, context.Arena.Heightmap.Data)) ValidateHeightmap(map);
        ValidateState(replay.InitialAttacker);
        ValidateState(replay.InitialDefender);
        int previous = -1, presses = 0;
        foreach (var action in replay.Actions)
        {
            if (action == null || action.Tick <= previous || action.Tick >= replay.AttackerInputs.Length
                || action.Tick < 0 || replay.AttackerInputs[action.Tick].ActiveSlot != WireSlot(action.Slot))
                throw new InvalidDataException("Planned actions must uniquely match ordered input press ticks.");
            previous = action.Tick;
        }
        foreach (var input in replay.AttackerInputs)
        {
            ValidateInput(input);
            if (input.ActiveSlot != 0) presses++;
        }
        foreach (var input in replay.DefenderInputs) ValidateInput(input);
        if (presses != replay.Actions.Count) throw new InvalidDataException("Every attacker press must have a planned action.");
    }
    private static void ValidateHeightmap(in ArenaHeightmap map)
    {
        if (map.Width < 2 || map.Height < 2 || map.Width > 1000 || map.Height > 1000 || map.Data == null
            || map.Data.Length != map.Width * map.Height || !float.IsFinite(map.CellSize) || map.CellSize <= 0f
            || !float.IsFinite(map.OriginX) || !float.IsFinite(map.OriginZ)
            || map.Data.Any(h => !float.IsFinite(h))) throw new InvalidDataException("Invalid replay heightmap.");
    }
    private static void ValidateState(in CharacterState state)
    {
        if (!float.IsFinite(state.PX) || !float.IsFinite(state.PY) || !float.IsFinite(state.PZ)
            || !float.IsFinite(state.VX) || !float.IsFinite(state.VY) || !float.IsFinite(state.VZ)
            || !float.IsFinite(state.FacingYaw) || state.DamagePercent > 999)
            throw new InvalidDataException("Invalid initial fighter state.");
        if (state.State is ActionState.Attacking or ActionState.Aiming || state.InteractionId != 0
            || state.AnimLockTicks != 0 || state.BufferedSlot != 0)
            throw new InvalidDataException("Initial state requires a fresh experiment, not missing active-ability/interaction runtime.");
    }
    private static void ValidateInput(in InputState input)
    {
        if (!float.IsFinite(input.MoveX) || !float.IsFinite(input.MoveY) || MathF.Abs(input.MoveX) > 1f
            || MathF.Abs(input.MoveY) > 1f || input.LockMode > TargetLockMode.OnHit || input.ActiveSlot > AbilitySlots.Count)
            throw new InvalidDataException("Invalid replay input.");
    }
    private static SimulationIdentity CurrentSimulationIdentity(CombatTuning tuning) => new(SharedHash, ToolHash,
        tuning.KnockbackScale, tuning.HitstunCoefficient, tuning.HitstunMagnitudeBonus);
    private static string AssemblyHash(string path)
    {
        using var input = File.OpenRead(path);
        using var hash = SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(input)).ToLowerInvariant();
    }
    internal static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    internal static string OutcomeHash(Outcome outcome)
    {
        string json = ToJson(new { outcome.ObservedTicks, outcome.RouteSucceeded, outcome.Verdict,
            outcome.ConnectedActions, outcome.ContactCount, outcome.Damage, outcome.FirstContactTick, outcome.LastContactTick,
            outcome.ConversionTicks, outcome.LongestNoActionTicks, outcome.NoActionWindowClipped,
            outcome.FinalAttacker, outcome.FinalDefender, outcome.Actions, outcome.Contacts,
            outcome.DefenderActionTicks, outcome.GapTicks, outcome.AttackerActionTicks,
            outcome.TakeoffTicks, outcome.LandingTicks, outcome.FastFallTicks, outcome.Deaths });
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    internal static int Run(string[] args)
    {
        try
        {
            var options = new SearchOptions();
            string? replayPath = null, witnessId = null, output = null;
            bool search = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!seen.Add(key)) throw new ArgumentException($"Repeated option '{key}'.");
                if (key == "--route-search") { search = true; continue; }
                if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for '{key}'.");
                string value = args[++i];
                int Integer() => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                switch (key)
                {
                    case "--route-replay": replayPath = value; break;
                    case "--witness": witnessId = value; break;
                    case "--out": output = value; break;
                    case "--budget": options.Budget = Integer(); break;
                    case "--measure-budget": options.MeasurementBudget = Integer(); break;
                    case "--horizon": options.Horizon = Integer(); break;
                    case "--keep": options.Keep = Integer(); break;
                    case "--prefix-width": options.PrefixWidth = Integer(); break;
                    case "--timing-radius": options.TimingRadius = Integer(); break;
                    case "--pcts": options.Percents = value.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray(); break;
                    case "--distances": options.Distances = value.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray(); break;
                    case "--di": options.DiDirections = value.Split(','); break;
                    default: throw new ArgumentException($"Unknown route option '{key}'.");
                }
            }
            if (search == (replayPath != null)) throw new ArgumentException("Choose exactly one of --route-search or --route-replay <file>.");
            options.Validate();
            var context = Context.FightGuy();
            string json;
            if (search)
            {
                if (witnessId != null) throw new ArgumentException("--witness is a replay option.");
                var report = Search(context, options);
                json = ToJson(report);
                Console.Error.WriteLine($"routes: {report.CandidatesRun} candidates, {report.MeasurementsRun} measurements, {report.Witnesses.Count} witnesses; budgetExhausted={report.BudgetExhausted}");
            }
            else
            {
                Replay replay = ReadReplay(replayPath!, witnessId);
                var outcome = Execute(context, replay, captureFrames: true).Outcome;
                bool? matches = replay.ExpectedOutcomeHash == null ? null : OutcomeHash(outcome) == replay.ExpectedOutcomeHash;
                if (matches == false) throw new InvalidDataException("Replay observations differ from the retained witness.");
                json = ToJson(new ReplayReport { Replay = replay, Outcome = outcome, MatchesExpected = matches });
                Console.Error.WriteLine($"replay: {replay.Id}, {outcome.Verdict}, damage={outcome.Damage}, matchesExpected={matches}");
            }
            string path = Path.GetFullPath(output ?? (search ? "artifacts/route-search.json" : "artifacts/route-replay.json"));
            if (replayPath != null && path == Path.GetFullPath(replayPath)) throw new ArgumentException("Replay output must differ from its input.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            Console.Error.WriteLine($"wrote {path}");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or InvalidDataException
            or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"route-report: {e.Message}");
            return 1;
        }
    }
    internal static Replay ReadReplay(string path, string? witnessId = null)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Replay file exceeds 64 MiB.");
        string json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Replay must be an object.");
        Replay Selected(JsonElement element)
        {
            var selected = element.Deserialize<Replay>(JsonOptions) ?? throw new InvalidDataException("Empty replay.");
            if (witnessId != null && selected.Id != witnessId)
                throw new InvalidDataException("Requested witness ID does not match the replay.");
            return selected;
        }
        if (document.RootElement.TryGetProperty("witnesses", out var witnesses))
        {
            if (witnesses.ValueKind != JsonValueKind.Array
                || !document.RootElement.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema) || schema != 1)
                throw new InvalidDataException("Invalid search report schema/witnesses.");
            foreach (var witness in witnesses.EnumerateArray())
            {
                if (witness.ValueKind != JsonValueKind.Object) continue;
                if (witnessId != null && (!witness.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String || id.GetString() != witnessId)) continue;
                if (witness.TryGetProperty("replay", out var retained))
                    return Selected(retained);
            }
            throw new InvalidDataException("Requested retained witness was not found.");
        }
        if (document.RootElement.TryGetProperty("replay", out var replay))
            return Selected(replay);
        return Selected(document.RootElement);
    }
}
