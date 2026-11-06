# ADR-0063: Manki Mayhem variety: airings, twists, remixes and the finale

**Status:** Accepted 2026-11-05 ("go for it"). The maintainer picked the content on 2026-11-05: seven new
regular channels, PRIME TIME! remixes plus a GET HIM! boss, and seeded twists for every channel.
The rules are in the [feature brief](../design/manki-mayhem.md#variety-pass). The shape below
comes from an architect arena: two runners (generic channel roles with one CPU planner, and
explicit pair rows with twist data), a Codex cross-judge that scored them 15 and 23, and a
synthesis on the second. Every length, count, radius and speed stays provisional.
**Amends:** [ADR-0061](0061-manki-mayhem-channels.md) decision 3 (the show plan draws per act and
every boss is PRIME TIME!) and decision 10 (PRIME TIME! is BREAK OUT! plus DODGE!).
**Keeps:** every other ADR-0061 decision, and ADR-0044's host identity: the host entity
`1 << 48` stays presentation only and never becomes hittable.

## Context

The show repeats itself. Six regular channels fill twelve regular slots, so each airs about twice
(`MayhemChannels.cs`, `MayhemShowPlan.cs`). PRIME TIME! closes all three acts. A channel varies only
in positions and speed from one airing to the next.

Five facts in today's code shape the change:

- The registry row owns the extra verb, the timing and the copy key. The director reads them by
  channel, so nothing can change per airing (`MayhemShow.cs:106`, `MayhemShowPlan.cs:68`).
- `ChannelRestore` carries no seed and no tier. A twist drawn inside a channel could not be
  re-derived after a rollback, and the plan, the call card and the view could not see it.
- PRIME TIME! is hand-written glue. Every wall hit goes to BREAK OUT!, and its CPU calls methods
  that only BREAK OUT! and DODGE! have (`PrimeTimeChannel.cs:37-58`).
- Host contacts route before the shield check, so shielding does not stop a host bomb. Fighter
  shoves never reach a channel. Nothing can hit the host.
- Unity's call-card copy is a hard-coded English table with no validation. A missing key throws
  at the call (`MankiMayhemView.cs:23-34`).

## Decision

1. **The plan computes an airing per episode.** `MayhemAiring` is either a `SoloAiring` with one
   `ChannelDraw` or a `RemixAiring` with a pair id, a goal draw and a hazard draw. A `ChannelDraw`
   is the channel id, its twist id and the channel seed. The airing also carries the resolved extra
   verb and timing. The director, the gate, the CPU adapter, `MayhemShowView` and replay all read
   the airing. A restore rebuilds it from the show seed, so the World Snapshot still stores only
   the seed. `ChannelSetup` and `ChannelRestore` gain the draw and the tier.

2. **Twists are registry rows, and channels own their behavior.** Each `ChannelEntry` lists its
   twists. A row has a permanent local `TwistId`, a copy key `mayhem.twist.<channel>.<slug>` and an
   optional timing or extra-verb override. Each channel maps the id to its own twist enum and
   switches on it with no discard arm (CS8509 is an error under `src/Shared/Mayhem/`). Registry
   load fails unless every channel's enum and rows match one to one. Adding a twist touches the
   channel file, its registry row and the Unity copy table.

3. **One deal fills the show.** One seeded shuffle of every regular channel deals the twelve
   regular slots in order, four per act. With thirteen regulars, nothing repeats and one channel
   sits out. While fewer than twelve exist, the deal reshuffles and never repeats a channel back to
   back. Acts one and two close with PRIME TIME! remixes drawn from a shuffled pair deck, never the
   same pair twice. Act three closes with GET HIM!. Every draw uses its own seed stream, and the
   plan never reads the roster or withdrawals.

4. **PRIME TIME! stays one class over explicit pair rows.** The goal child runs in lane 0 and the
   hazard child in lane 1. Each allowed pair is one `PrimePairEntry` row: goal, hazard, timing, the
   hazard's lead-in and a named CPU policy function.
   - Host body and bomb-impact contacts route by source lane.
   - An object hit routes to the child whose scene owns the entity. At most one child in a pair
     produces solids or targets, and a registry test enforces that.
   - A host blast reaches its source child and the owner of any wall it strikes.
   - Either child's `FailNow` wins, with the goal first on a tie. Either `Incomplete` holds the
     verdict. `Ready` needs both.
   - The extra verb folds as None plus X gives X. A row whose children need two different verbs
     fails registry load.
   - Each pair's CPU policy keeps the goal's destination while a shared `SafeStep` helper steps
     around the hazard's published dangers. One slip roll per seat per airing reaches whichever child
     the CPU is working on, so a slip costs the CPU as much as it did in the old PRIME TIME!.
   - Children start from their own layouts. A pair gets a coupled layout only when certification
     shows it needs one.
   - A pair joins the allowed list only when careful CPUs pass every goal twist with every hazard
     twist in seeded runs, with no hazard failures, and the goal's contest holds. The maintainer
     judges readability in play.

5. **New engine facts stay small and read-only.**
   - `TargetCue` gains an active window, and `MayhemCollisionTargets` skips inactive targets. BONK!
     heads and GET HIM!'s body use it. Board stacks get the whole episode, so they act as today.
   - QUICK DRAW! reads swings from `AttackSequence`, captured at `BeginTick` and compared at
     `EndTick`. The counter increments before an ability's early return (`ServerSimulation.cs:363`),
     and the gate admits only normals in that channel. The director recaptures it every tick. The
     channel latches the first swing per seat into its own snapshot.
   - HOT POTATO! passes the bomb on a bump or a shove. A bump is a channel-side test of capsule
     overlap on frame positions, so it works under both friendly-fire settings. A shove arrives as a
     new `MayhemContactKind.FighterHit` sent from the shove branch, so it exists only under Shoves.
     Under Off, attacks pass through fighters, so only bumps pass the bomb. The channel resolves
     passes once per tick from the holders at the start of the tick. A new touch passes the bomb,
     not continued overlap, so the channel snapshots the previous tick's touches. The last giver
     cannot take it straight back.
   - BLOCK! judges `ActionState.Shielding` at its own scheduled blast ticks. Its barrage bombs are
     cues with no host hitbox, so no bomb explodes early on the first fighter it touches. The rings
     in its Block or jump twist stay real unblockable host rings.
   - GLITCH!, SUMO! and SMILE! judge positions and `IsGrounded` against analytic zone cues.
   - GET HIM!'s dizzy Manki is one `TargetCue` of kind `DizzyHost` in the existing target id range,
     open to every seat. The channel counts each seat's hits in a bounded set of accepted activation
     ids. A high-water mark would drop an older activation that lands after a newer one.
   - `RuntimeSolidSet` grows from 16 to 32 walls for the Double cage twist.

6. **Cue records stay closed and grow kinds.** `ZoneCue` becomes analytic: start and end centre and
   radius, an armed tick, and one `CenterAt`, `RadiusAt` and `Contains` that judgement, CPUs and Unity
   share. Its kinds are Spotlight, SumoRing, SelfieFrame (a rectangle) and GlitchPanel. `TargetCue`
   gains a window and the kinds CardboardHead, DecoyHead and DizzyHost. `WatchCue` gains a kind
   (Look, FakeLook, Draw, FakeDraw). `BombCue` gains a kind (Aimed, Barrage). `CarryCue` is new, for
   the potato. `MayhemEnumLimits` names each Mayhem enum's last value once. Every range check reads
   it, and a test pins it.

7. **Existing CPUs keep their code.** Before any CPU changes, a test records each channel's pass
   rate per difficulty on fixed seeds and roster. Later changes must stay within five points. The
   CPU intent gains a press token so a Tall wave's second jump presses again.

8. **Tests follow the new show.** The show-plan test asserts the new deal and bosses. The roster
   pass test reads a per-row contest (`Shared` or `Scarce`) instead of special-casing SPOT!. The
   codec test that needs every event kind runs a fixed set of airings instead of one seed. The
   restore-at-every-tick test covers every twist and every allowed pair.

9. **Versions.** The World Snapshot and World Frame formats bump. The protocol change goes under
   Unreleased in `SimulationProtocol.cs` and bumps once at release.

## As built (units 1 to 3, 2026-11-05)

Two adversarial reviews (Codex and Opus) ran against units 1 to 3. These differences came from them or from a test:

- Twist cycling is one cycle per channel per show that counts solo and remix airings in episode order. A remix that
  would rerun an earlier remix's pair and both twists steps its hazard twist on.
- The classic pair's DODGE! lead-in grows with BREAK OUT! Double cage's longer play time. CPUs need up to 227 ticks to
  break both rings, and the first blast used to land at 186.
- `SafeStep` keeps a way out while that point stays safe. For the classic it also reads the cage bombs from the call, so
  Unity shows those landings too.
- Bomb rain inside a remix closes its rows up, so it still shows several rows with moving gaps.
- Salamander cannot pass a Double cage remix: its first normal takes 322 of the 364 ticks just to leave a solo Double
  cage. A waiver in the certification names that case and fails the day it is no longer needed.
- A 40-seed sweep over 2,160 careful airings finds 2 hazard failures, both Wibou between two Aimed bombs in a three-seat
  Double cage. Fixing them needs an escape planner that looks ahead over time, which a rare CPU miss does not justify.
  A test pins both cases and fails when they stop failing.

### Units 4 and 5

SUMO!, SMILE!, GLITCH! and BONK! now join the regular registry with nine twists. Unity draws their analytic cues,
call copy, guest portraits and failure stamps. Live presentation acceptance is pending the next Editor session.

- `ZoneCue` has independent `HalfDepth` and `EndHalfDepth` for rectangular X/Z geometry. SUMO! alone eases radius
  with `t * (2 - t)`. Its provisional final radius is 0.45 m. It leaves room for one offset fighter but does not
  guarantee exactly one survivor under Off. Its base Play is 420 ticks.
- `TargetCue.OwnerSeat` is nullable for shared heads. Replay OwnerId zero represents that absence. Heads use inclusive
  ActiveFrom and exclusive ActiveUntil windows. Reused collision IDs get new presentation ordinals each wave.
  Board collision remains available for the whole episode even when HitsLeft is zero.
- BONK! now has two waves over 480 base Play ticks and a Scarce contest. The shorter window cannot promise every
  careful contestant a head. GLITCH! has three rounds over 480 ticks, decreasing warnings of at least 60 ticks at
  every tier, and 12-tick armed windows. All values remain provisional.
- SUMO! + JUMP!, SMILE! + DODGE! and BONK! + GLITCH! failed careful CPU certification. Ring and wave hazards could
  leave no SUMO! survivor. SMILE! contestants hit bombs or missed the flash. BONK! attack approaches crossed armed
  panels or missed heads. None was admitted. The allowed list remains BREAK OUT! + DODGE! and SPOT! + JUMP!.
- Existing pair certification now pins the unit 3 child draws, seeds and episode indices. Expanding the deck exposed
  an additional all-Wibou Any wall + Bomb rain miss. The original pair CPU policy is unchanged. The SUMO! pace
  adjustments were not recertified in a remix.

### Units 6 to 9 and the second review

GET HIM!, QUICK DRAW!, HOT POTATO! and BLOCK! are built in Shared and drawn in Unity.
The show deals twelve distinct regulars from thirteen, two different PRIME TIME! pairs, and GET HIM! as the finale.
The maintainer's Local Play acceptance is pending. Values below remain provisional.

- GET HIM! publishes one shared DizzyHost proxy until its wake. Each seat needs two unique normal activations.
  The resolver admits one hit per attacker on shared targets, while ordinary fighters retain one hit per tick.
  Unity moves the existing big host out of the booth, shows stars until ActiveUntil, and displays each seat's HitProgress.
  The wake launches a real slam ring or five bombs. Quota completion never protects a fighter from those hazards.
- QUICK DRAW! latches accepted AttackSequence changes. Early swings fail immediately as MISFIRE; missing swings fail
  as TOO SLOW. Fake-out publishes separate DRAWER! and DRAWL! keys. Unity exposes a word only within its cue window.
  Duel uses every latched draw to find the slowest, including departed contestants. An all-cast tie cancels nobody.
- HOT POTATO! passes once per fresh capsule touch under Off or Shoves. Under Shoves only the holder's outgoing hit
  passes it. The last giver is barred for 60 ticks; LastGiver then clears. Each potato resolves once from starting holders.
  Two potatoes requires three live contestants. With fewer, an orphan retires first, otherwise the last potato retires.
  An invalid holder transfers to the nearest live nonholder with seat-order ties, except in the final 60-tick bang grace
  or when fewer than two live contestants remain. Then it retires. Unity draws a small fizzle for early retirement.
  A lone holder still loses at the fuse. The fuse ends six ticks before the deadline.
- BLOCK! samples authoritative Shielding on every barrage impact. Its zero-radius Barrage cues mean the whole deck,
  without resolver blast hitboxes. The shove branch now uses the ordinary shield block path for blockable contacts,
  so a held shield survives shoves. Block or jump adds a real unblockable ring. Unity shows Shield on HUD and CRT cards.
- Startup aim correction tries eligible channel targets before rivals and client soft targets. Heads, the host and
  owned unbroken boards qualify; decoys and rivals' boards do not. Off drops rivals from targeting entirely.
  Ordinary combat behavior retains its existing target order outside Mayhem.
- GLITCH! Sweep arms one, then two, then one columns, seeded left-to-right or right-to-left. Its middle round has
  eight panels. Every column arms once; the nearest gap remains reachable. BONK! Whack uses seeded deck positions
  with a 2 m edge margin, 3 m spacing and reachability checks, with a bounded fallback to its prior ring.
- SMILE! plus JUMP! certified and joins as `mayhem.pair.smile-jump`, with its own PoseOverRings policy.
  BREAK THE BOARDS! plus JUMP! failed attack-and-ring trials and stays out. No failed pair was admitted.
  The existing pair waivers and pinned misses remain. QUICK DRAW! plus DODGE! has no admitted row.
- Replay adds draw word keys, bomb kind, carries and per-seat progress. World Snapshot is 31, World Frame is 11,
  and SimulationProtocol stays 38 until the coordinated release bump.

## Consequences

- A twist is local to its channel. A new regular is one class, one registry row, its looks and
  copy rows and its tests. A new pair is one row and one CPU policy, admitted by evidence.
- PRIME TIME! policies are explicit. A reader sees which child owns which contact and whose
  movement wins without tracing a generic planner. The cost is one small function per pair.
- The restore-at-every-tick test grows from 19 cases to well over 100. If it gets too slow, it
  samples pairs per seed.
- Unity gains a validated copy table, twist lines on the call card, guest portraits from the
  registry row, and drawing for each new cue kind.
- Friendly fire Off changes HOT POTATO!: only bumps pass the bomb.

## Considered alternatives

| Alternative | Why not |
| --- | --- |
| **Generic channel roles with one CPU planner** (arena candidate A): a hazard interface that owns no objects, a composer for every airing, and every CPU ported to a shared planner | Any goal composes with any hazard, with no per-pair code. It ports all six tuned CPUs at once and adds a second channel interface. The judge found concrete defects: a dropped second jump press, a high-water hit count, and both remix children slipping. Its baseline guard, twist completeness check, analytic zones and enum limits were kept. |
| **Twists drawn inside each channel** | No plumbing. The plan could not time a twist, the card could not show it, and a restore could not re-derive it. |
| **The host as a hittable entity** | Breaks ADR-0044, and `RegisterEntity` refuses the id. A target proxy reuses the hit path that walls and boards already use. |
| **HOT POTATO! on the resolver's sticky fuse** | The fuse never re-attaches or passes on. |
| **A resolver observation branch for hits under Off** | It would duplicate hit geometry for one channel and contradict what Off means. |
| **BLOCK! as a shield exception on host hits** | A general rule to save one channel. Judging `Shielding` at BLOCK!'s own blast ticks keeps ordinary host bombs unchanged. |
