using System;

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
/// regular channels drawn without repeats, each closed by a boss at the top tier. It never depends on the cast,
/// so a withdrawal cannot move a deadline, and the World Snapshot stores only the seed.
/// </summary>
public sealed class MayhemShowPlan
{
    private readonly ChannelId[] _channels = new ChannelId[MayhemShowTuning.Episodes];
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
        if (regulars.Count < MayhemShowTuning.RegularPerAct + 1 || bosses.Count == 0)
            throw new InvalidOperationException("The show needs five regular channels and a boss.");
        ChannelId previous = ChannelId.None;
        for (int act = 0; act < MayhemShowTuning.Acts; act++)
        {
            var deck = new ChannelId[regulars.Count];
            for (int i = 0; i < deck.Length; i++) deck[i] = regulars[i].Id;
            MayhemMath.Shuffle(deck, seed, MayhemMath.DeckStream + (uint)act);
            if (deck[0] == previous) (deck[0], deck[1]) = (deck[1], deck[0]);
            int first = act * MayhemShowTuning.EpisodesPerAct;
            for (int i = 0; i < MayhemShowTuning.RegularPerAct; i++) _channels[first + i] = deck[i];
            previous = deck[MayhemShowTuning.RegularPerAct - 1];
            _channels[first + MayhemShowTuning.RegularPerAct] =
                bosses[(int)(MayhemMath.Sample(seed, MayhemMath.BossStream + (uint)act, 0) % (uint)bosses.Count)].Id;
        }
        uint t = 1u + MayhemShowTuning.IntroTicks;
        for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
        {
            if (episode > 0 && episode % MayhemShowTuning.EpisodesPerAct == 0) t += MayhemShowTuning.SpeedUpTicks;
            var timing = MayhemChannels.Find(_channels[episode]).Timing;
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

    public ChannelId ChannelAt(int episode) => _channels[episode];
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
