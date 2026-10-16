# Character Import Checklist

## Authoritative authoring modules

- [ ] `package.json` owns package identity, version, creator, license, attribution, and dependencies.
- [ ] `character.json` owns gameplay source, presentation IDs, and the canonical sixteen-slot grid.
- [ ] `CharacterAssetCatalog.asset` owns the Unity rig, semantic clip bindings, and pose-track IDs.
- [ ] All three modules use schema version 1 and the same stable package ID.
- [ ] Package-local source assets retain their Unity `.meta` files and have confirmed licensing.

## Rig and semantic bindings

- [ ] Import the rig as Humanoid when appropriate; inspect the Avatar before binding clips.
- [ ] Reject invalid orientation, scale, root motion, required-bone, or non-finite-pose diagnostics.
- [ ] Bind every required semantic animation ID to its exact intended imported clip.
- [ ] Set **Import Materials** to None on animation-only FBX sources. Otherwise Unity resolves their embedded texture names against unrelated project textures, and the cook source hash differs between your Editor and the isolated release import.
- [ ] Assign one unique deterministic pose-track ID to each semantic binding.
- [ ] Confirm unresolved bindings report diagnostics; never fall back to another character.

## Source and inspect

- [ ] Define `ground.1` through `ground.F` and `air.1` through `air.F` as the canonical sixteen slots.
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
- [ ] Verify a failed cook preserves the prior four payloads, generated cache, and cook status;
      for a new package with no prior artifact, verify the cooked directory remains absent.
- [ ] Admit a package only after successful cook, complete presentation assets, and kit tests.
- [ ] Add the exact version/content/package hash requirement to the Built-In Roster Manifest.
- [ ] Verify a fresh `MatchContentCatalog` resolves the package by identity and hash.
- [ ] Confirm missing, tampered, mismatched, or unrostered content fails closed.

## Bonk pipeline probe

- [ ] Confirm all eight standard semantic bindings resolve, including shared dash and hit-reaction clips.
- [ ] Run inspect and cook; record `packageId: bonk`, sixteen slots, successful cook, and hashes.
- [ ] Confirm cooked Bonk payloads exist but no roster requirement or Character Select entry exists.
- [ ] Confirm package-local dependency changes queue Bonk without stale-marking unrelated packages.
- [ ] Confirm failed recook preserves the valid Bonk payloads and generated catalog.

## Verification

- [ ] Run the equivalent package verifier (and `scripts/verify-fightguy-package.sh` for FightGuy).
- [ ] Build Shared and Server and run the Shared test suite.
- [ ] Recompile Unity and read current console errors.
- [ ] Exercise Ability Lab, Training, and a local match only after the package is roster-admitted.
- [ ] Add `KitScenario` golden coverage only when gameplay behavior is actually specified.

Do not add legacy factories, raw runtime source loaders, manual animation configs, standalone
skeleton ownership, or a second persisted slot mapping.
