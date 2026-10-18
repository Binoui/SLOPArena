# Fighter model sheets

Neutral front, 3/4, back and face renders of each live fighter model and its props,
composed into the reference sheets in `docs/design/references/key-art/`. Image
generators get these sheets with every [key art prompt](../../../docs/design/key-art.md);
rebuild them after any model or texture change, or the art drifts back to the old model.

The inputs are the local, ignored models and textures under
`client/Unity/Assets/Art/Characters/`. Blender's lighting only approximates Unity's URP look.

```bash
A=client/Unity/Assets/Art/Characters
R=/tmp/model-sheet-renders
render() { blender -b --factory-startup --python-exit-code 1 \
  --python tools/blender/model_sheet/render_views.py -- "$1" "$2" "$R/$3"; }
render $A/fightguy/Models/fightguy.fbx $A/fightguy/Textures/FightGuy2_BaseColor.png fightguy
render $A/manki/Models/Manki2.fbx $A/manki/Textures/Manki2_BaseColor.png manki
render $A/manki/Models/bazooka.fbx $A/manki/Textures/bazooka.jpg manki_bazooka
render $A/manki/Models/bomb.fbx $A/manki/Textures/bomb.jpg manki_bomb
render $A/manki/Models/aerosol.glb - manki_aerosol
render $A/wibou/Models/wibou.fbx $A/wibou/Textures/Baked_BaseColor.png wibou
render $A/wibou/Models/katana.fbx grey wibou_katana
render $A/wibou/Models/kunai.fbx - wibou_kunai
render $A/bonk/Models/bonk.fbx $A/bonk/Textures/Color.jpg bonk
render $A/bonk/Models/bonk_sword.fbx $A/bonk/Textures/bonk_sword.jpg bonk_sword
python3 tools/blender/model_sheet/compose.py "$R" docs/design/references/key-art
```

`Manki2.fbx` holds the live Manki v3 (the name predates v3; see
[manki_rig](../manki_rig/README.md)). The katana's texture is missing, so it renders as
flat grey for its shape only.
