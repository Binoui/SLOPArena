"""Original procedural textures for After Hours.

atlas (2048 x 2048, RGBA; alpha = StageNeon glow mask):
  billboards: eight 512 x 256 backlit ads in u 0..0.5, v 0.5..1 (see BILLBOARDS)
  screens:    four 256 x 256 LED screens in u 0.5..1, v 0.75..0.875
  white:      solid white, alpha 1, u 0.97..1, v 0.97..1 (WHITE_UV in geo.py)
detail: tileable grayscale grit for StageVertexLit's triplanar detail.

All imagery is drawn from code with the original sign font in font.py.
"""
from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

import font

SIZE = 2048
BB_W, BB_H = 512, 256
SCREEN = 256
WHITE_BLOCK = 64

# name -> (column, row) of a 512 x 256 billboard slot; v grows upward in UV space.
BILLBOARDS = ["slop_cola", "bonk_burger", "manki_motors", "wibou_watches",
              "fightguy_gym", "after_hours", "ramen", "karaoke"]
SCREENS = ["arena_tonight", "heart", "sale", "dj_set"]


def billboard_uv(name):
    """((u0, v0), (u1, v1)) of a billboard slot with a half-texel inset."""
    i = BILLBOARDS.index(name)
    col, row = i % 2, i // 2
    x0, y0 = col * BB_W, row * BB_H  # image space, y down
    return _uv_rect(x0, y0, BB_W, BB_H)


def screen_uv(name):
    i = SCREENS.index(name)
    x0, y0 = 1024 + i * SCREEN, SCREEN
    return _uv_rect(x0, y0, SCREEN, SCREEN)


def _uv_rect(x0, y0, w, h, inset=2):
    u0, u1 = (x0 + inset) / SIZE, (x0 + w - inset) / SIZE
    v1, v0 = 1 - (y0 + inset) / SIZE, 1 - (y0 + h - inset) / SIZE
    return (u0, v0), (u1, v1)


# ---------------------------------------------------------------- drawing kit

