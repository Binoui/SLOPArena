"""The warehouse around the cage: walls and murals, roof, flood rig, light beams and props.

Everything here stands outside the 40 x 40 x 20 room (the beams excepted: they are light,
not scenery). The hall walls are 7 m beyond the cage; the camera never leaves the room,
so the hall is only seen through the chain-link.
"""
from __future__ import annotations

import math
import random

import palette as P
import parts
import textures as T
from lighting import CourtBounce, Light, Rig
from stagekit import geo

HALL = 27.0
ROOF = 29.0
TILE = 3.0
FLOOD_Y = 21.05
LAMP_Y = 5.4  # sodium wall lamps sit inside the default camera band
FLOODS = [(x, z) for z in (-9.0, 9.0) for x in (-12.0, 0.0, 12.0)]
TRUSS_Z = (-9.0, 9.0)


def flood_aim(x, z):
    return (x * 0.55, 0.0, z * 0.6)


def rig(spec):
    half = spec["half"]
    sources = [Light((x, FLOOD_Y, z), P.FLOOD, 4.0, 14.0, aim=flood_aim(x, z), cone=82, soft=0.6)
               for x, z in FLOODS]
    sources.append(CourtBounce(half, (0.58, 0.64, 0.70), 1.3, 9.0))
    for x in (-10.0, 0.0, 10.0):  # mural wash lights at the hero wall's foot
        sources.append(Light((x, 0.5, HALL - 1.2), P.WARM, 3.4, 7.0, aim=(x, 11.0, HALL), cone=95, soft=0.6))
    for sx in (-1, 1):
        for z in (-14.0, 0.0, 14.0):
            sources.append(Light((sx * (HALL - 0.6), LAMP_Y, z), P.SODIUM, 1.5, 4.5))
    sources += [
        Light((-8.0, 2.4, -HALL + 1.6), P.MAGENTA, 1.4, 3.5),        # vending machine
        Light((4.5, 4.6, -HALL + 2.2), P.WARM, 1.1, 4.5),            # lounge lamp
        Light((0.0, 4.8, -HALL + 0.9), P.PINK, 1.2, 5.0),            # SLOP CLUB neon spill
        Light((-HALL + 1.0, 7.5, 6.0), P.CYAN_LIGHT, 1.1, 5.0),      # west accent
        Light((7.9, 5.0, half - 0.6), P.WARM, 1.3, 2.6, aim=(7.9, 2.8, half), cone=110, soft=0.6),  # combo board
    ]
    for sx in (-1, 1):
        sources.append(Light((sx * 23.0, 2.6, HALL - 3.6), P.CYAN, 0.5, 2.5))  # speaker rings
    return Rig((0.06, 0.066, 0.088), sources)


def build(b):
    rng = random.Random(20261017)
    hall = b.group("Hall", role="outside")
    glow = b.group("Glow", role="outside")
    half = b.spec["half"]
    _walls(b, hall, rng)
    _floor_and_roof(b, hall, rng)
    _murals(b, hall)
    _rig(b, hall)
    _props(b, hall, glow, rng)
    _beams(b)


def _concrete_uv(rng):
    name = rng.choice(("concrete_a", "concrete_b", "concrete_c"))
    u0, v0, u1, v1 = T.uv(name, inset=4)
    return (u1, v0, u0, v1) if rng.random() < 0.5 else (u0, v0, u1, v1)


def _walls(b, hall, rng):
    """Hall walls as 3 m concrete tiles with a 1 m light grid, facing the court."""
    white = (0.72, 0.72, 0.74)
    for side in parts.SIDES:
        u = -HALL
        while u < HALL - 1e-6:
            y = 0.0
            while y < ROOF - 1e-6:
                h = min(TILE, ROOF - y)
                parts.panel(hall, side.P(u, y, HALL), side.vec(TILE, 0, 0), (0, h, 0), 3, max(1, int(round(h))),
                            _concrete_uv(rng), lambda p, n: b.rig.shade(white, p, n), "NC_Night")
                y += h
            u += TILE


