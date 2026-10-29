import * as THREE from 'three';

const QUERY = new URLSearchParams(location.search);
const STOCKS = Number(QUERY.get('stocks')) || 3;
const AUTOPLAY = QUERY.has('autoplay');
// ?fw=build loads the debug-enabled build output, about 4x slower per tick than the published one.
// A packaged build sets SLOPA_PATHS in its index.html; the dev page reads the repo directly.
const PATHS = globalThis.SLOPA_PATHS ?? {
  content: '../../../content-cooked/',
  arenas: '../../../data/arenas/',
  framework: QUERY.get('fw') === 'build'
    ? '../sim/bin/Release/net10.0/wwwroot/_framework/dotnet.js' : '../publish/wwwroot/_framework/dotnet.js',
};
const { dotnet } = await import(PATHS.framework);
const TICK_MS = 1000 / 60;

const STAGES = [
  ['rec_center_roof', 'Rec Center Roof', 0x6f9a80],
  ['after_hours', 'After Hours', 0x4a5f92],
  ['picnic_panic', 'Picnic Panic', 0x7fae5c],
  ['industrial_rooftop', 'Industrial Rooftop', 0x948472],
  ['last_stop', 'Last Stop', 0xa0705a],
  ['slop_pit', 'Slop Pit', 0x7c63a6],
  ['splash_deck', 'Splash Deck', 0x4fa9c4],
  ['table_manners', 'Table Manners', 0xb08452],
  ['log_off', 'Log Off', 0x5c78ad],
  ['hamster_habitat', 'Hamster Habitat', 0xd2a861],
  ['training', 'Training', 0x6c7d8e],
  ['slopball', 'Slopball Court', 0x3b5a8a],
];
const SEATS = [0xff4b4b, 0x3f8cff, 0xffc93a, 0x45d36b];
const BODY = {
  fightguy: 0xff8a3d, manki: 0x9a6334, wibou: 0x9d7bff, bonk: 0xb9c2d4,
  bashie: 0x3fcf9f, salamander: 0xff5f3a, nyx: 0x3a3450,
};
const LEVELS = ['Easy', 'Normal', 'Hard'];

const $ = id => document.getElementById(id);
const status = t => { $('status').textContent = t; };

// ── Simulation (Shared C# compiled to WebAssembly) ──
status('starting .NET runtime…');
const t0 = performance.now();
const { getAssemblyExports, getConfig } = await dotnet.create();
const { Demo } = await getAssemblyExports(getConfig().mainAssemblyName);
const PROTOCOL = Demo.ProtocolVersion();
globalThis.Demo = Demo;
const bytes = async url => new Uint8Array(await (await fetch(url)).arrayBuffer());

status('loading fighters…');
const rosterJson = await (await fetch(PATHS.content + 'roster/manifest.json')).text();
Demo.SetRoster(rosterJson);
const packageFiles = await Promise.all(JSON.parse(rosterJson).entries.map(async e => [e.packageId, await Promise.all(
  ['manifest.json', 'character.runtime.json', 'poses.bin', 'client.bindings']
    .map(n => bytes(`${PATHS.content}${e.packageId}/${n}`)))]));
for (const [id, files] of packageFiles) {
  const err = Demo.AddPackage(id, ...files);
  if (err) console.warn(id, err);
}
const catalog = JSON.parse(Demo.BuildCatalog());
if (catalog.error) throw new Error(catalog.error);
const fighterInfo = Object.fromEntries(catalog.fighters.map(f => [f.id, f]));
const bootMs = performance.now() - t0;

