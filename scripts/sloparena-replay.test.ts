import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { parseJsonStream, planReplay, runReplay, summarize, validateScenario, validateSteps } from './sloparena-replay.ts';

const scenario = { version: 1 as const, stage: 'fightguy_sanctuary', fighter: 'fightguy', inputs: 'inputs.json',
  captures: [{ tick: 601, name: 'landing', camera: { yaw: 90, pitch: 25 } }], expect: { completed: true, targets: 20, falls: 0 } };
const receipt = (command: string, result: any) => ({ success: true, data: { success: true, command, result } });
const ended = { phase: 'Completed', brokenTargets: 20, checkpoint: 2, fighter: { Deaths: 0 } };
function proof() {
  return [receipt('sloparena.replay.run', { success: true, state: ended, result: { completedTicks: 601, events: [{ kind: 'Completed' }] }, provenance: { StageHash: 'stage' } }),
    receipt('sloparena.replay.capture', { success: true, tick: 601, capture: { freshness: 'observed-game-render' } }),
    receipt('sloparena.replay.restore', { success: true, restored: true }),
    { success: true, state: 'free', settled: true, activeOperations: 0 }];
}

test('chunking and capture cuts preserve held inputs and apply edges exactly once', () => {
  const steps = validateSteps([{ ticks: 1250, moveY: 1, jumpPressed: true, jumpHeld: true, slot: 'R', toggleLock: true, shieldPressed: true }]);
  const plan = planReplay({ ...scenario, captures: [{ tick: 601, name: 'one' }, { tick: 601, name: 'reverse' }, { tick: 900, name: 'two' }] }, steps, 'tmp/replays/test', 'session');
  assert.deepEqual(plan.chunks.map(x => x.reduce((sum, y) => sum + y.ticks, 0)), [600, 1, 299, 350]);
  const frames = plan.chunks.flatMap(chunk => chunk.flatMap(step => Array.from({ length: step.ticks }, (_, tick) => ({
    moveY: step.moveY, jumpHeld: step.jumpHeld, jump: tick === 0 && step.jumpPressed === true,
    slot: tick === 0 ? step.slot : undefined, lock: tick === 0 && step.toggleLock === true,
  }))));
  assert.equal(frames.length, 1250); assert.ok(frames.every(x => x.moveY === 1 && x.jumpHeld === true));
  assert.equal(frames.filter(x => x.jump).length, 1); assert.equal(frames.filter(x => x.slot === 'R').length, 1); assert.equal(frames.filter(x => x.lock).length, 1);
  assert.equal(plan.batch.commands.filter(x => x[0] === 'sloparena.replay.capture').length, 3);
  assert.deepEqual(plan.batch.restore, [['sloparena.replay.stop', '--session', 'session'], ['sloparena.replay.restore', '--session', 'session']]);
});

test('a later original step retains its button press even when a prior step was cut', () => {
  const steps = [{ ticks: 610, jumpPressed: true }, { ticks: 3, jumpPressed: true, slot: 'E' }];
  const plan = planReplay({ ...scenario, captures: [] }, steps, 'tmp/replays/test', 'session');
  assert.deepEqual(plan.chunks[1], [{ ticks: 10 }, { ticks: 3, jumpPressed: true, slot: 'E' }]);
});

test('scenario and input validation rejects mistakes before touching Unity', () => {
  for (const step of [{ ticks: 0 }, { ticks: 1, jumpPressed: 'true' }, { ticks: 1, moveX: 1, moveY: 1 },
    { ticks: 1, slot: 'Q' }, { ticks: 1, targetEntityId: '256' }, { ticks: 1, aimYaw: 18001 },
    { ticks: 1, moveY: NaN }, { ticks: 1, teleport: true }, { ticks: 36001 }]) assert.throws(() => validateSteps([step]));
  assert.doesNotThrow(() => validateSteps([{ ticks: 600, jumpHeld: true, targetEntityId: '128', slot: 'E', lockMode: 'OnHit' }]));
  for (const changed of [{ version: 2 }, { stage: '../other' }, { captures: [{ tick: 602, name: 'late' }] },
    { captures: [{ tick: 1, name: '../escape' }] }, { captures: [{ tick: 1, name: 'x' }, { tick: 2, name: 'x' }] },
    { captures: [{ tick: 1, name: 'x', camera: { yaw: 0, pitch: 46 } }] }, { expect: { completed: 'yes' } }, { extra: true }]) {
    assert.throws(() => validateScenario({ ...scenario, ...changed }, 601));
  }
});

