# Manki rig rebuild

Turns a Mixamo-rigged Manki FBX into the rig used by
`CharacterPackages/manki/Presentation/MankiRig.prefab`. The model, texture and FBX
exports stay local and ignored (`Assets/Art/Characters/manki/Models/Manki2.fbx`,
`Textures/Manki2_BaseColor.png`, `Materials/Manki2_Body.mat`; the names predate v3 and
are kept so GUIDs do not change); this folder records how to rebuild them. The previous
rigs are kept under `Presentation/Legacy/`.

The live model is Manki v5 (2026-10-28), a Tripo model with worn props, repackaged with step 0
and rigged in Mixamo. Step 1 takes `rigid_parts/manki_v5.json` as a sixth argument: it pins the jetpack and
holster islands to one bone each and removes a stray shard. When Mixamo rigs fewer fingers than the hand
has (v5 got index chains only), step 1 rebuilds the thumb/index/middle/ring chains (`finger_chains.py`) and
`unity_import.eval.cs` maps the missing finger bones by their Mixamo names. Export scale 1.3834, no face paint. See
`docs/characters/manki.md`.

Manki v4 (2026-10-27) was a Tripo model that arrived already rigged in
Mixamo, so it starts at step 1. It also skips step 3 (its face is clean): the colour map is
the FBX's packed 4K map, downscaled to 2048. Export scale 1.3486 (humanScale 0.6194).
Its FBX differs from v3's in three ways, and `fix_rig.py` handles each one:

- It binds a centimetre mesh to a rest pose lying along −Y; only the Mixamo clip stands
  it up. fix_rig bakes the visible T-pose into the mesh and the rest pose, in metres.
- It has quads, which fix_rig triangulates.
- Its tail is a separate mesh island. `tail_segment.py` takes that island whole and
  gives it new weights, because the generated ones follow a thigh.

v3 (2026-10-16) followed the steps below. Mixamo rejected the generator's FBX (four
embedded PBR maps) and accepted the `mixamo_package.py` re-export, which matches the
Manki 2 upload: one triangulated mesh and a single colour map beside it. The exact
rejection cause was not isolated.

```bash
# 0. Repackage the generated model for Mixamo and keep its full-resolution colour map.
#    Upload out/manki_v3_mixamo.zip, auto-rig with fingers, download the FBX with skin.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/mixamo_package.py -- \
  manki_v3.fbx out manki_v3
python3 -c "from PIL import Image; Image.open('out/manki_v3_BaseColor_source.jpg').convert('RGB')\
.resize((2048, 2048), Image.LANCZOS).save('out/manki_v3_basecolor_2048.png')"
# 1. Weld the mesh, smooth it (180 = fully smooth, as v3 was generated), build a 5-bone
#    tail chain from the tail geometry, keep 4 influences per vertex and give the fur
#    tufts body-like normals (fur_normals.py). Manki 2 used the default 40-degree smoothing.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/fix_rig.py -- \
  manki_v3_mixamo.fbx /tmp/manki-rig-fixed.fbx 5 180 40
# 2. Unity export: rename the armature Manki_Rig, add the deformation controls, skinned
#    finger copies and Tail1..Tail5, and scale so the Humanoid scale matches the gameplay
#    tuning (humanScale 0.6194): 1.24873 for v3 (1.26 m), 1.18465 for Manki 2 (1.32 m).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/export_unity_rig.py -- \
  /tmp/manki-rig-fixed.fbx /tmp/manki-rig-export Manki2 1.24873
# 3. Repaint the face onto the 2048 colour map from step 0 with the model's face profile.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/face_paint.py -- \
  /tmp/manki-rig-export/Manki2.fbx out/manki_v3_basecolor_2048.png \
  /tmp/manki-rig-export/textures/Manki2_BaseColor.png \
  tools/blender/manki_rig/face_profiles/manki_v3.json 2048 skin /tmp/manki-rig-export/face-debug
```

