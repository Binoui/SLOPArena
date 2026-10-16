# ADR-0022: Workshop-First Content Architecture

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0023 (Built-In Content API), ADR-0024 (Creator Gameplay Primitives), ADR-0025 (Workshop Packages), ADR-0026 (Workshop Multiplayer), ADR-0027 (ModKit and Preview), ADR-0028 (Built-In Compatibility)

## Context

SLOPArena is intended to be more than a fixed official roster. Its long-term product identity is a free-to-play, open-source 3D platform fighter where community-created fighters, stages, and presentation content are first-class parts of the game through Steam Workshop.

The repository already separates the shared simulation from Unity presentation and uses reusable character, ability, arena, animation, and VFX concepts. It also contains third-party and licensed assets that cannot become raw public dependencies. Treating community creation as a later modding layer would therefore create the wrong boundaries now: official content could depend on private implementation details, and creator content could be forced to redistribute assets it does not own.

## Decision

### Product and architecture

- Community content is a first-class product concern, even though Workshop support ships after the current playable demo.
- New systems should avoid blocking eventual creator access. They do not need to expose every internal implementation immediately.
- Official fighters, stages, and presentation content are the reference corpus for creator capabilities. Official content should use creator-facing abstractions by default.
- Engine-only official behavior is permitted only as an explicit, documented exception with a reason, scope, owner, and migration path.
- The first-class content scope is fighters, stages, and presentation assets such as models, textures, animations, VFX, SFX, metadata, and portraits.

### Content boundary

Workshop packages contain creator-owned definitions and assets, plus references to SLOPArena built-in capabilities. They do not contain copies of built-in SLOPArena implementation assets.

Workshop content references semantic built-in capability IDs rather than Unity paths, filenames, package names, or asset vendors. The installed game owns the mapping from those IDs to runtime assets.

Community gameplay is composed from deterministic, engine-owned, versioned primitives. Workshop content does not execute arbitrary code or arbitrary shaders in the authoritative simulation.

### Open-source and access scope

The open-source commitment covers SLOPArena code and project assets that SLOPArena can legally redistribute. Third-party licensed assets remain behind the installed-game boundary or are distributed under their own valid terms.

The game, the creator ModKit, and gameplay-affecting Workshop content remain free to access. This ADR does not prohibit future monetization of non-gameplay content, but paid gameplay content is outside the product commitment.

## Considered Options

- **Fixed-roster game with optional mods** — rejected: it would allow official systems to bypass creator-facing boundaries and make Workshop a permanent afterthought.
- **Expose raw Unity assets** — rejected: Unity paths and vendor assets are unstable and may not be redistributable.
- **Arbitrary creator code** — rejected: it conflicts with deterministic shared simulation, server authority, multiplayer verification, and platform safety.
- **Make Workshop launch-critical** — rejected for current scope: the architecture is designed now, but the playable demo does not depend on a finished creator ecosystem.

## Consequences

- Official content becomes both game content and a test corpus for creator capabilities.
- Built-in capabilities require public identity, compatibility, and registry governance (ADR-0023).
- Creator behavior must fit deterministic primitive and validation boundaries (ADR-0024).
- Workshop packages require immutable identity, cooking, compatibility, and dependency rules (ADR-0025).
- Online play requires exact content synchronization and server-side admission (ADR-0026).
- The ModKit and installed-game preview path become product infrastructure (ADR-0027).
- Licensed assets cannot be assumed to be public merely because the runtime uses them.

## Non-Goals

This ADR does not define the final package schema, Steam integration details, runtime scripting language, security implementation, matchmaking UX, or the complete compatibility/deprecation policy. Those concerns are constrained by this direction and specified by the related ADRs.

## Core Principle

> **Workshop content references SLOPArena capabilities; it does not depend on SLOPArena's underlying asset files.**
