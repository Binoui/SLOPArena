"""Splash Deck kraken hazard (concept B, "the grumpy neighbour"): mesh, rig, weights and AO.

One faceted vertex-coloured mesh skinned to one armature: `Root`, `Head` and eight
tentacle chains (`Arm0_00` ...). Everything here is built in metres in the kraken's
own frame (Blender axes: +Z up, the face looks along -Y, which the FBX export maps to
Unity local +Z). `kraken_clips.py` poses the chains; `kraken_build.py` is the entry point.

Every arm has the same length (set by the longest reach the clips need), so spare length
is hidden in dives under the water. The hero (slam) arm is the broad flat one: across the
deck it is exactly as wide as the slam lane, `layout.SLAM_HALF_WIDTH` on each side.

Built from the 2026-11-04 v2 prototype (tmp/stage-hazards/kraken-proto). Runs inside
`blender -b --factory-startup`.
"""
from __future__ import annotations

import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

from stagekit import blender as kit
import layout as LAYOUT
import palette as P

# ---------------------------------------------------------------- look knobs (local units; x S = metres)
S = 2.6                       # whole-kraken scale: head ~18 m tall, concept B's head-to-boat ratio
RX, RY, RZ = 3.8, 3.3, 3.5    # head radii
HZ = 3.0                      # head centre above the tentacle root
BONE_LEN = 0.8                # target bone length: chains get round(length / BONE_LEN) bones
ARM_R = 1.3                   # spare-arm base radius
HERO_R = 1.55                 # hero arm base radius
TIP_R = 0.26                  # tentacle tip radius
TAPER = 0.62                  # spare arms: radius falls as (1 - t) ** TAPER (thick for most of the length)
ARM_W, ARM_HT, ARM_HB = 1.45, 0.85, 0.6   # cross-section: half-width, top and (flatter) belly half-heights, x r
HERO_HT, HERO_HB = 0.36, 0.28  # the hero arm is flatter: it lies across the deck like a plank
HERO_TIP_M = 10.0             # metres of taper at the hero tip (the droop over the far rail)
HERO_TAPER = 1.3              # hero tip: radius falls as (1 - u) ** HERO_TAPER, so the curled warning tip reads as a tip
RING_R, RING_Z = 1.9, 0.45    # where tentacles leave the head
SEED = 7

FLAT_HALF_WIDTH = LAYOUT.SLAM_HALF_WIDTH       # metres: the hero arm across the deck matches the slam lane
HERO_R_FLAT = FLAT_HALF_WIDTH / S / ARM_W       # local radius that gives that half-width
HERO_SUCKER, HERO_SUCKER_H = 0.62, 0.26         # hero suckers: radius x r, stand height x sucker radius
SUCKER, SUCKER_H = 0.84, 0.42                   # spare-arm suckers

RNG = random.Random(SEED)


def jit(c, a=0.05):
    k = 1.0 + RNG.uniform(-a, a)
    return tuple(min(1.0, max(0.0, x * k)) for x in c[:3])


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


# ---------------------------------------------------------------- mesh soup with weights

class KMesh:
    """Vertices (local units), faces, one colour per face and a weight dict per vertex."""

    def __init__(self):
        self.v, self.f, self.c, self.w = [], [], [], []
        self.group = None  # default weights for added vertices

    def add(self, p, w=None):
        self.v.append(Vector(p))
        self.w.append(w if w is not None else self.group)
        return len(self.v) - 1

    def face(self, idx, col, away=None, toward=None):
        pts = [self.v[i] for i in idx]
        n = Vector((0, 0, 0))
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        ctr = sum(pts, Vector()) / len(pts)
        if away is not None and n.dot(ctr - Vector(away)) < 0:
            idx = list(reversed(idx))
        if toward is not None and n.dot(Vector(toward)) < 0:
            idx = list(reversed(idx))
        self.f.append(tuple(idx))
        self.c.append(col)


