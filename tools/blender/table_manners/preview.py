"""Eevee review renders approximating the PvP gameplay camera in daylight.

The PvP camera orbits the fighter at up to 15 m (radius 10 x 1.5 zoom), pitched
0-45 degrees down, with a 40 degree vertical FOV. These renders are agent review
aids only; Unity captures remain the in-game reference.
"""
from __future__ import annotations

import math
import os

import bpy

from stagekit.preview import B, gameplay_camera, render_views

GLOW = 1.4

# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance)
VIEWS = {
    "spawn1-ne": ((-6.5, 1.0, -6.5), 45, 18, 14),
    "spawn2-sw": ((6.5, 1.0, 6.5), 225, 18, 14),
    "spawn3-se": ((-6.5, 1.0, 6.5), 135, 16, 14),
    "spawn4-nw": ((6.5, 1.0, -6.5), 315, 16, 14),
    "north-window": ((0.0, 1.0, 8.0), 0, 8, 15),
    "east-bunk": ((8.0, 1.0, 0.0), 90, 10, 15),
    "south-play": ((0.0, 1.0, -8.0), 180, 10, 15),
    "west-art": ((-8.0, 1.0, 0.0), 270, 10, 15),
    "cake-top": ((0.0, 2.5, 0.0), 160, 32, 13),
    "edge-low-ne": ((11.0, -5.0, 11.0), 225, 4, 15),
    "overview": ((0.0, -4.0, 0.0), 30, 30, 78),
    "overview-high": ((0.0, -10.0, 0.0), 205, 45, 120),
}

LIGHTS = [
    # (name, unity position, colour, watts)
    ("Window Bounce", (0.0, 20.0, 55.0), (1.0, 0.92, 0.80), 90000),
    ("Room Fill", (0.0, 40.0, -20.0), (0.92, 0.95, 1.0), 60000),
    ("Lamp", (-56.0, 7.0, 9.5), (1.0, 0.85, 0.6), 4000),
]


def _material(name, atlas):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    tex = None
    if atlas is not None:
        uv = nt.nodes.new("ShaderNodeUVMap")
        uv.uv_map = "UVMap"
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = atlas
        tex.interpolation = "Linear"
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    color = attr.outputs["Color"]
    if tex is not None:
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        nt.links.new(color, mul.inputs[6])
        nt.links.new(tex.outputs["Color"], mul.inputs[7])
        color = mul.outputs[2]
    if name in ("TM_Build", "TM_Cloth"):
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(color, bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.7
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        mat.use_backface_culling = name == "TM_Build"
        return mat
    # TM_Glow / TM_Sky: unlit base plus glow where vertex alpha marks it
    strength = nt.nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY_ADD"
    nt.links.new(attr.outputs["Alpha"], strength.inputs[0])
    strength.inputs[1].default_value = GLOW if name == "TM_Glow" else 1.0
    strength.inputs[2].default_value = 0.85 if name == "TM_Glow" else 1.0
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(color, em.inputs["Color"])
    nt.links.new(strength.outputs[0], em.inputs["Strength"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = False
    return mat


def _fighter(col, name, color):
    import bmesh
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me)
    col.objects.link(ob)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.35, radius2=0.3, depth=1.7)
    bm.to_mesh(me)
    bm.free()
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = color
    me.materials.append(mat)
    return ob


def render(build, objects, out_dir, atlas_path, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    atlas = bpy.data.images.load(atlas_path) if atlas_path and os.path.exists(atlas_path) else None
    for name in [m.name for m in bpy.data.materials]:
        if name.startswith("TM_"):
            _material(name, atlas)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.eevee.taa_render_samples = 32
    try:
        sc.view_settings.view_transform = "AgX"
        sc.view_settings.look = "None"
    except Exception:
        pass
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.62, 0.74, 0.92, 1)
    bg.inputs[1].default_value = 0.9
    for name, pos, color, watts in LIGHTS:
        data = bpy.data.lights.new(name, "POINT")
        data.energy = watts
        data.color = color
        data.shadow_soft_size = 6.0
        ob = bpy.data.objects.new(name, data)
        sc.collection.objects.link(ob)
        ob.location = B(pos)
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2
    sun.color = (1.0, 0.95, 0.86)
    sun.angle = math.radians(3)
    so = bpy.data.objects.new("Sun", sun)
    sc.collection.objects.link(so)
    # Light travels south and down from the north window (Unity forward ~ (0.15, -0.6, -0.78)).
    so.rotation_euler = (B((0.0, 0.0, 0.0)) - B((-0.15, 0.6, 0.78))).to_track_quat("-Z", "Y").to_euler()
    cam = gameplay_camera(sc, clip_end=8000, clip_start=0.1)
    spawn_colors = [(0.1, 0.25, 0.9, 1), (0.9, 0.12, 0.1, 1), (0.1, 0.7, 0.25, 1), (0.95, 0.75, 0.1, 1)]
    for i, (x, z, yaw) in enumerate(build.data["spawns"]):
        f = _fighter(sc.collection, "spawn%d" % (i + 1), spawn_colors[i])
        f.location = B((x, 0.85, z))
    f = _fighter(sc.collection, "cake-standin", (0.6, 0.2, 0.8, 1))
    f.location = B((1.5, 1.5 + 0.85, -1.0))
    render_views(sc, cam, VIEWS, views_filter, out_dir)
