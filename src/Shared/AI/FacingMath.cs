using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Horizontal facing-frame math for telemetry: the match recorder's whiff-spot and
/// closest-approach normalization. It uses the simulation's own convention so recorded
/// vectors agree with the hitboxes the resolver actually tested.
/// </summary>
public static class FacingMath
{
    /// <summary>
    /// Rotate a world-space horizontal delta into an entity's facing frame.
    /// Returns (side, forward). The simulation faces <c>yaw = atan2(dx, dz)</c>, so forward
    /// is (sin yaw, cos yaw) and the character's right is (cos yaw, −sin yaw); at yaw 0 they
    /// are +Z and +X. This is the inverse of <see cref="HitboxGeometry"/>'s offset rotation
    /// <c>wx = OffX·cos + OffZ·sin, wz = −OffX·sin + OffZ·cos</c>.
    /// </summary>
    public static (float Side, float Forward) ToFacingFrame(float dx, float dz, float facingYaw)
    {
        float cos = MathF.Cos(facingYaw);
        float sin = MathF.Sin(facingYaw);
        return (dx * cos - dz * sin, dx * sin + dz * cos);
    }
}
