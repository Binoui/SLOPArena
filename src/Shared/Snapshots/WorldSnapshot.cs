using System;
using System.IO;

namespace SlopArena.Shared
{
    /// <summary>
    /// The complete state of a PvP <see cref="ServerSimulation"/> after one tick (ADR-0039):
    /// every fighter's full state, active ability runtimes, live hitboxes, projectiles and
    /// simulation counters, in a canonical encoding. Equal simulation states produce equal
    /// bytes. Content (definitions, cooked slots, baked animation) is referenced, not copied:
    /// restore into a simulation with the same arena, rule and registered fighters.
    /// </summary>
    public sealed class WorldSnapshot
    {
        public WorldSnapshot(byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            var header = new SnapshotReader(data);
            uint version = header.ReadUInt();
            if (version != ServerSimulation.SnapshotFormatVersion)
                throw new InvalidDataException($"World snapshot format {version} is not supported.");
            Tick = header.ReadUInt();
        }

        /// <summary>The last completed simulation tick.</summary>
        public uint Tick { get; }

        public byte[] Data { get; }

        public bool ContentEquals(WorldSnapshot other)
            => other != null && Data.AsSpan().SequenceEqual(other.Data);
    }
}
