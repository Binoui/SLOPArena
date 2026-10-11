using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{

    /// <summary>
    /// Pure C# hitbox management — no Godot dependency.
    /// Spawn hitboxes via Spawn(), then call Tick() each server tick.
    /// Handles: movement (projectiles), sphere-sphere collision, aging, knockback.
    /// </summary>
    public class SpellResolver
    {
        private readonly List<Hitbox> _hitboxes = new();
        private readonly List<EntityData> _orderedEntities = new();
        private readonly ContactDistanceComparer _contactDistance = new();

        private sealed class ContactDistanceComparer : IComparer<EntityData>
        {
            public float X, Y, Z;

            private float SurfaceDistance(in EntityData entity)
            {
                float ux = entity.EndX - entity.PosX;
                float uy = entity.EndY - entity.PosY;
                float uz = entity.EndZ - entity.PosZ;
                float lengthSq = ux * ux + uy * uy + uz * uz;
                float t = lengthSq > 0.000001f
                    ? Math.Clamp(((X - entity.PosX) * ux + (Y - entity.PosY) * uy
                        + (Z - entity.PosZ) * uz) / lengthSq, 0f, 1f)
                    : 0f;
                float dx = X - entity.PosX - ux * t;
                float dy = Y - entity.PosY - uy * t;
                float dz = Z - entity.PosZ - uz * t;
                return MathF.Sqrt(dx * dx + dy * dy + dz * dz) - entity.Radius;
            }

            public int Compare(EntityData first, EntityData second)
            {
                int result = SurfaceDistance(in first).CompareTo(SurfaceDistance(in second));
                if (result != 0) return result;
                result = first.Id.CompareTo(second.Id);
                if (result != 0) return result;
                result = first.PosX.CompareTo(second.PosX);
                if (result != 0) return result;
                result = first.PosY.CompareTo(second.PosY);
                if (result != 0) return result;
                result = first.PosZ.CompareTo(second.PosZ);
                return result != 0 ? result : first.Radius.CompareTo(second.Radius);
            }
        }

        /// <summary>
        /// Projectile deactivation events this tick (for explosion spawning).
        /// Position is the last known position before deactivation.
        /// </summary>
        private readonly List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId, byte attackSlot, ulong activationId, bool airborne, byte attackSequence)> _pendingExplosions = new();
        /// <summary>Fired when any hitbox is removed. Args: the hitbox, its last position, removal reason.</summary>
        public Action<Hitbox, float, float, float>? OnHitboxRemoved;


        /// <summary>
        /// Result of a single hitbox-entity collision.
        /// </summary>
        public struct HitResult
        {
            public ulong TargetEntityId;
            public ulong OwnerEntityId;
            /// <summary>Resolved canonical attack slot that produced the hit.</summary>
            public byte AttackSlot;
            /// <summary>Unique server-local ability activation that produced the hit.</summary>
            public ulong ActivationId;
            /// <summary>Per-owner attack sequence captured when the hitbox was spawned.</summary>
            public byte AttackSequence;
            /// <summary>True when the originating ability activation began airborne.</summary>
            public bool Airborne;
            public float Damage;
            public float DirX;
            /// <summary>Launch angle in degrees (-90 to 90).</summary>
            public sbyte KnockbackAngle;
            public AuthoringKnockbackDirection KnockbackDirection;
            public float DirZ;
            public float BaseKnockback;
            public float KnockbackGrowth;
            public ushort StunTicks;
            /// <summary>Optional fixed hitstun override; zero uses the launch formula.</summary>
            public ushort FixedHitstunTicks;
            /// <summary>Snapshot of target armor at collision resolution.</summary>
            public bool ArmorProtected;
            /// <summary>If true, the hit freezes the owner too (melee contact, ADR-0012).</summary>
            public bool FreezesOwner;
            /// <summary>World-space contact point resolved from the colliding shapes.</summary>
            public float HitX, HitY, HitZ;
            /// <summary>Final launch magnitude resolved by ServerSimulation after ability hooks.</summary>
            public float ImpactForce;
            /// <summary>Hitstop applied to this accepted hit.</summary>
            public ushort HitstopTicks;
            /// <summary>True when this contact was blocked by an active shield.</summary>
            public bool Blocked;
            /// <summary>Authoritative simulation tick used by the stable block feedback key.</summary>
            public uint MatchTick;
            public BlockEventIdentity BlockEventIdentity =>
                new(MatchTick, OwnerEntityId, ActivationId, TargetEntityId);
        }

        /// <summary>Stable combat-block identity across prediction correction and feedback replay.</summary>
        public readonly record struct BlockEventIdentity(
            uint MatchTick, ulong OwnerEntityId, ulong ActivationId, ulong TargetEntityId);

        /// <summary>
        /// Entity data needed for hit detection.
        /// Supports Sphere and Capsule shapes.
        /// </summary>
        public struct EntityData
        {
            public ulong Id;
            public float PosX, PosY, PosZ;
            public float Radius;
            /// <summary>
            /// Sphere or Capsule
            /// </summary>
            public HitboxShape Shape;
            /// <summary>
            /// Capsule end (0 = sphere)
            /// </summary>
            public float EndX, EndY, EndZ;
            public ushort InvincibilityTicks;
            /// <summary>Shared shield collision surface, distinct from body/grab hurtboxes.</summary>
            public bool ShieldSurface;
            public bool Active;
        }

        /// <summary>
        /// Spawn a new hitbox. Call this from ability code.
        /// </summary>
        public void Spawn(Hitbox hb)
        {
            hb.Active = true;
            hb.AgeTicks = 0;
            if (hb.HitsMultipleOpponents && hb.HitEntities == null)
                hb.HitEntities = new HashSet<ulong>();
            _hitboxes.Add(hb);
        }

        /// <summary>
        /// Remove every active hitbox owned by one ability activation without firing
        /// projectile explosions. Used when the owning ability is cancelled before its
        /// effects are allowed to linger. A hit-history filter removes only that group.
        /// Gameplay cancellation preserves launched projectiles; snapshot replacement removes them.
        /// </summary>
        internal void RemoveOwnedHitboxes(ulong ownerId, ulong activationId, HashSet<ulong>? hitEntities = null,
            bool preserveProjectiles = false)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || hb.OwnerId != ownerId || hb.ActivationId != activationId)
                    continue;
                if (preserveProjectiles && !hb.CancelWithAbility)
                    continue;
                if (hitEntities != null && !ReferenceEquals(hb.HitEntities, hitEntities))
                    continue;

                hb.Active = false;
                OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                _hitboxes.RemoveAt(i);
            }
        }


        /// <summary>
        /// Clear all hitboxes (e.g., on match reset).
        /// </summary>
        public void Clear() => _hitboxes.Clear();

        /// <summary>
        /// Drain and return all pending explosion events from this tick.
        /// Call after Tick() to spawn explosion hitboxes at the returned positions.
        /// </summary>
        public List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId, byte attackSlot, ulong activationId, bool airborne, byte attackSequence)> DrainPendingExplosions()
        {
            var result = new List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId, byte attackSlot, ulong activationId, bool airborne, byte attackSequence)>(_pendingExplosions);
            _pendingExplosions.Clear();
            return result;
        }

        /// <summary>
        /// Check remaining active projectiles for ground collision.
        /// Samples the arena heightmap at each projectile's XZ position.
        /// If the projectile has reached or crossed the terrain surface,
        /// spawns the explosion at ground level and deactivates the projectile.
        /// </summary>
        public void CheckGroundCollision(ArenaDefinition arena)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Explosion.HasValue || hb.Gravity <= 0f) continue;

                // Sample ground height at projectile's current XZ
                float groundY = arena.Heightmap.Data != null && arena.Heightmap.Data.Length > 0
                    ? arena.Heightmap.Sample(hb.X, hb.Z)
                    : 0f;
                // Fallback for invalid sample (outside heightmap bounds)
                if (groundY == float.MinValue) groundY = 0f;

                if (hb.Y - hb.Radius > groundY) continue;

                // Ground contact: queue explosion at ground level, deactivate
                var exp = hb.Explosion.Value;
                _pendingExplosions.Add((hb.X, groundY, hb.Z, exp, hb.OwnerId, hb.AttackSlot,
                    hb.ActivationId, hb.ActivationAirborne, hb.AttackSequence));
                OnHitboxRemoved?.Invoke(hb, hb.X, groundY, hb.Z);
                _hitboxes.RemoveAt(i);
            }
        }

        /// <summary>
        /// Get a snapshot of all active hitboxes (for debug visualization).
        /// </summary>
        public List<Hitbox> GetActiveHitboxes()
        {
            var snapshot = new List<Hitbox>(_hitboxes.Count);
            foreach (var hb in _hitboxes)
            {
                if (hb.Active) snapshot.Add(hb);
            }
            return snapshot;
        }

        /// <summary>
        /// Re-resolve every bone-tracked hitbox to its owner's current bone position.
        /// Call once per tick, before Tick(), so melee hitboxes sweep with the limb
        /// instead of freezing at their spawn position. Projectiles and non-bone
        /// hitboxes are untouched (they carry their own velocity).
        /// </summary>
        public void UpdateBoneHitboxes(IReadOnlyDictionary<ulong, CharacterState> states)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.TracksBone || hb.Def == null) continue;
                if (!states.TryGetValue(hb.OwnerId, out var owner)) continue;
                if (owner.AttackSlot == 0) continue; // owner interrupted/ended — leave the lingering hitbox as-is

                HitboxGeometry.ResolvePositions(owner, hb.SourceEvent, hb.Baked, hb.Def,
                    hb.AnimationNames, hb.AnimIndex, hb.Slot, hb.Airborne,
                    out float wx, out float wy, out float wz,
                    out float wex, out float wey, out float wez);
                hb.X = wx; hb.Y = wy; hb.Z = wz;
                hb.EndX = wex; hb.EndY = wey; hb.EndZ = wez;
                _hitboxes[i] = hb;
            }
        }

        /// <summary>
        /// Process one tick: move projectiles, check collisions, age hitboxes.
        /// Returns all hits this tick (one per entity, first hitbox to connect wins).
        /// Already-hit entities in this tick are skipped (no double-hit).
        /// </summary>
        public List<HitResult> Tick(List<EntityData> entities)
        {
            var results = new List<HitResult>();
            var hitThisTick = new HashSet<ulong>();

            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active) { _hitboxes.RemoveAt(i); continue; }

                // Save position before movement (for explosion spawning)
                float prevX = hb.X, prevY = hb.Y, prevZ = hb.Z;

                // Move projectile
                hb.X += hb.VX * Simulation.TickDt;
                hb.Y += hb.VY * Simulation.TickDt;
                hb.Z += hb.VZ * Simulation.TickDt;

                // Apply gravity to projectile (if set)
                if (hb.Gravity > 0f)
                    hb.VY -= hb.Gravity * Simulation.TickDt;

                List<EntityData> candidates = entities;
                if (entities.Count > 1)
                {
                    bool hasShield = false;
                    foreach (var entity in entities)
                        if (entity.ShieldSurface) { hasShield = true; break; }
                    if (hasShield)
                    {
                        _orderedEntities.Clear();
                        _orderedEntities.AddRange(entities);
                        _contactDistance.X = prevX;
                        _contactDistance.Y = prevY;
                        _contactDistance.Z = prevZ;
                        _orderedEntities.Sort(_contactDistance);
                        candidates = _orderedEntities;
                    }
                }

                // Check collision against each entity
                foreach (var entity in candidates)
                {
                    if (!entity.Active) continue;
                    if (!hb.CanHitOwner && entity.Id == hb.OwnerId) continue;
                    if (hitThisTick.Contains(entity.Id)) continue;
                    if (entity.InvincibilityTicks > 0) continue;
                    bool hit = false;
                    float dist = 0f, dx = 0f, dy = 0f, dz = 0f;
                    float hitX = 0f, hitY = 0f, hitZ = 0f;

                    bool shieldSweep = entity.ShieldSurface && hb.Shape == HitboxShape.Sphere
                        && SweptShieldContact(in hb, in entity, prevX, prevY, prevZ,
                            out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ);

                    if (shieldSweep)
                    {
                        hit = true;
                    }
                    else if (hb.Shape == HitboxShape.Capsule || entity.Shape == HitboxShape.Capsule)
                    {
                        hit = CapsuleCollision(hb, entity, out dist, out dx, out dy, out dz,
                            out hitX, out hitY, out hitZ);
                    }
                    else
                    {
                        // Sphere-sphere (original)
                        dx = entity.PosX - hb.X;
                        dy = entity.PosY - hb.Y;
                        dz = entity.PosZ - hb.Z;
                        float distSq = (dx * dx) + (dy * dy) + (dz * dz);
                        float combinedRadius = hb.Radius + entity.Radius;
                        if (distSq <= combinedRadius * combinedRadius)
                        {
                            dist = MathF.Sqrt(distSq);
                            hit = true;
                            if (dist > 0.001f)
                            {
                                float invDist = 1f / dist;
                                hitX = hb.X + dx * invDist * hb.Radius;
                                hitY = hb.Y + dy * invDist * hb.Radius;
                                hitZ = hb.Z + dz * invDist * hb.Radius;
                            }
                            else
                            {
                                hitX = hb.X;
                                hitY = hb.Y;
                                hitZ = hb.Z;
                            }
                        }
                    }

                    if (hit && entity.ShieldSurface && !shieldSweep)
                        ShieldSurfaceContact(in hb, in entity, prevX, prevY, prevZ,
                            dist, dx, dy, dz, out hitX, out hitY, out hitZ);

                    if (hit)
                    {
                        // One-hit-per-opponent: skip entities this hitbox already tagged.
                        if (hb.HitsMultipleOpponents && hb.HitEntities!.Contains(entity.Id))
                            continue;

                        // Calculate knockback direction (normalized, no force multiplier)
                        float dirXNorm = dist > 0.001f ? (dx / dist) : 0f;
                        float dirZNorm = dist > 0.001f ? (dz / dist) : 0f;

                        // Adaptive auto-angle: derive the launch pitch from the hitbox→victim
                        // displacement instead of a fixed constant. dx/dz are the horizontal
                        // hitbox→victim vector; the vertical reference is the victim's hurtbox
                        // CENTER (midpoint of its capsule), matching Melee's 361° auto-angle —
                        // a grounded victim sends flat, an airborne one pops.
                        sbyte launchAngle = hb.KnockbackAngle;
                        if (launchAngle == KnockbackData.AdaptiveAngle)
                        {
                            float refY = (entity.PosY + entity.EndY) * 0.5f;
                            launchAngle = ComputeAdaptiveAngle(dx, refY - hb.Y, dz);
                        }

                        results.Add(new HitResult
                        {
                            TargetEntityId = entity.Id,
                            OwnerEntityId = hb.OwnerId,
                            AttackSlot = hb.AttackSlot,
                            ActivationId = hb.ActivationId,
                            Airborne = hb.ActivationAirborne,
                            Damage = hb.Damage,
                            AttackSequence = hb.AttackSequence,
                            DirX = dirXNorm,
                            KnockbackAngle = launchAngle,
                            KnockbackDirection = hb.KnockbackDirection,
                            FixedHitstunTicks = hb.FixedHitstunTicks,
                            DirZ = dirZNorm,
                            BaseKnockback = hb.BaseKnockback,
                            KnockbackGrowth = hb.KnockbackGrowth,
                            StunTicks = hb.StunTicks,
                            FreezesOwner = hb.FreezesOwner,
                            HitX = hitX,
                            HitY = hitY,
                            HitZ = hitZ,
                        });

                        hitThisTick.Add(entity.Id);
                        if (hb.HitsMultipleOpponents)
                        {
                            hb.HitEntities!.Add(entity.Id);
                            continue;   // tag every opponent once, expire at DurationTicks
                        }
                        hb.Active = false; // one-hit per hitbox (projectiles)
                        break;
                    }
                }

                // Age / expire
                hb.AgeTicks++;
                if (hb.AgeTicks >= hb.DurationTicks || !hb.Active)
                {
                    if (hb.Explosion.HasValue)
                        _pendingExplosions.Add((prevX, prevY, prevZ, hb.Explosion.Value, hb.OwnerId, hb.AttackSlot,
                            hb.ActivationId, hb.ActivationAirborne, hb.AttackSequence));
                    OnHitboxRemoved?.Invoke(hb, prevX, prevY, prevZ);
                    _hitboxes.RemoveAt(i);
                }
                else
                {
                    _hitboxes[i] = hb; // write back velocity/age changes
                }
            }

            return results;
        }

        private static bool SweptShieldContact(in Hitbox hb, in EntityData shield,
            float prevX, float prevY, float prevZ,
            out float dist, out float dx, out float dy, out float dz,
            out float hitX, out float hitY, out float hitZ)
        {
            dist = dx = dy = dz = hitX = hitY = hitZ = 0f;
            float vx = hb.X - prevX, vy = hb.Y - prevY, vz = hb.Z - prevZ;
            float travelSq = vx * vx + vy * vy + vz * vz;
            if (travelSq < 0.000001f) return false;
            float ox = prevX - shield.PosX, oy = prevY - shield.PosY, oz = prevZ - shield.PosZ;
            float reach = hb.Radius + shield.Radius;
            float c = ox * ox + oy * oy + oz * oz - reach * reach;
            float t = 0f;
            if (c > 0f)
            {
                float b = ox * vx + oy * vy + oz * vz;
                float discriminant = b * b - travelSq * c;
                if (discriminant < 0f) return false;
                t = (-b - MathF.Sqrt(discriminant)) / travelSq;
                if (t < 0f || t > 1f) return false;
            }
            float centerX = prevX + vx * t, centerY = prevY + vy * t,
                centerZ = prevZ + vz * t;
            dx = shield.PosX - centerX; dy = shield.PosY - centerY; dz = shield.PosZ - centerZ;
            dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            ShieldSurfaceContact(in hb, in shield, prevX, prevY, prevZ,
                dist, dx, dy, dz, out hitX, out hitY, out hitZ);
            return true;
        }

        private static void ShieldSurfaceContact(in Hitbox hb, in EntityData shield,
            float prevX, float prevY, float prevZ, float dist, float dx, float dy, float dz,
            out float hitX, out float hitY, out float hitZ)
        {
            if (dist > 0.0001f)
            {
                float scale = shield.Radius / dist;
                hitX = shield.PosX - dx * scale;
                hitY = shield.PosY - dy * scale;
                hitZ = shield.PosZ - dz * scale;
                return;
            }
            float nx = prevX - shield.PosX, ny = prevY - shield.PosY, nz = prevZ - shield.PosZ;
            float lengthSq = nx * nx + ny * ny + nz * nz;
            if (lengthSq < 0.000001f)
            {
                nx = -hb.VX; ny = -hb.VY; nz = -hb.VZ;
                lengthSq = nx * nx + ny * ny + nz * nz;
            }
            if (lengthSq < 0.000001f) { nx = 0f; ny = 0f; nz = 1f; lengthSq = 1f; }
            float radiusScale = shield.Radius / MathF.Sqrt(lengthSq);
            hitX = shield.PosX + nx * radiusScale;
            hitY = shield.PosY + ny * radiusScale;
            hitZ = shield.PosZ + nz * radiusScale;
        }

        /// <summary>
        /// Capsule vs Entity collision (handles all shape combos).
        /// Capsule = segment from (X,Y,Z) to (EndX,EndY,EndZ) with radius.
        /// Returns true if collision, with distance and direction vector.
        /// <summary>
        /// Adaptive auto-angle (Melee's 361°): derive a launch pitch from the
        /// hitbox→victim displacement instead of a fixed constant. The victim's height
        /// relative to the hitbox (dy) decides the send — an airborne victim above pops
        /// up (juggle), a level/grounded victim goes flat (clean neutral reset). The
        /// lower bound is clamped to 0 so a Adaptive move never buries a grounded
        /// opponent; full upward range is allowed for juggles. Spikes are authored
        /// separately with the fixed <see cref="KnockbackProfile.Spike"/> profile.
        /// Returns degrees, clamped to [0, 90].
        /// </summary>
        public static sbyte ComputeAdaptiveAngle(float dx, float dy, float dz)
        {
            float horiz = MathF.Sqrt((dx * dx) + (dz * dz));
            float deg = MathF.Atan2(dy, horiz) * (180f / MathF.PI);
            return (sbyte)Math.Clamp((int)MathF.Round(deg), 0, 90);
        }

        /// </summary>
        internal static bool CapsuleCollision(Hitbox hb, EntityData entity,
            out float dist, out float dx, out float dy, out float dz,
            out float hitX, out float hitY, out float hitZ)
        {
            // Get capsule endpoints for hitbox
            float hbStartX = hb.X, hbStartY = hb.Y, hbStartZ = hb.Z;
            float hbEndX = hb.Shape == HitboxShape.Capsule ? hb.EndX : hb.X;
            float hbEndY = hb.Shape == HitboxShape.Capsule ? hb.EndY : hb.Y;
            float hbEndZ = hb.Shape == HitboxShape.Capsule ? hb.EndZ : hb.Z;

            // Get capsule endpoints for entity
            float entStartX = entity.PosX, entStartY = entity.PosY, entStartZ = entity.PosZ;
            float entEndX = entity.Shape == HitboxShape.Capsule ? entity.EndX : entity.PosX;
            float entEndY = entity.Shape == HitboxShape.Capsule ? entity.EndY : entity.PosY;
            float entEndZ = entity.Shape == HitboxShape.Capsule ? entity.EndZ : entity.PosZ;

            // Find closest points between two segments
            ClosestPointsSegmentSegment(
                hbStartX, hbStartY, hbStartZ, hbEndX, hbEndY, hbEndZ,
                entStartX, entStartY, entStartZ, entEndX, entEndY, entEndZ,
                out float cx1, out float cy1, out float cz1,
                out float cx2, out float cy2, out float cz2);

            dx = cx2 - cx1;
            dy = cy2 - cy1;
            dz = cz2 - cz1;
            float distSq = (dx * dx) + (dy * dy) + (dz * dz);
            float combinedRadius = hb.Radius + entity.Radius;

            if (distSq <= combinedRadius * combinedRadius)
            {
                dist = MathF.Sqrt(distSq);
                if (dist > 0.001f)
                {
                    float invDist = 1f / dist;
                    hitX = cx1 + dx * invDist * hb.Radius;
                    hitY = cy1 + dy * invDist * hb.Radius;
                    hitZ = cz1 + dz * invDist * hb.Radius;
                }
                else
                {
                    hitX = cx1;
                    hitY = cy1;
                    hitZ = cz1;
                }
                return true;
            }

            dist = 0f;
            hitX = hitY = hitZ = 0f;
            return false;
        }

        /// <summary>
        /// Find closest points between two line segments.
        /// Uses the standard algorithm for segment-segment distance.
        /// </summary>
        private static void ClosestPointsSegmentSegment(
            float a0x, float a0y, float a0z, float a1x, float a1y, float a1z,
            float b0x, float b0y, float b0z, float b1x, float b1y, float b1z,
            out float cx, out float cy, out float cz,
            out float dx, out float dy, out float dz)
        {
            float d1x = a1x - a0x, d1y = a1y - a0y, d1z = a1z - a0z;
            float d2x = b1x - b0x, d2y = b1y - b0y, d2z = b1z - b0z;
            float rx = a0x - b0x, ry = a0y - b0y, rz = a0z - b0z;

            float a = (d1x * d1x) + (d1y * d1y) + (d1z * d1z); // |d1|²
            float e = (d2x * d2x) + (d2y * d2y) + (d2z * d2z); // |d2|²
            float f = (d2x * rx) + (d2y * ry) + (d2z * rz);

            float t = 0f, u = 0f;

            if (a <= 1e-6f && e <= 1e-6f)
            {
                // Both degenerate to points
                t = 0f; u = 0f;
            }
            else if (a <= 1e-6f)
            {
                // Segment 1 is a point
                u = Math.Clamp(f / e, 0f, 1f);
            }
            else
            {
                float c = (d1x * rx) + (d1y * ry) + (d1z * rz);

                if (e <= 1e-6f)
                {
                    // Segment 2 is a point
                    t = Math.Clamp(-c / a, 0f, 1f);
                }
                else
                {
                    float b = (d1x * d2x) + (d1y * d2y) + (d1z * d2z);
                    float denom = (a * e) - (b * b);

                    if (MathF.Abs(denom) > 1e-6f)
                    {
                        t = Math.Clamp(((b * f) - (c * e)) / denom, 0f, 1f);
                        u = Math.Clamp(((b * t) + f) / e, 0f, 1f);
                    }
                    else
                    {
                        t = 0f;
                        u = Math.Clamp(f / e, 0f, 1f);
                    }
                }
            }

            cx = a0x + (d1x * t);
            cy = a0y + (d1y * t);
            cz = a0z + (d1z * t);
            dx = b0x + (d2x * u);
            dy = b0y + (d2y * u);
            dz = b0z + (d2z * u);
        }
    }
}
