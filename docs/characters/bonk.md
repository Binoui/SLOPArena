# Sir Bonk

Sir Bonk knighted himself. His armour is homemade, his sword is a plank, and his battle cry is BONK.
The package ID remains `bonk`. The kit uses the canonical three normals and seven ground and air slots.

The current specials prototype follows [ADR-0059](../adr/0059-sir-bonk-scoop-joust-and-leap-of-faith.md).
Its values are untuned until the owner plays Training. Shared simulation and source authoring are implemented;
Unity bindings, presentation, recooking and human acceptance are separate work.

## Normals

Squire's Cut (`1`) is the shoulder slash, Sunrise (`3`) is the rising anti-air slash,
and The BONK (`4`) is the overhead strike. Each has its authored air variant.
Barn Door (`2`) is retired under [ADR-0042](../adr/0042-controller-layout-three-normals-homing-dash.md).
Normals and their damage timelines are unchanged by this specials prototype.

## Normal authoring record

This October 2026 authoring record includes the now-retired normal 2. The current grid has three normals.

The owner picked four new ground normals from a design board (`tmp/bonk-normals/`), one swing direction each:
1 Squire's Cut (a fast one-handed slash off the shoulder), 2 Barn Door (a flat two-handed sweep), 3 Sunrise (a
rising anti-air slash, without the board's crouch) and 4 The BONK (an overhead kill strike). Normals stay
simple, without gags. Each move keeps today's move-data clock and is authored with GenRA one at a time, from a
Seedance 2.5 reference the owner approved (`artifacts/video-refs/sir-bonk-normals/`, front three-quarter view).

| Slot | Clip | Status |
| --- | --- | --- |
| `ground.1` | `Animations/bonk_sir_g1_squires_cut.anim` (`anim.bonk.g1`, 60 ticks, 1× playback) | Bound and cooked 2026-10-21 |
| `ground.2` | `Animations/bonk_sir_g2_barn_door.anim` (`anim.bonk.g2`, 93 ticks, 1× playback) | Bound and cooked 2026-10-22; misses a standing Manki (below) |
| `ground.3` | `Animations/bonk_sir_g3_sunrise.anim` (`anim.bonk.g3`, 60 ticks, 1× playback) | Rising Cut since 2026-11-01 (file keeps its old name; below) |
| `ground.4` | `Animations/bonk_sir_g4_the_bonk.anim` (`anim.bonk.g4`, 118 ticks, 1× playback) | Bound and cooked 2026-10-22 |

The first Squire's Cut moved pose to pose: the owner found the hand-made clip it replaced "much smoother". GenRA
now measures flow against 44 hand-made attacks ([toolkit README, "Flow"](../../tools/genra/README.md)); the bound
revision stops 1.0 times a joint a second, 15% of the body together and 1% frozen, inside the hand-made range.
Against the old Reverse Slash, the hit-reliability report has it at 1.00× playback instead of 1.42×, all 13 active
ticks touching a target 1 m ahead instead of 3, and reach 0.30–2.75 m against FightGuy and Manki (0.30–2.95 m and
0.30–2.35 m before). The move data is unchanged; the move was renamed from Reverse Slash to Squire's Cut.

The Barn Door took four sets of keys. Take 2's hands folded at his belt, and the third keys locked the left hand to
one exact grip, which winged both elbows up in front of his face. The owner's fix: the hands only need to be "side
by side, as long as they're both approximately on the handle". GenRA now has a loose two-handed hold with the elbows
kept down, and carries the plank both ways between keys ([toolkit README](../../tools/genra/README.md), "Carry fixes").
One smoothing pass, keeping the approved poses, brought it to 0.61 stops a joint a second and 15% of the body
together. Against the old Greatsword Swing, the hit-reliability report has it at 1.00× playback instead of 0.95×,
the same 3 of 15 active ticks touching a target 1 m ahead, and reach 0.75–3.35 m against FightGuy (0.30–2.80 m
before) but **never** against a standing Manki (1.00–2.00 m before): the plank sweeps about 1.4 m off the floor,
over the 1.5 m fighters (Manki and Bashie). The move data is unchanged; the move was renamed from Greatsword Swing to Barn Door. Its
hitbox (a 0.5 m capsule along the plank) needs the owner's tuning.

