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
        // 18: press-to-fire homing Bazooka and World Snapshot v8 projectile state.
        // 19: persistent Finger Guns cylinder state in World Snapshot v9.
        // 20: Mayhem online, three-value friendly fire, World Frame v5 and World Snapshot v10.
        // 21: JUMP! shockwaves, World Snapshot v11 and World Frame v6.
        // 22: BREAK OUT! walls, World Snapshot v12 and World Frame v7.
        // 23: universal progressive ground locomotion. Snapshot and input layouts unchanged.
        // 24: Run-started attack carry brakes over 20 ticks; replicated carry origin.
        // 25: homing dash holds a slot pressed in flight for the arrival; airborne arrivals glide.
        // 26: Bashie's shell stance (ADR-0045): ActionState.Shell, owner windows, thrust, World Snapshot v13.
        // 27: Beeline climbs feet to feet toward fighters, so grounded dashes at taller fighters stay grounded.
        // 28: grounded Beelines never climb, airborne ones aim just above the target's feet; arrival lands on a 0.3 m gap.
        // 29: snappier ground start: 60% of RunSpeed, then a 12-tick ramp.
        // 30: Cyclone Kick carries victims with interval hits and a finisher; dizzy only on a whiff.
        //
        // Bump once per deploy, never per change: release prepare refuses a release whose src/Shared
        // changed since the previous release tag while this value (and the Master's Protocol) did not.
        // Note simulation changes below; the release bump folds them into one numbered line.
        // Unreleased:
        // - Manki Can Bomb and powered Jetpack v2 (ADR-0049), three charges, mobile F charge,
        //   shared owner-fire/blast can reactions and World Snapshot v14 cans/ignition state.
        // - holding the stick against post-hitstun launch momentum brakes at 8 m/s², not the air acceleration.
        // - stick-turned recoveries: a RecoveryMove without a cursor goes where the stick points in its first 3 input ticks.
        // - Bashie's Rescue Ring (ADR-0048): slop.ability.tether.v1, runtime API 1.13.0, a thrown line held as a paired
        //   interaction (ActionState.Reeled = 27, DefenseInteractionPhase.Reeling = 5, presentation source
        //   TetherRetract = 8); Hose Down leaves her kit. World Snapshot layout unchanged.
        public const byte Version = 30;
    }
}
