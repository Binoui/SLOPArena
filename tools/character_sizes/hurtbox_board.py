#!/usr/bin/env python3
"""Build a local review board from the hurtbox visual and coverage reports."""
import html
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
folder = root / 'tmp/hurtbox-fit'
fighters = ['bonk', 'fightguy', 'wibou', 'nyx', 'salamander', 'manki', 'bashie']
labels = {'fightguy': 'FightGuy'}
notes = {
    'bonk': 'Cape excluded.', 'fightguy': 'Body and gloves.', 'wibou': 'Coat excluded.',
    'nyx': 'Skirt excluded.', 'salamander': 'Tail included. Hat excluded.',
    'manki': 'Tail and deform arms included.', 'bashie': 'Shell included.',
}
cards = []
for fighter in fighters:
    visual = folder / f'visual-{fighter}.json'
    coverage = folder / f'evaluate-{fighter}.json'
    if not visual.exists():
        cards.append(f'<article><h2>{labels.get(fighter, fighter.title())}</h2><p>Captures pending.</p></article>')
        continue
    captures = json.loads(visual.read_text())['paths']
    data = {pose: {view: next(item for item in captures if item['pose'] == pose and item['view'] == view)
                   for view in ('front', 'side')} for pose in ('idle', 'attack', 'hit')}
    score = ''
    if coverage.exists():
        c = json.loads(coverage.read_text())
        score = f"Body coverage {c['beforeBody']}% → {c['currentBody']}%. "
    buttons = ''.join(f'<button type="button" data-pose="{pose}" aria-pressed="{str(pose == "idle").lower()}">{label}</button>'
                      for pose, label in [('idle', 'Idle'), ('attack', 'Mid-swing'), ('hit', 'Hit-hard')])
    images = ''.join(f'<figure><a class="{view}" href="{html.escape(data["idle"][view]["path"])}" target="_blank">'
                     f'<img src="{html.escape(data["idle"][view]["path"])}" alt="{fighter} idle {view}, model with Shared capsule wires"></a>'
                     f'<figcaption>{view.title()}</figcaption></figure>' for view in ('front', 'side'))
    payload = html.escape(json.dumps(data), quote=True)
    cards.append(f'<article data-captures="{payload}"><h2>{labels.get(fighter, fighter.title())}</h2>'
                 f'<p>{score}{notes[fighter]}</p><nav aria-label="{fighter} pose">{buttons}</nav>'
                 f'<div class="views">{images}</div><small class="frame"></small></article>')
page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Capsule hurtbox review</title><style>
*{box-sizing:border-box}body{margin:0;padding:12px;background:#10151c;color:#edf2fa;font:14px/1.35 system-ui,sans-serif}
main{max-width:1040px;margin:auto}h1{font-size:22px;margin:0 0 5px}header p{max-width:760px;margin:0 0 12px;color:#bdc9da}
.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}article{background:#1c2530;border:1px solid #354355;border-radius:8px;padding:9px;min-width:0}
h2{font-size:17px;margin:0 0 2px}article p{font-size:12px;min-height:16px;margin:0 0 6px;color:#c5d2e4}
nav{display:flex;gap:5px;margin-bottom:7px}button{font:inherit;font-size:12px;min-height:28px;padding:3px 9px;color:#d4dfed;background:#263345;border:1px solid #51627b;border-radius:5px;cursor:pointer}
button[aria-pressed=true]{background:#146346;color:white;border-color:#66e9b5}button:focus-visible,a:focus-visible{outline:3px solid #ffd56e;outline-offset:2px}
.views{display:grid;grid-template-columns:1fr 1fr;gap:5px}figure{margin:0}img{width:100%;display:block;aspect-ratio:4/3;object-fit:contain;background:#222831;border-radius:4px;max-height:166px}
figcaption{font-size:11px;color:#aebed3;margin-top:2px}.frame{display:block;color:#aebed3;font-size:10px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:3px}
@media(max-width:650px){.grid{grid-template-columns:1fr}body{padding:9px}header p{font-size:12px}article{padding:7px}img{max-height:112px}h2{font-size:16px}}
</style><main><header><h1>Capsule hurtbox review</h1><p>Green wires are actual cooked Shared hurtboxes. Switch each fighter between three poses. Tap an image for full size. Body coverage excludes cloth and hats. This board supports visual review; coverage does not establish acceptance.</p></header><div class="grid">'''
page += '\n'.join(cards)
page += '''</div></main><script>
document.querySelectorAll('article[data-captures]').forEach(card=>{const data=JSON.parse(card.dataset.captures);function show(pose){
card.querySelectorAll('button').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.pose===pose)));
for(const view of ['front','side']){const item=data[pose][view],link=card.querySelector('a.'+view),img=link.querySelector('img');link.href=item.path;img.src=item.path;img.alt=card.querySelector('h2').textContent+' '+pose+' '+view+', model with Shared capsule wires'}
const item=data[pose].front;card.querySelector('.frame').textContent=item.semanticId+' · frame '+item.frame+' · '+item.capsules+' capsules'}
card.querySelectorAll('button').forEach(button=>button.onclick=()=>show(button.dataset.pose));show('idle')});
</script></html>'''
path = folder / 'visual/board.html'
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(page)
print(path)
