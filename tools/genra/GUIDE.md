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
- `./genra check result.json --fast` checks numerical constraints during iteration,
  without mesh checks or automatic renders. Use full checks at pose checkpoints and delivery.
- `./genra check result.json` checks a saved clip against the brief and writes `sheet.png`
  (poses) and `motion.png` (paths). `./genra motion result.json --range 0-12` shows one stretch.
  `./genra sheet result.json --ticks 12,23 --views front,three-quarter-right --out look.png` renders
  chosen ticks; `reference:fall` renders a reference; `--every 2 --cols 8` makes a filmstrip of the whole clip.
  `./genra closeup result.json --ticks 0,11,22,33` frames each hand from the front, its outer side and above (held
  weapons hidden). `./genra curves result.json --reference reference:run` plots knee, thigh, elbow, arm, wrist, foot,
  pelvis and height curves over a reference's in grey. `./genra contacts result.json` lists the ticks each foot
  lands on. View PNGs with the Read tool.
- Work only in the current directory. The rig is already summarised below; do not explore
  the toolkit source unless something fails.

## Visual direction

The brief's attached pose sheet may be image-generated or extracted from a video. Read
`image_notes` and map its labelled beats to the brief's ticks. Keep the measured rig,
entry/exit poses and timing contract. Carry the reference's foot contacts, weight shifts,
leading body part and limb/weapon arcs into the blocking; keep grips and proportions
consistent. Add breakdowns to explain the travel between poses. Inspect paths and timed
playback as soon as transitions exist: matching the stills does not establish timing,
weight or continuity. Repair the responsible poses and curves within the brief.

## Load

