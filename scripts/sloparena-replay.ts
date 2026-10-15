import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { lstat, mkdir, readFile, realpath, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

type Json = Record<string, any>;
export type Step = { ticks: number; [key: string]: unknown };
export type Scenario = {
  version: 1; stage: string; fighter: string; inputs: string;
  captures?: { tick: number; name: string; camera?: { yaw: number; pitch: number } }[];
  expect?: { completed?: boolean; targets?: number; falls?: number; checkpoints?: number };
};
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const prefixes = ['tmp/', '.ability-lab-cache/', '.impeccable/', 'client/Unity/Temp/'];
const edges = ['jumpPressed', 'downPressed', 'shieldPressed', 'grabPressed', 'faceToCamera', 'toggleLock', 'retargetPressed', 'slot'];
const boolFields = [...edges.filter(x => x !== 'slot'), 'jumpHeld', 'downHeld', 'shieldHeld', 'isAiming'];
const integerRanges: Record<string, [number, number]> = {
  facingYaw: [-18000, 18000], aimYaw: [-18000, 18000], aimPitch: [-9000, 9000], aimDistance: [0, 6500],
};
const maxTicks = 36000;
function object(value: unknown, label: string): Json {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} must be an object.`);
  return value as Json;
}
function fields(value: Json, allowed: string[], label: string) {
  for (const key of Object.keys(value)) if (!allowed.includes(key)) throw new Error(`Unknown ${label} field '${key}'.`);
}
function integer(value: unknown, min: number, max: number, label: string) {
  if (!Number.isSafeInteger(value) || Number(value) < min || Number(value) > max) throw new Error(`${label} must be an integer from ${min} to ${max}.`);
}
function finite(value: unknown, min: number, max: number, label: string) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max) throw new Error(`${label} must be a number from ${min} to ${max}.`);
}
const hash = (value: string) => createHash('sha256').update(value).digest('hex');

export function validateSteps(value: unknown): Step[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > maxTicks) throw new Error('inputs must contain 1–36000 steps.');
  let total = 0;
  for (const item of value) {
    const step = object(item, 'input step');
    fields(step, ['ticks', 'moveX', 'moveY', ...boolFields, ...Object.keys(integerRanges), 'slot', 'targetEntityId', 'lockMode'], 'input');
    integer(step.ticks, 1, maxTicks, 'ticks'); total += step.ticks;
    for (const key of boolFields) if (key in step && typeof step[key] !== 'boolean') throw new Error(`${key} must be boolean.`);
    for (const key of ['moveX', 'moveY']) if (key in step) finite(step[key], -1, 1, key);
    if ((step.moveX ?? 0) ** 2 + (step.moveY ?? 0) ** 2 > 1.0001) throw new Error('Movement magnitude must be at most 1.');
    for (const [key, [min, max]] of Object.entries(integerRanges)) if (key in step) integer(step[key], min, max, key);
    if ('slot' in step && !['1', '2', '3', '4', 'A', 'E', 'R', 'F'].includes(step.slot)) throw new Error('slot must be a canonical input label.');
    if ('targetEntityId' in step && (typeof step.targetEntityId !== 'string' || !/^\d+$/.test(step.targetEntityId) || BigInt(step.targetEntityId) > 255n)) throw new Error('targetEntityId must be a decimal string from 0 to 255.');
    if ('lockMode' in step && (typeof step.lockMode !== 'string' || !['never', 'always', 'onhit'].includes(step.lockMode.toLowerCase()))) throw new Error('lockMode must be Never, Always or OnHit.');
  }
  if (total > maxTicks) throw new Error(`A replay may request at most ${maxTicks} ticks (10 minutes).`);
  return value as Step[];
}

export function validateScenario(value: unknown, totalTicks: number): Scenario {
  const scenario = object(value, 'scenario');
  fields(scenario, ['version', 'stage', 'fighter', 'inputs', 'captures', 'expect'], 'scenario');
  if (scenario.version !== 1) throw new Error('scenario.version must be 1.');
  if (typeof scenario.stage !== 'string' || !/^[a-z][a-z0-9_]{0,99}$/.test(scenario.stage)) throw new Error('stage must be a snake_case key.');
  if (typeof scenario.fighter !== 'string' || !/^[a-z0-9][a-z0-9._-]{0,99}$/.test(scenario.fighter)) throw new Error('fighter must be a package ID.');
  if (typeof scenario.inputs !== 'string' || !scenario.inputs.trim()) throw new Error('inputs must name a JSON file relative to the scenario.');
  const names = new Set();
  if ('captures' in scenario) {
    if (!Array.isArray(scenario.captures) || scenario.captures.length > 64) throw new Error('captures must contain at most 64 capture points.');
    for (const item of scenario.captures) {
      const capture = object(item, 'capture'); fields(capture, ['tick', 'name', 'camera'], 'capture');
      integer(capture.tick, 1, totalTicks, 'capture.tick');
      if (typeof capture.name !== 'string' || !/^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$/.test(capture.name) || names.has(capture.name)) throw new Error('Capture names must be unique file-safe labels.');
      names.add(capture.name);
      if ('camera' in capture) {
        const camera = object(capture.camera, 'camera'); fields(camera, ['yaw', 'pitch'], 'camera');
        finite(camera.yaw, -360, 360, 'camera.yaw'); finite(camera.pitch, 0, 45, 'camera.pitch');
      }
    }
  }
  if ('expect' in scenario) {
    const expected = object(scenario.expect, 'expect'); fields(expected, ['completed', 'targets', 'falls', 'checkpoints'], 'expect');
    if ('completed' in expected && typeof expected.completed !== 'boolean') throw new Error('expect.completed must be boolean.');
    for (const key of ['targets', 'falls', 'checkpoints']) if (key in expected) integer(expected[key], 0, key === 'falls' ? 255 : 127, `expect.${key}`);
  }
  return scenario as Scenario;
}

// Preserve held values, but never re-press a button/ability when a step is split.
export function sliceSteps(steps: Step[], start: number, end: number): Step[] {
  let tick = 0; const result: Step[] = [];
  for (const step of steps) {
    const next = tick + step.ticks;
    if (next > start && tick < end) {
      const part = { ...step, ticks: Math.min(next, end) - Math.max(tick, start) };
      if (start > tick) for (const key of edges) delete part[key];
      result.push(part);
    }
    tick = next; if (tick >= end) break;
  }
  return result;
}

export function planReplay(scenario: Scenario, steps: Step[], output: string, session: string) {
  const totalTicks = steps.reduce((sum, step) => sum + step.ticks, 0);
  const captures = [...(scenario.captures ?? [])].sort((a, b) => a.tick - b.tick);
  const stops = [...new Set([...captures.map(x => x.tick), totalTicks])].sort((a, b) => a - b);
  const commands: string[][] = [
    ['sloparena.replay.prepare', '--session', session, '--stage', scenario.stage, '--fighter', scenario.fighter, '--output', output],
    ['editor_play'], ['sloparena.ui.viewport', '--width', '1920', '--height', '1080', '--gizmos', 'off'],
  ];
  const chunks: Step[][] = []; let tick = 0;
  for (const stop of stops) {
    while (tick < stop) {
      const end = Math.min(tick + 600, stop);
      chunks.push(sliceSteps(steps, tick, end));
      commands.push(['sloparena.replay.run', '--session', session, '--chunk', String(chunks.length - 1), '--from-go', String(tick === 0)]);
      tick = end;
    }
    for (const capture of captures.filter(x => x.tick === stop)) {
      const command = ['sloparena.replay.capture', '--session', session, '--tick', String(stop), '--output', `${output}/${capture.name}.png`];
      if (capture.camera) command.push('--yaw', String(capture.camera.yaw), '--pitch', String(capture.camera.pitch));
      commands.push(command);
    }
  }
  commands.push(['get_console_logs', '--severity', 'error', '--limit', '20']);
  return { chunks, totalTicks, batch: { commands, restore: [['sloparena.replay.stop', '--session', session], ['sloparena.replay.restore', '--session', session]] } };
}

// Gateway stdout is a stream of pretty-printed JSON documents, not NDJSON.
export function parseJsonStream(text: string): Json[] {
  const values: Json[] = []; let start = -1, depth = 0, quoted = false, escaped = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (start < 0) { if (/\s/.test(c)) continue; if (c !== '{') throw new Error('Non-JSON gateway output.'); start = i; }
    if (quoted) { if (escaped) escaped = false; else if (c === '\\') escaped = true; else if (c === '"') quoted = false; }
    else if (c === '"') quoted = true;
    else if (c === '{' || c === '[') depth++;
    else if (c === '}' || c === ']') { if (--depth === 0) { values.push(JSON.parse(text.slice(start, i + 1))); start = -1; } }
  }
  if (start >= 0) throw new Error('Incomplete gateway JSON; command/restoration completion is uncertain.');
  return values;
}

export function summarize(scenario: Scenario, documents: Json[], code: number | null) {
  const receipts = documents.filter(x => x.data?.command).map(x => ({ command: x.data.command, result: x.data.result, success: x.success && x.data.success }));
  const runs = receipts.filter(x => x.command === 'sloparena.replay.run');
  const captures = receipts.filter(x => x.command === 'sloparena.replay.capture');
  const state = runs.at(-1)?.result?.state;
  const restoration = receipts.findLast(x => x.command === 'sloparena.replay.restore')?.result;
  const ownership = documents.findLast(x => typeof x.state === 'string' && !x.data);
  const observed: Json = { completed: state ? state.phase === 'Completed' : undefined, targets: state?.brokenTargets, falls: state?.fighter?.Deaths,
    checkpoints: typeof state?.checkpoint === 'number' ? state.checkpoint + 1 : undefined };
  const checks = Object.entries(scenario.expect ?? {}).map(([field, expected]) => ({ field, expected, actual: observed[field] ?? null, passed: Boolean(state) && observed[field] === expected }));
  const failures: string[] = [];
  if (code !== 0) failures.push(`Gateway exited ${code ?? 'without an exit code'}; inspect gateway logs.`);
  if (documents.some(x => x.success === false) || receipts.some(x => !x.success || x.result?.success === false)) failures.push('A native command failed.');
  for (const doc of documents) for (const error of doc.errors ?? []) if (typeof error.message === 'string') failures.push(error.message);
  if (!state) failures.push('No replay state was observed.');
  if (!restoration?.restored) failures.push('Editor restoration was not confirmed; inspect the gateway ownership receipt before retrying.');
  if (!ownership?.success || !['free', 'held'].includes(ownership.state)) failures.push('Settled gateway release was not confirmed.');
  if (captures.length !== (scenario.captures?.length ?? 0)) failures.push('Not all requested captures completed.');
  if (captures.some(x => x.result?.capture?.freshness !== 'observed-game-render')) failures.push('A capture lacks fresh rendered-frame evidence.');
  for (const check of checks) if (!check.passed) failures.push(`${check.field}: expected ${check.expected}, observed ${check.actual}.`);
  return { success: failures.length === 0, failures, checks, state: state ?? null,
    provenance: runs.find(x => x.result?.provenance)?.result.provenance ?? null,
    completedInputTicks: runs.reduce((sum, x) => sum + (x.result?.result?.completedTicks ?? 0), 0),
    events: runs.flatMap(x => x.result?.result?.events ?? []), hits: runs.flatMap(x => x.result?.result?.hits ?? []),
    console: receipts.findLast(x => x.command === 'get_console_logs')?.result ?? null,
    captures: captures.map(x => x.result), restoration: restoration ?? null,
    gateway: ownership ?? null };
}

type GatewayResult = { code: number | null; stdout: string; stderr: string };
type Gateway = (args: string[], cwd: string) => Promise<GatewayResult>;
async function gateway(args: string[], cwd: string): Promise<GatewayResult> {
  return new Promise((accept, reject) => {
    const child = spawn(process.execPath, [resolve(root, 'scripts/unity-editor-gateway.ts'), ...args], { cwd, env: process.env, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '', stderr = '', interrupted = false;
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; process.stderr.write(chunk); });
    const interrupt = (signal: NodeJS.Signals) => { if (!interrupted) { interrupted = true; child.kill(signal); } };
    const onInt = () => interrupt('SIGINT'), onTerm = () => interrupt('SIGTERM');
    process.on('SIGINT', onInt); process.on('SIGTERM', onTerm);
    const cleanup = () => { process.off('SIGINT', onInt); process.off('SIGTERM', onTerm); };
    child.once('error', error => { cleanup(); reject(error); });
    child.once('close', code => { cleanup(); accept({ code, stdout, stderr }); });
  });
}

export async function runReplay(options: { scenario: string; output?: string; projectPath?: string }, invoke: Gateway = gateway) {
  const scenarioPath = await realpath(resolve(options.scenario));
  const scenarioText = await readFile(scenarioPath, 'utf8'); const raw = object(JSON.parse(scenarioText), 'scenario');
  if (typeof raw.inputs !== 'string' || !raw.inputs.trim()) throw new Error('inputs must name a JSON file.');
  const inputsPath = await realpath(resolve(dirname(scenarioPath), raw.inputs));
  const inputsText = await readFile(inputsPath, 'utf8'); const steps = validateSteps(JSON.parse(inputsText));
  const scenario = validateScenario(raw, steps.reduce((sum, x) => sum + x.ticks, 0));
  const project = await realpath(resolve(options.projectPath ?? resolve(root, 'client/Unity')));
  await readFile(resolve(project, 'ProjectSettings/ProjectVersion.txt')); await realpath(resolve(project, 'Assets'));
  const editorRoot = await realpath(resolve(project, '../..'));
  const session = randomUUID().replaceAll('-', '');
  const output = options.output ?? `tmp/replays/${scenario.stage}-${Date.now()}-${session.slice(0, 8)}`;
  if (isAbsolute(output) || output.includes('\\') || output.split('/').some(x => !x || x === '.' || x === '..') || !prefixes.some(x => output.startsWith(x))) throw new Error('output must be a fresh directory below tmp/, .ability-lab-cache/, .impeccable/ or client/Unity/Temp/.');
  const destination = resolve(editorRoot, output); const parent = dirname(destination);
  let component = editorRoot;
  for (const part of output.split('/')) {
    component = resolve(component, part);
    try { if ((await lstat(component)).isSymbolicLink()) throw new Error('output must not traverse symlinks.'); }
    catch (error: any) { if (error.code !== 'ENOENT') throw error; }
  }
  await mkdir(parent, { recursive: true });
  const canonicalParent = await realpath(parent);
  if (relative(editorRoot, canonicalParent).startsWith(`..${sep}`) || relative(editorRoot, canonicalParent) === '..' || isAbsolute(relative(editorRoot, canonicalParent))) throw new Error('output escapes the Editor checkout.');
  // Also reject a permitted-prefix symlink into Assets or a different subtree.
  if (!prefixes.some(prefix => relative(editorRoot, canonicalParent).replaceAll(sep, '/').concat('/').startsWith(prefix))) throw new Error('output parent resolves outside an allowed artifact prefix.');
  await mkdir(destination); // Never overwrite evidence from an earlier run.
  const plan = planReplay(scenario, steps, output, session);
  await writeFile(resolve(destination, 'chunks.json'), JSON.stringify(plan.chunks));
  await writeFile(resolve(destination, 'batch.json'), JSON.stringify(plan.batch, null, 2));
  const metadata = { version: 1, scenario: scenarioPath, scenarioHash: hash(scenarioText), inputs: inputsPath,
    inputsHash: hash(inputsText), stage: scenario.stage, fighter: scenario.fighter, requestedTicks: plan.totalTicks,
    project, sourceCheckout: root, output: destination, session };
  await writeFile(resolve(destination, 'request.json'), JSON.stringify({ ...metadata, definition: scenario }, null, 2));
  let report: Json;
  try {
    const result = await invoke(['--project-path', project, '--checkout-path', root, '--batch', resolve(destination, 'batch.json')], root);
    await writeFile(resolve(destination, 'gateway.stdout.json'), result.stdout); await writeFile(resolve(destination, 'gateway.stderr.log'), result.stderr);
    report = { ...metadata, ...summarize(scenario, parseJsonStream(result.stdout), result.code) };
  } catch (error) {
    report = { ...metadata, success: false, failures: [String(error)], restoration: null,
      recovery: 'Completion/restoration is unconfirmed. Inspect gateway logs and status; do not automatically replay.' };
  }
  await writeFile(resolve(destination, 'report.json'), JSON.stringify(report, null, 2) + '\n');
  return report;
}

async function main() {
  const { values } = parseArgs({ options: { scenario: { type: 'string' }, output: { type: 'string' }, 'project-path': { type: 'string' }, help: { type: 'boolean' } } });
  if (values.help) { console.log('bun scripts/sloparena-replay.ts --scenario <scenario.json> [--output <fresh-cache-directory>] [--project-path <canonical-Unity-project>]'); return; }
  if (!values.scenario) throw new Error('--scenario is required.');
  const report = await runReplay({ scenario: values.scenario, output: values.output, projectPath: values['project-path'] });
  console.log(JSON.stringify(report, null, 2)); if (!report.success) process.exitCode = 1;
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main().catch(error => {
  console.log(JSON.stringify({ success: false, failures: [error.message] }, null, 2)); process.exitCode = 1;
});
