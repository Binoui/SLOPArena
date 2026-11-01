using System.Numerics;

namespace SlopArena.Shared.Tests;

public abstract record RouteStep;
public sealed record Go(float X, float Z) : RouteStep;
public sealed record Hop(float X, float Z, float? MinLandY = null) : RouteStep;
public sealed record Break(string TargetId) : RouteStep;
public sealed record CourseRoute(string ArenaKey, RouteStep[] Steps, CourseFormat? Format = null, bool PlainJumpsOnly = false, RouteStep[]? RacingSteps = null);

/// <summary>
/// Drives any fighter along a course route table. Each Hop or Break searches a small set of input plans
/// from a World Snapshot and commits the first that lands or breaks without a death; the committed inputs
/// must replay to the same completed course.
/// </summary>
public sealed class CourseFinishability
{
    public static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    public readonly ArenaDefinition Arena;
    public ServerSimulation Sim { get; private set; }
    public readonly CharacterDefinition Fighter;
    public readonly List<InputState> Inputs = new();
    public readonly List<CharacterState> FrameStates = new();
    public readonly HashSet<string> SpecialTargets = new();
    public readonly List<CharacterCourseEvent> Events = new();
    public readonly List<ArcadePoint> RoutePoints = new();
    public readonly List<(RouteStep Step, int InputCount, CharacterState State)> Landings = new();
    private readonly Action<string> _log;
    private readonly CharacterClass _selector;
    private readonly bool _allowSnapshots;
    private CourseFormat? _format;
    private bool _plainJumpsOnly;
    private readonly List<InputState> _trial = new();
    private float _bestHorizontal, _bestVertical, _bestScore;
    private Vector3 _bestPosition;
    private string _bestPlan = "none";
    private int _attempts;
    public bool UsesSnapshots { get; private set; }
    public CharacterState State => Sim.GetState(1);
    private float Feet => State.PY - Fighter.CapsuleHeight * .5f;
    private Vector2 Position => new(State.PX, State.PZ);

    public CourseFinishability(string arenaKey, CharacterClass selector, Action<string>? log = null, bool allowSnapshots = true)
    {
        Arena = Environment.GetEnvironmentVariable("SLOPARENA_COURSE_SHELL") is { } shell
            ? CourseShellArena.FromShellFile(shell)
            : ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data", "arenas", arenaKey + ".arena"))!.Value;
        if (Arena.Name != arenaKey) throw new ArgumentException("Course shell does not match " + arenaKey);
        _selector = selector;
        _allowSnapshots = allowSnapshots;
        Fighter = BuiltInContentResolver.Resolve(selector).Definition;
        _log = log ?? (_ => { });
        Sim = Fresh();
    }

