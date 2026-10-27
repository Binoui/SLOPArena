using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure math functions for combat calculations.
    /// No Godot dependencies - usable by Server and Client.
    ///
    /// All positions are in 3D space (X, Y, Z).
    /// Y is the up axis.
    /// </summary>
    public static class CombatMath
    {
        /// <summary>
        /// Check if a point is within a circle (cylinder) centered at another point.
        /// Used for AoE circle hits and projectile proximity checks.
        /// </summary>
        public static bool IsInCircle(
            float targetX, float targetZ,
            float centerX, float centerZ,
            float radius)
        {
            float dx = targetX - centerX;
            float dz = targetZ - centerZ;
            float distSq = (dx * dx) + (dz * dz);
            return distSq <= radius * radius;
        }

        /// <summary>
        /// Check if a point is within a cone originating from a position.
        /// The cone is defined by its origin, direction, angle (half-angle in radians), and range.
        /// </summary>
        public static bool IsInCone(
            float targetX, float targetZ,
            float originX, float originZ,
            float dirX, float dirZ,
            float halfAngleRad, float range)
        {
            float dx = targetX - originX;
            float dz = targetZ - originZ;
            float distSq = (dx * dx) + (dz * dz);

            if (distSq > range * range)
                return false;

            if (distSq < 0.001f)
                return true; // At origin

            float dist = MathF.Sqrt(distSq);
            float dot = ((dx * dirX) + (dz * dirZ)) / dist; // Normalized dot product

            // Clamp to avoid NaN from floating point
            dot = Math.Clamp(dot, -1f, 1f);
            float angle = MathF.Acos(dot);

            return angle <= halfAngleRad;
        }

        /// <summary>
        /// Check if a line segment (projectile path) intersects a circle (entity hitbox).
        /// Uses segment-to-circle distance check.
        /// </summary>
        public static bool LineIntersectsCircle(
            float lineStartX, float lineStartZ,
            float lineEndX, float lineEndZ,
            float circleX, float circleZ,
            float radius)
        {
            float dx = lineEndX - lineStartX;
            float dz = lineEndZ - lineStartZ;
            float fx = lineStartX - circleX;
            float fz = lineStartZ - circleZ;

            float a = (dx * dx) + (dz * dz);
            float b = 2f * ((fx * dx) + (fz * dz));
            float c = (fx * fx) + (fz * fz) - (radius * radius);

            float discriminant = (b * b) - (4f * a * c);
            if (discriminant < 0)
                return false;

            discriminant = MathF.Sqrt(discriminant);
            float t1 = (-b - discriminant) / (2f * a);
            float t2 = (-b + discriminant) / (2f * a);

            // Check if intersection is within the segment [0, 1]
            return (t1 >= 0 && t1 <= 1) || (t2 >= 0 && t2 <= 1) || (t1 < 0 && t2 > 1);
        }

        /// <summary>
        /// Calculate knockback direction from attacker to target (horizontal only).
        /// Used by pull-type abilities.
        /// NOT the main knockback path — see Simulation.ApplyKnockback for the
        /// standard Smash-style formula with BaseKnockback + KnockbackGrowth.
        /// </summary>
        public static void CalculateKnockback(
            float targetX, float targetZ,
            float attackerX, float attackerZ,
            float force, float upward,
            out float kbX, out float kbY, out float kbZ)
        {
            float dx = targetX - attackerX;
            float dz = targetZ - attackerZ;
            float distSq = (dx * dx) + (dz * dz);

            if (distSq > 0.001f)
            {
                float dist = MathF.Sqrt(distSq);
                kbX = (dx / dist) * force;
                kbZ = (dz / dist) * force;
            }
            else
            {
                kbX = 0f;
                kbZ = force; // Default forward
            }

            kbY = upward;
        }

        /// <summary>
        /// Distance between two 3D points (horizontal only, XZ plane).
        /// </summary>
        public static float HorizontalDistance(float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1;
            float dz = z2 - z1;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>
        /// Compute projectile launch speed and horizontal/vertical components
        /// for a targeted throw landing at a different height than launch.
        ///
        /// Formula: v² = g·D² / (2·cos²(θ)·(D·tan(θ) - dY))
        /// where D = horizontal distance, dY = targetY - launchY (negative = lower),
        /// θ = launch angle, g = gravity.
        ///
        /// Used by both client (arc visual) and server (hitbox velocity).
        /// </summary>
        /// <param name="targetDistance">Horizontal distance in meters (D).</param>
        /// <param name="launchAngleRad">Launch angle above horizontal in radians.</param>
        /// <param name="gravity">Gravity in m/s².</param>
        /// <param name="heightOffset">Vertical displacement: targetY - launchY (negative when throwing downward).</param>
        /// <param name="speed">Launch speed magnitude.</param>
        /// <param name="hSpeed">Horizontal speed component.</param>
        /// <param name="vSpeed">Vertical speed component (upward positive).</param>
        public static void ComputeProjectileLaunch(
            float targetDistance, float launchAngleRad, float gravity, float heightOffset,
            out float speed, out float hSpeed, out float vSpeed)
        {
            float tanθ = MathF.Tan(launchAngleRad);
            float cosθ = MathF.Cos(launchAngleRad);
            float denom = 2f * cosθ * cosθ * (targetDistance * tanθ - heightOffset);
            speed = denom > 0.001f
                ? MathF.Sqrt(gravity * targetDistance * targetDistance / denom)
                : MathF.Sqrt(targetDistance * gravity);
            hSpeed = speed * cosθ;
            vSpeed = speed * MathF.Sin(launchAngleRad);
        }

        /// <summary>Launch for an exact landing tick under SpellResolver's move-then-gravity integration.</summary>
        public static void ComputeProjectileLaunch(ArcadePoint origin, ArcadePoint target, float gravity,
            ushort flightTicks, out ArcadePoint velocity)
        {
            if (flightTicks == 0) throw new ArgumentOutOfRangeException(nameof(flightTicks));
            float time = flightTicks * Simulation.TickDt;
            velocity = new((target.X - origin.X) / time,
                (target.Y - origin.Y) / time + gravity * Simulation.TickDt * (flightTicks - 1) * .5f,
                (target.Z - origin.Z) / time);
        }

        /// <summary>
        /// Inverse of <see cref="ComputeProjectileLaunch"/> for a throw whose distance is always
        /// measured at one landing height: the distance to send so the same fixed-angle arc passes
        /// through a point at another height. A ground cursor on a raised island sends a longer
        /// distance and one on a lower surface a shorter one, so the throw lands where it points.
        /// Gravity cancels out. Returns <see cref="float.PositiveInfinity"/> when the point is on or
        /// above the launch line, where no arc at this angle reaches it.
        /// </summary>
        /// <param name="horizontalDistance">Horizontal distance to the point.</param>
        /// <param name="heightOffset">Point height minus launch height.</param>
        /// <param name="launchAngleRad">Fixed launch angle above horizontal.</param>
        /// <param name="landingHeightOffset">The throw's own landing height minus launch height.</param>
        public static float LobDistanceThrough(float horizontalDistance, float heightOffset,
            float launchAngleRad, float landingHeightOffset)
        {
            float tanθ = MathF.Tan(launchAngleRad);
            float rise = horizontalDistance * tanθ - heightOffset;
            if (horizontalDistance <= 0.001f || rise <= 0.001f) return float.PositiveInfinity;
            // y(x) = x·tanθ − k·x² through the point; solve k·D² − tanθ·D + landing = 0 for D.
            float k = rise / (horizontalDistance * horizontalDistance);
            return (tanθ + MathF.Sqrt(tanθ * tanθ - 4f * k * landingHeightOffset)) / (2f * k);
        }
    }
}
