import * as THREE from 'three';
import { value, color, bakeEffect } from './curves.js';

const data = await (await fetch(new URL('./effects.json', import.meta.url))).json();
export const EFFECTS = data.presentations;
export const PROJECTILES = data.projectiles;
export const DEFAULT_PROJECTILE = data.defaults.projectile;
const prefabs = new Map(Object.values(EFFECTS).map(effect => [effect.prefab, effect]));
const TAU = Math.PI * 2, UP = new THREE.Vector3(0, 1, 0), Z = new THREE.Vector3(0, 0, 1);
const textures = new Map(), geometries = new Map();
const white = new THREE.DataTexture(new Uint8Array([255, 255, 255, 255]), 1, 1); white.needsUpdate = true;
function noise(x, y, z) {
  const hash = (a,b,c) => {const n = Math.sin(a*127.1+b*311.7+c*74.7)*43758.5453;return (n-Math.floor(n))*2-1;};
  const ix=Math.floor(x),iy=Math.floor(y),iz=Math.floor(z),sx=x-ix,sy=y-iy,sz=z-iz;
  const u=sx*sx*(3-2*sx),v=sy*sy*(3-2*sy),w=sz*sz*(3-2*sz),mix=(a,b,t)=>a+(b-a)*t;
  return mix(mix(mix(hash(ix,iy,iz),hash(ix+1,iy,iz),u),mix(hash(ix,iy+1,iz),hash(ix+1,iy+1,iz),u),v),
    mix(mix(hash(ix,iy,iz+1),hash(ix+1,iy,iz+1),u),mix(hash(ix,iy+1,iz+1),hash(ix+1,iy+1,iz+1),u),v),w);
}
const loader = new THREE.TextureLoader();
const texture = file => {
  if (!file) return white;
  if (!textures.has(file)) textures.set(file, loader.loadAsync(new URL('../../assets/vfx/' + file, import.meta.url).href).then(t => {
    t.colorSpace = THREE.SRGBColorSpace; t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping; textures.set(file, t); return t;
  }));
  return textures.get(file);
};
export async function prepareEffects(packages = null) {
  const ids = new Set();
  for (const [id, effect] of Object.entries({...EFFECTS, ...PROJECTILES})) if (!packages || packages.includes(id.split('.')[id.startsWith('presentation.') ? 1 : 0])) {
    bakeEffect(effect);
    for (const n of effect.nodes) {
      if (n.system?.material) ids.add(n.system.material);
      if (n.system?.trailMaterial) ids.add(n.system.trailMaterial);
      if (n.trail?.material) ids.add(n.trail.material);
      for (const id of n.mesh?.materials ?? []) if (id) ids.add(id);
    }
  }
  await Promise.all([...ids].map(id => texture(data.materials[id]?.texture)));
}
function geometry(ref) {
  const key = ref?.id ?? ref?.builtin ?? 'quad';
  if (ref?.unsupported) return null;
  if (geometries.has(key)) return geometries.get(key);
  let g;
  if (ref?.id) {
    const d = data.meshes[ref.id];
    g = new THREE.BufferGeometry();
    for (const [name, size] of [['position', 3], ['normal', 3], ['uv', 2], ['color', 4]]) if (d[name]) g.setAttribute(name, new THREE.Float32BufferAttribute(d[name], size));
    if (!d.uv) g.setAttribute('uv', new THREE.Float32BufferAttribute(new Float32Array(d.position.length / 3 * 2), 2));
    g.setIndex(d.index);
  } else if (key === 10202) g = new THREE.BoxGeometry(1, 1, 1);
  else if (key === 10207) g = new THREE.SphereGeometry(.5, 10, 6);
  else if (key === 10206) g = new THREE.CylinderGeometry(.5, .5, 2, 10);
  else g = new THREE.PlaneGeometry(1, 1);
  geometries.set(key, g); return g;
}

