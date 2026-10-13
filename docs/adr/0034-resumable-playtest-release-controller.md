---
status: accepted
---

# Resumable Playtest releases

Release sessions currently reconstruct source selection, local Windows builds,
workflow correlation, receipts and Steam activation in conversation. Separate
green game/Master builds admitted an incompatible protocol pair, and deployment
failure details remained available only in private host history.

The user approved a small release CLI and the reliability fixes on 2026-10-13,
then selected CLI and reliability fixes first. Implement `scripts/release.py`
around the existing isolated builder, coordinator and restricted receiver.
Persist a versioned, atomically written release record in the ignored
`build/playtest/<version>/candidate.json`. Serialize controller mutations with
a local file lock. Retain immutable CI receipts and subsequent observations
separately. A different source snapshot requires a new candidate.

## Options considered

- Continue agent/skill coordination: inexpensive to maintain, but progress and
  recovery still depend on conversational reconstruction. Superseded by the CLI.
- Add a release dashboard/backend service first: offers visibility, but adds a
  service, credentials and deployment before the release contract is reliable.
  Deferred, together with backend-only hotfixes, by the user's selected scope.
- Wrap the maintained scripts with a local CLI: selected. Reuses the installed
  toolchain, GitHub protections and restricted VPS path without new dependencies.

## Contract

`prepare` pins the approved published game SHA, main-reachable Master SHA,
endpoint, Unity version and licensed input fingerprint, and checks protocol
compatibility from those exact revisions before expensive work. CI repeats the
same source check before images or Steam upload. `run` builds in a task-owned
clean checkout, verifies/publishes the artifact and dispatches one correlated
workflow. `status` reports durable observations; `resume` reconciles that exact
run and receipts, including a bounded deploy-job retry when failure evidence
proves it safe. Uncertain dispatch or host state never causes a blind redispatch.

Steam default activation remains an operator action. `verify-live` compares
both the uploaded BuildID and depot manifest with fresh anonymous Steam metadata.
Player acceptance is a separate explicit record of countdown, match/rematch
and diagnostic evidence; deployment success cannot mark it passed.

The receiver returns an allowlisted failure receipt even on nonzero exit. It
contains identity, failure stage/reason, active release and recovery outcome,
not private commands, credentials or raw exception text. Old receivers or
broken transport produce an explicitly ambiguous local receipt.

## Cook reproducibility

Keep the existing immutable cook/hash contract. Certification and diagnostic
cooks use a fresh isolated Win64 import, the pinned Unity version and captured
licensed inputs. Compare every roster package and dependency, retaining an
explicit correction for review rather than silently recooking release payloads.
Changing dependency-hash semantics would require a separate cook/schema migration;
it is not necessary to provide reproducible Windows certification and useful diffs.

## Boundaries

The controller does not commit, push, install tools, operate the canonical Editor,
activate Steam or widen VPS access. Invoking `run` authorizes the release actions
within the prepared source/target scope, including permitted GitHub environment
approval; an agent reuses the user's existing authorization. Source corrections
retain the repository's explicit commit/push boundary. Backups, idle registration,
immutable images, schema checks and guarded recovery remain in the existing CI
and receiver. This implementation does not perform a production release.
