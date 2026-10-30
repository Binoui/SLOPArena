import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { DRACOLoader } from 'three/addons/loaders/DRACOLoader.js';

const exported = new Set(['after_hours', 'slop_pit', 'splash_deck', 'table_manners', 'log_off', 'hamster_habitat', 'training', 'slopball', 'license_test']);
const loaders = new Map();

export const hasStageArt = stageId => exported.has(stageId);

// Unity's StageSightHoles (SlopArenaStageCutaway.hlsl): stage geometry above a fighter's feet dithers away inside a
// camera-to-fighter cone, so walls never hide the fight and the camera never pulls in. Radius, edge and opacity
// are Unity's defaults; Shared positions are capsule centres and the roster's shortest half height keeps floors whole.
const SIGHT_SHAPE = new THREE.Vector3(1.8, 0.5, 0);
const FEET_BELOW_CENTRE = 0.75;
const sightCentres = [];
// points: up to four capsule centres in three.js world space, the player first; none closes every hole.
export function setSightHoles(points) {
  sightCentres.length = 0;
  for (const p of points.slice(0, 4)) sightCentres.push(p);
}

export async function loadStageArt(stageId, { paths }) {
  if (!exported.has(stageId)) return null;
  const checking = new URLSearchParams(location.search).has('stagecheck');
  let loader = loaders.get(paths.assets);
  if (!loader) {
    const draco = new DRACOLoader().setDecoderPath(paths.assets + 'stages/draco/').setWorkerLimit(2);
    loader = new GLTFLoader().setDRACOLoader(draco);
    loaders.set(paths.assets, loader);
  }
  try {
    const gltf = await loader.loadAsync(paths.assets + `stages/${stageId}.glb`);
    const art = gltf.scene;
    const environment = art.userData.environment ?? {};
    const materials = new Map();
    const meshes = [];
    art.traverse(object => { if (object.isMesh) meshes.push(object); });
    for (const mesh of meshes) {
      if (!materials.has(mesh.material)) materials.set(mesh.material, stageMaterial(mesh.material, gltf.parser, environment));
    }
    const eye = new THREE.Vector3(), centre = new THREE.Vector3(), feet = new THREE.Vector3();
    for (const mesh of meshes) {
      const original = mesh.material;
      mesh.material = await materials.get(original);
      const shadow = mesh.userData.stageShadows ?? mesh.parent?.userData.stageShadows ?? {};
      mesh.castShadow = shadow.cast ?? false;
      mesh.receiveShadow = shadow.receive ?? true;
      const surface = mesh.material.userData.stageSurface;
      if (mesh.receiveShadow && surface?.shader.endsWith('StageVertexLit')
          && surface.floats?._ReceiveShadowsOff !== 1 && !(surface.floats?._Wind > 0)) {
        const receiver = new THREE.Mesh(mesh.geometry, new THREE.ShadowMaterial({
          opacity: .3, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1,
        }));
        receiver.name = 'Fighter shadow receiver';
        receiver.receiveShadow = true;
        receiver.material.fog = false;
        mesh.add(receiver);
      }
      mesh.onBeforeRender = (renderer, scene, camera) => {
        const u = mesh.material.uniforms;
        if (!u) return;
        const review = checking ? globalThis.__stageCheck : null;
        if (review?.art === art && review.view) {
          camera.position.set(...review.view.position);
          camera.lookAt(...review.view.target);
          camera.far = environment.cameraFar ?? camera.far;
          camera.updateProjectionMatrix();
          camera.updateMatrixWorld();
        }
        if (review?.art === art) review.camera = camera;
        u.clock.value = performance.now() * .001;
        camera.getWorldPosition(eye);
        mesh.worldToLocal(eye);
        u.eyeSim.value.copy(eye);
        u.sightCount.value = sightCentres.length;
        sightCentres.forEach((p, i) => {
          mesh.worldToLocal(centre.copy(p));
          mesh.worldToLocal(feet.set(p.x, p.y - FEET_BELOW_CENTRE, p.z));
          u.sightTargets.value[i].set(centre.x, centre.y, centre.z, feet.y);
        });
      };
      // Shader travel can wrap hundreds of metres away from its original bounds.
      if (mesh.geometry.hasAttribute('_stage_travel')) mesh.frustumCulled = false;
    }
    // Expose atmosphere/light values for the owner to apply to fighters and the scene.
    const bounds = new THREE.Box3().setFromObject(art);
    environment.cameraFar = Math.ceil(Math.max(bounds.min.length(), bounds.max.length()) * 1.2 + 100);
    art.userData.environment = environment;
    art.name = `${stageId} presentation`;
    if (checking) {
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(globalThis.Demo.ArenaTriangles(), 3));
      const wire = new THREE.LineSegments(new THREE.WireframeGeometry(geometry), new THREE.LineBasicMaterial({
        color: 0x00ffff, depthTest: false, transparent: true, opacity: .8,
      }));
      geometry.dispose();
      wire.name = 'Authoritative collision overlay';
      wire.renderOrder = 10000;
      art.add(wire);
      meshes.forEach(mesh => { mesh.frustumCulled = false; });
      globalThis.__stageCheck = { stageId, art, overlay: wire };
    }
    console.info(`Stage art: ${stageId} (${meshes.length} meshes)`);
    return art;
  } catch (error) {
    console.warn(`Stage art unavailable: ${stageId}; using collision geometry`, error);
    return null;
  }
}