def _floor_and_roof(b, hall, rng):
    half = b.spec["half"]
    floor = (0.42, 0.42, 0.44)
    up = (0.0, 1.0, 0.0)
    # concrete floor ring between the cage and the hall walls
    x = -HALL
    while x < HALL - 1e-6:
        z = -HALL
        while z < HALL - 1e-6:
            if not (-half <= x and x + TILE <= half and -half <= z and z + TILE <= half):
                x0, z0 = x, z
                x1, z1 = x + TILE, z + TILE
                # trim tiles that straddle the court edge to the outside part only
                pieces = []
                if x0 < -half or x1 > half or z0 < -half or z1 > half:
                    pieces.append((x0, z0, x1, z1))
                for a0, c0, a1, c1 in pieces:
                    parts.panel(hall, (a0, -0.01, c0), (a1 - a0, 0, 0), (0, 0, c1 - c0), 3, 3, _concrete_uv(rng),
                                lambda p, n: b.rig.shade(floor, p, up), "NC_Night")
            z += TILE
        x += TILE
    # dark roof deck and its beams
    down = (0.0, -1.0, 0.0)
    roof = (0.10, 0.105, 0.12)
    for i in range(18):
        for j in range(18):
            x0, z0 = -HALL + i * TILE, -HALL + j * TILE
            geo.oriented(hall, [(x0, ROOF, z0), (x0 + TILE, ROOF, z0), (x0 + TILE, ROOF, z0 + TILE), (x0, ROOF, z0 + TILE)],
                         down, b.rig.shade(roof, (x0 + 1.5, ROOF, z0 + 1.5), down), "NC_Night")
    for x in range(-24, 25, 6):
        geo.box(hall, (x, ROOF - 0.5, 0.0), (0.4, 1.0, 2 * HALL), b.rig.shade(P.STEEL_DARK, (x, ROOF - 1, 0), down),
                "NC_Night", skip=("top",))


def _murals(b, hall):
    white = (1.0, 1.0, 1.0)
    north, east, south, west = parts.SIDES

    def mural(side, u0, u1, y0, y1, cell, depth=0.015, uv=None):
        nu = max(1, int(round(u1 - u0)))
        nv = max(1, int(round(y1 - y0)))
        parts.panel(hall, side.P(u0, y0, HALL - depth), side.vec(u1 - u0, 0, 0), (0, y1 - y0, 0), nu, nv,
                    uv or T.uv(cell), lambda p, n: b.rig.shade(white, p, n), "NC_Night")

    mural(north, -21.0, 21.0, 0.7, 5.95, "band")            # SLOPBALL lettering in the default frame
    mural(north, -11.0, 11.0, 6.4, 17.4, "mural", 0.02)      # the mascot, seen when the camera looks level
    mural(east, -14.0, 4.0, 2.0, 6.5, "side")                # clear of the container (z -15.5..-6.5)
    mural(west, -20.0, -6.0, 2.2, 5.7, "side")
    mural(west, -5.0, 1.0, 0.0, 6.5, "door", 0.03)            # roll-up door (west)


