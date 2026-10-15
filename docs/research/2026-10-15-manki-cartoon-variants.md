# Manki aerial: cartoon deformation variants

Prepared by Codex, 15 October 2026. Two authored variants build on the accepted leg-corrected GenRA aerial. No additional GenRA generation job was used. This is a local animation experiment; approved assets, package bindings, gameplay values and cooked packages remain unchanged.

The reference principles and bone/hitbox investigation are in the [cartoon deformation study](2026-10-15-cartoon-deformation-study.md).

## Candidates

Both clips live in `client/Unity/Assets/Art/Experiments/GenRAMankiCartoon20261015/` and require that folder's **`MankiCartoonExperimentRig.prefab`**.

- **`BigFistsDoubleFistAerial.anim`:** all 570 original curves are unchanged. Fists grow from 1× after tick 16 to 2× at tick 23, stay at 2× through tick 30, and return to 1× by tick 42.
- **`SnapOvershootDoubleFistAerial.anim`:** the upper body holds its tick-18 loaded pose through tick 21, passes through source pose 20 at tick 22, then reaches the original contact pose at tick 23. The fists briefly peak at 2.2×, settle to 2× by tick 26 and return to normal by 42. A small shoulder/arm overshoot at ticks 24–25 follows the existing swing direction. The remaining recovery is preserved.

Both retain **duration 62 ticks, contact pose 23, active interval 23–30, IASA/handoff 57 and landing lag 12**. Playback is 60 fps at 1×, with 62 baked frames and no actor-root travel. Original-skeleton entry, contact and handoff/end positions match the baseline exactly in the recorded evaluation.

## Conversion findings

Direct scale curves on the mapped Humanoid hand bones did not affect their evaluated scale: the curves existed, but actual Animancer playback kept those hands at 1×. The unsuccessful clips are retained in the experiment artifacts. Unity's [animation import documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AnimationClip.html) documents discarded scale data among Humanoid import limitations.

The separate rig copy adds two unmapped fist-deformation controls and copies the finger branches beneath them: 34 additional transforms in total. Skinned-renderer bone references use these controls/copies. The original 66 bone paths, Avatar, mesh assets, skin weights and bind poses remain intact. The clips copy 320 finger curves to the new paths and animate six control-scale channels, for 896 curves per clip. No runtime deformation script is needed.

At scale 1×, the copied rig's skin-bone matrices match the original exactly across all 63 sampled poses. A plain approved Manki rig lacks these extra controls, so playing either candidate on it will not show the intended enlargement. Future integration must account for this explicit rig dependency.

Upper-body retiming also changes Unity Humanoid's reconstructed body center and pelvis orientation. Preserving only the original root curves shifted the pelvis; fixing position alone still rotated the legs. The sharper clip therefore includes measured Humanoid body-position/orientation corrections on the six affected ticks. Actor-root movement stays zero. Final original leg-bone position differences from the accepted baseline are below 0.0002 mm; the scale-only variant's difference is zero.

## Validation and visual findings

- Both original Avatars remain valid Humanoid Avatars. The scale-only clip preserves every original curve; the sharper clip preserves 516 of 570, changing 47 upper-body channels and seven Humanoid body-position/orientation channels.
- Twenty actual Animancer interruption checks cover ticks 12/22/23/30/35 into idle and fall, with 12-tick fades. Normal fist size and the original target skin pose return within floating-point tolerance.
- All 35 protected source files match their pre-experiment hashes. The final Unity error-console query was empty and the gateway released a settled lease.
- The enlarged silhouette is clearer from the side. The existing rear gameplay-like angle still conceals part of the strike behind the torso/legs. The sharper variant concentrates its fastest downward wrist movement into tick 23; the baseline's peak is at tick 22. This is presentation spacing, not a gameplay timing change.

The synchronized comparison, six normal-speed MP4s, two contact sheets, authoring scripts, path map and validation receipts are under `artifacts/genra-manki-cartoon-20261015/`. The comparison includes the accepted baseline and both variants, in side and gameplay-like views. Videos contain 62 frames at 60 fps, lasting about 1.033 seconds; frame 62 is retained separately for endpoint inspection.

These are standalone actual-prefab Animancer previews. They do not simulate jump/fall trajectories or prove Shared hit/contact behavior. No new hitbox radius, pose payload or gameplay binding was authored. Human acceptance of deformation strength and attack feel remains the purpose of reviewing these candidates.

The owner's first review found the two variants hard to distinguish. The original comparison defaulted to their shared contact pose; the sharper version's extra fist scale there is only 10%. A separate direct comparison now starts at tick 21 and offers quarter-speed playback and frame stepping. This exposes the timing change without claiming it establishes a strong style difference at normal speed. The clips were not revised in response to this feedback; a more pronounced cartoon pass would need additional wind-up/follow-through pose contrast.
