"""Neon signage: tube and strip lettering, blade signs, billboards, screens, bulbs.

Glowing faces use the AH_Night material with vertex alpha 1 (StageNeon glow mask);
dark backing, frames and supports use alpha 0. Animation channels follow geo.py.
"""
from __future__ import annotations

import math

import font
from stagekit import geo
import palette as P
import textures

NIGHT = "AH_Night"


class Plane:
    """A vertical sign plane: origin, horizontal unit `right`, outward unit normal."""

    def __init__(self, origin, right, normal):
        self.o = origin
        self.r = right
        self.n = normal

    @staticmethod
    def facing(origin, normal):
        """Plane whose right vector keeps text readable when viewed from `normal`."""
        nx, nz = normal
        L = math.hypot(nx, nz)
        nx, nz = nx / L, nz / L
        return Plane(origin, (-nz, 0.0, nx), (nx, 0.0, nz))

    def at(self, u, v, off=0.0):
        o, r, n = self.o, self.r, self.n
        return (o[0] + r[0] * u + n[0] * off, o[1] + v, o[2] + r[2] * u + n[2] * off)


def strip(m, plane, a, b, width, off, color, mat=NIGHT):
    """Flat glowing segment from 2D a to b on the plane (facing the plane normal)."""
    du, dv = b[0] - a[0], b[1] - a[1]
    L = math.hypot(du, dv)
    if L < 1e-6:
        return
    pu, pv = -dv / L * width / 2, du / L * width / 2
    eu, ev = du / L * width / 2, dv / L * width / 2  # extend ends so joints overlap
    pts = [(a[0] - eu - pu, a[1] - ev - pv), (b[0] + eu - pu, b[1] + ev - pv),
           (b[0] + eu + pu, b[1] + ev + pv), (a[0] - eu + pu, a[1] - ev + pv)]
    geo.oriented(m, [plane.at(u, v, off) for u, v in pts], plane.n, color, mat)


def strokes_flat(m, plane, strokes, width, off, color, anim=None):
    with geo.state(m, alpha=1.0, anim=anim):
        for s in strokes:
            for a, b in zip(s, s[1:]):
                strip(m, plane, a, b, width, off, color)


def strokes_tube(m, plane, strokes, radius, off, color, anim=None, segments=6):
    with geo.state(m, alpha=1.0, anim=anim):
        for s in strokes:
            geo.tube(m, [plane.at(u, v, off) for u, v in s], radius, segments, color, NIGHT)


def text(m, plane, string, height, center=(0.0, 0.0), color=P.MAGENTA, style="flat", off=0.03,
         width=None, anim=None, tracking=1.0, per_letter_anim=None):
    """Horizontal text centred at plane-space `center`. Returns its width."""
    strokes, w = font.text_strokes(string, height, origin=(center[0], center[1] - height / 2), tracking=tracking)
    lw = width or height * 0.11
    if per_letter_anim:
        # Animate letters individually (e.g. one faulty letter); keyed by stroke order.
        s_idx = 0
        for i, ch in enumerate(string):
            glyph = font.GLYPHS.get(ch.upper() if ch.upper() in font.GLYPHS else ch)
            if not glyph:
                continue
            letter = strokes[s_idx:s_idx + len(glyph)]
            s_idx += len(glyph)
            a = per_letter_anim(i, ch)
            _draw(m, plane, letter, style, lw, off, color, a)
    else:
        _draw(m, plane, strokes, style, lw, off, color, anim)
    return w


def _draw(m, plane, strokes, style, lw, off, color, anim):
    if style == "tube":
        strokes_tube(m, plane, strokes, lw / 2, off + lw / 2, color, anim)
    else:
        strokes_flat(m, plane, strokes, lw, off, color, anim)


def vertical_text(m, plane, string, height, top_center, color, off=0.03, width=None, anim=None, spacing=1.3):
    strokes, total = font.vertical_text_strokes(string, height, origin=top_center, spacing=spacing)
    strokes_flat(m, plane, strokes, width or height * 0.12, off, color, anim)
    return total