const common = /* glsl */`
uniform float clock;
uniform vec4 tint;
uniform vec3 sky, ground, sunColor, sunDirection;
uniform vec3 fogColor;
uniform vec2 fogRange;
uniform float fogAmount;
uniform sampler2D atlas;
uniform float hasAtlas;
varying vec4 paint;
varying vec2 texcoord;
varying vec3 simPosition, simNormal, eye;
varying float depth;
float saturate(float x) { return clamp(x, 0.0, 1.0); }
vec3 ambient(vec3 n) { return mix(ground, sky, n.y * .5 + .5); }
vec4 sampleAtlas() { return mix(vec4(1.0), texture2D(atlas, texcoord), hasAtlas); }
vec3 finishColor(vec3 c) {
  float fog = smoothstep(fogRange.x, fogRange.y, depth) * fogAmount;
  c = mix(c, fogColor, fog);
  // The Unity gameplay camera uses ACES. Keep the stage's HDR emission coloured.
  return clamp((c * (2.51 * c + .03)) / (c * (2.43 * c + .59) + .14), 0.0, 1.0);
}
`;

const animation = /* glsl */`
float hash11(float n) { return fract(sin(n * 12.9898) * 43758.5453); }
float stageAnimation(vec2 channel, float t) {
  float rate = channel.x, phase = channel.y;
  // Draco's floating channels can differ at the last digit around zero and the mode boundaries.
  if (abs(rate) < .0001) return 1.0;
  if (rate < 0.0) {
    float burst = step(.72, hash11(floor(t * .35 + phase * 17.0)));
    float drop = step(hash11(floor(t * -rate * 9.0 + phase * 61.0)), .45);
    return mix(1.0, .12, burst * drop);
  }
  if (rate < 49.9999) {
    float x = fract(t * rate + phase);
    return smoothstep(0.0, .04, x) * (1.0 - smoothstep(.5, .54, x));
  }
  if (rate < 99.9999) return .55 + .45 * sin((t * (rate - 50.0) + phase) * 6.2831853);
  if (rate < 149.9999) return step(fract(t * (rate - 100.0) + phase), .3333);
  return mix(.08, 1.0, step(.22, hash11(floor(t * (rate - 150.0) + phase * 7.0) + phase * 131.0)));
}
vec3 stageTravel(vec3 p, vec2 travel, vec2 dirLoop, float t) {
  if (abs(travel.x) < .0001) return p;
  float loopLength = length(dirLoop);
  if (loopLength < .001) {
    p.y += travel.x * abs(sin(3.14159265 * (t * floor(travel.y + .00001) * .1 + fract(travel.y))));
  } else {
    float along = travel.y + travel.x * t;
    float wrapped = along - loopLength * floor(along / loopLength + .5);
    p.xz += dirLoop / loopLength * (wrapped - travel.y);
  }
  return p;
}
`;

