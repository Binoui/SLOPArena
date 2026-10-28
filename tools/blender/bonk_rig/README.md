# Sir Bonk rig build

Turns a Tripo auto-rig of the generated Sir Bonk model into a Unity Humanoid rig. It writes the
candidate prefab `CharacterPackages/bonk/Presentation/SirBonkRig.prefab`. The model, texture and material
stay local, beside the live Bonk's: `Art/Characters/bonk/Models/sir_bonk.fbx`,
`Textures/SirBonk_BaseColor.png` and `Materials/SirBonk_Body.mat`. The live Bonk rig
(`Resources/Characters/bonk.prefab`, the catalog `_rig`) is untouched until the swap, so building
the candidate cooks nothing.

```bash
# 1. Build: delete the back stick, set the weights, lengthen the arms, bake the T-pose,
#    add the cape chains, rename to Mixamo names, and set the size (bucket top = 0.9 x the old Bonk).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bonk_rig/build_rig.py -- \
  <tripo.fbx> client/Unity/Assets/Art/Characters/bonk/Models/bonk.fbx <build_dir> SirBonk 1.15 0.9
# 2. Optional Blender review: turnaround, raised arms, overhead swing, kick, squat, twist, nod.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bonk_rig/review_renders.py -- \
  <build_dir>/SirBonk_rig.blend <out_dir>
```

3. Edit `Build` in `unity_import.eval.cs`, then run it through the gateway (`eval_file`, absolute
   path). It imports the model, texture and material, builds the Humanoid avatar from this T-pose,
   applies the live Bonk's mapping plus UpperChest = Spine2, and writes the candidate prefab with
   three cape `BoneChainSpring`s on Spine2. It compares `humanScale` and the right-hand frame with
   the live rig.
4. `candidate_review.eval.cs` renders the live Bonk and the candidate side by side in Bonk's bound clips,
   placed like `PlayerRenderer`. Output goes to `tmp/bonk-sir/unity-review/<time>/`. It reports the lowest,
   top, head and hips heights per frame.

5. `plank_import.eval.cs` turns the Tripo plank into `Art/Characters/bonk/Prefabs/sir_bonk_plank.prefab`
   and points the candidate weapon config at it.
   - The root keeps the old sword's frame, so the converted offsets apply.
   - The red grip sits in the GenRA idle's closed fist, along the old blade direction.
   - It is scaled so the fist-to-tip reach equals the old sword's (1.85 m). The plank's long handle
     would otherwise change every move's reach.
6. `swap_live.eval.cs` makes the candidate the live Bonk:
   - the catalog `_rig` becomes the candidate prefab;
   - anim.idle and anim.run bind the promoted package copies of the accepted GenRA clips;
   - the live weapon config gets the candidate's entries.

   Saving the catalog persists a cook of every package. Back up `CharacterPackages/bonk`,
   `content-cooked/` and `Resources/Generated/` first. Then run `sloparena.character.verify --target bonk`
   and `sloparena.character.roster.refresh --package-id bonk`. The 2026-10-20 swap's backups are in
   `artifacts/sir-bonk-ingame-20261020/backup/`.
7. `horse_review.eval.cs` imports the Tripo hobby horse for the Joust (2026-10-21) as
   `sir_bonk_hobby_horse.fbx`, `SirBonkHobbyHorse_BaseColor.png` and `SirBonkHobbyHorse.mat` (the plank's
   shader). It then renders size options ridden by the live Sir Bonk in his idle and run, with the stick
   through his crotch at the model's own angle (about 49°, so the head stays level). Output goes to
   `tmp/bonk-kit/horse/unity-review/<time>/`. Nothing the live Bonk uses changes.
   - The stick is found from the mesh: the longest thin run of a width profile. The head's up is the model's
     up, because Tripo props come upright.
   - The match camera orbits behind him at 15°, so check the behind views. From there the horse hides behind
     his body and cape unless its head pokes out.
   - It also writes `first-<view>.png`: 1280×720 transparent first frames of the chosen horse for Seedance
     references. `tools/genra/video_ref.py studio` puts them on the studio floor.
