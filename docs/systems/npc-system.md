# Training CPUs

Training CPUs use the same Shared simulation and admitted character packages as the
human player. `TrainingMatch` supplies synthetic `InputState` values through
`LocalSimulationBridge`; `PlayerRenderer` presents the resulting state.

## Set up a training CPU

1. Start Training with the desired player, CPU character, and baked stage.
2. Pause with Escape or Start to edit the Training panel.
3. Select Idle, Attack, Shield, or Avoid. Attack and Avoid expose Easy, Normal,
   and Hard difficulty settings.
4. Adjust CPU damage for the scenario. Use the damage and position reset controls
   to repeat it. Position reset preserves damage and CPU settings; Reset All also
   clears damage.
5. Resume and check the CPU's response in the HUD and rendered match.

Behavior and difficulty settings apply to every CPU in the session. Damage edits
apply to the selected CPU. Idle supplies neutral input. The other modes call
`HeuristicBotPolicy.Decide` with the requested `CpuBehavior`; Shield holds defense,
Attack fights, and Avoid tries to keep distance and avoid stage edges.

These modes exercise the normal input and combat path. CPU policy does not call
abilities directly or decide whether a hit succeeds.

## Runtime setup

[`TrainingMatch.OnMatchStart`](../../client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs)
loads the baked arena and builds the local Match Content Catalog. It resolves the
player and CPU entries before setup. Missing catalog content or client bindings
stop setup rather than selecting a legacy character fallback.

`SpawnNpcSlot` performs the CPU setup:

1. Reuse the optional scene-placed first `PlayerRenderer`, or create a renderer.
2. Call `MatchBase.SetupRenderer` with the admitted content entry.
3. Register the entity, definition, initial `CharacterState`, and resolved baked
   animation through `LocalSimulationBridge.RegisterEntity`.
4. Set its assigned stage spawn through `SetRespawnPosition`.

The first CPU uses entity ID 100 for the existing capture and Solo setup.
`AddNpc` assigns subsequent IDs and cycles through the stage's non-player spawns.
`DeleteSelectedNpc` removes the Shared entity and renderer but keeps at least one CPU.
These are `TrainingMatch` APIs, not a network-wide ID reservation.

Each fixed tick builds one input per CPU before `_bridge.Tick`. Each CPU owns
`BotMemory` and a random generator. The runner feeds authoritative hit outcomes back
into memory after simulation; the policy uses delayed observations.
[`BotDifficultyProfile`](../../src/Shared/AI/BotDifficultyProfile.cs) owns the named
tiers and their timing and probability values. Training seeds its generators per
session, so repeated manual sessions need not make identical choices.

## Death and respawn

Shared `ServerSimulation.CheckBlastDeaths` checks the stage's six resolved blast
planes. Training uses `NoWinMatchRule`; Solo uses `StockMatchRule` and starts the CPU
in Attack mode.

A stock-fight blast death immediately resets the fighter at its per-entity spawn
override, or at an indexed arena spawn if no override exists. Shared increments
`Deaths`, clears damage and attack runtime, and grants
`ServerSimulation.RespawnInvincibilityTicks` while the fighter remains active.
An eliminated Solo fighter stays inactive at spawn. There is no CPU-owned respawn
timer or Unity out-of-bounds damage check. See [blast zones](blast-zones.md).

## Check a change

- Confirm that the admitted CPU model, HUD damage, and stage spawn agree.
- Exercise Idle, Shield, Attack, and Avoid through normal input-driven simulation.
- Knock a CPU out and confirm its assigned respawn and damage reset.
- Reset positions during an attack or grab and check that stale effects and paired
  interactions clear.
- Treat CPU challenge and gamefeel as human acceptance, separate from automated evidence.

Training's Scene-view Gizmos draw Shared hurtboxes and the hitboxes tested on the
latest tick, including swept poses. The serialized `_showHitboxes` option also draws
runtime debug lines. See [hitbox visualization](hitbox-system.md#debug-visualization).

## Source references

- [`TrainingSettingsPanel`](../../client/Unity/Assets/Scripts/Runtime/UI/TrainingSettingsPanel.cs) binds the current controls.
- [`HeuristicBotPolicy`](../../src/Shared/AI/HeuristicBotPolicy.cs) produces CPU input.
- [`BotMemory`](../../src/Shared/AI/BotMemory.cs) stores per-CPU observations and decisions.
- [`ServerSimulation`](../../src/Shared/ServerSimulation.cs) owns combat, death, and respawn.
