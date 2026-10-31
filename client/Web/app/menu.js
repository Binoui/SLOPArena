import { ROSTER_UI, portrait } from './roster-ui.js';
import { tutorialCompleted } from './tutorial.js';

const $ = id => document.getElementById(id);
const LEVELS = ['Easy', 'Normal', 'Hard'];

export function createMenu({ catalog, modes, stages, paths, pick, go, isPaused, inMatch, escapeMatch }) {
  function renderMenu() {
    const btn = (label, on, fn) => {
      const b = document.createElement('button');
      b.className = 'pick' + (on ? ' on' : ''); b.textContent = label;
      b.onclick = () => {
        const focused = document.activeElement === b, row = b.parentElement, index = [...row.children].indexOf(b);
        fn(); renderMenu();
        if (focused) { focusMenu(row.children[index]); row.children[index].focus({ preventScroll: true }); }
      };
      return b;
    };
    const fighterTile = (f, on, fn) => {
      const b = btn('', on, fn);
      b.dataset.fighter = f.id;
      b.classList.add('fighter-tile'); b.setAttribute('aria-label', f.name + (ROSTER_UI[f.id].wip ? ' (WIP)' : ''));
      const img = document.createElement('img');
      img.src = portrait(paths.assets, f.id); img.alt = '';
      const name = document.createElement('span'); name.textContent = f.name;
      b.append(img, name);
      if (ROSTER_UI[f.id].wip) b.insertAdjacentHTML('beforeend', '<small class="wip">WIP</small>');
      return b;
    };
    $('pickMode').replaceChildren(...Object.entries(modes).map(([id, m]) => {
      const b = btn(m.label, pick.mode === id, () => { pick.mode = id; });
      b.dataset.mode = id;
      if (id === 'tutorial' && !tutorialCompleted()) b.insertAdjacentHTML('beforeend', '<small class="badge">New here?</small>');
      return b;
    }));
    $('pickYou').replaceChildren(...catalog.fighters.map(f => fighterTile(f, pick.you === f.id, () => { pick.you = f.id; })));
    $('fighterHelp').textContent = ROSTER_UI[pick.you].description;
    $('pickCpu').replaceChildren(...catalog.fighters.map(f => fighterTile(f, pick.cpus.includes(f.id), () => {
      pick.cpus = pick.cpus.includes(f.id) ? pick.cpus.filter(x => x !== f.id) : [...pick.cpus, f.id].slice(-3);
    })));
    $('pickStage').replaceChildren(...stages.map(([id, name]) => {
      const b = btn(name, pick.stage === id, () => { pick.stage = id; });
      const img = document.createElement('img');
      img.src = `${paths.assets}ui/stages/${id}.webp`; img.alt = '';
      b.dataset.stage = id;
      b.prepend(img); b.classList.add('stage-tile');
      return b;
    }));
    $('pickLevel').replaceChildren(...LEVELS.map((name, i) => btn(name, pick.level === i, () => { pick.level = i; })));
    for (const id of ['secCpu', 'secStage', 'secLevel']) $(id).classList.toggle('hidden', !modes[pick.mode].rows.includes(id));
  }
  $('go').onclick = go;
  addEventListener('keydown', e => { if (e.code === 'Enter' && !inMatch() && !startScreen) $('go').click(); });

  // ── Gamepad outside play: the start screen, menu navigation (D-pad or stick, A selects, B steps back,
  // Start fights) and Start to leave a match. Browsers only count clicks and keys as the gesture that unlocks
  // sound, so the start screen asks for one and a pad still dismisses it (silently) if that's all there is.
  let startScreen = document.createElement('div');
  startScreen.id = 'start';
  startScreen.innerHTML = '<div class="press">Click or press any key</div><div class="sub">Gamepad ready: press A</div>';
  document.body.append(startScreen);
  function dismissStart(e) {
    if (!startScreen) return;
    e?.stopImmediatePropagation?.();
    startScreen.remove();
    startScreen = null;
    focusMenu(document.querySelector('#pickMode .pick.on') ?? $('go'));
  }
  addEventListener('keydown', dismissStart, { capture: true });
  addEventListener('pointerdown', dismissStart, { capture: true });

  let menuFocus = null, repeatAt = 0;
  const focusables = () => [...document.querySelectorAll(isPaused() ? '#pause button' : '#menu button, #menu a, #menu summary')]
    .filter(el => el.offsetParent);
  function focusMenu(el) {
    menuFocus?.classList.remove('pad-focus');
    menuFocus = el;
    el?.classList.add('pad-focus');
    el?.scrollIntoView({ block: 'nearest' });
  }
  function moveFocus(dx, dy) {
    const all = focusables();
    if (!menuFocus || !all.includes(menuFocus)) return focusMenu(all[0]);
    const from = menuFocus.getBoundingClientRect();
    const cx = from.x + from.width / 2, cy = from.y + from.height / 2;
    let best = null, bestScore = Infinity;
    for (const el of all) {
      if (el === menuFocus) continue;
      const r = el.getBoundingClientRect();
      const ex = r.x + r.width / 2 - cx, ey = r.y + r.height / 2 - cy;
      const along = ex * dx + ey * dy;
      if (along <= 4) continue;
      const score = along + 3 * Math.abs(ex * dy - ey * dx);
      if (score < bestScore) { bestScore = score; best = el; }
    }
    if (best) focusMenu(best);
  }
  function pollPad(pad) {
    if (!pad) return;
    const { held: b, pressed } = pad;
    const any = pad.buttons.some((_, i) => pressed(i));
    if (startScreen) {
      if (any) dismissStart();
    } else {
      const [ax, ay] = pad.axes;
      const dx = (b(15) ? 1 : 0) - (b(14) ? 1 : 0) || (Math.abs(ax) > 0.5 ? Math.sign(ax) : 0);
      const dy = (b(13) ? 1 : 0) - (b(12) ? 1 : 0) || (Math.abs(ay) > 0.5 ? Math.sign(ay) : 0);
      const now = performance.now();
      if ((dx || dy) && now >= repeatAt) {
        moveFocus(dx, dx ? 0 : dy);
        repeatAt = now + (repeatAt === 0 ? 350 : 140);
      } else if (!dx && !dy) repeatAt = 0;
      if (pressed(0) && menuFocus) {
        const keep = menuFocus.id || menuFocus.textContent;
        menuFocus.click();
        // renderMenu rebuilds the buttons; keep the focus on the same choice.
        focusMenu(focusables().find(el => (el.id || el.textContent) === keep) ?? focusables()[0]);
      }
      if (pressed(9)) { if (inMatch()) escapeMatch(); else $('go').click(); }
    }
  }
  function navigateKey(e) {
    if (!inMatch() && !startScreen && e.code.startsWith('Arrow')) {
      e.preventDefault(); moveFocus(e.code === 'ArrowRight' ? 1 : e.code === 'ArrowLeft' ? -1 : 0,
        e.code === 'ArrowDown' ? 1 : e.code === 'ArrowUp' ? -1 : 0);
      menuFocus?.focus({ preventScroll: true }); return true;
    }
  }
  function pressKey(e) {
    if (!isPaused()) return false;
    if (e.code === 'ArrowUp' || e.code === 'ArrowDown') moveFocus(0, e.code === 'ArrowUp' ? -1 : 1);
    if (e.code === 'Enter' || e.code === 'Space') { e.preventDefault(); menuFocus?.click(); }
    return true;
  }
  return { renderMenu, focusMenu, navigateKey, pressKey, pollPad };
}
