"""Eevee review renders approximating the PvP gameplay camera.

The PvP camera orbits the fighter at up to 15 m (radius 10 x 1.5 zoom), pitched
0-45 degrees down (15 by default), with a 40 degree vertical FOV. These renders are
agent review aids only; Unity captures with the match camera's ACES grade and bloom
remain the reference for exposure, colour and emission.
"""
from __future__ import annotations

import os

import bpy

from stagekit.preview import B, gameplay_camera, render_views

GLOW = 1.5
BASE = 0.42

# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance)
VIEWS = {
    "spawn1-ne": ((-5.0, 2.2, -5.0), 45, 15, 12),
    "spawn2-sw": ((5.0, 2.2, 5.0), 225, 15, 12),
    "north-hero": ((0.0, 2.2, 3.0), 0, 10, 14),
    "east-bookie": ((3.0, 2.2, 0.0), 90, 10, 14),
    "south-bar": ((0.0, 2.2, -3.0), 180, 10, 14),
    "west-walkout": ((-3.0, 2.2, 0.0), 270, 10, 14),
    "ringside-low": ((0.0, 1.0, -12.0), 10, 4, 10),
    "recovery-west": ((-19.0, -5.0, 2.0), 95, 12, 14),
    "recovery-corner": ((15.0, -5.0, 15.0), 225, 16, 14),
    "overview": ((0.0, -2.0, 0.0), 30, 30, 70),
    "thumbnail": ((0.0, 0.0, 0.0), 35, 24, 42),
}

LIGHTS = [
    # (name, kind, unity position, colour, power, unity aim point)
    ("Ring Key", "SUN", (8.0, 40.0, -6.0), (1.0, 0.92, 0.8), 2.2, (0.0, 0.0, 0.0)),
    ("Spot NE", "SPOT", (20.0, 26.0, 20.0), (1.0, 0.72, 0.45), 90000, (0.0, 1.2, 0.0)),
    ("Spot SW", "SPOT", (-20.0, 26.0, -20.0), (1.0, 0.72, 0.45), 90000, (0.0, 1.2, 0.0)),
    ("Slop Glow N", "POINT", (0.0, -17.0, 22.0), (0.55, 1.0, 0.2), 9000, None),
    ("Slop Glow S", "POINT", (0.0, -17.0, -22.0), (0.55, 1.0, 0.2), 9000, None),
]


def _material(name, image):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Linear"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(attr.outputs["Color"], mul.inputs[6])
    nt.links.new(tex.outputs["Color"], mul.inputs[7])
    color = mul.outputs[2]
    if name in ("SP_Lit", "SP_Floor", "SP_Canvas"):
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(color, bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.7
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        mat.use_backface_culling = True
        return mat
    glow_mask = nt.nodes.new("ShaderNodeMath")
    glow_mask.operation = "MULTIPLY"
    nt.links.new(attr.outputs["Alpha"], glow_mask.inputs[0])
    nt.links.new(tex.outputs["Alpha"], glow_mask.inputs[1])
    strength = nt.nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY_ADD"
    nt.links.new(glow_mask.outputs[0], strength.inputs[0])
    strength.inputs[1].default_value = GLOW if name == "SP_Venue" else 0.7
    strength.inputs[2].default_value = BASE if name == "SP_Venue" else 0.9
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(color, em.inputs["Color"])
    nt.links.new(strength.outputs[0], em.inputs["Strength"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = True
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


def _compositor(sc):
    try:
        tree = bpy.data.node_groups.new("Bloom", "CompositorNodeTree")
        tree.interface.new_socket("Image", in_out="OUTPUT", socket_type="NodeSocketColor")
        rl = tree.nodes.new("CompositorNodeRLayers")
        glare = tree.nodes.new("CompositorNodeGlare")
        glare.inputs["Type"].default_value = "Bloom"
        glare.inputs["Threshold"].default_value = 1.0
        glare.inputs["Strength"].default_value = 0.45
        glare.inputs["Size"].default_value = 0.6
        out = tree.nodes.new("NodeGroupOutput")
        tree.links.new(rl.outputs["Image"], glare.inputs["Image"])
        tree.links.new(glare.outputs[0], out.inputs[0])
        sc.compositing_node_group = tree
        sc.render.use_compositing = True
    except Exception as exc:  # preview aid only
        print("PREVIEW_BLOOM_UNAVAILABLE", exc)


def render(build, objects, out_dir, images, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    loaded = {}
    for name, path in images.items():
        if path not in loaded:
            loaded[path] = bpy.data.images.load(path)
        _material(name, loaded[path])
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
    bg.inputs[0].default_value = (0.045, 0.028, 0.02, 1)
    bg.inputs[1].default_value = 0.8
    for name, kind, pos, color, power, aim in LIGHTS:
        data = bpy.data.lights.new(name, kind)
        data.color = color
        if kind == "SUN":
            data.energy = power
        else:
            data.energy = power
            data.shadow_soft_size = 0.6
        if kind == "SPOT":
            import math
            data.spot_size = math.radians(40)
            data.spot_blend = 0.5
        ob = bpy.data.objects.new(name, data)
        sc.collection.objects.link(ob)
        ob.location = B(pos)
        if aim is not None:
            ob.rotation_euler = (B(aim) - B(pos)).to_track_quat("-Z", "Y").to_euler()
    _compositor(sc)
    cam = gameplay_camera(sc, clip_end=600, clip_start=0.1)
    colors = [(0.15, 0.25, 0.85, 1), (0.85, 0.35, 0.1, 1), (0.08, 0.08, 0.09, 1), (0.5, 0.25, 0.15, 1)]
    for i, (x, z, yaw) in enumerate(build.data["spawns"]):
        f = _fighter(sc.collection, "spawn%d" % (i + 1), colors[i])
        f.location = B((x, build.data["ring"]["top"] + 0.85, z))
    render_views(sc, cam, VIEWS, views_filter, out_dir)
