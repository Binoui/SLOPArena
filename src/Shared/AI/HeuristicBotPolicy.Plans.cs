using System;

namespace SlopArena.Shared.AI;

public sealed partial class HeuristicBotPolicy
{
    private static bool Admits(in CharacterState self, CookedSlotDefinition? active)
        => ActionStates.AdmitsActions(self.State) || self.State == ActionState.HomingDashArrive
            || Simulation.IsIasaUnlocked(self, active);

    private static int OwnWait(in CharacterState self, CookedSlotDefinition? active)
    {
        int wait = Math.Max(self.LandingLagTicks, Math.Max(self.BlockStunTicks, self.HitstunTicks));
        if (self.State == ActionState.Shielding) wait = Math.Max(wait, DefenseConfig.ShieldDropTicks);
        if (self.State == ActionState.ShieldDrop) wait = Math.Max(wait, self.StateTicks);
        if (self.AnimLockTicks > 0 && !Simulation.IsIasaUnlocked(self, active))
        {
            int attackWait = self.AnimLockTicks;
            if (self.State == ActionState.Attacking && active != null)
            {
                int stage = Math.Min(self.ComboStage, active.Timeline.Stages.Count - 1), offset = 0;
                for (int i = 0; i < stage; i++) offset += active.Timeline.Stages[i].DurationTicks;
                var current = active.Timeline.Stages[stage];
                attackWait = Math.Min(attackWait, Math.Max(0, offset
                    + (current.IasaTicks > 0 ? current.IasaTicks : current.DurationTicks) - self.AttackElapsedTicks));
            }
            wait = Math.Max(wait, attackWait);
        }
        return self.HitstopTicks + wait;
    }

    private static CpuExecutionBias Sample(in BotDifficultyProfile profile, Random rng)
        => new((ushort)(profile.HesitationMin + rng.Next(profile.HesitationMax - profile.HesitationMin + 1)),
            1f + ((float)rng.NextDouble() * 2f - 1f) * profile.RangeError,
            rng.Next(2) == 0 ? (sbyte)-1 : (sbyte)1, (uint)rng.Next());

    private static CpuExecutionBias Immediate(in CpuExecutionBias bias)
        => new(0, bias.RangeScale, bias.Side, bias.TieBreak);

    private InputState Avoid(in CpuFacts f, Random rng, BotMemory memory, out Footstep step)
    {
        step = default;
        if (f.OwnWaitTicks > 0 || !Admits(f.Self, f.ActiveMove)) return default;
        float dx = f.Self.PX - f.Target.PX, dz = f.Self.PZ - f.Target.PZ, d = Distance(dx, dz);
        if (d < MathF.Max(5f, f.Definition.CapsuleRadius * 6f))
            step = d > .001f ? new Footstep(dx / d, dz / d) : new Footstep(0f, -1f);
        if (!f.Self.IsGrounded && f.Self.AirDodgesLeft > 0 && NewThreat(f, memory))
        {
            memory.EvaluatedThreat = f.Target.Timing.AttackEpisode; memory.ThreatEvaluations++;
            if (rng.NextDouble() < BotDifficultyProfile.ForDifficulty(memory.Difficulty).ThreatRecognition)
                return new InputState { ShieldPressed = true };
        }
        return default;
    }

    private static bool NewThreat(in CpuFacts f, BotMemory memory)
        => f.Target.Timing.AttackEpisode != 0 && f.Target.Timing.AttackEpisode != memory.EvaluatedThreat
            && f.Target.Timing.Phase != CpuAttackPhase.Recovery && Threatens(f.Self, f.Target);

