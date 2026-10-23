using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class NetplayReviewRegressionTests
{
    [Fact]
    public void AnUnseenReorderedPressInsideTheRescueWindowIsConsumedExactlyOnce()
    {
        var timeline = new PeerInputTimeline();
        for (uint tick = 1; tick <= 20; tick++) timeline.Consume(tick);
        timeline.Accept(20, 20, new InputState[6], 100);
        var press = new[] { new InputState { Jump = true } };
        var report = timeline.Accept(20, 14, press, 100);
        Assert.Equal(1, report.RescuedPresses);
        Assert.True(timeline.Consume(21).Input.Jump);
        Assert.Equal(0, timeline.Accept(21, 14, press, 100).RescuedPresses);
        Assert.False(timeline.Consume(22).Input.Jump);
    }

    [Fact]
    public void RejectedEndedFrameCannotChangeSessionLifecycleOrAcceptedTick()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var arena = TestHelpers.TestArena();
        var initial = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(entry.Definition) };
        var server = new ServerSimulation(arena);
        server.RegisterEntity(1, entry.Definition, initial, entry.BakedAnimation);
        server.SetTick(1);
        var session = new NetplaySession(arena, 1, new StockMatchRule(3));
        session.RegisterEntity(1, entry.Definition, initial, entry.BakedAnimation);
        session.IngestFrames(new[] { Frame(MatchState.Playing, server) });
        var foreign = new ServerSimulation(arena);
        foreign.RegisterEntity(1, entry.Definition, initial, entry.BakedAnimation);
        foreign.RegisterEntity(2, entry.Definition, initial with { EntityId = 2, PX = 20 }, entry.BakedAnimation);
        foreign.SetTick(10);
        session.IngestFrames(new[] { Frame(MatchState.Ended, foreign) });
        Assert.Equal(1, session.Predictor.TakeStats().RejectedFrames);
        Assert.Equal(MatchState.Playing, session.Predictor.AuthoritativeMatchState);
        Assert.Equal(MatchState.Playing, session.MatchState);
        Assert.Equal(1U, session.NewestFrameTick);
        Assert.Equal(1U, session.Predictor.AnchorTick);
    }

    [Fact]
    public void EndedFrameRestoresTheExactFinalWorldAndDiscardsFuturePrediction()
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        var arena = TestHelpers.TestArena();
        var initial = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(entry.Definition) };
        var server = new ServerSimulation(arena);
        server.RegisterEntity(1, entry.Definition, initial, entry.BakedAnimation);
        var predictor = new NetplayPredictor(arena, 1);
        predictor.RegisterEntity(1, entry.Definition, initial, entry.BakedAnimation);
        predictor.IngestFrame(Frame(MatchState.Playing, server));
        var input = new InputState { MoveY = 1 };
        for (int i = 0; i < 3; i++) server.Tick(new() { [1] = input });
        for (int i = 0; i < 6; i++) predictor.Advance(input);
        predictor.IngestFrame(Frame(MatchState.Ended, server));
        Assert.Equal(MatchState.Ended, predictor.AuthoritativeMatchState);
        Assert.Equal(server.CurrentTick, predictor.LocalTick);
        Assert.Equal(0U, predictor.PredictionTicks);
        Assert.True(server.CaptureSnapshot().ContentEquals(predictor.Simulation.CaptureSnapshot()));
    }

    private static WorldFramePacket Frame(MatchState state, ServerSimulation simulation) => new(
        state, simulation.CaptureSnapshot(), new[] { new RelayedInput(1, true, default) },
        Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>());
}
