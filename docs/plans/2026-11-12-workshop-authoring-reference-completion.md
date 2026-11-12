# Workshop authoring reference completion

The owner wants a creator or a fresh agent to author fighter gameplay from one reference and validate it without opening engine source.
This pass replaces syntax scraping with definitions that the Shared reader and compiler use directly.

## The definition and its consumers

[CharacterSourceSchema.xml](../../src/Shared/CharacterSourceSchema.xml) contains 60 object definitions and 514 field definitions.
Every field has an explicit name, type, required/default contract, unit, description, and primitive diagnostic identities.
It also supplies enum tokens, child schemas, conditional presence rules, source versions, 105 per-field gameplay ranges,
35 shared scalar-range references, public capability identities, operation units, and budget accounting.

[CharacterSourceSchema.cs](../../src/Shared/CharacterSourceSchema.cs) exposes immutable objects, fields, ranges, capability features,
and lookup methods. A field exposes its storage range, gameplay range or shared range ID, default JSON, null policy,
conditional requiredness, child schema, unit, description, and diagnostic codes.
The codec uses these definitions for accepted keys, primitive type/storage checks, scalar and object defaults, enum admission,
required fields, and precise source provenance. Compiler and contract validators read the shared scalar limits and budget costs.
Typed record construction remains ordinary C#; no creator expressions or executable schema language were added.

[AuthoringReference.cs](../../tools/KitCheck/AuthoringReference.cs) prints these definitions and the existing budget constants and bone registry.
It does not open C# source, inspect compiler statements, or need a repository to print the contract.
All `Microsoft.CodeAnalysis` references were removed. No package or tool was installed.
The Shared build uses an XML description check and fails if any field has a missing or blank description.
A temporary removal of the first description produced build exit 1 with the expected message; the original definition was restored.

## Deliberate hand-written validation exceptions

All Workshop field tables and one-line meanings are generated from the definitions. The reference's walkthrough, gameplay explanations,
and the following remaining constraint descriptions are hand-written. These checks retain their existing admission and diagnostic grouping.
Flattening them into early property validation would change which diagnostics are collected or require a new rule interpreter.
An editor must still compile the complete kit after field edits.

| Context and exact fields | Constraints retained in code |
| --- | --- |
| Manifest `packageId`, `version`, `creator`, `license`, `attribution`, dependency `packageId/version/cookedHash`; semantic IDs throughout the document | Identifier grammar, SemVer grammar, nonempty rights/dependency metadata, uniqueness, and resolved references. |
| Character `shieldRadius/capsuleHeight`; capture anchors; bone `endBoneId/endOffsetX/endOffsetY/endOffsetZ`; `attachmentBoneIds` | Cross-dimension geometry, finite-value grouping, rig-name rules, and an end bone for end offsets. |
| Aliases `from/to`; all slot `id` values | Canonical-grid resolution, explicit/alias conflicts, and cycle detection. |
| Slot `behavior/aimMode/aimMovement/isRecoveryMove/allowSlideCarry/chargePool/shellPolicy/aimMovementSpeedScale` | Eligibility and conflicts between move admission, movement ownership, recovery, aim and shell state. |
| Stage `durationTicks/iasaTicks/landingLagTicks/autoCancelBeforeTicks/autoCancelAfterTicks/animationIds/operations` | Coupled timing, stage-relative operations, animation references and whole-timeline watchdogs. |
| Stage `warpRange/useTargetLock/rotateTowardTarget/trackingStrength`; startup correction `tick/endTick`, acquisition and rate fields | Retired targeting defaults, one correction window, startup commitment ordering, and positive rates for nonzero correction. |
| Hitbox `startBoneId/endBoneId/hitGroup/stunTicks/fixedHitstunTicks/hitPresentationId`; placement `boneId/attachmentMode/localScaleX/localScaleY/localScaleZ/durationTicks` | Bone and cue references, fixed-stun gating, and placement geometry. |
| Aimed recovery `startupTicks/directionalVerticalSpeed/verticalSpeed/preparationRiseSpeed/hop`, hop `startTicks/contactTicks` | Directional speed versus neutral speed, hop sequencing, zero preparation rise with a hop, and stage lifetime. |
| Charged projectile `maxChargeTicks/tier2Ticks/tier3Ticks/fireTriggerTick/tier2Radius/tier3Radius/projectile`; charged stream `maxChargeTicks/fireTriggerTick/emitIntervalTicks/minStreamTicks/maxStreamTicks/maxBaseKnockback/minRecoilSpeed/maxRecoilSpeed/projectile` | Threshold/radius ordering, companion-operation conflicts, positive release/stream/projectile lifetimes, recoil ordering and forbidden projectile modes. |
| Finger guns `projectile/reloadAnimationId`; phase clips and presentation IDs on every lifecycle | Projectile mode restrictions and phase/cue references, order and lifecycle compatibility. |
| Command grab `capture/launch/captureTick/releaseTick/releaseLockTicks/whiffRecoveryTicks/lungeSpeed/anchorDistance/level`; capture's hitbox members | Capture-only geometry and zero launch/stun, swept coverage, capture/release ordering, launch versus lock release, and one/two-stage layout. |
| Dash strike `lightHitbox/tipHitbox/tipPoseTick/distance/speed/recoveryTicks`; flash cut `cutHitbox/lockRange/maxTargets/legTicks/passThroughDistance/sheatheTicks/revealLeadTicks` | Root geometry, light/held hit reaction, per-tick coverage, maximum hold duration and watchdog relationships. |
| Targeted leap `hitbox/noTargetDistance/maxRange/hitRecoveryTicks/missRecoveryTicks`, phase and seek fields; steered charge `lanceHitbox/chestHitbox/carry/skidTicks/missEndingTicks`, phase fields | Landing-only geometry and watchdogs, carry/fling relationships, phase compatibility and clip ordering. |
| Thrust `aim/targetConeDegrees/speed/maxSpeed/thrustTicks/trailHitbox`; tether `projectile/atTarget/retractSpeed/retractAnimationId/retractPresentationId`, and placed/unmarked geometry | Aim-dependent cone, speed ordering, travel-frame shape, thrown/placed mode exclusions, height versus radius, target-distance ordering and stage watchdogs. |
| Shell `splash/ride`, their hitboxes, slot `shellPolicy`; cannonball distance/flight pairs; alternate form and swap operations | Required block/feature pairing, one ride owner, tuck constraints, wave capture semantics, ballistic watchdogs and form-grid/swap rules. |
| Projectile `return/homing/perch/challengeTicks`; every member of return, return-leg hit, homing, perch, pop, dive and peck | Returning/perching validators remain intact, including their scalar limits and mode dependencies. Their exact bounds are written in the reference. This module also serves cooked loading and has coupled mark/pop/form behavior. |

