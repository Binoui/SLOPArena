"""Export the dressed salamander: a Mixamo upload (body, boots, mustache, straw as one mesh) and a dressed GLB.

blender -b --python-exit-code 1 <dressed.blend> --python export_mixamo.py -- <texture.png> <out_dir> [tex_size]
The hat and poncho stay out of the upload: they go on in Blender after rigging (hat as a prop bone for E,
poncho skinned from the body), so Mixamo's auto-rig sees a plain body with clear arms.
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
tex, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])
size = int(argv[2]) if len(argv) > 2 else 2048
os.makedirs(out_dir, exist_ok=True)
scene = bpy.context.scene
for o in list(scene.objects):
    if o.type in ("CAMERA", "LIGHT"):
        bpy.data.objects.remove(o)

img = bpy.data.images.load(tex)
if img.size[0] != size:
    img.scale(size, size)
img.filepath_raw = os.path.join(out_dir, "salamander_basecolor.png")
img.file_format = "PNG"
img.save()
img = bpy.data.images.load(os.path.join(out_dir, "salamander_basecolor.png"))
mat = bpy.data.materials.new("salamander"); mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
meshes = [o for o in scene.objects if o.type == "MESH"]
for o in meshes:
    o.data.materials.clear(); o.data.materials.append(mat)

def select(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]

# Dressed GLB (every part as its own node) for viewing.
select(meshes)
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, "salamander-dressed.glb"), export_format="GLB", use_selection=True)

# Mixamo upload: one mesh.
keep = [o for o in meshes if o.name in ("body", "boot_L", "boot_R", "bootbits_L", "bootbits_R", "mustache", "straw")]
copies = []
for o in keep:
    c = o.copy(); c.data = o.data.copy(); scene.collection.objects.link(c); copies.append(c)
select(copies)
bpy.ops.object.join()
up = bpy.context.view_layer.objects.active
up.name = up.data.name = "Salamander"
for o in meshes:
    o.hide_set(True)
select([up])
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, "salamander-mixamo.fbx"), use_selection=True,
                         path_mode="COPY", embed_textures=True, add_leaf_bones=False, bake_anim=False)
V = np.array([v.co[:] for v in up.data.vertices])
print("UPLOAD verts", len(V), "faces", len(up.data.polygons), "bounds", V.min(0).round(3), V.max(0).round(3))

# Verify: re-import into an empty scene.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(out_dir, "salamander-mixamo.fbx"))
obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
o = obs[0]
W = np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices])
head = W[W[:, 2] > 0.76]
snout = head[head[:, 1].argmin()]
print("VERIFY meshes", len(obs), "verts", len(W), "bounds", W.min(0).round(3), W.max(0).round(3),
      "snout", snout.round(3), "images", [(i.name, i.size[:], i.packed_file is not None) for i in bpy.data.images])
print("DONE", out_dir)
