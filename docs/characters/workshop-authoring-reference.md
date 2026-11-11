# Write a Workshop fighter kit

A kit is the gameplay source for a fighter. It has two UTF-8 JSON files in one folder.
`package.json` names the package and its rights. `character.json` defines its body, movement, and moves.
The compiler turns these files into deterministic gameplay data. The game runs that data at 60 ticks per second.
A successful check validates gameplay source. It does not supply a model or bind animation clips.

## Check a kit

Run this from the repository root. The .NET 8 SDK must already be installed. Unity is not needed.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- docs/characters/examples/workshop-kit
```

Replace the last argument with your source folder. Success prints `COMPILE OK (0 diagnostics)`.
Errors include a diagnostic code, a file and property path, and the compiler's explanation.
An array index starts at zero. Exit code 1 means compilation failed. Exit code 2 means an invocation or file error.
The command does not modify the source folder. Unknown fields are errors. JSON comments and trailing commas are errors.

## Make a fighter

1. Copy [the complete worked example](examples/workshop-kit/character.json) and its [manifest](examples/workshop-kit/package.json) into a new folder.
2. Change the package ID, display name, creator, license, and attribution to describe your work.
3. Adjust movement and body dimensions. The template values are a starting point, not roster tuning.
4. Edit one move's stage. A stage is a timed part of a move. An operation changes state at a tick in that stage.
5. Run the checker after each edit. Fix every diagnostic before adding another move.

You can also create the compiler's authoring-ready template without the Editor.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- template /tmp/my-fighter local.my-fighter 'My fighter'
```

The template has animation names and empty moves. It compiles but does not attack.
The worked example has a normal hit, a lunge, a projectile, an armored attack, and a public recovery capability.
Its grounded and aerial entries are explicit. Its animation names are placeholders awaiting asset assembly.

`iasaTicks` is the earliest cancel time. Zero disables early cancellation. `landingLagTicks` controls recovery on landing.
Auto-cancel windows waive that lag before or after their boundaries. Keep zero values until you want those windows.
`cooldownTicks` delays reuse. `isRecoveryMove` marks a move that spends the fighter's recovery availability.

## Units and coordinates

One tick is 1/60 second. Tick zero is the start of a stage. Hitstop freezes the move clock.
Distances are world meters. Speeds are meters per second. Accelerations are meters per second squared.
Angles use degrees. Positive launch angles lift the target. Damage adds percent. Weight affects knockback.
A hitbox is an attack volume. A hurtbox is the fighter's vulnerable volume. A capsule has two endpoints and a radius.

Root-relative attack offsets use local X sideways, Y up, and Z forward along facing.
Bone-relative hitbox offsets use the baked bone points. Use the semantic bone identifiers below.
A hurtbox bone can also name a rig-specific point with `rig:` followed by the exact rig name.
Declare custom attachment points in `attachmentBoneIds`. These names start with an underscore.
`setVelocity` uses world X/Y/Z. `absolute` replaces velocity, `additive` adds it, and `vertical` replaces only Y.
`forwardLunge` follows facing. Projectile launch offsets follow facing and `yawOffsetDegrees` turns the shot around Y.

A zero `hitGroup` gives each hitbox its own hit history. Equal nonzero groups share one hit per victim in an activation.
A hitbox's `stunTicks` gates stun. `fixedHitstunTicks` overrides its duration when nonzero.
An animation ID is a semantic name beginning with `anim.`. The gameplay checker does not prove that a clip exists.
`behavior` selects the engine's move lifecycle. It does not restrict a melee move to hitboxes.
`aimMode` selects aiming input. `aimMovement` controls whether an aiming fighter can move while holding aim.
Use the worked example's behavior and aim settings for ordinary fixed timelines.

## Fit the body and model

`capsuleHeight` and `capsuleRadius` describe movement collision. Simulation Y is the capsule center.
`hipHeight` is the standing hip pivot above the capsule's bottom. A baked bone's Y becomes
`capsuleCenterY - capsuleHeight / 2 + hipHeight + boneLocalY`.
Set hip height from the standing hip position of the scaled model. It is not an extra model offset.

`visualScale` multiplies the rendered model. Divide the desired standing model height by its unscaled standing height.
`modelYOffset` shifts the model relative to its rendering pivot. Inspect idle frame zero with the clips you will actually use.
Set the offset so the soles meet the floor. A feet-root model and a hips-root animation need different offsets.
Do not assume that `-capsuleHeight / 2` fits both. Recheck the floor contact after changing clips or scale.
`hurtboxBoneScale` scales sampled bone geometry. Check collision against the same scaled model.
`modelSoleOffset` is a separate presentation adjustment. `autoModelYOffset` is serialized metadata, not a promise of automatic fitting.

The proof used visual scale 1.8 for a model about 0.99 m tall. Its final model offset was -0.03 with shared clips.
Those values describe that proof, not defaults for another rig. Fit your own rig and clip convention.

## Not defined yet

A portable Workshop format for binding a GLB clip or shared animation to an animation ID is not defined yet.
The built-in Unity catalog binds clips and deterministic pose tracks today. It is not a portable Workshop binding file.
There is no Editor-free contact-frame discovery command here. Choose gameplay ticks explicitly, then verify them against the bound motion during assembly.
A successful source compile does not validate animation bindings or pose tracks.

Portable effect and sound registries, projectile presentation bindings, portraits, menu registration, and model-root import conventions are not defined yet.
`presentationIds` declares semantic cues. It does not provide their visual or audio assets.
`displayName` supplies gameplay metadata. It does not install a fighter in a menu.
The proof's `animations.json`, borrowed roster selector, and symbolic `slop.anim.*` bindings were temporary experiments.
Do not publish them as a supported format. See the [Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md).

<!-- BEGIN GENERATED CONTRACT -->

## Generated contract

Generated by `dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- reference --write .`. Do not edit this section.

### Canonical move grid

`ground.1`, `ground.2`, `ground.3`, `ground.A`, `ground.E`, `ground.R`, `ground.F`, `air.1`, `air.2`, `air.3`, `air.A`, `air.E`, `air.R`, `air.F`.

### Public capabilities

Declare each used capability in `capabilityRequirements`. Its ID and version must also match the `startCapability` operation. Forms and shell stance also govern document-level data.

| ID | Version |
| --- | --- |
| `slop.ability.aimed-recovery.v1` | `1` |
| `slop.ability.charged-projectile.v1` | `1` |
| `slop.ability.charged-stream.v1` | `1` |
| `slop.ability.finger-guns.v1` | `1` |
| `slop.ability.dash-strike.v1` | `1` |
| `slop.ability.flash-cut.v1` | `1` |
| `slop.ability.targeted-leap.v2` | `2` |
| `slop.ability.steered-charge.v2` | `2` |
| `slop.ability.shell-stance.v2` | `1` |
| `slop.ability.cannonball.v1` | `1` |
| `slop.ability.thrust.v1` | `1` |
| `slop.ability.tether.v1` | `1` |
| `slop.ability.command-grab.v1` | `1` |
| `slop.ability.forms.v1` | `1` |

### Budgets

Every operation consumes one operation entry, including completion and presentation. Aliases expand before counting slots and stages. Nested capability hitboxes and projectiles count through their capability's cooking rules.

| Limit | Value |
| --- | --- |
| MaxSlots | 16 |
| MaxStagesPerTimeline | 8 |
| MaxOperationsPerStage | 64 |
| MaxOperationsPerTimeline | 256 |
| MaxCapabilityRequirements | 32 |

### Source objects and fields

Each table lists the accepted keys for an object. Objects and arrays are required unless the table says otherwise. `null` is distinct from an omitted key. Optional strings accept null. Required fields have no default.

Numbers use finite 32-bit floating point. Unsigned 16-bit integers are 0 through 65535. Bytes are 0 through 255. Further constraints appear below. A default shown as an enum name uses the corresponding JSON token in the enum table.

#### Manifest object "manifest"

| Field | Type | Omission/default |
| --- | --- | --- |
| `manifestSchemaVersion` | unsigned 16-bit integer | required |
| `packageId` | string | required |
| `version` | string | required |
| `creator` | string | required |
| `license` | string | required |
| `attribution` | string | required |
| `dependencies` | array | required |
| `schemaVersion` | object; see matching object table | forbidden; see diagnostics |
| `id` | object; see matching object table | forbidden; see diagnostics |
| `class` | object; see matching object table | forbidden; see diagnostics |

#### Manifest object $"manifest.dependencies[{i}]"

| Field | Type | Omission/default |
| --- | --- | --- |
| `packageId` | string | required |
| `version` | string | required |
| `cookedHash` | string | required |

#### Character object "character"

| Field | Type | Omission/default |
| --- | --- | --- |
| `authoringSchemaVersion` | unsigned 16-bit integer | required |
| `displayName` | string | required |
| `weight` | number | required |
| `movement` | object; see matching object table | required |
| `presentation` | object; see matching object table | required |
| `capsuleRadius` | number | required |
| `capsuleHeight` | number | required |
| `hipHeight` | number | required |
| `hurtboxRadius` | number | required |
| `shieldRadius` | number | required |
| `captureGeometry` | object; see matching object table | required |
| `hurtboxCapsules` | array | required |
| `hurtboxBoneDefs` | array | required |
| `attachmentBoneIds` | string array | required |
| `presentationIds` | string array | required |
| `capabilityRequirements` | array | required |
| `shell` | string | "" |
| `slots` | array | required |
| `aliases` | array | required |
| `alternateForm` | object; see matching object table | null |
| `schemaVersion` | object; see matching object table | forbidden; see diagnostics |
| `id` | string | forbidden; see diagnostics |
| `class` | object; see matching object table | forbidden; see diagnostics |

#### AlternateForm object "alternateForm"

| Field | Type | Omission/default |
| --- | --- | --- |
| `id` | string | required |
| `displayName` | string | required |
| `movement` | object; see matching object table | required |
| `presentation` | object; see matching object table | required |
| `slots` | array | required |
| `aliases` | array | required |

#### AlternateForm object "presentation"

| Field | Type | Omission/default |
| --- | --- | --- |
| `idle` | string | "" |
| `run` | string | "" |
| `dash` | string | "" |
| `jump` | string | "" |
| `fall` | string | "" |
| `hitSmall` | string | "" |
| `hitMedium` | string | "" |
| `hitHard` | string | "" |
| `tumble` | string | "" |
| `shield` | string | "" |
| `grab` | string | "" |
| `grabbed` | string | "" |
| `throwForward` | string | "" |
| `airDodge` | string | "" |

#### Shell object "shell"

| Field | Type | Omission/default |
| --- | --- | --- |
| `popOutDeceleration` | number | required |
| `blockedRecoilSpeed` | number | required |
| `splash` | object; see matching object table | omitted |
| `ride` | object; see matching object table | omitted |

#### Shell object "splash"

| Field | Type | Omission/default |
| --- | --- | --- |
| `hitbox` | object; see matching object table | required |
| `popUpTicks` | unsigned 16-bit integer | required |
| `presentationId` | string | null (some roles normalize to empty string) |

#### Shell object "ride"

| Field | Type | Omission/default |
| --- | --- | --- |
| `ticks` | unsigned 16-bit integer | required |
| `speed` | number | required |
| `turnDegreesPerSecond` | number | required |
| `wave` | object; see matching object table | required |
| `hitPresentationId` | string | null (some roles normalize to empty string) |

#### Movement object "movement"

| Field | Type | Omission/default |
| --- | --- | --- |
| `runSpeed` | number | required |
| `runAccelerationA` | number | required |
| `runAccelerationB` | number | required |
| `dashSpeed` | number | required |
| `airDodgeSpeed` | number | required |
| `airSpeedMax` | number | required |
| `airAccelStick` | number | required |
| `airAccelBase` | number | required |
| `jumpForce` | number | required |
| `shortHopForce` | number | required |
| `airJumpVMultiplier` | number | required |
| `airJumpHMultiplier` | number | required |
| `gravity` | number | required |
| `airFloatGravity` | number | required |
| `dashDurationTicks` | unsigned 16-bit integer | required |
| `dashCooldownTicks` | unsigned 16-bit integer | required |
| `groundFriction` | number | required |
| `airFriction` | number | required |
| `maxFallSpeed` | number | required |
| `fastFallSpeed` | number | required |
| `maxJumps` | byte | required |
| `jumpSquatTicks` | unsigned 16-bit integer | required |
| `floatWindowTicks` | unsigned 16-bit integer | required |
| `rushTicks` | unsigned 16-bit integer | required |

#### Presentation object "presentation"

| Field | Type | Omission/default |
| --- | --- | --- |
| `idle` | string | required |
| `run` | string | required |
| `dash` | string | required |
| `jump` | string | required |
| `fall` | string | required |
| `hitSmall` | string | required |
| `hitMedium` | string | required |
| `hitHard` | string | required |
| `tumble` | string | "" |
| `crouch` | string | "" |
| `slide` | string | "" |
| `shield` | string | "" |
| `grab` | string | "" |
| `grabbed` | string | "" |
| `throwForward` | string | "" |
| `airDodge` | string | "" |
| `shell` | string | "" |
| `flying` | string | "" |
| `mayhemLaugh` | string | "" |
| `wallJump` | string | "" |
| `landStartOffsetSeconds` | number | required |
| `modelResourcePath` | string | required |
| `visualScale` | number | required |
| `hurtboxBoneScale` | number | required |
| `modelYOffset` | number | required |
| `modelSoleOffset` | number | required |
| `autoModelYOffset` | boolean | required |

#### CaptureGeometry object "captureGeometry"

| Field | Type | Omission/default |
| --- | --- | --- |
| `reach` | number | required |
| `width` | number | required |
| `height` | number | required |
| `offsetY` | number | required |
| `attackerAnchor` | object; see matching object table | required |
| `victimAnchor` | object; see matching object table | required |

#### CaptureAnchor object name

| Field | Type | Omission/default |
| --- | --- | --- |
| `x` | number | required |
| `y` | number | required |
| `z` | number | required |

#### Capsules object $"character.hurtboxCapsules[{i}]"

| Field | Type | Omission/default |
| --- | --- | --- |
| `startX` | number | required |
| `startY` | number | required |
| `startZ` | number | required |
| `endX` | number | required |
| `endY` | number | required |
| `endZ` | number | required |
| `radius` | number | required |

#### Bones object $"character.hurtboxBoneDefs[{i}]"

| Field | Type | Omission/default |
| --- | --- | --- |
| `boneId` | string | required |
| `offsetX` | number | required |
| `offsetY` | number | required |
| `offsetZ` | number | required |
| `radius` | number | required |
| `endBoneId` | string | null (some roles normalize to empty string) |
| `endOffsetX` | number | 0 |
| `endOffsetY` | number | 0 |
| `endOffsetZ` | number | 0 |

#### Capabilities object $"character.capabilityRequirements[{i}]"

| Field | Type | Omission/default |
| --- | --- | --- |
| `capabilityId` | string | required |
| `capabilityVersion` | string | required |

#### Aliases object $"{path}.aliases[{i}]"

| Field | Type | Omission/default |
| --- | --- | --- |
| `from` | string | required |
| `to` | string | required |

#### Slots object path

| Field | Type | Omission/default |
| --- | --- | --- |
| `id` | string | required |
| `name` | string | required |
| `description` | string | required |
| `iconId` | string | required |
| `behavior` | enum string | required |
| `aimMode` | enum string | required |
| `aimMovement` | enum string | AuthoringAimMovementMode.Fixed |
| `aimMovementSpeedScale` | number | 1f |
| `aimAnimationId` | string | null (some roles normalize to empty string) |
| `hitPresentationId` | string | null (some roles normalize to empty string) |
| `cooldownTicks` | unsigned 16-bit integer | required |
| `isRecoveryMove` | boolean | required |
| `preserveMomentumOnStart` | boolean | required |
| `allowSlideCarry` | boolean | false |
| `shellPolicy` | enum string | ShellPolicy.PopOut |
| `chargePool` | object; see matching object table | null |
| `timeline` | object; see matching object table | required |

#### ChargePool object path

| Field | Type | Omission/default |
| --- | --- | --- |
| `maxCharges` | signed 32-bit integer | required |
| `regenTicks` | unsigned 16-bit integer | required |

#### Timeline object "timeline"