The generated range column distinguishes machine-readable scalar limits from these additional constraints.
No claim is made that isolated field validation replaces the compiler's geometry or lifecycle validation.

## Evidence that behavior and admission stayed unchanged

The initial baseline capture preceded compiler edits. It used original head `8fd127e65f97ef84a0efd2aeb5e75908dbad9023`.
The final corpus additionally captures an unused-presentation warning using an offline source archive of that same original commit.
That archive was not a Git worktree and did not touch the main checkout or Unity.

The [baseline fixture](../../tests/Shared.Tests/Fixtures/WorkshopContract/baseline.json) saves complete input JSON,
profile, canonical compiled bytes, and the full original diagnostic list, including original messages and paths.
[WorkshopContractCorpusTests](../../tests/Shared.Tests/WorkshopContractCorpusTests.cs) recompiles every case and compares exact bytes
and the complete ordered severity/code sequence. Paths and messages are allowed to improve.

The comparison passes for 43 cases, 11 successful byte outputs, and all 85 recorded diagnostics across 34 distinct codes.
The ten principal successes are seven built-ins, the first example, the proof kit, and the knight.
The eleventh success is a warning-bearing mutation. Other inputs mutate the first example to trigger the remaining diagnostics.
All seven built-in authoring files still exactly match their captured inputs. No files under `client/Unity/` or `content-cooked/` changed.
No Unity operation or publishing cook was run.

| Original source | Canonical bytes | SHA-256, unchanged after the refactor |
| --- | --- | --- |
| fightguy | 48449 | `ddff950ab63767e92180eca95ab0f351050a6a3a7b08014d9e171864e465db59` |
| wibou | 29773 | `485bd4b92540db8aef5daf39d8a40cb1538c1e55fced05278ef79428665cf384` |
| manki | 29224 | `9403d79f7c04d9fdd1fbff164c9e7c78405478adc2259f8cae19d864a32b183e` |
| bonk | 42012 | `c04e21203d9a70c8d756c9b1fcaa488c776acfa46c5382e1596ed96193408b94` |
| bashie | 38281 | `b6006bcd376da0a077cfcb7d63a5c56a74a7fff01c13c0fe4fd81b06aa8b5b26` |
| salamander | 22687 | `e48543e0d638604e6890638d82d60cd39952ee6b9fad6f2af0d961c3073f7423` |
| nyx | 61370 | `de952aa61cd92565053b1b0d4cf39247bccc89240200a71b438e5954ed7bf226` |
| example | 14677 | `30dba947279484964bc13cc9517bc2c6aac1cf971f8a4f3c0e5fc7083f66255e` |
| proof | 23858 | `45183be6facbfdb8f6ec9abf9f259620d8284b36caf51ac705a28545a2c261c6` |
| knight | 17065 | `b932e9aec13296df87438bd894b4ee132e8c5c8d941e679efec28b55f8a78536` |

