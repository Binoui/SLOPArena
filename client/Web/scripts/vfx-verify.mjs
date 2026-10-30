// Browser checks + real four-fighter Hard Brawl or Slopball captures, on VFX's reserved Chrome port.
// Usage: node client/Web/scripts/vfx-verify.mjs [brawl|slopball|kit] [output-directory]
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const mode = process.argv[2] ?? 'brawl';
const out = process.argv[3] ?? `/tmp/vfx-${mode}-verified`;
if (!['brawl', 'slopball', 'kit'].includes(mode)) throw new Error('Expected brawl, slopball or kit');
async function browserChecks() {
  const THREE = await import('three');
  const { createVfx } = await import('./vfx.js');
  const { EFFECTS, PROJECTILES } = await import('./vfx/shuriken.js');
  const { classifyImpact, impactMesh } = await import('./vfx/graphic-hit.js');
  const assert = (ok, message) => { if (!ok) throw new Error(`VFX CHECK FAILED: ${message}`); };
  const world = new THREE.Group(); world.scale.z = -1;
  const camera = new THREE.PerspectiveCamera(45, 16 / 9, .1, 1000);
  camera.position.set(10,15,20); camera.lookAt(0,0,0); camera.updateMatrixWorld();
  const fx = createVfx({world,camera,legacy:false});
  const fighters = ['fightguy','manki','wibou','bonk','bashie','nyx','salamander'].map((pkg,i)=>({id:i+1,pkg,yaw:0,p:[0,1,0],state:'Attacking',slot:3,sequence:1,attackTick:0,anim:'',grounded:1}));
  const bone = new THREE.Vector3(1,2,3);
  const ctx = {seatOf:id=>id-1,seatColor:()=>0x14b8a6,infoOf:()=>({half:.85,shieldRadius:.98}),
    boneWorld:()=>bone,boneFrame:()=>new THREE.Matrix4().makeTranslation(...bone.toArray()),fighterOf:id=>fighters.find(f=>f.id===id),floorAt:()=>0};
  let tick=0;
  const frame = extra => ({tick:++tick,fighters,hits:[],events:[],deaths:[],hitboxes:[],...extra});
  const reset = () => {fx.onFrame(frame({tick:-1}),ctx);fx.update(0);};
  const event = (id,extra={}) => ({id,entity:1,p:[0,1,0],yaw:0,local:[0,0,0],scale:[1,1,1],ticks:60,sequence:1,...extra});
  const finite = label => world.traverse(m=>{
    if(m.isInstancedMesh){
      assert(Array.from(m.instanceMatrix.array.subarray(0,m.count*16)).every(Number.isFinite),`${label} finite matrices`);
      const tint=m.geometry.getAttribute('particleColor');if(tint)assert(Array.from(tint.array.subarray(0,m.count*4)).every(Number.isFinite),`${label} finite colors`);
      if(m.name.startsWith('Shuriken')&&m.count&&m.material.userData.hasMap)assert(m.material.uniforms.map.value.image.width>1,`${label} painted texture ready on the first frame`);
    }
  });
  assert(classifyImpact(0,12)==='Launch'&&classifyImpact(11,0)==='Heavy'&&classifyImpact(6,0)==='Medium'&&classifyImpact(5,0)==='Light','Unity impact classification boundaries');
  assert(impactMesh('Launch').index.length>impactMesh('Light').index.length,'launch has its extra directional rays');
  const visible = {};
  for(const [id,effect] of Object.entries(EFFECTS)){
    reset();const pkg=id.split('.')[1],f=fighters.find(f=>f.pkg===pkg);
    fx.onFrame(frame({events:[event(id,{entity:f.id,source:'Timeline'})]}),ctx);
    for(let k=0;k<40;k++){
      f.attackTick=k;fx.onFrame(frame(),ctx);fx.update(1/60);finite(id);
      world.traverse(m=>{if(m.isInstancedMesh&&m.name.startsWith('Shuriken')&&m.count)visible[pkg]=(visible[pkg]??0)+m.count;});
    }
  }
  assert(['fightguy','manki','wibou','bonk','bashie','nyx'].every(p=>visible[p]>0),'all six catalogued VFX kits draw');
  reset();
  const slots={'1':3,'3':8,'4':9,A:11,E:4,R:5,F:6};
  const projectileRows=Object.keys(PROJECTILES).map((key,i)=>{const [pkg,air,slot]=key.split('.');return {owner:fighters.find(f=>f.pkg===pkg).id,slot:slots[slot],air:air==='air'?1:0,emit:i+1,a:[i*.3,1,0],b:[0,0,0],r:.35,v:[0,0,8],age:1,projectile:1};});
  fx.onFrame(frame({hitboxes:projectileRows}),ctx);fx.update(1/60);finite('projectiles');
  assert(fx.stats.projectiles===14,'14 exported projectile bindings play');
  fx.onFrame(frame({hitboxes:projectileRows.map(h=>({...h,v:[0,0,0]}))}),ctx);fx.update(1/60);
  assert(fx.stats.projectiles===14,'stationary projectiles survive');
  const fallback={...projectileRows[0],owner:7,emit:999};
  fx.onFrame(frame({hitboxes:[...projectileRows,fallback]}),ctx);fx.update(1/60);finite('unbound projectile fallback');
  assert(fx.stats.projectiles===15,'unbound Salamander projectile uses Unity default sphere and trail');
  fx.onFrame(frame(),ctx);fx.update(1/60);assert(fx.stats.projectiles===0,'projectile removals release graphs');
  reset();
  const own=frame({hits:[[1,2,12,0,0,1,0,8,3,18,1,13]]});fx.onFrame(own,ctx);fx.update(1/60);
  assert(fx.stats.graphicHits.active===0,'authored hit presentation suppresses generic graphic');
  fx.onFrame(frame({hits:[[1,2,12,1,0,1,0,8,3,18,0,13]]}),ctx);fx.update(1/60);
  assert(fx.stats.graphicHits.active===0,'blocked hit row produces no damaging graphic');
  fx.onFrame(frame({events:[event('',{source:'BlockContact',entity:1,p:[.8,1,0]})]}),ctx);fx.update(1/60);
  assert(fx.stats.graphicHits.active===1,'semantic block event produces one ripple');
  for(const [damage,force] of [[4,0],[6,0],[11,0],[4,12]]){fx.onFrame(frame({hits:[[1,2,damage,0,0,1,0,8,3,18,0,force,1,0,35,0,1,0]]}),ctx);fx.update(1/60);}
  assert(fx.stats.graphicHits.active===5,'all four impact tiers plus block are active');
  fx.onFrame(frame({deaths:[[1,100,-20,100],[2,-100,20,-100]]}),ctx);fx.update(1/60);
  assert(fx.stats.knockoutBursts.active===2,'simultaneous offscreen KO bursts');
  finite('shared');
  reset();assert(fx.stats.shuriken.instances===0&&fx.stats.shuriken.particles===0&&fx.stats.graphicHits.active===0&&fx.stats.knockoutBursts.active===0,'restart clears every pool');
  const actor=fighters[3];actor.attackTick=10;actor.sequence=7;
  fx.onFrame(frame({events:[event('presentation.bonk.normal.glint',{entity:actor.id,source:'Timeline',bone:'bone.right-hand',sequence:7})]}),ctx);fx.update(1/60);
  actor.sequence=8;actor.attackTick=0;fx.onFrame(frame(),ctx);fx.update(1/60);
  assert(fx.stats.shuriken.instances===0,'replacement attack clears its old attachment');
  reset();
  fx.onFrame(frame({events:Array.from({length:100},()=>event('presentation.bonk.slam.landing'))}),ctx);fx.update(1/60);
  assert(fx.stats.shuriken.instances<=64&&fx.stats.shuriken.particles<=1024,'bounded 1024-particle and 64-instance pools');
  assert(fx.stats.unknown.length===0,'no unknown presentation ids');
  console.log('SHURIKEN_CHECKS_PASS',JSON.stringify({visible,projectiles:projectileRows.length,...fx.stats.shuriken}));
  console.log('VFX_CHECKS_PASS',JSON.stringify(fx.stats));
  const step=Demo.Step;
  window.__vfxCapture={tick:0,hits:0,blocked:0,deaths:0,events:0,ids:{},projectileSlots:{},modeEvents:{}};
  Demo.Step=(...args)=>{
    const result=step(...args),f=JSON.parse(result),c=window.__vfxCapture;
    c.finished=f.ended;c.tick=f.tick;c.hits+=f.hits.length;c.blocked+=f.hits.filter(h=>h[3]).length;c.deaths+=f.deaths.length;c.events+=f.events.length;
    for(const e of f.events)if(e.id)c.ids[e.id]=(c.ids[e.id]??0)+1;
    for(const h of f.hitboxes)if(h.v.some(v=>v!==0)){const pkg=f.fighters.find(f=>f.id===h.owner)?.pkg??'hazard';c.projectileSlots[`${pkg}.${h.slot}.${h.air}`]=(c.projectileSlots[`${pkg}.${h.slot}.${h.air}`]??0)+1;}
    for(const [kind] of f.mode?.events??[])c.modeEvents[kind]=(c.modeEvents[kind]??0)+1;
    if(c.tick%240===0)console.log('VFX_LIVE',JSON.stringify(c));return result;
  };
}
async function gallery() {
  const THREE = await import('three');
  const { createVfx } = await import('./vfx.js');
  for (const child of document.body.children) child.style.display = 'none';
  addEventListener('keydown', e => e.stopImmediatePropagation(), true);
  const renderer = new THREE.WebGLRenderer({ antialias: true }); renderer.setSize(innerWidth, innerHeight);
  renderer.domElement.style.cssText = 'position:fixed;inset:0;z-index:1'; document.body.append(renderer.domElement);
  const scene = new THREE.Scene(); scene.background = new THREE.Color(0x201b30);
  const camera = new THREE.PerspectiveCamera(40, innerWidth / innerHeight, 0.1, 300);
  camera.position.set(0, 12, 18); camera.lookAt(0, 0, 0); camera.updateMatrixWorld();
  scene.add(new THREE.HemisphereLight(0xfff3d8, 0x302840, 2));
  const light = new THREE.DirectionalLight(0xffffff, 3); light.position.set(-4, 12, 8); scene.add(light);
  const world = new THREE.Group(); world.scale.z = -1; scene.add(world);
  const floor = new THREE.Mesh(new THREE.BoxGeometry(21, 0.25, 10), new THREE.MeshStandardMaterial({ color: 0x343047, roughness: 1 }));
  floor.position.y = -0.2; world.add(floor);
  const fx = createVfx({ world, camera });
  const fighters = ['fightguy', 'manki', 'wibou', 'bonk', 'bashie', 'nyx', 'salamander'].map((pkg, i) => ({ id: i + 1, pkg, yaw: 0 }));
  const ctx = { seatOf: id => id - 1, seatColor: () => 0x14b8a6, arena: { bounds: [-10, 10, -5, 5], killHeight: -20 }, boneWorld: () => null };
  const projectiles = [['fightguy', 11], ['manki', 11], ['manki', 5], ['wibou', 11], ['bonk', 11], ['bashie', 11], ['nyx', 11]];
  const hitboxes = projectiles.map(([pkg, slot], i) => ({ owner: fighters.find(f => f.pkg === pkg).id, slot, air: false,
    emit: i, age: 1, shape: 0, a: [-7.5 + i * 2.5, 1, -2.8], b: [0, 0, 0], r: slot === 5 ? 0.5 : 0.43, v: [1, 0, 1] }));
  const effects = ['presentation.wibou.flash.cut', 'presentation.manki.bazooka.explosion', 'presentation.fightguy.ki-shot.charge', 'presentation.nyx.grasp-drag'];
  const events = effects.map((id, i) => ({ id, entity: 1, p: [-6 + i * 4, 0.8, 2.6], yaw: Math.PI / 2, bone: null, local: [0, 0, 0], scale: [1, 1, 1], ticks: 20 }));
  fx.onFrame({ tick: 1, fighters, hitboxes, events, hits: [[1, 2, 22, false, 2, 1, 2.6, 12, 9], [2, 1, 5, true, 6, 1, 2.6, 3, 3]], deaths: [[1, 12, 6, 0]] }, ctx);
  fx.update(0.04); fx.update(0.04); renderer.render(scene, camera);
  function label(text, x, y, z, fontSize = 13) {
    const p = new THREE.Vector3(x, y, -z).project(camera), el = document.createElement('div');
    el.textContent = text;
    el.style.cssText = `position:fixed;z-index:3;left:${(p.x * .5 + .5) * innerWidth}px;top:${(-p.y * .5 + .5) * innerHeight}px;transform:translate(-50%,-50%);font:800 ${fontSize}px system-ui;color:#eee6d4;letter-spacing:.06em;white-space:nowrap;pointer-events:none`;
    document.body.append(el);
  }
  ['KI SHOT', 'ROUND BOMB', 'ROCKET', 'KUNAI', 'POT LID', 'WATER RING', 'HEX CROW'].forEach((s, i) => label(s, -7.5 + i * 2.5, 0, -2.8));
  ['BLADE CUT', 'EXPLOSION', 'KI CHARGE / HEAVY HIT', 'TETHER / BLOCK'].forEach((s, i) => label(s, -6 + i * 4, 0, 2.6));
  const title = document.createElement('div'); title.textContent = 'SLOPARENA  /  EFFECT KIT';
  title.style.cssText = 'position:fixed;z-index:3;top:24px;left:32px;color:#ffcc22;font:900 24px system-ui;letter-spacing:.1em'; document.body.append(title);
  console.log('VFX_GALLERY_PASS', JSON.stringify({ ...fx.stats, calls: renderer.info.render.calls, triangles: renderer.info.render.triangles }));
}
const select = mode === 'brawl'
  ? `for(const n of ${JSON.stringify((process.env.VFX_BRAWL_FIGHTERS ?? "Bashie,Sir Bonk").split(","))}) [...document.querySelectorAll("#pickCpu .pick")].find(b=>b.textContent===n).click();[...document.querySelectorAll("#pickLevel .pick")].find(b=>b.textContent==="Hard").click();`
  : mode === 'slopball' ? '[...document.querySelectorAll("#pickMode .pick")].find(b=>b.textContent.includes("Slopball")).click();' : '';
