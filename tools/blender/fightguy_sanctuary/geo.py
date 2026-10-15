"""Small procedural mesh kit for the Sanctuary art generator.

Everything is authored in Unity world coordinates (+X east, +Y up, +Z north).
Faces use the right-hand rule in those coordinates: normal = (b - a) x (c - a).
`to_object` converts to Blender space so the project's FBX export settings land
each vertex back on the same Unity coordinate (verified: Blender (x, y, z)
imports as Unity (-x, z, -y)).
"""
from __future__ import annotations

import math
import random

import bpy

WHITE_UV = (0.985, 0.985)  # inside the atlas' solid white corner block


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


class detail:
    """Context manager: faces added inside use this triplanar-detail mask."""

    def __init__(self, mesh, amount):
        self.mesh, self.amount = mesh, amount

    def __enter__(self):
        self.previous = self.mesh.detail
        self.mesh.detail = self.amount

    def __exit__(self, *exc):
        self.mesh.detail = self.previous


class Mesh:
    """Polygon soup with per-corner color/uv and per-face material slot."""

    def __init__(self, name, materials):
        self.name = name
        self.materials = list(materials)
        self.verts = []
        self.faces = []
        self.colors = []  # per face: list of rgba per corner
        self.uvs = []     # per face: list of uv per corner
        self.mats = []
        self.detail = 1.0  # default vertex alpha: triplanar detail mask (SA_Stone) / wind weight (cloth)

    def mat(self, name):
        if name not in self.materials:
            self.materials.append(name)
        return self.materials.index(name)

    def add(self, p):
        self.verts.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.verts) - 1

    def face(self, idx, color, mat="SA_Stone", uvs=None):
        n = len(idx)
        if n < 3:
            return
        cols = color if isinstance(color, list) else [rgba(color, self.detail) if len(color) == 3 else color] * n
        self.faces.append(tuple(idx))
        self.colors.append([rgba(c, self.detail) if len(c) == 3 else c for c in cols])
        self.uvs.append(uvs if uvs is not None else [WHITE_UV] * n)
        self.mats.append(self.mat(mat))

    def poly(self, pts, color, mat="SA_Stone", uvs=None):
        self.face([self.add(p) for p in pts], color, mat, uvs)

    def extend(self, other):
        base = len(self.verts)
        self.verts.extend(other.verts)
        remap = [self.mat(name) for name in other.materials]
        for f, c, u, m in zip(other.faces, other.colors, other.uvs, other.mats):
            self.faces.append(tuple(i + base for i in f))
            self.colors.append(c)
            self.uvs.append(u)
            self.mats.append(remap[m])

    @property
    def triangle_count(self):
        return sum(len(f) - 2 for f in self.faces)


# ---------------------------------------------------------------- primitives

def quad_grid(m, origin, du, dv, nu, nv, color_fn, mat="SA_Stone"):
    """Planar grid of nu x nv quads starting at origin, spanning du/dv vectors."""
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


def box(m, center, size, color, mat="SA_Stone", skip=(), side_color=None, bottom_color=None, yaw=0.0, top_color=None):
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


def vertex_grid(m, origin, du, dv, nu, nv, color_at, mat="SA_Stone"):
    """Grid whose colours are sampled per vertex (smooth blends across faces).
    Normal = du x dv."""
    rows, cols = [], []
    for j in range(nv + 1):
        row, crow = [], []
        for i in range(nu + 1):
            p = [origin[k] + du[k] * i / nu + dv[k] * j / nv for k in range(3)]
            row.append(m.add(p))
            c = color_at(p)
            crow.append(rgba(c, m.detail) if len(c) == 3 else c)
        rows.append(row)
        cols.append(crow)
    for j in range(nv):
        for i in range(nu):
            q = [(j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i)]
            m.face([rows[a][b] for a, b in q], [cols[a][b] for a, b in q], mat)


def top_grid(m, x0, z0, x1, z1, y, nx, nz, color_fn, mat="SA_Stone"):
    """Upward-facing grid over [x0,x1] x [z0,z1] at height y."""
    quad_grid(m, (x0, y, z0), (0, 0, z1 - z0), (x1 - x0, 0, 0), nz, nx, lambda j, i: color_fn(i, j), mat)


