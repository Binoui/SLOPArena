# Ability Architecture

SLOPArena abilities are authored as deterministic content and executed by the Shared simulation. The current architecture separates engine-owned mechanics, package-authored timelines, trusted built-in capabilities, and legacy compatibility code.

## Authority

```text
Character Package source
  package.json + character.json + CharacterAssetCatalog.asset
                         │
                         ▼
        Shared compiler + Unity asset cook
                         │
                         ▼
              immutable cooked package
                         │
                         ▼
            Match Content Catalog
                         │
                         ▼
             Shared ServerSimulation
```

The GameServer and client prediction consume the same cooked representation. Unity does not execute gameplay logic independently. `PlayerRenderer` and other presentation systems consume authoritative state and semantic presentation events only.

## Package-authored ability model

A package has sixteen canonical entries: grounded and aerial variants for `1`, `2`, `3`, `4`, `A`, `E`, `R`, and `F`. Physical controls are input adapters; package identity is the canonical slot ID.

Each slot contains a fixed, ordered timeline of stages. A stage owns duration, IASA, landing-lag/auto-cancel timing, animation IDs, and typed operations. Operations have explicit versioned contracts, units, bounds, defaults, and budget cost. Same-tick operations execute in authored order.

Supported operation categories include:

- deterministic velocity changes;
- timed gravity windows that scale airborne gravity;
- hitbox and projectile spawns;
- aim-state changes;
- starts of approved stateful capabilities;
- semantic presentation events;
- explicit timeline completion.

`gravityWindow` carries a start tick, positive duration, and normalized `gravityScale`
from 0 through 1. It scales the active airborne gravity without changing the max-fall
cap or resetting FloatWindow. FastFall overrides the window. Time pauses during Hitstop;
the most recently started overlapping window replaces the earlier one. The operation
is part of runtime API `1.2.0`.

`forwardLunge` optionally sets `stopInAttackRange: true` (omitted means false).
It requires a later same-stage `spawnHitbox`. Shared caches that first hitbox's
active pose geometry and stops horizontal lunge motion once a forward opponent's
current hurtbox intersects it. Braking is latched: movement never resumes after
the opponent leaves, and attack/armor/recovery timing and vertical velocity remain
unchanged. It neither turns toward a target nor guarantees contact during startup.
Bonk ground/air R enable this; other lunges keep their authored travel.

`armorWindow` uses ticks and a positive duration that ends within its stage.
It protects ordinary damaging contacts without reducing damage or granting
invincibility; grabs remain effective. The timer belongs to the active ability,
pauses in Hitstop, and disappears when that activation ends or is canceled.
Overlapping operations replace the remaining window.

A spawned hitbox's `durationTicks` count the same way: a melee hitbox does not age while
its owner is in Hitstop, so the freeze from one hit cannot use up the move's other live
hitboxes. Projectiles and explosions age every tick.

Hitboxes may opt into `fixedHitstunTicks` (1–240) with a nonzero `stunTicks`
gate. This changes reaction duration, not launch velocity, so linking contacts
can retain proximity without changing the global knockback formula. Zero/omitted
keeps formula-derived hitstun and canonical bytes. Packages using either feature
require runtime API `1.3.0`; older supported content remains admitted.

Authoring does not contain arbitrary branches, expressions, or transition predicates. Hold/release and other variable-duration behavior lives in bounded engine capabilities. Ground/air aliases are expanded by the compiler and do not exist as runtime dispatch rules.

Legacy stage targeting fields remain reserved in source and cooked content:
`warpRange` and `trackingStrength` must be zero, and `useTargetLock` and
`rotateTowardTarget` must be false. Both compilation and cooked loading reject
non-default values. Current target lock, startup aim correction, and authored
lunge/dash/leap movement use their own supported mechanisms.

`slop.ability.targeted-leap.v1` is a public, bounded stateful lifecycle:
policy-controlled aim/release, clamped target-relative ballistic flight, no-landing
timeout, and a landing-triggered pose seek, ordinary typed hitbox and recovery.
The package authors its movement/landing/hitbox values; Shared owns every
transition and interruption. At most one targeted leap may own a slot, its
seek plus recovery fits the authored stage, and its hitbox lifetime fits
recovery. Ability Lab edits the same typed source contract, not a separate
simulation or a Bonk-specific ability.

