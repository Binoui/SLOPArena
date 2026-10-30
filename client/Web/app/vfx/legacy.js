import * as THREE from 'three';
import { createShuriken, EFFECTS, prepareEffects } from './shuriken.js';

await prepareEffects();
import { toThree, unitSphere, unitCylinder, placeSegment, placeBall } from '../geometry.js';
import { PRESENTATION_PRESETS, PROJECTILE_PRESETS } from './presets.js';

export { PRESENTATION_PRESETS, PROJECTILE_PRESETS } from './presets.js';

const TAU = Math.PI * 2;
const SLOT = { 3: '1', 8: '3', 9: '4', 11: 'A', 4: 'E', 5: 'R', 6: 'F' };
const INK = 0x262235, PAPER = 0xded8c9, YELLOW = 0xffcc22, ORANGE = 0xf05b35;
const rand = (a, b) => a + Math.random() * (b - a);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

function starGeometry(points, inner) {
  const shape = new THREE.Shape();
  for (let i = 0; i < points * 2; i++) {
    const a = i / (points * 2) * TAU, r = i % 2 ? inner : 1;
    if (i === 0) shape.moveTo(Math.cos(a) * r, Math.sin(a) * r);
    else shape.lineTo(Math.cos(a) * r, Math.sin(a) * r);
  }
  shape.closePath();
  return new THREE.ShapeGeometry(shape);
}

