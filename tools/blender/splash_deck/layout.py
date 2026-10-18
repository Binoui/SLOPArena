"""Splash Deck gameplay shell: the intended layout and the authoritative baked arena.

SPEC records the approved fishing boat: a flat deck with a square transom stern,
parallel sides and a parabolic bow (+X), whose hull walls drop straight to the KO
line, plus the barge's two proven raised spots, now an aft wheelhouse and a forward
fish-hold hatch. Spawns, facings, side edges and the lower KO plane keep their barge
values. The collision source scene (Assets/Stages/splash_deck/splash_deck.unity) is
built from `collision()` and baked into data/arenas/splash_deck.arena, which Shared,
the GameServer and client prediction load. The art generator never trusts SPEC alone:
`load()` reads the baked arena, checks that its triangles, spawns and blast planes
match SPEC and returns the authoritative corridor. `load(spec_only=True)` exists only
for previewing a proposed shell before it has been baked.

Coordinates are Unity world space: +X east (the bow), +Y up, +Z north (port side).
Triangles are wound so that (b - a) x (c - a) points out of the solid (Unity's
clockwise front faces).

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
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/splash_deck/splash_deck.unity"
ARENA = REPO / "data/arenas/splash_deck.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

SPEC = {
    "top": 0.0,
    "kill_y": -12.0,  # AuthoringAids/KillPlane sits on the lowest hull vertex
    # Deck outline, mirror-symmetric about z = 0. The transom spans |z| <= half_beam -
    # quarter_radius at stern_x; rounded quarters meet the parallel sides, which run to
    # shoulder_x; the bow's half-beam is half_beam * (1 - u ** bow_power) with
    # u = (x - shoulder_x) / (bow_x - shoulder_x), ending in a point at the stem.
    "hull": {"stern_x": -15.5, "quarter_radius": 3.0, "quarter_segments": 6,
             "half_beam": 11.0, "shoulder_x": 3.0, "bow_x": 20.5, "bow_power": 2.0, "bow_segments": 20},
    # The barge's two raised spots, one full jump for every roster fighter: an aft
    # wheelhouse and a forward fish-hold hatch. (x0, z0, x1, z1) footprints.
    "raised": {
        "Wheelhouse": {"footprint": (-13.5, -2.5, -7.5, 2.5), "top": 1.5},
        "FishHatch": {"footprint": (7.5, -2.5, 13.5, 2.5), "top": 1.5},
    },
    # Spawn_01..04 (x, z, yaw degrees; yaw 0 looks along +Z). The first two are
    # diagonal opposites; facings are the barge's diagonal inward ones.
    "spawns": [(-6.0, -5.0, 45.0), (6.0, 5.0, 225.0), (-6.0, 5.0, 135.0), (6.0, -5.0, 315.0)],
    "spawn_body_half_height": 0.85,
}


# ---------------------------------------------------------------- outline

def half_beam(x, spec=SPEC):
    """Deck half-width at x (0 beyond the stem or behind the transom)."""
    h = spec["hull"]
    if x < h["stern_x"] - 1e-9 or x > h["bow_x"] + 1e-9:
        return 0.0
    if x >= h["shoulder_x"]:
        u = (x - h["shoulder_x"]) / (h["bow_x"] - h["shoulder_x"])
        return h["half_beam"] * (1.0 - u ** h["bow_power"])
    cx = h["stern_x"] + h["quarter_radius"]
    if x >= cx:
        return h["half_beam"]
    r = h["quarter_radius"]
    return h["half_beam"] - r + math.sqrt(max(0.0, r * r - (x - cx) ** 2))


def port_side(spec=SPEC):
    """Port (+z) deck edge from the stem to the transom's port corner."""
    h = spec["hull"]
    pts = []
    n = h["bow_segments"]
    for k in range(n + 1):  # stem (u = 1) back to the shoulder (u = 0)
        u = 1.0 - k / n
        x = h["shoulder_x"] + u * (h["bow_x"] - h["shoulder_x"])
        pts.append((x, h["half_beam"] * (1.0 - u ** h["bow_power"])))
    r = h["quarter_radius"]
    cx, cz = h["stern_x"] + r, h["half_beam"] - r
    q = h["quarter_segments"]
    for j in range(q + 1):  # quarter arc from the side (90 deg) to the transom (180 deg)
        a = math.radians(90.0 + 90.0 * j / q)
        pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    pts[0] = (h["bow_x"], 0.0)
    return [(x + 0.0, z + 0.0) for x, z in pts]


