using System;
using System.Linq;

namespace SlopArena.Shared
{
    [Serializable]
    public struct SpawnPoint
    {
        public float X, Y, Z;
        /// <summary>
        /// facing direction in radians
        /// </summary>
        public float Yaw;
    }

    /// <summary>
    /// Precomputed 2D heightfield for fast ground-surface lookup.
    /// Built from collision triangles at bake time.
    /// Each cell stores the highest surface Y at that XZ position.
    /// Bilinear interpolation between 4 neighboring cells on Sample().
    /// </summary>
    [Serializable]
    public struct ArenaHeightmap
    {
        /// <summary>Number of cells along the X axis (columns).</summary>
        public int Width;
        /// <summary>Number of cells along the Z axis (rows).</summary>
        public int Height;
        /// <summary>World-space size of each cell in meters.</summary>
        public float CellSize;
        /// <summary>World-space X of the grid origin (min corner).</summary>
        public float OriginX;
        /// <summary>World-space Z of the grid origin (min corner).</summary>
        public float OriginZ;
        /// <summary>Height values, row-major: index = z * Width + x. float.MinValue = no surface.</summary>
        public float[] Data;

        /// <summary>
        /// Sample the ground surface Y at a world XZ position.
        /// Uses bilinear interpolation between the 4 nearest cells.
        /// Returns float.MinValue if outside the grid or no surface data.
        /// </summary>
        public readonly float Sample(float px, float pz)
        {
            if (Data == null || Width == 0 || Height == 0) return float.MinValue;

            float fx = (px - OriginX) / CellSize;
            float fz = (pz - OriginZ) / CellSize;

            int x0 = (int)MathF.Floor(fx);
            int z0 = (int)MathF.Floor(fz);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            if (x0 < 0 || x1 >= Width || z0 < 0 || z1 >= Height) return float.MinValue;

            float tx = fx - x0;
            float tz = fz - z0;

            float y00 = Data[z0 * Width + x0];
            float y10 = Data[z0 * Width + x1];
            float y01 = Data[z1 * Width + x0];
            float y11 = Data[z1 * Width + x1];

            // Treat MinValue cells as holes — skip in interpolation
            float top = float.MinValue, bot = float.MinValue;
            if (y00 > float.MinValue && y10 > float.MinValue) top = y00 + (y10 - y00) * tx;
            else if (y00 > float.MinValue) top = y00;
            else if (y10 > float.MinValue) top = y10;

            if (y01 > float.MinValue && y11 > float.MinValue) bot = y01 + (y11 - y01) * tx;
            else if (y01 > float.MinValue) bot = y01;
            else if (y11 > float.MinValue) bot = y11;

            if (top > float.MinValue && bot > float.MinValue) return top + (bot - top) * tz;
            if (top > float.MinValue) return top;
            if (bot > float.MinValue) return bot;
            return float.MinValue;
        }
    }

    /// <summary>
    /// A single collision triangle in world space.
    /// Three vertices forming a solid surface the server checks for character collision.
    /// All coordinates are in meters. Normal is computed at runtime.
    /// </summary>
    [Serializable]
    public struct CollisionTriangle
    {
        public float AX, AY, AZ;   // vertex A
        public float BX, BY, BZ;   // vertex B
        public float CX, CY, CZ;   // vertex C
    }

    /// <summary>
    /// Spatial grid for broad-phase collision queries.
    /// Pre-computed from CollisionTriangles at arena load time.
    /// Cells are uniform cubes over the arena bounding box.
    /// </summary>
    [Serializable]
    public struct CollisionGrid
    {
        /// <summary>Size of each cell in world units.</summary>
        public float CellSize;
        /// <summary>Number of cells along each axis.</summary>
        public int CellsX, CellsY, CellsZ;
        /// <summary>Origin of the grid (min corner of the covered volume).</summary>
        public float OriginX, OriginY, OriginZ;
        /// <summary>Prefix-sum into CellTriangles: cell i covers indices CellStarts[i]..CellStarts[i+1]-1.</summary>
        public int[] CellStarts;
        /// <summary>Triangle indices belonging to each cell.</summary>
        public int[] CellTriangles;
    }

    /// <summary>
    /// Pure data definition of an arena. No Godot types.
    /// Used by both client (ArenaManager) and server (for bounds/spawning).
    ///
    /// Blast Zone Design:
    /// - KillHeight defines the blast zone (Y coordinate below which = elimination)
    /// - Each map can have different blast zones for balance
    /// - Smaller stages typically have closer blast zones (easier to KO)
    /// - Larger stages can have farther blast zones (longer survival, more comeback potential)
    /// - Example: Final Destination (flat) vs Battlefield (platforms) in Smash
    /// </summary>
    [Serializable]
    public struct ArenaDefinition
    {
        /// <summary>
        /// machine-readable key
        /// </summary>
        public string Name;
        /// <summary>
        /// human-readable
        /// </summary>
        public string DisplayName;
        /// <summary>Hex color string for stage select card placeholder e.g. "#2a4a2a"</summary>
        public string PreviewColor;
        /// <summary>
        /// Y below this = BLAST ZONE (instant elimination)
        /// </summary>
        public float KillHeight;
        /// <summary>
        /// Y above this = BLAST ZONE (top kill line). 0 = auto: derived at resolve
        /// time from the heightmap's highest surface + <see cref="ArenaCollision.TopBlastMargin"/>.
        /// </summary>
        public float KillTop;
        /// <summary>
        /// X outside [KillMinX, KillMaxX] = BLAST ZONE (side kill lines). 0 = auto:
        /// derived at resolve time from the mesh bounds ± <see cref="ArenaCollision.SideBlastMargin"/>.
        /// </summary>
        public float KillMinX, KillMaxX;
        /// <summary>
        /// Z outside [KillMinZ, KillMaxZ] = BLAST ZONE (side kill lines). 0 = auto:
        /// derived at resolve time from the mesh bounds ± <see cref="ArenaCollision.SideBlastMargin"/>.
        /// </summary>
        public float KillMinZ, KillMaxZ;
        /// <summary>
        /// Precomputed heightmap for fast ground-surface lookup.
        /// Replaces PlatformDef[] + FloorHeight. Built from collision triangles at bake time.
        /// Null for hardcoded arenas that haven't been baked yet.
        /// </summary>
        public ArenaHeightmap Heightmap;
        /// <summary>
        /// arena bounds X (used for camera/mechanics)
        /// </summary>
        public float MinX, MaxX;
        /// <summary>
        /// arena bounds Z (used for camera/mechanics)
        /// </summary>
        public float MinZ, MaxZ;
        public SpawnPoint[] SpawnPoints;
        /// <summary>
        /// Collision triangle mesh for server-side stage collision.
        /// Characters collide with these triangles during movement and knockback.
        /// Baked from Unity scene colliders, loaded by server for client-side prediction + server authority.
        /// </summary>
        public CollisionTriangle[] CollisionTriangles;
        /// <summary>
        /// Pre-computed spatial grid for broad-phase collision queries.
        /// Built from CollisionTriangles when the arena is loaded.
        /// Null until BuildSpatialGrid() is called (happens in ArenaRegistry on arena load).
        /// </summary>
        public CollisionGrid SpatialGrid;
    }

