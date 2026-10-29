"""Neo Mall in Unity: materials, shadows, lights and atmosphere for sloparena.stage.art-apply.
Gameplay captures use the course replay runner (docs/testing/course-replay.md).
The named overview camera can be captured with sloparena.stage.views.cameras inside that replay.

Night inside the closed mall: cool moonlight falls through the glass vault, the galleries' LED
strips, downlights and the shop signs are StageNeon glow that the match camera's bloom picks up,
and a few warm and coloured pools (the food court, the arcade, the cinema, the fountain) keep
every deck readable. A lighting change needs no Blender run: see
tools/blender/stagekit/presentation.py.
"""
import math

from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON

GROUND = "NeoMallGround.png"
ATLAS = "NeoMallAtlas.png"
DETAIL = "NeoMallDetail.png"
SUN_AZIMUTH = 200.0     # the moon, high in the south-south-west
SUN_ELEVATION = 58.0
UNSHADOWED = ("Vault", "Cables", "Katana")


def sun_direction():
    az, el = math.radians(SUN_AZIMUTH), math.radians(SUN_ELEVATION)
    return (math.sin(az) * math.cos(el), math.sin(el), math.cos(az) * math.cos(el))


def lit(asset, base_map, detail_strength, detail_scale=0.5, wrap=0.4, specular=0.0, gloss=24):
    return {"asset": asset, "shader": LIT, "baseMap": base_map, "baseColor": [1, 1, 1, 1],
            "textures": {"_DetailMap": DETAIL},
            "floats": {"_DetailScale": detail_scale, "_DetailStrength": detail_strength, "_Wrap": wrap, "_Specular": specular,
                       "_Gloss": gloss, "_Emission": 0, "_Wind": 0, "_WindFrequency": 1, "_Cull": CULL_BACK,
                       "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.50, 0.54, 0.80]},
            "vectors": {"_WindDirection": [1, 0, 0.3, 0]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def neon(asset, base_level, ambient, shade, glow, fog, cull=CULL_BACK):
    sun = sun_direction()
    return {"asset": asset, "shader": NEON, "baseMap": ATLAS, "baseColor": [1, 1, 1, 1],
            "floats": {"_Base": base_level, "_Ambient": ambient, "_Shade": shade, "_Glow": glow, "_FogAmount": fog,
                       "_TimeScale": 1, "_Cull": cull},
            "vectors": {"_ShadeDirection": [round(sun[0], 3), round(sun[1], 3), round(sun[2], 3), 0]}}


def warm(name, position, intensity=0.7, rng=16.0, color=(1.0, 0.82, 0.60)):
    return {"name": name, "type": "point", "position": list(position), "color": list(color), "intensity": intensity, "range": rng}


PRESENTATION = {
    "stage": "wibou_mall",
    "folder": "Assets/Stages/wibou_mall/Presentation/Mall",
    "model": "NeoMall.fbx",
    "stamp": "NeoMall.layout.json",
    "root": "Neo Mall",
    "cameras": [
        {"name": "racing-overview", "position": [4.5, 22, -27], "target": [4.5, 4, -26.5], "fov": 65},
    ],
    "textures": [
        {"file": GROUND, "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": ATLAS, "srgb": True, "wrap": "clamp", "maxSize": 4096, "aniso": 8},
        {"file": DETAIL, "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "NM_Lit", **lit("NeoMallLit", ATLAS, 0.18, 0.6)},
        {"slot": "NM_Ground", **lit("NeoMallGroundSurface", GROUND, 0.22, 0.7, wrap=0.35, specular=0.25, gloss=40)},
        # Glass, polished stone and the wet ground floor.
        {"slot": "NM_Glass", **lit("NeoMallGlass", ATLAS, 0.1, 0.6, wrap=0.3, specular=0.9, gloss=90)},
        # Signs, LED strips, downlights and lit windows: vertex alpha x atlas alpha glows.
        {"slot": "NM_Glow", **neon("NeoMallGlow", 0.8, 0.4, 0.2, 2.6, 0.4)},
        {"slot": "NM_Sky", **neon("NeoMallSky", 1.0, 0.0, 0.0, 1.0, 0.0, CULL_OFF)},
    ],
    "shadows": {"cast": True, "receive": True, "objects": {
        **{name: {"cast": False, "receive": False} for name in UNSHADOWED},
        "Floor": {"cast": False, "receive": True},
    }},
    "lights": [
        {"name": "Moonlight", "type": "directional", "sun": {"azimuth": SUN_AZIMUTH, "elevation": SUN_ELEVATION},
         "color": [0.70, 0.76, 1.0], "intensity": 0.6, "shadows": "soft", "shadowStrength": 0.5},
        # The mall's own warm house lights from the north-east keep the moonlit sides readable.
        {"name": "House Fill", "type": "directional", "rotation": [32, 225, 0], "color": [1.0, 0.86, 0.70],
         "intensity": 0.55, "shadows": "none"},
        # The galleries' own warm light on the shopfronts across the atrium.
        {"name": "Shopfront Fill", "type": "directional", "rotation": [18, 70, 0], "color": [1.0, 0.84, 0.66],
         "intensity": 0.3, "shadows": "none"},
        warm("Kiosk Row Glow", (-11.5, 7.5, -32.0), 0.7, 18.0),
        warm("Skybridge Glow", (0.0, 8.0, -12.0), 0.6, 14.0),
        warm("Escalator Glow", (10.0, 10.0, 8.0), 0.6, 15.0),
        warm("Food Court Glow", (-13.0, 13.0, 20.0), 0.8, 18.0, (1.0, 0.70, 0.45)),
        warm("Arcade Glow", (12.5, 18.5, 28.0), 0.7, 15.0, (0.55, 0.85, 1.0)),
        warm("Game Store Glow", (12.0, 18.5, 41.0), 0.7, 12.0, (0.80, 0.60, 1.0)),
        warm("Fountain Glow", (0.0, 2.0, 0.0), 0.6, 10.0, (0.50, 0.85, 1.0)),
    ],
    "atmosphere": {"background": [0.05, 0.06, 0.12], "ambientSky": [0.46, 0.47, 0.58], "ambientGround": [0.36, 0.30, 0.26],
                   "ambientIntensity": 1, "fog": True, "fogColor": [0.10, 0.11, 0.20], "fogStart": 70, "fogEnd": 260},
}
