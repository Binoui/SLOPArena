using System;
using System.Collections.Generic;
using SlopArena.Shared.Mayhem;

namespace SlopArena.Shared;

/// <summary>Mayhem fighter-on-fighter shoves. Provisional.</summary>
public static class MayhemShoveTuning
{
    public const float Speed = 5f;
    public const ushort StunTicks = 12;
}

public partial class ServerSimulation : IMayhemWorld
{
    public const ulong MayhemHostEntityId = MayhemShow.HostEntityId;
    private readonly MayhemShow? _mayhem;
    private readonly Dictionary<ulong, CharacterState> _mayhemPrevious = new();

    public bool IsMankiMayhem => _mayhem != null;
    /// <summary>The show at the current tick; null outside Mayhem or before Begin.</summary>
    public MayhemShowView? MayhemView => _mayhem?.View(_tick, this);
    public IReadOnlyList<MayhemEvent> LastTickMayhemEvents => _mayhem?.Events ?? Array.Empty<MayhemEvent>();
    internal MayhemShow? MayhemShow => _mayhem;

    public void BeginMankiMayhem(uint seed)
    {
        if (_mayhem == null || _tick != 0) throw new InvalidOperationException("Mayhem must begin from a clean Mayhem Match.");
        _mayhem.Begin(seed, SortedEntityIds());
    }

    public void WithdrawMayhemPlayer(ulong id)
    {
        if (_mayhem == null || !_states.ContainsKey(id)) return;
        _mayhem.Withdraw(id);
        PinMayhemFighter(id, MayhemPose.Of(_states[id]));
    }

    /// <summary>The gate the next tick applies to <paramref name="id"/>; null outside Mayhem.</summary>
    public MayhemControls? MayhemControlsFor(ulong id) => _mayhem?.ControlsFor(id, _tick + 1);

    /// <summary>The channel's intent for a CPU contestant, planned <paramref name="lookaheadTicks"/> ahead.</summary>
    public MayhemCpuIntent MayhemCpuIntentFor(ulong id, int lookaheadTicks, int slipPercent = 0)
        => _mayhem?.CpuIntent(id, _tick, this, lookaheadTicks, slipPercent) ?? MayhemCpuIntent.Wait;

    private bool IsMayhemShove(ulong owner, ulong target)
        => _rule is MankiMayhemMatchRule { FriendlyFire: FriendlyFireMode.Shoves }
            && owner != target && _states.ContainsKey(owner) && _states.ContainsKey(target);

    ArenaDefinition IMayhemWorld.Arena => _arena;
    SpellResolver IMayhemWorld.Resolver => _spellResolver;
    CharacterState IMayhemWorld.Fighter(ulong id) => _states[id];
    CharacterDefinition IMayhemWorld.Definition(ulong id) => Def(id);

    void IMayhemWorld.ClearShowEffects()
    {
        _spellResolver.Clear();
        _heldCuts.Clear();
        _pendingPops.Clear();
        _lastHitCredits.Clear();
        _lastHitContexts.Clear();
    }

    void IMayhemWorld.PlaceAtStart(ulong id, MayhemPose start)
    {
        var previous = _states[id];
        if (previous.InteractionId != 0) InterruptInteraction(ref previous);
        RetireAbility(id, ref previous, AbilityExit.Reset);
        ForgetPerches(id);
        var definition = Def(id);
        var state = new CharacterState
        {
            EntityId = id, PX = start.X, PY = start.Y, PZ = start.Z, FacingYaw = start.Yaw, AimYaw = start.Yaw,
            IsGrounded = true, State = ActionState.Idle, MatchState = previous.MatchState,
            JumpsLeft = definition.Movement.MaxJumps, AirDodgesLeft = 1,
            AttackSequence = previous.AttackSequence, AutoLockSuppressed = previous.AutoLockSuppressed,
            LastTerminalInteractionId = previous.LastTerminalInteractionId,
            InteractionTerminalTick = previous.InteractionTerminalTick,
            Form = new FormState { Active = previous.Form.Active },
        };
        Flying.Refuel(ref state, definition);
        _states[id] = state;
        _animFrames[id] = 0; _prevAnimIndex[id] = -1;
        SetRespawnPosition(id, start.X, start.Y, start.Z, start.Yaw);
        _lastHitCredits.Remove(id); _lastHitContexts.Remove(id);
    }

    void IMayhemWorld.Splat(ulong id, MayhemPose anchor) => PinMayhemFighter(id, anchor);

