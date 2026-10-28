using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// The shell stance (ADR-0045). "In the shell" is derived, never stored: the fighter is in
    /// <see cref="ActionState.Shell"/>, or attacking in a move whose slot tucks. The skate's clock is
    /// <see cref="CharacterState.StateTicks"/>, its speed the fighter's velocity and its heading her facing.
    /// </summary>
    public static class Shell
    {
        /// <summary>A wall that would keep less than this share of the skate's forward speed stops it.</summary>
        internal const float BlockedTravelRatio = 0.6f;

        /// <summary>The one definition of "in the shell", for gameplay, the hurtbox pose and the Unity tuck.</summary>
        public static bool IsTucked(ActionState state, CookedSlotDefinition? activeMove)
            => state == ActionState.Shell
                || state == ActionState.Attacking && activeMove?.ShellPolicy == ShellPolicy.Tuck;

        /// <summary>
        /// Applies a starting move's shell policy to her velocity. Returns true when the shell decided it, so the
        /// activation skips its Run and Slide carry and its grounded stop. A tuck move keeps her momentum; a
        /// grounded move out of the shell starts at skate speed and brakes at the pop-out rate.
        /// </summary>
        internal static bool StartMove(ref CharacterState s, CookedSlotDefinition? move, bool fromShell)
        {
            if (move?.ShellPolicy == ShellPolicy.Tuck)
            {
                s.SlideAttackCarryActive = false;
                return true;
            }
            if (!fromShell || !s.IsGrounded) return false;
            s.SlideAttackCarryActive = true;
            s.AttackCarryFrom = AttackCarryOrigin.Shell;
            return true;
        }

        /// <summary>
        /// Puts her in the shell. Grounded, she skates along her facing at <see cref="SkateSpeed"/>; the clock
        /// restarts when asked or when it was not running, so bumps and walk-off landings keep the running clock.
        /// Airborne, she coasts on her velocity and a restart leaves the coast untimed until she lands.
        /// </summary>
        internal static void Settle(ref CharacterState s, CookedShellDefinition shell, float touchdownDownSpeed, bool restartClock)
        {
            s.State = ActionState.Shell;
            s.SlideAttackCarryActive = false;
            s.IsFastFalling = false;
            s.AnimLockTicks = 0;
            if (!s.IsGrounded)
            {
                if (restartClock) s.StateTicks = 0;
                return;
            }
            float speed = SkateSpeed(shell, MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ), touchdownDownSpeed);
            s.VX = MathF.Sin(s.FacingYaw) * speed;
            s.VZ = MathF.Cos(s.FacingYaw) * speed;
            if (restartClock || s.StateTicks == 0) s.StateTicks = shell.SkateTicks;
        }

        /// <summary>Her speed kept, plus a share of the touchdown's downward speed, pushed off and capped.</summary>
        internal static float SkateSpeed(CookedShellDefinition shell, float horizontalSpeed, float downSpeed)
            => Math.Clamp(horizontalSpeed + shell.LandingSpeedRatio * MathF.Max(0f, downSpeed),
                shell.MinSkateSpeed, shell.MaxSkateSpeed);

        /// <summary>True when a side contact with this horizontal normal stops a skate heading along the yaw.</summary>
        internal static bool IsHeadOn(float facingYaw, float normalX, float normalZ)
        {
            float into = normalX * MathF.Sin(facingYaw) + normalZ * MathF.Cos(facingYaw);
            return into < 0f && into * into > 1f - BlockedTravelRatio;
        }

        /// <summary>True when a constraint left less than the blocked share of the skate's forward speed.</summary>
        internal static bool LostForwardSpeed(float facingYaw, float beforeVX, float beforeVZ, float afterVX, float afterVZ)
        {
            float sin = MathF.Sin(facingYaw), cos = MathF.Cos(facingYaw);
            float before = beforeVX * sin + beforeVZ * cos;
            return before > 0f && afterVX * sin + afterVZ * cos < before * BlockedTravelRatio;
        }

        /// <summary>A wall or a shield stopped her: she pops up bouncing back, with no recovery lock.</summary>
        internal static void Blocked(ref CharacterState s, CookedShellDefinition shell)
        {
            s.State = ActionState.Idle;
            s.StateTicks = 0;
            s.VX = -MathF.Sin(s.FacingYaw) * shell.BlockedRecoilSpeed;
            s.VZ = -MathF.Cos(s.FacingYaw) * shell.BlockedRecoilSpeed;
        }
    }
}
