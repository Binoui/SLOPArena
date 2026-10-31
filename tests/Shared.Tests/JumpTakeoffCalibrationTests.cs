using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class JumpTakeoffCalibrationTests
{
    [Theory]
    [InlineData(CharacterClass.Wibou, 10.6f, 7f, 8.4f)]
    [InlineData(CharacterClass.Bashie, 9.8f, 9f, 9.8f)]
    public void RunningStandingJumpUsesTheLowerRunOrAirBasedCap(
        CharacterClass fighter, float runSpeed, float airSpeed, float expected)
    {
        var def = TestHelpers.EngineDef with
        {
            Class = fighter,
            Movement = TestHelpers.EngineDef.Movement with
            {
                RunSpeed = runSpeed, AirSpeedMax = airSpeed,
                // Isolate takeoff from the air steering and friction applied later that tick.
                AirAccelBase = 0f, AirAccelStick = 0f, AirFriction = 0f,
            },
        };
        var sim = Create(def);
        for (int tick = 0; tick < 30; tick++) Step(sim, new() { MoveX = 1f });
        Assert.Equal(runSpeed, sim.GetState(1).VX, 4);
        var takeoff = Jump(sim);
        Assert.Equal(expected, takeoff.VX, 4);
        Assert.Equal(0f, takeoff.VZ);
    }

    [Theory]
    [InlineData(ActionState.Sliding, JumpTakeoff.Slide, 11.27f)]
    [InlineData(ActionState.Shell, JumpTakeoff.Shell, 15f)]
    public void SlideJumpAndShellHopKeepTheirExistingTakeoffRules(
        ActionState origin, JumpTakeoff kind, float expected)
    {
        var def = TestHelpers.EngineDef with
        {
            Movement = TestHelpers.EngineDef.Movement with
            {
                RunSpeed = 9.8f, AirSpeedMax = 7f,
                AirAccelBase = 0f, AirAccelStick = 0f, AirFriction = 0f,
            },
        };
        var sim = Create(def);
        var state = sim.GetState(1);
        state.State = origin; state.VX = 15f;
        sim.SetState(1, state);
        Step(sim, new() { Jump = true, JumpHeld = true });
        Assert.Equal(ActionState.JumpSquat, sim.GetState(1).State);
        Assert.Equal(kind, sim.GetState(1).JumpTakeoff);
        var takeoff = FinishSquat(sim);
        Assert.Equal(expected, takeoff.VX, 4);
    }

    private static CharacterState Jump(ServerSimulation sim)
    {
        Step(sim, new() { Jump = true, JumpHeld = true });
        Assert.Equal(JumpTakeoff.Standing, sim.GetState(1).JumpTakeoff);
        return FinishSquat(sim);
    }

    private static CharacterState FinishSquat(ServerSimulation sim)
    {
        for (int tick = 0; tick < 20 && sim.GetState(1).IsGrounded; tick++)
            Step(sim, new() { JumpHeld = true });
        var state = sim.GetState(1);
        Assert.False(state.IsGrounded);
        Assert.Equal(JumpTakeoff.Standing, state.JumpTakeoff);
        return state;
    }

    private static ServerSimulation Create(CharacterDefinition def)
    {
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = 30f, PZ = 30f, PY = def.CapsuleHeight * .5f, IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
        });
        return sim;
    }

    private static void Step(ServerSimulation sim, InputState input)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
}
