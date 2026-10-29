import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
const repo = new URL('../../../', import.meta.url).pathname;
const { dotnet } = await import(join(repo, 'client/Web/sim/bin/Release/net10.0/wwwroot/_framework/dotnet.js'));
const { getAssemblyExports, getConfig } = await dotnet.create();
const { Demo } = await getAssemblyExports(getConfig().mainAssemblyName);
for (const f of readdirSync(join(repo, 'data/arenas')).filter(f => f.endsWith('.arena')))
  console.log(f, Demo.LoadArena(new Uint8Array(readFileSync(join(repo, 'data/arenas', f)))), Demo.ArenaTriangles().length / 9);
