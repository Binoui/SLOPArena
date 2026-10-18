using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SlopArena.Shared
{
    /// <summary>
    /// Canonical little-endian encoding behind <see cref="WorldSnapshot"/>. Strings and
    /// hit-target sets are written once and referenced afterwards: the hitboxes of one hit
    /// group share a single set with their ability, and a restore must rebuild that sharing,
    /// not independent copies. Sets are written sorted, so equal states encode to equal bytes.
    /// </summary>
    public sealed class SnapshotWriter
    {
        private byte[] _buffer;
        private int _length;
        private readonly Dictionary<string, int> _strings = new(StringComparer.Ordinal);
        private readonly Dictionary<HashSet<ulong>, int> _sets = new(SetIdentity.Instance);
        private readonly List<ulong> _sortScratch = new();

        public SnapshotWriter(int capacity = 1024) => _buffer = new byte[Math.Max(16, capacity)];

        public int Length => _length;

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);
        public void WriteByte(byte value) => Reserve(1)[0] = value;
        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));
        public void WriteUShort(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
        public void WriteShort(short value) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);
        public void WriteInt(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        public void WriteUInt(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        public void WriteULong(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        /// <summary>Null as -1; a repeated string as its first index.</summary>
        public void WriteString(string? value)
        {
            if (value == null)
            {
                WriteInt(-1);
                return;
            }
            if (_strings.TryGetValue(value, out int index))
            {
                WriteInt(index);
                return;
            }
            index = _strings.Count;
            _strings.Add(value, index);
            WriteInt(index);
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            bytes.CopyTo(Reserve(bytes.Length));
        }

        /// <summary>Null as -1; a set already written (the same instance) as its first index.</summary>
        public void WriteSet(HashSet<ulong>? set)
        {
            if (set == null)
            {
                WriteInt(-1);
                return;
            }
            if (_sets.TryGetValue(set, out int index))
            {
                WriteInt(index);
                return;
            }
            index = _sets.Count;
            _sets.Add(set, index);
            WriteInt(index);
            _sortScratch.Clear();
            _sortScratch.AddRange(set);
            _sortScratch.Sort();
            WriteInt(_sortScratch.Count);
            foreach (ulong id in _sortScratch)
                WriteULong(id);
        }

        private Span<byte> Reserve(int count)
        {
            if (_length + count > _buffer.Length)
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
            var span = new Span<byte>(_buffer, _length, count);
            _length += count;
            return span;
        }

        private sealed class SetIdentity : IEqualityComparer<HashSet<ulong>>
        {
            public static readonly SetIdentity Instance = new();
            public bool Equals(HashSet<ulong>? x, HashSet<ulong>? y) => ReferenceEquals(x, y);
            public int GetHashCode(HashSet<ulong> set) => RuntimeHelpers.GetHashCode(set);
        }
    }
}
