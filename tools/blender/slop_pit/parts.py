"""Small building blocks for Slop Pit: stand-side frames, beams, chain-link, chains and fans.

Coordinates are Unity world space (+X east, +Y up, +Z north). A `Side` maps a local
frame (u along the stand, y up, w outward from the ring centre) onto one of the four
stands, so each stand is authored once and rotated by quarter turns.
"""
from __future__ import annotations

import math

from stagekit import geo


class Side:
    """Stand frame k (0 north, 1 east, 2 south, 3 west); +w points away from the ring."""

    def __init__(self, k):
        self.k = k
        self.yaw = k * math.pi / 2

    def xz(self, u, w):
        return ((u, w), (w, -u), (-u, -w), (-w, u))[self.k]

    def P(self, u, y, w):
        x, z = self.xz(u, w)
        return (x, y, z)

    def dir(self, du, dw):
        """World (dx, dz) of a local direction."""
        return self.xz(du, dw)

    @property
    def inward(self):
        x, z = self.xz(0.0, -1.0)
        return (x, 0.0, z)


def rect(m, side, u0, u1, y0, y1, w, color, mat=None, facing=-1, uvs=None):
    """Vertical rectangle at depth w; facing -1 looks at the ring, +1 away from it."""
    pts = [side.P(u0, y0, w), side.P(u1, y0, w), side.P(u1, y1, w), side.P(u0, y1, w)]
    n = side.inward if facing < 0 else tuple(-c for c in side.inward)
    quv = None
    if uvs is not None:
        (a0, b0), (a1, b1) = uvs
        # Seen from the ring, +u is to the viewer's right on every stand (from behind, -u).
        if facing < 0:
            quv = [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
        else:
            quv = [(a1, b0), (a0, b0), (a0, b1), (a1, b1)]
    geo.oriented(m, pts, n, color, mat, quv)


def flat(m, side, u0, u1, w0, w1, y, color, mat=None, up=True):
    pts = [side.P(u0, y, w0), side.P(u1, y, w0), side.P(u1, y, w1), side.P(u0, y, w1)]
    geo.oriented(m, pts, (0.0, 1.0 if up else -1.0, 0.0), color, mat)


def end(m, side, u, y0, y1, w0, w1, color, mat=None, sign=1):
    """Vertical rectangle across w at u, facing +u (sign 1) or -u."""
    pts = [side.P(u, y0, w0), side.P(u, y0, w1), side.P(u, y1, w1), side.P(u, y1, w0)]
    dx, dz = side.dir(sign, 0.0)
    geo.oriented(m, pts, (dx, 0.0, dz), color, mat)


def box(m, side, center, size, color, mat=None, skip=(), **kw):
    """Box in the side frame: center (u, y, w), size (su, sy, sw)."""
    geo.box(m, side.P(*center), (size[0], size[1], size[2]), color, mat, skip=skip, yaw=side.yaw, **kw)


def beam(m, a, b, size, color, mat=None, caps=False):
    """Square-section bar between two world points (4-sided tube)."""
    geo.cylinder_between(m, a, b, size * 0.7071, 4, color, mat, caps=caps)


def ribbon(m, a, b, width, normal, color, mat=None):
    """Flat strip from a to b, `width` wide, facing `normal` (chain-link wires, cables)."""
    d = [b[i] - a[i] for i in range(3)]
    L = math.sqrt(sum(x * x for x in d)) or 1e-6
    d = [x / L for x in d]
    s = geo._norm(geo._cross(normal, d))
    h = width / 2
    pts = [tuple(a[i] - s[i] * h for i in range(3)), tuple(b[i] - s[i] * h for i in range(3)),
           tuple(b[i] + s[i] * h for i in range(3)), tuple(a[i] + s[i] * h for i in range(3))]
    geo.oriented(m, pts, normal, color, mat)


def _clip_line(p, d, u0, u1, y0, y1):
    """Segment of the line p + t d inside the rectangle [u0,u1] x [y0,y1] (or None)."""
    t0, t1 = -1e9, 1e9
    for axis, lo, hi in ((0, u0, u1), (1, y0, y1)):
        if abs(d[axis]) < 1e-12:
            if p[axis] < lo or p[axis] > hi:
                return None
            continue
        a, b = (lo - p[axis]) / d[axis], (hi - p[axis]) / d[axis]
        t0, t1 = max(t0, min(a, b)), min(t1, max(a, b))
    if t1 - t0 < 1e-3:
        return None
    return (p[0] + d[0] * t0, p[1] + d[1] * t0), (p[0] + d[0] * t1, p[1] + d[1] * t1)


def chain_link(m, side, u0, u1, y0, y1, w, spacing, wire, color, mat=None):
    """Diamond mesh fence panel at depth w: two families of 45-degree wires facing the ring."""
    n = side.inward
    for d in ((1.0, 1.0), (1.0, -1.0)):
        L = math.sqrt(2.0)
        dd = (d[0] / L, d[1] / L)
        perp = (-dd[1], dd[0])
        corners = [(u0, y0), (u1, y0), (u1, y1), (u0, y1)]
        cs = [c[0] * perp[0] + c[1] * perp[1] for c in corners]
        k0, k1 = math.floor(min(cs) / spacing), math.ceil(max(cs) / spacing)
        for k in range(k0, k1 + 1):
            c = k * spacing
            p = (perp[0] * c, perp[1] * c)
            seg = _clip_line(p, dd, u0, u1, y0, y1)
            if seg is None:
                continue
            (a0, b0), (a1, b1) = seg
            ribbon(m, side.P(a0, b0, w), side.P(a1, b1, w), wire, n, color, mat)


def chain(m, top, bottom_y, link, color, mat=None, rng=None):
    """Hanging chain of alternating links from `top` down to bottom_y."""
    x, y, z = top
    k = 0
    while y - link > bottom_y:
        yaw = (math.pi / 2) * (k % 2) + (rng.uniform(-0.15, 0.15) if rng else 0.0)
        geo.box(m, (x, y - link / 2, z), (0.075, link * 1.15, 0.022), color, mat, yaw=yaw, skip=("top", "bottom"))
        y -= link * 0.82
        k += 1
    return (x, y, z)


def hook(m, p, color, mat=None):
    x, y, z = p
    geo.box(m, (x, y - 0.12, z), (0.05, 0.24, 0.05), color, mat)
    geo.box(m, (x + 0.07, y - 0.26, z), (0.18, 0.05, 0.05), color, mat)
    geo.box(m, (x + 0.15, y - 0.2, z), (0.05, 0.14, 0.05), color, mat)


# ---------------------------------------------------------------- fans

POSES = ("down", "fist", "both", "phone", "point", "cross")


def fan(m, side, u, w, floor_y, rng, palette, still=False):
    """One low-poly spectator facing the ring. Only faces the camera can see are built
    (front, sides, tops). The whole figure hops with the StageNeon bob channel; raised
    arms pump higher than the body at the same frequency and phase."""
    P = palette
    h = rng.uniform(0.9, 1.08)
    shirt = rng.choice(P.FAN_CLOTHES) if rng.random() < 0.08 else rng.choice(P.CLOTHES)
    skin = rng.choice(P.SKIN)
    hair = rng.choice(P.HAIR)
    pants = geo.scale(rng.choice(P.CLOTHES), 0.8)
    pose = rng.choices(POSES, weights=(30, 24, 16, 10, 8, 12))[0]
    hz = rng.uniform(1.5, 2.6)
    phase = rng.random()
    still = still or pose in ("cross",) or rng.random() < 0.12
    body_amp = 0.0 if still else rng.uniform(0.025, 0.07) * (2.4 if rng.random() < 0.12 else 1.0)
    arm_amp = body_amp + (0.0 if still else rng.uniform(0.08, 0.16))
    code = math.floor(hz * 10.0) + phase  # StageNeon bob: (amplitude m, Hz * 10 + phase)
    lean = rng.uniform(-0.06, 0.06)
    a = side.yaw + rng.uniform(-0.25, 0.25)

    def at(du, y, dw):
        """Local figure offsets (du right, y up, dw towards the ring is negative)."""
        x, z = side.xz(u, w)
        c, s = math.cos(a), math.sin(a)
        # rotate (du, dw) by the figure yaw around its own centre
        lx, lz = du * c + dw * s, -du * s + dw * c
        return (x + lx, floor_y + y, z + lz)

    def part(m_, center, size, color, top_color=None, amp=0.0, alpha=0.0, hide=()):
        cu, cy, cw = center
        with geo.state(m_, alpha=alpha, travel=(amp, code) if amp > 0 else geo.STILL, direction=geo.NO_DIR):
            geo.box(m_, at(cu, cy, cw), size, color, None, skip=("bottom", "n") + tuple(hide), yaw=a, top_color=top_color)

    hip = 0.86 * h
    shoulder = 1.42 * h
    head = 0.25 * h
    lit = lambda c: geo.mix(c, P.HAZE_HOT, 0.22)  # warm rim on top faces
    part(m, (0.0, hip / 2, 0.0), (0.34 * h, hip, 0.2), pants, amp=body_amp, hide=("top",))
    part(m, (0.0, (hip + shoulder) / 2, lean), (0.44 * h, shoulder - hip, 0.24), shirt, top_color=lit(shirt), amp=body_amp)
    part(m, (0.0, shoulder + head * 0.62, lean), (0.21 * h, head, 0.22), skin, top_color=hair, amp=body_amp)
    if rng.random() < 0.25:  # cap or hood
        part(m, (0.0, shoulder + head * 1.12, lean - 0.03), (0.23 * h, 0.07, 0.26), rng.choice(P.CLOTHES + P.FAN_CLOTHES), amp=body_amp)
    arm_len = 0.62 * h
    for sgn in (-1, 1):
        raised = pose == "both" or (pose in ("fist", "phone", "point") and sgn == 1)
        inner = ("e",) if sgn < 0 else ("w",)  # the face against the body is never seen
        if raised:
            reach = arm_len * (0.92 if pose != "point" else 0.75)
            cu = sgn * 0.26 * h
            cy = shoulder - 0.05 + reach / 2
            part(m, (cu, cy, lean - (0.12 if pose == "point" else 0.0)), (0.11, reach + 0.16, 0.11), shirt, amp=arm_amp,
                 hide=inner + ("top",))
            fist_y = shoulder - 0.05 + reach + 0.05
            part(m, (cu, fist_y, lean), (0.12, 0.12, 0.12), skin, amp=arm_amp, hide=inner)
            if pose == "phone":  # glowing screen (StageNeon: vertex alpha marks glow)
                part(m, (cu, fist_y + 0.12, lean - 0.07), (0.075, 0.13, 0.012), P.PHONE, amp=arm_amp, alpha=1.0)
        elif pose == "cross":
            part(m, (0.0, shoulder - 0.25, lean - 0.13), (0.42 * h, 0.12, 0.1), shirt, amp=body_amp)
        else:
            part(m, (sgn * 0.27 * h, shoulder - arm_len / 2 + 0.04, lean), (0.1, arm_len, 0.11), shirt, amp=body_amp,
                 hide=inner + ("top",))
    return pose
