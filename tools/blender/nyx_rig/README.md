# Nyx Mixamo preparation

Prepares the owner's two dressed Tripo v1 A-pose models for rigging. Nyx is one woman with REAL and FAKE forms;
these scripts align their hair crowns at 1.70 m, preserve their differing proportions and full mesh resolution,
and take FAKE's backpack, pole and ring light out of the Mixamo upload. The preparation commands below precede the [shared rig and package build](#shared-rig-and-first-playable-package).

Run from the repository root with Blender 5.2 and Python with Pillow already available:

```bash
# Inspect source islands, calibrated front/back/side views, and raw coordinates.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/inspect_models.py -- \
  tmp/nyx-model/tripo-v1 tmp/nyx-model/mixamo-upload-v1/inspection
# Verify the rear component selection and expose REAL's hidden leg mesh.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/inspect_parts.py -- \
  tmp/nyx-model/mixamo-upload-v1/inspection
# Prepare: independent of inspection files. Exports the two uploads, rigid prop, packed blends and dressed GLBs.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/prepare.py -- \
  tmp/nyx-model/tripo-v1 tmp/nyx-model/mixamo-upload-v1
# Fresh-process re-import: isolated FBXs, no external texture search, coordinate/UV/media checks and nine renders.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify.py -- \
  tmp/nyx-model/mixamo-upload-v1
# Contact sheets and illustrated Mixamo markers, made from the actual re-imported uploads.
python3 tools/blender/nyx_rig/make_sheet.py tmp/nyx-model/mixamo-upload-v1
```

`verify.py -- <out> --checks-only` repeats numerical/media checks without rendering.
`probe_landmarks.py -- <inspection>` exposes FAKE's leg mesh and samples both crotches.
`inspect_regions.py -- <inspection>` colours FAKE's face islands and probes the torso back panel; blue is hair,
green is the top, yellow is torso shoulder straps. These optional inspections use the same headless Blender flags.

## Why the preparation looks like this

- **Scale:** crown-height matching, excluding cat ears, headband and ring. REAL scales 1.700830488 from raw coordinates;
  FAKE scales 1.862814353, which is 1.095238100 relative to REAL. No nonuniform reshaping or pose correction.
- **Measurements:** `landmarks.json` records visually estimated joint centres, method and uncertainty. Crown and sole
  come from geometry; the groin proxy comes from the isolated trouser/shorts component under the clothes. Height
  differences and transverse spans in A poses are not proof of equal bone lengths. See the output measurements.
- **Prop:** the 32 inspected edge-connected islands wholly behind raw Y=0.012 are the bag, hardware, pole and ring.
  Keep the torso straps and plain torso back panel. Selection is guarded by expected mesh counts; inspect a new
  Tripo revision and revise the configuration rather than reusing these IDs blindly.
- **Resolution:** copy complete islands and preserve every polygon, UV and texture pixel. Original zero-area polygons
  and mesh seams are counted in the verification report; no blanket cleanup, decimation, weld or texture resizing.
- **Orientation:** Z up, front -Y, feet on Z=0, X=0 centred. Ground origin is below the estimated ankle midpoint.
  Apply uniform scale to vertices. FBX uses the same default -Z-forward/Y-up export as Salamander, embedded media,
  no animation, no cameras/lights/armatures. GLBs are self-contained; FAKE's dressed preview includes the prop.
- **One later skeleton:** these files have none. Separate Mixamo downloads must later be reconciled to one shared
  rest skeleton; matching bone names does not establish that. The prop retains its body-relative placement and
  floor origin for a later rigid attachment under Spine2.

The output `README.md` is the current owner handoff, with upload/download instructions, marker locations, exact
file sizes and known risks. `preview.jpg` shows both body uploads at one scale plus the prop; `split-verification.jpg`
shows body-only and prop-only front/back/side. `nyx-prepared.blend` opens with REAL visible; hide it and reveal FAKE
and its backpack for the other dressed form. `nyx-fake-backpack.blend` is a complete prop-only scene with packed media.

