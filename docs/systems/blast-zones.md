# Blast zones

A blast zone is a stage boundary that causes a Shared simulation death. The current
arena contract has six axis-aligned planes, not only a bottom kill height.

## Runtime ownership

[`ArenaDefinition`](../../src/Shared/ArenaDefinition.cs) contains `KillHeight`,
`KillTop`, `KillMinX`, `KillMaxX`, `KillMinZ`, and `KillMaxZ`.
`ArenaCollision.ResolveBlastLines` produces the effective boundaries:

- `KillHeight` is the bottom plane.
- Nonzero authored top and side values take precedence.
- Unset top and side values derive from arena bounds and heightmap surfaces using
  `ArenaCollision.SideBlastMargin` and `TopBlastMargin`.
- Fixtures without meaningful bounds retain infinite side planes. Without the
  required heightmap, an unset top plane is infinite.

`ServerSimulation.IsOutsideBlastZone` compares the fighter's Shared position against
all six resolved planes. `CheckBlastDeaths` commits the death during the tick.
Unity colliders, trigger callbacks, and the cosmetic stage prefab do not decide it.
Online `NetplaySession` predicts the same Shared check and corrects from World Frames.

`ArenaRegistry.LoadFromDirectory` loads parseable `.arena` files. It has no hardcoded
arena fallback. A new stage is not a C# registry entry or a scene path in
`ArenaDefinition`.

## Change a stage's boundaries

Use the [stage authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md)
for stage source changes, bake, inspection, and human acceptance.

1. Record the intended recovery space and boundary behavior in the approved stage brief.
2. Update the stage's authoritative collision source and spawn markers through that
   workflow. Keep cosmetic dressing out of the collision source.
3. Bake with `sloparena.stage.bake` through the coordinated Editor gateway.
4. Inspect with `sloparena.stage.inspect`. Check the generated `killPlanes`, collision
   bounds, heightmap, and grounded spawns against the intended layout.
5. Play the stage and record human acceptance of recovery, launch distance, and pacing.

The baker derives side planes from geometry bounds plus `SideBlastMargin` and the
top plane from the highest rasterized surface plus `TopBlastMargin`. Without a
kill-plane marker, it derives the bottom from the lowest geometry vertex with the
offset defined in `SlopArenaStageAuthoringService.BuildFromScene`.

When the source has `AuthoringAids/KillPlane`, the baker requires its Y coordinate
to match the lowest gameplay geometry vertex. It uses that marker for `KillHeight`.
The marker is not a free override that can be moved independently of geometry.
The Shared arena fields support explicit values, but the current stage workflow
does not expose general blast-plane override controls.

The baked `data/arenas/<key>.arena` is the runtime collision artifact. Check the
derived planes after rebaking rather than editing generated values or changing
Shared defaults for one stage.

## Death and respawn

For stock fights and Training, Shared records the boundary and death context, interrupts
paired interactions, cancels the attack runtime, and increments the fighter's death
count. Respawn happens in the same tick at a per-entity override or an indexed stage
spawn. It resets damage, movement, and combat state.

An active fighter receives `ServerSimulation.RespawnInvincibilityTicks`.
A fighter eliminated by the match rule stays inactive at spawn and is excluded from
input, movement, and hurtboxes. Training's `NoWinMatchRule` keeps fighters active;
Solo and online stock fights use their configured stock rule. Course and Slopball
rules have their own progress and reset behavior.

There is no 20-second respawn or mandatory arena-center spawn above the stage.

## Check boundary changes

Keep these checks in the stage's human acceptance:

- Exercise bottom, top, and side exits. Confirm that visible recovery space agrees
  with the baked planes.
- Compare launches at low, medium, and high damage with the same move and position.
- Test edge recovery and directional influence, then compare the same launch from
  center stage.
- Confirm that surviving fighters return to their assigned grounded spawns with
  damage reset, and eliminated fighters remain inactive.

These checks judge the stage's balance. No fixed damage percentage guarantees a KO.

## Source references

- [`ArenaCollision.ResolveBlastLines`](../../src/Shared/ArenaDefinition.cs) resolves automatic planes.
- [`SlopArenaStageAuthoringService.BuildFromScene`](../../client/Unity/Assets/Scripts/Editor/SlopArenaStageAuthoringService.cs) bakes collision, planes, and spawns.
- [`ServerSimulation.CheckBlastDeaths`](../../src/Shared/ServerSimulation.cs) commits deaths and respawns.
