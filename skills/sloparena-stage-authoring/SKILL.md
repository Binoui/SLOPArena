---
name: sloparena-stage-authoring
description: "Implement SLOPArena PvP stages and solo Arcade courses through authoritative collision/marker baking and separate cosmetic presentation. Use for stage geometry, course spacing, targets/checkpoints, art passes (procedural Blender generators applied with sloparena.stage.art-apply, gameplay-camera views, review cameras, thumbnails, exposure checks) and source-to-bake verification."
metadata: {"category": "game-dev"}
---

For live Editor commands, use your own caller identity and bounded gateway lease from the [Unity CLI guide](../../docs/contributing/unity-cli.md) and [coordination protocol](../../docs/contributing/unity-editor-coordination.md). Run project commands from the selected checkout. Busy ownership waits in the gateway; blocked or unknown status requires recovery.

# SLOPArena Stage Authoring

Use this skill for new PvP stages, approved solo Arcade fixtures, gameplay changes
and cosmetic stage presentation. It is an implementation workflow, not a
concept-design guide. For traversal courses, also read
[`docs/design/traversal-courses.md`](../../docs/design/traversal-courses.md). For Arcade
fixtures and courses, also read the [Arcade reference](references/arcade.md).

## A stage pass, in order

This is how the 2026-10 PvP reworks ran (about 1.5 hours and one question round each).
Every step has a typed command; a stage needs data and Python, not Unity C# of its own.

1. **Start from current main.** `git pull` first, especially on a second machine; Last
   Stop was built on a checkout 67 commits behind and missed the shared kit, shaders and
   commands. Work on a branch and push it when the pass ends.
2. **Design in one round.** Run [`sloparena-stage-design`](../sloparena-stage-design/SKILL.md):
   two or three questions with options, a recommended default and one bolder choice.
   The picks lock the brief (`status: locked`, `approval_scope`).
3. **Shell.** Keep the layout as a `SPEC` in `tools/blender/<key>/layout.py` with a strict
   bake check. `python3 layout.py --unity-shell > /tmp/<key>-shell.json`, then one batch:
   `sloparena.stage.shell-apply --stage <key> --from /tmp/<key>-shell.json` (add
   `--spawns move` or `--remove <old child>` when the shell changes), `sloparena.stage.bake`,
   `sloparena.stage.inspect`. Then `python3 layout.py` (strict check) and the stage's arena
   tests for all four fighters. A new stage adds `--create` to build its source scene; a
   course adds `shell.course(...)` to the document, so its targets, checkpoints and finish
   follow the layout too (Kaboom Quarry is the example).
4. **Generator.** Copy the closest example under [`tools/blender/`](../../tools/blender/README.md)
   and import `stagekit`. Set `TRIANGLE_BUDGET` before building; the build refuses to export
   against a mismatched bake or a blast-corridor clash.
5. **Apply.** `tools/blender/<key>/presentation.py` holds textures, materials, shadow flags,
   the `Stage Lights` rig, atmosphere and review cameras. The build writes it next to the FBX;
   `sloparena.stage.art-apply --stage <key> --from /tmp/<key>-generated` copies the changed
   files in, imports only them and updates the prefab in place.
6. **Look through the game camera** after the rough pass, before detailing: the
   `sloparena.stage.views.*` batch below, then
   `python3 tools/capture_exposure.py tmp/<key>-views/<pass>` (flags clipped or dark views).
   A lighting or material change is a `presentation.py` edit:
   `python3 tools/blender/stagekit/presentation.py <key> --out /tmp/<key>-generated`, re-apply
   (about two seconds), recapture.
7. **Review cameras and thumbnail.** Add `cameras` to `presentation.py` (low, recovery, hero,
   and `thumbnail`), capture them with `sloparena.stage.views.cameras`, then install the card
   with `sloparena.stage.thumbnail --stage <key> --from tmp/<dir>/thumbnail.png`.
8. **Report and wait for acceptance.** Update the production brief, quote the design
   decisions with PASS/DEVIATION lines and leave a `TESTING-UNITY.md` checklist. When the
   maintainer accepts the stage after playing it, record `status: accepted` in the design
   brief and `roster.md`, commit its generated art (listed in `.gitignore` after
   `tools/check_asset_closure.py` passes) and add it to
   [`roster.json`](../../client/Unity/Assets/Resources/Stages/roster.json).
