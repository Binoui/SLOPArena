using System;
using System.Linq;
using System.Collections.Generic;

namespace SlopArena.Shared.AI;

public enum CpuBehavior
{
    Attack,
    Shield,
    Avoid,
}

/// <summary>
/// Deterministic heuristic bot policy v1 (issue #148): approach the opponent, attack when the
/// opponent is within the bot's (seeded-jittered) perceived range, pick from the move set with
/// seeded variation, back off after each swing and after being hit so fights develop spacing,
/// and jump toward an airborne opponent. The seeded jitter makes the bot sometimes attack at
/// the edge of its reach (whiffs) and use a mix of moves — without it, two identical bots
/// trading at guaranteed-connect range produce degenerate telemetry (100% hit rate, no KOs).
///
/// Attack reach is the move's ACTUAL hitbox reach (OffX/OffZ extent + radius + lunge), NOT the
/// authored <c>AttackRange</c> (the sim's engage distance, far beyond where the hitbox
/// connects). The sim normalizes <c>MoveX/Y</c> (no analog easing), so the policy approaches
/// outright rather than trying to slow precisely.
///
/// The sim consumes <see cref="InputState.MoveX"/>/<c>MoveY</c> as WORLD-SPACE X/Z and auto-faces
/// on movement input (<c>Atan2</c>); the policy also snaps facing via <c>AimYaw</c> +
/// <c>FaceToCamera</c> (ADR-0017).
///
/// Invariants: <c>MoveX/MoveY</c> magnitude ≤ 1; no action input while
/// hitstun/hitstop/recovery/landing-lag/anim-lock; no slot press on cooldown or while its
/// returning projectile is out.
/// Same <c>Random</c> stream → same decisions.
/// </summary>
public sealed class HeuristicBotPolicy
{
    /// <summary>
    /// Canonical functional combat slots, each once: 1, 3, 4, A, E, R, F. The grounded and aerial profiles
    /// are indexed alike. LMB/RMB are physical controls with no canonical package slot identity.
    /// </summary>
    private static readonly byte[] Slots = CanonicalSlotProjection.All.Where(slot => !slot.IsAirborne)
        .Select(CanonicalSlotProjection.ToWire).ToArray();

    private const float JumpGap = 0.6f;
    private const float VictimRadiusMargin = 0.2f;
    private const float SlotReachTolerance = 0.85f;
    private const ushort AimHoldTicks = 12;

    private readonly struct MoveCandidate
    {
        public readonly byte Slot;
        public readonly float Reach;
        public readonly float Damage;
        public readonly float RecoveryHorizontal;
        public readonly float RecoveryVertical;
        public readonly bool RecoveryRequiresDelayedOpponent;
        public readonly bool Functional;
        public readonly bool HasDamage;
        public readonly bool HasMovement;
        public readonly bool RequiresAim;
        public readonly bool IsRecovery;
        public readonly ushort StartupTicks;
        public readonly ushort TravelTicks;
        public readonly float TravelSpeed;
        public readonly float TravelOffset;
        public readonly float ForwardTravel;
        public readonly int CommitmentTicks;

        public MoveCandidate(byte slot, float reach, float damage, bool functional,
            bool hasDamage, bool hasMovement, bool requiresAim, bool isRecovery,
            ushort startupTicks, ushort travelTicks = 0, float travelSpeed = 0f,
            float travelOffset = 0f, float recoveryHorizontal = 0f,
            float recoveryVertical = 0f, bool recoveryRequiresDelayedOpponent = false,
            float forwardTravel = 0f, int commitmentTicks = 0)
        {
            Slot = slot;
            Reach = MathF.Max(0f, reach);
            Damage = MathF.Max(0f, damage);
            RecoveryHorizontal = MathF.Max(0f, recoveryHorizontal);
            RecoveryVertical = MathF.Max(0f, recoveryVertical);
            RecoveryRequiresDelayedOpponent = recoveryRequiresDelayedOpponent;
            Functional = functional;
            HasDamage = hasDamage;
            HasMovement = hasMovement;
            RequiresAim = requiresAim;
            IsRecovery = isRecovery;
            StartupTicks = startupTicks;
            TravelTicks = travelTicks;
            TravelSpeed = MathF.Max(0f, travelSpeed);
            TravelOffset = MathF.Max(0f, travelOffset);
            ForwardTravel = MathF.Max(0f, forwardTravel);
            CommitmentTicks = commitmentTicks + (requiresAim ? AimHoldTicks : 0);
        }
    }
    private readonly struct RecoveryTarget
    {
        public readonly float X, Y, Z;

        public RecoveryTarget(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    private const int MaxRecoveryTargets = 48;
    private const float NearEdgeDistance = 2f;
    private readonly RecoveryTarget[] _recoveryTargets = new RecoveryTarget[MaxRecoveryTargets];
    private float[]? _recoveryHeightmap;
    private CollisionTriangle[]? _recoveryTriangles;
    private int[] _recoveryTriangleCandidates = Array.Empty<int>();
    private int _recoveryTargetCount;
    private float _recoveryMinSurface;
    private float _recoveryCapsuleHeight;

    private static bool HasArenaBounds(in ArenaDefinition arena)
        => arena.MaxX > arena.MinX && arena.MaxZ > arena.MinZ;

    private bool HasSupportAt(float x, float y, float z, in ArenaDefinition arena)
    {
        if (arena.Heightmap.Data != null && arena.Heightmap.Data.Length > 0)
            return arena.Heightmap.Sample(x, z) > float.MinValue;
        return ArenaCollision.HasTriangles(arena)
            && _recoveryTriangleCandidates.Length > 0
            && ArenaCollision.TryFindSupport(x, y, z, 0.35f, _recoveryCapsuleHeight,
                in arena, _recoveryTriangleCandidates, out _);
    }

    private bool IsOffstage(in CharacterState state, in ArenaDefinition arena,
        float minSurface = float.MinValue)
    {
        _ = minSurface;
        if (!HasArenaBounds(arena))
            return false;
        bool outside = state.PX < arena.MinX - 0.35f || state.PX > arena.MaxX + 0.35f
            || state.PZ < arena.MinZ - 0.35f || state.PZ > arena.MaxZ + 0.35f;
        if (outside)
            return true;
        if (HasSupportAt(state.PX, state.PY, state.PZ, in arena))
            return false;
        return true;
    }

    private bool IsOffstage(in CpuObservation state, in ArenaDefinition arena,
        float minSurface = float.MinValue)
    {
        var current = new CharacterState
        {
            PX = state.PX, PY = state.PY, PZ = state.PZ, IsGrounded = state.IsGrounded,
        };
        return IsOffstage(current, in arena, minSurface);
    }

    private bool IsNearEdge(in CharacterState state, in ArenaDefinition arena)
    {
        if (!HasArenaBounds(arena))
            return false;
        if (!HasSupportAt(state.PX, state.PY, state.PZ, in arena))
            return true;
        return !HasSupportAt(state.PX + NearEdgeDistance, state.PY, state.PZ, in arena)
            || !HasSupportAt(state.PX - NearEdgeDistance, state.PY, state.PZ, in arena)
            || !HasSupportAt(state.PX, state.PY, state.PZ + NearEdgeDistance, in arena)
            || !HasSupportAt(state.PX, state.PY, state.PZ - NearEdgeDistance, in arena);
    }

    private void EnsureRecoveryTargets(in ArenaDefinition arena, in CharacterDefinition def)
    {
        if (ReferenceEquals(_recoveryHeightmap, arena.Heightmap.Data)
            && ReferenceEquals(_recoveryTriangles, arena.CollisionTriangles)
            && _recoveryCapsuleHeight == def.CapsuleHeight)
            return;
        _recoveryHeightmap = arena.Heightmap.Data;
        // CPUs of different sizes share one policy and alternate here every tick; only a new stage
        // needs a new candidate buffer.
        if (!ReferenceEquals(_recoveryTriangles, arena.CollisionTriangles))
            _recoveryTriangleCandidates = new int[arena.CollisionTriangles?.Length ?? 0];
        _recoveryTriangles = arena.CollisionTriangles;
        _recoveryCapsuleHeight = def.CapsuleHeight;
        _recoveryTargetCount = 0;
        _recoveryMinSurface = float.MinValue;
        if (!HasArenaBounds(arena) || arena.Heightmap.Data == null
            || arena.Heightmap.Width < 3 || arena.Heightmap.Height < 3)
            return;
        _recoveryMinSurface = float.MaxValue;

        int cells = arena.Heightmap.Width * arena.Heightmap.Height;
        int stride = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(cells / (float)MaxRecoveryTargets)));
        for (int z = 1; z < arena.Heightmap.Height - 1; z += stride)
        {
            for (int x = 1; x < arena.Heightmap.Width - 1; x += stride)
            {
                float px = arena.Heightmap.OriginX + x * arena.Heightmap.CellSize;
                float pz = arena.Heightmap.OriginZ + z * arena.Heightmap.CellSize;
                if (px < arena.MinX + def.CapsuleRadius
                    || px > arena.MaxX - def.CapsuleRadius
                    || pz < arena.MinZ + def.CapsuleRadius
                    || pz > arena.MaxZ - def.CapsuleRadius)
                    continue;

                float surface = arena.Heightmap.Sample(px, pz);
                if (surface <= float.MinValue)
                    continue;
                _recoveryMinSurface = MathF.Min(_recoveryMinSurface, surface);
                if (_recoveryTargetCount < _recoveryTargets.Length)
                    _recoveryTargets[_recoveryTargetCount++] =
                        new RecoveryTarget(px, surface + def.CapsuleHeight * 0.5f, pz);
            }
        }
        if (_recoveryMinSurface == float.MaxValue)
            _recoveryMinSurface = float.MinValue;
    }

    private bool HasTraversableRecoveryPath(in CharacterState self,
        in RecoveryTarget candidate, in CharacterDefinition def, in ArenaDefinition arena)
    {
        if (!ArenaCollision.HasTriangles(arena))
            return true;

        if (_recoveryTriangleCandidates.Length < arena.CollisionTriangles!.Length)
            _recoveryTriangleCandidates = new int[arena.CollisionTriangles.Length];

        if (!ArenaCollision.TryFindSupport(candidate.X, candidate.Y, candidate.Z,
                def.CapsuleRadius, def.CapsuleHeight, in arena,
                _recoveryTriangleCandidates, out var support))
            return false;

        int count = ArenaCollision.GetCandidateTrianglesForSweep(
            self.PX, self.PY, self.PZ, candidate.X, candidate.Y, candidate.Z,
            def.CapsuleRadius, def.CapsuleHeight, in arena, _recoveryTriangleCandidates);
        if (count == 0)
            return true;
        if (!ArenaCollision.SweepCapsule(
                self.PX, self.PY, self.PZ, candidate.X, candidate.Y, candidate.Z,
                def.CapsuleRadius, def.CapsuleHeight, in arena,
                _recoveryTriangleCandidates, count, out var contact))
            return true;

        // The destination support is the only allowed contact on the direct recovery path.
        // Earlier contact means the sampled heightmap target is blocked by stage geometry.
        return contact.TriangleIndex == support.TriangleIndex || contact.Time >= 0.98f;
    }


