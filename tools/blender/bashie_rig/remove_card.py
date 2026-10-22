"""Delete flat backdrop cards (an image background the generator turned into geometry) and re-export.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/bashie_rig/remove_card.py -- \
    <generated.fbx> <out_dir> <name>
A card is an island of at most 8 faces that is flat (thinner than 0.1 mm along some axis) and covers more
than 20% of the model's tallest bbox side squared. Writes <name>.fbx (texture embedded) and <name>.glb.
"""
import bpy, bmesh, sys, os

src, out_dir, name = sys.argv[sys.argv.index("--") + 1:][:3]
os.makedirs(out_dir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
assert len(meshes) == 1, [o.name for o in meshes]
o = meshes[0]
mw = o.matrix_world
bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
world = [mw @ v.co for v in bm.verts]
size = max(max(p[i] for p in world) - min(p[i] for p in world) for i in range(3))
seen, doomed = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    comp, stack = [], [f]
    seen.add(f.index)
    while stack:
        g = stack.pop(); comp.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index); stack.append(h)
    vs = {v.index for g in comp for v in g.verts}
    pts = [world[i] for i in vs]
    ext = [max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)]
    area = sum(g.calc_area() for g in comp) * (mw.to_scale()[0] ** 2)
    if len(comp) <= 8 and min(ext) < 1e-4 and area > 0.2 * size * size:
        print(f"CARD faces={len(comp)} verts={len(vs)} extent={[round(x, 3) for x in ext]} area={area:.3f} m2")
        doomed.extend(comp)
assert doomed, "no card found"
before = (len(bm.verts), len(bm.faces))
bmesh.ops.delete(bm, geom=list({v for f in doomed for v in f.verts}), context="VERTS")
bm.to_mesh(o.data); bm.free(); o.data.update()
print(f"verts/faces {before} -> {(len(o.data.vertices), len(o.data.polygons))}")
o.name = o.data.name = name
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, f"{name}.fbx"), path_mode="COPY", embed_textures=True,
                         add_leaf_bones=False, bake_anim=False)
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, f"{name}.glb"), export_format="GLB")
print("WROTE", out_dir)
