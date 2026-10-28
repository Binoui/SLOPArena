"""Split a generated kit (one mesh, every part an island beside the body) into one model per chosen island.
blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/kit_pieces.py -- \
    <kit.fbx> <out_dir> [name=island,island ...]

Without names it lists the islands (index, size, bounds, mean colour), largest first, and writes
<out_dir>/islands.png: each island of at least 50 vertices rendered on its own with its index. With names it writes
<out_dir>/<name>.fbx (texture embedded) holding the listed islands, in the kit's own placement, for build_rig.py.
The 2026-10-28 outfit kit (take 6): suit=3 wing_l=14 wing_r=13 (wing sides are swapped freely; build_rig.py
puts each on the forearm nearest to it after fitting).
"""
import bpy, bmesh, sys, os, json
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir = os.path.abspath(argv[0]), os.path.abspath(argv[1])
picks = {k: [int(i) for i in v.split(",")] for k, v in (a.split("=", 1) for a in argv[2:])}
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
ob = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
ob.data.transform(ob.matrix_world)
ob.parent = None
ob.matrix_world.identity()
me = ob.data
node = max((n for n in me.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image),
           key=lambda n: n.image.size[0] * n.image.size[1])
img = node.image
w, h = img.size
px = np.empty(w * h * 4, dtype=np.float32)
img.pixels.foreach_get(px)
px = px.reshape(h, w, 4)
uv = me.uv_layers.active.data

bm = bmesh.new()
bm.from_mesh(me)
bm.faces.ensure_lookup_table()
seen, isl = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    stack, faces = [f], []
    seen.add(f.index)
    while stack:
        g = stack.pop()
        faces.append(g.index)
        for e in g.edges:
            for k in e.link_faces:
                if k.index not in seen:
                    seen.add(k.index)
                    stack.append(k)
    isl.append(faces)
bm.free()


def info(faces):
    vs = sorted({v for i in faces for v in me.polygons[i].vertices})
    p = np.array([tuple(me.vertices[i].co) for i in vs])
    cols = [px[min(h - 1, int(uv[li].uv[1] * h)), min(w - 1, int(uv[li].uv[0] * w)), :3]
            for i in faces[::max(1, len(faces) // 200)] for li in me.polygons[i].loop_indices]
    return {"faces": len(faces), "verts": len(vs), "lo": p.min(0).round(3).tolist(), "hi": p.max(0).round(3).tolist(),
            "rgb": np.mean(cols, 0).round(2).tolist()}


isl.sort(key=lambda fs: -len({v for i in fs for v in me.polygons[i].vertices}))
table = [info(fs) for fs in isl]


def keep_only(faces, label):
    """A copy of the kit mesh holding only `faces`."""
    cp = ob.copy()
    cp.data = me.copy()
    cp.name = cp.data.name = label
    cp.hide_render = False
    bpy.context.scene.collection.objects.link(cp)
    bm = bmesh.new()
    bm.from_mesh(cp.data)
    bm.faces.ensure_lookup_table()
    keep = set(faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(cp.data)
    bm.free()
    return cp


if not picks:
    for i, d in enumerate(table):
        print("ISLAND", i, json.dumps(d))
    # Contact sheet: each island alone, framed from the front (the kit faces -Y).
    from PIL import Image, ImageDraw
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x = sc.render.resolution_y = 256
    sc.render.film_transparent = True
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    ob.hide_render = True
    tiles = []
    for i, fs in enumerate(isl):
        if table[i]["verts"] < 50:
            continue
        cp = keep_only(fs, f"island_{i}")
        lo, hi = np.array(table[i]["lo"]), np.array(table[i]["hi"])
        c = (lo + hi) / 2
        cam.location = (c[0], lo[1] - 2, c[2])
        cam.rotation_euler = (1.5708, 0, 0)
        cam.data.ortho_scale = max(hi[0] - lo[0], hi[2] - lo[2]) * 1.15
        path = os.path.join(out_dir, f"_island_{i}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cp)
        tiles.append((i, path))
    cols = 6
    sheet = Image.new("RGB", (cols * 256, ((len(tiles) + cols - 1) // cols) * 280), (205, 205, 205))
    d = ImageDraw.Draw(sheet)
    for k, (i, path) in enumerate(tiles):
        t = Image.open(path)
        x, y = (k % cols) * 256, (k // cols) * 280
        sheet.paste(t, (x, y), t)
        d.text((x + 8, y + 258), f"{i}: {table[i]['verts']} verts", fill=(0, 0, 0))
        os.remove(path)
    sheet.save(os.path.join(out_dir, "islands.png"))
    json.dump(table, open(os.path.join(out_dir, "islands.json"), "w"), indent=1)
    print("WROTE", os.path.join(out_dir, "islands.png"))
else:
    ob.hide_set(True)
    for label, idx in picks.items():
        cp = keep_only([f for i in idx for f in isl[i]], label)
        bpy.ops.object.select_all(action="DESELECT")
        cp.select_set(True)
        bpy.context.view_layer.objects.active = cp
        dst = os.path.join(out_dir, f"{label}.fbx")
        bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"MESH"}, path_mode="COPY",
                                 embed_textures=True, mesh_smooth_type="OFF")
        bpy.data.objects.remove(cp)
        print("WROTE", dst, json.dumps(info([f for i in idx for f in isl[i]])))