def ico(m, center, radii, subdiv, colfn, rot=None, jitter=0.0, keep=None):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    bm.verts.ensure_lookup_table()
    rot = rot or Matrix.Identity(3)
    idmap = {}
    for vt in bm.verts:
        u = vt.co.copy()
        if keep is not None and not keep(u):
            continue
        r = 1.0 + RNG.uniform(-jitter, jitter)
        p = Vector(center) + rot @ Vector((u.x * radii[0] * r, u.y * radii[1] * r, u.z * radii[2] * r))
        idmap[vt.index] = (m.add(p), u)
    for fc in bm.faces:
        ids = [vt.index for vt in fc.verts]
        if not all(i in idmap for i in ids):
            continue
        u = sum((idmap[i][1] for i in ids), Vector()) / len(ids)
        m.face([idmap[i][0] for i in ids], colfn(u), away=center)
    bm.free()


def tube(m, pts, radii, sides, colfn, caps=True, flat=1.0, up=Vector((0, 0, 1))):
    """Tube along pts (Vectors) with a radius per point; colfn(i, j) per face."""
    rings = []
    n = len(pts)
    prev_x = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        z = up - t * up.dot(t)
        if z.length < 1e-4:
            z = prev_x.cross(t) if prev_x is not None else Vector((1, 0, 0))
        z.normalize()
        x = t.cross(z)
        prev_x = x
        ring = []
        for j in range(sides):
            a = 2 * math.pi * (j + 0.5) / sides
            ring.append(m.add(p + x * math.cos(a) * radii[i] + z * math.sin(a) * radii[i] * flat))
        rings.append(ring)
    for i in range(n - 1):
        ctr = (pts[i] + pts[i + 1]) / 2
        for j in range(sides):
            k = (j + 1) % sides
            m.face([rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j]], colfn(i, j), away=ctr)
    if caps:
        for i, d in ((0, -1), (n - 1, 1)):
            tip = m.add(pts[i] + (pts[i] - pts[i - d]).normalized() * radii[i] * 0.6)
            for j in range(sides):
                k = (j + 1) % sides
                m.face([rings[i][j], rings[i][k], tip], colfn(i, j), away=pts[i])


def catmull(ctrl, per=24):
    """Catmull-Rom through Vectors, `per` samples per span, ending on the last point."""
    pts = [Vector(p) for p in ctrl]
    pts = [2 * pts[0] - pts[1]] + pts + [2 * pts[-1] - pts[-2]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(per):
            t = k / per
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-2].copy())
    return out


def arc_lengths(pts):
    s = [0.0]
    for i in range(1, len(pts)):
        s.append(s[-1] + (pts[i] - pts[i - 1]).length)
    return s


# ---------------------------------------------------------------- head

HC = Vector((0, 0, HZ))


def head_surface(x, z, out=0.0):
    """Point on the front (-Y) of the head ellipsoid at local x, z (relative to HC) and its normal."""
    q = 1 - (x / RX) ** 2 - (z / RZ) ** 2
    y = -RY * math.sqrt(max(q, 0.0))
    n = Vector((x / RX ** 2, y / RY ** 2, z / RZ ** 2)).normalized()
    return HC + Vector((x, y, z)) + n * out, n


def sag(p):
    """Gentle sag: wider jowls low, crown leaning back."""
    q = p - HC
    lo = max(0.0, -q.z / RZ)
    hi = max(0.0, q.z / RZ)
    return HC + Vector((q.x * (1 + 0.16 * lo), q.y * (1 + 0.06 * lo) + 0.35 * hi * hi, q.z * (1 - 0.05 * lo)))