const swell = /* glsl */`
uniform float swellHeight, swellLength, swellSpeed, swellFade;
vec3 swell(vec2 xz) {
  float t = clock * swellSpeed;
  float k1 = 6.2831853 / max(swellLength, .5), k2 = k1 * 1.63;
  vec2 d1 = normalize(vec2(-.94, .34)), d2 = normalize(vec2(-.42, -.91));
  float p1 = dot(d1, xz) * k1 + t, p2 = dot(d2, xz) * k2 + t * 1.31;
  float fade = saturate(1.0 - length(xz - vec2(2.5, 0.0)) / max(swellFade, 1.0));
  float a1 = swellHeight * .65 * fade, a2 = swellHeight * .35 * fade;
  return vec3(a1 * sin(p1) + a2 * sin(p2), a1 * k1 * cos(p1) * d1 + a2 * k2 * cos(p2) * d2);
}
`;

const vertex = /* glsl */`
attribute vec4 color;
attribute vec2 _stage_anim, _stage_travel, _stage_dir;
uniform vec3 eyeSim;
uniform float timeScale, wind, windFrequency;
uniform vec3 windDirection;
varying float glowAnimation;
${common}
${animation}
#ifdef WATER
${swell}
#endif
void main() {
  vec3 p = position;
  #ifdef NEON
    p = stageTravel(p, _stage_travel, _stage_dir, clock * timeScale);
  #endif
  #ifdef VERTEX_LIT
    float t = clock * windFrequency, phase = dot(p, vec3(.37, .21, .29));
    float sway = sin(t + phase) * .65 + sin(t * 2.3 + phase * 1.7) * .35;
    p += (normalize(windDirection + vec3(.0001, 0.0, 0.0)) * sway
      + vec3(0.0, .3 * sin(t * 3.1 + phase * 2.3), 0.0)) * wind * color.a;
  #endif
  #ifdef WATER
    p.y += swell(p.xz).x;
  #endif
  paint = color;
  texcoord = uv;
  simPosition = p;
  simNormal = normal;
  eye = eyeSim;
  glowAnimation = stageAnimation(_stage_anim, clock * timeScale);
  vec4 mv = modelViewMatrix * vec4(p, 1.0);
  depth = -mv.z;
  gl_Position = projectionMatrix * mv;
}
`;

