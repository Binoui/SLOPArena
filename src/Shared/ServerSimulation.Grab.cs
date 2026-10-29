using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared;

public partial class ServerSimulation
{
    private static readonly CookedStartupAimCorrectionOperation GrabStartupCorrection =
        new(0, AuthoringUnit.Ticks, DefenseConfig.GrabStartupTicks, 4f, 180f, 90f, 0f, 720f, 0f);

    private void BeginGrabStartupAim(ref CharacterState state, in InputState press)
    {
        BeginStartupAimCorrection(ref state, GrabStartupCorrection, in press);
        if (!_states.ContainsKey(state.AttackCorrectionTargetId))
            state.ClearStartupAimCorrection();
    }

    private void UpdateGrabStartupAim(ulong[] ids)
    {
        foreach (ulong id in ids)
        {
            if (!_states.TryGetValue(id, out var state) || state.State != ActionState.GrabAttempt
                || _hitstopFrozenThisTick.Contains(id)) continue;
            if (state.StateTicks > DefenseConfig.GrabWhiffRecoveryTicks + DefenseConfig.GrabActiveTicks)
            {
                UpdateStartupAimCorrection(ref state, GrabStartupCorrection, applyPose: true);
                int yaw = (int)MathF.Round(state.FacingYaw * (18000f / MathF.PI));
                yaw = (yaw + 18000) % 36000;
                if (yaw < 0) yaw += 36000;
                state.CapturedYaw = (short)(yaw - 18000);
                state.FacingYaw = state.AimYaw = state.CapturedYaw * (MathF.PI / 18000f);
            }
            else state.ClearStartupAimCorrection();
            _states[id] = state;
        }
    }

    private bool PrepareBeelinePress(ref CharacterState state, ref InputState input)
    {
        if (state.State is ActionState.HomingDashFloat or ActionState.HomingDash)
        {
            if (state.HitstopTicks == 0) HeldPress.CaptureLatest(ref state, in input);
            input.ActiveSlot = 0;
            input.GrabPressed = false;
            return true;
        }
        if (state.State == ActionState.HomingDashArrive)
        {
            if (input.ActiveSlot != 0 || input.GrabPressed)
            {
                state.BufferedSlot = 0;
                HeldPress.CaptureLatest(ref state, in input);
                if (input.ActiveSlot != 0)
                {
                    state.BufferedSlot = 0;
                    input.GrabPressed = false;
                    return false;
                }
            }
            if (HeldPress.IsGrab(in state))
            {
                state.BufferedSlot = 0;
                input.GrabPressed = state.IsGrounded;
                input.ActiveSlot = 0;
                input.Jump = input.ShieldHeld = input.ShieldPressed = false;
                return true;
            }
        }
        else if (HeldPress.IsGrab(in state)) state.BufferedSlot = 0;
        return false;
    }

    private void BeginGrabThrow(ref CharacterState holder, ref CharacterState victim, in InputState press)
    {
        bool fling = RecoveryTurn.TryStickYaw(in press, out float yaw);
        var spec = GrabThrow.Specification(fling ? GrabThrow.Kind.Fling : GrabThrow.Kind.UpToss);
        if (fling) TurnGrabPair(ref holder, ref victim, yaw);
        holder.State = ActionState.Throwing;
        holder.InteractionPhase = victim.InteractionPhase = spec.WindupPhase;
        holder.InteractionTick = victim.InteractionTick = _tick;
        holder.StateTicks = victim.StateTicks = DefenseConfig.ThrowReleaseTicks;
        holder.VX = holder.VY = holder.VZ = victim.VX = victim.VY = victim.VZ = 0f;
    }