Sunrise took three motion runs. The first parked the plank (an arm-only swing, too narrow an arc), so the brief
asked for a rainbow from in front of him, over his head and onto his shoulder, with a real torso turn and a
pivoting back foot, and the clip was measured against the game's hurtboxes (`tools/genra/hitarc.py`) before it was
shown. Against the old Reverse Rising Slash, the hit-reliability report has it at 1.00× playback instead of 0.72×,
5 of 20 active ticks touching a target 1 m ahead (8 before), and reach 0.30–3.40 m against FightGuy and 0.30–3.10 m
against Manki (0.30–2.15 m and 0.30–1.75 m before); the arc map reaches targets standing in front and up to 3 m
above the floor. The move data is unchanged; the move was renamed from Reverse Rising Slash to Sunrise.

The BONK took one motion run on the approved keys (a one-leg tell, a stamp, the plank over the top and flat onto the
floor). The hit-reliability report has it at 1.00× playback instead of 0.79×, all 16 active ticks touching a target
1 m ahead instead of 4, and reach 0.30–3.35 m against FightGuy and Manki (0.30–3.10 m and 0.30–3.00 m before). The
move data is unchanged; the move was renamed from Heavy Double Slice to The BONK.

### Aerial normals — approved direction and timing, 2026-10-22

The owner approved aerial partners for all four ground normals, with new clocks. All four aerial normals
are integrated and owner accepted. Each uses the airborne fall
pose for entry and recovery, with torso and leg counterbalance instead of planted feet. Ordinary
gravity stays in effect. The plank's path through the active window determines its coverage.

| Slot | Motion | First hit | Active ticks, inclusive | Recovery ticks | Free / end | Landing lag | Late auto-cancel from |
| --- | --- | ---: | --- | --- | ---: | ---: | ---: |
| `air.1` | Squire's Cut — quick one-handed diagonal cut in front | 12 | 12–21 | 22–39 | 40 | 12 | 34 |
| `air.2` | Barn Door — broad two-handed horizontal sweep | 18 | 18–29 | 30–49 | 50 | 16 | 42 |
| `air.3` | Sunrise — low-to-high scoop through the front and overhead | 20 | 20–33 | 34–53 | 54 | 18 | 46 |
| `air.4` | The BONK — loaded two-handed chop below his boots | 28 | 28–37 | 38–65 | 66 | 24 | 58 |

All values are 60 Hz ticks from entry tick 0. Entry auto-cancel is ticks 0–3 for each move;
late auto-cancel starts when its fall pose is restored. Clip duration equals the free tick,
without a long neutral tail. Damage, launch angles and hitbox tuning are separate from this
timing approval. Author one move at a time: review its actual-rig key poses before motion,
then review its animation before live binding or gameplay integration.

