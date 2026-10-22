"""Neo Mall gameplay shell: Wibou's Character Course and its authoritative baked arena.

SPEC records the locked course (docs/design/stages/wibou_mall.design.md): a shopping mall after
closing, climbed from the south wing's level-2 gallery to the game store on level 4. The freshly
mopped ground floor is the kill plane; galleries, bridges and the elevator tower stand on it. The
route runs north up the west gallery, east over the closed skybridge, up the Grand Court escalator,
west over the level-3 bridge to the food court, up the glass elevator tower and east over the top
bridge to the game store. Upper levels never overhang the route, so the camera always has headroom.

Targets are shop mannequins dressed as agents, in the shapes of Wibou's kit: rows for R (Dash
Slash), fans of three for A (Shuriken Toss), the lip of the elevator tower for E, and one boss per
section that takes five hits. Spacing is calibrated with the cooked Wibou kit in
tests/Shared.Tests/WibouMallRouteTests.cs; move a piece there and here together.

Solids:
- route decks stand on the floor (base = water_y) unless stacked on another piece or floating
  (hanging planters and signs, with their own base);
- ramps (the escalator and its balustrades) slope along +Z from the south height to the north one;
- rails are 1 m railings stacked on a deck's edge, open wherever the route leaves the deck;
- walls are the shopfronts behind the galleries: tall boxes from the floor, so Rising Slash's
  forward carry cannot throw Wibou out of the mall.

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
STAGE = "wibou_mall"
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/wibou_mall/wibou_mall.unity"
ARENA = REPO / "data/arenas/wibou_mall.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0
L2, L3, L4 = 4.0, 9.0, 15.0
RAIL = 1.0          # railing height above its deck
RAIL_T = 0.12       # railing thickness
SLAB = 0.8          # gallery and bridge floor thickness
STEP_SLAB = 0.6     # the escalator's sloped slab
DOME = 24.0         # the envelope walls rise to the dome's springing line

SPEC = {
    # The mopped ground floor: every standing solid's base and the kill plane.
    "water_y": -0.2,
    "spawn_body_half_height": 0.85,
    "levels": {"L2": L2, "L3": L3, "L4": L4},
    # Route decks in route order.
    "route": {
        # Section 1 - Kiosk Row: the south wing's west gallery, the fashion podium out in the
        # atrium and the closed skybridge (a missing middle span with two hanging planters).
        "WestL2": (-16.0, -50.0, -8.0, -8.0, L2),
        "Podium": (-3.0, -38.0, 1.0, -30.0, L2),
        "BridgeW": (-8.0, -14.0, -6.0, -10.0, L2),
        "PlanterW": (-3.0, -12.7, -1.6, -11.3, 3.7),
        "PlanterE": (1.6, -12.7, 3.0, -11.3, 3.7),
        "BridgeE": (6.0, -14.0, 8.0, -10.0, L2),
        # Section 2 - the escalators: the east gallery, the Grand Court escalator (ramps below) to
        # the level-3 landing and the level-3 bridge west to the food court.
        "EastL2": (8.0, -20.0, 16.0, 4.0, L2),
        "LandingL3": (6.0, 13.0, 16.0, 17.0, L3),
        "BridgeL3": (-8.0, 13.0, 6.0, 17.0, L3),
        # Section 3 - the food court and the glass elevator tower (Jump, jump, E up its west
        # face), with the hanging menu signs and speakers as the ordinary climb on its north side.
        "FoodCourt": (-18.0, 8.0, -8.0, 32.0, L3),
        "Tower": (-8.0, 21.0, -4.0, 25.0, L4),
        "Sign1": (-7.6, 26.2, -6.0, 27.8, 10.5),
        "Sign2": (-6.0, 28.4, -4.4, 30.0, 12.0),
        "Sign3": (-3.6, 26.2, -2.0, 27.8, 13.5),
        # Section 4 - level 4: the top bridge under the giant katana and the east gallery with the
        # arcade, the cinema and the game store.
        "BridgeL4": (-4.0, 21.0, 8.0, 25.0, L4),
        "EastL4": (8.0, 20.0, 16.0, 45.0, L4),
    },
    # Balconies, bridges and hanging pieces over the atrium: name -> base height (their top is in
    # "route"). Galleries and bridges are SLAB-thick floors with the ground floor visible below; the
    # tower and the podium stand on the floor.
    "floating": {
        "WestL2": L2 - SLAB, "BridgeW": L2 - SLAB, "BridgeE": L2 - SLAB, "EastL2": L2 - SLAB,
        "LandingL3": L3 - SLAB, "BridgeL3": L3 - SLAB, "FoodCourt": L3 - SLAB,
        "BridgeL4": L4 - SLAB, "EastL4": L4 - SLAB,
        "PlanterW": 3.1, "PlanterE": 3.1, "Sign1": 10.1, "Sign2": 11.6, "Sign3": 13.1,
    },
    # Pieces standing on another piece instead of the floor: name -> host.
    "stacked": {},
    # The stopped escalator, out of order in the middle: sloped slabs (x0, z0, x1, z1, y at z0,
    # y at z1); the balustrades are thin sloped walls from under the slab to RAIL above the steps.
    "ramps": {
        "EscalatorLow": (8.2, 4.0, 9.8, 7.6, L2, L2 + 3.6 * (L3 - L2) / 9.0),
        "EscalatorHigh": (8.2, 10.0, 9.8, 13.0, L2 + 6.0 * (L3 - L2) / 9.0, L3),
        "BalustradeW": (8.0, 4.0, 8.2, 13.0, L2 + RAIL, L3 + RAIL),
        "BalustradeE": (9.8, 4.0, 10.0, 13.0, L2 + RAIL, L3 + RAIL),
    },
    # 1 m railings on deck edges: name -> (x0, z0, x1, z1, host deck).
    "rails": {
        "RailWestL2A": (-8.0 - RAIL_T, -50.0, -8.0, -14.0, "WestL2"),
        "RailWestL2B": (-8.0 - RAIL_T, -10.0, -8.0, -8.0, "WestL2"),
        "RailWestL2N": (-16.0, -8.0 - RAIL_T, -8.0 - RAIL_T, -8.0, "WestL2"),
        "RailBridgeWS": (-8.0, -14.0, -6.0, -14.0 + RAIL_T, "BridgeW"),
        "RailBridgeWN": (-8.0, -10.0 - RAIL_T, -6.0, -10.0, "BridgeW"),
        "RailBridgeES": (6.0, -14.0, 8.0, -14.0 + RAIL_T, "BridgeE"),
        "RailBridgeEN": (6.0, -10.0 - RAIL_T, 8.0, -10.0, "BridgeE"),
        "RailEastL2A": (8.0, -20.0 + RAIL_T, 8.0 + RAIL_T, -14.0, "EastL2"),
        "RailEastL2B": (8.0, -10.0, 8.0 + RAIL_T, 4.0, "EastL2"),
        "RailEastL2S": (8.0, -20.0, 16.0, -20.0 + RAIL_T, "EastL2"),
        "RailEastL2N": (10.0, 4.0 - RAIL_T, 16.0, 4.0, "EastL2"),
        "RailLandingS1": (6.0, 13.0, 8.0, 13.0 + RAIL_T, "LandingL3"),
        "RailLandingS2": (10.0, 13.0, 16.0, 13.0 + RAIL_T, "LandingL3"),
        "RailLandingN": (6.0, 17.0 - RAIL_T, 16.0, 17.0, "LandingL3"),
        "RailBridgeL3S": (-8.0, 13.0, 6.0, 13.0 + RAIL_T, "BridgeL3"),
        "RailBridgeL3N": (-8.0, 17.0 - RAIL_T, 6.0, 17.0, "BridgeL3"),
        "RailFoodS": (-18.0, 8.0, -8.0 - RAIL_T, 8.0 + RAIL_T, "FoodCourt"),
        "RailFoodE1": (-8.0 - RAIL_T, 8.0, -8.0, 13.0, "FoodCourt"),
        "RailFoodE2": (-8.0 - RAIL_T, 17.0, -8.0, 21.0, "FoodCourt"),
        "RailFoodE3": (-8.0 - RAIL_T, 28.6, -8.0, 32.0, "FoodCourt"),
        "RailTowerS": (-8.0, 21.0, -4.0, 21.0 + RAIL_T, "Tower"),
        "RailTowerN": (-8.0, 25.0 - RAIL_T, -4.0, 25.0, "Tower"),
        "RailBridgeL4S": (-4.0, 21.0, 8.0, 21.0 + RAIL_T, "BridgeL4"),
        "RailBridgeL4N1": (-4.0, 25.0 - RAIL_T, -3.8, 25.0, "BridgeL4"),
        "RailBridgeL4N2": (-1.8, 25.0 - RAIL_T, 8.0, 25.0, "BridgeL4"),
        "RailEastL4A": (8.0, 20.0 + RAIL_T, 8.0 + RAIL_T, 21.0, "EastL4"),
        "RailEastL4B": (8.0, 25.0, 8.0 + RAIL_T, 45.0, "EastL4"),
        "RailEastL4S": (8.0, 20.0, 16.0, 20.0 + RAIL_T, "EastL4"),
    },
    # The mall's envelope: shopfront walls from the floor up to the dome, (x0, z0, x1, z1, top).
    # Every facade is collision, so Rising Slash's carry and the camera both stop at it.
    "walls": {
        "WallSouth": (-17.0, -51.0, 17.0, -50.0, DOME),
        "WallWestSouth": (-17.0, -50.0, -16.0, -8.0, DOME),
        "WallWestCorner": (-19.0, -9.0, -17.0, -8.0, DOME),
        "WallGrandWest": (-19.0, -8.0, -18.0, 8.0, DOME),
        "WallFoodWest": (-19.0, 8.0, -18.0, 33.0, DOME),
        "WallFoodNorth": (-18.0, 32.0, -8.0, 33.0, DOME),
        "WallCinemaWest": (-9.0, 33.0, -8.0, 45.0, DOME),
        "WallNorth": (-9.0, 45.0, 17.0, 46.0, DOME),
        "WallEast": (16.0, -50.0, 17.0, 45.0, DOME),
    },
    # Spawn_01 (x, z, yaw degrees; yaw 0 looks along +Z) by the katana kiosk on the west gallery.
    "start": (-12.0, -44.0, 0.0),
    "start_piece": "WestL2",
    # Targets in route order: (id, x, y, z, radius, hits). Mannequins break on one hit; the four
    # bosses take five (Skyfall lands two, a close kunai volley three).
    "mannequin_radius": 0.55,
    "targets": [
        # Section 1: the gallery row (R), three agents posing on the fashion podium (A), the boss:
        # the phone-case kiosk's inflatable tube man.
        ("row_1", -10.5, L2 + 1.1, -34.0, None, 1),
        ("row_2", -10.5, L2 + 1.1, -32.6, None, 1),
        ("row_3", -10.5, L2 + 1.1, -31.2, None, 1),
        ("row_4", -10.5, L2 + 1.1, -29.8, None, 1),
        ("podium_1", -1.0, L2 + 1.1, -36.2, None, 1),
        ("podium_2", -1.0, L2 + 1.1, -34.0, None, 1),
        ("podium_3", -1.0, L2 + 1.1, -31.8, None, 1),
        ("tubeman", -12.0, L2 + 1.4, -19.0, 1.0, 5),
        # Section 2: agents frozen on the escalator, the boss on the level-3 landing (the
        # department store's giant display mannequin) and the level-3 bridge row (R).
        ("escalator_1", 9.0, L2 + 0.89 + 0.8, 5.6, None, 1),
        ("escalator_2", 9.0, L2 + 3.78 + 0.8, 10.8, None, 1),
        ("escalator_3", 9.0, L2 + 4.67 + 0.8, 12.4, None, 1),
        ("agent", 13.5, L3 + 1.4, 15.0, 1.0, 5),
        ("bridge3_1", 3.0, L3 + 1.1, 15.0, None, 1),
        ("bridge3_2", 1.6, L3 + 1.1, 15.0, None, 1),
        ("bridge3_3", 0.2, L3 + 1.1, 15.0, None, 1),
        ("bridge3_4", -1.2, L3 + 1.1, 15.0, None, 1),
        # Section 3: the food court row (R), the boss (the hot dog stand's fiberglass mascot) and
        # two agents hanging over the elevator tower's lip (E).
        ("food_1", -11.0, L3 + 1.1, 18.0, None, 1),
        ("food_2", -11.0, L3 + 1.1, 19.4, None, 1),
        ("food_3", -11.0, L3 + 1.1, 20.8, None, 1),
        ("food_4", -11.0, L3 + 1.1, 22.2, None, 1),
        ("hotdog", -13.0, L3 + 1.4, 28.5, 1.0, 5),
        ("lip_1", -8.6, L4 + 0.4, 22.4, None, 1),
        ("lip_2", -8.6, L4 + 0.4, 23.6, None, 1),
        # Section 4: the top bridge row (R), three agents peeking over the east gallery's railing
        # (A from the food court, or slash them on the way), the boss: the mall cop's cardboard
        # cutout outside the game store.
        ("bridge4_1", 0.0, L4 + 1.1, 23.0, None, 1),
        ("bridge4_2", 1.4, L4 + 1.1, 23.0, None, 1),
        ("bridge4_3", 2.8, L4 + 1.1, 23.0, None, 1),
        ("bridge4_4", 4.2, L4 + 1.1, 23.0, None, 1),
        ("peek_1", 8.6, L4 + 1.4, 26.7, None, 1),
        ("peek_2", 8.6, L4 + 1.4, 31.5, None, 1),
        ("peek_3", 8.6, L4 + 1.4, 36.3, None, 1),
        ("cop", 12.5, L4 + 1.4, 37.5, 1.0, 5),
    ],
    # (id, piece, sensor centre x/z, half size x/y/z, respawn x/z, respawn yaw, required targets)
    "checkpoints": [
        ("checkpoint_1", "EastL2", (11.0, -12.0), (2.5, 1.5, 2.0), (12.0, -12.0), 0.0, 8),
        ("checkpoint_2", "FoodCourt", (-10.5, 15.0), (1.5, 1.5, 2.0), (-11.0, 15.0), 0.0, 16),
        # On the tower top: the E climb carries Wibou over it toward the top bridge.
        ("checkpoint_3", "Tower", (-6.0, 23.0), (1.5, 1.5, 1.5), (-6.0, 23.0), 90.0, 23),
    ],
    # The game store's back room at the north end of the east gallery.
    "finish": ("EastL4", (12.0, 42.0), (3.0, 1.5, 1.5)),
    "profile_id": "wibou_mall_v1",
    "packages": ("wibou",),
}


# ---------------------------------------------------------------- solids

def _box(x0, y0, z0, x1, y1, z1):
    """Closed axis-aligned box: 12 outward triangles."""
    return _prism(x0, z0, x1, z1, y0, y0, y1, y1)


def _prism(x0, z0, x1, z1, bottom_south, bottom_north, top_south, top_north):
    """Closed solid over the footprint whose bottom and top slope along +Z."""
    v = {}
    for i in (0, 1):
        for k in (0, 1):
            x, z = (x1 if i else x0), (z1 if k else z0)
            v[(i, 0, k)] = (x, bottom_north if k else bottom_south, z)
            v[(i, 1, k)] = (x, top_north if k else top_south, z)
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
    """Every walkable deck footprint (x0, z0, x1, z1, top)."""
    return dict(spec["route"])


def base(name, spec=SPEC):
    """The bottom of a deck: its host's top when stacked, its own base when floating, else the floor."""
    if name in spec["floating"]:
        return spec["floating"][name]
    host = spec["stacked"].get(name)
    return spec["route"][host][4] if host else spec["water_y"]