9. **Land it alone.** One PR per stage. A change to `stagekit`, a stage shader, a capture
   command or this skill goes in its own small change, because parallel stage threads
   edit the same shared files.

### Rules from the 2026-10 passes

- **Fix the platform when two stages need the same workaround.** Training stacked three
  directional lights on every stage's own rig; three passes darkened their art to
  compensate before a seven-line `MatchBase` change fixed all of them.
- **The generator is the source.** Never leave the only copy of a script in `/tmp`, an
  ignored cache or another machine; commit it under `tools/blender/<key>/`.
- **No per-stage Unity code.** Apply, shell rebuild, review cameras and thumbnails are
  shared commands. A new Unity-side need becomes a typed command or a field in
  `presentation.json` for every stage, not an `eval_file` script or a stage class. Each new
  Editor script costs every thread a compile and a reload.
- **Let the gateway handle the Editor.** It compiles scripts edited since the last
  compile before running a batch, and agent `editor_play` skips the domain reload. A
  `blocked` lease still means stop and ask; do not improvise Play Mode or refresh steps.
- **Judge exposure from numbers too.** The ACES grade and bloom hide clipping; run
  `capture_exposure.py`, and compare passes with `--against` when lighting differs.

Read [`docs/design/stage-concepts.md`](../../docs/design/stage-concepts.md) before substantive work. That canonical page exists and is required input for topology, readability, and balance intent.

For external environment assets newly referenced by the stage, read and complete [`sloparena-asset-selection`](../sloparena-asset-selection/SKILL.md) before introducing dependencies, even if the pack is already imported. These assets require its verified workset and inspection evidence; reusing accepted stage assets and original authored geometry is not new catalog procurement.

## Current authority and runtime path

```text
collision authoring scene
        │  sloparena.stage.bake
        ▼
data/arenas/<key>.arena
        │
        ├── Shared ArenaDefinition
        ├── GameServer MatchInstance
        └── client prediction/rollback

Resources/Stages/<key>.prefab
        │
        ▼
Unity MatchBase.SpawnStageVisual
        │
        ▼
cosmetic rendering, local lighting, presentation only
```

Solo Arcade uses the same baked arena through `LocalSimulationBridge` and its
dedicated match runner; it does not require a dedicated-server course session.
Embedded challenge data remains Shared authority, not a runtime Unity component.

`src/Shared/ArenaDefinition.cs` is the authoritative stage contract. It owns collision triangles, heightmap, bounds, blast lines, and spawn points. `Simulation` uses it for grounding, ledges, and collision; `ServerSimulation` resolves blast deaths and respawns; `MatchInstance` loads it on the dedicated server for PvP.

`client/Unity/Assets/Resources/Stages/<key>.prefab` is cosmetic. `MatchBase.SpawnStageVisual` loads it dynamically under the runtime Stage root. Unity scene colliders, physics, animation, and presentation must not decide PVP results.

The present stage registry is file-driven, not version/hash-pinned match content. `ArenaRegistry` discovers parseable `.arena` files; Stage Select offers non-training arenas only when a matching Resources prefab and nonempty collision data exist. Do not invent a registry entry, fallback, or version-admission mechanism in this workflow.
Accepted stages built from our own art commit their prefab and generated art (listed
per stage in `.gitignore`, checked with `tools/check_asset_closure.py`), so a fresh clone
and a release build show them. Prefabs that depend on licensed packs stay local. Without
a matching prefab, Stage Select hides the stage and a direct match can run collision-only
with no visual presentation. Stage Select offers only the stages in
[`roster.json`](../../client/Unity/Assets/Resources/Stages/roster.json), and each release
admits the same list online.

## Design brief prerequisite

Substantive stage work — a new stage, a gameplay change, or a visual pass that changes composition (masses, landmark, lighting intent) — REQUIRES a Stage Design Brief at `docs/design/stages/<key>.design.md` with `status: locked`, produced by [`sloparena-stage-design`](../sloparena-stage-design/SKILL.md). If it is absent, fail closed with exactly that message. Production does not invent visual intent.

