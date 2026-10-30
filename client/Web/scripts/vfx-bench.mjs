import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
const label = process.argv[2] ?? 'before';
const legacy = process.argv.includes('--legacy');
const paired = process.argv.includes('--paired');
const runs = Number(process.env.VFX_BENCH_RUNS ?? 5), results = [];
for (let trial = 0; trial < runs * (paired ? 2 : 1); trial++) {
  const run = Math.floor(trial / (paired ? 2 : 1));
  const baseline = paired ? trial % 2 === 0 : legacy;
  const side = paired ? (baseline ? 'before' : 'after') : label;
  const out = `tmp/web-vfx/perf-${side}-${run}`; mkdirSync(out, { recursive: true });
  const script = { port: 9345, size: '960x540', query: '?autoplay&stocks=99&vfxbench' + (baseline ? '&legacyvfx' : ''),
    setup: `(() => {const start=Demo.Start;Demo.Start=(m,p,s,d,seed)=>start(m,p,s,d,884);for(const name of ['Bashie','Sir Bonk']) [...document.querySelectorAll('#pickCpu .pick')].find(b=>b.textContent===name)?.click();[...document.querySelectorAll('#pickLevel .pick')].find(b=>b.textContent==='Hard').click();})()`,
    steps: [{ until: 'window.__vfxBench?.ticks >= 120', timeout: 15000 }, { eval: `__vfxBench.cpu=[];__vfxBench.raf=[];0`, ms: 5000 },
      { eval: `JSON.stringify((()=>{const b=__vfxBench;const stat=a=>{const s=a.slice().sort((a,b)=>a-b);return {n:s.length,p50:s[Math.floor(s.length*.5)],p95:s[Math.floor(s.length*.95)],min:s[0],max:s.at(-1)}};return {cpu:stat(b.cpu),raf:stat(b.raf),ticks:b.ticks,vfx:b.vfx,drawCalls:b.drawCalls,triangles:b.triangles,fighters:b.fighters}})())`, ms: 0 }] };
  const p = spawnSync(process.execPath, ['client/Web/scripts/drive.mjs', out, JSON.stringify(script)], { encoding: 'utf8', maxBuffer: 1e7 });
  writeFileSync(`${out}/log.txt`, p.stdout + p.stderr);
  const row = p.stdout.split('\n').find(l => l.startsWith('eval: {'));
  if (p.status || !row || !p.stdout.includes('BROWSER_ERRORS 0') || !p.stdout.includes('>= 120 -> true') || /EXC:|Shader Error|error:/.test(p.stdout)) throw new Error(p.stdout + p.stderr);
  results.push({side, run, ...JSON.parse(row.slice(6))}); console.log(side, run, JSON.stringify(results.at(-1)));
}
writeFileSync(`tmp/web-vfx/perf-${label}.json`, JSON.stringify(results, null, 2));
