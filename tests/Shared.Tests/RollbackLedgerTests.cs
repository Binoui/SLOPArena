using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Start/stop rules for one-shot outputs across rollback (ADR-0039, plan phase 3).</summary>
public class RollbackLedgerTests
{
    private static RollbackLedger<PresentationEventKey, TimelinePresentationEvent> NewLedger()
        => new(static e => e.Key, static k => k.MatchTick);

    private static TimelinePresentationEvent Evt(uint tick, ulong owner = 1, int operation = 0)
        => new(tick, owner, operation, "presentation.test", 1, PresentationEventSource.Timeline, 0f, 0f, 0f, 0f);

    private static (List<TimelinePresentationEvent> Started, List<TimelinePresentationEvent> Retracted) Drain(
        RollbackLedger<PresentationEventKey, TimelinePresentationEvent> ledger)
    {
        var started = new List<TimelinePresentationEvent>();
        var retracted = new List<TimelinePresentationEvent>();
        ledger.Drain(started, retracted);
        return (started, retracted);
    }

    [Fact]
    public void PredictedEventConfirmedByTheGameHostStartsOnce()
    {
        var ledger = NewLedger();
        ledger.RecordPredicted(new[] { Evt(10) });
        Assert.Single(Drain(ledger).Started);
        ledger.RecordAuthoritative(10, new[] { Evt(10) });
        ledger.RecordAuthoritative(10, new[] { Evt(10) }); // the same record arrives in three frames
        var (started, retracted) = Drain(ledger);
        Assert.Empty(started);
        Assert.Empty(retracted);
    }

    [Fact]
    public void PredictedEventMissingFromTheGameHostRecordIsRetracted()
    {
        var ledger = NewLedger();
        ledger.RecordPredicted(new[] { Evt(10), Evt(10, operation: 1) });
        Drain(ledger);
        ledger.RecordAuthoritative(10, new[] { Evt(10, operation: 1) });
        var (started, retracted) = Drain(ledger);
        Assert.Empty(started);
        Assert.Equal(Evt(10).Key, Assert.Single(retracted).Key);
    }

    [Fact]
    public void ReSimulationRetractsOnlyWhatItNoLongerProduces()
    {
        var ledger = NewLedger();
        ledger.RecordPredicted(new[] { Evt(11), Evt(12), Evt(13) });
        Drain(ledger);
        ledger.BeginResimulation(afterTick: 10);
        ledger.RecordPredicted(new[] { Evt(11) });
        ledger.RecordPredicted(new[] { Evt(13, owner: 2) });
        ledger.EndResimulation();
        var (started, retracted) = Drain(ledger);
        Assert.Equal(new[] { Evt(13, owner: 2).Key }, started.Select(e => e.Key));
        Assert.Equal(new[] { Evt(12).Key, Evt(13).Key }, retracted.Select(e => e.Key).OrderBy(k => k.MatchTick));
    }

    [Fact]
    public void ConfirmedEventsSurviveAReSimulationThatSkipsTheirTick()
    {
        var ledger = NewLedger();
        ledger.RecordPredicted(new[] { Evt(10) });
        ledger.RecordAuthoritative(10, new[] { Evt(10) });
        Drain(ledger);
        ledger.BeginResimulation(afterTick: 9); // defensive: anchors never precede a confirmed tick
        ledger.EndResimulation();
        Assert.Empty(Drain(ledger).Retracted);
    }

    [Fact]
    public void UnpredictedGameHostEventStartsLateExactlyOnce()
    {
        var ledger = NewLedger();
        ledger.RecordAuthoritative(20, new[] { Evt(20, owner: 2) });
        ledger.RecordAuthoritative(20, new[] { Evt(20, owner: 2) });
        Assert.Single(Drain(ledger).Started);
        // A later re-simulation cannot reach a recorded tick, and prediction of it is not repeated.
        ledger.RecordPredicted(new[] { Evt(20, owner: 2) });
        Assert.Empty(Drain(ledger).Started);
    }

    [Fact]
    public void PredictionOfATickTheGameHostRuledOutIsNeverStarted()
    {
        var ledger = NewLedger();
        ledger.RecordAuthoritative(30, System.Array.Empty<TimelinePresentationEvent>());
        ledger.RecordPredicted(new[] { Evt(30) });
        Assert.Empty(Drain(ledger).Started);
    }

    [Fact]
    public void StartAndRetractBetweenDrainsCancelOut()
    {
        var ledger = NewLedger();
        ledger.RecordPredicted(new[] { Evt(40) });
        ledger.BeginResimulation(39);
        ledger.EndResimulation();
        var (started, retracted) = Drain(ledger);
        Assert.Empty(started);
        Assert.Empty(retracted);
    }

    [Fact]
    public void RetractedThenPredictedAgainBeforeTheDrainKeepsShowing()
    {
        var ledger = NewLedger();
        ledger.RecordPredicted(new[] { Evt(50) });
        Drain(ledger);
        ledger.BeginResimulation(49);
        ledger.EndResimulation(); // gone in this re-simulation…
        ledger.BeginResimulation(49);
        ledger.RecordPredicted(new[] { Evt(50) }); // …back in the next
        ledger.EndResimulation();
        var (started, retracted) = Drain(ledger);
        Assert.Empty(started);
        Assert.Empty(retracted);
    }

    [Fact]
    public void RecordsOlderThanTheHorizonAreIgnored()
    {
        var ledger = NewLedger();
        uint newest = 1000;
        ledger.RecordAuthoritative(newest, System.Array.Empty<TimelinePresentationEvent>());
        ledger.RecordAuthoritative(newest - RollbackLedger<PresentationEventKey, TimelinePresentationEvent>.HorizonTicks - 1,
            new[] { Evt(newest - RollbackLedger<PresentationEventKey, TimelinePresentationEvent>.HorizonTicks - 1) });
        Assert.Empty(Drain(ledger).Started);
    }

    [Fact]
    public void HitsUseTheSameRules()
    {
        var ledger = new RollbackLedger<HitKey, SpellResolver.HitResult>(static h => HitKey.Of(h), static k => k.MatchTick);
        var clean = new SpellResolver.HitResult { MatchTick = 36, OwnerEntityId = 1, TargetEntityId = 2, AttackSequence = 1, AttackSlot = 3 };
        var blocked = clean with { Blocked = true };
        ledger.RecordPredicted(new[] { clean });
        ledger.RecordAuthoritative(36, new[] { blocked });
        var started = new List<SpellResolver.HitResult>();
        var retracted = new List<SpellResolver.HitResult>();
        ledger.Drain(started, retracted);
        // Never handed out, the clean hit simply disappears; the blocked one is reported.
        Assert.True(Assert.Single(started).Blocked);
        Assert.Empty(retracted);
    }
}
