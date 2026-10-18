"""Slop Pit in Unity: materials, shadows, lights and atmosphere for sloparena.stage.art-apply.

Only the fight surfaces and their tower take part in shadows; the venue is unlit StageNeon.
A lighting change needs no Blender run: see tools/blender/stagekit/presentation.py.
"""
from stagekit.presentation import CULL_BACK, LIT, NEON

# Shadow casters and receivers; build.py also records them in the stamp.
SHADOWED = ("Ring", "Ringside Floor", "Ring Tower")


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
        # Glow stays near 1.4 so lamps and neon keep their hue under the match's ACES grade.
        {"slot": "SP_Venue", **night("SlopPitVenue", 0.42, 0.5, 1.4, 1)},
        # The orange wash behind the fans: fully lit, partly through the haze.
        {"slot": "SP_Haze", **night("SlopPitHaze", 0.42, 0, 0.32, 0.55)},
    ],
    "shadows": {"cast": False, "receive": False,
                "objects": {name: {"cast": True, "receive": True} for name in SHADOWED}},
    "lights": [
        # A high, warm-white key from the south-south-west: short readable fighter shadows on the canvas.
        {"name": "Ring Key", "type": "directional", "rotation": [64, 22, 0], "color": [1, 0.92, 0.80],
         "intensity": 1.05, "shadows": "soft", "shadowStrength": 0.62},
        # The ring's overhead rig: brighter canvas in the middle, falling off towards ringside.
        {"name": "Ring Overhead", "type": "point", "position": [0, 9, 0], "color": [1, 0.86, 0.66], "intensity": 28, "range": 26},
        # Slop glow from below on the tower walls.
        {"name": "Slop Glow North", "type": "point", "position": [0, -18, 22], "color": [0.55, 1, 0.25], "intensity": 24, "range": 20},
        {"name": "Slop Glow South", "type": "point", "position": [0, -18, -22], "color": [0.55, 1, 0.25], "intensity": 24, "range": 20},
    ],
    # Fight night underground: near-black warm void, smoky orange haze that swallows the stands.
    "atmosphere": {"background": [0.035, 0.022, 0.016], "ambientSky": [0.36, 0.30, 0.26], "ambientGround": [0.10, 0.08, 0.07],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.30, 0.16, 0.07], "fogStart": 24, "fogEnd": 190},
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
