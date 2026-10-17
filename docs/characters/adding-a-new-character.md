# Adding a Character

A character is a package, not a registry factory. New package work must have an approved
kit and presentation specification before it becomes roster content.

## 1. Create authoring-ready source

Create `client/Unity/Assets/CharacterPackages/<package>/` through
`AbilityLabPackageWorkspace.NewPackage`. The pipeline writes an authoring-ready source
template with sane movement, capsule, hurtbox, presentation, thirty-tick timelines, and
one unique `anim.move.*` semantic ID per canonical slot. The catalog receives matching
empty binding rows.

Keep the three authoring modules separate:

- `package.json` owns identity, version, creator, license, attribution, and dependencies.
- `character.json` owns gameplay source, presentation IDs, and the canonical sixteen-slot
  grid, capture geometry/anchors, and standardized defense animation roles.
- `CharacterAssetCatalog.asset` owns Unity rig and clip bindings.

The starter values are editable defaults, not approved gameplay balance. Replace them with
the character's approved kit data before roster admission. Import package-local source assets
with their `.meta` files. Record licensing and stop if a source asset is not redistributable.

`character.json` uses `authoringSchemaVersion: 3`. Author a positive finite
`movement.airDodgeSpeed` independently of the historical `dashSpeed`; it is
the forward air dodge's horizontal speed for ten simulation ticks. The
current admitted fighters use 11 m/s except Wibou at 12.1 m/s. Older source
and cooked schema versions cannot be admitted without migration and a recook.

## 2. Validate and bind assets

