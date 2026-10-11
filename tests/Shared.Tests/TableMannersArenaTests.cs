using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class TableMannersArenaTests
{
    private static ArenaDefinition Load()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return ArenaBinaryFormat.LoadFromFile(Path.Combine(root, "data", "arenas", "table_manners.arena"))!.Value;
    }

    public static IEnumerable<object[]> TabletopSamples()
    {
        yield return new object[] { 0f, 0f };
        for (int degrees = 0; degrees < 360; degrees += 15)
        foreach (float radius in new[] { 7f, 14f })
        {
            float angle = degrees * MathF.PI / 180f;
            yield return new object[] { MathF.Cos(angle) * radius, MathF.Sin(angle) * radius };
        }
    }

    [Theory]
    [MemberData(nameof(TabletopSamples))]
    public void FightingSurfaceStaysFlatAcrossCenterAndDecorativeRim(float x, float z)
    {
        var arena = Load();
        Assert.InRange(arena.Heightmap.Sample(x, z), -0.001f, 0.001f);
    }

    public static IEnumerable<object[]> EdgeContacts()
    {
        foreach (var cls in new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk })
        foreach (var outward in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        foreach (bool belowSlab in new[] { false, true })
            yield return new object[] { cls, outward.Item1, outward.Item2, belowSlab };
    }

    [Theory]
    [MemberData(nameof(EdgeContacts))]
    public void TableEdgeBlocksAtSlabHeightAndAllowsMovementUnderneath(
        CharacterClass cls, float outwardX, float outwardZ, bool belowSlab)
    {
        var arena = Load();
        var def = TestHelpers.ResolveDef(cls);
        float edge = outwardX != 0 ? arena.MaxX : arena.MaxZ;
        var state = TestHelpers.PlayerState(outwardX * (edge + 1), outwardZ * (edge + 1));
        state.PY = belowSlab ? -5f : -1.5f;
        state.IsGrounded = false;
        state.AirTimeTicks = def.Movement.FloatWindowTicks;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 60;
        state.VX = -outwardX * 120f;
        state.VZ = -outwardZ * 120f;

        Simulation.SimulateTick(ref state, def, default, arena, out _, out _, DownActionTuning.Default, false);

        float distance = state.PX * outwardX + state.PZ * outwardZ;
        if (belowSlab)
            Assert.True(distance < edge - 0.7f, $"{cls}: invisible obstruction below the tabletop at ({outwardX},{outwardZ}).");
        else
            Assert.True(distance >= edge + def.CapsuleRadius - 0.15f, $"{cls}: passed through the visible table edge at ({outwardX},{outwardZ}).");
        Assert.False(state.IsGrounded);
    }
}