    /// <summary>
    /// Utility methods for ArenaDefinition.
    /// </summary>
    public static class ArenaCollision
    {
        /// <summary>Distance beyond the stage mesh bounds for auto side blast lines.</summary>
        public const float SideBlastMargin = 10f;
        /// <summary>Height above the heightmap's highest surface for the auto top blast line.</summary>
        public const float TopBlastMargin = 20f;
        public readonly struct CollisionContact
        {
            public readonly float Time;
            public readonly float NormalX, NormalY, NormalZ;
            public readonly float Penetration;
            public readonly int TriangleIndex;

            public CollisionContact(float time, float normalX, float normalY, float normalZ,
                float penetration, int triangleIndex)
            {
                Time = time;
                NormalX = normalX;
                NormalY = normalY;
                NormalZ = normalZ;
                Penetration = penetration;
                TriangleIndex = triangleIndex;
            }
        }

        private readonly struct ClosestPoints
        {
            public readonly float SegmentX, SegmentY, SegmentZ;
            public readonly float TriangleX, TriangleY, TriangleZ;
            public readonly float Distance;
            public readonly float NormalX, NormalY, NormalZ;

            public ClosestPoints(float segmentX, float segmentY, float segmentZ,
                float triangleX, float triangleY, float triangleZ, float distance,
                float normalX, float normalY, float normalZ)
            {
                SegmentX = segmentX;
                SegmentY = segmentY;
                SegmentZ = segmentZ;
                TriangleX = triangleX;
                TriangleY = triangleY;
                TriangleZ = triangleZ;
                Distance = distance;
                NormalX = normalX;
                NormalY = normalY;
                NormalZ = normalZ;
            }
        }

        /// <summary>Returns true when the arena has authoritative triangle collision.</summary>
        public static bool HasTriangles(in ArenaDefinition arena)
            => arena.CollisionTriangles is { Length: > 0 };
        internal static bool TryGetTriangleNormal(int triangleIndex, in ArenaDefinition arena,
            out float normalX, out float normalY, out float normalZ)
        {
            normalX = normalY = normalZ = 0f;
            if (arena.CollisionTriangles == null
                || triangleIndex < 0 || triangleIndex >= arena.CollisionTriangles.Length)
                return false;
            var triangle = arena.CollisionTriangles[triangleIndex];
            float abx = triangle.BX - triangle.AX;
            float aby = triangle.BY - triangle.AY;
            float abz = triangle.BZ - triangle.AZ;
            float acx = triangle.CX - triangle.AX;
            float acy = triangle.CY - triangle.AY;
            float acz = triangle.CZ - triangle.AZ;
            normalX = aby * acz - abz * acy;
            normalY = abz * acx - abx * acz;
            normalZ = abx * acy - aby * acx;
            float length = MathF.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
            if (length <= 0.000001f)
            {
                normalX = normalY = normalZ = 0f;
                return false;
            }
            normalX /= length;
            normalY /= length;
            normalZ /= length;
            return true;
        }

        internal static bool IsUpwardFacingTriangle(int triangleIndex, in ArenaDefinition arena)
        {
            return TryGetTriangleNormal(triangleIndex, in arena,
                out _, out float normalY, out _) && normalY > 0.5f;
        }


