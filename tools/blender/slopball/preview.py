"""Eevee review renders of the Night Court at the Slopball gameplay camera.

The Arcade camera orbits the fighter at about 8 m, 15 degrees down with a 40 degree
vertical FOV (the fighter starts at z = -6 facing the sender wall). Materials mimic the
Unity shaders: NC_Night (StageNeon) is unlit vertex colour x atlas x LIGHT_SCALE plus
glow, NC_Court (StageVertexLit) is lit by a stand-in key, NC_Beam is additive. These
renders are agent review aids only; Unity captures with the match camera's ACES grade
and bloom remain the reference for exposure, colour and emission.
"""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Euler

from lighting import LIGHT_SCALE
from stagekit.preview import B

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
GLOW = 1.6
# name: (target xyz in Unity space, yaw deg (0 = looking north +Z), pitch deg, distance, vertical FOV)
VIEWS = {
    "play": ((0.0, 1.2, -6.0), 0, 15, 8, 40),
    "lob": ((0.0, 1.2, -6.0), 0, -12, 8, 40),
    "side": ((-2.0, 1.2, -6.0), 62, 10, 8, 40),
    "rear": ((0.0, 1.2, -6.0), 180, 10, 8, 40),
    "wide": ((2.0, 5.5, 6.0), 32, 14, 30, 64),
}


def _vc(nt):
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    return vc


def _tex(nt, image):
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Smart"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
    return tex


def _mul(nt, a, b):
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = "MULTIPLY"
    m.inputs["Factor"].default_value = 1.0
    nt.links.new(a, m.inputs[6])
    nt.links.new(b, m.inputs[7])
    return m.outputs[2]


def _night(mat, atlas):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vc, tex = _vc(nt), _tex(nt, atlas)
    albedo = _mul(nt, vc.outputs["Color"], tex.outputs["Color"])
    glow = nt.nodes.new("ShaderNodeMath")
    glow.operation = "MULTIPLY"
    nt.links.new(vc.outputs["Alpha"], glow.inputs[0])
    nt.links.new(tex.outputs["Alpha"], glow.inputs[1])
    strength = nt.nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY_ADD"
    nt.links.new(glow.outputs[0], strength.inputs[0])
    strength.inputs[1].default_value = GLOW
    strength.inputs[2].default_value = LIGHT_SCALE
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(albedo, em.inputs["Color"])
    nt.links.new(strength.outputs[0], em.inputs["Strength"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    mat.use_backface_culling = True


def _court(mat, floor):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vc, tex = _vc(nt), _tex(nt, floor)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(_mul(nt, vc.outputs["Color"], tex.outputs["Color"]), bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])


def _beam(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vc = _vc(nt)
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(vc.outputs["Color"], em.inputs["Color"])
    strength = nt.nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY"
    strength.inputs[1].default_value = 3.0
    nt.links.new(vc.outputs["Alpha"], strength.inputs[0])
    nt.links.new(strength.outputs[0], em.inputs["Strength"])
    add = nt.nodes.new("ShaderNodeAddShader")
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    nt.links.new(tr.outputs[0], add.inputs[0])
    nt.links.new(em.outputs[0], add.inputs[1])
    nt.links.new(add.outputs[0], out.inputs["Surface"])
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "BLENDED"
    mat.use_backface_culling = False


def _light(sc, kind, name, pos, color, power, aim=None, size=0.5):
    data = bpy.data.lights.new(name, kind)
    data.color = color
    data.energy = power
    if kind == "SUN":
        data.angle = math.radians(size)
    else:
        data.shadow_soft_size = size
    ob = bpy.data.objects.new(name, data)
    sc.collection.objects.link(ob)
    ob.location = B(pos)
    if aim is not None:
        ob.rotation_euler = (B(aim) - B(pos)).to_track_quat("-Z", "Y").to_euler()
    return ob


def _fighter(sc):
    """FightGuy in a relaxed ready stance at the start, for scale; falls back to a capsule."""
    path = os.path.join(REPO, "client/Unity/Assets/Art/Characters/fightguy/Models/fightguy.fbx")
    try:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path)
        new = [o for o in bpy.data.objects if o not in before]
        arm = next(o for o in new if o.type == "ARMATURE")
        for o in new:
            if o.type in ("CAMERA", "LIGHT") or (o.type == "MESH" and o.name.startswith("Cube")):
                bpy.data.objects.remove(o, do_unlink=True)
        arm.location = B((0.0, 0.0, -6.0))
        arm.rotation_euler = Euler((math.radians(90), 0, 0))
        pose = {"mixamorig:LeftArm": (68, 0, 20), "mixamorig:RightArm": (68, 0, -20),
                "mixamorig:LeftForeArm": (0, 0, 35), "mixamorig:RightForeArm": (0, 0, -35)}
        for name, (rx, ry, rz) in pose.items():
            if name in arm.pose.bones:
                pb = arm.pose.bones[name]
                pb.rotation_mode = "XYZ"
                pb.rotation_euler = Euler((math.radians(rx), math.radians(ry), math.radians(rz)))
        tex = os.path.join(REPO, "client/Unity/Assets/Art/Characters/fightguy/Textures/FightGuy2_BaseColor.png")
        for o in bpy.data.objects:
            if o.type == "MESH" and o.parent == arm:
                for mat in o.data.materials:
                    if mat and mat.use_nodes:
                        for n in mat.node_tree.nodes:
                            if n.type == "TEX_IMAGE" and (n.image is None or not n.image.has_data):
                                n.image = bpy.data.images.load(tex, check_existing=True)
                        for link in list(mat.node_tree.links):
                            if link.to_socket.name == "Normal":
                                mat.node_tree.links.remove(link)
    except Exception as exc:  # preview aid only
        print("PREVIEW_FIGHTER_UNAVAILABLE", exc)


def _ball(sc, pos=(1.4, 3.4, -2.2), radius=0.65):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=B(pos), segments=32, ring_count=16)
    ball = bpy.context.active_object
    mat = bpy.data.materials.new("ball")
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (1.0, 0.62, 0.04, 1)
    p.inputs["Emission Color"].default_value = (0.6, 0.25, 0.02, 1)
    p.inputs["Emission Strength"].default_value = 0.6
    ball.data.materials.append(mat)
    bpy.ops.mesh.primitive_torus_add(major_radius=radius, minor_radius=0.03, location=B((pos[0], 0.035, pos[2])))
    ring = bpy.context.active_object
    rmat = bpy.data.materials.new("ring")
    rmat.use_nodes = True
    nt = rmat.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (1.0, 0.85, 0.25, 1)
    em.inputs["Strength"].default_value = 3.0
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], o.inputs["Surface"])
    ring.data.materials.append(rmat)


