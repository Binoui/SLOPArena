"""Fight Night Pier gameplay shell: FightGuy's Character Course and its authoritative baked arena.

SPEC records the locked course (docs/design/stages/fightguy_pier.design.md): a seaside amusement
pier at dusk that the fight club has taken over for title night. The sea is the kill plane; the
route runs up the midway, across to the burnt old pier, up the Big Wheel's tower and down the
coaster to the ring at the pier head. Targets are gold balloons in the shapes of FightGuy's
specials (a row for R, a stack for E, a far one for A) plus one bonus target per section that
takes eight hits (one Fist of Fury). Spacing is calibrated with the cooked FightGuy kit in
tests/Shared.Tests/FightGuyPierRouteTests.cs; move a piece there and here together.

Every solid is a closed box. A piece stands in the sea (base = water_y) unless it is stacked on
another piece, in which case its base is that piece's top (the wheel tower, the stacked gondola
pods, the coaster's lift-hill top and the ring stand on the decks).

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
STAGE = "fightguy_pier"
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/fightguy_pier/fightguy_pier.unity"
ARENA = REPO / "data/arenas/fightguy_pier.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0
DECK = 3.0

SPEC = {
    # The sea: every standing solid's base and the kill plane (the visual sea sits just above it).
    "water_y": -0.2,
    "spawn_body_half_height": 0.85,
    # Route pieces in loop order.
    "route": {
        # Section 1 - the Midway: the promenade (the car bash beside the gate), the midway and the
        # dunk tank out at sea.
        "Promenade": (-22.0, -48.0, 22.0, -38.0, DECK),
        "Midway": (-6.0, -38.0, 6.0, -16.0, DECK),
        "DunkBuoy": (-11.5, -31.0, -9.5, -29.0, 2.6),
        "DunkTank": (-19.0, -32.0, -15.0, -28.0, DECK),
        # The carousel plaza; the carousel house stands on its east side, so the camera has
        # room behind a fighter taking off west for the old pier.
        "Plaza": (-10.0, -16.0, 10.0, -4.0, DECK),
        "Carousel": (3.5, -13.5, 9.5, -6.5, 6.6),
        # Section 2 - the Old Pier: the burnt pier beside the new one. Jump -> R crosses the
        # 10 m gap from the plaza through the balloon line (it fits the kick's 8 m hit window);
        # hopping the three burnt pilings zig-zagging under it is the ordinary way over.
        "Piling1": (-13.5, -10.7, -12.1, -9.3, DECK),
        "Piling2": (-16.1, -8.7, -14.7, -7.3, 3.3),
        "Piling3": (-18.7, -10.7, -17.3, -9.3, 2.8),
        "OldLanding": (-34.0, -14.0, -20.0, -4.0, DECK),
        "OldDeckB": (-36.0, 1.0, -27.0, 7.0, 2.6),
        "SteppingBuoy": (-43.0, -1.5, -41.0, 0.5, 2.6),
        "BellBuoy": (-50.0, -2.0, -47.0, 1.0, 2.4),
        "OldDeckC": (-34.0, 11.5, -28.0, 15.5, 3.4),
        "OldDeckD": (-38.0, 19.5, -31.0, 22.5, 2.8),
        "OldPierHead": (-40.0, 25.5, -24.0, 37.5, DECK),
        # Section 3 - the VIP Tower: the ticket raft, the wheel deck, the champ's 6 m VIP tower
        # (Jump -> E up its west face) and the gondolas he borrowed off the Big Wheel, stacked on
        # crates round its north side (the ordinary climb).
        "TicketBooth": (-19.0, 27.0, -15.0, 31.0, 3.4),
        "WheelDeck": (-12.0, 16.0, 8.0, 38.0, DECK),
        "Pod1": (-7.5, 31.0, -5.5, 33.0, 4.3),
        "Pod2": (-4.0, 32.6, -2.0, 34.6, 5.6),
        "Pod3": (-0.5, 32.6, 1.5, 34.6, 6.9),
        "Pod4": (3.0, 31.0, 5.0, 33.0, 8.2),
        "WheelTower": (-3.0, 24.0, 3.0, 30.0, 9.0),
        # Section 4 - the Coaster: the lift-hill top, a drop, the long straight, the last drop
        # to the pier head and the ring.
        "LiftHill": (4.5, 25.0, 8.0, 29.0, 8.4),
        "CoasterDrop": (12.5, 24.0, 16.5, 28.0, 7.2),
        "CoasterStraight": (20.0, 20.0, 24.0, 40.0, 6.0),
        "CoasterEnd": (20.0, 43.5, 24.0, 47.5, 4.6),
        "PierHead": (-10.0, 38.0, 16.0, 54.0, DECK),
        "PrizeBuoy": (20.0, 51.0, 23.0, 54.0, 2.6),
        "Ring": (0.0, 44.0, 10.0, 52.0, 4.2),
    },
    # Pieces that stand on another piece instead of the sea: name -> host.
    "stacked": {
        "Carousel": "Plaza",
        "Pod1": "WheelDeck", "Pod2": "WheelDeck", "Pod3": "WheelDeck", "Pod4": "WheelDeck",
        "WheelTower": "WheelDeck", "LiftHill": "WheelDeck", "Ring": "PierHead",
    },
    # Spawn_01 (x, z, yaw degrees; yaw 0 looks along +Z) on the promenade at the pier gate.
    "start": (0.0, -43.0, 0.0),
    # Targets in route order: (id, x, y, z, radius, hits). Balloons break on one hit; the four
    # bonus targets (car, pinata, punch, bag) take eight, one Fist of Fury.
    "balloon_radius": 0.55,
    "targets": [
        # Section 1: the car bash by the gate (F), the shooting gallery row (R), the High Striker
        # stack (E), the dunk tank (A).
        ("car", 12.0, 3.9, -43.0, 1.1, 8),
        ("gallery_1", 3.2, 4.6, -33.0, None, 1),
        ("gallery_2", 3.2, 4.6, -31.5, None, 1),
        ("gallery_3", 3.2, 4.6, -30.0, None, 1),
        ("gallery_4", 3.2, 4.6, -28.5, None, 1),
        ("gallery_5", 3.2, 4.6, -27.0, None, 1),
        ("striker_1", -3.6, 4.3, -24.0, None, 1),
        ("striker_2", -3.6, 5.6, -24.0, None, 1),
        ("striker_3", -3.6, 6.9, -24.0, None, 1),
        ("dunk", -17.0, 4.6, -30.0, None, 1),
        # Section 2: the balloon line over the gap (R), the pinata, the bell buoy (A).
        ("line_1", -12.8, 5.1, -9.0, None, 1),
        ("line_2", -15.4, 5.3, -9.0, None, 1),
        ("line_3", -18.0, 5.1, -9.0, None, 1),
        ("line_4", -20.6, 5.0, -9.0, None, 1),
        ("pinata", -30.0, 4.7, -9.5, 0.9, 8),
        ("bell", -48.5, 4.0, -0.5, None, 1),
        # Section 3: two balloons on the ticket booth, the stack up the tower (E), the punch
        # machine on top.
        ("booth_1", -17.0, 4.8, 28.0, None, 1),
        ("booth_2", -17.0, 4.8, 30.0, None, 1),
        ("tower_1", -3.6, 5.3, 27.0, None, 1),
        ("tower_2", -3.6, 7.3, 27.0, None, 1),
        ("tower_3", -3.6, 9.4, 27.0, None, 1),
        ("punch", 1.5, 10.0, 27.0, 0.75, 8),
        # Section 4: the coaster straight row (R), the prize buoy (A), the heavy bag.
        ("coaster_1", 22.0, 7.6, 27.5, None, 1),
        ("coaster_2", 22.0, 7.6, 29.0, None, 1),
        ("coaster_3", 22.0, 7.6, 30.5, None, 1),
        ("coaster_4", 22.0, 7.6, 32.0, None, 1),
        ("prize", 21.5, 4.2, 52.5, None, 1),
        ("bag", 13.0, 4.4, 42.0, 0.75, 8),
    ],
    # (id, piece, sensor centre x/z, half size x/y/z, respawn x/z, respawn yaw, required targets)
    "checkpoints": [
        ("checkpoint_1", "Plaza", (-7.0, -9.0), (3.0, 1.5, 4.0), (-7.0, -9.0), 270.0, 10),
        ("checkpoint_2", "OldPierHead", (-32.0, 31.5), (4.0, 1.5, 4.0), (-30.0, 31.5), 90.0, 16),
        # On the lift hill, past the punch machine: a sensor on the tower top would hold the
        # machine and greet every landing with "1 target missed" (check() refuses that).
        ("checkpoint_3", "LiftHill", (6.25, 27.0), (1.75, 1.5, 2.0), (6.0, 27.0), 90.0, 22),
    ],
    # The ring at the pier head.
    "finish": ("Ring", (5.0, 48.0), (4.0, 1.5, 3.5)),
    "profile_id": "fightguy_pier_v1",
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
    """Every named footprint (x0, z0, x1, z1, top)."""
    return dict(spec["route"])


def base(name, spec=SPEC):
    """The bottom of a piece: its host's top when stacked, else the sea."""
    host = spec["stacked"].get(name)
    return spec["route"][host][4] if host else spec["water_y"]