## Source for the gameplay explanations

These explanations were checked against the following runtime files, not inferred from field names.

| Explanation | Source read |
| --- | --- |
| Launch formula, weight, ordinary and fixed hitstun | [Simulation.ApplyKnockback](../../src/Shared/Simulation.cs), [CombatTuning](../../src/Shared/CombatTuning.cs), and damage accumulation in [ServerSimulation](../../src/Shared/ServerSimulation.cs). |
| Ground ramp, dormant acceleration/rush fields, air steering, linear drag, jump speeds and resources | [Simulation.ProcessNormalMovement, ProcessAirMovement and RefreshGroundResources](../../src/Shared/Simulation.cs), plus [MovementStats](../../src/Shared/CharacterDefinition.cs). |
| Float age, recovery reset, and walk-offs skipping the low-gravity window | [Simulation.ApplyGravity and ApplyWalkOffTransition](../../src/Shared/Simulation.cs), and recovery admission in [ServerSimulation](../../src/Shared/ServerSimulation.cs). |
| Aimed recovery setup, input sample, steering, gravity and completion | [AimedRecoveryAbility](../../src/Shared/Abilities/AimedRecoveryAbility.cs), [CookedTimelineAbility](../../src/Shared/Abilities/CookedTimelineAbility.cs), and [ServerSimulation](../../src/Shared/ServerSimulation.cs). |
| Armor timers and ordinary hit reaction | [ServerAbility](../../src/Shared/Abilities/ServerAbility.cs), [ServerSimulation.HitReaction](../../src/Shared/ServerSimulation.HitReaction.cs), and armored contact handling in [ServerSimulation](../../src/Shared/ServerSimulation.cs). |
| `interruptible` is carried but not read by Shared contact resolution | [AttackData](../../src/Shared/AttackData.cs), [CookedCharacterRuntimeAdapter](../../src/Shared/CookedCharacterRuntimeAdapter.cs), [SpellResolver](../../src/Shared/SpellResolver.cs), [ServerSimulation](../../src/Shared/ServerSimulation.cs), and the snapshot codec. A source search of all Shared reads found no simulation consumer. |
| Hurtbox capsule-center frame, bone priority, hips and fitted body scale | [ServerSimulation](../../src/Shared/ServerSimulation.cs), [CharacterDefinition](../../src/Shared/CharacterDefinition.cs), and [HitboxGeometry](../../src/Shared/HitboxGeometry.cs). |
| Cancel inclusivity, normal versus special landing, cooldown start and shared slot banks | [Simulation.IsIasaUnlocked](../../src/Shared/Simulation.cs), [ServerSimulation landing and RetireAbility](../../src/Shared/ServerSimulation.cs), and [Forms](../../src/Shared/Forms.cs). |
| Forward lunge replacing horizontal velocity, preserving Y, then stopping | [CookedTimelineAbility.StartForwardLunge and ApplyForwardLunge](../../src/Shared/Abilities/CookedTimelineAbility.cs). |
| Command-grab capture, anchors, level lunge, release and whiff layout | [CommandGrab](../../src/Shared/CommandGrab.cs), [CommandGrabAbility](../../src/Shared/Abilities/CommandGrabAbility.cs), and [ServerSimulation.CommandGrab](../../src/Shared/ServerSimulation.CommandGrab.cs). |
| Model fitting and unused model-sole/automatic-offset metadata | [CharacterDefinition](../../src/Shared/CharacterDefinition.cs), [HitboxGeometry](../../src/Shared/HitboxGeometry.cs), and a read-only inspection of [PlayerRenderer](../../client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs). The Editor was never operated. |

Two corrections are significant for authors. Walking off a platform skips the float window; it does not start a gravity-free hover.
Also, `isRecoveryMove` itself only resets that clock. It does not enforce a hidden once-per-airtime recovery stock.

## Fresh-agent findings