    /// <summary>Cancels everything the fighter runs without gameplay end effects and pins it; attached effects never explode.</summary>
    private void PinMayhemFighter(ulong id, MayhemPose anchor)
    {
        var state = _states[id];
        if (state.InteractionId != 0) InterruptInteraction(ref state);
        RetireAbility(id, ref state, AbilityExit.Reset);
        _spellResolver.RemoveAttachedTo(id);
        _spellResolver.RemoveOwnerEffects(id);
        _spellResolver.StopHomingAt(id);
        ForgetPerches(id);
        state.PX = anchor.X; state.PY = anchor.Y; state.PZ = anchor.Z; state.FacingYaw = anchor.Yaw;
        state.VX = state.VY = state.VZ = state.KVX = state.KVY = state.KVZ = 0f;
        state.HitstopTicks = 0; state.HitstunTicks = 0; state.BlockStunTicks = 0; state.LandingLagTicks = 0;
        state.AnimLockTicks = 0; state.InvincibilityTicks = 0; state.BufferedSlot = 0;
        state.TargetEntityId = 0; state.LockOn = false; state.IsGrounded = true; state.State = ActionState.Idle;
        state.Flight = default; state.DamagePercent = 0;
        _states[id] = state;
        _lastHitCredits.Remove(id); _lastHitContexts.Remove(id);
    }

    /// <summary>
    /// The gate's first point: every contestant's raw input is filtered before PreTickAbilities, forbidden buffered
    /// presses are dropped and a forbidden running move is cancelled. Inactive contestants consume no input.
    /// </summary>
    private Dictionary<ulong, InputState> AdmitMayhemInputs(Dictionary<ulong, InputState> inputs)
    {
        var admitted = new Dictionary<ulong, InputState>(inputs.Count);
        foreach (ulong id in SortedEntityIds())
        {
            if (_mayhem!.Inactive(id)) continue;
            var gate = _mayhem.ControlsFor(id, _tick);
            var state = _states[id];
            gate.ClearForbidden(ref state);
            if (_activeAbilities.TryGetValue(id, out var running) && !gate.AdmitsSlot((byte)(running.Slot + 1)))
                RetireAbility(id, ref state, AbilityExit.Reset);
            _states[id] = state;
            if (inputs.TryGetValue(id, out var raw)) admitted[id] = gate.Filter(raw);
        }
        return admitted;
    }

    private bool AdmitsMayhemSlot(ulong id, byte wireSlot)
        => _mayhem == null || _mayhem.ControlsFor(id, _tick).AdmitsSlot(wireSlot);

    private IEnumerable<SpellResolver.EntityData> MayhemCollisionTargets()
    {
        var scene = _mayhem!.Scene;
        foreach (var wall in RuntimeSolidSet.CollisionTargets(scene.Solids)) yield return wall;
        foreach (var target in scene.Targets)
            yield return new SpellResolver.EntityData
            {
                Id = target.EntityId, Active = true, RuntimeSolid = true, Shape = HitboxShape.Capsule,
                PosX = target.Center.X, PosY = target.Center.Y - target.HalfHeight + target.Radius, PosZ = target.Center.Z,
                EndX = target.Center.X, EndY = target.Center.Y + target.HalfHeight - target.Radius, EndZ = target.Center.Z,
                Radius = target.Radius,
            };
    }

    private void CaptureMayhemPositions()
    {
        if (_mayhem == null) return;
        _mayhemPrevious.Clear();
        foreach (var pair in _states) _mayhemPrevious.Add(pair.Key, pair.Value);
    }

    private void ConstrainMayhemFighters()
    {
        if (_mayhem == null) return;
        var solids = _mayhem.Scene.Solids;
        if (solids.Count == 0) return;
        foreach (var id in SortedEntityIds())
        {
            var state = _states[id];
            if (IsEliminated(state)) continue;
            var before = state;
            RuntimeSolidSet.Constrain(solids, _mayhemPrevious[id], ref state, Def(id));
            StopShellAtRuntimeWall(id, in before, ref state);
            _states[id] = state;
        }
    }

