using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public enum RuntimeSolidPhase : byte { Rising, Active, Broken, Sinking }

public readonly record struct RuntimeSolid(ulong EntityId, ulong CageEntityId, ArcadePoint CageCenter,
    ArcadePoint Center, ArcadePoint HalfExtents, float Yaw, byte HitsLeft, byte HitBudget,
    RuntimeSolidPhase Phase, ushort PhaseTick)
{
    public bool Blocks => HitsLeft > 0 && Phase is RuntimeSolidPhase.Rising or RuntimeSolidPhase.Active;
}

/// <summary>Mayhem walls have no support surface and never change the baked arena.</summary>
public sealed class RuntimeSolidSet
{
    public const ulong FirstEntityId = 1UL << 44;
    public const int MaximumCount = 16;
    private readonly List<RuntimeSolid> _walls = new();
    public IReadOnlyList<RuntimeSolid> Walls => _walls.AsReadOnly();
    public static bool IsWallId(ulong id) => id >= FirstEntityId && id < FirstEntityId + MaximumCount;
    public bool Contains(ulong id) => _walls.Any(w => w.EntityId == id);
    internal void Clear() => _walls.Clear();
    internal void AddCage(ulong fighter, ArcadePoint center, float top, int cageIndex)
    {
        for (int side = 0; side < 4; side++)
        {
            float yaw = side % 2 == 0 ? 0 : MathF.PI * .5f;
            float dx = side == 1 ? 1 : side == 3 ? -1 : 0;
            float dz = side == 0 ? 1 : side == 2 ? -1 : 0;
            byte budget = side == 2 ? MankiMayhemTuning.CrackedWallHits : MankiMayhemTuning.WallHits;
            _walls.Add(new(FirstEntityId + (ulong)(cageIndex * 4 + side), fighter, center,
                new(center.X + dx * MankiMayhemTuning.CageHalfSize, top + MankiMayhemTuning.WallHeight * .5f,
                    center.Z + dz * MankiMayhemTuning.CageHalfSize),
                new(MankiMayhemTuning.CageHalfSize + MankiMayhemTuning.WallHalfThickness,
                    MankiMayhemTuning.WallHeight * .5f, MankiMayhemTuning.WallHalfThickness),
                yaw, budget, budget, RuntimeSolidPhase.Rising, 0));
        }
    }

    internal void Step()
    {
        for (int i = _walls.Count - 1; i >= 0; i--)
        {
            var wall = _walls[i];
            ushort age = (ushort)Math.Min(ushort.MaxValue, wall.PhaseTick + 1);
            if (wall.Phase == RuntimeSolidPhase.Sinking && age >= MankiMayhemTuning.WallSinkTicks)
            { _walls.RemoveAt(i); continue; }
            _walls[i] = wall with { PhaseTick = age,
                Phase = wall.Phase == RuntimeSolidPhase.Rising && age >= MankiMayhemTuning.WallRiseTicks
                    ? RuntimeSolidPhase.Active : wall.Phase };
        }
    }

    internal void Sink()
    {
        for (int i = 0; i < _walls.Count; i++)
            if (_walls[i].Phase != RuntimeSolidPhase.Sinking)
                _walls[i] = _walls[i] with { Phase = RuntimeSolidPhase.Sinking, PhaseTick = 0 };
    }

    internal bool Hit(ulong id, bool wreck, out RuntimeSolid result)
    {
        for (int i = 0; i < _walls.Count; i++)
        {
            var wall = _walls[i];
            if (wall.EntityId != id || !wall.Blocks) continue;
            byte left = wreck ? (byte)0 : (byte)(wall.HitsLeft - 1);
            result = _walls[i] = wall with { HitsLeft = left,
                Phase = left == 0 ? RuntimeSolidPhase.Broken : wall.Phase, PhaseTick = left == 0 ? (ushort)0 : wall.PhaseTick };
            return true;
        }
        result = default; return false;
    }