Prior art: [Salamander](../salamander_rig/README.md), [Bashie](../bashie_rig/README.md),
[model polish](../../../docs/characters/character-model-polish.md).

## Shared rig and first playable package

The 2026-10-29 rig build uses the owner's fake Mixamo skeleton for both forms. The real mesh keeps its own Mixamo weights, with the repairs below. All scripts run from the repository root.

The original artifacts and receipts are preserved under `tmp/nyx-model/rig-build-v1/`. Do not rebuild into that approved historical baseline. Use the v3 build commands below for the current candidate.

`build_rig.py` writes `Nyx.fbx` without embedded media, `Nyx_rig.blend`, two 2048-square colour maps in `textures/`, and `Nyx_report.json`. `fit-coordinates.npz` retains the real mesh before and after fitting. `a-pose.npz` retains the original fake bone matrices for the round-trip check. `verify_rig.py` independently imports the exported FBX and checks the single armature, 65 Mixamo bones plus two prop bones, horizontal T-pose arms, finite geometry, normalized weights, four-influence limit, rigid props and skirt, texture size and absent embedded media.

### Fit and weights

- The fake skeleton is normalized to scale 1. Its 65 `mixamorig:` names and full finger chains remain.
- The real face, torso, hair and dress stay at their original fitted coordinates. Limbs receive blended translations from their own joints toward the fake joints. The soles keep their height. The head is neither rotated nor moved to the fake head joint. Its bind offset preserves the face and profile despite the different head pivot and lean. The report distinguishes the joint displacement table from mesh displacement.
- The T-pose rest uses Salamander's inverse-skinning solve. Reposing the final rig into the original fake A pose reproduces the fitted meshes within 12 micrometres. This preserves the face during fitting; later poses still move the head normally.
- Hair islands, 5,045 fake vertices and 3,253 real vertices, follow `Head` rigidly. They receive no arm weights. Hair has no secondary motion.
- Small disconnected jewelry pieces receive one bone per island. The report lists 225 fake and 227 real rigid islands, including nails and face accessories. Torso skin loses stray limb weights.
- The long dress is a separate `Nyx_Real_Dress` renderer on `Hips`, with no leg weights. This avoids skinning the skirt into a crotch. `unity_import.eval.cs` adds waist-pinned cloth, five torso/leg capsules, self-collision and a separate double-sided dress material. `GraphicCharacter.shader` exposes `_Cull`, and `MatchVisualStyle` preserves the source value when it replaces materials. Other materials default to backface culling. The saved prefab is unpacked from the imported model before authoring Cloth. A model variant dropped the Cloth during serialization, so the importer asserts that the saved asset contains it. `dress_cloth.eval.cs` reapplies those cloth limits to an existing prefab. Extreme leg clearance remains a separate runtime check. The Blender sheet shows skinning without Unity cloth.
- `Nyx_Backpack` is the complete bag, pole and ring, rigid on a new `Nyx_Backpack` bone under `mixamorig:Spine2`. `Nyx_PoleHeld` copies only the pole, its lower shaft, ring and collar, rigid on a new bone under `mixamorig:LeftHand`. It is hidden by default. The source-island guards reject changed input topology.
- No clean phone island was found in the fake body. The build extracts no phone and invents no replacement. The body retains its original surfaces. A shared phone prop remains open.

### Historical first-package integration

Use a distinct caller identity and your own bounded gateway lease under [Editor coordination](../../../docs/contributing/unity-editor-coordination.md). The canonical project is `client/Unity`. Never run these through a raw Editor connection.

