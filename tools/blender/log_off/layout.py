"""Log Off gameplay shell: the intended layout and the authoritative baked arena.

SPEC records the approved chopping-block stump: an irregular round top split by a
lethal radial crack from the north rim, and two bracket-fungus shelves on the east
and west bark just below the rim. The collision source scene
(Assets/Stages/log_off/log_off.unity) is built from `collision()` and baked into
data/arenas/log_off.arena, which Shared, the GameServer and client prediction load.
The art generator never trusts SPEC alone: `load()` reads the baked arena, checks
that its triangles, spawns and blast planes match SPEC and returns the authoritative
corridor. `load(spec_only=True)` exists only for previewing an unbaked shell.

Coordinates are Unity world space: +X east, +Y up, +Z north. Triangles are wound so
that (b - a) x (c - a) points out of the solid, the convention Unity and the baker use.

Usage (outside Blender):
    python3 layout.py [--spec-only]   layout summary as JSON
    python3 layout.py --unity-shell   collision mesh + markers for the source-scene rebuild
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # tools/blender: the shared stagekit
from stagekit import arena as baked  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/log_off/log_off.unity"
ARENA = REPO / "data/arenas/log_off.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "top": 0.0,
    "kill_y": -10.0,  # AuthoringAids/KillPlane sits on the lowest bark vertex
    # Stump rim: r(theta) = radius + sum(amp * sin(k * theta)), theta from +X towards +Z.
    # Odd sine terms keep the outline mirror-symmetric (x -> -x) and put the east and
    # west rim exactly at x = +-radius, where the shelves attach.
    "radius": 14.5,
    "harmonics": [(1, 0.3), (3, -0.2), (5, 0.12)],
    "rim_segments": 48,
    # Axe split: half-widths along the crack, from the rim mouth to the tip. Lethal to
    # the kill plane; 3 m wide at the running-jump crossing (z = 6).
    "split": [(2.0, None), (1.5, 6.0), (1.05, 1.0), (0.4, -1.0)],
    "split_tip": -1.4,
    # Bracket-fungus shelves: top below the rim, depth out from the bark, half-length
    # along it, superellipse exponent, thickness at the outer lip and at the bark.
    "shelves": {
        "East Shelf": {"side": 1, "top": -1.5, "depth": 4.0, "half_length": 4.5, "power": 2.2,
                        "lip": 0.5, "root": 2.8, "embed": 1.5, "segments": 16},
        "West Shelf": {"side": -1, "top": -1.5, "depth": 4.0, "half_length": 4.5, "power": 2.2,
                        "lip": 0.5, "root": 2.8, "embed": 1.5, "segments": 16},
    },
    # Spawn_01..04 (x, z); each faces the centre. The first two are diagonal opposites.
    "spawns": [(-7.0, 5.0), (7.0, -5.0), (-7.0, -5.0), (7.0, 5.0)],
    "spawn_body_half_height": 0.85,
}


# ---------------------------------------------------------------- outline

def rim_radius(theta, spec=SPEC):
    return spec["radius"] + sum(a * math.sin(k * theta) for k, a in spec["harmonics"])


def rim_point(theta, spec=SPEC):
    r = rim_radius(theta, spec)
    return (r * math.cos(theta), r * math.sin(theta))


def _rim_theta_at_x(x, spec=SPEC):
    """Angle near north (+Z) where the rim has this x (bisection on the upper arc)."""
    lo, hi = math.pi / 2 - 0.6, math.pi / 2 + 0.6
    f = lambda t: rim_point(t, spec)[0] - x  # decreasing in t on the north arc
    for _ in range(80):
        mid = (lo + hi) / 2
        if f(mid) > 0:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def outline(spec=SPEC):
    """CCW stump-top outline (map view, +x east, +z north) including the split notch."""
    n = spec["rim_segments"]
    mouth = spec["split"][0][0]
    t_east = _rim_theta_at_x(mouth, spec)   # east lip of the mouth (theta < pi/2)
    t_west = _rim_theta_at_x(-mouth, spec)  # west lip of the mouth (theta > pi/2)
    pts = []
    # Rim from the west lip, around the south, to the east lip (CCW = increasing theta).
    start, end = t_west, t_east + 2 * math.pi
    steps = [start + (end - start) * i / n for i in range(n + 1)]
    for t in steps:
        pts.append(rim_point(t, spec))
    # Down the east wall of the split to the tip, then up the west wall.
    inner = [(hw, z) for hw, z in spec["split"][1:]]
    for hw, z in inner:
        pts.append((hw, z))
    pts.append((0.0, spec["split_tip"]))
    for hw, z in reversed(inner):
        pts.append((-hw, z))
    # The final west-wall point connects back to the west lip (pts[0]).
    return _dedupe(pts)


def _dedupe(pts):
    out = []
    for p in pts:
        if not out or math.dist(out[-1], p) > 1e-6:
            out.append(p)
    if math.dist(out[0], out[-1]) < 1e-6:
        out.pop()
    return out


def bark_x(z, side, spec=SPEC):
    """x of the rim on the east (side 1) or west (-1) arc at this z."""
    lo, hi = (-0.9, 0.9) if side > 0 else (math.pi - 0.9, math.pi + 0.9)
    f = lambda t: rim_point(t, spec)[1] - z
    increasing = side > 0
    for _ in range(80):
        mid = (lo + hi) / 2
        if (f(mid) < 0) == increasing:
            lo = mid
        else:
            hi = mid
    return rim_point((lo + hi) / 2, spec)[0]


def shelf_outline(shelf, spec=SPEC):
    """CCW top outline of a fungus shelf: an outer superellipse lip from the bark and
    back, closed by an arc `embed` metres inside the bark (hidden under the stump top).
    Returns (points, depth_out_of_bark per point)."""
    s, L, D, p, n = shelf["side"], shelf["half_length"], shelf["depth"], shelf["power"], shelf["segments"]
    outer, inner = [], []
    for i in range(n + 1):
        u = -1.0 + 2.0 * i / n
        z = L * u
        d = D * (1.0 - abs(u) ** p) ** (1.0 / p)
        bx = bark_x(z, s, spec)
        outer.append(((bx + s * d, z), d))
        inner.append(((bx - s * shelf["embed"], z), -shelf["embed"]))
    ring = outer + list(reversed(inner))
    if s < 0:  # mirror the walk so the outline stays counter-clockwise
        ring = list(reversed(ring))
    pts = [q for q, _ in ring]
    depth = [d for _, d in ring]
    return pts, depth


# ---------------------------------------------------------------- triangulation

def polygon_area(pts):
    return sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
               for i in range(len(pts))) / 2


def _cross2(o, a, b):
    return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def _in_triangle(p, a, b, c):
    d1, d2, d3 = _cross2(a, b, p), _cross2(b, c, p), _cross2(c, a, p)
    return d1 > 1e-12 and d2 > 1e-12 and d3 > 1e-12


def ear_clip(pts):
    """Indices of a CCW simple polygon's triangles (each CCW in map view)."""
    if polygon_area(pts) <= 0:
        raise ValueError("outline must be counter-clockwise")
    idx = list(range(len(pts)))
    tris = []
    guard = 0
    while len(idx) > 3:
        guard += 1
        if guard > 100000:
            raise ValueError("ear clipping failed: polygon is not simple")
        best = None
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            a, b, c = pts[i0], pts[i1], pts[i2]
            if _cross2(a, b, c) <= 1e-12:
                continue
            if any(_in_triangle(pts[j], a, b, c) for j in idx if j not in (i0, i1, i2)):
                continue
            # Prefer the fattest ear for well-shaped triangles.
            la, lb, lc = math.dist(a, b), math.dist(b, c), math.dist(c, a)
            quality = _cross2(a, b, c) / max(1e-9, la * la + lb * lb + lc * lc)
            if best is None or quality > best[0]:
                best = (quality, k)
        if best is None:
            raise ValueError("ear clipping found no ear: polygon is not simple")
        k = best[1]
        tris.append((idx[k - 1], idx[k], idx[(k + 1) % len(idx)]))
        idx.pop(k)
    tris.append(tuple(idx))
    return tris


