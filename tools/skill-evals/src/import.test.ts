import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { canonicalSkillName, importSessions } from './import.ts';

test('OMP imports keep zero-skill baselines and force imports preserve manual labels', t => {
  const folder = mkdtempSync(join(tmpdir(), 'skill corpus '));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  const sessions = join(folder, 'sessions');
  const project = join(sessions, 'relocated-SlopArena');
  mkdirSync(project, { recursive: true });
  const log = join(project, 'session.jsonl');
  const output = join(folder, 'observations.jsonl');
  const events = [
    { type: 'session', id: 'fixture-session', timestamp: '2026-10-11T00:00:00Z', cwd: folder },
    { type: 'message', message: { role: 'user', content: [{ type: 'text', text: 'Fix this documentation link.' }] } },
  ];
  writeFileSync(log, events.map(event => JSON.stringify(event)).join('\n'));
  const options = { sessionsRoot: sessions, output, skills: new Set(['sloparena-build']) };
  assert.equal(importSessions(options), 1);
  let observations = readFileSync(output, 'utf8').trim().split('\n').map(line => JSON.parse(line));
  assert.deepEqual(observations[0].loadedSkills, []);
  observations[0].outcome = 'good';
  observations[0].notes = 'Manual result review.';
  writeFileSync(output, observations.map(value => JSON.stringify(value)).join('\n'));
  const read = { type: 'custom', customType: 'tool_execution_start', data: { toolName: 'read', args: { path: 'skill://agents/sloparena-build/SKILL.md' } } };
  const write = { type: 'custom', customType: 'tool_execution_start', data: { toolName: 'write', args: { path: 'skill://agents/sloparena-ops/SKILL.md' } } };
  writeFileSync(log, [...events, read, write].map(event => JSON.stringify(event)).join('\n'));
  assert.equal(importSessions(options), 1);
  assert.deepEqual(JSON.parse(readFileSync(output, 'utf8')).loadedSkills, []);
  assert.equal(importSessions({ ...options, force: true }), 1);
  observations = readFileSync(output, 'utf8').trim().split('\n').map(line => JSON.parse(line));
  assert.deepEqual(observations[0].loadedSkills, ['sloparena-build']);
  assert.equal(observations[0].outcome, 'good');
  assert.equal(observations[0].notes, 'Manual result review.');
});

test('missing OMP logs do not create or overwrite observation files', t => {
  const folder = mkdtempSync(join(tmpdir(), 'absent skill corpus '));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  const sessionsRoot = join(folder, 'missing-sessions');
  const output = join(folder, 'observations.jsonl');
  assert.equal(importSessions({ sessionsRoot, output }), 0);
  assert.equal(existsSync(output), false);
  const existing = JSON.stringify({ id: 'old', prompt: 'Previously annotated', loadedSkills: [], outcome: 'good' }) + '\n';
  writeFileSync(output, existing);
  assert.equal(importSessions({ sessionsRoot, output }), 1);
  assert.equal(readFileSync(output, 'utf8'), existing);
});

test('skill URIs normalize known OMP namespaces without inventing a skill', () => {
  for (const uri of ['skill://sloparena-build/SKILL.md', 'skill://agent/sloparena-build', 'skill://agents/sloparena-build/SKILL.md'])
    assert.equal(canonicalSkillName(uri), 'sloparena-build');
  assert.equal(canonicalSkillName('skill://agents/'), null);
  assert.equal(canonicalSkillName('docs/testing.md'), null);
});