// ── Scene. Shared is left-handed (Unity); the world group mirrors Z so stages read correctly. ──
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.setSize(innerWidth, innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
document.body.prepend(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1d1830);
scene.fog = new THREE.Fog(0x1d1830, 40, 90);
const camera = new THREE.PerspectiveCamera(40, innerWidth / innerHeight, 0.1, 300);
addEventListener('resize', () => {
  renderer.setSize(innerWidth, innerHeight);
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
});
scene.add(new THREE.HemisphereLight(0xfff1dc, 0x2a2340, 1.4));
const sun = new THREE.DirectionalLight(0xffffff, 2.2);
sun.position.set(-8, 20, 10);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
Object.assign(sun.shadow.camera, { left: -35, right: 35, top: 35, bottom: -35, near: 1, far: 80 });
sun.shadow.bias = -0.0005;
scene.add(sun, sun.target);
const world = new THREE.Group();
world.scale.z = -1;
scene.add(world);
const toThree = (x, y, z) => new THREE.Vector3(x, y, -z);

const gridTexture = (() => {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d');
  g.fillStyle = '#ffffff';
  g.fillRect(0, 0, 128, 128);
  g.fillStyle = '#e4e4e4';
  g.fillRect(0, 0, 64, 64);
  g.fillRect(64, 64, 64, 64);
  g.strokeStyle = '#bdbdbd';
  g.lineWidth = 3;
  g.strokeRect(0, 0, 128, 128);
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
})();
let stageGroup = null;
let arena = null;
function buildStage(color) {
  const tri = Demo.ArenaTriangles();
  const pos = new Float32Array(tri);
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.computeVertexNormals();
  const n = geo.getAttribute('normal');
  const base = new THREE.Color(color);
  const top = base.clone().lerp(new THREE.Color(0xffffff), 0.18);
  const side = base.clone().multiplyScalar(0.55);
  const under = base.clone().multiplyScalar(0.3);
  const cols = new Float32Array(pos.length);
  for (let i = 0; i < n.count; i++) {
    const ny = Math.abs(n.getY(i)) > 0.7 ? n.getY(i) : 0;
    const c = ny > 0 ? top : ny < 0 ? under : side;
    cols.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(cols, 3));
  const uv = new Float32Array(n.count * 2);
  for (let i = 0; i < n.count; i++) {
    const x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
    const flat = Math.abs(n.getY(i)) > 0.7;
    uv[i * 2] = (flat ? x : x + z) / 2;
    uv[i * 2 + 1] = (flat ? z : y) / 2;
  }
  geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  const mesh = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.85, side: THREE.DoubleSide, map: gridTexture }));
  mesh.receiveShadow = true;
  mesh.castShadow = true;
  const edges = new THREE.LineSegments(new THREE.EdgesGeometry(geo, 25),
    new THREE.LineBasicMaterial({ color: 0x120e1c, transparent: true, opacity: 0.55 }));
  const g = new THREE.Group();
  g.add(mesh, edges);
  return g;
}

// ── Puppets: limbless capsule fighters posed from the cooked hurtbox skeleton (poses.bin). ──
const UP = new THREE.Vector3(0, 1, 0);
const unitSphere = new THREE.SphereGeometry(1, 20, 14);
const unitCylinder = new THREE.CylinderGeometry(1, 1, 1, 16, 1);
function placeSegment(mesh, a, b, r) {
  const d = new THREE.Vector3().subVectors(b, a);
  const len = Math.max(d.length(), 1e-4);
  mesh.position.copy(a).addScaledVector(d, 0.5);
  mesh.quaternion.setFromUnitVectors(UP, d.divideScalar(len));
  mesh.scale.set(r, len, r);
}
function placeBall(mesh, p, r) { mesh.position.copy(p); mesh.scale.setScalar(r); }

