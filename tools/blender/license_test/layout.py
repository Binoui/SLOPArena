"""Fighter's License gameplay shell: the intended layout and the authoritative baked arena.

SPEC records the approved tutorial deck (docs/design/stages/license_test.design.md): the
40 x 30 m roof of the Department of Fighting with one open east edge, walled north (the
Test Center's base), south (the safety fence) and west (the neighbour's party wall), plus
the jump course: a 1.5 m block, a 3 m block and the 5.5 m lookout tower. Every solid is a
closed box. The collision source scene (Assets/Stages/license_test/license_test.unity) is
built from `collision()` and baked into data/arenas/license_test.arena, which Shared and
the Tutorial load. The lesson heights and the knockout spot live in
`TutorialCurriculum.LicenseTest` (src/Shared/TutorialProgress.cs) and move with this spec.

The art generator never trusts SPEC alone: `load()` reads the baked arena, checks that its
triangles, spawns and blast planes match SPEC and returns the authoritative corridor.
`load(spec_only=True)` exists only for previewing a proposed shell before it is baked.

Coordinates are Unity world space: +X east (the open edge), +Y up, +Z north.
Triangles are wound so that (b - a) x (c - a) points out of the solid (Unity's
clockwise front faces).

Usage (outside Blender):
    python3 layout.py [--spec-only]   layout summary as JSON (strict: checks the bake)
    python3 layout.py --unity-shell   collision meshes + markers for the source-scene rebuild
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
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/license_test/license_test.unity"
ARENA = REPO / "data/arenas/license_test.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "top": 0.0,
    # The building block's base: a fall past it is a knockout (AuthoringAids/KillPlane).
    "kill_y": -11.0,
    # The deck (x0, z0, x1, z1); the east side (x1) is the open edge.
    "deck": (-20.0, -15.0, 20.0, 15.0),
    # Walls stand on the deck's three closed sides, 12 m high: above the tower top plus
    # the highest double jump (Bonk, 5.1 m). (x0, z0, x1, z1) footprints.
    "wall_top": 12.0,
    "walls": {
        "NorthWall": (-21.0, 15.0, 20.0, 16.0),
        "SouthFence": (-21.0, -16.0, 20.0, -15.0),
        "WestWall": (-21.0, -15.0, -20.0, 15.0),
    },
    # The jump course west to east in front of the north wall: a full jump reaches the
    # first block, a double jump the second, and the tower is a double jump from the second.
    "raised": {
        "Block15": {"footprint": (-14.0, 8.0, -10.0, 12.0), "top": 1.5},
        "Block30": {"footprint": (-8.0, 8.0, -4.0, 12.0), "top": 3.0},
        "LookoutTower": {"footprint": (-2.0, 9.0, 1.0, 12.0), "top": 5.5},
    },
    # Spawn_01..04 (x, z, yaw degrees; yaw 0 looks along +Z). Spawn_01 is the player's
    # start facing the course, Spawn_02 the dummy's place in the test ring; the others
    # keep the normal four-spawn contract.
    "spawns": [(-6.0, -10.0, 0.0), (7.0, -2.0, 270.0), (-14.0, -6.0, 90.0), (10.0, 8.0, 270.0)],
    "spawn_body_half_height": 0.85,
}


# ---------------------------------------------------------------- solids

def _box(x0, y0, z0, x1, y1, z1):
    """Closed axis-aligned box: 12 outward triangles."""
    v = {(i, j, k): (x1 if i else x0, y1 if j else y0, z1 if k else z0) for i in (0, 1) for j in (0, 1) for k in (0, 1)}
    quads = [  # each CCW seen from outside, so (b - a) x (c - a) points out
        [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)],  # top +Y
        [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)],  # bottom -Y
        [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)],  # east +X
        [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)],  # west -X
        [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)],  # north +Z
        [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)],  # south -Z
    ]
    tris = []
    for q in quads:
        a, b, c, d = (v[k] for k in q)
        tris += [(a, b, c), (a, c, d)]
    return tris


def _normal(t):
    a, b, c = t
    u = [b[i] - a[i] for i in range(3)]
    w = [c[i] - a[i] for i in range(3)]
    n = (u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0])
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def _check_outward(name, tris, inside):
    for t in tris:
        n = _normal(t)
        c = [sum(p[i] for p in t) / 3 for i in range(3)]
        if sum(n[i] * (c[i] - inside[i]) for i in range(3)) <= 0:
            raise AssertionError("%s triangle %s faces into the solid" % (name, t))


def solids(spec=SPEC):
    """Named closed boxes (x0, y0, z0, x1, y1, z1) in GameplayGeometry order."""
    x0, z0, x1, z1 = spec["deck"]
    out = {"DeckBuilding": (x0, spec["kill_y"], z0, x1, spec["top"], z1)}
    for name, (a0, b0, a1, b1) in spec["walls"].items():
        out[name] = (a0, spec["top"], b0, a1, spec["wall_top"], b1)
    for name, r in spec["raised"].items():
        a0, b0, a1, b1 = r["footprint"]
        out[name] = (a0, spec["top"], b0, a1, r["top"], b1)
    return out


def collision(spec=SPEC):
    """Named authoritative meshes as triangle lists [((x, y, z), (x, y, z), (x, y, z)), ...]."""
    meshes = {}
    for name, b in solids(spec).items():
        meshes[name] = _box(*b)
        _check_outward(name, meshes[name], ((b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2))
    return meshes


def bounds(spec=SPEC):
    """Map extent of all collision (x0, x1, z0, z1): the walls reach past the deck."""
    boxes = list(solids(spec).values())
    return (min(b[0] for b in boxes), max(b[3] for b in boxes), min(b[2] for b in boxes), max(b[5] for b in boxes))


def spec_corridor(spec=SPEC):
    x0, x1, z0, z1 = bounds(spec)
    highest = max(b[4] for b in solids(spec).values())
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
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - (spec["top"] + spec["spawn_body_half_height"])) > tol:
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
        "deck": tuple(spec["deck"]),
        "wallTop": spec["wall_top"],
        "walls": {name: tuple(f) for name, f in spec["walls"].items()},
        "raised": {name: dict(r) for name, r in spec["raised"].items()},
        "spawns": [tuple(s) for s in spec["spawns"]],
        "specOnly": spec_only,
    }
    if spec_only:
        data.update({"corridor": spec_corridor(spec), "source": None, "sourceSha256": None, "arena": None, "arenaSha256": None})
        return data
    arena = baked.read_arena(ARENA)
    problems = verify_arena(arena, spec)
    if problems:
        raise ValueError("Baked license_test.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


# GameplayGeometry child -> mesh asset in Assets/Stages/license_test (GUIDs kept on rebuild).
SHELL_ASSETS = {"DeckBuilding": "LicenseTestDeck", "NorthWall": "LicenseTestNorthWall", "SouthFence": "LicenseTestSouthFence",
                "WestWall": "LicenseTestWestWall", "Block15": "LicenseTestBlock15", "Block30": "LicenseTestBlock30",
                "LookoutTower": "LicenseTestTower"}


def unity_shell_json(spec=SPEC) -> dict:
    """Input for sloparena.stage.shell-apply: indexed meshes and spawn markers."""
    meshes = [shell.mesh(name, SHELL_ASSETS[name], *shell.indexed(tris)) for name, tris in collision(spec).items()]
    top = spec["top"] + spec["spawn_body_half_height"]
    return shell.document("license_test", spec["kill_y"], meshes,
                          [(x, top, z, yaw) for x, z, yaw in spec["spawns"]])


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
