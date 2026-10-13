# Contributing to SlopArena

SlopArena welcomes code, gameplay design, art, documentation, and playtesting contributions. All project communication and code use English. Read the [Code of Conduct](CODE_OF_CONDUCT.md) before participating.

## Development setup

Clone the repository:

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

The repository has three runtime layers:

- `src/Shared/` — pure C# `netstandard2.1` deterministic simulation and content compiler;
- `src/Server/` — headless .NET GameServer and match orchestration;
- `client/Unity/` — Unity presentation, input, networking, Ability Lab, and asset cooking.

### Simulation, server, and command-line tools

Install .NET SDK 8 through [dotnet.microsoft.com](https://dotnet.microsoft.com).
The root solution includes Shared, Server, their tests, and the maintained
[C# tools](tools/README.md). Build and test from the repository root:

```bash
dotnet build SlopArena.sln --nologo -p:SkipUnityPluginCopy=true
dotnet test SlopArena.sln --no-build --nologo -p:SkipUnityPluginCopy=true
```

These paths work without Unity or the ignored local art. The property prevents
offline .NET checks from copying plugins into an open Unity Editor. You can also
build/test a single affected project. Documentation checks require Python 3;
the optional agent-tool tests require Node 22.18 or newer. Exact commands live
in [quality checks](docs/contributing/quality.md).

### Unity client and authoring

The complete Unity client currently needs a local asset installation. Before
opening it, provide:

- Unity `6000.0.78f1` through [Unity Hub](https://unity.com/download);
- the required Animancer package under `client/Unity/Packages/com.kybernetik.animancer/`
  (the recorded maintainer installation is 8.4.0);
- the exact rigs, clips, VFX, audio and stage presentation assets referenced by
  the character catalogs and scenes, preserving their Unity metadata and import
  settings.

Animancer and restricted/local art are excluded from Git. A general download
of similarly named packs does not establish the required bindings. Consult
[asset source and license notes](docs/assets/credits-and-licenses.md) and
[asset conventions](docs/contributing/conventions.md); supply only assets you
are entitled to use. The worktree package-link script reuses an existing local
installation and does not provision a fresh contributor checkout.

The [Unity setup guide](docs/contributing/unity-setup.md) records dependency
ownership, exact input requirements, and the read-only
`python3 tools/check_unity_setup.py` preflight. Run it after provisioning the
Shared plugin and local assets; its result does not replace Unity compilation.

For initial setup with this Unity project closed, generate its Shared DLL:

```bash
dotnet build src/Shared/ --nologo
```

This intentional build copies only the Shared DLL and its PDB into
`client/Unity/Assets/Plugins/SlopArena.Shared/`. If the canonical Editor is
already open, follow the [Editor coordination protocol](docs/contributing/unity-editor-coordination.md)
and acquire your own bounded gateway hold before that copy. Then open
`client/Unity/` in Unity Hub, open the tracked `Frontend` scene, and inspect
compilation errors before using Training or Ability Lab. Follow
[Unity CLI](docs/contributing/unity-cli.md) and [testing](docs/testing.md) for
the affected workflow.

## Agent tooling

Project instructions live in root [AGENTS.md](AGENTS.md); maintained workflows live
in [skills/](skills/README.md). Codex and OMP have generated discovery adapters;
Claude Code and other file-capable assistants can read the same public sources.
See [agent setup](docs/contributing/agents.md) for discovery, selected checkouts,
manual handoffs and explicit invocation policy. No private workspace or BMAD
installation is required.

The public [root solution](SlopArena.sln) includes Shared, Server, their tests
and C# tools. `.omp/lsp.json` invokes `omnisharp` on PATH with this solution when
OMP starts at the game root. Configure your own installed
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
