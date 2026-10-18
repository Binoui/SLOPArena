using System;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hits on the local player are decided by the server and arrive a few ticks late. The
/// self track must present the server's freeze and the whole hit reaction rather than
/// keep running and slide into a reaction that is already partly over (2026-10-18 Steam
/// playtest: 0 of 23 hits on the local player showed a freeze online, 30 of 30 offline).
/// </summary>
public class SelfHitReactionTests
{
    private const ulong SelfId = NetplayHarness.SelfId;
    private const int DelayTicks = 4;

    private static Hitbox LaunchingHitbox(CharacterState target) => new()
    {
        X = target.PX, Y = target.PY, Z = target.PZ,
        EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
        Radius = 1.5f,
        Damage = 10f,
        BaseKnockback = 6f,
        KnockbackGrowth = 6f,
        KnockbackAngle = 45,
        DurationTicks = 1,
        StunTicks = 30,
        OwnerId = NetplayHarness.OpponentId,
    };

    private static NetplayHarness HitHarness()
    {
        var h = new NetplayHarness(TestHelpers.TestArena(), TestHelpers.EngineDef, delayTicks: DelayTicks);
        for (int i = 0; i < 10; i++)
            h.Step(default, default);
        h.SpawnServerHitbox(LaunchingHitbox(h.ServerState(SelfId)));
        return h;
    }

    [Fact]
    public void ServerHitOnSelf_ShowsTheFreezeAndTheWholeReaction()
    {
        var h = HitHarness();
        bool sawFreeze = false;
        ushort launchHitstun = 0, firstShownHitstun = 0;
        for (int i = 0; i < 150; i++)
        {
            h.Step(default, default);
            var server = h.ServerState(SelfId);
            if (server.State == ActionState.Hitstun && server.HitstopTicks == 0)
                launchHitstun = Math.Max(launchHitstun, server.HitstunTicks);
            var shown = h.ClientState(SelfId);
            sawFreeze |= shown.HitstopTicks > 0;
            if (firstShownHitstun == 0 && shown.State == ActionState.Hitstun && shown.HitstopTicks == 0)
                firstShownHitstun = shown.HitstunTicks;
        }

        Assert.True(sawFreeze, "the local player must see their own hit freeze");
        Assert.True(launchHitstun > 0, "the fixture must launch the victim");
        Assert.Equal(launchHitstun, firstShownHitstun);
        var stats = h.TakeSelfStats();
        Assert.Equal(1, stats.HitReactions);
        Assert.Equal(0, stats.Mispredictions);
        NetplayHarness.AssertSelfConverged(h);
    }

    [Fact]
    public void InfluenceHeldOnlyDuringTheFreeze_IsRebuiltFromOwnInputs()
    {
        // Combo Influence captured during the freeze persists through hitstun and nudges the
        // fighter when it ends; a re-seed that forgot it would mispredict that final nudge.
        var h = HitHarness();
        var influence = TestHelpers.Input(moveX: 1f);
        for (int i = 0; i < 150; i++)
        {
            var input = h.ServerState(SelfId).HitstopTicks > 0 ? influence : default;
            h.Step(input, default);
        }

        var stats = h.TakeSelfStats();
        Assert.Equal(1, stats.HitReactions);
        Assert.Equal(0, stats.Mispredictions);
        NetplayHarness.AssertSelfConverged(h);
    }

    [Fact]
    public void AttackBufferedAtTheEndOfHitstun_IsPredictedOnTheLocalTimeline()
    {
        var h = HitHarness();
        var press = TestHelpers.Input(activeSlot: AbilitySlots.Slot1);
        bool pressed = false, compared = false;
        for (int i = 0; i < 150 && !compared; i++)
        {
            var server = h.ServerState(SelfId);
            bool lastHitstunTicks = server.State == ActionState.Hitstun && server.HitstopTicks == 0 &&
                server.HitstunTicks is > 0 and <= 3;
            var input = lastHitstunTicks && !pressed ? press : default;
            pressed |= input.ActiveSlot != 0;
            h.Step(input, default);

            // Once presentation returns to the local timeline, the buffered attack is already
            // running there exactly as on the server.
            var shown = h.ClientState(SelfId);
            if (pressed && h.ServerState(SelfId).State == ActionState.Attacking &&
                shown.State != ActionState.Hitstun)
            {
                var authority = h.ServerState(SelfId);
                if (shown.State == ActionState.Attacking)
                {
                    Assert.Equal(authority.AttackSlot, shown.AttackSlot);
                    Assert.Equal(authority.AttackElapsedTicks, shown.AttackElapsedTicks);
                    compared = true;
                }
            }
        }

        Assert.True(pressed, "the fixture must reach the hitstun buffer window");
        Assert.True(compared, "the buffered attack must appear on the local timeline");
        for (int i = 0; i < 120; i++)
            h.Step(default, default);
        Assert.Equal(0, h.TakeSelfStats().Mispredictions);
        NetplayHarness.AssertSelfConverged(h);
    }

    [Fact]
    public void VictimFreeze_PausesTheSelfTrackOnTheServerSnapshot()
    {
        var track = new LocalTrack(TestHelpers.TestArena(), SelfId);
        var ground = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(TestHelpers.EngineDef) };
        track.RegisterEntity(TestHelpers.EngineDef, ground);
        for (int i = 0; i < 4; i++)
            track.Tick(TestHelpers.Input(moveX: 1f));

        var frozen = ground with { HitstopTicks = 7, DamagePercent = 10 };
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 1, State = CharacterStatePacket.FromState(frozen, 1),
        });

        Assert.True(track.PresentFromServer);
        Assert.Equal((ushort)7, track.GetState().HitstopTicks);
        Assert.Equal(ground.PX, track.GetState().PX);
        Assert.Equal(ground.PX, track.Tick(TestHelpers.Input(moveX: 1f)).PX);
        Assert.Equal(1, track.TakeWindowStats().HitReactions);
    }

    [Fact]
    public void LaunchAfterFreeze_ReseedsAndReplaysToTheLocalTick()
    {
        var track = new LocalTrack(TestHelpers.TestArena(), SelfId);
        var ground = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(TestHelpers.EngineDef) };
        track.RegisterEntity(TestHelpers.EngineDef, ground);
        for (int i = 0; i < 6; i++)
            track.Tick(default);
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 1,
            State = CharacterStatePacket.FromState(ground with { HitstopTicks = 1, DamagePercent = 10 }, 1),
        });

        var launched = ground with
        {
            State = ActionState.Hitstun, HitstunTicks = 20, HitReactionSequence = 1,
            DamagePercent = 10, IsGrounded = false, PY = ground.PY + 0.5f, KVX = 2f, KVY = 4f,
        };
        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 2, State = CharacterStatePacket.FromState(launched, 2),
        });

        // Ticks 3..6 replayed locally: the reaction now runs on the local timeline.
        Assert.True(track.PresentFromServer);
        Assert.Equal(ActionState.Hitstun, track.GetState().State);
        Assert.Equal((ushort)16, track.GetState().HitstunTicks);
        Assert.True(track.GetState().PX > ground.PX);

        track.ReconcileWithServer(new ServerEntityPacket
        {
            EntityId = SelfId, Tick = 3,
            State = CharacterStatePacket.FromState(ground with { DamagePercent = 10, HitReactionSequence = 1 }, 3),
        });
        Assert.False(track.PresentFromServer);
    }
}
