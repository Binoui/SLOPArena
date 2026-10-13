# Replay delivery and parallel work

This plan specifies implementation dependencies; it does not start implementation.
Capability IDs refer to [spec-replay-v1.md](spec-replay-v1.md). Acceptance details
remain in the adopted [Replay v1 design](../plans/2026-10-13-replay-v1-design.md#v1-acceptance-cases).

## Gates

| Gate | Evidence needed | Work it unlocks |
| --- | --- | --- |
| G0: Contract freeze | Shared artifact/session surface, metadata/reason DTOs and versioned fixtures reviewed together; unresolved storage revision stays explicitly marked | Independent capture, viewer and backend scaffolding against agreed contracts |
| G1: Parity prototype | Real GameHost captures reproduced tick by tick in the same runtime, then Linux GameHost → supported Unity Windows; full Match scenarios include actual cooked combat, deaths/respawns, input loss/disconnect and clean prefixes | Confidence in input playback; measured full-viewer work and resource policy |
| G2: Resource budgets | Compressed/decompressed sizes, capture/finalization CPU, peak memory, decode/seek cost; conservative configurable byte/tick/queue/upload/decode limits fixed and exercised | Backend integration after storage revision/provider is resolved |
| G3: Integrated acceptance | Agreed viewer, library/import/share flow plus outage/restart, queue limits, expiry, malformed files and failed-check behavior verified | Reviewable v1 for a separately authorized release |

No backend integration begins before G1/G2. Scaffolding may prepare contracts and
fixtures without provisioning production storage or making product availability
depend on an unproven recording path. A smoke capture is useful intermediate
evidence, not whole-Match parity proof. If parity fails, investigate and revisit
the recorded-state alternative explicitly before shipping a different design.

## Parallel work packets

| Packet | Scope and capabilities | Write ownership | Handoff |
| --- | --- | --- | --- |
| A: Capture and Shared playback | Exact setup/pre-Tick tape, codec/session, validation/parity, complete/partial finalization; CAP-1/2/8 | New Shared Replay code, necessary ServerSimulation diagnostics, GameHost MatchInstance/capture hooks and focused Shared/Server tests | Versioned files and metadata fixtures, parity evidence and measured recording budgets |
| B: Unity viewer and entry points | Import, controls/seeks, presentation reset/camera, Home/Results library; CAP-2/3/4/5/6 | Unity Replay/Frontend/presentation code and assets; fixtures consumed without changing A's schema | Working viewer with real files, presentation evidence, client integration against frozen metadata API |
| C: Master/storage and delivery | Durable upload/retry, storage/metadata/access, link lifecycle and expiry; CAP-4/5/6/7/8 | Separate Master repository, new GameHost publication/queue files and startup/shutdown wiring, deployment queue/storage provisioning and relevant tests | Authenticated immutable publication, file/library endpoints, restart/retention evidence |

A owns existing MatchInstance edits. C integrates queue calls through A's agreed
handoff; it does not independently patch those capture/lifecycle methods. A owns
schema/validation changes and updates fixtures before consumers adopt a new version.
B and C share DTO fixtures; neither creates another Match/content identity model.

Use isolated branches/worktrees where appropriate and preserve unrelated edits.
One coordinator integrates and verifies the combined contracts. Worktrees do not
isolate the canonical Editor: plugin imports and live Unity checks follow
[Editor coordination](../contributing/unity-editor-coordination.md), with each
caller waiting for their own bounded lease. Current documentation authorizes no
worker launch, Editor operation, deployment, commit or push.

## Verification and handoff

- A demonstrates the selected-input policy, attacks whose ActiveSlot is consumed,
  absolute tick initialization, real content/arena, death/respawn and partial
  endpoints. Record validation coverage, original runtime/build identities and
  first divergence evidence rather than weakening checks to hide differences.
- B demonstrates pause during hitstop/animations, camera while paused, multiple
  visible ticks per frame and repeated seeks without stale projectiles/effects/
  trails or historical audio. Incompatible and corrupt files use the import path.
- C exercises identical retry versus digest conflict, object/metadata failure
  boundaries, finished-queue restart, queue exhaustion, lost pending status,
  participant/link authorization, revocation and original-deadline expiry.
- Integrated evidence covers two- and four-fighter Matches across the supported
  runtime pair and all agreed acceptance cases. A same-process combo replay or
  passing API test alone is insufficient evidence of the complete feature.

Use the applicable checks in [testing.md](../testing.md): offline Shared/Server
builds/tests pass `-p:SkipUnityPluginCopy=true`; copying into Unity needs a lease.
Master follows its own repository checks. Unity-facing changes leave the required
Test in Unity checklist and observed runtime/console evidence. Production bucket
setup and release remain separate authorized actions.

The earlier time estimates are planning ranges. Re-estimate after G1/G2 using
observed agent progress and verification time; they are not task deadlines.
