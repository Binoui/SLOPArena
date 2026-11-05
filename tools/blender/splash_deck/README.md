# Splash Deck fishing harbour

Original procedural presentation for `splash_deck`: a painted fishing boat anchored in
a Mediterranean harbour bay on a sunny morning. The boat's deck, hull, wheelhouse and
fish hatch dress the authoritative shell; the village and quay, breakwater and
lighthouse, olive hills and chapel, bay mouth, sea and sky surround it. No vendor art
is used. The approved scope is the [design brief](../../../docs/design/stages/splash_deck.design.md);
the [production record](../../../docs/design/stages/splash_deck.md) describes the
source/bake/presentation boundary and verification.

## Build and apply

Use the installed Blender with its Python/Pillow support. Generate outside Unity's
Assets directory; do not interrupt an interactive Blender session.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/splash_deck/build.py -- \
  --out /tmp/splash-deck-generated \
  --report .stage-authoring-cache/splash_deck/harbour --preview
```

`layout.py` keeps the boat `SPEC` and reads the baked arena; a normal build refuses
to run unless its triangles, spawns, facings and blast planes match. `--spec-only`
requires `--no-export` and is for reviewing an unbaked change. `--no-bake` skips the
AO bake for a faster pass; `--views a,b` limits the Eevee previews.

A layout change is a gameplay change: write the shell with `python3 layout.py --unity-shell
> /tmp/splash_deck-shell.json`, rebuild the collision source scene with `sloparena.stage.shell-apply
--stage splash_deck --from /tmp/splash_deck-shell.json`, bake, update `tests/Shared.Tests/SplashDeckArenaTests.cs`,
then regenerate the art.

Apply the build with one gateway call under your own bounded lease; it copies only the
changed files into `Assets/Stages/splash_deck/Presentation/Harbour/` and imports only them:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage splash_deck --from /tmp/splash-deck-generated
```

