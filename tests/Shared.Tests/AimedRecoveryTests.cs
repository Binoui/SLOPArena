using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

public sealed class AimedRecoveryTests
{
    private const ushort LaunchTick = 12, EndTick = 18;
    private static readonly CookedAimedRecoveryCapabilityParameters Parameters = new(LaunchTick, 15f, 13f, 8f, .8f);
    internal static readonly CookedAimedRecoveryCapabilityParameters HopParameters = new(44, 15f, 13f, 8f, 0f,
        new RecoveryHopParameters(18, 34, .72f, .45f, .18f));
    private static readonly CookedAimedRecoveryCapabilityParameters SteerParameters = Parameters with { RiseSteerAcceleration = 30f };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupHasAThrowHoldSmallForwardArcContactHoldThenAimedHighLaunch(bool grounded)
    {
        var sim = MakeSim(grounded, hop: true, yaw: MathF.PI / 2f);
        var start = sim.GetState(1);
        // The stick waits out the recovery turn window, so the hop keeps the fighter's facing.
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        for (int tick = 1; tick <= 44; tick++)
        {
            var state = sim.GetState(1);
            float u = Math.Clamp((tick - 18) / 16f, 0f, 1f);
            float y = .18f * u + 4f * .45f * u * (1f - u);
            Assert.InRange(state.PX - start.PX, .72f * u - .002f, .72f * u + .002f);
            Assert.InRange(state.PY - start.PY, y - .002f, y + .002f);
            Assert.InRange(MathF.Abs(state.PZ - start.PZ), 0f, .002f);
            Assert.Equal(start.JumpsLeft, state.JumpsLeft);
            Assert.Equal(start.AirDodgesLeft, state.AirDodgesLeft);
            Assert.False(state.IsFastFalling);
            if (tick < 18 || tick is >= 34 and < 44)
                Assert.Equal((0f, 0f, 0f), (state.VX, state.VY, state.VZ));
            if (tick == 44) break;
            Tick(sim, tick < RecoveryTurn.WindowTicks ? default : new InputState { MoveY = -1 });
        }
        Assert.Equal(13f, sim.GetState(1).VY);
        Assert.Equal(-8f, sim.GetState(1).VZ);
        Assert.Equal(0f, sim.GetState(1).VX);
        for (int tick = 44; tick < 54; tick++) Tick(sim, default);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((ushort)210, sim.GetState(1).GetCooldown(AbilitySlots.E));
        Assert.True(sim.GetState(1).PY > start.PY + 1.9f);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData(26)]
    [InlineData(38)]
    public void HitstopAndInterruptionPauseOrCancelTheSmallHopAndPendingBounce(int tick)
    {
        var sim = MakeSim(hop: true);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        while (sim.CurrentTick < tick) Tick(sim, default);
        var frozen = sim.GetState(1); frozen.HitstopTicks = 4; sim.SetState(1, frozen);
        for (int i = 0; i < 4; i++) Tick(sim, default);
        var after = sim.GetState(1);
        Assert.Equal(frozen.AttackElapsedTicks, after.AttackElapsedTicks);
        Assert.Equal((frozen.PX, frozen.PY, frozen.PZ), (after.PX, after.PY, after.PZ));
        after.State = ActionState.Hitstun; after.HitstunTicks = 60; after.VY = -2f;
        sim.SetState(1, after);
        for (int i = 0; i < 45; i++)
        {
            Tick(sim, default);
            Assert.Null(sim.GetActiveAbility(1));
            Assert.True(sim.GetState(1).VY <= 0f, "interruption cannot schedule a later hop or high bounce");
        }
    }

    [Fact]
    public void FallingSetupArrestsDescentAndLaunchesWithoutOrdinaryJumps()
    {
        var sim = MakeSim();
        float startY = sim.GetState(1).PY;
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        for (int tick = 1; tick < LaunchTick; tick++)
        {
            var state = sim.GetState(1);
            Assert.Equal((ushort)tick, state.AttackElapsedTicks);
            Assert.Equal(.8f, state.VY);
            Assert.Equal(0f, state.VX);
            Assert.False(state.IsFastFalling);
            Assert.True(state.PY >= startY);
            Assert.Equal((byte)0, state.JumpsLeft);
            Assert.Equal((byte)0, state.AirDodgesLeft);
            Tick(sim, default);
        }
        var launched = sim.GetState(1);
        Assert.Equal(LaunchTick, launched.AttackElapsedTicks);
        Assert.Equal(15f, launched.VY);
        Assert.Equal(0f, launched.VX);
        Assert.False(launched.IsGrounded);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        Tick(sim, default);
        Assert.True(sim.GetState(1).VY < launched.VY, "ordinary gravity starts after launch");
    }