The roster's ground-cursor, directional, and camera-aimed holds use
`aimMovement: fixed`: they stop grounded horizontal velocity and reject movement
input until release. Airborne holds retain incoming horizontal momentum with the
fighter's normal air drag; landing restores the fixed stance. Vertical physics
and capability-owned travel still run.
Controller left-stick input aims during these holds; the right stick also remains
available. Directional aim follows the stick direction, ground targeting moves a
virtual cursor, and camera aiming adjusts yaw/pitch. Mouse aiming is unchanged.
`mobile` remains an explicit option for abilities that allow repositioning, using
the left stick for movement and right stick for aim. A mobile slot may set
`aimMovementSpeedScale` (above 0, at most 1; runtime API `1.7.0`) to walk at that fraction
of the fighter's run and air speed, as Bonk does at 35% during Blade Storm's spin. When a
timeline `setAimState` ends a mobile aim, horizontal speed stops: attacks have no friction,
so the stick's speed would otherwise carry through the rest of the move.

`slop.ability.charged-directional-dash.v1` is a public lifecycle requiring runtime
API `1.5.0`. It owns capped hold charge, release-locked manual direction,
continuously scaled distance, fighter-pushbox-only phasing, separate traversal
and tier-scaled sword hit histories, final-segment animation seek, and recovery.
Shields/terrain stop travel; terrain stops it when a tick keeps less than 60% of
its forward travel, except on the landing tick, so seams, ramps and landings do
not end it. Recovery holds its position against fighter pushboxes, so the dash
ends on its marker. Incoming attacks retain ordinary interruption and damage
authority. Its slot uses `DirectionalDash`/`GroundVector`, one stage,
tick-zero activation and zero IASA. Competing lifecycle, startup correction,
motion and external hitbox operations are rejected. Lead/seek/recovery timings
must fit the stage; both nested hitboxes use independent hit group zero.
Ability Lab edits these typed parameters and records actual hold/release frames.
Traversal keeps its one-hit-per-opponent history through Hitstop-paused travel,
then is removed at the endpoint or a shield/terrain stop. Sword lifetime remains
independent; interruption removes both activation-owned hitboxes.


`slop.ability.charged-projectile.v1` requires runtime API `1.6.0`. It owns a
stationary capped hold, two thresholds selecting three projectile radii, and one
release-triggered shot after authored startup. The outer timeline pauses during
charge and controls firing recovery. It accepts one tick-zero capability in a
single `ChargeAttack` stage with zero IASA; competing motion, aim, hitbox and
projectile operations are rejected. Target-assisted slots resolve the opponent
at launch; manually aimed slots retain the release direction. Ability Lab edits
the typed charge and nested projectile parameters. Projectile visual packet v2
carries radius so remote visuals match authoritative collision size.
`slop.ability.charged-stream.v1` requires runtime API `1.8.0` (Bashie's Hose
Down, like FLUDD). Holding pumps it, in place, up to `maxChargeTicks`; a manually
aimed slot turns the fighter to its aim while she pumps. Release sprays a stream:
one projectile every `emitIntervalTicks` from `fireTriggerTick`, along the release
aim. The pump scales the stream linearly, from a tap to a full pump:
- its length, from `minStreamTicks` to `maxStreamTicks`;
- each projectile's push, from the projectile's `baseKnockback` to `maxBaseKnockback`;
- in the air, a push back on its owner, from `minRecoilSpeed` to `maxRecoilSpeed` (m/s).

On the ground she stays put. The capability owns the end of its move: the end comes
`recoveryTicks` after the stream. `sprayAnimationId` plays from the release, looped
every `sprayLoopTicks`, and `endAnimationId` plays for the recovery. Both are phase
clips: they sort after the stage's first clip and play in real time. Its slot has one
`chargeAttack` stage with IASA zero and one lifecycle. Motion, hitbox, projectile,
aim and other capability operations are rejected. Ability Lab does not edit it yet.

