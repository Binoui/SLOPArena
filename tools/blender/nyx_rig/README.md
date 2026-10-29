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

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/build_rig.py -- tmp/nyx-model/rig-build-v1
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/verify_rig.py -- tmp/nyx-model/rig-build-v1
blender -b --factory-startup --python-exit-code 1 --python tools/blender/nyx_rig/review_renders.py -- tmp/nyx-model/rig-build-v1
python3 tools/blender/nyx_rig/make_review_sheet.py tmp/nyx-model/rig-build-v1
```

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

### Unity package

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
