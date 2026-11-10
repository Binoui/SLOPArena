# ADR-0061: Manki Mayhem channels, verdicts and the control gate

**Status:** Accepted 2026-11-04. The maintainer approved the redesign in the
[feature brief](../design/manki-mayhem.md) ("this sounds really good. lets go for it"). The
shape below comes from an architect arena: two runners (one class per channel, and channels as
data recipes), a Codex cross-judge, and a synthesis. Tuning values are provisional.
**Amended by:** [ADR-0063](0063-manki-mayhem-variety.md) (2026-11-05): decision 3's per-act draw and decision 10's
fixed PRIME TIME! pair.
**Supersedes:** decision 3 of [ADR-0044](0044-manki-mayhem-hazard-director.md) (the round
cursor, benching and survival scoring), its "hazards are ordinary hits" consequence, and the
Mayhem part of its decision 6 (friendly fire On).
**Keeps from ADR-0044:** the `manki-mayhem` format, host entity `1 << 48`, the sticky fuse and
expanding ring primitives, `RuntimeSolidSet`, the stage's Mayhem Arcade section, and the
presentation boundary.

## Context

The 2026-10-27 show is a survival mode. Three cards repeat for eighteen rounds, hazards launch
with knockback, a knockout benches the fighter, and a round ends only when every survivor has
landed (`MankiMayhemSimulation.FinishTick`). Scoring is survival points, and `MatchResultPacket`
enforces that equation. Nothing judges a per-player objective, and no code restricts controls
per mode or per round.

The redesign needs short channels on a fixed clock, a pass or fail for every contestant, ratings
over three acts, a splat instead of a launch, and at most one extra move per channel enforced in
Shared. It must stay deterministic and rollback-safe for online play. The channel list mixes
variations (SPOT!, SUMO!, SMILE! are zones) with one-off rules (HOT POTATO!'s hand-off,
QUICK DRAW!'s timing, GET HIM!'s hit quota).

## Decision

1. **One class per channel.** A channel is a sealed internal class behind one interface,
   `IMayhemChannel`. It ticks at two moments (before actions, after combat), answers contacts,
   judges one contestant (`Ready`, `Incomplete`, or `FailNow`), returns CPU intent, projects its
   telegraphs as closed cue records (bombs, rings, zones, solids, targets), and writes its own
   state. Construction from a setup and restore from a snapshot live in its registry entry.
   Channels are trusted Shared code, never content or plug-ins (ADR-0037).

2. **One registry.** `MayhemChannels` lists every channel once: id, copy key, extra verb, role
   (regular or boss), timing, factory and restore factory. Decks, prompts and codec lookup derive
   from it. Switch expressions over Mayhem enums have no discard arm, and `.editorconfig` makes
   CS8509 an error for `src/Shared/Mayhem/`. A test fails when a concrete `IMayhemChannel` is not
   registered exactly once.

3. **The show plan is a function of the seed.** `MayhemShowPlan.For(seed).At(tick)` returns the
   cursor: episode, act, speed tier, phase and deadlines. Three acts each draw regular channels
   without repeats (seeded Fisher-Yates; act size provisional: four) and end with the boss.
   Phases are Intro, Call, Play, Verdict, SpeedUp and Ended. Every length comes from the
   registry entry and the tier, so the World Snapshot stores the seed, not the cursor.

4. **Tick rules.** During Call, contestants stand frozen at their starts and the telegraphs
   show. Play starts inclusive and its deadline is exclusive. At the deadline the director
   judges the state after the last Play tick. Verdict, SpeedUp and Ended run no gameplay. A
   channel never waits for landings, live effects or survivors.

5. **One verdict writer.** The director's verdict book is the only writer. A failure latches on
   the tick it happens. `Ready` stays provisional until the deadline, so a fighter who broke
   their boards can still be splatted before the buzzer, and PRIME TIME! needs both objectives.
   The stored outcome is one pass bit per episode per contestant, plus the current episode's
   failure (cause, splat style, anchor pose). Ratings are one fold over the pass bits
   (provisional: regular one, boss two). `MayhemRunResult` and `MatchResultPacket` carry the pass
   masks and recompute ratings.

6. **The control gate.** `MayhemControls` is `Frozen` or `Playing(ExtraVerb)`, where
   `ExtraVerb` is `None`, `Shield`, `Grab`, `Specials` or `Beeline`. Playing always admits move,
   walk, jumps, normals 1 to 3, and aim and facing. Down actions, lock toggle and retarget are
   always off. Shared enforces the gate in four places: the input filter before
   `PreTickAbilities`, buffered slots, the actual ability activation, and admission in
   `Simulation.SimulateTick`. Every Call also resets each contestant at its start with no
   ability, buffer, projectile, interaction or percent, so nothing crosses a gate change. The HUD
   prompt and the CPU read the same gate.

