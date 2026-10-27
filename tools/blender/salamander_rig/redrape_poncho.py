"""Re-drape the built poncho with his arms down, so its rest shape hangs over the arms instead of resting on them.

blender -b --factory-startup --python-exit-code 1 --python redrape_poncho.py -- <build_dir> <out_dir> ['<json options>']

The draped poncho (drape_poncho.py) was simulated on the upload A-pose, arms 45 degrees out, so its sides lie on the
arms at shoulder height. Skinned to the torso only (build_rig.py poncho_torso=1, for Unity cloth), those sides then
stayed up as a stiff shelf over the lowered arms (2026-10-27). Here the arms are lowered (Arm bones only: the poncho's
torso bones keep their rest), the poncho is cloth-simulated on that body with its collar pinned, and the result
replaces the poncho's rest shape. Writes <out_dir>/Salamander.fbx, Salamander_rig.blend and textures/ (copied).
Options: arms (degrees down from the T-rest, 72), frames (50), pin_r (0.10 m around the neck joint), pin_r1 (0.15),
bending (2), clearance (0.006).
Optional solver controls: tension (15), mass (0.15), quality (10), self (false).
preclear=true starts UV-mapped blanket panels outside the posed colliders, avoiding embedded jeans at the hem.
jeans_collision=false uses the jeans for initial clearance only, avoiding catches on their extracted patch edges.
"""
import bpy, bmesh, sys, os, json, math, shutil
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
BUILD, OUT = (os.path.abspath(a) for a in argv[:2])
O = {"arms": 72, "frames": 50, "pin_r": 0.10, "pin_r1": 0.15, "bending": 2.0, "clearance": 0.006}
if len(argv) > 2:
    O.update(json.loads(argv[2]))
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(BUILD, "Salamander_rig.blend"))
scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
body = next(o for o in scene.objects if o.type == "MESH" and o.name.endswith("_Body"))
poncho = next(o for o in scene.objects if o.type == "MESH" and o.name.endswith("_Poncho"))
P = "mixamorig:"


def rot(bone, axis, deg):
    bpy.context.view_layer.update()
    pb = arm.pose.bones[P + bone]
    ax = (arm.matrix_world.to_3x3().inverted() @ Vector(axis)).normalized()
    R = Matrix.Rotation(math.radians(deg), 4, ax)
    pb.matrix = Matrix.Translation(pb.head) @ R @ Matrix.Translation(-pb.head) @ pb.matrix
    bpy.context.view_layer.update()

for pb in arm.pose.bones:
    pb.matrix_basis.identity()
rot("LeftArm", (0, 1, 0), O["arms"])
rot("RightArm", (0, 1, 0), -O["arms"])
dg = bpy.context.evaluated_depsgraph_get()
# The collider: the body (and jeans) as posed, frozen into plain meshes.
colliders = []
for src in [o for o in scene.objects if o.type == "MESH" and o is not poncho and not o.name.endswith("_Hat")]:
    me = bpy.data.meshes.new_from_object(src.evaluated_get(dg))
    me.transform(src.matrix_world)
    col = bpy.data.objects.new("collide_" + src.name, me)
    scene.collection.objects.link(col)
    m = col.modifiers.new("Collision", "COLLISION")
    col.collision.thickness_outer = O["clearance"]
    col.collision.thickness_inner = O["clearance"]
    col.collision.cloth_friction = 2.0
    colliders.append(col)
# The poncho at rest (its torso bones are at rest in this pose), as a free mesh to simulate.
rest = [poncho.matrix_world @ v.co for v in poncho.data.vertices]
pme = bpy.data.meshes.new_from_object(poncho.evaluated_get(dg))
pme.transform(poncho.matrix_world)
# Evaluated meshes retain deform weights even without the source object's group names.
# A new group at index zero would inherit Hips weights and accidentally pin the hem.
bm = bmesh.new(); bm.from_mesh(pme)
deform = bm.verts.layers.deform.active
if deform:
    bm.verts.layers.deform.remove(deform)
