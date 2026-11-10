using System;
using System.Linq;
using System.Collections.Generic;

namespace SlopArena.Shared.Mayhem;

/// <summary>Show tuning that is not per channel. Every value is provisional.</summary>
public static class MayhemShowTuning
{
    public const int Acts = 3, RegularPerAct = 4, EpisodesPerAct = RegularPerAct + 1, Episodes = Acts * EpisodesPerAct;
    public const ushort IntroTicks = 120, VerdictTicks = 75, SpeedUpTicks = 120;
    public const ushort RegularAward = 1, BossAward = 2;
    /// <summary>Spawned hazards and zones keep this far inside the deck edge.</summary>
    public const float PlacementMargin = 1f;
    /// <summary>A hip this far below the deck top has fallen off.</summary>
    public const float FallDepth = .5f;
}

/// <summary>Per-tier scaling of call and play lengths: each act is faster than the last.</summary>
public static class MayhemSpeed
{
    private static readonly ushort[] CallPercent = { 100, 85, 70 };
    private static readonly ushort[] PlayPercent = { 100, 88, 76 };

    public static ushort Call(ChannelTiming timing, SpeedTier tier) => Scale(timing.CallTicks, CallPercent[(int)tier]);
    public static ushort Play(ChannelTiming timing, SpeedTier tier) => Scale(timing.PlayTicks, PlayPercent[(int)tier]);
    private static ushort Scale(ushort ticks, ushort percent) => (ushort)Math.Max(1, ticks * percent / 100);
}

/// <summary>
/// The show for one seed: which channel airs in each episode and when each phase starts. Three acts of four
/// regular slots dealt from one deck, each act closed by its boss at the top tier. Acts one and two close with two
/// different PRIME TIME! pairs from one shuffled pair deck; GET HIM! closes act three. Each channel cycles
/// through its twists once per show, counting solo airings and remix children alike, so it shows every twist before
/// it repeats one, and no remix reruns an earlier remix's pair and both twists. It never depends on the cast, so a
/// withdrawal cannot move a deadline, and the World Snapshot stores only the seed.
/// </summary>
public sealed class MayhemShowPlan
{
    private readonly MayhemAiring[] _airings = new MayhemAiring[MayhemShowTuning.Episodes];
    private readonly uint[] _callStart = new uint[MayhemShowTuning.Episodes];
    private readonly ushort[] _callTicks = new ushort[MayhemShowTuning.Episodes];
    private readonly ushort[] _playTicks = new ushort[MayhemShowTuning.Episodes];

    public uint Seed { get; }
    /// <summary>The first tick of Ended.</summary>
    public uint EndTick { get; }

    private MayhemShowPlan(uint seed)
    {
        Seed = seed;
        var regulars = MayhemChannels.Regulars;
        var bosses = MayhemChannels.Bosses;
        if (regulars.Count < MayhemShowTuning.RegularPerAct + 1 || bosses.Count == 0 || PrimePairs.All.Count < 2)
            throw new InvalidOperationException("The show needs five regular channels, a boss and two PRIME TIME! pairs.");
        var dealt = Deal(seed, regulars.Select(e => e.Id).ToArray());
        var pairs = PrimePairs.All.Select(p => p.Id).OrderBy(id => id.Value).ToArray();
        MayhemMath.Shuffle(pairs, seed, MayhemMath.PairStream);
        var airings = new Dictionary<ChannelId, int>();
        var remixes = new HashSet<(PrimePairId, TwistId, TwistId)>();
        ChannelDraw Next(ChannelId channel, int episode, byte lane)
        {
            var twists = MayhemChannels.Find(channel).Twists;
            airings.TryGetValue(channel, out int airing);
            airings[channel] = airing + 1;
            int basis = (int)(MayhemMath.Sample(seed, MayhemMath.TwistStream, channel.Value) % (uint)twists.Count);
            return new(channel, twists[(basis + airing) % twists.Count].Id, MayhemMath.ChannelSeed(seed, episode, lane));
        }
        for (int episode = 0, slot = 0; episode < MayhemShowTuning.Episodes; episode++)
        {
            var channel = IsBoss(episode) ? MayhemChannels.BossByAct[ActOf(episode)] : dealt[slot++];
            if (channel != MayhemChannels.PrimeTime)
            {
                _airings[episode] = new SoloAiring(Next(channel, episode, 0));
                continue;
            }
            int act = ActOf(episode);
            var pair = PrimePairs.Find(act < MayhemShowTuning.Acts - 1 ? pairs[act]
                : pairs[(int)(MayhemMath.Sample(seed, MayhemMath.PairStream + 1, (uint)act) % (uint)pairs.Length)]);
            var goal = Next(pair.Goal, episode, 0);
            var hazard = Next(pair.Hazard, episode, 1);
            // A rerun repeats both children's twists, which their cycles allow only once they have shown every twist.
            if (!remixes.Add((pair.Id, goal.Twist, hazard.Twist)))
            {
                hazard = Next(pair.Hazard, episode, 1);
                remixes.Add((pair.Id, goal.Twist, hazard.Twist));
            }
            _airings[episode] = new RemixAiring(pair.Id, goal, hazard);
        }
        uint t = 1u + MayhemShowTuning.IntroTicks;
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
        {
            if (episode > 0 && episode % MayhemShowTuning.EpisodesPerAct == 0) t += MayhemShowTuning.SpeedUpTicks;
            var timing = _airings[episode].Timing;
            var tier = TierOf(episode);
            _callStart[episode] = t;
            _callTicks[episode] = MayhemSpeed.Call(timing, tier);
            _playTicks[episode] = MayhemSpeed.Play(timing, tier);
            t += (uint)_callTicks[episode] + _playTicks[episode] + MayhemShowTuning.VerdictTicks;
        }
        EndTick = t;
    }

