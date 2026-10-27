# ADR-0044: Manki Mayhem's hazard director, host identity and runtime solids

**Status:** Accepted — 2026-10-27. The maintainer approved this record and the recommended
defaults for every open decision in the [implementation plan](../design/manki-mayhem-implementation.md#open-decisions-for-the-maintainer).
Implementation has started with steps 1–2 (offline).
**Extends:** [ADR-0037](0037-arcade-shared-objects-and-unified-stage-artifact.md) (focused
Arcade objects), [ADR-0039](0039-full-world-rollback.md) (World Snapshots) and
[ADR-0040](0040-online-arcade-room-pick-and-match-format.md) (Match Formats)

## Context

The maintainer selected **Manki Mayhem** on 2026-10-27: an online Arcade mode where Manki
hosts a game show from the booth of the `manki_mayhem` stage, shouts a call card each
round and throws one hazard pattern at two to four fighters. Hazards hit like normal
attacks; survivors score. The first slice has three rounds: SCATTER! (sticky bombs),
JUMP! (shockwave rings) and BREAK OUT! (walls around every fighter, a bomb incoming).
The [feature brief](../design/manki-mayhem.md) owns the rules.

The code has most of the pieces but not these:

- **No owner for hazards.** Every projectile is a `Hitbox` moved by `SpellResolver`
  with an `OwnerId`. `ResolveHits` already tolerates an owner that is not a fighter
  (knockback from the hitbox direction, no ability hook, no KO credit), and
  `CanAttackTarget` lets such owners hit fighters. But presentation events are keyed by
  (tick, entity, attack sequence, source, operation), and both
  `TimelinePresentationDispatcher` and `ProjectileVFXManager` skip owners without a
  registered renderer. `SpellResolver.RemoveOwnerEffects` cleans up per owner.
- **No pattern source.** Arcade components are built in the `ServerSimulation`
  constructor from the stage's Arcade section (ball, Slopball, course) and hooked into
  `Tick`. The Slopball Launcher is a seeded generator, not an entity: the GameHost draws
  a session seed and the snapshot carries it.
- **No sticky or ring projectiles.** Manki's Round Bomb explodes on contact or landing;
  his Bazooka homes. Hitboxes are spheres or capsules.
- **No runtime walls.** Arena collision is the baked static triangle set. The only
  runtime constraint is the Slopball net clamp (`ConstrainSlopballFighters`). Course
  targets are breakable hurtbox spheres that do not block movement.
- **Friendly fire is a boolean** in the Match start contract, admitted only for co-op
  Slopball; Master stores it as a Lobby-only Room preference.
- **The stage is a plain PvP deck** with no Arcade section. Its Unity prefab carries an
  empty `MankiHostAnchor`; Shared knows nothing about the booth.

## Decision

1. **A Match Format and rule.** `MatchFormat.MankiMayhem` (wire `manki-mayhem`) admits
   arenas with a Mayhem section and two to four fighters, starting at the arena's spawns.
   `MankiMayhemMatchRule` is immutable and carries the friendly-fire mode. It never
   eliminates by stocks; the simulation decides the end through `EvaluateMatch`, as
   Slopball and courses do.

2. **Hazards have a reserved Host Entity ID.** One constant identity in a new reserved
   range, clear of fighters, the Arcade ball (`1 << 32`) and course targets (`1 << 40`).
   The host is not a registered fighter, so combat treats it like an absent attacker
   (hitbox direction, no hook, no KO credit). It is still a real owner everywhere an
   owner is a key:
   - **Presentation:** host events use the host ID, the round index as attack sequence
     and the hazard's ordinal in its round as operation index, so keys stay unique and
     deduplicate across re-simulation. Unity registers a host presentation entry and a
     host projectile-visual owner that map hazard IDs to mode visuals.
   - **Cleanup:** ending a round calls `RemoveOwnerEffects(host)`, which clears hazards
     and pending blasts without touching fighters' projectiles.
   - **Filtering and records:** the host always hits fighters, whatever the friendly
     fire setting. The last-hit context names the host, so results and feeds can say
     "Manki got you" while kill credit stays empty.

3. **A seeded director in Shared.** `MankiMayhemSimulation` is a focused component, built
   by `ServerSimulation` only under the Mayhem rule; without it the deck is a plain stage,
   so Training's dev path keeps working. The GameHost draws the Match seed as it does for
   co-op Slopball. The deck order (a shuffled bag per pass) and every hazard's parameters
   are pure functions of the seed, the round index, the hazard ordinal and fighter state
   at a fixed decision tick. The World Snapshot stores the seed, the cursor (round,
   phase, phase tick), scores, bench flags and live round state; never a precomputed list
   or a random stream. Aimed choices are made at the call, and telegraphs fade in for
   longer than the Prediction Window, so a corrected aim moves a ring before anyone can
   read it.
   - **Tick hooks:** phase advance and hazard spawns before abilities; the solid
     constraint after movement, beside the net clamp; solid hits routed in `ResolveHits`
     like course targets; benching, settle checks and scoring after `CheckBlastDeaths`.
   - **Benching:** a KO uses normal respawn, then the fighter is benched until the next
     round. Benched fighters are inactive through the same `IsEliminated` combination
     online courses use: no movement, no hurtbox, no pushbox, and the GameHost relays no
     input until they return.

4. **Two new hazard primitives**, engine-owned and versioned under
   [ADR-0024](0024-deterministic-creator-gameplay-primitives.md), carried by ordinary
   `Hitbox` values in `SpellResolver`. Only the director uses them at first; package
   admission is a later decision.
   - **Sticky fuse:** on its first fighter contact the projectile attaches to that
     fighter, follows their body centre, deals no contact damage and explodes through the
     existing `Explosion` path when its fuse ends. If the carrier is knocked out or
     benched it explodes where it is. This also serves the "Round Bomb sticks" upgrade
     idea in [game modes](../design/game-modes.md#upgrade-rounds).
   - **Expanding ring:** `HitboxShape.Ring` has a centre, a radius that grows each tick,
     a band half-width and half-height. Contact sweeps the band from last tick's radius
     to this tick's, so a fast ring cannot skip a fighter, and hits each fighter once.
     The first version is flat-floor only: the ring keeps its spawn height, which the
     flat deck satisfies.
   - **Lane motion, later (WEAVE!):** position becomes a pure function of age along an
     authored lane (polyline or arc), snapshotted as lane and age, instead of velocity
     steering. Spin is presentation.
   - Bombs fly from a **host origin** in the stage's Arcade section on analytic arcs
     (`CombatMath.ComputeProjectileLaunch`), so each lands on its telegraphed point on its
     telegraphed tick.

5. **Runtime breakable solids.** `RuntimeSolidSet` is a deterministic, snapshotted list
   of oriented boxes: centre, half extents, yaw, hits left and phase. The director fills
   it; it exists only in formats that use it.
   - **Blocking:** after movement, each active fighter's capsule is swept from its
     previous to its new position against every solid expanded by the capsule radius. A
     fighter stops at the face and slides along it; movement and knockback velocity into
     the face are removed. Solids are walls, not floors: they give no support, and a
     fighter above a wall's top passes over it.
   - **Hitting:** each solid adds a resolver hurtbox (a capsule along its length) in its
     own identity range. Hits with damage count down its hits left, then break it;
     attackers get normal hitstop. Host hazards in flight ignore solids; host explosions
     break the solids they touch.
   - **ADR-0037 still holds.** That record rejected a general world-object framework, not
     focused objects. Solids are a focused Arcade object like the ball and the targets:
     one fixed shape and fixed behaviour (appear, block, take hits, break, expire), their
     own identity range, entering the existing collision and hit paths. There is no
     scripting, no per-object behaviour type, no moving geometry, and no change to baked
     arena collision or the movement integrator's terrain sweep. Moving platforms remain a
     separate scope.

6. **Friendly fire becomes a three-value Match rule:** Off, Shoves, On. It replaces the
   start contract's boolean. Co-op Slopball admits Off and On, Mayhem all three (default
   Shoves), other formats Off. Off uses the existing attack target filter. Shoves are
   resolved in `ResolveHits`: a fighter-on-fighter hit, grab or throw keeps its
   Knockback, Hitstun and Hitstop at the victim's current percent but adds no damage, and
   its launch is computed as for a zero-damage hit. Master keeps a Mayhem preference
   (default Shoves), leader-only and fixed once the Match starts, projected only into
   Mayhem starts.

7. **Version bumps, one coordinated release.**
   - `SimulationProtocol.Version`: the start contract gains the format and the
     friendly-fire mode. Master, GameHosts and clients ship together; Room entry refuses
     older clients.
   - `ServerSimulation.SnapshotFormatVersion`: director state, scores, bench flags,
     solids and the new hitbox fields.
   - `WorldFramePacket.FormatVersion`: Mayhem events (call, wind-up, launch, slam, wall
     hit and break, bench, score, end) ride the frame and a `RollbackLedger`, like
     course events.
   - `MatchResultPacket`: a Mayhem marker with scores, rounds survived and bonuses.
     Master still records only the winner.
   - Later rounds that add state bump the snapshot and the protocol again, as the Online
     Arcade slices did.

8. **The stage gains a Mayhem Arcade section.** `ArcadeStageDefinition` gets a third
   kind, `MankiMayhemDefinition`: the host origin, the deck's play area for placements
   and the deck top. A section has exactly one of ball, course or Mayhem. Section
   version 7 adds a kind byte; existing stages keep writing their current versions byte
   for byte. Mayhem admits any fighter the Match Content Catalog admits, so its
   allowed-package list may be empty for this kind only. The stage's purpose becomes
   Arcade, which removes it from stock PvP selection and admission as ADR-0037 intends.
   `roster.json` lists it under `arcade`, and Master admits `manki-mayhem:manki_mayhem`.

9. **Presentation boundary.** The giant host, call cards, CRT screens, telegraphs, the
   booth bench and the show camera are Unity presentation driven by Shared state and
   Mayhem events; none of them decides gameplay. The host uses the installed Manki
   package's model and clips as presentation only, so the Match Content Catalog does not
   pin it. `PvPMatch` hosts the Mayhem presentation (ADR-0040's revision); the
   presentation takes explicit dependencies so `TrainingMatch` can host it for Local
   Play with CPUs.

## Consequences

- Hazards are ordinary hits: Shield, air-dodge and respawn invincibility, DI and Hitstop
  apply unless a later decision makes a hazard unblockable.
- Mayhem's state is small next to four fighters: one cursor, four scores, at most a few
  dozen hazards and sixteen walls. Measure it against ADR-0039's budget anyway.
- The restore-equivalence fuzz and the GameHost/predictor determinism tests must cover a
  Mayhem arena with every round type, including benching and unbenching mid-round.
- Each new card is Shared work plus presentation; cards that need new state bump the
  protocol.
- Solids answer walls and later crates. Standing on top of a solid needs the dynamic
  triangle alternative below; it is not part of this decision.
- On acceptance, `CONTEXT.md` gains Manki Mayhem, Call Card, Host Entity, Hazard and
  Runtime Solid.

## Considered alternatives

| Question | Alternative | Why not |
| --- | --- | --- |
| Owner | **Owner 0** | 0 already means "nobody" for targets and homing; cleanup by owner 0 would also catch other ownerless effects; presentation drops it. |
| Owner | **Host as a registered dummy fighter** | Needs a definition, state, input and a hurtbox, and becomes targetable. ADR-0037: objects are not dummy fighters. |
| Director | **GameHost-only director that sends hazards** | Server-only objects arrive late and disagree with predicted hits; rejected for Arcade in ADR-0040. |
| Director | **Precomputed schedule in the snapshot** | Larger snapshots, and aims that depend on fighters cannot be precomputed. |
| Director | **Unity-driven host timeline** | Presentation never decides gameplay timing. |
| Sticky bomb | **Director-owned bomb objects** | Duplicates projectile motion, contact and explosion; the primitive also serves upgrades. |
| Ring | **A ring of spheres** | Sphere count grows with radius, leaves gaps and multiplies hit identities. |
| Solids | **Dynamic triangles in arena collision** | Exact and standable, but touches every mode's movement hot path, the spatial grid and support/ledge code. Kept as the upgrade path if crates need tops. |
| Solids | **Push-out without a sweep** | Strong launches tunnel through thin walls in one tick. |
| Solids | **Solids as dummy fighters** | ADR-0037. |
| Friendly fire | **A separate Mayhem flag** | Two fields would mean the same thing; the tri-state also covers co-op. |
| Hosting | **A dedicated Mayhem scene** | ADR-0040's revision chose one online lifecycle in `PvPMatch`. |

Binary layouts, class names beyond those above and every tuning value remain
implementation work; see the [implementation plan](../design/manki-mayhem-implementation.md).
