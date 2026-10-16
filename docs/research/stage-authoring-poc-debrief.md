# SLOPArena Stage Authoring POC Debrief

**Status:** Reference report for designing a future stage-authoring skill. This document does not implement that skill and does not define a new runtime stage contract.

## Purpose

This report records what the Tropical Festival stage POC taught us about importing third-party environment assets, composing a readable fighting arena, separating art from gameplay, configuring lighting, and reviewing a scene from multiple 3D views.

The intended reader is the author of a future skill that can turn a stage concept and an asset source into a reviewable SLOPArena stage.

## Existing repository constraints

The future workflow must fit the existing project boundaries:

- The server-side Shared simulation remains authoritative for gameplay.
- Unity scenes provide presentation and authored stage data; Unity rendering, animation callbacks, VFX, and audio must not decide combat results.
- Gameplay geometry must be explicit. Decorative prefab colliders must not silently become platforms, walls, hazards, or blast-zone behavior.
- Third-party or non-redistributable source assets must remain local or ignored. Only legally redistributable project-owned adaptations belong in the repository.
- Visual review must use the actual gameplay camera when judging fighter readability. Scene-view screenshots alone are insufficient.
- A build, static scene inspection, and live Unity/Play Mode exercise are different kinds of evidence and must be reported separately.

Relevant existing guidance:

- [Art and asset conventions](../contributing/conventions.md)
- [Testing and verification](../testing.md)
- [Visual presentation baseline](../visual-baseline.md)
- [Unity CLI](../contributing/unity-cli.md)

## POC record

### Inputs

The POC used two local Unity Asset Store-style packages from:

```text
/mnt/storage/Assets/LowPolyMegaBundle/
```

The primary sources were:

- `LowPolyTropicalCity/LowPolyTropicalCity_2020.3.0_SRP_v1.02.unitypackage`
- `TropicalIsland/TropicalEnvironment_2020.3.0_SRP_1.03.unitypackage`

The local source location was useful for experimentation. It should not be treated as a repository distribution path.

### Outputs

The isolated scene was:

```text
client/Unity/Assets/Scenes/StagePOC_TropicalFestival.unity
```

Imported content was placed under:

```text
client/Unity/Assets/LowPolyTropicalCity/
client/Unity/Assets/TropicalEnvironment/
```

The main review image was:

```text
client/Unity/Assets/Screenshots/stage-tropical-asset-poc-final.png
```

A useful visual reference from the Tropical City package demo was also captured at:

```text
client/Unity/Assets/Screenshots/stage-demo-camera1.png
```

The demo scene was used as a temporary visual reference and was unloaded afterward. It was not used as the final POC stage.

### Scene organization

The POC used separate roots for the major concerns:

```text
StagePOC_TropicalFestival
├── Floor
├── CameraMount
│   └── Main Camera
├── TropicalCityDressing
├── IslandBackdrop
├── FestivalProps
└── LightingProbes
```

This organization made it possible to move, review, or remove the imported dressing without confusing it with the gameplay floor.

### Composition

The scene used city buildings and market props on the left and right, with tropical-island landmarks behind the arena:

- houses, cafe, bar, pizza shop, and donut shop;
- lighthouse, ship, and pier landmarks;
- palms, banners, flags, umbrellas, tables, food props, chairs, torches, and barrels;
- a dark navy floor and the existing project lighting setup;
- an open center lane for the fighters.

The final result was a composition proof, not a complete match-integrated stage. The POC used a static camera for review and did not wire the scene into the normal Training or PvP stage-selection flow.

## What worked

### 1. The asset families were visually compatible

LowPolyTropicalCity supplied the strongest stage identity: shops, houses, market props, banners, umbrellas, palms, and readable landmarks. TropicalEnvironment supplied useful perimeter depth: ship, pier, watchtower, torches, barrels, and additional vegetation.

The combination worked because it provided both:

- repeated small-scale dressing for rhythm and color; and
- a few large silhouettes that identify the location from a distance.

### 2. Large landmarks improved orientation

The lighthouse, ship, pier, and shop clusters did more work than isolated small props. A stage needs a few objects that remain recognizable in the gameplay camera. The future skill should deliberately select landmark candidates instead of treating every prefab equally.