def _rig(b, hall):
    """Two Warren trusses over the cage net carry six flood housings."""
    steel = P.STEEL_DARK
    for z in TRUSS_Z:
        for dz in (-0.45, 0.45):
            for y in (21.6, 23.3):
                c = b.rig.shade(steel, (0.0, y, z), (0.0, -1.0, 0.0))
                parts.beam(hall, (-HALL, y, z + dz), (HALL, y, z + dz), 0.14, c, "NC_Night")
            k = 0
            x = -HALL
            while x < HALL - 1e-6:
                c = b.rig.shade(steel, (x, 22.4, z), (0.0, -1.0, 0.0))
                y0, y1 = (21.6, 23.3) if k % 2 == 0 else (23.3, 21.6)
                parts.beam(hall, (x, y0, z + dz), (x + 1.8, y1, z + dz), 0.08, c, "NC_Night")
                x += 1.8
                k += 1
    panels = b.group("Flood Panels", role="outside")
    u0, v0, u1, v1 = T.uv("flood", inset=6)
    for x, z in FLOODS:
        geo.box(hall, (x, FLOOD_Y + 0.33, z), (3.0, 0.36, 1.6), b.rig.shade(steel, (x, FLOOD_Y + 0.4, z), (0, -1, 0)),
                "NC_Night", skip=("bottom",))
        parts.beam(hall, (x - 1.2, FLOOD_Y + 0.5, z), (x - 1.2, 21.6, z), 0.08, steel, "NC_Night")
        parts.beam(hall, (x + 1.2, FLOOD_Y + 0.5, z), (x + 1.2, 21.6, z), 0.08, steel, "NC_Night")
        with geo.state(panels, alpha=1.0):
            # LED face looking down at the court (two 1.35 m cells)
            for a in (-1.45, 0.0):
                pts = [(x + a, FLOOD_Y + 0.15, z - 0.75), (x + a + 1.45, FLOOD_Y + 0.15, z - 0.75),
                       (x + a + 1.45, FLOOD_Y + 0.15, z + 0.75), (x + a, FLOOD_Y + 0.15, z + 0.75)]
                geo.oriented(panels, pts, (0.0, -1.0, 0.0), P.FLOOD, "NC_Night",
                             uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])


def _beams(b):
    """Additive light shafts: four soft crossed fins per flood, fading towards the court."""
    beams = b.group("Flood Beams", role="beam")
    rows, cols = 7, (-1.0, -0.5, -0.2, 0.0, 0.2, 0.5, 1.0)
    for x, z in FLOODS:
        top = (x, FLOOD_Y, z)
        aim = flood_aim(x, z)
        bottom = (top[0] + (aim[0] - top[0]) * 0.97, 0.8, top[2] + (aim[2] - top[2]) * 0.97)
        axis = geo._norm(tuple(bottom[i] - top[i] for i in range(3)))
        ref = (0.0, 0.0, 1.0) if abs(axis[2]) < 0.9 else (1.0, 0.0, 0.0)
        s0 = geo._norm(geo._cross(axis, ref))
        s1 = geo._cross(axis, s0)
        for f in range(4):
            a = f * math.pi / 4
            s = tuple(s0[i] * math.cos(a) + s1[i] * math.sin(a) for i in range(3))
            grid = []
            for r in range(rows + 1):
                t = r / rows
                centre = tuple(top[i] + (bottom[i] - top[i]) * t for i in range(3))
                width = 0.9 + (3.4 - 0.9) * t
                # the camera never looks up, so the shafts must carry down to the court
                fade = (1.0 - t) ** 0.85 * min(1.0, t * 6.0 + 0.25)
                grid.append([(tuple(centre[i] + s[i] * width * c for i in range(3)), fade * (1.0 - c * c) ** 2)
                             for c in cols])
            for r in range(rows):
                for c in range(len(cols) - 1):
                    q = [grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]]
                    beams.poly([p for p, _ in q], [(*P.FLOOD, 0.32 * w) for _, w in q], "NC_Beam")


