# Character Import Checklist

## Authoritative authoring modules

- [ ] `package.json` owns package identity, version, creator, license, attribution, and dependencies.
- [ ] `character.json` owns gameplay source, presentation IDs, and the canonical fourteen-slot grid.
- [ ] `CharacterAssetCatalog.asset` owns the Unity rig, semantic clip bindings, and pose-track IDs.
- [ ] `package.json` uses `manifestSchemaVersion: 1` and owns the stable `packageId`.
- [ ] `character.json` uses `authoringSchemaVersion: 3` without duplicated identity or
      legacy `id`, `class`, or `schemaVersion` fields. Check the
      [source codec](../../src/Shared/CharacterPackageSourceCodec.cs).
- [ ] The [asset catalog](../../client/Unity/Assets/Scripts/Runtime/Animation/CharacterAssetCatalog.cs)
      uses catalog schema 1 and the same package ID as `package.json`.
- [ ] Package-local source assets retain their Unity `.meta` files and have confirmed licensing.

## Rig and semantic bindings

- [ ] Import the rig as Humanoid when appropriate; inspect the Avatar before binding clips.
- [ ] Reject invalid orientation, scale, root motion, required-bone, or non-finite-pose diagnostics.
- [ ] When replacing a character's model, scale the export so `Animator.humanScale` matches
      the previous rig rather than its height. The body is placed relative to the renderer root
      in `humanScale` units, so this keeps the feet grounded with the unchanged `modelYOffset`.
      Then compare the idle hips-to-sole distance with `hipHeight`, and measure the lowest
      skinned vertex against the floor in Training. Manki v3 sank 2.9 cm at matched height and
      0.7 cm at matched scale; see [Manki](manki.md).
- [ ] Check the fighter against the [roster size](adding-a-new-character.md#roster-size) and
      every clip's foot contact with [`tools/character_sizes`](../../tools/character_sizes/README.md).
      Resize with `presentation.visualScale`; fix a single floating or sinking clip with its
      root Y offset rather than `modelYOffset`.
- [ ] When several meshes share one skeleton (forms, outfits), fit every mesh to that
      skeleton's measured joint pivots, the torso and spine included. Nyx's first fit kept the
      real form's torso as modelled: her knees spread 8.8 cm under the dress and her spine
      pivots sat 2 to 6 cm in front of her body, so she bent like a broken spine.
- [ ] Skin a long skirt or dress to spring bone chains (`BoneChainSpring`) with leg capsules,
      not Unity Cloth. Two Cloth attempts on Nyx's dress rested as a lampshade, collapsed
      between the legs and swung the hem 1.6 m on turns.
- [ ] Bind every required semantic animation ID to its exact intended imported clip.
- [ ] Set **Import Materials** to None on animation-only FBX sources. Otherwise Unity resolves their embedded texture names against unrelated project textures, and the cook source hash differs between your Editor and the isolated release import.
- [ ] Assign one unique deterministic pose-track ID to each semantic binding.
- [ ] Confirm unresolved bindings report diagnostics; never fall back to another character.

## Source and inspect

- [ ] Define `ground.1` through `ground.F` and `air.1` through `air.F` as the canonical fourteen slots (no normal `2`).
- [ ] Author fixed timelines in 60 Hz ticks from an approved kit specification.
- [ ] Run `sloparena.character.inspect` before cooking and record identity, slot projection,
      status, hashes, stale reasons, and structured diagnostics.

## Four cooked payloads

- [ ] `manifest.json` pins identity, versions, dependencies, capabilities, and hashes.
- [ ] `character.runtime.json` contains the normalized Shared runtime definition.
- [ ] `poses.bin` contains deterministic pose data from the exact rig/import/catalog inputs.
- [ ] `client.bindings` contains generated semantic bindings and pose-track metadata.
- [ ] Confirm all four payloads, source hash, cooked-content hash, package hash, and dependency
      records agree byte-for-byte with the successful cook result.
- [ ] Treat generated client bindings as a cache; do not edit cooked output as source.

## Failed cook and admission

- [ ] A semantic cook failure returns `success: false` with structured diagnostics.
- [ ] Verify preparation failures leave the prior cook intact and publication failures
      restore it without rollback errors. With no prior artifact, the cooked directory
      remains absent. See the [cook mutation and failure scope](adding-a-new-character.md#5-cook-and-verify-four-payloads).
- [ ] After a roster refresh error, inspect payloads, generated cache, cook status, and
      roster pins. Publication precedes refresh and may already have succeeded.
- [ ] For an already-rostered package, confirm successful cook refreshed its exact pin
      while preserving its selector and other entries. Automatic refresh is not acceptance.
- [ ] Admit a package only after successful cook, complete presentation assets, and kit tests.
- [ ] For new admission, add the exact version/content/package hash requirement to the Built-In Roster Manifest.
- [ ] Verify a fresh `MatchContentCatalog` resolves the package by identity and hash.
- [ ] Confirm missing, tampered, mismatched, or unrostered content fails closed.

## Unrostered pipeline probe

Bonk is rostered in the [roster manifest](../../content-cooked/roster/manifest.json).
Use a throwaway unrostered fixture for these checks, not Bonk.

- [ ] Confirm the fixture's required semantic bindings resolve to intended assets.
- [ ] Run inspect and cook; record the fixture's stable package ID, fourteen slots,
      successful cook, and hashes.
- [ ] Confirm cooked fixture payloads exist but no roster requirement or Character Select entry exists.
- [ ] Confirm package-local dependency changes queue the fixture without stale-marking unrelated packages.
- [ ] Confirm a preparation-failing recook preserves its valid payloads and generated catalog.

## Verification

- [ ] Run the equivalent package verifier (and `scripts/verify-fightguy-package.sh` for FightGuy).
- [ ] Build Shared and Server and run applicable Shared tests with `-p:SkipUnityPluginCopy=true`.
- [ ] Recompile Unity and read current console errors.
- [ ] Exercise Ability Lab for source-preview checks. After admission, exercise affected
      Editor Training behavior. These paths compile live source, even when status is **Cooked**.
- [ ] Execute the saved package through pinned-catalog Shared gameplay tests or a player
      check, following [persisted gameplay verification](adding-a-new-character.md#verify-persisted-gameplay).
      Cover the complete agreed kit for acceptance and record identity, hashes, and outcomes.
      Disk verification alone does not prove persisted gameplay.
- [ ] Complete applicable local gameplay and human presentation/gamefeel acceptance.
- [ ] Add focused `KitScenario` assertions only when gameplay behavior is specified;
      follow the [kit regression workflow](../../skills/sloparena-kit-regression-testing/SKILL.md).

Do not add legacy factories, raw runtime source loaders, manual animation configs, standalone
skeleton ownership, or a second persisted slot mapping.
