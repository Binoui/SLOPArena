# Log Off campsite

Original procedural presentation for the split chopping stump and its two
bracket-fungus recovery shelves, surrounded by an ant-scale campsite at sunset.
The source geometry, shaders and textures are stage-authored; no vendor pack is
required. See the [design decisions](../../../docs/design/stages/log_off.design.md)
and [production contract](../../../docs/design/stages/log_off.md).

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/log_off/build.py -- \
  --out /tmp/log-off-generated \
  --report .stage-authoring-cache/log_off/campsite --preview
```

`layout.py` records the gameplay specification and checks the baked `.arena`
triangles, spawns and blast planes before generating any art. `--spec-only`
requires `--no-export`; it cannot produce importable art for an unbaked shell.
The build writes an FBX, stump-top PNG, atlas, detail PNG and source/hash stamp.
`build-report.json` records geometry digests, planned triangle budget, AO timing,
actual-triangle corridor checks and playable-surface extents. Generation stays
outside `Assets/`. Headless Blender does not open an interactive Blender session.

Apply the build with one gateway call under your own bounded lease; it copies only the
changed files into `Assets/Stages/log_off/Presentation/Campsite/` and imports only them:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage log_off --from /tmp/log-off-generated
```

[`presentation.py`](presentation.py) holds what it applies: the six `StageVertexLit`/`StageNeon` materials, the sunset sun, campfire, lantern and tent lights (placed by `camp.py`) and the dusk atmosphere. It rejects a stamp
whose source-scene or arena hash differs, replaces only superseded roots and updates its
own in place, and never writes gameplay data. A lighting or material change is a
`presentation.py` edit applied without a Blender run (see the
[shared kit](../README.md#shared-kit)). The accepted art and Resources prefab are committed.

`stump.py` follows the collision top, split walls and solid shelves. Bark furrows
recess into the walls; the flared foot and roots begin below the kill plane.
`forest.py` supplies ground, understory, giant trunks, canopy, distant silhouettes,
sky and fireflies. `camp.py` supplies the tent, fire, chair, gear, sign and phone.
Fire flicker and firefly travel use existing shader channels, without scripts.

Eevee previews are quick composition aids. Apply the art and inspect actual
Training captures using `sloparena.stage.views.*` before judging exposure,
colour and emission. Follow the [stage-authoring workflow](../../../skills/sloparena-stage-authoring/SKILL.md)
for native inspection, orbit review and human PvP acceptance. A successful render
or clearance check does not establish player-build performance or playability.
