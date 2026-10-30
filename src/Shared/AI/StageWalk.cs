using System;

namespace SlopArena.Shared.AI;

/// <summary>How a probed grounded walk ends.</summary>
internal enum WalkEnd : byte
{
    /// <summary>The engine's mover covered a walkable prefix long enough for the next input.</summary>
    Clear,
    /// <summary>The walk left its floor and the fall landed on a lower floor.</summary>
    Drop,
    /// <summary>A steep face stopped the walk, with walkable ground on top within the asked rise.</summary>
    Ledge,
    /// <summary>A steep face stopped the walk and has no top within the asked rise.</summary>
    Wall,
    /// <summary>The walk left the arena bounds, or its fall found no floor.</summary>
    Hazard,
}

internal readonly struct WalkVerdict
{
    public readonly WalkEnd End;
    /// <summary>Horizontal metres covered along the heading before the walk ended.</summary>
    public readonly float Covered;
    /// <summary>Drop: depth down to the landing floor. Ledge: rise up to the top. Otherwise 0.</summary>
    public readonly float Height;
    /// <summary>Wall and Ledge: horizontal unit normal of the face, pointing back at the walker.</summary>
    public readonly float NormalX, NormalZ;

    public WalkVerdict(WalkEnd end, float covered, float height = 0f, float normalX = 0f, float normalZ = 0f)
    {
        End = end;
        Covered = MathF.Max(0f, covered);
        Height = MathF.Max(0f, height);
        NormalX = normalX;
        NormalZ = normalZ;
    }
}

/// <summary>
/// Walks a scratch copy of a grounded fighter along one heading with the simulation's own mover, so the
/// CPU's walk check and the engine agree on slopes, crests, platform edges, walls and lower floors.
/// Heightmap-only arenas follow the engine's heightmap ground rule. Deterministic; allocates only when an
/// arena with more triangles than the reusable buffer is first probed.
/// </summary>
internal static class StageWalk
{
    internal const int DenseTriangleThreshold = 1024;
    [ThreadStatic]
    private static int[]? _candidates;

    public static WalkVerdict Probe(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena,
        float dirX, float dirZ, float length, float maxRise)
    {
        if (arena.MaxX <= arena.MinX || arena.MaxZ <= arena.MinZ)
            return new WalkVerdict(WalkEnd.Clear, length);
        return ArenaCollision.HasTriangles(arena)
            ? WalkTriangles(self, def, arena, dirX, dirZ, length, maxRise)
            : WalkHeightmap(self, def, arena, dirX, dirZ, length);
    }

    private static WalkVerdict WalkTriangles(CharacterState s, CharacterDefinition def, in ArenaDefinition arena,
        float dirX, float dirZ, float length, float maxRise)
    {
        float startY = s.PY;
        s.VX = s.VY = s.VZ = 0f;
        s.KVX = s.KVY = s.KVZ = 0f;
        s.IsGrounded = true;
        s.IsFastFalling = false;
        s.InPostHitstunFlight = false;
        float increment = MathF.Max(0.05f, MathF.Min(def.Movement.RunSpeed * Simulation.TickDt, def.CapsuleRadius));
        float covered = 0f;
        int moves = 0;
        int maxMoves = arena.CollisionTriangles!.Length > DenseTriangleThreshold ? 3 : 64;
        while (covered < length - 0.0001f && moves++ < maxMoves)
        {
            float step = MathF.Min(increment, length - covered);
            float x0 = s.PX, z0 = s.PZ;
            Simulation.MoveThroughStage(ref s, def, arena, dirX * step, 0f, dirZ * step, out float wallX, out float wallZ);
            float progress = (s.PX - x0) * dirX + (s.PZ - z0) * dirZ;
            covered += MathF.Max(0f, progress);
            if (OutsideBounds(s.PX, s.PZ, def, arena))
                return new WalkVerdict(WalkEnd.Hazard, covered);
            if (!s.IsGrounded)
                return Fall(s, startY, def, arena, dirX, dirZ, covered);
            if (progress < step * 0.5f)
            {
                if (wallX == 0f && wallZ == 0f)
                {
                    wallX = -dirX;
                    wallZ = -dirZ;
                }
                return TryFindTop(s, def, arena, dirX, dirZ, maxRise, out float rise)
                    ? new WalkVerdict(WalkEnd.Ledge, covered, rise, wallX, wallZ)
                    : new WalkVerdict(WalkEnd.Wall, covered, 0f, wallX, wallZ);
            }
        }
        return new WalkVerdict(WalkEnd.Clear, covered);
    }

