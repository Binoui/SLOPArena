"""Lifts Sir Bonk's left pauldron, the pot lid he throws for Q, off a built rig (2026-10-22).
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bonk_rig/lid_split.py -- \
    <build_dir>/SirBonk_rig.blend <out_dir> [name=SirBonk]

The lid is every island that rides only his left shoulder and upper arm: the dome and four fittings (build_rig.py
weights them rigidly that way). Writes:
- <out_dir>/<name>.fbx: the body without the lid, with build_rig.py's export settings, so nothing else changes;
- <out_dir>/<name>_Lid.fbx: the lid alone, a static mesh at its place in the model (object at the origin, the
  vertices where they were), so in Unity it lines up with the body when placed at the model's root;
- <out_dir>/<name>_rig.blend and <out_dir>/<name>_lid.json (the islands, vertex counts and the lid's centre).
`lid_import.eval.cs` then makes the lid a prop that WeaponAttach puts back on his shoulder."""
import bpy, bmesh, sys, os, json
from collections import defaultdict
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
blend, out_dir = (os.path.abspath(a) for a in argv[:2])
name = argv[2] if len(argv) > 2 else "SirBonk"
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=blend)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
me = body.data
groups = {g.index: g.name for g in body.vertex_groups}
SHOULDER = {"mixamorig:LeftShoulder", "mixamorig:LeftArm"}


def islands():
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v0 in bm.verts:
        if v0.index in seen:
            continue
        stack, comp = [v0], []
        seen.add(v0.index)
        while stack:
            v = stack.pop()
            comp.append(v.index)
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        out.append(comp)
    bm.free()
    return out


lid, found = set(), []
for comp in islands():
    acc = defaultdict(float)
    for i in comp:
        for g in me.vertices[i].groups:
            acc[groups.get(g.group, "?")] += g.weight
    total = sum(acc.values()) or 1.0
    if sum(x for n, x in acc.items() if n in SHOULDER) / total < 0.999:
        continue
    lid.update(comp)
    found.append({"verts": len(comp), "weights": {n: round(x / total, 3) for n, x in acc.items()}})
if len(found) != 5:
    raise SystemExit(f"expected the dome and four fittings on his left shoulder, found {len(found)} islands")

world = body.matrix_world.copy()
points = [world @ me.vertices[i].co for i in lid]
centre = sum(points, Vector()) / len(points)
report = {"source": blend, "islands": found, "lid_verts": len(lid), "body_verts_before": len(me.vertices),
          "lid_centre_blender": [round(x, 5) for x in centre]}

# The lid: a copy of the body's mesh keeping only the lid, baked to world space, with no armature or weights.
lid_mesh = me.copy()
lid_mesh.name = f"{name}_Lid"
bm = bmesh.new()
bm.from_mesh(lid_mesh)
bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in lid], context="VERTS")
bm.to_mesh(lid_mesh)
bm.free()
lid_mesh.transform(world)
lid_obj = bpy.data.objects.new(f"{name}_Lid", lid_mesh)
bpy.context.scene.collection.objects.link(lid_obj)
lid_obj.vertex_groups.clear()

# The body without it.
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in sorted(lid)], context="VERTS")
bm.to_mesh(me)
bm.free()
me.update()
report["body_verts_after"] = len(me.vertices)

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = arm
fbx = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="OFF", use_armature_deform_only=False, armature_nodetype="NULL")
bpy.ops.object.select_all(action="DESELECT")
lid_obj.select_set(True)
bpy.context.view_layer.objects.active = lid_obj
lid_fbx = os.path.join(out_dir, f"{name}_Lid.fbx")
bpy.ops.export_scene.fbx(filepath=lid_fbx, use_selection=True, object_types={"MESH"}, add_leaf_bones=False,
                         bake_anim=False, path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, f"{name}_rig.blend"))
report.update({"fbx": fbx, "lid_fbx": lid_fbx})
json.dump(report, open(os.path.join(out_dir, f"{name}_lid.json"), "w"), indent=1)
print("REPORT", json.dumps(report))
