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
the pit beyond the blast corridor. The [design brief](slop_pit.design.md) holds the
approved direction; the maintainer accepted the stage on 2026-10-17.

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
`sloparena.stage.art-apply --stage slop_pit` verifies the generator stamp against the
source-scene and arena hashes, imports the art, applies the materials, lights and
atmosphere from `tools/blender/slop_pit/presentation.py`, and removes the previous
factory roots. The prefab has no Colliders and no
gameplay-owning components.

Background geometry stays out of the blast corridor, and nothing within 15 m of the
corridor rises above two metres under the kill plane, so the 15 m orbit camera cannot
clip into scenery: the stands begin 41.5 m from the centre and only the slop and its
junk lie inside, 13 m down. Ring and floor dressing is flush with the walkable tops,
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
