# Bashie: NO RUNNING! (F)

**Status:** Built 2026-10-26 and awaiting the owner's in-game verdict. Bashie's own cooked whistle timeline
replaces the Manki Aerosol Inferno stand-in.

The owner approved Bashie's kit on 2026-10-21 ([Bashie](../characters/bashie.md#specials)). F is the
whistle: she takes a huge breath and puffs up like a balloon, then blows: TWEEEET! The inhale is the
tell. If she is hit mid-inhale, she whizzes around deflating.

The kit first described the blow as a slow ("can't run or dash and walks slowly"). On 2026-10-26 the
owner made it a stun instead: "lets do area when used but lets make it a stun not just a slow.
everyone around her is stunned for a bit. she has a free follow up after so at least 1sec stun
(maybe 2 ?)". They also chose that Shields block it, and the bigger timing: a 0.8 s inhale, a 5 m
radius and a 12 s cooldown.

**In short:** the stun needs no engine change. The blow is an ordinary `spawnHitbox` with a fixed
hitstun and no launch, the same combination Sir Bonk's pot lid uses when it hits him. It is content
only: no new state, no World Snapshot or protocol change.

## How a hitbox stuns without a launch

- A hitbox may set `fixedHitstunTicks` (1 to 240) with a nonzero `stunTicks` gate (runtime API
  1.3.0, [ability architecture](../systems/ability-architecture.md)). It replaces the hitstun the
  launch formula gives, not the launch.
- `ApplyFixedHitstun` only replaces hitstun that the formula started (`Simulation.cs:1855-1861`),
  and the formula starts none without some launch (`Simulation.cs:1893-1912`). The whistle therefore
  needs a tiny base knockback: 1, with no growth and no damage, pushes at 0.17 m/s.
- The push must point straight down (angle -90). Hitstun keeps the horizontal knockback velocity
  for the whole stun, with no friction (`Simulation.cs:919-929`). At angle 0 the 0.17 m/s push slid
  the stunned fighter 0.34 m over the 2 s. Straight down it slides 0 m, on the ground and in the air,
  and the stun still lasts 120 ticks (probe in the ignored `tmp/bashie-f/mechanism-down/`).
- The pot lid's self-hit builds a similar contact in code: no base knockback or growth, angle 0 and
  a fixed stun (`ServerSimulation.cs:2126-2131`). Its small damage supplies the launch the formula
  needs.

## Rules

These are the ordinary hit rules, so none of them needs new code.

| Situation | Rule |
|---|---|
| Who | Every fighter whose hurtbox touches a 5 m sphere around her chest during the blow, once each. |
| Shields | A Shield blocks it, as it blocks any hit. |
| Invincibility | An air dodge's or a respawn's invincibility dodges it. |
| Teams | The match's attack filter decides, so teammates are spared when friendly fire is off. |
| The stun | Their move is interrupted and they stay in Hitstun for 2 s with no launch. In the air they fall. |
| The follow-up | Any hit during the stun replaces it with its own launch, so she gets a free follow-up. |
| Damage | None: it is a whistle. |
| Herself | Her own hitbox never hits her. |
| Hit during the inhale | The move ends like any interrupted move. No hitbox spawns and F's cooldown starts, as for any cancelled move. |
| In the air | The same move through the `air.F` alias. A gravity window lets her hang while she inhales, like a balloon. |
| CPU | It reads the hitbox's reach, so it blows when someone is within 5 m. |

Two side effects come with using a hitbox. It touches a Slopball ball inside the 5 m (a tiny push),
and it would break Course Targets in range if she ever gets a Character Course. Check the ball in
Slopball after the first try.

## Values for the first try

| Value | Ticks | Seconds |
|---|---|---|
| Inhale, then the blow | 48 | 0.8 |
| Recovery after the blow | 24 | 0.4 |
| Stun, no launch | 120 | 2 |
| Cooldown | 720 | 12 |

The radius is 5 m. The stun is 2 s because the follow-up has to work on someone at the edge. The
blow's hitstop freezes her and them for 6 ticks (`ServerSimulation.cs:139-140`), then her 24
recovery ticks run. Running 4 m at her 11.5 m/s takes about 21 ticks, and her Slap hits on its
ninth tick. That is about 60 ticks, or 1 s, from the blow to the hit. A 1 s stun would end at about
the same moment. A 2 s stun leaves time for that hit or for a stronger move.

## Presentation

The first try uses placeholders. The effects pass follows concepts painted over real captures, as
for her other specials.

- **Inhale.** The owner picked C, "full balloon", on 2026-10-26. The client cue swells her yellow
  belly and cheeks with the baked `Puff` blend shape in `Presentation/BashieBodyPuff.asset`.
  `BashiePresentationBuilder.PuffProfile` sets belly 0.11 m, cheeks 0.05 m, sides 0.05 m,
  roundness 0.7 and six smoothing passes. The Shell bone grows to 1.2 times its rest scale.
  The swell eases up to full size at tick 40 with a small wobble and snaps to zero at tick 48.
  Blend weights stay nonnegative. The builder always starts from the source FBX, classifies yellow
  vertices from the source albedo and smooths the weights over welded triangle adjacency.
  Rerun `sloparena.presentation.bashie.puff.build` after re-importing the rig or changing its albedo.
- **Whistle.** Its measured mouthpiece and beak offset define the held pose once in the Head
  bone's rest frame. Runtime placement follows that bone, including when the real clip tips her
  head back. The final whistle transform accounts for nonuniform scale from the hit accent.
  Release, disable and knockout restore the whistle, shell scale and blend weight.
  A stale prefab without `Puff` logs one warning and skips the swell.
- **Blow.** TWEEEET, soft and low after the owner's note on Hose Down's first sounds. Rings spread
  out to 5 m, so players see the area. NO RUNNING! pops up over her like NO SPLASHING!.
- **Stunned fighters.** They play their hard hit reaction stretched over the stun until a stunned
  look exists. The slot's hit presentation can add a marker over each stunned fighter.
- **Deflate.** When her inhale ends in a hit, the puff lets her down with a squeal and puffs of air.
  The whizzing is spin over her real knockback, client only.
- **Animation.** Her idle stands in, with the puff cue. Her own clips (inhale, blow and recovery)
  follow the GenRA workflow from pose direction sheets the owner picks.

## Tests

Bashie's kit tests run her F through the catalog path:

- a fighter within 5 m is stunned for exactly 120 ticks after the hitstop and, with no input, does
  not slide (under 0.01 m), on the ground and in the air;
- a fighter outside 5 m, a shielding one and an invincible one are not stunned;
- a hit during the inhale stops the blow;
- the air variant stuns too, and she never hits herself;
- the slot's cooldown is 720 ticks.

Run them with `dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true`
([testing](../testing.md)). In Training, check the dummy in and out of range, a shielding dummy and
the puff, the sound and the rings.
