# genra toolkit guide

Author the animation in Python with `genra`: rig-aware poses, anatomical IK, checks and
renders for the actual character. Units are metres and 60 Hz ticks. Positions and
directions use the **character frame** `[right, front, up]`, in metres from the entry
pose's hips; front is the fighter's facing (where it moves, attacks and finds its opponent)
and up is world up. A fighting stance turns the hips and chest away from front, by up to 90°,
so read where the chest points from the pose, not from front. The fighter itself never moves;
the hips can (crouch, lunge, weight shift), and Unity keeps that motion in the pose.

## Run

- `./py script.py` runs Python with `genra` importable (numpy, scipy available).
- `./genra check result.json` checks a saved clip against the brief and writes `sheet.png`
  (poses) and `motion.png` (paths). `./genra motion result.json --range 0-12` shows one stretch.
  `./genra sheet result.json --ticks 12,23 --views front,three-quarter-right --out look.png` renders
  chosen ticks; `reference:fall` renders a reference. View PNGs with the Read tool.
- Work only in the current directory. The rig is already summarised below; do not explore
  the toolkit source unless something fails.

## Load

```python
from genra.brief import Brief
from genra.clip import Clip
from genra.solve import two_bone, solve, fist, two_hand_grip, hold_grip
from genra.checks import check_clip, format_report
from genra.render import contact_sheet
b = Brief('brief.json'); rig = b.rig
entry = b.entry_pose          # tick-0 Pose and the exit pose; a free entry only sets the frame
fall = b.reference('fall')    # reference Clip on the same skeleton
```

## Poses (edits happen in place; copy first)

