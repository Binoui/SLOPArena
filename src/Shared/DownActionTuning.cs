using System;

namespace SlopArena.Shared
{
    /// <summary>Reason a grounded Down request was admitted or rejected this tick.</summary>
    public enum DownActionAdmissionReason
    {
        Accepted,
        Released,
        Locked,
        MotionOwned,
        ActionAccepted,
        BelowThreshold,
    }

    /// <summary>Immutable tuning shared by authoritative and predicted down actions.</summary>
    public sealed class DownActionTuning
    {
        public const float GroundEntryRatio = 0.65f;
        public const float LandingEntryRatio = 0.25f;
        public const float EndRatio = 0.15f;
        public const float EntryCapRatio = 1.25f;
        public const float JumpCapRatio = 1.15f;
        public const float AttackEntryCapRatio = 1f;
        public const float AttackDecelerationRatio = 6f;
        public const ushort RunAttackCarryTicks = 8;
        public const float RunAttackEntryCapRatio = RunAttackCarryTicks * AttackDecelerationRatio * Simulation.TickDt;

        public float SlideDecelerationRatio { get; }
        public float CrouchLaunchMultiplier { get; }

        public DownActionTuning(float slideDecelerationRatio = 1f, float crouchLaunchMultiplier = 0.90f)
        {
            if (float.IsNaN(slideDecelerationRatio) || float.IsInfinity(slideDecelerationRatio)
                || slideDecelerationRatio <= 0f)
                throw new ArgumentOutOfRangeException(nameof(slideDecelerationRatio));
            if (float.IsNaN(crouchLaunchMultiplier) || float.IsInfinity(crouchLaunchMultiplier)
                || crouchLaunchMultiplier <= 0f || crouchLaunchMultiplier > 1f)
                throw new ArgumentOutOfRangeException(nameof(crouchLaunchMultiplier));
            SlideDecelerationRatio = slideDecelerationRatio;
            CrouchLaunchMultiplier = crouchLaunchMultiplier;
        }

        public static readonly DownActionTuning Default = new();
    }
}
