# Nyx

> **Identity (owner, 2026-10-28):** a goth girl with two forms, like Zelda and Sheik in Melee. The real goth is a
> shadow spellcaster and witch. The fake goth is an influencer who fights as a brawler with crowd control ("cause
> influencer... get it?"). The fake form parodies the "goth girlfriend" content e-girl: the reference must read
> clearly "without overboard into ridiculous sexualization". Name picked on 2026-10-29: Nyx, the Greek night
> goddess; her fake form calls herself Nyxxie.

## Status

Both forms have authored normals, hit-confirmed F cancels, and their recoveries. Nyxxie's A is Ring Light, a short lunge into a zero-launch dazzle that only catches fighters facing her. Her other three specials use new engine pieces: Grasp (real R) places a catch on her target's spot and drags a caught target to her feet, Hex Crow (real A) is a slow homing crow that perches and pops on her next fake-form hit, and Selfie Grab (fake R) is a lunging command grab with a held selfie and a launch. The owner approved all 24 GenRA candidates and the effect concepts on 2026-10-29. Her own clips and cut-paper effects are now bound in the package. The timing slowdown below lengthens both forms while preserving hitbox shapes and hit reactions. The Stomp-Kick contact mismatch still needs a decision.

| Piece | State | Where |
| --- | --- | --- |
| Look, both forms | Approved 2026-10-28 ("approve both") | [Real sheet](../design/references/nyx/nyx-real-sheet.jpg), [fake sheet](../design/references/nyx/nyx-fake-sheet.jpg) |
| 3D models | Received 2026-10-29: one dressed A-pose mesh per form, no rig | [Real](../design/references/nyx/nyx-model-real.jpg), [fake](../design/references/nyx/nyx-model-fake.jpg); files in ignored `tmp/nyx-model/tripo-v1/` |
| Kit | Accepted 2026-10-29 (below) | This page |
| Move concepts | Shown 2026-10-29, verdict pending | [Board](../design/references/nyx/nyx-moves.jpg) |
| Animation pose sheets | 24 made 2026-10-29; all resulting motion candidates approved the same day | Ignored `tmp/nyx-model/anim-sheets/board/` |
| Rig | Built 2026-10-29: one shared 116-bone skeleton and twelve dress spring chains. The body delegate's spine fit is installed; extreme hem/boot clearance still needs review | [nyx_rig](../../tools/blender/nyx_rig/README.md); build in ignored `tmp/nyx-model/rig-build-v1/` |
| Two-form engine | Phases 0 and 1 built 2026-10-29, uncommitted: two grids, swap on F (Going Live pulls, Logging Off pushes, 300-tick shared clock), per-form E clocks, start form picked on Fighter Select. Phases 4 to 6 built 2026-10-29, uncommitted: the tether's placed catch, homing and perching projectiles with the pop, and `slop.ability.command-grab.v1` | [ADR-0051](../adr/0051-nyx-two-forms.md) |
| Package | `nyx`, `CharacterClass` 8, admitted 2026-10-29. The 24 approved clips replace attack, idle and run placeholders | `client/Unity/Assets/CharacterPackages/nyx/` |
| Normals and existing-capability specials | Phase 2 and 3 implementation, 2026-10-29. Bat Scatter uses immediate thrust with no invincibility; Ring Light Rise steers during ascent; Ring Light now filters fighters by facing | [ADR-0051](../adr/0051-nyx-two-forms.md) |
| Grasp, Hex Crow, Selfie Grab | Phase 4 to 6 implementation, 2026-10-29, uncommitted. Combos 1 to 3 connect in the Shared sim and in Training. Grasp now shows a warning, shadow hand and drag; Hex Crow uses its own flying/perched paper bird. Phase 7 adds the facing filter, form-aware CPU, HUD decks and Lab form selector | [ADR-0051](../adr/0051-nyx-two-forms.md) |
| Phase 7 | Built 2026-10-29, uncommitted: facing-filtered Ring Light, cached CPU profiles with range swaps and Normal/Hard hit cancels, live HUD decks and portraits, and both editable Lab grids. HUD icons are borrowed placeholders; final renders remain separate work | [ADR-0051](../adr/0051-nyx-two-forms.md) |
| Portraits | Installed 2026-10-29, one per form; other takes in ignored `tmp/nyx-model/portraits/` | `Resources/UI/Portraits/Nyx.png`, `Nyx_Fake.png` |
| Her own animations | All 24 approved 2026-10-29, including the four check exceptions. Promoted, bound and cooked with separate pose-track IDs | `Animations/NyxApprovedClips.json`; local evidence in `tmp/nyx-model/integration/` |

## Look

Nyx is drawn like the roster's other humans, FightGuy and Wibou: adult anatomy, small realistic eyes, faceted
low-poly faces. Rounds of big-eyed, doll-like concepts were rejected as "not SLOPArena". She is one woman, so both
forms keep the same face, height, build and long straight black hair with blunt bangs.

- **Real form:** a stark white face, black lips, waist-length straight hair, a long black dress with puffed
  shoulders and no slit, a square-buckled belt, a spiked choker, silver chains, a big ankh, a bat brooch, stacked
  studded bangles, rings, and buckled platform boots. She stands straight and stiff. The owner asked for straight
  posture, straight hair, a white face, more jewelry and fewer dangly bits.
- **Fake form:** normal skin, a cat-ear headband with pink LED ears, huge false lashes, overlined dark lips, a pink
  heart under the left eye, a spiked O-ring choker, a fishnet top over a black tank top, studded belts with a heart
  charm, a black-and-pink pleated skirt over shorts, fishnet tights, and pink-buckled platform boots. A backpack
  carries a pole with a ring light above her left shoulder. She holds one phone; the owner dropped the second one.

## Kit

Each form has to win on its own, and switching should be the fun part. The real form barely moves while her
shadows hit from range. The fake form never stops moving and hits hard up close. The swap moves enemies to the
range the next form wants.

### Movement

The forms split like Zelda and Sheik (owner, 2026-10-29). The real form is slow on the ground and floaty in the
air. The fake form runs fast and falls fast.

### Real form: the witch

Her body barely moves, so her animations are small gestures and her shadow effects carry the hits.

| Slot | Move | What happens |
| --- | --- | --- |
| Ground 1 | Shadow Flick | She flicks two fingers and a shadow claw swipes in front of her. |
| Ground 3 | Grasping Hands | Two shadow hands rise around her and close above her head. Anti-air. |
| Ground 4 | Shadow Lash | A long shadow tendril lashes along the floor. Slow, long reach. |
| Air 1 | Shadow Ring | A ring of shadow spins around her. |
| Air 3 | Up Claw | A shadow claw rakes upward above her. |
| Air 4 | Shadow Fist | A shadow fist slams down below her. Spike. |
| A | Hex Crow | A slow crow flies at the current target. The target can outrun it, shield it or hit it away. If it arrives, it perches on the target's head. |
| E | Bat Scatter | She bursts into bats, flies in the stick's direction and re-forms. Her recovery. |
| R | Grasp | After a warning, a shadow hand rises under the current target and drags them to her feet. |
| F | Going Live | The swap to the fake form. A ring-light flash pulls nearby enemies in. |

### Fake form: the influencer

All her moves use props she already carries: the phone, the ring light and the platform boots. No move summons a
crowd: the owner ruled crowds out as too messy in play.

| Slot | Move | What happens |
| --- | --- | --- |
| Ground 1 | Phone Slaps | A three-hit slap string with the phone. |
| Ground 3 | Ring Light Uppercut | She pulls the pole off her backpack and swings it up in an arc. Launcher. |
| Ground 4 | Platform Stomp-Kick | A front push-kick with the platform boot. |
| Air 1 | Spinning Slap | She spins with the phone hand out. |
| Air 3 | Ring Light Overhead | The pole swings in an arc above her. |
| Air 4 | Double Dropkick | Both boots drive down. Spike. |
| A | Ring Light | She snaps toward the target and flashes a short cone that dazzles everyone facing her. |
| E | Ring Light Rise | The ring light spins like a helicopter rotor and lifts her. Her recovery. |
| R | Selfie Grab | A short lunge into a command grab. She poses, and the selfie flash blasts the victim away. |
| F | Logging Off | The swap to the real form. A shadow-shard burst pushes nearby enemies away. |

### The swap and the crow

- **Both swaps hit.** Going Live pulls enemies in for the fake form's close range. Logging Off pushes them out to
  the real form's range. The owner set these directions on 2026-10-28.
- **Swap-cancel.** When a move connects, F during its recovery swaps at once. This is what makes her a combo
  character rather than two characters.
- **The crow carries across.** A perched crow stays through the swap. The fake form's next hit on that target pops
  it for a bonus hit. The owner called it "a fine addition, not too much".

Three combos the kit is built for:

1. Grasp drags the target in, Going Live pulls them tight, then Phone Slaps into Selfie Grab.
2. Ring Light Uppercut launches, Logging Off pushes them further away, then Hex Crow chases them as they fall.
3. Hex Crow perches, she swaps, and any fake-form hit pops the crow.

The kit follows the owner's play verdicts: specials pick the current target and can be dodged by moving
([character kit principles](character-kit-design-principles.md)).

## Effects

Effects use the game's flat comic style: a near-black ink outline with dark, accent and pale bands, never smoke,
bloom or particle haze. The real form's shadows are deep violet with a pale lavender core, cut like torn black paper.
The fake form's effects are hot pink and white, with small flat hearts and four-point sparkles. The
[move concepts](../design/references/nyx/nyx-moves.jpg) show one key frame per special, both swaps and the crow pop.

Two effects hide work that would cost too much to animate:

- **The outfit swap** happens in a single frame, inside the Going Live flash or the Logging Off shard burst. The
  concept's half-and-half split body is not built.
- **Bat Scatter** hides her body inside a burst of bats, moves the swarm, and shows her again inside a second burst.
  She is never assembled bat by bat.

Grasp's drag can reuse the tether and `Reeled` state from [ADR-0048](../adr/0048-bashie-rescue-ring-tether.md).

## Model and rig

Both forms share one skeleton. Each form is a skinned mesh on that skeleton, and the swap only changes which mesh
draws. Because she is one woman, the same clips and cooked poses fit both bodies.

1. Tripo returned each form as one dressed mesh, about 26,000 vertices, with a 4K texture and no rig.
2. The fake form's backpack, pole and ring light come off the body. They hang as one rigid prop on a spine bone,
   the way Bashie's shell hangs on her mid-back bone. The ring-light swings also need a hand-held copy of the pole
   that shows while the backpack's copy hides.
3. The owner rigs each body in Mixamo. The build then puts both meshes on one skeleton.
4. Known risks: the long hair next to the arms, the long dress over the legs, and many small jewelry pieces.

## Animation

Each move gets a pose sheet on the actual model before any motion is authored, following the
[GenRA workflow](genra-animation-workflow.md). The 24 sheets cover six normals, three specials, the swap, an idle
and a run per form. Idles stay calm, with one slow gesture. The real form's run is the joke "creepy glide": fast
legs under the dress and a torso that never moves. The fake form runs while filming herself.

## Animation integration, 2026-10-29

`NyxAnimationIntegration` binds all 24 approved candidates from package-owned assets. Every
clip has its own semantic animation and pose-track ID. Normals, A and the original swap
takes were authored at twice their game length. Each attack clip now plays over the longer stage clock below. The accepted poses and
attack clips are preserved. Run cycles stretch from 16 to 24 ticks for Nyx and from 20
to 26 ticks for Nyxxie, with unchanged movement speeds of 10 and 15 m/s.

Bat Scatter's original source remap is retained. Its stage now lasts 63 ticks and
its thrust lasts 30 ticks. Going Live lasts 38 ticks with its swap and pull at 11;
Logging Off lasts 41 ticks with its swap and push at 13. Both keep the shared
300-tick cooldown. The original accepted takes remain under `Animations/Source`.

### Timing slowdown, 2026-10-29

`tools/nyx/retime.py` applies 1.25 to every move clock, except both ground 1 moves
which use 1.35. Its journal records the original leaves and applied factors so reruns
cannot compound. `tools/genra/retime_anim.py` stretches run curves and their tangents
without changing poses or loop settings. Damage, launch, hit reactions, cooldowns,
landing lag, crow rules, and movement stats keep their prior values.

The table uses 60 Hz ticks and inclusive active windows. Hex Crow's active entry is
its launch tick. Grasp and Selfie Grab list capture windows. An IASA of 0 leaves
completion to the timeline or capability. Air A, E, R, and F alias their ground moves.

| Form | Slot | First contact | Active windows | IASA | Total |
| --- | --- | --- | --- | --- | --- |
| real | ground.1 | 11 | 11 to 18 | 32 | 41 |
| real | ground.3 | 18 | 18 to 31 | 45 | 55 |
| real | ground.4 | 30 | 30 to 39 | 70 | 80 |
| real | ground.A | 20 | 20 | 40 | 50 |
| real | ground.E | none | none | 0 | 63 |
| real | ground.R | 30 | 30 to 35 | 0 | 105 |
| real | ground.F | 11 | 11 to 14 | 23 | 38 |
| real | air.1 | 10 | 10 to 23 | 43 | 50 |
| real | air.3 | 13 | 13 to 22 | 45 | 53 |
| real | air.4 | 25 | 25 to 34 | 63 | 70 |
| fake | ground.1 | 11 | 11 to 15, 22 to 26, 35 to 41 | 51 | 59 |
| fake | ground.3 | 20 | 20 to 30 | 50 | 60 |
| fake | ground.4 | 20 | 20 to 28 | 58 | 65 |
| fake | ground.A | 13 | 13 to 18 | 38 | 45 |
| fake | ground.E | none | none | 0 | 63 |
| fake | ground.R | 13 | 13 to 18 | 85 | 95 |
| fake | ground.F | 13 | 13 to 16 | 25 | 41 |
| fake | air.1 | 9 | 9 to 21 | 39 | 45 |
| fake | air.3 | 15 | 15 to 28 | 48 | 55 |
| fake | air.4 | 15 | 15 to 30 | 53 | 60 |

Nyxxie's Phone Slaps contact ticks are 11, 22, and 35. Both linking hits retain
14 fixed Hitstun ticks. Source and saved-package sim checks verify that the victim
stays locked between all three contacts and that the three documented combos connect.
The full before/after table and verification receipts are local in
`tmp/nyx-model/timing-slowdown/REPORT.md`.

## Combat presentation, 2026-10-29

The approved concepts use ink, dark, accent and pale bands. Real effects use violet
`#5B2A86` and lavender `#D9C8F0`; fake effects use hot pink `#FF4FA3` and white. The package
paper shader has no emission, bloom, smoke or camera-shake behavior. Mesh cues follow the
restored attack clock, so their active silhouettes stop during hitstop and on interruption.

`tools/nyx/build_paper_assets.py` makes the source meshes and palette offline.
`sloparena.presentation.nyx.build` assembles separate prefabs with the roster's
`PresentationBuilderKit`. It preserves the existing phone and pole bindings and never
writes `NyxRig.prefab`.

The direction-readability pass replaces Ring Light's filled pyramid with a small hoop and
six separated pink/white rays. Hands turn toward the gameplay camera around their own
up axes; the aerial claw/fist have open centres, and Grasp's catch and carried hand are
smaller and offset beside the victim. Bat,
launch-feather and swap-scrap faces use the same yaw-facing presentation with varied rolls.
Going Live uses one thin hoop about 1.5 fighter heights across, a small flash and hearts.
Contact stars have a shorter peak and smaller fragments. These changes preserve the
attack clock, gameplay data, rig and flat comic palette. Eight-view before/after evidence
and remaining limits are recorded locally in `tmp/nyx-model/vfx-direction-fix/REPORT.md`.

All IDs below have the prefix `presentation.nyx.`.

| Form | Move | Effect ID |
| --- | --- | --- |
| Real | Shadow Flick | `shadow-claws` |
| Real | Grasping Hands | `rising-hands` |
| Real | Shadow Lash | `floor-lash` |
| Real | Shadow Ring | `shadow-ring` |
| Real | Up Claw | `up-claw` |
| Real | Shadow Fist | `spike-fist` |
| Real | Hex Crow | `crow-launch`, `crow`, `crow-pop` |
| Real | Bat Scatter | `bat-scatter` |
| Real | Grasp | `grasp-warning`, `grasp-catch`, `grasp-drag` |
| Real | Going Live | `going-live` |
| Fake | Phone Slaps | `phone-slaps` |
| Fake | Ring Light Uppercut | `pole-uppercut` |
| Fake | Platform Stomp-Kick | `stomp-impact` |
| Fake | Spinning Slap | `spin-arc` |
| Fake | Ring Light Overhead | `pole-overhead` |
| Fake | Double Dropkick | `dropkick-spike` |
| Fake | Ring Light | `ring-light` |
| Fake | Ring Light Rise | `rotor-arcs` |
| Fake | Selfie Grab | `selfie-pop`, `selfie-release` |
| Fake | Logging Off | `logging-off` |
| Both | Confirmed contacts | `shadow-hit`, `pink-hit` |

The crow's projectile profile binds both A variants. Resolver attachment places it on the
victim's head; its wings fold on a perch. Only the authored pop makes feathers and shadow.
Grasp warning/catch cues use its existing capability events. The placed-tether presenter
carries a bound shadow-hand prefab during the drag and suppresses the borrowed ring/rope.

## Open items

- In-game visual and feel acceptance of the integrated animations and effects remains with the owner.
- Stomp-Kick's approved right boot sits above its unchanged low capsule at contact. The inspected ankle is approximately 0.17 m outside it. Proposed correction, not applied: raise both capsule Y offsets by 0.30 m, from -0.45/-0.15 to -0.15/+0.15, then verify standing, crouched and airborne contacts. The original take and hitbox are unchanged.
- Final HUD glyphs, sounds and the owner's Fighter's License decision remain separate work.
