# ADR-0068: The body pose clock is Shared state

**Status:** Accepted 2026-11-14. The owner approved option A ("Shared owns a deterministic body-pose
clock that reproduces today's rendered motion") over option B (the renderer samples the old Shared
frame counter, which changed the look).
**Deciders:** @Binoui
**Related:** [ADR-0039](0039-full-world-rollback.md), [ADR-0055](0055-capsule-hurtboxes.md),
[airborne tumble brief](../design/airborne-tumble-animation.md)

## Context

Hurtboxes follow baked poses, but outside attacks two clocks chose the pose. Shared stepped a
per-fighter frame counter one baked frame per tick and reset it when the animation index changed.
`PlayerRenderer` kept its own rules: run playback scaled by horizontal speed with a 0.35 floor and a
0.1 m/s threshold (Shared used 1 m/s), hit reactions stretched over the hitstun they began with, and
strong launches cut to a tumble. Measured at 60 fps, run and hitstun hurtboxes were up to about
730 mm (P95) from the drawn body. Attacks already shared one clip clock.

## Decision

1. `BodyPoseClock` (`src/Shared/BodyPoseClock.cs`) is per-fighter simulation state: the clip rule,
   baked animation index, sampled frame, rate, elapsed ticks, a segment counter, reaction identity
   (sequence, applied hitstun, level) and the previous tick's state, jumps, ground contact and wall
   jump for transitions.
2. Shared steps it once per tick when it builds hurtboxes, and the hurtboxes take its pose. Its
   rules are the renderer's former rules, now deterministic:
   - run: one baked frame per tick times `max(0.35, |v| / RunSpeed)` above 0.1 m/s, looping;
   - a reaction restarts on a new `HitReactionSequence` and stretches over its starting hitstun;
   - a launch with at least 24 ticks of hitstun rising 15° or more plays a 10-tick impact snap at
     clip speed, then the tumble's build-up once and its loop from frame 24, through post-hitstun
     flight until the fighter lands, jumps or acts;
   - a launch that lands inside hitstun finishes the reaction's ground recovery;
   - an ordinary reaction plays out through an airborne launch before blending;
   - hitstop holds the pose.
   Attacks keep their move's clip clock. Idle, jump, fall, dodge and captive poses keep one baked
   frame per tick.
3. World Snapshot v32 stores the clock in place of the frame counter, so rollback restores it and
   snapshot comparison covers it. A respawn starts it afresh.
4. `ServerSimulation.GetBodyClock` exposes it. Presenters pass it to `PlayerRenderer`, which shows
   the clips the clock owns (run, reaction, impact snap, tumble, recovery) at the clock's frame and
   rate and keeps its own crossfades. Callers without a simulation clock (Replay View shells,
   synthetic tool states) step a renderer-local clock with the same Shared rules. Ability Lab
   scenario frames record each fighter's clock for scrubbing.
5. No package metadata changed. The clock uses baked frame counts. Every shipped tumble clip is
   non-looping, which the build-up-then-loop rule assumes.

## Consequences

- Run and hitstun hurtboxes follow the drawn body; crossfades remain presentation only.
- Grounded fighters between 0.1 and 1 m/s now have run-pose hurtboxes, and reactions, tumble and
  hitstop change hurtbox poses. This is a gameplay change that needs the next protocol bump.
- A loop's period is its baked frame count. Where float rounding gave a clip one extra baked frame
  (FightGuy's run: 29 frames for 28 frames of clip), its cycle is one frame longer than before.
- Re-hits on the same clip now restart the reaction. The old renderer reused the playing Animancer
  state and kept its time, contrary to its stated intent.
