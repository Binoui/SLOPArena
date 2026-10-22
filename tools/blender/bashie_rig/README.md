# Bashie rig build

Turns the owner's Mixamo rig of the generated Bashie body into a Unity-ready rig, with her shell, whistle and cord as
separate pieces skinned rigidly to bones, and fills her `bashie` package. The design is in
[docs/characters/bashie.md](../../../docs/characters/bashie.md).

```bash
# 0. A generated model made from an opaque image gets a backdrop card: delete it before rigging (transparent
#    inputs avoid it), then rig the clean copy in Mixamo (T-pose download with skin).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/remove_card.py -- \
  <generated.fbx> <out_dir> bashie
# 1. Build: bake the Mixamo take into the rest pose, keep 4 influences, skin the shell, whistle and cord to bones.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/build_rig.py -- \
  <mixamo.fbx> <build_dir> shell=<shell.fbx> whistle=<whistle.fbx>
# 2. Review: weight shares per piece, plus arms up, overhead, kick, run, squat, twist and bend renders.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/review_renders.py -- \
  <build_dir>/Bashie.fbx <build_dir>/textures <out_dir>
# Optional: the joints drawn over a see-through body.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/joint_map.py -- \
  <rig.fbx> <basecolor.png> <out_dir>
```

The build writes `Bashie.fbx` (no embedded media), one colour map per piece in `textures/` (`Bashie_Body`, `_Shell`,
`_Whistle` and `_Cord`), `Bashie_rig.blend` and `Bashie_report.json`. Its options (shell fit, whistle size and
height, cord radius) are listed at the top of `build_rig.py`. The 2026-10-22 inputs and outputs are local, under
`tmp/bashie-model/` (`v4-raw/`, `rig-build-v4/`).

## Her package

`client/Unity/Assets/CharacterPackages/bashie/` (2026-10-22) is a trusted built-in package like the other four,
created with `sloparena.character.create`. Run each script as a gateway `eval_file` (absolute path):

1. `unity_import.eval.cs` imports the build: the model, textures and materials under `Assets/Art/Characters/bashie/`
   and the rig prefab as `Presentation/BashieRig.prefab`, which her catalog binds.
2. `borrow_clips.eval.cs` copies her own approved idle and run into `Animations/`, and every other clip Manki's
   catalog binds into `Animations/Borrowed/`, as a stand-in kit the owner asked for:
   - Played on her as they are, Manki's clips stand her 0.33-0.45 m under the floor. Standard proportions put her
     heavy-headed centre of mass too low.
   - So each copy bakes its height from the authored root ("Based Upon: Original"; most of his clips use the centre
     of mass, which ignores `RootT.y`) and gets root curves when it had none.
   - Its root is then shifted until her lowest point at frame 0 is 0.342 m higher than his clip left it, measured
     through Animancer as `PlayerRenderer` plays clips. Grounded moves then start within 4 cm of the floor.
   - Animation events are dropped, since they call Manki's components.
3. `character.json` is Manki's, with her own IDs (`anim.bashie.*`), size (`visualScale` 1.1, `modelYOffset` -0.75,
   `hipHeight` 0.34) and none of his effects: no `presentationIds`, `hitPresentationId` or `emitPresentation`, and
   blank `explosionPresentationId`s.
4. `bind_catalog.eval.cs` binds the rig and one clip per animation ID, with its own pose track. Then run
   `sloparena.character.inspect`, `cook` and `verify`, and `sloparena.character.roster.admit --package-id bashie
   --selector Bashie`.

Shipping follows the sources. Her model, materials, textures, idle and run are original and re-included in
`.gitignore`, as are her copies of Manki's project-authored clips (crouch, the shared tumble, g4, a3 and the two F
clips). Her copies of his vendor-pack clips stay local, like the originals.

In game, `sloparena.tutorial.open --fighter bashie` plays her as herself. The 30-second Pipeline command limit
covers a whole `tutorial.run`, so split long captures into runs of about 40 ticks:
- A step's `slot` presses on its first tick only. Holding a charge needs `isAiming: true`, and held inputs reset
  between runs, so keep a whole pump in one run (50 ticks fit).
- Lesson 9 (Combo, `sloparena.tutorial.lesson --index 9`) has a dummy and no cooldowns. `sloparena.tutorial.place --x
  2 --z -2 --yaw 90` puts her 5 m from it, facing it.

## Water blaster (Hose Down, A)

