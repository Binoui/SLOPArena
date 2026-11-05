"""Eevee key-frame renders of the kraken clips on the real boat, and a contact sheet.

Review aids only (the Unity unit judges the real look): the boat comes from boat.py with
preview.py's materials, the sea is a flat translucent stand-in at bay.WATER_Y and the foam rings
mark where arms cross the water. Two cameras per key frame:

- fight: CameraMount's Steady framing (pitch 35, vertical FOV 40, 15 m from an anchor 0.2 m above
  the player's ground, yaw towards the opponent), with two 2 m capsules as fighters (red player,
  blue opponent) beside the lane, looking towards the kraken's rail;
- stage: a stage-wide camera from across the boat, so the face shows.
"""
from __future__ import annotations

import math
import os
import random
import subprocess

import bmesh
import bpy
from mathutils import Vector

from stagekit import geo
from stagekit import blender as kit
import bay as BAY
import boat as BOAT
import kraken as K
import kraken_clips as KC
from kraken_clips import apply_frame
import layout as LAYOUT
import palette as P
import textures as T

W = BAY.WATER_Y
VARIANTS = ("port.mid", "starboard.fore")
RES = (800, 450)
RNG = random.Random(3)

# column label -> (clip suffix or "lurk", normalised time)
KEYS = [("lurk", "lurk", 0.5), ("rise", "rise", 0.55), ("rise end", "rise", 1.0), ("warn", "warn", 0.25),
        ("slam apex", "slam", 0.35), ("slam hit", "slam", 0.75), ("lie", "lie", 0.5), ("withdraw", "withdraw", 0.3),
        ("sinking", "withdraw", 0.75)]


def boat_objects(tex_dir):
    data = LAYOUT.load(spec_only=True)
    mats = ["SD_Wood", "SD_Deck", "SD_Hull"]

    class Shim:
        def __init__(self):
            self.data = data
            self.groups = {}

        def part(self, role, group, owner, fn):
            m = geo.Mesh(owner, mats)
            fn(m)
            self.groups.setdefault(group, geo.Mesh(group, mats)).extend(m)

    sh = Shim()
    for name in mats:
        bpy.data.materials.get(name) or bpy.data.materials.new(name)
    BOAT.Boat(sh).build_all()
    os.makedirs(tex_dir, exist_ok=True)
    paths = {k: os.path.join(tex_dir, k + ".png") for k in ("deck", "hull", "atlas")}
    if not all(os.path.exists(p) for p in paths.values()):
        T.deck(paths["deck"], data["spec"])
        T.hull(paths["hull"], data["spec"])
        T.atlas(paths["atlas"])
    objs = kit.build_objects(sh.groups)
    import preview as PV
    images = {}
    for k, p in paths.items():
        img = bpy.data.images.load(p)
        img.alpha_mode = "CHANNEL_PACKED"
        images[k] = img
    for name in mats:
        PV._material(name, images)
    return objs


