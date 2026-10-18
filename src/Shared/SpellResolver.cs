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
        private readonly List<Hitbox> _lastTickChecked = new();
        private readonly List<EntityData> _orderedEntities = new();
        private readonly ContactDistanceComparer _contactDistance = new();

        /// <summary>
        /// Diagnostics only: every hitbox exactly as the latest <see cref="Tick"/> tested it,
        /// including hitboxes that connected or expired during that tick. Gameplay never reads
        /// it; telemetry uses it to measure how close an attack came to its target.
        /// </summary>
        public IReadOnlyList<Hitbox> LastTickCheckedHitboxes => _lastTickChecked;

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
            /// <summary>Contact fraction within the tick, including moving Arcade spheres.</summary>
            public float ContactTime;
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
            /// <summary>Continuous sphere path; null retains the ordinary fighter path.</summary>
            public IReadOnlyList<SphereMotionSegment>? MotionPath;
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
        public void Clear()
        {
            _hitboxes.Clear();
            _lastTickChecked.Clear();
        }

        /// <summary>World Snapshot: live hitboxes in resolution order and undrained explosions.
        /// The last tick's checked list is presentation output and is not captured.</summary>
        internal void WriteSnapshot(SnapshotWriter w,
            Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
        {
            w.WriteInt(_hitboxes.Count);
            foreach (var hb in _hitboxes)
                SnapshotCodecs.Write(w, hb, ownerContent);
            w.WriteInt(_pendingExplosions.Count);
            foreach (var (x, y, z, explosion, ownerId, attackSlot, activationId, airborne, attackSequence) in _pendingExplosions)
            {
                w.WriteFloat(x);
                w.WriteFloat(y);
                w.WriteFloat(z);
                SnapshotCodecs.Write(w, explosion);
                w.WriteULong(ownerId);
                w.WriteByte(attackSlot);
                w.WriteULong(activationId);
                w.WriteBool(airborne);
                w.WriteByte(attackSequence);
            }
        }

        /// <summary>Parsed resolver state, applied only after the whole snapshot decoded.</summary>
        internal sealed class SnapshotData
        {
            public readonly List<Hitbox> Hitboxes = new();
            public readonly List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId, byte attackSlot, ulong activationId, bool airborne, byte attackSequence)> Explosions = new();
        }

        internal static SnapshotData ReadSnapshot(SnapshotReader r,
            Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
        {
            var data = new SnapshotData();
            int hitboxes = r.ReadCount(1);
            for (int i = 0; i < hitboxes; i++)
                data.Hitboxes.Add(SnapshotCodecs.ReadHitbox(r, ownerContent));
            int explosions = r.ReadCount(1);
            for (int i = 0; i < explosions; i++)
            {
                float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
                var explosion = SnapshotCodecs.ReadProjectileExplosion(r);
                data.Explosions.Add((x, y, z, explosion, r.ReadULong(), r.ReadByte(), r.ReadULong(),
                    r.ReadBool(), r.ReadByte()));
            }
            return data;
        }

        internal void ApplySnapshot(SnapshotData data)
        {
            _hitboxes.Clear();
            _lastTickChecked.Clear();
            _pendingExplosions.Clear();
            _hitboxes.AddRange(data.Hitboxes);
            _pendingExplosions.AddRange(data.Explosions);
        }

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
        /// Call once per tick, before Tick(), so melee hitboxes follow the limb instead
        /// of freezing at their spawn position. From a hitbox's second active tick on,
        /// <see cref="Hitbox.Sweep"/> also holds the poses it passed through since the
        /// previous tick, which Tick() tests before the current pose. Projectiles and
        /// non-bone hitboxes are untouched (they carry their own velocity).
        /// </summary>
        public void UpdateBoneHitboxes(IReadOnlyDictionary<ulong, CharacterState> states)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.TracksBone || hb.Def == null) continue;
                if (!states.TryGetValue(hb.OwnerId, out var owner)) continue;
                if (owner.AttackSlot == 0)
                {
                    // Owner interrupted/ended — leave the lingering hitbox as-is.
                    if (hb.Sweep != null) { hb.Sweep = null; _hitboxes[i] = hb; }
                    continue;
                }

                HitboxGeometry.ResolvePositions(owner, hb.SourceEvent, hb.Baked, hb.Def,
                    hb.AnimationNames, hb.AnimIndex, hb.Slot, hb.Airborne,
                    out float wx, out float wy, out float wz,
                    out float wex, out float wey, out float wez);
                var current = new HitboxPose { X = wx, Y = wy, Z = wz, EndX = wex, EndY = wey, EndZ = wez };
                hb.Sweep = hb.HasSweepOrigin
                    ? HitboxGeometry.SweepPoses(hb.SourceEvent, hb.Baked, hb.Def, hb.AnimationNames,
                        hb.AnimIndex, hb.Slot, hb.Airborne, in hb.SweepOrigin, in owner, in current)
                    : null;
                hb.X = wx; hb.Y = wy; hb.Z = wz;
                hb.EndX = wex; hb.EndY = wey; hb.EndZ = wez;
                hb.SweepOrigin = new HitboxSweepOrigin
                {
                    PX = owner.PX, PY = owner.PY, PZ = owner.PZ,
                    FacingYaw = owner.FacingYaw, AttackPosePitch = owner.AttackPosePitch,
                    AttackElapsedTicks = owner.AttackElapsedTicks, ComboStage = owner.ComboStage,
                    Pose = current,
                };
                hb.HasSweepOrigin = true;
                _hitboxes[i] = hb;
            }
        }

        /// <summary>
        /// Process one tick: move projectiles, check collisions, age hitboxes.
        /// Returns all hits this tick (one per entity, first hitbox to connect wins).
        /// Already-hit entities in this tick are skipped (no double-hit).
        /// Melee windows (<see cref="Hitbox.CancelWithAbility"/>) of <paramref name="frozenOwners"/>
        /// do not age: hitstop pauses the owner's move (ADR-0012), so a freeze cannot use up the
        /// move's other live windows. They still test contact. Projectiles keep their own time.
        /// </summary>
        public List<HitResult> Tick(List<EntityData> entities, bool orderMovingTargetContacts = false,
            HashSet<ulong>? frozenOwners = null)
        {
            var results = new List<HitResult>();
            var hitThisTick = new HashSet<ulong>();
            _lastTickChecked.Clear();
            if (orderMovingTargetContacts) OrderMovingTargetContacts(entities);

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
                    // One-hit-per-opponent: skip entities this hitbox already tagged.
                    if (hb.HitsMultipleOpponents && hb.HitEntities!.Contains(entity.Id))
                        continue;

                    // A swept bone hitbox tests the poses it passed through since the
                    // previous tick, earliest first, then its current pose. The first
                    // pose in contact supplies the contact point and launch geometry.
                    var contact = hb;
                    bool hit = false;
                    float dist = 0f, dx = 0f, dy = 0f, dz = 0f;
                    float hitX = 0f, hitY = 0f, hitZ = 0f;
                    float contactTime = 1f;
                    float movingTargetY = entity.PosY;
                    if (hb.Sweep != null)
                    {
                        for (int p = 0; p < hb.Sweep.Length && !hit; p++)
                        {
                            var probe = hb;
                            var pose = hb.Sweep[p];
                            probe.X = pose.X; probe.Y = pose.Y; probe.Z = pose.Z;
                            probe.EndX = pose.EndX; probe.EndY = pose.EndY; probe.EndZ = pose.EndZ;
                            hit = TryContact(in probe, in entity, probe.X, probe.Y, probe.Z,
                                out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ,
                                out contactTime, out movingTargetY);
                            if (hit) contact = probe;
                        }
                    }
                    if (!hit)
                        hit = TryContact(in hb, in entity, prevX, prevY, prevZ,
                            out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ,
                            out contactTime, out movingTargetY);

                    if (hit)
                    {
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
                            float refY = entity.MotionPath != null ? movingTargetY : (entity.PosY + entity.EndY) * 0.5f;
                            launchAngle = ComputeAdaptiveAngle(dx, refY - contact.Y, dz);
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
                            ContactTime = contactTime,
                        });

                        hitThisTick.Add(entity.Id);
                        if (hb.HitsMultipleOpponents)
                        {
                            hb.HitEntities!.Add(entity.Id);
                            continue;   // tag every opponent once, expire at DurationTicks
                        }
                        if (entity.MotionPath != null)
                        {
                            // Detached explosion/removal events originate at the actual
                            // swept impact, rather than the projectile's start or overshoot.
                            prevX += (hb.X - prevX) * contactTime;
                            prevY += (hb.Y - prevY) * contactTime;
                            prevZ += (hb.Z - prevZ) * contactTime;
                        }
                        hb.Active = false; // one-hit per hitbox (projectiles)
                        break;
                    }
                }
                _lastTickChecked.Add(hb);

                // Age / expire
                if (!hb.CancelWithAbility || frozenOwners == null || !frozenOwners.Contains(hb.OwnerId))
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

        /// <summary>
        /// Contact test between one hitbox pose and one entity: moving Arcade spheres,
        /// swept shield contact for spheres, capsule and sphere-sphere overlap. Shield
        /// surfaces report their contact point on the shield.
        /// </summary>
        private static bool TryContact(in Hitbox hb, in EntityData entity, float prevX, float prevY, float prevZ,
            out float dist, out float dx, out float dy, out float dz, out float hitX, out float hitY, out float hitZ,
            out float contactTime, out float movingTargetY)
        {
            bool hit = false;
            dist = dx = dy = dz = 0f;
            hitX = hitY = hitZ = 0f;
            contactTime = 1f;
            movingTargetY = entity.PosY;

            bool shieldSweep = entity.ShieldSurface && hb.Shape == HitboxShape.Sphere
                && SweptShieldContact(in hb, in entity, prevX, prevY, prevZ,
                    out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ);

            if (entity.MotionPath != null)
            {
                hit = MovingSphereContact(in hb, in entity, prevX, prevY, prevZ,
                    out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ,
                    out contactTime, out movingTargetY);
            }
            else if (shieldSweep)
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
            return hit;
        }

        private void OrderMovingTargetContacts(List<EntityData> entities)
        {
            EntityData? moving = null;
            foreach (var entity in entities)
                if (entity.Active && entity.MotionPath != null) { moving = entity; break; }
            if (!moving.HasValue) return;
            var target = moving.Value;
            var ordered = new List<(Hitbox Box, int Order, float Time)>(_hitboxes.Count);
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var original = _hitboxes[i]; var predicted = original;
                predicted.X += predicted.VX * Simulation.TickDt;
                predicted.Y += predicted.VY * Simulation.TickDt;
                predicted.Z += predicted.VZ * Simulation.TickDt;
                float time = float.PositiveInfinity;
                if (original.Active && (original.CanHitOwner || original.OwnerId != target.Id)
                    && (!original.HitsMultipleOpponents || !original.HitEntities!.Contains(target.Id)))
                    if (MovingSphereContact(in predicted, in target, original.X, original.Y, original.Z,
                        out _, out _, out _, out _, out _, out _, out _, out float candidateTime, out _)) time = candidateTime;
                ordered.Add((original, i, time));
            }
            // Tick iterates backwards. Equal-time contacts preserve its ordinary insertion priority.
            ordered.Sort((a, b) => { int time = b.Time.CompareTo(a.Time); return time != 0 ? time : a.Order.CompareTo(b.Order); });
            _hitboxes.Clear(); foreach (var item in ordered) _hitboxes.Add(item.Box);
        }

        private static bool MovingSphereContact(in Hitbox hb, in EntityData entity,
            float prevX, float prevY, float prevZ, out float dist,
            out float dx, out float dy, out float dz, out float hitX, out float hitY, out float hitZ,
            out float contactTime, out float targetY)
        {
            dist = dx = dy = dz = hitX = hitY = hitZ = targetY = 0f;
            contactTime = 1f;
            float hvx = hb.X - prevX, hvy = hb.Y - prevY, hvz = hb.Z - prevZ;
            foreach (var segment in entity.MotionPath!)
            {
                float span = segment.EndTime - segment.StartTime;
                float sx = prevX + hvx * segment.StartTime - segment.Start.X;
                float sy = prevY + hvy * segment.StartTime - segment.Start.Y;
                float sz = prevZ + hvz * segment.StartTime - segment.Start.Z;
                float vx = hvx * span - (segment.End.X - segment.Start.X);
                float vy = hvy * span - (segment.End.Y - segment.Start.Y);
                float vz = hvz * span - (segment.End.Z - segment.Start.Z);
                float t = 0f;
                bool contact = false;
                if (hb.Shape == HitboxShape.Sphere)
                {
                    float radius = hb.Radius + entity.Radius;
                    float c = sx * sx + sy * sy + sz * sz - radius * radius;
                    float a = vx * vx + vy * vy + vz * vz;
                    if (c <= 0f) contact = true;
                    else if (a > 0.00000001f)
                    {
                        float b = sx * vx + sy * vy + sz * vz;
                        float discriminant = b * b - a * c;
                        if (discriminant >= 0f)
                        {
                            t = (-b - MathF.Sqrt(discriminant)) / a;
                            contact = t >= 0f && t <= 1f;
                        }
                    }
                }
                else
                {
                    // The moving sphere versus a melee capsule uses the existing
                    // closest-segment primitive with conservative advancement.
                    float speed = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                    float lengthX = hb.EndX - hb.X, lengthY = hb.EndY - hb.Y, lengthZ = hb.EndZ - hb.Z;
                    float lengthSq = lengthX * lengthX + lengthY * lengthY + lengthZ * lengthZ;
                    for (int iteration = 0; iteration < 64 && t <= 1f; iteration++)
                    {
                        float px = -sx - vx * t, py = -sy - vy * t, pz = -sz - vz * t;
                        float projection = lengthSq > 0.000001f
                            ? Math.Clamp((px * lengthX + py * lengthY + pz * lengthZ) / lengthSq, 0f, 1f) : 0f;
                        float cx = px - lengthX * projection, cy = py - lengthY * projection, cz = pz - lengthZ * projection;
                        float gap = MathF.Sqrt(cx * cx + cy * cy + cz * cz) - hb.Radius - entity.Radius;
                        if (gap <= 0.0001f) { contact = true; break; }
                        if (speed <= 0.000001f) break;
                        t += gap / speed;
                    }
                }
                if (!contact) continue;
                contactTime = segment.StartTime + span * t;
                var point = segment.Sample(contactTime);
                var sample = entity;
                sample.PosX = sample.EndX = point.X;
                sample.PosY = sample.EndY = point.Y;
                sample.PosZ = sample.EndZ = point.Z;
                var attack = hb;
                float atX = prevX + hvx * contactTime, atY = prevY + hvy * contactTime, atZ = prevZ + hvz * contactTime;
                attack.EndX += atX - attack.X; attack.EndY += atY - attack.Y; attack.EndZ += atZ - attack.Z;
                attack.X = atX; attack.Y = atY; attack.Z = atZ;
                targetY = point.Y;
                // Reuse the contact-point calculation at the conservative sweep boundary.
                CapsuleCollision(attack, sample, out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ);
                return true;
            }
            return false;
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

        /// <summary>
        /// Diagnostics: signed surface gap in metres between a hitbox and one entity volume,
        /// zero or negative when they overlap. The out vector runs from the hitbox surface to
        /// the entity surface along their closest points, and is zero when they overlap.
        /// </summary>
        public static float SurfaceGap(in Hitbox hb, in EntityData entity,
            out float gapX, out float gapY, out float gapZ)
        {
            bool hbCapsule = hb.Shape == HitboxShape.Capsule;
            bool entityCapsule = entity.Shape == HitboxShape.Capsule;
            ClosestPointsSegmentSegment(
                hb.X, hb.Y, hb.Z,
                hbCapsule ? hb.EndX : hb.X, hbCapsule ? hb.EndY : hb.Y, hbCapsule ? hb.EndZ : hb.Z,
                entity.PosX, entity.PosY, entity.PosZ,
                entityCapsule ? entity.EndX : entity.PosX, entityCapsule ? entity.EndY : entity.PosY,
                entityCapsule ? entity.EndZ : entity.PosZ,
                out float cx1, out float cy1, out float cz1,
                out float cx2, out float cy2, out float cz2);
            float dx = cx2 - cx1, dy = cy2 - cy1, dz = cz2 - cz1;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            float gap = distance - (hb.Radius + entity.Radius);
            if (gap <= 0f || distance <= 0.000001f)
            {
                gapX = gapY = gapZ = 0f;
                return gap;
            }
            float scale = gap / distance;
            gapX = dx * scale;
            gapY = dy * scale;
            gapZ = dz * scale;
            return gap;
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
