using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using SlopArena.Shared.Tests;
using Xunit;

namespace SlopArena.Tests;

public sealed class BladeFlurryTimingTests
{
    [Fact]
    public void AuthoredAscentAndDescentTicksGovernFlight()
    {
        var parameters = new CookedWibouBladeFlurryCapabilityParameters(2f, 2, 4, 7, 3f, 5f);
        Assert.True(InternalCapabilityRegistry.TryCreate("slop.internal.wibou.blade-flurry.v2", "2", parameters, out var ability));
        Assert.False(InternalCapabilityRegistry.TryCreate("slop.internal.wibou.blade-flurry.v2", "1", parameters, out _));
        var state = new CharacterState { EntityId = 1 };
        var definition = TestHelpers.EngineDef;
        var input = default(InputState);
        ability.OnStart(ref state, definition);
        Assert.Equal(2f, state.VZ);

        float[] expectedY = { 0f, 3f, 3f, 3f, 0f, 0f, -5f, -5f };
        for (int tick = 1; tick <= expectedY.Length; tick++)
        {
            state.VX = state.VY = state.VZ = 99f;
            ability.Tick(ref state, ref input, definition);
            Assert.Equal(expectedY[tick - 1], state.VY);
            Assert.Equal(tick is >= 2 and <= 4 ? 2f : 0f, state.VZ);
            Assert.Equal(0f, state.VX);
        }
    }
}
