"""Split the Sir Bonk v2 Tripo kit into one object per part and drop the glitches (extra head, grey stick).

blender -b --factory-startup --python-exit-code 1 --python split.py -- <kit.fbx> <texture.png> <out.blend>
Kit frame (as imported, transforms applied): everything faces -Y, Z up. The body stands at x<-0.17; the parts
column is at x>0.16 with "a" items on the -X side and "b" items on the +X side.
"""
import bpy, bmesh, sys, os, json
import numpy as np
from mathutils import Vector

fbx, tex, out = (os.path.abspath(a) for a in sys.argv[sys.argv.index("--") + 1:][:3])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
src = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
for o in list(bpy.context.scene.objects):
    if o is not src:
        bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active = src; src.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# one fresh material with the kit texture (the imported one is replaced, so the image path is explicit)
img = bpy.data.images.load(tex, check_existing=True)
mat = bpy.data.materials.new("SirBonkV2"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
src.data.materials.clear(); src.data.materials.append(mat)

def classify(c):
    x, y, z = c
    if x < -0.17:
        return "body"
    if x < -0.015:
        return None if z > 0.75 else "sword"  # the extra head is a glitch
    if x < 0.16:
        return "helm" if z > 0.74 else "cape"
    s = "a" if x < 0.336 else "b"
    if z > 0.88:
        return "pauldron_" + s
    if z > 0.735:
        return "chestplate"
    if z > 0.585:
        return "colander_a" if x < 0.232 else "wrap_a" if x < 0.33 else "wrap_b" if x < 0.43 else "colander_b"
    if z > 0.44:
        return "tabard" if x < 0.33 else "sash"
    if z > 0.325:
        return "skirt"
    if z > 0.245:
        return "knee_" + ("a" if x < 0.33 else "b")
    if z > 0.14:
        return "pants_" + ("a" if x < 0.33 else "b")
    return "boot_" + s

bm = bmesh.new(); bm.from_mesh(src.data); bm.faces.ensure_lookup_table()
# the grey stick: a few long triangles fused into the crest island
long_faces = [f for f in bm.faces if max(e.calc_length() for e in f.edges) > 0.06 and f.calc_center_median().x > -0.3
              and f.calc_center_median().x < 0.16 and max(v.co.z for v in f.verts) > 0.86]
print("STICK faces", len(long_faces))
bmesh.ops.delete(bm, geom=long_faces, context="FACES")
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context="VERTS")
bm.faces.ensure_lookup_table()
seen, label = set(), {}
for f in bm.faces:
    if f.index in seen:
        continue
    comp, st = [], [f]; seen.add(f.index)
    while st:
        g = st.pop(); comp.append(g)
        for v in g.verts:
            for h in v.link_faces:
                if h.index not in seen:
                    seen.add(h.index); st.append(h)
    pts = np.array([v.co[:] for g in comp for v in g.verts])
    name = classify(pts.mean(0))
    # the bucket's wire handle loops under the chin: its own object so it can bend round the jaw
    if name == "helm" and pts[:, 2].min() < 0.75 and pts[:, 2].max() < 0.82 and np.ptp(pts[:, 0]) > 0.09:
        name = "helm_handle"
    for g in comp:
        label[g.index] = name
names = sorted({n for n in label.values() if n})
report = {}
for n in names:
    b2 = bm.copy(); b2.faces.ensure_lookup_table()
    drop = [f for f in b2.faces if label.get(f.index) != n]
    bmesh.ops.delete(b2, geom=drop, context="FACES")
    bmesh.ops.delete(b2, geom=[v for v in b2.verts if not v.link_faces], context="VERTS")
    me = bpy.data.meshes.new(n); b2.to_mesh(me); b2.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(n, me); bpy.context.scene.collection.objects.link(ob)
    P = np.array([v.co[:] for v in me.vertices])
    report[n] = {"verts": len(P), "lo": P.min(0).round(4).tolist(), "hi": P.max(0).round(4).tolist()}
    print("PART", n, report[n])
print("DROPPED faces", sum(1 for v in label.values() if v is None))
bpy.data.objects.remove(src)
json.dump(report, open(os.path.splitext(out)[0] + ".json", "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=out)