7. **Splat.** Host contacts route to the director before armor, invincibility, damage, hitstop
   and knockback. A hazard never launches or damages. A DODGE! bomb is an impact bomb: touching
   it or landing near it explodes it, and the blast's host hit fails everyone it reaches, one
   tick after contact. Channels schedule their last impact so the blast ends before the
   deadline. Falling off the deck fails the channel and is consumed before ordinary death
   processing, so a contestant never respawns, loses a stock or gains a death. A failed
   contestant is inactive (no input, movement, hurtbox or pushbox) and pinned to its anchor pose
   until the next Call. Damage percent never rises in Mayhem.

8. **Friendly fire is Off or Shoves.** Mayhem refuses On. A shove is a bounded horizontal push
   at zero damage with a short stun. It never fails anyone by itself. NO RUNNING! ignores
   displacement while a fighter is in hitstun and for a short slide after it, because the
   engine turns leftover push into run speed when hitstun ends.

9. **Presentation contract.** Unity reads one `MayhemShowView` (cursor, channel, prompt and gate,
   cues, contestants with verdicts and ratings) and one event stream. Event keys are (episode,
   lane, source operation, kind, subject), with the attacker and activation in the operation for
   repeated hits on one wall or target. Host hitboxes pack (episode, lane, ordinal) into
   `ActivationId` and `VisualOperationIndex`. Shared pure helpers for seeded sampling, schedules
   and geometry serve channels, CPUs and cue projection alike.

10. **PRIME TIME!** is one explicit composite boss: a BREAK OUT! child in lane 0 and a DODGE!
    child in lane 1, both at the top tier, one deadline and one verdict per contestant.

11. **CPUs.** Each channel's `Cpu` returns intent (destination plus Wait, Brake, Move, Jump,
    Normal or the extra verb). One adapter turns intent into `InputState`, applies the difficulty
    reaction delay and runs the result through the gate. It replaces `MayhemThreats`. CPUs also
    slip: one seeded roll per episode and seat makes a channel-specific mistake (provisional
    chances: Easy 45%, Normal 25%, Hard 10%), so a newcomer can beat them.

12. **Versions.** Snapshot and World Frame formats bump with the new layouts. The protocol change
    is recorded under Unreleased in `SimulationProtocol.cs` and bumps once at the release.
    Replays project the view and decode the new events.

## As built (2026-11-04)

The first slice follows this record with these differences, each forced by a test or a trace:

- Event keys also carry the match tick, because the rollback ledger orders and expires by tick.
- DODGE! bombs drop steeply from above their ring instead of flying from the booth. From the booth
  the final approach was flat and clipped heads short of the ring. Unity draws the throw from
  Manki's hand.
- A channel ticks from Call through Verdict, so cages rise and wind-ups play while contestants are
  frozen. Hazards launch only in Play.
- Cage walls and boards count one hit per activation, so a multi-hitbox normal is no faster.
- The cracked wall faces away from the other cages. Facing the centre sent every escape under
  PRIME TIME!'s bombs.
- SPOT! gives each spotlight one holder: the contestant nearest its centre, then the lower seat.
- A splat anchors on the deck under the contestant, never in the air.
- `Simulation.SimulateTick` takes a nullable gate, and null means unrestricted for every
  non-Mayhem caller.

## Consequences

- Deleted: `MankiMayhemRounds`, the card enum and deck passes, Settle, benching by knockout,
  survival and last-standing scoring, damage carry-over, fighter projectiles that survive a round,
  `MayhemThreats`, Unity's card copy switches and its survival texts.
- Each channel writes its own snapshot state, so a missed field desyncs only under rollback. One
  parameterized test runs every registered channel through a seeded episode at each tier and
  checks that restoring at every tick equals the continuous run.
- A one-off rule is one new class plus one registry line. A variation reuses the shared helpers
  and cue records, so Unity and CPUs need no per-channel code for it.
- The Unity presentation, the Local Play entry and online Rooms break until they move to the view
  and events. Master drops the Mayhem On setting when online returns.
- `CONTEXT.md` gains Channel, Episode, Verdict, Splat and Control Gate once the code exists.

## Considered alternatives

| Alternative | Why not |
| --- | --- |
| **Channels as data recipes** from a closed set of parts with one interpreter (arena candidate 1) | Cheapest for variations, and its snapshot is the smallest. One-off rules spread across a part struct, the interpreter, a CPU advisor and a presenter, and early passes granted hazard immunity inside PRIME TIME!. Its seed-derived timeline, pass masks and pure helpers were kept. |
| **Recipes as stage content** | ADR-0037 forbids per-object behaviour from content, and it would need a versioned parser. |
| **Timed operation lists** | A scripting language in disguise, and CPUs could not read intent from them. |
| **Show editions and a Roslyn analyzer** (from candidate 2) | One show definition and a registry test cover the first slice. Protocol versions already gate old clients. |
| **Ready latches pass immediately** | Simpler, but PRIME TIME! could be passed on one objective. A provisional Ready keeps the stamp immediate while the verdict waits for the buzzer. |
