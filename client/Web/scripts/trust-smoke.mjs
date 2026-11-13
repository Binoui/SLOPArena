// Run after build.sh. The JavaScript roster is input, never the demo's trust authority.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
const repo = new URL('../../../', import.meta.url).pathname;
const { dotnet } = await import(join(repo, 'client/Web/publish/wwwroot/_framework/dotnet.js'));
const { getAssemblyExports, getConfig } = await dotnet.create();
const { Demo } = await getAssemblyExports(getConfig().mainAssemblyName);
const roster = JSON.parse(readFileSync(join(repo, 'content-cooked/roster/manifest.json'), 'utf8'));
const read = name => readFileSync(join(repo, 'content-cooked/fightguy', name));
const manifest = JSON.parse(read('manifest.json'));
// Preserve the internal capabilities, change only provenance metadata, then recompute a valid package hash.
manifest.creator = 'External';
manifest.packageHash = '';
const chunks = [];
for (const name of ['character.runtime.json', 'poses.bin', 'client.bindings']) {
  const path = Buffer.from(name), bytes = read(name);
  const pathSize = Buffer.alloc(4), size = Buffer.alloc(8);
  pathSize.writeUInt32LE(path.length); size.writeBigUInt64LE(BigInt(bytes.length));
  chunks.push(pathSize, path, size, bytes);
}
manifest.packageHash = createHash('sha256').update(Buffer.concat([...chunks, Buffer.from(JSON.stringify(manifest))])).digest('hex');
const add = () => Demo.AddPackage('fightguy', new Uint8Array(Buffer.from(JSON.stringify(manifest))),
  new Uint8Array(read('character.runtime.json')), new Uint8Array(read('poses.bin')), new Uint8Array(read('client.bindings')));
Demo.SetRoster(JSON.stringify(roster));
assert.match(add(), /capability\.untrusted/, 'stale-pin fallback must use Workshop');
const forged = structuredClone(roster);
const entry = forged.entries.find(e => e.packageId === 'fightguy');
entry.requirement.packageHash = manifest.packageHash;
Demo.SetRoster(JSON.stringify(forged));
assert.match(add(), /capability\.untrusted/, 'a caller-supplied pin cannot confer trust');
console.log('Workshop policy holds for stale-pin fallback and a forged JavaScript roster');
