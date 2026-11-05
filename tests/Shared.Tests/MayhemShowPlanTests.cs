using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemShowPlanTests
{
    [Fact]
    public void ThePlanIsAFunctionOfTheSeed()
    {
        var a = MayhemShowPlan.For(41);
        var b = MayhemShowPlan.For(41);
        var episodes = Enumerable.Range(0, MayhemShowTuning.Episodes).ToArray();
        Assert.Equal(episodes.Select(a.ChannelAt), episodes.Select(b.ChannelAt));
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
            for (int act = 0; act < MayhemShowTuning.Acts; act++)
            {
                int first = act * MayhemShowTuning.EpisodesPerAct;
                var drawn = Enumerable.Range(first, MayhemShowTuning.RegularPerAct).Select(plan.ChannelAt).ToArray();
                Assert.All(drawn, id => Assert.Contains(id, regulars));
                Assert.Equal(drawn.Length, drawn.Distinct().Count());
                Assert.Equal(MayhemChannels.PrimeTime, plan.ChannelAt(first + MayhemShowTuning.RegularPerAct));
                Assert.True(MayhemShowPlan.IsBoss(first + MayhemShowTuning.RegularPerAct));
                if (act > 0) Assert.NotEqual(plan.ChannelAt(first - 2), plan.ChannelAt(first));
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
            var timing = MayhemChannels.Find(plan.ChannelAt(episode)).Timing;
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
