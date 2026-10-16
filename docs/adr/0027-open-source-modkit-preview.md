# ADR-0027: Open-Source ModKit and Installed-Game Preview

**Status:** Accepted — 2026-08-21  
**Deciders:** @Binoui  
**Related:** ADR-0022 (Workshop-First Content Architecture), ADR-0024 (Creator Gameplay Primitives), ADR-0025 (Workshop Packages)

## Context

Creators need to author models, animation, VFX, audio, and gameplay configuration without receiving every private or licensed asset used by SLOPArena. Unity and external DCC tools already provide general-purpose authoring capabilities; SLOPArena should focus on its own character, stage, ability, timing, hitbox, validation, packaging, and playtesting concerns.

The ModKit must also preview the same behavior that the installed game and GameServer use. A Unity-editor-only approximation would allow content to work in the tool but fail under the authoritative shared simulation.

## Decision

The creator ModKit is distributed as open-source project/tooling code and includes only legally redistributable project assets. Creators may continue using Blender, Maya, Mixamo, or other external DCC tools for source production.

The ModKit focuses on SLOPArena-specific authoring:

- character and stage definitions;
- animation assignment and timing;
- approved ability primitives;
- hitbox and hurtbox authoring;
- movement and landing timing;
- VFX/SFX event references;
- validation and budget reporting;
- cooking and Workshop packaging;
- playtesting and publishing preparation.

Unity is the preferred authoring environment for the official ModKit. Normal humanoid fighters use Unity Humanoid rigs as the default path so built-in animations can be retargeted. Generic/custom rigs remain a future-supported path with additional creator-supplied animation requirements; Humanoid is not a universal content requirement.

The installed SLOPArena game is the authoritative preview environment. The ModKit cooks a temporary package, the installed game loads it through the same validation and runtime registry path, and the shared simulation runs the actual gameplay. The ModKit may show metadata or placeholders for private built-in resources, but it must not claim an approximation is authoritative.

The intended workflow is:

```text
Unity ModKit + external DCC tools
        │
        ▼
Cook and validate temporary package
        │
        ▼
Installed SLOPArena
        │
        ├── resolve built-in capabilities
        ├── load creator assets
        └── run the shared authoritative simulation
```

Official fighters, stages, and presentation content serve as the reference corpus. Where an official asset or capability bypasses the creator workflow, the exception is recorded under ADR-0022 with a reason, scope, owner, and migration path.

## Considered Options

- **Build a standalone proprietary editor** — rejected for the first architecture: it would recreate Blender/Unity functionality and delay SLOPArena-specific tooling.
- **Use only Unity-editor preview** — rejected: editor behavior could diverge from the installed game's runtime and authoritative simulation.
- **Load raw source assets in the installed game** — rejected: source formats are not the cooked runtime contract (ADR-0025).
- **Require Humanoid rigs** — rejected: it would exclude legitimate non-humanoid community fighters; custom rigs may trade away built-in animation reuse instead.

## Consequences

- The ModKit depends on a stable cooking and validation pipeline.
- Creator iteration requires a fast path from Unity authoring to an installed-game test package.
- Private built-in assets can remain out of the public ModKit while still being available during installed-game preview.
- Humanoid retargeting lowers the entry barrier, while custom rigs increase creator workload.
- Live ModKit-to-game communication may be added later without changing the authoritative preview rule.

## Non-Goals

This ADR does not define the Unity package layout, the final editor UI, the asset import implementation, the Steam publishing client, or the complete humanoid/custom-rig compatibility matrix.
