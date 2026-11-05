# Salamander rig build

Fighter #6, the tiger-salamander gunslinger (2026-10-26; his name is still open, so his package ID is the working
`salamander`). These scripts take the owner's Tripo model and Mixamo rig to a Unity-ready rig and fill his package,
which borrows Manki's kit until his own moves exist. The 2026-10-26 inputs and outputs are local, under
`tmp/salamander-gunslinger/`.

```bash
B="blender -b --factory-startup --python-exit-code 1"
# 1. Tripo's character extraction returns a kit, not a dressed figure: split it into parts...
$B --python tools/blender/salamander_rig/split_parts.py -- <tripo.fbx> <tripo_rgb.png> <parts_dir>
# ...and dress the body (hat on the skull, poncho on the neck, mirrored boot, mustache and straw on the snout).
blender -b --python-exit-code 1 <parts_dir>/parts.blend --python tools/blender/salamander_rig/dress.py -- <dress_dir>
# Optional: repaint texture blemishes from the mirrored side (the tail had an orange smear).
blender -b --python-exit-code 1 <dress_dir>/dressed.blend --python tools/blender/salamander_rig/tex_mirror_fix.py -- \
  <tex.png> <fixed.png> '[{"lo": [x, y, z], "hi": [x, y, z]}]'
# 2. The Mixamo upload: body, boots, mustache and straw as one mesh (hat and poncho go on after rigging).
blender -b --python-exit-code 1 <dress_dir>/dressed.blend --python tools/blender/salamander_rig/export_mixamo.py -- \
  <fixed.png> <upload_dir>
# 3. A clean rectangular poncho: 2 cm quads, an oval neck hole, its own striped texture and an A-pose cloth drape.
$B --python tools/blender/salamander_rig/build_poncho.py -- <dress_dir>/dressed.blend <poncho_dir>
#    Writes poncho.blend, textures/Salamander_Poncho.png and review renders. drape_poncho.py is the old Tripo pass.
# 4. The jeans (a separate Tripo model) fitted to the Mixamo body, with the tail hole and tail patch.
$B --python tools/blender/salamander_rig/fit_jeans.py -- <jeans.fbx> <jeans_rgb.png> <mixamo.fbx> <fixed.png> <jeans_dir> \
  '{"patch_blur": 2.0, "sub_r": 0.075}'
# 5. Build: weights fixed, tail chain, finger chains, Hat/Mustache/Straw bones, poncho and jeans skinned, T-pose rest.
$B --python tools/blender/salamander_rig/build_rig.py -- <mixamo.fbx> <dress_dir>/dressed.blend <poncho_dir>/poncho.blend \
  <fixed.png> <build_dir> jeans=<jeans_dir>/fitted.blend jeans_tex=<jeans_dir>/jeans_basecolor.png \
  poncho_torso=1 poncho_tex=<poncho_dir>/textures/Salamander_Poncho.png
# 6. Settle the poncho over lowered arms. The separate fabric material and textures survive the re-export.
$B --python tools/blender/salamander_rig/redrape_poncho.py -- <build_dir> <redrape_dir> \
  '{"bending":2,"frames":75,"pin_r":0.075,"pin_r1":0.105,"tension":60,"self":true,"quality":12,"mass":0.2,"preclear":true,"jeans_collision":false}'
#    The jeans set initial clearance only; their extracted patch edges catch cloth. The body remains a collider.
# 7. Review the lowered-arm rig. This also writes sheet.jpg beside the pose PNGs.
$B <redrape_dir>/Salamander_rig.blend --python tools/blender/salamander_rig/pose_check.py -- \
  <redrape_dir>/textures/Salamander_Body.png <redrape_dir>/poses
#    jeans_poses.py <out_dir> (poncho hidden: walk, kick, crouch, tail), finger_check.py <tex> <out_dir> (finger gun),
#    render_mats.py <out_dir> '<json views>' (any .blend with its own materials).
```

