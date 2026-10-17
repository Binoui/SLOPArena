using SlopArena.Shared;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The current stage's floor for cosmetic effects such as blast rings and scorch marks
    /// (<see cref="SnapToFloor"/>). <see cref="MatchBase"/> sets it when it spawns the stage and
    /// clears it on teardown. It is only sampled for presentation; gameplay uses Shared.
    /// </summary>
    public static class PresentationFloor
    {
        private static ArenaHeightmap? _heightmap;

        public static void Use(ArenaHeightmap heightmap) => _heightmap = heightmap;

        public static void Clear() => _heightmap = null;

        /// <summary>The floor height under (x, z), or false outside the stage or without a stage.</summary>
        public static bool TrySample(float x, float z, out float y)
        {
            y = _heightmap?.Sample(x, z) ?? float.MinValue;
            return y > float.MinValue / 2f && !float.IsNaN(y) && !float.IsInfinity(y);
        }

        public static bool Available => _heightmap != null;
    }
}
