"""After Hours gameplay shell: the intended layout and the authoritative baked arena.

SPEC records the approved broad club roof. The collision source scene
(Assets/Stages/after_hours/after_hours.unity) is built from it and baked into
data/arenas/after_hours.arena, which Shared, the GameServer and client prediction
load. The art generator never trusts SPEC alone: `load()` reads the baked arena,
checks that its walkable tops, walls, spawns and blast planes match SPEC, and
returns the authoritative corridor. `load(spec_only=True)` exists only for
previewing a proposed shell before it has been baked.

Coordinates are Unity world space: +X east, +Y up, +Z north.

Usage (outside Blender):  python3 layout.py [--spec-only] > layout.json
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # tools/blender: the shared stagekit
from stagekit import arena as baked  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/after_hours/after_hours.unity"
ARENA = REPO / "data/arenas/after_hours.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "roof_top": 0.0,
    "kill_y": -12.0,  # AuthoringAids/KillPlane sits on the lowest wall vertex
    # Broad roof, 30 x 24 with 3 m chamfered corners. CCW in map view (+x right, +z up).
    "roof": [(-12.0, -12.0), (12.0, -12.0), (15.0, -9.0), (15.0, 9.0),
             (12.0, 12.0), (-12.0, 12.0), (-15.0, 9.0), (-15.0, -9.0)],
    # Performance risers: (x0, z0, x1, z1, top). Bottoms rest on the roof.
    "risers": {
        "West Riser": (-13.5, -3.0, -7.5, 3.0, 1.5),
        "East Riser": (7.5, -3.0, 13.5, 3.0, 1.5),
    },
    # Spawn_01..04: (x, z, yaw degrees). First two are diagonal opposites.
    "spawns": [(-6.0, -5.0, 45.0), (6.0, 5.0, 225.0), (-6.0, 5.0, 135.0), (6.0, -5.0, 315.0)],
    "spawn_body_half_height": 0.85,
}


def spec_corridor(spec=SPEC):
    xs = [p[0] for p in spec["roof"]]
    zs = [p[1] for p in spec["roof"]]
    top = max([spec["roof_top"]] + [r[4] for r in spec["risers"].values()])
    return {"minX": min(xs) - SIDE_BLAST_MARGIN, "maxX": max(xs) + SIDE_BLAST_MARGIN,
            "minZ": min(zs) - SIDE_BLAST_MARGIN, "maxZ": max(zs) + SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": top + TOP_BLAST_MARGIN}


# ---------------------------------------------------------------- baked arena

def _tri_normal_area(t):
    a, b, c = t[0:3], t[3:6], t[6:9]
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    ln = math.sqrt(sum(x * x for x in n))
    return (tuple(x / ln for x in n) if ln else (0, 0, 0)), ln / 2


def _polygon_area(outline):
    return abs(sum(outline[i][0] * outline[(i + 1) % len(outline)][1] - outline[(i + 1) % len(outline)][0] * outline[i][1]
                   for i in range(len(outline)))) / 2


def _inside(outline, x, z, tol):
    for i in range(len(outline)):
        (ax, az), (bx, bz) = outline[i], outline[(i + 1) % len(outline)]
        ex, ez = bx - ax, bz - az
        L = math.hypot(ex, ez)
        if ((x - ax) * ez - (z - az) * ex) / L > tol:
            return False
    return True


def verify_arena(arena: dict, spec=SPEC) -> list:
    """Return mismatches between the baked arena and SPEC (empty when they agree)."""
    problems = []
    tol = 1e-3
    roof, top = spec["roof"], spec["roof_top"]
    tops = {}
    for t in arena["triangles"]:
        n, area = _tri_normal_area(t)
        if n[1] > 0.99:
            y = round(sum(t[1::3]) / 3, 3)
            tops.setdefault(y, []).append((t, area))
    roof_tris = tops.pop(round(top, 3), [])
    roof_area = sum(a for _, a in roof_tris)
    if abs(roof_area - _polygon_area(roof)) > 0.05:
        problems.append("roof top area %.2f differs from spec %.2f" % (roof_area, _polygon_area(roof)))
    for t, _ in roof_tris:
        for i in range(3):
            if not _inside(roof, t[i * 3], t[i * 3 + 2], tol):
                problems.append("roof top vertex (%.3f, %.3f) lies outside the spec outline" % (t[i * 3], t[i * 3 + 2]))
                break
    for name, (x0, z0, x1, z1, rtop) in spec["risers"].items():
        tris = [(t, a) for t, a in tops.get(round(rtop, 3), [])
                if all(x0 - tol <= t[i * 3] <= x1 + tol and z0 - tol <= t[i * 3 + 2] <= z1 + tol for i in range(3))]
        area = sum(a for _, a in tris)
        if abs(area - (x1 - x0) * (z1 - z0)) > 0.05:
            problems.append("%s top area %.2f differs from spec %.2f" % (name, area, (x1 - x0) * (z1 - z0)))
        for t, _ in tris:
            tops[round(rtop, 3)].remove((t, _))
    leftovers = sum(len(v) for v in tops.values())
    if leftovers:
        problems.append("baked arena has %d unexpected upward faces at %s" % (leftovers, sorted(k for k, v in tops.items() if v)))
    lowest = min(min(t[1::3]) for t in arena["triangles"])
    if abs(lowest - spec["kill_y"]) > tol:
        problems.append("lowest wall vertex %.3f is not the spec kill plane %.3f" % (lowest, spec["kill_y"]))
    c = spec_corridor(spec)
    for key, value in (("killHeight", c["killY"]), ("killTop", c["topY"]), ("killMinX", c["minX"]),
                       ("killMaxX", c["maxX"]), ("killMinZ", c["minZ"]), ("killMaxZ", c["maxZ"])):
        if abs(arena[key] - value) > tol:
            problems.append("%s %.3f differs from spec %.3f" % (key, arena[key], value))
    if len(arena["spawns"]) != len(spec["spawns"]):
        problems.append("baked arena has %d spawns, spec %d" % (len(arena["spawns"]), len(spec["spawns"])))
    for i, ((x, y, z, yaw), (sx, sz, syaw)) in enumerate(zip(arena["spawns"], spec["spawns"])):
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - (top + spec["spawn_body_half_height"])) > tol:
            problems.append("Spawn_%02d at (%.2f, %.2f, %.2f) differs from spec" % (i + 1, x, y, z))
        d = (math.degrees(yaw) - syaw + 180) % 360 - 180
        if abs(d) > 0.05:
            problems.append("Spawn_%02d yaw %.2f differs from spec %.2f" % (i + 1, math.degrees(yaw), syaw))
    return problems


def load(spec_only: bool = False, spec=SPEC) -> dict:
    """Layout for the art generator. Strict mode requires the baked arena to match SPEC."""
    data = {
        "spec": spec,
        "roof": [tuple(p) for p in spec["roof"]],
        "roofTop": spec["roof_top"],
        "risers": {k: tuple(v) for k, v in spec["risers"].items()},
        "spawns": [tuple(s) for s in spec["spawns"]],
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
        raise ValueError("Baked after_hours.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


def unity_shell_json(spec=SPEC) -> dict:
    """Input for the collision-source rebuild script (Unity eval)."""
    return {"roofTop": spec["roof_top"], "killY": spec["kill_y"], "roof": [list(p) for p in spec["roof"]],
            "risers": [{"name": k.replace(" ", ""), "min": [v[0], spec["roof_top"], v[1]], "max": [v[2], v[4], v[3]]}
                       for k, v in spec["risers"].items()],
            "spawns": [{"name": "Spawn_%02d" % (i + 1), "position": [x, spec["roof_top"] + spec["spawn_body_half_height"], z], "yaw": yaw}
                       for i, (x, z, yaw) in enumerate(spec["spawns"])]}


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
