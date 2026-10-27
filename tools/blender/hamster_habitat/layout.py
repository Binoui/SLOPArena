"""Hamster Habitat (layout C, Two Cages) shell, emitted for Unity and checked against the bake.

Unity coordinates: +X east, +Y up, +Z north. A desk cage (west, floor y 0) and a lower
shelf cage (east, floor y -3) sit on either side of an open gap. A low tube with a junction
hatch and a high tube from the house roof to the wheel deck cross it. Tubes are closed
annular solids: their inner wall faces the hollow, so fighters walk inside on the inner
bottom and on top of the outer wall.

Heights follow the roster (FightGuy single apex ~2.1, double ~3.4): the wheel deck is one
jump above the shelf floor, the house roof is a double jump from the food bowl, and the
junction step lets a double jump clear the low tube's top through the hatch.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from stagekit import arena as baked, shell

REPO = Path(__file__).resolve().parents[3]
STAGE = "hamster_habitat"
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/hamster_habitat/hamster_habitat.unity"
ARENA = REPO / "data/arenas/hamster_habitat.arena"
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

TUBE_SIDES = 16
TUBE_INNER, TUBE_WALL = 2.0, 0.2
# Flat inner floor: centre height above the surface the tube rests on.
TUBE_FLOOR = TUBE_INNER * math.cos(math.pi / TUBE_SIDES)
DESK, SHELF, DECK, ROOF = 0.0, -3.0, -1.0, 4.4
WHEEL_INNER, WHEEL_SIDES = 3.8, 24
HATCH = (0.0, 6.5)        # x, z of the low tube's junction hatch
HATCH_RADIUS = 1.6

# 2026-10-27: the footprint is 1.3x the first gray-box at the maintainer's request ("bigger,
# at least 30%"). Heights, tube bore and the wheel keep their jump-tied sizes.
SPEC = {
    "kill_y": -14.0,
    "desk": (-32.5, -10.4, -10.4, 10.4),
    "shelf": (10.4, 32.5, -10.4, 10.4),
    "house": (-29.9, -19.5, DESK, ROOF, -9.1, -1.3),
    "bowl": ((-26.65, 1.82), 2.3, DESK + 1.2),
    "deck": (16.9, 29.9, SHELF, DECK, -9.1, 3.25),
    "wheel": ((24.7, DECK + WHEEL_INNER * math.cos(math.pi / WHEEL_SIDES)), (-7.67, -5.07)),
    "wheel_post": (24.3, 25.1, DECK, DECK + 3.9, -8.27, -7.67),
    "tp_roll": ((28.6, SHELF + 1.9 * math.cos(math.pi / TUBE_SIDES)), (4.16, 9.88), 1.9),
    "low_tube": [(-14.3, DESK + TUBE_FLOOR, 6.5), (-10.4, DESK + TUBE_FLOOR, 6.5), (0.0, 0.46, 6.5),
                 (10.4, SHELF + TUBE_FLOOR, 6.5), (14.3, SHELF + TUBE_FLOOR, 6.5)],
    "junction_step": (-1.1, 1.1, -1.75, 0.1, 5.5, 7.5),
    "high_tube": [(-23.4, ROOF + TUBE_FLOOR, -5.2), (-19.5, 6.6, -5.2), (-10.4, 8.8, -3.9), (0.0, 9.4, -2.08),
                  (8.45, 7.2, -0.52), (14.95, 4.0, 0.39), (20.15, 1.6, 0.39), (25.35, DECK + TUBE_FLOOR, 0.39)],
    # (x, surface y, z): Spawn_01/02 are one per cage for duels.
    "spawns": [(-14.95, DESK, 0.65), (22.1, SHELF, 7.15), (-27.3, DESK, 7.15), (13.65, SHELF, -6.5)],
    "spawn_body_half_height": 0.85,
}


def _sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def _add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def _mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def _dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def _cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(a):
    n = math.sqrt(_dot(a, a)) or 1.0
    return (a[0] / n, a[1] / n, a[2] / n)


def _centre(points):
    return _mul(sum_points(points), 1.0 / len(points))


def sum_points(points):
    x = y = z = 0.0
    for p in points:
        x, y, z = x + p[0], y + p[1], z + p[2]
    return (x, y, z)


def _face(tris, points, outward):
    """Triangulate a convex polygon wound so its normal (b - a) x (c - a) faces `outward`."""
    n = _cross(_sub(points[1], points[0]), _sub(points[2], points[0]))
    if _dot(n, outward) < 0:
        points = list(reversed(points))
    for k in range(1, len(points) - 1):
        tris.append((points[0], points[k], points[k + 1]))


def prism(outline, y0, y1):
    """Closed vertical prism over a convex outline [(x, z)]."""
    tris = []
    cx = sum(p[0] for p in outline) / len(outline)
    cz = sum(p[1] for p in outline) / len(outline)
    _face(tris, [(x, y1, z) for x, z in outline], (0, 1, 0))
    _face(tris, [(x, y0, z) for x, z in outline], (0, -1, 0))
    for i, (ax, az) in enumerate(outline):
        bx, bz = outline[(i + 1) % len(outline)]
        _face(tris, [(ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az)],
              ((ax + bx) / 2 - cx, 0, (az + bz) / 2 - cz))
    return tris


def box(x0, x1, y0, y1, z0, z1):
    return prism([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], y0, y1)


def disc(centre, radius, sides=12):
    return [(centre[0] + radius * math.cos(2 * math.pi * k / sides),
             centre[1] + radius * math.sin(2 * math.pi * k / sides)) for k in range(sides)]


def spline(points, steps=8):
    """Catmull-Rom through the control points with clamped ends."""
    out = []
    for i in range(len(points) - 1):
        p0, p1, p2, p3 = (points[max(0, i - 1)], points[i], points[i + 1], points[min(len(points) - 1, i + 2)])
        for k in range(steps):
            t = k / steps
            out.append(tuple(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t * t
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t ** 3) for j in range(3)))
    out.append(tuple(points[-1]))
    return out


def rings(path, radius, sides=TUBE_SIDES):
    """Ring vertices around each path point. A flat facet sits at the bottom and the top:
    vertex k is at angle -90 deg + 180/sides + k * 360/sides around the path's up vector."""
    out = []
    for i, c in enumerate(path):
        t = _norm(_sub(path[min(i + 1, len(path) - 1)], path[max(i - 1, 0)]))
        up = _norm(_sub((0, 1, 0), _mul(t, t[1])))
        side = _cross(up, t)
        ring = []
        for k in range(sides):
            a = -math.pi / 2 + math.pi / sides + 2 * math.pi * k / sides
            ring.append(_add(c, _add(_mul(side, math.cos(a) * radius), _mul(up, math.sin(a) * radius))))
        out.append(ring)
    return out


