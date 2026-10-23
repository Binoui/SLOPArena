# SLOPArena workflows

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
| [code-review](code-review/SKILL.md) | Review SLOPArena changes against project standards and the approved specification. |
| [implement](implement/SKILL.md) | Implement SLOPArena work from an approved spec or ticket. |
| [movement-platform-fighter-3d](movement-platform-fighter-3d/SKILL.md) | SLOPArena Unity 6 C# movement and combat fundamentals: camera-relative 8-way movement, JumpArc, ShortHop, FastFall, grounded Shield, directional air dodge, server authority, and Smash-style percent. |
| [orient](orient/SKILL.md) | Minimal read-only SLOPArena session orientation: current demo goal, working-tree status, and relevant next action. Load technical references only for the current task. |
| [sim-test](sim-test/SKILL.md) | Requested Shared tests with an optional filter; no Unity plugin copy. |
| [sloparena-ability-lab](sloparena-ability-lab/SKILL.md) | Use when an agent needs native-first Ability Lab runs or scenarios against idle/shield opponents, Shared hit/block/miss/grab outcomes, recorded-frame preview or capture, or workspace diagnostics. |
| [sloparena-animation-authoring](sloparena-animation-authoring/SKILL.md) | Author SLOPArena character animations with the GenRA/Blender workflow, actual-character review before move integration, Unity Humanoid import, package binding and cooking. |
| [sloparena-asset-selection](sloparena-asset-selection/SKILL.md) | Prepare verified Unity environment assets from a stage concept: deterministic catalog retrieval, diverse shortlist generation, safe Unitypackage materialization, prefab inspection, thumbnails, and contact-sheet evidence. |
| [sloparena-build](sloparena-build/SKILL.md) | Verify SLOPArena Unity compilation and affected runtime behavior through Unity CLI and com.unity.pipeline after Unity-facing changes or compiler errors; also use for runtime Unity screenshots, UI, HUD, and viewport tasks. Use docs/testing.md to distinguish local iteration from package publishing. |
| [sloparena-build-export](sloparena-build-export/SKILL.md) | Releases the Windows Steam Playtest client and compatible VPS backends through an isolated local Unity build and GitHub Actions, with manual Steam activation. Use for release, publish or deploy requests; use sloparena-ops for read-only logs. |
| [sloparena-character-workflow](sloparena-character-workflow/SKILL.md) | Package-native workflow for designing, authoring, cooking, validating, and previewing SLOPArena characters. Covers Character Packages, asset catalogs, deterministic cooking, generated bindings, admission, hashes, and Ability Lab. |
| [sloparena-character-vfx-pass](sloparena-character-vfx-pass/SKILL.md) | Give a fighter a combat presentation pass: package-owned VFX, sounds, trail style and projectile visuals from a presentation builder, timeline cues, cooking and before/after hit-feel review captures. |
| [sloparena-combat-engine](sloparena-combat-engine/SKILL.md) | SLOPArena server-authoritative combat: deterministic cooked timelines, typed operations, admitted engine capabilities, hitbox/projectile resolution, and Shared lifecycle and interruption ownership. |
| [sloparena-entity-system](sloparena-entity-system/SKILL.md) | SLOPArena entity identity, Shared hitbox processing, NPC input, bone attachments, and Unity presentation boundaries. |
| [sloparena-finish-branch](sloparena-finish-branch/SKILL.md) | Verify and prepare a branch; perform only authorized commit, push and PR operations. |
| [sloparena-kit-regression-testing](sloparena-kit-regression-testing/SKILL.md) | Use for explicit kit regression coverage, KitScenario maintenance, and focused combat regression tests. Not for routine numerical move tuning, adding content alone, or Unity-only presentation changes; use docs/testing.md for those verification modes. |
| [sloparena-move-data-report](sloparena-move-data-report/SKILL.md) | Record and report a character's move data — authored frame data (trigger/active/duration/IASA/landing lag), simulated per-hit trajectories with frame advantage, knockback shape, or a full eight-normal animation/hurtbox/role audit — via scripts/move-data.sh → tools/MoveDataReport. |
| [sloparena-netcode](sloparena-netcode/SKILL.md) | SLOPArena Steam and development UDP netcode with Shared full-world rollback, match content admission, authoritative pacing, and Unity bridges. |
| [sloparena-ops](sloparena-ops/SKILL.md) | Investigates SLOPArena Steam/VPS readiness, connection failures, lag and match logs. Use when the user asks for logs, reports frozen or snapping fighters, or asks whether the servers are up. Alfred is an explicitly selected historical alternative. |
| [sloparena-stage-authoring](sloparena-stage-authoring/SKILL.md) | Implement PvP stages and solo Arcade courses: authoritative collision/marker baking, separate presentation (including procedural Blender art and gameplay-camera views), geometry/spacing tuning and source-to-bake verification. |
| [sloparena-stage-design](sloparena-stage-design/SKILL.md) | Design PvP stages and solo traversal courses: focused layout interviews, jump/shortcut choices, route sketches, composition and a Stage Design Brief. |
| [slopbot](slopbot/SKILL.md) | Guide users through SLOPArena project orientation, humanoid character creation, bounded adaptation of existing animation clips, and resuming character work. Use for SlopBot requests or help making a SLOPArena fighter; not for stage/game-mode creation, unrelated coding, deployment, or publication. |
| [slopbot-animation](slopbot-animation/SKILL.md) | Adapt an existing sourced humanoid animation clip within an approved scope, then verify its exact import, binding, and playback on the actual character. |
| [slopbot-character](slopbot-character/SKILL.md) | Plan and complete an approved SLOPArena humanoid character package using the project's existing character, Ability Lab, and acceptance workflows. |
