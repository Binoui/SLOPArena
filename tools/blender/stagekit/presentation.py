"""Unity presentation of a generated stage: the data `sloparena.stage.art-apply` reads (pure Python).

Each stage keeps a presentation.py beside its build.py with one PRESENTATION dict:
texture imports, materials (shader properties by name), shadow flags per object, the
Stage Lights rig, atmosphere, the optional look, optional poster faces, named surface markers and the review cameras that
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

Optional `look` is what the stage gives a match beyond its art (the StageLook component on the
prefab; a stage without one gets the defaults in StageLookSettings). A stage without a generator
keeps the same object in client/Unity/Assets/Stages/<stage>/<stage>.look.json and applies it with
sloparena.stage.look-apply --stage <stage>. Every part is optional:

    "look": {
        # The picture: bloom, contrast, colour. Split toning is on when either tone is given. "tonemap" is
        # "none" (default: the brightness the stage was authored at) or "neutral" (highlights roll off
        # instead of clipping, for a stage built on glow; a bright stage comes out a few percent darker).
        "grade": {"tonemap": "neutral", "bloomThreshold": 0.9, "bloomIntensity": 0.9, "bloomScatter": 0.65, "contrast": 12, "saturation": 6,
                  "filter": [1, 0.97, 0.93], "splitShadows": [...], "splitHighlights": [...], "splitBalance": 0,
                  "vignette": 0.26, "vignetteColor": [...]},
        # A soft blur on scenery beyond the playfield, in metres from the camera. Low quality drops it.
        "farBlur": {"start": 36, "end": 90, "radius": 1.0},
        # Quieter scenery: a tint per object, by exact name or by prefix ending in '*'. A name is a
        # renderer or an object above it, and the nearest named object wins; an exact name beats a
        # prefix. art-apply rejects a name that matches nothing.
        "tints": {"Stands": [0.72, 0.70, 0.68], "City *": [0.5, 0.47, 0.58]},
        # Lamps over the playfield (at most two). Not URP lights: they light the stage's surfaces and the
        # fighters on every quality level, whatever lights the floor already carries. Fighters take at
        # most `fighters.lampCap` from all lamps together, so a strong one does not bleach them.
        "pools": [{"position": [0, 20, 0], "direction": [0, -1, 0], "color": [1, 0.9, 0.72],
                   "intensity": 430, "range": 40, "angle": 56, "innerAngle": 30}],
        # Visible shafts in the air below a lamp, and motes drifting in its light.
        "shafts": [{"top": [-3, 22, 2], "bottom": [-1.5, 1.2, 1], "topRadius": 0.7, "bottomRadius": 4.2,
                    "color": [1, 0.9, 0.7], "strength": 0.16}],
        "dust": {"count": 420, "center": [0, 7, 0], "size": [16, 11, 16], "color": [1, 0.92, 0.75, 0.7],
                 "sizeMin": 0.04, "sizeMax": 0.09},
        # Fighters on this stage: two edge lights in the stage's colours (leave both out to take them
        # from the key light), a brightness lift, the ink line (pixels at 1080p) and the soft shadow
        # under their feet.
        "fighters": {"kickA": {"color": [...], "direction": [-1, 0.25, 0.1], "strength": 1.7}, "kickB": {...},
                     "kickPower": 2.6, "lift": 0.08, "lampCap": 1.6, "ink": 1.6, "inkColor": [...],
                     "contactShadow": 0.5, "contactShadowColor": [...], "contactShadowSize": 1.5},
    }

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
LOOK_SECTIONS = ("grade", "farBlur", "tints", "pools", "shafts", "dust", "fighters")


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
    validate_look(p.get("look"))
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


def validate_look(look):
    """The shape of an optional `look`; sloparena.stage.art-apply checks every field and the tint names."""
    if look is None:
        return
    unknown = sorted(set(look) - set(LOOK_SECTIONS))
    if unknown:
        raise ValueError("look: unknown part %s; known: %s" % (", ".join(unknown), ", ".join(LOOK_SECTIONS)))
    for pattern, colour in (look.get("tints") or {}).items():
        if not pattern or pattern == "*" or len(colour) not in (3, 4):
            raise ValueError("look.tints: %r needs an object name (or prefix ending in '*') and a colour" % pattern)
    if len(look.get("pools") or []) > 2:
        raise ValueError("look.pools: a stage has at most two pools")
    fighters = look.get("fighters") or {}
    if ("kickA" in fighters) != ("kickB" in fighters):
        raise ValueError("look.fighters: give both kickA and kickB, or neither")


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
