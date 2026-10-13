---
id: SPEC-replay-v1
status: draft
companions:
  - ../plans/2026-10-13-replay-v1-design.md
  - ../adr/0033-gamehost-input-replays-with-shared-playback.md
  - ../../CONTEXT.md
  - implementation-contracts.md
  - delivery-plan.md
sources: []
---

# Replay v1 implementation spec

Drafted 2026-10-13 from the accepted design and subsequent storage/parallel-work
discussion. Read the listed companions as part of the contract. Accepted product
decisions remain authoritative; the proposed storage revision is explicitly open.
This artifact does not authorize implementation or deployment.

## Why

Players need to revisit authoritative PvP gameplay and attach useful evidence to
bug reports. Shared simulation makes input recordings a plausible foundation, but
whole-Match playback is unproven. This spec defines the contracts and verification
gates needed to implement the agreed feature across GameHost, Unity and Master,
with clear boundaries for parallel work.

## Capabilities

- **CAP-1**
  - **intent:** Players can review the recorded authoritative portion of an online PvP Match.
  - **success:** Real two- and four-fighter Matches reconstruct validated gameplay, including combat, deaths, respawns and the capture endpoint; incomplete recordings are labeled Partial.
- **CAP-2**
  - **intent:** Players can distinguish faithful playback from incompatible or invalid evidence.
  - **success:** Compatibility/corruption failures explain the reason; the first failed recorded authority check stops playback with its tick/reason and preserves the file.
- **CAP-3**
  - **intent:** Players can inspect combat at their chosen time, speed and fighter viewpoint.
  - **success:** Pause, speed, forward tick-step and waiting seeks work; fighter follow/orbit/zoom remains responsive while paused; seeking leaves no stale presentation or historical audio.
- **CAP-4**
  - **intent:** Participants can find their recent Match recordings after relaunch or directly from Results.
  - **success:** Home Replay Library and Results Watch Replay expose the Match ID and pending, available complete/partial, unavailable-with-reason or expired status, including Matches without files.
- **CAP-5**
  - **intent:** Players can exchange and reopen Replay files for review and bug reports.
  - **success:** Downloaded files import through the same playback checks without backend retrieval, including after backend expiry when compatible.
- **CAP-6**
  - **intent:** Participants can retrieve their Replay and share it through one unlisted link per Match.
  - **success:** Any participant can manage/revoke the shared link; participants and active link holders can retrieve the file; revocation/regeneration stops old-link retrieval and downloaded copies remain usable.
- **CAP-7**
  - **intent:** Finished recordings survive backend outages and GameHost restarts within bounded storage.
  - **success:** Durable retries preserve original provenance and publish idempotently; a full queue preserves existing files and labels newer unretainable recordings unavailable.
- **CAP-8**
  - **intent:** Replay retention and recording limits remain bounded without changing Match gameplay.
  - **success:** Valid cancellation/limit prefixes are retained where capacity allows; backend retrieval expires 30 days after recorded Match termination; retries and renewed links never extend it; gameplay continues at recording limits.

## Constraints

- Reproduce GameHost-authoritative gameplay using immutable setup plus untouched
  selected pre-Tick inputs; client prediction and InputRelay packets are insufficient.
- Shared owns gameplay at 60 Hz without Unity types. Unity presentation/camera
  cannot change the recorded simulation. Exact cooked content, poses, arena,
  rules/tuning and absolute ticks must match.
- Whole-Match Linux GameHost → supported Unity Windows parity gates shipping.
  Measured byte/tick/queue/decode/upload budgets must be fixed before backend integration.
- Compatibility may reject older gameplay/content revisions. Seek reconstruction
  starts from initial setup; a visible wait is acceptable.
- Competitive Match outcome, artifact completeness and publication availability
  are separate. Library entries do not depend on successful artifact upload.
- The accepted PostgreSQL-bytes decision remains in force until the proposed
  object-storage revision is adopted. Storage-independent recording/viewer work
  can be specified without resolving a provider.
- Parallel implementation follows the ownership and dependency gates in
  [delivery-plan.md](delivery-plan.md); live Unity work uses the existing shared lease protocol.

## Non-goals

- Live Spectator admission/feed, Training/CPU/Ability Lab recording, ongoing-Match
  hard-crash recovery and preservation of historical runtimes.
- Complete checkpoints/instant seeking, free camera, video export, public Replay
  browsing, browser gameplay playback and app-launch integration.
- Production capacity claims or delivery guarantees inferred from the illustrative
  storage calculator or unmeasured agentic time estimates.

## Success signal

A participant completes a real PvP Match, reopens its authoritative Replay from
Results or after relaunch, inspects combat, downloads and shares the file, and can
import the compatible copy after backend expiry. Partial/unavailable cases remain
explainable by Match ID, and playback never continues past a failed authority check.

## Assumptions

- The proposed interfaces and metadata schemas in the companions are a draft for
  contract freeze; names/layout and validation representation are not implemented.
- The storage revision would use a private managed S3-compatible service, PostgreSQL
  metadata and Master-authorized downloads; no provider or bucket is provisioned.

## Open Questions

- Adopt the suggested object-storage revision instead of PostgreSQL artifact bytes?
  If adopted, select the provider before storage integration. The options and
  retrieval/publication behavior are in [implementation-contracts.md](implementation-contracts.md#storage-revision).
