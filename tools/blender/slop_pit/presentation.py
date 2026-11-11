"""Slop Pit in Unity: materials, shadows, lights and atmosphere for sloparena.stage.art-apply.

Only the fight surfaces and their tower take part in shadows; the venue is unlit StageNeon.
The look puts the ring under its own lamp: the stands go quieter and the canvas brighter.
A lighting or look change needs no Blender run: see tools/blender/stagekit/presentation.py.
"""
from stagekit.presentation import CULL_BACK, LIT, NEON

# Shadow casters and receivers; build.py also records them in the stamp.
SHADOWED = ("Ring", "Ringside Floor", "Ring Tower")

# The venue around the ring, a step quieter than the fight.
QUIET = [0.72, 0.70, 0.68]
LAMP_LIGHT = [1.0, 0.9, 0.7]


def lit(asset, base_map, detail_strength, wrap, specular):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [1, 1, 1, 1],
            "textures": {"_DetailMap": "SlopPitDetail.png"},
            "floats": {"_DetailScale": 0.5, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": specular,
                       "_Gloss": 22, "_Emission": 0, "_Wind": 0, "_Cull": CULL_BACK, "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.62, 0.55, 0.52]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def night(asset, base_level, ambient, glow, fog):
    return {"asset": asset, "shader": NEON, "baseMap": "SlopPitAtlas.png", "baseColor": [1, 1, 1, 1],
            "floats": {"_Base": base_level, "_Ambient": ambient, "_Shade": 0.35, "_Glow": glow, "_FogAmount": fog,
                       "_TimeScale": 1, "_Cull": CULL_BACK},
            "vectors": {"_ShadeDirection": [0.2, 0.85, -0.3, 0]}}


PRESENTATION = {
    "stage": "slop_pit",
    "folder": "Assets/Stages/slop_pit/Presentation/Ring",
    "model": "SlopPitArena.fbx",
    "stamp": "SlopPitArena.layout.json",
    "root": "Slop Pit Fight Ring",
    # Roots of the previous hand-built factory pass (vendor scaffolds, conveyors, scrap); the ring replaces them.
    "supersededRoots": ["Platform_A", "Platform_B", "ArtPass01", "PitScrapFill", "ConveyorSilhouettes",
                        "DeckEdgeLighting", "PerimeterWalls", "MainDeck", "SparseScrap", "BlockoutFill"],
    "textures": [
        {"file": "SlopPitAtlas.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "SlopPitCanvas.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "SlopPitFloor.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "SlopPitDetail.png", "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "SP_Lit", **lit("SlopPitLit", "SlopPitAtlas.png", 0.28, 0.3, 0.12)},
        {"slot": "SP_Floor", **lit("SlopPitFloor", "SlopPitFloor.png", 0.3, 0.3, 0.18)},
        {"slot": "SP_Canvas", **lit("SlopPitCanvas", "SlopPitCanvas.png", 0.12, 0.35, 0.05)},
        # Glow stays near 1.4 so lamps and neon keep their hue under the match's bloom.
        {"slot": "SP_Venue", **night("SlopPitVenue", 0.2, 0.2, 1.4, 1)},
        # The orange wash behind the fans: fully lit, partly through the haze.
        {"slot": "SP_Haze", **night("SlopPitHaze", 0.42, 0, 2.2, 1)},
    ],
    "shadows": {"cast": False, "receive": False,
                "objects": {name: {"cast": True, "receive": True} for name in SHADOWED}},
    "lights": [
        # A high, warm-white key from the south-south-west: short readable fighter shadows on the canvas.
        # Key and overhead rig are kept low: the ring lamp of the look carries the canvas.
        {"name": "Ring Key", "type": "directional", "rotation": [64, 22, 0], "color": [1, 0.92, 0.80],
         "intensity": 0.79, "shadows": "soft", "shadowStrength": 0.62},
        # The ring's overhead rig: brighter canvas in the middle, falling off towards ringside.
        {"name": "Ring Overhead", "type": "point", "position": [0, 9, 0], "color": [1, 0.86, 0.66], "intensity": 16.8, "range": 26},
        # Slop glow from below on the tower walls.
        {"name": "Slop Glow North", "type": "point", "position": [0, -18, 22], "color": [0.55, 1, 0.25], "intensity": 24, "range": 20},
        {"name": "Slop Glow South", "type": "point", "position": [0, -18, -22], "color": [0.55, 1, 0.25], "intensity": 24, "range": 20},
    ],
    # Fight night underground: near-black warm void, smoky orange haze that swallows the stands.
    "atmosphere": {"background": [0.035, 0.022, 0.016], "ambientSky": [0.324, 0.27, 0.234], "ambientGround": [0.09, 0.072, 0.063],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.14, 0.085, 0.05], "fogStart": 24, "fogEnd": 130},
    # One lamp high over the ring (owner pick 2026-11-10): a pool on the canvas, shafts and dust in the air,
    # the venue quieter and softly out of focus behind it.
    "look": {
        "grade": {"tonemap": "neutral", "bloomThreshold": 0.9, "bloomIntensity": 0.9, "bloomScatter": 0.65, "contrast": 12, "saturation": 6,
                  "filter": [1.0, 0.97, 0.93], "splitShadows": [0.50, 0.47, 0.44], "splitHighlights": [0.56, 0.52, 0.46],
                  "splitBalance": 0, "vignette": 0.26, "vignetteColor": [0.03, 0.02, 0.01]},
        "farBlur": {"start": 36, "end": 90, "radius": 1.0},
        "tints": {"Stands": QUIET, "Crowd": QUIET, "Fences": QUIET, "Banners": QUIET, "Pit": QUIET,
                  "Venue Glow": [0.9, 0.9, 0.9], "Haze": [0.85, 0.85, 0.85], "Ringside Floor": [0.95, 0.93, 0.91]},
        "pools": [{"position": [0, 20, 0], "direction": [0, -1, 0], "color": [1.0, 0.9, 0.72],
                   "intensity": 430, "range": 40, "angle": 56, "innerAngle": 30}],
        "shafts": [
            {"top": [-3, 22, 2], "bottom": [-1.5, 1.2, 1], "topRadius": 0.7, "bottomRadius": 4.2, "color": LAMP_LIGHT, "strength": 0.16},
            {"top": [3.5, 22, -1], "bottom": [2, 1.2, -0.5], "topRadius": 0.7, "bottomRadius": 4.4, "color": LAMP_LIGHT, "strength": 0.14},
            {"top": [0.5, 22, 5], "bottom": [0.5, 1.2, 3.5], "topRadius": 0.7, "bottomRadius": 4.0, "color": LAMP_LIGHT, "strength": 0.12},
        ],
        "dust": {"count": 420, "center": [0, 7, 0], "size": [16, 11, 16], "color": [1.0, 0.92, 0.75, 0.7],
                 "sizeMin": 0.04, "sizeMax": 0.09},
        # Warm lamp light on the top edges, slop green from below; the lamp never bleaches a fighter.
        "fighters": {"kickA": {"color": [1.0, 0.84, 0.58], "direction": [-0.4, 1, -0.3], "strength": 1.5},
                     "kickB": {"color": [0.55, 1.0, 0.35], "direction": [0.8, -0.1, 0.6], "strength": 1.0},
                     "kickPower": 2.6, "lift": 0.06, "lampCap": 1.0,
                     "contactShadow": 0.45, "contactShadowColor": [0.10, 0.06, 0.03]},
    },
    # Review poses for sloparena.stage.views.cameras; "thumbnail" is the stage-select card.
    "cameras": [
        {"name": "thumbnail", "position": [-27, 16, -31], "target": [0, -1.5, 2.5], "fov": 40},
        {"name": "ringside", "position": [2, 1.75, -14.5], "target": [0, 1.9, 0], "fov": 50},
        {"name": "recovery-west", "position": [-27, -3.5, 9], "target": [-16, -6.5, 0], "fov": 52},
        {"name": "recovery-corner", "position": [25, -2, -25], "target": [13.5, -6, -13.5], "fov": 52},
        {"name": "pit-down", "position": [-3, 5, -21], "target": [2, -22, -31], "fov": 55},
        {"name": "hero-low", "position": [0, 3.2, -9], "target": [0, 4.5, 48], "fov": 42},
    ],
}
