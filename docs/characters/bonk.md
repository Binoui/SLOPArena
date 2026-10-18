# Bonk

> **Identity (owner, 2026-10-17):** a big, simple, good-natured brute, the "big hits"
> character and one of the easier fighters to play. His name is the sound his hits make,
> so a comic **BONK!** is his brand. The gags are on him: he's dense. His headbutt rings his
> own bucket, and when armour shrugs off a hit a little "?" pops over his head. His colour
> is the bucket red of his helmet and pauldron over bandage cream; steel was too plain.

## Status

Bonk is a package-native greatsword kit admitted to the built-in roster.
It exercises the authoritative Shared compiler, trusted built-in capability
admission, cooked package loading, Character Select discovery, and match
content admission. Avatar visual/pose review remains a release prerequisite.

## Blade Storm spin — 2026-10-18

Blade Storm (`ground.F` and `air.F`) now plays `bonk_gf_blade_storm.anim` (100 frames at
1×), authored with the GenRA toolkit (Opus 5.5) on the move's existing clock. The owner
approved it on the actual character. The old clip was an overhead twirl that kept the plank
2.3–3.4 m up, so its four big hits passed over every standing fighter and only the long chip
hitbox landed.

- He drops into a wide, low stance and turns three full circles to his left with the blade
  flat, 1 m off the floor. Spin hits 1 and 3 and the finisher sweep across his right side
  into the front; spin hit 2 crosses his left side toward his back. He stops at tick 66 and
  has the sword back on his shoulder by tick 94. In the air his legs keep the same steps.
- The chip hitbox (ticks 2–75) followed the blade 0.2–0.5 m lower, an offset for the old
  twirl. Its offsets are now zero. Damage, timing and the other hitboxes are unchanged.
- The whirl smears (`BonkBladeStormWind`, `BonkAirStormWind`) orbit 0.2 m above his hips,
  at the blade, instead of 0.9 m.

Bearings (of 24) at which each hit touches a standing target, from
`HitReliabilityReport --char bonk --slots F`, before → after:

| Hit | FightGuy 1 m | FightGuy 1.5 m | Manki 1 m | Manki 1.5 m |
| --- | ---: | ---: | ---: | ---: |
| Chip, ticks 2–75 | 23 → 24 | 13 → 24 | 8 → 24 | 5 → 24 |
| Spin hit, tick 16 | 0 → 11 | 0 → 8 | 0 → 9 | 0 → 8 |
| Spin hit, tick 24 | 0 → 10 | 0 → 7 | 0 → 8 | 0 → 7 |
| Spin hit, tick 32 | 0 → 11 | 0 → 9 | 0 → 9 | 0 → 8 |
| Finisher, tick 44 | 0 → 15 | 0 → 13 | 0 → 15 | 0 → 13 |

In the real simulation, one press against a standing fighter 1–2.5 m away now connects from
all 8 directions tested, for each of the four fighters (before: 0–8 of 8). Straight ahead and
on his right, the finisher lands with one to three 2.5-damage hits (14.5–19.5 damage). Behind
him and on his left, only one or two 2.5-damage hits land (2.5–5): the finisher sweeps his
right side and front only, and each small hit pushes the target away. Air F reaches 0.4 m
below his feet against Manki (0.3 m before), which is still flagged.

Native Ability Lab scenarios on the cooked package, opponent 1.5 m ahead:

| Move | Idle opponent | Shield | 8 m away |
| --- | --- | --- | --- |
| Blade Storm (ground) | spin hit at frame 18, finisher at frame 54 (14.5 damage) | blocked at frames 16, 39 and 58 | miss |
| Blade Storm (air) | finisher at frame 48, chip at 49 (14.5 damage) | blocked at frames 47 and 48 | miss |

Bonk ends every run in Idle. See the [Blade Storm record](../research/2026-10-18-bonk-blade-storm-spin.md).

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
| `ground.1` | Reverse Slash — forward check | 60 | 38 | 11 / 13 | 0.5 | 6 / 30° | 4 / 20 | 12 |
| `ground.2` | Greatsword Swing — forward spacing | 93 | 47 | 20 / 15 | 0.5 | 10 / 35° | 7 / 30 | 16 |
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

`ground.F` is `Blade Storm`: a 100-tick timeline with hitboxes at ticks 2, 16, 24,
32, and 44, and a 900-tick cooldown. Its mobile aim ends at tick 75, locking
movement for the final 25 ticks. `air.F` has the same gameplay with airborne wind
and no ground-dust emitter.

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
| `anim.bonk.g1` … `anim.bonk.g4` | `Animations/bonk_g_1.FBX` … `bonk_g_4.FBX` | matching semantic ID |
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