def water_plane():
    me = bpy.data.meshes.new("Water")
    s = 900
    me.from_pydata([(-s, -s, W), (s, -s, W), (s, s, W), (-s, s, W)], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new("Water", me)
    bpy.context.scene.collection.objects.link(ob)
    mat = bpy.data.materials.new("Water")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = (*[c ** 2.2 for c in P.SEA_BAY], 1)
    bsdf.inputs["Roughness"].default_value = 0.12
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    tr.inputs["Color"].default_value = (0.55, 0.85, 0.9, 1)
    mx = nt.nodes.new("ShaderNodeMixShader")
    mx.inputs[0].default_value = 0.9
    nt.links.new(tr.outputs[0], mx.inputs[1])
    nt.links.new(bsdf.outputs[0], mx.inputs[2])
    nt.links.new(mx.outputs[0], out.inputs["Surface"])
    ob.visible_shadow = False
    me.materials.append(mat)
    return ob, mx


def ring(verts, faces, c, r0, r1, n=14):
    base = len(verts)
    for k in range(n):
        a = 2 * math.pi * k / n + RNG.uniform(-0.1, 0.1)
        j = RNG.uniform(0.9, 1.15)
        verts.append(c + Vector((math.cos(a) * r0, math.sin(a) * r0, 0.04)))
        verts.append(c + Vector((math.cos(a) * r1 * j, math.sin(a) * r1 * j, 0.04)))
    for k in range(n):
        q = (k + 1) % n
        faces.append((base + 2 * k, base + 2 * k + 1, base + 2 * q + 1, base + 2 * q)[::-1])


def foam(rig, arms):
    """White rings where the posed arms and the head cross the water."""
    old = bpy.data.objects.get("Foam")
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    verts, faces = [], []
    Mw = rig.matrix_world
    for arm in arms:
        pts = []
        for k in range(arm.n):
            pts.append((Mw @ rig.pose.bones[arm.bone(k)].head, k * arm.length_m / arm.n))
        pts.append((Mw @ rig.pose.bones[arm.bone(arm.n - 1)].tail, arm.length_m))
        for (a, sa), (b, sb) in zip(pts, pts[1:]):
            if (a.z - W) * (b.z - W) < 0:
                t = (W - a.z) / (b.z - a.z)
                r = arm.r_at((sa + (sb - sa) * t) / K.S) * K.S * K.ARM_W
                ring(verts, faces, a.lerp(b, t), r * 0.95, r * 0.95 + 0.4 + 0.15 * r)
    head = Mw @ rig.pose.bones["Head"].matrix @ Vector((0, (K.HZ - 1.0) * K.S, 0))
    dz = (W - head.z) / K.S
    if abs(dz) < K.RZ:
        rr = K.RX * math.sqrt(1 - (dz / K.RZ) ** 2) * K.S * 1.12
        ring(verts, faces, Vector((head.x, head.y, W)), rr * 0.98, rr + 0.5, 24)
    if not faces:
        return None
    me = bpy.data.meshes.new("Foam")
    me.from_pydata([tuple(v) for v in verts], [], faces)
    mat = bpy.data.materials.get("FoamMat") or bpy.data.materials.new("FoamMat")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.95, 0.97, 0.97, 1)
    me.materials.append(mat)
    ob = bpy.data.objects.new("Foam", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.visible_shadow = False
    return ob


def capsule(name, pos, col, height=2.0, radius=0.42):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=radius)
    for v in bm.verts:
        v.co.z += (height / 2 - radius) * (1 if v.co.z > 0 else -1)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*[c ** 2.2 for c in col], 1)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = KC.B((pos[0], height / 2, pos[2]))
    return ob


def aim(cam, target, yaw, pitch, dist, fov=40.0):
    y, p = math.radians(yaw), math.radians(pitch)
    fwd = Vector((math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p)))
    pos = Vector(target) - fwd * dist
    cam.location = KC.B(pos)
    cam.rotation_euler = (KC.B(target) - KC.B(pos)).to_track_quat("-Z", "Y").to_euler()
    cam.data.angle_y = math.radians(fov)


def fighters_for(spot):
    s = spot.s
    return Vector((spot.lx + 5 * s, 0.0, -2 * s)), Vector((spot.lx + 1 * s, 0.0, 5 * s))


def fight_view(cam, player, opponent):
    off = opponent - player
    aim(cam, (player.x, 0.2, player.z), math.degrees(math.atan2(off.x, off.z)), 35.0, 15.0)


def stage_view(cam, spot):
    target = ((spot.anchor[0] + spot.lx) / 2, 0.0, spot.s * 5.0)
    aim(cam, target, -32.0 if spot.s > 0 else 148.0, 17.0, 62.0)