bm.to_mesh(pme); bm.free()
assert not any(v.groups for v in pme.vertices), "Temporary cloth must not inherit skin weights"
sim = bpy.data.objects.new("poncho_sim", pme)
scene.collection.objects.link(sim)
neck = arm.matrix_world @ arm.pose.bones[P + "Neck"].head
preclear_moved = 0.0
if O.get("preclear", False):
    points, triangles = [], []
    for col in colliders:
        offset = len(points); points.extend(v.co.copy() for v in col.data.vertices)
        col.data.calc_loop_triangles()
        triangles.extend(tuple(offset + i for i in t.vertices) for t in col.data.loop_triangles)
    surface = BVHTree.FromPolygons(points, triangles, all_triangles=True)
    # The unfolded island's lower half is the front panel, upper half the back.
    uvv = {}
    for loop in pme.loops:
        uvv[loop.vertex_index] = pme.uv_layers.active.data[loop.index].uv.y
    for v in pme.vertices:
        if (v.co - neck).length < O["pin_r1"]:
            continue
        side = -1 if uvv[v.index] < 0.5 else 1
        loc, _, _, _ = surface.ray_cast(Vector((v.co.x, side * 2, v.co.z)), Vector((0, -side, 0)), 4)
        if loc is not None:
            move = max(0, side * (loc.y - v.co.y) + 2.5 * O["clearance"])
            v.co.y += side * move
            preclear_moved = max(preclear_moved, move)
    pme.update()
if not O.get("jeans_collision", True):
    for col in colliders:
        if col.name.endswith("_Jeans"):
            col.modifiers.clear()
pin = sim.vertex_groups.new(name="pin")
for v in pme.vertices:
    d = (v.co - neck).length
    w = 1.0 if d < O["pin_r"] else max(0.0, 1 - (d - O["pin_r"]) / (O["pin_r1"] - O["pin_r"]))
    if w > 0:
        pin.add([v.index], w, "REPLACE")
cl = sim.modifiers.new("Cloth", "CLOTH")
s = cl.settings
s.quality = O.get("quality", 10); s.mass = O.get("mass", 0.15)
s.tension_stiffness = s.compression_stiffness = O.get("tension", 15.0)
s.shear_stiffness = O.get("tension", 15.0) / 3
s.bending_stiffness = O["bending"]; s.air_damping = 1.0; s.vertex_group_mass = "pin"; s.pin_stiffness = 1.0
cs = cl.collision_settings
cs.use_collision = True; cs.distance_min = O["clearance"]; cs.collision_quality = 4
cs.use_self_collision = bool(O.get("self", False)); cs.self_distance_min = 0.003
cl.point_cache.frame_start = 1; cl.point_cache.frame_end = O["frames"]
scene.frame_start, scene.frame_end = 1, O["frames"]
for f in range(1, O["frames"] + 1):
    scene.frame_set(f)
ev = sim.evaluated_get(bpy.context.evaluated_depsgraph_get())
draped = [v.co.copy() for v in ev.data.vertices]
assert len(draped) == len(poncho.data.vertices)
moved = max((a - b).length for a, b in zip(draped, rest))
Wi = poncho.matrix_world.inverted()
for v, p in zip(poncho.data.vertices, draped):
    v.co = Wi @ p
poncho.data.update()
for o in colliders + [sim]:
    bpy.data.objects.remove(o)
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()
print("REDRAPE moved max", round(moved, 3), "m; verts", len(draped))

shutil.copytree(os.path.join(BUILD, "textures"), os.path.join(OUT, "textures"), dirs_exist_ok=True)
for img in bpy.data.images:
    if img.filepath:
        img.filepath = os.path.join(OUT, "textures", os.path.basename(img.filepath))
meshes = [o for o in scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Salamander.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="OFF", use_armature_deform_only=False, armature_nodetype="NULL")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Salamander_rig.blend"))
json.dump({"options": O, "maxMovedMetres": moved, "preclearMaxMetres": preclear_moved},
          open(os.path.join(OUT, "redrape.json"), "w"), indent=1)
print("DONE", OUT)
