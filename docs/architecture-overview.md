# Architecture Overview

Read this map before changing SlopArena. The authoritative gameplay implementation is the pure C# Shared simulation. Unity presents it; the GameServer hosts it.

## Repository map

```text
SlopArena/
├── client/Unity/
│   └── Assets/
│       ├── Scripts/                 # Unity input, presentation, networking, UI, editor tools
│       ├── AbilityLab/              # package editing and preview
│       ├── CharacterPackages/       # editable package source and asset catalogs
│       └── Plugins/SlopArena.Shared/ # generated Shared DLL consumed by Unity
├── src/
│   ├── Shared/                      # netstandard2.1 compiler, simulation, codecs, runtime model
│   └── Server/                      # headless GameServer and match orchestration
├── tests/Shared.Tests/              # Shared simulation, compiler, codec, and catalog tests
├── content-cooked/                  # immutable cooked runtime packages and roster manifest
├── content/                         # source content still used by legacy compatibility paths
├── tools/                           # asset inspection, baking, reports, and documentation checks
└── docs/                            # canonical guidance, ADRs, plans, research, and reports
```

## Content boundary

New characters follow one source-to-runtime path:

```text
Assets/CharacterPackages/<package>/
  package.json + character.json + CharacterAssetCatalog.asset
                         │
                         ▼
        Shared compiler + Unity asset/pose cook
                         │
                         ▼
content-cooked/<package>/
  manifest.json + character.runtime.json + poses.bin + client.bindings
                         │
                         ▼
              Match Content Catalog
```

`package.json` owns identity and metadata. `character.json` owns gameplay semantics. `CharacterAssetCatalog.asset` owns imported Unity bindings. Raw authoring JSON is not a runtime contract. Cooked packages are immutable, hash-addressed, and pinned per match.

FightGuy is the first cooked vertical slice. Its editable source is under `client/Unity/Assets/CharacterPackages/fightguy/`; its canonical runtime package is under `content-cooked/fightguy/`. The generated client catalog is a regenerable presentation cache.

FightGuy, Manki, Wibou, and Bonk are package-native cooked roster characters. Nilus, its registry/factory dispatch, and the old LMB combo classes were removed on 2026-10-02. Catalog admission and ability activation use cooked packages only; missing cooked content never falls back to a C# character definition.

## Runtime flow

```text
InputController / NetworkClient
          │
          ▼
      InputState
          │
          ▼
Shared ServerSimulation
  ├── movement and state transitions
  ├── cooked timeline/capability execution
  ├── SpellResolver hitbox/projectile collision
  └── authoritative CharacterState/events
          │
          ├── Training: LocalSimulationBridge
          └── PvP: RollbackSimulationBridge + GameServer
                         │
                         ▼
               PlayerRenderer / UI / VFX
```

The GameServer validates and loads the exact package set before simulation starts. Clients
verify the same package IDs, versions, dependencies, capability versions, and hashes. A
match never observes a later recook.

Arena binary deserialization builds the derived collision spatial grid once, so direct
match loads and registry loads both use Shared's broadphase queries. The grid is not
serialized; callers must not rebuild it after loading an `.arena` file.

## Ability boundary

Package abilities are fixed timelines of typed, versioned operations on the canonical
16-entry grid: grounded and aerial variants for `1`, `2`, `3`, `4`, `A`, `E`, `R`, and `F`.
Engine-owned Shared primitives implement movement, hitbox/projectile resolution, damage,
Knockback, Hitstun, Hitstop, Clash, Shield, timing locks, and presentation events.

`CookedTimelineAbility` is the current interpreter. `ServerAbility` and character-specific
classes remain for legacy implementations and trusted temporary FightGuy capabilities.
They are not a universal new-content authoring API. `AbilityFactory(CharacterClass, slot)`
and `MankiData` references in legacy docs must be read only in that compatibility scope.

## Unity responsibilities

Unity owns imported assets, package asset catalogs, Ability Lab, input polling, animation playback, cameras, UI, VFX, audio, and network transport. `PlayerRenderer` resolves generated semantic animation bindings and plays them through Animancer. Unity does not decide hit results, damage, timing, or match admission.

`MatchBase` mirrors the loaded arena's baked triangles into a two-sided Unity mesh on
the Ignore Raycast layer for camera queries only. `CameraObstruction` sphere-casts
from the orbit's player target or the aim pivot, pulls in immediately, and smooths
the return to the requested distance. It changes camera position, not aim rotation.
Fighters, triggers, and ordinary aim raycasts do not participate in this proxy;
Shared remains the gameplay collision authority.

`ChatSession` owns one launch-scoped identity and reusable SignalR `LobbyClient`.
The shared `ChatOverlay` attaches to each scene's existing UI document; Global and
Direct conversations survive scene changes, while Server Chat follows the authenticated
player's current Master Room. Room membership survives Matches and rematches independently
of the assigned GameHost. Physical GameHost admission grants no Server Chat access;
`ChatInputGate` suppresses human gameplay input while composing, never Shared
simulation or incoming damage.
See [launch/session ownership and chat](systems/netcode-architecture.md#launch-session-and-chat).

## Common change paths

### Package gameplay

Choose the applicable mode in [Testing and Verification](testing.md): local iteration uses the transient Editor development catalog and affected Ability Lab/Training path; accepted package work uses inspect, cook, verify, and roster refresh before persistence. Do not treat local preview as accepted package verification. See [Adding a Character](characters/adding-a-new-character.md).

### Shared mechanics

Edit `src/Shared/`, add/update a behavioral test, build Shared, and run the relevant tests. Keep the code free of Unity dependencies and engine physics.

### Server or netcode

Edit `src/Server/` or the Unity network bridge only after tracing the authoritative flow. Build the server and exercise a local match when the change affects admission, transport, reconciliation, or match lifecycle. See [Netcode Architecture](systems/netcode-architecture.md).

## Quick commands

```bash
dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true
dotnet build src/Server/ --nologo -p:SkipUnityPluginCopy=true
dotnet run --project src/Server/ -p:SkipUnityPluginCopy=true -- src/Server/server.json
```

Package commands:

```bash
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

## Related docs

- [Ability Architecture](systems/ability-architecture.md)
- [Combat Systems](systems/combat-systems.md)
- [Ability Lab](systems/ability-lab.md)
- [Animation System](systems/animation-system.md)
- [Netcode Architecture](systems/netcode-architecture.md)
- [Testing and Verification](testing.md)
- [Conventions](contributing/conventions.md)
- [Documentation map](README.md)
