# GenRA provider A/B: Codex vs Opus 5.5

Prepared by Claude (Opus 5.5), 16 October 2026. The owner compared the results
and judged the Opus 5.5 motion "so much better". GenRA motion authorship now uses
Opus 5.5; see the [GenRA animation workflow](../characters/genra-animation-workflow.md#tooling-and-authorship).

## Setup

The comparison reran the generate pass of the
[Manki aerial experiment](2026-10-14-genra-manki-aerial-experiment.md), changing
only the provider. Same pinned GenRA (`5f4ffb8`), explicit jump/fall references,
captions, four attached images, request and 62-tick clock. GenRA rebuilt the prompt,
and the run checked it was byte-identical to the retained Codex prompt (SHA-256
`6ab30900…`) before any model call. The retained inputs matched their recorded hashes.
The Codex workspace was unchanged afterwards (2,486 files hashed).

Opus 5.5 ran through `claude -p` (Claude Code 2.1.301) at effort medium, with the saved
Claude login. Its tools were Bash, Read, Grep and Glob. Its sandbox could read only the
prompt's inputs, GenRA source and the venv, and had no network. The working directory
was an empty folder outside the repository, so no project instructions, memory, skills
or MCP servers loaded. Codex had run `codex exec` with `gpt-6.1-sol` at medium effort in
a read-only sandbox. Opus had longer limits: 1800 s instead of 600 s, and a 128K output
ceiling.

The first Opus attempt was a harness failure. With `--permission-mode dontAsk`, the
sandbox auto-allow setting did not cover heredoc, multi-line or piped Python, so the CLI
refused 9 of Opus's calculation commands. It reasoned without tools, hit a 64K output
ceiling and was stopped at 1200 s with no answer. Pre-approving Bash inside the same
sandbox fixed it. A probe confirmed the refused commands then ran while repository
reads, outside writes and network access stayed blocked.

## Results

Both first passes keep the exact clock, a fixed root and the exact fall pose at ticks
57 and 62. The checks below come from processed USD and Blender renders of the actual
experiment-time rig.

| First pass | Codex | Opus 5.5 |
| --- | --- | --- |
| Knee / elbow bend direction | all correct | all correct |
| Finger curl | **830/830 samples reversed** | 768/768 correct |
| Wind-up silhouette | fists beside the face | fists clearly overhead |
| Fist drop, ticks 18–23 | 62 cm | 83 cm |
| Fist separation at contact | 22 cm | 35 cm |
| Fists ahead of chest at contact | 21 cm | 8–10 cm |
| Largest arm turn in one frame, ticks 0–6 | 12° | 65° |
| Job time / tool calls | 354 s / 7 | 1,502 s / 71 |
| Output tokens | 21,395 | 137,890 (57,707 thinking) |

Opus built its own forward kinematics, mesh-skinning collision checks and
hinge-constrained arm IK before writing keys. Most of its time and tokens went into
that tooling. Its remaining weaknesses were the wide fist gap at contact and a fast arm
snap out of the entry pose. Codex's backward knees were introduced by its later
refinement job, not its first pass.

The Opus job's list-price cost was $6.95: $2.76 output, $2.19 cache reads (10.95M
tokens) and $2.00 one-hour cache writes. On the Claude subscription it was not billed
per token. During the run the plan's five-hour utilization rose from 36% to 44% and
weekly utilization from 11% to 13%. Those figures include any other activity on the
account at the time.

## Finger curl in the Codex aerial

The finger check measures each finger joint's rotation since rest against the
palm-down bind pose: both thumbs sit 4–10 cm below the knuckle line. Rendered close-ups
confirmed the result. Every Codex aerial version, first pass, refinement and leg
correction, folds the fingers back over the knuckles. The check also flags the
pre-correction Head Slam pose, matching the defect the owner caught there. The cartoon
variants copied the aerial's finger curves, so Manki's accepted air `3` probably still
curls its fingers backwards. This has not been checked in Unity.

## Limits and evidence

This was one run per model on one brief, using the experiment-time Manki rig rather
than the current v3 model. There was no Unity import or gameplay check. Evidence is in
`artifacts/genra-opus-ab-20261016/`: probes, both attempts, prompt identity check,
event streams, tool audit, usage, processed USD, Opus's helper scripts, assessment,
renders, comparison grids and normal/quarter-speed videos. Its README indexes the files.
