using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

/// <summary>
/// One channel's rules: it ticks at the two engine moments, answers contacts, judges one contestant, gives CPU
/// intent, projects its cues and writes its own state. Channels are trusted Shared code, never content.
/// </summary>
internal interface IMayhemChannel
{
    void Tick(in ChannelTick tick, MayhemHazards hazards);
    ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards);
    MayhemJudgement Judge(in ContestantProbe probe);
    MayhemCpuIntent Cpu(in CpuProbe probe);
    ChannelScene Observe();
    void WriteSnapshot(SnapshotWriter writer);
}

/// <summary>Base call and play lengths at tier one; <see cref="MayhemSpeed"/> scales them per tier.</summary>
public readonly record struct ChannelTiming(ushort CallTicks, ushort PlayTicks);

/// <summary>A registry row: decks, prompts and codec lookup all derive from these.</summary>
public sealed class ChannelEntry
{
    internal ChannelEntry(ChannelId id, string copyKey, ExtraVerb extra, ChannelRole role, ChannelTiming timing, IReadOnlyList<TwistEntry> twists, ChannelContest contest,
        Func<ChannelSetup, IMayhemChannel> create, Func<SnapshotReader, ChannelRestore, IMayhemChannel> read)
    {
        Id = id; CopyKey = copyKey; Extra = extra; Role = role; Timing = timing; Twists = twists; Contest = contest; Create = create; Read = read;
    }

    public ChannelId Id { get; }
    /// <summary>Localisation key for the call card and HUD prompt.</summary>
    public string CopyKey { get; }
    public ExtraVerb Extra { get; }
    public ChannelRole Role { get; }
    public ChannelTiming Timing { get; }
    public IReadOnlyList<TwistEntry> Twists { get; }
    public ChannelContest Contest { get; }
    public ChannelContest ContestFor(TwistId id) => Id == MayhemChannels.QuickDraw && id.Value == (byte)QuickDrawTwist.Duel ? ChannelContest.Scarce : Contest;
    public TwistEntry Twist(TwistId id) => Twists.FirstOrDefault(t => t.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id));
    /// <summary>The twist's override, else the channel's own.</summary>
    public ExtraVerb ExtraFor(TwistId id) => Twist(id).Extra ?? Extra;
    public ChannelTiming TimingFor(TwistId id) => Twist(id).Timing ?? Timing;
    internal Func<ChannelSetup, IMayhemChannel> Create { get; }
    internal Func<SnapshotReader, ChannelRestore, IMayhemChannel> Read { get; }
}

/// <summary>The only list of channels. IDs are never reused.</summary>
public static class MayhemChannels
{
    public static readonly ChannelId Dodge = new(1), Jump = new(2), BreakOut = new(3), Spot = new(4),
        NoRunning = new(5), BreakBoards = new(6), PrimeTime = new(7),
        Bonk = new(8), Smile = new(9), Glitch = new(10), Sumo = new(14), QuickDraw = new(11), HotPotato = new(12), Block = new(13), GetHim = new(15);

