"""Procedural mesh kit shared by the stage generators.

Everything is authored in Unity world coordinates (+X east, +Y up, +Z north).
Faces use the right-hand rule in those coordinates: normal = (b - a) x (c - a).
`to_object` converts to Blender space so the project's FBX export settings land
each vertex back on the same Unity coordinate (verified: Blender (x, y, z)
imports as Unity (-x, z, -y)).

Vertex alpha is a mask whose meaning the material's shader decides:
SLOPArena/StageVertexLit reads it as the triplanar-detail amount (`detail`) or, on
cloth, the wind weight; SLOPArena/StageNeon reads it as the glow amount. A face
without an explicit material uses its mesh's first material slot.

Optional per-corner animation channels (exported as extra UV maps with
`to_object(..., channels=True)`, imported by Unity as TEXCOORD1..3 and read only
by StageNeon):
  Anim      (rate, phase)   rate 0 steady, > 0 blink in Hz, < 0 flicker; phase 0..1
  Travel    (speed, along)  metres per second along TravelDir; along = rest position
  TravelDir (dx, dz)        unit direction in the horizontal plane
  Bob: Travel (height, Hz * 10 + phase) with TravelDir (0, 0) hops vertically (crowds)
The StageNeon shader documents the full rate encoding (pulse, chase, window bands).
"""
from __future__ import annotations

import math
from contextlib import contextmanager

WHITE_UV = (0.985, 0.985)  # inside the atlas' solid white corner block
STEADY = (0.0, 0.0)
STILL = (0.0, 0.0)
NO_DIR = (0.0, 0.0)


def mix(a, b, t):
    n = min(len(a), len(b))
    out = tuple(a[i] + (b[i] - a[i]) * t for i in range(n))
    return out + tuple(a[n:]) if len(a) > n else out


def jitter(color, rng, amount=0.04, alpha=None):
    k = 1.0 + rng.uniform(-amount, amount)
    c = tuple(min(1.0, max(0.0, v * k)) for v in color[:3])
    if alpha is None and len(color) > 3:
        alpha = color[3]
    return c if alpha is None else c + (alpha,)


def rgba(color, alpha=1.0):
    return tuple(color[:3]) + (alpha,)


def scale(color, k):
    return tuple(min(1.0, max(0.0, c * k)) for c in color[:3])


class Mesh:
    """Polygon soup with per-corner color/uv/animation and per-face material slot."""

    def __init__(self, name, materials):
        self.name = name
        self.materials = list(materials)
        self.verts = []
        self.faces = []
        self.colors = []  # per face: list of rgba per corner
        self.uvs = []     # per face: list of uv per corner
        self.mats = []
        self.anims = []   # per face: (rate, phase)
        self.travels = []  # per face: (speed, along)
        self.dirs = []    # per face: (dx, dz)
        self.alpha = 1.0  # default vertex alpha for rgb colours (see the module docstring)
        self.anim = STEADY
        self.travel = STILL
        self.direction = NO_DIR

    def mat(self, name):
        if name not in self.materials:
            self.materials.append(name)
        return self.materials.index(name)

    def add(self, p):
        self.verts.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.verts) - 1

    def face(self, idx, color, mat=None, uvs=None):
        n = len(idx)
        if n < 3:
            return
        cols = color if isinstance(color, list) else [color] * n
        self.faces.append(tuple(idx))
        self.colors.append([rgba(c, self.alpha) if len(c) == 3 else tuple(c) for c in cols])
        self.uvs.append(list(uvs) if uvs is not None else [WHITE_UV] * n)
        self.mats.append(self.mat(self.materials[0] if mat is None else mat))
        self.anims.append(self.anim)
        self.travels.append(self.travel)
        self.dirs.append(self.direction)

    def poly(self, pts, color, mat=None, uvs=None):
        self.face([self.add(p) for p in pts], color, mat, uvs)

    def extend(self, other):
        base = len(self.verts)
        self.verts.extend(other.verts)
        remap = [self.mat(name) for name in other.materials]
        for f, c, u, m, a, t, d in zip(other.faces, other.colors, other.uvs, other.mats, other.anims, other.travels, other.dirs):
            self.faces.append(tuple(i + base for i in f))
            self.colors.append(c)
            self.uvs.append(u)
            self.mats.append(remap[m])
            self.anims.append(a)
            self.travels.append(t)
            self.dirs.append(d)

    @property
    def triangle_count(self):
        return sum(len(f) - 2 for f in self.faces)

    def bounds(self):
        if not self.verts:
            return None
        xs, ys, zs = zip(*self.verts)
        return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


