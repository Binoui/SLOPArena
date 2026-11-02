---
status: superseded
---

# GameHost input Replays with compatible Shared playback

**Superseded (recording and playback) by [ADR-0060](0060-gamehost-replays-world-frames-and-view-track.md), 2026-11-01.**
Input re-simulation cannot reproduce a Match across runtimes. The delivery parts below are
deferred to a later decision.

For personal PvP review and bug reports, the GameHost records the exact initial
Match setup and selected per-tick inputs before simulation consumes them. Shared
reconstructs all fighters and checks authoritative validation data; whole-Match
GameHost/Unity parity is a shipping gate, not an existing proof. V1 accepts that
simulation/content updates can make older recordings unplayable, and initially
seeks by reconstructing from Match start with a visible wait.

Finished recordings survive restart in a bounded durable publication queue and
are published immutably to PostgreSQL independently of competitive Match results.
Preserve queued recordings when that queue fills; newer recordings that cannot
be retained are unavailable with a reason. Derive conservative configurable
recording/queue budgets from the parity prototype before backend integration.
Stop playback at failed authoritative validation rather than present divergent
simulation as the original Match. Imported files use the same playback compatibility
checks. Complete simulation checkpoints, preservation of historical runtimes,
original client-prediction views, ongoing-Match crash
recovery and Live Spectator admission are deferred.

## Considered alternatives

- **Recorded state/event playback:** avoids combat re-simulation for viewing, but
  increases recording volume and needs complete projectile/effect capture. Chosen
  input playback reuses Shared under the accepted version and seek limitations.
- **In-memory publication retry:** requires less persistence work, but can lose
  finished recordings when the GameHost restarts during a backend outage. Durable
  retry preserves finished recordings without promising ongoing crash recovery.
- **Object storage with database metadata:** keeps recording bytes outside database
  backups, but needs additional application storage integration/provisioning. V1
  uses compressed PostgreSQL artifacts with bounded recording and retention policy.

The complete design was accepted on 2026-10-13, finalizing documentation only.
The complete delivery/viewing contract and implementation acceptance criteria are
maintained in the [Replay v1 design](../plans/2026-10-13-replay-v1-design.md).
Implementation and runtime verification are pending.
