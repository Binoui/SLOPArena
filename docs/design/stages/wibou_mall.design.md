---
key: wibou_mall
display_name: Neo Mall
purpose: arcade
status: locked
locked: 2026-10-20
approved_by: maintainer
approval_scope: "Picks of 2026-10-20: direction A (Neo Mall) from the three-direction board (A Neo Mall, B Jump Program, C Critical Hit), a boss in every section, and building it (\"lets go for it\"): shell, route tests, then art, course effects and sounds"
variant: static
blockout:
  - tmp/replays/wibou-mall-01/start.png
  - tmp/replays/wibou-mall-01/skybridge.png
  - tmp/replays/wibou-mall-01/escalator.png
  - tmp/replays/wibou-mall-01/tower.png
  - tmp/replays/wibou-mall-01/complete.png
---

# Neo Mall — Wibou's Character Course

**Status:** locked by the maintainer's picks on 2026-10-20. Playable layout, controller
difficulty, camera and visual acceptance remain the owner's after playing, as with
[Kaboom Quarry](manki_quarry.design.md) and [Fight Night Pier](fightguy_pier.design.md).

The owner's brief: start Wibou's course, "very nerd focused like inside a MTG/DND store, or
maybe a matrix thing". From a three-direction board the owner picked **A, Neo Mall** (the
mall after closing, holding both ideas: the Matrix in his head and the game store as the
finish) and asked for **a boss in every section**.

## Personality

The mall closed at nine. Friday night D&D on the top floor didn't, and Wibou is taking the
ninja way up.

Wibou is the ironic mall ninja ([identity](../../characters/wibou.md)): a cringe Neo with a
katana who takes himself completely seriously. The joke is the gap between how cool he thinks
he is and where he actually is.

## The kit, as measured

Calibrated with the cooked kit in Shared before the layout
(`tests/Shared.Tests/WibouMallRouteTests.cs` keeps the promises):

- **Rising Slash (E)** is his traversal special. It rises about 4 m and keeps carrying him
  forward along the camera for its whole duration (about 13 m on the flat, 18 m after a
  running jump). It is a recovery move: once per airtime, and since 2026-10-20 a hit never
  refreshes a charge. Every E crossing or climb therefore lands on a deck that continues in
  the direction of travel, or against a shopfront wall.
- **Jump, jump, E** is his tallest climb (about 7.5 m); Jump → E reaches 5 m, a double jump 3 m.
- **Dash Slash (R)** cuts everything along its dash and slashes at the end, but its charge
  stops his drift, so it does not extend a jump: rows are its shape, gaps are not.
- **Shuriken Toss (A)** fans three kunai at the locked target; a fan of three breaks from 6
  to 18 m, and a close volley lands all three kunai on one big target.
- **Skyfall (F)** lands its launch and its spike on a fixed boss within about 2 m.

## Composition decisions

1. **A shopping mall after closing.** Neo Mall at night: shutters down, shop signs still lit,
   moonlight through the glass dome. The freshly mopped ground floor is the kill plane; a
   fall respawns at the last saved YOU ARE HERE map.
2. **One climb, level 2 to level 4, with headroom.** The route runs north up the south
   wing's level-2 west gallery, east over the closed skybridge, up the Grand Court escalator
   to level 3, west over the level-3 bridge to the food court, up the glass elevator tower
   and east over the top bridge to the game store on level 4. Upper levels never overhang the
   route, so the camera always has room. Shopfront walls close the backs of the galleries
   (Rising Slash cannot carry Wibou out of the mall); 1 m railings line the atrium edges and
   open only where the route leaves a deck.
3. **Targets are shop mannequins he dressed as agents** (suits, shades), in the shapes of his
   kit: rows for R, fans of three for A, two agents hanging over the elevator tower's lip for
   E, and **one boss per section that takes five hits** (owner pick): Skyfall lands two, a
   close kunai volley three, five slashes do the same. Any slash breaks any target, so plain
   jumps and slashes always finish the course.
4. **Section 1 — Kiosk Row (level 2).** Start beside his katana kiosk, BLADES OF THE EAST. A
   row of agents down the gallery (R), three agents posing on the fashion podium out in the
   atrium (A from the gallery, or double jump over the railing), and the boss: the phone-case
   kiosk's inflatable tube man. The skybridge is CLOSED FOR CLEANING with its middle span
   missing: E across it in one go, or hop the two hanging planters. Checkpoint 1 on the east
   gallery.
5. **Section 2 — the escalators.** Up the stopped Grand Court escalator past three agents
   frozen mid-ride, jumping the steps that are OUT OF ORDER; the boss on the level-3 landing:
   the department store's giant display mannequin, THE AGENT; then the row on the level-3
   bridge (R). Checkpoint 2 at the food court.
6. **Section 3 — the food court, the signature fork.** The row past the tables (R), the boss:
   the hot dog stand's fiberglass mascot, and the glass elevator tower: Jump, jump, E straight
   up its face cuts the two agents over its lip; the ordinary climb hops the hanging menu
   signs and speakers round its north side. Three agents peek over level 4's railing across
   the atrium: kunai them from the food court, or slash them on level 4. Checkpoint 3 on the
   tower top.
7. **Section 4 — level 4.** The top bridge, looking back at the giant katana over the Grand Court (R row), the agents at the
   railing, the boss: the mall cop's cardboard cutout, OFFICER DOUG, and the finish in the
   game store's back room, where his party has waited since nine.
8. **One landmark.** A giant katana sign hangs under the vault over the Grand Court,
   BLADES OF THE EAST, higher than any deck's best climb; it is visible from the galleries
   and the top bridge and is never a route.
9. **Fork signs.** A pharmacy's giant RED PILL and BLUE PILL signs mark the ability way and
   the long way at both forks.

## Negative decisions

- No required target reachable only with an ability; no E, R or F gate.
- No moving escalators, elevators or platforms; no course-only combat rules.
- No route under a ceiling; nothing overhangs the route.
- No real brand, card-game or role-playing-game names on signs; parody tropes only (agents,
  pills, code rain), no film names or logos.

## Palette & lighting

Night interior: cool moonlight through the dome, warm pools under the gallery lights, lit
shop signs (pink, cyan, yellow) and green EXIT signs. Route decks are cream terrazzo so
Wibou's black outfit reads; the hacker green `#91FF69` stays his own accent. Mannequins are
white with black suits and shades; bosses keep their own colours.

## Camera vantages

- North-facing start on the west gallery: the row ahead, the podium right, the katana sign above.
- East-facing skybridge stub: the missing span, the planters and the east gallery.
- North-facing escalator foot: the Grand Court, the landing and the level-3 bridge.
- East-facing food court at the tower: the face, the signs to the left, the top bridge above.
- North-facing level 4: the gallery, the boss and the game store.

## Authoring and acceptance

The shell lives as a `SPEC` in [`tools/blender/wibou_mall/layout.py`](../../../tools/blender/wibou_mall/layout.py)
and reaches the source scene through `sloparena.stage.shell-apply`, then the normal bake.
The [production contract](wibou_mall.md) names the artifacts and checks. Scripted Shared
routes prove reachability with the real cooked Wibou kit; they are not human difficulty,
camera or feel acceptance, which the owner gives after playing.