New or substantially reworked move animations go through the
[animation user review checkpoint](../../skills/sloparena-animation-authoring/SKILL.md#user-review-before-move-integration)
before their move data or live catalog bindings change. Prepare an unbound preview
on the actual character, obtain animation acceptance, then integrate it below.
For new in-house motion, use the maintained
[GenRA animation workflow](genra-animation-workflow.md).

For generated-model cleanup or an existing-model replacement, follow the
[character model polish guide](character-model-polish.md) before final integration.
It separates surface repairs from rig changes and defines visual, retargeting and
cooked-pose acceptance.

### Roster size

The roster shares one size order (owner, 2026-10-17): Bonk is the biggest, FightGuy and
Wibou are the same height, and Manki is the smallest. Idle head heights above the floor
are Bonk 1.64 m, Wibou 1.34 m, FightGuy 1.33 m and Manki 0.84 m. Fit a new fighter
into that order with `presentation.visualScale`; the cook samples its hurtboxes at the
same scale. Then set `modelYOffset` so idle frame 0 stands on the floor and keep
`hipHeight` near the idle hip height: it is the pivot for attack-pose pitch and aim
correction. Hurtboxes and bone hitboxes follow the drawn hips through the cooked
`_hips_offset`, not `hipHeight`. Measure with the
[character size tools](../../tools/character_sizes/README.md).

Import the rig as Humanoid when the character is an ordinary humanoid fighter. Inspect the
Avatar before binding clips. Reject invalid orientation, scale, root motion, required-bone,
or non-finite-pose diagnostics. Do not remap a bad rig at runtime or own a standalone
skeleton binary.

Bind each required semantic animation ID to the exact imported clip in
`CharacterAssetCatalog.asset`. Assign one unique deterministic pose-track ID per semantic
ID. Confirm catalog package ID, schema version, sample rate, rig, and binding paths. Do not
use another character's clip as a fallback.

Optional `presentation.crouch` and `presentation.slide` IDs require real bound clips and
distinct deterministic pose-track IDs when nonempty. They may share one static low clip.
The renderer and authoritative hurtboxes hold its frame zero; enter/exit uses zero fade.
Author planted feet and a lowered articulated skeleton without changing the model root,
stage capsule or pushbox. Low-track baking inverts the renderer's root/visual-scale mapping
instead of subtracting the sampled hips; other tracks retain their existing convention.
Absent legacy bindings remain upright and cannot enable CrouchBrace. Never fabricate
duck protection with a shrunken hurtbox or a runtime offset.

Every package also authors a finite `shieldRadius` in meters, greater than
half its `capsuleHeight`. Shared uses that sphere for shielded attack contact,
centered on the capsule origin; the held VFX uses exactly the same radius.
Choose a radius that encloses the character's defense pose without hiding
the fighter. The value is cooked and hashed, not a Unity collider setting.

Every package must define `captureGeometry` in `character.json`: positive `reach`,
`width`, and `height`, a vertical `offsetY`, and local-space `attackerAnchor` and
`victimAnchor` positions in meters. Local +Z is fighter-forward. Keep the capture volume
short and grounded around the character's authored body size; place both anchors at
plausible torso height and forward of the character origin. The per-character values
are authoritative package content and flow through validation, cooking, and hashes;
never edit generated `character.runtime.json` as their source.

Ability Lab exposes the same `captureGeometry` under **Moves → Ground → Grab** as
numeric volume and restraint-anchor fields, with a Scene-view wire preview.
Grab is an editor-only selector, not a seventeenth canonical ability slot.
Changes participate in workspace undo and live draft validation; **Save** crosses
the normal package cook boundary. The active in-match grab Gizmo reads the
cooked value, never a separate Inspector copy.

For PvP, verify the cooked result and refresh the admitted roster pin after
saving; the transient Ability Lab preview does not change a pinned match.

The defense presentation roles are `presentation.shield`, `grab`, `grabbed`,
`throwForward`, and `airDodge`. They may be empty until real clips are authored. Once a
role has a semantic animation ID, the package cooker requires its normal asset-catalog
binding and deterministic pose track; do not map a role to a generic fallback clip.

## 3. Author the canonical grid

Persist these sixteen slot IDs, in ground-then-air order:

`ground.1`, `ground.2`, `ground.3`, `ground.4`, `ground.A`, `ground.E`, `ground.R`,
`ground.F`, then the same eight IDs under `air.`. Physical controls are input adapters,
not alternate move identity. Author fixed timelines in 60 Hz ticks with engine-owned
operations. Define damage, hitboxes, recovery, movement, and capability values only from the
approved kit specification.

`allowSlideCarry` defaults false. Only grounded normals 1–4 may opt in, without aiming,
recovery designation, SetVelocity, ForwardLunge, SetAimState or
StartCapability operations. Conflicts fail compilation with
`slot.slide-carry.motion-conflict`; aliases inherit the resolved target value.

### Optional attack hit VFX

Add an optional slot field such as:

```json
{"hitPresentationId": "presentation.fightguy.ki-shot.hit"}
```

Declare the ID in the document's `presentationIds` and bind it to an owned prefab
in `CharacterAssetCatalog.asset`. Ground/air aliases inherit the resolved target's
hit ID; explicit slots may use different IDs. Omit the field to retain the shared
light/medium/heavy/launch graphics. These are prefab bindings, not animation IDs
or pose tracks; the compiler and package admission require declared, bound IDs.

An override replaces only the hit visual. Shared accepted contacts supply the
captured slot/air identity and world contact position; shields, invincibility,
counters and misses do not emit custom hit events. PvP displays the confirmed
server event once, not predicted contact effects. Sounds, Hitstop and controller
feedback retain their existing behavior. Hit one-shots use the dispatcher's
150-tick cleanup limit; tune particle size/color on the owned prefab and keep its
emission finite.

### Startup aim correction

`startupAimCorrection` is an optional cooked timeline operation, reusable by any slot;
only the current 32 normals opt in, and existing specials are unchanged. Capture occurs
at ability activation (not first correction tick), using a valid selected/locked opponent
inside the acquisition range/cone or the nearest eligible opponent there; distance ties
break by entity ID. One target is retained for the activation and never switched. No target
means no correction. `RetargetPressed` still selects the nearest valid enemy under its
existing rule, independently of this captured target.

The operation has stage-relative fields `tick`, `unit: "ticks"`, exclusive `endTick`,
`acquisitionRange`, `acquisitionHalfAngleDegrees`, `maxYawDegrees`, `maxPitchDegrees`,
`yawDegreesPerSecond`, and `pitchDegreesPerSecond`. `endTick` is after `tick`, within that
stage, and no later than the earliest hitbox/projectile contact, forward lunge, or horizontal
SetVelocity launch commitment; an earlier cutoff is valid. It bounds correction to startup:
never active/recovery, no late acquisition or target switching, no physics pulling or
trajectory changes. Hitstop freezes correction with the attack clock; invalidation closes
the window. Completion, interruption and landing cancellation clear its pose/state.

Current normals use 4 m range, 60° forward half-cone, at most 45° yaw at 360°/s.
Pitch is at most 15° at 180°/s only for air.1/air.2 except Bonk air.2; other slots have
zero pitch. The pivot is the simulation hip-height world point (`BoneYToWorldY(PY, 0)`),
shared by model/collision/trails; opponent torso reference is `PY + 0.25 × targetCapsuleHeight`.
Pose pitch does not change the physics capsule. These are authored tuning values, not
balance claims.

## 4. Inspect before cook

Inspection is read-only and must happen before cooking:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
```

Require the expected package identity, sixteen-slot projection, source status, structured
source/catalog diagnostics, and exact dependency list. Repair invalid or stale inputs
before continuing.

## 5. Cook and verify four payloads

`SAVE + COOK` is the only authoritative cook path:

```bash
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

Authoring inspect/cook records the Editor target and Unity version; this remains
local authoring evidence. For character or cook changes before a Windows release,
use the isolated
[`release.py certify-cook` gate](../systems/release-pipeline.md#timings-and-opt-in-import-reuse)
against the prepared source. It verifies committed cooks before player compilation
and retains the native report and input fingerprint. If it differs, review the
cold cook correction rather than committing another warm Editor fingerprint.

A successful cook atomically produces exactly these payloads under
`content-cooked/<package>/`:

- `manifest.json` — identity, versions, dependencies, capabilities, and hashes;
- `character.runtime.json` — normalized Shared runtime definition;
- `poses.bin` — deterministic pose payload;
- `client.bindings` — generated semantic client bindings.

The generated catalog is a regenerable cache, not a source of gameplay truth. Require
matching source, cooked-content, package, payload, and dependency hashes. A failed cook
returns semantic `success: false` and preserves the last valid artifact, generated cache,
and cook status. It must not promote invalid drafts.

Cooked schema version remains 3 and runtime API maximum is `1.x`. The loader accepts
known minima `1.0.0` through `1.5.0`; select the lowest feature floor the authored content
requires. `1.0.0` is the baseline, timed gravity-window operations require `1.2.0`, armor
windows and nonzero `fixedHitstunTicks` require `1.3.0`, `startupAimCorrection` requires
`1.4.0`, and public charged directional dash requires `1.5.0`. A package combining
features uses the highest required minimum; older runtimes must reject features above
their API level. New additive source fields do not themselves change the cooked schema.

Semantic animation IDs and pose-track IDs are distinct namespaces: validated client
bindings map loaded pose tracks to runtime semantic lookup IDs.

## 6. Admit exact content

Only after a successful cook, complete presentation assets, and kit regression proof, add
the exact package requirement to `content-cooked/roster/manifest.json` and the corresponding
selector/admission path. Verify manifest identity, version, cooked hash, package hash,
capability versions, and client/server compatibility. A source-only package may be
discoverable in Ability Lab while remaining unrostered.

## 7. Exercise runtime surfaces

Open the package in Ability Lab and verify semantic bindings, canonical slot projection,
preview identity, and diagnostics. Then exercise Training and a local match with the same
cooked package. Server simulation remains authoritative; Unity presentation resolves the
cooked semantic IDs and never decides hit results, damage, timing, or admission.

## 8. Add regression coverage

Once gameplay is specified and the package cooks successfully, add focused compiler,
catalog, and package tests. Add `KitScenario` golden snapshots for authored damage,
knockback, timing, recovery, and capability behavior. Do not add gameplay goldens for a
source-only probe with invented values.

## Prohibitions

Do not add `Build<Name>`, registry factories, legacy adapter branches, raw runtime JSON
loaders, manual animation configs, standalone skeleton ownership, or a second persisted
move mapping. Nilus compatibility and character/slot factory dispatch have been removed.
