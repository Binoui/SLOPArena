# Wibou coat

Puts the owner's Tripo coat on Wibou as an extra skinned renderer in
`Resources/Characters/Wibou.prefab`. Wibou's own model (`wibou.fbx`), Avatar, clips and
cooked poses are untouched: the coat is presentation only, so a recook changes only
Wibou's `client.bindings`, `manifest.json` and the roster pin, never `poses.bin`.

The local art stays ignored: `Art/Characters/wibou/Models/wibou_coat.fbx`,
`Textures/WibouCoat_BaseColor.png` and `Materials/WibouCoat.mat`. The Tripo source of
2026-10-21 is kept in `artifacts/wibou-coat-20261021/source/`. Shipping the coat needs
those three files force-added like `wibou.fbx`; check with
`python3 tools/check_asset_closure.py client/Unity/Assets/Resources/Characters/Wibou.prefab`.

```bash
# 1. Fit, skin and export (about 10 s). Writes WibouCoat.fbx, the colour map, previews and build-report.json.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_coat/build_coat.py -- \
  artifacts/wibou-coat-20261021/source/tripo/tripo_convert_e0a42b7e-2078-41a9-845f-09c99b252e92.fbx \
  client/Unity/Assets/Art/Characters/wibou/Models/wibou.fbx /tmp/wibou-coat-build
# 2. Bind it in Unity through your own gateway lease (absolute path; Src* point at step 1's output).
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  eval_file --file "$PWD/tools/blender/wibou_coat/unity_import.eval.cs" --timeout 120000
```

Saving the prefab makes the cook postprocessor persist a cook of the whole roster. Then
run `sloparena.character.verify` for every package and expect `valid`.

To see it on real clips, render bound clips on the prefab with the GenRA toolkit. Any
Wibou brief frames the views; springs are stepped per tick:

```bash
cd tools && ../artifacts/genra-20261014/.venv/bin/python genra/unity_clip.py render \
  --brief genra/briefs/wibou-r-charge-loop-b.json --clip run=anim.wibou.run --travel 0.12 --margin 1.8
```

## Why it looks like this

- **One coat out of three.** Tripo turned the turnaround sheet into three coats side by
  side (front, side and back views). Only the back view (`--pick R`) has a closed back
  and a vent; it faces +Y and is turned to face -Y. The side view's collar ring is broken.
- **Fit.** Scale 1.65 and offset (0, 0.095, -0.085) m in Blender's bind frame put the
  coat on his shoulders with the collar framing his jaw and the hem 17 cm below the hip
  joint. The sleeves are turned about the shoulder joints onto the A-pose arms (left
  -12.3°, right +3.6°). Eight rounds of a smoothed push keep the cloth 12 mm off the skin;
  the push spreads over 4 cm so both cloth layers move together. Pushing a single
  vertex made jagged cuffs.
- **Hair over the collar.** Above the neck only surfaces within 7.5 cm of the neck axis
  count as skin. His long hair is allowed to fall over the collar instead of pushing it
  out, which spiked the collar's back.
- **Weights** come from the nearest body surface, then:
  - head bones are dropped;
  - sleeves keep only their own upper arm, never the forearm or hand;
  - the collar follows Spine2 and Neck, not the head;
  - the skirt below the belt hangs from four sway chains under Hips (`Coat_FL/BL/BR/FR`,
    two bones and a tip each) and follows the thigh on its side by 25%;
  - three smoothing passes, at most four influences.
- **Sway** is the existing presentation-only `BoneChainSpring`, one per chain on its
  first bone, with the default tuning. It runs in Play Mode and in the GenRA render's
  per-tick stepping; edit-mode previews and the pose bake keep the rest pose.
- **Bind poses.** `wibou.fbx` carries a Mixamo take whose first frame lifts the hips
  about 1 m, so the prefab's default pose is not the bind pose. The build clears the take
  and exports in the bind pose. `unity_import.eval.cs` proves that the coat's bind poses
  equal the body's for every shared bone (0.00085 on 2026-10-21; a 0.02° mesh-node
  rounding) before binding, and refuses otherwise.
- **Rig import:** Generic with no Avatar. `None` imports the coat as an unskinned
  `MeshRenderer`.
- **Material:** a copy of the body's URP/Lit material with the coat's colour map. In
  matches `MatchVisualStyle` swaps every `SkinnedMeshRenderer` to `GraphicCharacter`, and
  `FighterHitFeel` flashes it, so both include the coat with no code change.
- **Shared Editor:** the import saves only its own material
  (`AssetDatabase.SaveAssetIfDirty`). Its first run called `AssetDatabase.SaveAssets()`,
  which also flushed another thread's unsaved `SirBonkPlank.mat` and recooked Bonk.

## Cost

The coat adds 13,204 triangles (Unity: 9,485 vertices) to Wibou's 19,709, plus one
renderer and material and a 2048² DXT1 colour map.
