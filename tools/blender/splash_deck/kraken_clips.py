"""Splash Deck kraken clips: every frame is solved from authored paths, never blended from key poses.

A pose is the Root bone's transform plus one path per arm (`N_PATH` points with an "up" vector
each, in the armature frame). `Rig.lay()` turns a pose into bone transforms by a chord walk: each
bone's head sits where the previous one ended and its tail is the point further along the path at
exactly the bone's rest length, so bones never stretch. The bone's z axis (the back; suckers face
away from it) follows the path's up vector. Arms are all the same length; every shape has a knob
`k` that only deepens a dive under the water, and `fit()` bisects it so the path is exactly as long
as the arm. So spare length always goes under the water.

Frames are in the kraken's own frame: the presenter places the kraken object at an anchor (on the
water, yaw from kraken.json) and the clips move the Root bone. Shapes are authored in Unity stage
coordinates around that anchor and converted.

Clips (30 fps; the presenter samples each phase by normalised time, so lengths are free):
  lurk                    in-place swimming loop under the water; the eyes and cap surface once a loop
  <side>.<lane>.rise      lurk frame 0 -> surfaced beside that rail, the slam arm climbing the hull
  <side>.<lane>.warn      loop: the tip curled over the rail at the lane, below WARN_MAX_HEIGHT
  <side>.<lane>.slam      whip up and down; ends lying across the lane, full width and full beam
  <side>.<lane>.lie       loop: lying there, squirming a little
  <side>.<lane>.withdraw  dragged back off the deck, then sinks to lurk frame 0
  jab                     idle flourish from its own anchor: surfaces off the transom, pokes the
                          NO FIGHTING ON DECK sign three times, sinks to lurk frame 0
"""
from __future__ import annotations

import math

import numpy as np
from mathutils import Euler, Matrix, Vector

import bay as BAY
import kraken as K
import layout as LAYOUT

W = BAY.WATER_Y
FPS = 30
FRAMES = {"lurk": 240, "rise": 72, "warn": 48, "slam": 16, "lie": 48, "withdraw": 72, "jab": 180}
LOOPS = ("lurk", "warn", "lie")
PHASES = ("rise", "warn", "slam", "lie", "withdraw")
SIDES = (("port", 1.0), ("starboard", -1.0))
LANES = ("aft", "mid", "fore")

HEAD_OFFSET = 9.0      # metres along the hull from the lane to the head (port: aft, starboard: fore)
HEAD_CLEAR = 1.2       # metres between the head's front and the widest deck edge beside it
LIFT = 2.5 * K.S       # surfaced Root height above the water (v2's LIFT)
LEAN = math.radians(6.0)        # surfaced: leaning in over the rail
SLAM_LEAN = math.radians(9.0)
WARN_MAX_HEIGHT = 4.0  # owner, 2026-11-04: during the warning no part of the arm rises above this (m above deck)
N_PATH = 40
LEAD = K.S             # metres: a short straight lead out of the body along the arm's rest direction

Y = np.array((0.0, 1.0, 0.0))


# ---------------------------------------------------------------- small maths

def B(p):
    """Unity (x, y, z) -> Blender."""
    return Vector((-p[0], -p[2], p[1]))


def U(v):
    """Blender -> Unity."""
    return np.array((-v[0], v[2], -v[1]))


def unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def window(t, a, b):
    """0 before a, 1 after b, smooth between."""
    return smooth((t - a) / (b - a)) if b > a else float(t >= a)


def catmull(P, per):
    P = np.asarray(P, float)
    if len(P) == 1:
        return P.copy()
    Pp = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]])
    p0, p1, p2, p3 = Pp[:-3, None], Pp[1:-2, None], Pp[2:-1, None], Pp[3:, None]
    t = (np.arange(per) / per)[None, :, None]
    out = 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t ** 2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
    return np.vstack([out.reshape(-1, 3), P[-1:]])


def lerp_rows(Uv, per):
    Uv = np.asarray(Uv, float)
    if len(Uv) == 1:
        return Uv.copy()
    t = (np.arange(per) / per)[None, :, None]
    out = Uv[:-1, None] * (1 - t) + Uv[1:, None] * t
    return np.vstack([out.reshape(-1, 3), Uv[-1:]])


