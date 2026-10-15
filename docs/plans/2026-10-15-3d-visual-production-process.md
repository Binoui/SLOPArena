# A repeatable 3D visual-production process

**Status:** proposed execution plan, 2026-10-15. This document plans work; it does
not approve a new stage, change runtime architecture, install tools, or claim that
the proposed improvements are implemented.

**Priority:** new signature stages and polishing existing stages. Model and VFX
work are smaller supporting tracks. The user selected these priorities after the
FightGuy Sanctuary art pass.

**Recommendation:** retain Blender → Unity and SlopArena's existing authoring
tools. Improve the design-to-review loop, prove it on one existing arena and one
new arena, and extract reusable code from those actual uses. Keep visible art
delivery ahead of building a general studio application.

## 1. What the research changes

The [repository research](../research/2026-10-15-ai-3d-production-workflows.md)
examines revision-pinned documentation and code from gamedev-skills and
OhMyGame. Three practices are worth adopting:

1. **Resolve the place before filling it.** Develop each region and landmark
   through construction, relationships, activity, materials and the views that
   reveal it. This extends our numbered stage decisions with concrete visual
   information; it does not add another design-document system.
2. **Finish a representative slice, then produce a family.** Establish the
   material response, proportions, detail scale and lighting on a small Unity
   sample before multiplying assets across a stage.
3. **Compare pixels, name a defect, repair its source, and recheck.** An image
   being generated, an object being present and a successful import are different
   from the scene looking right.