The build writes `Salamander.fbx` (no embedded media), `textures/Salamander_Body.png` (body and hat),
`textures/Salamander_Jeans.png`, `textures/Salamander_Poncho.png` (1024 square), `Salamander_rig.blend` and
`Salamander_report.json`. Without `poncho_tex`, the poncho shares the body material as before.

## His package

`client/Unity/Assets/CharacterPackages/salamander/` (2026-10-26) was made with `sloparena.character.create` and is a
trusted built-in package (`CharacterPackageAuthoringService.ProfileFor`), because Manki's specials use internal
capabilities. Run each script as a gateway `eval_file` (absolute path):

1. `unity_import.eval.cs` imports the build: the model, textures and materials under
   `Assets/Art/Characters/salamander/` and the rig prefab as `Presentation/SalamanderRig.prefab`. Import settings,
   material and Humanoid mapping are Manki's; all 46 Humanoid bones map, fingers included. The tail chain and the
   Hat, Mustache and Straw bones are unmapped, so clips leave them at rest.
2. `borrow_clips.eval.cs` copies every clip Manki's catalog binds into `Animations/Borrowed/`, idle and run included,
   with events dropped. Played on him, Manki's clips stand him 0.44 m under the floor (prefab scale 1), so each copy
   bakes its height from the authored root and is raised by what puts Manki's idle on the floor.
3. `character.json` is Manki's kit and movement with his IDs (`anim.salamander.*`), FightGuy's body volumes
   (capsule, hurtboxes, shield, capture), none of Manki's effects (no `presentationIds`, `hitPresentationId` or
   `emitPresentation`, blank `explosionPresentationId`s), `visualScale` 1.8, `modelYOffset` -0.85 (minus half the
   capsule) and `hipHeight` 0.7. In the lineup (`tools/character_sizes/lineup.eval.cs`) his idle head is at 1.34 m,
   level with Wibou and FightGuy (owner: their height), and his lowest point is on the floor.
   `poncho_cloth.eval.cs` (after every import, which rewrites the prefab) makes the poncho Unity cloth: the build's
   `poncho_torso=1` skins it to his spine, neck, shoulders and hips only, the collar is pinned, the rest may swing up
   to 10 cm, and capsules on his arms, chest, hips and thighs push it. Cloth.vertices are world-scaled and centred on
   the renderer's root bone (Hips), not in the mesh's space.
4. `bind_catalog.eval.cs` binds the rig and one clip per animation ID, with its own pose track. Then
   `sloparena.character.inspect`, `cook`, `verify` and `sloparena.character.roster.admit --package-id salamander
   --selector Salamander` (`CharacterClass.Salamander` = 7).

In game, `sloparena.tutorial.open --fighter salamander` plays him (2026-10-26: lesson 9, punch, kick and Bazooka hit the
dummy, no console errors).

Shipping follows the sources. His model, materials and textures are original and re-included in `.gitignore`, as are
his copies of Manki's project-authored clips (crouch, the shared tumble, g4, a3 and the two F clips). His copies of
Manki's vendor-pack clips stay local, like the originals.

## Why the rig looks like this

- **Tripo kit:** character extraction returned the bare body with every part beside it at its own scale and turn,
  an extra head carrying the face props, one boot and a backdrop card. `dress.py` keeps the body's own head, moves the
  mustache onto the snout, cuts the cone capping the brim's hole so the hat sits on the skull, mirrors the boot and
  deletes the feet hidden inside the boots.
- **Mixamo's weights:** his right hand and forearm spilled onto his waist, the straw followed `RightArm`, the snout
  and jaw followed `Neck` and the tail followed the thighs. The build cuts arm weights by surface distance from the
  fingertip (wrist seeds caught hip skin), gives the lower head to `Head`, and adds `Tail1`-`Tail5` under `Hips`.
- **Fingers:** Mixamo gave one chain per hand. His cartoon hand has a thumb and three fingers, split by position into
  Mixamo-named thumb, index, middle and ring chains, so Finger Guns can keep the index straight. The knuckles (option
  `knuckle`, default 0.065 m from the wrist joint) sit where the fingers part from the palm; a finger whose base stays
  on the palm only hooks its tip, and the finger gun reads as an open hand (rig-build-v10, 2026-10-27).
