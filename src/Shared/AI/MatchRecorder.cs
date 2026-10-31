using System;
using System.Collections.Generic;

namespace SlopArena.Shared.AI;

/// <summary>
/// Accumulates telemetry during a self-play match: attempted inputs, accepted actions,
/// confirmed hits, combo links, per-tick positions, and swing records.
///
/// Swings are detected from the bot's PRE-TICK press (<c>RecordPresses</c>, called before the
/// sim consumes the input). <c>CharacterState.AttackSlot</c> is NOT a reliable start signal —
/// it persists as the "last used slot" and never returns to 0. Every nonzero press records an
/// attempt and provisional swing; only accepted swings count as whiffs or connected attacks.
/// Combo classification uses only the defender's authoritative ordinary-action availability;
/// it does not use an elapsed-time window.
/// </summary>
public sealed class MatchRecorder
{
    private sealed class PendingAction
    {
        public ActionAttempt Attempt = null!;
        public SwingRecord Swing = null!;
        public ulong ActivationId;
    }

    /// <summary>Largest horizontal distance (metres) at which an unselected fighter can
    /// become a swing's proximity <see cref="SwingRecord.ReferenceTarget"/>.</summary>
    public const float ReferenceSearchRadius = 12f;

    private readonly MatchRecord _record = new();
    private readonly Dictionary<ulong, PendingAction> _pendingActions = new();
    private readonly Dictionary<(ulong Attacker, ulong Target), ComboLink> _openCombos = new();
    private readonly Dictionary<(ulong Attacker, ulong Target), bool> _actionableSinceHit = new();
    private readonly Dictionary<ulong, SwingRecord> _acceptedSwings = new();
    private readonly List<ulong> _pendingActionIds = new();
    private readonly List<(ulong Attacker, ulong Target)> _openComboPairs = new();
    private readonly HashSet<(ulong Attacker, ulong Target)> _hitPairs = new();

    public MatchRecord Record => _record;

    /// <summary>Append every entity's position to <see cref="MatchRecord.Samples"/> each tick.
    /// Long-running recorders that log positions elsewhere can turn this off.</summary>
    public bool RecordPositionSamples { get; set; } = true;

    /// <summary>Finalize the record after the match loop. Returns the record.</summary>
    public MatchRecord Finish(int durationTicks, int seed, MatchOutcome outcome)
    {
        _record.DurationTicks = durationTicks;
        _record.Seed = seed;
        _record.WinnerEntityId = outcome.WinnerEntityId;
        _record.SharedVictory = outcome.IsSharedVictory;
        CloseCombos();
        return _record;
    }

    /// <summary>
    /// Explicitly end the current exchange without inventing a time-based combo boundary.
    /// Match loops use this for interruptions that are not represented by a stock change.
    /// </summary>
    public void RecordInterruption() => CloseCombos();

    /// <summary>Open attempted swings from the tick's presses. Call BEFORE sim.Tick.</summary>
    public void RecordPresses(ServerSimulation sim, int tick, IReadOnlyDictionary<ulong, InputState> inputs, CharacterDefinition def)
    {
        var states = sim.GetAllStates();
        foreach (var (id, input) in inputs)
        {
            if (input.ActiveSlot == 0) continue;
            if (!states.TryGetValue(id, out var st)) continue;

            bool air = !st.IsGrounded;
            var attempt = new ActionAttempt
            {
                EntityId = id, Tick = tick, ActiveSlot = input.ActiveSlot, Air = air,
            };
            _record.ActionAttempts.Add(attempt);

            int window = ActiveWindowTicks(sim.GetDefinition(id) ?? def, input.ActiveSlot, air);
            ulong targetId = st.TargetEntityId;
            float side = 0f, fwd = 0f, dy = 0f;
            if (targetId > 0 && states.TryGetValue(targetId, out var target))
            {
                float dx = target.PX - st.PX;
                float dz = target.PZ - st.PZ;
                dy = target.PY - st.PY;
                (side, fwd) = FacingMath.ToFacingFrame(dx, dz, st.FacingYaw);
            }
            var swing = new SwingRecord
            {
                Attacker = id, Target = targetId, ActiveSlot = input.ActiveSlot, Air = air,
                StartTick = tick, WindowTicks = window,
                RelSide = side, RelForward = fwd, RelHeight = dy,
            };
            swing.ReferenceTarget = FindReferenceTarget(id, in st, states, out swing.ReferenceByProximity);
            if (swing.ReferenceTarget != 0)
            {
                var reference = states[swing.ReferenceTarget];
                swing.TargetGroundedAtStart = reference.IsGrounded;
                swing.TargetVXAtStart = reference.VX;
                swing.TargetVYAtStart = reference.VY;
                swing.TargetVZAtStart = reference.VZ;
            }
            _pendingActions[id] = new PendingAction
            {
                Attempt = attempt, Swing = swing, ActivationId = sim.GetLastActivationId(id),
            };
            _record.Swings.Add(swing);
        }
    }
    /// <summary>Capture the exact per-entity inputs chosen for this simulation tick.</summary>
    public void RecordInputs(int tick, IReadOnlyDictionary<ulong, InputState> inputs)
    {
        foreach (var (id, input) in inputs)
            _record.Inputs.Add(new InputSample { Tick = tick, EntityId = id, Input = input });
    }


