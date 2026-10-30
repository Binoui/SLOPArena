using System;

namespace SlopArena.Shared.AI;

public sealed partial class HeuristicBotPolicy
{
    internal bool TryChooseMove(in CpuMoveRequest request, in CpuFacts facts, out CpuMoveChoice choice)
    {
        EnsureProfile(facts.Definition, facts.Baked);
        if (!facts.Self.IsGrounded && HasArenaBounds(facts.Arena) && !HasLandingBelow(facts.Self, facts.Arena))
        { choice = default; return false; }
        bool safeCoast = !facts.Self.IsGrounded || HasSafeCoast(facts.Self, facts.Arena);
        var candidates = facts.Self.IsGrounded ? _profile : _airProfile;
        Span<byte> ties = stackalloc byte[7];
        Span<CpuTargetPoint> points = stackalloc CpuTargetPoint[7];
        Span<float> scores = stackalloc float[7];
        int count = 0, bestPriority = int.MaxValue, bestTime = int.MaxValue;
        float best = float.NegativeInfinity;
        bool allowRecovery = !IsNearEdge(facts.Self, facts.Arena);
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (!safeCoast && candidate.ForwardTravel <= 0f) continue;
            if (!IsCandidateAvailable(facts.Self, candidate, !facts.Self.IsGrounded, allowRecovery, facts.Arena, facts.ActiveMove)
                || !candidate.HasDamage) continue;
            var point = CpuTargetPrediction.AtActivation(facts.Self, facts.Target, facts.Arena, candidate.AimTiming,
                facts.OwnWaitTicks + request.Execution.HesitationTicks);
            float distance = point.DistanceFrom(facts.Self);
            if ((candidate.Reach + VictimRadiusMargin) * request.Execution.RangeScale < distance * SlotReachTolerance
                || !request.Fits(point.ContactTicks, facts.Tick)) continue;
            float dy = point.Y - facts.Self.PY;
            float half = facts.Definition.CapsuleHeight * 0.5f;
            if (candidate.VerticalKnown && (dy - half > candidate.HighReach || dy + half < candidate.LowReach)) continue;
            if (request.Purpose == CpuMovePurpose.AntiAir && !candidate.VerticalKnown) continue;
            int priority = KitPriority(candidate, facts, point);
            if (request.Purpose == CpuMovePurpose.OpenShield && !(candidate.Finisher != null && priority == 1
                || candidate.CommandGrab != null && priority == 2)) continue;
            float reachError = facts.Definition.AlternateForm != null || candidate.AimTiming.TravelSpeed > 0f
                ? MathF.Max(0f, distance - candidate.Reach) : MathF.Abs(candidate.Reach - distance);
            float score = candidate.Damage * 0.1f - Math.Min(candidate.CommitmentTicks, 60) * 0.015f
                - reachError * 0.2f - candidate.StartupTicks * 0.05f
                + (IsNormalSlot(candidate.Slot) ? 0.6f : 0f);
            if (request.Purpose == CpuMovePurpose.AntiAir) score -= point.ContactTicks;
            if (request.Purpose == CpuMovePurpose.Kill) score += candidate.LaunchStrength * (1f + facts.Target.DamagePercent * 0.01f);
            bool punish = request.Purpose == CpuMovePurpose.FastestPunish;
            points[i] = point;
            scores[i] = score;
            bool better = punish ? point.ContactTicks < bestTime || point.ContactTicks == bestTime && priority < bestPriority
                : priority < bestPriority;
            if (better) { count = 0; best = score; bestPriority = priority; bestTime = point.ContactTicks; }
            if (priority != bestPriority || punish && point.ContactTicks != bestTime) continue;
            if (score > best)
            {
                best = score;
                int kept = 0;
                for (int t = 0; t < count; t++)
                    if (scores[ties[t]] >= best - 2f) ties[kept++] = ties[t];
                count = kept;
            }
            if (score < best - 2f) continue;
            ties[count++] = (byte)i;
        }
        if (count > 0)
        {
            int index = ties[(int)(request.Execution.TieBreak % (uint)count)];
            var candidate = candidates[index];
            var point = points[index];
            float distance = point.DistanceFrom(facts.Self);
            CanonicalSlotProjection.TryGet(candidate.Slot, !facts.Self.IsGrounded, out var slot);
            choice = new CpuMoveChoice(CpuActionKind.Slot, slot, point, candidate.RequiresAim,
                candidate.CommitmentTicks, distance > 0.001f ? AimYaw(point.X - facts.Self.PX, point.Z - facts.Self.PZ)
                    : AimYaw(facts.Target.PX - facts.Self.PX, facts.Target.PZ - facts.Self.PZ),
                AimPitch(point.Y - facts.Self.PY, distance), AimDistance(distance), bestPriority < 4);
            return true;
        }
        if (safeCoast && request.Purpose == CpuMovePurpose.OpenShield && facts.Target.State == ActionState.Shielding
            && facts.Target.IsGrounded && facts.Self.IsGrounded && facts.Self.BlockStunTicks == 0
            && facts.Definition.CaptureGeometry != null)
        {
            var point = CpuTargetPrediction.AtActivation(facts.Self, facts.Target, facts.Arena,
                new CpuMoveTiming(DefenseConfig.GrabStartupTicks), facts.OwnWaitTicks + request.Execution.HesitationTicks);
            if (InCaptureVolume(facts.Self, point, facts.Definition))
            {
                choice = new CpuMoveChoice(CpuActionKind.Grab, default, point, false,
                    DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks + DefenseConfig.GrabWhiffRecoveryTicks,
                    AimYaw(point.X - facts.Self.PX, point.Z - facts.Self.PZ), 0, 0);
                return true;
            }
        }
        choice = default;
        return false;
    }

    private static int KitPriority(in MoveCandidate candidate, in CpuFacts facts, in CpuTargetPoint point)
    {
        var self = facts.Self; var target = facts.Target;
        float dx = point.X - self.PX, dy = point.Y - self.PY, dz = point.Z - self.PZ;
        if (candidate.DashStrike is { } dash && target.State != ActionState.Shielding
            && DashStrikeReach.Classify(dash, self, new CharacterState { PX = point.X, PY = point.Y, PZ = point.Z }, facts.Definition) == DashStrikeContact.Tip) return 0;
        if (candidate.Flash is { } flash && MathF.Sqrt(dx * dx + dy * dy + dz * dz) <= flash.LockRange) return 1;
        if (candidate.Finisher is { } finisher && (target.State == ActionState.Shielding || target.DamagePercent >= FinisherKillPercent)
            && point.DistanceFrom(self) <= finisher.LungeSpeed * finisher.LungeTicks / 60f + finisher.Capture.OffsetZ + finisher.Capture.Radius
            && MathF.Abs(dy) <= finisher.Capture.Radius + 0.5f) return 1;
        if (candidate.CommandGrab != null && target.State == ActionState.Shielding) return 2;
        return 4;
    }

    private static (bool Known, float Low, float High, float Launch) MoveExtent(CookedSlotDefinition? slot)
    {
        bool known = false; float low = float.PositiveInfinity, high = float.NegativeInfinity, launch = 0f;
        if (slot == null) return default;
        foreach (var stage in slot.Timeline.Stages)
        foreach (var operation in stage.Operations)
            if (operation is CookedSpawnHitboxOperation spawn)
            {
                var hit = spawn.Hitbox;
                launch = MathF.Max(launch, hit.BaseKnockback + hit.KnockbackGrowth);
                if (hit.StartBoneId != null) continue;
                known = true;
                low = MathF.Min(low, MathF.Min(hit.OffsetY, hit.EndOffsetY) - hit.Radius);
                high = MathF.Max(high, MathF.Max(hit.OffsetY, hit.EndOffsetY) + hit.Radius);
            }
        return (known, low, high, launch);
    }
}
