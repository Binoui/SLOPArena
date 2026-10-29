# ADR-0055. Mesh-fitted bone capsules

Date. 2026-10-29.
Status. Accepted by the owner. Visual acceptance remains a separate check.

## Context

Seven shared bone spheres leave gaps between joints and ignore each mesh's proportions.
The original area-weighted mesh evaluation sampled every fourth frame of every bound clip.
Coverage was Bonk 22%, Salamander 39%, Bashie 46%, Manki 54%, FightGuy 60%, Nyx 64%, and Wibou 77%.
These figures include cosmetic cloth and Salamander's hat. They measure surface coverage, not hit probability.

ADR-0015 deliberately shrank bone positions with `HurtboxBoneScale`, normally 0.85.
That field also positions body-bone attack hitboxes. Changing it would change attack reach.

## Options

| Option | Benefit | Cost |
| --- | --- | --- |
| Retune the spheres | Small data change | Large spheres cover joints but also fill empty space between limbs. |
| Fit capsules between bones | Follows each animated limb with existing Shared collision geometry | Requires more baked pose points and mesh fitting. |
| Derive convex hulls from the mesh | Closer silhouette | Requires new deterministic collision geometry, more data, and more runtime work. |

## Decision

Use mesh-fitted capsules for the head, torso, upper arms, forearms, hands, thighs, shins, feet, and tails.
Fit radii from the 85th percentile of distances of vertices dominated by each segment's bones in idle frame zero.
Use humanoid mappings rather than rig-name assumptions. Custom tail points use `rig:<exact transform name>` IDs.
Manki's weighted arm deform bones participate in fitting even though their names differ from the humanoid controls.

Cloth has no dedicated hurtbox. Exclude Nyx's skirt, Bonk's cape, and Wibou's coat from fitting.
Salamander's hat is cosmetic. Tails are hittable, including Manki's existing tail chain.
Bashie's shell is body geometry and receives its own capsule if the torso fit leaves it outside.

The head capsule uses Head at both ends with an upward end offset.
Head-top rig markers are not reliable mesh boundaries on these stylized rigs.

## Data and ownership

A `hurtboxBoneDefs` entry retains `boneId`, `offsetX/Y/Z`, and `radius`.
Optional `endBoneId` selects a capsule. Optional `endOffsetX/Y/Z` default to zero.
Without an end bone, the entry remains a sphere. Offsets use facing-relative simulation metres.
Both endpoints receive attack-pose pitch after placement.

`CharacterBoneRegistry` owns semantic IDs, humanoid names, stable baked point names, and the original required points.
Unity resolves the humanoid mapping and bakes the original points plus referenced hurtbox endpoints.
Stable baked names preserve attack bindings even when the rig uses custom transform names.
The baker restores the rig's original local transforms before each clip so unkeyed tail and shell bones cannot inherit a previous clip's pose.
Package validation rejects any referenced hurtbox point absent from `poses.bin`.

Hurtbox endpoints resolve at scale 1 on the drawn pose. Fitted radii replace the old positional shrink.
Body-bone attack hitboxes continue using `HurtboxBoneScale`. Weapon attack points continue using scale 1.
The root-relative `hurtboxCapsules` fallback remains available.

## Consequences

The Shared simulation, Training, and Ability Lab consume the same capsule entities.
JUMP! CPUs sustain the jump press through JumpSquat. A released-squat short hop no longer reliably clears full body and tail capsules.
Cooked payloads, source hashes, and Arcade record fingerprints change with the new geometry.
The protocol number remains unchanged here. The unreleased simulation note records the behavior for the next release bump.

Capsules approximate the body rather than reproduce the mesh. A percentile fit leaves some surface outside.
Cloth that overlaps a body capsule can occupy the same space, but cloth never adds a vulnerable volume.
A head end offset rotates with facing and attack-pose pitch, not the head joint's own rotation.
Check bent and tilted poses visually before accepting a fit.

Run the rerunnable [hurtbox tools](../../tools/character_sizes/README.md) after a model swap or a change to visual scale.
Inspect idle, an attack mid-swing, and hit-hard from front and side.
