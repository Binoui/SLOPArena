# SLOPArena Documentation Map

Canonical guidance is short and linked below. Dated plans, research, generated reports, handoffs, and accepted ADRs are preserved as project records; they are not automatically current implementation instructions.

For release or log work, start directly with [Release Pipeline](systems/release-pipeline.md)
or [sloparena-ops](../skills/sloparena-ops/SKILL.md). The Alfred home runbook is historical;
current Steam operations use the VPS.

## Start here

Read these in order:

1. [Playable friends demo reset](plans/2026-09-05-playable-demo-reset.md)
2. [Steam Playtest identity and gameplay on the VPS](plans/2026-09-25-steam-playtest-integration.md) — architecture, implementation contracts and acceptance gates; deployment status is maintained in private operator records.
3. [Architecture overview](architecture-overview.md)
4. [Testing and verification](testing.md)
5. [Contributing](../CONTRIBUTING.md)
## Architecture

| Document | Use it for |
| --- | --- |
| [Ability Architecture](systems/ability-architecture.md) | Cooked timelines, typed operations, capabilities, and interruption |
| [Animation System](systems/animation-system.md) | Animancer playback, semantic bindings, and timing |
| [Netcode Architecture](systems/netcode-architecture.md) | GameServer, prediction, reconciliation, and transport |
| [Replay v1 design](plans/2026-10-13-replay-v1-design.md) | Personal-review/bug-report scope, playback and delivery contract, parity gate and spectator extension |
| [Replay v1 implementation spec](spec-replay-v1/spec-replay-v1.md) | Draft recording/API contracts, parallel ownership and verification gates; proposed storage revision |
| [Release Pipeline](systems/release-pipeline.md) | Isolated client build, coordinated CI/VPS release and manual Steam activation |
| [Hosted owner console](systems/owner-console.md) | Independent console deployment, owner password login and private Master access |
| [Operations skill](../skills/sloparena-ops/SKILL.md) | Match diagnostics, exact VPS process-log commands and readiness |
| [Troubleshooting](systems/troubleshooting.md) | Current Steam startup, lag and admission investigation |
| [Restricted local stack](../deploy/local/README.md) | Caddy/PostgreSQL/GameServer image-only integration without changing the home endpoint |
| [Restricted VPS release](../deploy/vps/README.md) | Operator bootstrap, pinned deployment, rollback, and external ingress gates |
| [Unity CLI](contributing/unity-cli.md) | Inspect/cook commands, Ability Lab agent commands, and live Editor verification |

## Gameplay systems

Use [Project context](../CONTEXT.md) for canonical gameplay vocabulary and settled mechanics.

| Document | Use it for |
| --- | --- |
| [Combat Systems](systems/combat-systems.md) | Universal 8-normal/4-special model and combat mechanics |
| [Game mode exploration](design/game-modes.md) | Capture, volleyball, crown, solo arcade and moving-platform directions; proposed rules and open decisions |
| [Solo Arcade feature brief](design/arcade.md) | Approved Slopball and FightGuy Course scope, rules, offline entry and personal records |
| [Solo Arcade implementation plan](design/arcade-implementation.md) | Focused Shared objects, unified stage migration, Slopball-first delivery and verification handoff |
| [Arcade Phase 1](design/arcade-phase-1.md) | Ball/contact foundation, test-court launch steps and verification status |
| [Arcade Phase 2](design/arcade-phase-2.md) | Playable Slopball, court/shot rules, launch steps and native verification |
| [Arcade Phase 3](design/arcade-phase-3.md) | Offline menu, locked starts, results/retry and compatible local personal bests |
| [Arcade Phase 4](design/arcade-phase-4.md) | FightGuy Sanctuary greybox, targets, checkpoints, timed completion and course records |
| [Arcade Phase 5](design/arcade-phase-5.md) | Cloudtop first-crossing art, wider course spacing, local asset sources and verification |
| [Solo Arcade interview log](design/arcade-interview.md) | Seven resolved question rounds and inspected runtime/kit constraints |
| [Hitbox System](systems/hitbox-system.md) | Hitbox, hurtbox, and collision geometry |
| [Hitstun DI](systems/hitstun-di.md) | Hitstun, Hitstop, and Combo Influence design |
| [Ability Lab](systems/ability-lab.md) | Package editing, authoritative preview, and agent-facing workspace commands |
| [Knockback visual comparison](../tools/KnockbackVisuals/README.md) | Saved before/after flight playback, time scrubbing, and offline trajectory comparisons |
| [NPC System](systems/npc-system.md) | Training entities and AI |
| [Blast Zones](systems/blast-zones.md) | Void death and arena boundaries |
| [VFX and Particles](systems/vfx-particles.md) | Presentation effects and visual contracts |

