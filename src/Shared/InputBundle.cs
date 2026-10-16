using System;
using System.Buffers.Binary;
using System.IO;

namespace SlopArena.Shared
{
    /// <summary>
    /// Uplink input datagram: entityId(8) + newestTick(4) + count(1) + count × InputState,
    /// ordered oldest to newest for consecutive ticks ending at <c>newestTick</c>. Every
    /// datagram repeats the latest simulated ticks, so a lost datagram does not lose a
    /// press; the GameServer keeps one input per tick.
    /// </summary>
    public static class InputBundle
    {
        public const int MaxInputs = 6;
        public const int HeaderSize = 8 + 4 + 1;
        public const int MaxSize = HeaderSize + MaxInputs * InputState.Size;

        public static int SizeFor(int count) => HeaderSize + count * InputState.Size;

        /// <summary>Write <paramref name="inputs"/> (oldest first) and return the byte count.</summary>
        public static int Write(Span<byte> destination, ulong entityId, uint newestTick, ReadOnlySpan<InputState> inputs)
        {
            if (inputs.Length is < 1 or > MaxInputs)
                throw new ArgumentOutOfRangeException(nameof(inputs));
            if (newestTick < inputs.Length - 1)
                throw new ArgumentOutOfRangeException(nameof(newestTick));
            int size = SizeFor(inputs.Length);
            if (destination.Length < size)
                throw new ArgumentException("Buffer too small", nameof(destination));

            BinaryPrimitives.WriteUInt64LittleEndian(destination, entityId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), newestTick);
            destination[12] = (byte)inputs.Length;
            for (int i = 0; i < inputs.Length; i++)
                inputs[i].Write(destination.Slice(HeaderSize + i * InputState.Size, InputState.Size));
            return size;
        }

        /// <summary>Strictly decode a bundle; <paramref name="inputs"/> needs <see cref="MaxInputs"/> slots.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out ulong entityId, out uint newestTick,
            Span<InputState> inputs, out int count)
        {
            entityId = 0;
            newestTick = 0;
            count = 0;
            if (source.Length < HeaderSize || inputs.Length < MaxInputs)
                return false;
            int declared = source[12];
            if (declared is < 1 or > MaxInputs || source.Length != SizeFor(declared))
                return false;
            uint newest = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8));
            if (newest < declared - 1)
                return false;
            try
            {
                for (int i = 0; i < declared; i++)
                    inputs[i] = InputState.Deserialize(source.Slice(HeaderSize + i * InputState.Size, InputState.Size));
            }
            catch (ArgumentException) { return false; }
            catch (InvalidDataException) { return false; }

            entityId = BinaryPrimitives.ReadUInt64LittleEndian(source);
            newestTick = newest;
            count = declared;
            return true;
        }

        /// <summary>Tick carried by input <paramref name="index"/> of a bundle with <paramref name="count"/> inputs.</summary>
        public static uint TickAt(uint newestTick, int count, int index) => newestTick - (uint)(count - 1 - index);
    }
}