```bash
# The Tripo model (1.0 long, muzzle +X, tank up) -> a 0.44 m prop with its origin in her fist.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/build_blaster.py -- \
  <blaster.fbx> <build_dir>
```

Then run each script as a gateway `eval_file`:
- `blaster_import.eval.cs` imports the build: the model, texture and material under `Assets/Art/Characters/bashie/`,
  plus two package assets. `Presentation/BashieBlaster.prefab` has a `Muzzle` anchor at the barrel's tip.
  `Presentation/BashieWeaponAttachments.asset` holds one slot-A entry on her right hand, and her catalog binds it.
- `blaster_check.eval.cs` renders her holding it, at rest, on her idle and on the borrowed bazooka hold, into
  `tmp/bashie-model/blaster/check/`.

The grip is a pistol grip measured on her own hand bones. Her metacarpals point along the barrel, and her knuckles
(index to ring: her cartoon hand has four fingers) line up with the grip, which leans back 20°. Props spawn at
world scale 1 while her offsets follow her hand's scale, so the 0.44 m blaster stays about 40% of her 1.10 m height.
`bind_catalog.eval.cs` keeps the weapon config when it rebinds. The prefab's `Pump` (the pump grip, where her left hand
holds) and `Muzzle` anchors are the weapon entry's hilt and tip anchors, so the GenRA rig pack gives them joints:
`unity_rigpack.py all bashie --label hose --prop A` builds `bashie-unity-hose`.

Hose Down's GenRA clips (briefs `bashie-hose-pump.json` and `bashie-hose-spray.json`) are written to
`Assets/Art/Experiments/GenRA/bashie/`. `hose_clips.eval.cs` copies them into her package's `Animations/`, and
`bind_catalog.eval.cs` binds them, with her idle as the end clip until hers exists. Re-run both, then cook, after each
write the owner should see in game.

## Shell tuck (Shell Slide, R)

```bash
# Look A, the owner's pick (2026-10-22), from the front, side and match-camera angles. Add "frames" for a timeline,
# or "report": true for the tucked bone positions on Unity's model root.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/tuck_preview.py -- \
  "$PWD/tmp/bashie-model/rig-build-v4/Bashie_rig.blend" <out_dir> \
  '{"variants": {"A": {"shell": 1.7, "arm": 0.15, "leg": 0.3, "headScale": 0.8, "peek": [0, -0.33, -0.16], "whistle": 0.01}}}'
```

She tucks into her own shell without a new model: `ShellTuckRig` changes her bones after the clip, and
`tuck_preview.py` makes the same changes in Blender, so a look can be judged there first. Her body pitches prone with
the shell on top, the shell grows 1.7×, her upper arms and legs shrink into it, the whistle vanishes, and her head
(0.8×) is held upright where it peeks out of the shell's front. `ShellTuckPose.Bashie` carries look A's numbers from
the report, with her hips moved back so the shell is over her position.
- **Gotcha:** turning her neck to lift her face swings her big head back through the shell. Pin the head instead.
- **Gotcha:** prone, her shell bowl opens only underneath, so the head must sit in front of the shell's front edge to
  show.

## Unity candidate and in-game previews

Before her package existed, these scripts previewed her on Manki's fighter:

- `unity_import.eval.cs` copies Manki's import settings, material and Humanoid mapping; all 46 Humanoid bones map. The
  prefab assigns one material per piece itself, because Manki's importer brings no materials in.
- `size_lineup.eval.cs` (Edit Mode) renders the roster and Bashie side by side on one floor, with lines every
  0.25 m, into `tmp/bashie-model/lineup/`.
- `preview_swap.eval.cs` (Play Mode) makes the live Manki fighter render as Bashie, in memory only, at scale 1.10:
  she tops out at 1.10 m against Manki's 1.16 m. In a CPU showcase it also adds a temporary follow camera; elsewhere
  it hides the HUD.
- `preview_clips.eval.cs` (Play Mode, after the swap) plays her own candidate idle and run
  (`Assets/Art/Experiments/GenRA/bashie/`) on the fighter: a runtime copy of its animation catalog with those two clips
  changed, so nothing on disk changes.
- `preview_ground.eval.cs` (Play Mode, a few frames after the swap) puts her lowest point on the capsule bottom.
  Run it twice; the second run should report about 0. On her own idle it corrects about 1 mm (`ModelYOffset` -0.75).