Cosmetic-only maintenance that does not change composition (for example, replacing a broken material GUID in place) is exempt, matching the cosmetic-only carve-out under "Existing stages".

## Scope and availability

- The PVP workflow below covers normal multiplayer stages. Approved solo Arcade
  fixtures use the focused bake contract described under Arcade foundation stages.
- Every normal PvP stage currently supports **2–4 players**. The present PvP selection/server path has no per-stage capacity admission, so a PvP stage with fewer than four valid spawns is unsafe. Approved solo Arcade fixtures instead require one grounded start and purpose-based exclusion from PvP.
- `static` is the only currently available variant.
- `hazard`, `moving-geometry`, and special-mode variants must be declared explicitly in the stage brief, name their required authoritative capability, and fail closed until that Shared/server capability exists. Never approximate them with Unity-only scripts or decorative animation.
- The maintainer's acceptance after playing the stage is recorded in its design brief and `roster.md` (section 6). Agents must not perform it or claim it.

## Arcade foundation stages

Solo Arcade fixtures and Character Courses use the same bake pipeline with explicit
solo purpose and compiled challenge sections. Their contract is in the
[Arcade reference](references/arcade.md).

Use the gateway directly: `held`/`releasing` waits for your own lease; `blocked`/unknown means stop. Do not message the Editor owner or collect blanket acknowledgments. For source conflicts and plugin-copying Shared builds, follow the [shared protocol](../../docs/contributing/unity-editor-coordination.md) and [build gate](../sloparena-build/SKILL.md); plugin copies still require your own bounded hold.

The repository provides the typed bake and inspect commands below. Use them instead of the `Tools/SLOPArena/Legacy/Bake Arena...` editor menu:

```bash
bun scripts/unity-editor-gateway.ts status --project-path client/Unity
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.bake --stage <key>

bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.inspect --stage <key> --output .stage-authoring-cache/<key>/inspection.json
```

`sloparena.stage.bake` is a deliberate mutation: it opens the fixed authoring scene, validates its collision-source topology, and writes `data/arenas/<key>.arena` after source validation. It must not inspect or mutate cosmetic content.

`sloparena.stage.inspect` is read-only against stage source/output assets. It validates the complete source-to-baked-to-cosmetic relationship, writes a deterministic report and captures, and must not rebake or silently repair stale data.

Its generated output belongs in the local, gitignored directory:

```text
.stage-authoring-cache/<key>/
  inspection.json
  top.png
  front.png
  back.png
  left.png
  right.png
  isometric.png
```

Do not commit generated captures or reports. Briefs may name capture folders as local evidence; acceptance is the maintainer's recorded verdict, not a capture.

## Required stage files

Use one immutable lowercase `snake_case` key everywhere. For example, `slop_pit`:

```text
docs/design/stages/slop_pit.md
client/Unity/Assets/Stages/slop_pit/slop_pit.unity
client/Unity/Assets/Resources/Stages/slop_pit.prefab
data/arenas/slop_pit.arena
```

The key is immutable after acceptance. Change the display name in arena data and the brief, not the key. A new key is a deliberate migration, never a casual file rename.

The runtime Stage Select flow is file-driven: a stage in `roster.json` with a valid
`.arena` and Resources prefab appears. Once accepted, a stage made from our own art
ships its prefab, generated art and thumbnail in Git;
`ArenaShippingTests.PickerRoster_StagesShipWithArenaPrefabAndThumbnail` checks every
roster entry. Do not create another stage-registration path.

## Stage brief

Every new stage has a tracked Markdown brief at:

```text
docs/design/stages/<key>.md
```

The file uses YAML front matter plus explanatory Markdown. It is a human production contract; `sloparena.stage.inspect` validates asset topology only and must not parse the brief as a runtime configuration source.

The front matter and body together bind:

