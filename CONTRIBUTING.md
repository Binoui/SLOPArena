# Contributing to SlopArena

SlopArena welcomes code, gameplay design, art, documentation, and playtesting contributions. All project communication and code use English. Read the [Code of Conduct](CODE_OF_CONDUCT.md) before participating.

## Development setup

Install:

- Unity `6000.0.78f1` through [Unity Hub](https://unity.com/download);
- .NET SDK 8 through [dotnet.microsoft.com](https://dotnet.microsoft.com).

Clone the repository and open `client/Unity/` in Unity Hub for the local training flow:

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

The repository has three runtime layers:

- `src/Shared/` — pure C# `netstandard2.1` deterministic simulation and content compiler;
- `src/Server/` — headless .NET GameServer and match orchestration;
- `client/Unity/` — Unity presentation, input, networking, Ability Lab, and asset cooking.

The generated Shared DLL is copied to `client/Unity/Assets/Plugins/SlopArena.Shared/` by the Shared build.

A clean clone does not include ignored licensed/local art or stage presentation prefabs.
Supply those assets separately for a complete Unity build and visible PVP stages.

## Agent tooling

Project instructions live in root [AGENTS.md](AGENTS.md); maintained workflows live
in [skills/](skills/README.md). Codex and OMP have generated discovery adapters;
Claude Code and other file-capable assistants can read the same public sources.
See [agent setup](docs/contributing/agents.md) for discovery, selected checkouts,
manual handoffs and explicit invocation policy. No private workspace or BMAD
installation is required.

The optional navigation-only [OMP solution](.omp/SlopArena.sln) includes Shared,
Server, their tests and C# tools. `.omp/lsp.json` invokes `omnisharp` on PATH with
this solution when OMP starts at the game root. Configure your own installed
language server if needed; this does not install it or generate Unity metadata.
Unity semantic navigation uses Editor-generated projects and the same bounded
lease protocol as other live Editor work. Compilation/presentation checks use
[Unity CLI](docs/contributing/unity-cli.md).

Routine numerical tuning does not automatically require new goldens. Prefer
focused behavioral assertions for mechanics; use the
[kit regression workflow](skills/sloparena-kit-regression-testing/SKILL.md) for
explicit scenario coverage and deliberate golden maintenance.

## Core rules

- The Shared simulation is the gameplay authority. Do not implement gameplay mechanics only in Unity.
- Keep `src/Shared/` free of Unity types and physics queries. Use the existing deterministic math and resolver APIs.
- Represent gameplay time in 60 Hz simulation ticks. Do not replace tick durations with frame-rate-dependent seconds.
- The client renders authoritative state and presentation events. Visuals, animation, VFX, and audio must not feed back into simulation.
- Preserve immutable package and match-content identity. A failed or stale cook must not silently fall back to another definition.
- Keep changes focused. Follow the repository's existing C# and Unity conventions instead of introducing parallel abstractions.

## Character contributions

New characters use the package-native workflow. Start with [Adding a Character](docs/characters/adding-a-new-character.md). The editable package lives under `client/Unity/Assets/CharacterPackages/<package>/` and contains:

The current product target is a playable friends demo with Manki, FightGuy, Wibou, and
Bonk. Package authoring remains technically supported, but a fifth character is not a demo
prerequisite. Nilus compatibility has been removed; characters use cooked packages only.

- `package.json` for package identity, dependencies, creator, license, and attribution;
- `character.json` for gameplay semantics and the canonical 16-slot move grid;
- `CharacterAssetCatalog.asset` for package-local Unity asset bindings.

Cooked runtime content belongs under `content-cooked/<package>/` and is admitted through the cooked roster manifest. Do not add a registry factory, raw runtime JSON loader, manual FightGuy animation configuration, or standalone skeleton payload for new work. There is no legacy character execution fallback.

## Verification

Choose the applicable mode in [Testing and verification](docs/testing.md):

- **Local iteration:** use the Editor development catalog and affected Ability Lab or
  Training path; do not publish a package for every tuning edit.
- **Integrated change:** run focused behavioral coverage and the applicable Shared,
  Server, Unity, or package checks.
- **Distributable demo:** cook accepted content, verify roster-complete publish outputs,
  build the client/server, and exercise the packaged join-to-rematch path.

Use [Unity CLI](docs/contributing/unity-cli.md) for concrete commands. Preserve the
Shared/server authority, exact package identity, and fail-closed cook behavior.

## Pull requests

1. Create a focused branch.
2. Explain the user-visible behavior and the authoritative data path.
3. Include the verification commands and their results.
4. Link relevant issues, ADRs, or design documents.
5. Keep generated artifacts and unrelated local changes out of the pull request.

Do not commit or push on behalf of another contributor. Follow the repository's [commit conventions](docs/contributing/conventions.md) when creating commits.

## Non-code contributions

Open an issue or pull request for:

- fighter and move design;
- 3D models, animation, VFX, audio, or arena work;
- UI and accessibility improvements;
- documentation and research;
- reproducible playtest findings.

For design changes, describe the intended counterplay, timing, and interaction with the shared simulation before implementation.
