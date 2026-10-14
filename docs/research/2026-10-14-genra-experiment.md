# FightGuy GenRA experiment — 2026-10-14

The bounded experiment produced a grounded punch and one focused anticipation refinement, imported as separate Unity candidates. Both preserve the real `ground.2` timing and a planted stance. The refinement increased the fist's pullback from 4 cm to 12 cm, while every dense bone channel from contact through handoff stayed identical. The result is useful as an authoring starting point; wrist/fist pose and readability from the rear orbit camera still need manual polish.

This used a **Codex-backed GenRA adapter**, approved by the user after the stock API request hit an unfunded account. It is not a quality comparison against GenRA's stock `gpt-5.4` API run.

## Scope and source

The isolated workspace is repository-local `artifacts/genra-20261014/`, outside Unity Assets. Only final candidate FBXs and their importer metadata went into `Assets/Art/Experiments/GenRA20261014/`:

- `ReferenceRoundtrip.fbx`: conversion control, not generated motion.
- `GeneratedPunch.fbx`: first generated result.
- `RefinedPunch.fbx`: the focused refinement.

The approved model, clips, catalog and `character.json` remain unchanged, verified against SHA-256 backups. No catalog binding, publishing cook, roster update, commit or push was performed. Candidates remain unbound and unaccepted; actual-prefab sampling and skin snapshots are separate from live Ability Lab/Training gamefeel acceptance. Binding a candidate later would change pose-derived collision and needs the existing package/contact validation workflow.

Read before implementation: maintained animation-authoring and bounded animation skills; Unity CLI/Editor coordination and testing docs; animation system, baker, renderer and FBX exporter; workspace specs for gamefeel retiming, normal startup correction and sourced clips. Those specs reinforced current Shared timing authority and the separation between source assets and cooked poses.