def setup_scene():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.eevee.taa_render_samples = 24
    try:
        sc.view_settings.view_transform = "AgX"
        sc.view_settings.look = "AgX - Punchy"
    except Exception as e:
        print("look", e)
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.42, 0.62, 0.88, 1)
    bg.inputs[1].default_value = 1.0
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 4.0
    sun.color = (1.0, 0.96, 0.9)
    sun.angle = math.radians(3)
    so = bpy.data.objects.new("Sun", sun)
    sc.collection.objects.link(so)
    so.rotation_euler = (KC.B((0, 0, 0)) - KC.B((0.55, 0.75, -0.38))).to_track_quat("-Z", "Y").to_euler()
    cd = bpy.data.cameras.new("cam")
    cd.lens_unit = "FOV"
    cd.sensor_fit = "VERTICAL"
    cd.clip_start, cd.clip_end = 0.1, 3000
    cam = bpy.data.objects.new("cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = RES
    return sc, cam


def shoot(sc, path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def frame_at(laid, clip, t):
    frames = laid[clip]
    return frames[round(t * (len(frames) - 1))]


def render(kr, rig, mesh, laid, out_dir, log=print):
    pdir = os.path.join(out_dir, "preview")
    os.makedirs(pdir, exist_ok=True)
    boat_objects(os.path.join(pdir, "tex"))
    water, water_mix = water_plane()
    sc, cam = setup_scene()
    tiles = []
    for name in VARIANTS:
        spot = kr.spots[name]
        rig.matrix_world = spot.F
        player, opponent = fighters_for(spot)
        fighters = [capsule("Player", player, (0.86, 0.22, 0.2)), capsule("Opponent", opponent, (0.2, 0.42, 0.9))]
        for label, clip, t in KEYS:
            apply_frame(rig, frame_at(laid, clip if clip == "lurk" else "%s.%s" % (name, clip), t))
            f = foam(rig, kr.arms)
            water_mix.inputs[0].default_value = 0.8 if clip == "lurk" else 0.92
            for view in ("fight", "stage"):
                for o in fighters:
                    o.hide_render = view != "fight"
                (fight_view(cam, player, opponent) if view == "fight" else stage_view(cam, spot))
                path = os.path.join(pdir, "%s_%s_%s.png" % (name, view, label.replace(" ", "-")))
                shoot(sc, path)
                tiles.append((name, view, label, path))
            if f:
                bpy.data.objects.remove(f, do_unlink=True)
        for o in fighters:
            bpy.data.objects.remove(o, do_unlink=True)
        log("rendered", name)
    # the jab flourish: stage-wide and a close look at the sign
    spot = kr.spots["jab"]
    rig.matrix_world = spot.F
    jab_tiles = []
    for t in (0.3, 0.55):
        apply_frame(rig, frame_at(laid, "jab", t))
        f = foam(rig, kr.arms)
        aim(cam, (-14.0, 0.0, 2.0), 210.0, 20.0, 60.0)
        p1 = os.path.join(pdir, "jab_stage_%02d.png" % round(t * 100))
        shoot(sc, p1)
        aim(cam, (-8.0, 1.5, 0.5), 240.0, 18.0, 20.0)
        p2 = os.path.join(pdir, "jab_sign_%02d.png" % round(t * 100))
        shoot(sc, p2)
        jab_tiles += [p1, p2]
        if f:
            bpy.data.objects.remove(f, do_unlink=True)
    rig.matrix_world.identity()
    sheet = os.path.join(out_dir, "contact-sheet.jpg")
    args = ["magick", "montage"]
    for name, view, label, path in tiles:
        args += ["-label", "%s  %s  %s" % (name, view, label), path]
    for p in jab_tiles:
        args += ["-label", os.path.basename(p)[:-4].replace("_", "  "), p]
    args += ["-tile", "%dx" % len(KEYS), "-geometry", "400x225+3+3", "-pointsize", "13", "-background", "#202020",
             "-fill", "white", "-quality", "88", sheet]
    subprocess.run(args, check=True)
    log("contact sheet", sheet)
    return {"contactSheet": sheet, "renders": len(tiles) + len(jab_tiles), "dir": pdir}
