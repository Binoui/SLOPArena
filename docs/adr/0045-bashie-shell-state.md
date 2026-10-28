# ADR-0045: Bashie's shell, a fighter state shared by her moves

**Status:** Accepted for the Training prototype — 2026-10-28. The maintainer approved the kit and "the shell as a
fighter state" ("sounds good, i want to try it"). The values under [Prototype defaults](#prototype-defaults) are
the coordinator's and stay open to the maintainer's play verdict.
**Extends:** [ADR-0039](0039-full-world-rollback.md) (World Snapshots) and
[ADR-0042](0042-controller-layout-three-normals-homing-dash.md) (slot grid)

## Context

On 2026-10-28 the maintainer rejected Bashie's first specials. Hose Down aimed with the camera, Ring Bounce launched
only after a 44-tick setup, Shell Slide was hard to steer for one 9% hit, and NO RUNNING! froze her for its inhale.
The replacement kit is built around one condition, "in the shell":

- **R, ground: Shell Shift.** She drops into her shell and skates straight on at her run speed. A hit bowls the
  victim up and she skates on under them. When the skate runs out she pops up, with no flipped punish.
- **R, air: Shell Dive.** She dives steeply at the ground ahead, leaning toward her target if it is in front. The
  dive's speed becomes the skate's speed on landing. This is her approach, not a recovery.
- **E: Hydro Jet.** She tucks and jets off in the stick's direction with no wind-up. The jet behind her shoves.
  Pressing R mid-jet turns the jet into the dive without untucking. Landing still tucked flows into the skate.
- **In the shell**, a normal or a jump pops her out at skate speed. E and R keep her in. Q (the later Rescue Ring)
  pops her out. F's behaviour is parked; the shape must allow a later "keeps her in" rule.

The engine cannot express this today:

- Moves are activations, and nothing runs between them. `TickAbilities` retires an activation whose fighter left
  `Attacking` or `Aiming`.
- Hit hooks route by activation, and `UpdateBoneHitboxes` stops following an owner whose `AttackSlot` is 0. A
  hitbox that lives between moves has no owner.
- Run and Slide carry into an attack is capped at RunSpeed, and timeline velocity samples end carry.
- Air-started specials keep running after landing; the jet and the dive must end there.

## Decision

**"In the shell" is derived, not stored.** It is true when the fighter is in the new `ActionState.Shell`, or is
`Attacking` in a move whose slot declares `shellPolicy: "tuck"`. One predicate, `Shell.IsTucked`, answers it for
Shared, the CPU and Unity. `CharacterState` gains no field.

**`ActionState.Shell` is Sliding's sibling.** It holds the skate on the ground and the coast in the air. The fighter
may act from it. It runs no steering, friction or normal movement; grounded she skates on her velocity along
`FacingYaw`, airborne she coasts ballistically. Its clock is `StateTicks`, whose existing expiry returns her to Idle:
that is the pop-up. It ends four ways: the clock expires, a head-on wall, a shield meets the bowling hitbox, or any
accepted action or hit.

**Moves own their motion; the shell owns the time between moves.**
- Hydro Jet and Shell Dive are ordinary activations of one new public capability, `slop.ability.thrust.v1`. It picks
  one direction at start (the stick, or a target ahead) and drives that velocity, gravity off, for `thrustTicks`.
- Ground Shell Shift needs no capability. Its short tuck timeline ends and `Shell.Settle` turns her velocity into a
  skate.
- A tuck move that touches down is retired like an aerial and settles into the skate. R mid-jet is an ordinary IASA
  cancel; the dive keeps the jet's speed magnitude without adding to it.
- `Shell.Settle` keeps her horizontal speed, pushes a standing start off at `minSkateSpeed`, adds `landingSpeedRatio`
  times her downward speed at touchdown, and clamps at `maxSkateSpeed`. A higher dive skates faster.

**The bowling hitbox is engine-owned.** One resolver hitbox flagged `OwnerWindow` exists exactly while she is in
`ActionState.Shell`. An idempotent `SyncShellWindows` spawns or removes it from the state once per tick. Each stay
gets a fresh hit set, so each fighter is bowled once per stay. While skating she ignores fighter pushboxes, so she
passes under the victim. The jet's shove is the thrust activation's own trailing hitbox.

**Exits use existing paths.** A grounded pop-out normal starts at skate speed through a third carry origin, `Shell`,
with no RunSpeed cap. A grounded jump takes off through a third takeoff origin, `Shell`, with no cap. Hits, shield,
grab, air dodge and homing dash leave the state through their own admission code.

**Subtract first.** Before adding the state, the hand-written "free state" lists collapse into one
`ActionStates.AdmitsActions` predicate, and `JumpFromSlide` and `AttackCarryFromRun` become the enums
`JumpTakeoff {Standing, Slide, Shell}` and `AttackCarryOrigin {Slide, Run, Shell}`. This step changes no behaviour.

**Package contract.** A character opts in with the public capability `slop.ability.shell-stance.v1`, a character-level
`shell` block (skate clock, speeds, landing ratio, pop-out braking, blocked recoil, bowling hitbox, hit presentation)
and a `presentation.shell` pose that also sets her hurtbox between moves. `shellPolicy` defaults to `popOut` and is
written only when `tuck`, so other packages cook to identical bytes. Shell fields and thrust need runtime API
`1.12.0`. The compiler and the cooked loader enforce the same rules.

## Rules taken from the competing design

A second candidate kept the shell as an orthogonal motion component and owned every phase in the engine. It lost on
interface depth: the jet and dive would lose their own commitment and cooldown timing, and hit results would need an
owner byte in World Frames. Four of its rules are adopted:

- Shell Shift captures her speed before `ActivateAbility`'s grounded braking.
- Wall outcomes use real side-contact flags, including runtime solids and the Slopball net. Floors and slopes are not
  walls.
- A shield or a hit on Bashie invalidates any of her shell hits still waiting in the same resolver batch.
- Ground 4, Rising Shell, gets a vertical-only velocity mode so it keeps the pop-out carry while it hops.

Every reset path (respawn, course freeze, Mayhem reset, removal) clears the state and its window.

## Prototype defaults

- No armour: any hit pops her out.
- A wall or shield pops her up with a small bounce back and no recovery lock.
- A neutral-stick Hydro Jet goes straight up.
- Jump cannot cancel the jet or the dive; R, normals, shield and homing dash can at IASA.
- R's cooldown starts when the tuck hands off to the skate, E's when the jet ends or R cancels it, air R's at
  touchdown. A cooling R pressed in the shell changes nothing.
- The coast after a walk-off keeps the skate clock running and still bowls.
- Tuning values are named parameters, set by play.

## Consequences

- `SimulationProtocol` 25 → 26 and `SnapshotFormatVersion` 12 → 13 (`Hitbox.OwnerWindow`, the two enums,
  `ThrustAbility` state). World Frames and `InputState` keep their formats. Online play needs a coordinated client and
  GameServer release; protocol-25 replays and Bashie's Arcade records stop comparing.
- Bashie drops `steered-charge` (still Sir Bonk's) and `aimed-recovery`. Her Ring Bounce and Shell Slide tests move to
  fixtures or retire.
- The CPU treats Shell as able to act through `AdmitsActions`, but will play it poorly until it learns the state.
- A later F "keeps her in" needs a `stay` policy and one remembered bit; this ADR does not decide it.
- The main risk is a missed state list leaving her stuck or steerable in the shell. The `AdmitsActions` subtraction
  and a Shared test per rule are the guard.

The design packages are local in `tmp/bashie-shell/` (`sketch-opus/` is the base, `sketch-codex/` the competitor).