        /// <summary>
        /// Returns unique triangles overlapping an AABB. The grid is optional: directly
        /// constructed synthetic arenas fall back to a deterministic linear scan.
        /// </summary>
        public static int GetCandidateTrianglesForAabb(
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ,
            in ArenaDefinition arena, int[] outIndices)
        {
            var triangles = arena.CollisionTriangles;
            if (triangles == null || triangles.Length == 0 || outIndices.Length == 0)
                return 0;

            var grid = arena.SpatialGrid;
            if (grid.CellStarts == null || grid.CellTriangles == null || grid.CellSize <= 0f
                || grid.CellsX <= 0 || grid.CellsY <= 0 || grid.CellsZ <= 0)
            {
                int scanCount = Math.Min(triangles.Length, outIndices.Length);
                for (int i = 0; i < scanCount; i++)
                    outIndices[i] = i;
                return scanCount;
            }

            int ixMin = (int)MathF.Floor((minX - grid.OriginX) / grid.CellSize);
            int ixMax = (int)MathF.Floor((maxX - grid.OriginX) / grid.CellSize);
            int iyMin = (int)MathF.Floor((minY - grid.OriginY) / grid.CellSize);
            int iyMax = (int)MathF.Floor((maxY - grid.OriginY) / grid.CellSize);
            int izMin = (int)MathF.Floor((minZ - grid.OriginZ) / grid.CellSize);
            int izMax = (int)MathF.Floor((maxZ - grid.OriginZ) / grid.CellSize);
            if (ixMin < 0) ixMin = 0;
            if (ixMax >= grid.CellsX) ixMax = grid.CellsX - 1;
            if (iyMin < 0) iyMin = 0;
            if (iyMax >= grid.CellsY) iyMax = grid.CellsY - 1;
            if (izMin < 0) izMin = 0;
            if (izMax >= grid.CellsZ) izMax = grid.CellsZ - 1;
            if (ixMin > ixMax || iyMin > iyMax || izMin > izMax)
                return 0;

            int count = 0;
            for (int iz = izMin; iz <= izMax; iz++)
            for (int iy = iyMin; iy <= iyMax; iy++)
            for (int ix = ixMin; ix <= ixMax; ix++)
            {
                int cell = iz * grid.CellsX * grid.CellsY + iy * grid.CellsX + ix;
                if (cell < 0 || cell + 1 >= grid.CellStarts.Length) continue;
                int start = grid.CellStarts[cell];
                int end = grid.CellStarts[cell + 1];
                for (int i = start; i < end && i < grid.CellTriangles.Length; i++)
                {
                    int triangleIndex = grid.CellTriangles[i];
                    if (triangleIndex < 0 || triangleIndex >= triangles.Length) continue;
                    bool duplicate = false;
                    for (int j = 0; j < count; j++)
                        if (outIndices[j] == triangleIndex) { duplicate = true; break; }
                    if (!duplicate && count < outIndices.Length)
                        outIndices[count++] = triangleIndex;
                }
            }
            return count;
        }

        public static int GetCandidateTrianglesForSweep(
            float startX, float startY, float startZ, float endX, float endY, float endZ,
            float radius, float capsuleHeight, in ArenaDefinition arena, int[] outIndices)
        {
            float halfLine = MathF.Max(0f, capsuleHeight * 0.5f - radius);
            return GetCandidateTrianglesForAabb(
                MathF.Min(startX, endX) - radius,
                MathF.Min(startY, endY) - halfLine - radius,
                MathF.Min(startZ, endZ) - radius,
                MathF.Max(startX, endX) + radius,
                MathF.Max(startY, endY) + halfLine + radius,
                MathF.Max(startZ, endZ) + radius,
                in arena, outIndices);
        }

        /// <summary>
        /// Sweeps a vertical capsule along a displacement and returns the first solid
        /// triangle contact. The fixed conservative-advancement and binary-refinement
        /// loops are deterministic and do not depend on frame rate or Unity physics.
        /// </summary>
        public static bool SweepCapsule(
            float startX, float startY, float startZ, float endX, float endY, float endZ,
            float radius, float capsuleHeight, in ArenaDefinition arena,
            int[] candidateIndices, int candidateCount, out CollisionContact contact)
        {
            contact = default;
            var triangles = arena.CollisionTriangles;
            if (triangles == null || candidateCount <= 0) return false;

            float dx = endX - startX, dy = endY - startY, dz = endZ - startZ;
            float displacementSq = dx * dx + dy * dy + dz * dz;
            const float tolerance = 0.0001f;
            // Keep the sweep boundary at the true capsule radius. Expanding it by the
            // numerical tolerance makes a nearby triangle edge a zero-time lateral
            // contact even when a coplanar neighboring triangle already supports the
            // capsule. The tolerance remains a classification margin below.
            float contactRadius = radius;
            float halfLine = MathF.Max(0f, capsuleHeight * 0.5f - radius);
            float bestTime = float.PositiveInfinity;
            int bestTriangle = int.MaxValue;
            ClosestPoints best = default;

            for (int c = 0; c < candidateCount; c++)
            {
                int triangleIndex = candidateIndices[c];
                if (triangleIndex < 0 || triangleIndex >= triangles.Length) continue;
                var triangle = triangles[triangleIndex];
                if (!TryCapsuleTriangleDistance(startX, startY, startZ, halfLine,
                        in triangle, out var atStart))
                    continue;

                float closingSpeed = -(dx * atStart.NormalX + dy * atStart.NormalY + dz * atStart.NormalZ);
                if (atStart.Distance <= contactRadius
                    && (atStart.Distance < radius - tolerance || closingSpeed > tolerance))
                {
                    if (0f < bestTime || (bestTime == 0f && triangleIndex < bestTriangle))
                    {
                        bestTime = 0f;
                        bestTriangle = triangleIndex;
                        best = atStart;
                    }
                    continue;
                }
                if (displacementSq <= 1e-12f) continue;

                float time = 0f;
                ClosestPoints previous = atStart;
                bool finished = false;
                for (int iteration = 0; iteration < 64 && time < 1f; iteration++)
                {
                    closingSpeed = -(dx * previous.NormalX + dy * previous.NormalY + dz * previous.NormalZ);
                    if (closingSpeed <= tolerance) { finished = true; break; }
                    float gap = previous.Distance - contactRadius;
                    float next = MathF.Min(1f, time + MathF.Max(0f, gap / closingSpeed));
                    if (next <= time)
                    {
                        RecordConservativeContact(time, previous, triangleIndex,
                            ref bestTime, ref bestTriangle, ref best);
                        finished = true;
                        break;
                    }
                    float nx = startX + dx * next;
                    float ny = startY + dy * next;
                    float nz = startZ + dz * next;
                    if (!TryCapsuleTriangleDistance(nx, ny, nz, halfLine,
                            in triangle, out var atNext))
                    {
                        finished = true;
                        break;
                    }
                    float nextClosingSpeed = -(dx * atNext.NormalX + dy * atNext.NormalY + dz * atNext.NormalZ);
                    if (atNext.Distance <= contactRadius && nextClosingSpeed > tolerance)
                    {
                        float lo = time, hi = next;
                        for (int refine = 0; refine < 20; refine++)
                        {
                            float mid = (lo + hi) * 0.5f;
                            TryCapsuleTriangleDistance(startX + dx * mid, startY + dy * mid,
                                startZ + dz * mid, halfLine, in triangle, out var atMid);
                            if (atMid.Distance <= contactRadius) hi = mid;
                            else lo = mid;
                        }
                        TryCapsuleTriangleDistance(startX + dx * hi, startY + dy * hi,
                            startZ + dz * hi, halfLine, in triangle, out var atHit);
                        RecordContact(hi, atHit, triangleIndex,
                            ref bestTime, ref bestTriangle, ref best);
                        finished = true;
                        break;
                    }
                    time = next;
                    previous = atNext;
                }
                if (!finished && time < 1f)
                    RecordConservativeContact(time, previous, triangleIndex,
                        ref bestTime, ref bestTriangle, ref best);
            }

            if (bestTriangle == int.MaxValue) return false;
            contact = new CollisionContact(bestTime, best.NormalX, best.NormalY, best.NormalZ,
                MathF.Max(0f, radius - best.Distance), bestTriangle);
            return true;
        }

