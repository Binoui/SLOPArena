# genra animation toolkit

Rig-aware authoring for the [GenRA animation workflow](../../docs/characters/genra-animation-workflow.md):
Opus 5.5 authors motion in Python against the actual skinned rig, checks it and looks
at renders. The runner writes USD, a contact sheet and a preview video, and `unity_clip.py`
turns the result into a Humanoid clip on the character's own prefab.

| Module | Purpose |
| --- | --- |
| `rig.py` | USD skeleton, forward kinematics (matches UsdSkel to 1e-7 m), bind-pose locals, anatomical hinges for knees, elbows and fingers, Unity's Humanoid muscle axes |
| `clip.py` | `Pose` edits in the character frame (including hip shifts), `Clip` keyframes with monotone or eased interpolation and seamless loop cycles, the `genra-clip-1` file |
| `solve.py` | Two-bone limb IK on anatomical hinges (elbows and knees kept in range), least-squares targets with a wrist limit and seeds, contacts held tick by tick (`track`, `pin`, `hold_grip`, `carry`), fists, two-handed grips (exact or loose, elbows kept down) |
| `skin.py` | UsdSkel skinning, body regions, clearance queries |
| `checks.py` | Clock, entry/exit, loop seams, bone lengths and scales, hinge direction, finger poses Unity can play, snaps and IK flips, clearances, a hidden face, hands on the handle, approved key poses, brief targets; form (wrists, gaits), life (secondary motion) and flow (stops, dead holds) |
| `curves.py` | Joint-angle curves (knees, thighs, elbows, arms, wrists, feet, pelvis, heights) over a reference's, the statistics behind the gait checks, and flow: stops, dead holds and the speed map |
| `render.py` | Fast software contact sheets, motion paths and MP4 previews, no GPU needed; how much of the strike each view shows |
| `usdio.py` | Writes a clip into a copy of the rig USD (lossless quaternions, textures copied) |
| `brief.py`, `rigpack.py` | Animation briefs and hashed rig packs |
| `run.py`, `claude_runner.py` | One sandboxed authoring job (or a revision of one), end to end |
| `review.py`, `REVIEW.md`, `loop.py`, `review_benchmark.py` | The director's review in Unity, the review-and-revise loop, and the reviewer's benchmark against the owner's verdicts |
| `unity_rigpack.py`, `unity_clip.py` | Rig packs from the game's own data; the way back to a Unity clip, measured in Unity |
| `hitarc.py` | Where a weapon normal hits: its capsule swept over the active ticks against the game's idle hurtboxes, as a side-view map (see Weapon arcs) |
| `flow_corpus.py` | The hand-made liveliness corpus the flow checks are calibrated on (`benchmarks/flow-corpus.json`) |
| `GUIDE.md` | The toolkit guide included in the model's prompt |

## Environment

Use the GenRA Python environment: Python 3.12 with numpy, scipy, usd-core and Pillow.
The current local interpreter is `artifacts/genra-20261014/.venv/bin/python`, pinned by
`artifacts/genra-20261014/requirements.lock.txt`. Creating an environment on another
machine installs packages and needs approval under [AGENTS.md](../../AGENTS.md). The
toolkit itself does not import GenRA. GenRA's Euler JSON route remains available for
comparison, but the runner writes USD directly.

```bash
PY=artifacts/genra-20261014/.venv/bin/python
$PY -m unittest discover -s tools/genra -p 'test_*.py'      # rig-dependent tests skip without their packs
$PY tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json --dry-run
$PY tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json --probe   # ~$0.06 sandbox check
$PY tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json          # one authoring job
cd tools && $PY -m genra check RESULT.json --brief genra/benchmarks/manki-aerial-double-fist.json
cd tools && $PY -m genra contacts RESULT.json --brief BRIEF.json   # when each foot lands, for footstep cues
```

A run writes `artifacts/genra-runs/<id>/`: brief, prompt, CLI command and settings, event
stream, tool audit, usage summary, the model's scratch files, `check.txt`, `sheet.png`,
`motion.png`, `preview.mp4` and `usd/<name>.usda`.

Every authoring scratch starts with a loadable `result.json`, a working `author.py`, and a local
`API.md` generated from the installed toolkit's signatures. It uses the prior take or approved keys on the same
clock; otherwise it starts from an explicitly labeled entry/exit placeholder. `Clip.save()` replaces files atomically.
The runner archives changed, structurally valid saves as `checkpoint-result.json`, restores that file if the last
save is incomplete, and records completed, failed, interrupted, timed-out or stalled status in `summary.json`.
Missing provider cost stays unknown. The original seed is `initial-result.json`; an unchanged seed is labeled
`checkpointOnly`, cannot qualify as final motion, and does not trigger a paid director review.

The overall `--timeout` still bounds each job. `run.py --checkpoint-timeout SECONDS` also stops a job after that
long without a changed, loadable save (default 1200 seconds). Checkpoints may still fail motion checks: they preserve
reviewable work and do not certify quality. New saves cannot extend the overall time limit.

A `--stage keys` brief carries its stage into the scratch CLI. Reports judge anatomy, grip and targets at the story
ticks separately from provisional interpolation (`transitionRisks`); window/arc targets and snaps belong to that
motion report. `./genra check result.json --stage full` checks the complete clip explicitly. A blocking pass never
substitutes for the full motion gate. Flow and `chatter` diagnostics stay advisory; do not add motion just to make
the score green. Chatter warns about bursts of repeated 1-2 tick joint reversals, including within fast windows,
where an intended body spin can still have unintended leg jitter.

- **Iteration checks:** `./genra check result.json --fast` runs numerical checks without
  mesh clearances, face visibility, the gait's forearm-to-torso mesh gap, or automatic
  sheet/motion renders. The Python equivalent is `check_clip(clip, b.check_spec(), mode='fast')`.
  Clock, contacts, anatomy, Humanoid limits, approved keys, targets, snaps and flips remain checked.
  Reports say `FAST PASS` / `FAST FAIL` and list skipped checks; a fast pass cannot qualify
  a final candidate. Use full `./genra check result.json` at pose checkpoints and delivery.
  Full checks remain the default, and the authoring runner and review always use them.
  Explicit `--sheet PATH` or `--motion PATH` can still request a render during a fast check.
- `contacts` lists the ticks each foot lands on: the first tick a foot or toe joint is within 2 cm of its lowest point
  (`--tolerance`), wrapping round a loop. Key footstep sounds, dust and similar cues to these ticks, not to the brief's
  beats: Sir Bonk's Joust gallop lands on ticks 1 and 13, a tick after its "contact 0" beats.
