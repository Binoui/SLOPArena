# ADR-0062: Stage hazards: one switch, typed hazard families and baked anchors

**Status:** Accepted 2026-11-04. The maintainer picked the hazards in the
[stage hazards feature brief](../design/stage-hazards.md), asked for this record ("go"),
approved it ("approved") and answered its open questions (see the end). The shape comes from
an architect arena: two runners (hazards as authored data programs, and hazards as typed
Shared code), a Codex cross-judge, and a synthesis. An adversarial review the same day proved
three gaps, fixed in decisions 4 and 10. No tuning value is selected.
**Extends:** [ADR-0037](0037-arcade-shared-objects-and-unified-stage-artifact.md) (one stage
artifact), [ADR-0039](0039-full-world-rollback.md) (World Snapshots),
[ADR-0044](0044-manki-mayhem-hazard-director.md) (a hazard owner that is not a fighter,
`RuntimeSolidSet`) and [ADR-0060](0060-gamehost-replays-world-frames-and-view-track.md)
(Replays).
**Sibling of:** [ADR-0061](0061-manki-mayhem-channels.md). Mayhem keeps its own director.

## Context

The maintainer wants livelier, more chaotic stages behind one Off/On switch per match. The
picks are a kraken around Splash Deck, Train of the Day on Last Stop, Bass Drop on After Hours
and a crowd that throws things at the leader on Slop Pit. Off must play today's accepted
stages exactly. The [stage concepts guide](../design/stage-concepts.md#current-boundaries)
blocks hazards until Shared and the server own them.

The code has most of the pieces, but not in a usable shape:

- **Stages are static.** Collision is the baked arena. Nothing in Shared moves collision, and
  ADR-0044 rejected dynamic triangles for touching every mode's movement path.
- **Mayhem is welded to its rule and is being rewritten.** The ADR-0061 rewrite, uncommitted in
  the shared checkout, deletes `MankiMayhemSimulation.cs`, `MankiMayhemRounds.cs` and
  `MayhemThreats.cs`. In the rewrite a Mayhem host hit splats instead of launching, and the
  resolver lets the Mayhem host ignore invincibility (`SpellResolver.cs:897`).
- **An Arcade section takes a stage out of PvP** (`ArenaDefinition.Purpose`, line 131), so
  hazard data cannot ride in one.
- **Hits from a non-fighter owner already work as attacks.** `ResolveHits` takes the knockback
  direction from the hitbox, keeps Shield, Hitstun and Hitstop, and never overwrites last-hit
  credit. Two limits apply. A counter only catches hits from an existing attacker
  (`ServerSimulation.cs:2502`), and a knockout credits the last hitter only inside
  `KillCreditWindowTicks` (`ServerSimulation.cs:2886`).
- **Nothing shoves without hitting.** Rogue Wave needs a new effect.
- **Free projectiles move only their start point** (`SpellResolver.cs:851`). A train body is a
  capsule whose two ends must move together.
- **Stage motion runs on the wall clock** (`_Time.y`, legacy `Animation` clips). Hazard
  warnings must follow the simulation tick, or rollback, replay seeking and spectating drift.
- **The Prediction Window is `NetplayPredictor.MaxPredictionTicks`.**

## Decision

1. **The switch is on the rule.** `StockMatchRule` gains `StageHazards` (Off or On). Training
   gets `NoWinMatchRule.ForPractice(mode)`, and `Instance` stays the plain Off rule.
   `MatchFormat.CreateRule` refuses On for every format but Stock. Every simulation
   construction site already receives the rule, so nothing else carries the switch. Local Brawl
   defaults to On. Online, the flag follows the `BallOnly` path from the Room to the GameHost,
   clients and `ReplayRules`, plus the Master Room setting.

2. **Off is the absence of the hazard session.** With the switch Off, or on a stage with no
   hazard family, `ServerSimulation` holds no session, runs no hazard hook and writes no hazard
   bytes. There is no presence byte. The guarantee is identical play. Seeded CPU fights on the
   four stages produce the same per-tick snapshot bodies (after the version word) as a baseline
   captured on main once ADR-0061 lands. A hazard stage's arena digest changes once, when its
   anchors are baked, and the change is the same for Off and On.

3. **One typed module per hazard family.** `KrakenHazard`, `TrainOfTheDay`, `BassDrop` and
   `CrowdThrows` are internal Shared classes behind one `IStageHazardFamily` interface, listed
   in one registry table keyed by stage. A module owns its schedule, move choice, route, aim and
   tuning. It plans an immutable action, and the action yields `StageAttack` values that carry
   an ordinary `Hitbox`. Families therefore use the resolver's full vocabulary (capsules,
   spheres, rings, projectiles, explosions) without new engine primitives. Hazards are not a
   data language.