| Field | Type | Omission/default |
| --- | --- | --- |
| `stages` | array | required |

#### Timeline object stagePath

| Field | Type | Omission/default |
| --- | --- | --- |
| `durationTicks` | unsigned 16-bit integer | required |
| `iasaTicks` | unsigned 16-bit integer | required |
| `landingLagTicks` | unsigned 16-bit integer | required |
| `autoCancelBeforeTicks` | unsigned 16-bit integer | required |
| `autoCancelAfterTicks` | unsigned 16-bit integer | required |
| `attackRange` | number | 0 |
| `warpRange` | number | 0 |
| `useTargetLock` | boolean | false |
| `rotateTowardTarget` | boolean | false |
| `trackingStrength` | number | 0 |
| `animationIds` | string array | required |
| `operations` | array | required |

#### Placement object path + ".placement"

| Field | Type | Omission/default |
| --- | --- | --- |
| `attachmentMode` | enum string | AuthoringPresentationAttachmentMode.World |
| `boneId` | string | null (some roles normalize to empty string) |
| `localPositionX` | number | 0 |
| `localPositionY` | number | 0 |
| `localPositionZ` | number | 0 |
| `localRotationX` | number | 0 |
| `localRotationY` | number | 0 |
| `localRotationZ` | number | 0 |
| `localScaleX` | number | 1f |
| `localScaleY` | number | 1f |
| `localScaleZ` | number | 1f |
| `durationTicks` | unsigned 16-bit integer | 28 |

#### Hitbox object "hitbox"

| Field | Type | Omission/default |
| --- | --- | --- |
| `shape` | enum string | required |
| `radius` | number | required |
| `offsetX` | number | required |
| `offsetY` | number | required |
| `offsetZ` | number | required |
| `endOffsetX` | number | required |
| `endOffsetY` | number | required |
| `endOffsetZ` | number | required |
| `startBoneId` | string | null (some roles normalize to empty string) |
| `endBoneId` | string | null (some roles normalize to empty string) |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |
| `durationTicks` | unsigned 16-bit integer | required |
| `interruptible` | boolean | required |
| `hitGroup` | byte | required |
| `knockbackDirection` | enum string | AuthoringKnockbackDirection.AwayFromOwner |
| `fixedHitstunTicks` | unsigned 16-bit integer | 0 |
| `hitPresentationId` | string | null (some roles normalize to empty string) |
| `requiresFacingOwner` | boolean | false |

#### Projectile object "projectile"

| Field | Type | Omission/default |
| --- | --- | --- |
| `launchOffsetX` | number | required |
| `launchOffsetY` | number | required |
| `launchOffsetZ` | number | required |
| `speed` | number | required |
| `gravity` | number | required |
| `radius` | number | required |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |
| `maxFlightTicks` | unsigned 16-bit integer | required |
| `yawOffsetDegrees` | number | required |
| `return` | object; see matching object table | omitted |
| `homing` | object; see matching object table | required |
| `perch` | object; see matching object table | required |
| `challengeTicks` | unsigned 16-bit integer | 0 |

#### Projectile object "return"

| Field | Type | Omission/default |
| --- | --- | --- |
| `outboundTicks` | unsigned 16-bit integer | required |
| `returnSpeed` | number | required |
| `returnAcceleration` | number | required |
| `selfHitDamage` | number | required |
| `selfHitStunTicks` | unsigned 16-bit integer | required |
| `catchPresentationId` | string | "" |
| `selfHitPresentationId` | string | "" |
| `hit` | object; see matching object table | required |

#### LegHit object "hit"

| Field | Type | Omission/default |
| --- | --- | --- |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |
| `fixedHitstunTicks` | unsigned 16-bit integer | 0 |

#### Homing object "homing"

| Field | Type | Omission/default |
| --- | --- | --- |
| `turnDegreesPerTick` | number | required |

#### Perch object "perch"

| Field | Type | Omission/default |
| --- | --- | --- |
| `perchTicks` | unsigned 16-bit integer | required |
| `pop` | object; see matching object table | required |
| `dive` | object; see matching object table | omitted |
| `peck` | object; see matching object table | omitted |
| `scatterPresentationId` | string | "" |

#### Perch object "pop"

| Field | Type | Omission/default |
| --- | --- | --- |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |
| `presentationId` | string | "" |

#### Perch object "dive"

| Field | Type | Omission/default |
| --- | --- | --- |
| `hoverHeight` | number | required |
| `maxRiseTicks` | unsigned 16-bit integer | required |
| `markTicks` | unsigned 16-bit integer | required |
| `diveSpeed` | number | required |
| `noTargetDistance` | number | required |

#### Perch object "peck"

| Field | Type | Omission/default |
| --- | --- | --- |
| `intervalTicks` | unsigned 16-bit integer | required |
| `damage` | number | required |
| `presentationId` | string | "" |

#### CapabilityParameters object path + ".parameters"

| Field | Type | Omission/default |
| --- | --- | --- |
| `kind` | object; see matching object table | required |
| `character.alternateForm` | object; see matching object table | required |
| `.operations[` | object; see matching object table | required |
| `]` | object; see matching object table | required |
| `.kind` | object; see matching object table | required |
| `setVelocity` | object; see matching object table | required |
| `tick` | object; see matching object table | required |
| `unit` | object; see matching object table | required |
| `velocityMode` | object; see matching object table | required |
| `x` | object; see matching object table | required |
| `y` | object; see matching object table | required |
| `z` | object; see matching object table | required |
| `forwardLunge` | object; see matching object table | required |
| `speed` | number | required |
| `durationTicks` | unsigned 16-bit integer | required |
| `stopInAttackRange` | object; see matching object table | required |
| `gravityWindow` | object; see matching object table | required |
| `gravityScale` | object; see matching object table | required |
| `armorWindow` | object; see matching object table | required |
| `spawnHitbox` | object; see matching object table | required |
| `hitbox` | object; see matching object table | required |
| `spawnProjectile` | object; see matching object table | required |
| `projectile` | object; see matching object table | required |
| `setAimState` | object; see matching object table | required |
| `aimState` | object; see matching object table | required |
| `startCapability` | object; see matching object table | required |
| `capabilityId` | string | required |
| `capabilityVersion` | object; see matching object table | required |
| `parameters` | object; see matching object table | required |
| `emitPresentation` | object; see matching object table | required |
| `presentationId` | string | "" |
| `placement` | object; see matching object table | required |
| `condition` | object; see matching object table | required |
| `completeTimeline` | object; see matching object table | required |
| `swapForm` | object; see matching object table | required |
| `startupAimCorrection` | object; see matching object table | required |
| `endTick` | object; see matching object table | required |
| `acquisitionRange` | object; see matching object table | required |
| `acquisitionHalfAngleDegrees` | object; see matching object table | required |
| `maxYawDegrees` | object; see matching object table | required |
| `maxPitchDegrees` | object; see matching object table | required |
| `yawDegreesPerSecond` | object; see matching object table | required |
| `pitchDegreesPerSecond` | object; see matching object table | required |

#### CapabilityParameters object "launch"

| Field | Type | Omission/default |
| --- | --- | --- |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |

#### CapabilityParameters object hp

| Field | Type | Omission/default |
| --- | --- | --- |
| `startTicks` | unsigned 16-bit integer | required |
| `contactTicks` | unsigned 16-bit integer | required |
| `forwardDistance` | number | required |
| `arcHeight` | number | required |
| `landingHeight` | number | required |

#### Tether object "atTarget"

| Field | Type | Omission/default |
| --- | --- | --- |
| `warningTicks` | unsigned 16-bit integer | required |
| `catchTicks` | unsigned 16-bit integer | required |
| `radius` | number | required |
| `height` | number | required |
| `damage` | number | required |
| `maxRange` | number | required |
| `noTargetDistance` | number | required |
| `warningPresentationId` | string | "" |
| `catchPresentationId` | string | "" |
| `unmarked` | object; see matching object table | null |

#### Tether object "unmarked"

| Field | Type | Omission/default |
| --- | --- | --- |
| `riseTick` | unsigned 16-bit integer | required |
| `activeTicks` | unsigned 16-bit integer | required |
| `distance` | number | required |
| `radius` | number | required |
| `height` | number | required |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |
| `recoveryTicks` | unsigned 16-bit integer | required |
| `presentationId` | string | "" |

#### ChargeCarry object "carry"

| Field | Type | Omission/default |
| --- | --- | --- |
| `anchorDistance` | number | required |
| `anchorHeight` | number | required |
| `maxCarryTicks` | unsigned 16-bit integer | required |
| `fling` | object; see matching object table | required |
| `flingTicks` | unsigned 16-bit integer | required |
| `flingAnimationId` | string | "" |
| `scoopPresentationId` | string | "" |
| `flingPresentationId` | string | "" |

#### ChargeCarry object "fling"

| Field | Type | Omission/default |
| --- | --- | --- |
| `damage` | number | required |
| `angle` | number | required |
| `baseKnockback` | number | required |
| `knockbackGrowth` | number | required |
| `stunTicks` | unsigned 16-bit integer | required |

#### ChargeAssist object "assist"

| Field | Type | Omission/default |
| --- | --- | --- |
| `assistRange` | number | required |
| `assistHalfAngleDegrees` | number | required |
| `assistDegreesPerSecond` | number | required |

### Operation kinds

The common fields are `kind` (string), `tick` (stage-relative unsigned 16-bit integer), and `unit` (string). Each is required. Same-tick operations run in array order. A tick must be inside its stage.

#### `setVelocity`

Unit is `metersPerSecond`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `velocityMode` | enum string | required |
| `x` | number | required |
| `y` | number | required |
| `z` | number | required |

#### `forwardLunge`

Unit is `metersPerSecond`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `speed` | number | required |
| `durationTicks` | unsigned 16-bit integer | required |
| `stopInAttackRange` | boolean | false |

#### `gravityWindow`

Unit is `normalized`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `gravityScale` | number | required |
| `durationTicks` | unsigned 16-bit integer | required |

#### `armorWindow`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `durationTicks` | unsigned 16-bit integer | required |

#### `spawnHitbox`

Unit is `meters`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `hitbox` | object; see matching object table | required |

#### `spawnProjectile`

Unit is `meters`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `projectile` | object; see matching object table | required |

#### `setAimState`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `aimState` | enum string | required |

#### `startCapability`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `capabilityId` | string | required |
| `capabilityVersion` | string | required |
| `parameters` | object; see matching object table | required |

#### `emitPresentation`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `presentationId` | string | required |
| `placement` | object; see matching object table | required |
| `condition` | enum string | AuthoringPresentationCondition.Always |

#### `completeTimeline`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |

#### `swapForm`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |

#### `startupAimCorrection`

Unit is `ticks`. Budget cost is one operation entry.

| Field | Type | Omission/default |
| --- | --- | --- |
| `endTick` | unsigned 16-bit integer | required |
| `acquisitionRange` | number | required |
| `acquisitionHalfAngleDegrees` | number | required |
| `maxYawDegrees` | number | required |
| `maxPitchDegrees` | number | required |
| `yawDegreesPerSecond` | number | required |
| `pitchDegreesPerSecond` | number | required |

### Capability parameter objects

#### `slop.ability.aimed-recovery.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `startupTicks` | unsigned 16-bit integer | required |
| `verticalSpeed` | number | required |
| `directionalVerticalSpeed` | number | required |
| `horizontalSpeed` | number | required |
| `preparationRiseSpeed` | number | required |
| `hop` | object; see matching object table | omitted |
| `riseSteerAcceleration` | number | 0f |

#### `slop.ability.charged-projectile.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `maxChargeTicks` | unsigned 16-bit integer | required |
| `tier2Ticks` | unsigned 16-bit integer | required |
| `tier3Ticks` | unsigned 16-bit integer | required |
| `fireTriggerTick` | unsigned 16-bit integer | required |
| `tier2Radius` | number | required |
| `tier3Radius` | number | required |
| `projectile` | object; see matching object table | required |

#### `slop.ability.charged-stream.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `maxChargeTicks` | unsigned 16-bit integer | required |
| `fireTriggerTick` | unsigned 16-bit integer | required |
| `emitIntervalTicks` | unsigned 16-bit integer | required |
| `minStreamTicks` | unsigned 16-bit integer | required |
| `maxStreamTicks` | unsigned 16-bit integer | required |
| `recoveryTicks` | unsigned 16-bit integer | required |
| `maxBaseKnockback` | number | required |
| `minRecoilSpeed` | number | required |
| `maxRecoilSpeed` | number | required |
| `projectile` | object; see matching object table | required |
| `sprayAnimationId` | string | "" |
| `sprayLoopTicks` | unsigned 16-bit integer | (ushort)0 |
| `endAnimationId` | string | "" |

#### `slop.ability.finger-guns.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `fireTriggerTick` | unsigned 16-bit integer | required |
| `recoveryTicks` | unsigned 16-bit integer | required |
| `shotsPerReload` | byte | required |
| `reloadTicks` | unsigned 16-bit integer | required |
| `reloadAnimationId` | string | required |
| `airMaxFallSpeed` | number | required |
| `airRecoilSpeed` | number | required |
| `projectile` | object; see matching object table | required |

#### `slop.ability.dash-strike.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `distance` | number | required |
| `speed` | number | required |
| `tipPoseTick` | unsigned 16-bit integer | required |
| `recoveryTicks` | unsigned 16-bit integer | required |
| `lightHitbox` | object; see matching object table | required |
| `tipHitbox` | object; see matching object table | required |
| `tipPresentationId` | string | required |

#### `slop.ability.flash-cut.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `stanceTicks` | unsigned 16-bit integer | required |
| `lockRange` | number | required |
| `maxTargets` | unsigned 16-bit integer | required |
| `legTicks` | unsigned 16-bit integer | required |
| `passThroughDistance` | number | required |
| `sheatheTicks` | unsigned 16-bit integer | required |
| `revealLeadTicks` | unsigned 16-bit integer | required |
| `recoveryTicks` | unsigned 16-bit integer | required |
| `whiffTicks` | unsigned 16-bit integer | required |
| `cutHitbox` | object; see matching object table | required |
| `stanceAnimationId` | string | required |
| `legAnimationId` | string | required |
| `sheatheAnimationId` | string | required |
| `lockPresentationId` | string | required |
| `revealPresentationId` | string | required |
| `sheathePresentationId` | string | required |
| `popPresentationId` | string | required |

#### `slop.ability.targeted-leap.v2` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `riseTicks` | unsigned 16-bit integer | required |
| `riseHeight` | number | required |
| `hangTicks` | unsigned 16-bit integer | required |
| `dropTicks` | unsigned 16-bit integer | required |
| `maxRange` | number | required |
| `noTargetDistance` | number | required |
| `maxFallTicks` | unsigned 16-bit integer | required |
| `hitRecoveryTicks` | unsigned 16-bit integer | required |
| `missRecoveryTicks` | unsigned 16-bit integer | required |
| `hitbox` | object; see matching object table | required |
| `riseAnimationId` | string | "" |
| `hangAnimationId` | string | "" |
| `hangPoseTick` | unsigned 16-bit integer | 0 |
| `dropAnimationId` | string | "" |
| `landingAnimationId` | string | "" |
| `hitLandingSeekTick` | unsigned 16-bit integer | 0 |
| `risePresentationId` | string | "" |
| `hangPresentationId` | string | "" |
| `lockPresentationId` | string | "" |
| `landingPresentationId` | string | "" |

#### `slop.ability.steered-charge.v2` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `gallopSpeed` | number | required |
| `turnDegreesPerSecond` | number | required |
| `maxGallopTicks` | unsigned 16-bit integer | required |
| `armored` | boolean | required |
| `lanceHitbox` | object; see matching object table | required |
| `hitEndingTicks` | unsigned 16-bit integer | required |
| `missEndingTicks` | unsigned 16-bit integer | required |
| `skidTicks` | unsigned 16-bit integer | required |
| `turnAroundOnMiss` | boolean | required |
| `airStrideTicks` | unsigned 16-bit integer | required |
| `lookDownTicks` | unsigned 16-bit integer | required |
| `challengedRearTicks` | unsigned 16-bit integer | required |
| `trackDegreesPerSecond` | number | required |
| `challengedTrackDegreesPerSecond` | number | required |
| `chestHitbox` | object; see matching object table | new Dictionary<string, JsonElement>() |
| `carry` | object; see matching object table | required |
| `assist` | object; see matching object table | null |
| `challengePresentationId` | string | "" |
| `gallopAnimationId` | string | "" |
| `gallopLoopTicks` | unsigned 16-bit integer | 0 |
| `hitEndingAnimationId` | string | "" |
| `missEndingAnimationId` | string | "" |
| `airEndingAnimationId` | string | "" |
| `gallopPresentationId` | string | "" |
| `hitEndingPresentationId` | string | "" |
| `missEndingPresentationId` | string | "" |
| `airEndingPresentationId` | string | "" |

