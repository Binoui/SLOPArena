# Hurtbox and model alignment

The roster uses bone-to-bone capsules fitted to its rendered meshes.
[ADR-0055](adr/0055-capsule-hurtboxes.md) records the decision and alternatives.
The [hitbox system](systems/hitbox-system.md#hurtboxes) describes runtime placement.

## Why the seven spheres missed visible hits

Every package previously used near-identical spheres at the head, spine, hips, hands, and feet.
Those volumes left the upper arms, forearms, thighs, and shins mostly uncovered.
Bonk's original surface coverage was 22%, while Wibou's was 77%, despite their shared sphere layout.
The original whole-mesh figures were Bashie 46%, FightGuy 60%, Manki 54%, Nyx 64%, and Salamander 39%.

The coverage tool samples every fourth frame of every bound clip.
Each vertex receives one third of each adjacent triangle's area and a region from its dominant skin bone.
Coverage is the share of sampled surface area inside the union of Shared hurtboxes.
It is not a hit-rate benchmark and does not establish player acceptance.
Whole-mesh coverage includes cloth and hats. Body coverage excludes those cosmetic regions.

## How the capsules follow the model

`CharacterBoneRegistry` maps package IDs to humanoid mappings and stable baked point names.
The baker samples the humanoid transforms through `Animator.GetBoneTransform`.
This also handles Manki's custom arm mapping. `rig:<exact transform name>` covers tail and shell points.
Missing hurtbox endpoints fail package validation instead of disappearing during simulation.

Baked points are hips-relative. `_hips_offset` stores the drawn hips relative to the capsule centre.
Shared places hurtbox endpoints at those drawn positions with scale 1, rotates their offsets with facing,
and applies attack-pose pitch. Radii come from the mesh rather than the old 0.85 positional shrink.
Body-bone attack hitboxes still use `HurtboxBoneScale`, so this fit does not change attack reach.

The head, torso, upper arms, forearms, hands, thighs, shins, and feet receive capsules.
Salamander and Manki have tail chains. Bashie's shell is hittable body geometry.
Nyx's skirt, Bonk's cape, Wibou's coat, and Salamander's hat do not enlarge the vulnerable body.
A cosmetic surface can overlap an existing body volume without adding one of its own.

## Earlier alignment fixes

Before `_hips_offset`, a fixed `HipHeight` pivot could leave hurtboxes behind an animation's hip motion.
Current package bakes carry the drawn pivot. `HipHeight` and `BoneYToWorldY` remain relevant to
attack-pose pitch, aim correction, and fallback bakes without `_hips_offset`.
`presentation.modelYOffset` places the model root relative to the capsule centre.
Do not derive it from a generic half-height formula after a model swap.

## Reproduce the fit and check it

Use the [character size tools](../tools/character_sizes/README.md#fit-and-check-hurtboxes).
The fitter changes only `hurtboxBoneDefs`, re-reads source before writing, and imports/cooks that package.
The evaluation scores the actual cooked capsules against the original sphere baseline.
It reports total and per-region coverage, excluded cosmetic regions, and bone-placement error.

Inspect front and side renders in idle, an attack mid-swing, and hit-hard.
The visual mode uses Shared's cooked entities for the wire capsules.
A percentile fit deliberately leaves some mesh surface outside; inspect those gaps rather than chase 100% coverage.
Run the cooker and Rendered Pose Alignment self-tests after cooking.
