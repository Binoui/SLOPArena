# Salamander rig build

Fighter #6, Dusty the tiger-salamander gunslinger (package ID `salamander`, `CharacterClass.Salamander` = 7). Since
2026-11-05 his live model, `client/Unity/Assets/Art/Characters/salamander/Models/salamander.fbx`, is built from the
owner's Tripo v2 FBX by `build_v2.py`: a cropped leather vest over a cream shirt, an orange neckerchief, belt, buckle and
a coiled lasso on his right hip, blue jeans, big boots, a wide hat with an orange band, a curly mustache, a wheat straw,
a tail and four-fingered hands with a thumb. No poncho. The Tripo source is kept locally (ignored) at
`artifacts/salamander-rig-inputs/tripo-v2-20261105.fbx`; the build and the review are in
`artifacts/salamander-model-v2/` and `tmp/dusty-model-v2/`.

```bash
B="systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1"
IN=artifacts/salamander-rig-inputs/tripo-v2-20261105.fbx
PONCHO=artifacts/salamander-model-v2/backup/Models/salamander.fbx   # read only: hip height, bone contract, frames
OUT=artifacts/salamander-model-v2/build
# 1. Build: salamander.fbx, textures/Salamander_Body.png, salamander_rig.blend, report.json, pieces.json.
#    Pass the poncho model it replaced, never the installed v2: that one already carries the size factor.
$B --python tools/blender/salamander_rig/build_v2.py -- $IN $PONCHO $OUT [size=1.25] [spurs=1] [hat_subdiv=1] [lasso_hips=0.75]
# 2. Review against the model it replaces, under his real clips (one clip per process; see review_v2.py's docstring).
$B --python tools/blender/salamander_rig/review_v2.py -- <old.fbx> <old textures dir> $OUT/salamander.fbx \
  $OUT/textures/Salamander_Body.png <out_dir> stills clip=<genra run>/usd/<clip>.usda name=<clip> frames=12,20 views=three,game
#    modes: rest, stills, seq (video frames), hands (rhand / bothhands close-ups), metrics (edge stretch per piece)
# 3. Install, re-express his clips' prop and tail curves, cook (one owned gateway batch).
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch <batch.json>
#    commands: eval_file install_v2.eval.cs, eval_file retarget_clips.eval.cs, sloparena.character.inspect / cook /
#    verify --target salamander (each eval with --timeout 600000)
# 4. Refit the hurtbox capsules to the new mesh, and rebuild the GenRA rig packs.
python3 tools/character_sizes/hurtboxes.py fit --packages salamander
artifacts/genra-20261014/.venv/bin/python tools/genra/unity_rigpack.py all salamander
artifacts/genra-20261014/.venv/bin/python tools/genra/unity_rigpack.py all salamander --label moves \
  --reference showdown=anim.salamander.idle --reference shot=anim.salamander.ga
```

`build_v2.py` re-imports its export and fails (renaming it `salamander.REJECTED.fbx`) unless every live bone is present
with its parent, nothing moved, no vertex has more than four influences and none is unweighted. Always import FBX with
`ignore_leaf_bones=False`: the leaf bones (`HeadTop_End`, `*4`, `Toe_End`) carry the chain ends.

## Why the v2 rig looks like this

- **Size.** Tripo writes Z-up data into a Y-up file (his height runs along -Y in Blender). The build turns him upright and
  scales him (0.8218) so the Hips joint sits at the poncho rig's 0.434 m; every fitting length in the build (finger
  split, knuckles, jaw band, tail, props) is tuned in that space. His proportions differ (long legs, a short torso), so
  at that scale his idle head stood at 1.063 m in game against FightGuy's 1.328 m. The last step scales the finished rig
  and mesh by `size=1.25` (owner, 2026-11-05: his old height): overall 1.0273, Hips joint 0.542 m, head joint 0.776 m,
  skull top 0.924 m, hat top 1.026 m, in game ×1.8 (`visualScale`); idle head 1.329 m, hips 0.917 m
  (`hipHeight` 0.92), humanScale 0.5339. The poncho Dusty's idle head was 1.385 m.
