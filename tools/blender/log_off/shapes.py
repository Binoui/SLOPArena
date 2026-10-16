"""Organic mesh helpers for Log Off, in Unity coordinates (see stagekit.geo).

Faces follow the kit's right-hand rule: (b - a) x (c - a) points outward.
Colour callbacks return RGB (the mesh's current vertex alpha is appended) or RGBA.
"""
from __future__ import annotations

import math

from stagekit import geo


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def mul(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def yaw_point(p, yaw, origin=(0.0, 0.0, 0.0)):
    """Rotate a local point about +Y (yaw radians, 0 = local +Z stays +Z) and translate."""
    c, s = math.cos(yaw), math.sin(yaw)
    return (origin[0] + p[0] * c + p[2] * s, origin[1] + p[1], origin[2] - p[0] * s + p[2] * c)


def bezier(p0, p1, p2, t):
    a = lerp3(p0, p1, t)
    b = lerp3(p1, p2, t)
    return lerp3(a, b, t)


def frames(points):
    """Parallel-transport frames (tangent, u, w) along a polyline; w = tangent x u."""
    n = len(points)
    tangents = [geo._norm(sub(points[min(n - 1, i + 1)], points[max(0, i - 1)])) for i in range(n)]
    t0 = tangents[0]
    ref = (0.0, 1.0, 0.0) if abs(t0[1]) < 0.9 else (1.0, 0.0, 0.0)
    u = geo._norm(geo._cross(t0, ref))
    out = []
    for t in tangents:
        u = geo._norm(sub(u, mul(t, dot(u, t))))
        out.append((t, u, geo._cross(t, u)))
    return out


def tube(m, points, radii, segments, color_fn, mat=None, cap_start=False, cap_end=True, squash=1.0):
    """Tube through `points` with per-point radii (or one radius). color_fn(i, k, normal).
    `squash` scales the frame's w axis (an oval cross-section)."""
    fr = frames(points)
    rings, norms = [], []
    for i, (p, (t, u, w)) in enumerate(zip(points, fr)):
        r = radii[i] if isinstance(radii, (list, tuple)) else radii
        ring, nr = [], []
        for k in range(segments):
            a = 2 * math.pi * k / segments
            n = tuple(u[j] * math.cos(a) + w[j] * math.sin(a) for j in range(3))
            off = tuple(u[j] * math.cos(a) * r + w[j] * math.sin(a) * r * squash for j in range(3))
            ring.append(m.add(add(p, off)))
            nr.append(n)
        rings.append(ring)
        norms.append(nr)
    for i in range(len(points) - 1):
        for k in range(segments):
            k2 = (k + 1) % segments
            m.face([rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]],
                   [color_fn(i, k, norms[i][k]), color_fn(i, k2, norms[i][k2]),
                    color_fn(i + 1, k2, norms[i + 1][k2]), color_fn(i + 1, k, norms[i + 1][k])], mat)
    if cap_end:
        last = len(points) - 1
        m.face(list(rings[last]), [color_fn(last, k, fr[last][0]) for k in range(segments)], mat)
    if cap_start:
        m.face(list(reversed(rings[0])), [color_fn(0, k, mul(fr[0][0], -1)) for k in reversed(range(segments))], mat)


def lathe(m, base, profile, segments, color_fn, mat=None, yaw=0.0, cap_top=True, cap_bottom=False,
          wobble=None):
    """Surface of revolution around +Y. profile [(radius, height)] bottom -> top;
    color_fn(ring_index, k) per vertex; wobble(ring_index, k) scales a vertex's radius."""
    rings, cols = [], []
    for b, (r, h) in enumerate(profile):
        ring, col = [], []
        for k in range(segments):
            a = yaw + 2 * math.pi * k / segments
            rr = r * (wobble(b, k) if wobble else 1.0)
            ring.append(m.add((base[0] + rr * math.cos(a), base[1] + h, base[2] + rr * math.sin(a))))
            col.append(color_fn(b, k))
        rings.append(ring)
        cols.append(col)
    for b in range(len(profile) - 1):
        for k in range(segments):
            k2 = (k + 1) % segments
            m.face([rings[b][k], rings[b + 1][k], rings[b + 1][k2], rings[b][k2]],
                   [cols[b][k], cols[b + 1][k], cols[b + 1][k2], cols[b][k2]], mat)
    if cap_top and profile[-1][0] > 1e-4:
        m.face(list(reversed(rings[-1])), list(reversed(cols[-1])), mat)
    if cap_bottom and profile[0][0] > 1e-4:
        m.face(list(rings[0]), list(cols[0]), mat)