def path_len(P, per=10):
    d = np.diff(catmull(P, per), axis=0)
    return float(np.sqrt((d * d).sum(1)).sum())


def cumlen(pts):
    d = np.sqrt((np.diff(pts, axis=0) ** 2).sum(1))
    return np.concatenate([[0.0], np.cumsum(d)])


def sample(pts, ups, s_tab, s):
    """Positions and ups at arc lengths s (array) along a dense polyline."""
    s = np.clip(s, 0, s_tab[-1])
    i = np.clip(np.searchsorted(s_tab, s, side="right") - 1, 0, len(pts) - 2)
    seg = np.maximum(s_tab[i + 1] - s_tab[i], 1e-9)
    f = ((s - s_tab[i]) / seg)[:, None]
    return pts[i] * (1 - f) + pts[i + 1] * f, ups[i] * (1 - f) + ups[i + 1] * f


def truncate(ctrl, r, per=12):
    """The first fraction r (0..1) of a control path's length, as a dense list of (pos, up)."""
    P = np.array([p for p, u in ctrl], float)
    Uv = np.array([u for p, u in ctrl], float)
    pts, ups = catmull(P, per), lerp_rows(Uv, per)
    s_tab = cumlen(pts)
    if r >= 1.0:
        return list(zip(pts[::3], ups[::3])) + [(pts[-1], ups[-1])]
    end = max(r, 0.0) * s_tab[-1]
    keep = s_tab < end
    out_p, out_u = list(pts[keep][::3]), list(ups[keep][::3])
    p_end, u_end = sample(pts, ups, s_tab, np.array([end]))
    out_p.append(p_end[0])
    out_u.append(u_end[0])
    return list(zip(out_p, out_u))


# ---------------------------------------------------------------- frames, spots and poses

class Spot:
    """Where the kraken object sits: an anchor on the water and a facing (Unity map direction)."""

    def __init__(self, name, anchor, face, side=0.0, lane=None, lane_x=0.0, half_beam=0.0, water=W, inward=None):
        self.name = name
        self.anchor = np.array(anchor, float)
        self.face = unit(np.array((face[0], 0.0, face[1])))
        self.s = side
        self.lane = lane
        self.lx = lane_x
        self.hb = half_beam
        self.water = water
        theta = math.atan2(-self.face[0], self.face[2])
        self.F = Matrix.Translation(B(self.anchor)) @ Matrix.Rotation(theta, 4, "Z")
        self.Finv = self.F.inverted()
        self.n = np.array((0.0, 0.0, side))   # outward through the near rail
        self.n_in = -self.n if inward is None else unit(np.array(inward, float))

    @property
    def yaw(self):
        """Unity yaw (degrees) of the kraken object: its local +Z (the face) turned onto `face`."""
        return math.degrees(math.atan2(self.face[0], self.face[2]))

    def world(self, p_arm):
        return U(self.F @ Vector(p_arm))

    def world_dir(self, d_arm):
        return U(self.F.to_3x3() @ Vector(d_arm))

    def to_arm(self, P, Uv):
        R = np.array(self.Finv.to_3x3())
        t = np.array(self.Finv.translation)
        Pb = np.stack([-P[:, 0], -P[:, 2], P[:, 1]], 1)
        Ub = np.stack([-Uv[:, 0], -Uv[:, 2], Uv[:, 1]], 1)
        return Pb @ R.T + t, Ub @ R.T


LOCAL = Spot("local", (0.0, 0.0, 0.0), (0.0, 1.0), water=0.0)   # the kraken frame itself: water at y = 0


class Pose:
    """Root offset (armature-space position and small Euler) plus per-arm paths (N_PATH x 3, armature space)."""

    def __init__(self, pos, rot, arms):
        self.pos = np.asarray(pos, float)
        self.rot = np.asarray(rot, float)
        self.arms = arms

    def T(self):
        return Matrix.Translation(Vector(self.pos)) @ Euler(tuple(self.rot), "XYZ").to_matrix().to_4x4()


