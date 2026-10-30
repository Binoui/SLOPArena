import { prompt } from './bindings.js';
import { portrait } from './roster-ui.js';
import { SEATS } from './seat-colours.js';

// present() runs every sim tick; write only what changed so an idle HUD costs no layout.
const set = (target, key, value) => { if (target[key] !== value) target[key] = value; };

export function createHud({ frame, deck, assets, info, stocks = 0, dummy = false, cards = true }) {
  const root = document.getElementById('hud'), moves = document.getElementById('moveDeck');
  const nameOf = f => f.form === 1 ? info[f.pkg].alternateName : info[f.pkg].name;
  const panels = cards ? frame.fighters.map((f, i) => {
    const card = document.createElement('div');
    card.className = 'player-card';
    card.style.setProperty('--seat', `#${SEATS[i].toString(16).padStart(6, '0')}`);
    card.innerHTML = '<img class="portrait" alt=""><div class="player-info"><div class="player-heading"><b class="seat-badge"></b><strong class="player-name"></strong></div><div class="pct"></div><div class="lives"><span>LIVES</span><b></b><div class="stocks"></div></div></div><div class="ko-flyer" hidden><b></b><small></small></div>';
    card.querySelector('.seat-badge').textContent = dummy && i === 1 ? 'DUMMY' : `P${i + 1}`;
    card.querySelector('.lives').hidden = !stocks;
    if (stocks <= 8) card.querySelector('.stocks').innerHTML = '<i></i>'.repeat(stocks);
    const q = s => card.querySelector(s);
    return { card, deaths: f.deaths, toastUntil: 0, portrait: q('.portrait'), name: q('.player-name'), pct: q('.pct'),
      lives: q('.lives b'), dots: [...q('.stocks').children], flyer: q('.ko-flyer') };
  }) : [];
  if (cards) {
    root.classList.add('players');
    root.replaceChildren(...panels.map(p => p.card));
  }
  let slots = [], shownPad = null;
  function setDeck(rows, self) {
    moves.innerHTML = '<div class="beeline"></div><div class="move-stack"><div class="normal-row"></div><div class="special-row"></div></div>';
    slots = rows.map((row, i) => {
      const el = document.createElement('div');
      el.className = i === 0 ? 'move-slot dash-slot' : `move-slot ${i < 4 ? 'normal-slot' : 'special-slot'}`;
      el.classList.toggle('locked', row.locked);
      el.dataset.action = row.key;
      el.title = row.name;
      el.setAttribute('aria-label', row.name);
      if (i === 0) {
        el.innerHTML = '<div class="beeline-tag">BEELINE</div><div class="dash-ring"><span>»</span></div><kbd></kbd><b class="move-timer"></b>';
      } else {
        el.innerHTML = '<div class="move-plate"></div><div class="move-face"><img alt=""><div class="move-refill"></div><b class="move-timer"></b></div><kbd></kbd><span class="move-name"></span>';
        const img = el.querySelector('img');
        img.hidden = !row.icon;
        if (row.icon) img.src = row.icon.startsWith('icon.')
          ? `${assets}ui/abilities/${self.pkg}/${self.pkg === 'nyx' ? (self.form === 0 ? 'Real/' : 'Fake/') : ''}${row.icon}.webp`
          : `${assets}ui/portraits/${row.icon}.webp`;
        el.querySelector('.move-name').textContent = row.name;
      }
      moves.querySelector(i === 0 ? '.beeline' : i < 4 ? '.normal-row' : '.special-row').append(el);
      return { ...row, el, previous: 0, pulseUntil: 0, timer: el.querySelector('.move-timer'), refill: el.querySelector('.move-refill'), kbd: el.querySelector('kbd') };
    });
    shownPad = null;
  }
  setDeck(deck, frame.fighters[0]);
  function present(next, pad) {
    const self = next.fighters[0];
    if (next.deck) setDeck(next.deck, self);
    panels.forEach((panel, i) => {
      const f = next.fighters[i];
      const src = portrait(assets, f.pkg, f.form);
      if (panel.portrait.getAttribute('src') !== src) panel.portrait.src = src;
      set(panel.name, 'textContent', nameOf(f));
      set(panel.pct, 'textContent', `${Math.floor(f.dmg)}%`);
      set(panel.pct.style, 'color', f.dmg >= 90 ? 'rgb(239, 68, 68)' : f.dmg >= 40 ? 'rgb(249, 115, 22)' : 'var(--paper)');
      const left = Math.max(0, stocks - f.deaths);
      set(panel.lives, 'textContent', `×${left}`);
      panel.dots.forEach((dot, k) => dot.classList.toggle('lost', k >= left));
      if (f.deaths > panel.deaths) {
        panel.toastUntil = next.tick + 100;
        const flyer = panel.flyer;
        flyer.querySelector('b').textContent = stocks && left === 0 ? 'OUT!' : 'KO!';
        flyer.querySelector('small').textContent = stocks ? `${left} ${left === 1 ? 'LIFE' : 'LIVES'} LEFT` : 'BACK IN!';
        flyer.getAnimations().forEach(a => a.cancel());
        flyer.animate([{ transform: 'rotate(-12deg) scale(1.3)' }, { transform: 'rotate(-7deg) scale(1)' }], { duration: 180 });
      }
      set(panel.flyer, 'hidden', next.tick >= panel.toastUntil);
      panel.card.classList.toggle('out', !!stocks && left === 0);
      panel.deaths = f.deaths;
    });
    slots.forEach(slot => {
      const dash = slot.slot === 0, el = slot.el;
      const cooldown = dash ? self.dashCooldown : self.cooldowns[slot.slot];
      const inUse = dash ? ['HomingDashFloat', 'HomingDash'].includes(self.state)
        : self.slot === slot.slot || self.returningSlot === slot.slot;
      el.classList.toggle('in-use', inUse);
      el.classList.toggle('on-cooldown', cooldown > 0);
      if (slot.previous > 0 && cooldown === 0 && !inUse) slot.pulseUntil = next.tick + 25;
      if (cooldown > 0 || inUse) slot.pulseUntil = 0;
      el.classList.toggle('ready-pulse', next.tick < slot.pulseUntil);
      if (dash) {
        const land = cooldown === 0 && !self.grounded && self.dashAirUsed;
        el.classList.toggle('air-used', land);
        const progress = `${land ? 0 : 360 * (1 - Math.min(1, cooldown / slot.max))}deg`;
        if (progress !== slot.progress) { slot.progress = progress; el.style.setProperty('--progress', progress); }
        set(slot.timer, 'textContent', land ? 'LAND' : '');
      } else {
        set(slot.refill.style, 'height', `${slot.max ? 100 * (1 - Math.min(1, cooldown / slot.max)) : 0}%`);
        set(slot.timer, 'textContent', cooldown > 0 ? String(cooldown >= 180 ? Math.ceil(cooldown / 60) : (Math.ceil(cooldown / 6) / 10).toFixed(1)) : '');
      }
      if (pad !== shownPad) slot.kbd.textContent = prompt(slot.key, pad, true);
      slot.previous = cooldown;
    });
    shownPad = pad;
  }
  present(frame, false);
  return {
    present,
    dispose() {
      root.classList.remove('players');
      if (cards) root.replaceChildren();
      moves.replaceChildren();
    },
  };
}