function slashGeometry() {
  const vertices = [], indices = [];
  for (let i = 0; i <= 28; i++) {
    const t = i / 28, a = t * Math.PI * 1.45 - 0.5;
    const width = Math.sin(t * Math.PI) * 0.25;
    for (const r of [1, 1 - width]) vertices.push(Math.cos(a) * r, Math.sin(a) * r, 0);
    if (i < 28) { const n = i * 2; indices.push(n, n + 1, n + 2, n + 1, n + 3, n + 2); }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
  g.setIndex(indices);
  return g;
}

// One shared shader, eight instanced draws, and a bounded reusable particle pool.
function particleKit(world) {
  const capacity = 640;
  const material = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.DoubleSide,
    vertexShader: `attribute float effectAlpha; attribute float effectOverlay;
      varying vec3 tint; varying float alpha;
      void main() { tint = instanceColor; alpha = effectAlpha;
        gl_Position = projectionMatrix * modelViewMatrix * instanceMatrix * vec4(position, 1.0);
        gl_Position.z = mix(gl_Position.z, -0.99 * gl_Position.w, effectOverlay); }`,
    fragmentShader: `varying vec3 tint; varying float alpha;
      void main() { gl_FragColor = vec4(tint, alpha);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  });
  const geometries = {
    spark: new THREE.OctahedronGeometry(1), ring: new THREE.RingGeometry(0.87, 1, 40),
    slash: slashGeometry(), glint: starGeometry(4, 0.18), star: starGeometry(11, 0.53),
    smoke: new THREE.IcosahedronGeometry(1, 1), scrap: new THREE.PlaneGeometry(1, 1),
    beam: new THREE.CylinderGeometry(1, 1, 1, 8, 1, true),
  };
  const batches = {};
  for (const [name, geometry] of Object.entries(geometries)) {
    const alpha = new THREE.InstancedBufferAttribute(new Float32Array(capacity), 1);
    alpha.setUsage(THREE.DynamicDrawUsage); geometry.setAttribute('effectAlpha', alpha);
    const overlay = new THREE.InstancedBufferAttribute(new Float32Array(capacity), 1);
    overlay.setUsage(THREE.DynamicDrawUsage); geometry.setAttribute('effectOverlay', overlay);
    const mesh = new THREE.InstancedMesh(geometry, material, capacity);
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    mesh.setColorAt(0, new THREE.Color()); mesh.instanceColor.setUsage(THREE.DynamicDrawUsage);
    mesh.count = 0; mesh.frustumCulled = false; mesh.renderOrder = name === 'smoke' ? 1 : 3;
    world.add(mesh); batches[name] = { mesh, alpha, overlay, count: 0 };
  }
  const slots = Array.from({ length: capacity }, () => ({
    active: false, p: new THREE.Vector3(), v: new THREE.Vector3(), q: new THREE.Quaternion(),
    scale: new THREE.Vector3(), color: new THREE.Color(), offset: new THREE.Vector3(),
  }));
  let cursor = 0, active = 0;
  const matrix = new THREE.Matrix4(), scale = new THREE.Vector3();
  const q = new THREE.Quaternion(), spinQ = new THREE.Quaternion(), Z = new THREE.Vector3(0, 0, 1);
  function spawn(kind, p, color, size, duration, options = {}) {
    let s;
    for (let n = 0; n < capacity; n++) {
      const candidate = slots[cursor]; cursor = (cursor + 1) % capacity;
      if (!candidate.active) { s = candidate; break; }
    }
    if (!s) return null;
    s.active = true; s.kind = kind; s.p.copy(p); s.color.setHex(color);
    s.scale.set(size, size, size); s.v.set(0, 0, 0); s.q.identity();
    s.age = 0; s.duration = Math.max(0.04, duration); s.growth = options.growth ?? 0;
    s.gravity = options.gravity ?? 0; s.opacity = options.opacity ?? 1;
    s.angle = options.angle ?? rand(0, TAU); s.spin = options.spin ?? 0;
    s.billboard = options.billboard ?? ['glint', 'star', 'slash', 'scrap'].includes(kind);
    s.anchor = options.anchor ?? null; s.offset.set(0, 0, 0); s.hold = options.hold ?? false;
    s.overlay = options.overlay ? 1 : 0;
    if (options.velocity) s.v.copy(options.velocity);
    if (options.scale) s.scale.copy(options.scale);
    if (options.quaternion) s.q.copy(options.quaternion);
    if (s.anchor) s.offset.copy(p).sub(s.anchor.p);
    return s;
  }
  function update(dt, cameraQ) {
    active = 0;
    for (const b of Object.values(batches)) b.count = 0;
    for (const s of slots) {
      if (!s.active) continue;
      s.age += dt;
      if (s.age >= s.duration) { s.active = false; s.anchor = null; continue; }
      active++;
      const t = s.age / s.duration;
      s.v.y -= s.gravity * dt;
      if (s.anchor) s.p.copy(s.anchor.p).add(s.offset);
      s.p.addScaledVector(s.v, dt); s.offset.addScaledVector(s.v, dt);
      const factor = s.hold ? 0.9 + 0.1 * Math.sin(t * Math.PI) : s.growth > 0 ? 0.35 + s.growth * t : 1 - t * t;
      scale.copy(s.scale).multiplyScalar(factor); q.copy(s.billboard ? cameraQ : s.q);
      spinQ.setFromAxisAngle(Z, s.angle + s.spin * s.age); q.multiply(spinQ);
      matrix.compose(s.p, q, scale);
      const b = batches[s.kind], index = b.count++;
      b.mesh.setMatrixAt(index, matrix); b.mesh.setColorAt(index, s.color);
      b.alpha.setX(index, s.opacity * Math.min(1, (1 - t) * (s.hold ? 6 : 2.5)));
      b.overlay.setX(index, s.overlay);
    }
    for (const b of Object.values(batches)) {
      b.mesh.count = b.count;
      if (b.count) { b.mesh.instanceMatrix.needsUpdate = true; b.mesh.instanceColor.needsUpdate = true; b.alpha.needsUpdate = true; b.overlay.needsUpdate = true; }
    }
  }
  function clear() { for (const s of slots) { s.active = false; s.anchor = null; } }
  return { spawn, update, clear, get active() { return active; }, capacity };
}

export function createLegacyVfx({ world, camera, legacy = new URLSearchParams(location.search).has('legacyvfx') }) {
  const shuriken = createShuriken({ world, camera });
  const kit = particleKit(world);
  const temp = new THREE.Vector3(), velocity = new THREE.Vector3();
  const cameraQ = new THREE.Quaternion(), groundQ = new THREE.Quaternion().setFromEuler(new THREE.Euler(-Math.PI / 2, 0, 0));
  const yawQ = new THREE.Quaternion(), UP = new THREE.Vector3(0, 1, 0);
  const fighters = new Map(), emitters = [], projectiles = new Map(), projectilePool = new Map();
  const hitboxPool = [], textPool = [];
  const meleeMat = new THREE.MeshBasicMaterial({ color: 0xff3355, transparent: true, opacity: 0.25, depthWrite: false });
  let showHitboxes = false, ctx = null, lastTick = -1, time = 0;
  const stats = { presentations: 0, hits: 0, blocked: 0, knockouts: 0, projectileKinds: {}, unknown: new Set() };
  const css = document.createElement('style');
  css.textContent = `.vfx-number{position:fixed;z-index:12;pointer-events:none;font:900 25px/1 system-ui,sans-serif;
    -webkit-text-stroke:3px #262235;paint-order:stroke fill;text-shadow:2px 3px 0 #262235;
    transform:translate(-50%,-50%);will-change:transform,opacity;contain:layout style}`;
  document.head.append(css);

  function sparks(p, color, count = 10, speed = 6, size = 0.07, duration = 0.3) {
    for (let i = 0; i < Math.min(36, count); i++) {
      velocity.set(rand(-1, 1), rand(-0.3, 1), rand(-1, 1)).normalize().multiplyScalar(speed * rand(0.35, 1));
      kit.spawn('spark', p, i % 4 ? color : PAPER, size, duration * rand(0.7, 1.2),
        { velocity, gravity: 6, spin: rand(-12, 12), scale: temp.set(size * 0.65, size * rand(1.5, 3.5), size * 0.65) });
    }
  }
  function burst(x, y, z, color, count = 12, speed = 7, size = 0.07) {
    sparks(new THREE.Vector3(x, y, z), color, count, speed, size);
  }
  function puff(p, color, size, smoke = false) {
    for (let i = 0; i < (smoke ? 5 : 7); i++) {
      velocity.set(rand(-1, 1), smoke ? rand(0.6, 1.7) : rand(0.1, 0.6), rand(-1, 1)).multiplyScalar(size);
      kit.spawn('smoke', p, color, size * rand(0.16, 0.32), smoke ? rand(0.45, 0.8) : rand(0.25, 0.45),
        { velocity, growth: smoke ? 2.5 : 1.8, opacity: smoke ? 0.32 : 0.4 });
    }
  }
  function ring(p, color, size, duration = 0.3, ground = true, anchor = null) {
    return kit.spawn('ring', p, color, size, duration, { growth: 1.6, billboard: !ground, quaternion: groundQ, angle: 0, opacity: 0.8, anchor });
  }
  function popText(x, y, z, text, color) {
    let s = textPool.find(t => !t.active);
    if (!s && textPool.length < 24) {
      const el = document.createElement('div'); el.className = 'vfx-number'; el.hidden = true; document.body.append(el);
      s = { el, p: new THREE.Vector3(), active: false }; textPool.push(s);
    }
    if (!s) return;
    s.active = true; s.age = 0; s.p.set(x, y, z); s.el.textContent = text; s.el.style.color = color; s.el.hidden = false;
  }
  function eventPosition(e, result) {
    const bone = e.bone && ctx?.boneWorld(e.entity, e.bone);
    const yaw = bone ? fighters.get(e.entity)?.yaw ?? e.yaw ?? 0 : e.yaw ?? 0;
    const local = e.local ?? [0, 0, 0];
    result.set(local[0], local[1], local[2]).applyAxisAngle(UP, yaw);
    return bone ? result.add(bone) : result.add(temp.fromArray(e.p));
  }
  function playPresentation(e) {
    if (!e.id) return;
    const effect = false;
    if (effect) {
      stats.presentations++;
      const placed = eventPosition(e, new THREE.Vector3());
      shuriken.play(effect, e, ctx, target => {
        const bone = e.bone && ctx.boneFrame?.(e.entity, e.bone);
        return bone ? target.fromArray(e.local ?? [0, 0, 0]).applyMatrix4(bone) : e.bone ? eventPosition(e, target) : target.copy(placed);
      },
        () => e.bone ? fighters.get(e.entity)?.yaw ?? e.yaw ?? 0 : e.yaw ?? 0);
      return;
    }
    const preset = PRESENTATION_PRESETS[e.id];
    if (!preset) {
      if (!stats.unknown.has(e.id)) { stats.unknown.add(e.id); console.warn('[VFX] Missing preset', e.id); }
      return;
    }
    stats.presentations++;
    const p = eventPosition(e, new THREE.Vector3());
    const size = preset.size * clamp(Math.max(...(e.scale ?? [1, 1, 1]).map(Math.abs)), 0.1, 6);
    const duration = preset.duration, color = preset.color;
    const attachedDuration = e.bone && e.ticks > 0 ? e.ticks / 60 : duration;
    const attachment = e.bone ? { p, e, ticks: Math.max(1, e.ticks ?? 1), kit: preset.kit, color, size, pulse: 0 } : null;
    if (attachment) { if (emitters.length >= 64) emitters.shift(); emitters.push(attachment); }
    yawQ.setFromEuler(new THREE.Euler(Math.PI / 2.8, e.yaw ?? 0, 0));
    for (const primitive of preset.kit) {
      if (primitive === 'spark') sparks(p, color, 7 + Math.round(size * 3), 3 + size * 2, 0.05 + size * 0.02, duration);
      else if (primitive === 'ring') ring(p, color, size, attachedDuration, !e.bone, attachment);
      else if (primitive === 'slash') kit.spawn('slash', p, color, size, attachedDuration,
        { billboard: false, quaternion: yawQ, spin: 5, angle: 0, anchor: attachment });
      else if (primitive === 'glint' || primitive === 'star' || primitive === 'flash') {
        kit.spawn(primitive === 'flash' ? 'star' : primitive, p, color, size * (primitive === 'flash' ? 0.65 : 0.6),
          primitive === 'flash' ? Math.min(duration, 0.13) : primitive === 'glint' ? attachedDuration : duration, { spin: 0.7, anchor: attachment });
        if (primitive === 'star') kit.spawn('glint', p, PAPER, size * 0.42, 0.12, { anchor: attachment });
      } else if (primitive === 'dust' || primitive === 'smoke') puff(p, primitive === 'smoke' ? 0x716773 : color, size, primitive === 'smoke');
      else if (primitive === 'trail') {
        if (!attachment) sparks(p, color, 5, size * 2, 0.055, duration);
      } else if (primitive === 'beam') {
        const forward = new THREE.Vector3(Math.sin(e.yaw ?? 0), 0.05, Math.cos(e.yaw ?? 0)).normalize();
        const q = new THREE.Quaternion().setFromUnitVectors(UP, forward);
        kit.spawn('beam', p.clone().addScaledVector(forward, size), color, size, attachedDuration,
          { quaternion: q, angle: 0, scale: temp.set(0.045 * size, size * 2, 0.045 * size), anchor: attachment });
      }
    }
  }
  function hit(row) {
    const [target, , damage, blocked, x, y, z, stop = 0, , , ownHitPresentation = 0] = row;
    stats.hits++; if (blocked) stats.blocked++;
    const p = new THREE.Vector3(x, y, z), strength = clamp(0.5 + damage / 18 + stop / 18, 0.6, 2.2);
    const color = blocked ? 0x8fd8ff : YELLOW;
    // Unity's CombatFeedback spawns the generic hit graphic only for moves without their own hit presentation.
    if (blocked || !ownHitPresentation)
      sparks(p, color, blocked ? 7 : Math.round(8 + strength * 6), 3 + strength * 3, 0.05 + strength * 0.025, 0.19 + strength * 0.055);
    if (blocked) {
      ring(p, color, 0.45 + strength * 0.2, 0.22, false); kit.spawn('glint', p, PAPER, 0.25, 0.1);
    } else {
      if (!ownHitPresentation) {
        kit.spawn('star', p, ORANGE, strength * 0.46, 0.13 + stop / 180);
        kit.spawn('glint', p, PAPER, strength * 0.3, 0.1);
        if (strength > 1.1) ring(p, YELLOW, strength * 0.7, 0.24, false);
      }
      const seat = ctx.seatOf(target);
      if (seat >= 0) popText(x, y + 0.6, z, `${Math.round(damage)}`, `#${ctx.seatColor(seat).toString(16).padStart(6, '0')}`);
    }
  }
  function knockout([id, x, y, z]) {
    stats.knockouts++;
    const spawn = (kind, p, color, size, duration, options = {}) => kit.spawn(kind, p, color, size, duration, { ...options, overlay: true });
    // Unity pins its flyer inside the screen; unproject at the crossing's depth to retain a stable size.
    const ndc = toThree(x, y, z).project(camera);
    ndc.x = clamp(ndc.x, -0.76, 0.76); ndc.y = clamp(ndc.y, -0.68, 0.65);
    ndc.z = clamp(ndc.z, -0.9, 0.999);
    const p = ndc.unproject(camera); p.z *= -1;
    const distance = camera.position.distanceTo(toThree(p.x, p.y, p.z));
    const size = distance * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)) * 0.19;
    const seat = ctx.seatColor(ctx.seatOf(id)), angle = rand(-0.2, 0.2);
    const right = new THREE.Vector3(1, 0, 0).applyQuaternion(cameraQ), up = new THREE.Vector3(0, 1, 0).applyQuaternion(cameraQ);
    const offset = p.clone().addScaledVector(right, size * 0.1).addScaledVector(up, -size * 0.12);
    spawn('star', offset, INK, size * 1.14, 0.58, { hold: true, angle });
    spawn('star', p.clone().addScaledVector(right, -size * 0.11), YELLOW, size * 1.06, 0.56, { hold: true, angle: angle + 0.09 });
    spawn('star', p, INK, size * 1.04, 0.55, { hold: true, angle });
    spawn('star', p, ORANGE, size * 0.94, 0.54, { hold: true, angle });
    spawn('glint', p, PAPER, size * 0.52, 0.25, { angle: 0.2 });
    const bounds = ctx.arena?.bounds;
    const target = bounds ? new THREE.Vector3((bounds[0] + bounds[1]) / 2, 1, (bounds[2] + bounds[3]) / 2) : new THREE.Vector3();
    const direction = target.sub(p).normalize();
    for (let i = 1; i <= 8; i++) spawn('smoke', p.clone().addScaledVector(direction, size * i * 0.35), seat,
      size * (0.18 - i * 0.014), 0.5, { growth: 0.5, opacity: 0.65 });
    for (let i = 0; i < 12; i++) {
      const a = i / 12 * TAU + rand(-0.2, 0.2);
      velocity.copy(right).multiplyScalar(Math.cos(a)).addScaledVector(up, Math.sin(a)).multiplyScalar(size * rand(2, 5));
      spawn('scrap', p, [PAPER, PAPER, YELLOW, seat][i % 4], size * 0.13, 0.8,
        { velocity, gravity: size * 3, spin: rand(-14, 14), scale: temp.set(size * rand(0.1, 0.2), size * 0.07, 1) });
    }
  }

  const projectileMaterials = new Map();
  const projectileGeo = {
    sphere: new THREE.SphereGeometry(1, 12, 8), cylinder: new THREE.CylinderGeometry(1, 1, 1, 12),
    cone: new THREE.ConeGeometry(1, 1, 10), torus: new THREE.TorusGeometry(1, 0.09, 5, 24),
    blade: new THREE.OctahedronGeometry(1), wing: new THREE.BoxGeometry(1, 1, 1),
  };
  function projectileMaterial(color, lit = true) {
    const key = `${color}.${lit}`;
    if (!projectileMaterials.has(key)) projectileMaterials.set(key, lit
      ? new THREE.MeshStandardMaterial({ color, roughness: 0.55, metalness: 0.25 }) : new THREE.MeshBasicMaterial({ color }));
    return projectileMaterials.get(key);
  }
  function makeProjectile(preset) {
    const root = new THREE.Group(), kind = preset.kind;
    function part(geometry, color, scale, p = [0, 0, 0], lit = true) {
      const mesh = new THREE.Mesh(projectileGeo[geometry], projectileMaterial(color, lit));
      mesh.scale.set(...scale); mesh.position.set(...p); root.add(mesh); return mesh;
    }
    let spin = null, fuse = null, wings = null;
    if (kind === 'bomb') {
      part('sphere', 0x30313c, [1, 1, 1]);
      part('torus', 0xe8b65d, [0.9, 0.9, 0.9]).rotation.x = Math.PI / 2;
      part('cylinder', 0xa07842, [0.17, 0.6, 0.17], [0.16, 1.16, 0]);
      fuse = part('blade', YELLOW, [0.23, 0.35, 0.23], [0.16, 1.52, 0], false);
      part('sphere', PAPER, [0.16, 0.26, 0.08], [-0.4, 0.32, 0.83], false);
    } else if (kind === 'rocket') {
      part('cylinder', 0x777e55, [0.42, 1.9, 0.42]);
      part('cone', 0xc85436, [0.45, 0.7, 0.45], [0, 1.25, 0]);
      part('wing', 0x4a4d43, [1.3, 0.5, 0.12], [0, -0.8, 0]);
      part('wing', 0x4a4d43, [0.12, 0.5, 1.3], [0, -0.8, 0]);
      fuse = part('cone', 0xffb941, [0.35, 1, 0.35], [0, -1.3, 0], false); fuse.rotation.z = Math.PI;
    } else if (kind === 'lid') {
      spin = part('sphere', 0xa9c5c3, [1.35, 0.2, 1.35]);
      part('torus', 0xf5d775, [1.3, 1.3, 1.3]).rotation.x = Math.PI / 2;
      part('torus', 0x43403f, [0.27, 0.27, 0.27], [0, 0.3, 0]);
    } else if (kind === 'kunai') {
      part('blade', 0xd2f1e5, [0.32, 1.6, 0.12]);
      part('cylinder', 0x37354d, [0.12, 0.9, 0.12], [0, -1.3, 0]);
      part('torus', 0x9ac8b5, [0.23, 0.23, 0.23], [0, -1.85, 0]);
    } else if (kind === 'crow') {
      part('sphere', 0x291b3b, [0.65, 0.5, 1]);
      part('sphere', 0x382249, [0.42, 0.4, 0.45], [0, 0.4, 0.7]);
      part('cone', 0xf1d5f2, [0.16, 0.6, 0.16], [0, 0.38, 1.2]).rotation.x = Math.PI / 2;
      part('sphere', 0xff72b4, [0.1, 0.1, 0.08], [0.29, 0.5, 0.95], false);
      wings = [part('wing', 0x46305c, [1.2, 0.07, 0.6], [-0.8, 0.1, 0]), part('wing', 0x46305c, [1.2, 0.07, 0.6], [0.8, 0.1, 0])];
    } else if (kind === 'water') {
      spin = part('torus', preset.color, [1.1, 1.1, 1.1], [0, 0, 0], false);
      part('torus', PAPER, [0.85, 0.85, 0.85], [0, 0, 0], false);
    } else {
      part('sphere', preset.color, [1, 1, 1], [0, 0, 0], false);
      part('sphere', 0xf2fbff, [0.7, 0.7, 0.7], [0, 0, 0.15], false);
      spin = part('torus', 0xffda7f, [1.25, 1.25, 1.25], [0, 0, 0], false); spin.rotation.x = Math.PI / 3;
    }
    root.visible = false; world.add(root);
    return { root, kind, fuse, spin, wings, color: preset.color, p: new THREE.Vector3(), direction: new THREE.Vector3() };
  }
  function updateProjectiles(list) {
    for (const v of projectiles.values()) v.seen = false;
    for (const h of list) {
      const key = `${h.owner}.${h.slot}.${h.air}.${h.emit}`;
      const moving = h.v.some(n => Math.abs(n) > 0.0001);
      if (!moving && !projectiles.has(key)) continue;
      const pkg = fighters.get(h.owner)?.pkg;
      const preset = PROJECTILE_PRESETS[`${pkg}.${h.air ? 'air' : 'ground'}.${SLOT[h.slot] ?? h.slot}`] ?? { kind: 'ki', color: 0xffc56b };
      // Emission stays stable while age advances; owner and slot separate simultaneous multishots.
      let view = projectiles.get(key);
      if (!view) {
        if (projectiles.size >= 64) continue;
        const pool = projectilePool.get(preset.kind) ?? [];
        view = pool.pop() ?? makeProjectile(preset); projectilePool.set(preset.kind, pool);
        view.trailClock = 0; projectiles.set(key, view);
        stats.projectileKinds[preset.kind] = (stats.projectileKinds[preset.kind] ?? 0) + 1;
      }
      view.seen = true; view.root.visible = true; view.p.fromArray(h.a);
      view.root.position.copy(view.p); if (moving) view.direction.fromArray(h.v).normalize();
      view.radius = Math.max(0.12, h.r); view.root.scale.setScalar(view.radius);
      if (['rocket', 'kunai'].includes(view.kind)) view.root.quaternion.setFromUnitVectors(UP, view.direction);
      else if (view.kind === 'crow' || view.kind === 'water') view.root.quaternion.setFromUnitVectors(temp.set(0, 0, 1), view.direction);
      if (++view.trailClock % 3 === 0) {
        if (view.kind === 'rocket') puff(view.p, 0x918b87, view.radius * 1.2, true);
        else if (view.kind === 'bomb') {
          const p = view.p.clone(); p.y += view.radius * 1.5; sparks(p, YELLOW, 2, 1.2, 0.035, 0.15);
        } else kit.spawn(view.kind === 'crow' ? 'scrap' : 'spark', view.p, view.color, view.radius * 0.6, 0.2,
          { opacity: 0.5, velocity: velocity.copy(view.direction).multiplyScalar(-0.5), spin: 4 });
      }
    }
    for (const [key, view] of projectiles) {
      if (view.seen) continue;
      view.root.visible = false; projectilePool.get(view.kind).push(view); projectiles.delete(key);
    }
  }
  function drawHitboxes(list) {
    let used = 0;
    if (showHitboxes) for (const h of list) {
      if (h.v.some(n => n !== 0)) continue;
      let view = hitboxPool[used++];
      if (!view) {
        view = { ball: new THREE.Mesh(unitSphere, meleeMat), end: new THREE.Mesh(unitSphere, meleeMat), seg: new THREE.Mesh(unitCylinder, meleeMat) };
        world.add(view.ball, view.end, view.seg); hitboxPool.push(view);
      }
      const a = temp.fromArray(h.a), b = velocity.fromArray(h.b);
      const capsule = h.shape === 1 && a.distanceToSquared(b) > 1e-6;
      view.ball.visible = true; view.end.visible = view.seg.visible = capsule;
      placeBall(view.ball, a, h.shape === 2 ? h.r * 0.15 : h.r);
      if (capsule) { placeBall(view.end, b, h.r); placeSegment(view.seg, a, b, h.r); }
    }
    for (let i = used; i < hitboxPool.length; i++) {
      const v = hitboxPool[i]; v.ball.visible = v.end.visible = v.seg.visible = false;
    }
  }
  function reset() {
    kit.clear(); shuriken.clear(); emitters.length = 0;
    for (const s of textPool) { s.active = false; s.el.hidden = true; }
    for (const [key, view] of projectiles) {
      view.root.visible = false; projectilePool.get(view.kind).push(view); projectiles.delete(key);
    }
  }
  function onFrame(frame, context) {
    ctx = context;
    if (frame.tick != null && frame.tick < lastTick) reset(); lastTick = frame.tick ?? lastTick;
    fighters.clear(); for (const f of frame.fighters) fighters.set(f.id, f);
    for (let i = emitters.length - 1; i >= 0; i--) {
      const e = emitters[i];
      if (--e.ticks <= 0 || !fighters.has(e.e.entity)) { emitters.splice(i, 1); continue; }
      eventPosition(e.e, e.p);
      if (++e.pulse % 3 === 0 && e.kit.includes('trail')) {
        kit.spawn('spark', e.p, e.color, e.size * 0.12, 0.22, { opacity: 0.7, spin: 3 });
        if (e.pulse % 6 === 0) kit.spawn('slash', e.p, e.color, e.size * 0.5, 0.14, { opacity: 0.45 });
      }
      if (e.pulse % 10 === 0 && e.kit.includes('glint')) kit.spawn('glint', e.p, e.color, e.size * 0.3, 0.18, { anchor: e });
    }
    shuriken.tick(frame);
    for (const e of frame.events ?? []) playPresentation(e);
    for (const row of frame.hits ?? []) hit(row);
    cameraQ.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    for (const row of frame.deaths ?? []) knockout(row);
    updateProjectiles(frame.hitboxes ?? []);
  }
  function update(dt) {
    dt = clamp(dt, 0, 0.05); time += dt;
    cameraQ.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    for (const e of emitters) eventPosition(e.e, e.p);
    kit.update(dt, cameraQ);
    shuriken.update(dt);
    for (const view of projectiles.values()) {
      if (view.spin) view.spin.rotation.z += dt * 10;
      if (view.fuse) view.fuse.scale.y = 0.28 + Math.sin(time * 23) * 0.06;
      if (view.wings) { view.wings[0].rotation.z = Math.sin(time * 24) * 0.55; view.wings[1].rotation.z = -Math.sin(time * 24) * 0.55; }
    }
    for (const s of textPool) {
      if (!s.active) continue;
      s.age += dt;
      if (s.age > 0.65) { s.active = false; s.el.hidden = true; continue; }
      const projected = toThree(s.p.x, s.p.y + s.age * 1.8, s.p.z).project(camera);
      s.el.style.left = `${(projected.x * 0.5 + 0.5) * innerWidth}px`;
      s.el.style.top = `${(-projected.y * 0.5 + 0.5) * innerHeight}px`;
      s.el.style.opacity = `${clamp((0.65 - s.age) * 4, 0, 1)}`;
      s.el.style.transform = `translate(-50%,-50%) rotate(-8deg) scale(${1 + Math.max(0, 0.12 - s.age) * 3})`;
    }
  }
  return {
    drawHitboxes, burst, popText, onFrame, update, toggleHitboxes: () => { showHitboxes = !showHitboxes; },
    get stats() { return { ...stats, unknown: [...stats.unknown], particles: kit.active, capacity: kit.capacity, shuriken: shuriken.stats, projectiles: projectiles.size, emitters: emitters.length }; },
  };
}