8. The pot lid for Q (2026-10-22): his left pauldron becomes a prop, so it can leave his shoulder.
   - `lid_split.py <build_dir>/SirBonk_rig.blend <out_dir>` lifts the five islands that ride only his left shoulder and
     upper arm (the dome and its fittings) out of the build. It re-exports the body with `build_rig.py`'s settings and
     the lid alone at its place in the model. A control re-export of the untouched build matches the live FBX
     structurally (bones, vertices, weights). FBX bytes differ by their timestamp.
   - `lid_import.eval.cs` imports the lid while the live body still has it. It checks that the lid lands on the body's
     own lid vertices (within a micron in the bind pose), and writes `Art/Characters/bonk/Prefabs/sir_bonk_pot_lid.prefab`:
     a lid-centred prop with +Y the dome's face, +Z the crown's up, and anchors `lidCentre`, `lidOut`, `lidCrownUp`. It
     reports the WeaponAttach offset on `mixamorig:LeftArm` that puts it back. A one-object FBX imports as that object,
     so keep the lid root's own axis and unit conversion (270° on x, ×100) under a holder at the body's FBX root.
   - `lid_swap.eval.cs` renders his idle before, copies the split body over `sir_bonk.fbx`, and adds the lid to
     `BonkWeaponAttachConfig` (slot 0). It checks the avatar (same humanScale, 2,431 fewer vertices), then renders
     after and placement options (`tmp/bonk-q/lid-swap/<time>/`). Backups are in
     `artifacts/sir-bonk-q-20261022/backup/before-lid-split/`.
   - `lid_hand.eval.cs` puts the owner's placement B on the live config and writes the pack-only
     `SirBonkLidWeaponAttachConfig.asset` for the rig pack `bonk-unity-sirlid`. It holds the plank, the lid in his left
     hand (a frisbee grip: centre on the palm side, face along the thumb), and a copy of the shoulder lid whose anchors are
     renamed `shoulderLid*` (`Art/Experiments/GenRA/bonk/`). A brief target can then make the two lids meet at the grab.

- **Rendering clips in an eval:** pose a fresh rig instance (PlayableGraph `Evaluate`) for each frame *before*
  `PreviewRenderUtility.AddSingleGO`. On 2026-10-22, Q's key poses were posed after the rig had moved into the preview
  scene: his left arm kept the idle (fist on the hip) while the plank arm followed the clip, so the wrong hand seemed to
  grab the lid. Check a rendered frame against the clip's bone positions before showing it.

## Sir Bonk v2 (2026-10-28): Tripo kit + Mixamo

v2 replaces the v1 Tripo auto-rig. The owner generated concept take 3 ("Junk Knight") with Tripo's character extraction,
which returns a kit: a bare body and every costume part beside it, each at its own scale. The owner rigged the body in
Mixamo. The scripts are in `v2/`; the 2026-10-28 inputs and outputs are under `tmp/bonk-sir/tripo-v2/`. The kit
coordinates in `split.py` and the part targets in `assemble.py` are this kit's.

```bash
B="blender -b --factory-startup --python-exit-code 1"
$B --python v2/split.py -- <kit.fbx> <kit_rgb.png> <parts>/parts.blend          # one object per part, glitches dropped
blender -b <parts>/parts.blend --python v2/frame_body.py -- <parts>/framed.blend  # body turned square (it was yawed 25°)
blender -b --python-exit-code 1 <parts>/framed.blend --python v2/assemble.py -- <dress_dir>   # fit every part
blender -b --python-exit-code 1 <dress_dir>/dressed.blend --python v2/apose.py -- <apose_dir> # arms 35° up for Mixamo
blender -b --python-exit-code 1 <apose_dir>/apose.blend --python v2/export_mixamo.py -- <kit_rgb.png> <upload_dir>
#  -> the owner rigs <upload_dir>/sir-bonk-v2-mixamo.fbx (body + boots) in Mixamo, Original Pose
$B --python v2/swap_mesh.py -- <mixamo.fbx> <sent apose.blend> <new apose.blend> <rigged_dir>  # reshape, keep weights
$B --python v2/build_rig.py -- <rigged_dir>/sir-bonk-v2-rigged.blend <apose_dir>/apose.blend \
  client/Unity/Assets/Art/Characters/bonk/Models/bonk.fbx <kit_rgb.png> <build_dir>
LID_ISLANDS=2 $B --python lid_split.py -- <build_dir>/SirBonk_rig.blend <lid_dir>   # Q's pot lid as its own prop
```

Then, through the gateway (`eval_file`, absolute paths), in this order:
1. `v2_import.eval.cs` with `Build` = `<build_dir>`: candidate `Art/Characters/bonk/Prefabs/SirBonkV2Rig.prefab`.
2. `v2_lid_import.eval.cs`: the lid prop `sir_bonk_v2_pot_lid.prefab` and its offset on `mixamorig:LeftArm`.
3. `v2_import.eval.cs` again with `Build` = `<lid_dir>`: the body without the lid.
4. `v2_review.eval.cs`: the live rig and the candidate side by side in Bonk's bound clips.
5. `v2_swap.eval.cs`: back up first (it cooks every package). It:
   - makes v2 the catalog `_rig`;
   - puts the v2 lid in the weapon config and in the thrown lid (`BonkPotLidProjectile`);
   - strips `Cape_*` curves from the bound clips.