- immutable key and mutable display name;
- explicit purpose: normal PvP with 2–4-player support, or approved solo Arcade with its allowed fighter packages and challenge;
- declared variant and, when non-static, the required authoritative capability and blocked state;
- competitive intent: `competitive`, `mixed`, or `playful`; and the gameplay intent plus applicable Stage Concepts criteria;
- platform, traversability, spawn, and derived-boundary plan;
- visual concept and local-lighting intent;
- verified asset-selection workset/inspection references for newly referenced external assets;
- fixed collision source scene, baked `.arena`, and cosmetic prefab paths;
- required automated preflight and the maintainer acceptance it awaits.

Do not duplicate baked collision values or render metrics into the brief. The `.arena` and inspection report are their respective sources of truth.

### Existing stages

Create or update this brief on the first **gameplay** change to an existing stage: gameplay geometry, spawns, bounds, blast-boundary behavior, or variant/capability behavior.

A cosmetic-only change to an existing stage has no dedicated stage-authoring gate by decision. It still must preserve the global authority boundary: cosmetics cannot introduce gameplay ownership, and newly referenced external assets still require the asset-selection workflow.

## Collision authoring scene

The fixed scene path is:

```text
client/Unity/Assets/Stages/<key>/<key>.unity
```

It contains exactly one required root named `Stage_<key>`:

```text
Stage_<key>
├── GameplayGeometry
├── SpawnPoints
└── AuthoringAids
```

Rules:

- `GameplayGeometry` contains static `MeshFilter` collision geometry only. Unity Terrain is out of scope.
- No `MeshFilter` may exist outside `GameplayGeometry` anywhere under `Stage_<key>`.
- `SpawnPoints` contains empty GameObjects tagged `SpawnPoint`: strict `Spawn_01` through `Spawn_04` for normal PvP, or one `Spawn_01` for approved solo Arcade.
- Every required spawn marker must resolve onto authoritative gameplay ground after baking.
- `AuthoringAids` may contain non-mesh editor aids only. It must not contain decorative meshes, cosmetic prefabs, Terrain, or hidden visual references.
- Do not put art references under disabled objects: the current baker ingests descendant meshes and cannot safely distinguish dressing.
- The source root transform is position `(0,0,0)`, identity rotation, and scale `(1,1,1)`.

Rebuild it from the layout with `sloparena.stage.shell-apply` rather than an eval
script: it keeps mesh GUIDs and GameplayGeometry children, verifies the spawn markers and
the kill plane against the layout, and saves the scene for the bake. After Hours predates
the shared shell format; port its `layout.py` before its shell next changes.

The collision scene exists only to produce authoritative arena data. Keep gameplay geometry sparse, intentional, and readable; use the Stage Concepts contract for what constitutes usable PVP space. The current baker derives the heightmap, X/Z bounds, kill height, side blast lines, and spatial grid from this geometry. Plan desired boundaries in the brief, bake, then inspect the derived result; no explicit blast-line override is available in this workflow.

## Cosmetic stage prefab

The runtime prefab path is fixed:

```text
client/Unity/Assets/Resources/Stages/<key>.prefab
```

Rules:

