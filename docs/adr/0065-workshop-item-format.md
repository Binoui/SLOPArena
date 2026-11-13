# ADR-0065: What a Workshop item holds

**Status:** Accepted 2026-11-11. The owner agreed ("i agree with it") after step 0.1b. Its three
corrections to the GLB requirements are folded in below.
**Deciders:** @Binoui
**Related:** [ADR-0025](0025-workshop-package-architecture.md),
[ADR-0026](0026-workshop-multiplayer-synchronization.md),
[ADR-0064](0064-workshop-creator-route.md),
[Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md)

## Context

ADR-0025 fixed what identifies a WorkshopPackage, but not its files, ID rule, or payload. A
built-in package's `client.bindings` stores Unity Editor object IDs (`CharacterBindingWriter.cs:25`),
so it cannot travel. Phase 0 evidence is in `tmp/workshop-spikes/`.

## Decision

### Item and manifest

A Workshop item is one Steam folder holding one revision. `item.json` has a schema version, the
type, the item version, each payload file's SHA-256, creator, license, attribution, an AI-assisted
flag, and for a model swap the base fighter's package ID. A strict codec refuses unknown fields,
missing fields, and wrong hashes with a named diagnostic. The hash map also goes into the item's
Steam metadata, which queries return without a download.

- **`model-swap`** holds `item.json`, one GLB, and an optional `humanoid.json` bone map. It names the base fighter by package ID only,
  because it never reaches the simulation.
- **`fighter`** holds the four cooked files, one GLB, a portrait, and the editable source. Its
  bindings use a new schema that names GLB clips and `slop.*` IDs.
- **`stage`** comes later.

### Presentation payload

Each item has one GLB, loaded with glTFast.

- **Path A, for fighters,** sets joint transforms from the GLB's own clips at the current tick,
  because Unity creates only legacy clips at runtime and those cannot use Animancer. It cost 13.4 µs
  per fighter per frame against 62.5 µs built-in (step 0.1, median).
- **Path B, for model swaps,** builds a Humanoid avatar at runtime and plays the base fighter's
  built-in clips. It worked for `bonk.glb` and `bashie.glb`, and for a raw Tripo character that
  never went through Unity (step 0.1b, `tmp/workshop-spikes/tripo-swap/`).

The GLB must carry the following.

| Requirement | Why |
| --- | --- |
| One clip per semantic animation ID, named by it | Bindings and `poses.bin` key on these IDs |
| Each clip's frame count | From the GLB alone, 7 of 22 FightGuy clips were one frame short, and a bot Match's hits fell from 61 to 7 (step 0.2) |
| 60 Hz keys, keyframe k on tick k, read through one sampler shared by bake and renderer | 20 fps keys lost up to 3,560 mm on Bonk, and the interpolation rule alone moved 318 mm (step 0.2) |
| Mixamo bone names, or a `humanoid.json` bone map beside the GLB | `poses.bin` holds positions relative to the hips, and Path B needs Unity's 15 required human bones. The game derives the map from Mixamo names, with or without a `prefix:`, and a map file wins when present. Tripo's and Mixamo's auto-riggers write these names (step 0.1b, tested on a model swap only) |
| Weapon marker nodes | The bake reads the weapon tip and hilt from them |
| Path B only: a T-pose rest pose | The A-pose `wibou.glb` held its arms about 43 degrees low (step 0.1). Fingers are optional: a mitten hand with a thumb and one finger chain worked (step 0.1b) |
| A base-colour texture within the texture budget | A 4096 px texture loaded uncompressed at about 64 MB and cost one frame of about 250 ms (step 0.1b). The game downscales and compresses at load, or the importer's limit caps the size. Neither is built or tested |

### What the game works out from the file

A creator does not set these. The game reads them from the GLB alone. Step 0.1b needed all four
for a model swap, as spike code with no number tuned for the model. Phase 1 builds them.

- **Bone map.** Matched from Mixamo bone names, as in the table above.
- **Upright, position, and ground.** Up runs from hips to head and right from the left thigh to the
  right thigh, both read from the rest skeleton. The game turns that frame onto its own axes, puts
  the hips over the origin and the lowest skinned vertex on the floor. This covers Y-up, Z-up, and
  a rest pose stored lying down, which is how Tripo writes it. Loaded as-is, that model lies on
  the floor.
- **Size.** A model swap is scaled so its avatar's hips height matches the base fighter's.
- **Palms.** Unity's Humanoid expects palms down in the rest pose. The game measures each thumb's
  direction around the forearm and rolls the avatar's reference pose to match. Without this rule
  the hands sat 72 to 86 degrees off (median). With it they sat 8 to 9 degrees off.

Alpha and specular material settings are ignored, and a fighter draws opaque with the game's
shader. Anything that is not a human bone moves with the bone it is weighted to. A tail, hat, or
cape that should move on its own needs its own bones, and built-in clips do not animate them.

### Weapon rule

