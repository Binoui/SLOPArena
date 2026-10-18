"""Original procedural textures for the Fighter's License deck (Pillow + numpy).

deck (4096 x 3072, RGB): the fight surface, planar over x -20..20 and z -15..15 (v grows
    north, about 1 cm per pixel). Warm asphalt with patches, slab joints and cracks; the
    painted test course: start bay and chequered line, parking bays, the jump course's
    approach boxes, the teal test ring with the department seal, the knockout box, the
    hazard stripe and slab lip along the open edge with the barrier's sheared post bases,
    skid marks to the edge, drains, oil and contact shade along the walls and blocks.
atlas (2048 x 2048, RGBA; alpha = StageNeon glow mask): road signs, wall lettering,
    billboards, the rooftop sign, stencils, windows, shutter, doors; see CELLS.
brick (512 x 512, tileable, 1.8 x 1.8 m): the neighbour's party wall.
concrete (1024 x 1024, tileable, 2.4 x 2.4 m): board-formed panels with tie holes.
detail (512 x 512, grayscale, tileable): grit for StageVertexLit's triplanar detail.

Lettering uses the project's Archivo Black and Space Mono (OFL, client/Unity/Assets/Fonts).
Everything else is drawn here. Floor text reads upright for the viewer named beside it.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

from stagekit.noise import fbm, mix as _mix, smooth as _smooth
import layout as LAYOUT
import palette as P

REPO = Path(__file__).resolve().parents[3]
FONTS = REPO / "client/Unity/Assets/Fonts"
DISPLAY = FONTS / "ArchivoBlack-Regular.ttf"
MONO = FONTS / "SpaceMono-Regular.ttf"

DECK_W, DECK_H = 4096, 3072
X0, X1, Z0, Z1 = -20.0, 20.0, -15.0, 15.0
S = DECK_W / (X1 - X0)  # pixels per metre

# Painted features shared with the geometry (world metres).
START_BOX = (-9.4, -15.0, -2.6, -8.6)          # x0, z0, x1, z1; the chequered line is its north end
BAY_LINES = [-19.0, -15.8, -12.6, -2.6, 0.6, 3.8, 7.0, 10.2, 13.4]
BAY_DEPTH = 4.4
RING_CENTER, RING_RADIUS = (7.0, -2.0), 6.0
KO_CENTER, KO_HALF = (15.0, -2.0), 1.5
EDGE_STRIPE = (19.0, 19.8)                      # hazard stripe x range; the slab lip runs to the edge
POST_BASES = [z for z in np.arange(-12.5, 12.6, 2.5)]
SKID = [(2.8, 5.6), (7.0, 9.4), (12.5, 12.6), (19.95, 12.9)]


def deck_uv(x, z):
    return ((x - X0) / (X1 - X0), (z - Z0) / (Z1 - Z0))


def _px(x, z):
    return ((x - X0) * S, (Z1 - z) * S)


def _rgb(c, a=255):
    return tuple(int(round(min(1.0, max(0.0, v)) * 255)) for v in c[:3]) + (a,)


def _c(rgb):
    return np.array(rgb[:3], dtype=np.float32)


def font(px_size, mono=False):
    return ImageFont.truetype(str(MONO if mono else DISPLAY), max(6, int(px_size)))


def _cap_font(cap_px, mono=False):
    """A font whose capital height is about cap_px pixels."""
    f = font(cap_px, mono)
    l, t, r, b = f.getbbox("H")
    return font(cap_px * cap_px / max(1, b - t), mono)


def _text_image(text, cap_px, color, mono=False, tracking=0.0, stroke=0, stroke_color=None):
    """Tight RGBA image of a single line of text (capital height cap_px)."""
    f = _cap_font(cap_px, mono)
    widths = [f.getlength(ch) for ch in text]
    total = sum(widths) + tracking * cap_px * max(0, len(text) - 1)
    pad = int(cap_px * 0.4) + stroke * 2
    img = Image.new("RGBA", (int(total) + pad * 2, int(cap_px * 1.9) + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x = pad
    for ch, w in zip(text, widths):
        d.text((x, pad + cap_px * 1.25), ch, font=f, fill=color, anchor="ls", stroke_width=stroke,
               stroke_fill=stroke_color)
        x += w + tracking * cap_px
    return img.crop(img.getbbox() or (0, 0, 1, 1))


def _paste_center(layer, img, cx, cy, angle=0.0):
    if angle:
        img = img.rotate(angle, resample=Image.BICUBIC, expand=True)
    layer.alpha_composite(img, (int(round(cx - img.width / 2)), int(round(cy - img.height / 2))))


def floor_text(layer, text, x, z, height, color, angle=0.0, mono=False, tracking=0.04):
    """Paint text centred at world (x, z), capitals `height` metres tall. angle 0 reads from
    the south (letters' tops point north), 90 from the east, -90 from the west, 180 from the north."""
    img = _text_image(text, height * S, _rgb(color), mono=mono, tracking=tracking)
    _paste_center(layer, img, *_px(x, z), angle)


# ---------------------------------------------------------------- deck

def _grid(W, H, q=1):
    xs = X0 + (np.arange(W // q) + 0.5) / (W // q) * (X1 - X0)
    zs = Z1 - (np.arange(H // q) + 0.5) / (H // q) * (Z1 - Z0)
    return np.meshgrid(xs.astype(np.float32), zs.astype(np.float32))


def _up(a, size):
    return np.asarray(Image.fromarray(np.asarray(a, dtype=np.float32), "F").resize(size, Image.BICUBIC))


def _box_distance(X, Z, x0, z0, x1, z1):
    dx = np.maximum(np.maximum(x0 - X, X - x1), 0.0)
    dz = np.maximum(np.maximum(z0 - Z, Z - z1), 0.0)
    return np.hypot(dx, dz)


def _box_sdf(X, Z, x0, z0, x1, z1):
    """Signed distance to a rectangle: negative inside."""
    depth = np.minimum(np.minimum(X - x0, x1 - X), np.minimum(Z - z0, z1 - Z))
    return np.where(depth > 0, -depth, _box_distance(X, Z, x0, z0, x1, z1))


def deck(path, spec=LAYOUT.SPEC, seed=7):
    rng = random.Random(seed)
    nrng = np.random.default_rng(seed)
    W, H = DECK_W, DECK_H

    # Low-frequency fields at quarter resolution, upsampled.
    LX, LZ = _grid(W, H, 4)
    big = _up(fbm(LX * 0.11, LZ * 0.11, seed, 4), (W, H))
    mid = _up(fbm(LX * 0.75, LZ * 0.75, seed + 1, 3), (W, H))
    bleach = _up(_smooth(0.46, 0.80, fbm(LX * 0.05 + 3.3, LZ * 0.05, seed + 2, 3)), (W, H))
    wear_n = _up(fbm(LX * 1.7, LZ * 1.7, seed + 3, 4), (W, H))
    X, Z = _grid(W, H)

    col = np.empty((H, W, 3), dtype=np.float32)
    col[:] = _c(P.ASPHALT)
    col = _mix(col, _c(P.ASPHALT_LIGHT), bleach * 0.55)
    tone = 0.90 + 0.18 * big + 0.10 * (mid - 0.5)
    # Fine grit comes from the material's triplanar detail map; the texture keeps only sparse
    # aggregate stones so it stays small enough to commit (PNG cannot compress pixel noise).
    stones = nrng.random((H, W), dtype=np.float32)
    grain = np.where(stones < 0.004, np.float32(1.16), np.where(stones > 0.997, np.float32(0.84), np.float32(1.0)))
    col *= (tone * grain)[..., None]

    # Repair patches: darker, smoother asphalt inside a sealed seam.
    for x0, z0, x1, z1 in ((-17.6, 2.4, -14.6, 4.6), (4.2, 9.6, 7.0, 13.6), (15.2, 5.0, 17.8, 8.1), (-19.2, -9.6, -17.0, -7.4)):
        jag = 0.06 * (_up(fbm(LX * 2.5, LZ * 2.5, seed + int(x0 * 10), 2), (W, H)) - 0.5)
        d = _box_sdf(X, Z, x0, z0, x1, z1) + jag
        inside = d <= 0.0
        patch = _c(P.ASPHALT_DARK) * (0.96 + 0.08 * mid)[..., None] * grain[..., None] ** 0.5
        col = np.where(inside[..., None], _mix(col, patch, np.full(X.shape, 0.6, np.float32)), col)
        col[(d <= 0.0) & (d > -0.028)] = _c(P.SEALANT) * 1.25

    # The driving aisle in front of the bays is polished darker by tyres, streaked along x.
    streak = _up(fbm(LX * 0.18, LZ * 6.0, seed + 4, 3), (W, H))
    aisle = np.exp(-((Z + 8.6) / 1.8) ** 2) * (0.6 + 0.8 * (streak - 0.5))
    col *= (1 - 0.10 * np.clip(aisle, 0, 1))[..., None]

    # Oil drips in the middle of each parking bay.
    for a, b in zip(BAY_LINES[:-1], BAY_LINES[1:]):
        if b - a > 4.0:
            continue
        cx, cz = (a + b) / 2 + rng.uniform(-0.3, 0.3), -15.0 + BAY_DEPTH * 0.55
        r = np.hypot((X - cx) / 0.9, (Z - cz) / 1.3) + 0.35 * (mid - 0.5)
        col *= (1 - 0.32 * _smooth(1.0, 0.2, r))[..., None]

    paint = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    _paint_markings(paint, rng)
    pa = np.asarray(paint).astype(np.float32) / 255.0
    # Paint wear: blotchy loss where traffic runs, plus tiny chips everywhere.
    lanes = np.exp(-((Z + 6.0) / 4.0) ** 2) * 0.35 + 0.65 * _smooth(0.55, 0.85, wear_n)
    chips = (nrng.random((H, W), dtype=np.float32) < 0.035).astype(np.float32)
    keep = np.clip(1.0 - 0.55 * lanes - 0.6 * chips, 0.12, 1.0)
    alpha = pa[..., 3] * keep
    painted = pa[..., :3] * (0.92 + 0.10 * tone[..., None] * grain[..., None])
    col = _mix(col, painted, alpha)

    # Slab joints (cut through the paint), sealed dark with a pale arris.
    for x in (-10.0, 0.0, 10.0):
        d = np.abs(X - x)
        col = np.where((d < 0.012)[..., None], _c(P.SEALANT), col)
        col = np.where(((d >= 0.012) & (d < 0.022))[..., None], col * 1.12, col)
    for z in (0.0,):
        d = np.abs(Z - z)
        col = np.where((d < 0.012)[..., None], _c(P.SEALANT), col)
        col = np.where(((d >= 0.012) & (d < 0.022))[..., None], col * 1.12, col)

    # Contact shade along the walls and around the course; the barred fence shades less.
    x0, z0, x1, z1 = spec["deck"]
    shade = np.ones(X.shape, np.float32)
    shade *= 1 - 0.34 * np.exp(-(z1 - Z) / 0.35)
    shade *= 1 - 0.34 * np.exp(-(X - x0) / 0.35)
    shade *= 1 - 0.16 * np.exp(-(Z - z0) / 0.30)
    for r in spec["raised"].values():
        d = _box_distance(X, Z, *r["footprint"])
        shade *= 1 - 0.42 * np.exp(-d / 0.22) * (d > 0)
        scuff = _smooth(0.6, 0.75, mid) * np.exp(-d / 1.2) * (d > 0)
        shade *= 1 - 0.12 * scuff
    col *= shade[..., None]

    img = Image.fromarray(np.clip(col * 255.0, 0, 255).astype(np.uint8), "RGB")
    _decals(img, rng)  # an RGBA Draw on an RGB image blends translucent fills
    img.save(path, optimize=True)
    return path


def _rect(d, x0, z0, x1, z1, color):
    (a, b), (c, e) = _px(x0, z1), _px(x1, z0)
    d.rectangle((a, b, c, e), fill=color)


def _outline(d, x0, z0, x1, z1, w, color):
    _rect(d, x0, z0, x1, z0 + w, color)
    _rect(d, x0, z1 - w, x1, z1, color)
    _rect(d, x0, z0, x0 + w, z1, color)
    _rect(d, x1 - w, z0, x1, z1, color)


def _poly(d, pts, color):
    d.polygon([_px(x, z) for x, z in pts], fill=color)


def _arrow(d, x, z, length, width, angle, color):
    """Road arrow: tail at (x, z), pointing along `angle` (radians, 0 = north)."""
    ux, uz = math.sin(angle), math.cos(angle)
    rx, rz = uz, -ux
    shaft = length * 0.62
    w, hw = width * 0.32, width
    pts = [(-w, 0), (-w, shaft), (-hw, shaft), (0, length), (hw, shaft), (w, shaft), (w, 0)]
    _poly(d, [(x + rx * a + ux * b, z + rz * a + uz * b) for a, b in pts], color)


def _paint_markings(layer, rng):
    d = ImageDraw.Draw(layer)
    white, yellow, teal = _rgb(P.PAINT_WHITE), _rgb(P.PAINT_YELLOW), _rgb(P.PAINT_TEAL)
    black, red = _rgb(P.PAINT_BLACK), _rgb(P.PAINT_RED)

    # Open edge: hazard stripe, then the slab lip (drawn in _decals).
    a, b = EDGE_STRIPE
    _rect(d, a, Z0, b, Z1, yellow)
    period = 0.5
    z = Z0 - 1.0
    while z < Z1 + 1.0:
        _poly(d, [(a, z), (b, z + (b - a)), (b, z + (b - a) + period / 2), (a, z + period / 2)], black)
        z += period

    # Parking bays along the safety fence, numbered for drivers backing in (read from the north).
    number = 1
    for x in BAY_LINES:
        if START_BOX[0] - 0.01 <= x <= START_BOX[2] + 0.01:
            continue
        _rect(d, x - 0.06, Z0, x + 0.06, Z0 + BAY_DEPTH, white)
    for a_, b_ in zip(BAY_LINES[:-1], BAY_LINES[1:]):
        if b_ - a_ > 4.0:
            continue
        cx = (a_ + b_) / 2
        if number == 2:
            floor_text(layer, "EXAMINER", cx, Z0 + 2.2, 0.34, P.PAINT_YELLOW, angle=180)
            floor_text(layer, "ONLY", cx, Z0 + 1.65, 0.34, P.PAINT_YELLOW, angle=180)
        elif number == 6:
            # No parking: diagonal hatch filling the bay.
            hatch = Image.new("RGBA", layer.size, (0, 0, 0, 0))
            hd = ImageDraw.Draw(hatch)
            x0, x1, z0, z1 = a_ + 0.25, b_ - 0.25, Z0 + 0.3, Z0 + BAY_DEPTH - 0.3
            _outline(hd, x0, z0, x1, z1, 0.1, yellow)
            s = z0 - (x1 - x0)
            while s < z1:
                _poly(hd, [(x0, s), (x1, s + (x1 - x0)), (x1, s + (x1 - x0) + 0.12), (x0, s + 0.12)], yellow)
                s += 0.55
            mask = Image.new("L", layer.size, 0)
            ImageDraw.Draw(mask).rectangle((*_px(x0, z1), *_px(x1, z0)), fill=255)
            layer.paste(hatch, (0, 0), Image.fromarray(np.minimum(np.asarray(hatch)[..., 3], np.asarray(mask))))
        floor_text(layer, "%02d" % number, cx, Z0 + BAY_DEPTH - 0.55, 0.42, P.PAINT_WHITE, angle=180)
        number += 1

    # Start bay: yellow outline, chequered start line at its north end, START ahead of it.
    x0, z0, x1, z1 = START_BOX
    _outline(d, x0, z0, x1, z1, 0.15, yellow)
    sq = 0.34
    n = int(round((x1 - x0 - 0.3) / sq))
    sq = (x1 - x0 - 0.3) / n
    for row in range(2):
        for i in range(n):
            c = white if (i + row) % 2 == 0 else black
            _rect(d, x0 + 0.15 + i * sq, z1 - 0.15 - (row + 1) * sq, x0 + 0.15 + (i + 1) * sq, z1 - 0.15 - row * sq, c)
    floor_text(layer, "START", (x0 + x1) / 2, z1 + 1.05, 1.15, P.PAINT_WHITE, tracking=0.08)
    floor_text(layer, "LICENSE TEST", (x0 + x1) / 2, z0 + 1.4, 0.4, P.PAINT_YELLOW, tracking=0.08)

    # Yellow kerb lines along the closed walls.
    _rect(d, X0 + 0.35, Z1 - 0.47, EDGE_STRIPE[0] - 0.4, Z1 - 0.35, yellow)
    _rect(d, X0 + 0.35, Z0 + BAY_DEPTH + 0.3, X0 + 0.47, Z1 - 0.35, yellow)

    # Jump course: an approach box before each obstacle with its height, and a dashed lane.
    labels = {"Block15": "1.5 M", "Block30": "3 M", "LookoutTower": "5.5 M"}
    for name, r in LAYOUT.SPEC["raised"].items():
        bx0, bz0, bx1, bz1 = r["footprint"]
        _outline(d, bx0, bz0 - 2.4, bx1, bz0 - 0.25, 0.12, yellow)
        floor_text(layer, labels[name], (bx0 + bx1) / 2, bz0 - 1.75, 0.55, P.PAINT_YELLOW)
        _arrow(d, (bx0 + bx1) / 2, bz0 - 1.15, 0.75, 0.42, 0.0, white)
    x = -16.0
    while x < 2.6:
        _rect(d, x, 5.22, min(x + 1.0, 2.6), 5.34, white)
        x += 1.6
    floor_text(layer, "JUMP COURSE", -16.6, 5.95, 0.36, P.PAINT_WHITE, angle=0)

    # Test ring: teal band, a thin inner line, ticks and the department seal in the middle.
    cx, cz = RING_CENTER
    pxc, pyc = _px(cx, cz)
    for r0, r1, c in ((RING_RADIUS - 0.32, RING_RADIUS, teal), (RING_RADIUS - 0.72, RING_RADIUS - 0.64, teal)):
        d.ellipse((pxc - r1 * S, pyc - r1 * S, pxc + r1 * S, pyc + r1 * S), fill=c)
        d.ellipse((pxc - r0 * S, pyc - r0 * S, pxc + r0 * S, pyc + r0 * S), fill=(0, 0, 0, 0))
    for k in range(24):
        a = 2 * math.pi * k / 24
        ux, uz = math.sin(a), math.cos(a)
        r0 = RING_RADIUS - (1.0 if k % 6 == 0 else 0.62)
        w = 0.05 if k % 6 else 0.08
        _poly(d, [(cx + ux * r0 - uz * w, cz + uz * r0 + ux * w), (cx + ux * (RING_RADIUS - 0.3) - uz * w, cz + uz * (RING_RADIUS - 0.3) + ux * w),
                  (cx + ux * (RING_RADIUS - 0.3) + uz * w, cz + uz * (RING_RADIUS - 0.3) - ux * w), (cx + ux * r0 + uz * w, cz + uz * r0 - ux * w)], teal)
    _seal(layer, cx, cz)

    # Knockout box: yellow frame and hatch where the dummy waits at 100%.
    kx, kz = KO_CENTER
    h = KO_HALF
    hatch = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    hd = ImageDraw.Draw(hatch)
    s = kz - h - 2 * h
    while s < kz + h:
        _poly(hd, [(kx - h, s), (kx + h, s + 2 * h), (kx + h, s + 2 * h + 0.16), (kx - h, s + 0.16)], yellow)
        s += 0.5
    mask = Image.new("L", layer.size, 0)
    ImageDraw.Draw(mask).rectangle((*_px(kx - h + 0.3, kz + h - 0.3), *_px(kx + h - 0.3, kz - h + 0.3)), fill=255)
    ImageDraw.Draw(mask).ellipse((*_px(kx - 0.75, kz + 0.75), *_px(kx + 0.75, kz - 0.75)), fill=0)
    layer.paste(hatch, (0, 0), Image.fromarray(np.minimum(np.asarray(hatch)[..., 3], np.asarray(mask))))
    _outline(d, kx - h, kz - h, kx + h, kz + h, 0.16, yellow)
    floor_text(layer, "KO ZONE", kx + h + 0.6, kz, 0.42, P.PAINT_YELLOW, angle=-90, tracking=0.08)
    floor_text(layer, "KO", kx, kz, 0.62, P.PAINT_WHITE, angle=-90)

    # A lane arrow through the open run space, and a stop bar facing the edge.
    _arrow(d, -15.6, -4.8, 3.2, 0.9, 0.0, white)
    _rect(d, 17.9, -9.0, 18.25, -4.6, white)
    floor_text(layer, "STOP", 17.3, -6.8, 0.6, P.PAINT_WHITE, angle=-90, tracking=0.06)


def _seal(layer, cx, cz):
    """The Department of Fighting seal: two rings, curved lettering and a license card."""
    d = ImageDraw.Draw(layer)
    white = _rgb(P.PAINT_WHITE)
    pxc, pyc = _px(cx, cz)
    for r, w in ((2.05, 0.08), (1.55, 0.05)):
        d.ellipse((pxc - r * S, pyc - r * S, pxc + r * S, pyc + r * S), outline=white, width=max(1, int(w * S)))
    cap = 0.2
    for text, centre_deg, sign in (("DEPARTMENT OF FIGHTING", 0.0, 1), ("TEST RING", 180.0, -1)):
        f = _cap_font(cap * S)
        widths = [f.getlength(ch) / S * 1.08 for ch in text]
        radius = 1.80
        total = sum(widths) / radius  # radians
        a = math.radians(centre_deg) - sign * total / 2
        for ch, w in zip(text, widths):
            mid = a + sign * (w / radius) / 2
            x, z = cx + radius * math.sin(mid), cz + radius * math.cos(mid)
            img = _text_image(ch, cap * S, white) if ch != " " else None
            if img is not None:
                # Top arc: letters' tops point outward; bottom arc: inward, so both read from the south.
                angle = -math.degrees(mid) if sign > 0 else 180 - math.degrees(mid)
                _paste_center(layer, img, *_px(x, z), angle)
            a += sign * w / radius
    for star_deg in (90.0, 270.0):
        a = math.radians(star_deg)
        _star(d, cx + 1.8 * math.sin(a), cz + 1.8 * math.cos(a), 0.16, white)
    # License card: rounded card, head and shoulders, three lines of text.
    w, h = 1.36, 0.9
    (a0, b0), (a1, b1) = _px(cx - w / 2, cz + h / 2), _px(cx + w / 2, cz - h / 2)
    d.rounded_rectangle((a0, b0, a1, b1), radius=int(0.1 * S), outline=white, width=int(0.06 * S))
    hx, hz = cx - 0.32, cz + 0.08
    d.ellipse((*_px(hx - 0.14, hz + 0.18), *_px(hx + 0.14, hz - 0.1)), fill=white)
    d.chord((*_px(hx - 0.27, hz - 0.08), *_px(hx + 0.27, hz - 0.5)), 180, 360, fill=white)
    for k in range(3):
        z = cz + 0.2 - k * 0.2
        _rect(d, cx - 0.05, z - 0.035, cx + 0.5 - (0.14 if k == 2 else 0.0), z + 0.035, white)


def _star(d, cx, cz, r, color):
    pts = []
    for k in range(10):
        a = math.pi * k / 5
        rr = r if k % 2 == 0 else r * 0.42
        pts.append((cx + rr * math.sin(a), cz + rr * math.cos(a)))
    _poly(d, pts, color)


def _decals(img, rng):
    """Opaque details over the painted deck: slab lip, sheared posts, skid marks, drains, cracks."""
    d = ImageDraw.Draw(img, "RGBA")
    # The slab lip at the open edge, with the guardrail's sheared post bases and rust runs.
    _rect(d, EDGE_STRIPE[1], Z0, X1, Z1, _rgb(P.CURB))
    lx0, lx1 = EDGE_STRIPE[1], X1
    for z in POST_BASES:
        cx = (lx0 + lx1) / 2
        _rect(d, cx - 0.09, z - 0.11, cx + 0.09, z + 0.11, _rgb(P.STEEL_DARK))
        _rect(d, cx - 0.06, z - 0.06, cx + 0.06, z + 0.06, _rgb((0.40, 0.30, 0.22)))
        for bx, bz in ((-0.05, -0.08), (0.05, -0.08), (-0.05, 0.08), (0.05, 0.08)):
            x, y = _px(cx + bx, z + bz)
            d.ellipse((x - 2.5, y - 2.5, x + 2.5, y + 2.5), fill=_rgb((0.62, 0.62, 0.60)))
        for k in range(3):
            x, y = _px(cx - 0.08 - rng.uniform(0, 0.12), z + rng.uniform(-0.1, 0.1))
            d.line((x, y, x - rng.uniform(20, 55), y + rng.uniform(-8, 8)), fill=_rgb(P.RUST, 120), width=3)
    # Skid marks: two tyre tracks swerving to the edge where the barrier gave way. Brush dabs
    # every 3 cm build up darker rubber where the car braked hardest; light tread lines run along.
    path = _resample(_bezier(SKID, 400), 0.03)
    for side in (-0.8, 0.8):
        track = []
        for i in range(len(path)):
            a_, b_ = path[max(0, i - 1)], path[min(len(path) - 1, i + 1)]
            tx, tz = b_[0] - a_[0], b_[1] - a_[1]
            L = math.hypot(tx, tz) or 1.0
            nx, nz = -tz / L, tx / L
            wob = 0.05 * math.sin(i * 0.011 + side) + 0.02 * math.sin(i * 0.07)
            track.append((path[i][0] + nx * (side + wob), path[i][1] + nz * (side + wob), nx, nz))
        n = len(track)
        r = 0.11 * S
        for i, (x, z, nx, nz) in enumerate(track):
            t = i / (n - 1)
            strength = (0.10 + 0.16 * min(1.0, t * 1.8)) * (0.75 + 0.25 * math.sin(i * 0.05 + side * 3))
            px_, py_ = _px(x, z)
            d.ellipse((px_ - r, py_ - r, px_ + r, py_ + r), fill=_rgb(P.TYRE, int(255 * strength)))
        for off in (-0.06, 0.0, 0.06):
            line = [_px(x + nx * off, z + nz * off) for x, z, nx, nz in track[::4]]
            d.line(line, fill=_rgb(P.ASPHALT, 45), width=2)
    # Drains: slotted cast-iron grates.
    for cx, cz in ((-17.2, 1.2), (2.6, -6.6), (12.4, 7.0)):
        _rect(d, cx - 0.24, cz - 0.24, cx + 0.24, cz + 0.24, _rgb((0.20, 0.19, 0.18)))
        for k in range(6):
            x = cx - 0.17 + k * 0.068
            _rect(d, x, cz - 0.18, x + 0.03, cz + 0.18, _rgb((0.08, 0.08, 0.08)))
    # Cracks: thin sealed lines wandering from the joints and the edge.
    for _ in range(34):
        x, z = rng.uniform(X0 + 0.5, X1 - 1.4), rng.uniform(Z0 + 0.5, Z1 - 0.5)
        a = rng.uniform(0, 2 * math.pi)
        line = [_px(x, z)]
        for _k in range(rng.randint(6, 22)):
            a += rng.uniform(-0.6, 0.6)
            step = rng.uniform(0.08, 0.22)
            x, z = x + step * math.sin(a), z + step * math.cos(a)
            line.append(_px(x, z))
        d.line(line, fill=_rgb(P.SEALANT, 95), width=2)


def _resample(pts, step):
    """Points every `step` metres along a polyline."""
    out = [pts[0]]
    carry = 0.0
    for a, b in zip(pts, pts[1:]):
        L = math.dist(a, b)
        t = step - carry
        while t <= L:
            out.append((a[0] + (b[0] - a[0]) * t / L, a[1] + (b[1] - a[1]) * t / L))
            t += step
        carry = L - (t - step)
    return out


def _bezier(ctrl, n):
    out = []
    for i in range(n + 1):
        t = i / n
        pts = list(ctrl)
        while len(pts) > 1:
            pts = [(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t) for a, b in zip(pts, pts[1:])]
        out.append(pts[0])
    return out


# ---------------------------------------------------------------- atlas

ATLAS = 2048
CELLS = {}
for _i, _n in enumerate(["sign_jump", "sign_double", "sign_fastfall", "sign_edge", "sign_shield", "sign_grab", "sign_stop"]):
    CELLS[_n] = (_i * 256, 0, 256, 256)
CELLS["white"] = (1984, 0, 64, 64)
CELLS["sign_exit"] = (1792, 64, 192, 96)
CELLS["sign_noentry"] = (1792, 160, 96, 96)
CELLS["sign_tickets"] = (0, 256, 512, 192)
CELLS["sign_examiner"] = (512, 256, 512, 192)
CELLS["sign_equipment"] = (1024, 256, 512, 128)
CELLS["sign_stairs"] = (1024, 384, 256, 64)
CELLS["sign_pay"] = (1280, 384, 256, 64)
CELLS["sign_height"] = (1536, 256, 512, 192)
CELLS["fascia"] = (0, 448, 2048, 192)
CELLS["facade_letters"] = (0, 640, 2048, 256)
for _i, _n in enumerate(["bb_psa", "bb_cola", "bb_motors", "bb_burger"]):
    CELLS[_n] = (_i * 512, 896, 512, 256)
CELLS["big_sign"] = (0, 1152, 2048, 320)
CELLS["ghost"] = (0, 1472, 1024, 320)
CELLS["stencil_15"] = (1024, 1472, 256, 160)
CELLS["stencil_30"] = (1280, 1472, 256, 160)
CELLS["stencil_55"] = (1536, 1472, 256, 160)
CELLS["examiner_glass"] = (1792, 1472, 256, 160)
CELLS["glass"] = (1024, 1632, 256, 160)
CELLS["shutter"] = (1280, 1632, 256, 160)
CELLS["window_office"] = (1536, 1632, 256, 160)
CELLS["window_apt"] = (1792, 1632, 128, 160)
CELLS["window_apt_lit"] = (1920, 1632, 128, 160)
CELLS["door_glass"] = (0, 1792, 256, 256)
CELLS["door_steel"] = (256, 1792, 128, 256)
CELLS["booth_window"] = (384, 1792, 256, 256)
CELLS["ticket_machine"] = (640, 1792, 128, 256)
CELLS["cone_cage"] = (768, 1792, 256, 256)
CELLS["hazard"] = (1024, 1792, 512, 64)
CELLS["fire_hose"] = (1024, 1856, 128, 192)
CELLS["panel_box"] = (1152, 1856, 128, 192)
CELLS["window_shop"] = (1280, 1856, 256, 192)
CELLS["sign_dept"] = (1536, 1792, 512, 256)


def uv(name, inset=2):
    """((u0, v0), (u1, v1)) of an atlas cell with a small texel inset."""
    x, y, w, h = CELLS[name]
    u0, u1 = (x + inset) / ATLAS, (x + w - inset) / ATLAS
    v1, v0 = 1 - (y + inset) / ATLAS, 1 - (y + h - inset) / ATLAS
    return (u0, v0), (u1, v1)


def _fit(d, text, box, color, mono=False, anchor="mm", start=None, stroke=0, stroke_fill=None):
    x0, y0, x1, y1 = box
    size = start or (y1 - y0)
    while size > 6:
        f = font(size, mono)
        l, t, r, b = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
        if r - l <= x1 - x0 and b - t <= y1 - y0:
            break
        size = int(size * 0.94)
    d.text(((x0 + x1) / 2, (y0 + y1) / 2), text, font=f, fill=color, anchor=anchor, stroke_width=stroke, stroke_fill=stroke_fill)


def _figure(d, cx, cy, s, color, pose="stand"):
    """A road-sign pictogram person (pixels; s = body height)."""
    head = s * 0.13
    d.ellipse((cx - head, cy - s * 0.5 - head * 2, cx + head, cy - s * 0.5), fill=color)
    w = max(3, int(s * 0.1))
    if pose == "jump":
        body = [(cx, cy - s * 0.48), (cx - s * 0.02, cy - s * 0.08)]
        arms = [(cx - s * 0.28, cy - s * 0.62), (cx, cy - s * 0.4), (cx + s * 0.28, cy - s * 0.62)]
        legs = [(cx - s * 0.24, cy + s * 0.12), (cx - s * 0.02, cy - s * 0.08), (cx + s * 0.2, cy + s * 0.02), (cx + s * 0.14, cy + s * 0.22)]
    elif pose == "fall":
        body = [(cx, cy - s * 0.5), (cx, cy - s * 0.1)]
        arms = [(cx - s * 0.3, cy - s * 0.78), (cx, cy - s * 0.42), (cx + s * 0.3, cy - s * 0.78)]
        legs = [(cx - s * 0.12, cy + s * 0.3), (cx, cy - s * 0.1), (cx + s * 0.12, cy + s * 0.3)]
    elif pose == "shield":
        body = [(cx, cy - s * 0.48), (cx, cy - s * 0.08)]
        arms = [(cx - s * 0.2, cy - s * 0.3), (cx, cy - s * 0.4), (cx + s * 0.22, cy - s * 0.36)]
        legs = [(cx - s * 0.2, cy + s * 0.3), (cx, cy - s * 0.08), (cx + s * 0.2, cy + s * 0.3)]
    else:
        body = [(cx, cy - s * 0.48), (cx, cy - s * 0.08)]
        arms = [(cx - s * 0.22, cy - s * 0.18), (cx, cy - s * 0.4), (cx + s * 0.22, cy - s * 0.18)]
        legs = [(cx - s * 0.16, cy + s * 0.3), (cx, cy - s * 0.08), (cx + s * 0.16, cy + s * 0.3)]
    for line in (body, arms, legs):
        d.line(line, fill=color, width=w, joint="curve")


def _road_sign(kind):
    W = 256 * 2
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    white, ink = _rgb(P.PAPER), _rgb(P.INK)
    if kind in ("edge",):
        # Warning diamond with a figure falling off a ledge.
        d.polygon([(W / 2, 14), (W - 14, W / 2), (W / 2, W - 14), (14, W / 2)], fill=_rgb(P.SIGN_YELLOW), outline=ink, width=14)
        d.rectangle((W * 0.2, W * 0.52, W * 0.5, W * 0.58), fill=ink)
        d.line([(W * 0.5, W * 0.52), (W * 0.5, W * 0.64)], fill=ink, width=10)
        _figure(d, W * 0.64, W * 0.47, W * 0.26, ink, "fall")
        _fit(d, "EDGE", (W * 0.3, W * 0.66, W * 0.7, W * 0.78), ink)
        return img
    if kind == "stop":
        r = W / 2 - 10
        pts = [(W / 2 + r * math.cos(math.pi / 8 + k * math.pi / 4), W / 2 + r * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]
        d.polygon(pts, fill=_rgb(P.SIGN_RED), outline=white, width=12)
        _fit(d, "STOP", (W * 0.16, W * 0.36, W * 0.84, W * 0.64), white)
        return img
    # Blue rectangular information signs with a pictogram and a caption.
    d.rounded_rectangle((8, 8, W - 8, W - 8), radius=36, fill=_rgb(P.SIGN_BLUE), outline=white, width=12)
    caption = {"jump": "JUMP", "double": "DOUBLE JUMP", "fastfall": "FAST FALL", "shield": "SHIELD", "grab": "GRAB"}[kind]
    _fit(d, caption, (40, W * 0.74, W - 40, W * 0.9), white)
    if kind == "jump":
        d.rectangle((W * 0.56, W * 0.48, W * 0.84, W * 0.68), fill=white)
        _figure(d, W * 0.34, W * 0.5, W * 0.3, white, "jump")
        d.arc((W * 0.3, W * 0.12, W * 0.78, W * 0.6), 200, 330, fill=white, width=10)
    elif kind == "double":
        d.rectangle((W * 0.6, W * 0.4, W * 0.86, W * 0.68), fill=white)
        _figure(d, W * 0.3, W * 0.56, W * 0.26, white, "jump")
        d.arc((W * 0.16, W * 0.26, W * 0.5, W * 0.6), 200, 340, fill=white, width=9)
        d.arc((W * 0.42, W * 0.08, W * 0.8, W * 0.46), 200, 340, fill=white, width=9)
    elif kind == "fastfall":
        _figure(d, W * 0.38, W * 0.44, W * 0.3, white, "fall")
        d.polygon([(W * 0.62, W * 0.2), (W * 0.74, W * 0.2), (W * 0.74, W * 0.46), (W * 0.82, W * 0.46),
                   (W * 0.68, W * 0.64), (W * 0.54, W * 0.46), (W * 0.62, W * 0.46)], fill=white)
    elif kind == "shield":
        cx, cy, r = W * 0.5, W * 0.4, W * 0.24
        d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=white, width=12)
        _figure(d, cx, cy + W * 0.12, W * 0.32, white, "shield")
    elif kind == "grab":
        _figure(d, W * 0.32, W * 0.56, W * 0.32, white, "stand")
        _figure(d, W * 0.66, W * 0.56, W * 0.32, white, "stand")
        d.line([(W * 0.36, W * 0.4), (W * 0.6, W * 0.42)], fill=white, width=12)
    return img.resize((256, 256), Image.LANCZOS)


def _board(w, h, bg, border=None, radius=0, bw=6):
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=radius, fill=_rgb(bg), outline=_rgb(border) if border else None,
                        width=bw if border else 0)
    return img, d


def _grime(img, rng, amount=0.12, seed=1):
    """Soft dirt streaks running down: weathered outdoor signage."""
    a = np.asarray(img).astype(np.float32)
    h, w = a.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    n = fbm(xs / w * 6, ys / h * 1.5, seed, 3)
    streak = fbm(xs / w * 22, ys / h * 0.8, seed + 1, 2)
    k = 1 - amount * (_smooth(0.45, 0.8, n) * 0.6 + _smooth(0.5, 0.85, streak) * 0.4) * (ys / h) ** 0.5
    a[..., :3] *= k[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def _billboard(kind, rng):
    w, h = 1024, 512
    if kind == "bb_psa":
        img, d = _board(w, h, P.SIGN_YELLOW)
        d.rectangle((0, h - 70, w, h), fill=_rgb(P.INK))
        _fit(d, "DRIVE SAFE.", (40, 40, w - 40, 210), _rgb(P.INK))
        _fit(d, "FIGHT DIRTY.", (40, 220, w - 40, 400), _rgb(P.PAINT_RED))
        _fit(d, "DEPARTMENT OF FIGHTING  ·  PUBLIC SERVICE NOTICE", (40, h - 64, w - 40, h - 8), _rgb(P.SIGN_YELLOW), mono=True)
    elif kind == "bb_cola":
        img, d = _board(w, h, (0.72, 0.10, 0.10))
        for k in range(8):
            d.ellipse((620 + k * 30, 40 + (k % 3) * 120, 700 + k * 30, 120 + (k % 3) * 120), fill=_rgb((0.80, 0.18, 0.15)))
        d.rounded_rectangle((720, 70, 900, 460), radius=60, fill=_rgb((0.36, 0.62, 0.18)), outline=_rgb(P.PAPER), width=10)
        _fit(d, "SLOP", (760, 160, 860, 260), _rgb(P.PAPER))
        _fit(d, "SLOP COLA", (40, 60, 660, 260), _rgb(P.PAPER))
        _fit(d, "NOW 40% MORE SLOP", (40, 290, 660, 380), _rgb(P.SIGN_YELLOW))
    elif kind == "bb_motors":
        img, d = _board(w, h, (0.10, 0.22, 0.44))
        _fit(d, "MANKI MOTORS", (40, 40, w - 40, 200), _rgb(P.SIGN_YELLOW))
        _fit(d, "DENTS? WE MAKE 'EM.", (40, 230, w - 40, 330), _rgb(P.PAPER))
        d.rounded_rectangle((300, 360, 724, 470), radius=30, fill=_rgb(P.PAPER))
        _fit(d, "USED CARS  ·  LOW KM", (320, 370, 704, 460), _rgb((0.10, 0.22, 0.44)))
    else:  # bb_burger
        img, d = _board(w, h, (0.98, 0.62, 0.12))
        d.ellipse((60, 90, 420, 430), fill=_rgb((0.70, 0.34, 0.12)))
        d.rectangle((60, 250, 420, 300), fill=_rgb((0.30, 0.55, 0.20)))
        d.rectangle((60, 300, 420, 340), fill=_rgb((0.40, 0.18, 0.10)))
        _fit(d, "BONK", (460, 60, w - 40, 230), _rgb(P.INK))
        _fit(d, "BURGER", (460, 220, w - 40, 380), _rgb(P.PAINT_RED))
        _fit(d, "HITS HARD.", (460, 390, w - 40, 470), _rgb(P.INK))
    return _grime(img, rng, 0.18, seed=len(kind) * 7)


def atlas(path, seed=13):
    rng = random.Random(seed)
    img = Image.new("RGBA", (ATLAS, ATLAS), (128, 128, 128, 0))

    def put(name, cell_img):
        x, y, w, h = CELLS[name]
        img.paste(cell_img.resize((w, h), Image.LANCZOS) if cell_img.size != (w, h) else cell_img, (x, y))

    for kind in ("jump", "double", "fastfall", "edge", "shield", "grab", "stop"):
        put("sign_" + kind, _road_sign(kind))
    put("white", Image.new("RGBA", (64, 64), (255, 255, 255, 255)))

    b, d = _board(384, 192, P.EXIT_GREEN, P.PAPER, 18, 10)
    _fit(d, "EXIT", (90, 30, 360, 160), _rgb(P.PAPER))
    _figure(d, 55, 130, 90, _rgb(P.PAPER), "stand")
    put("sign_exit", b)
    b, d = _board(192, 192, P.SIGN_RED, None, 0)
    d.ellipse((4, 4, 188, 188), fill=_rgb(P.SIGN_RED), outline=_rgb(P.PAPER), width=8)
    d.rectangle((30, 80, 162, 112), fill=_rgb(P.PAPER))
    put("sign_noentry", b)

    b, d = _board(1024, 384, P.SIGN_BLUE, P.PAPER, 24, 12)
    _fit(d, "TICKETS", (60, 30, 964, 250), _rgb(P.PAPER))
    _fit(d, "TAKE A NUMBER  ·  PAY HERE", (60, 260, 964, 350), _rgb(P.SIGN_YELLOW), mono=True)
    put("sign_tickets", b)
    b, d = _board(1024, 384, P.PAPER, P.INK, 16, 12)
    _fit(d, "EXAMINER", (60, 40, 964, 250), _rgb(P.INK))
    _fit(d, "WATCHING YOU", (60, 260, 964, 350), _rgb(P.PAINT_RED), mono=True)
    put("sign_examiner", b)
    b, d = _board(1024, 256, P.INK, P.PAINT_YELLOW, 12, 10)
    _fit(d, "TEST EQUIPMENT", (40, 36, 984, 220), _rgb(P.PAINT_YELLOW))
    put("sign_equipment", _grime(b, rng, 0.2, 3))
    b, d = _board(512, 128, P.EXIT_GREEN, P.PAPER, 10, 6)
    _fit(d, "STAIRS", (20, 14, 492, 114), _rgb(P.PAPER))
    put("sign_stairs", b)
    b, d = _board(512, 128, P.INK, P.SIGN_YELLOW, 10, 6)
    _fit(d, "PAY HERE", (20, 14, 492, 114), _rgb(P.SIGN_YELLOW))
    put("sign_pay", b)
    b, d = _board(1024, 384, P.SIGN_YELLOW, P.INK, 20, 14)
    _fit(d, "MAX HEIGHT 12 M", (50, 40, 974, 230), _rgb(P.INK))
    _fit(d, "WALLS ARE NOT A SHORTCUT", (50, 250, 974, 340), _rgb(P.INK), mono=True)
    put("sign_height", b)

    # Wall fascia over the Test Center's glass front.
    b, d = _board(4096, 384, (0.16, 0.18, 0.22))
    d.rectangle((0, 0, 4096, 24), fill=_rgb(P.PAINT_YELLOW))
    d.rectangle((0, 360, 4096, 384), fill=_rgb(P.PAINT_YELLOW))
    _fit(d, "DEPARTMENT OF FIGHTING   ·   FIGHTER'S LICENSE TEST CENTER", (120, 60, 3976, 330), _rgb(P.PAPER))
    put("fascia", b)
    # Giant letters for the building's east face (read from beyond the edge, recovering).
    b, d = _board(4096, 512, P.CONCRETE)
    _fit(d, "DEPT. OF FIGHTING", (60, 40, 4036, 480), _rgb((0.16, 0.18, 0.22)))
    put("facade_letters", _grime(b, rng, 0.14, 5))

    for name in ("bb_psa", "bb_cola", "bb_motors", "bb_burger"):
        put(name, _billboard(name, rng))

    # The rooftop sign: yellow capitals on a dark frame.
    b, d = _board(4096, 640, (0.10, 0.11, 0.13), P.PAINT_YELLOW, 0, 18)
    _fit(d, "FIGHTER'S LICENSE", (120, 60, 3976, 580), _rgb(P.PAINT_YELLOW))
    put("big_sign", b)
    # A faded ghost sign painted over the party wall's own brick: the cell covers the wall from
    # z -6.4 to 6.4 and y 6.4 to 10.4 (walls.py), so its bricks line up with the tiled wall.
    gw, gh = 2048, 640
    ys, xs = np.mgrid[0:gh, 0:gw].astype(np.float32)
    z = GHOST_RECT[0] + (xs + 0.5) / gw * (GHOST_RECT[2] - GHOST_RECT[0])
    y = GHOST_RECT[3] - (ys + 0.5) / gh * (GHOST_RECT[3] - GHOST_RECT[1])
    bricks = brick_color(z / BRICK_SIZE, y / BRICK_SIZE)
    letters = Image.new("RGBA", (gw, gh), (0, 0, 0, 0))
    d = ImageDraw.Draw(letters)
    _fit(d, "PASS OR GET PUNCHED", (80, 60, 1968, 320), _rgb((0.90, 0.86, 0.76)))
    _fit(d, "DEPT. OF FIGHTING  ·  SINCE 1987", (80, 360, 1968, 560), _rgb((0.94, 0.80, 0.30)))
    la = np.asarray(letters).astype(np.float32) / 255.0
    fade = (0.42 + 0.38 * fbm(xs / gw * 9, ys / gh * 3, 31, 3)) * la[..., 3]
    mortar = brick_mortar(z / BRICK_SIZE, y / BRICK_SIZE)
    fade *= np.where(mortar, 0.45, 1.0)  # paint wore off the recessed mortar first
    ghost = bricks * (1 - fade[..., None]) + la[..., :3] * fade[..., None]
    put("ghost", Image.fromarray(np.clip(ghost * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA"))

    for name, text in (("stencil_15", "1.5 M"), ("stencil_30", "3 M"), ("stencil_55", "5.5 M")):
        b, d = _board(512, 320, P.STRIPE_WHITE)
        _fit(d, text, (30, 30, 482, 290), _rgb(P.INK))
        put(name, b)
    b, d = _board(512, 320, P.GLASS)
    for k in range(5):
        d.polygon([(k * 140 - 60, 320), (k * 140 + 40, 0), (k * 140 + 90, 0), (k * 140 - 10, 320)], fill=_rgb(P.GLASS_SKY, 255))
    put("examiner_glass", b.filter(ImageFilter.GaussianBlur(6)))
    put("glass", _glass(rng))
    put("shutter", _shutter())
    put("window_office", _office_window(rng))
    put("window_apt", _apt_window(False))
    put("window_apt_lit", _apt_window(True))
    put("door_glass", _door_glass())
    put("door_steel", _door_steel())
    put("booth_window", _booth_window())
    put("ticket_machine", _ticket_machine())
    put("cone_cage", _cone_cage())
    b, d = _board(1024, 128, P.SIGN_YELLOW)
    for k in range(-2, 18):
        d.polygon([(k * 64, 128), (k * 64 + 64, 0), (k * 64 + 96, 0), (k * 64 + 32, 128)], fill=_rgb(P.INK))
    put("hazard", b)
    b, d = _board(256, 384, P.SIGN_RED, P.INK, 8, 6)
    d.ellipse((48, 90, 208, 250), outline=_rgb(P.PAPER), width=18)
    _fit(d, "FIRE HOSE", (20, 290, 236, 360), _rgb(P.PAPER))
    put("fire_hose", b)
    b, d = _board(256, 384, (0.58, 0.60, 0.58), P.STEEL_DARK, 6, 6)
    d.polygon([(128, 70), (96, 190), (140, 190), (112, 300), (176, 150), (130, 150), (156, 70)], fill=_rgb(P.SIGN_YELLOW))
    put("panel_box", _grime(b, rng, 0.25, 9))
    put("window_shop", _shop_window(rng))
    b, d = _board(1024, 512, P.PAPER, P.SIGN_BLUE, 30, 18)
    _fit(d, "DEPARTMENT", (60, 50, 964, 190), _rgb(P.SIGN_BLUE))
    _fit(d, "OF FIGHTING", (60, 190, 964, 330), _rgb(P.SIGN_BLUE))
    _fit(d, "LICENSE TESTS · FLOOR R", (60, 360, 964, 450), _rgb(P.INK), mono=True)
    put("sign_dept", b)
    img.save(path)
    return path


def _glass(rng):
    w, h = 512, 320
    a = np.zeros((h, w, 4), np.float32)
    ys = np.linspace(0, 1, h)[:, None, None]
    sky = _c(P.GLASS_SKY) * (1 - ys) + _c(P.GLASS) * ys
    a[..., :3] = sky * 255
    a[..., 3] = 255
    img = Image.fromarray(a.astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(img)
    for k in range(3):
        x = rng.uniform(0, w)
        d.polygon([(x, 0), (x + 60, 0), (x - 40, h), (x - 100, h)], fill=_rgb(geo_mix(P.GLASS_SKY, (1, 1, 1), 0.35), 255))
    return img.filter(ImageFilter.GaussianBlur(4))


def geo_mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def _office_window(rng):
    img = _glass(rng)
    d = ImageDraw.Draw(img)
    for x in (0, 170, 340, 508):
        d.rectangle((x, 0, x + 6, 320), fill=_rgb(P.MULLION))
    d.rectangle((0, 0, 512, 8), fill=_rgb(P.MULLION))
    d.rectangle((0, 312, 512, 320), fill=_rgb(P.MULLION))
    for x in (40, 230, 400):
        d.rectangle((x, 200, x + 90, 320), fill=_rgb((0.16, 0.2, 0.24)))  # desks and screens behind
        d.rectangle((x + 20, 160, x + 60, 196), fill=_rgb((0.28, 0.42, 0.52)))
    return img


def _apt_window(lit):
    img, d = _board(256, 320, (0.86, 0.84, 0.78))
    glass = (0.62, 0.70, 0.76) if lit else (0.22, 0.27, 0.32)
    d.rectangle((24, 24, 232, 280), fill=_rgb(glass))
    d.rectangle((124, 24, 132, 280), fill=_rgb((0.86, 0.84, 0.78)))
    d.rectangle((24, 120, 232, 128), fill=_rgb((0.86, 0.84, 0.78)))
    if not lit:
        d.rectangle((24, 24, 232, 110), fill=_rgb((0.70, 0.62, 0.50)))  # blinds
    d.rectangle((14, 280, 242, 300), fill=_rgb((0.70, 0.68, 0.64)))
    return img


def _shutter():
    img, d = _board(512, 320, P.SHUTTER)
    for y in range(0, 320, 16):
        d.rectangle((0, y, 512, y + 3), fill=_rgb(geo_mix(P.SHUTTER, (0, 0, 0), 0.35)))
        d.rectangle((0, y + 4, 512, y + 6), fill=_rgb(geo_mix(P.SHUTTER, (1, 1, 1), 0.3)))
    d.rectangle((0, 290, 512, 320), fill=_rgb(P.STEEL_DARK))
    _fit(d, "NO PARKING", (60, 110, 452, 200), _rgb(P.PAINT_RED))
    return img


def _door_glass():
    img, d = _board(512, 512, P.STEEL)
    for x0 in (20, 266):
        d.rectangle((x0, 20, x0 + 226, 500), fill=_rgb(P.GLASS))
        d.polygon([(x0 + 30, 20), (x0 + 90, 20), (x0 + 20, 300), (x0, 300)], fill=_rgb(P.GLASS_SKY))
        d.rectangle((x0 + 190 if x0 == 20 else x0 + 20, 220, x0 + 206 if x0 == 20 else x0 + 36, 320), fill=_rgb((0.85, 0.85, 0.85)))
    _fit(d, "PUSH", (60, 330, 220, 380), _rgb(P.PAPER))
    _fit(d, "PULL", (300, 330, 460, 380), _rgb(P.PAPER))
    return img


def _door_steel():
    img, d = _board(256, 512, P.DOOR_GREEN)
    d.rectangle((16, 16, 240, 496), outline=_rgb(geo_mix(P.DOOR_GREEN, (0, 0, 0), 0.4)), width=6)
    d.rectangle((60, 60, 196, 180), fill=_rgb(P.GLASS))
    d.rectangle((24, 270, 70, 290), fill=_rgb((0.8, 0.8, 0.78)))
    return img


def _booth_window():
    img, d = _board(512, 512, P.BOOTH)
    d.rectangle((30, 40, 482, 330), fill=_rgb(P.GLASS))
    d.polygon([(60, 40), (160, 40), (90, 330), (30, 330), (30, 200)], fill=_rgb(P.GLASS_SKY))
    d.ellipse((300, 120, 380, 200), fill=_rgb((0.26, 0.22, 0.2)))  # the examiner's head
    d.rectangle((280, 200, 400, 330), fill=_rgb((0.18, 0.22, 0.3)))
    d.rectangle((150, 270, 360, 300), fill=_rgb((0.86, 0.86, 0.84)))  # the speaking slot
    d.rectangle((0, 330, 512, 370), fill=_rgb(P.STEEL))
    for k in range(6):
        d.polygon([(k * 96 - 40, 512), (k * 96 + 8, 370), (k * 96 + 56, 370), (k * 96 + 8, 512)], fill=_rgb(P.SIGN_YELLOW))
    return img


def _ticket_machine():
    img, d = _board(256, 512, P.STEEL_DARK, P.INK, 16, 6)
    d.rectangle((40, 60, 216, 170), fill=_rgb((0.16, 0.42, 0.36)))
    _fit(d, "PAY", (50, 70, 206, 160), _rgb(P.PAPER))
    for r in range(3):
        for c in range(3):
            d.rectangle((60 + c * 48, 210 + r * 44, 96 + c * 48, 244 + r * 44), fill=_rgb((0.8, 0.8, 0.78)))
    d.rectangle((70, 360, 186, 380), fill=_rgb(P.INK))
    d.rectangle((60, 420, 196, 470), fill=_rgb(P.SIGN_YELLOW))
    return img


def _cone_cage():
    """Steel mesh over a dark recess with cones stacked inside (the geometry adds real cones)."""
    img, d = _board(512, 512, (0.16, 0.16, 0.17))
    for k in range(0, 512, 32):
        d.line([(k, 0), (k, 512)], fill=_rgb(P.STEEL), width=4)
        d.line([(0, k), (512, k)], fill=_rgb(P.STEEL), width=4)
    d.rectangle((0, 0, 511, 511), outline=_rgb(P.STEEL_DARK), width=14)
    return img


def _shop_window(rng):
    img = _glass(rng).resize((512, 384))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 512, 60), fill=_rgb((0.70, 0.18, 0.14)))
    _fit(d, rng.choice(["DELI", "TACOS", "KEYS CUT", "LAUNDRY"]), (20, 6, 492, 54), _rgb(P.PAPER))
    return img


# ---------------------------------------------------------------- tiling maps

BRICK_SIZE = 1.8                      # metres per brick tile (parts.BRICK_TILE)
GHOST_RECT = (-6.4, 6.4, 6.4, 10.4)   # z0, y0, z1, y1 of the ghost sign on the party wall


def _brick_parts(u, v):
    """Running bond in tile units (1 = 1.8 m): 24 courses of 75 mm, 8 bricks of 225 mm."""
    course = np.floor(v * 24).astype(np.int64)
    s = u * 8 + (course % 2) * 0.5
    brick_id = (np.floor(s).astype(np.int64) % 8) + (course % 24) * 8
    return course, s - np.floor(s), v * 24 - course, brick_id


def brick_mortar(u, v):
    _course, fu, fv, _id = _brick_parts(u, v)
    return (fu < 0.045) | (fv < 0.13)


def brick_color(u, v, seed=21, grain=None):
    """Brick colour (0..1 RGB) at tile coordinates (u right, v up), periodic over one tile."""
    rng = np.random.default_rng(seed)
    tone_table = rng.uniform(0.82, 1.12, 24 * 8)
    warm_table = rng.uniform(0, 1, 24 * 8)
    _course, fu, fv, brick_id = _brick_parts(u, v)
    tone, warm = tone_table[brick_id], warm_table[brick_id]
    col = _mix(np.broadcast_to(_c(P.BRICK), u.shape + (3,)).copy(), _c(P.BRICK_DARK), (warm > 0.8) * 0.7)
    g = np.full(u.shape, 0.5, np.float32) if grain is None else np.asarray(grain, np.float32)
    col = col * (tone * (0.9 + 0.2 * g))[..., None]
    mortar = (fu < 0.045) | (fv < 0.13)
    col[mortar] = _c(P.MORTAR) * (0.9 + 0.1 * g[mortar])[..., None]
    return col


def brick(path, size=512, seed=21):
    """Tileable running-bond brick over 1.8 x 1.8 m (row 0 is the top, v = 1)."""
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32) / size
    u, v = xs + 0.5 / size, 1.0 - ys - 0.5 / size
    grain = fbm(xs * 64, ys * 64, seed, 3, period=64)
    col = brick_color(u, v, seed, grain)
    Image.fromarray(np.clip(col * 255, 0, 255).astype(np.uint8), "RGB").save(path)
    return path


def concrete(path, size=1024, seed=23):
    """Tileable board-formed concrete: a 2.4 m panel, joints at its edges, four tie holes."""
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32) / size
    v = 0.92 + 0.08 * fbm(xs * 6, ys * 6, seed, 4, period=6)
    v *= 0.96 + 0.06 * fbm(xs * 2, ys * 48, seed + 1, 2, period=(2, 48))  # board grain
    stain = _smooth(0.55, 0.8, fbm(xs * 3, ys * 1.2, seed + 2, 3, period=(3, 1)))
    v *= 1 - 0.08 * stain * ys
    col = _c(P.CONCRETE)[None, None, :] * v[..., None]
    joint = (xs < 0.006) | (ys < 0.006)
    col[joint] = _c(P.CONCRETE_DARK) * 0.8
    for tx in (0.25, 0.75):
        for ty in (0.25, 0.75):
            r = np.hypot(xs - tx, ys - ty)
            col *= (1 - 0.5 * (r < 0.012))[..., None]
    Image.fromarray(np.clip(col * 255, 0, 255).astype(np.uint8), "RGB").save(path)
    return path


def detail(path, size=512, seed=59):
    """Tileable grit, mean near 0.5 (neutral for the shader)."""
    c = np.arange(size) / size
    X, Z = np.meshgrid(c, c)
    grit = fbm(X * 16, Z * 16, seed, 4, period=16)
    fine = fbm(X * 64, Z * 64, seed + 7, 2, period=64)
    v = 0.5 + 0.5 * (grit - 0.5) + 0.35 * (fine - 0.5)
    img = Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8), "L")
    img.save(path)
    return path


if __name__ == "__main__":
    import os
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/license-test-textures"
    os.makedirs(out, exist_ok=True)
    which = sys.argv[2:] or ["deck", "atlas", "brick", "concrete", "detail"]
    names = {"deck": "LicenseTestDeck.png", "atlas": "LicenseTestAtlas.png", "brick": "LicenseTestBrick.png",
             "concrete": "LicenseTestConcrete.png", "detail": "LicenseTestDetail.png"}
    for w in which:
        globals()[w](os.path.join(out, names[w]))
        print("wrote", names[w])
