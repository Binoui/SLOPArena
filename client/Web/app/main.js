import * as THREE from 'three';
import { toThree, unitSphere, unitCylinder, placeSegment, placeBall } from './geometry.js';
import { Puppet, flashVictims } from './fighters.js';
import { ModelFighter, loadFighterModel } from './fighter-model.js';
import { createVfx } from './vfx.js';
import { floorSampler } from './vfx/floor.js';
import { loadStageArt, hasStageArt } from './stage-art.js';
import { createAudio } from './audio.js';
import { createCamera } from './camera.js';

const QUERY = new URLSearchParams(location.search);
const STOCKS = Number(QUERY.get('stocks')) || 3;
const AUTOPLAY = QUERY.has('autoplay');
// ?fw=build loads the debug-enabled build output, about 4x slower per tick than the published one.
// A packaged build sets SLOPA_PATHS in its index.html; the dev page reads the repo directly.
const PATHS = globalThis.SLOPA_PATHS ?? {
  content: '../../../content-cooked/',
  arenas: '../../../data/arenas/',
  assets: '../assets/',
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
// URP shades without three.js's 1/pi diffuse factor; these intensities match the game's fighter brightness.
const hemi = new THREE.HemisphereLight(0xfff1dc, 0x2a2340, 3);
scene.add(hemi);
const sun = new THREE.DirectionalLight(0xffffff, 4.5);
sun.position.set(-8, 20, 10);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
Object.assign(sun.shadow.camera, { left: -35, right: 35, top: 35, bottom: -35, near: 1, far: 80 });
sun.shadow.bias = -0.0005;
scene.add(sun, sun.target);
const world = new THREE.Group();
world.scale.z = -1;
scene.add(world);

// A stage's exported Unity lighting (stage-art.js environment) sets the sky, fog and key-light direction. Fighters
// keep the calibrated rig above, only tinted toward the stage: Unity's fighters stay bright on night stages, and
// lighting them with the raw night values (After Hours' key light is 0.3) turned them into silhouettes.
const linear = rgb => new THREE.Color(rgb[0], rgb[1], rgb[2]).convertSRGBToLinear();
const STAGE_TINT = 0.25;
function applyEnvironment(env) {
  const air = env?.atmosphere;
  scene.background = air ? linear(air.background) : new THREE.Color(0x1d1830);
  scene.fog = air?.fog ? new THREE.Fog(linear(air.fogColor), air.fogStart, air.fogEnd) : new THREE.Fog(0x1d1830, 40, 90);
  hemi.color.set(0xfff1dc);
  hemi.groundColor.set(0x2a2340);
  if (air) {
    hemi.color.lerp(linear(air.ambientSky), STAGE_TINT);
    hemi.groundColor.lerp(linear(air.ambientGround), STAGE_TINT);
  }
  const key = env?.lights?.find(l => l.type === 'directional');
  sun.color.set(0xffffff);
  sun.position.set(-8, 20, 10);
  if (key) {
    // Same reading as stage-art.js: a sun's azimuth/elevation, or a Unity rotation's x (elevation) and y (azimuth).
    const elevation = THREE.MathUtils.degToRad(key.sun?.elevation ?? key.rotation?.[0] ?? 55);
    const azimuth = THREE.MathUtils.degToRad(key.sun?.azimuth ?? key.rotation?.[1] ?? 30);
    sun.position.copy(toThree(-Math.sin(azimuth) * Math.cos(elevation), Math.sin(elevation), -Math.cos(azimuth) * Math.cos(elevation))).multiplyScalar(40);
    sun.color.lerp(linear(key.color), STAGE_TINT);
  }
  camera.far = Math.max(300, env?.cameraFar ?? 0);
  camera.updateProjectionMatrix();
}
const vfx = createVfx({ world, camera });
const cam = createCamera(camera);
const audio = createAudio({ paths: PATHS });

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

// ── Input: keyboard by physical key position (as in Unity), plus the first gamepad ──
const SLOT = { Slot1: 3, Slot3: 8, Slot4: 9, A: 11, E: 4, R: 5, F: 6 };
const KEY_SLOTS = { Digit1: SLOT.Slot1, Digit2: SLOT.Slot3, Digit3: SLOT.Slot4, KeyQ: SLOT.A, KeyE: SLOT.E, KeyR: SLOT.R, KeyF: SLOT.F };
// Unity's face order: south, west, north, east.
const PAD_FACE = [0, 2, 3, 1];
const PAD_NORMAL = [0, SLOT.Slot1, SLOT.Slot3, SLOT.Slot4];
// ADR-0042 right-stick specials: a flick past 60% presses the special laid out like the face diamond
// (up E, left R, right F, down Q); another one needs the stick back under 35%.
const STICK_PRESS = 0.6, STICK_RELEASE = 0.35;
let stickArmed = true;
const held = new Set();
const edges = { jump: false, slot: 0, shield: false, grab: false, dash: false, down: false, retarget: false };
let padPrev = [];
addEventListener('keydown', e => {
  if (e.code === 'Tab') e.preventDefault();
  if (e.repeat) return;
  held.add(e.code);
  if (e.code === 'Escape') { leaveMatch(); return; }
  if (e.code === 'KeyH') vfx.toggleHitboxes();
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
  PAD_FACE.forEach((btn, face) => {
    if (!pressed(btn)) return;
    if (PAD_NORMAL[face]) edges.slot = PAD_NORMAL[face]; else edges.jump = true;
  });
  const [ax, ay, rx = 0, ry = 0] = pad.axes;
  const rmag = Math.hypot(rx, ry);
  if (rmag < STICK_RELEASE) stickArmed = true;
  else if (stickArmed && rmag >= STICK_PRESS) {
    stickArmed = false;
    edges.slot = Math.abs(rx) > Math.abs(ry) ? (rx > 0 ? SLOT.F : SLOT.R) : (ry < 0 ? SLOT.E : SLOT.A);
  }
  if (pressed(4)) edges.grab = true;
  if (pressed(5)) edges.dash = true;
  if (pressed(7)) edges.shield = true;
  if (pressed(6)) edges.down = true;
  if (pressed(15)) edges.retarget = true;
  padPrev = pad.buttons.map(x => x.pressed);
  const mag = Math.hypot(ax, ay);
  return { x: mag > 0.25 ? ax : 0, y: mag > 0.25 ? -ay : 0, jump: b(0), shield: b(7), down: b(6) };
}

// Unity keeps the camera basis a held direction started with, so a camera turn does not bend a run underway;
// a new direction (more than 40 degrees away) re-reads the camera.
let heldBasis = null, heldAngle = 0;
function cameraRelative(mx, my) {
  if (mx === 0 && my === 0) { heldBasis = null; return [0, 0]; }
  const angle = Math.atan2(mx, my) * 180 / Math.PI;
  if (heldBasis === null || Math.abs(((angle - heldAngle + 540) % 360) - 180) > 40) { heldBasis = cam.yaw; heldAngle = angle; }
  const y = heldBasis * Math.PI / 180;
  return [my * Math.sin(y) + mx * Math.cos(y), my * Math.cos(y) - mx * Math.sin(y)];
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
  [mx, my] = cameraRelative(mx, my);
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
    camera: 'steady',
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
      for (const [id] of frame.deaths) {
        const seat = frame.fighters.findIndex(f => f.id === id);
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
    camera: 'frame',
    rows: [],
    stage: () => 'slopball',
    packages: () => [pick.you],
    ready: () => true,
    begin(frame, court, { stageArt }) {
      this.court = court;
      this.props = new THREE.Group();
      // The exported Night Court carries its own net; the prototype one stands in on the grey box.
      const ownNet = !!stageArt?.getObjectByName('Court Net');
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
      if (ownNet) this.props.children.slice().forEach(post => this.props.remove(post));
      else this.props.add(net, tape);
      this.props.add(this.launcher, this.ball, this.marker, this.drop);
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
        if (kind === 'Returned') { vfx.burst(x, y, z, 0xffd23a, 18, 9, 0.08); vfx.popText(x, y + 0.8, z, `${returns}`, '#ffd23a'); }
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

// ── Fighter views and the context effects and audio read each tick ──
// ?puppets keeps the capsule puppets even when a fighter has an exported model.
const models = new Map();
async function loadModels(pkgs) {
  if (QUERY.has('puppets')) return;
  await Promise.all(pkgs.map(async pkg => { models.set(pkg, await loadFighterModel(pkg, PATHS.assets)); }));
}
function createFighterView(pkg, seat) {
  const gltf = models.get(pkg);
  const info = fighterInfo[pkg];
  return gltf
    ? new ModelFighter({ world, info, gltf, seatColor: SEATS[seat] })
    : new Puppet({ world, info, seatColor: SEATS[seat], bodyColor: BODY[pkg] ?? 0xdddddd });
}
function frameContext(frame) {
  const seatOf = id => frame.fighters.findIndex(f => f.id === id);
  return {
    seatOf,
    infoOf: pkg => fighterInfo[pkg],
    viewOf: id => match?.views[seatOf(id)],
    seatColor: seat => SEATS[seat] ?? 0xffffff,
    arena,
    floorAt: arena?.floorAt,
    fighterOf: id => frame.fighters.find(f => f.id === id),
    boneFrame: (id, boneId) => match?.views[seatOf(id)]?.boneFrame?.(boneId) ?? null,
    boneWorld: (id, boneId) => match?.views[seatOf(id)]?.boneWorld(boneId) ?? null,
  };
}

// ── Match lifecycle ──
let match = null;
const stageCache = new Map();
async function startMatch() {
  const mode = MODES[pick.mode];
  const stageId = mode.stage();
  const [, , color] = STAGES.find(s => s[0] === stageId);
  arena = JSON.parse(Demo.LoadArena(await bytes(`${PATHS.arenas}${stageId}.arena`)));
  arena.floorAt = floorSampler(Demo.ArenaTriangles());
  const pkgs = mode.packages();
  await loadModels(pkgs);
  const started = JSON.parse(Demo.Start(pick.mode, pkgs.join(','), STOCKS, pick.level, (Math.random() * 1e9) | 0));
  if (started.error) { status(started.error); return; }
  if (stageGroup) world.remove(stageGroup);
  if (!stageCache.has(stageId)) {
    const group = new THREE.Group();
    const greybox = buildStage(color);
    const art = await loadStageArt(stageId, { paths: PATHS });
    if (art) { greybox.visible = false; group.add(art); group.userData.art = art; }
    group.add(greybox);
    stageCache.set(stageId, group);
  }
  stageGroup = stageCache.get(stageId);
  world.add(stageGroup);
  applyEnvironment(stageGroup.userData.art?.userData.environment);
  const frame = JSON.parse(Demo.Step(new Uint8Array(0)));
  match = { mode, modeId: pick.mode, frame, views: pkgs.map((p, i) => createFighterView(p, i)), ended: false, stepMs: 0, steps: 0 };
  audio.onMatchStart(pick.mode);
  cam.reset();
  $('menu').classList.add('hidden');
  mode.begin(frame, started.court, { stageArt: stageGroup.userData.art ?? null });
}
function finishMatch(title, sub) {
  match.ended = true;
  banner(title, sub, 2600);
  setTimeout(leaveMatch, 2800);
}
function leaveMatch() {
  if (!match) return;
  match.views.forEach(v => v.dispose());
  match.mode.dispose?.();
  audio.onMatchEnd();
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
  flashVictims(frame, id => match.views[frame.fighters.findIndex(f => f.id === id)], t);
  const ctx = frameContext(frame);
  vfx.onFrame(frame, ctx);
  audio.onFrame(frame, ctx);
  match.mode.onFrame(frame);
}

// ── Camera: Steady follows P1 toward its target; framing modes keep their focus points in view ──
function updateCamera(dt) {
  const frame = match.frame;
  if (match.mode.camera !== 'steady') return cam.frame(match.mode.focus(frame), dt);
  const self = frame.fighters[0];
  const target = frame.fighters.find(f => f.id === self.target && alive(f))
    ?? frame.fighters.filter(f => f !== self && alive(f) && f.p[1] > arena.killHeight + 3)
      .sort((a, b) => Math.hypot(a.p[0] - self.p[0], a.p[2] - self.p[2]) - Math.hypot(b.p[0] - self.p[0], b.p[2] - self.p[2]))[0];
  // Unity's FramingSubject.Launched also counts post-hitstun flight, which the frame does not carry.
  const subjects = frame.fighters.filter(f => f !== self && alive(f))
    .map(f => ({ id: f.id, p: f.p, launched: !f.grounded && f.state === 'Hitstun' }));
  cam.steady(self, target?.p ?? null, dt, { modelYOffset: match.views[0].info.modelYOffset, subjects });
}

// ── Frame loop: fixed 60 Hz simulation, render every display frame ──
let acc = 0, last = performance.now(), fpsFrames = 0, fpsT = last, fps = 0;
function loop(now) {
  const frameStart = performance.now();
  requestAnimationFrame(loop);
  pollMenuPad();
  const dt = Math.min(now - last, 100);
  last = now;
  if (match && !match.ended) {
    acc += dt;
    while (acc >= TICK_MS) { step(); acc -= TICK_MS; }
  } else acc = 0;
  if (match) {
    match.frame.fighters.forEach((f, i) => match.views[i].update(f, !alive(f), now));
    vfx.drawHitboxes(match.frame.hitboxes);
    match.mode.draw?.(match.frame);
    updateCamera(dt / 1000);
  } else {
    const a = now / 6000;
    camera.position.set(Math.sin(a) * 26, 14, Math.cos(a) * 26);
    camera.lookAt(0, 0, 0);
  }
  vfx.update(dt / 1000);
  renderer.render(scene, camera);
  if (QUERY.has('vfxbench') && match && !match.ended) {
    const b = globalThis.__vfxBench ??= { cpu: [], raf: [], ticks: 0, last: now };
    if (b.cpu.length < 1200) { b.cpu.push(performance.now() - frameStart); b.raf.push(now - b.last); }
    b.ticks = match.frame.tick; b.last = now; b.vfx = vfx.stats; b.drawCalls = renderer.info.render.calls; b.triangles = renderer.info.render.triangles; b.fighters = match.frame.fighters.map(f => f.pkg);
  }
  fpsFrames++;
  if (now - fpsT > 500) {
    fps = Math.round(fpsFrames * 1000 / (now - fpsT));
    fpsFrames = 0; fpsT = now;
    const sim = match?.steps ? `sim ${(match.stepMs / match.steps).toFixed(2)} ms/tick · ` : '';
    status(`${sim}${fps} fps · boot ${bootMs.toFixed(0)} ms · H toggles hitboxes`);
  }
}

// ── Menu ──
// The public menu lists finished stages only; ?allstages adds the grey boxes for development.
const MENU_STAGES = STAGES.filter(([id]) => id !== 'slopball' && (hasStageArt(id) || QUERY.has('allstages')));
const pick = { mode: 'brawl', you: 'fightguy', cpus: ['manki'], stage: 'after_hours', level: 1 };
function renderMenu() {
  const btn = (label, on, fn) => { const b = document.createElement('button'); b.className = 'pick' + (on ? ' on' : ''); b.textContent = label; b.onclick = () => { fn(); renderMenu(); }; return b; };
  $('pickMode').replaceChildren(...Object.entries(MODES).map(([id, m]) => btn(m.label, pick.mode === id, () => { pick.mode = id; })));
  $('pickYou').replaceChildren(...catalog.fighters.map(f => btn(f.name, pick.you === f.id, () => { pick.you = f.id; })));
  $('pickCpu').replaceChildren(...catalog.fighters.map(f => btn(f.name, pick.cpus.includes(f.id), () => {
    pick.cpus = pick.cpus.includes(f.id) ? pick.cpus.filter(x => x !== f.id) : [...pick.cpus, f.id].slice(-3);
  })));
  $('pickStage').replaceChildren(...MENU_STAGES.map(([id, name]) => btn(name, pick.stage === id, () => { pick.stage = id; })));
  $('pickLevel').replaceChildren(...LEVELS.map((name, i) => btn(name, pick.level === i, () => { pick.level = i; })));
  for (const id of ['secCpu', 'secStage', 'secLevel']) $(id).classList.toggle('hidden', !MODES[pick.mode].rows.includes(id));
}
$('go').onclick = () => { if (MODES[pick.mode].ready()) startMatch().catch(e => { console.error(e); status(String(e)); }); };
addEventListener('keydown', e => { if (e.code === 'Enter' && !match && !startScreen) $('go').click(); });

// ── Gamepad outside play: the start screen, menu navigation (D-pad or stick, A selects, B steps back,
// Start fights) and Start to leave a match. Browsers only count clicks and keys as the gesture that unlocks
// sound, so the start screen asks for one and a pad still dismisses it (silently) if that's all there is.
let startScreen = document.createElement('div');
startScreen.id = 'start';
startScreen.innerHTML = '<div class="press">Click or press any key</div><div class="sub">Gamepad ready: press A</div>';
document.body.append(startScreen);
function dismissStart(e) {
  if (!startScreen) return;
  e?.stopImmediatePropagation?.();
  startScreen.remove();
  startScreen = null;
  focusMenu(document.querySelector('#pickMode .pick.on') ?? $('go'));
}
addEventListener('keydown', dismissStart, { capture: true });
addEventListener('pointerdown', dismissStart, { capture: true });

let menuPadPrev = [], menuFocus = null, repeatAt = 0;
const focusables = () => [...document.querySelectorAll('#menu button, #menu a, #menu summary')].filter(el => el.offsetParent);
function focusMenu(el) {
  menuFocus?.classList.remove('pad-focus');
  menuFocus = el;
  el?.classList.add('pad-focus');
  el?.scrollIntoView({ block: 'nearest' });
}
function moveFocus(dx, dy) {
  const all = focusables();
  if (!menuFocus || !all.includes(menuFocus)) return focusMenu(all[0]);
  const from = menuFocus.getBoundingClientRect();
  const cx = from.x + from.width / 2, cy = from.y + from.height / 2;
  let best = null, bestScore = Infinity;
  for (const el of all) {
    if (el === menuFocus) continue;
    const r = el.getBoundingClientRect();
    const ex = r.x + r.width / 2 - cx, ey = r.y + r.height / 2 - cy;
    const along = ex * dx + ey * dy;
    if (along <= 4) continue;
    const score = along + 3 * Math.abs(ex * dy - ey * dx);
    if (score < bestScore) { bestScore = score; best = el; }
  }
  if (best) focusMenu(best);
}
function pollMenuPad() {
  const pad = [...(navigator.getGamepads?.() ?? [])].find(Boolean);
  if (!pad) return;
  const b = i => !!pad.buttons[i]?.pressed;
  const pressed = i => b(i) && !menuPadPrev[i];
  const any = pad.buttons.some((_, i) => pressed(i));
  if (match) {
    if (pressed(9)) leaveMatch();
  } else if (startScreen) {
    if (any) dismissStart();
  } else {
    const [ax, ay] = pad.axes;
    const dx = (b(15) ? 1 : 0) - (b(14) ? 1 : 0) || (Math.abs(ax) > 0.5 ? Math.sign(ax) : 0);
    const dy = (b(13) ? 1 : 0) - (b(12) ? 1 : 0) || (Math.abs(ay) > 0.5 ? Math.sign(ay) : 0);
    const now = performance.now();
    if ((dx || dy) && now >= repeatAt) {
      moveFocus(dx, dx ? 0 : dy);
      repeatAt = now + (repeatAt === 0 ? 350 : 140);
    } else if (!dx && !dy) repeatAt = 0;
    if (pressed(0) && menuFocus) {
      const keep = menuFocus.id || menuFocus.textContent;
      menuFocus.click();
      // renderMenu rebuilds the buttons; keep the focus on the same choice.
      focusMenu(focusables().find(el => (el.id || el.textContent) === keep) ?? focusables()[0]);
    }
    if (pressed(9)) $('go').click();
  }
  menuPadPrev = pad.buttons.map(x => x.pressed);
}
renderMenu();
$('menu').classList.remove('hidden');
audio.onMenu();
status(`ready · boot ${bootMs.toFixed(0)} ms`);
if (QUERY.has('vfxreview')) {
  const { createReview } = await import('./vfx/review.js');
  globalThis.__review = await createReview({ world, scene, renderer, camera, vfx, Demo, PATHS, fighterInfo,
    loadModels, createFighterView, applyEnvironment, frameContext });
} else requestAnimationFrame(loop);
