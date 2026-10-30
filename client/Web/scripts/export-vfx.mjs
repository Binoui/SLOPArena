import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const run = spawnSync('python3', [fileURLToPath(new URL('./export-vfx.py', import.meta.url))], { stdio: 'inherit' });
if (run.error) console.error(run.error.message);
if (run.signal) console.error(`VFX exporter terminated by ${run.signal}`);
process.exitCode = run.status ?? 1;
