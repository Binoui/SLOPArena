export const ACTIONS = {
  move: { code: null, pad: 'L-STICK' },
  jump: { code: 'Space', pad: 'A' }, down: { code: 'KeyX', pad: 'LT' },
  slot1: { slot: 3, code: 'Digit1', pad: 'X' },
  slot3: { slot: 8, code: 'Digit2', pad: 'Y' },
  slot4: { slot: 9, code: 'Digit3', pad: 'B' },
  A: { slot: 11, code: 'KeyQ', pad: 'R-STICK DOWN', shortPad: 'R↓' },
  E: { slot: 4, code: 'KeyE', pad: 'R-STICK UP', shortPad: 'R↑' },
  R: { slot: 5, code: 'KeyR', pad: 'R-STICK LEFT', shortPad: 'R←' },
  F: { slot: 6, code: 'KeyF', pad: 'R-STICK RIGHT', shortPad: 'R→' },
  shield: { code: 'ShiftLeft', pad: 'RT' }, grab: { code: 'KeyC', pad: 'LB' },
  dash: { slot: 0, code: 'Digit4', pad: 'RB' },
};
export const SLOT = Object.fromEntries(Object.entries(ACTIONS).filter(([, a]) => a.slot !== undefined).map(([key, a]) => [key, a.slot]));
export const KEY_SLOTS = Object.fromEntries(Object.values(ACTIONS).filter(a => a.slot).map(a => [a.code, a.slot]));
// Letter prompts follow the player's keyboard layout when the browser exposes it.
let layout = null;
navigator.keyboard?.getLayoutMap?.().then(map => { layout = map; }).catch(() => {});
const keyName = code => code === 'Space' ? 'SPACE' : code === 'ShiftLeft' ? 'SHIFT'
  : code.startsWith('Digit') ? code.slice(5) : (layout?.get(code) ?? code.slice(3)).toUpperCase();
export const prompt = (action, pad, compact = false) => pad
  ? (compact ? ACTIONS[action].shortPad : null) ?? ACTIONS[action].pad
  : action === 'move' ? ['KeyW', 'KeyA', 'KeyS', 'KeyD'].map(keyName).join('') : keyName(ACTIONS[action].code);