1. Add the `Nyx = 8` selector and its existing selector adapters. Build Shared without a plugin copy during preparation. Copy the plugin only under your own bounded gateway lease.
2. Create `nyx` with `sloparena.character.create`. Its trusted built-in profile admits FightGuy's two internal stand-in capabilities. The package retains source provenance.
3. Run `unity_import.eval.cs` as an absolute-path gateway `eval_file`. It imports `Assets/Art/Characters/nyx/Models/Nyx.fbx`, the colour maps and materials. It writes `Assets/CharacterPackages/nyx/Presentation/NyxRig.prefab`, maps all 46 Humanoid bones and rejects invalid avatars or implausible `humanScale`. The measured scale is 0.9674076. Fake, backpack and held pole renderers are inactive. Real body and dress are active. There is no phone renderer or `FormPresentation` component.
4. `write_character.py` prepares FightGuy's kit and movement with `anim.nyx.*` IDs and no FightGuy effects. It refuses to overwrite an engine-authored `alternateForm`. For a review-only file, pass `--output <path>`. Do not rerun it after the engine task starts editing the package.
5. Run `borrow_clips.eval.cs` and `bind_catalog.eval.cs` through gateway `eval_file`. There are 22 semantic animation bindings. Copies have their animation events removed and authored-root height adjusted. Idle is raised 0.850 m at rig scale 1. Reruns preserve clip GUIDs.
6. Run `tools/character_sizes/lineup.eval.cs`. The accepted first-pass values are `visualScale` 1.0375, `modelYOffset` -0.85 and `hipHeight` 0.832. Idle head height is 1.334 m, between FightGuy at 1.328 and Wibou at 1.340. The lowest point is on the floor.
7. Run `sloparena.character.inspect`, `cook`, `verify` and `sloparena.character.roster.admit --package-id nyx --selector Nyx`. Later cooks refresh the existing pin. Check `git status content-cooked/` before and after batches, and preserve other packages' outputs.
8. Exercise Training through `sloparena.photo.prepare --stage slop_pit --fighter nyx --training true --opponent fightguy`, `editor_play`, `sloparena.training.arrange`, then `photo.live-begin`, `training.run` and `photo.live-end`. Declare `photo.live-end`, `editor_stop` and `photo.restore` as restoration. `photo.live` itself waited for Slopball in this checkout; `live-begin` worked with Training.

The package has FightGuy's moves and movement as approved placeholders. The separate ADR-0051 engine task owns `alternateForm` and `FormPresentation`. This build does not author them. Vendor animation copies remain local, following Salamander's policy. The original Nyx model, colour maps and materials are allowlisted in `.gitignore`; project-authored crouch and tumble copies are allowlisted too.

`tests/Shared.Tests/NyxPackageTests.cs` executes all fourteen saved slots through the pinned content catalog, checks recovery and finite positions, and covers hit, shield and miss on the first normal. Local task receipts, hashes, review paths and the complete displacement table are under `tmp/nyx-model/rig-build-v1/`.


## Real lower-body rebuild, v3

This section supersedes the v1 Cloth instructions above. The v3 candidate is `tmp/nyx-model/spring-dress-v3/`. The owner rejected both Cloth versions. The coarse original was stiff, and the procedural v2 cone lost the approved folds, collapsed between the legs and moved its hem 1.6 m on a turn. The long fighting-game dress now uses skinned leaf chains with presentation-only springs.

