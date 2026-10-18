# Airborne Tumble Animation Brief

## Purpose

Replace the airborne tumble with a laid-out flail that sells the knockback. The tumble
now takes over from the hit reaction early in hitstun and lasts until the fighter acts
or lands. This is presentation only: Shared simulation keeps knockback, hitstun,
gravity, landing and hurtboxes.

Owner decisions (17 October 2026):

- **Shape:** laid-out flail. The fighter lies back, face up, rocking and flailing.
  The body is never upside down, and there is no full flip.
- **No input:** it keeps tumbling until the player acts or lands, as in Melee's
  tumble. There is no hand-over to `fall` when hitstun ends.
- **Which hits:** strong launches only. Small pops keep today's hit reaction and fall.
- **Clip (18 October 2026):** one sword-aware clip for all four fighters,
  `Art/Characters/shared/Animations/tumble_laid_out.anim`. It is candidate v2b, authored
  on Bonk's rig. The FightGuy-authored candidates laid Bonk's greatsword across his chest
  and shoulders, and Wibou's sword past his head.

## Why the previous version looked straight

Measured on the cooked FightGuy package and `tools/MoveDataReport` at 120%:

- The shared hit clips (`hit_light`, `hit_medium`, `hit_hard`) and Manki's own are
  ground reactions. They start and end in the fighting stance, so a launched fighter
  went back to that stance in mid-air.
- `PlayerRenderer` stretched the hit clip over all of hitstun: 0.75× for a Double
  Kick, 1.38× for a High Kick. The fastest part of the flight showed a ground flinch.
- Tumble started only once hitstun had ended. It faded in over 0.5 s, played one
  0.8 s cycle, then handed over to `fall`. After a Double Kick it was fully visible
  for 3 ticks before landing.
- The clip itself was one held pose: torso tilted back 18–27°, arms out, perfectly
  symmetric left to right, hands moving about 5 cm. The old brief forbade any body
  rotation, which produced it.

## Runtime sequence

```text
hitstop freeze
  -> impact snap: the tier's hit clip at normal speed (~10 ticks)
  -> short crossfade into the tumble clip's build-up (~8 ticks)
  -> tumble loop, through the rest of hitstun and the post-hitstun flight
  -> ends on a player action (immediate), landing, or a new hit
```

- The impact snap keeps the light, medium or hard identity. Only qualifying
  launches cut the hit clip short and play it unstretched.
- The tumble clip loops from the end of its build-up, not from its first frame.
- **Player action:** jump, attack, air dodge or any other action cuts the tumble
  immediately, as today.
- **Landing:** the tumble blends into the existing landing presentation within a few
  ticks. If hitstun remains after landing, the remaining ground reaction plays.
- **New hit:** a new hit starts a new impact snap from the current pose.

### Which hits tumble

A hit qualifies when, at the first frame after hitstop:

- the applied hitstun is at least 24 ticks (0.4 s), and
- the launch rises at least about 15° above horizontal.

Spikes and flat ground kicks keep the current reaction. At 120% most launchers
qualify; at 60% only the strongest hits do. Both thresholds are renderer tuning
constants, not gameplay data.

## The clip

### Structure and timing

The owner confirmed this clock on 17 October 2026, along with the qualification
thresholds above.

| Part | Length | Content |
|---|---|---|
| Impact snap | ~10 ticks | existing hit clip at normal speed, not part of this clip |
| Build-up (plays once) | ~24 ticks (0.4 s) | compact catch → the body is laid back and unfolds |
| Loop | ~60 ticks (1.0 s) | laid out, rocking, limbs flailing; seamless |

### Look

**Build-up: the slow, progressive start.** The first pose is a compact catch:
knees drawn up a little, arms close in front of the chest, chin tucked, torso slightly
folded. Its limbs sit near the body, so it blends from any hit tier's snap. From there
the force takes over. The hips are carried away first and the chest follows. The head
whips back last, and the arms and legs fling outward and trail. The body tips back
progressively and reaches the laid-out pose at the end of the build-up.

**Loop: laid out and flailing.**

- Lying back, face up, at roughly 65–100° of backward pitch. The body rocks slowly
  within that range and never goes head-down past about 110°.
- Bow the body around the hips: the hips sag lowest and lead the flight, while the head
  and the legs trail higher. The back faces the direction of flight.
- The limbs flail loosely, out of phase and asymmetric. One arm flings up past the head
  while the other trails out to the side. One leg is bent and the other extended,
  swapping through the loop. Wrists, ankles and the head lag behind their parents.
  The hands stay loose.
- Add a slight twist or roll about the body's long axis (about 10–20°) so the
  silhouette is never flat or mirrored.
- Nothing should look like a repeated flinch or a new impact on each cycle.

**Read it end-on.** The attacker's camera sees the victim fly away, and the victim's
camera is behind their own fighter, which flies toward it. Both see the launch along
the flight axis, so a side-view C-shape alone disappears. Spread the arms and legs
sideways into a wide X-like silhouette and keep the twist.

### Constraints

- Keep the runtime yaw: the fighter keeps facing the attacker. Pitch and twist are
  local body motion. No full flips, and no head-down poses.
- Weapons: Wibou's sword and Bonk's 1.84 m greatsword stay in the right hand. They
  must never cut through the body or head while the arms flail.