const finish = process.env.VFX_BRAWL_TICKS ? `window.__vfxCapture.finished || window.__vfxCapture.tick >= ${Number(process.env.VFX_BRAWL_TICKS)}` : 'window.__vfxCapture.finished';
const script = {
  port: Number(process.env.VFX_PORT ?? 9341), size:'960x540', query: mode === 'brawl' ? '?autoplay&stocks=1&vfxbench' : '',
  setup: `(async () => { await (${browserChecks.toString()})();const start=Demo.Start;Demo.Start=(m,p,s,d,seed)=>start(m,p,s,d,884);${mode === 'kit' ? `await (${gallery.toString()})();` : select} })()`,
  steps: mode === 'brawl'
    ? [{ ms: 3500, shot: 'brawl' }, { until: 'window.__vfxCapture.hits >= 20', timeout: 30000, shot: 'brawl-contact' }, { until: finish, timeout: 180000, shot: 'brawl-finished' }, {eval: 'JSON.stringify(__vfxBench?.vfx)',ms:0}]
    : mode === 'slopball' ? [{ ms: 5000, shot: 'slopball-serve' }, { down: ['Digit1'], ms: 1800, up: ['Digit1'], shot: 'slopball-swing' }, { ms: 5000, shot: 'slopball-late' }]
      : [{ ms: 100, shot: 'effect-kit' }],
};
const child = spawn(process.execPath, [fileURLToPath(new URL('./drive.mjs', import.meta.url)), out, JSON.stringify(script)], { stdio: ['ignore', 'pipe', 'inherit'] });
let output = '';
child.stdout.on('data', data => { output += data; process.stdout.write(data); });
const code = await new Promise(resolve => child.on('exit', resolve));
if (code || (mode === 'brawl' && !output.includes(`${finish} -> true`)) || !output.includes('BROWSER_ERRORS 0') || !output.includes('VFX_CHECKS_PASS') || output.includes('Shader Error')
  || (mode === 'kit' ? !output.includes('VFX_GALLERY_PASS') : !output.includes('VFX_LIVE') || output.includes('EXC:'))) process.exitCode = 1;
