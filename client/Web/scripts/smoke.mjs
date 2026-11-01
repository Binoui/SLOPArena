// Headless match through the published WASM sim.
//   node smoke.mjs brawl after_hours fightguy,manki,nyx,bashie
//   node smoke.mjs slopball slopball fightguy        (a scripted P1 chases and swats the ball)
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
const repo = new URL('../../../', import.meta.url).pathname;
const [mode = 'brawl', arenaName = 'rec_center_roof', pkgs = 'fightguy,manki'] = process.argv.slice(2);
const { dotnet } = await import(join(repo, 'client/Web/publish/wwwroot/_framework/dotnet.js'));
const t0 = performance.now();
const { getAssemblyExports, getConfig } = await dotnet.create();
const { Demo } = await getAssemblyExports(getConfig().mainAssemblyName);
console.log('runtime up', (performance.now() - t0).toFixed(0), 'ms');
const cooked = join(repo, 'content-cooked');
const roster = readFileSync(join(cooked, 'roster/manifest.json'), 'utf8');
Demo.SetRoster(roster);
for (const e of JSON.parse(roster).entries) {
  const f = n => new Uint8Array(readFileSync(join(cooked, e.packageId, n)));
  const err = Demo.AddPackage(e.packageId, f('manifest.json'), f('character.runtime.json'), f('poses.bin'), f('client.bindings'));
  if (err) console.log(e.packageId, err);
}
const cat = JSON.parse(Demo.BuildCatalog());
console.log('catalog', cat.error ?? cat.fighters.map(f => f.id).join(','));
console.log('arena', Demo.LoadArena(new Uint8Array(readFileSync(join(repo, 'data/arenas', arenaName + '.arena')))));
const start = JSON.parse(Demo.Start(mode, pkgs, 3, 1, 42));
console.log('start', JSON.stringify(start));
if (start.error) process.exit(1);

const buf = new Uint8Array(22), view = new DataView(buf.buffer);
function chase(self, ball) {
  buf.fill(0);
  const home = ball.p[2] > 0 ? [0, -7] : [ball.p[0], ball.p[2]];
  const dx = home[0] - self.p[0], dz = home[1] - self.p[2], d = Math.hypot(dx, dz);
  if (d > 0.6) { view.setFloat32(0, dx / d, true); view.setFloat32(4, dz / d, true); }
  const yaw = Math.round(Math.atan2(ball.p[0] - self.p[0], ball.p[2] - self.p[2]) * 180 / Math.PI * 100);
  view.setInt16(10, yaw, true); view.setInt16(12, yaw, true);
  const reach = Math.hypot(ball.p[0] - self.p[0], ball.p[1] - self.p[1], ball.p[2] - self.p[2]);
  if (ball.active && reach < 2.6 && ++swing % 6 === 0) buf[9] = 3;
  buf[20] = 1; buf[21] = Demo.ProtocolVersion();
  return buf;
}
let swing = 0;

assert.deepEqual(start.deck.map(row => row.key), ['dash', 'slot1', 'slot2', 'slot3', 'A', 'E', 'R', 'F']);
if (mode === 'brawl') {
  const special = start.deck.find(row => row.key === 'A' && !row.locked && row.max > 0);
  assert(special, 'P1 has a special with a cooldown');
  const neutral = new Uint8Array(22);
  neutral[21] = Demo.ProtocolVersion();
  let probe = JSON.parse(Demo.Step(neutral));
  for (let i = 0; i < 180 && !probe.fighters[0].grounded; i++) probe = JSON.parse(Demo.Step(neutral));
  const cast = neutral.slice(); cast[9] = special.slot;
  probe = JSON.parse(Demo.Step(cast));
  for (let i = 0; i < 180 && probe.fighters[0].cooldowns[special.slot] === 0; i++) probe = JSON.parse(Demo.Step(neutral));
  const began = probe.fighters[0].cooldowns[special.slot], initialDeaths = probe.fighters[0].deaths;
  assert(began > 0, 'using a special publishes a nonzero cooldown');
  let previous = began, decreased = false;
  for (let i = 0; i < special.max + 180 && previous > 0; i++) {
    probe = JSON.parse(Demo.Step(neutral));
    const current = probe.fighters[0].cooldowns[special.slot];
    assert(current <= previous, 'the cooldown does not increase without another cast');
    assert(previous - current <= 1, 'the cooldown counts down instead of resetting');
    decreased ||= current < previous;
    previous = current;
  }
  assert(decreased && previous === 0, 'the special becomes ready again');
  assert.equal(probe.fighters[0].deaths, initialDeaths, 'the cooldown completes without a respawn reset');
  console.log(`special cooldown: ${special.name}, ${began} ticks -> 0`);
  Demo.Start(mode, pkgs, 3, 1, 42);
}

