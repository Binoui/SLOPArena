using Xunit;

namespace SlopArena.Shared.Tests;

public class ClipExtrapolationTests
{
    // ── ExtrapolationMode enum ──

    [Fact]
    public void ExtrapolationMode_None_IsZero()
    {
        Assert.Equal(0, (int)ExtrapolationMode.None);
    }

    [Fact]
    public void ExtrapolationMode_Hold_IsOne()
    {
        Assert.Equal(1, (int)ExtrapolationMode.Hold);
    }

    [Fact]
    public void ExtrapolationMode_Continuous_IsTwo()
    {
        Assert.Equal(2, (int)ExtrapolationMode.Continuous);
    }

    // ── AnimationClipConfig defaults ──

    [Fact]
    public void AnimationClipConfig_DefaultExtrapolation_IsNone()
    {
        var cfg = new AnimationClipConfig { Name = "test" };
        Assert.Equal(ExtrapolationMode.None, cfg.Extrapolation);
    }
}
