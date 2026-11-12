# Adding a Character

A character is a package, not a registry factory. New package work must have an approved
kit and presentation specification before it becomes roster content.

For text-first and AI-assisted authoring, use the [Workshop authoring reference](workshop-authoring-reference.md). It documents the JSON contract, the Unity-free template and checker, operation parameters, public capabilities, and the limits of source-only validation. The Editor workflow below also handles built-in asset bindings.

## 1. Create authoring-ready source

Create `client/Unity/Assets/CharacterPackages/<package>/` through
`AbilityLabPackageWorkspace.NewPackage`. The pipeline writes an authoring-ready source
template with starter movement, capsule, hurtbox, presentation, thirty-tick timelines, and
one unique `anim.move.*` semantic ID per canonical slot. The catalog receives matching
empty binding rows.

Keep the three authoring modules separate:

- `package.json` uses `manifestSchemaVersion: 1` and owns `packageId`, version,
  creator, license, attribution, and dependencies.
- `character.json` uses `authoringSchemaVersion: 4` and owns gameplay source,
  presentation IDs, and the canonical fourteen-slot
  grid, capture geometry/anchors, and standardized defense animation roles.
- `CharacterAssetCatalog.asset` uses catalog schema 2 and owns Unity rig, clip and
  effect bindings, plus the character's sound pools, respawn tint and projectile
  styles. Its package ID must match `package.json`.

Do not repeat identity in `character.json`. The source codec rejects legacy `id`,
`class`, and `schemaVersion` fields. See
[`CharacterPackageSourceCodec`](../../src/Shared/CharacterPackageSourceCodec.cs) and
[`CharacterAssetCatalog`](../../client/Unity/Assets/Scripts/Runtime/Animation/CharacterAssetCatalog.cs)
for the module contracts.

The starter values are editable defaults, not approved gameplay balance. Replace them with
the character's approved kit data before roster admission. Import package-local source assets
with their `.meta` files. Record licensing and stop if a source asset is not redistributable.

`character.json` uses `authoringSchemaVersion: 4`. Author a positive finite
`movement.airDodgeSpeed` independently of the historical `dashSpeed`; the air
dodge's burst starts at 2.3 times this speed and eases out over twelve simulation
ticks. The current admitted fighters use 11 m/s except Wibou at 12.1 m/s. Older source
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

