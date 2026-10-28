# ADR-0049: Manki's cans, one rule for his kit

**Status:** Accepted for the Training prototype — 2026-10-28. The maintainer picked the direction ("fire or a blast
sets off a can") from a design board and set Q's impact hit, three charges, a steerable E and a mobile F charge.
The values under [Prototype defaults](#prototype-defaults) are the coordinator's and stay open to the maintainer's
play verdict.
**Extends:** [ADR-0039](0039-full-world-rollback.md) (World Snapshots) and
[ADR-0042](0042-controller-layout-three-normals-homing-dash.md) (normal roles)

## Context

Manki's identity is a chaotic gadget fighter. His kit had drifted into four unrelated weapons that never interact:
an impact round bomb (Q), a vertical jetpack launch (E), a homing bazooka (R) and an aerosol flamethrower (F). Q and
R both did the same job, a ranged explosive aimed at the opponent. The v5 "pyro" model wears spray cans, a lighter
and a spray-can jetpack; the round bomb and the bazooka appear nowhere on it.

The approved direction ties the kit together with one rule: **Manki's fire or blast sets off his cans, with a bigger
bang.** Each special gets a distinct job. Q puts danger on the floor, E moves him through it, R chases someone, F
cooks from range.

## Decision

### Q, Can Bomb

- Hold to aim the ground cursor and release to lob, as today, on ground and in the air.
- **Landing pops.** Where the can first touches the ground it deals a small impact hit.
- **The can stays.** It sits where it landed with a burning fuse. When the fuse runs out it explodes with an
  ordinary blast.
- **Set off early.** Manki's fire (F's stream, E's exhaust) or any of his blasts (another can, R's rocket, the
  landing pop of a later can) touching a can sets it off at once with a **bigger** blast. Cans chain.
- **Readable.** A can about to go off early swells and glows red for a short warning before it bursts.
- **Three charges** in a charge pool (the existing `chargePool`), each regenerating on its own timer. The cooldown
  between throws stays short.

### E, Jetpack flight

E becomes a short, steerable powered flight instead of an unsteerable vertical launch.

- Press E: the existing short ignition with its small hitbox, then flight for a fixed fuel time.
- During flight he flies low and fast along the stick. With no stick input he holds his heading.
- He sinks slowly instead of falling. **Pressing jump** gives a burst of height.
- The exhaust behind him sets off cans he flies over, so he can rush through his own cans and leave them exploding
  behind him.
- Flight ends when the fuel runs out or E is pressed again. He then falls as normal with his usual air options.
  It stays recovery-capable.

### R, homing rocket

Behaviour unchanged (homing, auto-aimed). Its blast sets off any can it catches. Aiming the rocket at a can is not a
goal. The bazooka prop becomes a lit can rocket in a later presentation pass.

### F, Aerosol Inferno

Shake, fire and flame stay as accepted. **He can walk while charging**, at a reduced speed (the existing
`aimMovementSpeedScale`), so he can reposition and reach cans from a distance. Its flame sets off cans.

### Normals

Plain body hits, per ADR-0042's roles. Ground button 2 becomes **Monkey Flip**, a backflip launcher (Sweeping Kick
sent at 25° and could not launch). Air button 3 becomes **Club Tail**, a forward flip bringing the tail club down.
The other four normals stay. Both new clips go through the animation workflow as their own task.

### Engine shape

`SpellResolver.MankiCans` owns landed `MankiCanState` records. Each record retains the thrown hitbox's
emission ID, owner entity ID, activation, slot, ground/air identity, attack sequence, floor position,
fuse countdown, early-warning countdown, and captured ordinary/early explosion payloads. The can has a
0.35 m collision radius. It is not a fighter and does not consume fighter targeting or pushboxes.

The trusted `slop.internal.manki.round-bomb.v2` capability keeps the existing held cursor and ballistic
launch. The projectile passes through fighters. Its first ground contact queues the small landing pop
and creates the floor can. A flight timeout removes an unlanded can without a blast. The existing
`chargePool` supplies three charges. `CharacterState` stores three independent regeneration clocks.
Admission also bounds the combined flying/floor population to three, including after a long aim hold.

