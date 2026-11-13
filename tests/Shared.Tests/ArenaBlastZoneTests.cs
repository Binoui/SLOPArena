using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Blast-zone coverage: the 0 = auto sentinel, bounds/heightmap derivation, the
/// death check on every plane (bottom/top/±X/±Z), and v5 format round-trip with
/// v4 backward compatibility (v4 files carry no lines → 0 → auto-derive).
/// </summary>
public class ArenaBlastZoneTests
{
    /// <summary>Arena with meaningful bounds (±20) and a flat floor at Y=0.</summary>
    private static ArenaDefinition MakeArena()
    {
        int w = 200, h = 200;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            MinX = -20f, MaxX = 20f, MinZ = -20f, MaxZ = 20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap
            {
                Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f,
            },
        };
    }

    // ── Resolver: derivation + sentinel ──

    [Fact]
    public void Resolve_UnsetLines_DeriveFromBoundsAndHeightmap()
    {
        var arena = MakeArena(); // bounds ±20, floor 0, all lines 0 = unset
        var lines = ArenaCollision.ResolveBlastLines(in arena);

        Assert.Equal(-20f, lines.KillHeight);
        Assert.Equal(ArenaCollision.TopBlastMargin, lines.KillTop); // floor 0 + margin
        Assert.Equal(-20f - ArenaCollision.SideBlastMargin, lines.KillMinX);
        Assert.Equal(20f + ArenaCollision.SideBlastMargin, lines.KillMaxX);
        Assert.Equal(-20f - ArenaCollision.SideBlastMargin, lines.KillMinZ);
        Assert.Equal(20f + ArenaCollision.SideBlastMargin, lines.KillMaxZ);
    }

    [Fact]
    public void Resolve_AuthoredLines_WinOverDerivation()
    {
        var arena = MakeArena();
        arena.KillTop = 50f;
        arena.KillMinX = -5f;
        arena.KillMaxX = 99f;

        var lines = ArenaCollision.ResolveBlastLines(in arena);

        Assert.Equal(50f, lines.KillTop);
        Assert.Equal(-5f, lines.KillMinX);
        Assert.Equal(99f, lines.KillMaxX);
        // Unset planes still derive.
        Assert.Equal(-20f - ArenaCollision.SideBlastMargin, lines.KillMinZ);
        Assert.Equal(20f + ArenaCollision.SideBlastMargin, lines.KillMaxZ);
    }

    [Fact]
    public void Resolve_ZeroBoundsArena_KeepsVoidOnlyDeath()
    {
        var arena = MakeArena();
        arena.MinX = 0f; arena.MaxX = 0f; arena.MinZ = 0f; arena.MaxZ = 0f;

        var lines = ArenaCollision.ResolveBlastLines(in arena);

        Assert.Equal(-20f, lines.KillHeight);
        Assert.Equal(float.PositiveInfinity, lines.KillTop);
        Assert.Equal(float.NegativeInfinity, lines.KillMinX);
        Assert.Equal(float.PositiveInfinity, lines.KillMaxX);
        Assert.Equal(float.NegativeInfinity, lines.KillMinZ);
        Assert.Equal(float.PositiveInfinity, lines.KillMaxZ);
    }

    // ── Death checks on each plane ──

    [Fact]
    public void Tick_AboveTopKillLine_Respawns()
    {
        var arena = MakeArena();
        arena.KillTop = 30f;
        var sim = new ServerSimulation(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 40f;
        state.IsGrounded = false;
        sim.RegisterEntity(1, BuiltInContentResolver.ResolvePackage("fightguy").Definition, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(0u, result.DamagePercent);
        Assert.Equal(arena.SpawnPoints[0].Y, result.PY); // respawned at spawn
    }

    [Fact]
    public void Tick_BeyondPositiveXKillLine_Respawns()
    {
        var arena = MakeArena();
        arena.KillMaxX = 10f;
        var sim = new ServerSimulation(arena);
        var state = TestHelpers.PlayerState();
        state.PX = 15f;
        sim.RegisterEntity(1, BuiltInContentResolver.ResolvePackage("fightguy").Definition, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(arena.SpawnPoints[0].X, result.PX);
    }

    [Fact]
    public void Tick_BeyondNegativeXKillLine_Respawns()
    {
        var arena = MakeArena();
        arena.KillMinX = -10f;
        var sim = new ServerSimulation(arena);
        var state = TestHelpers.PlayerState();
        state.PX = -15f;
        sim.RegisterEntity(1, BuiltInContentResolver.ResolvePackage("fightguy").Definition, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(1, result.Deaths);
    }

    [Fact]
    public void Tick_BeyondZKillLines_Respawns()
    {
        var arena = MakeArena();
        arena.KillMaxZ = 10f;
        var sim = new ServerSimulation(arena);
        var state = TestHelpers.PlayerState();
        state.PZ = 15f;
        sim.RegisterEntity(1, BuiltInContentResolver.ResolvePackage("fightguy").Definition, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(1, result.Deaths);
        Assert.Equal(arena.SpawnPoints[0].Z, result.PZ);
    }

    [Fact]
    public void Tick_InsideAllLines_NoDeath()
    {
        var arena = MakeArena(); // derived lines ±30 side, 20 top — entity at (5, 2, 5) is inside
        var sim = new ServerSimulation(arena);
        var state = TestHelpers.PlayerState();
        state.PY = 2f;
        sim.RegisterEntity(1, BuiltInContentResolver.ResolvePackage("fightguy").Definition, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, default } });

        var result = sim.GetState(1);
        Assert.Equal(0, result.Deaths);
    }

    // ── Format v5 ──

    [Fact]
    public void Format_RoundTripsAuthoredBlastLines()
    {
        var arena = MakeArena();
        arena.KillTop = 40f;
        arena.KillMinX = -33f;
        arena.KillMaxX = 33f;
        arena.KillMinZ = -31f;
        arena.KillMaxZ = 31f;

        var round = ArenaBinaryFormat.Deserialize(ArenaBinaryFormat.Serialize(arena));

        Assert.NotNull(round);
        Assert.Equal(40f, round.Value.KillTop);
        Assert.Equal(-33f, round.Value.KillMinX);
        Assert.Equal(33f, round.Value.KillMaxX);
        Assert.Equal(-31f, round.Value.KillMinZ);
        Assert.Equal(31f, round.Value.KillMaxZ);
        Assert.Equal(-20f, round.Value.KillHeight);
    }

    [Fact]
    public void Format_V4File_LeavesLinesUnset_AutoDerives()
    {
        var bytes = BuildV4File();
        var arenaOpt = ArenaBinaryFormat.Deserialize(bytes);

        Assert.True(arenaOpt.HasValue, "v4 file must still parse");
        var arena = arenaOpt.Value;

        // No lines in the file → all 0 (unset) → resolver derives from bounds.
        Assert.Equal(0f, arena.KillTop);
        Assert.Equal(0f, arena.KillMinX);
        Assert.Equal(0f, arena.KillMaxX);
        Assert.Equal(0f, arena.KillMinZ);
        Assert.Equal(0f, arena.KillMaxZ);
        Assert.Equal(-20f, arena.KillHeight);
        Assert.Equal(-20f, arena.MinX);

        var lines = ArenaCollision.ResolveBlastLines(in arena);
        Assert.Equal(ArenaCollision.TopBlastMargin, lines.KillTop); // floor 0 + margin
        Assert.Equal(-20f - ArenaCollision.SideBlastMargin, lines.KillMinX);
        Assert.Equal(20f + ArenaCollision.SideBlastMargin, lines.KillMaxX);
    }

    /// <summary>Hand-builds a v4 .arena byte stream: no blast-line fields exist in v4.</summary>
    private static byte[] BuildV4File()
    {
        const uint magic = 0x4E455241;
        const uint version = 4;
        int size = 8;
        size += 4 + 4; // name "test"
        size += 4 + 12; // display "Test Arena"
        size += 4; // preview color "" (v4)
        size += 4; // KillHeight
        size += 16; // bounds
        size += 4; // spawn count 0
        size += 24; // heightmap header
        size += 4; // hm data len 1
        size += 4; // 1 float
        size += 4; // tri count 0
        var buf = new byte[size];
        int pos = 0;
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), v); pos += 4; }
        void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos), v); pos += 4; }
        void F(float v) { BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos), BitConverter.SingleToInt32Bits(v)); pos += 4; }
        void Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            I32(b.Length);
            Buffer.BlockCopy(b, 0, buf, pos, b.Length);
            pos += b.Length;
        }

        U32(magic); U32(version);
        Str("test"); Str("Test Arena"); Str("");
        F(-20f); // KillHeight
        F(-20f); F(20f); F(-20f); F(20f); // bounds
        I32(0); // spawns
        I32(1); I32(1); F(1f); F(0f); F(0f); I32(1); F(0f); // heightmap: 1x1, floor 0
        I32(0); // triangles
        return buf;
    }
}
