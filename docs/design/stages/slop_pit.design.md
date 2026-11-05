---
key: slop_pit
status: locked
locked: 2026-11-04
approved_by: maintainer
approval_scope: "Picks of 2026-10-16: the warehouse cage-fight venue and the raised-ring layout, procedural rebuild (accepted 2026-10-17). Picks of 2026-11-04: background direction \"A + slop falls\" from the four-tab concept board (A Lights down, B Big crowd, C Into the pit, A + slop falls): relight the accepted venue into lamp pools with silhouetted fans and pour slop from the stand fronts into the pit; ring, floor and tower unchanged (\"go for A + slop falls\")"
blockout:
  - .stage-authoring-cache/slop_pit/ring-20261016/build/preview/
  - tmp/slop-pit-ring/pass-05-pvp/
  - tmp/slop-pit-bg/comp/R-lights-down-slop-falls-1.png
---

# Slop Pit — Underground Fight Ring

On 2026-10-16 the maintainer asked to remake Slop Pit with the procedural stage
process because the factory deck did not sell the underground fight ring of the
key art. From the presented options they selected the **warehouse cage fight**
venue and, with the shell explicitly open to change, the **raised ring** layout.
Those choices authorize this bounded implementation. The maintainer accepted the
built stage on 2026-10-17 ("the maps are much better"). The stage keeps the immutable key `slop_pit` and its display name.

On 2026-11-04 the maintainer said the playable area looks nice but the background
needs work. From the gameplay camera the stands read as one lamp-lit orange stripe
repeated four times, busier than the fight, and the slop never showed. From a board of
four directions painted over a real gameplay capture, the maintainer picked
**A + slop falls**: keep this venue, light it like fight night and put the slop on
screen. Decisions 5 to 7, 11 and 12, the palette and the negative decisions below
record that pick. The 2026-10-17 acceptance covers the previous background. The ring,
floor and tower it accepted do not change.

## Personality

Four fighters settle it in a raised ring over a pit of glowing slop while an underground crowd bays for someone to go in.

## Fight premise

The ring is the main event: a broad, raised canvas where all four fighters start.
Getting knocked out of the ring is not the end — the fight spills onto the ringside
floor, where one full jump climbs back in. Getting knocked off the floor means the
slop. The floor's walls are solid down to the KO line, so recovery uses ordinary
jumps and kit mobility against a wall, with no under-stage route.

## Composition decisions

Camera yaw names the direction the gameplay camera looks: yaw 0° looks north (+Z).
Coordinates live in `tools/blender/slop_pit/layout.py` and the generator modules.

1. **The ring is the hero of every view.** A 16 × 16 m ring, 1.2 m above the
   ringside floor, fills the centre from the default camera. Its top is worn
   off-white canvas with a faded SLOP PIT crown print, one red and one blue corner,
   two neutral corners and a black apron border with a yellow trim. There are no
   posts, ropes or cage panels: anything standing on the ring edge would sit in
   fighting and recovery space without collision.
2. **The ring skirt carries the joke.** Its four 1.2 m sides are a black skirt
   printed with SLOP PIT, GET IN THE SLOP, NO RULES and roster-joke sponsors (Slop
   Cola, Bonk Burger, Manki Motors, Wibou Watches), flush with the collision faces.
3. **A quiet ringside floor.** A 32 × 32 m floor with 5 m chamfers: stained,
   saw-cut concrete, a yellow ringside line 1 m outside the ring, stencils (STAY
   BEHIND THE LINE, FIGHTERS ONLY), taped cable runs and a yellow-black hazard band
   along the edge. Everything on it is flush; nothing stands on it.
4. **The tower.** The floor's recovery walls are the sides of a tower rising out of
   the slop: a hazard-striped kick plate, a calm dark band under the edge, then
   plywood hoarding — mostly plain dark sheets, some raw ply, a few tags and pasted
   fight posters built from the roster portraits — and dark concrete with I-beams
   further down.
