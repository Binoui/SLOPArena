import * as THREE from 'three';
import { toThree } from './geometry.js';

// Unity's cameras, in sim space. Steady (ADR-0043, CameraMount.UpdateSteadyFraming) is the game's default:
// player-centred at a fixed 10 m orbit, starting behind the fighter at 15 degrees and easing up to 35, height
// following landings rather than jumps, turning slowly toward the opponent only once they leave a wide cone.
// Framing keeps several points on screen (Slopball).
const RADIUS = 10;
const PITCH = 35;
const START_PITCH = 15; // CameraMount.ResetView; Steady eases up to PITCH
const TURN_CONE = 60;
const TURN_SPEED = 20; // degrees per second, ClientSettings.cameraTurnSpeed default
const HEIGHT_BAND = 3;
const LAUNCH_SECONDS = 6;
// CameraMount.prefab's OrbitalFollow damps the followed point by 1 s (Cinemachine leaves 1% after the damp time);
// the HardLookAt still aims at the undamped point.
const FOLLOW_RATE = 4.605;
const deltaAngle = (a, b) => ((b - a + 540) % 360) - 180;
const ease = (rate, dt) => 1 - Math.exp(-rate * dt);

export function createCamera(camera) {
  let yaw = 0, pitch = PITCH, yawVelocity = 0, turning = false, groundY = null, raise = 0, clock = 0;
  const followed = new THREE.Vector3();
  const launches = new Map();
  let snapped = false;

  function place(anchor, dt, snap) {
    const y = yaw * Math.PI / 180, x = pitch * Math.PI / 180;
    const back = new THREE.Vector3(-Math.sin(y) * Math.cos(x), Math.sin(x), -Math.cos(y) * Math.cos(x)).multiplyScalar(RADIUS);
    if (snap) followed.copy(anchor);
    else followed.lerp(anchor, ease(FOLLOW_RATE, dt));
    camera.position.copy(toThree(followed.x + back.x, followed.y + back.y, followed.z + back.z));
    camera.lookAt(toThree(anchor.x, anchor.y, anchor.z));
  }

  // self: the player's frame entry; focus: [x,y,z] of the combat target or null; modelYOffset: the player's
  // drawn offset (Unity follows the rendered transform, not the capsule centre); subjects: the other fighters
  // as { id, p, launched }, where a launched one lifts the view for its first 6 s within 20 m.
  function steady(self, focus, dt, { modelYOffset = 0, subjects = [] } = {}) {
    const snap = !snapped;
    snapped = true;
    clock += dt;
    const p = [self.p[0], self.p[1] + modelYOffset, self.p[2]];
    if (snap) { yaw = (self.yaw ?? 0) * 180 / Math.PI; pitch = START_PITCH; }
    if (groundY === null) groundY = p[1];
    const above = p[1] - groundY;
    const goal = self.grounded ? p[1] : above > HEIGHT_BAND ? p[1] - HEIGHT_BAND : above < -0.5 ? p[1] + 0.5 : groundY;
    groundY += (goal - groundY) * ease(1, dt);
    let highest = Math.max(groundY + 2, focus ? focus[1] + 1.5 : -Infinity);
    for (const s of subjects) {
      if (!s.launched) { launches.delete(s.id); continue; }
      if (!launches.has(s.id)) launches.set(s.id, clock);
      const dx = s.p[0] - p[0], dz = s.p[2] - p[2];
      if (clock - launches.get(s.id) <= LAUNCH_SECONDS && dx * dx + dz * dz < 400) highest = Math.max(highest, s.p[1] + 1.5);
    }
    const height = Math.max(0, highest - (groundY + 2));
    let desired = null;
    if (focus) {
      const dx = focus[0] - p[0], dz = focus[2] - p[2];
      // Within 3 m (scrambles, cross-ups) no new turn starts.
      if (dx * dx + dz * dz > 9) {
        const wantYaw = Math.atan2(dx, dz) * 180 / Math.PI;
        const error = deltaAngle(yaw, wantYaw);
        const settle = TURN_CONE * 0.4;
        if (Math.abs(error) > TURN_CONE) turning = true;
        else if (Math.abs(error) <= settle) turning = false;
        if (turning) desired = wantYaw - Math.sign(error) * settle;
      }
    }
    const wanted = desired === null ? 0 : Math.max(-TURN_SPEED, Math.min(TURN_SPEED, deltaAngle(yaw, desired) * 1.5));
    yawVelocity += (wanted - yawVelocity) * ease(3, dt);
    yaw += yawVelocity * dt;
    pitch += (PITCH - pitch) * ease(3, dt);
    raise += (Math.min(height * 0.4, 6) - raise) * ease(height * 0.4 > raise ? 6 : 2, dt);
    place(new THREE.Vector3(p[0], groundY + 0.2 + raise, p[2]), dt, snap);
  }

  // Court and overview framing: keep every point in view from behind the nearest one.
  function frame(points, dt) {
    const pts = points.map(p => toThree(p[0], p[1] + 1, p[2]));
    if (!pts.length) return;
    const box = new THREE.Box3().setFromPoints(pts);
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    const d = Math.min(7 + size.x * 0.75 + size.z * 0.25, 30);
    const want = new THREE.Vector3(center.x, center.y + 2.5 + d * 0.45, box.max.z + d * 0.8);
    const k = snapped ? 1 - Math.exp(-dt * 4) : 1;
    snapped = true;
    const aim = new THREE.Vector3(center.x, center.y - 1.2, center.z + size.z * 0.3);
    frameAim.lerp(aim, k);
    if (k === 1) frameAim.copy(aim);
    camera.position.lerp(want, k);
    camera.lookAt(frameAim);
    yaw = 0;
  }
  const frameAim = new THREE.Vector3();

  return {
    steady,
    frame,
    reset() { snapped = false; groundY = null; raise = 0; yawVelocity = 0; turning = false; launches.clear(); },
    // The yaw movement input is relative to, in degrees (0 looks along +Z).
    get yaw() { return yaw; },
  };
}
