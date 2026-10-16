"""Slop Pit gameplay shell: the intended layout and the authoritative baked arena.

SPEC records the approved underground fight ring: a 32 x 32 ringside floor with
chamfered corners whose walls drop straight to the KO line, and a raised 16 x 16
ring in its centre that every roster fighter reaches with one full jump. All four
spawns start in the ring. The collision source scene
(Assets/Stages/slop_pit/slop_pit.unity) is built from `collision()` and baked into
data/arenas/slop_pit.arena, which Shared, the GameServer and client prediction load.
The art generator never trusts SPEC alone: `load()` reads the baked arena, checks that
its triangles, spawns and blast planes match SPEC and returns the authoritative
corridor. `load(spec_only=True)` exists only for previewing an unbaked shell.

Coordinates are Unity world space: +X east, +Y up, +Z north. Triangles are wound so
that (b - a) x (c - a) points out of the solid (Unity's clockwise front faces).

Usage (outside Blender):
    python3 layout.py [--spec-only]   layout summary as JSON
    python3 layout.py --unity-shell   collision meshes + markers for the source-scene rebuild
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # tools/blender: the shared stagekit
from stagekit import arena as baked  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/slop_pit/slop_pit.unity"
ARENA = REPO / "data/arenas/slop_pit.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "top": 0.0,
    "kill_y": -11.0,  # AuthoringAids/KillPlane sits on the lowest wall vertex (unchanged)
    # Ringside floor: 32 x 32 with 5 m chamfered corners. CCW in map view (+x right, +z up).
    "floor": [(-11.0, -16.0), (11.0, -16.0), (16.0, -11.0), (16.0, 11.0),
              (11.0, 16.0), (-11.0, 16.0), (-16.0, 11.0), (-16.0, -11.0)],
    # The ring: (x0, z0, x1, z1) footprint resting on the floor; a full jump for everyone.
    "ring": {"footprint": (-8.0, -8.0, 8.0, 8.0), "top": 1.2},
    # Spawn_01..04 (x, z, yaw degrees; yaw 0 looks along +Z), all in the ring corners
    # facing its centre. The first two are diagonal opposites (1v1 and Training).
    "spawns": [(-5.0, -5.0, 45.0), (5.0, 5.0, 225.0), (-5.0, 5.0, 135.0), (5.0, -5.0, 315.0)],
    "spawn_body_half_height": 0.85,
}


def spawn_surface(x, z, spec=SPEC):
    """Walkable height under a spawn: the ring top inside its footprint, else the floor."""
    x0, z0, x1, z1 = spec["ring"]["footprint"]
    return spec["ring"]["top"] if x0 <= x <= x1 and z0 <= z <= z1 else spec["top"]


def _normal(t):
    a, b, c = t
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def _floor(spec):
    """Closed prism: fan-triangulated top and bottom, outward walls down to the KO line."""
    top, bottom = spec["top"], spec["kill_y"]
    ring = spec["floor"]
    tris = []
    for k in range(1, len(ring) - 1):
        p, q, r = ring[0], ring[k], ring[k + 1]  # CCW in map view
        tris.append(((p[0], top, p[1]), (r[0], top, r[1]), (q[0], top, q[1])))
        tris.append(((p[0], bottom, p[1]), (q[0], bottom, q[1]), (r[0], bottom, r[1])))
    for i in range(len(ring)):
        (ax, az), (bx, bz) = ring[i], ring[(i + 1) % len(ring)]
        a_top, b_top = (ax, top, az), (bx, top, bz)
        a_low, b_low = (ax, bottom, az), (bx, bottom, bz)
        tris.append((a_top, b_top, b_low))
        tris.append((a_top, b_low, a_low))
    return tris


def _raised(footprint, top, base):
    """Open-bottomed box resting on the floor: top and four outward walls."""
    x0, z0, x1, z1 = footprint
    ring = [(x1, z0), (x1, z1), (x0, z1), (x0, z0)]  # CCW
    tris = [((x0, top, z0), (x0, top, z1), (x1, top, z1)), ((x0, top, z0), (x1, top, z1), (x1, top, z0))]
    for i in range(4):
        (ax, az), (bx, bz) = ring[i], ring[(i + 1) % 4]
        tris.append(((ax, top, az), (bx, top, bz), (bx, base, bz)))
        tris.append(((ax, top, az), (bx, base, bz), (ax, base, az)))
    return tris


def _check_outward(name, tris, inside):
    for t in tris:
        n = _normal(t)
        c = [sum(p[i] for p in t) / 3 for i in range(3)]
        d = [c[i] - inside[i] for i in range(3)]
        if sum(n[i] * d[i] for i in range(3)) <= 0:
            raise AssertionError("%s triangle %s faces into the solid" % (name, t))


def collision(spec=SPEC):
    """Named authoritative meshes as triangle lists [((x, y, z), (x, y, z), (x, y, z)), ...]."""
    meshes = {"RingsideFloor": _floor(spec)}
    # Both solids are convex, so every face must point away from an interior point.
    _check_outward("RingsideFloor", meshes["RingsideFloor"], (0.0, (spec["top"] + spec["kill_y"]) / 2, 0.0))
    r = spec["ring"]
    meshes["Ring"] = _raised(r["footprint"], r["top"], spec["top"])
    x0, z0, x1, z1 = r["footprint"]
    _check_outward("Ring", meshes["Ring"], ((x0 + x1) / 2, (r["top"] + spec["top"]) / 2, (z0 + z1) / 2))
    return meshes


def bounds(spec=SPEC):
    xs = [p[0] for p in spec["floor"]]
    zs = [p[1] for p in spec["floor"]]
    return (min(xs), max(xs), min(zs), max(zs))


def spec_corridor(spec=SPEC):
    x0, x1, z0, z1 = bounds(spec)
    highest = max(spec["top"], spec["ring"]["top"])
    return {"minX": x0 - SIDE_BLAST_MARGIN, "maxX": x1 + SIDE_BLAST_MARGIN,
            "minZ": z0 - SIDE_BLAST_MARGIN, "maxZ": z1 + SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": highest + TOP_BLAST_MARGIN}


# ---------------------------------------------------------------- baked arena

def _canonical(t, digits=3):
    """Rotate a triangle to start at its smallest vertex (winding preserved) and round it."""
    pts = [tuple(round(v, digits) + 0.0 for v in p) for p in t]
    k = min(range(3), key=lambda i: pts[i])
    return tuple(pts[(k + i) % 3] for i in range(3))


def verify_arena(arena: dict, spec=SPEC) -> list:
    """Return mismatches between the baked arena and SPEC (empty when they agree)."""
    problems = []
    tol = 1e-3
    expected = sorted(_canonical(t) for tris in collision(spec).values() for t in tris)
    actual = sorted(_canonical((t[0:3], t[3:6], t[6:9])) for t in arena["triangles"])
    if len(expected) != len(actual):
        problems.append("baked arena has %d collision triangles, spec %d" % (len(actual), len(expected)))
    else:
        worst = max(max(abs(a - b) for pa, pb in zip(ta, tb) for a, b in zip(pa, pb))
                    for ta, tb in zip(expected, actual))
        if worst > 2e-3:
            problems.append("baked collision differs from spec by up to %.4f m" % worst)
    c = spec_corridor(spec)
    for key, value in (("killHeight", c["killY"]), ("killTop", c["topY"]), ("killMinX", c["minX"]),
                       ("killMaxX", c["maxX"]), ("killMinZ", c["minZ"]), ("killMaxZ", c["maxZ"])):
        if abs(arena[key] - value) > tol:
            problems.append("%s %.3f differs from spec %.3f" % (key, arena[key], value))
    if len(arena["spawns"]) != len(spec["spawns"]):
        problems.append("baked arena has %d spawns, spec %d" % (len(arena["spawns"]), len(spec["spawns"])))
    for i, ((x, y, z, yaw), (sx, sz, syaw)) in enumerate(zip(arena["spawns"], spec["spawns"])):
        want_y = spawn_surface(sx, sz, spec) + spec["spawn_body_half_height"]
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - want_y) > tol:
            problems.append("Spawn_%02d at (%.2f, %.2f, %.2f) differs from spec" % (i + 1, x, y, z))
        d = (math.degrees(yaw) - syaw + 180) % 360 - 180
        if abs(d) > 0.05:
            problems.append("Spawn_%02d yaw %.2f differs from spec %.2f" % (i + 1, math.degrees(yaw), syaw))
    return problems


def load(spec_only: bool = False, spec=SPEC) -> dict:
    """Layout for the art generator. Strict mode requires the baked arena to match SPEC."""
    data = {
        "spec": spec,
        "top": spec["top"],
        "killY": spec["kill_y"],
        "floor": [tuple(p) for p in spec["floor"]],
        "ring": dict(spec["ring"]),
        "spawns": [tuple(s) for s in spec["spawns"]],
        "specOnly": spec_only,
    }
    if spec_only:
        data["corridor"] = spec_corridor(spec)
        data["source"] = None
        data["sourceSha256"] = None
        data["arena"] = None
        data["arenaSha256"] = None
        return data
    arena = baked.read_arena(ARENA)
    problems = verify_arena(arena, spec)
    if problems:
        raise ValueError("Baked slop_pit.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


def unity_shell_json(spec=SPEC) -> dict:
    """Input for the collision-source rebuild (Unity eval): indexed meshes and markers."""
    meshes = []
    for name, tris in collision(spec).items():
        index = {}
        vertices, triangles = [], []
        for t in tris:
            for p in t:
                key = tuple(round(v, 6) for v in p)
                if key not in index:
                    index[key] = len(vertices)
                    vertices.append(list(key))
                triangles.append(index[key])
        meshes.append({"name": name, "vertices": vertices, "triangles": triangles})
    return {"killY": spec["kill_y"], "meshes": meshes,
            "spawns": [{"name": "Spawn_%02d" % (i + 1),
                        "position": [x, spawn_surface(x, z, spec) + spec["spawn_body_half_height"], z], "yaw": yaw}
                       for i, (x, z, yaw) in enumerate(spec["spawns"])]}


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