#### `slop.ability.thrust.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `aim` | enum string | required |
| `targetConeDegrees` | number | 0 |
| `neutralPitchDegrees` | number | required |
| `stickPitchDegrees` | number | required |
| `speed` | number | required |
| `acceleration` | number | required |
| `maxSpeed` | number | required |
| `keepIncomingSpeed` | boolean | required |
| `thrustTicks` | unsigned 16-bit integer | required |
| `trailHitbox` | object; see matching object table | null |

#### `slop.ability.tether.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `throwTick` | unsigned 16-bit integer | required |
| `projectile` | object; see matching object table | required |
| `reelSpeed` | number | required |
| `anchorDistance` | number | required |
| `arrivalStunTicks` | unsigned 16-bit integer | required |
| `maxReelTicks` | unsigned 16-bit integer | required |
| `retractSpeed` | number | 0f |
| `missRecoveryTicks` | unsigned 16-bit integer | required |
| `airGravityScale` | number | required |
| `reelAnimationId` | string | "" |
| `retractAnimationId` | string | "" |
| `throwPresentationId` | string | "" |
| `retractPresentationId` | string | "" |
| `atTarget` | object; see matching object table | omitted |

#### `slop.ability.command-grab.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `lungeTicks` | unsigned 16-bit integer | required |
| `lungeSpeed` | number | required |
| `stopDistance` | number | required |
| `captureTick` | unsigned 16-bit integer | required |
| `capture` | object; see matching object table | required |
| `releaseTick` | unsigned 16-bit integer | required |
| `launch` | object; see matching object table | omitted |
| `catchPresentationId` | string | "" |
| `releasePresentationId` | string | "" |
| `level` | boolean | false |
| `anchorDistance` | number | null |
| `releaseLockTicks` | unsigned 16-bit integer | 0 |
| `whiffRecoveryTicks` | unsigned 16-bit integer | 0 |

#### `slop.ability.cannonball.v1` parameters

| Field | Type | Omission/default |
| --- | --- | --- |
| `takeoffTick` | unsigned 16-bit integer | required |
| `maxRange` | number | required |
| `noTargetDistance` | number | required |
| `minFlightTicks` | unsigned 16-bit integer | required |
| `maxFlightTicks` | unsigned 16-bit integer | required |
| `maxLaunchSpeed` | number | required |
| `launchPresentationId` | string | "" |

`slop.ability.shell-stance.v2` has no supported `startCapability` parameter object. It declares a document-level feature.

`slop.ability.forms.v1` has no supported `startCapability` parameter object. It declares a document-level feature.

### Enum tokens

| Enum | Allowed strings |
| --- | --- |
| Cannonball |  |
| Unmarked |  |
| PresentationAttachmentMode | `world`, `bone` |
| PresentationCondition | `always`, `sweetspotHit` |
| Behavior | `meleeCombo`, `chargeAttack`, `aimedProjectile`, `projectile`, `airGroundProjectile`, `selfBuff`, `areaDenial`, `directionalDash` |
| AimMode | `none`, `groundCursor`, `cameraForward3D`, `groundVector` |
| AimMovement | `fixed`, `mobile` |
| Shape | `sphere`, `capsule` |
| KnockbackDirection | `awayFromOwner`, `towardOwner` |
| VelocityMode | `absolute`, `additive`, `vertical` |
| ShellPolicy | `popOut`, `tuck`, `ride` |

### Bone identifiers

`bone.head`, `bone.spine2`, `bone.hips`, `bone.right-hand`, `bone.left-hand`, `bone.right-foot`, `bone.left-foot`, `bone.right-toes`, `bone.left-toes`, `bone.neck`, `bone.right-upper-arm`, `bone.left-upper-arm`, `bone.right-lower-arm`, `bone.left-lower-arm`, `bone.right-upper-leg`, `bone.left-upper-leg`, `bone.right-lower-leg`, `bone.left-lower-leg`, `bone.right-hand-end`, `bone.left-hand-end`, `bone.spine`.

### Validation rules and diagnostics

These rules come directly from validation statements. The condition is the rejected case. `&&` means both, `||` means either, `!` means not, and `x is > a and <= b` means a < x ≤ b. Names with capital letters refer to the matching camelCase JSON fields. Index variables mean the current array entry. Helper predicates below state their accepted case.

