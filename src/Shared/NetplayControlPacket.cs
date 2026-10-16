using System;
using System.Buffers.Binary;

namespace SlopArena.Shared;

public enum NetplayControlKind : byte
{
    Bootstrap = 1,
    Ready = 2,
    Clock = 3,
}

/// <summary>
/// Fixed-size, versioned startup and authoritative-clock control frame. Clock frames also
/// report how early this peer's inputs reached the GameServer since the previous Clock:
/// <see cref="InputSlack"/> is the smallest number of ticks between arrival and consumption
/// (<see cref="NoInputSlack"/> when none arrived) and <see cref="LateInputs"/> counts
/// datagrams whose newest input arrived after its tick had been simulated.
/// </summary>
public readonly record struct NetplayControlPacket(NetplayControlKind Kind, ulong EntityId, uint Tick, uint StartTick,
    sbyte InputSlack = 127, byte LateInputs = 0)
{
    public const byte Version = SimulationProtocol.Version;
    public const int Size = 20;
    public const sbyte NoInputSlack = 127;

    public bool IsValid => EntityId != 0 && Kind is >= NetplayControlKind.Bootstrap and <= NetplayControlKind.Clock;

    public void Serialize(Span<byte> destination)
    {
        if (destination.Length != Size) throw new ArgumentException("Invalid control frame length.", nameof(destination));
        if (!IsValid) throw new InvalidOperationException("Invalid netplay control packet.");
        destination[0] = (byte)Kind;
        destination[1] = Version;
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(2, 8), EntityId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(10, 4), Tick);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(14, 4), StartTick);
        destination[18] = unchecked((byte)InputSlack);
        destination[19] = LateInputs;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> source, out NetplayControlPacket packet)
    {
        packet = default;
        if (source.Length != Size || source[1] != Version ||
            source[0] < (byte)NetplayControlKind.Bootstrap || source[0] > (byte)NetplayControlKind.Clock)
            return false;
        var value = new NetplayControlPacket((NetplayControlKind)source[0],
            BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(2, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(10, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(14, 4)),
            unchecked((sbyte)source[18]),
            source[19]);
        if (!value.IsValid) return false;
        packet = value;
        return true;
    }
}
