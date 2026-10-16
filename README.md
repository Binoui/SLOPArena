# SLOPArena

**An open-source 3D platform fighter built around expressive movement, knockback, and compact hero-style kits.**

SLOPArena takes the platform-fighter fundamentals of movement, damage percent, recovery, edge pressure, and launching opponents off the stage, then gives each fighter a small set of distinctive specials and tools to play around.

The goal is a game that is readable and competitive without taking itself too seriously: strong character identities, generous 3D hitboxes, expressive movement, and plenty of room for stupid things to happen.

> **Current state:** SLOPArena is preparing a playable friends demo with four admitted cooked demo-roster packages: FightGuy, Manki, Wibou, and Bonk. Package admission does not prove kit completeness or player acceptance. Character execution is cooked-only; Nilus and the old LMB combo compatibility path were retired on 2026-10-02. See the [playable friends demo reset](docs/plans/2026-09-05-playable-demo-reset.md) for current product work.

## The game

SLOPArena combines:

- camera-relative 8-direction movement, jump arcs, double jumps, fast-fall, ledges, and Dash;
- Smash-style damage percent, Knockback, Hitstun, Hitstop, Combo Influence, Clash, and Burst;
- readable hero kits with generous 3D hitboxes and meaningful recovery, zoning, and finisher choices;
- a server-authoritative GameServer for online matches and a local Shared simulation for prediction and training.

Each kit has a canonical **16-entry grid**: grounded and aerial variants for normals `1`, `2`, `3`, `4` and specials `A`, `E`, `R`, `F`. `LMB` and `RMB` are not persisted move identities; physical controls map to the canonical slots through the client input layer.

Solo uses an attacking CPU at the selected difficulty. Training starts with one idle CPU and keeps its controls visible on the HUD. Press Escape or controller Start to adjust Idle, Attack, Shield, or Avoid behavior, difficulty, and damage, or reset damage and positions. Turn off “Show during play” to hide the panel during gameplay; it always appears while paused.

### Gamepad controls

Left Stick moves; Right Stick controls the camera and ability aim. Face buttons South/East/West/North use normals 1/2/3/4; hold LB with those buttons for specials A/E/R/F. The selected move stays fixed until its face button is released. RB jumps, LT crouches/slides/fast-falls, RT dashes, D-pad Left bursts, L3 faces the camera, R3 toggles target lock, and Start pauses. Remap buttons and the special modifier under Settings → Controls → Controller.

## Current roster

| Fighter | Style | Content status |
| --- | --- | --- |
| [**FightGuy**](docs/characters/fightguy.md) | Close-range martial-arts brawler with Ki Shot, Rising Dragon, Cyclone Kick, and Fist of Fury | Admitted cooked package |
| [**Manki**](docs/characters/manki.md) | Explosive all-rounder / jetpack-bazooka skirmisher with bombs and aerosol area denial | Admitted cooked package |
| [**Wibou**](docs/characters/wibou.md) | Mid-range kitsune sword-spacing duelist focused on launches and air juggles | Admitted cooked package |
| [**Bonk**](docs/characters/bonk.md) | Greatsword fighter with sword-reach normals, targeted jump-slam recovery, and Blade Storm | Admitted cooked package; avatar visual/pose review pending |

Package admission identifies content accepted by the cooked roster manifest; it does not by itself establish kit completeness or player acceptance.

## Development

The game is built in **Unity 6** with a pure C# simulation shared by the client and GameServer. The simulation advances at **60 Hz**, keeps gameplay deterministic, and supports client prediction with server reconciliation and rollback.

### Quick start

```bash
git clone https://github.com/Binoui/SlopArena.git
cd SlopArena
```

Install .NET SDK 8 to work on the simulation, server, or C# tools. A clean clone
supports these checks without Unity or the local art installation:

```bash
dotnet build SlopArena.sln --nologo -p:SkipUnityPluginCopy=true
dotnet test SlopArena.sln --no-build --nologo -p:SkipUnityPluginCopy=true
```

The root solution also supports ordinary C# IDE navigation. See the
[tool inventory](tools/README.md) for the maintained command-line tools.

