using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The online client's transport-free core (ADR-0039, plan phase 4): when prediction starts,
/// what each input bundle carries, and that presses survive updates that predict nothing.
/// The GameHost end-to-end run is FullWorldNetplayTests in Server.Tests.
/// </summary>
public class NetplaySessionTests
{
    private static readonly CharacterClass[] Duel = { CharacterClass.FightGuy, CharacterClass.Manki };
    private const uint StartTick = 301;
    private const double Tick = 1.0 / 60.0;

    private sealed class Fixture
    {
        public readonly ServerSimulation Server;
        public readonly NetplaySession Session;
        public double Now = 100.0;

        public Fixture()
        {
            var arena = WorldSnapshotTests.LoadArena("slop_pit");
            Server = WorldSnapshotTests.NewMatch(arena, Duel, reverseRegistration: false);
            Session = new NetplaySession(arena, 1, new StockMatchRule(250));
            for (int slot = 0; slot < Duel.Length; slot++)
            {
                var content = BuiltInContentResolver.Resolve(Duel[slot]);
                ulong id = (ulong)(slot + 1);
                Session.RegisterEntity(id, content.Definition, Server.GetState(id), content.BakedAnimation);
            }
        }

        public void Frame(MatchState state)
            => Session.IngestFrames(new[]
            {
                new WorldFramePacket(state, Server.CaptureSnapshot(),
                    new[] { new RelayedInput(1, true, default), new RelayedInput(2, true, default) },
                    Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>()),
            });

        public void Clock(uint serverTick)
            => Session.ObserveControl(new NetplayControlPacket(NetplayControlKind.Clock, 1, serverTick, StartTick), Now, 0);

        /// <summary>The GameHost's countdown through its GO frame, then one played tick per call.</summary>
        public void ReachGo()
        {
            Server.SetTick(StartTick - 2);
            Frame(MatchState.Countdown);
            Server.SetTick(StartTick - 1);
            Frame(MatchState.Playing);
            Clock(StartTick - 1);
        }
    }

    [Fact]
    public void NothingIsPredictedBeforeTheGoFrame()
    {
        var f = new Fixture();
        Assert.False(f.Session.Update(default, f.Now));
        f.Server.SetTick(120);
        f.Frame(MatchState.Countdown);
        f.Clock(120);
        Assert.False(f.Session.Update(default, f.Now));
        Assert.Equal(120u, f.Session.LocalTick); // countdown frames move the timeline, nothing is simulated
        Assert.True(f.Session.HasFrame);

        f.Server.SetTick(StartTick - 1);
        f.Frame(MatchState.Playing);
        f.Clock(StartTick - 1);
        f.Now += Tick;
        Assert.True(f.Session.Update(default, f.Now));
        Assert.True(f.Session.LocalTick >= StartTick);
        Assert.Equal(f.Session.RecentInputs.Length, (int)(f.Session.LocalTick - (StartTick - 1)));
    }

    [Fact]
    public void BundlesEndAtTheLocalTickAndRestartAfterAJump()
    {
        var f = new Fixture();
        f.ReachGo();
        for (int i = 0; i < 10; i++)
        {
            f.Now += Tick;
            f.Session.Update(new InputState { MoveX = 1f }, f.Now);
        }
        Assert.Equal(InputBundle.MaxInputs, f.Session.RecentInputs.Length);
        Assert.All(f.Session.RecentInputs.ToArray(), input => Assert.Equal(1f, input.MoveX));

        // The GameHost is far ahead (a stall): the timeline jumps, older inputs are not resent.
        uint ahead = f.Session.LocalTick + 100;
        f.Server.SetTick(ahead);
        f.Frame(MatchState.Playing);
        Assert.Equal(ahead, f.Session.LocalTick);
        Assert.Equal(0, f.Session.RecentInputs.Length);
        f.Clock(ahead);
        f.Now += Tick;
        Assert.True(f.Session.Update(new InputState { MoveX = -1f }, f.Now));
        Assert.Equal((int)(f.Session.LocalTick - ahead), f.Session.RecentInputs.Length);
        Assert.Equal(-1f, f.Session.RecentInputs[^1].MoveX);
    }

    [Fact]
    public void APressOnAnUpdateThatPredictsNothingIsAppliedOnTheNextTick()
    {
        var f = new Fixture();
        f.ReachGo();
        f.Now += Tick;
        // Updates at one instant run ahead of the frontier until the pacer skips one.
        bool skipped = false;
        for (int i = 0; i < 20 && !skipped; i++)
            skipped = !f.Session.Update(new InputState { Jump = true, JumpHeld = true }, f.Now);
        Assert.True(skipped, "the pacer never skipped an update");

        f.Now += 10 * Tick;
        Assert.True(f.Session.Update(new InputState { JumpHeld = true }, f.Now));
        Assert.True(f.Session.Predictor.TryGetRecordedInput(f.Session.LocalTick, out var applied));
        Assert.True(applied.Jump, "the press made during the skipped update was lost");
    }

    [Fact]
    public void PressesWaitWhileThePredictionWindowIsFull()
    {
        var f = new Fixture();
        f.ReachGo();
        // No frames arrive: the window fills, then updates predict nothing.
        for (int i = 0; i < NetplayPredictor.MaxPredictionTicks + 20; i++)
        {
            f.Now += Tick;
            f.Clock(StartTick - 1 + (uint)i);
            f.Session.Update(default, f.Now);
        }
        Assert.Equal(NetplayPredictor.MaxPredictionTicks, f.Session.Predictor.PredictionTicks);
        f.Now += Tick;
        Assert.False(f.Session.Update(new InputState { ActiveSlot = AbilitySlots.Normal1 }, f.Now));

        // A newer frame frees the window; the press goes out with the next predicted tick.
        f.Server.SetTick(f.Session.LocalTick - 2);
        f.Frame(MatchState.Playing);
        f.Now += Tick;
        Assert.True(f.Session.Update(default, f.Now));
        var sent = f.Session.RecentInputs.ToArray();
        Assert.Contains(sent, input => input.ActiveSlot == AbilitySlots.Normal1);
    }
}
