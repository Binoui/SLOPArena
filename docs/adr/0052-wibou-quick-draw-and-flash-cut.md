# ADR-0052: Wibou's Quick Draw and flash cut: dash through, freeze, pop on the click

**Status:** Accepted for the Training prototype, 2026-10-29. The maintainer approved Quick Draw for R ("down to try
it") and the multi-target flash for F ("sounds way funnier if we can pull it off"). The values under
[Prototype defaults](#prototype-defaults) are the coordinator's and stay open to the maintainer's play verdict.
**Supersedes:** Wibou's charged Dash Slash (`slop.ability.charged-directional-dash.v1`), Skyfall
(`slop.internal.wibou.blade-flurry.v2`), and the "no positional sweetspot" pillar in the historical concept of
[Wibou](../characters/wibou.md).

## Context

Wibou is the fastest fighter, and his specials stopped him. Measured in the offline Shared sim on 2026-10-29 (flat
ground, idle FightGuy): charging R set the dash distance, so tier 3 and a full charge flew past a target 3 m away for
1 damage, the dash moved at 10.5 m/s against his 14 m/s run, and a full charge took 122 ticks before he could attack
again. Skyfall's launch lifted FightGuy 5 cm at any percent, the victim could act 10 ticks after the hit, and the spike
came about 45 ticks later; a shield or a step escaped it. Rising Slash (E) never hits a standing fighter. E and A do
not change here.

The approved moves:
- **R, Quick Draw.** He dashes at once toward his current target, faster than he runs, one fixed length. The blade tip
  at the end of the dash is the sweet spot and cuts hard. A fighter he dashes through takes a light cut and is left
  stunned behind him. While R is ready, a green bracket on the target's ring shows that the target stands at
  sweet-spot distance.
- **F, the flash** (working name "Omae wa mou shindeiru"). A readable stance with a reticle on every opponent in
  range, then he dashes through each locked opponent in turn. Each cut freezes its victim without freezing him. He
  sheathes, and at the click every frozen victim pops at once with his big hit. "NANI?!" pops over each victim first.

Two candidate designs and a cross-judge are in `tmp/wibou-quickdraw-flash/` (`candidate-claude`, `candidate-codex`,
`cross-judge.md`). This decision takes R's contract, its endpoint tip and its test plan from the Codex candidate and
F's freeze mechanism from the Claude candidate, corrected by the cross-judge's source checks.

## Decision

**Two public capabilities, one engine addition.** R is `slop.ability.dash-strike.v1`. F is
`slop.ability.flash-cut.v1`. The engine adds a held cut: a hit whose victim stays frozen until a chosen match tick
and then takes the ordinary queued launch. Both retired IDs fail closed with a reason in the wave that replaces them,
with every caller (Unity aim and charge-tier code, `PlayerRenderer`, Ability Lab, CPU, records, tests). No other
package uses them.

**Quick Draw takes its line at activation.** `OnStart` calls `ServerSimulation.TryGetCurrentTarget` and keeps that
target's horizontal bearing; with none it keeps his facing. It commands the first travel interval in `OnStart`, so
the press tick moves. The stick, the camera, a hold and later target changes never bend it. The dash is level on the
ground and in the air, with gravity off while dashing. It phases through fighter pushboxes and keeps the charged
dash's terrain rule (a tick that keeps under 60% of its travel stops it; a landing does not). A shield stops it, as
before.

**One cut per fighter.** The light cut is a root-relative sphere following the body from the first travel tick. The
tip cut is an authored root-relative volume placed in front of him when the dash reaches its full length, active for
its own short lifetime while the clip seeks `tipPoseTick`. It does not follow the animated sword, so the sweet spot
and the bracket stay put when the clip changes. Both cuts share one activation hit group: a fighter takes at most one
cut per Quick Draw, and the tip wins a same-tick overlap. A terrain or shield stop places no tip. The light cut does
not freeze Wibou; the tip cut freezes him as ordinary hits do.

**After a light cut he turns to face the victim** on his last recovery tick, if that fighter is still valid.
Startup aim correction cannot do it: Wibou's normals turn at most 90° at startup. The light cut's
`fixedHitstunTicks` is the follow-up window.

**One function places the sweet spot.** `DashStrikeReach` (pure Shared) classifies a standing target at the current
spacing as tip, light or none from the cooked distance, speed, light radius and tip volume. Unity's bracket and the
CPU both call it, and a test pins it against the simulation.

**Sampling covers the line.** The compiler rejects a dash whose per-tick travel exceeds twice its cut radius, for
Quick Draw and for flash legs. Per-tick sampling of a straight line then leaves no gap, without a movement-trace hook.
A test places a small target between two tick positions.

**The flash locks once and knows its click.** The stance holds him still with gravity off. On its last tick he locks
every eligible enemy fighter within `lockRange` (3D, from his centre to theirs): not allies under the match rule, not
eliminated, not in respawn grace, not in a grab or tether pair, not already held. Shielding and air-dodging fighters
lock, so their defence decides the cut. Order is nearest first, ties by entity ID, at most `maxTargets`. With nobody
locked he plays a short whiff sheathe, his punish window. With n victims the capability stores the absolute pop tick,
`now + n × legTicks + sheatheTicks`. Nothing moves it.

**Legs go straight and take fixed time.** Each leg lasts `legTicks`. It fixes its end point when it starts, a point
`passThroughDistance` beyond where its victim stood then, with gravity off and pushboxes ignored. A victim who keeps
running clears the leg; shield and air dodge also escape it. (Homing legs, the first version, made the flash too sure
a hit; the maintainer chose straight legs on 2026-10-29.) A wall ends the leg where he stands and the next leg starts there. A victim that died,
respawned or became ineligible is skipped, and its time folds into the sheathe. Legs may go off-stage. Each leg's cut
is a root-following sphere restricted to that leg's victim (`Hitbox.OnlyTargetId`) that does not freeze Wibou.

**A held cut is a long freeze with the ordinary queued launch.** An unblocked, unarmoured cut retires the victim's
own move, applies its damage, sets `ActionState.CutHeld`, and freezes the victim until the pop tick
(`HitstopTicks = popTick − now`). Its knockback is queued in the existing `QueuedKB*` fields and fires when the
freeze ends, so every victim launches on the pop tick, whether or not Wibou is still in his move. On the pop tick,
before movement, each victim's queued direction is re-aimed away from Wibou while he is still in that flash: the cut
stored the direction toward where he stops, and a low-percent pop launched FightGuy into his back. An interrupted
Wibou leaves the cut's direction. While held, SDI is
off (DI is still read), and the body is immovable in pushbox resolution. When a held freeze ends, Simulation returns
the fighter to `Idle` before the queued launch applies, and any `CutHeld` fighter with no freeze converges to `Idle`
the same way. Grabs, tether rings and Bashie's wave refuse a held fighter. Another fighter's hit on a held victim adds
its damage only: the freeze, the queued launch and the KO credit stay the cut's.

**The simulation records each release.** When a held freeze ends it records the release, and the pop cue comes from
that record, so the burst plays even if Wibou was hit first. Wibou's capability emits the lock pulses, "NANI?!" over
each still-held victim `revealLeadTicks` before the pop tick, and the sheathe click. Several victims on one tick keep
distinct event keys through deterministic operation indices on the existing presentation sources.

**The pop is the payoff, not a combo starter.** After the click his recovery lasts at least `recoveryTicks` and until
every victim he popped has left hitstun, at most 60 ticks longer, so nothing follows the pop for free.

**Wibou during F** stays `Attacking` with gravity off from the stance to the click, then recovers with gravity. No
armour, no IASA. A hit on him pauses his move during its freeze and its hitstun then ends the move. Victims he
already cut still pop on the pop tick. `ServerAbility.SpawnHitbox` gains a construction option that sets
`FreezesOwner = false`; it was always true.

**Courses and the CPU.** Flash is fighter-only. The Neo Mall layout and boss budgets stay; its specials route test
drops the charged-row and Skyfall shortcuts and uses Quick Draw where it reaches, normals and kunai. The CPU models
both capabilities (`DashStrikeReach` for R; lock range and startup for F). Wibou's Arcade records stop comparing,
as for earlier package changes. Simulation changes go under Unreleased in `SimulationProtocol.cs`; the runtime API
minor and the snapshot format take the next free values when the code lands (ADR-0049 and ADR-0050 hold the current
ones).

## Prototype defaults

- **Quick Draw:** 3.5 m at 28 m/s, 16 recovery ticks, 90 cooldown ticks. Light cut 3 damage, zero base and growth,
  stun gate 12, fixed hitstun 38, radius 0.6 m. Tip cut 13 damage, 35°, base 10, growth 42, stun gate 12, a few
  ticks long, placed where the current clip's forward slash reaches (about 0.5 to 2.2 m ahead at blade height). These
  match the feel test in Training. A shield stops the dash. The turn after a light cut is on.
- **Flash:** stance 24 ticks, lock range 7 m, up to 3 victims, straight legs of 10 ticks with a 0.8 m cut, 1.2 m past each victim, sheathe 30
  ticks, "NANI?!" 18 ticks before the click, recovery 14 ticks, whiff sheathe 40 ticks. The cut is 16 damage, 35°,
  base 12, growth 42 (Skyfall's finisher). Cooldown 540 ticks.
- **Free-for-all:** others add damage to a held victim but cannot launch, grab or catch them, and the pops still happen
  if Wibou is hit before the click.
- **Clips:** `anim.wibou.r` for both dashes; `anim.wibou.r-charge` (R's old stance) for F's stance and sheathe until
  a GenRA sheathe exists. Effects reuse Wibou's pixel set; the CRIT pop, lock stamp, "NANI?!" and pop burst follow
  the VFX concept workflow later.

## Consequences

- Wibou has one fewer trusted `slop.internal.*` capability. Two Wibou-only contracts become public primitives.
- The new state and hitbox field touch files that ADR-0049 and ADR-0050 are editing; each registration lands as a
  small separate change.
- The Neo Mall's special shortcuts retire. The course needs a play check with the new kit.
- Open for the maintainer: whether a light cut should instead end the dash a set distance behind the victim (a
  steadier follow-up, but no longer a fixed length); whether free-for-all hits should steal or rescue a held victim,
  and whether hitting Wibou before the click should cancel the pops; level or target-pitched airborne Quick Draw;
  F's in-game name.

## Alternatives considered

- **Keep the charged dash and tune data** (the Training feel test). A target near the end took both cuts, and the
  contract keeps a hold and manual aim the maintainer removed.
- **One primitive for both moves.** R's fixed length and tip and F's homing legs and held release would become mode
  switches with invalid combinations.
- **A separate held state with its own release pass** (Codex candidate). It needs a hit-response extraction from
  `ResolveHits`, a movement-trace hook and new release delivery modes in the files other threads are editing. The
  freeze and queued launch already are this mechanic.
- **Shorten R after a light cut and use the animated sword for the tip** (Claude candidate). Shortening breaks the
  fixed length the maintainer approved, and the sword's swing reached behind Wibou in the current clip.
- **Newest hit replaces a held victim's pop.** Less code and funny steals, but the simultaneous pop would stop being
  a rule.