    private InputState ContinueCombatPlan(in CpuFacts f, Random rng, BotMemory memory, out Footstep step)
    {
        step = default;
        if (f.Self.HitstunTicks > 0 || f.Self.HitstopTicks > 0) return default;
        bool offstage = IsOffstage(f.Target, f.Arena);
        if (f.Self.State == ActionState.Shielding && !Threatens(f.Self, f.Target))
            return default;
        var profile = BotDifficultyProfile.ForDifficulty(memory.Difficulty);
        if (offstage && memory.Combat.Kind != CpuCombatPlanKind.Edgeguard)
        {
            memory.EndCombat(CpuPlanEnd.Interrupted);
            memory.BeginCombat(CpuCombatPlan.Edgeguard(f, Sample(profile, rng), memory.OpponentId));
        }
        if (!offstage && NewThreat(f, memory))
        {
            memory.EvaluatedThreat = f.Target.Timing.AttackEpisode; memory.ThreatEvaluations++;
            if (rng.NextDouble() < profile.ThreatRecognition)
            {
                memory.EndCombat(CpuPlanEnd.Interrupted);
                memory.BeginCombat(CpuCombatPlan.Defend(f.Target.Timing.AttackEpisode, f, Sample(profile, rng), memory.OpponentId));
            }
        }
        if (!offstage && memory.Combat.Phase is not (CpuCombatPhase.Executing or CpuCombatPhase.Result)
            && memory.Combat.Kind != CpuCombatPlanKind.Punish && !memory.TryGetNewContact(out _)
            && f.Target.Timing.RecoveryEpisode != 0 && f.Target.Timing.RecoveryEpisode != memory.EvaluatedRecovery)
        {
            memory.EvaluatedRecovery = f.Target.Timing.RecoveryEpisode; memory.RecoveryEvaluations++;
            if (rng.NextDouble() < profile.PunishRecognition
                && RecoveryOpportunity.TryFromDelayed(f.Target, f.Tick, out var opening))
            {
                var execution = Sample(profile, rng);
                if (TryChooseMove(CpuMoveRequest.Punish(opening, execution, profile.PunishMargin), f, out var punish))
                {
                    memory.EndCombat(CpuPlanEnd.Interrupted);
                    memory.BeginCombat(CpuCombatPlan.Punish(opening, punish, f, execution, memory.OpponentId));
                }
            }
        }
        if (!offstage && memory.TryGetNewContact(out var contact))
        {
            memory.MarkContactEvaluated(contact); memory.ContactEvaluations++;
            if (memory.Combat.Kind == CpuCombatPlanKind.Pressure)
            {
                // Contacts inside a string advance its evidence, never replenish its budget.
                bool recognized = contact.Kind == CpuContactKind.Block || rng.NextDouble() < profile.ConfirmReliability;
                if (memory.Combat.Remaining > 0)
                {
                    if (!recognized) memory.EndCombat(CpuPlanEnd.Completed, true);
                    else memory.Combat.NextFollowUp(contact, f.Tick);
                }
            }
            else if (memory.Combat.Kind != CpuCombatPlanKind.Defend)
            {
                bool confirmed = contact.Kind == CpuContactKind.Block || rng.NextDouble() < profile.ConfirmReliability;
                memory.EndCombat(CpuPlanEnd.Completed, space: !confirmed);
                if (confirmed) memory.BeginCombat(CpuCombatPlan.Pressure(contact, profile.PressureBudget,
                    f, Sample(profile, rng), memory.OpponentId));
            }
        }
        if (memory.Combat.Kind == CpuCombatPlanKind.None && f.OwnWaitTicks == 0 && Admits(f.Self, f.ActiveMove)
            && TrySwitchForm(f, Distance(f.Target.PX - f.Self.PX, f.Target.PZ - f.Self.PZ), out var swap)) return swap;
        if (memory.Combat.Kind == CpuCombatPlanKind.None)
            SelectCombatPlan(f, rng, memory, offstage);
        ref var plan = ref memory.Combat;
        if (plan.Opponent != memory.OpponentId)
        { memory.ForgetCombat(); return default; }
        if (f.Tick >= plan.Expires)
        { memory.EndCombat(CpuPlanEnd.Expired, space: plan.Kind != CpuCombatPlanKind.Space); return default; }
        var point = CpuTargetPrediction.AtActivation(f.Self, f.Target, f.Arena, EngageTiming(f.Self));
        float dx = point.X - f.Self.PX, dz = point.Z - f.Self.PZ, dist = Distance(dx, dz);
        var input = new InputState { FaceToCamera = true, AimYaw = AimYaw(dx, dz) };
        bool canAct = f.OwnWaitTicks == 0 && Admits(f.Self, f.ActiveMove);
        if (!canAct && f.Self.State != ActionState.Shielding) input = default;
        if (plan.Phase == CpuCombatPhase.Result)
        {
            if (!canAct) return input;
            if (plan.Kind == CpuCombatPlanKind.Pressure)
            {
                if (plan.Remaining == 0) { memory.EndCombat(CpuPlanEnd.Completed, true); return input; }
                memory.EndCombat(CpuPlanEnd.Whiff, true); return input;
            }
            if (plan.RangedAction && plan.Kind == CpuCombatPlanKind.Approach)
            { plan.ResumeClosing(); plan.Progress(f.Self, f.Tick); }
            else
            { memory.EndCombat(CpuPlanEnd.Completed, space: true); return input; }
        }
        switch (plan.Kind)
        {
            case CpuCombatPlanKind.Defend:
                if (f.Target.Timing.AttackEpisode != plan.Threat || f.Target.Timing.Phase == CpuAttackPhase.Recovery
                    || !Threatens(f.Self, f.Target))
                { memory.EndCombat(CpuPlanEnd.Completed); return input; }
                if (f.Self.IsGrounded)
                {
                    if (canAct || f.Self.State == ActionState.Shielding || f.Self.BlockStunTicks > 0)
                    { input.ShieldHeld = true; input.ShieldPressed = !plan.DodgeIssued && f.Self.State != ActionState.Shielding; plan.Dodge(); }
                }
                else if (canAct)
                {
                    if (!plan.DodgeIssued && f.Self.AirDodgesLeft > 0) { input.ShieldPressed = true; plan.Dodge(); }
                    else if (dist > .001f) step = new Footstep(-dx / dist, -dz / dist);
                }
                return input;
            case CpuCombatPlanKind.Punish:
                if (plan.Opening.Episode != f.Target.Timing.RecoveryEpisode || f.Tick >= plan.Opening.Deadline)
                { memory.EndCombat(CpuPlanEnd.Expired, true); return input; }
                int hesitation = Math.Max(0, plan.ReadyAt - f.Tick - f.OwnWaitTicks);
                if (f.Tick + f.OwnWaitTicks + hesitation + plan.ContactLead + profile.PunishMargin >= plan.Opening.Deadline)
                { memory.EndCombat(CpuPlanEnd.Expired, true); return input; }
                if (canAct && hesitation == 0)
                {
                    if (plan.Started == f.Tick && CanIssue(plan.Pending, f, plan.Execution.RangeScale))
                        Issue(plan.Pending, f, memory, ref input);
                    else if (TryChooseMove(CpuMoveRequest.Punish(plan.Opening, Immediate(plan.Execution), profile.PunishMargin), f, out var fastest)
                        && fastest.WireSlot == plan.Pending.WireSlot) Issue(fastest, f, memory, ref input);
                    else memory.EndCombat(CpuPlanEnd.Expired, true);
                }
                return input;
            case CpuCombatPlanKind.Pressure:
                if (plan.Remaining == 0)
                { if (canAct) memory.EndCombat(CpuPlanEnd.Completed, true); return input; }
                if (!canAct || f.Tick < plan.ReadyAt) return input;
                var bias = Immediate(plan.Execution);
                var hitFacts = new CpuFacts(f.Self, memory.ContactObservation(plan.Contact), f.Definition, f.Arena,
                    f.ActiveMove, f.Tick, baked: f.Baked);
                CpuMoveChoice follow;
                if (!plan.PressureOnly && CpuHitConfirm.TryFromDelayed(plan.Contact, f.Tick, out var confirm))
                {
                    if (TryChooseMove(CpuMoveRequest.ComboFollowUp(confirm, bias), hitFacts, out follow))
                        Issue(follow, f, memory, ref input);
                    else plan.TryPressure();
                    return input;
                }
                var request = f.Target.State == ActionState.Shielding ? CpuMoveRequest.OpenShield(bias) : CpuMoveRequest.Pressure(bias);
                if (!TryChooseMove(request, f, out follow))
                {
                    if (f.Target.State == ActionState.Shielding && dist > .001f)
                    { step = new Footstep(dx / dist, dz / dist, dist, point.Y - f.Self.PY); return input; }
                    memory.EndCombat(CpuPlanEnd.Escaped, true); return input;
                }
                Issue(follow, f, memory, ref input); return input;
            case CpuCombatPlanKind.Space:
                float threatReach = float.IsPositiveInfinity(f.Target.ThreatReach) ? NormalReach(f.Self) : f.Target.ThreatReach;
                float band = MathF.Max(.5f, threatReach) + plan.Margin + f.Definition.CapsuleRadius;
                if (dist > band + 2f * f.Definition.CapsuleRadius)
                { memory.EndCombat(CpuPlanEnd.Completed); return input; }
                if (plan.Phase == CpuCombatPhase.Settle)
                {
                    if (dist < band && dist > .001f && canAct) step = new Footstep(-dx / dist, -dz / dist);
                    else plan.Settle(f.Tick);
                }
                else if (f.Tick >= plan.ReadyAt)
                { memory.EndCombat(CpuPlanEnd.Completed); return input; }
                else if (canAct && dist < band - f.Definition.CapsuleRadius && dist > .001f)
                    step = new Footstep(-dx / dist, -dz / dist);
                return input;
            case CpuCombatPlanKind.Edgeguard:
                if (!offstage) { memory.EndCombat(CpuPlanEnd.Completed); return input; }
                if (canAct && !plan.SpacingShot && f.Tick >= plan.ReadyAt)
                {
                    if (TryChooseMove(CpuMoveRequest.Poke(Immediate(plan.Execution)), f, out var safe)) Issue(safe, f, memory, ref input);
                    else plan.WatchAfterAction(f.Tick);
                }
                return input;
        }
        if (plan.Kind != CpuCombatPlanKind.Approach || !canAct && plan.Phase is not (CpuCombatPhase.DashTravel or CpuCombatPhase.JumpHold)) return input;
        if (plan.Phase == CpuCombatPhase.DashTravel)
        {
            if (f.Self.State == ActionState.HomingDashArrive) plan.Arrived();
            else if (f.Self.State == ActionState.HomingDashMiss)
            { memory.EndCombat(CpuPlanEnd.Whiff, true); return input; }
            else if (f.Tick > plan.Issued && f.Self.State is not (ActionState.HomingDash or ActionState.HomingDashFloat))
            { memory.EndCombat(CpuPlanEnd.Rejected, true); return input; }
            else return input;
        }
        if (plan.Phase == CpuCombatPhase.JumpHold)
        {
            if (f.Self.IsGrounded && f.Tick > plan.Issued + 2)
            { memory.EndCombat(CpuPlanEnd.Rejected, true); return input; }
            input.JumpHeld = f.Tick - plan.Issued < ((plan.Execution.TieBreak & 1) == 0 ? 12 : 5);
            if (!f.Self.IsGrounded && !input.JumpHeld) plan.Airborne();
            if (dist > .001f) step = new Footstep(dx / dist, dz / dist);
            return input;
        }
        if (TrySwitchForm(f, dist, out var formInput)) return formInput;
        var actionBias = Immediate(plan.Execution);
        bool shielding = f.Target.State == ActionState.Shielding && f.Target.IsGrounded;
        if (plan.Pending.Kind != CpuActionKind.None)
        {
            if (plan.Pending.KitResponse || plan.Approach == CpuApproachKind.WalkInPoke)
            {
                if (f.Tick < plan.ReadyAt && !plan.Pending.KitResponse) return input;
                var pending = plan.Pending;
                if (CanIssue(pending, f, plan.Execution.RangeScale))
                {
                    if (!pending.KitResponse && point.DistanceFrom(f.Self) > NormalReach(f.Self)) plan.UseRanged();
                    Issue(pending, f, memory, ref input); return input;
                }
            }
            plan.DiscardPending();
        }
        if (plan.Phase == CpuCombatPhase.DashPress && f.Tick < plan.ReadyAt) return input;
        if (plan.Phase == CpuCombatPhase.DashPress)
        {
            if (!SafeApproach(CpuApproachKind.Beeline, f, memory.OpponentId, dist, dx, dz))
            { memory.EndCombat(CpuPlanEnd.Blocked, true); return input; }
            input.DashPressed = true; plan.Dash(f.Tick); return input;
        }
        if (plan.Approach == CpuApproachKind.JumpIn && plan.Phase == CpuCombatPhase.Closing && f.Tick >= plan.ReadyAt)
        {
            if (!SafeApproach(CpuApproachKind.JumpIn, f, memory.OpponentId, dist, dx, dz))
            { memory.EndCombat(CpuPlanEnd.Blocked, true); return input; }
            input.Jump = input.JumpHeld = true; plan.Jump(f.Tick);
            if (dist > .001f) step = new Footstep(dx / dist, dz / dist);
            return input;
        }
        float reach = shielding && f.Definition.CaptureGeometry != null
            ? f.Definition.CaptureGeometry.Reach + f.Definition.CapsuleRadius : NormalReach(f.Self);
        if (dist <= reach) memory.Engaged = true;
        else if (dist > reach + f.Definition.CapsuleRadius) memory.Engaged = false;
        if (plan.Phase == CpuCombatPhase.AirStrike && f.Self.IsGrounded)
        { memory.EndCombat(CpuPlanEnd.Whiff, true); return input; }
        bool atBoundary = memory.Engaged || plan.Phase is CpuCombatPhase.Arrival or CpuCombatPhase.AirStrike
            || !plan.RangedChecked && f.Tick >= plan.ReadyAt;
        if (atBoundary && plan.Started != f.Tick && f.Tick >= plan.ReadyAt && TryChooseMove(shielding ? CpuMoveRequest.OpenShield(actionBias)
            : plan.Phase is CpuCombatPhase.Arrival or CpuCombatPhase.AirStrike ? CpuMoveRequest.Approach(actionBias)
            : f.Target.DamagePercent >= 100 ? CpuMoveRequest.Kill(actionBias)
            : !f.Target.IsGrounded ? CpuMoveRequest.AntiAir(actionBias) : CpuMoveRequest.Poke(actionBias), f, out var poke))
        {
            bool distantSpecial = !memory.Engaged && IsNormalSlot(poke.WireSlot) == false;
            if (!distantSpecial || !plan.RangedUsed)
            {
                if (distantSpecial) plan.UseRanged();
                Issue(poke, f, memory, ref input); return input;
            }
        }
        if (atBoundary && f.Tick >= plan.ReadyAt && !memory.Engaged) plan.CheckRanged();
        if (plan.Phase == CpuCombatPhase.Arrival)
        { memory.EndCombat(CpuPlanEnd.Whiff, true); return input; }

        if (Distance(f.Self.PX - plan.ProgressX, f.Self.PZ - plan.ProgressZ) >= .25f) plan.Progress(f.Self, f.Tick);
        else if (f.Tick - plan.ProgressAt >= 30)
        { memory.EndCombat(CpuPlanEnd.Blocked, true); return input; }
        if ((!memory.Engaged || shielding) && dist > .001f) step = new Footstep(dx / dist, dz / dist, dist, point.Y - f.Self.PY);
        else if (memory.Engaged && f.Tick >= plan.ReadyAt) memory.EndCombat(CpuPlanEnd.Whiff, true);
        return input;
    }

