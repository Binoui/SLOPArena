# Bonk

> **Identity (owner, 2026-10-20): Sir Bonk.** He knighted himself. A big, good-natured brute in
> homemade armour: a bucket for a helm, a plank for a sword, pot lids on his shoulders and a
> bedsheet cape with a crown he painted himself. He wants to be a hero, protects anyone smaller
> than him (everyone) and can't tell a windmill from a giant. Big hits, easy to play. **BONK!** is
> his battle cry, and the gags are on him.
>
> **Before the rework (owner, 2026-10-17):** a big, simple, good-natured brute, the "big hits"
> character and one of the easier fighters to play. His name is the sound his hits make,
> so a comic **BONK!** is his brand. The gags are on him: he's dense. His headbutt rings his
> own bucket, and when armour shrugs off a hit a little "?" pops over his head. His colour
> is the bucket red of his helmet and pauldron over bandage cream; steel was too plain.

## Status

Bonk is a package-native greatsword kit admitted to the built-in roster.
It exercises the authoritative Shared compiler, trusted built-in capability
admission, cooked package loading, Character Select discovery, and match
content admission. Avatar visual/pose review remains a release prerequisite.

## Sir Bonk kit — 2026-10-21

The owner approved this kit for the Sir Bonk rework on 2026-10-21. The engine for Q and R is built
(a returning projectile and `slop.ability.steered-charge.v1`, see the
[engine plan](../plans/2026-10-21-sir-bonk-pot-lid-and-joust.md#built-2026-10-21)). R plays
[the Joust](#the-joust-r--wired-2026-10-21) since 2026-10-21; Q still plays Shoulder Bash until the lid's model,
animation and effects are in. Each special is something from his book, done completely seriously, that goes a
bit wrong for him, and what goes wrong is the opening.

| Slot | Special | Shape | The opening |
| --- | --- | --- | --- |
| Q (`A`) | Pot-Lid Throw | He skims the pot lid off his left shoulder like a frisbee. It hits the first fighter it meets (DOOONG), curves back and hits on the way back too, knocking them toward him, and he catches it. One lid: no second throw while it is out. | If he is busy when it comes back, it rings off his own bucket and he wobbles under a "?". |
| E | Leap of Faith | Kept: the aim, the plank-first slam on a ring, the stuck plank and the handstand ([phase clips](#targeted-jump-slam-e--sir-bonk-phase-clips--2026-10-21)). | The handstand and hauling the plank out. |
| R | The Joust | He pulls out a hobby horse, rears and does the neigh himself, then gallops a long way with the plank couched as a lance and armour on. The first fighter he meets is unhorsed (BONK!). In the air he gallops a few strides on thin air, looks down and drops, which makes it a sideways recovery. It replaces Charging Double Slash; Shoulder Bash's snort and armour move into it. | A miss: he skids to a dramatic stop (the owner dropped the turn-round, 2026-10-21). |
| F | Blade Storm | Kept as tuned on 2026-10-18, with a new clip on his body. | A tiny windmill spins over his bucket until he can act. |

New pieces: a projectile that returns to its thrower and can hit him (Q), steering and a skid after a
miss (R), a hobby horse model, and his left pauldron as its own piece of the model so the lid can
leave his shoulder.

His effects look homemade, like the pictures in his book: paint, wood, tin, straw and dust, with no
energy and no glow.

- Cream, from his cape and tabard, is the base. Plank red stays the hit colour: BONK!, smears and
  thunks. His blue paint is only for heraldry (crowns, pennants, lettering), because a lot of blue
  reads as FightGuy's ki.
- His crown is his mark: painted on the lid and on his finisher. On E the owner picked a holy light
  instead (2026-10-21): a crown flat on the floor reads as a squashed strip from the low match
  camera.
- He makes his own sounds: a neigh, coconut clip-clop, a pot-lid DOOONG, a windmill creak, a cork pop.
- Each move has one loud moment: DOOONG on Q, the holy light on E, NEIGH! on R, the sails on F.
  BONK! marks the big hits.
- From the [BONK! pass](#combat-presentation--2026-10-17), BONK!, the thunks, dust, cracks, the "?"
  and the bucket ring stay. Steel chips become wood splinters, the bull snort becomes his neigh,
  Blade Storm's smears become windmill sails and the plank's smear becomes wet paint.

## Sir Bonk normals — 2026-10-21

The owner picked four new ground normals from a design board (`tmp/bonk-normals/`), one swing direction each:
1 Squire's Cut (a fast one-handed slash off the shoulder), 2 Barn Door (a flat two-handed sweep), 3 Sunrise (a
rising anti-air slash, without the board's crouch) and 4 The BONK (an overhead kill strike). Normals stay
simple, without gags. Each move keeps today's move-data clock and is authored with GenRA one at a time, from a
Seedance 2.5 reference the owner approved (`artifacts/video-refs/sir-bonk-normals/`, front three-quarter view).

| Slot | Clip | Status |
| --- | --- | --- |
| `ground.1` | `Animations/bonk_sir_g1_squires_cut.anim` (`anim.bonk.g1`, 60 ticks, 1× playback) | Bound and cooked 2026-10-21 |
| `ground.2` | `Animations/bonk_sir_g2_barn_door.anim` (`anim.bonk.g2`, 93 ticks, 1× playback) | Bound and cooked 2026-10-22; misses a standing Manki (below) |
| `ground.3`–`ground.4` | the old Bonk clips | Next |

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

## The Joust (R) — wired 2026-10-21

Both R slots play `slop.ability.steered-charge.v1`. The owner approved the Seedance references
(`artifacts/video-refs/sir-bonk-r-joust/`), the GenRA keys and the timing.

| Phase | Clip (semantic ID) | Game rule |
| --- | --- | --- |
| Mount | `bonk_sir_r_mount` (`anim.bonk.gr`, `anim.bonk.ar` in the air) | The 31-tick stage. The charge starts at tick 30 and the rear is the tell: no armour. The air version keeps half gravity until then. |
| Gallop | `bonk_sir_r_gallop` (`anim.bonk.gr-gallop`) | A 24-tick loop at 12 m/s for up to 60 ticks, steered at 180°/s, armoured. The lance is a 0.5 m capsule on the plank (`_weapon_hilt`–`_weapon_tip`): 11 damage, 40°, knockback 8/30, 18 stun. |
| Hit | `bonk_sir_r_hit` (`anim.bonk.gr-hit`) | He stops dead on the hit, then 36 ticks of the proud rear. The hit is the big BONK! (`presentation.bonk.heavy.hit`). |
| Miss | `bonk_sir_r_stop` (`anim.bonk.gr-stop`) | 36 ticks, with an even 18-tick skid from 12 m/s and the dust effect. No turn-round (`turnAroundOnMiss: false`, the owner's call). |
| Air | the gallop, then `bonk_sir_r_lookdown` (`anim.bonk.gr-lookdown`) | 24 ticks of strides on thin air, then a 20-tick look-down held in mid-air, then his fall. A hit in the air also leads into the look-down. |

- **Horse:** `sir_bonk_hobby_horse.prefab` (1.9 m) rides `mixamorig:Hips` on attack slot R
  (`BonkWeaponAttachConfig`), placed as it rides on his idle.
- **Effects:** built 2026-10-21, see [the Joust's effects](#the-jousts-effects).
- **Gaps:** the air mount reuses the ground clip.
- **How the clips were made:** a GenRA pack `bonk-unity-sirjoust` carries the horse as joint Weapon1 for the
  animator. Its briefs are `tools/genra/briefs/bonk-sir-r-*.json`, and the review renders are in
  `artifacts/genra-unity-clips/`. The owner kept the gallop's round 0 over the reviewed revision, which hunched him
  like his run.

## The Pot-Lid Throw (Q) — wired 2026-10-22

He skims his left pauldron, a pot lid, off his shoulder like a frisbee. The lid flies out flat, hits, curves
back, hits again on the way and returns to his shoulder.

| Part | What | Notes |
| --- | --- | --- |
| Clip | `bonk_sir_q_lid_throw` (`anim.bonk.ga`, `anim.bonk.aa` in the air) | 41 ticks. GenRA revision r1 (he leans into the throw), cut from 36 ticks with a slower wind-up (owner, 2026-10-22). Grab at 6, release at 19, can act at 35, idle from 38. |
| Throw | `spawnProjectile` on tick 19 with a `return` block | 15 m/s out for 22 ticks, back at up to 16 m/s turning at 80 m/s². Standing still, he catches it around tick 84. 5 damage, 35°, knockback 4/12, 14 stun, radius 0.45. The self-hit stuns for 30 ticks with no damage. The hit numbers are first values for Training. |
| Aim | `startupAimCorrection` 0–16 | Up to 45° of yaw toward a target, no pitch. The lid flies where he faces. Auto-aimed projectiles (`aimedProjectile`) would snap his facing at the release. |
| Height | launch (-0.24, 0.45, 0.97) from his capsule centre | About 1.3 m off the floor, 20 cm under his hand. From his hand (1.5 m) it flew over Manki; this height hits all four fighters on both legs. |
| Air | the same clip, half gravity for 30 ticks | As the Shoulder Bash did. |

- **The lid:** `sir_bonk_pot_lid.prefab` is split out of the body (`tools/blender/bonk_rig` step 8). In
  `BonkWeaponAttachConfig`, the shoulder lid rides `mixamorig:LeftArm` with `HiddenBySlot` 11 from tick 6: it leaves
  at the grab and stays off while his returning projectile is out. A second entry, on `mixamorig:LeftHand` for
  slot 11, shows from the grab to the release (`ShowFromTicks` 6, `HideAfterTicks` 19).
- **In flight:** `BonkPotLidProjectile` (the same lid under a `PropSpin`, 3 turns a second) is the Q projectile
  visual, ground and air, in `ProjectileVisuals.asset`.
- **Not yet:** the effects. Concepts come first for the DOOONG on a hit, the catch clang and the self-hit "?". Q
  shows the normal hit and nothing on the catch. The Shoulder Bash cues (`presentation.bonk.bash.*`) are no longer
  used.

## Sir Bonk effects — 2026-10-21

The owner approved these from concept paint-overs on real captures (`tmp/vfx/sirbonk/concepts/`)
and picked the holy landing over a crown of his paint or of splinters. `BonkPresentationBuilder`
generates them on top of the [BONK! pass](#combat-presentation--2026-10-17); only
`presentationIds` and one cue per F slot changed in `character.json`. The "after" captures land the
same hits as the "before" ones (E at frame 89, F at 19, 41 and 60).

| Move | Presentation |
| --- | --- |
| Every hit | Contacts throw wood splinters instead of steel chips: it's a plank. The armour's "?" keeps its steel chips, since his armour is tin. |
| Leap of Faith (E) landing | Six attack ticks after touchdown, once he is up in the handstand and past the impact's BONK!, a shaft of storybook light comes down on him and the stuck plank, with rays fanning from its foot, a few gleams and a choir "aah" (`bonk_choir`). It is flat painted cream, alpha-blended with no glow, drawn a metre behind him so he stays in front of it, and it is gone at tick 40 when the haul starts. At tick 53 the plank comes out with a cork POP! in his blue paint (`bonk_pop`), a puff and splinters at the hole, 1.3 m in front of him. Both live in the landing cue (`presentation.bonk.slam.landing`) and run on his attack clock through `AttackTickWindow`, so Hitstop delays them with the clip and a hit that knocks him out of the landing clears them. |
| Blade Storm (F) spin | The three red smears are four windmill sails: cream canvas on a wooden lattice with a spar along the outer edge, each the camera-facing trail of a particle orbiting him (about 2 m of trail each, so they stay apart). A wooden creak (`bonk_creak`) plays under the whum-whum. |
| Blade Storm (F) recovery | `presentation.bonk.storm.dizzy` at tick 60 of both F slots: a tiny windmill spins over his bucket and slows down until he can act, his dizzy stars, with a higher creak. |

The sounds are synthesized by `tools/sfx/sir_bonk_clips.py`. The POP! mask is
`Presentation/Source/Pop.png` (`tools/glyph_mask.py 'POP!' … --size 1024 --weight 800 --fill 0.86
--bounce 7`, Baloo 2 like the BONK!). Not built yet: the plank's smear as wet paint (with the
normals), and Q's effects (after its animation). The F spin clip is still the old Bonk's,
so the sails follow its blade path for now.

### The Joust's effects

The owner approved the recommended picks from the concept board (`tmp/vfx/sirbonk-r/concepts/`) and the sound tour
(`tmp/vfx/sirbonk-r/joust-sounds.mp4`): "Go for it, I like it picks". He does every sound himself, and
NEIGH! is the move's one word. In both R slots, three timeline cues replace the borrowed snort, and the
steered charge's gallop, miss and air-ending cues replace the bash dust. The "after" captures land the
"before" hits: the lance at frame 36, the shield at 33.

| Beat | Presentation |
| --- | --- |
| The press | `presentation.bonk.joust.horse` on his hips: a puff of straw dust, stalks flying out and a soft fwump (`bonk_poof`) hide the hobby horse appearing. The puff is full size at once and drawn half a metre toward the camera, over the horse's head. |
| The end, however it ends | The same cue plays a second puff where the horse is at that moment (`AttackEndCue`), so the horse never just vanishes. |
| The lance | `presentation.bonk.joust.pennant` on `_weapon_tip`: a cream pennant with his blue crown, tied 12 cm back from the tip (`PennantPresentation`). It hangs while he rears, streams back at a gallop and swings over when he stops dead. It comes and goes in a small puff. |
| The rear (tick 10) | `presentation.bonk.joust.neigh` on his head: NEIGH! in his lettering (cream faces, a wood extrusion, since red stays for hits) pops over his bucket, with sound lines from the eye slit and his own neigh (`bonk_neigh`). |
| The gallop | `presentation.bonk.joust.gallop` (`gallopPresentationId`): a ring of dust where he sets off. Then on every hoof (loop ticks 1 and 13, measured on the rig), a small "clip!" or "clop!" pops beside the landing foot with a coconut knock (`bonk_clip`, `bonk_clop`) and a kick of dust (`LoopFootfalls`). On thin air the knocks play without the dust. |
| A miss or a shield | `presentation.bonk.joust.skid` (`missEndingPresentationId`): both heels gouge the deck. Dark grooves on a pale lip follow each heel (`FollowOwnerBone`), with wood chips and a wave of dust that piles up ahead of the front (left) heel as he slows. He squeals his own brakes (`bonk_squeal`). |
| The hit | Unchanged: the big BONK! and splinters at the lance (`presentation.bonk.heavy.hit`). |
| Off a ledge | `presentation.bonk.joust.lookdown` (`airEndingPresentationId`): a "?" in his blue paint pops over his bucket and stays in the air as he drops. Six ticks in come a sweat drop, a small "gulp" and the gulp sound (`bonk_gulp`). Then the horse leaves in its puff. |

The masks are `Presentation/Source/{Neigh,Clip,Clop,Gulp}.png`. They use the POP! command with
`'NEIGH!' … --size 1024` and the default size with `--bounce 9` (6 for gulp) for the small words. The
builder bakes each word around its own ink. Rerunning `tools/sfx/sir_bonk_clips.py` rewrites the clips
byte for byte (bitexact Ogg). The capture rig can't play air moves, so the look-down is checked in Training.

## Blade Storm spin — 2026-10-18

Blade Storm (`ground.F` and `air.F`) now plays `bonk_gf_blade_storm.anim` (100 frames at
1×), authored with the GenRA toolkit (Opus 5.5) on the move's existing clock. The owner
approved it on the actual character. The old clip was an overhead twirl that kept the plank
2.3–3.4 m up, so its four big hits passed over every standing fighter and only the long chip
hitbox landed.

- He drops into a wide, low stance and turns three full circles to his left with the blade
  flat, 1 m off the floor, from tick 8 to tick 66. The blade passes behind him at about ticks
  12, 29, 43 and 58 and in front at 21, 36 and 50. He has the sword back on his shoulder by
  tick 94. In the air his legs keep the same steps.
- The whirl smears (`BonkBladeStormWind`, `BonkAirStormWind`) orbit 0.2 m above his hips,
  at the blade, instead of 0.9 m.

### Storm rework

With the new clip the owner still found the move awkward: the hitboxes and knockback were
"probably not best", and Bonk could move in the end lag. The owner chose a storm that holds
its victims (option A), first planted, then, after trying it ("much better feel!"), steerable
at a slower walk. The clip and its clock did not change.

- **Slow steering.** The spin's mobile aim state (ticks 0–65) walks him at 35% of his speed
  (`aimMovementSpeedScale` 0.35: 3.5 m/s on the ground, 2.1 m/s in the air), and the walking
  stops when the spin ends at tick 66. Before, mobile aim gave him his full 10 m/s, and the move
  then kept that speed through the recovery: he slid 3.6 m after letting go of the stick at
  tick 70. The speed scale and the stop are engine options (runtime API 1.7.0; see
  [Ability architecture](../systems/ability-architecture.md)).
- **One window per turn.** Ticks 8–28 and 29–42 are hold hits, 43–58 the finisher. The turns
  change behind him, so a target in front of him or beside him meets each window once. They
  replace the 74-tick chip and the three 4-tick spin hits, which each covered part of a turn.
- **Hold, then launch.** The hold hits (2.5 damage) pull the target toward him (angle 0, base
  20, no growth) with 24 ticks of fixed hitstun, the pattern of FightGuy's Fist of Fury.
  Before, every small hit pushed the target away and out of the next window. The finisher (12
  damage, base 10, growth 36) launches at 35° instead of 25°. The string totals 17 damage (22
  before, which almost never all landed).

Measured on the cooked package:

- **Hit reliability report:** each of the three hits touches all 24 bearings around him at 1
  and 1.5 m against FightGuy and Manki (before: spin hits 7–11, finisher 13–15). Reach is
  0.30–3.50 m against FightGuy.
- **Real simulation**, one press against each of the four fighters at 1–3 m from 8 directions,
  at 0% and 60%: the full string lands everywhere, except Manki 3 m away behind him, who takes
  the finisher only. Before the rework a press averaged 10.4–13.1 damage, and behind him or on
  his left only 2.5–5 landed.
- **Walking:** holding the stick toward a fighter 1–4 m ahead, the full string lands; from 5 m
  he catches them by the second turn. Walking away drops a victim 2 m away after the first
  hit; at 1 m the pull keeps them in.
- **The review setup**, FightGuy 1.3 m ahead at 60%: hits at move ticks 19, 33 and 47. The
  first pulls him from 1.3 m to 0.7 m, against Bonk. The finisher sends him about 9.5 m out and
  2.3 m up (0%: 6 m and 1.25 m; 100%: 11.5 m and 3.2 m). He recovers 5–30 ticks before Bonk,
  far away.
- **Air F** started 1.2 m up: the hold hits pass over standing FightGuy and Wibou and only the
  finisher lands; against Bonk the whole string lands.
- **Wind-up:** nothing hits during the overhead wind-up (ticks 2–7) any more. The old chip
  reached targets up to 2.0 m above his feet there; air F now reaches 1.3 m above them.

Native Ability Lab scenarios, a Bonk dummy 1.5 m ahead:

| Move | Idle opponent | Shield | 8 m away |
| --- | --- | --- | --- |
| Blade Storm (ground) | frames 19, 37 and 56 (2.5 + 2.5 + 12) | all three blocked (frames 17, 39, 58) | miss |
| Blade Storm (air) | finisher at frame 48 (the Lab's air start is a capsule height up) | blocked at frame 47 | miss |

Bonk ends every run in Idle. In game (Splash Deck, FightGuy 1.3 m ahead at 60%, planted
version) the string lands at frames 19, 39 and 59, and the finisher's BONK! sends him off. The
Tutorial's Bonk combo hint changed with it: Blade Storm can no longer chase the launched dummy,
so the hint is now Reverse Rising Slash → jump and Reverse Air Slash (see
[Tutorial](../design/tutorial.md#findings-that-shape-the-lessons)).

### Before the rework

Bearings (of 24) at which each hit touched a standing target with the clip's original
windows, from `HitReliabilityReport --char bonk --slots F`, old twirl → new spin:

| Hit | FightGuy 1 m | FightGuy 1.5 m | Manki 1 m | Manki 1.5 m |
| --- | ---: | ---: | ---: | ---: |
| Chip, ticks 2–75 | 23 → 24 | 13 → 24 | 8 → 24 | 5 → 24 |
| Spin hit, tick 16 | 0 → 11 | 0 → 8 | 0 → 9 | 0 → 8 |
| Spin hit, tick 24 | 0 → 10 | 0 → 7 | 0 → 8 | 0 → 7 |
| Spin hit, tick 32 | 0 → 11 | 0 → 9 | 0 → 9 | 0 → 8 |
| Finisher, tick 44 | 0 → 15 | 0 → 13 | 0 → 15 | 0 → 13 |

With those windows the chip's offsets (−0.2 and −0.5 m, made for the old twirl) were zeroed,
and the [hitstop fix](../systems/combat-systems.md#damage-and-hit-response) let spin hit 1
land after the chip's freeze. The windows still closed 1–2 ticks before the blade crossed his
front, and each small hit pushed the target out of the next one. See the
[Blade Storm record](../research/2026-10-18-bonk-blade-storm-spin.md).

## Whirlwind and Meteor Cleave aerials — 2026-10-17

Reverse Air Slash (`air.1`) now plays the **Whirlwind** and Downward Sweep (`air.3`) the
**Meteor Cleave**. Both were authored with the GenRA toolkit (Opus 5.5) on the slots'
existing clocks, and the owner approved them on the actual character. Their gameplay values
are unchanged. The hitboxes run from `_weapon_hilt` to `_weapon_tip`, so their reach follows
the new blade paths.

- **Whirlwind** (`bonk_air1_whirlwind.anim`, 88 frames at 1×). Both hands take the hilt and
  he winds up to his left, then turns one full flat circle to his right through the active
  window: the blade is on his right at tick 24, behind at 29, left at 34 and in front at 39.
  After a small overshoot the sword is back on his shoulder by tick 52. The blade now also
  passes beside and behind him.
- **Meteor Cleave** (`bonk_air3_meteor_cleave.anim`, 50 frames at 1×). The blade drops
  behind his back and whips over his right shoulder. At contact (tick 21) it points 45°
  down beside his right knee with his body folded forward, then hangs straight down beside
  his right foot. Because it comes down his right side, the game camera sees 48% of the
  blade at contact; a first version that chopped down his middle showed none.
- Acrobatic Double Slice (`air.4`) keeps its clip by the owner's choice. A somersault
  concept was not adopted.

Native Ability Lab scenarios on the cooked package, opponent 1.5 m ahead:

| Move | Idle opponent | Shield | 8 m away |
| --- | --- | --- | --- |
| Whirlwind | hit at tick 19, 8 damage | blocked at tick 39 | miss |
| Meteor Cleave | hit at tick 22, 11 damage, −45° spike | blocked at tick 21 | miss |

See the [GenRA Bonk aerials record](../research/2026-10-17-bonk-genra-aerials.md).

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

## Horizontal aerial — 2026-10-16

Air Cleave (`air.2`, normal 2 while airborne) now uses the approved horizontal
greatsword animation with torso, free-arm and leg motion. The sword arm reaches
forward and turns the hand horizontally with the forearm. The package-owned
native clip is bound as `anim.bonk.a2`, with the existing 53-tick duration,
contact ticks 15–28 and IASA 45. Gameplay values are unchanged by this integration.

The package was cooked, verified and refreshed in the roster. Actual Unity
playback and sword anchors match the cooked pose track; focused Shared cases
pass for a low aerial hit, shield block and out-of-range miss. See the
[authoring and integration record](../research/2026-10-16-bonk-horizontal-aerial.md)
for the approved revision and evidence.

## Combat presentation — 2026-10-17

"BONK!", built for the identity above from concepts the owner approved on 2026-10-17. His
name is the sound his hits make: his big hits print a comic **BONK!** in bandage cream on a
bucket-red balloon, the way 肉 is FightGuy's brand and the grin is Manki's. A thick red smear
follows the plank, contacts are blunt red "thunks" with ringing arcs instead of sharp sparks,
and dust, cracks and rubble give him weight. The gags are on him: he snorts like a bull before
an armoured charge, his own bucket rings after a headbutt, and a hit his armour shrugs off
gets a little "?". `sloparena.presentation.bonk.build` (`BonkPresentationBuilder.cs`, on the
shared `PresentationBuilderKit`) generates every texture, material and prefab and binds them
to the asset catalog and the weapon's trail style. The lettering comes from
`Presentation/Source/Bonk.png` and `Question.png`, rendered by `tools/glyph_mask.py` with
Baloo 2 ExtraBold (`--weight 800 --bounce 7`). Only presentation cues, `presentationIds` and
`hitPresentationId` values in `character.json` changed; damage, hitboxes and timing did not.
The workflow is the [character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md).

| Move | Presentation |
| --- | --- |
| Normals (ground and air 1–4) | A red twinkle on the end of the plank before the swing and a low "whoomp". The sword trail is a bucket-red smear with a cream streak along the outer third of the plank (`BonkSmearTrail`), so big overhead swings never hide him. Ground normals kick up foot dust. Hits use `normal.hit`: a blunt red thunk with ringing arcs, steel chips, a puff of dust and a wooden "thock". Ground and air 4 use `heavy.hit`, which adds a small BONK!. |
| Shoulder Bash (A) | At tick 0 he snorts like a bull: two steam puffs out of the bucket's eye slit and his eyes flash (`bash.snort`). Through the armour window he glows bucket red and his eyes stay lit (`armor`); the charge stomps and kicks dust behind him. Contact (`bash.hit`): the big BONK!, drawn in front of both fighters, and his own bucket rings twice on his head ("DOOONG"). |
| Charging Double Slash (R) | The same snort, armour glow and charge dust. Hits use `slash.hit`: slash 1 (4 damage) is a small thunk, slash 2 (8 damage) a bigger one with dust (`DamageScaleVariant`). |
| Armour (A and R) | When the armour absorbs a hit, a steel tink, chips and a little "?" pop over his bucket and float up (`ArmorGlowPresentation`). He didn't notice. |
| Jump Slam (E) | Takeoff: a ring of dust along the floor, rocks and upward speed lines (air version: a puff ring). Landing, wherever he lands: a dust ring, rubble that bounces once, and floor cracks that fade over 2.5 s with a "THOOM". A hit (`slam.hit`) adds the BONK!. |
| Blade Storm (F) | Three red smears orbit him at the height of his flat blade (chest height until the 2026-10-18 spin), about 2.5 turns a second, with a "whum-whum" loop; on the ground a dust tornado spins at his feet. Spin hits are small thunks; the 12-damage finisher (`storm.hit`, `DamageScaleVariant`) is the biggest BONK! with his bucket ringing. |

- **Armour read:** `ArmorGlowPresentation` asks `FighterHitFeel` for a pulsing rim glow while
  the cue lives. The cue is attack-bound and lasts exactly the authored armour window (25 ticks
  for A, 17 for R), so interruption ends it. The "?" is a detached copy of the cue's `Absorb`
  child, spawned on `FighterHitFeel.ArmorAbsorbed`, which fires for an accepted hit whose
  `ArmorProtected` is set. Under Reduced Flashing the glow is steady.
- **BONK! placement:** the balloon and letters are billboards whose pivot moves them 1 m toward
  the camera, so the fighters never cover them. Contacts scale with damage
  (`sqrt(damage/6)`, 0.6–1.6), which sizes the BONK! too.
- **Bucket ring:** `SnapToOwnerBone` moves that part of `bash.hit` from the contact onto
  Bonk's head bone.
- **Sounds:** generated by `tools/sfx/bonk_clips.py` from Kenney's CC0 Impact and Sci-Fi packs
  plus filtered noise, tracked in `Resources/Audio/SFX/Bonk/` with their `.meta` files (see
  [provenance](../assets/credits-and-licenses.md)). They are first-draft mixes. His swing
  whooshes (`bonk_swoosh_01`–`07`) and hit impacts (`impact_light`/`medium`/`heavy`) are
  synthesized by `tools/sfx/bonk_foley_clips.py`.
- **Fixed issue (gameplay, not this pass):** on an idle FightGuy only Blade Storm's first spin
  hit landed. The plank stayed above his head through the spin, so the later spin hits and the
  finisher passed over him. The [2026-10-18 spin](#blade-storm-spin--2026-10-18) swings it at
  his hips.

The generated prefabs replace the hand-made 2026-10-11 pass (cream, rust-red and charcoal
steel, a SLASH sword trail) in place, keeping their GUIDs where the names match. The pass
deleted the unreferenced `BonkSteelImpact`, `BonkBrace` and `BonkSteelSwordTrail` prefabs,
the `BonkSteelFlash` and `BonkSteelSparks` materials and the five mesh assets they used.

## Local cooldown tuning — 2026-10-03

Both explicit ground/air A variants now cost **120 ticks / 2 s**, and both R
variants cost **240 / 4 s**, instead of zero. This adds a repeat-use cost to the
armored engages without changing armor, linked contacts, reward or IASA.
E remains **240 / 4 s**; F and its air alias remain **900 / 15 s**; normals
remain zero. These are playtest tuning values, not a match-balance verdict.
Cooldown begins on completion/cancellation. Shared now rejects same-slot IASA
recasts that would bypass the pending cooldown while allowing another ready
slot to cancel normally. Source-compiled ground/air probes verified the gate,
exact timers and recast after expiry. Persisted cooked content/roster are unchanged.

## Local gamefeel tuning — 2026-10-02

Bonk is the slower, high-jumping acrobat: Run 10 m/s, air cap 6 m/s,
air acceleration 10 + 2 m/s². Jump/short-hop impulses remain 13/7.2 m/s;
gravity is 26 m/s² and fall/fast-fall caps are 13/20 m/s.
Source-compiled Shared measures full hop **58 ticks / 3.142 m** and short hop
**31 ticks / 0.938 m**. Manki has longer airtime and more air control; Bonk
has the higher arc.

Normal durations increase approximately 25%, with retimed contact windows,
IASA and auto-cancel thresholds. Ground normals retain four inactive ticks
before another ability, eight for ground.4. Bonk air.2's pre-hit cancel is
removed: duration 53, IASA/late auto-cancel 45, landing lag 18, no early
auto-cancel. Geometry, damage, knockback, special timelines and aliases are unchanged.

This is source-only Editor tuning. Cooked packages/roster remain unchanged.
Shared lifetime/IASA and representative landing probes passed; visual
Lab/Training alignment verification awaits the retained Editor owner's window.

## Targeted Jump Slam (E) — landing follow-through — 2026-10-02

Authoritative timing contract for `ground.E` and `air.E` (`anim.bonk.ge`, 56
frames at 30 fps; both stages run 112 ticks, so frame = elapsed / 2 in both
authoritative pose sampling and rendered playback):

- Landing synchronizes the move: the authoritative clock seeks to tick 56
  (frame 28) regardless of flight duration, so the sword pose and the clip
  enter the impact pose together.
- One six-tick damage window opens at landing (`parameters.hitbox.durationTicks: 6`,
  damage 13, radius 0.42 m, angle 55°, base 9, growth 32, stun 20). It is
  independent of recovery and is the only damage source — the obsolete
  operations at ticks 71/90 were removed.
- Recovery runs 52 ticks to tick 108 (frame 54, the approved endpoint; the
  import's extra frame 55 is unused), then locomotion returns — no six-tick cutoff.
- `maxFlightTicks` is 96 (human-approved): the tuned gravity 26 m/s² makes the
  leap land at tick 79, past the old 72 cap, which ended the ability mid-air and
  killed the slam. The timeout still ends a no-landing flight in the air without
  a slam.
- Horizontal speed uses the same-height ballistic airtime estimate. Starting
  above the landing surface lengthens flight and can overshoot the cursor:
  native Lab `air.E` from 1.7 m above the floor targeted 4 m but landed at
  4.733 m and whiffed. This is not a homing/guaranteed-hit move.
- Interruption (hitstun, death, cancellation) removes the activation's hitboxes
  and clears the attack marker and movement lock — no phantom impact or
  lingering lock.

Source-only Shared simulation using Bonk's baked poses produced one accepted
13-damage contact for both E variants at 4 m when air E started 1 m above
ground, and none at 20 m. The accepted native Lab ground.E scenario hit for
13 at frame 84 (impact pose tick 56), froze during Hitstop and returned to
locomotion at frame 146. Native Lab air.E from higher altitude sought tick 56
on landing at frame 91 and recovered at frame 143 despite whiffing. Recorded
frame captures show the airborne hold and distinct impact/follow-through poses;
Training and human feel acceptance remain unverified.

## Targeted Jump Slam (E) — Sir Bonk phase clips — 2026-10-21

Sir Bonk's E plays one clip per phase. This replaces four parts of the 2026-10-02 contract
above: the 112-tick stage, the seek into a single clip, the 52-tick recovery and the
hilt-to-tip blade hitbox.

| Phase | Semantic ID | Clip | Length | Clock |
|---|---|---|---|---|
| Aim (held) | `anim.bonk.ge-aim` | `bonk_sir_e1_aim` | 40-tick loop | `aimAnimationId`, `ground.E` only |
| Flight | `anim.bonk.ge` | `bonk_sir_e1_leap` | 74 ticks | ground-aware pose clock; `durationTicks` 74 = its baked frames |
| Landing and recovery | `anim.bonk.ge-land` | `bonk_sir_e1_land` | 68 ticks | real time from the landing |

- The flight always lasts the same time on flat ground, whatever the distance: the launch is a
  fixed 16 m/s up, and the horizontal speed fits the distance into it. With gravity 26 m/s² he
  touches down after 73 flight ticks. The flight clip swings the plank over his head in its last
  ~10 authored ticks (from tick 62). Shared eases through the final 18 pose ticks using the predicted
  time to touchdown, so the swing starts before contact and slows into the impact pose.
- A longer flight (landing below the launch height, or `air.E` from higher up) holds before the
  final swing until ground approaches. A higher platform brings that swing forward. The bounded
  lookahead uses the existing Shared capsule/triangle collision and gravity, with the same
  heightmap fallback as movement; walls and void do not count as landing surfaces. Only the
  replicated pose clock changes: flight timeout, trajectory, landing damage and recovery keep
  their original clocks. Single-clip targeted leaps retain their existing playback.
- At landing, `TargetedLeapAbility` moves to the stage's second clip when it lists one
  (`ComboStage` 1) and starts it at tick 0 (`landingSeekTick` 0). Recovery runs the whole
  68-tick clip. A stage with one clip keeps the old seek into that clip.
- A phase clip is one listed after the stages' own clips: `ComboStage` modulo the clip count is at
  or past the stage count. It plays in real time from the landing (the baked frame is the elapsed
  tick, clamped to the clip) for the server's hurtboxes, the client's weapon sampler
  (`PlayerRenderer.TryGetBakedWeaponPose`, which poses Sir Bonk every attacking frame) and the
  rendered clip. Abilities that only raise `ComboStage` (Manki's releases) keep their stage clip
  and its timing.
- The renderer cuts to a phase clip instead of crossfading. A crossfade from the swing's end (plank
  forward) to the impact (plank in the floor) blends them in muscle space and flips the plank up and
  back for a frame or two.
- The hit is a ring around the landing point: a 2 m sphere centred 0.35 m below Bonk's
  centre, with no bones. Damage, knockback (away from Bonk), stun and the six-tick window
  are unchanged.
- Both clips are cut from one GenRA leap (the plank stays behind his back from tick 40 to 96, then
  swings into the floor over ticks 96–100):
  - flight: `unity_clip.py write --ticks 0-100 --retime 0:0,40:40,96:62,97:66,98:69,99:72,100:73`.
    A held tick extends the clip to 74 ticks so the cook includes the impact key at tick 73;
    the flight's final sampled pose now matches the landing clip's first pose.
  - landing: `--ticks 100-148 --retime 0:0,2:8,4:12,10:30,14:40,18:46,32:52,38:58,48:68`. The
    handstand and getting back up take ticks 0–40; pulling the plank out and returning to idle
    take 40–68. Recovery went from 52 to 68 ticks.

`air.E` has no aim clip yet.

## Grounded hit coverage

The 2026-10-12 hitbox retune broadens ground.3's blade capsule to 0.65 m
and lowers both endpoints to -0.35 m. Ground R's first slash keeps its
0.5 m radius and lowers both endpoints to -0.15 m. The finisher and
aerial variants retain their existing geometry.

The sampled idle-target grid connects ground.3 against Manki at 1.5 m
directly ahead and both ground R contacts through 3 m. These are sampled
spacing bounds, not exact reach limits. Seven R positions at +50/+60°
lose one contact while still hitting; moving-target and Training feel
acceptance remain separate from the automated coverage check.

## Special roles — provisional tuning

Q maps to canonical `ground.A` / `air.A`, not normal slot `3`.
Both variants are Shoulder Bash: **6 damage, base 5, growth 20**, with
armor on authored ticks **12–36**. It is a short forward check, not the
kit's high-percent finisher.

R is Charging Double Slash: a committed forward engage with two independent
sword contacts. Hit 1 at ticks **22–28** deals **4 damage, base 2, growth 0**
and opts into **24 fixed hitstun ticks** to retain proximity without a
percent-scaled launch. Hit 2 at **42–50** deals **8 damage, base 7, growth 28**
at **35°**. Armor covers only **12–28**; the second swing and recovery are exposed.

Both R variants opt into `forwardLunge.stopInAttackRange`: the 10 m/s,
12-tick lunge skips or stops when a forward opponent's current hurtbox overlaps
the first slash's baked active-window geometry. It stops once, without chasing
or resuming, and does not change swing timing, armor, vertical motion or recovery.
Opponents outside that geometry retain the full lunge; proximity alone is not a hit.

Native ground-R mirror checks: at 1.2 m Bonk stayed at the origin and hit at
frames 22/48; at 2.5 m he stopped after 1 m and hit at frames 23/48.
At 12 m he travelled the full 2 m and missed. Elevated air-R versus a grounded
dummy still respects vertical reach; this is not a guaranteed aerial connection.
Persisted-package Shared checks with both fighters at equal elevation also
landed both air-R swings at 1.2 m without travel and at 2.5 m after 0.833 m
of travel. Ambient gravity was disabled for that isolated aerial reach check.

Armor retains incoming damage and hitstop, but prevents ordinary launch,
hitstun and SDI during the protected contact. Grabs still capture and cancel it;
cancellation or a new activation cannot carry protection forward. Air A/R
retain their existing temporary gravity windows. E/F reward values are unchanged;
F's individual contacts are not a guaranteed full-damage sequence.

These values establish roles, not final roster balance. Source-backed mirror
scenarios verify the R link at 0/60/120/180% with escape inputs; human
Lab/Training acceptance and accepted-package verification are separate gates.

Native Ability Lab idle-target probes of the local Bonk package: ground Q
connected once for 6 damage at 1.5 m (frame 20); ground R connected twice
for 4 then 8 damage at 2.5 m (frames 22/48); ground F connected once for
2.5 damage at 1.5 m (frame 9). Air Q at 1.5 m missed the grounded dummy from
the Lab's elevated start; air R at 2.5 m connected its second 8-damage hit
at frame 41; air F at 1.5 m connected once for 2.5 at frame 66. These are
fixed-distance, idle-opponent contacts, not evidence of armor under incoming
attacks, a guaranteed R link, or final visual/gamefeel acceptance. The Lab
reported a live draft with a stale workspace, not an accepted cooked package.

## Package ownership

The package is `client/Unity/Assets/CharacterPackages/bonk/`:

- `package.json` — package identity and attribution (`bonk`, `0.0.0-dev`, Binoui, MIT,
  SLOPArena).
  `character.json` — authoritative gameplay source with the canonical sixteen slots.
  Eight normals use sword capsules from `_weapon_hilt` to `_weapon_tip`; A is Shoulder
  Bash, E is the targeted recovery slam, R is Charging Double Slash, and F is Blade Storm.
  `air.A` and `air.R` are explicit aerial variants with their own temporary gravity
  windows; `air.E` and `air.F` choose their own airborne presentation cues.
- `CharacterAssetCatalog.asset` — schema 1, 60 Hz, Bonk rig, presentation bindings,
  independent move-animation bindings, and the package-owned `BonkWeaponAttachConfig.asset`.

Bonk is admitted by `content-cooked/roster/manifest.json` as selector `Bonk`,
package `bonk`, version `0.0.0-dev`, with the exact cooked-content and package
hashes from its cooked manifest. Character Select discovers it from that roster.

## WIP gameplay kit

The table describes the local source candidate, in authored 60-Hz ticks.
Each normal uses a `hitGroup: 0` capsule from `_weapon_hilt` to `_weapon_tip`;
the existing air.2 probe is noninterruptible, while the other normals are interruptible.

| Slot | Role | Duration | IASA | Trigger / active | Radius | Damage / angle | Base / growth | Stun |
| --- | --- | ---: | ---: | --- | ---: | --- | --- | ---: |
| `ground.1` | Squire's Cut — fast one-handed slash | 60 | 38 | 11 / 13 | 0.5 | 6 / 30° | 4 / 20 | 12 |
| `ground.2` | Barn Door — flat two-handed sweep | 93 | 47 | 20 / 15 | 0.5 | 10 / 35° | 7 / 30 | 16 |
| `ground.3` | Reverse Rising Slash | 125 | 44 | 21 / 20 | 0.5 | 10 / 35° | 7 / 30 | 16 |
| `ground.4` | Heavy Double Slice — kill read | 118 | 71 | 48 / 16 | 0.5 | 15 / 25° | 10 / 42 | 22 |
| `air.1` | Reverse Air Slash — spacing | 88 | 52 | 19 / 24 | 0.5 | 8 / 35° | 5 / 24 | 14 |
| `air.2` | Existing air.2 probe, recovery retimed | 53 | 45 | 15 / 14 | 0.5 | 1 / 45° | 5 / 80 | 8 |
| `air.3` | Downward Sweep — spike | 100 | 60 | 19 / 16 | 0.5 | 11 / -45° | 7 / 30 | 20 |
| `air.4` | Acrobatic Double Slice — kill read | 73 | 63 | 37 / 4 | 0.5 | 14 / 25° | 9 / 40 | 22 |

Grounded landing/auto-cancel fields remain zero. Aerial landing lag / early
auto-cancel / late auto-cancel: `air.1: 20 / 19 / 43`, `air.2: 18 / 0 / 45`,
`air.3: 22 / 20 / 50`, `air.4: 24 / 20 / 62`. Zero early auto-cancel is disabled.

On 2026-10-17 two windows were lined up with the drawn strike, after sword hitboxes started
sweeping between ticks ([clip clock and sweep](../systems/hitbox-system.md#clip-clock-and-swept-bone-hitboxes--since-2026-10-17)).
`air.1` Reverse Air Slash runs t19–t42 instead of t19–t39: the greatsword kept full speed for
three ticks after the window. `air.4` Acrobatic Double Slice runs t37–t40 instead of t37–t48:
the slice is over at t39, and its end pose no longer hits for nine more ticks. These aerial
animations are due to be redone; their windows will need another pass then.

`ground.E` and `air.E` use the same reusable targeted-leap lifecycle:
`groundCursor` hold/release aim, a 240-tick cooldown, and a 112-tick authored
timeline. The public Shared primitive clamps the horizontal target to 1–12 m,
launches at vertical speed 16, and dispatches its authored sword hitbox only
on landing. The source owns the 96-tick flight limit, pose seek to tick 56,
52-tick recovery, and the nested six-tick hilt-to-tip hitbox; none of these
values live in Bonk-specific C#. Aim yaw and distance are cached while held,
so release input cannot replace the selected direction. Ability Lab's Moves
inspector edits these parameters through the package draft and Undo.

`ground.F` is `Blade Storm`: a 100-tick timeline with a 900-tick cooldown and one
hit window per turn of the spin. Ticks 8–28 and 29–42 deal 2.5 damage and hold the
target (a pull toward him and 24 ticks of fixed hitstun); the 12-damage finisher on
ticks 43–58 launches at 35°. A mobile aim state from tick 0 to 65 lets him walk at 35%
of his speed (`aimMovementSpeedScale` 0.35); the walking stops with it, so he does not
slide in the recovery. `air.F` has the same gameplay with airborne wind and no
ground-dust emitter.

`air.A` and `air.R` preserve their grounded timelines and add a `gravityWindow`
at tick 0: 0.5× the active airborne gravity for 30 ticks. FastFall overrides
this temporary reduction; the operation does not reset FloatWindow.

The capability requirement is `slop.ability.targeted-leap.v1` version `1`.
This bounded primitive is admitted for validated Workshop and trusted
built-in packages; the retired Bonk-only ID fails admission.

Assets remain in the existing shared Bonk art tree, matching the FightGuy/Wibou convention,
with their Unity `.meta` files preserved:

- `Assets/Art/Characters/bonk/Models/bonk.fbx`.
- `Assets/Art/Characters/bonk/Animations/idle.FBX`, `run.FBX`, `jump_start.FBX`,
  `jump_loop.FBX`, and `jump_end.FBX`.
- `Assets/Art/Characters/bonk/Animations/bonk_g_1.FBX` through `bonk_g_4.FBX`.
- `Assets/Art/Characters/bonk/Animations/bonk_a_1.FBX`, `bonk_a_3.FBX`, and `bonk_a_4.FBX`.
- `Assets/Art/Characters/bonk/Animations/bonk_spell_e.FBX` and `bonk_spell_f.FBX`.

The equipped Bonk sword is `Assets/Art/Characters/bonk/Prefabs/bonk_sword.prefab`,
sourced from `Assets/Art/Characters/bonk/Models/bonk_sword.fbx` and referenced by
`BonkWeaponAttachConfig.asset`. The package now declares `_weapon_hilt` and
`_weapon_tip` attachment points and its probe slash uses a baked hilt-to-tip capsule.
The package weapon config attaches the sword to the source rig's `hand_r` bone.
Source licensing remains an approval prerequisite before release, not before the
current built-in roster admission.

## Historical air.2 design — Rising Greatsword Sweep

The following September 15 animation/role proposal is historical, not the
current gameplay table. The October local pass retimes the existing damaging
air.2 probe and adopts the proposed recovery phase and landing commitment;
it does not implement the proposal's different damage, angle, radius or active window.

Animation candidate: `Assets/Art/Characters/bonk/Animations/bonk_a2_rising_sweep.FBX`,
bound as `anim.bonk.a2`. Source frames 1–22 at 30 fps span 0.7 seconds.
Entry and handoff use the first pose of `jump_loop.FBX`, sampled on Bonk's
actual rig. The normal Blender FBX export imports as Humanoid with a
reference pose derived from Bonk's existing Avatar; no binary FBX patching
or gameplay-root correction is used.

Earlier Ability Lab review used a 30-tick probe, compressing the clip to
0.5 seconds. The current source candidate uses 53 ticks; the historical
42-tick proposal below is not its current timing contract. Live review of
the new timing is pending.

Editable source and review evidence:
`art/blender/exports/bonk-a2-37jjjrkv/bonk-a2-source.blend`,
`bonk-a2-preview.mp4`, `motion-sheet.jpg`, and `ability-lab.png`.
These local authoring artifacts include recovery copies of the prior Blender
session, original rig/import settings, and catalog. Unity checks sampled
43 times: zero root translation/rotation/scale drift and start/end bone
positions matching within 0.000001 m. Human motion/feel approval is pending.

### Role and counterplay

An aerial anti-air / juggling tool: a two-handed rising greatsword sweep
through the space in front of and above Bonk. It complements `air.1` forward
spacing, `air.3` downward spike, and `air.4` aerial kill read.

The sword tip draws one upward arc, not a full helicopter spin. Below and
behind Bonk remain vulnerable. Whiffing commits him through recovery; the
move is not an all-direction defensive bubble or a promised true-combo starter.

### Animation and timing target

All intervals below use zero-based 60 Hz gameplay ticks. Endpoints shown
as ranges are inclusive; tick 42 is the duration boundary.

| Phase | Ticks | Required pose / motion |
| --- | --- | --- |
| Startup | 0–11 | Gather the blade low and slightly across the body, with a compact, readable two-handed windup. No damaging contact. |
| Active | 12–19 | Sweep upward from in front of the chest to overhead in one continuous strike. |
| Recovery | 20–35 | Finish with the sword high, then visibly recover toward the airborne pose. No damaging contact. |
| Actionable / IASA | 36 | Bonk may act again. |
| Visual settle | 36–41 | Complete the return toward the airborne pose if uninterrupted. |
| Duration boundary | 42 | End of the 0.7-second timeline. |

Author the contact poses first: at tick 12 the blade enters useful
front-upper space; by tick 19 it has swept overhead.

First contact is at 200 ms. The active window lasts 8 ticks (about 133 ms).
At 30 fps, zero-based animation frame positions are: first contact 6,
active-window end boundary 10, IASA 18, duration boundary 21.
At 60 fps, frame positions match gameplay ticks. Preserve the 0.7-second
duration on export rather than confusing inclusive sample count with duration.

### Hitbox contract

- One moving capsule from `_weapon_hilt` to `_weapon_tip`, following the
  authoritative baked sword pose throughout ticks 12–19.
- Initial radius: 0.35 m, subject to visible-blade alignment in Ability Lab.
- One hit per opponent for the entire sweep. The moving capsule must not
  repeatedly damage an opponent as it passes through them.
- No stationary overhead sphere, extra torso hitbox, or added rear/below
  coverage. Close opponents are hit only where the sword capsule reaches.
- If implementation later splits the contact into multiple hitboxes, preserve
  the same one-hit-per-opponent contract through shared hit grouping.

### Provisional reward and landing cost

| Parameter | Initial target |
| --- | --- |
| Damage | 8 |
| Launch angle | 75° upward |
| Landing lag | 18 ticks |
| Early autocancel | Disabled |
| Late autocancel | Starts at tick 36 |

Reward intent is useful vertical lift, not strong sideways knockback.
Base knockback, growth, and actual hitstun remain undecided until the
authored move can be measured through Shared simulation. Damage, angle,
radius, and landing cost are starting values, not validated balance.
Do not claim a guaranteed follow-up or true combo without real-simulation evidence.

### Acceptance before gameplay approval

- Scrub ticks 12–19 in Ability Lab: the capsule follows the blade and the
  front-upper-to-overhead arc reads clearly at gameplay distance.
- Verify no damaging contact before tick 12 or from tick 20 onward, and
  exactly one hit per opponent across the sweep.
- Check representative front-upper and overhead targets, plus rear/below
  targets outside the blade path; there must be no invisible body coverage.
- Exercise whiff recovery, tick-36 actionability, landing lag, and the
  late-autocancel boundary through the authoritative runtime.
- Measure launch and follow-up opportunities at representative damage
  percentages; then playtest whether coverage, commitment, and reward
  distinguish it from Bonk's other aerials.

## Rig and bindings

`bonk.FBX.meta` reports `animationType: 3` (Humanoid). The catalog rig resolves to the
Bonk FBX root GameObject. The imported clips resolve through their Humanoid animation
subassets. The current headless checks produced no Unity console warnings. Clip importer
metadata still reports copied-avatar bone-length mismatch warnings; Avatar visual/pose
review remains an unverified prerequisite before gameplay use.

Intentionally bound semantic clips:

| Semantic ID | Asset | Pose track |
| --- | --- | --- |
| `anim.idle` | `Animations/idle.FBX` | `anim.idle` |
| `anim.run` | `Animations/run.FBX` | `anim.run` |
| `anim.jump` | `Animations/jump_start.FBX` | `anim.jump` |
| `anim.fall` | `Animations/jump_loop.FBX` | `anim.fall` |
| `anim.dash` | shared `Assets/Art/Characters/shared/Animations/dash.anim` | `anim.dash` |
| `anim.hit-light` | shared `Assets/Art/Characters/shared/Animations/hit_light.anim` | `anim.hit-light` |
| `anim.hit-medium` | shared `Assets/Art/Characters/shared/Animations/hit_medium.anim` | `anim.hit-medium` |
| `anim.hit-hard` | shared `Assets/Art/Characters/shared/Animations/hit_hard.anim` | `anim.hit-hard` |
| `anim.bonk.g1` | `CharacterPackages/bonk/Animations/bonk_sir_g1_squires_cut.anim` (GenRA, 2026-10-21) | matching semantic ID |
| `anim.bonk.g2` | `CharacterPackages/bonk/Animations/bonk_sir_g2_barn_door.anim` (GenRA, 2026-10-22) | matching semantic ID |
| `anim.bonk.g3`, `anim.bonk.g4` | `Animations/bonk_g_3.anim`, `bonk_g_4_n.anim` | matching semantic ID |
| `anim.bonk.a1`, `anim.bonk.a3`, `anim.bonk.a4` | matching Bonk air FBX | matching semantic ID |
| remaining `anim.bonk.*` move IDs | `Animations/idle.FBX` fallback | matching semantic ID |

Each move slot owns its semantic ID. Replacing `anim.bonk.a1` no longer changes
the other move rows. The fallback bindings keep the probe cookable until the
remaining authored clips are available.

## Inspection and cook

```bash
unity pipeline list --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target bonk --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target bonk --format json
```

The accepted 2026-10-03 combined Bonk cook and verification resolved all
sixteen slots with no diagnostics and `dirtyOrStale: false`. Source/cooked-source
hash: `596dc90049ad069f32f7be781b3b22988f30f40177c1b8ebbde2ea9cb9c46637`;
cooked-content hash: `b7e7f6e3823a97860228789677eed6e457592f70953503388d48c596e9e3126c`;
package hash: `835a7b93dc703e3c91e2d62d7cd56dab9740c04474086f3c9fee81fd8240e40d`.
Runtime API minimum is `1.3.0`, and the verified requirement is the public
`slop.ability.targeted-leap.v1` version `1`. The built-in Bonk roster pin was
refreshed to this identity. An earlier forced invalid cook preserved the last
valid cooked payloads and catalog; that check predates this cutover.

Ability Lab and Character Select may discover Bonk through the verified package and
roster manifests. Compatibility remains the legacy-only path; Training and online
deployment remain release-gated.

## Findings

| Problem | Evidence | Impact | Fix or prerequisite |
| --- | --- | --- | --- |
| Editor status was hard-coded to FightGuy | `CharacterAssetCatalogEditor.OnEnable` and stale help text | Bonk could show the wrong status and diagnostics context | Status now reads only after catalog selection and names the selected package |
| Cook profile policy was duplicated | Service and catalog editor profile expressions | New packages could diverge in trust policy | Shared internal profile helper; `fightguy`, `wibou`, and `bonk` use the trusted built-in profile |
| Dependency tracking only inspected FightGuy | `CharacterCookAssetPostprocessor` | Bonk changes were not queued or isolated | Postprocessor now discovers every catalog and matches its persisted dependencies |
| Shared dash/hit clips were initially unresolved | Four `asset-catalog.clip.missing` diagnostics | Cook could not reach pose validation | Bound the existing shared clips used by FightGuy/Wibou |
| Temporary clip imports report bone-length mismatch warnings | `Animations/*.FBX.meta` `rigImportWarnings` | Pose quality is not yet approved | Re-author/import clips against the validated Bonk Avatar |
| No package creation control exists in Ability Lab | `AbilityLabPackageWorkspace.NewPackage` is the existing creation seam | Onboarding needs an editor-side API call | Add a UI control only when package onboarding is explicitly scoped |

Shared tests cover the package compiler, capability admission, authoritative E
hold/release/landing, timeout cancellation, and F's five independent contacts.
Bonk is admitted to the built-in roster and Character Select. Do not treat this
as approval for Training or online deployment until avatar visual/pose review
and the remaining release checks pass.