def annulus(path, inner, wall, sides=TUBE_SIDES, window=None):
    """Closed hollow tube along `path`. `window(centre)` removes wall quads (both walls) whose
    centre it accepts; the cut edges are closed with liner faces so the solid stays closed."""
    tris = []
    ri, ro = rings(path, inner, sides), rings(path, inner + wall, sides)
    n = len(path)
    cut = set()
    if window:
        for i in range(n - 1):
            for k in range(sides):
                q = [ro[i][k], ro[i][(k + 1) % sides], ro[i + 1][(k + 1) % sides], ro[i + 1][k]]
                if window(_centre(q), _centre([path[i], path[i + 1]])):
                    cut.add((i, k))
    for i in range(n - 1):
        axis = _centre([path[i], path[i + 1]])
        for k in range(sides):
            if (i, k) in cut:
                continue
            k2 = (k + 1) % sides
            qi = [ri[i][k], ri[i][k2], ri[i + 1][k2], ri[i + 1][k]]
            qo = [ro[i][k], ro[i][k2], ro[i + 1][k2], ro[i + 1][k]]
            _face(tris, qi, _sub(axis, _centre(qi)))
            _face(tris, qo, _sub(_centre(qo), axis))
    for (i, k) in cut:
        k2 = (k + 1) % sides
        hole = _centre([ro[i][k], ro[i + 1][k2]])
        edges = [((i - 1, k), [ri[i][k], ri[i][k2], ro[i][k2], ro[i][k]]),
                 ((i + 1, k), [ri[i + 1][k], ri[i + 1][k2], ro[i + 1][k2], ro[i + 1][k]]),
                 ((i, (k - 1) % sides), [ri[i][k], ri[i + 1][k], ro[i + 1][k], ro[i][k]]),
                 ((i, k2), [ri[i][k2], ri[i + 1][k2], ro[i + 1][k2], ro[i][k2]])]
        for neighbour, edge in edges:
            if neighbour not in cut and 0 <= neighbour[0] < n - 1:
                _face(tris, edge, _sub(hole, _centre(edge)))
    for i, sign in ((0, -1), (n - 1, 1)):
        t = _norm(_sub(path[min(i + 1, n - 1)], path[max(i - 1, 0)]))
        for k in range(sides):
            k2 = (k + 1) % sides
            _face(tris, [ri[i][k], ri[i][k2], ro[i][k2], ro[i][k]], _mul(t, sign))
    return tris