## Visual design

| Document | Use it for |
| --- | --- |
| [Visual language](design/visual-language.md) | Graphic identity, UI composition, palette, typography, motion, and presentation voice |
| [Stage concepts](design/stage-concepts.md) | Gameplay-first PVP stage concepts, readability, topology, and background hierarchy |
| [Traversal courses](design/traversal-courses.md) | Solo course interviews, ordinary/ability route choices, jump calibration and repeatable tuning |
| [Course replay and capture](testing/course-replay.md) | Scenario/input files, tick-based screenshots, observed checks and Editor restoration |
| [Art and asset conventions](contributing/conventions.md) | 3D character rendering, asset production, naming, licensing, and hygiene |
| [Credits](../CREDITS.md) | Public thank-you and creator links |
| [Asset source and license notes](assets/credits-and-licenses.md) | Internal provenance and release checks |
| [Contributor input inventory](assets/contributor-inputs.md) | Catalog dependencies, local-only asset families and provenance gaps |
| [Visual presentation baseline](visual-baseline.md) | Repeatable gameplay-camera evidence for visual comparisons |
| [3D visual-production process proposal](plans/2026-10-15-3d-visual-production-process.md) | Stage-first plan for design, Blender production, material-accurate review, reuse and targeted model/VFX work |
| [AI 3D workflow research](research/2026-10-15-ai-3d-production-workflows.md) | Revision-pinned study of gamedev-skills and OhMyGame, including transferable practices and implementation limits |

## Character authoring

| Document | Use it for |
| --- | --- |
| [Adding a Character](characters/adding-a-new-character.md) | Package source, asset catalog, cooking, and admission |
| [Character import checklist](characters/character-import-checklist.md) | Asset import and presentation checklist |
| [Character model polish](characters/character-model-polish.md) | Directed post-generation repair, visual acceptance, rig-change boundaries, and Manki lessons |
| [Kit design principles](characters/character-kit-design-principles.md) | Fighter roles, counterplay, and the canonical move grid |
| [FightGuy reference](characters/fightguy.md) | First cooked package and runtime path |

New packages live under `client/Unity/Assets/CharacterPackages/<package>/`. Cooked runtime artifacts live under `content-cooked/<package>/`.

## Character roster

| Character | Status | Reference |
| --- | --- | --- |
| FightGuy | Cooked package; reference vertical slice | [FightGuy](characters/fightguy.md) |
| Bonk | Cooked package; rostered | [Bonk](characters/bonk.md) |
| Manki | Cooked package; rostered | [Manki](characters/manki.md) |
| Wibou | Cooked package; rostered | [Wibou](characters/wibou.md) |

The [Nilus page](characters/nilus.md) is a historical record of a retired character, not
an implementation or authoring guide.

## Contributing

