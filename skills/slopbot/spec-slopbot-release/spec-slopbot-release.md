---
id: SPEC-slopbot-release
companions:
  - conventions.md
  - release-evidence.md
  - ../../../docs/characters/adding-a-new-character.md
sources: []
---

# SlopBot release

## Why

SLOPArena needs a public creator guide before broader public discussion of the project. SlopBot will help technical newcomers who already use development tools and AI coding assistants turn a humanoid character concept into a verified, locally playable fighter, while explaining the project and locating its documentation. Creating the fifth character supplies the first real workflow run and the subject of a creation-diary video.

## Capabilities

- **CAP-1**
  - **intent:** Users can discover SlopBot's supported journey, understand prerequisites, and navigate relevant SLOPArena documentation.
  - **success:** A public-bundle session explains character creation and project orientation, identifies required or missing capabilities, and routes an out-of-scope request to documentation without starting unrelated implementation.
- **CAP-2**
  - **intent:** Users can agree a humanoid character concept and complete kit, then obtain usable assets through explicit sourcing, generation, and rigging handoffs.
  - **success:** The approved kit, required asset roles, source/licensing information, and unresolved handoffs are identifiable before integration; an unsupported mechanic pauses for a separate engine task instead of an unapproved redesign.
- **CAP-3**
  - **intent:** Users can integrate their rig and animation assets, make bounded adaptations to existing clips, and optionally integrate supplied splash art and VFX.
  - **success:** Real assets and exact bindings are verified on the actual character; an exercised clip adaptation has an observed before/after result, and any selected splash/VFX integration is checked on its consuming surface.
- **CAP-4**
  - **intent:** Users can author and iterate their complete agreed gameplay kit using existing engine capabilities.
  - **success:** Required canonical slots deliberately express the approved kit; Slop Editor/Training observations support the authored timing, hitboxes, damage, movement, and other selected behavior, including a test/change/retest iteration.
- **CAP-5**
  - **intent:** Users can finish with a verified persisted package and locally play the complete agreed fighter.
  - **success:** Fresh accepted package payloads and exact local roster requirements agree, package inspection is valid and not stale, and local gameplay consumes that persisted content rather than a transient draft or previous cook.
- **CAP-6**
  - **intent:** Users can choose guided or fast interaction and receive approved execution or explicit handoffs according to available supported tools.
  - **success:** Both modes enforce the same scope, approval boundary, and acceptance gates; a missing tool produces an actionable manual handoff or honest blocker, never a claimed action or verification that did not happen.
- **CAP-7**
  - **intent:** Users can resume character creation across assistant sessions without losing approved decisions or confusing old evidence with current state.
  - **success:** A resumed session recovers the goal, approvals, references, completed checks, and next blocker, reconciles them with actual project state, and invalidates checkpoints whose inputs changed.

## Constraints

- Target technical newcomers, not users who need general computing instruction; initially support humanoid fighters only.
- Distribute SlopBot and only its required helpers under the SLOPArena repository's public `skills/` tree. Users must not need BMAD, the private workspace, or the author's absolute paths.
- Guidance is harness-neutral for assistants able to load skills; execution is conditional on supported tools and permissions. Missing capabilities do not authorize a bypass or certify unexercised harnesses.
- Reuse existing technical guidance and project tooling with one authoritative owner per instruction; packaging must not create a drifting private/public copy or expand into a new cross-harness integration layer.
- Execute only approved bounded work. Guided and fast modes share permissions, runtime ownership requirements, and verification; installation, spending, publication, commits, and unrelated mutations need explicit permission.
- Follow the adopted repository authoring contract and current repository operation rules. Shared/server simulation owns gameplay; transient previews and generated artifacts are not authoring sources or proof of acceptance.
- Unsupported mechanics pause for a separately scoped engine task. Never silently substitute a different character design or extend the engine within this release.
- Complete means the whole approved kit with every required canonical slot intentionally resolved. Approved reuse is allowed; template filler, broken bindings, and stale output are not completion.
- Persist only the small progress record needed to resume. Concrete packaging, authority, and state rules are in `conventions.md`; release evidence and claim limits are in `release-evidence.md`.
- The video is a creation diary, not a tutorial; animation remains its main story. Release proof is the next-character run, not independent newcomer success or multi-harness certification.

## Non-goals

- Stage creation, game-mode creation, unrelated fixes/features, contribution implementation, and general environment repair.
- Custom rigs, original-motion generation, full model/rig authoring, and automated ownership of external asset services.
- Original splash-art/VFX generation; optional presentation polish as a mandatory completion gate.
- New engine capabilities, online match verification, server deployment, Workshop publication, or other release/publishing automation.
- A separate skills repository, the full existing skill catalog, a BMAD-style process system, or video scripting/production.
- Multiple independently verified harnesses or a newcomer trial as conditions of this release.

## Success signal

The distributable SlopBot bundle is exercised from the public skills tree for orientation, guided/fast behavior, missing-capability handoff, and interrupted-session resume. The fifth character then completes its approved humanoid kit through SlopBot, reaches verified persisted-package/local-play acceptance, and provides truthful evidence for the creation diary; no completion or portability claim exceeds the observed run.
