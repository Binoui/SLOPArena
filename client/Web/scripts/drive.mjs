// Drives the demo in headless Chrome over CDP: boot, start a match, play inputs, capture screenshots.
// Usage: node drive.mjs <outDir> '{"url"?, "query"?, "size"?: "390x844", "port"?: 9334, "setup"?: js, "bench"?: true, "pad"?: true, "steps": [{"eval"?: js, "down"?: [codes], "ms"?, "up"?: [codes], "until"?: js, "timeout"?, "shot"?: name}]}'
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
const out = process.argv[2] ?? '/tmp/webdemo-shots';
const script = JSON.parse(process.argv[3] ?? '{}');
mkdirSync(out, { recursive: true });
const port = Number(script.port ?? 9333);
const [width, height] = (script.size ?? '1280x720').split('x').map(Number);
const chrome = spawn('google-chrome-stable', ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=/tmp/webdemo-chrome-${port}`,
  `--window-size=${width},${height}`, '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--no-first-run', '--mute-audio', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let ws;
for (let i = 0; i < 50 && !ws; i++) {
  try {
    const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    const page = list.find(t => t.type === 'page');
    if (page) ws = new WebSocket(page.webSocketDebuggerUrl);
  } catch { await sleep(200); }
}
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const pending = new Map(); const logs = [];
ws.addEventListener('message', e => {
  const m = JSON.parse(e.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  if (m.method === 'Runtime.consoleAPICalled') logs.push(m.params.type + ': ' + m.params.args.map(a => a.value ?? a.description).join(' '));
  if (m.method === 'Runtime.exceptionThrown') logs.push('EXC: ' + JSON.stringify(m.params.exceptionDetails).slice(0, 600));
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const evalJs = async expr => {
  const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
  if (r.error || r.result?.exceptionDetails) {
    ws.close(); chrome.kill();
    throw new Error(JSON.stringify(r.error ?? r.result.exceptionDetails));
  }
  return r.result?.result?.value;
};
const shot = async name => { const r = await send('Page.captureScreenshot', { format: 'png' }); writeFileSync(`${out}/${name}.png`, Buffer.from(r.result.data, 'base64')); };
const CODES = { Space: [' ', 32], KeyW: ['w', 87], KeyA: ['a', 65], KeyS: ['s', 83], KeyD: ['d', 68], Digit1: ['1', 49], Digit2: ['2', 50], Digit3: ['3', 51], Digit4: ['4', 52], KeyQ: ['q', 81], KeyE: ['e', 69], KeyR: ['r', 82], KeyF: ['f', 70], Enter: ['Enter', 13], Escape: ['Escape', 27], ShiftLeft: ['Shift', 16], KeyH: ['h', 72] };
const key = async (code, type) => { const [k, vk] = CODES[code]; await send('Input.dispatchKeyEvent', { type, code, key: k, windowsVirtualKeyCode: vk }); };
await send('Runtime.enable'); await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
await send('Network.enable'); await send('Network.setCacheDisabled', { cacheDisabled: true });
await send('Page.navigate', { url: (script.url ?? 'http://127.0.0.1:8642/client/Web/app/index.html') + (script.query ?? '') });
for (let i = 0; i < 100; i++) { await sleep(200); if (script.document ? (await evalJs('document.readyState')) === 'complete' : script.lineup ? (await evalJs('document.title')) === 'ready' : (await evalJs(`document.getElementById('status')?.textContent`) ?? '').startsWith('ready')) break; }
const ready = await evalJs(`document.getElementById('status')?.textContent`);
console.log('status:', ready);
if (!script.document && !script.lineup && !ready?.startsWith('ready')) {
  writeFileSync(`${out}/browser-log.json`, JSON.stringify(logs, null, 2));
  console.log(logs.join('\n')); ws.close(); chrome.kill();
  throw new Error('Demo did not reach ready state');
}
await shot('0-menu');
if (script.lineup) { ws.close(); chrome.kill(); process.exit(0); }
if (!script.document && !script.pad) { await key('Escape', 'keyDown'); await key('Escape', 'keyUp'); }
await evalJs(script.setup ?? '');
if (!script.document && !script.pad) { await key('Enter', 'keyDown'); await key('Enter', 'keyUp'); }
await sleep(script.document ? 50 : 1500); await shot('1-start');
for (const [i, step] of (script.steps ?? []).entries()) {
  if (step.until) {
    const t = Date.now();
    while (Date.now() - t < (step.timeout ?? 60000) && !(await evalJs(step.until))) await sleep(250);
    console.log('until', step.until, '->', await evalJs(step.until), `after ${((Date.now() - t) / 1000).toFixed(1)} s`);
  }
  if (step.cdp) { const result = await send(step.cdp, step.params ?? {}); if (step.save) writeFileSync(`${out}/${step.save}`, JSON.stringify(result.result)); }
  if (step.eval) console.log('eval:', await evalJs(step.eval));
  for (const c of step.down ?? []) await key(c, 'keyDown');
  await sleep(step.ms ?? 300);
  for (const c of step.up ?? []) await key(c, 'keyUp');
  if (step.shot) await shot(`${2 + i}-${step.shot}`);
}
if (script.bench) console.log('bench:', await evalJs(`(() => { const t = performance.now(); let n = 0; for (; n < 600; n++) JSON.parse(Demo.Step(new Uint8Array(0))); return ((performance.now() - t) / n).toFixed(3) + ' ms/tick (600 ticks, render loop blocked)'; })()`));
console.log('status:', await evalJs(`document.getElementById('status')?.textContent`));
console.log('hud:', await evalJs(`document.getElementById('hud')?.innerText.replace(/\\n+/g,' | ')`));
writeFileSync(`${out}/browser-log.json`, JSON.stringify(logs, null, 2));
const errors = logs.filter(l => l.startsWith('error:') || l.startsWith('EXC:') || l.includes('Shader Error'));
console.log([...logs.slice(0, 30), ...errors.filter(l => !logs.slice(0, 30).includes(l))].join('\n'));
console.log('BROWSER_ERRORS', errors.length);
ws.close(); chrome.kill();
process.exit(errors.length ? 1 : 0);
