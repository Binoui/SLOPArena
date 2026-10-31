# Two-Faced Manor generation

The [locked brief](../../../docs/design/stages/nyx_manor.design.md) approves the
pink front, black gothic roof and two ravine crossings to Nyx's graveyard.
`layout.py` is the gameplay SPEC. `build.py` reads and verifies the bake before
exporting original cosmetic geometry. `presentation.py` owns materials, lights,
atmosphere and review cameras. `parts.py` and `targets.py` retain the existing
Nyxxie junk target library and comic effects.

## Rebuild the shell

Run in the canonical checkout. All Unity commands require your own gateway
lease under the [Editor protocol](../../../docs/contributing/unity-editor-coordination.md).

```sh
python3 tools/blender/nyx_manor/layout.py --unity-shell > tmp/nyx-manor-shell.json
SLOPARENA_COURSE_SHELL=$PWD/tmp/nyx-manor-shell.json dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter FullyQualifiedName~NyxManorTests
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.shell-apply --stage nyx_manor --from tmp/nyx-manor-shell.json --remove B2W,B2E,B3,CryptLedge
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.bake --stage nyx_manor
SLOPARENA_GENERATE_RACING_LINES=1 SLOPARENA_RACING_LINE_STAGE=nyx_manor dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter FullyQualifiedName~CourseRacingLineGenerator.GenerateFromVerifiedRoutesWhenRequested
python3 tools/blender/nyx_manor/layout.py --unity-shell > tmp/nyx-manor-shell.json
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.shell-apply --stage nyx_manor --from tmp/nyx-manor-shell.json
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.bake --stage nyx_manor
python3 tools/blender/nyx_manor/layout.py
```

For a real revision, put each dependent apply, bake and inspect sequence in one
bounded batch. `tmp/nyx-course/graveyard/final-shell.batch.json` is the recorded
example. The old Crypt asset is updated in place. The four obsolete terrace mesh
assets and their scene children are removed. Local copies remain in the revision evidence.
Do not remove other source geometry or regenerate the front.

## Rebuild the art

```sh
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_manor/build.py -- --out tmp/nyx-course/graveyard/generated --report tmp/nyx-course/graveyard/final-build
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.art-apply --stage nyx_manor --from tmp/nyx-course/graveyard/generated
```

The polish budget is 180,000 source triangles. The recorded before and after frame-time checks are in `tmp/nyx-course/polish/REPORT.md`. The generator refuses a
mismatched bake, a budget overrun or background faces inside the blast corridor.
The aqueduct's openings and sea-rock sides come from collision geometry.
Walkable tops match the shell and remain brighter than walls. The cliff ramp
has a solid cliff base. Fencing and grave props stay inside supported side plots.
The moon uses continuous per-vertex surface colour and a generated `MoonHalo.png` alpha texture. `world.py` also generates the grit and ripple textures. The sea uses the existing StageWater shader.
The transparent halo material uses the installed URP Unlit shader.

`--rough` omits garden and detailed dressing for the first lighting check.
Both exports retain explicit StageNeon animation and travel UV channels.
A presentation-only edit can rewrite the JSON without exporting the FBX:

```sh
python3 tools/blender/stagekit/presentation.py nyx_manor --out tmp/nyx-course/graveyard/generated
```

The existing target art need not be rebuilt for this geometry revision. If its
source changes, export `targets.py` and use the existing typed
`sloparena.presentation.course.nyx-manor.build` command. The initial install
example is `tmp/nyx-course/art/install-final.batch.json`. Save only owned assets.

## Verify the course

```sh
dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter 'FullyQualifiedName~NyxManorTests|FullyQualifiedName~CourseRacingLineTests|FullyQualifiedName~CourseFinishabilityTests|FullyQualifiedName~CharacterCourseTests|FullyQualifiedName~ArenaShippingTests'
SLOPARENA_EXPORT_COURSE_INPUTS=$PWD/tmp/nyx-course/graveyard/scenarios dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter FullyQualifiedName~NyxManorTests.OrdinaryRouteExportsReplayWhenRequested
bun scripts/sloparena-replay.ts --scenario tmp/nyx-course/graveyard/scenarios/nyx-manor-nyx.json --output tmp/nyx-course/graveyard/replay-nyx-high-final --project-path client/Unity
bun scripts/sloparena-replay.ts --scenario tmp/nyx-course/graveyard/scenarios/nyx-manor-fightguy.json --output tmp/nyx-course/graveyard/replay-fightguy-low --project-path client/Unity
```

Replay output directories must be fresh. Nyx uses the aqueduct. FightGuy uses
the low sea rocks and cliff stairs. Both scenarios capture the start,
scaffolding, roof, ridge, crossing, graveyard and crypt door. A short forward tail
crosses the finish sensor after the final camera pose, so input quantization cannot
leave the native replay just outside its boundary.
Both require 17 targets, four saves and zero falls.

Arcade rejects `stage.views.prepare`. Use the existing replay lifecycle, then
`sloparena.stage.views.cameras` and `sloparena.stage.views.frametime` in the same
bounded batch, declaring replay stop and restore. The recorded example is
`tmp/nyx-course/graveyard/review-installed.batch.json`. It includes front, back, low,
recovery, crossing, graveyard, moon and thumbnail views, 24 elevated angles,
eight low angles and eight low ravine angles. Camera poses live in
`presentation.py`. The current low rim uses X radius 39 m and Z radius 82 m at y -1, aimed at y 10. The earlier 33/78 ellipse put its front diagonal probes inside the lawn; the polish run replaces those captures.
The elevated orbit uses radius 100 m at y 48, except the 345-degree probe at y 68
to avoid the distant moon. The ravine ellipse stays between the manor and graveyard;
its north and south probes move to x -8 to avoid the aqueduct piers.
`probe-fix.batch.json` records the corrected eleven poses.
`final-orbit-captures.json` maps the final forty views to their native captures.

```sh
python3 tools/capture_exposure.py tmp/nyx-course/graveyard/review-installed-views --json
python3 tools/capture_exposure.py tmp/nyx-course/graveyard/replay-nyx-high-final tmp/nyx-course/graveyard/replay-fightguy-low --json
```

Install the captured thumbnail in Edit Mode through `sloparena.stage.thumbnail`.
The phone board, complete report, per-fighter table and path inventory live in
`tmp/nyx-course/graveyard/`. Human play acceptance remains pending through check #31.

## Art polish modules

`surfaces.py` builds stone paving, masonry and cliff skins from the fixed shell.
`dressing.py` owns the roof relief, turret cap, graves, planting, candles and front trim.
`world.py` owns the sea, sky, moon and supported distant scenery. These modules write no gameplay data.
The original roof deck and the playable mausoleum tops stay clear. New roof volume sits on the non-route turret.
The source, arena, all 43 collision mesh assets, layout, racing line and course asset remain unchanged.
The phone comparison board and the current conformance report are in `tmp/nyx-course/polish/`.
