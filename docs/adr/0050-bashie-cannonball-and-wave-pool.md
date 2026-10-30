# ADR-0050: Bashie's Cannonball and Wave Pool: the shell splashes and rides

**Status:** Accepted for the Training prototype — 2026-10-29. The maintainer approved the kit ("this sounds very
cool. lets go for it"). The values under [Prototype defaults](#prototype-defaults) are the coordinator's; the
maintainer's Training verdict on them the same day was GOOD.
**Supersedes:** [ADR-0045](0045-bashie-shell-state.md)'s skate model, bowling window, walk-off coast and Hydro Jet
landing skate, and the stun in [the NO RUNNING! plan](../plans/2026-10-26-bashie-no-running.md).
**Extends:** ADR-0045 (`ActionState.Shell`, tuck moves, thrust) and [ADR-0048](0048-bashie-rescue-ring-tether.md)
(the Rescue Ring).

## Context

The maintainer's Training verdict on 2026-10-29: the Rescue Ring "feels nice to use"; Shell Shift "just goes in a
direction and feels useless and hard to hit"; NO RUNNING! "feels out of place"; Hydro Jet is "kinda pointless but
it's a recovery". Two R designs rested on one premise, a shell line the player aims, and every fighter already has
the homing Beeline, so approach-only specials are redundant. What lands is target-aware and pays off on contact.

The approved kit keeps Q (Rescue Ring) and E (Hydro Jet) and replaces the rest:
- **R Cannonball.** She hops up tucked and cannonballs onto where her current target stood at takeoff. In the air R
  dives at the target (today's Shell Dive). A wide splash on landing launches everyone around upward, into her shell
  normals. A miss leaves her popping up out of the splash. Hydro Jet then R is her route in from above.
- **F Wave Pool.** She blows her whistle, a wave rises behind her and rolls toward her target, carrying everyone it
  meets toward the edge. She rides it in her shell and can jump off into an aerial.

## Decision

**The shell owns two things, a splash and a ride.** The character's `shell` block becomes
`slop.ability.shell-stance.v2` with an optional `splash` (pop-up time, one wide upward hitbox, a cue) and an optional
`ride` (clock, speed, turn rate, wave window, cue). `slop.ability.shell-stance.v1` retires with a reason. The skate
model (`skateTicks`, `minSkateSpeed`, `maxSkateSpeed`, `landingSpeedRatio`) and the bowl are deleted.

**`ShellPolicy { PopOut, Tuck, Ride }`, one value per slot.** A tuck move keeps her tucked while it runs. A ride move
starts the ride when it completes. A slot cannot both tuck and ride.

**Every tucked landing splashes.** `ResolveShellTouchdown` keeps its call site. It retires the tucked move (its
cooldown starts), pins her in the splash's pop-up time and spawns the splash at her feet, resolved in that tick's hit
pass. Ground R, air R and Hydro Jet then R all land through it. A Hydro Jet that meets the ground before it ends
also splashes. When the jet ends in the air she untucks and falls normally: no coast, no landing skate.

**Ground R adds the only new capability, `slop.ability.cannonball.v1`.** It takes the target in its first ability
tick, after that tick's target processing: the selected target if eligible in lock range, else the closest eligible
enemy, else `noTargetDistance` along her facing. It captures their feet, clamps to `maxRange` and `maxLaunchSpeed`,
solves one arc under her gravity with flight time scaled from `minFlightTicks` (target at her feet) to
`maxFlightTicks`, and never updates it. The shell's splash does the landing. With no landing by the stage end she
untucks and falls with her velocity, free to jump or Hydro Jet. One `TryGetCurrentTarget` lookup serves the ring, the
dive, the cannonball and the ride.

**Air R is today's thrust dive, unchanged except for its landing**, which now splashes. Hydro Jet then R keeps the
jet's speed as today.

**Wave Pool needs no capability.** Its slot is the whistle (the approved puff and blow, shorter), a turn and an air
hang, with `shellPolicy: "ride"`. When it completes, `EnterRide` puts her into `ActionState.Shell` heading for her
current target's ground point, else her facing. During the ride her heading turns toward her current target
(`TargetEntityId`) at most `ride.turnDegreesPerSecond`; zero keeps it straight. Walls, runtime solids, the Slopball net,
the pop-out carry and the jump takeoff are the shell's, as today.

**The wave carries with a catch and a lease, not repeated hits.** The engine-owned window that follows her in the shell
becomes the wave: unblockable and without an owner freeze. A new contact goes through the hit pipeline once (small
damage once, credit, friendly fire, invincibility and Mayhem rules apply) and turns the fighter into a new
`ActionState.Swept`. Each tick `SyncShellWindows` copies her ride velocity onto every fighter in her wave's hit set and
renews a short `StateTicks` lease. When she leaves the ride by any path, the window goes, nobody renews the leases, and
carried fighters expire into Idle keeping the wave's speed. Any hit from someone else replaces Swept with that hit's
reaction. The carry skips a fighter in hitstop, counting a frozen tick spent this tick.

**The wave breaks at the lip.** A grounded ride step that would leave her support puts her back on the lip, ends the
ride and pops her up. Carried fighters past the lip keep the wave's speed off the edge.

**Subtract first.** The two hand-written target lookups move onto `TryGetCurrentTarget`, and the paired-captive state
literals move onto `ActionStates.Captive`, with no behaviour change, before Swept joins them.

## Prototype defaults

- **Cannonball after the ring:** `minFlightTicks` is set by a test so that ring arrival, R on her first free tick,
  squat and flight splash inside the ring's arrival window. Q then R is her combo.
- **Splash:** a sphere at her feet reaching about her height; each fighter once; fighters high above it are missed;
  a shield blocks it like any hit and she still pops up; one pop-up time for a hit and a miss.
- **Hit in flight:** no armour; the cannonball ends with no splash.
- **Wave:** carries shields (water ignores them); an air dodge's invincibility passes through; fighters in a ring pair,
  and the thrower reeling them, are not carried until the pair ends; a fighter already Swept is refused by a second
  wave; a head-on wall pops her up with the existing bounce and releases everyone.
- **Her vulnerability on the wave:** no armour; any hit ends the ride and releases everyone.
- **Jump-off:** keeps her speed and releases everyone at the same speed, so her aerial meets them. Pop-out normals
  start at ride speed; Q pops her out to throw; R from the ride is a Cannonball.
- **Air F:** the whistle hangs her; the ride starts as a coast along the heading and its clock starts when she lands.
- **Modes:** the Slopball ball takes one push; course targets take one hit; Mayhem carries without damage and its
  walls stop her; every reset already rewrites both states.
- **Turn rate:** a small first value, tuned so jumping over the wave still escapes it.

## Consequences

- New values: `ShellPolicy.Ride`, `ActionState.Swept` and runtime API, each the next free value when implemented
  (Manki's ADR-0049 work is also claiming state and API values). World Snapshot and World Frame layouts are unchanged;
  the cannonball's few values travel in its own capability payload. The change is listed under "Unreleased" in
  `SimulationProtocol.cs`.
- Deleted: Shell Shift's slot, the skate model and hand-offs, the bowl and its shield branch, the walk-off coast,
  Hydro Jet's coast and landing skate, NO RUNNING!'s stun hitbox and its tests, `shell-stance.v1`.
- Kept: `ActionState.Shell`, `Hitbox.OwnerWindow` and `SyncShellWindows`, tuck and `Shell.IsTucked`, `thrust.v1`,
  `AttackCarryOrigin.Shell`, `JumpTakeoff.Shell`, pop-out braking, head-on and runtime walls, the pushbox skip, and the
  tuck, plop, surf, hit, puff and blow presentations.
- Risk: a missed state list for Swept leaves a carried fighter stuck or free to act; `ActionStates.Captive` and a test
  per admission path guard it.
- Risk: the lip test may end rides early on step-down seams; ride sweeps on the stages in rotation check it.
- Risk: the wave is still a line. The target lock, the turn, its width and the carry are the bet that it lands where
  Shell Shift did not.

The design packages are local in `tmp/bashie-cannonball-wave/` (`sketch-opus/` is the base, `sketch-codex/` the
competitor: an independent wave object with a conveyor carry, rejected for its larger engine surface).