def blend(a, b, w, arm_w=None):
    """w: root weight; arm_w: optional {arm name: weight or per-point weight array}."""
    arms = {}
    for name, (Pa, Ua) in a.arms.items():
        Pb, Ub = b.arms[name]
        x = w if arm_w is None else arm_w.get(name, w)
        x = np.asarray(x, float).reshape(-1, 1) if np.ndim(x) else x
        Uv = Ua * (1 - x) + Ub * x
        arms[name] = (Pa * (1 - x) + Pb * x, Uv / np.maximum(np.linalg.norm(Uv, axis=1, keepdims=True), 1e-9))
    return Pose(a.pos * (1 - w) + b.pos * w, a.rot * (1 - w) + b.rot * w, arms)


def root_T(pos, rot):
    return Matrix.Translation(Vector(pos)) @ Euler(tuple(rot), "XYZ").to_matrix().to_4x4()


def arm_frame(spot, T, arm):
    """Base and lead point (Unity, around the spot) and the horizontal outward direction of an arm."""
    base = T @ arm.base_m
    lead = T @ (arm.base_m + arm.dir * LEAD)
    b, l = spot.world(base), spot.world(lead)
    d = spot.world_dir(T.to_3x3() @ arm.dir)
    d[1] = 0.0
    return b, l, unit(d)


def fit(prefix, shape, L, hi=8.0):
    """Bisect k in [0, hi] so prefix + shape(k) is L long; shape returns [(pos, up)] in Unity space."""
    pre = [np.asarray(p, float) for p in prefix]

    def length(k):
        return path_len(np.array(pre + [p for p, u in shape(k)]))
    if length(0.0) >= L:
        return shape(0.0)
    lo = 0.0
    while length(hi) < L and hi < 400:
        hi *= 2
    for _ in range(32):
        k = (lo + hi) / 2
        if length(k) < L:
            lo = k
        else:
            hi = k
    return shape((lo + hi) / 2)


def make_path(spot, T, arm, ctrl):
    """Resample a fitted Unity path (after the lead point) to N_PATH points in the armature frame."""
    b, l, d = arm_frame(spot, T, arm)
    P = np.array([b, l] + [np.asarray(p, float) for p, u in ctrl])
    Uv = np.array([Y, Y] + [unit(np.asarray(u, float)) for p, u in ctrl])
    pts, ups = catmull(P, 12), lerp_rows(Uv, 12)
    s_tab = cumlen(pts)
    s_lead = s_tab[12]
    s = np.linspace(s_lead, s_lead + max(arm.length_m - s_lead, 1.0), N_PATH + 1)[1:]
    ps, us = sample(pts, ups, s_tab, s)
    return spot.to_arm(ps, us)


# ---------------------------------------------------------------- shapes (Unity stage space)

def spare_shape(b, d, k, lift, sgn, water, wob=0.0):
    """A thick hump beside the body, a dive that takes the spare length, then the tip surfaces and curls."""
    side = np.array((-d[2], 0.0, d[0])) * sgn
    def y(h):
        return np.array((0.0, water - b[1] + h, 0.0))
    sw = side * 0.6 * math.sin(wob)
    cu = 0.5 * math.sin(wob + 1.3)
    return [(b + d * 3.0 + y(-1.5), Y),
            (b + d * 6.5 + side * 0.5 + y(2.0 + 2.5 * lift), Y),
            (b + d * 10.0 + side * 1.0 + y(-3.0), Y),
            (b + d * 12.0 + side * 1.5 + y(-4.0 - 8.0 * k), Y),
            (b + d * 14.5 + side * 2.0 + y(-3.0), Y),
            (b + d * 16.5 + side * 2.5 + sw + y(1.5), Y),
            (b + d * 17.5 + side * 2.5 + sw + y(4.5 + 1.0 * lift + cu), d),
            (b + d * 16.3 + side * 2.5 + sw + y(6.5 + 1.0 * lift + cu), d + Y),
            (b + d * 15.2 + side * 2.5 + sw + y(5.5 + 1.0 * lift + cu), Y)]


def off_hull(ctrl, r):
    """Push control points that would sit inside the hull out past its side or transom."""
    hull = LAYOUT.SPEC["hull"]
    out = []
    for p, u in ctrl:
        p = np.array(p, float)
        half = LAYOUT.half_beam(p[0]) if hull["stern_x"] <= p[0] <= hull["bow_x"] else 0.0
        if W - 0.5 < p[1] < 0.5 + r and half > 0 and abs(p[2]) < half + r + 0.4:
            p[2] = math.copysign(half + r + 0.4, p[2] if p[2] else 1.0)
        out.append((p, u))
    return out