def rail_solid(name, spec=SPEC):
    x0, z0, x1, z1, host = spec["rails"][name]
    top = spec["route"][host][4]
    return (x0, top, z0, x1, top + RAIL, z1)


def ramp_slab(name, spec=SPEC):
    """A ramp's sloped slab (x0, z0, x1, z1, bottom south, bottom north, top south, top north)."""
    x0, z0, x1, z1, y0, y1 = spec["ramps"][name]
    if name.startswith("Balustrade"):
        return (x0, z0, x1, z1, y0 - RAIL - STEP_SLAB, y1 - RAIL - STEP_SLAB, y0, y1)
    return (x0, z0, x1, z1, y0 - STEP_SLAB, y1 - STEP_SLAB, y0, y1)


def solids(spec=SPEC):
    """Named solids in GameplayGeometry order: (kind, data). Boxes are (x0, y0, z0, x1, y1, z1);
    ramps (x0, z0, x1, z1, bottom south, bottom north, top south, top north)."""
    out = {}
    for name, (x0, z0, x1, z1, top) in pieces(spec).items():
        out[name] = ("box", (x0, base(name, spec), z0, x1, top, z1))
    for name in spec["ramps"]:
        out[name] = ("ramp", ramp_slab(name, spec))
    for name in spec["rails"]:
        out[name] = ("box", rail_solid(name, spec))
    for name, (x0, z0, x1, z1, top) in spec["walls"].items():
        out[name] = ("box", (x0, spec["water_y"], z0, x1, top, z1))
    return out


