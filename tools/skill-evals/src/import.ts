/**
 * Importer: ~/.omp/agent/sessions/[project]/[session].jsonl  →  data/observations.jsonl
 *
 * Consumes the harness's own session logs; no live instrumentation.
 * Idempotent: sessions already present in observations.jsonl are skipped.
 * Keep sessions with no skill reads too, so routing comparisons have a baseline.
 *
 * Usage:
 *   bun run src/import.ts             # import sessions whose cwd mentions SlopArena
 *   bun run src/import.ts --all       # import every project's sessions
 *   bun run src/import.ts --force     # re-import even if session id already recorded
 */
import { readdirSync, readFileSync, existsSync, mkdirSync, writeFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { basename, join, dirname, resolve } from "node:path";
import { homedir } from "node:os";
import { fileURLToPath } from "node:url";
import type { SkillObservation } from "./schema";

const here = dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = resolve(here, "../../..");
const OUT = join(here, "..", "data", "observations.jsonl");
const SESSIONS_ROOT = join(homedir(), ".omp", "agent", "sessions");
const PROMPT_CAP = 2000;

type AnyRec = Record<string, any>;

/** `skill://name/…` → canonical skill name `name`. */
export function canonicalSkillName(path: string): string | null {
  if (!path.startsWith("skill://")) return null;
  const parts = path.slice("skill://".length).split("/");
  return (parts[0] === "agent" || parts[0] === "agents" ? parts[1] : parts[0]) || null;
}

/** Known skill names across all install locations — used to detect explicit `/name` invocations. */
function knownSkillNames(): Set<string> {
  const names = new Set<string>();
  const roots = [
    join(homedir(), ".agents", "skills"),
    join(homedir(), ".omp", "agent", "skills"),
    join(PROJECT_ROOT, "skills"),
    join(PROJECT_ROOT, ".omp", "skills"),
    join(PROJECT_ROOT, ".agents", "skills"),
    join(PROJECT_ROOT, ".agents", "skills", "unity-skills", "skills"),
    join(PROJECT_ROOT, ".claude", "skills"),
  ];
  for (const root of roots) {
    if (!existsSync(root)) continue;
    for (const entry of readdirSync(root, { withFileTypes: true })) {
      if (entry.name === "skills") continue; // meta-doc directory, not a routable skill
      if (entry.isDirectory() && existsSync(join(root, entry.name, "SKILL.md")))
        names.add(entry.name);
    }
  }
  return names;
}

/** Repo HEAD as of the session start, resolved from git history of the session cwd. */
function gitHeadAt(cwd: string, isoTimestamp: string): string | undefined {
  try {
    return (
      execFileSync(
        "git",
        ["-C", cwd, "log", "-1", "--format=%H", `--before=${isoTimestamp}`],
        { encoding: "utf8", timeout: 5000, stdio: ["ignore", "pipe", "ignore"] },
      ).trim() || undefined
    );
  } catch {
    return undefined;
  }
}

export function parseSessionFile(file: string, skills: Set<string>): SkillObservation | null {
  const lines = readFileSync(file, "utf8").split("\n").filter(Boolean);
  let session: AnyRec | null = null;
  let prompt: string | undefined;
  let explicit: string[] = [];
  const loaded = new Set<string>();
  const tools = new Set<string>();
  const modelCounts = new Map<string, number>();

  for (const line of lines) {
    let ev: AnyRec;
    try {
      ev = JSON.parse(line);
    } catch {
      continue;
    }
    if (ev.type === "session") {
      session = ev;
    } else if (ev.type === "custom" && ev.customType === "tool_execution_start") {
      const d = ev.data ?? {};
      if (d.toolName) tools.add(d.toolName);
      const skill = d.toolName === "read" && d.args?.path ? canonicalSkillName(d.args.path) : null;
      if (skill) loaded.add(skill);
    } else if (ev.type === "message" && ev.message?.role === "user") {
      const texts: string[] = (ev.message.content ?? [])
        .filter((c: AnyRec) => c.type === "text" && typeof c.text === "string")
        .map((c: AnyRec) => c.text);
      for (const text of texts) {
        if (prompt === undefined && !text.startsWith("[Image")) {
          prompt = text.slice(0, PROMPT_CAP);
        }
        for (const name of skills) {
          if (new RegExp(`(?<![\\w/-])/${name}\\b`).test(text) && !explicit.includes(name))
            explicit.push(name);
        }
      }
    } else if (ev.type === "message" && ev.message?.role === "assistant") {
      const m: string | undefined = ev.message.model;
      if (m) modelCounts.set(m, (modelCounts.get(m) ?? 0) + 1);
    }
  }
  if (!session || prompt === undefined) return null;
  const model = [...modelCounts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0];
  return {
    id: session.id,
    timestamp: session.timestamp,
    prompt,
    title: session.title,
    repo: session.cwd ? basename(session.cwd) : undefined,
    commit: session.cwd ? gitHeadAt(session.cwd, session.timestamp) : undefined,
    model,
    explicitlyInvokedSkills: explicit.length ? explicit : undefined,
    loadedSkills: [...loaded],
    toolsUsed: [...tools],
  };
}

export function importSessions(options: {
  sessionsRoot?: string; output?: string; all?: boolean; force?: boolean; skills?: Set<string>;
} = {}): number {
  const sessionsRoot = options.sessionsRoot ?? SESSIONS_ROOT;
  const output = options.output ?? OUT;
  const existing: SkillObservation[] = existsSync(output)
    ? readFileSync(output, "utf8").split("\n").filter(Boolean).map(line => JSON.parse(line))
    : [];
  if (!existsSync(sessionsRoot)) return existing.length;
  const byId = new Map(existing.map(observation => [observation.id, observation]));
  const skills = options.skills ?? knownSkillNames();
  for (const projectDir of readdirSync(sessionsRoot, { withFileTypes: true })) {
    if (!projectDir.isDirectory()) continue;
    if (!options.all && !projectDir.name.includes("SlopArena")) continue;
    const dir = join(sessionsRoot, projectDir.name);
    for (const file of readdirSync(dir)) {
      if (!file.endsWith(".jsonl")) continue;
      const observation = parseSessionFile(join(dir, file), skills);
      if (!observation) continue;
      const previous = byId.get(observation.id);
      if (previous && !options.force) continue;
      // Re-import measured fields without discarding manually assigned labels/notes.
      byId.set(observation.id, { ...previous, ...observation });
    }
  }
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, [...byId.values()].map(observation => JSON.stringify(observation)).join("\n") + "\n");
  return byId.size;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (!existsSync(SESSIONS_ROOT)) {
    console.log("No OMP session directory found; no observations changed.");
  } else {
    const args = process.argv.slice(2);
    console.log(`wrote ${importSessions({ all: args.includes("--all"), force: args.includes("--force") })} observation(s)`);
  }
}
