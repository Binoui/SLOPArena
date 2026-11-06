using System;
using System.Linq;
using System.Reflection;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemEnumLimitsTests
{
    [Fact]
    public void RegistryRejectsMissingDuplicateAndUnknownTwistRows()
    {
        var row = new TwistEntry(new(1), "mayhem.twist.dodge.aimed");
        Assert.Throws<InvalidOperationException>(() => TwistRows.For<DodgeTwist>("dodge"));
        Assert.Throws<InvalidOperationException>(() => TwistRows.For<DodgeTwist>("dodge", row, row));
        Assert.Throws<InvalidOperationException>(() => TwistRows.For<DodgeTwist>("dodge", row with { Id = new(255) }));
    }

    [Fact]
    public void EveryMayhemEnumHasOneCurrentMaximum()
    {
        var fields = typeof(MayhemEnumLimits).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var enums = typeof(MayhemPhase).Assembly.GetTypes().Where(t => t.IsEnum && t.Namespace == typeof(MayhemPhase).Namespace)
            .Append(typeof(RuntimeSolidPhase)).ToArray();
        Assert.Equal(enums.OrderBy(t => t.FullName), fields.Select(f => f.FieldType).OrderBy(t => t.FullName));
        foreach (var field in fields)
            Assert.Equal(Enum.GetValues(field.FieldType).Cast<object>().Max(Convert.ToUInt64), Convert.ToUInt64(field.GetValue(null)));
    }
}
