# Stage hazards feature brief

**Status:** Direction picked 2026-11-04 from the stage-hazards brainstorm board. Nothing
is built. No tuning value is selected: timers, damage, launch and warning lengths are
"not selected". The [stage concepts guide](stage-concepts.md#current-boundaries) blocks
hazards until they have Shared/server authority.
[ADR-0062](../adr/0062-stage-hazards.md) records the architecture. The maintainer accepted it
on 2026-11-04.

Some picker stages get a hazard family tied to the stage's joke: a kraken around the
fishing boat, passing trains at the summit halt, a bass drop on the club roof, a crowd
that throws things. One switch per match turns them on or off. The goal is stages that
feel alive and a little chaotic in casual play, while the accepted static stages stay
exactly as they are for anyone who switches hazards off.

## Decisions (2026-11-04)

| Question | Decision |
| --- | --- |
| Switch | One **Stage Hazards** switch per match: Off or On. Off plays the accepted stage exactly, with the same shell and the same simulation. In Rooms the leader picks it on the Stage Select card. |
| Stages | Splash Deck, Last Stop, After Hours and Slop Pit. Hamster Habitat is out while the maintainer is unsure about the map. Table Manners and Log Off were not picked. |
| Splash Deck | The kraken: Kraken Slam, Rogue Wave and Catch of the Day. The kraken lurks hidden around the boat. The maintainer also asked for the boat to move. |
| Last Stop | **Train of the Day**: a different parked train each match, same shape, and the **All Aboard!** hazard from that train (picked 2026-11-04). |
| After Hours | **Bass Drop** on the LED dance floor. |
| Slop Pit | **The Crowd Has Opinions**: fans throw things at whoever is winning. |
| Kind of hazard | Hazards hit or shove. None changes collision in this feature: no moving or breaking floors. |

## Rules the maintainer settled (2026-11-04)

- Shields block hazards, unless a hazard's attack is marked unblockable.
- Hazards stop once the match is decided.

## Defaults until the maintainer says otherwise

These were proposed on the board and not answered. They stand as defaults.

- Hazards default to On in Local Play. Online, the Room leader picks.
- A hazard knockout counts for whoever hit the fighter last. The combat code already
  keeps that credit when the attacker is not a fighter.
- CPUs read hazard warnings and dodge. A CPU that walks into every train looks broken,
  not funny.
- Training offers the switch, so players can practise around hazards.

## Rules every hazard keeps

- A warning the players can read before it lands, in sound and on screen.
- It creates a choice: move, jump, or shove someone in.
- It hits like an attack, with damage and launch. No instant knockout.
- One hazard at a time per stage, so the fight still reads.
- It belongs to the stage's joke.

## Splash Deck: the kraken

**The kraken lurks.** With hazards on, a kraken circles the boat under the water. Players
see a dark shape, a line of bubbles, and now and then a tentacle tip breaking the surface.
Its route comes from the match seed, so every player and every replay sees it in the same
place. It attacks from wherever it is, so the lurking shape is the first warning: watch
which side it is on.

All three hazards are the kraken's moves (proposal). It uses one at a time:

- **Kraken Slam.** A tentacle rears over the rail on the kraken's side and slams across
  the deck in a lane. Later, the tentacle can lie on the deck for a moment as a wall,
  using the runtime walls Manki Mayhem already has.
- **Rogue Wave.** The kraken surfaces off the bow and the wave washes everyone toward the
  stern. This needs a shove that does not hit, which the game does not have yet.
- **Catch of the Day.** The kraken bumps the hull from below. The fish hatch bursts and a
  geyser of fish launches whoever stands on it.

**The joke (picked 2026-11-04).** The wheelhouse sign says NO FIGHTING ON DECK. The kraken is
the grumpy neighbour who enforces it: purple, nightcap on, droopy annoyed eyes, woken up by the
fight. The maintainer picked concept B ("B is fun") from four takes; the concepts are in
`tmp/stage-hazards/kraken-concepts/`.

**The boat moves (proposal).** In the simulation the deck stays still and the world moves
past it. A boat sailing at a steady speed is the same fight seen from on board, so moving
the sea, the shore and the sky changes nothing in play and costs no simulation work. With
hazards on, the boat leaves the harbour for open water, where the kraken lives. With
hazards off, it stays moored in the harbour as it is today. Rocking that tips fighters
would be gameplay; Rogue Wave covers that moment instead of a constant roll. The open sea
needs new background art: a horizon, a few passing islands, a wake and moving water.
Whether the effect reads as sailing is a prototype question, judged on gameplay-camera
captures.

The accepted [Splash Deck brief](stages/splash_deck.design.md) rules out a rocking boat
and moving geometry. Both proposals keep its shell and its simulation. The sailing changes
its background composition, which needs the maintainer's pick.

## Last Stop: Train of the Day

**Picked 2026-11-04: a different train each match, plus All Aboard!** ("lets go with your
plan"). The maintainer pictured the parked train itself changing and asked whether that works
without a layout change. The parked train's roof is a floor fighters stand on, so it depends on
when the train changes:

- **A different train each match: no layout change (picked).** Every train keeps today's
  shape: the same roof height and length, and the cab block at the west end. Collision does
  not change. With hazards On, the match seed picks today's train, for example the steam
  loco, a snowplough freight or a sleek express. This part is presentation only.
- **The train leaves and another arrives during a match: a layout change.** While the
  track is empty the roof is gone, which is the moving-floor work ADR-0062 rules out.

**All Aboard! (picked).** Today's train keeps trying to leave and never
does. The whistle blows, the bell rings and the doors light up, then the train does its own
thing:

- the steam loco vents a blast of steam along the gap;
- the snowplough freight sprays snow over the platform edge;
- the express slams its doors, and the jolt pops anyone standing on its roof.

This needs no second track, so the approved south cliff stays as it is.

**Not picked: trains that pass.** A second track carries passing trains on a seeded timer.
The south side is a sheer rock face, and the [Last Stop brief](stages/last_stop.design.md)
forbids any cosmetic surface above the KO plane, so the track would run through a gallery
cut into the rock face just under the platform edge, or deep under the gap.

## After Hours: Bass Drop

The flush LED dance floor lights a tile pattern on the beat while the DJ counts down. On
the drop, every lit tile launches whoever stands on it. Players read the pattern, find
dark tiles, or shove someone onto lit ones. The pattern comes from the match seed. The
warning lights and the music follow the simulation tick, so the drop lands on the beat
for every player.

## Slop Pit: The Crowd Has Opinions

The crowd boos, a spotlight finds whoever is winning, and fans throw chairs, bottles and
slop buckets into the ring at that fighter. Each throw shows its arc before it lands.
Leading gets loud; trailing gets a moment to catch up. The leader is the fighter with the
most stocks left, and a tie goes to the higher percent, so the crowd goes after the more
damaged leader (maintainer, 2026-11-04).

## Not in this feature

- Table Manners (Make a Wish, Rolling Crayon, Cake Knife) and Log Off (47 Notifications,
  The Camper Is Back): ideas on the board, not picked.
- Hamster Habitat: out for now.
- Floors that move or break, such as the parked train departing with its roof or a real
  moving boat. [ADR-0044](../adr/0044-manki-mayhem-hazard-director.md) rejected dynamic
  collision because it touches movement, the spatial grid and the support and ledge code
  in every mode.

## Code survey behind ADR-0062

A read-only survey of the code on 2026-11-04 found the following.
[ADR-0062](../adr/0062-stage-hazards.md) records the decisions.

- **Reusable from Manki Mayhem:** the Host Entity owner pattern, ring and sticky hitboxes,
  the stateless seeded hash, snapshot and World Frame events with the rollback ledger, and
  presenters driven by the simulation tick. The Mayhem director is not reusable: it is
  built only for `MankiMayhemMatchRule` and owns rounds, benching and scoring.
- **Arena data:** an Arcade section takes a stage out of PvP (`ArenaDefinition.Purpose`),
  so hazard data needs its own arena section.
- **The switch:** a flag on `StockMatchRule` that follows the `BallOnly` path from the
  Room through `MatchStartRequest`, the GameHost, `MatchConfig` and `ReplayRules`, plus
  the Master repository.
- **New effect:** nothing in Shared shoves without hitting. Rogue Wave needs one.
- **Aiming:** the crowd picks its target at a fixed tick and freezes its aim before the
  warning, like Mayhem's SCATTER!. Warnings must last longer than the Prediction Window.
- **CPUs:** a hazard-threat input like `MayhemThreats`, so CPUs can step out of a lane.
- **Replays:** a rules tag for the switch and a View-track source for stage hazards.
- **Presentation:** current stage motion runs on the wall clock. Hazard warnings and the
  kraken's route must read the simulation tick, or rollback and replay seeking drift.

## Proposed order

1. [ADR-0062](../adr/0062-stage-hazards.md), for approval.
2. The shared hazard core and Kraken Slam, offline in Local Play against CPUs and in
   Training, with the switch.
3. The kraken's lurk and a sailing prototype, judged on captures.
4. Train of the Day with All Aboard!, Bass Drop and The Crowd Has Opinions.
5. The shove effect, then Rogue Wave and Catch of the Day.
6. Online Rooms, Master and Replays in one coordinated release.

The kraken, the trains and the crowd throws need concept art before modelling.