def _props(b, hall, glow, rng):
    shade = b.rig.shade
    up = (0.0, 1.0, 0.0)
    # speaker stacks flanking the hero wall, cones facing the court
    for sx in (-1, 1):
        x, z = sx * 23.0, HALL - 2.4
        for k in range(3):
            y = 0.72 + k * 1.44
            geo.box(hall, (x, y, z), (2.2, 1.4, 1.5), shade((0.05, 0.05, 0.055), (x, y, z - 0.8), (0, 0, -1)), "NC_Night")
            u0, v0, u1, v1 = T.uv("speaker", inset=4)
            with geo.state(glow, alpha=1.0, anim=(51.2 + 0.1 * k, 0.17 * k + (0.5 if sx > 0 else 0.0))):
                for cx in (-0.52, 0.52):
                    pts = [(x + cx - 0.5, y - 0.5, z - 0.76), (x + cx + 0.5, y - 0.5, z - 0.76),
                           (x + cx + 0.5, y + 0.5, z - 0.76), (x + cx - 0.5, y + 0.5, z - 0.76)]
                    geo.oriented(glow, pts, (0.0, 0.0, -1.0), (0.8, 0.8, 0.85), "NC_Night",
                                 uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
    # shipping container on the east side, doors towards the court
    cx, cz = 24.4, -11.0
    red = P.CONTAINER
    geo.box(hall, (cx, 1.3, cz), (2.5, 2.6, 9.0), shade(red, (cx - 1.3, 1.5, cz), (-1, 0, 0)), "NC_Night")
    for k in range(19):
        zz = cz - 4.3 + k * 0.48
        geo.box(hall, (cx - 1.28, 1.3, zz), (0.06, 2.45, 0.1), shade(geo.scale(red, 0.82), (cx - 1.3, 1.3, zz), (-1, 0, 0)),
                "NC_Night")
    # lounge corner behind the player: couch, table, rug, vending machine, lamp
    lx, lz = 4.5, -HALL + 1.4
    couch = P.COUCH
    geo.box(hall, (lx, 0.25, lz + 0.2), (4.2, 0.5, 1.5), shade(couch, (lx, 0.6, lz + 1.0), up), "NC_Night")
    geo.box(hall, (lx, 0.85, lz - 0.38), (4.2, 0.9, 0.4), shade(couch, (lx, 0.9, lz), (0, 0, 1)), "NC_Night")
    for ax in (-2.2, 2.2):
        geo.box(hall, (lx + ax, 0.55, lz + 0.2), (0.3, 0.6, 1.5), shade(couch, (lx + ax, 0.8, lz + 0.8), up), "NC_Night")
    geo.box(hall, (lx, 0.22, lz + 2.4), (2.2, 0.44, 1.0), shade((0.25, 0.17, 0.11), (lx, 0.5, lz + 2.4), up), "NC_Night")
    geo.oriented(hall, [(lx - 3.2, 0.004, lz - 0.6), (lx + 3.2, 0.004, lz - 0.6), (lx + 3.2, 0.004, lz + 3.6),
                        (lx - 3.2, 0.004, lz + 3.6)], up, shade((0.45, 0.12, 0.20), (lx, 0.0, lz + 1.5), up), "NC_Night")
    geo.box(hall, (lx + 3.0, 1.0, lz), (0.08, 2.0, 0.08), shade(P.STEEL_DARK, (lx + 3, 1, lz), up), "NC_Night")
    with geo.state(glow, alpha=1.0):
        geo.ellipsoid(glow, (lx + 3.0, 2.15, lz), (0.32, 0.26, 0.32), P.WARM, "NC_Night", rings=4, segments=8)
    # a tall drinks cooler: its lit front rises above the dasher boards in the camera's view
    vx, vz = -8.0, -HALL + 0.6
    geo.box(hall, (vx, 1.35, vz), (1.3, 2.7, 0.9), shade((0.55, 0.06, 0.10), (vx, 1.3, vz + 0.5), (0, 0, 1)), "NC_Night")
    u0, v0, u1, v1 = T.uv("vending", inset=4)
    with geo.state(glow, alpha=1.0):
        pts = [(vx - 0.55, 0.2, vz + 0.46), (vx + 0.55, 0.2, vz + 0.46), (vx + 0.55, 2.6, vz + 0.46), (vx - 0.55, 2.6, vz + 0.46)]
        geo.oriented(glow, pts, (0.0, 0.0, 1.0), (1.0, 1.0, 1.0), "NC_Night", uvs=[(u1, v0), (u0, v0), (u0, v1), (u1, v1)])
    # SLOP CLUB neon on the south wall, on a dark backer
    u0, v0, u1, v1 = T.uv("neon", inset=4)
    with geo.state(glow, alpha=1.0, anim=(-1.0, 0.3)):
        pts = [(4.0, 2.9, -HALL + 0.06), (-4.0, 2.9, -HALL + 0.06), (-4.0, 6.9, -HALL + 0.06), (4.0, 6.9, -HALL + 0.06)]
        geo.oriented(glow, pts, (0.0, 0.0, 1.0), (1.0, 1.0, 1.0), "NC_Night", uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
    # EXIT over a door on the west wall, and the door itself
    u0, v0, u1, v1 = T.uv("exit", inset=4)
    with geo.state(glow, alpha=1.0):
        pts = [(-HALL + 0.06, 3.3, 6.0), (-HALL + 0.06, 3.3, 7.6), (-HALL + 0.06, 3.9, 7.6), (-HALL + 0.06, 3.9, 6.0)]
        geo.oriented(glow, pts, (1.0, 0.0, 0.0), (1.0, 1.0, 1.0), "NC_Night", uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
    geo.box(hall, (-HALL + 0.05, 1.2, 6.8), (0.1, 2.4, 1.3), shade((0.16, 0.2, 0.24), (-HALL + 0.5, 1.2, 6.8), (1, 0, 0)),
            "NC_Night")
    # sodium wall lamps on the side walls
    for sx in (-1, 1):
        for z in (-14.0, 0.0, 14.0):
            x = sx * (HALL - 0.25)
            geo.box(hall, (x, LAMP_Y + 0.25, z), (0.5, 0.3, 0.7), shade(P.STEEL_DARK, (x - sx, LAMP_Y, z), (-sx, 0, 0)), "NC_Night")
            with geo.state(glow, alpha=1.0):
                geo.box(glow, (x - sx * 0.05, LAMP_Y + 0.05, z), (0.36, 0.1, 0.56), P.SODIUM, "NC_Night")
    # mural wash fixtures at the hero wall's foot
    for x in (-10.0, 0.0, 10.0):
        geo.box(hall, (x, 0.2, HALL - 1.2), (0.7, 0.4, 0.5), shade(P.STEEL_DARK, (x, 0.5, HALL - 1.6), (0, 0, -1)), "NC_Night")
        with geo.state(glow, alpha=1.0):
            geo.box(glow, (x, 0.42, HALL - 1.1), (0.56, 0.05, 0.3), P.WARM, "NC_Night")
    # drums and tyres in the corners
    spots = [(-24.5, 24.5), (-25.0, 22.5), (24.8, 21.6), (-24.6, -24.0), (-22.8, -24.8), (24.6, -24.6), (25.0, 4.0)]
    for i, (x, z) in enumerate(spots):
        col = (P.DRUM_BLUE, P.DRUM_ORANGE, P.STEEL)[i % 3]
        geo.lathe(hall, (x, 0.0, z), [(0.34, 0.0), (0.36, 0.45), (0.34, 0.9)], 12,
                  shade(col, (x, 0.5, z), geo._norm((-x, 0.0, -z))), "NC_Night")
    for i, (x, z) in enumerate([(22.5, 24.6), (-22.0, -23.0)]):
        for k in range(3):
            geo.lathe(hall, (x, 0.3 * k, z), [(0.42, 0.0), (0.48, 0.15), (0.42, 0.3)], 14,
                      shade(P.RUBBER, (x, 0.3 * k, z), up), "NC_Night", cap_top=k == 2)
    # string lights across the lounge
    bulbs = []
    for a, bb in (((-HALL + 0.3, 7.4, -HALL + 0.3), (-4.6, 7.8, -HALL + 0.3)),
                  ((4.6, 7.8, -HALL + 0.3), (HALL - 0.3, 7.4, -HALL + 0.3)),
                  ((-HALL + 0.3, 7.4, -HALL + 0.3), (-HALL + 0.3, 7.6, -14.0))):
        pts = geo.catenary(a, bb, 1.2, 26)
        for p, q in zip(pts, pts[1:]):
            parts.ribbon(hall, p, q, 0.025, (0.0, 0.0, 1.0) if abs(p[2] - q[2]) < 1e-3 else (1.0, 0.0, 0.0),
                         (0.02, 0.02, 0.02), "NC_Night")
        bulbs += pts[1:-1]
    with geo.state(glow, alpha=1.0):
        for i, p in enumerate(bulbs):
            geo.ellipsoid(glow, (p[0], p[1] - 0.12, p[2]), (0.08, 0.11, 0.08), P.BULB, "NC_Night", rings=3, segments=5)
