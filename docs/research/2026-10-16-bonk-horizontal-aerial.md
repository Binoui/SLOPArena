# Bonk horizontal aerial slash

The requested motion is one simple horizontal greatsword strike through the
space in front of airborne Bonk. **R5 was approved and integrated into Air Cleave
(`air.2`, `anim.bonk.a2`).** The existing gameplay values and timing are preserved.

Package source:
`Assets/CharacterPackages/bonk/Animations/bonk_air2_horizontal_slash.anim`.

Candidate:
`Assets/Art/Experiments/GenRABonkHorizontal20261016/BonkHorizontalAerialR5HorizontalTurn.anim`.
The current self-contained `preview.html`, synchronized R4/R5 comparison,
overhead/arm/whole-character views, and normal-speed MP4s are under
`artifacts/bonk-horizontal-aerial-20261016/r5-horizontal-grip/`. R2, R3 and R4
sources and evidence remain in their previous workspaces.

## Unity integration

The user approved R5 ("good ! nice work") and requested Unity integration.
Unity's AssetDatabase copied the native clip into Bonk's package, with a fresh
GUID `a9cc09b2cba80652b85b96f51b9f8c0a` and fileID `7400000`. All 260 curves,
keys, tangents and clip settings match the approved candidate; only the asset
name and identity change. The candidate and original `bonk_a_2.anim` remain
intact. A narrow `.gitignore` exception includes this project-authored source.

The typed package binding command changed only `anim.bonk.a2` in
`CharacterAssetCatalog.asset`. It now plays 53 frames over 53 ticks at 1×,
with contact at ticks 15–28 and IASA at 45. Damage, Knockback, Hitstun,
landing lag, effects, controls, rig, Avatar and sword attachment are unchanged.
Both source gameplay JSON and cooked runtime JSON are byte-identical to the
pre-integration snapshot.

The accepted package crossed `inspect` → `cook` → `verify` → `roster.refresh`.
Source/cooked-source hashes match, final status is `valid`, and `dirtyOrStale`
is false. The Bonk roster entry pins the new verified package; other entries
were preserved, including the concurrent Manki refresh. The cook changed only
`anim.bonk.a2` among 25 pose tracks (28 old baked frames → 53 new frames).
The other 24 tracks are byte-identical. Generated bindings/catalog and package
hashes were produced by the cooker rather than edited by hand. These changed
poses affect authoritative sword and hurtbox geometry.

Native Ability Lab scenarios and captures exercised the bound clip through
the actual `PlayerRenderer`, valid Humanoid Avatar and equipped sword. At
attack elapsed tick 19 the observed clip time is 19/60 s. Scrubbed playback
reports speed zero because it is paused; the 53-frame/53-tick runtime mapping
is 1×. Lab reports a live source draft even after a verified cook; package
acceptance is established by the separate persisted package verification.
At active ticks 15 and 28 the equipped sword's hilt/tip relative to the animated
hips agree with the persisted pose track within 0.00056 mm. Named native
presentation inspection resolves both blade anchors and both hands without
diagnostics; an overlay capture checks the bound strike in the gameplay view.
The default Lab aerial starts a full capsule height above a grounded mirror,
so its near idle/shield runs correctly pass overhead. The far run also misses.

Focused Shared coverage resolves Bonk through the admitted Match Content
Catalog and uses its actual cooked poses. Three cases in
`BonkAir2_CookedSwordPathHitsBlocksAndMisses` pass: a low aerial hits an idle
opponent 2.2 m forward once, the same cut is shield-blocked without damage,
and an opponent 8 m away is missed. Each run finishes with the attacker idle
and no active ability. This does not establish online play or human game feel.
Current Unity compilation is settled and its error console is empty.

Integration receipts, backups and the focused test log are under
`artifacts/bonk-horizontal-aerial-20261016/integration/`; bound runtime captures
are under `.ability-lab-cache/bonk/horizontal-aerial-integration-20261016/`.
No commit or push was performed.

