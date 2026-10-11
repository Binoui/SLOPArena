---
name: sloparena-character-workflow
description: "Package-native workflow for designing, authoring, cooking, validating, and previewing SlopArena characters. Covers Character Packages, asset catalogs, deterministic cooking, generated bindings, admission, hashes, and Ability Lab."
metadata: {"category": "game-dev"}
---

For live Editor commands, use your own caller identity and bounded gateway lease from the [Unity CLI guide](../../docs/contributing/unity-cli.md) and [coordination protocol](../../docs/contributing/unity-editor-coordination.md). Run project commands from the selected checkout. Busy ownership waits in the gateway; blocked or unknown status requires recovery.

# SlopArena Character Workflow

Use this skill when designing or implementing a playable character. The canonical authoring guide is [`docs/characters/adding-a-new-character.md`](../../docs/characters/adding-a-new-character.md). New characters are packages, not registry factories.

For agent-driven package move inspection, preview, scrubbing, capture, or diagnostics,
use the [Ability Lab skill](../sloparena-ability-lab/SKILL.md) and
[Unity CLI reference](../../docs/contributing/unity-cli.md).

## Authority and boundaries

A Character Package contains exactly one playable character:

```text
client/Unity/Assets/CharacterPackages/<package>/
├── package.json                 # identity, version, dependencies, creator, license
├── character.json               # gameplay source and canonical slots
└── CharacterAssetCatalog.asset  # package-local Unity asset bindings
```

`package.json` owns package metadata. `character.json` owns gameplay semantics: movement, hurtboxes, animation IDs, fixed timelines, typed operations, timings, and parameters. `CharacterAssetCatalog.asset` owns imported rig and clip bindings. Do not duplicate one fact across these modules.

The authoritative pipeline is:

```text
source package
    │
    ├── Shared CharacterPackageCompiler
    ├── Unity asset catalog and pose cook
    └── package assembler and hash calculation
          │
          ▼
content-cooked/<package>/
  manifest.json
  character.runtime.json
  poses.bin
  client.bindings
```

Raw JSON is cook input. Runtime consumers load the immutable cooked package through the Match Content Catalog. The generated client catalog is a regenerable cache, not a second source of gameplay truth.

## Canonical move grid

Every package resolves the sixteen canonical slots in this order:

```text
ground.1  ground.2  ground.3  ground.4  ground.A  ground.E  ground.R  ground.F
air.1     air.2     air.3     air.4     air.A     air.E     air.R     air.F
```

Use package slot IDs as persisted identity. Ability Lab labels are projections of that identity. `LMB`, `RMB`, `Q`, and other physical controls are input adapters, not alternate package slots. Ground/air aliases may appear once in authoring and are expanded by the compiler; runtime data is explicit.
## Verification choice

### Local iteration

Tune source through the transient Editor development catalog and exercise the
affected Ability Lab or Training path. Local preview is not persisted package
verification and does not require a publishing cook for every numerical edit.

### Accepted package

Content intended to ship crosses the explicit `inspect` → `cook` → `verify` →
`roster.refresh` boundary. Require fresh cooked artifacts, matching hashes, and
exact admitted roster pins before treating it as an accepted package.

## Workflow

### 1. Design the kit

Define the fighter's role, counterplay, movement profile, eight normals, four specials, recovery choice, and presentation needs. Author timing in 60 Hz ticks. Prefer engine-owned deterministic primitives and fixed timelines over bespoke simulation code.

### 2. Create source

Start from the minimal universal package template, not a copied existing fighter. Use stable lowercase semantic IDs (`anim.*`, package-local bones, and package-owned presentation IDs). Declare every built-in capability requirement and every referenced animation or bone.

Community/Workshop profiles may use approved versioned primitives only. Trusted built-in profiles may use explicitly admitted temporary `slop.internal.*` capabilities; the package cannot grant itself that privilege.

### 3. Bind Unity assets

Import the rig and clips through Unity. Bind them in `CharacterAssetCatalog.asset` with one semantic animation ID and one deterministic pose-track ID per required animation. The Unity cook stage bakes pose data from the exact clip, rig, import settings, and catalog binding. Do not hand-coordinate a standalone pose file with a clip.

Use Humanoid for ordinary humanoid fighters when built-in retargeting helps. Custom rigs remain valid when they provide the required package-owned animation data. Keep art and naming rules in [`docs/contributing/conventions.md`](../../docs/contributing/conventions.md).