def build_head(m):
    """Head, eyes, lids, brows, muzzle, frown and nightcap, all weighted to `Head`. Returns the eye centres."""
    m.group = {"Head": 1.0}
    start = len(m.v)

    def skin(u):
        t = 0.5 + 0.5 * u.z
        return jit(mix(P.KRAKEN_DARK, P.KRAKEN_LIGHT, 0.25 + 0.6 * t), 0.06)

    ico(m, HC, (RX, RY, RZ), 3, skin, jitter=0.025)

    eyes = []
    er = 1.08
    ez = -0.2
    gaze = Vector((0.36, -1.0, -0.5)).normalized()  # down and to its left, at the deck
    for side in (-1, 1):
        ex = side * 1.18
        sp, n = head_surface(ex, ez)
        c = sp - n * 0.18 * er
        eyes.append(sag(c.copy()))
        ico(m, c, (er, er, er), 2, lambda u: jit(P.KRAKEN_EYE, 0.03))
        pc = c + gaze * er * 0.9
        rot = gaze.to_track_quat("Z", "Y").to_matrix()
        ico(m, pc, (0.4 * er, 0.4 * er, 0.14 * er), 2, lambda u: P.KRAKEN_PUPIL, rot=rot)
        ico(m, pc + rot @ Vector((-0.12, 0.13, 0.11)) * er, (0.08, 0.08, 0.05), 1, lambda u: P.KRAKEN_GLINT, rot=rot)
        nl = Vector((-side * 0.2, -0.22, 1.0)).normalized()  # lid cut: lower in front and at the inner corner
        d = 0.1
        lr = 1.12 * er
        ico(m, c, (lr, lr, lr), 2, lambda u: jit(P.KRAKEN_LID, 0.04), keep=lambda u: u.dot(nl) > d - 0.08)
        # dark rim along the lid edge (front part of the cut circle)
        cc = c + nl * d * lr
        rr = lr * math.sqrt(1 - d * d)
        a1 = nl.cross(Vector((0, 0, 1))).normalized() if abs(nl.z) < 0.99 else Vector((1, 0, 0))
        a2 = nl.cross(a1).normalized()
        rim = []
        for k in range(15):
            a = math.pi * (0.2 + 0.6 * k / 14)
            p = cc + (a1 * math.cos(a) + a2 * math.sin(a)) * rr
            if p.y > c.y + 0.1:
                p = cc + (a1 * math.cos(a) - a2 * math.sin(a)) * rr
            rim.append(p)
        rim.sort(key=lambda p: p.x)
        tube(m, rim, [0.08 * er] * len(rim), 6, lambda i, j: P.KRAKEN_RIM)
        # under-eye bag
        bag = []
        for k in range(7):
            u = k / 6
            p, n2 = head_surface(side * (0.35 + 1.55 * u), ez - 1.0 * er - 0.18 * math.sin(math.pi * u), 0.05)
            bag.append(p)
        tube(m, bag, [0.2 * er * (0.6 + 0.4 * math.sin(math.pi * k / 6)) for k in range(7)], 6,
             lambda i, j: jit(P.KRAKEN, 0.04))
        # heavy brow: thick at the inner end, angled down towards the middle
        brow = []
        rads = []
        for k in range(8):
            u = k / 7
            x = side * (0.08 + 2.2 * u)
            z = ez + 0.92 * er + 0.7 * u - 0.15 * u * u
            p, n2 = head_surface(x, z, 0.22)
            brow.append(p)
            rads.append(0.5 * er * (1.0 - 0.45 * u))
        tube(m, brow, rads, 7, lambda i, j: jit(mix(P.KRAKEN, P.KRAKEN_DARK, 0.25), 0.05), flat=0.8)

    # Muzzle, jowls, frown.
    mz = -1.75
    mp, mn = head_surface(0, mz, -0.45)
    MR = (1.55, 0.95, 0.95)
    ico(m, mp, MR, 2, lambda u: jit(P.KRAKEN, 0.05), jitter=0.03)
    for side in (-1, 1):
        jp, jn = head_surface(side * 1.75, mz + 0.05, -0.35)
        ico(m, jp, (0.95, 0.85, 0.95), 2, lambda u: jit(mix(P.KRAKEN, P.KRAKEN_DARK, 0.15 * (1 - u.z)), 0.05), jitter=0.03)

    def muzzle(x, z, out):
        q = 1 - (x / MR[0]) ** 2 - ((z - mp.z) / MR[2]) ** 2
        return Vector((x, mp.y - MR[1] * math.sqrt(max(q, 0.02)) - out, z))

    lip, lipr, gap = [], [], []
    for k in range(11):
        u = k / 10 * 2 - 1
        x = u * 1.65
        z = mp.z + 0.3 - 0.85 * u * u
        lip.append(muzzle(x * 0.97, z, 0.05))
        lipr.append(0.32 * (1 - 0.45 * u * u))
        gap.append(muzzle(x * 0.9, z - 0.22 + 0.06 * u * u, -0.02))
    tube(m, gap, [0.1 * (1 - 0.4 * (k / 5 - 1) ** 2) for k in range(11)], 6, lambda i, j: P.KRAKEN_MOUTH)
    tube(m, lip, lipr, 7, lambda i, j: jit(P.KRAKEN_LIP, 0.04))

    # Nightcap: rolled brim, striped floppy cone, pompom.
    a0 = Vector((-0.32, 0.22, 1.0)).normalized()
    base = HC + a0 * (0.6 * RZ)
    brim_r = 0.82 * RX
    a1 = a0.cross(Vector((0, 1, 0))).normalized()
    a2 = a0.cross(a1).normalized()
    brim = [base + (a1 * math.cos(2 * math.pi * k / 18) + a2 * math.sin(2 * math.pi * k / 18)) * brim_r for k in range(19)]
    tube(m, brim, [0.45] * 19, 7, lambda i, j: jit(P.NIGHTCAP_CREAM, 0.04), caps=False)
    side = Vector((-1.0, 0.25, 0.0)).normalized()
    ctrl = [base - a0 * 0.2, base + a0 * 1.4, base + a0 * 2.6 + side * 0.9, base + a0 * 2.9 + side * 2.4,
            base + a0 * 2.2 + side * 3.6, base + a0 * 0.9 + side * 4.2]
    path = catmull(ctrl, 6)
    lens = arc_lengths(path)
    total = lens[-1]
    radii = [brim_r * 1.0 * (1 - s / total) ** 1.15 + 0.22 for s in lens]
    stripe = 0.85
    tube(m, path, radii, 12, lambda i, j: jit(P.NIGHTCAP_BLUE if int(lens[i] / stripe) % 2 == 0 else P.NIGHTCAP_CREAM, 0.04),
         caps=False)
    pom = path[-1] + (path[-1] - path[-2]).normalized() * 0.3
    ico(m, pom, (0.78, 0.78, 0.78), 1, lambda u: jit(P.NIGHTCAP_CREAM, 0.06), jitter=0.12)

    for i in range(start, len(m.v)):
        m.v[i] = sag(m.v[i])
    m.group = None
    return eyes, er