const fragment = /* glsl */`
${common}
varying float glowAnimation;
uniform float baseLevel, ambientScale, shade, glow;
uniform vec3 shadeDirection;
uniform float detailScale, detailStrength, wrapLight, specular, gloss, emission;
uniform float hasDetail;
uniform sampler2D detailMap;
uniform vec3 shadowTint;
uniform vec3 pointPosition[6], pointColor[6];
uniform float pointRange[6];
uniform float faceOpacity, rim, rimPower;
#ifdef WATER
${swell}
uniform sampler2D rippleMap;
uniform float rippleScale, rippleStrength, rippleSpeed, fresnel, bodyDiffuse;
uniform float foamStrength, foamSpeed;
uniform vec3 reflectionSky, reflectionHorizon, foamColor;
vec3 ripple(vec2 uv) { return (texture2D(rippleMap, uv).rgb * 2.0 - 1.0).rbg; }
#endif
uniform vec4 sightTargets[4];
uniform float sightCount;
uniform vec3 sightShape;
// Unity's 4x4 Bayer matrix, bayer[y][x].
float bayer2(vec2 p) { return 2.0 * abs(p.x - p.y) + p.y; }
float bayer4(vec2 p) { vec2 q = mod(floor(p), 4.0); return 4.0 * bayer2(mod(q, 2.0)) + bayer2(floor(q * .5)); }
void sightHoles() {
  if (sightCount <= 0.0) return;
  float coverage = 1.0;
  for (int i = 0; i < 4; i++) {
    if (float(i) >= sightCount) break;
    vec4 target = sightTargets[i];
    if (simPosition.y <= target.w + 0.1) continue;
    vec3 toTarget = target.xyz - eye;
    float len = length(toTarget);
    if (len <= 0.0001) continue;
    vec3 dir = toTarget / len;
    vec3 rel = simPosition - eye;
    float along = dot(rel, dir);
    if (along <= 0.0) continue;
    float radius = sightShape.x * along / len;
    float soft = sightShape.y * along / len;
    float perp = length(rel - dir * along);
    float hole = (1.0 - smoothstep(radius - soft, radius, perp)) * smoothstep(len + 0.3, len - 0.1, along);
    coverage = min(coverage, mix(1.0, sightShape.z, hole));
  }
  if (coverage < (bayer4(gl_FragCoord.xy) + 0.5) / 16.0) discard;
}
void main() {
  sightHoles();
  vec3 n = normalize(simNormal) * (gl_FrontFacing ? 1.0 : -1.0);
  vec3 viewDir = normalize(eye - simPosition);
  vec4 tex = sampleAtlas();
  vec3 albedo = paint.rgb * tint.rgb * tex.rgb;
  vec3 color;
  float alpha = 1.0;
  #ifdef NEON
    float facing = saturate(dot(n, normalize(shadeDirection)) * .5 + .5);
    color = albedo * (vec3(baseLevel * mix(1.0, facing, shade)) + ambient(n) * ambientScale);
    color += albedo * paint.a * tex.a * glow * glowAnimation;
  #endif
  #ifdef VERTEX_LIT
    vec3 blend = pow(abs(n), vec3(4.0));
    blend /= max(.0001, blend.x + blend.y + blend.z);
    vec3 p = simPosition * detailScale;
    float detail = texture2D(detailMap, p.zy).r * blend.x + texture2D(detailMap, p.xz).r * blend.y
      + texture2D(detailMap, p.xy).r * blend.z;
    albedo *= mix(1.0, detail * 2.0, detailStrength * paint.a * hasDetail);
    float ndl = saturate((dot(n, sunDirection) + wrapLight) / (1.0 + wrapLight));
    vec3 a = ambient(n) * mix(shadowTint, vec3(1.0), ndl);
    color = albedo * (a + sunColor * ndl);
    for (int i = 0; i < 6; ++i) {
      vec3 delta = pointPosition[i] - simPosition;
      float d2 = dot(delta, delta);
      float attenuation = pow(saturate(1.0 - pow(d2 / max(.01, pointRange[i] * pointRange[i]), 2.0)), 2.0) / max(d2, 1.0);
      float lambert = saturate((dot(n, normalize(delta)) + wrapLight) / (1.0 + wrapLight));
      color += albedo * pointColor[i] * attenuation * lambert;
    }
    float highlight = pow(saturate(dot(n, normalize(sunDirection + viewDir))), gloss) * specular;
    float edge = pow(1.0 - saturate(dot(n, viewDir)), 3.0) * .35 * specular;
    color += sunColor * albedo * highlight * 2.0 + albedo * a * edge + albedo * emission;
  #endif
  #ifdef GLASS
    float ndl = saturate(dot(n, sunDirection) * .5 + .5);
    float sheen = pow(saturate(dot(n, normalize(sunDirection + viewDir))), gloss) * specular;
    color = albedo * (ambient(n) + sunColor * ndl) + sunColor * sheen;
    alpha = saturate(faceOpacity + pow(1.0 - saturate(dot(n, viewDir)), rimPower) * rim + sheen * .5);
  #endif
  #ifdef ADDITIVE
    color = paint.rgb;
    alpha = paint.a;
  #endif
  #ifdef WATER
    vec3 p = simPosition;
    vec3 wave = swell(p.xz);
    float dist = length(p.xz - vec2(2.5, 0.0));
    float t = clock * rippleSpeed;
    vec2 uv = p.xz * rippleScale;
    vec3 r1 = ripple(uv + vec2(.71, .23) * t);
    vec3 r2 = ripple(uv * 1.73 + vec2(-.31, .77) * t * 1.21);
    float k = rippleStrength * mix(1.0, .3, saturate(dist / 700.0));
    n = normalize(vec3(-wave.y + (r1.x + r2.x) * k, 1.0, -wave.z + (r1.z + r2.z) * k));
    float ndl = saturate(dot(n, sunDirection));
    vec3 a = ambient(vec3(0, 1, 0));
    vec3 body = paint.rgb * tint.rgb * (a + sunColor * (.35 + .65 * ndl)) * bodyDiffuse;
    vec3 reflection = reflect(-viewDir, n);
    vec3 skyReflection = mix(reflectionHorizon, reflectionSky, saturate(reflection.y * 2.2));
    float f = .02 + .98 * pow(1.0 - saturate(dot(n, viewDir)), 5.0);
    color = mix(body, skyReflection, saturate(f * fresnel + .12 * fresnel));
    color += sunColor * pow(saturate(dot(n, normalize(sunDirection + viewDir))), gloss) * specular;
    float lap = .72 + .28 * sin(clock * foamSpeed + dot(p.xz, vec2(.37, .21)));
    float breakup = saturate(.5 + .5 * (r1.x * 1.7 - r2.z * 1.3));
    float foam = saturate((paint.a * lap * 1.25 - breakup * .55) * 2.6) * foamStrength;
    color = mix(color, foamColor * (a + sunColor * (.4 + .6 * ndl)), foam);
  #endif
  gl_FragColor = vec4(finishColor(color), alpha);
  #include <colorspace_fragment>
}
`;