## R5: horizontal hand turn and coherent sword grip

Review found R4's right-hand motion unsuitable for a human sword swing. Its
blade orientation was imposed independently of the arm, forcing the wrist
through 109–124° relative to the existing fall grip during the active interval.
The R4 continuity and elbow checks did not establish believable weapon handling.

R5 rebuilds the right-arm arc around the original fall wrist articulation. The
local wrist stays fixed in authoring; the calibrated elbow and upper-arm pose
carry the hand and blade. During contact, the hand and forearm yaw together
through a horizontal turn. The blade is rolled to match the grip instead of
forcing the palm upright through a bent wrist. The sword attachment and finger
articulation are unchanged. Wind-up and recovery blend the arm from/to fall,
with entry eased over ten ticks. Only the right-arm branch changes; the free
arm, torso, head, pelvis, legs, actor root and 53-tick clock are preserved.

The final actual-prefab candidate has 5.50° maximum active wrist deviation from
the original fall grip (6.97° across the entire clip), including Humanoid twist
redistribution. The hand and forearm rotation axes deviate from world-up yaw
by at most 0.016° during contact. Active blade elevation stays within 0.0046°
of horizontal. The maximum adjacent-frame hand rotation is 31.94° during the
short entry at tick 5; the faster first-entry pass reached 39.9° and is retained
outside Assets. This is a fast pose transition, not an accepted 180° twist wrap.

The same actual Bonk prefab, Avatar, materials, sword grip and Animancer path
were captured at 1× from gameplay, side, overhead, overhead-hand and arm views.
The comparison includes 54 poses per view/revision and the endpoint. The
maximum authored/playback joint position residual is 0.452 mm on a left-hand
helper. GenRA's maximum position residual is 0.00058 mm. Unchanged body and
finger-local matrices agree with R4 within floating-point roundoff, all thirteen
protected source hashes match, and the R4 clip/metadata hashes remain intact.
The current fall entry/handoff and all eight six-tick fall/idle fades pass.

This is current-session pose authorship through the pinned GenRA quaternion/USD
route with no extra provider call. `author-slash.py`, `validate-grip.py`, raw
poses, playback receipts, five-camera renders and `preview.html` are retained in
the R5 workspace. This authoring pass remained unbound until user review;
the approved integration is recorded above. The earlier conversion-control
warnings below remain applicable.

## R4: reach and anatomical arm repair

Review found the sword arm too folded and the free elbow bending incorrectly.
At the main strike (tick 18), shoulder-to-wrist reach now increases from
0.328 m to 0.666 m. The upper arm changes by 38.7° and the elbow moves
0.241 m relative to R3. Active elbow flexion is 49–60°, retaining a bend.
The R3 torso, pelvis, head and leg authoring matrices remain exactly unchanged.

The free-arm defect originates in the authored R3 poses, before GenRA. The
previous solver independently aimed upper arm and forearm with shortest-arc
rotations. It preserved segment lengths and wrist targets while allowing the
elbow to rotate outside its anatomical bend plane. This is an authoring and
verification failure; it does not establish that GenRA cannot produce the move.
This run uses current-session numerical authorship, not a separate model request.
The pinned GenRA parser/interpolator preserves the input rotations rather than
correcting anatomy.

R4 measures the current Avatar's forearm-stretch rotation with twist held at
zero, solves its one-dimensional elbow articulation, and orients the whole
upper-arm/forearm chain to that articulation. Its bend plane follows the torso
and wrist target. Free-wrist articulation and original finger curves follow the
forearm. Original fall articulation is eased in/out at entry and recovery.

Actual Humanoid playback also redistributes twist, so a correct authored wrist
position is insufficient. One repair preserving R3's exact elbow placement was
rejected after Unity changed its bone roll. Direct FK and mapped FK/Humanoid
controls also failed actual-Avatar checks and were rejected. No Avatar or rig
changes were made. The final free elbow retains its calibrated bend direction;
maximum plane deviation after conversion is 4.96° over ticks 6–38. Axial forearm
rotation is allowed; distance from a zero-twist pose is diagnostic, not by itself
an anatomical failure.

