"""The craft table and the play-dough cake: everything dressed to the authoritative shell.

Walkable tops never rise above their baked heights: the cloth lies 3 cm under y = 0
and flush decals stack between it and the top. Walls (the drape and the cake's sides)
stay inside their collision outlines and stand at most WALL_PROUD outside them inside
the blast corridor; the drape flares and puddles only below the kill plane. Visual
outlines follow the baked polygons, not the circles they approximate.
"""
from __future__ import annotations

import math

from stagekit import geo
import font
import palette as P
import textures

BUILD = "TM_Build"   # StageVertexLit: lit by the stage and match lights
GLOW = "TM_Glow"     # StageNeon: glitter twinkle
WALL_PROUD = 0.025
FLOOR_Y = -16.0
CLOTH_Y = -0.03      # oilcloth surface; decals stack above it up to y = 0
LIP = 0.30           # rolled edge: from CLOTH_Y at this inset down to LIP_DROP at the edge
LIP_DROP = -0.30
BORDER = (0.42, 1.12)  # raspberry band, insets from the edge
CELL = 1.0           # gingham check size on the top
PLATE_R = 5.3


# ---------------------------------------------------------------- outline helpers

def poly_radius(theta, radius, segments):
    """Distance from the centre to a regular polygon (vertex 0 on +X) along theta."""
    step = 2 * math.pi / segments
    k = math.floor((theta % (2 * math.pi)) / step)
    mid = (k + 0.5) * step
    return radius * math.cos(math.pi / segments) / math.cos((theta % (2 * math.pi)) - mid)


def clip_convex(poly, outline):
    """Sutherland-Hodgman: clip polygon (map points) by a convex CCW outline."""
    out = list(poly)
    for a, b, (nx, nz), L in geo.edges(outline):
        if not out:
            break
        src, out = out, []
        for i in range(len(src)):
            p, q = src[i - 1], src[i]
            dp = (p[0] - a[0]) * nx + (p[1] - a[1]) * nz
            dq = (q[0] - a[0]) * nx + (q[1] - a[1]) * nz
            if dq <= 0:
                if dp > 0:
                    t = dp / (dp - dq)
                    out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
                out.append(q)
            elif dp <= 0:
                t = dp / (dp - dq)
                out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return out


def top_poly(m, poly, y, color, mat=BUILD, uvs=None):
    """Upward-facing polygon from map points. Upward faces run clockwise in map view
    (+x right, +z up); the area sign decides, so collinear first corners cannot flip it."""
    pts = list(poly)
    if geo.polygon_area(pts) > 0:
        pts.reverse()
        if isinstance(color, list):
            color = list(reversed(color))
        if uvs is not None:
            uvs = list(reversed(uvs))
    m.poly([(x, y, z) for x, z in pts], color, mat, uvs)


def rotated_rect(cx, cz, w, h, angle):
    c, s = math.cos(angle), math.sin(angle)
    pts = []
    for u, v in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)):
        pts.append((cx + u * c - v * s, cz + u * s + v * c))
    return pts


def ribbon(m, pts, width, y, color, mat=BUILD):
    """Flat stroke of quads along a map polyline (crayon line, dough snake)."""
    for i in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[i], pts[i + 1]
        dx, dz = bx - ax, bz - az
        L = math.hypot(dx, dz)
        if L < 1e-5:
            continue
        nx, nz = -dz / L * width / 2, dx / L * width / 2
        # extend each piece a little so joints do not open on bends
        ex, ez = dx / L * width * 0.35, dz / L * width * 0.35
        quad = [(ax - ex + nx, az - ez + nz), (ax - ex - nx, az - ez - nz), (bx + ex - nx, bz + ez - nz), (bx + ex + nx, bz + ez + nz)]
        top_poly(m, quad, y, color, mat)


def star_points(cx, cz, r, inner=0.45, points=5, rot=0.0):
    pts = []
    for k in range(points * 2):
        a = rot + math.pi / 2 + k * math.pi / points
        rr = r if k % 2 == 0 else r * inner
        pts.append((cx + rr * math.cos(a), cz + rr * math.sin(a)))
    return pts


