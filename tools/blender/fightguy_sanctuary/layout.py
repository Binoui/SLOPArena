"""Read the authoritative FightGuy Sanctuary source scene for presentation generation.

The collision source scene owns every playable surface and course marker. This
reader extracts those transforms from Unity's text YAML so the Blender art fits
the current shell exactly. It never writes the scene and never reads the cosmetic
prefab. Coordinates are Unity world space: +X east, +Y up, +Z north.

Usage (outside Blender):  python3 layout.py [scene.unity] > layout.json
"""
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/fightguy_sanctuary/fightguy_sanctuary.unity"
ROOT_NAME = "Stage_fightguy_sanctuary"
# Must match ArenaCollision's derived blast margins (see the stage-design skill).
SIDE_BLAST_MARGIN = 10.0
KILL_MARGIN = 10.0
TOP_BLAST_MARGIN = 20.0

_DOC = re.compile(r"^--- !u!(\d+) &(-?\d+)")
_VEC = re.compile(r"\{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)(?:, w: ([-\d.eE]+))?\}")
_REF = re.compile(r"\{fileID: (-?\d+)\}")


def _documents(text: str):
    current = None
    for line in text.splitlines():
        match = _DOC.match(line)
        if match:
            if current:
                yield current
            current = {"class": int(match.group(1)), "id": match.group(2), "lines": []}
        elif current is not None:
            current["lines"].append(line)
    if current:
        yield current


def _field(lines, name):
    prefix = "  " + name + ":"
    for line in lines:
        if line.startswith(prefix):
            return line[len(prefix):].strip()
    return None


def _vector(value):
    match = _VEC.search(value or "")
    if not match:
        raise ValueError("Unreadable vector: %r" % value)
    return [float(match.group(i)) for i in range(1, 5) if match.group(i) is not None]


def load(scene: Path = SOURCE_SCENE) -> dict:
    text = scene.read_text(encoding="utf-8")
    names, transforms = {}, {}
    for doc in _documents(text):
        if doc["class"] == 1:
            names[doc["id"]] = (_field(doc["lines"], "m_Name") or "").strip()
        elif doc["class"] == 4:
            transforms[doc["id"]] = {
                "go": _REF.search(_field(doc["lines"], "m_GameObject")).group(1),
                "parent": _REF.search(_field(doc["lines"], "m_Father")).group(1),
                "position": _vector(_field(doc["lines"], "m_LocalPosition")),
                "rotation": _vector(_field(doc["lines"], "m_LocalRotation")),
                "scale": _vector(_field(doc["lines"], "m_LocalScale")),
            }
    by_name = {}
    for tid, t in transforms.items():
        t["name"] = names.get(t["go"], "")
        by_name.setdefault(t["name"], []).append(tid)

    def single(name):
        ids = by_name.get(name, [])
        if len(ids) != 1:
            raise ValueError("Expected exactly one %r transform, found %d" % (name, len(ids)))
        return ids[0]

    root = transforms[single(ROOT_NAME)]
    if root["position"] != [0, 0, 0] or root["rotation"] != [0, 0, 0, 1] or root["scale"] != [1, 1, 1]:
        raise ValueError("Source root must keep the identity transform.")

    def children(parent_name):
        pid = single(parent_name)
        return sorted((t for t in transforms.values() if t["parent"] == pid), key=lambda t: t["name"])

    surfaces = []
    for t in children("GameplayGeometry"):
        if any(abs(a - b) > 1e-6 for a, b in zip(t["rotation"], [0, 0, 0, 1])):
            raise ValueError("Surface %r is rotated; the art generator expects axis-aligned boxes." % t["name"])
        (x, y, z), (sx, sy, sz) = t["position"], t["scale"]
        surfaces.append({
            "name": t["name"], "center": [x, y, z], "size": [sx, sy, sz],
            "min": [x - sx / 2, y - sy / 2, z - sz / 2], "max": [x + sx / 2, y + sy / 2, z + sz / 2],
            "top": y + sy / 2,
        })
    markers = {t["name"]: t["position"] for t in children("AuthoringAids")}
    markers.update({t["name"]: t["position"] for t in children("SpawnPoints")})

    lo = [min(s["min"][i] for s in surfaces) for i in range(3)]
    hi = [max(s["max"][i] for s in surfaces) for i in range(3)]
    corridor = {
        "minX": lo[0] - SIDE_BLAST_MARGIN, "maxX": hi[0] + SIDE_BLAST_MARGIN,
        "minZ": lo[2] - SIDE_BLAST_MARGIN, "maxZ": hi[2] + SIDE_BLAST_MARGIN,
        "killY": lo[1] - KILL_MARGIN, "topY": max(s["top"] for s in surfaces) + TOP_BLAST_MARGIN,
    }
    return {
        "source": str(scene.relative_to(REPO)) if scene.is_relative_to(REPO) else str(scene),
        "sourceSha256": hashlib.sha256(scene.read_bytes()).hexdigest(),
        "surfaces": surfaces, "markers": markers, "corridor": corridor,
    }


if __name__ == "__main__":
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else SOURCE_SCENE
    json.dump(load(path), sys.stdout, indent=1)
    sys.stdout.write("\n")
