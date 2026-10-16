# ADR-0023: Built-In Content API and Dynamic Registry

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0022 (Workshop-First Content Architecture), ADR-0025 (Workshop Packages), ADR-0026 (Workshop Multiplayer), ADR-0028 (Built-In Compatibility)

## Context

Workshop content must be able to use SLOPArena-provided animations, VFX, SFX, materials, and other capabilities without receiving the Unity assets that implement them. Direct references to `Assets/...` paths, prefab names, vendors, or package layouts would make published content fragile and could expose licensed source assets.

Official content also currently has compile-time identity and factory-oriented registration. A Workshop-first architecture requires official and community definitions to resolve through one runtime content model without recompiling the game for every creator package.

## Decision

SLOPArena exposes built-in resources through a SLOPArena-owned, additive Built-In Content API. Public references use semantic IDs such as:

```text
slop.anim.fall.default.v1
slop.anim.tumble.default.v1
slop.vfx.hit.heavy.v1
slop.sfx.jump.default.v1
```

A built-in ID identifies a documented capability contract, not a Unity asset. Workshop definitions must not reference Unity paths, filenames, vendor package names, or implementation asset IDs.

- A compatible implementation replacement may retain its existing public ID.
- An incompatible semantic change receives a new versioned public ID.
- IDs are additive and are not silently repurposed.
- Deprecated IDs remain resolvable for their supported compatibility window or cause content validation to fail; they do not silently fall back to another capability.
- The registry is owned and governed by SLOPArena. Creators may consume published IDs but do not redefine the `slop.*` namespace.
- The registry resolves both official and Workshop definitions through a dynamic runtime content registry. Match-local entity/content IDs refer to loaded definitions without requiring a game rebuild.
- Missing, deprecated, or incompatible required IDs fail content validation before simulation starts.

Built-in implementation assets may be licensed, private, or replaced over time. The public ID remains the compatibility boundary. Whether a specific capability may be exposed is still subject to the asset's license.

Conceptually:

```text
Workshop definition
        │
        ▼
Built-In Content Registry
        │
        ▼
Private runtime asset or engine capability
```

## Considered Options

- **Direct Unity asset paths** — rejected: paths are implementation details, fragile across reorganizations, and may expose vendor/licensed assets.
- **Copy built-in assets into every Workshop package** — rejected: packages become large, duplicate common content, and create licensing problems.
- **Immutable assets behind permanent IDs** — rejected: it prevents safe implementation replacement and would grow the registry unnecessarily.
- **Per-package capability namespaces** — rejected for the built-in namespace: shared capabilities need one authoritative contract and lookup mechanism.

## Consequences

- Built-in IDs are public API and require documentation, compatibility tests, and deprecation handling.
- The registry must distinguish capability contracts from runtime Unity assets.
- The current compile-time character registration path must migrate toward runtime content identity for Workshop support.
- A missing built-in capability is a content-resolution error, not an invitation to use a local fallback.
- Detailed ID naming, compatibility, and deprecation policy is defined by ADR-0028.

## Non-Goals

This ADR does not define the complete Workshop package file format, Steam distribution, creator gameplay primitive catalog, or Unity asset-cooking implementation.