def away_from_boat(d, n_in):
    """Turn a spare arm's direction away from the boat so it never reaches the deck."""
    dn = float(d @ n_in)
    if dn > -0.35:
        d = d - n_in * (dn + 0.35)
    return unit(d)


def lurk_shape(b, d, k, arm, t):
    """Kraken frame (water at y = 0, facing +Z): arms reach out, sweep back along the body in a
    swimming wave and dive; spare length goes down."""
    back = np.array((0.0, 0.0, -1.0))
    side = np.array((-d[2], 0.0, d[0]))
    pulse = 1.0 + 0.18 * math.sin(2 * math.pi * 2 * t)
    pts = []
    spec = ((4.0, 0.0, -1.5), (7.0, 3.0, -3.5), (8.0, 9.0, -5.0), (8.0, 15.0, -6.0 - 10 * k), (7.0, 20.0, -9.0 - 18 * k))
    for j, (out, aft, dy) in enumerate(spec):
        wave = 1.4 * j / 4 * math.sin(2 * math.pi * 2 * t - 0.9 * j - 0.4 * arm.i)
        pts.append((b + d * out * (pulse if j else 1.0) + back * aft + side * wave + np.array((0.0, dy, 0.0)), Y))
    return pts


def back_of(t_vec, n_in):
    """Hero arm in its lane plane: the back (z axis) is the tangent turned 90 degrees, so a path that
    climbs the hull keeps its suckers on the hull, lies suckers down on the deck and droops suckers
    to the far hull."""
    return unit((t_vec @ n_in) * Y - (t_vec @ Y) * n_in)


def lane_route(spot, qy, x_shift=None):
    """(q, y) points in the lane plane -> [(pos, up)]: q is metres inboard from the near hull side."""
    pts = [np.array((spot.lx + (x_shift[i] if x_shift is not None else 0.0), y, spot.s * (spot.hb - q)))
           for i, (q, y) in enumerate(qy)]
    out = []
    for i, p in enumerate(pts):
        t_vec = unit(pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)])
        out.append((p, back_of(t_vec, spot.n_in)))
    return out


def hero_lead_in(spot, b, d, k):
    """Out of the body, down into the water and a dive (k) on the way to the foot of the hull at the lane."""
    foot = np.array((spot.lx, spot.water - 1.5, spot.s * (spot.hb + 1.8)))
    mid = (b + foot) / 2
    mid[1] = spot.water - 3.0 - 12.0 * k
    first = b + d * 4.5
    first[1] = spot.water + 0.5
    return [(first, Y), (mid, Y)]


def lie_y(arm, L, rem):
    """Centreline height that rests the hero's sucker plates on the deck, `rem` metres before the tip."""
    return arm.underside_m(max(0.0, L - rem)) + 0.05


def route_qy(spot, kind, arm, t=0.0, squash=0.0):
    hb = spot.hb
    span = 2 * hb
    L = arm.length_m
    if kind == "warn":
        tw = 0.22 * math.sin(2 * math.pi * 2 * t) + 0.12 * math.sin(2 * math.pi * 3 * t + 0.7)
        slide = 0.3 * math.sin(2 * math.pi * t)
        return [(-1.8, spot.water - 1.5), (-1.3, -4.5), (-0.95, 0.9), (1.8 + slide, 1.2), (4.2 + slide, 1.15),
                (6.2 + slide, 1.3), (7.4 + slide, 1.95 + tw), (7.0 + slide, 2.65 + tw), (6.1 + slide, 2.45 + tw)]
    if kind == "apex":
        return [(-1.8, spot.water - 1.5), (-1.3, -4.0), (-0.6, 3.0), (2.5, 8.0), (span * 0.35, 10.5),
                (span * 0.62, 9.6), (span * 0.82, 6.5), (span * 0.9, 3.5)]
    if kind == "lie":
        qs = [-1.8, -1.3, -0.95] + list(np.linspace(1.5, span - 1.5, 6)) + [span + 0.95, span + 1.3, span + 1.4]
        ys = [spot.water - 1.5, -4.5, 0.95] + [1.2] * 6 + [0.95, -4.0, -8.5]
        qy = list(zip(qs, ys))
        # rest the deck points on the sucker plates (the plates shrink towards the tip)
        rem = cumlen(np.array([(0.0, y, q) for q, y in qy]))
        rem = rem[-1] - rem
        out = []
        for i, (q, y) in enumerate(qy):
            if 3 <= i <= 8:
                y = lie_y(arm, L, rem[i]) - squash
            elif i in (2, 9):
                y = 0.95 - squash * 0.5
            out.append((q, y))
        return out
    raise ValueError(kind)