@contextmanager
def state(m, alpha=None, anim=None, travel=None, direction=None):
    """Faces added inside use these vertex-alpha/animation channels."""
    saved = (m.alpha, m.anim, m.travel, m.direction)
    if alpha is not None:
        m.alpha = alpha
    if anim is not None:
        m.anim = anim
    if travel is not None:
        m.travel = travel
    if direction is not None:
        m.direction = direction
    try:
        yield m
    finally:
        m.alpha, m.anim, m.travel, m.direction = saved


def detail(m, amount):
    """Faces added inside use this StageVertexLit triplanar-detail mask."""
    return state(m, alpha=amount)


# ---------------------------------------------------------------- primitives

def quad_grid(m, origin, du, dv, nu, nv, color_fn, mat=None):
    """Planar grid of nu x nv quads starting at origin, spanning du/dv vectors. Normal = du x dv."""
    rows = []
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            p = [origin[k] + du[k] * i / nu + dv[k] * j / nv for k in range(3)]
            row.append(m.add(p))
        rows.append(row)
    for j in range(nv):
        for i in range(nu):
            m.face([rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]], color_fn(i, j), mat)


def vertex_grid(m, origin, du, dv, nu, nv, color_at, mat=None):
    """Grid whose colours are sampled per vertex (smooth blends across faces).
    Normal = du x dv."""
    rows, cols = [], []
    for j in range(nv + 1):
        row, crow = [], []
        for i in range(nu + 1):
            p = [origin[k] + du[k] * i / nu + dv[k] * j / nv for k in range(3)]
            row.append(m.add(p))
            c = color_at(p)
            crow.append(rgba(c, m.alpha) if len(c) == 3 else c)
        rows.append(row)
        cols.append(crow)
    for j in range(nv):
        for i in range(nu):
            q = [(j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i)]
            m.face([rows[a][b] for a, b in q], [cols[a][b] for a, b in q], mat)


def top_grid(m, x0, z0, x1, z1, y, nx, nz, color_fn, mat=None):
    """Upward-facing grid over [x0,x1] x [z0,z1] at height y."""
    quad_grid(m, (x0, y, z0), (0, 0, z1 - z0), (x1 - x0, 0, 0), nz, nx, lambda j, i: color_fn(i, j), mat)


def wall_grid(m, a, b, y0, y1, nu, nv, color_fn, mat=None):
    """Vertical grid from a to b (x, z) and y0..y1; faces outward to the right of a->b
    when seen from above with +x east and +z north (i.e. CCW outline order)."""
    quad_grid(m, (a[0], y0, a[1]), (0, y1 - y0, 0), (b[0] - a[0], 0, b[1] - a[1]), nv, nu, lambda j, i: color_fn(i, j), mat)


def box(m, center, size, color, mat=None, skip=(), side_color=None, bottom_color=None, yaw=0.0, top_color=None):
    """Axis-aligned (optionally yawed) box. Colors: top/side/bottom overrides."""
    cx, cy, cz = center
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    c, s = math.cos(yaw), math.sin(yaw)

    def P(x, y, z):
        return (cx + x * c + z * s, cy + y, cz - x * s + z * c)

    v = [P(-hx, -hy, -hz), P(hx, -hy, -hz), P(hx, -hy, hz), P(-hx, -hy, hz),
         P(-hx, hy, -hz), P(hx, hy, -hz), P(hx, hy, hz), P(-hx, hy, hz)]
    sc = side_color or color
    faces = {
        "top": ([4, 7, 6, 5], top_color or color),
        "bottom": ([0, 1, 2, 3], bottom_color or sc),
        "s": ([0, 4, 5, 1], sc),   # -Z
        "n": ([2, 6, 7, 3], sc),   # +Z
        "w": ([3, 7, 4, 0], sc),   # -X
        "e": ([1, 5, 6, 2], sc),   # +X
    }
    for key, (idx, col) in faces.items():
        if key in skip:
            continue
        m.poly([v[i] for i in idx], col, mat)


