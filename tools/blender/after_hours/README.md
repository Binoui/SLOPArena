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
- `SLOPArena/StageNeon` is an unlit night shader with a glow mask and per-vertex
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
blender -b --factory-startup --python-exit-code 1 --python tools/blender/after_hours/build.py -- \
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

Apply the build with one gateway call under your own bounded lease; it copies only the
changed files into `Assets/Stages/after_hours/Presentation/Neon/` and imports only them:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage after_hours --from /tmp/after-hours-generated
```

[`presentation.py`](presentation.py) holds what it applies: the four materials, the
night light rig, the stage atmosphere and the fight-night poster face. It rejects a
stamp whose source-scene or arena hash differs, replaces only previous art roots and
updates its own in place, and prunes orphaned sub-assets. Inspect with
`sloparena.stage.inspect` without rebaking. The accepted art and Resources prefab are
committed. Judge appearance from real gameplay-camera captures with the
`sloparena.stage.views.*` batch in the
[stage-authoring workflow](../../../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review);
it handles the Play Mode setup and restores the Editor afterwards.

The collision shell predates the shared `stagekit.shell` format: `layout.py
--unity-shell` writes the roof outline and risers for the original eval rebuild, which
created the scene from scratch with cube risers. Port `layout.py` to `stagekit.shell`
(meshes for the roof and both risers) before the shell next changes, then rebake and
rerun the arena tests; until then the baked arena stays as accepted.

## Fight-night billboard artwork

The hero arena screen now uses the approved four-character fight-night poster at
`Assets/Stages/after_hours/Presentation/Neon/AfterHoursFightNight.png`. It was
generated from the project's existing roster portraits; the adjacent artwork JSON
records the references and intent. The rest of the city's signs remain procedural.

The 1254×1254 poster has its own texture/material so a close camera does not magnify
the old 256×256 atlas cell. The `posters` entry in `presentation.py` names that cell:
`sloparena.stage.art-apply` finds the two triangles belonging to `arena_tonight` in the
generated atlas UVs, derives a matching poster face and installs it within the existing
frame whenever the poster PNG is present. It does not rebuild the FBX, alter the city,
or write the authoritative scene/arena.

The fixed poster explicitly supplies zero animation/travel UV channels to StageNeon
and disables its material clock. This keeps the artwork rigid and steadily lit while
the surrounding city retains its sign and traffic animation.

Apply under your own bounded gateway lease, then judge it in Unity. The PNG is
committed with the rest of the accepted art.
