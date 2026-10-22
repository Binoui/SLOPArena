namespace SlopArena.Client.Camera
{
    // Persisted player preference; separate from the temporary cursor/aim CameraMode.
    public enum CameraStyle
    {
        Manual = 0,
        AutoCombat = 1,
        Arena = 2,
        AirDuel = 3,
        SideDuel = 4,
        Shoulder = 5,
    }
}
