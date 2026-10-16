"""Original procedural textures for Table Manners (kids' crayon art, posters, blocks).

atlas (2048 x 2048, RGB; image y grows downwards, UV v upwards):
  white:     y 0..64, the whole top row (WHITE_UV in stagekit/geo.py samples it)
  drawings:  512 x 362 crayon drawings on paper (DRAWINGS), rows from y 64
  posters:   wall posters, the corkboard, the height chart and the easel painting
  letters:   128 x 128 alphabet-block faces
detail: tileable grayscale paper/fabric grain for StageVertexLit's triplanar detail.

Everything is drawn from code with the monoline font in font.py; crayon strokes are
jittered, layered and broken up so they read as wax on paper rather than vectors.
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import font
import palette as P

SIZE = 2048
SS = 2  # supersampling for every drawing

# name -> (x, y, w, h) in atlas pixels (y down)
SLOTS = {
    "house": (0, 64, 512, 362), "fighters": (512, 64, 512, 362), "rainbow": (1024, 64, 512, 362),
    "rocket": (1536, 64, 512, 362),
    "dino": (0, 426, 512, 362), "corkboard": (512, 426, 512, 307), "map": (1024, 426, 512, 370),
    "kindness": (1536, 426, 512, 256),
    "abc": (0, 800, 512, 695), "space": (512, 800, 512, 670), "fightguy": (1024, 800, 512, 683),
    "manki": (1536, 800, 512, 652),
    "painting": (0, 1500, 400, 514), "growth": (408, 1500, 64, 538),
}
LETTERS = ["A", "B", "C", "D"]
for _i, _l in enumerate(LETTERS):
    SLOTS["letter_" + _l] = (480 + _i * 136, 1500, 128, 128)
POSTER_ALIASES = {"rainbow": "kindness"}


def _uv(slot, inset=3):
    x, y, w, h = SLOTS[slot]
    u0, u1 = (x + inset) / SIZE, (x + w - inset) / SIZE
    v1, v0 = 1 - (y + inset) / SIZE, 1 - (y + h - inset) / SIZE
    return (u0, v0), (u1, v1)


def drawing_uv(name):
    return _uv(name)


def poster_uv(name):
    return _uv(POSTER_ALIASES.get(name, name))


def letter_uv(letter):
    return _uv("letter_" + letter)


def rgb(c):
    return tuple(int(max(0, min(1, v)) * 255) for v in c[:3])


# ---------------------------------------------------------------- crayon kit

class Sheet:
    """A supersampled drawing surface with crayon-like strokes and scribble fills."""

    def __init__(self, w, h, paper=P.PAPER, seed=1):
        self.w, self.h = w, h
        self.img = Image.new("RGB", (w * SS, h * SS), rgb(paper))
        self.d = ImageDraw.Draw(self.img)
        self.rng = random.Random(seed)

    def S(self, p):
        return (p[0] * SS, p[1] * SS)

    def line(self, pts, color, width=5.0, wobble=1.2, passes=3, gaps=0.08):
        rng = self.rng
        col = rgb(color)
        dense = []
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            n = max(1, int(math.dist(a, b) / 6))
            for k in range(n):
                t = k / n
                dense.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
        dense.append(pts[-1])
        for p in range(passes):
            off = (rng.uniform(-wobble, wobble), rng.uniform(-wobble, wobble))
            seg = []
            for q in dense:
                jit = (q[0] + off[0] + rng.uniform(-wobble, wobble) * 0.5, q[1] + off[1] + rng.uniform(-wobble, wobble) * 0.5)
                if rng.random() < gaps:
                    if len(seg) > 1:
                        self.d.line([self.S(s) for s in seg], fill=col, width=int(width * SS * rng.uniform(0.6, 1.0)), joint="curve")
                    seg = []
                    continue
                seg.append(jit)
            if len(seg) > 1:
                self.d.line([self.S(s) for s in seg], fill=col, width=int(width * SS * rng.uniform(0.7, 1.0)), joint="curve")

    def poly(self, pts, color, width=5.0, **kw):
        self.line(list(pts) + [pts[0]], color, width, **kw)

    def fill(self, pts, color, spacing=7.0, angle=None, width=4.0, solid=0.0):
        """Scribble hatching inside a polygon (zigzag strokes through a mask)."""
        rng = self.rng
        layer = Image.new("RGB", self.img.size, (0, 0, 0))
        ld = ImageDraw.Draw(layer)
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
        size = max(max(xs) - min(xs), max(ys) - min(ys)) * 0.8 + spacing * 2
        ang = angle if angle is not None else rng.uniform(0.3, 1.2)
        c, s = math.cos(ang), math.sin(ang)
        zig = []
        k = -size
        flip = 1
        while k < size:
            for t in (-size, size) if flip > 0 else (size, -size):
                zig.append((cx + c * t - s * k + rng.uniform(-1, 1), cy + s * t + c * k + rng.uniform(-1, 1)))
            k += spacing * rng.uniform(0.8, 1.2)
            flip = -flip
        ld.line([self.S(z) for z in zig], fill=rgb(color), width=int(width * SS))
        mask = Image.new("L", self.img.size, 0)
        md = ImageDraw.Draw(mask)
        md.polygon([self.S(p) for p in pts], fill=255)
        if solid > 0:
            md2 = Image.new("RGB", self.img.size, rgb(color))
            self.img.paste(Image.blend(self.img, md2, solid), (0, 0), mask)
        # only where the hatch layer has ink
        ink = layer.convert("L").point(lambda v: 255 if v > 8 else 0)
        mask = Image.fromarray(np.minimum(np.array(mask), np.array(ink)))
        self.img.paste(layer, (0, 0), mask)

    def circle(self, c, r, color, width=5.0, fill=None, n=28, **kw):
        pts = [(c[0] + r * math.cos(2 * math.pi * k / n) * self.rng.uniform(0.97, 1.03),
                c[1] + r * math.sin(2 * math.pi * k / n) * self.rng.uniform(0.97, 1.03)) for k in range(n)]
        if fill:
            self.fill(pts, fill, spacing=6.0)
        self.poly(pts, color, width, **kw)
        return pts

    def text(self, text, cx, cy, height, color, width=4.0, tracking=1.1, wobble=1.0):
        strokes, w = font.text_strokes(text, height, origin=(0, 0), align="center", tracking=tracking)
        for st in strokes:
            pts = [(cx + x, cy + height / 2 - y) for x, y in st]
            self.line(pts, color, width, wobble=wobble, passes=2, gaps=0.03)

    def image(self):
        return self.img.resize((self.w, self.h), Image.LANCZOS)


def _paper_grain(img, amount=10, seed=3):
    a = np.asarray(img).astype(np.int16)
    rng = np.random.default_rng(seed)
    noise = rng.normal(0, amount, a.shape[:2])[..., None]
    return Image.fromarray(np.clip(a + noise, 0, 255).astype(np.uint8))


C = P.CRAYON


# ---------------------------------------------------------------- the kids' drawings

def stick_fighter(s, x, ground, kind, scale=1.0):
    """A kid's version of a roster fighter (FightGuy, Manki, Wibou, Bonk)."""
    k = scale
    head_y = ground - 120 * k
    body = [(x, head_y + 22 * k), (x, ground - 45 * k)]
    legs = [[(x, ground - 45 * k), (x - 18 * k, ground)], [(x, ground - 45 * k), (x + 18 * k, ground)]]
    arms = [[(x, head_y + 40 * k), (x - 30 * k, head_y + 25 * k)], [(x, head_y + 40 * k), (x + 30 * k, head_y + 55 * k)]]
    skin = (0.98, 0.80, 0.62)
    if kind == "fightguy":
        s.circle((x, head_y), 22 * k, C["black"], 4, fill=skin)
        s.line([(x - 24 * k, head_y - 6 * k), (x + 24 * k, head_y - 6 * k), (x + 38 * k, head_y + 4 * k)], C["red"], 7)
        s.line([(x - 16 * k, head_y - 16 * k), (x - 12 * k, head_y - 28 * k), (x - 5 * k, head_y - 20 * k), (x, head_y - 32 * k),
                (x + 5 * k, head_y - 20 * k), (x + 12 * k, head_y - 28 * k), (x + 16 * k, head_y - 16 * k)], C["black"], 4)
        s.line(body[0:2], C["blue"], 9)
        arms[1] = [(x, head_y + 40 * k), (x + 34 * k, head_y + 20 * k)]
        s.circle((x + 38 * k, head_y + 16 * k), 8 * k, C["black"], 3, fill=skin)
    elif kind == "manki":
        s.circle((x - 22 * k, head_y - 4 * k), 9 * k, C["brown"], 4, fill=C["brown"])
        s.circle((x + 22 * k, head_y - 4 * k), 9 * k, C["brown"], 4, fill=C["brown"])
        s.circle((x, head_y), 21 * k, C["brown"], 4, fill=(0.70, 0.48, 0.30))
        s.circle((x, head_y + 6 * k), 11 * k, C["brown"], 3, fill=skin)
        s.poly([(x - 16 * k, head_y + 30 * k), (x + 16 * k, head_y + 30 * k), (x + 18 * k, ground - 40 * k), (x - 18 * k, ground - 40 * k)], C["blue"], 4)
        s.fill([(x - 16 * k, head_y + 30 * k), (x + 16 * k, head_y + 30 * k), (x + 18 * k, ground - 40 * k), (x - 18 * k, ground - 40 * k)], C["blue"], 5)
        s.line([(x + 16 * k, ground - 50 * k), (x + 40 * k, ground - 60 * k), (x + 46 * k, ground - 80 * k), (x + 36 * k, ground - 86 * k)], C["brown"], 5)
        s.circle((x - 44 * k, head_y + 26 * k), 12 * k, C["black"], 4, fill=C["black"])
        s.line([(x - 40 * k, head_y + 14 * k), (x - 34 * k, head_y + 2 * k)], C["orange"], 4)
        s.line([(x - 34 * k, head_y + 2 * k), (x - 30 * k, head_y - 6 * k)], C["yellow"], 6)
        arms[0] = [(x, head_y + 40 * k), (x - 34 * k, head_y + 30 * k)]
    elif kind == "wibou":
        s.poly([(x - 20 * k, head_y - 6 * k), (x - 16 * k, head_y - 34 * k), (x - 4 * k, head_y - 16 * k)], C["orange"], 4)
        s.poly([(x + 20 * k, head_y - 6 * k), (x + 16 * k, head_y - 34 * k), (x + 4 * k, head_y - 16 * k)], C["orange"], 4)
        s.circle((x, head_y), 20 * k, C["orange"], 4, fill=skin)
        for t in range(3):
            s.line([(x - 6 * k, ground - 50 * k), (x - 40 * k - t * 10 * k, ground - 70 * k + t * 14 * k), (x - 58 * k - t * 6 * k, ground - 40 * k + t * 10 * k)], C["orange"], 8)
        s.poly([(x - 18 * k, head_y + 26 * k), (x + 18 * k, head_y + 26 * k), (x + 26 * k, ground - 38 * k), (x - 26 * k, ground - 38 * k)], C["purple"], 4)
        s.fill([(x - 18 * k, head_y + 26 * k), (x + 18 * k, head_y + 26 * k), (x + 26 * k, ground - 38 * k), (x - 26 * k, ground - 38 * k)], C["purple"], 6)
        s.line([(x + 30 * k, head_y + 55 * k), (x + 52 * k, head_y - 30 * k)], (0.55, 0.60, 0.70), 5)
        s.line([(x + 24 * k, head_y + 44 * k), (x + 38 * k, head_y + 50 * k)], C["brown"], 5)
    else:  # bonk: big and square with a giant sword
        s.circle((x, head_y - 6 * k), 20 * k, C["black"], 4, fill=skin)
        s.poly([(x - 28 * k, head_y + 18 * k), (x + 28 * k, head_y + 18 * k), (x + 26 * k, ground - 40 * k), (x - 26 * k, ground - 40 * k)], C["green"], 5)
        s.fill([(x - 28 * k, head_y + 18 * k), (x + 28 * k, head_y + 18 * k), (x + 26 * k, ground - 40 * k), (x - 26 * k, ground - 40 * k)], C["green"], 6)
        s.line([(x - 30 * k, head_y + 40 * k), (x - 46 * k, head_y - 70 * k)], (0.55, 0.60, 0.70), 11)
        s.line([(x - 44 * k, head_y + 30 * k), (x - 16 * k, head_y + 36 * k)], C["brown"], 6)
        body = [(x, head_y + 18 * k), (x, head_y + 18 * k)]
    s.line(body, C["black"], 5)
    for leg in legs:
        s.line(leg, C["black"], 5)
    for arm in arms:
        s.line(arm, C["black"], 5)
    # smile
    s.line([(x - 8 * k, head_y + 6 * k), (x, head_y + 11 * k), (x + 8 * k, head_y + 6 * k)], C["black"], 3)
    for ex in (-7, 7):
        s.circle((x + ex * k, head_y - 4 * k), 2 * k, C["black"], 3, fill=C["black"])


