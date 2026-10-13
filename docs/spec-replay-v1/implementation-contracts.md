# Replay implementation contracts

**Status:** Draft interfaces for review. The [accepted product design](../plans/2026-10-13-replay-v1-design.md)
defines behavior; this companion makes the cross-component handoffs explicit.

## Ownership

| Component | Owns | Reuses |
| --- | --- | --- |
| Shared | Recording schema/codec, compatibility and canonical validation, replay simulation session | Production ServerSimulation, InputState codec, immutable cooked runtime content |
| GameHost | Exact capture, complete-tick finalization, bounded durable queue and publication client | MatchInstance input selection, initialization, terminal metadata and current host credentials |
| Unity | File import, viewer lifecycle/time, presentation reset, camera, library and Results entry | Existing simulation bridge/presentation seams and frontend session; no second gameplay engine |
| Master | Participant/link authorization, metadata, immutable publication receipt, expiry and file delivery | Existing player JWT, owning-host authentication and Match identities in the separate Master repository |

These are ownership boundaries, not requirements to introduce a service or
abstraction for every row. Extend existing seams before adding new ones.

## Recording contract

The contract owner freezes the names and encoding at G0. Its semantic fields are:

| Part | Required information |
| --- | --- |
| Identity | Schema version, Match GUID, logical simulation compatibility ID, exact tuning/content/pose/arena identities, original build/runtime/process provenance |
| Setup | Ordered fighter/entity roster, participant references, full initial registration state, definitions/pose references, arena setup, respawn transforms, rule configuration and initial absolute tick |
| Tick | Absolute uint tick and exact selected entity/InputState entries copied before Tick; omitted input and a present neutral input remain distinct |
| Validation | Checks indexed by tick, declared canonical gameplay/event coverage and cadence, and the final captured tick check |
| Terminal metadata | Last captured tick, completeness/partial reason, recorded Match terminal tick/time/kind/reason; capture may end before Match termination |
| Integrity | Encoded/decompressed lengths, captured tick count and in-file payload integrity checks; full-file SHA-256 in publication metadata/receipts after final encoding |

Use the existing InputState representation under an explicit encoding/version
contract; Replay schema version is distinct from network protocol version. Preserve
canonical move projection rather than invent another physical-control mapping.
Resolve only the pinned cooked content and arena identity. Never substitute
currently balanced content or raw authoring JSON.

Initialize a fresh production simulation in recorded roster order, including
respawn positions and the initial clock offset. Registration initializes internal
bookkeeping; ordinary network snapshots are not full replay restart points.

Every playback/seek attempt receives fresh mutable input dictionaries. Shared
consumes fields such as ActiveSlot, so playback must not mutate the stored tape or
reuse previously consumed input entries on a later reconstruction.

Copy candidate inputs before Tick and commit them only after a successful complete
Tick and its check. Finalize on the Match loop or a synchronized complete-tick
boundary; the externally called cancellation callback alone is unsafe. Bound
capture/finalization work and keep upload/storage/compression I/O off the gameplay
tick path.

Ticks are contiguous through the captured prefix. Finalization reserves enough
budget for a valid trailer and final validation check; a recording limit cannot
leave half a tick as playable evidence. Cancellation is recorded metadata, not a
replayed wall-clock timeout. A cancellation before gameplay produces an unavailable
Match entry rather than a fabricated gameplay file.

G1 must compare canonical gameplay state/events at every tick and document coverage
of dynamic abilities, interactions, projectiles, counters and respawns. Network
snapshot hashes alone do not prove parity. The shipped validation cadence and
representation are fixed from that evidence; report the failed recorded check,
without claiming it locates divergence earlier than the available checks.

## Playback contract

Provide one checked recording/session path for backend files and local imports:

- Opening performs bounded decoding, integrity and compatibility checks, resolves
  immutable setup and creates a fresh Shared simulation for the entire roster.
- Advancing one tick returns its gameplay state and semantic events. Visible
  multi-tick updates dispatch every tick's events, not only the final bridge list.
- Speed changes scheduling; the simulation timestep stays 1/60 second.
- Seeking recreates setup and advances with validation in bounded work slices,
  exposing progress and allowing the viewer to remain responsive. A replaced seek
  cannot publish stale completion into a newer viewer/session.