    private bool CanIssue(in CpuMoveChoice choice, in CpuFacts f, float rangeScale)
    {
        if (choice.Kind == CpuActionKind.Grab)
            return f.Self.IsGrounded && f.Self.BlockStunTicks == 0 && HasSafeCoast(f.Self, f.Arena) && InCaptureVolume(f.Self, choice.Point, f.Definition);
        if (choice.Slot.IsAirborne != !f.Self.IsGrounded || !CanPress(f.Self, f.Definition, choice.WireSlot, f.ActiveMove)) return false;
        var candidate = (f.Self.IsGrounded ? _profile : _airProfile)[Array.IndexOf(Slots, choice.WireSlot)];
        return choice.Point.DistanceFrom(f.Self) * SlotReachTolerance <= (candidate.Reach + VictimRadiusMargin) * rangeScale
            && HasSafeAttackTravel(f.Self, candidate, f.Arena);
    }

    private void Issue(in CpuMoveChoice move, in CpuFacts f, BotMemory memory, ref InputState input)
    {
        memory.Combat.BeginAction(f.Tick);
        StartAttack(ref input, move, f.Self, f.Target, f.Arena, memory);
        input.FaceToCamera = true;
    }

    private float NormalReach(in CharacterState self)
        => self.IsGrounded ? _currentProfile!.GroundReach : _currentProfile!.AirReach;

