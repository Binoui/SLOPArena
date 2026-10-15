import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { chmod, mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { identityFromT3, identityFromCodex, identityFromClaude, identityFromLauncher, identityFromEnvironment, run } from './unity-editor-gateway.ts';

test('native Codex and launcher identities are distinct and require complete session IDs', async () => {
  const session = '01a127c6-bae8-7792-b662-4d0aab865c4b';
  const codex = { CODEX_THREAD_ID: '01a127c6-bae8-7792-b662-4d0aab865c4a', CODEX_SESSION_ID: session };
  const launcher = { SLOPARENA_AGENT_ID: 'claude-worker.1', SLOPARENA_AGENT_SESSION_ID: session };
  assert.deepEqual(await identityFromEnvironment(codex), { terminalHandle: `codex:${codex.CODEX_THREAD_ID}`, incarnationId: session });
  assert.deepEqual(await identityFromEnvironment(launcher), { terminalHandle: 'agent:claude-worker.1', incarnationId: session });
  assert.throws(() => identityFromCodex({ CODEX_THREAD_ID: codex.CODEX_THREAD_ID }), /Codex caller requires/);
  assert.throws(() => identityFromCodex({ ...codex, CODEX_SESSION_ID: 'not-a-session' }), /Codex caller requires/);
  assert.throws(() => identityFromLauncher({ SLOPARENA_AGENT_ID: launcher.SLOPARENA_AGENT_ID }), /Launcher caller requires/);
  for (const agent of [' ', 't3:other', '../owner', 'agent\nowner'])
    assert.throws(() => identityFromLauncher({ ...launcher, SLOPARENA_AGENT_ID: agent }), /Launcher caller requires/);
  await assert.rejects(identityFromEnvironment({}), /identity is required/);
  // An explicit launcher pair is provider-neutral: it applies inside the T3 harness too.
  assert.deepEqual(await identityFromEnvironment({ ...launcher, T3CODE_CLI_PATH: '/fixture/t3' }), identityFromLauncher(launcher));
  await assert.rejects(identityFromEnvironment({ ...launcher, SLOPARENA_AGENT_SESSION_ID: 'not-a-session', T3CODE_CLI_PATH: '/fixture/t3' }), /Launcher caller requires/);
  // Explicit Orca identity must never fall back to another source after failure.
  await assert.rejects(identityFromEnvironment({ ...launcher, ORCA_TERMINAL_HANDLE: ' ' }), /ORCA_TERMINAL_HANDLE is required/);
  // Launchers may support runners that expose only a Codex thread ID.
  assert.deepEqual(await identityFromEnvironment({ ...launcher, CODEX_THREAD_ID: codex.CODEX_THREAD_ID }), identityFromLauncher(launcher));
});

test('runtime agent adapters are provider-agnostic inside and outside the T3 harness', async () => {
  const session = 'ebb23d78-afe3-463f-99d8-2f987f4b0902';
  const claude = { CLAUDE_CODE_SESSION_ID: session };
  const expected = { terminalHandle: `claude:${session}`, incarnationId: session };
  assert.deepEqual(identityFromClaude(claude), expected);
  assert.deepEqual(await identityFromEnvironment(claude), expected);
  // T3 hosts several runtimes; its markers no longer imply Codex-only IDs.
  assert.deepEqual(await identityFromEnvironment({ ...claude, T3CODE_CLI_PATH: '/fixture/t3' }), expected);
  assert.deepEqual(await identityFromEnvironment({ ...claude, T3_MCP_BEARER_TOKEN: 't3-secret-fixture' }), expected);
  for (const value of ['', ' ', 'not-a-session', `${session}\nowner`])
    await assert.rejects(identityFromEnvironment({ CLAUDE_CODE_SESSION_ID: value, T3CODE_CLI_PATH: '/fixture/t3' }), /Claude Code caller requires/);
  // A selected runtime never falls back to a different runtime after failing validation.
  await assert.rejects(identityFromEnvironment({ ...claude, CODEX_THREAD_ID: 'term-other', T3CODE_CLI_PATH: '/fixture/t3' }), /T3 caller requires/);
  await assert.rejects(identityFromEnvironment({ T3CODE_CLI_PATH: '/fixture/t3' }), /T3 caller requires runtime-injected agent session IDs/);
});

test('T3 identity uses namespaced runtime thread and session IDs without exposing its bearer', () => {
  const env = {
    CODEX_THREAD_ID: '01a127c6-bae8-7792-b662-4d0aab865c4a',
    CODEX_SESSION_ID: '01a127c6-bae8-7792-b662-4d0aab865c4b',
    T3_MCP_BEARER_TOKEN: 't3-secret-fixture',
  };
  const caller = identityFromT3(env);
  assert.deepEqual(caller, { terminalHandle: `t3:${env.CODEX_THREAD_ID}`, incarnationId: env.CODEX_SESSION_ID });
  assert.ok(!JSON.stringify(caller).includes(env.T3_MCP_BEARER_TOKEN));
  for (const key of Object.keys(env)) {
    const missing = { ...env };
    delete missing[key];
    assert.throws(() => identityFromT3(missing), /T3 caller requires/);
  }
  assert.throws(() => identityFromT3({ ...env, CODEX_THREAD_ID: 'term-other' }), /T3 caller requires/);
  assert.throws(() => identityFromT3({ ...env, CODEX_SESSION_ID: ' ' }), /T3 caller requires/);
});

test('current T3 CLI harness marker still requires valid runtime thread and session IDs', () => {
  const env = {
    CODEX_THREAD_ID: '01a127c6-bae8-7792-b662-4d0aab865c4a',
    CODEX_SESSION_ID: '01a127c6-bae8-7792-b662-4d0aab865c4b',
    T3CODE_CLI_PATH: '/fixture/t3',
  };
  assert.deepEqual(identityFromT3(env), {
    terminalHandle: `t3:${env.CODEX_THREAD_ID}`, incarnationId: env.CODEX_SESSION_ID,
  });
  for (const key of Object.keys(env)) {
    const missing = { ...env };
    delete missing[key];
    assert.throws(() => identityFromT3(missing), /T3 caller requires/);
  }
  assert.throws(() => identityFromT3({ ...env, T3CODE_CLI_PATH: ' ' }), /T3 caller requires/);
  assert.throws(() => identityFromT3({ ...env, CODEX_THREAD_ID: 'term-other' }), /T3 caller requires/);
  assert.throws(() => identityFromT3({ ...env, CODEX_SESSION_ID: ' ' }), /T3 caller requires/);
});

type Call = { path: string; body: Record<string, unknown> };
const bearer = 'pipeline-secret-fixture';
const lease = 'lease-secret-fixture';
const identity = { terminalHandle: 'term-fixture', incarnationId: 'incarnation-fixture' };

async function fixture(commandPollLimit = 2, restartAfterCommand = false, reacquireAfterRelease = false, reacquireDuringRelease = false, omitNullStatusFields = false) {
  const folder = await mkdtemp(join(tmpdir(), 'unity-editor-gateway-test-'));
  const project = join(folder, 'Project');
  await mkdir(join(project, 'Library', 'Pipeline'), { recursive: true });
  await mkdir(join(project, 'Assets'));
  await mkdir(join(project, 'ProjectSettings'));
  await writeFile(join(project, 'ProjectSettings', 'ProjectVersion.txt'), 'm_EditorVersion: 6000.0.78f1\n');
  const commandActivity = Promise.withResolvers<void>();
  const claimActivity = Promise.withResolvers<void>();
  const claimGate = Promise.withResolvers<void>();
  const busyPollActivity = Promise.withResolvers<void>();
  const secondBusyPollActivity = Promise.withResolvers<void>();
  const secondConflictActivity = Promise.withResolvers<void>();
  const state: { owner: unknown; calls: Call[]; busy: boolean; busyState: 'held' | 'releasing'; claimAttempts: number; conflictOnNextClaim: boolean; sessionChangesOnNextConflict: boolean; sessionChangesOnSuccessfulClaim: boolean; invalidConflict: boolean; loseNextClaimResponse: boolean; statusOverride: Record<string, unknown> | null; evalToken: string; busyPolls: number; releaseCount: number; settlePolls: number; commandPolls: number; observedStates: string[]; commandAdmission: boolean[]; observedOwnerBatches: string[]; sessionId: string; batchId: string; packageArgument: string; claimActivity: Promise<void>; claimGate: Promise<void>; releaseClaim: () => void; signalDuringClaim: boolean; busyPollActivity: Promise<void>; secondBusyPollActivity: Promise<void>; secondConflictActivity: Promise<void>; testTerminalStatus?: string; testFailedCount?: number } = {
    owner: null, calls: [], busy: false, busyState: 'held', claimAttempts: 0, conflictOnNextClaim: false, sessionChangesOnNextConflict: false, sessionChangesOnSuccessfulClaim: false, invalidConflict: false, loseNextClaimResponse: false, statusOverride: null, evalToken: bearer, busyPolls: 0, releaseCount: 0, settlePolls: 2, commandPolls: 0, observedStates: [], commandAdmission: [], observedOwnerBatches: [], sessionId: 'session-fixture', batchId: '', packageArgument: '', claimActivity: claimActivity.promise, claimGate: claimGate.promise, releaseClaim: () => claimGate.resolve(), signalDuringClaim: false, busyPollActivity: busyPollActivity.promise, secondBusyPollActivity: secondBusyPollActivity.promise, secondConflictActivity: secondConflictActivity.promise,
  };
  const server = createServer(async (request, response) => {
    let bodyText = '';
    for await (const chunk of request) bodyText += chunk;
    const body = bodyText ? JSON.parse(bodyText) as Record<string, unknown> : {};
    const path = request.url || '';
    state.calls.push({ path, body });
    response.setHeader('content-type', 'application/json');
    if (request.headers.authorization !== `Bearer ${state.evalToken}`) { response.statusCode = 401; response.end(JSON.stringify({ success: false, error: 'invalid bearer' })); return; }
    if (path === '/api/editor-ownership' && request.method === 'GET') {
      if (state.statusOverride) { response.end(JSON.stringify(state.statusOverride)); return; }
      let stateName = state.busy ? state.busyState : state.owner ? 'held' : 'free';
      let settled = !state.busy || state.busyState === 'held', activeOperations = state.busy && state.busyState === 'releasing' ? 1 : 0;
      if (state.owner === 'releasing') {
        if (state.settlePolls-- > 0) stateName = 'releasing';
        else { state.owner = null; stateName = 'free'; }
        settled = stateName === 'free'; activeOperations = stateName === 'releasing' ? 1 : 0;
      } else if (state.owner && state.commandPolls > 0) {
        state.commandPolls--;
        settled = state.commandPolls === 0;
        activeOperations = settled ? 0 : 1;
      }
      if (state.busy && state.claimAttempts > 0) { state.busyPolls++; if (state.busyPolls === 1) busyPollActivity.resolve(); if (state.busyPolls === 2) secondBusyPollActivity.resolve(); }
      if (!settled && state.owner !== 'releasing') commandActivity.resolve();
      state.observedStates.push(stateName);
      const releaseOwner = { ...identity, batchId: state.batchId };
      const owner = state.busy ? { terminalHandle: 'other', incarnationId: 'other-incarnation', batchId: 'other-batch' }
        : state.owner === 'releasing' ? releaseOwner : state.owner;
      if (owner && typeof owner === 'object' && !Array.isArray(owner)) {
        const ownerBatch = (owner as Record<string, unknown>).batchId;
        if (typeof ownerBatch === 'string') state.observedOwnerBatches.push(ownerBatch);
      }
      const status = { success: true, state: stateName, settled, editorSessionId: state.sessionId, owner, activeOperations, reason: omitNullStatusFields ? null : bearer };
      response.end(JSON.stringify(status, omitNullStatusFields ? (_key, value) => value === null ? undefined : value : undefined)); return;
    }
    if (path === '/api/editor-ownership/claim' && request.method === 'POST') {
      state.claimAttempts++;
      claimActivity.resolve();
      if (state.signalDuringClaim) await claimGate.promise;
      if (state.loseNextClaimResponse) {
        state.loseNextClaimResponse = false;
        state.batchId = typeof body.batchId === 'string' ? body.batchId : '';
        state.owner = { ...identity, batchId: state.batchId };
        response.destroy(); return;
      }
      if (state.conflictOnNextClaim) { state.conflictOnNextClaim = false; state.busy = true; }
      if (state.busy) {
        if (state.sessionChangesOnNextConflict) { state.sessionChangesOnNextConflict = false; state.sessionId = 'replacement-session'; }
        if (state.claimAttempts >= 2) secondConflictActivity.resolve();
        response.statusCode = 409;
        response.end(JSON.stringify({ success: false, state: state.busyState, settled: state.invalidConflict ? undefined : state.busyState === 'held', editorSessionId: state.sessionId, owner: { terminalHandle: 'other', incarnationId: 'other-incarnation', batchId: 'other-batch' }, activeOperations: state.busyState === 'releasing' ? 1 : 0, operation: 'claim', reason: 'owned by other' })); return;
      }
      state.batchId = typeof body.batchId === 'string' ? body.batchId : '';
      state.owner = { ...identity, batchId: state.batchId };
      if (state.sessionChangesOnSuccessfulClaim) { state.sessionChangesOnSuccessfulClaim = false; state.sessionId = 'replacement-session'; }
      response.end(JSON.stringify({ success: true, token: lease, editorSessionId: state.sessionId })); return;
    }
    if (path === '/api/editor-ownership/release' && request.method === 'POST') {
      state.releaseCount++;
      if (reacquireAfterRelease) {
        state.owner = { terminalHandle: 'next', incarnationId: 'next-incarnation', batchId: 'next-batch' };
        response.end(JSON.stringify({ success: true, state: 'free', settled: true, editorSessionId: state.sessionId, owner: null, activeOperations: 0, reason: null })); return;
      }
      if (reacquireDuringRelease) {
        const receipt = { success: true, state: 'releasing', settled: false, editorSessionId: state.sessionId, owner: { ...identity, batchId: state.batchId }, activeOperations: 1, reason: null };
        state.owner = { ...identity, batchId: 'next-batch' };
        response.end(JSON.stringify(receipt)); return;
      }
      const releaseOwner = { ...identity, batchId: state.batchId };
      state.owner = 'releasing';
      response.end(JSON.stringify({ success: true, state: 'releasing', settled: false, editorSessionId: state.sessionId, owner: releaseOwner, activeOperations: 1, reason: null })); return;
    }
    if (path === '/api/exec' && request.method === 'POST') {
      if (request.headers['x-editor-ownership'] !== lease || !state.owner || state.owner === 'releasing') {
        response.statusCode = 409;
        response.end(JSON.stringify({ success: false, error: 'execution requires this batch lease' })); return;
      }
      state.commandAdmission.push(state.observedStates.at(-1) === 'held' && state.commandPolls === 0);
      state.commandPolls = commandPollLimit;
      const command = body.command;
      if (command === 'lost') { response.destroy(); return; }
      if (restartAfterCommand) { state.owner = null; state.sessionId = 'replacement-session'; state.commandPolls = 0; }
      const argumentsList = Array.isArray(body.arguments) ? body.arguments : [];
      if (command === 'package_add' || command === 'package_remove') state.packageArgument = String(argumentsList[0] || '');
      const asyncTests = command === 'run_tests' && argumentsList.includes('async_tests=true');
      const result = command === 'fail' ? { success: false, error: 'semantic command failure' }
        : command === 'run_tests' && argumentsList.includes('mode=all') ? { success: true, result: 'playmode_running', Summary: { Failed: 0 } }
        : command === 'run_tests' && asyncTests ? { success: true, result: 'running' }
        : command === 'run_tests' ? { success: true, Summary: { Failed: 2 } }
        : command === 'test_status' ? JSON.stringify({ status: state.testTerminalStatus ?? 'completed', summary: { failed: state.testFailedCount ?? 1 } })
        : command === 'package_add' || command === 'package_remove' ? { success: true, operation: command === 'package_add' ? 'add' : 'remove', argument: argumentsList[0] || '', status: 'in_progress', operationId: 'package-op-fixture', applied: true, plan: 'fixture', message: 'started' }
        : command === 'package_status' ? JSON.stringify(argumentsList.length === 0
          ? state.packageArgument === 'pkg-id'
            ? { status: 'failed', operation: 'add', argument: state.packageArgument, operationId: 'package-op-fixture', success: false, error: 'package install failed' }
            : { status: 'completed', operation: 'add', argument: state.packageArgument, operationId: state.packageArgument === 'pkg-mismatch' ? 'stale-operation' : 'package-op-fixture', success: true, completionEvidence: state.packageArgument === 'pkg-no-evidence' ? undefined : 'upm_request' }
          : argumentsList[0] === 'malformed' ? 'not-json'
          : argumentsList[0] === 'idle' ? { status: 'idle', success: true }
          : argumentsList[0] === 'completed-failure' ? { status: 'completed', success: false, error: 'package install failed' }
          : argumentsList[0] === 'failed' ? { status: 'failed', success: true, error: 'package install failed' }
          : { status: argumentsList[0] || 'unknown', success: false, error: 'package result unknown' })
        : command === 'recompile_status' ? '{"status":"completed","failed":true}'
        : command === 'eval' && argumentsList[0] === 'fail' ? { success: false, error: 'evaluation failed' }
        : command === 'eval' ? { success: true, result: { success: false } } : { command };
      response.end(JSON.stringify({ success: true, result })); return;
    }
    response.statusCode = 404;
    response.end(JSON.stringify({ error: 'not found' }));
  });
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  const addr = server.address();
  assert.ok(addr && typeof addr !== 'string');
  await writeFile(join(project, 'Library', 'Pipeline', '.unity-pipeline-port'), JSON.stringify({ pid: process.pid, port: addr.port, projectPath: project, evalToken: state.evalToken, unityVersion: 'test' }), { mode: 0o600 });
  const fakeUnity = join(folder, 'unity'), fakeOrca = join(folder, 'orca');
  await writeFile(fakeUnity, `#!/usr/bin/env node\nimport { readFile } from 'node:fs/promises';\nconst a=process.argv.slice(2); const get=k=>a[a.indexOf(k)+1]; const d=JSON.parse(await readFile(get('--runtime-path')+'/.unity-pipeline-runtime-port','utf8')); const root='http://127.0.0.1:'+d.port;\nconst denied=await fetch(root+'/api/exec',{method:'POST',headers:{authorization:'Bearer wrong','content-type':'application/json'},body:'{}'}); const outside=await fetch(root+'/private',{headers:{authorization:'Bearer '+d.evalToken}}); if(denied.status!==401||outside.status!==404)process.exit(90);\nconst commandIndex=a.indexOf('--proxy-disable')+1; const command=a[commandIndex]; const response=await fetch(root+'/api/exec',{method:'POST',headers:{authorization:'Bearer '+d.evalToken,'content-type':'application/json'},body:JSON.stringify({command,arguments:a.slice(commandIndex+1),parameters:{}})}); const payload=await response.json(); if(!response.ok)process.exitCode=1; process.stdout.write(JSON.stringify({data:{result:payload.result}}));\n`);
  await writeFile(fakeOrca, `#!/usr/bin/env node\nconst a=process.argv.slice(2); const caller=process.env.ORCA_TERMINAL_HANDLE; if(!caller||a[a.indexOf('--terminal')+1]!==caller)process.exit(91); process.stdout.write(JSON.stringify({ok:true,result:{terminal:{handle:process.env.ORCA_FIXTURE_HANDLE||caller,incarnationId:'${identity.incarnationId}',orphaned:process.env.ORCA_FIXTURE_ORPHAN==='1'}}}));\n`);
  await chmod(fakeUnity, 0o700);
  await chmod(fakeOrca, 0o700);
  const close = async () => { await new Promise<void>(resolve => server.close(() => resolve())); await rm(folder, { recursive: true, force: true }); };
  return { folder, project, fakeUnity, fakeOrca, state, close, waitForCommandActivity: commandActivity.promise };
}

async function capture<T>(action: () => Promise<T>) {
  const oldOut = process.stdout.write, oldErr = process.stderr.write, out: string[] = [], err: string[] = [];
  process.stdout.write = ((chunk: string | Uint8Array) => { out.push(String(chunk)); return true; }) as typeof process.stdout.write;
  process.stderr.write = ((chunk: string | Uint8Array) => { err.push(String(chunk)); return true; }) as typeof process.stderr.write;
  try { return { value: await action(), out: out.join(''), err: err.join('') }; }
  catch (error) { return { error, out: out.join(''), err: err.join('') }; }
  finally { process.stdout.write = oldOut; process.stderr.write = oldErr; }
}

test('preflight validates roots and batch locally, reports observed recovery without claiming or executing', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  const checkout = join(f.folder, 'SourceCheckout');
  await mkdir(join(checkout, 'src', 'Shared'), { recursive: true });
  await mkdir(join(checkout, 'tests', 'Shared.Tests'), { recursive: true });
  await writeFile(join(checkout, 'src', 'Shared', 'SlopArena.Shared.csproj'), '');
  await writeFile(join(checkout, 'tests', 'Shared.Tests', 'Shared.Tests.csproj'), '');
  const batch = join(f.folder, 'batch.json');
  await writeFile(batch, JSON.stringify({ commands: [['editor_status']], restore: [['scene_status']] }));
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['preflight', '--project-path', f.project, '--checkout-path', checkout, '--batch', batch], { identity }));
    assert.equal('error' in result, false);
    const output = JSON.parse(result.out);
    assert.equal(output.ownership, 'not-acquired');
    assert.equal(output.roots.editor, f.project);
    assert.equal(output.roots.source, checkout);
    assert.equal(output.recovery.availability, 'observed');
    assert.equal(output.recovery.state, 'free');
    assert.deepEqual(output.plan.commands, [['editor_status']]);
    assert.deepEqual(output.plan.restore, [['scene_status']]);
    const direct = await capture(() => run(['preflight', '--project-path', f.project, '--checkout-path', checkout, '--', 'editor_status'], { identity }));
    assert.equal('error' in direct, false);
    assert.deepEqual(JSON.parse(direct.out).plan.commands, [['editor_status']]);
    assert.equal(f.state.calls.some(call => call.path.includes('/claim') || call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('preflight rejects an invalid explicit game checkout before observing runtime', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    await assert.rejects(run(['preflight', '--project-path', f.project, '--checkout-path', f.folder]));
    await assert.rejects(run(['preflight', '--project-path', f.project, '--checkout-path', '']));
    assert.equal(f.state.calls.length, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('preflight rejects invalid Editor roots even with a valid explicit source checkout', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  await mkdir(join(f.folder, 'src', 'Shared'), { recursive: true });
  await mkdir(join(f.folder, 'tests', 'Shared.Tests'), { recursive: true });
  await writeFile(join(f.folder, 'src', 'Shared', 'SlopArena.Shared.csproj'), '');
  await writeFile(join(f.folder, 'tests', 'Shared.Tests', 'Shared.Tests.csproj'), '');
  try {
    for (const project of [f.folder, join(f.project, 'ProjectSettings', 'ProjectVersion.txt')]) {
      await assert.rejects(run(['preflight', '--project-path', project, '--checkout-path', f.folder]));
    }
    assert.equal(f.state.calls.length, 0);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('preflight rejects an invalid batch before observing runtime', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  const batch = join(f.folder, 'invalid-batch.json');
  await writeFile(batch, '{"commands":[]}');
  process.exitCode = undefined;
  try {
    await assert.rejects(run(['preflight', '--project-path', f.project, '--batch', batch]));
    assert.equal(f.state.calls.length, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('preflight marks a missing runtime unknown rather than free', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  await rm(join(f.project, 'Library', 'Pipeline', '.unity-pipeline-port'));
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['preflight', '--project-path', f.project]));
    assert.equal('error' in result, false);
    assert.equal(JSON.parse(result.out).recovery.availability, 'unknown');
    assert.notEqual(JSON.parse(result.out).recovery.state, 'free');
    assert.equal(f.state.calls.length, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('a batch confirms the explicitly supplied Orca identity, runs in order, restores, and waits for settled release', async () => {
  const f = await fixture(), previousExit = process.exitCode, previousHandle = process.env.ORCA_TERMINAL_HANDLE;
  const previousFixtureHandle = process.env.ORCA_FIXTURE_HANDLE, previousOrphan = process.env.ORCA_FIXTURE_ORPHAN;
  process.env.ORCA_TERMINAL_HANDLE = identity.terminalHandle;
  delete process.env.ORCA_FIXTURE_HANDLE;
  delete process.env.ORCA_FIXTURE_ORPHAN;
  process.exitCode = undefined;
  try {
    const batch = join(f.folder, 'batch.json');
    await writeFile(batch, JSON.stringify({ commands: [['first'], ['second']], restore: [['restore']] }));
    const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, orcaPath: f.fakeOrca, pollInterval: 1 }));
    assert.equal('error' in result, false);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['first', 'second', 'restore']);
    assert.deepEqual(f.state.calls.find(call => call.path === '/api/editor-ownership/claim')?.body.owner, identity);
    assert.ok(f.state.calls.findIndex(call => call.path === '/api/editor-ownership/release') > f.state.calls.map(call => call.path).lastIndexOf('/api/exec'));
    assert.ok(f.state.commandAdmission.every(admitted => admitted));
    assert.equal(f.state.releaseCount, 1);
    const releaseAt = f.state.observedStates.indexOf('releasing');
    const settledAt = f.state.observedStates.lastIndexOf('free');
    assert.ok(releaseAt >= 0 && settledAt > releaseAt);
    assert.equal(f.state.owner, null);
    assert.equal(result.out.includes(bearer), false);
    assert.equal(result.out.includes(lease), false);
  } finally {
    if (previousHandle === undefined) delete process.env.ORCA_TERMINAL_HANDLE;
    else process.env.ORCA_TERMINAL_HANDLE = previousHandle;
    if (previousFixtureHandle === undefined) delete process.env.ORCA_FIXTURE_HANDLE;
    else process.env.ORCA_FIXTURE_HANDLE = previousFixtureHandle;
    if (previousOrphan === undefined) delete process.env.ORCA_FIXTURE_ORPHAN;
    else process.env.ORCA_FIXTURE_ORPHAN = previousOrphan;
    process.exitCode = previousExit;
    await f.close();
  }
});
test('a missing runtime caller identity fails before claim or command', async () => {
  const f = await fixture(), previousHandle = process.env.ORCA_TERMINAL_HANDLE;
  const previousT3Bearer = process.env.T3_MCP_BEARER_TOKEN;
  const previousT3Cli = process.env.T3CODE_CLI_PATH;
  const identityKeys = ['CODEX_THREAD_ID', 'CODEX_SESSION_ID', 'CLAUDE_CODE_SESSION_ID', 'SLOPARENA_AGENT_ID', 'SLOPARENA_AGENT_SESSION_ID'];
  const previousIds = identityKeys.map(key => process.env[key]);
  for (const key of identityKeys) delete process.env[key];
  delete process.env.ORCA_TERMINAL_HANDLE;
  delete process.env.T3_MCP_BEARER_TOKEN;
  delete process.env.T3CODE_CLI_PATH;
  try {
    await assert.rejects(run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, orcaPath: f.fakeOrca }));
    assert.equal(f.state.claimAttempts, 0);
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
  } finally {
    for (let i = 0; i < identityKeys.length; i++) {
      if (previousIds[i] === undefined) delete process.env[identityKeys[i]];
      else process.env[identityKeys[i]] = previousIds[i];
    }
    if (previousHandle === undefined) delete process.env.ORCA_TERMINAL_HANDLE;
    else process.env.ORCA_TERMINAL_HANDLE = previousHandle;
    if (previousT3Bearer === undefined) delete process.env.T3_MCP_BEARER_TOKEN;
    else process.env.T3_MCP_BEARER_TOKEN = previousT3Bearer;
    if (previousT3Cli === undefined) delete process.env.T3CODE_CLI_PATH;
    else process.env.T3CODE_CLI_PATH = previousT3Cli;
    await f.close();
  }
});
test('mismatched and orphaned Orca identities fail before claim or command', async () => {
  for (const failure of ['mismatch', 'orphan'] as const) {
    const f = await fixture(), previousHandle = process.env.ORCA_TERMINAL_HANDLE;
    const previousFixtureHandle = process.env.ORCA_FIXTURE_HANDLE, previousOrphan = process.env.ORCA_FIXTURE_ORPHAN;
    process.env.ORCA_TERMINAL_HANDLE = identity.terminalHandle;
    delete process.env.ORCA_FIXTURE_HANDLE;
    delete process.env.ORCA_FIXTURE_ORPHAN;
    if (failure === 'mismatch') process.env.ORCA_FIXTURE_HANDLE = 'different-terminal';
    else process.env.ORCA_FIXTURE_ORPHAN = '1';
    try {
      await assert.rejects(run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, orcaPath: f.fakeOrca }));
      assert.equal(f.state.claimAttempts, 0);
      assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    } finally {
      if (previousHandle === undefined) delete process.env.ORCA_TERMINAL_HANDLE;
      else process.env.ORCA_TERMINAL_HANDLE = previousHandle;
      if (previousFixtureHandle === undefined) delete process.env.ORCA_FIXTURE_HANDLE;
      else process.env.ORCA_FIXTURE_HANDLE = previousFixtureHandle;
      if (previousOrphan === undefined) delete process.env.ORCA_FIXTURE_ORPHAN;
      else process.env.ORCA_FIXTURE_ORPHAN = previousOrphan;
      await f.close();
    }
  }
});

test('typed semantic failure stops later commands, restores, and releases despite a zero CLI exit', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const batch = join(f.folder, 'batch.json');
    await writeFile(batch, JSON.stringify({ commands: [['fail'], ['must-not-run']], restore: [['restore']] }));
    const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.equal(process.exitCode, 1);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['fail', 'restore']);
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('native test summaries fail synchronously and after asynchronous test settlement', async () => {
  for (const command of [['run_tests'], ['run_tests', 'async_tests=true'], ['test_status']]) {
    const f = await fixture(), previousExit = process.exitCode;
    process.exitCode = undefined;
    try {
      const result = await capture(() => run(['--project-path', f.project, '--', ...command], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
      assert.ok('error' in result);
      assert.equal(process.exitCode, 1);
      const calls = f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command);
      assert.deepEqual(calls, command[0] === 'run_tests' && command.length > 1 ? ['run_tests', 'test_status'] : [command[0]]);
      assert.equal(f.state.releaseCount, 1);
      assert.equal(f.state.owner, null);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});
test('successful native test receipts without a success field allow later commands and settled release', async () => {
  for (const command of [['run_tests', 'async_tests=true'], ['run_tests', 'mode=all'], ['test_status']]) {
    const f = await fixture(), previousExit = process.exitCode;
    f.state.testFailedCount = 0;
    process.exitCode = undefined;
    try {
      const batch = join(f.folder, 'passing-tests.json');
      await writeFile(batch, JSON.stringify({ commands: [command, ['after-pass']], restore: [['restore']] }));
      const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
      assert.equal('error' in result, false);
      assert.notEqual(process.exitCode, 1);
      assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command),
        command[0] === 'run_tests' ? ['run_tests', 'test_status', 'after-pass', 'restore'] : ['test_status', 'after-pass', 'restore']);
      assert.equal(f.state.owner, null);
      assert.equal(f.state.releaseCount, 1);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});

test('all-mode native tests inspect PlayMode failure before later commands and release', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const batch = join(f.folder, 'all-tests.json');
    await writeFile(batch, JSON.stringify({ commands: [['run_tests', 'mode=all'], ['must-not-run']], restore: [['restore']] }));
    const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.equal(process.exitCode, 1);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['run_tests', 'test_status', 'restore']);
    assert.equal(f.state.owner, null);
    assert.equal(f.state.releaseCount, 1);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('terminal native test errors stop later commands and restore before release', async () => {
  for (const status of ['error', 'cancelled', 'canceled']) {
    const f = await fixture(), previousExit = process.exitCode;
    f.state.testTerminalStatus = status;
    process.exitCode = undefined;
    try {
      const batch = join(f.folder, 'terminal-tests.json');
      await writeFile(batch, JSON.stringify({ commands: [['run_tests', 'async_tests=true'], ['must-not-run']], restore: [['restore']] }));
      const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
      assert.ok('error' in result);
      assert.equal(process.exitCode, 1);
      assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['run_tests', 'test_status', 'restore']);
      assert.equal(f.state.owner, null);
      assert.equal(f.state.releaseCount, 1);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});

test('delayed compile failure stops main commands but restores and releases', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const batch = join(f.folder, 'compile-batch.json');
    await writeFile(batch, JSON.stringify({ commands: [['recompile'], ['must-not-run']], restore: [['restore']] }));
    const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.equal(process.exitCode, 1);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['recompile', 'recompile_status', 'restore']);
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('recompile JSON failure is semantic while arbitrary eval result data is not', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const failed = await capture(() => run(['--project-path', f.project, '--', 'recompile_status'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in failed);
    assert.equal(process.exitCode, 1);
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }

  const evalFixture = await fixture(), evalExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', evalFixture.project, '--', 'eval', 'return false'], { unityPath: evalFixture.fakeUnity, identity, pollInterval: 1 }));
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, undefined);
    assert.equal(evalFixture.state.releaseCount, 1);
    assert.equal(evalFixture.state.owner, null);
  } finally { process.exitCode = evalExit; await evalFixture.close(); }
});
test('package status receipts with failed or unknown outcomes remain semantic failures', async () => {
  for (const outcome of ['completed-failure', 'failed', 'unknown', 'malformed']) {
    const f = await fixture(), previousExit = process.exitCode;
    process.exitCode = undefined;
    try {
      const batch = join(f.folder, 'package-status.json');
      await writeFile(batch, JSON.stringify({ commands: [['package_status', outcome], ['must-not-run']], restore: [['restore']] }));
      const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
      assert.ok('error' in result);
      assert.equal(process.exitCode, 1);
      assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['package_status', 'restore']);
      assert.equal(f.state.releaseCount, 1);
      assert.equal(f.state.owner, null);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});
test('an idle package status remains informational', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', f.project, '--', 'package_status', 'idle'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, undefined);
    assert.equal(f.state.releaseCount, 1);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('asynchronous package mutations require a matching terminal receipt before continuing', async () => {
  for (const [argument, shouldFail] of [['pkg-id', true], ['pkg-mismatch', true], ['pkg-no-evidence', true], ['pkg-success', false]] as const) {
    const f = await fixture(), previousExit = process.exitCode;
    process.exitCode = undefined;
    try {
      const batch = join(f.folder, 'async-package.json');
      await writeFile(batch, JSON.stringify({ commands: [['package_add', argument], ['must-not-run']], restore: [['restore']] }));
      const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
      assert.equal('error' in result, shouldFail);
      assert.equal(process.exitCode, shouldFail ? 1 : undefined);
      const commands = f.state.calls.filter(call => call.path === '/api/exec');
      assert.deepEqual(commands.map(call => call.body.command), shouldFail
        ? ['package_add', 'package_status', 'restore']
        : ['package_add', 'package_status', 'must-not-run', 'restore']);
      assert.deepEqual(commands[1].body.arguments, []);
      assert.equal(f.state.releaseCount, 1);
      assert.equal(f.state.owner, null);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});
test('a genuine eval execution failure is not confused with arbitrary eval return data', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', f.project, '--', 'eval', 'fail'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.equal(process.exitCode, 1);
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('an ambiguous command response is never replayed and restoration waits for settlement', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const batch = join(f.folder, 'batch.json');
    await writeFile(batch, JSON.stringify({ commands: [['lost'], ['must-not-run']], restore: [['restore']] }));
    const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.equal(process.exitCode, 1);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['lost', 'restore']);
    assert.ok(f.state.commandAdmission.every(admitted => admitted));
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('a new Editor session cannot settle, restore, or release the previous session lease', async () => {
  const f = await fixture(2, true), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const batch = join(f.folder, 'batch.json');
    await writeFile(batch, JSON.stringify({ commands: [['first']], restore: [['restore']] }));
    const result = await capture(() => run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.equal(process.exitCode, 2);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['first']);
    assert.equal(f.state.releaseCount, 0);
    assert.equal(f.state.owner, null);
    assert.ok(result.err.split('\n').some(line => {
      try { return (JSON.parse(line) as Record<string, unknown>).editorSessionId === 'replacement-session'; }
      catch { return false; }
    }));
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('a confirmed free release receipt remains successful after a successor acquires', async () => {
  const f = await fixture(2, false, true), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', f.project, '--', 'first'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, undefined);
    assert.equal(f.state.releaseCount, 1);
    assert.deepEqual(f.state.owner, { terminalHandle: 'next', incarnationId: 'next-incarnation', batchId: 'next-batch' });
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('a successor batch from the same Orca terminal does not invalidate settled release', async () => {
  const f = await fixture(2, false, false, true), previousExit = process.exitCode;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', f.project, '--', 'first'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, undefined);
    assert.equal(f.state.releaseCount, 1);
    assert.deepEqual(f.state.owner, { ...identity, batchId: 'next-batch' });
    assert.ok(f.state.observedOwnerBatches.includes('next-batch'));
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('a competing caller waits through held and releasing ownership before acquiring its own lease', async () => {
  for (const busyState of ['held', 'releasing'] as const) {
    const f = await fixture(), previousExit = process.exitCode;
    f.state.busy = true;
    f.state.busyState = busyState;
    process.exitCode = undefined;
    try {
      const pending = capture(() => run(['--project-path', f.project, '--', 'after-wait'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
      await f.state.secondBusyPollActivity;
      assert.equal(f.state.claimAttempts, 1);
      assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
      assert.equal(f.state.releaseCount, 0);
      assert.ok(f.state.observedStates.includes(busyState));
      f.state.busy = false;
      const result = await pending;
      assert.equal('error' in result, false);
      assert.equal(f.state.claimAttempts, 2);
      assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['after-wait']);
      assert.equal(f.state.releaseCount, 1);
      assert.equal(f.state.owner, null);
      assert.ok(f.state.busyPolls >= 2);
      const waitingReports = result.err.split('\n').flatMap(line => {
        try {
          const status = JSON.parse(line) as { owner?: { batchId?: string }; state?: string; operation?: string; reason?: string; activeOperations?: number };
          return status.owner?.batchId === 'other-batch' ? [JSON.stringify([status.state, status.owner.batchId, status.operation, status.reason, status.activeOperations])] : [];
        } catch { return []; }
      });
      assert.equal(new Set(waitingReports).size, waitingReports.length);
      assert.equal(waitingReports.length, 2);
      assert.ok(waitingReports.some(report => report.includes('owned by other')));
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});

test('a waiting caller acquires after release when Pipeline omits nullable status fields', async () => {
  const f = await fixture(2, false, false, false, true), previousExit = process.exitCode;
  f.state.busy = true;
  process.exitCode = undefined;
  try {
    const pending = capture(() => run(['--project-path', f.project, '--', 'after-release'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    await f.state.busyPollActivity;
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 0);
    f.state.busy = false;
    const result = await pending;
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, undefined);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['after-release']);
    assert.equal(f.state.owner, null);
    assert.equal(f.state.releaseCount, 1);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('a competing claim between free observation and retry causes another wait, not insertion', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.busy = true;
  process.exitCode = undefined;
  try {
    const pending = capture(() => run(['--project-path', f.project, '--', 'after-race'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    await f.state.busyPollActivity;
    f.state.busy = false;
    f.state.conflictOnNextClaim = true;
    await f.state.secondConflictActivity;
    await f.state.secondBusyPollActivity;
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    f.state.busy = false;
    const result = await pending;
    assert.equal('error' in result, false);
    assert.equal(f.state.claimAttempts, 3);
    assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['after-race']);
    assert.equal(f.state.releaseCount, 1);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('a session change between rejected claims stops any further retry', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.busy = true;
  process.exitCode = undefined;
  try {
    const pending = capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    await f.state.busyPollActivity;
    f.state.busy = false;
    f.state.conflictOnNextClaim = true;
    f.state.sessionChangesOnNextConflict = true;
    const result = await pending;
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, 2);
    assert.equal(f.state.claimAttempts, 2);
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 0);
    assert.match(result.err, /session changed between rejected claims/);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('a session change on a confirmed claim runs no command and releases only that lease', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.busy = true;
  process.exitCode = undefined;
  try {
    const pending = capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    await f.state.busyPollActivity;
    f.state.busy = false;
    f.state.sessionChangesOnSuccessfulClaim = true;
    const result = await pending;
    assert.ok('error' in result);
    assert.match(String(result.error), /session changed while waiting/);
    assert.equal(process.exitCode, 2);
    assert.equal(f.state.claimAttempts, 2);
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('claim waiting re-reads the canonical descriptor after an Editor reload', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.busy = true;
  process.exitCode = undefined;
  try {
    const pending = capture(() => run(['--project-path', f.project, '--', 'after-reload'], { unityPath: f.fakeUnity, identity, pollInterval: 5 }));
    await f.state.busyPollActivity;
    const descriptorPath = join(f.project, 'Library', 'Pipeline', '.unity-pipeline-port');
    const current = JSON.parse(await readFile(descriptorPath, 'utf8')) as Record<string, unknown>;
    current.evalToken = 'reloaded-pipeline-secret';
    await writeFile(descriptorPath, JSON.stringify(current));
    f.state.evalToken = 'reloaded-pipeline-secret';
    f.state.busy = false;
    const result = await pending;
    assert.equal('error' in result, false);
    assert.equal(f.state.claimAttempts, 2);
    assert.equal(f.state.releaseCount, 1);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('SIGINT and SIGTERM during claim waiting stop without commands or release', async () => {
  for (const signal of ['SIGINT', 'SIGTERM'] as const) {
    const f = await fixture(), previousExit = process.exitCode;
    f.state.busy = true;
    process.exitCode = undefined;
    try {
      const pending = capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 50 }));
      await f.state.busyPollActivity;
      process.emit(signal);
      const result = await pending;
      assert.equal('error' in result, false);
      assert.equal(process.exitCode, signal === 'SIGTERM' ? 143 : 130);
      assert.equal(f.state.claimAttempts, 1);
      assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
      assert.equal(f.state.releaseCount, 0);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});

test('blocked or unknown ownership while waiting never permits a retry', async () => {
  for (const status of [
    { success: false, state: 'blocked', settled: false, editorSessionId: 'session-fixture', owner: null, activeOperations: null, reason: 'unknown work' },
    { success: true, state: 'unrecognized', settled: false, editorSessionId: 'session-fixture', owner: null, activeOperations: null, reason: null },
  ]) {
    const f = await fixture(), previousExit = process.exitCode;
    f.state.busy = true;
    process.exitCode = undefined;
    try {
      const pending = capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 5 }));
      await f.state.busyPollActivity;
      f.state.statusOverride = status;
      const result = await pending;
      assert.equal('error' in result, false);
      assert.equal(process.exitCode, 2);
      assert.equal(f.state.claimAttempts, 1);
      assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
      assert.equal(f.state.releaseCount, 0);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});

test('an invalid claim conflict fails closed without retry', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.busy = true;
  f.state.invalidConflict = true;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.equal('error' in result, false);
    assert.equal(process.exitCode, 2);
    assert.equal(f.state.claimAttempts, 1);
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('an ambiguous claim response is never replayed or released', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.loseNextClaimResponse = true;
  process.exitCode = undefined;
  try {
    const result = await capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    assert.ok('error' in result);
    assert.match(String(result.error), /Claim response was lost/);
    assert.equal(f.state.claimAttempts, 1);
    assert.equal(f.state.calls.filter(call => call.path === '/api/editor-ownership/claim').length, 1);
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('Editor process or session mismatch while waiting blocks successor acquisition', async () => {
  for (const mismatch of ['process', 'session'] as const) {
    const f = await fixture(), previousExit = process.exitCode;
    f.state.busy = true;
    process.exitCode = undefined;
    try {
      const pending = capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 5 }));
      await f.state.busyPollActivity;
      if (mismatch === 'session') f.state.sessionId = 'replacement-session';
      else {
        const descriptorPath = join(f.project, 'Library', 'Pipeline', '.unity-pipeline-port');
        const current = JSON.parse(await readFile(descriptorPath, 'utf8')) as Record<string, unknown>;
        current.pid = process.pid + 1;
        await writeFile(descriptorPath, JSON.stringify(current));
      }
      const result = await pending;
      assert.equal('error' in result, false);
      assert.equal(process.exitCode, 2);
      assert.equal(f.state.claimAttempts, 1);
      assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
      assert.equal(f.state.releaseCount, 0);
    } finally { process.exitCode = previousExit; await f.close(); }
  }
});

test('duplicate Unity CLI command and pipeline list prefixes are rejected', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  try {
    for (const command of [['command', 'recompile'], ['pipeline', 'list']]) {
      await assert.rejects(run(['--project-path', f.project, '--', ...command], { unityPath: f.fakeUnity, identity }));
    }
    const batch = join(f.folder, 'invalid-prefix.json');
    await writeFile(batch, JSON.stringify({ commands: [['pipeline', 'list']] }));
    await assert.rejects(run(['--project-path', f.project, '--batch', batch], { unityPath: f.fakeUnity, identity }));
    assert.equal(f.state.calls.length, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('proxy flags are rejected before a claim', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  try {
    await assert.rejects(run(['--project-path', f.project, '--', 'do-thing', '--proxy=http://localhost:9'], { unityPath: f.fakeUnity, identity }));
    assert.equal(f.state.calls.length, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('public status redacts descriptor credentials', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  try {
    const result = await capture(() => run(['status', '--project-path', f.project]));
    assert.equal('error' in result, false);
    assert.equal(JSON.parse(result.out).state, 'free');
    assert.equal(result.out.includes(bearer), false);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('descriptor project mismatch is rejected before any API call', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  try {
    const path = join(f.project, 'Library', 'Pipeline', '.unity-pipeline-port');
    const value = JSON.parse(await readFile(path, 'utf8')) as Record<string, unknown>;
    value.projectPath = join(f.folder, 'different-project');
    await writeFile(path, JSON.stringify(value));
    await assert.rejects(run(['status', '--project-path', f.project]));
    await assert.rejects(run(['preflight', '--project-path', f.project]));
    assert.equal(f.state.calls.length, 0);
  } finally { process.exitCode = previousExit; await f.close(); }
});
test('a signal during claim prevents commands and releases the confirmed lease', async () => {
  const f = await fixture(), previousExit = process.exitCode;
  f.state.signalDuringClaim = true;
  process.exitCode = undefined;
  try {
    const pending = capture(() => run(['--project-path', f.project, '--', 'must-not-run'], { unityPath: f.fakeUnity, identity, pollInterval: 1 }));
    await f.state.claimActivity;
    process.emit('SIGINT');
    f.state.releaseClaim();
    const result = await pending;
    assert.ok('error' in result);
    assert.equal(process.exitCode, 1);
    assert.equal(f.state.calls.some(call => call.path === '/api/exec'), false);
    assert.equal(f.state.releaseCount, 1);
    assert.equal(f.state.owner, null);
  } finally { process.exitCode = previousExit; await f.close(); }
});

test('SIGINT and SIGTERM wait for active work, restore, and release only after settlement', async () => {
  for (const signal of ['SIGINT', 'SIGTERM'] as const) {
    const f = await fixture(100);
    const runner = join(f.folder, 'interrupt.mjs');
    const batch = join(f.folder, 'interrupt-batch.json');
    await writeFile(batch, JSON.stringify({ commands: [['first'], ['must-not-run']], restore: [['restore']] }));
    const gatewayUrl = new URL('./unity-editor-gateway.ts', import.meta.url).href;
    await writeFile(runner, `import { run } from ${JSON.stringify(gatewayUrl)};\nrun(['--project-path',${JSON.stringify(f.project)},'--batch',${JSON.stringify(batch)}],{unityPath:${JSON.stringify(f.fakeUnity)},identity:${JSON.stringify(identity)},pollInterval:5}).catch(()=>{process.exitCode=1});\n`);
    const child = spawn(process.execPath, [runner], { stdio: 'ignore' });
    const { promise, resolve } = Promise.withResolvers<{ code: number | null; signal: NodeJS.Signals | null }>();
    child.once('close', (code, closedBy) => resolve({ code, signal: closedBy }));
    try {
      await Promise.race([f.waitForCommandActivity, promise.then(() => { throw new Error('Gateway exited before admitting work.'); })]);
      child.kill(signal);
      const result = await promise;
      assert.equal(result.code, 1);
      assert.deepEqual(f.state.calls.filter(call => call.path === '/api/exec').map(call => call.body.command), ['first', 'restore']);
      assert.equal(f.state.releaseCount, 1);
      assert.equal(f.state.owner, null);
      const releaseAt = f.state.observedStates.indexOf('releasing');
      const settledAt = f.state.observedStates.lastIndexOf('free');
      assert.ok(releaseAt >= 0 && settledAt > releaseAt);
    } finally {
      if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
      await f.close();
    }
  }
});
