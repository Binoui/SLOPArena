# Kaboom Quarry (Manki's Character Course)

Source for `manki_quarry`, Manki's solo course in a flooded demolition quarry. The approved
scope is the [design brief](../../../docs/design/stages/manki_quarry.design.md); the
[production contract](../../../docs/design/stages/manki_quarry.md) lists the artifacts and
checks.

- `layout.py` keeps the whole shell as a `SPEC`: route pieces, target islands, the crane's
  rock, the start, 18 crates, three checkpoints and the finish. Every solid is a box from
  the KO plane (y = −0.2) to its top. `--unity-shell` writes meshes, `Spawn_01` and the
  course markers for `sloparena.stage.shell-apply`; the strict run checks the bake.
- `build.py` builds Manki Demolition's quarry (brief section "Art pass") and checks every
  part against its role before exporting:
  - `pieces.py`: every walkable top as one face on the ground texture, and each piece's
    sides dressed as what it is (concrete with bumpers, drum-stack pontoons, granite with
    wired drill holes, the crusher shed, conveyor tower, hopper, dump truck, gravel crib,
    rafts, buoy, rock islands), the Blast Face's target board and the props on tops.
  - `landmark.py`: the crane on the Last Pillar, the low dynamite bundle with its lit fuse
    (the declared exception) and the detonator at the finish.
  - `quarry.py`: the rounded terraces outside the blast corridor, Mount Manki in the north
    wall and the yard on the low shore bench (signs with chasing bulbs, excavator, TNT
    water tower, drums, beacons, the fresh blast) and the rim smoke.
  - `water.py`: the water with foam and shallows in its vertex colours, flotsam and the
    duck.
  - `parts.py`: shared shapes (wall grids, slabs, drums, cones, tyres, beacons, sparks,
    puffs) and paint without a background: googly eyes, spray text in stagekit's font,
    boards and stickers for atlas cells.
- `textures.py` draws the ground (every top, planar), the atlas (signs, billboard, crate
  and dynamite labels, corrugated metal, planks; a white block for plain faces and a lamp
  block for glowing ones), the tiling strata, detail and ripples. `palette.py` holds the
  colours.
- `presentation.py`: dusty afternoon light from the south-west, the five materials
  (`MQ_Lit`, `MQ_Ground`, `MQ_Rock`, `MQ_Glow` on StageNeon, `MQ_Water`) and shadows. Review
  captures come from the course replay runner; `sloparena.stage.views.*` refuses courses.

The course's crates, breaks, checkpoint frames, flares, finish and sounds are not in the
FBX: `sloparena.presentation.course.quarry.build` writes them (`Resources/Courses/manki_quarry`)
and `python3 tools/sfx/quarry_course_clips.py` makes the clips.

A layout change is a gameplay change:

```bash
python3 tools/blender/manki_quarry/layout.py --unity-shell > /tmp/manki-quarry-shell.json
# one gateway batch: shell-apply (add --create for a fresh checkout), then bake
#   ["sloparena.stage.shell-apply", "--stage", "manki_quarry", "--from", "/tmp/manki-quarry-shell.json"]
#   ["sloparena.stage.bake", "--stage", "manki_quarry"]
python3 tools/blender/manki_quarry/layout.py          # strict: the bake matches SPEC
dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true --filter MankiQuarryRouteTests
```

Then rebuild and apply the art:

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_quarry/build.py -- \
  --out /tmp/manki-quarry-generated --report .stage-authoring-cache/manki_quarry/quarry
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage manki_quarry --from /tmp/manki-quarry-generated
```

Move a crate with its island, and keep the route test's waypoints in step: a leg that
walks to a takeoff edge, jumps and lands is the calibration, not the picture.