def draw_house(seed):
    s = Sheet(512, 362, seed=seed)
    s.line([(0, 318), (90, 312), (180, 322), (300, 314), (420, 320), (512, 312)], C["green"], 9)
    s.fill([(0, 318), (512, 312), (512, 362), (0, 362)], C["green"], 8)
    s.circle((70, 70), 38, C["orange"], 6, fill=C["yellow"])
    for k in range(10):
        a = 2 * math.pi * k / 10
        s.line([(70 + 48 * math.cos(a), 70 + 48 * math.sin(a)), (70 + 70 * math.cos(a), 70 + 70 * math.sin(a))], C["orange"], 5)
    walls = [(170, 175), (330, 175), (330, 316), (170, 316)]
    s.fill(walls, C["red"], 9)
    s.poly(walls, C["red"], 6)
    roof = [(150, 180), (250, 92), (350, 180)]
    s.fill(roof, C["brown"], 7)
    s.poly(roof, C["brown"], 6)
    s.fill([(232, 250), (268, 250), (268, 316), (232, 316)], C["brown"], 6)
    s.poly([(232, 250), (268, 250), (268, 316), (232, 316)], C["black"], 4)
    for wx in (192, 288):
        s.fill([(wx - 18, 200), (wx + 18, 200), (wx + 18, 236), (wx - 18, 236)], C["sky"], 6)
        s.poly([(wx - 18, 200), (wx + 18, 200), (wx + 18, 236), (wx - 18, 236)], C["black"], 4)
        s.line([(wx, 200), (wx, 236)], C["black"], 3)
        s.line([(wx - 18, 218), (wx + 18, 218)], C["black"], 3)
    s.fill([(296, 130), (316, 130), (316, 160), (296, 150)], C["brown"], 5)
    for k in range(3):
        s.circle((314 + k * 14, 112 - k * 20), 10 + k * 4, (0.55, 0.55, 0.58), 3)
    s.line([(430, 316), (430, 230)], C["brown"], 12)
    s.circle((430, 200), 46, C["green"], 5, fill=C["lime"])
    for bx, by in ((380, 60), (420, 90), (460, 50)):
        s.line([(bx - 12, by), (bx - 6, by - 7), (bx, by), (bx + 6, by - 7), (bx + 12, by)], C["black"], 3)
    s.text("MY HOUSE", 256, 30, 26, C["purple"], 4)
    return s.image()