The commands in this v3 section record the historical build. Use the current prop build commands below for new runs. Do not overwrite the preserved v3 baseline.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/build_rig.py -- tmp/nyx-model/spring-dress-v3
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_rig.py -- tmp/nyx-model/spring-dress-v3
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/check_preservation.py -- tmp/nyx-model/cloth-rebuild-v2 tmp/nyx-model/spring-dress-v3
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/review_renders.py -- tmp/nyx-model/spring-dress-v3
python3 tools/blender/nyx_rig/make_review_sheet.py tmp/nyx-model/spring-dress-v3
# After the Unity pose captures:
python3 tools/blender/nyx_rig/make_spring_review.py tmp/nyx-model/spring-dress-v3
```

`real_lower_body.py` keeps v2's black tights and real boots. It restores the dress's original Tripo coordinates before the limb fit, subdivides the original triangles twice, and applies four mild smoothing iterations. It retains the original texture atlas and interpolated UVs. It does not replace the folds with a cone or a fabric swatch. The refined skirt has 7,979 vertices and 7,876 faces. Its hem is at 0.273 m, just above the boots.

Twelve four-bone chains, `Nyx_Skirt_00_00` through `Nyx_Skirt_11_03`, branch below `mixamorig:Hips`. They add 48 leaf bones. The existing 67 bone names, hierarchy and rest matrices remain exactly unchanged. The preservation check also compares the retained real body, tights, fake body and both props exactly against v2. No bone is inserted between Humanoid mappings.

The waistband follows Hips. Lower weights interpolate neighbouring angular chains and vertical bone levels, with at most four influences. The dress stays one continuous mesh. `SkirtBoneSpring` uses the tail spring's Verlet/rest-target approach, with circumferential smoothing, bounded inertial travel, a 20-degree world-direction limit around each posed segment, six mathematical leg capsules and pose-driven lift for raised thighs. After solving tips, it checks every vertex of the actual weighted fabric and rotates skirt bones out of capsule contacts between chains. Pose lift moves with the leg immediately. The spring preserves its inertial offset around that lifted pose. It never changes mapped bones or Shared state. `LateUpdate` runs only in Play Mode. Controlled offline pose checks explicitly call `Step`.

Run `unity_import.eval.cs` through your own gateway lease after the build. Its build path currently names the v3 directory. It loads the latest prefab, checks the existing 67 bone transforms and Humanoid description, adds only missing skirt leaves, replaces the real meshes, removes all old Cloth and cloth-collider objects, and configures the spring. The existing Avatar and humanScale must remain valid and unchanged. It refreshes FormPresentation's real-form objects without editing gameplay source. `dress_cloth.eval.cs` now rejects use so an old plan cannot silently restore Cloth.

Real-form renderer names are exactly `Nyx_Real`, `Nyx_Real_Tights`, and `Nyx_Real_Dress`. GenRA's final real pack must include all three. Added skirt bones require rebuilding that rig pack. The fake form and its props are unchanged.

For controlled eight-pose spring captures, run `pose_springs.eval.cs` through the gateway with the Editor stopped. It uses a disposable preview scene and the actual saved prefab and spring. It also renders fist and open-hand closeups and records independent baked-skin vertex penetration counts against the leg capsules. Those counts are sampled geometry checks, not a universal collision guarantee. Blender pose sheets are skin-only and do not simulate the runtime spring.

Generate a fresh native Training plan, execute it through your own gateway lease, then encode and measure:

```bash
python3 tools/blender/nyx_rig/capture_springs.py tmp/nyx-model/spring-dress-v3/final
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch tmp/nyx-model/spring-dress-v3/final/play.json > tmp/nyx-model/spring-dress-v3/final/play.jsonl
python3 tools/blender/nyx_rig/report_springs.py tmp/nyx-model/spring-dress-v3/final
```

The plan records the current R kick, resumes only a paused owned Play session, restores capture timing, stops Play Mode and restores the previous scene. Each output directory must be fresh. MP4 output is H.264 with yuv420p. `measure_springs.eval.cs` samples fixed rest-hem vertices on the baked skin, removes hip translation and rotation, and reports deformation rather than fighter travel. The imported bones have scale 100, so physical spring distances use the model root. The Unity probe verified that `BakeMesh(mesh, true)` returns vertices that can be transformed with the renderer matrix. The rest-hem selection uses a fraction of mesh height because imported mesh coordinates are centimetre-scaled. The report's source receipts identify run and turn windows. Read the candidate's `REPORT.md` for measured values and remaining visual problems. Automated checks do not establish owner acceptance.

Leave `nyx/character.json` to its gameplay owner. Transient `capability.ambiguous` diagnostics during concurrent engine work are a reason to wait and retry, not to edit that file. Cook and verify identities remain pending whenever current source is invalid or another owner is rebuilding the animation catalog.

## Current prop split and shared phone

The current integrated candidate is `tmp/nyx-model/props/`. It retains the complete spring-dress-v3 surface and all 115 existing bone rest matrices. The only added bone is the leaf `Nyx_PhoneBone` under `mixamorig:RightHand`. `props-preservation.json` proves the existing bones and retained surfaces exactly match v3. It also proves that the split backpack surfaces reconstruct the original vertices, faces, UVs and weights without loss.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/build_rig.py -- tmp/nyx-model/props
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_rig.py -- tmp/nyx-model/props
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_props.py -- tmp/nyx-model/spring-dress-v3 tmp/nyx-model/props
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/review_props.py
python3 tools/blender/nyx_rig/prop_events.py
```