4. Run `unity_import.eval.cs` through the gateway (`eval_file`, absolute path). It
   copies the colour map (raising its import size cap to 2048), replaces the model,
   regenerates the Avatar skeleton, re-applies the 46-bone mapping
   (body plus thumb/index/middle/ring), and re-adds the finger `RotationConstraint`s
   and the tail `BoneChainSpring`. Check that `humanScale` is 0.6194. Importing the
   model persists a Manki cook (new `poses.bin`, roster pin); afterwards run
   `sloparena.character.verify` and expect `valid`, then the Shared tests.
5. Use `finger_bake.eval.cs` only after re-authoring finger poses in air 3 or the
   head slam on the legacy rig. It bakes those poses into Humanoid finger muscles.
   Dry-run it first; it writes only when `write = true`.

## Why the rig looks like this

- **Two finger chains:** Humanoid retargeting resets any transform between two
  mapped bones, so `<Side>FistDeform` cannot sit between `Hand` and the mapped
  fingers.
  - The mapped `mixamorig:*Hand<Finger><n>` bones stay under the hand as unskinned
    anchors.
  - Skinned `<Side>Skin<Finger><n>` copies under the fist follow them through
    constraints.
  - Scaling the fist (air 3, up to 2.2×) inflates the palm and fingers together.
- **Arm controls:** `<Side>UpperArmStretch` and `<Side>ForearmOffset` carry all of
  the upper-arm and forearm skin, as on the legacy rig. Air 3's generic curves drive
  them through the `Manki_Rig/...` paths.
- **Tail names:** the tail bones are not `tail:N`. The legacy clips' tail curves
  assume the old tail's rest frames, so the new tail is driven only by the spring,
  which is presentation-only and runs in Play Mode.
- **Tail detection** (`tail_segment.py`): v3's tail ends in a club hanging next to the
  leg. The tip search keeps climbing away from the spine past the leg-adjacent club,
  and the root is where three successive rings are much fuller and wider than the
  shaft just before them. Manki 2's detected tail is unchanged (92 vertices).
- **Scale:** matching the Humanoid scale, not the height, keeps the feet on the floor:
  the Animator places the body relative to the renderer root (`modelYOffset`) in
  units of `humanScale`. At v3's full height (humanScale 0.642) its lowest vertex sat
  2.9 cm below the floor in Training; at 0.6194 it sits 0.7 cm below.

## Why the surface looks like this

- **Fur normals:** remeshed fur is a field of small spikes. The arena key light is
  almost straight down, so every spike facet facing down or sideways went dark and the
  fur read as dark speckled tips at gameplay distance. In Training, forcing sharper mips
  and disabling shadow receiving changed nothing; diffused, body-like normals on fur
  vertices (orange in the colour map) removed it. Only normals change: bones, weights
  and UVs stay identical. `fix_rig.py ... 5 <angle> 0` turns the step off.
- **Face paint:** the generators painted the face with dirt specks, a smeared mask and a
  winking lid in the mask's red, which read as a swollen eye. Their high-poly sources
  carry the same paint, so rebaking does not help. `face_paint.py` renders the head
  from the front, cleans skin and mask, redraws the mouth, lash line, nostrils and a
  catch-light, and projects the result back onto the visible texels.
- **Face profiles** (`face_profiles/*.json`): feature positions are hand-placed per
  model, in pixels of the profile's front camera; its centre, width and neck cut scale
  with the export. `brows: geometry` repaints the brow ridges (Manki 2's paint missed
  them); `brows: colour` keeps painted brows (v3). `keep_zones` leave paint untouched
  (v3's snout); `skin_zones` force skin (Manki 2's smudged snout). The tool aborts if
  the open eye is not where the profile expects. `lid=red` keeps a red winking lid.
- **Known gap:** the winking eye's slit is a hairline groove in the low-poly mesh; with
  backface culling it reads as part of the lash line.
