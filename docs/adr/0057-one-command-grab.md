# ADR-0057: One command grab

**Status:** Accepted, 2026-10-29. The owner approved merging Title Match's clinch into Selfie Grab's command grab
("go for 1", 2026-10-29). Built 2026-10-29 (see [Build notes](#build-notes)).
**Supersedes in part:** [ADR-0053](0053-fightguy-title-match-clinch.md) (the clinch capability, its phase and victim
state) and [ADR-0051](0051-nyx-two-forms.md) (Selfie Grab's victim state and phase)

## Context

Two command grabs landed in one commit (7155ae16) and both shipped in 0.2.0-playtest.38 (protocol 31):

- `slop.ability.clinch.v1`, FightGuy's F "Title Match" (ADR-0053). A level, fixed-distance lunge carrying a catch
  placed afresh each tick, a hold stage whose own hits deal damage and hitstop only, a short lock one tick before the
  timeline's kick, a whiff that holds the lunge stage, and a lost pair that ends the move.
- `slop.ability.command-grab.v1`, Nyx's fake-form R "Selfie Grab" (ADR-0051). A lunge that stops near the target, a
  separate capture window, the victim held at her grab anchor, and a release with an authored launch.

The engine carried two victim states (`Clinched`, `Grabbed`), two phases (6, 7), two hold writers, two validators,
two catch routes and two CPU rules for one idea.

## Decision

**One public capability, `slop.ability.command-grab.v1`, extended with optional fields.** Nyx's package keeps every
byte; FightGuy's F moves onto it. "Command grab" is the generic name, and Nyx's v1 shape already covers the lunge,
capture and release. The clinch's shape needed new names anyway. The new fields are written only when set:

| Field | Meaning | Title Match | Selfie Grab |
|---|---|---|---|
| `lungeTicks`, `lungeSpeed`, `stopDistance` | Lunge at the target's bearing; the last tick takes the distance that remains | 6, 20, 0 | 13, 14, 1.1 |
| `level` | Gravity off while lunging; a wall that stops it whiffs the grab | true | absent |
| `captureTick`, `capture` | The capture, placed afresh each tick of its window; damage above 0 is credited | 1, sphere r0.6, 6 ticks, 1 damage | 13, sphere r0.75, 6 ticks, 0 damage |
| `anchorDistance` | Held this far in front, through the body mover, facing the grabber; absent: the grab anchor | 0.8 | absent |
| `releaseTick` | Ticks from the press | 94 (stage 1 tick 87) | 65 |
| `launch` or `releaseLockTicks` | An authored launch, or a lock before the timeline's finisher | lock 6 | launch |
| `whiffRecoveryTicks` | Stage 1 is a hold stage that never plays empty | 24 | absent |

- **One victim state:** `ActionState` 31, renamed `CommandGrabbed`, in the captive family (small-hit pose, no input,
  one position writer). **One phase:** `CommandHold` (7); 6 stays reserved.
- **One catch route** (`CatchOnCommandGrab`), **one hold writer** (`HoldCommandGrab`, two anchors by data), **one
  validator** (`CommandHolds`), one release (`ReleaseCommandGrab`) and one CPU profile entry.
- **Retirement.** `slop.ability.clinch.v1` joins `RetiredCapabilities`, because it shipped: a package from that build
  gets a pointer here instead of "unknown capability". `command-grab.v1` is extended, not broken, so it stays.
- **Runtime API `1.20.0`** for a level lunge, an anchor distance, a lock release, a hold stage or a credited catch.
  FightGuy's minimum rises from 1.17.0; Nyx stays at 1.19.0.

## What the old rules became

| Old rule | Now |
|---|---|
| ADR-0053: two stages, lunge stage = travel + 1 | With `whiffRecoveryTicks`, stage 0 ends as the capture closes (`captureTick` + window) |
| ADR-0053: whiff holds stage 0, then ends; a lost pair ends the move | Same, for a grab with a hold stage |
| ADR-0053: held hits deal damage and hitstop; validator allows hitstop | Same for every command grab |
| ADR-0053: release lock leaves the victim captive | `releaseLockTicks` without a `launch` |
| ADR-0053: refuses captive fighters | Refuses paired, cut-held or damaged fighters, as the tether does |
| ADR-0053: objects stop the lunge and the hold stage plays | Same, for a capture with damage |
| ADR-0051: victim `Grabbed` at the grab anchor | Victim `CommandGrabbed`; the grab anchor stays when `anchorDistance` is absent |
| ADR-0051: one stage that ends the move, hit or whiff | Same without `whiffRecoveryTicks` |
| ADR-0051: grab clash, shields and armour do not stop it | Same for every command grab |
| ADR-0051: `CommandHold` = 7 | Kept; the clinch's 6 is reserved |

## Consequences

- **Title Match is unchanged.** All 54 of its per-tick traces match the old build after the renames.
- **Selfie Grab changes where its victim state did** (deliberate, owner-visible). Her held victim shows the captive
  small-hit pose instead of the shared `grab_idle_b` clip, its hurtbox takes that pose, and other fighters' bodies pass
  through it during the hold, as for Title Match and the tether. Its hidden hold counter no longer counts down.
  One-on-one traces match apart from that counter. To bring the grab clip back, the renderer would read the holder's
  move for grab-anchor holds; that is not built.
- **Edge rules one catch route harmonizes.** Title Match now clashes with a same-tick shield grab and can catch an
  unpaired captive (a wave rider, a delivered tether victim). Both grabs clear the victim's attack fields and end its
  flight. Selfie Grab's capture no longer sweeps between ticks.
- The CPU's startup estimate for Title Match is its capture tick (1, was 0). Its F and Nyx's R rules keep their
  triggers, told apart by the hold stage.
- Arcade record fingerprints for FightGuy and Nyx change with the parameter shape, so their records reset at the next
  release.
- The command grab's own World Snapshot record changed; the layout did not.

## Build notes

Built 2026-10-29. Evidence is in `tmp/grab-merge/report.md`: per-tick traces of both moves before and after, World
Snapshot restores at every tick, cook comparisons and the Shared suite.