# ---------------------------------------------------------------- collision mesh

class _Solid:
    def __init__(self):
        self.verts, self.tris = [], []

    def v(self, p):
        self.verts.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.verts) - 1

    def tri(self, a, b, c):
        self.tris.append((a, b, c))

    def quad(self, a, b, c, d):
        self.tri(a, b, c)
        self.tri(a, c, d)


def _prism(solid, pts, top_y, bottom_y):
    """Closed vertical prism over a CCW outline. bottom_y: float or one value per point."""
    n = len(pts)
    bys = bottom_y if isinstance(bottom_y, list) else [bottom_y] * n
    top = [solid.v((x, top_y, z)) for x, z in pts]
    bot = [solid.v((x, bys[i], z)) for i, (x, z) in enumerate(pts)]
    tris = ear_clip(pts)
    for i, j, k in tris:
        # CCW in map view (x east, z north) seen from above is clockwise in Unity's
        # left-handed view, so (b-a)x(c-a) points down: reverse for the top face.
        solid.tri(top[i], top[k], top[j])
        solid.tri(bot[i], bot[j], bot[k])
    for i in range(n):
        j = (i + 1) % n
        # Outward wall for a CCW outline edge i -> j.
        solid.quad(top[i], top[j], bot[j], bot[i])


def collision(spec=SPEC):
    """The authoritative collision solids: {name: (vertices, triangles)}."""
    stump = _Solid()
    _prism(stump, outline(spec), spec["top"], spec["kill_y"])
    out = {"Split Stump": (stump.verts, stump.tris)}
    for name, shelf in spec["shelves"].items():
        pts, depth = shelf_outline(shelf, spec)
        D = shelf["depth"]
        bottoms = []
        for d in depth:
            t = min(1.0, max(0.0, d / D))  # 0 at the bark (or inside it), 1 at the outer lip
            bottoms.append(shelf["top"] - (shelf["root"] + (shelf["lip"] - shelf["root"]) * t))
        solid = _Solid()
        _prism(solid, pts, shelf["top"], bottoms)
        out[name] = (solid.verts, solid.tris)
    return out


