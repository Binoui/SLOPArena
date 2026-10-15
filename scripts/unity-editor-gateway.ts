import { createServer, request as httpRequest, type IncomingMessage, type ServerResponse } from 'node:http';
import { randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { mkdtemp, readFile, realpath, rm, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { parseArgs } from 'node:util';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createInterface } from 'node:readline/promises';

const DESCRIPTOR = '.unity-pipeline-port';
const RUNTIME_DESCRIPTOR = '.unity-pipeline-runtime-port';
type JsonObject = Record<string, unknown>;
type EditorDescriptor = { pid: number; port: number; projectPath: string; evalToken: string; unityVersion?: string };
type Identity = { terminalHandle: string; incarnationId: string };
type RunOptions = { unityPath?: string; identity?: Identity; orcaPath?: string; pollInterval?: number };
type GatewayStatus = { success: boolean; state: 'free' | 'held' | 'releasing' | 'blocked'; settled: boolean; editorSessionId: string | null; owner: JsonObject | null; activeOperations: number | null; operation: string | null; reason: string | null };

function object(value: unknown): JsonObject {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Expected a JSON object.');
  return value as JsonObject;
}
function parseDescriptor(value: unknown, project: string): EditorDescriptor {
  const d = object(value);
  if (!Number.isSafeInteger(d.port) || (d.port as number) < 1 || (d.port as number) > 65535 || !Number.isSafeInteger(d.pid) || (d.pid as number) < 1 || typeof d.evalToken !== 'string' || !d.evalToken || d.projectPath !== project) {
    throw new Error('The canonical project Pipeline descriptor is invalid or targets a different project.');
  }
  return { pid: d.pid as number, port: d.port as number, projectPath: project, evalToken: d.evalToken, unityVersion: typeof d.unityVersion === 'string' ? d.unityVersion : undefined };
}
async function descriptor(project: string): Promise<EditorDescriptor> {
  const path = join(project, 'Library', 'Pipeline', DESCRIPTOR);
  try { return parseDescriptor(JSON.parse(await readFile(path, 'utf8')), project); }
  catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') throw Object.assign(new Error(`No Pipeline Editor descriptor exists for ${project}.`), { code: 'ENOENT' });
    if (error instanceof SyntaxError) throw new Error('The canonical project Pipeline descriptor is unreadable.');
    throw error;
  }
}
function redact(text: string, ...secrets: string[]): string {
  return secrets.filter(Boolean).reduce((value, secret) => value.split(secret).join('[REDACTED]'), text);
}
function strings(value: JsonObject, keys: string[]): JsonObject {
  const result: JsonObject = {};
  for (const key of keys) if (typeof value[key] === 'string') result[key] = value[key];
  return result;
}
function safeStatus(raw: unknown): GatewayStatus {
  const s = object(raw);
  const state = ['free', 'held', 'releasing', 'blocked'].includes(String(s.state)) ? s.state as GatewayStatus['state'] : 'blocked';
  const rawOwner = s.owner && typeof s.owner === 'object' && !Array.isArray(s.owner) ? s.owner as JsonObject : null;
  const owner = rawOwner ? strings(rawOwner, ['terminalHandle', 'incarnationId', 'batchId']) : null;
  const activeOperations = Number.isSafeInteger(s.activeOperations) && (s.activeOperations as number) >= 0 ? s.activeOperations as number : null;
  return {
    success: s.success === true, state, settled: s.settled === true,
    editorSessionId: typeof s.editorSessionId === 'string' ? s.editorSessionId : null,
    owner, activeOperations, operation: typeof s.operation === 'string' ? s.operation : null,
    reason: typeof s.reason === 'string' ? s.reason : null,
  };
}
async function api(d: EditorDescriptor, method: string, path: string, body?: unknown, ownershipToken?: string, timeoutMs = 15_000, signal?: AbortSignal): Promise<{ status: number; value: JsonObject }> {
  const response = await fetch(`http://127.0.0.1:${d.port}${path}`, {
    method, headers: { authorization: `Bearer ${d.evalToken}`, ...(body === undefined ? {} : { 'content-type': 'application/json' }), ...(ownershipToken ? { 'X-Editor-Ownership': ownershipToken } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body), signal: signal ? AbortSignal.any([AbortSignal.timeout(timeoutMs), signal]) : AbortSignal.timeout(timeoutMs),
  });
  let value: JsonObject;
  try { value = object(await response.json()); } catch { throw new Error(`Pipeline ${method} ${path} returned a non-JSON response (HTTP ${response.status}).`); }
  return { status: response.status, value };
}
async function identityFromOrca(handle: string, orcaPath = process.env.ORCA_CLI_COMMAND || 'orca-ide', signal?: AbortSignal): Promise<Identity> {
  if (!handle.trim()) throw new Error('ORCA_TERMINAL_HANDLE is required; caller identity is never inferred from the active pane.');
  if (signal?.aborted) throw new Error('Orca identity lookup was interrupted.');
  const args = ['terminal', 'show', '--terminal', handle, '--json'];
  const child = spawn(orcaPath, args, { stdio: ['ignore', 'pipe', 'ignore'] });
  let stdout = '';
  child.stdout.setEncoding('utf8').on('data', chunk => { stdout += chunk; });
  const { promise, resolve: finish, reject } = Promise.withResolvers<void>();
  child.once('error', reject);
  child.once('close', code => code === 0 ? finish() : reject(new Error('Orca identity lookup failed.')));
  const abort = () => child.kill();
  signal?.addEventListener('abort', abort, { once: true });
  if (signal?.aborted) abort();
  const timeout = setTimeout(() => { child.kill(); reject(new Error('Orca identity lookup timed out.')); }, 10_000);
  try { await promise; } catch { throw new Error('Could not confirm this Orca terminal handle and incarnation.'); } finally { clearTimeout(timeout); signal?.removeEventListener('abort', abort); }
  let root: JsonObject;
  try { root = object(JSON.parse(stdout)); } catch { throw new Error('Orca returned an invalid terminal identity response.'); }
  const result = root.result && typeof root.result === 'object' && !Array.isArray(root.result) ? root.result as JsonObject : {};
  const terminal = result.terminal && typeof result.terminal === 'object' && !Array.isArray(result.terminal) ? result.terminal as JsonObject : {};
  if (root.ok !== true || typeof terminal.handle !== 'string' || !terminal.handle || terminal.handle !== handle || typeof terminal.incarnationId !== 'string' || !terminal.incarnationId || terminal.orphaned === true) {
    throw new Error('Orca terminal identity does not match this caller.');
  }
  return { terminalHandle: terminal.handle, incarnationId: terminal.incarnationId };
}

// Caller identity is configured or runtime-attested, never inferred from an active pane.
// Precedence: a verified Orca terminal; an explicit launcher pair for any agent; then a
// runtime-injected agent session (one adapter per agent runtime). The T3 harness hosts
// several runtimes, so its markers only select the `t3:` namespace for Codex threads.
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function hasT3Harness(env: NodeJS.ProcessEnv): boolean {
  return Boolean(env.T3_MCP_BEARER_TOKEN || env.T3CODE_CLI_PATH?.trim());
}

export function identityFromT3(env: NodeJS.ProcessEnv): Identity {
  const thread = env.CODEX_THREAD_ID;
  const session = env.CODEX_SESSION_ID;
  if (!hasT3Harness(env) || !thread || !session || !UUID.test(thread) || !UUID.test(session))
    throw new Error('T3 caller requires runtime-injected CODEX_THREAD_ID and CODEX_SESSION_ID in the T3 harness.');
  return { terminalHandle: `t3:${thread.toLowerCase()}`, incarnationId: session.toLowerCase() };
}

export function identityFromCodex(env: NodeJS.ProcessEnv): Identity {
  const thread = env.CODEX_THREAD_ID, session = env.CODEX_SESSION_ID;
  if (!thread || !session || !UUID.test(thread) || !UUID.test(session))
    throw new Error('Codex caller requires runtime-injected CODEX_THREAD_ID and CODEX_SESSION_ID.');
  return { terminalHandle: `codex:${thread.toLowerCase()}`, incarnationId: session.toLowerCase() };
}

export function identityFromClaude(env: NodeJS.ProcessEnv): Identity {
  const session = env.CLAUDE_CODE_SESSION_ID;
  if (!session || !UUID.test(session))
    throw new Error('Claude Code caller requires a runtime-injected UUID CLAUDE_CODE_SESSION_ID.');
  return { terminalHandle: `claude:${session.toLowerCase()}`, incarnationId: session.toLowerCase() };
}

export function identityFromLauncher(env: NodeJS.ProcessEnv): Identity {
  const agent = env.SLOPARENA_AGENT_ID, session = env.SLOPARENA_AGENT_SESSION_ID;
  if (!agent || !/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$/.test(agent) || !session || !UUID.test(session))
    throw new Error('Launcher caller requires SLOPARENA_AGENT_ID and a UUID SLOPARENA_AGENT_SESSION_ID assigned to this agent session.');
  return { terminalHandle: `agent:${agent}`, incarnationId: session.toLowerCase() };
}

// Add another agent runtime here: detect its own injected variables, then validate them.
const AGENT_RUNTIMES: { present: (env: NodeJS.ProcessEnv) => boolean; identity: (env: NodeJS.ProcessEnv) => Identity }[] = [
  { present: env => env.CODEX_THREAD_ID !== undefined || env.CODEX_SESSION_ID !== undefined,
    identity: env => hasT3Harness(env) ? identityFromT3(env) : identityFromCodex(env) },
  { present: env => env.CLAUDE_CODE_SESSION_ID !== undefined, identity: identityFromClaude },
];

export async function identityFromEnvironment(env: NodeJS.ProcessEnv, orcaPath?: string, signal?: AbortSignal): Promise<Identity> {
  if (env.ORCA_TERMINAL_HANDLE !== undefined)
    return identityFromOrca(env.ORCA_TERMINAL_HANDLE, orcaPath, signal);
  if (env.SLOPARENA_AGENT_ID !== undefined || env.SLOPARENA_AGENT_SESSION_ID !== undefined)
    return identityFromLauncher(env);
  const runtime = AGENT_RUNTIMES.find(candidate => candidate.present(env));
  if (runtime) return runtime.identity(env);
  if (hasT3Harness(env))
    throw new Error('T3 caller requires runtime-injected agent session IDs (Codex or Claude Code) or a launcher-configured SLOPARENA_AGENT_ID/SLOPARENA_AGENT_SESSION_ID pair.');
  throw new Error('A verified Orca identity, runtime agent session identity (Codex or Claude Code), or launcher-configured agent session identity is required. See docs/contributing/unity-editor-coordination.md.');
}

async function callerIdentity(orcaPath?: string, signal?: AbortSignal): Promise<Identity> {
  return identityFromEnvironment(process.env, orcaPath, signal);
}
function equalSecret(actual: string | undefined, expected: string): boolean {
  if (!actual) return false;
  const a = Buffer.from(actual), b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

function listen(getTarget: () => EditorDescriptor, ownershipToken: string, aliasToken: string) {
  const server = createServer((incoming: IncomingMessage, outgoing: ServerResponse) => {
    const url = incoming.url || '';
    const pathname = url.split('?', 1)[0];
    const methods: Record<string, string> = { '/api/status': 'GET', '/api/editor_status': 'GET', '/api/commands': 'GET', '/api/exec': 'POST', '/api/progress': 'GET', '/api/test-status': 'GET', '/api/job': 'GET', '/api/job/cancel': 'POST' };
    if (methods[pathname] !== incoming.method || !/^\/api\/[a-zA-Z0-9_/-]+(?:\?[^#\r\n]*)?$/.test(url)) {
      outgoing.writeHead(404).end(); return;
    }
    if (!equalSecret(incoming.headers.authorization?.replace(/^Bearer\s+/i, ''), aliasToken)) { outgoing.writeHead(401).end(); return; }
    const target = getTarget();
    const headers: Record<string, string | number> = { authorization: `Bearer ${target.evalToken}`, 'X-Editor-Ownership': ownershipToken };
    if (incoming.headers['content-type']) headers['content-type'] = incoming.headers['content-type'];
    if (incoming.headers['content-length']) headers['content-length'] = incoming.headers['content-length'];
    const upstream = httpRequest({ hostname: '127.0.0.1', port: target.port, method: incoming.method, path: url, headers }, response => {
      outgoing.writeHead(response.statusCode || 502, { 'content-type': response.headers['content-type'] || 'application/json' });
      response.pipe(outgoing);
    });
    upstream.on('error', () => { if (!outgoing.headersSent) outgoing.writeHead(502, { 'content-type': 'application/json' }); outgoing.end('{"error":"Pipeline forwarding failed"}'); });
    incoming.pipe(upstream);
  });
  server.on('clientError', (_error, socket) => socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n'));
  const { promise, resolve: resolveListen, reject } = Promise.withResolvers<{ server: ReturnType<typeof createServer>; port: number }>();
  server.once('error', reject);
  server.listen(0, '127.0.0.1', () => {
    server.removeListener('error', reject);
    const addr = server.address();
    if (!addr || typeof addr === 'string') { server.close(); reject(new Error('Could not create private forwarding listener.')); return; }
    resolveListen({ server, port: addr.port });
  });
  return promise;
}
type PackageReceiptTarget = { operation: string; argument: string; operationId: string };
function semanticFailure(command: string, output: string, packageReceipt?: PackageReceiptTarget): boolean {
  let packageStatusObserved = false;
  const values: unknown[] = [];
  try { values.push(JSON.parse(output)); } catch {}
  for (const line of output.split(/\r?\n/)) {
    try { values.push(JSON.parse(line)); } catch {}
  }
  const failedTests = (value: unknown): boolean => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const root = value as JsonObject;
    const summary = root.summary ?? root.Summary;
    if (!summary || typeof summary !== 'object' || Array.isArray(summary)) return false;
    const count = (summary as JsonObject).failed ?? (summary as JsonObject).Failed;
    return typeof count === 'number' && count > 0;
  };
  for (const value of values) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) continue;
    const root = value as JsonObject;
    const data = root.data && typeof root.data === 'object' && !Array.isArray(root.data) ? root.data as JsonObject : {};
    const result = data.result;
    if (result && typeof result === 'object' && !Array.isArray(result)) {
      if ((result as JsonObject).success === false || (command === 'run_tests' && failedTests(result))) return true;
    }
    if ((command === 'test_status' || command === 'package_status') && typeof result === 'string') {
      if (command === 'package_status') packageStatusObserved = true;
      try {
        const status = object(JSON.parse(result));
        if (status.status === 'error' || status.status === 'cancelled' || status.status === 'canceled'
          || status.status === 'failed' || status.status === 'unknown' || status.success === false
          || (status.status === 'completed' && (failedTests(status) || (command === 'package_status' && status.success !== true)))) return true;
        if (packageReceipt && (status.status !== 'completed'
          || status.operation !== packageReceipt.operation || status.argument !== packageReceipt.argument
          || status.operationId !== packageReceipt.operationId
          || (status.completionEvidence !== 'upm_request' && status.completionEvidence !== 'resolved_package_state'))) return true;
      } catch { if (command === 'package_status') return true; }
    }
    if ((command === 'recompile' || command === 'recompile_status') && typeof result === 'string') {
      try { if (object(JSON.parse(result)).failed === true) return true; } catch {}
    }
  }
  return command === 'package_status' && !packageStatusObserved;
}
function nativeRunning(command: string, output: string): boolean {
  if (command !== 'run_tests') return false;
  try {
    const root = object(JSON.parse(output));
    const data = object(root.data);
    return !!data.result && typeof data.result === 'object' && !Array.isArray(data.result)
      && ((data.result as JsonObject).result === 'running' || (data.result as JsonObject).result === 'playmode_running');
  } catch { return false; }
}
function packageFollowup(command: string, output: string): PackageReceiptTarget | null | undefined {
  if (command !== 'package_add' && command !== 'package_remove') return;
  try {
    const result = object(object(object(JSON.parse(output)).data).result);
    if (result.status !== 'in_progress') return;
    if (result.applied !== true || typeof result.operation !== 'string' || typeof result.argument !== 'string'
      || typeof result.operationId !== 'string' || !result.operationId) return null;
    return { operation: result.operation, argument: result.argument, operationId: result.operationId };
  } catch { return null; }
}
function runUnity(binary: string, args: string[], runtimePath: string, secrets: string[], packageReceipt?: PackageReceiptTarget): Promise<{ code: number; signal: NodeJS.Signals | null; semanticFailure: boolean; asyncTests: boolean; packageOperation: PackageReceiptTarget | null | undefined }> {
  const { promise, resolve: resolveRun, reject } = Promise.withResolvers<{ code: number; signal: NodeJS.Signals | null; semanticFailure: boolean; asyncTests: boolean; packageOperation: PackageReceiptTarget | null | undefined }>();
  const child = spawn(binary, ['command', '--runtime-path', runtimePath, '--format', 'json', '--no-banner', '--no-pager', '--non-interactive', '--proxy-disable', ...args], {
    stdio: ['inherit', 'pipe', 'pipe'], env: { ...process.env, UNITY_PROXY: '', UNITY_FORMAT: 'json', UNITY_NO_BANNER: '1', UNITY_NO_PAGER: '1' },
  });
  let out = '', err = '';
  child.stdout.setEncoding('utf8').on('data', chunk => { out += chunk; });
  child.stderr.setEncoding('utf8').on('data', chunk => { err += chunk; });
  child.once('error', reject);
  child.once('close', (code, signal) => {
    if (out) process.stdout.write(redact(out, ...secrets));
    if (err) process.stderr.write(redact(err, ...secrets));
    const command = args[0] || '';
    const packageOperation = packageFollowup(command, out);
    resolveRun({ code: code ?? 1, signal, semanticFailure: semanticFailure(command, out, packageReceipt) || packageOperation === null, asyncTests: nativeRunning(command, out), packageOperation });
  });
  return promise;
}
type Invocation = { mode: 'status' | 'preflight' | 'run'; projectArg: string; checkoutArg?: string; batchPath?: string; hold: boolean; commandArgs: string[] };
function validateInvocation(args: string[]): Invocation {
  const status = args[0] === 'status';
  const preflight = args[0] === 'preflight';
  const modeArgs = status || preflight ? args.slice(1) : args;
  const split = modeArgs.indexOf('--');
  if (status && split !== -1) throw new Error('Status does not accept a Unity command.');
  const flags = split < 0 ? modeArgs : modeArgs.slice(0, split);
  const commandArgs = split < 0 ? [] : modeArgs.slice(split + 1);
  const { values, positionals } = parseArgs({ args: flags, allowPositionals: true, options: { help: { type: 'boolean' }, 'project-path': { type: 'string' }, 'checkout-path': { type: 'string' }, batch: { type: 'string' }, hold: { type: 'boolean' } } });
  if (values.help) return { mode: 'status', projectArg: '', hold: false, commandArgs: ['__help__'] };
  if (positionals.length || !values['project-path']) throw new Error('Use --project-path <canonical-project>.');
  if (values['checkout-path'] !== undefined && !values['checkout-path'].trim()) throw new Error('--checkout-path must not be empty.');
  if (values.batch && status) throw new Error('Status cannot use --batch.');
  if (status && (values.hold || values['checkout-path'])) throw new Error('Status cannot use --hold or --checkout-path.');
  if (preflight && values.hold) throw new Error('Preflight cannot retain an ownership lease.');
  if (status) return { mode: 'status', projectArg: values['project-path'], hold: false, commandArgs: [] };
  if (preflight && split !== -1 && !commandArgs.length) throw new Error('Supply a Unity command after --.');
  if (preflight && values.batch && split !== -1) throw new Error('Use --batch or a Unity command plan, not both.');
  if (preflight) return { mode: 'preflight', projectArg: values['project-path'], checkoutArg: values['checkout-path'], batchPath: values.batch, hold: false, commandArgs };
  if (commandArgs.length && !commandArgs[0]) throw new Error('Supply a Unity command after --.');
  if (!values.batch && !commandArgs.length) throw new Error('Supply --batch <json-file> or a Unity command after --.');
  if (values.batch && commandArgs.length) throw new Error('Use --batch or a Unity command, not both.');
  if (commandArgs[0] === 'command' || (commandArgs[0] === 'pipeline' && commandArgs[1] === 'list')) throw new Error('Supply the Unity command directly after --; do not include the Unity CLI command or pipeline list prefix.');
  const forbidden = /^(?:--(?:project-path|checkout-path|runtime-path|runtime|proxy|proxy-disable|detach|format|json|no-banner|no-pager|non-interactive)(?:=|$))/;
  if (commandArgs.some(arg => forbidden.test(arg))) throw new Error('Unity connection, proxy, detach, and output flags are controlled by the gateway.');
  return { mode: 'run', projectArg: values['project-path'], checkoutArg: values['checkout-path'], batchPath: values.batch, hold: !!values.hold, commandArgs };
}
async function validateEditorProject(path: string): Promise<string> {
  const project = await realpath(resolve(path));
  try {
    if (!(await stat(join(project, 'Assets'))).isDirectory()
      || !(await stat(join(project, 'ProjectSettings', 'ProjectVersion.txt'))).isFile()) throw new Error();
  } catch {
    throw new Error(`Editor project ${project} is missing Assets/ or ProjectSettings/ProjectVersion.txt.`);
  }
  return project;
}
const SHARED_PROJECTS = ['src/Shared/SlopArena.Shared.csproj', 'tests/Shared.Tests/Shared.Tests.csproj'];
async function validateCheckout(path: string): Promise<string> {
  const checkout = await realpath(resolve(path));
  for (const relative of SHARED_PROJECTS) {
    try { await readFile(join(checkout, relative)); }
    catch { throw new Error(`Game checkout ${checkout} is missing ${relative}.`); }
  }
  return checkout;
}
function writeStatus(status: GatewayStatus, secrets: string[], stream: NodeJS.WriteStream = process.stdout): void {
  stream.write(`${redact(JSON.stringify(status, null, stream === process.stderr ? undefined : 2), ...secrets)}\n`);
}
async function sourceCheckout(project: string, explicit?: string): Promise<{ path: string | null; resolution: string }> {
  if (explicit !== undefined) return { path: await validateCheckout(explicit), resolution: 'explicit-validated' };
  const candidate = resolve(project, '..', '..');
  if (resolve(project, '..') !== resolve(candidate, 'client')) return { path: null, resolution: 'not-derivable' };
  try { return { path: await validateCheckout(candidate), resolution: 'derived-validated' }; }
  catch { return { path: null, resolution: 'not-derivable' }; }
}
async function batchPlan(invocation: Invocation): Promise<{ commands: string[][]; restore: string[][] }> {
  if (invocation.batchPath) return parseBatch(JSON.parse(await readFile(resolve(invocation.batchPath), 'utf8')));
  return { commands: invocation.commandArgs.length ? [invocation.commandArgs] : [], restore: [] };
}
function planStatusKey(status: GatewayStatus): string {
  return JSON.stringify([status.success, status.state, status.settled, status.editorSessionId, status.owner?.terminalHandle, status.owner?.incarnationId, status.owner?.batchId, status.operation, status.reason, status.activeOperations]);
}
type StatusReporter = (status: GatewayStatus) => void;
function statusReporter(secrets: string[], stream: NodeJS.WriteStream): StatusReporter {
  let previous: string | undefined;
  return status => {
    const key = planStatusKey(status);
    if (key === previous) return;
    previous = key;
    writeStatus(status, secrets, stream);
  };
}
function unityPlan(batch: { commands: string[][]; restore: string[][] }): string[][] {
  const commands = [...batch.commands, ...batch.restore];
  if (commands.some(command => command[0] === 'command' || (command[0] === 'pipeline' && command[1] === 'list'))) throw new Error('Batch commands must not include the Unity CLI command or pipeline list prefix.');
  for (const command of commands) if (command.some(arg => /^(?:--(?:project-path|checkout-path|runtime-path|runtime|proxy|proxy-disable|detach|format|json|no-banner|no-pager|non-interactive)(?:=|$))/.test(arg))) throw new Error('Batch contains a gateway-controlled Unity connection, proxy, detach, or output flag.');
  return commands;
}
function parseBatch(value: unknown): { commands: string[][]; restore: string[][] } {
  const input = object(value);
  const commands = input.commands;
  const restore = input.restore;
  const validCommands = (candidate: unknown): candidate is string[][] => Array.isArray(candidate) && candidate.length > 0 && candidate.every(row => Array.isArray(row) && row.length > 0 && row.every(arg => typeof arg === 'string' && arg.length > 0 && !arg.includes('\0')));
  if (!validCommands(commands)) throw new Error('Batch requires a non-empty commands array.');
  let restoration: string[][] = [];
  if (restore !== undefined) {
    if (!Array.isArray(restore) || !restore.every(row => validCommands([row]))) throw new Error('Batch restore must contain command argument arrays.');
    restoration = restore as string[][];
  }
  const commandsToCheck = [...commands, ...restoration];
  if (commandsToCheck.some(command => command[0] === 'command' || (command[0] === 'pipeline' && command[1] === 'list'))) throw new Error('Batch commands must not include the Unity CLI command or pipeline list prefix.');
  for (const command of commandsToCheck) if (command.some(arg => /^(?:--(?:project-path|runtime-path|runtime|proxy|proxy-disable|detach|format|json|no-banner|no-pager|non-interactive)(?:=|$))/.test(arg))) throw new Error('Batch contains a gateway-controlled Unity connection, proxy, detach, or output flag.');
  return { commands, restore: restoration };
}
function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  const { promise, resolve } = Promise.withResolvers<void>();
  let timer: NodeJS.Timeout | undefined;
  const done = () => {
    clearTimeout(timer);
    signal?.removeEventListener('abort', done);
    resolve();
  };
  if (signal?.aborted) { resolve(); return promise; }
  timer = setTimeout(done, ms);
  signal?.addEventListener('abort', done, { once: true });
  if (signal?.aborted) done();
  return promise;
}
type SettlementPhase = 'held' | 'release';
type SettlementResult = { status: GatewayStatus; descriptor: EditorDescriptor | null; ready: boolean };
function blockedStatus(reason: string, editorSessionId: string | null = null): GatewayStatus {
  return { success: false, state: 'blocked', settled: false, editorSessionId, owner: null, activeOperations: null, operation: null, reason };
}
function sameOwner(status: GatewayStatus, identity: Identity, batchId: string): boolean {
  const owner = status.owner;
  return !!owner && owner.terminalHandle === identity.terminalHandle && owner.incarnationId === identity.incarnationId && owner.batchId === batchId;
}
function differentLease(status: GatewayStatus, identity: Identity, batchId: string): boolean {
  const owner = status.owner;
  return !!owner && (owner.terminalHandle !== identity.terminalHandle || owner.incarnationId !== identity.incarnationId || (typeof owner.batchId === 'string' && owner.batchId !== batchId));
}
function processAlive(pid: number): boolean {
  try { process.kill(pid, 0); return true; }
  catch (error) { return (error as NodeJS.ErrnoException).code !== 'ESRCH'; }
}
function hasKnownOwner(owner: JsonObject | null): owner is JsonObject {
  return !!owner && typeof owner.terminalHandle === 'string' && owner.terminalHandle.length > 0
    && typeof owner.incarnationId === 'string' && owner.incarnationId.length > 0
    && typeof owner.batchId === 'string' && owner.batchId.length > 0;
}
function knownClaimConflict(raw: JsonObject, status: GatewayStatus): boolean {
  if (raw.success !== false || (raw.state !== 'held' && raw.state !== 'releasing') || typeof raw.editorSessionId !== 'string' || !raw.editorSessionId
    || typeof raw.settled !== 'boolean' || !Number.isSafeInteger(raw.activeOperations) || typeof raw.activeOperations !== 'number' || raw.activeOperations < 0
    || typeof raw.operation !== 'string' || !raw.operation.trim() || !hasKnownOwner(status.owner)) return false;
  if (status.state === 'held') return !status.settled || status.activeOperations === 0;
  return status.state === 'releasing' && !status.settled;
}
async function waitForClaimAvailability(project: string, original: EditorDescriptor, conflict: GatewayStatus, interval: number, signal: AbortSignal, report: StatusReporter): Promise<SettlementResult & { interrupted?: boolean }> {
  const sessionId = conflict.editorSessionId!;
  let disconnectedAt = 0;
  for (;;) {
    if (signal.aborted) return { status: conflict, descriptor: null, ready: false, interrupted: true };
    let current: EditorDescriptor;
    try { current = await descriptor(project); }
    catch {
      if (signal.aborted) return { status: conflict, descriptor: null, ready: false, interrupted: true };
      if (!processAlive(original.pid) || (disconnectedAt && Date.now() - disconnectedAt > 60_000)) return { status: blockedStatus('Canonical Editor descriptor is unavailable; ownership remains unverified.', sessionId), descriptor: null, ready: false };
      disconnectedAt ||= Date.now();
      await sleep(interval, signal); continue;
    }
    if (current.pid !== original.pid || !processAlive(original.pid)) {
      return { status: blockedStatus('Editor process changed or exited while waiting; ownership cannot be transferred.', sessionId), descriptor: current, ready: false };
    }
    let response: { status: number; value: JsonObject };
    try { response = await api(current, 'GET', '/api/editor-ownership', undefined, undefined, 1_500, signal); }
    catch {
      if (signal.aborted) return { status: conflict, descriptor: current, ready: false, interrupted: true };
      if (!processAlive(original.pid) || (disconnectedAt && Date.now() - disconnectedAt > 60_000)) return { status: blockedStatus('Editor ownership endpoint remained unreachable; ownership cannot be transferred.', sessionId), descriptor: current, ready: false };
      disconnectedAt ||= Date.now();
      await sleep(interval, signal); continue;
    }
    if (signal.aborted) return { status: conflict, descriptor: current, ready: false, interrupted: true };
    disconnectedAt = 0;
    const status = safeStatus(response.value);
    if (response.status !== 200 || status.editorSessionId !== sessionId) {
      return { status: blockedStatus('Editor session changed or ownership status is invalid; no claim retry is safe.', status.editorSessionId), descriptor: current, ready: false };
    }
    if (status.state === 'blocked') return { status, descriptor: current, ready: false };
    if (response.value.success !== true || typeof response.value.settled !== 'boolean' || !Number.isSafeInteger(response.value.activeOperations)
      || typeof response.value.activeOperations !== 'number' || response.value.activeOperations < 0
      || !(response.value.reason == null || typeof response.value.reason === 'string')) {
      return { status: blockedStatus('Editor ownership status is incomplete; no claim retry is safe.', sessionId), descriptor: current, ready: false };
    }
    if (status.state === 'free') {
      if (status.settled && status.activeOperations === 0 && response.value.owner == null) return { status, descriptor: current, ready: true };
      return { status: blockedStatus('Editor reported free before ownership was fully settled.', sessionId), descriptor: current, ready: false };
    }
    if ((status.state === 'held' || status.state === 'releasing') && hasKnownOwner(status.owner)
      && !(status.state === 'releasing' && status.settled) && !(status.settled && status.activeOperations !== 0)) {
      report(status);
      await sleep(interval, signal);
      continue;
    }
    return { status: blockedStatus('Editor ownership state is unknown or inconsistent; no claim retry is safe.', sessionId), descriptor: current, ready: false };
  }
}
async function waitForSettlement(project: string, original: EditorDescriptor, sessionId: string, identity: Identity, batchId: string, interval: number, phase: SettlementPhase, report: StatusReporter): Promise<SettlementResult> {
  let disconnectedAt = 0;
  for (;;) {
    let current: EditorDescriptor;
    try { current = await descriptor(project); }
    catch {
      if (!processAlive(original.pid) || (disconnectedAt && Date.now() - disconnectedAt > 60_000)) return { status: blockedStatus('Canonical Editor descriptor is unavailable; completion remains unverified.', sessionId), descriptor: null, ready: false };
      disconnectedAt ||= Date.now();
      await sleep(interval); continue;
    }
    if (current.pid !== original.pid || !processAlive(original.pid)) {
      return { status: blockedStatus('Editor process changed or exited; the previous session result cannot be inferred.', sessionId), descriptor: current, ready: false };
    }
    let response: { status: number; value: JsonObject };
    try { response = await api(current, 'GET', '/api/editor-ownership', undefined, undefined, 1_500); }
    catch {
      if (!processAlive(original.pid) || (disconnectedAt && Date.now() - disconnectedAt > 60_000)) return { status: blockedStatus('Editor ownership endpoint remained unreachable; completion remains unverified.', sessionId), descriptor: current, ready: false };
      disconnectedAt ||= Date.now();
      await sleep(interval); continue;
    }
    disconnectedAt = 0;
    const status = safeStatus(response.value);
    if (!status.success || status.editorSessionId !== sessionId) return { status: blockedStatus('Editor session changed or ownership status is invalid; the previous result cannot be inferred.', status.editorSessionId), descriptor: current, ready: false };
    if (status.state === 'blocked') return { status, descriptor: current, ready: false };
    if (phase === 'held') {
      if (status.state !== 'held' || !sameOwner(status, identity, batchId)) return { status: blockedStatus('The claimed Editor lease is no longer held by this caller batch.', sessionId), descriptor: current, ready: false };
      if (status.settled && status.activeOperations === 0) return { status, descriptor: current, ready: true };
    } else {
      if (status.state === 'free') return status.settled && status.activeOperations === 0 ? { status, descriptor: current, ready: true } : { status: blockedStatus('Editor reported free before all operations were settled.', sessionId), descriptor: current, ready: false };
      if ((status.state === 'held' || status.state === 'releasing') && differentLease(status, identity, batchId)) return { status, descriptor: current, ready: true };
      if (status.state !== 'releasing' || !sameOwner(status, identity, batchId)) return { status: blockedStatus('Release is not settling under the original Editor session.', sessionId), descriptor: current, ready: false };
    }
    report(status);
    await sleep(interval);
  }
}
async function holdUntilEnded(setEnd: (end?: () => void) => void): Promise<void> {
  const rl = createInterface({ input: process.stdin, output: process.stderr });
  const { promise, resolve } = Promise.withResolvers<void>();
  const close = () => { rl.close(); resolve(); };
  rl.once('line', resolve);
  rl.once('close', resolve);
  setEnd(close);
  process.stderr.write('Editor lease held. Press Enter to release.\n');
  try { await promise; } finally { setEnd(); rl.close(); }
}
export async function run(args = process.argv.slice(2), options: RunOptions = {}): Promise<void> {
  const unityPath = options.unityPath || process.env.UNITY_CLI_COMMAND || 'unity';
  const invocation = validateInvocation(args);
  if (invocation.commandArgs[0] === '__help__') { process.stdout.write('bun scripts/unity-editor-gateway.ts --project-path <canonical-project> [--checkout-path <game-checkout>] [--batch <json-file>] [--hold] -- <Unity-command-and-arguments>\nbun scripts/unity-editor-gateway.ts preflight --project-path <canonical-project> [--checkout-path <game-checkout>] [--batch <json-file> | -- <Unity-command-and-arguments>]\nbun scripts/unity-editor-gateway.ts status --project-path <canonical-project>\n'); return; }
  if (invocation.hold && !process.stdin.isTTY) throw new Error('--hold requires an interactive terminal.');
  const project = await validateEditorProject(invocation.projectArg);
  const source = await sourceCheckout(project, invocation.checkoutArg);
  const tooling = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  const plan = invocation.mode === 'status' ? null : await batchPlan(invocation);
  if (plan) unityPlan(plan);
  const runtimeState = async () => {
    let current: EditorDescriptor;
    try { current = await descriptor(project); }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      return { state: null, error: (error as Error).message, secret: null };
    }
    try {
      const response = await api(current, 'GET', '/api/editor-ownership');
      const status = safeStatus(response.value);
      if (response.status >= 400 || !status.success) return { state: null, error: `Pipeline ownership status was not successful (HTTP ${response.status}).`, secret: current.evalToken };
      return { state: status, error: null, secret: current.evalToken };
    } catch (error) {
      return { state: null, error: error instanceof Error ? error.message : 'Runtime status unavailable.', secret: current.evalToken };
    }
  };
  if (invocation.mode === 'preflight') {
    const runtime = await runtimeState();
    const context = { mode: 'preflight', ownership: 'not-acquired', roots: { tooling, source: source.path, sourceResolution: source.resolution, editor: project }, plan, recovery: runtime.state ? { availability: 'observed', ...runtime.state } : { availability: 'unknown', reason: runtime.error } };
    process.stdout.write(`${redact(JSON.stringify(context, null, 2), runtime.secret || '')}\n`);
    if (runtime.state?.state === 'blocked') process.exitCode = 2;
    return;
  }
  let activeDescriptor = await descriptor(project);
  const secrets = [activeDescriptor.evalToken];
  if (invocation.mode === 'status') {
    const response = await api(activeDescriptor, 'GET', '/api/editor-ownership');
    const status = safeStatus(response.value);
    writeStatus(status, secrets);
    if (response.status >= 400 || !status.success || status.state === 'blocked') process.exitCode = 2;
    return;
  }
  const runtime = await runtimeState();
  process.stderr.write(`${redact(JSON.stringify({ execution: { roots: { tooling, source: source.path, sourceResolution: source.resolution, editor: project }, plan, recovery: runtime.state ? { availability: 'observed', ...runtime.state } : { availability: 'unknown', reason: runtime.error } } }), runtime.secret || '')}\n`);
  let identity: Identity;
  let interruption: NodeJS.Signals | undefined;
  let endHold: (() => void) | undefined;
  const interruptionController = new AbortController();
  const onInterrupt = (signal: NodeJS.Signals) => {
    const first = !interruption;
    interruption ||= signal;
    interruptionController.abort();
    if (first) process.stderr.write(`Received ${signal}; waiting for current Editor work before cleanup.\n`);
    endHold?.();
  };
  const onSigint = () => onInterrupt('SIGINT');
  const onSigterm = () => onInterrupt('SIGTERM');
  const removeSignalHandlers = () => {
    process.off('SIGINT', onSigint);
    process.off('SIGTERM', onSigterm);
  };
  process.on('SIGINT', onSigint);
  process.on('SIGTERM', onSigterm);
  const reportTransition = statusReporter(secrets, process.stderr);
  const batch = plan!;
  const batchId = randomUUID();
  const interval = options.pollInterval ?? 500;
  try {
    identity = options.identity || await callerIdentity(options.orcaPath, interruptionController.signal);
    if (!identity.terminalHandle || !identity.incarnationId) throw new Error('Caller handle and incarnation are required.');
  } catch (error) {
    removeSignalHandlers();
    if (interruption) { process.exitCode = interruption === 'SIGTERM' ? 143 : 130; return; }
    throw error;
  }
  if (interruption) { process.exitCode = interruption === 'SIGTERM' ? 143 : 130; removeSignalHandlers(); return; }
  let claim: { status: number; value: JsonObject };
  let waitingSessionId: string | null = null;
  try {
    for (;;) {
      if (interruption) { process.exitCode = interruption === 'SIGTERM' ? 143 : 130; removeSignalHandlers(); return; }
      try { claim = await api(activeDescriptor, 'POST', '/api/editor-ownership/claim', { owner: identity, batchId }); }
      catch {
        removeSignalHandlers();
        try { writeStatus(safeStatus((await api(activeDescriptor, 'GET', '/api/editor-ownership')).value), secrets, process.stderr); } catch {}
        throw new Error('Claim response was lost; no command was replayed, and ownership must be checked before retrying.');
      }
      if (claim.status !== 409) break;
      const conflict = safeStatus(claim.value);
      if (!knownClaimConflict(claim.value, conflict)) {
        writeStatus(conflict.state === 'blocked' ? conflict : blockedStatus('Pipeline returned an invalid claim conflict; no retry is safe.', conflict.editorSessionId), secrets, process.stderr);
        process.exitCode = 2;
        removeSignalHandlers();
        return;
      }
      if (waitingSessionId !== null && conflict.editorSessionId !== waitingSessionId) {
        writeStatus(blockedStatus('Editor session changed between rejected claims; no further claim is safe.', conflict.editorSessionId), secrets, process.stderr);
        process.exitCode = 2;
        removeSignalHandlers();
        return;
      }
      waitingSessionId ??= conflict.editorSessionId;
      reportTransition(conflict);
      const result = await waitForClaimAvailability(project, activeDescriptor, conflict, interval, interruptionController.signal, reportTransition);
      if (result.interrupted) { process.exitCode = interruption === 'SIGTERM' ? 143 : 130; removeSignalHandlers(); return; }
      if (result.descriptor) {
        activeDescriptor = result.descriptor;
        if (!secrets.includes(activeDescriptor.evalToken)) secrets.push(activeDescriptor.evalToken);
      }
      if (!result.ready) {
        writeStatus(result.status, secrets, process.stderr);
        process.exitCode = 2;
        removeSignalHandlers();
        return;
      }
    }
  } catch (error) {
    removeSignalHandlers();
    throw error;
  }
  if (claim.status < 200 || claim.status >= 300 || claim.value.success !== true || typeof claim.value.token !== 'string' || !claim.value.token || typeof claim.value.editorSessionId !== 'string' || !claim.value.editorSessionId) {
    removeSignalHandlers();
    try { writeStatus(safeStatus((await api(activeDescriptor, 'GET', '/api/editor-ownership')).value), secrets, process.stderr); } catch {}
    throw new Error('Pipeline did not confirm the Editor ownership claim; no Unity command was run.');
  }
  const ownershipToken = claim.value.token;
  const sessionId = claim.value.editorSessionId;
  const sessionChangedWhileWaiting = waitingSessionId !== null && sessionId !== waitingSessionId;
  secrets.push(ownershipToken);
  let listener: Awaited<ReturnType<typeof listen>> | undefined;
  let runtimeDirectory: string | undefined;
  let operationError: Error | undefined = sessionChangedWhileWaiting ? new Error('Editor session changed while waiting; no Unity command was run.') : undefined;
  if (sessionChangedWhileWaiting) process.exitCode = 2;
  let safeToRelease = false;
  let safeToRestore = false;
  const settleHeld = async () => {
    const result = await waitForSettlement(project, activeDescriptor, sessionId, identity, batchId, interval, 'held', reportTransition);
    if (result.descriptor) { activeDescriptor = result.descriptor; if (!secrets.includes(activeDescriptor.evalToken)) secrets.push(activeDescriptor.evalToken); }
    if (result.ready) safeToRelease = true;
    else { safeToRelease = false; writeStatus(result.status, secrets, process.stderr); }
    return result.ready;
  };
  try {
    if (await settleHeld()) {
      if (interruption) operationError = new Error(`Batch interrupted by ${interruption}.`);
      else if (!operationError) {
      try {
        const aliasToken = randomBytes(32).toString('hex');
        secrets.push(aliasToken);
        listener = await listen(() => activeDescriptor, ownershipToken, aliasToken);
        runtimeDirectory = await mkdtemp(join(tmpdir(), 'unity-editor-gateway-'));
        const now = new Date().toISOString();
        const alias = { pid: activeDescriptor.pid, port: listener.port, platform: process.platform, unityVersion: activeDescriptor.unityVersion || '', buildGuid: randomUUID(), startedAt: now, lastHeartbeat: now, projectPath: project, workingDirectory: project, evalToken: aliasToken };
        await writeFile(join(runtimeDirectory, RUNTIME_DESCRIPTOR), `${JSON.stringify(alias)}\n`, { mode: 0o600, flag: 'wx' });
        safeToRestore = true;
        const execute = async (command: string[]) => {
          let result: Awaited<ReturnType<typeof runUnity>> | undefined;
          try { result = await runUnity(unityPath, command, runtimeDirectory!, secrets); }
          catch { operationError ||= new Error('Unity command result is uncertain; it was not replayed.'); }
          const settled = await settleHeld();
          if (!settled) return false;
          if (result && (result.code !== 0 || result.signal || result.semanticFailure)) {
            operationError ||= new Error(result.semanticFailure ? 'Unity command reported a semantic failure.' : `Unity command failed (exit ${result.code}${result.signal ? `, signal ${result.signal}` : ''}).`);
          }
          const followup = command[0] === 'recompile' ? ['recompile_status'] : result?.asyncTests ? ['test_status'] : result?.packageOperation ? ['package_status'] : undefined;
          if (followup && result?.code === 0 && !result.signal) {
            try {
              const observed = await runUnity(unityPath, followup, runtimeDirectory!, secrets, result?.packageOperation ?? undefined);
              if (!await settleHeld()) return false;
              if (observed.code !== 0 || observed.signal || observed.semanticFailure) {
                operationError ||= new Error(observed.semanticFailure ? 'Unity command reported a semantic failure.' : `Unity command failed (exit ${observed.code}${observed.signal ? `, signal ${observed.signal}` : ''}).`);
              }
            } catch { operationError ||= new Error('Unity command result is uncertain; it was not replayed.'); }
          }
          return true;
        };
        for (const command of batch.commands) {
          if (interruption) { operationError ||= new Error(`Batch interrupted by ${interruption}.`); break; }
          if (!await execute(command)) { safeToRestore = false; break; }
          if (operationError) break;
        }
        if (safeToRestore) {
          for (const command of batch.restore) {
            if (!await execute(command)) { safeToRestore = false; break; }
          }
        }
        if (invocation.hold && !operationError && !interruption) await holdUntilEnded(end => { endHold = end; });
      } catch (error) {
        operationError ||= error instanceof Error ? error : new Error('Gateway setup failed.');
      }
      }
    } else {
      operationError = new Error('The claimed Editor session is not safely settled; no command or release was attempted.');
      process.exitCode = 2;
    }
    if (interruption) operationError ||= new Error(`Batch interrupted by ${interruption}.`);
    if (safeToRelease) {
      if (await settleHeld()) {
        let release: { status: number; value: JsonObject } | undefined;
        try { release = await api(activeDescriptor, 'POST', '/api/editor-ownership/release', {}, ownershipToken); }
        catch { process.stderr.write('Release response was lost; checking the original session without replaying release.\n'); }
        let receiptTargetValid = true;
        if (release) {
          try {
            const current = await descriptor(project);
            if (current.pid !== activeDescriptor.pid || !processAlive(activeDescriptor.pid)) receiptTargetValid = false;
            else { activeDescriptor = current; if (!secrets.includes(current.evalToken)) secrets.push(current.evalToken); }
          } catch { receiptTargetValid = false; }
        }
        let pollRelease = !release;
        let releaseRejected = false;
        if (release && !receiptTargetValid) {
          writeStatus(blockedStatus('Editor process changed after the release request; settlement cannot be attributed to this instance.', sessionId), secrets, process.stderr);
          operationError ||= new Error('Editor process changed while confirming release.');
          process.exitCode = 2;
        } else if (release) {
          const receipt = safeStatus(release.value);
          const sameSession = receipt.editorSessionId === sessionId;
          const receiptSettledFree = sameSession && receipt.success && receipt.state === 'free' && receipt.settled && receipt.activeOperations === 0;
          const laterLease = sameSession && (receipt.state === 'held' || receipt.state === 'releasing') && differentLease(receipt, identity, batchId);
          if (receiptSettledFree || laterLease) {
            writeStatus(receipt, secrets);
          } else if (receipt.state === 'blocked') {
            writeStatus(receipt, secrets);
            operationError ||= new Error('Pipeline blocked ownership release because completion is unknown.');
            process.exitCode = 2;
          } else if (release.status >= 400) {
            writeStatus(receipt, secrets, process.stderr);
            releaseRejected = true;
            pollRelease = receipt.state === 'releasing';
            if (!pollRelease) {
              process.exitCode = 2;
              operationError ||= new Error('Pipeline rejected ownership release; the reported state remains authoritative.');
            }
          } else {
            pollRelease = true;
          }
        }
        if (pollRelease) {
          const result = await waitForSettlement(project, activeDescriptor, sessionId, identity, batchId, interval, 'release', reportTransition);
          if (result.descriptor) { activeDescriptor = result.descriptor; if (!secrets.includes(activeDescriptor.evalToken)) secrets.push(activeDescriptor.evalToken); }
          writeStatus(result.status, secrets);
          if (!result.ready) {
            operationError ||= new Error(`Editor ownership remains ${result.status.state}; no takeover or retry was attempted.`);
            process.exitCode = 2;
          } else if (releaseRejected) process.stderr.write('The release response was rejected, but the original lease is confirmed settled.\n');
        }
      } else {
        operationError ||= new Error('Editor session changed before release; ownership was not transferred or force-released.');
        process.exitCode = 2;
      }
    } else if (!operationError) {
      operationError = new Error('Editor work is unresolved; ownership remains held and no release was attempted.');
      process.exitCode = 2;
    }
    if (operationError) {
      process.exitCode ||= 1;
      throw operationError;
    }
  } finally {
    removeSignalHandlers();
    listener?.server.closeAllConnections();
    if (listener) await new Promise<void>(resolveClose => listener!.server.close(() => resolveClose()));
    if (runtimeDirectory) await rm(runtimeDirectory, { recursive: true, force: true });
  }
}
export async function main(args = process.argv.slice(2)): Promise<void> { return run(args); }
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  run().catch(error => { process.stderr.write(`${redact(error instanceof Error ? error.message : 'Gateway failed.', process.env.ORCA_AGENT_HOOK_TOKEN || '')}\n`); process.exitCode ||= 1; });
}
