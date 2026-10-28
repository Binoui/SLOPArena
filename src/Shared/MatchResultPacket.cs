using System;
using System.Linq;
using System.Buffers.Binary;

namespace SlopArena.Shared;

/// <summary>
/// One player's authoritative final match statistics. Names and character
/// presentation are resolved by the client from the locked match roster.
/// </summary>
public readonly struct MatchResultEntry
{
    public MatchResultEntry(ulong entityId, byte placement, byte kos, byte falls)
    {
        EntityId = entityId;
        Placement = placement;
        KOs = kos;
        Falls = falls;
    }

    public ulong EntityId { get; }
    public byte Placement { get; }
    public byte KOs { get; }
    public byte Falls { get; }
}

/// <summary>A finished co-op Slopball run's team result (ADR-0040).</summary>
public readonly record struct SlopballRunResult(uint TotalReturns, uint BestCombo, byte Misses);
public readonly record struct SlopballVersusResult(ushort TealScore, ushort ClayScore, SlopballCamp Winner);

/// <summary>
/// Final match snapshot emitted by the authoritative game server. It is sent
/// repeatedly during the post-match window because the transport is UDP.
/// A co-op Slopball run appends its team result after the entries.
/// </summary>
public sealed class MatchResultPacket
{
    private const uint Magic = 0x544C5352; // little-endian bytes: RSLT
    private const int HeaderSize = 10;
    private const int EntrySize = 11;
    private const byte RunMarker = 1;
    private const int RunSize = 10;
    private const byte VersusMarker = 2;
    private const int VersusSize = 6;
    private const byte MayhemMarker = 4;
    private const int MayhemHeaderSize = 2;
    private const int MayhemPlayerSize = 15;
    private const byte CourseMarker = 3;
    private const byte ScrambleMarker = 5;
    private const int ScramblePlayerSize = 24;
    private const int CourseHeaderSize = 12;
    private const int CoursePlayerSize = 22;
    public const int MaxPlayers = 4;
    public const int MaxSize = HeaderSize + EntrySize * MaxPlayers + CourseHeaderSize + ScramblePlayerSize * MaxPlayers;

    public MatchResultPacket(uint durationTicks, bool sharedVictory, MatchResultEntry[] entries,
        SlopballRunResult? run = null, SlopballVersusResult? versus = null, CourseRunResult? course = null, MayhemRunResult? mayhem = null)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        if (entries.Length is < 1 or > MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(entries));

