using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopArena.Shared;

/// <summary>Steam transport envelope; gameplay payloads after the kind byte retain their existing Shared codecs.</summary>
public static class SteamGameplayWire
{
    public const byte Join = 1;
    public const byte Ack = 2;
    public const byte Deny = 3;
    public const byte Input = 0x10;
    public const byte State = 0x11; // Retired per-entity packet kind; reserved.
    public const byte Event = 0x12; // Retired standalone event kind; reserved.
    public const byte Result = 0x13;
    public const byte Projectile = 0x14; // Retired visual packet kind; reserved.
    public const byte SwordTrail = 0x15; // Retired trail packet kind; reserved.
    public const byte Control = 0x16;
    /// <summary>One <see cref="WorldFramePacket"/> per GameHost tick (protocol 10, ADR-0039).</summary>
    public const byte Frame = 0x17;
    public const int JoinSize = 1 + 36 + 2 + 64;
    public const int AckSize = 1 + 8;

    public static byte[] CreateJoin(SteamMatchDescriptor descriptor)
    {
        if (descriptor is null || descriptor.MatchId == Guid.Empty ||
            descriptor.ProtocolVersion != SteamMatchDescriptor.CurrentProtocolVersion ||
            !IsLowerHash(descriptor.ContentHash))
            throw new ArgumentException("Invalid Steam match descriptor.", nameof(descriptor));
        var frame = new byte[JoinSize];
        frame[0] = Join;
        Encoding.ASCII.GetBytes(descriptor.MatchId.ToString("D"), frame.AsSpan(1, 36));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(37, 2), (ushort)descriptor.ProtocolVersion);
        Encoding.ASCII.GetBytes(descriptor.ContentHash, frame.AsSpan(39, 64));
        return frame;
    }

    public static bool TryParseJoin(ReadOnlySpan<byte> frame, out Guid matchId, out string contentHash)
    {
        matchId = Guid.Empty;
        contentHash = string.Empty;
        if (frame.Length != JoinSize || frame[0] != Join ||
            BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(37, 2)) != SteamMatchDescriptor.CurrentProtocolVersion)
            return false;
        for (int i = 0; i < 64; i++)
        {
            byte c = frame[39 + i];
            if (c is not (>= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f'))
                return false;
        }
        if (!Guid.TryParseExact(Encoding.ASCII.GetString(frame.Slice(1, 36)), "D", out matchId) || matchId == Guid.Empty)
            return false;
        contentHash = Encoding.ASCII.GetString(frame.Slice(39, 64));
        return true;
    }

    public static byte[] CreateAck(ulong entityId)
    {
        if (entityId == 0) throw new ArgumentOutOfRangeException(nameof(entityId));
        var frame = new byte[AckSize];
        frame[0] = Ack;
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(1), entityId);
        return frame;
    }

    public static bool TryParseAck(ReadOnlySpan<byte> frame, out ulong entityId)
    {
        entityId = 0;
        if (frame.Length != AckSize || frame[0] != Ack) return false;
        entityId = BinaryPrimitives.ReadUInt64LittleEndian(frame.Slice(1));
        return entityId != 0;
    }

    private static bool IsLowerHash(string? hash)
    {
        if (hash is not { Length: 64 }) return false;
        foreach (var c in hash)
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}
