# ADR-0042: Controller layout v2 — A jumps, three normals, homing dash

**Status:** Accepted — 2026-10-26 (implemented; supersedes ADR-0016's four-normal slot layout)

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
- **Tuning pass 2026-10-28** (protocol 25). A slot pressed during the float or travel used to be
  dropped; it is now held (latest press wins) and starts on the first arrival-window tick, so
  jump → dash → aerial works without timing the press after arrival. A miss or a hit drops it.
  An airborne arrival keeps `ArriveCarrySpeed` (3 m/s) along the dash instead of stopping dead;
  air friction ends the glide. In the apex-dash rows of `MoveDataReport --dash`, a press in
  flight now hits as often as a press on arrival. With the glide, aerials hit far more often
  (Manki 14 → 24 of 27, Bashie 21 → 23). The remaining misses are mostly slow aerials against
  a jumping target that falls away during the hover.
