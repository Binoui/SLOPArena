# Bashie

> **Identity (owner, 2026-10-21):** a pool lifeguard turtle who can "look cute and be fun". She wants
> everyone to follow the pool rules, blows her whistle at all of them, and breaks every rule herself.
> She is the fifth fighter and the roster's first woman.

## Status

Playable with a stand-in kit. On 2026-10-21 the owner approved the look (concept A below, redesigned the same day),
the four specials and the name Bashie (Shelly was "a bit common"). On 2026-10-22 she got her package and joined the
roster, playing Manki's kit until hers exists. The same day her first special, Hose Down (A), went in (below), with its
own capability, her water blaster and key-pose animation. Her normals, her other specials, her effects and her sounds
are still open.

**Package (owner, 2026-10-22):** "im totally down to steal all of manki's slots especially normals just to have her
ingame. its fine if specials looks weird until we do them properly".
- `client/Unity/Assets/CharacterPackages/bashie/` is a trusted built-in package (`CharacterClass.Bashie`, number 6:
  5 was the retired Nilus).
- Her character data is Manki's: movement, the normals and E, R and F with their capabilities (jetpack, bazooka,
  aerosol). Her own changes are the size (`visualScale` 1.1), `modelYOffset` -0.75, `hipHeight` 0.34, her idle and run,
  and Hose Down in place of his Round Bomb.
- Every other clip is a copy of Manki's under `Animations/Borrowed/`, raised 0.342 m for her body. Played on her as
  they are, his clips stand her a third of a metre under the floor.
- She borrows none of his effects or props: her hits use the shared impact effects, and her specials fire without
  their models, explosions or trails.