The Unity client requires Unity `6000.0.78f1`, the separately installed
Animancer package, and exact local rig/animation/presentation assets. A stock
clone cannot compile and play the complete client. Follow the
[Unity setup instructions](CONTRIBUTING.md#unity-client-and-authoring) before
opening the project; Shared must be built for Unity after those prerequisites
are available.

Run the development GameServer from the repository root:

```bash
dotnet run --project src/Server/ -p:SkipUnityPluginCopy=true -- src/Server/server.json
```

Its development configuration expects a local Master server for registration.
See the [local stack](deploy/local/README.md) for the separate Master checkout
and integration setup.

Explore the [knockback visual comparison](tools/KnockbackVisuals/README.md) to watch saved before/after trajectories for all four fighters. Run `python3 tools/KnockbackVisuals/serve.py` and open http://localhost:8765/, or open the standalone HTML directly in a browser.

## Architecture in one view

```text
Unity client ── input ──► Shared ServerSimulation ◄── input/state ── GameServer
     │                            │
     └── render authoritative state/events

Editable Character Package
  package.json + character.json + CharacterAssetCatalog.asset
                    │
                    ▼
       Shared compiler + Unity asset cook
                    │
                    ▼
Immutable content-cooked package
  manifest + runtime definition + poses + client bindings
```

The server and client consume the same cooked deterministic representation. Runtime matches pin package IDs, versions, hashes, dependencies, and capability versions in an immutable Match Content Catalog. Raw authoring JSON is cook input, not a runtime contract.

Explore the [interactive runtime architecture diagram](docs/generated/runtime-architecture.html) for component relationships, trust boundaries, and the primary runtime path.

The longer-term direction is cautious Workshop/package support: creators compose approved deterministic primitives and package-owned assets; they do not ship arbitrary simulation code, native plugins, or direct Unity-path dependencies. See [ADR-0022](docs/adr/0022-workshop-first-content-architecture.md) through [ADR-0030](docs/adr/0030-ability-lab-canonical-slot-projection.md).

## FightGuy and Ability Lab

FightGuy demonstrates the package-native workflow:

- edit `client/Unity/Assets/CharacterPackages/fightguy/`;
- inspect and cook through the Unity CLI;
- validate source, asset bindings, deterministic pose data, generated client bindings, and hashes;
- load the immutable result in Ability Lab, Training, PvP, and GameServer paths.

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target fightguy --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target fightguy --format json
```

Ability Lab is the primary gameplay editor for package drafts. It previews valid drafts through the same cooked definition and interpreter used by the game; an invalid draft is never silently substituted into a match.

To add a new character, start with [Adding a Character](docs/characters/adding-a-new-character.md). Do not copy the legacy C# registry path for new work.

### SlopBot creator guide

[SlopBot skills](skills/README.md) provide guided or fast project orientation and
humanoid character creation with explicit asset/tool handoffs, bounded animation
adaptation, and resumable local progress. Instructions are harness-neutral;
execution requires supported tools and approval. The next-character verified
package/local-play run remains the public-release gate.

## Documentation

Start with the [documentation map](docs/README.md), then read:

- [Architecture overview](docs/architecture-overview.md)
- [Combat systems](docs/systems/combat-systems.md)
- [Ability architecture](docs/systems/ability-architecture.md)
- [Ability Lab](docs/systems/ability-lab.md)
- [Animation system](docs/systems/animation-system.md)
- [Netcode architecture](docs/systems/netcode-architecture.md)
- [Testing and verification](docs/testing.md)
- [Contributing](CONTRIBUTING.md)

## Contributing

Issues, design feedback, code, art, documentation, and testing are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. This project uses the [MIT license](LICENSE) and follows the [Code of Conduct](CODE_OF_CONDUCT.md).

Thank you to the artists and developers behind the assets and tools used here. See [Credits](CREDITS.md) for their names and work.

## AI usage

SLOPArena uses agent-assisted development and generated art as practical tools. Contributions remain reviewed project work: determinism, licensing, gameplay correctness, and maintainability matter more than how an asset or patch was produced.

Coding assistants use the same public [project instructions](AGENTS.md) and
[task workflows](skills/README.md). See [agent setup](docs/contributing/agents.md)
for Codex, OMP, Claude Code and direct-file use.
