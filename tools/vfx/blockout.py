#!/usr/bin/env python3
"""Blockout videos for controlled VFX clips, sized and timed from a move's real hitboxes.

    # normals and aerials: world-space capsules per tick from the simulation (bone hitboxes included)
    scripts/move-data.sh bonk --pcts 0 --reach --json tmp/vfx/bonk-reach.json
    python3 tools/vfx/blockout.py reach tmp/vfx/bonk-reach.json g1 --package bonk --out tmp/vfx/bonk-g1
    # fixed-offset hitboxes straight from character.json (specials, capability hitboxes)
    python3 tools/vfx/blockout.py package bashie shell.splash --out tmp/vfx/bashie-splash

Each run writes `masses/` (soft white shapes on black, the control that worked best), `edges/`, `masses.mp4`,
`edges.mp4`, `check.mp4` (the masses with the real hitboxes in red and the fighter's capsule in grey, for us, never
the control) and `blockout.json` (framing, ticks per frame, motif). Feed `masses.mp4` to
`comfy_video.py --control`. The motif is a starting shape whose reach and timing agree with the hitbox; it is never
the hitbox itself (owner, 2026-10-31):

- `sweep` for a hitbox that travels (a swing): a smear along the outer part of its path that is full size on the
  first active tick, trails the motion and retreats after the last one, with a few flung sparks.
- `geyser` for a still hitbox that launches upward (angle >= 60): a column to the top of the hitbox, a floor ring
  out to its reach on the floor, a crown, the collapse and droplets landing on the ring.
- `blast` for any other still hitbox: a burst of tapered spikes biased along the launch angle, out to its radius.

Drawn at --slow times slow motion (default 4: a 4-tick rise spans six 24 fps frames); `blockout.json` maps frames to
ticks. The default camera is a three-quarter view from his front-left (--view, --yaw, --pitch). Setup and findings: docs/contributing/local-comfyui.md.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
W, H, FPS = 1280, 704, 24


# ── hitbox sources: a list of {tick, p0, p1, radius, angle} per active tick, in metres (x side, y up, z forward) ──

def slot_id(label: str) -> str:
    """Reach labels (g1, a3, gE) to character.json slot ids (ground.1, air.3, ground.E)."""
    return ('ground.' if label[0] == 'g' else 'air.') + label[1:]


def slot_angles(package: str, label: str) -> list[float]:
    path = ROOT / f'client/Unity/Assets/CharacterPackages/{package}/character.json'
    if not path.exists():
        return []
    slot = next((s for s in json.loads(path.read_text())['slots'] if s['id'] == slot_id(label)), None)
    if not slot:
        return []
    return [op['hitbox']['angle'] for st in slot['timeline']['stages'] for op in st['operations'] if op['kind'] == 'spawnHitbox']


def from_reach(path: Path, label: str, package: str | None) -> tuple[list[dict], dict]:
    data = json.loads(path.read_text())
    reach = data['reach']
    hits = [h for h in reach['hits'] if h['slot'] == label]
    if not hits:
        raise SystemExit(f'{label}: no hits in {path}; slots there: {sorted({h["slot"] for h in reach["hits"]})}')
    angles = slot_angles(package, label) if package else []
    samples = []
    for h in hits:
        angle = angles[h['hitIndex']] if h['hitIndex'] < len(angles) else 45.0
        for c in h['capsules']:
            samples.append({'tick': c['tick'], 'p0': (c['x0'], c['y0'], c['z0']), 'p1': (c['x1'], c['y1'], c['z1']),
                            'radius': c['radius'], 'angle': angle, 'hit': h['hitIndex']})
    return samples, {'height': reach['charHeight'], 'radius': reach['capsuleRadius'], 'label': hits[0]['label']}


def find(node, dotted: str):
    for part in dotted.split('.'):
        if isinstance(node, list):
            node = next(n for n in node if n.get('id') == part)
        else:
            node = node[part]
    return node


def from_package(package: str, dotted: str) -> tuple[list[dict], dict]:
    """Fixed-offset hitboxes from character.json: a capability's `hitbox`, or every spawnHitbox of a slot id."""
    char = json.loads((ROOT / f'client/Unity/Assets/CharacterPackages/{package}/character.json').read_text())
    centre = char['capsuleHeight'] / 2  # offsets start at the capsule centre
    if dotted.startswith(('ground.', 'air.')):
        slot = next(s for s in char['slots'] if s['id'] == dotted)
        events = [(op['tick'], op['hitbox']) for st in slot['timeline']['stages'] for op in st['operations']
                  if op['kind'] == 'spawnHitbox']
        label = slot['name']
    else:
        node = find(char, dotted)
        events = [(0, node['hitbox'] if 'hitbox' in node else node)]
        label = dotted
    samples = []
    for i, (tick, hb) in enumerate(events):
        if hb.get('startBoneId') or hb.get('endBoneId'):
            raise SystemExit(f'{dotted}: hit {i} rides a bone; use the reach source (scripts/move-data.sh --reach --json)')
        p0 = (hb['offsetX'], centre + hb['offsetY'], hb['offsetZ'])
        p1 = (hb['endOffsetX'] or p0[0], centre + hb['endOffsetY'] if hb['endOffsetY'] else p0[1], hb['endOffsetZ'] or p0[2]) \
            if hb['shape'] == 'capsule' else p0
        for t in range(tick, tick + max(1, hb['durationTicks'])):
            samples.append({'tick': t, 'p0': p0, 'p1': p1, 'radius': hb['radius'], 'angle': hb['angle'], 'hit': i})
    return samples, {'height': char['capsuleHeight'], 'radius': char['capsuleRadius'], 'label': label}


