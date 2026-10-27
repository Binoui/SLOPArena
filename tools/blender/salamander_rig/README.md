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
- **T-pose rest by inverse skinning:** the download's bone rest is the upload A-pose and its take is a T-pose. The
  mesh is solved back into the T-pose so posing the skeleton in the A-pose reproduces the model (within 3 µm); a
  forward bake pulls blended skin toward the joints.
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

## Known

- Everything he plays is Manki's, so he hunches like Manki; his hat, mustache and tail don't move.
- He has no portrait (the HUD shows an empty box), no sounds and no effects.
- The jeans are 11.5k vertices next to the body's 3.2k (Tripo's mesh, the subdivided seat and the copied stitches).
