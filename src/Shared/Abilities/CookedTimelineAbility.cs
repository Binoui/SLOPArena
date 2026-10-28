using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities;

public sealed class CookedTimelineAbility : ServerAbility
{
    private readonly CookedSlotDefinition _slot;
    private readonly List<ServerAbility> _capabilities = new();
    private ushort _stageTick;
    private int _stageIndex;
    private int _operationCursor;
    private bool _completed;
    private bool _unlimitedAimHold;
    private float _forwardLungeSpeed;
    private float _forwardLungeYaw;
    private ushort _forwardLungeTicksRemaining;
    private bool _forwardLungeActive;
    private Hitbox[] _lungeReach = Array.Empty<Hitbox>();
    private ushort _gravityWindowTicksRemaining;
    private float _gravityWindowScale = 1f;
    private readonly bool _timelineOwnsVerticalMotion;
    private readonly CookedStartupAimCorrectionOperation? _startupCorrection;
    private readonly int _startupCorrectionStage;
    internal bool UsesStartupAimCorrection { get; }

    public override float GravityMultiplier
    {
        get
        {
            float multiplier = _gravityWindowTicksRemaining > 0 ? _gravityWindowScale : 1f;
            // A capability may hold its own height (a charge striding on thin air).
            for (var i = 0; i < _capabilities.Count; i++)
                multiplier = MathF.Min(multiplier, _capabilities[i].GravityMultiplier);
            return multiplier;
        }
    }
    public override bool OwnsVerticalMotion
    {
        get
        {
            if (_timelineOwnsVerticalMotion)
                return true;
            for (var i = 0; i < _capabilities.Count; i++)
                if (_capabilities[i].OwnsVerticalMotion)
                    return true;
            return false;
        }
    }
    public override bool HasArmor
    {
        get
        {
            if (base.HasArmor)
                return true;
            for (var i = 0; i < _capabilities.Count; i++)
                if (_capabilities[i].HasArmor)
                    return true;
            return false;
        }
    }
    public override bool IgnoresFighterPushboxes
    {
        get
        {
            for (var i = 0; i < _capabilities.Count; i++)
                if (_capabilities[i].IgnoresFighterPushboxes)
                    return true;
            return false;
        }
    }

    public override bool HoldsFighterPushboxPosition
    {
        get
        {
            if (_completed)
                return false;
            for (var i = 0; i < _capabilities.Count; i++)
                if (_capabilities[i].HoldsFighterPushboxPosition)
                    return true;
            // Pulling victims into body contact must not push the caster out of the combo.
            foreach (var operation in CurrentStage.Operations)
                if (operation is CookedSpawnHitboxOperation hitbox &&
                    hitbox.Hitbox.KnockbackDirection == AuthoringKnockbackDirection.TowardOwner)
                    return true;
            return false;
        }
    }


    public override CookedSlotDefinition Move => _slot;

    public CookedTimelineAbility(CookedSlotDefinition slot)
    {
        _slot = slot ?? throw new ArgumentNullException(nameof(slot));
        bool ownsVerticalMotion = false;
        for (var i = 0; i < _slot.Timeline.Stages.Count; i++)
        {
            var operations = _slot.Timeline.Stages[i].Operations;
            for (var j = 0; j < operations.Count; j++)
            {
                if (operations[j] is CookedStartupAimCorrectionOperation correction)
                {
                    _startupCorrection = correction;
                    _startupCorrectionStage = i;
                    UsesStartupAimCorrection = true;
                }
                if (operations[j] is CookedSetVelocityOperation velocity &&
                    (velocity.VelocityMode is AuthoringVelocityMode.Absolute or AuthoringVelocityMode.Vertical ||
                     velocity.Y != 0f))
                {
                    ownsVerticalMotion = true;
                }
            }
        }
        _timelineOwnsVerticalMotion = ownsVerticalMotion;
    }
    internal ActiveMoveView GetMoveView(in CharacterState owner)
        => new(_slot, _stageIndex, _stageTick, AirborneAtStart, AnimIndex,
            ActiveMoveView.ResolvePose(_slot, AnimIndex, owner.AttackElapsedTicks));

