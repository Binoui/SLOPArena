---
key: fightguy_pier
display_name: Fight Night Pier
purpose: arcade
status: locked
locked: 2026-10-18
approved_by: maintainer
approval_scope: "Picks of 2026-10-18: the Fight Night Pier direction, kit-shaped balloon groups plus one eight-hit bonus target per section (a Shared course addition), and building straight through: greybox, route tests, then art, effects and sounds without a review stop"
variant: static
blockout:
  - tmp/replays/fightguy-pier-03/start.png
  - tmp/replays/fightguy-pier-03/gallery-kick.png
  - tmp/replays/fightguy-pier-03/old-pier-kick.png
  - tmp/replays/fightguy-pier-03/tower-rise.png
  - tmp/replays/fightguy-pier-03/coaster-kick.png
  - tmp/replays/fightguy-pier-03/complete.png
---

# Fight Night Pier — FightGuy's Character Course

**Status:** locked by the maintainer's picks on 2026-10-18 (one question round) and built
straight through the same day: shell, route tests, art, course effects and sounds. Playable
layout, controller difficulty, camera and visual acceptance remain the owner's after playing,
as with [Kaboom Quarry](manki_quarry.design.md).

The owner's brief: FightGuy's course "feels very lacking" next to Kaboom Quarry. It was
designed before his identity as the underground champ, and its layout is "a bit boring". Bring
it to the Quarry's quality, even if that takes a whole new direction.

From three directions (Fight Night Pier, Training Montage, Ring Walk) the owner picked
**Fight Night Pier**. Targets come in the **shapes of his kit**: a row for R (Cyclone Kick),
a stack for E (Rising Dragon), a far one for A (Ki Shot), and **one bonus target per section
that takes eight hits** (one Fist of Fury, F). Any hit still breaks a balloon. The owner also
chose to **build straight through** with no review stop. FightGuy Sanctuary stays as his
fallback course; the pier comes first in `CharacterCourseMatch.Courses`.

## Personality

The champ can't walk past a fairground game without winning it.

## Composition decisions

1. **A seaside pier at dusk, taken over by the fight club for title night.** Bleached planks,
   white trim and FightGuy's blue, gold and headband red on everything he paid for. The
   burnt old pier runs beside the new one. The sea is the kill plane; a fall respawns at the
   last saved checkpoint.
2. **One loop to the ring.** From the gate on the promenade the route runs north up the
   midway, west across the gap to the burnt old pier and north along it, east over the ticket
   booth to the Big Wheel's deck and up the champ's VIP tower, then down the coaster to the
   ring at the pier head. The finish is inside the ring.
3. **Balloons in the shapes of his kit.** Gold 肉 foil balloons float where one special clears
   a group. Rows for R: the shooting gallery, the line over the old-pier gap, the pair over
   the ticket booth and the coaster straight. The gap line fits the Cyclone Kick's 8 m hit
   window: its hitboxes are live on ticks 7–48 of a 12 m glide. Stacks for E: the High
   Striker and the three balloons up the wall Rising Dragon climbs. Far ones for Ki Shot:
   the dunk tank, the bell buoy and the prize buoy, out at sea. Every balloon is also
   reachable by jumping and punching.
4. **One bonus target per section takes eight hits** (owner pick; Shared
   `CourseTargetDefinition.Hits`, see [arcade.md](../arcade.md)). One Fist of Fury breaks
   it; eight ordinary hits do too. Section 1: the contender's car in the pen by the gate
   (the car bash). Section 2: a bull piñata on the burnt pier's derrick (肉 means meat).
   Section 3: the PUNCH-O-METER on the tower top, its meter climbing from WIMP to FIGHTGUY.
   Section 4: the champ's own heavy bag by the ring. Each hit shows the next damage stage
   and a climbing count; the eighth is a BONUS! burst.
5. **Section 1, the Midway.** The promenade and the pier gate under the FIGHT NIGHT PIER
   arch, the midway with the open-air shooting gallery (R row), the High Striker, the dunk
   tank out on a buoy and the carousel plaza. Checkpoint 1 on the plaza.
6. **Section 2, the Old Pier, the first fork.** Jump → R crosses the 10 m gap from the plaza
   through the balloon line; the ordinary way hops three burnt pilings zig-zagging under it
   and punches the line. The collapsed middle between the piers (20 m, PIER CLOSED (NOT HIS
   FAULT)) is too wide for any kick. Burnt decks run north past the piñata to the old pier
   head, with the bell buoy out west. Checkpoint 2 on the old pier head.