def _normal(a, b, c):
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(x * x for x in n))
    return tuple(x / length for x in n) if length else (0.0, 0.0, 0.0), length / 2


def spawn_yaw(x, z):
    """Degrees, Unity convention (0 = +Z, 90 = +X): face the stage centre."""
    return math.degrees(math.atan2(-x, -z)) % 360.0


def spec_corridor(spec=SPEC):
    xs, zs = [], []
    for verts, _ in collision(spec).values():
        xs += [v[0] for v in verts]
        zs += [v[2] for v in verts]
    return {"minX": min(xs) - SIDE_BLAST_MARGIN, "maxX": max(xs) + SIDE_BLAST_MARGIN,
            "minZ": min(zs) - SIDE_BLAST_MARGIN, "maxZ": max(zs) + SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": spec["top"] + TOP_BLAST_MARGIN}


def check_spec(spec=SPEC):
    """Self-consistency of the spec's own solids (pure Python, no arena needed)."""
    problems = []
    rim = outline(spec)
    if polygon_area(rim) <= 0:
        problems.append("stump outline is not counter-clockwise")
    for name, (verts, tris) in collision(spec).items():
        # Closed and consistently wound: every edge used once in each direction.
        edges = {}
        for a, b, c in tris:
            for e in ((a, b), (b, c), (c, a)):
                key = (tuple(round(x, 6) for x in verts[e[0]]), tuple(round(x, 6) for x in verts[e[1]]))
                edges[key] = edges.get(key, 0) + 1
        open_edges = sum(1 for (p, q), k in edges.items() if edges.get((q, p), 0) != k)
        if open_edges:
            problems.append("%s is not a closed, consistently wound solid (%d unmatched edges)" % (name, open_edges))
        # Top faces point up.
        for a, b, c in tris:
            pa, pb, pc = verts[a], verts[b], verts[c]
            n, area = _normal(pa, pb, pc)
            if area < 1e-9:
                problems.append("%s has a degenerate triangle" % name)
                break
    for x, z in spec["spawns"]:
        if not _inside(rim, x, z):
            problems.append("spawn (%.1f, %.1f) is off the stump top" % (x, z))
    return problems


def _inside(poly, x, z):
    inside = False
    n = len(poly)
    for i in range(n):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % n]
        if (az > z) != (bz > z):
            t = (z - az) / (bz - az)
            if x < ax + t * (bx - ax):
                inside = not inside
    return inside


