using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Rollback;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Full-world rollback gate (ADR-0039, plan phase 3): NetplayPredictor clients against an
/// in-process GameHost through the real codecs (<see cref="NetplayLab"/>), with delay, jitter,
/// loss, stalls and outages. Player 1 is "you" unless a test says otherwise.
/// </summary>
public class NetplayPredictorTests
{
    private readonly ITestOutputHelper _output;
    public NetplayPredictorTests(ITestOutputHelper output) => _output = output;

    private static readonly CharacterClass[] Duel = { CharacterClass.FightGuy, CharacterClass.Manki };

    [Fact]
    public void IdleAndMovementNeedNoCorrections()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 1);
        lab[1].Input = (_, tick) => Walk(tick);
        lab.Run(600);
        Report(lab);

        // You know your own inputs and the idle opponent's: every anchor confirms the prediction.
        var you = lab[1];
        Assert.Equal(you.Totals.Anchors, you.Totals.ExactAnchors);
        Assert.Equal(0, you.Totals.Corrections);
        AssertPresentedLikeGameHost(lab, you, 1, from: 1);
        AssertPresentedLikeGameHost(lab, you, 2, from: 1);

        // The opponent's client learns each of your input changes one frame late, and only then.
        int changes = Enumerable.Range(2, 600).Count(t => !SameInput(Walk((uint)t), Walk((uint)t - 1)));
        // (Its own fighter can differ too: automatic lock-on follows where it predicts you are.)
        Assert.InRange(lab[2].Totals.Corrections, 1, changes);
        foreach (var player in lab.Players)
        {
            AssertEffectsMatchGameHost(lab, player);
            Assert.Empty(player.Retracted);
        }
    }

    [Fact]
    public void ComboOnYouShowsEveryFreezeAndReaction()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 2, FacingPair(1.1f));
        lab[2].Input = (p, tick) => Chase(p, 1, attack: tick >= 30 && (tick - 30) % 24 == 0);
        lab.Run(420);
        Report(lab);

        var hitsOnYou = lab.ServerHits.Where(h => h.TargetEntityId == 1 && !h.Blocked).ToList();
        Assert.True(hitsOnYou.Count >= 6, $"hits on you {hitsOnYou.Count}");
        int frozen = 0, reacting = 0;
        foreach (var (tick, server) in Ticks(lab, 1))
        {
            if (!lab[1].Presented.TryGetValue(tick, out var presented))
                continue;
            var seen = presented[1];
            if (server.HitstopTicks > 0)
            {
                frozen++;
                Assert.True(seen.HitstopTicks > 0, $"tick {tick}: the GameHost froze you, your screen did not");
            }
            if (server.State == ActionState.Hitstun)
            {
                reacting++;
                Assert.Equal(ActionState.Hitstun, seen.State);
            }
        }
        Assert.True(frozen >= hitsOnYou.Count * 5 && reacting > frozen, $"frozen {frozen}, reacting {reacting}");
        AssertHitsMatchGameHost(lab, lab[1], predictedOnly: true);
        foreach (var player in lab.Players)
            AssertEffectsMatchGameHost(lab, player);
    }

    [Fact]
    public void AttackBufferedOutOfHitstunComesOutOnTheGameHostTick()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 3, FacingPair(1.1f));
        lab[2].Input = (p, tick) => Toward(p, 1, slot: tick == 30 ? AbilitySlots.Slot1 : (byte)0);
        bool pressed = false;
        lab[1].Input = (p, tick) =>
        {
            // Press near the end of the hit reaction you see, inside the input buffer window.
            var you = p.State(1);
            bool press = !pressed && you.State == ActionState.Hitstun && you.HitstunTicks is > 0 and <= 4;
            pressed |= press;
            return Toward(p, 2, slot: press ? AbilitySlots.Slot1 : (byte)0);
        };
        lab.Run(200);
        Report(lab);

        uint hit = lab.ServerHits.Single(h => h.TargetEntityId == 1).MatchTick;
        uint serverAttack = FirstTick(lab.ServerStates, 1, hit, s => s.State == ActionState.Attacking);
        uint seenAttack = FirstTick(lab[1].Presented, 1, hit, s => s.State == ActionState.Attacking);
        Assert.True(serverAttack > hit + 7, $"the buffered attack started at {serverAttack}, hit at {hit}");
        Assert.Equal(serverAttack, seenAttack);
        AssertPresentedLikeGameHost(lab, lab[1], 1, from: hit - 2);
        AssertEffectsMatchGameHost(lab, lab[1]);
    }

    [Fact]
    public void GrabAndThrowArePredictedForTheGrabber()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 4, FacingPair(0.8f));
        lab[1].Input = (p, tick) => Toward(p, 2) with
        {
            GrabPressed = tick == 30,
            ActiveSlot = tick == 60 ? AbilitySlots.Slot1 : (byte)0,
        };
        lab.Run(260);
        Report(lab);

        uint grab = FirstTick(lab.ServerStates, 1, 1, s => s.State == ActionState.Grabbing);
        uint throwStart = FirstTick(lab.ServerStates, 1, grab, s => s.State == ActionState.Throwing);
        uint thrown = FirstTick(lab.ServerStates, 2, grab, s => s.State == ActionState.Thrown);
        Assert.True(grab > 30 && throwStart > grab && thrown >= throwStart, $"grab {grab} throw {throwStart} thrown {thrown}");
        Assert.Equal(grab, FirstTick(lab[1].Presented, 1, 1, s => s.State == ActionState.Grabbing));
        Assert.Equal(thrown, FirstTick(lab[1].Presented, 2, 1, s => s.State == ActionState.Thrown));
        // The grabber's client predicts the whole interaction, victim included, without a correction.
        Assert.Equal(0, lab[1].Totals.Corrections);
        AssertPresentedLikeGameHost(lab, lab[1], 2, from: 1);
        // The grab's startup outlasts the prediction window, so the victim's client shows it on
        // time too; the throw press reaches it a few ticks late, after which it matches again.
        Assert.Equal(grab, FirstTick(lab[2].Presented, 2, 1, s => s.State == ActionState.Grabbed));
        AssertPresentedLikeGameHost(lab, lab[2], 2, from: throwStart + 8);
        foreach (var player in lab.Players)
            AssertEffectsMatchGameHost(lab, player);
    }

    [Fact]
    public void ProjectilesFlyOnEveryClientLikeOnTheGameHost()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 5, FacingPair(5f));
        // Ki Shot: press, hold to charge, release.
        lab[1].Input = (p, tick) => Toward(p, 2, slot: tick == 30 ? AbilitySlots.A : (byte)0) with
        {
            IsAiming = tick >= 30 && tick < 55,
        };
        lab.Run(200);
        Report(lab);

        var flight = lab.ServerHitboxes.Where(x => x.Value > 0).Select(x => x.Key).ToList();
        Assert.True(flight.Count >= 5, $"projectile ticks {flight.Count}");
        foreach (var player in lab.Players)
            foreach (var (tick, count) in lab.ServerHitboxes)
                if (player.PresentedHitboxes.TryGetValue(tick, out int seen))
                    Assert.True(count == seen, $"player {player.Id} tick {tick}: {seen} hitboxes, GameHost {count}");
        var hit = Assert.Single(lab.ServerHits);
        Assert.Equal(2ul, hit.TargetEntityId);
        foreach (var player in lab.Players)
        {
            AssertHitsMatchGameHost(lab, player, predictedOnly: true);
            AssertEffectsMatchGameHost(lab, player);
            Assert.Empty(player.Retracted);
        }
    }

    [Fact]
    public void KnockOutAndRespawnArePredicted()
    {
        // Manki walks off the stage's edge, falls through the blast line and respawns.
        var lab = new NetplayLab("slop_pit", Duel, seed: 6);
        var edge = lab.Server.GetState(2);
        lab[2].Input = (p, tick) => tick < 20 || p.State(2).Deaths > 0
            ? default
            : new InputState { MoveX = MathF.Sign(edge.PX == 0 ? 1 : edge.PX) };
        lab.Run(900);
        Report(lab);

        uint death = FirstTick(lab.ServerStates, 2, 1, s => s.Deaths > 0);
        Assert.True(death < 800, "Manki never fell out");
        foreach (var player in lab.Players)
        {
            uint seen = FirstTick(player.Presented, 2, 1, s => s.Deaths > 0);
            Assert.Equal(death, seen);
            Assert.Equal(1, player.Presented.Values.Max(states => (int)states[2].Deaths));
            // Manki's controls change when it sees itself respawn; that reaches the other client a frame later.
            AssertPresentedLikeGameHost(lab, player, 2, from: death + 10);
            AssertEffectsMatchGameHost(lab, player);
        }
    }

    [Fact]
    public void SimultaneousAttacksTradeOnEveryClient()
    {
        var mirror = new[] { CharacterClass.FightGuy, CharacterClass.FightGuy };
        var lab = new NetplayLab("slop_pit", mirror, seed: 7, FacingPair(1.0f));
        lab[1].Input = (p, tick) => Toward(p, 2, slot: tick == 30 ? AbilitySlots.Slot1 : (byte)0);
        lab[2].Input = (p, tick) => Toward(p, 1, slot: tick == 30 ? AbilitySlots.Slot1 : (byte)0);
        lab.Run(150);
        lab.Quiesce(120);
        Report(lab);

        Assert.Equal(2, lab.ServerHits.Count(h => h.MatchTick == lab.ServerHits[0].MatchTick));
        foreach (var player in lab.Players)
        {
            AssertEffectsMatchGameHost(lab, player);
            AssertHitsMatchGameHost(lab, player, predictedOnly: true);
        }
        AssertConverged(lab, 60);
    }

    [Fact]
    public void PredictedHitRolledBackByALateShieldIsRetracted()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 10, FacingPair(1.0f));
        // You kick; the opponent raises a shield so late that your client has already shown the hit.
        lab[1].Input = (p, tick) => Toward(p, 2, slot: tick == 30 ? AbilitySlots.Slot1 : (byte)0);
        lab[2].Input = (p, tick) => Toward(p, 1) with { ShieldHeld = tick >= 33 && tick < 60 };
        lab.Run(150);
        Report(lab);

        var blocked = Assert.Single(lab.ServerHits);
        Assert.True(blocked.Blocked, "the GameHost should have blocked the kick");
        var you = lab[1];
        var rolledBack = Assert.Single(you.EffectLog, e => !e.Start);
        Assert.Equal(PresentationEventSource.HitContact, rolledBack.Event.Source);
        Assert.Contains(you.EffectLog, e => e.Start && e.Event.Source == PresentationEventSource.BlockContact);
        Assert.True(you.Totals.Mispredictions > 0);
        // The clean hit your client predicted is ruled out; the blocked one is reported instead.
        var ruledOut = Assert.Single(you.RetractedHits);
        Assert.False(ruledOut.Blocked);
        Assert.Contains(you.Hits, h => h.Blocked);
        foreach (var player in lab.Players)
        {
            AssertEffectsMatchGameHost(lab, player);
            AssertHitsMatchGameHost(lab, player);
        }
        // The defender's own client never showed the hit.
        Assert.DoesNotContain(lab[2].EffectLog, e => e.Event.Source == PresentationEventSource.HitContact);
    }

    [Fact]
    public void HalfSecondUploadStallRecovers()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 8);
        lab[1].Input = (_, tick) => Walk(tick) with { Jump = tick is 212 or 226 };
        lab[1].Up.StallFrom = 200;
        lab[1].Up.StallUntil = 230;
        lab.Run(500);
        Report(lab);

        var you = lab[1];
        var sources = lab.ServerSources[1];
        _output.WriteLine($"GameHost consumed for you: received={sources[0]} held={sources[1]} neutral={sources[2]} rescued={lab.ServerRescued}");
        Assert.InRange(sources[(int)ConsumedInputSource.Held], 20, 30);
        Assert.True(sources[(int)ConsumedInputSource.Neutral] > 0);
        Assert.Equal(1, lab.ServerRescued); // the press 4 ticks late is kept, the one 18 ticks late dropped
        // Mismatches are the ticks where what the GameHost held differs from what you pressed.
        Assert.True(you.Totals.SelfInputMismatches >= 5 && you.Totals.Mispredictions > 0);
        Assert.True(you.Totals.MaxSelfCorrectionMeters > 0.05f);
        // A few frames after the stall every prediction is exact again.
        Assert.All(you.Windows.Where(w => w.Wall > 260), w => Assert.Equal(0, w.Stats.Corrections));
        AssertEffectsMatchGameHost(lab, you);
    }

    [Fact]
    public void DisconnectAndReconnectResumeFromTheGameHostState()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 9);
        lab[1].Input = (_, tick) => Walk(tick);
        lab.Run(200);
        lab[1].Up.Cut = lab[1].Down.Cut = true;
        lab.Run(60);
        lab[1].ServerSeesDisconnected = true;
        lab.Run(70);
        lab[1].Up.Cut = lab[1].Down.Cut = false;
        lab[1].ServerSeesDisconnected = false;
        lab[1].ServerInputs.Reset();
        lab.Run(200);
        Report(lab);

        var you = lab[1];
        Assert.True(you.Totals.StarvedTicks > 0, "the prediction window never filled");
        Assert.True(you.Totals.Jumps >= 1, "no jump to the GameHost after reconnecting");
        Assert.True(you.Totals.PredictionTicks <= NetplayLab.DefaultLeadTicks + 1);
        Assert.All(you.Windows.Where(w => w.Wall > 345), w => Assert.Equal(0, w.Stats.Corrections));
        AssertPresentedLikeGameHost(lab, you, 1, from: 345);
        AssertEffectsMatchGameHost(lab, you);
    }

    [Fact]
    public void FightersAreRegisteredAndTheTimelineSeededBeforePredictionStarts()
    {
        var arena = WorldSnapshotTests.LoadArena("slop_pit");
        var reference = WorldSnapshotTests.NewMatch(arena, Duel, reverseRegistration: false);
        var predictor = new NetplayPredictor(arena, 1, new StockMatchRule(250));
        var fightGuy = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
        var manki = BuiltInContentResolver.Resolve(CharacterClass.Manki);
        predictor.RegisterEntity(1, fightGuy.Definition, reference.GetState(1), fightGuy.BakedAnimation);
        Assert.True(predictor.SetTimeline(100));
        Assert.True(predictor.Advance(default));
        Assert.Equal(101u, predictor.LocalTick);
        Assert.False(predictor.SetTimeline(200));
        Assert.Throws<InvalidOperationException>(() =>
            predictor.RegisterEntity(2, manki.Definition, reference.GetState(2), manki.BakedAnimation));
    }

    [Fact]
    public void AFrameFromAnotherMatchIsRefusedAndPredictionCarriesOn()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 11);
        lab[1].Input = (_, tick) => Walk(tick);
        lab.Run(30);
        var you = lab[1].Predictor;
        uint anchor = you.AnchorTick;
        var stranger = WorldSnapshotTests.NewMatch(WorldSnapshotTests.LoadArena("slop_pit"),
            new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Bonk }, false);
        for (int i = 0; i < 100; i++)
            stranger.Tick(new Dictionary<ulong, InputState>());
        you.IngestFrame(new WorldFramePacket(MatchState.Ended, stranger.CaptureSnapshot(), Array.Empty<RelayedInput>(),
            Array.Empty<TimelinePresentationEvent>(), Array.Empty<SpellResolver.HitResult>()));
        var stats = you.TakeStats();
        Assert.Equal(1, stats.RejectedFrames);
        Assert.Equal(anchor, you.AnchorTick);
        Assert.Equal(MatchState.Playing, you.AuthoritativeMatchState);

        lab.Run(60);
        Assert.Equal(0, lab[1].Windows.Sum(w => w.Stats.Corrections));
        AssertPresentedLikeGameHost(lab, lab[1], 1, from: 1);
    }

    [Fact]
    public void PredictionWaitsWhenNoFrameArrivesForTheWholeWindow()
    {
        var lab = new NetplayLab("slop_pit", Duel, seed: 12);
        lab.Run(20);
        lab[1].Down.Cut = true;
        lab.Run((int)NetplayPredictor.MaxPredictionTicks + 30);
        var you = lab[1].Predictor;
        Assert.Equal(NetplayPredictor.MaxPredictionTicks, you.PredictionTicks);
        Assert.False(you.Advance(default));
        Assert.True(lab[1].Totals.StarvedTicks >= 25);
    }

    public static IEnumerable<object[]> Fights()
    {
        yield return new object[] { "slop_pit", new[] { CharacterClass.FightGuy, CharacterClass.Manki }, 21, 1, 0, 0.0 };
        yield return new object[] { "table_manners", new[] { CharacterClass.Wibou, CharacterClass.Bonk }, 22, 2, 1, 0.02 };
        yield return new object[] { "slop_pit", new[] { CharacterClass.Manki, CharacterClass.Wibou }, 23, 1, 2, 0.05 };
        yield return new object[] { "last_stop", new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk }, 24, 2, 2, 0.03 };
    }

    [Theory]
    [MemberData(nameof(Fights))]
    public void FightsOverImperfectLinksStayConsistent(string arena, CharacterClass[] roster, int seed,
        int delay, int jitter, double loss)
    {
        var lab = new NetplayLab(arena, roster, seed);
        var scratch = new Dictionary<ulong, InputState>();
        foreach (var player in lab.Players)
        {
            var driver = new WorldSnapshotTests.FightDriver(seed * 31 + (int)player.Id, lab.Ids.ToArray());
            player.Input = (p, tick) =>
            {
                if (tick < 10) return default;
                driver.Next(p.State, scratch);
                return scratch[p.Id];
            };
            player.LeadTicks = (uint)(delay + 2);
            foreach (var link in new[] { player.Up, player.Down })
            {
                link.DelayTicks = delay;
                link.JitterTicks = jitter;
                link.Loss = loss;
            }
        }
        lab.Run(1500);
        lab.Quiesce(150);
        Report(lab, details: false);

        // Enough hits for the ledger checks to mean something. The exact fight depends on the
        // host's float functions (seed 21 lands 12 hits under glibc 2.44, 7 on ubuntu-latest).
        Assert.True(lab.ServerHits.Count >= 5, $"server hits {lab.ServerHits.Count}");
        foreach (var player in lab.Players)
        {
            AssertEffectsMatchGameHost(lab, player);
            AssertHitsMatchGameHost(lab, player);
            Assert.Equal(0, player.Totals.RejectedFrames);
        }
        AssertConverged(lab, 60);
    }

    // ── Assertions ──

    /// <summary>What a player saw of <paramref name="fighter"/> at each local tick equals the GameHost's state.</summary>
    private static void AssertPresentedLikeGameHost(NetplayLab lab, NetplayLab.Player player, ulong fighter, uint from)
    {
        foreach (var (tick, server) in Ticks(lab, fighter))
        {
            if (tick < from || !player.Presented.TryGetValue(tick, out var presented))
                continue;
            if (!server.Equals(presented[fighter]))
                Assert.Fail($"player {player.Id} saw fighter {fighter} at tick {tick} unlike the GameHost:\n" +
                    Diff(server, presented[fighter]));
        }
    }

    /// <summary>Effects alternate start/stop per key, and on every tick whose record reached the
    /// player the effects left showing are exactly the GameHost's.</summary>
    private static void AssertEffectsMatchGameHost(NetplayLab lab, NetplayLab.Player player)
    {
        var showing = new HashSet<PresentationEventKey>();
        foreach (var (wall, start, evt) in player.EffectLog)
        {
            if (start)
                Assert.True(showing.Add(evt.Key), $"player {player.Id} started {evt.Key} twice (wall {wall})");
            else
                Assert.True(showing.Remove(evt.Key), $"player {player.Id} stopped {evt.Key} that was not showing (wall {wall})");
        }
        var authoritative = lab.ServerEvents.Select(e => e.Key).ToHashSet();
        var ghosts = showing.Where(k => player.CoveredEventTicks.Contains(k.MatchTick) && !authoritative.Contains(k)).ToList();
        var missed = authoritative.Where(k => player.CoveredEventTicks.Contains(k.MatchTick) && !showing.Contains(k)).ToList();
        Assert.True(ghosts.Count == 0, $"player {player.Id} still shows effects the GameHost never had: {string.Join(", ", ghosts)}");
        Assert.True(missed.Count == 0, $"player {player.Id} never showed: {string.Join(", ", missed)}");
    }

    /// <summary>Hit feedback alternates start/stop per hit, and on every tick whose record reached the
    /// player the hits left standing are exactly the GameHost's. With <paramref name="predictedOnly"/>,
    /// no hit was ever ruled out after being handed out.</summary>
    private void AssertHitsMatchGameHost(NetplayLab lab, NetplayLab.Player player, bool predictedOnly = false)
    {
        var standing = new HashSet<HitKey>();
        foreach (var (wall, start, hit) in player.HitLog)
        {
            var key = HitKey.Of(hit);
            if (start)
                Assert.True(standing.Add(key), $"player {player.Id} reported {key} twice (wall {wall})");
            else
                Assert.True(standing.Remove(key), $"player {player.Id} retracted {key} it never reported (wall {wall})");
        }
        var authoritative = lab.ServerHits.Select(HitKey.Of).ToHashSet();
        var ghosts = standing.Where(k => player.CoveredEventTicks.Contains(k.MatchTick) && !authoritative.Contains(k)).ToList();
        var missed = authoritative.Where(k => player.CoveredEventTicks.Contains(k.MatchTick) && !standing.Contains(k)).ToList();
        int late = player.HitLog.Count(e => e.Start && authoritative.Contains(HitKey.Of(e.Hit)) &&
            e.Wall > e.Hit.MatchTick - player.LeadTicks);
        _output.WriteLine($"player {player.Id} hits: GameHost {authoritative.Count}, reported {player.Hits.Count}, " +
            $"retracted {player.RetractedHits.Count}, reported after their tick was shown {late}");
        Assert.True(ghosts.Count == 0, $"player {player.Id} still stands by hits the GameHost never had: {string.Join(", ", ghosts)}");
        Assert.True(missed.Count == 0, $"player {player.Id} never reported: {string.Join(", ", missed)}");
        if (predictedOnly)
            Assert.Empty(player.RetractedHits);
    }

    /// <summary>After a quiet period every client shows exactly the GameHost's states.</summary>
    private static void AssertConverged(NetplayLab lab, int lastTicks)
    {
        foreach (var player in lab.Players)
            foreach (ulong id in lab.Ids)
                AssertPresentedLikeGameHost(lab, player, id, from: lab.Tick - (uint)lastTicks);
    }

    // ── Scenario helpers ──

    private static IEnumerable<(uint Tick, CharacterState State)> Ticks(NetplayLab lab, ulong fighter)
        => lab.ServerStates.OrderBy(x => x.Key).Select(x => (x.Key, x.Value[fighter]));

    private static uint FirstTick(Dictionary<uint, Dictionary<ulong, CharacterState>> states, ulong fighter, uint from,
        Func<CharacterState, bool> condition)
    {
        foreach (var (tick, byFighter) in states.OrderBy(x => x.Key))
            if (tick >= from && condition(byFighter[fighter]))
                return tick;
        throw new InvalidOperationException($"fighter {fighter} never met the condition from tick {from}");
    }

    /// <summary>Walk in a square, changing direction every 20 ticks, with a jump every 45 ticks.</summary>
    private static InputState Walk(uint tick)
    {
        if (tick < 10) return default;
        int leg = (int)(tick / 20) % 4;
        return new InputState
        {
            MoveX = leg switch { 0 => 1f, 2 => -1f, _ => 0f },
            MoveY = leg switch { 1 => 1f, 3 => -1f, _ => 0f },
            Jump = tick % 45 == 0,
            JumpHeld = tick % 45 < 6,
        };
    }

    /// <summary>Walk toward <paramref name="target"/> while out of reach and attack when told to.</summary>
    private static InputState Chase(NetplayLab.Player player, ulong target, bool attack)
    {
        var me = player.State(player.Id);
        var them = player.State(target);
        float distance = MathF.Sqrt((them.PX - me.PX) * (them.PX - me.PX) + (them.PZ - me.PZ) * (them.PZ - me.PZ));
        return Toward(player, target, move: distance > 1.2f ? 1f : 0f, slot: attack ? AbilitySlots.Slot1 : (byte)0);
    }

    /// <summary>Fighter 2 starts <paramref name="gap"/> metres from fighter 1 along x, the two facing each other.</summary>
    private static Func<ulong, CharacterState, CharacterState> FacingPair(float gap)
    {
        CharacterState first = default;
        return (id, state) =>
        {
            if (id == 1)
            {
                state.FacingYaw = MathF.PI / 2f;
                first = state;
                return state;
            }
            state.PX = first.PX + gap;
            state.PY = first.PY;
            state.PZ = first.PZ;
            state.FacingYaw = -MathF.PI / 2f;
            return state;
        };
    }

    /// <summary>An input aimed at <paramref name="target"/> as the player sees it, optionally walking toward it.</summary>
    private static InputState Toward(NetplayLab.Player player, ulong target, float move = 0f, byte slot = 0)
    {
        var me = player.State(player.Id);
        var them = player.State(target);
        float dx = them.PX - me.PX, dz = them.PZ - me.PZ;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        short yaw = (short)Math.Clamp(MathF.Atan2(dx, dz) * 18000f / MathF.PI, -18000f, 18000f);
        var input = new InputState
        {
            TargetEntityId = (byte)target,
            AimYaw = yaw,
            FacingYaw = yaw,
            AimDistance = (ushort)Math.Min(6500f, distance * 100f),
            ActiveSlot = slot,
        };
        if (move != 0f && distance > 0.01f)
        {
            input.MoveX = dx / distance * move;
            input.MoveY = dz / distance * move;
        }
        return input;
    }

    private static bool SameInput(InputState a, InputState b)
    {
        var x = new byte[InputState.Size];
        var y = new byte[InputState.Size];
        a.Write(x);
        b.Write(y);
        return x.AsSpan().SequenceEqual(y);
    }

    private static string Diff(CharacterState expected, CharacterState actual)
        => string.Join("\n", typeof(CharacterState).GetFields()
            .Where(f => !Equals(f.GetValue(expected), f.GetValue(actual)))
            .Select(f => $"  {f.Name}: GameHost={f.GetValue(expected)} client={f.GetValue(actual)}"));

    private void Report(NetplayLab lab, bool details = true)
    {
        foreach (var player in lab.Players)
            _output.WriteLine(player.Describe());
        _output.WriteLine($"GameHost: events={lab.ServerEvents.Count} hits={lab.ServerHits.Count} rescued={lab.ServerRescued} sources " +
            string.Join(" ", lab.ServerSources.Select(s => $"{s.Key}:[{string.Join(",", s.Value)}]")));
        if (!details) return;
        foreach (var hit in lab.ServerHits)
            _output.WriteLine($"  GameHost hit t={hit.MatchTick} {hit.OwnerEntityId}->{hit.TargetEntityId} slot={hit.AttackSlot} seq={hit.AttackSequence} hitstop={hit.HitstopTicks} blocked={hit.Blocked}");
        foreach (var player in lab.Players)
            foreach (var (wall, start, evt) in player.EffectLog.Where(e => !e.Start))
                _output.WriteLine($"  player {player.Id} stopped at wall {wall}: t={evt.MatchTick} {evt.EntityId} {evt.Source} {evt.PresentationId}");
    }
}
