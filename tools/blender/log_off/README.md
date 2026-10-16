# Log Off campsite

Original procedural presentation for the split chopping stump and its two
bracket-fungus recovery shelves, surrounded by an ant-scale campsite at sunset.
The source geometry, shaders and textures are stage-authored; no vendor pack is
required. See the [design decisions](../../../docs/design/stages/log_off.design.md)
and [production contract](../../../docs/design/stages/log_off.md).

```bash
blender -b --factory-startup --python tools/blender/log_off/build.py -- \
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

After C# compilation has settled, copy the five generated files into
`Assets/Stages/log_off/Presentation/Campsite/` inside your own bounded gateway
lease, then run:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-log-off
```

The command verifies source/arena hashes, remaps six materials to the existing
`StageVertexLit` and `StageNeon` shaders, applies lighting and atmosphere, and
replaces only the old woodland roots and its own campsite roots. It preserves the
prefab GUID, collision source and baked arena. Generated assets and the Resources
prefab stay local under the project's stage-art ignore rules.

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
