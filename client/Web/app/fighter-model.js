import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { seatRing, shieldBubble, createFlash } from './fighters.js';
import { SEMANTIC_BONES } from './geometry.js';

// Real fighter models exported from Unity by client/Web/tools/fighter_glb.py. Each clip is named by its semantic id
// and keyed per 60 Hz clip frame, so the view shows exactly the animation frame the sim reports for the fighter.

const FPS = 60;
const BLEND_SECONDS = 0.1;
const loader = new GLTFLoader();
const cache = new Map();

export function loadFighterModel(pkg, assets) {
  if (!cache.has(pkg)) cache.set(pkg, loader.loadAsync(`${assets}fighters/${pkg}.glb`).catch(() => null));
  return cache.get(pkg);
}

export class ModelFighter {
  constructor({ world, info, gltf, seatColor }) {
    this.world = world;
    this.info = info;
    this.root = new THREE.Group();
    this.model = SkeletonUtils.clone(gltf.scene);
    // The glb is Unity space mirrored in x; the world group holds Unity (sim) space, so mirror back.
    this.model.scale.x = -1;
    this.root.add(this.model);
    this.mats = [];
    this.bones = {};
    this.weapons = [];
    this.model.traverse(o => {
      if (o.isMesh) {
        o.castShadow = true;
        o.frustumCulled = false;
        o.material = o.material.clone();
        this.mats.push(o.material);
      }
      if (o.isBone || o.type === 'Object3D') this.bones[o.name] ??= o;
      const weapon = /^weapon:(\d+):/.exec(o.name);
      if (weapon) this.weapons.push({ node: o, slot: Number(weapon[1]) });
    });
    this.semantic = Object.fromEntries(Object.entries(SEMANTIC_BONES)
      .map(([id, suffix]) => [id, Object.values(this.bones).find(b => b.name.endsWith(suffix))]));
    // Two-form fighters (Nyx) list each form's parts by node name; the sim reports the active form.
    const forms = gltf.userData?.forms;
    this.formParts = forms ? [0, 1].map(k => (forms[k] ?? []).map(name => this.model.getObjectByName(name)).filter(Boolean)) : null;
    for (const name of forms?.hidden ?? []) { const o = this.model.getObjectByName(name); if (o) o.visible = false; }
    this.flash = createFlash(this.mats);
    this.mixer = new THREE.AnimationMixer(this.model);
    this.actions = new Map(gltf.animations.map(clip => [clip.name, this.mixer.clipAction(clip)]));
    this.current = null;
    this.previous = null;
    this.blend = 1;
    this.ring = seatRing(seatColor);
    this.shield = shieldBubble(seatColor);
    this.root.add(this.shield);
    world.add(this.root, this.ring);
  }

  play(anim, frame, dt) {
    const action = this.actions.get(anim);
    if (action && action !== this.current?.action) {
      if (this.previous) this.previous.action.stop();
      this.previous = this.current;
      this.current = { action, frame };
      action.reset().play();
      this.blend = this.previous ? 0 : 1;
    }
    if (!this.current) return;
    this.current.frame = frame;
    this.blend = Math.min(1, this.blend + dt / BLEND_SECONDS);
    const clipTime = (a, f) => Math.min(f / FPS, a.getClip().duration);
    this.current.action.time = clipTime(this.current.action, frame);
    this.current.action.setEffectiveWeight(this.blend);
    if (this.previous) {
      this.previous.action.time = clipTime(this.previous.action, this.previous.frame);
      this.previous.action.setEffectiveWeight(1 - this.blend);
      if (this.blend >= 1) { this.previous.action.stop(); this.previous = null; }
    }
    this.mixer.update(0);
  }

  update(f, out, now) {
    const dt = this.lastNow ? Math.min((now - this.lastNow) / 1000, 0.1) : 0;
    this.lastNow = now;
    this.root.visible = this.ring.visible = !out;
    if (out) return;
    const jitter = f.stop > 0 ? (Math.random() - 0.5) * 0.05 : 0;
    this.root.position.set(f.p[0] + jitter, f.p[1] + this.info.modelYOffset, f.p[2]);
    this.root.rotation.y = f.yaw;
    this.root.scale.setScalar(this.info.visualScale);
    this.play(f.anim, f.frame, dt);
    if (this.formParts) this.formParts.forEach((parts, form) => parts.forEach(o => { o.visible = form === (f.form === 1 ? 1 : 0); }));
    const attacking = f.state === 'Attacking' || f.state === 'Aiming';
    for (const w of this.weapons) w.node.visible = w.slot === 0 || (attacking && w.slot === f.slot);
    this.ring.position.set(f.p[0], f.p[1] - this.info.half + 0.03, f.p[2]);
    const shielding = /Shield/.test(f.state);
    this.shield.visible = shielding;
    if (shielding) {
      this.shield.position.set(0, -this.info.modelYOffset / this.info.visualScale, 0);
      this.shield.scale.setScalar(this.info.half * 1.15 / this.info.visualScale);
    }
    this.flash(f, now);
  }

  boneWorld(boneId) {
    const bone = this.semantic[boneId];
    if (!bone) return null;
    this.root.updateMatrixWorld(true);
    return this.world.worldToLocal(bone.getWorldPosition(new THREE.Vector3()));
  }

  dispose() {
    this.world.remove(this.root, this.ring);
    this.mixer.stopAllAction();
  }
}
