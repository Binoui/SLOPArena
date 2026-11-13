# ADR-0065: What a Workshop item holds

**Status:** Accepted 2026-11-11. The owner agreed ("i agree with it") after step 0.1b. Its three
corrections to the GLB requirements are folded in below.
**Deciders:** @Binoui
**Related:** [ADR-0025](0025-workshop-package-architecture.md),
[ADR-0026](0026-workshop-multiplayer-synchronization.md),
[ADR-0064](0064-workshop-creator-route.md),
[Workshop roadmap](../plans/2026-11-10-workshop-roadmap.md)

The fighter item format remains open to change until the first fighter is published in unit 5.
No fighter has been published. These revisions do not migrate existing published content.

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
- **`fighter`** uses item schema 2 and holds the four cooked files, one GLB, editable
  `package.json` and `character.json`, and `card.json`. Portrait, `humanoid.json`, and
  move icon PNG/JPEG images are optional. All files live directly in the item folder.
  Every payload has a SHA-256 entry. Extra files, symbolic links, reparse points, and special
  files are refused. Hard links are allowed because the opened bytes are hashed. A nonblocking
  regular-file handle check precedes size inspection and reading on Linux; Windows requires a
  regular disk-file handle without a reparse point. It has no base-fighter field.
  Model swaps retain schema 1 and its existing contract.
- **`stage`** comes later.

Card descriptions, work-in-progress flags, and Tutorial hints live only in `card.json`, using
ADR-0067's existing card contract. `item.json` refuses these fields. The cooked definition owns
the display name. Keeping one card format avoids divergent metadata for bundled and made fighters.
When no portrait is supplied, the client renders one from the model once per session (unit 4c):
idle frame 0 posed by the Shared sampler, framed on the head and upper chest from the front.

### Portable bindings

`client.bindings` schema 3 has matching catalog and binding versions, package/source identity,
SKEL pose version 1, 60 Hz, and `rootRuleVersion` 1. Its `payloads` map pins every non-cooked
file, including source and card bytes. `modelFile` names the model. `portraits` is an array of
nullable image filenames, one per runtime form. `icons` is an array of maps, one per form.
Each map has exactly the distinct nonempty icon IDs used by that form's cooked slots, with
an image filename or explicit null. Valid IDs are `icon.1`, `icon.2`, `icon.3`, `icon.a`,
`icon.e`, `icon.r`, and `icon.f`. Unknown or missing keys and wrong form counts fail.
All image filenames resolve through `payloads`. There are no Unity Editor object IDs.

Each animation has `semanticId`, `poseTrackId`, `frameCount`, and a `source` object. A source is
`{"kind":"glb","clipName":"..."}` or
`{"kind":"shared","id":"slop.anim.idle.default.v1","version":"1.0.0"}`.
Different semantic IDs may bind the same uniquely named GLB clip. Clip names need not equal
semantic IDs. Required shared clips fail while the shared-clip registry is empty.
The GLB JSON is parsed once per binding validation into a name index. At most 256 animations
are allowed; larger binding arrays fail before clip indexing.

`presentations` maps every runtime-declared presentation ID to either `{"kind":"none"}` or
`{"kind":"engine","vfx":null,"audio":null,"projectile":null}` with at least one non-null role.
VFX and projectile roles contain an exact engine reference. Audio is null or an object with
`selection` equal to `fixed` or `cycle` and a `clips` array of references. Fixed pools have one
clip; cycle pools have one to sixteen. VFX and projectile IDs use `slop.vfx.*`; audio uses
`slop.sfx.*`. Unknown, duplicate, or unbound runtime presentation IDs fail. This applies to
non-hit presentation IDs as well as `hitPresentationId`. All required engine presentations
fail while the presentation registry is empty. Explicit none remains valid.

An engine reference has exactly `id` and `version`. IDs are ASCII lowercase dot-separated
segments, beginning `slop.anim.`, `slop.vfx.`, or `slop.sfx.` and ending `.vN` for positive N.
Each intervening segment starts with a letter and contains letters, digits, or hyphens.
`version` is an exact numeric `MAJOR.MINOR.PATCH`, without leading zeros or ranges; MAJOR must
match N. A registry resolves that exact ID and version, never a fallback or nearest version.
IDs have at most 256 characters; each numeric version component has at most six digits.

`sourceHash` is SHA-256 of two framed files, in order `package.json`, `character.json`.
Each frame contains the UTF-8 filename byte length as little-endian int32, the filename bytes,
the content byte length as little-endian int32, and the exact content bytes. Both the bindings
and cooked manifest carry this hash. Admission recomputes it. The package hash also covers
binding bytes and therefore every external file digest. Built-in binding schema 2 and its
writer stay unchanged.