    public IEnumerable<SpellResolver.EntityData> CollisionTargets()
    {
        foreach (var wall in _walls)
        {
            if (!wall.Blocks) continue;
            float c = MathF.Cos(wall.Yaw), s = MathF.Sin(wall.Yaw);
            float length = wall.HalfExtents.X - wall.HalfExtents.Z;
            float radius = wall.HalfExtents.Z;
            for (float y = wall.Center.Y - wall.HalfExtents.Y + radius;
                y < wall.Center.Y + wall.HalfExtents.Y + radius; y += radius * 2)
                yield return new SpellResolver.EntityData
                {
                    Id = wall.EntityId, Active = true, RuntimeSolid = true, Shape = HitboxShape.Capsule,
                    PosX = wall.Center.X - c * length, PosY = MathF.Min(y, wall.Center.Y + wall.HalfExtents.Y - radius),
                    PosZ = wall.Center.Z + s * length,
                    EndX = wall.Center.X + c * length, EndY = MathF.Min(y, wall.Center.Y + wall.HalfExtents.Y - radius),
                    EndZ = wall.Center.Z - s * length, Radius = radius,
                };
        }
    }

    internal IEnumerable<ulong> InBlast(float x, float y, float z, float radius)
    {
        foreach (var wall in _walls)
        {
            if (!wall.Blocks) continue;
            Local(wall, x, z, out float lx, out float lz);
            float dx = MathF.Max(0, MathF.Abs(lx) - wall.HalfExtents.X);
            float dy = MathF.Max(0, MathF.Abs(y - wall.Center.Y) - wall.HalfExtents.Y);
            float dz = MathF.Max(0, MathF.Abs(lz) - wall.HalfExtents.Z);
            if (dx * dx + dy * dy + dz * dz <= radius * radius) yield return wall.EntityId;
        }
    }

    public void Constrain(in CharacterState previous, ref CharacterState state, CharacterDefinition def)
    {
        float x = previous.PX, z = previous.PZ;
        float remainingX = state.PX - x, remainingZ = state.PZ - z;
        foreach (var wall in _walls)
        {
            if (!wall.Blocks || !Vertical(wall, previous.PY, def)) continue;
            Local(wall, x, z, out float lx, out float lz);
            float hx = wall.HalfExtents.X + def.CapsuleRadius, hz = wall.HalfExtents.Z + def.CapsuleRadius;
            if (MathF.Abs(lx) >= hx || MathF.Abs(lz) >= hz) continue;
            Local(wall, wall.CageCenter.X, wall.CageCenter.Z, out _, out float inside);
            float delta = (inside < 0 ? -1 : 1) * (hz + .001f) - lz;
            float nx = MathF.Sin(wall.Yaw), nz = MathF.Cos(wall.Yaw);
            x += nx * delta; z += nz * delta;
            RemoveInto(ref state, nx * MathF.Sign(delta), nz * MathF.Sign(delta));
        }
        for (int iteration = 0; iteration < 4; iteration++)
        {
            float earliest = 1f, normalX = 0, normalZ = 0;
            bool found = false;
            foreach (var wall in _walls)
            {
                if (!wall.Blocks) continue;
                Local(wall, x, z, out float lx, out float lz);
                float c = MathF.Cos(wall.Yaw), s = MathF.Sin(wall.Yaw);
                float dx = remainingX * c - remainingZ * s, dz = remainingX * s + remainingZ * c;
                float enter = 0, leave = 1, nx = 0, nz = 0;
                if (!Slab(lx, dx, wall.HalfExtents.X + def.CapsuleRadius, 1, 0, ref enter, ref leave, ref nx, ref nz)
                    || !Slab(lz, dz, wall.HalfExtents.Z + def.CapsuleRadius, 0, 1, ref enter, ref leave, ref nx, ref nz)
                    || enter < 0 || enter > earliest || nx == 0 && nz == 0
                    || !Vertical(wall, previous.PY + (state.PY - previous.PY) * enter, def)) continue;
                earliest = enter; normalX = nx * c + nz * s; normalZ = -nx * s + nz * c; found = true;
            }
            if (!found) { x += remainingX; z += remainingZ; break; }
            x += remainingX * earliest + normalX * .001f;
            z += remainingZ * earliest + normalZ * .001f;
            remainingX *= 1 - earliest; remainingZ *= 1 - earliest;
            float into = remainingX * normalX + remainingZ * normalZ;
            if (into < 0) { remainingX -= into * normalX; remainingZ -= into * normalZ; }
            RemoveInto(ref state, normalX, normalZ);
        }
        state.PX = x; state.PZ = z;
    }