    /// <summary>The only constructor. The seed is nonzero.</summary>
    public static MayhemShowPlan For(uint seed)
        => seed == 0 ? throw new ArgumentOutOfRangeException(nameof(seed)) : new MayhemShowPlan(seed);

    public MayhemAiring AiringAt(int episode) => _airings[episode];
    public ChannelId ChannelAt(int episode) => _airings[episode].Channel;

    internal static ChannelId[] Deal(uint seed, ChannelId[] regulars)
    {
        if (regulars.Length < 2) throw new ArgumentException("A deal needs at least two regulars.");
        var result = new ChannelId[MayhemShowTuning.Acts * MayhemShowTuning.RegularPerAct];
        var deck = regulars.OrderBy(id => id.Value).ToArray();
        int at = deck.Length;
        uint round = 0;
        for (int i = 0; i < result.Length; i++)
        {
            if (at == deck.Length)
            {
                MayhemMath.Shuffle(deck, seed, MayhemMath.DeckStream + round++);
                if (i > 0 && deck[0] == result[i - 1]) (deck[0], deck[1]) = (deck[1], deck[0]);
                at = 0;
            }
            result[i] = deck[at++];
        }
        return result;
    }
    public static byte ActOf(int episode) => (byte)(episode / MayhemShowTuning.EpisodesPerAct);
    public static bool IsBoss(int episode) => episode % MayhemShowTuning.EpisodesPerAct == MayhemShowTuning.RegularPerAct;
    public static SpeedTier TierOf(int episode) => IsBoss(episode) ? SpeedTier.Three : (SpeedTier)ActOf(episode);
    public uint CallStart(int episode) => _callStart[episode];
    public uint PlayStart(int episode) => _callStart[episode] + _callTicks[episode];
    public uint Deadline(int episode) => PlayStart(episode) + _playTicks[episode];

    /// <summary>The cursor at <paramref name="tick"/>. Tick 1 is the first simulated tick after Begin.</summary>
    public MayhemCursor At(uint tick)
    {
        uint introEnd = 1u + MayhemShowTuning.IntroTicks;
        if (tick < introEnd) return new(MayhemPhase.Intro, -1, 0, SpeedTier.One, 0, introEnd, 0, 0);
        if (tick >= EndTick)
            return new(MayhemPhase.Ended, -1, MayhemShowTuning.Acts - 1, SpeedTier.Three, EndTick, uint.MaxValue, 0, 0);
        int episode = 0;
        while (episode + 1 < MayhemShowTuning.Episodes && _callStart[episode + 1] <= tick) episode++;
        uint call = _callStart[episode], play = PlayStart(episode), deadline = Deadline(episode);
        uint verdictEnd = deadline + MayhemShowTuning.VerdictTicks;
        byte act = ActOf(episode);
        var tier = TierOf(episode);
        if (tick < play) return new(MayhemPhase.Call, episode, act, tier, call, play, play, deadline);
        if (tick < deadline) return new(MayhemPhase.Play, episode, act, tier, play, deadline, play, deadline);
        if (tick < verdictEnd) return new(MayhemPhase.Verdict, episode, act, tier, deadline, verdictEnd, play, deadline);
        int next = episode + 1;
        return new(MayhemPhase.SpeedUp, -1, ActOf(next), TierOf(next), verdictEnd, _callStart[next], 0, 0);
    }

    /// <summary>Ratings one pass in <paramref name="episode"/> earns.</summary>
    public static ushort Award(int episode) => IsBoss(episode) ? MayhemShowTuning.BossAward : MayhemShowTuning.RegularAward;

    /// <summary>The only ratings formula: a fold over one contestant's pass bits.</summary>
    public static ushort Ratings(uint passMask)
    {
        int total = 0;
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
            if ((passMask >> episode & 1) != 0) total += Award(episode);
        return (ushort)total;
    }

    public static uint BossMask
    {
        get
        {
            uint mask = 0;
            for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++) if (IsBoss(episode)) mask |= 1u << episode;
            return mask;
        }
    }

    public const uint AllEpisodesMask = (1u << MayhemShowTuning.Episodes) - 1;
    public static int Passes(uint passMask)
    {
        int count = 0;
        for (; passMask != 0; passMask &= passMask - 1) count++;
        return count;
    }
}
