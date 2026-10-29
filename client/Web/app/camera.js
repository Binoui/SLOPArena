import * as THREE from 'three';
import { toThree } from './geometry.js';

// Unity's cameras, in sim space. Steady (ADR-0043, CameraMount.UpdateSteadyFraming) is the game's default:
// player-centred at a fixed 10 m orbit and 35 degree pitch, height following landings rather than jumps, turning
// slowly toward the opponent only once they leave a wide cone. Framing keeps several points on screen (Slopball).
const RADIUS = 10;
const PITCH = 35 * Math.PI / 180;
const TURN_CONE = 60;
const TURN_SPEED = 20; // degrees per second, ClientSettings.cameraTurnSpeed default
const HEIGHT_BAND = 3;
const deltaAngle = (a, b) => ((b - a + 540) % 360) - 180;

export function createCamera(camera) {
  let yaw = 0, yawVelocity = 0, turning = false, groundY = null, raise = 0;
  const target = new THREE.Vector3();
  let snapped = false;

  function place(lookAt, dt, snap) {
    const y = yaw * Math.PI / 180;
    const back = new THREE.Vector3(-Math.sin(y) * Math.cos(PITCH), Math.sin(PITCH), -Math.cos(y) * Math.cos(PITCH)).multiplyScalar(RADIUS);
    const k = snap ? 1 : 1 - Math.exp(-dt * 6);
    target.lerp(lookAt, k);
    camera.position.copy(toThree(target.x + back.x, target.y + back.y, target.z + back.z));
    camera.lookAt(toThree(target.x, target.y, target.z));
  }

  // self: the player's frame entry, focus: [x,y,z] of the combat target or null.
  function steady(self, focus, dt) {
    const snap = !snapped;
    snapped = true;
    const p = self.p;
    if (groundY === null) groundY = p[1];
    const above = p[1] - groundY;
    const goal = self.grounded ? p[1] : above > HEIGHT_BAND ? p[1] - HEIGHT_BAND : above < -0.5 ? p[1] + 0.5 : groundY;
    groundY += (goal - groundY) * (1 - Math.exp(-dt));
    const highest = Math.max(groundY + 2, focus ? focus[1] + 1.5 : -Infinity);
    raise += (Math.min(Math.max(0, highest - (groundY + 2)) * 0.4, 6) - raise) * (1 - Math.exp(-3 * dt));
    let desired = null;
    if (focus) {
      const dx = focus[0] - p[0], dz = focus[2] - p[2];
      if (dx * dx + dz * dz > 9) {
        const wantYaw = Math.atan2(dx, dz) * 180 / Math.PI;
        const error = deltaAngle(yaw, wantYaw);
        const settle = TURN_CONE * 0.4;
        if (Math.abs(error) > TURN_CONE) turning = true;
        else if (Math.abs(error) <= settle) turning = false;
        if (turning) desired = wantYaw - Math.sign(error) * settle;
        if (snap) { yaw = wantYaw; turning = false; desired = null; }
      }
    }
    const wanted = desired === null ? 0 : Math.max(-TURN_SPEED, Math.min(TURN_SPEED, deltaAngle(yaw, desired) * 1.5));
    yawVelocity += (wanted - yawVelocity) * (1 - Math.exp(-3 * dt));
    yaw += yawVelocity * dt;
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
    reset() { snapped = false; groundY = null; raise = 0; yawVelocity = 0; turning = false; },
    // The yaw movement input is relative to, in degrees (0 looks along +Z).
    get yaw() { return yaw; },
  };
}
