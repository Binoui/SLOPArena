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
`aimMovement: fixed`: they stop horizontal velocity and reject movement input
until release. Vertical physics and the released ability's travel still run.
Controller left-stick input aims during these holds; the right stick also remains
available. Directional aim follows the stick direction, ground targeting moves a
virtual cursor, and camera aiming adjusts yaw/pitch. Mouse aiming is unchanged.
`mobile` remains an explicit option for abilities that allow repositioning, using
the left stick for movement and right stick for aim.

`slop.ability.charged-directional-dash.v1` is a public lifecycle requiring runtime
API `1.5.0`. It owns capped hold charge, release-locked manual direction,
continuously scaled distance, fighter-pushbox-only phasing, separate traversal
and tier-scaled sword hit histories, final-segment animation seek, and recovery.
Shields/terrain stop travel; incoming attacks retain ordinary interruption and
damage authority. Its slot uses `DirectionalDash`/`GroundVector`, one stage,
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

For package creation, follow [Adding a Character](../characters/adding-a-new-character.md). For universal mechanics, see [Combat Systems](combat-systems.md). There is no legacy character execution path.