- This deliberately breaks the [no-fallback rule](adding-a-new-character.md#2-validate-and-bind-assets) as a labelled
  stand-in; each borrowed slot goes when her own move replaces it.
- Her roster portrait is `Resources/UI/Portraits/Bashie.png`.

The same day the owner generated a first 3D model, rigged it with Mixamo, and generated the water blaster and the
life ring. [tools/blender/bashie_rig](../../tools/blender/bashie_rig/README.md) removes the backdrop card the
generator left behind her and keeps 4 influences. A shell that bent with her back looked weird, and a shell cut off
that model as a solid piece on her mid-back bone still looked wrong.

On 2026-10-22 the owner generated her redesigned body (the look below) and rigged it with Mixamo. They also generated
her shell and her whistle as separate models. The rig build hangs the shell on her mid-back bone and the whistle, on
a red cord, on her chest bone, so none of them bend. She is a local Unity candidate (`BashieRig.prefab`, no package
yet). The files are local.

**Size (owner, 2026-10-21):** "a little bit smaller than Manki". Previews render the redesigned model at scale 1.10
against Manki's `visualScale` (the first model needed 1.15):
- standing on Manki's idle, she is about 1.05 m tall against his 1.16 m
- at rest, the top of her hat is 1.10 m against his 1.16 m

In-game previews run Manki's fighter with her model, so she uses his moves and animations until she has her own.

**Idle and run (owner, 2026-10-22):** picked from generated direction sheets, then authored with the
[GenRA workflow](genra-animation-workflow.md); the owner approved the key poses before the motion:
- Idle, "on duty": a 2 s loop scanning the pool under a visor hand, a fist on her hip and a toe tap. Her arms are too
  short to reach the front of her brim, so the visor salutes at its side.
- Run, "I'm not running!": she enforces NO RUNNING, so she speed-walks: two steps in 26 ticks, straight legs landing on
  the heel, a foot always on the floor, big hips and pumping elbows that stay in front of her shell. The first key
  poses folded her slides and goose-stepped; her slides now bend at most 15 degrees and her steps are 0.23 m.

Both are bound in her package (`Animations/bashie_idle.anim` and `bashie_run.anim`).

## Why a turtle

The roster had no woman.
- **Round 1, rejected:** everyday people (a roller-derby jammer, a luchadora, a landlady and a lunch lady). The
  owner said they "feel like real people". The roster's strongest designs are the least human: Manki is a monkey
  and Sir Bonk's face is a bucket.
- **Round 2, the possum:** came close but was dropped. Babies riding on her back would be costly to animate, and a
  tail hang has no use on our stages, where [ledge hang is off](../../CONTEXT.md).
- **Round 3, the turtle:** the owner proposed one inspired by Squirtle, built around a shell slide. Of three takes,
  the lifeguard won: her whistle gives her a running gag with every other fighter.

Squirtle inspired the moves, not the look: she has no blue skin, no curly tail and no brown shell. She is a
red-eared slider, the common pet turtle, which is named for sliding off logs into the water.

## Look

![Bashie's body sheet: front, side and back, without her shell](../design/references/bashie/bashie-model-sheet.jpg)

**Redesign (owner, 2026-10-21):** her bold stripes were "not really good looking" and her flower swim cap looked "a
bit weird". She now has plain green skin and a lifeguard bucket hat. Sunglasses were tried and dropped: over her
eyes they hide her big eyes, and Wibou already wears sunglasses.

- **Body:** small and round, with a big head, big brown eyes with lashes, stern brows and chubby cheeks. No tail:
  the generator gave her one, and the owner had it removed (2026-10-22).
- **Skin:** plain green with a yellow lower face, plus the slider's red patch behind each eye, which reads as blush.
- **Kit:**
  - a red lifeguard bucket hat with a white band and white stitching around the brim
  - a stripe of white zinc sunscreen across her snout
  - a silver whistle, a separate prop
  - red pool slides ("sliders" for a slider)
- **Shell:** domed and olive with yellow seams, and a yellow belly painted on her front. The bucket hat and the shell
  make her outline.

Concept A, its turnaround and the specials below came before the redesign. They still show the flower cap, the
stripes and the whistle on its cord:

![Bashie, concept A](../design/references/bashie/bashie-concept.jpg)

![Front, three-quarter and back views](../design/references/bashie/bashie-turnaround.jpg)

![Water blaster, life ring and whistle](../design/references/bashie/bashie-props.jpg)

Palette, sampled from the body sheet (the shell from the turnaround):

| Part | Colour |
| --- | --- |
| Hat | `#FC4E4A` |
| Hat band | `#F9E8D9` |
| Skin | `#86A552` |
| Face | `#FCD45E` |
| Belly | `#FCDC72` |
| Shell | `#534926` |
| Zinc | `#FDFDFC` |

For the 3D model:
- **Separate models:** the shell, the water blaster, the life ring and the whistle, like Sir Bonk's plank. The shell
  sits on her mid-back bone and the whistle on her chest bone, so a move can take the whistle to her beak for NO
  RUNNING!. The ring rides on her shoulder and the blaster comes out of her shell.
- **Solid pieces:** the shell and the hat are solid and chunky, which suits a generated mesh.
- **Thin pieces:** the ring's rope is thin: thicken it or paint it on. The rig build makes the whistle's cord.
- **Next:** her own idle and run, then the [new-character workflow](adding-a-new-character.md). The images her model
  came from are local, in `tmp/bashie-model/gen-inputs-v4/`.

## Kit

Each special uses one thing she carries, and each one pops up a pool-rule sign that she is breaking. The whistle is
the one rule she enforces, on everyone else.

| Slot | Special | Shape | The opening |
| --- | --- | --- | --- |
| A | Hose Down | She pulls a toy water blaster out of her shell. Hold to pump it, release to fire a gush that shoves rather than hurts; more pumping makes a bigger gush. In the air the recoil pushes her back. Sign: NO SPLASHING! | Pumping keeps her in place, and the recoil can push her away from the stage. |
| E | Ring Bounce | Her recovery: she slams the life ring under her feet and bounces off it (BOING, squash and stretch). Press again at the top for a cannonball drop with a splash. Sign: NO BOMBS! | The cannonball commits her downward, even off the stage. |
| R | Shell Slide | She tucks into her shell and slides off fast; the stick steers, and the shell shrugs off hits. The first fighter she meets is bowled over, and she pops out with a pose. In the air she slides on thin air, looks down and drops. Sign: NO HORSEPLAY! | A miss (a shield, a wall, or the slide running out) leaves her flipped on her back, paddling, until she rocks herself over. |
| F | NO RUNNING! | She takes a huge breath and puffs up like a balloon, then blows: TWEEEET! Everyone around her can't run or dash and walks slowly for a while, under a NO RUNNING! sign. | The inhale is the tell. If she is hit mid-inhale, she whizzes around deflating. |

### Hose Down (A)

![Hose Down](../design/references/bashie/bashie-a-hose-down.jpg)

- Ledge hang is off, so a fighter hosed away from the stage while recovering is in real trouble. She doesn't knock
  you off the stage; she hoses you off it.
- Idea for later: the water puts out Manki's fire.

**Like FLUDD (owner, 2026-10-22):** "i like C pose. i think like fludd in smash. more you charge, more water comes out
and more push it does. barely any dmg. i think we want aim like manki F". The first build fired one Ki Shot-style
gush; it became a stream.

**In the game (2026-10-22)**, on the new `slop.ability.charged-stream.v1`
([ability architecture](../systems/ability-architecture.md)):
- **Pump:** holding A pumps, in place, for up to 60 ticks. Like Manki's F, the player aims with the camera and she turns
  to face the aim.
- **Spray:** release sprays a stream 4 ticks later, one drop every 3 ticks along the release aim. Each drop flies
  16 m/s for 26 ticks, about 7 m.
- **More pump, more water:** a tap sprays for 12 ticks, a full pump for 45. Each drop's push rises from base 22 to
  40, about 4 to 7 m/s; the move ends 14 ticks after the stream.
- **Push without hurting:** the drops deal 0%, no hitstun and no hitstop, and they push flat (`angle` 0), because a
  push without hitstun ends when the victim lands.
  - Against a dummy 5 m away, a tap slid it about 1.5 m, half a pump about 4 m, and a full pump about 5.4 m. The
    full pump pushed it out of the stream's reach before the last drops.
  - The CPU knows the move's reach but scores it as doing no damage.
- **Recoil:** sprayed in the air, the stream pushes her back at 1.5 to 4.5 m/s by pump; on the ground she stays put.
  A pump in the air, then the spray, moved her 0.75 m before she landed.
- **Blaster:** her water blaster comes out for the pump and the spray, on her right hand with a pistol grip
  ([tools/blender/bashie_rig](../../tools/blender/bashie_rig/README.md)).
- **Animation:**
  - Pose direction C ("big comic pumps", `tmp/bashie-model/anim-directions/hose-c-big-pumps.png`) became two GenRA
    loops: a 24-tick pump and a 12-tick spray judder.
  - Their key poses are bound in game until the motion pass and the owner's check. Her idle stands in for the end
    clip.
- **Open:** the end clip, the water and splash effects (concepts first; the drops are the default white projectile
  for now) and the NO SPLASHING! sign. The cooldown is 90 ticks.

### Ring Bounce (E)

![Ring Bounce](../design/references/bashie/bashie-e-ring-bounce.jpg)

- A cannonball is *une bombe* in French, hence the sign.
- Fallbacks if the bounce doesn't work out: a FLUDD-style hover with the blaster, or a whistle blast that launches
  her.

### Shell Slide (R)

![Shell Slide](../design/references/bashie/bashie-r-shell-slide.jpg)

![Flipped after a miss](../design/references/bashie/bashie-r-flipped.jpg)

### NO RUNNING! (F)

![NO RUNNING!](../design/references/bashie/bashie-f-no-running.jpg)

- It sets up the rest of her kit: slowed fighters can't escape the slide or get out of the gush.
- The image shows a blast. The move is a slow: the opponents should look stuck in slow motion.

## Engine pieces

| Special | What exists | What's new |
| --- | --- | --- |
| R | `slop.ability.steered-charge.v1` (Sir Bonk's Joust) already steers, gives armour, ends on a hit or a miss and handles thin air ([ability architecture](../systems/ability-architecture.md)). | Mostly new clips and data. |
| A | Ki Shot's hold-and-release, and `stunTicks` 0 already shoves without hitstun. | Done 2026-10-22: `slop.ability.charged-stream.v1` (FLUDD: pump, stream, air push back) and no hitstop for pure pushes. |
| E | Close to Manki's Jetpack Boost and existing landing hits. | An upward burst with an optional slam. |
| F | Nothing: nothing in Shared slows another fighter yet. | A timed state in the simulation (no run, no dash, slower walk), plus a sign in presentation. |

## Concept images

The images in [`docs/design/references/bashie/`](../design/references/bashie/) were generated with an OpenAI image
model on 2026-10-21. Concept A came from a prompt and the others were made from concept A, except the body sheet,
which came from the redesign. They are design references, not game art.
