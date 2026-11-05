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
    internal ChannelEntry(ChannelId id, string copyKey, ExtraVerb extra, ChannelRole role, ChannelTiming timing,
        Func<ChannelSetup, IMayhemChannel> create, Func<SnapshotReader, ChannelRestore, IMayhemChannel> read)
    {
        Id = id; CopyKey = copyKey; Extra = extra; Role = role; Timing = timing; Create = create; Read = read;
    }

    public ChannelId Id { get; }
    /// <summary>Localisation key for the call card and HUD prompt.</summary>
    public string CopyKey { get; }
    public ExtraVerb Extra { get; }
    public ChannelRole Role { get; }
    public ChannelTiming Timing { get; }
    internal Func<ChannelSetup, IMayhemChannel> Create { get; }
    internal Func<SnapshotReader, ChannelRestore, IMayhemChannel> Read { get; }
}

/// <summary>The only list of channels. IDs are never reused.</summary>
public static class MayhemChannels
{
    public static readonly ChannelId Dodge = new(1), Jump = new(2), BreakOut = new(3), Spot = new(4),
        NoRunning = new(5), BreakBoards = new(6), PrimeTime = new(7);

    public static readonly IReadOnlyList<ChannelEntry> All = Array.AsReadOnly(new[]
    {
        new ChannelEntry(Dodge, "mayhem.channel.dodge", ExtraVerb.None, ChannelRole.Regular, new(90, 300),
            DodgeChannel.Start, DodgeChannel.Read),
        new ChannelEntry(Jump, "mayhem.channel.jump", ExtraVerb.None, ChannelRole.Regular, new(90, 300),
            JumpChannel.Start, JumpChannel.Read),
        new ChannelEntry(BreakOut, "mayhem.channel.break-out", ExtraVerb.None, ChannelRole.Regular, new(90, 300),
            BreakOutChannel.Start, BreakOutChannel.Read),
        new ChannelEntry(Spot, "mayhem.channel.spot", ExtraVerb.None, ChannelRole.Regular, new(90, 240),
            SpotChannel.Start, SpotChannel.Read),
        new ChannelEntry(NoRunning, "mayhem.channel.no-running", ExtraVerb.None, ChannelRole.Regular, new(90, 330),
            NoRunningChannel.Start, NoRunningChannel.Read),
        new ChannelEntry(BreakBoards, "mayhem.channel.break-boards", ExtraVerb.None, ChannelRole.Regular, new(90, 240),
            BreakBoardsChannel.Start, BreakBoardsChannel.Read),
        new ChannelEntry(PrimeTime, "mayhem.channel.prime-time", ExtraVerb.None, ChannelRole.Boss, new(90, 360),
            PrimeTimeChannel.Start, PrimeTimeChannel.Read),
    });

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
