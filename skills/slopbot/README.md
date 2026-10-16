# SlopBot skills

SlopBot helps technical newcomers learn SLOPArena and take a humanoid fighter from concept or existing assets to a verified persisted package and local play. It is a creator guide, not an asset generator or a general-purpose project coding agent.

## Load the bundle

Use all three skills:

| Skill | Responsibility |
|---|---|
| [slopbot](SKILL.md) | Entry point, project orientation, guided/fast behavior, permissions, handoffs, and progress |
| [slopbot-character](../slopbot-character/SKILL.md) | Approved kit, asset integration, move authoring, iteration, and package/local-play acceptance |
| [slopbot-animation](../slopbot-animation/SKILL.md) | Existing-clip sourcing and bounded corrections with actual-character verification |

Point your assistant's documented skill discovery at this repository's `skills/` directory, or copy these three complete skill directories into its documented skill location, keeping their names and referenced files. Use its documented reload/new-session procedure after changing discovery. SlopBot can load helpers through the skill loader or as sibling files; installing only the entry point leaves a missing-helper blocker.

No installation is needed to try the local files: ask a file-capable assistant to **read `skills/slopbot/SKILL.md` from your clone and follow it**, giving the absolute checkout path. For example:

> Read the SlopBot skill in my SLOPArena clone and follow it. Use this clone as the game checkout. Guided mode: I have a rigged humanoid and some attack clips; help me identify the next step.

For concise approved work, request **fast mode**. To continue later, give the project and the relevant `.slopbot/` record or ask to resume that character. Direct helper invocation must still load SlopBot's shared scope, permission and progress rules.

## What you need

- A skill/file-capable assistant for guidance and the selected SLOPArena checkout for current documentation and source inspection. The installed skill directory is not the game checkout.
- For character integration and playtesting, the current Unity/.NET prerequisites in [Contributing](../../CONTRIBUTING.md) and actual supported access to the project/editor/runtime. A clean clone does not include all ignored licensed/local art; supply required assets legitimately.
- A usable humanoid model/rig and real clips as you reach those steps. Model generation, rigging and clip sourcing may use external services, but users choose/provide access and confirm licenses. SlopBot does not require a particular asset vendor.
- A supported clip editor or explicit user-operated handoff for bounded animation adaptation. An assistant being able to load skills does not imply Unity/Blender control, paid-service access, or Editor ownership.

Missing tools produce a specific manual handoff or blocker. Required checks are not marked complete until real results can be inspected. Existing project operation/ownership rules apply; tool examples are not permission to bypass them.

## Supported scope

Guided mode explains project concepts as needed; fast mode reduces explanation. Both require bounded approval before mutations and keep the same verification gates. Neither authorizes installation/configuration, spending, unrelated/destructive work, commits/pushes or publication.

The character path includes concept/kit decisions, model/rig/clip handoffs, import/binding, bounded existing-clip adaptation, Slop Editor move work, Training iteration and verified-package/local-play acceptance. Optional supplied/sourced splash art and VFX can be integrated and checked; original art/effect generation is not promised.

The project guide locates current docs and explains the project. Stage/game-mode creation, general repairs/contributions, custom rigs, original motion/full model or rig authoring, engine extensions, online verification, deployment, and publication remain outside this release. Unsupported mechanics pause for a separate task; SlopBot does not silently change the character design.

## Progress and proof

Character progress lives in the selected clone's ignored `.slopbot/<goal-or-package>.md`, not the installed skills or a private BMAD workspace. The [progress contract](references/progress.md) records approved choices, asset references, input-bound observations and the next blocker. Resume reconciles the record against actual changed files; old approval is not a tool lease, and old evidence is not automatically current.

The finish is the complete approved kit with intentionally resolved required slots, valid real bindings, fresh accepted package payloads and exact local roster pins, then local gameplay using that persisted content. A transient editor draft, successful import, compiler result, or old cook alone is not completion.

**Public-release gate:** the [release contract](spec-slopbot-release/spec-slopbot-release.md) requires the next-character run to establish accepted-package/local-play completion. That gate has not been passed by implementing these skill files. The planned fifth-character video is a creation diary, not a tutorial; only the harness/tool path actually exercised may be claimed as verified. There is no independent-newcomer or all-harness execution certification.

Guidance smoke checks exercised a relocated bundle, existing-package orientation, direct animation-helper loading, guided/fast permissions, manual asset/tool handoffs, and resume after a source change. They did not run Unity or complete a fighter.

The [documentation map](../../docs/README.md) remains the entry point for detailed project rules. SlopBot needs no BMAD installation, private skill catalog, private workspace scripts, or author-specific paths.
