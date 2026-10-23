# Ability Lab

Ability Lab is a package-first UI Toolkit editor shell for authoring and inspecting fighter
packages. The shell owns selection, preview status, diagnostics, stage/tick controls, and
an embedded preview. Package authoring and cooking remain in `AbilityLabPackageWorkspace` and
`CharacterPackageAuthoringService`.

## Opening and preview ownership

Open **Tools → SLOPArena → Ability Lab** from any scene. The window automatically
owns one temporary Unity preview scene, with its fighter, optional opponent,
camera and lights. Switching packages reuses that environment. Preview objects
are excluded from the ordinary Scene/Game views and Hierarchy and cannot be
saved into the open scene. Closing the window, recompiling scripts or entering
Play Mode releases the preview and its animation graphs; returning to Edit Mode
recreates it. The saved `AbilityLab.unity` scene is no longer needed.

The window uses its embedded viewport for camera navigation and placement
handles. Right-drag orbits, middle-drag pans, and scrolling zooms. Package
workspaces update their own preview rig rather than a scene singleton.

This replaces the former **Create Lab Rig** workflow. Automatically opening the
saved Lab scene would remove manual browsing but still interrupt scene editing;
the isolated preview keeps the current scene, camera, lighting and save state
untouched.

## VFX authoring

1. Open the character package. In **Assets → Presentation Assets**, assign an
   owned prefab to a presentation ID. Keep vendor source prefabs unchanged.
2. In **Moves**, select a VFX timeline cue and edit its start tick, duration,
   attachment bone, position, rotation and scale. Scrub or play the move to inspect
   it; viewport handles edit the selected cue's placement.
3. Run **Scenario Controls** against an idle opponent, a shield and a distant
   opponent to compare hits, blocks and whiffs. Use **Save + Cook** when ready to
   persist the package.

Missing VFX prefab bindings and attachment bones appear below the timeline.
Native `sloparena.lab.inspect` also exposes `presentationDiagnostics` and
`activeVfxCount` for the selected frame. A missing bone retains the existing
event-position fallback and reports it explicitly.

Recorded previews and match playback share the attached-effect lifetime rules:
hitstop retains the remaining attack ticks; interruption, attack replacement and
attack completion end attached effects. Scrubbing backward reconstructs the
effect from recorded states. World contact bursts keep their independent lifetime.
Particle animation continues with elapsed match time, as it does in matches;
retaining an effect during hitstop does not freeze its particles.

Preparing a valid in-memory preview keeps a saved, current package **Cooked**.
The open workspace observes external cook and asset changes once per second
without replacing its draft, Undo history, selected move or cursor. External
source/binding changes produce a diagnostic and retain the current draft.
Preview effects are transient and are cleared before reload; Lab animation
graphs are released at compilation start and before reload, including the
inactive dummy's graph and rigs placed in a scene.

Regression check: **Tools → SLOPArena → Tests → Ability Lab VFX Workflow**.
It checks match/preview lifetime parity and draft-safe status refresh without
saving or cooking character content.

## Agent-facing workspace commands

Typed Unity Pipeline commands expose package open, Shared two-fighter scenarios,
recorded-frame preview, read-only inspect, and PNG capture without temporary C#.
Use native commands first for gameplay setup and evidence; do not use `eval` to
construct fighters or emulate gameplay.

