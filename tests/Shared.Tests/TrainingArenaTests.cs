using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public sealed class TrainingArenaTests(ITestOutputHelper output)
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly JsonElement Spec = ReadSpec();
    private static readonly ArenaDefinition Arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(Root, "data/arenas/training.arena"))
        ?? throw new InvalidOperationException("Training arena could not be loaded.");
    private static JsonElement ReadSpec()
    {
        string source = Environment.GetEnvironmentVariable("SLOPARENA_TRAINING_LAYOUT")
            ?? Path.Combine(Root, "tools/blender/training/layout.py");
        using var process = Process.Start(new ProcessStartInfo("python3")
        {
            ArgumentList = { source, "--spec-only" }, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        string json = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd());
        return JsonDocument.Parse(json).RootElement.GetProperty("spec").Clone();
    }
    private readonly record struct Solid(string Name, float X0, float Z0, float X1, float Z1, float Top, float Bottom)
    {
        public Vector2 Centre => new((X0 + X1) / 2, (Z0 + Z1) / 2);
        public bool Contains(CharacterState state) => state.PX >= X0 && state.PX <= X1 && state.PZ >= Z0 && state.PZ <= Z1;
    }
    private static IEnumerable<Solid> Solids => Spec.GetProperty("solids").EnumerateArray().Select(s =>
    {
        var f = s.GetProperty("footprint");
        return new Solid(s.GetProperty("name").GetString()!, f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle(), f[3].GetSingle(),
            s.GetProperty("top").GetSingle(), s.GetProperty("bottom").GetSingle());
    });
    private static Solid Piece(string name) => Solids.Single(s => s.Name == name);
    public static IEnumerable<object[]> Fighters() => BuiltInRosterManifestCodec.Load(Path.Combine(Root, "content-cooked/roster/manifest.json"))
        .Entries.Select(e => new object[] { e.Selector });
    public static IEnumerable<object[]> SpawnCases() => Fighters().SelectMany(f => Enumerable.Range(0, 4).Select(i => new object[] { f[0], i }));

    private static ServerSimulation Fresh(CharacterClass fighter, float x, float z, float surface, bool grounded = true)
    {
        var entry = BuiltInContentResolver.Resolve(fighter);
        var state = TestHelpers.PlayerState(x, z);
        state.PY = surface + entry.Definition.CapsuleHeight * .5f;
        state.IsGrounded = grounded;
        state.JumpsLeft = entry.Definition.Movement.MaxJumps;
        state.AirTimeTicks = entry.Definition.Movement.FloatWindowTicks;
        var sim = TestHelpers.MakeSim(Arena);
        sim.RegisterEntity(1, entry.Definition, state, entry.BakedAnimation);
        return sim;
    }
    private static void Tick(ServerSimulation sim, InputState input = default) => sim.Tick(new() { [1] = input });

    [Theory, MemberData(nameof(SpawnCases))]
    public void EverySpawnIsGroundedOnItsNamedSurface(CharacterClass fighter, int index)
    {
        var point = Spec.GetProperty("spawns")[index];
        float x = point[0].GetSingle(), z = point[1].GetSingle();
        float top = Solids.Where(s => s.X0 <= x && x <= s.X1 && s.Z0 <= z && z <= s.Z1).Max(s => s.Top);
        var spawn = Arena.SpawnPoints[index];
        Assert.Equal(x, spawn.X, 3); Assert.Equal(z, spawn.Z, 3);
        Assert.Equal(top + Spec.GetProperty("spawn_body_half_height").GetSingle(), spawn.Y, 3);
        Assert.Equal(point[2].GetSingle() * MathF.PI / 180, spawn.Yaw, 3);
        Assert.Equal(top, Arena.Heightmap.Sample(x, z), 3);
        var sim = Fresh(fighter, x, z, top, false);
        sim.SetState(1, sim.GetState(1) with { PY = spawn.Y });
        for (int t = 0; t < 60; t++) Tick(sim);
        var state = sim.GetState(1);
        Assert.True(state.IsGrounded); Assert.Equal(0, state.Deaths);
        Assert.Equal(top + TestHelpers.ResolveDef(fighter).CapsuleHeight * .5f, state.PY, 2);
    }

    [Fact]
    public void BakeDerivesBlastLinesFromEverySolid()
    {
        var lines = ArenaCollision.ResolveBlastLines(in Arena);
        Assert.Equal(Solids.Min(s => s.X0) - 10, lines.KillMinX, 3);
        Assert.Equal(Solids.Max(s => s.X1) + 10, lines.KillMaxX, 3);
        Assert.Equal(Solids.Min(s => s.Z0) - 10, lines.KillMinZ, 3);
        Assert.Equal(Solids.Max(s => s.Z1) + 10, lines.KillMaxZ, 3);
        Assert.Equal(Solids.Max(s => s.Top) + 20, lines.KillTop, 3);
        Assert.Equal(Spec.GetProperty("kill_y").GetSingle(), lines.KillHeight, 3);
        foreach (var s in Solids) Assert.Equal(s.Top, Arena.Heightmap.Sample(s.Centre.X, s.Centre.Y), 3);
    }

    [Theory, MemberData(nameof(Fighters))]
    public void KnockbackKillsThroughNorthAndSouth(CharacterClass fighter)
    {
        foreach (float dz in new[] { -1f, 1f })
        {
            var sim = Fresh(fighter, 0, dz * 11, 5, false);
            var state = sim.GetState(1);
            Simulation.ApplyKnockbackForce(ref state, 0, dz, 0, 120, 1); state.HitstunTicks = 60;
            sim.SetState(1, state);
            bool died = false;
            for (int t = 0; t < 120; t++)
            {
                Tick(sim);
                if (sim.LastTickDeaths.Count == 0) continue;
                Assert.Equal(dz < 0 ? "minZ" : "maxZ", Assert.Single(sim.LastTickDeaths).Boundary);
                died = true; break;
            }
            Assert.True(died);
        }
    }

    [Theory, MemberData(nameof(Fighters))]
    public void TrenchFallKillsAndEveryFighterRecoversOutOfIt(CharacterClass fighter)
    {
        var yard = Piece("DrillYard"); var pad = Piece("TestPlatform");
        float middle = (yard.X1 + pad.X0) / 2;
        var fall = Fresh(fighter, middle, 0, -2, false);
        bool died = false;
        for (int t = 0; t < 180; t++)
        {
            Tick(fall);
            if (fall.LastTickDeaths.Count == 0) continue;
            Assert.Equal("bottom", Assert.Single(fall.LastTickDeaths).Boundary); died = true; break;
        }
        Assert.True(died);
        Assert.True(Search(fighter, new(middle, 0), -.7f, pad with { X0 = -14, X1 = -10, Z0 = -2, Z1 = 2 }, 1, false, true, out string recoveryPlan), recoveryPlan);
    }

    private static bool Jump(CharacterClass fighter, Vector2 start, float surface, Solid target, int jumps, bool running,
        int second, int recovery, int charge, out string result)
    {
        var def = TestHelpers.ResolveDef(fighter);
        bool grounded = MathF.Abs(Arena.Heightmap.Sample(start.X, start.Y) - surface) < .01f;
        var sim = Fresh(fighter, start.X, start.Y, surface, grounded);
        if (!grounded) sim.SetState(1, sim.GetState(1) with { JumpsLeft = (byte)(def.Movement.MaxJumps - 1) });
        bool airborne = false;
        var direction = Vector2.Normalize(target.Centre - start);
        if (running)
        {
            for (int t = 0; t < 60; t++)
            {
                if (sim.GetState(1).PX >= Piece("TestPlatform").X1 - .9f) break;
                Tick(sim, new() { MoveX = 1, FacingYaw = 9000, AimYaw = 9000 });
            }
        }
        float maxX = start.X, apex = surface;
        for (int t = 0; t < 240; t++)
        {
            var s = sim.GetState(1);
            float feet = s.PY - def.CapsuleHeight * .5f;
            maxX = MathF.Max(maxX, s.PX); apex = MathF.Max(apex, feet);
            airborne |= !s.IsGrounded;
            if (s.Deaths != 0) break;
            if (airborne && s.IsGrounded && target.Contains(s) && MathF.Abs(feet - target.Top) < .05f)
            {
                result = $"land t={t}, second={second}, recovery={recovery}, charge={charge}, apex={apex:F2}";
                return true;
            }
            var delta = target.Centre - new Vector2(s.PX, s.PZ);
            Vector2 stick = direction;
            if (!s.IsGrounded)
            {
                var desired = delta * 5;
                if (desired.Length() > def.Movement.AirSpeedMax) desired = Vector2.Normalize(desired) * def.Movement.AirSpeedMax;
                stick = desired / def.Movement.AirSpeedMax;
            }
            else if (airborne) break;
            short yaw = (short)MathF.Round(MathF.Atan2(direction.X, direction.Y) * 18000 / MathF.PI);
            Tick(sim, new()
            {
                MoveX = stick.X, MoveY = stick.Y, FacingYaw = yaw, AimYaw = yaw,
                Jump = t == 0 || (jumps == 2 && t == second), JumpHeld = true,
                ActiveSlot = t == recovery ? AbilitySlots.E : (byte)0,
                IsAiming = charge >= 0 && t >= recovery && t < recovery + charge,
                AimDistance = (ushort)Math.Clamp(delta.Length() * 100, 0, 6500),
            });
        }
        result = $"miss maxX={maxX:F2}, apex={apex:F2}, second={second}, recovery={recovery}, final={sim.GetState(1).PX:F2}/{sim.GetState(1).PY:F2}/{sim.GetState(1).PZ:F2}";
        return false;
    }
    private static bool Search(CharacterClass fighter, Vector2 start, float surface, Solid target, int jumps, bool running, bool recovery, out string result)
    {
        result = "no plan";
        foreach (int second in jumps == 1 ? new[] { -1 } : Enumerable.Range(8, 73))
        foreach (int ability in recovery ? new[] { second + 4, second + 12, second + 24, second + 36, second + 48 } : new[] { -1 })
        foreach (int charge in recovery && fighter == CharacterClass.Bonk ? new[] { 1, 12, 24 } : new[] { -1 })
            if (Jump(fighter, start, surface, target, jumps, running, second, ability, charge, out result)) return true;
        return false;
    }

    [Theory, MemberData(nameof(Fighters))]
    public void TrainingRigTrenchAndShelfTeachTheirJumpHeights(CharacterClass fighter)
    {
        var step = Piece("StepDeck"); var block = Piece("StepBlock"); var yard = Piece("DrillYard"); var shelf = Piece("Shelf");
        foreach (var (start, y, target) in new[]
        {
            (new Vector2(-24, 1.8f), 0f, step),
            (new Vector2(-24.8f, 10), step.Top, block),
            (new Vector2(-13.7f, -3), 0f, yard with { Z0 = -5, Z1 = -1, X0 = -31, X1 = -17 }),
            (shelf.Centre, shelf.Top, yard with { X0 = -31, X1 = -27, Z0 = -7, Z1 = -2 }),
        }) Assert.True(Search(fighter, start, y, target, 1, false, false, out string plan), $"{fighter}/{target.Name}: {plan}");
        bool direct = Search(fighter, new Vector2(-28, 13 - .6f), 0, block, 1, false, false, out string directPlan);
        var probe = MovementProbe.Measure(TestHelpers.ResolveDef(fighter), TestHelpers.TestArena());
        Assert.Equal(probe.Jump.ApexHeight >= 3, direct);
        Assert.True(Search(fighter, new(-28, 12.4f), 0, block, 2, false, false, out string doublePlan), doublePlan);
        output.WriteLine($"{fighter} block single={direct}, apex={probe.Jump.ApexHeight:F2}, {directPlan}");
    }

    [Theory, MemberData(nameof(Fighters))]
    public void IslandLadderMeasuresSingleDoubleAndRecovery(CharacterClass fighter)
    {
        var results = new List<string>();
        foreach (string name in new[] { "NearIsland", "MidIsland", "FarIsland" })
        {
            var target = Piece(name); var start = new Vector2(13, target.Centre.Y);
            bool single = Search(fighter, start, 0, target, 1, true, false, out string one);
            bool twice = Search(fighter, start, 0, target, 2, true, false, out string two);
            bool recover = Search(fighter, start, 0, target, 2, true, true, out string three);
            results.Add($"{name} gap={target.X0 - Piece("TestPlatform").X1:g} single={single} double={twice} recovery={recover}; {one}; {two}; {three}");
        }
        foreach (string line in results) output.WriteLine($"ISLAND {fighter} {line}");
        Assert.Contains("single=True", results[0]);
        Assert.Contains("single=False double=True", results[1]);
        bool exception = Spec.GetProperty("solids").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "FarIsland")
            .GetProperty("double_jump_exceptions").EnumerateArray().Any(s => s.GetString() == fighter.ToString());
        Assert.Contains($"single=False double={exception} recovery=True", results[2]);
    }
}