# ── drawing ──

def ease_out(x): x = min(max(x, 0.0), 1.0); return 1 - (1 - x) ** 3
def ease_in(x): x = min(max(x, 0.0), 1.0); return x * x
def lerp(a, b, k): return tuple(p + (q - p) * k for p, q in zip(a, b))


VIEWS = {'side': (0.0, 8.0), 'three-quarter': (40.0, 18.0), 'front': (90.0, 12.0), 'behind': (-90.0, 30.0)}


class View:
    """Orthographic camera. yaw 0 looks at the fighter from his left (forward = screen right); positive yaw swings the
    camera toward his front (90 = facing him); pitch tilts it down. Points are (x side, y up, z forward) in metres."""

    def __init__(self, yaw: float, pitch: float):
        a, b = math.radians(yaw), math.radians(pitch)
        self.right = (math.sin(a), 0.0, math.cos(a))          # screen right on the floor
        self.toward = (-math.cos(a), 0.0, math.sin(a))        # from the fighter toward the camera, on the floor
        self.cos_p, self.sin_p = math.cos(b), math.sin(b)
        self.px, self.cx, self.cy = 1.0, 0.0, 0.0

    def raw(self, x, y, z):
        u = x * self.right[0] + z * self.right[2]
        depth = x * self.toward[0] + z * self.toward[2]
        return u, y * self.cos_p - depth * self.sin_p      # nearer the camera = lower on screen when looking down

    def fit(self, points, margin=0.08):
        us, vs = zip(*(self.raw(*q) for q in points))
        self.px = min(W * (1 - 2 * margin) / ((max(us) - min(us)) or 1), H * (1 - 2 * margin) / ((max(vs) - min(vs)) or 1))
        self.cx, self.cy = (min(us) + max(us)) / 2, (min(vs) + max(vs)) / 2

    def refit(self, box, margin=0.07):
        """Refit to a screen-space box of what was actually drawn (x0, y0, x1, y1 in pixels)."""
        u0, u1 = self.cx + (box[0] - W / 2) / self.px, self.cx + (box[2] - W / 2) / self.px
        v0, v1 = self.cy - (box[3] - H / 2) / self.px, self.cy - (box[1] - H / 2) / self.px
        self.px = min(W * (1 - 2 * margin) / ((u1 - u0) or 1), H * (1 - 2 * margin) / ((v1 - v0) or 1))
        self.cx, self.cy = (u0 + u1) / 2, (v0 + v1) / 2

    def __call__(self, x, y, z):
        u, v = self.raw(x, y, z)
        return (W / 2 + (u - self.cx) * self.px, H / 2 - (v - self.cy) * self.px)

    def plane(self, centre, u, v):
        """A point on the camera-facing plane through `centre`: u metres along screen right, v metres up."""
        return self(centre[0] + u * self.right[0], centre[1] + v, centre[2] + u * self.right[2])


