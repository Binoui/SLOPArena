const assets = globalThis.SLOPA_PATHS?.assets ?? '../assets/';
for (const image of document.querySelectorAll('[data-ui-asset]')) image.src = assets + 'ui/' + image.dataset.uiAsset;
document.documentElement.style.setProperty('--paper-art', `url("${assets}ui/paper.webp")`);
for (const [family, file] of [['Archivo Black', 'ArchivoBlack-Regular.ttf'], ['Space Mono', 'SpaceMono-Regular.ttf']]) {
  const font = new FontFace(family, `url("${assets}ui/${file}")`, { display: 'swap' });
  font.load().then(face => document.fonts.add(face)).catch(() => {});
}
const menu = document.getElementById('menu');
const banner = document.getElementById('banner');
const result = document.getElementById('sessionResult');
const go = document.getElementById('go');
addEventListener('keydown', event => {
  // main.js treats Enter as FIGHT globally; focused links keep their native action.
  if (event.code === 'Enter' && event.target.closest?.('a, summary, #mute, .pick')) event.stopImmediatePropagation();
}, { capture: true });
function syncPicks() {
  for (const button of menu.querySelectorAll('.pick')) {
    const pressed = String(button.classList.contains('on'));
    if (button.getAttribute('aria-pressed') !== pressed) button.setAttribute('aria-pressed', pressed);
  }
  const mode = document.querySelector('#pickMode .on')?.dataset.mode ?? 'brawl';
  const ready = mode !== 'brawl' || !!document.querySelector('#pickCpu .on');
  go.disabled = !ready;
  const help = {
    tutorial: "Earn your Fighter's License: 13 quick tests, from your first jump to your first knockout.",
    slopball: 'Return the serves over the net. How long can you keep it going?',
  }[mode] ?? 'Knock out the CPUs. Last fighter standing wins.';
  const helpNode = document.getElementById('modeHelp');
  if (helpNode.textContent !== help) helpNode.textContent = help;
  const label = mode === 'tutorial' ? 'START' : 'FIGHT';
  if (go.textContent !== label) go.textContent = label;
  const hint = !ready ? 'Choose at least<br>one CPU'
    : mode === 'tutorial' ? 'Enter / Start to begin<br>Esc / Start to pause' : 'Enter / Start to fight<br>Esc / Start to return';
  const hintNode = document.getElementById('startHint');
  if (hintNode.innerHTML !== hint) hintNode.innerHTML = hint;
}
// main.js regenerates the pick buttons; keep semantics in step with that DOM.
new MutationObserver(syncPicks).observe(menu, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
addEventListener('slopa:match-start', ({ detail }) => {
  document.body.classList.add('playing');
  document.body.classList.toggle('slopball', detail.modeId === 'slopball');
  document.body.classList.toggle('tutorial-match', detail.modeId === 'tutorial');
  banner.classList.remove('is-result');
  result.classList.add('hidden');
  menu.scrollTop = 0;
});
addEventListener('slopa:match-result', () => banner.classList.add('is-result'));
addEventListener('slopa:match-end', ({ detail }) => {
  document.body.classList.remove('playing', 'slopball', 'tutorial-match');
  banner.classList.add('hidden');
  result.textContent = detail.modeId === 'slopball' ? `Slopball · best streak: ${detail.best ?? 0}`
    : detail.modeId === 'tutorial' ? (detail.finished ? "Fighter's License granted. Now take on the CPUs." : "The Fighter's License is here whenever you want it.")
    : detail.finished ? 'GAME! Ready for the next round?' : 'Bring a friend to the next fight.';
  result.classList.remove('hidden');
  menu.scrollTop = 0;
});
syncPicks();
