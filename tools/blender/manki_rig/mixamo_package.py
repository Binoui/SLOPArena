"""Repackage a generated (unrigged) character FBX for Mixamo's auto-rigger.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/mixamo_package.py -- \
    <generated.fbx> <out_dir> <name> [preview_tex_size=2048]

Mixamo rejected Manki v3's generator FBX (one quad-dominant mesh, four embedded 4K/2K PBR maps) and accepted
this re-export, which matches the Manki 2 upload it had accepted: one triangulated mesh, one material, a single
colour map beside the model (not embedded), transforms applied, feet at the origin. The exact rejection cause
was not isolated. Writes:
  <out_dir>/<name>_mixamo.zip         upload this (FBX + preview colour map)
  <out_dir>/<name>_BaseColor_source.* the generator's colour map, byte for byte (the face step starts from it)
UVs are untouched, so the rigged FBX that comes back still matches the full-resolution source map.
"""
import bpy, bmesh, sys, os, json, zipfile

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name = argv[:3]
tex_size = int(argv[3]) if len(argv) > 3 else 2048
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
if len(meshes) != 1:
    raise SystemExit(f"mixamo_package: expected one mesh, found {len(meshes)}")
o = meshes[0]
for x in list(bpy.data.objects):
    if x is not o:
        bpy.data.objects.remove(x, do_unlink=True)
o.name = o.data.name = name

base = next((n.image for s in o.material_slots if s.material and s.material.use_nodes
             for n in s.material.node_tree.nodes
             if n.type == "TEX_IMAGE" and n.image and "basecolor" in n.image.name.lower()), None)
if base is None:  # Tripo's FBX names its map "Diffuse Texture": take the image feeding Base Color
    base = next((l.from_node.image for s in o.material_slots if s.material and s.material.use_nodes
                 for l in s.material.node_tree.links
                 if l.to_socket.name == "Base Color" and l.from_node.type == "TEX_IMAGE" and l.from_node.image), None)
if base is None:
    raise SystemExit("mixamo_package: no BaseColor image found")
ext = os.path.splitext(base.name)[1] or ".png"
source_path = os.path.join(out_dir, f"{name}_BaseColor_source{ext}")
if base.packed_file:
    open(source_path, "wb").write(base.packed_file.data)
else:
    base.save(filepath=source_path)

preview = base.copy()
preview.name = f"{name}_BaseColor"
if preview.size[0] != tex_size:
    preview.scale(tex_size, tex_size)
preview_path = os.path.join(out_dir, f"{name}_BaseColor.jpg")
bpy.context.scene.render.image_settings.quality = 92
preview.filepath_raw = preview_path
preview.file_format = "JPEG"
preview.save()
mat = bpy.data.materials.new(f"{name}_mat")
mat.use_nodes = True
tn = mat.node_tree.nodes.new("ShaderNodeTexImage")
tn.image = bpy.data.images.load(preview_path)
mat.node_tree.links.new(tn.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
o.data.materials.clear()
o.data.materials.append(mat)

bpy.context.view_layer.objects.active = o
o.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bm = bmesh.new()
bm.from_mesh(o.data)
bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
bm.to_mesh(o.data)
bm.free()
xs = [v.co.x for v in o.data.vertices]; ys = [v.co.y for v in o.data.vertices]; zs = [v.co.z for v in o.data.vertices]
off = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs))
for v in o.data.vertices:
    v.co.x -= off[0]; v.co.y -= off[1]; v.co.z -= off[2]
o.data.update()

fbx = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH"}, path_mode="RELATIVE",
                         embed_textures=False, mesh_smooth_type="OFF", add_leaf_bones=False, bake_anim=False)
zpath = os.path.join(out_dir, f"{name}_mixamo.zip")
with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(fbx, arcname=os.path.basename(fbx))
    z.write(preview_path, arcname=os.path.basename(preview_path))
print("REPORT", json.dumps({"zip": zpath, "source_colour_map": source_path, "source_size": list(base.size),
                            "triangles": len(o.data.polygons), "vertices": len(o.data.vertices),
                            "height_m": round(max(zs) - min(zs), 3)}))
