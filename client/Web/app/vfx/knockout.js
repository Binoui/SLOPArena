import * as THREE from 'three';

const TAU = Math.PI * 2, LIFE = .8;
const fragmentShader = `
precision highp float;
varying vec4 tint; varying vec2 local; varying vec4 shape; varying vec4 effectData;
const vec3 Ink=vec3(.106,.102,.094), Yellow=vec3(1.,.8,.133), Paper=vec3(.871,.847,.788);
const float PI=3.141592653589793; const int StarPoints=11;
float clamp01(float x){return clamp(x,0.,1.);}
            float Hash(float n) { return fract(sin(n) * 43758.5453); }

            vec2 StarVertex(int i, float angle, float seed, float outer, float inner)
            {
                float a = angle + PI * float(i) / float(StarPoints);
                float jitter = Hash(seed + float(i) * 7.13) * 2.0 - 1.0;
                float r = mod(float(i), 2.) == 0. ? outer * (1.0 + 0.28 * jitter) : inner * (1.0 + 0.15 * jitter);
                return r * vec2(cos(a), sin(a));
            }

            
            float StarDistance(vec2 p, float angle, float seed, float outer, float inner)
            {
                float d = 1e5;
                float side = 1.0;
                vec2 previous = StarVertex(2 * StarPoints - 1, angle, seed, outer, inner);
                for (int i = 0; i < 2 * StarPoints; i++)
                {
                    vec2 current = StarVertex(i, angle, seed, outer, inner);
                    vec2 e = previous - current;
                    vec2 w = p - current;
                    vec2 b = w - e * clamp01(dot(w, e) / dot(e, e));
                    d = min(d, dot(b, b));
                    bvec3 crossing = bvec3(p.y >= current.y, p.y < previous.y, e.x * w.y > e.y * w.x);
                    if (all(crossing) || !any(crossing)) side = -side;
                    previous = current;
                }
                return side * sqrt(d);
            }

            float BoxDistance(vec2 p, vec2 size)
            {
                vec2 q = abs(p) - size;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            float Fill(float d)
            {
                float aa = max(fwidth(d), 1e-4);
                return 1.0 - smoothstep(-aa, aa, d);
            }

            void Over(inout vec4 result, vec3 color, float coverage)
            {
                float alpha = coverage + result.a * (1.0 - coverage);
                result.rgb = (color * coverage + result.rgb * result.a * (1.0 - coverage)) / max(alpha, 1e-4);
                result.a = alpha;
            }

            vec4 Burst()
            {
                float radius = max(shape.y / 1.3, 1e-4);
                vec2 p = local / radius;
                float angle = effectData.x, seed = effectData.y;
                float main = StarDistance(p, angle, seed, 1.0, 0.45);
                float core = StarDistance(p, angle + 0.35, seed + 17.0, 0.52, 0.26);
                vec4 result = vec4(0.);
                Over(result, Ink, Fill(StarDistance(p - vec2(0.11, -0.11), angle, seed, 1.0, 0.45)));
                Over(result, Yellow, Fill(StarDistance(p - vec2(-0.07, 0.05), angle, seed, 1.0, 0.45)));
                Over(result, Ink, Fill(main));
                Over(result, tint.rgb, Fill(main + 0.05));
                Over(result, Ink, Fill(core));
                Over(result, Paper, Fill(core + 0.04));
                return result;
            }

            vec4 Trail()
            {
                
                float cell = shape.y * 0.36;
                vec2 q = vec2(local.x + shape.x, local.y) / cell;
                float row = floor(q.y + 0.5);
                float shift = mod(abs(row), 2.0) * 0.5;
                vec2 centre = vec2(floor(q.x - shift) + 0.5 + shift, row);
                float along = centre.x * cell / (2.0 * shape.x);
                float halfRows = mix(0.2, 1.0, along) * shape.y / cell;
                float grow = effectData.x, fade = effectData.y;
                float dotRadius = 0.46 * (1.0 - 0.75 * along) * (1.0 - fade);
                float coverage = Fill((length(q - centre) - dotRadius) * cell);
                bool shown = abs(row) <= halfRows && along <= grow && along <= 1.0 && dotRadius > 0.0;
                vec3 color = mod(abs(row), 2.0) < 0.5 ? Ink : tint.rgb;
                return vec4(color, shown ? coverage : 0.0);
            }

            vec4 Scrap()
            {
                vec2 size = shape.xy / 1.4;
                float rim = 0.3 * min(size.x, size.y);
                
                float s, c;
                s = sin(shape.z); c = cos(shape.z);
                vec2 drop = vec2(1.0, -1.0) * (0.45 * size.y);
                vec2 shadow = vec2(c * drop.x + s * drop.y, -s * drop.x + c * drop.y);
                float body = BoxDistance(local, size);
                vec4 result = vec4(0.);
                Over(result, Ink, Fill(BoxDistance(local - shadow, size)));
                Over(result, Ink, Fill(body));
                Over(result, tint.rgb, Fill(body + rim));
                return result;
            }


void main(){
  gl_FragColor=shape.w<.5?Burst():shape.w<1.5?Trail():Scrap();
}`;
const vertexShader = `
attribute vec4 color; attribute vec4 quadShape; attribute vec4 quadData; attribute vec2 quadOffset;
varying vec4 tint; varying vec2 local; varying vec4 shape; varying vec4 effectData;
void main(){
  vec3 origin=(modelViewMatrix*vec4(position,1.)).xyz;
  if(-origin.z<nearPlane+1.) origin.z=-max(abs(origin.z),6.);
  origin*=min(1.,farPlane*.5/-origin.z);
  float depth=-origin.z;
  vec2 focal=abs(vec2(projectionMatrix[0][0],projectionMatrix[1][1]));
  float screenHeight=2.*depth/focal.y;
  float radius=clamp(1.5,.08*screenHeight,.13*screenHeight), scale=radius/1.5;
  vec2 halfView=max(depth/focal-1.15*1.28*radius,vec2(0.));
  origin.xy=clamp(origin.xy,-halfView,halfView);
  vec2 offset=quadOffset; float rotation=quadShape.z;
  if(abs(quadShape.w-1.)<.5){
    vec2 toFight=dot(origin.xy,origin.xy)>1e-4?normalize(-origin.xy):vec2(0.,-1.);
    offset=toFight*quadShape.x; rotation=atan(toFight.y,toFight.x);
  }
  local=(uv*2.-1.)*quadShape.xy;
  float s=sin(rotation),c=cos(rotation);
  vec2 screen=offset+vec2(local.x*c-local.y*s,local.x*s+local.y*c);
  gl_Position=projectionMatrix*vec4(origin+vec3(screen*scale,0.),1.);
  tint=color; shape=vec4(quadShape.xy,rotation,quadShape.w); effectData=quadData;
}`;

