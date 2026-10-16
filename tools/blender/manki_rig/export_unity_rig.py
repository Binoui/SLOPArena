"""Unity handoff export of the repaired Manki 2 rig with Manki's deformation controls.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/export_unity_rig.py -- <fixed.fbx> <out_dir> Manki2 1.18465
The armature is named Manki_Rig so existing generic clip paths bind. Per side, adds
<Side>UpperArmStretch / <Side>ForearmOffset / <Side>FistDeform with the parent's rest matrix, moves all of
the Arm / ForeArm / Hand skin weights onto them. Mapped fingers stay under the hand as unskinned Humanoid
anchors; skinned copies <Side>Skin<Finger><n> hang under <Side>FistDeform (Unity RotationConstraints make them
follow the anchors). Tail bones become Tail1..TailN so generic tail curves from the old rig do not bind.
Writes <out_dir>/<name>.fbx (no embedded media) and <out_dir>/textures/<name>_BaseColor.<ext>."""
import bpy, sys, os, json

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name = argv[:3]
scale = float(argv[3]) if len(argv) > 3 else 1.0
os.makedirs(os.path.join(out_dir, "textures"), exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
if arm.animation_data:
    arm.animation_data.action = None
arm.name = "Manki_Rig"
arm.data.name = "Manki_Rig"
body.name = f"{name}_Body"
body.data.name = f"{name}_Body"
# Uniform size change on the armature object (the mesh is its child); Unity then imports at factor 1.
arm.scale = arm.scale * scale
bpy.context.view_layer.update()

# Texture: write the original packed bytes untouched (no colour-management round trip).
report = {"images": []}
for slot in body.material_slots:
    mat = slot.material
    if not mat:
        continue
    mat.name = f"{name}_Body"
    for n in mat.node_tree.nodes if mat.use_nodes else []:
        if n.type != "TEX_IMAGE" or not n.image:
            continue
        img = n.image
        linked = [l.to_socket.name for l in mat.node_tree.links if l.from_node == n]
        ext = ".png" if (img.file_format or "PNG").upper() == "PNG" else ".jpg"
        dst = os.path.join(out_dir, "textures", f"{name}_BaseColor{ext}")
        if img.packed_file:
            open(dst, "wb").write(img.packed_file.data)
        else:
            img.filepath_raw = dst
            img.save()
        img.filepath = dst
        if img.packed_file:
            img.unpack(method="REMOVE")
        report["images"].append({"node": n.name, "linked_to": linked, "size": list(img.size),
                                 "format": img.file_format, "written": dst})


# Deformation controls (same names and rest frames as MankiDeformRig.prefab).
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
added = []
skin_map = {}
for side in ("Left", "Right"):
    for parent_name, helper in ((f"mixamorig:{side}Arm", f"{side}UpperArmStretch"),
                                (f"mixamorig:{side}ForeArm", f"{side}ForearmOffset"),
                                (f"mixamorig:{side}Hand", f"{side}FistDeform")):
        p = eb[parent_name]
        h = eb.new(helper)
        h.head, h.tail, h.roll = p.head.copy(), p.tail.copy(), p.roll
        h.parent = p
        h.use_connect = False
        h.use_deform = True
        added.append(helper)
    # Mapped fingers stay under the hand as Humanoid anchors (a transform between two human bones is reset
    # by Humanoid retargeting, so the fist cannot sit between Hand and the mapped fingers). Skinned copies
    # <Side>Skin<Finger><n> hang under the fist; RotationConstraints in Manki2Rig.prefab follow the anchors.
    fist = eb[f"{side}FistDeform"]
    prefix = f"mixamorig:{side}Hand"
    def copy_chain(src, parent):
        c = eb.new(f"{side}Skin{src.name[len(prefix):]}")
        c.head, c.tail, c.roll = src.head.copy(), src.tail.copy(), src.roll
        c.parent = parent
        c.use_connect = False
        c.use_deform = True
        skin_map[src.name] = c.name
        for k in list(src.children):
            copy_chain(k, c)
    for c in list(eb[prefix].children):
        if c.name != fist.name and c.name.startswith(prefix):
            copy_chain(c, fist)
# Tail bones are renamed so Manki's generic tail curves (authored on the old tail's rest frames) do not bind.
for b in list(eb):
    if b.name.startswith("tail:"):
        skin_map[b.name] = "Tail" + b.name[len("tail:"):]
for src, dst in skin_map.items():
    if dst.startswith("Tail"):
        eb[src].name = dst
bpy.ops.object.mode_set(mode="OBJECT")
for src, dst in skin_map.items():
    g = body.vertex_groups.get(src)
    if g is not None:
        g.name = dst
    if not dst.startswith("Tail"):
        arm.data.bones[src].use_deform = False
moved = {}
for side in ("Left", "Right"):
    for src_name, dst_name in ((f"mixamorig:{side}Arm", f"{side}UpperArmStretch"),
                               (f"mixamorig:{side}ForeArm", f"{side}ForearmOffset"),
                               (f"mixamorig:{side}Hand", f"{side}FistDeform")):
        g = body.vertex_groups.get(src_name)
        if g is None:
            moved[dst_name] = 0
            continue
        g.name = dst_name  # all weight moves; the mapped bone stays as an unskinned anchor
        moved[dst_name] = sum(1 for v in body.data.vertices for vg in v.groups if vg.group == g.index and vg.weight > 0)
for b in (f"mixamorig:{s}{n}" for s in ("Left", "Right") for n in ("Arm", "ForeArm", "Hand")):
    arm.data.bones[b].use_deform = False

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = arm
dst = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="OFF", use_armature_deform_only=False, armature_nodetype="NULL")
report.update({"deform_added": added, "skin_copies": skin_map, "deform_moved_verts": moved, "scale": scale, "armature_scale": list(arm.scale), "fbx": dst, "armature": arm.name, "body": body.name, "bones": len(arm.data.bones),
               "verts": len(body.data.vertices), "materials": [s.material.name for s in body.material_slots if s.material]})
print("REPORT", json.dumps(report))
