// Builds dist/: a self-contained static site (page, trimmed .NET runtime, cooked fighters, listed stages).
// Run scripts/build.sh first. Serve dist/ from any static host.
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
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
cpSync(join(repo, 'content-cooked/roster/manifest.json'), join(dist, 'content/roster/manifest.json'), { recursive: true });
for (const { packageId } of roster.entries)
  for (const f of ['manifest.json', 'character.runtime.json', 'poses.bin', 'client.bindings'])
    cpSync(join(repo, 'content-cooked', packageId, f), join(dist, 'content', packageId, f), { recursive: true });

const main = readFileSync(join(web, 'app/main.js'), 'utf8');
const stagesBlock = main.slice(main.indexOf('const STAGES = ['), main.indexOf('];', main.indexOf('const STAGES = [')));
const stages = [...stagesBlock.matchAll(/\['([a-z0-9_]+)'/g)].map(m => m[1]);
for (const id of stages) cpSync(join(repo, 'data/arenas', `${id}.arena`), join(dist, 'arenas', `${id}.arena`), { recursive: true });

cpSync(join(web, 'app/main.js'), join(dist, 'main.js'));
const paths = `<script>globalThis.SLOPA_PATHS = { content: './content/', arenas: './arenas/', framework: './_framework/dotnet.js' };</script>`;
const html = readFileSync(join(web, 'app/index.html'), 'utf8');
if (!html.includes('<script type="module"')) throw new Error('index.html has no module script to precede');
writeFileSync(join(dist, 'index.html'), html.replace('<script type="module"', `${paths}\n<script type="module"`));

const size = dir => readdirSync(dir).reduce((n, f) => { const p = join(dir, f); const s = statSync(p); return n + (s.isDirectory() ? size(p) : s.size); }, 0);
const mb = b => (b / 1048576).toFixed(1) + ' MB';
console.log(`dist: ${roster.entries.length} fighters, ${stages.length} stages, framework ${mb(size(join(dist, '_framework')))}, content ${mb(size(join(dist, 'content')) + size(join(dist, 'arenas')))}`);