| Code | Rejected condition or context | Message |
| --- | --- | --- |
| `"schema.missing"` | `packageJson == null` | Package manifest JSON is null. |
| `"schema.missing"` | `characterJson == null` | Character JSON is null. |
| `"schema.invalid-json"` | `Load` | ex.Message |
| `"schema.invalid"` | `Load` | ex.Message |
| `"schema.unsupported"` | `p.ContainsKey("schemaVersion")` | Legacy schemaVersion is not accepted. |
| `"source.identity-forbidden"` | `p.ContainsKey("id")` | Package identity belongs in package.json fields. |
| `"source.class-forbidden"` | `p.ContainsKey("class")` | Character class is not part of the source contract. |
| `"schema.unsupported"` | `version != ManifestSchemaVersion` | Only manifest schema version 1 is supported. |
| `"value.out-of-range"` | `dependencyElement.ValueKind != JsonValueKind.Array` | Dependencies must be an array. |
| `"schema.missing"` | `p.TryGetValue("dependencies", out var dependencyElement)` | Required property is missing. |
| `"schema.unsupported"` | `p.ContainsKey("schemaVersion")` | Legacy schemaVersion is not accepted. |
| `"source.identity-forbidden"` | `p.ContainsKey("id")` | Character identity belongs in package.json. |
| `"source.class-forbidden"` | `p.ContainsKey("class")` | Character class is not part of the source contract. |
| `"schema.unsupported"` | `version != AuthoringSchemaVersion` | Only authoring schema version 4 is supported. Schema 3 numbered the normals 1, 3 and 4; `python3 tools/slot_renumber.py --packages-only` migrates it. |
| `"schema.missing"` | `!p.ContainsKey("maxCharges")` | Required integer is missing. |
| `"value.out-of-range"` | `!max.TryGetInt32(out _)` | 32-bit integer is required. |
| `"operation.unknown"` | `default:` | Unknown timeline operation. |
| `"operation.parameter-unknown"` | `!allowed.Contains(name, StringComparer.Ordinal)` | Field is not valid for this operation. |
| `"operation.parameter-missing"` | `!properties.ContainsKey(name) && !(kind == "emitPresentation" && name is "placement" or "condition")                 && !(kind == "forwardLunge" && name == "stopInAttackRange")` | Field is required for this operation. |
| `"operation.parameter-missing"` | `parent.TryGetValue(name, out var hitbox)` | Hitbox is required. |
| `"capability.retired"` | `RetiredCapabilities.TryGetReason(id, out var retiredReason)` | retiredReason |
| `"operation.parameter-missing"` | `!parent.TryGetValue("parameters", out var element)` | Capability parameters are required. |
| `"operation.parameter-missing"` | `required is not ("hop" or "riseSteerAcceleration") && !p.ContainsKey(required)` | Required capability parameter is missing. |
| `"operation.parameter-missing"` | `!string.Equals(required, "explosionPresentationId", StringComparison.Ordinal) && !string.Equals(required, "launchPresentationId", StringComparison.Ordinal) && !string.Equals(required, "landingPresentationId", StringComparison.Ordinal) && !string.Equals(required, "minimumChargeTicks", StringComparison.Ordinal) && !string.Equals(required, "chargeTiers", StringComparison.Ordinal) && !string.Equals(required, "sprayAnimationId", StringComparison.Ordinal) && !string.Equals(required, "sprayLoopTicks", StringComparison.Ordinal) && !string.Equals(required, "endAnimationId", StringComparison.Ordinal) && System.Array.IndexOf(CycloneKickOptional, required) < 0 && !p.ContainsKey(required)` | Required capability parameter is missing. |
| `"capability.unknown"` | `ParseCapabilityParameters` | Capability parameters are not supported. |
| `"operation.parameter-unknown"` | `p.ContainsKey(thrown)` | A placed catch (atTarget) throws no ring. |
| `"operation.parameter-missing"` | `!p.ContainsKey(required)` | Required capability parameter is missing. |
| `"value.out-of-range"` | `element.ValueKind != JsonValueKind.Object` | Object is required. |
| `"field.unknown"` | `!set.Contains(property.Name)` | Unknown field. |
| `"field.duplicate"` | `!result.TryAdd(property.Name, property.Value)` | Duplicate field. |
| `"value.out-of-range"` | `element.ValueKind != JsonValueKind.Object` | Object is required. |
| `unknownCode` | `!set.Contains(property.Name)` | Unknown capability parameter. |
| `"field.duplicate"` | `!result.TryAdd(property.Name, property.Value)` | Duplicate field. |
| `"schema.missing"` | `MissingObject` | Required object is missing. |
| `"schema.missing"` | `!parent.TryGetValue(name, out value)` | Required array is missing. |
| `"value.out-of-range"` | `value.ValueKind != JsonValueKind.Array` | Array is required. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.String` | String is required. |
| `"schema.missing"` | `!p.TryGetValue(name, out var x)` | Required string is missing. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.String` | String is required. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.Number &#124;&#124; !x.TryGetSingle(out var n)` | Finite number is required. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.Number &#124;&#124; !x.TryGetUInt16(out var n)` | Unsigned 16-bit integer is required. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.True && x.ValueKind != JsonValueKind.False` | Boolean is required. |
| `"schema.missing"` | `!p.TryGetValue(name, out var x)` | Required number is missing. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.Number &#124;&#124; !x.TryGetSingle(out var n)` | Finite number is required. |
| `"schema.missing"` | `!p.TryGetValue(name, out var x)` | Required integer is missing. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.Number &#124;&#124; !x.TryGetUInt16(out var n)` | Unsigned 16-bit integer is required. |
| `"schema.missing"` | `!p.TryGetValue(name, out var x)` | Required integer is missing. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.Number &#124;&#124; !x.TryGetByte(out var n)` | Unsigned 8-bit integer is required. |
| `"schema.missing"` | `!p.TryGetValue(name, out var x)` | Required boolean is missing. |
| `"value.out-of-range"` | `x.ValueKind != JsonValueKind.True && x.ValueKind != JsonValueKind.False` | Boolean is required. |
| `"schema.missing"` | `!p.TryGetValue(name, out var element)` | Required enum is missing. |
| `"enum.unknown"` | `element.ValueKind != JsonValueKind.String` | Enum must be a string token. |
| `"enum.unknown"` | `!parsed.HasValue` | Unknown enum value. |
| `"enum.unknown"` | `element.ValueKind != JsonValueKind.String` | Enum must be a string token. |
| `"enum.unknown"` | `!parsed.HasValue` | Unknown enum value. |
| `"schema.missing"` | `!p.TryGetValue(name, out var element)` | Required unit is missing. |
| `"unit.unknown"` | `element.ValueKind != JsonValueKind.String` | Unit must be a string token. |
| `"unit.unknown"` | `UnknownUnit` | Unknown unit. |
| `"schema.missing"` | `source == null` | Character package source is null. |
| `"schema.invalid"` | `Compile` | ex.Message |
| `"schema.missing"` | `m == null &#124;&#124; c == null` | Manifest and character are required. |
| `"schema.unsupported"` | `m.ManifestSchemaVersion != ManifestSchemaVersion` | Only manifest schema version 1 is supported. |
| `"schema.unsupported"` | `c.AuthoringSchemaVersion == 3` | Authoring schema 3 numbers the normals 1, 3 and 4. Run `python3 tools/slot_renumber.py --packages-only` to move them to 1, 2 and 3. |
| `"schema.unsupported"` | `c.AuthoringSchemaVersion != AuthoringSchemaVersion` | Only authoring schema version 4 is supported. |
| `"value.out-of-range"` | `string.IsNullOrWhiteSpace(m.Version) &#124;&#124; !IsSemVer(m.Version)` | Version must be SemVer 2.0 text. |
| `"value.out-of-range"` | `string.IsNullOrWhiteSpace(field.Item1)` | Value must be non-empty. |
| `"value.out-of-range"` | `string.IsNullOrWhiteSpace(dependency.Version) &#124;&#124; string.IsNullOrWhiteSpace(dependency.CookedHash)` | Dependency version and cooked hash are required. |
| `"id.duplicate"` | `!capabilityMap.TryAdd(requirement.CapabilityId, requirement.CapabilityVersion)` | Duplicate capability requirement. |
| `"capability.retired"` | `RetiredCapabilities.TryGetReason(requirement.CapabilityId, out var retiredReason)` | retiredReason |
| `"capability.untrusted"` | `profile == CharacterCookProfile.Workshop` | Trusted built-in capabilities are not allowed in Workshop profile. |
| `"capability.unknown"` | `!IsTrustedCapability(requirement.CapabilityId, requirement.CapabilityVersion)` | Capability is not admitted by the trusted profile. |
| `"capability.version-mismatch"` | `requirement.CapabilityVersion != supportedVersion` | Capability version is not supported. |
| `"capability.unknown"` | `requirement.CapabilityId == AimedRecoveryCapabilityId &#124;&#124;                      requirement.CapabilityId == TargetedLeapCapabilityId &#124;&#124;                      requirement.CapabilityId == DashStrikeCapabilityId &#124;&#124;                      requirement.CapabilityId == FlashCutCapabilityId &#124;&#124;                      requirement.CapabilityId == ChargedProjectileCapabilityId &#124;&#124;                      requirement.CapabilityId == ChargedStreamCapabilityId &#124;&#124;                      requirement.CapabilityId == FingerGunsCapabilityId &#124;&#124;                      requirement.CapabilityId == SteeredChargeCapabilityId &#124;&#124;                      requirement.CapabilityId == ShellStanceCapabilityId &#124;&#124;                      requirement.CapabilityId == ThrustCapabilityId &#124;&#124;                      requirement.CapabilityId == TetherCapabilityId &#124;&#124;                      requirement.CapabilityId == CommandGrabCapabilityId &#124;&#124;                      requirement.CapabilityId == CannonballCapabilityId &#124;&#124;                      requirement.CapabilityId == FormsCapabilityId` | Capability is not admitted by this profile. |
| `"budget.exceeded"` | `capabilityCount > CookedBudget.MaxCapabilityRequirements` | Capability requirement budget exceeded. |
| `"value.out-of-range"` | `c.ShieldRadius <= 0f &#124;&#124; c.ShieldRadius <= c.CapsuleHeight * 0.5f` | Shield radius must be positive and greater than half capsule height. |
| `"capability.ambiguous"` | `ShellPairingError(c.Shell?.Splash != null, c.Shell?.Ride != null,                 resolved.Select(pair => (pair.Key, pair.Value.ShellPolicy))) is { } pairing` | pairing |
| `"capability.ambiguous"` | `op.Parameters is SteeredChargeCapabilityParameters or TargetedLeapCapabilityParameters                     && BonkSpecialContract.Slot(slot, op) is { } slotError` | slotError |
| `"value.out-of-range"` | `FormsBoundsError(definition) is { } formsError` | formsError |
| `"id.invalid"` | `!CanonicalSlots.Contains(slot.Id)` | Unknown canonical slot ID. |
| `"id.duplicate"` | `!explicitSlots.TryAdd(slot.Id, slot)` | Duplicate explicit slot. |
| `"id.invalid"` | `!CanonicalSlots.Contains(alias.From) &#124;&#124; !CanonicalSlots.Contains(alias.To)` | Alias IDs must be canonical slot IDs. |
| `"id.duplicate"` | `explicitSlots.ContainsKey(alias.From)` | Alias overwrites an explicit slot. |
| `"id.duplicate"` | `!aliases.TryAdd(alias.From, alias.To)` | Duplicate alias. |
| `"reference.unresolved"` | `resolved.Count != CanonicalSlots.Length` | Not all canonical slots resolve. |
| `"reference.unresolved"` | `!c.PresentationIds.Contains(slot.HitPresentationId, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"budget.exceeded"` | `slot.Timeline.Stages.Count == 0 &#124;&#124; slot.Timeline.Stages.Count > CookedBudget.MaxStagesPerTimeline` | Timeline stage budget exceeded or empty. |
| `"value.out-of-range"` | `slot.ChargePool.MaxCharges is < 1 or > 3` | Max charges must be between one and three. |
| `"value.out-of-range"` | `slot.ChargePool.RegenTicks == 0` | Regen ticks must be positive. |
| `"capability.ambiguous"` | `recoveries.Length != 1 &#124;&#124; slot.Timeline.Stages.Count != 1 &#124;&#124; stage.IasaTicks != 0                 &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.MeleeCombo &#124;&#124; slot.AimMode != AuthoringAimMode.None                 &#124;&#124; !slot.IsRecoveryMove &#124;&#124; recoveries[0].Tick != 0                 &#124;&#124; recoveries[0].Parameters is not AimedRecoveryCapabilityParameters                 &#124;&#124; stage.Operations.Any(op => op != recoveries[0] && op is not EmitPresentationOperationSource && op is not SpawnHitboxOperationSource)` | Aimed recovery requires one tick-zero lifecycle in one recovery stage, no aim or IASA, and only presentation or hitbox operations alongside it. |
| `"value.out-of-range"` | `recoveries[0].Parameters is AimedRecoveryCapabilityParameters recovery && recovery.StartupTicks >= stage.DurationTicks` | Aimed recovery must launch before its stage ends. |
| `"capability.ambiguous"` | `chargedProjectiles.Length != 1 &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.ChargeAttack &#124;&#124;                 slot.Timeline.Stages.Count != 1 &#124;&#124; slot.Timeline.Stages[0].IasaTicks != 0` | Charged projectile requires one chargeAttack lifecycle in one stage with IASA disabled. |
| `"value.out-of-range"` | `projectile.Tick != 0 &#124;&#124; projectile.Parameters is not ChargedProjectileCapabilityParameters charge &#124;&#124;                     charge.FireTriggerTick >= slot.Timeline.Stages[0].DurationTicks` | Charged projectile must start at tick zero and fire before its release stage ends. |
| `"capability.ambiguous"` | `slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation =>                 operation is SpawnProjectileOperationSource or SpawnHitboxOperationSource or SetAimStateOperationSource                     or SetVelocityOperationSource or ForwardLungeOperationSource &#124;&#124;                 operation is StartCapabilityOperationSource capability && capability.CapabilityId != ChargedProjectileCapabilityId)` | Charged projectile cannot share a slot with competing motion, aim, projectile, or lifecycle operations. |
| `"capability.ambiguous"` | `chargedStreams.Length != 1 &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.ChargeAttack &#124;&#124;                 slot.Timeline.Stages.Count != 1 &#124;&#124; slot.Timeline.Stages[0].IasaTicks != 0` | Charged stream requires one chargeAttack lifecycle in one stage with IASA disabled. |
| `"value.out-of-range"` | `stream.Tick != 0 &#124;&#124; stream.Parameters is not ChargedStreamCapabilityParameters p` | Charged stream must start at tick zero. |
| `"reference.unresolved"` | `p.SprayAnimationId.Length > 0 && !PhaseClip(p.SprayAnimationId)                     &#124;&#124; p.EndAnimationId.Length > 0 && !PhaseClip(p.EndAnimationId)` | The charged stream's spray and end clips must be listed in its stage and sort after its first clip (name them after it). |
| `"value.out-of-range"` | `p.SprayLoopTicks > 0 && p.SprayAnimationId.Length == 0` | A charged stream loops only its own spray clip: set sprayAnimationId with sprayLoopTicks. |
| `"capability.ambiguous"` | `slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation =>                 operation is SpawnProjectileOperationSource or SpawnHitboxOperationSource or SetAimStateOperationSource                     or SetVelocityOperationSource or ForwardLungeOperationSource &#124;&#124;                 operation is StartCapabilityOperationSource capability && capability.CapabilityId != ChargedStreamCapabilityId)` | Charged stream cannot share a slot with competing motion, aim, projectile, or lifecycle operations. |
| `"capability.ambiguous"` | `fingerGuns.Length != 1 &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.MeleeCombo &#124;&#124;                 slot.Timeline.Stages.Count != 1 &#124;&#124; stage.IasaTicks != 0 &#124;&#124; slot.ChargePool != null &#124;&#124;                 slot.AllowSlideCarry &#124;&#124;                 slot.AimMode is not (AuthoringAimMode.None or AuthoringAimMode.CameraForward3D or AuthoringAimMode.GroundVector) &#124;&#124;                 stage.Operations.Any(operation => operation != fingerGuns[0] && operation is not EmitPresentationOperationSource)` | Finger Guns requires one tick-zero meleeCombo lifecycle, no IASA, slide carry or charge pool, and only presentation operations alongside it. |
| `"value.out-of-range"` | `guns.Tick != 0 &#124;&#124; guns.Parameters is not FingerGunsCapabilityParameters p` | Finger Guns must start at tick zero. |
| `"reference.unresolved"` | `string.IsNullOrEmpty(p.ReloadAnimationId) &#124;&#124;                     stage.AnimationIds.OrderBy(id => id, StringComparer.Ordinal).ToList().IndexOf(p.ReloadAnimationId) <= 0` | The reload clip must be listed in the stage and sort after its first clip. |
| `"capability.ambiguous"` | `targetedLeapCount > 1` | A slot may contain only one targeted-leap lifecycle. |
| `"capability.ambiguous"` | `DashStrikeContract.SlotError(dashStrikes.Length, slot.Timeline.Stages.Count, dashStrikes[0].Tick,                 slot.Behavior, slot.AimMode, stage.IasaTicks,                 stage.Operations.Any(x => x != dashStrikes[0] && x is not EmitPresentationOperationSource),                 slot.AllowSlideCarry, slot.ChargePool != null, stage.DurationTicks,                 (CookedDashStrikeCapabilityParameters)CookParameters(dashStrike)) is { } error` | error |
| `"capability.ambiguous"` | `FlashCutContract.SlotError(flashCuts.Length, slot.Timeline.Stages.Count, flashCuts[0].Tick,                 slot.Behavior, slot.AimMode, stage.IasaTicks,                 stage.Operations.Any(x => x != flashCuts[0] && x is not EmitPresentationOperationSource),                 slot.AllowSlideCarry, slot.ChargePool != null, stage.DurationTicks, stage.AnimationIds,                 (CookedFlashCutCapabilityParameters)CookParameters(flashCut)) is { } error` | error |
| `"capability.ambiguous"` | `slot.Timeline.Stages.SelectMany(stage => stage.Operations)             .Count(operation => operation is SpawnProjectileOperationSource { Projectile.Return: not null }) > 1` | A slot may throw only one returning projectile: its slot waits for it to come back. |
| `"capability.ambiguous"` | `CommandGrabContract.SlotError(commandGrabs.Length, stages.Count, commandGrabs[0].Tick,                     slot.Behavior, slot.AimMode, slot.ShellPolicy, slot.AllowSlideCarry, slot.ChargePool != null,                     !first.Operations.Contains(commandGrabs[0])                         &#124;&#124; first.Operations.Any(x => x != commandGrabs[0] && x is not EmitPresentationOperationSource),                     first.DurationTicks, first.IasaTicks, hold?.DurationTicks ?? 0, hold?.IasaTicks ?? 0,                     (CookedCommandGrabCapabilityParameters)CookParameters(commandGrab)) is { } error` | error |
| `"capability.ambiguous"` | `CannonballSlotError(cannonballs.Length, slot.Timeline.Stages.Count, cannonballs[0].Tick, slot.Behavior,                     slot.AimMode, stage.IasaTicks, slot.ShellPolicy, stage.DurationTicks,                     stage.Operations.Any(operation => operation != cannonballs[0] && operation is not EmitPresentationOperationSource),                     cannonballs[0].Parameters is CannonballCapabilityParameters x ? (CookedCannonballCapabilityParameters)CookParameters(x) : null)                 is { } cannonballError` | cannonballError |
| `"capability.ambiguous"` | `slot.ShellPolicy == ShellPolicy.Ride && RideSlotError(slot.Timeline.Stages.Count,                 slot.Timeline.Stages.Count == 0 ? (ushort)0 : slot.Timeline.Stages[0].IasaTicks,                 slot.Timeline.Stages.SelectMany(stage => stage.Operations).Any(operation => operation is not                     (EmitPresentationOperationSource or StartupAimCorrectionOperationSource or GravityWindowOperationSource)))             is { } rideError` | rideError |
| `"value.out-of-range"` | `stage.DurationTicks == 0 &#124;&#124; stage.IasaTicks > stage.DurationTicks &#124;&#124; stage.LandingLagTicks > stage.DurationTicks &#124;&#124; stage.AutoCancelBeforeTicks > stage.DurationTicks &#124;&#124; stage.AutoCancelAfterTicks > stage.DurationTicks` | Stage timing is outside its duration. |
| `"value.non-finite"` | `float.IsNaN(stage.AttackRange) &#124;&#124; float.IsInfinity(stage.AttackRange) &#124;&#124; float.IsNaN(stage.WarpRange) &#124;&#124; float.IsInfinity(stage.WarpRange) &#124;&#124; float.IsNaN(stage.TrackingStrength) &#124;&#124; float.IsInfinity(stage.TrackingStrength)` | Targeting metadata must be finite. |
| `"value.out-of-range"` | `stage.AttackRange < 0f` | Attack range must be non-negative. |
| `"value.out-of-range"` | `stage.WarpRange < 0f` | Warp range must be non-negative. |
| `"value.out-of-range"` | `stage.TrackingStrength < 0f &#124;&#124; stage.TrackingStrength > 1f` | Tracking strength must be between 0 and 1. |
| `"timeline.targeting.retired"` | `stage.WarpRange != 0f &#124;&#124; stage.UseTargetLock &#124;&#124; stage.RotateTowardTarget &#124;&#124; stage.TrackingStrength != 0f` | Automatic warp and per-stage target tracking are retired. Use authored movement and startup aim correction. |
| `"budget.exceeded"` | `stage.Operations.Count > CookedBudget.MaxOperationsPerStage` | Stage operation budget exceeded. |
| `"operation.forward-lunge.no-subsequent-hitbox"` | `!stage.Operations.Any(operation =>                     operation is SpawnHitboxOperationSource hitbox && hitbox.Tick > lunge.Tick)` | A forward lunge that stops in attack range requires a subsequent spawnHitbox in the same stage. |
| `"reference.unresolved"` | `!IsKnownAnimation(id, c)` | Animation ID is not declared. |
| `"value.out-of-range"` | `operation.Tick >= stage.DurationTicks` | Operation tick must be within its stage. |
| `"value.out-of-range"` | `operation is ForwardLungeOperationSource lunge &&                     (int)lunge.Tick + lunge.DurationTicks > stage.DurationTicks` | Forward lunge must end within its stage. |
| `"value.out-of-range"` | `operation is GravityWindowOperationSource gravity &&                     (int)gravity.Tick + gravity.DurationTicks > stage.DurationTicks` | Gravity window must end within its stage. |
| `"value.out-of-range"` | `operation is ArmorWindowOperationSource armorWindow &&                     (int)armorWindow.Tick + armorWindow.DurationTicks > stage.DurationTicks` | Armor window must end within its stage. |
| `"capability.unknown"` | `!capabilities.TryGetValue(capability.CapabilityId, out var version)` | Capability is not declared. |
| `"capability.version-mismatch"` | `version != capability.CapabilityVersion` | Capability version does not match its declaration. |
| `"capability.ambiguous"` | `charges.Length != 1 &#124;&#124; slot.Timeline.Stages.Count != 1 &#124;&#124; slot.Timeline.Stages[0].IasaTicks != 0` | Steered charge requires one lifecycle in one stage with IASA disabled. |
| `"capability.ambiguous"` | `operation is ForwardLungeOperationSource or SetVelocityOperationSource or SpawnHitboxOperationSource                 or SpawnProjectileOperationSource or SetAimStateOperationSource or StartCapabilityOperationSource                 or CompleteTimelineOperationSource` | Steered charge cannot share its stage with motion, hitboxes, projectiles, aim, completion or another lifecycle. |
| `"operation.cutoff-after-commitment"` | `operation is StartupAimCorrectionOperationSource correction && correction.EndTick > charge.Tick` | Startup aim correction must end no later than the charge starts. |
| `"value.out-of-range"` | `operation is GravityWindowOperationSource gravity && gravity.Tick + gravity.DurationTicks > charge.Tick` | A gravity window must end no later than the charge starts; the charge owns its own height. |
| `"value.out-of-range"` | `operation is ArmorWindowOperationSource armor && armor.Tick + armor.DurationTicks > charge.Tick` | An armour window must end no later than the charge starts; the charge owns its own armour. |
| `"reference.unresolved"` | `clip < 0` | A steered charge phase clip must be listed in its stage's animationIds. |
| `"value.out-of-range"` | `clip == 0` | The stage's first clip is the startup; phase clips sort after it (name them after it). |
| `"capability.ambiguous"` | `thrusts.Length != 1 &#124;&#124; slot.Timeline.Stages.Count != 1 &#124;&#124; thrusts[0].Tick != 0             &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.MeleeCombo &#124;&#124; slot.AimMode != AuthoringAimMode.None             &#124;&#124; stage.Operations.Any(operation => operation != thrusts[0]                 && operation is not (EmitPresentationOperationSource or ArmorWindowOperationSource))` | Thrust requires one tick-zero lifecycle in one meleeCombo stage with no aim, and only presentation or armour alongside it. |
| `"value.out-of-range"` | `thrusts[0].Parameters is ThrustCapabilityParameters thrust && thrust.ThrustTicks > stage.DurationTicks` | The thrust must end within its stage. |
| `"capability.ambiguous"` | `TetherSlotError(tethers.Length, slot.Timeline.Stages.Count, tethers[0].Tick, slot.Behavior, slot.AimMode,                 stage.IasaTicks, slot.ShellPolicy,                 stage.Operations.Any(operation => operation != tethers[0] && operation is not EmitPresentationOperationSource))             is { } error` | error |
| `"reference.unresolved"` | `clip < 0` | A tether phase clip must be listed in its stage's animationIds. |
| `"value.out-of-range"` | `clip == 0` | The stage's first clip is the throw; phase clips sort after it (name them after it). |
| `"schema.missing"` | `stance` | The shell-stance capability needs a shell block. |
| `"capability.unknown"` | `!string.IsNullOrEmpty(c.Presentation.Shell)` | A shell pose needs the shell-stance capability and a shell block. |
| `"capability.unknown"` | `!stance` | A shell block needs the slop.ability.shell-stance.v2 requirement. |
| `"schema.missing"` | `string.IsNullOrEmpty(c.Presentation.Shell)` | A shell block needs a shell pose. |
| `"value.out-of-range"` | `ShellBoundsError(CookShell(c.Shell)) is { } error` | error |
| `"reference.unresolved"` | `id != null && !c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"schema.missing"` | `forms` | The forms capability needs an alternateForm block. |
| `"capability.unknown"` | `!forms` | An alternateForm block needs the slop.ability.forms.v1 requirement. |
| `"value.out-of-range"` | `string.IsNullOrWhiteSpace(form.DisplayName)` | Value must be non-empty. |
| `"value.out-of-range"` | `slot.ShellPolicy is not (ShellPolicy.Tuck or ShellPolicy.Ride)` | Unknown shell policy. |
| `"capability.unknown"` | `c.Shell == null` | A tuck or ride move needs the character's shell block. |
| `"value.out-of-range"` | `!IsSpecial(canonicalId)` | Only specials (A, E, R, F) can tuck or ride. |
| `"capability.ambiguous"` | `slot.ShellPolicy == ShellPolicy.Tuck && (slot.AllowSlideCarry &#124;&#124; slot.PreserveMomentumOnStart)` | A tuck move already keeps her momentum: leave allowSlideCarry and preserveMomentumOnStart off. |
| `"operation.ambiguous"` | `windowCount > 1` | A timeline may contain only one startup aim-correction window. |
| `"value.out-of-range"` | `correction.EndTick <= correction.Tick &#124;&#124; correction.EndTick > slot.Timeline.Stages[stageIndex].DurationTicks` | End tick must be exclusive, after the operation tick, and inside the stage. |
| `"value.out-of-range"` | `correction.AcquisitionRange <= 0f &#124;&#124; correction.AcquisitionRange > MaxStartupAcquisitionRange` | $"Acquisition range must be greater than zero and at most {MaxStartupAcquisitionRange} meters." |
| `"value.out-of-range"` | `correction.AcquisitionHalfAngleDegrees <= 0f &#124;&#124; correction.AcquisitionHalfAngleDegrees > MaxStartupAngleDegrees` | $"Acquisition half-angle must be greater than zero and at most {MaxStartupAngleDegrees} degrees." |
| `"value.out-of-range"` | `correction.MaxYawDegrees < 0f &#124;&#124; correction.MaxYawDegrees > MaxStartupAngleDegrees` | $"Maximum yaw must be between zero and {MaxStartupAngleDegrees} degrees." |
| `"value.out-of-range"` | `correction.MaxPitchDegrees < 0f &#124;&#124; correction.MaxPitchDegrees > MaxStartupPitchDegrees` | $"Maximum pitch must be between zero and {MaxStartupPitchDegrees} degrees." |
| `"value.out-of-range"` | `correction.YawDegreesPerSecond < 0f &#124;&#124; correction.YawDegreesPerSecond > MaxStartupRateDegreesPerSecond &#124;&#124;                 correction.MaxYawDegrees > 0f && correction.YawDegreesPerSecond == 0f` | $"Yaw rate must be positive for nonzero yaw correction and at most {MaxStartupRateDegreesPerSecond} degrees per second." |
| `"value.out-of-range"` | `correction.PitchDegreesPerSecond < 0f &#124;&#124; correction.PitchDegreesPerSecond > MaxStartupRateDegreesPerSecond &#124;&#124;                 correction.MaxPitchDegrees > 0f && correction.PitchDegreesPerSecond == 0f` | $"Pitch rate must be positive for nonzero pitch correction and at most {MaxStartupRateDegreesPerSecond} degrees per second." |
| `"operation.commitment-precedes-correction"` | `commitments.Any(commitment => commitment < globalStart)` | Startup aim correction cannot follow a prior active or launch commitment. |
| `"operation.cutoff-after-commitment"` | `nextCommitment.HasValue && cutoff > nextCommitment.Value` | Startup aim correction must end no later than the earliest contact, projectile, or directional launch commitment. |
| `"value.out-of-range"` | `!(slot.AimMovementSpeedScale > 0f && slot.AimMovementSpeedScale <= 1f)` | Aim movement speed scale must be greater than 0 and at most 1. |
| `"slot.aim-movement-speed.requires-mobile"` | `slot.AimMovement != AuthoringAimMovementMode.Mobile` | Aim movement speed scale requires aimMovement mobile. |
| `code` | `canonicalId != "ground.1" && canonicalId != "ground.2" && canonicalId != "ground.3"` | Slide carry is only valid on grounded canonical normals. |
| `code` | `slot.AimMode != AuthoringAimMode.None` | Slide carry cannot be enabled on an aimed slot. |
| `code` | `slot.IsRecoveryMove` | Slide carry cannot be enabled on a recovery move. |
| `code` | `hasPositiveWarp` | Slide carry cannot be enabled on a slot with positive warp range. |
| `code` | `hasMotionOperation` | Slide carry cannot be enabled on a slot with motion-owning timeline operations. |
| `"unit.unknown"` | `operation.Unit != expected` | Unit does not match operation contract. |
| `"value.out-of-range"` | `operation is StartCapabilityOperationSource { Parameters: var embedded }             && embedded switch             {                 ChargedProjectileCapabilityParameters x => x.Projectile,                 FingerGunsCapabilityParameters x => x.Projectile,                 ChargedStreamCapabilityParameters x => x.Projectile,                 TetherCapabilityParameters x => x.Projectile,                 _ => null,             } is { } inner && (inner.Homing != null &#124;&#124; inner.Perch != null)` | homing and perch belong to spawnProjectile operations, not to a capability's projectile. |
| `"value.out-of-range"` | `!ValidAimedRecovery(recovery.StartupTicks, recovery.VerticalSpeed, recovery.DirectionalVerticalSpeed,                     recovery.HorizontalSpeed, recovery.PreparationRiseSpeed, recovery.Hop, recovery.RiseSteerAcceleration)` | Aimed recovery needs bounded speeds, rise steering 0–120 m/s² and startup 1–120 ticks. An optional hop requires zero preparation rise, start>0, at least 4 flight ticks, contact before launch, forward/arc distances in (0,3] m and landing lift in [0,1] m. |
| `"value.out-of-range"` | `MankiCapabilityBounds.Error(CookParameters(capability.Parameters)) is { } mankiError` | mankiError |
| `"value.out-of-range"` | `bazooka.ChargeTicks > 600 &#124;&#124; bazooka.ChargeTicks + bazooka.CastDuration > ushort.MaxValue                     &#124;&#124; rocket.CapabilityId == "slop.internal.manki.bazooka.v3" && bazooka.ChargeTicks == 0` | Bazooka charge must be at most 600 ticks. |
| `"value.out-of-range"` | `bazooka.HomingTurnDegPerTick <= 0f &#124;&#124; bazooka.HomingTurnDegPerTick > 90f` | Homing turn must be greater than zero and at most 90 degrees per tick. |
| `"value.out-of-range"` | `lunge.Speed <= 0f` | Speed must be greater than zero. |
| `"value.out-of-range"` | `lunge.DurationTicks == 0` | Duration must be greater than zero. |
| `"value.out-of-range"` | `gravity.GravityScale < 0f &#124;&#124; gravity.GravityScale > 1f` | Gravity scale must be between zero and one. |
| `"value.out-of-range"` | `gravity.DurationTicks == 0` | Duration must be greater than zero. |
| `"value.out-of-range"` | `armorWindow.DurationTicks == 0` | Duration must be greater than zero. |
| `"value.out-of-range"` | `hitbox.Hitbox.DurationTicks == 0` | Duration must be greater than zero. |
| `"reference.unresolved"` | `!c.PresentationIds.Contains(presentation.PresentationId, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"value.out-of-range"` | `CommandGrabContract.BoundsError((CookedCommandGrabCapabilityParameters)CookParameters(commandGrab)) is { } grabError` | grabError |
| `"reference.unresolved"` | `!string.IsNullOrEmpty(id) && !c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"value.out-of-range"` | `ThrustBoundsError((CookedThrustCapabilityParameters)CookParameters(thrust)) is { } thrustError` | thrustError |
| `"value.out-of-range"` | `CannonballBoundsError((CookedCannonballCapabilityParameters)CookParameters(cannonball)) is { } cannonballError` | cannonballError |
| `"reference.unresolved"` | `!string.IsNullOrEmpty(cannonball.LaunchPresentationId)                     && !c.PresentationIds.Contains(cannonball.LaunchPresentationId, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"value.out-of-range"` | `TetherBoundsError((CookedTetherCapabilityParameters)CookParameters(tether)) is { } tetherError` | tetherError |
| `"reference.unresolved"` | `!string.IsNullOrEmpty(id) && !c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"value.out-of-range"` | `charge.MaxChargeTicks == 0 &#124;&#124; charge.Tier2Ticks == 0 &#124;&#124;                     charge.Tier2Ticks >= charge.Tier3Ticks &#124;&#124; charge.Tier3Ticks > charge.MaxChargeTicks` | Charge thresholds must satisfy 0 < tier2Ticks < tier3Ticks <= maxChargeTicks. |
| `"value.out-of-range"` | `charge.Projectile.Return != null` | A charged projectile cannot return; use spawnProjectile. |
| `"value.out-of-range"` | `charge.Projectile.Radius <= 0f &#124;&#124; charge.Tier2Radius <= charge.Projectile.Radius &#124;&#124;                     charge.Tier3Radius <= charge.Tier2Radius &#124;&#124; charge.Projectile.Speed <= 0f &#124;&#124;                     charge.Projectile.MaxFlightTicks == 0 &#124;&#124; charge.FireTriggerTick == 0` | Tier radii must be positive and increasing; speed, flight lifetime, and fire startup must be positive. |
| `"value.out-of-range"` | `!ValidFingerGuns(guns.FireTriggerTick, guns.RecoveryTicks, guns.ShotsPerReload,                     guns.ReloadTicks, guns.AirMaxFallSpeed, guns.AirRecoilSpeed)` | Finger Guns requires fire ticks 1..120, recovery 1..240, shots 1..24, reload 1..600, and air speeds 0..30 m/s. |
| `"value.out-of-range"` | `guns.Projectile.Return != null &#124;&#124; guns.Projectile.Radius <= 0f &#124;&#124;                     guns.Projectile.Speed <= 0f &#124;&#124; guns.Projectile.MaxFlightTicks == 0` | Finger Guns needs a non-returning projectile with positive radius, speed and flight lifetime. |
| `"value.out-of-range"` | `stream.MaxChargeTicks == 0 &#124;&#124; stream.FireTriggerTick == 0 &#124;&#124; stream.EmitIntervalTicks == 0 &#124;&#124;                     stream.MinStreamTicks == 0 &#124;&#124; stream.MaxStreamTicks < stream.MinStreamTicks` | Charge, startup, interval and stream lengths must be positive, and maxStreamTicks at least minStreamTicks. |
| `"value.out-of-range"` | `stream.MaxBaseKnockback < stream.Projectile.BaseKnockback &#124;&#124; stream.MinRecoilSpeed < 0f &#124;&#124;                     stream.MaxRecoilSpeed < stream.MinRecoilSpeed` | maxBaseKnockback must be at least the projectile's baseKnockback, and 0 <= minRecoilSpeed <= maxRecoilSpeed. |
| `"value.out-of-range"` | `stream.Projectile.Return != null` | A stream's projectiles cannot return. |
| `"value.out-of-range"` | `stream.Projectile.Radius <= 0f &#124;&#124; stream.Projectile.Speed <= 0f &#124;&#124; stream.Projectile.MaxFlightTicks == 0` | A stream's projectiles need a positive radius, speed and flight lifetime. |
| `"reference.unresolved"` | `id != null && CharacterBoneRegistry.Find(id)?.Required != true && !c.HurtboxBoneDefs.Any(x => x.BoneId == id &#124;&#124; x.EndBoneId == id) && !c.AttachmentBoneIds.Contains(id, StringComparer.Ordinal)` | Bone ID is not declared. |
| `"value.out-of-range"` | `placement.DurationTicks == 0` | Presentation duration must be greater than zero. |
| `"value.out-of-range"` | `placement.LocalScaleX <= 0f &#124;&#124; placement.LocalScaleY <= 0f &#124;&#124; placement.LocalScaleZ <= 0f` | Presentation scale components must be greater than zero. |
| `"value.out-of-range"` | `placement.AttachmentMode != AuthoringPresentationAttachmentMode.World &&             placement.AttachmentMode != AuthoringPresentationAttachmentMode.Bone` | Unknown presentation attachment mode. |
| `"reference.unresolved"` | `placement.AttachmentMode == AuthoringPresentationAttachmentMode.Bone &&             (string.IsNullOrEmpty(placement.BoneId) &#124;&#124;              (placement.BoneId != null && CharacterBoneRegistry.Find(placement.BoneId)?.Required != true && !c.HurtboxBoneDefs.Any(x => x.BoneId == placement.BoneId &#124;&#124; x.EndBoneId == placement.BoneId) &&               !c.AttachmentBoneIds.Contains(placement.BoneId, StringComparer.Ordinal)))` | Presentation bone must be a declared bone. |
| `"value.out-of-range"` | `DashStrikeContract.BoundsError((CookedDashStrikeCapabilityParameters)CookParameters(x)) is { } error` | error |
| `"reference.unresolved"` | `!string.IsNullOrEmpty(x.TipPresentationId) && !c.PresentationIds.Contains(x.TipPresentationId, StringComparer.Ordinal)` | Tip presentation ID is not declared. |
| `"value.out-of-range"` | `FlashCutContract.BoundsError(cooked) is { } error` | error |
| `"reference.unresolved"` | `!string.IsNullOrEmpty(id) && !c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Flash cut presentation ID is not declared. |
| `"value.out-of-range"` | `ProjectilePerchContract.BoundsError(cooked) is { } error` | error |
| `"value.out-of-range"` | `projectile.ChallengeTicks > 600` | challengeTicks must be at most 600. |
| `"capability.ambiguous"` | `c.AlternateForm == null` | A perched projectile pops on a hit from the thrower's other form: it needs slop.ability.forms.v1. |
| `"reference.unresolved"` | `id.Length > 0 && !c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"value.out-of-range"` | `x.Hit is { } hit && BonkSpecialContract.LegHit(hit) is { } hitError` | hitError |
| `"value.out-of-range"` | `projectile.ChallengeTicks > 600` | challengeTicks must be at most 600. |
| `"value.out-of-range"` | `!(projectile.Speed > 0f) &#124;&#124; projectile.Gravity != 0f` | A returning projectile needs a positive speed and no gravity. |
| `"value.out-of-range"` | `x.OutboundTicks == 0 &#124;&#124; x.OutboundTicks >= projectile.MaxFlightTicks` | Outbound time must be positive and shorter than the projectile's flight lifetime. |
| `"value.out-of-range"` | `!(x.ReturnSpeed > 0f) &#124;&#124; !(x.ReturnAcceleration > 0f)` | Return speed and acceleration must be positive. |
| `"value.out-of-range"` | `x.SelfHitStunTicks == 0 &#124;&#124; x.SelfHitStunTicks > MaxFixedHitstunTicks` | Self-hit stun must be between 1 and 240 ticks. |
| `"id.invalid"` | `!id.StartsWith("presentation.", StringComparison.Ordinal)` | Return presentation ID must use the presentation. prefix. |
| `"reference.unresolved"` | `!c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Return presentation ID is not declared. |
| `"value.out-of-range"` | `!(x.GallopSpeed > 0f)` | Gallop speed must be positive. |
| `"value.out-of-range"` | `!(x.TurnDegreesPerSecond >= 0f && x.TurnDegreesPerSecond <= MaxSteeredChargeTurnDegreesPerSecond)` | $"Turn rate must be between 0 and {MaxSteeredChargeTurnDegreesPerSecond} degrees per second." |
| `"value.out-of-range"` | `x.MaxGallopTicks == 0 &#124;&#124; x.HitEndingTicks == 0 &#124;&#124; x.MissEndingTicks == 0` | Gallop, hit ending and miss ending must be positive. |
| `"value.out-of-range"` | `x.SkidTicks > x.MissEndingTicks` | The skid must fit within the miss ending. |
| `"value.out-of-range"` | `!string.IsNullOrEmpty(x.GallopAnimationId) && x.GallopLoopTicks == 0` | A gallop clip needs a positive loop length. |
| `"value.out-of-range"` | `BonkSpecialContract.Charge((CookedSteeredChargeCapabilityParameters)CookParameters(x)) is { } error` | error |
| `"value.out-of-range"` | `x.LanceHitbox.HitGroup != 0` | The lance keeps its own hit history (hitGroup 0). |
| `"reference.unresolved"` | `!IsKnownAnimation(id, c)` | Animation ID is not declared. |
| `"id.invalid"` | `!id.StartsWith("presentation.", StringComparison.Ordinal)` | Charge presentation ID must use the presentation. prefix. |
| `"reference.unresolved"` | `!c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Charge presentation ID is not declared. |
| `"value.out-of-range"` | `x.DurationTicks == 0` | Duration must be positive. |
| `"value.out-of-range"` | `x.Shape != AuthoringHitboxShape.Sphere && x.Shape != AuthoringHitboxShape.Capsule` | Unknown hitbox shape. |
| `"value.out-of-range"` | `x.KnockbackDirection != AuthoringKnockbackDirection.AwayFromOwner &&             x.KnockbackDirection != AuthoringKnockbackDirection.TowardOwner` | Unknown knockback direction. |
| `"reference.unresolved"` | `x.HitPresentationId != null && !c.PresentationIds.Contains(x.HitPresentationId, StringComparer.Ordinal)` | Hit presentation ID is not declared. |
| `"value.non-finite"` | `float.IsNaN(value) &#124;&#124; float.IsInfinity(value)` | Numeric value must be finite. |
| `"value.out-of-range"` | `fixedTicks > MaxFixedHitstunTicks` | Fixed hitstun must not exceed 240 ticks. |
| `"value.out-of-range"` | `fixedTicks > 0 && stunGate == 0` | Fixed hitstun requires a nonzero stun gate. |
| `"id.invalid"` | `!id.StartsWith("presentation.", StringComparison.Ordinal)` | Leap presentation ID must use the presentation. prefix. |
| `"reference.unresolved"` | `!character.PresentationIds.Contains(id, StringComparer.Ordinal)` | Leap presentation ID is not declared. |
| `"value.out-of-range"` | `parameters.RiseHeight <= 0 &#124;&#124; parameters.MaxRange <= 0 &#124;&#124; parameters.NoTargetDistance < 0             &#124;&#124; parameters.NoTargetDistance > parameters.MaxRange &#124;&#124; parameters.RiseTicks == 0 &#124;&#124; parameters.HangTicks == 0             &#124;&#124; parameters.DropTicks == 0 &#124;&#124; parameters.MaxFallTicks == 0 &#124;&#124; parameters.HitRecoveryTicks == 0             &#124;&#124; parameters.MissRecoveryTicks < parameters.HitRecoveryTicks` | Leap beats, range and recoveries are invalid. |
| `"value.out-of-range"` | `BonkSpecialContract.Leap((CookedTargetedLeapCapabilityParameters)CookParameters(parameters)) is { } leapError` | leapError |
| `"schema.missing"` | `hitbox == null` | Landing hitbox is required. |
| `"value.out-of-range"` | `hitbox.DurationTicks == 0 &#124;&#124; hitbox.DurationTicks > parameters.MissRecoveryTicks` | Landing hitbox duration must be positive and fit within recovery. |
| `"value.out-of-range"` | `hitbox.Shape != AuthoringHitboxShape.Sphere && hitbox.Shape != AuthoringHitboxShape.Capsule` | Unknown hitbox shape. |
| `"value.out-of-range"` | `hitbox.KnockbackDirection != AuthoringKnockbackDirection.AwayFromOwner &&             hitbox.KnockbackDirection != AuthoringKnockbackDirection.TowardOwner` | Unknown knockback direction. |
| `"id.duplicate"` | `!seenHurtboxBones.Add(bone.BoneId)` | Duplicate bone ID. |
| `"reference.unresolved"` | `bone.EndBoneId == null && (bone.EndOffsetX != 0f &#124;&#124; bone.EndOffsetY != 0f &#124;&#124; bone.EndOffsetZ != 0f)` | End offsets require an end bone. |
| `"id.duplicate"` | `!seenAttachmentBones.Add(id)` | Duplicate attachment bone ID. |
| `"id.duplicate"` | `!seenPresentation.Add(c.PresentationIds[i])` | Duplicate presentation ID. |
| `"id.duplicate"` | `!standardAnimations.Add(id)` | Duplicate standard animation ID. |
| `"id.duplicate"` | `!standardAnimations.Add(c.Presentation.Tumble)` | Duplicate standard animation ID. |
| `"id.duplicate"` | `!standardAnimations.Add(id)` | Duplicate standard animation ID. |
| `"presentation.unused-id"` | `!PresentationUsed(stageId, c)` | Declared presentation ID is not referenced by an attack or timeline operation. |
| `"id.invalid"` | `!id.StartsWith("presentation.", StringComparison.Ordinal)` | Explosion presentation ID must use the presentation. prefix. |
| `"reference.unresolved"` | `!c.PresentationIds.Contains(id, StringComparer.Ordinal)` | Presentation ID is not declared. |
| `"value.non-finite"` | `float.IsNaN(value) &#124;&#124; float.IsInfinity(value)` | Numeric value must be finite. |
| `"value.out-of-range"` | `c.Movement.AirDodgeSpeed <= 0f` | Air-dodge speed must be greater than zero. |
| `"schema.missing"` | `geometry == null &#124;&#124; geometry.AttackerAnchor == null &#124;&#124; geometry.VictimAnchor == null` | Capture geometry and both anchors are required. |
| `"value.out-of-range"` | `geometry.Reach <= 0f &#124;&#124; geometry.Width <= 0f &#124;&#124; geometry.Height <= 0f` | Capture reach, width, and height must be greater than zero. |
| `"alias.cycle"` | `!visiting.Add(id)` | Alias cycle detected. |
| `"alias.missing-target"` | `aliases.TryGetValue(id, out var target)` | Canonical slot has no explicit definition or alias. |
| `"budget.exceeded"` | `timelineOperations > CookedBudget.MaxOperationsPerTimeline` | Timeline operation budget exceeded. |
| `"id.invalid"` | `string.IsNullOrEmpty(value) &#124;&#124; value.Length > 64 &#124;&#124; value[0] != '_' &#124;&#124; value.Skip(1).Any(x => !(x >= 'a' && x <= 'z') && !(x >= '0' && x <= '9') && x != '.' && x != '-' && x != '_')` | Attachment ID must be underscore-prefixed lowercase ASCII. |
| `"id.invalid"` | `!IsId(value)` | ID must be lowercase ASCII and start with a letter. |
| `"value.non-finite"` | `float.IsNaN(value) &#124;&#124; float.IsInfinity(value)` | Value must be finite. |
| `"value.out-of-range"` | `value < 0` | Value must be non-negative. |
| `"value.out-of-range"` | `value < -90 &#124;&#124; value > 90` | Angle must be between -90 and 90 degrees. |
| `"value.out-of-range"` | `value < -180 &#124;&#124; value > 180` | Yaw offset must be between -180 and 180 degrees. |

