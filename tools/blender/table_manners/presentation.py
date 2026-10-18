"""Table Manners in Unity: materials, shadows, lights and atmosphere for sloparena.stage.art-apply.

The stage carries its key: high daylight from the north window whose soft shadows fall
close under the fighters. Large enclosing or distant surfaces would shade the whole room
under that key, so they do not cast. A lighting change needs no Blender run: see
tools/blender/stagekit/presentation.py.
"""
from stagekit.presentation import CULL_BACK, CULL_OFF, LIT, NEON


def lit(asset, wind, cull):
    # The 0.5 tint preserves the pale gingham and paper under the stage's daylight;
    # untinted vertex colours clipped to white in the gameplay camera.
    return {"asset": asset, "shader": LIT, "baseMap": "TableMannersAtlas.png", "baseColor": [0.5, 0.5, 0.5, 1],
            "textures": {"_DetailMap": "TableMannersDetail.png"},
            "floats": {"_DetailScale": 0.6, "_DetailStrength": 0.22, "_Wrap": 0.4, "_Specular": 0.08, "_Gloss": 20,
                       "_Emission": 0, "_Wind": wind, "_WindFrequency": 1.1, "_Cull": cull, "_ReceiveShadowsOff": 0},
            "colors": {"_ShadowTint": [0.78, 0.74, 0.88]},
            "vectors": {"_WindDirection": [0.4, 0, -1, 0]},
            "keywords": {"_RECEIVE_SHADOWS_OFF": False}}


def lamp(name, position, color, intensity, light_range):
    return {"name": name, "type": "point", "position": position, "color": color, "intensity": intensity,
            "range": light_range, "shadowStrength": 0}


PRESENTATION = {
    "stage": "table_manners",
    "folder": "Assets/Stages/table_manners/Presentation/CraftRoom",
    "model": "TableMannersCraftRoom.fbx",
    "stamp": "TableMannersCraftRoom.layout.json",
    "root": "Table Manners Craft Room",
    # Roots of the previous Cozy Interior living-room pass; the generated room replaces them.
    "supersededRoots": ["PlayableTable", "WindowTableFill", "SpawnMarkers", "WarmTableFill", "OversizedLivingRoom"],
    "textures": [
        {"file": "TableMannersAtlas.png", "srgb": True, "wrap": "clamp", "maxSize": 2048, "aniso": 8},
        {"file": "TableMannersDetail.png", "srgb": False, "wrap": "repeat", "maxSize": 512, "aniso": 8},
    ],
    "materials": [
        {"slot": "TM_Build", **lit("TableMannersBuild", 0, CULL_BACK)},
        # Curtains, bunting, balloons and the hanging blanket sway; vertex alpha is the sway weight.
        {"slot": "TM_Cloth", **lit("TableMannersCloth", 0.45, CULL_OFF)},
        # Glitter twinkle, glow stars, lamp bulbs and the toy train (StageNeon travel).
        {"slot": "TM_Glow", "asset": "TableMannersGlow", "shader": NEON, "baseMap": "TableMannersAtlas.png", "baseColor": [1, 1, 1, 1],
         "floats": {"_Base": 0.8, "_Ambient": 0.45, "_Shade": 0.4, "_Glow": 1.3, "_FogAmount": 1, "_TimeScale": 1, "_Cull": CULL_BACK},
         "vectors": {"_ShadeDirection": [-0.15, 0.8, 0.6, 0]}},
        {"slot": "TM_Sky", "asset": "TableMannersSky", "shader": NEON, "baseMap": "TableMannersAtlas.png", "baseColor": [1, 1, 1, 1],
         "floats": {"_Base": 1, "_Ambient": 0, "_Shade": 0, "_Glow": 1, "_FogAmount": 0.35, "_TimeScale": 1, "_Cull": CULL_OFF}},
    ],
    "shadows": {"cast": True, "receive": True, "objects": {
        "Room Walls": {"cast": False, "receive": True},
        "Ceiling": {"cast": False, "receive": True},
        "Garden": {"cast": False, "receive": True},
        "Garden Far": {"cast": False, "receive": False},
        "Sky": {"cast": False, "receive": False},
    }},
    "lights": [
        {"name": "Window Sun", "type": "directional", "rotation": [62, 165, 0], "color": [1, 0.94, 0.84], "intensity": 0.95,
         "shadows": "soft", "shadowStrength": 0.55},
        lamp("Window Glow", [0, 18, 56], [1, 0.92, 0.80], 1.6, 75),
        lamp("Lantern", [0, 38, 0], [1, 0.94, 0.86], 0.6, 60),
        lamp("Room Fill", [0, 30, -40], [0.88, 0.93, 1], 0.8, 95),
        lamp("Desk Lamp", [-56, 6, 9.5], [1, 0.84, 0.62], 1.5, 18),
    ],
    # Bright afternoon indoors: sky-blue clear colour, warm wood bounce, a faint haze on the far garden only.
    "atmosphere": {"background": [0.62, 0.78, 0.96], "ambientSky": [0.80, 0.86, 0.96], "ambientGround": [0.62, 0.52, 0.42],
                   "ambientIntensity": 0.9, "fog": True, "fogColor": [0.80, 0.88, 0.97], "fogStart": 400, "fogEnd": 3600},
}
