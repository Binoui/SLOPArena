namespace SlopArena.Shared
{
    public enum AimMode : byte
    {
        None,            // No aim input; camera free, cursor locked
        GroundCursor,    // Cursor unlocked; raycast ground → AimYaw + AimDistance
        CameraForward3D, // Cursor locked; camera yaw+pitch → AimYaw + AimPitch
        /// <summary>Camera locked; mouse delta rotates a ground direction vector around the character (Wibou E, Manki F).</summary>
        GroundVector,    // Camera frozen; mouse delta → AimYaw, fixed distance
    }
}