`slop.ability.steered-charge.v1` requires runtime API `1.8.0` (Sir Bonk's Joust). It
gallops at a fixed speed whose heading the stick turns at a capped rate (at most
1440°/s), with a lance hitbox that follows the fighter and optional armour while it
gallops. The first fighter the lance hits ends it with the hit ending; a shield, a
terrain stop (under 60% of a tick's forward travel, as the directional dash) or the
end of the gallop ends it with the miss ending, a skid that can turn the fighter round.
With no ground under it (started in the air, or more than four ticks off a ledge) it
strides level on thin air with gravity off, hangs for the look-down and ends, so the
fighter falls and can act. It owns the end of its move. Its slot has one stage with
IASA zero and one lifecycle; motion, hitbox, projectile, aim, other capability and
completion operations are rejected, and startup aim correction, gravity and armour
windows must end by its start tick. Phase clips (gallop loop, hit, miss, air ending)
must be listed in the stage after the startup clip. Ability Lab does not edit it yet.

A `spawnProjectile` may carry a `return` block (runtime API `1.8.0`; Sir Bonk's pot
lid). The projectile needs a positive speed and no gravity. It flies out for
`outboundTicks`, or until it meets the stage (it bounces back at half speed) or a
shield (it turns back, and that shield counts as hit on the way back), then steers to
its owner's chest at `returnSpeed`, its velocity changing by at most `returnAcceleration`
m/s². Each leg
hits each fighter once; the return leg's knockback pulls toward the owner. The owner
catches it when free (idle, running, crouching, sliding or in jump squat, with no
hitstop, blockstun or landing lag), shielding, hanging on a ledge (a self-hit there would
knock him off the stage), held or already being hit. Otherwise it hits him for
`selfHitDamage` with `selfHitStunTicks` of fixed hitstun (1–240) and no launch; armour
takes the stun. The two outcomes emit `ProjectileCatch` and `ProjectileSelfHit`
presentation events. A slot throws at most one returning projectile, and refuses its
press while it is out, until it is caught, hits him, reaches `maxFlightTicks` or he is
KO'd; `CharacterState.ReturningProjectileSlot` mirrors that for the CPU and the HUD
(the key stays pressed). A charged projectile cannot return. Ability Lab does not edit
the block yet.


## Engine-owned mechanics

The engine owns deterministic implementation of movement, target/aim state, hitbox geometry, baked-bone resolution, projectiles, explosions, damage, Knockback, Hitstun, Hitstop, Clash, Burst, cooldowns, IASA, landing lag, and air-use limits. Package data composes these capabilities; it does not execute code on the authoritative server.

`CookedTimelineAbility` is the current Shared interpreter. It advances stages,
executes operations, starts admitted public or trusted built-in capability
instances, and emits presentation events. `InternalCapabilityRegistry` resolves
the exact admitted ID/version; unknown and retired capability IDs fail closed.

Slot cooldowns are authored in 60-Hz ticks and begin when the ability completes
or is cancelled, not when it starts. Ground and air variants share the input-slot
timer. At IASA, another ready slot may cancel the current move and apply its
cooldown; a same-slot recast must also respect that pending outgoing cooldown,
without cancelling the rejected move. Zero-cooldown moves remain repeatable at
IASA. Training's entity-specific `NoCooldownsEntityId` bypasses both the timer
and pending-cooldown gate for that entity only.
CPU attack/recovery selection and queued presses also exclude the active slot
while its authored cooldown is pending.

## Interruption and lifecycle ownership

The generic runtime owns interruption. Hitstun, death, Burst, a new ability, and simulation-owned overrides cancel active timelines/capabilities through the cancellation path. Natural completion uses the completion path. Both paths must deterministically clear or preserve the state they own; authored timelines must not rely on a cleanup branch that may never execute.

Hit, block and stage-contact hooks (`OnHitEntity`, `OnShieldBlock`, `OnHitObject`) run
only on the activation whose hitbox made the contact, matched by activation ID. A
projectile that lands after its owner has started another move does not reach the new
move's hooks. A timeline forwards its capabilities' armour (`HasArmor`) and gravity
(`GravityMultiplier`, the lowest wins); fighters move before abilities tick, so a
capability that holds its height must switch gravity off, not only zero its speed.

`ServerSimulation` owns shared exit cleanup and cooldown application. Gameplay
interruption removes activation-owned melee windows, clears armor, startup aim
correction, and slide carry, and lets launched projectiles continue. Authoritative state replacement
also removes the active activation's projectiles and discards cancellation callbacks'
state writes so the incoming snapshot remains exact.

Cooked hitboxes and semantic bone IDs pass through one runtime conversion shared
by timeline execution, capability hitboxes, compatibility projection, and CPU reach.
The projected `AbilitySpec` remains needed for input, presentation, and pose geometry.
Capabilities such as Wibou E/F must not execute the projected hitbox events again.

A presentation event contains stable match-tick/entity/operation identity. Clients resolve its semantic asset ID and deduplicate it across prediction and rollback. Presentation never feeds back into simulation.

## Trusted temporary capabilities

FightGuy currently uses explicitly admitted `slop.internal.fightguy.*` capability IDs for native behavior that has not yet been decomposed into public creator primitives. These IDs are available only to the trusted built-in cook profile. A package cannot grant itself access, and Workshop content cannot reference them. Each exception needs an owner, reason, scope, and migration path under [ADR-0022](../adr/0022-workshop-first-content-architecture.md).

## Shared ability lifecycle

`ServerAbility` remains a concrete Shared lifecycle seam. Cooked timelines and built-in capability adapters may implement:

- `OnStart` for activation;
- `Tick` for per-tick behavior;
- `OnEnd` for natural completion;
- `OnCancel` for interruption cleanup;
- `OnHitEntity` for hit-time effects;
- resolver, baked-data, simulation-state, arena, and presentation-event context supplied by `ServerSimulation`.

The base class is also the current superclass of `CookedTimelineAbility`. This implementation detail does not make polymorphic subclasses the universal authoring contract. Character/slot factory dispatch and the old LMB combo classes have been removed. New behavior belongs in the compiler's typed timeline/capability model unless it is an explicitly recorded trusted exception.

## Runtime flow

```text
InputState
  → ServerSimulation selects a catalog slot
  → CookedTimelineAbility executes the current stage
  → typed operation invokes Shared primitive/capability
  → SpellResolver resolves hitboxes/projectiles
  → CharacterState receives authoritative results
  → presentation events/state reach the client
```

Fresh and buffered slot presses enter `Attacking` only through `ServerSimulation`'s
pre-simulation admission and ability lifecycle. A press buffered during JumpSquat,
or made eligible by takeoff/timer expiry, is admitted on the next eligible
pre-simulation tick using the current ground/air variant and ordinary resource gates.
A fresh accepted press supersedes an older buffer. Landing-lag and defense-commitment
presses are discarded, including on their expiry tick; they do not queue an attack.

The Match Content Catalog is immutable for the match. A later cook applies only to a later match. Missing, invalid, stale, incompatible, or hash-mismatched content fails closed; it does not fall back to raw source, C# definitions, or a similar capability.

## Modifying abilities

1. Confirm the behavior is not already provided by an engine primitive.
2. If package-authored, add or adjust a typed operation/timeline field with explicit schema, units, bounds, and deterministic semantics.
3. If variable-duration or native behavior is required, extend an approved stateful capability and record compatibility/versioning.
4. Define natural completion, cancellation, hit identity, and presentation events.
5. Validate and cook the package, then test the Shared observable behavior through the catalog path.
6. Keep the activation snapshot-complete ([ADR-0039](../adr/0039-full-world-rollback.md)): every field
   that changes after construction is written in `WriteSnapshot` and read back in `ReadSnapshot`
   (override and call `base` first); hit sets shared with hitboxes go through `WriteSet`/`ReadSet`
   so the sharing survives a restore. `WorldSnapshotContractTests` fails on a missed field and
   `WorldSnapshotTests` on any behaviour a restored simulation does not reproduce.

For package creation, follow [Adding a Character](../characters/adding-a-new-character.md). For universal mechanics, see [Combat Systems](combat-systems.md). There is no legacy character execution path.
