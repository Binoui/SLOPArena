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

A layout change is a gameplay change. Rebuild the collision source scene from
`python3 layout.py --unity-shell`, which writes the hull, wheelhouse and hatch meshes
plus the markers for an `eval_file` rebuild of `Assets/Stages/splash_deck/`. Then bake,
update `tests/Shared.Tests/SplashDeckArenaTests.cs`, and regenerate the art.

After any changed C# has compiled, copy the seven generated files into
`client/Unity/Assets/Stages/splash_deck/Presentation/Harbour/` within your own bounded
canonical Editor gateway lease:

- `SplashDeckHarbour.fbx` and `SplashDeckHarbour.layout.json` (source/arena stamp)
- `SplashDeckDeck.png`, `SplashDeckHull.png`, `SplashDeckAtlas.png`
- `SplashDeckDetail.png`, `SplashDeckRipples.png`

Then apply and inspect in one batch:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.art-splash-deck
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.inspect \
  --stage splash_deck --output .stage-authoring-cache/splash_deck/harbour/inspection.json
```

The apply command verifies the stamp against the source scene and arena, configures
the textures and nine materials, sets the morning sun, deck fill and atmosphere, and
replaces only the previous barge roots and its own roots. It does not rebake or
write gameplay data. The generated art and Resources prefab stay local/ignored.

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