# ---------------------------------------------------------------- arms

class Arm:
    """One tentacle chain. Geometry in local units; the `_m` properties are metres."""

    def __init__(self, i, angle):
        self.i = i
        self.name = "Arm%d" % i
        self.angle = angle
        self.dir = Vector((math.sin(angle), -math.cos(angle), -0.18)).normalized()
        self.base = Vector((RING_R * math.sin(angle), -RING_R * math.cos(angle), RING_Z))
        self.length = 20.0
        self.hero = False

    @property
    def n(self):
        return max(8, round(self.length / BONE_LEN))

    @property
    def length_m(self):
        return self.length * S

    @property
    def base_m(self):
        return self.base * S

    def bone(self, k):
        return "%s_%02d" % (self.name, k)

    def r_at(self, s):
        """Local radius at arc length s (local units)."""
        if not self.hero:
            t = min(1.0, s / self.length)
            return TIP_R + (ARM_R - TIP_R) * (1 - t) ** TAPER
        s1 = 0.3 * self.length
        s2 = self.length - HERO_TIP_M / S
        if s < s1:
            return HERO_R_FLAT + (HERO_R - HERO_R_FLAT) * (1 - s / s1) ** 1.5
        if s < s2:
            return HERO_R_FLAT
        u = min(1.0, (s - s2) / (self.length - s2))
        return TIP_R + (HERO_R_FLAT - TIP_R) * (1 - u) ** HERO_TAPER

    @property
    def heights(self):
        return (HERO_HT, HERO_HB) if self.hero else (ARM_HT, ARM_HB)

    @property
    def suckers(self):
        return (HERO_SUCKER, HERO_SUCKER_H) if self.hero else (SUCKER, SUCKER_H)

    def underside_m(self, s_m):
        """Metres from the centreline to the lowest point of the sucker plates at arc length s_m."""
        r = self.r_at(s_m / S)
        ht, hb = self.heights
        so, sh = self.suckers
        return (r * hb * 0.8 + sh * so * r) * S

    def top_m(self, s_m):
        r = self.r_at(s_m / S)
        return r * self.heights[0] * S


def make_arms():
    return [Arm(i, math.radians(22.5 + 45 * i)) for i in range(8)]


def arm_weights(arm, s):
    l = arm.length / arm.n
    u = s / l - 0.5
    k0 = math.floor(u)
    f = u - k0
    w = {}
    for k, wt in ((k0, 1 - f), (k0 + 1, f)):
        k = max(0, min(arm.n - 1, k))
        w[arm.bone(k)] = w.get(arm.bone(k), 0.0) + wt
    return w