def motif_for(samples: list[dict]) -> str:
    still = max(math.dist(lerp(s['p0'], s['p1'], .5), lerp(samples[0]['p0'], samples[0]['p1'], .5)) for s in samples) \
        < 0.5 * samples[0]['radius']
    if not still:
        return 'sweep'
    return 'geyser' if samples[0]['angle'] >= 60 else 'blast'


def sample_at(samples: list[dict], t: float, lead: float = 0.0) -> dict | None:
    """The hitbox at fractional tick t, interpolated between its per-tick samples (one hit group). Up to `lead` ticks
    before the first sample it is extrapolated backwards along the swing, so a trail can start with a lead-in."""
    ticks = [s['tick'] for s in samples]
    if ticks[0] - lead <= t < ticks[0] and len(samples) > 1:
        a, b = samples[0], samples[1]
        k = (t - a['tick']) / max(b['tick'] - a['tick'], 1)     # negative: behind the first sample
        step = math.dist(lerp(a['p0'], a['p1'], .5), lerp(b['p0'], b['p1'], .5)) or 1e-3
        k = max(k, -1.5 * a['radius'] / step)                   # a huge first step must not throw a long streak
        return {**a, 'p0': lerp(a['p0'], b['p0'], k), 'p1': lerp(a['p1'], b['p1'], k)}
    if t < ticks[0] or t > ticks[-1]:
        return None
    i = max(k for k, tick in enumerate(ticks) if tick <= t)
    if i == len(samples) - 1:
        return samples[i]
    a, b = samples[i], samples[i + 1]
    k = (t - a['tick']) / max(b['tick'] - a['tick'], 1)
    return {**a, 'p0': lerp(a['p0'], b['p0'], k), 'p1': lerp(a['p1'], b['p1'], k)}


def chaikin(points: list[tuple]) -> list[tuple]:
    out = [points[0]]
    for a, b in zip(points, points[1:]):
        out += [lerp(a, b, .25), lerp(a, b, .75)]
    return out + [points[-1]]


def dot(a, b): return sum(p * q for p, q in zip(a, b))


def disc(d, view, centre, radius_m, fill=255, squash=1.0):
    x, y = view(*centre); r = radius_m * view.px
    d.ellipse([x - r, y - r * squash, x + r, y + r], fill=fill)


