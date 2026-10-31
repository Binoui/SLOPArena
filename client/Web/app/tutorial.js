import { SLOT, prompt as key } from './bindings.js';

// The Fighter's License checklist and PASSED stamp, ported from Unity's TutorialView. The sim's TutorialMode
// sends the lessons, the player's move names and each tick's progress; this module owns the wording.
const COMPLETED_KEY = 'sloparena.tutorial.completed';
export function tutorialCompleted() {
  try { return localStorage.getItem(COMPLETED_KEY) === '1'; } catch { return false; }
}
export function markTutorialCompleted() {
  try { localStorage.setItem(COMPLETED_KEY, '1'); } catch {}
}

// Juggle openers a Shared probe confirmed against the dummy at 30% (docs/design/tutorial.md, TutorialView.ComboOpeners):
// starter, follow-up and whether to jump in between.
const COMBO_OPENERS = {
  fightguy: ['slot3', 'slot3', true],
  manki: ['slot1', 'R', false],
  wibou: ['slot3', 'slot3', false],
  bonk: ['slot3', 'slot1', true],
  bashie: ['slot1', 'R', false],
};

// The page has no free camera: Steady turns toward the dummy as the player circles it, as with Unity's
// right-stick specials. Web specials never aim, so the recovery hint has no hold-and-aim step.
const LESSONS = {
  MoveAndLook: ['MOVE & LOOK', 'You always move relative to the camera. Run around the dummy and the camera turns to keep it in view.'],
  Jump: ['JUMP', 'Tap jump for a short hop, hold it for a full jump. You get one more jump in the air.'],
  FastFall: ['FAST FALL', 'Fast falling gets you back on the ground sooner.'],
  Attacks: ['ATTACKS', 'Quick hit, upward hit, big hit. Each normal has a job.'],
  Aerials: ['AERIALS', 'The same buttons do different moves in the air.'],
  Beeline: ['BEELINE', 'Beeline, then attack on arrival. Nothing in reach means a punishable miss.'],
  BeelineTricks: ['BEELINE TRICKS', 'Block a swing, then Beeline to punish. It homes on the nearest opponent within 20 m; no lock needed. Cooldowns are off.'],
  Specials: ['SPECIALS', 'Specials hit harder but need to recharge. E also helps you get back to the stage.'],
  Recovery: ['RECOVERY', 'There are no ledges to grab. Use your air jump and E to get back.'],
  ShieldAndDodge: ['SHIELD & DODGE', 'Shield blocks attacks from every side. In the air, the same button dodges the way you push.'],
  GrabAndThrow: ['GRAB & THROW', 'Grab turns toward a nearby fighter. Grab during Beeline to catch on grounded arrival.'],
  Combo: ['COMBO', null],
  Knockout: ['KNOCKOUT', 'The higher the damage %, the farther they fly. Knock them off the stage to win.'],
};

// Inputs pressed in order read 'A, B'; alternatives read 'A B'.
const SEQUENCES = new Set(['BeelineAerial', 'BeelineOutOfShield']);
const PROMPTS = {
  Run: ['move'], LookAround: ['move'],
  StandOnBlock: ['jump'], DoubleJump: ['jump'], ReachTallBlock: ['jump'], RecoverToStage: ['jump'],
  FastFall: ['down'], HitNormal1: ['slot1'], HitNormal3: ['slot3'], HitNormal4: ['slot4'],
  LandAerials: ['slot1', 'slot3', 'slot4'], Throw: ['slot1', 'slot3', 'slot4'],
  BeelineHit: ['dash'], BeelineRecovery: ['dash'], BeelineChase: ['dash'],
  BeelineAerial: ['jump', 'dash'], BeelineOutOfShield: ['shield', 'dash'],
  UseSpecialA: ['A'], UseSpecialE: ['E'], UseSpecialR: ['R'], UseSpecialF: ['F'],
  BlockAttacks: ['shield'], AirDodge: ['shield'], Grab: ['grab'],
};

const $ = id => document.getElementById(id);