class Puppet {
  constructor(pkg, seat) {
    const bones = fighterInfo[pkg].bones;
    this.idx = name => bones.findIndex(b => b.endsWith(name));
    this.b = {
      head: this.idx(':Head'), chest: this.idx(':Spine2'), hips: this.idx(':Hips'),
      rh: this.idx(':RightHand'), lh: this.idx(':LeftHand'), rf: this.idx(':RightFoot'), lf: this.idx(':LeftFoot'),
      rt: this.idx(':RightToeBase'), lt: this.idx(':LeftToeBase'), tip: this.idx('_weapon_tip'), hilt: this.idx('_weapon_hilt'),
    };
    const body = new THREE.MeshStandardMaterial({ color: BODY[pkg] ?? 0xdddddd, roughness: 0.5 });
    const dark = new THREE.MeshStandardMaterial({ color: new THREE.Color(BODY[pkg] ?? 0xdddddd).multiplyScalar(0.55), roughness: 0.6 });
    const white = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.3 });
    const black = new THREE.MeshBasicMaterial({ color: 0x111111 });
    const seatMat = new THREE.MeshBasicMaterial({ color: SEATS[seat] });
    this.mats = [body, dark];
    const m = (geo, mat) => { const x = new THREE.Mesh(geo, mat); x.castShadow = true; this.group.add(x); return x; };
    this.group = new THREE.Group();
    this.head = m(unitSphere, body);
    this.eyes = [m(unitSphere, white), m(unitSphere, white)];
    this.pupils = [m(unitSphere, black), m(unitSphere, black)];
    this.torso = m(unitCylinder, body);
    this.hipBall = m(unitSphere, body);
    this.chestBall = m(unitSphere, body);
    this.hands = [m(unitSphere, dark), m(unitSphere, dark)];
    this.feet = [m(unitCylinder, dark), m(unitCylinder, dark)];
    this.toes = [m(unitSphere, dark), m(unitSphere, dark), m(unitSphere, dark), m(unitSphere, dark)];
    this.blade = this.b.tip >= 0 && this.b.hilt >= 0
      ? m(unitCylinder, new THREE.MeshStandardMaterial({ color: 0xdfe6f0, metalness: 0.8, roughness: 0.25 })) : null;
    this.ring = new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.05, 8, 32), seatMat);
    this.ring.rotation.x = Math.PI / 2;
    this.group.add(this.ring);
    this.shield = new THREE.Mesh(unitSphere, new THREE.MeshStandardMaterial({
      color: SEATS[seat], transparent: true, opacity: 0.28, emissive: SEATS[seat], emissiveIntensity: 0.6 }));
    this.group.add(this.shield);
    this.L = 0;
    this.half = fighterInfo[pkg].half;
    world.add(this.group);
  }
  pt(f, i) {
    if (i < 0 || f.pts[i * 3] == null) return null;
    return new THREE.Vector3(f.pts[i * 3], f.pts[i * 3 + 1], f.pts[i * 3 + 2]);
  }
  update(f, out, now) {
    this.group.visible = !out && f.pts.length > 0;
    if (!this.group.visible) return;
    const P = name => this.pt(f, this.b[name]);
    const head = P('head'), chest = P('chest'), hips = P('hips');
    if (!head || !chest || !hips) return;
    const L = head.distanceTo(hips);
    this.L = this.L ? this.L * 0.95 + L * 0.05 : L;
    const s = this.L;
    const jitter = f.stop > 0 ? (Math.random() - 0.5) * 0.06 : 0;
    this.group.position.set(jitter, 0, 0);
    const neck = new THREE.Vector3().subVectors(head, chest).normalize();
    const headR = s * 0.42;
    const hc = head.clone().addScaledVector(neck, headR * 0.7);
    placeBall(this.head, hc, headR);
    const fwd = new THREE.Vector3(Math.sin(f.yaw), 0, Math.cos(f.yaw));
    const right = new THREE.Vector3(Math.cos(f.yaw), 0, -Math.sin(f.yaw));
    for (let i = 0; i < 2; i++) {
      const side = i === 0 ? -1 : 1;
      const e = hc.clone().addScaledVector(fwd, headR * 0.8).addScaledVector(right, side * headR * 0.38).addScaledVector(UP, headR * 0.15);
      placeBall(this.eyes[i], e, headR * 0.28);
      placeBall(this.pupils[i], e.clone().addScaledVector(fwd, headR * 0.2), headR * 0.13);
    }
    const torsoR = s * 0.3;
    placeSegment(this.torso, hips, chest, torsoR);
    placeBall(this.hipBall, hips, torsoR);
    placeBall(this.chestBall, chest, torsoR * 1.1);
    [['rh', 0], ['lh', 1]].forEach(([n, i]) => { const p = P(n); this.hands[i].visible = !!p; if (p) placeBall(this.hands[i], p, s * 0.17); });
    [['rf', 'rt', 0], ['lf', 'lt', 1]].forEach(([a, b, i]) => {
      const pa = P(a), pb = P(b) ?? pa;
      this.feet[i].visible = !!pa;
      if (!pa) return;
      placeSegment(this.feet[i], pa, pb, s * 0.13);
      placeBall(this.toes[i * 2], pa, s * 0.13);
      placeBall(this.toes[i * 2 + 1], pb, s * 0.13);
    });
    if (this.blade) {
      const tip = P('tip'), hilt = P('hilt');
      this.blade.visible = !!(tip && hilt);
      if (tip && hilt) placeSegment(this.blade, hilt, tip, s * 0.035);
    }
    this.ring.position.set(f.p[0], f.p[1] - this.half + 0.03, f.p[2]);
    const shielding = /Shield/.test(f.state);
    this.shield.visible = shielding;
    if (shielding) placeBall(this.shield, hips.clone().lerp(chest, 0.5), s * 1.3);
    const blink = f.inv > 0 && Math.floor(now / 70) % 2 === 0;
    const hurt = f.state === 'Hitstun' || f.stop > 0;
    for (const mat of this.mats) {
      mat.emissive.setHex(hurt ? 0xffffff : 0x000000);
      mat.emissiveIntensity = hurt ? 0.35 : 0;
      mat.transparent = blink;
      mat.opacity = blink ? 0.35 : 1;
    }
  }
  dispose() { world.remove(this.group); }
}