def collision(spec=SPEC):
    """Named authoritative meshes as triangle lists [((x, y, z), (x, y, z), (x, y, z)), ...]."""
    meshes = {}
    for name, (kind, s) in solids(spec).items():
        if kind == "box":
            tris = _box(*s)
            inside = ((s[0] + s[3]) / 2, (s[1] + s[4]) / 2, (s[2] + s[5]) / 2)
        else:
            x0, z0, x1, z1, bs, bn, ys, yn = s
            tris = _prism(x0, z0, x1, z1, bs, bn, ys, yn)
            inside = ((x0 + x1) / 2, (bs + bn + ys + yn) / 4, (z0 + z1) / 2)
        _check_outward(name, tris, inside)
        meshes[name] = tris
    return meshes


def _footprints(spec=SPEC):
    """Every solid's footprint and vertical span: name -> (x0, z0, x1, z1, y0, y1)."""
    out = {}
    for name, (kind, s) in solids(spec).items():
        if kind == "box":
            out[name] = (s[0], s[2], s[3], s[5], s[1], s[4])
        else:
            out[name] = (s[0], s[1], s[2], s[3], min(s[4], s[5]), max(s[6], s[7]))
    return out


def _inside(piece, x, z, spec=SPEC, margin=0.0):
    x0, z0, x1, z1, _ = pieces(spec)[piece]
    return x0 + margin <= x <= x1 - margin and z0 + margin <= z <= z1 - margin


