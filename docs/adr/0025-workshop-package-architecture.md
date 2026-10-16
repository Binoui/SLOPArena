# ADR-0025: Workshop Package Identity and Compatibility

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0022 (Workshop-First Content Architecture), ADR-0023 (Built-In Content API), ADR-0024 (Creator Gameplay Primitives), ADR-0026 (Workshop Multiplayer), ADR-0027 (ModKit and Preview)

## Context

Workshop content must survive Unity project reorganizations, implementation asset replacement, runtime updates, and multiplayer verification. A package cannot be identified only by a Steam item, a filename, or a mutable latest version. Clients and GameServers need to agree on exact content bytes and compatible runtime behavior before simulation starts.

Creators also need a source-oriented authoring workflow while the installed game needs a safe, validated, runtime-loadable representation. Raw FBX, Unity paths, and other authoring details must not become the public runtime contract.

## Decision

A published Workshop package has:

- an immutable package/content ID;
- an immutable semantic version;
- a content hash covering the cooked runtime package;
- a declared runtime schema version;
- a declared supported SLOPArena runtime/API compatibility range;
- explicit dependencies, each pinned by package ID, version, and hash;
- creator identity, license, and attribution metadata.

Published versions are immutable. An update creates a new version and hash rather than mutating an existing package. Existing matches resolve their pinned versions; new matches use the exact package requirements advertised by the lobby or server.

The distributable runtime artifact is a cooked package behind a stable manifest. The public contract is the manifest, schema, content IDs, and compatibility rules—not Unity AssetBundles, Addressables, raw FBX, or another engine-private asset representation. The engine may change its internal cooked representation when the declared compatibility contract permits it.

An optional source attachment may accompany a published package for editing, inspection, or collaboration. It is non-authoritative: it does not change package identity, does not replace the cooked artifact for runtime loading, and is not required to reproduce the exact package unless a later policy says otherwise.

Creators may preview unpublished local packages using the same cooking and validation path. Local availability does not make a package eligible for online play; online use requires Workshop identity, exact hash agreement, and server admission.

Each runtime advertises a supported schema/API window. Before loading content, the runtime gates on that window and rejects packages outside it. Creators recook or migrate content for a compatible runtime; automatic silent migration is not part of the runtime contract.

## Considered Options

- **Mutable latest Workshop item** — rejected: it breaks deterministic match replay and makes dependencies drift.
- **Package ID only** — rejected: equal IDs would not prove equal bytes or behavior.
- **Raw source assets at runtime** — rejected: source formats are authoring inputs, not a stable or safe runtime boundary.
- **No dependencies** — rejected: it would force duplication and prevent reusable creator content.
- **Permanent support for every schema** — rejected: it would turn the installed game into an unbounded compatibility layer.

## Consequences

- The package manifest and hash are part of the multiplayer handshake (ADR-0026).
- A dependency resolver must reject missing, floating, or mismatched dependencies.
- Package cooking and validation become first-class ModKit infrastructure (ADR-0027).
- Runtime upgrades may temporarily make packages unavailable until they are recooked.
- Package authors need explicit license and attribution metadata, and distribution must respect those declarations.
- Package storage can preserve source and runtime artifacts separately.

## Non-Goals

This ADR does not define Steam Workshop API calls, the exact hash algorithm, the final manifest field names, the package compression/container format, or a universal migration tool.