    internal bool IsHoldingAim => _unlimitedAimHold;
    internal bool ContinuesThroughLanding
    {
        get
        {
            for (var i = 0; i < _capabilities.Count; i++)
                if (_capabilities[i] is ILandingContinuationCapability)
                    return true;
            return false;
        }
    }


    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _stageIndex = 0;
        _stageTick = 0;
        _operationCursor = 0;
        _completed = false;
        _unlimitedAimHold = false;
        _forwardLungeSpeed = 0f;
        _forwardLungeYaw = 0f;
        _forwardLungeTicksRemaining = 0;
        _forwardLungeActive = false;
        _gravityWindowTicksRemaining = 0;
        s.ClearStartupAimCorrection();
        ClearArmorWindow();
        _gravityWindowScale = 1f;
        s.State = ActionState.Attacking;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = false;
        AnimIndex = 0;
        s.AnimLockTicks = CurrentStage.DurationTicks;
        if (_startupCorrection != null)
        {
            if (OwnerSimulation == null)
                throw new InvalidOperationException("Startup correction requires a registered simulation.");
            OwnerSimulation.BeginStartupAimCorrection(ref s, _startupCorrection, ActivationInput);
        }
        ExecuteOperations(ref s, def);
        if (_completed)
            return;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_completed)
            return;
        ApplyForwardLunge(ref s);
        if (_gravityWindowTicksRemaining > 0)
            _gravityWindowTicksRemaining--;


        bool wasAiming = _unlimitedAimHold && s.State == ActionState.Aiming;
        if (!wasAiming)
        {
            _stageTick++;
            UpdateStartupAimCorrection(ref s);
        }
        else if (_startupCorrection != null)
        {
            // Validate the captured target while held without fighting manual aim.
            OwnerSimulation!.UpdateStartupAimCorrection(ref s, _startupCorrection, applyPose: false);
        }
        ExecuteOperations(ref s, def);
        if (_completed)
            return;

        for (var i = 0; i < _capabilities.Count; i++)
        {
            var capability = _capabilities[i];
            byte previousClip = capability.AnimIndex;
            capability.Tick(ref s, ref input, def);
            if (capability.AnimIndex != previousClip)
                AnimIndex = capability.AnimIndex;
        }

        // Aim-hold capabilities freeze stage time during aiming; reset the clock on release.
        if (wasAiming && s.State != ActionState.Aiming)
        {
            _stageTick = 0;
            if (_startupCorrection != null)
            {
                // Capabilities have committed their final manual/cached aim.
                s.AttackCorrectionStartYaw = s.FacingYaw;
                UpdateStartupAimCorrection(ref s);
            }
        }
        if (wasAiming)
            return;

        // Landing-continuation capabilities own their end: landing re-seeks animation time,
        // and impact plus recovery may outlast the authored stage duration.
        // Unrelated capabilities retain the ordinary stage clock.
        if (_stageTick >= CurrentStage.DurationTicks && !ContinuesThroughLanding)
        {
            if (_stageIndex + 1 >= _slot.Timeline.Stages.Count)
            {
                Complete(ref s);
                return;
            }

            _stageIndex++;
            _stageTick = 0;
            _operationCursor = 0;
            s.ComboStage = (byte)_stageIndex;
            AnimIndex = _slot.FirstClipIndex(_stageIndex);
            s.AnimLockTicks = CurrentStage.DurationTicks;
            ExecuteOperations(ref s, def);
        }
    }

    public override void OnEnd(ref CharacterState s)
    {
        CompleteCapabilities(ref s, cancel: false);
        s.ClearStartupAimCorrection();
        s.SlideAttackCarryActive = false;
        _gravityWindowTicksRemaining = 0;
        _gravityWindowScale = 1f;
        ClearArmorWindow();
    }

    public override void OnCancel(ref CharacterState s)
    {
        CompleteCapabilities(ref s, cancel: true);
        s.ClearStartupAimCorrection();
        s.SlideAttackCarryActive = false;
        s.IsAiming = false;
        _forwardLungeActive = false;
        _gravityWindowTicksRemaining = 0;
        _gravityWindowScale = 1f;
        _forwardLungeTicksRemaining = 0;
        ClearArmorWindow();
    }
    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
    {
        for (var i = 0; i < _capabilities.Count; i++)
            _capabilities[i].OnHitEntity(ref attacker, ref target, attackerDef, targetDef, ref damage, ref knockbackForce);
    }
    public override void OnShieldBlock(ref CharacterState attacker, ref CharacterState defender)
    {
        for (var i = 0; i < _capabilities.Count; i++)
            _capabilities[i].OnShieldBlock(ref attacker, ref defender);
    }
    public override void OnHitObject(ref CharacterState attacker, CharacterDefinition attackerDef,
        ref float damage, ref float knockbackForce)
    {
        for (var i = 0; i < _capabilities.Count; i++)
            _capabilities[i].OnHitObject(ref attacker, attackerDef, ref damage, ref knockbackForce);
    }

    private void UpdateStartupAimCorrection(ref CharacterState s)
    {
        if (_startupCorrection == null || !s.AttackCorrectionActive)
            return;
        if (_stageIndex > _startupCorrectionStage ||
            _stageIndex == _startupCorrectionStage && _stageTick >= _startupCorrection.EndTick)
        {
            s.AttackCorrectionActive = false;
            return;
        }
        OwnerSimulation!.UpdateStartupAimCorrection(ref s, _startupCorrection,
            applyPose: _stageIndex == _startupCorrectionStage && _stageTick >= _startupCorrection.Tick);
    }


    private CookedStage CurrentStage => _slot.Timeline.Stages[_stageIndex];

    private void ExecuteOperations(ref CharacterState s, CharacterDefinition def)
    {
        var operations = CurrentStage.Operations;
        while (_operationCursor < operations.Count && operations[_operationCursor].Tick == _stageTick)
        {
            var operation = operations[_operationCursor++];
            int flattenedOperationIndex = _operationCursor - 1;
            for (int i = 0; i < _stageIndex; i++)
                flattenedOperationIndex += _slot.Timeline.Stages[i].Operations.Count;
            switch (operation)
            {
                case CookedStartupAimCorrectionOperation:
                    // OnStart captures once; the cached profile drives its authored window.
                    break;
                case CookedSetVelocityOperation { VelocityMode: AuthoringVelocityMode.Vertical } vertical:
                    // A hop that keeps the horizontal carry (a normal out of the shell at skate speed).
                    s.IsFastFalling = false;
                    s.VY = vertical.Y;
                    break;
                case CookedSetVelocityOperation velocity:
                    ClearVelocityOwnership(ref s);
                    bool verticalWrite = velocity.VelocityMode == AuthoringVelocityMode.Absolute || velocity.Y != 0f;
                    if (verticalWrite)
                        s.IsFastFalling = false;
                    if (velocity.VelocityMode == AuthoringVelocityMode.Absolute)
                    {
                        s.VX = velocity.X;
                        s.VY = velocity.Y;
                        s.VZ = velocity.Z;
                    }
                    else
                    {
                        s.VX += velocity.X;
                        s.VY += velocity.Y;
                        s.VZ += velocity.Z;
                    }
                    break;
                case CookedForwardLungeOperation lunge:
                    ClearVelocityOwnership(ref s);
                    StartForwardLunge(ref s, lunge);
                    break;
                case CookedGravityWindowOperation gravity:
                    _gravityWindowScale = gravity.GravityScale;
                    _gravityWindowTicksRemaining = gravity.DurationTicks;
                    break;
                case CookedArmorWindowOperation armor:
                    StartArmorWindow(armor.DurationTicks);
                    break;
                case CookedSpawnHitboxOperation hitbox:
                    SpawnCookedHitbox(ref s, hitbox.Hitbox);
                    break;
                case CookedSpawnProjectileOperation projectile:
                    SpawnCookedProjectile(ref s, projectile.Projectile, flattenedOperationIndex,
                        _slot.Behavior == AuthoringAbilityBehavior.AimedProjectile && _slot.AimMode == AuthoringAimMode.None);
                    break;
                case CookedSetAimStateOperation aim:
                    // Walking under a mobile aim ends with it: attacks have no friction, so the
                    // stick's speed would otherwise carry through the rest of the move.
                    bool endsMobileAim = s.State == ActionState.Aiming && aim.AimState == AuthoringAimMode.None
                        && _slot.AimMovement == AuthoringAimMovementMode.Mobile;
                    s.IsAiming = aim.AimState != AuthoringAimMode.None;
                    s.State = s.IsAiming ? ActionState.Aiming : ActionState.Attacking;
                    if (endsMobileAim)
                    {
                        ClearVelocityOwnership(ref s);
                        s.VX = s.VZ = 0f;
                    }
                    break;
                case CookedStartCapabilityOperation capability:
                    StartCapability(ref s, def, capability, flattenedOperationIndex);
                    break;
                case CookedEmitPresentationOperation presentation:
                    var placement = presentation.Placement;
                    if (presentation.PresentationId == "presentation.manki.aerosol-inferno.start")
                        foreach (var activeCapability in _capabilities)
                            if (activeCapability is MankiAerosolInferno aerosol)
                                placement = aerosol.ScaleFlamePresentation(s.ChargeTicks, placement);
                    PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, presentation.OperationIndex, presentation.PresentationId, PresentationAttackSequence, PresentationEventSource.Timeline, s.PX, s.PY, s.PZ, s.FacingYaw)
                    {
                        Placement = placement,
                    });
                    break;
                case CookedCompleteTimelineOperation:
                    Complete(ref s);
                    return;
            }
        }
    }
    private void StartForwardLunge(ref CharacterState s, CookedForwardLungeOperation operation)
    {
        _forwardLungeSpeed = operation.Speed;
        _forwardLungeYaw = s.FacingYaw;
        _forwardLungeTicksRemaining = operation.DurationTicks;
        _forwardLungeActive = true;
        _lungeReach = Array.Empty<Hitbox>();
        if (operation.StopInAttackRange)
        {
            for (int i = _operationCursor; i < CurrentStage.Operations.Count; i++)
            {
                if (CurrentStage.Operations[i] is not CookedSpawnHitboxOperation hit) continue;
                var cooked = hit.Hitbox;
                var evt = CookedCharacterRuntimeAdapter.ToHitboxEvent(cooked, hit.Tick);
                // Cache the first swing's active poses relative to the lunge origin.
                _lungeReach = new Hitbox[Math.Max(1, (int)cooked.DurationTicks)];
                var pose = s;
                pose.PX = pose.PY = pose.PZ = 0f;
                // AttackElapsedTicks counts from the activation; operation ticks from their stage.
                int stageStart = 0;
                for (int stage = 0; stage < _stageIndex; stage++)
                    stageStart += _slot.Timeline.Stages[stage].DurationTicks;
                for (int tick = 0; tick < _lungeReach.Length; tick++)
                {
                    pose.AttackElapsedTicks = (ushort)(stageStart + hit.Tick + tick);
                    HitboxGeometry.ResolvePositions(pose, evt, BakedData, CharacterDef, _slot, AnimIndex, out float x, out float y, out float z, out float ex, out float ey, out float ez);
                    _lungeReach[tick] = new Hitbox
                    {
                        Shape = evt.Shape, Radius = evt.Radius,
                        X = x, Y = y, Z = z, EndX = ex, EndY = ey, EndZ = ez,
                    };
                }
                break;
            }
        }
        ApplyForwardLunge(ref s);
    }

    private void ApplyForwardLunge(ref CharacterState s)
    {
        if (!_forwardLungeActive)
            return;

        float sin = MathF.Sin(_forwardLungeYaw), cos = MathF.Cos(_forwardLungeYaw);
        if (_lungeReach.Length > 0 && OwnerSimulation!.LungeReachesOpponent(s, _lungeReach, sin, cos))
        {
            ClearVelocityOwnership(ref s);
            s.VX = s.VZ = 0f;
            _forwardLungeActive = false;
            return;
        }

        if (_forwardLungeTicksRemaining > 0)
        {
            ClearVelocityOwnership(ref s);
            s.VX = sin * _forwardLungeSpeed;
            s.VZ = cos * _forwardLungeSpeed;
            _forwardLungeTicksRemaining--;
        }
        else
        {
            ClearVelocityOwnership(ref s);
            s.VX = 0f;
            s.VZ = 0f;
            _forwardLungeActive = false;
        }
    }


    private void SpawnCookedHitbox(ref CharacterState s, CookedHitbox cooked)
        => SpawnHitbox(ref s, CookedCharacterRuntimeAdapter.ToHitboxEvent(cooked, _stageTick));

    private void StartCapability(ref CharacterState s, CharacterDefinition def, CookedStartCapabilityOperation operation, int flattenedOperationIndex)
    {
        if (!InternalCapabilityRegistry.TryCreate(operation.CapabilityId, operation.CapabilityVersion, operation.Parameters, out var capability))
            throw new InvalidOperationException($"Capability '{operation.CapabilityId}' version '{operation.CapabilityVersion}' is not admitted.");

        capability.Resolver = Resolver;
        capability.OwnerSimulation = OwnerSimulation;
        capability.ActivationInput = ActivationInput;
        capability.SimulationStates = SimulationStates;
        capability.BakedData = BakedData;
        capability.CharacterDef = CharacterDef;
        capability.Arena = Arena;
        capability.Slot = Slot;
        capability.Cooldown = Cooldown;
        capability.ActivationId = ActivationId;
        capability.PresentationAttackSequence = PresentationAttackSequence;
        capability.PresentationOperationIndex = flattenedOperationIndex;
        capability.AirborneAtStart = AirborneAtStart;
        capability.PresentationSink = PresentationSink;
        // Aim-hold capabilities own their hold/release lifecycle: freeze the stage
        // clock while they hold ActionState.Aiming so an authored stage timeout can
        // never cancel the aim, and resume (stage time reset) on their release.
        if (capability is IAimHoldCapability)
            _unlimitedAimHold = true;
        if (capability.OwnsVerticalMotion)
        {
            s.IsFastFalling = false;
            ClearVelocityOwnership(ref s);
        }
        _capabilities.Add(capability);
        byte previousClip = capability.AnimIndex;
        capability.OnStart(ref s, def);
        if (capability.AnimIndex != previousClip)
            AnimIndex = capability.AnimIndex;
        if (capability.OwnsVerticalMotion)
        {
            s.IsFastFalling = false;
            ClearVelocityOwnership(ref s);
        }
    }

    private void Complete(ref CharacterState s)
    {
        if (_completed)
            return;
        _completed = true;
        EndAbility(ref s);
    }

    private void CompleteCapabilities(ref CharacterState s, bool cancel)
    {
        for (var i = 0; i < _capabilities.Count; i++)
        {
            if (cancel)
                _capabilities[i].OnCancel(ref s);
            else
                _capabilities[i].OnEnd(ref s);
        }
        _capabilities.Clear();
    }

    /// <summary>The cooked slot this activation runs, by its definition ordinal.</summary>
    internal int SlotOrdinal => _slot.Ordinal;
    internal int RunningCapabilities => _capabilities.Count;

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_stageTick);
        w.WriteInt(_stageIndex);
        w.WriteInt(_operationCursor);
        w.WriteBool(_completed);
        w.WriteBool(_unlimitedAimHold);
        w.WriteFloat(_forwardLungeSpeed);
        w.WriteFloat(_forwardLungeYaw);
        w.WriteUShort(_forwardLungeTicksRemaining);
        w.WriteBool(_forwardLungeActive);
        w.WriteInt(_lungeReach.Length);
        foreach (var reach in _lungeReach)
            SnapshotCodecs.Write(w, reach, NoOwnerContent);
        w.WriteUShort(_gravityWindowTicksRemaining);
        w.WriteFloat(_gravityWindowScale);
        // Capabilities are recreated from the timeline operation that started them.
        w.WriteInt(_capabilities.Count);
        foreach (var capability in _capabilities)
        {
            w.WriteInt(capability.PresentationOperationIndex);
            capability.WriteSnapshot(w);
        }
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _stageTick = r.ReadUShort();
        _stageIndex = r.ReadInt();
        if (_stageIndex < 0 || _stageIndex >= _slot.Timeline.Stages.Count)
            throw SnapshotReader.Malformed("timeline stage");
        _operationCursor = r.ReadInt();
        if (_operationCursor < 0 || _operationCursor > CurrentStage.Operations.Count)
            throw SnapshotReader.Malformed("timeline operation cursor");
        _completed = r.ReadBool();
        _unlimitedAimHold = r.ReadBool();
        _forwardLungeSpeed = r.ReadFloat();
        _forwardLungeYaw = r.ReadFloat();
        _forwardLungeTicksRemaining = r.ReadUShort();
        _forwardLungeActive = r.ReadBool();
        int reach = r.ReadCount(100); // an encoded hitbox takes well over 100 bytes
        _lungeReach = reach == 0 ? Array.Empty<Hitbox>() : new Hitbox[reach];
        for (int i = 0; i < reach; i++)
            _lungeReach[i] = SnapshotCodecs.ReadHitbox(r, NoOwnerContent);
        _gravityWindowTicksRemaining = r.ReadUShort();
        _gravityWindowScale = r.ReadFloat();
        _capabilities.Clear();
        int capabilities = r.ReadCount(4);
        for (int i = 0; i < capabilities; i++)
        {
            int operationIndex = r.ReadInt();
            if (FindOperation(operationIndex) is not CookedStartCapabilityOperation operation ||
                !InternalCapabilityRegistry.TryCreate(operation.CapabilityId, operation.CapabilityVersion,
                    operation.Parameters, out var capability))
                throw SnapshotReader.Malformed("capability operation");
            capability.Resolver = Resolver;
            capability.OwnerSimulation = OwnerSimulation;
            capability.SimulationStates = SimulationStates;
            capability.BakedData = BakedData;
            capability.CharacterDef = CharacterDef;
            capability.Arena = Arena;
                capability.PresentationSink = PresentationSink;
            capability.ReadSnapshot(r);
            _capabilities.Add(capability);
        }
    }

    private CookedTimelineOperation? FindOperation(int flattenedIndex)
    {
        if (flattenedIndex < 0)
            return null;
        foreach (var stage in _slot.Timeline.Stages)
        {
            if (flattenedIndex < stage.Operations.Count)
                return stage.Operations[flattenedIndex];
            flattenedIndex -= stage.Operations.Count;
        }
        return null;
    }

    private static (CharacterDefinition? Def, BakedAnimationData? Baked) NoOwnerContent(ulong owner)
        => throw new InvalidOperationException("Lunge reach shapes carry no content references.");
}
