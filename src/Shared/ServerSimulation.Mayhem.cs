using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public partial class ServerSimulation
{
    public const ulong MayhemHostEntityId = MankiMayhemSimulation.HostEntityId;
    private readonly MankiMayhemSimulation? _mayhem;
    public MankiMayhemState? MankiMayhem => _mayhem?.State;
    public IReadOnlyList<MayhemPlayer> MayhemPlayers => _mayhem?.Players ?? Array.Empty<MayhemPlayer>();
    public IReadOnlyList<MayhemLandingRing> MayhemLandingRings => _mayhem?.Rings ?? Array.Empty<MayhemLandingRing>();
    public IReadOnlyList<RuntimeSolid> MayhemWalls => _mayhem?.Solids.Walls ?? Array.Empty<RuntimeSolid>();
    private readonly Dictionary<ulong, CharacterState> _mayhemPrevious = new();
    public IReadOnlyList<MayhemSlam> MayhemSlams => _mayhem?.Slams ?? Array.Empty<MayhemSlam>();
    public IReadOnlyList<MankiMayhemEvent> LastTickMayhemEvents => _mayhem?.Events ?? Array.Empty<MankiMayhemEvent>();

    public void BeginMankiMayhem(uint seed)
    {
        if (_mayhem == null || _tick != 0) throw new InvalidOperationException("Mayhem must begin from a clean Mayhem Match.");
        _mayhem.Begin(seed, SortedEntityIds());
    }

    public void WithdrawMayhemPlayer(ulong id)
    {
        if (_mayhem == null || !_states.ContainsKey(id)) return;
        _mayhem.Withdraw(id); BenchMayhemFighter(id);
    }

    private bool IsMayhemShove(ulong owner, ulong target)
        => _rule is MankiMayhemMatchRule { FriendlyFire: FriendlyFireMode.Shoves }
            && owner != target && _states.ContainsKey(owner) && _states.ContainsKey(target);

    private void BenchMayhemFighter(ulong id)
    {
        _spellResolver.ExplodeAttachedTo(id);
        FreezeCourseFighter(id);
        var state = _states[id]; state.InvincibilityTicks = 0; _states[id] = state;
    }

    private void ResetMayhemFighters()
    {
        var ids = SortedEntityIds();
        for (int index = 0; index < ids.Count; index++)
        {
            ulong id = ids[index];
            _spellResolver.RemoveMankiCans(id);
            if (_mayhem!.Inactive(id)) continue;
            var previous = _states[id];
            if (previous.InteractionId != 0) InterruptInteraction(ref previous);
            CancelAttackRuntime(id, ref previous, removeProjectiles: false);
            var start = MatchFormat.MankiMayhem.StartFor(_arena, index, ids.Count);
            start.Y = _arena.Arcade!.Mayhem!.DeckTop + _defs[id].CapsuleHeight * .5f;
            _states[id] = new CharacterState
            {
                EntityId = id, PX = start.X, PY = start.Y, PZ = start.Z, FacingYaw = start.Yaw,
                IsGrounded = true, State = ActionState.Idle, MatchState = previous.MatchState,
                DamagePercent = previous.DamagePercent, Deaths = previous.Deaths,
                JumpsLeft = _defs[id].Movement.MaxJumps, AirDodgesLeft = 1,
                AttackSequence = previous.AttackSequence, AutoLockSuppressed = previous.AutoLockSuppressed,
                LastTerminalInteractionId = previous.LastTerminalInteractionId,
                InteractionTerminalTick = previous.InteractionTerminalTick,
            };
            _animFrames[id] = 0; _prevAnimIndex[id] = -1;
            SetRespawnPosition(id, start.X, start.Y, start.Z, start.Yaw);
            _lastHitCredits.Remove(id); _lastHitContexts.Remove(id);
        }
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
        foreach (var id in SortedEntityIds())
        {
            var state = _states[id];
            if (IsEliminated(state)) continue;
            var before = state;
            _mayhem.Solids.Constrain(_mayhemPrevious[id], ref state, _defs[id]);
            StopShellAtRuntimeWall(id, in before, ref state);
            _states[id] = state;
        }
    }

    private void ResolveMayhemWallHit(in SpellResolver.HitResult hit)
    {
        if (_mayhem == null || hit.Damage <= 0 || !_states.TryGetValue(hit.OwnerEntityId, out var owner)) return;
        ulong target = hit.TargetEntityId;
        if (!MayhemWalls.Any(w => w.EntityId == target && w.Blocks)) return;
        float damage = hit.Damage, force = hit.BaseKnockback;
        ContactAbility(hit)?.OnHitObject(ref owner, _defs[hit.OwnerEntityId], ref damage, ref force);
        if (damage > 0)
        {
            _mayhem.HitWall(_tick, hit.TargetEntityId);
            ushort freeze = ComputeHitstopTicks(damage);
            if (hit.FreezesOwner) owner.HitstopTicks = freeze;
            var accepted = hit; accepted.MatchTick = _tick; accepted.Damage = damage; accepted.HitstopTicks = freeze;
            LastTickHits.Add(accepted);
        }
        _states[hit.OwnerEntityId] = owner;
    }

    private (bool Exists, float X, float Y, float Z) StickyCarrier(ulong id)
        => _states.TryGetValue(id, out var state) && !IsEliminated(state)
            ? (true, state.PX, state.PY, state.PZ) : (false, 0f, 0f, 0f);
}