### Accepted bounds predicates

These expressions are the compiler's shared predicates. All terms must hold. They supply the exact coupled limits for the diagnostics above.

- `TetherSlotError(tethers, stages, tick, behavior, aim, iasaTicks, shellPolicy, otherOperations)` accepts `tethers != 1 &#124;&#124; stages != 1 &#124;&#124; tick != 0 &#124;&#124; behavior != AuthoringAbilityBehavior.MeleeCombo             &#124;&#124; aim != AuthoringAimMode.None &#124;&#124; iasaTicks != 0 &#124;&#124; otherOperations             ? "Tether requires one tick-zero lifecycle in one meleeCombo stage with no aim and IASA disabled, and only presentation alongside it."             : shellPolicy == ShellPolicy.Tuck ? "A tether pops her out of the shell; it cannot tuck." : null`.

- `RideSlotError(stages, iasaTicks, otherOperations)` accepts `stages != 1 &#124;&#124; iasaTicks != 0 &#124;&#124; otherOperations             ? "A ride move is one stage with IASA disabled and only presentation, startup aim correction and gravity windows: the ride is the shell's."             : null`.

- `ValidAimedRecovery(startup, vertical, directional, horizontal, preparation, hop, riseSteer)` accepts `startup is > 0 and <= 120 && vertical is > 0f and <= 60f             && directional > 0f && directional <= vertical && horizontal is >= 0f and <= 60f             && preparation is >= 0f and <= 4f && riseSteer is >= 0f and <= 120f             && (hop == null &#124;&#124; preparation == 0f && hop.StartTicks > 0                 && hop.ContactTicks >= hop.StartTicks + 4 && hop.ContactTicks < startup                 && hop.ForwardDistance is > 0f and <= 3f                 && hop.ArcHeight is > 0f and <= 3f && hop.LandingHeight is >= 0f and <= 1f)`.

