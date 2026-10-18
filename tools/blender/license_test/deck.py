"""The fight deck, the Department of Fighting's facades and the broken edge barrier.

The deck is one textured surface at the collision top. The building's faces run from the
deck down to the street 60 m below; above the kill plane they stay flush with the
collision (FACADE_PROUD), so a recovering fighter never brushes visible relief. On the
open east face, giant DEPT. OF FIGHTING lettering sits just below the edge where the
recovery camera looks. The guardrail that once lined the open edge is gone: only its two
torn ends remain, bolted beside the north and south walls and hanging over the drop
(the declared barrier exception in build.py).
"""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import parts
from parts import CONCRETE, CONCRETE_TILE, GLASS, LIT, Plane
import textures as T

DECK = "LT_Deck"
STREET_Y = -60.0
FACADE_PROUD = 0.02
FLOOR = 4.0


class Deck:
    def __init__(self, build):
        self.b = build
        self.top = build.data["top"]
        self.x0, self.z0, self.x1, self.z1 = build.data["deck"]

    # ------------------------------------------------------------ deck surface
    def surface(self, m):
        nx, nz = 10, 6
        for i in range(nx):
            for j in range(nz):
                xa = self.x0 + (self.x1 - self.x0) * i / nx
                xb = self.x0 + (self.x1 - self.x0) * (i + 1) / nx
                za = self.z0 + (self.z1 - self.z0) * j / nz
                zb = self.z0 + (self.z1 - self.z0) * (j + 1) / nz
                pts = [(xa, self.top, za), (xb, self.top, za), (xb, self.top, zb), (xa, self.top, zb)]
                geo.oriented(m, pts, (0, 1, 0), (1.0, 1.0, 1.0), DECK, [T.deck_uv(x, z) for x, _, z in pts])

    # ------------------------------------------------------------ facades
    def facades(self, m):
        x0, z0, x1, z1 = self.x0, self.z0, self.x1, self.z1
        east = Plane.facing((x1, 0.0, 0.0), (1.0, 0.0))      # u runs south -> north (z)
        south = Plane.facing((0.0, 0.0, z0), (0.0, -1.0))    # u runs west -> east (x)
        north = Plane.facing((0.0, 0.0, z1), (0.0, 1.0))
        west = Plane.facing((x0, 0.0, 0.0), (-1.0, 0.0))
        half_ew, half_ns = (z1 - z0) / 2, (x1 - x0) / 2
        self._face(m, east, half_ew, letters=True)
        self._face(m, south, half_ns, letters=False)
        self._face(m, north, half_ns, letters=False, plain=True)
        self._face(m, west, half_ew, letters=False, plain=True)

    def _face(self, m, plane, half, letters, plain=False):
        top = self.top
        # Concrete skin down to the street, tiled in metres.
        parts.tiled_panel(m, plane, -half, STREET_Y, half, top, P.CONCRETE, CONCRETE, CONCRETE_TILE, off=0.0)
        # Corner pilasters, a shade darker.
        for u in (-half, half - 0.7):
            parts.panel(m, plane, u, STREET_Y, u + 0.7, top - 0.02, geo.scale(P.CONCRETE, 0.86), off=0.006)
        if not plain:
            # The slab edge: a pale band with a hazard strip just below the deck.
            parts.panel(m, plane, -half, top - 0.9, half, top, P.CONCRETE_WARM, off=0.008)
            u = -half
            while u < half - 0.01:
                w = min(2.4, half - u)
                cut = T.uv("hazard")
                (a0, b0), (a1, b1) = cut
                parts.panel(m, plane, u, top - 0.58, u + w, top - 0.28, (1, 1, 1), off=0.012,
                            uvs=((a0, b0), (a0 + (a1 - a0) * w / 2.4, b1)))
                u += 2.4
            parts.panel(m, plane, -half, top - 1.45, half, top - 0.9, geo.scale(P.CONCRETE_DARK, 0.7), off=0.004)
        y = top - 1.45
        if letters:
            # DEPT. OF FIGHTING across the face, read from beyond the edge while recovering.
            parts.plate(m, plane, "facade_letters", 0.0, y - 1.75, 2 * half - 3.0, off=0.012)
            y -= 3.9
        # Office floors: ribbon windows over concrete spandrels down to the lobby.
        first = 0
        while y - FLOOR > STREET_Y + 5.0:
            lo = y - FLOOR
            if not plain or first % 2 == 0:
                parts.window_grid(m, plane, -half + 1.2, half - 1.2, lo, y, 3.0, FLOOR, "window_office", (1, 1, 1),
                                  off=0.014, sill=0.95, head=0.55, frame=P.MULLION)
            y = lo
            first += 1
        # Street-level lobby: tall glass, a canopy and the entrance doors.
        lobby_top = STREET_Y + 4.6
        parts.panel(m, plane, -half + 1.2, STREET_Y, half - 1.2, lobby_top, (1, 1, 1), off=0.05, mat=GLASS, uvs=T.uv("glass"))
        if not plain:
            parts.panel(m, plane, -3.0, STREET_Y, 3.0, STREET_Y + 2.8, (1, 1, 1), off=0.07, uvs=T.uv("door_glass"))
            parts.panel(m, plane, -4.0, lobby_top + 0.1, 4.0, lobby_top + 0.45, geo.scale(P.STEEL_DARK, 1.2), off=1.6, depth=1.55)
            parts.plate(m, plane, "sign_dept", 0.0, lobby_top + 1.4, 4.0, off=0.06)

    # ------------------------------------------------------------ broken barrier
    def barrier(self, m):
        """The two torn ends of the edge guardrail, at the north-east and south-east corners."""
        x = self.x1
        for sign in (1, -1):
            wall = self.z1 if sign > 0 else self.z0
            end = wall - sign * 1.7
            # Posts: the inner one straight, the outer one bent out over the edge.
            for k, z in enumerate((wall - sign * 0.45, wall - sign * 1.4)):
                first = len(m.faces)
                geo.box(m, (x - 0.16, self.top + 0.45, z), (0.1, 0.9, 0.1), P.STEEL, LIT, skip=("bottom",))
                if k == 1:
                    parts._tilt(m, first, (x - 0.16, self.top, z), ((0.0, 1.0), -0.5))  # bent out over the drop
            # The attached rail run: yellow beam with black chevrons, from the wall to the tear.
            rail = Plane.facing((x - 0.09, 0.0, 0.0), (-1.0, 0.0))  # faces the deck (west)
            u_wall, u_end = -wall, -end  # facing west: u runs north -> south, so u = -z
            ua, ub = min(u_wall, u_end), max(u_wall, u_end)
            parts.stripes(m, rail, ua, self.top + 0.52, ub, self.top + 0.84, P.SIGN_YELLOW, P.INK, period=0.5, off=0.0)
            back = Plane.facing((x - 0.03, 0.0, 0.0), (1.0, 0.0))
            parts.panel(m, back, -ub, self.top + 0.52, -ua, self.top + 0.84, geo.scale(P.SIGN_YELLOW, 0.8), off=0.0)
            for yy, n in ((self.top + 0.84, (0, 1, 0)), (self.top + 0.52, (0, -1, 0))):
                geo.oriented(m, [(x - 0.09, yy, wall), (x - 0.03, yy, wall), (x - 0.03, yy, end), (x - 0.09, yy, end)],
                             n, geo.scale(P.SIGN_YELLOW, 0.7), LIT)
            # The torn end twists out over the drop and hangs down the facade.
            path = [(x - 0.06, self.top + 0.68, end), (x + 0.12, self.top + 0.6, end - sign * 0.35),
                    (x + 0.3, self.top + 0.15, end - sign * 0.62), (x + 0.36, self.top - 0.6, end - sign * 0.74),
                    (x + 0.33, self.top - 1.5, end - sign * 0.78), (x + 0.3, self.top - 2.2, end - sign * 0.8)]
            ups = [(0, 1, 0), (0.3, 0.95, 0), (0.75, 0.55, 0.1 * sign), (0.25, 0.1, 0.96 * sign), (0.05, 0.0, 1.0 * sign), (0.0, 0.0, 1.0 * sign)]
            for i in range(len(path) - 1):
                seg = [path[i], path[i + 1]]
                up = [geo._norm(u) for u in (ups[i], ups[i + 1])]
                quad = [tuple(seg[0][k] - up[0][k] * 0.16 for k in range(3)), tuple(seg[1][k] - up[1][k] * 0.16 for k in range(3)),
                        tuple(seg[1][k] + up[1][k] * 0.16 for k in range(3)), tuple(seg[0][k] + up[0][k] * 0.16 for k in range(3))]
                color = P.SIGN_YELLOW if i < 2 else geo.mix(P.SIGN_YELLOW, P.RUST, 0.35 * i / 4)
                m.poly(quad, color, LIT)
                m.poly(list(reversed(quad)), geo.scale(color, 0.75), LIT)
            # Bolts where the rail meets the wall.
            geo.box(m, (x - 0.06, self.top + 0.68, wall - sign * 0.06), (0.16, 0.4, 0.12), P.STEEL_DARK, LIT, skip=("bottom",))


def build_all(b):
    d = Deck(b)
    b.part("top", "Deck", "Deck Surface", d.surface)
    b.part("facade", "Facade", "Department Facades", d.facades)
    b.part("barrier", "Barrier", "Broken Barrier", d.barrier)
    return d
