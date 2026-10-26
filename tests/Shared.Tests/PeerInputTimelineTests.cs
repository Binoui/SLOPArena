using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The GameServer's per-peer input policy (ADR-0039 decision 5). On 2026-10-18 two
/// half-second upload stalls made the server drop a shielding player's shield after six
/// held ticks and discard every late press.
/// </summary>
public class PeerInputTimelineTests
{
    private const uint Horizon = NetplayClock.HistoryTicks;

    private static InputState Move(float x, bool shield = false) => new() { MoveX = x, ShieldHeld = shield };

    [Fact]
    public void EachTickConsumesItsOwnInput()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 12, new[] { Move(0.5f), Move(1f) }, Horizon);

        var first = timeline.Consume(11);
        var second = timeline.Consume(12);

        Assert.Equal(ConsumedInputSource.Received, first.Source);
        Assert.Equal(0.5f, first.Input.MoveX);
        Assert.Equal(ConsumedInputSource.Received, second.Source);
        Assert.Equal(1f, second.Input.MoveX);
    }

    [Fact]
    public void MissingInputHoldsTheNewestControlsForTheHoldWindowThenGoesNeutral()
    {
        var timeline = new PeerInputTimeline();
        var pressed = Move(1f, shield: true);
        pressed.ActiveSlot = AbilitySlots.Slot1;
        pressed.Jump = true;
        timeline.Accept(10, 11, new[] { pressed }, Horizon);
        Assert.Equal(ConsumedInputSource.Received, timeline.Consume(11).Source);

        for (uint tick = 12; tick <= 11 + PeerInputTimeline.DefaultHoldTicks; tick++)
        {
            var held = timeline.Consume(tick);
            Assert.Equal(ConsumedInputSource.Held, held.Source);
            Assert.Equal(1f, held.Input.MoveX);
            Assert.True(held.Input.ShieldHeld);
            Assert.False(held.Input.Jump);
            Assert.Equal((byte)0, held.Input.ActiveSlot);
        }
        var neutral = timeline.Consume(12 + PeerInputTimeline.DefaultHoldTicks);
        Assert.Equal(ConsumedInputSource.Neutral, neutral.Source);
        Assert.Equal(default, neutral.Input);
    }

    [Fact]
    public void LateInputRefreshesTheHeldControls()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 11, new[] { Move(1f) }, Horizon);
        timeline.Consume(11);
        timeline.Consume(12);
        timeline.Consume(13);

        // The stalled datagram arrives: ticks 12–13 already ran with the old controls.
        timeline.Accept(13, 13, new[] { Move(-1f), Move(-1f, shield: true) }, Horizon);
        var held = timeline.Consume(14);

        Assert.Equal(ConsumedInputSource.Held, held.Source);
        Assert.Equal(-1f, held.Input.MoveX);
        Assert.True(held.Input.ShieldHeld);
    }

    [Fact]
    public void HoldWindowCountsFromTheNewestKnownInput()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 11, new[] { Move(1f) }, Horizon);
        timeline.Consume(11);
        for (uint tick = 12; tick <= 40; tick++)
            timeline.Consume(tick);
        Assert.Equal(ConsumedInputSource.Neutral, timeline.Consume(41).Source);

        // A late input from tick 35 shows the player is still there: hold resumes until 55.
        timeline.Accept(41, 35, new[] { Move(0.25f) }, Horizon);
        var resumed = timeline.Consume(42);
        Assert.Equal(ConsumedInputSource.Held, resumed.Source);
        Assert.Equal(0.25f, resumed.Input.MoveX);
        for (uint tick = 43; tick <= 55; tick++)
            Assert.Equal(ConsumedInputSource.Held, timeline.Consume(tick).Source);
        Assert.Equal(ConsumedInputSource.Neutral, timeline.Consume(56).Source);
    }

    [Fact]
    public void LatePressInsideTheRescueWindowIsAppliedOnTheNextTick()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 11, new[] { Move(1f) }, Horizon);
        timeline.Consume(11);
        timeline.Consume(12); // missing: held

        var press = Move(1f);
        press.Jump = true;
        press.ActiveSlot = AbilitySlots.Slot3;
        var report = timeline.Accept(12, 12, new[] { press }, Horizon);
        var next = timeline.Consume(13);

        Assert.Equal(1, report.RescuedPresses);
        Assert.Equal(ConsumedInputSource.Held, next.Source);
        Assert.Equal(1, next.RescuedPresses);
        Assert.True(next.Input.Jump);
        Assert.Equal(AbilitySlots.Slot3, next.Input.ActiveSlot);
        Assert.False(timeline.Consume(14).Input.Jump);
    }

    [Fact]
    public void LatePressBeyondTheRescueWindowIsDropped()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 11, new[] { Move(1f) }, Horizon);
        for (uint tick = 11; tick <= 22; tick++)
            timeline.Consume(tick);

        // Tick 12's jump would land on tick 23: eleven ticks late, one past the window.
        var press = Move(1f);
        press.Jump = true;
        var report = timeline.Accept(22, 12, new[] { press }, Horizon);
        var next = timeline.Consume(23);

        Assert.Equal(0, report.RescuedPresses);
        Assert.Equal(1, report.DroppedPresses);
        Assert.False(next.Input.Jump);
        Assert.Equal(0, next.RescuedPresses);
    }

    [Fact]
    public void PressesAreRescuedOncePerSourceTickAndNeverForConsumedTicks()
    {
        var timeline = new PeerInputTimeline();
        var onTime = Move(1f);
        onTime.Jump = true;
        timeline.Accept(10, 11, new[] { onTime }, Horizon);
        Assert.True(timeline.Consume(11).Input.Jump);

        // Redundant bundles repeat tick 11 after it ran; its press was already consumed.
        var latePress = Move(1f);
        latePress.ShieldPressed = true;
        timeline.Accept(11, 12, new[] { onTime, Move(1f) }, Horizon);
        Assert.False(timeline.Consume(12).Input.Jump);

        timeline.Consume(13); // missing
        timeline.Accept(13, 13, new[] { latePress }, Horizon);
        timeline.Accept(13, 13, new[] { latePress }, Horizon); // duplicate datagram
        var rescued = timeline.Consume(14);
        Assert.Equal(1, rescued.RescuedPresses);
        Assert.True(rescued.Input.ShieldPressed);
        Assert.False(timeline.Consume(15).Input.ShieldPressed);
    }

    [Fact]
    public void ASlotPressedOnTheConsumedTickWinsOverARescuedOne()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 11, new[] { Move(1f) }, Horizon);
        timeline.Consume(11);
        timeline.Consume(12);

        var late = Move(1f);
        late.ActiveSlot = AbilitySlots.Slot3;
        var current = Move(1f);
        current.ActiveSlot = AbilitySlots.Slot4;
        timeline.Accept(12, 13, new[] { late, current }, Horizon);

        Assert.Equal(AbilitySlots.Slot4, timeline.Consume(13).Input.ActiveSlot);
    }

    [Fact]
    public void ResetForgetsQueuedAndRememberedInput()
    {
        var timeline = new PeerInputTimeline();
        timeline.Accept(10, 12, new[] { Move(1f), Move(1f) }, Horizon);
        timeline.Consume(11);
        timeline.Reset();

        Assert.Equal(ConsumedInputSource.Neutral, timeline.Consume(12).Source);
    }
}