GenRA was inspected at [revision `5f4ffb8`](https://github.com/MacPaw/GenRA/tree/5f4ffb8ac846f91265c48e8bb65d702dca11b7a1). Its [implementation](https://github.com/MacPaw/GenRA/blob/5f4ffb8ac846f91265c48e8bb65d702dca11b7a1/src/anim_gen/generation/generation.py) generates joint-space keyframes and writes USD. [Dependencies](https://github.com/MacPaw/GenRA/blob/5f4ffb8ac846f91265c48e8bb65d702dca11b7a1/requirements.txt) and imports were checked before choosing the already-installed Python 3.12.13. Installation into the experiment's `.venv` was explicitly approved; versions and the lockfile are retained. Blender 5.2.2, FFmpeg and Unity CLI were already available. No local inference weights were installed. The retained source [license](https://github.com/MacPaw/GenRA/blob/5f4ffb8ac846f91265c48e8bb65d702dca11b7a1/LICENSE) is Apache 2.0 with the Commons Clause.

## Real rig and timing

Model: `Assets/Art/Characters/fightguy/Models/fightguy.fbx`; actual prefab: `Assets/Resources/Characters/FightGuy.prefab`. There are 48 bones, 34 skin-weighted bones and 5,497 Blender mesh vertices. The same-rig reference is the approved `anim.straight-punch`, bound to `fightguy_g_2.anim` (GUID `2abcbbc86b240cfa4a3850e8922b0298`, clip fileID `7400000`).

The source clip is 30 fps, 1.16666675 seconds, frames 0–35. The existing baker yields 71 samples, and `PlayerRenderer` plays it at `71/29 = 2.4482758×`. The original-rate reference round trip and the separately sampled gameplay-rate reference are both retained; the original asset was not retimed.

The current `character.json` supplies these values:

| Event | Tick | Seconds |
|---|---:|---:|
| Anticipation chamber requested | 3 | 0.050000 |
| Right-hand hitbox / strike | 6 | 0.100000 |
| Active interval | 6–11 | 0.100000–0.183333 |
| Recovery begins | 12 | 0.200000 |
| IASA / ready pose | 26 | 0.433333 |
| Duration / handoff endpoint | 29 | 0.483333 |

Both requests explicitly required anticipation, strike, follow-through and recovery; frozen hips, leg chains, translations and scales; no steps, hops, turns or additional displacement. The existing reference itself lunges: hip travel 0.438 m and foot travel about 0.44 m. It supplied rig/upper-body examples, while its lower-body motion was excluded by the request.

## Conversion findings

The conversion proof ran before generation. The actual Unity clip was sampled on the actual prefab through `AnimationClip.SampleAnimation`, the same seam used by `SlopArenaBaker`. It was transferred onto the imported actual FBX rig, exported to USD, imported into Blender, exported through the ordinary FBX exporter, and sampled again on FightGuy in Unity.

The Unity Humanoid T-pose is different from the mesh A-pose. Using the real inverse bindposes established the coordinate mapping `Unity (x,y,z) → Blender (-x,-z,y)` at scale 1.0, with a calibration residual below 0.004 mm. USD sanitizes `mixamorig:` into `mixamorig_`; a complete reversible bone/path/parent map is retained. USD's outer Armature Xform was collapsed without changing world matrices so the FBX could reuse `fightguyAvatar`.

| Check | Result |
|---|---|
| Hierarchy, names and Blender geometry | All 48 bones and 5,497 vertices preserved |
| Source fps / duration | 30 fps / 1.16666675 s preserved |
| USD animated joint / skin error | 0.0315 mm / 0.0328 mm maximum |
| Unity bind position / axes | 0.00481 mm residual / axes unchanged |
| Unity 60 Hz animation error | 0.462 mm joint position, 2.770° rotation, 4.516 mm baked skin error maximum |
| Unity imported mesh count | 12,741 → 12,747 after triangulation/normal/UV splitting |

A new automatically generated Avatar gave 101 mm joint error and 110 mm skin error. That failed control is preserved. Reusing the actual Avatar resolved the large discrepancy. Two historical `Armature.001` mismatch messages remain in the console; the corrected control and final candidates imported successfully, with no new error entries. Exact curve or binary mesh equivalence is not claimed.

GenRA's USD writer copies a layer into a new output directory without rebasing relative texture references. Copying the texture dependency beside each output fixed the preview bundle while leaving USD bytes unchanged. This is a resource-conversion issue, separate from motion quality.

A transient Unity capture initially reused a cached GPU skin pose inside one Editor command. Final Unity evidence uses per-pose CPU skin snapshots from the actual prefab with its original materials. The diagnostic handles the FBX hierarchy's baked unit conversion without modifying the source prefab or runtime renderer. It establishes imported pose/deformation, not live Animancer gamefeel.

## Generation and refinement findings

Both completed passes used the existing ChatGPT-authenticated Codex CLI, `gpt-6.1-sol`, medium reasoning, explicit reference USD and images, identity joint mapping, and no automatic example selection. GenRA's parser, quaternion interpolation and USD writer remained unchanged. The CLI was allowed read-only rig/IK calculations; its prompts, tool events, model configuration, usage and sparse outputs are preserved.

The stock API returned `429 credit_balance_exhausted`. One high-reasoning CLI attempt without calculation tools then timed out after ten minutes without a returned animation. A 4.84-second connectivity probe succeeded; the completed generation took 438.3 seconds and refinement 165.9 seconds. These failed/diagnostic attempts are archived separately. CLI usage includes multiple internal model/tool turns, so “two passes” does not mean two underlying inference requests. Generation reports 1,031,889 input tokens (940,160 cached), 21,645 output and 4,961 reasoning output; refinement reports 602,106 input (540,544 cached), 3,880 output and 593 reasoning output. These counts are not a dollar-price estimate or the stock API's cost.

The refinement targeted one observed weakness: only 4 cm of fist pullback at tick 3. The model returned three rotational updates at that tick. The adapter explicitly merged this patch into the unchanged first sparse animation; it did not silently regenerate or retime accepted sections. Raw patch and complete merged input are retained. All 48 dense translation/rotation/scale channels at ticks 6–29 are exactly identical between outputs.

| Measurement | Generated | Refined |
|---|---:|---:|
| USD duration / fps | 29 ticks / 60 | 29 ticks / 60 |
| Maximum forward reach | Tick 6 | Tick 6 |
| Anticipation pullback | 4.000 cm | 12.000 cm |
| Hip travel | 0 | 0 |
| Maximum USD foot drift | 0.0125 mm | 0.0125 mm |
| Entry-pose error at IASA | 0.0131 mm | 0.0131 mm |
| Entry-pose error at endpoint | 0 | 0 |

Unity imports both at 60 fps, duration 0.483333349 s, 29 baked frames, runtime speed 1×, valid Humanoid Avatar and 136 curves. Actual-prefab sampling keeps maximum forward reach at tick 6. Maximum hip movement is 0.00061 mm, foot movement 0.00288 mm, IASA pose error 0.00310 mm and endpoint error 0.00014 mm. Compared with the USD, the right-hand position differs by at most 0.287 mm and left hand by 0.946 mm. Root position is `(0,0,0)` and root scale `(1,1,1)` throughout. No timing discrepancy was observed in the completed candidates; the very small stance/pose residuals are reported rather than zeroed.

At contact, the right hand is 0.449 m forward of Hips in the candidate, versus 0.980 m in the authored reference. Shared bakes Hips-relative poses for bone-attached hitboxes, so adoption would change contact coverage even with unchanged gameplay JSON; that is a separate acceptance check.

The stronger chamber is more visible from the side and changes the rear silhouette at tick 3. The short punch still relies heavily on arm/chest motion: the active pose is held nearly unchanged, wrist/fist presentation is soft, and the rear orbit camera hides much of the striking arm. The original authored clip has stronger body mechanics and weight transfer, but uses a lunge outside this experiment's planted-stance requirement. No gross bind-pose break, skin detachment or scale/facing failure was observed.

## Evidence and authoring comparison

Normal-speed side and gameplay-like orbit MP4s, complete frame sequences and contact sheets for all three clips are in `artifacts/genra-20261014/`. The MP4s have exactly 29 frames at 60 fps, duration 0.483333 s. Browser playback was checked at 6 frames per 100 ms. The Blender orbit pitch is about 25°, within the project's current 0–45° envelope; it is a repeatable study angle, not footage from a live match camera. Actual-prefab Unity sheets are also retained.

The existing Blender workflow gives direct control over blocking, arcs, joint/fist poses, interpolation and camera readability. GenRA can propose coherent sparse data and improve a tightly bounded pose while preserving a clock, but this run needed explicit rig/timing data, inverse-bind conversion, a provider adapter, numerical calculations and visual review. It is suitable for experiments or initial poses; this result does not justify replacing the established authoring and validation workflow. Human gamefeel and live Ability Lab/Training acceptance remain pending.

Detailed local receipts and reproduction scripts: `artifacts/genra-20261014/README.md`, `experiment-manifest.json`, `source-manifest.json`, `unity-roundtrip-validation-60hz.json`, `generated/assessment.json`, `refined/assessment.json`, `refinement-comparison.json`, `unity-candidate-assessment.json`, and `unity-candidates.json`.

Credentials were never written or printed. The API key was supplied by a hidden local prompt, held only in memory, checked against experiment artifacts for leakage, and discarded when switching to Codex. Codex reused its existing login without copying or opening credential files. Subscription sign-in and scripted Codex runs are documented in [OpenAI authentication](https://learn.chatgpt.com/docs/auth) and [non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode).
