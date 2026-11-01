import { SLOT, KEY_SLOTS } from './bindings.js';

export function createInput({ protocol, cameraYaw, navigateKey, pressKey }) {
  // ── Input: keyboard by physical key position (as in Unity), plus the first gamepad ──
  // Unity's face order: south, west, north, east.
  const PAD_FACE = [0, 2, 3, 1];
  const PAD_NORMAL = [0, SLOT.slot1, SLOT.slot2, SLOT.slot3];
  // ADR-0042 right-stick specials: a flick past 60% presses the special laid out like the face diamond
  // (up E, left R, right F, down Q); another one needs the stick back under 35%.
  const STICK_PRESS = 0.6, STICK_RELEASE = 0.35;
  let stickArmed = true;
  // Which device the player last used, for the tutorial's key prompts.
  let usingPad = false;
  const held = new Set();
  const edges = { jump: false, slot: 0, shield: false, grab: false, dash: false, down: false, retarget: false };
  let pad = null, padPrev = [];
  addEventListener('keydown', e => {
    if (e.code === 'Tab') e.preventDefault();
    if (navigateKey(e)) return;
    if (e.repeat) return;
    held.add(e.code);
    usingPad = false;
    if (pressKey(e)) return;
    if (e.code === 'Space') edges.jump = true;
    if (KEY_SLOTS[e.code]) edges.slot = KEY_SLOTS[e.code];
    if (e.code === 'ShiftLeft') edges.shield = true;
    if (e.code === 'KeyC') edges.grab = true;
    if (e.code === 'Digit4') edges.dash = true;
    if (e.code === 'KeyX') edges.down = true;
    if (e.code === 'Tab') edges.retarget = true;
  });
  addEventListener('keyup', e => held.delete(e.code));
  addEventListener('blur', () => held.clear());

  function poll() {
    const current = [...(navigator.getGamepads?.() ?? [])].find(Boolean);
    const buttons = current?.buttons.map(x => x.pressed) ?? [];
    const previous = padPrev;
    padPrev = buttons;
    pad = current ? { buttons, axes: [...current.axes], held: i => !!buttons[i], pressed: i => !!buttons[i] && !previous[i] } : null;
    return pad;
  }
  function gameplay() {
    if (!pad) return;
    const { pressed } = pad;
    PAD_FACE.forEach((btn, face) => {
      if (!pressed(btn)) return;
      if (PAD_NORMAL[face]) edges.slot = PAD_NORMAL[face]; else edges.jump = true;
    });
    const [ax, ay, rx = 0, ry = 0] = pad.axes;
    if (pad.buttons.some(Boolean) || Math.hypot(ax, ay) > 0.5 || Math.hypot(rx, ry) > 0.5) usingPad = true;
    const rmag = Math.hypot(rx, ry);
    if (rmag < STICK_RELEASE) stickArmed = true;
    else if (stickArmed && rmag >= STICK_PRESS) {
      stickArmed = false;
      edges.slot = Math.abs(rx) > Math.abs(ry) ? (rx > 0 ? SLOT.F : SLOT.R) : (ry < 0 ? SLOT.E : SLOT.A);
    }
    if (pressed(4)) edges.grab = true;
    if (pressed(5)) edges.dash = true;
    if (pressed(7)) edges.shield = true;
    if (pressed(6)) edges.down = true;
    if (pressed(15)) edges.retarget = true;
  }

  // Unity keeps the camera basis a held direction started with, so a camera turn does not bend a run underway;
  // a new direction (more than 40 degrees away) re-reads the camera.
  let heldBasis = null, heldAngle = 0;
  function cameraRelative(mx, my) {
    if (mx === 0 && my === 0) { heldBasis = null; return [0, 0]; }
    const angle = Math.atan2(mx, my) * 180 / Math.PI;
    if (heldBasis === null || Math.abs(((angle - heldAngle + 540) % 360) - 180) > 40) { heldBasis = cameraYaw(); heldAngle = angle; }
    const y = heldBasis * Math.PI / 180;
    return [my * Math.sin(y) + mx * Math.cos(y), my * Math.cos(y) - mx * Math.sin(y)];
  }
  const inputBuf = new Uint8Array(22);
  const inputView = new DataView(inputBuf.buffer);
  function buildInput(self) {
    const [ax = 0, ay = 0] = pad?.axes ?? [];
    const mag = Math.hypot(ax, ay);
    const move = pad ? { x: mag > 0.25 ? ax : 0, y: mag > 0.25 ? -ay : 0 } : null;
    let mx = (held.has('KeyD') ? 1 : 0) - (held.has('KeyA') ? 1 : 0);
    let my = (held.has('KeyW') ? 1 : 0) - (held.has('KeyS') ? 1 : 0);
    if (move && (move.x || move.y)) { mx = move.x; my = move.y; }
    const m = Math.hypot(mx, my);
    if (m > 0) { mx /= m; my /= m; }
    [mx, my] = cameraRelative(mx, my);
    const wrapDeg = r => { let d = (r * 180 / Math.PI) % 360; if (d > 180) d -= 360; if (d < -180) d += 360; return d; };
    const yaw = Math.round(wrapDeg(self?.yaw ?? 0) * 100);
    const down = held.has('KeyX') || !!pad?.held(6);
    const flags = (my > 0.3 ? 1 : 0) | (down ? 2 : 0) | (mx < -0.3 ? 4 : 0) | (mx > 0.3 ? 8 : 0)
      | (edges.jump ? 0x10 : 0) | (edges.dash ? 0x20 : 0);
    const flags2 = ((held.has('Space') || pad?.held(0)) ? 1 : 0) | (edges.down ? 8 : 0)
      | ((held.has('ShiftLeft') || pad?.held(7)) ? 0x10 : 0) | (edges.shield ? 0x20 : 0)
      | (edges.grab ? 0x40 : 0) | (edges.retarget ? 0x80 : 0);
    inputView.setFloat32(0, mx, true);
    inputView.setFloat32(4, my, true);
    inputBuf[8] = flags;
    inputBuf[9] = edges.slot;
    inputView.setInt16(10, yaw, true);
    inputView.setInt16(12, yaw, true);
    inputView.setInt16(14, 0, true);
    inputView.setUint16(16, 0, true);
    inputBuf[18] = 0;
    inputBuf[19] = flags2;
    inputBuf[20] = 1; // TargetLockMode.Always, the shipped default
    inputBuf[21] = protocol;
    clearEdges();
    return inputBuf;
  }
  function clearEdges() {
    for (const k in edges) edges[k] = k === 'slot' ? 0 : false;
  }
  return { poll, gameplay, buildInput, clearEdges, get usingPad() { return usingPad; } };
}
