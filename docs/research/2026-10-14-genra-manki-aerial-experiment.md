# Manki original aerial GenRA experiment — 2026-10-14

The experiment produced a new airborne double-fist downward strike using Manki's actual rig, its jump/fall references and the supplied concept image. No existing attack was supplied as a motion reference. One focused refinement made the strike clearer by extending both arms down and tucking the legs. The refined candidate is a useful starting point for manual polish; it remains separate and unbound.

The preferred Unity clip is `Assets/Art/Experiments/GenRAMankiAerial20261014/RuntimeAlignedRefinedDoubleFistAerial.anim`. The first-pass comparison is `RuntimeAlignedGeneratedDoubleFistAerial.anim` in the same directory. Both preserve the accepted clock, play at 1× on the actual Manki prefab and have zero root travel. Neither replaces an approved animation or creates a new gameplay ability.

## Scope, sources and provider

The experiment lives in `artifacts/genra-manki-aerial-20261014/`, outside Unity Assets. Only separate conversion controls and candidate clips/import metadata were imported. Seven protected sources and their backups still match their original SHA-256 hashes, including gameplay JSON, catalog, current prefab, original FBX/importer and airborne references. No catalog cutover, publishing cook, runtime-code change, dependency installation, commit or push was performed.

The current catalog rig is `Assets/CharacterPackages/manki/Presentation/MankiVisualRig.prefab`, using the ShoulderRepair199 `Manki.fbx` and its existing valid `MankiAvatar`. It has 66 bones and ten meshes; the original FBX has no embedded animation actions. References were therefore sampled from the actually bound `anim.manki.jump` and `anim.manki.fall` clips on this prefab. The real `air.4` source supplied only the clock.

