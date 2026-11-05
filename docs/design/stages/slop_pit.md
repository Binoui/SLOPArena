---
key: slop_pit
display_name: Slop Pit
target: pvp
players: 2-4
variant: static
authoritative_capability: null
availability: accepted
competitive_intent: mixed
topology: raised central ring on a chamfered ringside floor with closed recovery walls
source_scene: client/Unity/Assets/Stages/slop_pit/slop_pit.unity
baked_arena: data/arenas/slop_pit.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/slop_pit.prefab
design_brief: docs/design/stages/slop_pit.design.md
art_source: tools/blender/slop_pit
human_review:
  owner: project maintainer
  status: accepted 2026-10-17
  process: gameplay-camera orbit and recovery review, then normal PVP at 2 and 4 players
---

# Slop Pit — Underground Fight Ring

A raised ring is the main combat floor of an underground fight club. Fighters start
in the ring; a knockout from it lands on the ringside floor, one full jump below,
and the floor's solid walls drop to the KO line over a pit of glowing slop.
Terraced stands packed with fans, a lighting rig and a SLOP PIT neon sign surround
the pit beyond the blast corridor. The stands sit in warm darkness broken by a few
lamp pools, and big pipes pour slop from the stand fronts into the pit. The
[design brief](slop_pit.design.md) holds the approved direction. The maintainer
accepted the stage on 2026-10-17. The 2026-11-04 background pass awaits their
in-game verdict.

## Gameplay shell

The canonical scene contains a closed octagonal floor mesh
(`SlopPitRingsideFloor.asset`, 32 × 32 with 5 m chamfers, top y = 0, walls to
y = −11), an open-bottomed ring mesh (`SlopPitRing.asset`, 16 × 16, top y = 1.2),
four ordered/tagged spawn markers in the ring corners at (±5, ±5) facing its centre
and an empty `AuthoringAids/KillPlane` at the lowest wall vertex. The baked arena
gives side blast planes at x/z ±26, a top plane at 21.2 and the kill plane at −11.
Keep the KillPlane marker aligned with the walls when rebaking.

The walls are authoritative Shared collision: a fighter knocked off falls along a
solid face and recovers with ordinary jumps and kit mobility. They add no climbing,
wall jumping, sticking, ledge grab or automatic rescue. The ring's 1.2 m sides block
movement below its top; every roster fighter reaches the top with one full jump.

`tools/blender/slop_pit/layout.py` holds the approved `SPEC`. The art generator
refuses to build unless the baked arena matches it. A shell change means: edit
`SPEC`, rebuild the source scene from `python3 layout.py --unity-shell`, bake,
update `SlopPitArenaTests`, then regenerate and reapply the art.

## Presentation

All presentation is original procedural art from `tools/blender/slop_pit`
([guide](../../../tools/blender/slop_pit/README.md)): one vertex-coloured FBX with
baked AO, a ring-canvas texture, a floor texture, an atlas of posters, banners,
skirts, hoarding and signs, and a detail texture. Poster and banner art crops the
roster portraits in `Assets/Resources/UI/Portraits`; lettering uses the bundled
Archivo Black and Space Mono. The ring, floor and tower use
`SLOPArena/StageVertexLit` under the stage's own key and fill lights; the venue uses
`SLOPArena/StageNeon`, whose bob channel makes the crowd hop without scripts.
`SIDE_LIGHT` in `venue.py` is the one table of where the venue is lit. Each side lists
its warm lamp pools and its two slop falls. The wash behind the fans, the work lamps,
the green spill and the gaps in the fences and crowd for each pipe all read from it,
so moving a pool moves its lamp and its light together. Each fall has a steady body,
brightest at its centre, and thin streaks whose pulse channel runs down the fall.
`sloparena.stage.art-apply --stage slop_pit` verifies the generator stamp against the
source-scene and arena hashes, imports the art, applies the materials, lights and
atmosphere from `tools/blender/slop_pit/presentation.py`, and removes the previous
factory roots. The prefab has no Colliders and no
gameplay-owning components.

