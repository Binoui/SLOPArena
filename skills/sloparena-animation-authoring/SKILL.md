---
name: sloparena-animation-authoring
description: "Author SlopArena character animations with the GenRA/Blender workflow, actual-character review before move integration, Unity Humanoid import, package binding and cooking."
metadata: {"category": "game-dev"}
---

For live Editor commands, use your own caller identity and bounded gateway lease from the [Unity CLI guide](../../docs/contributing/unity-cli.md) and [coordination protocol](../../docs/contributing/unity-editor-coordination.md). Run project commands from the selected checkout. Busy ownership waits in the gateway; blocked or unknown status requires recovery.
# SlopArena Animation Authoring

Use this skill when creating or changing a character animation clip. It owns presentation assets and catalog bindings only. It does **not** change gameplay timing, damage, hitboxes, input, server simulation, or VFX behavior.

For agent-driven move preview, timeline scrubbing, captures, or diagnostics, use the
[Ability Lab skill](../sloparena-ability-lab/SKILL.md) and
[Unity CLI reference](../../docs/contributing/unity-cli.md).

For new in-house motion, start with the maintained
[GenRA animation workflow](../../docs/characters/genra-animation-workflow.md).
It records rig preparation, conversion controls, both documented authorship routes,
bounded repair and the candidate review handoff. Existing sourced clips and direct
native-clip corrections keep the applicable import/verification path below.

## Authority

```text
Rig-aware keys / Blender source/action (GenRA workflow for new motion)
  -> Unity candidate clip (FBX import or native .anim) and .meta settings
  -> actual-character candidate preview and user approval
  -> CharacterAssetCatalog.asset semantic binding
  -> inspect/cook generated package artifacts
  -> Ability Lab / Training presentation
```

`character.json` owns move timing in 60 Hz ticks. The animation clip must conform to that timing; do not retime gameplay to fit an animation without an explicit gameplay change.

`CharacterAssetCatalog.asset` owns the clip reference. The embedded Unity clip `fileID` is part of that binding and can change after an FBX reimport.

Blender/FBX source, native `.anim`, Unity `.meta`, and catalog bindings are source. `content-cooked/` and generated animation catalogs are outputs; never hand-edit them.

## User review before move integration

For new or substantially reworked move motion, get the user's approval of the
concrete animation **before** creating or changing that move in `character.json`,
replacing its live catalog binding, or cooking/admitting the candidate as accepted
content. A request to make an animation for a slot, including permission to change
move data, still includes this animation review checkpoint.

1. Prepare an unbound candidate and complete the applicable authoring/import checks.
   Preview it on the actual character through a transient instance or isolated
   candidate asset. Show normal-speed side and gameplay views, with scrubbing or
   slower playback when useful. Check anatomy yourself first; include a close view
   of hands or other affected joints when the wider view hides their articulation.
2. Present the candidate revision, preview and proposed contact/recovery clock, then
   wait for the user to approve the animation. If gameplay retiming is authorized,
   keep proposed values in candidate notes until this review, rather than editing
   live move data to enable the preview. Apply feedback within the candidate and
   present the revised result when it is ready.
3. After acceptance, integrate the reviewed candidate within the authorized gameplay
   scope, then run the applicable binding, cook and runtime checks below. Reuse
   approval already given for that candidate; do not ask again for each command.

This is one review before integration, not permission for every authoring pass.
Bounded corrections to an already accepted animation retain their existing task
authorization unless the user requests another checkpoint.

## Before authoring

Choose the applicable path first: static pose, bounded correction, or new/reworked motion.
Static poses need pose invariants and entry/exit behavior, not a beat plan or blocking,
breakdown and interpolation passes. For Unity-native `.anim` assets, skip Blender/FBX
export and reimport steps; retain actual-prefab, binding and applicable cook checks.

1. Identify the package, semantic animation ID, catalog entry, FBX asset, take name, clip duration/fps/frame range, and the ability event tick(s).
2. Map authoritative event ticks to source frames before posing. For constant forward playback:
   ```text
   sourceFrame = startFrame + (tick - playbackStartTick) / 60 * sourceFps * playbackSpeed
   tick = playbackStartTick + (sourceFrame - startFrame) / (sourceFps * playbackSpeed) * 60
   ```

   `startFrame` is the source frame at playback entry; `playbackSpeed` is the actual runtime multiplier, not an assumed 1. At 30 fps and 1x speed, playback starting at source frame 1 on tick 0 reaches frame 4 on tick 6. For variable speed, offsets, or holds, trace the actual playback mapping instead of applying this constant-speed formula.