    private static bool Vertical(in RuntimeSolid wall, float y, CharacterDefinition def)
        => y - def.CapsuleHeight * .5f < wall.Center.Y + wall.HalfExtents.Y
            && y + def.CapsuleHeight * .5f > wall.Center.Y - wall.HalfExtents.Y;

    private static bool Slab(float p, float d, float half, float ax, float az,
        ref float enter, ref float leave, ref float nx, ref float nz)
    {
        if (MathF.Abs(d) < .000001f) return MathF.Abs(p) <= half;
        float a = (-half - p) / d, b = (half - p) / d;
        float near = MathF.Min(a, b), far = MathF.Max(a, b);
        if (near >= enter) { enter = near; nx = d > 0 ? -ax : ax; nz = d > 0 ? -az : az; }
        leave = MathF.Min(leave, far);
        return enter <= leave && leave >= 0;
    }

    private static void Local(in RuntimeSolid wall, float x, float z, out float lx, out float lz)
    {
        float c = MathF.Cos(wall.Yaw), s = MathF.Sin(wall.Yaw);
        float dx = x - wall.Center.X, dz = z - wall.Center.Z;
        lx = dx * c - dz * s; lz = dx * s + dz * c;
    }

    private static void RemoveInto(ref CharacterState state, float nx, float nz)
    {
        float v = state.VX * nx + state.VZ * nz;
        if (v < 0) { state.VX -= v * nx; state.VZ -= v * nz; }
        float k = state.KVX * nx + state.KVZ * nz;
        if (k < 0) { state.KVX -= k * nx; state.KVZ -= k * nz; }
    }

    internal void WriteSnapshot(SnapshotWriter w)
    {
        w.WriteInt(_walls.Count);
        foreach (var wall in _walls)
        {
            w.WriteULong(wall.EntityId); w.WriteULong(wall.CageEntityId);
            Point(w, wall.CageCenter); Point(w, wall.Center); Point(w, wall.HalfExtents);
            w.WriteFloat(wall.Yaw); w.WriteByte(wall.HitsLeft); w.WriteByte(wall.HitBudget);
            w.WriteByte((byte)wall.Phase); w.WriteUShort(wall.PhaseTick);
        }
    }
    private static void Point(SnapshotWriter w, ArcadePoint p) { w.WriteFloat(p.X); w.WriteFloat(p.Y); w.WriteFloat(p.Z); }
    private static ArcadePoint Point(SnapshotReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    internal static RuntimeSolid[] ReadSnapshot(SnapshotReader r, IReadOnlyList<ulong> fighters)
    {
        int count = r.ReadCount(61);
        if (count > MaximumCount) throw SnapshotReader.Malformed("Mayhem solid count");
        var walls = new RuntimeSolid[count]; var ids = new HashSet<ulong>();
        for (int i = 0; i < count; i++)
        {
            var wall = walls[i] = new(r.ReadULong(), r.ReadULong(), Point(r), Point(r), Point(r), r.ReadFloat(),
                r.ReadByte(), r.ReadByte(), (RuntimeSolidPhase)r.ReadByte(), r.ReadUShort());
            if (!IsWallId(wall.EntityId) || !ids.Add(wall.EntityId) || !fighters.Contains(wall.CageEntityId)
                || !wall.CageCenter.IsFinite || !wall.Center.IsFinite || !wall.HalfExtents.IsFinite || !float.IsFinite(wall.Yaw)
                || wall.HalfExtents.X <= 0 || wall.HalfExtents.Y <= 0 || wall.HalfExtents.Z <= 0
                || wall.HitBudget == 0 || wall.HitsLeft > wall.HitBudget || wall.Phase > RuntimeSolidPhase.Sinking
                || wall.Phase == RuntimeSolidPhase.Broken && wall.HitsLeft != 0)
                throw SnapshotReader.Malformed("Mayhem solid");
        }
        return walls;
    }
    internal void Apply(RuntimeSolid[] walls) { _walls.Clear(); _walls.AddRange(walls); }
}
