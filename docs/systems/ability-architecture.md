# Ability Architecture

SLOPArena abilities are authored as deterministic content and executed by the Shared simulation. Package-authored timelines compose public engine capabilities and explicitly admitted temporary built-in capabilities through one Shared lifecycle.

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

A package has fourteen canonical entries: grounded and aerial variants for `1`, `3`, `4`, `A`, `E`, `R`, and `F` (ADR-0042 retired normal `2`). Physical controls are input adapters; package identity is the canonical slot ID.

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

`slop.ability.aimed-recovery.v1` requires runtime API `1.9.0` (Bashie's retired Ring Bounce; kept as a public
capability with a test fixture).
One tick-zero lifecycle in a single recovery stage suppresses gravity and horizontal momentum during a fixed
automatic setup, then samples world-plane movement input exactly once at `startupTicks`. Neutral uses `verticalSpeed`;
stick magnitude interpolates toward `directionalVerticalSpeed` and adds up to `horizontalSpeed` along the normalized
input. Preparation rises at `preparationRiseSpeed`; no floor, target or ordinary jump is needed. Launch resumes normal
ballistic gravity, and the outer timeline ends the follow-through. Hitstop pauses the clock; generic interruption
removes the pending launch. It neither refills jumps/dodges nor grants armour or damage. The stage uses `meleeCombo`,
no aim or IASA, and only presentation operations alongside the capability. Startup is 1–120 ticks and must precede
stage end; vertical speeds are positive and at most 60 m/s, directional height cannot exceed neutral, horizontal
speed is 0–60 m/s and preparation rise 0–4 m/s. Compiler and cooked loading enforce the same contract.

An optional `hop` requires runtime API `1.10.0` and zero preparation rise. Its `startTicks`/`contactTicks` define
at least four ticks of automatic travel before the high launch. Shared fixes entry facing and writes the next
interval's velocity through normal capsule collision: forward distance is linear, with a parabolic `arcHeight`
above the entry-to-`landingHeight` chord. Contact holds position until `startupTicks`; input still aims only the
high launch. Forward/arc distances are positive and at most 3 m; landing lift is 0–1 m. The heading is snapshotted,
Hitstop freezes both phases, and interruption removes every pending phase. Omitting `hop` preserves the original
API 1.9 parameters, cooked bytes and no-hop snapshot layout.

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

`slop.ability.dash-strike.v1` is a public lifecycle requiring runtime API `1.15.0`
([ADR-0052](../adr/0052-wibou-quick-draw-and-flash-cut.md)). It captures the current target's horizontal bearing at
activation, or keeps facing without a target, and moves on the press tick. Ground and air travel stay level at one
fixed distance. Dash, Tip, Recovery and Done own the phases. The root-relative light sphere samples each travel
tick; the compiler and cooked loader reject per-tick travel greater than twice its radius. A static authored tip
volume appears only at the full endpoint and wins a same-tick overlap. Both cuts share one activation hit group.
The light cut freezes only its victim and records the first victim to face on the last recovery tick. The tip
uses ordinary owner hitstop and a semantic contact effect. Shield or terrain stops place no tip. A landing is not
a terrain stop. Its slot requires one tick-zero `MeleeCombo` stage, `None` aim, no IASA, slide carry or charge pool,
and only presentation operations alongside the lifecycle. The watchdog must cover travel, tip lifetime and recovery.
The former `slop.ability.charged-directional-dash.v1` fails closed with a retirement reason.