3. Record the current Unity import state: Humanoid/Generic rig mode, avatar validity, root-motion settings, clip name, and embedded clip `fileID`.
4. Preserve a byte-identical FBX and `.meta` backup before reimporting. Do not overwrite a working source asset without a recovery copy.
5. Define pose invariants: start/end neutral pose, no unapproved root translation, fire/active-frame silhouette, recoil/settle, and transition compatibility with the adjacent animation.

Before editing, record a `tick | source frame | beat` plan using the actual playback mapping. Mark authoritative contact/fire events separately from optional presentation beats: entry, anticipation/load, compression, follow-through/recoil, settle, and handoff. Use only the beats that explain this move; short attacks, holds, loops, and airborne actions need not share a phase count. Fit presentation inside the gameplay envelope, or label an authorized proposed clock as provisional for user review; never import generic attack durations or cancel-window percentages.

## Blender authoring

- Work from the known-good source rig/action. Keep the hierarchy, rest pose, scale, bone names, and take identity stable unless the change explicitly includes a rig migration.
- Pose a readable fighting-game arc: anticipation, active/impact, recoil or follow-through, settle. Favor clear silhouettes and short contrast over physically realistic weapon handling.
- Keep the source entry frame and intended handoff/end pose exact when the move blends from/to an existing pose.
- The Blender action timing is authoritative in seconds. Preserve the intended action duration and authored FPS. Unity's reported `AnimationClip.frameRate` is verification data, not permission to resample or retime the source.
- Treat root translation as gameplay-sensitive presentation data. Do not add it unless the move contract explicitly permits it.

### Authoring passes

Inspect the current action, frame range, FPS, relevant bones, and existing keys first. New motions and major reworks start at blocking; bounded changes enter at the relevant pass and preserve accepted poses and curves.

1. **Blocking:** use sparse semantic poses with stepped/constant interpolation. Inspect them from the normal gameplay camera for action direction, silhouette, pose contrast, plausible weight transfer, and entry/handoff compatibility. Establish readable anticipation and a distinct active pose where applicable before adding transitions.
2. **Breakdowns:** add only poses needed to establish body mechanics, arcs, limb/weapon trajectories, recoil, and recovery. Choose spacing around authoritative events deliberately; do not change gameplay timing to improve the animation.
3. **Interpolation:** interpolate only proven primary motion. Choose interpolation per channel and phase, preserving attack snap and pose contrast rather than uniformly smoothing everything. Inspect resulting trajectories for overshoot, foot sliding, root drift, and silhouette collapse; diagnose responsible curves before adding corrective keys.
4. **Polish:** add overlap, drag, settling, secondary motion, and asymmetry only when primary motion and timing already read. Extra movement must support rather than compete with the gameplay event.

A pass is accepted internally when its visible criteria are met. Make routine decisions
within the approved move contract; present the validated candidate at the user review
checkpoint before move integration.

### Visual evidence and repair

Work in coherent passes: author → capture → inspect → identify a defect and likely cause → bounded repair → inspect affected evidence again. Do not rebuild accepted motion because another interval fails.

Each capture must answer an unresolved visual question. Reuse evidence across skills
and acceptance criteria: actual-prefab Training footage can satisfy both prefab and
runtime presentation checks. Start with one useful comparison per changed rig for a
static pose, plus entry/exit playback; add views only for unresolved defects. Inspect
the evidence, record the finding, and stop when the criterion is established.
Use tests/state measurements for timing, input and resources rather than extra images.
Keep detailed traces for failures and representative cases, not every passing
permutation, unless explicitly requested. Repeat only checks invalidated by a change.

- For new or substantially reworked motion, inspect a semantic-frame contact sheet and complete timed gameplay-camera preview before final import. Static poses need neither a repeated-frame contact sheet nor an artificial timed motion preview. Viewport captures are sufficient; polished rendering is not required.
- For a bounded correction, inspect the affected frames/interval first, then the complete action and entry/handoff before acceptance. Replay at the intended runtime speed, not merely the source FPS.
- Use the presentation acceptance criteria below for still poses. Use playback to judge acceleration, attack snap, arcs, continuity, contact, weight, recoil, foot sliding, interpolation overshoot, and secondary-motion timing. Stills cannot prove motion quality.
- Add diagnostic angles only when the gameplay view cannot establish the suspected defect. For unusually extreme poses, inspect affected joints/accessories for collapse, volume loss, clipping, twisting, or apparent detachment; this is not a full rig audit.
- Tie each repair to an observed view, frame range, and likely bones/channels. User critique identifies the failed dimension—pose, timing, trajectory, weight, silhouette, deformation, transition, or secondary motion—not automatic permission to rewrite the clip.
- Keep the current source recoverable and associate evidence with its action revision/pass. Repeat expensive checks only when relevant changes invalidate them.

