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
/// Delayed opponent facts select a persistent combat intent. Shared owns admission and action results.
/// Movement input is world-space and normalized by the mover; the Engage latch owns stopping.
/// </summary>
public sealed partial class HeuristicBotPolicy
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
        public readonly float ForwardTravel;
        public readonly int CommitmentTicks;
        public readonly CpuMoveTiming AimTiming;
        public readonly bool VerticalKnown;
        public readonly float LowReach, HighReach, LaunchStrength;
        public readonly CookedDashStrikeCapabilityParameters? DashStrike;
        public readonly CookedFlashCutCapabilityParameters? Flash;
        /// <summary>A command grab whose hold stage ends in the finisher (Title Match), and one without a hold stage.</summary>
        public readonly CookedCommandGrabCapabilityParameters? Finisher, CommandGrab;

        public MoveCandidate(byte slot, float reach, float damage, bool functional,
            bool hasDamage, bool hasMovement, bool requiresAim, bool isRecovery,
            ushort startupTicks, ushort travelTicks = 0, float travelSpeed = 0f,
            float travelOffset = 0f, float recoveryHorizontal = 0f,
            float recoveryVertical = 0f, bool recoveryRequiresDelayedOpponent = false,
            float forwardTravel = 0f, int commitmentTicks = 0, CookedSlotDefinition? cooked = null)
        {
            var extent = MoveExtent(cooked);
            VerticalKnown = extent.Known; LowReach = extent.Low; HighReach = extent.High; LaunchStrength = extent.Launch;
            DashStrike = Capability<CookedDashStrikeCapabilityParameters>(cooked);
            Flash = Capability<CookedFlashCutCapabilityParameters>(cooked);
            var grab = Capability<CookedCommandGrabCapabilityParameters>(cooked);
            Finisher = grab is { HasHoldStage: true } ? grab : null;
            CommandGrab = grab is { HasHoldStage: false } ? grab : null;
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
            ForwardTravel = MathF.Max(0f, forwardTravel);
            CommitmentTicks = commitmentTicks + (requiresAim ? AimHoldTicks : 0);
            AimTiming = new CpuMoveTiming(startupTicks + (requiresAim ? AimHoldTicks : 0),
                travelTicks, travelSpeed, travelOffset);
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

    private bool HasLandingBelow(in CharacterState state, in ArenaDefinition arena)
    {
        if (!ArenaCollision.HasTriangles(arena)) return HasSupportAt(state.PX, state.PY, state.PZ, arena);
        float bottom = arena.KillHeight + _recoveryCapsuleHeight * .5f;
        if (state.PY <= bottom) return false;
        float radius = _profileDefinition!.CapsuleRadius;
        int count = ArenaCollision.GetCandidateTrianglesForSweep(state.PX, state.PY, state.PZ,
            state.PX, bottom, state.PZ, radius, _recoveryCapsuleHeight, arena, _recoveryTriangleCandidates);
        if (count == 0 || !ArenaCollision.SweepCapsule(state.PX, state.PY, state.PZ, state.PX, bottom, state.PZ,
            radius, _recoveryCapsuleHeight, arena, _recoveryTriangleCandidates, count, out var contact)
            || contact.NormalY <= .5f) return false;
        float y = state.PY + (bottom - state.PY) * contact.Time;
        return ArenaCollision.IsTriangleInteriorContact(state.PX, y, state.PZ, radius, _recoveryCapsuleHeight, arena, contact);
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
            // Moving falls retain the landing route already admitted by steering.
            return !state.IsGrounded && state.VX * state.VX + state.VZ * state.VZ < 1f
                && !HasLandingBelow(state, arena);
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
    private bool NeedsRecovery(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena, BotMemory memory)
    {
        if (memory.Recovering && (self.Deaths != memory.RecoveryDeaths
            || HasLandingBelow(self, arena))) memory.Recovering = false;
        return memory.Recovering || IsOffstage(self, arena);
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
        if (!NeedsRecovery(self, def, arena, memory)) return false;
        memory.Recovering = true; memory.RecoveryDeaths = self.Deaths;
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


    private sealed class MoveProfile
    {
        public readonly CharacterDefinition Definition;
        public readonly BakedAnimationData? Baked;
        public readonly MoveCandidate[] Ground, Air;
        public readonly float GroundReach, AirReach;
        public readonly CpuMoveTiming GroundTiming, AirTiming;
        public MoveProfile(CharacterDefinition definition, BakedAnimationData? baked, MoveCandidate[] ground, MoveCandidate[] air)
        {
            Definition = definition; Baked = baked; Ground = ground; Air = air;
            (GroundReach, GroundTiming) = NeutralMetadata(ground);
            (AirReach, AirTiming) = NeutralMetadata(air);
        }
        private static (float, CpuMoveTiming) NeutralMetadata(MoveCandidate[] moves)
        {
            MoveCandidate best = default;
            foreach (var move in moves)
                if (move.Functional && IsNormalSlot(move.Slot) && move.Reach > best.Reach) best = move;
            float reach = best.Reach;
            if (reach == 0f)
                foreach (var move in moves)
                    if (move.Functional && move.HasDamage) reach = MathF.Max(reach, MathF.Min(6f, move.Reach));
            return (reach + VictimRadiusMargin, best.AimTiming);
        }
    }
    private readonly List<MoveProfile> _profiles = new();
    private readonly Dictionary<CookedSlotDefinition, CpuAttackModel> _timingModels = new();
    private MoveProfile? _currentProfile;
    private CharacterDefinition? _profileDefinition;
    private BakedAnimationData? _profileBaked;
    private MoveCandidate[] _profile = Array.Empty<MoveCandidate>();
    private MoveCandidate[] _airProfile = Array.Empty<MoveCandidate>();

    /// <summary>Capture the target's current-form reach with the observation, subject to the same reaction delay.</summary>
    public void ObserveOpponent(BotMemory memory, in CharacterState target, CharacterDefinition definition,
        BakedAnimationData? baked = null, CookedSlotDefinition? activeMove = null)
    {
        var profile = FindProfile(definition.ForForm(target.Form.Active), baked);
        float reach = 0f;
        foreach (var move in target.IsGrounded ? profile.Ground : profile.Air)
            if (target.AttackSlot == 0 ? IsNormalSlot(move.Slot) : move.Slot == target.AttackSlot)
                reach = MathF.Max(reach, move.Reach);
        var timing = activeMove != null && _timingModels.TryGetValue(activeMove, out var model)
            ? model.Capture(target) : CpuAttackModel.WithoutActivation(target);
        memory.ObserveOpponent(target, reach, timing);
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
        memory.WalkProbesThisTick = 0;
        InputState input;
        if (mayhemThreats.HasValue && mayhemThreats.Value.TryAvoid(self, def, arena, out input))
        {
            memory.ClearPlan();
            memory.ForgetCombat();
            memory.EndClimb();
        }
        else
        {
            def = def.ForForm(self.Form.Active);
            input = default;
            if (memory.Behavior != behavior) memory.EndClimb();
            // Finish a validated climb before selecting attacks or a recovery. Either can cancel the full jump
            // against the face before the capsule reaches the ledge top.
            if (!ContinueClimb(ref input, self, def, arena, memory))
            {
                input = DecideAction(self, def, rng, memory, in arena, baked, behavior, activeMove, canSwapCancel,
                    out var step);
                // Attacks and neutral input keep run momentum; Shield is the immediate brake.
                if (!step.Moves && input.ActiveSlot == 0 && !input.GrabPressed && !input.Jump
                    && !input.IsAiming && self.IsGrounded && OwnWait(self, activeMove) == 0
                    && Admits(self, activeMove) && !HasSafeCoast(self, arena)) input.ShieldHeld = true;
                if (ApplyFootstep(ref input, step, self, def, in arena, memory) == FootstepOutcome.Blocked
                    && memory.Combat.Kind != CpuCombatPlanKind.None)
                    memory.EndCombat(CpuPlanEnd.Blocked, space: true);
            }
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
        { memory.ClearPlan(); memory.ForgetCombat(); memory.EndClimb(); memory.Behavior = behavior; }
        EnsureProfile(def, baked);
        EnsureRecoveryTargets(in arena, in def);
        if (self.HitstunTicks > 0 || memory.Combat.Kind != CpuCombatPlanKind.None && self.Deaths != memory.Combat.Life)
        { memory.ClearPlan(); memory.ForgetCombat(); return default; }
        bool actionable = OwnWait(self, activeMove) == 0 && Admits(self, activeMove);
        if (self.State == ActionState.LedgeHang || actionable && memory.PlanKind != BotPlanKind.Recovery && NeedsRecovery(self, def, arena, memory))
        {
            memory.EndCombat(CpuPlanEnd.Interrupted);
            return TryBuildRecoveryInput(self, def, arena, memory, out var recovery, activeMove) ? recovery : default;
        }
        if (behavior == CpuBehavior.Attack && self.LandingLagTicks == 0 && self.HitstopTicks == 0 && self.HitstunTicks == 0
            && self.BlockStunTicks == 0 && canSwapCancel && memory.Difficulty != CpuDifficulty.Easy
            && self.AttackSlot != AbilitySlots.F && def.AlternateForm != null && self.GetCooldown(AbilitySlots.F) == 0
            && memory.TryGetNewContact(out var confirmed) && confirmed.Kind == CpuContactKind.Hit && confirmed.Slot == self.AttackSlot)
        {
            memory.MarkContactEvaluated(confirmed); memory.ClearPlan(); memory.EndCombat(CpuPlanEnd.Completed);
            return new InputState { ActiveSlot = AbilitySlots.F,
                AimYaw = AimYaw(confirmed.PX - self.PX, confirmed.PZ - self.PZ), FaceToCamera = true };
        }
        if (memory.PlanPhase != BotPlanPhase.None)
        {
            if (!self.IsGrounded) memory.PlanWasAirborne = true;
            var result = ContinuePlan(self, def, memory, arena, activeMove, out var execution);
            if (execution == CpuExecutionResult.Rejected) memory.EndCombat(CpuPlanEnd.Rejected, space: true);
            else if (execution == CpuExecutionResult.Accepted) memory.Combat.Accepted();
            return result;
        }
        if (behavior == CpuBehavior.Shield)
            return actionable && self.IsGrounded || self.State == ActionState.Shielding
                ? new InputState { ShieldHeld = true } : default;
        if (!memory.TryGetDelayedOpponent(out var target))
        { memory.EndCombat(CpuPlanEnd.Interrupted); return default; }
        var facts = new CpuFacts(self, target, def, arena, activeMove, memory.Clock, OwnWait(self, activeMove), baked);
        if (behavior == CpuBehavior.Avoid) return Avoid(facts, rng, memory, out step);
        return ContinueCombatPlan(facts, rng, memory, out step);
    }

    private static bool StartAttack(ref InputState input, in CpuMoveChoice selected, in CharacterState self,
        in CpuObservation target, in ArenaDefinition arena, BotMemory memory)
    {
        if (selected.Kind == CpuActionKind.Grab)
        {
            input.AimYaw = selected.AimYaw;
            return StartGrab(ref input, self, BotDifficultyProfile.ForDifficulty(memory.Difficulty), memory);
        }
        var point = selected.Point;
        float dist = point.DistanceFrom(self), dy = point.Y - self.PY;
        input.AimYaw = dist > 0.001f ? AimYaw(point.X - self.PX, point.Z - self.PZ)
            : AimYaw(target.PX - self.PX, target.PZ - self.PZ);
        input.ActiveSlot = selected.WireSlot;
        input.AimPitch = AimPitch(dy, dist);
        input.AimDistance = AimDistance(dist);
        memory.StartPlan(selected.WireSlot, selected.RequiresAim,
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
    private static bool InCaptureVolume(in CharacterState self, in CpuTargetPoint target, CharacterDefinition def)
    {
        var capture = def.CaptureGeometry;
        if (capture == null)
            return false;
        float bottom = self.PY + capture.OffsetY - capture.Height * 0.5f;
        float half = def.CapsuleHeight * 0.5f;
        return Distance(target.X - self.PX, target.Z - self.PZ) <= capture.Reach + def.CapsuleRadius
            && target.Y - half <= bottom + capture.Height + def.CapsuleRadius
            && target.Y + half >= bottom - def.CapsuleRadius;
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
                return new InputState { ActiveSlot = AbilitySlots.Normal1, MoveX = MathF.Sin(yaw), MoveY = MathF.Cos(yaw) };
            }
        }
        memory.ClearPlan();
        return default;
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

    private enum FootstepOutcome : byte { Standing, Walking, ClimbStarted, Blocked }

    private static FootstepOutcome ApplyFootstep(ref InputState input, in Footstep step, in CharacterState self,
        CharacterDefinition def, in ArenaDefinition arena, BotMemory memory)
    {
        if (!step.Moves)
            return FootstepOutcome.Standing;
        if (self.IsGrounded && self.State != ActionState.LedgeHang && InsideBounds(self, def, in arena))
        {
            return Steer(ref input, step, self, def, in arena, memory);
        }
        input.MoveX = step.DirX;
        input.MoveY = step.DirZ;
        return FootstepOutcome.Walking;
    }

    private static bool InsideBounds(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena)
        => !HasArenaBounds(arena)
            || self.PX >= arena.MinX + def.CapsuleRadius && self.PX <= arena.MaxX - def.CapsuleRadius
                && self.PZ >= arena.MinZ + def.CapsuleRadius && self.PZ <= arena.MaxZ - def.CapsuleRadius;

    /// <summary>
    /// Walk the heading the engine's mover accepts: the asked heading, else the wall tangent, then 45 and 90
    /// degrees off it, preferred side first. A raised goal behind a jumpable ledge starts a climb.
    /// </summary>
    private static FootstepOutcome Steer(ref InputState input, in Footstep step, in CharacterState self,
        CharacterDefinition def, in ArenaDefinition arena, BotMemory memory)
    {
        float runStep = def.Movement.RunSpeed * Simulation.TickDt;
        bool dense = arena.CollisionTriangles != null
            && arena.CollisionTriangles.Length > StageWalk.DenseTriangleThreshold;
        int probeBudget = dense ? 2 : 6;
        float length = dense ? MathF.Max(0.6f, runStep * 2f)
            : MathF.Max(0.75f, def.Movement.RunSpeed * 0.3f);
        if (step.HasGoal)
            length = MathF.Max(runStep, MathF.Min(length, step.GoalDistance + def.CapsuleRadius));
        float rise = JumpRise(def);
        int side = memory.Side == 0 ? 1 : memory.Side;

        memory.WalkProbesThisTick++;
        var verdict = StageWalk.Probe(self, def, in arena, step.DirX, step.DirZ, length, rise);
        if (TryWalk(ref input, verdict, step.DirX, step.DirZ, step, self, def, memory))
        {
            memory.DetourCursor = 0;
            return memory.Climbing ? FootstepOutcome.ClimbStarted : FootstepOutcome.Walking;
        }
        bool wall = verdict.End is WalkEnd.Wall or WalkEnd.Ledge;
        int count = wall ? 5 : 4;
        int cursor = dense ? memory.DetourCursor % count : 0;
        int attempted = 0;
        while (attempted < count && memory.WalkProbesThisTick < probeBudget)
        {
            float x, z;
            int turn;
            if (wall && cursor == 0)
            {
                x = -verdict.NormalZ;
                z = verdict.NormalX;
                float along = x * step.DirX + z * step.DirZ;
                float left = x * -step.DirZ + z * step.DirX;
                if (along < -0.001f || MathF.Abs(along) <= 0.001f && left * side < 0f)
                {
                    x = -x;
                    z = -z;
                    left = -left;
                }
                turn = left >= 0f ? 1 : -1;
            }
            else
            {
                int i = cursor - (wall ? 1 : 0);
                turn = (i & 1) == 0 ? side : -side;
                float cos = i < 2 ? HalfSqrt2 : 0f, sin = turn * (i < 2 ? HalfSqrt2 : 1f);
                x = step.DirX * cos - step.DirZ * sin;
                z = step.DirZ * cos + step.DirX * sin;
            }
            if (TryDetour(ref input, x, z, turn, step, self, def, in arena, length, rise, memory))
            {
                memory.DetourCursor = cursor;
                return memory.Climbing ? FootstepOutcome.ClimbStarted : FootstepOutcome.Walking;
            }
            cursor = (cursor + 1) % count;
            attempted++;
        }
        memory.DetourCursor = cursor;
        input.MoveX = input.MoveY = 0f;
        // Neutral coasts for twenty ticks. Shield is the ordinary input that stops before a refused edge.
        input.ShieldHeld = self.VX * self.VX + self.VZ * self.VZ > 0.0001f;
        return FootstepOutcome.Blocked;
    }

    private static bool TryDetour(ref InputState input, float x, float z, int turn, in Footstep step,
        in CharacterState self, CharacterDefinition def, in ArenaDefinition arena, float length, float rise,
        BotMemory memory)
    {
        memory.WalkProbesThisTick++;
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
        in ArenaDefinition arena, BotMemory memory)
    {
        if (!memory.Climbing)
            return false;
        if (!self.IsGrounded)
            memory.ClimbLeftGround = true;
        if (memory.PlanPhase != BotPlanPhase.None || input.ActiveSlot != 0 || input.ShieldHeld || input.GrabPressed
            || self.HitstunTicks > 0 || self.Deaths != memory.ClimbDeaths
            || memory.Behavior != CpuBehavior.Attack
            || memory.ClimbLeftGround && self.IsGrounded && StageWalk.HasLanding(self, def, arena)
            || memory.ClimbTicks > ClimbTicksMax)
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
        BotMemory memory, in ArenaDefinition arena, CookedSlotDefinition? activeMove, out CpuExecutionResult execution)
    {
        execution = CpuExecutionResult.Continuing;
        if (memory.PlanKind == BotPlanKind.Grab)
        {
            var input = ContinueGrab(self, memory);
            if (memory.PlanPhase == BotPlanPhase.None) execution = self.State == ActionState.Grabbing
                ? CpuExecutionResult.Accepted : CpuExecutionResult.Rejected;
            return input;
        }
        if (PlanInvalidated(self, memory))
        { memory.ClearPlan(); execution = CpuExecutionResult.Rejected; return default; }
        if (self.HitstopTicks > 0) return PlanInput(memory, memory.PlanPhase == BotPlanPhase.AimHold);
        if (!memory.PlanPressIssued)
        {
            if (!CanPress(self, def, memory.PlanSlot, activeMove)) return default;
            var candidates = self.IsGrounded ? _profile : _airProfile;
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i].Slot == memory.PlanSlot && !HasSafeAttackTravel(self, candidates[i], arena))
                { memory.ClearPlan(); execution = CpuExecutionResult.Rejected; return default; }
            memory.PlanPressIssued = true;
            return PlanPressInput(memory);
        }
        if (memory.PlanPhase == BotPlanPhase.AimHold && self.State == ActionState.Aiming && self.AttackSlot == memory.PlanSlot)
        {
            bool aiming = memory.PlanTicks < memory.PlanHoldTicks;
            var planned = PlanInput(memory, aiming);
            if (aiming) memory.PlanTicks++;
            else { memory.ClearPlan(); execution = CpuExecutionResult.Accepted; }
            return planned;
        }
        execution = (self.State is ActionState.Attacking or ActionState.Aiming) && self.AttackSlot == memory.PlanSlot
            ? CpuExecutionResult.Accepted : CpuExecutionResult.Rejected;
        memory.ClearPlan(); return default;
    }

    private static bool CanPress(in CharacterState self, CharacterDefinition def, byte slot, CookedSlotDefinition? activeMove)
        => self.HitstunTicks == 0
            && self.HitstopTicks == 0
            && self.BlockStunTicks == 0
            && self.LandingLagTicks == 0
            && (self.AnimLockTicks == 0 || Simulation.IsIasaUnlocked(in self, activeMove))
            && Admits(self, activeMove)
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

    private static float Distance(float x, float z)
        => MathF.Sqrt(x * x + z * z);

    private static bool IsNormalSlot(byte slot)
        => slot is AbilitySlots.Normal1 or AbilitySlots.Normal2 or AbilitySlots.Normal3;
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
            foreach (bool airborne in new[] { false, true })
            {
                var slot = def.GetCookedSlotAbility(Slots[i], airborne);
                if (slot != null && !_timingModels.ContainsKey(slot)) _timingModels.Add(slot, new CpuAttackModel(slot));
            }
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
        _currentProfile = profile;
        _profileDefinition = def;
        _profileBaked = baked;
        _profile = profile.Ground;
        _airProfile = profile.Air;
    }
    private bool HasSafeCoast(in CharacterState self, in ArenaDefinition arena)
    {
        if (!HasArenaBounds(arena) || self.VX * self.VX + self.VZ * self.VZ < .0001f) return true;
        float x = self.VX * GroundSpeedTuning.StopTicks * Simulation.TickDt;
        float z = self.VZ * GroundSpeedTuning.StopTicks * Simulation.TickDt;
        float distance = Distance(x, z);
        int steps = Math.Min(16, Math.Max(1, (int)MathF.Ceiling(distance / .5f)));
        for (int step = 1; step <= steps; step++)
        {
            var point = self;
            point.PX += x * step / steps; point.PZ += z * step / steps;
            if (!InsideBounds(point, _profileDefinition!, arena) || !HasLandingBelow(point, arena)) return false;
        }
        return true;
    }

    private bool HasSafeAttackTravel(in CharacterState self, in MoveCandidate candidate,
        in ArenaDefinition arena, float routeX = 0f, float routeZ = 0f, bool checkLanding = true, bool checkCoast = true)
    {
        if (!HasArenaBounds(arena))
            return true;

        if (checkLanding && !self.IsGrounded && !HasLandingBelow(self, arena)) return false;

        // Lunges capture facing before this tick's facing input. Other aerial moves
        // inherit velocity, including a previous lunge's momentum during their lock.
        float dx, dz;
        if (routeX != 0f || routeZ != 0f) { dx = routeX; dz = routeZ; }
        else if (candidate.ForwardTravel > 0f)
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
            return !checkCoast || HasSafeCoast(self, arena);

        float travel = Distance(dx, dz);
        if (travel <= 0f)
            return true;
        float radius = _profileDefinition!.CapsuleRadius;
        bool route = routeX != 0f || routeZ != 0f;
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
            if ((route || candidate.ForwardTravel > 0f && self.IsGrounded) && arena.Heightmap.Data != null)
            {
                float floor = arena.Heightmap.Sample(x, z);
                float feet = route && !self.IsGrounded ? arena.Heightmap.Sample(self.PX, self.PZ)
                    : self.PY - _profileDefinition.CapsuleHeight * .5f;
                if (MathF.Abs(floor - feet) > .35f
                    || !HasSupportAt(x + radius, self.PY, z, arena) || !HasSupportAt(x - radius, self.PY, z, arena)
                    || !HasSupportAt(x, self.PY, z + radius, arena) || !HasSupportAt(x, self.PY, z - radius, arena)) return false;
            }
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
            && HasSafeAttackTravel(self, candidate, in arena, checkLanding: false, checkCoast: false);
    private CpuMoveTiming EngageTiming(in CharacterState self)
        => self.IsGrounded ? _currentProfile!.GroundTiming : _currentProfile!.AirTiming;

    /// <summary>The first capability of this type, resolved when caching a move profile.</summary>
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

    private const ushort FinisherKillPercent = 70;

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
                                operationTick + CapabilityFirstActiveTicks(capability.Parameters, stage.DurationTicks));
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
                forwardTravel, timelineTicks, cooked);
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
            case CookedFlashCutCapabilityParameters:
                // A counter only answers a threat (the Defend plan), so it never joins the attacks.
                break;
            case CookedTargetedLeapCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.MaxRange + MathF.Max(x.Hitbox.OffsetZ, x.Hitbox.EndOffsetZ) + x.Hitbox.Radius);
                recoveryVertical = MathF.Max(recoveryVertical, x.RiseHeight);
                recoveryHorizontal = MathF.Max(recoveryHorizontal, x.NoTargetDistance);
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
            case CookedTetherCapabilityParameters { AtTarget.Unmarked: { } hand }:
                // The CPU does not read marks, so it plans for the unmarked hand ahead of her.
                hitReach = MathF.Max(hitReach, hand.Distance + hand.Radius);
                damage += hand.Damage;
                hasDamage = true;
                break;
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
                damage += x.LanceHitbox.Damage + x.Carry.Fling.Damage; hasDamage |= x.LanceHitbox.Damage > 0f; hasMovement = true; break;
            case CookedMankiRoundBombCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.TriggerRadius);
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

    private static ushort CapabilityFirstActiveTicks(CookedCapabilityParameters parameters, ushort stageDuration)
        => parameters switch
        {
            CookedAimedRecoveryCapabilityParameters x => x.StartupTicks,
            CookedRisingDragonCapabilityParameters x => x.RiseDelay,
            CookedCycloneKickCapabilityParameters x => x.WindupTicks,
            CookedChargedProjectileCapabilityParameters x => x.FireTriggerTick,
            CookedChargedStreamCapabilityParameters x => x.FireTriggerTick,
            CookedTetherCapabilityParameters x => x.AtTarget?.Unmarked?.RiseTick ?? x.AtTarget?.WarningTicks ?? x.ThrowTick,
            CookedCommandGrabCapabilityParameters x => x.CaptureTick,
            CookedCannonballCapabilityParameters x => x.TakeoffTick,
            CookedFingerGunsCapabilityParameters x => x.FireTriggerTick,
            CookedDashStrikeCapabilityParameters _ => 0,
            CookedFlashCutCapabilityParameters x => x.StanceTicks,
            CookedWibouRisingSlashCapabilityParameters _ => 0,
            CookedTargetedLeapCapabilityParameters x => (ushort)(x.RiseTicks + x.HangTicks + x.DropTicks),
            CookedSteeredChargeCapabilityParameters _ => stageDuration,
            CookedMankiRoundBombCapabilityParameters x => (ushort)(x.ArmTicks + x.WarningTicks),
            CookedMankiJetpackBoostCapabilityParameters x => x.StartupTicks,
            CookedMankiBazookaCapabilityParameters x => x.ChargeTicks > 0 ? x.ChargeTicks : x.FireTriggerTick,
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
            case CookedSteeredChargeCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxGallopTicks);
                travelSpeed = MathF.Max(travelSpeed, x.GallopSpeed);
                travelOffset = MathF.Max(travelOffset, MathF.Max(x.LanceHitbox.OffsetZ, x.ChestHitbox.OffsetZ)
                    + MathF.Max(x.LanceHitbox.Radius, x.ChestHitbox.Radius));
                break;
            case CookedTargetedLeapCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.DropTicks);
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