- `ValidFingerGuns(fire, recovery, shots, reload, fall, recoil)` accepts `fire is > 0 and <= 120 && recovery is > 0 and <= 240 && shots is > 0 and <= 24             && reload is > 0 and <= 600 && fall is >= 0f and <= 30f && recoil is >= 0f and <= 30f`.

- `LegHit(h)` accepts `!Finite(h.Damage, h.Angle, h.BaseKnockback, h.KnockbackGrowth) &#124;&#124; h.Damage < 0 &#124;&#124; h.Damage > 100            &#124;&#124; h.Angle < -90 &#124;&#124; h.Angle > 90 &#124;&#124; h.BaseKnockback < 0 &#124;&#124; h.KnockbackGrowth < 0            &#124;&#124; h.StunTicks > 240 &#124;&#124; h.FixedHitstunTicks > 240 &#124;&#124; h.FixedHitstunTicks > 0 && h.StunTicks == 0             ? "Return hit values are invalid; fixed hitstun needs a nonzero stun gate." : null`.

- `SlotError(count, stages, tick, behavior, aim, iasa, extra, carry, charge, duration, p)` accepts `count != 1 &#124;&#124; stages != 1 &#124;&#124; tick != 0 &#124;&#124; behavior != AuthoringAbilityBehavior.MeleeCombo &#124;&#124;            aim != AuthoringAimMode.None &#124;&#124; iasa != 0 &#124;&#124; extra &#124;&#124; carry &#124;&#124; charge             ? "Dash strike requires one tick-zero meleeCombo lifecycle, no aim, IASA, carry or charge pool, and only presentation alongside it."             : p.TravelTicks + p.TipHitbox.DurationTicks + p.RecoveryTicks > duration &#124;&#124;               (int)p.TipPoseTick + p.TipHitbox.DurationTicks + p.RecoveryTicks > duration                 ? "Dash strike travel, tip pose, tip lifetime and recovery must fit the stage watchdog."                 : null`.

- `SlotError(count, stages, tick, behavior, aim, iasa, extra, carry, charge, duration, clips, p)` accepts `count != 1 &#124;&#124; stages != 1 &#124;&#124; tick != 0 &#124;&#124; behavior != AuthoringAbilityBehavior.MeleeCombo &#124;&#124;            aim != AuthoringAimMode.None &#124;&#124; iasa != 0 &#124;&#124; extra &#124;&#124; carry &#124;&#124; charge             ? "Flash cut requires one tick-zero meleeCombo lifecycle, no aim, IASA, carry or charge pool, and only presentation alongside it."             : p.LongestTicks > duration                 ? "Flash cut stance, legs, sheathe and recovery, and its whiff, must fit the stage watchdog."                 : !clips.Contains(p.StanceAnimationId) &#124;&#124; !clips.Contains(p.LegAnimationId) &#124;&#124;                   p.SheatheAnimationId.Length > 0 && !clips.Contains(p.SheatheAnimationId)                     ? "Flash cut stance, leg and sheathe clips must be listed in its stage."                     : null`.

### Validation constants

| Name | Value |
| --- | --- |
| `AimedRecoveryCapabilityId` | `"slop.ability.aimed-recovery.v1"` |
| `AimedRecoveryCapabilityVersion` | `"1"` |
| `ChargedProjectileCapabilityId` | `"slop.ability.charged-projectile.v1"` |
| `ChargedProjectileCapabilityVersion` | `"1"` |
| `ChargedStreamCapabilityId` | `"slop.ability.charged-stream.v1"` |
| `ChargedStreamCapabilityVersion` | `"1"` |
| `FingerGunsCapabilityId` | `"slop.ability.finger-guns.v1"` |
| `FingerGunsCapabilityVersion` | `"1"` |
| `DashStrikeCapabilityId` | `"slop.ability.dash-strike.v1"` |
| `DashStrikeCapabilityVersion` | `"1"` |
| `FlashCutCapabilityId` | `"slop.ability.flash-cut.v1"` |
| `FlashCutCapabilityVersion` | `"1"` |
| `TargetedLeapCapabilityId` | `"slop.ability.targeted-leap.v2"` |
| `TargetedLeapCapabilityVersion` | `"2"` |
| `SteeredChargeCapabilityId` | `"slop.ability.steered-charge.v2"` |
| `SteeredChargeCapabilityVersion` | `"2"` |
| `ShellStanceCapabilityId` | `"slop.ability.shell-stance.v2"` |
| `ShellStanceCapabilityVersion` | `"1"` |
| `CannonballCapabilityId` | `"slop.ability.cannonball.v1"` |
| `CannonballCapabilityVersion` | `"1"` |
| `ThrustCapabilityId` | `"slop.ability.thrust.v1"` |
| `ThrustCapabilityVersion` | `"1"` |
| `TetherCapabilityId` | `"slop.ability.tether.v1"` |
| `TetherCapabilityVersion` | `"1"` |
| `CommandGrabCapabilityId` | `"slop.ability.command-grab.v1"` |
| `CommandGrabCapabilityVersion` | `"1"` |
| `FormsCapabilityId` | `"slop.ability.forms.v1"` |
| `FormsCapabilityVersion` | `"1"` |
| `MaxSteeredChargeTurnDegreesPerSecond` | `1440f` |
| `ManifestSchemaVersion` | `1` |
| `AuthoringSchemaVersion` | `4` |
| `CookedSchemaVersion` | `5` |
| `RuntimeApiMin` | `"1.2.0"` |
| `RuntimeApiMax` | `"1.x"` |
| `MaxFixedHitstunTicks` | `240` |
| `MaxStartupAcquisitionRange` | `32f` |
| `MaxStartupAngleDegrees` | `180f` |
| `MaxStartupPitchDegrees` | `90f` |
| `MaxStartupRateDegreesPerSecond` | `1440f` |

### Bounds returned by helper rules

