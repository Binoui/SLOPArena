using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.SessionLogReport;
using SlopArena.Shared.AI;
using SlopArena.Shared.Telemetry;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class SessionLogReportTests
{
    private const ulong Player = 1, Cpu = 100;

    /// <summary>Camera 10 m behind the origin looking along +Z (identity rotation).</summary>
    private static SessionFrame Frame(int tick, bool gamepad = false, float cpuZ = 3f) => new()
    {
        Tick = tick,
        Gamepad = gamepad,
        CamX = 0f, CamY = 1f, CamZ = -10f, CamQW = 1f,
        VerticalFov = 40f, Aspect = 16f / 9f,
        StickPeak = gamepad ? 1f : 0f,
        StickLookDegrees = gamepad ? 0.75f : 0f,
        Fighters = new[]
        {
            new SessionFighterFrame { Id = Player, PY = 0.85f, Target = Cpu, Grounded = true },
            new SessionFighterFrame { Id = Cpu, PZ = cpuZ, PY = 0.75f, Grounded = true },
        },
    };

    private static SwingRecord Swing(int tick, bool accepted = true, bool connected = false, bool blocked = false,
        int hitboxTicks = 4, ulong target = Cpu, bool measured = true, float missForward = 0.4f)
        => new()
        {
            Attacker = Player, ActiveSlot = AbilitySlots.Normal1, StartTick = tick, Accepted = accepted,
            Connected = connected, Blocked = blocked, HitboxTicks = hitboxTicks, ReferenceTarget = target,
            HasClosestApproach = measured, ClosestGap = connected ? -0.1f : 0.4f,
            MissForward = connected ? 0f : missForward, MissWorldZ = connected ? 0f : missForward,
            TargetGroundedAtClosest = true, ActivationId = (ulong)(tick + 1),
        };

    private static SessionLogData Session()
    {
        var data = new SessionLogData { Source = "test.slopsession.jsonl.gz" };
        data.Header.Fighters.Add(new SessionFighter { EntityId = Player, Role = "player", Character = "Bonk" });
        data.Header.Fighters.Add(new SessionFighter { EntityId = Cpu, Role = "cpu", Character = "Manki", CpuDifficulty = "Normal" });
        for (int tick = 0; tick < 60; tick++) data.Frames.Add(Frame(tick, gamepad: tick >= 30));
        data.Swings.Add(Swing(1, connected: true));
        data.Swings.Add(Swing(2));
        data.Swings.Add(Swing(3, blocked: true));
        data.Swings.Add(Swing(4, accepted: false));
        data.Swings.Add(Swing(5, target: 0, measured: false));
        // The CPU swings from behind the camera at tick 10 and hits the player.
        data.Frames[10] = Frame(10, cpuZ: -20f);
        data.Swings.Add(new SwingRecord { Attacker = Cpu, Accepted = true, StartTick = 10, ActivationId = 900 });
        data.Hits.Add(new SessionHit { Tick = 14, Attacker = Cpu, Target = Player, ActivationId = 900, Damage = 8f });
        data.End = new SessionEnd { Ticks = 60 };
        return data;
    }

    [Theory]
    [InlineData(0.6f, 0.1f, 0.2f, "short")]
    [InlineData(-0.6f, 0.1f, 0.2f, "past")]
    [InlineData(0.1f, 0.2f, 0.6f, "under")]
    [InlineData(0.1f, 0.2f, -0.6f, "over")]
    [InlineData(0.1f, 0.6f, 0.2f, "right")]
    [InlineData(0.1f, -0.6f, 0.2f, "left")]
    public void MissDirection_UsesDominantAttackerAxis(float forward, float side, float up, string expected)
    {
        var swing = new SwingRecord { MissForward = forward, MissSide = side, MissUp = up };
        Assert.Equal(expected, SessionAnalysis.MissDirection(swing));
    }

    [Fact]
    public void Viewport_CentersAPointStraightAheadAndRejectsBehind()
    {
        var frame = Frame(0);
        Assert.True(SessionAnalysis.TryViewport(frame, 0f, 1f, 10f, out float u, out float v));
        TestHelpers.AssertNear(0.5f, u, 0.001f);
        TestHelpers.AssertNear(0.5f, v, 0.001f);
        Assert.False(SessionAnalysis.TryViewport(frame, 0f, 1f, -20f, out _, out _));
        Assert.True(SessionAnalysis.TryViewport(frame, 40f, 1f, 10f, out u, out _));
        Assert.True(u > 1f);
    }

    [Fact]
    public void Viewport_FollowsCameraYaw()
    {
        var frame = Frame(0);
        frame.CamX = 0f; frame.CamZ = 0f;
        frame.CamQY = MathF.Sin(MathF.PI / 4f);
        frame.CamQW = MathF.Cos(MathF.PI / 4f); // 90° yaw: looking along +X

        Assert.True(SessionAnalysis.TryViewport(frame, 10f, 1f, 0f, out float u, out float v));
        TestHelpers.AssertNear(0.5f, u, 0.001f);
        TestHelpers.AssertNear(0.5f, v, 0.001f);
    }

    [Theory]
    [InlineData(0f, 0f, 0.5f, "depth")]
    [InlineData(0.5f, 0f, 0.1f, "lateral")]
    [InlineData(0.1f, 0.5f, 0.1f, "vertical")]
    public void CameraAxis_SplitsWorldMissByView(float x, float y, float z, string expected)
    {
        var swing = new SwingRecord { MissWorldX = x, MissWorldY = y, MissWorldZ = z };
        Assert.Equal(expected, SessionAnalysis.CameraAxis(Frame(0), swing));
    }

    [Fact]
    public void LineAngle_MeasuresViewAgainstAttackerToTarget()
    {
        var swing = new SwingRecord { Attacker = Player, ReferenceTarget = Cpu };
        Assert.True(SessionAnalysis.TryLineAngle(Frame(0), swing, out float ahead));
        TestHelpers.AssertNear(0f, ahead, 0.01f);

        var side = Frame(0);
        side.Fighters[1].PX = 3f;
        side.Fighters[1].PZ = 0f;
        Assert.True(SessionAnalysis.TryLineAngle(side, swing, out float across));
        TestHelpers.AssertNear(90f, across, 0.01f);
    }

    [Fact]
    public void Analyze_ClassifiesSwingsAndHitsTaken()
    {
        var model = SessionAnalysis.Analyze(new[] { Session() });

        var overall = model.Overall;
        Assert.Equal(5, overall.Presses);
        Assert.Equal(4, overall.Accepted);
        Assert.Equal(1, overall.Hits);
        Assert.Equal(1, overall.Blocked);
        Assert.Equal(1, overall.Whiffs);
        Assert.Equal(1, overall.NoTarget);
        Assert.Equal(2.0 / 3.0, overall.ContactRate, 3);
        Assert.Equal(1, overall.NearMisses);
        Assert.Equal(1, overall.MissDirections["short"]);
        Assert.Equal(1, overall.CameraAxes["depth"]);
        Assert.Equal("ground.1", Assert.Single(model.Moves).Label);

        var straightIntoScreen = model.CameraAngles[0];
        Assert.Equal(2, straightIntoScreen.Contacts);
        Assert.Equal(1, straightIntoScreen.Whiffs);
        Assert.Equal(1, straightIntoScreen.DepthMisses);

        var keyboard = Assert.Single(model.Devices, x => x.Device == "Keyboard + mouse");
        Assert.Equal(1, keyboard.HitsTaken);
        Assert.Equal(1, keyboard.HitsTakenFromOffscreen);
        Assert.Equal(1.0 / 30.0, keyboard.OffscreenShare, 3);
        var controller = Assert.Single(model.Devices, x => x.Device == "Controller");
        Assert.Equal(1.0, controller.StickSaturatedShare, 3);
        Assert.Equal(0.0, controller.SaturatedWhileTargetLostShare, 3);
        TestHelpers.AssertNear(45f, (float)controller.StickDegreesPerSecond, 0.01f);
    }

    [Fact]
    public void Analyze_SplitsPressesByDeviceAtThePress()
    {
        var data = Session();
        data.Swings.Add(Swing(40));
        var model = SessionAnalysis.Analyze(new[] { data });

        Assert.Equal(5, Assert.Single(model.ByDevice, x => x.Label == "Keyboard + mouse").Presses);
        Assert.Equal(1, Assert.Single(model.ByDevice, x => x.Label == "Controller").Whiffs);
    }

    [Fact]
    public void Render_ProducesSelfContainedHtml()
    {
        string html = ReportHtml.Render(SessionAnalysis.Analyze(new[] { Session() }), "now");

        Assert.Contains("<svg", html);
        Assert.Contains("ground.1", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http", html.Replace("http-equiv", ""));
    }
}