    /// <summary>
    /// Routes a contact before armour, invincibility, damage, hitstop and knockback. Host hazards and hits on
    /// channel objects go to the director; a fighter-on-fighter contact under Shoves becomes a bounded push.
    /// Returns false only for contacts the ordinary path still owns.
    /// </summary>
    private bool TryResolveMayhemContact(in SpellResolver.HitResult hit)
    {
        if (_mayhem!.Inactive(hit.OwnerEntityId) || _mayhem.Inactive(hit.TargetEntityId)) return true;
        if (hit.OwnerEntityId == MayhemHostEntityId)
        {
            MayhemHazards.TryDecode(hit.ActivationId, out _, out byte lane, out ushort ordinal);
            if (_states.ContainsKey(hit.TargetEntityId))
                _mayhem!.Contact(new MayhemContact(MayhemContactKind.HazardBody, lane, ordinal, MayhemHostEntityId,
                    hit.TargetEntityId, _mayhem.SeatOf(hit.TargetEntityId), hit.ActivationId), _tick, this);
            return true;
        }
        if (RuntimeSolidSet.IsWallId(hit.TargetEntityId) || MayhemEvent.IsTargetId(hit.TargetEntityId))
        {
            ResolveMayhemObjectHit(in hit);
            return true;
        }
        if (IsMayhemShove(hit.OwnerEntityId, hit.TargetEntityId))
        {
            ApplyMayhemShove(in hit);
            return true;
        }
        return false;
    }

    private void ResolveMayhemObjectHit(in SpellResolver.HitResult hit)
    {
        if (hit.Damage <= 0 || !_states.TryGetValue(hit.OwnerEntityId, out var owner)) return;
        float damage = hit.Damage, force = hit.BaseKnockback;
        if (ContactAbility(hit) is { } ability)
            ability.OnHitObject(ref owner, ability.CharacterDef ?? Def(hit.OwnerEntityId), ref damage, ref force);
        if (damage > 0)
        {
            _mayhem!.Contact(new MayhemContact(MayhemContactKind.SolidHit, 0, 0, hit.OwnerEntityId, hit.TargetEntityId, -1,
                hit.ActivationId), _tick, this);
            ushort freeze = ComputeHitstopTicks(damage);
            if (hit.FreezesOwner) owner.HitstopTicks = freeze;
            var accepted = hit; accepted.MatchTick = _tick; accepted.Damage = damage; accepted.HitstopTicks = freeze;
            LastTickHits.Add(accepted);
        }
        _states[hit.OwnerEntityId] = owner;
    }

    /// <summary>A bounded horizontal push with a short stun: no damage, no launch, no attacker gameplay hooks.</summary>
    private void ApplyMayhemShove(in SpellResolver.HitResult hit)
    {
        var target = _states[hit.TargetEntityId];
        var attacker = _states[hit.OwnerEntityId];
        if (target.InteractionId != 0) InterruptInteraction(ref target);
        RetireAbility(hit.TargetEntityId, ref target, AbilityExit.Superseded);
        Flying.End(ref target, Def(hit.TargetEntityId), hit.TargetEntityId != NoCooldownsEntityId);
        Simulation.ClearMovementInterruptionFlags(ref target);
        float dx = target.PX - attacker.PX, dz = target.PZ - attacker.PZ;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (length > .001f) { dx /= length; dz /= length; }
        else { dx = hit.DirX; dz = hit.DirZ; }
        target.KVX = dx * MayhemShoveTuning.Speed; target.KVZ = dz * MayhemShoveTuning.Speed; target.KVY = 0f;
        target.HitstunTicks = MayhemShoveTuning.StunTicks; target.HitstunLevel = 0;
        target.State = ActionState.Hitstun; target.StateTicks = 0; target.BufferedSlot = 0;
        target.HitReactionSequence = unchecked((ushort)(target.HitReactionSequence + 1));
        target.FacingYaw = MathF.Atan2(-dx, -dz);
        _states[hit.TargetEntityId] = target;
        var resolved = hit; resolved.MatchTick = _tick; resolved.Damage = 0f; resolved.DirX = dx; resolved.DirZ = dz;
        LastTickHits.Add(resolved);
    }

    /// <summary>A host blast wrecks the channel solids it reaches.</summary>
    private void ResolveMayhemHostBlast(float x, float y, float z, float radius, ulong activation)
    {
        var walls = new List<ulong>(RuntimeSolidSet.InBlast(_mayhem!.Scene.Solids, x, y, z, radius));
        MayhemHazards.TryDecode(activation, out _, out byte lane, out ushort ordinal);
        _mayhem.Contact(new MayhemContact(MayhemContactKind.BombImpact, lane, ordinal, MayhemHostEntityId, 0, -1, activation,
            new ArcadePoint(x, y, z)), _tick, this);
        foreach (ulong wall in walls)
            _mayhem.Contact(new MayhemContact(MayhemContactKind.HostBlast, lane, ordinal, MayhemHostEntityId, wall, -1, activation),
                _tick, this);
    }

    private (bool Exists, float X, float Y, float Z) StickyCarrier(ulong id)
        => _states.TryGetValue(id, out var state) && !IsEliminated(state)
            ? (true, state.PX, state.PY, state.PZ) : (false, 0f, 0f, 0f);
}
