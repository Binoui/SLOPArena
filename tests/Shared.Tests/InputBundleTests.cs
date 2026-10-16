using System;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class InputBundleTests
{
    [Fact]
    public void RoundTripCarriesConsecutiveTicksOldestFirst()
    {
        var sent = new[]
        {
            new InputState { MoveX = 0.25f, JumpHeld = true },
            new InputState { MoveX = 0.5f, Jump = true, JumpHeld = true },
            new InputState { MoveX = 1f, ActiveSlot = AbilitySlots.E, ShieldHeld = true },
        };
        var buffer = new byte[InputBundle.MaxSize];
        int size = InputBundle.Write(buffer, 7, 1002, sent);
        Assert.Equal(InputBundle.SizeFor(3), size);

        var received = new InputState[InputBundle.MaxInputs];
        Assert.True(InputBundle.TryRead(buffer.AsSpan(0, size), out ulong entity, out uint newest, received, out int count));
        Assert.Equal(7UL, entity);
        Assert.Equal(1002u, newest);
        Assert.Equal(3, count);
        Assert.Equal(1000u, InputBundle.TickAt(newest, count, 0));
        Assert.Equal(1002u, InputBundle.TickAt(newest, count, 2));
        Assert.True(received[1].Jump);
        Assert.Equal(AbilitySlots.E, received[2].ActiveSlot);
        Assert.Equal(0.25f, received[0].MoveX);
    }

    [Fact]
    public void DecoderRejectsMalformedBundles()
    {
        var one = new[] { new InputState { MoveY = 1f } };
        var buffer = new byte[InputBundle.MaxSize];
        int size = InputBundle.Write(buffer, 1, 50, one);
        var slots = new InputState[InputBundle.MaxInputs];

        Assert.False(InputBundle.TryRead(buffer.AsSpan(0, size - 1), out _, out _, slots, out _));
        Assert.False(InputBundle.TryRead(buffer.AsSpan(0, size + 1), out _, out _, slots, out _));

        var zeroCount = buffer.AsSpan(0, size).ToArray();
        zeroCount[12] = 0;
        Assert.False(InputBundle.TryRead(zeroCount, out _, out _, slots, out _));

        var tooMany = buffer.AsSpan(0, size).ToArray();
        tooMany[12] = InputBundle.MaxInputs + 1;
        Assert.False(InputBundle.TryRead(tooMany, out _, out _, slots, out _));

        var wrongVersion = buffer.AsSpan(0, size).ToArray();
        wrongVersion[InputBundle.HeaderSize + InputState.Size - 1] = (byte)(SimulationProtocol.Version - 1);
        Assert.False(InputBundle.TryRead(wrongVersion, out _, out _, slots, out _));

        // A bundle cannot reach before tick zero.
        var underflow = new byte[InputBundle.MaxSize];
        int underflowSize = InputBundle.Write(underflow, 1, 1, new[] { default(InputState), default });
        underflow[8] = 0;
        Assert.False(InputBundle.TryRead(underflow.AsSpan(0, underflowSize), out _, out _, slots, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InputBundle.Write(new byte[InputBundle.MaxSize], 1, 0, new[] { default(InputState), default }));
    }

    [Fact]
    public void HeldInputClearsEveryOneTickEdgeAndKeepsHeldControls()
    {
        var full = new InputState
        {
            MoveX = 0.7f, MoveY = -0.3f, Jump = true, JumpHeld = true, DownPressed = true, Down = true,
            ShieldPressed = true, ShieldHeld = true, GrabPressed = true, FaceToCamera = true,
            ToggleLock = true, RetargetPressed = true, ActiveSlot = AbilitySlots.R, IsAiming = true,
            AimYaw = 1234, AimPitch = -200, AimDistance = 900, TargetEntityId = 2, LockMode = TargetLockMode.Always,
        };
        var held = full.WithoutEdges();

        Assert.False(held.Jump || held.DownPressed || held.ShieldPressed || held.GrabPressed ||
            held.FaceToCamera || held.ToggleLock || held.RetargetPressed);
        Assert.Equal(0, held.ActiveSlot);
        Assert.True(held.JumpHeld && held.Down && held.ShieldHeld && held.IsAiming);
        Assert.Equal(full.MoveX, held.MoveX);
        Assert.Equal(full.MoveY, held.MoveY);
        Assert.Equal(full.AimYaw, held.AimYaw);
        Assert.Equal(full.AimPitch, held.AimPitch);
        Assert.Equal(full.AimDistance, held.AimDistance);
        Assert.Equal(full.TargetEntityId, held.TargetEntityId);
        Assert.Equal(full.LockMode, held.LockMode);
    }
}
