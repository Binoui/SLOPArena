using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's R — Cyclone Kick: forward lunge with per-tick hitboxes.
    /// Each opponent can be hit once per activation, or once per hit interval when
    /// the tuning carries victims with repeated hits; an optional finisher window
    /// launches them. The dizzy recovery gag plays only when no opponent was hit.
    /// </summary>
    public class FightGuyCycloneKick : ServerAbility
    {
        public override bool OwnsVerticalMotion => true;
        private readonly CookedCycloneKickCapabilityParameters _parameters;
        private ushort _ticks;
        private HashSet<ulong> _hitEntities = new();
        private bool _landedHit;

        public const string DizzyPresentationId = "presentation.fightguy.cyclone-kick.dizzy";
        /// <summary>A whiff or a blocked kick ends dizzy; a landed hit skips the gag.</summary>
        public bool ShowsDizzy => !_landedHit;

        public FightGuyCycloneKick(CookedCycloneKickCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _hitEntities.Clear();
            _landedHit = false;
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsFastFalling = false;
            ClearVelocityOwnership(ref s);
            SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            s.IsFastFalling = false;
            ClearVelocityOwnership(ref s);
            _ticks++;
            if (_ticks > _parameters.DurationTicks)
                return;
            // Movement integrates before ability ticks: brake after the last travel step.
            // Recovery must leave gravity and externally applied velocity alone.
            if (_ticks == _parameters.DurationTicks)
                s.VX = s.VZ = 0f;
            else
                SetVelocityInFacing(ref s, _parameters.ForwardSpeed);
            if (_ticks <= _parameters.WindupTicks || _ticks > _parameters.HitboxEndTick)
                return;
            bool finisher = _parameters.FinisherTick > 0 && _ticks >= _parameters.FinisherTick;
            if (_ticks == _parameters.FinisherTick
                || !finisher && _parameters.HitIntervalTicks > 0
                    && (_ticks - _parameters.WindupTicks - 1) % _parameters.HitIntervalTicks == 0)
                _hitEntities.Clear();

            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);
            SpawnDynamicHitbox(ref s, _parameters.BodyRadius, 0f, _parameters.BodyY, 0f, true, finisher);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, _parameters.SideOffset * sin, _parameters.SideY, _parameters.SideOffset * cos, false, finisher);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, _parameters.SideOffset * cos, _parameters.SideY, -_parameters.SideOffset * sin, false, finisher);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, -_parameters.SideOffset * sin, _parameters.SideY, -_parameters.SideOffset * cos, false, finisher);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, -_parameters.SideOffset * cos, _parameters.SideY, _parameters.SideOffset * sin, false, finisher);
        }

        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
            => _landedHit = true;

        private void SpawnDynamicHitbox(ref CharacterState s, float radius, float x, float y, float z, bool freezesOwner, bool finisher)
        {
            SpawnResolverHitbox(new Hitbox
            {
                X = s.PX + x,
                Y = s.PY + y,
                Z = s.PZ + z,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = finisher ? _parameters.FinisherDamage : _parameters.Damage,
                BaseKnockback = finisher ? _parameters.FinisherKnockbackBase : _parameters.KnockbackBase,
                KnockbackGrowth = finisher ? _parameters.FinisherKnockbackGrowth : _parameters.KnockbackGrowth,
                KnockbackAngle = (sbyte)(finisher ? _parameters.FinisherKnockbackAngle : _parameters.KnockbackAngle),
                StunTicks = _parameters.StunTicks,
                FixedHitstunTicks = finisher ? (ushort)0 : _parameters.FixedHitstunTicks,
                DurationTicks = 2,
                OwnerId = s.EntityId,
                AttackSlot = (byte)(Slot + 1),
                FreezesOwner = freezesOwner,
                CancelWithAbility = true,
                HitsMultipleOpponents = true,
                HitEntities = _hitEntities,
            });
        }

        internal override void WriteSnapshot(SnapshotWriter w)
        {
            base.WriteSnapshot(w);
            w.WriteUShort(_ticks);
            w.WriteSet(_hitEntities);
            w.WriteBool(_landedHit);
        }

        internal override void ReadSnapshot(SnapshotReader r)
        {
            base.ReadSnapshot(r);
            _ticks = r.ReadUShort();
            _hitEntities = r.ReadSet() ?? throw SnapshotReader.Malformed("FightGuyCycloneKick hit set");
            _landedHit = r.ReadBool();
        }
    }

}