export function createShuriken({ world, camera, capacity = 1024 }) {
  const batches = new Map(), instances = [], particles = [];
  const free = Array.from({ length: capacity }, () => ({ p: new THREE.Vector3(), v: new THREE.Vector3(), initialV: new THREE.Vector3(), noise: new THREE.Vector3(), size: new THREE.Vector3(), rotation: new THREE.Vector3(), startColor: [1, 1, 1, 1] }));
  const matrix = new THREE.Matrix4(), scale = new THREE.Vector3(), q = new THREE.Quaternion(), spin = new THREE.Quaternion();
  const cameraQ = new THREE.Quaternion(), p = new THREE.Vector3(), v = new THREE.Vector3(), w = new THREE.Vector3(), center = new THREE.Vector3();
  const euler = new THREE.Euler(0, 0, 0, 'ZXY'), rgba = [1, 1, 1, 1], rgba2 = [1, 1, 1, 1];
  let dropped = 0;
  function batch(materialId, ref, order = 0) {
    const spec = data.materials[materialId];
    if (!spec) return null;
    const key = JSON.stringify([spec.texture, spec.blend, ref, order, spec.colorMode, spec.shader, spec.depthTest]);
    if (batches.has(key)) return batches.get(key);
    const base = geometry(ref);
    if (!base) return null;
    const g = base.clone();
    const tint = new THREE.InstancedBufferAttribute(new Float32Array(capacity * 4), 4);
    const uv = new THREE.InstancedBufferAttribute(new Float32Array(capacity * 4), 4);
    const turn = new THREE.InstancedBufferAttribute(new Float32Array(capacity), 1);
    g.setAttribute('particleColor', tint); g.setAttribute('frameRect', uv); g.setAttribute('uvTurn', turn);
    tint.setUsage(THREE.DynamicDrawUsage); uv.setUsage(THREE.DynamicDrawUsage);
    const map = texture(spec.texture);
    const material = new THREE.ShaderMaterial({
      uniforms: { map: { value: map.isTexture ? map : white } }, transparent: true, depthWrite: !!spec.depthWrite, depthTest: spec.depthTest !== 8, side: THREE.DoubleSide,
      blending: spec.blend === 'additive' ? THREE.AdditiveBlending : spec.blend === 'multiply' ? THREE.MultiplyBlending : spec.blend === 'premultiply' ? THREE.CustomBlending : THREE.NormalBlending,
      blendSrc: THREE.OneFactor, blendDst: THREE.OneMinusSrcAlphaFactor,
      vertexShader: `attribute vec4 particleColor; attribute vec4 frameRect; attribute float uvTurn; ${g.hasAttribute('color') ? 'attribute vec4 color;' : ''}
        varying vec4 tint; varying vec2 texUv;
        void main(){ tint=particleColor${g.hasAttribute('color') ? '*color' : ''}; vec2 coords=uvTurn>.5?vec2(1.-uv.y,uv.x):uv; texUv=coords*frameRect.xy+frameRect.zw;
          gl_Position=projectionMatrix*modelViewMatrix*instanceMatrix*vec4(position,1.); }`,
      fragmentShader: `uniform sampler2D map; varying vec4 tint; varying vec2 texUv;
        void main(){gl_FragColor=texture2D(map,texUv)*tint;
          ${spec.shader === 'nyxPaper' ? 'if(gl_FragColor.a<.01)discard;gl_FragColor=clamp(gl_FragColor,0.,1.);' : ''}
          ${spec.blend === 'premultiply' ? 'gl_FragColor.rgb *= gl_FragColor.a;' : ''}
          #include <tonemapping_fragment>
          #include <colorspace_fragment>
        }`,
    });
    material.userData.hasMap = !!spec.texture;
    if (!map.isTexture) Promise.resolve(map).then(t => { material.uniforms.map.value = t; });
    const mesh = new THREE.InstancedMesh(g, material, capacity);
    mesh.name = `Shuriken ${spec.name}`;
    mesh.count = 0; mesh.frustumCulled = false; mesh.renderOrder = order;
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage); world.add(mesh);
    const b = { mesh, tint, uv, turn, count: 0 }; batches.set(key, b); return b;
  }
  function random(instance) {
    let x = instance.seed; x ^= x << 13; x ^= x >>> 17; x ^= x << 5; instance.seed = x;
    return (x >>> 0) / 4294967296;
  }
  const behaviours = {
    paperCue(i, n, b) {
      const f = i.fighter, current = f && ['Attacking', 'Aiming'].includes(f.state) && f.sequence === i.sequence;
      const tick = current ? f.attackTick : -1;
      if (current && b.FollowOwner) {
        const origin = b.OriginBoneId && i.context.boneWorld(i.event.entity, b.OriginBoneId);
        p.copy(origin ?? v.fromArray(f.p));
        n.object.position.copy(n.object.parent.worldToLocal(p));
        n.object.parent.getWorldQuaternion(spin).invert();
        n.object.quaternion.copy(spin).multiply(q.setFromAxisAngle(UP, f.yaw));
        n.object.parent.getWorldScale(scale);
        n.object.scale.set(...scale.toArray().map(x => Math.abs(x) > .0001 ? 1 / x : 1));
      }
      for (const layer of b.Layers) {
        const content = i.nodes[layer.Content]; if (!content) continue;
        content.object.visible = current && tick >= layer.StartTick && tick < layer.EndTick;
        if (!content.object.visible) continue;
        const age = tick - layer.StartTick, t = Math.min(1, age / Math.max(1, layer.EndTick - layer.StartTick - 1));
        const a = layer.From, z = layer.To, size = layer.Scale;
        content.object.position.set(a.x + (z.x - a.x) * t, a.y + (z.y - a.y) * t, a.z + (z.z - a.z) * t);
        content.object.quaternion.setFromAxisAngle(UP, age * layer.TurnDegreesPerTick * Math.PI / 180);
        content.object.scale.set(size.x, size.y, size.z).multiplyScalar(1 + .035 * Math.sin(t * Math.PI));
      }
    },
    spin(i, n, b) {
      n.object.quaternion.fromArray(n.spec.local.q).multiply(q.setFromAxisAngle(v.set(b.Axis.x, b.Axis.y, b.Axis.z).normalize(), i.age * b.DegreesPerSecond * Math.PI / 180));
    },
    crow(i, n, b) {
      const perched = !!i.event.perched, flap = (perched ? 68 : Math.sin(i.age * 32) * 40) * Math.PI / 180;
      for (const [index, sign] of [[b.LeftWing, -1], [b.RightWing, 1]]) if (i.nodes[index])
        i.nodes[index].object.quaternion.setFromEuler(euler.set(0, flap * sign, (perched ? -56 : 12) * sign * Math.PI / 180));
    },
    chargeTiers(i, n, b) {
      const f = i.fighter, charging = f?.state === 'Aiming' && f.slot === b.Slot;
      if (i.nodes[b.Charging]) i.nodes[b.Charging].emitting = charging;
      if (!charging) return;
      const tier = f.chargeTier ?? 0;
      for (let reached = (n.shownTier ?? 0) + 1; reached <= tier; reached++) activate(i, i.nodes[b['Tier' + (reached + 1)]]);
      n.shownTier = Math.max(n.shownTier ?? 0, tier);
      if (i.nodes[b.Grow]) i.nodes[b.Grow].object.scale.setScalar(1 + b.GrowPerTier * n.shownTier);
    },
    chargeVariant(i, n, b) {
      if (!n.variantSet) {
        const fired = lastCharge.get(i.event.entity);
        if (fired?.slot === b.Slot && fired.tier >= b.MinimumTier && i.nodes[b.Variant]) i.nodes[b.Variant].object.visible = true;
        n.variantSet = true;
      }
    },
    propVisibility(i, n, b) { i.context.viewOf?.(i.event.entity)?.overrideProps?.(b); },
    chargeProp(i, n, b) {
      const f = i.fighter;
      if (f?.state !== 'Aiming' || f.slot !== b.Slot) return;
      const progress = f.chargeProgress ?? 0;
      if (i.nodes[b.Marks]) i.nodes[b.Marks].rateScale = 1 + b.MarksBoost * progress;
      i.context.viewOf?.(i.event.entity)?.chargeProp?.(b, progress, i.age);
    },
    placedTether(i, n, b) {
      const f = i.fighter, victim = f?.partner && i.context.fighterOf?.(f.partner);
      if (!victim) return;
      p.fromArray(victim.p); p.y += i.context.infoOf?.(victim.pkg)?.modelYOffset ?? 0;
      n.object.position.copy(n.object.parent.worldToLocal(p));
      v.fromArray(f.p).sub(w.fromArray(victim.p));
      n.object.parent.getWorldQuaternion(spin).invert();
      n.object.quaternion.copy(spin).multiply(q.setFromAxisAngle(UP, Math.atan2(v.x, v.z)));
      if (i.nodes[b.Drag]) { i.nodes[b.Drag].object.visible = true; i.nodes[b.Drag].object.scale.z = Math.max(.4, Math.min(2.5, v.length() * .4)); }
      if (!n.drag && prefabs.has(b.DragPrefab)) {
        const event = {...i.event, id:b.DragPrefab, ticks:2147483647, bone:null, source:'Tether'};
        n.drag = play(prefabs.get(b.DragPrefab), event, i.context, target => target.copy(i.root.position), i.yaw);
        if (n.drag) n.drag.tetherPartner = f.partner;
      }
    },
    flashDissolve(i, n, b) {
      const f = i.fighter;
      if (!f || !['Attacking', 'Aiming'].includes(f.state) || f.sequence !== i.sequence || !f.flash) return;
      const {stanceTicks, legTicks, legAnimationId} = f.flash;
      const leg = f.anim === legAnimationId; n.hadLeg ||= leg;
      const look = leg && f.attackTick < legTicks - 1 ? 'leg' : !n.hadLeg && f.attackTick < stanceTicks ? 'stance' : 'visible';
      const half = i.context.infoOf?.(f.pkg)?.half ?? .85;
      const cut = look === 'stance' ? f.p[1] - half + Math.min(1, f.attackTick / stanceTicks) ** 2.2 * (half * 2 + .25) : -10000;
      i.context.viewOf?.(f.id)?.dissolve?.(look === 'leg', cut);
      if (n.lastTick === f.attackTick && n.lastAnim === f.anim) return;
      const count = look === 'stance' ? 15 : look === 'leg' ? 45 : n.look === 'leg' ? 70 : 0;
      const hash = x => { const r = Math.sin(x * 12.9898) * 43758.5453; return r - Math.floor(r); };
      for (let k = 0; k < count; k++) {
        const t = f.attackTick, a = hash(t * 397 + k * 31), c = hash(t * 113 + k * 73), d = hash(t * 251 + k * 17);
        p.fromArray(f.p);
        if (look === 'stance') p.add(v.set((a - .5) * .7, cut - f.p[1] + (c - .5) * .24, (d - .5) * .5));
        else { if (n.lastPosition) p.lerp(n.lastPosition, 1 - a); p.add(v.set((c - .5) * .45, (d - .5) * half * 2 * (look === 'leg' ? .28 : 1), (c - .5) * .45)); }
        emitAt(i, i.nodes[b.Pixels], p);
        if (look === 'stance' && k % 4 === 0) emitAt(i, i.nodes[b.Glyphs], p.clone().add(v.set(0, .1, 0)));
      }
      n.lastPosition ??= new THREE.Vector3(); n.lastPosition.fromArray(f.p);
      n.look = look; n.lastTick = f.attackTick; n.lastAnim = f.anim;
    },
  };
  const lastCharge = new Map();
  function activate(i, node) {
    if (!node) return;
    node.object.visible = true;
    for (const n of i.nodes) if (n === node || isChild(n.object, node.object)) { n.playing = true; n.startedAt = i.age; n.last = -1e-7; }
  }
  function emitAt(i, n, position) {
    if (!n?.spec.system) return;
    const a = spawn(i, n, 0);
    if (a) { a.p.copy(position); if (n.spec.system.main.simulationSpace === 'local') n.object.worldToLocal(a.p); }
  }
  function transform(instance) {
    instance.place(instance.root.position);
    instance.root.quaternion.setFromAxisAngle(UP, instance.yaw());
    instance.root.scale.fromArray(instance.event.scale ?? [1, 1, 1]);
    const bone = instance.event.bone && instance.context.boneFrame?.(instance.event.entity, instance.event.bone);
    if (bone) {
      bone.decompose(p, instance.root.quaternion, scale);
      instance.root.scale.multiply(scale);
    }
    if (instance.event.rotation) instance.root.quaternion.multiply(q.setFromEuler(euler.set(...instance.event.rotation.map(v => v * Math.PI / 180))));
    if (instance.event.quaternion) instance.root.quaternion.fromArray(instance.event.quaternion);
    instance.root.updateMatrixWorld(true);
    for (const node of instance.nodes) {
      const b = node.spec.behaviours;
      if (b.frame?.UnitScale) {
        instance.root.getWorldScale(scale);
        node.object.scale.set(1 / (Math.abs(scale.x) > 1e-4 ? scale.x : 1), 1 / (Math.abs(scale.y) > 1e-4 ? scale.y : 1), 1 / (Math.abs(scale.z) > 1e-4 ? scale.z : 1));
      }
      if (b.frame?.Upright) {
        q.setFromAxisAngle(UP, instance.yaw());
        node.object.parent.getWorldQuaternion(spin).invert(); node.object.quaternion.copy(spin).multiply(q);
      }
      if (b.attackClipShow) {
        const spec = b.attackClipShow, f = instance.fighter;
        const show = !!f && (spec.WhileRiding ? f.state === 'Shell' : spec.AnimationIds.includes(f.anim)) && (!spec.GroundedOnly || f.grounded);
        node.emitting = show;
        const grow = instance.nodes[spec.Grow];
        if (grow) grow.object.scale.setScalar(Math.max(.001, Math.min(1, instance.age / (spec.EaseSeconds || .001)) * spec.Scale * (show ? 1 : 0)));
      }
      if (b.followOwnerBone && instance.age <= b.followOwnerBone.Seconds) {
        const bone = instance.context.boneWorld(instance.event.entity, b.followOwnerBone.BoneId);
        if (bone) { node.object.getWorldPosition(p); p.x = bone.x; p.z = bone.z; node.object.position.copy(node.object.parent.worldToLocal(p)); }
      }
      if (b.snapToOwnerBone && !node.snapped) {
        const bone = instance.context.boneWorld(instance.event.entity, b.snapToOwnerBone.BoneId ?? 'bone.head');
        if (bone) {
          p.copy(bone); const o = b.snapToOwnerBone.Offset; if (o) p.add(v.set(o.x, o.y, o.z));
          node.object.position.copy(node.object.parent.worldToLocal(p)); node.snapped = true;
        }
      }
      if (b.snapToFloor && !node.floorSet) {
        node.object.parent.getWorldPosition(p);
        const y = instance.context.floorAt?.(p.x, p.z, p.y);
        if (y != null && p.y - y < (b.snapToFloor.MaxDrop ?? 3) && p.y >= y - .5) {
          node.object.position.copy(node.object.parent.worldToLocal(p.set(p.x, y + (b.snapToFloor.Lift ?? .03), p.z)));
        } else if (instance.context.floorAt) node.object.visible = false;
        node.floorSet = true;
      }
      if (b.yawBillboard) {
        node.object.parent.getWorldQuaternion(spin);
        w.copy(UP).applyQuaternion(spin).normalize();
        v.copy(Z).applyQuaternion(cameraQ).addScaledVector(w, -v.dot(w)).normalize();
        matrix.lookAt(v, center.set(0,0,0), w); q.setFromRotationMatrix(matrix);
        node.object.quaternion.copy(spin.invert()).multiply(q);
        p.fromArray(node.spec.local.p).applyMatrix4(node.object.parent.matrixWorld);
        p.add(v.set(1,0,0).applyQuaternion(cameraQ).multiplyScalar(b.yawBillboard.ViewRightOffset ?? 0));
        node.object.position.copy(node.object.parent.worldToLocal(p));
      }
      for (const [key, spec] of Object.entries(b)) behaviours[key]?.(instance, node, spec);
      node.object.updateMatrixWorld(true);
    }
  }
  function play(effect, event, context, place, yaw) {
    if (instances.length >= 64) { dropped++; return null; }
    const instance = { event, context, place, yaw, effect, age: 0, ticks: event.ticks > 0 ? event.ticks : 600,
      fighter: context.fighterOf?.(event.entity), root: new THREE.Object3D(), nodes: [], seed: 2166136261, elapsed: 0, bound: !!event.bone && event.source === 'Timeline' && effect.nodes.some(n => n.parent === -1 && n.behaviours.attackBound) };
    instance.sequence = event.sequence ?? instance.fighter?.sequence;
    instance.startAttackTick = instance.lastAttackTick = instance.fighter?.attackTick ?? 0;
    instance.hasMesh = effect.nodes.some(n => n.mesh?.enabled);
    for (const ch of event.id) instance.seed = Math.imul(instance.seed ^ ch.charCodeAt(0), 16777619);
    for (const spec of effect.nodes) {
      const object = new THREE.Object3D(); object.position.fromArray(spec.local.p); object.quaternion.fromArray(spec.local.q); object.scale.fromArray(spec.local.s);
      if (spec.parent < 0) { object.position.set(0, 0, 0); object.quaternion.identity(); if (event.bone) object.scale.set(1, 1, 1); }
      object.visible = !!spec.active;
      (spec.parent < 0 ? instance.root : instance.nodes[spec.parent].object).add(object);
      const s = spec.system;
      const node = { spec, object, startedAt: 0, clock: 0, carry: 0, emitted: 0, count: 0, delay: s ? value(s.main.startDelay, 0, random(instance)) : 0,
        previous: new THREE.Vector3(), first: true, last: -1e-7 };
      if (s) {
        node.batch = batch(s.material, s.renderer.mode === 4 ? s.renderer.mesh : null, s.renderer.order - s.renderer.sortingFudge);
        if (s.trails) { node.ribbonBatch = batch(s.trailMaterial ?? s.material, null, s.renderer.order); node.ribbon = []; }
      }
      if (spec.trail?.m_Enabled) { node.trailBatch = batch(spec.trail.material, null, spec.trail.m_SortingOrder); node.points = []; }
      if (spec.mesh?.enabled) node.meshBatch = batch(spec.mesh.materials[0], spec.mesh.geometry, spec.mesh.order);
      instance.nodes.push(node);
    }
    for (const n of instance.nodes) {
      const variant = n.spec.behaviours.damageScaleVariant;
      if (variant) {
        const heavy = Math.abs(event.scale?.[0] ?? 1) >= variant.HeavyFromScale;
        if (instance.nodes[variant.Regular]) instance.nodes[variant.Regular].object.visible = !heavy;
        if (instance.nodes[variant.Heavy]) instance.nodes[variant.Heavy].object.visible = heavy;
      }
    }
    bakeEffect(effect); transform(instance);
    for (const node of instance.nodes) node.object.getWorldPosition(node.previous);
    instances.push(instance); return instance;
  }
  function spawn(instance, node, time) {
    const s = node.spec.system, m = s.main;
    if (!free.length || node.count >= m.maxParticles || !node.batch) { if (!free.length) dropped++; return; }
    const a = free.pop(), r = () => random(instance), t = time / m.duration;
    a.instance = instance; a.node = node; a.age = 0; a.random = r(); a.duration = Math.max(.001, value(m.startLifetime, t, r()));
    a.size.set(value(m.startSize, t, r()), value(m.size3D ? m.startSizeY : m.startSize, t, a.random), value(m.size3D ? m.startSizeZ : m.startSize, t, a.random));
    if (!m.size3D) a.size.setScalar(value(m.startSize, t, a.random));
    a.rotation.set(m.rotation3D ? value(m.startRotationX, t, r()) : 0, m.rotation3D ? value(m.startRotationY, t, r()) : 0, value(m.startRotation, t, r()));
    color(m.startColor, t, r(), a.startColor);
    a.p.set(0, 0, 0); a.v.set(0, 0, 1);
    const sh = s.shape;
    if (sh) {
      const angle = r() * (sh.arc?.value ?? 360) * Math.PI / 180, radius = (sh.radius?.value ?? 1) * Math.sqrt(1 - r() * (1 - (1 - sh.radiusThickness) ** 2));
      if (sh.type === 0 || sh.type === 1) {
        a.v.set(Math.cos(angle) * Math.sqrt(1 - (2 * a.random - 1) ** 2), 2 * a.random - 1, Math.sin(angle) * Math.sqrt(1 - (2 * a.random - 1) ** 2));
        a.p.copy(a.v).multiplyScalar(radius);
      } else if ([4, 7, 10].includes(sh.type)) {
        a.p.set(Math.cos(angle) * radius, Math.sin(angle) * radius, 0);
        const spread = sh.type === 10 ? Math.PI / 2 : sh.angle * Math.PI / 180;
        a.v.set(Math.cos(angle) * Math.sin(spread), Math.sin(angle) * Math.sin(spread), Math.cos(spread));
      } else if ([5, 6].includes(sh.type)) {
        const size = sh.m_Scale ?? { x: 1, y: 1, z: 1 };
        a.p.set((r() - .5) * size.x, (r() - .5) * size.y, (r() - .5) * size.z);
      }
      const rot = sh.m_Rotation ?? { x: 0, y: 0, z: 0 };
      q.setFromEuler(euler.set(rot.x * Math.PI / 180, rot.y * Math.PI / 180, rot.z * Math.PI / 180));
      if (![5, 6].includes(sh.type) && sh.m_Scale) a.p.multiply(v.set(sh.m_Scale.x, sh.m_Scale.y, sh.m_Scale.z));
      a.p.applyQuaternion(q); a.v.applyQuaternion(q);
      if (sh.m_Position) a.p.add(v.set(sh.m_Position.x, sh.m_Position.y, sh.m_Position.z));
    }
    a.v.multiplyScalar(value(m.startSpeed, t, r()));
    if (m.simulationSpace === 'world') {
      a.p.applyMatrix4(node.object.matrixWorld);
      node.object.getWorldQuaternion(q); a.v.applyQuaternion(q);
    }
    a.initialV.copy(a.v); a.speed = a.v.length(); a.gravity = value(m.gravityModifier, t, r());
    a.uvStart = s.textureSheet ? value(s.textureSheet.startFrame, 0, r()) : 0;
    a.noise.set(0,0,0); a.points = null; a.birth = ++node.emitted;
    particles.push(a); node.count++; return a;
  }
  function write(b, mat, rgba, rect = [1, 1, 0, 0], turn = 0) {
    if (!b || b.count >= capacity) return;
    const i = b.count++; b.mesh.setMatrixAt(i, mat); b.tint.setXYZW(i, ...rgba); b.uv.setXYZW(i, ...rect); b.turn.setX(i, turn);
  }
  function isChild(object, ancestor) {
    for (let p = object.parent; p; p = p.parent) if (p === ancestor) return true;
    return false;
  }
  function tick(frame, context) {
    for (const f of frame.fighters) if (f.state === 'Aiming') lastCharge.set(f.id, {slot: f.slot, tier: f.chargeTier ?? 0});
    for (const i of instances) {
      const f = frame.fighters.find(f => f.id === i.event.entity);
      i.fighter = f; i.context = context ?? i.context;
      if (i.tetherPartner && (!f || f.partner !== i.tetherPartner || frame.fighters.find(f => f.id === i.tetherPartner)?.state !== 'Reeled')) i.dead = true;
      if (i.bound && (!f || !['Attacking', 'Aiming'].includes(f.state) || (i.sequence != null && f.sequence != null && i.sequence !== f.sequence) || (i.slot != null && i.slot !== f.slot))) i.dead = true;
      if (i.slot == null && f && ['Attacking', 'Aiming'].includes(f.state)) i.slot = f.slot;
      const advance = f?.attackTick != null ? Math.max(0, f.attackTick - i.lastAttackTick) : f?.stop ? 0 : 1;
      i.lastAttackTick = f?.attackTick ?? 0;
      i.elapsed += advance;
      i.ticks -= i.bound ? advance : 1;
      if (i.ticks <= 0) i.dead = true;
      const attacking = f && ['Attacking', 'Aiming'].includes(f.state) && (i.slot == null || f.slot === i.slot);
      for (const node of i.nodes) {
        const window = node.spec.behaviours.attackTickWindow;
        if (window && !node.windowDone) {
          const content = i.nodes[window.Content];
          if (!attacking && i.elapsed < window.CancelableUntilTick) { if (content) content.object.visible = false; node.windowDone = true; }
          else if (attacking && i.elapsed >= window.StartTick) {
            if (content && !content.object.visible) { content.object.visible = true; for (const n of i.nodes) if (n === content || isChild(n.object, content.object)) n.startedAt = i.age; }
            if (i.elapsed >= window.CancelableUntilTick) node.windowDone = true;
          }
        }
        const end = node.spec.behaviours.attackEndCue;
        if (end && !node.ended && !attacking) {
          node.ended = true;
          for (const index of end.HideOnEnd ?? []) if (i.nodes[index]) i.nodes[index].object.visible = false;
          const content = i.nodes[end.Content];
          if (content && f) { content.object.visible = true; for (const n of i.nodes) if (n === content || isChild(n.object, content.object)) n.startedAt = i.age; }
        }
      }
    }
  }
  function update(dt) {
    cameraQ.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    for (const b of batches.values()) b.count = 0;
    for (const i of instances) for (const n of i.nodes) if (n.ribbon) n.ribbon.length = 0;
    for (const i of instances) {
      if (i.dead) continue;
      i.age += dt; if (i.released && i.age >= i.releaseAt) { i.dead = true; continue; }
      if (!i.released) transform(i);
      for (const n of i.nodes) {
        let visible = true; for (let o = n.object; o; o = o.parent) visible &&= o.visible;
        if (!visible) continue;
        const trail = n.spec.trail;
        if (n.trailBatch) {
          const points = n.points, life = trail.m_Time;
          while (points.length && i.age - points[0].time > life) points.shift();
          n.object.getWorldPosition(p);
          if (trail.m_Emitting && (!points.length || points.at(-1).p.distanceTo(p) >= trail.m_MinVertexDistance)) {
            if (points.length >= 32) points.shift();
            points.push({p:p.clone(),time:i.age});
          }
          const params = trail.m_Parameters, mat = data.materials[trail.material];
          for (let k = 1; k < points.length; k++) {
            const a = points[k - 1], b = points[k], t = Math.min(1, (i.age - (a.time + b.time) * .5) / life);
            v.subVectors(b.p, a.p); const length = v.length(); if (length < 1e-5) continue;
            v.divideScalar(length); w.copy(Z).applyQuaternion(cameraQ);
            center.crossVectors(w, v).normalize(); w.crossVectors(v, center).normalize();
            matrix.makeBasis(v, center, w); q.setFromRotationMatrix(matrix);
            p.copy(a.p).add(b.p).multiplyScalar(.5);
            scale.set(length, params.widthMultiplier * value(params.widthCurve, t), 1);
            matrix.compose(p, q, scale); color(params.colorGradient, t, .5, rgba2);
            for (let c = 0; c < 4; c++) rgba2[c] *= mat.baseColor[c];
            write(n.trailBatch, matrix, rgba2);
          }
        }
        const s = n.spec.system;
        if (s && (s.main.playOnAwake || n.playing) && !i.released && !i.nodes.some(node => node.emitting === false && (node === n || isChild(n.object, node.object)))) {
          const duration = s.main.duration, now = (i.age - n.startedAt - n.delay) * s.main.simulationSpeed;
          if (now >= 0 && (s.main.looping || n.last < duration)) {
            const end = s.main.looping ? now : Math.min(now, duration), start = Math.max(0, n.last);
            const emission = s.emission;
            if (emission) {
              n.carry += (n.rateScale ?? 1) * value(emission.rateOverTime, end % duration / duration, random(i)) * Math.max(0, end - start);
              n.object.getWorldPosition(p);
              n.carry += value(emission.rateOverDistance, end % duration / duration, random(i)) * p.distanceTo(n.previous);
              n.previous.copy(p);
              let count = Math.floor(n.carry); n.carry -= count;
              for (const burst of emission.m_Bursts ?? []) {
                const first = Math.max(0, Math.floor(Math.max(0, n.last) / duration));
                const last = s.main.looping ? Math.floor(end / duration) : 0;
                for (let cycle = first; cycle <= last; cycle++) {
                  for (let repeat = 0; repeat < (burst.cycleCount || 1); repeat++) {
                    const at = cycle * duration + burst.time + repeat * burst.repeatInterval;
                    if (at > n.last && at <= end && random(i) <= burst.probability) count += Math.round(value(burst.countCurve, at % duration / duration, random(i)));
                  }
                }
              }
              for (let k = 0; k < Math.min(count, s.main.maxParticles); k++) spawn(i, n, end % duration);
            }
            n.last = now;
          }
        }
        if (n.meshBatch && i.ticks > 0 && !i.released) {
          const mat = data.materials[n.spec.mesh.materials[0]];
          write(n.meshBatch, n.object.matrixWorld, mat.baseColor);
        }
      }
    }
    for (let index = particles.length - 1; index >= 0; index--) {
      const a = particles[index], n = a.node, s = n.spec.system, i = a.instance;
      a.age += dt;
      if (a.age >= a.duration || i.dead || i.released && !i.nodes.some(n => n.spec.behaviours.linger && (n === a.node || isChild(a.node.object, n.object)))) {
        n.count--; free.push(a); particles[index] = particles.at(-1); particles.pop(); continue;
      }
      const t = a.age / a.duration, r = a.random, local = s.main.simulationSpace === 'local';
      if (local) {
        n.object.getWorldQuaternion(q).invert();
        a.v.addScaledVector(w.set(0, -9.81 * a.gravity, 0).applyQuaternion(q), dt);
      } else a.v.y -= 9.81 * a.gravity * dt;
      v.copy(a.v);
      const vel = s.velocityOverLifetime;
      if (vel) {
        w.set(value(vel.x, t, r), value(vel.y, t, r), value(vel.z, t, r));
        if (!!vel.inWorldSpace === local) {
          n.object.getWorldQuaternion(q); if (local) q.invert(); w.applyQuaternion(q);
        }
        v.add(w);
        const radial = value(vel.radial, t, r); if (radial) v.add(w.copy(a.p).normalize().multiplyScalar(radial));
        const ox = value(vel.orbitalX, t, r), oy = value(vel.orbitalY, t, r), oz = value(vel.orbitalZ, t, r);
        if (ox || oy || oz) {
          center.set(value(vel.orbitalOffsetX, t, r), value(vel.orbitalOffsetY, t, r), value(vel.orbitalOffsetZ, t, r));
          if (!local) center.applyMatrix4(n.object.matrixWorld);
          q.setFromEuler(euler.set(ox * dt, oy * dt, oz * dt)); a.p.sub(center).applyQuaternion(q).add(center);
        }
        v.multiplyScalar(value(vel.speedModifier, t, r));
      }
      const limit = s.limitVelocity;
      if (limit) {
        const before = v.length();
        const speed = v.length(), maximum = value(limit.magnitude, t, r);
        if (speed > maximum && speed) v.multiplyScalar(Math.max(0, 1 - (speed - maximum) / speed * (1 - (1 - limit.dampen) ** (dt * 60))));
        let drag = value(limit.drag, t, r);
        if (limit.multiplyDragByParticleSize) drag *= a.size.x;
        if (limit.multiplyDragByParticleVelocity) drag *= speed;
        const factor = Math.max(0, 1 - drag * dt); v.multiplyScalar(factor);
        if (before > 0) a.v.multiplyScalar(v.length() / before);
      }
      a.p.addScaledVector(v, dt);
      const noiseSpec = s.noise;
      if (noiseSpec) {
        const frequency = noiseSpec.frequency || 1, scroll = value(noiseSpec.scrollSpeed,t,r)*a.age;
        const np = w.copy(a.p).sub(a.noise).multiplyScalar(frequency);
        const coords = [np.x,np.y+scroll,np.z], delta = [0,0,0];
        for(let axis=0;axis<3;axis++) {
          let amplitude=1,freq=1,sum=0;
          for(let octave=0;octave<noiseSpec.octaves;octave++) { sum+=noise(coords[0]*freq+axis*31.7,coords[1]*freq,coords[2]*freq)*amplitude;amplitude*=noiseSpec.octaveMultiplier;freq*=noiseSpec.octaveScale; }
          if(noiseSpec.remapEnabled) sum=value(noiseSpec[axis===1?'remapY':axis===2?'remapZ':'remap'],(sum+1)*.5,r);
          delta[axis]=sum*value(noiseSpec.separateAxes && axis ? noiseSpec[axis===1?'strengthY':'strengthZ']:noiseSpec.strength,t,r)*(noiseSpec.damping?1/frequency:1)*value(noiseSpec.positionAmount,t,r);
        }
        a.p.sub(a.noise);a.noise.fromArray(delta);a.p.add(a.noise);
      }
      const rotation = s.rotationOverLifetime;
      if (rotation) {
        a.rotation.z += value(rotation.curve, t, r) * dt;
        if (rotation.separateAxes) { a.rotation.x += value(rotation.x, t, r) * dt; a.rotation.y += value(rotation.y, t, r) * dt; }
      }
      p.copy(a.p); if (local) p.applyMatrix4(n.object.matrixWorld);
      const collision = s.collision;
      if (collision && i.context.floorAt) {
        const floor = i.context.floorAt(p.x, p.z, p.y + 2);
        if (floor != null && p.y < floor + a.size.x * collision.radiusScale && v.y < 0) {
          p.y = floor + a.size.x * collision.radiusScale;
          a.p.copy(p); if (local) n.object.worldToLocal(a.p);
          a.v.y = -a.v.y * value(collision.m_Bounce, t, r); a.v.x *= 1 - value(collision.m_Dampen, t, r); a.v.z *= 1 - value(collision.m_Dampen, t, r);
          a.age += a.duration * value(collision.m_EnergyLossOnCollision, t, r);
        }
      }
      scale.copy(a.size);
      const size = s.sizeOverLifetime;
      if (size) scale.multiply(w.set(value(size.curve, t, r), value(size.separateAxes ? size.y : size.curve, t, r), value(size.separateAxes ? size.z : size.curve, t, r)));
      if (s.main.scalingMode !== 2) { n.object.getWorldScale(w); scale.multiply(w); }
      const mode = s.renderer.mode;
      if (mode === 5) continue;
      if (mode === 2) q.setFromEuler(euler.set(Math.PI / 2, 0, 0));
      else if (mode === 3) q.setFromAxisAngle(UP, Math.atan2(camera.position.x - p.x, -camera.position.z - p.z));
      else if (mode === 4 || s.renderer.alignment === 2) n.object.getWorldQuaternion(q);
      else if (s.renderer.alignment === 1) q.identity();
      else q.copy(cameraQ);
      if (mode === 1) {
        w.copy(v); if (local) { n.object.getWorldQuaternion(spin); w.applyQuaternion(spin); }
        const speed = w.length();
        if (speed > .0001) {
          const forward = v.copy(Z).applyQuaternion(cameraQ);
          const right = center.crossVectors(w, forward).normalize();
          const up = w.normalize(); const normal = v.crossVectors(right, up).normalize();
          matrix.makeBasis(right, up, normal); q.setFromRotationMatrix(matrix);
        }
        scale.y = scale.y * s.renderer.lengthScale + s.renderer.velocityScale * speed;
        p.addScaledVector(w, -scale.y * .5);
      }
      spin.setFromEuler(euler.set(a.rotation.x, a.rotation.y, a.rotation.z)); q.multiply(spin);
      const pivot = s.renderer.pivot;
      // The world reflection preserves billboard right/up axes and reverses its normal.
      p.add(w.set(pivot.x * scale.x, pivot.y * scale.y, (mode === 0 && s.renderer.alignment === 0 ? -pivot.z : pivot.z) * scale.z).applyQuaternion(q));
      matrix.compose(p, q, scale);
      const mat = data.materials[s.material]; color(s.colorOverLifetime?.gradient, t, r, rgba);
      for (let k = 0; k < 4; k++) rgba[k] *= a.startColor[k] * mat.baseColor[k];
      let rect = [mat.uvScale[0], mat.uvScale[1], mat.uvOffset[0], mat.uvOffset[1]];
      const sheet = s.textureSheet;
      if (sheet) {
        const total = sheet.tilesX * sheet.tilesY;
        const f = sheet.timeMode === 2 ? Math.floor(a.age * sheet.fps + a.uvStart * total)
          : Math.floor((value(sheet.frameOverTime, t, r) * sheet.cycles + a.uvStart) * total);
        const frame = ((f % total) + total) % total;
        rect = [rect[0] / sheet.tilesX, rect[1] / sheet.tilesY, (frame % sheet.tilesX) / sheet.tilesX + rect[2], 1 - (Math.floor(frame / sheet.tilesX) + 1) / sheet.tilesY + rect[3]];
      }
      write(n.batch, matrix, rgba, rect, mode === 1 ? 1 : 0);
      if (n.ribbon && r <= s.trails.ratio) n.ribbon.push({p:p.clone(),rgba:rgba.slice(),size:scale.x,birth:a.birth,age:t});
    }
    for (const i of instances) for (const n of i.nodes) if (n.ribbon?.length > 1) {
      const points=n.ribbon.sort((a,b)=>a.birth-b.birth),spec=n.spec.system.trails;
      const material=data.materials[n.spec.system.trailMaterial ?? n.spec.system.material];
      for(let k=1;k<Math.min(32,points.length);k++) {
        const a=points[k-1],b=points[k],t=k/(points.length-1);
        v.subVectors(b.p,a.p);const length=v.length();if(length<1e-5)continue;v.divideScalar(length);
        w.copy(Z).applyQuaternion(cameraQ);center.crossVectors(w,v).normalize();w.crossVectors(v,center).normalize();
        matrix.makeBasis(v,center,w);q.setFromRotationMatrix(matrix);p.copy(a.p).add(b.p).multiplyScalar(.5);
        scale.set(length,value(spec.widthOverTrail,t,.5)*(spec.sizeAffectsWidth?b.size:1),1);matrix.compose(p,q,scale);
        color(spec.colorOverTrail,t,.5,rgba);color(spec.colorOverLifetime,b.age,.5,rgba2);
        for(let c=0;c<4;c++)rgba[c]*=rgba2[c]*(spec.inheritParticleColor?b.rgba[c]:material.baseColor[c]);
        write(n.ribbonBatch,matrix,rgba);
      }
    }
    for (let index = instances.length - 1; index >= 0; index--) {
      const i = instances[index];
      if (i.dead || !i.hasMesh && !i.event.projectile && i.nodes.every(n => !n.count && (!n.spec.system || !n.spec.system.main.looping && i.age > n.delay + n.spec.system.main.duration)) && i.age > .1) instances.splice(index, 1);
    }
    for (const b of batches.values()) {
      b.mesh.count = b.count;
      if (b.count) for (const attribute of [b.mesh.instanceMatrix, b.tint, b.uv, b.turn]) {
        attribute.clearUpdateRanges(); attribute.addUpdateRange(0, b.count * attribute.itemSize); attribute.needsUpdate = true;
      }
    }
  }
  function clear() {
    for (const a of particles) free.push(a);
    particles.length = 0; instances.length = 0; lastCharge.clear();
    for (const b of batches.values()) b.mesh.count = b.count = 0;
  }
  function release(instance) {
    instance.released = true;
    let seconds = 0;
    for (const n of instance.nodes) seconds = Math.max(seconds, n.spec.behaviours.linger?.Seconds ?? 0);
    instance.releaseAt = instance.age + seconds;
    if (!seconds) instance.dead = true;
  }
  return { play, tick, update, clear, release, get stats() { return { particles: particles.length, instances: instances.length, batches: [...batches.values()].filter(b => b.count).length, dropped, capacity }; } };
}
