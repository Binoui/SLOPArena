"""Locked Manki Mayhem shell, emitted for Unity and checked against the actual bake.

Unity coordinates: +X east, +Y up, +Z north. The first two spawns are diagonal
opposites; all face the centre. Art export requires this exact outward-wound shell.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from stagekit import arena as baked, shell

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/manki_mayhem/manki_mayhem.unity"
ARENA = REPO / "data/arenas/manki_mayhem.arena"
SIDE_BLAST_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0
SPEC = {
    "top": 0.0, "kill_y": -11.0,
    "floor": [(-12.0, -10.0), (12.0, -10.0), (12.0, 10.0), (-12.0, 10.0)],
    "spawns": [(x, z, math.degrees(math.atan2(-x, -z)) % 360)
               for x, z in [(-5.0, -4.0), (5.0, 4.0), (-5.0, 4.0), (5.0, -4.0)]],
    "spawn_body_half_height": 0.85,
}


def collision(spec=SPEC):
    """Closed rectangular prism: two top, two bottom, eight vertical triangles."""
    outline, top, bottom = spec["floor"], spec["top"], spec["kill_y"]
    tris = []
    for k in (1, 2):
        a, b, c = outline[0], outline[k], outline[k + 1]
        tris.extend([((a[0], top, a[1]), (c[0], top, c[1]), (b[0], top, b[1])),
                     ((a[0], bottom, a[1]), (b[0], bottom, b[1]), (c[0], bottom, c[1]))])
    for i, (ax, az) in enumerate(outline):
        bx, bz = outline[(i + 1) % len(outline)]
        tris.extend([((ax, top, az), (bx, top, bz), (bx, bottom, bz)),
                     ((ax, top, az), (bx, bottom, bz), (ax, bottom, az))])
    return {"Deck": tris}


def spec_corridor(spec=SPEC):
    xs, zs = zip(*spec["floor"])
    return {"minX": min(xs) - SIDE_BLAST_MARGIN, "maxX": max(xs) + SIDE_BLAST_MARGIN,
            "minZ": min(zs) - SIDE_BLAST_MARGIN, "maxZ": max(zs) + SIDE_BLAST_MARGIN,
            "killY": spec["kill_y"], "topY": spec["top"] + TOP_BLAST_MARGIN}


def _canonical(triangle):
    points = [tuple(round(v, 3) + 0.0 for v in p) for p in triangle]
    start = min(range(3), key=lambda i: points[i])
    return tuple(points[(start + i) % 3] for i in range(3))


def verify_arena(arena, spec=SPEC):
    problems = []
    expected = sorted(_canonical(t) for ts in collision(spec).values() for t in ts)
    actual = sorted(_canonical((t[:3], t[3:6], t[6:9])) for t in arena["triangles"])
    if actual != expected:
        problems.append("collision differs from the 12 outward-wound rectangular-prism triangles")
    c = spec_corridor(spec)
    for key, want in [("killHeight", c["killY"]), ("killTop", c["topY"]),
                      ("killMinX", c["minX"]), ("killMaxX", c["maxX"]),
                      ("killMinZ", c["minZ"]), ("killMaxZ", c["maxZ"])]:
        if abs(arena[key] - want) > 0.001:
            problems.append("%s differs: %s != %s" % (key, arena[key], want))
    if len(arena["spawns"]) != 4:
        problems.append("expected exactly four spawns")
    for i, ((x, y, z, yaw), (sx, sz, syaw)) in enumerate(zip(arena["spawns"], spec["spawns"])):
        if max(abs(x - sx), abs(y - spec["top"] - spec["spawn_body_half_height"]), abs(z - sz)) > 0.001:
            problems.append("Spawn_%02d position differs" % (i + 1))
        if abs((math.degrees(yaw) - syaw + 180) % 360 - 180) > 0.05:
            problems.append("Spawn_%02d does not face the centre" % (i + 1))
    return problems


def load(spec_only=False, spec=SPEC):
    data = {"spec": spec, "top": spec["top"], "killY": spec["kill_y"], "floor": spec["floor"],
            "spawns": spec["spawns"], "specOnly": spec_only, "corridor": spec_corridor(spec),
            "source": None, "sourceSha256": None, "arena": None, "arenaSha256": None}
    if not spec_only:
        arena = baked.read_arena(ARENA)
        problems = verify_arena(arena, spec)
        if problems:
            raise ValueError("Baked manki_mayhem.arena does not match SPEC:\n  " + "\n  ".join(problems))
        data.update(corridor=baked.corridor(arena), source=str(SOURCE_SCENE.relative_to(REPO)),
                    sourceSha256=baked.sha256(SOURCE_SCENE), arena=str(ARENA.relative_to(REPO)),
                    arenaSha256=baked.sha256(ARENA))
    return data


def unity_shell_json(spec=SPEC):
    meshes = [shell.mesh(name, "MankiMayhemDeck", *shell.indexed(tris))
              for name, tris in collision(spec).items()]
    document = shell.document("manki_mayhem", spec["kill_y"], meshes,
                          [(x, spec["top"] + spec["spawn_body_half_height"], z, yaw)
                           for x, z, yaw in spec["spawns"]])
    document["mayhem"] = {
        "profileId": "manki_mayhem", "gameplayRevision": 1,
        "hostOrigin": [0, 6.5, 41.5], "playAreaMin": [-12, 0, -10],
        "playAreaMax": [12, 0, 10], "deckTop": spec["top"],
    }
    return document


if __name__ == "__main__":
    json.dump(unity_shell_json() if "--unity-shell" in sys.argv else load("--spec-only" in sys.argv),
              sys.stdout, indent=1)
    print()
