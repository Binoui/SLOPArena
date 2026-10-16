# ADR-0029: Character Authoring and Cooking Pipeline

**Status:** Accepted — 2026-08-26  
**Amended by ADR-0038** (2026-10-16): Editor play and Ability Lab preview reuse the committed pose data when bound clips, presentation prefabs or weapon prefabs are not installed; see [ADR-0038](0038-editor-play-without-installed-clips.md).  
**Deciders:** @Binoui  
**Related:** ADR-0022 (Workshop-First Content Architecture), ADR-0023 (Built-In Content API), ADR-0024 (Creator Gameplay Primitives), ADR-0025 (Workshop Packages), ADR-0026 (Workshop Multiplayer), ADR-0027 (ModKit and Preview), ADR-0028 (Built-In Compatibility)

## Context

FightGuy temporarily had three manually coordinated representations: `FightGuyData.cs`, `content/characters/fightguy/character.json`, and `FightGuy_AnimConfig.asset`. Ability Lab first made the JSON authoritative for authoring; a later startup override made it authoritative at runtime without removing the C# definition or validating the Unity binding. FightGuy R therefore requested `spell_e` from JSON while C# and the animation config said `spell_r`. Ability Lab and the game consistently rendered the wrong clip, and recompiling or rebaking could not repair the source mismatch.

ADR-0025 and ADR-0027 already require a stronger distinction: editable source is not the installed runtime contract. Character creation needs one owner for each fact, deterministic cooking, exact package identity, and the same cooked behavior in Ability Lab, the client, and the GameServer.

## Decision

### Package and source ownership

A **Character Package** contains exactly one playable Character. Package identity is Character identity. A published revision is identified by package ID, immutable semantic version, and exact cooked-content hash; local development uses a reserved development version plus the exact hash.

The editable package has three authoritative source modules, each with one concern:

- `package.json` owns package identity, dependencies, creator, license, and attribution.
- `character.json`, the **Character Authoring Document**, owns gameplay semantics: movement, slots, fixed timelines, typed operations, timings, hitboxes, hurtbox data, animation IDs, and parameters.
- `CharacterAssetCatalog.asset` owns package-local semantic ID to imported Unity asset bindings.

The Character Authoring Document does not repeat package `id` or legacy `CharacterClass`. Package-local IDs are scoped by package, for example `anim.cyclone-kick`; shared built-in capabilities use versioned `slop.*` IDs. Before publication, an ID rename is an explicit atomic refactor across the document and catalog, never free-text coordination.

FightGuy is the first vertical slice. Its authoring package lives under `Assets/CharacterPackages/fightguy/`. Other built-in Characters remain legacy inputs until migrated one at a time.

### Ability Lab authoring workflow

Ability Lab is the primary gameplay editor and can create and edit Character Packages. New packages start from a minimal universal template, not a FightGuy clone. Unity remains responsible for FBX/GLB import and rig settings; creators assign already-imported assets through the package asset catalog.

For the FightGuy slice, Ability Lab edits the complete gameplay document except hurtboxes: existing hurtbox data migrates and remains visible, but hurtbox editing is deferred. Raw JSON remains a supported expert escape hatch.

Ability Lab records the source hash it loaded. If the file changes externally, save is blocked until the creator reloads; last-writer-wins and automatic source merging are rejected. Saving always persists the draft, then automatically validates and cooks if possible. An invalid draft remains editable while the last valid cooked artifact remains unchanged.

Ability Lab's authoritative persisted preview still requires SAVE + COOK. A valid Editor Training or Solo Play launch separately compiles the current authoring source and semantic assets in memory into the same immutable runtime definition, cooked package, baked animation payload, and client catalog types. Invalid source blocks match start and never falls back to `content-cooked`; Ability Lab may retain its last-valid preview only with a visible Stale Cook diagnostic.

Older authoring schemas require an explicit, previewable migration command; neither Ability Lab nor runtime silently rewrites them.

### Authoring and runtime schemas

The authoring schema and cooked runtime schema are separate. Editor concepts and source aliases do not become permanent runtime interface. The pure Shared compiler validates gameplay source and emits a normalized **Cooked Character Definition**. A Unity cook stage resolves source assets and bakes deterministic payloads. A package assembler writes the immutable manifest and hashes the payloads.

Ground/air aliases appear once in authoring source and expand to explicit runtime slot data during cooking. Runtime consumers do not implement authoring alias semantics.

A package-local **Animation Definition** pairs one visual `AnimationClip` with one deterministic pose track. The Unity cook stage bakes the pose track from the exact clip, rig, and import settings bound in the asset catalog. Assigning a clip and manually coordinating an unrelated pre-baked file is not supported.

The cooked Character Package contains:

- an immutable manifest;
- the normalized Cooked Character Definition;
- deterministic pose data;
- private client binding payloads; and
- diagnostics and compatibility metadata required by the accepted package ADRs.

The canonical generated package is committed under a dedicated tree such as `content-cooked/<package-id>/`. StreamingAssets and release directories are staging outputs only. Unity generates a runtime catalog `ScriptableObject` from the canonical client payload as a non-authoritative, regenerable project cache. Editor Training and Solo Play may instead hold a `DontSave` semantic catalog created from the current source in memory.

The source hash covers every transitive authoring input that can affect cooked bytes: source manifests, Character document, asset catalog, referenced clips and rigs, import settings, and cooker/toolchain version. Cooking is deterministic and replaces the prior artifact atomically. Errors block replacement; warnings permit cooking and are recorded in the manifest. Any dependency edit automatically triggers a debounced recook.

