"""Table Manners gameplay shell: the intended layout and the authoritative baked arena.

SPEC records the approved kids' craft table: one round tabletop whose floor-length
tablecloth is a solid wall down to the KO line, and a round play-dough cake riser in
the centre. The collision source scene (Assets/Stages/table_manners/table_manners.unity)
is built from `collision()` and baked into data/arenas/table_manners.arena, which Shared,
the GameServer and client prediction load. The art generator never trusts SPEC alone:
`load()` reads the baked arena, checks that its triangles, spawns and blast planes match
SPEC and returns the authoritative corridor. `load(spec_only=True)` exists only for
previewing a proposed shell before it has been baked.

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
from stagekit import shell  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/table_manners/table_manners.unity"
ARENA = REPO / "data/arenas/table_manners.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "top": 0.0,
    "kill_y": -15.0,  # AuthoringAids/KillPlane sits on the lowest tablecloth vertex
    # Round craft table. Vertex 0 lies on +X, so the outline reaches exactly +-radius on
    # both axes and the side blast planes land at +-(radius + 10).
    "table": {"radius": 15.0, "segments": 64},
    # Centre riser: the kids' play-dough birthday cake, one jump for every roster fighter.
    "cake": {"radius": 4.0, "top": 1.5, "segments": 32},
    # Spawn_01..04 (x, z); each faces the centre. The first two are diagonal opposites.
    "spawns": [(-6.5, -6.5), (6.5, 6.5), (-6.5, 6.5), (6.5, -6.5)],
    "spawn_body_half_height": 0.85,
}


# ---------------------------------------------------------------- geometry

def circle(radius, segments):
    """CCW outline in map view (+x east/right, +z north/up), vertex 0 on +X."""
    return [(radius * math.cos(2 * math.pi * i / segments), radius * math.sin(2 * math.pi * i / segments))
            for i in range(segments)]


def spawn_yaw(x, z):
    """Unity yaw in degrees (0 = +Z) facing the table centre."""
    return math.degrees(math.atan2(-x, -z)) % 360.0


def _drum(outline, top, bottom, closed_bottom):
    """Closed vertical prism: centre-fan top, outward walls, optional centre-fan bottom."""
    tris = []
    n = len(outline)
    for i in range(n):
        (ax, az), (bx, bz) = outline[i], outline[(i + 1) % n]
        tris.append(((0.0, top, 0.0), (bx, top, bz), (ax, top, az)))
        a_top, b_top = (ax, top, az), (bx, top, bz)
        a_low, b_low = (ax, bottom, az), (bx, bottom, bz)
        tris.append((a_top, b_top, b_low))
        tris.append((a_top, b_low, a_low))
        if closed_bottom:
            tris.append(((0.0, bottom, 0.0), (ax, bottom, az), (bx, bottom, bz)))
    return tris


def _normal(t):
    a, b, c = t
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def collision(spec=SPEC):
    """Named authoritative meshes as triangle lists [((x, y, z), (x, y, z), (x, y, z)), ...]."""
    table = spec["table"]
    cake = spec["cake"]
    drums = {
        "CraftTable": (circle(table["radius"], table["segments"]), spec["top"], spec["kill_y"], True),
        # The cake rests on the tabletop; its hidden bottom face is omitted.
        "PlayDoughCake": (circle(cake["radius"], cake["segments"]), cake["top"], spec["top"], False),
    }
    meshes = {}
    for name, (outline, top, bottom, closed) in drums.items():
        meshes[name] = _drum(outline, top, bottom, closed)
        for t in meshes[name]:
            n = _normal(t)
            cx, cy, cz = (sum(p[i] for p in t) / 3 for i in range(3))
            if abs(n[1]) > 0.5:
                outward = n[1] > 0 if abs(cy - top) < 1e-6 else n[1] < 0
            else:
                outward = n[0] * cx + n[2] * cz > 0
            if not outward:
                raise AssertionError("%s triangle %s faces into the solid" % (name, t))
    return meshes


def spec_corridor(spec=SPEC):
    r = spec["table"]["radius"]
    return {"minX": -r - SIDE_BLAST_MARGIN, "maxX": r + SIDE_BLAST_MARGIN,
            "minZ": -r - SIDE_BLAST_MARGIN, "maxZ": r + SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": spec["cake"]["top"] + TOP_BLAST_MARGIN}


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
    for i, ((x, y, z, yaw), (sx, sz)) in enumerate(zip(arena["spawns"], spec["spawns"])):
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - (spec["top"] + spec["spawn_body_half_height"])) > tol:
            problems.append("Spawn_%02d at (%.2f, %.2f, %.2f) differs from spec" % (i + 1, x, y, z))
        d = (math.degrees(yaw) - spawn_yaw(sx, sz) + 180) % 360 - 180
        if abs(d) > 0.05:
            problems.append("Spawn_%02d yaw %.2f differs from spec %.2f" % (i + 1, math.degrees(yaw), spawn_yaw(sx, sz)))
    return problems


def load(spec_only: bool = False, spec=SPEC) -> dict:
    """Layout for the art generator. Strict mode requires the baked arena to match SPEC."""
    data = {
        "spec": spec,
        "top": spec["top"],
        "killY": spec["kill_y"],
        "table": dict(spec["table"]),
        "tableOutline": circle(spec["table"]["radius"], spec["table"]["segments"]),
        "cake": dict(spec["cake"]),
        "cakeOutline": circle(spec["cake"]["radius"], spec["cake"]["segments"]),
        "spawns": [(x, z, spawn_yaw(x, z)) for x, z in spec["spawns"]],
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
        raise ValueError("Baked table_manners.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


# GameplayGeometry child -> mesh asset in Assets/Stages/table_manners (GUIDs kept on rebuild).
SHELL_ASSETS = {"CraftTable": "TableMannersCraftTable", "PlayDoughCake": "TableMannersPlayDoughCake"}


def unity_shell_json(spec=SPEC) -> dict:
    """Input for sloparena.stage.shell-apply: indexed meshes and spawn markers."""
    meshes = [shell.mesh(name, SHELL_ASSETS[name], *shell.indexed(tris)) for name, tris in collision(spec).items()]
    top = spec["top"] + spec["spawn_body_half_height"]
    return shell.document("table_manners", spec["kill_y"], meshes,
                          [(x, top, z, spawn_yaw(x, z)) for x, z in spec["spawns"]])


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