- No landing brace, tech, get-up, planted feet or attack poses.
- The hips may move inside the clip; keep the body centred on the capsule. Unity plays
  it with root motion off.

## Hurtboxes and follow-ups

- Shared hurtboxes keep the hit-clip poses during hitstun, and the jump/fall poses
  afterwards. The laid-out body is up to about 0.8 m from them at the head and feet.
  If juggles feel off, a follow-up can pose Shared hurtboxes from the tumble track for
  qualifying launches. That is a gameplay change and needs its own approval.
- Landing from a laid-out pose is the main visual risk. Judge it in Training before
  considering a dedicated crash-landing clip.
- If air drift turns the fighter, the laid-out body turns with it. Check this in
  Training.
- Manki's hit clips are 1.4 s long and peak later than the shared clips. Check his snap.
  A per-package snap length is the knob if needed.

## Implementation

- `PlayerRenderer` owns the sequence. At the first post-hitstop state of a new hit it
  checks the applied `HitstunTicks` and the replicated knockback velocity. A
  qualifying hit plays its hit clip at 1× and, after 10 hitstun ticks, starts the tumble
  from tick 0 with an 8-tick fade. The tumble holds through post-hitstun flight and ends
  on an action, on landing, or on a new hit. A landing inside hitstun finishes the hit
  clip's ground recovery. Other hits keep the stretched reaction and never tumble
  afterwards. The thresholds are the `Tumble*` constants at the top of the tumble fields.
- The clip has no Loop Time. `MaintainTumbleLoop` wraps it from `TumbleLoopStartSeconds`
  (0.4 s, the end of the shared clip's build-up). A clip imported with Loop Time cycles
  whole instead. Move the loop start into binding metadata only if packages diverge.
- `TryResolveScrubbedState` (Ability Lab and replay scrubbing) applies the same rule
  inside hitstun. It rebuilds the applied hitstun from the pose clock and the launch rise
  from the flight law. After hitstun it shows `fall`: a scrubbed state alone cannot tell
  which hit launched the fighter.
- `FighterHitFeel`'s launch stretch and smoke trail are unchanged and combine with the
  laid-out pose.

## Verification (18 October 2026)

- **All four packages:** each binds the clip, and catalog import re-cooked the roster.
  Inspect reports `valid` with matching source and cooked hashes. The cooked tumble is
  84 frames (1.4 s).
- **Ability Lab:** FightGuy's Double Kick at 120% on the idle dummy (scrubbed path)
  shows `hit_medium` at 1× from frame 21 to 30, then the tumble from 0 at frame 31. It
  reaches 0.4 s at frame 55 and 0.617 s when hitstun ends at frame 68.
- **Play Mode duels** (`sloparena.hitfeel.capture`, live renderer):
  - FightGuy's Double Kick launched Manki, and FightGuy's High Kick launched Bonk.
  - Both go snap, then build-up, then a laid-out loop through the rise and fall, then
    land into idle.
  - Bonk's greatsword stays clear of his body, and the console stayed free of errors.

## Technical delivery

- One Humanoid clip from the [GenRA workflow](../characters/genra-animation-workflow.md),
  authored on Bonk's rig pack (the sword is the hardest constraint) and retargeted to
  all four fighters. Briefs: [`shared-tumble-laid-out`](../../tools/genra/briefs/shared-tumble-laid-out.json),
  its [v2](../../tools/genra/briefs/shared-tumble-laid-out-v2.json) and the shipped
  [v2-bonk](../../tools/genra/briefs/shared-tumble-laid-out-v2-bonk.json). Each is 85
  ticks: a build-up over ticks 0–24 and a loop from tick 24 to 84.
- Each package's existing `presentation.tumble` ID binds it (FightGuy and Bonk:
  `anim.tumble`; Manki: `anim.manki.tumble`; Wibou: `anim.wibou.tumble`). It replaces
  the local-only `Art/Characters/shared/Animations/tumble_drift.fbx`.
- `presentation.tumble` stays optional. When it is empty, the hit reaction and fall
  play as they do for small pops.
- No gameplay events, root motion, hitboxes or timing dependencies.
- Review before binding: side, three-quarter rear and the end-on front and back views,
  on all four fighters. Then review the full hit → tumble sequence in Training from
  each hit tier, including Manki's own clips.

## Acceptance checklist

Ticked items were checked on renders, toolkit checks or Play Mode captures. The rest
need hands-on play in Training.

- [ ] The impact snap of light, medium and hard (shared and Manki) blends into the
      build-up without a pop.
- [x] The build-up reads as the force taking the body: hips carried first, head and
      limbs trailing, progressive rather than a new impact.
- [x] The loop is laid out, face up and asymmetric. It is never head-down, and the
      fighter keeps facing the attacker.
- [x] It reads end-on from the attacker's and the victim's cameras, as well as from
      the side.
- [x] The loop seam is clean over at least three uninterrupted cycles (toolkit seam
      check), and it never looks like a repeated flinch.
- [x] Swords never cut through the body or head (toolkit clearance on Bonk's rig, real
      renders of Bonk and Wibou).
- [ ] Jump, attack and air dodge cut in immediately. Landing and re-hits are
      readable.
- [ ] Only strong launches tumble; small pops are unchanged.
- [ ] Short launches don't look truncated, and long ones don't look repetitive.
