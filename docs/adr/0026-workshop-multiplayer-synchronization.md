# ADR-0026: Workshop Multiplayer Synchronization and Admission

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0008 (Lobby Room Match Flow), ADR-0011 (Rollback Scope), ADR-0022 (Workshop-First Content Architecture), ADR-0024 (Creator Gameplay Primitives), ADR-0025 (Workshop Packages)

## Context

SLOPArena is server-authoritative and runs a shared deterministic simulation. If community fighters or stages participate in online matches, every client and the GameServer must use the same package bytes, dependency graph, built-in capability versions, and compatible runtime schema. A client-side mod or a package name without exact verification is insufficient.

The target platform is Steam Workshop. The project should remain open to any package that satisfies the deterministic and safety boundary, rather than requiring manual gameplay approval for every creator item.

## Decision

Steam Workshop is the distribution path for published Workshop packages. A match advertises its exact content requirements before simulation begins:

- package IDs;
- immutable versions;
- cooked-content hashes;
- pinned dependency IDs, versions, and hashes;
- required built-in capability IDs;
- compatible schema/runtime range.

The GameServer resolves the exact package set from the declared distribution source, validates it, loads it into the dynamic runtime registry, and only then starts simulation. The GameServer does not trust a client-provided behavior implementation. Clients must obtain and validate the same package set before joining.

Package authenticity uses the Workshop identity together with the exact cooked-content hash. A matching identity without a matching hash is not sufficient.

Any verified package using approved deterministic primitives may participate in online matches. Admission is automated rather than manually curated. Validation covers:

- schema and runtime compatibility;
- dependency resolution;
- built-in capability availability;
- deterministic primitive usage;
- approved asset types;
- package size and resource limits;
- simulation work and content budgets;
- creator license and attribution metadata.

Validation fails closed. If a required package, dependency, built-in capability, or compatibility contract cannot be resolved and verified, the lobby/match does not start.

Creators must declare identity, license, and attribution metadata and grant the rights needed for Steam distribution and gameplay. Platform moderation and takedown processes handle abuse and rights violations. A removed package is blocked from new matches; a running match may finish against its already-pinned package.

## Considered Options

- **Official content only online** — rejected as the long-term model: it would make community fighters and stages second-class content.
- **Host-provided packages** — rejected as the trust boundary: the authoritative server must resolve and validate exact content itself.
- **Identifier-only agreement** — rejected: package IDs and versions without hashes do not prove identical simulation inputs.
- **Manual approval for every package** — rejected: it does not scale to an open creator ecosystem; automated validation and platform moderation are the default.
- **Fallback when content is missing** — rejected: silently substituting built-ins can change gameplay and break deterministic agreement.

## Consequences

- Lobby and match-control protocols must carry exact package requirements before the fight starts.
- GameServers need Workshop package acquisition, caching, validation, and failure reporting.
- Clients cannot join an online match with locally modified bytes under the same package identity.
- Public online content is constrained by the deterministic primitive and asset allowlists.
- Server operators may still need operational controls for capacity, runtime support windows, or emergency package blocks, without changing the package identity model.
- Multiplayer content negotiation becomes a required follow-up to the current lobby and roster flow.

## Non-Goals

This ADR does not define the Steam Workshop endpoint implementation, matchmaking UI, moderation service, server cache layout, or the exact wire encoding of package requirements.
