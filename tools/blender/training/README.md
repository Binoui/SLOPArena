# Crash-Test Lab

Training combines the original Crash Pad with a western Drill Yard, a 3 m trench,
a low shelf and three eastern recovery islands. The maintainer's
[locked round-2 brief](../../../docs/design/stages/training.design.md) owns the design.
The [production brief](../../../docs/design/stages/training.md) records conformance,
measured movement exceptions and pending human acceptance.

`layout.py` owns one `SPEC` table of eleven named solids. Each row supplies the mesh
asset, footprint, top and bottom. Island rows also supply the gap. Spawn surface heights,
collision meshes, bounds and blast margins derive from those rows. The strict check
compares the actual baked triangles, spawns and KO planes with that table.
`TrainingArenaTests` reads the same table through `layout.py --spec-only` and resolves
every fighter from the cooked roster manifest. No second geometry table lives in tests.

`deck.py` builds every cap and plated face from a solid row, with braced supports below
the kill plane for deep platforms. The shelf has a visible underside and no supports.
`hall.py` derives the hall, foam basin, overhead trusses and curtains from the baked KO
planes and retains all four wall set pieces. `parts.py` owns the reusable lab parts.
`textures.py` paints the shared world-coordinate floor atlas, targets, lane, gap labels,
arrows, trench stencil and height/depth plates. `palette.py` names the neutral colours.
`presentation.py` owns materials, shadows, the directional light, atmosphere and seven
review cameras. `preview.py` provides the same vantages in Eevee as an optional aid.
Unity captures decide appearance. Fonts are the project's OFL Archivo Black and Space Mono.
There are no external environment assets or stage-specific Unity scripts.

Run the shell before art. Save the three commands in one gateway batch, with the
inspection's required `--output` argument.

```bash
python3 tools/blender/training/layout.py --unity-shell > /tmp/training-shell.json
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch /tmp/training-shell-batch.json
python3 tools/blender/training/layout.py
dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter 'FullyQualifiedName~Training|FullyQualifiedName~TutorialProgress|FullyQualifiedName~ArenaShipping|FullyQualifiedName~BlastZone'
```

The batch contains these commands.

```json
{"commands":[
  ["sloparena.stage.shell-apply","--stage","training","--from","/tmp/training-shell.json","--spawns","move"],
  ["sloparena.stage.bake","--stage","training"],
  ["sloparena.stage.inspect","--stage","training","--output",".stage-authoring-cache/training/inspection.json"]
],"restore":[]}
```

After the bake, strict check and tests agree, generate and apply the art.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/training/build.py -- \
  --out /tmp/training-generated --report .stage-authoring-cache/training/build
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage training --from /tmp/training-generated
```

Add `--rough --no-bake` for a first massing pass. `--preview` adds optional Eevee renders.
The 120,000-triangle budget, mesh-health report, geometry digest, per-face corridor
checks and source/bake hashes are part of the reproducible build. Cap paint is coplanar;
face relief stays within 3.5 cm of its authoritative solid. Walls stand 13 m beyond the
curtains. Background geometry stays outside the open blast corridor, including above
its 23 m top and below its -12 m kill plane.

Use the stage-authoring skill's `views.prepare`, `editor_play`, `views.capture`,
`views.cameras`, `views.stop` and `views.restore` batch through your own gateway lease.
Run `tools/capture_exposure.py` on each output directory. Install the `thumbnail` camera
image with `sloparena.stage.thumbnail`. Keep the generated art and prefab ignored until
maintainer acceptance. Never modify `artifacts/training-greylab-20261029/`.

The round-4 evidence is under `tmp/training-views/round4-*`. The final gaps are 4, 8 and
13.5 m. Manki, Bashie and Salamander can double-jump the far gap even at the maximum
permitted offset. The SPEC records those exceptions. Ledge grabs are disabled in the
current game (`CONTEXT.md`, Recovery), so the trench and the islands are recovery drills
without a grab.
