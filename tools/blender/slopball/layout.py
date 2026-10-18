"""Slopball court shell: the approved enclosed room and the authoritative baked arena.

SPEC records the accepted solo court (docs/design/stages/slopball.design.md): a closed
40 x 40 room with a 20 m ceiling, a 1.1 m net across z = 0 and one start at z = -6.
The ball bounces off the floor, the four walls, the ceiling and the net; the fighter
stays in the player half (z < 0). The art generator never trusts SPEC alone: `load()`
reads data/arenas/slopball.arena, checks that its 18 collision triangles, start and
blast planes match SPEC, and returns the source/arena hashes for the export stamp.

Coordinates are Unity world space: +X east, +Y up, +Z north (the sender side).

Usage (outside Blender):
    python3 layout.py    layout summary as JSON
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))  # tools/blender: the shared stagekit
from stagekit import arena as baked  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/slopball/slopball.unity"
ARENA = REPO / "data/arenas/slopball.arena"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0

SPEC = {
    "half": 20.0,        # walls at x, z = +-20
    "ceiling": 20.0,
    "floor": 0.0,
    "net_half": 0.05,    # the net is a 10 cm slab across z = 0
    "net_top": 1.1,
    "spawn": (0.0, -6.0, 0.0),  # x, z, yaw degrees
    "spawn_body_half_height": 0.85,
}


def collision(spec=SPEC):
    """The baked shell as triangles (winding ignored by the comparison)."""
    h, c, f = spec["half"], spec["ceiling"], spec["floor"]
    n, t = spec["net_half"], spec["net_top"]

    def quad(a, b, c_, d):
        return [(a, b, c_), (a, c_, d)]

    tris = []
    tris += quad((-h, f, -h), (-h, f, h), (h, f, h), (h, f, -h))          # floor
    tris += quad((-h, c, -h), (h, c, -h), (h, c, h), (-h, c, h))          # ceiling
    tris += quad((-h, f, -h), (-h, c, -h), (-h, c, h), (-h, f, h))        # west
    tris += quad((h, f, -h), (h, f, h), (h, c, h), (h, c, -h))            # east
    tris += quad((-h, f, -h), (h, f, -h), (h, c, -h), (-h, c, -h))        # south
    tris += quad((-h, f, h), (-h, c, h), (h, c, h), (h, f, h))            # north
    tris += quad((-h, f, -n), (h, f, -n), (h, t, -n), (-h, t, -n))        # net, player face
    tris += quad((-h, f, n), (-h, t, n), (h, t, n), (h, f, n))            # net, sender face
    tris += quad((-h, t, -n), (h, t, -n), (h, t, n), (-h, t, n))          # net top
    return tris


def _key(t, digits=3):
    return tuple(sorted(tuple(round(v, digits) + 0.0 for v in p) for p in t))


def verify_arena(arena: dict, spec=SPEC) -> list:
    """Mismatches between the baked arena and SPEC (empty when they agree)."""
    problems = []
    expected = sorted(_key(t) for t in collision(spec))
    actual = sorted(_key((t[0:3], t[3:6], t[6:9])) for t in arena["triangles"])
    if expected != actual:
        problems.append("baked collision (%d triangles) differs from the spec room and net (%d)"
                        % (len(actual), len(expected)))
    h = spec["half"]
    for key, value in (("killMinX", -h - SIDE_BLAST_MARGIN), ("killMaxX", h + SIDE_BLAST_MARGIN),
                       ("killMinZ", -h - SIDE_BLAST_MARGIN), ("killMaxZ", h + SIDE_BLAST_MARGIN)):
        if abs(arena[key] - value) > 1e-3:
            problems.append("%s %.3f differs from spec %.3f" % (key, arena[key], value))
    if len(arena["spawns"]) != 1:
        problems.append("baked arena has %d starts, the court has one" % len(arena["spawns"]))
    else:
        x, y, z, yaw = arena["spawns"][0]
        sx, sz, syaw = spec["spawn"]
        if abs(x - sx) > 1e-3 or abs(z - sz) > 1e-3 or abs(y - spec["floor"] - spec["spawn_body_half_height"]) > 1e-3:
            problems.append("Spawn_01 at (%.2f, %.2f, %.2f) differs from spec" % (x, y, z))
        if abs((math.degrees(yaw) - syaw + 180) % 360 - 180) > 0.05:
            problems.append("Spawn_01 yaw differs from spec")
    return problems


def load(spec=SPEC) -> dict:
    """Layout for the art generator; requires the baked arena to match SPEC."""
    arena = baked.read_arena(ARENA)
    problems = verify_arena(arena, spec)
    if problems:
        raise ValueError("Baked slopball.arena does not match the layout spec:\n  " + "\n  ".join(problems))
    return {
        "spec": spec,
        "source": str(SOURCE_SCENE.relative_to(REPO)),
        "sourceSha256": baked.sha256(SOURCE_SCENE),
        "arena": str(ARENA.relative_to(REPO)),
        "arenaSha256": baked.sha256(ARENA),
    }


if __name__ == "__main__":
    json.dump(load(), sys.stdout, indent=1, default=list)
    sys.stdout.write("\n")
