# Tutorial: the Fighter's License

**Status:** lessons implemented and verified in the Editor on the
[Fighter's License deck](stages/license_test.design.md) — 2026-10-17. The maintainer
approved the gray box on 2026-10-18 and the art pass followed the same day (procedural,
[`tools/blender/license_test`](../../tools/blender/license_test/README.md)). Their
in-game review of the art, a human playthrough and controller acceptance remain ahead.

New players struggle to discover the controls. The Tutorial is a free-roam map with
the player's fighter and a dummy. A checklist walks through one lesson at a time;
everything works at any moment, but only the current lesson's objectives count.

## Decisions (maintainer, 2026-10-17)

- **Order:** the adjusted sequence below. Camera joins movement, fast fall follows the
  jumps, aerials come before specials, and recovery, air dodge and a knockout finale
  are added.
- **Map:** a new dedicated hub-yard map themed as a *Fighter's License Test*, set on a
  rooftop parking deck with one long open edge (`license_test`). Each lesson has a
  visible spot: a jump course of 1.5 m and 3 m blocks and a 5.5 m lookout tower, a
  sparring ring, a knockout box and the open edge. The Training Lab layout remains in
  `TutorialCurriculum.TrainingLab` for tests.
- **Combo:** the game has no true combos today, so the combo lesson teaches a juggle
  (see Findings).
- **Fighter:** the player picks any roster fighter; lesson copy uses that kit's move names.
- **Entry:** a TUTORIAL button above the four Home modes, badged *NEW HERE?* and focused
  until the tutorial has been finished once on this machine.

## Decisions (maintainer, 2026-10-28)

- Teach the three normal roles from ADR-0042. Slot `1` is the quick jab or all-rounder,
  slot `3` hits upward, and slot `4` is the big hit. Each checklist entry also names the
  selected fighter's move. Aerial and throw prompts show all three normal buttons.
- Add **Beeline** after Aerials. Teach a grounded hit and an aerial hit from the arrival
  window against a reset, idle dummy. A miss with nothing in reach is punishable.
- Add **Beeline Tricks** after Combo and before Knockout. Reset the dummy to 30% and
  disable cooldowns. Teach Beeline out of Shield, offstage recovery, and a chase: launch the
  dummy, Beeline after it while it is airborne, and hit it from the arrival. The chase does not
  require the hit before the dummy lands, because Manki and Bonk cannot reach that (see
  Findings); the maintainer chose the chase over dropping the objective. The dummy spars until
  the shield exit objective completes, then rests.
- Beeline homes on the nearest opponent within 20 m without a lock. Its input action is
  `Dash`. Code and state names remain `HomingDash*`.

## Lessons

| # | Lesson | Objectives | Dummy |
| --- | --- | --- | --- |
| 1 | Move & Look | Run 15 m; turn the camera 180° | idle |
| 2 | Jump | Land on a block; jump in mid-air; stand on the 3 m block (the tower also counts) | idle |
| 3 | Fast Fall | Fast fall twice | idle |
| 4 | Attacks | Quick hit, upward hit, and big hit with ground normals `1`, `3`, and `4` | reset, idle |
| 5 | Aerials | Hit it with two different air normals | reset, idle |
| 6 | Beeline | Beeline into a grounded hit; jump, Beeline, and hit with an aerial | reset, idle |
| 7 | Specials | Use A, E, R, and F | reset, idle |
| 8 | Recovery | Drop 0.5 m below the floor and land back on it without losing a life | idle |
| 9 | Shield & Dodge | Block three of the dummy's swings; air dodge | reset, sparring until three blocks |
| 10 | Grab & Throw | Grab the dummy; throw it | reset, idle |
| 11 | Combo | Hit the dummy again before it lands; cooldowns are off | reset at 30%, idle |
| 12 | Beeline Tricks | Beeline out of Shield; Beeline back from offstage; launch the dummy, Beeline after it and hit it; cooldowns are off | reset at 30%, sparring until shield exit |
| 13 | Knockout | Knock the dummy off the stage | at 100% beside an open edge |

Each lesson start clears the player's cooldowns. A completed lesson stamps *PASSED* on
screen (gold *LICENSE GRANTED* after the last test), waits 1.5 s and starts the next. The
stamp is part of the tutorial's own HUD, not the match's particle callout, which stays where
it spawned in the world and drifted as players turned the camera (maintainer, 2026-10-18). The pause menu adds **SKIP LESSON** and **RESTART LESSON**.
The end card reads *LICENSE GRANTED* and offers Training or the menu.

## Architecture

- `src/Shared/TutorialProgress.cs` holds the lesson list, the stage layout facts (floor,
  block heights, knockout spot) and `TutorialProgress`, a pure observer of one tick:
  both fighters before/after, the tick's `HitResult`s and the camera's turn. It never
  feeds gameplay. `src/Shared/TutorialDummy.cs` builds the sparring dummy's ordinary
  input: walk toward a player on the same floor, then Slot1 every 80 ticks.
- `TutorialMatch` (Unity) runs the local simulation like Training, applies each lesson's
  dummy setup, and feeds `TutorialProgress`. `TutorialView` draws the checklist with the
  player's current keyboard or controller bindings and the end card. The scene
  `Assets/Scenes/Tutorial.unity` is a copy of the offline match scene.
- Mode `GameMode.Tutorial`: Home → Fighter Select (player only, no stage step) → scene.

Objective rules are deliberately lenient where the game allows several ways: any block
counts, the tallest block can be climbed, any special activation counts even without a
hit, and the throw counts once the dummy is released.

## Findings that shape the lessons

- **No true combos today.** The true-combo report (`scripts/move-data.sh <fighter>
  --truecombos`) finds none for FightGuy or Wibou at 0/30/60% (its Manki and Bonk runs
  crash later in `RecoveryHitSpec`). A Shared probe of 2026-10-17 also found no strict
  true combo for any fighter, but juggles work: a second, separate move landing while the
  dummy is still airborne from the first. The lesson therefore counts a hit while the dummy
  is stunned *or* still airborne, starts it at 30% and lifts cooldowns, because Bonk's only
  juggle then ended in Blade Storm (15 s cooldown). The hint names a tested opener per fighter
  (`TutorialView.ComboOpeners`): FightGuy High Kick → jump attack; Manki Straight Punch →
  Bazooka; Wibou Up Slash → Up Slash; Bonk Reverse Rising Slash → jump and Reverse Air Slash.
  Bonk's was rechecked on 2026-10-18, when Blade Storm's walking slowed to 35% of his run
  and could no longer chase the launched dummy; it lands from 0.8–1.8 m when the jump goes
  toward the dummy.
  Re-check them with a juggle probe when a kit changes.
- **Recovery is feasible for everyone with E.** From up to 3 m below the deck (and the
  Training Lab floor), an air jump and E return all four fighters; all but Manki also from
  5 m. Bonk's E is an aimed leap and works when held, aimed at the stage and released; the
  lesson hint says so for any fighter whose E aims. An air jump alone usually falls short.
- **Jump heights** (MovementProbe): short hop 0.70–0.94 m, full jump 1.99–3.14 m, double
  jump 3.25–5.13 m (FightGuy lowest, Bonk highest). The deck's 1.5 m and 3 m blocks fit a
  full and a double jump for everyone; the 12 m walls stay above the tower plus Bonk's
  double jump (10.6 m).
- **QWERTY conflict (fixed 2026-10-18).** Special A was bound to whichever key types
  "A", which on QWERTY is also move-left. It now uses the key position right of Tab, as
  ADR-0016 intended: A on AZERTY, Q on QWERTY (`HumanInputActions`).

### Beeline probes, 2026-10-28

`TutorialProgressTests` drives every resolvable roster fighter (Salamander included) against
the live tutorial's dummy (Bonk, or FightGuy for a Bonk player) on the Training Lab with ordinary
scripted inputs. Slots are canonical IDs. The first probes used mirror matches and missed a
Beeline bug the Editor run then found: Manki and Bashie, 1.5 m tall against a 1.7 m dummy,
climbed toward its centre, arrived airborne and could not land a ground hit. Beeline now climbs
feet to feet (ADR-0042, protocol 27); with the old aim the Manki and Bashie probes fail.

| Fighter | Ground hit | Aerial hit | Shield exit | Recovery | Chase | Juggle (rejected rule) |
| --- | --- | --- | --- | --- | --- | --- |
| FightGuy | Dash, buffer `1` | Jump, Dash, buffer `1` | passed | passed | passed | `3`, Dash, `1` |
| Manki | Dash, buffer `1` | Jump, Dash, buffer `1` | passed | passed | passed | not reached |
| Wibou | Dash, buffer `1` | Jump, Dash, buffer `1` | passed | passed | passed | `3`, Dash, `1` |
| Bonk | Dash, buffer `1` | Jump, Dash, buffer `1` | passed | passed | passed | not reached |
| Bashie | Dash, buffer `3` | Jump, Dash, buffer `1` | passed | passed | passed | `3`, Dash, `1` |
| Salamander | Dash, buffer `1` | Jump, Dash, buffer `1` | passed | passed | passed | `3`, Dash, `1` |

- Ground and aerial probes start 6 m apart and buffer the follow-up during travel. Shield
  probes block a swing from a sparring Bonk and Beeline after blockstun. Recovery starts
  beyond the west edge (x = -23, feet 0.7 m below the floor) with the dummy at x = -17;
  Beeline alone returns everyone without a death.
- Chase probes open with `3` at 1.2 m against the dummy at 30%, Beeline once it is airborne
  and buffer `1`.
- **Why the chase replaced a juggle.** A Beeline follow-up that lands before the dummy
  does is out of reach for Manki and Bonk: their launches are low, and the 12-tick float,
  travel and startup outlast the airtime. Every opener/follow-up pair from `1`, `3`, `4`
  and `R`, at 30% and 60%, with and without a jump, hit only after the dummy had landed
  with no Hitstun left (Manki arrives on tick 58 with the dummy grounded; Bonk arrives
  airborne on tick 77 and his `1` hits on tick 89). Re-probe if launches or Beeline
  numbers change; a true Beeline juggle could return then.
- Editor, Manki on the deck: the aerial, out-of-Shield, recovery and chase objectives completed
  through `sloparena.tutorial.run` with zero console errors; the ground objective failed, which
  exposed the climb bug above. The replay after the fix did not run: the tutorial's local
  catalog would not load while Bashie's package failed its cook during other in-progress work.

## Native commands

Through your own gateway lease, in stopped Edit Mode with saved scenes:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.tutorial.prepare
```

Play Mode batches use `sloparena.tutorial.open --fighter <id>` (saves Editor state and
disables domain reload), `editor_play`, then `sloparena.tutorial.lesson --index <n>
[--reset-player true]`, `sloparena.tutorial.run --steps <Training step JSON>` and
`sloparena.tutorial.status`, with `editor_stop` and `sloparena.tutorial.restore` as the
declared restore (it also restores the Game View size and gizmos). For review captures,
`sloparena.tutorial.place --x --z [--y --yaw]` puts the player on a surface and
`sloparena.tutorial.camera --yaw --pitch` aims the gameplay camera before
`sloparena.capture.game-view`. The map has no Unity code of its own: its shell and art come
from the generator through the shared stage commands (see its
[production contract](stages/license_test.md)). Completion is remembered in PlayerPrefs
`sloparena.tutorial.completed`; probes that finish the tutorial delete it afterwards.

## Verification — 2026-10-17

- Shared: 21 `TutorialProgressTests` cover each rule with synthetic frames and the real
  cooked kits on the Training Lab (air jump, fast fall, every special of all four fighters,
  blocked sparring swings, grab and throw); 11 `LicenseTestArenaTests` cover the deck.
  Full Shared suite: 2938 passed, 2 existing skips (before the deck tests were added).
- Editor, deck, FightGuy: a real jump onto the 1.5 m block and a double jump, a fast fall
  off the tower, a real recovery from 2.3 m below the edge with air jump + E, and a Double
  Kick knocking the 100% dummy off the edge into the end card; zero console errors.
- Editor on the Training Lab, FightGuy: run distance, two fast falls passing into Attacks with the dummy reset,
  all four normals, five blocked swings, a real air dodge, aerial hit, grab and throw,
  knockout setup at 100%, pause Skip/Restart and the end card; zero console errors.
- Editor menu flow: Home TUTORIAL → Fighter Select → Wibou → START TUTORIAL loads the
  lesson with the player's bindings (ZQSD on this AZERTY machine). Home at 1920×1080 and
  1366×768.
- Art pass, 2026-10-18: the lessons replayed on the generated art (jump, double jump,
  fast fall from the tower, the knockout into the end card) with zero console errors.
- Not verified: a human playthrough, controller prompts on a pad, 140% UI scale, a player build.

Evidence: `tmp/tutorial/` (captures `p1/`, `p2/`, `ui1/`, `map2/` gray box, `art/` and
`art2/` with the art; probe sources `probe/`), `tmp/license_test-views/` (gameplay-camera
orbits and review cameras) and `.stage-authoring-cache/license_test/`.
