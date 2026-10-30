import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync, readdirSync, renameSync, writeFileSync } from 'node:fs';
const side = process.argv[2] ?? 'before';
const out = `tmp/web-vfx/${side}`;
const attempt = `tmp/web-vfx/${side}-attempt`;
mkdirSync(attempt, { recursive: true });
mkdirSync(out, { recursive: true });
const cases = JSON.parse(readFileSync('client/Web/app/vfx/review-cases.json', 'utf8')).filter(c => !process.env.VFX_FIGHTERS || process.env.VFX_FIGHTERS.split(',').includes(c.pkg));
const steps = [];
for (const {pkg, move, ticks, key = `${pkg}-${move}`, options = {}} of cases) {
  steps.push({ eval: `__review.start('${pkg}','${move}',${JSON.stringify(options)})`, ms: 0 });
  for (const tick of ticks) steps.push({ eval: `JSON.stringify({...__review.seek(${tick}),case:${JSON.stringify(key)}})`, ms: 0, shot: `${key}-${tick}` });
}
const spec = { port: side === 'before' ? 9343 : 9344, query: '?vfxreview' + (side === 'before' ? '&legacyvfx' : ''), size: '960x540', pad: true,
  setup: 'document.querySelectorAll("main,header,#startScreen").forEach(e=>e.style.display="none")',
  steps: [{ until: '!!window.__review', timeout: 30000 }, ...steps] };
const child = spawn(process.execPath, ['client/Web/scripts/drive.mjs', attempt, JSON.stringify(spec)], { stdio: ['ignore', 'pipe', 'inherit'] });
let logs = ''; child.stdout.on('data', b => { logs += b; });
const code = await new Promise(r => child.on('exit', r));
const rows = logs.split('\n').filter(line => line.startsWith('eval: {"captured"')).map(line => JSON.parse(line.slice(6)));
const passed = !code && !logs.includes('EXC:') && rows.length === cases.reduce((n, c) => n + c.ticks.length, 0) && logs.includes('BROWSER_ERRORS 0');
writeFileSync(`${passed ? attempt : out}/${passed ? 'capture' : 'latest-failure'}.log`, logs);
if (passed) {
  let saved = rows;
  if (process.env.VFX_FIGHTERS) {
    let previous = [];
    try { previous = JSON.parse(readFileSync(`${out}/captures.json`, 'utf8')); } catch {}
    const merged = new Map(previous.filter(r => r.case).map(r => [`${r.case}:${r.captured}`, r]));
    for (const row of rows) merged.set(`${row.case}:${row.captured}`, row);
    saved = [...merged.values()];
  }
  writeFileSync(`${attempt}/captures.json`, JSON.stringify(saved, null, 2));
  for (const name of readdirSync(attempt)) renameSync(`${attempt}/${name}`, `${out}/${name}`);
}
console.log(side, passed ? `PASS, ${rows.length} captures` : 'FAIL, previous move captures preserved');
if (!passed) process.exitCode = 1;