def panel(m, plane, u0, v0, u1, v1, color, off=0.0, depth=0.0, uvs=None, glow=False, anim=None, mat=NIGHT):
    """Rectangle on the plane; optional thickness `depth` behind it (towards -normal)."""
    alpha = 1.0 if glow else 0.0
    quv = None
    if uvs is not None:
        (a0, b0), (a1, b1) = uvs
        quv = [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
    with geo.state(m, alpha=alpha, anim=anim):
        geo.oriented(m, [plane.at(u0, v0, off), plane.at(u1, v0, off), plane.at(u1, v1, off), plane.at(u0, v1, off)],
                     plane.n, color, mat, quv)
    if depth > 0:
        with geo.state(m, alpha=0.0):
            for (a, b) in (((u0, v0), (u1, v0)), ((u1, v0), (u1, v1)), ((u1, v1), (u0, v1)), ((u0, v1), (u0, v0))):
                pts = [plane.at(a[0], a[1], off), plane.at(b[0], b[1], off), plane.at(b[0], b[1], off - depth), plane.at(a[0], a[1], off - depth)]
                mid = ((a[0] + b[0]) / 2 - (u0 + u1) / 2, (a[1] + b[1]) / 2 - (v0 + v1) / 2)
                out = (plane.r[0] * mid[0], mid[1], plane.r[2] * mid[0])
                geo.oriented(m, pts, out, geo.scale(color, 0.7) if not glow else P.STEEL_DARK, mat)


def bulbs(m, points, color, size=0.14, rate=102.0, mat=NIGHT):
    """Chase-light bulbs: every third bulb shares a phase so the run marches along."""
    for i, p in enumerate(points):
        with geo.state(m, alpha=1.0, anim=(rate, (i % 3) / 3.0)):
            geo.box(m, p, (size, size, size), color, mat)


def bulb_line(a, b, spacing):
    n = max(1, int(math.dist(a, b) / spacing))
    return [tuple(a[k] + (b[k] - a[k]) * i / n for k in range(3)) for i in range(n + 1)]


def billboard(m, plane, width, height, image, bottom, legs=True, frame=P.STEEL_DARK, lights=True, rng=None):
    """Backlit atlas billboard standing on a roof (plane.o at the roof level, centre)."""
    u0, u1 = -width / 2, width / 2
    v0, v1 = bottom, bottom + height
    panel(m, plane, u0, v0, u1, v1, P.NEON_WHITE, off=0.0, uvs=textures.billboard_uv(image), glow=True)
    with geo.state(m, alpha=0.0):
        # frame border and back
        for a, b in (((u0 - 0.25, v0 - 0.25), (u1 + 0.25, v0)), ((u0 - 0.25, v1), (u1 + 0.25, v1 + 0.25)),
                     ((u0 - 0.25, v0), (u0, v1)), ((u1, v0), (u1 + 0.25, v1))):
            panel(m, plane, a[0], a[1], b[0], b[1], frame, off=0.05, depth=0.35)
        back = Plane(plane.at(0, 0, -0.32), (-plane.r[0], 0, -plane.r[2]), (-plane.n[0], 0, -plane.n[2]))
        panel(m, back, u0 - 0.25, v0 - 0.25, u1 + 0.25, v1 + 0.25, frame, off=0.0)
        if legs:
            for u in (u0 + width * 0.18, u1 - width * 0.18):
                for off in (-0.3, -1.6):
                    p0 = plane.at(u, 0.0, off)
                    p1 = plane.at(u, v0 - 0.2, off)
                    geo.box(m, ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2),
                            (0.22, abs(p1[1] - p0[1]), 0.22), frame, NIGHT)
            # catwalk
            a = plane.at(u0, v0 - 0.3, 0.6)
            b = plane.at(u1, v0 - 0.3, -0.4)
            geo.box_minmax(m, (min(a[0], b[0]), a[1] - 0.08, min(a[2], b[2])), (max(a[0], b[0]), a[1], max(a[2], b[2])), frame, NIGHT)
    if lights:
        # gooseneck lamps washing the face
        for k in range(4):
            u = u0 + width * (k + 0.5) / 4
            arm = plane.at(u, v0 - 0.15, 1.1)
            with geo.state(m, alpha=0.0):
                geo.cylinder_between(m, plane.at(u, v0 - 0.15, 0.1), arm, 0.04, 4, frame, NIGHT)
            with geo.state(m, alpha=1.0):
                geo.box(m, arm, (0.3, 0.12, 0.3), P.NEON_WHITE, NIGHT)


def screen(m, plane, width, height, image, center, off=0.04, anim=None):
    u0, u1 = center[0] - width / 2, center[0] + width / 2
    v0, v1 = center[1] - height / 2, center[1] + height / 2
    panel(m, plane, u0 - 0.3, v0 - 0.3, u1 + 0.3, v1 + 0.3, P.STEEL_DARK, off=off, depth=0.4)
    panel(m, plane, u0, v0, u1, v1, P.NEON_WHITE, off=off + 0.02, uvs=textures.screen_uv(image), glow=True, anim=anim)


