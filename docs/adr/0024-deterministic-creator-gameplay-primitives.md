# ADR-0024: Deterministic Creator Gameplay Primitives

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0022 (Workshop-First Content Architecture), ADR-0023 (Built-In Content API), ADR-0025 (Workshop Packages), ADR-0026 (Workshop Multiplayer)

## Context

SLOPArena's authoritative simulation runs in shared C# code on both the GameServer and the client. Rollback and online verification require community gameplay to produce the same result on every participating runtime. Arbitrary creator code would introduce security, determinism, versioning, and server-deployment problems.

Creators still need meaningful control over fighters and stages: movement, hitboxes, timing, projectiles, bursts, warps, VFX/SFX events, and other kit behavior. The boundary must provide expressive composition without making the authoritative simulation a general plugin host.

## Decision

Community gameplay is authored as deterministic data composed from engine-owned, versioned, approved gameplay primitives.

Examples of primitive categories include:

- movement and lunge operations;
- hitbox and hurtbox definitions;
- timed ability stages;
- projectile and explosion behaviors;
- warp and targeting operations;
- damage, knockback, hitstop, and recovery configuration;
- VFX/SFX event references;
- stage geometry and spawn configuration.

Primitive behavior ships with SLOPArena and is identified by versioned contracts. Creators compose the available primitives; they do not add executable primitives to a Workshop package. Incompatible primitive behavior receives a new version rather than silently changing the meaning of existing content.

The authoritative GameServer and the shared client simulation consume the same cooked deterministic representation. The server validates the representation before loading it. Validation covers schema compatibility, deterministic behavior constraints, approved asset types, resource limits, and simulation work budgets.

Workshop packages must not contain:

- executable creator code;
- arbitrary native plugins;
- arbitrary shaders or runtime-dangerous Unity assets;
- behavior that bypasses the shared simulation.

The ModKit may provide Unity inspectors, preview tools, and higher-level authoring helpers, but those tools export the canonical deterministic representation rather than becoming a second gameplay runtime.

## Considered Options

- **Arbitrary scripts or plugins** — rejected: incompatible with deterministic rollback, server authority, package verification, and safe distribution.
- **Creator-defined primitives** — rejected for the first architecture: they would make every server a plugin host and require a separate security and compatibility model.
- **Generated code from templates** — rejected as the public boundary: generated code still requires compilation, trust, and cross-runtime equivalence guarantees.
- **Visual-only content** — rejected: community fighters and stages need gameplay expression, not only custom presentation.

## Consequences

- New creator capabilities require engine work and a versioned primitive contract.
- The primitive catalog becomes an important part of SLOPArena's public creator API.
- Primitive parameters need explicit bounds and deterministic semantics.
- The shared simulation remains the source of truth; client-only gameplay behavior is not a supported extension path.
- Automated package validation must reject malformed, over-budget, or nondeterministic content before a match begins.
- Official content should exercise the same primitives where practical, with exceptions governed by ADR-0022.

## Non-Goals

This ADR does not define the final primitive catalog, the authoring UI, the exact serialized schema, or the implementation of validation. It also does not prohibit future sandboxed scripting; such a change would require a new ADR that replaces this boundary.