| Rule | Rejected condition | Message |
| --- | --- | --- |
| `OptionalString` | `see expression` | String(p, name, path, d) |
| `TetherBoundsError` | `x.AtTarget is { } at` | x.Projectile != null &#124;&#124; x.RetractSpeed != 0f &#124;&#124; x.RetractAnimationId.Length > 0                 &#124;&#124; x.RetractPresentationId.Length > 0                 ? "A placed catch (atTarget) throws no ring: no projectile, retract speed, retract clip or retract presentation."                 : TetherAtTargetBoundsError(x, at) |
| `TetherBoundsError` | `ring == null` | A thrown tether needs its projectile. |
| `TetherBoundsError` | `!AllFinite(x.ReelSpeed, x.AnchorDistance, x.RetractSpeed, x.AirGravityScale, ring.LaunchOffsetX,                 ring.LaunchOffsetY, ring.LaunchOffsetZ, ring.Speed, ring.Gravity, ring.Radius, ring.Damage, ring.Angle,                 ring.BaseKnockback, ring.KnockbackGrowth, ring.YawOffsetDegrees)` | Tether values must be finite. |
| `TetherBoundsError` | `x.ThrowTick == 0 &#124;&#124; x.ThrowTick > 120` | throwTick must be 1 to 120. |
| `TetherBoundsError` | `!(ring.Speed > 0f && ring.Speed <= 60f) &#124;&#124; !(ring.Radius > 0f && ring.Radius <= 3f)             &#124;&#124; ring.MaxFlightTicks == 0 &#124;&#124; ring.MaxFlightTicks > 240` | The ring needs 0 < speed <= 60, 0 < radius <= 3 and maxFlightTicks 1 to 240. |
| `TetherBoundsError` | `ring.Gravity != 0f &#124;&#124; ring.YawOffsetDegrees != 0f &#124;&#124; ring.Return != null` | The ring flies straight: no gravity, no yaw offset and no return block. |
| `TetherBoundsError` | `ring.Damage < 0f &#124;&#124; ring.BaseKnockback != 0f &#124;&#124; ring.KnockbackGrowth != 0f &#124;&#124; ring.StunTicks != 0` | The ring catches instead of launching: damage >= 0, no knockback and no stun. |
| `TetherBoundsError` | `!(x.ReelSpeed > 0f && x.ReelSpeed <= 60f) &#124;&#124; !(x.RetractSpeed > 0f && x.RetractSpeed <= 60f)` | reelSpeed and retractSpeed must be above 0 and at most 60. |
| `TetherBoundsError` | `see expression` | TetherReelBoundsError(x) |
| `TetherAtTargetBoundsError` | `!AllFinite(x.ReelSpeed, x.AnchorDistance, x.AirGravityScale, at.Radius, at.Height, at.Damage, at.MaxRange,                 at.NoTargetDistance)` | Tether values must be finite. |
| `TetherAtTargetBoundsError` | `x.ThrowTick == 0 &#124;&#124; x.ThrowTick > 120` | throwTick must be 1 to 120. |
| `TetherAtTargetBoundsError` | `at.WarningTicks == 0 &#124;&#124; at.WarningTicks > 120` | atTarget.warningTicks must be 1 to 120. |
| `TetherAtTargetBoundsError` | `at.CatchTicks == 0 &#124;&#124; at.CatchTicks > 60` | atTarget.catchTicks must be 1 to 60. |
| `TetherAtTargetBoundsError` | `!(at.Radius > 0f && at.Radius <= 3f) &#124;&#124; !(at.Height >= 2f * at.Radius && at.Height <= 6f)` | The catch needs 0 < radius <= 3 and a height of two radii up to 6. |
| `TetherAtTargetBoundsError` | `!(at.Damage >= 0f && at.Damage <= 100f)` | atTarget.damage must be 0 to 100. |
| `TetherAtTargetBoundsError` | `!(at.MaxRange > 0f && at.MaxRange <= MaxStartupAcquisitionRange)             &#124;&#124; !(at.NoTargetDistance >= 0f && at.NoTargetDistance <= at.MaxRange)` | atTarget needs 0 < maxRange <= 32 and 0 <= noTargetDistance <= maxRange. |
| `TetherAtTargetBoundsError` | `!(x.ReelSpeed > 0f && x.ReelSpeed <= 60f)` | reelSpeed must be above 0 and at most 60. |
| `TetherAtTargetBoundsError` | `see expression` | TetherUnmarkedBoundsError(at.Unmarked) ?? TetherReelBoundsError(x) |
| `TetherUnmarkedBoundsError` | `!AllFinite(hand.Distance, hand.Radius, hand.Height, hand.Damage, hand.Angle, hand.BaseKnockback,                 hand.KnockbackGrowth)` | atTarget.unmarked values must be finite. |
| `TetherUnmarkedBoundsError` | `hand.RiseTick == 0 &#124;&#124; hand.RiseTick > 120` | atTarget.unmarked.riseTick must be 1 to 120. |
| `TetherUnmarkedBoundsError` | `hand.ActiveTicks == 0 &#124;&#124; hand.ActiveTicks > 60` | atTarget.unmarked.activeTicks must be 1 to 60. |
| `TetherUnmarkedBoundsError` | `hand.RecoveryTicks > 240` | atTarget.unmarked.recoveryTicks must be 0 to 240. |
| `TetherUnmarkedBoundsError` | `!(hand.Distance >= 0f && hand.Distance <= 6f)` | atTarget.unmarked.distance must be 0 to 6. |
| `TetherUnmarkedBoundsError` | `!(hand.Radius > 0f && hand.Radius <= 3f) &#124;&#124; !(hand.Height >= 2f * hand.Radius && hand.Height <= 6f)` | The unmarked hand needs 0 < radius <= 3 and a height of two radii up to 6. |
| `TetherUnmarkedBoundsError` | `!(hand.Damage > 0f && hand.Damage <= 100f)` | atTarget.unmarked.damage must be above 0 and at most 100. |
| `TetherUnmarkedBoundsError` | `hand.Angle < -90f &#124;&#124; hand.Angle > 90f` | atTarget.unmarked.angle must be -90 to 90. |
| `TetherUnmarkedBoundsError` | `hand.BaseKnockback < 0f &#124;&#124; hand.KnockbackGrowth < 0f` | The unmarked hand's knockback cannot be negative. |
| `TetherUnmarkedBoundsError` | `hand.StunTicks > 240` | atTarget.unmarked.stunTicks must be 0 to 240. |
| `TetherReelBoundsError` | `!(x.AnchorDistance >= 0f && x.AnchorDistance <= 3f)` | anchorDistance must be 0 to 3. |
| `TetherReelBoundsError` | `x.ArrivalStunTicks == 0 &#124;&#124; x.ArrivalStunTicks > 240` | arrivalStunTicks must be 1 to 240. |
| `TetherReelBoundsError` | `x.MaxReelTicks == 0 &#124;&#124; x.MaxReelTicks > 600` | maxReelTicks must be 1 to 600. |
| `TetherReelBoundsError` | `x.MissRecoveryTicks > 240` | missRecoveryTicks must be 0 to 240. |
| `TetherReelBoundsError` | `!(x.AirGravityScale >= 0f && x.AirGravityScale <= 1f)` | airGravityScale must be 0 to 1. |
| `FormsBoundsError` | `forms != (x.AlternateForm != null)` | An alternateForm block exists exactly when the slop.ability.forms.v1 capability is required. |
| `FormsBoundsError` | `x.Slots.Any(slot => slot.Form != 0)` | The top-level grid is form 0. |
| `FormsBoundsError` | `slot.Timeline.Stages.SelectMany(stage => stage.Operations).Count(op => op is CookedSwapFormOperation) > (forms ? 1 : 0)` | forms ? $"Move {slot.Id} swaps form more than once." : "swapForm needs the forms capability and an alternateForm block." |
| `FormsBoundsError` | `!IsId(form.Id) &#124;&#124; string.IsNullOrWhiteSpace(form.DisplayName)` | The alternate form needs a lowercase ID and a display name. |
| `FormsBoundsError` | `form.Slots.Count != CanonicalSlots.Length             &#124;&#124; form.Slots.Where((slot, i) => slot.Form != 1 &#124;&#124; slot.Ordinal != i &#124;&#124; slot.Id != CanonicalSlots[i]).Any()` | The alternate form has its own fourteen canonical slots. |
| `FormsBoundsError` | `!(form.Movement.AirDodgeSpeed > 0f)` | The alternate form's air-dodge speed must be greater than zero. |
| `FormsBoundsError` | `id.Length > 0 && !IsId(id)` | Alternate form roles must be lowercase IDs. |
| `ShellPairingError` | `hasSplash != list.Any(slot => slot.Policy == ShellPolicy.Tuck)` | The shell's splash exists exactly when a slot tucks. |
| `ShellPairingError` | `hasRide != riders > 0` | The shell's ride exists exactly when a slot rides. |
| `ShellPairingError` | `see expression` | riders > 1 ? "Only one special (and its air or ground variant) can ride." : null |
| `CannonballSlotError` | `cannonballs != 1 &#124;&#124; stages != 1 &#124;&#124; tick != 0 &#124;&#124; behavior != AuthoringAbilityBehavior.MeleeCombo             &#124;&#124; aim != AuthoringAimMode.None &#124;&#124; iasaTicks != 0 &#124;&#124; otherOperations` | Cannonball requires one tick-zero lifecycle in one meleeCombo stage with no aim and IASA disabled, and only presentation alongside it. |
| `CannonballSlotError` | `shellPolicy != ShellPolicy.Tuck` | A cannonball tucks: the shell's splash lands it. |
| `CannonballSlotError` | `parameters == null` | Cannonball parameters are missing. |
| `CannonballSlotError` | `see expression` | parameters.TakeoffTick + parameters.MaxFlightTicks >= durationTicks             ? "The cannonball's stage must outlast its takeoff and longest flight." : null |
| `CannonballBoundsError` | `!AllFinite(x.MaxRange, x.NoTargetDistance, x.MaxLaunchSpeed)` | Cannonball values must be finite. |
| `CannonballBoundsError` | `x.TakeoffTick > 60` | takeoffTick must be 0 to 60. |
| `CannonballBoundsError` | `!(x.NoTargetDistance > 0f && x.NoTargetDistance <= x.MaxRange && x.MaxRange <= 30f)` | Ranges need 0 < noTargetDistance <= maxRange <= 30. |
| `CannonballBoundsError` | `x.MinFlightTicks < 2 &#124;&#124; x.MinFlightTicks > x.MaxFlightTicks &#124;&#124; x.MaxFlightTicks > 240` | Flight ticks need 2 <= minFlightTicks <= maxFlightTicks <= 240. |
| `CannonballBoundsError` | `!(x.MaxLaunchSpeed > 0f && x.MaxLaunchSpeed <= 60f)` | maxLaunchSpeed must be above 0 and at most 60. |
| `ShellBoundsError` | `!AllFinite(x.PopOutDeceleration, x.BlockedRecoilSpeed)` | Shell values must be finite. |
| `ShellBoundsError` | `!(x.PopOutDeceleration >= 0f && x.PopOutDeceleration <= 600f)` | popOutDeceleration must be 0 to 600. |
| `ShellBoundsError` | `!(x.BlockedRecoilSpeed >= 0f && x.BlockedRecoilSpeed <= 30f)` | blockedRecoilSpeed must be 0 to 30. |
| `ShellBoundsError` | `x.Splash == null && x.Ride == null` | A shell needs a splash, a ride or both. |
| `ShellBoundsError` | `splash.Hitbox.StartBoneId != null &#124;&#124; splash.Hitbox.EndBoneId != null &#124;&#124; splash.Hitbox.HitGroup != 0` | The splash hitbox is root-relative with its own hit history (no bones, hitGroup 0). |
| `ShellBoundsError` | `splash.PopUpTicks == 0 &#124;&#124; splash.PopUpTicks > 240` | splash.popUpTicks must be 1 to 240. |
| `ShellBoundsError` | `!AllFinite(ride.Speed, ride.TurnDegreesPerSecond)` | Ride values must be finite. |
| `ShellBoundsError` | `ride.Ticks == 0 &#124;&#124; ride.Ticks > 600` | ride.ticks must be 1 to 600. |
| `ShellBoundsError` | `!(ride.Speed > 0f && ride.Speed <= 60f)` | ride.speed must be above 0 and at most 60. |
| `ShellBoundsError` | `!(ride.TurnDegreesPerSecond >= 0f && ride.TurnDegreesPerSecond <= 720f)` | ride.turnDegreesPerSecond must be 0 to 720. |
| `ShellBoundsError` | `ride.Wave.StartBoneId != null &#124;&#124; ride.Wave.EndBoneId != null &#124;&#124; ride.Wave.HitGroup != 0` | The wave is root-relative with its own hit history (no bones, hitGroup 0). |
| `ShellBoundsError` | `ride.Wave.StunTicks != 0 &#124;&#124; ride.Wave.FixedHitstunTicks != 0` | The wave carries instead of stunning: stunTicks and fixedHitstunTicks are 0. |
| `ThrustBoundsError` | `!AllFinite(x.TargetConeDegrees, x.NeutralPitchDegrees, x.StickPitchDegrees, x.Speed, x.Acceleration, x.MaxSpeed)` | Thrust values must be finite. |
| `ThrustBoundsError` | `x.Aim is not (ThrustAim.Stick or ThrustAim.TargetAhead)` | Unknown thrust aim. |
| `ThrustBoundsError` | `!(x.TargetConeDegrees >= 0f && x.TargetConeDegrees <= 180f)             &#124;&#124; x.Aim == ThrustAim.Stick && x.TargetConeDegrees != 0f` | targetConeDegrees is 0 to 180 and only applies to targetAhead. |
| `ThrustBoundsError` | `!(MathF.Abs(x.NeutralPitchDegrees) <= 90f && MathF.Abs(x.StickPitchDegrees) <= 90f)` | Pitches must be -90 to 90 degrees. |
| `ThrustBoundsError` | `!(x.Speed > 0f && x.Speed <= x.MaxSpeed && x.MaxSpeed <= 60f)` | Speeds need 0 < speed <= maxSpeed <= 60. |
| `ThrustBoundsError` | `!(x.Acceleration >= 0f && x.Acceleration <= 240f)` | acceleration must be 0 to 240. |
| `ThrustBoundsError` | `x.ThrustTicks == 0 &#124;&#124; x.ThrustTicks > 600` | thrustTicks must be 1 to 600. |
| `ThrustBoundsError` | `x.TrailHitbox is { } trail && (trail.StartBoneId != null &#124;&#124; trail.EndBoneId != null &#124;&#124; trail.HitGroup != 0)` | The trail hitbox is root-relative with its own hit history (no bones, hitGroup 0). |
| `Charge` | `p.Assist is { } a && (!Finite(a.AssistRange, a.AssistHalfAngleDegrees, a.AssistDegreesPerSecond)             &#124;&#124; a.AssistRange <= 0 &#124;&#124; a.AssistRange > 10 &#124;&#124; a.AssistHalfAngleDegrees <= 0 &#124;&#124; a.AssistHalfAngleDegrees > 90             &#124;&#124; a.AssistDegreesPerSecond <= 0 &#124;&#124; a.AssistDegreesPerSecond > 1440)` | Charge assist range, cone or turn rate is invalid. |
| `Charge` | `!Finite(p.TrackDegreesPerSecond, p.ChallengedTrackDegreesPerSecond, c.AnchorDistance, c.AnchorHeight,                 f.Damage, f.Angle, f.BaseKnockback, f.KnockbackGrowth)             &#124;&#124; p.TrackDegreesPerSecond < 0 &#124;&#124; p.TrackDegreesPerSecond > 1440             &#124;&#124; p.ChallengedTrackDegreesPerSecond < 0 &#124;&#124; p.ChallengedTrackDegreesPerSecond > 1440             &#124;&#124; p.ChallengedRearTicks == 0 &#124;&#124; p.ChallengedRearTicks > 120             &#124;&#124; c.AnchorDistance < 0 &#124;&#124; c.AnchorDistance > 3 &#124;&#124; c.AnchorHeight < 0 &#124;&#124; c.AnchorHeight > 3             &#124;&#124; c.MaxCarryTicks == 0 &#124;&#124; c.MaxCarryTicks > 120 &#124;&#124; c.FlingTicks == 0 &#124;&#124; c.FlingTicks > 120             &#124;&#124; f.Damage <= 0 &#124;&#124; f.Damage > 100 &#124;&#124; f.Angle < -90 &#124;&#124; f.Angle > 90             &#124;&#124; f.BaseKnockback < 0 &#124;&#124; f.KnockbackGrowth < 0 &#124;&#124; f.StunTicks > 240             &#124;&#124; p.ChestHitbox.StartBoneId != null &#124;&#124; p.ChestHitbox.EndBoneId != null &#124;&#124; p.ChestHitbox.HitGroup != 0             &#124;&#124; p.LanceHitbox.Damage <= 0 &#124;&#124; p.ChestHitbox.Damage != p.LanceHitbox.Damage` | Charge tracking, chest contact, anchor, carry or fling values are invalid. |
| `Leap` | `!Finite(p.RiseHeight, p.MaxRange, p.NoTargetDistance) &#124;&#124; p.RiseHeight <= 0 &#124;&#124; p.RiseHeight > 30             &#124;&#124; p.MaxRange <= 0 &#124;&#124; p.MaxRange > 60 &#124;&#124; p.NoTargetDistance < 0 &#124;&#124; p.NoTargetDistance > p.MaxRange             &#124;&#124; p.RiseTicks == 0 &#124;&#124; p.RiseTicks > 240 &#124;&#124; p.HangTicks == 0 &#124;&#124; p.HangTicks > 240             &#124;&#124; p.DropTicks == 0 &#124;&#124; p.DropTicks > 240 &#124;&#124; p.MaxFallTicks == 0 &#124;&#124; p.MaxFallTicks > 600             &#124;&#124; p.HitRecoveryTicks == 0 &#124;&#124; p.MissRecoveryTicks < p.HitRecoveryTicks &#124;&#124; p.MissRecoveryTicks > 600             &#124;&#124; p.Hitbox.DurationTicks == 0 &#124;&#124; p.Hitbox.DurationTicks > p.HitRecoveryTicks` | Leap heights, ranges, beats or recoveries are invalid. |
| `Slot` | `stages.Count != 1 &#124;&#124; op.Tick != 0 &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.MeleeCombo             &#124;&#124; slot.AimMode != AuthoringAimMode.None &#124;&#124; slot.AllowSlideCarry &#124;&#124; slot.ChargePool != null             &#124;&#124; stages[0].IasaTicks != 0 &#124;&#124; stages[0].Operations.Any(o => o != op && o is not EmitPresentationOperationSource)` | A v2 special needs one tick-zero lifecycle, one meleeCombo stage, no aim, carry, charge pool or IASA, with only presentation alongside it. |
| `Slot` | `clips.Any(id => id.Length > 0 && !stages[0].AnimationIds.Contains(id, StringComparer.Ordinal))` | A phase clip must be listed in the stage. |
| `Slot` | `stages.Count != 1 &#124;&#124; op.Tick != 0 &#124;&#124; slot.Behavior != AuthoringAbilityBehavior.MeleeCombo             &#124;&#124; slot.AimMode != AuthoringAimMode.None &#124;&#124; slot.AllowSlideCarry &#124;&#124; slot.ChargePool != null             &#124;&#124; stages[0].IasaTicks != 0 &#124;&#124; stages[0].Operations.Any(o => o != op && o is not CookedEmitPresentationOperation)` | A v2 special owns one tick-zero lifecycle in one meleeCombo stage, no aim, carry, charge pool or IASA, with only presentation alongside it. |
| `BoundsError` | `!Finite(x.LungeSpeed, x.StopDistance, x.AnchorDistance ?? 0f, c.Radius, c.OffsetX, c.OffsetY, c.OffsetZ,                 c.EndOffsetX, c.EndOffsetY, c.EndOffsetZ, c.Damage)             &#124;&#124; x.Launch is { } launch && !Finite(launch.Damage, launch.Angle, launch.BaseKnockback, launch.KnockbackGrowth)` | Command grab values must be finite. |
| `BoundsError` | `x.LungeTicks > 60 &#124;&#124; !(x.LungeSpeed >= 0f && x.LungeSpeed <= 60f) &#124;&#124; !(x.StopDistance >= 0f && x.StopDistance <= 5f)` | The lunge needs lungeTicks 0 to 60, a speed of 0 to 60 and a stopDistance of 0 to 5. |
| `BoundsError` | `c.RequiresFacingOwner &#124;&#124; c.StartBoneId != null &#124;&#124; c.EndBoneId != null &#124;&#124; c.HitGroup != 0             &#124;&#124; c.Shape is not (AuthoringHitboxShape.Sphere or AuthoringHitboxShape.Capsule)` | The capture is a root-relative sphere or capsule with authored hitGroup 0. |
| `BoundsError` | `!(c.Radius > 0f && c.Radius <= 3f) &#124;&#124; c.Damage < 0f &#124;&#124; c.BaseKnockback != 0f &#124;&#124; c.KnockbackGrowth != 0f             &#124;&#124; c.StunTicks != 0 &#124;&#124; c.FixedHitstunTicks != 0` | The capture holds instead of launching: a radius above 0 and at most 3, damage >= 0, no knockback or stun. |
| `BoundsError` | `x.LungeSpeed * Simulation.TickDt > 2f * c.Radius` | The lunge's travel per tick exceeds twice the capture radius, leaving gaps in its reach. |
| `BoundsError` | `x.CaptureTick == 0 &#124;&#124; x.CaptureTick > 120 &#124;&#124; c.DurationTicks == 0 &#124;&#124; c.DurationTicks > 30` | The capture opens at tick 1 to 120 for 1 to 30 ticks. |
| `BoundsError` | `x.ReleaseTick < x.CaptureTick + c.DurationTicks &#124;&#124; x.ReleaseTick > 240` | releaseTick must follow the capture window and be at most 240. |
| `BoundsError` | `x.AnchorDistance is { } anchor && !(anchor >= 0f && anchor <= 3f)` | anchorDistance must be 0 to 3. |
| `BoundsError` | `!(l.Damage > 0f && l.Damage <= 100f) &#124;&#124; l.Angle < -90f &#124;&#124; l.Angle > 90f &#124;&#124; l.BaseKnockback < 0f                 &#124;&#124; l.KnockbackGrowth < 0f &#124;&#124; l.StunTicks > 240` | The launch needs damage above 0 and at most 100, an angle of -90 to 90, no negative knockback and stun to 240. |
| `BoundsError` | `x.ReleaseLockTicks != 0` | A release with a launch has no releaseLockTicks. |
| `BoundsError` | `x.ReleaseLockTicks == 0 &#124;&#124; x.ReleaseLockTicks > 60` | A release without a launch holds the victim for releaseLockTicks 1 to 60 before the timeline's finisher. |
| `BoundsError` | `x.WhiffRecoveryTicks > 120` | whiffRecoveryTicks must be at most 120. |
| `SlotError` | `count != 1 &#124;&#124; stages != (p.HasHoldStage ? 2 : 1) &#124;&#124; tick != 0 &#124;&#124; behavior != AuthoringAbilityBehavior.MeleeCombo             &#124;&#124; aim != AuthoringAimMode.None &#124;&#124; carry &#124;&#124; charge &#124;&#124; otherOperations` | "A command grab needs one tick-zero lifecycle in a meleeCombo with no aim, carry or charge pool and only "                 + "presentation beside it: one stage, or with whiffRecoveryTicks a lunge stage and then the hold stage." |
| `SlotError` | `shellPolicy == ShellPolicy.Tuck` | A command grab cannot tuck. |
| `SlotError` | `p.ReleaseTick >= firstDuration` | releaseTick must fall inside the stage. |
| `SlotError` | `firstIasa != 0 && firstIasa <= p.ReleaseTick` | The stage cannot admit IASA before the release. |
| `SlotError` | `firstDuration != lungeTicks &#124;&#124; firstIasa != 0` | $"The lunge stage ends as the capture closes, after {lungeTicks} ticks, with IASA disabled." |
| `SlotError` | `p.ReleaseTick <= firstDuration &#124;&#124; p.ReleaseTick >= firstDuration + holdDuration` | releaseTick must fall inside the hold stage. |
| `SlotError` | `holdIasa != 0 && holdIasa <= p.ReleaseTick - firstDuration` | The hold stage cannot admit IASA before the release. |
| `BoundsError` | `!Finite(x.Distance) &#124;&#124; x.Distance <= 0 &#124;&#124; x.Distance > 12 &#124;&#124;             !Finite(x.Speed) &#124;&#124; x.Speed <= 0 &#124;&#124; x.Speed > 60 &#124;&#124; x.RecoveryTicks == 0 &#124;&#124; x.RecoveryTicks > 120` | Dash strike requires finite distance (0, 12], speed (0, 60], and recovery 1..120 ticks. |
| `BoundsError` | `x.Distance / (double)(x.Speed * Simulation.TickDt) > ushort.MaxValue` | Dash strike travel exceeds the supported tick budget. |
| `BoundsError` | `light.Shape != AuthoringHitboxShape.Sphere &#124;&#124; light.StartBoneId != null &#124;&#124; light.EndBoneId != null &#124;&#124;             tip.StartBoneId != null &#124;&#124; tip.EndBoneId != null &#124;&#124; light.HitGroup != 0 &#124;&#124; tip.HitGroup != 0` | Dash strike requires a root-relative light sphere and root-relative tip, with authored hitGroup 0. |
| `BoundsError` | `light.BaseKnockback != 0 &#124;&#124; light.KnockbackGrowth != 0 &#124;&#124; light.FixedHitstunTicks == 0 &#124;&#124; tip.DurationTicks == 0` | The light cut needs zero base/growth and positive fixed hitstun; the tip needs a positive lifetime. |
| `BoundsError` | `x.Speed * Simulation.TickDt > 2 * light.Radius` | Dash strike travel per tick exceeds twice the light radius, leaving gaps in cut coverage. |
| `BoundsError` | `x.StanceTicks is 0 or > 120 &#124;&#124; x.LegTicks is 0 or > 30 &#124;&#124; x.SheatheTicks is 0 or > 120 &#124;&#124;             x.RecoveryTicks is 0 or > 120 &#124;&#124; x.WhiffTicks is 0 or > 120 &#124;&#124;             x.RevealLeadTicks == 0 &#124;&#124; x.RevealLeadTicks > x.SheatheTicks` | Flash cut requires stance, sheathe, recovery and whiff of 1..120 ticks, legs of 1..30, and a reveal lead of 1..sheatheTicks. |
| `BoundsError` | `!Finite(x.LockRange) &#124;&#124; x.LockRange <= 0 &#124;&#124; x.LockRange > 20 &#124;&#124;             !Finite(x.PassThroughDistance) &#124;&#124; x.PassThroughDistance < 0 &#124;&#124; x.PassThroughDistance > 3 &#124;&#124;             x.MaxTargets is 0 or > MaxTargetsLimit` | Flash cut requires a lock range in (0, 20] m, a pass-through in [0, 3] m and 1..7 targets. |
| `BoundsError` | `x.PopAfterLock(x.MaxTargets) > MaxHoldTicks` | Flash cut legs and sheathe hold their victims too long: maxTargets × legTicks + sheatheTicks must be at most 240. |
| `BoundsError` | `cut.Shape != AuthoringHitboxShape.Sphere &#124;&#124; cut.StartBoneId != null &#124;&#124; cut.EndBoneId != null &#124;&#124; cut.HitGroup != 0` | The flash cut requires a root-relative sphere with authored hitGroup 0. |
| `BoundsError` | `!(cut.Damage > 0) &#124;&#124; cut.StunTicks == 0 &#124;&#124; !(cut.BaseKnockback > 0 &#124;&#124; cut.KnockbackGrowth > 0)` | The flash cut needs damage, stun and a launch: its knockback is the pop. |
| `BoundsError` | `x.LongestLeg / x.LegTicks > 2 * cut.Radius` | Flash cut legs can travel twice the lock range plus the pass-through; that per-tick travel must not exceed twice the cut radius, or cuts leave gaps. |
| `BoundsError` | `!float.IsFinite(homing.TurnDegreesPerTick) &#124;&#124; !(homing.TurnDegreesPerTick > 0f)                     &#124;&#124; homing.TurnDegreesPerTick > MaxTurnDegreesPerTick &#124;&#124; !float.IsFinite(norm)                     &#124;&#124; MathF.Abs(norm - 1f) > .00001f &#124;&#124; homing.TurnCos < 0f &#124;&#124; !(homing.TurnSin > 0f)` | homing.turnDegreesPerTick must be above 0 and at most 90. |
| `BoundsError` | `!(projectile.Speed > 0f)` | A homing projectile needs a positive speed. |
| `BoundsError` | `projectile.Homing != null && perch.Dive != null` | A perch with a dive block commits to its line: remove its homing block. |
| `BoundsError` | `perch.PerchTicks == 0 &#124;&#124; perch.PerchTicks > MaxPerchTicks` | perch.perchTicks must be 1 to 1200. |
| `BoundsError` | `projectile.Return != null` | A perching projectile cannot return. |
| `BoundsError` | `projectile.Damage != 0f &#124;&#124; projectile.BaseKnockback != 0f &#124;&#124; projectile.KnockbackGrowth != 0f                 &#124;&#124; projectile.StunTicks != 0` | A perching projectile lands without damage: its own damage, knockback and stun stay zero. |
| `BoundsError` | `!float.IsFinite(pop.Damage) &#124;&#124; !float.IsFinite(pop.Angle) &#124;&#124; !float.IsFinite(pop.BaseKnockback)                 &#124;&#124; !float.IsFinite(pop.KnockbackGrowth)` | The pop's values must be finite. |
| `BoundsError` | `!(pop.Damage > 0f && pop.Damage <= 100f)` | perch.pop.damage must be above 0 and at most 100. |
| `BoundsError` | `pop.Angle < -90f &#124;&#124; pop.Angle > 90f` | perch.pop.angle must be -90 to 90. |
| `BoundsError` | `pop.BaseKnockback < 0f &#124;&#124; pop.KnockbackGrowth < 0f` | The pop's knockback cannot be negative. |
| `BoundsError` | `pop.StunTicks > MaxPopStunTicks` | perch.pop.stunTicks must be 0 to 240. |
| `BoundsError` | `peck.IntervalTicks == 0 &#124;&#124; peck.IntervalTicks > perch.PerchTicks` | perch.peck.intervalTicks must be 1 to perch.perchTicks. |
| `BoundsError` | `!float.IsFinite(peck.Damage) &#124;&#124; !(peck.Damage >= 1f) &#124;&#124; peck.Damage > MaxPeckDamage                     &#124;&#124; peck.Damage != MathF.Floor(peck.Damage)` | perch.peck.damage must be a whole number from 1 to 20. |
| `BoundsError` | `projectile.Gravity != 0f` | A diving perch flies without gravity. |
| `BoundsError` | `!(projectile.Speed > 0f && projectile.Speed <= MaxDiveSpeed)` | A perching projectile rises at its speed: above 0 and at most 60. |
| `BoundsError` | `!float.IsFinite(dive.HoverHeight) &#124;&#124; !(dive.HoverHeight > 0f) &#124;&#124; dive.HoverHeight > MaxHoverHeight` | perch.dive.hoverHeight must be above 0 and at most 12. |
| `BoundsError` | `dive.MaxRiseTicks == 0 &#124;&#124; dive.MaxRiseTicks > MaxDivePhaseTicks` | perch.dive.maxRiseTicks must be 1 to 240. |
| `BoundsError` | `dive.MarkTicks == 0 &#124;&#124; dive.MarkTicks > MaxDivePhaseTicks` | perch.dive.markTicks must be 1 to 240. |
| `BoundsError` | `!float.IsFinite(dive.DiveSpeed) &#124;&#124; !(dive.DiveSpeed > 0f) &#124;&#124; dive.DiveSpeed > MaxDiveSpeed` | perch.dive.diveSpeed must be above 0 and at most 60. |
| `BoundsError` | `!float.IsFinite(dive.NoTargetDistance) &#124;&#124; dive.NoTargetDistance < 0f &#124;&#124; dive.NoTargetDistance > MaxNoTargetDistance` | perch.dive.noTargetDistance must be 0 to 20. |
| `BoundsError` | `projectile.MaxFlightTicks <= dive.MaxRiseTicks + dive.MarkTicks` | A diving perch's maxFlightTicks must outlast its rise and mark, leaving time to dive. |

