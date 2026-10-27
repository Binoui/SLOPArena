---
key: hamster_habitat
display_name: Hamster Habitat
target: pvp
players: 2-4
variant: static
availability: picker (accepted 2026-10-27 from captures)
competitive_intent: playful
topology: two cages at different heights across an open gap, joined by a low tube with a junction hatch and a high tube
source_scene: client/Unity/Assets/Stages/hamster_habitat/hamster_habitat.unity
baked_arena: data/arenas/hamster_habitat.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/hamster_habitat.prefab
---

# Hamster Habitat

A gray-box of layout C, Two Cages, from the 2026-10-27 hamster habitat board. The
[design brief](hamster_habitat.design.md) records the pick and what is still open
and its acceptance. It is on the picker roster.

## Gameplay shell

[`tools/blender/hamster_habitat/layout.py`](../../../tools/blender/hamster_habitat/layout.py)
is the source of the collision: its `SPEC` builds every solid, `--unity-shell` writes the
document for `sloparena.stage.shell-apply`, and running it without flags checks the baked
arena against the layout triangle for triangle.

- Desk cage floor y 0 (x −32.5…−10.4), shelf cage floor y −3 (x 10.4…32.5), both z −10.4…10.4
  with solid walls to the KO line at y −14. The gap between them has no floor. The footprint
  is 1.3× the first gray-box (maintainer, 2026-10-27); heights and tube bore did not change.
- House roof y 4.4, a double jump from the food bowl (top y 1.2). Wheel deck y −1, one jump
  from the shelf floor; the wheel is static and open at its south face. TP roll on the
  shelf floor.
- Tubes are closed hollow solids (inner radius 2, wall 0.2, 16 sides with flat floor and
  roof facets): the low tube along z 6.5 from desk to shelf, with a roof hatch over a junction
  step (top y 0.1) at x 0; the high tube from the house roof over the gap to the wheel deck.
  Tube ends ease onto their floors because a hard kink folds the wall inside the bend.
- Spawn_01 desk and Spawn_02 shelf (one per cage for duels), Spawn_03 desk, Spawn_04 shelf.
  Spawns stay out from under the tubes: the bake grounds a spawn on the topmost surface.
- Side blast ±42.5 x, ±20.4 z; top blast 20 m above the high tube's top.

## Presentation

Concept C, the night bedroom (2026-10-27). [`build.py`](../../../tools/blender/hamster_habitat/build.py)
dresses the collision solids: deep blue storage bins filled with a procedural shavings
texture, the house with a painted door, the bowl with kibble, the deck, and the tubes and
wheel as their collision walls in clear `SLOPArena/StageGlass` under opaque hoops.
[`room.py`](../../../tools/blender/hamster_habitat/room.py) rebuilds Table Manners' bedroom at
its own scale and plan (about 32 game units per real metre, walls at x ±66 and z ±72), with the
floor 18.5 units lower so its art-desk top lands under the PEANUT bin below the KO line. The
window, door, toy box, west bookshelf, posters, cloud wallpaper over the beadboard and the
train track keep Table Manners' places; the bunk bed turns along the north wall in the same
corner, and the craft table with its cake and chairs moves south, because the camera's reach
around the bins forbids tall furniture there and the bins own the centre. The ceiling sits
9.5 units higher than Table Manners' so the camera never meets it at the top blast line.
Comparison plans: `tmp/hamster-vs-table/floorplans.png` (before) and `floorplans-after.png`.
[`details.py`](../../../tools/blender/hamster_habitat/details.py)
polishes the props: plank house walls with a framed door, window and roof trim, a ceramic bowl
rim with kibble, the deck panel, wheel rungs, the TP roll's spiral seam, moulded bin ribs and
the kid's masking-tape labels (PEANUT on the desk bin, GYM on the wheel bin).
[`clutter.py`](../../../tools/blender/hamster_habitat/clutter.py) adds the lived-in layer: a desk
hutch of books and jars, the PEANUT CRUNCH food bag, the train and a block castle, a star
nightlight and backpack by the door, fairy lights on the north and west walls, crayon drawings
lying flat on the furniture tops (below the KO line, so allowed inside the camera's reach) and
toys on the floor under the gap. [`hamster.py`](../../../tools/blender/hamster_habitat/hamster.py)
stands the giant hamster on the desk behind the left bin. The build checks every background
face against the blast corridor and the camera's reach and refuses to export on a clash.
[`presentation.py`](../../../tools/blender/hamster_habitat/presentation.py): warm lamp key
(directional, soft shadows) plus a point light at the bulb, cool moonlight, deep blue
ambient and fog. The hamster uses its own StageNeon material shaded from the lamp side:
URP gives a renderer at most two extra lights on High, so a dedicated face light was
dropped there. Review cameras `thumbnail`, `gap-south`, `gap-north`, `hamster`,
`room-east`, `drop`, `west`, `south`.

The export carries the empty `Camera See-Through Thin Walls`; `MatchBase` then lets
`CameraObstruction` cross collision walls thinner than 0.3 m. Tube and wheel walls are
0.2 m; the TP roll is 0.5 m cardboard so it still stops the camera. An Editor probe on the
baked proxy (2026-10-27): inside the low tube the camera went from 1.8–2.0 m to the full
10 m, the high tube crest 1.8 → 10 m, the wheel 4.8 → 10 m; the TP roll (1.6 m) and the
house behind a fighter (2.3 m) were unchanged. Stages without the marker keep every wall.

## Evidence (2026-10-27)

- `sloparena.stage.bake`: 6,192 triangles, four grounded spawns. `sloparena.stage.inspect`:
  no diagnostics, about 17k cosmetic triangles, no colliders, three stage lights. `layout.py` strict check passes
  (it matches triangles within 2 mm, since the bake stores float32).
- `HamsterHabitatArenaTests` (39): both tubes carry FightGuy, Manki, Wibou, Bonk and Bashie
  across in both directions; the food bowl double jump reaches the roof; the junction step's
  double jump lands on the low tube's top; one jump reaches the wheel deck; the gap is a KO.
  `ArenaShipping` passes.
- Night art (local): gameplay views `tmp/hamster-habitat-views/art-08`, review cameras
  `tmp/hamster-habitat-views/art-cameras-09` (density pass; `art-06` is the second pass,
  `art-04` the first). About 26k cosmetic triangles. Exposure:
  nothing flagged in either pass (an intermediate pass with a steeper, brighter key clipped 10–22%).

Not checked: frame time, bots, the see-through camera in live play, two- and four-player play.
Human review: the maintainer accepted the stage from captures on 2026-10-27; in-game and
two/four-player play are still to come.
