using System;
using System.Text.Json;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class HitReliabilityJsonNumbersTests
{
    [Fact]
    public void PortableNumbersMatchCliJsonIncludingRoundTripAndExponentBoundaries()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new HitReliabilitySingleConverter());
        options.Converters.Add(new HitReliabilityDoubleConverter());
        foreach (float value in new[] { .3f, 1.45f, 1.3f, .27866936f, 100f, 1e8f, 1e9f, 1e-4f, 1e-5f, -0f, float.Epsilon, float.MaxValue })
            Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(value, options));
        foreach (double value in new[] { 1d / 3, 1e16, 1e17, 1e-4, 1e-5, -0d, double.Epsilon, double.MaxValue })
            Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(value, options));
        var random = new Random(117);
        var bytes = new byte[8];
        for (int i = 0; i < 10000; i++)
        {
            random.NextBytes(bytes);
            float single = BitConverter.ToSingle(bytes, 0);
            double wide = BitConverter.ToDouble(bytes, 0);
            if (!float.IsNaN(single) && !float.IsInfinity(single))
                Assert.Equal(JsonSerializer.Serialize(single), JsonSerializer.Serialize(single, options));
            if (!double.IsNaN(wide) && !double.IsInfinity(wide))
                Assert.Equal(JsonSerializer.Serialize(wide), JsonSerializer.Serialize(wide, options));
        }
    }
}
