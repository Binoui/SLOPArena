# Ring Bounce: separate the two jumps

Owner feedback after v1 integration: the motion is nice, but the move needs a clear throw ahead, a small forward jump onto the ring, contact/compression, and then a high jump. The first part is too quick and does not read as two jumps. This supersedes v1's fixed 12-tick launch clock; the core recovery and automatic controls still apply.

## Candidate direction

Prepare a new isolated actual-rig candidate. Preserve the liked ready, plant, squash, spring and rising body poses; author new release, small-hop takeoff and small-hop apex poses. The ring visibly leaves her before she jumps toward it. Do not uniformly stretch the existing in-place move.

Provisional clock at 60 Hz, for key/blocking review:

| Tick | Beat |
| --- | --- |
| 0 | Ring held in front; fall arrested |
| 12 | Release the ring forward, feet still down |
| 18 | Ring is clearly ahead; begin the small forward hop |
| 26 | Small-hop apex, knees tucked over the rim |
| 34 | Both sandals connect to the padded rim |
| 40 | Loaded crouch and ring compression |
| 44 | High recovery launch |
| 54 | Rising handoff to ordinary air control |

The review stages the ring about 1 m ahead of the initial actor root and its top about 0.18 m above the entry sole plane. The automatic first hop moves the actor about 0.72 m forward with a roughly 0.5 m apex, then plants on the ring. These distances are candidate staging, not implemented Shared movement. The high launch retains v1's intended recovery strength. Timing and distances remain provisional until the actual candidate is accepted.

## Controls and recovery

One E handles the full sequence, including offstage with ordinary jumps spent. Placement and the first hop stay automatic; movement input aims the high launch through compression and locks on spring. Neutral maximizes height. Arrest descent throughout the longer setup so it does not waste recovery height. The cannonball remains deferred.

Actual forward/vertical actor travel belongs in the already approved Shared recovery capability, with bounded deterministic hop parameters if needed. Keep ring motion cosmetic; do not use Unity collision, animation callbacks or a new character/slot dispatch. Live movement/clip data stays on v1 until the new animation acceptance checkpoint. Preserve unrelated dirty work.

## Authoring and review

Use the existing verified Bashie rig pack and life-ring mesh. One maintained GenRA Opus 5.5 keys pass authors only the three new body poses. Retimed protected keys copy v1's approved core poses. Present actual-rig keys and a clearly labelled timed blocking preview with separate ring and actor staging. Obtain key approval before motion, then concrete animation acceptance before replacing live E, as required by the animation skill. No new reference generation or unattended creative revisions are needed.

Local workspace: `artifacts/bashie-e-v2-20261022/`. Source brief: `tools/genra/briefs/bashie-e-ring-bounce-v2.json`. The workspace saves the current live assets, source and cooked hashes before authoring.

## Key candidate — owner approved

One maintained Opus 5.5 keys-only round completed successfully (`20261022-182220-bashie-e-two-jumps-v2-keys`): 117.4 seconds, $0.47193, zero key-check failures. Ready, contact, squash, high spring and rise match their protected v1 body poses exactly. The three new keys are release, small-hop takeoff and apex. Interpolation/flow warnings belong to the unapproved blocking pass; no full motion round has run.

The unbound candidate is `Assets/Art/Experiments/GenRA/bashie-ring-bounce-v2/BashieRingBounce_keys_v2.anim`: valid Humanoid, 0.9 seconds, 60 fps, 132 curves, actual playback speed 1.0. A transient actual-prefab preview captured all 55 tick samples from front, side and rear. The ring waits ahead independently of the actor. Actual skinned sandal-to-rim gaps at ticks 34 and 40 are within 2 mm. The forward hop and high ascent remain schematic candidate travel, not implemented Shared movement. Actual-prefab evidence is the acceptance surface: the Humanoid conversion differs from toolkit joints by up to 9.94 mm at the tucked toe and from toolkit skin by 44.05 mm at the preserved spring pose.

Review artifacts: `artifacts/bashie-e-v2-20261022/unity-keys-v2/review.html`, `blocking-once-60fps.mp4`, `keys-contact-sheet.jpg`, and `artifacts/bashie-e-v2-20261022/review-report.json`. The HTML includes normal/quarter-speed playback, frame scrubbing, semantic key seeking and close key poses. Both gateway batches settled and released their own leases.

Live E's authoring data, air alias, animation binding, shipping clip, ring staging and Shared capability remain v1. Another task changed ground.4 and recooked the package during this candidate work; those changes were preserved, and E was compared semantically against the saved source. No v2 live cook, Shared build, gameplay test, commit or push was performed.

Owner response: **“this looks really good”**. This accepts the revised keys, clock and staging for the full motion round. The full take keeps all eight approved keys, high launch at tick 44 and handoff at 54; author transitions only. Its brief is `tools/genra/briefs/bashie-e-ring-bounce-v2-motion.json`. Present the concrete full animation before replacing live E, following the remaining animation acceptance checkpoint.

## Full motion candidate — owner accepted

One Opus 5.5 motion take completed in `artifacts/genra-runs/20261022-183910-bashie-e-two-jumps-v2-motion`: 343.2 seconds, $1.4057014, full checks passed. Added a throw dip/follow-through, small-hop push-off and descending leg extension, landing absorption, spring lead and rising overlap. No paid revision followed.