def blob(m, center, radii, color_fn, rng=None, rings=6, segments=10, bump=0.0, floor=None, mat=None):
    """Bumpy ellipsoid. color_fn(normal, position). `floor` clamps vertices from below."""
    verts = []
    for r in range(rings + 1):
        phi = math.pi * r / rings - math.pi / 2
        row = []
        for s in range(segments):
            th = 2 * math.pi * s / segments
            n = (math.cos(phi) * math.cos(th), math.sin(phi), math.cos(phi) * math.sin(th))
            k = 1.0 + (bump * (rng.random() * 2 - 1) if rng is not None and 0 < r < rings else 0.0)
            p = (center[0] + radii[0] * n[0] * k, center[1] + radii[1] * n[1] * k, center[2] + radii[2] * n[2] * k)
            if floor is not None and p[1] < floor:
                p = (p[0], floor, p[2])
            row.append((m.add(p), color_fn(n, p)))
        verts.append(row)
    for r in range(rings):
        for s in range(segments):
            s2 = (s + 1) % segments
            q = [verts[r][s], verts[r + 1][s], verts[r + 1][s2], verts[r][s2]]
            m.face([v for v, _ in q], [c for _, c in q], mat)


def fan(m, center, ring, color_center, color_ring, desired=(0.0, 1.0, 0.0), mat=None, closed=True):
    """Triangle fan from a centre point to a ring of points (closed or an open arc), facing `desired`."""
    n = len(ring)
    for i in range(n if closed else n - 1):
        a, b = ring[i], ring[(i + 1) % n]
        cr = color_ring if not callable(color_ring) else None
        geo.oriented(m, [center, a, b], desired,
                     [color_center, cr or color_ring(i), cr or color_ring((i + 1) % n)], mat)


def leaf(m, center, length, width, yaw, color, curl=0.15, lobes=0, mat=None, steps=10):
    """Flat leaf lying on the ground: a fan from its centre, tips curled up by `curl`."""
    pts = []
    for i in range(steps + 1):
        t = i / steps
        half = width * 0.5 * math.sin(math.pi * t) ** 0.8
        if lobes:
            half *= 0.8 + 0.2 * abs(math.sin(math.pi * t * lobes))
        pts.append((t * length - length / 2, half))
    outline = pts + [(x, -z) for x, z in reversed(pts[1:-1])]
    ring = []
    for x, z in outline:
        lift = curl * (abs(x) / (length / 2)) ** 2 + curl * 0.5 * (abs(z) / (width / 2 + 1e-6)) ** 2
        ring.append(yaw_point((z, lift, x), yaw, center))
    edge = tuple(c * 0.85 for c in color[:3])
    fan(m, add(center, (0.0, curl * 0.1, 0.0)), ring, color, edge, (0.0, 1.0, 0.0), mat)


def blade(m, base, height, width, yaw, bend, color_base, color_tip, mat=None, segments=3):
    """Tapered curved grass blade, double-sided material expected."""
    dx, dz = math.sin(yaw), math.cos(yaw)
    side = (dz, 0.0, -dx)
    pts = []
    for i in range(segments + 1):
        t = i / segments
        off = bend * t * t
        pts.append((base[0] + dx * off, base[1] + height * t * (1 - 0.15 * t * bend / max(height, 1e-6)),
                    base[2] + dz * off))
    for i in range(segments):
        t0, t1 = i / segments, (i + 1) / segments
        w0, w1 = width * (1 - t0) * 0.5, width * (1 - t1) * 0.5
        c0 = geo.mix(color_base, color_tip, t0)
        c1 = geo.mix(color_base, color_tip, t1)
        a = add(pts[i], mul(side, -w0))
        b = add(pts[i], mul(side, w0))
        c = add(pts[i + 1], mul(side, w1))
        d = add(pts[i + 1], mul(side, -w1))
        if i == segments - 1:
            m.poly([a, b, pts[i + 1]], [c0, c0, c1], mat)
        else:
            m.poly([a, b, c, d], [c0, c0, c1, c1], mat)