```python
from genra.brief import Brief
from genra.clip import Clip
from genra.solve import two_bone, solve, fist, two_hand_grip, hold_grip, track, pin
from genra.checks import check_clip, format_report
from genra.render import contact_sheet
b = Brief('brief.json'); rig = b.rig
entry = b.entry_pose          # tick-0 Pose and the exit pose; a free entry only sets the frame
end = b.exit_pose             # the exit pose: the entry, or a brief's exit.reference (the clip played next)
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
  each target takes an optional `'weight'`. `wrists=45` makes a hand among the joints cost past 45 degrees off its
  forearm, so the solver turns the forearm, elbow and shoulder instead: use it rather than searching poses for
  straight wrists. `seed=K[previous_key]` starts from the neighbouring key's solution, so both keys stay on one
  elbow and roll solution; keys solved separately from one start can flip between them.
- `p.move_hips([r, f, u])` shifts the hips by an offset in metres. The whole body above the
  feet moves with them: re-solve planted feet with `two_bone`.
- `fist(p, 'Left', amount=1.0, thumb=1.0)` closes a hand toward its palm.
- `p2, report = two_hand_grip(p, blade=[r, f, u], hilt=[r, f, u])` holds the weapon with both
  hands the way the character's own clips do. The left hand lands exactly on the measured grip,
  the blade points along `blade` (hilt to tip), and the hilt goes as close to `hilt` as both
  arms can hold it (`report['hiltFromRequestMetres']`). Use it for every two-handed key; after
  `Clip.from_keys`, `hold_grip(clip, first, last)` keeps the left hand on the grip between keys. Both keep the
  wrists under about 45 degrees; `roll=True` also lets the left hand turn about the handle.
- `two_hand_grip(..., hold='loose', elbows=True)`, for a brief with `"two_hands_hold": "loose"`: the left fist only
  has to sit on the handle beside the right hand (within 3 cm of its line, up to 40 degrees of tilt, any turn about
  it, 8-16 cm along it from the right hand on Sir Bonk's plank), and both elbows hang below the shoulders and the
  hands (`elbows_down`, started from `drop_elbows`). The report then gives `leftHandOffHandleMetres` and
  `leftHandTiltDegrees`. The exact grip leaves the arms one way to reach it, which can wing an elbow up over the
  shoulder (Sir Bonk's Barn Door, 2026-10-22); the loose hold lets them take their natural path. `hold_grip`,
  `carry` and the check take the same `hold`.
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

## Contacts

Interpolating keys lets hands slide off a handle and planted points drift. Hold contacts between keys per tick:

- `track(clip, first, last, joints, targets)` re-solves the joints on every tick. Each tick starts from and stays
  near the previous tick's solution, so the elbow and roll stay continuous; `targets` can be a function
  `(tick, pose) -> list`. It takes a few seconds for 30 ticks.
- `pin(clip, first, last, 'bladeEnd', grip='roll')` keeps a point where it is at `first` while the keyed body moves.
  The arm that carries it follows, and `grip` keeps the other hand on the weapon too. `extra(tick, pose)` adds
  targets, such as a soft hand position that steers how far the handle rocks.
- Pin what is physically fixed. A weapon sits rigidly in its hand, so a stuck weapon pins its tip, not the hand:
  pinning the hand freezes its orientation, and the wrists fold as the body moves around it. A tip pin lets the
  handle rock with the body and the yanks.
- Key the first and last tick with the same contact (`two_hand_grip`, or the pin's target), so the tracked
  stretch starts and ends where the keys are.
- `hold_grip(clip, first, last)` is `track` for the left hand on the grip.
- **Two-handed swings: the body turns the weapon.** In a big two-handed swing the arms and weapon move almost as
  one piece with the chest; most of the sweep comes from the hips and chest turning. Key it that way (the weapon's
  direction relative to the chest changes far less than its direction in the world), solve the two-handed keys in
  order with `two_hand_grip(..., seed=K[previous])`, then `carry(clip, keys)`: it interpolates the weapon in the
  chest's frame and solves both arms on every tick between the keys. Its `landing` report gives, per key, how far the
  arms arrive from that key's own pose; tens of degrees means the keys hold the arms on different solutions, so
  re-solve that key from its neighbour instead of searching. Interpolating each arm joint on its own rolls the off
  hand round the handle (Sir Bonk's Barn Door: 163 degrees in three ticks).
  `carry(clip, keys, hold='loose', elbows=True, both_ways=True)` also solves each stretch backward from the next key
  and blends the two passes, so every tick answers to the key after it too: on Barn Door's keys, forward-only arms
  reached the next key up to 126 degrees off, both ways at most 33 (the fast load-to-swing tick). The weapon's
  direction and roll are the swing and stay firm; `path` (default 3 per metre) weights its interpolated hilt, and 1
  lets the hilt give way to the arms (Barn Door: the same arms, the hilt up to 11 cm off its path, so 3 stayed).

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
- Flips fail too: an arm or leg bone rolling about its own length by more than 60 degrees in one tick or 120 in
  three, even in a fast window. A flip is not speed: two neighbouring keys landed on different elbow or roll
  solutions. Solve the key from its neighbour (`seed=`), or hold the stretch with `track`/`pin`; slowing the motion
  only shows the roll for longer.
- `clip.positions('LeftHand')` gives a (ticks, 3) path; `clip.pose(t)` gives a Pose;
  `clip.angular_speed()` gives degrees per tick.

## Life (secondary motion)

A loop or big move reads robotic when the limbs work but the body around them stays rigid or rides the hips in one
piece. The check warns about that (`life`). Treat those warnings like failures for loops (idle, run, walk) and big
moves. Motion travels through the body:

- **Pelvis:** in steps it turns 5-10 degrees toward the forward leg, tilts 3-6 degrees down on the swing-leg side
  and shifts 2-5 cm over the stance foot.
- **Chest:** counter-rotates against the pelvis (5-12 degrees). It folds a few degrees forward into each landing to
  absorb the drop and straightens on the push-off.
- **Head:** steadier than the body. It partly counters the chest's turn and trails the chest by 2-3 ticks. Its bob
  is smaller and later than the hips'.
- **Collarbones:** lift and roll 3-8 degrees with an arm that swings or carries.
- **Arms:**
  - Swing in arcs: the elbow leads, then the forearm and hand trail.
  - A carrying arm still bounces, and what it holds settles a few ticks after the body.
  - On a big character, keep the elbows and hands clear of the torso; a swing that hugs the belly reads stiff.
- **Feet:** the heel lands, the foot rolls flat, and the toes bend 20-40 degrees at push-off. The ankle flexes
  through the stance.
- **Asymmetry:** the two sides need not mirror exactly. Small differences read alive.

Key the big beats first. Then add follow-through with
`clip = clip.overlap({'LeftForeArm': 1.5, 'LeftHand': 3, 'Neck': 1, 'Head': 2.5}, cycle=(0, 44))`. Each listed
joint's local rotation plays that many ticks late; delays add up along a chain, and in a loop they wrap inside the
cycle. Re-check planted feet, held weapons and clearances afterwards.

## Flow (one-shot moves)

Hand-made attacks never stop much of the body at once and never park it. Keying a pose for the whole body on one
tick, interpolating, and repeating a pose to hold it does both: every joint that turns back halts on the key's tick,
and holds go dead. The check measures it against 44 hand-made attacks (`flow` warnings; `./genra` reports the
numbers) and the review shows a speed map. Treat the warnings like failures:

- **Stagger the arrivals.** At a big pose (the end of a strike, a landing), the hips arrive first, the chest a tick
  later, the arms one to two ticks after it and the head last. Key those parts at their own ticks, or `Clip.overlap`
  the joints that carry no contact (neck, head, the free arm, 1-2.5 ticks). The approved-key check lets the body meet
  its key pose up to two ticks early or late; a weapon keeps its tick.
- **Moving holds.** Never key the same pose at both ends of a hold, except the exit hold the brief fixes. Key the
  hold's end a few degrees on from its start (the chest still sinking and twisting, the head settling, the free arm
  drifting), so the body moves slowly through the hold instead of stopping in it. A hold that goes out and back
  stops at its turn: drift one way.
- **Recover in waves.** Leaving a pose, the hips lead, the chest and arms follow, and the head comes last.
- **A settle that gives.** Before the exit hold, ease into the final pose with a small overshoot in the chest and the
  weapon, not by sliding into it.

## Form

The check measures anatomy and gait the way the roster's approved clips do:

- **Wrists:** a hand stays near the line of its forearm. Approved clips bend it under 45 degrees in loops and under 95
  anywhere; past 105 fails. Aim a fist or a grip by turning the forearm and elbow, never by folding the wrist:
  `solve(..., wrists=45)` and the grip and contact helpers do that.
- **Two hands:** on a brief's `two_hands` ticks the other hand closes around the handle beyond the hilt. A hand
  floating beside the handle fails, even when the two hands are close together. With `"two_hands_hold": "loose"` the
  check measures the loose hold instead: the fist within its slack and tilt (plus 2 cm and 15 degrees), on the
  grip's side of the weapon hand.
- **Gaits** (looping clips whose feet step in turn):
  - the knees straighten to 6-36 degrees at push-off;
  - the two steps differ (the roster's legs differ by 4-8 degrees half a cycle apart);
  - the knee, thigh, elbow, arm, pelvis and chest curves are not plain sine waves;
  - a forearm keeps a gap from the torso on most of the cycle.

`./genra curves` shows all of these against a reference.

## Check and look

- While solving numerical problems, use `./genra check result.json --fast`, or
  `check_clip(clip, b.check_spec(), mode='fast')`. This keeps clock, entry/exit, planted
  feet, anatomy, Humanoid limits, grip, approved keys, snaps, flips and numeric targets.
  It skips mesh clearances, face visibility and the gait's forearm-to-torso mesh gap;
  the CLI also skips automatic sheet/motion renders. `FAST PASS` is iteration evidence
  only. Run `./genra check result.json` and inspect the renders at a meaningful pose
  checkpoint and before delivery. Do not rerun full geometry checks after every
  numerical adjustment; do rerun them after changes to poses or contacts are ready to judge.
- `print(format_report(check_clip(clip, b.check_spec())))` checks the clock, the exact
  entry and exit poses, fixed bone lengths and scales (only the hips translate), every knee,
  elbow and finger bend direction, joint and finger rotations the game can play, snaps and flips, region clearances,
  a face hidden behind the arms, a second hand off the handle, approved key poses, and the brief's numeric targets.
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
