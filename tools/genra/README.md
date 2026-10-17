# genra animation toolkit

Rig-aware authoring for the [GenRA animation workflow](../../docs/characters/genra-animation-workflow.md):
Opus 5.5 authors motion in Python against the actual skinned rig, checks it and looks
at renders. The runner writes USD, a contact sheet and a preview video, and `unity_clip.py`
turns the result into a Humanoid clip on the character's own prefab.

| Module | Purpose |
| --- | --- |
| `rig.py` | USD skeleton, forward kinematics (matches UsdSkel to 1e-7 m), bind-pose locals, anatomical hinges for knees, elbows and fingers, Unity's Humanoid muscle axes |
| `clip.py` | `Pose` edits in the character frame (including hip shifts), `Clip` keyframes with monotone or eased interpolation, the `genra-clip-1` file |
| `solve.py` | Two-bone limb IK on anatomical hinges (elbows and knees kept in range), least-squares targets, fists, two-handed grips |
| `skin.py` | UsdSkel skinning, body regions, clearance queries |
| `checks.py` | Clock, entry/exit, bone lengths and scales, hinge direction, finger poses Unity can play, snaps, clearances, a hidden face, brief targets |
| `render.py` | Fast software contact sheets, motion paths and MP4 previews, no GPU needed; how much of the strike each view shows |
| `usdio.py` | Writes a clip into a copy of the rig USD (lossless quaternions, textures copied) |
| `brief.py`, `rigpack.py` | Animation briefs and hashed rig packs |
| `run.py`, `claude_runner.py` | One sandboxed authoring job, end to end |
| `unity_rigpack.py`, `unity_clip.py` | Rig packs from the game's own data; the way back to a Unity clip, measured in Unity |
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
$PY -m unittest discover -s tools/genra -p 'test_*.py'      # 20 tests; skip without rig packs
$PY tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json --dry-run
$PY tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json --probe   # ~$0.06 sandbox check
$PY tools/genra/run.py tools/genra/benchmarks/manki-aerial-double-fist.json          # one authoring job
cd tools && $PY -m genra check RESULT.json --brief genra/benchmarks/manki-aerial-double-fist.json
```

A run writes `artifacts/genra-runs/<id>/`: brief, prompt, CLI command and settings, event
stream, tool audit, usage summary, the model's scratch files, `check.txt`, `sheet.png`,
`motion.png`, `preview.mp4` and `usd/<name>.usda`.

- `motion.png` shows ghosted poses with the paths of the hands, elbows and blade tip, one dot
  per tick, from entry to contact and from contact to the end. The model gets it with every
  `./genra check`, because stills alone hid a Bonk lift whose left arm swept across the face
  between two distant keys. `./genra motion --range a-b` shows one stretch.
- `check_clip` also warns when the character's own arms or weapon hide the face from the front
  for 4 or more ticks. That's a warning, not a failure, because Bonk's approved jump start does
  it for 17 ticks. A rerun of that Bonk lift fixed the path but held the sword with the left
  arm across the face for 7 ticks, which led to this warning.
- The preview shows the side and three-quarter views together, twice at real speed and once
  at quarter speed, with the tick and the active window labelled. It needs `ffmpeg`
  (`./genra video` renders one on demand). The model works in `/tmp/genra-runs/<id>/` through the `./py` and
`./genra` wrappers.

Review sheets show profile, front and a three-quarter rear view: behind the striking
shoulder, 35° to the side and 20° up. The match camera sits straight behind the fighter.
In the first five runs it showed only 0–24% of the striking hand or weapon at contact,
against 39–91% from the three-quarter view, so it is only an optional `gameplay` view.
When the brief names its `strike` joints, `check.txt` and `summary.json` record how much
of the striking part each view shows at contact. This is information, not a check.

## Rig packs and briefs

A rig pack holds `rig.usda`, `references/*.usda`, `textures/`, `images/` and a hash
manifest; the runner refuses changed files. Rig packs live under `artifacts/genra-rigpacks/`
and are local.

**Current characters** (`<package>-unity`) are built from the game's own data:

```bash
$PY tools/genra/unity_rigpack.py snapshot manki   # read-only eval_file through your own gateway lease
$PY tools/genra/unity_rigpack.py build manki      # USD rig pack + per-vertex validation
$PY tools/genra/unity_rigpack.py all manki fightguy bonk wibou
```

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
    `mapping.json` but not attached.
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
  Elbow and knee hinges keep their learned axes, because Unity's twist distribution turns
  their bend plane. Finger hinges use Unity's exact axes.

`manki-sr199` is the experiment-time Manki rig from 14 October, built through Blender. It is
used only by the aerial benchmark.

Briefs are JSON (see `brief.py`). The creative text stays verbatim, while the clock, fast
windows and numeric targets are what the checks enforce. `strike` lists the joints that
deliver the hit (`RightHand`, or `bladeEnd` for a weapon); a left-side strike moves the
review view behind the left shoulder. Targets should restate the
creative text, not add new requirements.

## Into Unity

`unity_clip.py` writes a toolkit result as a Humanoid `.anim` for the character's catalog
prefab, through the gateway's `eval_file` under your own lease
([Unity CLI](../../docs/contributing/unity-cli.md)):

```bash
$PY tools/genra/unity_clip.py roundtrip fightguy idle     # proof on an approved clip
$PY tools/genra/unity_clip.py write RESULT.json --brief BRIEF.json [--save | --asset Assets/...anim]
$PY tools/genra/unity_clip.py render --brief BRIEF.json --result RESULT.json \
    --clip new=Assets/Art/Experiments/GenRA/bonk/NAME.anim --clip current=anim.bonk.a1
```

- Mapped Humanoid bones become muscle and body curves. Unity's `GetHumanPose` is not an
  exact inverse (Manki's right-leg twist read back at half), so each chain's muscles are
  refined, together with the body, until `SetHumanPose` reproduces the toolkit's joint
  positions and orientations. Other joints (tails, deformers) keep exact local curves.
- Root motion is baked into the pose: `PlayerRenderer` plays clips through Animancer with
  `applyRootMotion` off. The hips may move; the fighter does not.
- Everything is measured as the game plays it (Animancer in a preview scene). `write`
  matches the body origin to the entry clip's playback at the entry tick, and reports
  joints and skinned vertices against the toolkit. Nothing is saved unless `--save` or
  `--asset` names a new path (default `Assets/Art/Experiments/GenRA/<package>/<name>.anim`),
  and nothing is bound.

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

## Sandbox

`claude_runner.py` runs `claude -p` with the saved login and an empty scratch directory.
It loads no settings, MCP servers, skills or memory. Bash is pre-approved inside the
bubblewrap sandbox, which can read only the toolkit, the rig pack and the venv, can write
nowhere in the repository, and has no network. Do not pass `--add-dir` for repository
paths: add-dir directories become writable even under `denyWrite`.