// ── Hitboxes and projectiles straight from SpellResolver ──
const hitboxPool = [];
const meleeMat = new THREE.MeshBasicMaterial({ color: 0xff3355, transparent: true, opacity: 0.32, depthWrite: false });
const projMat = new THREE.MeshStandardMaterial({ color: 0xffb030, emissive: 0xff8a00, emissiveIntensity: 1.2, transparent: true, opacity: 0.9 });
let showHitboxes = true;
function drawHitboxes(list) {
  let used = 0;
  for (const [x, y, z, ex, ey, ez, r, , projectile, shape] of list) {
    if (!projectile && !showHitboxes) continue;
    let h = hitboxPool[used];
    if (!h) {
      h = { ball: new THREE.Mesh(unitSphere, meleeMat), end: new THREE.Mesh(unitSphere, meleeMat), seg: new THREE.Mesh(unitCylinder, meleeMat) };
      world.add(h.ball, h.end, h.seg);
      hitboxPool.push(h);
    }
    used++;
    const mat = projectile ? projMat : meleeMat;
    const a = new THREE.Vector3(x, y, z), b = new THREE.Vector3(ex, ey, ez);
    const capsule = shape === 1 && a.distanceToSquared(b) > 1e-6;
    h.ball.material = h.end.material = h.seg.material = mat;
    h.ball.visible = true;
    h.end.visible = h.seg.visible = capsule;
    placeBall(h.ball, a, shape === 2 ? r * 0.15 : r);
    if (capsule) { placeBall(h.end, b, r); placeSegment(h.seg, a, b, r); }
  }
  for (let i = used; i < hitboxPool.length; i++) {
    const h = hitboxPool[i];
    h.ball.visible = h.end.visible = h.seg.visible = false;
  }
}

// ── Hit sparks and KO bursts ──
const sparks = [];
const sparkGeo = new THREE.OctahedronGeometry(1, 0);
function burst(x, y, z, color, count, speed, size) {
  for (let i = 0; i < count; i++) {
    const m = new THREE.Mesh(sparkGeo, new THREE.MeshBasicMaterial({ color, transparent: true }));
    m.position.set(x, y, z);
    m.scale.setScalar(size * (0.5 + Math.random()));
    const v = new THREE.Vector3(Math.random() - 0.5, Math.random() - 0.3, Math.random() - 0.5).normalize().multiplyScalar(speed * (0.4 + Math.random()));
    world.add(m);
    sparks.push({ m, v, life: 1 });
  }
}
function tickSparks(dt) {
  for (let i = sparks.length - 1; i >= 0; i--) {
    const s = sparks[i];
    s.life -= dt * 2.8;
    s.v.y -= 9 * dt;
    s.m.position.addScaledVector(s.v, dt);
    s.m.material.opacity = Math.max(s.life, 0);
    s.m.rotation.x += dt * 9;
    if (s.life <= 0) { world.remove(s.m); s.m.material.dispose(); sparks.splice(i, 1); }
  }
}
function popText(x, y, z, text, color) {
  const v = toThree(x, y, z).project(camera);
  const el = document.createElement('div');
  el.className = 'pop';
  el.textContent = text;
  el.style.color = color;
  el.style.left = `${(v.x * 0.5 + 0.5) * innerWidth}px`;
  el.style.top = `${(-v.y * 0.5 + 0.5) * innerHeight}px`;
  document.body.append(el);
  requestAnimationFrame(() => { el.style.transform = 'translateY(-50px) scale(1.2)'; el.style.opacity = '0'; });
  setTimeout(() => el.remove(), 700);
}

// ── Input: keyboard by physical key position (as in Unity), plus the first gamepad ──
const SLOT = { Slot1: 3, Slot3: 8, Slot4: 9, A: 11, E: 4, R: 5, F: 6 };
const KEY_SLOTS = { Digit1: SLOT.Slot1, Digit2: SLOT.Slot3, Digit3: SLOT.Slot4, KeyQ: SLOT.A, KeyE: SLOT.E, KeyR: SLOT.R, KeyF: SLOT.F };
// Unity's face order: south, west, north, east.
const PAD_FACE = [0, 2, 3, 1];
const PAD_NORMAL = [0, SLOT.Slot1, SLOT.Slot3, SLOT.Slot4];
const PAD_SPECIAL = [SLOT.A, SLOT.R, SLOT.E, SLOT.F];
const held = new Set();
const edges = { jump: false, slot: 0, shield: false, grab: false, dash: false, down: false, retarget: false };
let padPrev = [];
addEventListener('keydown', e => {
  if (e.code === 'Tab') e.preventDefault();
  if (e.repeat) return;
  held.add(e.code);
  if (e.code === 'Escape') { leaveMatch(); return; }
  if (e.code === 'KeyH') showHitboxes = !showHitboxes;
  if (e.code === 'Space') edges.jump = true;
  if (KEY_SLOTS[e.code]) edges.slot = KEY_SLOTS[e.code];
  if (e.code === 'ShiftLeft') edges.shield = true;
  if (e.code === 'KeyC') edges.grab = true;
  if (e.code === 'Digit4') edges.dash = true;
  if (e.code === 'KeyX') edges.down = true;
  if (e.code === 'Tab') edges.retarget = true;
});
addEventListener('keyup', e => held.delete(e.code));
addEventListener('blur', () => held.clear());