4. **Geometry lives in baked anchors.** Anchors are markers in the stage's source scene under
   `AuthoringAids/HazardAnchors`, which the bake turns into an optional `StageHazardAnchors`
   arena section, as it does for spawn markers. The section holds named points, capsules, boxes,
   cell lists and loops. A stage with a `layout.py` generates its markers from the same `SPEC`
   that builds its shell, through shell-apply. Last Stop has no `layout.py`; its shell is six
   collision meshes in its source scene, so its markers sit in that scene beside them and its
   collision does not change. The bake validates every anchor: finite, inside the blast lines
   and above the KO plane. Throw origins are the one exception, because Slop Pit's stands start
   19 m outside the blast lines. The simulation spawns a thrown hazard where its ballistic arc
   from the origin enters the blast corridor, with that arc's velocity, so the path that hits is
   the same arc. The presenter draws the part outside the corridor from the same arc. When the
   switch is On, binding a family checks that every anchor it needs exists with the right kind,
   and fails the match start loudly otherwise. The section leaves `Purpose` at PvP. A stage's
   hazard lanes live in the same authoring source as its shell.

5. **One session, one action at a time.** `StageHazardSession` owns the seed, the origin tick,
   the action ordinal, the next lock tick and at most one active action. It runs once per tick,
   after `_tick++` and before `PreTickAbilities`. Timing and move choice never depend on fighter
   state; only aim does. Every effect of an action, including projectile tails, ends by the
   action's retire tick, and `RemoveOwnerEffects` clears the owner before the next action.

6. **A hidden lock, then the warning.** An aimed action freezes its target at a lock tick that
   no view exposes. Its warning opens more than the Prediction Window later and lasts longer
   than the Prediction Window. No client can show an aim that is later corrected. Unaimed
   warnings also outlast the Prediction Window.

7. **The kraken's route causes the attack.** The route is a pure function of the seed and the
   absolute tick over anchor waypoints. Its pose at the lock tick decides the side and the
   eligible moves: a slam from a side, the wave from the bow, the geyser from under the hatch.
   This holds from the first slice, before the lurk is visible.

8. **Hazard hits are attacks.** A reserved stage-hazard owner ID sits next to the Mayhem host
   but never equals it, so respawn and dodge invincibility apply. Hits take the ordinary
   absent-attacker path: Shield blocks unless an attack is `Unblockable`, and launch, Hitstun
   and Hitstop apply. Counters do not catch hazard hits. A hazard knockout credits the last
   fighter who hit the victim, inside the existing kill-credit window.

9. **The shove is movement, not a hit.** Rogue Wave adds a horizontal displacement to the
   ordinary movement sweep. It deals no damage, no Hitstun and no credit, and the Off movement
   path gains no arithmetic.

10. **Rollback, frames and Replays.** The World Snapshot stores the session state and the
    active action with its frozen fields. Restore validates, then applies atomically, and never
    runs effects. World Frames gain no hazard event list. Hazard edges are pure in the seed and
    the tick, hits ride the existing hit records, and host explosions ride the existing
    presentation events. `NetplayPredictor.ValidPresentationOwner` must admit the stage-hazard
    owner when the world has a hazard session. Today it accepts only fighters and the Mayhem
    host, and a probe showed that each hazard hit or blast makes every client reject the frame,
    while a steady steam vent left clients stalled for 33 ticks. Presenters key one-shots on
    (ordinal, operation, edge) and fire every edge crossed since the last presented tick,
    because an online client can skip ticks. An online client
    session takes its seed from the first authoritative snapshot and never draws one. Replays
    add `ReplayRules.StageHazards` as an additive tag that defaults to Off. The View Track
    records a closed `stage-hazards` mode object with `SourceKind "stage-hazard"`. View
    playback shows the recorded cues without binding the current family code.