    private ServerSimulation Fresh()
    {
        var entry = BuiltInContentResolver.Resolve(_selector);
        var sim = new ServerSimulation(Arena, _format is { } format ? new CourseMatchRule(format) : NoWinMatchRule.Instance);
        var spawn = Arena.SpawnPoints[0];
        sim.RegisterEntity(1, entry.Definition, new()
        {
            PX = spawn.X, PY = spawn.Y, PZ = spawn.Z,
            FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw,
            JumpsLeft = Fighter.Movement.MaxJumps, AirDodgesLeft = 1
        }, entry.BakedAnimation);
        sim.SetRespawnPosition(1, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
        if (_format != null)
        {
            sim.RegisterEntity(2, entry.Definition, new() { PX = spawn.X, PY = spawn.Y, PZ = spawn.Z - 3 }, entry.BakedAnimation);
            sim.SetRespawnPosition(2, spawn.X, spawn.Y, spawn.Z - 3, spawn.Yaw);
            sim.BeginOnlineCourse();
            sim.Tick(new());
            // Isolate traversal from the shared target gate: a rival has claimed the points.
            foreach (var target in Arena.Arcade!.Course!.Targets)
                for (int hit = 0; hit < target.Hits; hit++)
                {
                    sim.Resolver.Spawn(new Hitbox
                    {
                        OwnerId = 2, AttackSlot = 1, ActivationId = sim.CurrentTick + 10,
                        X = target.Position.X, Y = target.Position.Y, Z = target.Position.Z,
                        Radius = .2f, Shape = HitboxShape.Sphere, Damage = 1, DurationTicks = 1
                    });
                    sim.Tick(new());
                }
            sim.WithdrawCoursePlayer(2);
            if (sim.CourseFor(1)!.Value.BrokenTargets != Arena.Arcade.Course.Targets.Count)
                throw new InvalidOperationException("Rival target setup did not clear the finish gate.");
        }
        else sim.BeginCourseAttempt(1);
        sim.Tick(new());
        for (int i = 0; i < 2; i++) sim.Tick(new() { [1] = default });
        return sim;
    }

    public bool SnapshotRoundTrips()
    {
        var before = Sim.CaptureSnapshot();
        var prefix = Enumerable.Range(0, 45).Select(i => new InputState
        {
            Jump = i == 0 || i == 24, JumpHeld = true, MoveY = .4f,
            ActiveSlot = i == 30 ? AbilitySlots.Normal1 : (byte)0
        }).ToArray();
        foreach (var input in prefix) Sim.Tick(new() { [1] = input });
        var after = Sim.CaptureSnapshot();
        Sim.RestoreSnapshot(before);
        if (!before.ContentEquals(Sim.CaptureSnapshot())) return false;
        foreach (var input in prefix) Sim.Tick(new() { [1] = input });
        bool equal = after.ContentEquals(Sim.CaptureSnapshot());
        Sim.RestoreSnapshot(before);
        return equal;
    }

    public void Run(CourseRoute route)
    {
        _format = route.Format;
        _plainJumpsOnly = route.PlainJumpsOnly;
        Sim = Fresh();
        try { UsesSnapshots = _allowSnapshots && SnapshotRoundTrips(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            UsesSnapshots = false;
        }
        if (!UsesSnapshots) Sim = Fresh();
        RoutePoints.Clear();
        RoutePoints.Add(new(State.PX, State.PY, State.PZ));
        _log($"{route.ArenaKey}/{_selector} rollback={(UsesSnapshots ? "snapshot" : "input replay")}");
        for (int index = 0; index < route.Steps.Length; index++)
        {
            var step = route.Steps[index];
            _bestScore = float.PositiveInfinity; _bestHorizontal = _bestVertical = float.PositiveInfinity;
            _bestPlan = "none";
            _attempts = 0;
            bool success = step switch
            {
                Go go => Walk(new(go.X, go.Z)),
                Hop hop => Jump(hop),
                Break hit => BreakTarget(hit.TargetId),
                _ => throw new ArgumentOutOfRangeException(nameof(step))
            };
            if (!success) throw Failure(route, index, step, "No candidate succeeded.");
            Landings.Add((step, Inputs.Count, State));
            if (step is Go goPoint) RoutePoints.Add(new(goPoint.X, State.PY, goPoint.Z));
            else if (step is Hop hopPoint) RoutePoints.Add(new(hopPoint.X, State.PY, hopPoint.Z));
            _log($"step {index} {step} at ({State.PX:F2},{Feet:F2},{State.PZ:F2}) t{Inputs.Count}");
        }
        var course = Sim.CourseFor(1)!.Value;
        if (course.Phase != CharacterCoursePhase.Completed || State.Deaths != 0)
            throw Failure(route, route.Steps.Length - 1, route.Steps.Last(), $"Ended {course.Phase}, " +
                $"{course.BrokenTargets}/{course.TotalTargets} targets, deaths={State.Deaths}.");
        VerifyReplay();
        _log($"RESULT {route.ArenaKey}/{_selector} PASS ticks={course.ElapsedTicks} specials=[{string.Join(",", SpecialTargets)}]");
    }

    public void Calibrate(float x, float feet, float z, params RouteStep[] steps)
    {
        UsesSnapshots = _allowSnapshots && SnapshotRoundTrips();
        var state = State;
        state.PX = x; state.PY = feet + Fighter.CapsuleHeight * .5f; state.PZ = z;
        state.VX = state.VY = state.VZ = 0; state.IsGrounded = true;
        state.JumpsLeft = Fighter.Movement.MaxJumps; state.AirDodgesLeft = 1;
        Sim.SetState(1, state);
        foreach (var step in steps)
        {
            bool success = step switch
            {
                Go go => Walk(new(go.X, go.Z)),
                Hop hop => Jump(hop),
                _ => throw new ArgumentException("Calibration expects movement steps.")
            };
            if (!success) throw new InvalidOperationException($"Leg {_selector} {step} failed at {State.PX},{Feet},{State.PZ}.");
        }
    }

    private InvalidOperationException Failure(CourseRoute route, int index, RouteStep step, string reason)
        => new($"{route.ArenaKey}/{_selector} step {index} {step}; " +
            $"position ({State.PX:F2},{Feet:F2},{State.PZ:F2}); best closest miss horizontal {_bestHorizontal:F2} m, " +
            $"vertical shortfall {_bestVertical:F2} m at {_bestPosition}; plan {_bestPlan}; {_attempts} candidates; " +
            $"committed {Inputs.Count} inputs. Specials [{string.Join(",", SpecialTargets)}]. {reason}");

    private void VerifyReplay()
    {
        var replay = Fresh();
        foreach (var input in Inputs) replay.Tick(new() { [1] = input });
        bool equal = UsesSnapshots
            ? Sim.CaptureSnapshot().ContentEquals(replay.CaptureSnapshot())
            : State.Equals(replay.GetState(1)) && Sim.Course.Equals(replay.Course)
                && Enumerable.Range(0, Arena.Arcade!.Course!.Targets.Count)
                    .All(i => Sim.CourseTargetHitsLeft(i) == replay.CourseTargetHitsLeft(i));
        if (!equal) throw new InvalidOperationException("Committed inputs did not reproduce the completed course.");
    }

    private WorldSnapshot? Save() => UsesSnapshots ? Sim.CaptureSnapshot() : null;
    private void Reset(WorldSnapshot? snapshot)
    {
        if (UsesSnapshots) Sim.RestoreSnapshot(snapshot!);
        else
        {
            Sim = Fresh();
            foreach (var input in Inputs) Sim.Tick(new() { [1] = input });
        }
        _trial.Clear();
    }
    private void Tick(InputState input)
    {
        _trial.Add(input);
        Sim.Tick(new() { [1] = input });
    }
    private void Commit(WorldSnapshot? start)
    {
        var accepted = _trial.ToArray();
        Reset(start);
        foreach (var input in accepted)
        {
            Sim.Tick(new() { [1] = input });
            Inputs.Add(input);
            FrameStates.Add(State);
            Events.AddRange(Sim.LastTickCourseEvents);
        }
        _trial.Clear();
    }
    private static short Yaw(Vector2 direction) => (short)MathF.Round(MathF.Atan2(direction.X, direction.Y) * 18000 / MathF.PI);
    private InputState Steering(Vector2 target, float brake = 1f)
    {
        var delta = target - Position;
        var velocity = new Vector2(State.VX, State.VZ);
        Vector2 stick;
        if (State.IsGrounded)
        {
            float deceleration = Fighter.Movement.RunSpeed * 60f / GroundSpeedTuning.StopTicks;
            float stopping = velocity.LengthSquared() / (2 * deceleration);
            bool coast = Vector2.Dot(delta, velocity) > 0 && delta.Length() <= stopping + .12f;
            stick = delta.Length() < .12f || coast ? Vector2.Zero : Vector2.Normalize(delta);
        }
        else
        {
            float acceleration = Fighter.Movement.AirAccelStick + Fighter.Movement.AirAccelBase;
            var desired = delta * 5f;
            float speed = Fighter.Movement.AirSpeedMax;
            if (desired.Length() > speed) desired = Vector2.Normalize(desired) * speed;
            var stopping = new Vector2(velocity.X * MathF.Abs(velocity.X), velocity.Y * MathF.Abs(velocity.Y)) / (2 * acceleration);
            if (Vector2.Dot(delta, velocity) > 0 && delta.Length() < stopping.Length() * brake + .08f)
                desired = Vector2.Zero;
            stick = desired / speed;
        }
        short yaw = Yaw(delta);
        return new() { MoveX = stick.X, MoveY = stick.Y, FacingYaw = yaw, AimYaw = yaw };
    }
    private void Observe(Vector2 target, float y, string plan)
    {
        float horizontal = Vector2.Distance(Position, target);
        float vertical = MathF.Max(0, y - Feet);
        float score = horizontal + vertical;
        if (score >= _bestScore) return;
        _bestScore = score; _bestHorizontal = horizontal; _bestVertical = vertical;
        _bestPosition = new(State.PX, Feet, State.PZ); _bestPlan = plan;
    }
    private bool Settle(Vector2 target, float y, int ticks = 28)
    {
        for (int i = 0; i < ticks; i++)
        {
            if (State.Deaths != 0) return false;
            Tick(Steering(target));
        }
        return State.Deaths == 0 && State.IsGrounded && Vector2.Distance(Position, target) < .42f && Feet >= y - .3f;
    }
    private bool Walk(Vector2 target)
    {
        var saved = Save();
        if (WalkTrial(target)) { Commit(saved); return true; }
        Reset(saved);
        return false;
    }
    private bool WalkTrial(Vector2 target)
    {
        float y = Feet;
        for (int i = 0; i < 600; i++)
        {
            Observe(target, y, "walk");
            if (State.Deaths != 0) return false;
            if (Sim.CourseFor(1)!.Value.Phase == CharacterCoursePhase.Completed) return true;
            if (State.IsGrounded && Vector2.Distance(Position, target) < .22f) return Settle(target, y);
            Tick(Steering(target));
        }
        return false;
    }

    private float Surface(Vector2 point)
    {
        float best = float.MinValue;
        foreach (var t in Arena.CollisionTriangles)
        {
            var a = new Vector2(t.AX, t.AZ); var b = new Vector2(t.BX, t.BZ); var c = new Vector2(t.CX, t.CZ);
            float cross = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (MathF.Abs(cross) < .0001f) continue;
            float u = ((b.Y - c.Y) * (point.X - c.X) + (c.X - b.X) * (point.Y - c.Y)) / cross;
            float v = ((c.Y - a.Y) * (point.X - c.X) + (a.X - c.X) * (point.Y - c.Y)) / cross;
            if (u >= -.001f && v >= -.001f && u + v <= 1.001f)
                best = MathF.Max(best, u * t.AY + v * t.BY + (1 - u - v) * t.CY);
        }
        return best;
    }

    private readonly record struct JumpPlan(int RunUp, int Held, int Second, int Dodge, float Brake, bool FastFall);
    private IEnumerable<JumpPlan> JumpPlans()
    {
        int apex = (int)MathF.Round(Fighter.Movement.JumpForce / Fighter.Movement.Gravity * 60) + Fighter.Movement.JumpSquatTicks;
        foreach (int run in new[] { 0, 1, 4, 8, 12 })
        foreach (int second in new[] { -1, apex, apex - 8, apex + 8, apex + 16, 18, 25, 36, 48 }.Distinct())
        foreach (int held in new[] { 65, 1 })
            yield return new(run, held, second, -1, 1f, false);
        if (_plainJumpsOnly) yield break;
        foreach (int run in new[] { 0, 6, 12 })
        foreach (int second in new[] { apex - 8, apex, apex + 12, 36 })
        foreach (int dodge in new[] { second + 12, second + 24, second + 36 })
        foreach (float brake in new[] { .6f, 1.5f })
            yield return new(run, 90, second, dodge, brake, true);
    }
    private bool Jump(Hop hop)
    {
        var saved = Save();
        var target = new Vector2(hop.X, hop.Z);
        float y = hop.MinLandY ?? Surface(target);
        Observe(target, y, "takeoff");
        foreach (var plan in JumpPlans())
        {
            Reset(saved); _attempts++;
            if (JumpTrial(target, y, plan)) { Commit(saved); return true; }
        }
        Reset(saved); return false;
    }
    private bool JumpTrial(Vector2 target, float y, JumpPlan plan)
    {
        if (!State.IsGrounded) return false;
        for (int i = 0; i < plan.RunUp; i++) Tick(Steering(target));
        if (!State.IsGrounded) return false;
        bool airborne = false;
        for (int i = 0; i < 220; i++)
        {
            if (State.Deaths != 0) return false;
            if (Sim.CourseFor(1)!.Value.Phase == CharacterCoursePhase.Completed) return true;
            Observe(target, y, plan.ToString());
            airborne |= !State.IsGrounded;
            if (airborne && State.IsGrounded && Vector2.Distance(Position, target) < .9f && Feet >= y - .3f)
                return Settle(target, y);
            var input = Steering(target, plan.Brake);
            input.Jump = i == 0 || i == plan.Second;
            input.JumpHeld = i < plan.Held;
            input.ShieldPressed = i == plan.Dodge && !State.IsGrounded;
            input.DownPressed = plan.FastFall && i > Math.Max(10, plan.Second + 6) && State.VY < 0 && Vector2.Distance(Position, target) < .35f && Feet > y;
            Tick(input);
        }
        return false;
    }

    private bool BreakTarget(string id)
    {
        int index = Arena.Arcade!.Course!.Targets.ToList().FindIndex(t => t.Id == id);
        if (index < 0) throw new ArgumentException("Unknown course target " + id);
        if (Sim.IsCourseTargetBroken(index)) return true;
        var origin = Position; float y = Feet;
        var target = Arena.Arcade.Course.Targets[index];
        var point = new Vector2(target.Position.X, target.Position.Z);
        var away = origin - point;
        if (away.Length() < .1f) away = new(0, -1);
        away = Vector2.Normalize(away);
        var directions = new[] { away, new Vector2(1, 0), new(0, -1), new(-1, 0), new(0, 1) }.Distinct().ToArray();
        foreach (bool special in new[] { false, true })
        {
            var slots = special ? new[] { AbilitySlots.A, AbilitySlots.R, AbilitySlots.E, AbilitySlots.F }
                : new[] { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3 };
            while (!Sim.IsCourseTargetBroken(index))
            {
                var saved = Save(); bool hit = false;
                foreach (var direction in directions)
                {
                    foreach (float range in new[] { 1f, .55f, 1.5f, 2.2f })
                    {
                        var approach = point + direction * range;
                        foreach (byte slot in slots)
                        foreach (int attack in new[] { -1, 8, 16, 26, 38, 52 })
                        {
                            if (hit) continue;
                            Reset(saved); _attempts++;
                            int before = Sim.CourseTargetHitsLeft(index);
                            if (!WalkTrial(approach)) continue;
                            for (int wait = 0; wait < 240 && (State.AttackSlot != 0 || State.GetCooldown(slot) != 0); wait++) Tick(default);
                            var delta = point - Position;
                            short yaw = Yaw(delta);
                            for (int turn = 0; turn < 3; turn++) Tick(new() { FacingYaw = yaw, AimYaw = yaw });
                            for (int tick = 0; tick < 170 && State.Deaths == 0; tick++)
                            {
                                var input = Steering(approach);
                                input.FacingYaw = input.AimYaw = yaw;
                                input.TargetEntityId = Sim.CourseTargetInputAlias(index);
                                input.Jump = attack >= 0 && (tick == 0 || (attack >= 26 && tick == 20));
                                input.JumpHeld = attack >= 0 && tick < 70;
                                input.ActiveSlot = tick == Math.Max(0, attack) ? slot : (byte)0;
                                input.IsAiming = special && tick >= Math.Max(0, attack) && tick < Math.Max(0, attack) + 22;
                                input.AimDistance = (ushort)Math.Clamp(Vector2.Distance(Position, point) * 100, 0, 6500);
                                Observe(point, target.Position.Y - Fighter.CapsuleHeight * .5f, $"attack {slot} at {attack}, range {range}");
                                Tick(input);
                                if (tick > Math.Max(0, attack) + 55 && State.IsGrounded && State.AttackSlot == 0) break;
                            }
                            if (State.Deaths != 0 || Sim.CourseTargetHitsLeft(index) >= before || !State.IsGrounded) continue;
                            if (!WalkTrial(origin) || Feet < y - .3f) continue;
                            Commit(saved); hit = true;
                            if (special) SpecialTargets.Add(id + "/" + (slot switch
                            {
                                AbilitySlots.A => "A", AbilitySlots.E => "E", AbilitySlots.R => "R", AbilitySlots.F => "F",
                                _ => throw new InvalidOperationException("Expected a special slot.")
                            }));
                            break;
                        }
                        if (hit) break;
                    }
                    if (hit) break;
                }
                if (!hit) { Reset(saved); break; }
            }
            if (Sim.IsCourseTargetBroken(index)) return true;
        }
        return false;
    }

    public static CourseRoute NeoMallShortcutRoute(bool saleBalloon, bool serviceLadder)
    {
        var steps = Routes.Single(r => r.ArenaKey == "wibou_mall").Steps.ToList();
        if (serviceLadder)
        {
            int start = steps.IndexOf(new Break("agent")) + 1;
            int end = steps.IndexOf(new Go(9.6f, 26.7f));
            steps.RemoveRange(start, end - start);
            steps.InsertRange(start, new RouteStep[]
            {
                new Go(15,16), new Hop(15,18.6f,11), new Hop(12.5f,19.2f,13),
                new Hop(11.5f,21.5f,15),
            });
        }
        if (saleBalloon)
        {
            int start = steps.IndexOf(new Break("podium_3")) + 1;
            int end = steps.IndexOf(new Go(12,-12));
            steps.RemoveRange(start, end - start);
            steps.InsertRange(start, new RouteStep[]
            {
                new Go(.4f,-30.6f), new Hop(3.8f,-26.5f,4), new Hop(6.4f,-23,4),
                new Hop(9,-19.5f,4),
            });
        }
        return new("wibou_mall", steps.Where(s => s is not Break).ToArray(), CourseFormat.Scramble, PlainJumpsOnly: true);
    }

    public static readonly CourseRoute[] Routes =
    {
        new("nyx_manor", NyxManorTests.Route(-1, quick: false, low: false), RacingSteps:
            NyxManorTests.Route(-1, quick: false, low: false).Where(s => s is not Break).ToArray()),
        new("fightguy_sanctuary", new RouteStep[]
        {
            new Go(-11,-32), new Break("target_01"), new Break("target_02"),
            new Go(-23.5f,-24), new Go(-28,-20), new Break("target_03"),
            new Go(-32.65f,-15.45f), new Hop(-35.5f,-13), new Go(-36.8f,-11.75f),
            new Hop(-41.5f,-4.5f), new Go(-41.7f,-5), new Hop(-37,2.5f),
            new Go(-35.9f,2.5f), new Hop(-32.5f,2.2f), new Break("target_04"),
            new Break("target_05"), new Go(-27,5), new Go(-25,6.4f),
            new Hop(-24,11.2f), new Go(-21,16.4f), new Hop(-20,19.5f),
            new Go(-20,21.5f), new Go(-18,26), new Break("target_06"),
            new Break("target_07"), new Go(-20,29.2f), new Hop(-20,34),
            new Go(-18.5f,35.1f), new Hop(-13,36.75f), new Go(-11.8f,35.7f),
            new Hop(-7,32.5f), new Go(-6.8f,31), new Hop(-6,28.5f),
            new Break("target_08"), new Break("target_09"), new Break("target_10"),
            new Go(-5,26), new Go(-1.6f,26), new Hop(2,26),
            new Go(6.3f,26), new Hop(12,26), new Go(16,18),
            new Break("target_11"), new Break("target_12"), new Go(19.3f,16),
            new Hop(26,16), new Go(27.3f,14.6f), new Hop(33,10),
            new Go(32.2f,9.1f), new Hop(32,1.5f), new Go(30.9f,.4f),
            new Hop(27.8f,-3), new Break("target_13"), new Break("target_14"),
            new Break("target_15"), new Go(26,-5), new Go(26.5f,-4),
            new Hop(28,-14), new Break("target_16"), new Go(25.7f,-15.7f),
            new Hop(19,-22), new Break("target_17"), new Go(21,-23.6f),
            new Hop(27,-28), new Break("target_18"), new Go(25.6f,-29.4f),
            new Hop(19,-36.5f), new Break("target_19"), new Go(17.4f,-35.5f),
            new Hop(10,-32), new Break("target_20"), new Go(7,-32),
        }),
        new("fightguy_pier", new RouteStep[]
        {
            new Go(10.4f,-43f), new Break("car"), new Go(2.2f,-33f), new Break("gallery_1"), new Go(2.2f,-31.5f), new Break("gallery_2"),
            new Go(2.2f,-30f), new Break("gallery_3"), new Go(2.2f,-28.5f), new Break("gallery_4"), new Go(2.2f,-27f), new Break("gallery_5"),
            new Go(-3.6f,-24.9f), new Break("striker_1"), new Break("striker_2"), new Break("striker_3"),
            new Go(-5.5f,-30f), new Hop(-10.5f,-30f), new Go(-10.2f,-30f),
            new Hop(-16.5f,-30f), new Go(-16f,-30f), new Break("dunk"), new Go(-15.5f,-30f),
            new Hop(-10.5f,-30f), new Go(-10.8f,-30f), new Hop(-5f,-30f),
            new Go(-4.5f,-18f), new Go(-7f,-9f), new Go(-9.6f,-9f), new Hop(-12.8f,-10.0f),
            new Go(-12.8f,-9.75f), new Break("line_1"), new Hop(-15.4f,-8.0f), new Go(-15.4f,-8.25f), new Break("line_2"),
            new Hop(-18.0f,-10.0f), new Go(-18f,-9.75f), new Break("line_3"), new Hop(-21.4f,-9f),
            new Go(-21.4f,-9f), new Break("line_4"), new Go(-28.6f,-9.5f), new Break("pinata"), new Go(-29f,-5.2f),
            new Hop(-30f,2.5f), new Go(-35.4f,1.6f), new Hop(-42f,-0.5f),
            new Go(-42.6f,-0.5f), new Hop(-48.5f,-0.5f), new Go(-47.6f,-0.5f), new Break("bell"),
            new Go(-47.6f,-0.5f), new Hop(-42f,-0.5f), new Go(-41.6f,-0.5f),
            new Hop(-34.5f,2f), new Go(-31f,5.4f), new Hop(-31f,12.5f),
            new Go(-32f,14.2f), new Hop(-34f,20.5f), new Go(-33.5f,21f),
            new Hop(-33f,26.5f), new Go(-30f,31.5f), new Go(-24.6f,29f),
            new Hop(-18.5f,29f), new Go(-17f,27.1f), new Break("booth_1"), new Go(-17f,30.7f), new Break("booth_2"),
            new Go(-15.6f,29f), new Hop(-10.5f,29f), new Go(-4.3f,27f), new Break("tower_1"),
            new Go(-4.3f,27f), new Break("tower_2"), new Go(-7.6f,30.2f), new Hop(-6.5f,32f),
            new Go(-5.8f,32.6f), new Hop(-3f,33.6f), new Go(-2.3f,33.6f),
            new Hop(0.5f,33.6f), new Go(1.2f,33.2f), new Hop(4f,32f),
            new Go(3.6f,31.3f), new Hop(1.5f,28.5f), new Go(-2.4f,27f), new Break("tower_3"),
            new Go(0.2f,27f), new Break("punch"), new Go(2.6f,27f), new Hop(6f,27f),
            new Go(7.6f,26f), new Hop(14.5f,26f), new Go(16.1f,26f),
            new Hop(21.5f,24f), new Go(22f,26.8f), new Break("coaster_1"), new Go(22f,28.3f), new Break("coaster_2"),
            new Go(22f,29.8f), new Break("coaster_3"), new Go(22f,31.3f), new Break("coaster_4"), new Go(21.5f,39.4f),
            new Hop(22f,45f), new Go(21.6f,47.2f), new Hop(21.5f,51.6f),
            new Go(21.5f,51.6f), new Break("prize"), new Go(20.7f,52f), new Hop(14f,50f),
            new Go(13f,43.3f), new Break("bag"), new Go(10.6f,48f), new Hop(7f,48f),
            new Go(5f,48f),
        }),
        new("manki_quarry", new RouteStep[]
        {
            new Go(-17f,-32.3f), new Hop(-17f,-24.6f), new Break("crate_01"),
            new Go(-17f,-23.3f), new Hop(-17f,-33f), new Go(-25f,-33.6f),
            new Hop(-25f,-45f), new Break("crate_02"), new Go(-25f,-45.8f),
            new Hop(-25f,-34.5f), new Go(-31f,-32.2f), new Hop(-31f,-22.4f),
            new Break("crate_03"), new Break("crate_04"), new Go(-31f,-20.8f),
            new Hop(-31f,-33f), new Go(-36f,-35.9f), new Hop(-36f,-42.5f),
            new Go(-36f,-43.4f), new Hop(-36f,-50.3f), new Break("crate_05"),
            new Go(-36f,-51.6f), new Hop(-36f,-42.5f), new Go(-36f,-43.5f),
            new Hop(-36f,-35f), new Go(-42f,-30f), new Go(-42f,-27.6f),
            new Hop(-42f,-23.5f), new Go(-43.0f,-22.5f), new Hop(-48f,-17f),
            new Break("crate_07"), new Break("crate_08"), new Break("crate_09"),
            new Go(-47.3f,-17.6f), new Hop(-42f,-23.5f), new Go(-40.9f,-22.4f),
            new Hop(-35f,-14f), new Go(-34.0f,-13.0f), new Hop(-30f,-11f),
            new Break("crate_06"), new Go(-29.4f,-12.0f), new Hop(-35f,-14f),
            new Go(-36.1f,-12.9f), new Hop(-41f,-8f), new Go(-41.6f,-6.9f),
            new Hop(-44f,-1f), new Go(-44f,0.1f), new Hop(-44f,4.5f),
            new Go(-33f,3.6f), new Hop(-33f,0f), new Break("crate_10"),
            new Go(-33f,0.6f), new Hop(-33f,5f), new Go(-26f,7f),
            new Go(-5f,6.6f), new Hop(-4f,2.1f), new Break("crate_12"),
            new Break("crate_13"), new Go(-4.6f,0f), new Hop(-6f,9f),
            new Go(-17.6f,15f), new Hop(-21.5f,15f), new Go(-22.6f,16.4f),
            new Hop(-25f,22f), new Go(-23.9f,23.1f), new Hop(-20f,29f),
            new Go(-18.9f,29f), new Hop(-14.5f,29f), new Go(-7f,31.4f),
            new Hop(-7f,37f), new Break("crate_11"), new Go(-7f,35.9f),
            new Hop(-7f,30.5f), new Go(1.4f,31.4f), new Hop(6f,35f),
            new Break("crate_14"), new Go(5f,34f), new Hop(0f,30f),
            new Go(1f,24f), new Go(1.6f,21.5f), new Hop(8f,19f),
            new Go(10.6f,16.4f), new Hop(16f,9f), new Go(17.4f,6.6f),
            new Hop(24f,4f), new Go(24.8f,3.6f), new Hop(30f,2f),
            new Break("crate_16"), new Go(29f,2.6f), new Hop(24f,4f),
            new Go(23.2f,4.6f), new Hop(16.5f,7.5f), new Go(14.8f,6.9f),
            new Hop(13f,-3f), new Go(12f,-5.4f), new Hop(8.4f,-12.4f),
            new Break("crate_15"), new Go(9f,-13.9f), new Hop(14.5f,-21.5f),
            new Go(16.2f,-22f), new Hop(23f,-24f), new Break("crate_17"),
            new Break("crate_18"), new Go(25.8f,-24f), new Hop(15f,-22f),
            new Go(15f,-23.4f), new Hop(14f,-30f), new Go(10f,-34f),
        }),
        new("wibou_mall", new RouteStep[]
        {
            new Go(-9.5f,-34f), new Break("row_1"), new Go(-9.5f,-32.6f), new Break("row_2"), new Go(-9.5f,-31.2f), new Break("row_3"),
            new Go(-9.5f,-29.8f), new Break("row_4"), new Go(-8.6f,-34f), new Hop(-2.0f,-34f),
            new Go(-2f,-36.2f), new Break("podium_1"), new Go(-2f,-34f), new Break("podium_2"), new Go(-2f,-31.8f), new Break("podium_3"),
            new Go(-2.6f,-34f), new Hop(-9.8f,-34f), new Go(-12f,-20.6f), new Break("tubeman"),
            new Go(-6.6f,-12f), new Hop(-2.3f,-12f), new Go(-1.9f,-12f),
            new Hop(2.3f,-12f), new Go(2.7f,-12f), new Hop(7.0f,-12f),
            new Go(12f,-12f), new Go(9.0f,3.0f), new Go(9f,4.8f), new Break("escalator_1"),
            new Go(9.0f,7.2f), new Hop(9.0f,10.4f), new Go(9f,10.2f), new Break("escalator_2"),
            new Go(9f,11.6f), new Break("escalator_3"), new Go(9.0f,14.2f), new Go(11.9f,15f), new Break("agent"),
            new Go(4f,15f), new Break("bridge3_1"), new Go(2.6f,15f), new Break("bridge3_2"), new Go(1.2f,15f), new Break("bridge3_3"),
            new Go(-0.2f,15f), new Break("bridge3_4"), new Go(-11f,15f), new Go(-11f,17f), new Break("food_1"),
            new Go(-11f,18.4f), new Break("food_2"), new Go(-11f,19.8f), new Break("food_3"), new Go(-11f,21.2f), new Break("food_4"),
            new Go(-11.4f,28.5f), new Break("hotdog"), new Go(-8.6f,27f), new Hop(-6.8f,27f),
            new Hop(-5.2f,29.2f), new Hop(-2.8f,27f), new Hop(-2.8f,23.6f),
            new Go(-7.3f,23f), new Go(-7.4f,22.4f), new Break("lip_1"), new Go(-7.4f,23.6f), new Break("lip_2"),
            new Go(-1f,23f), new Break("bridge4_1"), new Go(0.4f,23f), new Break("bridge4_2"), new Go(1.8f,23f), new Break("bridge4_3"),
            new Go(3.2f,23f), new Break("bridge4_4"), new Go(9.6f,26.7f), new Break("peek_1"), new Go(9.6f,31.5f), new Break("peek_2"),
            new Go(9.6f,36.3f), new Break("peek_3"), new Go(12.5f,35.9f), new Break("cop"), new Go(12f,42f),
        }),
    };
}
