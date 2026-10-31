# ADR-0049: Manki, the flying bomber

**Status:** Accepted for the Training prototype — 2026-10-29. Supersedes this ADR's first version (2026-10-28,
"fire or a blast sets off a can"), which was built and played but never landed. The maintainer's verdict on it:
the jetpack flight was "the only usable spell and a very fun one", hitting cans was too hard to base a kit on, R
was fine, F did not fit. The maintainer approved the kit below and the coordinator's picks ("I agree with your
picks"), and asked for a longer E that does not feel oppressive. The values under
[Prototype defaults](#prototype-defaults) are the coordinator's and stay open to the maintainer's play verdict.
**Extends:** [ADR-0039](0039-full-world-rollback.md) (World Snapshots),
[ADR-0042](0042-controller-layout-three-normals-homing-dash.md) (normal roles) and the fighter-state pattern of
[ADR-0045](0045-bashie-shell-state.md)

**Revision 2026-10-31.** The maintainer played the bomber and found it "way too much": Manki spends too long in the
air, where he is hard to hit, and moves too much. Q loses its movement and becomes proximity mines, R charges with
a visible lock before it fires, and E gets a long cooldown because it is his recovery. These replace the Q, R and E
sections below. The approved VFX follow-up below adds F startup; normals are unchanged. See [Revision 2026-10-31 decisions](#revision-2026-10-31-decisions).

## Context

Manki is the roster's chaotic gadget fighter. His specials were four unrelated weapons, and the first rework tied
them with an ignition rule that asked players to hit small cans on the floor. In play only the steerable jetpack
flight worked, and it was fun. The kit is rebuilt around it: Manki plays from the air as a bomber, and every
special either works in flight or starts it.

Verdicts across the roster favour specials that find their target or area by themselves and pay off on contact.
Manually aimed lines keep failing, and every fighter already has the homing Beeline dash, so a move that only
travels at someone must also threaten a kill.

## Decision

### Flying is a fighter state

E puts Manki into **Flying**, a state other moves read, as Bashie's shell is. Q and R keep him Flying. A normal ends
the flight and plays as his air normal. Running out of fuel, pressing E again, or being hit ends it too. After it
ends he falls with his usual air options.

### E, Jetpack

The flight the maintainer played, made longer:

- The short ignition with its small hitbox, then powered flight steered by the stick, low and fast, holding its
  heading with no input, sinking slowly. Each jump press gives a burst of height.
- **Longer, not oppressive.** More fuel than the first build. In exchange the fuel is visible, refills only after
  he lands, and E's cooldown starts when the flight ends. Any hit knocks him out of the air.
- Stays recovery-capable.

### Q, Bombs Away

No aiming. Three charges in the existing `chargePool`.

- **Flying:** a bomb drops straight down from under him and explodes when it touches the ground or a fighter.
  Several in a row make a bombing run. The slower fall gives opponents time to step out.
- **Grounded or falling without fuel:** **hop and drop.** A small jet hop backward, away from his current target
  (away from his facing with none). The bomb stays where he stood and explodes when a fighter touches it or after
  a short fuse.

Both bombs show a quiet orange circle on the floor with the blast radius. Its inner ring closes as impact or the
fuse approaches; the outer boundary stays fixed. The circle follows Shared floor collision, including raised
platforms, and disappears over pits. It never pulses, including with ReducedFlashing enabled.

### R, homing rocket

Behaviour unchanged. Usable while Flying without ending the flight.

### F, Kamikaze

He locks onto his current target, flies into them at full throttle and explodes on contact. The blast also throws
Manki, with no damage to him. Started while Flying it is a dive-bomb. With no target he flies along his facing and
explodes when the run ends. It must be a kill threat: an ordinary dash already reaches people. The Aerosol Inferno
leaves his kit; its clips stay where Manki Mayhem uses them.

### Normals

Plain body hits per ADR-0042. Ground button 2 becomes **Monkey Flip**, a backflip launcher. Air button 3 becomes
**Club Tail**, a forward flip bringing the tail club down. Both go through the animation workflow as their own task.

### Subtract first

The first version's machinery that this kit does not use leaves the code: the "fire or a blast sets off a can"
rule (`IgniteMankiCans`, the igniter tags, the early warning and the bigger blast), F's walking charge and the
Aerosol Inferno capability. The landed bomb record may stay if the grounded drop uses it; nothing else keeps it.

### Engine shape

`CharacterState.Flight` is a `FlyingState` containing the active flag, remaining fuel, lift-burst clock,
lift speed and held heading. `ActionState.Flying` is the state between moves. Q and R are ordinary
activations while the flight component keeps movement ownership. `Flying` owns their shared policy,
steering, fuel, entry, exits and refuel. Unsupported accepted states end flight. A normal leaves Flying
before activation and selects its air slot. Hits end flight before the ordinary reaction runs.

`slop.internal.manki.jetpack-boost.v3` owns only the three-tick ignition. It emits the existing hitbox
and hands off to Flying. No E activation survives into Q or R. Flying suppresses ordinary gravity and
jump consumption, steers at the existing speed, holds neutral heading and supplies jump bursts. Its
fuel clock keeps running through Q and R. It never refuels while active. Ground support after flight
ends refills the tank. E's 210-tick cooldown starts on the exit, including a hit, normal or Kamikaze.
Training's no-cooldowns option bypasses the cooldown but cannot refill an empty airborne tank.

`slop.internal.manki.round-bomb.v3` emits a resolver contact bomb immediately. In Flying it has zero
horizontal velocity and ordinary Shared gravity. First fighter or ground contact queues the blast.
Terrain contact also sweeps the bomb against authoritative triangle platforms. A flight timeout over
void discards it. Outside Flying, the same capability leaves a stationary contact bomb with a short
expiry fuse and supplies the backward hop. There is no landed-can collection or ignition metadata.
The existing three-charge pool retains its independent regeneration clocks.

`slop.internal.manki.kamikaze.v1` captures the current target and drives full-speed Shared movement
toward it. Without a valid target it retains its direction. A swept resolver contact volume covers
the path between ticks, including shields. Contact or run expiry queues one finisher blast and ends
the activation. The blast uses ordinary fighter, Shield, object and mode filtering. Captured explosion
payloads can apply a direct owner push without a hit record, damage, Hitstop or Hitstun. Q and F use
that push. R keeps its previous targeting, cast, projectile, blast and self-damage behavior.

World Snapshot format 15 serializes the fighter flight component, contact-bomb flags, captured
explosion push/completion policy and Kamikaze's target, direction, clock and previous position.
Rollback restores the whole simulation. KO, removal and round/reset paths discard dropped bombs
without a blast and clear flight. The release protocol remains unchanged until a coordinated release.

The source compiler and cooked loader share `MankiCapabilityBounds`. The replacement Q/E capability
IDs and Kamikaze remain trusted built-in exceptions. Unity reconciles round bomb visuals from resolver
identity. `MankiFlightPresentation` reads saved fuel and keeps the existing pack exhaust/trail alive
while Flying, including during Q/R. Kamikaze uses its package trail and explosion. The shake clip
remains bound for Mayhem. Removed aim/fire clips remain assets.
Normals, their bindings and the rocket prop are unchanged by this prototype.

The optional `presentation.flying` semantic animation role follows `presentation.shell` through source,
compiler, cooked admission, catalog and runtime definition as `FlyingAnim`. `presentation.mayhemLaugh`
explicitly retains the shake clip for the Mayhem host, independently of F. Manki binds the accepted
48-tick cruise loop. Shared samples frame zero for stable flight hurtboxes; the small bob is cosmetic.
Unity derives loop time from spent fuel, so Q/R and rollback do not restart it. A masked Animancer layer
plays Q/R on the upper body and fades back to the continuing loop. Their Shared hurtboxes retain the
existing full-clip sampling. Kamikaze binds its accepted 56-tick one-shot over the unchanged 55-tick stage.
Unity smoothly pitches the model and bone-attached pack trail toward replicated velocity. Shared keeps
the horizontal baked body and the existing swept capsule contact test; presentation tilt changes no
admission, movement or contact result.

## Prototype defaults

- **Fuel:** about one and a half times the first build's two seconds.
- **Refuel:** only on landing. Taking off again is gated by E's cooldown.
- **Normals in flight:** end the flight.
- **Bombs:** gravity is 9 m/s², about one second for a 4.5 m drop. Contact radius is 0.6 m and blast radius is 4 m;
  the bomb model matches the contact radius. The 180-tick flight limit covers the longest current arena drop.
- **Hop and drop:** the bomb hurts only opponents; its blast pushes Manki with no damage. The fuse stays at 60 ticks,
  with the same floor warning as an air drop. Damage, knockback and hop speeds are unchanged.
- **Kamikaze self-blast:** pushes Manki with no damage and does not stun him.
- **Kamikaze speed:** slower than a dash, so the target can see it coming. The first build's 28 m/s felt too fast in
  play (2026-10-29). It now travels at 18 m/s with about the same reach.
- **Teams and modes:** bomb and Kamikaze blasts follow the mode's ordinary hit rules.
- **Removal:** dropped bombs disappear with no blast when Manki is knocked out or the round resets.
- **Numbers:** set by the implementer as starting points, then by play.

## Consequences

- Manki's gameplan becomes take off, bomb, rocket, dive. When his fuel is out he is a monkey with normals, which is
  the opponent's window.
- A fighter state that several moves read is new for the jetpack but follows Bashie's shell.
- The kit gets smaller in code: the ignition rule and the aerosol capability go.
- The rocket prop swap and Kamikaze's look are presentation follow-ups.

## Revision 2026-10-31 decisions

The maintainer asked for less air time and less movement. The coordinator chose the details below; numbers are
starting points for play.

### Q, Proximity Mines

Q no longer moves Manki. The hop backward is removed, and so is the mine blast's push on him.

- **Grounded:** a mine is set at his feet.
- **Airborne, Flying or not:** he tosses the mine straight down. It falls, settles on the floor below and becomes a
  mine there. An opponent it hits on the way down sets it off at once. Over a pit it disappears.
- **Arming:** a set mine arms after a short delay, so it cannot be dropped point-blank on someone.
- **Trigger:** an armed mine reacts to an opponent inside its trigger radius. It beeps for a short warning and then
  explodes. Manki never triggers his own mines, and their blast ignores him. Teammates follow the mode's ordinary
  hit rules.
- **Limits:** three charges as before. At most three mines are on the field; a fourth removes the oldest without a
  blast. An untriggered mine fizzles out after its lifetime, also without a blast.
- **Readability:** the existing floor circle shows the blast radius. An armed mine shows a blinking light so it
  reads as live.

### R, charged lock

Press R and the rocket fires after a short charge instead of at once. The target is captured on the press. During
the charge a lock-on reticle sits on that target, visible to every player, and closes in until the rocket fires.
With no target there is no reticle and the rocket flies along his facing. Grounded he is rooted while charging;
Flying he keeps flying. Facing follows the captured target during charge while flight retains its movement heading.
A hit cancels the charge. Homing after launch is unchanged.

### E, long cooldown

E stays his recovery and keeps its fuel. Its cooldown, still counted from the flight's end, becomes much longer
than the roster's 210 to 240 ticks, so spending it to bomb the stage leaves him without a recovery for a while.

### Revision starting values

- Mine arm delay 30 ticks, trigger radius 2 m, warning 12 ticks, lifetime 600 ticks, blast radius and damage as
  the bomb's. Grounded cast keeps the 12-tick stage.
- R charge 40 ticks before launch.
- E cooldown 600 ticks after the flight ends.

### Approved VFX follow-up, 2026-10-31

Kamikaze now has sixteen vulnerable startup ticks before its existing 54-tick travel allowance.
Startup captures the target immediately. Manki stays rooted on the ground and falls normally in air.
Launch retains 18 m/s and the captured target. The authored stage is 71 ticks.
Interruption clears startup and prevents delayed travel or blast. World Snapshot v23 stores the phase.
The protocol stays at 32 until the next release.

The presentation uses a pack announcement, launch ignition, twin short jets, and separate closed soot clods.
The blast remains 4 m with 24 damage and the same owner push and Hitstop.
R and F keep their existing clips until the separate animation authoring task is accepted.