- `p = entry.copy()`. `p.pos('LeftHand')` gives the char-frame position, `p.describe()` key joints.
- `p.rotate(joint, [r, f, u], degrees)` rotates a joint and its subtree about a char-frame
  axis through the joint (`frame='local'` uses the joint's own axes).
- `p.aim(joint, child, [r, f, u])` points the joint-to-child segment along a direction.
- `p.set_hinge(name, degrees)` sets anatomical flexion from the straight bind pose for
  `LeftKnee`, `RightElbow`, `LeftIndexKnuckle`, `RightLittleMiddle` and so on. It always
  bends the correct way.
- `miss = two_bone(p, 'Left', 'Arm'|'Leg', target=[r, f, u], pole=[r, f, u])` places the
  hand or foot on the target. The elbow or knee bends only on its hinge and points toward
  `pole`. A non-zero `miss` (metres) means out of reach: move the shoulder, spine or target.
- `p2, report = solve(p, joints, targets)` is least-squares IK over the listed joints.
  Hinge joints keep one degree of freedom. Targets:
  `{'pos': 'RightHand', 'at': [r, f, u]}`, `{'dir': ['RightHand', 'RightHandMiddle1'], 'along': [...]}`,
  `{'palm': 'Right', 'along': [...]}`, `{'axis': joint, 'local': [x, y, z], 'along': [...]}`;
  each target takes an optional `'weight'`.
- `p.move_hips([r, f, u])` shifts the hips by an offset in metres. The whole body above the
  feet moves with them: re-solve planted feet with `two_bone`.
- `fist(p, 'Left', amount=1.0, thumb=1.0)` closes a hand toward its palm.
- `p2, report = two_hand_grip(p, blade=[r, f, u], hilt=[r, f, u])` holds the weapon with both
  hands the way the character's own clips do. The left hand lands exactly on the measured grip,
  the blade points along `blade` (hilt to tip), and the hilt goes as close to `hilt` as both
  arms can hold it (`report['hiltFromRequestMetres']`). Use it for every two-handed key; after
  `Clip.from_keys`, `hold_grip(clip, first, last)` keeps the left hand on the grip between keys.
- The game plays the spine, neck, head, collarbones, upper arms and thighs only through their Humanoid
  muscles: up to about 2.5 times Unity's default ranges, no twist past 85 degrees, and a collarbone never
  rolls about its own length. `solve`, `two_hand_grip` and `hold_grip` keep to these limits; `rotate` does
  not, and the check fails a pose past them. If `two_hand_grip` reports the left hand off the grip, that hold
  is out of reach within them: bring the hilt closer to the chest or turn the chest toward it.
- The game plays fingers, thumbs and toes only as rotations about their own joint axes.
  `set_hinge` and `fist` stay on them; after any other edit of a digit (`rotate`, `solve`),
  call `p.snap_to_humanoid()`.
- `p.palm_normal('Left')` gives the char-frame direction the palm faces.

Block poses top-down: spine and head, then arms with `two_bone`/`solve`, then hands, then
legs. Re-check positions after each step; spine changes move every limb.

## Timeline

- `clip = Clip.from_keys({0: entry, 12: load, 18: load, 23: hit, ..., 57: entry, 62: entry}, n_ticks=63)`
  uses monotone cubic interpolation per joint (smooth, no overshoot). Keying the same pose
  at both ends holds it. `method='slerp', ease={tick: 'in'|'out'|'inout'}` eases one segment.
- Add breakdown keys for arcs and overlap (lead with the torso, then let the arms follow).
- Key the path, not just the ends. Between two distant keys, interpolation takes the shortest
  joint rotation, which can swing an arm across the face or behind the body. Put breakdown keys
  where the hands and elbows should pass: a support hand meets the grip in front of the chest,
  then both hands lift together.
- Spins and flips turn the hips: `p.rotate('Hips', [0, 0, 1], -90)` turns the body a quarter to
  its right and `p.rotate('Hips', [1, 0, 0], -90)` pitches it a quarter forward (positive degrees
  turn left and lean back). Between two keys a joint turns the shorter way, so key every quarter
  turn or closer; the spline keeps the turn smooth across keys, any number of turns.
- Loops: `Clip.from_keys(keys, n_ticks, cycle=(first, last))` makes ticks `first`..`last` a seamless
  cycle. Key the same pose at both ends; the keys between follow a periodic spline, so the motion keeps its
  speed when the game wraps from `last` back to `first` (it can overshoot a little between keys, which reads
  as follow-through). Keys before `first` form a build-up that arrives with the loop's velocity. Offset the
  torso, head, arm and leg keys in time so they don't all peak on the same tick, and keep full turns out of
  a loop. The check fails a seam that doesn't close or that jerks harder than the loop itself.
- Two-handed holds read best where the arms frame the head rather than cross the face. The check
  warns when arms or a weapon hide the face from the front for several ticks; a character whose
  head sits below its shoulders hides it in any overhead hold, so judge that from the renders.
- Snaps fail the check: more than 35 degrees per tick in one joint outside the brief's fast
  windows, or more than 90 inside them (a one-frame jump, not speed). Spread a fast strike over
  its window with breakdown keys, and ease out of the entry pose instead of jumping to the first key.
- `clip.positions('LeftHand')` gives a (ticks, 3) path; `clip.pose(t)` gives a Pose;
  `clip.angular_speed()` gives degrees per tick.

## Check and look

- `print(format_report(check_clip(clip, b.check_spec())))` checks the clock, the exact
  entry and exit poses, fixed bone lengths and scales (only the hips translate), every knee,
  elbow and finger bend direction, joint and finger rotations the game can play, snaps, region clearances,
  a face hidden behind the arms, and the brief's numeric targets.
- `contact_sheet(clip, ticks, views=b.review_views, path='sheet.png')` renders flat-shaded
  frames. `b.review_views` is profile, front and the three-quarter view from the striking
  side: behind that shoulder, 35 degrees to the side and 20 degrees up. Other views:
  `three-quarter-left`/`-right`, `back`, `top`, and `gameplay` (the match camera, straight
  behind). Colours: left upper arm, forearm and hand are blue, dark blue and yellow; right
  ones are red, dark red and orange; legs are green and purple; weapons are pale grey.
- Report details: `report['failures']` and `report['warnings']` are dicts with `check`,
  `message` and fields such as `joint`, `ticks`, `pair` and `tick`.
  `report['metrics']['closestMetres']` maps every region pair (`'LeftHand-Head'`, ...) to
  `[metres, tick]`. For one pose: `skin = Skin(rig)` (from `genra.skin`), then
  `skin.clearances(pose.world())`. Pass `skin=skin` to `check_clip` to reuse it. Regions
  are Head, Torso, Left/Right UpperArm, ForeArm, Hand, Leg and Foot.
- `motion.png` has two rows (entry to contact, contact to the end) of ghosted poses with the
  paths of the hands (yellow left, orange right), elbows (blue left, red right) and blade tip
  (white), one dot per tick; right-hand ticks are numbered. Spacing is speed: a lone long
  segment is a snap. Paths should be clean arcs that suit the action, with hands travelling in
  front of the body and never crossing the face, unless the brief asks for it.
- Fix every failure. Before saving, look at the contact pose and the hands up close:
  `./genra sheet result.json --ticks 23 --views front,profile --size 400 --span 0.8 --center 0,0.2,-0.1 --out close.png`
  (`--center` is the char-frame point at the middle of the image; without `--span`/`--center` the
  framing fits the shown poses).

## Output contract

Save the final clip with `clip.save('result.json')` and run `./genra check result.json`.
Reply in at most 8 lines: the approach, the check result, and any remaining weakness.
Do not paste animation data or code into the reply.