The authored overlap pass passed the maintained key tolerance but moved some accepted local rotations. A bounded deterministic correction (`artifacts/bashie-e-v2-20261022/lock_motion_keys.py`) restored all eight exact poses and half the correction on each adjacent tick, preserving every other authored sample. The corrected full check passes with zero failures, the three existing near-contact warnings and a mild flow warning (1.04 joint stops/s; the cited hand-authored range extends to 1.16). The original take and its receipts remain intact. The exact corrected source is `artifacts/bashie-e-v2-20261022/motion-v2.result.json`; its contract and checks are under `motion-key-lock/`.

Unbound Unity clip: `Assets/Art/Experiments/GenRA/bashie-ring-bounce-v2/BashieRingBounce_motion_v2.anim`, GUID `9ae91e23ad7a5960d9740806e8dd6d07`, Humanoid, 132 curves, 0.9 s at 60 fps, actual playback speed 1.0. Native playback differs from toolkit joints by up to 9.94 mm at the preserved tucked toe and skin by 44.30 mm at tick 48; the actual prefab, including that raised-arm frame, was inspected rather than assuming mesh equivalence.

Actual-rig front/side/rear capture covers all 55 ticks. Both sandals stay within 1.94 mm of the real padded ring surface throughout ticks 34–40. A lowest-surface centroid appeared to shift 16.4 mm; a measurement-only follow-up tracked the same sole vertices and found at most 8.15 mm horizontal/3.94 mm vertical deformation, within the 10 mm horizontal criterion. This distinguished a changing low-vertex selection from whole-foot sliding without altering the animation. See `motion-review-report.json`, `unity-motion-v2/actual-sole-tracking.json` and the two breakdown sheets. Gateway conversion, render and measurement batches all settled and released their own leases; live E remains unchanged.

Full review: `artifacts/bashie-e-v2-20261022/unity-motion-v2/review.html`, with 1×/¼ speed, scrubber, side/rear playback and close key poses. The full 60 fps MP4 is `motion-once-60fps.mp4`. Desktop/mobile HTML checks passed without console errors. No gameplay implementation, live catalog cutover, package cook, Shared build, commit or push occurred in this motion-authoring step. Next is owner acceptance of this concrete full animation, then integration of the approved clock, automatic first hop and ring staging through the existing recovery seam.

Owner response: **“good”**. The full take is accepted for E integration; no further animation approval is required.

## Integration within the approved recovery seam

The existing approved capability owns automatic setup and samples launch input. Extend its bounded parameters with an optional `hop` (start/contact ticks, forward distance, arc height and landing lift), retaining the prior no-hop behavior and byte representation when omitted. Hop-bearing content requires runtime API 1.10.0 so older runtimes reject it. A second character-specific capability or a new general timeline movement operation would duplicate the approved lifecycle; neither is needed. This implements the Shared automatic-hop extension already identified above and accepted with the four-beat motion, using the existing architecture approval.

Use velocities through normal Shared capsule collision, never position teleports or Unity collision. Capture entry facing for the small hop; sample movement input only at44 for the high launch. Tick18 is takeoff,34 contact,44 launch,54 handoff. Suppress setup gravity, arrest incoming fall, and hold during compression. Preserve the existing neutral15/directed13+horizontal8 launch speeds, cooldown210, ordinary resources and interruption/Hitstop behavior. Snapshot the fixed hop heading only when hop parameters exist, preserving legacy no-hop snapshot bytes.

The approved preview uses model-space travel. Scale it by Bashie's existing1.1 visual scale when authoring world-metre movement parameters (0.792 m forward,0.495 m arc above the chord,0.198 m landing lift). This keeps the same body/ring proportions and exact contact at the current in-game scale. The first ring cue captures the entry model frame and waits in world space; the rebound cue uses the launch event frame. Both stay cosmetic and follow the attack clock. Preserve unrelated package moves and dirty source changes.

## Integrated result

Implemented and admitted. Ground/air E use the accepted 54-tick clip and Shared hop/contact/launch phases, with the ring waiting ahead during approach and following the sandals during compression. The shipping clip retained its GUID and semantic binding. The optional hop round-trips source/cooked content, fails closed for invalid bounds or API downgrades, and preserves the legacy no-hop representation and behavior.

Verification: 42 focused recovery/schema/admission checks; the full current Shared suite 3,312 passed / 2 existing skips; Shared and Server builds (one pre-existing nullable warning in Server); native Unity compile and error console clean; Bashie inspect/cook/verify/roster refresh; ground/air native Lab runs with no contacts, deaths or diagnostics; 111 frames each of real ground/air Play Mode footage on Splash Deck. Ground/air screenshots show the ring separated ahead, the first arc, planted sandals, compression and high rise. The spent-jumps test returns from 2 m below / 3 m outside a solid ledge. Hitstop, interruption, snapshots, late aim and ordinary actionability also pass. Human controller feel remains on `TESTING-UNITY.md`; no remote session was run.

Final source/cooked-source hash: `f104ea33da6dc1528582caddd4857da2b8d439819215ba5de13bbfaedaaa88e8`. Content hash: `ee08b0a046ee7da2d0188620125a34e21ed6e76ed1522cd54b5d56cb0d99d6a0`; package hash: `a9254ba136e85a2f85b5a23493780a398691b3f810ceb4ed4bff367bd3a2d315`. Final inspect is valid, not stale, and has zero diagnostics. Editor restoration returned to the original Frontend scene stopped, with ownership free/settled and zero active operations.

Only Bashie's four cooked payloads and the roster manifest changed across this integration's cook marker; concurrent Bashie air.4/source/catalog edits were retained. No commit or push. Evidence, summaries, the full 60 fps videos and self-contained Play Mode review are under `artifacts/bashie-e-v2-20261022/integration/`.
