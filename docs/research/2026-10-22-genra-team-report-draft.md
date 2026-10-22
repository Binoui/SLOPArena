# Draft: using GenRA ideas for animation in a Unity platform fighter

Draft for the SLOPArena owner to review, 22 October 2026. Not sent. Metrics are a snapshot at 10:05 Europe/Paris. Media and a public reproduction bundle are still to be selected.

Hello GenRA team,

We are developing SLOPArena, a Unity 6 cartoon platform fighter, and experimenting with LLM-assisted animation as our main authoring workflow. Our characters have different proportions, nonstandard secondary bones and held props. We need short, readable motions that preserve the existing rig and fit exact gameplay events in a 60 Hz simulation.

GenRA gave us a practical starting point: reason about the actual asset and its references, author inspectable joint-space animation, preserve the rig and iterate. We have moved from isolated experiments to original clips that our project owner reviewed in game and accepted into character packages.

We want to distinguish two implementations. Our initial experiments used GenRA at revision `5f4ffb8ac846f91265c48e8bb65d702dca11b7a1`, with a custom Codex adapter and then a Claude Code adapter. They used your prompt, sparse animation representation, processing and USD output. Our latest workflow has evolved into an in-house toolkit: an Opus 5.5 agent writes Python against constrained pose/IK tools, inspects renders and saves poses; our writer stores quaternion curves directly in USD. We then convert them to Unity Humanoid clips and verify them on the real prefab. The latest timings below describe that derived workflow, not unmodified GenRA performance.

The author works from a rig pack exported from Unity's actual playback path: skeleton, meshes, weights, textures, references, prop anchors and measured Humanoid limits. A brief supplies the story beats and event clock. The owner approves key poses before motion is filled in. Numeric checks, geometric checks and a separate image-based director review support the owner's final decision; model acceptance and owner acceptance are recorded separately.

Some recent examples:

| Example | Recorded authoring time across attempts | Recorded model cost | Result |
| --- | ---: | ---: | --- |
| Bashie: turtle lifeguard idle + power walk | 29m 14s summed job time, including one corrected pose pass | $5.16 | Both accepted by the owner in game and integrated. Footwear articulation and shell collisions required character-specific constraints. |
| Manki: aerosol shake + lighter/fire | 10m 36s across three blocking jobs | $2.07 | Owner chose the basic blocking as final because the props and effects conveyed the move. Raw source checks retained arm-limit/toe-drift issues; their final disposition needs documenting. |
| Bonk: discus-like pot-lid throw | 11m 45s for keys + motion | $2.01 including review | Beat targets passed; body commitment remained stiff. Owner selected a retimed 41-tick version and requested a revision. |
| Bonk: two-handed plank sweep | 2h 09m summed job time over six attempts | At least $8.57 including reviews; one timeout's cost unreported | Grip and silhouettes improved, but motion still needed a flow pass. |

Costs are provider-reported list-price equivalents, not subscription invoices. Times include tools inside authoring jobs and exclude concept/reference generation, rig preparation, Unity coordination, owner waiting and integration. Jobs can overlap. These are a small, changing production sample, not a controlled comparison between providers or systems.

The most useful lessons for us have been:

1. **Geometrically valid poses can still produce stiff motion.** Shared pose arrival times, static holds and a head moving rigidly with the hips survive many numeric targets. Speed maps and normal-speed gameplay-camera review expose this. Approved-key constraints also need room for secondary overlap without changing the accepted action.
2. **Props need authored contact semantics.** A loose second-hand grip on a huge plank differs from a rigid two-handed transform. Solves must keep a consistent elbow/roll branch between keys. A rigid shell and footwear need their own clearance and articulation treatment.
3. **Exportability is part of the problem.** A deformation-bone rotation can look correct in the authoring renderer but exceed Unity Humanoid axes or muscle limits. We obtain constraints from the actual Avatar and compare against the actual skinned asset and runtime playback.
4. **Repair scope matters more than more retries.** A revision can improve a measured feature while losing the approved gesture. We now favor one bounded pass tied to a diagnosed interval, preserving the previous accepted candidate and keeping owner direction ahead of generic director notes.
5. **Agent tools and diagnostics can dominate latency.** One 33-minute authoring job spent about 23 minutes in tool spans. Profiling one full check on its saved result took 31.5 seconds, almost entirely software visibility and mesh-clearance work; numeric checks alone took about 0.5 seconds. We are considering cheap inner checks with complete geometry validation at delivery.

Several failures were in our own integration: a two-handed carry solver changed the weapon path, an early clearance classifier missed a rigid shell, and our review path dropped an approved-pose constraint. We are addressing those locally and would not attribute them to upstream GenRA. We also need better interrupted-run accounting and immutable toolkit snapshots.

Upstream support that would help us most:

- A documented provider/agent-tool adapter, including prompt/image forwarding, bounded execution, checkpoints and usage records.
- Portable rig constraints: joint axes, limits, knee/elbow poles, root policy and prop contact anchors.
- A scoped refinement contract: editable joints/channels/intervals, hard event ticks, frozen contacts and explicit key replacement rules.
- Reproduction examples for Unity Humanoid interchange and stage-level timing/cost/validation reports.

We would be interested in your view on which of these fit GenRA's direction, and in supplying a small reproducible example if useful.

Before sharing, we would select three pieces of media: Bashie's accepted idle/run at game speed; Bonk's two-handed grip and flow failure; and Manki's complete move showing why intentionally simple blocking worked. A reproduction package should contain only redistributable inputs, the brief, tool/version information, outputs and measured checks. We would keep private account logs and unrelated project material out of that package.

Project background and detailed local evidence are in our [run audit](2026-10-22-genra-run-audit.md). Your [pipeline documentation](https://github.com/MacPaw/GenRA#pipeline) and [refinement API](https://github.com/MacPaw/GenRA#refinement-workflow) are the upstream reference points for this report.
