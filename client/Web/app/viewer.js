import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

// Look-dev lineup of every exported fighter at one clip frame, lit like the game:
//   viewer.html?anim=idle&frame=0&exposure=1   (anim matches the clip name's last part, or the full semantic id)
const q = new URLSearchParams(location.search);
const PKGS = (q.get('pkgs') ?? 'fightguy,manki,wibou,bonk,bashie,salamander,nyx').split(',');
const ROSTER = { fightguy: [1, 0], manki: [1, -0.2], wibou: [0.85, 0.034], bonk: [3.3, 0.24], bashie: [1.1, -0.75], salamander: [1.8, -0.85], nyx: [1.0375, -0.85] };
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.setSize(innerWidth, innerHeight);
renderer.toneMapping = q.get('tone') === 'aces' ? THREE.ACESFilmicToneMapping : THREE.NoToneMapping;
renderer.toneMappingExposure = Number(q.get('exposure') ?? 1);
document.body.append(renderer.domElement);
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x2a2340);
scene.add(new THREE.HemisphereLight(0xfff1dc, 0x2a2340, Number(q.get('hemi') ?? 1.4)));
const sun = new THREE.DirectionalLight(0xffffff, Number(q.get('sun') ?? 2.2));
sun.position.set(-4, 10, 8);
scene.add(sun);
const camera = new THREE.PerspectiveCamera(30, innerWidth / innerHeight, 0.1, 100);
camera.position.set(0, 1.6, 15);
camera.lookAt(0, 1.1, 0);
const loader = new GLTFLoader();
const anim = q.get('anim') ?? 'idle';
const frame = Number(q.get('frame') ?? 0);
const yaw = Number(q.get('yaw') ?? 0.5);
const results = await Promise.all(PKGS.map(p => loader.loadAsync(`../assets/fighters/${p}.glb`).catch(() => null)));
results.forEach((gltf, i) => {
  if (!gltf) return;
  const [scale, yOffset] = ROSTER[PKGS[i]] ?? [1, 0];
  const root = new THREE.Group();
  root.position.set((i - (PKGS.length - 1) / 2) * 2.4, 0.85 + yOffset, 0);
  root.rotation.y = Math.PI - yaw;
  root.scale.setScalar(scale);
  gltf.scene.scale.x = -1;
  root.add(gltf.scene);
  scene.add(root);
  const tint = q.get('tint') ?? 'linear';
  gltf.scene.traverse(o => {
    if (/^weapon:/.test(o.name)) o.visible = false;
    if (!o.isMesh) return;
    o.frustumCulled = false;
    if (tint === 'none') o.material.color.setRGB(1, 1, 1);
    if (tint === 'srgb') o.material.color.convertLinearToSRGB();
  });
  const forms = gltf.userData?.forms;
  if (forms) for (const name of [...(forms[1] ?? []), ...(forms.hidden ?? [])]) { const o = gltf.scene.getObjectByName(name); if (o) o.visible = false; }
  const clip = gltf.animations.find(c => c.name === anim) ?? gltf.animations.find(c => c.name.endsWith('.' + anim)) ?? gltf.animations[0];
  const mixer = new THREE.AnimationMixer(gltf.scene);
  const action = mixer.clipAction(clip).play();
  action.time = Math.min(frame / 60, clip.duration);
  mixer.update(0);
});
renderer.render(scene, camera);
document.getElementById('label').textContent = `${anim} frame ${frame} · ${location.search}`;
document.title = 'ready';