    private static float RecoveryHorizontalReach(in MoveCandidate candidate,
        in CharacterState self, in RecoveryTarget target, BotMemory memory)
    {
        if (candidate.RecoveryHorizontal <= 0f)
            return 0f;
        if (!candidate.RecoveryRequiresDelayedOpponent)
            return candidate.RecoveryHorizontal;
        if (!memory.TryGetDelayedOpponent(out var enemy))
            return 0f;
        float targetX = target.X - self.PX;
        float targetZ = target.Z - self.PZ;
        float targetDistance = MathF.Sqrt(targetX * targetX + targetZ * targetZ);
        float enemyX = enemy.PX - self.PX;
        float enemyZ = enemy.PZ - self.PZ;
        float enemyDistance = MathF.Sqrt(enemyX * enemyX + enemyZ * enemyZ);
        if (targetDistance <= 0.001f || enemyDistance <= 0.001f)
            return 0f;
        float alignment = (targetX * enemyX + targetZ * enemyZ)
            / (targetDistance * enemyDistance);
        return alignment >= 0.5f
            ? MathF.Min(candidate.RecoveryHorizontal, enemyDistance)
            : 0f;
    }

    private bool TryGetRecoveryTarget(in CharacterState self, in CharacterDefinition def,
        in ArenaDefinition arena, BotMemory memory, out RecoveryTarget target, out float distance, CookedSlotDefinition? activeMove)
    {
        EnsureRecoveryTargets(in arena, in def);
        target = default;
        distance = float.PositiveInfinity;
        float regularReach = def.Movement.AirSpeedMax
            * MathF.Min(90f, MathF.Max(30f, (self.PY - arena.KillHeight)
                / MathF.Max(1f, def.Movement.Gravity) * 60f)) / 60f;
        regularReach += self.JumpsLeft * def.Movement.AirSpeedMax
            * def.Movement.AirJumpHMultiplier * 0.75f;

        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < _recoveryTargetCount; i++)
        {
            var candidate = _recoveryTargets[i];
            float dx = candidate.X - self.PX;
            float dz = candidate.Z - self.PZ;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            float dy = candidate.Y - self.PY;
            var recovery = ChooseRecoveryMove(self, d, activeMove);
            float specialReach = recovery.HasValue
                ? RecoveryHorizontalReach(recovery.GetValueOrDefault(), self, candidate, memory)
                : 0f;
            float verticalReach = recovery?.RecoveryVertical ?? 0f;
            if (dy > def.Movement.JumpForce * def.Movement.JumpForce
                    / MathF.Max(1f, 2f * def.Movement.Gravity) + verticalReach
                && self.JumpsLeft == 0)
                continue;
            if (d > regularReach + specialReach + VictimRadiusMargin)
                continue;
            if (!HasTraversableRecoveryPath(in self, in candidate, in def, in arena))
                continue;

            float score = d + MathF.Max(0f, dy) * 0.35f;
            if (score < bestScore)
            {
                bestScore = score;
                target = candidate;
                distance = d;
            }
        }
        return distance < float.PositiveInfinity;
    }

    private MoveCandidate? ChooseRecoveryMove(in CharacterState self, float distance, CookedSlotDefinition? activeMove)
    {
        bool air = !self.IsGrounded;
        MoveCandidate? selected = null;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!candidate.Functional || !candidate.IsRecovery
                || HasCooldown(self, _profileDefinition!, candidate.Slot, activeMove)
                || IsChargePoolExhausted(self, candidate.Slot, air))
                continue;
            float score = candidate.Reach - MathF.Abs(candidate.Reach - distance) * 0.25f
                + (candidate.HasMovement ? 2f : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                selected = candidate;
            }
        }
        return selected;
    }
    private bool TryBuildRecoveryInput(in CharacterState self, CharacterDefinition def,
        in ArenaDefinition arena, BotMemory memory, out InputState input, CookedSlotDefinition? activeMove)
    {
        input = default;
        if (self.State == ActionState.LedgeHang)
        {
            memory.ClearPlan();
            if (Simulation.FindLedge(self, arena, def.CapsuleHeight * 0.5f,
                out _, out float inwardX, out float inwardZ, out _, out _))
            {
                input.MoveX = inwardX;
                input.MoveY = inwardZ;
            }
            return true;
        }
        if (!IsOffstage(self, arena, _recoveryMinSurface))
            return false;
        if (!TryGetRecoveryTarget(self, in def, in arena, memory,
                out var target, out float distance, activeMove))
        {
            float centerX = (arena.MinX + arena.MaxX) * 0.5f;
            float centerZ = (arena.MinZ + arena.MaxZ) * 0.5f;
            float centerDistance = MathF.Max(0.001f,
                MathF.Sqrt((centerX - self.PX) * (centerX - self.PX)
                    + (centerZ - self.PZ) * (centerZ - self.PZ)));
            input.MoveX = (centerX - self.PX) / centerDistance;
            input.MoveY = (centerZ - self.PZ) / centerDistance;
            return true;
        }

        float dx = target.X - self.PX;
        float dz = target.Z - self.PZ;
        float directionDistance = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dz * dz));
        input.MoveX = dx / directionDistance;
        input.MoveY = dz / directionDistance;
        input.AimYaw = AimYaw(dx, dz);
        input.AimPitch = AimPitch(target.Y - self.PY, distance);
        input.AimDistance = AimDistance(distance);
        var recovery = ChooseRecoveryMove(self, distance, activeMove);
        bool urgent = self.VY <= 0f && (self.JumpsLeft == 0
            || distance > 2f);
        if (recovery.HasValue && urgent)
        {
            var selected = recovery.GetValueOrDefault();
            memory.StartPlan(selected.Slot, selected.RequiresAim,
                input.AimYaw, input.AimPitch, input.AimDistance,
                selected.RequiresAim ? AimHoldTicks : (ushort)0,
                self.Deaths, self.IsGrounded, pressIssued: true, kind: BotPlanKind.Recovery);
            input.ActiveSlot = selected.Slot;
            input.IsAiming = selected.RequiresAim;
            return true;
        }
        if (self.JumpsLeft > 0 && self.VY <= 0f)
        {
            input.Jump = true;
            input.JumpHeld = true;
        }
        return true;
    }


    private sealed record MoveProfile(CharacterDefinition Definition, BakedAnimationData? Baked,
        MoveCandidate[] Ground, MoveCandidate[] Air);
    private readonly List<MoveProfile> _profiles = new();
    private CharacterDefinition? _profileDefinition;
    private BakedAnimationData? _profileBaked;
    private MoveCandidate[] _profile = Array.Empty<MoveCandidate>();
    private MoveCandidate[] _airProfile = Array.Empty<MoveCandidate>();

    /// <summary>Capture the target's current-form reach with the observation, subject to the same reaction delay.</summary>
    public void ObserveOpponent(BotMemory memory, in CharacterState target, CharacterDefinition definition,
        BakedAnimationData? baked = null)
    {
        var profile = FindProfile(definition.ForForm(target.Form.Active), baked);
        float reach = 0f;
        foreach (var move in target.IsGrounded ? profile.Ground : profile.Air)
            if (target.AttackSlot == 0 || move.Slot == target.AttackSlot)
                reach = MathF.Max(reach, move.Reach);
        memory.ObserveOpponent(target, reach);
    }

    private static bool Threatens(in CharacterState self, in CpuObservation target)
        => target.IsThreatening && Distance(target.PX - self.PX, target.PZ - self.PZ)
            <= target.ThreatReach + VictimRadiusMargin;

    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory, CookedSlotDefinition? activeMove = null)
        => Decide(self, target, def, rng, memory, default, baked: null, activeMove: activeMove);

    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory, in ArenaDefinition arena,
        BakedAnimationData? baked = null, CpuBehavior behavior = CpuBehavior.Attack, CookedSlotDefinition? activeMove = null)
        => DecideObserved(self, target, def, rng, memory, in arena, baked, behavior, activeMove);

    private InputState DecideObserved(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory, in ArenaDefinition arena,
        BakedAnimationData? baked, CpuBehavior behavior = CpuBehavior.Attack, CookedSlotDefinition? activeMove = null)
    {
        memory.ObserveOpponent(target);
        memory.ChooseOpponent(self);
        return Decide(self, def, rng, memory, in arena, baked, behavior, activeMove);
    }

    /// <summary>
    /// Convenience entry point for local adapters. The target is sampled before the decision,
    /// and only the delayed observation is read by the policy.
    /// </summary>
    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory, BakedAnimationData? baked, CookedSlotDefinition? activeMove = null)
        => DecideObserved(self, target, def, rng, memory, default, baked, activeMove: activeMove);

    public InputState Decide(in CharacterState self, CharacterDefinition def,
        Random rng, BotMemory memory, BakedAnimationData? baked = null, CookedSlotDefinition? activeMove = null)
        => Decide(self, def, rng, memory, default, baked, activeMove: activeMove);

    public InputState Decide(in CharacterState self, CharacterDefinition def,
        Random rng, BotMemory memory, in ArenaDefinition arena, BakedAnimationData? baked = null,
        CpuBehavior behavior = CpuBehavior.Attack, CookedSlotDefinition? activeMove = null,
        MayhemThreats? mayhemThreats = null, bool canSwapCancel = false)
    {
        if (memory.FootworkTicksLeft > 0)
            memory.FootworkTicksLeft--;
        InputState input;
        if (mayhemThreats.HasValue && mayhemThreats.Value.TryAvoid(self, def, arena, out input))
        {
            memory.ClearPlan();
            memory.EndClimb();
        }
        else
        {
            def = def.ForForm(self.Form.Active);
            input = DecideAction(self, def, rng, memory, in arena, baked, behavior, activeMove, canSwapCancel,
                out var step);
            // Recovery, ledge hang and Mayhem escape write the stick directly and may cross an edge.
            // Every other step walks only where the engine's own mover says it can.
            if (!ContinueClimb(ref input, self, def, memory))
                ApplyFootstep(ref input, step, self, def, in arena, memory);
        }
        // Startup aim correction prefers the input's target, so it turns toward the fighter the CPU chose.
        ulong opponent = memory.OpponentId;
        if (opponent is > 0 and <= byte.MaxValue)
            input.TargetEntityId = (byte)opponent;
        memory.EndTick();
        return input;
    }

    private InputState DecideAction(in CharacterState self, CharacterDefinition def,
        Random rng, BotMemory memory, in ArenaDefinition arena, BakedAnimationData? baked,
        CpuBehavior behavior, CookedSlotDefinition? activeMove, bool canSwapCancel, out Footstep step)
    {
        step = default;
        if (memory.Behavior != behavior)
        {
            memory.ClearPlan();
            memory.EndClimb();
            memory.EndFootwork();
            memory.DecisionTicksRemaining = 0;
            memory.Behavior = behavior;
        }
        if (self.HitstunTicks > 0)
            memory.EndFootwork();
        EnsureProfile(def, baked);
        EnsureRecoveryTargets(in arena, in def);

        if (self.State == ActionState.LedgeHang)
        {
            memory.EndClimb();
            return TryBuildRecoveryInput(self, def, in arena, memory, out var ledgeInput, activeMove)
                ? ledgeInput : default;
        }
        bool movementAllowed = self.HitstunTicks == 0
            && self.HitstopTicks == 0
            && self.LandingLagTicks == 0;
        bool actionable = movementAllowed
            && (self.AnimLockTicks == 0 || Simulation.IsIasaUnlocked(in self, activeMove))
            && (ActionStates.AdmitsActions(self.State)
                || Simulation.IsIasaUnlocked(in self, activeMove));
        if (behavior == CpuBehavior.Attack && movementAllowed && self.BlockStunTicks == 0 && canSwapCancel
            && memory.Difficulty != CpuDifficulty.Easy && self.AttackSlot != AbilitySlots.F
            && def.AlternateForm != null && self.GetCooldown(AbilitySlots.F) == 0
            && memory.TryGetNewHit(out var confirmed) && confirmed.Slot == self.AttackSlot)
        {
            memory.MarkHitEvaluated(confirmed);
            memory.ClearPlan();
            return new InputState { ActiveSlot = AbilitySlots.F,
                AimYaw = AimYaw(confirmed.PX - self.PX, confirmed.PZ - self.PZ), FaceToCamera = true };
        }
        if (memory.PlanPhase != BotPlanPhase.None
            && memory.PlanKind != BotPlanKind.Recovery)
        {
            if (actionable
                && TryBuildRecoveryInput(self, def, in arena, memory, out var recoveryInput, activeMove))
                return recoveryInput;
            if (!self.IsGrounded)
                memory.PlanWasAirborne = true;
            return ContinuePlan(self, def, memory, in arena, activeMove);
        }
        if (memory.PlanPhase != BotPlanPhase.None)
        {
            if (!self.IsGrounded)
                memory.PlanWasAirborne = true;
            return ContinuePlan(self, def, memory, in arena, activeMove);
        }

        var profile = BotDifficultyProfile.ForDifficulty(memory.Difficulty);
        if (memory.DecisionTicksRemaining > 0)
            memory.DecisionTicksRemaining--;

        var input = new InputState();
        if (self.State == ActionState.Shielding)
            return behavior == CpuBehavior.Shield
                || behavior == CpuBehavior.Attack
                    && memory.TryGetDelayedOpponent(out var threat) && Threatens(self, threat)
                ? new InputState { ShieldHeld = true }
                : default;
        if (!actionable)
            return input;
        if (TryBuildRecoveryInput(self, def, in arena, memory, out input, activeMove))
        {
            memory.EndClimb();
            return input;
        }
        if (behavior == CpuBehavior.Shield)
            return self.IsGrounded ? new InputState { ShieldHeld = true } : default;
        if (!memory.TryGetDelayedOpponent(out var target))
            return input;

        if (behavior == CpuBehavior.Avoid)
            return BuildAvoidInput(self, target, def, rng, memory, profile, out step);

        if (memory.TryGetNewHit(out var hit))
        {
            QueueFollowUp(self, target, hit, def, profile, rng, memory, in arena, activeMove);
            memory.MarkHitEvaluated(hit);
            if (memory.PlanPhase != BotPlanPhase.None)
                return ContinuePlan(self, def, memory, in arena, activeMove);
        }

        float dx = target.PX - self.PX;
        float dz = target.PZ - self.PZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        bool hasTarget = dist > 0.001f;
        if (!hasTarget)
            return input;

        input.AimYaw = AimYaw(dx, dz);
        input.FaceToCamera = true;
        if (def.AlternateForm != null && memory.DecisionTicksRemaining == 0
            && CanPress(self, def, AbilitySlots.F, activeMove))
        {
            var fake = FindProfile(def.ForForm(1), baked);
            var closeMoves = self.IsGrounded ? fake.Ground : fake.Air;
            float closeRange = 0f;
            foreach (var move in closeMoves)
                if (IsNormalSlot(move.Slot) && move.Functional)
                    closeRange = MathF.Max(closeRange, move.Reach + VictimRadiusMargin);
            byte desiredForm = dist <= closeRange ? (byte)1 : (byte)0;
            if (desiredForm != self.Form.Active)
            {
                input.ActiveSlot = AbilitySlots.F;
                memory.DecisionTicksRemaining = profile.DecisionIntervalTicks;
                return input;
            }
        }
        bool targetOffstage = IsOffstage(target, in arena, _recoveryMinSurface);

        // Range error is an intentional tier-specific spacing error around real reach. It is rolled once per
        // decision: re-rolled every tick, the CPU alternates approach and stop at the edge of its reach.
        bool decisionTick = memory.DecisionTicksRemaining == 0;
        if (decisionTick)
            memory.RangeScale = 1f + (((float)rng.NextDouble() * 2f) - 1f) * profile.RangeError;
        bool allowRecoveryMove = !IsNearEdge(self, in arena);
        // A shield is answered with a grab, so close to grab reach instead of swinging into it.
        float engageReach = target.State == ActionState.Shielding && target.IsGrounded && def.CaptureGeometry != null
            ? def.CaptureGeometry.Reach + def.CapsuleRadius
            : MaxConnectReach(self, memory.RangeScale, allowRecoveryMove, in arena, activeMove);
        if (dist <= engageReach)
        {
            memory.Engaged = true;
            memory.FiredOnApproach = false;
        }
        else if (dist > engageReach + def.CapsuleRadius)
            memory.Engaged = false;
        bool inRange = memory.Engaged
            || ReadyDashStrikeTip(in self, in target, allowRecoveryMove, in arena, activeMove)
            || ReadyFlash(in self, in target, allowRecoveryMove, in arena, activeMove)
            || ReadyFinisherGrab(in self, in target, allowRecoveryMove, in arena, activeMove);

        if (decisionTick)
        {
            memory.DecisionTicksRemaining = profile.DecisionIntervalTicks;
            if (DecideOnce(ref input, self, target, def, rng, memory, profile, in arena, activeMove,
                    inRange, targetOffstage, allowRecoveryMove))
                return input;
        }
        step = ResolveFootwork(self, target, memory, engageReach, targetOffstage);
        return input;
    }

    /// <summary>
    /// One decision: a shield, jump or attack press (true), or, when every roll fails and no footwork is held,
    /// a Retreat or Circle window that the following ticks walk.
    /// </summary>
    private bool DecideOnce(ref InputState input, in CharacterState self, in CpuObservation target,
        CharacterDefinition def, Random rng, BotMemory memory, in BotDifficultyProfile profile,
        in ArenaDefinition arena, CookedSlotDefinition? activeMove, bool inRange, bool targetOffstage,
        bool allowRecoveryMove)
    {
        float dx = target.PX - self.PX, dz = target.PZ - self.PZ;
        float dist = Distance(dx, dz), dy = target.PY - self.PY;
        bool targetThreatening = Threatens(self, target);
        // A fighter with every attack cooling down still needs to guard nearby
        // pressure; offensive reach must not disable the defensive decision.
        if (!inRange && self.IsGrounded && targetThreatening
            && dist <= 2f * def.CapsuleRadius
            && rng.NextDouble() < profile.DefenseChance)
            return RaiseShield(ref input, memory);
        if (!inRange)
        {
            // Fire one reaching special on the way in, which is the grounded approach, then keep closing to the
            // engage reach: a special without a cooldown would otherwise chain at range. An airborne CPU lands
            // first, and there is no way in to an offstage target.
            if (targetOffstage || !self.IsGrounded || memory.FiredOnApproach)
                return false;
            var special = ChooseSlot(self, target, dist, memory.RangeScale, rng, profile.SpecialChance,
                allowRecoveryMove, in arena, activeMove, outsideReach: true);
            if (!special.HasValue || rng.NextDouble() >= profile.AttackChance)
                return false;
            memory.FiredOnApproach = true;
            return StartAttack(ref input, special.GetValueOrDefault(), self, dist, dy, memory);
        }

        bool targetIsHigherOrAirborne = !target.IsGrounded || dy > JumpGap;
        float jumpChance = targetIsHigherOrAirborne ? profile.JumpChance : profile.JumpChance * 0.18f;
        if (self.IsGrounded && !targetOffstage && rng.NextDouble() < jumpChance)
        {
            input.Jump = true;
            input.JumpHeld = true;
            return true;
        }

        MoveCandidate? candidate = null;
        if (self.IsGrounded && self.BlockStunTicks == 0 && target.IsGrounded
            && target.State == ActionState.Shielding && InCaptureVolume(self, target, def)
            && !ReadyFinisherGrab(in self, in target, allowRecoveryMove, in arena, activeMove)
            && !ReadyCommandGrab(self, target, dist, allowRecoveryMove, in arena, activeMove).HasValue)
        {
            // Grab beats shield; with the roll failed, swinging into the shield is no better.
            if (rng.NextDouble() < profile.AttackChance)
                return StartGrab(ref input, self, profile, memory);
        }
        else
        {
            candidate = ChooseSlot(self, target, dist, memory.RangeScale, rng,
                profile.SpecialChance, allowRecoveryMove, in arena, activeMove);
            bool punish = candidate.HasValue
                && targetThreatening
                && rng.NextDouble() < profile.PunishChance;
            if (candidate.HasValue && (punish || rng.NextDouble() < profile.AttackChance))
                return StartAttack(ref input, candidate.GetValueOrDefault(), self, dist, dy, memory);
        }

        bool held = memory.Footwork != Footwork.Engage;
        if (targetOffstage && self.IsGrounded)
        {
            // Edgeguard from a safe stage-side position; never chase an unreachable
            // offstage target with a jump or forward movement.
            if (!held)
                memory.HoldFootwork(Footwork.Retreat, FootworkWindow(profile, rng));
            return false;
        }

        if (targetThreatening && rng.NextDouble() < profile.DefenseChance)
        {
            if (self.IsGrounded)
                return RaiseShield(ref input, memory);
            if (!held)
                memory.HoldFootwork(Footwork.Retreat, FootworkWindow(profile, rng));
            return false;
        }
        if (held)
            return false;

        bool pointBlankWithoutAttack = dist <= 0.35f && !candidate.HasValue;
        if (targetThreatening || pointBlankWithoutAttack || rng.NextDouble() < profile.RetreatChance)
            memory.HoldFootwork(Footwork.Retreat, FootworkWindow(profile, rng));
        else
        {
            if (memory.Side == 0)
                memory.Side = rng.Next(2) == 0 ? (sbyte)-1 : (sbyte)1;
            memory.HoldFootwork(Footwork.Circle, FootworkWindow(profile, rng));
        }
        return false;
    }

    private static bool StartAttack(ref InputState input, in MoveCandidate selected, in CharacterState self,
        float dist, float dy, BotMemory memory)
    {
        input.ActiveSlot = selected.Slot;
        input.AimPitch = AimPitch(dy, dist);
        input.AimDistance = AimDistance(dist);
        memory.StartPlan(selected.Slot, selected.RequiresAim,
            input.AimYaw, input.AimPitch, input.AimDistance,
            selected.RequiresAim ? AimHoldTicks : (ushort)0,
            self.Deaths, self.IsGrounded);
        if (selected.RequiresAim)
            input.IsAiming = true;
        return true;
    }

    private static bool StartGrab(ref InputState input, in CharacterState self, in BotDifficultyProfile profile,
        BotMemory memory)
    {
        memory.StartPlan(0, false, input.AimYaw, 0, 0, (ushort)profile.ReactionDelayTicks, self.Deaths,
            self.IsGrounded, kind: BotPlanKind.Grab);
        input.GrabPressed = true;
        return true;
    }

    /// <summary>
    /// Facing the target, the cooked capture volume would reach it: within its forward reach and overlapping its
    /// height, with this fighter's own capsule standing in for the target's.
    /// </summary>
    private static bool InCaptureVolume(in CharacterState self, in CpuObservation target, CharacterDefinition def)
    {
        var capture = def.CaptureGeometry;
        if (capture == null)
            return false;
        float bottom = self.PY + capture.OffsetY - capture.Height * 0.5f;
        float half = def.CapsuleHeight * 0.5f;
        return Distance(target.PX - self.PX, target.PZ - self.PZ) <= capture.Reach + def.CapsuleRadius
            && target.PY - half <= bottom + capture.Height + def.CapsuleRadius
            && target.PY + half >= bottom - def.CapsuleRadius;
    }

    /// <summary>
    /// The grab after its press: neutral through the attempt, hold for the reaction delay, then one throw press
    /// forward. Anything else (a whiff, an ended interaction, hitstun, death) clears it without a replayed press.
    /// </summary>
    private static InputState ContinueGrab(in CharacterState self, BotMemory memory)
    {
        if (self.Deaths == memory.PlanDeaths && self.HitstunTicks == 0)
        {
            if (self.State == ActionState.GrabAttempt)
                return default;
            if (self.State == ActionState.Grabbing && memory.PlanTicks < memory.PlanHoldTicks)
            {
                memory.PlanTicks++;
                return default;
            }
            if (self.State == ActionState.Grabbing)
            {
                memory.ClearPlan();
                float yaw = self.CapturedYaw * (MathF.PI / 18000f);
                return new InputState { ActiveSlot = AbilitySlots.Slot1, MoveX = MathF.Sin(yaw), MoveY = MathF.Cos(yaw) };
            }
        }
        memory.ClearPlan();
        return default;
    }

    private static bool RaiseShield(ref InputState input, BotMemory memory)
    {
        memory.EndFootwork();
        input.ShieldHeld = input.ShieldPressed = true;
        return true;
    }

    /// <summary>Retreat and Circle outlast Hard's decision cadence so they read as intent, not a twitch.</summary>
    private static int FootworkWindow(in BotDifficultyProfile profile, Random rng)
        => Math.Max(profile.DecisionIntervalTicks, 18) + rng.Next(13);

    /// <summary>
    /// This tick's step for the held footwork. Engage approaches to the engage reach and stands inside the band,
    /// and stands while the opponent is offstage; Retreat ends at twice the reach; Circle ends when the band breaks.
    /// </summary>
    private static Footstep ResolveFootwork(in CharacterState self, in CpuObservation target,
        BotMemory memory, float engageReach, bool targetOffstage)
    {
        float dx = target.PX - self.PX, dz = target.PZ - self.PZ;
        float dist = Distance(dx, dz);
        if (memory.Footwork == Footwork.Retreat && (memory.FootworkTicksLeft == 0 || dist >= 2f * engageReach)
            || memory.Footwork == Footwork.Circle && (memory.FootworkTicksLeft == 0 || !memory.Engaged))
            memory.EndFootwork();
        return memory.Footwork switch
        {
            Footwork.Retreat => new Footstep(-dx / dist, -dz / dist),
            Footwork.Circle => new Footstep(-dz / dist * memory.Side, dx / dist * memory.Side),
            _ => targetOffstage || memory.Engaged ? default : new Footstep(dx / dist, dz / dist, dist, target.PY - self.PY),
        };
    }

    private static InputState BuildAvoidInput(in CharacterState self, in CpuObservation target,
        CharacterDefinition def, Random rng, BotMemory memory, in BotDifficultyProfile profile, out Footstep step)
    {
        step = default;
        float dx = self.PX - target.PX;
        float dz = self.PZ - target.PZ;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        float spacing = MathF.Max(5f, def.CapsuleRadius * 6f);
        var input = new InputState();
        if (distance >= spacing)
            return input;
        if (distance <= 0.001f)
        {
            dx = MathF.Sin(self.FacingYaw);
            dz = MathF.Cos(self.FacingYaw);
            distance = 1f;
        }
        step = new Footstep(dx / distance, dz / distance);
        if (!self.IsGrounded && Threatens(self, target) && self.AirDodgesLeft > 0
            && memory.DecisionTicksRemaining == 0)
        {
            memory.DecisionTicksRemaining = profile.DecisionIntervalTicks;
            input.ShieldPressed = rng.NextDouble() < profile.DefenseChance;
        }
        return input;
    }

    /// <summary>What the decision asks the feet to do this tick, before stage safety. Default stands still.</summary>
    private readonly struct Footstep
    {
        /// <summary>Unit world-space heading.</summary>
        public readonly float DirX, DirZ;
        /// <summary>Horizontal metres to the goal; infinite for open-ended footwork.</summary>
        public readonly float GoalDistance;
        /// <summary>The goal's height above this fighter, capsule centre to capsule centre.</summary>
        public readonly float GoalRise;

        public Footstep(float dirX, float dirZ, float goalDistance = float.PositiveInfinity, float goalRise = 0f)
        {
            DirX = dirX;
            DirZ = dirZ;
            GoalDistance = goalDistance;
            GoalRise = goalRise;
        }

        public bool Moves => DirX != 0f || DirZ != 0f;
        public bool HasGoal => GoalDistance < float.PositiveInfinity;
    }

    private static void ApplyFootstep(ref InputState input, in Footstep step, in CharacterState self,
        CharacterDefinition def, in ArenaDefinition arena, BotMemory memory)
    {
        if (!step.Moves)
            return;
        if (self.IsGrounded && self.State != ActionState.LedgeHang && InsideBounds(self, def, in arena))
        {
            Steer(ref input, step, self, def, in arena, memory);
            return;
        }
        input.MoveX = step.DirX;
        input.MoveY = step.DirZ;
    }

    private static bool InsideBounds(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena)
        => !HasArenaBounds(arena)
            || self.PX >= arena.MinX + def.CapsuleRadius && self.PX <= arena.MaxX - def.CapsuleRadius
                && self.PZ >= arena.MinZ + def.CapsuleRadius && self.PZ <= arena.MaxZ - def.CapsuleRadius;

    /// <summary>
    /// Walk the heading the engine's mover accepts: the asked heading, else the wall tangent, then 45 and 90
    /// degrees off it, preferred side first. A raised goal behind a jumpable ledge starts a climb.
    /// </summary>
    private static void Steer(ref InputState input, in Footstep step, in CharacterState self,
        CharacterDefinition def, in ArenaDefinition arena, BotMemory memory)
    {
        float runStep = def.Movement.RunSpeed * Simulation.TickDt;
        float length = MathF.Max(0.75f, def.Movement.RunSpeed * 0.3f);
        if (step.HasGoal)
            length = MathF.Max(runStep, MathF.Min(length, step.GoalDistance + def.CapsuleRadius));
        float rise = JumpRise(def);
        int side = memory.Side == 0 ? 1 : memory.Side;

        var verdict = StageWalk.Probe(self, def, in arena, step.DirX, step.DirZ, length, rise);
        if (TryWalk(ref input, verdict, step.DirX, step.DirZ, step, self, def, memory))
            return;
        if (verdict.End is WalkEnd.Wall or WalkEnd.Ledge)
        {
            float tangentX = -verdict.NormalZ, tangentZ = verdict.NormalX;
            float along = tangentX * step.DirX + tangentZ * step.DirZ;
            float left = tangentX * -step.DirZ + tangentZ * step.DirX;
            if (along < -0.001f || MathF.Abs(along) <= 0.001f && left * side < 0f)
            {
                tangentX = -tangentX;
                tangentZ = -tangentZ;
                left = -left;
            }
            if (TryDetour(ref input, tangentX, tangentZ, left >= 0f ? 1 : -1, step, self, def, in arena,
                    length, rise, memory))
                return;
        }
        for (int i = 0; i < 4; i++)
        {
            int turn = (i & 1) == 0 ? side : -side;
            float cos = i < 2 ? HalfSqrt2 : 0f, sin = turn * (i < 2 ? HalfSqrt2 : 1f);
            if (TryDetour(ref input, step.DirX * cos - step.DirZ * sin, step.DirZ * cos + step.DirX * sin, turn,
                    step, self, def, in arena, length, rise, memory))
                return;
        }
        input.MoveX = input.MoveY = 0f;
        memory.EndFootwork();
    }

    private static bool TryDetour(ref InputState input, float x, float z, int turn, in Footstep step,
        in CharacterState self, CharacterDefinition def, in ArenaDefinition arena, float length, float rise,
        BotMemory memory)
    {
        var verdict = StageWalk.Probe(self, def, in arena, x, z, length, rise);
        if (!TryWalk(ref input, verdict, x, z, step, self, def, memory))
            return false;
        if (turn != (memory.Side == 0 ? 1 : memory.Side))
            memory.Side = (sbyte)turn;
        return true;
    }

    private static bool TryWalk(ref InputState input, in WalkVerdict verdict, float x, float z, in Footstep step,
        in CharacterState self, CharacterDefinition def, BotMemory memory)
    {
        if (!Accept(verdict, step, def, out bool climb))
            return false;
        input.MoveX = x;
        input.MoveY = z;
        if (climb && self.JumpsLeft > 0)
        {
            input.Jump = input.JumpHeld = true;
            memory.StartClimb(x, z, self.Deaths);
        }
        return true;
    }

    /// <summary>
    /// The walk policy: clear ground goes; a drop goes when it can be jumped back up or the goal is down
    /// there; a ledge is climbed toward a raised goal; a wall is walked up to; a hazard never.
    /// </summary>
    private static bool Accept(in WalkVerdict verdict, in Footstep step, CharacterDefinition def, out bool climb)
    {
        climb = false;
        float runStep = def.Movement.RunSpeed * Simulation.TickDt;
        switch (verdict.End)
        {
            case WalkEnd.Clear:
                return true;
            case WalkEnd.Drop:
                return verdict.Height <= JumpRise(def)
                    || step.HasGoal && step.GoalRise <= JumpGap - verdict.Height;
            case WalkEnd.Ledge when step.HasGoal && step.GoalRise > JumpGap
                && verdict.Height <= JumpRise(def) * 0.9f:
                climb = verdict.Covered <= def.CapsuleRadius + 2f * runStep;
                return true;
            case WalkEnd.Ledge:
            case WalkEnd.Wall:
                return verdict.Covered >= 2f * runStep;
            default:
                return false;
        }
    }

    /// <summary>
    /// Hold a climb's jump long enough for a full jump (a one-tick press is a short hop) with the stick toward
    /// the ledge, until the CPU lands again. An action, a plan, hitstun or death ends it.
    /// </summary>
    private static bool ContinueClimb(ref InputState input, in CharacterState self, CharacterDefinition def,
        BotMemory memory)
    {
        if (!memory.Climbing)
            return false;
        if (!self.IsGrounded)
            memory.ClimbLeftGround = true;
        if (memory.PlanPhase != BotPlanPhase.None || input.ActiveSlot != 0 || input.ShieldHeld || input.GrabPressed
            || self.HitstunTicks > 0 || self.Deaths != memory.ClimbDeaths
            || memory.ClimbLeftGround && self.IsGrounded || memory.ClimbTicks > ClimbTicksMax)
        {
            memory.EndClimb();
            return false;
        }
        input.MoveX = memory.ClimbX;
        input.MoveY = memory.ClimbZ;
        input.JumpHeld = memory.ClimbTicks < Simulation.ShortHopWindowTicks + def.Movement.JumpSquatTicks + 1;
        memory.ClimbTicks++;
        return true;
    }

    private const int ClimbTicksMax = 45;
    private const float HalfSqrt2 = 0.70710677f;

    /// <summary>Feet rise of one full ground jump, as recovery estimates it.</summary>
    private static float JumpRise(CharacterDefinition def)
        => def.Movement.JumpForce * def.Movement.JumpForce / MathF.Max(1f, 2f * def.Movement.Gravity);

    private InputState ContinuePlan(in CharacterState self, CharacterDefinition def,
        BotMemory memory, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        if (memory.PlanKind == BotPlanKind.Grab)
            return ContinueGrab(self, memory);
        if (PlanInvalidated(self, memory))
        {
            memory.ClearPlan();
            return default;
        }

        if (!memory.PlanPressIssued)
        {
            if (!CanPress(self, def, memory.PlanSlot, activeMove))
                return default;

            var candidates = self.IsGrounded ? _profile : _airProfile;
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i].Slot == memory.PlanSlot
                    && !HasSafeAttackTravel(self, candidates[i], in arena))
                {
                    memory.ClearPlan();
                    return default;
                }

            memory.PlanPressIssued = true;
            return PlanPressInput(memory);
        }

        if (memory.PlanPhase == BotPlanPhase.AimHold
            && self.State == ActionState.Aiming
            && self.AttackSlot == memory.PlanSlot)
        {
            bool aiming = memory.PlanTicks < memory.PlanHoldTicks;
            var planned = PlanInput(memory, aiming);
            if (aiming)
                memory.PlanTicks++;
            else
                memory.ClearPlan();
            return planned;
        }

        // A non-aim press was accepted, or an aim plan reached its release phase.
        if ((self.State is ActionState.Attacking or ActionState.Aiming)
            && self.AttackSlot == memory.PlanSlot)
        {
            memory.ClearPlan();
            return default;
        }

        // The simulation rejected or interrupted the precommitment. Do not replay it
        // from a fresh current-state read.
        memory.ClearPlan();
        return default;
    }

    private static bool CanPress(in CharacterState self, CharacterDefinition def, byte slot, CookedSlotDefinition? activeMove)
        => self.HitstunTicks == 0
            && self.HitstopTicks == 0
            && self.LandingLagTicks == 0
            && (self.AnimLockTicks == 0 || Simulation.IsIasaUnlocked(in self, activeMove))
            && (ActionStates.AdmitsActions(self.State)
                || Simulation.IsIasaUnlocked(in self, activeMove))
            && !HasCooldown(self, def, slot, activeMove)
            && (def.GetCookedSlotAbility(slot, !self.IsGrounded) != null);

    /// <summary>On cooldown, or waiting for its returning projectile to come back.</summary>
    private static bool HasCooldown(in CharacterState self, CharacterDefinition def, byte slot, CookedSlotDefinition? activeMove = null)
        => self.GetCooldown(slot) > 0
            || slot != 0 && self.ReturningProjectileSlot == slot
            || self.State == ActionState.Attacking && self.AttackSlot == slot
                && (activeMove?.CooldownTicks ?? 0) > 0;

    private static InputState PlanPressInput(BotMemory memory)
        => new()
        {
            ActiveSlot = memory.PlanSlot,
            IsAiming = memory.PlanPhase == BotPlanPhase.AimHold,
            AimYaw = memory.PlanAimYaw,
            AimPitch = memory.PlanAimPitch,
            AimDistance = memory.PlanAimDistance,
        };

    private void QueueFollowUp(in CharacterState self, in CpuObservation target,
        in CpuHitObservation hit, CharacterDefinition def, BotDifficultyProfile profile,
        Random rng, BotMemory memory, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        float rangeScale = memory.RangeScale;
        bool allowRecoveryMove = !IsNearEdge(self, in arena);
        var trueCombo = ChooseTrueCombo(self, hit, memory.RemainingHitstun(hit),
            rangeScale, profile.SpecialChance, rng, allowRecoveryMove, in arena, activeMove);
        if (trueCombo.HasValue && rng.NextDouble() < profile.ComboChance)
        {
            var selected = trueCombo.GetValueOrDefault();
            short yaw = AimYaw(hit.PX - self.PX, hit.PZ - self.PZ);
            memory.QueueFollowUp(selected.Slot, BotPlanKind.TrueCombo, yaw,
                AimPitch(hit.PY - self.PY, Distance(hit.PX - self.PX, hit.PZ - self.PZ)),
                AimDistance(Distance(hit.PX - self.PX, hit.PZ - self.PZ)),
                selected.RequiresAim ? AimHoldTicks : (ushort)0, self.Deaths, self.IsGrounded);
            return;
        }

        float dx = target.PX - self.PX;
        float dz = target.PZ - self.PZ;
        float dist = Distance(dx, dz);
        var pressure = ChooseSlot(self, target, dist, rangeScale, rng,
            profile.SpecialChance, allowRecoveryMove, in arena, activeMove);
        if (!pressure.HasValue || (!Threatens(self, target) && memory.RemainingHitstun(hit) == 0))
            return;
        if (!Threatens(self, target) && rng.NextDouble() >= profile.AttackChance)
            return;

        var pressureMove = pressure.GetValueOrDefault();
        memory.QueueFollowUp(pressureMove.Slot, BotPlanKind.PressureString,
            AimYaw(dx, dz), AimPitch(target.PY - self.PY, dist), AimDistance(dist),
            pressureMove.RequiresAim ? AimHoldTicks : (ushort)0, self.Deaths, self.IsGrounded);
    }

    private MoveCandidate? ChooseTrueCombo(in CharacterState self, in CpuHitObservation hit,
        int remainingHitstun, float rangeScale, float specialChance, Random rng,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        if (remainingHitstun <= 0)
            return null;

        bool air = !self.IsGrounded;
        Span<MoveCandidate> viable = stackalloc MoveCandidate[Slots.Length];
        int count = 0;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove)
                || !candidate.HasDamage)
                continue;

            int timing = candidate.StartupTicks;
            float predictedX = hit.PX + hit.VX * timing * Simulation.TickDt;
            float predictedZ = hit.PZ + hit.VZ * timing * Simulation.TickDt;
            float dist = Distance(predictedX - self.PX, predictedZ - self.PZ);
            if (candidate.TravelSpeed > 0f)
            {
                float flightDistance = MathF.Max(0f, dist - candidate.TravelOffset);
                int travel = (int)MathF.Ceiling(flightDistance / candidate.TravelSpeed
                    * (1f / Simulation.TickDt));
                timing += Math.Min(travel, candidate.TravelTicks);
                predictedX = hit.PX + hit.VX * timing * Simulation.TickDt;
                predictedZ = hit.PZ + hit.VZ * timing * Simulation.TickDt;
                dist = Distance(predictedX - self.PX, predictedZ - self.PZ);
            }
            else
            {
                timing += candidate.TravelTicks;
                predictedX = hit.PX + hit.VX * timing * Simulation.TickDt;
                predictedZ = hit.PZ + hit.VZ * timing * Simulation.TickDt;
                dist = Distance(predictedX - self.PX, predictedZ - self.PZ);
            }
            if (timing <= remainingHitstun
                && dist <= (candidate.Reach + VictimRadiusMargin) * rangeScale)
                viable[count++] = candidate;
        }

        if (count == 0)
            return null;
        count = PickKind(viable, count, specialChance, rng);


        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
            bestScore = MathF.Max(bestScore, Score(viable[i], viable[i].Reach, false));
        Span<MoveCandidate> best = stackalloc MoveCandidate[Slots.Length];
        int bestCount = 0;
        for (int i = 0; i < count; i++)
            if (Score(viable[i], viable[i].Reach, false) >= bestScore - 2f)
                best[bestCount++] = viable[i];
        return best[rng.Next(bestCount)];
    }



    private static float Distance(float x, float z)
        => MathF.Sqrt(x * x + z * z);

    private static bool IsNormalSlot(byte slot)
        => slot is AbilitySlots.Slot1 or AbilitySlots.Slot3 or AbilitySlots.Slot4;
    private static bool PlanInvalidated(in CharacterState self, BotMemory memory)
        => self.Deaths != memory.PlanDeaths
            || self.HitstunTicks > 0
            || self.LandingLagTicks > 0
            || (memory.PlanWasAirborne && self.IsGrounded)
            || (memory.PlanPressIssued
                && (self.State is ActionState.Attacking or ActionState.Aiming)
                && self.AttackSlot != memory.PlanSlot);

    private static InputState PlanInput(BotMemory memory, bool aiming)
        => new()
        {
            IsAiming = aiming,
            AimYaw = memory.PlanAimYaw,
            AimPitch = memory.PlanAimPitch,
            AimDistance = memory.PlanAimDistance,
        };

    private MoveCandidate Move(byte slot, bool air) => (air ? _airProfile : _profile)[Array.IndexOf(Slots, slot)];

    private MoveProfile FindProfile(CharacterDefinition def, BakedAnimationData? baked)
    {
        foreach (var cached in _profiles)
            if (ReferenceEquals(cached.Definition, def) && ReferenceEquals(cached.Baked, baked))
                return cached;
        var ground = new MoveCandidate[Slots.Length];
        var air = new MoveCandidate[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
        {
            EvaluateMove(def, Slots[i], false, baked, out ground[i]);
            EvaluateMove(def, Slots[i], true, baked, out air[i]);
        }
        var result = new MoveProfile(def, baked, ground, air);
        _profiles.Add(result);
        if (def.AlternateForm != null) FindProfile(def.ForForm((byte)(def.Form ^ 1)), baked);
        return result;
    }

    private void EnsureProfile(CharacterDefinition def, BakedAnimationData? baked)
    {
        if (ReferenceEquals(_profileDefinition, def) && ReferenceEquals(_profileBaked, baked)) return;
        var profile = FindProfile(def, baked);
        _profileDefinition = def;
        _profileBaked = baked;
        _profile = profile.Ground;
        _airProfile = profile.Air;
    }
    private bool HasSafeAttackTravel(in CharacterState self, in MoveCandidate candidate,
        in ArenaDefinition arena)
    {
        if (!HasArenaBounds(arena))
            return true;

        // Lunges capture facing before this tick's facing input. Other aerial moves
        // inherit velocity, including a previous lunge's momentum during their lock.
        float dx, dz;
        if (candidate.ForwardTravel > 0f)
        {
            dx = MathF.Sin(self.FacingYaw) * candidate.ForwardTravel;
            dz = MathF.Cos(self.FacingYaw) * candidate.ForwardTravel;
        }
        else if (!self.IsGrounded)
        {
            dx = self.VX * candidate.CommitmentTicks * Simulation.TickDt;
            dz = self.VZ * candidate.CommitmentTicks * Simulation.TickDt;
        }
        else
            return true;

        float travel = Distance(dx, dz);
        if (travel <= 0f)
            return true;
        float radius = _profileDefinition!.CapsuleRadius;
        int steps = Math.Max(1, (int)MathF.Ceiling(travel / 0.5f));
        for (int step = 1; step <= steps; step++)
        {
            float fraction = step / (float)steps;
            float x = self.PX + dx * fraction;
            float z = self.PZ + dz * fraction;
            if (x < arena.MinX + radius || x > arena.MaxX - radius
                || z < arena.MinZ + radius || z > arena.MaxZ - radius
                || !HasSupportAt(x, self.PY, z, in arena))
                return false;
        }
        return true;
    }


    private bool IsCandidateAvailable(in CharacterState self, in MoveCandidate candidate,
        bool air, bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
        => candidate.Functional
            && !(_profileDefinition!.AlternateForm != null && candidate.Slot == AbilitySlots.F)
            && (allowRecoveryMove || !candidate.IsRecovery)
            && !HasCooldown(self, _profileDefinition!, candidate.Slot, activeMove)
            && !IsChargePoolExhausted(self, candidate.Slot, air)
            && HasSafeAttackTravel(self, candidate, in arena);
    private float MaxConnectReach(in CharacterState self, float rangeScale,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        bool air = !self.IsGrounded;
        float normalMax = 0f;
        float specialMax = 0f;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove))
                continue;
            float reach = (candidate.Reach + VictimRadiusMargin) * rangeScale;
            if (IsNormalSlot(candidate.Slot))
                normalMax = MathF.Max(normalMax, reach);
            else
                specialMax = MathF.Max(specialMax, reach);
        }
        return _profileDefinition!.AlternateForm != null ? MathF.Max(normalMax, specialMax)
            : normalMax > 0f ? normalMax : specialMax;
    }
    /// <summary>
    /// Kit priority responses first, then the ordinary pool: viable normals or viable specials, one roll at
    /// <paramref name="specialChance"/> when both are viable. Outside normal reach only damaging specials count.
    /// </summary>
    private MoveCandidate? ChooseSlot(in CharacterState self, in CpuObservation target,
        float dist, float rangeScale, Random rng, float specialChance,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove,
        bool outsideReach = false)
    {
        bool air = !self.IsGrounded;
        if (ReadyDashStrikeTip(in self, in target, allowRecoveryMove, in arena, activeMove))
            return Move(AbilitySlots.R, air);
        if (ReadyFlash(in self, in target, allowRecoveryMove, in arena, activeMove)
            || ReadyFinisherGrab(in self, in target, allowRecoveryMove, in arena, activeMove))
            return Move(AbilitySlots.F, air);
        if (target.State == ActionState.Shielding
            && ReadyCommandGrab(self, target, dist, allowRecoveryMove, in arena, activeMove) is { } commandGrab)
            return commandGrab;
        Span<MoveCandidate> viable = stackalloc MoveCandidate[Slots.Length];
        int count = 0;
        int damagingCount = 0;
        float bestDamageReach = 0f;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove)
                || outsideReach && (IsNormalSlot(candidate.Slot) || !candidate.HasDamage))
                continue;

            bool inReach = candidate.Reach > 0f
                && (candidate.Reach + VictimRadiusMargin) * rangeScale >= dist * SlotReachTolerance;
            if (inReach && candidate.HasDamage)
            {
                damagingCount++;
                bestDamageReach = MathF.Max(bestDamageReach, candidate.Reach);
            }
        }

        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove)
                || outsideReach && (IsNormalSlot(candidate.Slot) || !candidate.HasDamage))
                continue;

            bool inReach = candidate.Reach > 0f
                && (candidate.Reach + VictimRadiusMargin) * rangeScale >= dist * SlotReachTolerance;
            bool usefulMovement = !candidate.HasDamage
                && candidate.HasMovement
                && (dist > bestDamageReach + VictimRadiusMargin || !target.IsGrounded && candidate.IsRecovery);
            if (!inReach || damagingCount > 0 && !candidate.HasDamage)
            {
                if (!usefulMovement) continue;
            }

            viable[count++] = candidate;
        }

        if (count == 0)
            return null;
        count = PickKind(viable, count, specialChance, rng);


        // Keep all similarly good options in the seeded tie pool. There is no per-slot memory,
        // so a clearly strong response remains eligible to repeat.
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
            bestScore = MathF.Max(bestScore, Score(viable[i], dist, Threatens(self, target)));

        Span<MoveCandidate> best = stackalloc MoveCandidate[Slots.Length];
        int bestCount = 0;
        for (int i = 0; i < count; i++)
            if (Score(viable[i], dist, Threatens(self, target)) >= bestScore - 2f)
                best[bestCount++] = viable[i];
        return best[rng.Next(bestCount)];
    }


    /// <summary>
    /// With both normals and specials viable, one roll at the special chance keeps the specials, otherwise the
    /// normals; a lone kind is kept as it is. Returns the kept count, compacted to the front.
    /// </summary>
    private static int PickKind(Span<MoveCandidate> viable, int count, float specialChance, Random rng)
    {
        int normalCount = 0;
        for (int i = 0; i < count; i++)
            if (IsNormalSlot(viable[i].Slot))
                normalCount++;
        if (normalCount == 0 || normalCount == count)
            return count;
        bool specials = rng.NextDouble() < specialChance;
        int write = 0;
        for (int i = 0; i < count; i++)
            if (IsNormalSlot(viable[i].Slot) != specials)
                viable[write++] = viable[i];
        return write;
    }

    /// <summary>A command grab special without a hold stage that is available and reaches the target.</summary>
    private MoveCandidate? ReadyCommandGrab(in CharacterState self, in CpuObservation target, float dist,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        bool air = !self.IsGrounded;
        foreach (var grab in air ? _airProfile : _profile)
            if (Capability<CookedCommandGrabCapabilityParameters>(_profileDefinition!.GetCookedSlotAbility(grab.Slot, air)) is { HasHoldStage: false }
                && dist <= grab.Reach + VictimRadiusMargin
                && IsCandidateAvailable(self, grab, air, allowRecoveryMove, in arena, activeMove))
                return grab;
        return null;
    }

    /// <summary>The first capability of this type that a slot's timeline starts. Allocation-free: it runs every tick.</summary>
    private static T? Capability<T>(CookedSlotDefinition? slot) where T : CookedCapabilityParameters
    {
        if (slot == null)
            return null;
        var stages = slot.Timeline.Stages;
        for (int stage = 0; stage < stages.Count; stage++)
        {
            var operations = stages[stage].Operations;
            for (int i = 0; i < operations.Count; i++)
                if (operations[i] is CookedStartCapabilityOperation { Parameters: T parameters })
                    return parameters;
        }
        return null;
    }

    /// <summary>F is worth its long cooldown when the target will still be inside the lock sphere when the stance ends.</summary>
    private bool ReadyFlash(in CharacterState self, in CpuObservation target,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        bool air = !self.IsGrounded;
        var flash = Capability<CookedFlashCutCapabilityParameters>(_profileDefinition!.GetCookedSlotAbility(AbilitySlots.F, air));
        if (flash == null) return false;
        float dx = target.PX - self.PX, dy = target.PY - self.PY, dz = target.PZ - self.PZ;
        float drift = MathF.Sqrt(target.VX * target.VX + target.VZ * target.VZ) * flash.StanceTicks * Simulation.TickDt;
        var candidate = Move(AbilitySlots.F, air);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz) + drift <= flash.LockRange
            && IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove);
    }

    /// <summary>
    /// An F command grab whose hold stage ends in the timeline's finisher (Title Match) is worth its long cooldown where a
    /// shield cannot stop it, or when that finisher can KO: the target stands where the lunge's capture will reach it,
    /// shielding or at <see cref="FinisherKillPercent"/> or more.
    /// </summary>
    private bool ReadyFinisherGrab(in CharacterState self, in CpuObservation target,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        bool air = !self.IsGrounded;
        if (target.State != ActionState.Shielding && target.DamagePercent < FinisherKillPercent) return false;
        if (Capability<CookedCommandGrabCapabilityParameters>(_profileDefinition!.GetCookedSlotAbility(AbilitySlots.F, air))
            is not { HasHoldStage: true } grab) return false;
        float dx = target.PX - self.PX, dy = target.PY - self.PY, dz = target.PZ - self.PZ;
        float reach = grab.LungeSpeed * grab.LungeTicks / 60f + grab.Capture.OffsetZ + grab.Capture.Radius;
        var candidate = Move(AbilitySlots.F, air);
        return MathF.Sqrt(dx * dx + dz * dz) <= reach && MathF.Abs(dy) <= grab.Capture.Radius + 0.5f
            && IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove);
    }

    // Title Match's launch KOs off Training from about 75% (ADR-0053); a little earlier still carries it.
    private const ushort FinisherKillPercent = 70;

    private bool ReadyDashStrikeTip(in CharacterState self, in CpuObservation target,
        bool allowRecoveryMove, in ArenaDefinition arena, CookedSlotDefinition? activeMove)
    {
        bool air = !self.IsGrounded;
        var dash = Capability<CookedDashStrikeCapabilityParameters>(_profileDefinition!.GetCookedSlotAbility(AbilitySlots.R, air));
        if (dash == null || target.State == ActionState.Shielding) return false;
        var observed = new CharacterState { PX = target.PX, PY = target.PY, PZ = target.PZ };
        var candidate = Move(AbilitySlots.R, air);
        return DashStrikeReach.Classify(dash, in self, in observed, _profileDefinition) == DashStrikeContact.Tip
            && IsCandidateAvailable(self, candidate, air, allowRecoveryMove, in arena, activeMove);
    }

    private float Score(in MoveCandidate candidate, float dist, bool threatening)
        => (candidate.HasDamage ? 100f : 10f)
            + (threatening && candidate.HasDamage ? 5f : 0f)
            + candidate.Damage * 0.1f
            - (_profileDefinition!.AlternateForm != null ? MathF.Max(0f, dist - candidate.Reach)
                : MathF.Abs(candidate.Reach - dist)) * 0.2f
            - candidate.StartupTicks * 0.05f;

    private bool IsChargePoolExhausted(in CharacterState state, byte slot, bool air)
    {
        var cooked = _profileDefinition!.GetCookedSlotAbility(slot, air);
        int maxCharges = cooked?.ChargePool?.MaxCharges ?? 0;
        return maxCharges > 0 && state.ChargeStockSpent >= maxCharges;
    }

    private static bool EvaluateMove(CharacterDefinition def, byte activeSlot, bool airborne,
        BakedAnimationData? baked, out MoveCandidate candidate)
    {
        var cooked = def.GetCookedSlotAbility(activeSlot, airborne);
        if (cooked != null)
        {
            bool functional = false, hasDamage = false, hasMovement = false;
            float hitReach = 0f, movementReach = 0f, damage = 0f;
            float forwardTravel = 0f;
            int timelineTicks = cooked.Timeline.Stages.Sum(x => (int)x.DurationTicks);
            float recoveryHorizontal = 0f, recoveryVertical = 0f;
            bool recoveryRequiresDelayedOpponent = false;
            float travelSpeed = 0f, travelOffset = 0f;
            int startup = int.MaxValue;
            int travelTicks = 0;
            int stageOffset = 0;
            // A hit group strikes one victim once per activation, across stages; count it at its strongest.
            Span<float> groupDamage = stackalloc float[byte.MaxValue + 1];
            var animationNames = cooked.Timeline.Stages
                .SelectMany(x => x.AnimationIds).ToArray();
            for (int stageIndex = 0; stageIndex < cooked.Timeline.Stages.Count; stageIndex++)
            {
                var stage = cooked.Timeline.Stages[stageIndex];
                foreach (var operation in stage.Operations)
                {
                    int operationTick = stageOffset + operation.Tick;
                    switch (operation)
                    {
                        case CookedSpawnHitboxOperation hitbox:
                            startup = Math.Min(startup, operationTick);
                            functional = true;
                            var evt = CookedCharacterRuntimeAdapter.ToHitboxEvent(hitbox.Hitbox, operationTick);
                            float poseReach = PoseForwardReach(def, evt, activeSlot, airborne,
                                animationNames, (byte)stageIndex, baked);
                            hitReach = MathF.Max(hitReach,
                                poseReach > 0f ? poseReach : DirectForwardReach(evt));
                            hasDamage |= hitbox.Hitbox.Damage > 0f || hitbox.Hitbox.FixedHitstunTicks > 0;
                            if (hitbox.Hitbox.HitGroup == 0)
                                damage += MathF.Max(0f, hitbox.Hitbox.Damage);
                            else
                                groupDamage[hitbox.Hitbox.HitGroup] = MathF.Max(groupDamage[hitbox.Hitbox.HitGroup],
                                    hitbox.Hitbox.Damage);
                            break;
                        case CookedSpawnProjectileOperation projectile:
                            startup = Math.Min(startup, operationTick);
                            // A returning projectile reaches out only for its outbound time.
                            ushort flight = projectile.Projectile.Return?.OutboundTicks ?? projectile.Projectile.MaxFlightTicks;
                            travelTicks = Math.Max(travelTicks, flight);
                            travelSpeed = MathF.Max(travelSpeed, projectile.Projectile.Speed);
                            travelOffset = MathF.Max(travelOffset,
                                MathF.Max(0f, projectile.Projectile.LaunchOffsetZ)
                                + projectile.Projectile.Radius);
                            functional = true;
                            damage += MathF.Max(0f, projectile.Projectile.Damage) + (projectile.Projectile.Perch?.Pop.Damage ?? 0f);
                            hasDamage |= projectile.Projectile.Damage > 0f || projectile.Projectile.Perch != null;
                            hitReach = MathF.Max(hitReach,
                                travelOffset + projectile.Projectile.Speed * flight / 60f);
                            break;
                        case CookedSetVelocityOperation velocity:
                            bool hasVelocity = velocity.X != 0f || velocity.Y != 0f || velocity.Z != 0f;
                            functional |= hasVelocity;
                            hasMovement |= hasVelocity;
                            movementReach = MathF.Max(movementReach,
                                MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z)
                                * MathF.Max(0, stage.DurationTicks - operation.Tick) / 60f);
                            break;
                        case CookedForwardLungeOperation lunge:
                            functional = true;
                            hasMovement = true;
                            movementReach = MathF.Max(movementReach,
                                lunge.Speed * lunge.DurationTicks / 60f);
                            forwardTravel = MathF.Max(forwardTravel,
                                lunge.Speed * (timelineTicks - operationTick) / 60f);
                            break;
                        case CookedStartCapabilityOperation capability
                            when IsSupportedCapability(capability.Parameters):
                            startup = Math.Min(startup,
                                operationTick + CapabilityFirstActiveTicks(capability.Parameters));
                            CapabilityTravelParameters(capability.Parameters,
                                ref travelTicks, ref travelSpeed, ref travelOffset);
                            functional = true;
                            ApplyCapability(capability.Parameters, ref hitReach, ref movementReach,
                                ref damage, ref hasDamage, ref hasMovement,
                                ref recoveryHorizontal, ref recoveryVertical,
                                ref recoveryRequiresDelayedOpponent);
                            if (capability.Parameters is CookedAimedRecoveryCapabilityParameters recovery)
                            {
                                hasMovement = true;
                                float vertical = MathF.Max(recovery.VerticalSpeed, recovery.DirectionalVerticalSpeed);
                                float gravity = MathF.Max(1f, def.Movement.Gravity);
                                recoveryVertical = MathF.Max(recoveryVertical, vertical * vertical / (2f * gravity));
                                recoveryHorizontal = MathF.Max(recoveryHorizontal, recovery.HorizontalSpeed * 2f * vertical / gravity);
                            }
                            else if (capability.Parameters is CookedCycloneKickCapabilityParameters cyclone)
                            {
                                // Cyclone brakes when its capability expires, before timeline recovery ends.
                                float committed = cyclone.ForwardSpeed
                                    * Math.Min(cyclone.DurationTicks, timelineTicks - operationTick) / 60f;
                                forwardTravel = MathF.Max(forwardTravel, committed);
                                hitReach = MathF.Max(hitReach, committed
                                    + MathF.Max(cyclone.BodyRadius, cyclone.SideOffset + cyclone.SideRadius));
                            }
                            else if (capability.Parameters is CookedCommandGrabCapabilityParameters grab)
                                forwardTravel = MathF.Max(forwardTravel, grab.LungeSpeed * grab.LungeTicks / 60f);
                            else if (capability.Parameters is CookedDashStrikeCapabilityParameters dash)
                            {
                                forwardTravel = MathF.Max(forwardTravel, dash.Distance);
                                hitReach = MathF.Max(hitReach, dash.Distance + dash.TipHitbox.OffsetZ
                                    + dash.TipHitbox.EndOffsetZ + dash.TipHitbox.Radius);
                            }
                            break;
                    }
                }
                stageOffset += stage.DurationTicks;
            }
            foreach (float group in groupDamage)
                damage += group;
            candidate = new MoveCandidate(activeSlot, hitReach + movementReach, damage,
                functional, hasDamage, hasMovement,
                cooked.AimMode != AuthoringAimMode.None
                    || cooked.Behavior is AuthoringAbilityBehavior.AimedProjectile
                    or AuthoringAbilityBehavior.DirectionalDash
                    or AuthoringAbilityBehavior.ChargeAttack,
                cooked.IsRecoveryMove,
                startup == int.MaxValue ? (ushort)0 : (ushort)Math.Min(startup, ushort.MaxValue),
                (ushort)Math.Min(travelTicks, ushort.MaxValue), travelSpeed, travelOffset,
                recoveryHorizontal, recoveryVertical, recoveryRequiresDelayedOpponent,
                forwardTravel, timelineTicks);
            return functional;
        }

        candidate = default;
        return false;
    }

    private static float PoseForwardReach(CharacterDefinition def, in HitboxEvent evt,
        byte activeSlot, bool airborne, string[] animationNames, byte animIndex,
        BakedAnimationData? baked)
    {
        var samples = MoveReach.SampleHit(def, evt, (byte)(activeSlot - 1), airborne,
            animIndex, baked);
        float reach = 0f;
        foreach (var sample in samples)
            reach = MathF.Max(reach, MathF.Max(sample.Z0, sample.Z1) + sample.Radius);
        return MathF.Max(0f, reach);
    }

    private static float DirectForwardReach(in HitboxEvent evt)
        => MathF.Max(evt.OffZ, evt.EndOffZ) + evt.Radius;
    private static bool IsSupportedCapability(CookedCapabilityParameters parameters)
        => parameters is CookedAimedRecoveryCapabilityParameters
            or CookedRisingDragonCapabilityParameters
            or CookedCycloneKickCapabilityParameters
            or CookedChargedProjectileCapabilityParameters
            or CookedChargedStreamCapabilityParameters
            or CookedFingerGunsCapabilityParameters
            or CookedDashStrikeCapabilityParameters
            or CookedWibouRisingSlashCapabilityParameters
            or CookedFlashCutCapabilityParameters
            or CookedTargetedLeapCapabilityParameters
            or CookedSteeredChargeCapabilityParameters
            or CookedThrustCapabilityParameters
            or CookedTetherCapabilityParameters
            or CookedCommandGrabCapabilityParameters
            or CookedCannonballCapabilityParameters
            or CookedMankiRoundBombCapabilityParameters
            or CookedMankiJetpackBoostCapabilityParameters
            or CookedMankiBazookaCapabilityParameters;

    private static void ApplyCapability(CookedCapabilityParameters parameters,
        ref float hitReach, ref float movementReach, ref float damage,
        ref bool hasDamage, ref bool hasMovement,
        ref float recoveryHorizontal, ref float recoveryVertical,
        ref bool recoveryRequiresDelayedOpponent)
    {
        switch (parameters)
        {
            case CookedRisingDragonCapabilityParameters x:
                hasMovement = true; break;
            case CookedChargedProjectileCapabilityParameters x:
                hitReach = MathF.Max(hitReach, MathF.Max(0f, x.Projectile.LaunchOffsetZ)
                    + x.Projectile.Radius + x.Projectile.Speed * x.Projectile.MaxFlightTicks / 60f);
                damage += x.Projectile.Damage;
                hasDamage |= x.Projectile.Damage > 0f;
                break;
            case CookedFingerGunsCapabilityParameters x:
                hitReach = MathF.Max(hitReach, MathF.Max(0f, x.Projectile.LaunchOffsetZ)
                    + x.Projectile.Radius + x.Projectile.Speed * x.Projectile.MaxFlightTicks / 60f);
                damage += x.Projectile.Damage;
                hasDamage |= x.Projectile.Damage > 0f;
                break;
            case CookedChargedStreamCapabilityParameters x:
                hitReach = MathF.Max(hitReach, MathF.Max(0f, x.Projectile.LaunchOffsetZ)
                    + x.Projectile.Radius + x.Projectile.Speed * x.Projectile.MaxFlightTicks / 60f);
                damage += x.Projectile.Damage;
                hasDamage |= x.Projectile.Damage > 0f;
                break;
            case CookedCycloneKickCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ForwardSpeed * x.DurationTicks / 60f
                    + MathF.Max(x.BodyRadius, x.SideOffset + x.SideRadius));
                damage += x.Damage; hasDamage |= x.Damage > 0f; hasMovement = true; break;
            case CookedDashStrikeCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.Distance + x.TipHitbox.OffsetZ + x.TipHitbox.EndOffsetZ + x.TipHitbox.Radius);
                damage += MathF.Max(x.LightHitbox.Damage, x.TipHitbox.Damage);
                hasMovement = true; hasDamage |= x.LightHitbox.Damage > 0 || x.TipHitbox.Damage > 0;
                break;
            case CookedWibouRisingSlashCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.HomingRange);
                recoveryHorizontal = MathF.Max(recoveryHorizontal,
                    x.HomingSpeed * x.RiseTicks / 60f);
                recoveryVertical = MathF.Max(recoveryVertical,
                    x.RiseSpeed * x.RiseTicks / 60f);
                recoveryRequiresDelayedOpponent = true;
                hasMovement = true; hasDamage = true; break;
            case CookedFlashCutCapabilityParameters x:
                // Anyone inside the lock sphere when the stance ends is cut, wherever they run.
                hitReach = MathF.Max(hitReach, x.LockRange);
                damage += x.CutHitbox.Damage; hasDamage = true; hasMovement = true; break;
            case CookedTargetedLeapCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.MaxRange + MathF.Max(x.Hitbox.OffsetZ, x.Hitbox.EndOffsetZ) + x.Hitbox.Radius);
                damage += x.Hitbox.Damage; hasDamage |= x.Hitbox.Damage > 0f; hasMovement = true; break;
            case CookedThrustCapabilityParameters x:
                // The thrust only travels; the shell's bowling hitbox is not part of the move.
                float pitch = MathF.Max(x.NeutralPitchDegrees, x.StickPitchDegrees) * MathF.PI / 180f;
                recoveryHorizontal = MathF.Max(recoveryHorizontal, x.Speed * MathF.Cos(pitch) * x.ThrustTicks / 60f);
                recoveryVertical = MathF.Max(recoveryVertical, x.Speed * MathF.Sin(pitch) * x.ThrustTicks / 60f);
                hasMovement = true; break;
            case CookedCannonballCapabilityParameters x:
                // The hop only travels; the shell's splash is not part of the move.
                movementReach = MathF.Max(movementReach, x.MaxRange);
                hasMovement = true; break;
            case CookedTetherCapabilityParameters { AtTarget: { } at }:
                // The placed catch reaches as far as its spot; the reel is not modelled.
                hitReach = MathF.Max(hitReach, at.MaxRange + at.Radius);
                damage += at.Damage;
                hasDamage |= at.Damage > 0f;
                break;
            case CookedTetherCapabilityParameters { Projectile: { } ring }:
                // The ring is thrown like a projectile; the catch and the reel are not modelled.
                hitReach = MathF.Max(hitReach, MathF.Max(0f, ring.LaunchOffsetZ)
                    + ring.Radius + ring.Speed * ring.MaxFlightTicks / 60f);
                damage += ring.Damage;
                hasDamage |= ring.Damage > 0f;
                break;
            case CookedCommandGrabCapabilityParameters x:
                // The lunge catches whoever its capture meets; a hold stage's hits are its own hitboxes.
                hitReach = MathF.Max(hitReach, x.LungeSpeed * x.LungeTicks / 60f
                    + MathF.Max(x.Capture.OffsetZ, x.Capture.EndOffsetZ) + x.Capture.Radius);
                damage += x.Capture.Damage + (x.Launch?.Damage ?? 0f); hasDamage = true; hasMovement = true; break;
            case CookedSteeredChargeCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.GallopSpeed * x.MaxGallopTicks / 60f
                    + MathF.Max(x.LanceHitbox.OffsetZ, x.LanceHitbox.EndOffsetZ) + x.LanceHitbox.Radius);
                recoveryHorizontal = MathF.Max(recoveryHorizontal, x.GallopSpeed * x.AirStrideTicks / 60f);
                damage += x.LanceHitbox.Damage; hasDamage |= x.LanceHitbox.Damage > 0f; hasMovement = true; break;
            case CookedMankiRoundBombCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ExplosionRadius);
                damage += x.ExplosionDamage; hasDamage |= x.ExplosionDamage > 0f; break;
            case CookedMankiJetpackBoostCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.HorizontalSpeed + x.ExplosionRadius);
                recoveryHorizontal = MathF.Max(recoveryHorizontal, x.HorizontalSpeed);
                damage += x.ExplosionDamage; hasDamage |= x.ExplosionDamage > 0f; hasMovement = true; break;
            case CookedMankiBazookaCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ProjectileSpeed * x.MaxFlightTicks / 60f + x.HitboxRadius);
                damage += x.Damage; hasDamage |= x.Damage > 0f; break;
        }
    }

    private static ushort CapabilityFirstActiveTicks(CookedCapabilityParameters parameters)
        => parameters switch
        {
            CookedAimedRecoveryCapabilityParameters x => x.StartupTicks,
            CookedRisingDragonCapabilityParameters x => x.RiseDelay,
            CookedCycloneKickCapabilityParameters x => x.WindupTicks,
            CookedChargedProjectileCapabilityParameters x => x.FireTriggerTick,
            CookedChargedStreamCapabilityParameters x => x.FireTriggerTick,
            CookedTetherCapabilityParameters x => x.AtTarget?.WarningTicks ?? x.ThrowTick,
            CookedCommandGrabCapabilityParameters x => x.CaptureTick,
            CookedCannonballCapabilityParameters x => x.TakeoffTick,
            CookedFingerGunsCapabilityParameters x => x.FireTriggerTick,
            CookedDashStrikeCapabilityParameters _ => 0,
            CookedFlashCutCapabilityParameters x => x.StanceTicks,
            CookedWibouRisingSlashCapabilityParameters _ => 0,
            CookedTargetedLeapCapabilityParameters x => x.MaxAimTicks,
            CookedMankiRoundBombCapabilityParameters _ => 0,
            CookedMankiJetpackBoostCapabilityParameters x => x.StartupTicks,
            CookedMankiBazookaCapabilityParameters x => x.FireTriggerTick,
            _ => 0,
        };

    private static void CapabilityTravelParameters(CookedCapabilityParameters parameters,
        ref int travelTicks, ref float travelSpeed, ref float travelOffset)
    {
        switch (parameters)
        {
            case CookedFingerGunsCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.Projectile.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, x.Projectile.Speed);
                travelOffset = MathF.Max(travelOffset,
                    MathF.Max(0f, x.Projectile.LaunchOffsetZ) + x.Projectile.Radius);
                break;
            case CookedTetherCapabilityParameters { Projectile: { } ring }:
                travelTicks = Math.Max(travelTicks, ring.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, ring.Speed);
                travelOffset = MathF.Max(travelOffset,
                    MathF.Max(0f, ring.LaunchOffsetZ) + ring.Radius);
                break;
            case CookedChargedStreamCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.Projectile.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, x.Projectile.Speed);
                travelOffset = MathF.Max(travelOffset,
                    MathF.Max(0f, x.Projectile.LaunchOffsetZ) + x.Projectile.Radius);
                break;
            case CookedChargedProjectileCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.Projectile.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, x.Projectile.Speed);
                travelOffset = MathF.Max(travelOffset,
                    MathF.Max(0f, x.Projectile.LaunchOffsetZ) + x.Projectile.Radius);
                break;
            case CookedMankiRoundBombCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                travelOffset = MathF.Max(travelOffset, x.HitboxRadius);
                break;
            case CookedMankiBazookaCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, x.ProjectileSpeed);
                travelOffset = MathF.Max(travelOffset, x.HitboxRadius);
                break;
            case CookedTargetedLeapCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                break;
        }
    }

    public static float ForwardReach(CharacterDefinition def, byte activeSlot, bool airborne)
        => ForwardReach(def, activeSlot, airborne, baked: null);

    /// <summary>
    /// Resolved horizontal envelope for a slot. Cooked timeline hitboxes and typed capabilities
    /// define the admitted move metadata.
    /// </summary>
    public static float ForwardReach(CharacterDefinition def, byte activeSlot, bool airborne,
        BakedAnimationData? baked)
        => EvaluateMove(def, activeSlot, airborne, baked, out var candidate)
            ? candidate.Reach : 0f;

    private static short AimYaw(float dx, float dz)
        => (short)Math.Clamp((int)(MathF.Atan2(dx, dz) * (180f / MathF.PI) * 100f),
            -18000, 18000);

    private static short AimPitch(float dy, float dist)
        => (short)Math.Clamp((int)(MathF.Atan2(dy, MathF.Max(dist, 0.001f))
            * (180f / MathF.PI) * 100f), -9000, 9000);

    private static ushort AimDistance(float dist)
        => (ushort)Math.Clamp((int)(dist * 100f), 1, 6500);
}