    private void TurnGrabPair(ref CharacterState holder, ref CharacterState victim, float requestedYaw)
    {
        float start = holder.CapturedYaw * (MathF.PI / 18000f);
        float delta = CorrectionAngleDelta(requestedYaw - start);
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 12f)));
        for (int step = 1; step <= steps; step++)
        {
            var candidate = holder;
            int yaw = (int)MathF.Round((start + delta * step / steps) * (18000f / MathF.PI));
            yaw = (yaw + 18000) % 36000;
            if (yaw < 0) yaw += 36000;
            candidate.CapturedYaw = (short)(yaw - 18000);
            candidate.FacingYaw = candidate.AimYaw = candidate.CapturedYaw * (MathF.PI / 18000f);
            if (!CanPlaceVictim(in candidate, in victim, Def(holder.EntityId), Def(victim.EntityId),
                out float x, out float y, out float z)) break;
            holder = candidate;
            victim.PX = x; victim.PY = y; victim.PZ = z;
            victim.CapturedYaw = holder.CapturedYaw;
            victim.FacingYaw = victim.AimYaw = holder.FacingYaw;
        }
    }

    private static void MirrorGrabPairClocks(ref CharacterState holder, ref CharacterState victim, uint elapsed)
    {
        int duration = holder.State == ActionState.Grabbing ? DefenseConfig.GrabHoldTicks : DefenseConfig.ThrowReleaseTicks;
        holder.StateTicks = victim.StateTicks = (ushort)Math.Max(0, duration - (int)elapsed);
    }

    private void ReleaseGrabThrow(ref CharacterState holder, ref CharacterState victim)
    {
        GrabThrow.TryDecode(holder.InteractionPhase, out var kind, out _);
        var spec = GrabThrow.Specification(kind);
        ulong interaction = holder.InteractionId;
        float yaw = holder.CapturedYaw * (MathF.PI / 18000f);
        float dirX = kind == GrabThrow.Kind.UpToss ? 0f : MathF.Sin(yaw);
        float dirZ = kind == GrabThrow.Kind.UpToss ? 0f : MathF.Cos(yaw);
        holder.ClearAttackFields();
        victim.ClearAttackFields();
        ClearReaction(ref victim);
        victim.State = ActionState.Idle;
        victim.StateTicks = victim.AnimLockTicks = 0;
        victim.CrouchSettled = false;
        holder.FacingYaw = holder.AimYaw = yaw;
        float dx = holder.PX - victim.PX, dz = holder.PZ - victim.PZ;
        victim.FacingYaw = victim.AimYaw = dx * dx + dz * dz > 0.000001f ? MathF.Atan2(dx, dz) : yaw + MathF.PI;
        float damage = IsMayhemShove(holder.EntityId, victim.EntityId) ? 0f : spec.Damage;
        victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
        holder.State = ActionState.Throwing;
        holder.StateTicks = holder.AnimLockTicks = spec.RecoveryTicks;
        holder.VX = holder.VY = holder.VZ = 0f;
        ushort freeze = ComputeHitstopTicks(spec.Damage);
        var hit = new SpellResolver.HitResult
        {
            OwnerEntityId = holder.EntityId, TargetEntityId = victim.EntityId,
            Form = holder.Form.Active, Damage = damage, DirX = dirX, DirZ = dirZ,
            KnockbackAngle = spec.AngleDegrees, BaseKnockback = spec.BaseKnockback,
            KnockbackGrowth = spec.GrowthKnockback, StunTicks = 1, FreezesOwner = true,
            HitX = victim.PX, HitY = victim.PY, HitZ = victim.PZ, MatchTick = _tick, HitstopTicks = freeze,
            ImpactForce = (spec.BaseKnockback + spec.GrowthKnockback * (victim.DamagePercent * 0.01f + 1f)
                + damage * 0.1f) * 200f / MathF.Max(0.01f, Def(victim.EntityId).Weight + 100f) * CombatTuning.KnockbackScale,
        };
        QueueHitReaction(in hit, ref victim, ref holder, true, freeze, dirX, dirZ,
            spec.BaseKnockback, spec.GrowthKnockback, 0f, damage, false, false, false);
        if (damage > 0f) _damagedThisTick.Add(victim.EntityId);
        if (_mayhem == null) _lastHitCredits[victim.EntityId] = (holder.EntityId, _tick, 0);
        _lastHitContexts[victim.EntityId] = (holder.EntityId, _tick, 0);
        LastTickHits.Add(hit);
        MarkInteractionTerminal(ref holder, interaction);
        MarkInteractionTerminal(ref victim, interaction);
        holder.InteractionPhase = victim.InteractionPhase = spec.ReleasedPhase;
    }
}
