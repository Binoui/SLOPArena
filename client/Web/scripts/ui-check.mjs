// Characterizes the page's input and menu behaviour in headless Chrome, for refactors of app/main.js.
// Serves the repo, drives four scenarios through drive.mjs and prints one observation per line, so two
// checkouts can be compared with diff:
//   node client/Web/scripts/ui-check.mjs > after.txt
// Each recorded input is `flags|slot|flags2` with the stick-direction bits masked off, plus `m` when the
// move vector is nonzero, so camera drift does not change the output. Neutral `0|0|0` ticks are left out.
import { spawn, spawnSync } from 'node:child_process';
import { rmSync } from 'node:fs';
const repo = new URL('../../../', import.meta.url).pathname;
const drive = new URL('./drive.mjs', import.meta.url).pathname;
const httpPort = 8650, cdpPort = 9350;
const server = spawn('python3', ['-m', 'http.server', String(httpPort), '--bind', '127.0.0.1'], { cwd: repo, stdio: 'ignore' });
process.once('exit', () => server.kill());
await new Promise(r => setTimeout(r, 800));

const setup = mode => `
  Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [globalThis.__pad] });
  globalThis.__pad = { buttons: Array.from({ length: 17 }, () => ({ pressed: false })), axes: [0, 0, 0, 0] };
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  // The page polls the pad once per frame, and a loading stage can stall frames for longer than any fixed hold.
  const frames = n => new Promise(r => { const f = () => --n > 0 ? requestAnimationFrame(f) : r(); requestAnimationFrame(f); });
  globalThis.__hold = ms => Promise.all([sleep(ms), frames(3)]);
  globalThis.__tap = async (i, ms = 150) => { __pad.buttons[i].pressed = true; await __hold(ms); __pad.buttons[i].pressed = false; await __hold(ms); };
  globalThis.__flick = async (axis, v) => { __pad.axes[axis] = v; await __hold(300); __pad.axes[axis] = 0; await __hold(300); };
  globalThis.__seen = new Set();
  globalThis.__ticks = 0;
  const step = Demo.Step;
  Demo.Step = b => {
    __ticks++;
    if (b.length) {
      const v = new DataView(b.buffer);
      __seen.add((b[8] & ~0x0d) + '|' + b[9] + '|' + b[19] + (v.getFloat32(0, true) || v.getFloat32(4, true) ? 'm' : ''));
    }
    return step.call(Demo, b);
  };
  // A neutral tick may or may not land before the press, so it carries no signal.
  globalThis.__report = () => { const s = [...__seen].filter(x => x !== '0|0|0').sort().join(' '); __seen.clear(); return s; };
  globalThis.__state = () => JSON.stringify({
    menu: !document.getElementById('menu').classList.contains('hidden'),
    pause: !document.getElementById('pause').classList.contains('hidden'),
    start: !!document.getElementById('start'),
    focus: document.querySelector('.pad-focus')?.id || document.querySelector('.pad-focus')?.dataset.mode
      || document.querySelector('.pad-focus')?.textContent.trim().slice(0, 20) || null,
    picked: document.querySelector('#pickMode .pick.on')?.dataset.mode,
    hud: document.getElementById('hud').children.length > 0,
  });
  ${mode ? `document.querySelector('#pickMode [data-mode=${mode}]').click();` : ''}
  __seen.clear();`;
const inMatch = { until: `document.getElementById('menu').classList.contains('hidden') && __seen.size > 0`, timeout: 30000 };
// A key stays down until the sim has ticked after it, so a stalled frame cannot push its edge past the report.
const key = code => [
  { eval: '__seen.clear(); globalThis.__from = __ticks', down: [code] },
  { until: '__ticks - __from >= 4', up: [code] },
  { eval: `'${code}: ' + __report()` },
];
const pad = (label, action) => [{ eval: '__seen.clear()' }, { eval: action }, { eval: `'${label}: ' + __report()` }];
const hash = `(() => { let h = 0; for (const c of document.getElementById('menu').innerHTML) h = (h * 31 + c.charCodeAt(0)) | 0; return 'menu hash ' + h; })()`;

