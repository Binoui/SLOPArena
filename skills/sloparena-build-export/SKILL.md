---
name: sloparena-build-export
description: "Releases the Windows Steam Playtest client and compatible VPS backends through an isolated local Unity build and GitHub Actions, with manual Steam activation. Use for release, publish or deploy requests; use sloparena-ops for read-only logs."
---

# SLOPArena release

## Current flow

Resolve the approved game checkout and the separate Master checkout. An explicit
worktree wins; never substitute main for an invalid explicit target. Repository
commands run in the checkout they affect. Read
[Release Pipeline](../../docs/systems/release-pipeline.md) for commands and
[the VPS runbook](../../deploy/vps/README.md) for privileged recovery/setup.

The routine flow is:

1. Select the approved published game `main` commit, a full approved Master SHA
   reachable from its `main`, and a new `0.2.0-playtest.N` version.
2. Run `scripts/release.py prepare` with the version, exact source pins, explicit
   Master checkout and approved HTTPS endpoint. Read its source/protocol/input
   summary; do not substitute a different explicitly selected checkout.
3. Run `scripts/release.py run` under the existing release authorization. It
   creates a clean checkout, builds/verifies/publishes the client, dispatches one
   correlated workflow, retains receipts and approves the protected VPS job when
   the account/environment policy permits. Preserve unrelated canonical edits.
4. Use `status` and `resume` on that version after interruption. Reuse completed
   steps and the exact run ID; never dispatch the latest run by assumption.
   `status --refresh` is read-only against CI and does not approve deployment.
5. Require successful VPS readiness/fresh registration before Steam activation.
   Failed deployments retain sanitized stage/recovery receipts. After resolving
   the cause, `resume --retry-deploy` is limited to a proven safe failed deploy
   job; unknown host state requires reconciliation. Retain the successful upload.
6. The operator selects the receipt's BuildID on **default**, previews the change,
   chooses **Set Build Live Now**, accepts any browser confirmation and completes
   any mobile confirmation in Steam. Run `verify-live` to observe the exact
   resulting branch/manifest. Record the packaged two-account countdown/match/
   rematch separately with `record-player-test`; CI does not mark it passed.

The selected published game SHA must contain the controller-compatible producer
and workflow. Existing historical receipts are retained, not implicitly adopted.
Cold builds remain the default. `prepare --use-build-cache` explicitly pins opt-in
release-owned import reuse; every cache hit still runs native verification. For
character or cook changes, use `certify-cook` to verify committed cooks with a cold
Windows import before player compilation. Routine releases already verify cooks
in the build and do not require separate certification. Successful certification
can seed that prepared cache. Timings retain
separate attempts, native stages, CI jobs and Steam operator wait.
For cook mismatch, `release.py cook` produces a cold Win64 report and reviewable
correction without a player build or publication. Review/publish source fixes
under their own authorization, then prepare a new version; never silently replace
an existing candidate's cooked artifacts. Low-level commands remain documented
for manual fallback.

Steam Playtest AppID is **5325920**, depot **5325921**. Build, image publication,
Steam upload, VPS deployment and default activation are separate outcomes.
Use the receipts' immutable source/image/BuildID/manifest identities; do not infer
live state from a historical release or relabel an old client artifact.

## Build and approval boundaries

The default wrapper uses the installed licensed Unity in a fresh isolated
project. It needs no shared-Editor lease, IDE-specific identity, canonical Editor
shutdown, or new approval for an already authorized release build. It copies
only the allowed ignored licensed inputs, compiles Shared in isolation, and
verifies cooked freshness, generated catalogs, actual payload/endpoint/native
bytes, attribution and excluded private/dev files before packaging.

Live canonical Editor work is a separate task governed by
[Unity CLI](../../docs/contributing/unity-cli.md). The legacy ZIP script runs
against its selected project and requires that project to be closed; it is not
the normal Steam build path.

Reuse the user's existing approvals within their authorized source/target scope.
Commits, pushes and software installations require authorization, but do not ask
again when it has already been given. Routine read-only checks, clean worktrees,
artifact handling and authorized workflow deployment need no extra confirmation.
CI runs the backend tests and deployment checks; do not repeat them manually
without a new failure or unresolved concern. Apply
[Testing and Verification](../../docs/testing.md) to the change, not every
unrelated historical checklist.

The CI receiver takes a fresh off-host backup, verifies it against the active
release, and the guarded CLI enforces fresh zero-active-match registration under
the release lock before replacing live writers. It creates a release-specific
private Master environment with the candidate catalog hash, preserving the old
file and every other setting. Routine CI releases need no manual environment
rewrite. Manual fallback requires the corresponding runbook steps.

Online arenas follow the Stage Select roster: the candidate carries the
`picker` list of `client/Unity/Assets/Resources/Stages/roster.json` as
`admitted_arenas`, and the receiver rewrites `Room__AdmittedArenas__*` from it.
Adding a stage online is one roster edit in the release commit; confirm the Room
snapshot's `admittedArenas` after readiness. A VPS receiver installed before
2026-10-17 rejects that field, so before the first such release the operator
copies the merged helpers to the VPS and installs them once
(`deploy/vps/README.md#updating-installed-helpers`; the VPS has no checkout, so
`sudo install` from a shell there finds nothing). Hidden stages need no admission. GameHost
readiness checks its own packaged `slop_pit.arena`, not this pin. Client and
GameHost arena bytes come from the same release commit; arena content has no
separate online hash check.

Credentials remain in private operator files/interfaces. Existing GitHub release
secrets handle CI SteamCMD upload and restricted VPS access. Only renew failed
credentials when needed; browser login and SteamCMD login are distinct. Do not
request passwords/codes in chat or make browser automation a release prerequisite.
When Preview is unavailable, give the operator the exact BuildID and manual
activation steps and continue independent work; do not repeatedly retry it.

## Evidence and completion

Retain local build evidence and downloaded CI receipts under
`build/playtest/<version>/`. Keep `candidate.json` resumable with source/image
pins, package/catalog identity, Steam BuildID/manifest/branch observation,
readiness/registration, backup/schema result and remaining player checks.
Leave CI receipts unchanged when recording the subsequent manual activation.

A successful packaged two-account match/rematch is gameplay acceptance; build
and backend checks do not prove it. Record whether it was exercised without
blocking routine release on unrelated historical tests. Clean only task-owned
staging/helpers and privileged sessions; preserve evidence and the reusable
private monitoring tunnel.

For an explicitly requested ZIP/GitHub or Alfred release, follow the
[alternate target instructions](../../docs/systems/release-pipeline.md#alternate-targets).
