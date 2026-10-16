"""Eevee review renders approximating the PvP gameplay camera at sunset.

The PvP camera orbits the fighter at up to 15 m (radius 10 x 1.5 zoom), pitched
10-30 degrees down, with a 40 degree vertical FOV. These renders are quick agent
review aids only: the match camera's ACES grade and bloom decide how emission and
colour read, so Unity gameplay captures remain the reference.
"""
from __future__ import annotations

import math
import os

import bpy

from stagekit.preview import B, gameplay_camera, render_views
import camp as CAMP
import forest as FOREST

# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance)
VIEWS = {
    "spawn1-se": ((-7.0, 1.0, 5.0), 125, 20, 14),
    "spawn2-nw": ((7.0, 1.0, -5.0), 305, 20, 14),
    "spawn3-ne": ((-7.0, 1.0, -5.0), 55, 18, 14),
    "spawn4-sw": ((7.0, 1.0, 5.0), 235, 18, 14),
    "split-to-camp": ((0.0, 1.0, -8.0), 0, 16, 16),
    "east-shelf": ((15.0, -1.0, 0.0), 270, 14, 14),
    "west-sunset": ((-8.0, 1.0, 0.0), 260, 8, 15),
    "recovery-south": ((0.0, -6.0, -15.5), 0, 6, 15),
    "phone": ((12.0, -23.0, -42.0), 200, 38, 13),
    "camp": ((0.0, -12.0, 80.0), 0, 12, 70),
    "overview": ((0.0, -6.0, 0.0), 210, 30, 75),
}


def _material(name, atlas, top):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    image = top if name == "LO_Stump" else atlas
    tex = None
    if image is not None:
        uv = nt.nodes.new("ShaderNodeUVMap")
        uv.uv_map = "UVMap"
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    color = attr.outputs["Color"]
    if tex is not None:
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(color, mix.inputs[6])
        nt.links.new(tex.outputs["Color"], mix.inputs[7])
        color = mix.outputs[2]
    if name in ("LO_Glow", "LO_Sky"):
        glow = attr.outputs["Alpha"]
        if tex is not None:
            m2 = nt.nodes.new("ShaderNodeMath")
            m2.operation = "MULTIPLY"
            nt.links.new(glow, m2.inputs[0])
            nt.links.new(tex.outputs["Alpha"], m2.inputs[1])
            glow = m2.outputs[0]
        strength = nt.nodes.new("ShaderNodeMath")
        strength.operation = "MULTIPLY_ADD"
        nt.links.new(glow, strength.inputs[0])
        strength.inputs[1].default_value = 2.5 if name == "LO_Glow" else 1.5
        strength.inputs[2].default_value = 0.6 if name == "LO_Glow" else 1.0
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(color, em.inputs["Color"])
        nt.links.new(strength.outputs[0], em.inputs["Strength"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
        mat.use_backface_culling = name == "LO_Glow"
        return mat
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(color, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = name in ("LO_Wood", "LO_Stump")
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


def render(build, objects, out_dir, atlas_path, top_path, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    atlas = bpy.data.images.load(atlas_path) if atlas_path and os.path.exists(atlas_path) else None
    if atlas is not None:
        atlas.alpha_mode = "CHANNEL_PACKED"  # alpha is a glow mask, not coverage
    top = bpy.data.images.load(top_path) if top_path and os.path.exists(top_path) else None
    for name in [m.name for m in bpy.data.materials]:
        if name.startswith("LO_"):
            _material(name, atlas, top)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.eevee.taa_render_samples = 32
    try:
        sc.view_settings.view_transform = "AgX"
    except Exception:
        pass
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.42, 0.36, 0.52, 1)
    bg.inputs[1].default_value = 0.45
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2
    sun.color = (1.0, 0.72, 0.48)
    sun.angle = math.radians(2.0)
    so = bpy.data.objects.new("Sun", sun)
    sc.collection.objects.link(so)
    az, el = math.radians(FOREST.SUN_AZ), math.radians(22.0)
    towards_sun = (math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az))
    so.rotation_euler = (B((0, 0, 0)) - B(towards_sun)).to_track_quat("-Z", "Y").to_euler()
    for name, pos, color, watts in (("Campfire", CAMP.LIGHTS["campfire"], (1.0, 0.55, 0.2), 90000),
                                    ("Lantern", CAMP.LIGHTS["lantern"], (1.0, 0.8, 0.45), 12000),
                                    ("Tent", CAMP.LIGHTS["tent"], (1.0, 0.65, 0.35), 8000)):
        data = bpy.data.lights.new(name, "POINT")
        data.energy = watts
        data.color = color
        data.shadow_soft_size = 2.0
        ob = bpy.data.objects.new(name, data)
        sc.collection.objects.link(ob)
        ob.location = B(pos)
    cam = gameplay_camera(sc, clip_end=5000, clip_start=0.1)
    colors = [(0.1, 0.25, 0.9, 1), (0.9, 0.12, 0.1, 1), (0.1, 0.7, 0.25, 1), (0.95, 0.75, 0.1, 1)]
    for i, (x, z, yaw) in enumerate(build.data["spawns"]):
        f = _fighter(sc.collection, "spawn%d" % (i + 1), colors[i])
        f.location = B((x, 0.85, z))
    render_views(sc, cam, VIEWS, views_filter, out_dir)
