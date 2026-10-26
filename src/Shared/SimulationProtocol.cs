namespace SlopArena.Shared
{
    public static class SimulationProtocol
    {
        // 11: World Snapshot v2 (Arcade ball and Slopball rules) and Match formats (ADR-0040).
        // 12: World Snapshot v3 (returning projectiles) and projectile catch/self-hit presentation events.
        // 13: leader-selected co-op friendly fire is frozen in Match setup.
        // 14: Versus Slopball camps, point state, events and results.
        // 15: online Character Courses, course progress/events/results and player colours.
        // 16: match content pins the exact baked arena digest as well as the character catalog.
        // 17: three normals, homing dash input bit, state and World Snapshot v7 (ADR-0042).
        public const byte Version = 17;
    }
}