Numbers follow the full fresh-agent log. Its postscript adds finding 16.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | Malformed capability catch-all table | Removed scraping and the catch-all; each public capability has an explicit schema. |
| 2 | Homing/perch falsely required | Both are optional with null omission defaults; removal tests validate both examples. |
| 3 | No complete grab or capture schema | Added a complete grappler kit with command grab and recovery; capture links directly to Hitbox. |
| 4 | Weight/launch numbers unexplained | Added exact damage/weight formula, tuning, hitstun formula and worked numeric outcomes. |
| 5 | Movement names unexplained | Every field has a one-line meaning and unit; prose identifies dormant fields and the actual movement rules. |
| 6 | Recovery lifecycle unexplained | Added press-to-launch-to-completion behavior, input sampling, steering, gravity, interruption and resource semantics. |
| 7 | Armor and interruptible unexplained | Documented damage-taking armor and grab bypass; explicitly identified interruptible as an unconsumed serialized flag. |
| 8 | Hurtbox frames and priority unclear | Documented capsule-center fallback, baked-bone replacement and hip-pivot distinctions. |
| 9 | Cancel/landing/cooldown rules missing | Added inclusive boundaries, cancellation gates, special landing continuation and cooldown at exit. |
| 10 | Lunge velocity semantics missing | Documented facing snapshot, horizontal replacement, preserved Y, end brake and geometry stop. |
| 11 | Inverted/raw C# conditions | Removed statement scraping and raw predicate sections; scalar definitions are data and remaining relationships are plain prose. |
| 12 | Slot roles, variants and aliases unclear | Added role conventions, selection at activation, no landing variant swap, alias expansion and shared cooldowns. |
| 13 | Source success does not prove playable assets | Retained and clarified the not-defined-yet boundary without inventing formats. |
| 14 | Repository-only invocation | Documented publishing and DLL invocation; publish includes the reference and both examples and works outside the checkout. |
| 15 | Raw headings, empty enums, duplicate tables and shell type | Generated stable object titles and child links; no empty enum section or parser fragments, and both meanings of shell have their correct types and contexts. |
| 16 | Uppercase animation IDs and indistinguishable errors | Put lowercase/prefix rules next to naming guidance; diagnostics include the offending value and exact move/stage/animation index. |

## Verification

- Focused compiler, codec and Workshop checks passed, 271 tests with zero failures. The final focused run also covers the reader object-default integration and diagnostic provenance cleanup.
- Requiredness tests remove 667 required and 284 optional fields from the first example, and 697 required and 296 optional fields from the grappler. All 1,944 removals match the documented result.
- Both examples compile with zero diagnostics under Workshop.
- Missing-description build guard was tested by removing a description, observing exit 1, then restoring it.
- The published DLL has no CodeAnalysis assembly or dependency and generates its contract outside the repository.
- Full Shared suite passed on the final source, 5,580 tests with zero failures or skips in 10 minutes 4 seconds. Documentation checks passed across 472 Markdown files. Every MSBuild invocation used `-p:SkipUnityPluginCopy=true`.
- Publication below is an offline `dotnet publish`, not a Steam/Workshop upload or a publishing cook.

Published command evidence follows. These commands ran from `/tmp/workshop-portable-run`, outside the repository.
The broken variants add an unknown capture field, set lunge speed negative, or declare an untrusted capability.
A fourth diagnostic exercise recreates the eight uppercase animation errors.

```text
$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-kitcheck-final/example-kit
COMPILE OK (0 diagnostics)

Exit 0

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-kitcheck-final/example-grappler
COMPILE OK (0 diagnostics)

Exit 0

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-portable-run/proof
COMPILE OK (0 diagnostics)

Exit 0

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-portable-run/knight
COMPILE OK (0 diagnostics)

Exit 0

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-portable-run/bad-unknown
Error field.unknown character.json.slots[6].timeline.stages[0].operations[0].parameters.capture.unknownField: Unknown field.
COMPILE FAILED

Exit 1

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-portable-run/bad-range
Error value.out-of-range character.json.slots[2].timeline.stages[0].operations[0].speed: Speed must be greater than zero.
COMPILE FAILED

Exit 1

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-portable-run/bad-untrusted
Error capability.untrusted character.json.capabilityRequirements[1].capabilityId: Trusted built-in capabilities are not allowed in Workshop profile.
COMPILE FAILED

Exit 1

$ dotnet /tmp/workshop-kitcheck-final/KitCheck.dll /tmp/workshop-portable-run/bad-animation
Error id.invalid character.json.slots[3].timeline.stages[0].animationIds[0]: ID 'anim.move.ground-A' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[4].timeline.stages[0].animationIds[0]: ID 'anim.move.ground-E' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[5].timeline.stages[0].animationIds[0]: ID 'anim.move.ground-R' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[6].timeline.stages[0].animationIds[0]: ID 'anim.move.ground-F' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[10].timeline.stages[0].animationIds[0]: ID 'anim.move.air-A' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[11].timeline.stages[0].animationIds[0]: ID 'anim.move.air-E' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[12].timeline.stages[0].animationIds[0]: ID 'anim.move.air-R' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Error id.invalid character.json.slots[13].timeline.stages[0].animationIds[0]: ID 'anim.move.air-F' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
COMPILE FAILED

Exit 1
```

Branch is `t3/workshop-fighter-authoring`. The final commit is identified in the handoff. Nothing was pushed and no PR was opened.

Build the Lever shaped the generator and frozen corpus runner. Model the Domain shaped the shared field registry. Both principle leaf skills were read for this work.
