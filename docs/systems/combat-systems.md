# Combat Systems

This document describes mechanics shared by every SLOPArena fighter. Character-specific kit data belongs in the character package; package execution is covered by [Ability Architecture](ability-architecture.md).

## Move model

Each fighter has twelve move concepts, each with grounded and aerial entries:

- normals `1`, `2`, `3`, and `4`;
- specials `A`, `E`, `R`, and `F`.

That produces the canonical 16-entry grid. `A` is the signature special, `E` is recovery-capable mobility, `R` is the playmaking special, and `F` is the long-cooldown power special. A package may alias entries in authoring, but cooking expands them into explicit runtime slots. Physical labels such as `LMB` and `RMB` are input mappings, not move identity.

Every move is a fixed-timeline entry with authored stage timing, animation IDs, and typed deterministic operations. The Shared simulation controls activation, duration, collision, interruption, and completion.

### Startup-only auto-target projectiles

An `aimedProjectile` slot with `aimMode: none` does not enter manual aiming or
wait for release. At its authored projectile-spawn tick, Shared validates the
current selected target against the existing 20 m horizontal targeting range,
or selects the nearest eligible enemy with entity-ID tie-breaking. Direction is
computed from the actual launch offset toward the target's size-scaled torso;
without an eligible target it uses combat facing and level pitch. Authored
projectile spread is applied afterward. Flight remains ballistic, not homing.
Camera aim input cannot put these slots into an aiming stance.


## Movement and resources

- Camera-relative 8-direction movement uses one ground Run tier; there is no selectable walk/sprint split.
- Jump and double jump use the character's movement definition. ShortHop is release-timed during the opening jump window.
- FastFall requires a fresh Down press while already descending; it sets and latches the configured downward speed until landing or interruption. Release preserves the latch; ascent/apex and locked/owned presses are discarded. Ordinary aerial attacks may coexist, but Hitstop, Hitstun, ledges and authored vertical motion prevent activation. FastFall overrides any active gravity window.
- Shield is a permanent, full-body grounded defense held with Left Shift / RT. It stops ordinary melee, projectile and explosion attacks from every direction, freezes combat facing, and enters a vulnerable 7-tick drop after release. Grab is C or the controller LB+RT chord.
- Automatic ledge grabs and LedgeHang are disabled for the friends demo. Fighters fall past stage edges; return with jumps, movement, and kit recovery moves.
- Each ability entry can be limited to one use per flight. Landing resets air-use counters.
- RecoveryMove is the per-character return-to-stage move. It is the only ordinary move that resets the FloatWindow mid-air.

All gameplay timing uses 60 Hz ticks. Values are authored in package data and resolved by Shared simulation.

### Permanent shield bubble

While `Shielding`, Shared replaces the fighter's ordinary attack-contact hurtboxes
with one non-shrinking sphere centered on its capsule center. The package-owned
`shieldRadius` is authoritative: FightGuy, Wibou, and Bonk use 1.05 m, Manki
0.95 m. Ordinary melee, projectile, and
explosion hitboxes contact the sphere from every direction, without damage,
reflection, shield-poking, or a meter. The first physical surface contact
determines which fighter a one-hit projectile strikes. Block stun, hitstop,
collision-safe pushback, projectile lifetime, and stable block-event identity
are unchanged. Shared emits the actual first shield-sphere contact before
pushback; Unity positions the tangent ripple on the currently rendered bubble
surface so the effect follows presentation interpolation without moving gameplay.
Unshielded fighters use ordinary baked hurtboxes. Grab capture continues to
check those body hurtboxes, never the expanded shield sphere. The Unity guard
mesh has no collider or combat authority; `PlayerRenderer` follows the
authoritative state and scales it to the same diameter.

### Forward air dodge

A fresh airborne Defense press (Left Shift / RT) starts a facing-locked forward
air dodge if the fighter has an air dodge left and is otherwise actionable.
Holding Defense through a shield-jump does not produce a fresh press. The
first 5 ticks are invulnerable; horizontal movement lasts 10 ticks from
acceptance, so ticks 5–9 are vulnerable. The following 20 ticks are vulnerable
recovery. Movement overrides, rather than adds to, inherited run/slide speed
and stops at tick 10. Gravity and vertical momentum continue. Attacks, jump,
grab, steering, Down/fast-fall and Shield cannot cancel commitment; rejected
edges do not queue. Landing ends movement and invulnerability but retains
all remaining commitment as grounded recovery. Ledge grabs do not interrupt
recovery while the ledge mechanic is disabled. Genuine landing and respawn
replenish the one-use resource; wall contact and hitstun do not.

