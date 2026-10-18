---
key: table_manners
display_name: Table Manners
target: pvp
players: 2-4
variant: static
availability: accepted
competitive_intent: mixed
topology: round craft table with a central cake riser and closed cloth recovery walls
source_scene: client/Unity/Assets/Stages/table_manners/table_manners.unity
baked_arena: data/arenas/table_manners.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/table_manners.prefab
design_brief: docs/design/stages/table_manners.design.md
art_source: tools/blender/table_manners
human_review:
  owner: project maintainer
  process: normal host/lobby/character-select/stage-select flow with two and four players
---

# Table Manners

Tiny fighters brawl on a child's round craft table around a play-dough birthday
cake. The maintainer selected the kids' craft-room setting, centre cake layout and
floor-length tablecloth on 2026-10-16. The [design brief](table_manners.design.md)
records the approved implementation scope. The maintainer accepted the stage on 2026-10-17.
It supersedes the former green coffee-table living room and its vendor art.

## Gameplay shell and intent

The table is the main combat floor. Its central low cake creates a choice between
circling to flank and jumping onto a contested high position; the surrounding
floor remains the larger fight space. The cake is a solid static drum, not a
pass-through platform. Four diagonal spawns face inward, with opposite corners
first for two-player matches. Full cloth walls remove the former route beneath
the slab and keep deep recovery on a clear outside face.

The canonical source scene contains `Stage_table_manners`, `GameplayGeometry`,
`SpawnPoints` and `AuthoringAids`. Original `TableMannersCraftTable.asset` and
`TableMannersPlayDoughCake.asset` supply the sparse collision meshes; four empty,
ordered `SpawnPoint` markers provide starts. The bake derives collision, heightmap,
bounds and blast planes for Shared, GameServer and client prediction. The
[layout reader](../../../tools/blender/table_manners/layout.py) verifies the baked
triangles, spawn ordering/yaw and derived boundaries against the selected shell.
No Unity Collider, animation or cosmetic effect owns gameplay.

## Presentation and reproduction

Original headless Blender source lives in
[`tools/blender/table_manners/`](../../../tools/blender/table_manners/README.md),
using the shared stagekit. Gingham, flush paper drawings, stickers and glitter
leave the functional tabletop readable. Layered play-dough and googly eyes explain
the central platform. The distant bedroom has a bunk bed, art corner, block toys,
train, chairs, bunting and balloons; the north window opens onto a garden and sky.
Room-floor clutter is below the lower death plane, and distant furniture stays
outside both the blast corridor and the checked camera envelope.

`sloparena.stage.art-apply --stage table_manners` validates the generated source/arena
hash stamp before applying the materials, lights and atmosphere from
`tools/blender/table_manners/presentation.py` and replacing the old presentation roots. The
prefab stays identity-aligned, carries its own daylight and atmosphere, and uses
StageVertexLit plus StageNeon. Animated decorations remain cosmetic. Its
`Stage Lights` rig is the only lighting in PvP and Training alike: Training switches
its own scene lights off for a stage with that rig.

The generated FBX/textures/materials and runtime Resources prefab are local and
ignored; rebuild and apply them through the documented gateway workflow. Original
collision mesh assets, source scene, generator source and baked arena are
versionable. The old local room/source were backed up before replacement in
`.stage-authoring-cache/table_manners/craft-20261016/backup/`. No vendor source
asset is modified or required by the new presentation.

## Verification and review

The source shell was rebuilt, baked and inspected in the initial implementation.
The subsequent art polish preserves source-scene, collision-mesh and arena hashes.
Focused Shared coverage checks the baked shell, floor/cake heights, all roster
capsules against cloth/cake sides, free outward recovery, and one-jump access to
the cake. Shared and Server builds run with `SkipUnityPluginCopy=true`.

Local implementation evidence lives in
`.stage-authoring-cache/table_manners/craft-20261016/`. The final inspection report
there is the source of structural counts, renderer/material/light metrics and six
shell-overlay views. Generated build reports record face clearance and geometry
digests. These metrics and renders do not establish packaged-player performance.

Human visual review must include free camera orbit, standing on the cake, edge
readability and low recovery views. External PvP acceptance must exercise two and
four players, all spawns, floor/cake fighting, recovery, blast boundaries and
respawns through the normal multiplayer flow. Agent verification does not perform
or certify that review. The updated implementation remains provisional until the
maintainer completes it.


The final stage-only PvP and Training gameplay-camera passes are in
`tmp/table-manners-craft/final-balanced-pvp/` and
`tmp/table-manners-craft/final-balanced-scene/`. A separate native Training input
sequence jumps from the floor and lands on the cake; its fresh cake-top frame is
in `tmp/table-manners-craft/cake-native/`. The full intermediate-bearing and low
Edit Mode sweep is in `.ability-lab-cache/table-manners-craft-20261016/orbit-final/`;
it checks structure before the final isolated material-value adjustment. The
production report records conformance for every numbered design decision and
separates those checks from human acceptance. Native global inspection renders
are framed by the far sky, so use the closer orbit and gameplay views to judge
the tabletop. Final changes also fix the capture tool's requested-angle drift;
the camera input component is suspended only within its own capture and restored
on exit.
