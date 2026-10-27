# GenRA animation workflow

Use this workflow for new in-house character motion, including Manki's next moves.
It is the maintained procedure behind the successful GenRA experiments and Head
Slam. The [animation-authoring skill](../../skills/sloparena-animation-authoring/SKILL.md)
owns the general import, review and package rules; this guide records the
GenRA-specific preparation, conversion and repair steps.

The sequence is **brief → actual rig and timing → author poses → GenRA processing
→ actual-character preview → user approval → move integration and verification**.
Candidate work stays unbound until animation approval. Proposed move data stays
in notes until then, even when the task authorizes changing that data.

## Tooling and authorship

The working runs use GenRA revision
[`5f4ffb8ac846f91265c48e8bb65d702dca11b7a1`](https://github.com/MacPaw/GenRA/tree/5f4ffb8ac846f91265c48e8bb65d702dca11b7a1).
Keep that revision explicit when reusing the conversion baseline. The installed
checkout and Python environment are recorded in the
[initial experiment](../research/2026-10-14-genra-experiment.md); check their
availability before starting. Tool installation still requires approval under
[AGENTS.md](../../AGENTS.md).

**Opus 5.5 authors GenRA motion** (owner decision, 16 October 2026). On the same
prompt, its Manki aerial first pass had correct finger curl and a clearer
wind-up than the Codex pass; see the [provider A/B](../research/2026-10-16-genra-opus-ab.md).
Codex threads may prepare inputs, convert, preview and integrate, but they hand
pose authoring to Opus 5.5.

**Use the [genra toolkit](../../tools/genra/README.md).** `tools/genra/run.py <brief>`
runs one sandboxed Opus 5.5 job. The model authors poses in Python against the actual
skinned rig. It has character-frame poses, IK that bends knees, elbows and fingers only
on their anatomical hinges, monotone interpolation, automated checks, and software
renders it inspects. The runner re-checks the result and writes it into a copy of the
rig pack's USD, as lossless quaternions with no GenRA Euler round trip. On the Manki
aerial benchmark, four runs took 105–209 s and $0.37–0.68 at list price, and all passed
every check. The GenRA-prompt route took 25 minutes and $6.95 and failed two checks.
Inputs are a rig pack (rig, references, textures, images, hash manifest) and a brief
whose numeric targets restate its creative text.

Earlier routes, for the records below:
- **Codex-backed GenRA adapter:** the FightGuy and first Manki aerial runs produced
  sparse joint-space keys through GenRA's parser, interpolation and USD writer, with
  explicit same-rig references and automatic example selection disabled.
- **Session-authored poses:** Head Slam and Bonk were authored in the Codex session
  through the same writer.
- **Opus A/B:** reused the GenRA adapter with `claude -p`.

`claude_runner.py` has three sandbox lessons built in:
- **Pre-approve Bash.** In dontAsk mode it otherwise refuses heredocs and piped Python.
- **Lock down repository access.** Deny repository reads, then allow back only the
  toolkit, rig pack and venv.
- **Never `--add-dir` repository paths.** They become writable even under `denyWrite`.

Record the route actually used. GenRA processing alone does not mean a separate
model generated the motion. Reuse the task's authorized provider/tool route;
do not silently switch providers or add model calls. For a separate generation
or refinement request, record the selected model/settings, references, call and
time limits, raw response and usage. Preserve failures rather than retrying
without a bound. Never include credentials in prompts or evidence.

The toolkit, runner, tests and briefs are tracked. Rig packs and the Python environment
are local (`artifacts/`).

`tools/genra/unity_rigpack.py` builds rig packs for the current characters: Manki v3,
FightGuy v2, Bonk and Wibou, swords included. It snapshots the catalog prefab and its
clips through a read-only gateway lease, and checks the result against Unity's own
skinning, vertex by vertex. Use these packs instead of re-deriving rigs in Blender.

Conversion back to Unity (Humanoid hybrid clip, Animancer checks) still uses the per-run
helpers listed below. `mapping.json` maps each pack joint to its Unity transform path for
that step. Check that the helpers you need exist and adapt them in a fresh workspace.
Missing local tooling needs a concrete setup or handoff, not a claim that the pipeline ran.

## 1. Capture the brief and clock

Resolve the actual package, canonical slot, semantic animation ID, current
catalog prefab, Avatar and adjacent idle/fall clips. A reference image describes
the motion; the current rig and playback path determine how to author it.

Record a short brief before posing. This is a task note, not a new package schema:

```text
Package / canonical slot / semantic animation ID:
Motion and visual reference:
Reference source / model if generated / revision / usage:
Grounded or airborne; entry and recovery reference:
Timing contract: fixed by the game / adjustable to suit the animation (the owner's answer):
Beats: entry, wind-up, contact, follow-through, recovery, endpoint:
Key poses for approval before transitions (story beats and their ticks):
Body mechanics: foot contacts, weight shifts, leading body part, limb/weapon arcs:
Phases: one clip, or one authoring job per clip the game plays:
Root / foot-contact constraints:
Permitted exaggeration and preserved regions:
Authoring route and tool versions:
Candidate revision / workspace / preview:
User review: pending / accepted revision:
```

Read the actual source FPS, range, duration, baked frame count and runtime playback
multiplier. Use the [tick-to-source mapping](../../skills/sloparena-animation-authoring/SKILL.md#before-authoring)
to place contact and recovery; do not assume 1× playback. Keep authored gameplay
time in 60 Hz ticks. When retiming is authorized, label the proposed clock
provisional until the candidate is accepted; otherwise preserve the existing one.

Ask the owner which timing the game must keep and what may change to suit the
animation, before authoring. Record the answer in the brief's `timing` field;
the animator and the director both see it. Sir Bonk's E1 leap (2026-10-21) was
authored as one 148-tick clip to keep the leap's rules. Only afterwards did the
owner say the physics and timing could change as long as aim, flight and the hit
with its recovery stay. The move was then cut into phase clips.

A move the game restarts on every tap (a rapid-fire shot) starts and ends on its held pose, not on the idle: the salamander's Finger Guns shot drew from the hip at tick 0, so six fast taps flailed the arm between hip and aim three times a second (owner, 2026-10-27: "like he's having an epilepsy attack"). The draw belongs to the game's blend from the idle into the first shot. Keep its recoil small (about 12 degrees, not 28) and its follow-through light, and review it as the game plays it: `unity_clip.py timeline` with the idle, a burst of taps at the fastest rate and the exit, not one clip looped.

Keep idles calm. An idle plays while the player presses nothing, so it gives the
fighter a bit of life; it is not a performance (owner, 2026-10-27, on Bashie's and
the salamander's GenRA idles: "way too quick and active"). The hand-made Manki and
Wibou idles turn their body joints 2–11°/s on average and move their hands
0.05–0.08 body lengths a second (hips to head) against the hips. Those two GenRA
idles moved 34–43°/s and 0.5–1.2 body lengths a second. Brief a long loop of slow
breathing and drift, with at most one small, slow gesture, and measure it against
those numbers before review. Slowed 3× to a 6 s loop, the salamander's idle fell to
6.6°/s and 0.054 outside its one gesture.

Split a long move into one authoring job per clip the game plays, such as a
leap's flight and its landing. Chain the jobs with the brief's `entry`/`exit`
references, so each job starts on the previous job's pose and an accepted phase
is not rebuilt. Give a section that needs room, such as a struggle, its own
job at its final timing. Squeezing it afterwards changes its speeds, and
`unity_clip.py write` re-checks a cut or retimed phase for that reason.

Create a fresh isolated workspace under `artifacts/` and candidate assets at new
Unity paths. Record hashes and back up the affected source assets, importer
metadata and live package bindings. Preserve the currently accepted version.
Do not rerun an old experiment in place or edit live move data to enable a preview.
All live Editor sampling/import/preview work follows the
[Unity CLI guide](../contributing/unity-cli.md) and
[Editor coordination protocol](../contributing/unity-editor-coordination.md), using
this caller's own bounded gateway lease.

### Choose visual references

Use image pose references by default when a new visual direction is needed
(21 October 2026). Reuse accepted references when they already explain the action.
For ordinary attacks and holds, start with one coherent pose sheet grounded in the
actual character render: about 4–6 labelled poses for an attack, or a single pose
for a hold. Choose only the beats the move needs. Keep the camera, proportions,
weapon dimensions and grip consistent; use a plain background and clear silhouettes.

Nano Banana 2 (`gemini-3.1-flash-image`) is the suggested starting model for the
first trial through an available, authorized image tool. Check current availability,
reference-image support and pricing before spending; this choice does not require
a new dependency. Follow the task's existing provider and budget authorization.
Record the actual model, prompt, input render, output revision and usage. See the
[image-model documentation](https://ai.google.dev/gemini-api/docs/image-generation).

Map each pose to the gameplay clock. Describe which foot stays planted, where
weight transfers, which body part leads and how the weapon or limb travels between
poses. Attach the sheet through the brief's existing `images` and `image_notes`,
with explicit `keys` ticks. The current `review.beats` comparison expects extracted
video frames and timestamps; leave it unset for a still-image sheet and compare
the reference directly beside `keys.png` and the Unity captures.

Block the poses on the real rig and use the key-pose approval step below. As soon
as transitions exist, inspect rough playback for timing, weight, arcs and contacts;
stills alone cannot establish those qualities. Repair the animation's observed
defects before buying more reference variations.

Use Seedance only for a question stills can't answer: a prop or mount that changes how
the body works, or an unusual gait, stumble or elaborate recovery. Ask the owner first,
with the movement question and the cost. Make one take of 8 s or less: Seedance 2.5 at
720p cost $1.17 for 8 s and $2.32 for 10 s on 2026-10-21. When a move has several
endings, shoot the shared opening once and start each ending's take where it differs.
Preserve useful approved video references and extract their beats and movement notes
as needed. Generating video solely to obtain key poses is no longer the default. See the
[toolkit reference instructions](../../tools/genra/README.md#visual-references).

The video route, measured on Sir Bonk's Joust (2026-10-21, five clips: mount, gallop
loop, hit, stop, look-down; [bonk.md](bonk.md#the-joust-r--wired-2026-10-21)):

| Stage | Spend | What it gave |
| --- | --- | --- |
| Seedance 2.5, four 10 s takes | $9.28 | The grip: his left hand on the horse's neck, the plank couched under his right arm, the broom tail between his legs. The owner kept the take where "he is actually holding the fake horse". Each take replayed the same mount and gallop before its own ending ("4 times the same anim"). |
| GenRA keys, five briefs | $2.88 | The key poses the owner approved before any motion. |
| GenRA motion, round 0 of each clip | $3.57 | The clips in the game. The gallop's gait came from the approved keys: Seedance's gallop was a heroic scissor run. |
| An unattended gallop revision | $2.75 | Nothing: its review pulled it toward his run and the owner kept round 0. |
| Director reviews | $0.62 | Two reviews. |

That is $19.10 in total. A shared opening with short ending takes, and no revision without
the owner, would have made the same move for about $11.

Try the image-only route on Sir Bonk's Q, the pot-lid throw, or his next normal attack.
Record reference spend and authoring revisions through user acceptance, then compare
with the Joust. The choice is a production trial; quality and total cost savings remain
to be established on an accepted rigged animation.

## 2. Prepare the actual rig and prove conversion

Use the real source rig, inverse bindposes, skin weights and current prefab.
Preserve complete bone paths, parents and a reversible Unity/Blender/USD name map,
including fingers, tail and helpers outside the Humanoid Avatar. Calibrate units,
facing and axes from those bindposes; do not infer them from a generic rig or
guessed Euler rotations.

Before generating new motion, take a known same-rig clip through
**Unity sampling → USD → Blender → ordinary FBX → Unity on the actual prefab**.
Compare joint transforms and deformed skin at relevant times, including between
original source frames where needed. Declare tolerances and required references
before judging the control. Record warnings and failed controls explicitly.

For the toolkit's Unity-built rig packs, `tools/genra/unity_clip.py roundtrip` is
this proof without Blender: the clip as the game plays it, then the rig pack, a
native Humanoid clip, and the game's playback again. On 17 October all four current
characters' idle, run, jump and fall came back within 0.084 mm per bone and
0.126 mm per vertex. Sample clips the way the game plays them (Animancer,
`applyRootMotion` off): plain `AnimationClip.SampleAnimation` was up to 3.5 m away,
because it applied root motion on prefabs that enable it and extracted roots
differently. See the [toolkit README](../../tools/genra/README.md#into-unity).

A conversion baseline can be reused when its source rig, Avatar, prefab/skin
hierarchy, reference assets, converter revision and import/export settings still
match. Record that match rather than rebuilding the whole proof for every move.
Recheck conversion after changes to those inputs. Every new candidate still
needs its own actual-character playback checks.

Manki lessons from the retained runs:

- The original rig has 66 joints; its Avatar maps 22. Plain Humanoid conversion
  dropped motion on the other 44 finger, tail and helper joints.
- USD sanitized `mixamorig:` to `mixamorig_`. Preserve the full path map rather
  than relying on leaf names alone.
- The measured mapping was Unity `(x,y,z)` to Blender `(-x,-z,y)` at metre scale.
  This is a calibration result, not a universal preset.
- The fall control passed its strict 1 mm skin threshold; the auxiliary jump
  retained a 1.074 mm warning. Do not report both as a clean strict pass.
- Temporary one-bone skinning for rigid attachments required an equivalence check
  on the copy. It did not authorize changing the source rig.

## 3. Author, process and repair motion

Block the important poses on the measured rig, then add breakdowns and
interpolation. Fit the requested wind-up, strike and recovery to the recorded
clock.

For a new move with a reference, get the key poses approved before the motion
between them. `tools/genra/run.py BRIEF --stage keys` authors only the story
poses and renders them next to the reference beats (`keys.png`,
`beats-keys.png`). After the owner approves, `run.py BRIEF --keys <that run>`
builds the transitions, and the check keeps the approved poses. E1's upright
launch and hunched haul were pose misses. They survived two 30–45 minute
revisions that spent their time solving transitions.

Blocking checks now distinguish story-pose validity from provisional motion risks. A Humanoid violation between
valid poses is a transition problem; a violation on a story tick is a pose problem. Window/arc checks stay visible
in the transition report. The scratch CLI inherits the stage; `--stage full` explicitly checks all motion.

Each job starts with a recoverable candidate, `author.py` and a local `API.md` with live signatures. Saves are atomic
and archived outside writable scratch. The runner stops after 20 minutes without a changed, loadable checkpoint,
or at its overall timeout; `run.py --checkpoint-timeout` overrides the former. An unchanged starting checkpoint
cannot qualify as a finished take or trigger paid review. Interrupted jobs retain unknown cost when the provider
did not report it. See the [toolkit guide](../../tools/genra/README.md) for the artifacts.

Preserve the approved gesture and personality. Do not impose new exact-pose locks on a brief that allows variation.
Flow and repeated-reversal diagnostics help locate visible problems; they are advisory, and do not justify adding
imperceptible sway or spending the job clearing scores. Repair a diagnosed interval, keep intentional holds and
impact snap, and explain any conflict between an acceptable variation and the machine constraints.

Use `./genra check result.json --fast` while iterating on numerical constraints.
It retains timing, anatomy, contacts and approved-key checks, and skips mesh
clearance, visibility and automatic renders. Run the default full check and inspect
its renders when a pose pass is ready to judge and before delivery. The runner's
final checks and the director's review always run full checks. Review uses the
run's saved brief and approved poses; reused reviews get fresh full checks too.
The loop only selects a candidate that passes both authoring and review checks.

Hold contacts with the toolkit's contact tools rather than by searching poses
(see the [toolkit README](../../tools/genra/README.md#contacts-flips-and-key-poses)):

- Pin what is physically fixed. A weapon stuck in the floor pins its tip and
  lets its handle rock (`pin`). Fixing the hand that carries it freezes that
  hand's orientation, so the wrists fold as the body moves around it.
- Solve each key from its neighbour (`seed`), and hold contacts between keys
  tick by tick (`track`, `pin`, `hold_grip`), so elbows do not flip. A flip is
  never speed, and slowing it down only shows the roll for longer.
- A second hand closes around the handle. A target on the distance between the
  hands passes a hand floating beside the handle.

When the game moves the character (a leap, a dash), preview the clip on the
game's trajectory early with `unity_clip.py timeline`, for short and long cases.
E1's late swing at touchdown first showed up in game. Keep the actor root fixed unless gameplay displacement is explicitly
authorized; a permitted local pelvis shift is distinct from moving the actor.
Grounded motions need foot-contact constraints; airborne motions need compatible
fall/landing transitions rather than artificially planted feet.

Use explicit same-rig references and retain the literal brief and effective
settings. For model-proposed keys, disable automatic example selection unless
deliberately included in the task. For either route, retain the authored keys
and processed USD so that conversion errors can be separated from pose choices.

Check limb lengths, anatomical knee/elbow directions, wrist orientation, finger
curl and thumb opposition on the actual mesh. Numerical goals support those
checks: the refined aerial reached its hand-height goal while bending the knees
incorrectly, and Head Slam inherited reversed finger curl from an earlier pose.
For current Manki, the corrected palm normal was hand-local +Z; inspect both
hands rather than applying that sign to another rig.

Use quaternion channels when Euler conversion introduces singularities. Head
Slam's curled fingers exposed that failure. Preserve explicitly sampled times
when preparing GenRA input: USD can omit constant samples, so materialize the
expected channel times before parsing when necessary. Check the processed
result against authored motion, duration, planted contacts and root constraints.
If USD previews move to another directory, resolve their texture dependencies.

Inspect complete timed playback as well as contact sheets. Fix the observed
problem in a bounded pass, preserve accepted regions/channels, and retain the
before/after comparison. Neither successful export nor one silhouette metric
establishes animation quality.

## 4. Convert and preview the unbound candidate

Reuse the original valid Avatar where appropriate. Import through ordinary FBX,
then retain any required unmapped FK curves in a Unity-native hybrid `.anim`.
For a directly authored native clip, skip unnecessary FBX steps. Confirm the
current clip GUID/fileID, curve paths, length, sample rate and baked frame count.

Toolkit results skip FBX: `unity_clip.py write` builds the hybrid clip directly,
starts a matched entry where the entry clip actually plays, and measures it as the game
plays it. A free entry (`entry.match: false`, such as a crouched aim loop) keeps the
toolkit's origin, and a loop from tick 0 is saved looping for aim holds. Brief directions
and review views follow the fighter's facing; before 18 October they followed the entry
hips, which fighting stances turn 40-90° away (see the toolkit README).
Read its report before preview. `meshVsToolkit` shows where Unity's limb twist
distribution changes the mesh even though joints match: in a 17 October test the
forearm mesh rolled up to 81 mm differently during a punch. Review those ticks on the
actual character before approval. A `muscleFit` miss of centimetres means the toolkit
pose left Unity's Humanoid limits: the result must pass the toolkit's `humanoid`
check before conversion. On 17 October a whirlwind authored before that check rolled a
collarbone 122° past its limits and missed by 72 mm per joint.

`unity_clip.py render` then plays the saved candidate on the catalog prefab, with its
materials and sword, through Animancer over the move's ticks, from the review views.
Add the bound clip it would replace as a second `--clip` to compare both on the same
clock. Show `real.mp4` to the owner for approval: it is the move as the game will play it.
For loops and holds, also show the handoffs with `unity_clip.py timeline`: for example idle,
then the held aim loop, then the release into the attack, with the game's crossfades and travel.

For Manki's deformation rigs (`Presentation/MankiRig.prefab` and the legacy
`Presentation/Legacy/MankiDeformRig.prefab`), fingers also have skin branches below
`LeftFistDeform` and `RightFistDeform`. Supply matching curves to the original and
copied paths, and reset the existing arm/fist deformation controls when the move
does not use them. Plain Generic FK on a Humanoid Animator did not preserve the
intended body playback. Direct scale curves on mapped Humanoid hands were also
ignored; use existing unmapped deformation controls when authorized, rather than
adding a new rig for each move.

When building such controls into a new rig, keep every animated control off the
path between two mapped Humanoid bones. Humanoid retargeting resets transforms
between mapped bones (for example a fist control between `Hand` and mapped
fingers) to their rest pose, so their curves bind but have no effect. Leave the
mapped fingers under the hand as unskinned anchors, hang skinned finger copies
under the fist control, and have the copies follow the anchors with
`RotationConstraint`s.

Validate through actual Animancer playback with the runtime root-motion setting.
`AnimationClip.SampleAnimation` alone did not establish the same root extraction.
Bake intended root channels into the pose and measure any constant Humanoid body
origin correction against the actually playing idle/fall reference. Do not copy
an old origin offset or hide a conversion error with client actor transforms.

Check that imported floating-point duration has not added a baked frame or
shifted contact under the runtime speed multiplier. The Manki runs needed tiny
FBX timestamp corrections to recover the intended exact 60 Hz grid. Record that
correction and verify the resulting clock; do not retime gameplay to conceal it.

Prepare normal-speed side and three-quarter rear previews of the whole action,
semantic pose sheets, and joint/hand close-ups when the wide view hides anatomy.
The three-quarter rear camera sits behind the striking shoulder, 35° to that side
and 20° up. Do not review from the match camera's angle, straight behind the
fighter: in the first toolkit runs it showed only 0–24% of the striking hand or
weapon at contact. The Ability Lab fighter faces world +Z, so capture the view with
absolute camera angles: `--camera-yaw -35` for a right-side strike, `35` for a
left-side one.

```bash
editor sloparena.lab.capture --action ground.2 --ticks 8 --camera-yaw -35 --camera-pitch 20 \
  --overlays none --dummy off --output .ability-lab-cache/fightguy/straight-punch-rear-right
```

Inspect foot drift, clipping, silhouette, contact timing, recovery, endpoint and
interruption blends into idle/fall. Test reset of deformation inherited from
adjacent moves. Candidate inspection must use the actual current character,
not only the Blender rig.

## 5. Obtain animation approval

Follow the [user review checkpoint](../../skills/sloparena-animation-authoring/SKILL.md#user-review-before-move-integration).
Present the candidate revision, actual-character preview and proposed
contact/recovery clock. Wait for acceptance of that revision before changing
`character.json`, the live catalog binding or accepted cooked content. Feedback
returns to candidate repair and another preview.

This is the animation acceptance gate. Internal authoring and technical checks
continue under the task's existing authorization; acceptance is reused for the
subsequent authorized integration steps.

## 6. Integrate and verify

Promote the accepted clip with its `.meta` into the character package, without
dependencies on experiment assets. Bind its exact current identity in
`CharacterAssetCatalog.asset`. Apply only authorized move-data changes using the
canonical slot and approved clock. Follow the
[character workflow](../../skills/sloparena-character-workflow/SKILL.md) for
inspect/cook/verification and roster admission when applicable; never hand-edit
`content-cooked/`.

Choose verification using [testing.md](../testing.md). A changed clip can change
pose-derived hitboxes even when gameplay JSON is unchanged. Check relevant
contact, shield and whiff behavior through native Ability Lab/Shared scenarios
when authoritative poses or move behavior change. For a bounded presentation
repair, compare cooked poses and gameplay data before deciding whether earlier
behavioral evidence remains applicable. Do not force broad engine tests or a
recompile for an unchanged implementation.

Inspect generated diffs: automatic cooking can refresh other packages' source
hashes and roster pins. Report that scope and verify whether their runtime/pose
payloads changed. Record the affected Unity checklist in `TESTING-UNITY.md`.

## Evidence and working examples

Keep a concise tracked result report and retain the detailed local run evidence:
brief/clock, source hashes and backups, rig/name maps, conversion controls,
authorship/settings/raw response when applicable, authored and processed motion,
candidate identity, complete previews, repair comparisons, user acceptance and
integration results. A fresh checkout must not need these local artifacts to
play the accepted package clip.

| Record | Useful precedent | Local workspace |
| --- | --- | --- |
| [FightGuy experiment](../research/2026-10-14-genra-experiment.md) | First conversion control and bounded model refinement | `artifacts/genra-20261014/` |
| [Manki aerial experiment](../research/2026-10-14-genra-manki-aerial-experiment.md) | Full rig map, hybrid clip, runtime origin and timing | `artifacts/genra-manki-aerial-20261014/` |
| [Manki leg correction](../research/2026-10-14-genra-manki-leg-correction.md) | Anatomical IK repair after a misleading pose metric | `artifacts/genra-manki-aerial-20261014/` |
| [Manki air 3](../research/2026-10-15-manki-air3-double-fist.md) | Accepted package integration and existing deformation controls | `artifacts/manki-air3-integration-20261015/` |
| [Manki Head Slam](../research/2026-10-16-manki-ground4-head-slam.md) | Current-session authorship, quaternion conversion and finger correction | `artifacts/manki-g4-head-slam-20261016/` |
| [Provider A/B](../research/2026-10-16-genra-opus-ab.md) | Opus 5.5 through the Claude CLI on the retained aerial prompt; bend-direction checks | `artifacts/genra-opus-ab-20261016/` |
| [Bonk aerials](../research/2026-10-17-bonk-genra-aerials.md) | Toolkit concepts to an approved bind; Humanoid limits; real-character review with `unity_clip.py render` | `artifacts/bonk-aerials-integration-20261017/` |
| [Bonk Blade Storm](../research/2026-10-18-bonk-blade-storm-spin.md) | A multi-hit ground special redone on its clock; hit coverage before and after; entry pelvis frame | `artifacts/bonk-f-redo-20261018/` |

The aerial workspace's `README.md` indexes the reference/conversion/import
helpers. Head Slam retains the later authoring and finger-repair passes. These
scripts contain move-specific paths, clocks, protected hashes and duplicate-run
guards; they are reference implementations, not generic commands to rerun for
the next animation. Dated reports describe their original task state; this guide
and the animation skill define the current process.