- Its root transform is position `(0,0,0)`, identity rotation, and scale `(1,1,1)`.
- It aligns with the authoritative source/baked coordinates with no offset, scale correction, or runtime alignment script.
- It contains **no `Collider` components** anywhere. Decorative collision is prohibited, including trigger-only convenience colliders.
- It owns stage-specific local lighting. Put a complete rig under a `Stage Lights` root: the match then switches off the scene's own directional lights, so the stage looks the same in Training, Solo and PvP. Without that root it also receives the match scene's base lighting (Training's key/fill/rim; Arena_PvP has none).
- Imported references must resolve to valid assets. Newly referenced external environment assets need an approved asset-selection workset; original meshes/materials must be persisted as valid project assets or prefab sub-assets.
- It must not carry scripts that adjudicate gameplay, movement, spawn, blast, collision, hazard, or platform behavior.

This prefab is reviewed as art and presentation. It is never a substitute for collision baking.

## Authoring workflow

### 1. Establish the contract

0. Read the locked Stage Design Brief at `docs/design/stages/<key>.design.md` and quote its numbered decisions at the top of the production report. The design brief's decisions outrank this production brief's prose.
1. Confirm `docs/design/stage-concepts.md` exists and applies to the stage.
2. Create or update `docs/design/stages/<key>.md` with its YAML front matter and production contract.
3. Confirm purpose and static topology. Normal PvP supports all 2–4-player matches; approved solo Arcade uses its grounded start and compiled challenge markers. A non-static declaration blocks on a missing authoritative capability.
4. For newly referenced external visual assets, produce and accept an asset-selection workset before introducing them to the prefab.

### 2. Author collision separately

1. Create or edit the fixed collision source scene.
2. Preserve the `Stage_<key>` hierarchy and source-root identity transform.
3. Put only static mesh collision under `GameplayGeometry`.
4. Place the purpose-appropriate tagged empty spawn markers under `SpawnPoints`; compile Arcade challenge markers/settings under `AuthoringAids`.
5. Keep all decorative presentation out of the source scene.

Do not make collision geometry from the cosmetic prefab, copy arbitrary vendor mesh hierarchies into the bake source, or rely on disabled visual objects to avoid baking.

### 3. Author presentation separately

1. Build the approved large-scale geometry first: landforms, building volumes, supporting foundations, slopes, shorelines, and intentional gaps. Use original cosmetic meshes where a pack cannot supply the required shape; do not stitch asset-pack scenes together with flat filler.
2. Keep the visual root identity-aligned with the collision source. Keep solid non-playable background land/buildings out of the fight/recovery/blast corridor; playable presentation must match the authoritative shell. Visual-only water must not suggest a safe surface or acquire gameplay collision.
3. Review the gray massing from a full orbit and low recovery views using the [stage-design capture recipe](../sloparena-stage-design/SKILL.md#3-wireframe-loop-gray-box--design-capture) before dressing it.
4. Dress only established roles with resident or verified imported assets. Ground houses, piers, vegetation, and rocks on actual support surfaces; props explain the geography rather than hide broken joins.
5. Put stage-specific local lighting in the prefab. Keep background detail and contrast subordinate to fighters and usable edges.
6. Remove every Collider and gameplay-owning component from the prefab.
7. Preserve accepted and unrelated existing content; use targeted edits, never broad scene regeneration. Take visual intent only from the locked Stage Design Brief; never improvise composition.

For existing-stage passes, record a baseline for the protected collision source, `.arena`, accepted visual roots, materials, and atmosphere. An additive request means add the agreed roots, not rebuild accepted scenery. Compare protected data after saving; report any intended exception explicitly.

For a polish pass, also capture current gameplay views first, name the three most visible defects, and fix one representative slice in Unity before propagating its treatment. Recapture the same views to compare before/after. A pass that changes masses, landmark or lighting intent updates the locked brief through the design skill first.

Verify placement numerically as well as visually: sample the actual support mesh under each prop footprint, check foundation contact/embedding, and keep island/building dressing parented to its own region without changing world poses. Bounds are a clearance broad phase; check actual triangles where bounds overlap the forbidden corridor, including relevant LODs. A visually convincing screenshot alone cannot prove grounding or clearance.

Repeat the full-orbit and low-view capture after surfacing and dressing. Inspect all frames for rear-face gaps, hovering foundations, sawtooth material boundaries, repeated islands, and false landable routes. Preserve open water/sky rather than filling every gap. Record structural metrics and remaining human review separately; renders do not establish performance or PVP acceptance.

For routine tuning that preserves accepted art/composition, recheck the affected
gameplay-camera vantages and numeric constraints. Repeat the broad orbit review
when topology, occlusion, backdrop or overall composition changes materially;
do not repeat asset procurement or every capture pass for an isolated value edit.
Course spacing changes keep source, presentation, targets/checkpoints/respawns,
finish and route cues aligned. Use the existing targeted relayout seam when
available; a greybox rebuild can discard accepted art.

#### Procedural presentation and visual review

Original presentation is generated from Python in headless Blender
(`blender -b --factory-startup --python-exit-code 1 --python ...`) with the
[procedural stage-art guide](../../tools/blender/README.md): the shared `stagekit`
(mesh kit, AO bake, FBX export, corridor checks, arena reader, preview camera,
presentation and shell writers) and one folder per stage. The five 2026-10 PvP
generators (After Hours, Log Off, Table Manners, Slop Pit, Splash Deck) are the worked
examples; FightGuy Sanctuary is the course example. Read the authoritative source scene
or baked arena, never the prefab, and leave any interactive Blender (MCP) session alone
unless asked.

Work in this order; each step prevents a mistake made in an earlier pass:

1. **Shell and budget first.** Check the art against the authoritative shell and
   set the generator's planned `TRIANGLE_BUDGET` before building; distant scenery
   needs level of detail and culling from the start, not afterwards.
2. **Rough pass, then Unity.** After the first massing-and-lighting build, apply it
   and capture gameplay views before detailing. Eevee previews (about the gameplay
   orbit, 40° FOV) are for quick fixes only: the match camera's ACES grade and bloom
   turn strong emission white and shift colour, so judge in Unity.
3. **Capture without Play Mode stalls.** Use the `sloparena.stage.views.*` batch
   below for PvP stages and the Slopball court, and the [course replay runner](../../docs/testing/course-replay.md)
   for courses. Both disable domain reload before Play Mode and restore the Editor
   afterwards. Do not improvise `editor_play` captures or setup scripts.
4. **Name each defect, repair its source and recapture** the same views. Structural
   inspection replaces materials, so it cannot judge appearance.
5. **Kit edits keep other stages.** Build reports and stamps record a
   `geometryDigest`; rebuild every generator and compare before and after a
   `stagekit` change.

Apply the generator output with one command; nothing is copied by hand:

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/<key>/build.py -- \
  --out /tmp/<key>-generated --report .stage-authoring-cache/<key>/<pass>
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage <key> --from /tmp/<key>-generated
```

`art-apply` reads `<Stem>.presentation.json` (written from `presentation.py`; see
[`stagekit/presentation.py`](../../tools/blender/stagekit/presentation.py)), rejects a stamp
whose source-scene or arena hash differs, copies only files that changed (the FBX and its
stamp travel together), imports exactly those files, configures texture and model
importers, materials by shader property name, per-object shadow flags, the `Stage Lights`
rig, `ArenaAtmosphere` and optional poster faces, and updates existing prefab objects in
place: unchanged data saves an identical prefab, so committed prefabs only change when
the art does. Without `--from` it re-applies the files already in the art folder. A
material or light tweak needs no Blender run: rewrite the JSON with
`python3 tools/blender/stagekit/presentation.py <key> --out /tmp/<key>-generated` and apply
again. For side-by-side options, apply each variant, capture it and keep the chosen one.

```json
{"commands": [
  ["sloparena.stage.views.prepare", "--stage", "<key>"],
  ["editor_play"],
  ["sloparena.ui.viewport", "--width", "1920", "--height", "1080", "--gizmos", "off"],
  ["sloparena.stage.views.capture", "--output", "tmp/<key>-views/pass-01", "--timeout", "120"],
  ["sloparena.stage.views.cameras", "--stage", "<key>", "--output", "tmp/<key>-views/cameras-01"],
  ["get_console_logs", "--severity", "error", "--limit", "20"]],
 "restore": [["sloparena.stage.views.stop"], ["sloparena.stage.views.restore"]]}
```

Run it with `bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch
<file>` from stopped Edit Mode with saved scenes and a Game View open. `prepare`
saves the open scenes, selection, Play Mode options and Game View, disables domain
reload and opens the Training-based [visual baseline scene](../../docs/visual-baseline.md)
on the stage in memory. `capture` orbits the normal gameplay camera around the
fighter at `Spawn_01` and writes one fresh composited PNG per view into a new
directory: eight directions plus two high views by default, or
`--views name:yaw:pitch,...` (at most 12 per call). `prepare --stage slopball` opens the
real Arcade scene instead, with FightGuy and live serves, and `capture` waits for Go.
The orbit camera's pitch is 0–45°, so it never looks up; a view outside the camera's
limits fails as drift. If an unrelated Editor error
triggers Error Pause, it resumes its own session and reports `editorPausesResumed`;
the console step shows the cause. `stop` and `restore` return the Editor to its
saved state.

`views.cameras` renders the named poses in the stage's presentation (`cameras`:
position, target, field of view) through a copy of the gameplay camera and its URP
post-processing, with the HUD hidden: low, recovery, below-edge and hero shots, and the
1280×720 `thumbnail` card. Install that card with `sloparena.stage.thumbnail --stage <key>
--from tmp/<key>-views/cameras-01/thumbnail.png` in Edit Mode after the session; it writes
`Resources/UI/Stages/<key>.png` with the shared import settings (opaque, no power-of-two
rescale, no mipmaps). Slop Pit's presentation has the first set of poses.

`sloparena.stage.views.frametime --seconds 5` samples Play Mode frame times in the same
session with vsync and the frame cap lifted (restored afterwards). On 2026-10-17 the six
roster stages cost 2.2–2.7 ms a frame at Ultra 1920×1080 in the Editor; compare a new
stage with them, and measure a packaged build before claiming player performance.

Then check exposure: `python3 tools/capture_exposure.py tmp/<key>-views/pass-01` prints
mean brightness, clipped and crushed shares per view and flags views over 10% clipped,
mean over 150 or under 30. Accepted 2026-10 views clip about 1–8%; Training's doubled
lights clipped 25–45%. A flag is a prompt to look, not a verdict: a pit view is dark by
design.

With a `Stage Lights` rig the default `scene` lighting matches `--lighting pvp`:
the match switches off the scene's own directional lights (Training's Key 0.7, Fill 0.4
and Rim 0.3; Arena_PvP has none), so one pass covers both. Without a rig, capture both
and compare them with `capture_exposure.py <scene-pass> --against <pvp-pass>`. URP limits
extra lights per renderer (Ultra 4, High 2, Medium 1), so carry the look with the key
light, ambient and baked AO.

The gateway recompiles scripts edited since the last compile before it runs a batch, so
another thread's C# no longer reloads the Editor in the middle of yours; see the
[reload hazard](../../docs/contributing/unity-cli.md#live-c-evaluation).

### 4. Bake and inspect

For new stages and gameplay changes, run the explicit sequence below. For a cosmetic-only pass, preserve the collision-source and `.arena` hashes and run inspect without rebaking; art must not mutate authoritative data.

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.bake --stage <key>

bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.inspect --stage <key> --output .stage-authoring-cache/<key>/inspection.json
```

Inspection must reject or fail the report for:

- missing/misnamed source scene or `Stage_<key>` root;
- MeshFilters outside `GameplayGeometry`, Terrain, or invalid source transforms;
- missing, unordered, untagged or non-grounded required spawn markers (four for normal PvP, one for approved solo Arcade), or invalid compiled challenge markers;
- unreadable/stale/mismatched `.arena`, missing collision triangles, or derived geometry inconsistent with the source;
- absent `Resources/Stages/<key>.prefab` or visual-root transform mismatch;
- any cosmetic Collider;
- missing mesh/material references, missing shaders, or unsupported shaders.

It must report, without a hard performance budget, renderer, triangle, material, shader, and local-light metrics for the cosmetic prefab. These are diagnostic evidence only; no performance gate exists in this workflow.

It captures six deterministic global views—top, front, back, left, right, and isometric—of the cosmetic prefab with the authoritative collision shell and spawn markers overlaid. The views expose density, sightlines, movement room, and visual-to-gameplay misalignment before human review.

### 5. Agent preflight

For every new stage and every gameplay change, run the applicable structural preflight before requesting purpose-appropriate human review:

```bash
dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true --filter FullyQualifiedName~ArenaShipping
dotnet build src/Server/ --nologo -p:SkipUnityPluginCopy=true

bun scripts/unity-editor-gateway.ts status --project-path client/Unity
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- recompile
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- get_console_logs --severity error --limit 20
```

Also run the typed bake and inspect commands above. Review the six captures and the inspection report. The preflight must prove the source/baked/prefab relationship, but it does not prove human playability.

For a Character Course gameplay change, include focused `CharacterCourse` coverage
with `ArenaShipping`, plus affected Server Arcade admission coverage if changing
purpose/serialization/admission. Prove both full ordinary and ability routes with
the real kit, targets, saves and finish; use negative single-jump cases when a
double-jump requirement is part of the design. Then verify the affected native
Arcade path. The [course guide](../../docs/design/traversal-courses.md) explains
chunked replay, isolated records and the boundary between scripted and human play.
Do not force a Unity recompile for geometry/material-only edits if the Editor is
already compiled and imports are settled; follow [testing.md](../../docs/testing.md)
for actual C#/Shared/package changes. Dated evidence is not a blanket checklist
to repeat for unrelated tuning.

Then run the brief-conformance self-check: for each numbered decision in the design brief, output one line `PASS` or `DEVIATION: <decision N> — <what differs and why>`. Deviations are reported to the user for accept/reject — never silently fixed, never silently accepted.

If the stage changes Shared/server source beyond baked data, run the project-required Shared/server contract tests for those code changes as well. Do not suppress unrelated current suite failures; report their existing cause separately.

### 6. Purpose-appropriate human acceptance

For approved solo Arcade courses, the maintainer reviews both traversal choices,
route readability, camera/aim, checkpoints/retry and controller difficulty through
the normal Arcade flow. Report that acceptance separately from scripted completion
and player-build verification. The multiplayer review below applies to normal PvP.

After the agent preflight, the maintainer plays the stage through the normal flow
(Training or Solo, then PvP with 2 and 4 players when possible) and judges whether it
is usable, open enough and not too crowded: spawns, movement, edges, platforms,
recovery, blast boundaries and combat space. Agents do not perform this step and must
not say it passed.

Record the verdict where the next session finds it: the design brief's `status:
accepted` and `accepted:` date with the maintainer's words, the production brief's
`availability`/`human_review`, and the stage's row in
[`roster.md`](../../docs/design/stages/roster.md). Then, and only then, commit the
stage's generated art and prefab and add it to `roster.json` (step 8 of the pass above).
State what was not checked, such as frame time or the packaged build.

A normal PvP stage is not accepted while that verdict is missing, even if all automated
checks pass.

## Explicit non-goals

- Client-only hazards, moving platforms, collision, or spawn logic.
- Unity Physics as PVP authority.
- Per-stage capacity subsets until Stage Select/server capacity admission exists.
- Explicit blast-boundary override controls.
- Terrain-backed authoritative stages.
- Versioned/hash-pinned stage admission.
- Tracked capture evidence or automatic acceptance.
- Performance-budget enforcement.
- Scene-wide regeneration, broad cleanup, or unrequested migration of untouched legacy stages.

## Completion checklist

- [ ] `docs/design/stage-concepts.md` exists and was used.
- [ ] Brief exists or was updated for the gameplay change.
- [ ] Key is immutable lowercase `snake_case`; display name is separate.
- [ ] Source scene, cosmetic prefab, and `.arena` use the fixed paths.
- [ ] Collision scene has the named root hierarchy; meshes only under `GameplayGeometry`.
- [ ] Purpose-appropriate ordered `SpawnPoint` markers bake onto valid ground: four for normal PvP, one for approved solo Arcade; Arcade markers compile and remain excluded from PvP.
- [ ] Cosmetic prefab is identity-aligned, has local lighting as needed, and has zero Colliders.
- [ ] Newly referenced external assets have asset-selection workset evidence; original authored meshes/materials are persisted and inspected.
- [ ] Static variant is accepted; unsupported variants are blocked on explicit authoritative capability work.
- [ ] Typed bake and inspect tools exist and both succeed.
- [ ] Inspection report and six overlay captures are written to local cache and reviewed.
- [ ] Applicable builds and arena/course tests pass; Unity compilation/imports are settled with no current errors; affected native behavior is checked.
- [ ] Human acceptance is reported accurately: 2/4-player PvP review for normal stages, solo traversal/gamefeel review for courses. Pending review is not claimed passed.
- [ ] Locked Stage Design Brief exists and was followed.
- [ ] Brief-conformance table reported.
- [ ] The pass used the shared commands (`shell-apply`, `art-apply`, `views.*`, `thumbnail`) and added no stage-specific Unity code; scripts live under `tools/blender/<key>/`.
- [ ] Exposure checked with `tools/capture_exposure.py`; flagged views explained.
- [ ] After acceptance: brief and roster record it, generated art and prefab committed after `check_asset_closure.py`, stage listed in `roster.json`.