def box_minmax(m, lo, hi, color, mat=None, **kw):
    box(m, ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2),
        (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]), color, mat, **kw)


def prism(m, outline, y0, y1, color, mat=None, top=True, bottom=False, top_color=None, side_colors=None):
    """Vertical prism from a CCW outline (map view: +x east/right, +z north/up)."""
    n = len(outline)
    lo = [(p[0], y0, p[1]) for p in outline]
    hi = [(p[0], y1, p[1]) for p in outline]
    for i in range(n):
        j = (i + 1) % n
        col = side_colors[i] if side_colors else color
        m.poly([lo[i], hi[i], hi[j], lo[j]], col, mat)
    if top:
        m.poly(list(reversed(hi)), top_color or color, mat)
    if bottom:
        m.poly(lo, color, mat)


def lathe(m, base, profile, segments, colors, mat=None, yaw=0.0, cap_top=True, cap_bottom=False, mats=None):
    """Surface of revolution around +Y at base. profile: [(radius, height)] bottom->top.
    colors (and optional mats): one per profile band (len(profile)-1) or a single value."""
    rings = []
    for r, h in profile:
        ring = []
        for k in range(segments):
            a = yaw + 2 * math.pi * k / segments
            ring.append(m.add((base[0] + r * math.cos(a), base[1] + h, base[2] + r * math.sin(a))))
        rings.append(ring)
    for b in range(len(profile) - 1):
        col = colors[b] if isinstance(colors, list) else colors
        mt = mats[b] if mats else mat
        for k in range(segments):
            k2 = (k + 1) % segments
            # CCW from outside: ring b (k -> k2) then ring b+1 (k2 -> k)
            m.face([rings[b][k], rings[b + 1][k], rings[b + 1][k2], rings[b][k2]], col, mt)
    if cap_top and profile[-1][0] > 1e-4:
        col = colors[-1] if isinstance(colors, list) else colors
        m.face(list(reversed(rings[-1])), col, mats[-1] if mats else mat)
    if cap_bottom and profile[0][0] > 1e-4:
        col = colors[0] if isinstance(colors, list) else colors
        m.face(rings[0], col, mats[0] if mats else mat)


def cylinder_between(m, a, b, radius, segments, color, mat=None, radius_b=None, caps=True):
    ax = [b[i] - a[i] for i in range(3)]
    length = math.sqrt(sum(x * x for x in ax)) or 1e-6
    d = [x / length for x in ax]
    ref = (0, 1, 0) if abs(d[1]) < 0.9 else (1, 0, 0)
    u = _norm(_cross(d, ref))
    w = _cross(d, u)
    rb = radius if radius_b is None else radius_b
    ra_idx, rb_idx = [], []
    for k in range(segments):
        t = 2 * math.pi * k / segments
        off = [u[i] * math.cos(t) + w[i] * math.sin(t) for i in range(3)]
        ra_idx.append(m.add([a[i] + off[i] * radius for i in range(3)]))
        rb_idx.append(m.add([b[i] + off[i] * rb for i in range(3)]))
    first = len(m.faces)
    for k in range(segments):
        k2 = (k + 1) % segments
        m.face([ra_idx[k], ra_idx[k2], rb_idx[k2], rb_idx[k]], color, mat)
    if caps:
        m.face(list(reversed(rb_idx)), color, mat)
        m.face(ra_idx, color, mat)
    # Orientation of the side quads depends on (u, w, d) handedness; fix by test.
    _orient_outward(m, first, a, b)


def tube(m, points, radius, segments, color, mat=None):
    """Polyline tube (neon glass): capped segments overlap at the joints."""
    for i in range(len(points) - 1):
        if math.dist(points[i], points[i + 1]) > 1e-5:
            cylinder_between(m, points[i], points[i + 1], radius, segments, color, mat, caps=True)


