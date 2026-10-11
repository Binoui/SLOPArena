# Skill Evals (phase 1: observation corpus)

Lightweight evaluation/observability for the agent skill system. Records which
skill reads were attempted for real SlopArena operations, makes them browsable
in Evalite, and lets you promote interesting cases into deterministic routing
regression tests. Not a telemetry platform.

## Where skill-use information comes from

This importer currently supports OMP logs only; native Codex/T3 rollouts are not
imported by this tool. Do not treat its corpus as a comparison across harnesses.

The OMP harness already writes structured per-line JSONL session logs:

```
~/.omp/agent/sessions/<project-dir>/<timestamp>_<session-id>.jsonl
```

No live instrumentation. The importer consumes them:

- `type:"session"` — session id, timestamp, cwd (repo), title
- `type:"custom", customType:"tool_execution_start"` — every tool call with args;
  `read` calls whose `args.path` starts with `skill://<name>` are recorded skill-read attempts; `agent/` and `agents/` URI namespaces
  are normalized to the skill name
- `type:"message"` — user prompts (first real instruction becomes `prompt`),
  assistant messages carry `model`/`provider`

Explicit invocation is detected as `/skill-name` mentioned in any user text of
the session (matched against the installed skill name list), distinguished from
automatic loading.

## Commands

```bash
cd tools/skill-evals
bun install          # once, after approval when an agent is installing tools

bun run import       # scan omp session logs → data/observations.jsonl (idempotent)
bun run list [text]  # quick triage table in the terminal
bun run annotate <id-prefix> good|questionable|bad \
  [--note "..."] [--expected a,b] [--forbidden c] [--promote]
bun run eval:dev     # Evalite UI at http://localhost:3006 (watch mode)
bunx evalite evals/routing.eval.ts   # run one eval file only
```

`--promote` appends the annotated observation to `data/fixtures.jsonl`, the
committed labelled set. An observation with `expectedSkills` becomes a routing
regression case automatically.

## Evals

- `evals/observations.eval.ts` — observation browser: every recorded operation
  with prompt / explicit skill / loaded skills / model / repo-commit / outcome /
  notes. Trivial deterministic task; **no scorers** — browsing only, never
  contributes to any score.
- `evals/routing.eval.ts` — the scored regression, over `data/fixtures.jsonl`
  ONLY (promoted, explicitly labelled observations; raw unlabelled data cannot
  enter). Two suites, split so no case can pass vacuously:
  - *required skills (recorded)* — only cases with `expectedSkills`; scores 1
    iff every required skill was loaded, else 0.
  - *forbidden skills (recorded)* — only cases with `forbiddenSkills`; scores 1
    iff no forbidden skill was loaded, else 0.
  Extras are reported in the output, not penalised. No LLM judge.

## Observation schema

```ts
{
  id, timestamp, prompt, title?, repo?, commit?, model?,
  explicitlyInvokedSkills?,   // slash-invoked by the user
  loadedSkills,               // skill-read attempts recorded at tool execution start
  toolsUsed?, outcome?,       // outcome: good | questionable | bad (optional)
  expectedSkills?,            // set by annotation; enables scoring
  forbiddenSkills?, notes?
}
```

All sessions with a real prompt are kept, including zero-skill sessions. Re-import
with `--force` to backfill that baseline; manual labels/notes are preserved. Missing
OMP logs leave existing observations untouched. Skill discovery uses this checkout
and the current user's documented install locations, without author-specific paths.

Prompts are capped at 2000 chars. No conversation dumps, no chain-of-thought.

## Storage

- `data/observations.jsonl` — raw local corpus, **gitignored**
- `data/fixtures.jsonl` — promoted labelled regression cases, **committed**
- results DB lives in `node_modules/.evalite` (Evalite-managed)


### Known limitation: no live router hook

The omp agent has no independently invocable skill router — skill selection
happens inside the model's context (system-prompt skill descriptions plus
mandatory SKILL.md reads). The routing eval therefore scores the routing that
was *recorded*. If a standalone routing hook ever exists, replace the `task` in
`evals/routing.eval.ts` with a live routing call; the scorers stay unchanged.

Other limitations:

- `loadedSkills` = skill-read attempts observed at `tool_execution_start` via `read skill://…`; a failed read is not proven loading (the harness rule
  makes matching skills read their SKILL.md, but a skill that matched and wasn't
  read is invisible). System-prompt skill *descriptions* are not logged.
- Subagent (task/eval) skill reads are logged in nested session files only.
- `outcome` is optional; unlabelled observations have no correctness score.
- Concurrency is capped at 2 in `evalite.config.ts` for future paid-model evals.