// Tutorial: run for the first lesson, land a real quick hit in Attacks, skip the rest through the pause
// command, and check the knockout setup and the end.
//   node smoke.mjs tutorial license_test fightguy,bonk
if (mode === 'tutorial') {
  const fail = message => { console.error('smoke: ' + message); process.exit(1); };
  const court = start.court;
  const toward = (self, p, slot = 0) => {
    buf.fill(0);
    const dx = p[0] - self.p[0], dz = p[2] - self.p[2], d = Math.hypot(dx, dz);
    if (d > 1.4) { view.setFloat32(0, dx / d, true); view.setFloat32(4, dz / d, true); }
    const yaw = Math.round(Math.atan2(dx, dz) * 18000 / Math.PI);
    view.setInt16(10, yaw, true); view.setInt16(12, yaw, true);
    buf[9] = d <= 1.6 ? slot : 0;
    buf[20] = 1; buf[21] = Demo.ProtocolVersion();
    return buf;
  };
  let frame = JSON.parse(Demo.Step(new Uint8Array(0)));
  const lesson = () => court.lessons[frame.mode.lesson].id;
  const until = (done, input, limit) => {
    for (let i = 0; i < limit && !done(); i++) frame = JSON.parse(Demo.Step(input()));
    return done();
  };
  console.log('lessons', court.lessons.map(l => l.id).join(','), 'moves', JSON.stringify(court.moves));
  until(() => false, () => toward(frame.fighters[0], [frame.fighters[0].p[0] + 5, 0, frame.fighters[0].p[2] + 5]), 180);
  console.log('run after 3 s:', frame.mode.amounts[0], 'm');
  if (!(frame.mode.amounts[0] > 5)) fail('running did not count toward Move & Look');
  const skip = () => {
    const from = frame.mode.lesson;
    Demo.Command('skip');
    if (!until(() => frame.mode.lesson !== from || frame.ended, () => new Uint8Array(0), 5)) fail('skip did not advance from ' + court.lessons[from].id);
  };
  while (lesson() !== 'Attacks') skip();
  const passed = until(() => frame.mode.passed || frame.mode.amounts[0] >= 1,
    () => toward(frame.fighters[0], frame.fighters[1].p, 3), 600);
  console.log('Attacks quick hit:', passed, 'dummy dmg', frame.fighters[1].dmg);
  if (!passed) fail('a ground Slot1 hit did not count in Attacks');
  while (lesson() !== 'Knockout') skip();
  const dummy = frame.fighters[1];
  console.log('Knockout setup: dummy dmg', dummy.dmg, 'at', dummy.p.map(v => v.toFixed(1)).join(','));
  if (dummy.dmg !== 100) fail('the knockout dummy is not at 100%');
  Demo.Command('skip');
  if (!until(() => frame.ended, () => new Uint8Array(0), 200)) fail('the last lesson did not end the tutorial');
  console.log('tutorial ended at tick', frame.tick);
  process.exit(0);
}

let frame = JSON.parse(Demo.Step(new Uint8Array(0)));
const t1 = performance.now(), counts = {};
let ticks = 0, hits = 0, deaths = 0;
for (; ticks < 3600 * 3; ticks++) {
  const input = frame.mode?.ball ? chase(frame.fighters[0], frame.mode.ball) : new Uint8Array(0);
  frame = JSON.parse(Demo.Step(input));
  hits += frame.hits.length; deaths += frame.deaths.length;
  for (const [kind] of frame.mode?.events ?? []) counts[kind] = (counts[kind] ?? 0) + 1;
  if (frame.ended) break;
}
const ms = performance.now() - t1;
console.log(`ticks ${ticks} in ${ms.toFixed(0)} ms = ${(ms / ticks).toFixed(3)} ms/tick; hits ${hits}, deaths ${deaths}, ended ${frame.ended}`);
console.log(frame.fighters.map(f => `${f.pkg} dmg ${f.dmg} deaths ${f.deaths} state ${f.state}`).join(' | '));
if (frame.mode) console.log('slopball events', JSON.stringify(counts), 'best', frame.mode.best, 'heat', frame.mode.heat);
if (mode === 'brawl' && (!frame.ended || hits === 0)) { console.error('smoke: the brawl did not play out'); process.exit(1); }