export function createKnockoutBursts({world,camera}) {
  const pool=[];
  const material=new THREE.ShaderMaterial({
    vertexShader:'uniform float nearPlane; uniform float farPlane;\n'+vertexShader,fragmentShader,
    uniforms:{nearPlane:{value:camera.near},farPlane:{value:camera.far}},
    transparent:true,depthWrite:false,depthTest:false,side:THREE.DoubleSide,
  });
  function spawn(origin,seat){
    let b=pool.find(b=>!b.mesh.visible);
    if(!b){
      if(pool.length>=16)return;
      const geometry=new THREE.BufferGeometry(), attrs={};
      for(const [name,size] of [['position',3],['color',4],['uv',2],['quadShape',4],['quadData',4],['quadOffset',2]]){
        attrs[name]=new THREE.Float32BufferAttribute(new Float32Array(56*size),size);attrs[name].setUsage(THREE.DynamicDrawUsage);geometry.setAttribute(name,attrs[name]);
      }
      const index=[];for(let i=0;i<14;i++){const k=i*4;index.push(k,k+1,k+2,k,k+2,k+3);}geometry.setIndex(index);
      const mesh=new THREE.Mesh(geometry,material);mesh.name='Knockout Burst';mesh.frustumCulled=false;mesh.renderOrder=3014;world.add(mesh);
      b={mesh,attrs,origin:[0,0,0],scraps:[]};pool.push(b);
    }
    b.mesh.visible=true;b.age=0;b.origin=origin.slice();b.angle=Math.random()*TAU;b.seed=Math.random()*100;
    const color=new THREE.Color(seat).convertLinearToSRGB(),fills=[[.871,.847,.788,1],[.871,.847,.788,1],[1,.8,.133,1],[color.r,color.g,color.b,1]];
    b.scraps=Array.from({length:12},(_,i)=>{
      const direction=(i+Math.random()-.5)/12*TAU,speed=3+Math.random()*7;
      return {v:[Math.cos(direction)*speed,Math.sin(direction)*speed],half:[.12+Math.random()*.1,.06+Math.random()*.05],angle:Math.random()*Math.PI,spin:Math.random()*28-14,color:fills[i%4]};
    });
    build(b);
  }
  function build(b){
    let used=0;
    function quad(offset,half,angle,kind,color,data=[0,0,0,0]){
      for(let i=0;i<4;i++){
        const k=used*4+i;
        b.attrs.position.setXYZ(k,...b.origin);b.attrs.color.setXYZW(k,...color);
        b.attrs.uv.setXY(k,i===1||i===2?1:0,i>=2?1:0);b.attrs.quadShape.setXYZW(k,...half,angle,kind);
        b.attrs.quadData.setXYZW(k,...data);b.attrs.quadOffset.setXY(k,...offset);
      }
      used++;
    }
    const age=b.age,grow=Math.min(1,age/.12),fade=Math.max(0,Math.min(1,(age-.22)/.3));
    if(fade<1)quad([0,0],[2.1,.75],0,1,b.scraps[3].color,[grow,fade,0,0]);
    const decay=(1-Math.exp(-3.2*age))/3.2,shrink=Math.min(1,(LIFE-age)/.15);
    for(const s of b.scraps)quad([s.v[0]*decay,s.v[1]*decay-7*(age-decay)/3.2],s.half.map(x=>x*1.4*shrink),s.angle+s.spin*age,2,s.color);
    const pop=age<.07?.35+.8*(1-(1-age/.07)**2):age<.16?1.15-.15*(age-.07)/.09:age<.42?1:1-Math.min(1,(age-.42)/.14)**2;
    if(pop>.01)quad([0,0],[1.5*1.3*pop,1.5*1.3*pop],0,0,[.941,.357,.208,1],[b.angle+age*.25,b.seed,0,0]);
    b.mesh.geometry.setDrawRange(0,used*6);for(const attr of Object.values(b.attrs))attr.needsUpdate=true;
  }
  function update(dt){
    material.uniforms.nearPlane.value=camera.near;material.uniforms.farPlane.value=camera.far;
    for(const b of pool)if(b.mesh.visible){b.age+=dt;if(b.age>=LIFE)b.mesh.visible=false;else build(b);}
  }
  return {spawn,update,clear:()=>pool.forEach(b=>{b.mesh.visible=false;}),get stats(){return {active:pool.filter(b=>b.mesh.visible).length,pool:pool.length};}};
}