// court: TutorialMode.WriteCourt (lessons, tallBlock, moves by wire slot as [ground, air]); pkg: the player's package.
export function createTutorialPanel({ court, pkg }) {
  const panel = $('tutorial'), rows = panel.querySelector('.t-objectives');
  const move = (action, airborne = false) => court.moves[SLOT[action]]?.[airborne ? 1 : 0] ?? 'that move';
  const objectiveText = id => ({
    Run: 'Run around',
    LookAround: 'Turn the camera',
    StandOnBlock: 'Jump onto a block',
    DoubleJump: 'Jump again in mid-air',
    ReachTallBlock: `Stand on the ${court.tallBlock} m block`,
    FastFall: 'While falling, press Down to fast fall',
    HitNormal1: 'Quick hit: ' + move('slot1'),
    HitNormal3: 'Hit upward: ' + move('slot3'),
    HitNormal4: 'Big hit: ' + move('slot4'),
    LandAerials: 'Hit the dummy with two different aerials',
    BeelineHit: 'Beeline to the dummy, then hit on the ground',
    BeelineAerial: 'Jump, Beeline, then hit with an aerial',
    BeelineOutOfShield: 'Beeline straight out of Shield',
    BeelineRecovery: 'Drop off an edge, Beeline and land back on stage',
    BeelineChase: 'Launch the dummy, Beeline after it and hit it',
    UseSpecialA: 'Use ' + move('A'),
    UseSpecialE: 'Use ' + move('E'),
    UseSpecialR: 'Use ' + move('R'),
    UseSpecialF: 'Use ' + move('F'),
    RecoverToStage: 'Drop off an open edge and make it back',
    BlockAttacks: "Hold Shield to block the dummy's attacks",
    AirDodge: 'Press Shield in the air to dodge',
    Grab: 'Grab the dummy',
    Throw: 'Attack to throw. Hold a direction to fling, or stay neutral to toss up.',
    Combo: 'Hit the dummy again before it lands',
    Knockout: 'Knock the dummy off the stage',
  })[id] ?? id;
  const comboHint = pad => {
    const lead = 'Launch the dummy, then hit it again before it lands.';
    const opener = COMBO_OPENERS[pkg];
    if (!opener) return lead + ' Cooldowns are off here.';
    const [starter, followUp, jump] = opener;
    return `${lead} Try ${move(starter)} [${key(starter, pad)}], then ${jump ? 'jump and ' : ''}`
      + `${move(followUp, jump)} [${key(followUp, pad)}]. Cooldowns are off here.`;
  };
  const describe = (id, pad) => {
    const [title, hint] = LESSONS[id] ?? [id.toUpperCase(), ''];
    return [title, id === 'Combo' ? comboHint(pad) : hint];
  };
  const amountText = ([id, goal], amount) => id === 'Run' ? `${Math.floor(amount)} / ${goal} m`
    : id === 'LookAround' ? `${Math.floor(amount)}° / ${goal}°` : goal > 1 ? `${Math.floor(amount)} / ${goal}` : '';

  let shown = -1, shownPad = null, stampTimer = 0;
  function render(index, pad) {
    const lesson = court.lessons[index];
    const [title, hint] = describe(lesson.id, pad);
    panel.querySelector('.t-eyebrow').textContent = `FIGHTER'S LICENSE · TEST ${index + 1} / ${court.lessons.length}`;
    panel.querySelector('.t-title').textContent = title;
    panel.querySelector('.t-hint').textContent = hint;
    rows.replaceChildren(...lesson.objectives.map(([id]) => {
      const row = document.createElement('li');
      const prompt = (PROMPTS[id] ?? []).map(action => key(action, pad)).join(SEQUENCES.has(id) ? ', ' : ' ');
      row.innerHTML = '<i class="t-check"></i><kbd class="t-key"></kbd><span class="t-text"></span><b class="t-amount"></b>';
      row.querySelector('.t-key').textContent = prompt;
      row.querySelector('.t-key').hidden = !prompt;
      row.querySelector('.t-text').textContent = objectiveText(id);
      return row;
    }));
    shown = index; shownPad = pad;
  }

  function stamp(word, detail, license) {
    const el = $('tutorialStamp');
    el.querySelector('.word').textContent = word;
    el.querySelector('.detail').textContent = detail;
    el.classList.toggle('license', license);
    // Restart the slam animation (CSS) even when two stamps come back to back.
    el.classList.add('hidden');
    void el.offsetWidth;
    el.classList.remove('hidden');
    clearTimeout(stampTimer);
    stampTimer = setTimeout(() => el.classList.add('hidden'), 1470);
  }

  panel.classList.remove('hidden');
  return {
    // state: TutorialMode.WriteFrame; pad: the last input came from a gamepad.
    present(state, pad) {
      if (state.lesson !== shown || pad !== shownPad) render(state.lesson, pad);
      const objectives = court.lessons[state.lesson].objectives;
      [...rows.children].forEach((row, i) => {
        const done = state.amounts[i] >= objectives[i][1];
        row.classList.toggle('done', done);
        const text = done ? '' : amountText(objectives[i], state.amounts[i]);
        const amount = row.querySelector('.t-amount');
        if (amount.textContent !== text) amount.textContent = text;
      });
      panel.classList.toggle('passed', state.between);
      if (state.passed) {
        const lesson = court.lessons[state.lesson];
        if (state.license) stamp('LICENSE GRANTED', "FIGHTER'S LICENSE", true);
        else stamp('PASSED', `TEST ${state.lesson + 1} / ${court.lessons.length} · ${describe(lesson.id, pad)[0]}`, false);
      }
    },
    setPaused(paused) { panel.classList.toggle('hidden', paused); },
    dispose() {
      clearTimeout(stampTimer);
      panel.classList.add('hidden');
      $('tutorialStamp').classList.add('hidden');
      rows.replaceChildren();
    },
  };
}