def outline(spec=SPEC):
    """CCW deck outline in map view (+x east/right, +z north/up), starting at the stem."""
    port = port_side(spec)
    starboard = [(x, -z) for x, z in reversed(port[1:])]
    return port + starboard


def spawn_yaw(x, z):
    """Unity yaw in degrees (0 = +Z) facing the deck centre (reference only)."""
    return math.degrees(math.atan2(-x, -z)) % 360.0


def _normal(t):
    a, b, c = t
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def _hull(spec):
    """Closed prism: zipper-triangulated deck and bottom, outward walls."""
    top, bottom = spec["top"], spec["kill_y"]
    port = port_side(spec)
    ring = outline(spec)
    tris = []
    # Deck (+Y) and bottom (-Y), zipped across the beam from the stem to the transom.
    for i in range(len(port) - 1):
        (ax, az), (bx, bz) = port[i], port[i + 1]
        pa, pb, sa, sb = (ax, az), (bx, bz), (ax, -az), (bx, -bz)
        quad = [sa, pa, pb, sb] if i else [pa, pb, sb]  # CCW in map view; the stem is one point
        for k in range(1, len(quad) - 1):
            p, q, r = quad[0], quad[k], quad[k + 1]
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
    """Open-bottomed box resting on the deck: top and four outward walls."""
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
    meshes = {"FishingBoatHull": _hull(spec)}
    # The hull is convex, so every face must point away from an interior point.
    _check_outward("FishingBoatHull", meshes["FishingBoatHull"], (0.0, (spec["top"] + spec["kill_y"]) / 2, 0.0))
    for name, r in spec["raised"].items():
        meshes[name] = _raised(r["footprint"], r["top"], spec["top"])
        x0, z0, x1, z1 = r["footprint"]
        _check_outward(name, meshes[name], ((x0 + x1) / 2, (r["top"] + spec["top"]) / 2, (z0 + z1) / 2))
    return meshes


def bounds(spec=SPEC):
    h = spec["hull"]
    return (h["stern_x"], h["bow_x"], -h["half_beam"], h["half_beam"])


def spec_corridor(spec=SPEC):
    x0, x1, z0, z1 = bounds(spec)
    highest = max([spec["top"]] + [r["top"] for r in spec["raised"].values()])
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
        "hull": dict(spec["hull"]),
        "outline": outline(spec),
        "raised": {name: dict(r) for name, r in spec["raised"].items()},
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
        raise ValueError("Baked splash_deck.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    data["corridor"] = baked.corridor(arena)
    data["source"] = str(SOURCE_SCENE.relative_to(REPO))
    data["sourceSha256"] = baked.sha256(SOURCE_SCENE)
    data["arena"] = str(ARENA.relative_to(REPO))
    data["arenaSha256"] = baked.sha256(ARENA)
    return data


# GameplayGeometry child -> mesh asset in Assets/Stages/splash_deck (GUIDs kept on rebuild).
SHELL_ASSETS = {"FishingBoatHull": "SplashDeckHull", "Wheelhouse": "SplashDeckWheelhouse", "FishHatch": "SplashDeckFishHatch"}


def unity_shell_json(spec=SPEC) -> dict:
    """Input for sloparena.stage.shell-apply: indexed meshes and spawn markers."""
    meshes = [shell.mesh(name, SHELL_ASSETS[name], *shell.indexed(tris)) for name, tris in collision(spec).items()]
    top = spec["top"] + spec["spawn_body_half_height"]
    return shell.document("splash_deck", spec["kill_y"], meshes,
                          [(x, top, z, yaw) for x, z, yaw in spec["spawns"]])


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
