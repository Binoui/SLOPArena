"""The three closed sides of the deck, dressed inside their 1 m collision walls.

North: the Test Center's base, a glass front with the entrance, the department fascia and
the jump course's road signs. West: the neighbour's brick party wall, with the parking
ticket booth, the closed vehicle exit and an equipment cage of cones recessed into the
wall's thickness, where no fighter or camera can reach. South: a tall barred safety fence
with billboards, see-through to the city. Everything stays inside each wall's collision
box, at most WALL_PROUD in front of its face and never above its top.
"""
from __future__ import annotations

import math
import random

from stagekit import geo
import palette as P
import parts
from parts import BRICK, BRICK_TILE, CONCRETE, CONCRETE_TILE, GLASS, LIT, Plane
import textures as T

WALL_PROUD = 0.06


def face_with_openings(m, plane, u0, u1, v0, v1, openings, color, mat, tile):
    """Tile the face rectangle into panels around rectangular openings (u0, v0, u1, v1)."""
    us = sorted({u0, u1, *[o[0] for o in openings], *[o[2] for o in openings]})
    vs = sorted({v0, v1, *[o[1] for o in openings], *[o[3] for o in openings]})
    for a, b in zip(us, us[1:]):
        for c, d in zip(vs, vs[1:]):
            cu, cv = (a + b) / 2, (c + d) / 2
            if any(o[0] <= cu <= o[2] and o[1] <= cv <= o[3] for o in openings):
                continue
            parts.tiled_panel(m, plane, a, c, b, d, color, mat, tile)


def recess(m, plane, u0, v0, u1, v1, depth, side_color, back_color=None, back_cell=None, floor_color=None, mat=LIT):
    """The sides, soffit, floor and back of an opening cut `depth` into the wall."""
    back = Plane(tuple(plane.o[k] - plane.n[k] * depth for k in range(3)), plane.r, plane.n)
    if back_cell:
        parts.panel(m, back, u0, v0, u1, v1, back_color or (1, 1, 1), mat=mat, uvs=T.uv(back_cell))
    else:
        parts.panel(m, back, u0, v0, u1, v1, back_color or geo.scale(side_color, 0.6), mat=mat)
    for a, b, inward in (((u0, v0), (u0, v1), (1, 0)), ((u1, v1), (u1, v0), (-1, 0)), ((u0, v1), (u1, v1), (0, -1)),
                         ((u1, v0), (u0, v0), (0, 1))):
        pts = [plane.at(a[0], a[1]), plane.at(b[0], b[1]), plane.at(b[0], b[1], -depth), plane.at(a[0], a[1], -depth)]
        normal = (plane.r[0] * inward[0], inward[1], plane.r[2] * inward[0])
        col = floor_color if inward == (0, 1) and floor_color else side_color
        geo.oriented(m, pts, normal, col if inward[1] >= 0 else geo.scale(side_color, 0.55), mat)