def build_arm(m, arm):
    """Broad flattened tentacle: domed purple back, flatter tan underside, one row of plate-sized suckers."""
    d = arm.dir
    z = Vector((0, 0, 1)) - d * d.z
    z.normalize()
    x = d.cross(z)
    ht, hb = arm.heights
    sides = 12
    s_list = [0.0]
    while s_list[-1] < arm.length - 0.05:
        s_list.append(min(arm.length, s_list[-1] + max(0.3, min(1.3, 0.8 * arm.r_at(s_list[-1])))))
    rings = []
    for i, s in enumerate(s_list):
        r = arm.r_at(s)
        c = arm.base + d * s
        w = arm_weights(arm, s)
        off = 0.5 * (i % 2)
        ring = []
        for j in range(sides):
            a = 2 * math.pi * (j + off) / sides - math.pi / 2
            jr = 1.0 + RNG.uniform(-0.05, 0.05)
            sa = math.sin(a)
            hz = ht if sa > 0 else hb * (abs(sa) ** 0.55) / max(abs(sa), 1e-6)  # flatter belly
            ring.append(m.add(c + x * math.cos(a) * r * ARM_W * jr + z * sa * r * hz * jr, w))
        rings.append((ring, c, off, r))

    def col(idx, c):
        p = sum((m.v[q] for q in idx), Vector()) / 3 - c
        px, pz = p.dot(x) / ARM_W, p.dot(z)
        pz /= ht if pz > 0 else hb
        sn = pz / max(1e-6, math.hypot(px, pz))
        if sn < -0.5:
            return jit(P.KRAKEN_UNDER, 0.04)
        if sn < -0.2:
            return jit(mix(P.KRAKEN_DARK, P.KRAKEN_UNDER, 0.55), 0.04)
        return jit(mix(P.KRAKEN, P.KRAKEN_LIGHT, 0.6 * sn), 0.06)
    for i in range(len(rings) - 1):
        (ra, ca, oa, _), (rb, cb, ob, _) = rings[i], rings[i + 1]
        ctr = (ca + cb) / 2
        for j in range(sides):
            k = (j + 1) % sides
            if oa == 0:
                tris = [[ra[j], ra[k], rb[j]], [rb[j], ra[k], rb[k]]]
            else:
                tris = [[rb[j], rb[k], ra[j]], [ra[j], rb[k], ra[k]]]
            for t in tris:
                m.face(t, col(t, ctr), away=ctr)
    tip_ring, tip_c, _, tr = rings[-1]
    tip = m.add(tip_c + d * tr * 1.2, arm_weights(arm, arm.length))
    for j in range(sides):
        m.face([tip_ring[j], tip_ring[(j + 1) % sides], tip], jit(P.KRAKEN, 0.05), away=tip_c)
    # suckers: one row of thick plate-sized discs down the middle of the flat underside
    so, sh = arm.suckers
    s = 0.9 * arm.r_at(0)
    while s < arm.length - 0.4:
        r = arm.r_at(s)
        ro = so * r
        if r < 0.3:
            break
        c = arm.base + d * s - z * (r * hb * 0.8)
        sucker(m, c, -z, x, ro, sh * ro, arm_weights(arm, s))
        s += 2 * ro + 0.08 * r


def sucker(m, c, axis, x, ro, h, w):
    K = 10
    y = axis.cross(x)
    rings = {}
    for name, off, rad in (("base", -0.5 * h, ro * 0.95), ("top", h, ro * 0.9), ("lip", h, ro * 0.5), ("floor", 0.45 * h, ro * 0.42)):
        rings[name] = [m.add(c + axis * off + (x * math.cos(2 * math.pi * j / K) + y * math.sin(2 * math.pi * j / K)) * rad, w)
                       for j in range(K)]
    for j in range(K):
        k = (j + 1) % K
        m.face([rings["base"][j], rings["base"][k], rings["top"][k], rings["top"][j]], jit(P.KRAKEN_SUCKER, 0.04), away=c)
        m.face([rings["top"][j], rings["top"][k], rings["lip"][k], rings["lip"][j]], jit(P.KRAKEN_SUCKER, 0.03), toward=axis)
        m.face([rings["lip"][j], rings["lip"][k], rings["floor"][k], rings["floor"][j]], jit(P.KRAKEN_SUCKER_IN, 0.03),
               away=c + axis * h * 3)
    m.face(rings["floor"], P.KRAKEN_SUCKER_IN, toward=axis)