def wall_grid(m, a, b, y0, y1, nu, nv, color_fn, mat="SA_Stone"):
    """Vertical grid from a to b (x, z) and y0..y1; faces outward to the right of a->b
    when seen from above with +x east and +z north (i.e. CCW outline order)."""
    quad_grid(m, (a[0], y0, a[1]), (0, y1 - y0, 0), (b[0] - a[0], 0, b[1] - a[1]), nv, nu, lambda j, i: color_fn(i, j), mat)


def prism(m, outline, y0, y1, color, mat="SA_Stone", top=True, bottom=False, top_color=None, side_colors=None):
    """Vertical prism from a CCW (seen from above, x east/z north) outline."""
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


def lathe(m, base, profile, segments, colors, mat="SA_Stone", yaw=0.0, cap_top=True, cap_bottom=False, mats=None):
    """Surface of revolution around +Y at base. profile: [(radius, height)] bottom->top.
    colors: one color per profile band (len(profile)-1) or a single color."""
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


def cylinder_between(m, a, b, radius, segments, color, mat="SA_Stone", radius_b=None, caps=True):
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
    for k in range(segments):
        k2 = (k + 1) % segments
        m.face([ra_idx[k], ra_idx[k2], rb_idx[k2], rb_idx[k]], color, mat)
    if caps:
        m.face(list(reversed(rb_idx)), color, mat)
        m.face(ra_idx, color, mat)
    # Orientation of the side quads depends on (u, w, d) handedness; fix by test.
    _orient_outward(m, len(m.faces) - (segments + (2 if caps else 0)), a, b)


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


def ellipsoid(m, center, radii, color, mat="SA_Stone", rings=6, segments=10, color_fn=None, rng=None):
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


def oriented(m, pts, desired, color, mat="SA_Stone", uvs=None):
    """Add a polygon, reversing it if needed so its normal faces `desired`."""
    n = _normal(pts)
    if n[0] * desired[0] + n[1] * desired[1] + n[2] * desired[2] < 0:
        pts = list(reversed(pts))
        if isinstance(color, list):
            color = list(reversed(color))
        if uvs is not None:
            uvs = list(reversed(uvs))
    m.poly(pts, color, mat, uvs)


def catenary(p0, p1, sag, n):
    pts = []
    for i in range(n + 1):
        t = i / n
        p = [p0[k] + (p1[k] - p0[k]) * t for k in range(3)]
        p[1] -= sag * 4 * t * (1 - t)
        pts.append(tuple(p))
    return pts


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


# ---------------------------------------------------------------- vectors

def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(a):
    length = math.sqrt(sum(x * x for x in a)) or 1e-9
    return tuple(x / length for x in a)


def _normal(pts):
    a, b, c = pts[0], pts[1], pts[2]
    return _norm(_cross([b[i] - a[i] for i in range(3)], [c[i] - a[i] for i in range(3)]))


normal = _normal


# ---------------------------------------------------------------- Blender

def to_object(m: Mesh, collection, smooth=False):
    """Create a Blender object; vertices map Unity (x, y, z) -> Blender (-x, -z, y)
    and faces are reversed so normals stay outward after the reflection."""
    me = bpy.data.meshes.new(m.name)
    verts = [(-x, -z, y) for x, y, z in m.verts]
    faces = [tuple(reversed(f)) for f in m.faces]
    me.from_pydata(verts, [], faces)
    col_flat, uv_flat = [], []
    for c, u in zip(m.colors, m.uvs):
        for cc in reversed(c):
            col_flat.extend(cc)
        for uu in reversed(u):
            uv_flat.extend(uu)
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    if len(col_flat) == len(attr.data) * 4:
        attr.data.foreach_set("color_srgb", col_flat)
    else:
        raise RuntimeError("%s: corner color count mismatch after validation" % m.name)
    uv = me.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", uv_flat)
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
    x, y, z = ob_vertex_co
    return (-x, z, -y)