def rest(path, *spans, ramp=3.0):
    """Settle the stretches of a tube path that lie on a floor. A span (edge x, side, centre y)
    pins every point beyond the edge (side -1: x <= edge, +1: x >= edge) and eases into it over
    `ramp` metres, because a hard kink folds the tube wall on the inside of the bend."""
    out = []
    for x, y, z in path:
        for edge, side, level in spans:
            t = min(1.0, max(0.0, ((x - edge) * side + ramp) / ramp))
            y += (level - y) * t * t * (3 - 2 * t)
        out.append((x, y, z))
    return out


def low_tube_window(quad_centre, axis):
    return (math.hypot(quad_centre[0] - HATCH[0], quad_centre[2] - HATCH[1]) < HATCH_RADIUS
            and quad_centre[1] > axis[1] + 0.5)


def straight(a, b, steps=1):
    return [tuple(a[j] + (b[j] - a[j]) * s / steps for j in range(3)) for s in range(steps + 1)]


def low_tube_path(spec=SPEC):
    return rest(spline(spec["low_tube"]), (-10.4, -1, DESK + TUBE_FLOOR), (10.4, 1, SHELF + TUBE_FLOOR))


def high_tube_path(spec=SPEC):
    return rest(spline(spec["high_tube"]), (-20.8, -1, ROOF + TUBE_FLOOR), (22.75, 1, DECK + TUBE_FLOOR))


def collision(spec=SPEC):
    k = spec["kill_y"]
    x0, x1, z0, z1 = spec["desk"]
    s0, s1, t0, t1 = spec["shelf"]
    (bx, bz), br, by = spec["bowl"]
    (wx, wy), (wz0, wz1) = spec["wheel"]
    (tx, ty), (tz0, tz1), tr = spec["tp_roll"]
    return {
        "DeskCage": box(x0, x1, k, DESK, z0, z1),
        "ShelfCage": box(s0, s1, k, SHELF, t0, t1),
        "House": box(*spec["house"]),
        "FoodBowl": prism(disc((bx, bz), br), DESK - 0.2, by),
        "WheelDeck": box(*spec["deck"]),
        "Wheel": annulus(straight((wx, wy, wz0), (wx, wy, wz1)), WHEEL_INNER, 0.2, WHEEL_SIDES),
        "WheelPost": box(*spec["wheel_post"]),
        # Thick cardboard: the see-through camera crosses only walls under 0.3 m (the tubes, the wheel).
        "TpRoll": annulus(straight((tx, ty, tz0), (tx, ty, tz1)), tr, 0.5),
        "LowTube": annulus(low_tube_path(spec), TUBE_INNER, TUBE_WALL, window=low_tube_window),
        "JunctionStep": box(*spec["junction_step"]),
        "HighTube": annulus(high_tube_path(spec), TUBE_INNER, TUBE_WALL),
    }


def spawn_yaw(x, z):
    return math.degrees(math.atan2(-x, -z)) % 360