def hero_path(spot, T, arm, kind, r=1.0, t=0.0, squash=0.0, squirm=0.0):
    b, l, d = arm_frame(spot, T, arm)
    qy = route_qy(spot, kind, arm, t, squash)
    shift = None
    if squirm:
        shift = [squirm * 0.3 * math.sin(2 * math.pi * (t + 0.13 * i)) * (1.0 if 3 <= i else 0.0) for i in range(len(qy))]
        qy = [(q, y + (0.6 * squirm * math.sin(2 * math.pi * (t + 0.2)) if i >= len(qy) - 2 else 0.0))
              for i, (q, y) in enumerate(qy)]
    route = lane_route(spot, qy, shift)
    if r < 1.0:
        route = truncate(route, r)
    ctrl = fit([b, l], lambda k: hero_lead_in(spot, b, d, k) + route, arm.length_m)
    return make_path(spot, T, arm, ctrl)


def spare_path(spot, T, arm, t=0.0, idle=1.0):
    b, l, d = arm_frame(spot, T, arm)
    d = away_from_boat(d, spot.n_in) if np.any(spot.n_in) else d
    front = abs(((arm.angle + math.pi) % (2 * math.pi)) - math.pi) < math.radians(60)
    lift = (-0.5 if front else 0.8 + 0.25 * (arm.i % 3)) + idle * 0.25 * math.sin(2 * math.pi * t + arm.i)
    sgn = 1 if arm.i % 2 else -1
    wob = idle * (2 * math.pi * t + 1.1 * arm.i)
    r = arm.r_at(0) * K.S
    ctrl = fit([b, l], lambda k: off_hull(spare_shape(b, d, k, lift, sgn, spot.water, wob), r), arm.length_m)
    return make_path(spot, T, arm, ctrl)


def jab_route(spot, poke=0.0):
    """Up the transom, over the wheelhouse roof, the tip a finger on the NO FIGHTING ON DECK sign."""
    hull = LAYOUT.SPEC["hull"]
    x0, z0, x1, z1 = LAYOUT.SPEC["raised"]["Wheelhouse"]["footprint"]
    top = LAYOUT.SPEC["raised"]["Wheelhouse"]["top"]
    st = hull["stern_x"]
    zc = (z0 + z1) / 2
    aft = np.array((-1.0, 0.0, 0.0))
    fwd = -aft
    sign = np.array((x1 + 0.55 - poke, 0.85, zc + 0.3))   # the sign is painted on the wheelhouse front (boat.py)
    return [(np.array((st - 1.6, spot.water - 1.5, zc + 2.0)), aft),
            (np.array((st - 1.4, -4.0, zc + 1.8)), aft),
            (np.array((st - 0.9, 0.9, zc + 1.6)), aft + Y),
            (np.array((x0 - 0.9, 1.4, zc + 1.3)), Y),
            (np.array((x0 + 0.6, top + 1.5, zc + 1.0)), Y),
            (np.array(((x0 + x1) / 2, top + 1.4, zc + 0.8)), Y),
            (np.array((x1 - 0.6, top + 1.1, zc + 0.6)), Y + fwd),
            (np.array((x1 + 0.9, top - 0.2, zc + 0.45)), fwd + Y * 0.4),
            (sign, unit(fwd + Y * 0.3))]


def jab_path(spot, T, arm, r=1.0, poke=0.0):
    b, l, d = arm_frame(spot, T, arm)
    route = jab_route(spot, poke)
    if r < 1.0:
        route = truncate(route, r)

    def shape(k):
        first = b + d * 4.0
        first[1] = spot.water + 0.5
        dip = (b + route[0][0]) / 2
        dip[1] = spot.water - 3.0 - 12.0 * k
        return [(first, Y), (dip, Y)] + route
    return make_path(spot, T, arm, fit([b, l], shape, arm.length_m))


# ---------------------------------------------------------------- the clip set