[`presentation.py`](presentation.py) holds what it applies: the five textures, nine materials, the morning sun on `bay.py`'s sun bearing, the deck sky fill and the sea-haze atmosphere. It rejects a stamp
whose source-scene or arena hash differs, replaces only superseded roots and updates its
own in place, and never writes gameplay data. A lighting or material change is a
`presentation.py` edit applied without a Blender run (see the
[shared kit](../README.md#shared-kit)). The accepted art and Resources prefab are committed.
Then inspect with `sloparena.stage.inspect --stage splash_deck --output
.stage-authoring-cache/splash_deck/harbour/inspection.json`.

## Source files

| File | Role |
| --- | --- |
| `layout.py` | Boat shell spec (outline, raised spots, spawns, KO plane), strict arena check and `--unity-shell` |
| `bay.py` | The shared map: water polygon, quay, breakwater, lighthouse and chapel sites, land height, sun |
| `boat.py` | Deck, painted hull walls, wheelhouse and fish hatch, all on the authoritative shell |
| `sea.py` | Polar sea grid with depth tint and foam alpha, sky dome and sun, clouds, gulls, yachts, far islands |
| `coast.py` | Terrain, shore rocks and outcrops, breakwater, lighthouse, olives, pines, cypresses, maquis, chapel, beach |
| `village.py` | Quay, terraced houses, church and bell tower, fish market, awnings, moored boats, laundry |
| `textures.py` | Deck and hull textures, sign/window/hatch atlas, detail grain and water ripple normals |
| `palette.py` | Sunny-morning Mediterranean palette |
| `build.py` | Assembly, clearance checks, AO, export, digest and report |
| `preview.py` | Eevee quick-review views; actual appearance is judged in Unity |
| `kraken.py` | Kraken hazard mesh, rig, weights and AO (see [Kraken hazard](#kraken-hazard)) |
| `kraken_clips.py` | Kraken clips, solved per frame from authored arm paths |
| `kraken_build.py` | Kraken entry point: FBX, `kraken.json`, checks, re-import |
| `kraken_preview.py` | Kraken key-frame renders on the boat and the contact sheet |

## Kraken hazard

The toggleable kraken (concept B, "the grumpy neighbour") is a separate rigged asset, not part of the
harbour FBX. It reads the slam lanes and their width from `layout.slam_lanes()` and
`layout.SLAM_HALF_WIDTH` at build time, so moving a lane is a rebuild:

```bash
blender -b --factory-startup --python-exit-code 1 -P tools/blender/splash_deck/kraken_build.py -- \
  [--out tmp/stage-hazards/kraken-build] [--no-bake] [--preview]
```

It writes `kraken.fbx` (one skinned mesh with `Col` vertex colours, armature `KrakenRig`: `Root`,
`Head` and eight tentacle chains, every clip on one take), `kraken.json` (per variant the anchor and yaw
the presenter places the object at, the surfaced Root position and the clip names; each clip's first and
last frame in the take; a few exact bone positions to check playback against) and `kraken-report.json`;
`--preview` adds key-frame renders and `contact-sheet.jpg`. It never writes into `client/Unity`. Every
run re-imports the FBX and checks its bones, the take and the colours, measures how far the arm tips
drift from the exact poses at every frame, and measures that no arm rises more than 4 m above the deck
during the warning; an unchanged build is byte-identical. The FBX must stay under 12 MB (no LFS).

- **Clips.** `lurk` (in-place swimming loop; the eyes and cap surface once a loop) and, for each
  `<side>.<lane>` (port or starboard, aft, mid or fore): `rise`, `warn` (loop), `slam`, `lie` (loop),
  `withdraw`, plus the optional `jab` flourish on the NO FIGHTING ON DECK sign from its own anchor.
  `rise` starts and `withdraw` and `jab` end on lurk frame 0. Root motion is on the `Root` bone; the
  object stays where the presenter puts it. The Unity importer splits the single take into these clips
  with the frame ranges in `kraken.json` (`takes.clips`).
- **Paths, not key poses.** Every frame, each arm is laid along an authored path by a chord walk that
  keeps bone lengths; the path's up vectors set the roll, so the slam arm lands suckers down. All arms
  are the same length and each shape's only knob deepens a dive, so spare length stays under the water.
- **Export.** One take, sampled every frame, then every key that linear interpolation rebuilds within
  `--key-tolerance` metres at the arm tip (default 5 mm per bone) is dropped: about 7.5 MB, tips within
  about 14 cm of the exact pose. Every rotation component and the Root's translation are written even
  when constant, so no player fills a channel from another clip or the rest pose. 32 takes cost 14 MB
  in curve headers alone, and keys every second frame let Euler angles wrap between keys (tips jumped
  tens of metres on the in-between frames).

- **Unity.** `client/Unity/Assets/Stages/splash_deck/Hazards/Resources/StageHazards/Kraken/` holds the
  imported `kraken.fbx` and `kraken.json` beside the kraken materials; `KrakenHazardImport` (Editor) splits
  the take into clips from `kraken.json` and sets the rig up on first import (Generic, no root motion node,
  no materials). The import uses resampled curves with Optimal compression at 0.05 degrees, checked
  against the `checkpoints` in `kraken.json` (about 11 cm at the arm tips). To update it, copy both files
  over under your Editor lease and import the json, then the FBX. `KrakenHazardPresentation` reads the
  same `kraken.json` at runtime.

## Rules the build checks

- The deck lies exactly on Y=0 inside the outline. The hull walls and raised-spot
  dressing stand at most 2.5 cm proud of the collision and never rise above a
  walkable top. Roof and hatch artwork are part of the flat tops, not quads above them.
- No background face crosses the blast corridor. Nothing within 25 units of the stage
  box rises above two metres under the KO plane (up to the top blast plus 30). Gull
  lanes are checked along their whole travel, not only at rest.
- The sea is the declared visual-only exception inside the corridor: flat at
  WATER_Y = −11.4, just above the KO plane, so a knocked-out fighter drops into it.
- The deck, hull and raised spots skip the AO bake and carry painted contact shade
  instead. Their few large faces would otherwise turn white paint grey.

## Shaders

The sea uses `SLOPArena/StageWater`
(`client/Unity/Assets/Shaders/SlopArenaStageWater.shader`). Vertex colour is the
depth tint, vertex alpha marks lapping foam, and `SplashDeckRipples.png` supplies
tiling normals stored as x, z and up. The swell is analytic, limited to 12 cm and
fades 140 units from the boat; the shader adds sun glitter, sky reflection by
fresnel and received shadows. Everything else uses `StageVertexLit`. Gulls, the
lighthouse lamp, clouds and the sky use `StageNeon`, whose travel channels move the
gulls and clouds without scripts.

Eevee previews are quick composition aids. Judge exposure, water and colour from
actual gameplay-camera captures with the `sloparena.stage.views.*` batch in the
[stage-authoring workflow](../../../skills/sloparena-stage-authoring/SKILL.md),
with `--lighting pvp` and `--lighting scene`. Agent captures and Shared tests do not
establish human PvP acceptance or player-build performance.