def spec_corridor(spec=SPEC):
    tris = [t for ts in collision(spec).values() for t in ts]
    xs = [p[0] for t in tris for p in t]
    zs = [p[2] for t in tris for p in t]
    top = max(p[1] for t in tris for p in t)
    return {"minX": min(xs) - SIDE_BLAST_MARGIN, "maxX": max(xs) + SIDE_BLAST_MARGIN,
            "minZ": min(zs) - SIDE_BLAST_MARGIN, "maxZ": max(zs) + SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": top + TOP_BLAST_MARGIN}


def _same_triangles(actual, expected, tolerance=2e-3):
    """True when both lists hold the same wound triangles within `tolerance` (the bake stores
    float32, so exact rounding would flip at .0005 boundaries)."""
    if len(actual) != len(expected):
        return False
    cells = {}
    for t in expected:
        c = tuple(round(sum(p[j] for p in t) / 3, 1) for j in range(3))
        cells.setdefault(c, []).append(t)
    def close(a, b):
        return all(abs(a[j] - b[j]) <= tolerance for j in range(3))
    for t in actual:
        c = tuple(round(sum(p[j] for p in t) / 3, 1) for j in range(3))
        found = None
        for dx in (-0.1, 0, 0.1):
            for dy in (-0.1, 0, 0.1):
                for dz in (-0.1, 0, 0.1):
                    for e in cells.get((round(c[0] + dx, 1), round(c[1] + dy, 1), round(c[2] + dz, 1)), ()):
                        if any(all(close(t[i], e[(i + r) % 3]) for i in range(3)) for r in range(3)):
                            found = e
                            break
                    if found: break
                if found: break
            if found: break
        if not found:
            return False
        cells[tuple(round(sum(p[j] for p in found) / 3, 1) for j in range(3))].remove(found)
    return True


def verify_arena(arena, spec=SPEC):
    problems = []
    expected = [t for ts in collision(spec).values() for t in ts]
    actual = [(t[:3], t[3:6], t[6:9]) for t in arena["triangles"]]
    if not _same_triangles(actual, expected):
        problems.append("collision differs from layout.py (%d baked vs %d expected triangles)"
                        % (len(actual), len(expected)))
    c = spec_corridor(spec)
    for key, want in [("killHeight", c["killY"]), ("killMinX", c["minX"]), ("killMaxX", c["maxX"]),
                      ("killMinZ", c["minZ"]), ("killMaxZ", c["maxZ"])]:
        if abs(arena[key] - want) > 0.01:
            problems.append("%s differs: %s != %s" % (key, arena[key], want))
    if len(arena["spawns"]) != 4:
        problems.append("expected exactly four spawns")
    for i, ((x, y, z, yaw), (sx, sy, sz)) in enumerate(zip(arena["spawns"], spec["spawns"])):
        if max(abs(x - sx), abs(y - sy - spec["spawn_body_half_height"]), abs(z - sz)) > 0.001:
            problems.append("Spawn_%02d position differs" % (i + 1))
        if abs((math.degrees(yaw) - spawn_yaw(sx, sz) + 180) % 360 - 180) > 0.05:
            problems.append("Spawn_%02d does not face the centre" % (i + 1))
    return problems


def load(spec_only=False, spec=SPEC):
    data = {"spec": spec, "killY": spec["kill_y"], "specOnly": spec_only, "corridor": spec_corridor(spec),
            "source": None, "sourceSha256": None, "arena": None, "arenaSha256": None}
    if not spec_only:
        arena = baked.read_arena(ARENA)
        problems = verify_arena(arena, spec)
        if problems:
            raise ValueError("Baked hamster_habitat.arena does not match SPEC:\n  " + "\n  ".join(problems))
        data.update(corridor=baked.corridor(arena), source=str(SOURCE_SCENE.relative_to(REPO)),
                    sourceSha256=baked.sha256(SOURCE_SCENE), arena=str(ARENA.relative_to(REPO)),
                    arenaSha256=baked.sha256(ARENA))
    return data


def unity_shell_json(spec=SPEC):
    meshes = [shell.mesh(name, "HamsterHabitat" + name, *shell.indexed(tris))
              for name, tris in collision(spec).items()]
    return shell.document(STAGE, spec["kill_y"], meshes,
                          [(x, y + spec["spawn_body_half_height"], z, spawn_yaw(x, z))
                           for x, y, z in spec["spawns"]])


if __name__ == "__main__":
    json.dump(unity_shell_json() if "--unity-shell" in sys.argv else load("--spec-only" in sys.argv),
              sys.stdout, indent=1)
    print()