| Document | Use it for |
| --- | --- |
| [Art and asset conventions](contributing/conventions.md) | Visual direction, naming, licensing, and asset hygiene |
| [Coding assistant setup](contributing/agents.md) | Shared instructions, skill discovery, checkouts, permissions, and manual handoffs |
| [Editor coordination](contributing/unity-editor-coordination.md) | Caller identities, bounded leases, preservation, and recovery |
| [Quality checks](contributing/quality.md) | Documentation, adapter, gateway, and applicable build/test commands |
| [Project tools](../tools/README.md) | Maintained .NET build set, reports, diagnostics, and research boundaries |
| [Contributor cleanup plan](plans/2026-10-13-contributor-cleanup.md) | Bounded cleanup batches and pending Unity decisions |
| [Unity CLI](contributing/unity-cli.md) | Package inspection/cooking, Ability Lab commands, and Editor checks |
| [Unity tools](contributing/unity-tools.md) | Supported menu groups and package self-test preservation |
| [Unity contributor setup](contributing/unity-setup.md) | Dependency owners, local inputs and offline catalog preflight |
| [Unity dependency ownership](plans/2026-10-13-unity-dependency-ownership.md) | Implemented managed dependency consolidation and proposed Pipeline fork route |
| [Workshop creator/reference target](plans/2026-10-13-public-reference-kit.md) | Deferred Editor-first authoring, Ability Lab/Training and package publication scope |
| [Repository contributing guide](../CONTRIBUTING.md) | Setup, rules, verification, and pull requests |
| [Code of Conduct](../CODE_OF_CONDUCT.md) | Community standards |

## Accepted ADRs

Accepted records for current gameplay, runtime and content decisions include:

- [ADR-0022: Workshop-first content architecture](adr/0022-workshop-first-content-architecture.md)
- [ADR-0023: Built-in content API](adr/0023-built-in-content-api.md)
- [ADR-0024: Deterministic creator gameplay primitives](adr/0024-deterministic-creator-gameplay-primitives.md)
- [ADR-0025: Workshop package architecture](adr/0025-workshop-package-architecture.md)
- [ADR-0026: Workshop multiplayer synchronization](adr/0026-workshop-multiplayer-synchronization.md)
- [ADR-0027: Open-source ModKit and installed-game preview](adr/0027-open-source-modkit-preview.md)
- [ADR-0028: Built-in content compatibility](adr/0028-built-in-content-compatibility.md)
- [ADR-0029: Character authoring and cooking](adr/0029-character-authoring-and-cooking.md)
- [ADR-0030: Ability Lab canonical slot projection](adr/0030-ability-lab-canonical-slot-projection.md)
- [ADR-0031: Character asset dependency classification](adr/0031-character-asset-dependency-classification.md)
- [ADR-0033: GameHost input Replays with compatible Shared playback](adr/0033-gamehost-input-replays-with-shared-playback.md)
- [ADR-0036: Arcade PvP kit parity and legal fall resets](adr/0036-arcade-pvp-kit-parity-and-legal-fall-resets.md)
- [ADR-0037: Arcade Shared objects and unified stage artifact](adr/0037-arcade-shared-objects-and-unified-stage-artifact.md)

Older accepted ADRs remain the decision record for the mechanics they cover. If a living guide conflicts with an accepted ADR or current code, update the living guide; do not rewrite the historical decision.

## Research and reference

The `research/` directory contains design analysis and external references, including:

- [Melee frame data](research/melee-frame-data.md)
- [Melee frame analysis](research/melee-frame-analysis.md)
- [Melee Knockback model](research/melee-knockback-model.md)
- [Melee movement audit](research/melee-movement-audit.md)
- [Melee/netcode impact](research/melee-model-netcode-impact.md)
- [DKO mechanics](research/dko-mechanics.md)
- [DKO character kits](research/dko-character-kits.md)

- [Stage authoring POC debrief](research/stage-authoring-poc-debrief.md) — imported-asset lessons and recommendations for a future stage-authoring skill.

These documents inform design but do not override the Shared implementation or accepted ADRs.

## Historical plans and reports

- `plans/` contains dated roadmaps, implementation plans, handoffs, and superseded approaches.
- `generated/` contains generated move-data, self-play, and analysis reports.
- `handoffs/` contains dated implementation handoffs.
- `superpowers/` contains archived plans and specifications.

The [FightGuy cooking cutover](plans/2026-08-26-fightguy-character-cooking-cutover.md) is a
historical migration record, not current execution work.
