# genra toolkit guide

Author with the supplied `author.py` and toolkit. Units are metres and 60 Hz ticks.
Positions and directions use the character frame `[right, front, up]`, measured from the entry hips.
Front is the fighter's facing. Read the chest's direction from the pose because a stance can turn it away from front.
Keep the actor fixed. Only the hips may translate for crouches, lunges, and weight shifts.

## Run and save

- `./py author.py` runs Python with genra, numpy, and scipy available.
- Read `API.md` for live signatures. `Pose` lives in `genra.clip`. Use `Pose.copy()` rather than deepcopy on a USD rig.
- Work in the current directory. Use the measured rig summary. Read toolkit source only if something fails.
- Save `clip.save('result.json')` before checks, even with unresolved failures. Saves are atomic.
  The runner archives changed, loadable checkpoints. An unchanged starting checkpoint is not a finished take.
- A keys job authors only story poses. Its check separates key-pose failures from provisional transition risks.
  Do not polish transitions during blocking. A blocking pass cannot qualify as final motion.

## Load and pose

```python
from genra.brief import Brief
from genra.clip import Clip, Pose
from genra.flow import flow, Accent
from genra.solve import solve, two_bone, fist
from genra.checks import check_clip, format_report
b = Brief('brief.json')
rig = b.rig
clip = Clip.load(rig, 'result.json', frame=b.entry_frame)
p = b.entry_pose.copy()
pose, report = solve(p, ['RightArm', 'RightForeArm'],
                     [{'pos': 'RightHand', 'at': [0.3, 0.4, 0.5]}])
```

`b.exit_pose` is the required exit. `b.reference('fall')` loads a same-rig reference when the brief defines it.
`clip.pose(tick)` returns a pose. Install an edited copy with `clip.set_pose(tick, pose)`.

- `p.pos('LeftHand')` returns its character-frame position.
- `p.rotate(joint, [r, f, u], degrees)` rotates the subtree about a character-frame axis.
  `frame='local'` uses the joint's own axes. `p.aim(joint, child, direction)` aims a segment.
- `p.set_hinge('RightKnee', degrees)` sets anatomical flexion from straight. Elbows and finger hinges work too.
- `miss = two_bone(p, 'Left', 'Arm', target=position, pole=direction)` edits the pose.
  Use `'Leg'` for a foot. A nonzero miss in metres means the target is out of reach.
- `pose, report = solve(p, joints, targets, wrists=45, seed=previous_pose)` returns a pose and a list of diagnostic dicts.
  Targets include `{'pos': 'RightHand', 'at': position}`, `{'palm': 'Right', 'along': direction}`,
  and `{'dir': ['RightHand', 'RightHandMiddle1'], 'along': direction}`.
  Seed from the neighbouring key to keep the elbow and roll solution continuous.
- `p.move_hips([r, f, u])` offsets the hips. Re-solve any planted feet afterwards.
- `fist(p, 'Left', amount=1.0, thumb=1.0)` closes the hand. `p.palm_normal('Left')` returns its facing.

Pose the spine and head, then arms, hands, and legs. Re-check limb positions after spine edits.
The game plays the spine, neck, head, collarbones, upper arms, and thighs through Humanoid muscles.
A collarbone can lift or raise the shoulder but never rolls about its own length.
`solve` respects those limits. `rotate` can exceed them. Flow composes in muscle coordinates, so it cannot add collarbone roll.
Fingers, thumbs, and toes rotate only about their own axes. `set_hinge` and `fist` respect those axes.
After another digit edit, call `p.snap_to_humanoid()`.

## Block the path

Map the attached reference beats to the brief's ticks. Preserve the clock, entry, exit, and approved intent.
Match weight, silhouette, and the leading body part. Check timed playback as soon as transitions exist.

For blocking, `Clip.from_keys(keys, n_ticks)` uses monotone cubic interpolation.
Identical poses at two keys create a hold. A joint that reverses at a key stops there.
Add breakdown keys to explain the path. Shortest-path joint interpolation can cross the face or pass behind the body.
For spins, key every quarter turn or closer. Hips turn right with `p.rotate('Hips', [0, 0, 1], -90)`
and pitch forward with `p.rotate('Hips', [1, 0, 0], -90)`.
A fast window permits speed, not an elbow or limb rolling onto a different solution.

