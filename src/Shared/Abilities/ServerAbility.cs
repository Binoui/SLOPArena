using System;
using System.Collections.Generic;
using System.Linq;
namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Base class for server-side abilities that control movement, hitboxes,
    /// and state transitions per tick. Each activation gets a fresh instance.
    ///
    /// Lifecycle:
    ///   OnStart  → called once when ability activates
    ///   Tick     → called every sim tick while active
    ///   OnEnd    → called on NATURAL completion only (NOT on interrupt)
    ///
    /// Interruption (hitstun, death, state override):
    ///   Simulation calls OnCancel, clears owned melee effects, and retires the instance.
    ///   Launched projectiles may continue after gameplay interruption.
    /// </summary>
    public abstract class ServerAbility
    {
        // Lazily allocated only for multi-event moves that deliberately share a hit identity
        // (for example an early sweetspot handing off to a late sourspot).
        private Dictionary<byte, HashSet<ulong>>? _hitGroups;
        private ushort _armorWindowTicks;
        private bool _armorTickActive;
        private bool _armorStartsOnFirstTick;
        internal bool FrozenForSimulationTick { get; set; }
        internal ServerSimulation? OwnerSimulation { get; set; }
        internal InputState ActivationInput { get; set; }
        // ── Lifecycle (implement in subclasses) ──

        /// <summary>Whether this activation currently owns vertical motion.</summary>
        public virtual bool OwnsVerticalMotion => false;
        /// <summary>Multiplier applied to ordinary airborne gravity while this ability is active.</summary>
        public virtual float GravityMultiplier => 1f;
        /// <summary>Whether this activation phases through other fighters' pushboxes.</summary>
        public virtual bool IgnoresFighterPushboxes => false;
        /// <summary>Whether body overlap moves only the other fighter during this activation.</summary>
        public virtual bool HoldsFighterPushboxPosition => false;
        /// <summary>The tether this activation runs, if any (ADR-0048): its contacts catch instead of hitting.</summary>
        public virtual TetherAbility? Tether => null;
        /// <summary>The command grab this activation runs, if any (ADR-0057): its capture holds the fighter it catches.</summary>
        public virtual CommandGrabAbility? CommandGrab => null;
        /// <summary>A capability that keeps its timeline in the current stage past the stage's length (a command grab's whiff).</summary>
        internal virtual bool HoldsStage => false;

        /// <summary>The hold this activation's contact puts on that victim (ADR-0052), or null for an ordinary hit.</summary>
        public virtual HeldCutRelease? HeldCutFor(ulong victimId) => null;

        /// <summary>
        /// True while this activation's authored armor window remains active. A capability may report
        /// armour of its own (a charge armoured for as long as it gallops); its timeline forwards it.
        /// </summary>
        public virtual bool HasArmor => _armorWindowTicks > 0;

        /// <summary>Starts or replaces this activation's armor window.</summary>
        protected void StartArmorWindow(ushort durationTicks)
        {
            _armorWindowTicks = durationTicks;
            // OnStart runs before the first collision step; do not spend that contact tick.
            _armorStartsOnFirstTick = durationTicks > 0 && !_armorTickActive;
        }

        /// <summary>Advances armor only when the owning ability receives an active tick.</summary>
        internal void TickArmorWindow()
        {
            _armorTickActive = true;
            if (_armorStartsOnFirstTick)
            {
                _armorStartsOnFirstTick = false;
                return;
            }
            if (_armorWindowTicks > 0)
                _armorWindowTicks--;
        }

        internal void EndArmorTick() => _armorTickActive = false;
        internal void AccountForCurrentContactFrame() => _armorStartsOnFirstTick = false;

        internal void ClearArmorWindow()
        {
            _armorWindowTicks = 0;
            _armorStartsOnFirstTick = false;
            _armorTickActive = false;
            FrozenForSimulationTick = false;
        }

        /// <summary>Called once when the ability activates.</summary>
        public abstract void OnStart(ref CharacterState s, CharacterDefinition def);

        /// <summary>Called every sim tick while the ability is active.</summary>
        public abstract void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def);

        /// <summary>
        /// Called on NATURAL completion only (duration expired, EndAbility called).
        /// NOT called on interruption. Override to clean up or apply lingering effects.
        /// </summary>
        public virtual void OnEnd(ref CharacterState s) { }
        /// <summary>Called when the ability is interrupted; natural completion does not use this hook.</summary>
        public virtual void OnCancel(ref CharacterState s) { }

        /// <summary>Runtime sink for semantic presentation events.</summary>
        public Action<TimelinePresentationEvent>? PresentationSink { get; set; }


        /// <summary>
        /// Called when this ability's hitbox connects with a target entity.
        /// Override for hit-time effects such as rocket-jump self damage.
        /// </summary>
        public virtual void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, CharacterDefinition targetDef,
            ref float damage, ref float knockbackForce)
        {
        }
        public virtual void OnHitConfirmed(in SpellResolver.HitResult hit, in CharacterState target, int operationIndex) { }

        /// <summary>Accepted nonfighter contact; owner effects do not require a fake fighter target.</summary>
        public virtual void OnHitObject(ref CharacterState attacker, CharacterDefinition attackerDef,
            ref float damage, ref float knockbackForce) { }
        /// <summary>Called when one of this activation's hitboxes contacts an active shield.</summary>
        public virtual void OnShieldBlock(ref CharacterState attacker, ref CharacterState defender) { }

        // ── Metadata (set by factory after construction) ──

        /// <summary>Zero-based wire ability slot.</summary>
        public byte Slot { get; set; }
        /// <summary>Cooldown in ticks. Applied by ServerSimulation when the activation exits.</summary>
        public ushort Cooldown { get; set; }

        /// <summary>
        /// Whether this ability instance was STARTED while airborne (set by the simulation
        /// at activation from the owner's grounded state). Landing-lag termination
        /// (ADR-0021 §3 / drift fix) uses this, not the grounded state at landing time: an
        /// air move that lands mid-flight ends on the landing frame; a ground move keeps its
        /// ground behavior even if launched and landed mid-move.
        /// </summary>
        public bool AirborneAtStart { get; set; }

        /// <summary>Unique server-local identity for this ability activation.</summary>
        public ulong ActivationId { get; set; }
        /// <summary>Attack sequence captured when this ability activation started.</summary>
        public byte PresentationAttackSequence { get; set; }
        /// <summary>Flattened authored operation index for a capability-originated event.</summary>
        public int PresentationOperationIndex { get; set; } = -1;
        // ── Animation (set during Tick, synced to client via CharacterState.AnimIndex) ──

        /// <summary>
        /// Current presentation clip index into the cooked timeline.
        /// Set this in Tick() to change the client's animation.
        /// </summary>
        public byte AnimIndex { get; protected set; }

        /// <summary>All entity states (set by ServerSimulation). Used by abilities that need
        /// to inspect other entities (for example pull or homing abilities).</summary>
        public Dictionary<ulong, CharacterState>? SimulationStates { get; set; }

        /// <summary>The immutable cooked content selected for this activation.</summary>
        public virtual CookedSlotDefinition? Move
            => CharacterDef?.GetCookedSlotAbility((byte)(Slot + 1), AirborneAtStart);

        // ── Context (set by simulation before first Tick) ──

        /// <summary>Hitbox resolver. Set by ServerSimulation before activation.</summary>
        public SpellResolver Resolver { get; set; } = null!;
        /// <summary>Baked animation data for bone-attached hitbox resolution. Set by ServerSimulation.</summary>
        public BakedAnimationData? BakedData { get; set; }
        /// <summary>Character definition for the ability owner. Set by ServerSimulation.</summary>
        public CharacterDefinition? CharacterDef { get; set; }
        /// <summary>
        /// The arena being played, for terrain-aware displacement: an ability that
        /// writes position directly (blinks, teleports, warps) can sample
        /// <see cref="ArenaHeightmap.Sample"/> to resolve a destination that does not
        /// end inside geometry. Set by ServerSimulation before activation.
        /// MAY be null — harnesses that drive abilities without a simulation never set
        /// it, so every consumer must have a no-arena fallback.
        /// </summary>
        public ArenaDefinition? Arena { get; set; }

        // ── Helpers (call from Tick) ──

        /// <summary>
        /// Spawn a hitbox at the character's position + facing-relative offsets.
        /// When evt.BoneName is set and baked data is available, positions at the
        /// bone's world position instead of the fixed OffX/Y/Z offset.
        /// </summary>
        protected HashSet<ulong> SpawnHitbox(ref CharacterState s, HitboxEvent evt, bool followOwner = false, bool freezesOwner = true,
            ulong onlyTargetId = 0)
        {
            // Position resolution is shared with the Ability Lab preview + tests
            // (spec #119) — one implementation, previews cannot drift from the server.
            HitboxGeometry.ResolvePositions(s, evt, BakedData, CharacterDef, Move, AnimIndex, out float wx, out float wy, out float wz, out float wex, out float wey, out float wez);

            float damage = evt.Damage;
            float radius = evt.Radius;

            // Resolve knockback profile to flat values
            var (kbAngle, kbBase, kbGrowth) = evt.Knockback.Resolve();

            // Explicit owner attachment also supports root-relative geometry and
            // missing baked poses; ordinary hitboxes retain their existing policy.
            bool tracksBone = followOwner || (evt.BoneName != null || evt.EndBoneName != null) && BakedData != null;

            HashSet<ulong>? sharedHitEntities = null;
            if (evt.HitGroup != 0)
            {
                _hitGroups ??= new Dictionary<byte, HashSet<ulong>>();
                if (!_hitGroups.TryGetValue(evt.HitGroup, out sharedHitEntities))
                {
                    sharedHitEntities = new HashSet<ulong>();
                    _hitGroups.Add(evt.HitGroup, sharedHitEntities);
                }
            }

            var hitbox = new Hitbox
            {
                X = wx, Y = wy, Z = wz,
                // Tracked hitboxes are re-resolved to the bone's absolute world
                // position each tick; velocity would double-apply the owner's
                // translation on top of that. Untracked bone-anchored hitboxes (no
                // baked data) fall back to entity-relative and inherit the owner's
                // velocity like any entity-anchored hitbox.
                VX = tracksBone ? 0f : ((evt.BoneName != null || evt.EndBoneName != null) ? s.VX : 0f),
                VY = tracksBone ? 0f : ((evt.BoneName != null || evt.EndBoneName != null) ? s.VY : 0f),
                VZ = tracksBone ? 0f : ((evt.BoneName != null || evt.EndBoneName != null) ? s.VZ : 0f),
                Radius = radius,
                Shape = evt.Shape,
                EndX = wex, EndY = wey, EndZ = wez,
                Damage = damage,
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = kbAngle,
                KnockbackDirection = evt.KnockbackDirection,
                StunTicks = evt.StunTicks,
                FixedHitstunTicks = evt.FixedHitstunTicks,
                DurationTicks = evt.DurationTicks,
                OwnerId = s.EntityId,
                ActivationId = ActivationId,
                ActivationAirborne = AirborneAtStart,
                AttackSequence = PresentationAttackSequence,
                FreezesOwner = freezesOwner,
                OnlyTargetId = onlyTargetId,
                CancelWithAbility = true,
                HitsMultipleOpponents = true,
                HitEntities = sharedHitEntities ??= new HashSet<ulong>(),
                TracksBone = tracksBone,
                SourceEvent = evt,
                Baked = BakedData,
                Def = CharacterDef,
                AnimIndex = AnimIndex,
                Slot = Slot,
                AttackSlot = (byte)(Slot + 1),
                Airborne = AirborneAtStart,
                Form = CharacterDef?.Form ?? 0,
            };
            Resolver.Spawn(hitbox);
            return sharedHitEntities;
        }


        /// <summary>Set character velocity (world space).</summary>
        protected void SetVelocity(ref CharacterState s, float vx, float vy, float vz)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;
            s.VX = vx;
            s.VY = vy;
            s.VZ = vz;
        }

        /// <summary>
        /// Spawn an authored projectile using launch-time targeting or the locked aim; returns it as spawned.
        /// One that cancels with its ability leaves with an interrupted move instead of flying on.
        /// </summary>
        protected Hitbox SpawnCookedProjectile(ref CharacterState s, CookedProjectile projectile, int operationIndex, bool autoTarget,
            bool cancelWithAbility = false)
        {
            float aimYaw;
            float aimPitch;
            if (autoTarget)
            {
                OwnerSimulation!.ResolveAutoTargetProjectileAim(ref s, projectile.LaunchOffsetX,
                    projectile.LaunchOffsetY, projectile.LaunchOffsetZ, out aimYaw, out aimPitch);
            }
            else
            {
                aimYaw = s.AttackCorrectionOwned ? s.FacingYaw : s.AimYaw;
                aimPitch = s.AttackCorrectionOwned ? s.AttackPosePitch : s.AimPitch;
            }
            aimYaw += projectile.YawOffsetDegrees * MathF.PI / 180f;
            float cosPitch = MathF.Cos(aimPitch);
            float dirX = cosPitch * MathF.Sin(aimYaw);
            float dirY = MathF.Sin(aimPitch);
            float dirZ = cosPitch * MathF.Cos(aimYaw);
            float cosYaw = MathF.Cos(s.FacingYaw);
            float sinYaw = MathF.Sin(s.FacingYaw);
            float offsetX = projectile.LaunchOffsetX * cosYaw + projectile.LaunchOffsetZ * sinYaw;
            float offsetZ = -projectile.LaunchOffsetX * sinYaw + projectile.LaunchOffsetZ * cosYaw;
            float damage = projectile.Damage;
            float radius = projectile.Radius;
            var spawned = new Hitbox
            {
                X = s.PX + offsetX,
                Y = s.PY + projectile.LaunchOffsetY,
                Z = s.PZ + offsetZ,
                VX = dirX * projectile.Speed,
                VY = dirY * projectile.Speed,
                VZ = dirZ * projectile.Speed,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = projectile.BaseKnockback,
                KnockbackGrowth = projectile.KnockbackGrowth,
                KnockbackAngle = (sbyte)projectile.Angle,
                StunTicks = projectile.StunTicks,
                DurationTicks = projectile.MaxFlightTicks,
                OwnerId = s.EntityId,
                AttackSlot = (byte)(Slot + 1),
                VisualOperationIndex = operationIndex,
                Gravity = projectile.Gravity,
                CancelWithAbility = cancelWithAbility,
            };
            if (projectile.Return is { } back)
            {
                // A boomerang keeps flying through what it hits, once per fighter per leg.
                spawned.Return = new ProjectileReturn
                {
                    OutboundTicks = back.OutboundTicks,
                    ReturnSpeed = back.ReturnSpeed,
                    ReturnAcceleration = back.ReturnAcceleration,
                    SelfHitDamage = back.SelfHitDamage,
                    SelfHitStunTicks = back.SelfHitStunTicks,
                    CatchPresentationId = back.CatchPresentationId,
                    SelfHitPresentationId = back.SelfHitPresentationId,
                };
                spawned.HitsMultipleOpponents = true;
                spawned.HitEntities = new HashSet<ulong>();
                spawned.Gravity = 0f;
            }
            // Homing takes the current target at spawn and never reacquires (ADR-0051).
            if (projectile.Homing is { } homing && OwnerSimulation != null
                && OwnerSimulation.TryGetCurrentTarget(in s, out ulong homingTarget, out _))
            {
                spawned.HomingTargetEntityId = homingTarget;
                spawned.HomingTurnCos = homing.TurnCos;
                spawned.HomingTurnSin = homing.TurnSin;
            }
            if (projectile.Perch is { } perch)
            {
                spawned.StickyFuseTicks = perch.PerchTicks;
                spawned.Perch = ProjectilePerch.From(perch.Pop);
            }
            SpawnResolverHitbox(spawned);
            return spawned;
        }

        /// <summary>Spawn a raw hitbox while preserving this activation's provenance.</summary>
        protected void SpawnResolverHitbox(Hitbox hitbox)
        {
            hitbox.ActivationId = ActivationId;
            hitbox.ActivationAirborne = AirborneAtStart;
            hitbox.AttackSequence = PresentationAttackSequence;
            hitbox.Form = CharacterDef?.Form ?? 0;
            Resolver.Spawn(hitbox);
        }

        /// <summary>
        /// Apply velocity in the character's facing direction.
        /// forwardSpeed > 0 = forward, < 0 = backward.
        /// </summary>
        protected void SetVelocityInFacing(ref CharacterState s, float forwardSpeed, float vertical = 0f)
        {
            ClearVelocityOwnership(ref s);
            if (vertical != 0f)
                s.IsFastFalling = false;
            s.VX = MathF.Sin(s.FacingYaw) * forwardSpeed;
            s.VZ = MathF.Cos(s.FacingYaw) * forwardSpeed;
            s.VY = vertical;
        }

        protected static void ClearVelocityOwnership(ref CharacterState s)
            => s.SlideAttackCarryActive = false;

        /// <summary>
        /// End the ability naturally: calls OnEnd, returns to Idle.
        /// The simulation applies cooldown after return.
        /// Horizontal velocity is intentionally NOT zeroed (issue #115 / ADR-0015):
        /// momentum survives the attack — lunge drift and pre-attack velocity carry
        /// into the next state, where normal friction/air control resumes.
        /// </summary>
        protected void EndAbility(ref CharacterState s)
        {
            OnEnd(ref s);
            s.SlideAttackCarryActive = false;
            s.State = ActionState.Idle;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            // Cooldown is applied by the simulation after EndAbility returns
            s.AttackSlot = 0; // signal to simulation that ability ended
        }

        // ── World Snapshot contract (ADR-0039) ──
        // Every field that changes after construction is written here or in an override that
        // calls base first. Wiring (simulation, resolver, content, presentation sink) is
        // re-attached by whoever recreates the instance, never written.

        /// <summary>Write this activation's runtime state.</summary>
        internal virtual void WriteSnapshot(SnapshotWriter w)
        {
            w.WriteBool(_hitGroups != null);
            if (_hitGroups != null)
            {
                var groups = new List<byte>(_hitGroups.Keys);
                groups.Sort();
                w.WriteInt(groups.Count);
                foreach (byte group in groups)
                {
                    w.WriteByte(group);
                    w.WriteSet(_hitGroups[group]);
                }
            }
            w.WriteUShort(_armorWindowTicks);
            w.WriteBool(_armorTickActive);
            w.WriteBool(_armorStartsOnFirstTick);
            w.WriteBool(FrozenForSimulationTick);
            SnapshotCodecs.Write(w, ActivationInput);
            w.WriteByte(Slot);
            w.WriteUShort(Cooldown);
            w.WriteBool(AirborneAtStart);
            w.WriteULong(ActivationId);
            w.WriteByte(PresentationAttackSequence);
            w.WriteInt(PresentationOperationIndex);
            w.WriteByte(AnimIndex);
        }

        /// <summary>Restore the state <see cref="WriteSnapshot"/> wrote into a freshly constructed, wired instance.</summary>
        internal virtual void ReadSnapshot(SnapshotReader r)
        {
            _hitGroups = null;
            if (r.ReadBool())
            {
                _hitGroups = new Dictionary<byte, HashSet<ulong>>();
                int count = r.ReadCount(5);
                for (int i = 0; i < count; i++)
                {
                    byte group = r.ReadByte();
                    _hitGroups[group] = r.ReadSet() ?? throw SnapshotReader.Malformed("hit group");
                }
            }
            _armorWindowTicks = r.ReadUShort();
            _armorTickActive = r.ReadBool();
            _armorStartsOnFirstTick = r.ReadBool();
            FrozenForSimulationTick = r.ReadBool();
            ActivationInput = SnapshotCodecs.ReadInputState(r);
            Slot = r.ReadByte();
            Cooldown = r.ReadUShort();
            AirborneAtStart = r.ReadBool();
            ActivationId = r.ReadULong();
            PresentationAttackSequence = r.ReadByte();
            PresentationOperationIndex = r.ReadInt();
            AnimIndex = r.ReadByte();
        }

    }
}