The maintained animation-authoring and Ability Lab workflows, Unity gateway/coordination rules, renderer, baker and existing import/export tooling were reused. GenRA remains pinned to [revision 5f4ffb8](https://github.com/MacPaw/GenRA/tree/5f4ffb8ac846f91265c48e8bb65d702dca11b7a1), inspected in the [earlier experiment](2026-10-14-genra-experiment.md). This run reused its isolated Python 3.12.13 environment, Blender 5.2.2 and FFmpeg.

The approved adapter used the existing ChatGPT-authenticated Codex CLI with `gpt-6.1-sol`, medium effort. Generation took 354.01 seconds; refinement took 210.76 seconds. Each job permitted one final animation response, no transport retries and a 600-second bound. There were exactly two animation passes; CLI jobs contain multiple internal model/calculation turns, so this is not a claim of two underlying inference requests. GenRA's stock `gpt-5.4` provider was not used for this comparison.

Both USD animation references and reference images were supplied explicitly; `auto_select_examples=false`. Joint-name cleanup and request text were deterministic, avoiding auxiliary model calls. GenRA's parser, quaternion interpolation and USD writer remained in use. Requests, effective model configuration, complete prompts, raw responses, sparse data, refinement patch, merged input, CLI usage and outputs are retained per pass. Saved CLI authentication was reused without opening/copying credential files or serializing the environment.

## Real clock and requested motion

`timing-contract.json` preserves the complete real Manki `air.4` source move. Presentation beats were fitted to these values:

| Event | Tick(s) | Requirement |
|---|---:|---|
| Entry | 0 | Existing falling pose |
| Anticipation | 1–12 | Tuck legs; lift both closed fists above/beside the head |
| Load | 13–18 | Hold a distinct airborne silhouette |
| Strike acceleration | 19–23 | Drive both fists downward together; fold torso forward |
| First impact | 23 | Simultaneous downward striking pose |
| Active interval / follow-through | 23–30 | Controlled extension and deceleration; exclusive end 31 |
| Recovery | 31–56 | Recoil and unfold toward entry |
| IASA / handoff | 57 | Entry pose restored; hold through endpoint |
| Duration | 62 | 62/60 seconds, about 1.033 seconds |
| Landing lag | 12 | Existing renderer's landing-to-idle blend |
| Auto-cancel | Before 6 / after 44 | Source values retained |

This is an already-airborne attack. Jump/fall physics continues to own the trajectory; no jump impulse, dive velocity, root travel or added gameplay displacement was authored. Every USD joint translation and scale is frozen at its entry value. A literal clasp was optional: two close, closed fists satisfy the concept without forcing intersecting hands.

## Conversion proof before generation

The existing fall clip completed Unity → actual FBX rig → USD → Blender → ordinary FBX → Unity before the first model call. The hierarchy, rest/bind matrices, bone names, weights, scale, axes, duration and animated transforms were checked.

USD sanitizes `:` to `_`; explicit reverse maps preserve all 66 original bone names, parents and exact Unity paths. A separate map restores ten mesh names changed by USD import. Four rigid bone attachments were converted to equivalent one-bone skinning in the experiment copy; three-pose vertex comparison differed by at most 0.628 micrometres. Approved source meshes were untouched.

Both original references are 30 fps: jump is 0.466666669 seconds and fall is 0.700000048 seconds. Original-rate artifacts are retained in `roundtrip-30hz/`. Sampling only at 30 fps missed the jump's between-frame cubic motion, producing a roughly 19.9 mm skin discrepancy. Conversion/reference copies were sampled at 60 Hz while retaining the original durations and leaving the approved 30 fps assets unchanged.

The required **fall** round trip passed the strict 1 mm check: maximum Unity skin error 0.480 mm, animated-bone position error 0.477 mm, bind-position error below one micrometre, unchanged bind axes and a valid original Avatar. Blender's USD skin comparison was below 11 micrometres. Unity uses Y-up; USD uses Z-up/metres, with the measured reflected coordinate mapping retained in `coordinate-mapping.json`.

The auxiliary **jump** round trip remained slightly outside the strict threshold: 1.074 mm skin error, 1.078 mm bone error and 0.392° maximum bone-rotation error. This is recorded as a discrepancy. `unity-roundtrip-validation.json` consequently has aggregate `passed=false`; `conversion-gate.json` explicitly requires the passed fall reference. The jump warning was not concealed or reclassified as a pass.

## Conversion issues found in candidate playback

These were distinct from the animation's artistic weaknesses:

1. **Unmapped fingers/tail.** The current Avatar maps 22 Humanoid bones. A plain Humanoid FBX discarded motion for the other 44 bones, including fingers and tail. The native candidates combine imported Humanoid body curves with explicit original-path FK curves for those bones: 570 curves total. A full Generic FK clip was rejected because a Humanoid Animator resets mapped body bones; its failed output is retained.
2. **Float duration changed runtime speed.** Raw FBX length rounded above 62/60, making the current baker count 63 and yielding 63/62 speed. That would move source contact 23 to gameplay tick 22.635. Native conversion canonicalized only FBX sampling timestamps to their original 60 Hz ticks, with a maximum correction of 119 ns. Final length is 1.0333333 seconds, baked count 62, speed 1×. Gameplay values and sparse model timestamps were unchanged.
3. **Root extraction differed from clip sampling.** The initial hybrid matched `AnimationClip.SampleAnimation`, but did not match Animancer with the renderer's `applyRootMotion=false`. Root rotation/position were therefore baked into the candidate's pose through separate FBX importer copies. Sampling alone was insufficient evidence; actual Animancer evaluation caught this failure.
4. **Current fall's runtime origin differed from its sampled origin.** The final transition test found a 0.612 m constant position mismatch with the existing playing fall. A diagnostic established matching rotations and a constant translation from root extraction. New `RuntimeAligned...` copies offset only the three Humanoid `RootT` body-position curves. The other 567 curves, every timestamp/tangent, duration and model data remain unchanged. This calibrates static presentation origin to the existing fall and adds no time-dependent displacement or gameplay movement. Earlier candidates and evidence are retained.

Final aligned candidates match the currently playing fall at entry and tick 57 within 0.219 mm. Actual Animancer playback matches their prefab samples within 0.001 mm across all 63 sampled poses. Root travel and root-scale change are zero; measured hip travel is below 0.0002 mm. After accounting for the recorded constant origin calibration, residual USD → Unity bone-position error is 1.934 mm for generation and 1.778 mm for refinement, worst on toe-end helpers. These Humanoid conversion residuals remain reported.

## Refinement and animation quality

The first pass established the coordinated double strike, but hanging feet extended below the fists and the rear view hid much of the attack. The single refinement targeted this observed silhouette problem. It permitted rotation patches only on 12 arm/leg joints at listed ticks, keeping Hips, torso, head, shoulders, fingers, tail, translations, scales, entry, handoff and all other keys frozen. The preserved patch contains 72 changes, zero out-of-scope changes and no removed original keys.

| Measurement | Generated | Refined |
|---|---:|---:|
| Duration / final Unity speed | 62 ticks / 1× | 62 ticks / 1× |
| Requested strike pose | 23 | 23 |
| Fastest downward interval ends, both hands | 22 | 22 |
| Lowest hands relative to chest | 26 | 24 |
| Both wrists below both ankles at contact | −18.1 cm | +12.1 cm |
| USD hip travel | 0 | 0 |
| Entry pose restored by IASA | 57 | 57 |

The first pass missed the desired fist/feet relationship; refinement improved it. The fastest descent occurs in the interval ending at tick 22, followed by the requested pose at 23. The lowest hand position follows at 24 in the refined version, inside the requested follow-through window. Neither observation was used to alter gameplay timing.

The refined motion has readable overhead anticipation, a short simultaneous downward snap, compact tucked legs and recovery to the existing asymmetric fall pose. Its rear-camera fists are still partly obscured by the torso, and the load/active hold and recovery look fairly rigid. Modest tail overlap helps, but manual work on silhouette, arcs, spacing and asymmetry would improve the result. No gross skin detachment, bind-pose break or scale failure was observed in the inspected actual-prefab evidence.

Presentation-only landing blends were evaluated from interruption ticks 12, 23 and 35 into the currently bound idle over 12 ticks. Both clips produced finite poses, fixed roots and completed idle weight. This verifies blend execution; it does not establish natural landing gamefeel. The maximum measured bone step was about 16.5 cm for the refined load interruption, so that transition merits visual/human review.

## Evidence, limits and comparison with existing authoring

`comparison.html` is a self-contained player with side/gameplay angles, generation/refinement and Blender/actual Unity views. `comparison-browser.html` is the local-server variant. Normal-speed MP4s encode exactly 62 frames at 60 fps; endpoint pose 62 is available for scrubbing. Contact sheets cover ticks 0, 12, 18, 23, 30, 44, 57 and 62. Complete PNGs, quantitative receipts and scripts remain alongside them. The orbit view is a repeatable gameplay-like angle, not footage from a live match.

Native Ability Lab baseline open/run was attempted first. It failed with `preview.binding.failed`: **Generated animation catalog source hash mismatch for 'manki'**, followed by `preview.unavailable`. No publishing cook or source rewrite was forced to resolve this existing workspace condition. Actual Shared scenario, collision/contact coverage, gamefeel and online acceptance remain unverified. The final console also contains a separate `ChatOverlay` destroyed-object exception from session notification; the experiment did not change that code. The owned gateway completed free/settled with zero active operations.

The original FBX was essential for accurate hierarchy, inverse bind poses, bone lengths, skin weights, geometry and coordinate calibration. Jump/fall references supplied transition familiarity rather than an attack to copy. This test demonstrates that GenRA can propose a fresh gesture and improve a bounded pose using preserved timing. It does not demonstrate source-free rig inference, control-rig output or turnkey Unity import.

Our existing Blender workflow remains stronger for deliberate blocking, arcs, overlap, exact wrist poses and camera readability. GenRA supplies an initial sparse animation and a useful constrained refinement; validation and rig-specific conversion still require the existing tools. The FightGuy IK/FK control copy was not used by this Manki experiment, and controller-space integration remains deferred. The practical next step is manual polish of the refined candidate, followed by normal package/contact validation when gameplay adoption is explicitly chosen. Current `air.4` uses a right-hand hitbox; a double-fist animation alone does not define new hitbox behavior.

Reproduction and artifact index: `artifacts/genra-manki-aerial-20261014/README.md`, `final-output-manifest.json`, `final-source-verification.json`, `conversion-gate.json`, `refinement-preservation.json`, `runtime-origin-curve-preservation.json`, `runtime-aligned-transition-validation.json`, and each pass's `runtime-aligned-unity-assessment.json` / `runtime-aligned-animancer-assessment.json`. Local Unity follow-up is recorded in the ignored root `TESTING-UNITY.md`.
