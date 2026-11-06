# Dusty

> **Identity (owner, 2026-11-04):** Dusty is a small-time tiger salamander who wants to be a legend of the West. He
> can't afford a gun, so he shoots finger guns. His mustache is fake and glued on, because amphibians can't grow
> hair. He is proud of all of it and he is good in a fight; the joke is on his dignity, never on him losing. He named
> himself Dusty after the desert, which is funny for an animal that is permanently damp. His menu line stays
> "The fastest fingers in the West."
>
> His package ID is still the working `salamander` (`CharacterClass` 7, selector `Salamander`). The player-facing name
> changes when the kit is built. Earlier notes call him Salamander or Billy the Gill; the owner dropped the axolotl
> name on 2026-10-26.

## Status

The identity and the specials were settled on 2026-11-04, before any more animation. On 2026-11-05 the owner remade
the model without the poncho and swapped the Sombrero Copter for the Lasso Grapple. Most of what the package plays
today predates that: the specials still use the 2026-10-27 shapes, and the aerials are stand-ins.

| Piece | State | Where |
| --- | --- | --- |
| Identity and name | Picked 2026-11-04 | This page |
| Look | Remade by the owner in Tripo 2026-11-05: vest and neckerchief, no poncho, steel spurs added in the build. Installed live at FightGuy's height. Training check #75 | Below; the [old model render](../design/references/dusty/dusty-model.jpg) shows the poncho |
| Kit | Specials locked 2026-11-04; E changed to Lasso Grapple 2026-11-05 (below) | This page |
| Move concepts | Board made 2026-11-04, verdict pending | [Move concepts](../design/references/dusty/dusty-moves.jpg) |
| Lasso Grapple concepts | Approved 2026-11-05 | [Grapple concepts](../design/references/dusty/dusty-grapple.jpg) |
| Rig | Rebuilt for the v2 model 2026-11-05: scaled to FightGuy's head height (1.33 m), per-finger chains, own tail, hat, mustache, straw and spur bones. Installed and cooked valid | [salamander_rig](../../tools/blender/salamander_rig/README.md) |
| Package | `salamander`, admitted 2026-10-27, shown with the WIP badge | `client/Unity/Assets/CharacterPackages/salamander/` |
| Ground normals | His own clips; cadence GOOD on 2026-10-29 | `Animations/salamander_g*.anim` |
| Specials | Not built in the shapes below. The package still plays camera-aimed Finger Guns, Tumbleweed and the old Big Iron | `character.json` |

## Look

He is lanky, about FightGuy's and Wibou's height. His skin is orange-yellow with dark brown blotches. He wears a wide
brown cowboy hat with an orange band, an orange neckerchief, a cropped brown leather vest over a cream shirt, a belt
with a holster, blue jeans and big brown boots. A black curly fake mustache sits on his snout and a wheat straw sits in
the corner of his mouth. Each hand has a thumb and four fingers; his finger gun is the index and middle finger
together with the other two curled.

The owner remade the model in Tripo on 2026-11-05. The first model's poncho never hung right on him, in spite of a
rebuilt rig, cloth springs and a re-draped version, and the new one has no poncho. Its boots had no spurs, so the build
adds a steel spur to each heel for Spur Kick and Spur Stomp.

## Kit

Dusty is a mid-range pest who drags people in. Finger guns poke from range and make the target move. The lasso pulls
a fighter to his feet for a close hit or a throw. Big Iron is the payoff and doubles as a second recovery. The rope
does both of his pulls: R pulls a fighter to him, E pulls him to the stage or to a fighter.

Four owner rules shaped the kit:

- No counters (2026-10-26).
- Specials pick the current target and can be dodged by moving
  ([character kit principles](character-kit-design-principles.md)).
- Normals are plain scrappy body hits. The gun gag lives only in the specials (2026-10-28).
- Shape before numbers. The tables below carry no tuning values.

### Movement

Proposed, not yet reviewed: light and springy, with a high hop, an average run with one hand holding his hat down,
and a slightly floaty fall that finger-gun shots slow further. The package still borrows Manki's movement.

### Normals

| Slot | Move | What happens |
| --- | --- | --- |
| Ground 1 | Haymaker | A big wind-up punch. |
| Ground 2 | Jumping Uppercut | A hopping uppercut. |
| Ground 3 | Tail Spin | He spins with his tail out, hitting all around him. |
| Air 1 | Spur Kick | A stiff flying kick held out spur first. Replaces Double Tap, which used finger guns. Not built. |
| Air 2 | Tail Whip | His tail flicks up over his head. |
| Air 3 | Spur Stomp | Both boots stomp down. Spike. |

### Specials

| Slot | Move | What happens |
| --- | --- | --- |
| A | Finger Guns | Tap for one shot at his current target, or straight ahead with no target. After six shots he plays the reload gag on his own: blow the smoke, twirl, holster. The gag is his weak moment. Shots in the air slow his fall. The shot is a plain cartoon bullet. |
| E | Lasso Grapple | He throws the loop up toward the nearest stage edge in reach. It catches, he hauls himself up the rope and pops over the edge onto the stage. If the loop meets a fighter first, he yanks himself to them boots first and kicks them. On a miss the loop flops and he keeps falling, with Big Iron's recoil as his backup. His recovery. |
| R | Lasso | He throws a rope loop at his current target. On a catch he reels them in, swings them overhead rodeo style and throws them where the stick points. On a miss the loop flops on the floor and he coils it back. Moving off the line dodges it. R cancels the reload gag. |
| F | Big Iron | He presses both hands together into the biggest finger gun he can make and fires one huge shot at his current target. The recoil throws him backward, which makes it a second recovery, and blows his mustache off. |

The owner dropped the Sombrero Copter on 2026-11-05 for a recovery built on the lasso. It also repeated Nyx's Ring
Light Rise, where something spinning overhead lifts her. The owner rejected two earlier specials on 2026-11-04. A tongue lasso lost to a real rope ("a lasso would be
easier"), and Tail Drop, a thrown tail that kept thrashing, was "unsure". The 2026-10-27 Tumbleweed rolled along an
aimed line, the pattern that failed on Bashie's Shell Slide, and is gone.

## Effects

Dusty's effects use the game's flat comic style in dust-cloud tan and sun orange, with white gunsmoke puffs and a near
black ink outline. The [move concepts](../design/references/dusty/dusty-moves.jpg) show one key frame per special, the
reload gag, the mustache peel and a lasso-into-Haymaker combo. Effects match each move's reach and never draw the
hitbox.

## Open questions

- **Grapple anchors.** Which surfaces the loop can catch (stage edges only, or walls and platforms too), and what it
  does on a stage without a clear edge. Ledge grabs are off in this game, so the haul ends with a hop onto the top,
  not a ledge hang. Bashie's tether already detects its ring hitting the stage; today that counts as a miss.
- **Big Iron as a recovery.** The recoil distance needs a feel check in play.
- **Movement.** His own movement values have not had a pass.
