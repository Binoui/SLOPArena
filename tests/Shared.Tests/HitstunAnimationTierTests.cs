using System;
using Xunit;

namespace SlopArena.Shared.Tests;

public class HitstunAnimationTierTests
{
    [Theory]
    [InlineData(0f,   0,   0)]
    [InlineData(30f,  15,  0)]
    [InlineData(60f,  30,  0)]
    [InlineData(62f,  31,  1)]
    [InlineData(80f,  40,  1)]
    [InlineData(100f, 50,  1)]
    [InlineData(102f, 51,  2)]
    [InlineData(160f, 80,  2)]
    [InlineData(400f, 200, 2)]
    public void KnockbackPaths_SetHitstunDurationAndTier(
        float force, ushort expectedStunTicks, byte expectedLevel)
    {
        var state = TestHelpers.PlayerState();
        var tuning = new CombatTuning(knockbackScale: 1f, hitstunCoefficient: 0.5f,
            hitstunMagnitudeBonus: 0f);

        Simulation.ApplyKnockbackForce(ref state, dirX: 1f, dirZ: 0f, angleDeg: 0,
            force: force, stunTicks: 1, combatTuning: tuning);

        Assert.Equal(expectedStunTicks, state.HitstunTicks);
        Assert.Equal(expectedLevel, state.HitstunLevel);

        var damageHit = TestHelpers.PlayerState();
        Simulation.ApplyKnockback(ref damageHit, dirX: 1f, dirZ: 0f, angleDeg: 0,
            baseKB: force, growthKB: 0f, damage: 0f, stunTicks: 1, weight: 100f,
            applyScale: false, combatTuning: tuning);
        Assert.Equal(expectedStunTicks, damageHit.HitstunTicks);
        Assert.Equal(expectedLevel, damageHit.HitstunLevel);
    }

    [Fact]
    public void HitstunLevel_DefaultsToZero()
    {
        var state = new CharacterState();
        Assert.Equal(0, (int)state.HitstunLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HitstunLevel_SurvivesSnapshotSerialization(byte level)
    {
        var state = TestHelpers.PlayerState();
        state.State = ActionState.Hitstun;
        state.HitstunTicks = 15;
        state.HitstunLevel = level;

        var writer = new SnapshotWriter();
        SnapshotCodecs.Write(writer, state);
        var reader = new SnapshotReader(writer.ToArray());
        var restored = SnapshotCodecs.ReadCharacterState(reader);
        Assert.True(reader.AtEnd);
        Assert.Equal(state, restored);
        Assert.Equal(level, restored.HitstunLevel);
    }
}