    public static readonly IReadOnlyList<ChannelEntry> All = Array.AsReadOnly(new[]
    {
        new ChannelEntry(Dodge, "mayhem.channel.dodge", ExtraVerb.None, ChannelRole.Regular, new(90, 300),
            TwistRows.For<DodgeTwist>("dodge", new TwistEntry(new(1), "mayhem.twist.dodge.aimed"),
                new TwistEntry(new(2), "mayhem.twist.dodge.bomb-rain"),
                new TwistEntry(new(3), "mayhem.twist.dodge.big-one")), ChannelContest.Shared,
            DodgeChannel.Start, DodgeChannel.Read),
        new ChannelEntry(Jump, "mayhem.channel.jump", ExtraVerb.None, ChannelRole.Regular, new(90, 300),
            TwistRows.For<JumpTwist>("jump", new TwistEntry(new(1), "mayhem.twist.jump.rings"),
                new TwistEntry(new(2), "mayhem.twist.jump.crossfire"),
                new TwistEntry(new(3), "mayhem.twist.jump.tall-wave", JumpTuning.TallTiming)), ChannelContest.Shared,
            JumpChannel.Start, JumpChannel.Read),
        new ChannelEntry(BreakOut, "mayhem.channel.break-out", ExtraVerb.None, ChannelRole.Regular, new(90, 300),
            TwistRows.For<BreakOutTwist>("break-out", new TwistEntry(new(1), "mayhem.twist.break-out.cracked-wall"),
                new TwistEntry(new(2), "mayhem.twist.break-out.any-wall"),
                new TwistEntry(new(3), "mayhem.twist.break-out.double-cage", BreakOutTuning.DoubleCageTiming)), ChannelContest.Shared,
            BreakOutChannel.Start, BreakOutChannel.Read),
        new ChannelEntry(Spot, "mayhem.channel.spot", ExtraVerb.None, ChannelRole.Regular, new(90, 240),
            TwistRows.For<SpotTwist>("spot", new TwistEntry(new(1), "mayhem.twist.spot.still"),
                new TwistEntry(new(2), "mayhem.twist.spot.drifting"),
                new TwistEntry(new(3), "mayhem.twist.spot.musical")), ChannelContest.Scarce,
            SpotChannel.Start, SpotChannel.Read),
        new ChannelEntry(NoRunning, "mayhem.channel.no-running", ExtraVerb.None, ChannelRole.Regular, new(90, 330),
            TwistRows.For<NoRunningTwist>("no-running", new TwistEntry(new(1), "mayhem.twist.no-running.looks"),
                new TwistEntry(new(2), "mayhem.twist.no-running.fake-out"),
                new TwistEntry(new(3), "mayhem.twist.no-running.rapid")), ChannelContest.Shared,
            NoRunningChannel.Start, NoRunningChannel.Read),
        new ChannelEntry(BreakBoards, "mayhem.channel.break-boards", ExtraVerb.None, ChannelRole.Regular, new(90, 240),
            TwistRows.For<BreakBoardsTwist>("break-boards", new TwistEntry(new(1), "mayhem.twist.break-boards.your-stack"),
                new TwistEntry(new(2), "mayhem.twist.break-boards.scattered", BreakBoardsTuning.ScatteredTiming)), ChannelContest.Shared,
            BreakBoardsChannel.Start, BreakBoardsChannel.Read),
        new ChannelEntry(Bonk, "mayhem.channel.bonk", ExtraVerb.None, ChannelRole.Regular, BonkTuning.Timing,
            TwistRows.For<BonkTwist>("bonk", new TwistEntry(new(1), "mayhem.twist.bonk.whack"),
                new TwistEntry(new(2), "mayhem.twist.bonk.decoys")), ChannelContest.Scarce,
            BonkChannel.Start, BonkChannel.Read),
        new ChannelEntry(Smile, "mayhem.channel.smile", ExtraVerb.None, ChannelRole.Regular, SmileTuning.Timing,
            TwistRows.For<SmileTwist>("smile", new TwistEntry(new(1), "mayhem.twist.smile.squeeze"),
                new TwistEntry(new(2), "mayhem.twist.smile.pan")), ChannelContest.Shared,
            SmileChannel.Start, SmileChannel.Read),
        new ChannelEntry(Glitch, "mayhem.channel.glitch", ExtraVerb.None, ChannelRole.Regular, GlitchTuning.Timing,
            TwistRows.For<GlitchTwist>("glitch", new TwistEntry(new(1), "mayhem.twist.glitch.scatter"),
                new TwistEntry(new(2), "mayhem.twist.glitch.sweep"),
                new TwistEntry(new(3), "mayhem.twist.glitch.checker")), ChannelContest.Shared,
            GlitchChannel.Start, GlitchChannel.Read),
        new ChannelEntry(Sumo, "mayhem.channel.sumo", ExtraVerb.None, ChannelRole.Regular, SumoTuning.Timing,
            TwistRows.For<SumoTwist>("sumo", new TwistEntry(new(1), "mayhem.twist.sumo.shrink"),
                new TwistEntry(new(2), "mayhem.twist.sumo.drift")), ChannelContest.Scarce,
            SumoChannel.Start, SumoChannel.Read),
        new ChannelEntry(QuickDraw, "mayhem.channel.quick-draw", ExtraVerb.None, ChannelRole.Regular, QuickDrawTuning.Timing,
            TwistRows.For<QuickDrawTwist>("quick-draw", new TwistEntry(new(1), "mayhem.twist.quick-draw.showdown"),
                new TwistEntry(new(2), "mayhem.twist.quick-draw.fake-out"),
                new TwistEntry(new(3), "mayhem.twist.quick-draw.duel")), ChannelContest.Shared,
            QuickDrawChannel.Start, QuickDrawChannel.Read),
        new ChannelEntry(HotPotato, "mayhem.channel.hot-potato", ExtraVerb.None, ChannelRole.Regular, HotPotatoTuning.Timing,
            TwistRows.For<HotPotatoTwist>("hot-potato", new TwistEntry(new(1), "mayhem.twist.hot-potato.one-potato"),
                new TwistEntry(new(2), "mayhem.twist.hot-potato.two-potatoes")), ChannelContest.Scarce,
            HotPotatoChannel.Start, HotPotatoChannel.Read),
        new ChannelEntry(Block, "mayhem.channel.block", ExtraVerb.Shield, ChannelRole.Regular, BlockTuning.Timing,
            TwistRows.For<BlockTwist>("block", new TwistEntry(new(1), "mayhem.twist.block.barrage"),
                new TwistEntry(new(2), "mayhem.twist.block.waves"),
                new TwistEntry(new(3), "mayhem.twist.block.block-or-jump")), ChannelContest.Shared,
            BlockChannel.Start, BlockChannel.Read),
        new ChannelEntry(GetHim, "mayhem.channel.get-him", ExtraVerb.None, ChannelRole.Boss, GetHimTuning.Timing,
            TwistRows.For<GetHimTwist>("get-him", new TwistEntry(new(1), "mayhem.twist.get-him.slam"),
                new TwistEntry(new(2), "mayhem.twist.get-him.tantrum")), ChannelContest.Shared,
            GetHimChannel.Start, GetHimChannel.Read),
        new ChannelEntry(PrimeTime, "mayhem.channel.prime-time", ExtraVerb.None, ChannelRole.Boss, new(90, 360),
            Array.Empty<TwistEntry>(), ChannelContest.Shared, PrimeTimeChannel.Start, PrimeTimeChannel.Read),
    });

    public static IReadOnlyList<ChannelId> BossByAct { get; } = Array.AsReadOnly(new[] { PrimeTime, PrimeTime, GetHim });

    public static IReadOnlyList<ChannelEntry> Regulars { get; } = All.Where(e => e.Role == ChannelRole.Regular).ToArray();
    public static IReadOnlyList<ChannelEntry> Bosses { get; } = All.Where(e => e.Role == ChannelRole.Boss).ToArray();

    public static bool TryFind(ChannelId id, out ChannelEntry entry)
    {
        foreach (var candidate in All)
            if (candidate.Id == id) { entry = candidate; return true; }
        entry = null!;
        return false;
    }

    public static ChannelEntry Find(ChannelId id)
        => TryFind(id, out var entry) ? entry : throw new ArgumentOutOfRangeException(nameof(id), $"Unknown Mayhem channel {id}.");
}