- `motion.png` shows ghosted poses with the paths of the hands, elbows and blade tip, one dot
  per tick, from entry to contact and from contact to the end. The model gets it with every
  full `./genra check`, because stills alone hid a Bonk lift whose left arm swept across the face
  between two distant keys. `./genra motion --range a-b` shows one stretch.
- `check_clip` also warns when the character's own arms or weapon hide the face from the front
  for 4 or more ticks. That's a warning, not a failure, because Bonk's approved jump start does
  it for 17 ticks. A rerun of that Bonk lift fixed the path but held the sword with the left
  arm across the face for 7 ticks, which led to this warning.
- The preview shows the side and three-quarter views together, twice at real speed and once
  at quarter speed, with the tick and the active window labelled. It needs `ffmpeg`
  (`./genra video` renders one on demand). The model works in `/tmp/genra-runs/<id>/` through the `./py` and
`./genra` wrappers.

**Character frame.** Brief targets, pose edits and every review view use the character frame:
origin at the entry hips, up world up, front the fighter's facing. With Unity rig packs the
facing is the actor's forward from `mapping.json`: the direction the game moves and attacks
along and the match camera looks along. Before 18 October, front was square to the entry
hips. Fighting stances turn the hips well off the facing (idle: Bonk 90°, Wibou 77°, FightGuy
51°, Manki 41°; run about 0°), so the `gameplay` and three-quarter views of idle- and
jump-entry briefs were taken from the side, and "ahead" in their targets pointed sideways.
Wibou's approved R charge loop beckons about 77° off the dash for this reason. Move briefs
written before then carry `"frame": "hips"` so they still reproduce their approved clips;
new briefs use the facing.

Review sheets show profile, front and a three-quarter rear view: behind the striking
shoulder, 35° to the side and 20° up. The match camera sits straight behind the fighter.
In the first five runs (hip frame) it showed only 0–24% of the striking hand or weapon at contact,
against 39–91% from the three-quarter view, so it is only an optional `gameplay` view.
When the brief names its `strike` joints, `check.txt` and `summary.json` record how much
of the striking part each view shows at contact. This is information, not a check.

## Rig packs and briefs

`run.py` prints advisory brief lint before any model call, including `--dry-run`.
Optional `purpose` accepts `attack`, `special`, `idle`, `locomotion`, `loop`, or `other`.
When absent, the runner reports an inference from the name and notes.
Lint checks slow authoring for attacks and specials, striking-part targets, calm idle budgets,
plain normal recoveries, recovery flourishes, and contradictory direction. Warnings never block a run.
Use `clock.game_duration_ticks` for a numeric authored/game ratio check around 2x, with a 1.7-2.3 tolerance.
Older briefs can state twice-speed authoring in their notes or timing contract.

A rig pack holds `rig.usda`, `references/*.usda`, `textures/`, `images/` and a hash
manifest; the runner refuses changed files. Rig packs live under `artifacts/genra-rigpacks/`
and are local.

**Current characters** (`<package>-unity`) are built from the game's own data:

```bash
$PY tools/genra/unity_rigpack.py snapshot manki   # read-only eval_file through your own gateway lease
$PY tools/genra/unity_rigpack.py build manki      # USD rig pack + per-vertex validation
$PY tools/genra/unity_rigpack.py all manki fightguy bonk wibou
$PY tools/genra/unity_rigpack.py all wibou --reference dash=anim.wibou.r   # adds a bound move: wibou-unity-dash
```

`--reference LABEL=SEMANTIC_ID` adds a bound move clip as `references/LABEL.usda`, for example
the attack a new aim loop hands off to, so the brief can show it to the model. That pack is
built beside the shared one (`<package>-unity-LABEL`, from its own snapshot), so other briefs
keep theirs.

**A new model before it replaces the catalog rig** gets a candidate pack:

```bash
$PY tools/genra/unity_rigpack.py all bonk --label sir \
  --prefab Assets/CharacterPackages/bonk/Presentation/SirBonkRig.prefab \
  --weapon-config Assets/Art/Characters/bonk/SirBonkWeaponAttachConfig.asset   # -> bonk-unity-sir
```

- **What it snapshots:** the unbound prefab, playing the package's bound clips. The weapon config is
  the candidate's own: a new hand's bone frame needs converted offsets. Keep that config outside the
  package folder, or the shared pack's config scan picks it up too.
- **Where clips play:** `mapping.json` records the prefab, and `unity_clip.py` write, render and
  timeline play on it. So a candidate's clips are written and reviewed on the new model while the
  catalog still binds the old one. The Sir Bonk idle and run (2026-10-20) were the first.

- **Snapshot:** `unity/snapshot_rigpack.cs.template` instantiates the catalog prefab in a
  preview scene. It records the skinned meshes as rendered (vertices, UVs, triangles,
  weights, bindposes), the rotation constraints, and always-visible weapons placed as
  `WeaponAttach` does. It plays the bound idle, run, jump and fall clips at 60 Hz the way
  `PlayerRenderer` does (Animancer, root motion off), bakes Unity's skinned vertices at three
  frames per clip and measures the Avatar's muscle axes. Nothing is saved or imported.
  Plain `AnimationClip.SampleAnimation` is not a substitute: it applied root motion on
  prefabs that enable it (Bonk, Wibou) and extracted roots differently, up to 3.5 m away
  from what the game shows.
  Follow the [Unity CLI](../../docs/contributing/unity-cli.md) and
  [coordination](../../docs/contributing/unity-editor-coordination.md) rules.