- **Palms down.** Tripo's T-pose has thumbs up and palms forward; the live rig's has palms down. Unity Humanoid takes the
  rest as every muscle's zero, so his clips would roll both hands about 90 degrees (Unity test on the oriented Tripo rig:
  palm normals 83-161 degrees off the live rig's in idle, Finger Guns, Big Iron and the Haymaker). The build twists each
  forearm gradually, none at the elbow and all of it (90.2 degrees) at the wrist, and turns the hand rigidly. It edits
  the rest mesh directly; nothing is solved back through the skinning, so the bind pose is the rest.
- **Fingers.** Tripo's hand is a mitten on one Index chain. Beyond the web (6 cm from the wrist joint) the hand splits
  into four finger pieces; each gets a chain through its slice centroids, MCP 8 mm behind the web, joints at 42 and 72
  percent of the finger. Index, Middle and Ring keep the live names and the Humanoid mapping (46 bones, as before). The
  new `mixamorig:<Side>HandPinky1-4` are unmapped: his clips write the Humanoid Little finger as 0 everywhere, which
  would leave the pinky half curled in fists and finger guns. Instead `SalamanderRig.prefab` gives Pinky1-3 a
  RotationConstraint on Ring1-3. The pinky chain runs along the ring finger's direction, so their rest frames are equal
  and the constraint needs no offset. Palm-to-finger, finger-to-finger and joint blends are smooth; the thumb keeps
  Tripo's weights.
- **Tail.** A five-bone chain `Tail1-5` under Hips along the tail piece's centreline (geodesic bands from its open root
  ring), smooth blends between bone centres, the first 12 percent easing in from Hips. Tripo had it on the right thigh.
- **Props and frames.** Hat (crown centre, 3 cm under the skull top), Mustache (its back centre) and Straw (its mouth
  end) are rigid on their own bones under Head; the hat band rides the hat, the eyes, lids, brows and nostrils ride Head.
  Head and Hips keep the live rig's rest frames, and Hat and Mustache its rest rotations.
- **Spurs.** A steel spur on the back of each boot heel (`spurs=1`): a band round the heel 5.2 cm up (before `size`), a
  hexagonal shank tilted 15 degrees down and a six-point star rowel with a low pyramid on each face, all flat shaded.
  Each spur is weighted fully to its own bone, `LeftSpur` / `RightSpur`, a child of the foot bone that no clip animates,
  so it is rigid on the foot. The bone name keeps it out of the hurtbox fit (`hurtbox_fit.eval.cs` treats spur bones as
  cosmetic, like the hat); weighted to the foot itself, the spurs grew the foot capsules 1.61x instead of 1.25x. The
  one body material stays: the build paints three steel tones into the largest square of the atlas no UV triangle
  touches (u 0.879, v 0.950, 84 px at 4K) and collapses each part's UVs onto one point of its tone, so mipmaps never
  bleed the neighbouring atlas in.
- **Clip curves.** His own clips carry Hat, Mustache, Straw and Tail1-5 as generic local transform curves holding the live
  rest values. `retarget_clips.eval.cs` gives each the live bone's rotation change relative to Head or Hips applied to
  its new rest, and its position change added to its new rest position. Hat and Mustache rotations come out unchanged;
  only their pivots move. It finds the rig each clip was last written for from the first keys of its position curves:
  `artifacts/salamander-model-v2/old_rest_locals.txt` holds the poncho rig the clips were authored on, and the install
  writes the rig it replaces to `artifacts/salamander-model-v2/rests/rig-tail2-<length>.txt` every time. Clips already
  on the new rig are skipped; the size pass chained from the v2 curves exactly (rotations unchanged, positions moved by
  up to 2.35 cm). Re-applying each tail bone's change in its own axes swung
  the curled tail across his hips in Tail Whip; the Hips-relative change follows the live tail.
