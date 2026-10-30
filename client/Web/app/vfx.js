import * as THREE from 'three';
import { createShuriken, EFFECTS, PROJECTILES, DEFAULT_PROJECTILE, prepareEffects } from './vfx/shuriken.js';

await prepareEffects();
import { toThree, unitSphere, unitCylinder, placeSegment, placeBall } from './geometry.js';
import { createGraphicHits, classifyImpact } from './vfx/graphic-hit.js';
import { createKnockoutBursts } from './vfx/knockout.js';
const legacyMode = new URLSearchParams(location.search).has('legacyvfx');
const legacyPlayer = legacyMode ? (await import('./vfx/legacy.js')).createLegacyVfx : null;

const TAU = Math.PI * 2;
const SLOT = { 3: '1', 8: '3', 9: '4', 11: 'A', 4: 'E', 5: 'R', 6: 'F' };
const PAPER = 0xded8c9;
const rand = (a, b) => a + Math.random() * (b - a);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

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
  const geometries = { spark: new THREE.OctahedronGeometry(1) };
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

export function createVfx({ world, camera, legacy = legacyMode }) {
  if (legacy && legacyPlayer) return legacyPlayer({ world, camera, legacy: true });
  const graphicHits = createGraphicHits({ world, camera }), knockoutBursts = createKnockoutBursts({ world, camera });
  const shuriken = createShuriken({ world, camera });
  const kit = particleKit(world);
  const temp = new THREE.Vector3(), velocity = new THREE.Vector3();
  const cameraQ = new THREE.Quaternion();
  const yawQ = new THREE.Quaternion(), UP = new THREE.Vector3(0, 1, 0);
  const fighters = new Map(), projectiles = new Map();
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
    if (!e.id && e.source !== 'BlockContact') return;
    if (e.source === 'BlockContact') {
      const fighter = fighters.get(e.entity), contact = new THREE.Vector3().fromArray(e.p);
      let normal = null;
      if (fighter) {
        const center = new THREE.Vector3().fromArray(fighter.p);
        normal = contact.clone().sub(center);
        if (normal.lengthSq() > .000001) { normal.normalize(); contact.copy(center).addScaledVector(normal, ctx.infoOf?.(fighter.pkg)?.shieldRadius ?? .98); }
      }
      graphicHits.spawn(contact.toArray(), 'Block', 0, undefined, normal);
      return;
    }
    const effect = EFFECTS[e.id];
    if (!effect) {
      if (!stats.unknown.has(e.id)) { stats.unknown.add(e.id); console.warn('[VFX] Missing export', e.id); }
      return;
    }
    stats.presentations++;
    const placed = eventPosition(e, new THREE.Vector3());
    shuriken.play(effect, e, ctx, target => {
      const bone = e.bone && ctx.boneFrame?.(e.entity, e.bone);
      return bone ? target.fromArray(e.local ?? [0, 0, 0]).applyMatrix4(bone) : e.bone ? eventPosition(e, target) : target.copy(placed);
    }, () => e.bone ? fighters.get(e.entity)?.yaw ?? e.yaw ?? 0 : e.yaw ?? 0);
  }
  function hit(row) {
    const [target, , damage, blocked, x, y, z, stop = 0, , , ownHitPresentation = 0,
      force = 0, dirX = 0, dirZ = 1, angle = 0, hitX = x, hitY = y, hitZ = z] = row;
    stats.hits++; if (blocked) { stats.blocked++; return; }
    if (!ownHitPresentation) {
      const radians = angle * Math.PI / 180;
      graphicHits.spawn([hitX, hitY, hitZ], classifyImpact(damage, force), stop, [dirX * Math.cos(radians), Math.sin(radians), dirZ * Math.cos(radians)]);
    }
    const seat = ctx.seatOf(target);
    if (damage > 0 && seat >= 0) popText(x, y + .6, z, `${Math.round(damage)}`, `#${ctx.seatColor(seat).toString(16).padStart(6, '0')}`);
  }
  function knockout([id, x, y, z]) {
    stats.knockouts++;
    knockoutBursts.spawn([x, y, z], ctx.seatColor(ctx.seatOf(id)));
  }
  function updateProjectiles(list) {
    for (const v of projectiles.values()) v.seen = false;
    for (const h of list) {
      const key = `${h.owner}.${h.activation ?? 0}.${h.operation ?? 0}.${h.emit}.${h.slot}.${h.air}`;
      if (!h.projectile && !h.v.some(n => Math.abs(n) > .0001) && !projectiles.has(key)) continue;
      const pkg = fighters.get(h.owner)?.pkg, id = `${pkg}.${h.air ? 'air' : 'ground'}.${SLOT[h.slot] ?? h.slot}`, effect = PROJECTILES[id] ?? DEFAULT_PROJECTILE;
      let view = projectiles.get(key);
      if (!view) {
        if (projectiles.size >= 64) continue;
        const e = {id,entity:h.owner,p:h.a,scale:[effect.scale,effect.scale,effect.scale],ticks:2147483647,projectile:true,quaternion:[0,0,0,1]};
        view = {p:new THREE.Vector3().fromArray(h.a),event:e,seen:true};
        view.instance = shuriken.play(effect, e, ctx, target => target.copy(view.p), () => 0);
        if (!view.instance) continue;
        projectiles.set(key, view);
        stats.projectileKinds[id] = (stats.projectileKinds[id] ?? 0) + 1;
      }
      view.seen = true; view.p.fromArray(h.a); view.event.perched = h.attached ?? 0;
      if (h.perch && h.attached) {
        const head = ctx.boneWorld(h.attached, 'bone.head'); if (head) view.p.copy(head).add(temp.set(0,.22,0));
        const carrier = fighters.get(h.attached);
        yawQ.setFromAxisAngle(UP, carrier?.yaw ?? 0); view.event.quaternion = yawQ.toArray();
      } else if (h.v.some(n => Math.abs(n) > .0001) && (effect.orientation === 1 || h.perch)) {
        const direction = velocity.fromArray(h.v).normalize(), up = Math.abs(direction.y) > .999 ? new THREE.Vector3(0,0,1) : UP;
        yawQ.setFromRotationMatrix(new THREE.Matrix4().lookAt(direction,new THREE.Vector3(),up));
        if (!h.perch) yawQ.multiply(new THREE.Quaternion().setFromEuler(new THREE.Euler(Math.PI/2,0,0)));
        view.event.quaternion = yawQ.toArray();
      }
      const size = effect.scale * (h.visualScale ?? 1); view.event.scale = [size,size,size];
    }
    for (const [key, view] of projectiles) if (!view.seen) { shuriken.release(view.instance); projectiles.delete(key); }
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
    kit.clear(); shuriken.clear(); graphicHits.clear(); knockoutBursts.clear();
    for (const s of textPool) { s.active = false; s.el.hidden = true; }
    projectiles.clear();
  }
  function onFrame(frame, context) {
    ctx = context;
    if (frame.tick != null && frame.tick < lastTick) reset(); lastTick = frame.tick ?? lastTick;
    fighters.clear(); for (const f of frame.fighters) fighters.set(f.id, f);
    shuriken.tick(frame, ctx);
    for (const e of frame.events ?? []) playPresentation(e);
    for (const row of frame.hits ?? []) hit(row);
    cameraQ.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    for (const row of frame.deaths ?? []) knockout(row);
    updateProjectiles(frame.hitboxes ?? []);
  }
  function update(dt) {
    dt = clamp(dt, 0, 0.05); time += dt;
    cameraQ.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    kit.update(dt, cameraQ);
    shuriken.update(dt);
    graphicHits.update(dt); knockoutBursts.update(dt);
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
    get stats() { return { ...stats, unknown: [...stats.unknown], particles: kit.active, capacity: kit.capacity, shuriken: shuriken.stats, graphicHits: graphicHits.stats, knockoutBursts: knockoutBursts.stats, projectiles: projectiles.size, emitters: 0 }; },
  };
}
