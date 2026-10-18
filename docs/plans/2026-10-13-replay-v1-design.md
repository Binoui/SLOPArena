# Replay v1 design

**Status:** Accepted — 2026-10-13 (documentation only). Implementation and
whole-Match playback verification are pending.

## Agreed scope

The first Replay version serves personal Match review and bug reports. Recorded
Replays come first; admitting Live Spectators is a later extension. This design
settles a bounded first version and records an extension path. Implementation is
a separate task; acceptance of this document confirms the design only.

The terms Replay and Live Spectator are defined in [the glossary](../../CONTEXT.md#replays--spectating).
An eliminated fighter watching the remainder of their Match is distinct from a
Live Spectator joining without a place in its fighter Roster.

## Playback and delivery contract

V1 covers online PvP Matches only. Training, CPU sessions and Ability Lab sessions
are outside this version's recording scope.

A Replay represents the GameHost-authoritative Match. It does not promise to
reproduce the original player's prediction, corrections, camera or exact on-screen
experience. Reports of those problems may require separate evidence.

Older recordings may become unplayable after simulation or content updates. V1
must check playback compatibility and clearly explain a mismatch. It does not
promise historical playback across gameplay updates. Exact compatibility identity
must cover the recording schema, simulation identity/tuning, exact cooked content
and arena identity; a network protocol or character catalog hash alone is insufficient.
Build provenance supports investigation; compatibility must identify the supported
Shared simulation/content contract rather than require identical Linux server and
Unity plugin binary bytes.

Players retrieve Replays from a backend recent-Match library, including after a
later game launch, and can download a Replay for a bug report.

## Recording, retention and sharing

Automatically record and save every online PvP Match. Keep completed Matches and
cleanly cancelled Matches; the latter are labeled Partial Replays and cover their
recorded gameplay. Hard-crash recovery is deferred.

Backend Replays expire after **30 days**. Downloaded copies can be kept locally,
while remaining subject to the same simulation/content compatibility limits.
The retention clock starts at recorded Match termination, including cancellation,
rather than publication or link creation. Delayed publication and link regeneration
never extend that deadline.

Match participants can retrieve their Replay. Anyone holding an unlisted Replay
Share Link can also access that Replay; public replay browsing is outside v1.
There is **one shared link per Match**, managed by any participant. Revoking it
affects everyone using that link; downloaded copies remain usable. Link expiry
matches Replay expiry and never extends backend retention. Opening a Share Link
downloads the Replay file for Import Replay in SLOPArena; app-launch integration
and a browser gameplay viewer are deferred.

Provide Import Replay for compatible downloaded files. Local copies remain usable
after backend expiry without retrieving the recording from the backend again.

## Viewing

Provide pause/resume, playback-speed controls, forward single-tick stepping and
timeline seeking. A visible wait during seeking is acceptable; near-instant seeking
is not a v1 requirement.

The camera follows any selected fighter with orbit and zoom controls. Players can
switch the followed fighter. An independent free camera is deferred.

Enter through Home's Replay Library or Watch Replay on Results. Pending saves show
their status; immediate post-Match access does not imply the upload has already
finished. Show a Match even when no Replay file exists, with an unavailable reason
and its Match ID for bug reports. Examples include cancellation before gameplay,
recording/save failure and publication-queue capacity exhaustion.

Stop at the first failed authoritative validation check, identify its tick/reason,
and retain the file for investigation. Do not continue presenting a divergent
simulation as the original Match. Compatibility and malformed-file failures also
need explicit reasons.

If a recording limit is reached, preserve a valid, explicitly labeled Partial
Replay of the captured prefix. Gameplay continues and may later finish normally;
Replay completeness is separate from Match completion. A limit ends capture at a
complete tick; the actual Match termination still establishes the retention
deadline. Exact resource budgets are derived from the parity prototype below.

## Recording design

Use **inputs plus Shared simulation playback**. Capture the exact initial Match
setup and an untouched copy of each selected input before Tick, including per-tick
entity membership. Re-run the production simulation for all fighters to generate
state/events, with authoritative validation data to detect divergence. Whole-Match
GameHost/Unity parity must be proved before shipping this playback path.

Seeking reconstructs from the beginning while showing progress. Complete simulation
checkpoints are a later optimization, not a prerequisite for v1 review controls.

Recorded authoritative-state/event playback was considered as an alternative. It
would increase recording volume and require complete presentation capture,
including projectiles and persistent effects. Existing network packets are not
already a universal complete recording. The selected input-driven design fits the
accepted compatibility limit and tolerance for waiting on seeks.

## Finalized recording delivery

Retain finished artifacts in a **bounded durable publication queue** until the
backend accepts them, including across GameHost restarts. This requires a persistent
storage provision; recovery of an ongoing Match lost in a crash remains deferred.
When the queue fills, preserve recordings already queued. Newer recordings that
cannot be retained become unavailable with a reason; do not evict queued files to
make room. Resume recording admission as capacity becomes available.

In-memory-only retry was considered and rejected because a restart during an outage
could otherwise lose a finished recording. Publication retry must preserve the
original recording's simulation/content/provenance rather than relabel it using
the restarted host's current build or catalog. A partially written file is not a
published artifact: durable finalization must commit coherent bytes and metadata
before a recording is reported as queued.

See [the architecture decision](../adr/0033-gamehost-input-replays-with-shared-playback.md).

## Backend storage

Store compressed recording bytes in PostgreSQL alongside recording metadata.
Replay bytes therefore participate in database backups. Object storage with
PostgreSQL metadata was considered and deferred: it adds application storage
integration/provisioning that v1 can avoid with the existing database.

Publication is immutable and idempotent under Match ID and artifact digest. Repeated
publication of identical bytes returns the existing receipt; different bytes cannot
silently replace a recording. Keep library metadata queries separate from artifact
retrieval so browsing does not transfer every recording's bytes.

## Ownership and minimum recording contract

| Owner | Responsibility |
| --- | --- |
| Shared | Replay encoding/validation and deterministic playback of the production simulation, without Unity types or gameplay authority in presentation. |
| GameHost | Capture setup and selected inputs, produce authoritative validation data, finalize a coherent complete/partial artifact, and retain finished uploads durably within budgets. |
| Master | Participant authorization, Replay availability metadata, immutable PostgreSQL artifacts, the shared link, and retention enforcement. Master remains outside Match simulation. |
| Unity | Compatibility checks, Import Replay, library/Results entry, controlled playback and seeking, fighter camera, and user-visible failure/status reasons. |

The recording contract must contain:

- A schema version, Match ID, original simulation build/tuning identity, exact
  cooked package/pose manifest, and arena identity/data needed for that setup.
- Ordered fighter/entity identities, participant references, initial states,
  respawn points, Match rules and the initial absolute gameplay tick. Initialize
  production simulation state consistently; a network snapshot is insufficient.
- The exact pre-consumption input values and membership for each captured tick.
  Record the GameHost's decisions about missing input rather than repeat input
  admission during playback.
- Authoritative validation checks indexed by tick, with declared coverage and
  cadence. The prototype establishes canonical state/event comparison across
  runtimes; network snapshot fields alone are insufficient validation coverage.
- Capture endpoint, completeness and partial reason, plus the eventual recorded
  Match termination time/reason and retention deadline. Capture ending at a limit
  is distinct from the Match ending.
- An artifact digest for integrity and immutable publication. Credentials and
  active access tokens are not recording content.

Validation failure identifies the first failed recorded check; that tick need not
be the tick where divergence originally began. Do not claim continuous validation
between checks unless that coverage is actually recorded. Local import and backend
retrieval use the same compatibility, integrity and bounded decoding checks.

Keep Match outcome, artifact completeness and delivery availability separate. A
normally completed Match can have a Partial Replay; a cancelled Match can have a
valid recording; a Match can have no recording. Library states distinguish pending
publication, available complete/partial artifacts, unavailable with a reason, and
expired bytes. Backend Match metadata must not depend on successful blob upload.

The fixed VPS host identity supports retry after re-registration using the host's
current bearer credential. Preserve the artifact's original process/build/content
provenance. A restart can cancel a still-open Master Match before a delayed result
arrives: recording publication must not rewrite that competitive outcome or depend
on a successful result retry. Missing-file status must eventually reconcile rather
than remain pending forever after the host or recording is lost.

## Playback time and presentation

Playback always simulates with the production **1/60-second tick**. Speed controls
change tick scheduling, not simulation timestep. Pausing stops simulation and
presentation progression while keeping the viewer UI and orbit/zoom responsive.
Forward frame-step advances one simulation tick and its visible presentation.

For a seek, rebuild from the recorded initial setup to the target tick, validate
along the way, and show progress. Suppress historical audio during reconstruction.
Restore presentation at the destination without stale effects from the previous
position: animation time, event deduplication, projectile visuals, persistent
effects and trails need explicit reset/reconstruction. Seeking forward can also
use this bounded reconstruction path; no complete checkpoint system is required.

Process semantic events for every tick during visible playback, including when
several ticks run in one rendered frame. Replay camera choices are local viewing
choices and do not affect recorded inputs or Shared gameplay. Follow existing
cooked runtime bindings; Ability Lab authoring data is not a Replay runtime path.

## Implementation order and verification gates

1. **Prove capture and playback parity.** Build a bounded prototype capturing real
   GameHost Matches before input consumption. First verify same-runtime replay,
   then full-Match Linux GameHost to supported Unity Windows playback. Compare
   canonical gameplay state and semantic events at every tick in the parity run,
   covering actual cooked kits/arenas, combat, respawns, stock depletion, hitstop,
   projectiles, missing inputs and disconnects. Include clean cancellation and
   recording-limit prefixes. Existing combo-report coverage does not pass this gate.
2. **Measure and fix configurable budgets before backend integration.** Measure
   compressed/uncompressed artifact size, capture and finalization cost, peak
   memory, decode cost and reconstruction time for two- and four-fighter Matches,
   including long and combat-heavy runs. Derive conservative per-Match byte/tick
   limits and bounded queue bytes/count/disk use; account for in-progress writes,
   validation overhead and publication buffers. Set upload/decompression bounds
   and validation encoding/cadence from those results. Check the resulting 30-day
   database/backup cost against measured Match volume; no production volume is
   assumed by this document.
3. **Complete controlled Unity playback.** Validate pause, speed, single-tick step,
   seeking/reset behavior, camera controls, compatibility and divergence reporting
   using local artifacts before relying on backend delivery.
4. **Integrate durable publication and Master storage/access.** Provision persistent
   GameHost queue storage; add idempotent upload, metadata-only participant listing,
   download, one shared link per Match and expiry. Retrying an already expired
   recording must not publish it with a fresh deadline. Apply the agreed full-queue
   policy and reconcile unavailable recordings independently of Match results.
5. **Connect the product flow.** Add Home Replay Library, Results Watch Replay,
   pending/unavailable/expired messaging, local Import Replay and browser file
   download for Share Links. Verify restart, outage and retention behavior together.

If whole-Match parity cannot be established, the input-playback design cannot ship.
Investigate the divergence and return to the documented state/event alternative
for a new design decision if necessary; do not silently replace the agreed format.
The numerical defaults and prototype evidence become part of the implementation
record before backend work begins.

## V1 acceptance cases

| Case | Required result |
| --- | --- |
| Real authoritative Matches | Full Match reproduces validated gameplay across GameHost and Unity, including all fighters, relevant cooked combat state, respawns and final outcome. Missing/held inputs reproduce the selected host input policy. |
| Compatibility and corruption | Compatible local/backend files use the same path. Wrong schema/build/content/arena or malformed/oversized data fails with a reason before unsafe playback; a failed authoritative check stops at its tick and preserves the file. |
| Viewer controls | Pause freezes playback; camera/UI remain responsive. Speed keeps the fixed tick. Step advances one tick. Repeated seeks restore target gameplay/presentation without historical audio or stale projectiles/effects/trails. |
| Partial and unavailable recordings | Clean cancellation and recording limits preserve a valid labeled prefix when it can be retained. Normal Match completion does not relabel a limited recording as complete. No-gameplay/save-failure Matches show their ID and unavailable reason. |
| Outage and restart | Finished queued recordings survive backend outages and GameHost restart, retain original provenance/deadline, and publish once. Queue exhaustion preserves existing queued files and reports newer unavailable recordings. |
| Backend ownership | A participant can retrieve their Match; an unrelated authenticated player cannot use participant access. A live unlisted link grants file retrieval. Publication cannot alter competitive Match results. |
| Shared link and expiry | Any participant can manage the single link. Revocation invalidates prior link access for everyone; regeneration does not extend expiry. Backend retrieval stops 30 days after recorded termination, including on delayed upload. Downloaded compatible files still import. |
| Library lifecycle | Home and Results expose available, partial, pending and unavailable outcomes without loading all artifact bytes. Relaunch preserves backend access. Failed/lost publication eventually resolves from pending. |
| Resource bounds | Measured byte/tick/queue/decode budgets are configured and exercised. Limits do not stall or terminate gameplay. Expiry cleanup bounds active Replay storage; backup retention remains the separate operator policy. |

These are implementation acceptance criteria, not checks already performed during
this documentation session. No complete recording is promised for an ongoing
Match lost to a hard crash.

## Live Spectator extension and deferred scope

Replays provide useful foundations for Live Spectators: immutable Match setup,
an authoritative timeline, validated Shared playback, viewer controls and a camera
independent of a fighter. They do not by themselves admit observers to a live host.

A later design must add observer admission/authentication outside the fighter
Roster and readiness slots, a live authoritative feed with buffering/catch-up,
disconnect/Match-end behavior and host/bandwidth budgets. Mid-Match joining needs
either setup plus the prior timeline to catch up, or a genuinely complete simulation
checkpoint plus subsequent input. Current network character snapshots are not
those checkpoints. Whether spectators watch live or with a delay also remains a
future product decision. An eliminated fighter's existing connection is not an
external Live Spectator admission mechanism.

Deferred from v1: Live Spectator admission/transport, Training/CPU/Ability Lab
recording, ongoing-Match hard-crash recovery, historical runtime preservation,
complete checkpoints/instant seeking, independent free camera, video export,
browser playback/app launch, public Replay discovery and object storage.

## Design acceptance

All interview decisions are incorporated. The user confirmed shared understanding
and accepted this bounded design on 2026-10-13, explicitly finalizing documentation
only. Implementation, publishing and deployment remain separate work.

## Source constraints

The GameHost selects exact-tick inputs. A missing tick repeats the newest known
input with press edges removed for up to 20 ticks after that input's tick, then uses
neutral input; presses that arrive at most 10 ticks late merge into the next consumed
tick (since 2026-10-18, [PeerInputTimeline](../../src/Shared/Rollback/PeerInputTimeline.cs)).
Disconnected and eliminated fighters are omitted from the selected input dictionary.
An input-driven Replay must preserve the selected inputs before simulation consumes
them; received client packets alone do not reproduce this policy. See
[MatchInstance](../../src/Server/MatchInstance.cs).

In the current host, disconnect/reconnect changes connection and input bookkeeping,
without directly mutating Shared fighter state. Elimination and respawn occur in
Shared; their effects are reproduced by simulation and the recorded input
membership. No additional per-tick control-command stream is currently needed.
Cancellation depends on host lifecycle/wall-clock decisions, so its endpoint and
reason must be explicit metadata. Future control-plane changes that directly
mutate gameplay would need to become part of the recording contract.

The current InputRelay is also insufficient as a complete Replay recording:
MatchInstance retains the same dictionary it passes to ServerSimulation, and
ability activation clears ActiveSlot in that dictionary before the relay is
serialized. Capturing it before Tick preserves those attack presses. This is a
source finding, not an approved runtime change. See
[ServerSimulation](../../src/Shared/ServerSimulation.cs).

Existing character network snapshots omit live ability/resolver state. They cannot
be treated as complete restart points for arbitrary combat. Starting from the
Match's initial setup avoids that particular reconstruction problem; accurate
full-Match reproduction still requires verification. See
[netcode architecture](../systems/netcode-architecture.md#6-prediction--rollback).

Reproduction also needs the ordered fighter Roster and entity identities, initial
states, respawn points, exact cooked definitions/poses, arena data, Match rules,
tuning and the gameplay start tick. Countdown advances the GameHost clock without
stepping gameplay; absolute tick identities affect interactions, events and KO
credit. A character catalog digest alone does not identify arena bytes or the
simulation build. See [MatchInstance](../../src/Server/MatchInstance.cs)
and [SteamMatchDescriptor](../../src/Shared/SteamMatchDescriptor.cs).

InputState's encoded payload is 22 bytes. Ten minutes at 60 Hz contains 36,000
ticks: raw inputs for two fighters occupy 1,584,000 bytes (about 1.51 MiB), or
3,168,000 bytes (about 3.02 MiB) for four fighters. This calculation excludes setup,
arena, ticks/membership, validation and container overhead; it is not a measured
compressed recording size. See [InputState](../../src/Shared/InputState.cs).

An ordinary Playing Match currently has no duration cap. Recording and publication
need bounded resource use without introducing a new gameplay timeout. Limits keep
a valid prefix where it can be retained; exact budgets come from the parity prototype.

The existing [combo-report replay runner](../../tools/MoveDataReport/ComboRouteReport.cs)
and [tests](../../tests/Shared.Tests/ComboRouteReportTests.cs) demonstrate bounded
same-runtime reconstruction for two fighters on a flat arena, stopping on the
first death. They do not establish whole-Match parity between a Linux GameHost
and the Unity Windows player. No cross-runtime replay failure was established by
this source review either.

Published content revisions are immutable and pinned by identity/version/hash.
Replay compatibility must respect that boundary rather than substitute newly
balanced content. See [ADR-0025](../adr/0025-workshop-package-architecture.md)
and [ADR-0028](../adr/0028-built-in-content-compatibility.md).

## Backend and client baseline

The separate Master repository already owns Match GUIDs, ordered participant
identities and terminal result/cancellation metadata. Those records do not already
contain the exact character/arena/setup data needed for replay playback. Its player
JWT/session can be reused for participant access, but a player-facing Replay Library,
artifact upload/download and Replay Share Links are new application facilities.
The private owner console's recent-Match query is not a player library.

Master uses PostgreSQL. S3-compatible storage is documented for operator database
backups, not exposed as an application replay-storage service. The tracked
GameHost deployment has no durable recording/spool mount; persistent publication retry needs
an explicit storage provision. See [VPS deployment](../../deploy/vps/README.md)
and [compose configuration](../../deploy/vps/compose.yaml).

Keep competitive Match results and recording availability separate. Finalizing a
Partial Replay must use the last complete simulation tick: Stop can be called from
outside the simulation thread, so the current cancellation callback alone is not a
safe place to capture coherent recording state. See
[MatchInstance](../../src/Server/MatchInstance.cs).

The Unity frontend has no Replay Library page, Results Watch action or packaged
runtime Replay file-import route today. Reuse the existing application session and
frontend lifecycle rather than introduce another login or menu-scene model. See
[frontend controller](../../client/Unity/Assets/Scripts/Runtime/UI/FrontendController.cs)
and [ADR-0032](../adr/0032-single-frontend-scene-with-persistent-social-ui.md).

Presentation needs explicit playback time and reset behavior. Existing scrubbed
animation helpers can help, but ordinary playback also advances animation and
triggers audio/effects independently of Shared ticks. Pause, speed changes and
hidden seek reconstruction must therefore control presentation as well as gameplay.
Some effects use unscaled time, so global Time.timeScale alone is insufficient.

[LocalSimulationBridge](../../client/Unity/Assets/Scripts/Runtime/Simulation/LocalSimulationBridge.cs)
and [PlayerRenderer](../../client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs)
provide stepping and scrubbed-pose seams. Ability Lab also has techniques for
reconstructing surviving effects and recent weapon trails. These are partial
building blocks, not an already complete Replay viewer. Event deduplication,
projectile tracking and pooled effects must be reset or recreated on backward seeks;
historical audio must be suppressed during hidden reconstruction. Process presentation
events for every visible playback tick rather than discard intermediate events when
multiple ticks occur within one rendered frame. See
[timeline dispatcher](../../client/Unity/Assets/Scripts/Runtime/World/TimelinePresentationDispatcher.cs)
and [presentation previewer](../../client/Unity/Assets/AbilityLab/Runtime/AbilityLabPresentationPreviewer.cs).
