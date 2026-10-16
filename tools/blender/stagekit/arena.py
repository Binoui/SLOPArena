"""Read a baked SlopArena arena (the gameplay authority) without Unity (pure Python)."""
from __future__ import annotations

import hashlib
import struct
from pathlib import Path


def read_arena(path: Path) -> dict:
    """Parse ArenaBinaryFormat v5/v6 (see src/Shared/ArenaBinaryFormat.cs) up to the
    collision triangles; later sections (spatial grid, Arcade data) are not read."""
    b = Path(path).read_bytes()
    pos = 0

    def take(fmt):
        nonlocal pos
        v = struct.unpack_from(fmt, b, pos)
        pos += struct.calcsize(fmt)
        return v

    magic, version = take("<II")
    if magic != 0x4E455241:
        raise ValueError("%s is not an arena file" % path)

    def string():
        nonlocal pos
        (n,) = take("<I")
        s = b[pos:pos + n].decode("utf-8")
        pos += n
        return s

    name, display, color = string(), string(), string()
    (kill,) = take("<f")
    lines = take("<5f") if version >= 5 else (0.0,) * 5
    bounds = take("<4f")
    (spawn_count,) = take("<i")
    spawns = [take("<4f") for _ in range(spawn_count)]
    w, h, cell, ox, oz, n = take("<iifffi")
    pos += 4 * n
    (tri_count,) = take("<I")
    tris = [take("<9f") for _ in range(tri_count)]
    return {"name": name, "displayName": display, "version": version, "killHeight": kill,
            "killTop": lines[0], "killMinX": lines[1], "killMaxX": lines[2], "killMinZ": lines[3], "killMaxZ": lines[4],
            "bounds": bounds, "spawns": spawns, "triangles": tris}


def corridor(arena: dict) -> dict:
    """The blast box the generators keep background geometry out of."""
    return {"minX": arena["killMinX"], "maxX": arena["killMaxX"], "minZ": arena["killMinZ"],
            "maxZ": arena["killMaxZ"], "killY": arena["killHeight"], "topY": arena["killTop"]}


def sha256(path: Path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()