For generated-model cleanup or replacement, use the [character model polish guide](../../docs/characters/character-model-polish.md) and `blender-character-polish`. Keep surface polish separate from approved rig changes.

After structural repairs, check the candidate early in Unity with target-material backface culling, relevant raised-arm/underside/rear views and actual bound clips. Use the installed Unity CLI/Pipeline on the main checkout. Validate attachments, animation-aware bounds, skin influences and gameplay-distance appearance; maintain `TESTING-UNITY.md`. Controlled Blender poses are not runtime acceptance.

When the rig, rest transforms or import/binding settings change, compare old/new retargeted output at matching clip times, including relevant attachment and authoritative pose tracks. A valid Avatar, unchanged bone names or byte-identical gameplay JSON does not prove pose equivalence. If presentation-only scope is required, investigate differences before promotion; otherwise obtain explicit approval for changed authoritative content and verify affected behavior. Report changed cooked poses without calling them cosmetic or hand-editing them. Local preview does not require a publishing cook on every iteration; accepted integration still follows the persistence boundary below.

### 4. Validate source and choose persistence boundary

For local iteration, resolve the source through the transient development catalog
and repair structured diagnostics without replacing persisted cooked output. For an
accepted package, use the typed Unity CLI commands:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.inspect --target <package>
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.character.cook --target <package>
```

`inspect` reports the canonical 16-slot projection, source/cooked hashes, status,
stale reasons, and diagnostics. Accepted `cook` validates the Shared source, resolves
Unity bindings, bakes poses, writes generated bindings, assembles the immutable
package, and reports source, cooked-content, and package hashes.

An accepted semantic failure has a structured result with `success: false`; a failed
cook preserves the last valid cooked artifact and status. Repair diagnostics and retry.
Do not edit generated runtime output to hide a source or catalog error.

### 5. Admit and verify

The package assembler writes an immutable manifest under `content-cooked/<package>/`. The manifest pins package ID, version, cooked-content hash, package hash, schema/API compatibility, dependencies, and capability requirements. Built-in roster admission pins the exact package requirement in `content-cooked/roster/manifest.json`.

Validation must fail closed for missing or mismatched source, catalog, pose payload, capability, dependency, schema, or hash. A Match Content Catalog pins the verified package set per match; a later recook cannot change a running match.

## 6. Preview through the real path

Local iteration previews valid drafts through the transient development catalog and
the same interpreter used by Training. Accepted packages preview the persisted cooked
definition after inspect/cook/verify and roster refresh. Invalid drafts may show a
clearly non-authoritative editing pose only; they must never silently become match content.

For online content, verify server admission and exact client/server hash agreement.
Presentation resolves semantic IDs to generated package bindings and plays through
Animancer; presentation never feeds back into simulation.

## Cooked-only content

Nilus compatibility, registry factories, and character/slot dispatch have been removed.
Character execution and Ability Lab preview use packages only. Do not:

- add `Build<Name>`, `BuildRegistry`, `CharacterRegistry`, or `(CharacterClass, slot)` dispatch;
- copy `MankiData` or another legacy character definition;
- load raw authoring JSON at runtime;
- add registry overrides or manual FightGuy animation-config ownership;
- make a standalone skeleton `.bin` the runtime owner of a new package;
- introduce a second persisted slot mapping or a client-only gameplay path.

## Verification checklist

Reuse evidence across this workflow, animation authoring and the build gate. One
actual-prefab capture on the affected runtime surface can satisfy multiple visual
criteria; no duplicate capture is required because another skill names the same check.
Name the unresolved question before adding evidence and stop once it is answered.
Keep detailed traces for failures and representative cases, not every passing
permutation, unless explicitly requested. Repeat only checks invalidated by a change.

For local iteration:

- source diagnostics are visible and invalid drafts never fall back to stale content;
- the affected Ability Lab or Training path consumes the transient development catalog;
- Shared/server authority remains unchanged and presentation is client-only.

For an accepted package:

- source package has one owner for every authored fact;
- compiler resolves all sixteen slots and rejects invalid IDs, units, references, and budgets;
- catalog bindings match required semantic IDs and exact imported assets;
- inspect, cook, and verify succeed; the cook replaces the prior artifact atomically;
- manifest, runtime definition, pose payload, generated bindings, and hashes agree;
- roster pins refresh only after accepted verification;
- the affected Ability Lab or Training surface consumes the persisted cooked definition; exercise both only when each has distinct changed behavior;
- Shared/server authority remains unchanged and presentation is client-only.
