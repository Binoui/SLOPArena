import { readdir, readFile } from 'node:fs/promises';
import { PRESENTATION_PRESETS, PROJECTILE_PRESETS, PRIMITIVES } from '../app/vfx/presets.js';

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
    const preset = Object.hasOwn(PRESENTATION_PRESETS, id) && PRESENTATION_PRESETS[id];
    const valid = preset && Number.isInteger(preset.color) && preset.size > 0 && preset.duration > 0
      && preset.kit.length && preset.kit.every(p => PRIMITIVES.includes(p));
    console.log(`  ${valid ? 'OK' : 'MISSING/INVALID'} ${id}`);
    if (!valid) failures.push(id);
  }
}
for (const id of Object.keys(PRESENTATION_PRESETS)) if (!seen.has(id)) failures.push(`Stale preset ${id}`);
const references = JSON.parse(await readFile(new URL('../app/vfx/unity-references.json', import.meta.url), 'utf8'));
for (const id of seen) if (!references.presentations[id]?.prefab) failures.push(`No Unity reference ${id}`);
for (const id of Object.keys(references.projectiles)) if (!PROJECTILE_PRESETS[id]) failures.push(`Missing projectile ${id}`);
console.log(`\n${seen.size - failures.filter(id => seen.has(id)).length}/${seen.size} presentations across ${packages} packages; ${Object.keys(PROJECTILE_PRESETS).length} projectile presets; ${failures.length} failures.`);
if (!seen.size || failures.length) { console.error(failures.join('\n')); process.exitCode = 1; }