class Kraken:
    def __init__(self, arms, ext):
        self.arms = arms
        self.ext = ext
        self.spots = self._spots()
        port_mid = self.spots["port.mid"]
        T = root_T(self.surfaced_pos(), self.surfaced_rot())
        rail = np.array((port_mid.lx, 0.0, port_mid.s * port_mid.hb))

        def facing(a, spot, target):
            b, l, d = arm_frame(spot, T, a)
            return float(d @ unit((target - b) * np.array((1.0, 0.0, 1.0))))
        self.hero = max(arms, key=lambda a: facing(a, port_mid, rail))
        for name, spot in self.spots.items():
            if not name.startswith(("port.", "starboard.")):
                continue
            best = max(arms, key=lambda a: facing(a, spot, np.array((spot.lx, 0.0, spot.s * spot.hb))))
            if best is not self.hero:
                raise AssertionError("%s would slam with %s, not the hero arm %s" % (name, best.name, self.hero.name))
        jab = self.spots["jab"]
        hull = LAYOUT.SPEC["hull"]
        transom = np.array((hull["stern_x"], 0.0, 1.0))
        self.jabber = max([a for a in arms if a is not self.hero], key=lambda a: facing(a, jab, transom))
        self.hero.hero = True
        # Surface and lurk heights (Root above the anchor on the water).
        self.deep = -(ext["top"] + 1.0)
        self.peek = -ext["eyeZ"] + 0.3 * ext["eyeRadius"]

    # ---- placement
    def _spots(self):
        ext = self.ext
        lanes = {lane["name"].split("_", 1)[1]: lane for lane in LAYOUT.slam_lanes()}
        spots = {}
        for side_name, s in SIDES:
            for lane_name in LANES:
                lane = lanes[lane_name]
                lx = lane["center"][0]
                ax = lx + (-HEAD_OFFSET if s > 0 else HEAD_OFFSET)   # the boat's 180-degree symmetry: one hero arm
                widest = max(LAYOUT.half_beam(x) for x in np.linspace(ax - ext["halfWidth"], ax + ext["halfWidth"], 61))
                az = s * (widest + ext["front"] + HEAD_CLEAR)
                spots["%s.%s" % (side_name, lane_name)] = Spot(
                    "%s.%s" % (side_name, lane_name), (ax, W, az), (0.0, -s), s, lane["name"], lx, LAYOUT.half_beam(lx))
        hull = LAYOUT.SPEC["hull"]
        spots["jab"] = Spot("jab", (hull["stern_x"] - ext["front"] - HEAD_CLEAR, W, 3.0), (1.0, 0.0), inward=(1.0, 0.0, 0.0))
        return spots

    def surfaced_pos(self, dz=0.0):
        return np.array((0.0, 0.0, LIFT + dz))

    def surfaced_rot(self, lean=LEAN):
        return np.array((lean, 0.0, 0.0))

    # ---- arm length: the longest reach any clip needs, plus 3 %
    def arm_length(self):
        need = {}
        T = root_T(self.surfaced_pos(), self.surfaced_rot())
        hero = self.hero
        for name, spot in self.spots.items():
            if name == "jab":
                b, l, d = arm_frame(spot, T, self.jabber)
                route = jab_route(spot)
                need[name + ".jab"] = path_len(np.array([b, l, b + d * 4.0, (b + route[0][0]) / 2] + [p for p, u in route]))
                continue
            b, l, d = arm_frame(spot, T, hero)
            for kind in ("warn", "apex", "lie"):
                hero.length = 1e3   # only the lie heights read the length; refined below
                route = lane_route(spot, route_qy(spot, kind, hero))
                need["%s.%s" % (name, kind)] = path_len(np.array([b, l] + [p for p, u in hero_lead_in(spot, b, d, 0.0) + route]))
        L = max(need.values()) * 1.03
        for a in self.arms:
            a.length = L / K.S
        return L, need

    # ---- poses
    def lurk(self, t):
        bob = 0.35 * math.sin(2 * math.pi * 2 * t)
        z = self.deep + (self.peek - self.deep) * (window(t, 0.30, 0.42) - window(t, 0.62, 0.76)) + bob
        rot = np.array((math.radians(2.0) * math.sin(2 * math.pi * t), math.radians(2.5) * math.sin(2 * math.pi * t + 1.0),
                        math.radians(3.0) * math.sin(2 * math.pi * t + 2.0)))
        T = root_T((0.0, 0.0, z), rot)
        arms = {}
        for a in self.arms:
            b, l, d = arm_frame(LOCAL, T, a)
            arms[a.name] = make_path(LOCAL, T, a, fit([b, l], lambda k: lurk_shape(b, d, k, a, t), a.length_m))
        return Pose((0.0, 0.0, z), rot, arms)

    def surfaced(self, spot, hero_fn, t=0.0, dz=0.0, lean=LEAN, idle=1.0, actor=None):
        actor = actor or self.hero
        pos, rot = self.surfaced_pos(dz), self.surfaced_rot(lean)
        T = root_T(pos, rot)
        arms = {}
        for a in self.arms:
            arms[a.name] = hero_fn(T, a) if a is actor else spare_path(spot, T, a, t, idle)
        return Pose(pos, rot, arms)

    def variant_clips(self, spot):
        hero = self.hero
        n = FRAMES
        lurk0 = self.lurk(0.0)

        def warn(t, r=1.0, dz=0.0):
            return self.surfaced(spot, lambda T, a: hero_path(spot, T, a, "warn", r, t), t,
                                 dz=dz + 0.25 * math.sin(2 * math.pi * t))

        clips = {}
        # rise: up out of the lurk with the slam arm climbing the hull, then over the rail into the warn pose
        frames = []
        for i in range(n["rise"] + 1):
            t = i / n["rise"]
            w = window(t, 0.0, 0.62)
            over = 0.9 * math.sin(math.pi * window(t, 0.45, 1.0))   # overshoot, then settle
            reach = 0.32 + 0.68 * window(t, 0.55, 1.0)
            up = warn(0.0, reach, dz=over)
            frames.append(blend(lurk0, up, w) if w < 1.0 else up)
        clips["rise"] = frames
        clips["warn"] = [warn(i / n["warn"]) for i in range(n["warn"] + 1)]

        # slam: warn -> high arch -> down onto the lane (base first, tip last) -> settle
        def lie(t, squirm=1.0, r=1.0, lean=SLAM_LEAN, squash=0.0, dz=0.0):
            return self.surfaced(spot, lambda T, a: hero_path(spot, T, a, "lie", r, t, squash, squirm), t, dz=dz, lean=lean)

        w0 = clips["warn"][0]
        lie0 = lie(0.0)
        apex = self.surfaced(spot, lambda T, a: hero_path(spot, T, a, "apex"), 0.0, dz=0.6, lean=LEAN - math.radians(3))
        squash = lie(0.0, squash=0.18, dz=-0.4, lean=SLAM_LEAN + math.radians(2))
        frames = []
        for i in range(n["slam"] + 1):
            t = i / n["slam"]
            if t <= 0.35:
                p = blend(w0, apex, smooth(t / 0.35))
            elif t <= 0.75:
                u = (t - 0.35) / 0.4
                per = np.clip(u * 1.6 - np.linspace(0, 0.6, N_PATH), 0, 1) ** 1.6
                p = blend(apex, squash, smooth(u), {hero.name: per})
            else:
                p = blend(squash, lie0, smooth((t - 0.75) / 0.25))
            frames.append(p)
        frames[-1] = lie0
        clips["slam"] = frames
        clips["lie"] = [lie(i / n["lie"]) for i in range(n["lie"])] + [lie0]

        # withdraw: drag the arm back along its own route off the deck and down the hull, then sink
        frames = [lie0]
        for i in range(1, n["withdraw"]):
            t = i / n["withdraw"]
            r = 1.0 - window(t, 0.0, 0.55)
            drag = lie(0.0, squirm=1.0 - window(t, 0.0, 0.3), r=max(r, 0.02),
                       lean=SLAM_LEAN + (LEAN - SLAM_LEAN) * window(t, 0.0, 0.5))
            w = window(t, 0.45, 1.0)
            frames.append(blend(drag, lurk0, w) if w > 0 else drag)
        frames.append(lurk0)
        clips["withdraw"] = frames
        return clips

    def jab_clip(self):
        spot = self.spots["jab"]
        lurk0 = self.lurk(0.0)
        n = FRAMES["jab"]
        frames = []
        for i in range(n + 1):
            t = i / n
            reach = 0.3 + 0.7 * window(t, 0.22, 0.45) - 0.75 * window(t, 0.7, 0.86)
            poke = 0.45 * max(0.0, math.sin(2 * math.pi * 3 * window(t, 0.47, 0.68))) if 0.47 < t < 0.68 else 0.0
            up = self.surfaced(spot, lambda T, a: jab_path(spot, T, a, reach, poke), t,
                               dz=0.25 * math.sin(2 * math.pi * t), actor=self.jabber)
            w = window(t, 0.0, 0.25) * (1.0 - window(t, 0.8, 1.0))
            frames.append(up if w >= 1.0 else blend(lurk0, up, w))
        frames[0] = frames[-1] = lurk0
        return frames

    def all_clips(self, log=print):
        clips = {"lurk": [self.lurk(i / FRAMES["lurk"]) for i in range(FRAMES["lurk"] + 1)]}
        clips["lurk"][-1] = clips["lurk"][0]
        log("clip lurk", len(clips["lurk"]))
        for name, spot in self.spots.items():
            if name == "jab":
                continue
            for phase, frames in self.variant_clips(spot).items():
                clips["%s.%s" % (name, phase)] = frames
            log("clips", name)
        clips["jab"] = self.jab_clip()
        log("clip jab", len(clips["jab"]))
        return clips