5. **The pit and its falls.** The tower stands in a moat of green slop 13 m below the
   kill plane, with a slow sheen, bubbles, and half-sunk barrels and crates. Two slop
   falls per side pour from big rusty pipes that leave the second gallery's riser near
   the ends of each stand, run out over the first gallery and the rim fence, and drop
   in front of the pit wall into the moat. The falls are the slop the gameplay camera
   sees: sickly yellow-green, matte, with brightness waves running down them, and
   duller than the hero neon. A steel walkout bridge with bulbs runs from the west
   wall's FIGHTERS tunnel to a door in the tower, three metres under the kill plane.
6. **The crowd all round.** Beyond the camera envelope, terraced scaffold stands
   rise from the pit rim on all four sides: a tall chain-link fence with barbed wire
   at the rim, then three galleries behind chain-link railings, packed with
   low-poly fans who hop and pump their fists; a few hold up glowing phones. From
   the default camera the first two galleries fill the band just above the ring.
   Fans in a lamp pool read as black silhouettes against hot light, and fans between
   pools sit in darkness.
7. **Lamp pools, not a lit wall.** Each side has a few warm pools of light, mostly on
   the upper galleries, each under its own work lamp, with irregular spacing and
   size. Between the pools the stands fall into warm darkness. Near each fall, green
   light spills onto the stand fronts and up the pit wall. Fire barrels burn at the
   rim. There are no bulb strings along the galleries.
8. **The rig.** A lighting truss with par cans aimed at the ring runs along every
   ceiling edge; four corner truss columns carry floodlight banks and speaker
   stacks. Chains with hooks hang from the ceiling edge, and fighter and crown
   banners hang from the top galleries.
9. **One hero sign.** SLOP PIT in yellow neon with an orange crown, one buzzing
   letter and a chasing bulb frame hangs on the north stand's second gallery.
10. **Side identities.** East is the bookie (a TONIGHT'S ODDS chalkboard, BETS and
    TICKETS signs); south is the bar (a HOUSE RULES board, SLOP COLA and BONK
    BURGER light boxes); west is the walkout (FIGHTERS light boxes over the tunnel
    and on the stand).
11. **The scene world.** Below: the slop moat, the falls and the tower base. Above: a
    low concrete ceiling over the stands and an open dark void over the pit. Far: a
    few lamp pools in the dark stands, with exit signs. The fight sits inside a
    place, never on a platter.
12. **Busy here / empty here.** Busy: the lamp pools high in the stands, the north
    sign and the falls near the ends of each stand. Calm: the band of stands directly
    behind the fighters, which stays dark between pools. Empty: the ring canvas and
    ringside floor (flush detail only), the air above the ring and the pit.

## Gameplay shell

```text
                    north: SLOP PIT neon over the stand
         /‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾\
        |              ringside floor, y = 0     |
        |      +----------------------------+    |
        |      |  S3 (-5, 5)      S2 (5, 5)  |    |
  west  |      |       RING, top y = 1.2      |    |  east
  walk- |      |  S1 (-5,-5)      S4 (5,-5)  |    |  bookie
  out   |      +----------------------------+    |
        |                                        |
         \______________________________________/
                    south: the bar
   floor 32 x 32 with 5 m chamfers, walls to the kill plane at y = -11
   ring 16 x 16, a full jump for every roster fighter; spawns face the centre
```

`tools/blender/slop_pit/layout.py` (`SPEC`) records the approved shell; the
collision source scene is rebuilt from it and baked. The baked `.arena` remains the
authority: floor x/z −16..16 with 5 m chamfers (top 0, walls to −11), ring x/z
−8..8 (top 1.2), spawns (−5,−5) 45°, (5,5) 225°, (−5,5) 135°, (5,−5) 315° at
marker height 2.05; blast planes x/z ±26, top 21.2, kill −11
(`AuthoringAids/KillPlane`).

## Negative decisions

- No ring posts, ropes, cage panels, props or lights on or above the ring and
  floor, and no new platform or route.