        private static void RecordContact(float time, in ClosestPoints candidate, int triangleIndex,
            ref float bestTime, ref int bestTriangle, ref ClosestPoints best)
        {
            if (time < bestTime - 0.000001f
                || (MathF.Abs(time - bestTime) <= 0.000001f && triangleIndex < bestTriangle))
            {
                bestTime = time; bestTriangle = triangleIndex; best = candidate;
            }
        }

        private static void RecordConservativeContact(float time, in ClosestPoints candidate, int triangleIndex,
            ref float bestTime, ref int bestTriangle, ref ClosestPoints best)
            => RecordContact(time, candidate, triangleIndex, ref bestTime, ref bestTriangle, ref best);

        /// <summary>Pushes a penetrating capsule out along the nearest contact only.</summary>
        public static bool RecoverCapsule(
            ref float px, ref float py, ref float pz, float radius, float capsuleHeight,
            in ArenaDefinition arena, int[] candidateIndices, int maxIterations = 4,
            float maxCorrection = 0.5f)
        {
            bool changed = false;
            float corrected = 0f;
            for (int iteration = 0; iteration < maxIterations && corrected < maxCorrection; iteration++)
            {
                float halfLine = MathF.Max(0f, capsuleHeight * 0.5f - radius);
                int count = GetCandidateTrianglesForAabb(
                    px - radius, py - halfLine - radius, pz - radius,
                    px + radius, py + halfLine + radius, pz + radius,
                    in arena, candidateIndices);
                float deepest = 0f;
                int deepestTriangle = int.MaxValue;
                float nx = 0f, ny = 0f, nz = 0f;
                for (int c = 0; c < count; c++)
                {
                    int triangleIndex = candidateIndices[c];
                    var triangle = arena.CollisionTriangles[triangleIndex];
                    if (!TryCapsuleTriangleDistance(px, py, pz, halfLine,
                            in triangle, out var closest)) continue;
                    float penetration = radius - closest.Distance;
                    if (penetration > deepest + 0.000001f
                        || (MathF.Abs(penetration - deepest) <= 0.000001f
                            && triangleIndex < deepestTriangle))
                    {
                        deepest = penetration;
                        deepestTriangle = triangleIndex;
                        nx = closest.NormalX; ny = closest.NormalY; nz = closest.NormalZ;
                    }
                }
                if (deepest <= 0.0001f || deepestTriangle == int.MaxValue) break;
                float amount = MathF.Min(deepest + 0.0005f, maxCorrection - corrected);
                px += nx * amount;
                py += ny * amount;
                pz += nz * amount;
                corrected += amount;
                changed = true;
            }
            return changed;
        }

        public static bool TryFindSupport(
            float px, float py, float pz, float radius, float capsuleHeight,
            in ArenaDefinition arena, int[] candidateIndices,
            out CollisionContact support)
        {
            support = default;
            float halfLine = MathF.Max(0f, capsuleHeight * 0.5f - radius);
            int count = GetCandidateTrianglesForAabb(
                px - radius, py - halfLine - radius - 0.002f, pz - radius,
                px + radius, py + halfLine + radius + 0.002f, pz + radius,
                in arena, candidateIndices);
            bool found = false;
            for (int c = 0; c < count; c++)
            {
                int triangleIndex = candidateIndices[c];
                var triangle = arena.CollisionTriangles[triangleIndex];
                if (!TryCapsuleTriangleDistance(px, py, pz, halfLine,
                        in triangle, out var closest)
                    || !IsUpwardFacingTriangle(triangleIndex, in arena)
                    || closest.Distance > radius + 0.002f
                    || closest.NormalY <= 0.5f)
                    continue;
                var candidate = new CollisionContact(0f, closest.NormalX, closest.NormalY,
                    closest.NormalZ, MathF.Max(0f, radius - closest.Distance), triangleIndex);
                if (!found || triangleIndex < support.TriangleIndex)
                {
                    support = candidate;
                    found = true;
                }
            }
            return found;
        }

        internal static bool IsTriangleInteriorContact(
            float px, float py, float pz, float radius, float capsuleHeight,
            in ArenaDefinition arena, in CollisionContact contact)
        {
            int triangleIndex = contact.TriangleIndex;
            if (arena.CollisionTriangles == null
                || triangleIndex < 0 || triangleIndex >= arena.CollisionTriangles.Length)
                return false;
            float halfLine = MathF.Max(0f, capsuleHeight * 0.5f - radius);
            var triangle = arena.CollisionTriangles[triangleIndex];
            if (!TryCapsuleTriangleDistance(px, py, pz, halfLine,
                    in triangle, out var closest)
                || !TryGetTriangleNormal(triangleIndex, in arena,
                    out float normalX, out float normalY, out float normalZ))
                return false;
            return PointInTriangleInterior(closest.TriangleX, closest.TriangleY,
                closest.TriangleZ, in triangle, normalX, normalY, normalZ);
        }