The full action exposed a separate right-wrist winding inherited from R2: its
orientation made a complete turn over the loop. Humanoid forearm twist wrapped
from +1.959 to −1.895 at tick 12, producing a 174° forearm step. A more compact
right-wrist load before tick 15 removes that winding. Contact sword rotations
at ticks 15–28 and the existing recovery orientation remain unchanged. The final
maximum adjacent-frame forearm rotation is 16.1° right and 18.1° left; all upper
arm, forearm and hand steps stay below 25°. No muscle-value unwrapping or Avatar
compensation was used.

Final evidence uses the actual Bonk prefab, materials, sword attachment and
Animancer evaluation: 54 sampled poses in four synchronized views per revision.
The maximum GenRA position residual is 0.00058 mm; authored/native joint error
is 0.454 mm on a finger helper. Active blade elevation remains below 0.218°,
actor root/scale stay fixed, and the 53-tick / 60 fps / 1× clock is unchanged.
All eight six-tick fall/idle fades pass, with entry and tick-45 fall handoff
matching within 0.0015 mm. All thirteen protected Bonk source hashes match.
Rejected native candidates and metadata were backed up outside Assets before
removal. `validation.json`, the raw source/calibration and preview receipts
record the numerical checks. The inherited reference-resampling warning below
still applies; no rig/converter baseline changed.

R3's small position error and isolated wrist/finger views were insufficient
anatomy evidence. Future repairs must check signed bend direction, skin/bone
roll, continuity across the complete clip and the actual Avatar. Knees and
finger segments need their own measured axes and limits; this elbow correction
does not prove those other joints correct. Candidate approval and live package
integration remain pending.

## R3: body motion after review

The owner found R2 too still: the sword hand dominated while the free arm,
torso and legs needed to contribute. R3 keeps the same single cut, complete
authored sword-hand trajectory, grip, clock and fixed actor root. It adds:

- A pelvis-led coil and stronger chest unwind/follow-through. Actual chest
  angular range increases from 48.0° to 100.8°; pelvis range increases from
  12.0° to 32.9°.
- A free-hand reach during the load, followed by a backward counter-swing.
  Maximum wrist travel increases from 0.192 m to 0.914 m.
- Asymmetric leg tuck, opening through the cut and delayed counter-motion.
  Left/right ankle travel increases from 0.032/0.057 m to 0.237/0.267 m.

Actual-character stepped blocking was compared against R2 before final
interpolation review. The free wrist follows its forearm while retaining the
original finger articulation. A bounded ankle repair preserves each original
ankle rotation relative to the shin: the first pass's world-oriented feet
caused up to 10 mm of Humanoid toe conversion error. The final maximum
authored/playback joint error is 1.033 mm, on a finger helper. Palm/back hand
views and both ankle views were inspected; knee bend ranges remain
82.4–117.4° left and 89.0–139.1° right.

The same pinned GenRA parser/quaternion/USD route processed the revised poses;
no separate model call, install or rig change occurred. Maximum GenRA world
position residual is 0.00051 mm. On actual Animancer playback the sword wrist
differs from R2 by at most 0.073 mm, active blade elevation stays below 0.216°,
and all eight fall/idle fades pass. Entry and tick-45 handoff again match fall
within 0.0015 mm. The final clip remains 53 frames at 60 fps and 1× speed.
All thirteen protected source hashes still match.

The comparison contains 54 scrub poses per revision/view, including the
endpoint. Normal playback advances about nine ticks per 150 ms and completes
the 53-tick loop; both camera views share the same cursor. R2, stepped blocking
and the pre-ankle-repair source are retained. No live binding or gameplay data
was changed. The inherited 30 fps reference resampling warning below remains
documented; no new whole-rig conversion control was required for unchanged
rig/Avatar/converter inputs.

