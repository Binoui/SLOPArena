# Combat Systems

This document describes mechanics shared by every SLOPArena fighter. Character-specific kit data belongs in the character package; package execution is covered by [Ability Architecture](ability-architecture.md).

## Move model

Each fighter has eight button concepts, each with grounded and aerial entries:

- normals `1`, `2`, `3`, and `4`;
- specials `A`, `E`, `R`, and `F`.

That produces the canonical 14-entry grid, including six normal moves (normals `1`, `3` and `4`, ground and air) and eight special moves. [`CanonicalSlotProjection`](../../src/Shared/SlotAddress.cs) defines the entries. `A` is the signature special, `E` is recovery-capable mobility, `R` is the playmaking special, and `F` is the long-cooldown power special. A package may alias entries in authoring, but cooking expands them into explicit runtime slots. Physical labels such as `LMB` and `RMB` are input mappings, not move identity.

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

- Camera-relative movement uses one ground Run tier. Keyboard stays digital in eight directions. Controller stick 8-way snap is a persisted Controls setting, default on, applied only by the client.
- Jump and double jump use the character's movement definition. ShortHop is release-timed during the opening jump window.
- FastFall requires a fresh Down press while already descending; it sets and latches the configured downward speed until landing or interruption. Release preserves the latch; ascent/apex and locked/owned presses are discarded. Ordinary aerial attacks may coexist, but Hitstop, Hitstun, ledges and authored vertical motion prevent activation. FastFall overrides any active gravity window.
- Shield is a permanent, full-body grounded defense held with Left Shift / RT. It stops ordinary melee, projectile and explosion attacks from every direction, freezes combat facing, and enters a vulnerable 7-tick drop after release. Grab is C or controller LB (LB+RT when Settings → Controls → Right Stick is Camera).
- Automatic ledge grabs and LedgeHang are disabled for the friends demo. Fighters fall past stage edges; return with jumps, movement, and kit recovery moves.
- Each ability entry can be limited to one use per flight. Landing resets air-use counters.
- RecoveryMove is the per-character return-to-stage move. It is the only ordinary move that resets the FloatWindow mid-air.
- A RecoveryMove goes where the stick points, not where the fighter faces, like a Melee up-B reversal in 360°. The stick held on the press tick or either of the next two ticks turns the fighter and the move (`RecoveryTurn`); a later turn wins. A neutral stick keeps the current facing; the camera does not aim it. Moves that read the stick later still do so (Manki's ignition, Salamander's launch). Cursor-aimed recoveries such as Sir Bonk's Targeted Jump Slam keep their cursor.

All gameplay timing uses 60 Hz ticks. Values are authored in package data and resolved by Shared simulation.

### Ground Run

Every simulation uses the same immutable `GroundSpeedTuning` constants, including online prediction and replay playback.
The first moving tick sets speed to 60% of RunSpeed. The next 12 linear increments each add RunSpeed/30.
Full speed arrives on completed tick 13, about 217 ms at 60 Hz. Tilt magnitude is ignored.
Turns and Turnaround redirect immediately at retained speed and continue acceleration, with the same 60% floor.
Facing follows input. Release removes RunSpeed/20 along the velocity vector per tick, so full-speed stopping takes 20 ticks.
Dash overspeed retains its shipped coast, release braking, and pivot behavior until ordinary Run resumes.
Rush, its dash-dance window, and its instant tap-release stop are retired.
The Run clip plays at actual horizontal speed divided by RunSpeed, with a 0.35 playback floor.
See the 2026-10-27 amendment to [ADR-0020](../adr/0020-melee-based-movement.md) for measured distances and the tick convention.

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

### Air dodge

A fresh airborne Defense press (Left Shift / RT) starts an air dodge if the
fighter has an air dodge left and is otherwise actionable. Holding Defense
through a shield-jump does not produce a fresh press. The dodge goes in the
held stick direction, camera-relative like running and subject to the client snap setting; a neutral stick
dodges toward the current facing. Facing never changes, so a backward dodge
works like a backdash and aerials still point the same way.

Horizontal movement overrides, rather than adds to, inherited run/slide speed.
It lasts 12 ticks: the burst starts at 2.3 times the fighter's `airDodgeSpeed` and
keeps 90% of its speed each tick, so it ends a little above air-drift speed. The first 8
ticks are invulnerable and ticks 8–11 are vulnerable. The following 14 ticks are
vulnerable recovery that keeps the burst's exit velocity and loses only the
fighter's `airFriction`. Gravity and vertical momentum continue throughout.
Attacks, jump, grab, steering, Down/fast-fall and Shield cannot cancel
commitment; rejected edges do not queue. Landing ends movement and
invulnerability, stops horizontal velocity (no wavedash) and leaves at most 10
ticks of grounded recovery, about an aerial's landing lag. Ledge grabs do not
interrupt recovery while the ledge mechanic is disabled. Genuine landing and
respawn replenish the one-use resource; wall contact and hitstun do not.

