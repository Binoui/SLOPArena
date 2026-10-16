using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Guards the baked-arena pipeline (issue #77): every shipped .arena file must
/// parse and carry real ground collision, and the file-driven ArenaRegistry
/// must serve them (no hardcoded fallback — Simulation grounded entities at
/// KillHeight + 1 when a stage had no baked heightmap). These tests fail at
/// bake/commit time instead of at the player's feet.
/// </summary>
public class ArenaShippingTests
{
    /// <summary>Repo root: tests run from tests/Shared.Tests/bin/Debug/net8.0/ (5 dirs up).</summary>
    private static string RepoRoot()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        Assert.True(Directory.Exists(Path.Combine(root, "data", "arenas")),
            $"repo data/arenas not found from {root} — are the baked arenas committed?");
        return root;
    }

    public static IEnumerable<object[]> ArenaFiles()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "data", "arenas"), "*.arena"))
            yield return new object[] { Path.GetFileName(file) };
    }

    [Theory]
    [MemberData(nameof(ArenaFiles))]
    public void ShippedArena_ParsesAndHasGroundCollision(string fileName)
    {
        string path = Path.Combine(RepoRoot(), "data", "arenas", fileName);
        var arenaOpt = ArenaBinaryFormat.LoadFromFile(path);
        Assert.True(arenaOpt.HasValue, $"failed to parse {fileName}");
        ArenaDefinition arena = arenaOpt.Value;

        Assert.NotNull(arena.Heightmap.Data);
        Assert.NotNull(arena.CollisionTriangles);
        Assert.NotEmpty(arena.CollisionTriangles);

        foreach (SpawnPoint spawn in arena.SpawnPoints)
        {
            float surface = arena.Heightmap.Sample(spawn.X, spawn.Z);
            Assert.True(surface > float.MinValue / 2f,
                $"{fileName}: no ground surface under spawn ({spawn.X:F1},{spawn.Z:F1})");
            Assert.True(surface > arena.KillHeight,
                $"{fileName}: ground {surface:F2} at spawn is below the blast zone {arena.KillHeight}");
            // Bug signature: Heightmap.Data == null makes Simulation ground at KillHeight + 1.
            Assert.NotEqual(arena.KillHeight + 1f, surface, precision: 2);
        }
    }

    [Fact]
    public void DirectLoad_CollisionQueriesCullDistantGeometry()
    {
        var source = new ArenaDefinition
        {
            MinX = 0, MaxX = 24, MinZ = 0, MaxZ = 4, KillHeight = -4,
            CollisionTriangles = new[]
            {
                new CollisionTriangle { AX = 0, AZ = 0, BX = 0, BZ = 2, CX = 2, CZ = 0 },
                new CollisionTriangle { AX = 20, AZ = 0, BX = 20, BZ = 2, CX = 22, CZ = 0 }
            }
        };
        var loaded = ArenaBinaryFormat.Deserialize(ArenaBinaryFormat.Serialize(source));
        Assert.True(loaded.HasValue);
        var arena = loaded.Value;
        var candidates = new int[source.CollisionTriangles.Length];

        int count = ArenaCollision.GetCandidateTrianglesForAabb(
            0, -1, 0, 2, 1, 2, in arena, candidates);

        Assert.Equal(new[] { 0 }, candidates.AsSpan(0, count).ToArray());
        Assert.Equal(0, ArenaCollision.GetCandidateTrianglesForAabb(
            40, -1, 0, 42, 1, 2, in arena, candidates));
        Assert.Equal(ArenaBinaryFormat.Serialize(source), ArenaBinaryFormat.Serialize(arena));
    }

    [Fact]
    public void Registry_AfterLoadFromDirectory_ServesBakedArenas()
    {
        ArenaRegistry.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "arenas"));
        Assert.Null(ArenaRegistry.Get("no-such-arena-xyz"));
        Assert.True(ArenaRegistry.Get("slop_pit").HasValue);
        Assert.True(ArenaRegistry.Get("splash_deck").HasValue);
        Assert.True(ArenaRegistry.Get("after_hours").HasValue);
        Assert.True(ArenaRegistry.Get("rec_center_roof").HasValue);
        Assert.True(ArenaRegistry.Get("picnic_panic").HasValue);
        Assert.True(ArenaRegistry.Get("table_manners").HasValue);
        Assert.True(ArenaRegistry.Get("last_stop").HasValue);
        Assert.True(ArenaRegistry.Get("training").HasValue);
        Assert.Null(ArenaRegistry.Get("square"));
        Assert.Null(ArenaRegistry.Get("steps"));
        Assert.Null(ArenaRegistry.Get("colosseum"));
    }

    [Theory]
    [MemberData(nameof(ArenaFiles))]
    public void ShippedArena_ResolvesSideAndTopBlastLines(string fileName)
    {
        // Every shipped stage must have real side/top kill lines (issue: side/top blast
        // zones were missing — only void death). Baked arenas carry meaningful bounds,
        // so the auto-derivation must produce finite lines strictly beyond the mesh.
        string path = Path.Combine(RepoRoot(), "data", "arenas", fileName);
        var arenaOpt = ArenaBinaryFormat.LoadFromFile(path);
        Assert.True(arenaOpt.HasValue, $"failed to parse {fileName}");
        ArenaDefinition arena = arenaOpt.Value;

        var lines = ArenaCollision.ResolveBlastLines(in arena);

        Assert.True(lines.KillTop < float.PositiveInfinity,
            $"{fileName}: top blast line is inactive");
        Assert.True(lines.KillTop > arena.Heightmap.Data.Max(),
            $"{fileName}: top line {lines.KillTop} not above highest surface {arena.Heightmap.Data.Max()}");
        Assert.True(lines.KillMinX < arena.MinX,
            $"{fileName}: min X line {lines.KillMinX} not beyond mesh {arena.MinX}");
        Assert.True(lines.KillMaxX > arena.MaxX,
            $"{fileName}: max X line {lines.KillMaxX} not beyond mesh {arena.MaxX}");
        Assert.True(lines.KillMinZ < arena.MinZ,
            $"{fileName}: min Z line {lines.KillMinZ} not beyond mesh {arena.MinZ}");
        Assert.True(lines.KillMaxZ > arena.MaxZ,
            $"{fileName}: max Z line {lines.KillMaxZ} not beyond mesh {arena.MaxZ}");
    }
    [Theory]
    [InlineData(CharacterClass.FightGuy, -7.5f, 1f)]
    [InlineData(CharacterClass.Manki, -7.5f, 1f)]
    [InlineData(CharacterClass.Wibou, -7.5f, 1f)]
    [InlineData(CharacterClass.Bonk, -7.5f, 1f)]
    [InlineData(CharacterClass.FightGuy, 7.5f, -1f)]
    [InlineData(CharacterClass.Manki, 7.5f, -1f)]
    [InlineData(CharacterClass.Wibou, 7.5f, -1f)]
    [InlineData(CharacterClass.Bonk, 7.5f, -1f)]
    public void ScifiCity_HeldRunCrossesRoofJoinsAndBridge(
        CharacterClass cls, float startX, float direction)
    {
        const float roofY = 13.25f;
        const float bridgeY = 13.37f;
        const float bridgeZ = -2.96f;
        var arena = LoadScifiCityDemo();
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(startX, bridgeZ);
        state.PY = TestHelpers.GroundPY(def, roofY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var input = TestHelpers.Input(moveX: direction);
        var inputs = new Dictionary<ulong, InputState> { [1] = input };

        const float endpointX = 6f;
        const int maxTicks = 120;
        int reachedTick = -1;
        for (int tick = 0; tick < maxTicks; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded,
                Trace(cls, "city roof joins and bridge", tick, input, current, "lost groundedness"));
            Assert.InRange(current.PY,
                TestHelpers.GroundPY(def, roofY) - 0.05f,
                TestHelpers.GroundPY(def, bridgeY) + 0.05f);
            if (direction * current.PX >= endpointX)
            {
                reachedTick = tick;
                break;
            }
        }

        var final = sim.GetState(1);
        int finalTick = reachedTick >= 0 ? reachedTick : maxTicks - 1;
        Assert.True(reachedTick >= 0,
            $"{cls}: city bridge route stalled before opposite roof; final={Trace(cls, "city roof joins and bridge", finalTick, input, final, "progress")}");
        Assert.InRange(final.PY,
            TestHelpers.GroundPY(def, roofY) - 0.03f,
            TestHelpers.GroundPY(def, roofY) + 0.03f);
    }
    [Fact]
    public void ScifiCity_HeldRunTraversesStairsWithoutJump()
    {
        var arena = LoadScifiCityDemo();
        var def = TestHelpers.FightGuyDef;

        ExerciseCityStairRoute(arena, def, "west stair", -6.8f, 2.94f, -0.3f, 1f);
        ExerciseCityStairRoute(arena, def, "east stair", 6.5f, 2.6f, 0.3f, -1f);
    }

    private static void ExerciseCityStairRoute(
        ArenaDefinition arena, CharacterDefinition def, string route,
        float startX, float startZ, float plateauX, float direction)
    {
        const float roofY = 13.25f;
        const float upperY = 16.543003f;
        const int maxTicks = 180;
        var state = TestHelpers.PlayerState(startX, startZ);
        state.PY = TestHelpers.GroundPY(def, roofY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = TestHelpers.Input(moveX: direction),
        };

        int upTick = -1;
        for (int tick = 0; tick < maxTicks; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded,
                Trace(CharacterClass.FightGuy, route, tick, inputs[1], current, "stair ascent lost groundedness"));
            Assert.InRange(current.PY,
                TestHelpers.GroundPY(def, roofY) - 0.05f,
                TestHelpers.GroundPY(def, upperY) + 0.05f);
            if (direction * (current.PX - plateauX) >= 0f)
            {
                upTick = tick;
                break;
            }
        }

        var upper = sim.GetState(1);
        Assert.True(upTick >= 0,
            $"{route}: held run stalled before upper plateau; final={Trace(CharacterClass.FightGuy, route, maxTicks - 1, inputs[1], upper, "stair ascent progress")}");
        Assert.InRange(upper.PY,
            TestHelpers.GroundPY(def, upperY) - 0.03f,
            TestHelpers.GroundPY(def, upperY) + 0.03f);

        inputs[1] = TestHelpers.Input(moveX: -direction);
        int downTick = -1;
        for (int tick = upTick + 1; tick <= upTick + maxTicks; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded,
                Trace(CharacterClass.FightGuy, route, tick, inputs[1], current, "stair descent lost groundedness"));
            Assert.InRange(current.PY,
                TestHelpers.GroundPY(def, roofY) - 0.05f,
                TestHelpers.GroundPY(def, upperY) + 0.05f);
            if (direction * (current.PX - startX) <= 0f)
            {
                downTick = tick;
                break;
            }
        }

        var roof = sim.GetState(1);
        Assert.True(downTick >= 0,
            $"{route}: reverse held run stalled before roof; final={Trace(CharacterClass.FightGuy, route, upTick + maxTicks, inputs[1], roof, "stair descent progress")}");
        Assert.InRange(roof.PY,
            TestHelpers.GroundPY(def, roofY) - 0.03f,
            TestHelpers.GroundPY(def, roofY) + 0.03f);
    }



    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void IndustrialRooftop_HeldRunStopsAtPenthouseWall(CharacterClass cls)
    {
        const float wallX = 2.5f;
        const float surfaceY = -1.2f;
        var arena = LoadIndustrialRooftop();
        RequireHorizontalSurface(arena, "penthouse wall route floor", 0f, surfaceY, 20.5f);
        RequireVerticalWall(arena, "penthouse west face", wallX, surfaceY, 20.5f);
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(0f, 20.5f);
        state.PY = TestHelpers.GroundPY(def, surfaceY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = TestHelpers.Input(moveX: 1f) };
        bool contacted = false;
        float contactX = 0f;

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.PX <= wallX - def.CapsuleRadius + 0.03f,
                Trace(cls, "penthouse wall", tick, inputs[1], current, "wall penetration"));
            Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def, surfaceY)) <= 0.03f,
                Trace(cls, "penthouse wall", tick, inputs[1], current, "wall climb"));
            Assert.True(current.IsGrounded,
                Trace(cls, "penthouse wall", tick, inputs[1], current, "groundedness"));
            if (!contacted && current.PX >= wallX - def.CapsuleRadius - 0.03f)
            {
                contacted = true;
                contactX = current.PX;
            }
        }

        Assert.True(contacted,
            $"{cls}: penthouse wall route never reached contact; final={Trace(cls, "penthouse wall", 119, inputs[1], sim.GetState(1), "contact")}");
        inputs[1] = TestHelpers.Input(moveX: -1f);
        for (int tick = 120; tick < 150; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.PX <= wallX - def.CapsuleRadius + 0.03f,
                Trace(cls, "penthouse wall reverse", tick, inputs[1], current, "penetration"));
            Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def, surfaceY)) <= 0.03f,
                Trace(cls, "penthouse wall reverse", tick, inputs[1], current, "climb"));
            Assert.True(current.IsGrounded,
                Trace(cls, "penthouse wall reverse", tick, inputs[1], current, "groundedness"));
        }

        var away = sim.GetState(1);
        Assert.True(contactX - away.PX >= 0.5f,
            $"{cls}: penthouse wall reverse failed; contactX={contactX:F3}, final={Trace(cls, "penthouse wall reverse", 149, inputs[1], away, "movement away")}");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void IndustrialRooftop_RunOffRoofFallsImmediately(CharacterClass cls)
    {
        const float roofY = 0f;
        const float roofEdgeX = 17f;
        var arena = LoadIndustrialRooftop();
        RequireHorizontalSurface(arena, "main roof walk-off", 12f, roofY, -5f);
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(12f, -5f);
        state.PY = TestHelpers.GroundPY(def, roofY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var input = TestHelpers.Input(moveX: 1f);
        var inputs = new Dictionary<ulong, InputState> { [1] = input };
        int leaveTick = -1;
        int fallTick = -1;

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.NotEqual(ActionState.LedgeHang, current.State);
            if (leaveTick < 0 && !current.IsGrounded)
                leaveTick = tick;
            if (leaveTick >= 0)
            {
                Assert.True(current.VY <= 0.001f,
                    Trace(cls, "main roof walk-off", tick, input, current, "upward launch"));
                if (current.VY < 0f && fallTick < 0)
                {
                    fallTick = tick;
                    Assert.True(tick - leaveTick <= 2,
                        Trace(cls, "main roof walk-off", tick, input, current, "fall delay"));
                    Assert.True(current.PY - def.CapsuleHeight * 0.5f < roofY - 0.001f,
                        Trace(cls, "main roof walk-off", tick, input, current, "height drop"));
                }
                if (current.PY - def.CapsuleHeight * 0.5f <= roofY - 1f)
                {
                    Assert.True(fallTick >= 0,
                        Trace(cls, "main roof walk-off", tick, input, current, "fall start"));
                    return;
                }
            }
        }

        var final = sim.GetState(1);
        Assert.True(leaveTick >= 0,
            $"{cls}: main roof route never left x={roofEdgeX}; final={Trace(cls, "main roof walk-off", 119, input, final, "leave")}");
        Assert.True(fallTick >= 0,
            $"{cls}: main roof route did not fall within two ticks; final={Trace(cls, "main roof walk-off", 119, input, final, "fall")}");
        Assert.True(final.PY - def.CapsuleHeight * 0.5f <= roofY - 1f,
            $"{cls}: main roof route did not descend 1m before timeout; final={Trace(cls, "main roof walk-off", 119, input, final, "depth")}");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void IndustrialRooftop_JumpBetweenServiceDecksLands(CharacterClass cls)
    {
        const float upperY = 4.43f;
        const float lowerY = 2.01f;
        var arena = LoadIndustrialRooftop();
        RequireHorizontalSurface(arena, "upper service deck", -2.5f, upperY, 2.5f);
        RequireHorizontalSurface(arena, "lower service deck", -7f, lowerY, 2.5f);
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(-2.5f, 2.5f);
        state.PY = TestHelpers.GroundPY(def, upperY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        bool tookOff = false;
        bool crossedGap = false;
        bool landed = false;
        int landingTick = -1;
        float landedX = 0f;

        for (int tick = 0; tick < 180; tick++)
        {
            var before = sim.GetState(1);
            bool jumpPress = tick == 0;
            float moveX = !tookOff || before.PX > (landed ? landedX - 0.75f : -7f) ? -1f : 0f;
            inputs[1] = TestHelpers.Input(moveX: moveX, jump: jumpPress, jumpHeld: true);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            if (!tookOff && !current.IsGrounded && current.VY > 0f)
                tookOff = true;
            if (tookOff && !crossedGap && before.PX > -4.57f && current.PX <= -4.57f)
                crossedGap = true;
            if (tookOff && current.IsGrounded && !landed)
            {
                Assert.True(current.PX <= -4.57f + 0.03f,
                    Trace(cls, "service deck jump", tick, inputs[1], current, "landed before lower deck"));
                landed = true;
                landingTick = tick;
                landedX = current.PX;
                Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def, lowerY)) <= 0.03f,
                    Trace(cls, "service deck jump", tick, inputs[1], current, "lower deck landing height"));
            }
            if (landed && tick - landingTick >= 20)
                break;
        }

        var final = sim.GetState(1);
        Assert.True(tookOff,
            $"{cls}: service deck route had no takeoff; final={Trace(cls, "service deck jump", 179, inputs[1], final, "takeoff")}");
        Assert.True(crossedGap,
            $"{cls}: service deck route did not cross the 0.57m gap airborne; final={Trace(cls, "service deck jump", 179, inputs[1], final, "gap")}");
        Assert.True(landed,
            $"{cls}: service deck route did not land on the lower deck; final={Trace(cls, "service deck jump", 179, inputs[1], final, "landing")}");
        Assert.True(final.IsGrounded && MathF.Abs(final.PY - TestHelpers.GroundPY(def, lowerY)) <= 0.03f,
            Trace(cls, "service deck jump", landingTick + 20, inputs[1], final, "post-landing support"));
        Assert.True(landedX - final.PX >= 0.5f,
            $"{cls}: lower-deck movement after landing was insufficient; landedX={landedX:F3}, final={Trace(cls, "service deck jump", landingTick + 20, inputs[1], final, "post-landing travel")}");
    }

    private static ArenaDefinition LoadScifiCityDemo()
    {
        string path = Path.Combine(RepoRoot(), "data", "arenas", "scifi_city_demo.arena");
        var arenaOpt = ArenaBinaryFormat.LoadFromFile(path);
        Assert.True(arenaOpt.HasValue, "scifi_city_demo: failed to parse shipped arena");
        ArenaDefinition arena = arenaOpt.Value;
        Assert.Equal("scifi_city_demo", arena.Name);
        Assert.NotEmpty(arena.CollisionTriangles);
        return arena;
    }

    private static ArenaDefinition LoadIndustrialRooftop()
    {
        string path = Path.Combine(RepoRoot(), "data", "arenas", "industrial_rooftop.arena");
        var arenaOpt = ArenaBinaryFormat.LoadFromFile(path);
        Assert.True(arenaOpt.HasValue, "industrial_rooftop: failed to parse shipped arena");
        ArenaDefinition arena = arenaOpt.Value;
        Assert.Equal("industrial_rooftop", arena.Name);
        Assert.NotEmpty(arena.CollisionTriangles);
        return arena;
    }

    private static void RequireHorizontalSurface(ArenaDefinition arena, string route, float x, float y, float z)
    {
        foreach (var triangle in arena.CollisionTriangles)
        {
            if (MathF.Abs(triangle.AY - y) > 0.01f
                || MathF.Abs(triangle.BY - y) > 0.01f
                || MathF.Abs(triangle.CY - y) > 0.01f)
                continue;
            float minX = MathF.Min(triangle.AX, MathF.Min(triangle.BX, triangle.CX));
            float maxX = MathF.Max(triangle.AX, MathF.Max(triangle.BX, triangle.CX));
            float minZ = MathF.Min(triangle.AZ, MathF.Min(triangle.BZ, triangle.CZ));
            float maxZ = MathF.Max(triangle.AZ, MathF.Max(triangle.BZ, triangle.CZ));
            if (x >= minX - 0.01f && x <= maxX + 0.01f
                && z >= minZ - 0.01f && z <= maxZ + 0.01f)
                return;
        }
        Assert.Fail($"industrial_rooftop route prerequisite missing horizontal surface '{route}' at ({x:F2},{y:F2},{z:F2})");
    }

    private static void RequireVerticalWall(ArenaDefinition arena, string route, float x, float y, float z)
    {
        foreach (var triangle in arena.CollisionTriangles)
        {
            if (MathF.Abs(triangle.AX - x) > 0.01f
                || MathF.Abs(triangle.BX - x) > 0.01f
                || MathF.Abs(triangle.CX - x) > 0.01f)
                continue;
            float minY = MathF.Min(triangle.AY, MathF.Min(triangle.BY, triangle.CY));
            float maxY = MathF.Max(triangle.AY, MathF.Max(triangle.BY, triangle.CY));
            float minZ = MathF.Min(triangle.AZ, MathF.Min(triangle.BZ, triangle.CZ));
            float maxZ = MathF.Max(triangle.AZ, MathF.Max(triangle.BZ, triangle.CZ));
            if (y >= minY - 0.01f && y <= maxY + 0.01f
                && z >= minZ - 0.01f && z <= maxZ + 0.01f)
                return;
        }
        Assert.Fail($"industrial_rooftop route prerequisite missing wall '{route}' at ({x:F2},{y:F2},{z:F2})");
    }

    private static string Trace(CharacterClass cls, string route, int tick, InputState input,
        CharacterState state, string contract)
        => $"{cls} route={route} tick={tick} contract={contract} input=({input.MoveX:F2},{input.MoveY:F2}) " +
           $"pos=({state.PX:F3},{state.PY:F3},{state.PZ:F3}) " +
           $"vel=({state.VX:F3},{state.VY:F3},{state.VZ:F3}) grounded={state.IsGrounded} state={state.State}";
}
