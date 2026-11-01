using System.Collections.Generic;
using System.IO;
using SlopArena.Shared.AI;
using SlopArena.Shared.Telemetry;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class SessionLogTests
{
    private static SessionHeader Header() => new()
    {
        StartedUtc = "2026-10-16T20:00:00Z",
        Mode = "Training",
        Arena = "slop_pit",
        Fighters = new List<SessionFighter>
        {
            new() { EntityId = 1, Role = "player", Character = "Bonk", PackageId = "bonk", CapsuleHeight = 1.7f },
            new() { EntityId = 100, Role = "cpu", Character = "Manki", PackageId = "manki", CpuDifficulty = "Normal" },
        },
        Camera = new SessionCameraSettings { VerticalFovDegrees = 40f, OrbitRadius = 10f, StickYawRateDegrees = 45f },
        Input = new SessionInputSettings { CameraSensitivityPercent = 100f, AutoLockMode = "Always" },
    };

    private static SessionFrame Frame(int tick) => new()
    {
        Tick = tick,
        Gamepad = tick % 2 == 0,
        CamX = 1.5f, CamY = 4.25f, CamZ = -9.5f,
        CamQX = 0.1f, CamQY = 0.2f, CamQZ = 0f, CamQW = 0.97f,
        VerticalFov = 40f, Aspect = 1.778f,
        MouseLookDegrees = 2.5f, StickLookDegrees = 0.75f, StickPeak = 1f,
        CameraMode = 3,
        Fighters = new[]
        {
            new SessionFighterFrame { Id = 1, PX = 2f, PY = 0.85f, PZ = 3f, VY = -4.5f, FacingYaw = 1.25f,
                Grounded = false, State = 7, LockOn = true, Target = 100, Damage = 42, Hitstun = 0 },
            new SessionFighterFrame { Id = 100, PX = 2f, PY = 0.75f, PZ = 5f, Grounded = true, Hitstun = 12 },
        },
    };

    private static byte[] Write(System.Action<SessionLogWriter> body, bool complete = true)
    {
        var buffer = new MemoryStream();
        var writer = new SessionLogWriter(buffer, Header());
        body(writer);
        if (complete)
            writer.Complete(new List<SwingRecord>(), new SessionEnd { EndedUtc = "later", Ticks = 2, Reason = "left" });
        else
            writer.Flush();
        return buffer.ToArray();
    }

    [Fact]
    public void RoundTrip_PreservesEveryRecordKind()
    {
        byte[] bytes = Write(writer =>
        {
            writer.WriteFrame(Frame(0));
            writer.WriteFrame(Frame(1));
            writer.WriteSwing(new SwingRecord
            {
                Attacker = 1, ActiveSlot = AbilitySlots.Normal2, Accepted = true, StartTick = 0,
                ReferenceTarget = 100, HasClosestApproach = true, ClosestGap = 0.4f, MissForward = 0.4f,
                FirstHitboxTick = 5, LastHitboxTick = 9,
            });
            writer.WriteHit(new SessionHit { Tick = 1, Attacker = 100, Target = 1, Damage = 6f, Blocked = true });
            writer.WriteEvent(new SessionEvent { Tick = 1, Kind = "reset-positions" });
        });

        var data = SessionLogReader.Read(new MemoryStream(bytes));

        Assert.False(data.Truncated);
        Assert.Equal("Training", data.Header.Mode);
        Assert.Equal(2, data.Header.Fighters.Count);
        Assert.Equal("Normal", data.Header.Fighters[1].CpuDifficulty);
        Assert.Equal(45f, data.Header.Camera.StickYawRateDegrees);
        Assert.Equal(2, data.Frames.Count);
        var frame = data.Frames[1];
        Assert.Equal(1, frame.Tick);
        Assert.False(frame.Gamepad);
        Assert.Equal(4.25f, frame.CamY);
        Assert.Equal(0.97f, frame.CamQW);
        Assert.Equal(3, frame.CameraMode);
        Assert.Equal(2, frame.Fighters.Length);
        Assert.Equal(-4.5f, frame.Fighters[0].VY);
        Assert.Equal(1.25f, frame.Fighters[0].FacingYaw);
        Assert.True(frame.Fighters[0].LockOn);
        Assert.Equal(100ul, frame.Fighters[0].Target);
        Assert.Equal((ushort)42, frame.Fighters[0].Damage);
        Assert.Equal((ushort)12, frame.Fighters[1].Hitstun);
        var swing = Assert.Single(data.Swings);
        Assert.Equal(0.4f, swing.ClosestGap);
        Assert.Equal(9, swing.LastHitboxTick);
        Assert.True(Assert.Single(data.Hits).Blocked);
        Assert.Equal("reset-positions", Assert.Single(data.Events).Kind);
        Assert.Equal("left", data.End!.Reason);
    }

    [Fact]
    public void FlushedButUnfinishedSession_ReadsAsTruncated()
    {
        byte[] bytes = Write(writer =>
        {
            for (int tick = 0; tick < 5; tick++) writer.WriteFrame(Frame(tick));
        }, complete: false);

        var data = SessionLogReader.Read(new MemoryStream(bytes));

        Assert.True(data.Truncated);
        Assert.Null(data.End);
        Assert.Equal(5, data.Frames.Count);
    }

    [Fact]
    public void NonFiniteFrameValues_AreWrittenAsZero()
    {
        byte[] bytes = Write(writer =>
        {
            var frame = Frame(0);
            frame.MouseLookDegrees = float.NaN;
            frame.CamX = float.PositiveInfinity;
            writer.WriteFrame(frame);
        });

        var frame = Assert.Single(SessionLogReader.Read(new MemoryStream(bytes)).Frames);
        Assert.Equal(0f, frame.MouseLookDegrees);
        Assert.Equal(0f, frame.CamX);
    }

    [Fact]
    public void SwingSettles_AfterWindowAndLastHitboxGoQuiet()
    {
        var rejected = new SwingRecord { StartTick = 10, Accepted = false };
        Assert.False(SessionLogWriter.IsSettled(rejected, 10));
        Assert.True(SessionLogWriter.IsSettled(rejected, 11));

        var accepted = new SwingRecord { StartTick = 10, Accepted = true, WindowTicks = 20, LastHitboxTick = 45 };
        int quiet = SessionLogWriter.SwingSettleTicks;
        Assert.False(SessionLogWriter.IsSettled(accepted, 10 + 20 + quiet - 1));
        Assert.False(SessionLogWriter.IsSettled(accepted, 45 + quiet - 1));
        Assert.True(SessionLogWriter.IsSettled(accepted, 45 + quiet));
    }

    [Fact]
    public void WriteSettledSwings_WritesEachSwingOnce()
    {
        var swings = new List<SwingRecord>
        {
            new() { StartTick = 0, Accepted = false },
            new() { StartTick = 0, Accepted = true, WindowTicks = 10 },
        };
        byte[] bytes = Write(writer =>
        {
            writer.WriteSettledSwings(swings, 5);
            writer.WriteSettledSwings(swings, 6);
            writer.WriteSettledSwings(swings, 10 + SessionLogWriter.SwingSettleTicks);
            writer.WriteSettledSwings(swings, 200);
        });

        Assert.Equal(2, SessionLogReader.Read(new MemoryStream(bytes)).Swings.Count);
    }
}
