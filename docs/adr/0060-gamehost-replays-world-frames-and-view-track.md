# ADR-0060: GameHost Replays record World Frames and a stable View Track

**Status:** Accepted by the owner in chat, 2026-11-01. Implementation pending; the
[Replay plan](../plans/2026-11-01-replay-design.md) lists the steps, starting with the
`tools/ReplayBudget` measurement.
**Supersedes:** the recording and playback decision of
[ADR-0033](0033-gamehost-input-replays-with-shared-playback.md). Its delivery parts (durable
publication queue, Master storage, Replay Share Links, 30-day backend retention) are deferred
to a later decision rather than accepted or rejected here.
**Builds on:** [ADR-0039](0039-full-world-rollback.md) (World Snapshots and World Frames).

## Context

The owner wants to watch online Matches other people played, to find bugs and see patterns
across games. Later, players will request their own Replays from the server and watch them.

ADR-0033 recorded the GameHost's consumed inputs and re-simulated them for playback. That only
reproduces the Match if the Linux GameHost and the Unity Windows player compute bit-identical
results. They do not: on 2026-10-18 the same seeded slop_pit fight landed 12 GameHost hits on
the maintainer's machine and 7 on GitHub's ubuntu-latest, both .NET 8, because the C library's
float functions differ ([full-world rollback plan](../plans/2026-10-18-full-world-rollback.md),
phase 5 notes). ADR-0039 already rejected input-only netcode for the same reason.

ADR-0033 rejected recorded state because it needed complete projectile and effect capture.
Since protocol 10 every World Frame carries exactly that: the complete World Snapshot after the
tick (fighters, ability runtimes, resolver hitboxes and projectiles, mode objects), the input
each fighter consumed, and the event and hit records of that tick and the two before it. A
single frame restores on its own into a `ServerSimulation` built from the arena, the Match rule
and the registered fighters, which is how `NetplayPredictor` builds its own shell.

The game ships almost daily. `SimulationProtocol.Version` went from 31 to 33 over playtest.38,
.40 and .41, and any tuning change alters the content hash. A Replay that needs the exact build
that recorded it would stop playing within days, often because of a single damage value.

## Decision

1. **The GameHost records.** `MatchInstance` hands the bytes `BuildWorldFrame` produces, which
   are identical for every peer, to a background writer. Recording never blocks the Match thread
   and never ends or changes a Match. If the writer falls behind or reaches a limit, the
   recording ends at a complete tick as a Partial Replay.
2. **Each Replay file has two tracks.**
   - The **Exact Track** is the World Frames, one per gameplay tick from the first Playing frame.
     Frames are stored with their bodies uncompressed, inside compressed chunks with a tick
     index. It plays only on a build with the same `SimulationProtocol.Version`,
     `WorldFramePacket.FormatVersion`, `ServerSimulation.SnapshotFormatVersion` and content
     hash. It is for exact restore and inspecting internal state.
   - The **View Track** is a small, versioned, per-tick record of what was on screen. The
     GameHost writes it at record time from the same Shared state and content that ran the
     Match. Per fighter it holds the state the Unity renderer reads, and the active move as its
     `CookedSlotDefinition.Id` string with its elapsed ticks. It also holds active hitboxes and
     projectiles as shapes and positions, mode objects, and that tick's events and hits by their
     semantic keys. It holds no slot ordinals and no tuning values, so a balance change never
     makes it unplayable.
3. **The View Track only grows.** Fields are added, never removed or reinterpreted, and each has
   a default. Identities are strings. A new View Track version keeps reading every older one.
   Each release adds a small golden Replay to the tests, and CI fails if any past golden Replay
   no longer opens and plays.
4. **Playback never re-runs the Match.** A Shared Replay reader and player decode both tracks.
   On a matching build the viewer restores the Exact Track. On any other build the View Track
   drives the existing Unity renderers through an adapter, with today's models, clips and
   effects looked up by ID. A move that no longer exists shows a neutral pose and its name. A
   file newer than the viewer asks the player to update. Display never calls
   `ServerSimulation.Tick`.
5. **The file is self-describing and append-only.** The header carries the Match ID, mode and
   rules, arena, start tick, the roster in entity order (package ID and version, form, camp,
   colour, participant identity) and the build and version identities. Chunks are appended as
   the Match runs, so a GameHost crash leaves a readable prefix. The trailer carries the last
   tick, the end reason (finished, cancelled, limit) and a result summary.
6. **Version 1 serves the owner.** Recordings go to a durable volume on the VPS with an age and
   size limit, and a tool pulls them to the owner's machine. The Unity viewer opens pulled files.
   A pattern report reads the View Track, so it can aggregate Matches across builds.

## Consequences

- No whole-Match cross-runtime parity gate exists any more. Exact playback restores what the
  GameHost computed; View Track playback shows what the GameHost recorded.
- The View Track is a new long-lived contract. Its golden-Replay test is the only thing that
  keeps it honest, so it must run in CI from the first release that records.
- `PlayerRenderer` and the other presenters read `CharacterState` and cooked definitions
  directly today. They need an adapter from View Track state, which is the largest Unity task.
- Files are larger than an input recording. The plan's first step measures the real size per
  minute and sets the VPS limits from it.
- Older Replays play with today's art. A model swap such as FightGuy v2 shows the new model
  performing the old Match, which is accepted.
- Inspecting exact internal state of an older Replay still needs a checkout of the release
  that recorded it. Most bug review does not need that.
- The GameHost needs a durable volume, which is a deployment change that needs the owner's
  approval.
- `MatchPlayer` carries a Steam ID but no display name. Names are resolved outside the file
  from the participant identity unless Master starts sending them.

## Considered alternatives

- **Inputs plus re-simulation (ADR-0033).** Smallest files, but the replayed fight drifts from
  the real one on another machine or runtime, so a hit that landed can miss. Rejected.
- **Client-side recording of received frames.** No server work, but the files stay on players'
  machines, which defeats the owner's use case, and lost packets leave gaps. Rejected.
- **A World Snapshot format that stays readable forever.** Possible with migrations and shipped
  old cooked content, but every simulation change would carry a compatibility cost, in the code
  that changes most often. Rejected in favour of a separate, small View Track.
- **Keeping old builds installed for old Replays.** Works for the owner through Steam branches,
  but a player would have to launch an older game. Rejected.
- **Rendering video on the server.** Survives every update, but costs GPU time and storage and
  removes pause, camera choice and inspection. Deferred as a later export feature.
- **Keyframe snapshots plus inputs.** Smaller than the Exact Track, but playback drifts between
  keyframes and jumps at each one. Rejected unless measured sizes force it.
