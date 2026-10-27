"""Unity presentation of a generated stage: the data `sloparena.stage.art-apply` reads (pure Python).

Each stage keeps a presentation.py beside its build.py with one PRESENTATION dict:
texture imports, materials (shader properties by name), shadow flags per object, the
Stage Lights rig, atmosphere, optional poster faces, named surface markers and the review cameras that
sloparena.stage.views.cameras captures ("thumbnail" is the stage-select card). build.py
writes it on export as
<Stem>.presentation.json next to the FBX. A lighting or material change needs no Blender
run and no C#: write the JSON alone, then apply it from that directory.

    python3 tools/blender/stagekit/presentation.py <stage> --out /tmp/<stage>-generated
    bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \\
        sloparena.stage.art-apply --stage <stage> --from /tmp/<stage>-generated

Optional `surfaces` name direct-child Transform markers with Unity `position`, Euler
`rotation` and positive `size` (width, height). They add no meshes or collision.
Runtime presenters use their transforms to place content on existing art.

Colours are Unity Color values (0..1, alpha optional); rotations are Euler degrees; a
directional light may give `sun` (azimuth clockwise from north, elevation) instead, to
match the sky's sun disc. Shadow overrides name objects of the FBX and give both flags.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
import os
import re
import sys

FORMAT = 1
LIT = "SLOPArena/StageVertexLit"
NEON = "SLOPArena/StageNeon"
WATER = "SLOPArena/StageWater"
CULL_OFF, CULL_FRONT, CULL_BACK = 0.0, 1.0, 2.0
LIGHT_TYPES = ("directional", "point", "spot")


def stem(presentation):
    return os.path.splitext(presentation["model"])[0]


def validate(p):
    """Raise ValueError for a presentation sloparena.stage.art-apply would reject."""
    for key in ("stage", "folder", "model", "stamp", "root", "textures", "materials", "shadows", "lights", "atmosphere"):
        if key not in p:
            raise ValueError("presentation is missing %r" % key)
    prefix = "Assets/Stages/%s/Presentation/" % p["stage"]
    if not p["folder"].startswith(prefix) or ".." in p["folder"]:
        raise ValueError("folder must be below " + prefix)
    textures = {t["file"] for t in p["textures"]}
    posters = p.get("posters") or []
    for m in p["materials"] + [poster["material"] for poster in posters]:
        for texture in [m.get("baseMap")] + list((m.get("textures") or {}).values()):
            if texture is not None and texture not in textures:
                raise ValueError("material %s uses %s, which textures does not list" % (m.get("slot") or m["asset"], texture))
    for t in p["textures"]:
        if t.get("wrap", "clamp") not in ("clamp", "repeat"):
            raise ValueError("texture %s: wrap must be clamp or repeat" % t["file"])
    names = [light["name"] for light in p["lights"]]
    if len(names) != len(set(names)):
        raise ValueError("stage light names must be unique")
    for light in p["lights"]:
        if light.get("type", "point") not in LIGHT_TYPES:
            raise ValueError("light %s: type must be one of %s" % (light["name"], ", ".join(LIGHT_TYPES)))
    for name, own in (p["shadows"].get("objects") or {}).items():
        if set(own) != {"cast", "receive"}:
            raise ValueError("shadow override %s must give both cast and receive" % name)
    cameras = [c["name"] for c in p.get("cameras") or []]
    if len(cameras) != len(set(cameras)) or not all(re.fullmatch(r"[a-z0-9-]{1,40}", c) for c in cameras):
        raise ValueError("review camera names must be unique lowercase words joined by '-'")
    for c in p.get("cameras") or []:
        if len(c.get("position", ())) != 3 or len(c.get("target", ())) != 3:
            raise ValueError("review camera %s needs a position and a target" % c["name"])
    surfaces = p.get("surfaces") or []
    if len({s["name"] for s in surfaces}) != len(surfaces):
        raise ValueError("surface names must be unique")
    for s in surfaces:
        if not s.get("name") or "/" in s["name"]:
            raise ValueError("surface names must name direct children")
        for field, length in (("position", 3), ("rotation", 3), ("size", 2)):
            values = s.get(field, [])
            if len(values) != length or not all(math.isfinite(v) for v in values):
                raise ValueError("surface %s needs finite %s" % (s["name"], field))
        if not all(v > 0 for v in s["size"]):
            raise ValueError("surface sizes must be positive")


def document(p):
    """The JSON document: format version first, then the stage's own data."""
    validate(p)
    return {"format": FORMAT, **p}


def write(p, out):
    """Write <Stem>.presentation.json into `out`; returns its path."""
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, stem(p) + ".presentation.json")
    with open(path, "w") as fh:
        json.dump(document(p), fh, indent=1)
        fh.write("\n")
    return path


def load_stage(stage):
    """Import tools/blender/<stage>/presentation.py the way its build.py would."""
    blender = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    folder = os.path.join(blender, stage)
    module_path = os.path.join(folder, "presentation.py")
    if not os.path.isfile(module_path):
        raise SystemExit("No presentation.py in tools/blender/%s" % stage)
    for path in (blender, folder):
        if path not in sys.path:
            sys.path.insert(0, path)
    spec = importlib.util.spec_from_file_location(stage + "_presentation", module_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.PRESENTATION


def main(argv=None):
    parser = argparse.ArgumentParser(description="Write a stage's <Stem>.presentation.json without Blender.")
    parser.add_argument("stage")
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)
    print(write(load_stage(args.stage), args.out))


if __name__ == "__main__":
    main()