- **T-pose rest:** the download's bone rest is the upload A-pose and its take is a T-pose. build_rig solves the mesh
  back into the T-pose by inverse skinning, so posing the skeleton in the A-pose reproduces the model (within 3 µm).
  With his arms 76° down that rest came out malformed at the shoulders, so his live rest is now forward-skinned
  instead (see "His rest is forward-skinned" below).
- **Poncho:** the fitted poncho "doesn't really float well" (owner), so it is cloth-draped: collar pinned with a soft
  falloff, body collider. Skinned from the body's nearest skin it followed the upper arms, and a raised arm stretched
  it up like a wing (2026-10-27), so it is now skinned to the torso only and moved by Unity cloth in game. GenRA's rig
  pack leaves it out (`unity_rigpack.py` `CLOTH_MESHES`), so the animator poses the body under it.
  The procedural replacement is a 44 × 84 cm rectangle with 2 cm quads, an oval neck hole and its own striped texture.
  Its lowered-arm pass clears inherited skin weights before assigning collar pins; otherwise Hips weights pin the hem.
- **Jeans:** the owner picked patched jeans; Tripo returned them alone, 1 m tall. `fit_jeans.py` maps each height's
  cross-section onto his (per leg below the crotch, the hip ring above, arms kept out), tucks the hems inside the
  boot walls and cuts a hole where rays across the tail axis hit tail skin all round. The tail patch is a lifted,
  square-trimmed copy of the seat whose image (the knee patch, blurred, darker rim) sits in a strip at the bottom of
  the jeans texture, the atlas squeezed into the top 7/8, so the jeans keep one material. Painting the patch into the
  seat's own texels streaked: Tripo's UVs there stretch up to 17×. The knee's X stitches are copied to its corners.
  The build skins the jeans from torso and leg skin (tail weights go to `Hips`) and deletes the body skin they hide.

## Fixing the model after the build

The build's `Salamander_rig.blend` was lost in a `tmp/` cleanup, so the live
`client/Unity/Assets/Art/Characters/salamander/Models/salamander.fbx` is now his source. Fix it in place: import it with
`ignore_leaf_bones=False` (the file has 65 bones, `Tail5` carries weights, and Unity reads the 11 end bones), edit, and
re-export with build_rig's export call. Check that the bone rest did not move before installing: the Humanoid avatar and
his bound clips depend on it.

**His rest is forward-skinned (2026-11-04).** His Mixamo upload hung the arms about 76° down, the inner arm 1 cm
from the ribs. Mixamo gave right rib skin up to full `RightArm` weight, left 30-40% `Spine2` weight down the whole
upper arm and almost none on the `Shoulder` bones. build_rig then made the T-pose rest by inverse skinning. For skin
that blends bones the A-to-T change turns far apart, the inverted blend is near singular, so the rest came out
malformed: rib shards flung up to 25 cm off the right shoulder. A weight-only fix that kept the inverse solve
(2026-11-04, r2) removed the shards but left a dented, lumpy upper arm, and its rib split had moved the inner half of
the upper arm onto the chest. Every clip deforms from the rest, so idle hung the arm off the ribs like a noodle and
the Haymaker and Uppercut twisted the shoulder into a ribbon.

`rebuild_rest.py` poses the FBX into the authored A-pose (the Mixamo download's bone rest; local copies of it and of
the pre-fix FBX are in ignored `artifacts/salamander-rig-inputs/`), fixes the arm weights there, and forward-skins that
clean A-pose into the existing T-pose bone rest, with Blender's Corrective Smooth bound at the A-pose so the raised
shoulder keeps its volume. The result is the new rest; the bones do not move. Forward skinning does not invert
exactly: posed back into the A-pose, his shoulder caps and upper back land up to 3.4 cm off the authored body (a
slightly slimmer shoulder, no dents). That is the price of a clean rest. Only the body needed it: the jeans' inverse
solve was off by at most 1.4 mm, the poncho's and hat's by nothing, and hand-only skin (the rebuilt finger tubes) keeps
its rest. The weight fixes, in order:

