// Browser checks + real four-fighter Hard Brawl or Slopball captures, on VFX's reserved Chrome port.
// Usage: node client/Web/scripts/vfx-verify.mjs [brawl|slopball|kit] [output-directory]
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const mode = process.argv[2] ?? 'brawl';
const out = process.argv[3] ?? `/tmp/vfx-${mode}-verified`;
if (!['brawl', 'slopball', 'kit'].includes(mode)) throw new Error('Expected brawl, slopball or kit');
async function browserChecks() {
  const THREE = await import('three');
  const { createVfx, PRESENTATION_PRESETS, PROJECTILE_PRESETS } = await import('./vfx.js');
  const world = new THREE.Group(); world.scale.z = -1;
  const camera = new THREE.PerspectiveCamera(45, 16 / 9, 0.1, 1000);
  camera.position.set(10, 15, 20); camera.lookAt(0, 0, 0); camera.updateMatrixWorld();
  const fx = createVfx({ world, camera }), bone = new THREE.Vector3(1, 2, 3);
  const fighters = ['fightguy', 'manki', 'wibou', 'bonk', 'bashie', 'nyx'].map((pkg, i) => ({ id: i + 1, pkg, yaw: 0 }));
  const ctx = { seatOf: id => id - 1, seatColor: () => 0x45d36b, boneWorld: () => bone, arena: { bounds: [-10, 10, -10, 10], killHeight: -20 } };
  let tick = 0;
  const frame = extra => ({ tick: ++tick, fighters, hits: [], deaths: [], events: [], hitboxes: [], ...extra });
  const assert = (ok, message) => { if (!ok) throw new Error(`VFX CHECK FAILED: ${message}`); };
  const reset = () => { fx.onFrame(frame({ tick: -1 }), ctx); fx.update(0); };
  const event = (id, extra = {}) => ({ id, entity: 1, p: [0, 1, 0], yaw: 0, bone: null, local: [0, 0, 0], scale: [1, 1, 1], ticks: 30, ...extra });
  const melee = { a: [0, 1, 0], b: [1, 1, 0], r: 0.3, shape: 1, v: [0, 0, 0] };
  fx.drawHitboxes([melee]);
  assert(world.children.every(m => m.isInstancedMesh), 'melee debug hidden by default');
  fx.toggleHitboxes(); fx.drawHitboxes([melee]);
  assert(world.children.some(m => !m.isInstancedMesh && m.visible), 'H toggle shows melee debug');
  fx.toggleHitboxes(); fx.drawHitboxes([]);
  const matrices = mesh => {
    const m = new THREE.Matrix4(), p = new THREE.Vector3();
    return Array.from({ length: mesh.count }, (_, i) => { mesh.getMatrixAt(i, m); return p.setFromMatrixPosition(m).toArray(); });
  };
  const rings = () => world.children.find(m => m.isInstancedMesh && m.geometry.type === 'RingGeometry');
  fx.onFrame(frame({ events: [event('presentation.fightguy.ki-shot.charge', { bone: 'bone.right-hand', local: [0.25, 0.5, 0] })] }), ctx);
  fx.update(1 / 60);
  const initial = matrices(rings())[0]; bone.x += 5; fx.update(1 / 60);
  assert(Math.abs(matrices(rings())[0][0] - initial[0] - 5) < 0.001, 'bone effect follows semantic bone');
  assert(Math.abs(matrices(rings())[0][1] - 2.5) < 0.001, 'bone local offset applied');
  for (let n = 0; n < 14; n++) fx.update(1 / 60);
  assert(rings().count === 1, 'attached charge survives the short prefab particle lifetime');
  reset();
  fx.onFrame(frame({ events: [event('presentation.bonk.slam.landing', { p: [3, 2, 5], local: [2, 0, 0], yaw: Math.PI / 2 })] }), ctx); fx.update(1 / 60);
  const wp = matrices(rings())[0];
  assert(Math.abs(wp[0] - 3) < 0.001 && Math.abs(wp[2] - 3) < 0.001, 'world event local offset rotates with yaw');
  bone.set(100, 100, 100); fx.update(1 / 60);
  assert(Math.abs(matrices(rings())[0][0] - 3) < 0.001, 'world effect stays at event position');
  reset();
  for (const id of Object.keys(PRESENTATION_PRESETS)) {
    fx.onFrame(frame({ events: [event(id)] }), ctx); fx.update(1 / 60);
    for (const m of world.children.filter(m => m.isInstancedMesh)) {
      assert(Array.from(m.instanceMatrix.array.subarray(0, m.count * 16)).every(Number.isFinite), `${id} finite transforms`);
    }
  }
  const projectileRows = Object.keys(PROJECTILE_PRESETS).map((key, i) => {
    const [pkg, air, slot] = key.split('.');
    return { a: [i, 1, 0], b: [i, 1, 0], v: [0, 0, 8], r: 0.35, owner: fighters.find(f => f.pkg === pkg).id,
      slot: slot === 'R' ? 5 : 11, air: air === 'air', emit: i, age: 1, shape: 0 };
  });
  fx.onFrame(frame({ hitboxes: projectileRows }), ctx); fx.update(1 / 60);
  assert(fx.stats.projectiles === 14 && Object.keys(fx.stats.projectileKinds).length === 7, '14 projectile variants, seven distinct visual kinds');
  fx.onFrame(frame({ hitboxes: projectileRows.map(h => ({ ...h, v: [0, 0, 0] })) }), ctx);
  assert(fx.stats.projectiles === 14, 'projectiles survive a stationary apex or landing');
  const childCount = world.children.length;
  const materialIds = () => { const ids = new Set(); world.traverse(o => { if (o.material) ids.add(o.material.uuid); }); return [...ids].sort().join(); };
  const beforeMaterials = materialIds();
  for (let n = 0; n < 30; n++) {
    fx.onFrame(frame(), ctx); fx.onFrame(frame({ hitboxes: projectileRows }), ctx); fx.update(1 / 60);
  }
  assert(world.children.length === childCount, 'projectile meshes reused');
  assert(materialIds() === beforeMaterials, 'materials reused across particles and projectiles');
  reset();
  const cameraBefore = camera.matrixWorld.toArray().join();
  fx.onFrame(frame({ hits: [[1, 2, 3, true, 0, 1, 0, 2, 3]] }), ctx); fx.update(1 / 60);
  assert(fx.stats.blocked === 1, 'shield contact handled');
  const lightCount = fx.stats.particles;
  reset(); fx.onFrame(frame({ hits: [[1, 2, 24, false, 0, 1, 0, 12, 9]] }), ctx); fx.update(1 / 60);
  assert(fx.stats.particles > lightCount, 'heavy hit scales with damage and hitstop');
  assert(camera.matrixWorld.toArray().join() === cameraBefore, 'effects never shake the camera');
  for (let n = 0; n < 40; n++) fx.burst(0, 1, 0, 0xffcc22, 36, 8, 0.1);
  fx.update(1 / 60); assert(fx.stats.particles <= 640, 'particle pool bounded');
  reset();
  fx.onFrame(frame({ deaths: [[1, 100, -20, 100]] }), ctx); fx.update(1 / 60);
  assert(fx.stats.knockouts === 1, 'offscreen KO handled');
  for (const m of world.children.filter(m => m.isInstancedMesh)) {
    assert(Array.from(m.instanceMatrix.array.subarray(0, m.count * 16)).every(Number.isFinite), 'KO finite transforms');
  }
  reset();
  assert(fx.stats.particles === 0 && fx.stats.projectiles === 0 && fx.stats.emitters === 0, 'match restart clears effects');
  assert(fx.stats.unknown.length === 0, 'no unhandled presentation ids');
  console.log('VFX_CHECKS_PASS', JSON.stringify(fx.stats));

  const step = Demo.Step;
  window.__vfxCapture = { tick: 0, hits: 0, blocked: 0, deaths: 0, events: 0, ids: {}, projectileSlots: {}, modeEvents: {} };
  Demo.Step = (...args) => {
    const result = step(...args), f = JSON.parse(result), c = window.__vfxCapture;
    c.tick = f.tick; c.hits += f.hits.length; c.blocked += f.hits.filter(h => h[3]).length; c.deaths += f.deaths.length;
    c.events += f.events.length;
    for (const e of f.events) c.ids[e.id] = (c.ids[e.id] ?? 0) + 1;
    for (const h of f.hitboxes) if (h.v.some(v => v !== 0)) {
      const pkg = f.fighters.find(f => f.id === h.owner)?.pkg ?? 'hazard';
      c.projectileSlots[`${pkg}.${h.slot}.${h.air}`] = (c.projectileSlots[`${pkg}.${h.slot}.${h.air}`] ?? 0) + 1;
    }
    for (const [kind] of f.mode?.events ?? []) c.modeEvents[kind] = (c.modeEvents[kind] ?? 0) + 1;
    if (c.tick % 240 === 0) console.log('VFX_LIVE', JSON.stringify(c));
    return result;
  };
}
async function gallery() {
  const THREE = await import('three');
  const { createVfx } = await import('./vfx.js');
  for (const child of document.body.children) child.style.display = 'none';
  addEventListener('keydown', e => e.stopImmediatePropagation(), true);
  const renderer = new THREE.WebGLRenderer({ antialias: true }); renderer.setSize(innerWidth, innerHeight);
  renderer.domElement.style.cssText = 'position:fixed;inset:0;z-index:1'; document.body.append(renderer.domElement);
  const scene = new THREE.Scene(); scene.background = new THREE.Color(0x201b30);
  const camera = new THREE.PerspectiveCamera(40, innerWidth / innerHeight, 0.1, 300);
  camera.position.set(0, 12, 18); camera.lookAt(0, 0, 0); camera.updateMatrixWorld();
  scene.add(new THREE.HemisphereLight(0xfff3d8, 0x302840, 2));
  const light = new THREE.DirectionalLight(0xffffff, 3); light.position.set(-4, 12, 8); scene.add(light);
  const world = new THREE.Group(); world.scale.z = -1; scene.add(world);
  const floor = new THREE.Mesh(new THREE.BoxGeometry(21, 0.25, 10), new THREE.MeshStandardMaterial({ color: 0x343047, roughness: 1 }));
  floor.position.y = -0.2; world.add(floor);
  const fx = createVfx({ world, camera });
  const fighters = ['fightguy', 'manki', 'wibou', 'bonk', 'bashie', 'nyx'].map((pkg, i) => ({ id: i + 1, pkg, yaw: 0 }));
  const ctx = { seatOf: id => id - 1, seatColor: () => 0x45d36b, arena: { bounds: [-10, 10, -5, 5], killHeight: -20 }, boneWorld: () => null };
  const projectiles = [['fightguy', 11], ['manki', 11], ['manki', 5], ['wibou', 11], ['bonk', 11], ['bashie', 11], ['nyx', 11]];
  const hitboxes = projectiles.map(([pkg, slot], i) => ({ owner: fighters.find(f => f.pkg === pkg).id, slot, air: false,
    emit: i, age: 1, shape: 0, a: [-7.5 + i * 2.5, 1, -2.8], b: [0, 0, 0], r: slot === 5 ? 0.5 : 0.43, v: [1, 0, 1] }));
  const effects = ['presentation.wibou.flash.cut', 'presentation.manki.bazooka.explosion', 'presentation.fightguy.ki-shot.charge', 'presentation.nyx.grasp-drag'];
  const events = effects.map((id, i) => ({ id, entity: 1, p: [-6 + i * 4, 0.8, 2.6], yaw: Math.PI / 2, bone: null, local: [0, 0, 0], scale: [1, 1, 1], ticks: 20 }));
  fx.onFrame({ tick: 1, fighters, hitboxes, events, hits: [[1, 2, 22, false, 2, 1, 2.6, 12, 9], [2, 1, 5, true, 6, 1, 2.6, 3, 3]], deaths: [[1, 12, 6, 0]] }, ctx);
  fx.update(0.04); fx.update(0.04); renderer.render(scene, camera);
  function label(text, x, y, z, fontSize = 13) {
    const p = new THREE.Vector3(x, y, -z).project(camera), el = document.createElement('div');
    el.textContent = text;
    el.style.cssText = `position:fixed;z-index:3;left:${(p.x * .5 + .5) * innerWidth}px;top:${(-p.y * .5 + .5) * innerHeight}px;transform:translate(-50%,-50%);font:800 ${fontSize}px system-ui;color:#eee6d4;letter-spacing:.06em;white-space:nowrap;pointer-events:none`;
    document.body.append(el);
  }
  ['KI SHOT', 'ROUND BOMB', 'ROCKET', 'KUNAI', 'POT LID', 'WATER RING', 'HEX CROW'].forEach((s, i) => label(s, -7.5 + i * 2.5, 0, -2.8));
  ['BLADE CUT', 'EXPLOSION', 'KI CHARGE / HEAVY HIT', 'TETHER / BLOCK'].forEach((s, i) => label(s, -6 + i * 4, 0, 2.6));
  const title = document.createElement('div'); title.textContent = 'SLOPARENA  /  EFFECT KIT';
  title.style.cssText = 'position:fixed;z-index:3;top:24px;left:32px;color:#ffcc22;font:900 24px system-ui;letter-spacing:.1em'; document.body.append(title);
  console.log('VFX_GALLERY_PASS', JSON.stringify({ ...fx.stats, calls: renderer.info.render.calls, triangles: renderer.info.render.triangles }));
}
const select = mode === 'brawl'
  ? 'for(const n of ["Wibou","Sir Bonk"]) [...document.querySelectorAll("#pickCpu .pick")].find(b=>b.textContent===n).click();[...document.querySelectorAll("#pickLevel .pick")].find(b=>b.textContent==="Hard").click();'
  : mode === 'slopball' ? '[...document.querySelectorAll("#pickMode .pick")].find(b=>b.textContent.includes("Slopball")).click();' : '';