    /// <summary>Accumulate hits, positions, and authoritative deaths. Call AFTER sim.Tick.</summary>
    public void RecordTick(ServerSimulation sim, int tick, IReadOnlyDictionary<ulong, InputState> inputs, CharacterDefinition def)
    {
        var states = sim.GetAllStates();
        _hitPairs.Clear();

        bool stockBoundary = sim.LastTickDeaths.Count > 0;
        if (stockBoundary)
            _record.Deaths.AddRange(sim.LastTickDeaths);
        if (RecordPositionSamples)
            foreach (var (id, st) in states)
                _record.Samples.Add(new TickSample { Tick = tick, EntityId = id, PX = st.PX, PY = st.PY, PZ = st.PZ });
        if (stockBoundary)
            CloseCombos();
        _pendingActionIds.Clear();
        foreach (var id in _pendingActions.Keys)
            _pendingActionIds.Add(id);
        foreach (var id in _pendingActionIds)
        {
            var pending = _pendingActions[id];
            if (!states.TryGetValue(id, out var state))
            {
                _pendingActions.Remove(id);
                continue;
            }
            if (sim.GetLastActivationId(id) != pending.ActivationId)
            {
                pending.Swing.Accepted = true;
                pending.Swing.ActivationId = sim.GetLastActivationId(id);
                _acceptedSwings[pending.Swing.ActivationId] = pending.Swing;
                _record.AcceptedActions.Add(new AcceptedAction
                {
                    EntityId = pending.Attempt.EntityId,
                    Tick = pending.Attempt.Tick,
                    ActiveSlot = pending.Attempt.ActiveSlot,
                    Air = pending.Attempt.Air,
                });
                _pendingActions.Remove(id);
            }
            else if (state.State != ActionState.Warping)
            {
                _pendingActions.Remove(id);
            }
        }
        UpdateContactGeometry(sim, tick, states);

        if (!stockBoundary)
        {
            _openComboPairs.Clear();
            foreach (var pair in _openCombos.Keys)
                _openComboPairs.Add(pair);
            foreach (var id in sim.LastTickOrdinaryActionOpportunities)
                foreach (var pair in _openComboPairs)
                    if (pair.Target == id)
                        _actionableSinceHit[pair] = true;
        }

        
        foreach (var hit in sim.LastTickHits)
        {
            if (hit.Blocked)
            {
                if (_acceptedSwings.TryGetValue(hit.ActivationId, out var blockedSwing))
                    blockedSwing.Blocked = true;
                continue;
            }
            _openComboPairs.Clear();
            foreach (var pair in _openCombos.Keys)
                _openComboPairs.Add(pair);
            foreach (var pair in _openComboPairs)
            {
                bool continuation = pair.Attacker == hit.OwnerEntityId
                    && pair.Target == hit.TargetEntityId;
                bool hitsParticipant = pair.Attacker == hit.TargetEntityId
                    || pair.Target == hit.TargetEntityId;
                if (hitsParticipant && !continuation)
                    CloseCombo(pair);
            }

            bool hitAir = hit.Airborne;
            _record.Hits.Add(new HitEvent
            {
                Attacker = hit.OwnerEntityId,
                Target = hit.TargetEntityId,
                AttackSlot = hit.AttackSlot,
                ActivationId = hit.ActivationId,
                Air = hitAir,
                Damage = hit.Damage,
                Tick = tick,
            });
            if (_acceptedSwings.TryGetValue(hit.ActivationId, out var swing))
            {
                swing.Connected = true;
                if (hit.Damage > 0f) swing.DamagingContacts++;
            }
            var pairForHit = (hit.OwnerEntityId, hit.TargetEntityId);
            bool hadActionableWindow = _actionableSinceHit.TryGetValue(pairForHit, out var actionable)
                && actionable;
            if (!_openCombos.TryGetValue(pairForHit, out var combo))
            {
                combo = new ComboLink
                {
                    Attacker = hit.OwnerEntityId,
                    Target = hit.TargetEntityId,
                    Hits = 0,
                    StartTick = tick,
                    EndTick = tick,
                    IsTrueCombo = true,
                };
                _openCombos[pairForHit] = combo;
            }

            if (hadActionableWindow)
            {
                combo.IsTrueCombo = false;
                combo.IsPressureString = true;
            }
            combo.Hits++;
            combo.EndTick = tick;
            _actionableSinceHit[pairForHit] = false;
            _hitPairs.Add(pairForHit);
        }

        _openComboPairs.Clear();
        foreach (var pair in _openCombos.Keys)
            _openComboPairs.Add(pair);
        foreach (var id in sim.LastTickOrdinaryActionOpportunities)
            foreach (var pair in _openComboPairs)
                if (!_hitPairs.Contains(pair) && pair.Target == id)
                    _actionableSinceHit[pair] = true;
        if (stockBoundary)
            CloseCombos();
    }

