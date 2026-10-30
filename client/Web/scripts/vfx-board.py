import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'tmp/web-vfx'
cases = json.loads((ROOT / 'client/Web/app/vfx/review-cases.json').read_text())

def captures(side):
    rows = json.loads((OUT / side / 'captures.json').read_text())
    return {row['case'] + '-' + str(row['captured']): row for row in rows}

before, after = captures('before'), captures('after')
unity = {(r['pkg'], r['move']): r['hits'] for r in json.loads((OUT / 'unity-hits.json').read_text())}
notes = {
    'bonk': 'Painted cues and the imported pot lid. Armour glow, clip footfalls and pennant deformation remain unsupported.',
    'bashie': 'Painted cues and the imported ring. Shell tuck and whistle deformation remain unsupported.',
    'fightguy': 'Exported charge tiers, mesh particles and bounded ribbons. Random particle trajectories differ.',
    'manki': 'Exported bomb and rocket, charge cues, heat, coherent noise and ribbons. Prop motion smear remains unsupported.',
    'wibou': 'Exported kunai and the attack-clock dissolve cue. The existing model lighting and animation blending differ.',
    'nyx': 'Depth-writing band-palette paper meshes on the restored attack clock. Crow wings and perch are state-driven. Tether rope lifecycle remains unsupported.',
    'salamander': 'This work-in-progress catalog has no particle or projectile bindings. These authored moves show the shared impact grammar.',
}
pages, checks, options = [], [], []
for page, case in enumerate(cases):
    pkg, move, ticks = case['pkg'], case['move'], case['ticks']
    key = case.get('key', f'{pkg}-{move}')
    rows = []
    for tick in ticks:
        images = []
        for label, side in [('Unity', 'unity'), ('Browser before', 'before'), ('Browser after', 'after')]:
            if side == 'unity':
                file = OUT / 'unity' / case.get('unity', f'{pkg}/{move}') / 'high/close' / f'{tick:03d}.jpg'
            else:
                file = max((OUT / side).glob(f'*-{key}-{tick}.png'), key=lambda p: p.stat().st_mtime)
            assert file.is_file(), file
            images.append(f'<figure><figcaption>{label}</figcaption><img src="{html.escape(str(file.relative_to(OUT)))}" alt="{key}, tick {tick}, {label}"></figure>')
        rows.append(f'<h3>Tick {tick}</h3><div class="triplet">{"".join(images)}</div>')
    last = str(ticks[-1])
    actual = [[h['frame'], h['hit'][2]] for h in after[key + '-' + last]['hits']]
    baseline = [[h['frame'], h['hit'][2]] for h in before[key + '-' + last]['hits']]
    assert actual == baseline, (key, actual, baseline)
    if 'unity' not in case:
        expected = [h for h in unity[pkg, move] if h[0] <= ticks[-1]]
    else:
        expected = [[21, 14]] if key == 'shared-ko' else [[12, 0]]
    matches = actual == expected
    checks.append(dict(pkg=pkg, move=move, key=key, hits=actual, unityHits=expected, matchesUnity=matches,
                       maxParticles=max(after[f'{key}-{tick}']['stats']['shuriken']['particles'] for tick in ticks)))
    title = key.replace('-', ' ').title() if key.startswith('shared-') else f'{pkg.title()} · Ground {move}'
    note = case.get('note', notes[pkg])
    if not matches:
        note += f' Receipt difference. Unity {expected}; browser {actual}.'
    pages.append(f'<section class="page" id="page-{page}" {"hidden" if page else ""}><h2>{title}</h2><p>{html.escape(note)}</p>{"".join(rows)}</section>')
    options.append(f'<option value="{page}">{html.escape(title)}</option>')
count = len(pages)
body = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Unity and browser combat presentation</title><style>
*{box-sizing:border-box}body{margin:0;padding:16px;font:15px/1.4 system-ui;background:#181923;color:#f4eee0}main{max-width:1500px;margin:auto}h1{font-size:24px;margin:0 0 8px}h2{font-size:20px;margin:12px 0 4px}h3{font-size:14px;margin:12px 0 6px}p{margin:4px 0 10px;color:#c9c4bd}nav{display:flex;gap:8px;align-items:center;margin:16px 0}button,select{background:#303342;color:inherit;border:1px solid #777580;border-radius:4px;padding:10px;font:inherit;min-height:44px}select{width:0;min-width:0;flex:1;max-width:400px}nav span{min-width:40px;white-space:nowrap;font-size:12px}nav button{flex:0 0 44px}.triplet{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}figure{margin:0}img{display:block;width:100%;height:auto;aspect-ratio:16/9;background:#252532}figcaption{font-size:13px;margin:0 0 4px;color:#f6cd58}small{display:block;color:#aca8ac;font-size:12px;margin:12px 0}[hidden]{display:none!important}@media(max-width:650px){.triplet{grid-template-columns:1fr;gap:8px}body{padding:10px}h1{font-size:21px}nav{gap:6px}button{padding:8px}p{font-size:13px}}</style>
<main><h1>Unity effects in the browser</h1><p>Seven fighters on Splash Deck. Two moments per move, plus the shared block ripple and KO flyer.</p>
<small>960×540 deterministic duels. The before column uses the frozen generic kit. Both browsers run the same C# simulation. The hit receipts are recorded in move-parity.json. Cosmetic random seeds, model animation blending and stage lighting differ. Select a page to compare.</small>
<nav><button id="prev" aria-label="Previous move">←</button><select id="move" aria-label="Move">OPTIONS</select><button id="next" aria-label="Next move">→</button><span id="count"></span></nav>
PAGES
</main><script>const select=document.getElementById('move'),total=COUNT;function show(n){n=(n+total)%total;select.value=n;document.querySelectorAll('.page').forEach((p,i)=>p.hidden=i!==n);document.getElementById('count').textContent=(n+1)+'/'+total;}select.onchange=()=>show(+select.value);document.getElementById('prev').onclick=()=>show(+select.value-1);document.getElementById('next').onclick=()=>show(+select.value+1);show(0);</script></html>'''.replace('OPTIONS', ''.join(options)).replace('PAGES', '\n'.join(pages)).replace('COUNT', str(count))
(OUT / 'board.html').write_text(body)
(OUT / 'move-parity.json').write_text(json.dumps(checks, indent=2) + '\n')
print(f'{sum(c["matchesUnity"] for c in checks)}/{len(checks)} Unity hit receipts match; board written {OUT / "board.html"}')