# ---------------------------------------------------------------- poses -> bone transforms

class Rig:
    """Rest data of the built armature, and the chord walk that lays a Pose onto it."""

    def __init__(self, rig, arms):
        self.arms = arms
        bones = rig.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.rel_inv = {b.name: ((b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local).inverted()
                        for b in bones}
        self.names = [b.name for b in bones]

    def lay(self, pose):
        """{bone: matrix_basis} for every bone."""
        T = pose.T()
        P_root = T @ self.rest["Root"]
        out = {"Root": self.rest["Root"].inverted() @ P_root, "Head": Matrix.Identity(4)}
        R = np.array(T.to_3x3())
        up0 = R @ np.array((0.0, 0.0, 1.0))
        for arm in self.arms:
            base = np.array(T @ arm.base_m)
            lead = np.array(T @ (arm.base_m + arm.dir * LEAD))
            Pp, Up = pose.arms[arm.name]
            ctrl = np.vstack([base, lead, Pp])
            ups = np.vstack([up0, up0, Up])
            pts, upl = catmull(ctrl, 6), lerp_rows(ups, 6)
            tan = unit(pts[-1] - pts[-2])
            ext = pts[-1] + tan * (np.arange(1, 31)[:, None] * 2.0)
            pts = np.vstack([pts, ext])
            upl = np.vstack([upl, np.repeat(upl[-1:], 30, 0)])
            parent = P_root
            l = arm.length_m / arm.n
            head = pts[0]
            j = 0
            prev_z = None
            for k in range(arm.n):
                while j < len(pts) - 2 and np.linalg.norm(pts[j + 1] - head) < l:
                    j += 1
                a, b = pts[j], pts[j + 1]
                dv = b - a
                f = a - head
                qa, qb, qc = dv @ dv, 2 * (f @ dv), f @ f - l * l
                disc = max(0.0, qb * qb - 4 * qa * qc)
                t = min(1.0, max(0.0, (-qb + math.sqrt(disc)) / (2 * qa))) if qa > 1e-12 else 0.0
                tail = a + dv * t
                up = upl[j] * (1 - t) + upl[j + 1] * t
                y = unit(tail - head)
                z = up - y * (up @ y)
                if np.linalg.norm(z) < 1e-3:
                    z = prev_z if prev_z is not None else np.array((0.0, 0.0, 1.0))
                z = unit(z)
                prev_z = z
                x = np.cross(y, z)
                M = Matrix(((x[0], y[0], z[0], head[0]), (x[1], y[1], z[1], head[1]),
                            (x[2], y[2], z[2], head[2]), (0, 0, 0, 1)))
                name = arm.bone(k)
                out[name] = self.rel_inv[name] @ parent.inverted() @ M
                parent = M
                head = y * l + head
        return out


def apply_frame(rig, frame):
    """Pose the Blender rig with one laid frame ({bone: matrix_basis})."""
    import bpy
    for name, basis in frame.items():
        rig.pose.bones[name].matrix_basis = basis
    bpy.context.view_layer.update()
