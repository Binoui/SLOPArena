using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemShowPlanTests
{
    /// <summary>Every airing of a regular in episode order, solo or as a remix child.</summary>
    private static IEnumerable<(int Episode, ChannelDraw Draw)> Showings(MayhemShowPlan plan)
    {
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
            switch (plan.AiringAt(episode))
            {
                case SoloAiring solo: yield return (episode, solo.Draw); break;
                case RemixAiring remix: yield return (episode, remix.Goal); yield return (episode, remix.Hazard); break;
            }
    }

    [Fact]
    public void AChannelNeverReairsATwistWhileItHasAnUnseenOneSoloOrInARemix()
    {
        for (uint seed = 1; seed <= 20_000; seed++)
        {
            var seen = new Dictionary<ChannelId, HashSet<TwistId>>();
            foreach (var (episode, draw) in Showings(MayhemShowPlan.For(seed)))
            {
                if (!seen.TryGetValue(draw.Channel, out var twists)) seen[draw.Channel] = twists = new();
                Assert.False(twists.Contains(draw.Twist) && twists.Count < MayhemChannels.Find(draw.Channel).Twists.Count,
                    $"seed {seed} episode {episode} re-airs {MayhemChannels.Find(draw.Channel).Twist(draw.Twist).CopyKey} with an unseen twist left");
                twists.Add(draw.Twist);
            }
        }
    }

    [Fact]
    public void NoRemixRerunsAnEarlierRemixsPairAndBothTwists()
    {
        for (uint seed = 1; seed <= 20_000; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            var aired = new HashSet<(PrimePairId, TwistId, TwistId)>();
            for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
                if (plan.AiringAt(episode) is RemixAiring remix)
                    Assert.True(aired.Add((remix.Pair, remix.Goal.Twist, remix.Hazard.Twist)),
                        $"seed {seed} episode {episode} reruns {remix.PairCopyKey} {remix.GoalTwistCopyKey} + {remix.HazardTwistCopyKey}");
        }
    }

    [Fact]
    public void ActsOneAndTwoCloseWithTwoDifferentRemixesAndEveryChildTwistAirs()
    {
        var orders = new HashSet<(byte, byte)>();
        var aired = new HashSet<(PrimePairId, TwistId, TwistId)>();
        for (uint seed = 1; seed <= 2000; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            var bosses = Enumerable.Range(0, 2)
                .Select(act => Assert.IsType<RemixAiring>(plan.AiringAt(act * MayhemShowTuning.EpisodesPerAct + MayhemShowTuning.RegularPerAct)))
                .ToArray();
            Assert.NotEqual(bosses[0].Pair, bosses[1].Pair);
            orders.Add((bosses[0].Pair.Value, bosses[1].Pair.Value));
            foreach (var remix in bosses)
            {
                var row = PrimePairs.Find(remix.Pair);
                Assert.Equal((row.Goal, row.Hazard), (remix.Goal.Channel, remix.Hazard.Channel));
                Assert.Equal(MayhemChannels.PrimeTime, remix.Channel);
                Assert.NotEqual(remix.Goal.Seed, remix.Hazard.Seed);
                Assert.Equal(ExtraVerb.None, remix.Extra);
                aired.Add((remix.Pair, remix.Goal.Twist, remix.Hazard.Twist));
            }
        }
        Assert.Equal(PrimePairs.All.Count * (PrimePairs.All.Count - 1), orders.Count);
        Assert.Equal(MayhemWorld.RemixCombinations().Count(), aired.Count);
    }

    [Theory]
    [InlineData(1, 1, 1, 360)]
    [InlineData(1, 3, 1, 480)]
    [InlineData(2, 1, 1, 360)]
    [InlineData(2, 2, 3, 420)]
    public void ARemixTakesItsRowTimingUnlessAChildTwistNeedsLonger(byte pair, byte goal, byte hazard, ushort play)
    {
        var (seed, episode) = MayhemWorld.SeedsWithRemix(new(pair), new(goal), new(hazard), 1).Single();
        var plan = MayhemShowPlan.For(seed);
        Assert.Equal(new ChannelTiming(90, play), plan.AiringAt(episode).Timing);
        Assert.Equal(MayhemSpeed.Play(new ChannelTiming(90, play), SpeedTier.Three), plan.Deadline(episode) - plan.PlayStart(episode));
    }

    [Fact]
    public void ExtraVerbsFoldNonePlusXToXAndRejectTwoDifferentVerbs()
    {
        Assert.Equal(ExtraVerb.None, PrimePairs.FoldExtra(ExtraVerb.None, ExtraVerb.None));
        Assert.Equal(ExtraVerb.Shield, PrimePairs.FoldExtra(ExtraVerb.None, ExtraVerb.Shield));
        Assert.Equal(ExtraVerb.Shield, PrimePairs.FoldExtra(ExtraVerb.Shield, ExtraVerb.None));
        Assert.Equal(ExtraVerb.Grab, PrimePairs.FoldExtra(ExtraVerb.Grab, ExtraVerb.Grab));
        Assert.Throws<System.InvalidOperationException>(() => PrimePairs.FoldExtra(ExtraVerb.Shield, ExtraVerb.Grab));
    }

    [Fact]
    public void AFullDeckOfThirteenDealsTwelveDistinctRegulars()
    {
        for (uint seed = 1; seed <= 2000; seed++)
        {
            var dealt = MayhemShowPlan.Deal(seed, Enumerable.Range(1, 13).Select(i => new ChannelId((ushort)i)).ToArray());
            Assert.Equal(12, dealt.Distinct().Count());
        }
    }

    [Fact]
    public void ThePlanIsAFunctionOfTheSeed()
    {
        var a = MayhemShowPlan.For(41);
        var b = MayhemShowPlan.For(41);
        var episodes = Enumerable.Range(0, MayhemShowTuning.Episodes).ToArray();
        Assert.Equal(episodes.Select(a.AiringAt), episodes.Select(b.AiringAt));
        Assert.Equal(episodes.Select(a.CallStart), episodes.Select(b.CallStart));
        Assert.Equal(a.EndTick, b.EndTick);
        var decks = Enumerable.Range(1, 64).Select(seed => string.Join(",", episodes.Select(MayhemShowPlan.For((uint)seed).ChannelAt)))
            .Distinct().Count();
        Assert.True(decks > 50, $"only {decks} distinct decks in 64 seeds");
        Assert.Throws<System.ArgumentOutOfRangeException>(() => MayhemShowPlan.For(0));
    }

    [Fact]
    public void EveryActDrawsDistinctRegularsThenTheBossAndNoChannelAirsTwiceInARowAcrossASeam()
    {
        var regulars = MayhemChannels.Regulars.Select(e => e.Id).ToHashSet();
        for (uint seed = 1; seed <= 2000; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            var dealt = Enumerable.Range(0, MayhemShowTuning.Episodes).Where(e => !MayhemShowPlan.IsBoss(e)).Select(plan.ChannelAt).ToArray();
            for (int i = 1; i < dealt.Length; i++) Assert.NotEqual(dealt[i - 1], dealt[i]);
            Assert.Equal(Math.Min(12, MayhemChannels.Regulars.Count), dealt.Distinct().Count());
            for (int act = 0; act < MayhemShowTuning.Acts; act++)
            {
                int first = act * MayhemShowTuning.EpisodesPerAct;
                var drawn = Enumerable.Range(first, MayhemShowTuning.RegularPerAct).Select(plan.ChannelAt).ToArray();
                Assert.All(drawn, id => Assert.Contains(id, regulars));

                Assert.Equal(MayhemChannels.BossByAct[act], plan.ChannelAt(first + MayhemShowTuning.RegularPerAct));
                Assert.True(MayhemShowPlan.IsBoss(first + MayhemShowTuning.RegularPerAct));

            }
        }
    }

    [Fact]
    public void TiersAndPhaseLengthsComeFromTheRegistryEntryAndTheTier()
    {
        var plan = MayhemShowPlan.For(1337);
        uint previousDeadline = 0;
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
        {
            var tier = MayhemShowPlan.TierOf(episode);
            Assert.Equal(MayhemShowPlan.IsBoss(episode) ? SpeedTier.Three : (SpeedTier)MayhemShowPlan.ActOf(episode), tier);
            var timing = plan.AiringAt(episode).Timing;
            Assert.Equal(MayhemSpeed.Call(timing, tier), plan.PlayStart(episode) - plan.CallStart(episode));
            Assert.Equal(MayhemSpeed.Play(timing, tier), plan.Deadline(episode) - plan.PlayStart(episode));
            Assert.True(plan.CallStart(episode) > previousDeadline);
            previousDeadline = plan.Deadline(episode);
        }
        foreach (var entry in MayhemChannels.All)
        {
            Assert.True(MayhemSpeed.Call(entry.Timing, SpeedTier.Three) < MayhemSpeed.Call(entry.Timing, SpeedTier.One));
            Assert.True(MayhemSpeed.Play(entry.Timing, SpeedTier.Three) < MayhemSpeed.Play(entry.Timing, SpeedTier.One));
        }
    }

    [Fact]
    public void TheCursorWalksIntroCallPlayVerdictSpeedUpAndEndedWithAnInclusiveStartAndExclusiveDeadline()
    {
        var plan = MayhemShowPlan.For(77);
        var sequence = new List<(MayhemPhase Phase, int Episode)>();
        for (uint tick = 0; tick <= plan.EndTick + 2; tick++)
        {
            var cursor = plan.At(tick);
            if (sequence.Count == 0 || sequence[^1] != (cursor.Phase, cursor.Episode)) sequence.Add((cursor.Phase, cursor.Episode));
            Assert.True(cursor.PhaseStart <= tick && tick < cursor.PhaseEnd, $"tick {tick} outside its phase {cursor}");
        }
        var expected = new List<(MayhemPhase, int)> { (MayhemPhase.Intro, -1) };
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
        {
            if (episode > 0 && episode % MayhemShowTuning.EpisodesPerAct == 0) expected.Add((MayhemPhase.SpeedUp, -1));
            expected.Add((MayhemPhase.Call, episode));
            expected.Add((MayhemPhase.Play, episode));
            expected.Add((MayhemPhase.Verdict, episode));
        }
        expected.Add((MayhemPhase.Ended, -1));
        Assert.Equal(expected, sequence);
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
        {
            Assert.Equal(MayhemPhase.Call, plan.At(plan.PlayStart(episode) - 1).Phase);
            Assert.Equal(MayhemPhase.Play, plan.At(plan.PlayStart(episode)).Phase);
            Assert.Equal(MayhemPhase.Play, plan.At(plan.Deadline(episode) - 1).Phase);
            Assert.Equal(MayhemPhase.Verdict, plan.At(plan.Deadline(episode)).Phase);
            Assert.Equal(plan.Deadline(episode), plan.At(plan.CallStart(episode)).Deadline);
        }
        Assert.Equal(SpeedTier.Two, plan.At(plan.CallStart(MayhemShowTuning.EpisodesPerAct) - 1).Tier);
        Assert.Equal(MayhemPhase.Ended, plan.At(plan.EndTick).Phase);
    }

    [Fact]
    public void RatingsAreOneFoldOverThePassBits()
    {
        Assert.Equal(0, MayhemShowPlan.Ratings(0));
        Assert.Equal(1, MayhemShowPlan.Ratings(1));
        Assert.Equal(MayhemShowTuning.BossAward, MayhemShowPlan.Ratings(1u << MayhemShowTuning.RegularPerAct));
        Assert.Equal(MayhemShowTuning.Acts * (MayhemShowTuning.RegularPerAct * MayhemShowTuning.RegularAward + MayhemShowTuning.BossAward),
            MayhemShowPlan.Ratings(MayhemShowPlan.AllEpisodesMask));
        Assert.Equal(3, MayhemShowPlan.Passes(MayhemShowPlan.BossMask));
        Assert.Equal(6, MayhemShowPlan.Ratings(MayhemShowPlan.BossMask));
    }
}