### Portable compilation and artifact authority

The cooked runtime is authoritative. `item.json` records `compilerVersion`, an exact numeric
SemVer, and `canonicalWriterVersion`, a positive integer. They describe the producer and are
not a requirement that the installed compiler reproduce those bytes. An admitted runtime
inside the loader's schema/API window remains supported across compiler updates. Outside
that window, admission reports `item.needs-update`, including both recorded producer versions.

Included source is editable material for forking and recooking. Admission hashes it but does
not parse it through the current source schema or recompile it. The recompile comparison was
dropped because it adds no authority beyond the validated cooked bytes and could make compiler
changes reject otherwise supported content. Optional field and validation changes cannot make
the current compiler a second admission authority.

`CharacterPackageSourceCodec.LoadPortable` rounds decimal rationals directly to binary32,
nearest with ties to even, using integer arithmetic. It avoids Mono's decimal-to-double-to-float
double rounding. Decimal tokens have at most 512 characters. Overflow fails with a diagnostic;
underflow rounds to zero. Negative zero becomes positive zero in the definition and output bytes.
`CompilePortable` uses this parser and exact finite IEEE-754 decimal expansions for runtime
JSON. Integer fields retain their values. Legacy parsers and built-in writers stay unchanged.

### Motion sampling and the pose bake (root rule 1)

Unit 4c made this rule concrete in `src/Shared/GlbMotion.cs` and `src/Shared/PortablePoseBake.cs`.
`GlbMotionRig.SampleLocal` is the one sampler. The admission bake and the renderer both read it.

**Clips.** A bound clip records `extras.slop.durationSecondsBits`, a finite positive binary32 as
its int32 bits, and `extras.slop.frameCount`. `extras.slop.loop` is an optional boolean for the
renderer. Unknown `extras.slop` fields fail. The frame count must equal
`max(1, ceil(binary32(durationSeconds * 60)))`, at most 4096, and the binding and pose-track
counts must agree with it. clipStart is the minimum first-key time among the clip's samplers.
A key belongs to tick `j = round((t - clipStart) * 60)`, computed in double, and must sit within
1/1000 tick of it. Binary32 times up to 4096 frames err below 1/10000 tick, so this tolerance
names the grid without accepting another frame rate. A channel's keys must be consecutive ticks.
A gap is allowed only between bitwise-equal keys under LINEAR, as written by exporters that drop
duplicate keys, or after any key under STEP. CUBICSPLINE is refused. Inputs and outputs are
32-bit float accessors. A key after the recorded duration fails. A key exactly at tick
`frameCount` is the duplicate terminal endpoint of a loop and is never sampled. Channels clamp
outside their key range. Tick `k` therefore always reads a stored key, and no interpolation enters
the bake. Channels may animate translation, rotation or scale of drawn nodes. Weights and nodes
with a `matrix` cannot be animated.

**Placement.** Every sample starts from the rest transforms. glTF X is reflected into game
coordinates, as glTFast does, and the node hierarchy is applied. The rest skeleton is then turned
upright like a model swap: up runs from the hips to the head, right from the left thigh to the
right thigh, and forward is right × up. The rest hips sit on the model origin, FightGuy's
convention, and the authored `visualScale` applies last. The character's `modelYOffset` places
that origin above or below the capsule centre. The rest hips are not lowered to the floor. A
creator sets `modelYOffset` so that the feet meet the capsule bottom. The turn and the origin come
from the rest skeleton, use only `+`, `-`, `*`, `/` and `sqrt`, and are the same numbers the
renderer puts on its upright node.

**Points.** `poses.bin` keeps the Editor baker's layout. First come the required humanoid points,
then hurtbox bones and end bones, deduplicated by baked name in that order. The definition's
attachment points come next, then `_hips_offset`. Tracks are ordered by pose track ID. Humanoid
points resolve through the bone map, a `humanoid.json` or Mixamo names. A `rig:` ID names exactly
one node. An attachment point such as `_weapon_tip` is the drawn position of the one node with the
same name, so a weapon modelled in the GLB carries marker nodes named after its attachment points.
The built-in weapon prefab rule below exists for a prop attached to a scaled bone and does not
apply to geometry already in the GLB. Every point is its model-space position minus the hips.
`_hips_offset` is the hips in model space plus `modelYOffset` on the vertical axis. Root
translation never moves the authoritative fighter.

