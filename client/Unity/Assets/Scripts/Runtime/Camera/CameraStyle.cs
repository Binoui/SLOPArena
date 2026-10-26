namespace SlopArena.Client.Camera
{
    // Persisted player preference; separate from the temporary cursor/aim CameraMode.
    // Values are saved as ints, so removed styles (1, 3, 4, 5) stay unused.
    public enum CameraStyle
    {
        Manual = 0,
        Arena = 2,
        Steady = 6,
    }

    public static class CameraStyleNames
    {
        public static bool IsValid(int style) => style is (int)CameraStyle.Manual or (int)CameraStyle.Arena or (int)CameraStyle.Steady;
        public static string DisplayName(this CameraStyle style) => style.ToString();
    }
}