def blade(m, base, normal, depth, thickness, top, bottom, string, color, back=P.STEEL_DARK, bulb_color=P.GOLD,
          letter_height=None, outline=None):
    """Vertical projecting sign: base = wall point (x, z), sticks out along `normal`."""
    nx, nz = normal
    L = math.hypot(nx, nz)
    nx, nz = nx / L, nz / L
    rx, rz = -nz, nx
    cx, cz = base[0] + nx * depth / 2, base[1] + nz * depth / 2
    h = top - bottom
    with geo.state(m, alpha=0.0):
        corners = [(cx - nx * depth / 2 - rx * thickness / 2, cz - nz * depth / 2 - rz * thickness / 2),
                   (cx + nx * depth / 2 - rx * thickness / 2, cz + nz * depth / 2 - rz * thickness / 2),
                   (cx + nx * depth / 2 + rx * thickness / 2, cz + nz * depth / 2 + rz * thickness / 2),
                   (cx - nx * depth / 2 + rx * thickness / 2, cz - nz * depth / 2 + rz * thickness / 2)]
        if geo.polygon_area(corners) < 0:
            corners.reverse()
        geo.prism(m, corners, bottom, top, back, NIGHT, top=True, bottom=True)
    lh = letter_height or min(depth * 0.55, h / (len(string) * 1.3 + 0.4))
    for side in (1, -1):
        plane = Plane((cx + rx * thickness / 2 * side, bottom, cz + rz * thickness / 2 * side), (nx * side * -1, 0, nz * side * -1), (rx * side, 0, rz * side))
        plane = Plane.facing((cx + rx * thickness / 2 * side, bottom, cz + rz * thickness / 2 * side), (rx * side, rz * side))
        total = len(string) * lh * 1.3
        vertical_text(m, plane, string, lh, (0.0, (h + total) / 2 - lh * 0.15), color, off=0.02)
        if outline:
            strokes = [[(-depth / 2 + 0.12, 0.12), (depth / 2 - 0.12, 0.12), (depth / 2 - 0.12, h - 0.12), (-depth / 2 + 0.12, h - 0.12), (-depth / 2 + 0.12, 0.12)]]
            strokes_flat(m, plane, strokes, 0.07, 0.02, outline)
    # bulbs on the outer edge
    outer = (base[0] + nx * depth, base[1] + nz * depth)
    pts = bulb_line((outer[0] + nx * 0.08, bottom + 0.2, outer[1] + nz * 0.08), (outer[0] + nx * 0.08, top - 0.2, outer[1] + nz * 0.08), 0.45)
    bulbs(m, pts, bulb_color, size=0.16)


def marquee(m, plane, width, depth, y, thickness, string, color, letter_height=0.5, bulb_color=P.GOLD):
    """Entrance canopy projecting from a wall plane; letters on its front fascia."""
    u0, u1 = -width / 2, width / 2
    with geo.state(m, alpha=0.0):
        a = plane.at(u0, y, 0.0)
        b = plane.at(u1, y, depth)
        lo = (min(a[0], b[0]), y - thickness, min(a[2], b[2]))
        hi = (max(a[0], b[0]), y, max(a[2], b[2]))
        geo.box_minmax(m, lo, hi, P.STEEL_DARK, NIGHT)
    front = Plane(plane.at(0, 0, depth + 0.001), plane.r, plane.n)
    text(m, front, string, letter_height, center=(0.0, y - thickness / 2), color=color, off=0.01)
    pts = bulb_line(plane.at(u0 + 0.15, y + 0.08, depth - 0.1), plane.at(u1 - 0.15, y + 0.08, depth - 0.1), 0.4)
    pts += bulb_line(plane.at(u0 + 0.15, y - thickness - 0.08, depth - 0.1), plane.at(u1 - 0.15, y - thickness - 0.08, depth - 0.1), 0.4)
    bulbs(m, pts, bulb_color)
    # soffit glow
    with geo.state(m, alpha=1.0):
        s0 = plane.at(u0 + 0.3, y - thickness - 0.01, 0.3)
        s1 = plane.at(u1 - 0.3, y - thickness - 0.01, depth - 0.3)
        geo.oriented(m, [(s0[0], s0[1], s0[2]), (s1[0], s0[1], s0[2]), (s1[0], s0[1], s1[2]), (s0[0], s0[1], s1[2])],
                     (0, -1, 0), geo.scale(P.WINDOW_WARM, 0.8), NIGHT)
