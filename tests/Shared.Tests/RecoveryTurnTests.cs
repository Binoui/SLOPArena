using System;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Recovery moves follow the stick held in their first three input ticks, whatever the fighter faced.
/// </summary>
public sealed class RecoveryTurnTests
{
    public static TheoryData<CharacterClass> StickRecoveries => new()
    {
        CharacterClass.FightGuy, CharacterClass.Wibou, CharacterClass.Manki,
        CharacterClass.Bashie, CharacterClass.Salamander,
    };

    private static readonly InputState Back = new() { MoveY = -1f };

    [Theory]
    [MemberData(nameof(StickRecoveries))]
    public void StickHeldOnThePressTurnsTheMoveAround(CharacterClass fighter)
    {
        var sim = AirborneFacingForward(fighter);
        Tick(sim, Back with { ActiveSlot = AbilitySlots.E });
        Assert.InRange(MathF.Abs(sim.GetState(1).FacingYaw), MathF.PI - 0.01f, MathF.PI + 0.01f);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void TheTurnWindowIsThePressTickAndTheTwoAfterIt(int stickTick, bool turns)
    {
        foreach (var fighter in StickRecoveries)
        {
            if (fighter == CharacterClass.Manki) continue;
            var sim = AirborneFacingForward(fighter);
            Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
            for (int tick = 1; tick <= stickTick; tick++)
                Tick(sim, tick == stickTick ? Back : default);
            float yaw = MathF.Abs(sim.GetState(1).FacingYaw);
            Assert.True(turns ? yaw > MathF.PI - 0.01f : yaw < 0.01f,
                $"{fighter} faced {yaw:F2} rad after a back stick on input tick {stickTick}");
        }
    }

    [Theory]
    [InlineData(CharacterClass.Wibou, 0)]
    [InlineData(CharacterClass.Wibou, 2)]
    [InlineData(CharacterClass.Bashie, 0)]
    [InlineData(CharacterClass.Bashie, 2)]
    public void ATurnedRecoveryTravelsTheNewWay(CharacterClass fighter, int stickTick)
    {
        var sim = AirborneFacingForward(fighter);
        float startZ = sim.GetState(1).PZ;
        for (int tick = 0; tick < 12; tick++)
        {
            var input = tick == stickTick ? Back : default;
            if (tick == 0) input.ActiveSlot = AbilitySlots.E;
            Tick(sim, input);
        }
        Assert.True(sim.GetState(1).PZ < startZ - 0.5f,
            $"{fighter} should travel backward, moved {sim.GetState(1).PZ - startZ:F2} m on Z");
    }

    [Fact]
    public void ALaterTurnWinsWithoutSteepeningHydroJet()
    {
        var sim = AirborneFacingForward(CharacterClass.Bashie);
        Tick(sim, Back with { ActiveSlot = AbilitySlots.E });
        float flatRise = sim.GetState(1).VY;
        Tick(sim, new InputState { MoveX = 0.3f });
        var turned = sim.GetState(1);
        Assert.InRange(turned.FacingYaw, MathF.PI / 2f - 0.01f, MathF.PI / 2f + 0.01f);
        Assert.True(turned.VX > 0f && MathF.Abs(turned.VZ) < 0.01f, $"jet should head +X, got ({turned.VX:F2}, {turned.VZ:F2})");
        Assert.Equal(flatRise, turned.VY, 3);
    }

    [Theory]
    [MemberData(nameof(StickRecoveries))]
    public void ANeutralStickKeepsFacingAndIgnoresTheCamera(CharacterClass fighter)
    {
        var sim = AirborneFacingForward(fighter);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E, AimYaw = 9000 });
        for (int tick = 0; tick < 3; tick++) Tick(sim, new InputState { AimYaw = 9000 });
        Assert.InRange(sim.GetState(1).FacingYaw, -0.01f, 0.01f);
    }

    private static ServerSimulation AirborneFacingForward(CharacterClass fighter)
    {
        var def = TestHelpers.ResolveDef(fighter);
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def) + 10f,
            IsGrounded = false,
            FacingYaw = 0f,
            AimYaw = 0f,
        };
        sim.RegisterEntity(1, def, state, TestHelpers.LoadBakedData(def));
        return sim;
    }

    private static void Tick(ServerSimulation sim, InputState input) => sim.Tick(new() { { 1, input } });
}
