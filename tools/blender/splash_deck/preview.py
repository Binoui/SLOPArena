"""Eevee review renders approximating the PvP gameplay camera on a sunny morning.

The PvP camera orbits the fighter at up to 15 m (radius 10 x 1.5 zoom), pitched
10-30 degrees down, with a 40 degree vertical FOV. These renders are quick agent
review aids only: the water here is a plain glossy stand-in for SLOPArena/StageWater,
and the match camera's ACES grade and bloom decide how colour and glints read, so
Unity gameplay captures remain the reference.
"""
from __future__ import annotations

import math
import os

import bpy

from stagekit.preview import B, gameplay_camera, render_views
import bay as BAY

# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance)
VIEWS = {
    "spawn1-ne": ((-6.0, 1.0, -5.0), 45, 18, 14),
    "spawn2-sw": ((6.0, 1.0, 5.0), 225, 18, 14),
    "spawn3-se": ((-6.0, 1.0, 5.0), 135, 18, 14),
    "spawn4-nw": ((6.0, 1.0, -5.0), 315, 18, 14),
    "village": ((2.0, 2.0, 0.0), 270, 6, 18),
    "mouth": ((-6.0, 2.0, 0.0), 100, 6, 16),
    "sign": ((-3.5, 1.0, 0.0), 270, 10, 8),
    "hatch": ((4.0, 1.5, 3.0), 120, 22, 10),
    "port-hull": ((0.0, -6.0, 17.0), 180, 4, 13),
    "bow": ((26.0, -4.0, 6.0), 250, 6, 14),
    "overview": ((0.0, -6.0, 0.0), 225, 32, 95),
    "bay-wide": ((0.0, 10.0, 0.0), 300, 12, 160),
}


def _material(name, images):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    image = {"SD_Deck": images.get("deck"), "SD_Hull": images.get("hull")}.get(name, images.get("atlas"))
    color = attr.outputs["Color"]
    tex = None
    if image is not None and name not in ("SD_Water",):
        uv = nt.nodes.new("ShaderNodeUVMap")
        uv.uv_map = "UVMap"
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(color, mix.inputs[6])
        nt.links.new(tex.outputs["Color"], mix.inputs[7])
        color = mix.outputs[2]
    if name in ("SD_Glow", "SD_Sky"):
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(color, em.inputs["Color"])
        em.inputs["Strength"].default_value = 1.0
        nt.links.new(em.outputs[0], out.inputs["Surface"])
        mat.use_backface_culling = False
        return mat
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(color, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.08 if name == "SD_Water" else 0.8
    if name == "SD_Water":
        bsdf.inputs["Specular IOR Level"].default_value = 0.6
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = name not in ("SD_Cloth", "SD_Foliage")
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


def render(build, objects, out_dir, paths, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    images = {}
    for key in ("deck", "hull", "atlas"):
        p = paths.get(key)
        if p and os.path.exists(p):
            img = bpy.data.images.load(p)
            img.alpha_mode = "CHANNEL_PACKED"  # atlas alpha is a glow mask, not coverage
            images[key] = img
    for name in [m.name for m in bpy.data.materials]:
        if name.startswith("SD_"):
            _material(name, images)
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
    bg.inputs[0].default_value = (0.55, 0.72, 0.92, 1)
    bg.inputs[1].default_value = 0.9
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 4.2
    sun.color = (1.0, 0.96, 0.88)
    sun.angle = math.radians(1.5)
    so = bpy.data.objects.new("Sun", sun)
    sc.collection.objects.link(so)
    towards_sun = BAY.sun_direction()
    so.rotation_euler = (B((0, 0, 0)) - B(towards_sun)).to_track_quat("-Z", "Y").to_euler()
    cam = gameplay_camera(sc, clip_end=5000, clip_start=0.1)
    colors = [(0.1, 0.25, 0.9, 1), (0.9, 0.12, 0.1, 1), (0.1, 0.7, 0.25, 1), (0.95, 0.75, 0.1, 1)]
    for i, (x, z, yaw) in enumerate(build.data["spawns"]):
        f = _fighter(sc.collection, "spawn%d" % (i + 1), colors[i])
        f.location = B((x, 0.85, z))
    render_views(sc, cam, VIEWS, views_filter, out_dir)
