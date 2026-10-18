# Slopball Night Court

Original procedural presentation for the Slopball Arcade court: the club after closing,
a floodlit chain-link cage inside a graffiti warehouse. The dasher boards, chain-link,
cargo net and court net lie on the baked bounce planes; the warehouse, murals, flood
rig and props stand outside the 40 x 40 x 20 room. A hand-flipped combo board on the
sender wall shows the live combo, and the floods pop on a return. No downloaded art is
used. The approved scope is the [design brief](../../../docs/design/stages/slopball.design.md).

It uses the [shared kit](../README.md#shared-kit): Python geometry in headless Blender,
vertex colours, generated textures, one FBX and an apply command. Specific to this court:

- `layout.py` keeps the accepted room `SPEC` and refuses to build unless the 18 baked
  collision triangles, the start and the blast planes of `data/arenas/slopball.arena`
  match it. The build's clearance check rejects anything standing inside the court
  except the floor paint, the net within its 10 cm slab, light beams, and dressing at
  most 2.5 cm proud of a wall or the ceiling plane.
- `SLOPArena/StageNeon` is unlit, so `lighting.py` bakes the night into vertex colours:
  flood pools, light bounced off the court, mural wash lights, sodium lamps and neon
  spill. The apply command restores the baked range through StageNeon's `_Base`. The
  court floor is `SLOPArena/StageVertexLit` with only the flood pools as vertex colour,
  so the fighter's and the ball's real shadows land on it.
- The light shafts are additive fins on URP Particles/Unlit (vertex colour carries the
  fade). The camera's orbit pitch is 0–45°, so it never looks up: everything that must
  read sits in the band it sees (about 1–6 m up the far wall at the default 15°, up to
  about 16 m when level), and the shafts carry their light down towards the court.
- The combo board's three digit cards are separate objects whose origins sit at their
  centres. `SlopballCourtShow` (runtime) flips them and selects digits by stepping
  `_BaseMap_ST` through the atlas's 128-pixel digit cells; the apply command refuses a
  stamp whose `digitCell`/`atlasSize` no longer match it.

Modules: `layout.py` (room spec and arena check), `palette.py`, `lighting.py` (baked
night light), `textures.py` (atlas, court and detail), `parts.py` (wall frames, shaded
panels, ribbons, chain-link), `court.py` (floor and net), `cage.py` (boards, fence,
cargo net, combo board), `hall.py` (walls, murals, flood rig, beams, props),
`build.py` (checks, AO, export) and `preview.py` (Eevee views at the Arcade camera).

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/slopball/build.py -- \
  --out /tmp/slopball-generated \
  --report .stage-authoring-cache/slopball/night-court --preview
```

`python3 tools/blender/slopball/build.py --dry` runs the geometry and clearance checks
without Blender in about a second.

To apply, take your own bounded canonical Editor lease under the
[coordination protocol](../../../docs/contributing/unity-editor-coordination.md).
Copy the five generated files into `Assets/Stages/slopball/Presentation/NightCourt`
inside that lease and import just those paths (a project-wide refresh can pick up
other threads' script edits), then run `sloparena.stage.art-slopball`. It rejects a
stamp whose source-scene or arena hash differs, replaces the club finish, hides the
base court surfaces and line-renderer net (kept for `sloparena.arcade.slopball.prepare`),
keeps the Ball Sender and its dressing, configures the three materials, the Stage
Lights rig and the atmosphere, and prunes orphaned sub-assets. Inspect with
`sloparena.stage.inspect --stage slopball` without rebaking, and judge the look from
`sloparena.stage.views.*` captures: `prepare --stage slopball` opens the Arcade scene
with FightGuy and live serves. The old `sloparena.arcade.slopball.finish` refuses to
run over the Night Court.

The Resources prefab and the generated FBX/textures/materials stay local/ignored; the
stamp and `.meta` files are tracked so references survive a rebuild.
