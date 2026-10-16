# FightGuy rig rebuild

Turns a Mixamo auto-rig of a generated FightGuy model into the model behind
`Resources/Characters/FightGuy.prefab` (the catalog `_rig`). The model, texture and
exports stay local and ignored (`Assets/Art/Characters/fightguy/Models/fightguy.fbx`,
`Textures/FightGuy2_BaseColor.png`, `Materials/FightGuy2_Body.mat`); this folder
records how to rebuild them.

```bash
# 1. Bake the Mixamo T-pose take into the rest pose, add thumb chains, move the thumb skin
#    off the index chain and scale to the old rig's rest-pose hip height.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_rig/build_rig.py -- \
  fightguy_mixamo.fbx <old fightguy.fbx> <build_dir> FightGuy2
# 2. Paint 肉 on the back, the right wrap up to the elbow and a band-aid across the nose.
python3 tools/blender/fightguy_rig/paint_texture.py <build_dir> FightGuy2
# 3. Optional Blender review renders (front/back/face/arm/gameplay-distance).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_rig/review_renders.py -- \
  <build_dir>/FightGuy2_rig.blend <build_dir>/textures/FightGuy2_BaseColor.png <out_dir>
```

4. Edit `Build` in `unity_import.eval.cs`, then run it through the gateway
   (`eval_file`, absolute path). It replaces `fightguy.fbx` in place (GUID, prefab
   overrides and the catalog `_rig` stay valid), creates the material from the one the
   old model rendered with and sets its Base Color to 0.45 (the v2 texture is far lighter
   than the old one; 0.8 made him the brightest fighter by 2–3×), regenerates the Avatar
   skeleton from the new T-pose and
   re-applies the saved 34-bone mapping (body plus thumb/index). Compare the reported
   `before`/`after` `humanScale`. Importing the model persists a cook of every package;
   afterwards run `sloparena.character.verify --target fightguy` and
   `sloparena.character.roster.refresh --package-id fightguy`.

## Why the rig looks like this

- **Thumbs:** the Mixamo upload used index-only fingers, so the thumb skin rode the
  index chain. The new `Thumb1..4` joints copy the old rig's thumb layout as
  world-space offsets scaled by hand length (the two rigs' hand-bone rolls differ, so
  hand-frame offsets landed 1–3 cm off), then snap the MCP/IP/tip joints to the
  thumb's cross-section centroids. Thumb weights fade into the palm over 2.5 cm of
  surface distance from the free thumb. If you re-rig in Mixamo, a skeleton option
  with full fingers or thumbs avoids this step.
- **Fingers:** all four fingers stay on the index chain, as on the old rig (Humanoid
  maps thumb and index only), so they curl together.
- **Size:** matching the old T-pose hip height (0.887 m) provides the initial size.
  Unity derives `humanScale` from the full skeleton; it changed from 0.984793 to
  0.938666 on this replacement despite the matching hip height. Check actual bound
  poses and foot contact rather than assuming the old alignment carries over.
  The proportions differ (shorter torso, upper arms and shins), so cooked poses and
  hurtboxes change. The user accepted those changes and deferred combat tuning.
- **Texture edits:** each decal is a 3D projection rasterized in UV space and
  limited by the colour underneath (vest blue for the kanji, skin for the wrap and
  band-aid), so nothing spills onto the hair, belt or eyes.
