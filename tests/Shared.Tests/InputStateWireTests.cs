using System;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

public class InputStateWireTests
{
    [Fact]
    public void InputState_Roundtrips_JumpHeldBit()
    {
        var input = new InputState
        {
            MoveX = 0.5f, MoveY = -0.5f,
            Up = true, Down = true, DownPressed = true, Left = false, Right = true,
            Jump = true, JumpHeld = true, IsAiming = true,
            ActiveSlot = AbilitySlots.A,
        };
        Span<byte> buf = stackalloc byte[InputState.Size];
        input.Write(buf);
        var restored = InputState.Deserialize(buf);

        Assert.True(restored.JumpHeld);
        Assert.True(restored.Jump);
        Assert.True(restored.Down);
        Assert.True(restored.DownPressed);
        Assert.Equal(AbilitySlots.A, restored.ActiveSlot);
    }

    [Fact]
    public void ReservedInputBitIsIgnoredAndNeverReemitted()
    {
        Span<byte> buffer = stackalloc byte[InputState.Size];
        var reserved = TestHelpers.ReservedInput(0x40);
        Assert.Equal(default(InputState), reserved);
        Assert.False(reserved.HasPressEdges);
        reserved.Write(buffer);
        Assert.Equal(0, buffer[8] & 0x40);

        new InputState { ShieldPressed = true }.Write(buffer);
        var restoredShieldPress = InputState.Deserialize(buffer);
        Assert.Equal(0, buffer[8] & 0x40);
        Assert.True(restoredShieldPress.ShieldPressed);
    }

    [Fact]
    public void DashPressRoundTripsAsAPressEdge()
    {
        Span<byte> buffer = stackalloc byte[InputState.Size];
        var dash = new InputState { DashPressed = true };
        Assert.True(dash.HasPressEdges);
        Assert.False(dash.WithoutEdges().DashPressed);
        Assert.True(default(InputState).WithEdgesFrom(dash).DashPressed);
        dash.Write(buffer);
        Assert.Equal(0x20, buffer[8] & 0x60);
        Assert.True(InputState.Deserialize(buffer).DashPressed);
    }


    [Fact]
    public void InputState_Roundtrips_FaceToCamera_LockPolicy_AndRetargetBits()
    {
        var input = new InputState
        {
            FaceToCamera = true,
            ToggleLock = true,
            RetargetPressed = true,
            LockMode = TargetLockMode.OnHit,
            JumpHeld = true,
        };
        Span<byte> buf = stackalloc byte[InputState.Size];
        input.Write(buf);
        var restored = InputState.Deserialize(buf);

        Assert.True(restored.FaceToCamera);
        Assert.True(restored.ToggleLock);
        Assert.True(restored.RetargetPressed);
        Assert.Equal(TargetLockMode.OnHit, restored.LockMode);
        Assert.True(restored.JumpHeld);
        Assert.Equal((byte)TargetLockMode.OnHit, buf[20]);
        Assert.Equal(SimulationProtocol.Version, buf[21]);

        Span<byte> cleanBuf = stackalloc byte[InputState.Size];
        default(InputState).Write(cleanBuf);
        var restoredClean = InputState.Deserialize(cleanBuf);
        Assert.False(restoredClean.FaceToCamera);
        Assert.False(restoredClean.ToggleLock);
        Assert.False(restoredClean.RetargetPressed);
        Assert.Equal(TargetLockMode.Never, restoredClean.LockMode);
        Assert.Equal((byte)TargetLockMode.Never, cleanBuf[20]);
        Assert.False(restoredClean.JumpHeld);

        Span<byte> alwaysBuf = stackalloc byte[InputState.Size];
        new InputState { LockMode = TargetLockMode.Always }.Write(alwaysBuf);
        Assert.Equal((byte)1, alwaysBuf[20]);
        Assert.Equal(TargetLockMode.Always, InputState.Deserialize(alwaysBuf).LockMode);
    }

    [Fact]
    public void InputCodecRejectsTruncationVersionAndLockMode()
    {
        var input = new byte[InputState.Size];
        default(InputState).Write(input);
        Assert.Throws<ArgumentException>(() => InputState.Deserialize(input.AsSpan(0, InputState.Size - 1)));
        input[21] = 2;
        Assert.Throws<InvalidDataException>(() => InputState.Deserialize(input));
        default(InputState).Write(input);
        input[20] = 3;
        Assert.Throws<InvalidDataException>(() => InputState.Deserialize(input));

    }
}
