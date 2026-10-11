---
name: code-review
description: "Review SlopArena changes against project standards and the approved specification."
---

Review requested SlopArena changes for both project standards and approved behavior.
Use independent reviewers when the user or an applicable workflow requests them.

## Snapshot and scope

Resolve the requested commit/tree or capture the requested working-tree diff.
Inspect the matching source, including intentional unstaged/untracked changes.
Use the user's request as the specification when no separate spec exists. State
missing evidence rather than inventing an acceptance criterion.

Inspect Shared authority, deterministic state, package/catalog admission, collision
geometry, event provenance and Unity presentation boundaries where affected.
Load relevant guidance only. Do not redesign selected UI/content or apply unrelated
standards. Explain concrete defects with an affected path and observable consequence.

## Verification and findings

Use [docs/testing.md](../../docs/testing.md) for checks relevant to the diff. Report
commands/results and known baseline failures; offline Shared checks pass
`-p:SkipUnityPluginCopy=true`. A docs-only review does not need an Editor cook,
a Shared suite or human gameplay acceptance. For gameplay changes, distinguish
implementation, automated evidence and any required human/runtime acceptance.

If the task requests a test matrix, record relevant requested/resolved identity,
seed/tier, command, result and artifact. Reject identity mismatches and semantic
failures even with exit zero. A zero-test filter proves nothing.

Order findings by severity; include source locations and concise reproduction or
reasoning. Distinguish confirmed defects from questions and unavailable validation.
No findings means no defects found in the reviewed scope, not proven correctness.
Do not modify, stage, commit or push a review snapshot unless the task authorizes it.