- rib skin comes off the arm: torso skin is nearer the chest midline over the surface than the forearm or the outer
  half of the upper-arm tube (inner-arm skin is a quarter turn round the tube from it, rib skin only reaches it through
  the armpit), and arm weight ramps across the armpit seam;
- the upper arm becomes all arm from about a quarter of the way down;
- `Spine2` weight within 17 cm of each shoulder joint moves onto that side's `Shoulder` bone, fading out, since the
  clips shrug the shoulders 50-77°;
- every weight within 15 cm of the joint is smoothed over the surface, then each vertex keeps its 4 largest weights.

It re-imports its export and compares it with the input. If the bone rest moved, it renames the export
`salamander.REJECTED.fbx` and fails. Its input is the pre-fix FBX from 73048edb
(`git show 73048edb:client/Unity/Assets/Art/Characters/salamander/Models/salamander.fbx`).

```bash
B="systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1"
$B --python tools/blender/salamander_rig/rebuild_rest.py -- <salamander.fbx> <salamander-mixamo.fbx> <out_dir> ['<json options>']
#   Writes salamander.fbx, fix_report.json (weights, A-pose deviation, the old/new comparison) and regions.json.
# The rest every clip deforms from, around the right shoulder (clay, front/side/below); also run it on the Mixamo FBX.
$B --python tools/blender/salamander_rig/rest_review.py -- <fbx> <out_dir> <label> [scale=0.5]
# Where the rebuilt rest misses the authored body: posed into the A-pose, coloured by distance from the pre-fix FBX.
$B --python tools/blender/salamander_rig/rest_review.py -- <fbx> <out_dir> <label> apose=<salamander-mixamo.fbx> ref=<pre-fix fbx>
# Clips: drive the FBX with GenRA clip USDs, one clip per process to keep Blender's memory low.
for c in idle g1-haymaker g2-jumping-uppercut ge-sombrero-copter run; do
  $B --python tools/blender/salamander_rig/clip_review.py -- <fbx> client/Unity/Assets/Art/Characters/salamander/Textures \
    <out_dir> <spec.json> clips=$c regions=<rebuild_out>/regions.json scale=0.7 res=900 [clay=1] [paint=Right]
done
```

`spec.json` lists clips as `{"name", "usd", "frames": [..] | "auto", "render": bool}`. Each run writes
`metrics-<clip>.json`: `rib_cm` is how far rib skin moves when only that arm's pose is applied, `stretch` is the
longest armpit edge over its authored length, and `folds` counts armpit edges folded past 120°. With `regions=`, ribs
and armpit come from the authored A-pose; without it, they come from the FBX rest, which missed the rib skin the old
rest had flung out. Use the same `regions.json` for every model you compare. Judge by eye first: `clay=1` shows shape
without the camouflage texture, and `paint=Right` colours that arm's weights (red arm, green `Shoulder`, blue `Spine2`).

## Spring poncho candidate (2026-11-05)