### Ability execution model

Gameplay timing is authored in 60 Hz simulation ticks. Creator abilities use permanent fixed timelines containing ordered, typed, versioned operations. Operations scheduled for the same tick execute in authored list order. Loose string-to-float parameter dictionaries are not part of migrated FightGuy content; each operation or capability defines required fields, units, bounds, defaults, and budget cost.

Documents do not author branches, expressions, or transition predicates. Variable-duration behavior such as hold/release lives inside approved stateful engine primitives with bounded parameters and deterministic lifecycle. New variable mechanics require a new or extended engine primitive rather than arbitrary creator control flow.

The generic ability runtime owns interruption. Active stateful operations declare deterministic start, cancel, and complete behavior so hitstun, death, Burst, and natural completion cannot leak state or depend on an authored cleanup timeline.

Presentation timing is represented by semantic events on the fixed timeline. The authoritative simulation emits stable tick events keyed by match tick, entity, and operation index; clients resolve those IDs to presentation assets and deduplicate them across prediction and rollback. Presentation never feeds back into gameplay.

FightGuy may temporarily reference versioned `slop.internal.*` capabilities for native behavior that has not yet been decomposed into public creator primitives. Only the trusted built-in cook profile may resolve those IDs; a content document cannot grant itself that privilege. Every exception records an owner and migration path under ADR-0022. Hidden `(CharacterClass, slot)` dispatch is removed.

### Runtime identity and catalogs

Runtime definitions are immutable. A process may cache verified packages, but every match owns an immutable **Match Content Catalog** pinned to exact package IDs, versions, hashes, dependencies, and capability versions. Running matches never observe recooks; a successful local recook applies to the next match.

The GameServer resolves packages before match start, assigns compact match-local content handles, and sends the exact handle-to-package map to clients for verification. Per-tick state does not repeat package strings or hashes.

`CharacterClass.FightGuy` remains temporarily as a protocol/UI selector only. The cooked Built-In Roster Manifest maps that selector to a stable package ID and pins the exact bundled version and cooked/package hashes; the previously described separate source-form roster contract is historical and has been retired. During the FightGuy-only slice, a catalog builder loads cooked FightGuy and snapshots legacy Manki, Kistu, and Nilus definitions into immutable entries. Only that builder touches the legacy registry; simulation and rendering do not branch between content systems.

Missing, invalid, stale, incompatible, or hash-mismatched content never falls back to C# or a similar capability. Invalid Editor Training/Solo Play source fails closed without using a persisted cooked package. Release builds, CI admission, PvP, Player, Workshop, and online matches use persisted deterministic packages and fail closed. Ability Lab may show the last valid cook only with a persistent **Stale Cook** banner containing the source hash, cooked source hash, and cook error; a console message alone is insufficient.

## Migration

1. Repair the current FightGuy R source mapping and add a temporary current-schema regression test.
2. Build the pure compiler, Unity asset stage, deterministic package assembler, source diagnostics, and committed cooked artifact.
3. Make Ability Lab create/edit the Character Authoring Document and preview valid drafts through an in-memory cook.
4. Add the Built-In Roster Manifest, immutable Match Content Catalog, GameServer-assigned handles, and isolated legacy catalog builder.
5. Atomically migrate every FightGuy consumer—Ability Lab runtime preview, Training, PvP client, GameServer, and tests—to the cooked package path.
6. Remove `BuildFightGuy`, raw authoring-JSON runtime loading, `RegisterOverride` use for FightGuy, manual C# fallback, duplicated alias bodies, and the obsolete `spell_r` regression assertion.

A differential simulation harness runs identical inputs through the hotfixed current JSON definition and the Cooked Character Definition for every FightGuy slot. Observable state, events, hitboxes, and lifecycle must match except for explicitly approved changes.

The vertical slice is complete when the full source-to-cook-to-package-to-client/server/Lab path is active, deterministic artifacts are committed and verified, clip/pose pairs validate, every FightGuy consumer uses immutable match content, and no authored FightGuy fallback remains. Explicit temporary internal capabilities may remain only with recorded migration paths.

## Rejected alternatives

- **Raw `character.json` as both editable source and runtime contract** — conflates drafts with validated immutable content and conflicts with ADR-0025/0027.
- **Manual C# fallback or generated C# snapshot** — preserves a second definition and reintroduces silent authority drift.
- **One shared authoring/runtime schema** — leaks editor conveniences and aliases into the installed compatibility interface.
- **Manual animation rebake** — allows clip and deterministic pose data to diverge.
- **Mutable global `CharacterRegistry` overrides** — unsafe for tests, local recooks, and concurrent matches with different package revisions.
- **Permanent official/Workshop behavior split** — makes official content an invalid reference corpus for creators.
- **General expression language or visual gameplay graph** — creates an unnecessary language, VM, debugger, and security surface before fixed timelines and approved stateful primitives prove insufficient.
- **Silent last-valid execution** — recreates the debugging failure that motivated this decision; permitted local stale execution must remain visibly stale.

## Consequences

Character creation gains a deep cooking module: callers provide package source and receive either a complete immutable package or structured diagnostics. Asset resolution, pose baking, alias expansion, schema normalization, validation, hashing, and generated runtime bindings stay behind that seam.

The first vertical slice is intentionally broader than the immediate animation bug because it removes the authority pattern that caused the bug. It also requires migration work in Ability Lab, client/server composition, networking, tests, and build staging. In return, future Characters follow one repeatable path compatible with the Workshop architecture rather than adding another manually synchronized convention.
