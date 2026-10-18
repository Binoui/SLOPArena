---
key: after_hours
display_name: After Hours
target: pvp
players: 2-4
variant: static
availability: accepted
competitive_intent: mixed
topology: broad chamfered club roof with two performance risers and closed recovery walls
source_scene: client/Unity/Assets/Stages/after_hours/after_hours.unity
baked_arena: data/arenas/after_hours.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/after_hours.prefab
design_brief: docs/design/stages/after_hours.design.md
art_source: tools/blender/after_hours
human_review:
  owner: project maintainer
  status: accepted 2026-10-17
  process: gameplay-camera orbit and recovery review, then normal PVP at 2 and 4 players
---

# After Hours — Neon Rooftop Club

A broad rooftop is the main combat floor of a club above a living night city. Two
equal performance risers add side high ground; a flush LED dance floor marks the
open center. Neon signage, traffic, an elevated train, an expressway and a lit river
bridge surround it. The [design brief](after_hours.design.md) holds the approved
direction and remains draft until the maintainer's review.

## Gameplay shell

The canonical scene contains one closed octagonal roof mesh
(`AfterHoursClubRoof.asset`, 30 × 24 with 3 m chamfers, top y = 0, walls to
y = −12), two static riser cubes (6 × 6 × 1.5 at x ±10.5), four ordered/tagged spawn
markers and an empty `AuthoringAids/KillPlane` at the lowest wall vertex. The baked
arena gives side blast planes at x ±25 and z ±22, a top plane at 21.5 and the kill
plane at −12. Keep the KillPlane marker aligned with the walls when rebaking.

The walls are authoritative Shared collision: a fighter knocked off falls along a
solid face and recovers with ordinary jumps and kit mobility. They add no climbing,
wall jumping, sticking, ledge grab or automatic rescue.

`tools/blender/after_hours/layout.py` holds the approved `SPEC`. The art generator
refuses to build unless the baked arena matches it. A shell change means: edit
`SPEC`, rebuild the source scene from `python3 layout.py --unity-shell`, bake,
update `AfterHoursArenaTests`, then regenerate and reapply the art.

## Presentation

All presentation is original procedural art from `tools/blender/after_hours`
([guide](../../../tools/blender/after_hours/README.md)): one vertex-coloured FBX with
baked AO, a billboard/screen atlas and a detail texture. The roof and club tower use
`SLOPArena/StageVertexLit`; the city, signs, sky and traffic use the new
`SLOPArena/StageNeon` shader, whose per-vertex channels animate signs, windows,
traffic and trains without scripts. `sloparena.stage.art-apply --stage after_hours`
verifies the generator stamp against the source-scene and arena hashes, imports the
art, and applies the materials, six-light night rig, stage atmosphere and fight-night
poster from `tools/blender/after_hours/presentation.py`. The prefab has no Colliders and no gameplay-owning components.

Background geometry stays out of the blast corridor. Within 25 m of the roof nothing
rises above two metres under the kill plane, so the 15 m orbit camera cannot clip
into scenery. Roof and riser dressing is flush with the walkable tops, and wall
dressing inside the corridor stands at most 2.5 cm proud. The Resources prefab and
generated FBX/textures/materials stay local/ignored; rebuild them from the source.

## Verification

- Native bake and inspection pass with matching source/baked hashes, four spawns,
  valid shaders/references, six local lights and zero cosmetic Colliders.
- `AfterHoursArenaTests` checks the baked shell and 128 recovery cases (all eight
  walls and eight corners, four roster capsules, two depths); ArenaShipping and the
  full Shared suite pass; the Server builds.
- The generator's clearance report finds no problems; gameplay-camera captures are
  in `tmp/after-hours-neon/pass-02/` and receipts in
  `.stage-authoring-cache/after_hours/neon-20261016/`.

Accepted by the maintainer on 2026-10-17 after the stage pass ("the maps are much
better"). Frame time and the packaged build were not measured separately.
