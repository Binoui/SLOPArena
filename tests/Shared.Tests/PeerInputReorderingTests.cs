using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class PeerInputReorderingTests
{
    private static readonly InputState[] Press = { new() { Jump = true } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingSourceRemainsEligibleAfterNewerInputWasConsumed(bool held)
    {
        var timeline = new PeerInputTimeline();
        if (held) timeline.Accept(9, 10, new[] { new InputState { MoveX = 1 } }, 100);
        for (uint tick = 10; tick <= 19; tick++) timeline.Consume(tick);
        timeline.Accept(19, 20, new InputState[1], 100);
        timeline.Consume(20);
        Assert.Equal(1, timeline.Accept(20, 14, Press, 100).RescuedPresses);
        Assert.Equal(0, timeline.Accept(20, 14, Press, 100).RescuedPresses);
        var rescued = timeline.Consume(21);
        Assert.True(rescued.Input.Jump);
        Assert.Equal(1, rescued.RescuedPresses);
        Assert.Equal(0, timeline.Accept(21, 14, Press, 100).RescuedPresses);
        Assert.False(timeline.Consume(22).Input.Jump);
    }

    [Fact]
    public void LateNeutralEvidenceDoesNotHandleAPressForTheSameSource()
    {
        var timeline = new PeerInputTimeline();
        timeline.Consume(20);
        timeline.Accept(20, 14, new InputState[1], 100);
        Assert.Equal(1, timeline.Accept(20, 14, Press, 100).RescuedPresses);
        Assert.True(timeline.Consume(21).Input.Jump);
    }

    [Fact]
    public void ReceivedNeutralSourceIsHandledButFallbackNeutralIsNot()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(9, 10, new InputState[1], 100);
        Assert.Equal(ConsumedInputSource.Received, timeline.Consume(10).Source);
        timeline.Consume(11);
        Assert.Equal(0, timeline.Accept(11, 10, Press, 100).RescuedPresses);
        Assert.Equal(1, timeline.Accept(11, 11, Press, 100).RescuedPresses);
        Assert.Equal(1, timeline.Consume(12).RescuedPresses);
    }

    [Fact]
    public void ExpiredSourceCannotEvictAReusedHandledSlot()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(24, 25, Press, 100);
        Assert.True(timeline.Consume(25).Input.Jump);
        Assert.Equal(1, timeline.Accept(25, 14, Press, 100).DroppedPresses);
        Assert.Equal(0, timeline.Accept(25, 25, Press, 100).RescuedPresses);
        Assert.False(timeline.Consume(26).Input.Jump);
        Assert.Equal(1, timeline.Accept(26, 17, Press, 100).RescuedPresses); // exactly ten ticks at application
        Assert.True(timeline.Consume(27).Input.Jump);
    }

    [Fact]
    public void FutureInputsAreHandledOnlyWhenConsumedAndOutOfHorizonInputsCanBeResent()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(20, 25, Press, 4); // too far ahead
        timeline.Accept(21, 25, Press, 4);
        timeline.Accept(21, 25, Press, 4);
        Assert.Equal(1, timeline.Accept(21, 16, Press, 4).RescuedPresses);
        Assert.True(timeline.Consume(22).Input.Jump);
        timeline.Consume(23); timeline.Consume(24);
        Assert.True(timeline.Consume(25).Input.Jump);
        Assert.Equal(0, timeline.Accept(25, 25, Press, 4).RescuedPresses);
        Assert.False(timeline.Consume(26).Input.Jump);
    }

    [Fact]
    public void ResetClearsHandledTicksPendingRescuesAndQueuedInputs()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(20, 14, Press, 100);
        timeline.Accept(20, 22, Press, 100);
        timeline.Reset();
        Assert.False(timeline.Consume(21).Input.Jump);
        Assert.False(timeline.Consume(22).Input.Jump);
        Assert.Equal(1, timeline.Accept(22, 14, Press, 100).RescuedPresses);
        Assert.True(timeline.Consume(23).Input.Jump);
    }

    [Fact]
    public void PendingRescueExpiresIfSimulationSkipsItsApplicationWindow()
    {
        var timeline = new PeerInputTimeline();
        Assert.Equal(1, timeline.Accept(20, 14, Press, 100).RescuedPresses);
        var consumed = timeline.Consume(30);
        Assert.False(consumed.Input.Jump);
        Assert.Equal(0, consumed.RescuedPresses);
    }
}
