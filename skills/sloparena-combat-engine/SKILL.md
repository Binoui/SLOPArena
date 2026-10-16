---
name: sloparena-combat-engine
description: "SLOPArena server-authoritative combat: deterministic cooked timelines, typed operations, approved capabilities, hitbox/projectile resolution, interruption ownership, and legacy ServerAbility compatibility."
license: "MIT"
metadata: {"omp": {"tags": ["sloparena", "combat", "hitbox", "netcode", "simulation", "projectiles", "explosions", "abilities"], "related_skills": ["sloparena-netcode", "sloparena-character-workflow"]}, "version": "4.0.0", "author": "OMP Agent"}
---

# SLOPArena Combat Engine

The Shared `ServerSimulation` is authoritative. The GameServer and client prediction consume the same immutable cooked character content. Unity renders state and semantic presentation events; it does not run a second gameplay FSM or resolve hits locally.

## Current content model

A cooked Character Package contains normalized slot definitions, fixed timelines, typed operations, versioned capability references, and deterministic budgets. The canonical grid is:

```text
ground.1 ground.2 ground.3 ground.4 ground.A ground.E ground.R ground.F
air.1    air.2    air.3    air.4    air.A    air.E    air.R    air.F
```

`LMB`, `RMB`, `Q`, and other physical labels are input adapters. They are not universal persisted move identities. Ground/air authoring aliases expand during cooking; runtime consumers receive explicit slots.

The compiler validates units, bounds, IDs, references, operation budgets, and capability declarations. Operations scheduled at the same tick execute in authored list order. A timeline contains no arbitrary branches, expressions, or transition predicates. Variable-duration behavior belongs in an approved stateful engine capability with bounded deterministic lifecycle.

### Engine-owned primitives

The engine owns deterministic behavior for:

- movement, lunge, Dash, targeting, and aim state;
- hitboxes, hurtboxes, bone geometry, damage, Knockback, Hitstun, Hitstop, Clash, and Burst;
- projectiles, gravity, ground contact, explosions, and rehit zones;
- timed stages, cooldowns, landing lag, IASA, and air-use limits;
- semantic presentation event emission and deduplication inputs.

Creator/package data supplies bounded parameters and ordered operations. It does not ship executable simulation code, native plugins, arbitrary shaders, or a client-only gameplay implementation.

## Cooked runtime execution

`CookedTimelineAbility` is the current interpreter for a cooked slot. It advances one stage per simulation tick, executes operations in order, starts approved stateful capabilities, spawns ordinary Shared hitboxes/projectiles, and emits semantic presentation events. `InternalCapabilityRegistry` resolves only trusted built-in `slop.internal.*` capabilities admitted by the cook profile.

Interruption belongs to the generic runtime. Hitstun, death, Burst, a new activation, and other simulation-owned overrides cancel the active operation/capability deterministically. Natural completion and cancellation use distinct lifecycle paths; cancellation must not leak aim state, hitboxes, velocity ownership, or capability state. Do not add authored cleanup branches to compensate for missing interruption ownership.

The client receives authoritative state and stable presentation event IDs keyed by match tick, entity, and operation index. It resolves semantic IDs to package bindings and deduplicates events across prediction and rollback. Presentation never changes simulation.

## Hitbox and projectile pipeline

Abilities use the existing Shared geometry and resolver APIs:

1. The timeline or trusted capability issues a typed spawn operation.
2. `HitboxGeometry` resolves fixed or baked-bone positions using the active cooked definition.
3. `SpellResolver` advances projectiles, checks entity/ground collision, applies damage and Knockback, and queues explosions.
4. `ServerSimulation` applies Hitstun, Hitstop, Clash, Combo Influence, Burst, and state transitions.

Bone-attached hitboxes use cooked pose data and semantic bone IDs. Projectile paths and explosions remain server-side. Do not add Unity physics queries or client-side trajectory logic to gameplay.

## Temporary native capabilities

FightGuy may use versioned `slop.internal.fightguy.*` capabilities while native behavior is decomposed into public primitives. They are trusted built-in exceptions, not package-defined behavior. Only the trusted built-in cook profile may admit them; Workshop content cannot reference them. Each exception requires an owner, reason, and migration path under ADR-0022.

## Shared runtime lifecycle

`ServerAbility` remains the Shared runtime base for `CookedTimelineAbility` and built-in capability adapters. They use its `OnStart`, `Tick`, `OnEnd`, `OnCancel`, `OnHitEntity`, resolver, and presentation sink hooks.

Nilus, the old LMB combo classes, and character/slot factory dispatch have been removed. New package behavior belongs in cooked timelines and typed/versioned operations. Do not add new global character/slot dispatch, raw authoring loaders, or manual data/config ownership.

## Change checklist

Before changing combat behavior:

- trace input → catalog entry → timeline/capability → resolver → state → presentation;
- identify whether the behavior belongs in a deterministic primitive, a typed operation contract, or a trusted temporary capability;
- preserve server/client Shared equivalence and immutable match content;
- define interruption, natural completion, hit identity, and presentation event behavior;
- add or update a behavioral Shared test when the observable contract is not already covered;
- run `dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true` and the targeted tests.

For new character content, follow [`sloparena-character-workflow`](../sloparena-character-workflow/SKILL.md) and [`docs/characters/adding-a-new-character.md`](../../docs/characters/adding-a-new-character.md). For universal mechanics, read [`docs/systems/combat-systems.md`](../../docs/systems/combat-systems.md) and [`docs/systems/ability-architecture.md`](../../docs/systems/ability-architecture.md).