The client presents it as the [print slip](vfx-particles.md#air-dodge-print-slip): paper
swooshes at the push-off and a misprinted fighter for exactly as long as it is invulnerable.

No-obstacle comparison at 60 Hz with a neutral stick, from the top of a jump.
The old universal airborne Dash used the per-character `dashSpeed` and
`dashDurationTicks`. Values are horizontal distance before collision/landing;
"actionable" is the distance when recovery ends.

| Fighter | Old dash travel m | Dodge start m/s | While invulnerable m | Burst m | Actionable m |
| --- | ---: | ---: | ---: | ---: | ---: |
| FightGuy | 6.667 | 25.3 | 2.402 | 3.026 | 4.703 |
| Manki | 6.000 | 25.3 | 2.402 | 3.026 | 4.703 |
| Wibou | 5.867 | 27.83 | 2.642 | 3.328 | 5.191 |
| Bonk | 6.667 | 25.3 | 2.402 | 3.026 | 4.703 |

The previous facing-locked tuning (11 m/s for 10 ticks, then 20 ticks frozen in
place) covered 1.83 m, less than holding the stick for the same 30 ticks, and a
dodge just before touchdown left 29 ticks of grounded recovery.

The old Universal Dash is retired. Its input bit now starts the Beeline, the universal homing dash (ADR-0042): a startup float, then a dash that turns toward the target at a limited rate. Arriving opens a short window that any action cancels, and a slot pressed during the dash starts there; an airborne arrival glides on briefly instead of stopping dead; running out of travel is a locked, punishable miss. It has a universal cooldown, one use per airtime, and starts from any state except being hit or attacking (an attack's IASA window counts as free). Shield, hitboxes and projectiles beat it. Values live in `HomingDashConfig`.
Kit-owned dash/lunge/recovery abilities retain their own timing and mobility.
The former Burst input bit is reserved and inert; Burst cannot escape
Hitstop or Hitstun, interrupt a move, or grant invulnerability.

### Contextual Down

Down (X by default) is independent of backward movement. Held Down enters Crouch on an
eligible ground tick; a fresh edge at ≥ .65× RunSpeed enters Slide. A held-Down actionable
landing enters Slide at ≥ .25× RunSpeed, after collision, ability termination and landing
lag. Locked landings bank no slide. Zero-speed landings never gain propulsion.

Slide caps live horizontal magnitude at 1.25× RunSpeed, then decays by 2× RunSpeed per
second without steering. Below .15× RunSpeed it becomes Crouch. The landing contact tick
adds neither a second integration nor slide friction. Crouch brakes at 36 units/s² and
snaps below .015; release restores ordinary control that tick. Slide preserves LastDir. Walk-off clears low posture without automatically fast-falling.

SlideJump uses ordinary jump squat, ShortHop and resources. Its takeoff cap is 1.15×
RunSpeed versus ordinary RunSpeed; caps never refill velocity or restore wall-blocked
components. Grounded mobility uses normal movement; character-specific mobility stays in kit abilities.

The roster packages opt their grounded normals into `allowSlideCarry`.
Activation switches immediately to the attack pose and caps existing Run or Slide
momentum at RunSpeed. Run starts brake to zero over `DownActionTuning.RunAttackCarryTicks`,
currently 20 ticks (3× RunSpeed per second), so a run attack slides as far as releasing
the stick. Slide starts decay by 6× RunSpeed per second. Neither origin steers, accelerates
or refills lower incoming speed. At the cap, Run stops after 20 unfrozen ticks and Slide
after 10. The replicated `AttackCarryFrom` origin (`Slide`, `Run`, `Shell`) selects the rate.
`PreserveMomentumOnStart` overrides Run braking; opted-in Slide carry retains priority.
Idle starts and chained normals do not gain carry. Carry freezes during Hitstop and clears
on completion, cancellation, hit or motion takeover. Aerial momentum and normal attack locks
remain unchanged. The replicated flag retains its historical `SlideAttackCarryActive` name.

Default max-fall/fast-fall speeds: FightGuy 14/22, Manki 13.5/21, Bonk 15/24, Wibou 14/22.
The report's historical-fall and 3× slide-friction controls do not change these defaults.

### Shell

Bashie's shell ([ADR-0045](../adr/0045-bashie-shell-state.md)) is a fighter state beside Slide,
`ActionState.Shell`, entered when a tuck move (Shell Shift, Shell Dive, Hydro Jet) completes or
touches down. "In the shell" is `Shell.IsTucked`: that state, or attacking in a tuck move. On the
ground she skates straight along her facing on her velocity, with no steering or friction; in the
air she coasts ballistically. `StateTicks` is the skate clock: it starts at a grounded hand-off or
her first touchdown, keeps running through walk-off coasts, and its expiry pops her up into Idle.
A landing adds `landingSpeedRatio` × the downward speed, so a higher dive skates faster; speeds stay
within the shell's minimum and maximum. She may act from the shell: a grounded normal pops her out
at skate speed (carry origin `Shell`: no RunSpeed cap, braking at `popOutDeceleration`, kept through
a vertical-only hop), a grounded jump takes off uncapped (`JumpTakeoff.Shell`), an air jump hops her
out, and shield, grab, air dodge and homing dash leave through their own admission. While she is in
the state an engine-owned bowling hitbox (`Hitbox.OwnerWindow`) follows her and hits each fighter
once per stay, freezing both, and she skates under the fighters she bowls (pushboxes skipped). A
head-on wall (a side contact that would keep under 60% of her forward speed, including runtime
walls and the Slopball net) or a shield pops her up bouncing back at `blockedRecoilSpeed` with no
recovery lock. Any hit pops her out (no armour) and drops her later bowling contacts in that batch.

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

`dotnet run --project tools/AerialApproachReport -p:SkipUnityPluginCopy=true -- --slide-flow --slide-deceleration 2
--fall-profile current --brace-multiplier 0.9 --assert --out artifacts/slide-flow/combat`
writes per-tick traces, aerial approaches, follow-ups and resolver target outcomes.
Use `3` for the friction control, `previous` for historical fall speeds and `1` to disable
brace in the report only. Each profile measures 140,640 movement scenarios plus recovery
ownership controls, ten-cycle resource routes and 1,152 finite response scenarios.
`status=complete` in target rows means the ground-normal follow-up ended; inspect
`aerialAccepted` and `slideStartTick` to distinguish a full aerial/slide route from
interruption or a crouched landing. These are scenario measurements, not balance proof.

### Ground grab, held movement, and forward throw

Grab uses C on keyboard (rebindable) or LB on controller; with the right stick set to
Camera, LB is the special modifier and grab is the LB+RT chord (ADR-0042 amendment). It takes priority
over grounded movement and snapshots facing. Incoming horizontal momentum carries
briefly, capped at run speed and braked to a stop within 6 ticks (100 ms), before
the active window; held movement cannot steer or add speed. Stationary grabs stay in place.
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

Training and the GameServer share these rules. Online `RollbackSimulationBridge`
uses `NetplaySession` to predict the full world, including both sides of grab
capture, hold, throw, and release. Authoritative World Frames restore and replay
the interaction when prediction differs. The earlier capture/release barrier
belongs to the retired three-track model; see [ADR-0039](../adr/0039-full-world-rollback.md)
and [`NetplayPredictorTests.GrabAndThrowArePredictedForTheGrabber`](../../tests/Shared.Tests/NetplayPredictorTests.cs).

Scene/Game **Gizmos** show the cooked forward box during `GrabAttempt`: yellow
startup, orange active contact, gray whiff recovery. The Edit Mode menu
**Tools → SLOPArena → Tests → Grab Animation Presentation** checks held loops,
both walk directions and roles, throw frame 11, victim landing frame 34, recorded
poses, replay/Hitstop, attack cleanup, and interruption on an isolated rig.

### Tether

A tether ([ADR-0048](../adr/0048-bashie-rescue-ring-tether.md), Bashie's Rescue Ring) is a thrown line that
delivers the first fighter it meets to the thrower's feet. It is the second kind of paired interaction. A catch writes
the same pair fields as a capture, with phase `Reeling`, and the victim enters `ActionState.Reeled`. Deaths, removal,
mode resets and damaging hits therefore end it through the grab path, and a fighter already in a pair cannot be
grabbed or caught (a ring that meets one bounces).

- **Thrower:** stays `Attacking` from the throw to the end, so clips, cooldown and interruption follow her move. The
  ring is an ordinary projectile of her activation that leaves with an interrupted move. It flies straight, aimed once
  by the auto-target projectile aim. Each tick she checks its next travel against the stage and stops it there.
- **Catch:** a ring contact after the shield, invincibility and mode-target rules. It supersedes the victim's move and
  any reaction in progress, deals the ring's damage with no launch, hitstop or DI, and credits the hit. Armour does not
  resist it. Two rings that meet each other's throwers in one batch clash and both come back empty. A fighter already
  hit in that batch bounces the ring.
- **Reel:** once per thrower ability tick, `ReelTether` steps the victim toward an anchor in front of her feet. Height
  and ground distance close separately, so a victim below a ledge lip rises over it, and the body mover slides it
  along walls. The victim's own movement step and the pushbox pass skip a paired `Reeled` fighter. A step that gains
  too little (a head-on wall) or the reel running out of time snaps the line and frees the victim.
- **Arrival:** the pair ends, the victim stays `Reeled` for the authored window facing her, under ordinary gravity with
  no launch or DI, then becomes Idle. She acts on the next tick. Any hit in the window gives its own reaction.
- **Miss:** range, a wall, a shield, a ball or a course target spends the ring. The empty ring has no body: its retract
  time is fixed at the miss and drawn from a `TetherRetract` presentation event, then a short recovery locks her.
- **Validator:** every pair pass checks both sides. The thrower must be `Attacking` with her tether reeling, the victim
  `Reeled`, both naming each other, and neither carrying a hit reaction. Anything else interrupts the pair: a hit on
  her drops the line with the victim free and unstunned, a hit on the victim breaks it and she reels back empty.

World Snapshots carry the tether's phase, clocks and ring position; the pair lives in the fighter states. Coverage:
[`TetherTests`](../../tests/Shared.Tests/TetherTests.cs) and the ring restores in
[`WorldSnapshotTests`](../../tests/Shared.Tests/WorldSnapshotTests.cs).

## Damage and hit response

SLOPArena uses damage percent rather than a conventional health pool. A hit applies damage, then Knockback using the hit's parameters and the victim's current percent. Shared retains Light, Medium, Launcher, Kill, Spike, Custom, and Adaptive profiles in [`KnockbackProfile`](../../src/Shared/KnockbackProfile.cs). Cooked package hitboxes project their explicit angle/base/growth values through Custom; Adaptive resolves its angle at contact.

- **Hitstun** is the victim's no-action duration after a damaging hit. Inputs buffer according to the simulation rules; residual launch continues after Hitstun ends.
- **Hitstop** freezes the attacker/victim pair briefly on contact while the match clock continues. The attacker's move pauses, and so do its live melee hitboxes: they keep their remaining active ticks for after the freeze. Projectiles keep flying. Block hitstop does not enter damaging-hit launch/DI logic.
- **Block stun** locks a defender after a blocked contact for `clamp(ceil(0.6 × incoming damage) + 2, 4, 15)` ticks; overlaps retain the larger remaining duration.
- **Shield drop** lasts 7 vulnerable ticks after release; release during block stun starts the full drop only when the stun expires.
- Reciprocal active grabs resolve as a **grab clash**. Simultaneous attacks trade; the attack-Clash design in [ADR-0015](../adr/0015-timing-model-combat-pivot.md) remains planned, not implemented.
- **Armor** is an authored activation window, not invulnerability: incoming
damage/contact feedback and Hitstop remain, but ordinary launch, Hitstun and
SDI do not apply. The contact snapshots protection before Hitstop; no deferred
launch appears when the window expires. Grabs bypass armor.
- Ordinary Hitstun is derived from launch magnitude; `stunTicks` is a gate.
  An opt-in hitbox `fixedHitstunTicks` gives a bounded linking duration without
  increasing displacement. It does not bypass armor or a zero stun gate.
- A **shove** is a hit with `stunTicks` 0: the launch applies without Hitstun, and
  until the push stops the victim cannot move, jump, shield or dodge. Landing ends such a push, so a
  shove meant to slide a grounded fighter is flat (`angle` 0); any upward angle
  pops the victim up and drops it within a few centimetres. A pure push, which also
  deals no damage, has no Hitstop either, so a stream of them keeps the victim sliding
  (Bashie's Hose Down, 2026-10-22).

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

A projectile stops at the first entity it touches. Its explosion is different: since
2026-10-18 the blast hits everything inside its radius once for its whole window, so a
bomb or rocket that lands between two fighters, between course targets or next to its
own owner (Manki's rocket jump) reaches all of them. Before that change the blast also
stopped at its first contact.

A returning projectile (Sir Bonk's pot lid, a `return` block on `spawnProjectile`) does
not stop at fighters: each leg hits each fighter once, and the way back pulls them toward
its owner. It turns at the end of its outbound time, at the stage (bouncing back at half
speed) or at a shield. Its owner catches it when free, shielding, held or being hit.
The reserved `LedgeHang` state also takes the catch branch, but automatic ledge grabs
remain disabled. An owner who is busy (in a move, hitstop, blockstun or landing lag) takes its self-hit
instead: fixed hitstun and no launch. Thrown point-blank into a shield, it is home inside
the throw itself, so it bonks him. Rules and limits:
[ability architecture](ability-architecture.md).

No Unity physics query or client-only trajectory determines gameplay.

## Targeting and aiming

The client may provide camera-derived aim and target intent. The Shared simulation validates targetability and range and owns the selected target. Target lock is passive while moving: it does not steer facing outside attacks. Ordinary stages do not continuously track targets. `startupAimCorrection` is an explicit cooked timeline operation available to any slot. Normals and selected specials opt in through their package's `character.json`; read that source for current coverage and tuning. When opted in, the simulation captures one eligible target at activation, even if the operation's correction tick is later. It prefers the currently selected/locked target when valid in the acquisition envelope; otherwise it picks the nearest eligible enemy, breaking distance ties by entity ID. RetargetPressed keeps its existing nearest-valid-enemy semantics; correction never switches target after capture.

Current normal tuning uses 4 m acquisition range and full-circle acquisition (`acquisitionHalfAngleDegrees: 180`, no forward-cone restriction), at most 90° yaw at 720°/s, and pitch up to 15° at 180°/s only for air.1/air.2 except Bonk air.2; grounded normals and other aerial normals have zero pitch. Selected/locked opponents beside or behind the fighter can be acquired, but the attack still turns only within its existing startup time and 90° cap. The hip world pivot is the simulation hip-height pivot (`BoneYToWorldY(PY, 0)`), shared by model, collision and trails. Target reference is the opponent torso at `PY + 0.25 × targetCapsuleHeight`. Pitch changes rendered pose geometry, not physics capsule or trajectories.

Each operation has stage-relative `tick`, `unit: "ticks"`, exclusive `endTick`, `acquisitionRange`, `acquisitionHalfAngleDegrees`, `maxYawDegrees`, `maxPitchDegrees`, `yawDegreesPerSecond`, and `pitchDegreesPerSecond`. The range/cone acquire once; per-tick rates and total-angle caps bound yaw from initial facing and pitch from the neutral attack pose, independently of raw camera aim. `endTick` must be later than the operation tick and within its stage. The compiler requires the window to end no later than the earliest authored hitbox, projectile, forward-lunge or horizontal SetVelocity commitment; it may close earlier. Capability-owned active timing also needs an appropriate authored cutoff. No correction runs on the exclusive cutoff tick, during active contact or recovery, or after target invalidation. Hitstop freezes both correction and attack clock. There is no late acquisition, target switching or physics pull.

The opted-in specials use the same 4 m acquisition, full-circle selection and 90° yaw cap at 720°/s, with zero pitch. Manki F preserves manual facing while held, but validates the captured target throughout the hold. Release rebases its turn limit on final manual/cached facing, applies the first correction step before resuming the timeline, and can continue correcting until its tick-18 flame. FightGuy R retains immediate movement and steers only during its existing six-tick windup, committing before the first active hit at tick 7. FightGuy F and Wibou F commit before first contact; Bonk A/R commit before their tick-12 lunges. No damage, speed, duration or hit timing changes are part of this assistance. Wibou R instead uses the public charged directional dash: manual direction is locked on release, with no startup correction, target tracking or post-release steering.

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

When Settings → Controls → Right Stick is Camera (the default flicks specials, ADR-0042),
the right stick orbits on a curve: precise near the centre and up to 300°/s yaw at full
deflection (pitch at 60% of yaw), reaching top speed after a quarter second held near the
edge. **Stick Look Speed** (Settings → Controls, 10–200%) scales it, plus the stick-driven
aim camera and ground cursor; **Mouse Sensitivity** (Settings → Gameplay) scales only the
mouse. While the last input came from a controller, lock assistance uses an 8° dead zone,
200°/s and twice the response, and resumes after half the manual grace. Keyboard and
mouse keep the values above. The stick speed, controller dead zone and turn speed are
tunable on `CameraMount` under **Controller Stick**.

Ground-cursor aim (Manki's Round Bomb, Bonk's Targeted Jump Slam) points at the stage's
own collision: MatchBase mirrors the baked triangles as a camera proxy, and the cursor ray
hits that proxy (a steep face counts as the top of its block; open water or void falls back
to the y = 0 plane). The bomb's server arc always lands at foot height at the sent distance,
so the client sends `CombatMath.LobDistanceThrough` for the point under the cursor; the arc
then passes through a raised island or lower roof, within the bomb's unchanged 12 m reach.
Height costs reach: an island 2.4 m up needs a 12.5 m throw at 9 m.

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
