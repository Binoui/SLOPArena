# Dusty grapple engine verification — 2026-11-05

Engine-only implementation of accepted ADR-0063. No Unity Editor or gateway operation was performed.

## Automated evidence

- Full Shared suite: 4,861 passed, zero failures (5.13 minutes).
- Documentation checks passed (463 Markdown files scanned).
- Final focused grapple, snapshot contract and Arcade compatibility suite: 33 passed.
- Full Server suite: 99 passed, zero failures (4.26 minutes).
- Shared and Server builds: passed with `-p:SkipUnityPluginCopy=true`.
- The full Shared run includes CourseFinishability, CharacterCourse, and both-direction Nyx Manor crossing checks across the roster. Packages remain unchanged; these establish existing course behavior, not acceptance of Dusty's future package switch.

## Existing package parity

Saved the pre-feature Shared assembly after the catch-only refactor commit `68cacef5` (the compiler was unchanged from base `ce7e09a5`). A standalone net8 runner invoked `CharacterPackageCompiler.Compile(packageJson, characterJson, CharacterCookProfile.TrustedBuiltIn)` for every directory containing `package.json` under `client/Unity/Assets/CharacterPackages`. It saved each `CookedPackage.CanonicalBytes`. Repeated with the final Shared assembly and compared all seven outputs byte for byte. All matched. `git diff ce7e09a5 -- content-cooked client/Unity/Assets/CharacterPackages` is empty.

| Package | Bytes | SHA-256 (identical before/after) |
| --- | ---: | --- |
| bashie | 38281 | `b6006bcd376da0a077cfcb7d63a5c56a74a7fff01c13c0fe4fd81b06aa8b5b26` |
| bonk | 42012 | `c04e21203d9a70c8d756c9b1fcaa488c776acfa46c5382e1596ed96193408b94` |
| fightguy | 48449 | `ddff950ab63767e92180eca95ab0f351050a6a3a7b08014d9e171864e465db59` |
| manki | 29224 | `9403d79f7c04d9fdd1fbff164c9e7c78405478adc2259f8cae19d864a32b183e` |
| nyx | 61370 | `de952aa61cd92565053b1b0d4cf39247bccc89240200a71b438e5954ed7bf226` |
| salamander | 22685 | `d74895ebe55460969943abc99ca8c5f0bae6e7417acae4927d0ba3df8cc2711a` |
| wibou | 29773 | `485bd4b92540db8aef5daf39d8a40cb1538c1e55fced05278ef79428665cf384` |

## Search cost spot measurement

Selected the baked arena with the most collision triangles: `hamster_habitat`, 6,192 triangles. At world position (-34.5, 0, 0), using the salamander body, prototype parameters and caller-owned triangle scratch, 10 warmups followed by 200 anchor queries averaged 0.424 ms/query; all 200 found an anchor. Debug .NET build, concurrent Shared test activity, one fixed position. This is a smoke measurement, not a worst-case bound or a release benchmark. Scratch storage was 24,768 bytes, allocated by the caller before timing. The 200 queries allocated zero bytes beyond the 40-byte Stopwatch used by the harness.

The tie-order regression also verifies that triangle stages reject the drop resolver's heightmap fallback outside the physical lip. Heightmap-only stages continue to use heightmap support.

## Package-switch handoff

Replace ground.E/air.E with one tick-zero `slop.ability.grapple.v1` version `1` in a single `meleeCombo` recovery stage; IASA zero, `aimMode: none`, ordinary slot cooldown. Set cooked motion/timing parameters, a straight loop projectile (zero gravity/yaw/knockback/stun, no return/homing/perch) and the ordinary boot kick hitbox. API minimum 1.23.0 is selected automatically only for opted-in packages. Optional phase clip IDs are `outAnimationId`, `haulAnimationId`, `popAnimationId`, `zipAnimationId`, `kickAnimationId`, `missAnimationId`; list those clips after the throw clip in the stage's sorted animation IDs.

The salamander package switch, right-hand/active-slot rope presenter, authored clips and Ability Lab UI remain deferred to the canonical Unity checkout. Haul/out presentation reads `ActiveMoveView.Spot`; zip reads the interaction partner; miss reads `PresentationEventSource.GrappleMiss`. Training feel, controller and visual acceptance remain for that integration. No approved gameplay decision was changed.
