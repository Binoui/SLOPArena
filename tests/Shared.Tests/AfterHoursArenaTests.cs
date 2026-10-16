using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class AfterHoursArenaTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CollisionGridIncludesWallsOnItsMaximumPlane(bool onX)
    {
        var triangle = onX
            ? new CollisionTriangle { AX = 8, AY = 0, AZ = 0, BX = 8, BY = 4, BZ = 0, CX = 8, CY = 0, CZ = 8 }
            : new CollisionTriangle { AX = 0, AY = 0, AZ = 8, BX = 0, BY = 4, BZ = 8, CX = 8, CY = 0, CZ = 8 };
        var source = new ArenaDefinition
        {
            MinX = 0, MaxX = 8, MinZ = 0, MaxZ = 8, KillHeight = -10,
            CollisionTriangles = new[] { triangle }
        };
        var arena = ArenaBinaryFormat.Deserialize(ArenaBinaryFormat.Serialize(source))!.Value;
        var candidates = new int[1];
        float x = onX ? 8 : 2;
        float z = onX ? 2 : 8;
        Assert.Equal(1, ArenaCollision.GetCandidateTrianglesForAabb(
            x - 0.1f, 0.5f, z - 0.1f, x + 0.1f, 1.5f, z + 0.1f, in arena, candidates));
        Assert.Equal(0, candidates[0]);
    }

    // Broad club roof: 30 x 24 with 3 m chamfered corners (CCW in the X/Z plane).
    private static readonly (float X, float Z)[] Roof =
    {
        (-12, -12), (12, -12), (15, -9), (15, 9), (12, 12), (-12, 12), (-15, 9), (-15, -9)
    };

    public static IEnumerable<object[]> RecoveryContacts()
    {
        // Every wall's midpoint and every convex corner, approached along its outward normal.
        var contacts = new List<(float x, float z, float dx, float dz)>();
        for (int i = 0; i < Roof.Length; i++)
        {
            var a = Roof[i];
            var b = Roof[(i + 1) % Roof.Length];
            var prev = Roof[(i + Roof.Length - 1) % Roof.Length];
            var (nx, nz) = OutwardNormal(a, b);
            contacts.Add(((a.X + b.X) / 2, (a.Z + b.Z) / 2, nx, nz));
            var (px, pz) = OutwardNormal(prev, a);
            float length = MathF.Sqrt((nx + px) * (nx + px) + (nz + pz) * (nz + pz));
            contacts.Add((a.X, a.Z, (nx + px) / length, (nz + pz) / length));
        }
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        foreach (var contact in contacts)
        foreach (float height in new[] { -2f, -9f })
            yield return new object[] { cls, contact.x, contact.z, contact.dx, contact.dz, height };
    }

    [Fact]
    public void BakedShellMatchesTheBroadClubRoof()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "after_hours.arena"))!.Value;
        Assert.Equal((-15f, 15f, -12f, 12f), (arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ));
        Assert.Equal(-12f, arena.KillHeight);
        Assert.Equal(21.5f, arena.KillTop, 3);
        Assert.Equal((-25f, 25f, -22f, 22f), (arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ));
        var spawns = new[] { (-6f, -5f), (6f, 5f), (-6f, 5f), (6f, -5f) };
        Assert.Equal(spawns.Length, arena.SpawnPoints.Length);
        for (int i = 0; i < spawns.Length; i++)
        {
            Assert.Equal(spawns[i].Item1, arena.SpawnPoints[i].X, 3);
            Assert.Equal(spawns[i].Item2, arena.SpawnPoints[i].Z, 3);
            Assert.Equal(0.85f, arena.SpawnPoints[i].Y, 3);
        }
    }

    [Theory]
    [MemberData(nameof(RecoveryContacts))]
    public void RecoveryWallsBlockInwardMovementAndAllowOutwardMovement(
        CharacterClass cls, float edgeX, float edgeZ, float outwardX, float outwardZ, float height)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "after_hours.arena"))!.Value;
        var def = TestHelpers.ResolveDef(cls);
        var state = TestHelpers.PlayerState(edgeX + outwardX, edgeZ + outwardZ);
        state.PY = height;
        state.IsGrounded = false;
        state.AirTimeTicks = def.Movement.FloatWindowTicks;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 60;
        state.VX = -outwardX * 120f;
        state.VZ = -outwardZ * 120f;

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float distance = DistanceToRoof(state.PX, state.PZ);
        Assert.True(distance >= def.CapsuleRadius - 0.03f,
            $"{cls}: entered roof volume from ({edgeX},{edgeZ}) at height {height}; final ({state.PX},{state.PZ}), clearance {distance}.");
        Assert.False(state.IsGrounded);
        Assert.InRange(state.PY, height - 0.1f, height + 0.01f);

        state.VX = outwardX * 12f;
        state.VZ = outwardZ * 12f;
        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);
        Assert.True(DistanceToRoof(state.PX, state.PZ) > distance + 0.1f,
            $"{cls}: wall trapped outward movement from ({edgeX},{edgeZ}) at height {height}.");
    }

    private static (float X, float Z) OutwardNormal((float X, float Z) a, (float X, float Z) b)
    {
        float ex = b.X - a.X, ez = b.Z - a.Z;
        float length = MathF.Sqrt(ex * ex + ez * ez);
        return (ez / length, -ex / length);
    }

    private static float DistanceToRoof(float x, float z)
    {
        bool inside = true;
        float best = float.MaxValue;
        for (int i = 0; i < Roof.Length; i++)
        {
            var a = Roof[i];
            var b = Roof[(i + 1) % Roof.Length];
            var (nx, nz) = OutwardNormal(a, b);
            if ((x - a.X) * nx + (z - a.Z) * nz > 0) inside = false;
            float ex = b.X - a.X, ez = b.Z - a.Z;
            float t = Math.Clamp(((x - a.X) * ex + (z - a.Z) * ez) / (ex * ex + ez * ez), 0f, 1f);
            float dx = x - (a.X + ex * t), dz = z - (a.Z + ez * t);
            best = MathF.Min(best, MathF.Sqrt(dx * dx + dz * dz));
        }
        return inside ? 0f : best;
    }
}
