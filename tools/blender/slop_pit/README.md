# Slop Pit fight ring

Original procedural presentation for the Slop Pit PvP stage: a raised canvas ring on
a ringside floor, standing on a tower in a pit of glowing slop, surrounded by
terraced scaffold stands packed with fans, a lighting rig and a SLOP PIT neon sign.
It is the key art's underground fight club made playable. No downloaded art is used.
The approved scope is the [design brief](../../../docs/design/stages/slop_pit.design.md).

It uses the [shared kit](../README.md#shared-kit): Python geometry in headless
Blender, vertex colours with baked AO, generated textures, one FBX and an apply
command. What is specific to this stage:

- `layout.py` keeps the approved ring-and-ringside `SPEC`, emits the collision meshes
  for the source-scene rebuild (`--unity-shell`) and refuses to build art unless the
  baked `data/arenas/slop_pit.arena` matches it triangle for triangle.
- Fans are built by `parts.fan` and hop and pump their fists through the
  `SLOPArena/StageNeon` bob channel (`Travel` = height and Hz × 10 + phase with a
  zero `TravelDir`); phones, bulbs, fire, neon and slop use the blink, chase, pulse
  and flicker channels. Nothing animated needs a script.
- Poster and banner art crops the roster portraits in
  `client/Unity/Assets/Resources/UI/Portraits`; lettering uses the bundled Archivo
  Black and Space Mono fonts.

Modules: `layout.py` (shell spec and arena check), `palette.py`, `textures.py` (ring
canvas, floor, atlas, detail), `parts.py` (stand frames, chain-link, chains, fans),
`ring.py` (floor, ring, skirt, tower, footing and walkout bridge), `venue.py` (slop,
pit walls, stands, haze, fences, crowd, rig, signs, corner towers), `build.py`
(checks, AO, export) and `preview.py` (Eevee views at the PvP camera's orbit).

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/slop_pit/build.py -- \
  --out /tmp/slop-pit-generated \
  --report .stage-authoring-cache/slop_pit/ring --preview
```

`python3 tools/blender/slop_pit/build.py --dry` runs the geometry and clearance
checks without Blender in about a second. `--spec-only --no-export` previews an
unbaked change to `SPEC`. A layout change is a gameplay change: rebuild the
collision source scene from `python3 layout.py --unity-shell`, bake, update
`tests/Shared.Tests/SlopPitArenaTests.cs`, then regenerate the art.

The report checks that ring and floor dressing stays inside its footprint at its
walkable top, that wall dressing inside the blast corridor stands at most 2.5 cm
proud, that nothing intersects the corridor, and that nothing within 15 m of it (the
orbit camera's reach) rises above two metres under the kill plane or below 16 m over
the top plane. It does not establish collision, performance or acceptance.

To apply, take your own bounded canonical Editor lease under the
[coordination protocol](../../../docs/contributing/unity-editor-coordination.md).
Copy the six generated files into `Assets/Stages/slop_pit/Presentation/Ring` inside
that lease, refresh, then run `sloparena.stage.art-slop-pit`. It rejects a stamp
whose source-scene or arena hash differs, replaces only the previous factory roots
and its own, configures the five materials, the key/overhead/slop lights and the
stage atmosphere, and prunes orphaned sub-assets. Inspect with
`sloparena.stage.inspect` without rebaking, and judge the look from real
gameplay-camera captures with the `sloparena.stage.views.*` batch in the
[stage-authoring workflow](../../../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review).

For HUD-free shots (the stage-select thumbnail, recovery, pit and ringside views),
run `review_cameras.eval.cs` with `eval_file` inside that Play Mode batch and capture
each camera with `sloparena.capture.game-view --source camera --camera <name>`.

The Resources prefab and generated FBX/textures/materials stay local/ignored; the
stamp and `.meta` files are tracked so references survive a rebuild.
