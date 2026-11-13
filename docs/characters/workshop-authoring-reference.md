# Write a Workshop fighter kit

A kit is the gameplay source for a fighter. It has two UTF-8 JSON files in one folder.
`package.json` names the package, its version, and its rights. `character.json` defines the body, movement, and moves.
The compiler turns these files into deterministic gameplay data for a simulation running at 60 ticks per second.
A successful check validates gameplay source. It does not bind a model, animation clips, effects, sounds, or menu assets.

## Run the checker

A published checker works anywhere with the .NET 8 runtime. Keep all files from its publish directory together.
Here `kitcheck` is that directory and `my-fighter` contains the two source files.

```sh
dotnet kitcheck/KitCheck.dll my-fighter
```

This is the standalone form. It does not need a repository or Unity. The following are repository forms, run from the repository root.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- docs/characters/examples/workshop-kit
dotnet publish tools/KitCheck -p:SkipUnityPluginCopy=true -c Release -o /tmp/kitcheck
```

Success prints `COMPILE OK (0 diagnostics)`. A warning can accompany a successful compile.
Errors name a diagnostic code, the source file, and a JSON property path. Array indices start at zero.
For example, `character.json.slots[7].timeline.stages[0].animationIds[0]` identifies one animation in one move.
Exit code 1 means compilation failed. Exit code 2 means an invocation or file error.
The checker does not modify a checked kit. JSON comments, trailing commas, duplicate keys, and unknown fields are rejected.

## Make a fighter

1. Copy the [first example's character](examples/workshop-kit/character.json) and [manifest](examples/workshop-kit/package.json) into your source folder.
2. Change `packageId`, `displayName`, `creator`, `license`, and `attribution` to describe your work.
3. Adjust the movement and body values. Starter movement is not a recommendation for roster balance.
4. Change one move, then check the complete folder. Fix diagnostics before adding another move.
5. For a command grab, use the [grappler character](examples/workshop-grappler/character.json) and [grappler manifest](examples/workshop-grappler/package.json). Its F moves capture and launch. Both examples use aimed recovery for E.

The examples are complete source kits. They use placeholder lowercase animation IDs and have no portable asset bindings.
The first has hitboxes, lunges, projectiles, armor, and recovery. The second adds a public command grab with a release launch.
If these links are unavailable in a standalone distribution, use the supplied `example-kit` and `example-grappler` directories.

You can create the compiler's empty-move template with either command form.

```sh
dotnet kitcheck/KitCheck.dll template my-fighter local.my-fighter 'My fighter'
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- template /tmp/my-fighter local.my-fighter 'My fighter'
```

The template refuses to overwrite either source file and rejects invalid package IDs before writing.
The template compiles but has no attacks. It is a structural starting point, not a playable asset package.

## Move identity and timing

The generated grid below is the complete set of canonical move IDs. All seven ground variants and all seven air variants are required. Every entry must resolve, either explicitly or through an alias.
Normals are `1` for the jab or all-rounder, `2` for an upward move, and `3` for the big move.
Specials conventionally use `A` for a signature move, `E` for recovery, `R` for a playmaking move, and `F` for a power move.
These roles are conventions, not restrictions on which public building blocks a slot may use.

Ground or air selects the entry when the move starts. Landing does not switch an active move to its ground entry.
Aliases reuse a complete authored move. Cooking expands aliases, including their ground/air projection. Aliases must not form a cycle.
Both variants of a button share its cooldown and charge pool. Two forms keep separate cooldown banks.

A stage is a fixed timed part of a move. An operation changes state at a tick in that stage.
Tick zero is stage entry. One tick is 1/60 second. Operations at the same tick run in array order.
Hitstop freezes the move clock, its armor windows, and owner-attached melee volumes. Ordinary projectiles age on simulation ticks.

`iasaTicks` is the first stage age that permits early cancellation. Zero disables that window.
At a nonzero boundary, age equal to `iasaTicks` is already unlocked. Ordinary action admission still checks hitstop, stun, landing lag, and cooldowns.
A ready grounded normal may cancel into movement. Ready move cancellation also admits jump, dash, defense, and another move through their normal input gates.
Capability-owned holds and special interaction states can impose their own restrictions.

An air-started normal normally ends on landing. Its remaining stage recovery is discarded.
Outside auto-cancel windows, `landingLagTicks` is then the landing lock, even when it is zero.
A nonzero `autoCancelBeforeTicks` includes its boundary. A nonzero `autoCancelAfterTicks` also includes its boundary.
Zero disables that particular window. Specials continue their active aerial timeline on the ground.
Aim holds and lifecycles that own landing transitions also continue across landing.

Cooldown is applied when an activation completes or is cancelled, not on the button press.
A self-recast cannot bypass the outgoing move's pending cooldown. Ground and air share the same input-slot cooldown.

## Units, names, and coordinates

Distances are world meters. Speeds are meters per second. Accelerations are meters per second squared.
Angles use degrees. Damage adds percent. Counts and ratios have no distance unit.
All numeric values must remain representable by the declared JSON type and pass cooking.

Package, icon, presentation, and animation identifiers use lowercase ASCII, begin with a letter, contain only letters, digits, dots, or hyphens, and have at most 64 characters.
Animation IDs also begin with `anim.`. For example, derive `anim.move.ground-a` from `ground.A`, not `anim.move.ground-A`.
Canonical special slot IDs preserve their uppercase letters. They are not animation identifiers.
Ordinary cue IDs such as `fx.dash-tip` are legal when declared. There is no universal `presentation.` prefix requirement.
Return catch/self-hit cues and steered-charge phase/carry cues specifically require that prefix.
Attachment-point IDs are underscore-prefixed lowercase names. Rig-specific hurtbox names use `rig:` followed by the exact rig name.

An unboned attack offset is relative to the fighter position at the capsule center.
Local X points right, Y up, and Z forward along facing. X/Z rotate with facing; Y stays vertical.
Bone-attached attack offsets use the sampled bone point plus the facing-rotated offset, not the bone's local rotation matrix.
`setVelocity` uses world X/Y/Z. `absolute` replaces all components, `additive` adds them, and `vertical` replaces only Y.

`forwardLunge` snapshots facing when it starts. It replaces horizontal velocity with facing times speed on each driven tick.
It preserves vertical velocity. At the end of the lunge window it sets horizontal velocity to zero.
It owns that movement rather than accelerating through ordinary run friction.
A later motion owner can override it. Terrain collision still applies.
With `stopInAttackRange`, the first later same-stage hitbox supplies reach geometry. Reaching an opponent brakes the lunge permanently for that activation.
The option does not turn toward an opponent or guarantee contact.

Zero `hitGroup` gives each hitbox an independent hit history. Equal nonzero groups share one contact per victim during an activation.
`armorWindow` prevents ordinary damaging contacts from interrupting or launching the owner while damage still applies.
It is not invincibility. Grabs and other capture interactions bypass that protection.
Its timer freezes in hitstop and clears on move completion or cancellation. A later armor window replaces the remaining window.
The `interruptible` hitbox flag is serialized, but current Shared contact simulation does not read it.
It does not grant armor, change the owner's interruption rules, or make a hitbox persistent after owner cleanup.

## Predict damage launch

For an ordinary damage hit, let `P` be the victim's percent after this hit and `D` the damage actually dealt by the hit.
Let `B` be `baseKnockback`, `G` be `knockbackGrowth`, and `W` be victim `weight`.
The unscaled launch magnitude is:

```text
M = (B + G × (P / 100 + 1) + D × 0.1) × 200 / max(0.01, W + 100)
```

Base and growth contribute launch speed before the weight divisor and match tuning.
Larger weight reduces launch strength. Larger base raises launch at every percent. Larger growth makes percent matter more.
The launch angle splits magnitude into horizontal and vertical components. Zero degrees is horizontal. Positive 90 degrees is straight up, negative 90 straight down, with only floating-point rounding residue horizontally.
A vertical throw can fall back onto its owner after the upward motion ends. Direction comes from the contact and the authored knockback direction.
Default combat tuning multiplies launch velocity by 0.17 after hitstun calculation. Other match tuning may change that multiplier.
A settled grounded crouch brace multiplies ordinary formula launch velocity by the default 0.9 after tuning. It does not reduce damage or hitstun.
It requires a grounded, settled, motionless crouching victim, with no active move, admitted crouch animation/bone data and a low-eligible ground state. Grabs, hook-supplied force and armored hits do not gain this ordinary-launch reduction.
This predicts initial velocity, not travel distance; terrain, gravity, drag, and player influence affect the trajectory.

For a victim already at 90%, a 10-damage hit gives `P = 100`.
With `B = 8`, `G = 5`, and `W = 100`, magnitude is `(8 + 5 × 2 + 1) × 1 = 19`.
Default tuning gives launch speed `19 × 0.17 = 3.23 m/s` before direction splitting.
At weight 150 the same hit has magnitude `19 × 200 / 250 = 15.2` and launch speed `2.584 m/s`.
Fractional authored damage is converted through the simulation's percent accounting; use whole damage for an exact simple estimate.

`stunTicks` is a zero/nonzero gate, not an authored duration. With a positive gate, ordinary hitstun is
`clamp(truncate(HitstunCoefficient × (M + HitstunMagnitudeBonus)), 1, 65535)` ticks.
Default coefficient is 0.45 and default magnitude bonus is zero. The magnitude-19 example produces 8 ticks.
A nonzero `fixedHitstunTicks` replaces that duration and requires a positive gate. Zero keeps the formula.

## Judge launch strength and KOs

A KO is caused by the fighter position crossing a blast line, not by reaching a launch-speed threshold.
`ServerSimulation.IsOutsideBlastZone` compares the fighter center against all six resolved lines. The boundary itself is safe; crossing it is not.
Arena source and baked `data/arenas/*.arena` define these lines. `ArenaCollision.ResolveBlastLines` supplies automatic side/top margins when a stage omits explicit lines.
The generated Training table below uses `data/arenas/training.arena`. Its values are world coordinates, not distances from the victim.
From world origin, its side distances are 45 m to the left, 41.5 m to the right and 23 m along either Z direction. Top and bottom are Y = 23 and Y = -12.
The generated roster table also shows that all seven committed fighters currently use weight 100. Weight near -100 is accepted source math, not a sensible starting balance value.

During hitstun, horizontal launch momentum is retained. Upward launch gets a short pop before flight gravity acts.
At hitstun expiry, momentum transfers to ordinary velocity and the fighter regains control. It does not vanish.
Neutral airborne drift then brakes with the flight friction shown below. Opposing input can brake harder; steering resumes under the air-movement rules.
Launch gravity continues during post-hitstun flight, unless fast fall takes over. Terrain contact, landing, recovery input and other hits can change the result.

For the trickster's proposed 16-damage kick, `B = 10`, `G = 9`, and victim weight 100 at 100% before contact give `P = 116`, `M = 31.04`, speed about 5.277 m/s and 13 ticks of ordinary hitstun.
At its authored 40-degree pitch that is about 4.042 m/s horizontal and 3.392 m/s upward. These numbers do not establish a KO from Training's center, whose nearest horizontal blast line is 23 m away.
Starting near an edge is a different case. There is no truthful universal speed that labels this a kill move.

Print ordinary single-contact estimates with either command form. The columns use victim percent before the hit, weight 100, default tuning and no crouch brace.

```sh
dotnet kitcheck/KitCheck.dll moves my-fighter
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- moves docs/characters/examples/workshop-kit
```

`moves` visits the cooked kit, including expanded aliases and alternate forms. It calls the simulation's launch and fixed-hitstun functions.
A multi-hit move, held hit, charge tier, capture or capability hook needs play verification when it replaces the ordinary contact calculation.

## Understand movement tuning

`jumpForce` and `shortHopForce` assign initial upward speed. They are not forces and are not multiplied by weight.
A jump starts at the end of the float window and uses ordinary gravity immediately.
`maxJumps` includes the first grounded jump. Air jumps assign `jumpForce × airJumpVMultiplier` upward and can add a directional horizontal boost scaled by `airSpeedMax × airJumpHMultiplier`.
Landing restores the ordinary jump and air-dodge resources.

Current ground movement uses a shared speed ramp scaled by `runSpeed`. It ignores `runAccelerationA`, `runAccelerationB`, and `rushTicks`.
Those fields remain required source data for compatibility. Changing them does not currently slow a knight's ground acceleration.
`dashSpeed`, `dashDurationTicks`, and `dashCooldownTicks` are also retained legacy data; universal air dodge uses its own engine timing and `airDodgeSpeed`.

Ordinary air steering adds `airAccelStick` and `airAccelBase`, then approaches the stick-selected `airSpeedMax` velocity with that acceleration.
Without stick input, `airFriction` brakes horizontal components linearly in m/s². Post-hitstun flight has separate engine braking.
`groundFriction` mainly handles excess-speed coasting toward run speed and selected locked movement states; ordinary stopping uses the shared run ramp.
Neither friction value is a unitless coefficient.

`floatWindowTicks` is an age threshold for low gravity after an explicit air-clock reset, such as recovery designation.
Below that threshold, gravity is `airFloatGravity`; zero gives a real gravity-free window.
Ordinary jumps and walking off a ledge set the clock to the end of the window and use normal `gravity` immediately.
Increasing the window does not make ordinary ledge walk-offs hover.
`maxFallSpeed` caps normal falling. Fast fall assigns downward `fastFallSpeed` and overrides the low-gravity window.

## Follow aimed recovery from press to landing

Use one tick-zero `startCapability` in one `meleeCombo` stage, `aimMode: none`, `iasaTicks: 0`, and `isRecoveryMove: true`.
The stage must outlast `startupTicks`. Only presentation and hitbox spawns may accompany this recovery lifecycle.

On start the recovery stops horizontal motion, assigns `preparationRiseSpeed` upward, and suppresses gravity.
It does not need a floor, a target, or a remaining jump. An optional `hop` performs a setup arc instead of preparation rise. It captures facing at entry, then re-captures it when the recovery-turn window accepts stick input during the first three input ticks. It keeps that captured yaw for the hop between those updates.
At `startupTicks`, it samples the movement input once. Neutral input launches at `verticalSpeed` with no horizontal speed.
Full input launches along that world-plane direction at `horizontalSpeed` and upward at `directionalVerticalSpeed`.
Partial input interpolates both contributions by stick magnitude. The stick chooses a direction in the horizontal X/Z plane; it does not pitch the launch up or down.

Launch resumes ordinary ballistic gravity and moves the air clock to the end of the float window.
If `riseSteerAcceleration` is nonzero, later movement input steers horizontal velocity toward the authored horizontal speed while the capability remains active.
Neutral later input keeps the current drift. The outer stage owns completion; its end does not extend a hidden recovery timer.
Hitstop pauses the lifecycle. Interruption removes the pending setup and launch.

The `isRecoveryMove` flag itself only resets the float clock when a move starts.
It does not replenish jumps or dodges or enforce a once-per-airtime recovery stock. Normal cooldown gates still apply.
Because E is a special, its aerial timeline continues on landing under the ordinary special landing rule.
Landing still restores ordinary jump and dodge resources. Validate reach and presentation during playable assembly; source checking alone cannot show them.

## Fit hurtboxes and the model

The movement capsule and vulnerable hurtboxes are different shapes. `capsuleHeight` and `capsuleRadius` control movement collision.
`hurtboxCapsules` are fallback vulnerable geometry relative to the capsule center, rotated by facing.
For a 1.7 m capsule, a point at Y = 0.65 is 0.65 m above its center, not above its feet.
These fallback capsules are not added on top of a valid bone-hurtbox set.

When admitted baked animation data and `hurtboxBoneDefs` are available, the simulation constructs vulnerable shapes from those sampled bone points instead.
A missing admitted hurtbox bone is an error, not permission to silently switch to a different body.
An end bone makes a capsule; no end bone makes a sphere. Offsets rotate with facing, and fitted hurtbox radii follow the baked body scale.
`hurtboxRadius` is the legacy sphere fallback in geometry paths without capsule data, not a multiplier for the bone capsules.

`hipHeight` is the hip pivot above the capsule bottom. The legacy bone-Y conversion is
`capsuleCenterY - capsuleHeight / 2 + hipHeight + boneLocalY`.
Baked geometry with an explicit hips-offset point follows the animation's baked hips placement instead.
`visualScale` enlarges the rendered model without enlarging the movement capsule.
Choose it by dividing desired model height by the imported model's unscaled height.
`hurtboxBoneScale` scales attack-bone sampling; fitted bone hurtboxes use the baked body scale.

`modelYOffset` shifts the visible model relative to its presentation pivot.
Fit idle frame zero with the actual clips you will use, then adjust until the soles meet the floor.
Feet-root models and hips-root animations need different offsets. Do not assume `-capsuleHeight / 2` fits both.
Recheck after changing clips or scale. `autoModelYOffset` is serialized metadata and does not currently implement automatic fitting.
`modelSoleOffset` is also serialized but not read by the current renderer; adjust `modelYOffset` for manual floor fitting.
The proof's scale 1.8 and final offset -0.03 are measurements for its rig and clips, not defaults for another fighter.

Universal grab geometry is capsule-center relative too. Its capture center is at the grabber Y plus `offsetY`.
Current universal hold placement uses the grabber-facing `victimAnchor.x/z` offset and a vertical offset of `victimAnchor.y - attackerAnchor.y`.
The current placement calculation retains but does not read `attackerAnchor.x/z`.
Command grabs instead author a Hitbox-shaped `capture` and may set their own `anchorDistance`.

## Author a command grab

The grappler example uses a single-stage F lifecycle. Its `capture` is the same complete [Hitbox schema](#schema-hitbox), not a separate undocumented shape.
The capture is root-relative, with no bone IDs, `hitGroup: 0`, `stunTicks: 0`, `fixedHitstunTicks: 0`, and zero base and growth.
It may deal capture damage, but holding owns the victim instead of ordinary launch.

On press, the grab turns toward the current target and lunges for `lungeTicks` at `lungeSpeed`.
A positive `stopDistance` can brake that approach; zero disables target-gap braking.
With `level`, gravity is suppressed during the lunge, and a blocking wall can end the grab empty.
The capture volume opens at `captureTick` for its own `durationTicks`.
It can capture through Shield and armor. Reciprocal grabs can clash.
`captureTick` and `releaseTick` are measured from activation, not from successful capture or the end of its window.
The first captured victim is held until `releaseTick`, unless an interruption or lost interaction ends the pair.
The capture object still requires `angle` because it uses Hitbox. That angle does not launch a held victim. The separate `launch.angle` controls release.
An explicit numeric `anchorDistance` holds the victim that far in front, facing the grabber. Omit the field to use universal grab placement anchors. A literal JSON `null` is rejected.

With `launch`, release deals the authored launch damage and knockback. `releaseLockTicks` must then be zero.
Without `launch`, release instead requires a positive `releaseLockTicks` so a later timeline finisher can meet the held victim.
A nonzero `whiffRecoveryTicks` selects a two-stage lunge/hold shape. A miss waits in the first stage for that recovery and never plays the empty hold stage.
For a first grab, use the example's single-stage launching shape and leave whiff recovery at zero.

## Time a charged projectile and a dash strike

A charged projectile holds while the aim button is held. Charge time is separate from the release stage length.
The hold caps at `maxChargeTicks`; reaching the cap does not automatically fire. Release resets the stage and attack clocks to zero.
`fireTriggerTick` counts from that release. The enclosing stage duration covers release startup and recovery, so fire must precede its end.
Charging selects only the projectile radius from the base, tier-2 and tier-3 radii. It does not increase damage, speed or knockback.
The charged-stream lifecycle differs: its charge controls stream duration, pushing strength and recoil. Read its linked field and lifecycle constraints.

Dash strike ignores fighter pushboxes during travel, so it can pass through an opponent. It still collides with terrain, and a Shield block ends its attack into recovery.
Use dash strike for a short travelling pass-through attack. Flash cut instead locks victims, travels authored legs with pass-through offsets, holds them, then releases a delayed pop.
Nominal dash travel is `max(1, ceil(distance / (speed / 60)))` ticks. For 4 m at 30 m/s it is 8 ticks. Collision can end travel early.
`tipPoseTick` is the 60 Hz pose sample to seek and hold when travel reaches its endpoint. It is not a delay after travel and does not have to equal the nominal travel ticks.

## Put armor on a special

`armorWindow` may accompany charged projectile and charged stream. Its window timer is independent of their held stage clock.
Thrust also permits armor companions. Steered charge permits startup armor only when its window ends by the charge operation's tick; its own `armored` parameter controls the charge phase.
Aimed recovery, dash strike, flash cut, command grab, targeted leap, tether, cannonball and finger guns reject an added armor operation under their current companion rules.
Normal timelines may use armor windows within the ordinary stage bounds. An armor operation is not an exception to a capability's companion rules.

## Not defined yet

Portable Workshop binding formats for GLB clips, shared animation IDs, effects, sounds, projectile looks, portraits, model-root import rules, and menu registration are not defined here.
The built-in Unity catalog binds clips and deterministic pose tracks today. It is not a portable Workshop binding file.
There is no Editor-free contact-frame discovery command in this checker. Author gameplay ticks explicitly, then verify them against bound motion during assembly.
A successful source compile does not prove that the animation IDs resolve or that an attack looks correct.
`presentationIds` declares cues but supplies no effect or audio assets. `displayName` supplies metadata but installs no menu entry.
The proof's `animations.json`, borrowed selector, and `slop.anim.*` conventions were temporary experiments, not supported portable formats.
See the [Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md).

<!-- BEGIN GENERATED CONTRACT -->

## Source contract

Generated from the public `CharacterSourceSchema` definitions used by the Shared source reader. Regenerate with `dotnet kitcheck/KitCheck.dll reference --write <repository-root>`.

`Required` means omission is an error. Optional fields show a writable JSON default or an instruction to omit the field. Empty text and null are different. A null omission default does not permit a literal JSON null unless the type column explicitly says so. An optional containing object can be omitted; its required children apply only when that object is present.

The range column gives storage limits, enum tokens, and shared scalar rules. A ushort stores 0 through 65535, a byte 0 through 255, and an integer -2147483648 through 2147483647. Shared scalar rules refine these storage limits at the compiler validation step. Successful cooking also requires the gameplay and lifecycle constraints described below the tables. Those coupled constraints remain in the existing compiler and are explicitly documented by hand.

### Roster and Training reference points

Generated from the committed roster, its cooked fighters, the Training arena, and simulation constants. These are reference values, not compiler balance limits.

| Built-in fighter | Weight |
| --- | --- |
| bashie | 100 |
| bonk | 100 |
| fightguy | 100 |
| manki | 100 |
| nyx | 100 |
| salamander | 100 |
| wibou | 100 |

| Training world coordinate | Blast line in meters |
| --- | --- |
| Minimum X | -45 |
| Maximum X | 41.5 |
| Minimum Z | -23 |
| Maximum Z | 23 |
| Bottom Y | -12 |
| Top Y | 23 |

Launch pop lasts 4 ticks for upward launches. Flight gravity is 14 m/s². Neutral flight braking is 6 m/s²; opposing input can raise it to 8 m/s². Settled crouch multiplies ordinary launch velocity by 0.9 without changing hitstun.

### Canonical move grid

`ground.1`, `ground.2`, `ground.3`, `ground.A`, `ground.E`, `ground.R`, `ground.F`, `air.1`, `air.2`, `air.3`, `air.A`, `air.E`, `air.R`, `air.F`.

### Public capabilities

| ID | Version | Parameter schema |
| --- | --- | --- |
| `slop.ability.aimed-recovery.v1` | `1` | [aimed-recovery.v1 parameters](#schema-cap-aimed-recovery-v1) |
| `slop.ability.charged-projectile.v1` | `1` | [charged-projectile.v1 parameters](#schema-cap-charged-projectile-v1) |
| `slop.ability.charged-stream.v1` | `1` | [charged-stream.v1 parameters](#schema-cap-charged-stream-v1) |
| `slop.ability.finger-guns.v1` | `1` | [finger-guns.v1 parameters](#schema-cap-finger-guns-v1) |
| `slop.ability.dash-strike.v1` | `1` | [dash-strike.v1 parameters](#schema-cap-dash-strike-v1) |
| `slop.ability.flash-cut.v1` | `1` | [flash-cut.v1 parameters](#schema-cap-flash-cut-v1) |
| `slop.ability.targeted-leap.v2` | `2` | [targeted-leap.v2 parameters](#schema-cap-targeted-leap-v2) |
| `slop.ability.steered-charge.v2` | `2` | [steered-charge.v2 parameters](#schema-cap-steered-charge-v2) |
| `slop.ability.thrust.v1` | `1` | [thrust.v1 parameters](#schema-cap-thrust-v1) |
| `slop.ability.tether.v1` | `1` | [tether.v1 parameters](#schema-cap-tether-v1) |
| `slop.ability.command-grab.v1` | `1` | [command-grab.v1 parameters](#schema-cap-command-grab-v1) |
| `slop.ability.cannonball.v1` | `1` | [cannonball.v1 parameters](#schema-cap-cannonball-v1) |
| `slop.ability.shell-stance.v2` | `1` | Enables the document shell block and per-move shell policy; not a startCapability operation. |
| `slop.ability.forms.v1` | `1` | Enables alternateForm and swapForm; not a startCapability operation. |

### Budgets

| Limit | Value |
| --- | --- |
| MaxSlots | 16 |
| MaxStagesPerTimeline | 8 |
| MaxOperationsPerStage | 64 |
| MaxOperationsPerTimeline | 256 |
| MaxCapabilityRequirements | 32 |

Every operation costs 1 operation entry. Additional counters are listed with each operation and capability schema below. Parameter counters add to the enclosing startCapability counters.


### Operation contracts

Each operation has a required `kind`, zero-based stage `tick`, and `unit`. Same-tick entries run in authored order.

<a id="schema-manifest"></a>

### Package manifest

Location: `package.json`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `manifestSchemaVersion` | ushort | Required | version | Exactly `1` [Relationships](#package-body-and-references). | Selects the source format version; use the supported version shown in the schema rules. | `schema.missing`, `value.out-of-range`, `schema.unsupported` |
| `packageId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Stable lowercase package identity; changing it creates a different package. | `schema.missing`, `value.out-of-range` |
| `version` | string | Required | version | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Package version in SemVer 2.0 text; dependencies pin this version. | `schema.missing`, `value.out-of-range` |
| `creator` | string | Required | text | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Names the person or team that made the package. | `schema.missing`, `value.out-of-range` |
| `license` | string | Required | text | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Names the license covering the package; does not relicense referenced assets. | `schema.missing`, `value.out-of-range` |
| `attribution` | string | Required | text | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Records required credits and the origin of the work. | `schema.missing`, `value.out-of-range` |
| `dependencies` | array of [Pinned dependency](#schema-dependency) | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Packages this package depends on; each entry pins an identity, version and hash. | `schema.missing`, `value.out-of-range` |

<a id="schema-dependency"></a>

### Pinned dependency

Location: `package.json.dependencies[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `packageId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Stable lowercase package identity; changing it creates a different package. | `schema.missing`, `value.out-of-range` |
| `version` | string | Required | version | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Package version in SemVer 2.0 text; dependencies pin this version. | `schema.missing`, `value.out-of-range` |
| `cookedHash` | string | Required | hash | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Pins the exact cooked dependency content hash. | `schema.missing`, `value.out-of-range` |

<a id="schema-character"></a>

### Fighter

Location: `character.json`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `authoringSchemaVersion` | ushort | Required | version | Exactly `4` [Relationships](#package-body-and-references). | Selects the source format version; use the supported version shown in the schema rules. | `schema.missing`, `value.out-of-range`, `schema.unsupported` |
| `displayName` | string | Required | text | Text; naming and reference rules apply [Relationships](#package-body-and-references). | Name shown for the fighter or alternate form; does not install a menu entry. | `schema.missing`, `value.out-of-range` |
| `weight` | number | Required | relative mass | Finite 32-bit number; no additional scalar limit [Relationships](#package-body-and-references). | Divides damage launch strength through 200 / max(0.01, weight + 100); larger weight reduces launch speed. | `schema.missing`, `value.out-of-range` |
| `movement` | object of [Movement](#schema-movement) | Required | object | Child fields and array budgets apply [Relationships](#package-body-and-references). | Movement tuning used by the Shared simulation; see the movement field descriptions. | `schema.missing`, `value.out-of-range` |
| `presentation` | object of [Presentation and model](#schema-presentation) | Required | object | Child fields and array budgets apply [Relationships](#package-body-and-references). | Semantic animation roles and model placement; these values do not bind asset files. | `schema.missing`, `value.out-of-range` |
| `capsuleRadius` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#package-body-and-references). | Movement capsule radius; larger values widen world collision and fighter separation. | `schema.missing`, `value.out-of-range` |
| `capsuleHeight` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#package-body-and-references). | Full movement capsule height; larger values make the collision body taller. | `schema.missing`, `value.out-of-range` |
| `hipHeight` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#package-body-and-references). | Hip pivot height above the capsule bottom; also positions baked geometry when no baked hips-offset point exists. | `schema.missing`, `value.out-of-range` |
| `hurtboxRadius` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#package-body-and-references). | Fallback sphere radius when no capsule geometry is available; larger values make the fallback easier to hit. | `schema.missing`, `value.out-of-range` |
| `shieldRadius` | number | Required | m | greater than 0, no upper gameplay limit [Relationships](#package-body-and-references). | Shield sphere radius; larger values protect a wider area and must exceed half the capsule height. | `schema.missing`, `value.out-of-range` |
| `captureGeometry` | object of [Universal grab geometry](#schema-captureGeometry) | Required | object | Child fields and array budgets apply [Relationships](#package-body-and-references). | Universal grab reach and holding anchors; command grabs have their own capture hitbox. | `schema.missing`, `value.out-of-range` |
| `hurtboxCapsules` | array of [Fallback hurtbox capsule](#schema-hurtboxCapsule) | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Fallback vulnerable capsules relative to the capsule center, rotated with facing; baked bone geometry replaces them when available. | `schema.missing`, `value.out-of-range` |
| `hurtboxBoneDefs` | array of [Bone hurtbox capsule](#schema-hurtboxBone) | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Vulnerable capsules built from sampled bone points; the asset cook must provide those points. | `schema.missing`, `value.out-of-range` |
| `attachmentBoneIds` | array | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Additional underscore-prefixed attachment point names that a pose bake may supply. | `schema.missing`, `value.out-of-range` |
| `presentationIds` | array | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Semantic effect or sound cues this package declares; declaring an ID does not supply its asset. | `schema.missing`, `value.out-of-range` |
| `capabilityRequirements` | array of [Capability requirement](#schema-capabilityRequirement) | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Versioned public engine features used by the kit; each used capability must be declared. | `schema.missing`, `value.out-of-range` |
| `slots` | array of [Move](#schema-slot) | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Explicit move definitions; aliases must resolve every canonical ground and air entry. | `schema.missing`, `value.out-of-range` |
| `aliases` | array of [Move alias](#schema-alias) | Required | list | Child fields and array budgets apply [Relationships](#package-body-and-references). | Source-only move reuse; cooking expands each alias to a complete runtime move. | `schema.missing`, `value.out-of-range` |
| `shell` | object of [Shell settings](#schema-shell) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#package-body-and-references). | Shell landing splash and wave ride settings; requires the shell-stance capability and shell animation role. | `schema.missing`, `value.out-of-range` |
| `alternateForm` | object of [Alternate form](#schema-alternateForm) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#package-body-and-references). | Second form movement, animation roles and move grid; uses the top-level body and requires forms capability. | `schema.missing`, `value.out-of-range` |

<a id="schema-movement"></a>

### Movement

Location: `character.json.movement`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `runSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Ordinary ground speed cap; larger values run faster and scale the shared start and braking ramp. | `schema.missing`, `value.out-of-range` |
| `runAccelerationA` | number | Required | reserved m/s² | Finite 32-bit number; no additional scalar limit | Serialized legacy ground acceleration coefficients; current ground locomotion ignores both and uses the shared speed ramp. | `schema.missing`, `value.out-of-range` |
| `runAccelerationB` | number | Required | reserved m/s² | Finite 32-bit number; no additional scalar limit | Serialized legacy ground acceleration coefficients; current ground locomotion ignores both and uses the shared speed ramp. | `schema.missing`, `value.out-of-range` |
| `dashSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Legacy dash tuning retained in source; universal air dodge uses airDodgeSpeed instead. | `schema.missing`, `value.out-of-range` |
| `airDodgeSpeed` | number | Required | m/s | greater than 0, no upper gameplay limit | Air-dodge base speed; larger values increase its initial 2.3-times burst and subsequent eased travel. | `schema.missing`, `value.out-of-range` |
| `airSpeedMax` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Ordinary air steering target speed; larger values allow faster horizontal drift. | `schema.missing`, `value.out-of-range` |
| `airAccelStick` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit | Added together for ordinary air steering acceleration; larger values approach the air speed target faster. | `schema.missing`, `value.out-of-range` |
| `airAccelBase` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit | Added together for ordinary air steering acceleration; larger values approach the air speed target faster. | `schema.missing`, `value.out-of-range` |
| `jumpForce` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Full-jump initial vertical speed, not a force or impulse; larger values jump higher under the same gravity. | `schema.missing`, `value.out-of-range` |
| `shortHopForce` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Short-hop initial vertical speed; larger values make the released-jump hop higher. | `schema.missing`, `value.out-of-range` |
| `airJumpVMultiplier` | number | Required | ratio | Finite 32-bit number; no additional scalar limit | Multiplies jumpForce for an air jump; larger values make air jumps rise faster. | `schema.missing`, `value.out-of-range` |
| `airJumpHMultiplier` | number | Required | ratio | Finite 32-bit number; no additional scalar limit | Multiplies airSpeedMax for the directional velocity added by an air jump; larger values add more horizontal speed. | `schema.missing`, `value.out-of-range` |
| `gravity` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit | Downward acceleration; larger values bend flight or falling downward faster. | `schema.missing`, `value.out-of-range` |
| `airFloatGravity` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit | Gravity while below floatWindowTicks; zero means no gravity in that window, not a disabled setting. | `schema.missing`, `value.out-of-range` |
| `dashDurationTicks` | ushort | Required | reserved ticks | 0 to 65535 | Retained legacy dash field; does not tune the universal air-dodge duration or cooldown. | `schema.missing`, `value.out-of-range` |
| `dashCooldownTicks` | ushort | Required | reserved ticks | 0 to 65535 | Retained legacy dash field; does not tune the universal air-dodge duration or cooldown. | `schema.missing`, `value.out-of-range` |
| `groundFriction` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit | Braking toward run speed when coasting above it and in selected locked states; larger values remove that excess speed faster. | `schema.missing`, `value.out-of-range` |
| `airFriction` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit | Ordinary air drag without stick input and in fixed airborne aim; larger values slow horizontal drift faster. | `schema.missing`, `value.out-of-range` |
| `maxFallSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Ordinary downward speed cap; larger values allow a faster fall. | `schema.missing`, `value.out-of-range` |
| `fastFallSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | Downward speed assigned during fast fall; larger values make fast fall faster. | `schema.missing`, `value.out-of-range` |
| `maxJumps` | byte | Required | count | 0 to 255 | Total jumps including the grounded jump; larger values allow more air jumps before landing. | `schema.missing`, `value.out-of-range` |
| `jumpSquatTicks` | ushort | Required | ticks | 0 to 65535 | Ground jump preparation before launch; larger values delay takeoff. | `schema.missing`, `value.out-of-range` |
| `floatWindowTicks` | ushort | Required | ticks | 0 to 65535 | Age threshold for low gravity after an explicit air-clock reset; larger values lengthen that window. Ordinary jumps and walking off a ledge skip it. | `schema.missing`, `value.out-of-range` |
| `rushTicks` | ushort | Required | reserved ticks | 0 to 65535 | Serialized legacy field ignored by current ground locomotion; changing it has no movement effect. | `schema.missing`, `value.out-of-range` |

<a id="schema-presentation"></a>

### Presentation and model

Location: `character.json.presentation`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `idle` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `run` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `dash` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `jump` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `fall` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `hitSmall` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `hitMedium` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `hitHard` | string | Required | animation identifier | Text; naming and reference rules apply | Semantic animation for this named presentation role; lowercase IDs refer to bindings supplied during asset assembly. | `schema.missing`, `value.out-of-range` |
| `landStartOffsetSeconds` | number | Required | seconds | Finite 32-bit number; no additional scalar limit | Seek offset into a landing animation; larger values start playback farther into that clip. | `schema.missing`, `value.out-of-range` |
| `modelResourcePath` | string | Required | asset key | Text; naming and reference rules apply | Built-in model lookup key; it does not define a portable Workshop model binding. | `schema.missing`, `value.out-of-range` |
| `visualScale` | number | Required | ratio | Finite 32-bit number; no additional scalar limit | Rendered model scale; larger values enlarge the visible model without changing the movement capsule. | `schema.missing`, `value.out-of-range` |
| `hurtboxBoneScale` | number | Required | ratio | Finite 32-bit number; no additional scalar limit | Scales sampled attack-bone geometry; fitted bone hurtboxes use their baked body scale rather than this attack scale. | `schema.missing`, `value.out-of-range` |
| `modelYOffset` | number | Required | m | Finite 32-bit number; no additional scalar limit | Manual vertical model shift relative to its presentation pivot; larger values raise the model. | `schema.missing`, `value.out-of-range` |
| `modelSoleOffset` | number | Required | m | Finite 32-bit number; no additional scalar limit | Serialized presentation metadata not read by current rendering; changing it does not adjust floor placement. | `schema.missing`, `value.out-of-range` |
| `autoModelYOffset` | boolean | Required | boolean | `true` or `false` | Serialized fitting hint; current rendering does not compute an automatic offset from this flag. | `schema.missing`, `value.out-of-range` |
| `tumble` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the tumble presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `crouch` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the crouch presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `slide` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the slide presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `shield` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the shield presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `grab` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the grab presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `grabbed` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the grabbed presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `throwForward` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the throwForward presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `airDodge` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the airDodge presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `shell` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the shell presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `flying` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the flying presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `mayhemLaugh` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the mayhemLaugh presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |
| `wallJump` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the wallJump presentation role; empty text omits that role. | `schema.missing`, `value.out-of-range` |

<a id="schema-formPresentation"></a>

### Alternate form animation roles

Location: `character.json.alternateForm.presentation`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `idle` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the idle role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `run` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the run role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `dash` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the dash role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `jump` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the jump role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `fall` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the fall role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `hitSmall` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the hitSmall role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `hitMedium` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the hitMedium role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `hitHard` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the hitHard role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `tumble` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the tumble role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `shield` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the shield role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `grab` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the grab role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `grabbed` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the grabbed role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `throwForward` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the throwForward role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |
| `airDodge` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic animation for the airDodge role; empty text falls back to form zero on the same rig. | `schema.missing`, `value.out-of-range` |

<a id="schema-alternateForm"></a>

### Alternate form

Location: `character.json.alternateForm`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `id` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-forms). | Identity of this move or form within its containing object; canonical move IDs preserve uppercase special letters. | `schema.missing`, `value.out-of-range` |
| `displayName` | string | Required | text | Text; naming and reference rules apply [Relationships](#constraint-forms). | Name shown for the fighter or alternate form; does not install a menu entry. | `schema.missing`, `value.out-of-range` |
| `movement` | object of [Movement](#schema-movement) | Required | object | Child fields and array budgets apply [Relationships](#constraint-forms). | Movement tuning used by the Shared simulation; see the movement field descriptions. | `schema.missing`, `value.out-of-range` |
| `presentation` | object of [Alternate form animation roles](#schema-formPresentation) | Required | object | Child fields and array budgets apply [Relationships](#constraint-forms). | Semantic animation roles and model placement; these values do not bind asset files. | `schema.missing`, `value.out-of-range` |
| `slots` | array of [Move](#schema-slot) | Required | list | Child fields and array budgets apply [Relationships](#constraint-forms). | Explicit move definitions; aliases must resolve every canonical ground and air entry. | `schema.missing`, `value.out-of-range` |
| `aliases` | array of [Move alias](#schema-alias) | Required | list | Child fields and array budgets apply [Relationships](#constraint-forms). | Source-only move reuse; cooking expands each alias to a complete runtime move. | `schema.missing`, `value.out-of-range` |

<a id="schema-hurtboxCapsule"></a>

### Fallback hurtbox capsule

Location: `character.json.hurtboxCapsules[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `startX` | number | Required | m | Finite 32-bit number; no additional scalar limit | Endpoint in the fallback hurtbox capsule-center facing frame; larger values shift the endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `startY` | number | Required | m | Finite 32-bit number; no additional scalar limit | Endpoint in the fallback hurtbox capsule-center facing frame; larger values shift the endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `startZ` | number | Required | m | Finite 32-bit number; no additional scalar limit | Endpoint in the fallback hurtbox capsule-center facing frame; larger values shift the endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endX` | number | Required | m | Finite 32-bit number; no additional scalar limit | Endpoint in the fallback hurtbox capsule-center facing frame; larger values shift the endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endY` | number | Required | m | Finite 32-bit number; no additional scalar limit | Endpoint in the fallback hurtbox capsule-center facing frame; larger values shift the endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endZ` | number | Required | m | Finite 32-bit number; no additional scalar limit | Endpoint in the fallback hurtbox capsule-center facing frame; larger values shift the endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `radius` | number | Required | m | Finite 32-bit number; no additional scalar limit | Volume radius; larger values widen the attack, vulnerable body or catch volume. | `schema.missing`, `value.out-of-range` |

<a id="schema-hurtboxBone"></a>

### Bone hurtbox capsule

Location: `character.json.hurtboxBoneDefs[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `boneId` | string | Required | bone identifier | Text; naming and reference rules apply | Bone point used as the local frame; null attack bones select the fighter root frame. | `schema.missing`, `value.out-of-range` |
| `offsetX` | number | Required | m | Finite 32-bit number; no additional scalar limit | Start-point offset in the containing root or bone frame; larger values shift that endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `offsetY` | number | Required | m | Finite 32-bit number; no additional scalar limit | Start-point offset in the containing root or bone frame; larger values shift that endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `offsetZ` | number | Required | m | Finite 32-bit number; no additional scalar limit | Start-point offset in the containing root or bone frame; larger values shift that endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `radius` | number | Required | m | at least 0, no upper gameplay limit | Volume radius; larger values widen the attack, vulnerable body or catch volume. | `schema.missing`, `value.out-of-range` |
| `endBoneId` | string or null | `null` | bone identifier | Text; naming and reference rules apply | Bone point used as the local frame; null attack bones select the fighter root frame. | `schema.missing`, `value.out-of-range` |
| `endOffsetX` | number | `0` | m | Finite 32-bit number; no additional scalar limit | End-point offset for capsule geometry; larger values shift the second endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endOffsetY` | number | `0` | m | Finite 32-bit number; no additional scalar limit | End-point offset for capsule geometry; larger values shift the second endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endOffsetZ` | number | `0` | m | Finite 32-bit number; no additional scalar limit | End-point offset for capsule geometry; larger values shift the second endpoint along the named axis. | `schema.missing`, `value.out-of-range` |

<a id="schema-captureGeometry"></a>

### Universal grab geometry

Location: `character.json.captureGeometry`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `reach` | number | Required | m | greater than 0, no upper gameplay limit | Universal grab distance ahead of the fighter; larger values can reach farther. | `schema.missing`, `value.out-of-range` |
| `width` | number | Required | m | greater than 0, no upper gameplay limit | Full capture volume dimension; larger values cover a wider or taller region. | `schema.missing`, `value.out-of-range` |
| `height` | number | Required | m | greater than 0, no upper gameplay limit | Full capture volume dimension; larger values cover a wider or taller region. | `schema.missing`, `value.out-of-range` |
| `offsetY` | number | Required | m | Finite 32-bit number; no additional scalar limit | Universal capture-volume center height relative to the grabber capsule center; larger values raise the capture box. | `schema.missing`, `value.out-of-range` |
| `attackerAnchor` | object of [Universal grab anchor](#schema-anchor) | Required | object | Child fields and array budgets apply | Universal hold placement subtracts this anchor Y from victimAnchor Y; current placement retains X and Z but does not read them. | `schema.missing`, `value.out-of-range` |
| `victimAnchor` | object of [Universal grab anchor](#schema-anchor) | Required | object | Child fields and array budgets apply | Victim capsule-center offset in the grabber facing frame; vertical offset is victimAnchor Y minus attackerAnchor Y. | `schema.missing`, `value.out-of-range` |

<a id="schema-anchor"></a>

### Universal grab anchor

Location: `captureGeometry.attackerAnchor or victimAnchor`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `x` | number | Required | m | Finite 32-bit number; no additional scalar limit | Coordinate along the named axis in the containing frame; see coordinate conventions for that object. | `schema.missing`, `value.out-of-range` |
| `y` | number | Required | m | Finite 32-bit number; no additional scalar limit | Coordinate along the named axis in the containing frame; see coordinate conventions for that object. | `schema.missing`, `value.out-of-range` |
| `z` | number | Required | m | Finite 32-bit number; no additional scalar limit | Coordinate along the named axis in the containing frame; see coordinate conventions for that object. | `schema.missing`, `value.out-of-range` |

<a id="schema-capabilityRequirement"></a>

### Capability requirement

Location: `character.json.capabilityRequirements[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `capabilityId` | string | Required | identifier | Text; naming and reference rules apply | Stable engine capability ID; use a public ID and declare the same ID in capabilityRequirements. | `schema.missing`, `value.out-of-range` |
| `capabilityVersion` | string | Required | version | Text; naming and reference rules apply | Exact capability version string; it must match the supported version and the requirement declaration. | `schema.missing`, `value.out-of-range` |

<a id="schema-slot"></a>

### Move

Location: `character.json.slots[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `id` | string | Required | identifier | Text; naming and reference rules apply | Identity of this move or form within its containing object; canonical move IDs preserve uppercase special letters. | `schema.missing`, `value.out-of-range` |
| `name` | string | Required | text | Text; naming and reference rules apply | Creator-facing move name; does not change its canonical identity. | `schema.missing`, `value.out-of-range` |
| `description` | string | Required | text | Text; naming and reference rules apply | Creator-facing move explanation; has no gameplay effect. | `schema.missing`, `value.out-of-range` |
| `iconId` | string | Required | identifier | Text; naming and reference rules apply | Semantic icon identifier; does not bind an image file. | `schema.missing`, `value.out-of-range` |
| `behavior` | enum | Required | enum | `meleeCombo`, `chargeAttack`, `aimedProjectile`, `projectile`, `airGroundProjectile`, `selfBuff`, `areaDenial`, `directionalDash` | Selects the move lifecycle; meleeCombo is an ordinary fixed timeline, not a restriction to melee hitboxes. | `schema.missing`, `enum.unknown` |
| `aimMode` | enum | Required | enum | `none`, `groundCursor`, `cameraForward3D`, `groundVector` | Selects aiming input; none disables aim, groundCursor selects a point, groundVector a horizontal direction, cameraForward3D a 3D direction. | `schema.missing`, `enum.unknown` |
| `cooldownTicks` | ushort | Required | ticks | 0 to 65535 | Reuse delay applied when the activation ends or is cancelled; larger values delay the shared ground/air input slot longer. | `schema.missing`, `value.out-of-range` |
| `isRecoveryMove` | boolean | Required | boolean | `true` or `false` | Resets the air float clock when the move starts; does not itself grant jumps, dodges, or a once-per-airtime availability gate. | `schema.missing`, `value.out-of-range` |
| `preserveMomentumOnStart` | boolean | Required | boolean | `true` or `false` | Keeps grounded incoming horizontal velocity at ordinary startup; false clears grounded horizontal speed unless slide/run carry applies. Air momentum and capability overrides still follow their own rules. | `schema.missing`, `value.out-of-range` |
| `timeline` | object of [Move timeline](#schema-timeline) | Required | object | Child fields and array budgets apply | Ordered stages of this move; each stage has its own zero-based operation clock. | `schema.missing`, `value.out-of-range` |
| `chargePool` | object of [Move stock](#schema-chargePool) or null | `null` | object | Child fields and array budgets apply | Optional shared input-slot stock and passive refill; larger stock permits more uses before waiting. | `schema.missing`, `value.out-of-range` |
| `aimMovement` | enum | `"fixed"` | enum | `fixed`, `mobile` | fixed locks grounded movement during aim; mobile admits stick movement and reserves the right stick for aiming. | `schema.missing`, `enum.unknown` |
| `aimAnimationId` | string or null | `null` | identifier | Text; naming and reference rules apply | Optional animation while aiming; omit to use the move animation. | `schema.missing`, `value.out-of-range` |
| `allowSlideCarry` | boolean | `false` | boolean | `true` or `false` | Allows an eligible grounded normal to carry a slide; incompatible with move-owned motion and aiming. | `schema.missing`, `value.out-of-range` |
| `hitPresentationId` | string or null | `null` | identifier | Text; naming and reference rules apply | Cue emitted for a confirmed contact; a hitbox-specific value identifies a sweetspot and overrides the slot cue. | `schema.missing`, `value.out-of-range` |
| `aimMovementSpeedScale` | number | `1` | ratio | greater than 0, at most 1 | Fraction of run and air speed while mobile aiming; larger values let the fighter move faster during the hold. | `schema.missing`, `value.out-of-range` |
| `shellPolicy` | enum | `"popOut"` | enum | `popOut`, `tuck`, `ride` | popOut exits the shell, tuck keeps a tucked move, ride starts the shell ride after successful completion. | `schema.missing`, `enum.unknown` |

<a id="schema-chargePool"></a>

### Move stock

Location: `slot.chargePool`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `maxCharges` | integer | Required | count | at least 1, at most 3; storage -2147483648 to 2147483647 | Number of stored uses; larger values allow more activations before refill is needed. | `schema.missing`, `value.out-of-range` |
| `regenTicks` | ushort | Required | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535 | Time between stock refills; larger values refill more slowly. | `schema.missing`, `value.out-of-range` |

<a id="schema-alias"></a>

### Move alias

Location: `character.json.aliases[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `from` | string | Required | canonical slot | Text; naming and reference rules apply | Alias entry being supplied; use a canonical slot ID. | `schema.missing`, `value.out-of-range` |
| `to` | string | Required | canonical slot | Text; naming and reference rules apply | Explicit move or alias reused by from; alias cycles and unresolved targets are rejected. | `schema.missing`, `value.out-of-range` |

<a id="schema-timeline"></a>

### Move timeline

Location: `slot.timeline`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `stages` | array of [Timeline stage](#schema-stage) | Required | list | Child fields and array budgets apply | Successive fixed parts of the move; each stage resets the operation clock. | `schema.missing`, `value.out-of-range` |

<a id="schema-stage"></a>

### Timeline stage

Location: `slot.timeline.stages[]`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `durationTicks` | ushort | Required | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535 [Relationships](#stages-movement-windows-and-presentation). | Length of this stage, volume or timed effect; larger values keep it alive longer in its owning clock. | `schema.missing`, `value.out-of-range` |
| `iasaTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#stages-movement-windows-and-presentation). | Earliest early-cancel boundary; zero disables early cancellation, larger nonzero values delay cancellation. | `schema.missing`, `value.out-of-range` |
| `landingLagTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#stages-movement-windows-and-presentation). | Recovery imposed when an aerial move lands outside auto-cancel windows; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `autoCancelBeforeTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#stages-movement-windows-and-presentation). | Landing at or before this nonzero stage age avoids move landing lag; larger values widen the early window. | `schema.missing`, `value.out-of-range` |
| `autoCancelAfterTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#stages-movement-windows-and-presentation). | Landing at or after this nonzero stage age avoids move landing lag; larger values delay the late window. | `schema.missing`, `value.out-of-range` |
| `attackRange` | number | `0` | m | at least 0, no upper gameplay limit [Relationships](#stages-movement-windows-and-presentation). | Legacy authored range metadata; stopInAttackRange instead uses the first later hitbox geometry. | `schema.missing`, `value.out-of-range` |
| `warpRange` | number | `0` | reserved | at least 0, no upper gameplay limit [Relationships](#stages-movement-windows-and-presentation). | Retired targeting metadata; keep zero, since nonzero values are rejected. | `schema.missing`, `value.out-of-range` |
| `trackingStrength` | number | `0` | reserved | at least 0, at most 1 [Relationships](#stages-movement-windows-and-presentation). | Retired targeting metadata; keep zero, since nonzero values are rejected. | `schema.missing`, `value.out-of-range` |
| `useTargetLock` | boolean | `false` | reserved | `true` or `false` [Relationships](#stages-movement-windows-and-presentation). | Retired per-stage targeting flag; keep false and use startup aim correction or a public lifecycle. | `schema.missing`, `value.out-of-range` |
| `rotateTowardTarget` | boolean | `false` | reserved | `true` or `false` [Relationships](#stages-movement-windows-and-presentation). | Retired per-stage targeting flag; keep false and use startup aim correction or a public lifecycle. | `schema.missing`, `value.out-of-range` |
| `animationIds` | array | Required | list | Child fields and array budgets apply [Relationships](#stages-movement-windows-and-presentation). | Lowercase semantic animation IDs for this stage; the compiler sorts them and the first is its primary clip. | `schema.missing`, `value.out-of-range` |
| `operations` | array | Required | list | Child fields and array budgets apply [Relationships](#stages-movement-windows-and-presentation). | Timed effects within this stage; same-tick operations execute in authored array order. | `schema.missing`, `value.out-of-range` |

<a id="schema-hitbox"></a>

### Hitbox and command-grab capture volume

Location: `operation.hitbox or capability hitbox/capture fields`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `shape` | enum | Required | enum | `sphere`, `capsule` [Relationships](#stages-movement-windows-and-presentation). | sphere uses one center; capsule uses start and end points joined by a radius. | `schema.missing`, `enum.unknown` |
| `radius` | number | Required | m | at least 0, no upper gameplay limit; command-grab capture: greater than 0, at most 3 [Relationships](#stages-movement-windows-and-presentation). | Volume radius; larger values widen the attack, vulnerable body or catch volume. | `schema.missing`, `value.out-of-range` |
| `offsetX` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Start-point offset in the containing root or bone frame; larger values shift that endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `offsetY` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Start-point offset in the containing root or bone frame; larger values shift that endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `offsetZ` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Start-point offset in the containing root or bone frame; larger values shift that endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endOffsetX` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | End-point offset for capsule geometry; larger values shift the second endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endOffsetY` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | End-point offset for capsule geometry; larger values shift the second endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `endOffsetZ` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | End-point offset for capsule geometry; larger values shift the second endpoint along the named axis. | `schema.missing`, `value.out-of-range` |
| `startBoneId` | string or null | `null` | bone identifier | Text; naming and reference rules apply [Relationships](#stages-movement-windows-and-presentation). | Bone point used as the local frame; null attack bones select the fighter root frame. | `schema.missing`, `value.out-of-range` |
| `endBoneId` | string or null | `null` | bone identifier | Text; naming and reference rules apply [Relationships](#stages-movement-windows-and-presentation). | Bone point used as the local frame; null attack bones select the fighter root frame. | `schema.missing`, `value.out-of-range` |
| `damage` | number | Required | percent | at least 0, no upper gameplay limit [Relationships](#stages-movement-windows-and-presentation). | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `angle` | number | Required | degrees | at least -90, at most 90 [Relationships](#stages-movement-windows-and-presentation). | Vertical launch pitch; larger positive values launch more upward, negative values launch downward. | `schema.missing`, `value.out-of-range` |
| `baseKnockback` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit; command-grab capture: at least 0, at most 0 [Relationships](#stages-movement-windows-and-presentation). | Flat term in launch magnitude; larger values strengthen a launch at every victim percent. | `schema.missing`, `value.out-of-range` |
| `knockbackGrowth` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit; command-grab capture: at least 0, at most 0 [Relationships](#stages-movement-windows-and-presentation). | Term multiplied by 1 + victimPercent / 100; larger values make high-percent victims fly faster. | `schema.missing`, `value.out-of-range` |
| `stunTicks` | ushort | Required | gate | 0 to 65535; command-grab capture: at least 0, at most 0 [Relationships](#stages-movement-windows-and-presentation). | Zero disables ordinary hitstun; any positive value enables formula-derived stun and is not the stun duration. | `schema.missing`, `value.out-of-range` |
| `durationTicks` | ushort | Required | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535; command-grab capture: at least 1, at most 30 [Relationships](#stages-movement-windows-and-presentation). | Length of this stage, volume or timed effect; larger values keep it alive longer in its owning clock. | `schema.missing`, `value.out-of-range` |
| `interruptible` | boolean | Required | boolean | `true` or `false` [Relationships](#stages-movement-windows-and-presentation). | Serialized hitbox flag retained in cooked and snapshot data; current Shared contact simulation does not read it, so it does not grant armor or prevent interruption. | `schema.missing`, `value.out-of-range` |
| `hitGroup` | byte | Required | group number | 0 to 255 [Relationships](#stages-movement-windows-and-presentation). | Zero gives each volume an independent hit history; equal nonzero groups share one hit per victim per activation. | `schema.missing`, `value.out-of-range` |
| `knockbackDirection` | enum | `"awayFromOwner"` | enum | `awayFromOwner`, `towardOwner` [Relationships](#stages-movement-windows-and-presentation). | awayFromOwner pushes away horizontally; towardOwner pulls toward the owner. | `schema.missing`, `enum.unknown` |
| `fixedHitstunTicks` | ushort | `0` | ticks | at least 0, at most 240; storage 0 to 65535; command-grab capture: at least 0, at most 0 [Relationships](#stages-movement-windows-and-presentation). | Nonzero replaces formula-derived hitstun duration and requires a positive stun gate; larger values hold the victim longer. | `schema.missing`, `value.out-of-range` |
| `hitPresentationId` | string or null | `null` | identifier | Text; naming and reference rules apply [Relationships](#stages-movement-windows-and-presentation). | Cue emitted for a confirmed contact; a hitbox-specific value identifies a sweetspot and overrides the slot cue. | `schema.missing`, `value.out-of-range` |
| `requiresFacingOwner` | boolean | `false` | boolean | `true` or `false` [Relationships](#stages-movement-windows-and-presentation). | Limits the hitbox contact to victims facing its owner; false admits either facing. | `schema.missing`, `value.out-of-range` |

<a id="schema-projectile"></a>

### Projectile

Location: `operation.projectile or capability.parameters.projectile`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `launchOffsetX` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#returning-and-perching-projectiles). | Projectile start offset in the owner facing frame; larger values move its spawn along the named axis. | `schema.missing`, `value.out-of-range` |
| `launchOffsetY` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#returning-and-perching-projectiles). | Projectile start offset in the owner facing frame; larger values move its spawn along the named axis. | `schema.missing`, `value.out-of-range` |
| `launchOffsetZ` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#returning-and-perching-projectiles). | Projectile start offset in the owner facing frame; larger values move its spawn along the named axis. | `schema.missing`, `value.out-of-range` |
| `speed` | number | Required | m/s | at least 0, at most 120; thrown tether: greater than 0, at most 60; diving perch: greater than 0, at most 60 [Relationships](#returning-and-perching-projectiles). | Travel speed; larger values cover more distance per tick. | `schema.missing`, `value.out-of-range` |
| `gravity` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit [Relationships](#returning-and-perching-projectiles). | Downward acceleration; larger values bend flight or falling downward faster. | `schema.missing`, `value.out-of-range` |
| `radius` | number | Required | m | at least 0, no upper gameplay limit; thrown tether: greater than 0, at most 3 [Relationships](#returning-and-perching-projectiles). | Volume radius; larger values widen the attack, vulnerable body or catch volume. | `schema.missing`, `value.out-of-range` |
| `damage` | number | Required | percent | at least 0, no upper gameplay limit [Relationships](#returning-and-perching-projectiles). | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `angle` | number | Required | degrees | at least -90, at most 90 [Relationships](#returning-and-perching-projectiles). | Vertical launch pitch; larger positive values launch more upward, negative values launch downward. | `schema.missing`, `value.out-of-range` |
| `baseKnockback` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#returning-and-perching-projectiles). | Flat term in launch magnitude; larger values strengthen a launch at every victim percent. | `schema.missing`, `value.out-of-range` |
| `knockbackGrowth` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#returning-and-perching-projectiles). | Term multiplied by 1 + victimPercent / 100; larger values make high-percent victims fly faster. | `schema.missing`, `value.out-of-range` |
| `stunTicks` | ushort | Required | gate | 0 to 65535 [Relationships](#returning-and-perching-projectiles). | Zero disables ordinary hitstun; any positive value enables formula-derived stun and is not the stun duration. | `schema.missing`, `value.out-of-range` |
| `maxFlightTicks` | ushort | Required | ticks | at least 1, at most 960; storage 0 to 65535; thrown tether: at least 1, at most 240 [Relationships](#returning-and-perching-projectiles). | Projectile or flight lifetime limit; larger values allow more time before the lifecycle times out. | `schema.missing`, `value.out-of-range` |
| `yawOffsetDegrees` | number | Required | degrees | at least -180, at most 180 [Relationships](#returning-and-perching-projectiles). | Horizontal shot offset around facing or aim; positive values rotate the outgoing direction around world up. | `schema.missing`, `value.out-of-range` |
| `return` | object of [Returning projectile](#schema-projectileReturn) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#returning-and-perching-projectiles). | Optional outbound-and-return flight; locks its move slot until the projectile is caught or resolved. | `schema.missing`, `value.out-of-range` |
| `homing` | object of [Projectile homing](#schema-homing) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#returning-and-perching-projectiles). | Optional per-tick turn toward the captured target; absence leaves straight flight. | `schema.missing`, `value.out-of-range` |
| `perch` | object of [Projectile perch](#schema-perch) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#returning-and-perching-projectiles). | Optional attached mark and pop lifecycle; absence uses ordinary projectile contact. | `schema.missing`, `value.out-of-range` |
| `challengeTicks` | ushort | `0` | ticks | at least 0, at most 600; storage 0 to 65535 [Relationships](#returning-and-perching-projectiles). | Projectile parry-challenge window; larger values lengthen the authored challenge period. | `schema.missing`, `value.out-of-range` |

<a id="schema-projectileReturn"></a>

### Returning projectile

Location: `projectile.return`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `outboundTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-projectile-return). | Maximum outward leg before returning; larger values delay the return. | `schema.missing`, `value.out-of-range` |
| `returnSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-projectile-return). | Return speed cap; larger values bring the projectile back faster. | `schema.missing`, `value.out-of-range` |
| `returnAcceleration` | number | Required | m/s² | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-projectile-return). | Return steering acceleration; larger values change return velocity faster. | `schema.missing`, `value.out-of-range` |
| `selfHitDamage` | number | Required | percent | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-projectile-return). | Damage to a busy thrower instead of a catch; larger values punish a failed catch more. | `schema.missing`, `value.out-of-range` |
| `selfHitStunTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-projectile-return). | Fixed stun applied to a busy thrower; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `catchPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-projectile-return). | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `selfHitPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-projectile-return). | Optional declared lifecycle cue; empty text omits that presentation event. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `hit` | object of [Return-leg contact overrides](#schema-legHit) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-projectile-return). | Optional return-leg damage and launch overrides; omission uses the projectile contact settings. | `schema.missing`, `value.out-of-range` |

<a id="schema-legHit"></a>

### Return-leg contact overrides

Location: `projectile.return.hit`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `damage` | number | Required | percent | at least 0, at most 100 [Relationships](#returning-and-perching-projectiles). | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `angle` | number | Required | degrees | at least -90, at most 90 [Relationships](#returning-and-perching-projectiles). | Vertical launch pitch; larger positive values launch more upward, negative values launch downward. | `schema.missing`, `value.out-of-range` |
| `baseKnockback` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#returning-and-perching-projectiles). | Flat term in launch magnitude; larger values strengthen a launch at every victim percent. | `schema.missing`, `value.out-of-range` |
| `knockbackGrowth` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#returning-and-perching-projectiles). | Term multiplied by 1 + victimPercent / 100; larger values make high-percent victims fly faster. | `schema.missing`, `value.out-of-range` |
| `stunTicks` | ushort | Required | gate | at least 0, at most 240; storage 0 to 65535 [Relationships](#returning-and-perching-projectiles). | Zero disables ordinary hitstun; any positive value enables formula-derived stun and is not the stun duration. | `schema.missing`, `value.out-of-range` |
| `fixedHitstunTicks` | ushort | `0` | ticks | at least 0, at most 240; storage 0 to 65535 [Relationships](#returning-and-perching-projectiles). | Nonzero replaces formula-derived hitstun duration and requires a positive stun gate; larger values hold the victim longer. | `schema.missing`, `value.out-of-range` |

<a id="schema-homing"></a>

### Projectile homing

Location: `projectile.homing`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `turnDegreesPerTick` | number | Required | degrees/tick | greater than 0, at most 90 [Relationships](#constraint-projectile-perch). | Maximum homing turn each simulation tick; larger values track a moving target more tightly. | `schema.missing`, `value.out-of-range` |

<a id="schema-perch"></a>

### Projectile perch

Location: `projectile.perch`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `perchTicks` | ushort | Required | ticks | at least 1, at most 1200; storage 0 to 65535 [Relationships](#constraint-projectile-perch). | Time a projectile mark remains perched; larger values keep the mark available longer. | `schema.missing`, `value.out-of-range` |
| `pop` | object of [Perch release hit](#schema-pop) | Required | object | Child fields and array budgets apply [Relationships](#constraint-projectile-perch). | Damage and launch when the perched projectile pops; required when perch is present. | `schema.missing`, `value.out-of-range` |
| `dive` | object of [Projectile dive](#schema-dive) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-projectile-perch). | Optional rise, mark and downward dive before perching; omit for ordinary perch flight. | `schema.missing`, `value.out-of-range` |
| `peck` | object of [Perched periodic damage](#schema-peck) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-projectile-perch). | Optional periodic damage while perched; omit to disable periodic pecks. | `schema.missing`, `value.out-of-range` |
| `scatterPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-projectile-perch). | Optional declared lifecycle cue; empty text omits that presentation event. | `schema.missing`, `value.out-of-range` |

<a id="schema-pop"></a>

### Perch release hit

Location: `projectile.perch.pop`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `damage` | number | Required | percent | greater than 0, at most 100 [Relationships](#constraint-projectile-perch). | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `angle` | number | Required | degrees | at least -90, at most 90 [Relationships](#constraint-projectile-perch). | Vertical launch pitch; larger positive values launch more upward, negative values launch downward. | `schema.missing`, `value.out-of-range` |
| `baseKnockback` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#constraint-projectile-perch). | Flat term in launch magnitude; larger values strengthen a launch at every victim percent. | `schema.missing`, `value.out-of-range` |
| `knockbackGrowth` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#constraint-projectile-perch). | Term multiplied by 1 + victimPercent / 100; larger values make high-percent victims fly faster. | `schema.missing`, `value.out-of-range` |
| `stunTicks` | ushort | Required | gate | at least 0, at most 240; storage 0 to 65535 [Relationships](#constraint-projectile-perch). | Zero disables ordinary hitstun; any positive value enables formula-derived stun and is not the stun duration. | `schema.missing`, `value.out-of-range` |
| `presentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-projectile-perch). | Declared semantic cue emitted by this event; asset assembly supplies the corresponding effect or sound. | `schema.missing`, `value.out-of-range` |

<a id="schema-dive"></a>

### Projectile dive

Location: `projectile.perch.dive`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `hoverHeight` | number | Required | m | greater than 0, at most 12 [Relationships](#constraint-projectile-perch). | Height above the selected spot during the dive setup; larger values start the dive higher. | `schema.missing`, `value.out-of-range` |
| `maxRiseTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-projectile-perch). | Rise-phase timeout; larger values allow more time to reach the hover height. | `schema.missing`, `value.out-of-range` |
| `markTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-projectile-perch). | Warning hold before the dive; larger values give the victim more time to leave the mark. | `schema.missing`, `value.out-of-range` |
| `diveSpeed` | number | Required | m/s | greater than 0, at most 60 [Relationships](#constraint-projectile-perch). | Downward dive speed; larger values reach the marked spot sooner. | `schema.missing`, `value.out-of-range` |
| `noTargetDistance` | number | Required | m | at least 0, at most 20 [Relationships](#constraint-projectile-perch). | Forward fallback distance when there is no eligible target; larger values place the move farther ahead. | `schema.missing`, `value.out-of-range` |

<a id="schema-peck"></a>

### Perched periodic damage

Location: `projectile.perch.peck`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `intervalTicks` | ushort | Required | ticks | at least 1, no upper gameplay limit; storage 0 to 65535 [Relationships](#constraint-projectile-perch). | Time between repeated emissions or contacts; larger values reduce their frequency. | `schema.missing`, `value.out-of-range` |
| `damage` | number | Required | percent | at least 1, at most 20; whole numbers only [Relationships](#constraint-projectile-perch). | Whole damage percent added per peck; larger values deal more damage each interval. | `schema.missing`, `value.out-of-range` |
| `presentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-projectile-perch). | Declared semantic cue emitted by this event; asset assembly supplies the corresponding effect or sound. | `schema.missing`, `value.out-of-range` |

<a id="schema-placement"></a>

### Presentation placement

Location: `operation.placement`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `attachmentMode` | enum | `"world"` | enum | `world`, `bone` [Relationships](#stages-movement-windows-and-presentation). | world places a cue in the world; bone follows its named bone attachment. | `schema.missing`, `enum.unknown` |
| `boneId` | string or null | `null` | bone identifier | Text; naming and reference rules apply [Relationships](#stages-movement-windows-and-presentation). | Bone point used as the local frame; null attack bones select the fighter root frame. | `schema.missing`, `value.out-of-range` |
| `localPositionX` | number | `0` | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local position; larger values shift the cue along the named axis. | `schema.missing`, `value.out-of-range` |
| `localPositionY` | number | `0` | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local position; larger values shift the cue along the named axis. | `schema.missing`, `value.out-of-range` |
| `localPositionZ` | number | `0` | m | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local position; larger values shift the cue along the named axis. | `schema.missing`, `value.out-of-range` |
| `localRotationX` | number | `0` | degrees | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local Euler rotation around the named axis. | `schema.missing`, `value.out-of-range` |
| `localRotationY` | number | `0` | degrees | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local Euler rotation around the named axis. | `schema.missing`, `value.out-of-range` |
| `localRotationZ` | number | `0` | degrees | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local Euler rotation around the named axis. | `schema.missing`, `value.out-of-range` |
| `localScaleX` | number | `1` | ratio | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local scale; larger values enlarge the cue along that axis. | `schema.missing`, `value.out-of-range` |
| `localScaleY` | number | `1` | ratio | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local scale; larger values enlarge the cue along that axis. | `schema.missing`, `value.out-of-range` |
| `localScaleZ` | number | `1` | ratio | Finite 32-bit number; no additional scalar limit [Relationships](#stages-movement-windows-and-presentation). | Presentation-only local scale; larger values enlarge the cue along that axis. | `schema.missing`, `value.out-of-range` |
| `durationTicks` | ushort | `28` | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535 [Relationships](#stages-movement-windows-and-presentation). | Length of this stage, volume or timed effect; larger values keep it alive longer in its owning clock. | `schema.missing`, `value.out-of-range` |

<a id="schema-shell"></a>

### Shell settings

Location: `character.json.shell`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `popOutDeceleration` | number | Required | m/s² | at least 0, at most 600 [Relationships](#constraint-shell). | Braking after leaving a grounded shell ride; larger values shed carried ride speed faster. | `schema.missing`, `value.out-of-range` |
| `blockedRecoilSpeed` | number | Required | m/s | at least 0, at most 30 [Relationships](#constraint-shell). | Upward and backward recoil when a ride meets a head-on wall; larger values bounce harder. | `schema.missing`, `value.out-of-range` |
| `splash` | object of [Tucked landing splash](#schema-splash) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-shell). | Landing hit and pop-up lag for tucked moves; omitted when the fighter has no tuck move. | `schema.missing`, `value.out-of-range` |
| `ride` | object of [Shell wave ride](#schema-ride) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-shell). | Wave movement and catch settings after a ride move; omitted when the fighter has no ride move. | `schema.missing`, `value.out-of-range` |

<a id="schema-splash"></a>

### Tucked landing splash

Location: `shell.splash`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `hitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `popUpTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 | Landing lag after a tucked splash, whether it hits or misses; larger values delay acting longer. | `schema.missing`, `value.out-of-range` |
| `presentationId` | string or null | `null` | identifier | Text; naming and reference rules apply | Declared semantic cue emitted by this event; asset assembly supplies the corresponding effect or sound. | `schema.missing`, `value.out-of-range` |

<a id="schema-ride"></a>

### Shell wave ride

Location: `shell.ride`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `ticks` | ushort | Required | ticks | at least 1, at most 600; storage 0 to 65535 | Grounded ride duration; larger values ride longer after touchdown. | `schema.missing`, `value.out-of-range` |
| `speed` | number | Required | m/s | greater than 0, at most 60 | Travel speed; larger values cover more distance per tick. | `schema.missing`, `value.out-of-range` |
| `turnDegreesPerSecond` | number | Required | degrees/s | at least 0, at most 720 | Maximum turn rate while charging or riding; larger values steer toward the desired heading faster. | `schema.missing`, `value.out-of-range` |
| `wave` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `hitPresentationId` | string or null | `null` | identifier | Text; naming and reference rules apply | Cue emitted for a confirmed contact; a hitbox-specific value identifies a sweetspot and overrides the slot cue. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-setVelocity"></a>

### setVelocity operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `metersPerSecond`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `setVelocity` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `metersPerSecond` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `velocityMode` | enum | Required | enum | `absolute`, `additive`, `vertical` | absolute replaces world velocity, additive adds world velocity, vertical replaces only vertical speed. | `schema.missing`, `enum.unknown` |
| `x` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | World-axis velocity component; absolute replaces it, additive adds it, vertical uses only Y. | `schema.missing`, `value.out-of-range` |
| `y` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | World-axis velocity component; absolute replaces it, additive adds it, vertical uses only Y. | `schema.missing`, `value.out-of-range` |
| `z` | number | Required | m/s | Finite 32-bit number; no additional scalar limit | World-axis velocity component; absolute replaces it, additive adds it, vertical uses only Y. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-forwardLunge"></a>

### forwardLunge operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `metersPerSecond`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `forwardLunge` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `metersPerSecond` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `speed` | number | Required | m/s | greater than 0, no upper gameplay limit | Travel speed; larger values cover more distance per tick. | `schema.missing`, `value.out-of-range` |
| `durationTicks` | ushort | Required | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535 | Length of this stage, volume or timed effect; larger values keep it alive longer in its owning clock. | `schema.missing`, `value.out-of-range` |
| `stopInAttackRange` | boolean | `false` | boolean | `true` or `false` | Stops the lunge when its first later hitbox geometry can reach an opponent; requires that same-stage hitbox. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-gravityWindow"></a>

### gravityWindow operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `normalized`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `gravityWindow` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `normalized` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `gravityScale` | number | Required | ratio | at least 0, at most 1 | Fraction of ordinary airborne gravity; larger values increase falling acceleration, zero suppresses gravity. | `schema.missing`, `value.out-of-range` |
| `durationTicks` | ushort | Required | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535 | Length of this stage, volume or timed effect; larger values keep it alive longer in its owning clock. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-armorWindow"></a>

### armorWindow operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `armorWindow` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `durationTicks` | ushort | Required | ticks | greater than 0, no upper gameplay limit; storage 0 to 65535 | Length of this stage, volume or timed effect; larger values keep it alive longer in its owning clock. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-spawnHitbox"></a>

### spawnHitbox operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `meters`.

Additional budget counters: 1 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `spawnHitbox` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `meters` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `hitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-spawnProjectile"></a>

### spawnProjectile operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `meters`.

Additional budget counters: 0 hitboxes, 1 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `spawnProjectile` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `meters` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `projectile` | object of [Projectile](#schema-projectile) | Required | object | Child fields and array budgets apply | Projectile launch and contact settings using the Projectile schema; larger speed travels farther per tick. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-setAimState"></a>

### setAimState operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `setAimState` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `aimState` | enum | Required | enum | `none`, `groundCursor`, `cameraForward3D`, `groundVector` | Selects aiming input; none disables aim, groundCursor selects a point, groundVector a horizontal direction, cameraForward3D a 3D direction. | `schema.missing`, `enum.unknown` |

<a id="schema-op-startCapability"></a>

### startCapability operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 1 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `startCapability` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `capabilityId` | string | Required | identifier | Text; naming and reference rules apply | Stable engine capability ID; use a public ID and declare the same ID in capabilityRequirements. | `schema.missing`, `value.out-of-range` |
| `capabilityVersion` | string | Required | version | Text; naming and reference rules apply | Exact capability version string; it must match the supported version and the requirement declaration. | `schema.missing`, `value.out-of-range` |
| `parameters` | object | Required | object | Child fields and array budgets apply | Parameters for capabilityId; use the matching public capability schema, not a mixed parameter bag. | `schema.missing`, `value.out-of-range` |

<a id="schema-op-emitPresentation"></a>

### emitPresentation operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `emitPresentation` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `presentationId` | string | Required | identifier | Text; naming and reference rules apply | Declared semantic cue emitted by this event; asset assembly supplies the corresponding effect or sound. | `schema.missing`, `value.out-of-range` |
| `placement` | object of [Presentation placement](#schema-placement) | `{}` | object | Child fields and array budgets apply | Presentation-only attachment and transform; omission uses world attachment and the Placement defaults. | `schema.missing`, `value.out-of-range` |
| `condition` | enum | `"always"` | enum | `always`, `sweetspotHit` | always plays on every activation; sweetspotHit waits for a confirmed hit from a hitbox with its own hit cue. | `schema.missing`, `enum.unknown` |

<a id="schema-op-completeTimeline"></a>

### completeTimeline operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `completeTimeline` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |

<a id="schema-op-swapForm"></a>

### swapForm operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `swapForm` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |

<a id="schema-op-startupAimCorrection"></a>

### startupAimCorrection operation

Location: `slot.timeline.stages[].operations[]`.

Operation unit: `ticks`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `kind` | enum | Required | enum | `startupAimCorrection` | Chooses the engine operation and its required fields; arbitrary creator code is not accepted. | `schema.missing`, `enum.unknown` |
| `tick` | ushort | Required | ticks | 0 to 65535 | Zero-based stage age at which this operation executes; larger values execute later and must stay inside the stage. | `schema.missing`, `value.out-of-range` |
| `unit` | enum | Required | enum | `ticks` | Required contract token for this operation; it does not convert numeric field values. | `schema.missing`, `unit.unknown` |
| `endTick` | ushort | Required | ticks | 0 to 65535 | Exclusive end of startup aim correction; larger values allow correction later, before the first launch commitment. | `schema.missing`, `value.out-of-range` |
| `acquisitionRange` | number | Required | m | greater than 0, at most 32 | Maximum opponent acquisition distance; larger values admit farther targets. | `schema.missing`, `value.out-of-range` |
| `acquisitionHalfAngleDegrees` | number | Required | degrees | greater than 0, at most 180 | Half-angle around facing for target acquisition; larger values admit targets farther to the side or behind. | `schema.missing`, `value.out-of-range` |
| `maxYawDegrees` | number | Required | degrees | at least 0, at most 180 | Maximum accumulated horizontal aim correction; larger values allow a greater turn from entry facing. | `schema.missing`, `value.out-of-range` |
| `maxPitchDegrees` | number | Required | degrees | at least 0, at most 90 | Maximum accumulated vertical pose correction; larger values allow steeper aim without moving the collision capsule. | `schema.missing`, `value.out-of-range` |
| `yawDegreesPerSecond` | number | Required | degrees/s | at least 0, at most 1440 | Maximum aim correction rate; larger values turn faster within the allowed correction window. | `schema.missing`, `value.out-of-range` |
| `pitchDegreesPerSecond` | number | Required | degrees/s | at least 0, at most 1440 | Maximum aim correction rate; larger values turn faster within the allowed correction window. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-aimed-recovery-v1"></a>

### aimed-recovery.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `startupTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-aimed-recovery). | Automatic recovery setup before the movement-input launch; larger values delay launch. | `schema.missing`, `value.out-of-range` |
| `verticalSpeed` | number | Required | m/s | greater than 0, at most 60 [Relationships](#constraint-aimed-recovery). | Neutral-input recovery launch speed upward; larger values give more height. | `schema.missing`, `value.out-of-range` |
| `directionalVerticalSpeed` | number | Required | m/s | greater than 0, no upper gameplay limit [Relationships](#constraint-aimed-recovery). | Recovery launch speed upward at full directional input; larger values retain more height while traveling sideways. | `schema.missing`, `value.out-of-range` |
| `horizontalSpeed` | number | Required | m/s | at least 0, at most 60 [Relationships](#constraint-aimed-recovery). | Recovery launch horizontal speed at full input; larger values give more sideways reach. | `schema.missing`, `value.out-of-range` |
| `preparationRiseSpeed` | number | Required | m/s | at least 0, at most 4 [Relationships](#constraint-aimed-recovery). | Upward speed during recovery setup with gravity suppressed; larger values rise more before launch. | `schema.missing`, `value.out-of-range` |
| `hop` | object of [Recovery setup hop](#schema-recoveryHop) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-aimed-recovery). | Optional fixed setup hop before recovery launch; requires zero preparationRiseSpeed. | `schema.missing`, `value.out-of-range` |
| `riseSteerAcceleration` | number | `0` | m/s² | at least 0, at most 120 [Relationships](#constraint-aimed-recovery). | Post-launch stick steering acceleration during recovery; zero disables steering and larger values redirect drift faster. | `schema.missing`, `value.out-of-range` |

<a id="schema-recoveryHop"></a>

### Recovery setup hop

Location: `aimed-recovery.parameters.hop`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `startTicks` | ushort | Required | ticks | at least 1, no upper gameplay limit; storage 0 to 65535 | Start of the automatic setup hop; larger values delay the hop within recovery startup. | `schema.missing`, `value.out-of-range` |
| `contactTicks` | ushort | Required | ticks | 0 to 65535 | End of the setup hop before launch; larger values lengthen its travel interval. | `schema.missing`, `value.out-of-range` |
| `forwardDistance` | number | Required | m | greater than 0, at most 3 | Setup hop travel along entry facing; larger values move farther forward. | `schema.missing`, `value.out-of-range` |
| `arcHeight` | number | Required | m | greater than 0, at most 3 | Setup hop height above its endpoint chord; larger values make a taller arc. | `schema.missing`, `value.out-of-range` |
| `landingHeight` | number | Required | m | at least 0, at most 1 | Setup hop endpoint height above its start; larger values finish the hop higher. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-charged-projectile-v1"></a>

### charged-projectile.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 1 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `maxChargeTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-projectile). | Hold-time cap; larger values permit a longer charge before maximum strength. | `schema.missing`, `value.out-of-range` |
| `tier2Ticks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-projectile). | Charge threshold for the named projectile radius tier; larger values require a longer hold to reach it. | `schema.missing`, `value.out-of-range` |
| `tier3Ticks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-projectile). | Charge threshold for the named projectile radius tier; larger values require a longer hold to reach it. | `schema.missing`, `value.out-of-range` |
| `fireTriggerTick` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-projectile). | Ticks after releasing the held charge before the projectile fires; larger values delay firing. Hold time is outside this release-stage clock. | `schema.missing`, `value.out-of-range` |
| `tier2Radius` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-charged-projectile). | Projectile radius for the named charge tier; larger values make that tier wider. | `schema.missing`, `value.out-of-range` |
| `tier3Radius` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-charged-projectile). | Projectile radius for the named charge tier; larger values make that tier wider. | `schema.missing`, `value.out-of-range` |
| `projectile` | object of [Projectile](#schema-projectile) | Required | object | Child fields and array budgets apply [Relationships](#constraint-charged-projectile). | Projectile launch and contact settings using the Projectile schema; larger speed travels farther per tick. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-charged-stream-v1"></a>

### charged-stream.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `maxChargeTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Hold-time cap; larger values permit a longer charge before maximum strength. | `schema.missing`, `value.out-of-range` |
| `fireTriggerTick` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Shot startup after activation or release, depending on lifecycle; larger values delay the shot. | `schema.missing`, `value.out-of-range` |
| `emitIntervalTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Time between repeated emissions or contacts; larger values reduce their frequency. | `schema.missing`, `value.out-of-range` |
| `minStreamTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Stream duration at minimum or maximum charge; larger values keep the spray running longer. | `schema.missing`, `value.out-of-range` |
| `maxStreamTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Stream duration at minimum or maximum charge; larger values keep the spray running longer. | `schema.missing`, `value.out-of-range` |
| `recoveryTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Committed recovery after the lifecycle action; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `maxBaseKnockback` | number | Required | m/s before weight/tuning | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-charged-stream). | Stream projectile base launch term at full charge; larger values push harder after a long hold. | `schema.missing`, `value.out-of-range` |
| `minRecoilSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-charged-stream). | Airborne owner recoil at minimum or maximum charge; larger values push the shooter back faster. | `schema.missing`, `value.out-of-range` |
| `maxRecoilSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-charged-stream). | Airborne owner recoil at minimum or maximum charge; larger values push the shooter back faster. | `schema.missing`, `value.out-of-range` |
| `projectile` | object of [Projectile](#schema-projectile) | Required | object | Child fields and array budgets apply [Relationships](#constraint-charged-stream). | Projectile launch and contact settings using the Projectile schema; larger speed travels farther per tick. | `schema.missing`, `value.out-of-range` |
| `sprayAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-charged-stream). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `sprayLoopTicks` | ushort | `0` | ticks | 0 to 65535 [Relationships](#constraint-charged-stream). | Playback loop duration for the named phase clip; larger values stretch each visual loop. | `schema.missing`, `value.out-of-range` |
| `endAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-charged-stream). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-finger-guns-v1"></a>

### finger-guns.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `fireTriggerTick` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-finger-guns). | Shot startup after activation or release, depending on lifecycle; larger values delay the shot. | `schema.missing`, `value.out-of-range` |
| `recoveryTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-finger-guns). | Committed recovery after the lifecycle action; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `shotsPerReload` | byte | Required | count | at least 1, at most 24; storage 0 to 255 [Relationships](#constraint-finger-guns). | Shots fired before the committed reload; larger values allow more shots per reload. | `schema.missing`, `value.out-of-range` |
| `reloadTicks` | ushort | Required | ticks | at least 1, at most 600; storage 0 to 65535 [Relationships](#constraint-finger-guns). | Committed reload after the last shot; larger values delay the next available shot. | `schema.missing`, `value.out-of-range` |
| `reloadAnimationId` | string | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-finger-guns). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `airMaxFallSpeed` | number | Required | m/s | at least 0, at most 30 [Relationships](#constraint-finger-guns). | Downward speed cap during the airborne shot; larger values permit a faster fall. | `schema.missing`, `value.out-of-range` |
| `airRecoilSpeed` | number | Required | m/s | at least 0, at most 30 [Relationships](#constraint-finger-guns). | Airborne recoil from a finger-gun shot; larger values push the owner back faster. | `schema.missing`, `value.out-of-range` |
| `projectile` | object of [Projectile](#schema-projectile) | Required | object | Child fields and array budgets apply [Relationships](#constraint-finger-guns). | Projectile launch and contact settings using the Projectile schema; larger speed travels farther per tick. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-dash-strike-v1"></a>

### dash-strike.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 2 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `distance` | number | Required | m | greater than 0, at most 12 [Relationships](#constraint-dash-strike). | Authored forward travel distance; larger values place the action farther ahead. | `schema.missing`, `value.out-of-range` |
| `speed` | number | Required | m/s | greater than 0, at most 60 [Relationships](#constraint-dash-strike). | Travel speed; larger values cover more distance per tick. | `schema.missing`, `value.out-of-range` |
| `tipPoseTick` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-dash-strike). | 60 Hz pose sample seek held at the dash endpoint during tip contact; it is neither a stage wait nor a delay after travel. | `schema.missing`, `value.out-of-range` |
| `recoveryTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-dash-strike). | Committed recovery after the lifecycle action; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `lightHitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-dash-strike). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `tipHitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-dash-strike). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `tipPresentationId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-dash-strike). | Declared cue emitted for the dash endpoint sweetspot contact. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-flash-cut-v1"></a>

### flash-cut.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 1 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `stanceTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-flash-cut). | Flash-cut target collection stance duration; larger values delay the lock and travel. | `schema.missing`, `value.out-of-range` |
| `lockRange` | number | Required | m | greater than 0, at most 20 [Relationships](#constraint-flash-cut). | Maximum target distance; larger values allow the lifecycle to reach farther targets. | `schema.missing`, `value.out-of-range` |
| `maxTargets` | ushort | Required | count | at least 1, at most 7; storage 0 to 65535 [Relationships](#constraint-flash-cut). | Maximum opponents selected by the flash-cut stance; larger values allow more legs. | `schema.missing`, `value.out-of-range` |
| `legTicks` | ushort | Required | ticks | at least 1, at most 30; storage 0 to 65535 [Relationships](#constraint-flash-cut). | Travel duration for each flash-cut leg; larger values slow each pass. | `schema.missing`, `value.out-of-range` |
| `passThroughDistance` | number | Required | m | at least 0, at most 3 [Relationships](#constraint-flash-cut). | Distance beyond a locked victim for a flash-cut leg endpoint; larger values travel farther past them. | `schema.missing`, `value.out-of-range` |
| `sheatheTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-flash-cut). | Delay after the final leg before held cuts release; larger values hold the finish longer. | `schema.missing`, `value.out-of-range` |
| `revealLeadTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-flash-cut). | Cue lead before the flash-cut release; larger values reveal the finish earlier. | `schema.missing`, `value.out-of-range` |
| `recoveryTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-flash-cut). | Committed recovery after the lifecycle action; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `whiffTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-flash-cut). | Flash-cut recovery when no target was locked; larger values punish a missed stance longer. | `schema.missing`, `value.out-of-range` |
| `cutHitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-flash-cut). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `stanceAnimationId` | string | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `legAnimationId` | string | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `sheatheAnimationId` | string | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `lockPresentationId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `revealPresentationId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `sheathePresentationId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `popPresentationId` | string | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-flash-cut). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-targeted-leap-v2"></a>

### targeted-leap.v2 parameters

Location: `startCapability.parameters`.

Additional budget counters: 1 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `riseTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-targeted-leap). | Targeted-leap upward phase duration; larger values take longer to reach the authored rise height. | `schema.missing`, `value.out-of-range` |
| `riseHeight` | number | Required | m | greater than 0, at most 30 [Relationships](#constraint-targeted-leap). | Targeted-leap height above the entry position; larger values rise higher before hanging. | `schema.missing`, `value.out-of-range` |
| `hangTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-targeted-leap). | Targeted-leap tracking hold before the destination locks; larger values wait longer before dropping. | `schema.missing`, `value.out-of-range` |
| `dropTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-targeted-leap). | Targeted-leap drop travel duration; larger values take longer to reach the locked spot. | `schema.missing`, `value.out-of-range` |
| `maxRange` | number | Required | m | greater than 0, at most 60 [Relationships](#constraint-targeted-leap). | Maximum target distance; larger values allow the lifecycle to reach farther targets. | `schema.missing`, `value.out-of-range` |
| `noTargetDistance` | number | Required | m | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-targeted-leap). | Forward fallback distance when there is no eligible target; larger values place the move farther ahead. | `schema.missing`, `value.out-of-range` |
| `maxFallTicks` | ushort | Required | ticks | at least 1, at most 600; storage 0 to 65535 [Relationships](#constraint-targeted-leap). | Targeted-leap free-fall timeout after the drop; larger values allow more time to find a real landing. | `schema.missing`, `value.out-of-range` |
| `hitRecoveryTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-targeted-leap). | Recovery after an unshielded hit or a miss/shield contact; larger values delay acting longer for that outcome. | `schema.missing`, `value.out-of-range` |
| `missRecoveryTicks` | ushort | Required | ticks | at least 0, at most 600; storage 0 to 65535 [Relationships](#constraint-targeted-leap). | Recovery after an unshielded hit or a miss/shield contact; larger values delay acting longer for that outcome. | `schema.missing`, `value.out-of-range` |
| `hitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-targeted-leap). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `riseAnimationId` | string or null | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `hangAnimationId` | string or null | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `hangPoseTick` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-targeted-leap). | Pose or animation seek sample for the named leap phase; larger values seek farther into that clip. | `schema.missing`, `value.out-of-range` |
| `dropAnimationId` | string or null | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `landingAnimationId` | string or null | Required | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `hitLandingSeekTick` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-targeted-leap). | Pose or animation seek sample for the named leap phase; larger values seek farther into that clip. | `schema.missing`, `value.out-of-range` |
| `risePresentationId` | string or null | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `hangPresentationId` | string or null | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `lockPresentationId` | string or null | Required | identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `landingPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-targeted-leap). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-steered-charge-v2"></a>

### steered-charge.v2 parameters

Location: `startCapability.parameters`.

Additional budget counters: 1 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `gallopSpeed` | number | Required | m/s | Finite 32-bit number; no additional scalar limit [Relationships](#constraint-steered-charge). | Charge travel speed; larger values cover more ground each gallop tick. | `schema.missing`, `value.out-of-range` |
| `turnDegreesPerSecond` | number | Required | degrees/s | at least 0, at most 1440 [Relationships](#constraint-steered-charge). | Maximum turn rate while charging or riding; larger values steer toward the desired heading faster. | `schema.missing`, `value.out-of-range` |
| `maxGallopTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Charge timeout; larger values allow a longer gallop before the miss ending. | `schema.missing`, `value.out-of-range` |
| `armored` | boolean | Required | boolean | `true` or `false` [Relationships](#constraint-steered-charge). | Protects the charge from ordinary hit interruption while still taking damage; grabs bypass that protection. | `schema.missing`, `value.out-of-range` |
| `lanceHitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-steered-charge). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `hitEndingTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Charge ending duration for the named outcome; larger values impose more recovery. | `schema.missing`, `value.out-of-range` |
| `missEndingTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Charge ending duration for the named outcome; larger values impose more recovery. | `schema.missing`, `value.out-of-range` |
| `skidTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Miss-ending skid duration; larger values take longer to brake the charge. | `schema.missing`, `value.out-of-range` |
| `turnAroundOnMiss` | boolean | Required | boolean | `true` or `false` [Relationships](#constraint-steered-charge). | Turns the fighter around during the charge miss ending when enabled. | `schema.missing`, `value.out-of-range` |
| `airStrideTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Charge airborne stride or look-down phase duration; larger values delay the fall-free ending. | `schema.missing`, `value.out-of-range` |
| `lookDownTicks` | ushort | Required | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Charge airborne stride or look-down phase duration; larger values delay the fall-free ending. | `schema.missing`, `value.out-of-range` |
| `challengedRearTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-steered-charge). | Rear-up duration after a challenged charge; larger values delay resuming the gallop. | `schema.missing`, `value.out-of-range` |
| `trackDegreesPerSecond` | number | Required | degrees/s | at least 0, at most 1440 [Relationships](#constraint-steered-charge). | Startup target tracking rate normally or after a challenge; larger values turn toward the target faster. | `schema.missing`, `value.out-of-range` |
| `challengedTrackDegreesPerSecond` | number | Required | degrees/s | at least 0, at most 1440 [Relationships](#constraint-steered-charge). | Startup target tracking rate normally or after a challenge; larger values turn toward the target faster. | `schema.missing`, `value.out-of-range` |
| `chestHitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-steered-charge). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `carry` | object of [Charge carry and fling](#schema-chargeCarry) | Required | object | Child fields and array budgets apply [Relationships](#constraint-steered-charge). | Charge catch anchor, maximum carry time and release launch settings. | `schema.missing`, `value.out-of-range` |
| `assist` | object of [Charge startup assistance](#schema-chargeAssist) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-steered-charge). | Optional charge startup target-acquisition assistance; omission disables the assistance. | `schema.missing`, `value.out-of-range` |
| `challengePresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `gallopAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `gallopLoopTicks` | ushort | `0` | ticks | 0 to 65535 [Relationships](#constraint-steered-charge). | Playback loop duration for the named phase clip; larger values stretch each visual loop. | `schema.missing`, `value.out-of-range` |
| `hitEndingAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `missEndingAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `airEndingAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `gallopPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `hitEndingPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `missEndingPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `airEndingPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-steered-charge). | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |

<a id="schema-chargeCarry"></a>

### Charge carry and fling

Location: `steered-charge.parameters.carry`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `anchorDistance` | number | Required | m | at least 0, at most 3 | Hold point ahead of the owner feet; larger values keep the victim farther forward. | `schema.missing`, `value.out-of-range` |
| `anchorHeight` | number | Required | m | at least 0, at most 3 | Charge carry anchor height above the owner feet; larger values hold the victim higher. | `schema.missing`, `value.out-of-range` |
| `maxCarryTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 | Charge carry timeout; larger values allow the victim to be carried longer before flinging. | `schema.missing`, `value.out-of-range` |
| `fling` | object of [Grab or carry release launch](#schema-launch) | Required | object | Child fields and array budgets apply | Release damage and knockback using the Launch schema; omit command-grab launch for a release lock instead. | `schema.missing`, `value.out-of-range` |
| `flingTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 | Charge fling phase duration; larger values prolong the committed release phase. | `schema.missing`, `value.out-of-range` |
| `flingAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `scoopPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |
| `flingPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply | Declared cue for the named lifecycle event; empty text disables optional cues. Nonempty cues require the presentation. prefix. | `schema.missing`, `value.out-of-range` |

<a id="schema-chargeAssist"></a>

### Charge startup assistance

Location: `steered-charge.parameters.assist`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `assistRange` | number | Required | m | greater than 0, at most 10 | Maximum opponent acquisition distance; larger values admit farther targets. | `schema.missing`, `value.out-of-range` |
| `assistHalfAngleDegrees` | number | Required | degrees | greater than 0, at most 90 | Half-angle around facing for target acquisition; larger values admit targets farther to the side or behind. | `schema.missing`, `value.out-of-range` |
| `assistDegreesPerSecond` | number | Required | degrees/s | greater than 0, at most 1440 | Maximum aim correction rate; larger values turn faster within the allowed correction window. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-thrust-v1"></a>

### thrust.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts. Add one hitbox when `trailHitbox` is present.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `aim` | enum | Required | enum | `stick`, `targetAhead` [Relationships](#constraint-thrust). | stick chooses input heading; targetAhead may choose an eligible target inside the authored cone. | `schema.missing`, `enum.unknown` |
| `targetConeDegrees` | number | `0` | degrees | at least 0, at most 180 [Relationships](#constraint-thrust). | Target-ahead acquisition cone; larger values admit targets farther from the forward heading. | `schema.missing`, `value.out-of-range` |
| `neutralPitchDegrees` | number | Required | degrees | at least -90, at most 90 [Relationships](#constraint-thrust). | Thrust pitch at neutral or full stick; larger positive values point more upward. | `schema.missing`, `value.out-of-range` |
| `stickPitchDegrees` | number | Required | degrees | at least -90, at most 90 [Relationships](#constraint-thrust). | Thrust pitch at neutral or full stick; larger positive values point more upward. | `schema.missing`, `value.out-of-range` |
| `speed` | number | Required | m/s | greater than 0, no upper gameplay limit [Relationships](#constraint-thrust). | Travel speed; larger values cover more distance per tick. | `schema.missing`, `value.out-of-range` |
| `acceleration` | number | Required | m/s² | at least 0, at most 240 [Relationships](#constraint-thrust). | Speed gain along the thrust direction; larger values accelerate toward maxSpeed faster. | `schema.missing`, `value.out-of-range` |
| `maxSpeed` | number | Required | m/s | no lower gameplay limit, at most 60 [Relationships](#constraint-thrust). | Thrust speed cap; larger values allow faster travel after acceleration. | `schema.missing`, `value.out-of-range` |
| `keepIncomingSpeed` | boolean | Required | boolean | `true` or `false` [Relationships](#constraint-thrust). | Includes incoming speed when choosing thrust speed; false starts from the authored speed. | `schema.missing`, `value.out-of-range` |
| `thrustTicks` | ushort | Required | ticks | at least 1, at most 600; storage 0 to 65535 [Relationships](#constraint-thrust). | Thrust motion-ownership duration; larger values drive the chosen direction longer. | `schema.missing`, `value.out-of-range` |
| `trailHitbox` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-thrust). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-tether-v1"></a>

### tether.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts. Add one hitbox when `atTarget` is present. Add one projectile when `atTarget` is absent.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `throwTick` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-tether). | Tether throw or placed-catch warning time; larger values delay its first action. | `schema.missing`, `value.out-of-range` |
| `projectile` | object of [Projectile](#schema-projectile) | Required when atTarget is absent; forbidden when atTarget is present | object | Child fields and array budgets apply [Relationships](#constraint-tether). | Projectile launch and contact settings using the Projectile schema; larger speed travels farther per tick. | `schema.missing`, `value.out-of-range` |
| `reelSpeed` | number | Required | m/s | greater than 0, at most 60 [Relationships](#constraint-tether). | Per-axis speed pulling a caught fighter toward the anchor; larger values bring the victim in faster. | `schema.missing`, `value.out-of-range` |
| `anchorDistance` | number | Required | m | at least 0, at most 3 [Relationships](#constraint-tether). | Hold point ahead of the owner feet; larger values keep the victim farther forward. | `schema.missing`, `value.out-of-range` |
| `arrivalStunTicks` | ushort | Required | ticks | at least 1, at most 240; storage 0 to 65535 [Relationships](#constraint-tether). | Fixed hold after the tether reaches its anchor; larger values retain the caught fighter longer. | `schema.missing`, `value.out-of-range` |
| `maxReelTicks` | ushort | Required | ticks | at least 1, at most 600; storage 0 to 65535 [Relationships](#constraint-tether). | Reel timeout; larger values allow a longer pull before the lifecycle releases. | `schema.missing`, `value.out-of-range` |
| `retractSpeed` | number | Required when atTarget is absent; forbidden when atTarget is present | m/s | greater than 0, at most 60 [Relationships](#constraint-tether). | Empty-ring return speed on a tether miss; larger values retract it faster. | `schema.missing`, `value.out-of-range` |
| `missRecoveryTicks` | ushort | Required | ticks | at least 0, at most 240; storage 0 to 65535 [Relationships](#constraint-tether). | Recovery after an unshielded hit or a miss/shield contact; larger values delay acting longer for that outcome. | `schema.missing`, `value.out-of-range` |
| `airGravityScale` | number | Required | ratio | at least 0, at most 1 [Relationships](#constraint-tether). | Fraction of ordinary airborne gravity; larger values increase falling acceleration, zero suppresses gravity. | `schema.missing`, `value.out-of-range` |
| `reelAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-tether). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `retractAnimationId` | string or null | `""` | animation identifier | Text; naming and reference rules apply [Relationships](#constraint-tether). | Semantic clip for this named lifecycle phase; list it in the stage as required by that capability. | `schema.missing`, `value.out-of-range` |
| `throwPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-tether). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `retractPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-tether). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `atTarget` | object of [Placed tether catch](#schema-tetherAtTarget) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-tether). | Replaces the thrown tether projectile with a placed catch at the selected target spot. | `schema.missing`, `value.out-of-range` |

<a id="schema-tetherAtTarget"></a>

### Placed tether catch

Location: `tether.parameters.atTarget`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `warningTicks` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-placed-tether). | Warning before the placed catch becomes active; larger values give more escape time. | `schema.missing`, `value.out-of-range` |
| `catchTicks` | ushort | Required | ticks | at least 1, at most 60; storage 0 to 65535 [Relationships](#constraint-placed-tether). | Placed-catch active duration; larger values keep its catch volume active longer. | `schema.missing`, `value.out-of-range` |
| `radius` | number | Required | m | greater than 0, at most 3 [Relationships](#constraint-placed-tether). | Volume radius; larger values widen the attack, vulnerable body or catch volume. | `schema.missing`, `value.out-of-range` |
| `height` | number | Required | m | no lower gameplay limit, at most 6 [Relationships](#constraint-placed-tether). | Full capture volume dimension; larger values cover a wider or taller region. | `schema.missing`, `value.out-of-range` |
| `damage` | number | Required | percent | at least 0, at most 100 [Relationships](#constraint-placed-tether). | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `maxRange` | number | Required | m | greater than 0, at most 32 [Relationships](#constraint-placed-tether). | Maximum target distance; larger values allow the lifecycle to reach farther targets. | `schema.missing`, `value.out-of-range` |
| `noTargetDistance` | number | Required | m | at least 0, no upper gameplay limit [Relationships](#constraint-placed-tether). | Forward fallback distance when there is no eligible target; larger values place the move farther ahead. | `schema.missing`, `value.out-of-range` |
| `warningPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-placed-tether). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `catchPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-placed-tether). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `unmarked` | object of [Unmarked tether attack](#schema-tetherUnmarked) | Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-placed-tether). | Alternate ordinary attack when the placed catch has no eligible owner-marked target. | `schema.missing`, `value.out-of-range` |

<a id="schema-tetherUnmarked"></a>

### Unmarked tether attack

Location: `tether.parameters.atTarget.unmarked`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `riseTick` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-placed-tether). | Activation age at which the unmarked hand rises; larger values delay the attack. | `schema.missing`, `value.out-of-range` |
| `activeTicks` | ushort | Required | ticks | at least 1, at most 60; storage 0 to 65535 [Relationships](#constraint-placed-tether). | Unmarked hand active duration; larger values leave its attack volume active longer. | `schema.missing`, `value.out-of-range` |
| `distance` | number | Required | m | at least 0, at most 6 [Relationships](#constraint-placed-tether). | Authored forward travel distance; larger values place the action farther ahead. | `schema.missing`, `value.out-of-range` |
| `radius` | number | Required | m | greater than 0, at most 3 [Relationships](#constraint-placed-tether). | Volume radius; larger values widen the attack, vulnerable body or catch volume. | `schema.missing`, `value.out-of-range` |
| `height` | number | Required | m | no lower gameplay limit, at most 6 [Relationships](#constraint-placed-tether). | Full capture volume dimension; larger values cover a wider or taller region. | `schema.missing`, `value.out-of-range` |
| `damage` | number | Required | percent | greater than 0, at most 100 [Relationships](#constraint-placed-tether). | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `angle` | number | Required | degrees | at least -90, at most 90 [Relationships](#constraint-placed-tether). | Vertical launch pitch; larger positive values launch more upward, negative values launch downward. | `schema.missing`, `value.out-of-range` |
| `baseKnockback` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#constraint-placed-tether). | Flat term in launch magnitude; larger values strengthen a launch at every victim percent. | `schema.missing`, `value.out-of-range` |
| `knockbackGrowth` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit [Relationships](#constraint-placed-tether). | Term multiplied by 1 + victimPercent / 100; larger values make high-percent victims fly faster. | `schema.missing`, `value.out-of-range` |
| `stunTicks` | ushort | Required | gate | at least 0, at most 240; storage 0 to 65535 [Relationships](#constraint-placed-tether). | Zero disables ordinary hitstun; any positive value enables formula-derived stun and is not the stun duration. | `schema.missing`, `value.out-of-range` |
| `recoveryTicks` | ushort | Required | ticks | at least 0, at most 240; storage 0 to 65535 [Relationships](#constraint-placed-tether). | Committed recovery after the lifecycle action; larger values delay the next action. | `schema.missing`, `value.out-of-range` |
| `presentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-placed-tether). | Declared semantic cue emitted by this event; asset assembly supplies the corresponding effect or sound. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-command-grab-v1"></a>

### command-grab.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 1 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `lungeTicks` | ushort | Required | ticks | at least 0, at most 60; storage 0 to 65535 [Relationships](#constraint-command-grab). | Command-grab forward lunge duration; larger values drive the approach longer. | `schema.missing`, `value.out-of-range` |
| `lungeSpeed` | number | Required | m/s | at least 0, at most 60 [Relationships](#constraint-command-grab). | Command-grab approach speed; larger values approach the capture point faster. | `schema.missing`, `value.out-of-range` |
| `stopDistance` | number | Required | m | at least 0, at most 5 [Relationships](#constraint-command-grab). | Command-grab lunge stopping gap near its target; larger values stop farther away. | `schema.missing`, `value.out-of-range` |
| `captureTick` | ushort | Required | ticks | at least 1, at most 120; storage 0 to 65535 [Relationships](#constraint-command-grab). | Command-grab capture-volume start; larger values delay the catch attempt. | `schema.missing`, `value.out-of-range` |
| `capture` | object of [Hitbox and command-grab capture volume](#schema-hitbox) | Required | object | Child fields and array budgets apply [Relationships](#constraint-command-grab). | Attack or capture volume using the Hitbox schema; its containing lifecycle constrains timing and hit behavior. | `schema.missing`, `value.out-of-range` |
| `releaseTick` | ushort | Required | ticks | at least 0, at most 240; storage 0 to 65535 [Relationships](#constraint-command-grab). | Command-grab release tick measured from activation; it must follow the whole capture window; larger values hold the victim longer. | `schema.missing`, `value.out-of-range` |
| `launch` | object of [Grab or carry release launch](#schema-launch) | Required when releaseLockTicks is zero or absent; otherwise Omit; no writable JSON default | object | Child fields and array budgets apply [Relationships](#constraint-command-grab). | Release damage and knockback using the Launch schema; omit command-grab launch for a release lock instead. | `schema.missing`, `value.out-of-range` |
| `catchPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-command-grab). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `releasePresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-command-grab). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |
| `level` | boolean | `false` | boolean | `true` or `false` [Relationships](#constraint-command-grab). | Keeps the command-grab lunge level with gravity off; a wall that blocks the approach can end the grab empty. | `schema.missing`, `value.out-of-range` |
| `anchorDistance` | number | Omit; no writable JSON default | m | at least 0, at most 3 [Relationships](#constraint-command-grab). | Numeric hold distance ahead of the owner feet; larger values hold farther forward. Omit for universal anchors. Literal null is invalid. | `schema.missing`, `value.out-of-range` |
| `releaseLockTicks` | ushort | Required when launch is absent; otherwise `0` | ticks | at least 0, at most 60; storage 0 to 65535 [Relationships](#constraint-command-grab). | Victim lock after a no-launch command-grab release; larger values keep the victim unable to act longer. | `schema.missing`, `value.out-of-range` |
| `whiffRecoveryTicks` | ushort | `0` | ticks | at least 0, at most 120; storage 0 to 65535 [Relationships](#constraint-command-grab). | Alternate command-grab miss recovery; zero keeps the enclosing stage recovery, larger values extend a nonzero miss ending. | `schema.missing`, `value.out-of-range` |

<a id="schema-launch"></a>

### Grab or carry release launch

Location: `command-grab.parameters.launch or steered-charge.parameters.carry.fling`.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `damage` | number | Required | percent | greater than 0, at most 100 | Damage added on contact; larger values raise the victim percent and the damage contribution to launch strength. | `schema.missing`, `value.out-of-range` |
| `angle` | number | Required | degrees | at least -90, at most 90 | Vertical launch pitch; larger positive values launch more upward, negative values launch downward. | `schema.missing`, `value.out-of-range` |
| `baseKnockback` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit | Flat term in launch magnitude; larger values strengthen a launch at every victim percent. | `schema.missing`, `value.out-of-range` |
| `knockbackGrowth` | number | Required | m/s before weight/tuning | at least 0, no upper gameplay limit | Term multiplied by 1 + victimPercent / 100; larger values make high-percent victims fly faster. | `schema.missing`, `value.out-of-range` |
| `stunTicks` | ushort | Required | gate | at least 0, at most 240; storage 0 to 65535 | Zero disables ordinary hitstun; any positive value enables formula-derived stun and is not the stun duration. | `schema.missing`, `value.out-of-range` |

<a id="schema-cap-cannonball-v1"></a>

### cannonball.v1 parameters

Location: `startCapability.parameters`.

Additional budget counters: 0 hitboxes, 0 projectiles, 0 capability starts.

| Field | Type or child schema | Required/default | Unit | Scalar range or tokens | Meaning | Diagnostics |
| --- | --- | --- | --- | --- | --- | --- |
| `takeoffTick` | ushort | Required | ticks | at least 0, at most 60; storage 0 to 65535 [Relationships](#constraint-cannonball). | Cannonball launch time within the move; larger values delay takeoff. | `schema.missing`, `value.out-of-range` |
| `maxRange` | number | Required | m | no lower gameplay limit, at most 30 [Relationships](#constraint-cannonball). | Maximum target distance; larger values allow the lifecycle to reach farther targets. | `schema.missing`, `value.out-of-range` |
| `noTargetDistance` | number | Required | m | greater than 0, no upper gameplay limit [Relationships](#constraint-cannonball). | Forward fallback distance when there is no eligible target; larger values place the move farther ahead. | `schema.missing`, `value.out-of-range` |
| `minFlightTicks` | ushort | Required | ticks | at least 2, no upper gameplay limit; storage 0 to 65535 [Relationships](#constraint-cannonball). | Cannonball flight time for a destination at the owner feet; larger values slow short hops. | `schema.missing`, `value.out-of-range` |
| `maxFlightTicks` | ushort | Required | ticks | no lower gameplay limit, at most 240; storage 0 to 65535 [Relationships](#constraint-cannonball). | Projectile or flight lifetime limit; larger values allow more time before the lifecycle times out. | `schema.missing`, `value.out-of-range` |
| `maxLaunchSpeed` | number | Required | m/s | greater than 0, at most 60 [Relationships](#constraint-cannonball). | Cannonball solved-launch speed cap; larger values allow a faster ballistic launch. | `schema.missing`, `value.out-of-range` |
| `launchPresentationId` | string or null | `""` | identifier | Text; naming and reference rules apply [Relationships](#constraint-cannonball). | Declared cue for the named lifecycle event; empty text disables optional cues. | `schema.missing`, `value.out-of-range` |

### cook.json

Generated from `FighterCookDocument.Fields`, the schema used by the game and KitCheck. Unknown fields and duplicate keys are errors. Null is allowed only where explicitly listed.

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `model` | GLB filename | Yes | One GLB directly in this folder. |
| `animations` | map of semantic ID to nonempty clip name | Yes | Exactly the animation IDs referenced by the compiled kit; names resolve against the GLB. |
| `creator` | nonempty string | Yes | Creator credited in the item manifest. |
| `license` | nonempty string | Yes | License for the item. |
| `attribution` | string | Yes | Required attribution; empty text is allowed. |
| `aiAssisted` | boolean | Yes | Whether AI assisted creation. |
| `portraits` | array of image filename or null | No | One entry per form; omitted art is rendered from the model. |
| `icons` | array of icon maps | No | One map per form; keys are icon.1, icon.2, icon.3, icon.a, icon.e, icon.r, icon.f; values are image filenames or null. |
| `humanoid` | JSON filename | No | Bone map filename; defaults to humanoid.json when that file exists. |

Only declared files are read as payloads. Unreferenced PNG and JPEG files are ignored. The optional standard bone map is `humanoid.json`, limited to 64 KiB. Image filenames must end in .png or .jpg. Animation IDs must match the kit exactly. `KitCheck cook <source-folder> [glb]` checks this schema and, when a GLB exists or is supplied, its clip names. Icons and portraits follow the form counts and canonical IDs of portable bindings.

### Bone identifiers

`bone.head`, `bone.spine2`, `bone.hips`, `bone.right-hand`, `bone.left-hand`, `bone.right-foot`, `bone.left-foot`, `bone.right-toes`, `bone.left-toes`, `bone.neck`, `bone.right-upper-arm`, `bone.left-upper-arm`, `bone.right-lower-arm`, `bone.left-lower-arm`, `bone.right-upper-leg`, `bone.left-upper-leg`, `bone.right-lower-leg`, `bone.left-lower-leg`, `bone.right-hand-end`, `bone.left-hand-end`. `bone.spine` also resolves to `bone.spine2`.

<!-- END GENERATED CONTRACT -->
## Gameplay constraints kept in the compiler

These are hand-written descriptions of the existing multi-field and lifecycle checks.
They are intentionally separate from the generated JSON field contract.
They remain in the compiler because changing their diagnostic ordering or capability admission would risk changing existing content.
The checker remains authoritative for these relationships. A future editor must still compile the complete kit before accepting an edit.

All ranges below include their endpoints unless the text says positive, above, below, or exclusive.
All numeric values used by a successful cook must be finite.
A field with no further gameplay constraint here has its storage range from the generated tables, not an implied balance recommendation.

### Package, body, and references

Manifest schema is 1 and authoring schema is 4. Version is SemVer 2.0 text.
Creator, license, and attribution are nonempty. Dependency version and cooked hash are nonempty.
The source must resolve every canonical move. An alias cannot replace an already explicit entry or make a cycle.
Capabilities must have unique IDs and supported versions. Unknown IDs raise `capability.unknown`; mismatched versions raise `capability.version-mismatch`.
`slop.internal.*` requirements raise `capability.untrusted` in Workshop. Retired identities raise `capability.retired` with a replacement reason.

`movement.airDodgeSpeed` is positive. `shieldRadius` is positive and greater than half `capsuleHeight`.
Capture reach, width, and height are positive; capture anchors and offsets are finite.
Bone hurtbox radii are nonnegative. End offsets require an end bone.
Bone IDs used by attacks must be required registry points or be declared by hurtbox bone definitions or attachment points.
Presentation cues must be declared. Nonempty animation roles use valid lowercase IDs; standard roles cannot reuse one ID.
Stage animation IDs must also start with `anim.`. Source compilation checks names, not asset availability.

### Stages, movement windows, and presentation

A stage has positive duration. IASA, landing lag, and both auto-cancel boundaries cannot exceed that duration.
Each operation tick is strictly below the stage duration. A lunge, gravity window, or armor window has positive duration and must end within the same stage.
Lunge speed is positive. Gravity scale is 0 through 1. A stopping lunge needs a later same-stage hitbox.
Attack range is nonnegative. Retired warp/tracking fields remain zero or false.

The generated budget tables give the compiler's stage, operation, slot and capability limits, and its per-operation counters.
The slot ceiling does not add move identities to the canonical grid.
Counters describe a compiled definition, not the number of runtime emissions a looping capability may later make.

A charge pool has one through three stocks and positive regeneration ticks.
Nondefault `aimMovementSpeedScale` is above zero and at most one, and requires mobile aim.
Slide carry is only for grounded normals with no aim, recovery designation, retired warp, or motion-owning operation.

A hitbox has nonnegative radius, damage, base, and growth, pitch -90 through 90, and positive lifetime.
Fixed hitstun is 0 through 240 and a nonzero override requires nonzero `stunTicks`.
An ordinary projectile has nonnegative speed, radius, and damage, pitch -90 through 90, and yaw offset -180 through 180.
Special lifecycles impose stronger projectile limits below.
A presentation placement has positive duration and positive scale on every axis. Bone attachment requires a declared bone.

Startup aim correction has one window per timeline. `endTick` is after `tick`, no later than stage end, and no later than the first contact or directional launch commitment.
It cannot begin after a prior commitment. Acquisition range is above zero through 32 m and acquisition half-angle is above zero through 180 degrees.
Maximum yaw is 0 through 180 degrees, maximum pitch 0 through 90 degrees, and both rates are 0 through 1440 degrees/s.
A nonzero yaw or pitch allowance requires a positive corresponding rate.

### Aimed recovery and charged shots

<a id="constraint-aimed-recovery"></a>

Aimed recovery startup is 1 through 120 ticks and precedes stage end.
Vertical speed is above zero through 60 m/s. Directional vertical speed is positive and cannot exceed neutral vertical speed.
Horizontal speed is 0 through 60 m/s, preparation rise is 0 through 4 m/s, and rise steering is 0 through 120 m/s².
For a setup hop, preparation rise is zero, start is positive, contact is at least four ticks after start and before launch.
Forward distance and arc height are above zero through 3 m; landing height is 0 through 1 m.

<a id="constraint-charged-projectile"></a>

Charged projectile uses one tick-zero `chargeAttack` lifecycle in one stage with IASA disabled.
It cannot share the slot with competing motion, aim changes, hitbox/projectile spawns, or another lifecycle.
Thresholds satisfy `0 < tier2Ticks < tier3Ticks <= maxChargeTicks`.
The base projectile radius and successive tier radii are positive and strictly increasing.
Projectile speed, flight lifetime, and fire startup are positive. Fire startup precedes the release stage end.
It cannot use returning, homing, or perching projectile blocks.

<a id="constraint-charged-stream"></a>

Charged stream has the same chargeAttack, stage, and competing-operation restrictions.
Charge, fire startup, interval, and minimum stream duration are positive. Maximum stream duration is at least the minimum.
Maximum base knockback is at least the projectile's base. Recoil satisfies `0 <= minRecoilSpeed <= maxRecoilSpeed`.
Its projectile has positive radius, speed, and lifetime, and cannot return, home, or perch.
A nonempty spray/end clip is listed in the stage and sorts after its primary clip. A positive spray loop requires a spray clip.

<a id="constraint-finger-guns"></a>

Finger guns uses one tick-zero meleeCombo lifecycle, one stage, no IASA, slide carry, or charge pool, and only presentation companions.
Aim may be none, cameraForward3D, or groundVector.
Fire startup is 1 through 120 ticks, recovery 1 through 240, shots per reload 1 through 24, and reload 1 through 600.
Air fall/recoil speeds are 0 through 30 m/s. The reload clip is listed after the stage's first sorted clip.
Its projectile is non-returning with positive speed, radius, and lifetime, and has no homing or perch block.

### Command grab

<a id="constraint-command-grab"></a>

Use one tick-zero meleeCombo lifecycle with no aim, slide carry, charge pool, or tuck policy.
Only presentation operations accompany the lifecycle in its first stage.
Lunge ticks are 0 through 60, speed 0 through 60 m/s, and stop distance 0 through 5 m.
Per-tick lunge distance cannot exceed twice the capture radius.

Capture is a root-relative sphere or capsule, with no bones, facing-owner restriction, knockback, stun, or nonzero hit group.
Radius is above zero through 3 m and capture damage is nonnegative.
The capture opens at tick 1 through 120 and lasts 1 through 30 ticks.
Release follows the whole capture window and is at most tick 240.
An authored anchor distance is 0 through 3 m.

Launch damage is above zero through 100, pitch -90 through 90, base and growth nonnegative, and stun gate 0 through 240.
A launching release has zero release-lock ticks. A release without launch instead needs release lock 1 through 60 ticks.
Whiff recovery is at most 120 ticks.
With zero whiff recovery, one stage must contain release; any IASA is strictly after release.
With nonzero whiff recovery, there are two stages. The first ends exactly at capture close with IASA disabled.
Release is strictly inside the second stage, measured from activation, and its IASA cannot precede release.

### Dash strike and flash cut

<a id="constraint-dash-strike"></a>

Dash strike uses one tick-zero meleeCombo stage, no aim, IASA, carry, or charge pool, and only presentation companions.
Distance is above zero through 12 m, speed above zero through 60 m/s, and recovery 1 through 120 ticks.
Travel must fit an unsigned 16-bit tick count.
The light hitbox is a root-relative sphere; the tip is root-relative. Both author hit group zero.
The light cut has zero base/growth and positive fixed hitstun. Per-tick travel cannot exceed twice its radius.
Travel plus tip lifetime plus recovery must fit the stage. Tip pose tick plus tip lifetime plus recovery must also fit.

<a id="constraint-flash-cut"></a>

Flash cut has the same one-stage tick-zero meleeCombo, no-aim/IASA/carry/stock restrictions and presentation-only companions.
Stance, sheathe, recovery, and whiff are 1 through 120 ticks. Legs are 1 through 30 ticks.
Reveal lead is 1 through sheathe ticks. Lock range is above zero through 20 m, pass-through 0 through 3 m, and target count 1 through 7.
`maxTargets × legTicks + sheatheTicks` is at most 240 ticks.
Its cut is a root sphere with group zero, positive damage, nonzero stun, and a positive base or growth launch term.
`(2 × lockRange + passThroughDistance) / legTicks` cannot exceed twice the cut radius.
The stage covers stance plus the longer of whiff or maximum legs/sheathe/recovery. Phase clips are listed in the stage and cues are declared.

### Targeted leap, steered charge, and thrust

<a id="constraint-targeted-leap"></a>

Targeted leap uses one tick-zero meleeCombo stage with no aim or IASA and presentation-only companions.
Rise height is above zero through 30 m. Maximum range is above zero through 60 m and fallback distance is between zero and that range.
Rise, hang, and drop are each 1 through 240 ticks. Fall timeout is 1 through 600.
Hit recovery is positive; miss recovery is at least hit recovery and at most 600.
The landing hitbox lifetime is positive and fits hit recovery. All ordinary hitbox constraints still apply.
Its optional nonempty presentation IDs begin with `presentation.` and are declared. Phase clips and timing must fit the stage watchdog.

<a id="constraint-steered-charge"></a>

Steered charge has one lifecycle in one stage with IASA disabled.
It admits presentation cues and startup correction, gravity, or armor that all end before charge start.
It rejects competing motion, aim changes, hitbox/projectile spawns, explicit completion, or another lifecycle.
Gallop speed and gallop/hit-ending/miss-ending ticks are positive. Turn rate is 0 through 1440 degrees/s.
Skid fits the miss ending. A gallop clip requires positive loop ticks.
Tracking rates are 0 through 1440 degrees/s; challenged rear ticks are 1 through 120.
Carry distance/height are 0 through 3 m; carry and fling durations are 1 through 120 ticks.
Fling damage is above zero through 100, pitch -90 through 90, base/growth nonnegative, and stun gate at most 240.
Chest geometry is root-relative with group zero. Lance damage is positive and chest damage equals it.
Assist range is above zero through 10 m, cone above zero through 90 degrees, and rate above zero through 1440 degrees/s.
Every nonempty phase clip is listed in the stage and sorts after the primary startup clip.

<a id="constraint-thrust"></a>

Thrust uses one tick-zero meleeCombo stage with no aim; presentation and armor may accompany it. IASA is allowed.
Target cone is 0 through 180 degrees and zero for stick aim. Neutral and stick pitch are -90 through 90 degrees.
Speed satisfies `0 < speed <= maxSpeed <= 60 m/s`; acceleration is 0 through 240 m/s².
Thrust ticks are 1 through 600 and fit the stage. An optional trail is root-relative with group zero.

### Tether

<a id="constraint-tether"></a>

Tether uses one tick-zero meleeCombo stage with no aim or IASA and presentation-only companions. It cannot tuck.
Throw tick is 1 through 120. Reel speed is above zero through 60 m/s, anchor distance 0 through 3 m, arrival stun 1 through 240 ticks,
maximum reel 1 through 600, miss recovery 0 through 240, and airborne gravity scale 0 through 1.

A thrown tether requires its projectile and positive retract speed at most 60 m/s.
Ring speed is above zero through 60 m/s, radius above zero through 3 m, and lifetime 1 through 240 ticks.
It has zero gravity and yaw offset, no return, no knockback or stun, and nonnegative damage.

<a id="constraint-placed-tether"></a>

With `atTarget`, omit projectile, retract speed, retract clip, and retract cue.
Warning ticks are 1 through 120 and catch ticks 1 through 60.
Catch radius is above zero through 3 m; height is at least twice radius and at most 6 m. Damage is 0 through 100.
Range is above zero through 32 m, with fallback distance between zero and that range.
The optional unmarked branch has rise tick 1 through 120, active ticks 1 through 60, recovery 0 through 240, and distance 0 through 6 m.
Its radius/height obey the same shape limits. Damage is above zero through 100, pitch -90 through 90, base/growth nonnegative, and stun gate 0 through 240.
Nonempty tether phase clips sort after the stage primary clip and cues are declared.

### Shell, cannonball, and forms

<a id="constraint-shell"></a>

The shell block exists with the shell-stance requirement and a nonempty shell animation role.
Pop-out deceleration is 0 through 600 m/s² and blocked recoil speed 0 through 30 m/s.
A splash exists exactly when a move tucks; a ride exists exactly when a move rides.
Only specials can tuck or ride. A tuck move has no slide carry or preserve-momentum flag.
At most one special button, with either or both variants, owns ride policy.
A ride move has one stage, no IASA, and only presentation, startup correction, or gravity-window operations.

Splash geometry is root-relative with group zero and pop-up lag 1 through 240 ticks.
Ride duration is 1 through 600 ticks, speed above zero through 60 m/s, and turn rate 0 through 720 degrees/s.
Wave geometry is root-relative with group zero and zero stun/fixed-hitstun.

<a id="constraint-cannonball"></a>

Cannonball uses one tick-zero meleeCombo stage with no aim or IASA, only presentation companions, and tuck policy.
Takeoff tick is 0 through 60. Distances satisfy `0 < noTargetDistance <= maxRange <= 30 m`.
Flight bounds satisfy `2 <= minFlightTicks <= maxFlightTicks <= 240`.
Maximum launch speed is above zero through 60 m/s. The stage outlasts takeoff plus the longest flight.

<a id="constraint-forms"></a>

The forms requirement and alternateForm block exist together. The alternate form has its own complete canonical grid and positive air-dodge speed.
Its nonempty role IDs are lowercase. An empty role falls back to form zero on the same rig.
A move may swap form at most once and only in a fighter with the forms feature.

### Returning and perching projectiles

<a id="constraint-projectile-return"></a>

A slot may spawn at most one returning projectile. Outbound ticks are positive and strictly below projectile lifetime.
Return speed and acceleration are positive, projectile gravity is exactly zero, self-hit damage is nonnegative, and self-hit stun is 1 through 240 ticks.
Return cues are declared and use the `presentation.` prefix. Optional return-leg damage is 0 through 100, pitch -90 through 90,
base/growth nonnegative, stun gate 0 through 240, and fixed hitstun 0 through 240 with a positive stun gate when overridden.

<a id="constraint-projectile-perch"></a>

Homing turn is above zero through 90 degrees per tick and requires positive projectile speed.
A perch cannot return. It requires an alternate form; contact itself has zero damage, base, growth, and stun.
Perch duration is 1 through 1200 ticks. Pop damage is above zero through 100, pitch -90 through 90, base/growth nonnegative, and stun gate 0 through 240.
Peck interval is positive and no longer than the perch duration. Peck damage is an integer 1 through 20.
A diving perch has no homing block and zero projectile gravity. Rise speed and dive speed are above zero through 60 m/s.
Hover height is above zero through 12 m, rise and mark are 1 through 240 ticks each, and fallback distance is 0 through 20 m.
Projectile lifetime outlasts rise plus mark. A projectile with homing or perch has challenge ticks at most 600.

## Fighter items and portable bindings

The first private fighter item was published in unit 5 on 2026-11-13. Published items keep their declared schema and root sampling rule.
The authoritative contract is [ADR-0065](../adr/0065-workshop-item-format.md#portable-bindings).

A fighter is a flat schema-2 `item.json` folder with type `fighter`, `itemVersion`, creator,
license, attribution, AI flag, `compilerVersion`, `canonicalWriterVersion`, and a filename-to-SHA-256
`payloads` map. Required files are the four cooked files, one lowercase `.glb`, editable
`package.json` and `character.json`, and `card.json`. Optional files are `humanoid.json` and
`.png` or `.jpg` images. Card descriptions, WIP flags and Tutorial hints live only in `card.json`.
Display names come from the cooked definition.

Schema-3 portable bindings have these fields.

| Fields | Value |
| --- | --- |
| `packageId`, `sourceHash` | Package ID and framed hash of the two source files |
| `catalogSchemaVersion`, `bindingSchemaVersion` | Both 3 |
| `poseFormat`, `poseVersion`, `sampleRate`, `rootRuleVersion` | `SKEL`, 1, 60, 1 |
| `payloads`, `modelFile` | All non-cooked file hashes and the GLB role |
| `portraits` | Nullable image filename per form |
| `icons` | Per-form maps of exactly the runtime slots' canonical icon IDs to image or null |
| `animations` | Semantic ID, pose track, frame count, and GLB/shared source |
| `presentations` | Every declared ID bound to explicit none or an engine presentation |

Icon IDs are `icon.1`, `icon.2`, `icon.3`, `icon.a`, `icon.e`, `icon.r`, and `icon.f`.
A source is `{"kind":"glb","clipName":"..."}` or
`{"kind":"shared","id":"slop.anim.idle.default.v1","version":"1.0.0"}`.
A presentation is `{"kind":"none"}` or `{"kind":"engine","vfx":null,"audio":null,"projectile":null}`
with at least one non-null role. VFX/projectile roles are `slop.vfx.*` references; audio has
`selection` and `clips` with `slop.sfx.*` references. Fixed pools have one clip and cycle pools
have one to sixteen. Registry references use lowercase dot segments ending `.vN` and exact numeric
`N.MINOR.PATCH` versions. Missing registries refuse required references; no registry fallback exists.

Root and sampling rule 1 is defined in
[ADR-0065](../adr/0065-workshop-item-format.md#motion-sampling-and-the-pose-bake-root-rule-1).
A bound GLB clip records `extras.slop.durationSecondsBits`, `extras.slop.frameCount` (equal to
`max(1, ceil(binary32(durationSeconds * 60)))`) and optionally `extras.slop.loop`. Its keys sit on
60 Hz ticks from clipStart, one per tick. A gap is allowed only between equal LINEAR keys or after a
STEP key, and CUBICSPLINE is refused. Export from Blender at 60 fps with sampled animation. The rest
skeleton is stood upright, its hips are put on the model origin and `visualScale` applies. Set
`modelYOffset` so that the feet meet the capsule bottom. An attachment point such as `_weapon_tip`
is baked from the GLB node with the same name. Admission re-bakes every bound clip and requires
`poses.bin` to be byte-equal, or fails with `item.pose-bake.mismatch`. The shared-clip table is
empty, so shared references fail with `portable.shared.unsupported`. `PortableFighterCook.Cook`
writes a verified item from source, card and GLB.

The source hash frames `package.json` then `character.json`, each with little-endian int32
filename byte length, UTF-8 filename, int32 content byte length, and exact content bytes.
Admission verifies this hash but does not recompile source. The cooked artifact is authoritative
within the loader's compatibility window. Outside it, `item.needs-update` names the recorded
compiler and canonical writer versions. `LoadPortable` rounds decimal rationals once to binary32,
ties to even; `CompilePortable` normalizes negative zero in both definitions and canonical bytes.
Legacy parsers and writers are unchanged.

Filenames have at most 128 ASCII letters, digits, dots, hyphens or underscores. Leading dots,
reserved device stems and case aliases fail. There are at most 40 payloads, with an additional
exact Steam metadata limit below 5,000 bytes. The complete item is capped at 64 MiB. GLB is capped
at 30,000,000 bytes, images at 4 MiB, source/card files at 2 MiB, humanoid.json at 65,536 bytes,
and cooked files at the [existing admission limits](../systems/cooked-package-admission.md).
Reads require regular files, refuse symbolic links/reparse points, FIFOs and devices before
reading, and allow hard links whose actual bytes match the hashes. JPEGs require one frame header.

The model-swap library reports "type not supported here" for a fighter item. Unit 5 will wire the
fighter adapter to discovery. No product path currently admits external fighters.
See `tools/PortableCookProof/README.md` for the rerunnable isolated Win64/Mono comparison.

## Cook a fighter in the installed game

Home's **Workshop** screen has a **Make a fighter** tab. Its workspace folder is
`Workshop/Fighters` under the game's persistent data directory. Each subfolder is
one fighter source. **Open workspaces folder** opens that location. A typed path
can point to a source folder elsewhere. Controller users select a listed folder or
use the text field's Steam keyboard action.

A source folder contains `package.json`, `character.json`, `card.json`, one GLB,
and `cook.json`. The package ID starts with `local.`. Optional `humanoid.json`
maps the rig. PNG and JPEG files can accompany the source.

`cook.json` records the model filename, rights information, and each semantic
animation's GLB clip name. For example:

```json
{
  "model": "fighter.glb",
  "creator": "Your name",
  "license": "CC0-1.0",
  "attribution": "Original work",
  "aiAssisted": false,
  "animations": { "anim.idle": "Idle", "anim.run": "Run" }
}
```

Every animation referenced by the kit needs an entry. Clip metadata follows the
portable motion contract above, including `extras.slop`. The cook does not invent
missing clips or metadata.

**Use this folder** starts watching the files. After the files remain unchanged for
750 ms, the game compiles the kit, measures the idle mesh, fits hurtbox capsules,
bakes poses, writes a schema-2 item under `Workshop/FighterCooks`, and admits it.
The work runs off the menu thread. **Cook again** requests another cook even when
no file changed. A save during cooking invalidates that result.

Diagnostics show the severity, code, file, exact field path, and message. Compiler
messages match KitCheck. Missing clips identify the semantic binding in `cook.json`.
The cooked item's `character.json` contains the fitted capsules. The editable
workspace keeps its authored capsule rows.

The game suggests `modelYOffset` from the lowest idle foot vertex and the capsule
bottom. **Apply suggested modelYOffset** writes that value to the workspace's
`character.json`, which triggers another cook. A newer edit prevents that write.
Capsule radii and endpoints are computed automatically from the idle mesh. Mesh
fitting supports up to four skin influences per vertex and no morph targets. The
game does not infer movement speeds, damage, shield size, or attack timing.

**Try in Training** becomes available after admission. It starts player 1 as that
exact fighter revision against a built-in CPU. Returning to Home reopens the
workspace. Cooking another revision cannot change a running Match. Latest admitted cooks also
appear at Fighter Select for Brawl and Training. Online play remains unavailable for made fighters.

Optional `cook.json` fields `portraits` and `icons` use the portable bindings shapes.
`portraits` is one image filename or null per form. `icons` is one map per form from
canonical icon IDs to image filenames or null. Referenced images live directly in
the source folder. Omitted art uses the made fighter's rendered portrait and neutral
move glyphs.

### Workspace saves and fitted parts

Only the model, portraits, icons, and bone map named by `cook.json` become payloads.
The standard optional bone map is `humanoid.json`. Unreferenced images, including
notes and work-in-progress renders, are ignored. The bone-map limit is the same
64 KiB used by item admission.

Rigid meshes parented under mapped joints participate in fitting and sole measurement.
This includes helmets, held props, and shoes. A mesh outside the mapped skeleton
produces `mesh.fit.detached`; it is not silently counted as body geometry.
Torso geometry beyond half the shoulder width is capped with `mesh.fit.loose-torso`.
Capes, coats, skirts, and torso-weighted tails are therefore reported rather than
silently enlarging the torso. Inspect those warnings in Training.

Each workspace keeps its latest cooked revision and any revision pinned by Training.
Cooks publish complete temporary folders atomically. A failed revision stays on screen
until the source changes or **Cook again** requests another attempt. Requests made
during a cook queue another attempt. Periodic source checks continue if file watching
fails or misses an event.

Applying the sole suggestion changes only the `presentation.modelYOffset` number.
The game preserves the remaining bytes, checks for a newer save, and replaces the
file atomically. A conflicting save is refused rather than silently overwritten.


### Publish a cooked fighter

In **Make a fighter**, choose a workspace and wait for an admitted cook. Choose **Publish latest cook**,
review the title, description and visibility, and accept the content guidelines. Private is the default.
The portrait becomes the Steam preview. The installed game publishes only through store app 5324910.
The Playtest app 5325920 displays an unavailable message.

Steam assigns the published ID before upload. The game recooks the verified item as
`workshop.<PublishedFileId>` and admits that result before uploading. The workspace keeps its
`local.*` ID. Publishing again updates the saved Steam item. Unedited title, description and visibility
retain their values from Steam. Portrait and tag updates are sent only when changed in the game.
A retry after a failed first upload sends the complete title, description and visibility again.
If the saved item was deleted or belongs to someone else, choose **Publish as a new item**.
The portrait keeps its aspect and sRGB colour. Publishing pins the admitted cook shown by the view.
If Steam requests its legal agreement, the item page opens.

Steam subscriptions download automatically while Steam is available. Admission checks the Steam
install record ID, payload hashes, limits and exact pose bake. A refusal appears in the Workshop
workspace status list and never becomes a Fighter Select card. A new revision replaces the menu
entry while Matches retain the revision they started with. Verification from the Published list
requires subscribing separately before the published fighter appears at Fighter Select.

The local library lists only workspace paths that still exist. After moving a workspace, cook its new
path. If two existing workspaces have the same `local.*` ID, the status names both and identifies the
`package.json.packageId` to rename. A refresh publishes its complete snapshot before releasing old
presentation. Restricted modes validate every active seat again when resolving the Match.
