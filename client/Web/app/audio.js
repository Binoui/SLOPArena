// Presentation only: the fixed-tick frame is the authority for every match cue.
export function createAudio({ paths }) {
  const base = paths.assets + 'audio/';
  const stats = { unlocked: false, muted: false, loaded: 0, played: {}, observed: {}, dropped: 0, errors: [], recent: [] };
  let muted = false;
  try { muted = localStorage.getItem('sloparena.muted') === '1'; } catch {}
  stats.muted = muted;
  // Automated browsers (Playwright, headless CDP) stay silent; ?sound=1 opts back in.
  const silent = navigator.webdriver === true && !new URLSearchParams(location.search).has('sound');
  stats.silent = silent;
  const level = () => (muted || silent ? 0 : 0.8);
  let context, master, music, manifest, mode, lastTick = -1, finished = false;
  let previous = new Map(), best = 0, returns = 0, countdown = 0;
  const buffers = new Map(), voices = new Set(), sequence = new Map(), cooldown = new Map();
  const emit = (type, detail) => dispatchEvent(new CustomEvent('slopa:' + type, { detail }));
  const count = (table, kind) => { table[kind] = (table[kind] ?? 0) + 1; };
  const ready = fetch(base + 'manifest.json').then(r => {
    if (!r.ok) throw new Error(`Audio manifest: HTTP ${r.status}`);
    return r.json();
  }).then(data => {
    manifest = data;
    return Promise.all(Object.entries(data.clips).filter(([key]) => key !== 'MainTheme').map(async ([key, file]) => {
      const response = await fetch(base + file);
      if (!response.ok) throw new Error(`Audio ${key}: HTTP ${response.status}`);
      buffers.set(key, await response.arrayBuffer());
    }));
  }).catch(error => stats.errors.push(String(error)));

  // Exposed counters make actual playback observable in headless verification.
  globalThis.SLOPA_AUDIO = stats;
  async function unlock() {
    try {
      if (!context) {
        const AudioContextClass = globalThis.AudioContext ?? globalThis.webkitAudioContext;
        if (!AudioContextClass) return;
        context = new AudioContextClass();
        master = context.createGain();
        master.gain.value = level();
        master.connect(context.destination);
      }
      // Call play/resume in the gesture itself, including on Safari.
      const resumed = context.resume();
      if (!music) {
        music = new Audio(base + 'MainTheme.mp3');
        music.loop = true;
        music.preload = 'auto';
        music.muted = muted || silent;
        music.volume = mode ? 0.16 : 0.25;
        music.addEventListener('error', () => stats.errors.push('MainTheme could not load'));
        music.addEventListener('playing', () => count(stats.played, 'music'));
      }
      if (music && !muted && !document.hidden) music.play().catch(() => {});
      await resumed;
      stats.unlocked = context.state === 'running';
      await ready;
      for (const [key, buffer] of buffers) {
        if (buffer instanceof ArrayBuffer) {
          // Mark before awaiting so another gesture cannot decode a detached buffer.
          buffers.set(key, null);
          context.decodeAudioData(buffer).then(decoded => {
            buffers.set(key, decoded);
            stats.loaded++;
          }).catch(error => stats.errors.push(`${key}: ${error}`));
        }
      }
    } catch (error) { stats.errors.push(String(error)); }
  }

  function sound(kind, key = kind, volume = 0.55, pitch = 1, seat = -1, delay = 0) {
    count(stats.observed, kind);
    const buffer = buffers.get(key);
    if (muted || document.hidden || !context || context.state !== 'running' || !buffer?.duration) return;
    const now = context.currentTime;
    // Bound overlapping volleys without changing the authoritative event stream.
    if (voices.size >= 20 || now - (cooldown.get(kind) ?? -1) < 0.025) { stats.dropped++; return; }
    cooldown.set(kind, now);
    const voice = context.createBufferSource();
    const gain = context.createGain();
    voice.buffer = buffer;
    voice.playbackRate.value = pitch;
    gain.gain.value = volume;
    voice.connect(gain);
    let pan;
    if (seat >= 0 && context.createStereoPanner) {
      pan = context.createStereoPanner();
      pan.pan.value = seat === 0 ? -0.18 : 0.18;
      gain.connect(pan).connect(master);
    } else gain.connect(master);
    voices.add(voice);
    voice.onended = () => { voices.delete(voice); voice.disconnect(); gain.disconnect(); pan?.disconnect(); };
    voice.start(now + delay);
    count(stats.played, kind);
    stats.recent.push({ kind, key, tick: lastTick });
    if (stats.recent.length > 24) stats.recent.shift();
  }

  function toggleMute() {
    muted = !muted;
    stats.muted = muted;
    try { localStorage.setItem('sloparena.muted', muted ? '1' : '0'); } catch {}
    if (master) master.gain.setValueAtTime(level(), context.currentTime);
    if (music) music.muted = muted || silent;
    updateButton();
    if (!muted) unlock();
  }
  function updateButton() {
    const button = document.getElementById('mute');
    if (!button) return;
    button.setAttribute('aria-pressed', String(muted));
    button.setAttribute('aria-label', muted ? 'Unmute audio (M)' : 'Mute audio (M)');
    button.title = muted ? 'Unmute (M)' : 'Mute (M)';
    button.querySelector('span').textContent = muted ? 'Sound off' : 'Sound on';
  }
  updateButton();
  document.getElementById('mute')?.addEventListener('click', toggleMute);
  addEventListener('pointerdown', unlock, { passive: true });
  addEventListener('keydown', event => {
    if (event.repeat || /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName) || event.target.isContentEditable) return;
    if (event.code === 'KeyM') { event.preventDefault(); toggleMute(); }
    else unlock();
  });
  document.addEventListener('click', event => {
    if (event.target.closest('button:not(#mute), a, summary')) sound('click', 'click', 0.32);
  });
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      music?.pause();
      for (const voice of voices) { try { voice.stop(); } catch {} }
    } else if (stats.unlocked && !muted) unlock();
  });

  function finish() {
    if (finished) return;
    finished = true;
    sound('finish', 'ko', 0.58, 0.82);
    emit('match-result', { modeId: mode });
  }
  return {
    onMenu() { if (music) music.volume = 0.25; },
    onMatchStart(modeId) {
      mode = modeId;
      finished = false;
      lastTick = -1;
      best = returns = countdown = 0;
      previous.clear(); sequence.clear(); cooldown.clear();
      if (music) music.volume = 0.16;
      sound('start', 'ko', 0.45);
      emit('match-start', { modeId });
    },
    onFrame(frame, ctx) {
      if (frame.tick != null && frame.tick === lastTick) return;
      lastTick = frame.tick ?? lastTick + 1;
      const fighters = new Map(frame.fighters.map(f => [f.id, f]));
      const blocked = new Set();
      for (const [target, owner, damage, block] of frame.hits ?? []) {
        if (block) {
          if (!blocked.has(target)) sound('block', 'block', 0.32, 1, ctx.seatOf(target));
          blocked.add(target);
          continue;
        }
        if (damage <= 0) continue;
        const tier = damage >= 11 ? 'heavy' : damage >= 6 ? 'medium' : 'light';
        const pkg = fighters.get(owner)?.pkg ?? 'bonk';
        const pool = manifest?.pools[pkg]?.[tier] ?? manifest?.pools.bonk?.[tier];
        const i = sequence.get(owner) ?? 0;
        sequence.set(owner, i + 1);
        if (pool?.length) sound('hit-' + tier, pool[i % pool.length], tier === 'heavy' ? 0.72 : 0.58, 1, ctx.seatOf(target));
      }
      for (const event of frame.events ?? []) {
        // BlockContact can arrive without a damaging hit. Never double the hit-array cue.
        if ((event.source === 'BlockContact' || event.id === 'presentation.block.contact') && !blocked.has(event.entity)) {
          sound('block', 'block', 0.32, 1, ctx.seatOf(event.entity));
          blocked.add(event.entity);
        }
      }
      const dead = new Set((frame.deaths ?? []).map(([id]) => id));
      for (const id of dead) sound('ko', 'ko', 0.72, 0.65, ctx.seatOf(id));
      for (const fighter of frame.fighters) {
        const old = previous.get(fighter.id);
        if (old && old.deaths === fighter.deaths && !dead.has(fighter.id)) {
          const rising = old.p && fighter.p && fighter.p[1] > old.p[1] + 0.015;
          const jumping = !fighter.grounded && rising && (old.grounded || (fighter.anim?.includes('jump') && old.anim !== fighter.anim));
          if (jumping && fighter.state !== 'Hitstun') sound('jump', 'jump', 0.24, 1, ctx.seatOf(fighter.id));
          if (!old.grounded && fighter.grounded) sound('land', 'land', 0.12, 1.12, ctx.seatOf(fighter.id));
        }
      }
      previous = fighters;
      if (mode === 'slopball' && frame.mode) {
        const state = frame.mode;
        best = state.best; returns = state.returns;
        const beat = state.phase === 'Countdown' ? Math.ceil(state.countdown / 60) : 0;
        if (beat && beat !== countdown) sound('slopball-countdown', 'slopball_beep', 0.32, beat === 1 ? 1.5 : 1);
        countdown = beat;
        for (const [kind, total] of state.events ?? []) {
          if (kind === 'Served') sound('slopball-serve', 'slopball_pop', 0.65);
          if (kind === 'Returned') {
            sound('slopball-return', 'slopball_splat', 0.58);
            const notes = [0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24];
            sound('slopball-note', 'slopball_note', 0.42, 2 ** (notes[Math.min(Math.max(0, total - 1), notes.length - 1)] / 12), -1, 0.04);
            if (total % 5 === 0) sound('slopball-heat', 'slopball_heat', 0.45, 1, -1, 0.1);
          }
          if (kind === 'Missed') sound('slopball-miss', 'slopball_miss', 0.55);
        }
      }
      if (frame.ended) finish();
    },
    onMatchEnd() {
      for (const voice of voices) { try { voice.stop(); } catch {} }
      emit('match-end', { modeId: mode, best, returns, finished });
      mode = null;
      previous.clear();
      if (music) music.volume = 0.25;
    },
  };
}