## Motion and proposed clock

Both candidates fit the existing `air.2` / `anim.bonk.a2` clock without changing
gameplay. That binding remains untouched pending review and slot confirmation.
The original clip is 30 fps, 0.456666648 seconds and 28 baked frames. The
candidate is 60 fps with 53 baked frames over 53 ticks, so its playback is 1×.

| Tick / source frame at 60 fps | Beat |
| --- | --- |
| 0 | Current fall pose |
| 6–10 | Compact right-side wind-up |
| 15–28 | One level cut through forward space |
| 18 | Blade points directly forward |
| 28–31 | Follow-through |
| 31–45 | Recover to fall |
| 45–53 | Exact fall endpoint |

In the initial R2, the free arm balances the cut and the legs retain the bent airborne pose. The
actor root stays fixed. No spin, jump/dive impulse or gameplay displacement
was added. Existing damage, hitboxes and landing rules were not exercised or
changed: this is an unbound animation review, not move integration.

## Authorship and conversion

The current Codex session authored original joint poses against Bonk's actual
65-joint hierarchy, inverse bindposes, current Avatar and measured runtime sword
placement. Arm IK preserves joint lengths, and primary parameters use controlled
interpolation. No separate model/provider request or installation occurred.

The existing GenRA checkout at
`5f4ffb8ac846f91265c48e8bb65d702dca11b7a1` processed the quaternion motion through
its parser, interpolation and USD writer. Maximum authored/processed world-joint
position error is 0.00052 mm. Authored Blender/USD sources, literal brief, keys,
conversion helpers and source backups are retained in the evidence directory.

Ordinary FBX controls were rejected. An extra Blender wrapper initially failed
Avatar hierarchy validation; matching the original wrapper admitted the Avatar
but still produced an incorrect skin basis/pose. These are failed controls,
not accepted exports. Rejected Unity imports and their metadata were copied
outside Assets before cleanup.

The final native Humanoid/FK route reconstructs the original Unity bone basis
from GenRA USD, retaining the current Avatar and unmapped helper joints.
The fall control exceeds the initial strict 1 mm threshold between original
30 fps frames: maximum joint/skin discrepancies are 3.203/3.123 mm. This
resampling warning remains explicit. The initial R2 candidate's independently measured
joint discrepancy is at most 1.244 mm, after accounting for the constant body
origin difference between direct sampling and actual Animancer playback.
R2 freezes the conversion reference phase, removing R1's 2.3 mm endpoint wobble
without changing authored/GenRA motion.

## Candidate verification

- Actual current Bonk prefab, materials, Avatar and runtime sword placement
  were captured through owned, bounded Unity gateway leases.
- All 54 semantic frames were captured from side and gameplay angles. Complete
  53-frame, 60 fps videos and a synchronized scrub player were inspected; browser
  playback advanced 18 frames in 300 ms at 1×.
- Close palm/back views at ticks 10, 18, 25 and 45 show the preserved sword grip.
  The final side framing includes the whole blade.
- Actual blade elevation stays within 0.217° of horizontal during ticks 15–28.
  Actor position and scale remain fixed throughout playback.
- Entry and tick-45 handoff match actual fall within 0.0015 mm on skin bones.
  Eight six-tick interruption fades from ticks 10, 18, 30 and 45 into fall/idle
  complete at full target weight, with finite transforms and matching end poses.
- Thirteen protected Bonk source/model/importer files retain their saved hashes.
  Existing unrelated Manki, stage, workflow and cooked-output edits were preserved.

No C# implementation changed, so no recompile or Shared test suite was required.
No cook, roster admission, commit or push was performed. Candidate review precedes
promotion and any authorized package/Ability Lab verification. Current console
inspection retains the earlier rejected-control import messages; these do not
establish a clean global console or successful ordinary FBX conversion.