Run `unity_import.eval.cs`, then `props_bindings.eval.cs`, through your own gateway lease. The importer now reads this candidate. It checks every existing bone and the existing Humanoid description, Avatar reference and humanScale. It keeps the spring dress and imports all eight named renderers. The binding step merges only three prop presentation bindings into the existing asset catalog.

The renderer names are exactly:

- `Nyx_Real`.
- `Nyx_Real_Tights`.
- `Nyx_Real_Dress`.
- `Nyx_Fake`.
- `Nyx_Backpack`.
- `Nyx_BackpackPole`.
- `Nyx_PoleHeld`.
- `Nyx_Phone`.

`Nyx_Backpack` contains the bag and its straps. `Nyx_BackpackPole` contains the pole, lower shaft, collar and ring light. Both remain rigid on the existing `Nyx_Backpack` bone. `Nyx_PoleHeld` retains its existing geometry and LeftHand leaf bone.

`Nyx_Phone` is one skinned renderer, rigid on `Nyx_PhoneBone`. Its black screen and pink glitter case are authored as a 7.5 × 15.5 × 0.9 cm rounded slab with small camera details. The current fake right hand is open and has no visible baked phone. Placement therefore uses its palm and finger landmarks. The saved real-form prefab hides the phone. Fake-form defaults show one phone. Final GenRA hand poses must grip it with the existing finger chains.

`PropVisibilityPresentation` receives the existing dispatcher events through `IPresentationOwnerReceiver`. It gives `FormPresentation` named renderer overrides scoped to a form. Its event prefab has `AttackBoundPresentation`, so duration, interruption, replacement, owner disable, rollback retraction and fresh-simulation cleanup use the existing dispatcher lifecycle. The component also runs in Edit Mode for Lab previews. Travel hiding suppresses the overrides. Form selection filters them, so correcting a predicted form keeps an event that is still active.

The three installed semantic events are `presentation.nyx.props.pole-held`, `presentation.nyx.props.phone-raise`, and `presentation.nyx.props.phone-drop`. Exact `emitPresentation` operations for fake ground 3, fake air 3, Going Live and Logging Off are in `tmp/nyx-model/props/events.json`. `prop_events.py` writes that handoff without modifying `character.json`. The engine owner must merge it. Ground and air F share the existing aliases.

Run `preview_props.eval.cs` through the gateway to fire actual `TimelinePresentationEvent` values through `TimelinePresentationDispatcher` in a disposable preview scene. It loads the saved rig, saved prop prefabs and catalog bindings. Its assertions and images are written to `tmp/nyx-model/props/unity-preview/`. This proves the presentation mechanism independently of the pending gameplay-source handoff. It does not prove the final GenRA timing or hand grip.


## Current real-form waist repair

The current candidate is `tmp/nyx-model/waist-fix/`. This supersedes the prop candidate for new imports. Keep `tmp/nyx-model/props/` as the approved comparison baseline and phone-mesh source. Do not overwrite it.

The copied fake hips caused the solid black band on the real dress. The builder removes tights faces above Z=0.84 m and tapers the surviving upper vertices inward from Z=0.70 m. Their weights and UVs remain exact. The renderer stays `Nyx_Real_Tights`. The dress cap above Z=1.00 m moves smoothly up by at most 1.4 cm and toward the bodice using Y × 1.33 + 0.12 m at the top, closing beneath the belt. Dress topology, UVs, weights and the lower bell remain unchanged. All 116 bones retain their exact rest matrices and parents.