def draw_fighters(seed):
    s = Sheet(512, 362, seed=seed)
    s.line([(0, 330), (512, 326)], C["green"], 8)
    for kind, x in (("fightguy", 80), ("manki", 200), ("wibou", 320), ("bonk", 440)):
        stick_fighter(s, x, 326, kind, 1.0)
    s.text("WE R THE BEST!!", 256, 34, 28, C["red"], 5)
    for hx, hy in ((30, 40), (480, 46)):
        s.text("♥", hx, hy, 24, C["pink"], 5)
    return s.image()


def draw_rainbow(seed):
    s = Sheet(512, 362, seed=seed)
    cols = [C["red"], C["orange"], C["yellow"], C["green"], C["blue"], C["purple"]]
    for k, col in enumerate(cols):
        r = 210 - k * 16
        pts = [(256 + r * math.cos(math.pi + math.pi * t / 30), 300 + r * math.sin(math.pi + math.pi * t / 30)) for t in range(31)]
        s.line(pts, col, 14, wobble=1.5, passes=3)
    for cx in (60, 452):
        for k in range(4):
            s.circle((cx - 30 + k * 20, 292 - (k % 2) * 14), 24, C["sky"], 4, fill=(0.95, 0.97, 1.0))
    s.circle((256, 230), 26, C["orange"], 4, fill=C["yellow"])
    # cat
    s.circle((256, 312), 22, C["black"], 4, fill=(0.6, 0.6, 0.62))
    s.poly([(238, 296), (242, 274), (252, 292)], C["black"], 4)
    s.poly([(274, 296), (270, 274), (260, 292)], C["black"], 4)
    s.text("I ♥ RAINBOWS", 256, 34, 26, C["pink"], 5)
    return s.image()