## Flow for one-shots
<!-- guide: one-shot -->

<a id="flow-one-shot-moves"></a>

Start with `clip, report = flow(b)`, then `print(report)` and `clip.save('result.json')`.
With approved keys, the runner already seeds this result and writes `flow.txt`.
Use the toolkit instead of rebuilding interpolation, hinge curves, or settling in scratch helpers.
Normals default to a plain, short recovery. Put personality in the move. Specials can carry more acting.

### Passing through keys

`flow(b)` lands every approved key exactly on its tick. It uses Catmull-Rom tangents, knee and elbow turnover,
arcs at reversal keys, ordered settle, and speed-driven finger curl. The entry and exit contract still applies.
Without approved keys, pass your authored poses as `flow(b, keys=keys)`.
Flow refuses an ambiguous turn near 180 degrees between keys and asks for a breakdown key.
Add a pose that states the intended turn. Do not force a winding direction by changing quaternion signs.

First inspect the default take. Then change only the dials needed for the move.
Defaults come from `PRESETS` by brief purpose. `tangents` scales through-key slopes.
`turnover` is hinge speed in degrees per tick at reversals. `arcs` scales the pass-around at reversal keys.
`settle` scales ordered recovery, and `fingers` scales curl with arm speed.
`lag` and `drag` are opt-in. The replay found that key-exact lag can add stops.

```python
clip, report = flow(b, arcs=0.5, settle=0.25)
print(report)
clip.save('result.json')
```

For owner-directed gestures, use `Accent`. Its motion returns to zero on the surrounding keys.
For example, `Accent('spine', tick=20, degrees=3, axis=(1, 0, 0), wave=1)` adds an arch between keys.
Choose the tick and amount from this move's direction, then pass `accents=[accent]` to `flow`.
Stiffness is a valid choice. `holds={'*': [(12, 18)]}` preserves a whole-body hold.
Use a joint name instead of `'*'` for a local hold. Holds suppress layers and stop on their keys.
Do not add sway to clear a score or smooth away an impact.

Read `report.flow_before` and `report.flow_after` as advisory measurements.
`report.approved` gives drift at each approved tick. `report.clamped` and `report.lost` identify motion limited by Humanoid axes.
`report.holds`, `report.layers`, and `report.notes` explain what flow retained or changed.
Inspect the motion at game speed. Flow numbers do not establish quality.
Run contacts after flow with `carry`, `pin`, `track`, or `hold_grip` as appropriate, then audit keys again with `key_drift`.

The retained [Flip Kick example](examples/fightguy-flip-kick-flow/) records the earlier hand-authored pass.
It is background for the toolkit's mechanics, not a recipe to copy into a new move.

## Life in loops and idles
<!-- guide: loop idle locomotion -->

Use `clip, report = flow(b)` with approved keys, or pass your own `keys`.
The purpose preset keeps settle off for loops. Inspect `print(report)` before changing dials.
A calm idle needs a slow speed budget and at most one slow gesture.
Keep deliberate stiffness with `holds`, and treat life and flow numbers as advisory.

For a keyed loop, `Clip.from_keys(keys, n_ticks, cycle=(first, last))` closes the seam with periodic tangents.
The endpoints must match. Keys before `first` form the build-up.
For secondary motion, use `clip = clip.overlap({'Neck': 1, 'Head': 2.5}, cycle=(first, last), preserve_ticks=keys)`.
Delays add along a chain and wrap in the cycle. Preserve approved ticks and re-check contacts and the seam afterwards.
Collarbones can lift or raise with the arms. Flow uses muscle coordinates and cannot roll them about their length.
Do not add a gesture merely to reduce frozen-joint counts.

## Gaits
<!-- guide: locomotion loop -->

For stepping cycles, shift weight over the stance foot. Counter-turn the chest against the pelvis.
The head can remain steadier. Let the heel land, roll the foot flat, and bend the toes at push-off.
Keep the elbows and hands clear of the torso. Use purposeful asymmetry rather than plain sine waves.
The gait checks inspect knee extension at push-off, differences between steps, curves, and forearm clearance.
Compare `./genra curves result.json --reference reference:run` against a same-rig reference.

## Planted feet
<!-- guide: planted -->

