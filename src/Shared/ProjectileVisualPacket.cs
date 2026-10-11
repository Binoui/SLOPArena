using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>Cosmetic snapshot of server-owned moving hitboxes; never used for collision.</summary>
public readonly record struct ProjectileVisualState(
    ulong OwnerId,
    ulong ActivationId,
    int OperationIndex,
    CharacterClass Character,
    byte AttackSlot,
    bool Airborne,
    float X, float Y, float Z,
    float VX, float VY, float VZ,
    float Radius = 0.5f);

public readonly struct ProjectileVisualPacket
{
    private const uint Magic = 0x53495650; // PVIS
    private const byte Version = 2;
    public const int HeaderSize = 10;
    public const int EntrySize = 51;
    public const int MaxProjectiles = 16;
    public const int MaxSize = HeaderSize + EntrySize * MaxProjectiles;

    public ProjectileVisualPacket(uint tick, IReadOnlyList<ProjectileVisualState> projectiles)
    {
        if (projectiles == null || projectiles.Count > MaxProjectiles)
            throw new ArgumentOutOfRangeException(nameof(projectiles));
        Tick = tick;
        Projectiles = projectiles;
    }

    public uint Tick { get; }
    public IReadOnlyList<ProjectileVisualState> Projectiles { get; }
    public int WireSize => HeaderSize + EntrySize * Projectiles.Count;

    public void Serialize(Span<byte> buffer)
    {
        if (buffer.Length < WireSize) throw new ArgumentException("Buffer too small", nameof(buffer));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, Magic);
        buffer[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(5), Tick);
        buffer[9] = (byte)Projectiles.Count;
        for (int i = 0; i < Projectiles.Count; i++)
        {
            var value = Projectiles[i];
            if (!Valid(value)) throw new ArgumentException("Invalid projectile visual state", nameof(Projectiles));
            var entry = buffer.Slice(HeaderSize + i * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt64LittleEndian(entry, value.OwnerId);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(8), value.ActivationId);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(16), value.OperationIndex);
            entry[20] = (byte)value.Character;
            entry[21] = value.AttackSlot;
            entry[22] = value.Airborne ? (byte)1 : (byte)0;
            WriteFloat(entry.Slice(23), value.X);
            WriteFloat(entry.Slice(27), value.Y);
            WriteFloat(entry.Slice(31), value.Z);
            WriteFloat(entry.Slice(35), value.VX);
            WriteFloat(entry.Slice(39), value.VY);
            WriteFloat(entry.Slice(43), value.VZ);
            WriteFloat(entry.Slice(47), value.Radius);
        }
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> buffer, out ProjectileVisualPacket packet)
    {
        packet = default;
        if (buffer.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != Magic || buffer[4] != Version)
            return false;
        int count = buffer[9];
        if (count > MaxProjectiles || buffer.Length != HeaderSize + count * EntrySize)
            return false;
        var entries = new ProjectileVisualState[count];
        for (int i = 0; i < count; i++)
        {
            var entry = buffer.Slice(HeaderSize + i * EntrySize, EntrySize);
            if (entry[22] > 1) return false;
            var value = new ProjectileVisualState(
                BinaryPrimitives.ReadUInt64LittleEndian(entry),
                BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(8)),
                BinaryPrimitives.ReadInt32LittleEndian(entry.Slice(16)),
                (CharacterClass)entry[20], entry[21], entry[22] == 1,
                ReadFloat(entry.Slice(23)), ReadFloat(entry.Slice(27)), ReadFloat(entry.Slice(31)),
                ReadFloat(entry.Slice(35)), ReadFloat(entry.Slice(39)), ReadFloat(entry.Slice(43)),
                ReadFloat(entry.Slice(47)));
            if (!Valid(value)) return false;
            entries[i] = value;
        }
        packet = new ProjectileVisualPacket(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(5)), entries);
        return true;
    }

    private static void WriteFloat(Span<byte> buffer, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(value));
    private static float ReadFloat(ReadOnlySpan<byte> buffer)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Valid(in ProjectileVisualState value)
        => value.OwnerId != 0 && value.ActivationId != 0 && value.OperationIndex >= 0
            && value.Character is > CharacterClass.None and <= CharacterClass.Bonk
            && value.AttackSlot > 0 && value.AttackSlot <= AbilitySlots.Count
            && Finite(value.X) && Finite(value.Y) && Finite(value.Z)
            && Finite(value.VX) && Finite(value.VY) && Finite(value.VZ)
            && Finite(value.Radius) && value.Radius > 0f;
}