        private static bool TryCapsuleTriangleDistance(
            float centerX, float centerY, float centerZ, float halfLine,
            in CollisionTriangle triangle, out ClosestPoints result)
        {
            float p0x = centerX, p0y = centerY - halfLine, p0z = centerZ;
            float p1x = centerX, p1y = centerY + halfLine, p1z = centerZ;
            float bestSquared = float.PositiveInfinity;
            float bestSx = 0f, bestSy = 0f, bestSz = 0f;
            float bestTx = 0f, bestTy = 0f, bestTz = 0f;

            void Consider(float sx, float sy, float sz, float tx, float ty, float tz)
            {
                float x = sx - tx, y = sy - ty, z = sz - tz;
                float squared = x * x + y * y + z * z;
                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    bestSx = sx; bestSy = sy; bestSz = sz;
                    bestTx = tx; bestTy = ty; bestTz = tz;
                }
            }

            ClosestPointOnTriangle(p0x, p0y, p0z, in triangle,
                out float p0tx, out float p0ty, out float p0tz);
            Consider(p0x, p0y, p0z, p0tx, p0ty, p0tz);
            ClosestPointOnTriangle(p1x, p1y, p1z, in triangle,
                out float p1tx, out float p1ty, out float p1tz);
            Consider(p1x, p1y, p1z, p1tx, p1ty, p1tz);

            SegmentSegmentClosest(p0x, p0y, p0z, p1x, p1y, p1z,
                triangle.AX, triangle.AY, triangle.AZ, triangle.BX, triangle.BY, triangle.BZ,
                out float sx, out float sy, out float sz, out float tx, out float ty, out float tz);
            Consider(sx, sy, sz, tx, ty, tz);
            SegmentSegmentClosest(p0x, p0y, p0z, p1x, p1y, p1z,
                triangle.BX, triangle.BY, triangle.BZ, triangle.CX, triangle.CY, triangle.CZ,
                out sx, out sy, out sz, out tx, out ty, out tz);
            Consider(sx, sy, sz, tx, ty, tz);
            SegmentSegmentClosest(p0x, p0y, p0z, p1x, p1y, p1z,
                triangle.CX, triangle.CY, triangle.CZ, triangle.AX, triangle.AY, triangle.AZ,
                out sx, out sy, out sz, out tx, out ty, out tz);
            Consider(sx, sy, sz, tx, ty, tz);

            float abx = triangle.BX - triangle.AX, aby = triangle.BY - triangle.AY, abz = triangle.BZ - triangle.AZ;
            float acx = triangle.CX - triangle.AX, acy = triangle.CY - triangle.AY, acz = triangle.CZ - triangle.AZ;
            float normalX = aby * acz - abz * acy;
            float normalY = abz * acx - abx * acz;
            float normalZ = abx * acy - aby * acx;
            float normalLength = MathF.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
            if (normalLength <= 0.000001f)
            {
                result = default;
                return false;
            }
            normalX /= normalLength; normalY /= normalLength; normalZ /= normalLength;
            float denom = normalX * (p1x - p0x) + normalY * (p1y - p0y) + normalZ * (p1z - p0z);
            if (MathF.Abs(denom) > 0.000001f)
            {
                float planeT = (normalX * (triangle.AX - p0x)
                    + normalY * (triangle.AY - p0y)
                    + normalZ * (triangle.AZ - p0z)) / denom;
                if (planeT >= 0f && planeT <= 1f)
                {
                    float ix = p0x + (p1x - p0x) * planeT;
                    float iy = p0y + (p1y - p0y) * planeT;
                    float iz = p0z + (p1z - p0z) * planeT;
                    if (PointInTriangle(ix, iy, iz, in triangle, normalX, normalY, normalZ))
                        Consider(ix, iy, iz, ix, iy, iz);
                }
            }

            float distance = MathF.Sqrt(bestSquared);
            float vx = bestSx - bestTx, vy = bestSy - bestTy, vz = bestSz - bestTz;
            if (distance > 0.000001f)
            {
                vx /= distance; vy /= distance; vz /= distance;
            }
            else
            {
                vx = normalX; vy = normalY; vz = normalZ;
                float toward = (centerX - bestTx) * vx + (centerY - bestTy) * vy
                    + (centerZ - bestTz) * vz;
                if (toward < 0f) { vx = -vx; vy = -vy; vz = -vz; }
            }
            result = new ClosestPoints(bestSx, bestSy, bestSz, bestTx, bestTy, bestTz,
                distance, vx, vy, vz);
            return true;
        }

        private static void ClosestPointOnTriangle(float px, float py, float pz,
            in CollisionTriangle t, out float x, out float y, out float z)
        {
            float abx = t.BX - t.AX, aby = t.BY - t.AY, abz = t.BZ - t.AZ;
            float acx = t.CX - t.AX, acy = t.CY - t.AY, acz = t.CZ - t.AZ;
            float apx = px - t.AX, apy = py - t.AY, apz = pz - t.AZ;
            float d1 = abx * apx + aby * apy + abz * apz;
            float d2 = acx * apx + acy * apy + acz * apz;
            if (d1 <= 0f && d2 <= 0f) { x = t.AX; y = t.AY; z = t.AZ; return; }
            float bpx = px - t.BX, bpy = py - t.BY, bpz = pz - t.BZ;
            float d3 = abx * bpx + aby * bpy + abz * bpz;
            float d4 = acx * bpx + acy * bpy + acz * bpz;
            if (d3 >= 0f && d4 <= d3) { x = t.BX; y = t.BY; z = t.BZ; return; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                x = t.AX + abx * v; y = t.AY + aby * v; z = t.AZ + abz * v; return;
            }
            float cpx = px - t.CX, cpy = py - t.CY, cpz = pz - t.CZ;
            float d5 = abx * cpx + aby * cpy + abz * cpz;
            float d6 = acx * cpx + acy * cpy + acz * cpz;
            if (d6 >= 0f && d5 <= d6) { x = t.CX; y = t.CY; z = t.CZ; return; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                x = t.AX + acx * w; y = t.AY + acy * w; z = t.AZ + acz * w; return;
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                x = t.BX + (t.CX - t.BX) * w;
                y = t.BY + (t.CY - t.BY) * w;
                z = t.BZ + (t.CZ - t.BZ) * w;
                return;
            }
            float denominator = 1f / (va + vb + vc);
            float vFace = vb * denominator, wFace = vc * denominator;
            x = t.AX + abx * vFace + acx * wFace;
            y = t.AY + aby * vFace + acy * wFace;
            z = t.AZ + abz * vFace + acz * wFace;
        }