11. **Presentation reads the view, per family.** Unity reads `StageHazardView`: the tick, cues
    with danger geometry taken from the same `Hitbox` values that hit, and ambient poses such as
    the kraken's position. It never sees a pending action. Each family gets its own On-only
    presentation root, so each joke can be sold. The accepted stage prefabs stay untouched. The
    stage owner is registered for projectile visuals and presentation. Splash Deck's sailing is
    an On-only background root driven by the sim tick. The deck, camera and fighters do not move.

12. **CPUs see what players see.** `BotMemory` receives the view's visible dangers after the
    difficulty's reaction delay. The policy steps out or jumps over, using `StageWalk` to check
    the footing. CPUs never read the pending action or the future schedule.

13. **Mayhem stays a sibling.** No shared director, and nothing is extracted while the rewrite
    is live. Stage hazards land after the ADR-0061 rewrite and touch no Mayhem file.

14. **No collision changes in this feature.** No floor moves or breaks. The later tentacle wall
    needs `RuntimeSolidSet` generalized first, because its push-out picks a side from
    `CageCenter`. Passing trains would need a translating capsule in `SpellResolver`; the
    chosen All Aboard! hazard does not.

## Delivery

1. Core, offline: the switch on Local Brawl and Training, the anchor section and its bake, the
   session, Kraken Slam chosen by the route, CPU escape, an On-only Slam presentation and the
   Off parity test.
2. The visible lurk and the sailing backdrop, judged on gameplay-camera captures.
3. Train of the Day with All Aboard!, Bass Drop (the drop lands on the beat) and The Crowd
   Has Opinions.
4. The shove, Rogue Wave and Catch of the Day.
5. Online Rooms, Master and Replays, in one coordinated release with the protocol bump.

## Consequences

- The stage concepts guide allows hazards through this record. Moving geometry stays blocked.
- The four stage records gain a hazards section. Their accepted shells do not change.
- `SnapshotFormatVersion` changes once for the On layout. `WorldFramePacket` keeps its format;
  `NetplayPredictor` admits the stage-hazard owner. The online slice ships a NetplayLab test
  in which hazard hits, blasts and a steady stream cause no rejected frames and no stalls.
  Simulation changes go under Unreleased in `SimulationProtocol.cs`.
- Adding a hazard to another stage takes one family module, one registry entry, anchor markers
  in its source scene (generated by its `layout.py` where it has one) and an On-only art root. CPU dangers and the View Track come from the view.
- Hazard knockouts follow today's credit rules, so knocking someone into the train counts if
  it happens inside the window.

## Considered alternatives

- **Hazards as authored data programs** (arena runner 1): a closed set of strike kinds (lane,
  pop, throw, shove, wall) composed in the arena section, run by one generic runner. It needs no
  C# per stage. It lost because each new strike shape, such as Log Off's ring wave, would need
  engine, codec, view and presenter work, and a generic presenter may not sell each joke. This
  record keeps three of its ideas: anchors from `layout.py`, Off as an absent session, and
  hazard edges projected from state.
- **Hazard geometry in C#, bound to the arena digest** (arena runner 2): no rebake and an
  unchanged digest. It lost because the code copies geometry that `layout.py` owns, and every
  shell edit would break On until someone fixes the anchors by hand.
- **Byte-identical Off snapshots through two snapshot layouts** (arena runner 2): it lost
  because the requirement is identical play, and two layouts would complicate the World
  Snapshot, restore and Replay identity for good.
- **A hazard event list in World Frames with a rollback ledger**, as Mayhem uses: not needed
  while hazard timing cannot be mispredicted.
- **One director shared with Mayhem:** Mayhem hits splat and score while stage hazards are
  attacks, so a shared director would need outcome switches. The rewrite is also live.
- **Moving collision** (the parked train departing, a rocking deck): rejected, as in ADR-0044.
- **Hazards as a Match Format:** formats would multiply, and Training is not a format.

## Maintainer answers (2026-11-04)

- **Shield blocks hazards.** A hazard is blockable unless its family marks an attack
  `Unblockable`.
- **The crowd's leader** is the fighter with the most stocks left. A tie goes to the higher
  percent, so the crowd goes after the more damaged leader (confirmed). Ties that remain break
  by entity ID.
- **Hazards stop once the match is decided.** No new action opens after the outcome.
- **Last Stop's Train of the Day** is a different parked train each match, all sharing today's
  shape, plus the All Aboard! hazard from that train ("lets go with your plan"). No second
  track, no collision change, no translating capsule.
- **Still open:** the open-sea backdrop for the sailing.