    /// <summary>The selected target, else the nearest other fighter within range (ties by ID).</summary>
    private static ulong FindReferenceTarget(ulong attackerId, in CharacterState attacker,
        IReadOnlyDictionary<ulong, CharacterState> states, out bool byProximity)
    {
        byProximity = false;
        ulong selected = attacker.TargetEntityId;
        if (selected != 0 && selected != attackerId && states.ContainsKey(selected))
            return selected;

        ulong nearest = 0;
        float best = ReferenceSearchRadius * ReferenceSearchRadius;
        foreach (var (otherId, other) in states)
        {
            if (otherId == attackerId) continue;
            float dx = other.PX - attacker.PX;
            float dz = other.PZ - attacker.PZ;
            float distanceSq = dx * dx + dz * dz;
            if (distanceSq < best || (distanceSq == best && nearest != 0 && otherId < nearest))
            {
                best = distanceSq;
                nearest = otherId;
            }
        }
        byProximity = nearest != 0;
        return nearest;
    }

    /// <summary>
    /// Track how close each accepted activation's hitboxes came to its reference target,
    /// using the hitboxes exactly as the resolver tested them during the last tick,
    /// swept poses included.
    /// </summary>
    private void UpdateContactGeometry(ServerSimulation sim, int tick,
        IReadOnlyDictionary<ulong, CharacterState> states)
    {
        var tested = sim.Resolver.LastTickCheckedHitboxes;
        if (tested.Count == 0) return;
        var volumes = sim.LastTickAttackEntities;
        foreach (var hitbox in tested)
        {
            if (hitbox.ActivationId == 0
                || !_acceptedSwings.TryGetValue(hitbox.ActivationId, out var swing)
                || swing.Attacker != hitbox.OwnerId)
                continue;
            if (swing.LastHitboxTick != tick)
            {
                swing.HitboxTicks++;
                if (swing.FirstHitboxTick < 0) swing.FirstHitboxTick = tick;
                swing.LastHitboxTick = tick;
            }
            if (swing.ReferenceTarget == 0 || volumes == null) continue;
            foreach (var volume in volumes)
            {
                if (volume.Id != swing.ReferenceTarget || !volume.Active) continue;
                float gap = SpellResolver.SurfaceGap(in hitbox, in volume,
                    out float gapX, out float gapY, out float gapZ);
                if (hitbox.Sweep != null)
                    foreach (var pose in hitbox.Sweep)
                    {
                        var probe = hitbox;
                        probe.X = pose.X; probe.Y = pose.Y; probe.Z = pose.Z;
                        probe.EndX = pose.EndX; probe.EndY = pose.EndY; probe.EndZ = pose.EndZ;
                        float sweptGap = SpellResolver.SurfaceGap(in probe, in volume,
                            out float sweptX, out float sweptY, out float sweptZ);
                        if (sweptGap >= gap) continue;
                        gap = sweptGap; gapX = sweptX; gapY = sweptY; gapZ = sweptZ;
                    }
                if (swing.HasClosestApproach && gap >= swing.ClosestGap) continue;
                RecordClosestApproach(swing, tick, gap, gapX, gapY, gapZ, states);
            }
        }
    }

    private static void RecordClosestApproach(SwingRecord swing, int tick, float gap,
        float gapX, float gapY, float gapZ, IReadOnlyDictionary<ulong, CharacterState> states)
    {
        swing.HasClosestApproach = true;
        swing.ClosestGap = gap;
        swing.ClosestTick = tick;
        swing.MissWorldX = gapX;
        swing.MissWorldY = gapY;
        swing.MissWorldZ = gapZ;
        if (!states.TryGetValue(swing.Attacker, out var attacker)) return;
        (swing.MissSide, swing.MissForward) = FacingMath.ToFacingFrame(gapX, gapZ, attacker.FacingYaw);
        swing.MissUp = gapY;
        if (!states.TryGetValue(swing.ReferenceTarget, out var target)) return;
        (swing.ClosestRelSide, swing.ClosestRelForward) = FacingMath.ToFacingFrame(
            target.PX - attacker.PX, target.PZ - attacker.PZ, attacker.FacingYaw);
        swing.ClosestRelHeight = target.PY - attacker.PY;
        swing.TargetGroundedAtClosest = target.IsGrounded;
        swing.TargetVXAtClosest = target.VX;
        swing.TargetVYAtClosest = target.VY;
        swing.TargetVZAtClosest = target.VZ;
    }