Initial no-obstacle comparison at 60 Hz (old universal airborne Dash used the
per-character `dashSpeed` and `dashDurationTicks`; the new independently
authored `airDodgeSpeed` runs for 10 ticks). Values are horizontal distance
before collision/landing:

| Fighter | Old peak m/s | Old travel m | Dodge peak m/s | Dodge travel m |
| --- | ---: | ---: | ---: | ---: |
| FightGuy | 20 | 6.667 | 11 | 1.833 |
| Manki | 20 | 6.000 | 11 | 1.833 |
| Wibou | 22 | 5.867 | 12.1 | 2.017 |
| Bonk | 20 | 6.667 | 11 | 1.833 |

Universal airborne Dash input no longer moves the fighter. Kit-owned
dash/lunge/recovery abilities retain their own timing and mobility.

### Contextual Down

Down (X by default) is independent of backward movement. Held Down enters Crouch on an
eligible ground tick; a fresh edge at ≥ .65× RunSpeed enters Slide. A held-Down actionable
landing enters Slide at ≥ .25× RunSpeed, after collision, ability termination and landing
lag. Locked landings bank no slide. Zero-speed landings never gain propulsion.

Slide caps live horizontal magnitude at 1.25× RunSpeed, then decays by 2× RunSpeed per
second without steering. Below .15× RunSpeed it becomes Crouch. The landing contact tick
adds neither a second integration nor slide friction. Crouch brakes at 36 units/s² and
snaps below .015; release restores ordinary control that tick. Slide freezes Rush and
preserves LastDir. Walk-off clears low posture without automatically fast-falling.

SlideJump uses ordinary jump squat, ShortHop and resources. Its takeoff cap is 1.15×
RunSpeed versus ordinary RunSpeed; caps never refill velocity or restore wall-blocked
components. Grounded mobility uses normal movement; character-specific mobility stays in kit abilities.

All four roster packages opt their grounded normals 1–4 into `allowSlideCarry`.
Activation switches immediately to the attack pose, caps existing Slide momentum at
RunSpeed and decays it by 6× RunSpeed per second. Carry freezes during Hitstop and clears
on completion, cancellation, hit or motion takeover. Normal attack locks remain intact.

Default max-fall/fast-fall speeds: FightGuy 14/22, Manki 13.5/21, Bonk 15/24, Wibou 14/22.
The report's historical-fall and 3× slide-friction controls do not change these defaults.

### Settled CrouchBrace

Settlement requires one complete unfrozen, eligible, zero-horizontal-velocity Crouch tick.
An accepted hit snapshots settlement and a valid authored/baked low pose before reaction
cleanup. Ordinary formula launch receives a .90 multiplier on its final vector, after
Hitstun calculation and before directional influence. Damage, Hitstop and Hitstun are
unchanged. Deferred launch applies the captured factor once after Hitstop; late Down does
not earn it and release does not revoke it. Replacement hits and block contact clear/replace the queue. Scripted force, direct velocity overrides and pulls are excluded.

Roster low postures use real static Humanoid poses and matching baked hurtboxes, not
smaller stage capsules. Missing legacy low poses remain upright and cannot earn brace.

### Measured flow report

`dotnet run --project tools/AerialApproachReport -- --slide-flow --slide-deceleration 2
--fall-profile current --brace-multiplier 0.9 --assert --out artifacts/slide-flow/combat`
writes per-tick traces, aerial approaches, follow-ups and resolver target outcomes.
Use `3` for the friction control, `previous` for historical fall speeds and `1` to disable
brace in the report only. Each profile measures 140,640 movement scenarios plus recovery
ownership controls, ten-cycle resource routes and 1,152 finite response scenarios.
`status=complete` in target rows means the ground-normal follow-up ended; inspect
`aerialAccepted` and `slideStartTick` to distinguish a full aerial/slide route from
interruption or a crouched landing. These are scenario measurements, not balance proof.

### Ground grab, held movement, and forward throw