Save the required foot positions before editing the hips or spine.
After flow, re-solve planted legs with `two_bone` on each contact tick.
Keep the pole direction consistent. A reach miss needs a torso or target adjustment.
Use `track` for a contact target that must remain fixed through an interval.
Check foot drift after overlap or any later body edit.

## Contacts and continuity
<!-- guide: contact -->

Run contacts after flow. `report = track(clip, first, last, joints, targets)` edits the clip and returns a dict.
Targets can be a function `(tick, pose) -> list`. It seeds each tick from its neighbour.
`pin(clip, first, last, 'bladeEnd', grip='roll')` keeps that point where it was at `first`.
Pin the physically fixed point. A stuck weapon pins its tip so the handle can rock.
Pinning the carrying hand freezes its orientation and can fold the wrists as the body moves.
Key both ends with the same contact. Re-check approved-key drift after solving.

When a grip or contact check fails, save and run `./genra continuity result.json` before retrying. Inspect the implicated interval's landing, roll, limit and reach diagnostics. Use `--keys a,b --replay-carry` for one bounded solver replay. Large key turns indicate risks, not confirmed flips. Save after each verified fix. If the runner plateaus, continue from the summary's nextTask.

## Two-hand grips
<!-- guide: two_hands -->

Import `two_hand_grip`, `carry`, and `hold_grip` from `genra.solve`.
`pose, report = two_hand_grip(p, blade=direction, hilt=position, seed=previous_pose)` solves a key.
For `two_hands_hold: loose`, pass `hold='loose'` and `elbows=True`.
The left fist may slide along the handle within the loose hold's tilt and distance bounds.
An exact grip places it on the measured grip. A nearby hand floating beside the handle fails.

Let the chest turn the weapon in a two-handed swing. Solve keys in order from their neighbours.
After flow, `report = carry(clip, keys, both_ways=True)` interpolates the weapon in the chest frame and solves both arms.
For loose holds, also pass `hold='loose', elbows=True`. Inspect its `landing` report before another solve.
`report = hold_grip(clip, first, last)` maintains the off-hand contact alone.
These functions edit the clip. They return reports, not replacement clips.

## Weapon paths
<!-- guide: weapon -->

Read the weapon direction from hilt to tip. Keep wrists near the forearm line by turning the chest, shoulders, and elbows.
Do not fold the wrist to aim the weapon. Inspect the face and both hands when the weapon crosses the body.
Re-check held weapons and clearances after secondary motion.

## Weapon arcs
<!-- guide: arc -->

For a weapon normal, its swept capsule during the active window determines who it hits.
The brief's `arc` targets count defender positions touched against game hurtboxes.
Sweep through the whole window. A parked weapon covers a narrow column.
`./genra check result.json` reports coverage. `hitarc.py --out arc.png` draws the hit region.

## Check and look

Use `./genra check result.json --fast` during numerical iteration.
It keeps timing, entry and exit, anatomy, contacts, approved keys, snaps, flips, and targets.
It skips mesh checks and automatic renders. Run full `./genra check result.json` at pose checkpoints and delivery.
The full command writes `sheet.png` and `motion.png`. View PNGs with the Read tool.

- `./genra sheet result.json --ticks 12,23 --views front,profile --out look.png` shows selected poses.
- `./genra closeup result.json --ticks 12,23` frames the hands with held weapons hidden.
- `./genra motion result.json --range 0-12` shows a bounded path.
- `./genra curves result.json` plots joint curves. `./genra contacts result.json` lists foot landings.
- `clip.positions('LeftHand')` returns a path. `clip.angular_speed()` returns degrees per tick.

The check's `report['failures']` and `report['warnings']` contain dicts with `check`, `message`, and implicated joints or ticks.
A fast pass is iteration evidence only. Inspect timed playback and full renders before delivery.
Wrists must stay near the forearm line. Humanoid limits and digit axes must remain playable.
Repair snaps in the responsible interval. A large key turn alone does not prove a flip.
Save each verified fix. Keep the best take and report unresolved constraints if the runner stops.

## Output contract

Save `clip.save('result.json')` and run `./genra check result.json`.
Reply in at most 8 lines with the approach, check result, and remaining weakness.
Do not paste animation data or code into the reply.
