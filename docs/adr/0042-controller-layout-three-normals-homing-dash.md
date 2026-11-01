# ADR-0042: Controller layout v2 — A jumps, three normals, homing dash

**Status:** Accepted — 2026-10-26 (implemented; supersedes ADR-0016's four-normal slot layout).
Amended 2026-10-28: specials moved to the right stick and LB grabs by default; see
[Right-stick specials](#right-stick-specials-2026-10-28). Amended 2026-11-01: the normals are
numbered 1, 2 and 3; see [Normals renumbered](#normals-renumbered-2026-11-01).

## Context

Players on controller say jump belongs on A. Today the face buttons are normals 1–4, LB plus
a face button is a special, and jump sits on RB (`HumanInputActions.cs`). Moving jump to A
takes a face button from the normals, so one normal would have to live on a shoulder.

The four normal roles are jab, all-rounder, upward and big. In most kits the jab and the
all-rounder overlap, so merging them costs little and frees RB.

Closing distance in 3D is the recurring problem in hit-ability reviews. Grabs already have
a universal input (keyboard C, controller LB+RT), so the freed button goes to a universal
approach: a homing dash toward the lock-on target, after the Dragon Ball FighterZ Super Dash.
Universal Dash was retired earlier; its input bit is still reserved and inert.

## Decision

1. **Layout.**

   | Input | Controller | Keyboard |
   |---|---|---|
   | Jump | A | Space |
   | Normals: jab/all-rounder, up, big | X / Y / B | 1 / 2 / 3 |
   | Specials Q / E / R / F | LB + A / X / Y / B | Q / E / R / F |
   | Homing dash | RB | 4 |
   | Shield / grab / fast fall | RT / LB+RT / LT | unchanged |

   Keyboard and controller keep the same move set (ADR-0016 parity).
2. **Three normals.** Each kit merges its jab and all-rounder into one normal. The grounded
   move that stays:

   | Fighter | Keeps | Retires |
   |---|---|---|
   | Bashie | Whirlpool Spin (g2) | Slap (g1) |
   | Manki | Monkey Punch (g1) | Straight Punch (g2) |
   | FightGuy | Straight Punch (g2) | Low Kick (g1) |
   | Wibou | Quick Slash (g1) | Double Slash (g2) |
   | Sir Bonk | Squire's Cut (g1) | Barn Door (g2) |

   The aerial merges are still open; air.2 is sometimes a distinct tool (Wibou's Reverse
   Slash).
3. **Homing dash.** One press dashes toward the lock-on target, or straight ahead with no
   lock. It stops at melee range and has no hit of its own: it ends in a short window that
   a normal, special, jump or shield can cancel, so the hit still comes from the kit.
   Shield, hitboxes and projectiles beat it. In the air it is limited to once per airtime.
4. **Homes by itself, with limited turning.** The player does not steer. The dash turns
   toward the target at a limited rate instead of snapping onto it, height included, so it
   catches a jump but a late enough change of direction can make it miss.
5. **A miss is punishable.** If the dash arrives without the target in reach, there is no
   cancel window, only a punishable end. The cancel window is the reward for landing it.
6. **Off stage is allowed.** Dashing toward a fighter on stage works as a recovery at no
   extra cost; it is a poor option there (predictable, shieldable, punishable on a miss).
7. **Universal cooldown with a HUD read.** Every fighter shares the same dash cooldown.
8. **Overlap with approach specials is accepted.** Wibou's Dash Slash, Bonk's Joust,
   Bashie's Shell Slide and Manki's lunge mostly approach along the ground; the dash
   brings an air approach. The two kinds coexist.

9. **Starts from almost anything.** Every state except hitstun and attacks, out of shield
   included. An attack's IASA window counts as free: wherever IASA lets other actions in,
   the dash comes in too, with no dash-specific exception.
10. **Big range, long range is a risk.** The range is large, but a long dash gives the
    defender time to see it and answer, so using it from far away is very punishable.
11. **Two phases.** A startup float (the fighter hangs in place), then the dash itself.
    Dash speed is the main tuning value.
12. **Instantly recognizable.** Clear VFX on the float and the dash so a defender reads it
    at any distance; concepts first per the VFX workflow.

Dash numbers (float length, speed, turn rate, range, cooldown) come from Training.

## Consequences

- ADR-0016's slot model changes: three normal slots instead of four. One slot's data
  retires in every kit, cooks rebuild, roster pins update.
- Retired moves leave the tutorial (Fighter's License), AI normal picks, HUD icons, Ability
  Lab scenarios and the kit-principles doc (`docs/characters/character-kit-design-principles.md`).
- The dash reuses the reserved Dash input bit, so the wire size does not grow; its state
  (cooldown, travel, cancel window) joins the rollback snapshot.
- Players relearn one button. Keyboard players lose key 4 as a normal and gain the dash
  there.

## Considered alternatives

- **Swap A and RB only, keep four normals.** One normal lands on RB. Rejected: no button for
  the dash, and the normal on RB is awkward.
- **Give the freed button to grab.** Rejected: grab already has LB+RT, the same chord
  pattern Smash uses.
- **Dash passes through projectiles (FighterZ).** Rejected: projectiles here are
  cooldown specials with character identity.

## Delivery

1. Dash prototype in Training on RB and key 4, before any layout change.
2. Normal merges per kit, including the open aerial picks.
3. Binding switch and tutorial update.

## Implementation notes (2026-10-26)

- **Slot ids.** Normals keep the canonical ids `1`, `3` and `4`; `2` left the grid, so
  `CanonicalSlotProjection` has 14 entries (air ordinals start at 7) and the compiler rejects
  `ground.2`/`air.2`. Wire value `AbilitySlots.Slot2` is reserved. Players see the normals as
  1/2/3 through the bindings. Cooked schema 4; every package was recooked and repinned.
- **Merges.** Where slot 2 survived (Bashie, FightGuy), its move moved into slot `1`. Wibou
  first kept slot 2 too; on 2026-10-27 the owner switched him back to Quick Slash and Air Slash.
  The aerial follows the grounded pick: the kept slot keeps both variants. The retired clips'
  catalog bindings were removed. Tutorial combo openers for Manki and Bashie now start from
  normal 1 and need a re-probe; FightGuy no longer has a normal that is safe on block (Low Kick
  was), which the shield-grab regression now measures with Manki.
- **Dash wiring.** `InputState.DashPressed` uses the old dash bit (0x20). States
  `HomingDashFloat`, `HomingDash`, `HomingDashArrive`, `HomingDashMiss`; `CharacterState`
  gains `DashDirY`, `HomingDashTargetId`, `HomingDashCooldownTicks`, `HomingDashAirUsed`.
  Protocol 17, World Snapshot v7. `ServerSimulation` admits the press before shield admission
  and steers the dash before each tick's movement; `Simulation` moves the body without gravity
  or stick control. The target is the current soft/lock target when eligible, otherwise the
  dash flies along facing and ends as a miss. Starting out of an air dodge drops its remaining
  dodge invulnerability.
- **First-pass numbers** (`HomingDashConfig`): float 12 ticks, 24 m/s for up to 40 ticks
  (16 m), 4° turn per tick, arrival at the two capsule radii plus 0.5 m, arrival window 14
  ticks, miss lock 30 ticks, cooldown 180 ticks.
- **Controls.** A = jump, X/Y/B = normals 1/3/4, LB + A/X/Y/B = specials A/R/E/F, RB = dash;
  keyboard 1/2/3 = normals, 4 = dash. `InputController` turns LB + A into special A without a
  jump. The HUD shows the Beeline as a round badge whose ring refills over the
  cooldown (2026-10-28), with the three normals centred over the four specials.
- **Presentation:** the air-dodge clip winds up during the float and holds through the dash.
  The look is the lock-on read (concept C, 2026-10-27): outline, afterimages, dotted line,
  chevrons, reticle and an arrival "!" (see `docs/systems/vfx-particles.md#homing-dash-lock-on`),
  with generated lock, whoosh and arrival sounds (`tools/sfx/homing_dash_clips.py`).
- **Tuning pass 2026-10-27.** An airborne arrival now hovers for `ArriveHoverTicks` (20) in the
  float window before gravity returns; an airborne miss falls at once. An attack started from the
  arrival window used to inherit its timer and was forced to Idle after 14 ticks, which cut slow
  follow-ups; activation now clears it. Frame data comes from `MoveDataReport --dash`.
- **Name, 2026-10-28.** The owner named the dash **Beeline**. The HUD and Controls settings
  show it; code, states and assets keep the `HomingDash` names.
- **Tuning pass 2026-10-28** (protocol 25). A slot pressed during the float or travel used to be
  dropped; it is now held (latest press wins) and starts on the first arrival-window tick, so
  jump → dash → aerial works without timing the press after arrival. A miss or a hit drops it.
  An airborne arrival keeps `ArriveCarrySpeed` (3 m/s) along the dash instead of stopping dead;
  air friction ends the glide. In the apex-dash rows of `MoveDataReport --dash`, a press in
  flight now hits as often as a press on arrival. With the glide, aerials hit far more often
  (Manki 14 → 24 of 27, Bashie 21 → 23). The remaining misses are mostly slow aerials against
  a jumping target that falls away during the hover.

## Right-stick specials (2026-10-28)

The Steady camera (ADR-0043) turns by itself, so on a controller the right stick was only a
brief manual nudge. LB + face-button chords were the hardest part of the layout for new
players, and grab needed a second chord (LB + RT).

1. **Default controller layout.** A right-stick flick is a special, laid out like the face
   diamond: up E (Y), left R (X), right F (B), down Q (A). LB alone grabs; RT then LB is a
   shield grab. A, X/Y/B, RB, RT and LT keep their ADR-0042 roles.
2. **Flick and hold.** A special fires when the stick crosses 60% of its range, on the
   larger axis. The slot stays held until the stick falls below 35%, so aimed and charged
   specials hold through the stick. Another special needs the stick back near the centre;
   rolling the held stick does not fire a second one.
3. **Aiming.** The six aimed specials (all Fixed aim) take their aim from the left stick.
   The right stick no longer aims while it holds the special.
4. **Camera.** On a controller the right stick no longer turns the camera. R3 keeps the
   lock toggle (tap) and the camera toggle (hold); a lock tap during a held flick is ignored.
5. **Fallback.** Settings → Controls → Right Stick: **Camera** restores the previous layout
   (LB + face-button specials, LB + RT grab, stick camera). Grab and Special Modifier share
   LB; the input adapter reads only the one the setting selects.

This is client input mapping only: the input bits, the wire format and the simulation are
unchanged, so there was no protocol bump. The HUD shows a stick-direction glyph on each
special (text arrows without the optional prompt sheet), and Fighter's License prompts follow
the setting. Its first lesson asks for a camera turn: with right-stick specials the player
circles the dummy and the Steady camera turns to keep it in view.
- **Feet-to-feet climb, 2026-10-28** (protocol 27). The dash used to aim at the target's capsule
  centre, so a 1.5 m fighter (Manki, Bashie) dashing at a grounded 1.7 m fighter climbed 0.1 m,
  arrived airborne and could only follow up with an aerial. Toward fighters it now climbs by the
  difference in feet height (`HomingDashRise`), so ground to ground stays on the floor and a jump
  is still caught. Other targets (ball, course targets) keep the centre offset. Found by the
  Fighter's License Beeline lesson in the Editor.
- **Ground and air aim, 2026-10-28** (protocol 28). Goal: a grounded Beeline always leaves a
  ground normal and an airborne one an aerial. `MoveDataReport --dash-pairs` measured every
  attacker × defender pair at 3–12 m. Before: a grounded dash at a jumper climbed and gave an
  aerial every time, an air dash early in a jump dived onto the floor and gave a ground move in
  180 of 432 runs, and the arrival gap drifted between 0.35 and 0.70 m with the distance, so
  short normals (Bashie 1, Manki 3) whiffed at the far end. Now a grounded dash never climbs
  (it can still drop off a ledge), an airborne dash aims its feet `AirAimLift` (10 %) of the
  target's height above the target's feet, and the last step shortens to land on the
  `ArrivalGap`, now 0.3 m. A sweep from feet to body middle picked 10 %: aiming at the middle
  lost aerials (Wibou air 2, Sir Bonk air 1, Salamander air 1). Hits of the expected version,
  out of 432: ground vs standing 377 → 406, ground vs jumper 0 → 255, rising air dash
  172 → 348, apex 348 → 356, apex vs jumper 267 → 244. Sir Bonk's meteor cleave (air 3) prefers
  a higher arrival (46 of 48 at 50 %) and stays weak here.

## Normals renumbered (2026-11-01)

Keeping the ids `1`, `3` and `4` after the merge confused every conversation about the
normals: the owner says "normal 2" for the up normal, which code called `ground.3`, `g3` and
`AbilitySlots.Slot3`. The ids now match the buttons.

- Canonical slots are `ground.1|2|3` and `air.1|2|3`; `ground.4` and `air.4` are rejected.
  Wire values do not change (`AbilitySlots.Normal1` = 3, `Normal2` = 8, `Normal3` = 9; 7 and 10
  stay reserved as `RetiredNormal` and `RetiredFifthNormal`), so the protocol and snapshots
  are unaffected.
- Clip ids follow the slot that plays them (`anim.<package>.g2` is the up normal), as do HUD
  icons (`icon.2`), GenRA brief names, input actions (`Normal1`–`Normal3`, with saved rebinds
  of the old `Slot1`/`Slot3`/`Slot4` still loading) and tutorial objectives
  (`HitNormal1`–`HitNormal3`). Clip files keep their names; they bind by GUID.
- Authoring schema 4 and cooked schema 5 carry the new ids. The compiler rejects schema 3 and
  names the migration: `python3 tools/slot_renumber.py --packages-only` migrates a
  character.json and its cook. The whole renumbering is that tool's output.
- The sections above keep the numbers of their date.
