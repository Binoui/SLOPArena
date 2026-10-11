# SlopBot implementation conventions

## Public bundle and boundaries

- `skills/slopbot/` is the public entry skill in the existing SlopArena repository. Its authoritative release contract is the sibling `spec-slopbot-release/` folder.
- Required helper skills live alongside SlopBot under `skills/`. Select only helpers needed for character creation and orientation; publishing the entire `.omp/skills` catalog is outside scope.
- Existing character-workflow, animation-authoring, Ability Lab, and applicable verification guidance are reuse candidates. Keep one authoritative technical source and update consumers when a selected source is relocated; do not maintain separate private/public copies that drift.
- Required skills, references, and prerequisites must be discoverable from the public bundle and repository. Do not depend on private BMAD installation, private workspace scripts, machine-specific paths, or a named harness merely to provide guidance.
- SlopBot orchestrates the existing process. It does not introduce a project-management system, generalized adapter framework, new gameplay primitives, or replacement asset tooling.

## Capabilities and approvals

- Identify the selected game/project paths and available capabilities before proposing execution. Use supported project routes for file work, live Unity operations, animation work, and verification; do not assume a harness has those tools because it can load the skill.
- Missing capability means an explicit user-operated handoff with expected input/output, or a blocker when no supported path is available. Resume only after inspecting the actual result. Do not report a manual step complete merely because instructions were supplied.
- Explain a bounded change and obtain approval before mutation. That approval covers its routine steps; do not request permission again for each command or skill transition.
- Installation, paid services, publication, commits/pushes, destructive or unrelated changes, and changes outside the approved character contract require explicit permission. Preserve unrelated user work.
- Live Editor actions obey the selected project's ownership and supported command protocol. Local command examples in existing guidance are not authorization to bypass ownership or require the author's private workspace. If no supported public tool route is available, hand off or stop honestly.
- Guided mode explains unfamiliar concepts and asks for decisions at the relevant point. Fast mode reduces explanation and proceeds within the approved task. Neither relaxes scope, permissions, or evidence.

## Character authoring

- Establish an approved kit and required presentation before integrating a new fighter. Record the gameplay role, movement, intended attacks/interactions, asset needs, and engine limitations without fixing the fifth character's creative choices in this reusable skill.
- Generated models, rigging services, animation libraries, and optional image/effect sources are explicit handoffs. Users supply access and select assets; check provenance, license, attribution, and redistribution rights before adding assets to the repository/package.
- Support ordinary humanoid rigs. Identify unsupported rigs early. Full model/rig authoring and custom-rig support are outside this release; required repairs are explicit handoffs rather than hidden expansion of scope.
- Keep package metadata, gameplay source, and Unity asset bindings in their existing distinct modules: `package.json`, `character.json`, and `CharacterAssetCatalog.asset`. Runtime reads accepted cooked content, not raw authoring JSON.
- Follow the existing sixteen-slot ground/air contract and authoritative 60 Hz timing. Approved aliases or reuse must intentionally serve the kit; starter defaults are not approved balance or completed moves.
- Existing engine primitives determine what can be authored. On an unsupported mechanic, identify the missing capability, pause the affected branch, and scope a separate engine task. Offer alternatives without silently adopting them.
- Bounded animation adaptation starts from real source clips and a known rig. Preserve recoverable source, distinguish presentation adjustment from gameplay changes, and check the imported result on the actual character at runtime. Successful export/import alone does not establish motion quality.
- Optional splash art and VFX are supplied/sourced assets with integration checks. Reuse existing assets only when their ownership and binding are valid; do not fabricate missing presentation or hide broken references behind another fighter's fallback.

## Acceptance and progress

- Local iteration can use transient Slop Editor/Ability Lab content. Final acceptance follows the existing inspect/cook/verify/local-roster boundary and exercises persisted content.
- Shared/server owns gameplay behavior. Unity animation, presentation, VFX, and editor preview do not decide damage, hit results, timing, or admission. Never repair a gameplay problem only in presentation.
- Keep a small durable, user-project-associated record: goal and package, approved kit and decisions, bounded approvals, asset/binding references, completed checks tied to their inputs, and the next action/blocker. Do not store account secrets or require private planning infrastructure.
- On resume, reconcile references, current source/bindings, and accepted-package identity with the record. Invalidate only evidence affected by changes; retain prior valid work rather than replaying the entire journey.
- Structured failures and stale diagnostics remain visible. A preserved older valid cook is recovery state, not proof the current character is accepted.

## Existing authority

The adopted [Adding a Character](../../../docs/characters/adding-a-new-character.md) guide owns detailed package authoring and acceptance rules. Current repository policy owns approval and tool authorization. SlopBot must locate the relevant public documentation rather than embed copied tuning values or private command paths as permanent requirements.