Open and inspect the evidence. Existing keys, successful rendering/export, and successful Unity import alone do not establish animation quality. Blender evidence does not replace the actual-prefab and Ability Lab/Training gates below.

## Unity import gate

1. Import the candidate FBX through Unity.
2. Inspect the imported clip on the actual character prefab, not only in Blender:
  - avatar and Humanoid import are valid;
  - mesh stays at expected scale and origin;
  - clip entry and end poses match the intended transition contract;
  - clip duration, fps, and binding count are expected;
  - relevant event poses, or the static hold and entry/exit, read at gameplay camera scale.
3. Reject the candidate if Unity changes bind pose, root offset, scale, facing, or Avatar validity. Do not compensate with client-side transforms.
4. A Blender FBX export that fails this gate is not a valid delivery. Preserve the known-good source asset and diagnose the importer/rig contract first.

## Catalog cutover

For new or substantially reworked motion, complete the user review checkpoint first.

After the import, resolve the imported clip's current embedded `fileID` and update the corresponding `CharacterAssetCatalog.asset` semantic binding if it changed. A correct GUID with a stale `fileID` is an invalid binding.

For Manki:

- Manki is package-native and cooked; preserve the package registry and presentation boundaries.
- Ground R uses semantic ID `anim.manki.gr`.
- Its binding is in `client/Unity/Assets/CharacterPackages/manki/CharacterAssetCatalog.asset`.
- Never assume an earlier imported `fileID` remains valid after reimport; read Unity's current importer metadata.

## Preview and accepted package verification

### Local iteration

Preview the imported clip on the actual character prefab and exercise the affected
Ability Lab or Training path. An imported clip can be tuned through the transient
development catalog without cooking a publishing package or changing generated
animation catalogs and roster pins.

### Accepted binding and package

For content intended to ship, run the supported Unity CLI flow from the repository root:

```bash
bun scripts/unity-editor-gateway.ts status --project-path client/Unity
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- recompile
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- recompile_status
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- get_console_logs --severity error --limit 20
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.inspect --target <package>
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.cook --target <package>
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.inspect --target <package>
```

Require all of the following for an accepted package:

- Status is observational: on `held`/`releasing`, invoke a gateway command and let it wait for an independent lease; stop on `blocked`/unknown. `free`, settled, and zero active operations are availability observations only;
- recompile reports no failure/errors when code or the Shared plugin changed;
- current Unity error console is empty;
- final package inspect reports `status: valid` and `dirtyOrStale: false`;
- source and cooked-source hashes match;
- the real character prefab shows the intended static hold and entry/exit, or the motion's applicable semantic beats; do not require anticipation/recoil phases for a static pose;
- Ability Lab or Training exercises the semantic animation through the normal presentation path when available.

Record Unity-facing verification in the ignored root `TESTING-UNITY.md`.

## Presentation acceptance:

Apply motion-specific criteria only when that motion exists. For static holds, check
silhouette, intended foot/contact placement, root stability, clipping and entry/exit.

- anticipation direction is readable before the active event;

- the active pose is visually distinct from anticipation and recoil;

- the primary action reads from the normal gameplay camera without relying on close-up detail;

- the character silhouette does not collapse behind its own limbs/weapon;

- pose contrast is prioritized over smooth interpolation;

- no unnecessary motion competes with the gameplay-critical action.

## Exceptional recovery: exporter incompatibility

Do **not** make binary FBX editing the normal pipeline.

It is permitted only when all of these are true:

1. Blender has authored the desired bone rotations;
2. Unity rejects or corrupts the normal exporter output through bind-pose, unit, root-offset, or Avatar failure;
3. a known-good FBX container with identical rig/take/import contract exists;
4. only verified live rotation-key values can be replaced without changing hierarchy, rest transforms, translation, scale, key timing, take identity, or binary structure;
5. the patched result passes every Unity import, catalog, cook, and prefab gate above.

Document the reason, exact validation evidence, and recovery artifact before using this route. Never use it to bypass a package/compiler diagnostic.
