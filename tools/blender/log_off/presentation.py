"""Log Off in Unity: materials, shadows, lights and atmosphere for sloparena.stage.art-apply.

Distant or emissive objects neither cast nor receive shadows; the camp lights sit where
camp.py builds the fire, lantern and tent. A lighting change needs no Blender run: see
tools/blender/stagekit/presentation.py.
"""
from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON

import camp as CAMP

UNSHADOWED = ("Sky", "Fireflies", "Far Forest", "Camp Glow", "Canopy")


def lit(asset, base_map, detail_strength, wrap, wind, cull):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [1, 1, 1, 1],
            "textures": {"_DetailMap": "LogOffDetail.png"},
            "floats": {"_DetailScale": 0.45, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": 0,
                       "_Emission": 0, "_Wind": wind, "_WindFrequency": 1.3, "_Cull": cull, "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.62, 0.56, 0.78]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


PRESENTATION = {
    "stage": "log_off",
    "folder": "Assets/Stages/log_off/Presentation/Campsite",
    "model": "LogOffCampsite.fbx",
    "stamp": "LogOffCampsite.layout.json",
    "root": "Log Off Campsite",
    # Roots of the previous woodland pass (vendor trees, hideout); the campsite replaces them.
    "supersededRoots": ["ApprovedStump", "WoodlandWorld", "WarmAfternoonSun"],
    "textures": [
        {"file": "LogOffStumpTop.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "LogOffAtlas.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "LogOffDetail.png", "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "LO_Wood", **lit("LogOffWood", "LogOffAtlas.png", 0.3, 0.35, 0, CULL_BACK)},
        {"slot": "LO_Stump", **lit("LogOffStumpTop", "LogOffStumpTop.png", 0.18, 0.45, 0, CULL_BACK)},
        {"slot": "LO_Foliage", **lit("LogOffFoliage", "LogOffAtlas.png", 0.1, 0.5, 0.8, CULL_OFF)},
        {"slot": "LO_Cloth", **lit("LogOffCloth", "LogOffAtlas.png", 0.12, 0.5, 0.35, CULL_OFF)},
        # Glow stays modest so fire and fireflies keep their hue under the match's bloom.
        {"slot": "LO_Glow", "asset": "LogOffGlow", "shader": NEON, "baseMap": "LogOffAtlas.png", "baseColor": [1, 1, 1, 1],
         "floats": {"_Base": 0.6, "_Ambient": 0.4, "_Shade": 0.2, "_Glow": 1.6, "_FogAmount": 0.6, "_TimeScale": 1, "_Cull": CULL_BACK},
         "vectors": {"_ShadeDirection": [-0.35, 0.8, -0.2, 0]}},
        {"slot": "LO_Sky", "asset": "LogOffSky", "shader": NEON, "baseMap": "LogOffAtlas.png", "baseColor": [1, 1, 1, 1],
         "floats": {"_Base": 1, "_Ambient": 0, "_Shade": 0, "_Glow": 1.2, "_FogAmount": 0, "_TimeScale": 1, "_Cull": CULL_OFF}},
    ],
    "shadows": {"cast": True, "receive": True,
                "objects": {name: {"cast": False, "receive": False} for name in UNSHADOWED}},
    "lights": [
        # Low warm sun from the west-south-west (the sky's sun sits on the same bearing).
        {"name": "Sunset Sun", "type": "directional", "sun": {"azimuth": 252, "elevation": 22}, "color": [1, 0.71, 0.47],
         "intensity": 1.25, "shadows": "soft", "shadowStrength": 0.55},
        {"name": "Campfire", "type": "point", "position": list(CAMP.LIGHTS["campfire"]), "color": [1, 0.55, 0.22], "intensity": 6, "range": 190},
        {"name": "Lantern", "type": "point", "position": list(CAMP.LIGHTS["lantern"]), "color": [1, 0.8, 0.45], "intensity": 3, "range": 70},
        {"name": "Tent Glow", "type": "point", "position": list(CAMP.LIGHTS["tent"]), "color": [1, 0.65, 0.35], "intensity": 2, "range": 55},
        # Keeps the fight surface warm and readable while the sun grazes it.
        {"name": "Stump Fill", "type": "point", "position": [0, 26, -4], "color": [1, 0.88, 0.74], "intensity": 0.9, "range": 50},
    ],
    # Dusk under the canopy: lavender sky light, warm earth bounce, rosy haze in the far trunks.
    "atmosphere": {"background": [0.62, 0.50, 0.56], "ambientSky": [0.52, 0.46, 0.64], "ambientGround": [0.22, 0.16, 0.11],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.66, 0.50, 0.50], "fogStart": 90, "fogEnd": 720},
}

# The stage's look (owner 2026-11-11: every stage gets one). Fields: stagekit/presentation.py.
# Dusk: the fire and the fireflies glow, the forest steps back behind the stump.
FOREST = [0.74, 0.70, 0.76]
PRESENTATION["look"] = {
    "grade": {"tonemap": "neutral", "bloomThreshold": 0.9, "bloomIntensity": 1.0, "bloomScatter": 0.68, "contrast": 10,
              "saturation": 6, "splitShadows": [0.46, 0.42, 0.58], "splitHighlights": [0.62, 0.52, 0.42], "splitBalance": 0,
              "vignette": 0.24, "vignetteColor": [0.05, 0.03, 0.04]},
    "farBlur": {"start": 60, "end": 240, "radius": 1.0},
    "tints": {"Far Forest": FOREST, "Trees": FOREST, "Canopy": FOREST, "Forest Floor": FOREST, "Undergrowth": FOREST,
              "Camp": [0.88, 0.85, 0.86], "Sky": [0.94, 0.92, 0.94]},
    # The low sun on one edge, the violet sky on the other.
    "fighters": {"kickA": {"color": [1.0, 0.66, 0.38], "direction": [-0.88, 0.37, -0.29], "strength": 1.5},
                 "kickB": {"color": [0.58, 0.56, 1.0], "direction": [0.85, 0.35, 0.3], "strength": 1.1},
                 "kickPower": 2.6, "lift": 0.05, "contactShadow": 0.4, "contactShadowColor": [0.12, 0.07, 0.06]},
}