def draw_sweep(d: ImageDraw.ImageDraw, view: View, samples: list[dict], t: float, rng_seed: int) -> None:
    """A crescent along the blade's path: from the first active tick to now, between a point on the outer part of the
    hitbox segment and its far edge (the tip plus the radius, the real reach). Thick at the head, thin at the tail;
    after the last tick the tail catches up with the head, and a few sparks fly off along the swing."""
    first, last = samples[0]['tick'], samples[-1]['tick']
    if t < first:
        return
    head = min(t, last)
    lead = 2.0                                                  # ticks of lead-in: full size on the first active tick
    retreat = ease_in((t - last) / 10) if t > last else 0.0
    start = (first - lead) + (head - first + lead) * retreat
    steps = max(2, int((head - start) * 4))
    rims, edges = [], []
    for k in range(steps + 1):
        tt = start + (head - start) * k / steps
        s = sample_at(samples, tt, lead)
        if not s:
            continue
        age = (head - tt) / max(head - first + lead, 1e-3)       # 0 at the head, 1 at the tail
        a, b = s['p0'], s['p1']
        seg = math.dist(a, b) or 1e-3
        far = tuple(q + (q - p) / seg * s['radius'] for p, q in zip(a, b))   # the reach: tip plus the radius
        thick = (0.55 - 0.45 * age) * (1 - 0.5 * retreat)                   # how far in from the edge the band goes
        rims.append(lerp(far, a, max(0.06, thick)))
        edges.append(far)
    if len(rims) >= 2 and start < head:  # Chaikin smoothing in 3D: per-tick samples turn a fast swing into corners
        for _ in range(3):
            rims, edges = chaikin(rims), chaikin(edges)
        d.polygon([view(*q) for q in edges] + [view(*q) for q in rims[::-1]], fill=255)
    rng = random.Random(rng_seed)
    if t > last:
        end, before = samples[-1], samples[max(0, len(samples) - 3)]
        tip = end['p1']
        swing = tuple(q - p for p, q in zip(before['p1'], tip))
        norm = math.sqrt(dot(swing, swing)) or 1
        swing = tuple(c / norm for c in swing)
        for _ in range(6):
            sec = (t - last) / 60
            speed = rng.uniform(2.5, 5)
            jitter = tuple(c + rng.uniform(-0.5, 0.5) for c in swing)
            pos = (tip[0] + jitter[0] * speed * sec, tip[1] + jitter[1] * speed * sec - 0.5 * 9 * sec * sec,
                   tip[2] + jitter[2] * speed * sec)
            r = end['radius'] * 0.15 * (1 - ease_in((t - last) / 22))
            if r > 0.005:
                disc(d, view, pos, r)


def draw_geyser(d: ImageDraw.ImageDraw, view: View, s: dict, t: float, rng_seed: int) -> None:
    """Column to the hitbox top, floor ring out to its reach on the floor; t counted from the first active tick."""
    cx, cy, cz = s['p0']
    r = s['radius']
    floor = (cx, 0.0, cz)
    top = cy + r
    ring = math.sqrt(max(r * r - cy * cy, (0.6 * r) ** 2))
    rng = random.Random(rng_seed)
    drops = [dict(a=rng.uniform(0, 2 * math.pi), y=rng.uniform(0.75, 0.95) * top, v=rng.uniform(1.0, 1.6) * ring,
                  vy=rng.uniform(0.3, 1.5), t0=rng.uniform(5, 14), r=rng.uniform(0.035, 0.065) * top) for _ in range(14)]
    if t < 0:
        return
    rr = ring * (0.25 + 0.75 * ease_out(t / 7))
    fade = 1 - ease_in((t - 24) / 18)
    if fade > 0:  # floor ring: a real circle on the floor with small splashes standing on it
        circle = [view(cx + rr * math.cos(k / 48 * 2 * math.pi), 0.0, cz + rr * math.sin(k / 48 * 2 * math.pi)) for k in range(49)]
        d.line(circle, fill=200, width=max(4, int(0.03 * view.px)))
        for i in range(12):
            a = (i + 0.5) / 12 * 2 * math.pi
            base = (cx + rr * math.cos(a), 0.0, cz + rr * math.sin(a))
            h = (0.16 + 0.06 * math.sin(i * 2.3)) * top * fade * ease_out(t / 4)
            d.polygon([view.plane(base, -0.04, 0), view.plane(base, 0.04, 0), view.plane(base, 0, h)], fill=210)
    h = top * ease_out(t / 4) if t < 4 else top if t < 12 else max(0.0, top * (1 - ease_in((t - 12) / 18)))
    if h > 0.02 * top:  # column and crown on the camera-facing plane through the hitbox
        base, tip = 0.22 * top + 0.05 * top * ease_out(t / 12), 0.12 * top
        n = 10
        left = [view.plane(floor, -(base + (tip - base) * k / n) + 0.03 * top * math.sin(k * 1.7 + t * .5), h * k / n) for k in range(n + 1)]
        right = [view.plane(floor, base + (tip - base) * k / n + 0.03 * top * math.sin(k * 1.3 - t * .6 + 1), h * k / n) for k in range(n + 1)]
        d.polygon(left + right[::-1], fill=255)
        crown = ease_out((t - 3) / 6) * (1 - ease_in((t - 12) / 10))
        for i in range(7 if crown > 0.02 else 0):
            a = (i / 6 - 0.5) * 2.4
            tu, tv = math.sin(a) * (0.2 + 0.2 * crown) * top, h + math.cos(a) * 0.17 * crown * top
            d.polygon([view.plane(floor, math.sin(a) * .05 * top - .07 * top, h - .05 * top),
                       view.plane(floor, math.sin(a) * .05 * top + .07 * top, h - .05 * top), view.plane(floor, tu, tv)], fill=235)
            x, y = view.plane(floor, tu, tv); rad = 0.04 * top * crown * view.px
            d.ellipse([x - rad, y - rad, x + rad, y + rad], fill=235)
    for p in drops:  # droplets fly out in every direction and land on the ring
        sec = (t - p['t0']) / 60
        if sec < 0:
            continue
        dist = 0.1 * r + p['v'] * sec
        y = p['y'] + p['vy'] * sec - 0.5 * 24 * sec * sec
        if y > 0.01:
            disc(d, view, (cx + math.cos(p['a']) * dist, y, cz + math.sin(p['a']) * dist), p['r'], squash=1.25)