test('gateway stream decoding handles nested/escaped braces and rejects incomplete proof', () => {
  const documents = proof(); documents[0].data.result.message = 'quote " and brace } and slash \\';
  const stream = documents.map(x => JSON.stringify(x, null, 2)).join('\n');
  assert.deepEqual(parseJsonStream(stream), documents);
  assert.throws(() => parseJsonStream(stream.slice(0, -3)), /Incomplete/);
  assert.throws(() => parseJsonStream('noise ' + stream), /Non-JSON/);
});

test('reports require expectations, fresh captures, restoration and release evidence', () => {
  const good = summarize(scenario, proof(), 0);
  assert.equal(good.success, true); assert.equal(good.events.length, 1); assert.equal(good.completedInputTicks, 601);
  const mismatched = summarize({ ...scenario, expect: { ...scenario.expect, falls: 1 } }, proof(), 0);
  assert.equal(mismatched.success, false); assert.match(mismatched.failures.join(), /falls/);
  assert.equal(summarize(scenario, proof().slice(0, -1), 0).success, false);
  const noRestore = proof().filter(x => x.data?.command !== 'sloparena.replay.restore');
  assert.match(summarize(scenario, noRestore, 1).failures.join(), /restoration/);
  const stale = proof(); stale[1].data.result.capture.freshness = 'last-rendered-unverified';
  assert.equal(summarize(scenario, stale, 0).success, false);
});

test('runner uses one gateway batch, retains failed evidence, and never overwrites a prior run', async () => {
  const folder = await mkdtemp(join(tmpdir(), 'sloparena-replay-test-'));
  try {
    const project = join(folder, 'client/Unity');
    await mkdir(join(project, 'Assets'), { recursive: true }); await mkdir(join(project, 'ProjectSettings'));
    await writeFile(join(project, 'ProjectSettings/ProjectVersion.txt'), 'test');
    await writeFile(join(folder, 'scenario.json'), JSON.stringify(scenario));
    await writeFile(join(folder, 'inputs.json'), JSON.stringify([{ ticks: 601 }]));
    let calls = 0;
    const invoke = async (args: string[]) => {
      calls++; assert.equal(args[0], '--project-path');
      const batch = JSON.parse(await readFile(args.at(-1)!, 'utf8'));
      assert.deepEqual(batch.restore.map((x: string[]) => x[0]), ['sloparena.replay.stop', 'sloparena.replay.restore']);
      assert.equal(batch.commands.filter((x: string[]) => x[0] === 'sloparena.replay.prepare').length, 1);
      return { code: 0, stdout: proof().map(x => JSON.stringify(x, null, 2)).join('\n'), stderr: 'diagnostic' };
    };
    const options = { scenario: join(folder, 'scenario.json'), projectPath: project, output: 'tmp/replays/test' };
    const report = await runReplay(options, invoke); assert.equal(report.success, true); assert.equal(calls, 1);
    await assert.rejects(runReplay(options, invoke), /EEXIST/); assert.equal(calls, 1);
    assert.equal(JSON.parse(await readFile(join(folder, 'tmp/replays/test/report.json'), 'utf8')).success, true);
    const failed = await runReplay({ ...options, output: 'tmp/replays/failure' }, async () => ({ code: 1, stdout: '{"incomplete":', stderr: 'failure' }));
    assert.equal(failed.success, false); assert.equal(failed.restoration, null);
    assert.equal(await readFile(join(folder, 'tmp/replays/failure/gateway.stderr.log'), 'utf8'), 'failure');
    await mkdir(join(folder, 'tmp/linked-parent'), { recursive: true });
    await symlink(join(project, 'Assets'), join(folder, 'tmp/linked-parent/assets'));
    await assert.rejects(runReplay({ ...options, output: 'tmp/linked-parent/assets/no-import' }, invoke), /symlinks/);
  } finally { await rm(folder, { recursive: true, force: true }); }
});