- **Torso and face weights.** Tripo jumped from Hips to Spine2 in a thin band at the jacket hem and gave the snout and
  face to Neck (the all-Head eyes would slide off his face). The spine family's share of each vertex is re-split by a
  smooth function of height, the same for the skin and every layer over it (shirt, vest, collar, neckerchief, jeans);
  shoulder, arm and leg shares stay Tripo's. Neck goes to Head across the jaw line; the neckerchief and collar stop at
  Neck (on Head they were pulled onto his chin in the Uppercut).
- **Layers.** The belt copies the jeans weights under it and is smoothed along itself; loops, buckle, keeper, knee patch
  and sleeve cuffs take one averaged weight set each (per-vertex copies straddled the jeans' left/right split and
  stretched the keeper 9.5×). The lasso hangs from the belt as one rigid piece, 75 percent Hips and 25 percent right thigh.
- **Hat shading.** Tripo's custom normals equal the smooth vertex normals, so the faceted brim is the hat's low polygon
  count (242 vertices), not shading. `hat_subdiv=1` adds a smooth subdivision (1,088 vertices) that rounds the outline
  but rolls the brim's edge into a thick lip; the live build keeps the owner's geometry.

## His package

`client/Unity/Assets/CharacterPackages/salamander/` is a trusted built-in package (`CharacterPackageAuthoringService.ProfileFor`).

- `install_v2.eval.cs` replaces `salamander.fbx` in place (its GUID, `.meta` import settings and the catalog's `_rig`,
  `Presentation/SalamanderRig.prefab`, stay), regenerates the Avatar skeleton from the new T-pose with the saved mapping
  (humanScale 0.4864 → 0.4271 at the hip-matched size), puts the new map into `Textures/Salamander_Body.png` (its importer keeps the roster's
  2048 cap) under `Materials/Salamander_Body.mat` (URP Lit, smoothness 0.22, like the other bodies), makes the prefab
  render the one body mesh with it and adds the pinky constraints. It also removed the poncho and jeans assets. It saves
  only the assets it changes (no `AssetDatabase.SaveAssets()`). Since the size pass humanScale is 0.5339.
- `borrow_clips.eval.cs` and `bind_catalog.eval.cs` made his first package from Manki's clips (2026-10-26); several of
  those borrowed clips (`Animations/Borrowed/`) are still bound. `apply_kit.py`, `make_briefs.py`, `integrate_kit.sh`
  and `clips.json` build and bind his own moves.
- Importing the model cooks the package. After a model change run `sloparena.character.cook` and `verify`, then
  `tools/character_sizes/hurtboxes.py fit`: the capsule radii in `character.json` are fitted to the mesh by that tool,
  not by the cook.

## Review tools

- `review_v2.py` renders two FBXs side by side under GenRA clip USDs. The old model is posed exactly from the USD (its
  rig pack is that rig); the new one emulates Unity (Humanoid bones take the world rotation change and keep their own
  joint offsets, the generic bones follow the retargeted curves, the pinky its ring finger).
- `clip_review.py` and `clip_pose.py` drive one FBX with clip USDs and measure torso skin against the arms;
  `rest_review.py` renders the rest around a shoulder; `render_mats.py` renders any .blend with its materials.
- `skirt_spring_identity.eval.cs` checks Nyx's skirt springs bit for bit against a saved baseline.
  `client/Unity/Assets/Scripts/Runtime/Entities/SkirtBoneSpring.cs` is a hashed cook dependency of Nyx's package: editing
  it recooks `content-cooked/nyx/`.

The v1 build (2026-10-26 to 11-05: Tripo kit splitting and dressing, Mixamo upload, the jeans fit, every poncho pass,
`build_rig.py`, the forward-skinned rest fix `rebuild_rest.py` and `unity_import.eval.cs`) was removed with the model it
built; git history before 2026-11-05 keeps it.

## Known

- Many clips are still Manki's (`Animations/Borrowed/`); none of his clips were authored for these proportions. His
  raised arm passes through the wide, low brim in the Copter and the Uppercut, and the Copter's hand only reaches the
  brim's front.