def heart_points(cx, cz, r, rot=0.0, n=20):
    pts = []
    c, s = math.cos(rot), math.sin(rot)
    for k in range(n):
        t = 2 * math.pi * k / n
        x = 16 * math.sin(t) ** 3
        z = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        x, z = x / 17 * r, z / 17 * r
        pts.append((cx + x * c - z * s, cz + x * s + z * c))
    return pts


def disc_points(cx, cz, r, n=16, rot=0.0):
    return [(cx + r * math.cos(rot + 2 * math.pi * k / n), cz + r * math.sin(rot + 2 * math.pi * k / n)) for k in range(n)]


def fan(m, pts, y, color, mat=BUILD):
    """Star/heart outlines are not convex: triangulate from their centroid."""
    cx = sum(p[0] for p in pts) / len(pts)
    cz = sum(p[1] for p in pts) / len(pts)
    for i in range(len(pts)):
        top_poly(m, [(cx, cz), pts[i], pts[(i + 1) % len(pts)]], y, color, mat)


# ---------------------------------------------------------------- the table

class CraftTable:
    def __init__(self, build):
        self.b = build
        self.rng = build.rng
        d = build.data
        self.R = d["table"]["radius"]
        self.N = d["table"]["segments"]
        self.outline = d["tableOutline"]
        self.top = d["top"]
        self.kill = d["killY"]
        self.cake_R = d["cake"]["radius"]
        self.cake_N = d["cake"]["segments"]
        self.cake_top = d["cake"]["top"]
        self.cake_outline = d["cakeOutline"]

    # ------------------------------------------------------------ top
    def cloth_top(self, m):
        """Gingham oilcloth inside the border band, as 1 m checks clipped to the outline."""
        inner = geo.inset_polygon(self.outline, BORDER[1])
        lo = -self.R
        n = int(2 * self.R / CELL) + 1
        with geo.detail(m, 0.55):
            for i in range(n):
                for j in range(n):
                    x0, z0 = lo + i * CELL, lo + j * CELL
                    cx, cz = x0 + CELL / 2, z0 + CELL / 2
                    if math.hypot(cx, cz) < PLATE_R - 1.0:
                        continue  # hidden under the paper plate and the cake
                    poly = clip_convex([(x0, z0), (x0 + CELL, z0), (x0 + CELL, z0 + CELL), (x0, z0 + CELL)], inner)
                    if len(poly) < 3 or abs(geo.polygon_area(poly)) < 1e-3:
                        continue
                    stripes = (i % 2 == 0) + (j % 2 == 0)
                    base = (P.CLOTH, P.CLOTH_STRIPE, P.CLOTH_CROSS)[stripes]
                    top_poly(m, poly, CLOTH_Y, geo.jitter(base, self.rng, 0.012))

    def border(self, m):
        """Raspberry band with a white piping line and scallops along its inner edge."""
        rings = [0.0, LIP, BORDER[0] - 0.1, BORDER[0], BORDER[1]]
        outl = [geo.inset_polygon(self.outline, d) if d else list(self.outline) for d in rings]
        n = self.N
        with geo.detail(m, 0.5):
            # rolled lip: quarter round from the piping down to the vertical drape
            prof = [(LIP, CLOTH_Y), (0.20, -0.05), (0.10, -0.11), (0.03, -0.19), (0.0, LIP_DROP)]
            rings_lip = [geo.inset_polygon(self.outline, d) if d else list(self.outline) for d, _ in prof]
            for k in range(len(prof) - 1):
                (d0, y0), (d1, y1) = prof[k], prof[k + 1]
                a, bring = rings_lip[k], rings_lip[k + 1]
                for i in range(n):
                    j = (i + 1) % n
                    col = P.CLOTH_BORDER if k < 2 else P.CLOTH_BORDER_DARK
                    pts = [(a[i][0], y0, a[i][1]), (a[j][0], y0, a[j][1]), (bring[j][0], y1, bring[j][1]), (bring[i][0], y1, bring[i][1])]
                    nrm_out = ((a[i][0] + a[j][0]) / 2, 0.0, (a[i][1] + a[j][1]) / 2)
                    L = math.hypot(nrm_out[0], nrm_out[2])
                    desired = (nrm_out[0] / L * (1 - (k + 0.5) / 4), 0.6, nrm_out[2] / L * (1 - (k + 0.5) / 4))
                    geo.oriented(m, pts, desired, geo.jitter(col, self.rng, 0.02), BUILD)
            for band, color in ((0, P.CLOTH_BORDER), (1, P.CLOTH_SCALLOP), (2, P.CLOTH_BORDER)):
                outer, inner = outl[band + 1], outl[band + 2]
                for i in range(n):
                    j = (i + 1) % n
                    top_poly(m, [outer[i], outer[j], inner[j], inner[i]], CLOTH_Y, geo.jitter(color, self.rng, 0.015))
    def scallops(self, m):
        """Half discs of raspberry biting into the gingham, one per edge (a flush decal)."""
        n = self.N
        edge_in = geo.inset_polygon(self.outline, BORDER[1])
        with geo.detail(m, 0.5):
            for i in range(n):
                a, b = edge_in[i], edge_in[(i + 1) % n]
                mx, mz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
                L = math.hypot(b[0] - a[0], b[1] - a[1])
                r = L * 0.42
                ang = math.atan2(-mz, -mx)  # pointing inward
                pts = [(mx, mz)]
                for k in range(9):
                    t = ang - math.pi / 2 + math.pi * k / 8
                    pts.append((mx + r * math.cos(t), mz + r * math.sin(t)))
                for k in range(1, len(pts) - 1):
                    top_poly(m, [pts[0], pts[k], pts[k + 1]], CLOTH_Y + 0.004, P.CLOTH_BORDER)

    def plate(self, m):
        """Paper party plate under the cake: flat, with a fluted rim drawn in."""
        y = CLOTH_Y + 0.006
        n = 64
        # The plate is baked without AO (a flush decal), so the cake's contact shade is painted in.
        shade = geo.scale(P.PLATE, 0.72)
        rings = [(self.cake_R - 0.3, shade, shade), (self.cake_R + 0.15, shade, P.PLATE), (self.cake_R + 0.9, P.PLATE, P.PLATE),
                 (PLATE_R - 1.0, P.PLATE, P.PLATE), (PLATE_R - 0.25, None, None), (PLATE_R, P.PLATE_RIM, P.PLATE_RIM)]
        with geo.detail(m, 0.3):
            for k in range(len(rings) - 1):
                r0, c0, c1 = rings[k]
                r1 = rings[k + 1][0]
                for i in range(n):
                    a0 = 2 * math.pi * i / n
                    a1 = 2 * math.pi * (i + 1) / n
                    if c0 is None:
                        col = P.PLATE if i % 2 == 0 else P.PLATE_RIM
                        cols = [col] * 4
                    else:
                        cols = [c0, c1, c1, c0]
                    q = [(r0 * math.cos(a0), r0 * math.sin(a0)), (r1 * math.cos(a0), r1 * math.sin(a0)),
                         (r1 * math.cos(a1), r1 * math.sin(a1)), (r0 * math.cos(a1), r0 * math.sin(a1))]
                    top_poly(m, q, y, cols)

    def papers(self, m):
        """Drawing paper and construction-paper cut-outs lying flat between the spawns."""
        inside = geo.inset_polygon(self.outline, BORDER[1] + 0.2)
        sheets = [
            # (centre angle deg, radius, rotation deg, drawing)
            (90, 9.0, 8, "house"),
            (0, 9.0, -6, "fighters"),
            (270, 9.0, 11, "rainbow"),
            (180, 9.0, -9, "rocket"),
        ]
        for k, (deg, r, rot, name) in enumerate(sheets):
            a = math.radians(deg)
            cx, cz = r * math.cos(a), r * math.sin(a)
            # long side tangential so the sheet stays inside the border band
            ang = a + math.pi / 2 + math.radians(rot)
            rect = rotated_rect(cx, cz, 8.9, 6.3, ang)
            for x, z in rect:
                if not geo.inside_convex(inside, x, z):
                    raise ValueError("sheet %s leaves the tabletop dressing area" % name)
            (u0, v0), (u1, v1) = textures.drawing_uv(name)
            y = CLOTH_Y + 0.008 + k * 0.003
            with geo.detail(m, 0.25):
                top_poly(m, rect, y, P.PAPER, BUILD, uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        # cut-outs: a lemon star and a pink heart on the diagonals between sheets
        with geo.detail(m, 0.3):
            fan(m, star_points(-7.6, 9.4, 2.0, rot=0.3), CLOTH_Y + 0.02, P.CARD_LEMON)
            fan(m, heart_points(8.6, -8.4, 1.9, rot=-0.4), CLOTH_Y + 0.02, P.CARD_PINK)
            fan(m, star_points(9.8, 7.0, 1.3, rot=1.1), CLOTH_Y + 0.02, P.CARD_LILAC)

    def scribbles(self, m):
        """Crayon that missed the paper: loops and zigzags straight onto the oilcloth."""
        rng = self.rng
        colors = list(P.CRAYON.values())
        for k in range(9):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(6.0, 12.5)
            cx, cz = r * math.cos(a), r * math.sin(a)
            col = colors[k % len(colors)]
            pts = []
            kind = k % 3
            for s in range(26):
                t = s / 25
                if kind == 0:   # loop-de-loop
                    pts.append((cx + t * 3.0 + 0.6 * math.cos(t * 14), cz + 0.6 * math.sin(t * 14)))
                elif kind == 1:  # zigzag
                    pts.append((cx + t * 3.2, cz + (0.45 if s % 2 else -0.45)))
                else:           # spiral
                    rr = 0.2 + 1.1 * t
                    pts.append((cx + rr * math.cos(t * 12), cz + rr * math.sin(t * 12)))
            rot = rng.uniform(0, math.pi)
            c, s_ = math.cos(rot), math.sin(rot)
            pts = [(cx + (x - cx) * c - (z - cz) * s_, cz + (x - cx) * s_ + (z - cz) * c) for x, z in pts]
            if all(math.hypot(x, z) < self.R - BORDER[1] - 0.3 and math.hypot(x, z) > PLATE_R + 0.3 for x, z in pts):
                with geo.detail(m, 0.6):
                    ribbon(m, pts, 0.13, CLOTH_Y + 0.016, col)

    def stickers(self, m):
        rng = self.rng
        spots = [(5.5, 11.0), (-11.5, 3.0), (-3.0, -12.0), (12.0, -2.5), (-6.0, -6.0), (3.0, 6.5),
                 (11.2, 5.4), (-10.6, -6.8), (0.5, 12.6), (-12.4, 0.0), (7.0, -11.0), (-2.0, 7.4)]
        kinds = ["star", "heart", "smile"]
        for k, (x, z) in enumerate(spots):
            kind = kinds[k % 3]
            rot = rng.uniform(0, math.pi * 2)
            y = CLOTH_Y + 0.022
            with geo.detail(m, 0.1):
                if kind == "star":
                    fan(m, star_points(x, z, 0.85, rot=rot), y, P.PAPER)
                    fan(m, star_points(x, z, 0.68, rot=rot), y + 0.002, (1.0, 0.80, 0.22))
                elif kind == "heart":
                    fan(m, heart_points(x, z, 0.85, rot=rot), y, P.PAPER)
                    fan(m, heart_points(x, z, 0.68, rot=rot), y + 0.002, (0.95, 0.30, 0.50))
                else:
                    fan(m, disc_points(x, z, 0.8, 18), y, P.PAPER)
                    fan(m, disc_points(x, z, 0.66, 18), y + 0.002, (1.0, 0.86, 0.25))
                    for ex in (-0.22, 0.22):
                        ox, oz = ex * math.cos(rot) - 0.18 * math.sin(rot), ex * math.sin(rot) + 0.18 * math.cos(rot)
                        fan(m, disc_points(x + ox, z + oz, 0.08, 8), y + 0.004, P.CRAYON["black"])
                    smile = []
                    for s in range(9):
                        t = math.pi + 0.5 + (math.pi - 1.0) * s / 8
                        u, v = 0.38 * math.cos(t), 0.38 * math.sin(t) + 0.05
                        smile.append((x + u * math.cos(rot) - v * math.sin(rot), z + u * math.sin(rot) + v * math.cos(rot)))
                    ribbon(m, smile, 0.07, y + 0.004, P.CRAYON["black"])

    def glitter(self, g):
        """A spilled glitter pile beside a glue blob: tiny flakes that twinkle (StageNeon)."""
        rng = self.rng
        cx, cz = -8.0, -3.2
        with geo.detail(g, 0.0):
            top_poly(g, disc_points(cx + 0.4, cz - 0.3, 1.1, 14), CLOTH_Y + 0.012, (0.92, 0.94, 0.96), GLOW)
        for k in range(420):
            r = abs(rng.gauss(0, 1.3)) + 0.05
            a = rng.uniform(0, 2 * math.pi)
            x, z = cx + r * math.cos(a) * 1.4, cz + r * math.sin(a)
            if math.hypot(x, z) > self.R - BORDER[1] - 0.2:
                continue
            s = rng.uniform(0.05, 0.11)
            col = rng.choice(P.GLITTER)
            rate = rng.uniform(0.35, 1.4)
            with geo.state(g, alpha=1.0, anim=(rate, rng.random())):
                top_poly(g, rotated_rect(x, z, s, s, rng.uniform(0, math.pi)), CLOTH_Y + 0.018 + (k % 3) * 0.001, col, GLOW)

    # ------------------------------------------------------------ drape
    def drape(self, m):
        """Floor-length oilcloth: vertical inside the corridor (flutes only inward), then a
        flared, hemmed puddle on the floor below the kill plane."""
        sub = 2
        cols = self.N * sub
        rows = [LIP_DROP, -0.75, -1.25] + [-1.25 - 1.0 * k for k in range(1, 14)] + [self.kill, -15.45, FLOOR_Y + 0.02]
        pts = []
        for i in range(cols):
            e, f = divmod(i, sub)
            a, b = self.outline[e], self.outline[(e + 1) % self.N]
            t = f / sub
            pts.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))

        def place(i, y):
            x, z = pts[i % cols]
            r = math.hypot(x, z)
            theta = math.atan2(z, x)
            depth = 0.10 * min(1.0, max(0.0, (-y - 0.6) / 7.0))
            flute = depth * (0.5 - 0.5 * math.cos(16 * theta))
            flare = 0.0
            if y < self.kill:
                flare = (self.kill - y) / (self.kill - FLOOR_Y) * 0.8
            k = (r - flute + flare) / r
            return (x * k, y, z * k)

        with geo.detail(m, 0.45):
            for i in range(cols):
                for rj in range(len(rows) - 1):
                    y0, y1 = rows[rj], rows[rj + 1]
                    quad = [place(i, y0), place(i + 1, y0), place(i + 1, y1), place(i, y1)]
                    mid = ((quad[0][0] + quad[1][0]) / 2, 0.0, (quad[0][2] + quad[1][2]) / 2)
                    if rj < 2:
                        col = P.CLOTH_BORDER if rj == 0 else P.CLOTH_BORDER_DARK
                    elif y1 < -15.2:
                        col = P.CLOTH_HEM
                    else:
                        stripes = ((i // sub) % 2 == 0) + (rj % 2 == 0)
                        col = (P.CLOTH, P.CLOTH_STRIPE, P.CLOTH_CROSS)[stripes]
                    geo.oriented(m, quad, mid, geo.jitter(col, self.rng, 0.012), BUILD)

    # ------------------------------------------------------------ the cake
    def _cake_inset(self, theta, y):
        lump = 0.045 + 0.035 * math.sin(theta * 7 + 1.3) * math.sin(y * 4.1 + theta * 2) + 0.03 * math.sin(theta * 13 + y * 7)
        lump = max(0.02, lump + 0.035)
        for boundary in (0.5, 1.0):
            g = abs(y - boundary)
            if g < 0.06:
                lump += 0.09 * (1 - g / 0.06)
        if y > 1.36:
            t = (y - 1.36) / 0.14
            lump += 0.2 * (1 - math.sqrt(max(0.0, 1 - t * t)))
        if y < 0.06:
            lump = max(0.025, lump - 0.03)
        return lump

    def cake_side(self, m):
        segs = self.cake_N * 2
        ys = [0.0, 0.05, 0.25, 0.44, 0.5, 0.56, 0.75, 0.94, 1.0, 1.06, 1.25, 1.36, 1.42, 1.465, 1.49]
        ring = []
        for y in ys:
            row = []
            for i in range(segs):
                th = 2 * math.pi * i / segs
                r = poly_radius(th, self.cake_R, self.cake_N) - self._cake_inset(th, y)
                row.append((r * math.cos(th), y, r * math.sin(th)))
            ring.append(row)
        self.cake_rim = ring[-1]
        # Shared vertices per ring: the cake object is smooth shaded, so the dough reads soft.
        ids = [[m.add(p) for p in row] for row in ring]
        with geo.detail(m, 0.65):
            for j in range(len(ys) - 1):
                ymid = (ys[j] + ys[j + 1]) / 2
                base = P.DOUGH_PINK if ymid < 0.5 else P.DOUGH_LILAC if ymid < 1.0 else P.DOUGH_LEMON
                if any(abs(ymid - g) < 0.07 for g in (0.5, 1.0)):
                    base = geo.scale(base, 0.84)
                for i in range(segs):
                    k = (i + 1) % segs
                    # upper ring first: (a_top, b_top, b_low, a_low) faces outward for CCW angles
                    m.face([ids[j + 1][i], ids[j + 1][k], ids[j][k], ids[j][i]], geo.jitter(base, self.rng, 0.008), BUILD)

    def cake_top_surface(self, m):
        """Flat lemon top just under the walkable height with flush dough decorations."""
        y = self.cake_top - 0.012
        segs = self.cake_N * 2
        rim = self.cake_rim
        radii = [0.0, 0.9, 1.8, 2.5, 3.0]
        rings = [[(r * math.cos(2 * math.pi * i / segs), r * math.sin(2 * math.pi * i / segs)) for i in range(segs)] for r in radii[1:]]
        rings.append([(p[0], p[2]) for p in rim])
        rim_y = rim[0][1]
        with geo.detail(m, 0.6):
            for i in range(segs):
                k = (i + 1) % segs
                top_poly(m, [(0.0, 0.0), rings[0][i], rings[0][k]], y, geo.jitter(P.DOUGH_LEMON, self.rng, 0.02))
            for j in range(len(rings) - 1):
                for i in range(segs):
                    k = (i + 1) % segs
                    if j == len(rings) - 2:
                        quad = [(rings[j][i][0], y, rings[j][i][1]), (rings[j][k][0], y, rings[j][k][1]),
                                (rings[j + 1][k][0], rim_y, rings[j + 1][k][1]), (rings[j + 1][i][0], rim_y, rings[j + 1][i][1])]
                        geo.oriented(m, quad, (0, 1, 0), geo.jitter(P.DOUGH_LEMON, self.rng, 0.02), BUILD)
                    else:
                        top_poly(m, [rings[j][i], rings[j][k], rings[j + 1][k], rings[j + 1][i]], y,
                                 geo.jitter(P.DOUGH_LEMON, self.rng, 0.02))
        # white dough "frosting" blobs around the top edge (flush discs)
        with geo.detail(m, 0.4):
            for i in range(26):
                a = 2 * math.pi * i / 26
                r = 3.05
                fan(m, disc_points(r * math.cos(a), r * math.sin(a), 0.24, 10), self.cake_top - 0.006, P.DOUGH_WHITE)
        # the kids' lettering, slightly wobbly: HAPY / BIRTHDAY (sic)
        letter_colors = [P.DOUGH_RED, (0.24, 0.48, 0.90), P.DOUGH_GREEN, P.DOUGH_PINK]
        for line, (text, vz, h) in enumerate((("HAPY", 0.75, 0.95), ("BIRTHDAY", -0.85, 0.72))):
            strokes, w = font.text_strokes(text, h, origin=(0.0, vz - h / 2), align="center", tracking=1.05)
            per = len(text)
            for si, s in enumerate(strokes):
                letter = min(per - 1, int((s[0][0] + w / 2) / (w / per))) if w else 0
                col = letter_colors[(letter + line) % len(letter_colors)]
                wob = [(x + 0.03 * math.sin(z * 5 + si), z + 0.03 * math.cos(x * 4 + si)) for x, z in s]
                with geo.detail(m, 0.5):
                    ribbon(m, wob, 0.2, self.cake_top - 0.003, col)
        # rainbow sprinkles
        rng = self.rng
        with geo.detail(m, 0.2):
            for k in range(140):
                r = math.sqrt(rng.random()) * 3.2
                a = rng.uniform(0, 2 * math.pi)
                x, z = r * math.cos(a), r * math.sin(a)
                if abs(z) < 1.4 and abs(x) < 3.2:
                    continue  # keep the lettering clear
                col = rng.choice(list(P.CRAYON.values())[:8])
                if r > 2.75:
                    continue  # the frosting ring
                top_poly(m, rotated_rect(x, z, 0.26, 0.08, rng.uniform(0, math.pi)), self.cake_top - 0.008, col)

    def googly_eyes(self, m):
        """Two googly eyes pressed into the cake's south face (within WALL_PROUD of the dough)."""
        for side, ang in ((-1, -math.pi / 2 - 0.26), (1, -math.pi / 2 + 0.26)):
            r = poly_radius(ang, self.cake_R, self.cake_N) - self._cake_inset(ang, 0.78)
            nx, nz = math.cos(ang), math.sin(ang)
            right = (-nz, nx)
            cx, cz, cy = r * nx, r * nz, 0.78
            for rad, col, off, dv in ((0.4, P.GOOGLY_WHITE, 0.008, 0.0), (0.19, P.GOOGLY_BLACK, 0.016, -0.14)):
                pts = []
                for k in range(16):
                    t = 2 * math.pi * k / 16
                    u, v = rad * math.cos(t) + (0.06 * side if rad < 0.3 else 0.0), rad * math.sin(t) + dv
                    pts.append((cx + right[0] * u + nx * off, cy + v, cz + right[1] * u + nz * off))
                geo.oriented(m, pts, (nx, 0.0, nz), col, BUILD)

    # ------------------------------------------------------------ assembly
    def build_all(self):
        b = self.b
        b.surface("Craft Table", "Tabletop", lambda m, g: (self.cloth_top(m), self.border(m)))
        # Flush layers above the cloth: baked without AO and hidden while the cloth bakes, so
        # cloth corners under them are not darkened into smudges.
        b.surface("Table Decals", "Table Decals", lambda m, g: (self.scallops(m), self.plate(m), self.papers(m),
                                                                self.scribbles(m), self.stickers(m), self.glitter(g)))
        b.wall("Craft Table", "Tablecloth Drape", lambda m, g: self.drape(m))
        b.cake("Play-Dough Cake", "Cake Sides", lambda m, g: (self.cake_side(m), self.googly_eyes(m)), top=False)
        b.cake("Play-Dough Cake", "Cake Top", lambda m, g: self.cake_top_surface(m), top=True)
