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
        // 31: the 0.2.0-playtest.38 release (29 and 30 never shipped on their own):
        // - Mesh-fitted bone capsules follow drawn hurtbox poses at scale 1 (ADR-0055); attack reach is unchanged.
        //   JUMP! CPUs sustain the full jump through squat to clear the fitted body and tail.
        // - Grab aim and directional throws (ADR-0054): stationary hold, Beeline attack/grab arbitration,
        //   press-only stick fling or neutral toss, shared release freeze/queued launch into ordinary Hitstun,
        //   attributed release outcomes and short detached recovery. Phase values 8/9/10; action 21 reserved.
        //   World Snapshot layout unchanged.
        // - Nyx phase 7 (ADR-0051): opt-in requiresFacingOwner hitboxes reject fighters looking away,
        //   runtime API 1.19.0; World Snapshot v21 packs the flag in the existing hitbox-event flags byte.
        //   CPU profiles and delayed threat reach follow forms, with range swaps and Normal/Hard hit-confirmed swap cancels.
        // - Package presentation.flying selects flight hurtboxes at its representative frame zero;
        //   Unity loops it beneath upper-body Q/R and pitches Kamikaze cosmetically. Snapshot layout unchanged.
        // - Nyx's specials (ADR-0051): the tether's atTarget placed catch (Grasp), spawnProjectile homing and perch
        //   blocks with a fragile rule and a hit-pass pop from the thrower's other form (Hex Crow), and
        //   slop.ability.command-grab.v1 (Selfie Grab; DefenseInteractionPhase.CommandHold = 7). Runtime API 1.18.0,
        //   World Snapshot v20 (perching projectiles and queued pops).
        // - FightGuy's Title Match (ADR-0053): slop.ability.clinch.v1, a lunging command grab held as a paired
        //   interaction (ActionState.Clinched = 31, DefenseInteractionPhase.Clinching = 6) whose holder's own hits deal
        //   damage and hitstop only; a hitbox may name its own hit presentation and a timeline presentation may need a
        //   sweetspot hit (Rising Dragon). Runtime API 1.17.0, World Snapshot v19.
        // - Fixed Hitstun can admit a zero-damage, zero-launch dazzle (Nyx Ring Light).
        // - Nyx swap-cancels (ADR-0051): confirmed fighter contacts unlock F through move recovery,
        //   hitstop buffers F, and World Snapshot v18 writes the activation's Connected flag.
        // - Wibou Quick Draw (ADR-0052): slop.ability.dash-strike.v1, fixed target-bearing R,
        //   exclusive light/tip cuts and recovery turn; runtime API 1.15.0. Snapshot layout unchanged.
        // - Wibou's flash (ADR-0052): slop.ability.flash-cut.v1 replaces Skyfall (blade-flurry.v2 retired); held cuts
        //   (ActionState.CutHeld = 30) freeze victims until the pop tick, single-target hitboxes; runtime API 1.15.0,
        //   World Snapshot v16.
        // - Manki flying bomber (ADR-0049): fighter-owned Flying/fuel, Bombs Away and Kamikaze,
        //   no ignition or Aerosol capability; World Snapshot v15.
        // - Manki contact bombs share upward-floor projection with their ground warning;
        //   slower gravity and larger contact/blast radii. Snapshot layout unchanged.
        // - holding the stick against post-hitstun launch momentum brakes at 8 m/s², not the air acceleration.
        // - stick-turned recoveries: a RecoveryMove without a cursor goes where the stick points in its first 3 input ticks.
        // - Bashie's Rescue Ring (ADR-0048): slop.ability.tether.v1, runtime API 1.13.0, a thrown line held as a paired
        //   interaction (ActionState.Reeled = 27, DefenseInteractionPhase.Reeling = 5, presentation source
        //   TetherRetract = 8); Hose Down leaves her kit. World Snapshot layout unchanged.
        // - Bashie's Cannonball and Wave Pool (ADR-0050): slop.ability.shell-stance.v2 (splash and ride; v1 retired),
        //   slop.ability.cannonball.v1, ShellPolicy.Ride = 2, ActionState.Swept = 29, runtime API 1.14.0; the skate,
        //   the bowl and NO RUNNING!'s stun leave her kit. World Snapshot layout unchanged.
        // - Nyx's two forms (ADR-0051): slop.ability.forms.v1 with an alternateForm grid, the swapForm operation,
        //   CharacterState.Form with the other form's cooldown bank, Hitbox.Form, the activation's form, runtime API
        //   1.16.0, World Snapshot v17 and World Frame v8 (a form byte per hit); MatchPlayer's optional form key.
        // 32: the 0.2.0-playtest.40 release:
        // - One command grab (ADR-0057): slop.ability.clinch.v1 is retired into slop.ability.command-grab.v1, which gains
        //   optional level, anchorDistance, releaseLockTicks and whiffRecoveryTicks, an optional launch and a capture
        //   with damage (runtime API 1.20.0). ActionState 31 is CommandGrabbed for every command grab victim, the hold
        //   phase is CommandHold (7) and 6 is reserved; the capture is placed afresh each tick. World Snapshot layout
        //   unchanged; the command grab's own snapshot record changed.
        //
        // Bump once per deploy, never per change: release prepare refuses a release whose src/Shared
        // changed since the previous release tag while this value (and the Master's Protocol) did not.
        // Note simulation changes below; the release bump folds them into one numbered line.
        // Unreleased:
        // - Manki proximity mines, captured-target charged rocket and 600-tick flight-exit cooldown (ADR-0049).
        //   World Snapshot v22 stores mine phases/clocks and the charged rocket target.
        // - Manki's charged rocket faces its captured target independently of Flying heading.
        //   Kamikaze has 16 vulnerable startup ticks with ordinary falling before its full travel allowance.
        //   World Snapshot v23 stores the startup/travel phase.
        // - Standing jump takeoff caps horizontal speed at min(RunSpeed, 1.2 * AirSpeedMax) (ADR-0058).
        // - Walk is held intent in input flags byte 8, bit 0x40, targeting 40% of RunSpeed (ADR-0058);
        //   the World Snapshot input record carries Walk in bit 0x40 of the DashPressed byte, with no layout change.
        // - Nyxxie's Ring Light Overhead (fake air.3) hits with its pole chop: the capsule opens at tick 10 instead of
        //   15 and runs along the pole's swing, from (-0.15, 1.3, -0.1) to (-0.05, 0.6, 1.0), radius 0.45. Damage,
        //   knockback, its 14 active ticks and the 55-tick move are unchanged. Package data only.
        public const byte Version = 32;
    }
}
