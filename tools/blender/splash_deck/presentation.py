"""Splash Deck in Unity: materials, shadows, lights and atmosphere for sloparena.stage.art-apply.

Sky, distant or emissive objects neither cast nor receive shadows; the sea only receives.
The sun shines from bay.py's sun disc. A lighting change needs no Blender run: see
tools/blender/stagekit/presentation.py.
"""
from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON, WATER

import bay as BAY

UNSHADOWED = ("Sky", "Clouds", "Gulls", "Far Islands", "Lighthouse Lamp")


def lit(asset, base_map, tint, detail_strength, detail_scale, wrap, specular, wind, wind_frequency, cull):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [tint, tint, tint, 1],
            "textures": {"_DetailMap": "SplashDeckDetail.png"},
            "floats": {"_DetailScale": detail_scale, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": specular,
                       "_Gloss": 28, "_Emission": 0, "_Wind": wind, "_WindFrequency": wind_frequency, "_Cull": cull,
                       "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.66, 0.72, 0.86]},
            "vectors": {"_WindDirection": [-1, 0, 0.3, 0]},  # the sea breeze blows ashore, westwards
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def neon(asset, base_level, ambient, shade, glow, fog, cull):
    return {"asset": asset, "shader": NEON, "baseMap": "SplashDeckAtlas.png", "baseColor": [1, 1, 1, 1],
            "floats": {"_Base": base_level, "_Ambient": ambient, "_Shade": shade, "_Glow": glow, "_FogAmount": fog,
                       "_TimeScale": 1, "_Cull": cull},
            "vectors": {"_ShadeDirection": [0.6, 0.75, -0.25, 0]}}


PRESENTATION = {
    "stage": "splash_deck",
    "folder": "Assets/Stages/splash_deck/Presentation/Harbour",
    "model": "SplashDeckHarbour.fbx",
    "stamp": "SplashDeckHarbour.layout.json",
    "root": "Splash Deck Harbour",
    # Roots of the previous moored-barge pass (vendor tropical port, trims, preview boardwalk).
    "supersededRoots": ["Playable Geometry", "North Trim", "South Trim", "East Trim", "West Trim", "Spawn Points",
                        "Distant tropical port", "Boardwalk Surface (preview)", "Authoritative recovery hull presentation",
                        "Barge warm deck fill"],
    # The previous pass kept a light on the prefab root itself.
    "removeRootLights": True,
    "textures": [
        {"file": "SplashDeckDeck.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "SplashDeckHull.png", "srgb": True, "wrap": "clamp", "maxSize": 8192, "aniso": 8},
        {"file": "SplashDeckAtlas.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "SplashDeckDetail.png", "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
        {"file": "SplashDeckRipples.png", "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "SD_Deck", **lit("SplashDeckDeck", "SplashDeckDeck.png", 0.88, 0.16, 0.45, 0.3, 0.12, 0, 1, CULL_BACK)},
        {"slot": "SD_Hull", **lit("SplashDeckHull", "SplashDeckHull.png", 0.9, 0.2, 0.45, 0.35, 0.25, 0, 1, CULL_BACK)},
        {"slot": "SD_Wood", **lit("SplashDeckWood", "SplashDeckAtlas.png", 0.9, 0.25, 0.45, 0.35, 0.1, 0, 1, CULL_BACK)},
        {"slot": "SD_Land", **lit("SplashDeckLand", "SplashDeckAtlas.png", 0.82, 0.4, 0.25, 0.35, 0, 0, 1, CULL_BACK)},
        {"slot": "SD_Foliage", **lit("SplashDeckFoliage", "SplashDeckAtlas.png", 0.85, 0.1, 0.45, 0.5, 0, 0.3, 1.1, CULL_OFF)},
        {"slot": "SD_Cloth", **lit("SplashDeckCloth", "SplashDeckAtlas.png", 0.9, 0.05, 0.45, 0.5, 0, 0.22, 1.7, CULL_OFF)},
        {"slot": "SD_Water", "asset": "SplashDeckWater", "shader": WATER, "baseColor": [1, 1, 1, 1],
         "textures": {"_RippleMap": "SplashDeckRipples.png"},
         "colors": {"_SkyColor": [0.40, 0.62, 0.90], "_HorizonColor": [0.80, 0.88, 0.94]}},
        # Gulls and the lighthouse lamp: softly shaded, lightly glowing where vertex alpha marks it.
        {"slot": "SD_Glow", **neon("SplashDeckGlow", 0.85, 0.55, 0.35, 1.2, 1, CULL_OFF)},
        {"slot": "SD_Sky", **neon("SplashDeckSky", 1, 0, 0, 1.4, 0, CULL_OFF)},
    ],
    "shadows": {"cast": True, "receive": True, "objects": {
        **{name: {"cast": False, "receive": False} for name in UNSHADOWED},
        "Sea": {"cast": False, "receive": True},
    }},
    "lights": [
        # Late-morning sun from the east-south-east, on the bearing of the sky's sun disc.
        {"name": "Morning Sun", "type": "directional", "sun": {"azimuth": BAY.SUN_AZIMUTH, "elevation": BAY.SUN_ELEVATION},
         "color": [1, 0.96, 0.88], "intensity": 1.3, "shadows": "soft", "shadowStrength": 0.7},
        # Soft sky bounce over the deck keeps the shaded side of fighters readable.
        {"name": "Deck Sky Fill", "type": "point", "position": [2.5, 24, -6], "color": [0.82, 0.9, 1], "intensity": 0.55, "range": 60},
    ],
    # Clear morning: blue sky light, warm ground bounce, pale sea haze towards the horizon.
    "atmosphere": {"background": [0.62, 0.78, 0.94], "ambientSky": [0.70, 0.80, 0.95], "ambientGround": [0.56, 0.50, 0.42],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.80, 0.87, 0.92], "fogStart": 170, "fogEnd": 1500},
}