### Retired capability IDs

These produce `capability.retired`.

| ID | Reason |
| --- | --- |
| `slop.internal.bonk.targeted-jump-slam.v1` | The Bonk-only targeted jump slam capability has been retired. |
| `slop.ability.charged-directional-dash.v1` | Charged directional dash has been retired. Use slop.ability.dash-strike.v1 (Quick Draw, ADR-0052). |
| `slop.internal.wibou.dash-slash.v1` | The Wibou dash-slash capability has been retired. |
| `slop.internal.wibou.blade-flurry.v1` | Blade Flurry v1 has been retired. Use slop.ability.flash-cut.v1 (the flash, ADR-0052). |
| `slop.internal.wibou.blade-flurry.v2` | Blade Flurry (Skyfall) has been retired. Use slop.ability.flash-cut.v1 (the flash, ADR-0052). |
| `slop.ability.shell-stance.v1` | Shell stance v1 (the skate and the bowl) has been retired. Use slop.ability.shell-stance.v2 with a splash and a ride (ADR-0050). |
| `slop.ability.steered-charge.v1` | Charge v1 has been retired. Use steered-charge.v2 with rear, tracking, chest and command carry (ADR-0059). |
| `slop.ability.targeted-leap.v1` | Cursor leap v1 has been retired. Use targeted-leap.v2 with rise, hang and drop (ADR-0059). |
| `slop.ability.clinch.v1` | Clinch v1 (Title Match) has been retired. Use slop.ability.command-grab.v1 with level, anchorDistance, releaseLockTicks and whiffRecoveryTicks (ADR-0057). |

### Budget accounting rules

| Counter | Increment | Condition |
| --- | --- | --- |
| operations | 1 | `each authored operation` |
| timelineOperations | 1 | `each authored operation` |
| hitboxes | 1 | `case SpawnHitboxOperationSource x:` |
| projectiles | 1 | `case SpawnProjectileOperationSource x:` |
| capabilities | 1 | `case StartCapabilityOperationSource x:` |
| hitboxes | 1 | `x.Parameters is TargetedLeapCapabilityParameters or SteeredChargeCapabilityParameters                             or ThrustCapabilityParameters { TrailHitbox: not null }` |
| hitboxes | 2 | `x.Parameters is DashStrikeCapabilityParameters` |
| hitboxes | 1 | `x.Parameters is FlashCutCapabilityParameters or CommandGrabCapabilityParameters` |
| projectiles | 1 | `x.Parameters is ChargedProjectileCapabilityParameters or TetherCapabilityParameters { AtTarget: null }` |
| hitboxes | 1 | `x.Parameters is TetherCapabilityParameters` |
<!-- END GENERATED CONTRACT -->
