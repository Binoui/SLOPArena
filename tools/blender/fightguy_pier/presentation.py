"""Fight Night Pier in Unity: materials, shadows, lights and atmosphere for
sloparena.stage.art-apply. A course has no review cameras: sloparena.stage.views.* refuses
courses, so review captures come from the course replay runner (docs/testing/course-replay.md).

Dusk on the pier: a low orange sun sets in the west behind the old pier, the sky fades from
gold to violet, and the fair's bulbs, the wheel's lights and the town's windows are StageNeon
glow that the match camera's bloom picks up. A cool sky fill keeps the east faces readable
and a warm fill sits over the fair. A lighting change needs no Blender run: see
tools/blender/stagekit/presentation.py.
"""
import math

from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON, WATER

GROUND = "FightNightPierGround.png"
ATLAS = "FightNightPierAtlas.png"
DETAIL = "FightNightPierDetail.png"
RIPPLES = "FightNightPierRipples.png"
SUN_AZIMUTH = 262.0
SUN_ELEVATION = 16.0   # the key light sits higher than the sky's sinking disc, so tops stay lit
UNSHADOWED = ("Sky", "Clouds", "Town", "Town Lights", "Headlands", "Big Wheel Lights", "Lights", "Lamps")


def sun_direction():
    az, el = math.radians(SUN_AZIMUTH), math.radians(SUN_ELEVATION)
    return (math.sin(az) * math.cos(el), math.sin(el), math.cos(az) * math.cos(el))


def lit(asset, base_map, detail_strength, detail_scale=0.5, wrap=0.35, specular=0.0, gloss=24):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [1, 1, 1, 1],
            "textures": {"_DetailMap": DETAIL},
            "floats": {"_DetailScale": detail_scale, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": specular,
                       "_Gloss": gloss, "_Emission": 0, "_Wind": 0, "_WindFrequency": 1, "_Cull": CULL_BACK,
                       "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.58, 0.56, 0.78]},
            "vectors": {"_WindDirection": [1, 0, 0.3, 0]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def neon(asset, base_level, ambient, shade, glow, fog, cull=CULL_BACK):
    sun = sun_direction()
    return {"asset": asset, "shader": NEON, "baseMap": ATLAS, "baseColor": [1, 1, 1, 1],
            "floats": {"_Base": base_level, "_Ambient": ambient, "_Shade": shade, "_Glow": glow, "_FogAmount": fog,
                       "_TimeScale": 1, "_Cull": cull},
            "vectors": {"_ShadeDirection": [round(sun[0], 3), round(sun[1], 3), round(sun[2], 3), 0]}}


PRESENTATION = {
    "stage": "fightguy_pier",
    "folder": "Assets/Stages/fightguy_pier/Presentation/Pier",
    "model": "FightNightPier.fbx",
    "stamp": "FightNightPier.layout.json",
    "root": "Fight Night Pier",
    "textures": [
        {"file": GROUND, "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": ATLAS, "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": DETAIL, "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
        {"file": RIPPLES, "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "FNP_Lit", **lit("FightNightPierLit", ATLAS, 0.2, 0.6)},
        {"slot": "FNP_Ground", **lit("FightNightPierGroundSurface", GROUND, 0.28, 0.7, wrap=0.3)},
        # Bulbs, globes, windows and neon: vertex alpha x atlas alpha glows.
        {"slot": "FNP_Glow", **neon("FightNightPierGlow", 0.75, 0.35, 0.25, 2.8, 0.6)},
        {"slot": "FNP_Sky", **neon("FightNightPierSky", 1.0, 0.0, 0.0, 1.5, 0.0, CULL_OFF)},
        {"slot": "FNP_Water", "asset": "FightNightPierWater", "shader": WATER, "baseColor": [1, 1, 1, 1],
         "textures": {"_RippleMap": RIPPLES},
         "floats": {"_SwellHeight": 0.08, "_SwellLength": 12, "_FoamStrength": 0.5, "_RippleStrength": 0.65,
                    "_Fresnel": 0.8, "_Specular": 3.2, "_Gloss": 600},
         "colors": {"_SkyColor": [0.52, 0.36, 0.62], "_HorizonColor": [1.0, 0.66, 0.44], "_FoamColor": [0.92, 0.86, 0.86, 1]}},
    ],
    "shadows": {"cast": True, "receive": True, "objects": {
        **{name: {"cast": False, "receive": False} for name in UNSHADOWED},
        "Sea": {"cast": False, "receive": True},
    }},
    "lights": [
        {"name": "Dusk Sun", "type": "directional", "sun": {"azimuth": SUN_AZIMUTH, "elevation": SUN_ELEVATION},
         "color": [1.0, 0.70, 0.50], "intensity": 1.05, "shadows": "soft", "shadowStrength": 0.6},
        # Cool sky light from the east keeps the shaded sides readable from every yaw.
        {"name": "Sky Fill", "type": "directional", "rotation": [38, 70, 0], "color": [0.55, 0.60, 0.95],
         "intensity": 0.35, "shadows": "none"},
        # The fair's own warm glow over the midway and the pier head.
        {"name": "Fair Glow", "type": "point", "position": [0, 9, -24], "color": [1.0, 0.78, 0.52], "intensity": 0.55, "range": 26},
        {"name": "Ringside Glow", "type": "point", "position": [5, 9, 46], "color": [1.0, 0.82, 0.60], "intensity": 0.6, "range": 22},
    ],
    "atmosphere": {"background": [0.40, 0.30, 0.48], "ambientSky": [0.56, 0.50, 0.74], "ambientGround": [0.36, 0.26, 0.24],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.66, 0.46, 0.56], "fogStart": 70, "fogEnd": 600},
}
