// Builds dist/: a self-contained static site (page, trimmed .NET runtime, cooked fighters, listed stages).
// Run scripts/build.sh first. Serve dist/ from any static host.
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
const web = new URL('..', import.meta.url).pathname;
const repo = join(web, '../..');
const dist = join(web, 'dist');
rmSync(dist, { recursive: true, force: true });
mkdirSync(dist, { recursive: true });

const framework = join(web, 'publish/wwwroot/_framework');
mkdirSync(join(dist, '_framework'));
for (const f of readdirSync(framework).filter(f => !f.endsWith('.gz')))
  cpSync(join(framework, f), join(dist, '_framework', f));

const roster = JSON.parse(readFileSync(join(repo, 'content-cooked/roster/manifest.json'), 'utf8'));
for (const { packageId, requirement } of roster.entries) {
  const manifest = JSON.parse(readFileSync(join(repo, 'content-cooked', packageId, 'manifest.json'), 'utf8'));
  if (manifest.cookedContentHash !== requirement.cookedContentHash || manifest.packageHash !== requirement.packageHash)
    throw new Error(`${packageId}: cooked package does not match its roster pin; refresh the roster before publishing`);
}
cpSync(join(repo, 'content-cooked/roster/manifest.json'), join(dist, 'content/roster/manifest.json'), { recursive: true });
for (const { packageId } of roster.entries)
  for (const f of ['manifest.json', 'character.runtime.json', 'poses.bin', 'client.bindings'])
    cpSync(join(repo, 'content-cooked', packageId, f), join(dist, 'content', packageId, f), { recursive: true });

const main = readFileSync(join(web, 'app/main.js'), 'utf8');
const stagesBlock = main.slice(main.indexOf('const STAGES = ['), main.indexOf('];', main.indexOf('const STAGES = [')));
const stages = [...stagesBlock.matchAll(/\['([a-z0-9_]+)'/g)].map(m => m[1]);
for (const id of stages) cpSync(join(repo, 'data/arenas', `${id}.arena`), join(dist, 'arenas', `${id}.arena`), { recursive: true });

// The page's modules and data, minus the look-dev viewer.
cpSync(join(web, 'app'), dist, { recursive: true, filter: src => !/viewer\.(js|html)$/.test(src) && !src.endsWith('index.html') });
if (existsSync(join(web, 'assets'))) cpSync(join(web, 'assets'), join(dist, 'assets'), { recursive: true });
// three.js and the addons the page imports are vendored, so the published site depends on no CDN.
const html0 = readFileSync(join(web, 'app/index.html'), 'utf8');
const importMap = JSON.parse(html0.match(/<script type="importmap">([\s\S]*?)<\/script>/)[1]).imports;
const sources = [join(web, 'app'), join(web, 'app/vfx')].filter(existsSync)
  .flatMap(dir => readdirSync(dir).filter(f => f.endsWith('.js')).map(f => readFileSync(join(dir, f), 'utf8')));
const addons = new Set(sources.flatMap(src => [...src.matchAll(/from\s*['"]three\/addons\/([^'"]+)['"]/g)].map(m => m[1])));
const vendored = new Set();
async function vendor(url, out) {
  if (vendored.has(out)) return;
  vendored.add(out);
  const res = await fetch(url);
  if (!res.ok) throw new Error(`vendor ${url}: ${res.status}`);
  const text = await res.text();
  mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, text);
  for (const [, rel] of text.matchAll(/(?:from|import)\s*['"](\.{1,2}\/[^'"]+)['"]/g))
    await vendor(new URL(rel, url).href, join(dirname(out), rel));
}
await vendor(importMap.three, join(dist, 'vendor/three/build/three.module.js'));
for (const addon of addons) await vendor(importMap['three/addons/'] + addon, join(dist, 'vendor/three/examples/jsm', addon));
const localMap = JSON.stringify({ imports: { three: './vendor/three/build/three.module.js', 'three/addons/': './vendor/three/examples/jsm/' } });

const paths = `<script>globalThis.SLOPA_PATHS = { content: './content/', arenas: './arenas/', assets: './assets/', framework: './_framework/dotnet.js' };</script>`;
const html = html0.replace(/<script type="importmap">[\s\S]*?<\/script>/, `<script type="importmap">${localMap}</script>`);
if (!html.includes('<script type="module"')) throw new Error('index.html has no module script to precede');
writeFileSync(join(dist, 'index.html'), html.replace('<script type="module"', `${paths}\n<script type="module"`));

const size = dir => readdirSync(dir).reduce((n, f) => { const p = join(dir, f); const s = statSync(p); return n + (s.isDirectory() ? size(p) : s.size); }, 0);
const mb = b => (b / 1048576).toFixed(1) + ' MB';
console.log(`dist: ${roster.entries.length} fighters, ${stages.length} stages, framework ${mb(size(join(dist, '_framework')))}, content ${mb(size(join(dist, 'content')) + size(join(dist, 'arenas')))}`);