The builder retains the approved phone mesh from `props/Nyx_rig.blend`. Blender's procedural bevel otherwise introduces small nondeterministic UV rounding differences during a full rebuild. This repair changes no prop surface.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/build_rig.py -- tmp/nyx-model/waist-fix
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_waist.py -- tmp/nyx-model/props tmp/nyx-model/waist-fix
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_rig.py -- tmp/nyx-model/waist-fix
```

After those checks pass, run `unity_import.eval.cs` through your own gateway lease. The importer reads the waist candidate and checks the existing Avatar and bones. The waist report records matching GenRA-lit closeups, controlled spring poses, Training captures and package verification status. Rebuild the GenRA real rig pack to include the corrected dress and tights geometry. Existing renderer names, bones and animation keys remain compatible.


## Current real-form spine fit

`tmp/nyx-model/spine-fix/` supersedes the waist candidate for new imports. Keep the waist, prop and v3 directories as comparison sources. The final packed Blender source and exported FBX are in the spine directory. Unity import, controlled poses, close Training idle/run/g3, the 24 prop checks, and package cook/verify passed. The task report records the exact hashes and remaining extreme-pose contacts.

`fit_spine.py` postprocesses the final waist candidate. It fits the real axial anatomy to the unchanged shared pivots, translates rigid face/hair geometry together, and replaces the torso's axial weights with a continuous segment field. It restores the refined approved skirt from v3, adds the canonical frame offset omitted by the lower-body rebuild, then fits its waist to the real bodice. The cap shares the bodice's skin field and fades into the existing skirt chains. The already trimmed/tapered tights remain exact.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/fit_spine.py -- tmp/nyx-model/waist-fix tmp/nyx-model/spine-fix
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_spine.py -- tmp/nyx-model/waist-fix tmp/nyx-model/spine-fix
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_rig.py -- tmp/nyx-model/spine-fix
```

Use `unity_import.eval.cs` through your own gateway lease. It reads this final candidate. All 116 bones, including skirt leaves and props, retain exact names, parents and rest matrices. The existing Humanoid avatar and scale are retained. Fake and prop surfaces, boots, topology, UVs and texture files remain unchanged. No animation or gameplay binding is edited by the fit.

The real GenRA rig pack must be rebuilt from `Nyx_Real`, `Nyx_Real_Tights`, and `Nyx_Real_Dress`. Existing clips remain structurally compatible. The complete authored idle and g3 USDs were played on the final mesh in Blender and their bound clips were exercised in Training. Review the other real-form moves and both swaps on the new geometry, especially extreme legs and torso twists. The new rig pack does not require new animation keys solely because of this mesh fit.

Blender spine comparisons, approved front/side/back silhouettes, all eight poses, full motion videos, Unity controlled captures and gateway receipts are under `tmp/nyx-model/spine-fix/`. Skin-only views do not simulate the runtime spring. Controlled spring tests still record high-kick and run-stride boot/hem contact; see the report before claiming universal clearance or owner acceptance.


## Current dress centring

`tmp/nyx-model/dress-centre/` supersedes the spine candidate for new imports. `fit_spine.py` applied the canonical frame offset (8.2 cm backward) to the whole skirt, leaving 15 cm of empty fabric behind the hips and a bustle that read as an oversized rear. The bell now leans forward linearly from the waist (no shift at z 1.0 m) to the hem (full offset back at z 0.3 m), so the waist join is unchanged and the hem centres over the boots. A short smoothstep fade was tried first; it bent the A-line into an S. Run the same `fit_spine.py`, `verify_spine.py` and `verify_rig.py` commands with `dress-centre` as the output; `side-compare.jpg` there compares approved v3, spine-fix and this fix.