class Canvas:
    """Supersampled RGB drawing with an additive glow layer."""

    def __init__(self, w, h, background, ss=2):
        self.ss = ss
        self.w, self.h = w * ss, h * ss
        self.img = Image.new("RGB", (self.w, self.h), background)
        self.glow = Image.new("RGB", (self.w, self.h), (0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.g = ImageDraw.Draw(self.glow)

    def S(self, v):
        return v * self.ss

    def gradient(self, top, bottom, box=None):
        x0, y0, x1, y1 = box or (0, 0, self.w / self.ss, self.h / self.ss)
        for y in range(int(self.S(y0)), int(self.S(y1))):
            t = (y - self.S(y0)) / max(1, self.S(y1) - self.S(y0))
            c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
            self.d.line([(self.S(x0), y), (self.S(x1), y)], fill=c)

    def text(self, text, cx, cy, height, color, width=None, glow=None, align="center", tracking=1.0):
        strokes, w = font.text_strokes(text, height, origin=(0, 0), align=align, tracking=tracking)
        lw = width or max(2, height * 0.16)
        for s in strokes:
            pts = [(self.S(cx + x), self.S(cy + height / 2 - y)) for x, y in s]
            if glow:
                self.g.line(pts, fill=glow, width=int(self.S(lw * 2.6)), joint="curve")
            self.d.line(pts, fill=color, width=int(self.S(lw)), joint="curve")
            for p in (pts[0], pts[-1]):
                r = self.S(lw) / 2
                self.d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=color)
        return w

    def finish(self, blur=10):
        glow = self.glow.filter(ImageFilter.GaussianBlur(self.S(blur)))
        out = ImageChops.add(self.img, glow)
        return out.resize((self.w // self.ss, self.h // self.ss), Image.LANCZOS)


def _star(d, cx, cy, r, fill):
    pts = []
    for k in range(10):
        a = -math.pi / 2 + k * math.pi / 5
        rr = r if k % 2 == 0 else r * 0.45
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    d.polygon(pts, fill=fill)


# ---------------------------------------------------------------- billboards

def slop_cola():
    c = Canvas(BB_W, BB_H, (196, 22, 34))
    c.gradient((214, 30, 40), (150, 10, 24))
    d, S = c.d, c.S
    # white swoosh
    pts = [(S(x), S(150 + 38 * math.sin(x / 70.0))) for x in range(0, 513, 8)]
    d.line(pts, fill=(250, 246, 240), width=int(S(22)))
    # bottle
    bx = 430
    d.rounded_rectangle([S(bx - 26), S(70), S(bx + 26), S(232)], S(18), fill=(64, 22, 14))
    d.rectangle([S(bx - 11), S(30), S(bx + 11), S(80)], fill=(64, 22, 14))
    d.rectangle([S(bx - 13), S(24), S(bx + 13), S(34)], fill=(230, 200, 60))
    d.rounded_rectangle([S(bx - 26), S(120), S(bx + 26), S(170)], S(4), fill=(250, 246, 240))
    c.text("SLOP", bx, 145, 18, (196, 22, 34), width=3.4)
    c.text("SLOP COLA", 205, 78, 42, (255, 250, 240), width=8, glow=(120, 20, 30))
    c.text("DRINK THE SLOP", 205, 204, 22, (255, 226, 120), width=4.2)
    return c.finish(6)


def bonk_burger():
    c = Canvas(BB_W, BB_H, (250, 178, 30))
    c.gradient((255, 196, 40), (236, 128, 20))
    d, S = c.d, c.S
    cx, cy = 104, 128
    d.ellipse([S(cx - 74), S(cy - 70), S(cx + 74), S(cy + 6)], fill=(214, 128, 44))
    for k in range(9):
        x = cx - 44 + k * 11
        d.ellipse([S(x), S(cy - 50 + (k % 2) * 6), S(x + 5), S(cy - 46 + (k % 2) * 6)], fill=(255, 236, 170))
    d.rectangle([S(cx - 78), S(cy + 2), S(cx + 78), S(cy + 16)], fill=(70, 170, 50))
    d.rounded_rectangle([S(cx - 76), S(cy + 14), S(cx + 76), S(cy + 38)], S(10), fill=(110, 46, 24))
    d.polygon([(S(cx - 80), S(cy + 38)), (S(cx + 80), S(cy + 38)), (S(cx + 60), S(cy + 50)), (S(cx - 70), S(cy + 46))], fill=(255, 210, 40))
    d.rounded_rectangle([S(cx - 74), S(cy + 46), S(cx + 74), S(cy + 78)], S(14), fill=(214, 128, 44))
    c.text("BONK", 350, 70, 62, (150, 30, 20), width=11)
    c.text("BURGER", 350, 148, 40, (150, 30, 20), width=7.5)
    c.text("OPEN 24H", 350, 214, 24, (255, 255, 240), width=4.6)
    return c.finish(5)


def manki_motors():
    c = Canvas(BB_W, BB_H, (16, 60, 120))
    c.gradient((24, 90, 160), (8, 30, 70))
    d, S = c.d, c.S
    # speed lines
    for k in range(7):
        y = 150 + k * 12
        d.line([(S(20 + k * 6), S(y)), (S(150 + k * 10), S(y))], fill=(120, 220, 255), width=int(S(3)))
    # stylized car
    d.rounded_rectangle([S(170), S(150), S(470), S(205)], S(22), fill=(240, 70, 60))
    d.polygon([(S(230), S(152)), (S(270), S(112)), (S(380), S(112)), (S(430), S(152))], fill=(240, 70, 60))
    d.polygon([(S(250), S(150)), (S(278), S(122)), (S(322), S(122)), (S(322), S(150))], fill=(170, 230, 255))
    d.polygon([(S(332), S(150)), (S(332), S(122)), (S(374), S(122)), (S(404), S(150))], fill=(170, 230, 255))
    for wx in (230, 410):
        d.ellipse([S(wx - 27), S(180), S(wx + 27), S(234)], fill=(20, 20, 26))
        d.ellipse([S(wx - 12), S(195), S(wx + 12), S(219)], fill=(190, 190, 200))
    c.text("MANKI MOTORS", 256, 44, 38, (255, 255, 255), width=6.8, glow=(40, 120, 255))
    c.text("FAST. LOUD. MOSTLY SAFE.", 256, 92, 16, (255, 220, 90), width=3.0)
    return c.finish(6)


def wibou_watches():
    c = Canvas(BB_W, BB_H, (30, 14, 50))
    c.gradient((56, 22, 92), (14, 6, 26))
    d, S = c.d, c.S
    cx, cy, r = 120, 128, 86
    d.ellipse([S(cx - r - 10), S(cy - r - 10), S(cx + r + 10), S(cy + r + 10)], fill=(214, 174, 80))
    d.ellipse([S(cx - r), S(cy - r), S(cx + r), S(cy + r)], fill=(250, 246, 236))
    for k in range(12):
        a = k * math.pi / 6
        d.line([(S(cx + math.cos(a) * r * 0.78), S(cy + math.sin(a) * r * 0.78)),
                (S(cx + math.cos(a) * r * 0.92), S(cy + math.sin(a) * r * 0.92))], fill=(40, 30, 60), width=int(S(4)))
    # owl-eye dial: two pupils as hands' hub
    for ex in (-24, 24):
        d.ellipse([S(cx + ex - 18), S(cy - 30), S(cx + ex + 18), S(cy + 6)], fill=(255, 196, 40))
        d.ellipse([S(cx + ex - 8), S(cy - 20), S(cx + ex + 8), S(cy - 4)], fill=(20, 14, 30))
    d.polygon([(S(cx - 8), S(cy + 8)), (S(cx + 8), S(cy + 8)), (S(cx), S(cy + 22))], fill=(230, 140, 30))
    c.text("WIBOU", 360, 92, 58, (250, 236, 200), width=10, glow=(140, 60, 220))
    c.text("TIME TO FLY", 360, 176, 26, (255, 200, 80), width=4.6)
    return c.finish(7)


def _fist(d, S, cx, cy, s, fill, outline):
    def rr(x0, y0, x1, y1, r):
        d.rounded_rectangle([S(cx + x0 * s), S(cy + y0 * s), S(cx + x1 * s), S(cy + y1 * s)], S(r * s), fill=fill, outline=outline, width=int(S(2)))
    rr(-0.62, -1.02, -0.22, -0.40, 0.12)
    rr(-0.26, -1.18, 0.14, -0.40, 0.12)
    rr(0.10, -1.08, 0.50, -0.40, 0.12)
    rr(0.46, -0.92, 0.84, -0.36, 0.12)
    rr(-0.70, -0.62, 0.86, 0.38, 0.18)
    rr(-0.46, 0.30, 0.60, 1.05, 0.08)
    d.rectangle([S(cx - 0.5 * s), S(cy + 0.52 * s), S(cx + 0.64 * s), S(cy + 0.74 * s)], fill=(214, 30, 30))


def fightguy_gym():
    c = Canvas(BB_W, BB_H, (16, 16, 18))
    c.gradient((34, 30, 32), (10, 10, 12))
    d, S = c.d, c.S
    for k in range(18):
        a = k * math.pi / 9
        d.polygon([(S(110), S(128)), (S(110 + math.cos(a) * 160), S(128 + math.sin(a) * 160)),
                   (S(110 + math.cos(a + 0.17) * 160), S(128 + math.sin(a + 0.17) * 160))], fill=(60, 18, 16))
    _fist(d, S, 110, 120, 58, (246, 196, 66), (90, 50, 10))
    c.text("FIGHTGUY", 345, 70, 38, (255, 255, 255), width=7.4, glow=(200, 30, 20))
    c.text("GYM", 340, 136, 40, (230, 40, 30), width=8)
    c.text("TRAIN HARD. HIT HARDER.", 345, 206, 14, (255, 220, 120), width=2.7)
    return c.finish(6)


def after_hours_poster():
    c = Canvas(BB_W, BB_H, (40, 6, 50))
    c.gradient((120, 16, 110), (22, 6, 52))
    d, S = c.d, c.S
    # moon and skyline
    d.ellipse([S(446), S(12), S(496), S(62)], fill=(255, 236, 200))
    d.ellipse([S(436), S(6), S(482), S(52)], fill=(116, 16, 108))
    rng = random.Random(4)
    x = 0
    while x < BB_W:
        w = rng.randint(22, 46)
        h = rng.randint(30, 90)
        d.rectangle([S(x), S(BB_H - h), S(x + w), S(BB_H)], fill=(18, 6, 32))
        for wy in range(BB_H - h + 8, BB_H - 6, 10):
            for wx in range(x + 5, x + w - 5, 8):
                if rng.random() < 0.4:
                    d.rectangle([S(wx), S(wy), S(wx + 3), S(wy + 4)], fill=(255, 190, 110))
        x += w + 2
    c.text("AFTER HOURS", 236, 84, 40, (255, 200, 240), width=7, glow=(255, 40, 170))
    c.text("ROOFTOP ★ OPEN TIL DAWN", 246, 146, 18, (120, 240, 255), width=3.4, glow=(30, 160, 220))
    return c.finish(8)


def ramen():
    c = Canvas(BB_W, BB_H, (240, 236, 222))
    c.gradient((250, 244, 228), (226, 214, 190))
    d, S = c.d, c.S
    cx = 120
    d.pieslice([S(cx - 92), S(70), S(cx + 92), S(230)], 0, 180, fill=(200, 30, 30))
    d.rectangle([S(cx - 92), S(146), S(cx + 92), S(152)], fill=(150, 20, 20))
    d.ellipse([S(cx - 92), S(132), S(cx + 92), S(162)], fill=(250, 224, 170))
    for k in range(5):
        x = cx - 40 + k * 20
        d.arc([S(x - 10), S(40), S(x + 10), S(80)], 90, 270, fill=(140, 140, 150), width=int(S(4)))
        d.arc([S(x - 10), S(80), S(x + 10), S(120)], 270, 90, fill=(140, 140, 150), width=int(S(4)))
    d.line([(S(cx + 30), S(150)), (S(cx + 120), S(40))], fill=(70, 40, 20), width=int(S(5)))
    d.line([(S(cx + 44), S(152)), (S(cx + 136), S(50))], fill=(70, 40, 20), width=int(S(5)))
    c.text("RAMEN", 350, 80, 64, (200, 30, 30), width=11)
    c.text("NOODLE BAR", 350, 160, 28, (40, 30, 30), width=5)
    c.text("LATE NIGHT", 350, 212, 20, (200, 30, 30), width=3.6)
    return c.finish(4)


def karaoke():
    c = Canvas(BB_W, BB_H, (10, 30, 60))
    c.gradient((14, 60, 110), (40, 10, 70))
    d, S = c.d, c.S
    for k in range(14):
        rng = random.Random(k)
        _star(d, S(rng.randint(10, 500)), S(rng.randint(10, 246)), S(rng.randint(5, 12)), (255, 236, 120))
    cx = 90
    d.ellipse([S(cx - 34), S(40), S(cx + 34), S(108)], fill=(200, 200, 214))
    for k in range(4):
        d.line([(S(cx - 30), S(54 + k * 14)), (S(cx + 30), S(54 + k * 14))], fill=(120, 120, 140), width=int(S(2)))
    d.polygon([(S(cx - 14), S(104)), (S(cx + 14), S(104)), (S(cx + 8), S(200)), (S(cx - 8), S(200))], fill=(40, 40, 50))
    c.text("KARAOKE", 320, 92, 52, (255, 255, 255), width=8.6, glow=(255, 60, 200))
    c.text("SING IT LIKE YOU MEAN IT", 320, 172, 16, (120, 240, 255), width=3.0)
    return c.finish(6)


# ---------------------------------------------------------------- LED screens

def screen(name):
    c = Canvas(SCREEN, SCREEN, (8, 8, 14))
    d, S = c.d, c.S
    if name == "arena_tonight":
        c.gradient((200, 30, 120), (40, 20, 120))
        c.text("SLOP", 128, 70, 44, (255, 255, 255), width=7, glow=(255, 120, 220))
        c.text("ARENA", 128, 130, 44, (255, 230, 90), width=7)
        c.text("TONIGHT", 128, 196, 26, (120, 240, 255), width=4.4)
    elif name == "heart":
        c.gradient((20, 10, 40), (60, 10, 60))
        strokes, _ = font.text_strokes("♥", 150, origin=(0, 0))
        for s in strokes:
            d.polygon([(S(128 + x), S(128 + 75 - y)) for x, y in s], fill=(255, 40, 120))
    elif name == "sale":
        c.gradient((255, 220, 40), (255, 140, 20))
        c.text("50%", 128, 96, 70, (180, 20, 40), width=12)
        c.text("OFF", 128, 190, 44, (40, 20, 60), width=8)
    elif name == "dj_set":
        c.gradient((10, 20, 50), (60, 10, 90))
        for k in range(16):
            h = 40 + 150 * abs(math.sin(k * 0.9 + 0.4))
            col = (80 + k * 10, 200 - k * 6, 255)
            d.rectangle([S(12 + k * 15), S(240 - h), S(22 + k * 15), S(240)], fill=col)
        c.text("DJ SET", 128, 50, 40, (255, 255, 255), width=6.6, glow=(140, 80, 255))
    return c.finish(6)


DRAW = {"slop_cola": slop_cola, "bonk_burger": bonk_burger, "manki_motors": manki_motors,
        "wibou_watches": wibou_watches, "fightguy_gym": fightguy_gym, "after_hours": after_hours_poster,
        "ramen": ramen, "karaoke": karaoke}


def atlas(path):
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    for i, name in enumerate(BILLBOARDS):
        col, row = i % 2, i // 2
        art = DRAW[name]().convert("RGBA")
        img.paste(art, (col * BB_W, row * BB_H))
    for i, name in enumerate(SCREENS):
        img.paste(screen(name).convert("RGBA"), (1024 + i * SCREEN, SCREEN))
    white = Image.new("RGBA", (WHITE_BLOCK, WHITE_BLOCK), (255, 255, 255, 255))
    img.paste(white, (SIZE - WHITE_BLOCK, 0))
    img.save(path)
    return path


def detail(path, size=512, seed=11):
    """Tileable grit (spectral noise) centred on 0.5 for StageVertexLit's detail map."""
    rng = np.random.default_rng(seed)
    noise = rng.standard_normal((size, size))
    f = np.fft.fft2(noise)
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    r = np.sqrt(fx * fx + fy * fy) + 1.0 / size
    field = np.real(np.fft.ifft2(f / r ** 1.05))
    field = (field - field.mean()) / (field.std() + 1e-9)
    speck = rng.standard_normal((size, size)) * 0.3
    v = np.clip(0.5 + 0.14 * field + 0.05 * speck, 0, 1)
    Image.fromarray((v * 255).astype(np.uint8), "L").save(path)
    return path


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    atlas(out + "/AfterHoursAtlas.png")
    detail(out + "/AfterHoursDetail.png")
