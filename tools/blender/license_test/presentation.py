"""Fighter's License in Unity: materials, shadows, lights, atmosphere and review cameras for
sloparena.stage.art-apply.

A bright afternoon: the sun stands high over the open east edge (city.py's sun disc), so
the 12 m walls throw their shadows away from the deck and only the barred fence stripes
its south side. Sky, clouds and the far city are unlit StageNeon haze; nothing distant
casts shadows. A lighting change needs no Blender run: see tools/blender/stagekit/presentation.py.
"""
from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON

import city as CITY

ATLAS = "LicenseTestAtlas.png"
DETAIL = "LicenseTestDetail.png"


def lit(asset, base_map, tint=1.0, detail_strength=0.2, detail_scale=0.5, wrap=0.3, specular=0.0, gloss=24, cull=CULL_BACK):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [tint, tint, tint, 1],
            "textures": {"_DetailMap": DETAIL},
            "floats": {"_DetailScale": detail_scale, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": specular,
                       "_Gloss": gloss, "_Emission": 0, "_Wind": 0, "_WindFrequency": 1, "_Cull": cull, "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.64, 0.70, 0.84]},
            "vectors": {"_WindDirection": [1, 0, 0.3, 0]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def neon(asset, base_level, ambient, shade, glow, fog, cull=CULL_BACK):
    sun = CITY.sun_direction()
    return {"asset": asset, "shader": NEON, "baseMap": ATLAS, "baseColor": [1, 1, 1, 1],
            "floats": {"_Base": base_level, "_Ambient": ambient, "_Shade": shade, "_Glow": glow, "_FogAmount": fog,
                       "_TimeScale": 1, "_Cull": cull},
            "vectors": {"_ShadeDirection": [round(sun[0], 3), round(sun[1], 3), round(sun[2], 3), 0]}}


UNSHADOWED = ("Sky", "Clouds", "City Far", "Traffic")
RECEIVE_ONLY = ("Deck", "Street", "Facade", "City Near", "Test Center Tower", "Neighbour")

PRESENTATION = {
    "stage": "license_test",
    "folder": "Assets/Stages/license_test/Presentation/Roof",
    "model": "LicenseTestRoof.fbx",
    "stamp": "LicenseTestRoof.layout.json",
    "root": "Fighter's License Roof",
    # The gray-box pass's children (its prefab root stays as the container).
    "supersededRoots": ["Playable Shell", "Floor Markings", "Scene World"],
    "textures": [
        {"file": "LicenseTestDeck.png", "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": ATLAS, "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "LicenseTestBrick.png", "srgb": True, "wrap": "repeat", "maxSize": 512, "aniso": 8},
        {"file": "LicenseTestConcrete.png", "srgb": True, "wrap": "repeat", "maxSize": 1024, "aniso": 8},
        {"file": DETAIL, "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "LT_Deck", **lit("LicenseTestDeckSurface", "LicenseTestDeck.png", 1.0, 0.34, 0.8, 0.3, 0.04, 20)},
        {"slot": "LT_Lit", **lit("LicenseTestLit", ATLAS, 1.0, 0.16, 0.6, 0.3)},
        {"slot": "LT_Glass", **lit("LicenseTestGlass", ATLAS, 1.0, 0.0, 0.5, 0.25, 0.7, 72)},
        {"slot": "LT_Concrete", **lit("LicenseTestConcreteWall", "LicenseTestConcrete.png", 1.0, 0.18, 0.55, 0.3)},
        {"slot": "LT_Brick", **lit("LicenseTestBrickWall", "LicenseTestBrick.png", 1.0, 0.12, 0.6, 0.3)},
        # Distant city, traffic and hills: shaded towards the sun without a light, fogged into haze.
        {"slot": "LT_Far", **neon("LicenseTestFar", 0.58, 0.30, 0.28, 1.0, 1.0)},
        {"slot": "LT_Sky", **neon("LicenseTestSky", 1.0, 0.0, 0.0, 1.4, 0.0, CULL_OFF)},
    ],
    "shadows": {"cast": True, "receive": True, "objects": {
        **{name: {"cast": False, "receive": False} for name in UNSHADOWED},
        **{name: {"cast": False, "receive": True} for name in RECEIVE_ONLY},
    }},
    "lights": [
        # The gray box's sun keeps its name, so the prefab's light updates in place.
        {"name": "Afternoon Sun", "type": "directional", "sun": {"azimuth": CITY.SUN_AZIMUTH, "elevation": CITY.SUN_ELEVATION},
         "color": [1, 0.95, 0.87], "intensity": 1.18, "shadows": "soft", "shadowStrength": 0.72},
        # Sky bounce over the deck keeps the fighters' shaded sides readable.
        {"name": "Deck Sky Fill", "type": "point", "position": [0, 26, -4], "color": [0.80, 0.88, 1], "intensity": 0.5, "range": 70},
    ],
    "atmosphere": {"background": [0.62, 0.76, 0.92], "ambientSky": [0.66, 0.76, 0.90], "ambientGround": [0.50, 0.47, 0.42],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.76, 0.82, 0.89], "fogStart": 90, "fogEnd": 1300},
    "cameras": [
        # Start, looking north over the start bay to the course and the Test Center.
        {"name": "start", "position": [-6.0, 3.2, -14.4], "target": [-5.0, 2.0, 6.0], "fov": 50},
        # Tower top, looking east-south-east over the ring, the knockout box and the skyline.
        {"name": "hero", "position": [-6.5, 13.0, 8.0], "target": [16.0, -2.0, -5.0], "fov": 50},
        # The ring looking east over the open edge, the broken barrier and the city below.
        {"name": "edge", "position": [4.0, 3.4, -2.0], "target": [40.0, -10.0, -2.0], "fov": 55},
        # Just off the edge, looking back west at the facade lettering: the recovery lesson.
        {"name": "recovery", "position": [31.0, -2.0, 6.0], "target": [19.0, -2.0, 0.0], "fov": 55},
        # Looking south through the safety fence from the course.
        {"name": "fence", "position": [-2.0, 4.0, 4.0], "target": [-2.0, 3.0, -30.0], "fov": 55},
        # Looking down from the edge onto the avenue, the cones and the traffic.
        {"name": "street", "position": [22.5, 0.5, -8.0], "target": [33.0, -60.0, 6.0], "fov": 60},
        {"name": "thumbnail", "position": [-14.0, 21.0, -26.0], "target": [5.0, 0.5, 2.0], "fov": 50},
    ],
}