function readPad() {
  const pad = [...(navigator.getGamepads?.() ?? [])].find(Boolean);
  if (!pad) return null;
  const b = i => !!pad.buttons[i]?.pressed;
  const pressed = i => b(i) && !padPrev[i];
  const lb = b(4);
  PAD_FACE.forEach((btn, face) => {
    if (!pressed(btn)) return;
    const slot = lb ? PAD_SPECIAL[face] : PAD_NORMAL[face];
    if (slot) edges.slot = slot; else edges.jump = true;
  });
  if (pressed(5)) edges.dash = true;
  if (pressed(7)) { if (lb) edges.grab = true; else edges.shield = true; }
  if (pressed(6)) edges.down = true;
  if (pressed(15)) edges.retarget = true;
  padPrev = pad.buttons.map(x => x.pressed);
  const [ax, ay] = pad.axes;
  const mag = Math.hypot(ax, ay);
  return { x: mag > 0.25 ? ax : 0, y: mag > 0.25 ? -ay : 0, jump: b(0) && !lb, shield: b(7) && !lb, down: b(6) };
}

const inputBuf = new Uint8Array(22);
const inputView = new DataView(inputBuf.buffer);
function buildInput(self) {
  const pad = readPad();
  let mx = (held.has('KeyD') ? 1 : 0) - (held.has('KeyA') ? 1 : 0);
  let my = (held.has('KeyW') ? 1 : 0) - (held.has('KeyS') ? 1 : 0);
  if (pad && (pad.x || pad.y)) { mx = pad.x; my = pad.y; }
  const m = Math.hypot(mx, my);
  if (m > 0) { mx /= m; my /= m; }
  const wrapDeg = r => { let d = (r * 180 / Math.PI) % 360; if (d > 180) d -= 360; if (d < -180) d += 360; return d; };
  const yaw = Math.round(wrapDeg(self?.yaw ?? 0) * 100);
  const down = held.has('KeyX') || !!pad?.down;
  const flags = (my > 0.3 ? 1 : 0) | (down ? 2 : 0) | (mx < -0.3 ? 4 : 0) | (mx > 0.3 ? 8 : 0)
    | (edges.jump ? 0x10 : 0) | (edges.dash ? 0x20 : 0);
  const flags2 = ((held.has('Space') || pad?.jump) ? 1 : 0) | (edges.down ? 8 : 0)
    | ((held.has('ShiftLeft') || pad?.shield) ? 0x10 : 0) | (edges.shield ? 0x20 : 0)
    | (edges.grab ? 0x40 : 0) | (edges.retarget ? 0x80 : 0);
  inputView.setFloat32(0, mx, true);
  inputView.setFloat32(4, my, true);
  inputBuf[8] = flags;
  inputBuf[9] = edges.slot;
  inputView.setInt16(10, yaw, true);
  inputView.setInt16(12, yaw, true);
  inputView.setInt16(14, 0, true);
  inputView.setUint16(16, 0, true);
  inputBuf[18] = 0;
  inputBuf[19] = flags2;
  inputBuf[20] = 1; // TargetLockMode.Always, the shipped default
  inputBuf[21] = PROTOCOL;
  for (const k in edges) edges[k] = k === 'slot' ? 0 : false;
  return inputBuf;
}

