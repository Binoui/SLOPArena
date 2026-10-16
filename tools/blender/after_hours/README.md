# After Hours neon city

Original procedural presentation for the After Hours PvP stage: a rooftop club in a
living night city. The fight roof, its two performance risers and the club tower are
dressed to the authoritative shell; the surrounding streets, traffic, elevated train,
expressway, river bridge, skyline and sky are background. No downloaded art is used.
The approved visual scope is the [design brief](../../../docs/design/stages/after_hours.design.md).

It uses the [shared kit](../README.md#shared-kit) like the Sanctuary: Python
geometry in headless Blender, vertex colours with baked AO, generated textures, one
FBX and an apply command. Two parts are specific to this stage:

- `layout.py` keeps the approved broad-roof `SPEC` and reads the baked
  `data/arenas/after_hours.arena` (the roof is a mesh, not boxes). A normal build
  refuses to run unless the arena's tops, walls, spawns and blast planes match `SPEC`.
- `SlopArena/StageNeon` is an unlit night shader with a glow mask and per-vertex
  animation channels exported as extra UV maps (`stagekit/geo.py` documents them):
  blinking, pulsing, chasing and flickering signs, slow window changes and looping
  traffic. Nothing animated needs a script. The match camera's ACES grade and bloom
  (threshold 1.05) turn strong emission white, so glow stays near 1.4 and is judged
  in Unity captures, not Eevee previews.

Modules: `layout.py` (approved shell and arena check), `palette.py`,
`font.py` (original monoline sign font), `signs.py` (neon text, blade signs,
billboards, screens, bulbs), `roof.py` (fight roof, risers, club tower),
`city.py` (streets, traffic, buildings, transit, river, sky), `textures.py`
(billboard/screen atlas and detail grit), `build.py` (checks, AO, export) and
`preview.py` (Eevee views at the PvP camera's 10–15 m orbit).

Build with the installed Blender:

```bash
blender -b --factory-startup --python tools/blender/after_hours/build.py -- \
  --out /tmp/after-hours-generated \
  --report .stage-authoring-cache/after_hours/neon --preview
```

`--spec-only --no-export` previews an unbaked change to `SPEC`. A layout change is a
gameplay change: rebuild the collision source scene from `python3 layout.py
--unity-shell`, bake, update `tests/Shared.Tests/AfterHoursArenaTests.cs`, then
regenerate the art.

The report checks that roof and riser dressing stays inside its footprint and never
rises above a walkable top, that wall dressing inside the blast corridor stands at
most 2.5 cm proud, that the marquee/blade sign/podium stay below the kill plane, that
no background face intersects the blast corridor, and that nothing within 25 m of
the roof (the 15 m orbit camera plus the 10 m side margin) rises above two metres
under the kill plane. It does not establish collision, performance or acceptance.

To apply, take your own bounded canonical Editor lease under the
[coordination protocol](../../../docs/contributing/unity-editor-coordination.md).
Copy the four generated files into `Assets/Stages/after_hours/Presentation/Neon`
inside that lease, refresh, then run `sloparena.stage.art-after-hours`. It rejects a
stamp whose source-scene or arena hash differs, replaces only previous art roots,
configures the four materials, the night light rig and the stage atmosphere, and
prunes orphaned sub-assets. Inspect with `sloparena.stage.inspect` without rebaking.

The Resources prefab stays local/ignored. Its art can be reconstructed from this
source, the generated files and the apply command. Judge appearance from real
gameplay-camera captures with the `sloparena.stage.views.*` batch in the
[stage-authoring workflow](../../../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review);
it handles the Play Mode setup and restores the Editor afterwards.
