# SLOPArena

SLOPArena is a Unity 6 C# 3D platform fighter with a server-authoritative 60 Hz simulation. Prioritize the four-character friends demo and first remote feedback over platform expansion; follow `docs/plans/2026-09-05-playable-demo-reset.md`. Domain vocabulary lives in `CONTEXT.md`; the documentation map is `docs/README.md`.

## Policy

- Explain the problem and intended change once, then complete the approved scope. Reuse authorization already given; approval covers the bounded task, not a new confirmation for every step.
- Preserve unrelated working-tree changes and explicitly selected checkouts. Never stage every file in a shared dirty tree. Commit and push require explicit authorization: commit authorizes a commit, push authorizes pushing existing commits, and commit push authorizes both. Reading or invoking a skill grants neither. Follow `docs/contributing/conventions.md`; rewrite or squash history only when requested.
- Ask before installing tools. User-supplied numeric choices are binding; raise a correctness issue once if needed, then follow the agreed value.
- For architecture changes, document options and get approval first. State the problem and intended change before editing; trace input → catalog/package → Shared simulation → state/event → Unity presentation when debugging.

## Where things are

- `src/Shared/` owns the compiler, cooked runtime model, deterministic simulation, codecs, and rollback primitives; its `netstandard2.1` build normally copies the Shared plugin into `client/Unity/Assets/Plugins/SlopArena.Shared/`.
- `src/Server/` hosts the headless GameServer and match orchestration. The Master server is a separate repository for lobby/meta work; it does not simulate matches.
- `client/Unity/Assets/Scripts/` owns input, presentation, networking, UI, and editor tooling. `client/Unity/Assets/AbilityLab/` owns package editing and authoritative preview. Shared coverage lives in `tests/Shared.Tests/`.
- For new character work, read `docs/characters/adding-a-new-character.md`: edit `client/Unity/Assets/CharacterPackages/<package>/package.json` for identity/version/dependencies/license/attribution, `character.json` for gameplay and canonical slots, and `CharacterAssetCatalog.asset` for Unity bindings. `content-cooked/<package>/` is the immutable runtime result, not authoring source.
- Use `docs/architecture-overview.md` for runtime boundaries, `docs/systems/ability-architecture.md` for cooked abilities, `docs/systems/combat-systems.md` for combat, and `docs/systems/netcode-architecture.md` for prediction/rollback.
- For live canonical Unity Editor/Pipeline work, read `docs/contributing/unity-cli.md` and `docs/contributing/unity-editor-coordination.md` before operating. Use your own caller identity under that protocol; wait for your own bounded runtime lease, never request insertion into another owner's batch. Historical Markdown ownership and active panes are not authority. For verification, read `docs/testing.md`.

## Skills and setup

Maintained workflows live in [skills/](skills/README.md). `.agents/skills/` and
`.omp/skills/` are generated discovery adapters. Load only task-relevant workflows.
Read [agent setup](docs/contributing/agents.md) for discovery, checkout and tool
boundaries. No private workspace, BMAD installation or specific IDE is required.

For GitHub issue work, follow [issue tracking](docs/agents/issue-tracker.md) and
[triage labels](docs/agents/triage-labels.md). Project context is a single root
`CONTEXT.md` with `docs/adr/`; see [domain guidance](docs/agents/domain.md).

## Running and verifying

- For offline Shared/Server builds and tests use `-p:SkipUnityPluginCopy=true`. Copying a plugin into an open Editor requires your own bounded gateway lease and settled imports.
- Choose the applicable mode in `docs/testing.md`: local iteration, integrated change, or distributable demo. Keep focused behavioral coverage and applicable Shared/Server/Unity/package checks; do not force a publishing cook or unrelated runtime checks for ordinary tuning.
- For Unity-facing implementation, maintain the gitignored root `TESTING-UNITY.md` with at most five short, actionable human checks for current work. Retire resolved or superseded checks, archive old notes under ignored `tmp/testing-unity/`, and keep automated evidence in task/PR reports or linked artifacts. Follow the checklist guidance in `docs/testing.md`.
- Editor operations use the approved canonical project through the gateway, not a worktree-local connection or unowned raw CLI. Worktree agents share the same runtime lease and honor source windows; never silently substitute main for an invalid explicit target.

## Conventions that differ from defaults

- Keep gameplay authority in Shared/GameServer: `ServerSimulation` runs on the server and in local/client prediction. Training uses `LocalSimulationBridge`; PvP uses `RollbackSimulationBridge` with `NetplaySession` and `NetplayPredictor` to restore complete World Snapshots and predict every fighter. The former three-track model is retired from online play. Unity `PlayerRenderer` consumes state and semantic events; Unity physics, animation callbacks, VFX, and audio never decide damage, hit results, timing, or admission.
- Keep `src/Shared/` free of Unity types and engine physics queries. Use deterministic math and existing `SpellResolver`/geometry APIs for collision. Represent gameplay durations in 60 Hz ticks, normally `ushort`; preserve server/client Shared equivalence and immutable match content.
- New characters use the package-native path. The Shared compiler and Unity cook produce normalized runtime definitions, deterministic poses, generated client bindings, and a manifest; the Match Content Catalog pins package IDs, versions, dependencies, capability versions, and hashes. Raw authoring JSON is cook input, never the runtime contract.
- Use the canonical 16-entry grid: ground and air variants of `1`, `2`, `3`, `4`, `A`, `E`, `R`, and `F`. Physical controls are adapters, not persisted move identity; use semantic package IDs and canonical slot projection, not a second mapping.
- Author new abilities as fixed cooked timelines with ordered typed/versioned operations. Engine-owned deterministic primitives own movement, hitboxes, projectiles, damage, Knockback, Hitstun, Hitstop, defense, grabs, timing locks, and presentation events. `ServerAbility` remains a Shared lifecycle seam for `CookedTimelineAbility` and trusted built-in capabilities, not the universal new-content model. Nilus and legacy character/slot factory dispatch have been removed. Burst input is reserved and inert; attack Clash is not implemented, while reciprocal grabs can clash.
- Only the trusted built-in cook profile may admit the temporary `slop.internal.*` capabilities owned by FightGuy, Wibou and Manki; Bashie's F uses the approved Manki stand-in. Workshop/package content cannot self-grant access. Owners and migration paths are listed in `docs/systems/ability-architecture.md`.
- Use `MonoBehaviour.Update`/`FixedUpdate` and Unity InputSystem APIs. Animancer plays semantic package bindings directly; do not give AnimatorController gameplay ownership.
- Reuse existing infrastructure before adding abstractions.

## Known pitfalls

- Admitted roster fighters use character packages; admission does not prove kit completeness or player acceptance. Resolve the roster from `content-cooked/roster/manifest.json`. Nilus, the Compatibility preview tab, and the old LMB combo classes are retired. Do not recreate a legacy character execution path.
- Warp is not a current gameplay contract. Do not add or document Warp mechanics as new behavior; use current movement, targeting, Shield, air-dodge, and recovery rules in `CONTEXT.md` and `docs/systems/combat-systems.md`.


## Current release and operations routing

Routine Steam/VPS releases follow `skills/sloparena-build-export/SKILL.md`
and `docs/systems/release-pipeline.md`: isolated licensed Unity client build,
coordinated GitHub Actions deployment, then manual Steam default activation.
The build does not operate the canonical Editor or require its lease/shutdown.
Use `skills/sloparena-ops/SKILL.md` for read-only match diagnostics and the
two exact permitted VPS log commands. Reuse approvals/access already given
within scope; do not repeat unrelated historical verification or require a
privileged status session before reading logs.