- No background inside the blast corridor, and nothing within 15 m of it higher
  than two metres under the kill plane (the 15 m orbit camera's reach).
- No bright sign or busy pattern directly under the fight edge.
- No moving geometry or hazard in gameplay space; the crowd, neon, bulbs, fire and
  slop animate in the StageNeon shader, beyond or below the corridor.
- No second hero landmark competing with the north sign, no vendor assets and no
  real brands.
- No even wash of light across the stands and no bulb strings. Light comes from the
  lamp pools, the signs, the fire barrels and the falls.
- No fall in front of the north hero sign, and no fall brighter than it.

## Palette & lighting

Fight night underground: a near-black warm void, a high warm-white key from the
south-south-west that leaves short fighter shadows on the canvas, and a warm
overhead over the ring. The ring is the brightest thing in view. The stands sit in
warm darkness broken by a few hot lamp pools with dark silhouettes in front, the
slop's sickly green shows in the falls and the light they throw, and the hero neon is
Action Yellow. The ring canvas and floor stay neutral so fighters and their amber
and cyan rims read first.

## Camera vantages

- The four spawn views across the ring at default zoom.
- Quarter turns at 10–15° pitch: north (hero sign), east (bookie), south (bar),
  west (walkout), plus intermediate yaws.
- Ringside views from the floor band looking back at the ring and its skirt.
- Low recovery views along each wall and corner, looking at the tower and the pit.
- A zoomed-out north-east view for the stage-select thumbnail.

## Production and verification (2026-10-16)

- **Shell:** the collision source was rebuilt from `SPEC` (a closed octagonal floor
  mesh and an open-bottomed ring mesh, four spawns moved into the ring, the
  kill-plane marker unchanged) and baked: 38 triangles, matching source and baked
  hashes, clean inspection. `layout.py` verifies the baked arena strictly.
- **Tests:** `SlopPitArenaTests` covers the shell, flat ring and floor heights, 128
  recovery cases (eight walls and eight corners, four fighters, two depths), the
  ring sides, one-jump ring access and stepping out to ringside for all four
  fighters. The old layout's platform-lip regression moved to a synthetic arena in
  `StageCollisionTests`. The full Shared suite passes (2,826 passed, 2 existing
  skips) and the Server builds.
- **Art:** the generator builds about 216k triangles (about 3,060 fans) with baked AO
  and zero clearance problems. `sloparena.stage.art-slop-pit` applied it: 10
  renderers, 5 materials, 4 local lights, no Colliders; the ten factory roots and
  their orphaned sub-assets were removed, and inspection passes without rebaking.
  The stage-select thumbnail was replaced in place from a HUD-free gameplay render.
- **Captures:** gameplay-camera views with PvP lighting (`tmp/slop-pit-ring/pass-05-pvp/`)
  and Training lighting (`pass-05-training/`), steep edge and low views
  (`pass-02-edges/`) and named recovery, pit, ringside and hero cameras (`cams-04/`).
  Pass 1 fixed a blown-out canvas, confetti-bright fans, dotted floor grunge and dead
  corners; later passes fixed a slop that read as a lawn and flat grey pit walls.
  Training's arena adds three directional lights, so the canvas print keeps headroom:
  tan under PvP lighting, bright cream (not clipped) in Training. These are agent
  evidence, not human review.
- **Accepted** by the maintainer on 2026-10-17. Frame time and the packaged build
  were not measured separately.

## History

The 2026-09-17 factory deck (a 32 × 26 deck with two 2 m diagonal platforms,
perimeter walls added on 2026-09-26, LowPolyFPS6 scaffolds and worklights, and
three original scrap meshes) is superseded. Backups of its source scene, arena,
prefab, thumbnail and briefs are in
`.stage-authoring-cache/slop_pit/ring-20261016/backup/`; earlier art passes remain
in `.stage-authoring-cache/slop_pit/`.

The 2026-11-04 background pass replaced the even orange wash, the work lamp every 5 m
and the bulb strings with lamp pools, and replaced the low outfalls under the floor
edge with falls from the stand fronts. The art the maintainer accepted on 2026-10-17
is in Git history. The [production brief](slop_pit.md#background-pass-2026-11-04)
records the pass and its checks.
