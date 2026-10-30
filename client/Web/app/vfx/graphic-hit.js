import * as THREE from 'three';

const TAU = Math.PI * 2;
const PALE = [1, .98, .88, .92];
const TIERS = {
  Light: { color: [1, .96, .72, .95], rings: [[.28, .055, 12]], rays: [4, .12, .48, .07, .25], streak: [0, .58, .11, .025], fade: .08, end: 1.15 },
  Medium: { color: [1, .76, .24, .98], rings: [[.4, .075, 16]], rays: [6, .18, .76, .09, .16], streak: [.12, .94, .17, .035], fade: .10, end: 1.4 },
  Heavy: { color: [1, .33, .08, 1], rings: [[.52, .095, 18], [.24, .12, 12]], rays: [9, .2, 1.05, .12, .1], streak: [.18, 1.35, .24, .045], fade: .13, end: 1.7 },
  Launch: { color: [1, .18, .08, 1], rings: [[.62, .11, 20], [.31, .08, 14]], rays: [11, .25, 1.25, .12, .08], streak: [.22, 1.8, .28, .035], fade: .16, end: 2.05 },
  Block: { color: [.25, .8, 1, .9], rings: [[.4, .09, 18], [.2, .04, 14]], fade: .12, end: 1.35 },
};
export const classifyImpact = (damage, force) => force >= 12 ? 'Launch' : damage >= 11 ? 'Heavy' : damage >= 6 ? 'Medium' : 'Light';

export function impactMesh(tier, launch = [1, 0]) {
  const spec = TIERS[tier], position = [], color = [], index = [];
  function ray(a, b, sw, ew, c) {
    const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.hypot(dx, dy);
    if (len * len < .0001) return;
    const nx = -dy / len, ny = dx / len, first = position.length / 3;
    position.push(a[0] + nx * sw, a[1] + ny * sw, 0, a[0] - nx * sw, a[1] - ny * sw, 0,
      b[0] - nx * ew, b[1] - ny * ew, 0, b[0] + nx * ew, b[1] + ny * ew, 0);
    for (let k = 0; k < 4; k++) color.push(...c);
    index.push(first, first + 1, first + 2, first, first + 2, first + 3);
  }
  spec.rings.forEach(([r, w, count], ring) => {
    for (let i = 0; i < count; i++) {
      const a = i * TAU / count, b = (i + 1) * TAU / count;
      ray([Math.cos(a) * r, Math.sin(a) * r], [Math.cos(b) * r, Math.sin(b) * r], w, w, ring ? PALE : spec.color);
    }
  });
  if (spec.rays) {
    const [count, start, end, width, phase] = spec.rays;
    for (let i = 0; i < count; i++) {
      const a = i * TAU / count + phase, x = Math.cos(a), y = Math.sin(a), r = end * (i % 2 ? .76 : 1);
      ray([x * start, y * start], [x * r, y * r], width, width * .18, spec.color);
    }
  }
  const [x, y] = launch;
  if (spec.streak) {
    const [start, end, sw, ew] = spec.streak;
    ray([x * start, y * start], [-x * end, -y * end], sw, ew, PALE);
  }
  if (tier === 'Launch') for (const sign of [-1, 1])
    ray([-y * .16 * sign, x * .16 * sign], [-x * 1.35 - y * .35 * sign, -y * 1.35 + x * .35 * sign], .13, .025, spec.color);
  return { position, color, index };
}

export function createGraphicHits({ world, camera }) {
  const pool = [], right = new THREE.Vector3(), up = new THREE.Vector3(), q = new THREE.Quaternion();
  const material = new THREE.ShaderMaterial({ transparent: true, depthWrite: false, side: THREE.DoubleSide,
    uniforms: { opacity: { value: 1 } },
    vertexShader: `attribute vec4 color; varying vec4 tint; void main(){tint=color;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
    fragmentShader: `uniform float opacity; varying vec4 tint; void main(){gl_FragColor=tint*vec4(1.,1.,1.,opacity);}`,
  });
  function build(s) {
    right.set(1, 0, 0).applyQuaternion(q); up.set(0, 1, 0).applyQuaternion(q);
    let x = s.launch.dot(right), y = s.launch.dot(up), length = Math.hypot(x, y);
    if (length < Math.sqrt(.001)) { x = 1; y = 0; length = 1; }
    x /= length; y /= length;
    if (s.direction && Math.hypot(x - s.direction[0], y - s.direction[1]) <= .001) return;
    s.direction = [x, y];
    const data = impactMesh(s.tier, s.direction), g = s.mesh.geometry;
    for (const [name, values] of [['position', data.position], ['color', data.color]]) {
      const attribute = g.getAttribute(name); attribute.array.set(values); attribute.clearUpdateRanges(); attribute.addUpdateRange(0, values.length); attribute.needsUpdate = true;
    }
    g.index.array.set(data.index); g.index.needsUpdate = true; g.setDrawRange(0, data.index.length);
  }
  function spawn(position, tier, stop = 0, launch = [0, 0, 1], normal = null) {
    let s = pool.find(s => !s.mesh.visible);
    if (!s) {
      if (pool.length >= 64) return;
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(192 * 3), 3));
      geometry.setAttribute('color', new THREE.Float32BufferAttribute(new Float32Array(192 * 4), 4));
      geometry.setIndex(new THREE.Uint16BufferAttribute(new Uint16Array(288), 1));
      const mesh = new THREE.Mesh(geometry, material.clone());
      mesh.name = 'Graphic Hit'; mesh.frustumCulled = false; mesh.visible = false; world.add(mesh);
      s = { mesh, launch: new THREE.Vector3() }; pool.push(s);
    }
    s.tier = tier; s.age = 0; s.hold = stop / 60; s.direction = null; s.launch.fromArray(launch).normalize();
    s.mesh.position.fromArray(position); s.mesh.scale.setScalar(.72); s.mesh.visible = true;
    q.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    if (tier === 'Block' && normal?.lengthSq() > .000001) {
      const up = Math.abs(normal.y) > .95 ? new THREE.Vector3(0, 0, 1) : new THREE.Vector3(0, 1, 0);
      s.mesh.quaternion.setFromRotationMatrix(new THREE.Matrix4().lookAt(normal, new THREE.Vector3(), up));
    } else s.mesh.quaternion.copy(q);
    s.mesh.material.uniforms.opacity.value = 1; build(s);
  }
  function update(dt) {
    q.set(-camera.quaternion.x, -camera.quaternion.y, camera.quaternion.z, camera.quaternion.w);
    for (const s of pool) {
      if (!s.mesh.visible) continue;
      s.age += dt;
      if (s.tier !== 'Block') { s.mesh.quaternion.copy(q); build(s); }
      const spec = TIERS[s.tier];
      if (s.age <= s.hold) { s.mesh.scale.setScalar(.72 + .28 * (s.hold > 0 ? s.age / s.hold : 1)); continue; }
      const t = Math.min(1, (s.age - s.hold) / spec.fade);
      s.mesh.scale.setScalar(1 + (spec.end - 1) * (1 - (1 - t) ** 3));
      s.mesh.material.uniforms.opacity.value = 1 - t;
      if (t >= 1) s.mesh.visible = false;
    }
  }
  return { spawn, update, clear: () => pool.forEach(s => { s.mesh.visible = false; }),
    get stats() { return { active: pool.filter(s => s.mesh.visible).length, pool: pool.length }; } };
}