The live poncho is skinned to his spine and hips only, so it moves like a rigid box (owner: "the poncho doesn't flow
and it makes everything look weird"). The candidate gives it Nyx's approach, skinned leaf chains moved by the
presentation-only `SkirtBoneSpring`, on a prefab beside the live one. Nothing live is replaced, bound or cooked.

```bash
B="systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1"
$B --python tools/blender/salamander_rig/spring_poncho.py -- \
  client/Unity/Assets/Art/Characters/salamander/Models/salamander.fbx tmp/dusty-poncho/build-v1
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  eval_file --file "$PWD/tools/blender/salamander_rig/spring_poncho_import.eval.cs" > import.jsonl
python3 tools/blender/salamander_rig/spring_poncho_review.py plan tmp/dusty-poncho/review-final
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch tmp/dusty-poncho/review-final/batch.json \
  > tmp/dusty-poncho/review-final/batch.jsonl
python3 tools/blender/salamander_rig/spring_poncho_review.py encode tmp/dusty-poncho/review-final
```

- **Chains.** `spring_poncho.py` adds ten four-bone leaf chains, `Salamander_Poncho_00_00` to `_09_03`, under
  `mixamorig:Spine2`. Five run down each panel at blanket x of -20, -10, 0, 10 and 20 cm (the planar `Blanket` UV is
  the unfolded 44 × 84 cm rectangle). Their joints sit on the fabric 10, 18, 26 and 34 cm from the shoulder line, and
  the hem is at 42 cm. Ring order is the front from his right to his left, then the back from his left to his right.
  Only the poncho is reweighted. The collar keeps its spine and neck weights. The fabric over each shoulder top moves up
  to half of that onto the shoulder bone. From 9 to 16 cm below the shoulder line the weights ramp onto two
  neighbouring chains at two bone levels, at most four influences.
- **Preservation.** The script re-imports its export and renames it `*.REJECTED.fbx` unless the 65 existing bones keep
  their names, parents and rest frames (heads within 1e-5 m, orientations within 1e-4) and every other mesh is
  identical. FBX stores no tails, so Blender's guessed tail for `Spine2`, which gained children, is only reported.
  `spring_poncho_import.eval.cs` checks again in Unity: every live transform within 1e-5 m and 0.01°, the only new
  transforms the 40 chain bones, the live Humanoid description, a valid Avatar and the live humanScale (0.4863539).
- **Candidate.** The import writes `Models/Candidates/salamander_spring_poncho.fbx` (the live importer's settings,
  readable, because the spring reads the poncho's vertices in players) and
  `CharacterPackages/salamander/Presentation/SalamanderRig_SpringPoncho.prefab`. Its spring hangs the chains from
  `Spine2` and collides with 14 capsules: thighs, shins, upper arms, forearms, hands, two tail segments, and the belly
  and chest. The belly and chest capsules run between unmapped helpers in front of Hips, Spine1 and Spine2, because his
  torso sits 8-10 cm in front of the spine and only 3.5-6 cm behind it. Without them, running pushed his belly out
  through the front panel. The poncho also gets `Materials/Salamander_Poncho_TwoSided.mat`. The live material culls
  back faces, so the single layer of fabric showed holes and slivers wherever its inside faced the camera.
- **Lift model.** Nyx's `LegPose` lift assumes a long skirt from the waist. On the poncho it flared the hem flat in
  the jump tuck and lifted the front centre in the run. The poncho uses `LiftModel.Clearance`. Each chain swings just
  far enough to clear the limb parts under it, straight out or 40 degrees to either side. A limb only counts when it is
  below the chain's top third, above its hem, inside its rest line and crossing it rather than hanging along it. The
  swing rises at up to 10 degrees a frame and settles at 4. The skin-contact correction is kept between frames and
  eased the same way. The collision passes fade out for a limb in front of the fabric. Each of these rules removed a
  failure seen in the captures: wings from arms hanging beside corner chains, a table-like flip over a forearm in front
  of the panel, and a one-frame snap when an arm left the fabric.
- **Review.** The capture runs in Edit Mode on both prefabs, so the spring takes an explicit `Step` once per frame. It
  draws freshly baked meshes, because skinned renderers only re-skin on Editor ticks, never inside one eval. Root motion
  uses his `character.json` run speed, jump force and gravity, with the run's 180-degree turn in one tick.
  `metrics.md` lists each take's largest hem offset from where `Spine2` would carry it, the largest one-frame change and
  the largest lift.
- **Tests.** `Tools/SLOPArena/Tests/Skirt Bone Spring Clearance` (`SkirtBoneSpringSelfTest`) covers the Clearance rules
  on a synthetic ring of chains. `skirt_spring_identity.eval.cs` drives Nyx's saved rig through 360 scripted frames and
  compares every skirt bone bit for bit with a baseline recorded before the change.
- **Cook coupling.** `SkirtBoneSpring.cs` is a hashed cook dependency of Nyx's package. Editing it makes the open Editor
  recook `content-cooked/nyx/` (manifest and bindings), so the script and that recook must land together.

## Gunslinger-flip poncho (2026-11-05, stage 1)

The owner picked how he wears it: the long side across his shoulders over both upper arms, the right side thrown back
over his right shoulder so his finger-gun arm is free (option B of `tmp/salamander-identity/poncho-wear/`). Stage 1 is
the shape only. Springs, the Unity candidate and videos follow the owner's approval.

```bash
B="systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1"
IDLE=artifacts/genra-runs/20261027-144132-salamander-idle-showdown-r2/usd/salamander-idle-showdown.usda
OUT=tmp/dusty-poncho/b-flip-v1
$B --python tools/blender/salamander_rig/flip_poncho.py -- \
  client/Unity/Assets/Art/Characters/salamander/Models/salamander.fbx $IDLE $OUT ['<json options>']
$B $OUT/poncho_flip.blend --python tools/blender/salamander_rig/flip_poncho_review.py -- stills $OUT res=640
python3 tools/blender/salamander_rig/flip_poncho_review.py sheet $OUT
for c in idle g1-haymaker ge-copter-live ge-copter-overhead run; do  # one clip per process
  $B $OUT/poncho_flip.blend --python tools/blender/salamander_rig/flip_poncho_review.py -- clips $OUT $OUT/clips-spec.json clips=$c
done
```

- **Pose.** The blanket is draped on his idle, frame 0 of the GenRA idle USD (`salamander_idle.anim` is Humanoid
  muscle curves, which Blender cannot read). `clip_pose.py` poses the FBX from clip USDs. The result goes back to the
  T-pose rest through `Spine2` (round trip within 1e-4 m), so it sits on lowered arms the way he stands in game and
  carries no T-pose shelf.
- **Pattern.** One quad sheet, 68 cm across and 62.5 cm front to back, 2 cm quads (1086 vertices, 990 quads), its
  planar `Blanket` UV the unfolded pattern. The neck hole is 2 cm toward his left so the left side ends at his elbow.
  A slit runs up his right front, 12 cm right of the neck, from the front hem to the shoulder line, and the block
  right of it is 5 cm shorter. The build asserts one piece, manifold, and two boundary loops (the hem with the slit,
  and the neck hole).
- **Why the slit.** Fabric that stays flat cannot keep a straight front hem and also reach his back. Throwing an uncut
  corner or edge over the shoulder (a hook, or pin goals carried over it) bunched the fabric behind his arm or lifted
  the front panel into a sash. Uncut paper folds laid a lapel across his chest and left his arm covered. With the
  slit, the block in front of his right arm folds back across the shoulder line onto the fabric behind it, like
  paper: the front panel's right edge hangs straight, and the flap's striped hem lands on his upper back. The trim
  keeps that hem clear of the back hem's stripes.
- **Drape.** The drop is a cloth fall onto his posed body without the head, no pins. The fold places each flap
  vertex on the drape behind it, lifted 1.2 cm. A 60-frame settle (collar pinned, flap held at 0.2, the rest at 0.5)
  rounds the crease and resolves contact. The drop is sensitive to the pattern (a 2 cm change can wrap the right side
  over the arm instead), but a given input rebuilds identically.
- **Output.** `poncho_flip.blend` holds the FBX's rig, body, jeans and hat, the new `Salamander_Poncho` (skinned to
  `Spine2` only, as a preview) and the live one hidden as `Salamander_Poncho_Tabard`, posed at the idle frame.
  `textures/Salamander_Poncho_Flip.png` puts two brown stripes at fixed distances from each region's own hem; the slit
  is a plain edge. `report.json` has clearance, hem heights and how much of each arm the front view sees.
- **Review.** `stills` renders front, three-quarter (the board's frame), his right side, back and gameplay for both
  ponchos, plus close-ups of the new one, and `sheet` composes them. `clips` measures fabric inside his skin, or inside
  the jeans treated as a shell round the hip and thigh bones (their normals are inconsistent and their rims open), and
  renders the worst frame.

## Known

- Everything he plays is Manki's, so he hunches like Manki; his hat, mustache and tail don't move.
- He has no portrait (the HUD shows an empty box), no sounds and no effects.
- The jeans are 11.5k vertices next to the body's 3.2k (Tripo's mesh, the subdivided seat and the copied stitches).