def render(objects, out_dir, atlas_path, floor_path, views_filter=""):
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    atlas = bpy.data.images.load(atlas_path)
    floor = bpy.data.images.load(floor_path)
    _night(bpy.data.materials["NC_Night"], atlas)
    _court(bpy.data.materials["NC_Court"], floor)
    _beam(bpy.data.materials["NC_Beam"])
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.eevee.taa_render_samples = 48
    try:
        sc.view_settings.view_transform = "ACES 1.3"
    except Exception:
        sc.view_settings.view_transform = "AgX"
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.01, 0.012, 0.02, 1)
    bg.inputs[1].default_value = 1.0
    # stand-ins for the Unity rig: flood key from above, a fill behind the camera
    _light(sc, "SUN", "Flood Key", (0.0, 40.0, -14.0), (0.93, 0.96, 1.0), 6.0, aim=(0.0, 0.0, 0.0), size=8)
    _light(sc, "POINT", "Court Fill", (0.0, 9.0, -11.0), (1.0, 0.95, 0.88), 2500, size=2.0)
    _fighter(sc)
    _ball(sc)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens_unit = "FOV"
    cam_data.sensor_fit = "VERTICAL"
    cam_data.clip_start = 0.1
    cam_data.clip_end = 400
    cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    chosen = [v for v in VIEWS if not views_filter or v in views_filter.split(",")]
    for name in chosen:
        target, yaw, pitch, dist, fov = VIEWS[name]
        y, p = math.radians(yaw), math.radians(pitch)
        fwd = (math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p))
        pos = [target[i] - fwd[i] * dist for i in range(3)]
        pos[0] = max(-19.7, min(19.7, pos[0]))
        pos[2] = max(-19.7, min(19.7, pos[2]))
        pos[1] = max(0.3, min(19.7, pos[1]))
        cam.location = B(pos)
        cam.rotation_euler = (B(target) - B(pos)).to_track_quat("-Z", "Y").to_euler()
        cam.data.angle_y = math.radians(fov)
        sc.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", sc.render.filepath)
