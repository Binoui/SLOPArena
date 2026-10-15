"""Eevee review renders that approximate the gameplay camera (20 m orbit, 40 deg FOV).

These are agent review aids only; Unity captures remain the in-game reference.
"""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector

import geo

# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance)
VIEWS = {
    "start-north": ((-6, 1.0, -32), 0, 18, 20),
    "bridge-takeoff": ((-28, 1.0, -19), 0, 20, 20),
    "bridge-landing-reverse": ((-28, 1.0, 3), 180, 18, 20),
    "poles-west": ((-37, 1.5, -6), 60, 16, 18),
    "terrace-east": ((-18, 1.0, 26), 90, 20, 20),
    "upper-terrace": ((-5, 7.0, 26), 90, 18, 20),
    "summit-south": ((16, 7.0, 18), 160, 18, 20),
    "summit-landing": ((26, 12.0, -4), 200, 22, 20),
    "descent-sw": ((28, 10.6, -14), 225, 25, 20),
    "finish-west": ((10, 1.0, -34), 270, 18, 20),
    "temple-from-court": ((0, 2.0, -24), 0, 8, 16),
    "overview-ne": ((-3, 0, 0), 210, 32, 105),
    "overview-sw": ((-3, 0, 0), 35, 26, 110),
    "low-east": ((0, -4, 0), 270, 2, 80),
}

KEY_LIGHT_DIR = (-0.0868, -0.9848, 0.1504)   # Arcade scene key light forward (Unity space)


def B(p):
    return Vector((-p[0], -p[2], p[1]))


def _material(name, atlas):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    color = attr.outputs["Color"]
    if atlas is not None and name in ("SA_Stone", "SA_Cloth", "SA_Gloss"):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = atlas
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        nt.links.new(color, mul.inputs[6])
        nt.links.new(tex.outputs["Color"], mul.inputs[7])
        color = mul.outputs[2]
    nt.links.new(color, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    if name == "SA_Gloss":
        bsdf.inputs["Metallic"].default_value = 0.65
        bsdf.inputs["Roughness"].default_value = 0.32
    if name == "SA_Glow":
        nt.links.new(color, bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 3.0
    if name in ("SA_Cloth", "SA_Foliage"):
        mat.use_backface_culling = False
    else:
        mat.use_backface_culling = True
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    return mat


def _fighter(col):
    me = bpy.data.meshes.new("fighter")
    ob = bpy.data.objects.new("fighter", me)
    col.objects.link(ob)
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.32, radius2=0.28, depth=1.75)
    bm.to_mesh(me)
    bm.free()
    mat = bpy.data.materials.new("fighter")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.08, 0.25, 1)
    me.materials.append(mat)
    return ob


def _markers(build, col):
    mat = bpy.data.materials.new("target")
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (1.0, 0.75, 0.1, 1)
    p.inputs["Emission Color"].default_value = (1.0, 0.6, 0.05, 1)
    p.inputs["Emission Strength"].default_value = 0.6
    for name, pos in build.data["markers"].items():
        if not name.startswith("Target_"):
            continue
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.65, location=B(pos), segments=16, ring_count=8)
        ob = bpy.context.object
        ob.data.materials.append(mat)


def render(build, objects, out_dir, atlas_path, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    atlas = bpy.data.images.load(atlas_path) if atlas_path and os.path.exists(atlas_path) else None
    for name in [m.name for m in bpy.data.materials]:
        if name.startswith("SA_"):
            _material(name, atlas)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    try:
        sc.eevee.taa_render_samples = 24
        sc.eevee.use_shadows = True
    except Exception:
        pass
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.0
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.36, 0.55, 0.78, 1)
    bg.inputs[1].default_value = 0.9
    sun_data = bpy.data.lights.new("Key", "SUN")
    sun_data.energy = 3.2
    sun_data.color = (1.0, 0.95, 0.85)
    sun_data.angle = math.radians(2.5)
    sun = bpy.data.objects.new("Key", sun_data)
    sc.collection.objects.link(sun)
    d = B(KEY_LIGHT_DIR) - B((0, 0, 0))
    sun.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    fill_data = bpy.data.lights.new("Fill", "SUN")
    fill_data.energy = 0.9
    fill_data.color = (0.65, 0.75, 1.0)
    fill_data.use_shadow = False
    fill = bpy.data.objects.new("Fill", fill_data)
    sc.collection.objects.link(fill)
    fill.rotation_euler = (B((0.4, -0.5, -0.75)) - B((0, 0, 0))).to_track_quat("-Z", "Y").to_euler()
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens_unit = "FOV"
    cam_data.angle_y = math.radians(40)
    cam_data.sensor_fit = "VERTICAL"
    cam_data.clip_end = 2500
    cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    fighter = _fighter(sc.collection)
    _markers(build, sc.collection)
    chosen = [v for v in VIEWS if not views_filter or v in views_filter.split(",")]
    for name in chosen:
        target, yaw, pitch, dist = VIEWS[name]
        y, p = math.radians(yaw), math.radians(pitch)
        fwd = (math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p))
        pos = tuple(target[i] - fwd[i] * dist for i in range(3))
        cam.location = B(pos)
        cam.rotation_euler = (B(target) - B(pos)).to_track_quat("-Z", "Y").to_euler()
        fighter.location = B((target[0], target[1] - 1.0 + 0.875, target[2]))
        fighter.hide_render = dist > 60
        sc.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", sc.render.filepath)