class Walls:
    def __init__(self, build):
        self.b = build
        self.data = build.data
        self.top = build.data["top"]
        self.wall_top = build.data["wallTop"]
        self.rng = random.Random(build.seed + 11)

    # ------------------------------------------------------------ north: the Test Center's base
    def north(self, m):
        H = self.wall_top
        z = self.data["walls"]["NorthWall"][1]
        f = Plane.facing((0.0, 0.0, z), (0.0, -1.0))  # faces the deck; u = x
        glass = (3.0, 0.35, 17.4, 3.7)
        doors = (9.2, 0.0, 11.8, 2.75)
        face_with_openings(m, f, -20.0, 20.0, 0.0, H, [glass], P.CONCRETE, CONCRETE, CONCRETE_TILE)
        parts.panel(m, f, -20.0, 0.0, glass[0], 0.36, geo.scale(P.CONCRETE, 0.78), off=0.012)
        parts.panel(m, f, glass[2], 0.0, 20.0, 0.36, geo.scale(P.CONCRETE, 0.78), off=0.012)
        # Glass front: storefront glazing in a 1.6 m mullion grid, recessed 0.25 m, with the doors.
        recess(m, f, *glass, 0.25, P.STEEL, back_color=(1, 1, 1), back_cell="glass", mat=GLASS)
        back = Plane(tuple(f.o[k] - f.n[k] * 0.25 for k in range(3)), f.r, f.n)
        u = glass[0]
        while u <= glass[2] + 1e-6:
            parts.panel(m, back, u - 0.05, glass[1], u + 0.05, glass[3], P.MULLION, off=0.02)
            u += 1.6
        parts.panel(m, back, glass[0], 2.95, glass[2], 3.05, P.MULLION, off=0.02)
        parts.panel(m, back, doors[0], doors[1] + 0.35, doors[2], doors[3], (1, 1, 1), off=0.03, uvs=T.uv("door_glass"))
        # Fascia, then the office floors above.
        parts.plate(m, f, "fascia", (glass[0] + glass[2]) / 2, 4.45, glass[2] - glass[0] + 0.6, off=0.015, frame=P.STEEL_DARK)
        parts.window_grid(m, f, 3.0, 17.4, 5.8, 11.4, 2.4, 2.8, "window_office", (1, 1, 1), off=0.012, sill=0.55, head=0.45,
                          frame=P.MULLION, skip=lambda i, j: j == 1 and i in (3, 4))
        parts.plate(m, f, "examiner_glass", 12.6, 9.9, 4.4, off=0.016, height=1.9, frame=P.STEEL_DARK)
        parts.plate(m, f, "sign_examiner", 12.6, 8.55, 1.6, off=0.018)
        # The pier at the edge: STOP over EDGE, facing the deck.
        parts.plate(m, f, "sign_stop", 18.7, 3.0, 1.3, off=0.02)
        parts.plate(m, f, "sign_edge", 18.7, 1.45, 1.3, off=0.02)
        # The course side: road signs over each obstacle, the height warning and high slot windows.
        parts.plate(m, f, "sign_jump", -12.0, 2.75, 1.9, off=0.02, frame=P.STEEL_DARK)
        parts.plate(m, f, "sign_double", -6.0, 4.3, 1.9, off=0.02, frame=P.STEEL_DARK)
        parts.plate(m, f, "sign_fastfall", -0.5, 6.95, 1.9, off=0.02, frame=P.STEEL_DARK)
        parts.plate(m, f, "sign_height", -17.0, 6.6, 3.6, off=0.02)
        # The license itself, painted big above the course where climbers see it.
        parts.plate(m, f, "big_sign", -8.6, 9.75, 20.0, off=0.02)
        parts.plate(m, f, "sign_dept", 1.75, 2.0, 1.5, off=0.016, frame=P.STEEL_DARK)
        self._coping(m, f, -20.0, 20.0, H)
        # The wall's open end at the edge, and its top.
        end = Plane.facing((20.0, 0.0, z + 0.5), (1.0, 0.0))
        parts.tiled_panel(m, end, -0.5, 0.0, 0.5, H, P.CONCRETE, CONCRETE, CONCRETE_TILE)
        self._top(m, -21.0, z, 20.0, z + 1.0, H)

    @staticmethod
    def _top(m, x0, z0, x1, z1, H):
        """A wall's top: weathered concrete, seen when the camera rises over the wall from the tower."""
        pts = [(x0, H, z0), (x1, H, z0), (x1, H, z1), (x0, H, z1)]
        uvs = [(x / CONCRETE_TILE, z / CONCRETE_TILE) for x, _, z in pts]
        geo.oriented(m, pts, (0, 1, 0), geo.scale(P.CONCRETE, 0.78), CONCRETE, uvs)

    def _coping(self, m, f, u0, u1, H):
        parts.panel(m, f, u0, H - 0.22, u1, H, P.CONCRETE_WARM, off=0.025)
        parts.panel(m, f, u0, H - 0.26, u1, H - 0.22, geo.scale(P.CONCRETE_DARK, 0.8), off=0.02)

    # ------------------------------------------------------------ west: the party wall
    def west(self, m):
        H = self.wall_top
        x = self.data["walls"]["WestWall"][2]
        f = Plane.facing((x, 0.0, 0.0), (1.0, 0.0))  # faces the deck; u = z
        booth = (-14.4, 0.0, -11.6, 2.7)
        shutter = (-8.0, 0.0, -2.0, 3.8)
        cage = (0.6, 0.0, 4.6, 2.5)
        door = (12.6, 0.0, 13.8, 2.25)
        face_with_openings(m, f, -15.0, 15.0, 0.0, H, [booth, shutter, cage, door], P.BRICK, BRICK, BRICK_TILE)
        # Plinth between the openings.
        for a, b in ((-15.0, booth[0]), (booth[2], shutter[0]), (shutter[2], cage[0]), (cage[2], door[0]), (door[2], 15.0)):
            parts.panel(m, f, a, 0.0, b, 0.4, geo.scale(P.CONCRETE, 0.8), off=0.012)

        # Parking ticket booth: the examiner behind glass, the TICKETS board, a pay machine and
        # the raised boom of the exit barrier.
        recess(m, f, *booth, 0.8, P.BOOTH, back_cell="booth_window", floor_color=geo.scale(P.CONCRETE, 0.7))
        parts.panel(m, f, booth[0] - 0.12, booth[1], booth[0], booth[3] + 0.12, P.SIGN_YELLOW, off=0.02)
        parts.panel(m, f, booth[2], booth[1], booth[2] + 0.12, booth[3] + 0.12, P.SIGN_YELLOW, off=0.02)
        parts.panel(m, f, booth[0] - 0.12, booth[3], booth[2] + 0.12, booth[3] + 0.12, P.SIGN_YELLOW, off=0.02)
        parts.plate(m, f, "sign_tickets", (booth[0] + booth[2]) / 2, 3.35, 2.7, off=0.02, frame=P.STEEL_DARK)
        parts.plate(m, f, "ticket_machine", -10.85, 0.95, 0.62, off=0.04, frame=P.STEEL_DARK)
        parts.plate(m, f, "sign_pay", -10.85, 1.95, 0.9, off=0.03)
        parts.panel(m, f, -9.95, 0.0, -9.45, 1.05, P.SIGN_YELLOW, off=0.035)
        parts.stripes(m, f, -9.95, 0.0, -9.45, 0.3, P.SIGN_YELLOW, P.INK, period=0.24, off=0.04)
        parts.stripes(m, f, -9.77, 1.05, -9.63, 4.4, P.STRIPE_WHITE, P.SIGN_RED, period=0.5, off=0.045, slant=0.0)

        # The vehicle exit, shuttered: steel frame, hazard jambs and a no-entry disc.
        recess(m, f, *shutter, 0.25, P.STEEL_DARK, back_cell="shutter", floor_color=geo.scale(P.CONCRETE, 0.7))
        for a in (shutter[0] - 0.3, shutter[2]):
            parts.stripes(m, f, a, 0.0, a + 0.3, shutter[3], P.SIGN_YELLOW, P.INK, period=0.45, off=0.02)
        parts.panel(m, f, shutter[0] - 0.3, shutter[3], shutter[2] + 0.3, shutter[3] + 0.3, P.STEEL_DARK, off=0.02)
        parts.plate(m, f, "sign_noentry", (shutter[0] + shutter[2]) / 2, shutter[3] + 0.85, 0.7, off=0.025)

        # Equipment cage: cones, a tyre and a sign behind steel mesh, inside the wall.
        depth = 0.85
        recess(m, f, *cage, depth, geo.scale(P.CONCRETE, 0.62), back_color=geo.scale(P.CONCRETE, 0.5),
               floor_color=geo.scale(P.CONCRETE, 0.55))
        self._cage_contents(m, f, cage, depth)
        u = cage[0]
        while u <= cage[2] + 1e-6:
            parts.panel(m, f, u - 0.012, cage[1], u + 0.012, cage[3], P.STEEL, off=0.03, depth=0.02)
            u += 0.16
        v = cage[1] + 0.16
        while v < cage[3]:
            parts.panel(m, f, cage[0], v - 0.012, cage[2], v + 0.012, P.STEEL, off=0.034, depth=0.02)
            v += 0.16
        for a, b, c, d in ((cage[0] - 0.08, 0, cage[0], cage[3] + 0.08), (cage[2], 0, cage[2] + 0.08, cage[3] + 0.08),
                           (cage[0] - 0.08, cage[3], cage[2] + 0.08, cage[3] + 0.08)):
            parts.panel(m, f, a, b, c, d, P.STEEL_DARK, off=0.04)
        parts.plate(m, f, "sign_equipment", (cage[0] + cage[2]) / 2, cage[3] + 0.45, 2.4, off=0.03)

        # Services and the stair door in the alley behind the course.
        parts.plate(m, f, "fire_hose", 6.3, 1.5, 0.6, off=0.035, frame=P.STEEL_DARK)
        parts.plate(m, f, "panel_box", 7.5, 1.5, 0.6, off=0.035)
        recess(m, f, *door, 0.15, P.CONCRETE_DARK, back_cell="door_steel")
        parts.plate(m, f, "sign_exit", (door[0] + door[2]) / 2, door[3] + 0.38, 0.75, off=0.03)
        parts.plate(m, f, "sign_stairs", 14.45, 1.6, 0.8, off=0.03)
        # The faded ghost sign on the upper storeys, and two downpipes.
        gz0, gy0, gz1, gy1 = T.GHOST_RECT
        parts.plate(m, f, "ghost", (gz0 + gz1) / 2, (gy0 + gy1) / 2, gz1 - gz0, off=0.006, height=gy1 - gy0)
        for u in (-0.9, 10.4):
            geo.cylinder_between(m, (x + 0.0, 0.45, u), (x + 0.0, H - 0.3, u), 0.05, 8, P.STEEL_DARK, LIT)
            for v in (2.0, 4.5, 7.0, 9.5):
                parts.panel(m, f, u - 0.09, v, u + 0.09, v + 0.06, P.STEEL, off=0.058)
        self._coping(m, f, -15.0, 15.0, H)
        self._top(m, x - 1.0, -15.0, x, 15.0, H)

    def _cage_contents(self, m, f, cage, depth):
        rng = self.rng
        floor_y = cage[1]
        # Stacks of nested cones along the back, a few loose ones tipped over in front.
        u = cage[0] + 0.35
        while u < cage[2] - 0.3:
            n = rng.randint(2, 5)
            base = f.at(u, floor_y, -depth + 0.3)
            for k in range(n):
                parts.cone(m, (base[0], base[1] + k * 0.09, base[2]), height=0.7, radius=0.2)
            u += rng.uniform(0.48, 0.62)
        for u, tilt in ((cage[0] + 0.9, 1.35), (cage[0] + 2.6, -1.3)):
            base = f.at(u, floor_y, -depth + 0.62)
            parts.cone(m, (base[0], base[1] + 0.2, base[2]), height=0.7, radius=0.2, tilt=((f.n[0] or 1e-6, f.n[2] + 0.3), tilt))
        # A spare tyre leaning on the side wall.
        c = f.at(cage[2] - 0.45, floor_y + 0.36, -depth + 0.3)
        geo.lathe(m, c, [(0.2, -0.12), (0.35, -0.12), (0.36, 0.0), (0.35, 0.12), (0.2, 0.12), (0.19, 0.0), (0.2, -0.12)], 14,
                  (0.1, 0.1, 0.11), LIT, cap_top=False)

    # ------------------------------------------------------------ south: the safety fence
    def south(self, m):
        H = self.wall_top
        z = self.data["walls"]["SouthFence"][3]
        f = Plane.facing((0.0, 0.0, z), (0.0, 1.0))  # faces the deck; u = -x
        curb_top = 0.45
        # Concrete curb along the base, flush with the collision face.
        geo.box_minmax(m, (-20.0, 0.0, z - 0.6), (20.0, curb_top, z), P.CONCRETE, LIT, skip=("bottom",))
        posts = [-20.0 + 4.0 * k for k in range(11)]
        posts[0], posts[-1] = -19.89, 19.89
        for x in posts:
            # I-beam posts: two flanges and a web, inside the fence's collision.
            mid_y, tall = (curb_top + H) / 2, H - curb_top
            for zc in (z - 0.035, z - 0.225):
                geo.box(m, (x, mid_y, zc), (0.22, tall, 0.03), P.FENCE, LIT, skip=("bottom",))
            geo.box(m, (x, mid_y, z - 0.13), (0.035, tall, 0.16), geo.scale(P.FENCE, 0.8), LIT, skip=("bottom",))
        # Flat bars every 20 cm with a spiked top; three rails across.
        x = -19.7
        bars = 0
        while x < 19.75:
            if all(abs(x - p) > 0.16 for p in posts):
                geo.box_minmax(m, (x - 0.025, curb_top, z - 0.05), (x + 0.025, H - 0.3, z - 0.02), P.FENCE, LIT,
                               skip=("bottom", "top"))
                apex = (x, H - 0.001, z - 0.035)
                quad = [(x - 0.025, H - 0.3, z - 0.02), (x + 0.025, H - 0.3, z - 0.02), (x + 0.025, H - 0.3, z - 0.05),
                        (x - 0.025, H - 0.3, z - 0.05)]
                for i in range(4):
                    a, b = quad[i], quad[(i + 1) % 4]
                    mid = ((a[0] + b[0]) / 2 - x, 0.2, (a[2] + b[2]) / 2 - (z - 0.035))
                    geo.oriented(m, [a, b, apex], mid, P.FENCE, LIT)
                bars += 1
            x += 0.2
        for v in (curb_top + 0.1, 5.8, H - 0.55):
            geo.box_minmax(m, (-19.9, v, z - 0.06), (19.9, v + 0.09, z - 0.015), geo.scale(P.FENCE, 1.1), LIT)
        # Billboards facing the deck, and the lesson signs below them.
        for name, xc in (("bb_psa", -13.0), ("bb_cola", 1.0), ("bb_motors", 13.0)):
            u = -xc
            parts.panel(m, f, u - 4.15, 3.25, u + 4.15, 7.55, P.STEEL_DARK, off=-0.005)
            parts.plate(m, f, name, u, 5.4, 8.0, off=0.0)
            for leg in (-2.6, 2.6):
                parts.panel(m, f, u + leg - 0.06, curb_top, u + leg + 0.06, 3.25, P.STEEL_DARK, off=-0.01)
        for name, xc in (("sign_shield", 6.4), ("sign_grab", 8.6), ("sign_edge", 18.3)):
            parts.plate(m, f, name, -xc, 1.9, 1.45, off=0.0, frame=P.STEEL_DARK)
        # The fence's open end at the edge.
        end = Plane.facing((20.0, 0.0, z - 0.5), (1.0, 0.0))
        parts.panel(m, end, -0.5, 0.0, 0.5, curb_top, P.CONCRETE, off=0.0)
        return bars


def build_all(b):
    w = Walls(b)
    b.part("wall", "Test Center Base", "NorthWall", w.north)
    b.part("wall", "Party Wall", "WestWall", w.west)
    b.part("wall", "Safety Fence", "SouthFence", w.south)
    return w
