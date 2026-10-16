"""Original procedural textures for Log Off.

stump top (2048 x 2048, RGB): the fight surface, planar-mapped over x, z in
    [-TOP_EXTENT, TOP_EXTENT]. Growth rings around an off-centre pith, radial
    checks, faint saw arcs, sapwood/cambium/bark bands at the rim, moss and lichen
    near the edge and pale fresh wood along the split lips. Its outline comes from
    the layout, so the bark band sits exactly on the authoritative rim.
atlas (2048 x 2048, RGBA; alpha = StageNeon glow mask):
    endgrain  512 x 512 at (0, 0)       log rounds and split billets
    sign     1024 x 512 at (512, 0)     hand-painted NO WI-FI board
    phone     256 x 512 at (1536, 0)    NO SERVICE lock screen (glows)
    white      64 x 64 at (1984, 0)     solid white, alpha 1 (WHITE_UV in geo.py)
detail (512 x 512, grayscale, tileable): grain for StageVertexLit's triplanar detail.

Everything is drawn from code; text uses the shared stroke font.
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from stagekit import font
import layout as LAYOUT
import palette as P

TOP_SIZE = 2048
TOP_EXTENT = 15.5
PITH = (0.7, -0.9)
ATLAS = 2048
REGIONS = {"endgrain": (0, 0, 512, 512), "sign": (512, 0, 1024, 512), "phone": (1536, 0, 256, 512),
           "white": (1984, 0, 64, 64)}


def top_uv(x, z):
    """Stump-top texture coordinate of a world point (v grows north)."""
    return ((x + TOP_EXTENT) / (2 * TOP_EXTENT), (z + TOP_EXTENT) / (2 * TOP_EXTENT))


def region_uv(name, inset=2):
    """((u0, v0), (u1, v1)) of an atlas region with a small texel inset."""
    x0, y0, w, h = REGIONS[name]
    u0, u1 = (x0 + inset) / ATLAS, (x0 + w - inset) / ATLAS
    v1, v0 = 1 - (y0 + inset) / ATLAS, 1 - (y0 + h - inset) / ATLAS
    return (u0, v0), (u1, v1)


# ---------------------------------------------------------------- noise

def _hash(ix, iz, seed):
    h = (ix.astype(np.int64) * 374761393 + iz.astype(np.int64) * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float64) / 65535.0


def noise(x, z, seed, period=None):
    """Smooth value noise in [0, 1] at array coordinates (lattice spacing 1)."""
    x0 = np.floor(x)
    z0 = np.floor(z)
    tx = x - x0
    tz = z - z0
    tx = tx * tx * (3 - 2 * tx)
    tz = tz * tz * (3 - 2 * tz)
    ix, iz = x0.astype(np.int64), z0.astype(np.int64)

    px, pz = (period, period) if isinstance(period, (int, float)) else (period or (None, None))

    def h(a, b):
        if px:
            a = a % px
        if pz:
            b = b % pz
        return _hash(a, b, seed)

    a = h(ix, iz) + (h(ix + 1, iz) - h(ix, iz)) * tx
    b = h(ix, iz + 1) + (h(ix + 1, iz + 1) - h(ix, iz + 1)) * tx
    return a + (b - a) * tz


def fbm(x, z, seed, octaves=4, period=None):
    total = np.zeros_like(x, dtype=np.float64)
    amp, norm, f = 1.0, 0.0, 1.0
    for o in range(octaves):
        if period is None:
            p = None
        elif isinstance(period, (int, float)):
            p = period * int(f)
        else:
            p = (period[0] * int(f), period[1] * int(f))
        total += amp * noise(x * f, z * f, seed + 17 * o, p)
        norm += amp
        amp *= 0.5
        f *= 2.0
    return total / norm


def _mix(a, b, t):
    t = np.clip(t, 0.0, 1.0)[..., None]
    return a * (1 - t) + b * t


def _smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _c(rgb):
    return np.array(rgb, dtype=np.float64)


# ---------------------------------------------------------------- rings

def _ring_table(rng, max_r, fine_rate):
    """Cumulative ring boundaries: wider rings in youth, narrower towards the bark."""
    bounds, darkness = [0.0], []
    r = 0.0
    while r < max_r + 2:
        base = 1.0 / (1.8 + fine_rate * r)
        w = base * rng.uniform(0.7, 1.35)
        r += w
        bounds.append(r)
        darkness.append(rng.uniform(0.55, 1.0))
    return np.array(bounds), np.array(darkness)


def ring_colors(r, theta, rng, seed, fine_rate=0.12):
    """Earlywood/latewood colour for distance r from the pith (arrays)."""
    wobble = (1 + 0.03 * np.sin(3 * theta + 0.6) + 0.018 * np.sin(7 * theta + 1.3)
              + 0.01 * np.sin(13 * theta + 2.1))
    rw = r * wobble + 0.25 * (fbm(np.cos(theta) * 2.2 + r * 0.15, np.sin(theta) * 2.2 + r * 0.15, seed, 3) - 0.5)
    rw = np.maximum(rw, 0.0)
    bounds, dark = _ring_table(rng, float(rw.max()), fine_rate)
    k = np.clip(np.searchsorted(bounds, rw) - 1, 0, len(bounds) - 2)
    frac = (rw - bounds[k]) / (bounds[k + 1] - bounds[k])
    late = _smooth(0.62, 0.9, frac) * (1 - _smooth(0.96, 1.0, frac)) * dark[np.clip(k, 0, len(dark) - 1)]
    return _mix(_c(P.EARLYWOOD) * np.ones(r.shape + (3,)), _c(P.LATEWOOD), late * 0.85)


def _segment_distance(px, pz, a, b):
    ax, az = a
    bx, bz = b
    ex, ez = bx - ax, bz - az
    L2 = ex * ex + ez * ez
    t = np.clip(((px - ax) * ex + (pz - az) * ez) / L2, 0.0, 1.0)
    dx, dz = px - (ax + ex * t), pz - (az + ez * t)
    return np.sqrt(dx * dx + dz * dz)


def stump_top(path, spec=LAYOUT.SPEC, size=TOP_SIZE, seed=7):
    rng = random.Random(seed)
    n = size
    coords = (np.arange(n) + 0.5) / n * 2 * TOP_EXTENT - TOP_EXTENT
    X, Zrow = np.meshgrid(coords, coords)
    Z = -Zrow  # image rows run south from the top edge (v = 1 is north)

    # Rings around the pith.
    dx, dz = X - PITH[0], Z - PITH[1]
    r = np.hypot(dx, dz)
    theta = np.arctan2(dz, dx)
    col = ring_colors(r, theta, rng, seed)
    heart = 1 - _smooth(7.5, 10.5, r + 1.2 * (fbm(X * 0.3, Z * 0.3, seed + 3, 3) - 0.5))
    col = _mix(col, col * _c(P.HEARTWOOD) / _c(P.EARLYWOOD), heart * 0.55)
    col *= (1 - 0.18 * (1 - _smooth(0.0, 0.5, r)))[..., None]  # dark pith

    # Distance to the rim, measured from the stage centre with the layout's radius.
    rc = np.hypot(X, Z)
    tc = np.arctan2(Z, X)
    rim = spec["radius"] + sum(a * np.sin(k * tc) for k, a in spec["harmonics"])
    d_rim = rim - rc
    edge_noise = 0.12 * (fbm(tc * 9.0, rc * 0.4, seed + 5, 3) - 0.5)
    sap = _smooth(3.2, 1.6, d_rim)
    col = _mix(col, col * _c(P.SAPWOOD) / _c(P.EARLYWOOD), sap * 0.6)
    cambium = _smooth(1.05, 0.85, d_rim + edge_noise) * (1 - _smooth(0.78, 0.62, d_rim + edge_noise))
    col = _mix(col, _c(P.CAMBIUM), cambium * 0.9)
    bark = _smooth(0.66, 0.58, d_rim + edge_noise)
    bark_tex = _c(P.BARK_RIM) * (0.75 + 0.5 * fbm(tc * 40.0, rc * 2.0, seed + 9, 3))[..., None]
    col = _mix(col, bark_tex, bark)

    # Radial drying checks: thin wedges opening towards the bark, away from the axe split.
    for angle, start, end, width in ((208, 0.36, 0.86, 0.075), (262, 0.5, 0.9, 0.06),
                                     (326, 0.3, 0.78, 0.07), (24, 0.55, 0.88, 0.05)):
        a = math.radians(angle)
        ux, uz = math.cos(a), math.sin(a)
        along = dx * ux + dz * uz
        across = np.abs(-dx * uz + dz * ux + 0.1 * np.sin(along * 1.3 + angle) + 0.05 * np.sin(along * 4.1))
        r0, r1 = start * spec["radius"], end * spec["radius"]
        w = 0.008 + width * np.clip((along - r0) / (r1 - r0), 0, 1) ** 1.5
        mask = (along > r0) & (along < r1) & (across < w)
        col[mask] = col[mask] * 0.3 + _c(P.CHECK) * 0.7
        lip = (along > r0) & (along < r1) & (across >= w) & (across < w + 0.05)
        col[lip] = col[lip] * 0.82

    # Faint chainsaw arcs and weathering.
    saw = np.sin((X * 0.94 + Z * 0.34 + 0.6 * np.sin(Z * 0.21)) / 0.32 * 2 * math.pi)
    col *= (1 + 0.025 * saw)[..., None]
    weather = _smooth(0.45, 0.85, fbm(X * 0.18, Z * 0.18, seed + 11, 4)) * _smooth(5.0, 1.0, d_rim) * 0.35
    col = _mix(col, _c(P.WEATHERED), weather)
    grime = fbm(X * 1.4, Z * 1.4, seed + 13, 3)
    col *= (0.94 + 0.1 * grime)[..., None]

    # Pale fresh wood and splinters along the axe split.
    pts = LAYOUT.outline(spec)
    rim_count = spec["rim_segments"] + 1
    chain = [pts[rim_count - 1]] + pts[rim_count:] + [pts[0]]
    d_split = np.full(X.shape, 1e9)
    for i in range(len(chain) - 1):
        d_split = np.minimum(d_split, _segment_distance(X, Z, chain[i], chain[i + 1]))
    fresh = _smooth(0.55, 0.12, d_split + 0.15 * (fbm(X * 3.0, Z * 3.0, seed + 15, 2) - 0.5))
    col = _mix(col, _c(P.FRESH_SPLIT), fresh * 0.75)

    # Moss cushions and lichen near the rim (none on the split lips).
    moss = fbm(X * 0.35 + 7.0, Z * 0.35 - 3.0, seed + 21, 4)
    moss_mask = _smooth(0.6, 0.72, moss) * _smooth(2.6, 0.9, d_rim) * _smooth(0.4, 1.2, d_split)
    moss_col = _mix(_c(P.MOSS_DARK) * np.ones(X.shape + (3,)), _c(P.MOSS_LIGHT),
                    fbm(X * 3.0, Z * 3.0, seed + 23, 3))
    col = _mix(col, moss_col, moss_mask * 0.9)
    lichen = _smooth(0.74, 0.8, fbm(X * 2.4, Z * 2.4, seed + 25, 3)) * _smooth(1.0, 0.3, d_rim)
    col = _mix(col, _c(P.LICHEN), lichen * 0.7)

    img = np.clip(col * 255.0, 0, 255).astype(np.uint8)
    Image.fromarray(img, "RGB").save(path)
    return path


# ---------------------------------------------------------------- atlas

def _endgrain(size=512, seed=31):
    rng = random.Random(seed)
    c = (np.arange(size) + 0.5) / size * 2 - 1
    X, Z = np.meshgrid(c, -c)
    r = np.hypot(X - 0.04, Z + 0.03) * 7.0
    theta = np.arctan2(Z, X)
    col = ring_colors(r, theta, rng, seed, fine_rate=0.35)
    rr = np.hypot(X, Z)
    col = _mix(col, _c(P.CAMBIUM), _smooth(0.86, 0.9, rr) * (1 - _smooth(0.9, 0.93, rr)))
    bark = _c(P.BARK) * (0.75 + 0.5 * fbm(theta * 20.0, rr * 5.0, seed, 3))[..., None]
    col = _mix(col, bark, _smooth(0.9, 0.93, rr))
    for angle in (35, 160, 260):
        a = math.radians(angle)
        along = X * math.cos(a) + Z * math.sin(a)
        across = np.abs(-X * math.sin(a) + Z * math.cos(a))
        mask = (along > 0.08) & (along < 0.75) & (across < 0.012 * (1 - along))
        col[mask] *= 0.45
    rgba = np.concatenate([np.clip(col, 0, 1), np.zeros(col.shape[:2] + (1,))], axis=2)
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")


def _stroke_text(draw, text, height, center, width, color, tracking=1.05, wobble=None):
    strokes, w = font.text_strokes(text, height, origin=center, tracking=tracking)
    for line in strokes:
        pts = [(x, y) for x, y in line]
        if wobble is not None:
            pts = [(x + wobble.uniform(-0.6, 0.6), y + wobble.uniform(-0.6, 0.6)) for x, y in pts]
        draw.line(pts, fill=color, width=width, joint="curve")
        r = width / 2
        for x, y in (pts[0], pts[-1]):
            draw.ellipse((x - r, y - r, x + r, y + r), fill=color)
    return w


def _flip_strokes_canvas(img):
    """Font strokes use v up; draw on a flipped canvas and flip back."""
    return img.transpose(Image.Transpose.FLIP_TOP_BOTTOM)


def _sign(w=1024, h=512, seed=41):
    rng = random.Random(seed)
    ss = 2
    W, H = w * ss, h * ss
    # Three weathered planks with grain.
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    plank = np.floor(yy / (H / 3))
    grain = fbm(xx / W * 3.0 + plank * 5.0, yy / H * 40.0, seed, 4)
    base = _c(P.SIGN_BOARD) * (0.82 + 0.3 * grain)[..., None]
    seam = (np.abs((yy % (H / 3)) - 0) < 6 * ss) | (np.abs((yy % (H / 3)) - H / 3) < 6 * ss)
    base[seam] *= 0.45
    edge = np.minimum.reduce([xx, W - xx, yy, H - yy])
    base *= (0.75 + 0.25 * _smooth(0, 40 * ss, edge))[..., None]
    img = Image.fromarray((np.clip(base, 0, 1) * 255).astype(np.uint8), "RGB")
    # Text on a v-up canvas.
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    paint = tuple(int(c * 255) for c in P.SIGN_PAINT) + (255,)
    _stroke_text(d, "NO WI-FI", 115 * ss, (W / 2, 260 * ss), 30 * ss, paint, wobble=rng)
    _stroke_text(d, "JUST LOGS", 80 * ss, (W / 2, 100 * ss), 17 * ss, paint, tracking=1.15, wobble=rng)
    canvas = _flip_strokes_canvas(canvas)
    img.paste(canvas, (0, 0), canvas)
    # Nail heads.
    d2 = ImageDraw.Draw(img)
    for nx, ny in ((40, 40), (W / ss - 40, 40), (40, H / ss - 40), (W / ss - 40, H / ss - 40)):
        cx, cy = nx * ss, ny * ss
        d2.ellipse((cx - 9 * ss, cy - 9 * ss, cx + 9 * ss, cy + 9 * ss), fill=(70, 66, 62))
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    a = Image.new("L", (w, h), 0)
    img.putalpha(a)
    return img


def _phone(w=256, h=512):
    ss = 2
    W, H = w * ss, h * ss
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    t = yy / H
    top = _c((0.10, 0.12, 0.30))
    bottom = _c((0.42, 0.20, 0.36))
    col = top * (1 - t[..., None]) + bottom * t[..., None]
    img = Image.fromarray((col * 255).astype(np.uint8), "RGB").convert("RGBA")
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    white = (245, 242, 235, 255)
    # Canvas coordinates are v-up (flipped at the end).
    # Crossed-out signal bars (top left) and a nearly empty battery (top right).
    for i in range(4):
        x0 = 24 * ss + i * 13 * ss
        base = H - 60 * ss
        d.rectangle((x0, base, x0 + 8 * ss, base + (8 + i * 7) * ss), fill=(150, 150, 160, 255))
    d.line((18 * ss, H - 64 * ss, 78 * ss, H - 20 * ss), fill=(240, 70, 60, 255), width=5 * ss)
    d.rectangle((W - 74 * ss, H - 52 * ss, W - 24 * ss, H - 26 * ss), outline=white, width=3 * ss)
    d.rectangle((W - 22 * ss, H - 45 * ss, W - 17 * ss, H - 33 * ss), fill=white)
    d.rectangle((W - 70 * ss, H - 48 * ss, W - 64 * ss, H - 30 * ss), fill=(240, 70, 60, 255))
    # The clock "6:42": the font has no colon, so draw it in the space.
    clock_h = 60 * ss
    s = clock_h / 6.0
    baseline = H - 230 * ss
    _stroke_text(d, "6 42", clock_h, (W / 2, baseline), 9 * ss, white, tracking=1.0)
    cx = W / 2 - 2.8 * s
    for cy in (baseline + 1.5 * s, baseline + 4.5 * s):
        d.ellipse((cx - 6 * ss, cy - 6 * ss, cx + 6 * ss, cy + 6 * ss), fill=white)
    _stroke_text(d, "NO", 46 * ss, (W / 2, H - 330 * ss), 7 * ss, white)
    _stroke_text(d, "SERVICE", 30 * ss, (W / 2, H - 380 * ss), 5 * ss, white, tracking=1.08)
    # A pile of unread notifications nobody is reading.
    d.rounded_rectangle((W / 2 - 52 * ss, 64 * ss, W / 2 + 52 * ss, 110 * ss), radius=22 * ss, fill=(225, 60, 55, 255))
    _stroke_text(d, "99+", 26 * ss, (W / 2, 74 * ss), 5 * ss, white)
    canvas = _flip_strokes_canvas(canvas)
    img.alpha_composite(canvas)
    img = img.resize((w, h), Image.Resampling.LANCZOS)
    # Whole screen glows; the bezel is drawn by geometry.
    img.putalpha(Image.new("L", (w, h), 255))
    return img


def atlas(path):
    img = Image.new("RGBA", (ATLAS, ATLAS), (255, 255, 255, 0))
    img.paste(_endgrain(), REGIONS["endgrain"][:2])
    img.paste(_sign(), REGIONS["sign"][:2])
    img.paste(_phone(), REGIONS["phone"][:2])
    x0, y0, w, h = REGIONS["white"]
    img.paste(Image.new("RGBA", (w, h), (255, 255, 255, 255)), (x0, y0))
    img.save(path)
    return path


def detail(path, size=512, seed=53):
    """Tileable grain: fibres plus soft grit, mean near 0.5 (neutral for the shader)."""
    c = np.arange(size) / size
    X, Z = np.meshgrid(c, c)
    period = 8
    grit = fbm(X * period, Z * period, seed, 4, period=period)
    fibre = fbm(X * 4, Z * 48, seed + 7, 3, period=(4, 48))
    v = 0.5 + 0.55 * (grit - 0.5) + 0.35 * (fibre - 0.5)
    img = Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    img.save(path)
    return path


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/log-off-textures"
    import os
    os.makedirs(out, exist_ok=True)
    stump_top(os.path.join(out, "LogOffStumpTop.png"))
    atlas(os.path.join(out, "LogOffAtlas.png"))
    detail(os.path.join(out, "LogOffDetail.png"))
    print("textures written to", out)