**Verification.** Admission parses the GLB once, re-bakes every bound clip and requires the result
to equal the included `poses.bin` byte for byte. A difference fails with
`item.pose-bake.mismatch` and names the first track, frame and point, and the distance in
millimetres. Exact equality was chosen over a tolerance. Every peer runs the simulation from the
same included bytes, so the check only has to prove that those bytes come from the drawn motion.
Exact equality proves that without a tolerance to argue about or hand-tuned poses hiding under it.
The bake makes this possible across runtimes. It computes in double with only `+`, `-`, `*`, `/`
and `sqrt` and rounds to binary32 once per output value. Node rest values from the GLB JSON
round once through the portable decimal parser, and keys come bit-exactly from the BIN chunk.
Release Mono players run float arithmetic in double precision while CoreCLR rounds each float
operation, so float arithmetic would split them. Double arithmetic without transcendental calls
does not. In the Win64 player (Mono 6.13), Tumbleweed's admission re-baked its poses to the .NET 8
bytes and its re-cook reproduced every item file byte for byte.
The work is bounded by the existing limits: at most 300 nodes, 256 animations, 4096 frames per
track and the pose-memory ceiling. Only ancestors of baked points are transformed, and a clip
bound by several animations is sampled once. Tumbleweed's 17 tracks bake in well under a second
in a cook.

`rootRuleVersion` 1 names this rule. A changed sampler, placement or point rule receives a new
version and keeps rule 1 for every published fighter in the supported window. The Shared suite
pins the SHA-256 of a fixture's rule-1 bake.

**Shared clips.** `src/Shared/SharedClips/registry.json` lists exact `slop.anim.*` IDs and
versions, each with a library GLB beside it, its SHA-256 and a clip name. The table and files
build into Shared, so the client and the GameServer resolve the same bytes. A shared clip's
channels are applied to the fighter's nodes of the same names, then sampled and baked like a GLB
clip. This retarget rule is provisional until the owner's clip inventory defines the library.
The table is empty, so every shared reference fails with `portable.shared.unsupported`.

### Item resource and filesystem limits

Fighters keep plain ASCII filenames with case aliases refused. Extensions are lowercase
`.glb`, `.png`, and `.jpg` everywhere; model-swap schema 1 retains its existing case behavior.
There are at most 40 payloads, and the exact serialized Steam hash map must be below 5,000 bytes
before a fighter manifest is accepted. Therefore every accepted manifest can write its metadata.
The complete item, including item.json, is capped at 64 MiB before allocation. Per-file caps are
30,000,000 bytes for GLB, 4 MiB for an image, 2 MiB for each source/card JSON, 65,536 bytes for
humanoid.json, and the existing cooked-file limits. The humanoid cap matches the model-swap
library. Regular hard-linked files are valid; symbolic links, FIFOs and devices are not.
Portable external and embedded JPEGs require exactly one frame header and one complete image;
multiple frame headers and concatenated images fail. Model-swap image checks remain unchanged.

### Presentation payload

Each item has one GLB, loaded with glTFast.

- **Path A, for fighters,** plays the GLB's own clips. Unity creates only legacy clips at runtime,
  and those cannot play in Animancer's graph. Each bound clip therefore becomes an Animancer
  job state that writes the Shared sampler's per-tick local transforms into the animation stream
  and blends between neighbouring ticks for display. Animancer's mixer fades between GLB states as
  it does between clips. The model uses a generic Animator without an avatar. Step 0.1 measured
  13.4 µs per fighter per frame for writing GLB joints, against 62.5 µs built-in (median).
- **Path B, for model swaps,** builds a Humanoid avatar at runtime and plays the base fighter's
  built-in clips. It worked for `bonk.glb` and `bashie.glb`, and for a raw Tripo character that
  never went through Unity (step 0.1b, `tmp/workshop-spikes/tripo-swap/`).

The GLB must carry the following.

| Requirement | Why |
| --- | --- |
| Uniquely named clips referenced by schema-3 bindings | Several semantic IDs may share a clip; names need not match IDs |
| Each clip's explicit original duration and frame count in `extras.slop` | From the GLB alone, 7 of 22 FightGuy clips were one frame short, and a bot Match's hits fell from 61 to 7 (step 0.2) |
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
creator recooks from the item's source and publishes a new version. The current compiler never
recompiles an older supported runtime as an admission condition. Recorded producer versions
appear in the needs-update diagnostic. A model swap has no cooked
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

This ADR does not design the renderer, measured platform memory limits, or the stage format.