def draw_blast(d: ImageDraw.ImageDraw, view: View, s: dict, t: float, rng_seed: int, active: int) -> None:
    """Tapered spikes on the camera-facing plane through the hitbox, longer along the launch direction."""
    centre, r = s['p0'], s['radius']
    if t < 0 or t > active + 18:
        return
    grow = ease_out(t / max(2, active))
    fade = 1 - ease_in((t - active) / 18)
    bias = math.radians(s['angle'])
    if view.right[2] < 0:  # forward points screen-left from this camera: mirror the launch direction
        bias = math.pi - bias
    rng = random.Random(rng_seed)
    disc(d, view, centre, 0.35 * r * grow * fade)
    for i in range(11):
        a = (i / 11) * 2 * math.pi + rng.uniform(-0.15, 0.15)
        toward = 0.5 + 0.5 * math.cos(a - bias)
        length = r * (0.6 + 0.6 * toward) * grow * (0.4 + 0.6 * fade)
        w = 0.12 * r * fade
        d.polygon([view.plane(centre, -math.sin(a) * w, math.cos(a) * w), view.plane(centre, math.sin(a) * w, -math.cos(a) * w),
                   view.plane(centre, math.cos(a) * length, math.sin(a) * length)], fill=255)


def outline_capsule(d, view, p0, p1, r, colour, width=2):
    a, b = view(*p0), view(*p1); rr = r * view.px
    for c in (a, b):
        d.ellipse([c[0] - rr, c[1] - rr, c[0] + rr, c[1] + rr], outline=colour, width=width)
    dx, dy = b[0] - a[0], b[1] - a[1]; n = math.hypot(dx, dy)
    if n > 1:
        ox, oy = -dy / n * rr, dx / n * rr
        d.line([(a[0] + ox, a[1] + oy), (b[0] + ox, b[1] + oy)], fill=colour, width=width)
        d.line([(a[0] - ox, a[1] - oy), (b[0] - ox, b[1] - oy)], fill=colour, width=width)