# ---------------------------------------------------------------- baked arena

def _tri_key(t, digits=3):
    pts = [tuple(round(t[i * 3 + k], digits) + 0.0 for k in range(3)) for i in range(3)]
    return tuple(sorted(pts))


def verify_arena(arena: dict, spec=SPEC) -> list:
    """Return mismatches between the baked arena and SPEC (empty when they agree)."""
    problems = []
    tol = 1e-3
    expected = {}
    for verts, tris in collision(spec).values():
        for a, b, c in tris:
            key = _tri_key(verts[a] + verts[b] + verts[c])
            expected[key] = expected.get(key, 0) + 1
    actual = {}
    for t in arena["triangles"]:
        key = _tri_key(t)
        actual[key] = actual.get(key, 0) + 1
    missing = sum(max(0, k - actual.get(t, 0)) for t, k in expected.items())
    extra = sum(max(0, k - expected.get(t, 0)) for t, k in actual.items())
    if missing or extra:
        problems.append("baked triangles differ from the spec solids (%d missing, %d unexpected)" % (missing, extra))
    c = spec_corridor(spec)
    for key, value in (("killHeight", c["killY"]), ("killTop", c["topY"]), ("killMinX", c["minX"]),
                       ("killMaxX", c["maxX"]), ("killMinZ", c["minZ"]), ("killMaxZ", c["maxZ"])):
        if abs(arena[key] - value) > tol:
            problems.append("%s %.3f differs from spec %.3f" % (key, arena[key], value))
    if len(arena["spawns"]) != len(spec["spawns"]):
        problems.append("baked arena has %d spawns, spec %d" % (len(arena["spawns"]), len(spec["spawns"])))
    for i, ((x, y, z, yaw), (sx, sz)) in enumerate(zip(arena["spawns"], spec["spawns"])):
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - (spec["top"] + spec["spawn_body_half_height"])) > tol:
            problems.append("Spawn_%02d at (%.2f, %.2f, %.2f) differs from spec" % (i + 1, x, y, z))
        d = (math.degrees(yaw) - spawn_yaw(sx, sz) + 180) % 360 - 180
        if abs(d) > 0.05:
            problems.append("Spawn_%02d yaw %.2f differs from spec %.2f" % (i + 1, math.degrees(yaw), spawn_yaw(sx, sz)))
    return problems


def load(spec_only: bool = False, spec=SPEC) -> dict:
    """Layout for the art generator. Strict mode requires the baked arena to match SPEC."""
    problems = check_spec(spec)
    if problems:
        raise ValueError("Log Off layout spec is inconsistent:\n  " + "\n  ".join(problems))
    shelves = {}
    for name, shelf in spec["shelves"].items():
        pts, depth = shelf_outline(shelf, spec)
        shelves[name] = {"outline": pts, "depth": depth, **shelf}
    data = {
        "spec": spec,
        "outline": outline(spec),
        "top": spec["top"],
        "killY": spec["kill_y"],
        "split": spec["split"],
        "splitTip": spec["split_tip"],
        "shelves": shelves,
        "spawns": [(x, z, spawn_yaw(x, z)) for x, z in spec["spawns"]],
        "specOnly": spec_only,
    }
    if spec_only:
        data["corridor"] = spec_corridor(spec)
        data["source"] = None
        data["sourceSha256"] = None
        data["arenaSha256"] = None
        return data
    arena = baked.read_arena(ARENA)
    problems = verify_arena(arena, spec)
    if problems:
        raise ValueError("Baked log_off.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


def unity_shell_json(spec=SPEC) -> dict:
    """Input for the collision-source rebuild (Unity eval): one closed mesh per solid."""
    solids = []
    for name, (verts, tris) in collision(spec).items():
        solids.append({"name": name.replace(" ", ""), "vertices": [list(v) for v in verts],
                       "triangles": [i for t in tris for i in t]})
    return {"top": spec["top"], "killY": spec["kill_y"], "solids": solids,
            "spawns": [{"name": "Spawn_%02d" % (i + 1),
                        "position": [x, spec["top"] + spec["spawn_body_half_height"], z],
                        "yaw": spawn_yaw(x, z)} for i, (x, z) in enumerate(spec["spawns"])]}


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
