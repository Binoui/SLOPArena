// Headless match through the published WASM sim.
//   node smoke.mjs brawl after_hours fightguy,manki,nyx,bashie
//   node smoke.mjs slopball slopball fightguy        (a scripted P1 chases and swats the ball)
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