The weapon keeps its own scale at the bone's position and rotation, as in `WeaponAttach.cs:92` and
`DeterministicPoseTrackBaker.cs:204`. Workshop bakes and renderers copy that rule. The web export
hangs the weapon under the scaled hand bone instead, which put the blade tip 139 mm off on Wibou.
The rule was read from code, not captured in game.

### Package ID rule

A published Workshop package's ID is `workshop.<PublishedFileId>`. It passes
`IsStablePackageId`, cannot collide with built-in IDs such as `fightguy`, and cannot be claimed by
another creator, because Steam assigns the number. `CreateItem` returns it before content uploads
(`steam-roundtrip/upload.log`), so the cook can stamp it. An unpublished package uses a `local.`
prefix and never plays online. Each update keeps the ID and gets a new version and hash, so a Room
that pinned the old hash fails closed (ADR-0026).

### Cooked schema changes

ADR-0025 and ADR-0028 hold. The loader accepts a window of runtime API minimums, 23 today. An item
outside it fails before load with a "needs update" message, and nothing migrates silently. The
creator recooks from the item's source and publishes a new version. A model swap has no cooked
files, so only the `item.json` schema version gates it.

### Trust at load

Shared's loader now selects a host trust profile, rejects internal capabilities under
Workshop, recomputes compiler budgets, and rejects malformed timelines, enums, and slot grids.
It bounds file reads, JSON depth, pose memory, operation counts, projectile speed, and lifetime.
Cook and load use the same limits and diagnostics. Body hurtboxes are capped at 32. Shared's
resolver also enforces deterministic owner/world volume and sweep-weighted pools for Workshop owners, including
after rollback restoration. Trusted built-in spawns count toward world totals but are never refused.
Excess Workshop emissions are dropped; the four-seat all-Workshop fighter collision
budget is 79,200 contact tests per tick. The contract suite covers peak and sustained work,
capability emitters and form changes, with all eight formerly skipped cases enabled.
The web demo trusts only its embedded roster's exact pins. Editor trusted cooks require an
application-owned first-party source folder as well as shipped roster membership.
See [cooked package admission](../systems/cooked-package-admission.md) for constants,
measurements, diagnostics, and typed capability lifetime accounting.
External admission remains internal or test-only until the subsequent roll-out units.

The GLB importer refuses files over its size, triangle, bone, and texture limits, naming the limit.

### Online play and distribution

Steam distributes items. Workshop content plays online only in Rooms whose leader turned Workshop
on (decision D6).

- A model swap is client-only. Each client downloads it, checks the hash, and shows the built-in
  model until it is ready or if it fails. A "Show Workshop models" setting offers everyone,
  friends, and nobody.
- For a fighter, the GameServer downloads the item itself with `SteamGameServerUGC.DownloadItem`
  and never takes bytes from a client. Clients download and check before they report ready. Any
  failure blocks the Match and names the item.

### Open questions

1. **Can an anonymous game server download items before the Workshop is public?** The owner's
   account round-tripped item 3837402870 with equal SHA-256. The anonymous server, run on the
   owner's workstation and not the VPS, got `k_EResultFail`, as a made-up ID does.
   The owner turned the anonymous-download setting on and published it. Retries on 2026-11-11
   failed the same way with the item private, unlisted, and public, so the item's visibility is
   not the cause. The developers-only Workshop on an unreleased app remains. Options are (a)
   retest when Phase 1 opens the Workshop, or (b) log the GameServer in with a game-server login
   token. Recommendation: (a), then (b) if it still fails. Model swaps do not need this, and the
   answer does not change the format.
2. **Why did world snapshots differ in step 0.2?** They differed on 2,598 of 18,000 ticks, with
   equal hits, damage, stocks, positions, and velocities. Options are (a) find the fields before
   Phase 2 relies on the GLB bake, or (b) accept it, since peers load the same bytes.
   Recommendation: (a), without blocking Phase 1.
3. **What are the memory limits?** Step 0.4 measured tick time only. Options are (a) measure the
   seven built-in fighters on the Steam Deck and set limits from that, or (b) cap file size only.
   Recommendation: (a).
4. **Where does shared motion come from?** Joint positions baked from it ship inside published
   `poses.bin` files, so it must be owner-made or CC0. Options are (a) an owner-made set, (b) a CC0
   set, or (c) none at first, with every clip in the fighter's GLB. Recommendation: (c), then (a)
   or (b).

## Considered options

- **One playback path.** Path B cannot play a creator's own clips. Path A makes swap creators
  animate every clip.
- **Creator-chosen package IDs.** They collide and allow impersonation.

## Consequences

- Workshop fighters online need a Master and Shared change in one release, because both hold the
  protocol number.
- For a model swap, Blender's default GLB export of a Tripo character met the requirements with no
  extra file (step 0.1b). For a fighter, no exporter writes all of them yet. Phase 0 supplied frame
  counts beside the web demo's Unity export.

## Non-goals

This ADR does not design the loader code, the limit values, the binding fields, or the stage format.
