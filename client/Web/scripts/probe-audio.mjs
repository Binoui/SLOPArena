// Browser-side integration probe: real decoded assets, synthetic authoritative frames.
import { createAudio } from '../app/audio.js';
export async function probeAudio() {
  const audio = createAudio({ paths: { assets: '../assets/' } });
  document.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  for (let i = 0; i < 100 && !(globalThis.SLOPA_AUDIO.loaded >= 25); i++) await wait(50);
  const stats = globalThis.SLOPA_AUDIO;
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  assert(stats.unlocked && stats.loaded >= 25, 'Audio did not unlock/decode');
  let tick = 10000;
  const fighter = (id, grounded = 1, y = 0) => ({ id, pkg: 'bonk', state: 'Idle', p: [id, y, 0], grounded, deaths: 0 });
  const frame = (extra = {}) => ({ tick: tick++, fighters: [fighter(1), fighter(2)], hits: [], deaths: [], events: [], ended: false, ...extra });
  const ctx = { seatOf: id => id - 1 };
  const feed = async extra => { audio.onFrame(frame(extra), ctx); await wait(70); };
  audio.onMatchStart('brawl');
  await feed({});
  await feed({ fighters: [fighter(1, 0, 1), fighter(2)] });
  await feed({});
  for (const damage of [3, 6, 11]) await feed({ hits: [[2, 1, damage, 0, 0, 1, 0, 4, 3]] });
  const blocked = frame({ hits: [[2, 1, 0, 1, 0, 1, 0, 0, 3]], events: [{ source: 'BlockContact', entity: 2, id: '' }] });
  audio.onFrame(blocked, ctx);
  audio.onFrame(blocked, ctx);
  await wait(70);
  assert(stats.observed.block === 1, 'A block was doubled by its semantic event or duplicate tick');
  const impacts = ['hit-light', 'hit-medium', 'hit-heavy'].map(kind => stats.played[kind]);
  await feed({ hits: [[2, 1, 0, 0, 0, 1, 0, 0, 3]] });
  assert(impacts.every((n, i) => n === stats.played[['hit-light', 'hit-medium', 'hit-heavy'][i]]), 'Pure push played a damaging hit');
  await feed({ deaths: [[2, 0, -5, 0]], ended: true });
  audio.onMatchEnd();
  audio.onMatchStart('slopball');
  const ball = (events, returns = 0) => ({ mode: { phase: 'Live', returns, best: returns, events } });
  await feed(ball([['Served', 0]]));
  await feed(ball([['Returned', 1]], 1));
  await feed(ball([['Returned', 5]], 5));
  await feed(ball([['Missed', 5]], 0));
  const before = JSON.stringify(stats.played);
  document.getElementById('mute').click();
  assert(stats.muted && localStorage.getItem('sloparena.muted') === '1', 'Mute was not saved');
  await feed(ball([['Served', 0]]));
  assert(JSON.stringify(stats.played) === before, 'Muted audio created a voice');
  dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyM', key: 'm' }));
  assert(!stats.muted && localStorage.getItem('sloparena.muted') === '0', 'M key did not restore sound');
  await wait(70);
  document.querySelector('#pickMode button').click();
  for (const kind of ['click', 'jump', 'land', 'hit-light', 'hit-medium', 'hit-heavy', 'block', 'ko', 'finish', 'slopball-serve', 'slopball-return', 'slopball-miss', 'slopball-note', 'slopball-heat']) {
    assert(stats.played[kind] > 0, `${kind} never created a playback source`);
  }
  assert(stats.errors.length === 0, 'Audio asset/decode errors');
  audio.onMatchEnd();
  console.log('AUDIO_FRAME_PROBE ' + JSON.stringify({ passed: true, duplicateBlockSuppressed: true, purePushSilent: true, mutePersisted: true, mKeyWorks: true, stats }));
  return true;
}