    [Theory]
    [InlineData(1f, 0f, 8f, 0f, 13f)]
    [InlineData(0f, -1f, 0f, -8f, 13f)]
    [InlineData(.5f, 0f, 4f, 0f, 14f)]
    [InlineData(1f, 1f, 5.656854f, 5.656854f, 13f)]
    public void LaunchSamplesTheCurrentStickAndClampsDiagonalMagnitude(float x, float z, float vx, float vz, float vy)
    {
        var sim = MakeSim();
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E, MoveX = -1f });
        while (sim.GetState(1).AttackElapsedTicks < LaunchTick - 1) Tick(sim, new InputState { MoveX = -1f });
        Tick(sim, new InputState { MoveX = x, MoveY = z });
        var launched = sim.GetState(1);
        Assert.InRange(launched.VX, vx - .0001f, vx + .0001f);
        Assert.InRange(launched.VZ, vz - .0001f, vz + .0001f);
        Assert.Equal(vy, launched.VY);
        Tick(sim, new InputState { MoveX = -x, MoveY = -z });
        Assert.Equal(launched.VX, sim.GetState(1).VX);
        Assert.Equal(launched.VZ, sim.GetState(1).VZ);
    }

    [Fact]
    public void RiseSteeringEasesTowardTheStickAndNeutralKeepsTheDrift()
    {
        var sim = MakeSim(steer: true);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        while (sim.GetState(1).AttackElapsedTicks < LaunchTick) Tick(sim, default);
        Assert.Equal((0f, 15f), (sim.GetState(1).VX, sim.GetState(1).VY));
        Tick(sim, new InputState { MoveX = 1f });
        Assert.InRange(sim.GetState(1).VX, .5f - .0001f, .5f + .0001f);
        Tick(sim, default);
        Assert.InRange(sim.GetState(1).VX, .5f - .0001f, .5f + .0001f);
        var fast = sim.GetState(1); fast.VX = 7.8f; sim.SetState(1, fast);
        Tick(sim, new InputState { MoveX = 1f });
        Assert.Equal(8f, sim.GetState(1).VX);
        Tick(sim, new InputState { MoveX = 1f });
        Assert.NotNull(sim.GetActiveAbility(1));
        Assert.Equal(8f, sim.GetState(1).VX);
    }

    [Fact]
    public void HeldInitialEProducesOneBounceThenNormalMovementAndCooldown()
    {
        var sim = MakeSim();
        var held = new InputState { ActiveSlot = AbilitySlots.E, MoveX = 1f };
        for (int tick = 0; tick < EndTick; tick++) Tick(sim, held);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((ushort)210, sim.GetState(1).GetCooldown(AbilitySlots.E));
        Assert.Equal((ushort)0, sim.GetState(1).AnimLockTicks);
        float outgoing = sim.GetState(1).VX;
        Tick(sim, new InputState { MoveX = -1f });
        Assert.True(sim.GetState(1).VX < outgoing, "ordinary air steering resumes after the follow-through");
        Tick(sim, held);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.True(sim.GetState(1).VY < 15f, "the held press cannot restart the bounce or dive");
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Normal1 });
        Assert.NotNull(sim.GetActiveAbility(1));
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(1).AttackSlot);
    }

    [Fact]
    public void HitstopPausesSetupAndHitstunCancelsThePendingLaunch()
    {
        var sim = MakeSim();
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        for (int i = 0; i < 4; i++) Tick(sim, default);
        var frozen = sim.GetState(1);
        frozen.HitstopTicks = 4;
        sim.SetState(1, frozen);
        for (int i = 0; i < 4; i++) Tick(sim, default);
        Assert.Equal(frozen.AttackElapsedTicks, sim.GetState(1).AttackElapsedTicks);
        Assert.Equal(frozen.PY, sim.GetState(1).PY);
        var hit = sim.GetState(1);
        hit.State = ActionState.Hitstun;
        hit.HitstunTicks = 40;
        hit.VY = -2f;
        sim.SetState(1, hit);
        Tick(sim, default);
        Assert.Null(sim.GetActiveAbility(1));
        for (int i = 0; i < 20; i++)
        {
            Tick(sim, default);
            Assert.True(sim.GetState(1).VY < 1f, "no delayed launch after interruption");
        }
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(13, false)]
    [InlineData(10, true)]
    [InlineData(26, true)]
    [InlineData(38, true)]
    [InlineData(45, true)]
    public void SnapshotReplayPreservesThePendingOrCommittedLaunch(int snapshotTick, bool hop)
    {
        var sim = MakeSim(hop: hop, yaw: .7f);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        while (sim.CurrentTick < snapshotTick) Tick(sim, default);
        var snapshot = sim.CaptureSnapshot();
        var expected = new List<byte[]>();
        for (int i = 0; i < 30; i++)
        {
            Tick(sim, new InputState { MoveX = .5f, MoveY = .5f });
            expected.Add(sim.CaptureSnapshot().Data.ToArray());
        }
        sim.RestoreSnapshot(snapshot);
        foreach (var state in expected)
        {
            Tick(sim, new InputState { MoveX = .5f, MoveY = .5f });
            Assert.Equal(state, sim.CaptureSnapshot().Data.ToArray());
        }
    }

    [Fact]
    public void GroundedRecoveryStartsTheSameAutomaticSetup()
    {
        var sim = MakeSim(grounded: true);
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.E });
        Assert.False(sim.GetState(1).IsGrounded);
        Assert.True(sim.GetState(1).PY > TestHelpers.GroundPY(Definition()));
        Assert.Equal((byte)2, sim.GetState(1).JumpsLeft);
        while (sim.GetState(1).AttackElapsedTicks < LaunchTick) Tick(sim, default);
        Assert.Equal(15f, sim.GetState(1).VY);
    }

    private static CharacterDefinition Definition(bool hop = false, bool steer = false)
    {
        var definition = TestHelpers.EngineDef;
        definition = definition with { Movement = definition.Movement with { Gravity = 24f } };
        var slots = definition.CookedSlots!.ToArray();
        foreach (bool airborne in new[] { false, true })
        {
            CanonicalSlotProjection.TryGet(airborne, "E", out var address);
            slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal, address.Id, airborne,
                "Aimed recovery fixture", "", "", AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 210, true, false,
                new CookedTimeline(new[] { new CookedStage(hop ? (ushort)54 : EndTick, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[] { new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                        CharacterPackageCompiler.AimedRecoveryCapabilityId, "1", hop ? HopParameters : steer ? SteerParameters : Parameters) }) }));
        }
        definition = definition with { CookedSlots = slots };
        return definition;
    }

    private static ServerSimulation MakeSim(bool grounded = false, bool hop = false, float yaw = 0f, bool steer = false)
    {
        var definition = Definition(hop, steer);
        var state = TestHelpers.PlayerState(100f, 100f);
        state.FacingYaw = yaw;
        state.PY = TestHelpers.GroundPY(definition) + (grounded ? 0f : 10f);
        state.IsGrounded = grounded;
        if (!grounded)
        {
            state.JumpsLeft = state.AirDodgesLeft = 0;
            state.VX = 3f;
            state.VY = -14f;
            state.IsFastFalling = true;
        }
        var sim = TestHelpers.MakeSim(FloorArena(200f));
        sim.RegisterEntity(1, definition, state);
        return sim;
    }

    internal static ArenaDefinition FloorArena(float edgeX)
    {
        var arena = TestHelpers.TestArena();
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 0, AY = 0, AZ = 0, BX = 0, BY = 0, BZ = 200, CX = edgeX, CY = 0, CZ = 0 },
            new CollisionTriangle { AX = 0, AY = 0, AZ = 200, BX = edgeX, BY = 0, BZ = 200, CX = edgeX, CY = 0, CZ = 0 },
            new CollisionTriangle { AX = edgeX, AY = -5, AZ = 0, BX = edgeX, BY = 0, BZ = 0, CX = edgeX, CY = -5, CZ = 200 },
            new CollisionTriangle { AX = edgeX, AY = 0, AZ = 0, BX = edgeX, BY = 0, BZ = 200, CX = edgeX, CY = -5, CZ = 200 },
        };
        arena.MinX = arena.MinZ = 0f;
        arena.MaxX = edgeX;
        arena.MaxZ = 200f;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static void Tick(ServerSimulation sim, InputState input) => sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
}