// ── Modes. Each owns its menu rows, stage, HUD, per-tick reactions and camera focus. ──
const hex = c => `#${c.toString(16).padStart(6, '0')}`;
const alive = f => f.deaths < STOCKS;
const MODES = {
  brawl: {
    label: 'Brawl',
    rows: ['secCpu', 'secStage', 'secLevel'],
    stage: () => pick.stage,
    packages: () => [pick.you, ...pick.cpus],
    ready: () => pick.cpus.length > 0,
    begin(frame) {
      $('hud').innerHTML = frame.fighters.map((f, i) => `
        <div class="card" style="--seat:${hex(SEATS[i])}">
          <div class="who">${i === 0 ? 'P1' : 'CPU'} · ${fighterInfo[f.pkg].name}</div>
          <div class="pct" id="pct${i}">0%</div>
          <div class="stocks" id="stk${i}">${'<i></i>'.repeat(STOCKS)}</div>
        </div>`).join('');
      banner('GO!', '', 900);
    },
    onFrame(frame) {
      for (const [id, x, y, z] of frame.deaths) {
        const seat = frame.fighters.findIndex(f => f.id === id);
        const b = arena.bounds;
        burst(Math.min(Math.max(x, b[0]), b[1]), Math.max(y, arena.killHeight + 4), Math.min(Math.max(z, b[2]), b[3]), SEATS[seat], 40, 16, 0.14);
        banner('KO!', fighterInfo[frame.fighters[seat].pkg].name, 900);
      }
      frame.fighters.forEach((f, i) => {
        const pct = `${Math.floor(f.dmg)}%`;
        const el = $(`pct${i}`);
        if (el.textContent !== pct) el.textContent = pct;
        el.style.color = `hsl(${Math.max(0, 50 - f.dmg / 3)}, 100%, ${Math.max(60, 95 - f.dmg / 4)}%)`;
        [...$(`stk${i}`).children].forEach((dot, k) => dot.classList.toggle('lost', k >= STOCKS - f.deaths));
      });
      if (frame.ended) {
        const left = frame.fighters.filter(alive);
        finishMatch('GAME!', left.length === 1 ? `${fighterInfo[left[0].pkg].name} wins` : 'draw');
      }
    },
    focus: frame => frame.fighters.filter(f => alive(f) && f.p[1] > arena.killHeight + 3).map(f => f.p),
  },

  slopball: {
    label: 'Slopball',
    rows: [],
    stage: () => 'slopball',
    packages: () => [pick.you],
    ready: () => true,
    begin(frame, court) {
      this.court = court;
      this.props = new THREE.Group();
      const width = court.landing[0] * 2 + 3;
      const net = new THREE.Mesh(new THREE.BoxGeometry(width, court.netTop - court.floorY, Math.max(court.netHalf * 2, 0.08)),
        new THREE.MeshStandardMaterial({ color: 0xf4efe2, transparent: true, opacity: 0.55, roughness: 0.9 }));
      net.position.set(0, (court.floorY + court.netTop) / 2, court.netZ);
      const tape = new THREE.Mesh(new THREE.BoxGeometry(width, 0.08, 0.1), new THREE.MeshStandardMaterial({ color: 0xffcf3a }));
      tape.position.set(0, court.netTop, court.netZ);
      for (const x of [-width / 2, width / 2]) {
        const post = new THREE.Mesh(unitCylinder, new THREE.MeshStandardMaterial({ color: 0x2b2438 }));
        placeSegment(post, new THREE.Vector3(x, court.floorY, court.netZ), new THREE.Vector3(x, court.netTop + 0.1, court.netZ), 0.07);
        this.props.add(post);
      }
      this.launcher = new THREE.Group();
      const body = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.9, 1, 20), new THREE.MeshStandardMaterial({ color: 0x3a3150, roughness: 0.6 }));
      body.position.y = 0.5;
      const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.32, 0.4, 1.3, 16), new THREE.MeshStandardMaterial({ color: 0xff6b3d, roughness: 0.4 }));
      barrel.position.set(0, 1.2, -0.35);
      barrel.rotation.x = -0.9;
      this.launcher.add(body, barrel);
      this.ball = new THREE.Mesh(unitSphere, new THREE.MeshStandardMaterial({ color: 0xffd23a, emissive: 0xff7a00, emissiveIntensity: 0.4, roughness: 0.35 }));
      this.ball.castShadow = true;
      this.marker = new THREE.Mesh(new THREE.RingGeometry(0.7, 0.85, 32), new THREE.MeshBasicMaterial({ color: 0xffd23a, transparent: true, opacity: 0.8, side: THREE.DoubleSide }));
      this.marker.rotation.x = -Math.PI / 2;
      this.drop = new THREE.Mesh(unitCylinder, new THREE.MeshBasicMaterial({ color: 0xffd23a, transparent: true, opacity: 0.25 }));
      this.props.add(net, tape, this.launcher, this.ball, this.marker, this.drop);
      world.add(this.props);
      $('hud').classList.add('top');
      $('hud').innerHTML = `
        <div class="card score" style="--seat:${hex(SEATS[0])}">
          <div class="who">${fighterInfo[frame.fighters[0].pkg].name} · Slopball</div>
          <div class="pct" id="returns">0</div>
          <div class="sub"><span id="best">best 0</span> · <span id="heat"></span></div>
        </div>`;
      this.lastBeat = -1;
    },
    onFrame(frame) {
      const s = frame.mode;
      if (s.phase === 'Countdown') {
        const beat = Math.ceil(s.countdown / 60);
        if (beat !== this.lastBeat) { this.lastBeat = beat; banner(String(beat), 'return the serves over the net', 1100); }
      } else if (this.lastBeat !== 0) { this.lastBeat = 0; banner('SLOP IT OUT', '', 900); }
      for (const [kind, returns] of s.events) {
        const [x, y, z] = s.ball.p;
        if (kind === 'Returned') { burst(x, y, z, 0xffd23a, 18, 9, 0.08); popText(x, y + 0.8, z, `${returns}`, '#ffd23a'); }
        if (kind === 'Missed' && returns > 0) banner('DROPPED', `${returns} in a row`, 1200);
      }
      $('returns').textContent = s.returns;
      $('best').textContent = `best ${s.best}`;
      $('heat').textContent = '🔥'.repeat(s.heat) || 'cool';
    },
    draw(frame) {
      const s = frame.mode, b = s.ball, c = this.court;
      this.launcher.position.set(s.launcher[0], c.floorY, s.launcher[2]);
      for (const m of [this.ball, this.marker, this.drop]) m.visible = b.active;
      if (!b.active) return;
      placeBall(this.ball, new THREE.Vector3(...b.p), b.r);
      this.ball.material.emissiveIntensity = 0.3 + s.heat * 0.3;
      this.marker.position.set(b.p[0], c.floorY + 0.02, b.p[2]);
      this.marker.scale.setScalar(Math.max(0.4, b.r * 1.6));
      placeSegment(this.drop, new THREE.Vector3(b.p[0], c.floorY, b.p[2]), new THREE.Vector3(...b.p), 0.03);
    },
    focus(frame) {
      const pts = [frame.fighters[0].p, [0, this.court.floorY, this.court.netZ]];
      if (frame.mode.ball.active) pts.push(frame.mode.ball.p);
      return pts;
    },
    dispose() { world.remove(this.props); },
  },
};