def draw_rocket(seed):
    s = Sheet(512, 362, seed=seed)
    s.fill([(20, 20), (492, 16), (496, 346), (16, 342)], (0.70, 0.80, 0.98), spacing=16, width=5)
    rng = s.rng
    for k in range(26):
        x, y = rng.uniform(10, 500), rng.uniform(10, 350)
        s.text("★", x, y, rng.uniform(10, 20), C["yellow"], 3)
    s.circle((430, 80), 40, C["yellow"], 5, fill=(0.98, 0.95, 0.70))
    s.circle((90, 270), 36, C["orange"], 5, fill=C["pink"])
    s.line([(40, 280), (140, 258)], C["yellow"], 6)
    body = [(236, 300), (236, 150), (256, 90), (276, 150), (276, 300)]
    s.fill(body, (0.85, 0.86, 0.90), 6)
    s.poly(body, C["black"], 5)
    s.fill([(236, 140), (256, 90), (276, 140)], C["red"], 5)
    s.circle((256, 200), 14, C["black"], 4, fill=C["sky"])
    for fx in (-1, 1):
        fin = [(256 + fx * 20, 250), (256 + fx * 48, 310), (256 + fx * 20, 300)]
        s.fill(fin, C["red"], 5)
        s.poly(fin, C["black"], 4)
    s.fill([(242, 304), (256, 352), (270, 304)], C["orange"], 5)
    s.text("TO SPACE!", 256, 30, 26, C["lime"], 5)
    return s.image()