const scenarios = {
  'keyboard brawl': { setup: setup('brawl'), steps: [
    inMatch, { eval: hash }, { eval: '__state()' },
    ...['KeyW', 'Space', 'Digit1', 'Digit2', 'Digit3', 'KeyQ', 'KeyE', 'KeyR', 'KeyF', 'ShiftLeft', 'KeyC', 'Digit4', 'KeyX'].flatMap(key),
    { down: ['Escape'], up: ['Escape'] }, { eval: '__state()' },
  ] },
  'keyboard tutorial pause': { setup: setup('tutorial'), steps: [
    inMatch, { eval: '__state()' },
    { down: ['Escape'], up: ['Escape'] }, { eval: '__state()' },
    { eval: '__seen.clear()' }, { down: ['Digit1'], ms: 200, up: ['Digit1'] },
    { down: ['ArrowDown'], up: ['ArrowDown'] }, { eval: '__state()' },
    { down: ['ArrowUp'], up: ['ArrowUp'] }, { eval: '__state()' },
    { down: ['Enter'], up: ['Enter'] }, { eval: '__state()' }, { eval: `'after resume: ' + __report()` },
    { down: ['Escape'], up: ['Escape'] }, { down: ['Escape'], up: ['Escape'] }, { eval: '__state()' },
  ] },
  'gamepad': { pad: true, setup: setup(null), steps: [
    { eval: '__state()' }, { eval: '__tap(0).then(__state)' },
    { eval: '__tap(15).then(__state)' }, { eval: '__tap(0).then(__state)' },
    { eval: '__tap(13).then(__state)' }, { eval: '__tap(14).then(__state)' },
    { eval: `__tap(9)` }, inMatch, { eval: '__state()' },
    ...pad('A', '__tap(0)'), ...pad('X', '__tap(2)'), ...pad('Y', '__tap(3)'), ...pad('B', '__tap(1)'),
    ...pad('stick right', '__flick(2, 1)'), ...pad('stick left', '__flick(2, -1)'),
    ...pad('stick up', '__flick(3, -1)'), ...pad('stick down', '__flick(3, 1)'),
    ...pad('stick twice held', '(async () => { __pad.axes[2] = 1; await __hold(300); __pad.axes[2] = 0.5; await __hold(300); __pad.axes[2] = 0; await __hold(300); })()'),
    ...pad('LB', '__tap(4)'), ...pad('RB', '__tap(5)'), ...pad('LT', '__tap(6)'), ...pad('RT', '__tap(7)'),
    ...pad('dpad right', '__tap(15)'), ...pad('left stick', '__flick(0, 1)'),
    { eval: '__tap(9).then(__state)' },
    { eval: `document.querySelector('#pickMode [data-mode=tutorial]').click(); __state()` },
    { eval: `__tap(9)` }, inMatch,
    { eval: '__tap(9).then(__state)' },
    { eval: '__seen.clear(); __pad.buttons[0].pressed = true; __hold(300).then(__state)' },
    { eval: `'A held through resume: ' + __report()` }, { eval: '__pad.buttons[0].pressed = false; __hold(300).then(() => {})' },
    { eval: '__tap(0).then(() => "A after release: " + __report())' },
  ] },
  'keyboard slopball': { setup: setup('slopball'), steps: [
    inMatch, { ms: 2000 }, { eval: `'returns ' + !!document.getElementById('returns') + ' ' + __state()` },
    { down: ['Escape'], up: ['Escape'] }, { eval: '__state()' },
  ] },
};

let failed = false;
for (const [name, script] of Object.entries(scenarios)) {
  rmSync(`/tmp/webdemo-chrome-${cdpPort}`, { recursive: true, force: true, maxRetries: 5 });
  const run = spawnSync('node', [drive, `${repo}tmp/ui-check/${name.replaceAll(' ', '-')}`,
    JSON.stringify({ ...script, port: cdpPort, url: `http://127.0.0.1:${httpPort}/client/Web/app/index.html` })], { encoding: 'utf8' });
  console.log(`## ${name}`);
  for (const line of run.stdout.split('\n')) {
    if (line.startsWith('eval: ') && line !== 'eval: undefined' && !/^eval: (true|false|\d+)$/.test(line)) console.log(line.slice(6));
    if (line.startsWith('BROWSER_ERRORS')) console.log(line);
  }
  if (run.status !== 0) { failed = true; console.log(`exit ${run.status}`, run.stderr.split('\n').slice(-3).join(' ')); }
}
rmSync(`/tmp/webdemo-chrome-${cdpPort}`, { recursive: true, force: true, maxRetries: 5 });
process.exit(failed ? 1 : 0);