function linear(rgb) {
  return new THREE.Color(...rgb.slice(0, 3)).convertSRGBToLinear();
}

async function stageMaterial(original, parser, environment) {
  const spec = original.userData.stageSurface;
  if (!spec) return original;
  const f = spec.floats ?? {}, c = spec.colors ?? {}, v = spec.vectors ?? {};
  const atmosphere = environment.atmosphere ?? {};
  const key = environment.lights?.find(l => l.type === 'directional') ?? {};
  const elevation = THREE.MathUtils.degToRad(key.sun?.elevation ?? key.rotation?.[0] ?? 55);
  const azimuth = THREE.MathUtils.degToRad(key.sun?.azimuth ?? key.rotation?.[1] ?? 30);
  const direction = new THREE.Vector3(-Math.sin(azimuth) * Math.cos(elevation), Math.sin(elevation), -Math.cos(azimuth) * Math.cos(elevation));
  const points = environment.lights?.filter(l => l.type === 'point').slice(0, 6) ?? [];
  const kind = { StageVertexLit: 'VERTEX_LIT', StageNeon: 'NEON', StageWater: 'WATER', StageGlass: 'GLASS', Additive: 'ADDITIVE' }[spec.shader.split('/').at(-1)];
  if (!kind) return original;
  const uniforms = {};
  const uniform = (name, value) => { uniforms[name] = { value }; };
  const number = (name, property, fallback) => uniform(name, f[property] ?? fallback);
  const colour = (name, value) => uniform(name, linear(value));
  const tex = async id => id == null ? null : parser.getDependency('texture', id);
  const detail = await tex(spec.detailTexture), ripples = await tex(spec.rippleTexture);
  if (detail) detail.colorSpace = THREE.NoColorSpace;
  if (ripples) ripples.colorSpace = THREE.NoColorSpace;
  const base = spec.baseColor ?? [1, 1, 1, 1];
  const tint = linear(base);
  uniform('tint', new THREE.Vector4(tint.r, tint.g, tint.b, base[3] ?? 1));
  uniform('atlas', original.map);
  uniform('hasAtlas', original.map ? 1 : 0);
  uniform('detailMap', detail);
  uniform('hasDetail', detail ? 1 : 0);
  uniform('rippleMap', ripples);
  uniform('eyeSim', new THREE.Vector3());
  uniform('sightTargets', Array.from({ length: 4 }, () => new THREE.Vector4()));
  uniform('sightCount', 0);
  uniform('sightShape', SIGHT_SHAPE);
  uniform('clock', 0);
  colour('sky', atmosphere.ambientSky ?? [.6, .65, .75]);
  colour('ground', atmosphere.ambientGround ?? [.25, .2, .2]);
  uniforms.sky.value.multiplyScalar(atmosphere.ambientIntensity ?? 1);
  uniforms.ground.value.multiplyScalar(atmosphere.ambientIntensity ?? 1);
  colour('sunColor', key.color ?? [1, 1, 1]);
  uniforms.sunColor.value.multiplyScalar(key.intensity ?? 1);
  uniform('sunDirection', direction);
  colour('fogColor', atmosphere.fogColor ?? [.2, .2, .2]);
  uniform('fogRange', new THREE.Vector2(atmosphere.fogStart ?? 100, atmosphere.fogEnd ?? 1000));
  uniform('fogAmount', atmosphere.fog ? f._FogAmount ?? 1 : 0);
  uniform('shadeDirection', new THREE.Vector3(...(v._ShadeDirection ?? [.35, .8, .45]).slice(0, 3)));
  uniform('windDirection', new THREE.Vector3(...(v._WindDirection ?? [1, 0, .35]).slice(0, 3)));
  uniform('pointPosition', Array.from({ length: 6 }, (_, i) => new THREE.Vector3(...(points[i]?.position ?? [0, 0, 0]))));
  uniform('pointColor', Array.from({ length: 6 }, (_, i) => linear(points[i]?.color ?? [0, 0, 0]).multiplyScalar(points[i]?.intensity ?? 0)));
  uniform('pointRange', Array.from({ length: 6 }, (_, i) => points[i]?.range ?? 1));
  colour('shadowTint', c._ShadowTint ?? [.62, .68, .82]);
  colour('reflectionSky', c._SkyColor ?? [.42, .62, .88]);
  colour('reflectionHorizon', c._HorizonColor ?? [.8, .88, .94]);
  colour('foamColor', c._FoamColor ?? [.95, .97, .98]);
  for (const [name, property, fallback] of [
    ['baseLevel', '_Base', .35], ['ambientScale', '_Ambient', .6], ['shade', '_Shade', .35],
    ['glow', '_Glow', 3], ['timeScale', '_TimeScale', 1], ['detailScale', '_DetailScale', .4],
    ['detailStrength', '_DetailStrength', .3], ['wrapLight', '_Wrap', .2], ['specular', '_Specular', kind === 'WATER' ? 2.4 : 0],
    ['gloss', '_Gloss', kind === 'WATER' ? 900 : 24], ['emission', '_Emission', 0],
    ['wind', '_Wind', 0], ['windFrequency', '_WindFrequency', 1.7],
    ['swellHeight', '_SwellHeight', .12], ['swellLength', '_SwellLength', 11], ['swellSpeed', '_SwellSpeed', .9],
    ['swellFade', '_SwellFade', 140], ['rippleScale', '_RippleScale', .11], ['rippleStrength', '_RippleStrength', .7],
    ['rippleSpeed', '_RippleSpeed', .35], ['fresnel', '_Fresnel', .75], ['bodyDiffuse', '_Diffuse', .85],
    ['foamStrength', '_FoamStrength', .85], ['foamSpeed', '_FoamSpeed', 1.3],
    ['rim', '_Rim', .45], ['rimPower', '_RimPower', 2.5],
  ]) number(name, property, fallback);
  uniform('faceOpacity', base[3] ?? .1);
  const transparent = kind === 'GLASS' || kind === 'ADDITIVE';
  const material = new THREE.ShaderMaterial({
    name: original.name,
    defines: { [kind]: '' }, uniforms, vertexShader: vertex, fragmentShader: fragment,
    side: f._Cull === 0 ? THREE.DoubleSide : f._Cull === 1 ? THREE.BackSide : THREE.FrontSide,
    transparent, depthWrite: !transparent,
    blending: kind === 'ADDITIVE' ? THREE.AdditiveBlending : THREE.NormalBlending,
    defaultAttributeValues: { color: [1, 1, 1, 1], uv: [0, 0], _stage_anim: [0, 0], _stage_travel: [0, 0], _stage_dir: [0, 0] },
  });
  material.userData.stageSurface = spec;
  return material;
}