def draw_dino(seed):
    s = Sheet(512, 362, seed=seed)
    s.line([(0, 330), (512, 324)], C["green"], 8)
    s.fill([(330, 330), (420, 160), (470, 160), (512, 330)], C["brown"], 8)
    s.fill([(420, 160), (445, 120), (470, 160)], C["red"], 6)
    body = [(80, 300), (120, 200), (200, 160), (260, 120), (300, 130), (300, 170), (250, 190), (240, 260), (270, 320), (230, 320),
            (200, 270), (150, 280), (140, 320), (100, 320)]
    s.fill(body, C["green"], 8)
    s.poly(body, (0.10, 0.45, 0.18), 6)
    for k in range(6):
        x = 130 + k * 22
        y = 200 - k * 12
        s.poly([(x, y), (x + 10, y - 22), (x + 20, y - 4)], C["orange"], 4)
    s.circle((280, 140), 5, C["black"], 3, fill=C["black"])
    s.line([(264, 168), (276, 160), (288, 166), (300, 160)], C["black"], 3)
    s.poly([(320, 70), (470, 60), (480, 110), (350, 116), (330, 136), (334, 112)], C["black"], 4)
    s.text("RAWR!", 405, 90, 30, C["red"], 5)
    return s.image()


# ---------------------------------------------------------------- posters

