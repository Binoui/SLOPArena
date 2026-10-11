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

    public static IEnumerable<object[]> RecoveryContacts()
    {
        // Every wall segment of the cross, plus its four recessed corners.
        var contacts = new (float x, float z, float dx, float dz)[]
        {
            (0, -13, 0, -1), (7, -10, 1, 0), (11.5f, -7, 0, -1),
            (16, 0, 1, 0), (11.5f, 7, 0, 1), (7, 10, 1, 0),
            (0, 13, 0, 1), (-7, 10, -1, 0), (-11.5f, 7, 0, 1),
            (-16, 0, -1, 0), (-11.5f, -7, 0, -1), (-7, -10, -1, 0),
            (-7, -7, -1, -1), (7, -7, 1, -1),
            (7, 7, 1, 1), (-7, 7, -1, 1)
        };
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        foreach (var contact in contacts)
        foreach (float height in new[] { -2f, -9f })
            yield return new object[] { cls, contact.x, contact.z, contact.dx, contact.dz, height };
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

    private static float DistanceToRoof(float x, float z)
    {
        float DistanceToRectangle(float halfX, float halfZ)
        {
            float dx = MathF.Max(MathF.Abs(x) - halfX, 0);
            float dz = MathF.Max(MathF.Abs(z) - halfZ, 0);
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        return MathF.Min(DistanceToRectangle(16, 7), DistanceToRectangle(7, 13));
    }
}