    /// <summary>A grazing rim contact is not a landing. Use ordinary air drift and require interior floor support.</summary>
    private static WalkVerdict Fall(CharacterState s, float startY, CharacterDefinition def,
        in ArenaDefinition arena, float dirX, float dirZ, float covered)
    {
        s.VX = dirX * def.Movement.RunSpeed;
        s.VZ = dirZ * def.Movement.RunSpeed;
        int contacts = 0;
        int[] candidates = Candidates(arena);
        for (int tick = 0; tick < 90; tick++)
        {
            Simulation.ProcessAirMovement(ref s, def.Movement, default, dirX, dirZ);
            Simulation.ApplyGravity(ref s, def.Movement, 1f);
            float x = s.PX + s.VX * Simulation.TickDt;
            float y = s.PY + s.VY * Simulation.TickDt;
            float z = s.PZ + s.VZ * Simulation.TickDt;
            int count = ArenaCollision.GetCandidateTrianglesForSweep(s.PX, s.PY, s.PZ, x, y, z,
                def.CapsuleRadius, def.CapsuleHeight, in arena, candidates);
            if (!ArenaCollision.SweepCapsule(s.PX, s.PY, s.PZ, x, y, z, def.CapsuleRadius,
                    def.CapsuleHeight, in arena, candidates, count, out _))
            {
                s.PX = x;
                s.PY = y;
                s.PZ = z;
            }
            else
            {
                if (++contacts > 4) return new WalkVerdict(WalkEnd.Hazard, covered);
                Simulation.MoveThroughStage(ref s, def, arena,
                    s.VX * Simulation.TickDt, s.VY * Simulation.TickDt, s.VZ * Simulation.TickDt);
            }
            if (s.PY < arena.KillHeight || OutsideBounds(s.PX, s.PZ, def, arena))
                return new WalkVerdict(WalkEnd.Hazard, covered);
            if (s.IsGrounded && HasLanding(s, def, arena))
                return new WalkVerdict(WalkEnd.Drop, covered, startY - s.PY);
        }
        return new WalkVerdict(WalkEnd.Hazard, covered);
    }

    internal static bool HasLanding(in CharacterState s, CharacterDefinition def, in ArenaDefinition arena)
        => !ArenaCollision.HasTriangles(arena)
            || ArenaCollision.TryFindSupport(s.PX, s.PY, s.PZ, def.CapsuleRadius, def.CapsuleHeight,
                in arena, Candidates(arena), out var support)
            && ArenaCollision.IsTriangleInteriorContact(s.PX, s.PY, s.PZ, def.CapsuleRadius,
                def.CapsuleHeight, in arena, support);

    /// <summary>Sweeps the capsule down from <paramref name="maxRise"/> above the floor, one radius past the face.</summary>
    private static bool TryFindTop(in CharacterState stopped, CharacterDefinition def, in ArenaDefinition arena,
        float dirX, float dirZ, float maxRise, out float rise)
    {
        rise = 0f;
        if (maxRise <= 0f)
            return false;
        Candidates(arena);
        float x = stopped.PX + dirX * def.CapsuleRadius * 2f;
        float z = stopped.PZ + dirZ * def.CapsuleRadius * 2f;
        float top = stopped.PY + maxRise;
        int count = ArenaCollision.GetCandidateTrianglesForSweep(x, top, z, x, stopped.PY, z,
            def.CapsuleRadius, def.CapsuleHeight, in arena, _candidates);
        if (!ArenaCollision.SweepCapsule(x, top, z, x, stopped.PY, z, def.CapsuleRadius, def.CapsuleHeight,
                in arena, _candidates, count, out var contact)
            || contact.NormalY <= 0.5f || contact.Time <= 0f)
            return false;
        rise = maxRise * (1f - contact.Time);
        return true;
    }

    /// <summary>The engine's heightmap path: higher cells are climbed, a lower cell is fallen to.</summary>
    private static WalkVerdict WalkHeightmap(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena,
        float dirX, float dirZ, float length)
    {
        float start = arena.Heightmap.Sample(self.PX, self.PZ);
        if (start == float.MinValue)
            start = self.PY - def.CapsuleHeight * 0.5f;
        float floor = start;
        float increment = MathF.Max(0.05f, MathF.Min(def.Movement.RunSpeed * Simulation.TickDt, def.CapsuleRadius));
        float covered = 0f;
        while (covered < length - 0.0001f)
        {
            float step = MathF.Min(increment, length - covered);
            float x = self.PX + dirX * (covered + step), z = self.PZ + dirZ * (covered + step);
            if (OutsideBounds(x, z, def, arena))
                return new WalkVerdict(WalkEnd.Hazard, covered);
            float height = arena.Heightmap.Sample(x, z);
            if (height == float.MinValue)
                return new WalkVerdict(WalkEnd.Hazard, covered);
            if (height < floor - Simulation.PlatformSnapTolerance)
                return new WalkVerdict(WalkEnd.Drop, covered + step, start - height);
            floor = height;
            covered += step;
        }
        return new WalkVerdict(WalkEnd.Clear, covered);
    }

    private static int[] Candidates(in ArenaDefinition arena)
    {
        int required = arena.CollisionTriangles!.Length;
        if (_candidates == null || _candidates.Length < required)
            _candidates = new int[required];
        return _candidates;
    }

    private static bool OutsideBounds(float x, float z, CharacterDefinition def, in ArenaDefinition arena)
        => x < arena.MinX + def.CapsuleRadius || x > arena.MaxX - def.CapsuleRadius
            || z < arena.MinZ + def.CapsuleRadius || z > arena.MaxZ - def.CapsuleRadius;
}