### 3. An open center is easier to preserve when the perimeter is authored first

Placing the main visual weight around the edges left a clear combat lane. This is safer than filling the whole scene and trying to remove occluding assets afterward.

### 4. Separate scene roots reduced iteration cost

The root split made composition changes straightforward. The future skill should create this structure before placing assets, not as a cleanup step after a scene becomes cluttered.

## What failed or required manual correction

### Unity package import was not a reliable first step

The available asset import operation copied `.unitypackage` files as opaque project assets instead of importing their contents. The packages had to be extracted while preserving their Unity asset and `.meta` files, then the Unity asset database had to be refreshed.

A future skill must detect this failure mode. It should not report a successful import merely because a `.unitypackage` file appears under `Assets/`.

Recommended import acceptance checks:

- expected prefab paths exist after import;
- at least one known prefab can be resolved as a Unity asset;
- materials and meshes are discoverable;
- the imported scene or prefab can render without missing references;
- the asset database has completed refresh/import;
- the source package is not accidentally left as a redundant embedded archive.

### Imported materials were not immediately renderable

Several package materials rendered magenta because their shader references were not compatible with the active URP project. The affected materials were converted to `Universal Render Pipeline/Lit` before visual review was meaningful.

The future skill should run a material audit before composition:

```text
material path → shader → render-pipeline compatibility → texture references → visible test result
```

A magenta-material count must be a hard review failure, not a cosmetic warning.

### Prefab colliders were not safe to keep by default

The initial POC contained 53 colliders. Fifty-two decorative colliders were disabled, and a duplicate `Floor` BoxCollider was removed so that the floor retained one enabled collider.

This was the correct cleanup for this art-only composition, but it is not a sufficient general policy. A future skill must classify colliders rather than blindly disabling or preserving them:

| Collider category | Default treatment |
|---|---|
| Main floor/platform geometry | Keep only when explicitly part of the gameplay shell |
| Blast-zone or stage-bound geometry | Keep only when explicitly authored and validated |
| Decorative building/prop collider | Disable or remove from gameplay collision |
| Presentation trigger | Keep only on a non-gameplay layer and document its consumer |
| Unknown collider | Fail review and require classification |

The skill should report every enabled collider with its owner, layer, purpose, and whether it is part of the authoritative stage shell.

### Lighting was prepared but not baked

The POC used the project lighting setup, static flags, a reflection probe, and a LightProbeGroup. The scene was structurally ready for lighting work, but no LightingSettings asset was assigned, `bakedGI` remained false, and no baked lightmap was produced.

The skill must distinguish these states:

```text
Unconfigured
Configured for bake
Baked and current
Baked but stale
Bake failed
```

“Reflection probe exists” must not be reported as “lighting complete.”

### A single hero screenshot hid important problems

The final three-quarter image was useful for overall impression, but it could not prove:

- platform and floor shape from the side;
- arena width and spawn separation from above;
- whether props entered the gameplay lane;
- whether the back of the scene was visually coherent;
- whether camera framing matched a real match;
- whether decorative colliders remained enabled.

The screenshot matrix below is therefore a required part of the recommended workflow.

## Visual composition recommendations

### Use a three-layer depth model

Organize dressing into:

1. **Foreground:** limited, low-height elements that frame the view without hiding fighters.
2. **Midground:** the main landmark clusters and stage identity.
3. **Background:** tall silhouettes, palms, buildings, ship, pier, sky, or controlled color fields.

The foreground should be the smallest layer. A common failure mode is using attractive props at fighter height in front of the combat lane.

### Place landmarks before detail

Recommended order:

1. establish floor, bounds, camera, and spawn spacing;
2. place two or three large landmarks;
3. establish left/right visual balance;
4. add background silhouettes;
5. add repeated market or festival detail;
6. remove anything that competes with fighter silhouettes.

Do not begin by instantiating every available prefab.

### Preserve gameplay readability

Review from the normal gameplay camera with representative fighter silhouettes. A stage can look excellent in a free camera and still fail when:

- a building intersects a fighter silhouette;
- a palm canopy merges with a character’s outline;
- a sign occupies the center of the frame;
- a dark floor loses the feet/landing reference;
- a bright background reduces hit readability.

The actual gameplay camera is the authoritative visual review surface.