def render(samples: list[dict], fighter: dict, out: Path, slow: float, motif: str | None, blur: float, lead: int, tail: int,
           seed: int, yaw: float, pitch: float) -> dict:
    groups: dict[int, list[dict]] = {}
    for s in sorted(samples, key=lambda s: s['tick']):
        groups.setdefault(s['hit'], []).append(s)
    motifs = {hit: motif or motif_for(g) for hit, g in groups.items()}
    first = min(s['tick'] for s in samples)
    last = max(s['tick'] for s in samples)
    t0, t1 = first - lead, last + tail
    ticks_per_frame = 60 / (FPS * slow)
    frames = int((t1 - t0) / ticks_per_frame)
    frames = frames // 4 * 4 + 1
    # framing: the fighter's capsule, every hitbox grown by its radius and the motifs' reach, nothing below the floor
    view = View(yaw, pitch)
    pts = [(sx * fighter['radius'], y, sz * fighter['radius']) for sx in (-1, 1) for sz in (-1, 1) for y in (0, fighter['height'])]
    for s in samples:
        if motifs[s['hit']] == 'sweep':  # the crescent's far edge: the tip plus the radius, wherever it goes
            a, b = s['p0'], s['p1']
            seg = math.dist(a, b) or 1e-3
            pts.append(tuple(q + (q - p) / seg * s['radius'] * 1.15 for p, q in zip(a, b)))
        grow = s['radius'] * 1.3
        for q in (s['p0'], s['p1']):
            for k in range(8):
                a = k / 8 * 2 * math.pi
                pts.append((q[0] + grow * math.cos(a), max(q[1] - grow, -0.1), q[2] + grow * math.sin(a)))
                pts.append((q[0] + grow * math.cos(a), q[1] + s['radius'] * (1.6 if motifs[s['hit']] == 'geyser' else 1.3),
                            q[2] + grow * math.sin(a)))
    view.fit(pts)

    def draw_frame(t):
        img = Image.new('L', (W, H), 0)
        d = ImageDraw.Draw(img)
        for hit, g in groups.items():
            local = t - g[0]['tick']
            if motifs[hit] == 'sweep':
                draw_sweep(d, view, g, t, seed + hit)
            elif motifs[hit] == 'geyser':
                draw_geyser(d, view, g[0], local, seed + hit)
            else:
                draw_blast(d, view, g[0], local, seed + hit, g[-1]['tick'] - g[0]['tick'] + 1)
        return img

    view.px *= 0.4  # zoomed out, so the measuring pass itself is never cut off by the frame
    box = None  # first pass: measure what the motifs really cover, plus the fighter, then refit so nothing is cut
    for f in range(frames):
        b = draw_frame(t0 + f * ticks_per_frame).getbbox()
        if b:
            box = b if not box else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    for q in pts[:8]:
        x, y = view(*q)
        box = (min(box[0], x), min(box[1], y), max(box[2], x), max(box[3], y)) if box else (x, y, x, y)
    pad = (blur * 2 + 6) * 0.4
    view.refit((box[0] - pad, box[1] - pad, box[2] + pad, box[3] + pad))
    for sub in ('masses', 'edges', 'check'):
        (out / sub).mkdir(parents=True, exist_ok=True)
    for f in range(frames):
        t = t0 + f * ticks_per_frame
        img = draw_frame(t)
        img.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 40 else 0).filter(ImageFilter.MaxFilter(3)) \
            .save(out / 'edges' / f'e{f:04d}.png')
        soft = img.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(blur)) if blur else img
        soft = soft.point(lambda v: min(255, int(v * 1.6)))
        soft.save(out / 'masses' / f'm{f:04d}.png')
        check = Image.merge('RGB', (soft, soft, soft))
        cd = ImageDraw.Draw(check)
        for radius in (1.0, 2.0, 3.0):  # floor rings every metre round the fighter's feet, to read reach
            cd.line([view(radius * math.cos(k / 64 * 2 * math.pi), 0, radius * math.sin(k / 64 * 2 * math.pi)) for k in range(65)],
                    fill=(70, 70, 70), width=1)
        cd.line([view(0, 0, 0), view(0, 0, 1.0)], fill=(90, 160, 90), width=2)  # facing
        outline_capsule(cd, view, (0, fighter['radius'], 0), (0, fighter['height'] - fighter['radius'], 0), fighter['radius'], (120, 120, 120))
        for s in samples:
            if s['tick'] <= t < s['tick'] + 1:
                outline_capsule(cd, view, s['p0'], s['p1'], s['radius'], (255, 60, 60), 3)
        cd.text((12, 10), f'tick {t:5.1f}', fill=(255, 220, 0))
        check.save(out / 'check' / f'c{f:04d}.png')
    for sub, prefix, name, crf in (('masses', 'm', 'masses.mp4', 10), ('edges', 'e', 'edges.mp4', 8), ('check', 'c', 'check.mp4', 18)):
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', str(FPS), '-i', str(out / sub / f'{prefix}%04d.png'),
                        '-pix_fmt', 'yuv420p', '-crf', str(crf), str(out / name)], check=True)
    meta = {'label': fighter.get('label'), 'motifs': {str(k): v for k, v in motifs.items()}, 'frames': frames,
            'firstTick': t0, 'ticksPerFrame': ticks_per_frame, 'slow': slow, 'pxPerMetre': view.px,
            'activeTicks': [first, last], 'size': [W, H], 'blur': blur, 'yaw': yaw, 'pitch': pitch,
            'note': 'check.mp4: floor rings every metre round the feet, green = facing, red = real hitboxes'}
    (out / 'blockout.json').write_text(json.dumps(meta, indent=2) + '\n')
    return meta


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='source', required=True)
    r = sub.add_parser('reach', help='a normal or aerial from a MoveDataReport --reach --json file')
    r.add_argument('json', type=Path)
    r.add_argument('slot', help='reach slot label, e.g. g1, a3')
    r.add_argument('--package', help="package id, to read each hit's launch angle from character.json")
    p = sub.add_parser('package', help='fixed-offset hitboxes from character.json')
    p.add_argument('package')
    p.add_argument('path', help='a slot id (ground.E) or a dotted path to a hitbox (shell.splash)')
    for q in (r, p):
        q.add_argument('--out', type=Path, required=True)
        q.add_argument('--motif', choices=['sweep', 'geyser', 'blast'])
        q.add_argument('--view', choices=sorted(VIEWS), default='three-quarter',
                       help='camera preset: yaw, pitch = ' + ', '.join(f'{k} {v[0]:g}/{v[1]:g}' for k, v in VIEWS.items()))
        q.add_argument('--yaw', type=float, help='override the preset yaw (0 = from his left, 90 = facing him)')
        q.add_argument('--pitch', type=float, help='override the preset pitch (degrees looking down)')
        q.add_argument('--slow', type=float, default=4.0)
        q.add_argument('--blur', type=float, default=10.0, help='mass blur in pixels (14 gave a dark halo; 0 = crisp)')
        q.add_argument('--lead', type=int, default=4, help='ticks before the first active tick')
        q.add_argument('--tail', type=int, default=30, help='ticks after the last active tick')
        q.add_argument('--seed', type=int, default=4)
    args = parser.parse_args()
    if args.source == 'reach':
        samples, fighter = from_reach(args.json, args.slot, args.package)
    else:
        samples, fighter = from_package(args.package, args.path)
    yaw, pitch = VIEWS[args.view]
    yaw = args.yaw if args.yaw is not None else yaw
    pitch = args.pitch if args.pitch is not None else pitch
    meta = render(samples, fighter, args.out, args.slow, args.motif, args.blur, args.lead, args.tail, args.seed, yaw, pitch)
    print(json.dumps({'out': str(args.out), 'frames': meta['frames'], 'motifs': meta['motifs'],
                      'activeTicks': meta['activeTicks'], 'view': [yaw, pitch]}))


if __name__ == '__main__':
    main()
