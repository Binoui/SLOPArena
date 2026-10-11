using System.Linq;
using System.Text.Json;
using SlopArena.MoveDataReport;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class KnockbackAuditReportTests
{
    [Theory]
    [InlineData(90)]
    [InlineData(-90)]
    public void ExactVerticalLaunchLandsOnReportFloorWithoutRespawn(int angle)
    {
        var hit = new HitboxEvent
        {
            Damage = 8, StunTicks = 18,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)angle,
                BaseKnockback = 6, KnockbackGrowth = 24 },
        };
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(
            KnockbackAuditReport.Flight(TestHelpers.FightGuyDef, hit, 120)));
        var row = report.RootElement;
        Assert.Equal("landing", row.GetProperty("termination").GetString());
        Assert.All(row.GetProperty("points").EnumerateArray(), p =>
            Assert.True(p.GetProperty("Height").GetSingle() >= -.01f,
                "The report must land on its floor instead of falling outside the heightmap and respawning."));
    }

    [Fact]
    public void FixedHitstunUsesTheRealQueuedLaunchInsteadOfTheMagnitudeDuration()
    {
        var hit = new HitboxEvent
        {
            Damage = 4, StunTicks = 8, FixedHitstunTicks = 24,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 45, BaseKnockback = 2 },
        };
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(
            KnockbackAuditReport.Flight(TestHelpers.FightGuyDef, hit, 120)));
        Assert.Equal(24, report.RootElement.GetProperty("stun").GetInt32());
        Assert.Equal(24, report.RootElement.GetProperty("hitstunExpiryTick").GetInt32());
        Assert.Contains(report.RootElement.GetProperty("points").EnumerateArray(), p =>
            p.GetProperty("Height").GetSingle() == 0 && p.GetProperty("InHitstun").GetBoolean());
    }
}