`slop.ability.flash-cut.v1` is Wibou's F, a public lifecycle on runtime API `1.15.0` (ADR-0052). Stance, Legs, Sheathe,
Recovery, Whiff and Done own the phases. The stance holds him still with gravity off; on its last tick he locks up to
`maxTargets` eligible enemy fighters within `lockRange` (3D, centre to centre, nearest first, ties by entity ID) and
fixes the pop tick at `lock + n × legTicks + sheatheTicks`. Each leg lasts `legTicks` and fixes its end point when it starts,
`passThroughDistance` past where its victim stood then at their feet height, so a runner escapes it; ignores fighter pushboxes, and ends early on
a wall. Its root sphere reaches only that victim (`Hitbox.OnlyTargetId`) and never freezes Wibou. An unarmoured,
unblocked contact becomes a held cut (see [combat systems](combat-systems.md#held-cut)). After the click his recovery also waits, at most 60 ticks, until every popped victim leaves hitstun. With nobody locked he plays
the whiff. The compiler and loader reject per-tick leg travel (`2 × lockRange + passThroughDistance` over `legTicks`)
greater than twice the cut radius, holds over 240 ticks, unlisted clips, undeclared cues and a watchdog shorter than
the longest path. Lock pulses, "NANI?!" (`revealLeadTicks` before the pop) and the sheathe click are capability cues;
the simulation emits each pop from its own release record, so it plays even if Wibou was hit first.

`slop.ability.charged-projectile.v1` requires runtime API `1.6.0`. It owns a
stationary capped hold, two thresholds selecting three projectile radii, and one
release-triggered shot after authored startup. The outer timeline pauses during
charge and controls firing recovery. It accepts one tick-zero capability in a
single `ChargeAttack` stage with zero IASA; competing motion, aim, hitbox and
projectile operations are rejected. Target-assisted slots resolve the opponent
at launch; manually aimed slots retain the release direction. Ability Lab edits
the typed charge and nested projectile parameters. Projectile visual packet v2
carries radius so remote visuals match authoritative collision size.
`slop.ability.charged-stream.v1` requires runtime API `1.8.0` (Bashie's retired Hose
Down, like FLUDD; FightGuy's test fixture keeps its coverage). Holding pumps it, in place, up to `maxChargeTicks`; a manually
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

`slop.ability.finger-guns.v1` requires runtime API `1.11.0`. Each press fires one
projectile at `fireTriggerTick`, with `recoveryTicks` after an ordinary shot. The
last of `shotsPerReload` shots starts `reloadAnimationId` immediately for
`reloadTicks`; pressing with an empty cylinder reloads without firing. Only a
completed reload refills it. Cancellation preserves spent rounds; respawn refills
it. `CharacterState.FingerGunsShotsSpent` persists through World Snapshot v9
(simulation protocol 19), with no passive regeneration. In air the capability
caps falling at `airMaxFallSpeed` and applies horizontal recoil opposite each shot
at `airRecoilSpeed`; grounded it stays planted. It uses the ordinary projectile
presentation path. One tick-zero `meleeCombo` lifecycle owns completion and landing
continuation, with IASA disabled and only presentation operations alongside it.
`aimMode` is `none` (launch-time auto-target) or `cameraForward3D` (press-time aim).
The reload clip must be listed after the stage's first clip in sorted order.
Bounds: fire 1–120 ticks, recovery 1–240, cylinder 1–24 rounds, reload 1–600,
both air speeds 0–30 m/s; projectile parameters use the existing projectile spec
with positive speed/radius/lifetime and no return. Package wiring is separate.

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

`slop.ability.thrust.v1` requires runtime API `1.12.0` ([ADR-0045](../adr/0045-bashie-shell-state.md)).
`slop.ability.shell-stance.v2`, `slop.ability.cannonball.v1` and `shellPolicy: ride` require runtime API `1.14.0`
([ADR-0050](../adr/0050-bashie-cannonball-and-wave-pool.md), Bashie's Cannonball, Wave Pool and Hydro Jet; rules in
[combat systems](combat-systems.md#shell)); `slop.ability.shell-stance.v1` (the skate and the bowl) is retired with
a reason. The shell stance is a character-level `shell` block, not an operation: `popOutDeceleration` (0–600 m/s²),
`blockedRecoilSpeed` (0–30), an optional `splash` (a root-relative `hitbox` with no bones and hitGroup 0,
`popUpTicks` 1–240, optional `presentationId`) and an optional `ride` (`ticks` 1–600, 0 < `speed` ≤ 60,
`turnDegreesPerSecond`, a root-relative `wave` hitbox with hitGroup 0 and no stun, optional `hitPresentationId`).
The block exists exactly when the capability is required and needs a `presentation.shell` pose, which also sets her
hurtbox in the shell. A slot opts in with `"shellPolicy": "tuck"` or `"ride"` (written only when not `popOut`, so
other packages cook to the same bytes); only specials may opt in. The splash exists exactly when some slot tucks,
the ride exactly when one special (and its air or ground variant) rides. A tuck slot leaves `allowSlideCarry` and
`preserveMomentumOnStart` off. A ride slot is one stage with IASA disabled and only presentation, startup aim
correction and gravity windows. `slop.ability.cannonball.v1` takes the landing point from her current target in its
first tick (else `noTargetDistance` along her facing), clamps it to `maxRange` (≤ 30), scales the flight from
`minFlightTicks` (≥ 2) to `maxFlightTicks` (≤ 240) with distance, solves one arc under her gravity capped at
`maxLaunchSpeed` (≤ 60) and holds it from `takeoffTick` (0–60); the shell's splash lands it. Its slot tucks, is one
tick-zero lifecycle in one `meleeCombo` stage with no aim, IASA disabled and only presentation alongside, and the
stage outlasts `takeoffTick` + `maxFlightTicks`. `slop.ability.thrust.v1` picks one direction
when its move starts (`aim`: `stick`, or `targetAhead` within `targetConeDegrees` of her
facing), pitches it from `neutralPitchDegrees` to `stickPitchDegrees` with stick magnitude,
and drives that velocity with gravity off for `thrustTicks` at `speed`, accelerating to
`maxSpeed`; `keepIncomingSpeed` starts at the faster of `speed` and her current speed. An
optional `trailHitbox` is authored along the travel (behind her is -Z) and lasts the thrust.
Its slot is one tick-zero lifecycle in one `meleeCombo` stage with no aim, alongside only
presentation and armour; IASA is allowed. A `setVelocity` may use `velocityMode: vertical`
(same API): it writes only the vertical speed and keeps horizontal carry. Compiler and cooked
loader enforce the same rules (`ShellBoundsError`, `ShellPairingError`, `RideSlotError`, `CannonballSlotError`,
`CannonballBoundsError`, `ThrustBoundsError`).

`slop.ability.tether.v1` requires runtime API `1.13.0`
([ADR-0048](../adr/0048-bashie-rescue-ring-tether.md), Bashie's Rescue Ring; rules in
[combat systems](combat-systems.md#tether)). At `throwTick` (1–120) it throws its `projectile`, the existing
projectile spec with 0 < speed ≤ 60, 0 < radius ≤ 3, `maxFlightTicks` 1–240, no gravity, no yaw offset, no `return`,
damage ≥ 0 and no knockback or stun. The ring is aimed once by the auto-target projectile aim and leaves with an
interrupted move. A caught fighter is reeled at `reelSpeed` per axis (0–60 m/s) to `anchorDistance` (0–3 m, at least
body contact) in front of her feet and held `arrivalStunTicks` (1–240) after it arrives; `maxReelTicks` (1–600) snaps
the line. A miss reels the empty ring back at `retractSpeed` (0–60 m/s), then `missRecoveryTicks` (0–240).
`airGravityScale` (0–1) scales her gravity while the move runs in the air. `reelAnimationId` and
`retractAnimationId` are phase clips listed after the stage's first clip; `throwPresentationId` and
`retractPresentationId` are optional. The retract always emits a `TetherRetract` presentation event whose placement
duration is the empty ring's time home. Its slot is one tick-zero lifecycle in one `meleeCombo` stage with no aim,
IASA disabled and only presentation alongside; it cannot tuck. Compiler and cooked loader enforce the same rules
(`TetherSlotError`, `TetherBoundsError`). Ability Lab does not edit it yet.

`slop.ability.command-grab.v1` requires runtime API `1.18.0`, and `1.20.0` for its optional `level`,
`anchorDistance`, `releaseLockTicks`, `whiffRecoveryTicks`, a missing `launch` or a capture with damage
([ADR-0057](../adr/0057-one-command-grab.md); Nyx's Selfie Grab, FightGuy's Title Match). At the press the fighter
turns to its current target and lunges `lungeTicks` (0–60) at `lungeSpeed` (0–60 m/s), stopping once that target is
within `stopDistance` (0–5 m, 0 never stops); the last travel tick takes the distance that remains. A `level` lunge
keeps its height with gravity off, and a travel tick a wall cuts below 60% whiffs the grab. From `captureTick` (1–120)
the `capture` (a root-relative sphere or capsule, radius up to 3 m, hit group 0, damage ≥ 0, no knockback or stun) is
placed afresh each tick for its `durationTicks` (1–30); per-tick travel may not exceed twice its radius. Its first
fighter contact, before the shield check and through armour, pairs both fighters (`DefenseInteractionPhase.CommandHold`,
victim `ActionState.CommandGrabbed`) unless the fighter is paired, cut-held or damaged this batch; a grab taking the
grabber on that tick clashes, two captures on each other's owners clash, and air-dodge invincibility ignores it. A
capture with damage credits it with no launch or hitstop. The victim is held `anchorDistance` (0–3 m, at least body
contact) in front through the body mover, facing the grabber, or at the grabber's grab anchor when that is absent;
gravity is off for both and the activation's own hits on it deal damage and hitstop only. At `releaseTick` (ticks
from the press, after the capture window, at most 240) the pair ends: the authored `launch` (damage 0–100, angle ±90,
stun to 240) lands as the grabber's hit, or without one the victim stays `CommandGrabbed` unpaired for
`releaseLockTicks` (1–60) so the timeline's finisher meets it. Without `whiffRecoveryTicks` the slot is one tick-zero
`meleeCombo` stage holding the release, IASA only after it, and a whiff, clash or lost pair leaves the rest of the
stage as recovery. With it (1–120) the slot has two stages: stage 0 holds only the grab and presentation, ends as the
capture closes and has IASA disabled; stage 1 is the hold stage holding the release. A miss then holds stage 0 for the
whiff recovery and ends the move, a lost pair ends it at once, and a capture with damage that meets an object (course
target, ball, Mayhem wall) plays the hold stage on it unpaired. No aim, carry, charge pool or tuck. Compiler and cooked
loader enforce the same rules (`CommandGrabContract`). `slop.ability.clinch.v1` is retired into it. Ability Lab
previews it; it does not edit it yet.

Under the same runtime API `1.17.0`, a timeline hitbox may name its own `hitPresentationId`, played at its contacts
instead of the slot's, and an `emitPresentation` may carry `condition: "sweetspotHit"`: it plays only once the
activation has confirmed a hit from a hitbox that names its own hit presentation (Rising Dragon's "#1" finger). Both
are written only when set, so packages without them cook unchanged; Arcade record fingerprints ignore them.

`slop.ability.forms.v1` requires runtime API `1.16.0`
([ADR-0051](../adr/0051-nyx-two-forms.md), Nyx). It is a character-level capability: the `alternateForm` block exists
exactly when it is required. The block is form 1, with an `id`, a `displayName`, a full `movement` block, optional
locomotion, hit and defense roles in `presentation` (an empty role plays form 0's clip) and its own fourteen canonical
`slots` with `aliases`. Body fields stay form 0's. The cook writes form 1's slots under `alternateForm` with
`form: 1`, and nothing for packages without the block, so they keep their bytes. The runtime builds one
`CharacterDefinition` per form, linked through `ForForm`. The typed `swapForm` operation (no fields, unit `ticks`)
toggles her form at its tick, at most once per move. The swap exchanges the live cooldowns of slots 1, 3, 4, A, E and R
with the other form's bank in `CharacterState.Form`; F keeps one clock. A running move keeps the definition it started
with, and its hitboxes and hit records carry its form. Compiler and cooked loader enforce the same rules
(`FormsBoundsError`). Ability Lab shows and edits form 0 only.

A `spawnProjectile` may carry a `return` block (runtime API `1.8.0`; Sir Bonk's pot
lid). The projectile needs a positive speed and no gravity. It flies out for
`outboundTicks`, or until it meets the stage (it bounces back at half speed) or a
shield (it turns back, and that shield counts as hit on the way back), then steers to
its owner's chest at `returnSpeed`, its velocity changing by at most `returnAcceleration`
m/s². Each leg
hits each fighter once; the return leg's knockback pulls toward the owner. The owner
catches it when free (idle, running, crouching, sliding or in jump squat, with no
hitstop, blockstun or landing lag), shielding, held or already being hit. The reserved
`LedgeHang` state also takes the catch branch; automatic ledge grabs remain disabled.
Otherwise it hits him for
`selfHitDamage` with `selfHitStunTicks` of fixed hitstun (1–240) and no launch; armour
takes the stun. The two outcomes emit `ProjectileCatch` and `ProjectileSelfHit`
presentation events. A slot throws at most one returning projectile, and refuses its
press while it is out, until it is caught, hits him, reaches `maxFlightTicks` or he is
KO'd; `CharacterState.ReturningProjectileSlot` mirrors that for the CPU and the HUD
(the key stays pressed). A charged projectile cannot return. Ability Lab does not edit
the block yet.


## Engine-owned mechanics

The engine owns deterministic implementation of movement, target/aim state, hitbox geometry, baked-bone resolution, projectiles, explosions, damage, Knockback, Hitstun, Hitstop, rotational DI, SDI, ASDI, Shield, air dodge, grabs, throws, cooldowns, IASA, landing lag, and air-use limits. Package data composes these capabilities; it does not execute code on the authoritative server.

Reciprocal active grabs clash; simultaneous attacks trade. Attack Clash remains planned, not implemented, under [ADR-0015](../adr/0015-timing-model-combat-pivot.md). The old Universal Dash is retired and automatic ledge grabs are disabled for the demo. Its input bit now carries the universal homing dash ([ADR-0042](../adr/0042-controller-layout-three-normals-homing-dash.md)), an engine-owned movement state with its own cooldown rather than a package slot. The Burst input bit stays reserved and inert.

`CookedTimelineAbility` is the current Shared interpreter. It advances stages,
executes operations, starts admitted public or trusted built-in capability
instances, and emits presentation events. [`InternalCapabilityRegistry`](../../src/Shared/Abilities/InternalCapabilityRegistry.cs)
constructs public engine capabilities as well as trusted `slop.internal.*` adapters.
[`CharacterPackageCompiler`](../../src/Shared/CharacterPackageCompiler.cs) owns admission by
ID, version, parameters, and cook profile; registry construction is not admission.
Unknown and retired capability IDs fail closed.

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

The generic runtime owns interruption. Hitstun, death, an accepted new ability, and simulation-owned overrides cancel active timelines/capabilities through the cancellation path. Natural completion uses the completion path. Both paths must deterministically clear or preserve the state they own; authored timelines must not rely on a cleanup branch that may never execute.

Hit, block and stage-contact hooks (`OnHitEntity`, `OnShieldBlock`, `OnHitObject`) run
only on the activation whose hitbox made the contact, matched by activation ID. A
projectile that lands after its owner has started another move does not reach the new
move's hooks. A timeline forwards its capabilities' armour (`HasArmor`) and gravity
(`GravityMultiplier`, the lowest wins); fighters move before abilities tick, so a
capability that holds its height must switch gravity off, not only zero its speed.

`ServerSimulation` owns shared exit cleanup and cooldown application. Gameplay
interruption removes activation-owned melee windows, clears armor, startup aim
correction, and slide carry, and lets launched projectiles continue. A reaction or
defense that has already taken ownership discards the old cancellation callback's
state writes. Permanent entity removal clears every owned activation's effects and
hit credits. World Snapshot restoration prepares the complete replacement before
applying it and runs no cancellation or completion callbacks.

Cooked hitboxes and semantic bone IDs pass through one runtime conversion shared
by timeline execution, capability hitboxes, and CPU reach. `CookedSlotDefinition`
is the sole move catalog. The running ability supplies its activation variant;
bridges pass that move to presentation and geometry. Clip indices and their pose
clock stay distinct from timeline stages and cancellation clocks. `AbilitySpec`
and its named-slot projection have been removed.

A presentation event contains stable match-tick/entity/operation identity. Clients resolve its semantic asset ID and deduplicate it across prediction and rollback. Presentation never feeds back into simulation.

## Trusted temporary capabilities

The compiler's trusted capability list identifies temporary `slop.internal.*` exceptions, including FightGuy's native behavior. These IDs are available only to the trusted built-in cook profile. A package cannot grant itself access, and Workshop content cannot reference them. Each exception needs an owner, reason, scope, and migration path under [ADR-0022](../adr/0022-workshop-first-content-architecture.md).

The kit named below owns its authored behavior; Shared combat owns the native
implementation. Removal requires a replacement that preserves the kit's motion,
contacts and input timing.

| Capability | Kit owner and scope | Migration path |
|---|---|---|
| `slop.internal.fightguy.rising-dragon.v1` | FightGuy E, rising recovery | Public recovery primitive after trajectory and contact parity |
| `slop.internal.fightguy.cyclone-kick.v1` | FightGuy R, rotating movement and hits | Cooked movement and hit windows after parity |
| `slop.internal.wibou.rising-slash.v1` | Wibou E, aimed rising slash | Public recovery primitive after trajectory and contact parity |
| `slop.internal.manki.round-bomb.v3` | Manki A, Bombs Away | Public contact-bomb and backward-hop operations after flight and fuse parity |
| `slop.internal.manki.jetpack-boost.v3` | Manki E, Flying ignition | Public fighter-flight state after fuel, interruption and rollback parity |
| `slop.internal.manki.bazooka.v2` | Manki R, press-to-fire homing rocket | Public projectile behavior after target capture, limited steering, cancellation and explosion parity |
| `slop.internal.manki.kamikaze.v1` | Manki F, Kamikaze | Public targeted thrust and contact explosion after finisher and self-push parity. Bashie's F uses her own cooked whistle timeline. |

Blade Flurry (Skyfall, `slop.internal.wibou.blade-flurry.v1` and `.v2`) is retired in favour of
`slop.ability.flash-cut.v1` (ADR-0052); both IDs fail closed with that reason. Wibou's package hashes
changed, so older local Arcade records remain stored but do not compare against this content.

Bazooka v2 replaces the held v1 input contract with immediate firing. Its authored
stage clock starts on press. The rocket captures the current target at its fire
trigger and stores the target id and cooked turn sine/cosine on `Hitbox`.
`SpellResolver` steers it after the ability ends or is interrupted. An invalid
target, a target death, or a nonpositive velocity-to-target dot product clears
homing permanently. The compiler accepts `homingTurnDegPerTick` in `(0, 90]` and
writes its sine and cosine into cooked content, keeping tick steering free of
runtime trigonometry. Bazooka v1 is rejected rather than silently changing its
hold semantics. The package hashes change with this contract. Explosion owner
damage is also persistent projectile data, so the 4 self-damage survives recovery.

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

Admitted `CharacterDefinition` values have init-only properties, defensively copied
collections and reference identity. Baked pose buffers and capability tiers are also
copied on construction and exposed read-only. Server and predictor can safely share
the admitted values; authoring and tests construct replacements when they change data.

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
