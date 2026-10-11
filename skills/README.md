# SlopArena workflows

This is the maintained source for project development and creator skills. Public
`docs/` owns project contracts; skills route a task to relevant docs and procedures.
Read root [AGENTS.md](../AGENTS.md) before project work.

Codex discovers generated adapters in `.agents/skills/`; OMP uses `.omp/skills/`.
Other file-capable assistants can read `skills/<name>/SKILL.md` directly. See
[agent setup](../docs/contributing/agents.md) for checkout selection and discovery.
Edit canonical files here, then run `python3 tools/sync_agent_skills.py --write`;
CI checks adapter drift. Do not copy discovery adapters as standalone skills.

Skills do not install tools, acquire Editor ownership or authorize commits/pushes
by being loaded. Explicit-only workflows retain invocation policy in `agents/`.
Load only the workflow needed by the task; ordinary orientation is not a build or
a full review. Some optional workflows require Blender, live Unity or operator access.
Missing capabilities require a concrete handoff, not a fabricated success.

SlopBot remains a separate three-skill creator bundle with its own scope, progress
and release acceptance: read [the SlopBot guide](slopbot/README.md). Development
workflows below do not widen that creator bundle.

| Workflow | Use |
|---|---|
| [blender-character-polish](blender-character-polish/SKILL.md) | Safely polish existing textured, rigged characters through Blender MCP. Use for mesh, normals, texture, material, accessory, skin-weight and deformation cleanup; candidate comparisons and game-asset handoff. Not animation authoring or Unity package integration. |
| [branch-status](branch-status/SKILL.md) | Show status of all local feature branches vs main — commits ahead/behind, last message, open PR if any. Use only for requested branch inventory, branch switching/integration, or PR drafting. |
| [code-review](code-review/SKILL.md) | Review SlopArena changes against project standards and the approved specification. |
| [implement](implement/SKILL.md) | Implement SlopArena work from an approved spec or ticket. |
| [movement-platform-fighter-3d](movement-platform-fighter-3d/SKILL.md) | SlopArena Unity 6 C# movement and combat fundamentals: camera-relative 8-way movement, JumpArc, ShortHop, FastFall, Dash, ledges, server authority, and Smash-style percent. |
| [orient](orient/SKILL.md) | Minimal read-only SlopArena session orientation: current demo goal, working-tree status, and relevant next action. Load technical references only for the current task. |
| [sim-test](sim-test/SKILL.md) | Requested Shared tests with an optional filter; no Unity plugin copy. |
| [sloparena-ability-lab](sloparena-ability-lab/SKILL.md) | Use when an agent needs native-first Ability Lab runs or scenarios against idle/shield opponents, Shared hit/block/miss/grab outcomes, recorded-frame preview or capture, or workspace diagnostics. |
| [sloparena-animation-authoring](sloparena-animation-authoring/SKILL.md) | Author and safely import SlopArena character animation clips through Blender, Unity Humanoid import, package catalog binding, cooking, and real-surface verification. |
| [sloparena-asset-selection](sloparena-asset-selection/SKILL.md) | Prepare verified Unity environment assets from a stage concept: deterministic catalog retrieval, diverse shortlist generation, safe Unitypackage materialization, prefab inspection, thumbnails, and contact-sheet evidence. |
| [sloparena-build](sloparena-build/SKILL.md) | Verify SlopArena Unity compilation and affected runtime behavior through Unity CLI and com.unity.pipeline after Unity-facing changes or compiler errors; also use for runtime Unity screenshots, UI, HUD, and viewport tasks. Use docs/testing.md to distinguish local iteration from package publishing. |
| [sloparena-build-export](sloparena-build-export/SKILL.md) | Releases the Windows Steam Playtest client and compatible VPS backends through an isolated local Unity build and GitHub Actions, with manual Steam activation. Use for release, publish or deploy requests; use sloparena-ops for read-only logs. |
| [sloparena-character-workflow](sloparena-character-workflow/SKILL.md) | Package-native workflow for designing, authoring, cooking, validating, and previewing SlopArena characters. Covers Character Packages, asset catalogs, deterministic cooking, generated bindings, admission, hashes, and Ability Lab. |
| [sloparena-combat-engine](sloparena-combat-engine/SKILL.md) | SlopArena server-authoritative combat: deterministic cooked timelines, typed operations, approved capabilities, hitbox/projectile resolution, interruption ownership, and legacy ServerAbility compatibility. |
| [sloparena-entity-system](sloparena-entity-system/SKILL.md) | SlopArena entity identity, Shared hitbox processing, NPC input, bone attachments, and Unity presentation boundaries. |
| [sloparena-finish-branch](sloparena-finish-branch/SKILL.md) | Verify and prepare a branch; perform only authorized commit, push and PR operations. |
| [sloparena-kit-regression-testing](sloparena-kit-regression-testing/SKILL.md) | Use for explicit kit regression coverage, KitScenario or AssertGoldenScenario maintenance, REGENERATE_GOLDENS, and diagnosing failing golden diffs. Not for routine numerical move tuning, adding content alone, or Unity-only presentation changes; use docs/testing.md for those verification modes. |
| [sloparena-move-data-report](sloparena-move-data-report/SKILL.md) | Record and report a character's move data — authored frame data (trigger/active/duration/IASA/landing lag), simulated per-hit trajectories with frame advantage, knockback shape, or a full eight-normal animation/hurtbox/role audit — via scripts/move-data.sh → tools/MoveDataReport. |
| [sloparena-netcode](sloparena-netcode/SKILL.md) | SlopArena server-authoritative UDP netcode with Shared simulation, client prediction, rollback reconciliation, match content admission, and Unity bridges. |
| [sloparena-ops](sloparena-ops/SKILL.md) | Investigates SlopArena Steam/VPS readiness, connection failures, lag and match logs. Use when the user asks for logs, reports frozen or snapping fighters, or asks whether the servers are up. Alfred is an explicitly selected historical alternative. |
| [sloparena-stage-authoring](sloparena-stage-authoring/SKILL.md) | Author and maintain SlopArena PVP stages through separate authoritative collision baking and cosmetic Unity presentation, with fixed topology, asset provenance, structural inspection, and human PVP acceptance. |
| [sloparena-stage-design](sloparena-stage-design/SKILL.md) | Interactive human+agent DESIGN phase for SlopArena PVP stages: visual composition, gray-box wireframing, and a locked Stage Design Brief. Use for stage design, stage wireframe, stage composition, design a stage, blockout, or 'what should the stage look like'. Production/baking/lighting work belongs to sloparena-stage-authoring. |
| [slopbot](slopbot/SKILL.md) | Guide users through SlopArena project orientation, humanoid character creation, bounded adaptation of existing animation clips, and resuming character work. Use for SlopBot requests or help making a SlopArena fighter; not for stage/game-mode creation, unrelated coding, deployment, or publication. |
| [slopbot-animation](slopbot-animation/SKILL.md) | Adapt an existing sourced humanoid animation clip within an approved scope, then verify its exact import, binding, and playback on the actual character. |
| [slopbot-character](slopbot-character/SKILL.md) | Plan and complete an approved SlopArena humanoid character package using the project's existing character, Ability Lab, and acceptance workflows. |
