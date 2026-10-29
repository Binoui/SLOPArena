"""Crash-Test Lab shell in Unity coordinates. SPEC is checked against the actual bake.

Run with --unity-shell to emit typed shell input, or without arguments for the strict check.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # tools/blender: the shared stagekit
from stagekit import arena as baked  # noqa: E402
from stagekit import shell  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/training/training.unity"
ARENA = REPO / "data/arenas/training.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "kill_y": -12.0,
    "solids": [
        {"name": "TestPlatform", "asset": "TrainingTestPlatform", "footprint": (-14, -13, 14, 13), "top": 0, "bottom": -12},
        {"name": "WestDeck", "asset": "TrainingWestDeck", "footprint": (-12.5, 7, -6.5, 12), "top": 1.5, "bottom": 0},
        {"name": "EastDeck", "asset": "TrainingEastDeck", "footprint": (6.5, 7, 12.5, 12), "top": 1.5, "bottom": 0},
        {"name": "Walkway", "asset": "TrainingWalkway", "footprint": (-17, -13, -14, -9), "top": 0, "bottom": -12},
        {"name": "DrillYard", "asset": "TrainingDrillYard", "footprint": (-31, -13, -17, 13), "top": 0, "bottom": -12},
        {"name": "StepDeck", "asset": "TrainingStepDeck", "footprint": (-30, 3, -22, 12), "top": 1.5, "bottom": 0},
        {"name": "StepBlock", "asset": "TrainingStepBlock", "footprint": (-30, 8, -26, 12), "top": 3, "bottom": 1.5},
        {"name": "Shelf", "asset": "TrainingShelf", "footprint": (-35, -9, -31, 1), "top": -1.5, "bottom": -2.1},
        {"name": "NearIsland", "asset": "TrainingNearIsland", "footprint": (18, 7, 22, 11), "top": 0, "bottom": -12, "gap": 4},
        {"name": "MidIsland", "asset": "TrainingMidIsland", "footprint": (22, -2, 26, 2), "top": 0, "bottom": -12, "gap": 8},
        {"name": "FarIsland", "asset": "TrainingFarIsland", "footprint": (27.5, -11, 31.5, -7), "top": 0, "bottom": -12, "gap": 13.5, "double_jump_exceptions": ["Manki", "Bashie", "Salamander"]},
    ],
    "spawns": [(0, -4, 0), (0, 4, 180), (-23.5, 7.5, 90), (24, 0, 270)],
    "spawn_body_half_height": 0.85,
}


def spawn_surface(x, z, spec=SPEC):
    return max(s["top"] for s in spec["solids"]
               if s["footprint"][0] <= x <= s["footprint"][2]
               and s["footprint"][1] <= z <= s["footprint"][3])


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


def _check_outward(name, tris, inside):
    for t in tris:
        n = _normal(t)
        c = [sum(p[i] for p in t) / 3 for i in range(3)]
        d = [c[i] - inside[i] for i in range(3)]
        if sum(n[i] * d[i] for i in range(3)) <= 0:
            raise AssertionError("%s triangle %s faces into the solid" % (name, t))


def collision(spec=SPEC):
    meshes = {}
    for solid in spec["solids"]:
        x0, z0, x1, z1 = solid["footprint"]
        prism = {"floor": [(x0,z0),(x1,z0),(x1,z1),(x0,z1)],
                 "top": solid["top"], "kill_y": solid["bottom"]}
        tris = _floor(prism)
        _check_outward(solid["name"], tris, ((x0+x1)/2, (solid["top"]+solid["bottom"])/2, (z0+z1)/2))
        meshes[solid["name"]] = tris
    return meshes


def bounds(spec=SPEC):
    return (min(s["footprint"][0] for s in spec["solids"]), max(s["footprint"][2] for s in spec["solids"]),
            min(s["footprint"][1] for s in spec["solids"]), max(s["footprint"][3] for s in spec["solids"]))


def spec_corridor(spec=SPEC):
    x0, x1, z0, z1 = bounds(spec)
    return {"minX": x0-SIDE_BLAST_MARGIN, "maxX": x1+SIDE_BLAST_MARGIN,
            "minZ": z0-SIDE_BLAST_MARGIN, "maxZ": z1+SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": max(s["top"] for s in spec["solids"])+TOP_BLAST_MARGIN}


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
        "solids": spec["solids"],
        "killY": spec["kill_y"],
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
        raise ValueError("Baked training.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


SHELL_ASSETS = {s["name"]: s["asset"] for s in SPEC["solids"]}


def unity_shell_json(spec=SPEC) -> dict:
    """Input for sloparena.stage.shell-apply: indexed meshes and spawn markers."""
    meshes = [shell.mesh(name, SHELL_ASSETS[name], *shell.indexed(tris)) for name, tris in collision(spec).items()]
    return shell.document("training", spec["kill_y"], meshes,
                          [(x, spawn_surface(x, z, spec) + spec["spawn_body_half_height"], z, yaw)
                           for x, z, yaw in spec["spawns"]])


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
