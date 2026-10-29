import * as THREE from 'three';
import { UP, unitSphere, unitCylinder, placeSegment, placeBall, SEMANTIC_BONES } from './geometry.js';

// A fighter view draws one fighter from its frame entry. Every view exposes:
//   update(f, out, now)   pose from the frame; out hides an eliminated fighter
//   boneWorld(boneId)     sim-space position of a semantic bone ('bone.right-hand'), or null
//   dispose()

// Limbless capsule fighter posed from the cooked hurtbox skeleton (poses.bin).
export class Puppet {
  constructor({ world, info, seatColor, bodyColor }) {
    const bones = info.bones;
    const idx = name => bones.findIndex(b => b.endsWith(name));
    this.b = {
      head: idx(':Head'), chest: idx(':Spine2'), hips: idx(':Hips'),
      rh: idx(':RightHand'), lh: idx(':LeftHand'), rf: idx(':RightFoot'), lf: idx(':LeftFoot'),
      rt: idx(':RightToeBase'), lt: idx(':LeftToeBase'), tip: idx('_weapon_tip'), hilt: idx('_weapon_hilt'),
    };
    this.semantic = Object.fromEntries(Object.entries(SEMANTIC_BONES).map(([id, suffix]) => [id, idx(suffix)]));
    const body = new THREE.MeshStandardMaterial({ color: bodyColor, roughness: 0.5 });
    const dark = new THREE.MeshStandardMaterial({ color: new THREE.Color(bodyColor).multiplyScalar(0.55), roughness: 0.6 });
    const white = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.3 });
    const black = new THREE.MeshBasicMaterial({ color: 0x111111 });
    this.mats = [body, dark];
    this.world = world;
    this.group = new THREE.Group();
    const m = (geo, mat) => { const x = new THREE.Mesh(geo, mat); x.castShadow = true; this.group.add(x); return x; };
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
    this.ring = seatRing(seatColor);
    this.group.add(this.ring);
    this.shield = shieldBubble(seatColor);
    this.group.add(this.shield);
    this.flash = createFlash(this.mats);
    this.L = 0;
    this.half = info.half;
    this.frame = null;
    world.add(this.group);
  }
  pt(f, i) {
    if (i < 0 || f.pts[i * 3] == null) return null;
    return new THREE.Vector3(f.pts[i * 3], f.pts[i * 3 + 1], f.pts[i * 3 + 2]);
  }
  boneWorld(boneId) {
    const i = this.semantic[boneId];
    return this.frame && i != null ? this.pt(this.frame, i) : null;
  }
  update(f, out, now) {
    this.frame = f;
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
    this.flash(f, now);
  }
  dispose() { this.world.remove(this.group); }
}

export function seatRing(color) {
  const ring = new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.05, 8, 32), new THREE.MeshBasicMaterial({ color }));
  ring.rotation.x = Math.PI / 2;
  return ring;
}

export function shieldBubble(color) {
  return new THREE.Mesh(unitSphere, new THREE.MeshStandardMaterial({
    color, transparent: true, opacity: 0.28, emissive: color, emissiveIntensity: 0.6, depthWrite: false }));
}

// Unity's FighterHitFeel: two white frames scaled by hit tier, then a four-frame fading rim pulse. A hit starts it
// when the fighter's hitstop begins. Respawn invincibility is a faint glow, never transparency.
const FLASH_WHITE = [0.55, 0.7, 0.85, 0.9];
export function createFlash(mats) {
  let start = -Infinity, tier = 0, lastStop = 0;
  return (f, now) => {
    if (f.stop > 0 && lastStop === 0) {
      start = now;
      tier = f.stop >= 12 ? 3 : f.stop >= 8 ? 2 : f.stop >= 4 ? 1 : 0;
    }
    lastStop = f.stop;
    const frames = (now - start) * 0.06;
    const white = frames < 2 ? FLASH_WHITE[tier] : 0;
    const rim = frames >= 2 && frames < 6 ? (1 - (frames - 2) / 4) * 0.5 : 0;
    const glow = f.inv > 0 ? 0.12 + 0.06 * Math.sin(now * 0.012) : 0;
    const amount = Math.max(white, rim, glow);
    for (const mat of mats) {
      mat.emissive?.setRGB(1, white > 0 || glow >= rim ? 1 : 0.85, white > 0 || glow >= rim ? 1 : 0.6);
      mat.emissiveIntensity = amount;
    }
  };
}
