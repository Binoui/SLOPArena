import * as THREE from 'three';

const vertexShader = `
varying vec2 markerUv;
void main() {
  markerUv = uv;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}`;

// Direct ports of Resources/Shaders/FighterGroundMarker.shader and FighterDropLine.shader.
const markerShader = `
uniform vec3 accent;
uniform vec4 campColor;
uniform float airborne;
varying vec2 markerUv;
void main() {
  vec2 floorUv = markerUv * 2.0 - 1.0;
  float radius = length(floorUv);
  float edge = max(fwidth(radius), 0.002);
  float shadowRadius = mix(0.75, 0.92, airborne);
  float shadow = pow(clamp(1.0 - radius / shadowRadius, 0.0, 1.0), 2.0)
    * mix(0.28, 0.16, airborne);
  float arrowDistance = max(abs(floorUv.x) - (0.96 - floorUv.y) * 0.60,
    max(0.59 - floorUv.y, floorUv.y - 0.96));
  float front = clamp(0.5 + 0.5 * floorUv.y / max(radius, 0.001), 0.0, 1.0);
  float taper = pow(front, 1.3);
  float fade = smoothstep(0.06, 0.52, front);
  float crescentDistance = abs(radius - 0.66) - mix(0.002, 0.10, taper);
  float facingBorder = max((1.0 - smoothstep(-edge, edge, crescentDistance - 0.018)) * fade,
    1.0 - smoothstep(-edge, edge, arrowDistance - 0.018));
  float facingCore = max((1.0 - smoothstep(-edge, edge, crescentDistance)) * fade,
    1.0 - smoothstep(-edge, edge, arrowDistance));
  float campRim = (1.0 - smoothstep(0.018 - edge, 0.018 + edge, abs(radius - 0.50))) * campColor.a * 0.65;
  float rimAlpha = max(facingBorder * 0.96, campRim);
  vec3 ringColor = mix(campColor.rgb, mix(vec3(0.10, 0.075, 0.025), accent,
    facingCore / max(facingBorder, 0.0001)), clamp(facingBorder, 0.0, 1.0));
  float alpha = rimAlpha + shadow * (1.0 - rimAlpha);
  vec3 color = (ringColor * rimAlpha + vec3(0.08, 0.075, 0.065)
    * shadow * (1.0 - rimAlpha)) / max(alpha, 0.0001);
  gl_FragColor = vec4(color, alpha);
}`;
const lineShader = `
uniform vec3 accent;
uniform float airborne;
uniform float lengthInWidths;
uniform float spacing;
uniform float coreRadius;
varying vec2 markerUv;
void main() {
  float cellSpacing = max(spacing, 1.05);
  float along = markerUv.x * lengthInWidths;
  vec2 cell = vec2((fract(along / cellSpacing) - 0.5) * cellSpacing, markerUv.y - 0.5);
  float radius = length(cell);
  float edge = max(fwidth(radius), 0.02);
  float disc = 1.0 - smoothstep(0.5 - edge, 0.5, radius);
  float core = 1.0 - smoothstep(coreRadius - edge, coreRadius + edge, radius);
  vec3 color = mix(vec3(0.106, 0.102, 0.094), accent, core);
  gl_FragColor = vec4(color, disc * 0.95 * airborne);
}`;

export class GroundMarker {
  constructor({ world, half, seatColor, floorAt }) {
    this.world = world;
    this.half = half;
    this.floorAt = floorAt;
    // Unity uses Gamma colour space, so its shader colours are already display values.
    const accent = new THREE.Color(seatColor).convertLinearToSRGB();
    const material = (fragmentShader, uniforms) => new THREE.ShaderMaterial({
      vertexShader, fragmentShader, uniforms: { accent: { value: accent }, airborne: { value: 0 }, ...uniforms },
      transparent: true, depthWrite: false, side: THREE.DoubleSide, toneMapped: false,
    });
    this.marker = new THREE.Mesh(new THREE.PlaneGeometry(1.2, 1.2), material(markerShader, {
      campColor: { value: new THREE.Vector4(0, 0, 0, 0) },
    }));
    this.marker.name = 'FighterGroundMarker';
    this.line = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), material(lineShader, {
      bottom: { value: this.marker.position }, length: { value: 0 }, sideWidth: { value: new THREE.Vector3() },
      lengthInWidths: { value: 1 }, spacing: { value: 2 }, coreRadius: { value: 0.3 },
    }));
    this.line.material.vertexShader = `
      uniform vec3 bottom;
      uniform float length;
      uniform vec3 sideWidth;
      varying vec2 markerUv;
      void main() {
        markerUv = uv;
        vec3 p = bottom + vec3(0.0, uv.x * length, 0.0) + sideWidth * (uv.y - 0.5);
        gl_Position = projectionMatrix * modelViewMatrix * vec4(p, 1.0);
      }`;
    this.line.name = 'FighterDropLine';
    this.line.frustumCulled = false;
    this.marker.visible = this.line.visible = false;
    const cameraPosition = new THREE.Vector3(), midpoint = new THREE.Vector3(), side = new THREE.Vector3();
    const up = new THREE.Vector3(0, 1, 0), pixels = new THREE.Vector2();
    this.line.onBeforeRender = (renderer, scene, camera) => {
      camera.getWorldPosition(cameraPosition);
      midpoint.copy(this.marker.position); midpoint.y += this.length * 0.5;
      world.localToWorld(midpoint);
      const width = camera.isPerspectiveCamera ? Math.max(0.05,
        4 * 2 * cameraPosition.distanceTo(midpoint) * Math.tan(THREE.MathUtils.degToRad(camera.fov * 0.5))
          / Math.max(1, renderer.getDrawingBufferSize(pixels).y)) : 0.05;
      world.worldToLocal(cameraPosition);
      side.subVectors(cameraPosition, this.marker.position).cross(up).normalize().multiplyScalar(width);
      this.line.material.uniforms.sideWidth.value.copy(side);
      this.line.material.uniforms.length.value = this.length;
      this.line.material.uniforms.lengthInWidths.value = this.length / width;
    };
    world.add(this.marker, this.line);
  }

  update(f, out) {
    const floor = this.floorAt(f.p[0], f.p[2]);
    const height = f.p[1] - floor - this.half;
    this.marker.visible = !out && floor != null && Number.isFinite(floor) && floor > -1.7e38 && height >= -0.1;
    this.line.visible = false;
    if (!this.marker.visible) return;
    this.marker.position.set(f.p[0], floor + 0.06, f.p[2]);
    // +UV.y becomes Unity +Z before the world's existing Z reflection.
    this.marker.rotation.set(Math.PI / 2, f.yaw, 0, 'YXZ');
    const t = THREE.MathUtils.clamp((height - 0.05) / 0.7, 0, 1);
    const airborne = f.grounded ? 0 : t * t * (3 - 2 * t);
    this.marker.material.uniforms.airborne.value = airborne;
    this.line.material.uniforms.airborne.value = airborne;
    this.length = height - 0.06;
    this.line.visible = airborne > 0.01 && this.length > 0.05;
  }

  dispose() {
    this.world.remove(this.marker, this.line);
    for (const mesh of [this.marker, this.line]) { mesh.geometry.dispose(); mesh.material.dispose(); }
  }
}