def _orient_outward(m, first_face, a, b):
    axis_mid = [(a[i] + b[i]) / 2 for i in range(3)]
    for fi in range(first_face, len(m.faces)):
        f = m.faces[fi]
        pts = [m.verts[i] for i in f]
        n = _normal(pts)
        c = [sum(p[i] for p in pts) / len(pts) for i in range(3)]
        out = [c[i] - axis_mid[i] for i in range(3)]
        if sum(n[i] * out[i] for i in range(3)) < 0:
            m.faces[fi] = tuple(reversed(f))
            m.colors[fi] = list(reversed(m.colors[fi]))
            m.uvs[fi] = list(reversed(m.uvs[fi]))


def ellipsoid(m, center, radii, color, mat=None, rings=6, segments=10, color_fn=None):
    verts = []
    for r in range(rings + 1):
        phi = math.pi * r / rings - math.pi / 2
        row = []
        for s in range(segments):
            th = 2 * math.pi * s / segments
            p = (center[0] + radii[0] * math.cos(phi) * math.cos(th),
                 center[1] + radii[1] * math.sin(phi),
                 center[2] + radii[2] * math.cos(phi) * math.sin(th))
            row.append(m.add(p))
        verts.append(row)
    for r in range(rings):
        for s in range(segments):
            s2 = (s + 1) % segments
            col = color_fn(r, s) if color_fn else color
            m.face([verts[r][s], verts[r + 1][s], verts[r + 1][s2], verts[r][s2]], col, mat)


def oriented(m, pts, desired, color, mat=None, uvs=None):
    """Add a polygon, reversing it if needed so its normal faces `desired`."""
    n = _normal(pts)
    if n[0] * desired[0] + n[1] * desired[1] + n[2] * desired[2] < 0:
        pts = list(reversed(pts))
        if isinstance(color, list):
            color = list(reversed(color))
        if uvs is not None:
            uvs = list(reversed(uvs))
    m.poly(pts, color, mat, uvs)


def facade_quad(m, a, b, y0, y1, normal, color, mat=None, off=0.0, uvs=None):
    """Vertical rectangle from map points a->b, facing `normal` (nx, nz), pushed `off` outward."""
    nx, nz = normal
    pts = [(a[0] + nx * off, y0, a[1] + nz * off), (b[0] + nx * off, y0, b[1] + nz * off),
           (b[0] + nx * off, y1, b[1] + nz * off), (a[0] + nx * off, y1, a[1] + nz * off)]
    quv = None
    if uvs is not None:
        (u0, v0), (u1, v1) = uvs
        quv = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    oriented(m, pts, (nx, 0.0, nz), color, mat, quv)


def catenary(p0, p1, sag, n):
    pts = []
    for i in range(n + 1):
        t = i / n
        p = [p0[k] + (p1[k] - p0[k]) * t for k in range(3)]
        p[1] -= sag * 4 * t * (1 - t)
        pts.append(tuple(p))
    return pts


# ---------------------------------------------------------------- 2D outlines (map view, CCW)

def rounded_rect(x0, z0, x1, z1, radius, per_corner=4):
    """CCW outline (seen from above: +x east, +z north)."""
    r = min(radius, (x1 - x0) / 2 - 1e-3, (z1 - z0) / 2 - 1e-3)
    pts = []
    corners = [(x1 - r, z0 + r, -90), (x1 - r, z1 - r, 0), (x0 + r, z1 - r, 90), (x0 + r, z0 + r, 180)]
    for cx, cz, start in corners:
        for k in range(per_corner + 1):
            a = math.radians(start + 90 * k / per_corner)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def polygon_area(outline):
    a = 0.0
    for i in range(len(outline)):
        x0, z0 = outline[i]
        x1, z1 = outline[(i + 1) % len(outline)]
        a += x0 * z1 - x1 * z0
    return a / 2


def inset_polygon(outline, d):
    """Inset a convex CCW polygon by d (negative d grows it)."""
    n = len(outline)
    lines = []
    for i in range(n):
        (ax, az), (bx, bz) = outline[i], outline[(i + 1) % n]
        ex, ez = bx - ax, bz - az
        L = math.hypot(ex, ez)
        nx, nz = ez / L, -ex / L  # outward normal of a CCW edge
        lines.append(((ax - nx * d, az - nz * d), (ex, ez)))
    out = []
    for i in range(n):
        (p, e), (q, f) = lines[i - 1], lines[i]
        den = e[0] * f[1] - e[1] * f[0]
        t = ((q[0] - p[0]) * f[1] - (q[1] - p[1]) * f[0]) / den
        out.append((p[0] + e[0] * t, p[1] + e[1] * t))
    return out


