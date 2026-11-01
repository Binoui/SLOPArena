# ADR-0059: Sir Bonk's specials: the tugging lid, the scooping Joust and the Leap of Faith

**Status:** Accepted for the Training prototype, 2026-11-01. The owner picked the shapes from the round-2 board
(`tmp/bonk-specials-2026-11/board.html`): Q-A, R-B with R-C ("down to try C with it, good idea"), E-B ("sounds really
good") and F-A. The values below are the coordinator's first pass and are untuned until the owner plays them.
**Supersedes:** the Joust contract (`slop.ability.steered-charge.v1`) and the targeted leap
(`slop.ability.targeted-leap.v1`) in [Sir Bonk](../characters/bonk.md).
**Extends:** [ADR-0057](0057-one-command-grab.md) (the command hold gains a second entry, a landed hit, and a moving
holder) and [ADR-0051](0051-nyx-two-forms.md) (a second kit records a mark, here on the challenger).

## Context

The owner's Training notes, 2026-11-01:

- **Q, Pot-Lid Throw.** "Fun idea, kinda boring", partly because the hitbox is too small. The lid is a 0.45 m
  projectile that flies straight where he faces, and a hit is a light poke.
- **R, The Joust.** The concept is liked (a movement boost for a slow fighter), but it is hard to hit and a hit is not
  satisfying. It is steered by hand at 12 m/s (his run is 8.5 m/s, the Beeline 24 m/s), starts with a 31-tick
  unarmoured rear, hits with the plank capsule only, and stops dead on contact.
- **E, Leap of Faith.** Hard to aim, unreliable, and the timing and animation are not smooth. It is the roster's only
  held ground-cursor aim, and every flight lasts about 73 ticks whatever the distance, so the target has walked away.
- **F, Blade Storm.** A bit slow but fine. Most of its length is the dizzy tail after the last hit.

R and E share one premise, a line the player aims by hand, which already failed for Bashie's Shell Slide and Shell
Shift (ADR-0050). The specials the owner liked pick their target and pay off on contact.

## Decision

### Q: a bigger lid that bends out and tugs home

- The lid's radius grows, and its visual scale grows with it through the projectile binding.
- The existing projectile `homing` block bends the outbound leg toward the current target captured at spawn.
- The `return` block gains an optional `hit` (damage, angle, knockback, stun, fixed hitstun) that `Turn()` writes onto
  the hitbox. Direction stays the leg's, toward the thrower. Turning back also clears outbound homing. An absent `hit`
  keeps both legs' values the same, so no other package changes.
- **A lid that hit someone on its way back is caught however busy Bonk is.** An empty lid still bonks a busy Bonk, so
  a blind throw-and-attack keeps its cost. Without this rule Q then R fails at the natural timing.
- A projectile with `challengeTicks > 0` writes a **challenge** on a landed, unshielded fighter hit (below).

### R-C: the challenge lives on the challenger

`CharacterState.Challenge { TargetId, TargetDeaths, UntilTick }`. Only the owner's own hits from a projectile with
`challengeTicks` write it, in the hit pass, so it survives the end of Q's activation. The latest lid hit wins. It is
valid while the tick is before `UntilTick`, the target's death count matches and the target is an eligible enemy in
lock range. The Joust takes it at its press and clears it, hit or miss. Bonk's own KO or reset clears it. The renderer
draws a marker over the challenged fighter from Bonk's state, so it is rollback-correct. Two Bonks keep independent
challenges.

### R: `slop.ability.steered-charge.v2`, the Joust scoops and carries

- **One lifecycle from tick 0.** The capability owns the rear, which lasts the stage's duration, or
  `challengedRearTicks` when he took a challenge. The target is chosen at the press: the challenge, else the current
  target (sampled after that tick's target processing). He faces it during the rear. The rear stays unarmoured.
- **Tracking.** In the gallop and the carry, with the stick neutral, the heading turns toward the target at
  `trackDegreesPerSecond` (`challengedTrackDegreesPerSecond` when challenged). A held stick overrides for that tick at
  `turnDegreesPerSecond`. Tracking stops for good once the target is more than 90° off the heading, so a missed pass
  does not circle back. Air strides steer by stick only, so tracking never pulls a sideways recovery off the stage.
- **Contact.** The plank lance and a new chest volume share one hit set. The contact is a landed hit: a shield blocks
  it (skid, nothing carried), and an armoured victim, a fighter already paired, held by a cut or Swept takes it as an
  ordinary hit and the gallop takes the hit ending.
- **The scoop.** A landed contact opens ADR-0057's command hold (CommandHold phase, CommandGrabbed victim) with the
  small scoop damage. Both volumes retire. Bonk keeps gallop speed. The victim sits at an anchor ahead of him, raised
  onto the lance.
- **One hold, two entries.** `ICommandHold` replaces the concrete `ServerAbility.CommandGrab` seam, with
  `HoldCatch { Capture, LandedHit }`: a grab's capture catches before the shield and through armour, a landed hit does
  not. Title Match and Selfie Grab keep their behaviour. **Victim placement moves to one post-movement pair pass** for
  every holder, so a moving holder places its victim from its final position. The ability requests the release; that
  pass places, then releases, before hurtboxes and hit resolution.
- **The fling.** The carry ends at the earliest of a second R press (consumed, never a second Joust), a wall (his
  travel under 60% of commanded, or the victim held back from its anchor by terrain), the ground ledge (he stops on the
  edge and flings outward), `maxCarryTicks`, or the end of the air strides. `ReleaseCommandGrab` queues the authored
  `fling` launch along his heading, credited to R. A ground fling plays a short BONK ending.
- **Breaking it.** A third party's damaging hit on the victim, a grab on Bonk or his KO ends the pair through the
  existing rules; the victim leaves with that hit's reaction and Bonk takes the hit ending. Armour covers the gallop and
  carry. There is no mash-out in this prototype.
- **Air R.** The rear holds level, the strides follow at gallop speed, then the look-down and drop, as today. An air
  scoop carries on thin air and flings on R, the clock or the end of the strides: the off-stage kill threat. A ground
  carry never leaves the stage.

### E: `slop.ability.targeted-leap.v2`, rise, hang, drop

- **A tap**, no cursor (`aimMode: none`). Phases Rise, Hang, Drop, Fall, Landing in one activation, gravity off until
  Fall.
- **Rise:** `riseHeight` over `riseTicks`, eased out, from where he is (ground or air). A ceiling stops it.
- **Hang:** still. Each tick the spot is the target's feet (the target chosen at the press), clamped to `maxRange`
  from where he rose and to supports below him; without a target, `noTargetDistance` along the stick, else his facing.
- **Drop:** the spot locks at the hang's end, and he travels a straight swept line to it in `dropTicks`, never upward.
  He passes through fighters' bodies on the way down and holds his landing spot while they are pushed aside, as Wibou's
  dash strike does. Without that, a target standing at a ledge stopped him past the edge and he fell with no slam
  (QA check #49, 2026-11-01).
  A late dash avoids it. A real landing starts Landing and spawns today's ring once, at his feet.
- **Fall:** a drop that ends in the air (a void, an airborne target) falls with ordinary gravity; no landing within
  `maxFallTicks` ends the move with no slam.
- **Landing:** the full stuck-plank recovery (`missRecoveryTicks`) on a miss or a shield. A confirmed unshielded
  fighter hit cuts the remaining recovery to `hitRecoveryTicks` and seeks the landing clip to `hitLandingSeekTick`.
- **The light follows state, not events.** `ActiveMoveView` gains an optional spot that a capability publishes; the
  leap publishes it during Hang and Drop, and a client presenter draws the light there.
- Fixed beats replace the pose-easing sweep (`UpdateFlightPose`, `LandingTicks`) and the cursor aim; both are deleted.

### F: shorter dizzy

Both F stages end at tick 112 instead of 142, and the dizzy windmill shows for 24 ticks instead of 54. The spin and its
hits are unchanged. If the cut into idle reads abrupt, the clip's tail is retimed rather than cut.

### Retirement

`steered-charge.v1` and `targeted-leap.v1` are used only by Bonk. Both retire in `RetiredCapabilities` with reasons, and
their parameters, parsers and tests that pin retired behaviour are deleted in the same wave. Simulation changes go under
`Unreleased` in `SimulationProtocol.cs`; the World Snapshot format and runtime API take the next free values.

## Prototype defaults

All untuned.

| Move | Values |
|---|---|
| Q | radius 0.75 (lid visual ×5/3); homing 1.25°/tick; return hit 5 damage, angle 0, knockback 20 + 0, fixed hitstun 48; challenge 120 ticks |
| R | rear 18 ticks (challenged 9); gallop 18 m/s for up to 90 ticks; track 240°/s (challenged 420°/s), stick 240°/s; lance radius 0.6, chest capsule radius 0.65 ahead of the hips; scoop 3 damage; carry anchor 1.1 m ahead, 0.4 m up, up to 36 ticks; fling 12 damage, angle 40, knockback 12 + 36, stun 20, ending 18 ticks; air strides 45 ticks, look-down 20; miss ending 30 with a 12-tick skid |
| E | rise 6 m in 18 ticks, hang 24, drop 18, max range 12 m, no-target 6 m, max fall 60; miss recovery 68, hit recovery 18 from landing-clip tick 40; ring unchanged (2 m, 13 damage) |
| F | stage 112, dizzy 24 |

Stand-in presentation: the existing rear, gallop, ending and look-down clips; `anim.bonk.g4`/`a4` for the fling; the
leap clip's first 40 ticks resampled to the 18-tick rise (`anim.bonk.ge-rise`), the approved sword-first `anim.bonk.ge-drop` held at tick 0 for the hang and restarted for the drop, the landing clip for both recoveries; an existing ring
prefab tinted gold for the challenge marker and the light. Final clips and effects wait for the owner's verdict.

## Implementation naming constraint

The Shared compiler requires the rear to remain the first sorted clip in the Joust stage.
Directly listing `anim.bonk.g4` or `anim.bonk.a4` as the fling would put it before the rear and fail admission.
The package therefore names `anim.bonk.gr-fling` and `anim.bonk.ar-fling` as aliases for those existing clips.
Unity must bind and cook those aliases to the same g4/a4 stand-ins. The motion and timing are unchanged.

## Consequences

- One way to hold a fighter, with two entries. The command-hold generalisation touches Title Match and Selfie Grab, so
  their tests gate every Bonk test.
- The tug is constant knockback, so its length is fixed rather than "to his feet". If Training shows near victims
  bumped into him and far ones short, the fallback is the lid hooking the victim home (a resolver-owned captive).
- An armoured, tracking, carrying charge has no mash-out. Shield, jump, dodge and grab answer it.
- E's drop can be fast at long range or from high up (about 55 m/s worst case); the swept move must not tunnel through
  thin platforms.

## Alternatives rejected

- **R:** a carry state of its own (a fourth way to hold a fighter), Bashie's Swept lease (no anchor, no release
  launch), folding the gallop into `command-grab.v1` (a god capability).
- **E:** timeline beats plus a drop capability (the move's invariants spread over operations), Cannonball's solved arc
  (always rises first; no hang to track in).
- **Challenge:** a mark on the victim (two challengers share one writer), deriving it from the last hit (a plank normal
  between Q and R loses it).

## Training follow-up, 2026-11-01

The owner liked the reworked R and asked for close aim help and a longer run. Both R slots now allow 150 gallop ticks, previously 90. The carry clock is unchanged. An optional `assist` block selects an eligible enemy inside 3.5 m horizontally and a 70-degree half cone, preferring the selected or challenged target and otherwise the nearest enemy. Gallop and AirStrides turn toward it at 900 degrees/s, overriding stick steering for that tick. Carrying never assists, and a dodge behind the cone escapes. The selector adds no persistent state or snapshot fields.

The owner also asked for a longer, faster F. Two copies of the accepted steady turn add 28 ticks before the finisher. Holds start at 8/29/43/57/71/85; the finisher starts at 99. Heavy slash moves to 100, dizzy to 116 for 30 ticks, aim-off to 122 and stage end to 146. The stage length sets the clip's playback rate, so a shorter stage speeds up the whole spin instead of cutting the tail (squeezing the 170-tick splice into 140 ticks lost rear finishers in 48 of 96 full-string probes). The clip's dizzy tail after tick 122 is therefore retimed to half its length (170 to 146 ticks, backup of the 170-tick version under `artifacts/sir-bonk-f-20261101/backup-170/`), and the stage matches it for 1:1 playback; 96 of 96 probes land the full string. The same rule means the 2026-11-01 F-A change (142 to 112) sped the old clip up rather than shortening its dizzy. Wind and dust extend by 28 ticks. Both slots use movement scale 0.7, previously 0.35. The original clip is backed up under `artifacts/sir-bonk-f-20261101/backup/`; measured plank passes live alongside it. All follow-up values remain untuned pending Training acceptance.