6. `sloparena.character.inspect --target bonk`, then the Bonk kit tests.
7. Rebuild the GenRA rig pack: `unity_rigpack.py all bonk --label sirv2 --prefab
   Assets/CharacterPackages/bonk/Presentation/SirBonkV2Rig.prefab --weapon-config
   Assets/CharacterPackages/bonk/BonkWeaponAttachConfig.asset`.

What the kit needed:
- **Glitches and caps:** Tripo added an extra head and a long grey stick. It also capped every tube end: the wraps
  are closed solids, and the pants and boot shafts are capped. `assemble.py` cuts the caps.
- **Knee guards:** the kit's knee guards carry a mirrored back pad, which is dropped.
- **Bucket handle:** it is its own part. Its loop under the chin pushed the bucket off the head.
- **Collision tests:** signed distance to thin cloth layers is unreliable, so solid parts test against the body only,
  with a bigger gap.
- **Hidden body:** the body is trimmed below the boot cuffs, so no foot or shin pokes through.
- **A-pose:** the body's arms hung about 1 cm from his sides. Raise them with a harmonic blend over the shoulder,
  with the arm and deltoid rigid. A piecewise blend left a ledge under the biceps, which the owner saw in Mixamo.
  The upload is body, boot_a, boot_b joined, so `swap_mesh.py` can reshape the rigged mesh by vertex index.
- **Mixamo rig:** it has index fingers only. `v2_import.eval.cs` allows the live mapping's other finger bones to be missing.
- **Cape keys:** clips must not key spring chains. BoneChainSpring takes its rest from the first animated frame, so
  the v1 clips' `Cape_*` keys flipped v2's cape. `tools/genra/unity/write_clip.cs.template` now skips every
  BoneChainSpring bone.

## Why the rig looks like this

- **Back stick:** the generator turned a frying-pan pauldron's handle into a stick strapped across
  his back. Its islands are deleted. It was also painted onto the tabard back and the cape, so step 8
  repaints dark texels behind it, as seen from the back, with clean cloth beside it.
- **Weights:** Tripo spread hard pieces over two or three bones. The fixes:
  - The bucket, handle, crest and eyes go to the head.
  - The vambraces go to the forearm, and the spurs and buckles to the foot.
  - Pauldrons and knee guards keep a fixed blend with the parent bone.
  - The book rides 60% on the hips.
  - The tabard skirt and sash tail hang from the hips and follow the thighs only toward the hem: 75%
    at the hem. At 55%, kicks pushed the thigh through the front flap.
  - The cape was weighted to the arms and thighs, so it lifted like wings. It now hangs from the upper
    chest on 3×4 chain bones (`Cape_L/C/R1..4`) for the spring.
- **Arms:** Sir Bonk's arms read short. The upper arm and forearm are 15% longer (owner pick, 2026-10-20).
  Skin stretches. Vambraces, pauldrons and the arm knot keep their size and slide along the limb.
- **T-pose:** Tripo rests in an A-pose with the arms 22.6° down. Humanoid takes the rest as its
  T-pose, so the arms are baked horizontal.
- **Names:** Mixamo names, so `BonkWeaponAttachConfig` (`mixamorig:RightHand`) and old clip paths
  resolve. Bone frames differ from the old rig's: the right hand is 110° off. Convert per-bone offsets
  in Unity so the old T-pose grip is kept in world space. `candidate_review.eval.cs` does this and reports
  the converted sword offsets.
- **Size:** baked into bones and vertices, not the armature object, so Unity bones keep unit scale.
  An object scale left the hand at lossy scale 0.70, and the sword offset (`bone.TransformPoint`)
  shrank with it.

## Pitfalls met here

- Scaling edit bones in place double-scales connected joints, because a child's head is its parent's
  tail. That moved one shoulder joint into the hips. Release `use_connect`, scale from recorded joints,
  then restore the flags. `candidate_review` showed noodle arms, and the bindpose check in Unity
  (`bone.localToWorldMatrix * bindpose == mesh root`) found the two bad bones.
- Tripo's material holds the colour map twice: two image nodes and two datablocks on one file. Saving
  both overwrote the repainted one, so keep one datablock.
- The gateway serializes plain values only. A `Quaternion` in the returned dictionary turned the whole
  result into its type name, so return rotations as `float[4]`.