Use the [Unity CLI gateway](../contributing/unity-cli.md#pipeline-connection-and-shared-editor-leases),
with your own caller identity under the
[coordination protocol](../contributing/unity-editor-coordination.md). Keep dependent open/run/inspect/preview/capture operations in one
`--batch` so another lease cannot change the workspace between them.
These Edit Mode examples intentionally leave the Lab open with the selected
package; add any task-required restoration to `restore` before invoking them.

`inspect` returns the current `rigInstanceId`. Hidden preview rigs cannot reliably
be resolved by hierarchy path; if a typed transform command is needed, use
`--target instanceId:<rigInstanceId>` from the current inspection. Do not reuse
an instance ID across rig recreation or reload.

Save this batch as `/tmp/ability-lab-scenarios.json`. It observes a close idle
opponent, a shield, a distant miss, and a Shared grab; outcomes are not forced:

```json
{
  "commands": [
    ["sloparena.lab.open", "--target", "fightguy"],
    ["sloparena.lab.run", "--action", "ground.1", "--ticks", "60", "--distance", "1.2", "--opponent", "idle"],
    ["sloparena.lab.run", "--action", "ground.1", "--ticks", "60", "--distance", "1.2", "--opponent", "shield"],
    ["sloparena.lab.run", "--action", "ground.1", "--ticks", "60", "--distance", "12", "--opponent", "idle"],
    ["sloparena.lab.run", "--action", "grab", "--ticks", "60", "--distance", "0.7", "--opponent", "idle"],
    ["sloparena.lab.inspect"]
  ],
  "restore": []
}
```

```bash
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity \
  --batch /tmp/ability-lab-scenarios.json
```

The UI presents both fighters and scenario controls; native `run` options are
`--action <canonical-id|grab>`, `--ticks <last-frame>` (0–3600),
`--distance <metres>`, `--opponent idle|shield`, `--damage 0..999`,
`--facing <relative-degrees>`, and `--charge-ticks 0..600`.
Defaults are 60, 2.5 m, idle, 0, 180°, and zero held ticks.
Run outcomes are observed, never forced: a successful command can be a hit,
zero-damage block, miss, grab whiff, or capture/throw.

`preview --action <id|grab> --tick <frame>` seeks a recorded scenario when its
action matches the latest run. `capture` likewise consumes that same recorded
run and options when the action matches; it does not substitute defaults.
Without a matching run, grab preview/capture creates a default grab scenario,
not a copy of another action's options/horizon; canonical actions retain
ordinary authoring preview. Run explicitly first to choose opponent settings.
Capture frames into the ignored cache. Save this complete sequence as
`/tmp/ability-lab-grab-capture.json`; choose a fresh output directory for each run:

```json
{
  "commands": [
    ["sloparena.lab.open", "--target", "fightguy"],
    ["sloparena.lab.run", "--action", "grab", "--ticks", "60", "--distance", "0.7", "--opponent", "idle"],
    ["sloparena.lab.preview", "--action", "grab", "--tick", "7"],
    ["sloparena.lab.capture", "--action", "grab", "--ticks", "7,19", "--output", ".ability-lab-cache/fightguy/grab"]
  ],
  "restore": []
}
```

```bash
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity \
  --batch /tmp/ability-lab-grab-capture.json
```

Scenario frame indexes are zero-based: frame 0 contains the first input at
MatchTick 1. This is distinct from canonical authoring preview, whose requested
cumulative duration endpoint maps to the final authored stage tick
(`durationTicks - 1`). Scenario output contains `scenario.frames[]` with
`frameIndex`, `matchTick`, and both fighters' position, movement velocity,
knockback velocity, facing yaw (radians), damage, state/state ticks, grounded,
hitstop, hitstun, block stun, and interaction phase/IDs/timing. `contacts[]`
are accepted Shared hit/block records (including damage, blocked flag, impact
force, knockback direction/angle, stun/hitstop, and hit position); paired grab
capture/release appear in `interactions[]`, not fabricated hit contacts.
`presentationEvents[]` and `deaths[]` report observed Shared events and deaths.

For deeper collision diagnosis, the Shared simulation exposes the read-only
`ServerSimulation.LastTickAttackEntities` view of exact attack-collision
surfaces consumed on the latest pass (including active shields). It is a
diagnostic surface, not a separate simulation or a replacement for reported
accepted `contacts[]`.
Use semantic `data.result.success`, inspect diagnostics, and assess these
observations rather than treating command success as proof of a hit.

All mutating commands are Edit Mode only. Invalid options, unavailable/invalid
drafts, missing package/rig, and invalid action/frame return structured
diagnostics (for example `scenario.options.invalid`,
`scenario.action.unavailable`, `preview.tick.out-of-range`,
`lab.mode.unsupported`). Capture also rejects unsafe paths, symlinks, invalid
dimensions, duplicate ticks, and existing output files; a failed batch removes
its partial PNGs. Capture restores the prior scenario/cursor, playback,
visibility, camera, and render target on success or failure.

Capture uses a temporary camera with the preview view's orientation and fits
the complete visible Lab fighter/weapon bounds for each requested frame.
Camera framing can change between frames; PNG scale is not a reach comparison.
Scene camera layer masks, lens shifts, and partial viewports do not crop the
capture. Only Lab-owned renderer hierarchies are included, including visible
scenario opponents; unrelated scene/editor-preview renderers are temporarily
excluded and their rendering state is restored. Weapon props follow their
owner's hierarchy, so hiding a fighter also hides its weapons.
Capture temporarily enables per-render skinned-mesh matrix recalculation so
freshly evaluated bones are reflected in the PNG without waiting for an Editor
frame. Original renderer settings are restored afterward.

Authoring and recorded-frame scrubs explicitly evaluate skeletal poses even
when the model is offscreen. The Animator's culling policy is restored after
each seek. Edit Mode playback is driven by the editor update loop and editor
clock, not by updates on the hidden preview rig. Play Mode authoring retains
scaled delta-time behavior; recorded scenarios retain their realtime clock.

Aerial authoring previews suspend ambient gravity for the isolated actor, so
long moves remain at their starting height after the character's float window.
Authored vertical velocity and capability-owned motion still execute in Shared.
Recorded two-fighter scenarios and matches retain normal gravity; use a scenario
to inspect falling, landing, and impact timing.

Package authoring and recorded previews select clips from the Shared state's
animation phase, independently of the number of timeline stages, and use the
same playback-speed calculation as the runtime renderer. Aim/release capabilities
can have multiple animation phases inside one timeline stage; clamping the phase
to that stage count would display the wrong clip and pose. Live-draft refreshes
also reattach the selected catalog's weapon props when reusing preview renderers.

The active preview owns its temporary animation-catalog snapshot. Reapplying the
same snapshot must preserve it; only replacing it with a different snapshot or
closing the preview may destroy it. Save status notifications can synchronously
refresh the UI before the save path reapplies the preview.

The workspace prepares current source in memory through the existing compiler,
verified poses, catalog, and rig; it does not save, cook, change Undo history,
or write source/cooked files. Ordinary window refresh applies this live draft
even when saved content is available. The persisted loader supplies verified
runtime/pose/catalog/rig prerequisites, not proof that the scenario executed
the saved definition. Missing/invalid persisted pose/rig prerequisites block
scenarios. A clean in-memory prepared source can
report `dirty: false` while `authoritativePreview: false`; dirty describes
workspace edits, not whether a scenario hit. Never claim online play or game
feel from these isolated scenarios.

Authoring preview/capture retains canonical slot IDs and cumulative 60 Hz
ticks from move start, including zero and the requested duration endpoint;
that endpoint maps to the final authored stage tick (`durationTicks - 1`).
Capture `captures[]` reports action, requested tick, applied stage/local and
cumulative tick for authoring, or frame and MatchTick for scenarios, plus PNG
path. Root state is the restored workspace snapshot. Capture writes only below
repository-relative `.ability-lab-cache/`; batches allow at most 64 distinct
ticks and dimensions 64–4096. The compact Pipeline result is under
`data.result`; outer transport success is not semantic success.

See the [Unity CLI reference](../contributing/unity-cli.md) for command
discovery and native-command-first routing.

## Package discovery and workflow

1. Open `Tools → SLOPArena → Ability Lab` from any scene. The window creates its
   own isolated preview scene; no component in the open scene is required.
2. Select a source package from `Assets/CharacterPackages`. Discovery may show a source-only
   package by display name and stable package ID even when it has no cooked output or roster
   admission.
3. Open the package in package mode. Ability Lab inspects source and catalog state; opening
   or editing does not cook, write generated bindings, update cook status, or change roster
   rows.
4. In the Editor, the rooted resolver checks repository `content-cooked` first,
   then `Application.streamingAssetsPath/content-cooked`. Players use StreamingAssets
   only. The resolver never depends on the process working directory. See
   [`LocalContentResolver.CreateDefault`](../../client/Unity/Assets/Scripts/Runtime/Content/LocalContentResolver.cs).
5. The persisted preview loader requires a verified cooked manifest, runtime definition, pose
   payload, animation catalog, and rig. In the Editor, a missing generated catalog (fresh
   clones never have one) is rebuilt from the package's committed `client.bindings`. Bound
   animation clips that are not installed stay empty: those moves keep their hitboxes and
   hurtboxes from the cooked poses but don't animate, and the console warns once per move
   ([ADR-0038](../adr/0038-editor-play-without-installed-clips.md)). Missing or invalid
   content shows `Preview unavailable` with structured code, path, and message diagnostics.
   It never falls back to FightGuy or legacy content. A throwaway source-only fixture is
   therefore discoverable but cannot preview without those prerequisites. Bonk is
   already rostered; it is not an unrostered pipeline probe.
6. With a valid package preview, scrub the stopped timeline at tick `0` or later. Edit Mode
   refreshes the renderer, baked bones, hurtboxes, and hitboxes in the embedded viewport.
   [`AbilityLabWindow.RefreshPreview`](../../client/Unity/Assets/AbilityLab/Editor/AbilityLabWindow.cs)
   prepares and applies the current workspace draft. This sets `authoritativePreview: false`
   even when the toolbar reports **Cooked**.
7. `SAVE + COOK` saves package source and invokes the validated cook. The cook publishes
   payloads, intermediate files, generated catalog, and cook status, then refreshes an
   existing roster pin. It does not admit a new package. Preparation failures preserve
   the prior cook; publication failures attempt rollback. A later roster error can return
   failure after publication succeeded. Inspect artifacts and pins before retrying. See
   [cook mutation and failure scope](../characters/adding-a-new-character.md#5-cook-and-verify-four-payloads).
8. Editor Training and Solo Play have a separate `edit → Play` path. They compile the current
   source and semantic assets in memory into the existing cooked runtime/catalog types. Invalid
   source blocks match start; it never falls back to a persisted cooked package. When the only
   problems are bound clips, presentation prefabs or weapon prefabs that are not installed,
   the current source plays with the committed pose data instead, unless the edit changes
   how poses are baked ([ADR-0038](../adr/0038-editor-play-without-installed-clips.md)).

Ordinary Lab and Editor Training are source-iteration paths. Matching disk hashes
and a **Cooked** status establish source/cook agreement, not saved gameplay playback.
For accepted content, execute affected gameplay through the persisted Shared catalog
or a player using staged saved content. The
[persisted gameplay verification recipe](../characters/adding-a-new-character.md#verify-persisted-gameplay)
names the existing resolver API and a Bonk hit/block/miss test. No saved-content
selection mode is exposed by the ordinary Lab window.

The compact toolbar status has this precedence: `No package → Unsaved → Cooking… → Cook
failed → Stale → Cooked`. Clicking status opens the structured diagnostics panel. Hashes and
raw IDs are shown only in Advanced.

Weapon props use the package catalog's `WeaponConfig`; timeline VFX use separate
semantic presentation bindings. Manki F holds `manki_aerosol.prefab` on the right
hand during aiming/attacking and emits `MankiAerosolInferno` at attack tick 18.
Presentation attachment IDs such as `bone.right-hand` resolve to the rig's
`mixamorig:RightHand` transform; they are not literal transform names.

## Authority boundary

Canonical package slot IDs are the persisted move identity. `CanonicalSlotProjection.All`
exposes the sixteen read-only `SlotAddress` values in ground-then-air order, with input
labels `1`, `2`, `3`, `4`, `A`, `E`, `R`, `F`. Human labels and `CharacterClass`
roster selectors are adapters only; see ADR-0030.

Valid stale source previews as a live draft using the verified pose/catalog/rig
prerequisites. Invalid drafts stop simulation and show diagnostics rather than
silently playing the old definition. Missing or invalid cooked prerequisites produce
`Preview unavailable`. Missing catalog/rig bindings are reported at the preview seam.
The window owns the temporary rig in its preview scene.

## Moves interaction

The Moves workspace has a live Unity camera viewport, tick ruler/transport directly beneath,
and a resizable fields rail. The scene uses the existing Lab camera and renderer/overlays;
one reusable render target isolates camera clearing from native Editor controls. It is
released on UI rebuild/owner disable and does not create another actor or simulation.
Hitboxes, Hurtboxes and Motion control the existing Lab overlay flags. Missing package,
rig/camera and invalid-draft states are explicit rather than showing a stale current preview.
On the timeline, the hitbox and hurtbox overlays show what the Shared preview
simulation tested on the tick the model shows (`SpellResolver.LastTickCheckedHitboxes` and
`LastTickAttackEntities`), so they follow the move's real movement, including rises and
dives. A bone hitbox also draws, in darker orange, the band Shared swept through since the
previous tick (`Hitbox.Sweep`): each swept pose's axis joined by its start and end paths.
Recorded scenarios keep every tested hitbox per frame, including one on its last
active tick. Without a Shared run, overlays fall back to the authored approximation
(grounded, forward lunges only, one tick behind the model).

`Move overview` exposes authored timing/animation; selecting an inventory effect switches to
`Selected effect` with source-addressed detail. The inventory stays available in either mode.
Selecting an effect or inspected stage changes the authoring
context without seeking the preview. `Seek tick` uses cumulative move ticks; `Seek to
selected effect` explicitly seeks its source-stage start. Field bounds and edits use the
inspected source stage, not the preview stage. Commits and Undo retain the cumulative cursor
and camera; shortening a move clamps the cursor and explains the change. Capability-owned
or conditional windows remain labelled as such; unavailable editors are not presented as complete.

Public `spawnProjectile` operations expose **Timing**, **Flight**, **Launch**, and
**Combat** fields: spawn tick, flight lifetime, speed, gravity, radius, local launch
offsets, yaw spread, damage, knockback and stun. Edits address the selected source
operation, including individual spread shots and operations reached through air aliases.
They compile into the live draft, use the existing Undo/Redo history, and do not save
or cook automatically. Invalid projectile values report compiler diagnostics.

`Detach fields` moves the same fields subtree into a native Editor pane, sharing the owner's
draft and Undo history. Dock panes and SceneView manually. Closing the fields pane or using
`Return fields inline` restores the fields inline without saving; other authoring tabs hide
detached Moves controls. Detaching reclaims the rail's scene space; returning restores it.
Fields scroll independently of transport, adapt to narrow panes, and keep native text-input
shortcuts. Narrow owners stack the panes; the scene column has native scrolling so expanded
scenario/phase controls remain reachable in short surfaces. There is no automatic window
layout or Training handoff; users still control docking and SceneView authoring handles.

The Moves timeline supports snapped marker/body drags and hitbox endpoint resizing. Each
release applies one immutable source edit and one workspace Undo snapshot; canceled or
unchanged drags do not mutate the draft. The retained timeline caches its projection while
scrubbing, supports `0.5×..4×` zoom with horizontal scrolling, and preserves inspector
control identity. Root and timeline focus support Left/Right tick stepping, `Ctrl+S`,
`Ctrl+Z`, `Ctrl+Shift+Z`, and Escape drag cancellation; text fields keep their normal editor
shortcuts.

`Add targeted leap` creates one public `slop.ability.targeted-leap.v1` operation
and its package capability requirement in the same Undo step. Its Moves inspector
edits aim/flight limits, range, vertical launch, landing animation seek, recovery,
and the nested landing hitbox; changes compile into the live draft without saving.
The seek plus recovery must fit the authored stage, and the hitbox duration must
fit recovery. Ordinary timeline scrubbing cannot predict the landing frame of a
variable-length flight: use a recorded Shared scenario to inspect the actual
landing and impact. `SAVE + COOK` persists the source/cooked package and automatically
refreshes an existing roster pin. New admission remains a separate accepted-content step.

`Add charged directional dash` creates public
`slop.ability.charged-directional-dash.v1`, raises the runtime API minimum to
`1.5.0`, sets the directional aim/lifecycle contract, and extends a short stage
to fit travel/seek/recovery in the same Undo step. Its Moves inspector edits
charge cap and tier thresholds, range/speed, finisher lead/animation seek,
recovery, tier damage, and both nested traversal/finisher hitboxes. Live-draft
edits affect Shared scenarios before saving. The Scenario **Charge ticks**
control holds aim for that many simulation ticks, then releases; a capped charge
stays held until release. Recorded charge previews show tier-coloured endpoint
rings. Native `capture` without `--charge-ticks` retains a matching recorded
scenario; explicit `--charge-ticks 0` requests a tap rather than reusing a hold.
Zero means no held input for every action, including targeted leaps; it never
silently adds a hold. Use an explicit charge duration (for example 10 ticks) when
a leap scenario needs to acquire a target before release. Changing the opponent
control invalidates the previous run; the next receipt reports the recorded
distance, opponent behavior and starting damage.


In package Edit Mode, active resolved hitboxes expose selection buttons and a radius
handle in the embedded viewport. Radius changes commit through the same source workspace authority.
Unavailable previews, playback, and non-Moves pages expose no package handles or mutation
path. Existing `AbilityLab.OnRenderObject` remains the only visible geometry path, including
optional baked bones.

## Agent workflow

Inspect is read-only and reports source status, canonical slots, hashes, stale reasons, and
structured diagnostics:

```bash
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity \
  -- sloparena.character.inspect --target fightguy
```

Cook publishes only a validated package:

```bash
bun scripts/unity-editor-gateway.ts \
  --project-path client/Unity \
  -- sloparena.character.cook --target fightguy
```

Inspect both artifacts and roster pins after a cook failure, using the failure scope
above. The Compatibility preview tab and legacy execution path are retired.
Do not delete or rename source packages or cooked artifacts while running the
package/frontend self-tests. Package creation currently has no Ability Lab UI control; use
the existing `AbilityLabPackageWorkspace.NewPackage` editor seam until onboarding is scoped.