// ── Match lifecycle ──
let match = null;
const stageCache = new Map();
async function startMatch() {
  const mode = MODES[pick.mode];
  const stageId = mode.stage();
  const [, , color] = STAGES.find(s => s[0] === stageId);
  arena = JSON.parse(Demo.LoadArena(await bytes(`${PATHS.arenas}${stageId}.arena`)));
  const pkgs = mode.packages();
  const started = JSON.parse(Demo.Start(pick.mode, pkgs.join(','), STOCKS, pick.level, (Math.random() * 1e9) | 0));
  if (started.error) { status(started.error); return; }
  if (stageGroup) world.remove(stageGroup);
  if (!stageCache.has(stageId)) stageCache.set(stageId, buildStage(color));
  stageGroup = stageCache.get(stageId);
  world.add(stageGroup);
  const frame = JSON.parse(Demo.Step(new Uint8Array(0)));
  match = { mode, frame, puppets: pkgs.map((p, i) => new Puppet(p, i)), ended: false, stepMs: 0, steps: 0 };
  camTarget = null;
  $('menu').classList.add('hidden');
  mode.begin(frame, started.court);
}
function finishMatch(title, sub) {
  match.ended = true;
  banner(title, sub, 2600);
  setTimeout(leaveMatch, 2800);
}
function leaveMatch() {
  if (!match) return;
  match.puppets.forEach(p => p.dispose());
  match.mode.dispose?.();
  match = null;
  $('hud').innerHTML = '';
  $('hud').classList.remove('top');
  $('menu').classList.remove('hidden');
}
let bannerTimer = 0;
function banner(text, sub, ms) {
  $('bannerText').textContent = text;
  $('bannerSub').textContent = sub;
  $('banner').classList.remove('hidden');
  clearTimeout(bannerTimer);
  bannerTimer = setTimeout(() => $('banner').classList.add('hidden'), ms);
}

function step() {
  const self = match.frame.fighters[0];
  const t = performance.now();
  const frame = JSON.parse(Demo.Step(AUTOPLAY ? new Uint8Array(0) : buildInput(self)));
  match.stepMs += performance.now() - t;
  match.steps++;
  match.frame = frame;
  for (const [target, , dmg, blocked, x, y, z] of frame.hits) {
    const seat = frame.fighters.findIndex(f => f.id === target);
    burst(x, y, z, blocked ? 0x8fd8ff : 0xfff2a0, blocked ? 6 : 10 + dmg, 7, 0.06);
    if (!blocked && seat >= 0) popText(x, y + 0.6, z, `${Math.round(dmg)}`, hex(SEATS[seat]));
  }
  match.mode.onFrame(frame);
}

