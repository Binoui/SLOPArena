using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SlopArena.Shared
{
    /// <summary>Reads the encoding written by <see cref="SnapshotWriter"/>; malformed input throws
    /// <see cref="InvalidDataException"/> instead of producing a partial state.</summary>
    public sealed class SnapshotReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;
        private readonly List<string> _strings = new();
        private readonly List<HashSet<ulong>> _sets = new();

        public SnapshotReader(byte[] data) : this(data, 0, data?.Length ?? 0) { }

        public SnapshotReader(byte[] data, int offset, int count)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            _position = offset;
            _end = offset + count;
        }

        public bool AtEnd => _position == _end;

        public bool ReadBool()
        {
            byte value = ReadByte();
            if (value > 1) throw Malformed("boolean");
            return value == 1;
        }

        public byte ReadByte()
        {
            Require(1);
            return _data[_position++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());
        public ushort ReadUShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public short ReadShort() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
        public int ReadInt() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public uint ReadUInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong ReadULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

        /// <summary>A count that the remaining bytes could hold at <paramref name="minimumBytesEach"/>.</summary>
        public int ReadCount(int minimumBytesEach)
        {
            int count = ReadInt();
            if (count < 0 || (long)count * Math.Max(1, minimumBytesEach) > _end - _position)
                throw Malformed("count");
            return count;
        }

        /// <summary>Like <see cref="ReadCount"/>, with -1 meaning an absent (null) collection.</summary>
        public int ReadOptionalCount(int minimumBytesEach)
        {
            int count = ReadInt();
            if (count < -1 || (long)count * Math.Max(1, minimumBytesEach) > _end - _position)
                throw Malformed("count");
            return count;
        }

        public string? ReadString()
        {
            int index = ReadInt();
            if (index == -1) return null;
            if (index >= 0 && index < _strings.Count) return _strings[index];
            if (index != _strings.Count) throw Malformed("string reference");
            int length = ReadCount(1);
            string value = Encoding.UTF8.GetString(_data, _position, length);
            _position += length;
            _strings.Add(value);
            return value;
        }

        public HashSet<ulong>? ReadSet()
        {
            int index = ReadInt();
            if (index == -1) return null;
            if (index >= 0 && index < _sets.Count) return _sets[index];
            if (index != _sets.Count) throw Malformed("set reference");
            int count = ReadCount(8);
            var set = new HashSet<ulong>();
            for (int i = 0; i < count; i++)
                set.Add(ReadULong());
            _sets.Add(set);
            return set;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            Require(count);
            var span = new ReadOnlySpan<byte>(_data, _position, count);
            _position += count;
            return span;
        }

        private void Require(int count)
        {
            if (_end - _position < count) throw Malformed("truncated data");
        }

        internal static InvalidDataException Malformed(string what) => new($"World snapshot is malformed: {what}.");
    }
}
