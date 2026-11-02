using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{

    /// <summary>
    /// Pure C# hitbox management — no Godot dependency.
    /// Spawn hitboxes via Spawn(), then call Tick() each server tick.
    /// Handles: movement (projectiles), sphere-sphere collision, aging, knockback.
    /// </summary>
    public partial class SpellResolver
    {
        private readonly List<Hitbox> _hitboxes = new();
        private ulong _lastEmissionId;
        private readonly List<Hitbox> _lastTickChecked = new();
        private readonly List<EntityData> _orderedEntities = new();
        private readonly ContactDistanceComparer _contactDistance = new();

        /// <summary>
        /// Diagnostics only: every hitbox exactly as the latest <see cref="Tick"/> tested it,
        /// including hitboxes that connected or expired during that tick. Gameplay never reads
        /// it; telemetry uses it to measure how close an attack came to its target.
        /// </summary>
        public IReadOnlyList<Hitbox> LastTickCheckedHitboxes => _lastTickChecked;

        private readonly List<Contact> _contacts = new();
        private readonly record struct Contact(EntityData Entity, Hitbox Pose, int Order,
            float Distance, float DX, float DY, float DZ, float X, float Y, float Z,
            float Time, float TargetY);

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
            /// <summary>The attacker's form whose move produced the hit (ADR-0051).</summary>
            public byte Form;
            /// <summary>Returning projectile leg: 0 none, 1 flying out, 2 coming back to its owner.</summary>
            public byte ReturnPhase;
            public ushort ChallengeTicks;
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
            /// <summary>True when a fighter-state window (her ride's wave, ADR-0050) made the contact.</summary>
            public bool OwnerWindow;
            /// <summary>World-space contact point resolved from the colliding shapes.</summary>
            public float HitX, HitY, HitZ;
            /// <summary>The hitbox's own hit presentation (ADR-0053), or null for the slot's. Not on the wire.</summary>
            public string? HitPresentationId;
            /// <summary>Final launch magnitude resolved by ServerSimulation after ability hooks.</summary>
            public float ImpactForce;
            /// <summary>Hitstop applied to this accepted hit.</summary>
            public ushort HitstopTicks;
            /// <summary>True when this contact was blocked by an active shield.</summary>
            public bool Blocked;
            public bool Unblockable;
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
            public bool ProtectedByShield;
            public bool RuntimeSolid;
            public bool Active;
            /// <summary>Continuous sphere path; null retains the ordinary fighter path.</summary>
            public IReadOnlyList<SphereMotionSegment>? MotionPath;
        }

        /// <summary>
        /// Spawn a new hitbox. Call this from ability code.
        /// </summary>
        public void Spawn(Hitbox hb)
        {
            hb.EmissionId = _lastEmissionId = checked(_lastEmissionId + 1);
            hb.Active = true;
            hb.AgeTicks = 0;
            if (hb.HitsMultipleOpponents && hb.HitEntities == null)
                hb.HitEntities = new HashSet<ulong>();
            if (hb.MinePhase != MinePhase.None)
            {
                int count = 0, oldest = -1;
                for (int i = 0; i < _hitboxes.Count; i++)
                    if (_hitboxes[i].OwnerId == hb.OwnerId && _hitboxes[i].MinePhase != MinePhase.None)
                    {
                        count++;
                        if (oldest < 0 || _hitboxes[i].EmissionId < _hitboxes[oldest].EmissionId) oldest = i;
                    }
                if (count >= hb.MineMaxCount && oldest >= 0) _hitboxes.RemoveAt(oldest);
            }
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

        internal bool HasOwnerWindow(ulong ownerId)
        {
            foreach (var hitbox in _hitboxes)
                if (hitbox.OwnerWindow && hitbox.Active && hitbox.OwnerId == ownerId) return true;
            return false;
        }

        /// <summary>Who her live wave holds (ADR-0050); empty when she has none.</summary>
        internal IReadOnlyCollection<ulong> OwnerWindowHits(ulong ownerId)
        {
            foreach (var hitbox in _hitboxes)
                if (hitbox.OwnerWindow && hitbox.Active && hitbox.OwnerId == ownerId && hitbox.HitEntities != null)
                    return hitbox.HitEntities;
            return System.Array.Empty<ulong>();
        }

        /// <summary>Drops one contact from her wave's hit set, so a refused catch can be tried again later.</summary>
        internal void ForgetOwnerWindowContact(ulong ownerId, ulong entityId)
        {
            foreach (var hitbox in _hitboxes)
                if (hitbox.OwnerWindow && hitbox.OwnerId == ownerId)
                    hitbox.HitEntities?.Remove(entityId);
        }

        /// <summary>Drops one fighter from every wave but one, so a carried fighter has a single carrier.</summary>
        internal void ForgetOwnerWindowContacts(ulong entityId, ulong exceptOwnerId)
        {
            foreach (var hitbox in _hitboxes)
                if (hitbox.OwnerWindow && hitbox.OwnerId != exceptOwnerId)
                    hitbox.HitEntities?.Remove(entityId);
        }

        internal void RemoveOwnerWindow(ulong ownerId)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hitbox = _hitboxes[i];
                if (!hitbox.OwnerWindow || hitbox.OwnerId != ownerId) continue;
                hitbox.Active = false;
                OnHitboxRemoved?.Invoke(hitbox, hitbox.X, hitbox.Y, hitbox.Z);
                _hitboxes.RemoveAt(i);
            }
        }

        internal void RemoveOwnerEffects(ulong ownerId)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hitbox = _hitboxes[i];
                if (hitbox.OwnerId != ownerId) continue;
                hitbox.Active = false;
                OnHitboxRemoved?.Invoke(hitbox, hitbox.X, hitbox.Y, hitbox.Z);
                _hitboxes.RemoveAt(i);
            }
            _pendingExplosions.RemoveAll(explosion => explosion.ownerId == ownerId);
            RemoveContactBombs(ownerId);
        }


        private int[] _returnSweepTriangles = Array.Empty<int>();

        /// <summary>
        /// Steers returning projectiles before the collision pass. On the way out a projectile keeps its
        /// launch velocity until its outbound time ends or it would meet the stage; then it turns: a fresh
        /// hit history for the return leg, knockback toward its owner, and the owner becomes hittable (the
        /// simulation decides catch or self-hit). Coming back it steers at <paramref name="ownerTarget"/>'s
        /// point, its velocity changing by at most the return acceleration each tick, so it slows, swings
        /// round and homes in. A projectile whose owner is gone disappears.
        /// </summary>
        internal void UpdateReturningProjectiles(
            Func<ulong, (bool Exists, float X, float Y, float Z)> ownerTarget, in ArenaDefinition arena)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Return.HasValue) continue;
                var target = ownerTarget(hb.OwnerId);
                if (!target.Exists)
                {
                    hb.Active = false;
                    OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                    _hitboxes.RemoveAt(i);
                    continue;
                }
                var config = hb.Return.Value;
                if (hb.LegTicks < ushort.MaxValue) hb.LegTicks++;
                if (!hb.ReturnLeg && MeetsStage(in hb, in arena, out float nx, out float ny, out float nz))
                {
                    // It bounces off the stage: reflect off the surface at half speed, then home in.
                    float along = hb.VX * nx + hb.VY * ny + hb.VZ * nz;
                    hb.VX = (hb.VX - 2f * along * nx) * BounceRetention;
                    hb.VY = (hb.VY - 2f * along * ny) * BounceRetention;
                    hb.VZ = (hb.VZ - 2f * along * nz) * BounceRetention;
                    Turn(ref hb);
                }
                else if (!hb.ReturnLeg && hb.LegTicks >= config.OutboundTicks)
                    Turn(ref hb);
                if (hb.ReturnLeg)
                {
                    float dx = target.X - hb.X, dy = target.Y - hb.Y, dz = target.Z - hb.Z;
                    float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    float scale = distance > 0.0001f ? config.ReturnSpeed / distance : 0f;
                    float ax = dx * scale - hb.VX, ay = dy * scale - hb.VY, az = dz * scale - hb.VZ;
                    float change = MathF.Sqrt(ax * ax + ay * ay + az * az);
                    float limit = config.ReturnAcceleration * Simulation.TickDt;
                    if (change > limit && change > 0f)
                    {
                        ax *= limit / change; ay *= limit / change; az *= limit / change;
                    }
                    hb.VX += ax; hb.VY += ay; hb.VZ += az;
                }
                _hitboxes[i] = hb;
            }
        }

        internal bool HasOwnerEffects(ulong ownerId)
            => _hitboxes.Exists(h => h.Active && h.OwnerId == ownerId)
                || _pendingExplosions.Exists(e => e.ownerId == ownerId);

        internal void UpdateStickyProjectiles(Func<ulong, (bool Exists, float X, float Y, float Z)> carrierPosition)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (hb.AttachedEntityId == 0) continue;
                var carrier = carrierPosition(hb.AttachedEntityId);
                if (!carrier.Exists) { ExplodeSticky(i, hb); continue; }
                hb.X = carrier.X; hb.Y = carrier.Y; hb.Z = carrier.Z;
                _hitboxes[i] = hb;
            }
        }

        internal void ExplodeAttachedTo(ulong carrierId)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
                if (_hitboxes[i].AttachedEntityId == carrierId) ExplodeSticky(i, _hitboxes[i]);
        }

        private void ExplodeSticky(int index, Hitbox hb)
        {
            // A perch only pops (ADR-0051): its fuse and its carrier's KO remove it silently.
            if (hb.Explosion.HasValue && !hb.Perch.HasValue)
                _pendingExplosions.Add((hb.X, hb.Y, hb.Z, hb.Explosion.Value, hb.OwnerId, hb.AttackSlot,
                    hb.ActivationId, hb.ActivationAirborne, hb.AttackSequence));
            OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
            _hitboxes.RemoveAt(index);
        }

        internal void StopHomingAt(ulong targetId)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (hb.HomingTargetEntityId != targetId) continue;
                hb.HomingTargetEntityId = 0;
                _hitboxes[i] = hb;
            }
        }

        internal void UpdateHomingProjectiles(
            Func<ulong, ulong, (bool Exists, float X, float Y, float Z)> targetPosition)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || hb.HomingTargetEntityId == 0) continue;
                var target = targetPosition(hb.OwnerId, hb.HomingTargetEntityId);
                float dx = target.X - hb.X, dy = target.Y - hb.Y, dz = target.Z - hb.Z;
                float along = hb.VX * dx + hb.VY * dy + hb.VZ * dz;
                if (!target.Exists || along <= 0f)
                {
                    hb.HomingTargetEntityId = 0;
                }
                else
                {
                    float speed = MathF.Sqrt(hb.VX * hb.VX + hb.VY * hb.VY + hb.VZ * hb.VZ);
                    float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    dx /= distance; dy /= distance; dz /= distance;
                    float x = hb.VX / speed, y = hb.VY / speed, z = hb.VZ / speed;
                    float dot = x * dx + y * dy + z * dz;
                    if (dot >= hb.HomingTurnCos)
                    {
                        x = dx; y = dy; z = dz;
                    }
                    else
                    {
                        float px = dx - dot * x, py = dy - dot * y, pz = dz - dot * z;
                        float perpendicular = MathF.Sqrt(px * px + py * py + pz * pz);
                        px /= perpendicular; py /= perpendicular; pz /= perpendicular;
                        x = x * hb.HomingTurnCos + px * hb.HomingTurnSin;
                        y = y * hb.HomingTurnCos + py * hb.HomingTurnSin;
                        z = z * hb.HomingTurnCos + pz * hb.HomingTurnSin;
                        float norm = MathF.Sqrt(x * x + y * y + z * z);
                        x /= norm; y /= norm; z /= norm;
                    }
                    hb.VX = x * speed; hb.VY = y * speed; hb.VZ = z * speed;
                }
                _hitboxes[i] = hb;
            }
        }

        internal void RecordReturnHit(ulong ownerId, ulong activationId)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (hb.OwnerId != ownerId || hb.ActivationId != activationId || !hb.ReturnLeg) continue;
                hb.ReturnHit = true;
                _hitboxes[i] = hb;
            }
        }

        private static void Turn(ref Hitbox hb)
        {
            hb.ReturnLeg = true;
            hb.HomingTargetEntityId = 0;
            if (hb.Return?.Hit is { } hit)
            {
                hb.Damage = hit.Damage; hb.KnockbackAngle = (sbyte)hit.Angle;
                hb.BaseKnockback = hit.BaseKnockback; hb.KnockbackGrowth = hit.KnockbackGrowth;
                hb.StunTicks = hit.StunTicks; hb.FixedHitstunTicks = hit.FixedHitstunTicks;
            }
            hb.LegTicks = 0;
            hb.HitEntities = new HashSet<ulong>();
            hb.KnockbackDirection = AuthoringKnockbackDirection.TowardOwner;
            hb.CanHitOwner = true;
        }

        // Share of its speed a returning projectile keeps when it bounces off the stage or a shield.
        private const float BounceRetention = 0.5f;

        /// <summary>
        /// Whether this tick's travel would bring a projectile into the stage, and the surface normal. Returning
        /// projectiles bounce on it and a tether's ring stops on it (ADR-0048).
        /// </summary>
        internal bool MeetsStage(in Hitbox hb, in ArenaDefinition arena, out float nx, out float ny, out float nz)
        {
            nx = 0f; ny = 1f; nz = 0f;
            float ex = hb.X + hb.VX * Simulation.TickDt, ey = hb.Y + hb.VY * Simulation.TickDt, ez = hb.Z + hb.VZ * Simulation.TickDt;
            if (ArenaCollision.HasTriangles(arena))
            {
                int triangles = arena.CollisionTriangles!.Length;
                if (_returnSweepTriangles.Length < triangles) _returnSweepTriangles = new int[triangles];
                int count = ArenaCollision.GetCandidateTrianglesForSweep(hb.X, hb.Y, hb.Z, ex, ey, ez,
                    hb.Radius, hb.Radius * 2f, in arena, _returnSweepTriangles);
                if (!ArenaCollision.SweepCapsule(hb.X, hb.Y, hb.Z, ex, ey, ez, hb.Radius, hb.Radius * 2f,
                        in arena, _returnSweepTriangles, count, out var contact))
                    return false;
                nx = contact.NormalX; ny = contact.NormalY; nz = contact.NormalZ;
                return true;
            }
            if (arena.Heightmap.Data == null || arena.Heightmap.Data.Length == 0) return false;
            float ground = arena.Heightmap.Sample(ex, ez);
            return ground != float.MinValue && ey - hb.Radius <= ground;
        }

        /// <summary>The wire slot of an owner's returning projectile in flight, 0 when none.</summary>
        internal byte ReturningProjectileSlot(ulong ownerId)
        {
            foreach (var hb in _hitboxes)
                if (hb.Active && hb.Return.HasValue && hb.OwnerId == ownerId)
                    return hb.AttackSlot;
            return 0;
        }

        /// <summary>The live projectile one activation owns (a tether's ring), if any.</summary>
        internal bool TryGetActivationProjectile(ulong ownerId, ulong activationId, out Hitbox projectile)
        {
            foreach (var hb in _hitboxes)
                if (hb.Active && hb.OwnerId == ownerId && hb.ActivationId == activationId && !hb.OwnerWindow)
                {
                    projectile = hb;
                    return true;
                }
            projectile = default;
            return false;
        }

        /// <summary>Whether an owner has a returning projectile from this wire slot in flight.</summary>
        internal bool HasReturningProjectile(ulong ownerId, byte attackSlot)
        {
            foreach (var hb in _hitboxes)
                if (hb.Active && hb.Return.HasValue && hb.OwnerId == ownerId && hb.AttackSlot == attackSlot)
                    return true;
            return false;
        }

        /// <summary>
        /// Bounces an owner's outbound returning projectile off a shield: it reverses at half speed and
        /// turns, and the shield it met counts as already hit on the way back, so it is not blocked twice.
        /// </summary>
        internal void TurnReturningProjectile(ulong ownerId, ulong activationId, ulong blockerId)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Return.HasValue || hb.ReturnLeg || hb.OwnerId != ownerId
                    || hb.ActivationId != activationId) continue;
                hb.VX *= -BounceRetention; hb.VY *= -BounceRetention; hb.VZ *= -BounceRetention;
                Turn(ref hb);
                hb.HitEntities!.Add(blockerId);
                _hitboxes[i] = hb;
            }
        }

        /// <summary>Removes and returns an owner's returning projectile (caught, or it hit him).</summary>
        internal bool TryTakeReturningProjectile(ulong ownerId, ulong activationId, out Hitbox taken)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Return.HasValue || hb.OwnerId != ownerId || hb.ActivationId != activationId) continue;
                taken = hb;
                hb.Active = false;
                OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                _hitboxes.RemoveAt(i);
                return true;
            }
            taken = default;
            return false;
        }

        /// <summary>Removes every returning projectile an owner has in flight (KO, leaving the match).</summary>
        internal void RemoveReturningProjectiles(ulong ownerId)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Return.HasValue || hb.OwnerId != ownerId) continue;
                hb.Active = false;
                OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                _hitboxes.RemoveAt(i);
            }
        }

        /// <summary>
        /// Clear all hitboxes (e.g., on match reset).
        /// </summary>
        internal void RemoveContactBombs(ulong owner)
        {
            _hitboxes.RemoveAll(h => h.OwnerId == owner && h.ContactExplosion);
            _pendingExplosions.RemoveAll(e => e.ownerId == owner && e.explosion.SelfPushSpeed > 0f);
        }

        internal void QueueExplosion(float x, float y, float z, ProjectileExplosion explosion,
            ulong owner, byte slot, ulong activation, bool airborne, byte sequence)
            => _pendingExplosions.Add((x, y, z, explosion, owner, slot, activation, airborne, sequence));

        public void Clear()
        {
            _hitboxes.Clear();
            _lastTickChecked.Clear();
            _pendingExplosions.Clear();
        }

        /// <summary>World Snapshot: live hitboxes in resolution order and undrained explosions.
        /// The last tick's checked list is presentation output and is not captured.</summary>
        internal void WriteSnapshot(SnapshotWriter w,
            Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
        {
            w.WriteULong(_lastEmissionId);
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
            public ulong LastEmissionId;
            public readonly List<Hitbox> Hitboxes = new();
            public readonly List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId, byte attackSlot, ulong activationId, bool airborne, byte attackSequence)> Explosions = new();
        }

        internal static SnapshotData ReadSnapshot(SnapshotReader r,
            Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
        {
            var data = new SnapshotData { LastEmissionId = r.ReadULong() };
            var emissionIds = new HashSet<ulong>();
            int hitboxes = r.ReadCount(1);
            for (int i = 0; i < hitboxes; i++)
            {
                var hitbox = SnapshotCodecs.ReadHitbox(r, ownerContent);
                if (hitbox.EmissionId == 0 || hitbox.EmissionId > data.LastEmissionId ||
                    !emissionIds.Add(hitbox.EmissionId))
                    throw SnapshotReader.Malformed("hitbox emission identity");
                data.Hitboxes.Add(hitbox);
            }
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
            _lastEmissionId = data.LastEmissionId;
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
        /// Project a spherical, straight-down contact bomb onto its first floor.
        /// Uses authoritative upward triangle contact before the heightmap, without
        /// advancing the resolver or changing the arena. The caller supplies scratch
        /// space for every arena triangle, as with the other ArenaCollision queries.
        /// </summary>
        internal bool TryProjectStraightDrop(in ArenaDefinition arena, float x, float y, float z, float radius, out float landingY)
        {
            int triangles = arena.CollisionTriangles?.Length ?? 0;
            if (_returnSweepTriangles.Length < triangles) _returnSweepTriangles = new int[triangles];
            return TryProjectStraightDrop(in arena, x, y, z, radius, _returnSweepTriangles, out landingY);
        }

        public static bool TryProjectStraightDrop(in ArenaDefinition arena, float x, float y, float z,
            float radius, int[] triangleCandidates, out float landingY)
        {
            float endY = MathF.Min(arena.KillHeight, y - radius);
            if (ArenaCollision.HasTriangles(arena))
            {
                int candidates = ArenaCollision.GetCandidateTrianglesForSweep(x, y, z, x, endY, z,
                    radius, radius * 2f, arena, triangleCandidates);
                int upward = 0;
                for (int i = 0; i < candidates; i++)
                    if (ArenaCollision.IsUpwardFacingTriangle(triangleCandidates[i], arena))
                        triangleCandidates[upward++] = triangleCandidates[i];
                if (ArenaCollision.SweepCapsule(x, y, z, x, endY, z, radius, radius * 2f,
                        arena, triangleCandidates, upward, out var contact) && contact.NormalY > .5f)
                {
                    landingY = y + (endY - y) * contact.Time - radius;
                    return true;
                }
            }
            landingY = arena.Heightmap.Data is { Length: > 0 } ? arena.Heightmap.Sample(x, z) : 0f;
            return float.IsFinite(landingY) && landingY != float.MinValue && landingY <= y - radius + .001f;
        }

        /// <summary>Queue floor explosions and remove projectiles that reached their landing surface.</summary>
        private int[] _bombGroundCandidates = Array.Empty<int>();

        public void CheckGroundCollision(ArenaDefinition arena)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Explosion.HasValue || hb.Gravity <= 0f || hb.AttachedEntityId != 0) continue;

                // Sample ground height at projectile's current XZ
                float groundY = arena.Heightmap.Data != null && arena.Heightmap.Data.Length > 0
                    ? arena.Heightmap.Sample(hb.X, hb.Z)
                    : 0f;
                if (hb.ContactExplosion)
                {
                    int triangleCount = arena.CollisionTriangles?.Length ?? 0;
                    if (_bombGroundCandidates.Length < triangleCount)
                        _bombGroundCandidates = new int[triangleCount];
                    float previousY = hb.Y - (hb.VY + hb.Gravity * Simulation.TickDt) * Simulation.TickDt;
                    if (!TryProjectStraightDrop(arena, hb.X, previousY, hb.Z, hb.Radius,
                            _bombGroundCandidates, out groundY)) continue;
                }
                if (groundY == float.MinValue) groundY = 0f;
                if (hb.Y - hb.Radius > groundY) continue;

                if (hb.MinePhase == MinePhase.Falling)
                {
                    hb.Y = groundY + hb.Radius;
                    hb.VX = hb.VY = hb.VZ = hb.Gravity = 0f;
                    hb.MinePhase = MinePhase.Arming;
                    hb.MineTicksLeft = hb.MineArmTicks;
                    hb.AgeTicks = 0;
                    hb.DurationTicks = hb.MineLifetimeTicks;
                    _hitboxes[i] = hb;
                    continue;
                }

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
                if (owner.AttackSlot == 0 && !hb.OwnerWindow)
                {
                    // Owner interrupted/ended — leave the lingering hitbox as-is.
                    if (hb.Sweep != null) { hb.Sweep = null; _hitboxes[i] = hb; }
                    continue;
                }

                var move = hb.Def?.GetCookedSlotAbility((byte)(hb.Slot + 1), hb.Airborne);
                HitboxGeometry.ResolvePositions(owner, hb.SourceEvent, hb.Baked, hb.Def, move, hb.AnimIndex,
                    out float wx, out float wy, out float wz, out float wex, out float wey, out float wez);
                var current = new HitboxPose { X = wx, Y = wy, Z = wz, EndX = wex, EndY = wey, EndZ = wez };
                hb.Sweep = hb.HasSweepOrigin
                    ? HitboxGeometry.SweepPoses(hb.SourceEvent, hb.Baked, hb.Def, move,
                        hb.AnimIndex, in hb.SweepOrigin, in owner, in current)
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
            HashSet<ulong>? frozenOwners = null, Func<ulong, ulong, bool>? canHitTarget = null,
            IReadOnlyDictionary<ulong, CharacterState>? fighters = null)
        {
            var results = new List<HitResult>();
            var hitThisTick = new HashSet<ulong>();
            _lastTickChecked.Clear();
            _perchedThisTick.Clear();
            if (orderMovingTargetContacts) OrderMovingTargetContacts(entities);

            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active) { _hitboxes.RemoveAt(i); continue; }

                if (hb.MinePhase is MinePhase.Arming or MinePhase.Armed or MinePhase.Triggered)
                {
                    TickMine(i, hb, entities, canHitTarget);
                    continue;
                }

                if (hb.AttachedEntityId != 0)
                {
                    if (hb.FuseTicksLeft > 0) hb.FuseTicksLeft--;
                    if (hb.Perch is { } riding)
                    {
                        if (riding.PhaseTicks < ushort.MaxValue) riding.PhaseTicks++;
                        hb.Perch = riding;
                        if (hb.FuseTicksLeft == 0) Scatter(in hb, hb.X, hb.Y, hb.Z);
                    }
                    if (hb.FuseTicksLeft == 0) ExplodeSticky(i, hb);
                    else _hitboxes[i] = hb;
                    _lastTickChecked.Add(hb);
                    continue;
                }

                // Save position before movement (for explosion spawning)
                float prevX = hb.X, prevY = hb.Y, prevZ = hb.Z;
                var removalOrigin = (X: prevX, Y: prevY, Z: prevZ);

                if (hb.Shape == HitboxShape.Ring)
                {
                    hb.PreviousRadius = hb.Radius;
                    hb.Radius += hb.RingSpeed * Simulation.TickDt;
                }

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

                _contacts.Clear();
                // A perch touches bodies only in its dive (ADR-0051 section 9).
                bool touches = hb.Perch is not { } flight || flight.Phase == PerchPhase.Diving;
                foreach (var entity in candidates)
                {
                    if (!touches || !entity.Active) continue;
                    if (entity.RuntimeSolid && (hb.Damage <= 0 || hb.OwnerId == ServerSimulation.MayhemHostEntityId)) continue;
                    if (hb.Unblockable ? entity.ShieldSurface : entity.ProtectedByShield) continue;
                    if (!hb.CanHitOwner && entity.Id == hb.OwnerId) continue;
                    if (hb.OnlyTargetId != 0 && entity.Id != hb.OnlyTargetId) continue;
                    // Reject friendly contacts before consuming a projectile or its hit history.
                    if (canHitTarget != null && !canHitTarget(hb.OwnerId, entity.Id)) continue;
                    if (hb.SourceEvent.RequiresFacingOwner && fighters != null
                        && fighters.TryGetValue(entity.Id, out var victim)
                        && (!fighters.TryGetValue(hb.OwnerId, out var owner)
                            || MathF.Sin(victim.FacingYaw) * (owner.PX - victim.PX)
                                + MathF.Cos(victim.FacingYaw) * (owner.PZ - victim.PZ) <= 0f)) continue;
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
                        if (entity.MotionPath == null && IsSweptProjectile(in hb, prevX, prevY, prevZ))
                        {
                            contact.X = prevX + (hb.X - prevX) * contactTime;
                            contact.Y = prevY + (hb.Y - prevY) * contactTime;
                            contact.Z = prevZ + (hb.Z - prevZ) * contactTime;
                        }
                        _contacts.Add(new Contact(entity, contact, _contacts.Count, dist, dx, dy, dz,
                            hitX, hitY, hitZ, contactTime, movingTargetY));
                    }
                }
                if (!orderMovingTargetContacts && IsSweptProjectile(in hb, prevX, prevY, prevZ))
                    _contacts.Sort((a, b) =>
                    {
                        int time = a.Time.CompareTo(b.Time);
                        return time != 0 ? time : a.Order.CompareTo(b.Order);
                    });
                foreach (var collision in _contacts)
                {
                    var entity = collision.Entity;
                    if (hitThisTick.Contains(entity.Id)) continue;
                    if (hb.HitsMultipleOpponents && hb.HitEntities!.Contains(entity.Id)) continue;
                    var contact = collision.Pose;
                    float dist = collision.Distance, dx = collision.DX, dy = collision.DY, dz = collision.DZ;
                    float hitX = collision.X, hitY = collision.Y, hitZ = collision.Z;
                    float contactTime = collision.Time, movingTargetY = collision.TargetY;
                    float dirXNorm = dist > 0.001f ? (dx / dist) : 0f;
                    float dirZNorm = dist > 0.001f ? (dz / dist) : 0f;

                    sbyte launchAngle = hb.KnockbackAngle;
                    if (launchAngle == KnockbackData.AdaptiveAngle)
                    {
                        float hurtboxCenterY = entity.MotionPath != null ? movingTargetY : (entity.PosY + entity.EndY) * 0.5f;
                        launchAngle = ComputeAdaptiveAngle(dx, hurtboxCenterY - contact.Y, dz);
                    }

                    if (hb.StickyFuseTicks > 0 && !entity.ShieldSurface)
                    {
                        if (hb.Perch is { } landing)
                        {
                            _perchedThisTick.Add(hb.EmissionId);
                            landing.Phase = PerchPhase.Perched;
                            landing.PhaseTicks = 0;
                            hb.Perch = landing;
                        }
                        hb.AttachedEntityId = entity.Id; hb.FuseTicksLeft = hb.StickyFuseTicks;
                        hb.X = entity.PosX; hb.Y = (entity.PosY + entity.EndY) * .5f; hb.Z = entity.PosZ;
                        hb.VX = hb.VY = hb.VZ = hb.Gravity = 0f;
                        break;
                    }

                    if (hb.ContactExplosion)
                    {
                        removalOrigin = (contact.X, contact.Y, contact.Z);
                        hb.Active = false;
                        break;
                    }

                    results.Add(new HitResult
                    {
                        TargetEntityId = entity.Id,
                        Unblockable = hb.Unblockable,
                        OwnerEntityId = hb.OwnerId,
                        AttackSlot = hb.AttackSlot,
                        ActivationId = hb.ActivationId,
                        Airborne = hb.ActivationAirborne,
                        Form = hb.Form,
                        ChallengeTicks = hb.ChallengeTicks,
                        ReturnPhase = hb.Return.HasValue ? (hb.ReturnLeg ? (byte)2 : (byte)1) : (byte)0,
                        Damage = entity.Id == hb.OwnerId ? hb.OwnerDamage ?? hb.Damage : hb.Damage,
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
                        OwnerWindow = hb.OwnerWindow,
                        HitX = hitX,
                        HitY = hitY,
                        HitZ = hitZ,
                        ContactTime = contactTime,
                        HitPresentationId = hb.SourceEvent.HitPresentationId,
                    });

                    hitThisTick.Add(entity.Id);
                    if (hb.HitsMultipleOpponents)
                    {
                        hb.HitEntities!.Add(entity.Id);
                        continue;
                    }
                    if (entity.MotionPath != null || IsSweptProjectile(in hb, prevX, prevY, prevZ)
                        && !entity.ShieldSurface && !OverlapsAtCurrentPosition(in hb, in entity))
                    {
                        removalOrigin = (prevX + (hb.X - prevX) * contactTime,
                            prevY + (hb.Y - prevY) * contactTime, prevZ + (hb.Z - prevZ) * contactTime);
                    }
                    hb.Active = false;
                    break;
                }
                _lastTickChecked.Add(hb);
                if (hb.StickyFuseTicks > 0 && hb.Active)
                    removalOrigin = (hb.X, hb.Y, hb.Z);

                // Age / expire
                if (!hb.CancelWithAbility || frozenOwners == null || !frozenOwners.Contains(hb.OwnerId))
                    hb.AgeTicks++;
                if (hb.AttachedEntityId == 0 && (hb.AgeTicks >= hb.DurationTicks || !hb.Active))
                {
                    if (hb.Perch.HasValue) Scatter(in hb, removalOrigin.X, removalOrigin.Y, removalOrigin.Z);
                    if (hb.Explosion.HasValue && (!hb.ContactExplosion || !hb.Active))
                        _pendingExplosions.Add((removalOrigin.X, removalOrigin.Y, removalOrigin.Z, hb.Explosion.Value, hb.OwnerId, hb.AttackSlot,
                            hb.ActivationId, hb.ActivationAirborne, hb.AttackSequence));
                    OnHitboxRemoved?.Invoke(hb, removalOrigin.X, removalOrigin.Y, removalOrigin.Z);
                    _hitboxes.RemoveAt(i);
                }
                else
                {
                    _hitboxes[i] = hb; // write back velocity/age changes
                }
            }

            ReplacePerches();
            return results;
        }

        private void TickMine(int index, Hitbox mine, List<EntityData> entities, Func<ulong, ulong, bool>? canHitTarget)
        {
            if (mine.MinePhase == MinePhase.Triggered)
            {
                if (mine.MineTicksLeft > 0) mine.MineTicksLeft--;
                if (mine.MineTicksLeft == 0)
                {
                    _pendingExplosions.Add((mine.X, mine.Y, mine.Z, mine.Explosion!.Value, mine.OwnerId,
                        mine.AttackSlot, mine.ActivationId, mine.ActivationAirborne, mine.AttackSequence));
                    _hitboxes.RemoveAt(index);
                    return;
                }
            }
            else
            {
                if (++mine.AgeTicks >= mine.DurationTicks)
                {
                    _hitboxes.RemoveAt(index);
                    return;
                }
                if (mine.MinePhase == MinePhase.Arming)
                {
                    if (mine.MineTicksLeft > 0) mine.MineTicksLeft--;
                    if (mine.MineTicksLeft == 0) mine.MinePhase = MinePhase.Armed;
                }
                if (mine.MinePhase == MinePhase.Armed)
                {
                    var probe = mine;
                    probe.Radius = mine.MineTriggerRadius;
                    foreach (var entity in entities)
                    {
                        if (!entity.Active || entity.Id == mine.OwnerId || entity.RuntimeSolid
                            || canHitTarget != null && !canHitTarget(mine.OwnerId, entity.Id)) continue;
                        if (!OverlapsAtCurrentPosition(probe, entity)) continue;
                        mine.MinePhase = MinePhase.Triggered;
                        mine.MineTicksLeft = mine.MineWarningTicks;
                        break;
                    }
                }
            }
            _lastTickChecked.Add(mine);
            _hitboxes[index] = mine;
        }

        private static bool OverlapsAtCurrentPosition(in Hitbox hb, in EntityData entity)
        {
            if (hb.Shape == HitboxShape.Capsule || entity.Shape == HitboxShape.Capsule)
                return CapsuleCollision(hb, entity, out _, out _, out _, out _, out _, out _, out _);
            float dx = hb.X - entity.PosX, dy = hb.Y - entity.PosY, dz = hb.Z - entity.PosZ;
            float radius = hb.Radius + entity.Radius;
            return dx * dx + dy * dy + dz * dz <= radius * radius;
        }

        private static bool TryContact(in Hitbox hb, in EntityData entity, float prevX, float prevY, float prevZ,
            out float dist, out float dx, out float dy, out float dz, out float hitX, out float hitY, out float hitZ,
            out float contactTime, out float movingTargetY)
        {
            bool hit = false;
            dist = dx = dy = dz = 0f;
            hitX = hitY = hitZ = 0f;
            contactTime = 1f;
            movingTargetY = entity.PosY;

            if (hb.Shape == HitboxShape.Ring)
            {
                float ex = entity.Shape == HitboxShape.Capsule ? entity.EndX : entity.PosX;
                float ey = entity.Shape == HitboxShape.Capsule ? entity.EndY : entity.PosY;
                float ez = entity.Shape == HitboxShape.Capsule ? entity.EndZ : entity.PosZ;
                float low = hb.Y - hb.RingHalfHeight - entity.Radius;
                float high = hb.Y + hb.RingHalfHeight + entity.Radius;
                float a = 0f, b = 1f, vy = ey - entity.PosY;
                if (MathF.Abs(vy) < .000001f)
                { if (entity.PosY < low || entity.PosY > high) return false; }
                else
                {
                    float t0 = (low - entity.PosY) / vy, t1 = (high - entity.PosY) / vy;
                    a = MathF.Max(a, MathF.Min(t0, t1)); b = MathF.Min(b, MathF.Max(t0, t1));
                    if (a > b) return false;
                }
                float ax = entity.PosX + (ex - entity.PosX) * a - hb.X;
                float az = entity.PosZ + (ez - entity.PosZ) * a - hb.Z;
                float bx = entity.PosX + (ex - entity.PosX) * b - hb.X;
                float bz = entity.PosZ + (ez - entity.PosZ) * b - hb.Z;
                float sx = bx - ax, sz = bz - az, length = sx * sx + sz * sz;
                float t = length > .000001f ? Math.Clamp(-(ax * sx + az * sz) / length, 0f, 1f) : 0f;
                dx = ax + sx * t; dz = az + sz * t;
                dist = MathF.Sqrt(dx * dx + dz * dz);
                float far = MathF.Sqrt(MathF.Max(ax * ax + az * az, bx * bx + bz * bz));
                float width = hb.RingHalfWidth + entity.Radius;
                hitX = hb.X + dx; hitY = hb.Y; hitZ = hb.Z + dz;
                return dist <= hb.Radius + width && far >= MathF.Max(0f, hb.PreviousRadius - width);
            }
            if (entity.MotionPath != null)
            {
                hit = MovingSphereContact(in hb, in entity, prevX, prevY, prevZ,
                    out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ,
                    out contactTime, out movingTargetY);
            }
            else if (IsSweptProjectile(in hb, prevX, prevY, prevZ))
            {
                if (!FirstContact(in hb, in entity, prevX, prevY, prevZ, out contactTime))
                    return false;
                float x = prevX + (hb.X - prevX) * contactTime;
                float y = prevY + (hb.Y - prevY) * contactTime;
                float z = prevZ + (hb.Z - prevZ) * contactTime;
                ClosestPointsSegmentSegment(x, y, z, x, y, z,
                    entity.PosX, entity.PosY, entity.PosZ,
                    entity.Shape == HitboxShape.Capsule ? entity.EndX : entity.PosX,
                    entity.Shape == HitboxShape.Capsule ? entity.EndY : entity.PosY,
                    entity.Shape == HitboxShape.Capsule ? entity.EndZ : entity.PosZ,
                    out _, out _, out _, out float ex, out float ey, out float ez);
                dx = ex - x; dy = ey - y; dz = ez - z;
                dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                float scale = dist > 0.000001f ? hb.Radius / dist : 0f;
                hitX = x + dx * scale; hitY = y + dy * scale; hitZ = z + dz * scale;
                hit = true;
            }
            else if (entity.ShieldSurface && hb.Shape == HitboxShape.Sphere
                && SweptShieldContact(in hb, in entity, prevX, prevY, prevZ,
                    out dist, out dx, out dy, out dz, out hitX, out hitY, out hitZ))
                return true;
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

            if (hit && entity.ShieldSurface)
                ShieldSurfaceContact(in hb, in entity, prevX, prevY, prevZ,
                    dist, dx, dy, dz, out hitX, out hitY, out hitZ);
            return hit;
        }

        private static bool IsSweptProjectile(in Hitbox hb, float prevX, float prevY, float prevZ)
            => hb.Shape == HitboxShape.Sphere && !hb.CancelWithAbility && !hb.TracksBone && hb.Sweep == null
                && (hb.X != prevX || hb.Y != prevY || hb.Z != prevZ);

        private static bool FirstContact(in Hitbox hb, in EntityData entity,
            float prevX, float prevY, float prevZ, out float time)
        {
            float vx = hb.X - prevX, vy = hb.Y - prevY, vz = hb.Z - prevZ;
            float ax = entity.Shape == HitboxShape.Capsule ? entity.EndX - entity.PosX : 0f;
            float ay = entity.Shape == HitboxShape.Capsule ? entity.EndY - entity.PosY : 0f;
            float az = entity.Shape == HitboxShape.Capsule ? entity.EndZ - entity.PosZ : 0f;
            float px = prevX - entity.PosX, py = prevY - entity.PosY, pz = prevZ - entity.PosZ;
            float axisSq = ax * ax + ay * ay + az * az;
            float radius = hb.Radius + entity.Radius;
            float projection = axisSq > 0f ? (px * ax + py * ay + pz * az) / axisSq : 0f;
            float clamped = Math.Clamp(projection, 0f, 1f);
            float dx = px - ax * clamped, dy = py - ay * clamped, dz = pz - az * clamped;
            time = 0f;
            if (dx * dx + dy * dy + dz * dz <= radius * radius) return true;
            time = float.PositiveInfinity;
            if (SphereContactTime(px, py, pz, vx, vy, vz, radius, out float cap)) time = cap;
            if (axisSq > 0f)
            {
                if (SphereContactTime(px - ax, py - ay, pz - az, vx, vy, vz, radius, out cap))
                    time = MathF.Min(time, cap);
                float speedAlong = (vx * ax + vy * ay + vz * az) / axisSq;
                float ux = vx - ax * speedAlong, uy = vy - ay * speedAlong, uz = vz - az * speedAlong;
                float qx = px - ax * projection, qy = py - ay * projection, qz = pz - az * projection;
                float a = ux * ux + uy * uy + uz * uz;
                float b = qx * ux + qy * uy + qz * uz;
                float c = qx * qx + qy * qy + qz * qz - radius * radius;
                float discriminant = b * b - a * c;
                if (a > 0f && discriminant >= 0f)
                {
                    float cylinder = (-b - MathF.Sqrt(discriminant)) / a;
                    float along = projection + speedAlong * cylinder;
                    if (cylinder >= 0f && cylinder <= 1f && along >= 0f && along <= 1f)
                        time = MathF.Min(time, cylinder);
                }
            }
            return time <= 1f;
        }

        private static bool SphereContactTime(float x, float y, float z,
            float vx, float vy, float vz, float radius, out float time)
        {
            float a = vx * vx + vy * vy + vz * vz;
            float b = x * vx + y * vy + z * vz;
            float c = x * x + y * y + z * z - radius * radius;
            float discriminant = b * b - a * c;
            time = a > 0f && discriminant >= 0f ? (-b - MathF.Sqrt(discriminant)) / a : -1f;
            return time >= 0f && time <= 1f;
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