// ── Camera: frames the mode's focus points from behind the nearest one ──
let camTarget = null;
const camPos = new THREE.Vector3(0, 10, 20);
function updateCamera(dt) {
  const pts = match.mode.focus(match.frame).map(p => toThree(p[0], p[1] + 1, p[2]));
  if (!pts.length) return;
  const box = new THREE.Box3().setFromPoints(pts);
  const center = box.getCenter(new THREE.Vector3());
  const size = box.getSize(new THREE.Vector3());
  // Stand back from the nearest point so depth spread never pushes anyone under the HUD.
  const d = Math.min(7 + size.x * 0.75 + size.z * 0.25, 30);
  const want = new THREE.Vector3(center.x, center.y + 2.5 + d * 0.45, box.max.z + d * 0.8);
  const k = camTarget ? 1 - Math.exp(-dt * 4) : 1;
  // Aim low and toward the camera so the nearest fighter clears the HUD cards.
  const aim = new THREE.Vector3(center.x, center.y - 1.2, center.z + size.z * 0.3);
  camTarget = camTarget ? camTarget.lerp(aim, k) : aim;
  camPos.lerp(want, k);
  camera.position.copy(camPos);
  camera.lookAt(camTarget);
}

// ── Frame loop: fixed 60 Hz simulation, render every display frame ──
let acc = 0, last = performance.now(), fpsFrames = 0, fpsT = last, fps = 0;
function loop(now) {
  requestAnimationFrame(loop);
  const dt = Math.min(now - last, 100);
  last = now;
  if (match && !match.ended) {
    acc += dt;
    while (acc >= TICK_MS) { step(); acc -= TICK_MS; }
  } else acc = 0;
  if (match) {
    match.frame.fighters.forEach((f, i) => match.puppets[i].update(f, !alive(f), now));
    drawHitboxes(match.frame.hitboxes);
    match.mode.draw?.(match.frame);
    updateCamera(dt / 1000);
  } else {
    const a = now / 6000;
    camera.position.set(Math.sin(a) * 26, 14, Math.cos(a) * 26);
    camera.lookAt(0, 0, 0);
  }
  tickSparks(dt / 1000);
  renderer.render(scene, camera);
  fpsFrames++;
  if (now - fpsT > 500) {
    fps = Math.round(fpsFrames * 1000 / (now - fpsT));
    fpsFrames = 0; fpsT = now;
    const sim = match?.steps ? `sim ${(match.stepMs / match.steps).toFixed(2)} ms/tick · ` : '';
    status(`${sim}${fps} fps · boot ${bootMs.toFixed(0)} ms · H toggles hitboxes`);
  }
}

// ── Menu ──
const pick = { mode: 'brawl', you: 'fightguy', cpus: ['manki'], stage: 'rec_center_roof', level: 1 };
function renderMenu() {
  const btn = (label, on, fn) => { const b = document.createElement('button'); b.className = 'pick' + (on ? ' on' : ''); b.textContent = label; b.onclick = () => { fn(); renderMenu(); }; return b; };
  $('pickMode').replaceChildren(...Object.entries(MODES).map(([id, m]) => btn(m.label, pick.mode === id, () => { pick.mode = id; })));
  $('pickYou').replaceChildren(...catalog.fighters.map(f => btn(f.name, pick.you === f.id, () => { pick.you = f.id; })));
  $('pickCpu').replaceChildren(...catalog.fighters.map(f => btn(f.name, pick.cpus.includes(f.id), () => {
    pick.cpus = pick.cpus.includes(f.id) ? pick.cpus.filter(x => x !== f.id) : [...pick.cpus, f.id].slice(-3);
  })));
  $('pickStage').replaceChildren(...STAGES.filter(s => s[0] !== 'slopball').map(([id, name]) => btn(name, pick.stage === id, () => { pick.stage = id; })));
  $('pickLevel').replaceChildren(...LEVELS.map((name, i) => btn(name, pick.level === i, () => { pick.level = i; })));
  for (const id of ['secCpu', 'secStage', 'secLevel']) $(id).classList.toggle('hidden', !MODES[pick.mode].rows.includes(id));
}
$('go').onclick = () => { if (MODES[pick.mode].ready()) startMatch().catch(e => status(String(e))); };
addEventListener('keydown', e => { if (e.code === 'Enter' && !match) $('go').click(); });
renderMenu();
$('menu').classList.remove('hidden');
status(`ready · boot ${bootMs.toFixed(0)} ms`);
requestAnimationFrame(loop);
