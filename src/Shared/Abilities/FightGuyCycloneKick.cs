using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's R — Cyclone Kick: forward lunge with per-tick hitboxes.
    /// Each opponent can be hit once per activation.
    /// </summary>
    public class FightGuyCycloneKick : ServerAbility
    {
        public override bool OwnsVerticalMotion => true;
        private readonly CookedCycloneKickCapabilityParameters _parameters;
        private ushort _ticks;
        private HashSet<ulong> _hitEntities = new();

        public FightGuyCycloneKick(CookedCycloneKickCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _ticks = 0;
            _hitEntities.Clear();
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

            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);
            SpawnDynamicHitbox(ref s, _parameters.BodyRadius, 0f, _parameters.BodyY, 0f, true);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, _parameters.SideOffset * sin, _parameters.SideY, _parameters.SideOffset * cos, false);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, _parameters.SideOffset * cos, _parameters.SideY, -_parameters.SideOffset * sin, false);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, -_parameters.SideOffset * sin, _parameters.SideY, -_parameters.SideOffset * cos, false);
            SpawnDynamicHitbox(ref s, _parameters.SideRadius, -_parameters.SideOffset * cos, _parameters.SideY, _parameters.SideOffset * sin, false);
        }

        private void SpawnDynamicHitbox(ref CharacterState s, float radius, float x, float y, float z, bool freezesOwner)
        {
            SpawnResolverHitbox(new Hitbox
            {
                X = s.PX + x,
                Y = s.PY + y,
                Z = s.PZ + z,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = _parameters.Damage,
                BaseKnockback = _parameters.KnockbackBase,
                KnockbackGrowth = _parameters.KnockbackGrowth,
                KnockbackAngle = (sbyte)_parameters.KnockbackAngle,
                StunTicks = _parameters.StunTicks,
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
        }

        internal override void ReadSnapshot(SnapshotReader r)
        {
            base.ReadSnapshot(r);
            _ticks = r.ReadUShort();
            _hitEntities = r.ReadSet() ?? throw SnapshotReader.Malformed("FightGuyCycloneKick hit set");
        }
    }

}
