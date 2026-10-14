# Arcade Phase 1: ball playground

**Status:** implemented; Shared/Server and native runtime checks passed with an
Editor reload workaround — 2026-10-14. Human playtesting remains open.

Phase 1 implements the approved stage/contact foundation and a dedicated local
test court. [The full Arcade brief](arcade.md) still defines the eventual
Slopball and FightGuy Course release. Challenge scoring, serving, records and
the main-menu entry belong to later phases.

## What is available

- Arena v6 embeds an immutable ball profile and allowed fighter package IDs with
  baked geometry. Ordinary stages continue exporting the same v5 bytes; readers
  retain v1–v5 support. Solo stage purpose is checked by PvP selection/admission.
- Shared simulates gravity, continuous wall/ceiling/floor rebounds and harmless
  body deflection. The playground rebounds from floors so contacts can be tested
  continuously. Slopball's floor-miss rule will be added with its challenge loop.
- Melee and projectiles hit the ball in the existing combat pass. Hits retain
  provenance, hit history, move direction/strength, percent and owner timing.
  Nonfighter owner callbacks preserve current kit effects, including Wibou's
  existing charge refund until its separate PvP removal.
- Normal lock-on, startup correction and current-position projectile aiming can
  select the ball. A local byte alias maps to a separate, fresh `ulong` ball
  identity; normal network protocol fields remain the same.
- A dedicated `ArcadeBallLabMatch` uses the ordinary local bridge, package-native
  renderer, InputController, orbit/aim cameras, projectile VFX and HUD. It runs one
  fighter and exposes a fresh-ball reset for experimentation.

## Play in Unity

Use the canonical project at `client/Unity` under the
[Editor coordination protocol](../contributing/unity-editor-coordination.md).

The canonical Editor still stalls during normal Play Mode domain reload, as
previously recorded in [testing](../testing.md). For this prototype, temporarily
enable Enter Play Mode options and disable **Reload Domain** before pressing
Play. The native checks used `DisableDomainReload`; they restored the original
settings afterward. A player build has not been verified for this phase.

1. Select **Tools → SlopArena → Arcade → Phase 1 → Prepare Test Court** if the
   original cosmetic prefab is missing. Preparation creates missing assets and
   bakes the current source without rewriting the normal offline/PvP scenes.
2. Select **Open FightGuy**, **Open Manki**, **Open Wibou** or **Open Bonk** in that
   menu, then press Play. Alternatively open
   `Assets/Scenes/Prototypes/ArcadeBallLab.unity` and select the fighter override
   on its match component.
3. Use normal controls. **Backspace** creates a fresh ball at zero percent while
   retaining the fighter's current position and resources. **Escape** opens the
   normal local pause menu.

The scene's top-left label identifies this as the Phase 1 ball lab. Its contact
count is development feedback; it is not Slopball Return scoring or a record.

Typed agent commands are available through the gateway:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.phase1.prepare
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.phase1.open --fighter fightguy
```

`sloparena.arcade.phase1.run` applies bounded semantic inputs to the live playground
using the existing Training sequence parser and returns actual Shared contacts.
Inspect its schema before use. Dependent open/play/run/capture/restoration commands
belong in one bounded batch; the run deliberately mutates this playground.

## Source and artifacts

| Part | Location |
| --- | --- |
| Immutable profile and serialization section | [ArcadeStageDefinition](../../src/Shared/ArcadeStageDefinition.cs) |
| Authoritative ball physics | [ArcadeBallSimulation](../../src/Shared/ArcadeBallSimulation.cs) |
| Contact, target-query and owner effects | [ServerSimulation.Arcade](../../src/Shared/ServerSimulation.Arcade.cs) and [SpellResolver](../../src/Shared/SpellResolver.cs) |
| Scene metadata authoring | [ArcadeStageAuthoring](../../client/Unity/Assets/Scripts/Runtime/World/ArcadeStageAuthoring.cs) |
| Dedicated local runner | [ArcadeBallLabMatch](../../client/Unity/Assets/Scripts/Runtime/World/ArcadeBallLabMatch.cs) |
| Original asset preparation and agent commands | [Phase 1 commands](../../client/Unity/Assets/Scripts/Editor/SlopArenaArcadePhase1Commands.cs) |
| Court source | [arcade_ball_lab.unity](../../client/Unity/Assets/Stages/arcade_ball_lab/arcade_ball_lab.unity) |
| Baked runtime stage | [arcade_ball_lab.arena](../../data/arenas/arcade_ball_lab.arena) |
| Playable scene | [ArcadeBallLab.unity](../../client/Unity/Assets/Scenes/Prototypes/ArcadeBallLab.unity) |

The cosmetic Resources prefab follows the project's existing local/ignored stage
policy and is recreated from original primitive assets by `phase1.prepare`.
No external environment asset dependency was introduced.

## Verification evidence and remaining checks

The focused Shared tests passed 17 cases. The full Shared suite passed 1,832 tests
with two existing skips. The Server build and existing 34-test suite passed; the
new Arcade admission regression passed separately. These checks used
`-p:SkipUnityPluginCopy=true` where no Editor copy was required.

The Shared plugin was refreshed under an owned bounded gateway hold, with matching
assembly hashes. Native Unity compilation completed with no errors and an empty
error console. The court bake passed; native stage inspection passed with matching
source/baked hashes, one spawn, no cosmetic colliders and no diagnostics. Its report
and edit-mode captures are under `.stage-authoring-cache/arcade_ball_lab/`.

Native Play Mode checks passed for all four fighters using the normal local
simulation, cooked kits and bounded semantic inputs:

| Fighter | Observed ball contact |
| --- | --- |
| FightGuy | Ki Shot: 6 damage/percent |
| Manki | Aerial normal 1: 3 damage/percent |
| Wibou | Normal 3: 8 damage/percent; three Shuriken contacts: 4 each |
| Bonk | Normal 3: 10 damage/percent |

These are selected contact probes, not coverage of every move. Other attempted
shots whiffed; they were not counted as hits. Each session produced a fresh
observed Game View capture and an empty runtime error console. The ball's rendered
diameter matched its Shared radius, with no presentation Collider remaining.
Fresh-ball reset changed its identity and cleared percent while preserving the
complete fighter state. The normal local pause menu froze the simulation tick and
ball state across command boundaries. An ordinary Training scene also completed
70 real Shared ticks of movement and attack presentation with its NPC present.

Evidence is under `tmp/arcade-phase1/`: `fightguy-native.json`, `roster-native.json`,
`final-native.json`, `contact-final-native.json` and the runtime PNGs. The final
pause probe compares explicit ball fields; the earlier generic JSON snapshot is
not used as ball-state evidence.

Normal domain reload still stalled before gameplay and made Pipeline unreachable;
those timed-out runs are excluded from runtime proof. The authorized restarts
recovered the Editor; the second close completed gracefully. Final restoration
returned to the clean, stopped Frontend scene, original match configuration and
original Play Mode options, with the gateway free and settled.

Human play with all four kits still needs to assess camera framing, physical
controls and ball feel. The root `TESTING-UNITY.md` contains the session checklist.
Phase 2 adds Slopball's receive/return/serve and miss loop after this foundation
is accepted.