# ---------------------------------------------------------------- measurements

def head_extents():
    """Metres, kraken frame: forward reach of the head (along -Y), its half-width, top of the cap,
    and the eye centres' height above the root. Built on a scratch mesh with the build's seed."""
    RNG.seed(SEED)
    m = KMesh()
    eyes, er = build_head(m)
    ys = [v.y for v in m.v]
    xs = [abs(v.x) for v in m.v]
    zs = [v.z for v in m.v]
    return {"front": -min(ys) * S, "back": max(ys) * S, "halfWidth": max(xs) * S, "top": max(zs) * S,
            "bottom": min(zs) * S, "eyeZ": sum(e.z for e in eyes) / len(eyes) * S, "eyeRadius": er * S,
            "headCentreZ": HZ * S}


# ---------------------------------------------------------------- Blender objects

def build_mesh(arms):
    """The skinned mesh object (metres) and the vertex index range of every arm."""
    RNG.seed(SEED)
    m = KMesh()
    build_head(m)
    ranges = {"Head": (0, len(m.v))}
    for arm in arms:
        a = len(m.v)
        build_arm(m, arm)
        ranges[arm.name] = (a, len(m.v))
    me = bpy.data.meshes.new("Kraken")
    me.from_pydata([tuple(v * S) for v in m.v], [], m.f)
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    flat = []
    for f, c in zip(m.f, m.c):
        for _ in f:
            flat.extend((c[0], c[1], c[2], 1.0))
    attr.data.foreach_set("color_srgb", flat)
    me.color_attributes.active_color = attr
    me.polygons.foreach_set("use_smooth", [False] * len(me.polygons))
    me.validate()
    me.update()
    mat = bpy.data.materials.new("Kraken")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    me.materials.append(mat)
    ob = bpy.data.objects.new("Kraken", me)
    bpy.context.scene.collection.objects.link(ob)
    groups = {}
    for i, w in enumerate(m.w):
        for g, val in (w or {"Head": 1.0}).items():
            groups.setdefault(g, {}).setdefault(round(val, 6), []).append(i)
    order = ["Root", "Head"] + [a.bone(k) for a in arms for k in range(a.n)]
    for g in order:
        vg = ob.vertex_groups.new(name=g)
        for val in sorted(groups.get(g, {})):
            vg.add(groups[g][val], val, "REPLACE")
    return ob, ranges


def build_armature(arms):
    """Root at the origin pointing up, Head above it, one connected chain per arm (z axis up at rest)."""
    ad = bpy.data.armatures.new("KrakenRig")
    rig = bpy.data.objects.new("KrakenRig", ad)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    root = ad.edit_bones.new("Root")
    root.head, root.tail = (0, 0, 0), (0, 0, 1.0 * S)
    head = ad.edit_bones.new("Head")
    head.head, head.tail = (0, 0, 1.0 * S), (0, 0, (HZ + RZ) * S)
    head.parent = root
    for arm in arms:
        l = arm.length_m / arm.n
        parent = root
        for k in range(arm.n):
            b = ad.edit_bones.new(arm.bone(k))
            b.head = arm.base_m + arm.dir * (k * l)
            b.tail = arm.base_m + arm.dir * ((k + 1) * l)
            b.align_roll(Vector((0, 0, 1)))
            b.parent = parent
            b.use_connect = k > 0
            parent = b
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


def build(arms, bake=True):
    """Mesh + rig + AO (baked in the rest pose like the stage generators). Returns (rig, mesh, info)."""
    ob, ranges = build_mesh(arms)
    rig = build_armature(arms)
    bake_s = None
    if bake:
        bake_s = kit.bake_ao({"Kraken": ob}, 48, ao_distance=2.5 * S, floor=0.42, gamma=1.0)
    ob.parent = rig
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    return rig, ob, {"ranges": ranges, "triangles": tris, "vertices": len(ob.data.vertices),
                     "bones": len(rig.data.bones), "aoBakeSeconds": bake_s}
