"""The jump course: two painted test blocks and the examiner's lookout tower.

Each obstacle is dressed exactly on its collision box: faces flush (at most
RAISED_PROUD), nothing above the walkable top and nothing below the deck. Tops carry a
yellow landing edge around a dark rubber mat so every jump target reads from above.
"""
from __future__ import annotations

from stagekit import geo
import palette as P
import parts
from parts import GLASS, LIT, Plane

RAISED_PROUD = 0.025
EDGE = 0.16


class Course:
    def __init__(self, build):
        self.b = build
        self.data = build.data
        self.base = build.data["top"]

    def _faces(self, footprint):
        """The four outward wall planes of a footprint: (plane, half width) with u centred."""
        x0, z0, x1, z1 = footprint
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        return {"s": (Plane.facing((cx, 0.0, z0), (0.0, -1.0)), (x1 - x0) / 2),
                "n": (Plane.facing((cx, 0.0, z1), (0.0, 1.0)), (x1 - x0) / 2),
                "w": (Plane.facing((x0, 0.0, cz), (-1.0, 0.0)), (z1 - z0) / 2),
                "e": (Plane.facing((x1, 0.0, cz), (1.0, 0.0)), (z1 - z0) / 2)}

    def _top(self, m, footprint, top, mat_color=P.MAT):
        """Landing top at exactly the collision height: yellow edge band around a rubber mat."""
        x0, z0, x1, z1 = footprint
        e = EDGE
        quads = [((x0, z0), (x1, z0 + e)), ((x0, z1 - e), (x1, z1)), ((x0, z0 + e), (x0 + e, z1 - e)), ((x1 - e, z0 + e), (x1, z1 - e))]
        for (a0, b0), (a1, b1) in quads:
            geo.oriented(m, [(a0, top, b0), (a1, top, b0), (a1, top, b1), (a0, top, b1)], (0, 1, 0), P.PAINT_YELLOW, LIT)
        geo.oriented(m, [(x0 + e, top, z0 + e), (x1 - e, top, z0 + e), (x1 - e, top, z1 - e), (x0 + e, top, z1 - e)],
                     (0, 1, 0), mat_color, LIT)

    def block(self, m, name, stencil, body, accent):
        r = self.data["raised"][name]
        top = r["top"]
        faces = self._faces(r["footprint"])
        for key, (f, half) in faces.items():
            parts.panel(m, f, -half, self.base, half, top, body, off=0.0)
            # Diagonal kick band, a white top band and steel corner guards.
            parts.stripes(m, f, -half, self.base, half, self.base + 0.36, accent, P.INK, period=0.6, off=0.006)
            parts.panel(m, f, -half, top - 0.16, half, top, P.STRIPE_WHITE, off=0.006)
            for u in (-half, half - 0.08):
                parts.panel(m, f, u, self.base, u + 0.08, top, P.STEEL, off=0.012)
        f, half = faces["s"]
        w = 1.9 if top < 2 else 2.2
        parts.plate(m, f, stencil, 0.0, self.base + 0.36 + (top - 0.52) / 2, w, off=0.014, frame=P.STEEL_DARK)
        self._top(m, r["footprint"], top)

    def tower(self, m):
        r = self.data["raised"]["LookoutTower"]
        top = r["top"]
        faces = self._faces(r["footprint"])
        cab0, cab1 = top - 1.9, top - 0.45
        for key, (f, half) in faces.items():
            parts.panel(m, f, -half, self.base, half, cab0, P.TOWER, off=0.0)
            # Steel cladding ribs every 0.6 m, the cabin's glazing band and the roof edge.
            v = self.base + 0.6
            while v < cab0 - 0.1:
                parts.panel(m, f, -half, v, half, v + 0.05, geo.scale(P.TOWER, 0.78), off=0.006)
                v += 0.6
            parts.panel(m, f, -half, cab0, half, cab0 + 0.12, P.STEEL_DARK, off=0.004)
            parts.panel(m, f, -half + 0.06, cab0 + 0.12, half - 0.06, cab1, (1, 1, 1), off=0.006, mat=GLASS,
                        uvs=parts.T.uv("examiner_glass"))
            for u in (-half + 0.05, 0.0, half - 0.05):
                parts.panel(m, f, u - 0.05, cab0 + 0.12, u + 0.05, cab1, P.STEEL_DARK, off=0.012)
            parts.stripes(m, f, -half, cab1, half, top, P.SIGN_YELLOW, P.INK, period=0.5, off=0.006)
            parts.stripes(m, f, -half, self.base, half, self.base + 0.36, P.SIGN_YELLOW, P.INK, period=0.6, off=0.006)
            for u in (-half, half - 0.08):
                parts.panel(m, f, u, self.base, u + 0.08, cab0, P.STEEL_DARK, off=0.012)
        # South face: the EXAMINER board and a steel ladder to the roof.
        f, half = faces["s"]
        parts.plate(m, f, "sign_examiner", -0.55, cab0 - 0.62, 1.4, off=0.016)
        parts.plate(m, f, "stencil_55", -0.55, 1.45, 1.3, off=0.016, frame=P.STEEL_DARK)
        lx0, lx1 = 0.55, 1.05
        for u in (lx0, lx1):
            parts.panel(m, f, u - 0.03, self.base, u + 0.03, top - 0.02, P.STEEL, off=0.022)
        v = self.base + 0.3
        while v < top - 0.1:
            parts.panel(m, f, lx0, v, lx1, v + 0.035, P.STEEL, off=0.02)
            v += 0.3
        self._top(m, r["footprint"], top)


def build_all(b):
    c = Course(b)
    b.part("raised", "Course", "Block15", lambda m: c.block(m, "Block15", "stencil_15", P.ORANGE, P.STRIPE_WHITE))
    b.part("raised", "Course", "Block30", lambda m: c.block(m, "Block30", "stencil_30", P.STRIPE_WHITE, P.ORANGE))
    b.part("raised", "Course", "LookoutTower", c.tower)
    return c
