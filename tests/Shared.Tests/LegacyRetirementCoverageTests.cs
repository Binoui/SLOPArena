using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public class LegacyRetirementCoverageTests
{
    private static (ServerSimulation Server, NetplayPredictor Predictor) Match(
        CharacterDefinition def, ulong self = 1, ArenaDefinition? arena = null,
        Func<ulong, CharacterState, CharacterState>? place = null)
    {
        var stage = arena ?? TestHelpers.TestArena();
        var server = new ServerSimulation(stage);
        var predictor = new NetplayPredictor(stage, self);
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            var state = TestHelpers.PlayerState(x: id == 1 ? 20f : 40f, z: 20f) with
            {
                EntityId = id, PY = TestHelpers.GroundPY(def), FacingYaw = 0,
            };
            if (place != null) state = place(id, state);
            server.RegisterEntity(id, def, state);
            predictor.RegisterEntity(id, def, state);
        }
        return (server, predictor);
    }

    private static WorldFramePacket Frame(ServerSimulation server, RelayedInput[]? inputs = null,
        TimelinePresentationEvent[]? events = null)
        => WorldFramePacket.Deserialize(new WorldFramePacket(MatchState.Playing, server.CaptureSnapshot(),
            inputs ?? Array.Empty<RelayedInput>(), events ?? Array.Empty<TimelinePresentationEvent>(),
            server.LastTickHits).Serialize());

    private static void AssertWorld(ServerSimulation server, NetplayPredictor predictor)
    {
        foreach (var (id, expected) in server.GetAllStates())
            Assert.Equal(expected, predictor.GetState(id));
        Assert.True(server.CaptureSnapshot().ContentEquals(predictor.Simulation.CaptureSnapshot()),
            $"Whole worlds differ at server tick {server.SimulationTick}, local tick {predictor.LocalTick}.");
    }

    [Theory]
    [InlineData("slide-stop", 50)]
    [InlineData("fast-fall", 5)]
    [InlineData("slide-jump", 2)]
    [InlineData("wall-contact", 8)]
    public void CorrectedWorldReplaysDownFlow(string scenario, int ticks)
    {
        var def = TestHelpers.EngineDef;
        var arena = TestHelpers.TestArena();
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def), VX = def.Movement.RunSpeed, State = ActionState.Run,
        };
        switch (scenario)
        {
            case "slide-stop": initial.VX *= .8f; break;
            case "fast-fall":
                initial.IsGrounded = false;
                initial.PY += 4f;
                initial.VY = -2f;
                initial.State = ActionState.Idle;
                break;
            case "slide-jump": initial.State = ActionState.Sliding; initial.VX *= 1.2f; break;
            case "wall-contact":
                initial.State = ActionState.Sliding;
                initial.VZ = def.Movement.RunSpeed * .5f;
                arena.CollisionTriangles = new[]
                {
                    new CollisionTriangle { AX = -20, AZ = -20, BX = -20, BZ = 20, CX = 20, CZ = -20 },
                    new CollisionTriangle { AX = 20, AZ = 20, BX = 20, BZ = -20, CX = -20, CZ = 20 },
                    new CollisionTriangle { AX = 1.5f, AZ = -10, BX = 1.5f, BY = 8, BZ = -10, CX = 1.5f, CZ = 10 },
                };
                break;
        }
        var (server, predictor) = Match(def, arena: arena, place: (id, state) => id == 1 ? initial : state);
        server.SetState(1, initial with { PX = initial.PX + .5f, EntityId = 1 });
        var correction = Frame(server);
        for (int tick = 0; tick < ticks; tick++)
        {
            var input = scenario == "slide-jump"
                ? new InputState { Jump = tick == 0, JumpHeld = true, Down = true }
                : new InputState { Down = scenario != "fast-fall" || tick == 0, DownPressed = tick == 0 };
            server.Tick(new() { [1] = input, [2] = default });
            Assert.True(predictor.Advance(input));
        }
        predictor.IngestFrame(correction);
        AssertWorld(server, predictor);
        var actual = predictor.GetState(1);
        if (scenario == "slide-stop") Assert.Equal(ActionState.Crouching, actual.State);
        if (scenario == "fast-fall") Assert.True(actual.IsFastFalling);
        if (scenario == "slide-jump") Assert.Equal(JumpTakeoff.Slide, actual.JumpTakeoff);
        if (scenario == "wall-contact")
        {
            TestHelpers.AssertNear(0, actual.VX);
            Assert.True(actual.VZ > 0);
            Assert.True(actual.PX <= 1.5f - def.CapsuleRadius + .01f);
        }
    }

    [Theory]
    [InlineData(1ul)]
    [InlineData(2ul)]
    public void AirDodgeCorrectionPreservesEveryPhaseBoundaryForEitherFighter(ulong dodger)
    {
        var def = TestHelpers.EngineDef;
        var (server, predictor) = Match(def, place: (id, state) => id == dodger
            ? state with { PY = 20f, IsGrounded = false, AirDodgesLeft = 1, JumpsLeft = 0 } : state);
        var press = new InputState { ShieldHeld = true, ShieldPressed = true };
        server.Tick(new() { [1] = dodger == 1 ? press : default, [2] = dodger == 2 ? press : default });
        var frame = Frame(server, new[] { new RelayedInput(dodger, true, press) });
        Assert.Equal(ActionState.AirDodgeMovement, server.GetState(dodger).State);
        Assert.Equal(DefenseConfig.AirDodgeInvulnerabilityTicks, server.GetState(dodger).InvincibilityTicks);
        Assert.True(predictor.Advance(default));
        Assert.True(predictor.Advance(dodger == 1 ? press.WithoutEdges() : default));
        server.Tick(new() { [1] = dodger == 1 ? press.WithoutEdges() : default, [2] = dodger == 2 ? press.WithoutEdges() : default });
        predictor.IngestFrame(frame);
        AssertWorld(server, predictor);

        int movement = DefenseConfig.AirDodgeMovementTicks;
        int actionable = movement + DefenseConfig.AirDodgeRecoveryTicks;
        for (int elapsed = 2; elapsed <= actionable; elapsed++)
        {
            server.Tick(new() { [1] = dodger == 1 ? press.WithoutEdges() : default, [2] = dodger == 2 ? press.WithoutEdges() : default });
            Assert.True(predictor.Advance(dodger == 1 ? press.WithoutEdges() : default));
            AssertWorld(server, predictor);
            var state = predictor.GetState(dodger);
            Assert.Equal(Math.Max(0, DefenseConfig.AirDodgeInvulnerabilityTicks - elapsed), state.InvincibilityTicks);
            Assert.Equal(elapsed < movement ? ActionState.AirDodgeMovement
                : elapsed < actionable ? ActionState.AirDodgeRecovery : ActionState.Idle, state.State);
            if (elapsed == movement) Assert.Equal(DefenseConfig.AirDodgeRecoveryTicks, state.AirDodgeRecoveryTicks);
            if (elapsed == actionable - 1) Assert.Equal((ushort)1, state.AirDodgeRecoveryTicks);
            if (elapsed == actionable) Assert.Equal((ushort)0, state.AirDodgeRecoveryTicks);
        }
    }

    [Theory]
    [InlineData("jump")]
    [InlineData("down")]
    [InlineData("grab")]
    [InlineData("shield")]
    [InlineData("attack")]
    public void RelayedOpponentPressIsNotRepeatedBeyondTheAnchor(string action)
    {
        var def = TestHelpers.EngineDef;
        var (server, predictor) = Match(def, place: (id, state) => action == "down" && id == 2
            ? state with { IsGrounded = false, PY = 5f, VY = -1f } : state);
        var press = action switch
        {
            "jump" => new InputState { Jump = true, JumpHeld = true },
            "down" => new InputState { Down = true, DownPressed = true },
            "grab" => new InputState { GrabPressed = true },
            "shield" => new InputState { ShieldPressed = true },
            _ => new InputState { ActiveSlot = AbilitySlots.Normal1 },
        };
        server.SetTick(100);
        Assert.True(predictor.SetTimeline(100));
        var frame = Frame(server, new[] { new RelayedInput(2, true, press) });
        for (int tick = 0; tick < 5; tick++)
        {
            server.Tick(new() { [1] = default, [2] = press.WithoutEdges() });
            Assert.True(predictor.Advance(default));
        }
        predictor.IngestFrame(frame);
        AssertWorld(server, predictor);
        var opponent = predictor.GetState(2);
        Assert.NotEqual(ActionState.Attacking, opponent.State);
        Assert.NotEqual(ActionState.GrabAttempt, opponent.State);
        Assert.NotEqual(ActionState.Shielding, opponent.State);
        if (action == "jump") Assert.True(opponent.IsGrounded);
        if (action == "down") Assert.False(opponent.IsFastFalling);
    }

    [Fact]
    public void OpponentMovesEachTickAndNewNeutralOrDisconnectedInputReplacesHeldMovement()
    {
        var (server, predictor) = Match(TestHelpers.EngineDef);
        server.SetTick(200);
        Assert.True(predictor.SetTimeline(200));
        foreach (var input in new[]
        {
            new RelayedInput(2, true, TestHelpers.Input(moveX: 1f)),
            new RelayedInput(2, false, default),
            new RelayedInput(2, true, default),
        })
        {
            predictor.IngestFrame(Frame(server, new[] { input }));
            float before = predictor.GetState(2).PX;
            for (int tick = 0; tick < 12; tick++)
            {
                var inputs = new Dictionary<ulong, InputState> { [1] = default };
                if (input.HasInput) inputs[2] = input.Input;
                server.Tick(inputs);
                Assert.True(predictor.Advance(default));
                AssertWorld(server, predictor);
                if (input.Input.MoveX > 0)
                {
                    Assert.True(predictor.GetState(2).PX > before);
                    before = predictor.GetState(2).PX;
                }
            }
        }
        Assert.NotEqual(ActionState.Run, predictor.GetState(2).State);
    }

    [Fact]
    public void LateCapturedWorldReplacesSpeculativeAttackAndStaleFramesCannotRestoreReleasedHold()
    {
        var def = TestHelpers.EngineDef;
        def = def with { CaptureGeometry = new CookedCaptureGeometry(1.5f, .8f, 1.2f, .6f,
            new CaptureAnchor(0, .6f, .2f), new CaptureAnchor(0, .6f, .65f)) };
        var (server, predictor) = Match(def, self: 2, place: (id, state) => state with
        {
            PX = 20f, PZ = id == 1 ? 20f : 20.8f, FacingYaw = id == 1 ? 0 : MathF.PI,
        });
        for (int tick = 0; tick <= DefenseConfig.GrabStartupTicks; tick++)
        {
            server.Tick(new() { [1] = new InputState { GrabPressed = tick == 0 }, [2] = default });
            Assert.True(predictor.Advance(new InputState { ActiveSlot = tick == 0 ? AbilitySlots.Normal1 : (byte)0 }));
        }
        Assert.Equal(ActionState.Attacking, predictor.GetState(2).State);
        Assert.Equal(ActionState.Grabbed, server.GetState(2).State);
        var captured = Frame(server);
        predictor.IngestFrame(captured);
        AssertWorld(server, predictor);
        ulong interaction = server.GetState(2).InteractionId;
        Assert.NotEqual(0ul, interaction);
        for (int tick = 0; tick < 5; tick++)
        {
            var resist = new InputState { MoveX = 1, Jump = true, ActiveSlot = AbilitySlots.F };
            server.Tick(new() { [1] = default, [2] = resist });
            Assert.True(predictor.Advance(resist));
            AssertWorld(server, predictor);
            Assert.Equal(ActionState.Grabbed, predictor.GetState(2).State);
        }
        server.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 }, [2] = default });
        Assert.True(predictor.Advance(default));
        predictor.IngestFrame(Frame(server));
        Assert.Equal(ActionState.Throwing, predictor.GetState(1).State);
        AssertWorld(server, predictor);
        bool sawRelease = false;
        for (int tick = 0; tick < DefenseConfig.ThrowReleaseTicks + 80; tick++)
        {
            server.Tick(new() { [1] = default, [2] = default });
            Assert.True(predictor.Advance(default));
            AssertWorld(server, predictor);
            sawRelease |= predictor.GetState(2).InteractionPhase == (byte)DefenseInteractionPhase.UpTossReleased;
            if (tick % 12 == 0) predictor.IngestFrame(Frame(server));
        }
        Assert.True(sawRelease);
        Assert.Equal(ActionState.Idle, predictor.GetState(2).State);
        Assert.Equal(0ul, predictor.GetState(2).InteractionId);
        Assert.Equal(interaction, predictor.GetState(2).LastTerminalInteractionId);
        var released = Frame(server);
        predictor.IngestFrames(new[] { released, captured, released });
        predictor.IngestFrame(captured);
        AssertWorld(server, predictor);
        Assert.Equal(released.Tick, predictor.AnchorTick);
        var move = TestHelpers.Input(moveX: 1f);
        float before = predictor.GetState(2).PX;
        server.Tick(new() { [1] = default, [2] = move });
        Assert.True(predictor.Advance(move));
        AssertWorld(server, predictor);
        Assert.True(predictor.GetState(2).PX > before);
    }

    [Fact]
    public void LateBlockStunCorrectsSpeculativeAttackOnce()
    {
        var def = TestHelpers.EngineDef;
        var (server, predictor) = Match(def);
        var target = server.GetState(1);
        server.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = target.PX, Y = target.PY, Z = target.PZ,
            EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
            Radius = .5f, Damage = 5, BaseKnockback = 2, DurationTicks = 1, StunTicks = 20,
        });
        server.Tick(new() { [1] = new InputState { ShieldHeld = true }, [2] = default });
        Assert.True(predictor.Advance(new InputState { ActiveSlot = AbilitySlots.Normal1 }));
        Assert.Equal(ActionState.Attacking, predictor.GetState(1).State);
        var frame = Frame(server);
        predictor.IngestFrame(frame);
        AssertWorld(server, predictor);
        Assert.Equal(ActionState.Shielding, predictor.GetState(1).State);
        Assert.True(predictor.GetState(1).BlockStunTicks > 0);
        predictor.IngestFrame(frame);
        AssertWorld(server, predictor);
        Assert.Equal(1, predictor.TakeStats().Corrections);
    }

    [Fact]
    public void FreezeOnlyInfluenceAndQueuedLaunchSurviveLateWholeWorldCorrection()
    {
        var (server, predictor) = Match(TestHelpers.EngineDef);
        var target = server.GetState(1);
        server.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = target.PX, Y = target.PY, Z = target.PZ,
            EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
            Radius = 1.5f, Damage = 10, BaseKnockback = 6, KnockbackGrowth = 6,
            KnockbackAngle = 45, DurationTicks = 1, StunTicks = 30,
        });
        WorldFramePacket? frozen = null;
        bool sawFreeze = false, sawInfluence = false, sawLaunch = false, sawRecovery = false;
        for (int tick = 1; tick <= 150; tick++)
        {
            var input = server.GetState(1).HitstopTicks > 0 ? TestHelpers.Input(moveX: 1f) : default;
            server.Tick(new() { [1] = input, [2] = default });
            Assert.True(predictor.Advance(input));
            if (tick == 1) frozen = Frame(server);
            if (tick == 4) predictor.IngestFrame(frozen!);
            if (tick < 4) continue;
            AssertWorld(server, predictor);
            var state = predictor.GetState(1);
            sawFreeze |= state.HitstopTicks > 0;
            sawInfluence |= state.DIX > 0;
            sawLaunch |= state.State == ActionState.Hitstun && state.HitstopTicks == 0;
            sawRecovery |= sawLaunch && state.State == ActionState.Idle;
            if (tick % 20 == 0) predictor.IngestFrame(Frame(server));
        }
        Assert.True(sawFreeze && sawInfluence && sawLaunch && sawRecovery);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(83)]
    [InlineData(107)]
    public void BothPlayersAttackOverImperfectLinksWithoutInvalidStatesAndConverge(int seed)
    {
        var lab = new NetplayLab("slop_pit", new[] { CharacterClass.FightGuy, CharacterClass.Manki }, seed);
        foreach (var player in lab.Players)
        {
            var driver = new WorldSnapshotTests.FightDriver(seed + (int)player.Id, lab.Ids.ToArray());
            var scratch = new Dictionary<ulong, InputState>();
            player.Input = (p, _) => { driver.Next(p.State, scratch); return scratch[p.Id]; };
            player.Up.DelayTicks = (int)player.Id;
            player.Down.DelayTicks = 3;
            player.Up.JitterTicks = player.Down.JitterTicks = 1;
            player.Up.Loss = player.Down.Loss = .05;
        }
        for (int tick = 0; tick < 400; tick++)
        {
            lab.Step();
            foreach (var player in lab.Players)
                foreach (var state in player.Predictor.GetAllStates().Values)
                {
                    Assert.True(Enum.IsDefined(state.State));
                    Assert.InRange(state.DamagePercent, 0, 999);
                    Assert.True(float.IsFinite(state.PX) && float.IsFinite(state.PY) && float.IsFinite(state.PZ));
                    Assert.True(float.IsFinite(state.VX) && float.IsFinite(state.VY) && float.IsFinite(state.VZ));
                }
        }
        foreach (ulong id in lab.Ids)
            Assert.Contains(lab.ServerStates.Values, states => states[id].State == ActionState.Attacking);
        lab.Quiesce(150);
        foreach (var player in lab.Players)
            foreach (var (tick, states) in lab.ServerStates.Where(x => x.Key > lab.Tick - 30))
                foreach (ulong id in lab.Ids)
                    Assert.Equal(states[id], player.Presented[tick][id]);
    }

    [Fact]
    public void PredictedMoveEventKeepsAbsoluteTickThroughCorrectionAndConfirmation()
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[5] = new CookedSlotDefinition(5, "ground.R", false, "Presentation fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(20, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                {
                    new CookedEmitPresentationOperation(2, AuthoringUnit.Ticks, "presentation.test.self", 0),
                }),
            }));
        def = def with { CookedSlots = slots };
        var (server, predictor) = Match(def);
        server.SetTick(100);
        Assert.True(predictor.SetTimeline(100));
        var started = new List<TimelinePresentationEvent>();
        var retracted = new List<TimelinePresentationEvent>();
        var emitted = new List<TimelinePresentationEvent>();
        WorldFramePacket? correction = null, confirmation = null;
        for (int tick = 101; tick <= 135; tick++)
        {
            var input = new InputState
            {
                MoveX = tick <= 105 ? 1f : 0f,
                FaceToCamera = tick == 108, AimYaw = 9000,
                ActiveSlot = tick == 110 ? AbilitySlots.R : (byte)0,
            };
            server.Tick(new() { [1] = input, [2] = default });
            Assert.True(predictor.Advance(input));
            if (tick == 103)
            {
                var state = server.GetState(1);
                server.SetState(1, state with { PX = state.PX + .5f });
                correction = Frame(server);
            }
            if (tick == 107) predictor.IngestFrame(correction!);
            var events = server.GetPresentationEvents(clear: true).ToArray();
            emitted.AddRange(events);
            if (events.Length > 0) confirmation = Frame(server, events: events);
            if (tick == 116) predictor.IngestFrames(new[] { confirmation!, correction!, confirmation! });
            predictor.DrainPresentation(started, retracted);
            if (tick >= 107) AssertWorld(server, predictor);
        }
        Assert.Equal(111u, Assert.Single(emitted).MatchTick);
        Assert.Equal(emitted[0], Assert.Single(started));
        Assert.Empty(retracted);
        Assert.True(predictor.TakeStats().Corrections > 0);
    }

    [Fact]
    public void ReorderedContactRecordsKeepIndependentKeysAndDoNotChangeTheWorld()
    {
        var (server, predictor) = Match(TestHelpers.EngineDef);
        server.SetTick(20);
        var first = new TimelinePresentationEvent(20, 2, 0, "presentation.hit.a", 7,
            PresentationEventSource.HitContact, 1, 2, 3, .5f);
        var second = first with { OperationIndex = 1, PresentationId = "presentation.hit.b", WorldX = 4f };
        var timeline = first with { Source = PresentationEventSource.Timeline, PresentationId = "presentation.timeline" };
        var frame = Frame(server, events: new[] { second, first, timeline });
        predictor.IngestFrames(new[] { frame, frame });
        var started = new List<TimelinePresentationEvent>();
        var retracted = new List<TimelinePresentationEvent>();
        predictor.DrainPresentation(started, retracted);
        Assert.Equal(3, started.Count);
        Assert.Contains(first, started);
        Assert.Contains(second, started);
        Assert.Contains(timeline, started);
        Assert.Empty(retracted);
        AssertWorld(server, predictor);
        server.SetTick(21);
        var later = Frame(server, events: new[] { timeline, first, second });
        predictor.IngestFrames(new[] { later, frame, later });
        started.Clear();
        predictor.DrainPresentation(started, retracted);
        Assert.Empty(started);
        Assert.Empty(retracted);
        AssertWorld(server, predictor);
    }
}