    private void CloseCombos()
    {
        _openComboPairs.Clear();
        foreach (var pair in _openCombos.Keys)
            _openComboPairs.Add(pair);
        foreach (var pair in _openComboPairs)
            CloseCombo(pair);
        _actionableSinceHit.Clear();
    }

    private void CloseCombo((ulong Attacker, ulong Target) pair)
    {
        if (_openCombos.TryGetValue(pair, out var combo))
        {
            if (combo.Hits >= 2)
                _record.Combos.Add(combo);
            _openCombos.Remove(pair);
        }
        _actionableSinceHit.Remove(pair);
    }

    /// <summary>Active window for the resolved slot's timeline, or legacy first-stage hitboxes.</summary>
    private static int ActiveWindowTicks(CharacterDefinition def, byte activeSlot, bool airborne)
    {
        var cooked = def.GetCookedSlotAbility(activeSlot, airborne);
        if (cooked != null)
        {
            int total = 0;
            int effectEnd = 0;
            foreach (var stage in cooked.Timeline.Stages)
            {
                foreach (var operation in stage.Operations)
                {
                    int end = operation.Tick;
                    if (operation is CookedSpawnProjectileOperation projectile)
                        end += projectile.Projectile.MaxFlightTicks;
                    else if (operation is CookedStartCapabilityOperation capability)
                        end += CapabilityWindowTicks(capability.Parameters);
                    effectEnd = Math.Max(effectEnd, total + end);
                }
                total += stage.DurationTicks;
            }
            return Math.Max(total, effectEnd);
        }

        return 0;
    }
    private static int CapabilityWindowTicks(CookedCapabilityParameters parameters)
        => parameters switch
        {
            CookedRisingDragonCapabilityParameters x => x.RiseDelay + x.RiseTicks,
            CookedCycloneKickCapabilityParameters x => x.DurationTicks,
            CookedDashStrikeCapabilityParameters x => x.TravelTicks + x.TipHitbox.DurationTicks + x.RecoveryTicks,
            CookedWibouRisingSlashCapabilityParameters x => x.RiseTicks,
            CookedFlashCutCapabilityParameters x => x.LongestTicks,
            CookedTargetedLeapCapabilityParameters x => x.MaxAimTicks + x.MaxFlightTicks + x.RecoveryTicks,
            CookedThrustCapabilityParameters x => x.ThrustTicks,
            CookedCannonballCapabilityParameters x => x.TakeoffTick + x.MaxFlightTicks,
            CookedCommandGrabCapabilityParameters x => x.CaptureTick + x.Capture.DurationTicks + x.WhiffRecoveryTicks,
            CookedTetherCapabilityParameters { AtTarget: { } at } x => x.ThrowTick + at.WarningTicks + at.CatchTicks
                + Math.Max(x.MaxReelTicks, x.MissRecoveryTicks),
            CookedTetherCapabilityParameters { Projectile: { } ring } x => x.ThrowTick + ring.MaxFlightTicks
                + Math.Max(x.MaxReelTicks, (int)MathF.Ceiling(ring.Speed / x.RetractSpeed * ring.MaxFlightTicks)
                    + x.MissRecoveryTicks),
            CookedSteeredChargeCapabilityParameters x => x.MaxGallopTicks + x.AirStrideTicks + x.LookDownTicks
                + Math.Max(x.HitEndingTicks, x.MissEndingTicks),
            CookedMankiRoundBombCapabilityParameters x => x.MaxFlightTicks + x.LifetimeTicks + x.WarningTicks + x.ExplosionDurationTicks,
            CookedMankiJetpackBoostCapabilityParameters x => x.StartupTicks + x.FuelTicks + x.ExplosionDurationTicks,
            CookedMankiKamikazeCapabilityParameters x => x.RunTicks + x.ExplosionDurationTicks,
            CookedMankiBazookaCapabilityParameters x => x.ChargeTicks + x.FireTriggerTick + x.MaxFlightTicks + x.RecoveryDuration,
            CookedChargedStreamCapabilityParameters x => x.MaxChargeTicks + x.FireTriggerTick + x.MaxStreamTicks
                + Math.Max(x.RecoveryTicks, x.Projectile.MaxFlightTicks),
            CookedFingerGunsCapabilityParameters x => x.FireTriggerTick
                + Math.Max(x.ReloadTicks, Math.Max(x.RecoveryTicks, x.Projectile.MaxFlightTicks)),
            _ => 0,
        };
}