def target_radius(target, spec=SPEC):
    return target[4] if target[4] is not None else spec["mannequin_radius"]


def check(spec=SPEC):
    """Structural rules the bake cannot see: markers on their pieces, no unplanned overlaps."""
    problems = []
    spans = _footprints(spec)
    names = list(spans)
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            ax0, az0, ax1, az1, ay0, ay1 = spans[a]
            bx0, bz0, bx1, bz1, by0, by1 = spans[b]
            if (ax0 < bx1 - 1e-6 and bx0 < ax1 - 1e-6 and az0 < bz1 - 1e-6 and bz0 < az1 - 1e-6
                    and ay0 < by1 - 1e-6 and by0 < ay1 - 1e-6):
                problems.append("%s overlaps %s" % (a, b))
    for name, (x0, z0, x1, z1, host) in spec["rails"].items():
        if not (_inside(host, x0, z0, spec) and _inside(host, x1, z1, spec)):
            problems.append("%s is not on %s" % (name, host))
    x, z, _ = spec["start"]
    if not _inside(spec["start_piece"], x, z, spec, 1.0):
        problems.append("the start is not on %s" % spec["start_piece"])
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
    return shell.course(spec["profile_id"], spec["packages"], targets, checkpoints, finish)


def bounds(spec=SPEC):
    """Map extent of all collision (x0, x1, z0, z1)."""
    spans = list(_footprints(spec).values())
    return (min(s[0] for s in spans), max(s[2] for s in spans), min(s[1] for s in spans), max(s[3] for s in spans))


def spec_corridor(spec=SPEC):
    x0, x1, z0, z1 = bounds(spec)
    highest = max(s[5] for s in _footprints(spec).values())
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
    top = spec["route"][spec["start_piece"]][4]
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
        "levels": dict(spec["levels"]),
        "pieces": {name: tuple(p) for name, p in pieces(spec).items()},
        "solids": {name: (kind, tuple(s)) for name, (kind, s) in solids(spec).items()},
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
    meshes = [shell.mesh(name, "NeoMall" + name, *shell.indexed(tris)) for name, tris in collision(spec).items()]
    x, z, yaw = spec["start"]
    top = spec["route"][spec["start_piece"]][4] + spec["spawn_body_half_height"]
    return shell.document(STAGE, spec["water_y"], meshes, [(x, top, z, yaw)], course=course(spec))


if __name__ == "__main__":
    if "--unity-shell" in sys.argv:
        json.dump(unity_shell_json(), sys.stdout, indent=1)
    else:
        json.dump(load(spec_only="--spec-only" in sys.argv), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
