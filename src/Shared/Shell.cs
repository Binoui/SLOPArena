using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// The shell (ADR-0045, reshaped by ADR-0050): a tucked landing splashes, and the ride is her wave. "In the shell"
    /// is derived, never stored: she is riding (<see cref="ActionState.Shell"/>), or attacking in a move whose slot
    /// tucks. The ride's clock is <see cref="CharacterState.StateTicks"/>, its speed her velocity and its heading her
    /// facing.
    /// </summary>
    public static class Shell
    {
        /// <summary>A wall that would keep less than this share of the ride's forward speed stops it.</summary>
        internal const float BlockedTravelRatio = 0.6f;

        /// <summary>Ticks a carried fighter stays Swept after the last carry: one tick of slack over the tick order.</summary>
        internal const ushort SweptLeaseTicks = 2;

        /// <summary>The one definition of "in the shell", for gameplay, the hurtbox pose and the Unity tuck.</summary>
        public static bool IsTucked(ActionState state, CookedSlotDefinition? activeMove)
            => state == ActionState.Shell
                || state == ActionState.Attacking && activeMove?.ShellPolicy == ShellPolicy.Tuck;

        /// <summary>
        /// Applies a starting move's shell policy to her velocity. Returns true when the shell decided it, so the
        /// activation skips its Run and Slide carry and its grounded stop. A tuck move keeps her momentum; a
        /// grounded move off the ride starts at ride speed and brakes at the pop-out rate.
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
        /// Puts her on the ride along <paramref name="heading"/> at the ride speed. Grounded, the clock starts;
        /// airborne, she coasts keeping her vertical speed and the clock waits for <see cref="LandRide"/>.
        /// </summary>
        internal static void Ride(ref CharacterState s, CookedShellRide ride, float heading)
        {
            s.State = ActionState.Shell;
            s.SlideAttackCarryActive = false;
            s.IsFastFalling = false;
            s.AnimLockTicks = 0;
            s.FacingYaw = heading;
            s.VX = MathF.Sin(heading) * ride.Speed;
            s.VZ = MathF.Cos(heading) * ride.Speed;
            s.StateTicks = s.IsGrounded ? ride.Ticks : (ushort)0;
        }

        /// <summary>A coast that lands starts the ride's clock and lays its speed back along her heading.</summary>
        internal static void LandRide(ref CharacterState s, CookedShellRide ride)
        {
            s.VX = MathF.Sin(s.FacingYaw) * ride.Speed;
            s.VZ = MathF.Cos(s.FacingYaw) * ride.Speed;
            if (s.StateTicks == 0) s.StateTicks = ride.Ticks;
        }

        /// <summary>
        /// Turns a grounded ride toward <paramref name="targetYaw"/> by at most <paramref name="maxRadians"/>, keeping
        /// its speed.
        /// </summary>
        internal static void Steer(ref CharacterState s, float targetYaw, float maxRadians)
        {
            float delta = MathF.IEEERemainder(targetYaw - s.FacingYaw, 2f * MathF.PI);
            if (delta == 0f || maxRadians <= 0f) return;
            float speed = MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
            s.FacingYaw += Math.Clamp(delta, -maxRadians, maxRadians);
            s.VX = MathF.Sin(s.FacingYaw) * speed;
            s.VZ = MathF.Cos(s.FacingYaw) * speed;
        }

        /// <summary>
        /// The state half of a splash: she stands where she landed, Idle, in landing lag for the pop-up time. The
        /// hitbox and its cue come from the simulation, which owns the resolver.
        /// </summary>
        internal static void Splash(ref CharacterState s, CookedShellSplash splash)
        {
            s.State = ActionState.Idle;
            s.StateTicks = 0;
            s.VX = s.VY = s.VZ = 0f;
            s.LandingLagTicks = splash.PopUpTicks;
        }

        /// <summary>
        /// The wave breaks at the lip: a grounded ride whose step left the ground is put back where the step
        /// started, stopped and popped up.
        /// </summary>
        internal static void BreakAtLip(ref CharacterState s, float x, float y, float z)
        {
            s.PX = x;
            s.PY = y;
            s.PZ = z;
            s.IsGrounded = true;
            s.AirTimeTicks = 0;
            s.VX = s.VY = s.VZ = 0f;
            s.State = ActionState.Idle;
            s.StateTicks = 0;
        }

        /// <summary>One carry step: the carried fighter takes her horizontal velocity and its lease is renewed.</summary>
        internal static void Carry(ref CharacterState victim, in CharacterState rider)
        {
            victim.VX = rider.VX;
            victim.VZ = rider.VZ;
            victim.StateTicks = SweptLeaseTicks;
        }

        /// <summary>True when a side contact with this horizontal normal stops a ride heading along the yaw.</summary>
        internal static bool IsHeadOn(float facingYaw, float normalX, float normalZ)
        {
            float into = normalX * MathF.Sin(facingYaw) + normalZ * MathF.Cos(facingYaw);
            return into < 0f && into * into > 1f - BlockedTravelRatio;
        }

        /// <summary>True when a constraint left less than the blocked share of the ride's forward speed.</summary>
        internal static bool LostForwardSpeed(float facingYaw, float beforeVX, float beforeVZ, float afterVX, float afterVZ)
        {
            float sin = MathF.Sin(facingYaw), cos = MathF.Cos(facingYaw);
            float before = beforeVX * sin + beforeVZ * cos;
            return before > 0f && afterVX * sin + afterVZ * cos < before * BlockedTravelRatio;
        }

        /// <summary>A wall stopped her: she pops up bouncing back, with no recovery lock.</summary>
        internal static void Blocked(ref CharacterState s, CookedShellDefinition shell)
        {
            s.State = ActionState.Idle;
            s.StateTicks = 0;
            s.VX = -MathF.Sin(s.FacingYaw) * shell.BlockedRecoilSpeed;
            s.VZ = -MathF.Cos(s.FacingYaw) * shell.BlockedRecoilSpeed;
        }
    }
}
