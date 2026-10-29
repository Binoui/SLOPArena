// Read Unity assets as text; no Editor or YAML dependency is needed.
import { readdir, readFile, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join, relative } from 'node:path';

const root = fileURLToPath(new URL('../../../', import.meta.url));
async function files(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  return (await Promise.all(entries.map(e => e.isDirectory() ? files(join(dir, e.name)) : join(dir, e.name)))).flat();
}
const index = new Map();
await Promise.all((await files(join(root, 'client/Unity/Assets'))).filter(p => p.endsWith('.meta')).map(async p => {
  const guid = (await readFile(p, 'utf8')).match(/^guid: (\w+)/m)?.[1];
  if (guid) index.set(guid, p.slice(0, -5));
}));
const scalar = (s, key) => Number(s.match(new RegExp(`^    ${key}:\\n[\\s\\S]*?^      scalar: ([^\\n]+)`, 'm'))?.[1]) || null;
const color = s => s?.match(/\{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}/)?.slice(1).map(Number) ?? null;
async function inspect(guid) {
  const path = index.get(guid);
  if (!path) throw new Error(`Unresolved Unity GUID ${guid}`);
  const yaml = await readFile(path, 'utf8');
  const materials = [];
  for (const m of yaml.matchAll(/m_Materials:\n((?:  -[^\n]*\n)+)/g)) {
    for (const g of m[1].matchAll(/guid: (\w+)/g)) {
      const p = index.get(g[1]);
      if (!p || materials.some(r => r.path === relative(root, p))) continue;
      const mat = await readFile(p, 'utf8');
      materials.push({ path: relative(root, p), color: color(mat.match(/_BaseColor: ([^\n]+)/)?.[1]), emission: color(mat.match(/_EmissionColor: ([^\n]+)/)?.[1]) });
    }
  }
  return {
    prefab: relative(root, path),
    particles: yaml.split(/^--- /m).filter(s => s.includes('\nParticleSystem:\n')).map(s => ({
      color: color(s.match(/startColor:\n[\s\S]*?maxColor: ([^\n]+)/)?.[1]),
      size: scalar(s, 'startSize'), lifetime: scalar(s, 'startLifetime'),
      shape: Number(s.match(/ShapeModule:\n[\s\S]*?^    type: (\d+)/m)?.[1]) || 0,
      trails: /TrailModule:\n\s+serializedVersion: \d+\n\s+enabled: 1/.test(s),
    })),
    lineRenderers: (yaml.match(/\nLineRenderer:\n/g) ?? []).length,
    trailRenderers: (yaml.match(/\nTrailRenderer:\n/g) ?? []).length,
    materials,
  };
}
const presentations = {}, projectiles = {};
for (const pkg of (await readdir(join(root, 'content-cooked'))).sort()) {
  let bindings;
  try { bindings = JSON.parse(await readFile(join(root, 'content-cooked', pkg, 'client.bindings'), 'utf8')); } catch { continue; }
  for (const row of bindings.presentations ?? []) presentations[row.semanticId] = await inspect(row.prefabGlobalObjectId.split('-')[2]);
  let catalog;
  try { catalog = await readFile(join(root, 'client/Unity/Assets/CharacterPackages', pkg, 'CharacterAssetCatalog.asset'), 'utf8'); } catch { continue; }
  for (const m of catalog.matchAll(/_slotId: ((?:ground|air)\.[\w]+)\n\s+_prefab: \{[^\n]*guid: (\w+)[^\n]*\}\n\s+_scale: ([^\n]+)\n\s+_orientation: (\d+)/g)) {
    projectiles[`${pkg}.${m[1]}`] = { ...await inspect(m[2]), scale: Number(m[3]), orientation: Number(m[4]) };
  }
}
const out = join(root, 'client/Web/app/vfx/unity-references.json');
if (process.argv.includes('--write')) {
  await mkdir(join(root, 'client/Web/app/vfx'), { recursive: true });
  await writeFile(out, JSON.stringify({ presentations, projectiles }, null, 2) + '\n');
}
console.log(`${Object.keys(presentations).length} presentation references, ${Object.keys(projectiles).length} projectile references${process.argv.includes('--write') ? ` → ${out}` : ' (pass --write to save)'}`);