        DurationTicks = durationTicks;
        SharedVictory = sharedVictory;
        Entries = entries;
        Run = run;
        if (run.HasValue && versus.HasValue) throw new ArgumentException("A result has one Slopball format.");
        Versus = versus;
        if (course != null && (run.HasValue || versus.HasValue || !ValidCourse(course, entries)))
            throw new ArgumentException("Invalid course result.", nameof(course));
        Course = course;
        if (mayhem != null && (course != null || run.HasValue || versus.HasValue || !ValidMayhem(mayhem, entries)))
            throw new ArgumentException("Invalid Mayhem result.", nameof(mayhem));
        Mayhem = mayhem;
    }

    public uint DurationTicks { get; }
    public bool SharedVictory { get; }
    public MatchResultEntry[] Entries { get; }
    /// <summary>The team result of a co-op Slopball run; null for a stock fight.</summary>
    public SlopballRunResult? Run { get; }
    public SlopballVersusResult? Versus { get; }
    public CourseRunResult? Course { get; }
    public MayhemRunResult? Mayhem { get; }
    public int WireSize => HeaderSize + EntrySize * Entries.Length + (Run.HasValue ? RunSize : Versus.HasValue ? VersusSize : Course != null ? CourseHeaderSize + Course.Players.Count * (Course.Format == CourseFormat.Scramble ? ScramblePlayerSize : CoursePlayerSize) : Mayhem != null ? MayhemHeaderSize + Mayhem.Players.Count * MayhemPlayerSize : 0);

    private static bool ValidCourse(CourseRunResult course, MatchResultEntry[] entries)
        => course.Format <= CourseFormat.Scramble && course.Players != null && course.Players.Count == entries.Length
            && course.Players.Select(x => x.EntityId).Distinct().Count() == entries.Length
            && course.Players.All(x => Array.Exists(entries, e => e.EntityId == x.EntityId && e.Placement == course.PlaceFor(x.EntityId))
                && x.TotalTargets is > 0 and <= CharacterCourseDefinition.MaxTargets && x.BrokenTargets <= x.TotalTargets
                && (course.Format == CourseFormat.Scramble ? x.Points <= x.TotalTargets * CourseScrambleTuning.BonusTargetPoints + CourseScrambleTuning.FirstFinishPoints : x.Points == 0)
                && x.Place <= entries.Length && (x.Place == 0) == (x.FinishTicks == 0)
                && x.FinishTicks <= course.ElapsedTicks && (x.Place == 0 || x.BrokenTargets == x.TotalTargets))
            && course.Completed == course.Players.Any(x => x.Place != 0);

    private static bool ValidMayhem(MayhemRunResult result, MatchResultEntry[] entries)
        => result.Players != null && result.Players.Count == entries.Length
            && result.Players.Select(p => p.EntityId).Distinct().Count() == entries.Length
            && result.Players.All(p => p.EntityId is > 0 and < ServerSimulation.MayhemHostEntityId
                && p.RoundsSurvived <= MankiMayhemTuning.DeckPasses * MankiMayhemRounds.DeckSize
                && p.Bonuses <= p.RoundsSurvived * MankiMayhemTuning.LastStandingBonus
                && p.Score == p.RoundsSurvived * MankiMayhemTuning.SurvivalPoints + p.Bonuses
                && Array.Exists(entries, e => e.EntityId == p.EntityId && e.Placement == result.PlaceFor(p.EntityId)));

    public void Serialize(Span<byte> buffer)
    {
        if (buffer.Length < WireSize)
            throw new ArgumentException("Buffer too small", nameof(buffer));

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(0, 4), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(4, 4), DurationTicks);
        buffer[8] = SharedVictory ? (byte)1 : (byte)0;
        buffer[9] = (byte)Entries.Length;

        int offset = HeaderSize;
        foreach (var entry in Entries)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), entry.EntityId);
            buffer[offset + 8] = entry.Placement;
            buffer[offset + 9] = entry.KOs;
            buffer[offset + 10] = entry.Falls;
            offset += EntrySize;
        }
        if (Run is { } run)
        {
            buffer[offset] = RunMarker;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset + 1, 4), run.TotalReturns);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(offset + 5, 4), run.BestCombo);
            buffer[offset + 9] = run.Misses;
        }
        else if (Versus is { } versus)
        {
            buffer[offset] = VersusMarker;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset + 1, 2), versus.TealScore);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset + 3, 2), versus.ClayScore);
            buffer[offset + 5] = (byte)versus.Winner;
        }
        else if (Mayhem is { } mayhem)
        {
            buffer[offset++] = MayhemMarker; buffer[offset++] = (byte)mayhem.Players.Count;
            foreach (var player in mayhem.Players)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), player.EntityId); offset += 8;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, 2), player.Score); offset += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, 2), player.RoundsSurvived); offset += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, 2), player.Bonuses); offset += 2;
                buffer[offset++] = player.Withdrawn ? (byte)1 : (byte)0;
            }
        }
        else if (Course is { } course)
        {
            buffer[offset++] = course.Format == CourseFormat.Scramble ? ScrambleMarker : CourseMarker; buffer[offset++] = (byte)course.Format;
            buffer[offset++] = course.Completed ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), course.ElapsedTicks); offset += 8;
            buffer[offset++] = (byte)course.Players.Count;
            foreach (var player in course.Players)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), player.EntityId); offset += 8;
                BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(offset, 8), player.FinishTicks); offset += 8;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, 2), player.BrokenTargets); offset += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, 2), player.TotalTargets); offset += 2;
                buffer[offset++] = player.Place; buffer[offset++] = player.Withdrawn ? (byte)1 : (byte)0;
                if (course.Format == CourseFormat.Scramble) { BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset, 2), player.Points); offset += 2; }
            }
        }
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> buffer, out MatchResultPacket? packet)
    {
        packet = null;
        if (buffer.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4)) != Magic)
            return false;

        int count = buffer[9];
        if (count is < 1 or > MaxPlayers || buffer.Length < HeaderSize + EntrySize * count)
            return false;

        var entries = new MatchResultEntry[count];
        int offset = HeaderSize;
        for (int i = 0; i < count; i++)
        {
            entries[i] = new MatchResultEntry(
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8)),
                buffer[offset + 8],
                buffer[offset + 9],
                buffer[offset + 10]);
            offset += EntrySize;
        }

        SlopballRunResult? run = null;
        SlopballVersusResult? versus = null;
        CourseRunResult? course = null;
        MayhemRunResult? mayhem = null;
        if (buffer.Length > offset && buffer[offset] == MayhemMarker)
        {
            if (buffer.Length < offset + MayhemHeaderSize || buffer[offset + 1] != count
                || buffer.Length != offset + MayhemHeaderSize + count * MayhemPlayerSize) return false;
            offset += MayhemHeaderSize;
            var players = new MayhemFinishResult[count];
            for (int i = 0; i < count; i++)
            {
                if (buffer[offset + 14] > 1) return false;
                players[i] = new(BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8)),
                    BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 8, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 10, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 12, 2)), buffer[offset + 14] == 1);
                offset += MayhemPlayerSize;
            }
            mayhem = new(Array.AsReadOnly(players));
            if (!ValidMayhem(mayhem, entries)) return false;
        }
        else if (buffer.Length > offset && buffer[offset] is CourseMarker or ScrambleMarker)
        {
            bool scramble = buffer[offset] == ScrambleMarker;
            int playerSize = scramble ? ScramblePlayerSize : CoursePlayerSize;
            if (buffer.Length < offset + CourseHeaderSize || (scramble ? buffer[offset + 1] != 2 : buffer[offset + 1] > 1) || buffer[offset + 2] > 1
                || buffer[offset + 11] != count || buffer.Length != offset + CourseHeaderSize + count * playerSize) return false;
            var format = (CourseFormat)buffer[offset + 1]; bool completed = buffer[offset + 2] == 1;
            ulong elapsed = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset + 3, 8));
            offset += CourseHeaderSize;
            var players = new CourseFinishResult[count];
            for (int i = 0; i < count; i++)
            {
                ulong id = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8));
                ulong time = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset + 8, 8));
                ushort broken = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 16, 2));
                ushort total = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 18, 2));
                if (buffer[offset + 21] > 1) return false;
                players[i] = new(id, time, broken, total, buffer[offset + 20], buffer[offset + 21] == 1,
                    scramble ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 22, 2)) : (ushort)0);
                offset += playerSize;
            }
            course = new(format, completed, elapsed, Array.AsReadOnly(players));
            if (!ValidCourse(course, entries)) return false;
        }
        else if (buffer.Length > offset && buffer[offset] == VersusMarker)
        {
            if (buffer.Length < offset + VersusSize || buffer[offset + 5] is < 1 or > 2) return false;
            ushort teal = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 1, 2));
            ushort clay = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 3, 2));
            var winner = (SlopballCamp)buffer[offset + 5];
            if (winner == SlopballCamp.Teal ? teal != SlopballVersusMatchRule.ScoreToWin || clay >= teal
                : clay != SlopballVersusMatchRule.ScoreToWin || teal >= clay) return false;
            versus = new SlopballVersusResult(teal, clay, winner);
        }
        else if (buffer.Length >= offset + RunSize && buffer[offset] == RunMarker)
            run = new SlopballRunResult(
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset + 1, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset + 5, 4)),
                buffer[offset + 9]);

        packet = new MatchResultPacket(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4)),
            buffer[8] != 0,
            entries,
            run, versus, course, mayhem);
        return true;
    }
}
