/**
 * Skill observation schema — phase 1 (corpus building).
 *
 * One line = one agent session. Append-only JSONL.
 * Raw observations: data/observations.jsonl (gitignored, local).
 * Promoted labelled fixtures: data/fixtures.jsonl (committed).
 */
export type Outcome = "good" | "questionable" | "bad";

export type SkillObservation = {
  /** Stable id: `<session-id>` (dedup key on re-import). */
  id: string;
  /** First real user instruction of the session (ISO timestamp of session start). */
  timestamp: string;
  /** The real user prompt that started the operation. Capped at 2000 chars. */
  prompt: string;
  /** Session title assigned by the harness (handy for browsing). */
  title?: string;
  repo?: string;
  /** Repo HEAD at session start, resolved via git history. */
  commit?: string;
  agent?: string;
  model?: string;
  provider?: string;
  /** Skills the user invoked by name (slash command in prompt, e.g. `/implement`). */
  explicitlyInvokedSkills?: string[];
  /** Canonical skill-read attempts (`tool_execution_start` for `skill://…`); success is not inferred. */
  loadedSkills: string[];
  toolsUsed?: string[];
  outcome?: Outcome;
  /** Optional: skills the observer says SHOULD have been loaded. */
  expectedSkills?: string[];
  /** Optional: skills the observer says must NOT be loaded. */
  forbiddenSkills?: string[];
  notes?: string;
};