7. **Section 3, the VIP Tower, the signature fork.** Jump → R over the ticket booth sweeps its
   pair. The champ's 6 m VIP tower stands on the wheel deck: Jump → E goes up its sheer west
   face through the stack (E alone and a double jump fall short). The ordinary climb hops
   four Big Wheel gondolas he "borrowed" and stacked on crates round its north side (WHEEL
   CLOSED: GONDOLAS BORROWED). Both land on the tower top, at the punch machine.
8. **Section 4, the Coaster.** Off the tower to the lift-hill top (checkpoint 3), down the
   drop to the long straight (R row), the last drop, the prize buoy for Ki Shot, the heavy
   bag on the pier head and into the ring.
9. **Checkpoint sensors never hold a target they require.** Checkpoint 3 sits on the lift
   hill, past the punch machine. On the tower top it would greet every landing with "1
   TARGET MISSED", because the machine sits inside the sensor. `layout.py` refuses a
   required target over its checkpoint's sensor.
10. **Landmarks.** The Big Wheel stands in the sea north of the pier head with the champ's
    portrait on its hub, and the coaster's loop rises over the sea east of the straight. The
    ring at the pier head is lit for the title fight, with bleachers over the sea and light
    towers. The town's lights and the HOTEL SLOP and ARCADE neon line the shore behind the
    gate. A lighthouse stands on the far headland. None of them is a route.
11. **The camera never looks up.** Tall dressing stays out of the space behind a fighter's
    takeoffs: the arch sign at 13 m, the gallery out on stilts, the carousel on the plaza's
    east side, the Big Wheel moved out to sea. Replay captures from every section checked
    this.

## Negative decisions

- No E, R, A or F gate: jumps and ordinary punches always complete the course (about twice
  as slow as the specials route).
- No target that only an ability reaches, and no bonus target that needs F.
- No moving platforms, turning wheel or coaster cars. The rides are scenery.
- No combat rule change outside the course: hit counts belong to course targets only.
- No route through the collapsed middle; the barrier and the sign explain it.
- No second hero landmark competing with the wheel; the ring is the destination.

## Palette & lighting

Dusk: a low orange sun sets in the west behind the old pier, and the sky fades from gold to
violet. Walkable tops stay mid-toned bleached planks so the low sun never clips them; the
old pier is charred brown, not black. FightGuy's royal blue, champion gold and headband red
mark what the champ paid for. The fair's bulbs, the wheel's lights and the town's windows
glow on StageNeon for the match camera's bloom; a cool sky fill keeps east faces readable.
The balloons are unlit gold so they read against both sea and sky.

## Camera vantages

- North from the gate: the midway, the arch, the Big Wheel and coaster on the horizon.
- West from the plaza takeoff: the balloon line, the pilings and the old pier.
- East from the old pier head: the ticket booth, the wheel deck and the VIP tower.
- East up the tower's west face (the Rising Dragon) and south from its top.
- East along the coaster straight; south-west from the pier head into the ring.
- Reverse views at the checkpoints and low views at the piling and buoy edges.

## Build (2026-10-18)

Implemented: the shell (30 boxes, 28 targets, 3 checkpoints, the ring finish), the Shared
multi-hit course targets, the dressed pier, the course look and sounds, and the Arcade course
entry. Scripted FightGuy inputs on the baked arena finish by specials (3133 ticks, 52.2 s,
using A, E, R and F) and by jumps and punches alone (6188 ticks, 103 s). The wheel tower
rejects a double jump and E alone, the collapsed middle stops a Cyclone Kick at every timing
tried, and one Fist of Fury breaks each bonus target. The Unity replay of the specials route
completed with 28 targets, no falls and three saves; the review replays show each pop, bonus
hit, bonus break, checkpoint save, the all-clear spotlight and the finish. The captures are
agent-reviewed implementation evidence, not the owner's acceptance; the sounds are unheard.

## Authoring and acceptance

The shell lives as a `SPEC` in [`tools/blender/fightguy_pier/layout.py`](../../../tools/blender/fightguy_pier/layout.py)
and reaches the source scene through `sloparena.stage.shell-apply`, then the normal bake.
The [production contract](fightguy_pier.md) names the artifacts and checks. Scripted Shared
routes prove reachability with the real cooked FightGuy kit; human difficulty, camera and feel
are the owner's verdict after playing.