    private bool TrySwitchForm(in CpuFacts f, float distance, out InputState input)
    {
        input = default;
        if (f.Definition.AlternateForm == null || !CanPress(f.Self, f.Definition, AbilitySlots.F, f.ActiveMove)) return false;
        var close = FindProfile(f.Definition.ForForm(1), f.Baked);
        float reach = 0f;
        foreach (var move in f.Self.IsGrounded ? close.Ground : close.Air)
            if (move.Functional && IsNormalSlot(move.Slot)) reach = MathF.Max(reach, move.Reach + VictimRadiusMargin);
        byte desired = distance <= reach ? (byte)1 : (byte)0;
        if (desired == f.Self.Form.Active) return false;
        input.ActiveSlot = AbilitySlots.F; return true;
    }

    private void SelectCombatPlan(in CpuFacts f, Random rng, BotMemory memory, bool offstage)
    {
        var profile = BotDifficultyProfile.ForDifficulty(memory.Difficulty);
        var bias = Sample(profile, rng);
        if (offstage) { memory.BeginCombat(CpuCombatPlan.Edgeguard(f, bias, memory.OpponentId)); return; }
        var entryRequest = f.Target.State == ActionState.Shielding ? CpuMoveRequest.OpenShield(bias)
            : f.Target.DamagePercent >= 100 ? CpuMoveRequest.Kill(bias) : CpuMoveRequest.Poke(bias);
        TryChooseMove(entryRequest, f, out var entry);
        if (entry.KitResponse)
        {
            var ready = CpuCombatPlan.ApproachPlan(CpuApproachKind.WalkInPoke, f, bias, memory.OpponentId);
            ready.RememberEntry(entry); memory.BeginCombat(ready); return;
        }
        if (memory.SpaceNext || f.Self.DamagePercent >= 70 && memory.LastEnd != CpuPlanEnd.Completed)
        { memory.SpaceNext = false; memory.BeginCombat(CpuCombatPlan.Space(f, bias, memory.OpponentId)); return; }
        float dx = f.Target.PX - f.Self.PX, dz = f.Target.PZ - f.Self.PZ, distance = Distance(dx, dz);
        var variant = CpuApproachKind.WalkInPoke;
        if (distance > NormalReach(f.Self) + f.Definition.CapsuleRadius)
        {
            if (memory.WalkBag + memory.JumpBag + memory.DashBag == 0)
            {
                memory.WalkBag = memory.Difficulty == CpuDifficulty.Easy ? (byte)8 : memory.Difficulty == CpuDifficulty.Normal ? (byte)4 : (byte)3;
                memory.JumpBag = memory.Difficulty == CpuDifficulty.Easy ? (byte)1 : (byte)3;
                memory.DashBag = memory.Difficulty == CpuDifficulty.Hard ? (byte)4 : memory.Difficulty == CpuDifficulty.Easy ? (byte)1 : (byte)3;
            }
            bool jump = SafeApproach(CpuApproachKind.JumpIn, f, memory.OpponentId, distance, dx, dz);
            bool dash = SafeApproach(CpuApproachKind.Beeline, f, memory.OpponentId, distance, dx, dz);
            int walkWeight = memory.WalkBag * 2, jumpWeight = jump ? memory.JumpBag * 2 : 0,
                dashWeight = dash ? memory.DashBag * 2 : 0;
            if (memory.Difficulty != CpuDifficulty.Easy && memory.LastApproach.HasValue)
            {
                if (memory.LastApproach == CpuApproachKind.WalkInPoke) walkWeight = memory.WalkBag;
                else if (memory.LastApproach == CpuApproachKind.JumpIn) jumpWeight /= 2;
                else dashWeight /= 2;
            }
            int total = walkWeight + jumpWeight + dashWeight;
            if (total > 0)
            {
                int pick = (int)(bias.TieBreak % (uint)total);
                if (pick < walkWeight) memory.WalkBag--;
                else if (jump && pick < walkWeight + jumpWeight) { variant = CpuApproachKind.JumpIn; memory.JumpBag--; }
                else { variant = CpuApproachKind.Beeline; memory.DashBag--; }
            }
        }
        else if (bias.TieBreak % 5 >= 3 && f.Target.State != ActionState.Shielding)
        { memory.BeginCombat(CpuCombatPlan.Space(f, bias, memory.OpponentId)); return; }
        memory.LastApproach = variant;
        var plan = CpuCombatPlan.ApproachPlan(variant, f, bias, memory.OpponentId);
        plan.RememberEntry(entry);
        memory.BeginCombat(plan);
    }