### Prefer asymmetry with controlled balance

The POC benefited from varied left/right dressing, but random asymmetry can look unfinished. Use different objects on each side while maintaining comparable visual weight, height, and color distribution.

### Keep stage art independent from gameplay semantics

A lighthouse should remain a landmark unless the stage designer explicitly makes it a platform or obstacle. A pier mesh should not become a platform merely because it has a collider. Art intent and gameplay intent must be separate authored decisions.

## Recommended future skill workflow

### Phase 1: Inspect

Inputs:

- stage concept or theme;
- source asset directory/package;
- target scene or isolated POC mode;
- art-only or full-stage mode;
- optional camera and gameplay-shell reference.

Actions:

- inspect existing stage conventions and target scene;
- inventory package files, prefabs, materials, meshes, textures, and colliders;
- check license/attribution and repository-ownership constraints;
- detect render-pipeline compatibility;
- identify candidate landmarks and repeated dressing families;
- estimate scene complexity.

Output: an inventory report before scene mutation.

### Phase 2: Import and normalize

Actions:

- import through the normal Unity path when possible;
- detect opaque `.unitypackage` copies;
- preserve Unity `.meta` files when extraction is required;
- refresh and wait for the asset database;
- convert or repair incompatible materials using an explicit allowlist;
- remove redundant source archives from the project after successful extraction;
- verify known prefab, mesh, material, and texture references.

Output: an import report with failures, repaired materials, and unresolved assets.

### Phase 3: Establish the gameplay shell

In art-only mode, load and preserve the existing shell. In full-stage mode, author it explicitly.

The shell should own:

```text
Floor/platforms
Spawn points
Blast zones
Stage bounds
Camera bounds or camera anchor
Gameplay layers
```

The dressing roots must not own authoritative gameplay decisions.

### Phase 4: Compose the scene

Actions:

- create stable scene roots;
- place landmarks first;
- reserve the center combat lane;
- add background, midground, then foreground dressing;
- keep imported assets grouped by semantic role;
- avoid saving uncontrolled demo-scene clutter;
- check object scale, orientation, pivot, and ground contact.

Output: a first-pass scene plus a composition summary.

### Phase 5: Configure lighting

Actions:

- use the project’s established lighting setup;
- normalize materials before judging light;
- add reflection probes where reflective surfaces need them;
- add light probes where moving characters cross varied lighting;
- mark only appropriate geometry static;
- configure and, when requested, bake lighting;
- report bake status separately from structural readiness.

Output: lighting status and any stale/failing assets.

### Phase 6: Generate the review pack

Capture all views under stable names and record the scene, camera, resolution, build/commit, and settings used. Do not rely on manually positioned Scene views as the only evidence.

### Phase 7: Validate and iterate

Validation should cover:

- missing references;
- duplicate or redundant gameplay components;
- enabled decorative colliders;
- floor/platform continuity;
- spawn points and bounds;
- material/shader failures;
- camera obstruction;
- scene dirtiness and unsaved changes;
- renderer, triangle, material-slot, and texture complexity;
- lighting bake state.

The skill should stop with an actionable report when a review gate fails. It should not hide failures by disabling arbitrary objects.

## Multi-view screenshot review protocol

The following matrix is the minimum useful review pack for a 3D-ready stage.

| ID | View | Purpose | Required overlays |
|---|---|---|---|
| ST-01 | Normal gameplay camera | Fighter silhouette, center-lane readability, HUD framing | None |
| ST-02 | Wide gameplay camera | Stage edge, blast-zone context, full arena composition | None |
| ST-03 | Front three-quarter hero | Overall visual identity and landmark balance | None |
| ST-04 | Left three-quarter | Left-side clutter, depth layering, camera occlusion | None |
| ST-05 | Right three-quarter | Right-side clutter, asymmetry, camera occlusion | None |
| ST-06 | Side orthographic | Floor/platform heights, vertical spacing, ledges | Gameplay geometry |
| ST-07 | Top-down orthographic | Arena width, spawn separation, prop intrusion, bounds | Gameplay geometry |
| ST-08 | Rear or back three-quarter | Backdrop continuity and accidental scene boundaries | None |
| ST-09 | Collision debug | Enabled gameplay colliders versus decorative colliders | Collider categories |
| ST-10 | Bounds/spawn debug | Spawn points, camera limits, blast zones, stage extents | Bounds and markers |
| ST-11 | Lighting/debug | Light probes, reflection probes, shadows, bake state | Lighting markers |
| ST-12 | Material/debug | Missing textures, magenta shaders, unexpected fallback materials | Material status |

