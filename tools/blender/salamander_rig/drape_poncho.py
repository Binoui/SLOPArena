"""Drape the fitted poncho on the A-pose body with a cloth simulation (collar pinned, gravity, body collider).

blender -b --python-exit-code 1 <dressed.blend> --python drape_poncho.py -- <out_dir> '<json options>'
Options: frames, scale (relative to the current fit, about the neck hole), pin_r (m around the neck hole),
bending, tension, mass, subdiv, clearance, quality, drop (m the poncho is lowered before the sim).
Writes draped.blend (poncho replaced by the draped one) and renders.
"""
import bpy, bmesh, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
O = {"frames": 60, "scale": 1.0, "pin_r": 0.03, "bending": 2.0, "tension": 15.0, "mass": 0.15, "subdiv": 0,
     "clearance": 0.004, "quality": 10, "drop": 0.0, "shrink": 0.0, "self": True}
if len(argv) > 1:
    O.update(json.loads(argv[1]))
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene
body, po = bpy.data.objects["body"], bpy.data.objects["poncho"]
for o in scene.objects:
    if o.type == "MESH" and o not in (body, po):
        o.hide_viewport = False

# neck hole loop
bm = bmesh.new(); bm.from_mesh(po.data); bm.edges.ensure_lookup_table(); bm.verts.ensure_lookup_table()
adj = {}
for e in bm.edges:
    if len(e.link_faces) == 1:
        a, b = e.verts
        adj.setdefault(a.index, []).append(b.index); adj.setdefault(b.index, []).append(a.index)
seen, loops = set(), []
for st in adj:
    if st in seen: continue
    comp, stack = [], [st]; seen.add(st)
    while stack:
        v = stack.pop(); comp.append(v)
        for w in adj[v]:
            if w not in seen: seen.add(w); stack.append(w)
    loops.append(comp)
P = np.array([v.co[:] for v in po.data.vertices])
nl = max([l for l in loops if len(l) > 15], key=lambda l: P[l][:, 2].mean())
nc = Vector(P[nl].mean(0))
bm.free()
if O["scale"] != 1.0 or O["drop"]:
    po.data.transform(Matrix.Translation(nc + Vector((0, 0, -O["drop"]))) @ Matrix.Scale(O["scale"], 4) @ Matrix.Translation(-nc))
    po.data.update()
if O["subdiv"]:
    bm = bmesh.new(); bm.from_mesh(po.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=O["subdiv"], use_grid_fill=True)
    bm.to_mesh(po.data); bm.free(); po.data.update()
# pin group: within pin_r of the neck-hole ring (measured to the ring's points)
ring = [Vector(p) for p in P[nl]]
ring = [nc + (p - nc) * O["scale"] + Vector((0, 0, -O["drop"])) for p in ring]
pin = po.vertex_groups.new(name="pin")
npin = 0
r1 = O.get("pin_r1", O["pin_r"])
for v in po.data.vertices:
    d = min((v.co - r).length for r in ring)
    if d < O["pin_r"]:
        w = 1.0
    elif d < r1:
        t = (d - O["pin_r"]) / (r1 - O["pin_r"])
        w = O.get("pin_soft", 0.6) * (1 - t * t * (3 - 2 * t))
    else:
        w = 0.0
    if w > 0:
        pin.add([v.index], w, "REPLACE"); npin += 1

col = body.modifiers.new("Collision", "COLLISION")
body.collision.thickness_outer = O["clearance"]
body.collision.thickness_inner = 0.02
body.collision.cloth_friction = 5.0
cl = po.modifiers.new("Cloth", "CLOTH")
s = cl.settings
s.quality = O["quality"]
s.mass = O["mass"]
s.tension_stiffness = s.compression_stiffness = O["tension"]
s.shear_stiffness = O["tension"] / 3
s.bending_stiffness = O["bending"]
s.air_damping = 1.0
s.vertex_group_mass = "pin"
s.pin_stiffness = 1.0
if O.get("shrink"):
    s.shrink_min = O["shrink"]
cs = cl.collision_settings
cs.use_collision = True
cs.distance_min = O["clearance"]
cs.collision_quality = 4
cs.use_self_collision = bool(O["self"])
cs.self_distance_min = 0.003
cl.point_cache.frame_start = 1
cl.point_cache.frame_end = O["frames"]
scene.frame_start, scene.frame_end = 1, O["frames"]
for f in range(1, O["frames"] + 1):
    scene.frame_set(f)
dg = bpy.context.evaluated_depsgraph_get()
ev = po.evaluated_get(dg)
newme = bpy.data.meshes.new_from_object(ev)
po.modifiers.clear()
old = po.data
po.data = newme
newme.name = "poncho"
body.modifiers.remove(col)
Q = np.array([v.co[:] for v in newme.vertices])
print("DRAPE pinned", npin, "verts", len(Q), "bounds", Q.min(0).round(3), Q.max(0).round(3))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "draped.blend"))
print("DONE", OUT)