        private static void SegmentSegmentClosest(
            float p1x, float p1y, float p1z, float q1x, float q1y, float q1z,
            float p2x, float p2y, float p2z, float q2x, float q2y, float q2z,
            out float c1x, out float c1y, out float c1z, out float c2x, out float c2y, out float c2z)
        {
            float d1x = q1x - p1x, d1y = q1y - p1y, d1z = q1z - p1z;
            float d2x = q2x - p2x, d2y = q2y - p2y, d2z = q2z - p2z;
            float rx = p1x - p2x, ry = p1y - p2y, rz = p1z - p2z;
            float a = d1x * d1x + d1y * d1y + d1z * d1z;
            float e = d2x * d2x + d2y * d2y + d2z * d2z;
            float f = d2x * rx + d2y * ry + d2z * rz;
            float s, t;
            if (a <= 0.000001f && e <= 0.000001f) { s = t = 0f; }
            else if (a <= 0.000001f)
            {
                s = 0f; t = Math.Clamp(f / e, 0f, 1f);
            }
            else
            {
                float c = d1x * rx + d1y * ry + d1z * rz;
                if (e <= 0.000001f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else
                {
                    float b = d1x * d2x + d1y * d2y + d1z * d2z;
                    float denominator = a * e - b * b;
                    s = denominator != 0f ? Math.Clamp((b * f - c * e) / denominator, 0f, 1f) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                    else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
                }
            }
            c1x = p1x + d1x * s; c1y = p1y + d1y * s; c1z = p1z + d1z * s;
            c2x = p2x + d2x * t; c2y = p2y + d2y * t; c2z = p2z + d2z * t;
        }

        private static bool PointInTriangle(float px, float py, float pz,
            in CollisionTriangle t, float nx, float ny, float nz)
        {
            float abx = t.BX - t.AX, aby = t.BY - t.AY, abz = t.BZ - t.AZ;
            float bcx = t.CX - t.BX, bcy = t.CY - t.BY, bcz = t.CZ - t.BZ;
            float cax = t.AX - t.CX, cay = t.AY - t.CY, caz = t.AZ - t.CZ;
            float apx = px - t.AX, apy = py - t.AY, apz = pz - t.AZ;
            float bpx = px - t.BX, bpy = py - t.BY, bpz = pz - t.BZ;
            float cpx = px - t.CX, cpy = py - t.CY, cpz = pz - t.CZ;
            float c1x = aby * apz - abz * apy, c1y = abz * apx - abx * apz, c1z = abx * apy - aby * apx;
            float c2x = bcy * bpz - bcz * bpy, c2y = bcz * bpx - bcx * bpz, c2z = bcx * bpy - bcy * bpx;
            float c3x = cay * cpz - caz * cpy, c3y = caz * cpx - cax * cpz, c3z = cax * cpy - cay * cpx;
            float d1 = c1x * nx + c1y * ny + c1z * nz;
            float d2 = c2x * nx + c2y * ny + c2z * nz;
            float d3 = c3x * nx + c3y * ny + c3z * nz;
            return d1 >= -0.0001f && d2 >= -0.0001f && d3 >= -0.0001f;
        }

        private static bool PointInTriangleInterior(float px, float py, float pz,
            in CollisionTriangle t, float nx, float ny, float nz)
        {
            float abx = t.BX - t.AX, aby = t.BY - t.AY, abz = t.BZ - t.AZ;
            float bcx = t.CX - t.BX, bcy = t.CY - t.BY, bcz = t.CZ - t.BZ;
            float cax = t.AX - t.CX, cay = t.AY - t.CY, caz = t.AZ - t.CZ;
            float apx = px - t.AX, apy = py - t.AY, apz = pz - t.AZ;
            float bpx = px - t.BX, bpy = py - t.BY, bpz = pz - t.BZ;
            float cpx = px - t.CX, cpy = py - t.CY, cpz = pz - t.CZ;
            float c1x = aby * apz - abz * apy, c1y = abz * apx - abx * apz, c1z = abx * apy - aby * apx;
            float c2x = bcy * bpz - bcz * bpy, c2y = bcz * bpx - bcx * bpz, c2z = bcx * bpy - bcy * bpx;
            float c3x = cay * cpz - caz * cpy, c3y = caz * cpx - cax * cpz, c3z = cax * cpy - cay * cpx;
            float d1 = c1x * nx + c1y * ny + c1z * nz;
            float d2 = c2x * nx + c2y * ny + c2z * nz;
            float d3 = c3x * nx + c3y * ny + c3z * nz;
            return d1 > 0.0001f && d2 > 0.0001f && d3 > 0.0001f;
        }



        /// <summary>
        /// Resolved blast-zone planes for one arena. Inactive planes are ±infinity,
        /// so the death check can compare unconditionally.
        /// </summary>
        public readonly struct BlastLines
        {
            public readonly float KillHeight, KillTop, KillMinX, KillMaxX, KillMinZ, KillMaxZ;

            public BlastLines(float killHeight, float killTop,
                float killMinX, float killMaxX, float killMinZ, float killMaxZ)
            {
                KillHeight = killHeight;
                KillTop = killTop;
                KillMinX = killMinX;
                KillMaxX = killMaxX;
                KillMinZ = killMinZ;
                KillMaxZ = killMaxZ;
            }
        }

        /// <summary>
        /// Resolve the arena's effective blast lines. Authored values (nonzero) win;
        /// unset lines (0 — the struct default) are derived: sides from the mesh bounds
        /// ± <see cref="SideBlastMargin"/>, top from the heightmap's highest surface +
        /// <see cref="TopBlastMargin"/>. Arenas without meaningful bounds (zero bounds —
        /// test fixtures, unbaked arenas) keep void-only death: side/top resolve to ±infinity.
        /// </summary>
        public static BlastLines ResolveBlastLines(in ArenaDefinition arena)
        {
            bool hasBounds = !(arena.MinX == 0f && arena.MaxX == 0f
                && arena.MinZ == 0f && arena.MaxZ == 0f);

            float killTop = arena.KillTop != 0f ? arena.KillTop
                : hasBounds && arena.Heightmap.Data is { Length: > 0 }
                    ? arena.Heightmap.Data.Max() + TopBlastMargin
                    : float.PositiveInfinity;
            float killMinX = arena.KillMinX != 0f ? arena.KillMinX
                : hasBounds ? arena.MinX - SideBlastMargin : float.NegativeInfinity;
            float killMaxX = arena.KillMaxX != 0f ? arena.KillMaxX
                : hasBounds ? arena.MaxX + SideBlastMargin : float.PositiveInfinity;
            float killMinZ = arena.KillMinZ != 0f ? arena.KillMinZ
                : hasBounds ? arena.MinZ - SideBlastMargin : float.NegativeInfinity;
            float killMaxZ = arena.KillMaxZ != 0f ? arena.KillMaxZ
                : hasBounds ? arena.MaxZ + SideBlastMargin : float.PositiveInfinity;

            return new BlastLines(arena.KillHeight, killTop, killMinX, killMaxX, killMinZ, killMaxZ);
        }

        /// <summary>
        /// Build a spatial grid from the arena's collision triangles.
        /// Grid cells are cubes of the given size. Each triangle is assigned to every cell
        /// its bounding box overlaps. The result is stored in arena.SpatialGrid.
        /// If CollisionTriangles is null or empty, SpatialGrid is left as default.
        /// </summary>
        public static CollisionGrid BuildSpatialGrid(in ArenaDefinition arena, float cellSize = 4f)
        {
            if (arena.CollisionTriangles == null || arena.CollisionTriangles.Length == 0)
                return default;

            var tris = arena.CollisionTriangles;

            // Compute grid dimensions from arena bounds + triangle extent
            float minY = float.MaxValue, maxY = float.MinValue;
            float minX = arena.MinX, maxX = arena.MaxX;
            float minZ = arena.MinZ, maxZ = arena.MaxZ;
            for (int i = 0; i < tris.Length; i++)
            {
                var t = tris[i];
                float tMinY = t.AY; if (t.BY < tMinY) tMinY = t.BY; if (t.CY < tMinY) tMinY = t.CY;
                float tMaxY = t.AY; if (t.BY > tMaxY) tMaxY = t.BY; if (t.CY > tMaxY) tMaxY = t.CY;
                if (tMinY < minY) minY = tMinY;
                if (tMaxY > maxY) maxY = tMaxY;
            }

            // Ensure min height spans the full expected play area
            if (minY > arena.KillHeight) minY = arena.KillHeight;
            float surfaceMaxY = arena.Heightmap.Data != null && arena.Heightmap.Data.Length > 0
                ? arena.Heightmap.Data.Max()
                : arena.KillHeight + 20f;
            if (maxY < surfaceMaxY + 20f) maxY = surfaceMaxY + 20f;

            int cellsX = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cellSize));
            int cellsY = Math.Max(1, (int)MathF.Ceiling((maxY - minY) / cellSize));
            int cellsZ = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / cellSize));
            int totalCells = cellsX * cellsY * cellsZ;

            // Count triangles per cell (first pass) and assign (second pass)
            var counts = new int[totalCells];

            for (int ti = 0; ti < tris.Length; ti++)
            {
                var t = tris[ti];
                float tMinX = t.AX; if (t.BX < tMinX) tMinX = t.BX; if (t.CX < tMinX) tMinX = t.CX;
                float tMaxX = t.AX; if (t.BX > tMaxX) tMaxX = t.BX; if (t.CX > tMaxX) tMaxX = t.CX;
                float tMinY = t.AY; if (t.BY < tMinY) tMinY = t.BY; if (t.CY < tMinY) tMinY = t.CY;
                float tMaxY = t.AY; if (t.BY > tMaxY) tMaxY = t.BY; if (t.CY > tMaxY) tMaxY = t.CY;
                float tMinZ = t.AZ; if (t.BZ < tMinZ) tMinZ = t.BZ; if (t.CZ < tMinZ) tMinZ = t.CZ;
                float tMaxZ = t.AZ; if (t.BZ > tMaxZ) tMaxZ = t.BZ; if (t.CZ > tMaxZ) tMaxZ = t.CZ;

                int ixMin = (int)((tMinX - minX) / cellSize);
                int ixMax = (int)((tMaxX - minX) / cellSize);
                int iyMin = (int)((tMinY - minY) / cellSize);
                int iyMax = (int)((tMaxY - minY) / cellSize);
                int izMin = (int)((tMinZ - minZ) / cellSize);
                int izMax = (int)((tMaxZ - minZ) / cellSize);
                ClampRange(ref ixMin, ref ixMax, 0, cellsX - 1);
                ClampRange(ref iyMin, ref iyMax, 0, cellsY - 1);
                ClampRange(ref izMin, ref izMax, 0, cellsZ - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                    for (int iy = iyMin; iy <= iyMax; iy++)
                        for (int ix = ixMin; ix <= ixMax; ix++)
                            counts[iz * cellsX * cellsY + iy * cellsX + ix]++;
            }

            // Build prefix sum
            var starts = new int[totalCells + 1];
            for (int i = 0; i < totalCells; i++)
                starts[i + 1] = starts[i] + counts[i];

            var triIndices = new int[starts[totalCells]];
            var nextSlot = new int[totalCells];
            Array.Copy(starts, 0, nextSlot, 0, totalCells);
            // nextSlot[i] now tracks the current write position for cell i
            // (starts[i] is the start, nextSlot[i] advanced during assignment)

            for (int ti = 0; ti < tris.Length; ti++)
            {
                var t = tris[ti];
                float tMinX = t.AX; if (t.BX < tMinX) tMinX = t.BX; if (t.CX < tMinX) tMinX = t.CX;
                float tMaxX = t.AX; if (t.BX > tMaxX) tMaxX = t.BX; if (t.CX > tMaxX) tMaxX = t.CX;
                float tMinY = t.AY; if (t.BY < tMinY) tMinY = t.BY; if (t.CY < tMinY) tMinY = t.CY;
                float tMaxY = t.AY; if (t.BY > tMaxY) tMaxY = t.BY; if (t.CY > tMaxY) tMaxY = t.CY;
                float tMinZ = t.AZ; if (t.BZ < tMinZ) tMinZ = t.BZ; if (t.CZ < tMinZ) tMinZ = t.CZ;
                float tMaxZ = t.AZ; if (t.BZ > tMaxZ) tMaxZ = t.BZ; if (t.CZ > tMaxZ) tMaxZ = t.CZ;

                int ixMin = (int)((tMinX - minX) / cellSize);
                int ixMax = (int)((tMaxX - minX) / cellSize);
                int iyMin = (int)((tMinY - minY) / cellSize);
                int iyMax = (int)((tMaxY - minY) / cellSize);
                int izMin = (int)((tMinZ - minZ) / cellSize);
                int izMax = (int)((tMaxZ - minZ) / cellSize);
                ClampRange(ref ixMin, ref ixMax, 0, cellsX - 1);
                ClampRange(ref iyMin, ref iyMax, 0, cellsY - 1);
                ClampRange(ref izMin, ref izMax, 0, cellsZ - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                    for (int iy = iyMin; iy <= iyMax; iy++)
                        for (int ix = ixMin; ix <= ixMax; ix++)
                        {
                            int cell = iz * cellsX * cellsY + iy * cellsX + ix;
                            triIndices[nextSlot[cell]++] = ti;
                        }
            }

            Console.WriteLine($"[ArenaCollision] Built spatial grid for '{arena.Name}': " +
                $"{tris.Length} triangles, {totalCells} cells ({cellsX}×{cellsY}×{cellsZ}), " +
                $"avg {starts[totalCells] / (float)totalCells:F1} tri/cell");

            return new CollisionGrid
            {
                CellSize = cellSize,
                CellsX = cellsX,
                CellsY = cellsY,
                CellsZ = cellsZ,
                OriginX = minX,
                OriginY = minY,
                OriginZ = minZ,
                CellStarts = starts,
                CellTriangles = triIndices,
            };
        }

        private static void ClampRange(ref int lo, ref int hi, int min, int max)
        {
            // A wall on the grid's maximum plane belongs to the last cell.
            // Clamping only hi left lo one past the grid and dropped that wall.
            lo = Math.Clamp(lo, min, max);
            hi = Math.Clamp(hi, min, max);
        }
    }

    public static class ArenaRegistry
    {
        private static ArenaDefinition[] _loaded = System.Array.Empty<ArenaDefinition>();

        public static ArenaDefinition[] All => _loaded;

        /// <summary>
        /// Load arena definitions from .arena files in a directory, replacing the
        /// current list. A missing/empty directory or a directory with no parseable
        /// files leaves the list empty — callers fail loud, there is no hardcoded
        /// fallback. Call once at startup, e.g.: ArenaRegistry.LoadFromDirectory("data/arenas");
        /// </summary>
        public static void LoadFromDirectory(string directoryPath)
        {
            try
            {
                if (!System.IO.Directory.Exists(directoryPath))
                {
                    Console.WriteLine($"[ArenaRegistry] Directory not found: {directoryPath} — no arenas loaded");
                    _loaded = System.Array.Empty<ArenaDefinition>();
                    return;
                }

                var files = System.IO.Directory.GetFiles(directoryPath, "*.arena");
                if (files.Length == 0)
                {
                    Console.WriteLine($"[ArenaRegistry] No .arena files in {directoryPath} — no arenas loaded");
                    _loaded = System.Array.Empty<ArenaDefinition>();
                    return;
                }

                var loaded = new System.Collections.Generic.List<ArenaDefinition>();
                foreach (var file in files)
                {
                    var arena = ArenaBinaryFormat.LoadFromFile(file);
                    if (arena.HasValue)
                    {
                        var a = arena.Value;
                        loaded.Add(a);
                        Console.WriteLine($"[ArenaRegistry] Loaded: {a.Name} ({file})");
                    }
                    else
                    {
                        Console.WriteLine($"[ArenaRegistry] Failed to load: {file}");
                    }
                }

                _loaded = loaded.ToArray();
                Console.WriteLine($"[ArenaRegistry] Loaded {loaded.Count} arenas from {directoryPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArenaRegistry] Error loading from {directoryPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Look up an arena by name. Returns null when the name is unknown —
        /// callers must NOT silently substitute another arena (issue #77).
        /// </summary>
        public static ArenaDefinition? Get(string name)
        {
            foreach (var a in _loaded)
                if (a.Name == name) return a;
            return null;
        }
    }
}