Background geometry stays out of the blast corridor, and nothing within 15 m of the
corridor rises above two metres under the kill plane, so the 15 m orbit camera cannot
clip into scenery: the stands begin 41.5 m from the centre, the slop falls hang in
front of the pit wall no closer than 41 m, and only the slop and its junk lie further
in, 13 m down. Ring and floor dressing is flush with the walkable tops,
and wall dressing inside the corridor stands at most 2.5 cm proud. The Resources
prefab and generated FBX/textures/materials stay local/ignored; rebuild them from
the source.

## Verification

- Native bake and inspection pass with matching source/baked hashes, four grounded
  spawns, 10 renderers, 5 materials, 4 local lights, valid shaders/references and
  zero cosmetic Colliders.
- `SlopPitArenaTests` checks the baked shell, flat ring and floor heights, 128 recovery
  cases, the ring sides, one-jump ring access and stepping out to ringside for all
  four roster fighters; ArenaShipping and the full Shared suite pass; the Server
  builds.
- The generator's clearance report finds no problems; receipts are in
  `.stage-authoring-cache/slop_pit/ring-20261016/` and gameplay-camera captures under
  PvP and Training lighting in `tmp/slop-pit-ring/`.

The stage's key light, a warm overhead over the ring and two green slop lights are its
only lights: the prefab's `Stage Lights` rig makes the match switch off the scene's own
directional lights, so Training, Solo and PvP look the same. (The passes before that
fix kept the canvas print darker for Training's extra lights; it still reads tan.)
Accepted by the maintainer on 2026-10-17 after the stage pass ("the maps are much
better"). Frame time and the packaged build were not measured separately.

### Background pass (2026-11-04)

The pass changed only presentation: `venue.py`, `presentation.py`, the generated art,
the prefab and the stage-select thumbnail. The source-scene and arena hashes are the
same as before (`aefdb06f…` and `0c15fbb2…`), so the gameplay shell and its tests did
not need a rerun.

- The generator builds 228,814 triangles (budget 320,000) with no clearance problems.
  The stamp's `geometryDigest` is `8ea4632d…`. Rebuilding the atlas also picked up the
  current roster portraits for the posters and banners.
- `sloparena.stage.inspect` without a rebake reports matching hashes, 38 collision
  triangles, four spawns, 10 renderers, 5 materials, 4 local lights, 227,414 imported
  triangles, no missing shaders or references and zero Colliders. Receipts are in
  `.stage-authoring-cache/slop_pit/bg-20261104/`.
- `tmp/slop-pit-bg/measure.py` splits each HUD-free gameplay capture into background
  and playfield with per-view masks. On the eight 15° views the background's detail,
  the mean luminance step between neighbouring pixels, fell from 6.3–7.3 to 3.4–4.1,
  below the playfield's 5.0–6.0. Background luminance fell from 33–38 to 22–25, at most
  3.2% of it is crushed and its brightest accents stay at p99 111–136. The nearest 40%
  of the playfield is identical. The darker fog is global, so the farthest 15%, around
  the far floor edge, is 1–5 luminance levels darker (out of about 100).
- On the north view the hero neon measures p99 199, a fall core 131 and the green
  spill behind it 80. That keeps the sign brightest and each fall brighter than the
  light it throws.
- Captures are in `tmp/slop-pit-bg/` (`cap-01` before, `pass-06` after, `-cams` for the
  review cameras). Frame time and the packaged build were not measured.

| Design decision | Result |
| --- | --- |
| 5. The pit and its falls | PASS. Two falls per side at 28.7–32.5 m along the stand, steady bodies with pulsed streaks, the old low outfalls removed. |
| 6. Fans as silhouettes in the pools | PASS. The wash peaks behind the fans' heads. |
| 7. Lamp pools, not a lit wall | PASS. Seven or eight pools per side, one lamp each, near-black between them, no bulb strings, corner washes cut to about a third. |
| 12. Calm band behind the fighters | PASS. See the detail numbers above. |
| No fall in front of the hero sign or brighter than it | PASS. |
| Green spill up the pit wall | DEVIATION. It is built, but the recovery cameras face away from the falls and barely show it. |
| Concept extra: rising green mist | Not built. No stage shader draws translucent glow. A shader for it would be its own change. |

The maintainer's in-game verdict on this pass is pending.
