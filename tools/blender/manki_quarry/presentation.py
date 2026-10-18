"""Kaboom Quarry in Unity: materials, shadows, lights and atmosphere for
sloparena.stage.art-apply. A course has no review cameras: sloparena.stage.views.* refuses
courses, so review captures come from the course replay runner (docs/testing/course-replay.md).

Dusty afternoon in Manki's flooded quarry: a warm sun high in the south-west lights Mount
Manki's face and every sign the route looks at, a pale dusty sky and haze soften the
terraces, and the beacons, bulbs and fuse sparks are StageNeon glow that the match camera's
bloom picks up. Route surfaces stay cool grey and steel so Manki's orange reads; the ochre
lives in the walls. A lighting change needs no Blender run: see
tools/blender/stagekit/presentation.py.
"""
import math

from stagekit.presentation import CULL_BACK, LIT, NEON, WATER

GROUND = "KaboomQuarryGround.png"
ATLAS = "KaboomQuarryAtlas.png"
STRATA = "KaboomQuarryStrata.png"
DETAIL = "KaboomQuarryDetail.png"
RIPPLES = "KaboomQuarryRipples.png"
SUN_AZIMUTH = 215.0
SUN_ELEVATION = 56.0


def sun_direction():
    az, el = math.radians(SUN_AZIMUTH), math.radians(SUN_ELEVATION)
    return (math.sin(az) * math.cos(el), math.sin(el), math.cos(az) * math.cos(el))


def lit(asset, base_map, detail_strength, detail_scale=0.5, wrap=0.3, specular=0.0, gloss=24):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [1, 1, 1, 1],
            "textures": {"_DetailMap": DETAIL},
            "floats": {"_DetailScale": detail_scale, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": specular,
                       "_Gloss": gloss, "_Emission": 0, "_Wind": 0, "_WindFrequency": 1, "_Cull": CULL_BACK,
                       "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.66, 0.66, 0.76]},
            "vectors": {"_WindDirection": [1, 0, 0.3, 0]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def neon(asset, base_level, ambient, shade, glow, fog):
    sun = sun_direction()
    return {"asset": asset, "shader": NEON, "baseMap": ATLAS, "baseColor": [1, 1, 1, 1],
            "floats": {"_Base": base_level, "_Ambient": ambient, "_Shade": shade, "_Glow": glow, "_FogAmount": fog,
                       "_TimeScale": 1, "_Cull": CULL_BACK},
            "vectors": {"_ShadeDirection": [round(sun[0], 3), round(sun[1], 3), round(sun[2], 3), 0]}}


PRESENTATION = {
    "stage": "manki_quarry",
    "folder": "Assets/Stages/manki_quarry/Presentation/Quarry",
    "model": "KaboomQuarry.fbx",
    "stamp": "KaboomQuarry.layout.json",
    "root": "Kaboom Quarry",
    "supersededRoots": ["Kaboom Quarry (rough pass)"],
    "textures": [
        {"file": GROUND, "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": ATLAS, "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": STRATA, "srgb": True, "wrap": "repeat", "maxSize": 1024, "aniso": 8},
        {"file": DETAIL, "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
        {"file": RIPPLES, "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "MQ_Lit", **lit("KaboomQuarryLit", ATLAS, 0.22, 0.6)},
        {"slot": "MQ_Ground", **lit("KaboomQuarryGroundSurface", GROUND, 0.32, 0.7, wrap=0.25)},
        {"slot": "MQ_Rock", **lit("KaboomQuarryRock", STRATA, 0.28, 0.45, wrap=0.35)},
        # Beacons, bulbs, sparks and lamps: vertex alpha x atlas alpha glows.
        {"slot": "MQ_Glow", **neon("KaboomQuarryGlow", 0.7, 0.35, 0.3, 2.6, 1.0)},
        {"slot": "MQ_Water", "asset": "KaboomQuarryWater", "shader": WATER, "baseColor": [1, 1, 1, 1],
         "textures": {"_RippleMap": RIPPLES},
         "floats": {"_SwellHeight": 0.05, "_SwellLength": 9, "_FoamStrength": 0.55, "_RippleStrength": 0.6},
         "colors": {"_SkyColor": [0.60, 0.70, 0.80], "_HorizonColor": [0.88, 0.82, 0.72]}},
    ],
    "shadows": {"cast": True, "receive": True, "objects": {
        "Water": {"cast": False, "receive": True},
        "Quarry Walls": {"cast": False, "receive": True},
        "Mount Manki": {"cast": False, "receive": True},
        "Flotsam": {"cast": False, "receive": True},
        "Smoke": {"cast": False, "receive": False},
    }},
    "lights": [
        {"name": "Dusty Sun", "type": "directional", "sun": {"azimuth": SUN_AZIMUTH, "elevation": SUN_ELEVATION},
         "color": [1, 0.92, 0.78], "intensity": 1.05, "shadows": "soft", "shadowStrength": 0.72},
        # Warm bounce off the ochre walls keeps shaded sides readable from every yaw.
        {"name": "Pit Bounce", "type": "point", "position": [-10, 22, -4], "color": [1, 0.86, 0.68], "intensity": 0.45, "range": 80},
    ],
    "atmosphere": {"background": [0.84, 0.77, 0.64], "ambientSky": [0.74, 0.78, 0.84], "ambientGround": [0.64, 0.53, 0.40],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.88, 0.80, 0.67], "fogStart": 80, "fogEnd": 650},
}
