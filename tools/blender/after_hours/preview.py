"""Eevee review renders approximating the PvP gameplay camera.

The PvP camera orbits the fighter at up to 15 m (radius 10 x 1.5 zoom), pitched
10-30 degrees down, with a 40 degree vertical FOV. These renders are agent review
aids only; Unity captures remain the in-game reference.
"""
from __future__ import annotations

import os

import bpy

from stagekit.preview import B, gameplay_camera, render_views

GLOW = 1.4
BASE = 0.42

# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance)
VIEWS = {
    "spawn1-ne": ((-6.0, 1.0, -5.0), 50, 20, 14),
    "spawn2-sw": ((6.0, 1.0, 5.0), 230, 20, 14),
    "spawn3-se": ((-6.0, 1.0, 5.0), 130, 18, 14),
    "spawn4-nw": ((6.0, 1.0, -5.0), 310, 18, 14),
    "dj-riser-west": ((10.5, 2.5, 0.0), 270, 16, 13),
    "bar-riser-east": ((-10.5, 2.5, 0.0), 90, 16, 13),
    "north-neon-row": ((0.0, 1.0, 9.0), 0, 12, 15),
    "south-river": ((0.0, 1.0, -9.0), 180, 12, 15),
    "east-train": ((12.0, 1.0, 0.0), 90, 14, 15),
    "west-expressway": ((-12.0, 1.0, 0.0), 270, 14, 15),
    "recovery-north": ((0.0, -6.0, 15.5), 180, 10, 15),
    "recovery-ne-corner": ((15.0, -5.0, 12.0), 225, 10, 15),
    "overview": ((0.0, -2.0, 0.0), 30, 28, 75),
    "overview-high": ((0.0, -10.0, 0.0), 205, 42, 140),
}

LIGHTS = [
    # (name, unity position, colour, watts)
    ("DJ Violet", (10.5, 5.0, 0.0), (0.62, 0.35, 1.0), 2600),
    ("Bar Pink", (-10.5, 5.0, 0.0), (1.0, 0.35, 0.65), 2600),
    ("Floor Magenta", (0.0, 6.0, 0.0), (1.0, 0.45, 0.8), 2200),
    ("City Amber", (0.0, 9.0, 22.0), (1.0, 0.72, 0.45), 5000),
    ("River Cyan", (0.0, 9.0, -22.0), (0.45, 0.8, 1.0), 4000),
]


def _nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    return nt


def _material(name, atlas):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt = _nodes(mat)
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
    if name == "AH_Build":
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(color, bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.62
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
        mat.use_backface_culling = True
        return mat
    # AH_Night / AH_Sky: unlit base plus glow where vertex alpha x atlas alpha
    glow_mask = attr.outputs["Alpha"]
    if tex is not None:
        m2 = nt.nodes.new("ShaderNodeMath")
        m2.operation = "MULTIPLY"
        nt.links.new(glow_mask, m2.inputs[0])
        nt.links.new(tex.outputs["Alpha"], m2.inputs[1])
        glow_mask = m2.outputs[0]
    strength = nt.nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY_ADD"
    nt.links.new(glow_mask, strength.inputs[0])
    strength.inputs[1].default_value = {"AH_Night": GLOW, "AH_Far": 1.6}.get(name, 1.0)
    strength.inputs[2].default_value = BASE if name in ("AH_Night", "AH_Far") else 1.0
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(color, em.inputs["Color"])
    nt.links.new(strength.outputs[0], em.inputs["Strength"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = name in ("AH_Night", "AH_Far")
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
    p.inputs["Emission Color"].default_value = color
    p.inputs["Emission Strength"].default_value = 0.15
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
        return True
    except Exception as exc:  # preview aid only
        print("PREVIEW_BLOOM_UNAVAILABLE", exc)
        return False


def render(build, objects, out_dir, atlas_path, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    atlas = bpy.data.images.load(atlas_path) if atlas_path and os.path.exists(atlas_path) else None
    for name in [m.name for m in bpy.data.materials]:
        if name.startswith("AH_"):
            _material(name, atlas)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.eevee.taa_render_samples = 32
    try:
        sc.view_settings.view_transform = "Standard"
        sc.view_settings.look = "None"
    except Exception:
        pass
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.05, 0.035, 0.11, 1)
    bg.inputs[1].default_value = 0.6
    for name, pos, color, watts in LIGHTS:
        data = bpy.data.lights.new(name, "POINT")
        data.energy = watts
        data.color = color
        data.shadow_soft_size = 1.5
        data.use_soft_falloff = True
        ob = bpy.data.objects.new(name, data)
        sc.collection.objects.link(ob)
        ob.location = B(pos)
    moon = bpy.data.lights.new("Moon", "SUN")
    moon.energy = 0.35
    moon.color = (0.75, 0.78, 1.0)
    mo = bpy.data.objects.new("Moon", moon)
    sc.collection.objects.link(mo)
    mo.rotation_euler = (B((0.35, -0.75, 0.55)) - B((0, 0, 0))).to_track_quat("-Z", "Y").to_euler()
    _compositor(sc)
    cam = gameplay_camera(sc, clip_end=5000, clip_start=0.1)
    fighters = []
    spawn_colors = [(0.1, 0.25, 0.9, 1), (0.9, 0.12, 0.1, 1), (0.1, 0.7, 0.25, 1), (0.95, 0.75, 0.1, 1)]
    for i, (x, z, yaw) in enumerate(build.data["spawns"]):
        f = _fighter(sc.collection, "spawn%d" % (i + 1), spawn_colors[i])
        f.location = B((x, 0.85, z))
        fighters.append(f)
    render_views(sc, cam, VIEWS, views_filter, out_dir)
