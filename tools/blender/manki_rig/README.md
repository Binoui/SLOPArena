# Manki rig rebuild

Turns a Mixamo-rigged Manki FBX into the rig used by
`CharacterPackages/manki/Presentation/MankiRig.prefab`. The model, texture and FBX
exports stay local and ignored (`Assets/Art/Characters/manki/Models/Manki2.fbx`,
`Textures/Manki2_BaseColor.png`, `Materials/Manki2_Body.mat`); this folder records how
to rebuild them. The previous rigs are kept under `Presentation/Legacy/`.

```bash
# 1. Weld the mesh, smooth it, build a 5-bone tail chain from the tail geometry and keep
#    4 influences per vertex.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/fix_rig.py -- \
  manki_mixamo.fbx /tmp/manki-rig-fixed.fbx
# 2. Unity export: rename the armature Manki_Rig, add the deformation controls, skinned
#    finger copies and Tail1..Tail5, and scale ×1.18465 so the Humanoid scale matches
#    the gameplay tuning (humanScale 0.6194, 1.32 m tall).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/export_unity_rig.py -- \
  /tmp/manki-rig-fixed.fbx /tmp/manki-rig-export Manki2 1.18465
```

3. Run `unity_import.eval.cs` through the gateway (`eval_file`, absolute path). It
   replaces the model, regenerates the Avatar skeleton, re-applies the 46-bone mapping
   (body plus thumb/index/middle/ring), and re-adds the finger `RotationConstraint`s
   and the tail `BoneChainSpring`. Check that `humanScale` is unchanged. Importing the
   model persists a Manki cook; afterwards run `sloparena.character.verify` and expect
   `valid`.
4. Use `finger_bake.eval.cs` only after re-authoring finger poses in air 3 or the
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