- Pause and seek reconstruction control animation/effect clocks independently of
  camera/UI input. Reset event deduplication, projectile tracking, persistent
  effects and trails; suppress historical audio and reconstruct destination visuals.
- End-of-recording and validation failures stop advancement. Preserve original
  bytes for download/investigation; do not offer divergent continuation as the Match.

Reuse LocalSimulationBridge/ServerSimulation and runtime presentation bindings.
Ability Lab provides techniques, not a second authoring-data playback path.

## Metadata and API contract

Keep three independent fields: **Match outcome**, **artifact completeness** and
**availability**. Availability is Pending, Available, Unavailable or Expired;
Available may be Complete or Partial. Playback compatibility is a separate
client/build-dependent result. Unavailable reasons include no gameplay, capture/save
failure, queue exhaustion and lost/terminally failed publication.

The proposed API operations below are frozen into concrete routes/DTOs at G0:

| Operation | Authorization and contract |
| --- | --- |
| List recent Matches / get Replay metadata | Participant JWT; metadata-only, paginated, includes stable Match ID, fighter/stage identity, result, availability/reason, capture range/completeness, compatibility identities and original expiry |
| Publish artifact | Current authenticated owning GameHost; bounded metadata plus bytes; verify metadata agrees with recording, persist immutable bytes, then commit Available and return Match ID/digest receipt |
| Report recording unavailable | Owning host or Master reconciliation; bounded reason metadata independent of blob publication, without changing competitive results |
| Download participant file | Participant authorization and current expiry check; serve attachment bytes only when Available |
| Create/regenerate/revoke link | Any participant; one active unlisted token per Match; regeneration invalidates the prior token, without changing expiry |
| Download through link | Active unlisted token plus current expiry; attachment download for later Import Replay, no app launch |

Serialize opaque 64-bit identities safely at JSON boundaries. Proposed DTO names
and reason codes must be shared as fixtures rather than independently invented by
the client and backend agents.

Compute the full-file digest after final encoding and carry it alongside the file
in publication metadata/receipts. In-file integrity checks cover declared payload
ranges without including their own checksum field. Backend downloads compare the
expected artifact digest; standalone imports use container integrity and recorded
validation checks. Retries reuse finalized bytes rather than re-encode them.

Publication is idempotent for Match ID plus digest: identical retries return the
receipt; conflicting bytes never overwrite an artifact. Retry uses the owning
host's current credential after restart while retaining original build/process
metadata. Artifact publication must tolerate a Match whose Master outcome became
cancelled during restart reconciliation; it cannot repair or rewrite that outcome.

Pending must resolve after lost recordings/hosts. Retain the terminal Match rows
needed for participant-visible unavailable entries; do not construct the library
solely from successful artifact rows. Existing no-recording cleanup paths need review.

## Storage revision

[ADR-0033](../adr/0033-gamehost-input-replays-with-shared-playback.md) currently selects
compressed bytes in PostgreSQL. Later discussion recommends a **private managed
S3-compatible bucket for immutable bytes, with PostgreSQL metadata**. This is a
proposed revision requiring adoption before backend integration; no accepted record
is silently superseded by this draft.

| Option | Consequence |
| --- | --- |
| Keep PostgreSQL bytes | Existing accepted choice; simplest storage provisioning and transactional publication, with recording bytes in database backups |
| Adopt object storage + PostgreSQL metadata | Separates file growth from the Match database; requires bucket/credentials, coordinated publication and reconciliation/cleanup of unreferenced objects |

For the object-storage option, initially upload through Master using bounded
streaming/hash verification, then mark metadata Available only after object
persistence and database commit. Retry must recover an object written before a
failed metadata commit. Use immutable object keys and avoid overwriting conflicts.
Cleanup must not race an in-progress publication or delete a referenced artifact.

Master-authorized streaming downloads enforce participant/link access on each new
request. Do not expose persistent public object URLs. Direct reusable presigned
URLs would require a separate decision about their remaining validity after link
revocation; they are not the proposed v1 retrieval path.

Master enforces **recorded termination + 30 days**, including on delayed uploads,
library actions and downloads. Object deletion is separate cleanup; a bucket rule
based on upload age cannot extend application access. Expired pending uploads are
discarded with their original status/deadline. Database backups cover metadata;
object retention/recovery policy must be provisioned explicitly if this option is adopted.
