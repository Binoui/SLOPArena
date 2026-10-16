# ADR-0028: Built-In Content Compatibility Policy

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0023 (Built-In Content API), ADR-0025 (Workshop Packages), ADR-0026 (Workshop Multiplayer)

## Context

ADR-0023 establishes semantic built-in capability IDs, but a public ID is only useful if creators can know what remains compatible as SLOPArena changes. Silent semantic drift would change published characters, stages, replays, and online matches without changing their package identity.

The installed runtime also cannot preserve every historical capability forever. Compatibility must be explicit, bounded, and validated before a match starts.

## Decision

The `slop.*` namespace is an additive, SLOPArena-owned public registry. A built-in ID names a documented semantic capability contract.

- IDs use an explicit version component, for example `slop.anim.tumble.default.v1`.
- Compatible implementation changes may retain the ID when the documented contract remains true.
- Incompatible timing, inputs, outputs, determinism, or gameplay semantics require a new versioned ID.
- Existing IDs are never silently repurposed.
- Creators may reference published IDs but may not publish replacements into the `slop.*` namespace.
- A package declares the built-in IDs it requires. Runtime validation resolves each ID before loading the package.
- A runtime advertises a supported built-in API/schema window. Content outside that window is rejected before simulation rather than silently migrated or substituted.
- Deprecated IDs remain available only for the declared support window. After that window, creators must publish a new package referencing a supported ID.

The compatibility promise is semantic, not visual. SLOPArena may replace the private animation, VFX, SFX, or material implementation behind a compatible ID, but it may not change the contract in a way that changes the published content's intended behavior while retaining that ID.

## Considered Options

- **Unversioned stable names** — rejected: incompatible changes would be indistinguishable from compatible implementation replacements.
- **Metadata-selected versions behind one base ID** — rejected for the public contract: package validation and authoring tools become harder to reason about deterministically.
- **Permanent backward compatibility** — rejected: the installed runtime would accumulate unbounded legacy behavior.
- **Silent fallback to a similar capability** — rejected: fallback changes authored content and can change gameplay or determinism.

## Consequences

- The registry needs contract documentation and compatibility tests for every public ID.
- Built-in IDs must be included in package validation and multiplayer content requirements.
- Runtime releases need an explicit supported API/schema window and deprecation process.
- A content package may become unavailable on a newer runtime until it is recooked or migrated by its creator.
- Internal asset replacement is possible without breaking content when the semantic contract remains compatible.

## Non-Goals

This ADR does not define the complete ID naming taxonomy, the duration of a support window, the exact registry data format, or the tooling used to publish and deprecate IDs.