const script = {
  port: 9341, query: mode === 'brawl' ? '?autoplay' : '',
  setup: `(async () => { await (${browserChecks.toString()})();${mode === 'kit' ? `await (${gallery.toString()})();` : select} })()`,
  steps: mode === 'brawl'
    ? [{ ms: 3500, shot: 'brawl' }, { until: 'window.__vfxCapture.hits >= 20', timeout: 30000, shot: 'brawl-contact' }, { ms: 3000, shot: 'brawl-late' }]
    : mode === 'slopball' ? [{ ms: 5000, shot: 'slopball-serve' }, { down: ['Digit1'], ms: 1800, up: ['Digit1'], shot: 'slopball-swing' }, { ms: 5000, shot: 'slopball-late' }]
      : [{ ms: 100, shot: 'effect-kit' }],
};
const child = spawn(process.execPath, [fileURLToPath(new URL('./drive.mjs', import.meta.url)), out, JSON.stringify(script)], { stdio: ['ignore', 'pipe', 'inherit'] });
let output = '';
child.stdout.on('data', data => { output += data; process.stdout.write(data); });
const code = await new Promise(resolve => child.on('exit', resolve));
if (code || !output.includes('VFX_CHECKS_PASS') || output.includes('Shader Error')
  || (mode === 'kit' ? !output.includes('VFX_GALLERY_PASS') : !output.includes('VFX_LIVE') || output.includes('EXC:'))) process.exitCode = 1;