Grab uses C on keyboard (rebindable) or LB+RT on controller. It takes priority
over grounded movement, brakes horizontal momentum, and snapshots facing.
`GrabAttempt` commits to 7 startup, 3 front-only active, and 36 whiff-recovery
ticks; the 46-tick total matches the 23-frame `grab.fbx` at 30 fps. Block stun
prevents admission. Cooked per-character `captureGeometry` defines the forward
volume and restraint anchors. Hurtbox overlap and stage collision determine
capture. Reciprocal active grabs clash with 10 recovery ticks; damaging contact
resolves before capture and can interrupt either side of a linked pair.

Successful capture enters `Grabbing` for the grabber and `Grabbed` for the victim.
The grabber walks forward/backward along its facing at 25% of run speed. Sideways
movement input (Q/D or the stick's horizontal axis) rotates the pair at 60°/second
instead of strafing. Camera azimuth recovers the original control axes from the
world-space movement input; camera rotation alone does not turn the pair. The
victim faces the same direction, away from the grabber, follows its restraint
anchor, and cannot move, jump, attack, or shield independently. Stage collision
limits both bodies; movement or rotation that would pull the victim through a wall is stopped.
Any attack-slot press from the grabber starts the throw, including an empty or
cooling-down slot, without activating or buffering that ability. Otherwise the
throw starts automatically after 180 simulation ticks (three seconds).

The shared animation config binds the paired clips independently of character
package catalogs. `grab_idle_a`/`grab_idle_b` play while stationary;
`Grab_Walk_F_A`/`Grab_Walk_F_B` and `Grab_Walk_B_A`/`Grab_Walk_B_B` play during
forward and backward held movement. Velocity is evaluated against captured facing,
so the victim's movement input cannot select its animation. Shared state ticks
sample each clip, including recorded previews and rollback rewinds; animation
callbacks never decide movement, capture, damage, or release.

A throw starts `throw.fbx` for the grabber and `Grab_Throw_B.fbx` for the victim
on the same clock. Both clips are authored at 30 fps. At frame 11 (22 simulation
ticks), the server releases the pair, deals 6 damage, and enters `Thrown` for
the victim. The grabber finishes its 29-frame clip in `Throwing`, retaining 36
recovery ticks after release. Horizontal launch retains ordinary percent, weight,
and match-tuning scaling with base 5 and growth 26; the vertical arc is timed for
landing at victim frame 34 (68 ticks after throw start) on level ground. The
victim completes eight more frames of grounded recovery, through frame 42.
Stage collision remains authoritative: a higher or lower landing surface starts
the frame-34 landing segment at actual ground contact. A missing or invalid
partner, damage, or death ends the interaction without an extra throw.

Training and the GameServer share these rules. Online local prediction waits for
authoritative capture and release; detached throw recovery and thrown flight can
be reconstructed from replicated fields. Protocol version 8 preserves all older
state wire values and adds `Grabbing` (20) and `Thrown` (21).

Scene/Game **Gizmos** show the cooked forward box during `GrabAttempt`: yellow
startup, orange active contact, gray whiff recovery. The Edit Mode menu
**Tools → SLOPArena → Tests → Grab Animation Presentation** checks held loops,
both walk directions and roles, throw frame 11, victim landing frame 34, recorded
poses, replay/Hitstop, attack cleanup, and interruption on an isolated rig.

## Damage and hit response

SLOPArena uses damage percent rather than a conventional health pool. A hit applies damage, then Knockback using the hit's profile and the victim's current percent. Profiles cover Light, Medium, Launcher, Kill, Spike, and explicit Custom values.

- **Hitstun** is the victim's no-action duration after a damaging hit. Inputs buffer according to the simulation rules; residual launch continues after Hitstun ends.
- **Hitstop** freezes the attacker/victim pair briefly on contact while the match clock continues. Block hitstop does not enter damaging-hit launch/DI logic.
- **Block stun** locks a defender after a blocked contact for `clamp(ceil(0.6 × incoming damage) + 2, 4, 15)` ticks; overlaps retain the larger remaining duration.
- **Shield drop** lasts 7 vulnerable ticks after release; release during block stun starts the full drop only when the stun expires.
- **Clash** resolves simultaneous Interruptible hitboxes as mutual pushback and short stun instead of an arbitrary trade.
- **Armor** is an authored activation window, not invulnerability: incoming
damage/contact feedback and Hitstop remain, but ordinary launch, Hitstun and
SDI do not apply. The contact snapshots protection before Hitstop; no deferred
launch appears when the window expires. Grabs bypass armor.
- Ordinary Hitstun is derived from launch magnitude; `stunTicks` is a gate.
  An opt-in hitbox `fixedHitstunTicks` gives a bounded linking duration without
  increasing displacement. It does not bypass armor or a zero stun gate.

Visual hit reactions, VFX, audio, and camera effects are presentation only. Damage and state transitions occur in Shared simulation.

Each `ServerSimulation` owns immutable [CombatTuning](../../src/Shared/CombatTuning.cs).
Shipped defaults are launch velocity ×0.17, hitstun coefficient 0.45, and magnitude
bonus 0. Damage-formula hitstun uses unscaled magnitude plus the bonus; fixed-force
launches omit the velocity scale and bonus. Immediate hits, launches deferred by
Hitstop, and throws all use the owning simulation's settings.

Diagnostic profiles resolve with `TuningProfiles.Get(name)` or `TryGet(name, out tuning)`;
pass the result as `combatTuning` to `ServerSimulation`, `SelfPlayMatch.Run`, or
`MoveDataReport.Program.BuildReport`. Profile lookup does not mutate global settings.
Production and predicted simulations use `CombatTuning.Default`; lab profiles are
local analysis inputs and are not negotiated by the match protocol.

## Duration locks and interruption

A Duration Lock prevents action during an authored move commitment. The engine owns interruption:

- IASA lets an authored stage accept a new ability from its configured tick onward;
- grounded normals also accept directional movement at that same pre-tick IASA boundary; movement cancels the current activation and its remaining hitboxes rather than steering an active attack;
- explicit ability input takes priority over movement cancellation; hitstop, Hitstun, block stun, and landing lag still block cancellation, and jumping retains its existing full-lock gate;
- Hitstun, death, and simulation-owned overrides cancel active content through the cancellation path;
- landing lag applies to an aerial move unless the landing tick is in its auto-cancel window;
- a cancellation never depends on an authored cleanup operation that may not execute.

`IasaTicks = 0` and `LandingLagTicks = 0` preserve the default no-early-out/no-landing-commitment behavior. Auto-cancel windows are per air stage.

During positive grounded landing lag, Unity blends the current aerial pose into idle
over the remaining replicated `LandingLagTicks / 60` seconds. Ordinary and
auto-cancelled landings do not trigger the blend. Hitstun interrupts it, and zero
landing lag returns animation ownership to locomotion immediately. This presentation
never extends the gameplay lock.

## Hitboxes and projectiles

The Shared resolver owns collision. A move can issue fixed-position, capsule, sphere, bone-attached, or projectile operations through its cooked timeline or an approved trusted capability. The resolver handles:

- facing-relative and cooked-pose bone positions;
- entity collision and owner-hit rules;
- projectile velocity, gravity, lifetime, and ground contact;
- explosion queues and lingering rehit zones.

No Unity physics query or client-only trajectory determines gameplay.

## Targeting and aiming

The client may provide camera-derived aim and target intent. The Shared simulation validates targetability and range and owns the selected target. Target lock is passive while moving: it does not steer facing outside attacks. Ordinary stages do not continuously track targets. `startupAimCorrection` is an explicit cooked timeline operation available to any slot. Source authoring enables it on all 32 normals and on Manki F, FightGuy R/F, Wibou F, and Bonk A/R, including their air variants. When opted in, the simulation captures one eligible target at activation, even if the operation's correction tick is later. It prefers the currently selected/locked target when valid in the acquisition envelope; otherwise it picks the nearest eligible enemy, breaking distance ties by entity ID. RetargetPressed keeps its existing nearest-valid-enemy semantics; correction never switches target after capture.

Current normal tuning uses 4 m acquisition range and full-circle acquisition (`acquisitionHalfAngleDegrees: 180`, no forward-cone restriction), at most 45° yaw at 360°/s, and pitch up to 15° at 180°/s only for air.1/air.2 except Bonk air.2; grounded normals and other aerial normals have zero pitch. Selected/locked opponents beside or behind the fighter can be acquired, but the attack still turns only within its existing startup time and 45° cap. The hip world pivot is the simulation hip-height pivot (`BoneYToWorldY(PY, 0)`), shared by model, collision and trails. Target reference is the opponent torso at `PY + 0.25 × targetCapsuleHeight`. Pitch changes rendered pose geometry, not physics capsule or trajectories.

Each operation has stage-relative `tick`, `unit: "ticks"`, exclusive `endTick`, `acquisitionRange`, `acquisitionHalfAngleDegrees`, `maxYawDegrees`, `maxPitchDegrees`, `yawDegreesPerSecond`, and `pitchDegreesPerSecond`. The range/cone acquire once; per-tick rates and total-angle caps bound yaw from initial facing and pitch from the neutral attack pose, independently of raw camera aim. `endTick` must be later than the operation tick and within its stage. The compiler requires the window to end no later than the earliest authored hitbox, projectile, forward-lunge or horizontal SetVelocity commitment; it may close earlier. Capability-owned active timing also needs an appropriate authored cutoff. No correction runs on the exclusive cutoff tick, during active contact or recovery, or after target invalidation. Hitstop freezes both correction and attack clock. There is no late acquisition, target switching or physics pull.

The opted-in specials use the same 4 m acquisition, full-circle selection and 45° yaw cap at 360°/s, with zero pitch. Manki F preserves manual facing while held, but validates the captured target throughout the hold. Release rebases its turn limit on final manual/cached facing, applies the first correction step before resuming the timeline, and can continue correcting until its tick-18 flame. FightGuy R retains immediate movement and steers only during its existing six-tick windup, committing before the first active hit at tick 7. FightGuy F and Wibou F commit before first contact; Bonk A/R commit before their tick-12 lunges. No damage, speed, duration or hit timing changes are part of this assistance. Wibou R instead uses the public charged directional dash: manual direction is locked on release, with no startup correction, target tracking or post-release steering.

The Gameplay target-lock setting has three modes: **Always auto-lock** (default) acquires an enemy automatically; **Never auto-lock** requires manual activation; **Auto-lock on hit** activates when the fighter deals or receives a damaging hit. An active lock keeps its target while valid within 20 m rather than switching as the camera moves. Retarget selects the nearest valid enemy within 20 m; the lock toggle explicitly disables or reenables automatic locking. If a target leaves range, automatic modes may reacquire when one returns, but explicit lock-off remains off until reenabling. Manual camera-facing snap does not disable lock.

A fresh lock toggle or retarget resolves its new candidate before invalidating the
previous target. If no eligible candidate exists, lock remains off. Shield freezes
combat facing without stopping authoritative target selection or lock acquisition.

While locked, the normal camera can gently orbit toward that authoritative target,
keeping the player as both its follow and look anchor. Horizontal assistance has a
dead zone and bounded turn speed because movement is camera-relative; pitch and
zoom stay manual. Mouse/right-stick camera input overrides assistance, which resumes
after a short grace period. Unlocked, missing-target, explicit aiming and
input-suppressed modes do not auto-orbit. Training and PvP share this behavior.
The experimental tuning and enable switch live on `CameraMount` under **Lock Camera
Assistance** in the Inspector, not in target-selection or Shared gameplay rules.

The right stick orbits on a curve: precise near the centre and up to 300°/s yaw at full
deflection (pitch at 60% of yaw), reaching top speed after a quarter second held near the
edge. **Stick Look Speed** (Settings → Controls, 10–200%) scales it, plus the stick-driven
aim camera and ground cursor; **Mouse Sensitivity** (Settings → Gameplay) scales only the
mouse. While the last input came from a controller, lock assistance uses an 8° dead zone,
200°/s and twice the response, and resumes after half the manual grace. Keyboard and
mouse keep the values above. The stick speed, controller dead zone and turn speed are
tunable on `CameraMount` under **Controller Stick**.

Aim indicators and camera movement are visual input aids. They do not bypass server validation or replace Shared simulation.

## Design rules

- Give 3D attacks enough width, height, or depth to compensate for camera perspective, while preserving readable counterplay.
- Telegraph high-damage F moves with a wind-up so Shield and movement decisions matter.
- Make aerial strength and recovery resources part of the fighter's tradeoff rather than granting every move unrestricted air use.
- Keep move behavior deterministic, bounded, and expressible through engine-owned primitives.

## Related docs

- [Ability Architecture](ability-architecture.md) — cooked timelines, typed operations, capabilities, and interruption ownership.
- [Character kit design principles](../characters/character-kit-design-principles.md) — role and counterplay guidance.
- [Adding a Character](../characters/adding-a-new-character.md) — package authoring and cooking.
- [Netcode Architecture](netcode-architecture.md) — server authority, prediction, and rollback.
- [Hitstun DI](hitstun-di.md) — detailed launch-drift design.
