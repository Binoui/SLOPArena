using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopArena.Shared
{
    /// <summary>
    /// Binary serialization for ArenaDefinition.
    /// Pure C# — no Godot dependency. Used by both client and server.
    ///
    /// Format:
    ///   Magic:  "AREN" (0x4E455241)
    ///   Version: uint32 (1 through 6; ordinary stages continue writing 5)
    ///   Name:        uint32 len + UTF8 bytes
    ///   DisplayName: uint32 len + UTF8 bytes
    ///   PreviewColor: uint32 len + UTF8 bytes
    ///   KillHeight:  float32
    ///   [Version 5+]: KillTop, KillMinX, KillMaxX, KillMinZ, KillMaxZ: 5 × float32
    ///   MinX, MaxX, MinZ, MaxZ: 4 × float32
    ///   SpawnCount: uint32
    ///     For each: X, Y, Z, Yaw (4 × float32)
    ///   [Version 3+]: Heightmap: Width(int), Height(int), CellSize(float),
    ///     OriginX(float), OriginZ(float), DataLength(int), then Data[] floats
    ///   [Version 2+]: TriangleCount: uint32
    ///     For each: AX, AY, AZ, BX, BY, BZ, CX, CY, CZ (9 × float32)
    ///   [Version 6]: Arcade section byte length and typed section bytes
    /// <summary>
    /// Version history:
    ///   1 = Initial format: platforms, spawns, bounds. No triangle collision.
    ///   2 = Added CollisionTriangle[] after spawn data.
    ///   3 = Removed FloorHeight + PlatformDef[]; added Heightmap after spawn data.
    ///   4 = PreviewColor replaces ScenePath (ScenePath was written but never read).
    ///   5 = Added side/top blast lines (KillTop, KillMinX/MaxX, KillMinZ/MaxZ) after KillHeight.
    ///       0 in a v5 file means "auto" (derived from bounds/heightmap at resolve time);
    ///       v4 files carry no lines and resolve the same way.
    ///   6 = Embedded Arcade metadata. Plain stages retain byte-identical v5 exports.
    /// </summary>

    public static class ArenaBinaryFormat
    {
        private const uint Magic = 0x4E455241; // "AREN"
        private const uint Version = 6;

        /// <summary>
        /// Serialize an ArenaDefinition to a byte array.
        /// </summary>
        public static byte[] Serialize(ArenaDefinition arena)
        {
            arena.Arcade?.Slopball?.ValidateCourt(in arena);
            arena.Arcade?.Course?.ValidateArena(in arena);
            arena.Arcade?.Mayhem?.ValidateArena(in arena);
            byte[]? arcade = arena.Arcade != null ? ArcadeStageBinaryFormat.Serialize(arena.Arcade) : null;
            if (arcade?.Length > 8192) throw new ArgumentException("Arcade metadata exceeds the bounded stage section.");
            if (arcade != null && (arena.SpawnPoints == null || arena.SpawnPoints.Length != (arena.Arcade?.Mayhem != null ? 4 : 1)))
                throw new ArgumentException("An Arcade stage needs one authored start, or four for Mayhem.");
            // Calculate size upfront
            int size = 4 + 4; // magic + version
            size += 4 + Encoding.UTF8.GetByteCount(arena.Name ?? "");
            size += 4 + Encoding.UTF8.GetByteCount(arena.DisplayName ?? "");
            size += 4 + Encoding.UTF8.GetByteCount(arena.PreviewColor ?? "");

            size += 4; // KillHeight (float)
            size += 5 * 4; // KillTop, KillMinX, KillMaxX, KillMinZ, KillMaxZ (v5)

            // Heightmap (v3+)
            int hmDataLen = arena.Heightmap.Data?.Length ?? 0;
            size += 6 * 4; // Width, Height, CellSize, OriginX, OriginZ, DataLength
            size += hmDataLen * 4; // Data[] floats

            // Bounds
            size += 4 * 4; // MinX, MaxX, MinZ, MaxZ

            // Spawns
            int spawnCount = arena.SpawnPoints?.Length ?? 0;
            size += 4; // count
            size += spawnCount * 4 * 4; // 4 floats each

            // Collision triangles (v2)
            int triCount = arena.CollisionTriangles?.Length ?? 0;
            size += 4; // count
            size += triCount * 9 * 4; // 9 floats per triangle
            if (arcade != null) size += 4 + arcade.Length;

            var buffer = new byte[size];
            int pos = 0;

            // Magic + Version
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), Magic); pos += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), arcade != null ? Version : 5u); pos += 4;

            // Name
            WriteString(buffer, ref pos, arena.Name ?? "");

            // DisplayName
            WriteString(buffer, ref pos, arena.DisplayName ?? "");

            // PreviewColor
            WriteString(buffer, ref pos, arena.PreviewColor ?? "");

            // KillHeight
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.KillHeight)); pos += 4;

            // Blast lines (v5)
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.KillTop)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.KillMinX)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.KillMaxX)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.KillMinZ)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.KillMaxZ)); pos += 4;

            // Bounds
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.MinX)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.MaxX)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.MinZ)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(arena.MaxZ)); pos += 4;

            // Spawns
            int spawnCount32 = arena.SpawnPoints?.Length ?? 0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), spawnCount32); pos += 4;
            for (int i = 0; i < spawnCount32; i++)
            {
                var s = arena.SpawnPoints![i];
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(s.X)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(s.Y)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(s.Z)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(s.Yaw)); pos += 4;
            }

            // Heightmap (v3+)
            ArenaHeightmap hm = arena.Heightmap;
            int dataLen = hm.Data?.Length ?? 0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), hm.Width); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), hm.Height); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(hm.CellSize)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(hm.OriginX)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(hm.OriginZ)); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), dataLen); pos += 4;
            for (int i = 0; i < dataLen; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(hm.Data![i])); pos += 4;
            }

            // Collision triangles (v2)
            int triCount32 = arena.CollisionTriangles?.Length ?? 0;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), triCount32); pos += 4;
            for (int i = 0; i < triCount32; i++)
            {
                var t = arena.CollisionTriangles![i];
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.AX)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.AY)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.AZ)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.BX)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.BY)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.BZ)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.CX)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.CY)); pos += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), BitConverter.SingleToInt32Bits(t.CZ)); pos += 4;
            }

            if (arcade != null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), arcade.Length); pos += 4;
                arcade.CopyTo(buffer, pos);
            }
            return buffer;
        }

        public static ArenaDefinition? Deserialize(byte[] data)
        {
            int pos = 0;

            // Magic
            if (data.Length < 8) return null;
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (magic != Magic) return null;

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (version < 1 || version > Version) return null;

            var arena = new ArenaDefinition();

            // Name
            arena.Name = ReadString(data, ref pos)!;
            if (arena.Name == null) return null;

            // DisplayName
            arena.DisplayName = ReadString(data, ref pos)!;
            if (arena.DisplayName == null) return null;

            // PreviewColor (v4+); v3 wrote ScenePath here — read and discard it.
            if (version >= 4)
            {
                arena.PreviewColor = ReadString(data, ref pos) ?? "";
            }
            else
            {
                if (ReadString(data, ref pos) == null) return null; // truncated v3 file
                arena.PreviewColor = "";
            }

            // KillHeight
            if (pos + 4 > data.Length) return null;
            arena.KillHeight = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;

            // Blast lines (v5+); v4 files carry none — fields stay 0 = auto-derive.
            if (version >= 5)
            {
                if (pos + 20 > data.Length) return null;
                arena.KillTop = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                arena.KillMinX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                arena.KillMaxX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                arena.KillMinZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                arena.KillMaxZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
            }

            // Bounds
            if (pos + 16 > data.Length) return null;
            arena.MinX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
            arena.MaxX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
            arena.MinZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
            arena.MaxZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;

            // Spawns
            if (pos + 4 > data.Length) return null;
            int spawnCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (spawnCount < 0 || spawnCount > (data.Length - pos) / 16) return null;
            arena.SpawnPoints = new SpawnPoint[spawnCount];
            for (int i = 0; i < spawnCount; i++)
            {
                if (pos + 16 > data.Length) return null;
                float sx = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                float sy = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                float sz = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                float syaw = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                arena.SpawnPoints[i] = new SpawnPoint { X = sx, Y = sy, Z = sz, Yaw = syaw };
            }

            // Heightmap (v3+)
            if (version >= 3)
            {
                if (pos + 24 > data.Length) return null;
                int hmWidth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                int hmHeight = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                float hmCellSize = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                float hmOriginX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                float hmOriginZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                int hmDataLen = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                if (hmDataLen < 0 || hmDataLen > (data.Length - pos) / 4) return null;
                var hmData = new float[hmDataLen];
                for (int i = 0; i < hmDataLen; i++)
                {
                    hmData[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                }
                arena.Heightmap = new ArenaHeightmap { Width = hmWidth, Height = hmHeight, CellSize = hmCellSize, OriginX = hmOriginX, OriginZ = hmOriginZ, Data = hmData };
            }
            else
            {
                arena.Heightmap = default;
            }

            // Collision triangles (v2+)
            if (version >= 2)
            {
                if (pos + 4 > data.Length) return null;
                int triCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                arena.CollisionTriangles = new CollisionTriangle[triCount];
                for (int i = 0; i < triCount; i++)
                {
                    if (pos + 36 > data.Length) return null;
                    float ax = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float ay = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float az = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float bx = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float by = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float bz = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float cx = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float cy = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    float cz = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos))); pos += 4;
                    arena.CollisionTriangles[i] = new CollisionTriangle { AX = ax, AY = ay, AZ = az, BX = bx, BY = by, BZ = bz, CX = cx, CY = cy, CZ = cz };
                }
            }
            else
            {
                arena.CollisionTriangles = null;
            }

            if (version >= 6)
            {
                if (pos + 4 > data.Length) return null;
                int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                if (length <= 0 || length > 8192 || length != data.Length - pos) return null;
                var section = new byte[length];
                Array.Copy(data, pos, section, 0, length);
                arena.Arcade = ArcadeStageBinaryFormat.Deserialize(section);
                if (arena.Arcade == null || arena.SpawnPoints.Length != (arena.Arcade?.Mayhem != null ? 4 : 1)) return null;
            }

            // The grid is derived at load time, not serialized. Direct match loads
            // need the same broadphase as registry loads.
            arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
            try { arena.Arcade?.Slopball?.ValidateCourt(in arena); arena.Arcade?.Course?.ValidateArena(in arena); arena.Arcade?.Mayhem?.ValidateArena(in arena); }
            catch (ArgumentException) { return null; }
            return arena;
        }

        /// <summary>Load from file path. Returns null on failure.</summary>
        public static ArenaDefinition? LoadFromFile(string path)
        {
            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                return Deserialize(data);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Save to file path.</summary>
        public static void SaveToFile(string path, ArenaDefinition arena)
        {
            byte[] data = Serialize(arena);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
            System.IO.File.WriteAllBytes(path, data);
        }

        // ── String helpers ──

        private static void WriteString(byte[] buffer, ref int pos, string value)
        {
            int byteLen = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos), byteLen); pos += 4;
            if (byteLen > 0)
            {
                Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, pos);
                pos += byteLen;
            }
        }

        private static string? ReadString(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) return null;
            int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            if (pos + len > data.Length) return null;
            if (len == 0) return "";
            string result = Encoding.UTF8.GetString(data, pos, len);
            pos += len;
            return result;
        }
    }
}
