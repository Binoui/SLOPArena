---
name: implement
description: "Implement SlopArena work from an approved spec or ticket."
metadata: {"invocation": "explicit"}
---

Implement the approved SlopArena work. Loading this workflow does not expand scope.

## Plan and execution context

Read the selected checkout's `AGENTS.md`, the requested spec/ticket if any, and
relevant public docs. Keep a bounded plan: affected paths, authoritative seam,
verification and material risks. Reuse the user's approval; architecture changes
need agreement on their intended shape before implementation.

Validate the selected game checkout using [orientation's execution-root rules](../orient/SKILL.md#execution-root).
Run commands there. Optional BMAD/planning artifacts remain at their supplied root;
a plain user request does not require a BMAD installation, spec ceremony or private
workspace. Handoffs name actual checkout/spec paths, scope and acceptance.
For multi-repository work, assign explicit targets. Worktrees and symlinks do not
isolate linked repositories or a shared Unity Editor.

Trace gameplay changes through input → package/catalog → Shared simulation →
state/events → Unity presentation. Identify legality, event provenance and collision
geometry before changing callers. Reuse existing infrastructure. Read only relevant
combat/entity/netcode/stage guidance. Preserve unrelated working-tree changes.

## Implementation and proof

Use focused behavioral coverage where a plausible regression warrants it. Run the
checks applicable to the changed surface in [docs/testing.md](../../docs/testing.md).
Do not require a full suite for documentation edits or unrelated layers. Offline
Shared tests/builds pass `-p:SkipUnityPluginCopy=true`; live Editor commands and
plugin copies follow [Unity CLI](../../docs/contributing/unity-cli.md) and the
[coordination protocol](../../docs/contributing/unity-editor-coordination.md).
Worktree agents may use their own gateway lease against an explicitly approved
Editor; never substitute another target or ask an owner to insert routine checks.

Do not hand-edit cooked output in place of authored input or add client-only
gameplay corrections. User-requested numeric values are binding under `AGENTS.md`.
Report human gameplay acceptance separately from compile, headless or spectator
results; keep it pending if it was required and could not be exercised.

## Review and delivery

Review the captured diff and matching source. If independent reviewers were
requested, keep their snapshot stable until they finish, then resolve findings
and review any changed delta. Do not invent a reviewer requirement or stage a
snapshot to make it easier to review. Report changed behavior, verification and
remaining limitations. Commit and push only within explicit user authorization.
