# Nyx

> **Identity (owner, 2026-10-28):** a goth girl with two forms, like Zelda and Sheik in Melee. The real goth is a
> shadow spellcaster and witch. The fake goth is an influencer who fights as a brawler with crowd control ("cause
> influencer... get it?"). The fake form parodies the "goth girlfriend" content e-girl: the reference must read
> clearly "without overboard into ridiculous sexualization". Name picked on 2026-10-29: Nyx, the Greek night
> goddess; her fake form calls herself Nyxxie.

## Status

Playable in Training since 2026-10-29 as the real form only, with FightGuy's moves standing in for her own. The two-form engine and her own animations are being built.

| Piece | State | Where |
| --- | --- | --- |
| Look, both forms | Approved 2026-10-28 ("approve both") | [Real sheet](../design/references/nyx/nyx-real-sheet.jpg), [fake sheet](../design/references/nyx/nyx-fake-sheet.jpg) |
| 3D models | Received 2026-10-29: one dressed A-pose mesh per form, no rig | [Real](../design/references/nyx/nyx-model-real.jpg), [fake](../design/references/nyx/nyx-model-fake.jpg); files in ignored `tmp/nyx-model/tripo-v1/` |
| Kit | Accepted 2026-10-29 (below) | This page |
| Move concepts | Shown 2026-10-29, verdict pending | [Board](../design/references/nyx/nyx-moves.jpg) |
| Animation pose sheets | 24 made 2026-10-29, owner review pending | Ignored `tmp/nyx-model/anim-sheets/board/` |
| Rig | Built 2026-10-29: one skeleton, both meshes, the real dress as Unity Cloth; dress shape in big poses awaits the owner | [nyx_rig](../../tools/blender/nyx_rig/README.md); build in ignored `tmp/nyx-model/rig-build-v1/` |
| Two-form engine | Phases 0 and 1 built 2026-10-29, uncommitted: two grids, swap on F (Going Live pulls, Logging Off pushes, 300-tick shared clock), per-form E clocks, start form picked on Fighter Select | [ADR-0051](../adr/0051-nyx-two-forms.md) |
| Package | `nyx`, `CharacterClass` 8, admitted 2026-10-29 with FightGuy's kit and clips as placeholders | `client/Unity/Assets/CharacterPackages/nyx/` |
| Portraits | Installed 2026-10-29, one per form; other takes in ignored `tmp/nyx-model/portraits/` | `Resources/UI/Portraits/Nyx.png`, `Nyx_Fake.png` |
| Her own animations | GenRA keys and motion for all 24 sheets in progress; nothing binds before the owner approves | Ignored `tmp/nyx-model/genra/` |

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

## Open items

- The owner's verdict on the move concepts and the pose sheets.
- The shared skeleton for both bodies, her package and the engine phases of
  [ADR-0051](../adr/0051-nyx-two-forms.md).
- The phone, as a separate prop in her right hand.
- Her package, sounds, portrait and HUD move deck for both forms.
