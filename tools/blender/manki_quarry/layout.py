"""Kaboom Quarry gameplay shell: Manki's Character Course and its authoritative baked arena.

SPEC records the locked course (docs/design/stages/manki_quarry.design.md): a flooded quarry
pit whose water is the kill plane, a clockwise loop of route pieces round it and
dynamite crates (course targets) on islands off the route. Every solid is a closed box that
stands in the water (base y = -0.2), so the lowest gameplay vertex is the KillPlane marker and
a fall into the water is a fall out. The collision source scene
(Assets/Stages/manki_quarry/manki_quarry.unity) is built from `collision()` and `course()` and
baked into data/arenas/manki_quarry.arena. Spacing is calibrated with the cooked Manki kit in
tests/Shared.Tests/MankiQuarryRouteTests.cs; move a piece there and here together.

The art generator never trusts SPEC alone: `load()` reads the baked arena, checks that its
triangles, spawn and blast planes match SPEC and returns the authoritative corridor.
`load(spec_only=True)` exists only for previewing a proposed shell before it is baked.

Coordinates are Unity world space: +X east, +Y up, +Z north. Boxes are (x0, z0, x1, z1, top).

Usage (outside Blender):
    python3 layout.py [--spec-only]   layout summary as JSON (strict: checks the bake)
    python3 layout.py --unity-shell   collision meshes, spawn and course markers for shell-apply
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
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/manki_quarry/manki_quarry.unity"
ARENA = REPO / "data/arenas/manki_quarry.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    # Every solid's base and the kill plane, just under the visual water (build.py, y = -0.05).
    # Not 1 m under the route: ArenaShippingTests reads a floor exactly KillHeight + 1 as an
    # unbaked heightmap.
    "water_y": -0.2,
    "spawn_body_half_height": 0.85,
    # Route pieces in loop order. Section 1 Blast Range: dock, haul road, West Landing.
    # Section 2 Pontoon Crossing. Section 3 Blast Face: North Bench, Cliff Base, the 5.5 m
    # face up to the Upper Bench, and the ordinary benches round its west side. Section 4
    # Conveyor Run: crusher roof, conveyor gantry, hopper, dump truck, gravel pile.
    "route": {
        "LoadingDock": (-12.0, -40.0, 16.0, -28.0, 1.0),
        "HaulRoad": (-38.0, -36.5, -12.0, -31.5, 1.0),
        "WestLanding": (-46.0, -38.0, -38.0, -27.0, 1.0),
        "Pontoon1": (-43.5, -25.0, -40.5, -22.0, 1.0),
        "Pontoon2": (-36.5, -15.5, -33.5, -12.5, 1.8),
        "Pontoon3": (-42.5, -9.5, -39.5, -6.5, 2.6),
        "Pontoon4": (-45.5, -2.5, -42.5, 0.5, 1.6),
        "NorthBench": (-46.0, 3.0, -18.0, 11.0, 1.0),
        "CliffBase": (-18.0, 6.0, -4.0, 20.0, 1.0),
        "UpperBench": (-16.0, 20.0, 2.0, 32.0, 6.5),
        "Bench1": (-23.25, 13.25, -19.75, 16.75, 2.5),
        "Bench2": (-26.5, 20.5, -23.5, 23.5, 4.6),
        "Bench3": (-21.5, 27.5, -18.5, 30.5, 5.5),
        "CrusherRoof": (5.0, 16.0, 11.0, 22.0, 5.4),
        "ConveyorGantry": (14.0, 6.0, 18.0, 12.0, 4.2),
        "Hopper": (11.5, -6.0, 14.5, 0.0, 3.2),
        "DumpTruck": (18.0, -16.0, 22.0, -12.0, 2.2),
        "GravelPile": (13.25, -23.75, 16.75, -20.25, 1.6),
    },
    # Target islands and the stepping stones that make far ones reachable by jumping.
    "islands": {
        "HopIslet": (-19.0, -26.5, -15.0, -22.5, 1.0),
        "SeaStack": (-26.5, -46.5, -23.5, -43.5, 3.2),
        "TwinIslet": (-34.0, -24.0, -28.0, -20.0, 1.0),
        "SouthStone": (-37.5, -44.0, -34.5, -41.0, 1.0),
        "FarBuoy": (-37.75, -52.25, -34.25, -48.75, 1.0),
        "LowRaft": (-31.5, -12.5, -28.5, -9.5, 0.4),
        "TrioBlock": (-51.5, -18.5, -46.5, -13.5, 1.0),
        "HighStack": (-34.25, -1.25, -31.75, 1.25, 4.0),
        "NorthPillar": (-8.5, 35.5, -5.5, 38.5, 4.5),
        "BaseIslet": (-6.5, -0.5, -1.5, 3.5, 1.0),
        "EastRock": (4.5, 33.5, 7.5, 36.5, 5.5),
        "LowIslet": (6.5, -14.5, 9.5, -11.5, 0.6),
        "EastStone": (22.75, 2.75, 25.25, 5.25, 2.6),
        "FarStack": (28.5, 0.5, 31.5, 3.5, 3.0),
        "TwinRaft": (21.5, -25.5, 26.5, -22.5, 0.6),
    },
    # The crane's rock in the middle of the pit (the crane itself is presentation only).
    "landmark": {"LastPillar": (-15.0, -12.0, -7.0, -4.0, 3.0)},
    # Spawn_01 (x, z, yaw degrees; yaw 0 looks along +Z) on the loading dock.
    "start": (-6.0, -34.0, 0.0),
    # Crates in route order: (id, island, x, z). Each floats 1.1 m above its island.
    "target_height": 1.1,
    "target_radius": 0.65,
    "targets": [
        ("crate_01", "HopIslet", -17.0, -24.5),
        ("crate_02", "SeaStack", -25.0, -45.0),
        ("crate_03", "TwinIslet", -32.5, -22.0),
        ("crate_04", "TwinIslet", -29.5, -22.0),
        ("crate_05", "FarBuoy", -36.0, -50.5),
        ("crate_06", "LowRaft", -30.0, -11.0),
        ("crate_07", "TrioBlock", -50.1, -16.7),
        ("crate_08", "TrioBlock", -47.9, -16.7),
        ("crate_09", "TrioBlock", -49.0, -14.8),
        ("crate_10", "HighStack", -33.0, 0.0),
        ("crate_11", "NorthPillar", -7.0, 37.0),
        ("crate_12", "BaseIslet", -5.3, 1.5),
        ("crate_13", "BaseIslet", -2.7, 1.5),
        ("crate_14", "EastRock", 6.0, 35.0),
        ("crate_15", "LowIslet", 8.0, -13.0),
        ("crate_16", "FarStack", 30.0, 2.0),
        ("crate_17", "TwinRaft", 22.8, -24.0),
        ("crate_18", "TwinRaft", 25.2, -24.0),
    ],
    # (id, piece, sensor centre x/z, half size x/y/z, respawn x/z, respawn yaw, required targets)
    "checkpoints": [
        ("checkpoint_1", "WestLanding", (-42.0, -29.0), (4.0, 1.5, 2.0), (-42.0, -31.0), 0.0, 5),
        ("checkpoint_2", "NorthBench", (-30.0, 7.0), (2.0, 1.5, 4.0), (-34.0, 7.0), 90.0, 10),
        ("checkpoint_3", "UpperBench", (-1.0, 26.0), (2.0, 1.5, 6.0), (-6.0, 26.0), 90.0, 14),
    ],
    # The detonator on the loading dock, beside the start.
    "finish": ("LoadingDock", (10.0, -34.0), (3.0, 1.5, 3.0)),
    "profile_id": "manki_quarry_v1",
    "packages": ("manki",),
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


def pieces(spec=SPEC):
    """Every named footprint (x0, z0, x1, z1, top): route, islands and landmark."""
    return {**spec["route"], **spec["islands"], **spec["landmark"]}


def solids(spec=SPEC):
    """Named closed boxes (x0, y0, z0, x1, y1, z1) in GameplayGeometry order, standing on the water."""
    return {name: (x0, spec["water_y"], z0, x1, top, z1) for name, (x0, z0, x1, z1, top) in pieces(spec).items()}


def collision(spec=SPEC):
    """Named authoritative meshes as triangle lists [((x, y, z), (x, y, z), (x, y, z)), ...]."""
    meshes = {}
    for name, b in solids(spec).items():
        meshes[name] = _box(*b)
        _check_outward(name, meshes[name], ((b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2))
    return meshes


def _inside(piece, x, z, spec=SPEC, margin=0.0):
    x0, z0, x1, z1, _ = pieces(spec)[piece]
    return x0 + margin <= x <= x1 - margin and z0 + margin <= z <= z1 - margin


def check(spec=SPEC):
    """Structural rules the bake cannot see: markers sit on their pieces, solids do not overlap."""
    problems = []
    boxes = pieces(spec)
    names = list(boxes)
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            ax0, az0, ax1, az1, _ = boxes[a]
            bx0, bz0, bx1, bz1, _ = boxes[b]
            if ax0 < bx1 - 1e-6 and bx0 < ax1 - 1e-6 and az0 < bz1 - 1e-6 and bz0 < az1 - 1e-6:
                problems.append("%s overlaps %s" % (a, b))
    x, z, _ = spec["start"]
    if not _inside("LoadingDock", x, z, spec, 1.0):
        problems.append("the start is not on the loading dock")
    for tid, island, tx, tz in spec["targets"]:
        if island not in spec["islands"] or not _inside(island, tx, tz, spec, 0.4):
            problems.append("%s is not on island %s" % (tid, island))
    for cid, piece, centre, half, respawn, _, _ in spec["checkpoints"]:
        if not _inside(piece, *respawn, spec, 0.5):
            problems.append("%s respawn is not on %s" % (cid, piece))
        if not _inside(piece, *centre, spec):
            problems.append("%s sensor is not over %s" % (cid, piece))
    piece, centre, _ = spec["finish"]
    if not _inside(piece, *centre, spec):
        problems.append("the finish is not over %s" % piece)
    return problems


def course(spec=SPEC):
    """The Character Course markers for shell-apply (positions in world space)."""
    tops = pieces(spec)
    body = spec["spawn_body_half_height"]
    targets = [(tid, (x, round(tops[island][4] + spec["target_height"], 4), z), spec["target_radius"])
               for tid, island, x, z in spec["targets"]]
    checkpoints = []
    for cid, piece, (cx, cz), half, (rx, rz), yaw, required in spec["checkpoints"]:
        top = tops[piece][4]
        checkpoints.append((cid, (cx, top + half[1], cz), half, (rx, top + body, rz), yaw, required))
    piece, (fx, fz), half = spec["finish"]
    finish = ((fx, tops[piece][4] + half[1], fz), half)
    return shell.course(spec["profile_id"], spec["packages"], targets, checkpoints, finish)


def bounds(spec=SPEC):
    """Map extent of all collision (x0, x1, z0, z1)."""
    boxes = list(solids(spec).values())
    return (min(b[0] for b in boxes), max(b[3] for b in boxes), min(b[2] for b in boxes), max(b[5] for b in boxes))


def spec_corridor(spec=SPEC):
    x0, x1, z0, z1 = bounds(spec)
    highest = max(b[4] for b in solids(spec).values())
    return {"minX": x0 - SIDE_BLAST_MARGIN, "maxX": x1 + SIDE_BLAST_MARGIN,
            "minZ": z0 - SIDE_BLAST_MARGIN, "maxZ": z1 + SIDE_BLAST_MARGIN,
            "killY": spec["water_y"], "topY": highest + TOP_BLAST_MARGIN}


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
    sx, sz, syaw = spec["start"]
    top = spec["route"]["LoadingDock"][4]
    if len(arena["spawns"]) != 1:
        problems.append("baked arena has %d spawns, a course has one" % len(arena["spawns"]))
    else:
        x, y, z, yaw = arena["spawns"][0]
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - (top + spec["spawn_body_half_height"])) > tol:
            problems.append("Spawn_01 at (%.2f, %.2f, %.2f) differs from spec" % (x, y, z))
        if abs((math.degrees(yaw) - syaw + 180) % 360 - 180) > 0.05:
            problems.append("Spawn_01 yaw %.2f differs from spec %.2f" % (math.degrees(yaw), syaw))
    return problems


def load(spec_only: bool = False, spec=SPEC) -> dict:
    """Layout for the art generator. Strict mode requires the baked arena to match SPEC."""
    problems = check(spec)
    if problems:
        raise ValueError("manki_quarry SPEC is inconsistent:\n  " + "\n  ".join(problems))
    data = {
        "spec": spec,
        "waterY": spec["water_y"],
        "pieces": {name: tuple(p) for name, p in pieces(spec).items()},
        "route": list(spec["route"]),
        "islands": list(spec["islands"]),
        "landmark": list(spec["landmark"]),
        "start": tuple(spec["start"]),
        "course": course(spec),
        "specOnly": spec_only,
    }
    if spec_only:
        data.update({"corridor": spec_corridor(spec), "source": None, "sourceSha256": None, "arena": None, "arenaSha256": None})
        return data
    arena = baked.read_arena(ARENA)
    problems = verify_arena(arena, spec)
    if problems:
        raise ValueError("Baked manki_quarry.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


def unity_shell_json(spec=SPEC) -> dict:
    """Input for sloparena.stage.shell-apply: one mesh per solid, Spawn_01 and the course."""
    problems = check(spec)
    if problems:
        raise ValueError("manki_quarry SPEC is inconsistent:\n  " + "\n  ".join(problems))
    meshes = [shell.mesh(name, "MankiQuarry" + name, *shell.indexed(tris)) for name, tris in collision(spec).items()]
    x, z, yaw = spec["start"]
    top = spec["route"]["LoadingDock"][4] + spec["spawn_body_half_height"]
    return shell.document("manki_quarry", spec["water_y"], meshes, [(x, top, z, yaw)], course=course(spec))


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
