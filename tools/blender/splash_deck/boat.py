"""The fishing boat: deck, painted hull, wheelhouse and fish hatch.

Everything here dresses the authoritative shell from layout.py and stays inside it:
the deck lies exactly on y = 0 within the outline (a 5 cm chamfer rounds its edge),
the hull walls follow the outline from the chamfer down past the waterline, and the
raised spots' faces sit on their collision boxes with decals at most DECAL_PROUD
proud. Nothing stands on a walkable top: the wheelhouse roof number and the hatch
boards are part of the flat top meshes, not quads above them.
"""
from __future__ import annotations

import math

from stagekit import geo
import layout as LAYOUT
import palette as P
import textures as T

WOOD = "SD_Wood"
DECK = "SD_Deck"
HULL = "SD_Hull"
CHAMFER = 0.05
DECAL_PROUD = 0.012
WALL_PROUD = 0.025      # largest allowed outward offset of any hull/raised dressing
WHITE = geo.WHITE_UV


def _quad_uv(region):
    (u0, v0), (u1, v1) = T.region_uv(region)
    return [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]


class Boat:
    def __init__(self, build):
        self.b = build
        self.spec = build.data["spec"]
        self.outline = build.data["outline"]
        self.top = build.data["top"]

    def build_all(self):
        b = self.b
        b.part("top", "Deck", "Deck", self.deck)
        b.part("wall", "Hull", "Hull", self.hull)
        b.part("raised", "Wheelhouse", "Wheelhouse", self.wheelhouse)
        b.part("raised", "FishHatch", "FishHatch", self.fish_hatch)

    # ------------------------------------------------------------ deck
    def deck(self, m):
        inner = geo.inset_polygon(self.outline, CHAMFER)
        y = self.top
        with geo.detail(m, 0.22):
            # Zipper the inset outline across the beam (it is mirror-symmetric like the shell).
            n = len(inner)
            half = (n + 1) // 2  # port side: stem .. transom port corner
            port = inner[:half]
            star = [inner[0]] + list(reversed(inner[half:]))
            for i in range(len(port) - 1):
                pa, pb = port[i], port[i + 1]
                sa, sb = star[i], star[i + 1]
                quad = [sa, pa, pb, sb] if i else [pa, pb, sb]
                pts = [(p[0], y, p[1]) for p in quad]
                uvs = [T.deck_uv(p[0], p[1]) for p in quad]
                geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), DECK, uvs)
            # Chamfered edge from the inset deck to the hull top, in the margin paint.
            for i in range(n):
                a, b2 = self.outline[i], self.outline[(i + 1) % n]
                ia, ib = inner[i], inner[(i + 1) % n]
                pts = [(ia[0], y, ia[1]), (ib[0], y, ib[1]), (b2[0], y - CHAMFER, b2[1]), (a[0], y - CHAMFER, a[1])]
                nx, nz = geo.edges(self.outline)[i][2]
                uvs = [T.deck_uv(p[0], p[2]) for p in pts]
                geo.oriented(m, pts, (nx, 1.0, nz), (0.92, 0.92, 0.92), DECK, uvs)

    # ------------------------------------------------------------ hull
    def hull(self, m):
        """Walls on the authoritative outline from the chamfer to below the water.
        Vertices are shared round the ring so the bow curve shades smoothly."""
        pts, s_tab, total = T.hull_perimeter(self.spec)
        y0, y1 = self.top - CHAMFER, T.HULL_BOTTOM
        n = len(pts)
        top_idx, low_idx = [], []
        for i in range(n + 1):  # duplicate the stem column so u can run 0..1
            x, z = pts[i % n]
            top_idx.append(m.add((x, y0, z)))
            low_idx.append(m.add((x, y1, z)))
        with geo.detail(m, 0.25):
            for i in range(n):
                u0, u1 = s_tab[i] / total, s_tab[i + 1] / total
                v0, v1 = T.hull_v(y0), T.hull_v(y1)
                # Outward winding for a CCW outline: top a, top b, low b, low a.
                idx = [top_idx[i], top_idx[i + 1], low_idx[i + 1], low_idx[i]]
                m.face(idx, (1.0, 1.0, 1.0), HULL, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    # ------------------------------------------------------------ raised spots
    def _box_walls(self, m, footprint, top, colors_at, mat=WOOD, rows=None):
        """Four outward walls of a raised box, split into horizontal bands:
        rows = [(y0, y1, colour)] bottom to top; corner posts get `post` colour."""
        x0, z0, x1, z1 = footprint
        ring = [(x1, z0), (x1, z1), (x0, z1), (x0, z0)]  # CCW
        for i in range(4):
            a, b2 = ring[i], ring[(i + 1) % 4]
            for (ya, yb, col) in rows:
                geo.wall_grid(m, a, b2, ya, yb, 1, 1, lambda u, v, c=col: c, mat)
        return ring

    def _wall_decal(self, m, face, center_t, y0, y1, width, region, footprint, mat=WOOD, flip=False):
        """Quad on a wall face ('n', 's', 'e', 'w') of an axis-aligned box, DECAL_PROUD out.
        center_t is the horizontal position along the face from its left edge (seen
        from outside) in metres."""
        x0, z0, x1, z1 = footprint
        d = DECAL_PROUD
        # Unity is left-handed: facing -X your right is +Z, facing +Z your right is +X.
        if face == "e":    # +X face, seen from outside (facing -X): left is -Z
            left, normal, along = (x1 + d, z0), (1.0, 0.0), (0.0, 1.0)
        elif face == "w":  # -X face (facing +X): left is +Z
            left, normal, along = (x0 - d, z1), (-1.0, 0.0), (0.0, -1.0)
        elif face == "n":  # +Z face (facing -Z): left is +X
            left, normal, along = (x1, z1 + d), (0.0, 1.0), (-1.0, 0.0)
        else:              # -Z face (facing +Z): left is -X
            left, normal, along = (x0, z0 - d), (0.0, -1.0), (1.0, 0.0)
        c = (left[0] + along[0] * center_t, left[1] + along[1] * center_t)
        a = (c[0] - along[0] * width / 2, c[1] - along[1] * width / 2)
        b2 = (c[0] + along[0] * width / 2, c[1] + along[1] * width / 2)
        (u0, v0), (u1, v1) = T.region_uv(region)
        if flip:
            u0, u1 = u1, u0
        geo.facade_quad(m, a, b2, y0, y1, normal, (1.0, 1.0, 1.0), mat, uvs=((u0, v0), (u1, v1)))

    def wheelhouse(self, m):
        r = self.spec["raised"]["Wheelhouse"]
        fp = r["footprint"]
        top = r["top"]
        x0, z0, x1, z1 = fp
        with geo.detail(m, 0.2):
            rows = [(0.0, 0.12, geo.scale(P.CABIN_TRIM, 0.8)), (0.12, 0.16, geo.scale(P.CABIN_WHITE, 0.82)),
                    (0.16, 1.32, P.CABIN_WHITE), (1.32, 1.38, P.CABIN_TRIM), (1.38, top, P.CABIN_WHITE)]
            self._box_walls(m, fp, top, None, rows=rows)
            # Siding lines: thin shaded bands every 0.2 m (flush, part of the wall).
            for y in [0.36 + 0.2 * k for k in range(5)]:
                for face in ("e", "w", "n", "s"):
                    self._siding(m, fp, face, y)
            # Corner posts.
            self._corners(m, fp, 0.08, top - 0.001, P.CABIN_TRIM)
        # Front (+X, facing the main deck): the sign between two windows.
        self._wall_decal(m, "e", 2.5, 0.30, 1.30, 2.62, "sign", fp)
        self._wall_decal(m, "e", 0.62, 0.74, 1.26, 0.72, "wheel", fp)
        self._wall_decal(m, "e", 4.38, 0.74, 1.26, 0.72, "window", fp)
        # Sides: two windows and a life ring each.
        for face in ("n", "s"):
            for t in (1.2, 2.55):
                self._wall_decal(m, face, t if face == "n" else 6.0 - t, 0.74, 1.26, 0.9, "window", fp)
            self._wall_decal(m, face, 4.6 if face == "n" else 1.4, 0.42, 1.12, 0.7, "lifering", fp)
        # Back (-X, towards the transom): door and two portholes.
        self._wall_decal(m, "w", 2.5, 0.08, 1.30, 0.66, "door", fp)
        for t in (1.05, 3.95):
            self._wall_decal(m, "w", t, 0.66, 1.18, 0.52, "porthole", fp)
        # Roof: trim border and the registration number, all on the walkable plane.
        self._flat_roof(m, fp, top, P.CABIN_ROOF, P.CABIN_TRIM, 0.14, "roofnum", (4.4, 2.2))

    @staticmethod
    def _corners(m, fp, inside, height, color, proud=0.015):
        """Corner posts/caps reaching `inside` m along each wall and `proud` m out of it."""
        x0, z0, x1, z1 = fp
        for cx, sx in ((x0, -1), (x1, 1)):
            for cz, sz in ((z0, -1), (z1, 1)):
                xa, xb = sorted((cx + sx * proud, cx - sx * inside))
                za, zb = sorted((cz + sz * proud, cz - sz * inside))
                geo.box_minmax(m, (xa, 0.0, za), (xb, height, zb), color, WOOD, skip=("top", "bottom"))

    def _siding(self, m, fp, face, y):
        x0, z0, x1, z1 = fp
        d = 0.002
        h = 0.012
        if face == "e":
            a, b2, n = (x1 + d, z1), (x1 + d, z0), (1.0, 0.0)
        elif face == "w":
            a, b2, n = (x0 - d, z0), (x0 - d, z1), (-1.0, 0.0)
        elif face == "n":
            a, b2, n = (x0, z1 + d), (x1, z1 + d), (0.0, 1.0)
        else:
            a, b2, n = (x1, z0 - d), (x0, z0 - d), (0.0, -1.0)
        geo.facade_quad(m, a, b2, y - h, y + h, n, geo.scale(P.CABIN_WHITE, 0.86), WOOD)

    def _flat_roof(self, m, fp, top, fill, trim, border, region, size):
        """A flat walkable top at exactly `top`: trim border, plain fill and a centred
        atlas panel `size` (along x, along z), as one non-overlapping mesh."""
        x0, z0, x1, z1 = fp
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        px, pz = size[0] / 2, size[1] / 2
        xs = [x0, x0 + border, cx - px, cx + px, x1 - border, x1]
        zs = [z0, z0 + border, cz - pz, cz + pz, z1 - border, z1]
        (u0, v0), (u1, v1) = T.region_uv(region)
        for i in range(5):
            for j in range(5):
                a, b2 = xs[i], xs[i + 1]
                c, d = zs[j], zs[j + 1]
                pts = [(a, top, c), (a, top, d), (b2, top, d), (b2, top, c)]
                if i == 2 and j == 2:
                    # Panel text reads from the main deck looking aft (-X): u along +z, v along -x.
                    uvs = [(u0, v1), (u1, v1), (u1, v0), (u0, v0)]
                    geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), WOOD, uvs)
                    continue
                edge = i in (0, 4) or j in (0, 4)
                geo.oriented(m, pts, (0, 1, 0), trim if edge else fill, WOOD)

    def fish_hatch(self, m):
        r = self.spec["raised"]["FishHatch"]
        fp = r["footprint"]
        top = r["top"]
        x0, z0, x1, z1 = fp
        with geo.detail(m, 0.3):
            rows = [(0.0, 0.1, P.COAMING_DARK), (0.1, 1.12, P.COAMING), (1.12, 1.16, P.COAMING_DARK),
                    (1.16, top, P.TEAK_DARK)]
            self._box_walls(m, fp, top, None, rows=rows)
            # Vertical plank seams on the coaming and steel corner caps.
            for face in ("e", "w", "n", "s"):
                length = (z1 - z0) if face in ("e", "w") else (x1 - x0)
                for t in [0.5 * k for k in range(1, int(length / 0.5))]:
                    self._seam(m, fp, face, t, 0.1, 1.12)
            self._corners(m, fp, 0.14, 1.16, P.STEEL)
        # Hatch boards, net heap and coiled line as the whole walkable plane (atlas region).
        (u0, v0), (u1, v1) = T.region_uv("hatchtop")
        pts = [(x0, top, z0), (x0, top, z1), (x1, top, z1), (x1, top, z0)]
        uvs = [(u0, v0), (u0, v1), (u1, v1), (u1, v0)]
        geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), WOOD, uvs)

    def _seam(self, m, fp, face, t, y0, y1):
        x0, z0, x1, z1 = fp
        d, w = 0.003, 0.012
        if face == "e":
            c = (x1 + d, z1 - t); a, b2, n = (c[0], c[1] + w), (c[0], c[1] - w), (1.0, 0.0)
        elif face == "w":
            c = (x0 - d, z0 + t); a, b2, n = (c[0], c[1] - w), (c[0], c[1] + w), (-1.0, 0.0)
        elif face == "n":
            c = (x0 + t, z1 + d); a, b2, n = (c[0] - w, c[1]), (c[0] + w, c[1]), (0.0, 1.0)
        else:
            c = (x1 - t, z0 - d); a, b2, n = (c[0] + w, c[1]), (c[0] - w, c[1]), (0.0, -1.0)
        geo.facade_quad(m, a, b2, y0, y1, n, P.COAMING_DARK, WOOD)

    # ------------------------------------------------------------ checks
    @staticmethod
    def proud_from_box(footprint, x, z):
        x0, z0, x1, z1 = footprint
        return math.hypot(max(x0 - x, 0.0, x - x1), max(z0 - z, 0.0, z - z1))
