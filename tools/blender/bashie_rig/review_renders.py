"""Stress-test Bashie's rig: per-island weight shares and posed renders (Blender approximation).

blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/review_renders.py -- \
    <rig.fbx> <basecolor.png | textures_dir> <out_dir>
Works on the Mixamo download (its take is the base pose) and on build_rig.py's output (T-pose rest). Given a
directory, each material gets <material>.png from it (build_rig.py's textures/); given a file, every material gets it.
Adapted from tools/blender/bonk_rig/review_renders.py, which expects a built .blend.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

fbx, tex, out_dir = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:3])
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
# The body: build_rig.py names it <name>_Body; the shell, whistle and cord are skinned too, so else take the largest.
skinned = [o for o in scene.objects if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers)]
body = next((o for o in skinned if o.name.endswith("_Body")), None) or max(skinned, key=lambda o: len(o.data.vertices))
meshes = [o for o in scene.objects if o.type == "MESH"]
P = "mixamorig:"
BASE = {}
if arm.animation_data and arm.animation_data.action:
    act = arm.animation_data.action
    print("ACTION", act.name, "frames", tuple(act.frame_range))
    bpy.context.scene.frame_set(int(act.frame_range[0]))
    bpy.context.view_layer.update()
    BASE = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data_clear()
    for pb in arm.pose.bones:
        pb.matrix_basis = BASE[pb.name]
    bpy.context.view_layer.update()
def image_for(material):
    path = os.path.join(tex, material.name + ".png") if os.path.isdir(tex) else tex
    return bpy.data.images.load(path, check_existing=True) if os.path.isfile(path) else None
for s in [sl for ob in meshes for sl in ob.material_slots]:
    m = s.material
    img = image_for(m)
    if img is None:
        continue
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Metallic"].default_value = 0.0
    if "Alpha" in bsdf.inputs:
        for l in list(bsdf.inputs["Alpha"].links): nt.links.remove(l)
        bsdf.inputs["Alpha"].default_value = 1.0

# --- weight shares per island (which bones move each separate piece) ---
me = body.data
names = {g.index: g.name.replace(P, "") for g in body.vertex_groups}
bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
seen = set(); islands = []
for f in bm.faces:
    if f.index in seen: continue
    comp = set(); stack = [f]; seen.add(f.index)
    while stack:
        g = stack.pop(); comp.update(v.index for v in g.verts)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); stack.append(h)
    islands.append(comp)
bm.free()
mw = body.matrix_world
for comp in sorted(islands, key=len, reverse=True)[:9]:
    share = {}
    for i in comp:
        for g in me.vertices[i].groups:
            share[names[g.group]] = share.get(names[g.group], 0) + g.weight
    tot = sum(share.values()) or 1
    c = sum((mw @ me.vertices[i].co for i in comp), Vector()) / len(comp)
    top = sorted(share.items(), key=lambda kv: -kv[1])[:7]
    print(f"WEIGHTS island verts={len(comp)} centroid=({c.x:.2f},{c.y:.2f},{c.z:.2f}) " +
          ", ".join(f"{n} {100 * w / tot:.0f}%" for n, w in top))

# --- scene ---
world = bpy.data.worlds.new("review"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.72, 0.72, 0.75, 1)
for rot, e in (((math.radians(50), 0, math.radians(30)), 2.6), ((math.radians(65), 0, math.radians(200)), 1.4)):
    d = bpy.data.lights.new("sun", "SUN"); d.energy = e
    o = bpy.data.objects.new("sun", d); scene.collection.objects.link(o); o.rotation_euler = rot
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
cam_d = bpy.data.cameras.new("cam"); cam_d.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_d); scene.collection.objects.link(cam); scene.camera = cam
bpy.context.view_layer.update()
ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
zs = [(ev.matrix_world @ v.co).z for v in ev.data.vertices]
H = max(zs) - min(zs)
centre = Vector((0, 0, min(zs) + H / 2))

def shoot(name, target, yaw_deg, pitch_deg, ortho, res=(600, 800)):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = target + direction * 6
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_d.ortho_scale = ortho; cam_d.clip_end = 20
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)

def rot_world(bone, axis_world, deg):
    bpy.context.view_layer.update()
    pb = arm.pose.bones[P + bone]
    axis = (arm.matrix_world.to_3x3().inverted() @ Vector(axis_world)).normalized()
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(pb.head) @ R @ Matrix.Translation(-pb.head) @ pb.matrix
    bpy.context.view_layer.update()

def move_hips(dz):
    bpy.context.view_layer.update()
    pb = arm.pose.bones[P + "Hips"]
    pb.matrix = Matrix.Translation(arm.matrix_world.to_3x3().inverted() @ Vector((0, 0, dz))) @ pb.matrix
    bpy.context.view_layer.update()

def reset():
    for pb in arm.pose.bones: pb.matrix_basis = BASE.get(pb.name, Matrix.Identity(4)).copy()
    bpy.context.view_layer.update()

def arm_angle(side):
    pb = arm.pose.bones[P + side + "Arm"]
    v = (arm.matrix_world.to_3x3() @ (pb.tail - pb.head)).normalized()
    return math.degrees(math.asin(max(-1, min(1, v.z))))

reset()
rest = {s: arm_angle(s) for s in ("Left", "Right")}
print("REST arm angles", {k: round(v, 1) for k, v in rest.items()})
def arms_to(deg):  # deg above horizontal; + raises. Left raises with -Y rotation, right with +Y.
    return [("LeftArm", (0, 1, 0), -(deg - rest["Left"])), ("RightArm", (0, 1, 0), (deg - rest["Right"]))]

S = 1.25 * H
for name, yaw in (("rest_front", 0), ("rest_34", 35), ("rest_side", 90), ("rest_back", 180), ("rest_34back", 215)):
    shoot(name, centre, yaw, 5, S)
DOWN = arms_to(-75)
POSES = {
    "tpose": arms_to(0),
    "arms_up": arms_to(70),
    "overhead": arms_to(30) + [("LeftArm", (0, 0, 1), -60), ("RightArm", (0, 0, 1), 60),
                               ("LeftForeArm", (0, 0, 1), -60), ("RightForeArm", (0, 0, 1), 60), ("Spine2", (1, 0, 0), 12)],
    "kick": DOWN + [("LeftUpLeg", (1, 0, 0), -80), ("LeftLeg", (1, 0, 0), 45)],
    "run_back_leg": DOWN + [("LeftUpLeg", (1, 0, 0), 40), ("LeftLeg", (1, 0, 0), 70), ("RightUpLeg", (1, 0, 0), -35)],
    "squat": DOWN + [("LeftUpLeg", (1, 0, 0), -75), ("RightUpLeg", (1, 0, 0), -75), ("LeftLeg", (1, 0, 0), 110),
                     ("RightLeg", (1, 0, 0), 110), ("LeftFoot", (1, 0, 0), -35), ("RightFoot", (1, 0, 0), -35)],
    "twist_look": DOWN + [("Spine2", (0, 0, 1), 35), ("Spine1", (0, 0, 1), 15), ("Head", (0, 0, 1), 40)],
    "bend_fwd": DOWN + [("Spine", (1, 0, 0), -25), ("Spine1", (1, 0, 0), -20), ("Spine2", (1, 0, 0), -15)],
}
for name, ops in POSES.items():
    reset()
    for bone, axis, deg in ops:
        rot_world(bone, axis, deg)
    if name == "squat":
        move_hips(-0.3 * H)
    shoot("pose_" + name + "_front", centre, 0, 5, S * 1.1)
    shoot("pose_" + name + "_34back", centre, 215, 10, S * 1.1)
    if name in ("kick", "run_back_leg", "arms_up", "bend_fwd"):
        shoot("pose_" + name + "_side", centre, 90, 5, S * 1.1)
reset()
print("RENDERED", out_dir)