    private bool HasTakeoffMargin(in CharacterState self, in ArenaDefinition arena)
    {
        // Shared homes on a live target after delayed admission; allow room for that turn.
        for (int direction = 0; direction < 4; direction++)
        {
            var point = self;
            point.PX += direction == 0 ? 2f * NearEdgeDistance : direction == 1 ? -2f * NearEdgeDistance : 0f;
            point.PZ += direction == 2 ? 2f * NearEdgeDistance : direction == 3 ? -2f * NearEdgeDistance : 0f;
            if (!HasLandingBelow(point, arena)) return false;
        }
        return true;
    }

    private bool SafeApproach(CpuApproachKind variant, in CpuFacts f, ulong opponent, float distance, float dx, float dz)
    {
        if (distance <= .001f || !f.Self.IsGrounded && variant == CpuApproachKind.JumpIn) return false;
        if (variant == CpuApproachKind.Beeline && (opponent == 0 || opponent > byte.MaxValue || f.Self.HomingDashCooldownTicks > 0
            || !f.Self.IsGrounded && f.Self.HomingDashAirUsed || f.Self.InteractionId != 0 || Simulation.HasKnockback(f.Self)
            || distance > HomingDashConfig.Speed * HomingDashConfig.MaxTravelTicks / 60f)) return false;
        if (variant == CpuApproachKind.Beeline)
        {
            if (!HasTakeoffMargin(f.Self, f.Arena)) return false;
            var point = CpuTargetPrediction.AtActivation(f.Self, f.Target, f.Arena,
                new CpuMoveTiming(HomingDashConfig.FloatTicks, HomingDashConfig.MaxTravelTicks,
                    HomingDashConfig.Speed, HomingDashConfig.ArrivalGap), f.OwnWaitTicks,
                horizonCap: 90, leadCap: HomingDashConfig.Speed * HomingDashConfig.MaxTravelTicks / 60f);
            var arrival = f.Self with { PX = point.X, PY = point.Y, PZ = point.Z };
            if (!HasTakeoffMargin(arrival, f.Arena)) return false;
        }
        float envelope = variant == CpuApproachKind.Beeline ? HomingDashConfig.Speed * HomingDashConfig.MaxTravelTicks / 60f
            : f.Definition.Movement.AirSpeedMax * f.Definition.Movement.JumpForce * 2f / MathF.Max(1f, f.Definition.Movement.Gravity);
        if (variant == CpuApproachKind.JumpIn && (f.Self.JumpsLeft == 0 || distance > envelope || distance < NormalReach(f.Self))) return false;
        return HasSafeAttackTravel(f.Self, default, f.Arena, dx / distance * envelope, dz / distance * envelope);
    }
}
