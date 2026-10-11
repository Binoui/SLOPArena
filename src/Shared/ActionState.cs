namespace SlopArena.Shared
{
    public enum ActionState : byte
    {
        Idle = 0,
        // Wire code 1 is reserved for the retired universal dash.
        Hitstun = 2,
        Sliding = 3,
        Attacking = 4,
        // Wire code 5 is reserved.
        JumpSquat = 6,
        Warping = 7, // Reserved retired state; keep the numeric wire value.
        /// <summary>Hold-to-aim phase (Wibou E): ability active, movement unlocked, jump/dash blocked.</summary>
        Aiming = 8,
        Run = 9,
        LedgeHang = 10,
        Crouching = 11,
        // Defense states; appended to preserve all existing wire values.
        Shielding = 12,
        ShieldDrop = 13,
        GrabAttempt = 14,
        Grabbed = 15,
        Throwing = 16,
        // Wire code 17 is reserved to preserve the phase wire codes.
        AirDodgeMovement = 18,
        AirDodgeRecovery = 19
    }
}