The useful external starting points are OhMyGame's
[world expansion](https://github.com/WhiteTowerAI/ohmygame/blob/2cec05e4af5c83d341a02df3462088475685c772/plugins/threejs-world/skills/threejs-world/references/world-expansion.md)
and [visual review](https://github.com/WhiteTowerAI/ohmygame/blob/2cec05e4af5c83d341a02df3462088475685c772/plugins/threejs-world/skills/threejs-world/references/validation.md),
and gamedev-skills'
[asset-family workflow](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/blob/0a70cfc64672d512c5b5b6006b5dcb5cf90dbdbd/skills/disciplines/create-game-assets/SKILL.md)
and [3D import checks](https://github.com/gamedev-skills/awesome-gamedev-agent-skills/blob/0a70cfc64672d512c5b5b6006b5dcb5cf90dbdbd/skills/disciplines/create-game-assets/references/three-d-pipeline.md).
Adapt these practices to the fight camera: detail that helps a walking simulator
can become noise behind four fighting characters. External showcase UI, mobile
web publishing, broad skill routers and provider daemons are outside this plan.

OhMyGame's shipped playtest adapter is Web/Chromium; its
[runtime documentation](https://github.com/WhiteTowerAI/ohmygame/blob/2cec05e4af5c83d341a02df3462088475685c772/docs/runtime.md#runtime-adapter-boundary)
explicitly distinguishes this from future Unity/Godot adapters. Its documented
3D providers produce GLB assets and limited metadata/axis handling. Neither
replaces our Blender cleanup, Unity importer, package cook or native verification.

## 2. Current strengths and specific gaps

Local source was inspected at `9de7909` with the existing dirty working tree,
including the uncommitted Sanctuary tools. This is a saved-source audit, not a
claim that HEAD alone contains the art installation.

| Area | Already available | Improvement needed |
| --- | --- | --- |
| Visual direction | [Visual language](../design/visual-language.md), [art conventions](../contributing/conventions.md), character identities | A small set of current gameplay examples showing the material, lighting and detail hierarchy in practice |
| Stage production | Design/authoring/asset-selection skills; authoritative bake; cosmetic prefab; source/bake inspection | Connect those steps into a concise creative loop with an early finished sample and region-level repair notes |
| Existing assets | Catalog index records 9,509 prefabs from 35 packs; inspected worksets and thumbnails already exist | Reuse by geometric role and art family; avoid another catalog or importing a pack just to browse it |
| Procedural art | [Sanctuary source](../../tools/blender/fightguy_sanctuary/README.md), original textures, AO bake, FBX export and StageVertexLit | Stage-specific surface names, palette, atlas slots and box assumptions must remain explicit; the layout hash alone is an incomplete rebuild receipt |
| Review images | Stage inspection/design captures, generic capture commands, course replay, Ability Lab and Training | Structural inspection replaces materials; appearance review needs real materials/light and deliberate framing. The Sanctuary run also exposed captures arriving before the requested Cinemachine pose was evaluated |
| Cost checks | Triangle/renderer/material/LOD diagnostics | Active render cost, memory and worst visible frame measurements; counts alone cannot prove performance |
| Retention | Source tools plus private Unity asset-bundle transfer | One clearly identified accepted source/export/import record per produced family; cache scripts and ignored prefabs are otherwise easy to lose |

Two documentation details must be corrected when implementation starts. The older
[visual roadmap](visual-production-roadmap.md) and
[baseline](../visual-baseline.md) contain historical roster/presentation claims;
the baseline's captures are still marked pending. The current
[stage roster](../design/stages/roster.md) and `StageSelectController` use a curated
allowlist as well as valid arena/prefab availability. A new PvP map needs that
explicit admission step; do not infer that writing a prefab makes it selectable.

This proposal complements the existing roadmap and current visual direction.
After approval, update their affected guidance rather than creating competing
canonical rules.

## 3. The production loop

### Step A — Establish the visible result

Start each task as one of: new stage, existing-stage composition revision,
surface/lighting polish, model repair, or effect polish. Record what the user
wants to see, what is already accepted and the gameplay data that is protected.
Reuse prior decisions; ask only for unresolved choices that materially change
the result.

Use the existing `docs/design/stages/<key>.design.md` for stage intent and its
sibling production brief for implementation/provenance. Add a short visual
expansion there, covering:

- the single premise and hero landmark;
- near/middle/far regions, their support and connections, and intentional empty space;
- a small material family and palette roles, including quiet background values;
- traces of activity that explain the place, instead of uniformly scattered props;
- which details survive the gameplay camera, and which are reserved for closer views;
- named review views that demonstrate these decisions.

Preserve SlopArena's chunky, scavenged fight-club identity. Stages can have distinct
settings and palettes; they do not all inherit the Sanctuary's Tibetan motifs or
the menu's Paper/Ink treatment.

### Step B — Prove the gameplay space and the composition

For a **new stage**, design the playable shell and world masses together, then
use the existing graybox and human review/LOCK workflow. Normal PvP supports
2–4 players and four valid spawns. A solo course uses its approved purpose,
grounded start, marker/routing and ordinary/ability-route requirements.

For **existing-stage art**, snapshot the collision source, baked arena, spawns,
accepted visual roots, lighting and affected materials. An intended layout change
becomes a separately identified gameplay revision; cosmetic work preserves those
authoritative bytes and does not rebake.

In both cases, inspect all relevant sides and intermediate/low views. Establish
the world below, above and beyond the shell, grounded support and full blast-space
clearance before spending time on detail. A visual platform or blocking mass must
correspond to gameplay geometry, or live outside the player corridor. The camera's
actual allowed orbit/zoom must also remain clear.

### Step C — Finish one representative slice in Unity

For a new look, finish one hero fragment, one ordinary playing surface, one
supporting prop cluster and the surrounding light/haze. Show the same sample at
gameplay distance and a useful closer view. This should answer whether the look
works before the full asset family is produced.

For an established look, use the accepted example and go directly to its bounded
repair. Keep at most a few meaningful alternatives when direction is open; do
not generate a large moodboard to postpone a decision. Combine artistic decisions
into the existing review checkpoints rather than requesting approval for each prop.

### Step D — Produce families with the appropriate tools

| Need | Default production route |
| --- | --- |
| Repeated architecture, supports, paving, trim, simple props and coherent rock forms | Blender Python using the retained helpers and a stage-specific composition script |
| A suitable existing environment piece | Resident accepted art first; otherwise targeted catalog selection, inspection and stage-owned adaptation |
| Distinctive complex hero prop or organic shape | Existing suitable source or focused Blender authoring; consider generated 3D only for a demonstrated gap |
| Concept, motif, decal or material reference | Image/vector/procedural texture work selected for the task, followed by actual resolution/UV/tiling and Unity checks |
| Rigged fighter repair | Existing character-polish and character-workflow paths; preserve identity, rig and bindings within the approved scope |
| Combat or ambient motion | Existing presentation events/prefabs and small cosmetic animation/shader changes appropriate to the effect |

Generate into a fresh temporary output first. Background Blender runs suit
repeatable construction; Blender MCP suits inspecting and editing an existing
artist scene. Never reset that scene as a side effect of a headless build.
Keep the verified Unity FBX conventions. Do not introduce GLB as a project-wide
interchange change simply because an external workflow prefers it.

Group art by named region/host so a repair can replace its own part. Preserve
accepted world poses. Keep atlas white regions, vertex-colour space, alpha/detail
and wind masks explicit; these are real dependencies of the current mesh/shader
contract. Use separate material roles for fighters, playable surfaces, background,
emissive accents and effects without requiring a universal master shader.

### Step E — Review, diagnose and repair

Use a short defect table: `view/time | visible problem | probable source |
bounded repair | result`. Prioritize composition and silhouette, then support and
scale, material/light hierarchy, repeated shapes, and finally small detail.

Appearance evidence uses the imported asset, actual stage light/grade, real
fighters and evaluated gameplay camera. Structural overlays and Blender renders
remain useful supporting evidence. Record the actual camera transform, viewport,
quality, lighting state, relevant frame/tick and source identity so comparisons
can be reproduced. Keep cinematic or free-orbit diagnostic images labeled.

For a broad stage revision, cover the full orbit, low recovery, major player
positions and the hero/detail views named in the brief. Add normal-speed movement
and combat, including a launch near an edge. Review a four-fighter stress case as
well as two-player readability for PvP; posed rigs are not a running match. A local
controlled stress scene is not connected four-player acceptance.

Recheck only affected views after an isolated repair. Repeat the broad pass when
composition, topology, lighting or occlusion changes warrant it. Keep a short
moving clip for timing/atmosphere/VFX; a still cannot establish those qualities.
Stop when the visible targets are met and remaining defects are explicitly accepted
or deferred. Do not add screenshots or art changes solely to enlarge the report.

### Step F — Measure and retain the accepted result

Record frame-time behavior on the same reference hardware, resolution, quality,
camera and workload before/after. Include CPU/GPU observations where available,
active draws/batches, texture/mesh memory, shadows, transparency/overdraw and
visible LOD transitions. Report unavailable measurements plainly.

Proposed initial rendering target: stable 60 fps at 1920×1080 on a named reference
machine, to be confirmed with the user before setting budgets. The 60 Hz simulation
is a separate contract. Set measured per-scene budgets after the baseline; neither
Sanctuary's triangle count nor another project's limits become universal caps.

Retain a compact production record with source, parent asset where applicable,
generator files/revision, input hashes, seed, Blender version, exact command,
export settings, textures/materials, Unity paths/GUIDs/import settings, output
hashes, acceptance scope and reproduction steps. Keep code-driven source in
`tools/blender/<stage>/`; preserve canonical `.blend` sources separately when
manual edits are part of the accepted result. Never leave the only source in
`/tmp` or an anonymous study folder.

Use the existing [private asset bundle](../contributing/unity-setup.md#copying-an-existing-private-asset-installation)
to provision ignored local Unity inputs. Verify a fresh-directory reconstruction
and its Unity application before describing art as recoverable. Original source
code and permitted metadata can be versioned with explicit commit authorization;
vendor inputs retain their existing private/local boundary. No upload or purchase
is implied by this plan.

## 4. First execution cycle

The sequence below delivers art while improving the smallest pieces of tooling
that currently obstruct it. These are work batches, not elapsed-time promises.
When an existing asset already meets the target, keep it and move to the next
observed visual defect.

| Batch | Deliverable | Concrete work | Done when |
| --- | --- | --- | --- |
| 1. Establish current examples | A compact bright/dark 3D reference board | Use Sanctuary as the procedural construction example; capture current Table Manners and After Hours with matched gameplay conditions and the roster. Select material/light examples and identify three highest-value defects in the polish candidate | Each chosen repair has a real baseline view. Existing acceptance is distinguished from pending gameplay/performance review |
| 2. Remove iteration friction | Reliable view capture and a reproducible Sanctuary build packet | Extend the existing capture path to verify evaluated camera pose; add material-accurate review support alongside structural capture; record a fuller rebuild receipt and reconstruct into a fresh directory | Requested versus rendered angles agree; no frozen simulation drift; Editor restoration settles; a fresh reconstruction reproduces geometry/textures and applies without changing gameplay |
| 3. Polish an existing arena | A bounded After Hours visual pass, unless the fresh baseline selects a better candidate | Choose the top three actual defects. Likely investigations are roof/riser edge read, concrete/metal separation, and skyline/sign/light hierarchy. Finish one sample, propagate only accepted changes, and compare the same views | The three observed issues are resolved or explicitly deferred, art-only gameplay hashes match, normal gameplay and stress captures are reviewed, cost delta is recorded |
| 4. Create one new signature arena | A complete static PvP stage through the improved loop | Review a small set of layout/composition options, lock the chosen shell/intent, finish the style slice, build families, bake/inspect, and exercise normal stage entry | Distinctive place, readable 360° composition, grounded world, correct four spawns and purpose, source/bake agreement, clean references, performance observations, and the required human 2/4-player review |
| 5. Consolidate demonstrated reuse | A small working environment kit | Compare Sanctuary and the new stage's needs. Extract only genuinely shared mesh/export/preview pieces, retain per-stage composition/palette, document reuse and known assumptions | Both stages still rebuild correctly; a small new prop family uses the retained helpers without copying an entire generator |
| 6. Optional visual accents | One effect family and one bounded model repair | Select by gameplay-distance benefit; use the model/VFX track below | A clear before/after improvement in motion with retained simulation and rig/content contracts |

The current roster names Table Manners as the strongest map and After Hours as
acceptable; their briefs retain review limitations. They are starting candidates,
not proof that a particular repair is needed. Do not restart an accepted stage
because its source predates this plan. Sanctuary's positive visual feedback is a
reference for the shown art, not a performance or physical-controller sign-off.

**Proposed new-stage subject:** Manki's improvised backlot test yard, reflecting
his inventor/explosives identity. An oversized test rig could anchor the backdrop,
with a static service platform as the fight space and sparse traces of repairs
and failed experiments. This is a concept for review, not a locked stage or an
authorization for explosive hazards, dynamic scenery or gameplay changes. Another
character/location can use the same production process.

## 5. Bounded implementation backlog

The first two batches should modify existing owners before adding abstractions:

1. **Camera proof:** update `SlopArenaReplayCommands.Capture` and reuse the generic
   capture service. Wait for/verify the intended evaluated camera state and fresh
   render; report requested and actual pose/FOV, not only frame freshness. Keep
   ordinary gameplay camera behavior untouched. Regressions should include rapid
   consecutive yaw/pitch changes at one frozen tick, stale/mismatched pose rejection,
   no simulation drift and actual stop/restore/release. The session's temporary
   forced-Cinemachine diagnostic is evidence, not the proposed permanent algorithm.
2. **Appearance capture:** extend the existing stage capture/service with a
   clearly labeled actual-material review mode and reusable saved view recipe.
   Preserve structural inspection and overlays. Frame the gameplay shell/landmark
   deliberately so a kilometre-wide backdrop does not reduce the arena to a dot.
   Isolated Edit Mode views must not impersonate the runtime light/grade or actor state.
   Use the normal Training/PvP/course path for final appearance and movement checks.
3. **Rebuild record:** extend the Sanctuary build stamp/report and its apply
   command's input validation with the compact production data above. Compare
   canonical mesh/material data and textures for reproducibility; FBX file bytes
   can contain unrelated export metadata. Preserve stable imported asset GUIDs.
4. **Non-box stages:** the current layout reader handles named axis-aligned boxes.
   Before a non-box consumer needs it, extend the existing authoring service's
   read-only export to provide actual geometry/markers/blast bounds. Fail on
   unsupported input rather than silently approximating arbitrary meshes with boxes.
   Do not add a second collision runtime or general-purpose Unity YAML editor.
5. **Workflow guidance:** add the region-expansion, representative-slice and
   critique-loop recipes to the maintained stage design/authoring references;
   link the retained Blender kit. Refresh the baseline/roster guidance and sync
   discovery adapters when needed. Keep one owner for each existing production rule.

Choose between a separate read-only visual-report wrapper and further command
options only after the two pilots expose repetitive work. This plan recommends
small existing-command extensions first. Any shared-library extraction or broader
tool architecture still receives its normal design approval before implementation.

## 6. Model and VFX opportunities

These are targeted candidates, not a roster rebuild or a blanket effects pass:

- **Environment models:** give each new stage one distinctive hero prop and a
  small related supporting family. Spend modeling effort on silhouette and
  construction visible through the orbit. Distant buildings can use simpler
  geometry/materials; fine textures should not carry structural readability.
- **Fighter presentation:** compare all four admitted fighters on the bright and
  dark references, then repair the highest-value shading, coverage, material or
  deformation problem on one fighter. Retain identity and the existing Avatar/
  bindings; use real attack and recovery poses. A rig/clip/import change can alter
  cooked poses and must follow the character workflow rather than being labeled
  cosmetic from unchanged bone names.
- **Combat effects:** compare the existing light/heavy/launch and defense cues
  before adding more layers. A useful first experiment is a clearer silhouette
  and faster visual decay for one overbearing effect, or a stronger directional
  accent for one weak contact. Review hit, shield, whiff, Hitstop and interruption,
  with two overlapping fighters and then the larger stress case.
- **Stage atmosphere:** restrained cloth/foliage motion, distant smoke, dust,
  water or haze can establish life. Select one or two setting-specific motions
  away from gameplay silhouettes; decorative timing never becomes a hazard or
  an authoritative hit window.

Reuse `CombatFeedback`, `GraphicHitEffect`, package-owned presentation bindings
and the timeline presentation dispatcher. Normals stay restrained and specials
keep their identity. Check Reduced Flashing and worst-case transparency; accepted
hits/events and their existing prediction/rollback handling remain the authority.
Use [character polish](../characters/character-model-polish.md),
[animation authoring](../../skills/sloparena-animation-authoring/SKILL.md) and
[VFX presentation](../systems/vfx-particles.md) for those focused changes.

Paid image-to-3D, rigging or retexture services are optional gap-fillers. Trial a
single required asset against the same geometry/material/runtime checks before
considering batch production. Their availability, spend and installation remain
separate explicit choices. A generated preview does not establish usable topology,
UVs, rigging or permission to publish the source.

## 7. Daily collaboration and completion

An agent starts from the existing brief and accepted source, states the visible
change, prepares off-Editor work, and imports/captures under its own bounded
gateway lease. It brings back a small comparison with specific observations and
clear remaining choices. A user can steer personality, composition and finish
without supervising every command or prop. Preserve previous authorization for
bounded iterations; keep existing new-PvP layout/human acceptance requirements.

The iteration ends with one current accepted source record, before/after views,
the appropriate native behavior checks, measured cost or explicit unknowns, and
a short Test in Unity checklist. Source edits trigger only applicable compilation,
tests/cook and runtime work under [testing](../testing.md). Art-only iteration does
not require an unrelated full release or publishing cook. Final player-build and
real multiplayer acceptance are reported separately from local rendering.

**First actionable batch:** capture the current bright/dark examples, select the
three actual After Hours defects, and fix camera-proof/rebuild friction only as
needed to make their before/after review trustworthy. Then deliver that bounded
polish pass before starting the new-stage design. The process succeeds when the
next stage is easier to make and the art is visibly better—not when the tool or
document count increases.
