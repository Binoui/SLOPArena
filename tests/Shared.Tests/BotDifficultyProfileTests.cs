using Xunit;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Tests;

public sealed class BotDifficultyProfileTests
{
    [Fact]
    public void NamedDifficultiesHaveExactReactionDelays()
    {
        Assert.Equal(24, BotDifficultyProfile.ForDifficulty(CpuDifficulty.Easy).ReactionDelayTicks);
        Assert.Equal(18, BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).ReactionDelayTicks);
        Assert.Equal(12, BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard).ReactionDelayTicks);
    }

    [Fact]
    public void NamedDifficultiesUseOnlyCanonicalSerializedValues()
    {
        Assert.Equal(0, (int)CpuDifficulty.Easy);
        Assert.Equal(1, (int)CpuDifficulty.Normal);
        Assert.Equal(2, (int)CpuDifficulty.Hard);
        Assert.Equal(CpuDifficulty.Normal, BotDifficultyProfile.Normalize((CpuDifficulty)(-1)));
        Assert.Equal(CpuDifficulty.Normal, BotDifficultyProfile.Normalize((CpuDifficulty)3));
    }
    [Fact]
    public void ProfilesBecomeFasterMoreAccurateAndMoreAggressive()
    {
        var easy = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Easy);
        var normal = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal);
        var hard = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard);

        Assert.True(easy.HesitationMax > normal.HesitationMax);
        Assert.True(normal.HesitationMax > hard.HesitationMax);
        Assert.True(easy.RangeError > normal.RangeError);
        Assert.True(normal.RangeError > hard.RangeError);
        Assert.True(easy.PunishRecognition < normal.PunishRecognition);
        Assert.True(normal.PunishRecognition < hard.PunishRecognition);
        Assert.True(easy.ThreatRecognition < normal.ThreatRecognition);
        Assert.True(normal.ThreatRecognition < hard.ThreatRecognition);
        Assert.True(easy.PunishRecognition < normal.PunishRecognition);
        Assert.True(normal.PunishRecognition < hard.PunishRecognition);
        Assert.True(easy.ConfirmReliability < normal.ConfirmReliability);
        Assert.True(normal.ConfirmReliability < hard.ConfirmReliability);
    }

    [Theory]
    [InlineData(CpuDifficulty.Easy, 6, 12, 12, 1)]
    [InlineData(CpuDifficulty.Normal, 2, 6, 6, 2)]
    [InlineData(CpuDifficulty.Hard, 0, 2, 3, 3)]
    public void PlansUseBoundedHesitationMarginsAndPressure(CpuDifficulty tier, int min, int max, int margin, int budget)
    {
        var profile = BotDifficultyProfile.ForDifficulty(tier);
        Assert.Equal(min, profile.HesitationMin); Assert.Equal(max, profile.HesitationMax);
        Assert.Equal(margin, profile.PunishMargin); Assert.Equal(budget, profile.PressureBudget);
    }

    [Fact]
    public void HardRetainsNonZeroRangeError()
    {
        var hard = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Hard);

        Assert.InRange(hard.RangeError, float.Epsilon, 1f);
        Assert.True(hard.PunishRecognition > 0f);
        Assert.True(hard.ConfirmReliability > 0f);
    }
}