### Capture conditions

The future skill should record, at minimum:

```text
Scene path
Stage mode: art-only or full-stage
Camera name and projection
Resolution and aspect ratio
Lighting state
Debug overlays enabled
Asset source/package identifier
Build or commit
Timestamp
```

The normal gameplay-camera image should use the same camera contract as the existing visual baseline: normal gameplay framing, no free-camera composition, no editor gizmos, and no debug overlays unless the matrix explicitly requires them.

Suggested evidence paths:

```text
docs/evidence/stages/<stage-id>/st-01-gameplay.png
docs/evidence/stages/<stage-id>/st-06-side.png
docs/evidence/stages/<stage-id>/st-07-top.png
```

If screenshots are kept as transient local artifacts instead, the report must still record their paths and the capture conditions.

## Recommended skill contract

### Inputs

```text
stage concept
source asset path or package
target scene or isolated-scene request
art-only/full-stage mode
camera reference
preserve-gameplay-shell flag
lighting/bake request
screenshot output directory
```

### Outputs

```text
scene path
asset inventory
import and material report
composition report
enabled-collider report
gameplay-shell report
lighting/bake report
complexity report
multi-view screenshot pack
validation result
unresolved issues
```

### Suggested operations

These are conceptual operation names, not an implementation requirement:

```text
stage.inspect-assets
stage.import-assets
stage.create-poc
stage.compose
stage.configure-lighting
stage.capture-review-pack
stage.validate
```

Each operation should be repeatable and should report what it changed. A dry-run or plan mode is valuable for import and composition because asset packs can contain thousands of objects.

## Acceptance gates

A stage-authoring run should not be considered successful unless the applicable gates pass:

### Import gate

- known prefabs resolve;
- no opaque package-only import is mistaken for a successful import;
- no unresolved material or mesh references remain;
- licensing and ownership status is recorded.

### Gameplay gate

- the gameplay shell is preserved or explicitly authored;
- enabled colliders are classified;
- decorative colliders do not affect gameplay;
- floor/platform continuity is visible in side and top views;
- spawn points and bounds are present in full-stage mode.

### Visual gate

- no magenta materials;
- fighters remain readable from the gameplay camera;
- the center combat lane remains clear;
- landmarks are visible without blocking combat;
- left/right composition is balanced enough to read as intentional.

### Technical gate

- missing references: zero;
- scene saved and not dirty;
- complexity warnings are reported with object paths;
- lighting state is explicit;
- all required review images exist.

A warning such as high triangle count is not automatically a failure, but it must identify the object and remain visible to the skill consumer. The POC produced approximately 87 renderers, 108,004 triangles, and 99 material slots; `Watchtower_01_LOD0` was the notable high-poly object at approximately 20,340 triangles.

## Lessons to avoid encoding as bad defaults

- Do not instantiate every prefab in a package.
- Do not use the vendor demo scene as the production stage without auditing its objects and colliders.
- Do not accept a magenta scene as an import success.
- Do not treat a reflection probe as proof that lighting is baked.
- Do not leave prefab colliders enabled “just in case.”
- Do not judge stage quality from one cinematic camera.
- Do not allow stage dressing to define server gameplay behavior.
- Do not overwrite the existing gameplay shell when the task is art-only.
- Do not commit purchased or non-redistributable source assets merely because the POC needs them locally.

## Bottom line

The POC confirmed that the asset packs can support a good SLOPArena stage, but the difficult part is not prefab placement. The difficult part is a repeatable boundary between imported art, authored gameplay geometry, camera readability, and technical validation.

The future skill should therefore be a **stage review and authoring pipeline**, not a prefab spawner. Its defining feature should be the standardized multi-view evidence pack: gameplay camera, front/three-quarter views, side, top, collision, bounds, lighting, and material diagnostics. That evidence will make a stage genuinely 3D-ready and will expose failures that a single attractive screenshot cannot.