def solids(spec=SPEC):
    """Named closed boxes (x0, y0, z0, x1, y1, z1) in GameplayGeometry order."""
    return {name: (x0, base(name, spec), z0, x1, top, z1) for name, (x0, z0, x1, z1, top) in pieces(spec).items()}


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


def target_radius(target, spec=SPEC):
    return target[4] if target[4] is not None else spec["balloon_radius"]


def check(spec=SPEC):
    """Structural rules the bake cannot see: markers on their pieces, no unplanned overlaps."""
    problems = []
    boxes = pieces(spec)
    names = list(boxes)
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            if spec["stacked"].get(a) == b or spec["stacked"].get(b) == a:
                continue
            ax0, az0, ax1, az1, _ = boxes[a]
            bx0, bz0, bx1, bz1, _ = boxes[b]
            if ax0 < bx1 - 1e-6 and bx0 < ax1 - 1e-6 and az0 < bz1 - 1e-6 and bz0 < az1 - 1e-6:
                problems.append("%s overlaps %s" % (a, b))
    for name, host in spec["stacked"].items():
        if host is not None:
            x0, z0, x1, z1, _ = boxes[name]
            if not (_inside(host, x0, z0, spec) and _inside(host, x1, z1, spec)):
                problems.append("%s is not on %s" % (name, host))
    x, z, _ = spec["start"]
    if not _inside("Promenade", x, z, spec, 1.0):
        problems.append("the start is not on the promenade")
    ids = [t[0] for t in spec["targets"]]
    if len(ids) != len(set(ids)):
        problems.append("duplicate target ids")
    for t in spec["targets"]:
        if not 1 <= t[5] <= 32 or not 0 < target_radius(t, spec) <= 2:
            problems.append("%s has an invalid radius or hit count" % t[0])
    for cid, piece, centre, half, respawn, _, required in spec["checkpoints"]:
        if not _inside(piece, *respawn, spec, 0.5):
            problems.append("%s respawn is not on %s" % (cid, piece))
        if not _inside(piece, *centre, spec):
            problems.append("%s sensor is not over %s" % (cid, piece))
        if not 0 < required <= len(ids):
            problems.append("%s requires %d targets" % (cid, required))
        # A required target over the sensor greets every landing with "1 target missed".
        for t in spec["targets"][:required]:
            reach = half[0] + target_radius(t, spec), half[2] + target_radius(t, spec)
            if abs(t[1] - centre[0]) < reach[0] and abs(t[3] - centre[1]) < reach[1]:
                problems.append("%s counts %s, which sits over its sensor" % (cid, t[0]))
    piece, centre, _ = spec["finish"]
    if not _inside(piece, *centre, spec):
        problems.append("the finish is not over %s" % piece)
    return problems


