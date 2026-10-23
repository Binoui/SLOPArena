using System;
using Xunit;

namespace SlopArena.Shared.Tests;

using SlopArena.Shared.AI;

public class ReachEnvelopeTests
{
    [Theory]
    [InlineData(0.21f, 0.35f, 0f, 30, 0.56f)]
    [InlineData(-0.16f, 0.4f, 0f, 30, 0.24f)]
    [InlineData(0f, 0.2f, 2f, 60, 0.8f)]
    public void ForwardReach_UsesForwardExtentAndLunge(
        float offsetZ, float radius, float lunge, ushort duration, float expected)
    {
        var def = new CharacterDefinition
        {
            CookedSlots = new[]
            {
                new CookedSlotDefinition(0, "ground.1", false, "reach", "", "",
                    AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
                    new CookedTimeline(new[]
                    {
                        new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                        {
                            new CookedForwardLungeOperation(0, AuthoringUnit.MetersPerSecond, lunge, 18),
                            new CookedSpawnHitboxOperation(0, AuthoringUnit.Meters,
                                new CookedHitbox(AuthoringHitboxShape.Sphere, radius,
                                    2, 0, offsetZ, 0, 0, 0, null, null, 1, 0, 0, 0, 0, 1, true, 0)),
                        }),
                    })),
            },
        };

        TestHelpers.AssertNear(expected, HeuristicBotPolicy.ForwardReach(def, AbilitySlots.Slot1, airborne: false), 0.001f);
    }

    [Fact]
    public void ForwardReach_DataLessSlot_ReturnsZero()
    {
        var def = new CharacterDefinition();

        Assert.Equal(0f, HeuristicBotPolicy.ForwardReach(def, AbilitySlots.Slot1, airborne: false));
    }

    [Fact]
    public void FacingFrame_ForwardDeltaIsPureForward()
    {
        var (side, fwd) = FacingMath.ToFacingFrame(0f, 3f, facingYaw: 0f);
        TestHelpers.AssertNear(0f, side, 0.0001f);
        TestHelpers.AssertNear(3f, fwd, 0.0001f);
    }

    [Fact]
    public void FacingFrame_QuarterTurnFacesPositiveX()
    {
        // The simulation faces yaw = atan2(dx, dz): a quarter turn looks along +X,
        // so +X is ahead and +Z is on the character's left.
        float yaw = MathF.PI / 2f;
        var (aheadSide, aheadFwd) = FacingMath.ToFacingFrame(3f, 0f, yaw);
        TestHelpers.AssertNear(0f, aheadSide, 0.0001f);
        TestHelpers.AssertNear(3f, aheadFwd, 0.0001f);
        var (leftSide, leftFwd) = FacingMath.ToFacingFrame(0f, 3f, yaw);
        TestHelpers.AssertNear(-3f, leftSide, 0.0001f);
        TestHelpers.AssertNear(0f, leftFwd, 0.0001f);
    }

    [Theory]
    [InlineData(0.7f, 0.4f, 1.3f)]
    [InlineData(-2.1f, -0.6f, 0.9f)]
    [InlineData(3.1f, 0.25f, -1.1f)]
    public void FacingFrame_InvertsHitboxGeometryOffsets(float yaw, float offX, float offZ)
    {
        var state = new CharacterState { PX = 4f, PY = 1f, PZ = -2f, FacingYaw = yaw };
        var evt = new HitboxEvent { OffX = offX, OffZ = offZ, Radius = 0.5f };
        HitboxGeometry.ResolvePositions(state, evt, null, null, null, 0, out float wx, out _, out float wz, out _, out _, out _);

        var (side, fwd) = FacingMath.ToFacingFrame(wx - state.PX, wz - state.PZ, yaw);

        TestHelpers.AssertNear(offX, side, 0.0001f);
        TestHelpers.AssertNear(offZ, fwd, 0.0001f);
    }
}