Fire hitboxes and explosion payloads carry `IgnitesOwnerCans`. E's nondamaging exhaust uses
`IsCanIgniterOnly`. `SpellResolver.IgniteMankiCans` is the single reaction rule. It checks the same
sphere/capsule geometry as ordinary contacts, requires matching owners, and ignores the can's own
activation so its landing pop cannot ignite itself. First contact starts an eight-tick red warning.
Further contact cannot restart it. The warning suspends the fuse and ends in the larger blast.
Ordinary and early can blasts use the existing pending-explosion pipeline and can ignite other cans.
Their fighter, Shield, ball, course-target, and friendly-fire outcomes use normal mode rules.

`MankiJetpackBoost` now executes `slop.internal.manki.jetpack-boost.v2`. After ignition it owns vertical
motion with zero ambient gravity and writes normalized stick heading, fixed horizontal speed, and
lift/sink velocity. Neutral stick retains the heading. A jump press restarts a bounded lift burst
without spending an ordinary air jump. Fuel, lift, heading, and the previous exhaust position are
ability snapshot fields. The existing landing-continuation seam keeps floor contact from ending fuel.
Fuel exhaustion, another E press, or ordinary interruption releases movement ownership. Existing pack
exhaust and trail bindings last through the powered phase. F uses mobile aiming at 0.35 speed.

World Snapshot format 14 writes flying can payloads, landed cans, ignition metadata, independent charge
clocks, and powered-flight state. Restore replaces the resolver collection and replays the same timers.
KO, fighter removal, resolver clearing, Training position reset, course retirement, Slopball point reset,
and Mayhem reset remove the affected cans without detonating them. Simulation protocol admission stays
at its existing release number until the release process folds this change into a version bump.

`MankiCapabilityBounds` validates the same new parameter bounds at source compilation and cooked
package loading. Both capability IDs remain trusted built-in exceptions. Unity's `MankiCanVisual` draws
a yellow cylinder with a red band and fuse spark, then swells/glows red from snapshot state. The
projectile presenter reconciles floor visuals by owner and emission ID, so rollback and resets remove
or restore them. The existing slot hit binding supplies the compact landing pop. Existing explosion bindings supply
the fuse/early blasts, scaled to their radii.

Starting values are a 3-damage, 1.1 m landing pop; a 240-tick fuse; a 10-damage, 3 m ordinary blast;
a 16-damage, 4 m early blast; an eight-tick warning; 360-tick independent charge regeneration; and an
18-tick Q cooldown after its existing throw recovery. Early blasts multiply base knockback by 1.35.
E retains three startup ticks and its 10-damage, 1.5 m ignition. Powered flight lasts 120 ticks at
12 m/s, begins with 12 ticks of 8 m/s lift, then sinks at 1.2 m/s. Jump supplies 12 ticks of 10 m/s
lift. The exhaust radius is 1.4 m. E retains its 210-tick cooldown and recovery admission.

Self can blasts apply ordinary push, hitstop, and hitstun with zero damage. They can interrupt powered
flight. Whether that interruption, the warning length, and the low flight's recovery height feel right
remain Training judgments. Normals and the rocket prop are outside this implementation.

## Prototype defaults

- **Who sets off a can:** only Manki's own fire and blasts. Other fighters' attacks pass through it. Letting
  opponents knock a can away is a candidate for later, not part of this slice.
- **His own cans:** their blasts push Manki but deal him no damage. R keeps its existing self-damage.
- **Cans on the floor at once:** bounded by the three charges; a fourth throw is not possible until a charge
  returns.
- **Teams and modes:** can blasts follow the ordinary hit rules (friendly fire, Mayhem, Slopball) of the mode.
- **Removal:** a can disappears with no blast when Manki is knocked out or the round resets.
- **Numbers** (pop and blast damage, fuse time, regen time, flight speed and fuel, jump burst, F walk speed): set by
  the implementer as starting points, then by play.

## Consequences

- Manki gains a persistent world object. Rollback, snapshots, CPUs and every mode reset must cover it.
- Q stops being a second ranged poke, so his neutral leans on floor control and F's range.
- E's new flight replaces the vertical launch; recovery feel will need a Training check.
- Mayhem's host can reuse the can look for its bombs so the host and the fighter match.
