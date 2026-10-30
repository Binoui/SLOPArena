import { readdir, readFile } from 'node:fs/promises';
const exported = JSON.parse(await readFile(new URL('../app/vfx/effects.json', import.meta.url), 'utf8'));

const cooked = new URL('../../../content-cooked/', import.meta.url);
const failures = [], seen = new Set();
let packages = 0;
for (const name of (await readdir(cooked)).sort()) {
  let data;
  try { data = JSON.parse(await readFile(new URL(`${name}/client.bindings`, cooked), 'utf8')); }
  catch (e) { if (e.code === 'ENOENT') continue; throw e; }
  packages++;
  const rows = data.presentations ?? [];
  console.log(`${name}: ${rows.length} presentation ids`);
  for (const { semanticId: id } of rows) {
    seen.add(id);
    const valid = exported.presentations[id]?.nodes.length > 0;
    console.log(`  ${valid ? 'OK' : 'MISSING/INVALID'} ${id}`);
    if (!valid) failures.push(id);
  }
}
for (const id of Object.keys(exported.presentations)) if (!seen.has(id)) failures.push(`Stale export ${id}`);
const references = JSON.parse(await readFile(new URL('../app/vfx/unity-references.json', import.meta.url), 'utf8'));
for (const id of seen) if (!references.presentations[id]?.prefab) failures.push(`No Unity reference ${id}`);
for (const id of Object.keys(references.projectiles)) if (!exported.projectiles[id]) failures.push(`Missing projectile ${id}`);
console.log(`\n${seen.size - failures.filter(id => seen.has(id)).length}/${seen.size} presentations across ${packages} packages; ${Object.keys(exported.projectiles).length} projectile exports; ${failures.length} failures.`);
if (!seen.size || failures.length) { console.error(failures.join('\n')); process.exitCode = 1; }

let systems = 0;
for (const group of ['presentations', 'projectiles']) for (const id of Object.keys(references[group])) {
  const effect = exported[group][id];
  if (!effect || effect.prefab !== references[group][id].prefab) throw new Error(`Missing exported ${id}`);
  effect.nodes.forEach((n, index) => {
    if (n.parent >= index || n.parent < -1) throw new Error(`Invalid hierarchy ${id} ${n.name}`);
    if (n.system) {
      systems++;
      if (!n.system.main.startLifetime || !n.system.renderer) throw new Error(`Incomplete system ${id} ${n.name}`);
      if (n.system.material && !exported.materials[n.system.material]) throw new Error(`Missing material ${id}`);
    }
  });
}
const report = JSON.parse(await readFile(new URL('../app/vfx/export-coverage.json', import.meta.url), 'utf8'));
if (report.systems !== systems) throw new Error('Export coverage system count differs');
console.log(`${systems} referenced particle systems exported; ${report.uniqueSystems} unique; ${report.textures.length} textures; all reference ids covered.`);
