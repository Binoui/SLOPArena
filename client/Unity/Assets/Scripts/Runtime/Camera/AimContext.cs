namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Aim data produced each FixedUpdate tick by <see cref="AimHandler"/>.
    /// Passed directly into <see cref="Input.InputController.BuildInputState"/>.
    /// Null fields mean "use camera default" — only set by an active aimed ability.
    /// </summary>
    public struct AimContext
    {
        public float? AimYawRad;
        public float? AimPitchRad;
        public ushort? AimDistanceCm;
        public bool IsAiming;
        public bool LocksMovement;

        /// <summary>No active aim — camera-driven defaults.</summary>
        public static readonly AimContext None = new AimContext { IsAiming = false };
    }
}