def course(spec=SPEC):
    """The Character Course markers for shell-apply (positions in world space)."""
    tops = pieces(spec)
    body = spec["spawn_body_half_height"]
    targets = [(t[0], (t[1], t[2], t[3]), target_radius(t, spec), t[5]) for t in spec["targets"]]
    checkpoints = []
    for cid, piece, (cx, cz), half, (rx, rz), yaw, required in spec["checkpoints"]:
        top = tops[piece][4]
        checkpoints.append((cid, (cx, top + half[1], cz), half, (rx, top + body, rz), yaw, required))
    piece, (fx, fz), half = spec["finish"]
    finish = ((fx, tops[piece][4] + half[1], fz), half)
    return shell.course(spec["profile_id"], targets, checkpoints, finish,
                        racing_line=json.loads(Path(__file__).with_name("racing_line.json").read_text()))


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
    top = spec["route"]["Promenade"][4]
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
        raise ValueError("%s SPEC is inconsistent:\n  " % STAGE + "\n  ".join(problems))
    data = {
        "spec": spec,
        "waterY": spec["water_y"],
        "pieces": {name: tuple(p) for name, p in pieces(spec).items()},
        "solids": {name: tuple(s) for name, s in solids(spec).items()},
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
        raise ValueError("Baked %s.arena does not match the layout spec:\n  " % STAGE + "\n  ".join(problems))
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
        raise ValueError("%s SPEC is inconsistent:\n  " % STAGE + "\n  ".join(problems))
    meshes = [shell.mesh(name, "FightGuyPier" + name, *shell.indexed(tris)) for name, tris in collision(spec).items()]
    x, z, yaw = spec["start"]
    top = spec["route"]["Promenade"][4] + spec["spawn_body_half_height"]
    return shell.document(STAGE, spec["water_y"], meshes, [(x, top, z, yaw)], course=course(spec))


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
