using System;
using System.Linq;
using Xunit;
using static SlopArena.Shared.Tests.GrabHoldTests;

namespace SlopArena.Shared.Tests;

public sealed class GrabThrowTests
{
    [Fact]
    public void HoldIgnoresBothSticksAndCamera()
    {
        var (sim, _) = Capture();
        var owner = sim.GetState(1);
        var victim = sim.GetState(2);
        for (int i = 0; i < 179; i++)
        {
            Tick(sim, new() { MoveX = 1, MoveY = -1, AimYaw = 9000 }, new() { MoveX = -1, Jump = true });
            Assert.Equal((owner.PX, owner.PY, owner.PZ, owner.CapturedYaw),
                (sim.GetState(1).PX, sim.GetState(1).PY, sim.GetState(1).PZ, sim.GetState(1).CapturedYaw));
            Assert.Equal((victim.PX, victim.PZ), (sim.GetState(2).PX, sim.GetState(2).PZ));
            Assert.Equal(0f, sim.GetState(1).VX);
        }
    }

    [Theory]
    [InlineData(0f, 0f, 4)]
    [InlineData(1f, 0f, 8)]
    [InlineData(0f, -1f, 8)]
    public void PressChoosesRealLaunchWithSharedFreeze(float x, float z, int damage)
    {
        var (sim, _) = Capture();
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = x, MoveY = z, AimYaw = -9000 });
        var heading = sim.GetState(1).CapturedYaw;
        for (int i = 0; i < 22; i++) Tick(sim, new() { MoveX = -x, MoveY = -z });
        var victim = sim.GetState(2);
        Assert.Equal(damage, victim.DamagePercent);
        Assert.Equal(ServerSimulation.ComputeHitstopTicks(damage), victim.HitstopTicks);
        Assert.Equal(victim.HitstopTicks, sim.GetState(1).HitstopTicks);
        Assert.Equal(0ul, victim.InteractionId);
        var hit = Assert.Single(sim.LastTickHits);
        Assert.Equal((byte)0, hit.AttackSlot);
        Assert.Equal(victim.HitstopTicks, hit.HitstopTicks);
        for (int i = 0; i < hit.HitstopTicks; i++) Tick(sim);
        victim = sim.GetState(2);
        Assert.Equal(ActionState.Hitstun, victim.State);
        Assert.True(victim.KVY > 0f);
        Assert.True(victim.HitstunTicks > 0);
        Assert.Equal(heading, sim.GetState(1).CapturedYaw);
        if (damage == 4) Assert.Equal((0f, 0f), (victim.KVX, victim.KVZ));
        else
        {
            if (x != 0) Assert.True(victim.KVX > 0f);
            if (z < 0) Assert.True(victim.KVZ < 0f);
        }
    }

    [Fact]
    public void CaptureDoesNotReplayOldReactionOnRelease()
    {
        var (sim, _) = Capture();
        var victim = sim.GetState(2);
        victim.BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact;
        victim.SdiApplied = true;
        victim.DIX = 1f;
        victim.QueuedKBBase = 900f;
        sim.SetState(2, victim);
        Tick(sim, new() { ActiveSlot = AbilitySlots.F });
        for (int i = 0; i < 40; i++) Tick(sim);
        victim = sim.GetState(2);
        Assert.InRange(victim.KVY, -10f, 12f);
        Assert.Equal(0f, victim.KVX);
        Assert.Equal(0f, victim.KVZ);
        Assert.Equal((byte)DefenseBlockHitstopKind.None, victim.BlockHitstopKind);
    }

    [Theory]
    [InlineData(20.5f, 1500)]
    [InlineData(20.31f, 0)]
    public void WallStopsAtLastLegalSubstepAndStillFlings(float wallX, short expectedYaw)
    {
        var arena = TriangleArena();
        arena.CollisionTriangles = new[] { arena.CollisionTriangles[0], arena.CollisionTriangles[1],
            new CollisionTriangle { AX = wallX, AY = 0, AZ = 0, BX = wallX, BY = 0, BZ = 40, CX = wallX, CY = 10, CZ = 0 },
            new CollisionTriangle { AX = wallX, AY = 0, AZ = 40, BX = wallX, BY = 10, BZ = 40, CX = wallX, CY = 10, CZ = 0 } };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var (sim, def) = Capture(arena: arena);
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = 1 });
        Assert.Equal(ActionState.Throwing, sim.GetState(1).State);
        Assert.Equal(expectedYaw, sim.GetState(1).CapturedYaw);
        Assert.True(sim.GetState(2).PX <= wallX - def.CapsuleRadius + 0.001f);
        for (int i = 0; i < 22; i++) Tick(sim);
        var hit = Assert.Single(sim.LastTickHits);
        TestHelpers.AssertNear(MathF.Sin(expectedYaw * MathF.PI / 18000f), hit.DirX);
        Assert.Equal(8f, hit.Damage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreReplaysWindupFreezeRecoveryAndLaunchExactly(bool fling)
    {
        var (sim, _) = Capture();
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = fling ? -1f : 0f });
        for (int milestone = 0; milestone < 45; milestone++)
        {
            var snapshot = sim.CaptureSnapshot();
            Tick(sim, victim: new() { MoveX = 1 });
            var expected = sim.CaptureSnapshot();
            var hits = sim.LastTickHits.ToArray();
            sim.RestoreSnapshot(snapshot);
            Tick(sim, victim: new() { MoveX = 1 });
            Assert.True(expected.ContentEquals(sim.CaptureSnapshot()), $"Replay diverged at throw age {milestone}.");
            Assert.Equal(hits, sim.LastTickHits);
        }
    }

    [Theory]
    [InlineData(false, 1ul)]
    [InlineData(false, 2ul)]
    [InlineData(true, 1ul)]
    [InlineData(true, 2ul)]
    public void ReleaseWitnessSurvivesRespawnAndDoesNotReportInterruption(bool fling, ulong dying)
    {
        var (sim, _) = Capture();
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = fling ? 1 : 0 });
        for (int i = 0; i < 21; i++) Tick(sim);
        var before = sim.GetState(1); var held = sim.GetState(2);
        Tick(sim);
        Assert.True(GrabThrow.WasReleased(in before, in held, sim.GetState(1), sim.GetState(2)));
        var released = sim.GetState(1);
        sim.SetState(dying, sim.GetState(dying) with { PY = -100f });
        Tick(sim);
        Assert.True(GrabThrow.WasReleased(in before, in held, sim.GetState(1), sim.GetState(2)));
        Assert.False(GrabThrow.WasReleased(in before, in held,
            released with { InteractionPhase = (byte)DefenseInteractionPhase.Terminal }, sim.GetState(2)));
    }

    [Fact]
    public void DetachedRecoveryBuffersChaseAttackAndPoseHoldsThroughFreeze()
    {
        var (sim, _) = Capture();
        Tick(sim, new() { ActiveSlot = AbilitySlots.F });
        for (int i = 0; i < 22; i++) Tick(sim);
        Assert.True(GrabThrow.TrySamplePose(sim.GetState(1), out var release));
        while (sim.GetState(1).HitstopTicks > 0)
        {
            Tick(sim);
            Assert.True(GrabThrow.TrySamplePose(sim.GetState(1), out var frozen));
            Assert.Equal(release.Seconds, frozen.Seconds);
        }
        while (sim.GetState(1).StateTicks > 3) Tick(sim);
        Tick(sim, new() { ActiveSlot = AbilitySlots.Normal1 });
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(1).BufferedSlot);
        for (int i = 0; i < 4; i++) Tick(sim);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
    }

    [Theory]
    [InlineData(false, 1ul)]
    [InlineData(false, 2ul)]
    [InlineData(true, 1ul)]
    [InlineData(true, 2ul)]
    public void DamagingHitOnEitherParticipantWinsOverRelease(bool fling, ulong target)
    {
        var (sim, _) = Capture();
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = fling ? 1f : 0f });
        for (int i = 0; i < 21; i++) Tick(sim);
        var before = sim.GetState(1); var held = sim.GetState(2);
        var hit = sim.GetState(target);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 100, X = hit.PX, Y = hit.PY, Z = hit.PZ,
            Radius = 0.1f, Damage = 2, StunTicks = 1, BaseKnockback = 20, DurationTicks = 1 });
        Tick(sim);
        Assert.DoesNotContain(sim.LastTickHits, h => h.OwnerEntityId == 1);
        Assert.False(GrabThrow.WasReleased(in before, in held, sim.GetState(1), sim.GetState(2)));
        Assert.Equal(0ul, sim.GetState(1).InteractionId);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
    }

    [Theory]
    [InlineData(0f, 0.2f, false)]
    [InlineData(0f, 0.201f, true)]
    [InlineData(-1f, 0f, true)]
    public void TimeoutReadsOnlyItsOwnTickStick(float x, float z, bool fling)
    {
        var (sim, _) = Capture();
        for (int i = 1; i < 180; i++) Tick(sim, new() { MoveX = 1 });
        Tick(sim, new() { MoveX = x, MoveY = z });
        Assert.Equal((byte)(fling ? DefenseInteractionPhase.Throwing : DefenseInteractionPhase.UpTossing),
            sim.GetState(1).InteractionPhase);
        for (int i = 0; i < 22; i++) Tick(sim, new() { MoveX = -x, MoveY = -z });
        Assert.Equal(fling ? 8f : 4f, Assert.Single(sim.LastTickHits).Damage);
    }

    [Fact]
    public void CpuWaitsForAutoTossThenResumesOrdinaryPolicy()
    {
        var (sim, def) = Capture(definition: TestHelpers.MankiDef);
        var policy = new AI.HeuristicBotPolicy();
        var memory = new AI.BotMemory();
        var random = new Random(19);
        bool released = false, resumed = false;
        for (int t = 0; t < 350; t++)
        {
            var owner = sim.GetState(1); var victim = sim.GetState(2);
            var input = policy.Decide(in owner, in victim, def, random, memory);
            if (owner.InteractionId != 0)
            {
                Assert.Equal(0, input.ActiveSlot);
                Assert.False(input.GrabPressed);
                Assert.Equal((0f, 0f), (input.MoveX, input.MoveY));
            }
            Tick(sim, input);
            released |= GrabThrow.WasReleased(in owner, in victim, sim.GetState(1), sim.GetState(2));
            if (released && sim.LastTickAcceptedActions.Contains(1ul)) { resumed = true; break; }
        }
        Assert.True(released, "The neutral locked CPU reaches its automatic toss.");
        Assert.True(resumed, "The CPU resumes its normal action policy after recovery.");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(135f)]
    [InlineData(180f)]
    [InlineData(-45f)]
    [InlineData(-90f)]
    [InlineData(-135f)]
    public void FlingUsesWorldStickAtEveryHeadingRegardlessOfCamera(float degrees)
    {
        var (sim, _) = Capture();
        float yaw = degrees * MathF.PI / 180f;
        Tick(sim, new() { ActiveSlot = AbilitySlots.F, MoveX = MathF.Sin(yaw), MoveY = MathF.Cos(yaw), AimYaw = -9000 });
        float reached = sim.GetState(1).CapturedYaw * MathF.PI / 18000f;
        TestHelpers.AssertNear(MathF.Sin(yaw), MathF.Sin(reached));
        TestHelpers.AssertNear(MathF.Cos(yaw), MathF.Cos(reached));
        for (int i = 0; i < 22; i++) Tick(sim);
        var hit = Assert.Single(sim.LastTickHits);
        TestHelpers.AssertNear(MathF.Sin(yaw), hit.DirX);
        TestHelpers.AssertNear(MathF.Cos(yaw), hit.DirZ);
    }
}