The clean in-game take is the Tutorial, which has one fighter and nothing in the way:
- `sloparena.tutorial.open --fighter manki`, `editor_play`, then `sloparena.tutorial.camera` to wait for ready.
- The swap, a camera settle, ground twice.
- `sloparena.photo.live-begin` with a `follow: fighter` camera, then `sloparena.tutorial.run` and
  `sloparena.photo.live-end`.
- Restore with `photo.live-end`, `editor_stop` and `tutorial.restore`. ffmpeg turns the frames into an MP4.

Pitfalls:
- **Size:** don't scale her from a live measurement of Manki. He may be mid-crouch, which made her about 0.67× him.
- **Floor:** Manki's crouched idle retargets onto her about 38 cm below her rest pose. Hence the settled ground step
  (`ModelYOffset` about -0.37 with his idle). Her own idle stands on the floor at `ModelYOffset` -0.75.
- **Facing:** Training step `facingYaw` is in hundredths of a degree, between -18000 and 18000, and the Tutorial
  starts locked on the dummy, so send `toggleLock` first.
- **Showcase framing:** the four CPU fighters walk between the camera and her, and its film camera frames the whole stage.

## Animation

Her GenRA rig pack is a candidate pack on Manki's package, because she plays on Manki's fighter until she has her own:

```bash
artifacts/genra-20261014/.venv/bin/python tools/genra/unity_rigpack.py all manki --label bashie \
  --prefab Assets/Art/Characters/bashie/Prefabs/BashieRig.prefab      # -> artifacts/genra-rigpacks/manki-unity-bashie
```

Its references are Manki's idle, run, jump and fall played on her. Rebuild it after every re-import.

## Why the rig looks like this

- **Backdrop card:** the generator turned the first source image's grey background into one quad behind her
  (0.64 × 1.0 m). `remove_card.py` deletes flat islands of at most 8 faces that are bigger than 20% of the
  model; `build_rig.py` repeats the check. The 2026-10-22 models came from transparent cutouts and had none.
- **The Mixamo take is the rest pose:** Blender reads Mixamo's bone rest as the upload pose, which doesn't line up
  with the mesh; the take (a T-pose) does. Don't clear the take before baking it, or the bones and the body disagree
  (Sir Bonk's old Mixamo rig had the same trap).
- **Shell:** the first model (2026-10-21) had its shell generated as part of her torso. Mixamo bound it to her arms
  and legs, and cutting it off left a torso that still looked "very cursed" (owner). Her redesigned model is a plain
  humanoid body plus a separate shell model:
  - `shell_fit` scales the shell to 0.38 of her height, 1.1 wide and 0.75 deep, puts its bottom at 0.25 and moves it
    0.11 back, tipped 4° backward. It is skinned rigidly to a new bone, Shell, under Spine1, pivoting on her back, so
    a clip can bounce it.
  - It hugs her back, with its rim at her sides. A shell set further back and tipped more clipped her arms less but
    read as a backpack.
  - When her arms hang straight down, the backs of her upper arms dip into the shell's sides.
  - Comparisons: `tmp/bashie-model/review/v4-shellfit/`.
- **Tail:** the generator gave her a small tail, its own island behind her hips. The owner had it removed
  (2026-10-22): the build deletes it (`tail=keep` keeps it). The skin under it is painted red, and the shell's rim can
  bare it when she bends, so those faces take their colour from the green skin next to them.
- **Whistle and cord:** the whistle is a separate model on a new bone, Whistle, under Spine2, pivoting on its ring,
  so a move can take it to her beak for NO RUNNING!. It hangs from its ring at the top of her chest, part of the way
  toward straight down. Then it swings out until it clears her belly. Fully hung, her belly pushed the chunky barrel
  out flat. The cord is a tube from the ring, up both sides of her chest and around her neck at its narrowest. It
  rides on Spine2 and hands over to Whistle within 6 cm of the ring, so its strands follow the whistle.
- **Skinned, not parented:** the pieces are skinned meshes rather than meshes under bones, because the GenRA rig-pack
  snapshot reads only skinned meshes. Parented, the shell was invisible to the animator and its clearance checks.
- **4 influences:** Unity's default skin quality; the build keeps the four strongest and normalises.

## Known

- The life ring (2026-10-21) is a separate Tripo model, clean and not imported yet.
- Her own idle and run exist as approved candidate clips (2026-10-22, GenRA: `tools/genra/briefs/bashie-idle-on-duty.json`
  and `bashie-run-not-running.json`); everything else is still Manki's, so she hunches and flips in jumps.
