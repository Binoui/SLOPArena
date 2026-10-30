using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.CpuFeelBench;

/// <summary>
/// Measures CPU Opponent behavior through the public policy and the real simulation against scripted
/// targets: reaching a target across each stage, approaching a target near an edge, connecting against still
/// and moving targets, holding footwork, using the kit, grabbing a shield, and the cost of a decision.
/// Run it before and after a CPU change. The numbers are diagnostics; the owner judges feel in play.
/// </summary>
internal static partial class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/CpuFeelBench -p:SkipUnityPluginCopy=true -- [scenario ...] [--out report.md]

        Scenarios: reach, edge, contact, moves, shieldwall, cost, costmap (every stage), all (default).
        Combat scenarios: plans, whiff, approaches, defend, tiers, pressure.
        --script name filters combat scripts; --stage arena filters stage sweeps; --seed n selects a reach trial.
        --trace-deaths writes death trails and unreached trials to stderr. Exclude it from timed comparisons.
        Reads the admitted roster in content-cooked/ and the stages in data/arenas/.
        """;

    private const ulong CpuId = 100;
    private const ulong TargetId = 1;
    private static readonly string[] SlotNames = { "", "", "", "1", "E", "R", "F", "", "3", "4", "", "A" };
    private static readonly StringBuilder Report = new();
    private static string _root = "";
    private static bool _traceDeaths;
    private static string? _stage, _script;
    private static int? _seed;

    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }
        _traceDeaths = args.Contains("--trace-deaths");
        int stageIndex = Array.IndexOf(args, "--stage"), seedIndex = Array.IndexOf(args, "--seed"), scriptIndex = Array.IndexOf(args, "--script");
        _script = scriptIndex >= 0 ? args[scriptIndex + 1] : null;
        _stage = stageIndex >= 0 ? args[stageIndex + 1] : null;
        _seed = seedIndex >= 0 ? int.Parse(args[seedIndex + 1]) : null;
        _root = FindRoot();
        Directory.SetCurrentDirectory(_root);
        int outIndex = Array.IndexOf(args, "--out");
        string? outPath = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null;
        var scenarios = args.Where((a, i) => !a.StartsWith("--")
            && (outIndex < 0 || i != outIndex + 1)
            && (stageIndex < 0 || i != stageIndex + 1)
            && (scriptIndex < 0 || i != scriptIndex + 1)
            && (seedIndex < 0 || i != seedIndex + 1)).ToList();
        if (scenarios.Count == 0 || scenarios.Contains("all"))
            scenarios = new() { "reach", "edge", "contact", "moves", "shieldwall", "cost" };
        Line($"# CPU feel bench, {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var scenario in scenarios)
        {
            switch (scenario)
            {
                case "plans": CombatBench("plans"); break;
                case "whiff": CombatBench("whiff"); break;
                case "approaches": CombatBench("approaches"); break;
                case "defend": CombatBench("defend"); break;
                case "tiers": CombatBench("tiers"); break;
                case "pressure": CombatBench("pressure"); break;
                case "reach": Reach(); break;
                case "edge": Edge(); break;
                case "contact": Contact(); break;
                case "moves": Moves(); break;
                case "shieldwall": ShieldWall(); break;
                case "cost": Cost(); break;
                case "costmap":
                    foreach (var (name, stage) in Stages())
                    {
                        Line();
                        Line($"### {name}");
                        CostMap(stage);
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown scenario '{scenario}'.\n{Usage}");
                    return 2;
            }
        }
        if (outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, Report.ToString());
            Console.WriteLine($"Wrote {outPath}");
        }
        return 0;
    }

    private static void Line(string text = "")
    {
        Console.WriteLine(text);
        Report.AppendLine(text);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "arenas")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Run from inside the SLOPArena checkout.");
    }

    private static ArenaDefinition LoadArena(string name)
        => ArenaBinaryFormat.LoadFromFile(Path.Combine(_root, "data", "arenas", name + ".arena"))
            ?? throw new InvalidDataException($"Cannot load arena '{name}'.");

    private static IEnumerable<(string Name, ArenaDefinition Arena)> Stages()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "data", "arenas"), "*.arena").OrderBy(f => f))
        {
            if (_stage != null && Path.GetFileNameWithoutExtension(file) != _stage) continue;
            var arena = ArenaBinaryFormat.LoadFromFile(file);
            if (arena is { } loaded && loaded.Heightmap.Data != null && loaded.MaxX > loaded.MinX
                && loaded.SpawnPoints is { Length: > 0 })
                yield return (Path.GetFileNameWithoutExtension(file), loaded);
        }
    }

    private static IEnumerable<CharacterClass> Roster()
    {
        foreach (var character in Enum.GetValues<CharacterClass>())
        {
            if (character == CharacterClass.None) continue;
            bool admitted;
            try { admitted = BuiltInContentResolver.Resolve(character) != null; }
            catch (Exception) { admitted = false; }
            if (admitted) yield return character;
        }
    }

    private static CharacterState Spawn(ulong id, CharacterDefinition def, in ArenaDefinition arena, float x, float z,
        float yaw, float? feetY = null)
    {
        float floor = feetY ?? arena.Heightmap.Sample(x, z);
        return new CharacterState
        {
            EntityId = id, PX = x, PY = (floor > float.MinValue ? floor : 0f) + def.CapsuleHeight * 0.5f, PZ = z,
            State = ActionState.Idle, IsGrounded = true, JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
            FacingYaw = yaw, MatchState = MatchState.Playing,
        };
    }

    private static float Horizontal(in CharacterState a, in CharacterState b)
        => MathF.Sqrt((a.PX - b.PX) * (a.PX - b.PX) + (a.PZ - b.PZ) * (a.PZ - b.PZ));

    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0) return 0f;
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>One CPU against one scripted target, driven the way TrainingMatch drives a CPU.</summary>
    private sealed class Duel
    {
        public readonly ServerSimulation Sim;
        public readonly ArenaDefinition Arena;
        public readonly MatchRecorder Recorder = new();
        public InputState CpuInput;
        public int Tick;
        private readonly MatchContentEntry _cpu;
        private readonly MatchContentEntry _target;
        private readonly HeuristicBotPolicy _policy;
        private readonly BotMemory _memory;
        private readonly Random _rng;
        private readonly Dictionary<ulong, InputState> _inputs = new();
        private readonly Queue<string> _trace = new();

        public Duel(in ArenaDefinition arena, MatchContentEntry cpu, MatchContentEntry target, CpuDifficulty difficulty,
            int seed, CharacterState cpuStart, CharacterState targetStart, HeuristicBotPolicy? policy = null)
        {
            Arena = arena;
            _cpu = cpu;
            _target = target;
            Sim = new ServerSimulation(arena);
            Sim.RegisterEntity(CpuId, cpu.Definition, cpuStart, cpu.Baked);
            Sim.RegisterEntity(TargetId, target.Definition, targetStart, target.Baked);
            _policy = policy ?? new HeuristicBotPolicy();
            _memory = new BotMemory { Difficulty = difficulty };
            _rng = new Random(seed);
        }

        public void ResetSelf(in CharacterState state)
        {
            Sim.SetState(CpuId, state);
            _memory.Reset();
        }

        public CpuPlanTrace Trace => _memory.CombatTrace;
        public int WalkProbes => _memory.WalkProbesThisTick;
        public CharacterState Self => Sim.GetState(CpuId);
        public CharacterState Other => Sim.GetState(TargetId);

        public void Settle(int ticks)
        {
            _inputs[CpuId] = default;
            _inputs[TargetId] = default;
            for (int i = 0; i < ticks; i++) Sim.Tick(_inputs);
        }

        public void Step(in InputState targetInput, bool record = false)
        {
            var self = Sim.GetState(CpuId);
            var other = Sim.GetState(TargetId);
            _policy.ObserveOpponent(_memory, other, Sim.GetDefinition(TargetId), _target.Baked, Sim.GetActiveMove(TargetId)?.Slot);
            CpuInput = _policy.Decide(self, _cpu.Definition.ForForm(self.Form.Active), _rng, _memory, Arena, _cpu.Baked,
                CpuBehavior.Attack, Sim.GetActiveMove(CpuId)?.Slot, canSwapCancel: Sim.CanSwapCancel(CpuId));
            if (_traceDeaths)
            {
                var verdict = StageWalk.Probe(self, _cpu.Definition, Arena, CpuInput.MoveX, CpuInput.MoveY,
                    _cpu.Definition.Movement.RunSpeed * 0.3f, 4f);
                _memory.TryGetDelayedOpponent(out var seen);
                _trace.Enqueue($"{Tick} pos={self.PX:F3},{self.PY:F3},{self.PZ:F3} v={self.VX:F2},{self.VY:F2},{self.VZ:F2} ground={self.IsGrounded} state={self.State} heading={CpuInput.MoveX:F3},{CpuInput.MoveY:F3} walk={verdict.End}/{verdict.Covered:F2}/{verdict.Height:F2} climb={_memory.ClimbTicks} slot={CpuInput.ActiveSlot} jump={CpuInput.Jump} plan={_memory.PlanKind}/{_memory.PlanSlot} intent={_memory.CombatTrace.Kind}/{_memory.CombatTrace.Phase} dash={CpuInput.DashPressed} seen={seen.PX:F2},{seen.PY:F2},{seen.PZ:F2}/{seen.State}/stun={seen.HitstunTicks}/flight={seen.InPostHitstunFlight}/v={seen.VX:F2},{seen.VY:F2},{seen.VZ:F2}");
                if (_trace.Count > 360) _trace.Dequeue();
            }
            _inputs[CpuId] = CpuInput;
            _inputs[TargetId] = targetInput;
            if (record)
            {
                Recorder.RecordInputs(Tick, _inputs);
                Recorder.RecordPresses(Sim, Tick, _inputs, _cpu.Definition);
            }
            Sim.Tick(_inputs);
            if (_traceDeaths && Self.Deaths > self.Deaths)
                foreach (string line in _trace) Console.Error.WriteLine(line);
            if (record) Recorder.RecordTick(Sim, Tick, _inputs, _cpu.Definition);
            var post = Sim.GetState(TargetId);
            foreach (var hit in Sim.LastTickHits)
                if (hit.OwnerEntityId == CpuId && hit.TargetEntityId == TargetId)
                    _memory.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, post, hit.HitstopTicks);
            Tick++;
        }
    }

    /// <summary>Classifies one CPU input against the target's true position.</summary>
    private enum Move : byte { None, Approach, Retreat, Sideways }

    private static Move Classify(in InputState input, in CharacterState self, in CharacterState target)
    {
        if (input.ActiveSlot != 0 || input.Jump || input.ShieldHeld || input.GrabPressed) return Move.None;
        if (input.MoveX == 0f && input.MoveY == 0f) return Move.None;
        float dx = target.PX - self.PX, dz = target.PZ - self.PZ;
        float d = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dz * dz));
        float along = (input.MoveX * dx + input.MoveY * dz) / d;
        return along > 0.5f ? Move.Approach : along < -0.5f ? Move.Retreat : Move.Sideways;
    }

    /// <summary>Footwork statistics from a stream of classified inputs.</summary>
    private sealed class Footwork
    {
        public readonly List<int> RetreatRuns = new();
        public readonly List<int> SidewaysRuns = new();
        public int Ticks, Sideways, ApproachToggles, CloseTicks, JumpsInPlace;
        private Move _previous;
        private int _length;

        public void Add(in InputState input, in CharacterState self, in CharacterState target)
        {
            var move = Classify(input, self, target);
            Ticks++;
            if (move == Move.Sideways) Sideways++;
            if (input.Jump && input.MoveX == 0f && input.MoveY == 0f && self.IsGrounded) JumpsInPlace++;
            if (Horizontal(self, target) <= 3f)
            {
                CloseTicks++;
                if ((move == Move.Approach) != (_previous == Move.Approach) && (move is Move.None or Move.Approach)
                    && (_previous is Move.None or Move.Approach))
                    ApproachToggles++;
            }
            if (move == _previous)
            {
                _length++;
                return;
            }
            if (_previous == Move.Retreat) RetreatRuns.Add(_length);
            if (_previous == Move.Sideways) SidewaysRuns.Add(_length);
            _previous = move;
            _length = 1;
        }

        public float TogglesPerSecond => CloseTicks == 0 ? 0f : ApproachToggles * 60f / CloseTicks;
    }

    private static void Reach()
    {
        var fighter = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        foreach (var difficulty in new[] { CpuDifficulty.Normal, CpuDifficulty.Hard })
        {
            Line();
            Line($"## Reach sweep, FightGuy CPU {difficulty}");
            Line("From floor spots on a 2 m grid to an idle FightGuy on a spawn point. Reached = within 2 m and 1.2 m of height in 6 s.");
            Line();
            Line("| Stage | Trials | Reached | Median ticks | CPU deaths |");
            Line("|---|---|---|---|---|");
            int allTrials = 0, allReached = 0, allDeaths = 0;
            foreach (var (name, arena) in Stages())
            {
                var spots = new List<(float X, float Z)>();
                for (float x = arena.MinX + 1f; x < arena.MaxX - 1f; x += 2f)
                for (float z = arena.MinZ + 1f; z < arena.MaxZ - 1f; z += 2f)
                    if (arena.Heightmap.Sample(x, z) != float.MinValue) spots.Add((x, z));
                int stride = Math.Max(1, spots.Count / 60);
                int trials = 0, reached = 0, deaths = 0;
                var ticks = new List<int>();
                bool admitsDuel = true;
                for (int i = 0; i < spots.Count && admitsDuel; i += stride)
                {
                    if (_seed.HasValue && i + 1 != _seed.Value) continue;
                    var spawn = arena.SpawnPoints[(i / stride) % arena.SpawnPoints.Length];
                    var (sx, sz) = spots[i];
                    if (MathF.Sqrt((sx - spawn.X) * (sx - spawn.X) + (sz - spawn.Z) * (sz - spawn.Z)) < 4f) continue;
                    Duel duel;
                    try
                    {
                        var targetStart = Spawn(TargetId, fighter.Definition, arena, spawn.X, spawn.Z, 0f);
                        targetStart.PY = spawn.Y;
                        duel = new Duel(arena, fighter, fighter, difficulty, i + 1,
                            Spawn(CpuId, fighter.Definition, arena, sx, sz, 0f), targetStart);
                    }
                    catch (ArgumentException)
                    {
                        admitsDuel = false;
                        continue;
                    }
                    duel.Settle(6);
                    if (!duel.Self.IsGrounded || !duel.Other.IsGrounded || duel.Self.Deaths > 0 || duel.Other.Deaths > 0)
                        continue;
                    trials++;
                    for (int tick = 0; tick < 360; tick++)
                    {
                        var self = duel.Self;
                        var other = duel.Other;
                        if (Horizontal(self, other) <= 2f && MathF.Abs(other.PY - self.PY) <= 1.2f)
                        {
                            reached++;
                            ticks.Add(tick);
                            break;
                        }
                        if (self.Deaths > 0)
                        {
                            deaths++;
                            if (_traceDeaths) Console.Error.WriteLine($"DEATH stage={name} tier={difficulty} seed={i + 1} start={sx},{sz} target={spawn.X},{spawn.Y},{spawn.Z}");
                            break;
                        }
                        duel.Step(default);
                        if (_traceDeaths && tick == 359)
                            Console.Error.WriteLine($"UNREACHED stage={name} tier={difficulty} seed={i + 1} start={sx},{sz} target={spawn.X},{spawn.Y},{spawn.Z} ended={duel.Self.PX},{duel.Self.PY},{duel.Self.PZ}");
                    }
                }
                if (trials == 0) continue;
                allTrials += trials;
                allReached += reached;
                allDeaths += deaths;
                Line($"| {name} | {trials} | {100 * reached / trials}% | {Median(ticks)} | {deaths} |");
            }
            Line($"| all | {allTrials} | {100 * allReached / Math.Max(1, allTrials)}% | | {allDeaths} |");
        }
    }

    private static void Edge()
    {
        var fighter = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        Line();
        Line("## Edge approach, FightGuy CPU Normal");
        Line("An idle target stands within 1.5 m of a drop; the CPU starts 6 m inward. Reached = within 2 m in 5 s.");
        Line();
        Line("| Stage | Trials | Reached | Sideways share (median) | CPU deaths |");
        Line("|---|---|---|---|---|");
        foreach (var name in new[] { "training", "log_off", "after_hours", "splash_deck", "slop_pit" })
        {
            var arena = LoadArena(name);
            float cx = (arena.MinX + arena.MaxX) * 0.5f, cz = (arena.MinZ + arena.MaxZ) * 0.5f;
            var spots = new List<(float X, float Z, float CX, float CZ)>();
            for (float x = arena.MinX + 1f; x < arena.MaxX - 1f; x += 1f)
            for (float z = arena.MinZ + 1f; z < arena.MaxZ - 1f; z += 1f)
            {
                if (arena.Heightmap.Sample(x, z) == float.MinValue) continue;
                bool nearDrop = false;
                for (int k = 0; k < 8 && !nearDrop; k++)
                {
                    float yaw = k * MathF.PI / 4f;
                    nearDrop = arena.Heightmap.Sample(x + MathF.Sin(yaw) * 1.5f, z + MathF.Cos(yaw) * 1.5f) == float.MinValue;
                }
                if (!nearDrop) continue;
                float dx = cx - x, dz = cz - z, d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < 7f) continue;
                float sx = x + dx / d * 6f, sz = z + dz / d * 6f;
                if (arena.Heightmap.Sample(sx, sz) == float.MinValue) continue;
                spots.Add((x, z, sx, sz));
            }
            int stride = Math.Max(1, spots.Count / 30);
            int trials = 0, reached = 0, deaths = 0;
            var sideways = new List<float>();
            for (int i = 0; i < spots.Count; i += stride)
            {
                var (tx, tz, sx, sz) = spots[i];
                var duel = new Duel(arena, fighter, fighter, CpuDifficulty.Normal, i + 1,
                    Spawn(CpuId, fighter.Definition, arena, sx, sz, 0f), Spawn(TargetId, fighter.Definition, arena, tx, tz, 0f));
                duel.Settle(6);
                if (!duel.Self.IsGrounded || !duel.Other.IsGrounded || duel.Self.Deaths > 0 || duel.Other.Deaths > 0)
                    continue;
                trials++;
                var footwork = new Footwork();
                for (int tick = 0; tick < 300; tick++)
                {
                    var self = duel.Self;
                    var other = duel.Other;
                    if (Horizontal(self, other) <= 2f && MathF.Abs(other.PY - self.PY) <= 1.2f)
                    {
                        reached++;
                        break;
                    }
                    if (self.Deaths > 0)
                    {
                        deaths++;
                        break;
                    }
                    duel.Step(default);
                    footwork.Add(duel.CpuInput, self, other);
                }
                sideways.Add(footwork.Ticks == 0 ? 0f : footwork.Sideways / (float)footwork.Ticks);
            }
            if (trials == 0) continue;
            Line($"| {name} | {trials} | {100 * reached / trials}% | {100 * Median(sideways):F0}% | {deaths} |");
        }
    }

    private static void Contact()
    {
        var fighter = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var arena = LoadArena("training");
        Line();
        Line("## Contact and footwork, FightGuy CPU vs a scripted FightGuy on Training");
        Line("6 seeds x 60 s per row. Contact = hits plus blocks over swings with a measured closest approach.");
        Line();
        Line("| Difficulty | Target | Swings | Contact (per-seed range) | Whiff gap median | Retreat run | Sideways run | Approach toggles/s within 3 m | Jumps in place |");
        Line("|---|---|---|---|---|---|---|---|---|");
        foreach (var difficulty in new[] { CpuDifficulty.Normal, CpuDifficulty.Hard })
        foreach (var pattern in new[] { "still", "strafe", "in-out" })
        {
            int swings = 0, contact = 0, measured = 0;
            var perSeed = new List<int>();
            var gaps = new List<float>();
            var footwork = new Footwork();
            for (int seed = 1; seed <= 6; seed++)
            {
                var duel = new Duel(arena, fighter, fighter, difficulty, seed,
                    Spawn(CpuId, fighter.Definition, arena, 0f, -3f, 0f), Spawn(TargetId, fighter.Definition, arena, 0f, 0f, MathF.PI));
                for (int tick = 0; tick < 3600; tick++)
                {
                    var self = duel.Self;
                    var target = duel.Other;
                    int phase = tick / 45 % 2 == 0 ? 1 : -1;
                    var scripted = pattern switch
                    {
                        "strafe" => new InputState { MoveX = phase },
                        "in-out" => new InputState { MoveY = MathF.Abs(target.PZ) > 3f ? -MathF.Sign(target.PZ) : tick / 40 % 2 == 0 ? -1f : 1f },
                        _ => default,
                    };
                    duel.Step(scripted, record: true);
                    footwork.Add(duel.CpuInput, self, target);
                    var post = duel.Other;
                    if (post.DamagePercent > 60 || post.Deaths > 0)
                        duel.Sim.SetState(TargetId, Spawn(TargetId, fighter.Definition, arena, 0f, 0f, MathF.PI));
                }
                var record = duel.Recorder.Finish(3600, seed, default);
                int seedMeasured = 0, seedContact = 0;
                foreach (var swing in record.Swings.Where(s => s.Attacker == CpuId && s.Accepted))
                {
                    swings++;
                    if (!swing.HasClosestApproach) continue;
                    measured++;
                    seedMeasured++;
                    if (swing.DamagingContacts > 0 || swing.Blocked || swing.Connected)
                    {
                        contact++;
                        seedContact++;
                    }
                    else gaps.Add(swing.ClosestGap);
                }
                perSeed.Add(seedMeasured == 0 ? 0 : 100 * seedContact / seedMeasured);
            }
            perSeed.Sort();
            Line($"| {difficulty} | {pattern} | {swings} | {(measured == 0 ? 0 : 100 * contact / measured)}% ({perSeed.First()} to {perSeed.Last()}) | "
                + $"{Median(gaps):F2} m | {Median(footwork.RetreatRuns)} ticks (n={footwork.RetreatRuns.Count}) | "
                + $"{Median(footwork.SidewaysRuns)} ticks (n={footwork.SidewaysRuns.Count}) | {footwork.TogglesPerSecond:F1} | {footwork.JumpsInPlace} |");
        }
    }

    private static void Moves()
    {
        var arena = LoadArena("training");
        Line();
        Line("## Move use, Normal CPU in a mirror match on Training");
        Line("Accepted swings against a scripted target, still then stepping in and out, 4 seeds x 60 s each.");
        Line();
        Line("| Fighter | Swings | Normals | Shares |");
        Line("|---|---|---|---|");
        foreach (var character in Roster())
        {
            var entry = BuiltInContentResolver.Resolve(character);
            var counts = new Dictionary<string, int>();
            int total = 0, normals = 0;
            foreach (var pattern in new[] { "still", "in-out" })
            for (int seed = 1; seed <= 4; seed++)
            {
                var duel = new Duel(arena, entry, entry, CpuDifficulty.Normal, seed,
                    Spawn(CpuId, entry.Definition, arena, 0f, -3f, 0f), Spawn(TargetId, entry.Definition, arena, 0f, 0f, MathF.PI));
                for (int tick = 0; tick < 3600; tick++)
                {
                    var target = duel.Other;
                    var scripted = pattern == "in-out"
                        ? new InputState { MoveY = MathF.Abs(target.PZ) > 3f ? -MathF.Sign(target.PZ) : tick / 40 % 2 == 0 ? -1f : 1f }
                        : default;
                    duel.Step(scripted, record: true);
                    var post = duel.Other;
                    if (post.DamagePercent > 60 || post.Deaths > 0)
                        duel.Sim.SetState(TargetId, Spawn(TargetId, entry.Definition, arena, 0f, 0f, MathF.PI));
                }
                foreach (var swing in duel.Recorder.Finish(3600, seed, default).Swings.Where(s => s.Attacker == CpuId && s.Accepted))
                {
                    string slot = swing.ActiveSlot < SlotNames.Length ? SlotNames[swing.ActiveSlot] : swing.ActiveSlot.ToString();
                    string key = (swing.Air ? "a" : "g") + slot;
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    total++;
                    if (slot is "1" or "3" or "4") normals++;
                }
            }
            string shares = string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {100 * c.Value / Math.Max(1, total)}%"));
            Line($"| {character} | {total} | {100 * normals / Math.Max(1, total)}% | {shares} |");
        }
    }

    private static void ShieldWall()
    {
        var arena = LoadArena("training");
        Line();
        Line("## Shield wall, Normal CPU vs a target holding shield");
        Line("3 seeds x 60 s per fighter, mirror match on Training.");
        Line();
        Line("| Fighter | Grabs started | Ticks target was held | Blocked contacts | Presses |");
        Line("|---|---|---|---|---|");
        foreach (var character in Roster())
        {
            var entry = BuiltInContentResolver.Resolve(character);
            int grabs = 0, held = 0, blocked = 0, presses = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                var duel = new Duel(arena, entry, entry, CpuDifficulty.Normal, seed,
                    Spawn(CpuId, entry.Definition, arena, 0f, -3f, 0f), Spawn(TargetId, entry.Definition, arena, 0f, 0f, MathF.PI));
                var previous = ActionState.Idle;
                for (int tick = 0; tick < 3600; tick++)
                {
                    var self = duel.Self;
                    if (self.State == ActionState.GrabAttempt && previous != ActionState.GrabAttempt) grabs++;
                    previous = self.State;
                    if (duel.Other.State == ActionState.Grabbed) held++;
                    duel.Step(new InputState { ShieldHeld = true });
                    if (duel.CpuInput.ActiveSlot != 0 || duel.CpuInput.GrabPressed) presses++;
                    foreach (var hit in duel.Sim.LastTickHits)
                        if (hit.Blocked && hit.TargetEntityId == TargetId) blocked++;
                    var post = duel.Other;
                    if (post.DamagePercent > 80 || post.Deaths > 0)
                        duel.Sim.SetState(TargetId, Spawn(TargetId, entry.Definition, arena, 0f, 0f, MathF.PI));
                }
            }
            Line($"| {character} | {grabs} | {held} | {blocked} | {presses} |");
        }
    }

    private static void Cost()
    {
        var arena = LoadArena("hamster_habitat");
        var roster = new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou }
            .Select(character => BuiltInContentResolver.Resolve(character)).ToArray();
        var target = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var sim = new ServerSimulation(arena);
        var policy = new HeuristicBotPolicy();
        var memories = new BotMemory[roster.Length];
        var rngs = new Random[roster.Length];
        for (int i = 0; i < roster.Length; i++)
        {
            var spawn = arena.SpawnPoints[i % arena.SpawnPoints.Length];
            var state = Spawn(CpuId + (ulong)i, roster[i].Definition, arena, spawn.X, spawn.Z, 0f);
            state.PY = spawn.Y;
            sim.RegisterEntity(CpuId + (ulong)i, roster[i].Definition, state, roster[i].Baked);
            memories[i] = new BotMemory { Difficulty = CpuDifficulty.Hard };
            rngs[i] = new Random(i + 1);
        }
        var targetSpawn = arena.SpawnPoints[roster.Length % arena.SpawnPoints.Length];
        var targetState = Spawn(TargetId, target.Definition, arena, targetSpawn.X, targetSpawn.Z, 0f);
        targetState.PY = targetSpawn.Y;
        sim.RegisterEntity(TargetId, target.Definition, targetState, target.Baked);
        var inputs = new Dictionary<ulong, InputState>();
        var decide = new List<double>();
        var ticks = new List<double>();
        var transitions = new List<double>();
        var continuations = new List<double>();
        long allocated = 0;
        int decisions = 0;
        for (int tick = 0; tick < 1200; tick++)
        {
            bool measure = tick >= 600;
            var other = sim.GetState(TargetId);
            for (int i = 0; i < roster.Length; i++)
            {
                ulong id = CpuId + (ulong)i;
                var self = sim.GetState(id);
                var form = roster[i].Definition.ForForm(self.Form.Active);
                var targetDefinition = sim.GetDefinition(TargetId);
                var activeMove = sim.GetActiveMove(id)?.Slot;
                bool canSwap = sim.CanSwapCancel(id);
                uint serial = memories[i].CombatTrace.Serial;
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                policy.ObserveOpponent(memories[i], other, targetDefinition, target.Baked, sim.GetActiveMove(TargetId)?.Slot);
                inputs[id] = policy.Decide(self, form, rngs[i], memories[i], arena, roster[i].Baked, CpuBehavior.Attack,
                    activeMove, canSwapCancel: canSwap);
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (!measure) continue;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                double microseconds = elapsed * 1e6 / Stopwatch.Frequency;
                decide.Add(microseconds);
                (memories[i].CombatTrace.Serial == serial ? continuations : transitions).Add(microseconds);
                decisions++;
            }
            inputs[TargetId] = new InputState { MoveX = tick / 45 % 2 == 0 ? 1f : -1f };
            long tickStart = Stopwatch.GetTimestamp();
            sim.Tick(inputs);
            if (measure) ticks.Add((Stopwatch.GetTimestamp() - tickStart) * 1e6 / Stopwatch.Frequency);
        }
        decide.Sort();
        ticks.Sort();
        Line();
        Line("## Decision cost, three Hard CPUs chasing a strafing target on Hamster Habitat");
        Line("600 measured ticks after 600 warmup ticks, one shared policy as TrainingMatch uses.");
        Line();
        Line("| Measure | Mean | p99 | Max |");
        Line("|---|---|---|---|");
        Line($"| Decide per CPU (µs) | {decide.Average():F1} | {decide[(int)(decide.Count * 0.99)]:F1} | {decide.Last():F1} |");
        Line($"| Simulation tick, all fighters (µs) | {ticks.Average():F1} | {ticks[(int)(ticks.Count * 0.99)]:F1} | {ticks.Last():F1} |");
        Line($"| Bytes allocated per observe and Decide | {allocated / (double)Math.Max(1, decisions):F0} | | |");
        foreach (var bucket in new[] { ("Continuation", continuations), ("Transition", transitions) })
        {
            bucket.Item2.Sort();
            if (bucket.Item2.Count == 0) Line($"{bucket.Item1}: n=0.");
            else Line($"{bucket.Item1}: n={bucket.Item2.Count}, mean={bucket.Item2.Average():F1} µs, p99={bucket.Item2[(int)(bucket.Item2.Count * 0.99)]:F1}, max={bucket.Item2.Last():F1}.");
        }
        CostMap(arena);
    }

    /// <summary>
    /// The worst decisions, not the average: a Hard CPU on every floor spot of the stage with an idle target
    /// 6 m away, timed over its first second of chasing. Dense geometry makes walk probes expensive.
    /// </summary>
    private static void CostMap(in ArenaDefinition arena)
    {
        var fighter = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var worst = new List<double>();
        var all = new List<double>();
        int maxProbes = 0;
        for (float x = arena.MinX + 1f; x < arena.MaxX - 1f; x += 2f)
        for (float z = arena.MinZ + 1f; z < arena.MaxZ - 1f; z += 2f)
        {
            if (arena.Heightmap.Sample(x, z) == float.MinValue) continue;
            (float X, float Z)? goal = null;
            for (int k = 0; k < 8 && goal == null; k++)
            {
                float yaw = k * MathF.PI / 4f, gx = x + MathF.Sin(yaw) * 6f, gz = z + MathF.Cos(yaw) * 6f;
                if (gx > arena.MinX + 1f && gx < arena.MaxX - 1f && gz > arena.MinZ + 1f && gz < arena.MaxZ - 1f
                    && arena.Heightmap.Sample(gx, gz) != float.MinValue)
                    goal = (gx, gz);
            }
            if (goal == null) continue;
            Duel duel;
            try
            {
                duel = new Duel(arena, fighter, fighter, CpuDifficulty.Hard, 1,
                    Spawn(CpuId, fighter.Definition, arena, x, z, 0f), Spawn(TargetId, fighter.Definition, arena, goal.Value.X, goal.Value.Z, 0f));
            }
            catch (ArgumentException)
            {
                return;
            }
            duel.Settle(6);
            if (!duel.Self.IsGrounded || !duel.Other.IsGrounded) continue;
            for (int tick = 0; tick < 12; tick++) duel.Step(default);
            double spotWorst = 0;
            for (int tick = 0; tick < 60; tick++)
            {
                long start = Stopwatch.GetTimestamp();
                duel.Step(default);
                double elapsed = (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency;
                all.Add(elapsed);
                maxProbes = Math.Max(maxProbes, duel.WalkProbes);
                spotWorst = Math.Max(spotWorst, elapsed);
            }
            worst.Add(spotWorst);
        }
        if (worst.Count == 0) return;
        worst.Sort();
        all.Sort();
        Line();
        Line("Worst step per floor spot (one Hard CPU, observe + Decide + simulation tick, first second of a chase):");
        Line();
        Line("| Spots | Median spot worst (µs) | p90 spot worst | Max | Spots with a step over 1 ms | All steps p99 (µs) |");
        Line("|---|---|---|---|---|---|");
        Line($"| {worst.Count} | {worst[worst.Count / 2]:F0} | {worst[(int)(worst.Count * 0.9)]:F0} | {worst.Last():F0} | "
            + $"{worst.Count(w => w > 1000)} | {all[(int)(all.Count * 0.99)]:F0} |");
        Line($"Maximum walk probes in one CPU tick: {maxProbes}.");
    }
}