- **Build:** writes the pack in a right-handed Y-up space (Unity x mirrored).
  - A uniform import scale moves onto the Skeleton prim, because USD stores joint scales
    as half floats.
  - Constraint-driven copy bones (Manki's skin fingers) are bound to their source joints.
  - Weapons become a rigid `Weapon<k>` joint under their bone, with `bladeHilt`/`bladeEnd`
    anchor joints. Slot-specific props (Manki's bazooka, aerosol, bomb) are listed in
    `mapping.json` but not attached, unless `--prop SLOT` names one: `all manki --label aerosol --prop F
    --reference fire=anim.manki.gf-atk` builds `manki-unity-aerosol` with the can as `Weapon0`, so the model sees it,
    the clearance checks keep it off his head and body, and `unity_clip.py render` draws it (Manki's F hold loop,
    2026-10-21: until then the model would have shaken an invisible can). Props without trail anchors get no
    `bladeHilt`/`bladeEnd`; aim brief targets at `Weapon0`, the prop's pivot in the hand.
- **Validation:** the pack's forward kinematics and skinning are compared with Unity's
  sampled bones and baked vertices, vertex by vertex (`validation.json`). The
  17 October packs agree within 0.0008 mm for bones and 0.0008 mm for skin.
- **Anatomy:** `anatomy.json` holds each hinge's flexion axis, learned from the
  character's approved clips where they agree. Knuckles take their finger's middle-joint
  axis, because open hands hyperextend them. `reversedAllowanceDegrees` records the
  backward bend the approved clips already use (for example Bonk's and Wibou's open left
  hands), so the checks do not flag the character's own style.

- **Two-handed grip:** Bonk's pack also carries four approved attacks (`swing`, `rising`,
  `double`, `charge`) as references. `grip.json` is the left hand's exact transform on the
  sword in the cleanest approved two-handed frame (Charging Double Slash, tick 46: the left
  fist 0.6 cm off the handle axis, under the right fist), and `images/two-hand-grip.png`
  shows it. Animators rarely keep a second hand rigid: across Bonk's frames the same grip
  varies by up to 65°, so one clean frame beats an average. A geometric fist model missed the
  real right-hand grip by 12 cm. `two_hand_grip()` solves the spine and both arms together
  so the left hand lands on that grip and the hilt goes where both arms reach. `hold_grip()`
  keeps it there between keys, and a brief's `two_hands` ticks fail the check if it leaves.
  A frame counts as a grip when the left grip point is no further off the handle axis than the
  weapon hand's own distance plus 3 cm. The grip point lies between the wrist and the knuckles,
  so even the weapon hand sits off the axis: 4.8 cm on the old sword, 3.2 cm on Sir Bonk's plank.
  A fixed 3 cm found no grip for Sir Bonk, and his E1 runs improvised one 10–13 cm off the handle.
  `unity_rigpack.py grip bonk --label sir [--grip-from LABEL:TICK]` rewrites a pack's `grip.json`
  and image from its references without Unity. Sir Bonk's comes from the old Double Slash
  played on his body (tick 20, 3.5 cm off the axis); re-learn it from his first approved
  two-handed clip. Without a `grip.json`, the `two_hands` check still fails another hand more
  than 2 cm further off the handle than the weapon hand.
  A brief's `"two_hands_hold": "loose"` swaps the exact grip for a loose hold (`solve.handle_target`): the fist
  stays on the handle beside the weapon hand, free to turn about it and slide along it, and `elbows=True` keeps the
  elbows hanging. The exact grip left Sir Bonk's Barn Door arms one solution, with the left elbow up to 11 cm over
  the shoulder; the owner saw it at once ("both hands try to be together on the handle, when in fact they could be
  side by side"). Re-solved loose, the elbows sat 16-29 cm below the shoulders (2026-10-22).
  Elbow and knee hinges keep their learned axes, because Unity's twist distribution turns
  their bend plane. Finger hinges use Unity's exact axes.

**Props.** A mesh skinned entirely to one leaf joint outside the Humanoid mapping, hung directly on a mapped bone,
is a rigid prop, such as Bashie's shell and whistle (2026-10-22). It gets its own region, `Prop`, and the clearance
check watches the forearms, hands and head against it (`Skin.props` lists them). Under the ancestor rule alone
such a mesh joined the Torso, and her first run keys swung an arm 10 cm into the shell unreported: arms are never
checked against the torso they join at the shoulder. Helper joints that share a mesh with the body, such as Manki's
arm stretch and offset controls, stay in the body's regions.

**One skeleton, several bodies (Nyx, 2026-10-29).** `--meshes` builds one pack per form, and each pack lists every
renderer its form draws: `Nyx_Real,Nyx_Real_Tights,Nyx_Real_Dress` and `Nyx_Fake,Nyx_Backpack,Nyx_BackpackPole,Nyx_Phone`.
A brief's `meshes_shown` gives the other form's meshes, and props that change places, their tick windows. When her model
was rebuilt mid-session, these held:

- **Mapped toes are always kept.** A pack keeps only weighted joints and their ancestors, and the rebuilt boots were
  weighted to the feet alone, so `LeftToeBase` and `RightToeBase`, which briefs plant and floor-check, dropped out.
- **Added subtrees load.** `Clip.load` puts an older clip onto a pack that has gained joints hanging off its own (a held
  prop, the dress's 48 spring-chain joints under Hips) and leaves those joints at their bind locals.
- **Unposed helpers are not held to the keys.** The approved-keys check skips joints outside the Humanoid that stay at
  their bind rotation in both the keys and the take. Riding the hips, the dress chains' tips swung 3 cm within the
  allowed overlap and failed keys the animator had kept.
- **A pole is at least 0.3 m long.** A rigid skinned mesh in one hand gets `bladeHilt`/`bladeEnd` only if its ends lie
  0.3 m or more apart. Without this the phone took the anchors, which the keys check holds to the exact tick.
- **Rebuild packs when the prefab changes.** Compare the prefab's hash before rendering. `write` names a changed vertex
  count (`meshChanged`) but not new bones: a re-export that gave the fake body 115 bindposes for 67 bones drew garbage
  and made sole grounding read nonsense, with matching vertex counts.

`manki-sr199` is the experiment-time Manki rig from 14 October, built through Blender. It is
used only by the aerial benchmark.

Briefs are JSON (see `brief.py`). The creative text stays verbatim, while the clock, fast
windows and numeric targets are what the checks enforce. `strike` lists the joints that
deliver the hit (`RightHand`, or `bladeEnd` for a weapon); a left-side strike moves the
review view behind the left shoulder. Targets should restate the
creative text, not add new requirements.

Clips that are not attacks leave out the contact, active and IASA ticks. A looping clip
(`loop.from_tick`) plays a one-shot build-up and then cycles from that tick to the end; the
model keys it with `Clip.from_keys(..., cycle=...)` and the check fails a seam that does not
close or that jerks harder than the loop itself. `entry.match: false` keeps the entry
reference only as the character frame, for clips such as the shared tumble that blend in from
several different reactions. `views` and `preview_views` replace the default review views,
for example end-on views where the players see the motion along its path.

## Into Unity

`unity_clip.py` writes a toolkit result as a Humanoid `.anim` for the character's catalog
prefab, through the gateway's `eval_file` under your own lease
([Unity CLI](../../docs/contributing/unity-cli.md)):

```bash
$PY tools/genra/unity_clip.py roundtrip fightguy idle     # proof on an approved clip
$PY tools/genra/unity_clip.py write RESULT.json --brief BRIEF.json [--save | --asset Assets/...anim] \
    [--ticks FIRST-LAST [--retime SOURCE:NEW,...]]
$PY tools/genra/unity_clip.py render --brief BRIEF.json --result RESULT.json \
    --clip new=Assets/Art/Experiments/GenRA/bonk/NAME.anim --clip current=anim.bonk.a1
```

- Mapped Humanoid bones become muscle and body curves. Unity's `GetHumanPose` is not an
  exact inverse (Manki's right-leg twist read back at half), so each chain's muscles are
  refined, together with the body, until `SetHumanPose` reproduces the toolkit's joint
  positions and orientations. Other joints (tails, deformers) keep exact local curves.
- Root motion is baked into the pose: `PlayerRenderer` plays clips through Animancer with
  `applyRootMotion` off. The hips may move; the fighter does not.
- Everything is measured as the game plays it (Animancer in a preview scene). For a matched
  entry, `write` matches the body origin to the entry clip's playback at the entry tick. A
  free entry (`entry.match: false`) keeps the toolkit's origin, which is already Unity's:
  moving a crouched loop's body centre onto idle's had lifted it 17–21 cm off the floor.
  The report warns when a clip plays more than 1 cm from where the toolkit put it, and
  compares joints and skinned vertices with the toolkit. Nothing is saved unless `--save` or
  `--asset` names a new path (default `Assets/Art/Experiments/GenRA/<package>/<name>.anim`),
  and nothing is bound.
- Judge written clips with `render`, `timeline`, the Ability Lab or Training, not Unity's Inspector preview.
  For grounded locomotion, set `"grounding": "reference-sole"` in the brief. The writer
  measures the lowest foot-weighted skinned vertex on the entry reference, then changes
  only `RootT.y` to keep one sole at that height every tick. It verifies the result through
  Animancer and reports each vertical correction and residual. This is for walks without
  flight, not jumps or runs with airborne phases. Character-frame up is relative to the
  entry hips, so a prose floor height must not be mistaken for the prefab's world floor.
  The clips name no preview model, so the Inspector shows them broken on its default mannequin
  (Wibou's R charge loop, 2026-10-18), and plain sampling applies the prefab's root motion.
- Unity loops only a whole clip. A loop from tick 0 (an aim hold) is saved looping, and the
  report gives its `loopSeam`. A loop after a build-up is saved one-shot, with
  `loopFromSeconds` for the game to wrap it, as `PlayerRenderer` does for the tumble.
- `--ticks FIRST-LAST` writes one phase of a result as its own clip, for an ability that plays a
  clip per phase. A phase that starts after tick 0 keeps the toolkit's origin. `--retime` warps
  the phase's time linearly between anchor ticks (`Clip.retime`, rotations slerped). Sir Bonk's
  E flight shortens its descent hold and stretches the swing into the last ticks before
  touchdown with `--ticks 0-100 --retime 0:0,40:40,96:62,97:66,98:69,99:72,100:73`
  ([bonk.md](../../docs/characters/bonk.md)). A cut or warped phase is checked again
  (`phase_check`: snaps, flips, wrists, with the brief's fast windows and wrist exemption moved along). Its failures
  are printed and reported as `phaseCheck`: squeezing a section makes it turn faster than the authored clip did. That
  E land clip's squeezed haul snapped at 47°/tick, and its slam still carried the authored IK flip.
- The cook bakes `length × 60` frames, so a one-shot clip's last key is never shown in game: the
  weapon sampler and the server hold the frame before it. End a phase clip one key past the pose
  it should hold.

The 17 October round trips rebuilt idle, run, jump and fall for all four characters within
0.084 mm per bone and 0.126 mm per vertex of the original playback. A toolkit clip with
fists, a hip drop and a punch played back within 1.2 mm per joint.

**Known gap: limb twist.** Unity spreads a hand's or foot's twist along the forearm or shin
(the Avatar's twist settings, 50%), while the toolkit keeps it at the wrist or ankle. Joints
still match, but in that test the forearm mesh rolled up to 81 mm differently during the
punch and the shin up to 21 mm. `write` reports it as `meshVsToolkit`.

`render` is the review on the real character: the catalog prefab with its materials and
runtime weapons, in a preview scene, playing each clip through Animancer stretched over the
brief's ticks as `PlayerRenderer` does (speed = baked frames / stage ticks). It renders the
toolkit's review views and framing into `real.mp4` (a row per clip, real speed twice, then
quarter speed) and `real-sheet.png`. Pass the bound clip the candidate would replace as a
second `--clip` to compare them on the same clock. Nothing in the project changes.

- **Springs:** `BoneChainSpring` chains (Manki's tail, Sir Bonk's cape) step once per tick as
  they do in Play Mode. A looping clip runs one silent cycle first, so the spring is already
  swinging at tick 0. `--no-springs` keeps them at rest.
- **Travel:** `--travel` carries a single clip forward in metres per tick, so a cape trails as it
  does in a run: 0.1515 is Bonk's 10 m/s at the prefab's scale.
- **Cameras:** they follow only the fighter's travel from the prefab's own root position. Bonk's
  root sits 1 m down, and following the absolute position framed him a metre low until
  2026-10-20.
- **Props that change hands:** a brief's `props_shown` (`{"Weapon<k>": [[first, last]]}`, the k-th
  weapon in `mapping.json`) shows a pack weapon only in those ticks, as the game's hide rules do.
  Unlisted weapons always show. Sir Bonk's Q pack carries his pot lid twice, on the shoulder and in
  the hand, and drew both on every tick until 2026-10-22. A timeline row takes its own
  `props_shown` in its own ticks, so a retimed cut keeps the lid in step.

`timeline SPEC.json --brief BRIEF.json [--result RESULT.json]` reviews the handoffs into and
out of a candidate, which a single-clip render cannot show. Each row of the spec is a
sequence of clip events, as `PlayerRenderer` drives them: a tick, a clip (an `Assets/...`
`.anim` or a bound semantic id), a crossfade in seconds, a start time, and a speed or
`ticks` to stretch the clip like an ability. Rows can also travel (`moves`: first tick, last
tick, metres per tick forward, and optionally metres per tick up), and the cameras follow. A
spec's `floor` (rig-pack height in metres) draws the ground, so a leap's landing or a weapon
stuck in the floor reads. Phase labels name the ticks. It
writes `timeline.mp4` and `timeline-sheet.png`. Wibou's R review was idle, then the held
charge loop, then the release into the dash at its 6.4 m travel; it showed a 3-tick spring
up into the dash clip's upright first frame. Sir Bonk's E drop review (2026-11-01) rises
6 m, hangs, and drops onto the floor along the game's path: before that, a timeline could
only move forward and had no ground.

`promote SOURCE --over Assets/CharacterPackages/<pkg>/Animations/<name>.anim` makes an accepted clip live:

- Over an existing package clip, it copies the content and keeps the GUID and name, so bindings and abilities keep
  pointing at it. `before.anim` in the evidence folder keeps the old file.
- A missing target is created as a copy; bind it with `sloparena.character.bind`. Clips bound to a model's take (an
  FBX) cannot take new content.

Then run `sloparena.character.cook` and `verify --target <pkg>`, and `roster.refresh --package-id <pkg>`.

Rig packs also carry `humanoid.json`: each mapped joint's muscle axes and ranges, measured
on the Avatar. Fingers, thumbs and toes only play rotations about those axes, so finger
hinges use them, `fist()` curls thumbs on them and snaps every digit onto them, and
`check_clip` fails digit rotations Unity would drop (`snap_to_humanoid()` fixes free edits).
The approved clips sit exactly on these axes; the bind pose does not (13–29°).

**Humanoid limits.** Unity does not clamp muscle values, but it plays a joint only through
its muscles. A collarbone cannot roll about its own length, a twist folds back past 90°
and a swing past 180°. Before 17 October the solvers rotated collarbones freely. Every
two-handed Bonk run then exceeded these limits, by up to 122° (whirlwind, right
collarbone), and its Unity conversion missed by 72 mm per joint and 40 cm per vertex.
Repairing those clips afterwards failed: within the limits the left hand came 23 cm off
the grip. Now `solve()` (and so `two_hand_grip` and `hold_grip`) moves the spine, neck,
head, collarbones, upper arms and thighs only through their muscles. Each stays within
2.5 times Unity's default range, as far as the characters' own approved clips go, with no
twist past 85° and no swing past 170°. `check_clip` fails anything past that by more than
5° (`rig.humanoid_excess`). Hands and feet stay free: Unity plays their twist through the
forearm and shin.

## Review loop

The default route is approved keys, first take, owner look, then `run.py --revise --owner` when requested.
Build flow into the first take. Flow stays advisory, and the owner judges stiffness at game speed.
Normals get a plain, short recovery; personality can live in the move itself. Specials can carry more.

Use `run.py BRIEF --revise RUN --owner-file direction.md` with the
[owner direction template](briefs/OWNER-DIRECTION.md). Markdown is passed verbatim; JSON strings and objects also work.
`--owner "text"` remains available. The two direction flags are mutually exclusive.

The director is an opt-in diagnostic. Calling `review.py` explicitly opts in.
For automated rounds, add `--director` to `loop.py`.

```bash
$PY tools/genra/review.py BRIEF.json RESULT.json [--asset Assets/...anim] [--reference oldrun] [--travel 0.1515]
$PY tools/genra/loop.py BRIEF.json --director [--from artifacts/genra-runs/<id>] [--rounds 1]
$PY tools/genra/review_benchmark.py [--only sir-run-v2]
```

- **The director** (`review.py`, Opus 5.5 at high effort, no tools) follows [REVIEW.md](REVIEW.md): the bar, the known
  failure modes of generated clips, and the notes format. It gets:
  - Unity filmstrips of the clip from the gameplay, side and front views, each cropped to the character, 12 frames an
    image;
  - the brief's style reference played on the same prefab over the same ticks;
  - joint curves over the reference's;
  - hand close-ups with held weapons hidden;
  - the check report.

  It replies in schema-checked JSON: issues with ticks, joints, evidence, a fix and a measurable target, what to keep,
  and a verdict. About $0.26 and one minute a review. Evidence goes to `artifacts/genra-reviews/<id>/`.
- **Style references:** a brief's `review` block names a rig pack reference and the ground speed. `unity_rigpack.py
  --reference LABEL=Assets/...` carries any Humanoid clip retargeted onto the character, such as other characters' runs:
  the owner wants cartoony, hand-keyed motion, and the roster's approved clips are the bar. Sir Bonk's pack carries
  `oldrun` (the old Bonk run, Grruzam's big-sword `Run_ver_B`), `swordrun` (Grruzam `Run_ver_A`), `fightguyrun`, `wibourun`
  and `mankirun`.
- **The loop** authors one take for the owner by default. `--rounds 0` alone never buys a review.
  With `--director`, it reviews the latest clip, revises at most `--rounds` times, and reviews the final revision.
  `--director --rounds 0` reviews without revising. `--polish`, `--skip`, and `--start-review` require director mode.
  An owner's `--owner` direction takes priority over the director's notes.
  `artifacts/genra-loops/<id>/loop.md` logs the rounds and shows missing costs as `UNKNOWN`, including reused takes.
  `--dry-run` prepares authoring without any model call or review.
- **One constraint contract per run:** the run's saved `brief.json` and `keys/result.json`
  retain its resolved brief and approved poses outside writable scratch. Authoring postprocessing,
  review (including direct `review.py`) and `genra check` on that saved result use this contract.
  Older runs resolve their matching archived scratch brief and keys. Missing keys or disagreeing
  legacy brief copies fail explicitly. A standalone clip still uses the supplied brief.
  Both `--start-review` and `review.py --reuse` rerun full checks against the resolved contract;
  they never copy an old PASS. New evidence records the clip's content hash; reuse rejects a
  changed clip. Legacy reviews compare their recorded result file to the requested clip.
  Final selection requires passing full authoring **and** review reports. If the latest round
  fails, the loop keeps the latest passing round; if all fail, it reports no passing candidate
  and exits with status 1. This closes the Joust gap where authoring failed approved keys
  while review and selection passed the same revision without checking them.
- **The style reference pulls the revision toward it.** Sir Bonk's Joust gallop (2026-10-21) was reviewed against his run.
  The director asked for run-like life and more lean, and the revision hunched him over the hobby horse and moved the
  owner's approved keys 24 cm. The owner kept round 0: "he is actually holding the fake horse. the second one just looks
  like his run anim". Pick a reference that moves like the clip should, or none, and let the approved keys and video
  beats be the bar. The loop now keeps the last round that passed its checks when a revision fails them.
- **Show the owner the candidate alone.** The review render's lower rows are the style reference; sent as they were,
  the owner took them for the take. Render the takes with `unity_clip.py render --clip ...` and no reference.
- **The benchmark** (`benchmarks/review/cases.json`) holds clips with the owner's verdicts: the two rejected Sir Bonk
  runs with the owner's notes, the accepted Sir Bonk idle, Wibou's R charge loop B and two Bonk aerials. The director must
  find the owner's issues and must not send back clips the owner approved for anything but the anatomy breaks the checks
  fail. Run it after changing REVIEW.md or the evidence.

**Form checks, calibrated on the roster.** The gait checks apply to looping clips whose feet step in turn (both travel
over 0.3 m front to back, in opposite phase), so spins, flips and flails are left out. On the four roster runs and idles
they raise nothing; on the two rejected Sir Bonk runs they raise 6-10 issues each.

- **Wrists:** approved clips bend a hand at most 95° off its forearm (45° in loops). Past 105° fails.
- **Knees:** the roster's runs straighten them to 6-36°. A run whose knees never straighten past 40° is warned.
- **Mirror copies:** the roster's legs differ by 4-8° half a cycle apart, with each curve's mean removed. Under 2.5° is
  warned.
- **Plain sines:** a curve is a plain sine when under 0.2% of its variation lies past its second harmonic. Two or more
  among the knee, thigh, elbow, arm, pelvis and chest curves are warned.
- **Arms on the torso:** a forearm within 2 cm of the torso's skin on more than half a run is warned. The roster's worst
  is 18%.
- **Head riding the hips:** warned when the head moves under 25% of the hips' bob relative to them. The roster's runs
  sit at 41-81%.

Lessons from Sir Bonk's jump and fall (2026-10-21):

- **The reference is shown side by side with the clip.** The review image puts the reference and the clip next to each
  other at the same ticks from the gameplay, side and front views. When the reference was a small two-view strip, a
  review called a wrist fix "almost exactly" the approved idle. Side by side, the fix had raised the elbow to head height.
- **A matched entry inherits the entry clip's flaws.** The jump starts exactly on the approved idle, whose plank hand
  folds 137°. Its tick 0 failed the wrist check, which no jump revision could fix. A brief's `wrist_exempt` skips the ticks
  that inherit such a fold and must say why; the clip straightens the wrist after them. It takes one `[first, last]`
  range or a list of them: a move that starts and ends on the idle, such as Sir Bonk's normals, exempts both ends.
- **Brief targets shape what the game camera sees.** The first fall brief let the plank lie at 28° above level. From
  behind, it showed end-on as a block beside the crest. Matching the idle's angle (tip at least 1.0 m above the hilt)
  fixed it.

The upper-arm curve measures the arm's angle out of the side plane. An angle within that plane has no stable value
when the arm points sideways, as a big character's elbows do: Sir Bonk's pumping arm read as a 220° "double peak" swing
that was really a single 28° hump, and a review asked to fix it.

Three approved clips fail the wrist check, which nobody had measured before: the whirlwind's right wrist at 123°, the
blade-storm spin's left at 115°, and Sir Bonk's idle plank hand at 137°, which the plank hides in game.

## Flow

Start the motion between approved keys with the toolkit:

```python
from genra.brief import Brief
from genra.flow import flow, Accent
b = Brief('brief.json')
clip, report = flow(b)
print(report)
clip.save('result.json')
```

The runner seeds the first take this way when approved keys exist.
Purpose presets supply tangents, hinge turnover, reversal arcs, ordered settle, and finger curl.
Lag and drag are opt-in. Use `Accent` for directed gestures and `holds` for intentional stiffness.
Run contacts after flow and audit approved-key drift again. Flow numbers are advisory.
Normals default to a plain, short recovery. Put personality in the move. Specials can carry more.
See [the toolkit guide](GUIDE.md#flow-for-one-shots) for dials and report fields.

Replay retained takes without model calls:

```bash
PYTHONPATH=tools $PY -m genra.replay_flow
```

The board lands at `artifacts/genra-replays/flow-<toolkit hash>/board.html` by default.
`--out DIR` selects another directory. The board compares saved takes at game speed and records owner verdicts.

### Continuity before retries

When a grip or contact check fails, save and run `./genra continuity result.json` in the run's scratch directory.
Inspect the implicated interval's landing, roll, limit, and reach diagnostics.
Use `./genra continuity result.json --keys a,b --replay-carry` for one bounded solver replay on a copy.
Replace `a,b` with the interval's ticks. Large key turns indicate risks, not confirmed flips.
Save after each verified fix. If the runner plateaus, continue from the summary's `nextTask`.

## Contacts, flips and key poses

Sir Bonk's E1 leap (2026-10-21) was the longest move yet: 148 ticks, ten beats, a plank stuck in the floor. Both
authoring runs hit their time limits (30 and 45 minutes). The event logs show where the time went:

- **The toolkit ran, the model waited.** About 60% of each run's wall time was toolkit compute: r0 waited 12 of its
  30 minutes on one tick-by-tick grip solve, and r1 spent about 15 of 43 minutes on brute-force searches, mostly for
  poses where both wrists stayed acceptable. Neither could use `two_hand_grip`: the pack had no `grip.json` (see above).
- **The stuck plank was over-constrained.** The weapon is rigid in its hand, and the brief fixed that hand in space.
  The right hand's orientation then changed by 0.0° from t100 to t132, through a handstand and a haul. Its roll suited
  the handstand (wrist 0°), so the haul folded the right wrist to 57°, and the searches settled on a crouch. The brief's
  checks pinned only the tip.
- **The slam's 158° pop was an IK flip.** 153° of it was LeftArm rolling about its own length between keys solved
  separately. The review called it a rotation taking the long way round and asked to slow the swing, which only shows
  the roll for longer.
- **The pose misses survived both revisions.** The upright launch and the hunched haul missed the reference's story
  poses. The revisions spent their time solving transitions while the technical defects got fixed.

What changed:

- **Wrists in the solver:** `solve(..., wrists=45)` makes a hand cost past 45° off its forearm, so the solver turns the
  forearm, elbow and shoulder instead. `two_hand_grip`, `hold_grip`, `track` and `pin` use it by default. On Sir Bonk's
  pack, two-handed ready and low holds came out at 45° or less, with the grip exact. Without it, one wrist folded
  127-162°.
- **Continuity:** `solve(..., seed=K[previous_key])` starts from the neighbouring key's solution. `track()` re-solves a
  stretch tick by tick, each tick seeded from and kept near the last (`continuity`, 0.3 per radian), so the elbow and
  its roll stay on one solution.
- **Pins:** `pin(clip, first, last, 'bladeEnd', grip='roll')` keeps a point fixed while the keyed body moves, and the
  arm that carries it follows. `grip='roll'` (`grip_target(rig, roll=True)`) lets the other hand turn about the handle.
  On r1's stuck stretch it held the tip within 2.5 mm and put the left hand on the handle (9.1 to 3.5 cm off its axis).
  Wrists were at most 49° (right) and 55° (left), against 57° and 98° before, in 3.4 s for 33 ticks. The authored boundary
  ticks still jump: key the first and last tick with the same contact.
- **Checks:** `flip` fails a limb bone rolling about its own length by more than 60° in a tick or 120° in three, fast
  windows included. The roster's 51 approved references stay under 37° and 80°; the slowed E1 slam rolled 153° in three
  ticks. `grip` measures the left hand after turning the grip about the handle to it (at most 90°), and fails a hand
  off the handle when a pack has no `grip.json`. `unity_clip.py write` re-checks a cut or retimed phase.
- **Timing contract:** a brief's `timing {fixed, adjustable}` records what the game holds the clip to and what may
  change. The animator and the director see it, and `loop.py` notes a brief without one. E1 squeezed a 148-tick leap
  into rules the owner was happy to change; nobody had asked.
- **Key poses first:** `run.py BRIEF --stage keys` authors only the key poses (`Brief.key_beats`: the ticks the
  review beats map to, or `keys`) and renders `keys.png`, plus `beats-keys.png` when the brief has video beats. Once
  the owner approves them, `run.py BRIEF --keys <that run>` (or `loop.py --keys`) builds the motion between them, and
  the check fails a key that moves more than 2 cm (`approved_keys`).
- **When the body has to change:** the 2 cm key lock holds every body joint, so a revision that leans the body into
  a move cannot pass it. Sir Bonk's Q throw (2026-10-22) read stiff, and the owner agreed with the review. The
  revision ran from round 0 (`loop.py --from`) without `--keys`, and the brief's targets kept the beats the owner
  had approved: where the lid is at the grab, the cock and the release. It passed with no warnings; the motion
  revision cost $3.51, against $0.62 for round 0.
- **Long moves:** author one job per phase the game plays (for E1: the flight with its swing; the impact and
  handstand; the haul and pop). Chain the jobs with `entry`/`exit` references so each starts on the last one's pose.
  Preview the game's trajectory early with `unity_clip.py timeline` (travel): E1's touchdown cut showed up only in
  game.

**Two-handed swings (2026-10-21).** Sir Bonk's Barn Door take spent its whole 45-minute budget on the left hand,
though the grip helpers are fast (`two_hand_grip` 0.1 s a key, `hold_grip` 0.03 s a tick). Its keys turned the plank
180 degrees in three ticks while the chest turned 45, and were solved one at a time, so neighbouring keys held the
arms on different solutions: carried forward, the arms arrived 48-163 degrees from the next key's. Between keys the
left hand then rolled round the handle (163 degrees in three ticks), and the animator wrote its own optimisers
instead of finishing. `carry(clip, keys)` holds the weapon in the chest's frame between keys and reports that landing
gap; with keys that agree it swept the plank cleanly in 9 seconds. Keys that disagree have to be re-keyed, not
searched: re-solving them from their neighbours moved the elbows about 50 cm.

**Carry fixes (2026-10-22).** `carry` aimed the hilt-to-hand line at a direction the hand can never reach (77 degrees
off on the plank, whose hand sits 39 cm down the handle), so every carried tick tipped the weapon ~18 degrees and
moved the hilt up to 15 cm; take 2's in-betweens had it. It now aims that line where the hand sits in the weapon's
frame, and a weapon carried between two equal keys stays put. `both_ways=True` solves each stretch from both ends
and blends them (the arms land on each key's own solution: Barn Door's worst landing fell from 126 to 33 degrees),
and `hold='loose'` with `elbows=True` carries the loose hold. The owner asked whether one arm should decide the
weapon: in the game it does (the plank hangs on the right hand bone), but authoring solves both arms together, holds
the weapon's direction and lets its position give (`path`).

## Weapon arcs

For a weapon normal the weapon is the hitbox, so its arc is the move (owner, 2026-10-22: "for those moves it's the most
important part"). `hitarc.py` sweeps the weapon capsule (`bladeHilt` to `bladeEnd`, the move's radius) over the active
ticks of a toolkit clip and tests it against the defenders' real idle hurtboxes, read from a HitReliabilityReport JSON
(`targets`), on a grid of positions from 2.5 m behind to 3.5 m ahead and 0-3 m up: the same map as the report's Arc
section, before anything is cooked. `CALIBRATION` maps the rig pack's frame into the game's: Sir Bonk's cooked plank sits
about 0.2 m higher than his pack's, fitted on Barn Door's active ticks; with it the estimate matched the game's maps for
Squire's Cut and Barn Door on 97-99% of cells. Use it on key poses and sketched arcs before authoring a weapon normal's
motion, and check the bound move with the report. On Sunrise's first keys it showed what the owner saw: the plank held
still overhead for most of the window covered 151 FightGuy positions (23 behind him) against the old hand-made rising
slash's 266 (front to back).

A kick or punch whose limb is the hitbox sweeps a joint line instead: `--joints RightFoot,RightToeBase` on the
command line, or `"joints": ["RightFoot", "RightToeBase"]` in a brief's `arc` target. For an aerial the grid's heights
are relative to its feet. FightGuy's Flip Kick (2026-10-29) was briefed against the old aerial High Kick's 51 positions
and estimated at 124 on its keys. The cooked move, a 0.5 m sphere on the foot of the game's model, reached 86. Without
a calibration, read the estimate as an upper bound. `report_spheres` reads each target volume as a sphere at its
start point, so recheck it once a report's targets are capsule hurtboxes.

## Visual references

### Image pose sheets (default)

Reuse accepted references first. For new visual direction on ordinary attacks and holds,
use a reference-capable image tool with the character's actual render. Start with one
coherent, labelled pose sheet and keep the camera, proportions, weapon and grip consistent.
The [workflow](../../docs/characters/genra-animation-workflow.md#choose-visual-references)
records the suggested Nano Banana 2 trial, spending boundaries and movement-video cases.

Attach the sheet in the brief's `images`; use `image_notes` to name the poses and their
game ticks, and `keys` to select blocking ticks. Include foot contacts, weight shifts,
the leading body part and limb/weapon arcs in `description`. These are existing brief
fields; no new schema or generator command is needed. `--stage keys` produces `keys.png`
for comparison with the sheet. The current `review.beats` helper requires video timestamps,
so leave it unset for still-image references and inspect the sheet beside the key and
Unity captures directly. Inspect rough playback once transitions exist, then repair the
animation's timing, spacing and contacts.

### Video references

Use generated video when an unresolved movement idea needs exploration, such as a stumble,
distinctive gait or elaborate recovery. State the question the take should answer. Start
with one short take from the selected model within the task's authorized budget.
`video_ref.py` can also compare several models when that comparison is part of the task:

```bash
$PY tools/genra/video_ref.py generate PROMPT.txt --first-frame FRAME.jpg --models bytedance/seedance-2.5 \
    [--duration 8] [--dry-run]
$PY tools/genra/video_ref.py frames CLIP.mp4 --at 0.9,1.5,3.0 --labels crouch,launch,impact   # key-pose sheet, no credits
$PY tools/genra/video_ref.py studio RENDER.png --out FRAME.jpg   # a transparent render on the studio floor, no credits
```

`frames` writes the chosen clip's beats as images, a labelled `key-poses.png` and `beats.json`. The brief attaches the
sheet and maps each beat to a game tick, the owner checks it before authoring, and the director compares the Unity render
with it beat by beat. The review renders the beats' camera (`review.beats.view`) in Unity too, so a front three-quarter
reference, as for Sir Bonk's normals, pairs with the same camera (before 2026-10-21 a view outside the filmstrip views
crashed the review).

- **The first frame is the character's own Unity render.** For example `unity_clip.py render ... --views
  profile,front-three-quarter-right --background 210,214,220`, composed onto a studio floor. The clips then show him, not
  a lookalike. A render with a prop the toolkit can't attach yet goes through `studio`: Sir Bonk's hobby horse comes
  from the transparent first frames of `tools/blender/bonk_rig/horse_review.eval.cs`.
- **The clips are storyboards, not animation.** Use them for poses, silhouettes and ideas, as key-pose images in a brief
  and in the director's side-by-side. Generated video floats, morphs props and runs 5-15 s against moves of half a
  second. It is never motion capture: the owner wants hand-made, cartoony motion.
- **Keys and cost.** The key comes from `$OPENROUTER_API_KEY` or `~/.config/sloparena/openrouter.key`, never the
  repository. Every clip spends credits; `GET /api/v1/videos/models` lists the prices. Seedance 2.5 at 720p cost $1.17
  for 8 s but $2.32 for 10 s in the recorded 2026-10-21 tests. Check current prices and supported durations, then
  choose the shortest take that answers the movement question. What Sir Bonk's Joust spent at each stage, and what the
  video gave it, is in the [workflow guide](../../docs/characters/genra-animation-workflow.md#choose-visual-references).
- **One take per shot, no repeats.** Four Joust takes that each opened with the same mount and gallop before their
  own ending read to the owner as "4 times the same anim" (2026-10-21). Shoot a shared opening once; start each
  ending's take where it differs (a first frame already in the gallop), keep it short, and ask before running several.
- **Historical comparison (2026-10-21).** Sir Bonk's E1 leap, side view, 8 s from his own render, cost $5.20 for five models
  (`artifacts/video-refs/sir-bonk-e1/`):
  - Seedance 2.5 was the owner's best overall: personality, technically good, smooth. It remains the preferred
    video model when movement exploration is needed; new pose references default to images.
  - Wan 3.0 was a bit bland but solid, with personality in the sword digging. The cheap alternative.
  - Kling 3.0 Pro respected neither the character nor the prompt.
  - Hailuo 3 Max grabbed the plank away from its handle.
  - Grok Imagine 1.5 was the worst overall, but the only one that deformed the body. That squash and stretch is what the
    owner wants from cartoon animation.

## Sandbox

`claude_runner.py` runs `claude -p` with the saved login and an empty scratch directory.
It loads no settings, MCP servers, skills or memory. Bash is pre-approved inside the
bubblewrap sandbox, which can read only the toolkit, the rig pack and the venv, can write
nowhere in the repository, and has no network. Do not pass `--add-dir` for repository
paths: add-dir directories become writable even under `denyWrite`.

### Promotion receipts and owner verdicts

Every `unity_clip.py promote` writes a `PromotionReceipt` JSON under `tools/genra/receipts/`,
keyed by the target path's SHA-256. Add these files to source control with the promoted clip.
One file per target avoids a shared append log. Repeating the same promotion preserves its verdict
and date. A changed source or clip archives the previous receipt under `receipts/history/` and clears the verdict.

New `write` reports record the source run, transforms, and exported asset hash. Promotion finds
these reports by source asset and hash. For older exports, pass `--run-dir RUN` and optionally
`--write-report REPORT.json`. Unrecorded transforms and final checks stay unknown.
A cut or retime uses its export phase check, whose flow metrics may be unknown.
The receipt snapshots the source check separately in `runs` and hashes the promoted target's bytes.

```sh
python3 tools/genra/receipts.py verdict Assets/CharacterPackages/fightguy/Animations/flip.anim good --note "Accepted in game"
python3 tools/genra/receipts.py summary
python3 tools/genra/receipts.py dry-run RUN --target Assets/CharacterPackages/fightguy/Animations/flip.anim --clip PATH.anim
```

Verdict accepts an exact target path or an unambiguous move name, with `good`, `redo`, or `rejected`.
The summary counts unique linked keys, takes, and revisions. Cost uses the ledger's recorded
list-price parser and traverses both `keys` and `revise` links without double-counting.
Missing cost or provenance makes the total `UNKNOWN` or partial, even when known costs sum to zero.
It excludes unlinked experiments and director-review costs.
First-take acceptance requires a `good` verdict and exactly one full take in the recorded chain.
Flow passes count full takes with recorded flow metrics, passing checks, and no flow warnings or failures.
Missing flow evidence stays unknown. These are chain-local counts, not a claim about all attempts for a move.
`dry-run` prints JSON without updating the manifest or inferring a historical owner verdict.
