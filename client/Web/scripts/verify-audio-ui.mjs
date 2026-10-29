// Runs the shared drive.mjs on our reserved CDP port, with exact CSS viewport sizes.
// Usage: node client/Web/scripts/verify-audio-ui.mjs /tmp/sloparena-audio-ui 1280x720 brawl
// Repeat with 390x844 and slopball. No dependencies beyond drive.mjs / Chrome.
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
const out = process.argv[2] ?? '/tmp/sloparena-audio-ui';
const size = process.argv[3] ?? '1280x720';
const mode = process.argv[4] ?? 'brawl';
const folder = `${out}/${size}-${mode}`;
mkdirSync(folder, { recursive: true });
const report = `console.log('UI_AUDIO_EVIDENCE '+JSON.stringify({audio:SLOPA_AUDIO,width:innerWidth,height:innerHeight,overflow:document.querySelector('#menu').scrollWidth>innerWidth,gameCTA:!document.querySelector('#banner').classList.contains('hidden')&&document.querySelector('#banner').classList.contains('is-result'),inPlay:document.body.classList.contains('playing'),steam:[...document.querySelectorAll('.steam-link')].map(a=>a.href)}))`;
const setup = `globalThis.uiAudioReport=()=>{${report}};uiAudioReport();` + (mode === 'slopball' ? `document.querySelectorAll('#pickMode button')[1].click();` : '');
const steps = mode === 'brawl' ? [
  { down: ['Space'], ms: 180, up: ['Space'], shot: 'hud' },
  { down: ['ShiftLeft'], ms: 2200, up: ['ShiftLeft'] },
  { down: ['Digit1'], ms: 300, up: ['Digit1'] },
  { down: ['KeyA'], ms: 500 },
  { until: `document.querySelector('#bannerText').textContent==='GAME!'`, timeout: 45000, ms: 30, up: ['KeyA'], shot: 'game' },
  { until: '(uiAudioReport(),true)', ms: 3000, shot: 'after-game' },
] : [
  { ms: 6000, shot: 'slopball' },
  { down: ['Digit1'], ms: 300, up: ['Digit1'] },
  { ms: 8000 },
  { until: '(uiAudioReport(),true)', ms: 30 },
  { until: `(dispatchEvent(new KeyboardEvent('keydown',{code:'Escape',key:'Escape'})),true)`, ms: 300, shot: 'slopball-exit' },
];
if (mode === 'slopball' && size === '390x844') steps.push({
  until: `(globalThis.audioProbeResult ??= import('../scripts/probe-audio.mjs').then(m=>m.probeAudio()).catch(e=>{console.error('AUDIO_FRAME_PROBE_FAILED '+e.stack);return true;}))`,
  timeout: 10000, ms: 30,
});
const child = spawn(process.execPath, [new URL('./drive.mjs', import.meta.url).pathname, folder,
  JSON.stringify({ port: 9343, size, query: mode === 'brawl' ? '?stocks=1' : '', setup, steps })], { stdio: ['ignore', 'pipe', 'pipe'] });
let output = '';
child.stdout.on('data', data => { output += data; process.stdout.write(data); });
child.stderr.on('data', data => { output += data; process.stderr.write(data); });
const done = new Promise(resolve => child.once('exit', resolve));
let ws;
for (let i = 0; i < 100; i++) {
  try {
    const list = await (await fetch('http://127.0.0.1:9343/json/list')).json();
    const page = list.find(t => t.type === 'page');
    if (page) { ws = new WebSocket(page.webSocketDebuggerUrl); break; }
  } catch {}
  await new Promise(resolve => setTimeout(resolve, 50));
}
if (!ws) throw new Error('Our Chrome on port 9343 did not start.');
await new Promise(resolve => ws.addEventListener('open', resolve));
const pending = new Map(); let id = 0;
ws.addEventListener('message', event => { const m = JSON.parse(event.data); if (m.id) { pending.get(m.id)?.(m); pending.delete(m.id); } });
const send = (method, params) => new Promise(resolve => { const key = ++id; pending.set(key, resolve); ws.send(JSON.stringify({ id: key, method, params })); });
const [width, height] = size.split('x').map(Number);
// Chrome's window flag includes browser chrome and clamps narrow windows to 500 px.
// An explicit device-metrics override makes drive's captures match the requested viewport.
await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
await send('Network.enable', {});
await send('Network.setCacheDisabled', { cacheDisabled: true });
const code = await done;
writeFileSync(`${folder}/evidence.log`, output);
ws.close();
if (code !== 0) process.exitCode = code ?? 1;
const evidence = output.split('\n').filter(line => line.startsWith('log: UI_AUDIO_EVIDENCE '))
  .map(line => JSON.parse(line.slice('log: UI_AUDIO_EVIDENCE '.length)));
if (/EXC:|AUDIO_FRAME_PROBE_FAILED/.test(output) || !evidence.some(e => e.inPlay)
    || evidence.some(e => e.overflow || e.width !== width || e.height !== height || e.audio.errors.length)
    || (mode === 'brawl' && !evidence.some(e => e.gameCTA))
    || (mode === 'slopball' && size === '390x844' && !output.includes('AUDIO_FRAME_PROBE {"passed":true'))) {
  console.error('Verification failed; inspect evidence.log.');
  process.exitCode = 1;
}