def poster_abc(seed):
    s = Sheet(512, 695, paper=(0.99, 0.97, 0.90), seed=seed)
    s.text("ABC", 256, 60, 64, C["red"], 9, wobble=0.4)
    letters = "ABCDEFGHIJKL"
    cols = list(C.values())
    for i, ch in enumerate(letters):
        cx, cy = 90 + (i % 3) * 166, 170 + (i // 3) * 130
        s.text(ch, cx - 28, cy, 52, cols[i % 9], 8, wobble=0.3)
        s.circle((cx + 38, cy + 4), 26, cols[(i + 3) % 9], 4, fill=cols[(i + 5) % 9])
    return s.image()


def poster_space(seed):
    s = Sheet(512, 670, paper=(0.10, 0.12, 0.30), seed=seed)
    rng = s.rng
    for k in range(60):
        x, y = rng.uniform(0, 512), rng.uniform(0, 670)
        s.d.ellipse([s.S((x - 1.5, y - 1.5)), s.S((x + 1.5, y + 1.5))], fill=(255, 250, 220))
    s.circle((-40, 360), 160, C["orange"], 8, fill=C["yellow"])
    planets = [(150, 18, C["brown"]), (205, 26, C["orange"]), (265, 28, C["sky"]), (322, 22, C["red"]), (390, 46, C["orange"]), (465, 38, C["yellow"])]
    for x, r, col in planets:
        s.circle((x, 360), r, C["black"], 3, fill=col)
    s.line([(420, 362), (512, 350)], (0.85, 0.80, 0.60), 5)
    s.text("PLANETS", 256, 70, 50, (0.98, 0.95, 0.80), 7, wobble=0.3)
    return s.image()


def poster_map(seed):
    s = Sheet(512, 370, paper=(0.62, 0.82, 0.96), seed=seed)
    land = [[(40, 80), (160, 60), (190, 140), (130, 200), (70, 180)], [(150, 230), (210, 220), (230, 320), (170, 340)],
            [(260, 70), (420, 60), (470, 150), (380, 190), (300, 150)], [(330, 220), (410, 230), (400, 300), (340, 290)],
            [(420, 280), (480, 290), (470, 330)]]
    for k, poly in enumerate(land):
        s.fill(poly, (C["green"], C["lime"], C["yellow"], C["orange"], C["green"])[k], 6, solid=0.4)
        s.poly(poly, (0.20, 0.40, 0.25), 4)
    s.circle((60, 310), 30, C["black"], 3)
    s.text("N", 60, 266, 18, C["red"], 4)
    s.text("WORLD", 256, 26, 30, C["blue"], 6, wobble=0.3)
    return s.image()


def poster_fightguy(seed):
    s = Sheet(512, 683, paper=(0.99, 0.86, 0.30), seed=seed)
    burst = []
    for k in range(24):
        a = 2 * math.pi * k / 24
        r = 230 if k % 2 == 0 else 160
        burst.append((256 + r * math.cos(a), 330 + r * math.sin(a)))
    s.fill(burst, C["red"], 7, solid=0.6)
    s.poly(burst, C["black"], 6)
    stick_fighter(s, 256, 470, "fightguy", 2.1)
    s.text("FIGHTGUY!", 256, 600, 58, C["blue"], 9, wobble=0.4)
    return s.image()


def poster_manki(seed):
    s = Sheet(512, 652, paper=(0.74, 0.90, 0.70), seed=seed)
    stick_fighter(s, 256, 470, "manki", 2.0)
    banana = [(372 + 80 * math.cos(a), 92 + 52 * math.sin(a)) for a in [math.pi * (0.15 + 0.7 * t / 10) for t in range(11)]]
    s.line(banana, C["yellow"], 16, wobble=0.6, gaps=0.0)
    s.line(banana[:2], C["brown"], 8, gaps=0.0)
    s.text("MANKI", 256, 590, 64, C["brown"], 10, wobble=0.4)
    return s.image()


def poster_kindness(seed):
    s = Sheet(512, 256, paper=(0.99, 0.98, 0.95), seed=seed)
    cols = [C["red"], C["orange"], C["yellow"], C["green"], C["blue"], C["purple"]]
    for k, col in enumerate(cols):
        r = 200 - k * 14
        pts = [(256 + r * math.cos(math.pi + math.pi * t / 30), 190 + r * 0.75 * math.sin(math.pi + math.pi * t / 30)) for t in range(31)]
        s.line(pts, col, 12, wobble=0.6, passes=2, gaps=0.0)
    s.text("BE KIND", 256, 222, 38, C["purple"], 7, wobble=0.3)
    return s.image()


def corkboard(seed):
    s = Sheet(512, 307, paper=(0.72, 0.52, 0.32), seed=seed)
    rng = s.rng
    a = np.asarray(s.img).astype(np.int16)
    noise = np.random.default_rng(seed).normal(0, 22, a.shape[:2])[..., None]
    s.img = Image.fromarray(np.clip(a + noise, 0, 255).astype(np.uint8))
    s.d = ImageDraw.Draw(s.img)
    s.poly([(4, 4), (508, 4), (508, 303), (4, 303)], (0.55, 0.36, 0.20), 10, wobble=0.2, gaps=0.0)
    notes = [(40, 30, 150, 120, P.PAPER), (180, 40, 300, 150, P.CARD_LEMON), (330, 26, 470, 130, P.CARD_PINK),
             (60, 160, 200, 280, P.CARD_MINT), (240, 170, 350, 270, P.PAPER), (380, 160, 480, 280, P.CARD_LILAC)]
    cols = list(C.values())
    for k, (x0, y0, x1, y1, col) in enumerate(notes):
        s.d.rectangle([s.S((x0, y0)), s.S((x1, y1))], fill=rgb(col))
        for j in range(4):
            y = y0 + 22 + j * (y1 - y0 - 30) / 4
            s.line([(x0 + 12, y), (x1 - 12 - rng.uniform(0, 30), y + rng.uniform(-4, 4))], cols[(k + j) % 9], 4, passes=1)
        s.circle(((x0 + x1) / 2, y0 + 6), 6, C["red"], 3, fill=C["red"])
    return s.image()


def growth_chart(seed):
    s = Sheet(64, 538, paper=(0.99, 0.97, 0.90), seed=seed)
    cols = [C["red"], C["orange"], C["yellow"], C["green"], C["blue"], C["purple"]]
    for k in range(6):
        y0, y1 = k * 538 / 6, (k + 1) * 538 / 6
        s.d.rectangle([s.S((0, y0)), s.S((14, y1))], fill=rgb(cols[k]))
    for k in range(27):
        y = 520 - k * 19
        w = 26 if k % 5 == 0 else 16
        s.d.line([s.S((14, y)), s.S((14 + w, y))], fill=(40, 40, 50), width=3 * SS)
    for mark, y in ((3, 170), (4, 120)):
        s.line([(10, y), (60, y)], C["red"], 4, passes=1)
    return s.image()


def painting(seed):
    s = Sheet(400, 514, paper=(0.97, 0.96, 0.92), seed=seed)
    s.fill([(0, 0), (400, 0), (400, 300), (0, 300)], C["sky"], 10, width=12, solid=0.5)
    s.fill([(0, 300), (400, 290), (400, 514), (0, 514)], C["green"], 10, width=12, solid=0.5)
    s.circle((320, 80), 46, C["orange"], 10, fill=C["yellow"])
    for kind, x in (("fightguy", 70), ("manki", 160), ("wibou", 245), ("bonk", 330)):
        stick_fighter(s, x, 420, kind, 0.8)
    for k in range(3):
        s.circle((60 + k * 40, 80 - k * 6), 24, (0.92, 0.94, 0.98), 6, fill=(0.98, 0.99, 1.0))
    return s.image()


def block_letter(letter, seed):
    s = Sheet(128, 128, paper=(0.99, 0.99, 0.98), seed=seed)
    col = {"A": C["red"], "B": C["blue"], "C": C["green"], "D": C["orange"]}[letter]
    strokes, w = font.text_strokes(letter, 76, origin=(64, 26), align="center")
    for st in strokes:
        s.d.line([s.S((x, 128 - y)) for x, y in st], fill=rgb(col), width=14 * SS, joint="curve")
    s.d.rectangle([s.S((5, 5)), s.S((123, 123))], outline=rgb(col), width=5 * SS)
    return s.image()


def atlas(path):
    img = Image.new("RGB", (SIZE, SIZE), (255, 255, 255))
    makers = {
        "house": draw_house, "fighters": draw_fighters, "rainbow": draw_rainbow, "rocket": draw_rocket, "dino": draw_dino,
        "corkboard": corkboard, "map": poster_map, "kindness": poster_kindness, "abc": poster_abc, "space": poster_space,
        "fightguy": poster_fightguy, "manki": poster_manki, "painting": painting, "growth": growth_chart,
    }
    for k, (name, make) in enumerate(makers.items()):
        x, y, w, h = SLOTS[name]
        tile = make(100 + k)
        if name in ("house", "fighters", "rainbow", "rocket", "dino"):
            tile = _paper_grain(tile, 6, 40 + k)
        img.paste(tile.resize((w, h)), (x, y))
    for k, letter in enumerate(LETTERS):
        x, y, w, h = SLOTS["letter_" + letter]
        img.paste(block_letter(letter, 300 + k), (x, y))
    img.save(path)
    return path


def detail(path, size=512, seed=11):
    """Tileable soft grain: paper tooth plus a faint fabric weave (mean ~0.5)."""
    rng = np.random.default_rng(seed)
    base = rng.normal(0.0, 1.0, (size, size))
    f = np.fft.fft2(base)
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    falloff = 1.0 / (1.0 + (np.sqrt(fx ** 2 + fy ** 2) * size / 10.0) ** 2)
    smooth = np.real(np.fft.ifft2(f * falloff))
    smooth = (smooth - smooth.mean()) / (smooth.std() + 1e-6)
    yy, xx = np.mgrid[0:size, 0:size]
    weave = np.sin(xx * 2 * math.pi / 8) * np.sin(yy * 2 * math.pi / 8)
    grain = rng.normal(0.0, 1.0, (size, size))
    v = 0.5 + 0.07 * smooth + 0.035 * weave + 0.04 * grain
    Image.fromarray(np.clip(v * 255, 0, 255).astype(np.uint8), "L").save(path)
    return path
