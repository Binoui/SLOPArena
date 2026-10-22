namespace SlopArena.Shared
{
    public static class SimulationProtocol
    {
        // 11: World Snapshot v2 (Arcade ball and Slopball rules) and Match formats (ADR-0040).
        // 12: World Snapshot v3 (returning projectiles) and projectile catch/self-hit presentation events.
        // 13: leader-selected co-op friendly fire is frozen in Match setup.
        // 14: Versus Slopball camps, point state, events and results.
        // 15: online Character Courses, course progress/events/results and player colours.
        public const byte Version = 15;
    }
}