`air.1` uses the [Squire's Cut brief](../../tools/genra/briefs/bonk-sir-a1-squires-cut.json).
The owner approved its keys and first motion take on 2026-10-22 ("looks good keep it").
The accepted Humanoid clip is `bonk_sir_a1_squires_cut.anim`, bound to `anim.bonk.a1`,
cooked at 1× with the clock above and a refreshed roster pin. Damage, launch angle,
knockback and hitbox radius are unchanged; the slash effect follows active ticks 12–21.
The motion check passes with advisory clearance warnings at ticks 6 and 24 and two flow
warnings; the owner kept the take after actual-prefab playback review. The full Shared
suite passes (3,324 passed, two skipped), including cooked-path hit, shield and miss checks.
Ability Lab confirms an 8-damage contact and the return to airborne idle after recovery.

`air.2` uses the [Barn Door brief](../../tools/genra/briefs/bonk-sir-a2-barn-door.json), the
approved 50-tick clock and loose two-hand grip. The owner approved its repaired r1 keys
and first motion take on 2026-10-22 ("looks very good"). `bonk_sir_a2_barn_door.anim` is
bound to `anim.bonk.a2`, cooked at 1× and pinned in the refreshed roster. Its current
9 damage, 30° launch angle, knockback, stun and radius are preserved; the slash effect
follows active ticks 18–29. The motion check passes with the disclosed contact and flow
advisories, including the handle near the torso at tick 22; the left arm is nearly straight
at the finish. The 58 focused tests and full Shared suite pass (3,339 passed, two skipped),
including low-aerial hit, shield and miss coverage. Ability Lab confirms the integrated
clip and recovery; its higher aerial start misses the standing dummy as expected.

`air.3` uses the [Sunrise brief](../../tools/genra/briefs/bonk-sir-a3-sunrise.json).
The owner accepted repaired r1 keys and the first motion take ("Pretty good").
`bonk_sir_a3_sunrise.anim` is bound to `anim.bonk.a3`, cooked at 1× on the 54-tick
clock above, with the roster pin refreshed. Damage 11, launch −45°, knockback, stun
and radius are preserved; damage/launch tuning remains outside this animation pass.
Approved keys are unchanged; the motion pass fixes the overhead left-hand drift.
The disclosed early contacts, minor Humanoid overrun and advisory flow warnings remain.
Cooked low-aerial hit, shield and whiff checks pass, as does the full Shared suite
(3,342 passed, two skipped). Ability Lab confirms the integrated
clip and recovery, restoring its previous Bashie workspace afterward. The rising arc has
5/14 active ticks touching the report's target 1 m ahead, versus 16/16 for the old
Downward Sweep; it reaches higher and only 0.1 m below the feet versus Manki. This is
the approved change to an upward role; air.4 provides the deep downward chop.

`air.4` uses [The BONK brief](../../tools/genra/briefs/bonk-sir-a4-the-bonk.json).
The owner accepted revised motion r1 on 2026-10-23 ("very good"). The accepted Humanoid
clip `bonk_sir_a4_the_bonk.anim` is bound to `anim.bonk.a4`, cooked at 1× on the 66-tick
clock above, with the roster pin refreshed. Its 14 damage, 25° launch, knockback, stun and
0.5 m radius are preserved; the heavy slash follows active ticks 28–37.

The revision restores the overhead tip arc and removes the hand flip by changing only
load/contact arms and weapon poses at 24/28. Other approved keys and the body/leg channels
at those two ticks are unchanged. The plank cuts below both boots, then heaves back outside
the right shoulder. The full motion check passes with disclosed helmet/torso proximity,
wrist, minor Humanoid and flow advisories. Cooked low-aerial hit, shield and whiff checks
pass. Hit reliability measures 10/10 active ticks at the 1 m probe (previously 3/4), reaches
2.7 m below the feet against FightGuy and 2.2 m against Manki (previously 1.7/1.3 m), and
reports no sampled collision loss. Ability Lab confirms a single 14-damage hit and recovery with the integrated clip,
then restores the prior Bashie workspace. The full Shared suite passes (3,375 passed,
2 skipped). Training spacing, game feel and landing recovery remain
human checks in `TESTING-UNITY.md`.

### Swing on the hitbox, 2026-11-01

The owner found the normals awkward. Measured on the cooked moves, four of the six
swung the plank before their hitbox opened: their clips keyed the contact pose on the
first active tick, so the window covered a plank that had nearly stopped (Air BONK's
whole chop fell in the four ticks before it). No new motion was authored. Air BONK,
The BONK and Air Sunrise were retimed with `unity_clip.py write --retime` from their
accepted GenRA results, then the windows moved onto the swing:

| Slot | Retime (source:new ticks) | Active ticks | Other changes |
| --- | --- | --- | --- |
| `ground.1` Squire's Cut | none | 11–23 → 10–16 | |
| `ground.4` The BONK | `0:0,46:46,48:50,53:54,118:118` | 48–63 → 47–54 | |
| `air.1` Squire's Cut | none | 12–21 → 11–21 | late auto-cancel 34 → 28: a short hop lands clean when pressed up to 3 ticks late |
| `air.3` Sunrise | `0:0,6:6,12:14,18:16,54:54` | 20–33 → 15–30 | launch −45° → 75° (up); glint 12 → 8 |
| `air.4` The BONK | `0:0,24:25,28:32,38:38,66:66` | 28–37 → 28–33 | launch 25° → −70° (spike) |

Slash effects and startup aim correction follow each new first hit. The hit-reliability
Swing columns now flag none of the six (share of the turn inside the window: 22%, 32%,
25%, 30%, 42%, 27%; before 12%, 32%, 15%, 18%, 38%, 8%). Squire's Cut reaches 0.30–3.20 m
(2.85 before) with 7 active ticks instead of 13; Air Sunrise reaches 1.3 m below his feet
(0.7 before); Air BONK reaches 2.2 m below (2.7 before). Its window first ended at 35; independent QA found 27 of
141 grounded hits landing at ticks 34–35, when the plank had stopped and Bonk fell onto the target, so it ends
at 33. QA also measured the real short hop: the earliest air.1 lands on its tick 30, so 32 still took the lag.
The BONK still ends on the October idle, 0.47 m from the idle approved on 10-28; the
retime keeps its last frame. Training feel is Slop Factory check #53. Backups of the
replaced clips are each promote run's `before.anim` under
`artifacts/genra-unity-clips/20261101-1620*-promote-*`.

### Rising Cut replaces Sunrise, 2026-11-01

The owner: Sunrise's path was the problem, not its frame data ("the coverage and overall flow of the move is
unclear"). From the gameplay camera, Sunrise swung up and over in the plane the camera looks along, so its hit
happened behind his own back, and its wind-up travelled further than its hit. Rising Cut is a one-handed cut from his
left hip up across his front to high on his right, the mirror of Squire's Cut, so it crosses the screen.

- Ground: brief `tools/genra/briefs/bonk-sir-g3-rising-cut.json` (v2 pack `bonk-unity-sirv2rise`, authored 120 ticks).
  Keys v2 run `20261101-180227` (owner OK), motion take 2 `20261101-181636` ($0.77). Written at game length with
  `--retime 0:0,120:60` and promoted over `bonk_sir_g3_sunrise.anim` (same GUID, so the binding is unchanged).
  ground.3: 60 ticks, hitbox 15-19 (radius 0.5, no offset; QA #56 found 13-14 hit from the plank still cocked out to
  his left), IASA 38, glint 6, plant 11, aim correction to 15.
  Hit report: reach 0.30-2.40 m vs FightGuy (Sunrise 3.15 m), nothing behind him (Sunrise covered 2.5 m behind),
  swing flags clear. At game speed the unity_clip checks flag a fast right-hand roll at game ticks 4-6 (the draw);
  the authored take passes.
- Air: brief `tools/genra/briefs/bonk-sir-a3-rising-cut.json`, keys `20261101-181151` (owner OK), motion take 1
  `20261101-200615` ($0.28, passes). The owner asked for it a bit slower than the planned 40 ticks: written with
  `--retime 0:0,80:48`, promoted over `bonk_sir_a3_sunrise.anim`. air.3: 48 ticks, hitbox 13-23 (opened one tick
  early so the fastest step is swept), auto-cancel from 24 (QA #56: at 34 a short hop never landed clean), landing lag 18. Reach below his feet -2.6 m vs FightGuy
  (Sunrise -1.3 m); short hop vs a grounded FightGuy hits 88/180 presses (Sunrise 44/180). Check #56.
- `NyxManorTests.EveryTargetIsInOrdinaryAttackReachOfEveryWay(Bonk)` now fails on `goth_balcony_2`: the test stands
  Bonk 0.6 m from the five-hit gargoyle, startup aim correction turns him to it, and each normal's single press hits
  the gargoyle. Sunrise used to pass only because its hitbox also reached behind him.

## Pot-Lid Throw (Q, canonical A)

The lid has a 0.75 m radius. Outbound homing bends at most 1.25 degrees per tick toward the target
captured at spawn. After 34 ticks, stage contact or a shield, it turns toward Bonk and clears outbound homing.
Each fighter can be hit once on each leg. A return hit deals 5 damage with horizontal knockback 20 + 0
and fixed Hitstun of 48 ticks. This is ordinary knockback, including weight and DI, rather than a guaranteed delivery.

A landed, unshielded fighter hit records Challenge on Bonk for 120 ticks. The latest accepted lid hit wins.
The record names the target and its death count. Two Bonks have independent records.

A lid that hit a fighter during its return is caught even while Bonk is busy.
An empty return still self-bonks a busy Bonk with the existing zero-damage, 30-tick daze.
The slot waits for its one lid, and Bonk's KO removes the lid. The projectile visual needs a 5/3 scale increase.

## The Joust (R)

`slop.ability.steered-charge.v2`, version `2`, owns the entire activation from tick zero.
It consumes Challenge at the press, valid or invalid, and takes the valid challenged target or the current target.
The unarmoured rear lasts the stage's 18 ticks, or 9 when challenged. Bonk faces the target during the rear.

The armoured gallop travels at 18 m/s for at most 150 ticks. Neutral ground input tracks at 240 degrees/s,
or 420 when challenged. The stick overrides for that tick at 240 degrees/s.
Tracking gives up permanently when the target passes more than 90 degrees off the heading.
The November 1 follow-up adds optional close assist during Gallop and AirStrides only.
Inside 3.5 m horizontally and a 70-degree half cone, it turns at 900 degrees/s and overrides the stick for that tick.
The selected or challenged fighter wins when eligible; otherwise the nearest eligible enemy wins, with ID breaking ties.
Carrying never assists. Targets behind the cone escape. These are untuned first-pass values.
The carry clock stays at 36 ticks.
Air strides use only the stick and last 45 ticks, followed by a 20-tick look-down.

The plank capsule, radius 0.6 m, and root-relative chest capsule, radius 0.65 m, share one hit history.
A shield blocks contact and starts the miss ending. Armour, an existing pair, a held cut or Swept prevents the scoop;
the contact instead takes the ordinary hit route and Bonk takes his hit ending.

An admitted contact deals 3 scoop damage and opens the shared command hold.
Bonk keeps his gallop speed. The victim's anchor is 1.1 m ahead and 0.4 m above the ordinary hold anchor.
The post-movement pair pass places the victim from Bonk's final position through terrain collision.

The first of another R press, a wall, blocked passenger placement, a ground ledge,
36 carry ticks or the end of air strides requests the fling. Another R is consumed, never buffered as another Joust.
A ground carry stops at the edge and flings outward. An air carry can fling offstage.
The launch deals 12 damage, angle 40, knockback 12 + 36 and stun 20, credited to R.
The ground fling lasts 18 ticks. An air fling enters look-down immediately.
The miss ending lasts 30 ticks with a 12-tick skid.

A damaging third-party hit on the passenger, a grab on Bonk or a KO breaks the pair through ordinary interaction rules.
The passenger retains the interrupting hit's reaction. There is no mash-out.

## Leap of Faith (E)

`slop.ability.targeted-leap.v2`, version `2`, is a tap with `aimMode: none`.
It chooses its target at the press and runs Rise, Hang, Drop, Fall and Landing.

- Rise eases upward 6 m over 18 ticks, from ground or air. A ceiling constrains the body.
- Hang lasts 24 ticks with gravity off. The spot follows the chosen target's feet,
  clamped to 12 m horizontally from takeoff and supports below Bonk. Without a target,
  it uses 6 m along the stick or facing.
- Drop locks the spot and travels toward it over 18 ticks through the ordinary swept capsule mover, never upward.
  Fighters' bodies don't stop the drop, and during Landing he keeps his spot while they are pushed aside, so a target at
  a ledge can't hold him over the void.
  A late escape can avoid the locked landing. Walls constrain travel.
- A drop that ends without support enters Fall with ordinary gravity. After 60 fall ticks,
  the move ends with no slam. An interrupted rise or hang also leaves no delayed slam.
- A real touchdown spawns the existing 2 m, 13-damage ring once, at actual feet.
  A miss or shield keeps the full 68-tick stuck-plank recovery. A confirmed unshielded fighter hit
  cuts remaining recovery to 18 ticks and seeks the landing clip to tick 40.

`ActiveMoveView.Spot` publishes the light during Hang and Drop. A client presenter reads this state every frame;
it must clear the light when the spot disappears, including rollback and interruption.
The cursor aim and the old flight pose predictor are removed.

## Blade Storm (F)

The November 1 follow-up repeats the accepted steady turn twice more, preserving its 14-tick cadence.
The plank is behind at ticks 43, 57, 71, 85 and 99 and ahead at 50, 64, 78, 92 and 106.
These passes are measured from the spliced clip in `artifacts/sir-bonk-f-20261101/plank-passes.json`.
Both slots have holds at 8, 29, 43, 57, 71 and 85, then the 12-damage finisher at 99 for 16 ticks.
The full string is six 2.5-damage hits plus 12, totalling 27 damage.
Heavy slash starts at 100, dizzy starts at 116 for 24 ticks, and aim turns off at 122.
The stage ends at 146 for 1:1 playback: the clip's dizzy tail after tick 122 is retimed to half its length, and the dizzy cue runs from 116 for 30 ticks. A stage shorter than its clip speeds the whole clip up rather than cutting it, so shorten the clip, not the stage.
Wind and dust last 28 ticks longer. Mobile aim speed is 0.7 of movement speed, previously 0.35.
These are untuned first-pass values. The previous clip is backed up under `artifacts/sir-bonk-f-20261101/backup/`.

## Presentation handoff

All new IDs use the `presentation.bonk.` prefix.
`joust.challenge`, `joust.scoop` and `joust.bonk` are capability phase cues.
`faith.halo` starts the overhead hold and `faith.lock` marks commitment.
`challenge.marker` is declared for the state-driven marker over the challenged fighter.
Use an existing gold-tinted ring for the marker and spot stand-ins.

Rise uses `anim.bonk.ge-rise` (`bonk_sir_e_rise`, ticks 0 to 40 of the leap clip resampled into the 18-tick rise, so it ends on the hang pose); hang and drop use the approved sword-first `anim.bonk.ge-drop`, including the air move. Hang holds tick 0. Before the rise clip, the rise showed the leap up to tick 17 and the hang snapped to tick 40's pose.
Landing uses `anim.bonk.ge-land`. Joust keeps the existing rear, gallop, stop and look-down bindings.
New aliases `anim.bonk.gr-fling` and `anim.bonk.ar-fling` should bind the existing
`anim.bonk.g4` and `anim.bonk.a4` clips. Aliases keep the rear as the first sorted stage clip.
No Unity catalog or cooked payload has been changed here.

## Package and history

Authoring lives under `client/Unity/Assets/CharacterPackages/bonk/`.
`character.json` owns gameplay and semantic declarations. `CharacterAssetCatalog.asset` owns Unity bindings.
The next cook requires runtime API `1.22.0`; old cooked v1 specials are rejected.
World Snapshot v25 stores Challenge, lid return values and complete capability phases.

Sir Bonk's identity and normals were approved in October 2026.
The November 1 Training prototype replaces the first Joust and cursor leap under ADR-0059.
Training feel, avatar poses and the final effects remain human acceptance work.

## Size and floor contact — 2026-10-17

Bonk is now the biggest fighter in the [roster size](adding-a-new-character.md#roster-size)
order: `visualScale` 3 → 3.3, `modelYOffset` 0 → 0.24 and `hipHeight` 0.82 → 0.92.
His idle head is at 1.64 m (in-game 1.35 m before, level with FightGuy). The old
offset left his feet 14 cm in the floor in idle frame 0 and 31 cm in the run. The cook
now samples poses at `visualScale`, so his standing hurtboxes are 10% larger and follow
the higher hips. Capsule, shield radius, capture geometry and the greatsword's length are
unchanged; his bigger arms carry the blade tip 13–20 cm (6–7%) farther in the normals.

- **Run:** clip `Run_ver_B` in the ignored `Art/Characters/bonk/Animations/run.FBX` has a
  root Y offset of −0.189 (import setting). Planted frames sit 0–3 cm above the floor;
  two single-frame foot pops reach about −5 cm.
- **Crouch:** crouch hurtboxes follow the rendered pose, and the old crouch sank 23 cm,
  which was most of its duck. It now sinks 10 cm: the crouched head is 8.5 cm below the
  standing head hurtbox (was 20 cm), and the raised sword hand stays at standing height.
  The crouch-ducks-the-top-hurtbox Shared row for Bonk was removed; a deeper crouch pose
  is the fix.
- **Still open:** idle dips 8 cm into the floor mid-cycle; several attack, hit and air
  clips still dip 15–37 cm (`g3`, `gr`, hit reactions, `a1`, jump). In idle, run, jump and
  most attacks his head also drops below the shoulder line (12 cm above the upper chest,
  31 cm in rest and hit clips), so he reads as headless. These are clip retarget issues;
  list them with the [grounding tool](../../tools/character_sizes/README.md).