def frond(m, base, yaw, length, rise, droop, rng, color_fn, mat=None, pinnae=18):
    """Fern frond: an arching rachis with paired tapered leaflets. color_fn(t) per leaflet."""
    dx, dz = math.sin(yaw), math.cos(yaw)
    p0 = base
    p1 = (base[0] + dx * length * 0.35, base[1] + rise, base[2] + dz * length * 0.35)
    p2 = (base[0] + dx * length, base[1] + rise - droop, base[2] + dz * length)
    rachis = [bezier(p0, p1, p2, i / pinnae) for i in range(pinnae + 1)]
    side = (dz, 0.0, -dx)
    for i in range(1, pinnae):
        t = i / pinnae
        p = rachis[i]
        q = rachis[i + 1]
        fwd = geo._norm(sub(q, p))
        leaf_len = length * 0.22 * math.sin(math.pi * (0.12 + 0.88 * t)) * (1.0 - 0.35 * t)
        leaf_w = leaf_len * 0.28
        col = color_fn(t)
        for s in (-1, 1):
            out = geo._norm(add(mul(side, s), mul(fwd, 0.45)))
            tip = add(add(p, mul(out, leaf_len)), (0.0, -leaf_len * 0.18, 0.0))
            mid = add(p, mul(out, leaf_len * 0.45))
            w = mul(fwd, leaf_w * 0.5)
            m.poly([p, add(mid, mul(w, -1)), tip, add(mid, w)], [col, col, geo.scale(col, 1.15), col], mat)
    # The rachis itself: a thin flat strip.
    for i in range(pinnae):
        a, b = rachis[i], rachis[i + 1]
        w = 0.12 * (1 - i / pinnae) + 0.03
        m.poly([add(a, mul(side, -w)), add(a, mul(side, w)), add(b, mul(side, w)), add(b, mul(side, -w))],
               color_fn(0.0), mat)


def frame_box(m, center, axes, half, color, mat=None, skip=()):
    """Box in an arbitrary orthonormal frame `axes` = (X, Y, Z) with half sizes.
    `color` is one colour or a dict keyed by face name (+x, -x, +y, -y, +z, -z)."""
    X, Y, Z = axes

    def corner(sx, sy, sz):
        return tuple(center[i] + X[i] * sx * half[0] + Y[i] * sy * half[1] + Z[i] * sz * half[2] for i in range(3))

    faces = {
        "+x": ([(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)], X),
        "-x": ([(-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1)], mul(X, -1)),
        "+y": ([(-1, 1, -1), (-1, 1, 1), (1, 1, 1), (1, 1, -1)], Y),
        "-y": ([(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)], mul(Y, -1)),
        "+z": ([(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)], Z),
        "-z": ([(-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1)], mul(Z, -1)),
    }
    for name, (corners, normal) in faces.items():
        if name in skip:
            continue
        col = color[name] if isinstance(color, dict) else color
        geo.oriented(m, [corner(*c) for c in corners], normal, col, mat)


def extrude(m, outline, origin, U, V, W, length, side_color, cap_color, mat=None):
    """Convex CCW outline in the (U, V) plane extruded `length` along W.
    side_color(i) colours edge i -> i + 1; caps are fans."""
    n = len(outline)

    def p(q, w):
        return tuple(origin[i] + U[i] * q[0] + V[i] * q[1] + W[i] * w for i in range(3))

    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        eu, ev = b[0] - a[0], b[1] - a[1]
        out = add(mul(U, ev), mul(V, -eu))
        geo.oriented(m, [p(a, 0), p(b, 0), p(b, length), p(a, length)], out, side_color(i), mat)
    cu = sum(q[0] for q in outline) / n
    cv = sum(q[1] for q in outline) / n
    for w, desired in ((0.0, mul(W, -1)), (length, W)):
        centre = p((cu, cv), w)
        for i in range(n):
            a, b = outline[i], outline[(i + 1) % n]
            geo.oriented(m, [centre, p(a, w), p(b, w)], desired, cap_color, mat)