def edges(outline):
    """(a, b, outward normal, length) for each edge of a CCW outline."""
    n = len(outline)
    out = []
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        ex, ez = b[0] - a[0], b[1] - a[1]
        L = math.hypot(ex, ez)
        out.append((a, b, (ez / L, -ex / L), L))
    return out


def inside_convex(outline, x, z, tol=0.0):
    for a, b, (nx, nz), L in edges(outline):
        if (x - a[0]) * nx + (z - a[1]) * nz > tol:
            return False
    return True


def ring_quads(m, outer, inner, y, color, mat=None, normal=(0, 1, 0)):
    """Flat band between two matching convex outlines."""
    n = len(outer)
    for i in range(n):
        j = (i + 1) % n
        oriented(m, [(outer[i][0], y, outer[i][1]), (outer[j][0], y, outer[j][1]),
                     (inner[j][0], y, inner[j][1]), (inner[i][0], y, inner[i][1])], normal,
                 color if not callable(color) else color(i), mat)


# ---------------------------------------------------------------- vectors

def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(a):
    length = math.sqrt(sum(x * x for x in a)) or 1e-9
    return tuple(x / length for x in a)


def _normal(pts):
    # First-corner normal (the right-hand rule above). Generators branch on it, for
    # example Sanctuary rock colouring on jittered quads, so a different formula
    # changes their art: compare geometryDigest before changing it.
    a, b, c = pts[0], pts[1], pts[2]
    return _norm(_cross([b[i] - a[i] for i in range(3)], [c[i] - a[i] for i in range(3)]))


normal = _normal


# ---------------------------------------------------------------- Blender

def to_object(m: Mesh, collection, smooth=False, channels=False):
    """Create a Blender object; vertices map Unity (x, y, z) -> Blender (-x, -z, y)
    and faces are reversed so normals stay outward after the reflection.
    `channels` also writes the Anim/Travel/TravelDir UV maps for StageNeon."""
    import bpy

    me = bpy.data.meshes.new(m.name)
    verts = [(-x, -z, y) for x, y, z in m.verts]
    faces = [tuple(reversed(f)) for f in m.faces]
    me.from_pydata(verts, [], faces)
    col_flat, uv_flat = [], []
    anim_flat, travel_flat, dir_flat = [], [], []
    for c, u, a, t, d, f in zip(m.colors, m.uvs, m.anims, m.travels, m.dirs, m.faces):
        for cc in reversed(c):
            col_flat.extend(cc)
        for uu in reversed(u):
            uv_flat.extend(uu)
        n = len(f)
        anim_flat.extend(a * n)
        travel_flat.extend(t * n)
        dir_flat.extend(d * n)
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    if len(col_flat) != len(attr.data) * 4:
        raise RuntimeError("%s: corner color count mismatch after validation" % m.name)
    attr.data.foreach_set("color_srgb", col_flat)
    layers = [("UVMap", uv_flat)]
    if channels:
        layers += [("Anim", anim_flat), ("Travel", travel_flat), ("TravelDir", dir_flat)]
    for name, flat in layers:
        layer = me.uv_layers.new(name=name)
        layer.data.foreach_set("uv", flat)
    me.uv_layers.active_index = 0
    me.polygons.foreach_set("material_index", m.mats)
    me.polygons.foreach_set("use_smooth", [smooth] * len(me.polygons))
    if me.validate(clean_customdata=False):
        raise RuntimeError("%s: invalid geometry was generated" % m.name)
    for name in m.materials:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        me.materials.append(mat)
    me.update()
    ob = bpy.data.objects.new(m.name, me)
    collection.objects.link(ob)
    return ob


def unity_point(ob_vertex_co):
    """Inverse of the to_object mapping: a Blender vertex back in Unity space."""
    x, y, z = ob_vertex_co
    return (-x, z, -y)