After a model swap or visual-scale change, run the
[hurtbox fitter](../../tools/character_sizes/README.md#fit-and-check-hurtboxes) for that package.
It changes only `hurtboxBoneDefs` and cooks the result through the gateway.
Inspect the resulting capsules in idle, an attack mid-swing, and hit-hard from front and side.
Exclude cosmetic cloth and hats. Include tails and body shells.
Re-run coverage and the cooker and Rendered Pose Alignment self-tests before accepting the model.

Import the rig as Humanoid when the character is an ordinary humanoid fighter. Inspect the
Avatar before binding clips. Reject invalid orientation, scale, root motion, required-bone,
or non-finite-pose diagnostics. Do not remap a bad rig at runtime or own a standalone
skeleton binary.

Enable **Read/Write** on the body model's import. The match draws an ink line around every
fighter from smoothed normals that `FighterInk` works out when the fighter spawns
([ADR-0066](../adr/0066-stage-look.md)); a mesh that is not readable gets no line and logs one
warning. The setting is part of the model's `.meta`, so changing it recooks the package.

Bind each required semantic animation ID to the exact imported clip in
`CharacterAssetCatalog.asset`. Assign one unique deterministic pose-track ID per semantic
ID. Confirm catalog package ID, schema version, sample rate, rig, and binding paths. Do not
use another character's clip as a fallback. An owner-approved stand-in is different: Bashie's package
(2026-10-22) plays raised copies of Manki's clips, labelled in [her page](bashie.md), until her own moves replace them.

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

For PvP, verify the cooked result and the roster pin. Cooking an already-rostered
package refreshes its pin automatically; the transient Ability Lab preview does
not change a pinned match.

The defense presentation roles are `presentation.shield`, `grab`, `grabbed`,
`throwForward`, and `airDodge`. They may be empty until real clips are authored. Once a
role has a semantic animation ID, the package cooker requires its normal asset-catalog
binding and deterministic pose track; do not map a role to a generic fallback clip.

The optional `presentation.wallJump` role binds a dedicated wall-jump clip. An absent,
empty, or unresolved client binding falls back to `presentation.jump`. It follows
`slide` through source and cooked presentation data. A bound clip requires the normal
asset-catalog binding and pose track because package admission requires a track for
every animation binding. Simulation hurtboxes still use the existing jump pose;
`wallJump` does not enter the Arcade gameplay fingerprint. Like `flying` and
`mayhemLaugh`, this optional presentation role does not change the cooked schema,
runtime API minimum, or simulation protocol.

## 3. Author the canonical grid

Persist these fourteen slot IDs, in ground-then-air order:

`ground.1`, `ground.2`, `ground.3`, `ground.A`, `ground.E`, `ground.R`, `ground.F`,
then the same seven IDs under `air.`. Normals are `1`, `2`, and `3` in authoring schema 4. Physical controls are input adapters,
not alternate move identity. Author fixed timelines in 60 Hz ticks with engine-owned
operations. Define damage, hitboxes, recovery, movement, and capability values only from the
approved kit specification.

When you add or retime a move, add or update its entry in
`scenarios/moves/<package>.json`. The
[move review recipe guide](../../scenarios/moves/README.md) covers semantic and pad
inputs, forms, dummy arrangement, and phase anchors. Each form board shows coverage
of the fourteen canonical slots, including aliases and slots without a recipe.

`allowSlideCarry` defaults false. Only grounded normals may opt in, without aiming,
recovery designation, SetVelocity, ForwardLunge, SetAimState or
StartCapability operations. Conflicts fail compilation with
`slot.slide-carry.motion-conflict`; aliases inherit the resolved target value.

`aimMovementSpeedScale` defaults to 1 and is written only when lower. On an
`aimMovement: mobile` slot it walks the fighter at that fraction (greater than 0, at most 1)
of its run and air speed while the slot's aim state lasts; a fixed slot rejects it with
`slot.aim-movement-speed.requires-mobile`. When a timeline `setAimState` ends a mobile aim,
the walking stops, so it never carries through the rest of the move.

### Optional attack hit VFX

Set the catalog's `PresentationProfile` for fighter-wide media. Sound pools use
Fixed or Cycle selection; Launch uses the Heavy impact pool. An empty Fixed Swing
pool means silence. Keep ordered silent positions when a pool intentionally skips
a cue. Use package-owned or explicitly approved shared audio assets.

Projectile rows use canonical slot IDs such as `ground.A` and `air.A`, with a
prefab, scale, flight orientation and optional semantic held-bone ID. Declare
ground and air rows separately. The profile also owns removal-explosion suppression;
the global projectile config retains generic explosion defaults. Adding a fighter
does not require sound, tint or projectile branches in the runtime. Cooking carries
these settings through binding schema 2 into the admitted generated catalog.

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

`startupAimCorrection` is an optional cooked timeline operation, reusable by any slot.
Only slots that author the operation opt in, including normals and selected specials
such as FightGuy's `ground.R` and `ground.F`. Capture occurs
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

Read acquisition range, cone, yaw, and pitch tuning from each slot in its package's
`character.json`. For example,
[`fightguy/character.json`](../../client/Unity/Assets/CharacterPackages/fightguy/character.json)
authors a 180° acquisition half-angle for its opted-in slots, not a shared forward cone.
The pivot is the simulation hip-height world point (`BoneYToWorldY(PY, 0)`),
shared by model/collision/trails; opponent torso reference is `PY + 0.25 × targetCapsuleHeight`.
Pose pitch does not change the physics capsule. These are authored tuning values, not
balance claims.

## 4. Inspect before cook

Inspection is read-only and must happen before cooking:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
```

Require the expected package identity, fourteen-slot projection, source status, structured
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

A successful cook publishes these payloads under
`content-cooked/<package>/`:

- `manifest.json` — identity, versions, dependencies, capabilities, and hashes;
- `character.runtime.json` — normalized Shared runtime definition;
- `poses.bin` — deterministic pose payload;
- `client.bindings` — generated semantic client bindings.

The cook also writes intermediate pose/binding files, the generated client catalog,
and cook status. For an already-rostered package, publication includes that package's
version and hash requirement in `content-cooked/roster/manifest.json`, preserving
its selector and other entries. It does not admit a new package. See
[`CharacterPackageAuthoringService.Cook`](../../client/Unity/Assets/Scripts/Editor/CharacterPackageAuthoringService.cs).

The generated catalog is a regenerable cache, not a source of gameplay truth. Require
matching source, cooked-content, package, payload, and dependency hashes. Preparation
failures leave the prior cook intact. Publication retains backups through package,
generated-catalog, status and roster verification. A caught failure restores only
unchanged transaction-owned bytes; conflicting writes retain recovery evidence and
block a blind retry. This is not a crash-atomic multi-file transaction. Lab saves
source before invoking the cook.

Cooked schema version is 5 and runtime API maximum is `1.x`.
[`CharacterPackageCompiler`](../../src/Shared/CharacterPackageCompiler.cs) selects
the highest API floor required by the authored features, starting at `1.2.0`.
Read its feature checks for the full mapping. Steered charge, charged stream, and
returning projectiles require `1.8.0`; aimed recovery requires `1.9.0`, and its hop
requires `1.10.0`. Do not hand-select a floor in generated output. The
[`loader`](../../src/Shared/CookedCharacterPackageLoader.cs) accepts known minima
through `1.10.0`, including older `1.0.0` and `1.1.0` declarations. Those accepted
declarations are compatibility support, not the current compiler baseline.

Semantic animation IDs and pose-track IDs are distinct namespaces: validated client
bindings map loaded pose tracks to runtime semantic lookup IDs.

### Workshop model swap files

A model swap uses `item.json` plus one GLB, separately from the four cooked files.
[`WorkshopItemManifestCodec`](../../src/Shared/WorkshopItemManifest.cs) reads schema 1.
Its required fields are `schemaVersion` (integer), `itemType` (`model-swap`),
`itemVersion` (nonempty string), `payloads` (file name to lowercase SHA-256 object),
`creator`, `license`, `attribution`, `aiAssisted` (boolean), and
`baseFighterPackageId` (stable package ID). All text fields are strings.
`fighter` and `stage` are unsupported. Unknown, duplicate, missing, or incorrectly
typed fields fail with named diagnostics. The manifest limit is 16,384 bytes;
text and file names are limited to 1,024 UTF-16 code units. Payload names have no
separators, colons, control characters, or `..`.

`Read` returns an immutable manifest only on acceptance. `Write` emits compact
canonical UTF-8 in fixed field order, with ordinal payload ordering. `Verify`
checks the actual payload map for missing files, extra files, and hash mismatches.
`HashPayload` computes a payload SHA-256. `TryWriteSteamMetadata` emits the compact
payload hash object and refuses output of 5,000 UTF-8 bytes or more.

[`WorkshopGlbPrecheck.Check`](../../src/Shared/WorkshopGlbPrecheck.cs) returns
`IsAccepted`, `CharacterDiagnostic` entries, and `Measurements`. Measurements count
vertices and triangles per mesh primitive, including repeated accessor references.
Triangle lists use count divided by three; strips and fans use count minus two.
Points and lines contribute no triangles. Nodes, materials, and images are array
lengths; joints are the largest skin's joint array length. A node must reference
both a mesh and a nonempty skin. PNG IHDR and JPEG frame headers supply each image's
width and height without image decoding. The check does not establish Humanoid
compatibility or replace the importer's complete glTF validation.

All buffers and images must be embedded, with bounded buffer views. Every
`extensionsRequired` entry is refused by name, except `KHR_mesh_quantization`,
which the importer reads without an extra package. Container lengths, JSON syntax,
duplicate JSON properties, depth, indices used by this check, and count arithmetic
are checked before use. No allocation uses a declared binary count. JSON has a
configurable size and depth cap, with hard ceilings of 16 MB and depth 128.
Limit diagnostics include the maximum and measured value. A structurally refused
file can have partial measurements.

`WorkshopGlbLimits.Default` keeps all defaults in one type. Measurements below
came from running the check on 2026-11-11 against the seven reference exports and
the read-only Tripo spike. No model files are included in the tests.

| GLB | File bytes | Triangles | Vertices | Nodes | Largest skin | Materials | Images | Largest image | JSON bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| fightguy | 1,683,400 | 8,695 | 23,032 | 45 | 41 | 1 | 1 | 1024 × 1024 | 232,496 |
| manki | 3,863,948 | 64,250 | 67,398 | 116 | 100 | 9 | 5 | 1024 × 1024 | 453,532 |
| wibou | 2,138,428 | 38,080 | 37,934 | 60 | 53 | 3 | 3 | 1024 × 1024 | 202,468 |
| bonk | 4,034,316 | 69,847 | 85,753 | 57 | 45 | 3 | 3 | 1024 × 1024 | 243,004 |
| bashie | 3,647,016 | 50,627 | 45,957 | 75 | 59 | 7 | 7 | 1024 × 1024 | 328,180 |
| salamander | 2,575,064 | 31,612 | 29,408 | 75 | 65 | 3 | 3 | 1024 × 1024 | 324,784 |
| nyx | 7,316,868 | 112,331 | 126,416 | 134 | 116 | 5 | 5 | 1024 × 1024 | 528,816 |
| dusty (Tripo) | 27,926,692 | 49,274 | 48,813 | 43 | 41 | 1 | 1 | 4096 × 4096 | 38,352 |
| Default limit | 30,000,000 | 250,000 | 260,000 | 300 | 250 | 20 | 16 | 4096 × 4096 | 1,200,000 |

The pixel limit per image is 16,777,216, and JSON depth defaults to 64.
Defaults round up near twice the built-in maxima, except file size and texture
size, which admit the Tripo file. All eight files pass. The seven built-in
exports declare `KHR_mesh_quantization`. The owner confirmed these defaults on
2026-11-11.

## 6. Admit exact content

For new admission, require a successful cook, complete presentation assets, and kit
regression proof before adding the exact package requirement to
`content-cooked/roster/manifest.json` and the corresponding selector/admission path.
Recooking an existing entry already refreshes its pin before acceptance checks;
that automatic mutation is not acceptance. Verify manifest identity, version, cooked
hash, package hash, capability versions, and client/server compatibility. A source-only
package may be discoverable in Ability Lab while remaining unrostered.

## 7. Exercise runtime surfaces

Open the package in Ability Lab and verify semantic bindings, canonical slot projection,
preview identity, and diagnostics. Exercise affected behavior in Editor Training.
Ordinary Lab refresh compiles the current workspace into a live draft; Editor Training
and Solo Play compile source into a transient development catalog. Even a clean
**Cooked** status and matching disk hashes do not prove that saved gameplay ran.

Review animation and VFX from every direction with
`python3 tools/move_review.py run <package>`. Each recipe move runs once in Training;
all view cameras render each frame at the same Shared tick. Inspect the sheets,
clips, mosaics, form montages, `summary.json`, and `media/board/form-<n>.html`.
For selection, media rebuilds, native commands, and restoration, see
[Move review](../contributing/unity-cli.md#move-review).
These captures use Editor development content and do not replace the saved-package
acceptance check below.

### Verify persisted gameplay

Keep a separate acceptance check that executes the saved package. Choose applicable
Shared gameplay tests that load the pinned catalog, or exercise the agreed kit in a
player using the verified package staged under StreamingAssets.

One existing saved-content gameplay check is
[`BonkKitTests.BonkAerialNormals_CookedSwordPathHitsBlocksAndMisses`](../../tests/Shared.Tests/BonkKitTests.cs):

```bash
dotnet test tests/Shared.Tests/Shared.Tests.csproj -p:SkipUnityPluginCopy=true \
  --filter FullyQualifiedName~BonkKitTests.BonkAerialNormals_CookedSwordPathHitsBlocksAndMisses
```

That test loads the pinned Bonk package through
[`BuiltInContentResolver.Resolve`](../../src/Shared/BuiltInContentResolver.cs),
registers its `Definition` and `BakedAnimation` with `ServerSimulation`, and observes
contacts, blocks, and misses. It covers Bonk's aerial normals, not another package
or the whole kit. For another admitted package, use the same API with its selector
and an explicit cooked root, then execute and assert the affected gameplay. Merely
loading the catalog or verifying disk hashes does not satisfy this check. Use
[`KitScenario` and `ScenarioRunner`](../../tests/Shared.Tests/KitScenario.cs) where
appropriate, with definitions and poses resolved from saved content rather than
source compilation. Require persisted coverage of the complete agreed kit for
character acceptance, and record remaining gaps.

Record package identity, version, hashes, content root, checked moves, and outcomes.
Keep human presentation/gamefeel acceptance separate from Shared results. Server
simulation remains authoritative; Unity presentation resolves semantic IDs and never
decides hit results, damage, timing, or admission.

## 8. Add regression coverage

Once gameplay is specified and the package cooks successfully, add focused compiler,
catalog, and package tests. Use `KitScenario` assertions for specified timing, contact,
recovery, and capability behavior. Follow the
[kit regression workflow](../../skills/sloparena-kit-regression-testing/SKILL.md);
whole-state golden comparison has been retired. Do not invent gameplay contracts for
a source-only probe or freeze incidental tuning values in engine tests.

## Prohibitions

Do not add `Build<Name>`, registry factories, legacy adapter branches, raw runtime JSON
loaders, manual animation configs, standalone skeleton ownership, or a second persisted
move mapping. Nilus compatibility and character/slot factory dispatch have been removed.

## Fighter presentation

Each shipped fighter has `card.json` beside `package.json` and `character.json`.
The card contains only `description`, `workInProgress`, and `tutorialHint`.
The nullable Tutorial hint contains `starter` and `followUp` canonical button labels and a `jump` flag.
These values describe a suggested combo. They do not configure abilities or simulation.
Names and form names come from the cooked character definition.

Run `python3 tools/fighter-presentation/generate.py` after changing a card or shipped UI art.
Run the same command with `--check` to verify the generated output.
The generator reads the shipped roster in order and binds its legacy filenames to package IDs.
It writes `Assets/Resources/Generated/FighterUi.json` without moving existing art.
The Shared suite runs `--check`, so a stale snapshot fails `dotnet test`.
The shipped UI layout requires canonical-case paths and `.png` textures in
`Resources/UI/Portraits`, `Resources/UI/AbilityCards`, and `Resources/Icons`.
The generator rejects other image extensions, ambiguous resource names, and incorrect casing.
Text documentation in those folders is ignored.

Before Editor roster admission, run
`python3 tools/fighter-presentation/generate.py --prepare-admission <package-id> --selector <selector>`.
The command creates a presentation-only default card if absent and prepares the candidate binding.
Review its text, then retry admission. Admission refuses missing or mismatched card/binding data
with this command in the diagnostic. After admission, ordinary generation includes the new roster entry.
A missing runtime binding degrades to neutral art with a diagnostic, including in Fighter Select.

The generator is the source-card reader. The client reads the resulting immutable presentation snapshot.
Shared admission and simulation do not read cards.

`card.json` and the generated UI bindings are outside character cook inputs.
Release verification still checks all seven cooks because Unity dependencies can be transitive.
The existing missing-art treatment of built-ins is retained, including Salamander's missing portrait.
Selector-free content uses the neutral portrait and slot glyphs.

`MatchBase` binds each entity's exact catalog entry and resolved appearance as content resolves,
before renderer or HUD setup. Undrawn fighters retain the same binding.
HUD, feedback, end cards, and result snapshots consume that binding.
Result snapshots retain the appearance after the Match scene unloads.
`LocalFighterLibrary` lists admitted shipped fighters in roster order and supplies each menu card from the cooked name and generated UI bindings. Local seats retain an exact `FighterChoice` and its validated starting form. Installed fighter discovery remains ADR-0067 unit 5.

The HUD deck takes its focused entity and appearance together. Other local racers only add
fight cards; their art cannot replace the focused deck. Replay View draws today's stand-in
model when recorded content is unavailable. Its HUD uses the stand-in's art and identifies
it as a stand-in for the recorded display name. Unavailable attacks retain the existing
missing-move indication instead of executing the stand-in's attacks.

Practice logs retain legacy selector keys for bundled fighters in headers, filename slugs,
seat events, and results. Selector-free fighters use package IDs. `SessionLogReport` accepts
both these keys and the display-name headers produced during the unit 2 migration.
