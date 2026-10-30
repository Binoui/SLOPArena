import * as THREE from 'three';
import { SEATS } from '../seat-colours.js';
import { floorSampler } from './floor.js';
import { toThree } from '../geometry.js';
import { loadStageArt } from '../stage-art.js';
import { flashVictims } from '../fighters.js';

export async function createReview({ world, scene, renderer, camera, vfx, Demo, PATHS, fighterInfo, loadModels, createFighterView, applyEnvironment }) {
  for (const el of document.body.children) if (el.tagName !== 'CANVAS') el.style.display = 'none';
  for (const id of ['start', 'menu', 'hud', 'banner', 'audioToggle', 'status']) document.getElementById(id)?.classList.add('hidden');
  for (const e of document.querySelectorAll('main,header,.start-screen')) e.style.display = 'none';
  const arena = JSON.parse(Demo.LoadArena(new Uint8Array(await (await fetch(PATHS.arenas + 'splash_deck.arena')).arrayBuffer())));
  arena.floorAt = floorSampler(Demo.ArenaTriangles());
  const art = await loadStageArt('splash_deck', { paths: PATHS });
  if (art) { world.add(art); applyEnvironment(art.userData.environment); }
  await loadModels(Object.keys(fighterInfo));
  let views = [], frame, press = 40, captured = -1;
  const hits = [], events = [], deaths = [];
  const context = () => ({ infoOf: pkg => fighterInfo[pkg], viewOf: id => views[id - 1], arena, floorAt: arena.floorAt, fighterOf: id => frame.fighters.find(f => f.id === id), seatOf: id => id - 1, seatColor: id => SEATS[id] ?? 0xffffff,
    boneFrame: (id, bone) => views[id - 1]?.boneFrame?.(bone) ?? null,
    boneWorld: (id, bone) => views[id - 1]?.boneWorld(bone) ?? null });
  let focus, distance;
  function drawCamera() {
    camera.fov = 30;
    const a = frame.fighters[0], b = frame.fighters[1];
    const forward = new THREE.Vector3(Math.sin(a.yaw), 0, Math.cos(a.yaw));
    const side = new THREE.Vector3().crossVectors(new THREE.Vector3(0, 1, 0), forward);
    const target = focus;
    const pos = target.clone().addScaledVector(side, distance).add(new THREE.Vector3(0, distance * .08, 0));
    camera.position.copy(toThree(...pos.toArray())); camera.lookAt(toThree(...target.toArray()));
    camera.updateProjectionMatrix(); camera.updateMatrixWorld();
  }
  function render() { renderer.render(scene, camera); }
  function tick() {
    frame = JSON.parse(Demo.ReviewStep());
    for (const h of frame.hits) hits.push({ frame: frame.tick - press, hit: h });
    for (const d of frame.deaths) deaths.push({frame:frame.tick - press,death:d});
    for (const e of frame.events) events.push({ frame: frame.tick - press, id: e.id, source:e.source, bone: e.bone, p: e.p });
    flashVictims(frame, id => views[id - 1], frame.tick * 1000 / 60);
    frame.fighters.forEach((f, i) => views[i].update(f, false, frame.tick * 1000 / 60));
    vfx.onFrame(frame, context()); vfx.update(1 / 60);
    captured = frame.tick - press;
  }
  return {
    async start(pkg, move, options = {}) {
      for (const v of views) v.dispose();
      views = [pkg, pkg === 'bonk' ? 'bashie' : 'bonk'].map((p, i) => createFighterView(p, i, arena.floorAt));
      hits.length = 0; events.length = 0; deaths.length = 0;
      const slot = { 1: 3, 3: 8, 4: 9, A: 11, E: 4, R: 5, F: 6 }[move];
      const spacing = options.distance ?? (move === 'A' ? 4 : move === 'R' ? 1.5 : 1.3);
      frame = JSON.parse(Demo.ReviewStart(pkg, pkg === 'bonk' ? 'bashie' : 'bonk', slot, spacing, options.damage ?? (['4', 'F'].includes(move) ? 60 : 40), options.hold ?? 0, options.shield ? 1 : 0));
      vfx.onFrame({ ...frame, tick: -1 }, context());
      for (let i = 0; i < press; i++) tick();
      const [a, b] = frame.fighters, height = Math.max(fighterInfo[pkg].half, fighterInfo[pkg === 'bonk' ? 'bashie' : 'bonk'].half) * 2 + .3;
      const floor = Math.min(a.p[1] - fighterInfo[a.pkg].half, b.p[1] - fighterInfo[b.pkg].half);
      const gap = Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]);
      const tangent = Math.tan(Math.PI / 12);
      distance = Math.max(height / (.8 * 2 * tangent), (gap + 1.8) / (2 * tangent * camera.aspect), 3.2);
      focus = new THREE.Vector3((a.p[0] + b.p[0]) * .5 + Math.sin(a.yaw) * .3, floor + height * .5, (a.p[2] + b.p[2]) * .5 + Math.cos(a.yaw) * .3);
      drawCamera(); render();
      return frame;
    },
    seek(t) { while (captured < t) tick(); render(); return { captured, hits, events, deaths, stats: vfx.stats, fighters: frame.fighters }; },
    shared(kind, age = .1) {
      const f = frame.fighters[1], p = f.p;
      const extra = kind === 'KO' ? {deaths:[[f.id,...p]]} : {hits:[[f.id,1,kind==='Light'?4:kind==='Medium'?6:11,0,...p,8,3,18,0,kind==='Launch'?12:0,1,0,35,...p]]};
      vfx.onFrame({...frame,events:[],hits:[],deaths:[],...extra},context());
      for(let t=0;t<age;t+=1/60)vfx.update(1/60); render(); return vfx.stats;
    },
    debug() {
      const draws = [];
      world.traverse(m => { if (m.isInstancedMesh && m.name.startsWith('Shuriken') && m.count) {
        const mat = new THREE.Matrix4(); m.getMatrixAt(0, mat);
        draws.push({name:m.name,count:m.count,p:new THREE.Vector3().setFromMatrixPosition(mat).toArray()});
      } });
      return { draws, bone: views[0].boneWorld('bone.hips')?.toArray() };
    },
    render,
  };
}
