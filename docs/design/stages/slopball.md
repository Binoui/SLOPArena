---
key: slopball
display_name: Slopball Court
purpose: arcade
variant: static
intent: playful
---

# Slopball production contract

Follow the locked [design brief](slopball.design.md): the Night Court, a floodlit
chain-link cage inside a graffiti warehouse. This is a cosmetic pass over the accepted
solo court. Preserve the collision source, baked arena, spawn marker, shot settings and
normal fighter behaviour. The floor, net and shell source meshes keep their asset
identities; the Night Court is separate original presentation.

Collision source: `Assets/Stages/slopball/slopball.unity`. Runtime artifact:
`data/arenas/slopball.arena`. Cosmetic prefab: `Assets/Resources/Stages/slopball.prefab`.
The normal Arcade entry loads this presentation through the existing stage path. The
sender remains the existing `Ball Sender` transform driven by Shared's shot origin, with
its club dressing.

The art comes from [`tools/blender/slopball`](../../../tools/blender/slopball/README.md)
(headless Blender, original geometry and textures, no external packs) and
`sloparena.stage.art-slopball`, which verifies the generator stamp against the source
and arena hashes. Everything a ball can touch lies on a baked plane: dasher-board faces
and chain-link on the walls, the cargo net on the ceiling, the court net in its slab.
The build rejects any other geometry inside the room beyond 2.5 cm of a plane. The
prefab carries no Colliders and no gameplay authority. `SlopballCourtShow` reads
Slopball state and events: the combo board flips to the live combo (capped at 999) and
the floods pop briefly on a return.

Verify source and baked hashes before and after a pass, and inspect the prefab with
`sloparena.stage.inspect` without rebaking. Judge the look from
`sloparena.stage.views.*` captures (`prepare --stage slopball` runs the Arcade scene
with FightGuy and live serves); the camera's pitch is 0–45°, so check the default and
level views. Keep structural evidence and captures in the ignored stage cache. Solo
human visual review remains separate from the normal PvP acceptance process.
