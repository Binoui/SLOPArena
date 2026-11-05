"""Review the gunslinger-flip poncho built by flip_poncho.py: comparison stills, a sheet, and clearance in real clips.

B="systemd-run --user --scope -q -p MemoryMax=3G -p MemorySwapMax=1G blender -b --factory-startup --python-exit-code 1"
$B <out>/poncho_flip.blend --python tools/blender/salamander_rig/flip_poncho_review.py -- stills <out> [res=720]
python3 tools/blender/salamander_rig/flip_poncho_review.py sheet <out>
$B <out>/poncho_flip.blend --python tools/blender/salamander_rig/flip_poncho_review.py -- clips <out> <spec.json> [clips=a,b]

stills renders the saved pose (the idle frame) with the new poncho and with the live tabard, from the front, three-quarter
(front right, the board's frame), his right side, the back and the gameplay angle (behind, 28 degrees up), into
<out>/stills/<poncho>-<view>.png, plus upper-body close-ups of the new one (flip-close-<view>.png). sheet composes them
into <out>/sheet.jpg: new, live, then the new one's close-ups.
clips poses the rig with GenRA clip USDs ({"clips": [{"name", "usd", "render": [frames]}]}) and measures, for every
frame, poncho vertices inside his body or jeans (the poncho rides Spine2 rigidly, as flip_poncho.py skins it): how many,
how deep, and the bone under the deepest ones. Writes clearance-<clip>.json and, for the frames in "render" and the
worst frame, <out>/clips/<clip>-f<frame>-<view>.png (three-quarter and gameplay), where clipping shows as skin through
the fabric.
"""
import json, math, os, sys

VIEWS = {"front": (0, 4), "threequarter": (-40, 12), "side": (-90, 4), "back": (180, 4), "gameplay": (180, 28)}
LABELS = {"front": "front", "threequarter": "three-quarter", "side": "his right side", "back": "back", "gameplay": "gameplay"}

if __name__ == "__main__" and "bpy" not in sys.modules and sys.argv[1] == "sheet":
    from PIL import Image, ImageDraw, ImageFont
    out = sys.argv[2]
    rows = [("flip", "B gunslinger flip (new)"), ("tabard", "today's tabard"), ("flip-close", "B close-up")]
    tiles = [[Image.open(os.path.join(out, "stills", f"{p}-{v}.png")).convert("RGB") for v in VIEWS] for p, _ in rows]
    w, h = tiles[0][0].size
    pad, head = 12, 54
    font = ImageFont.load_default(size=36)
    sheet = Image.new("RGB", (pad + len(VIEWS) * (w + pad), len(rows) * (h + head + pad) + pad), (22, 23, 28))
    draw = ImageDraw.Draw(sheet)
    for r, (_, title) in enumerate(rows):
        y = pad + r * (h + head + pad)
        for c, v in enumerate(VIEWS):
            x = pad + c * (w + pad)
            draw.text((x + 6, y + 8), f"{title}: {LABELS[v]}" if c == 0 else LABELS[v], fill=(230, 230, 235), font=font)
            sheet.paste(tiles[r][c], (x, y + head))
    sheet.save(os.path.join(out, "sheet.jpg"), quality=90)
    print("sheet", os.path.join(out, "sheet.jpg"), sheet.size)
    sys.exit(0)

import bpy
import numpy as np
from mathutils import Vector, geometry
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clip_pose as cp

argv = sys.argv[sys.argv.index("--") + 1:]
MODE, OUT = argv[0], os.path.abspath(argv[1])
OPT = dict(a.split("=", 1) for a in argv[2:] if "=" in a)
scene = bpy.context.scene
fa = next(o for o in scene.objects if o.type == "ARMATURE")
new, tabard = bpy.data.objects["Salamander_Poncho"], bpy.data.objects["Salamander_Poncho_Tabard"]
body, jeans = bpy.data.objects["Salamander_Body"], bpy.data.objects["Salamander_Jeans"]


def setup(res_x, res_y):
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = res_x, res_y
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.world = bpy.data.worlds.new("review")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.13, 0.14, 0.18, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 1.6
    for name, rot, energy in [("key", (50, 0, -35), 3.2), ("fill", (60, 0, 150), 1.3), ("rim", (20, 0, 200), 1.0)]:
        light = bpy.data.lights.new(name, "SUN"); light.energy = energy; light.angle = math.radians(8)
        ob = bpy.data.objects.new(name, light); scene.collection.objects.link(ob)
        ob.rotation_euler = [math.radians(a) for a in rot]
    floor = bpy.data.meshes.new("floor")
    floor.from_pydata([(-3, -3, 0), (3, -3, 0), (3, 3, 0), (-3, 3, 0)], [], [(0, 1, 2, 3)])
    fm = bpy.data.materials.new("floor"); fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.2, 0.21, 0.25, 1)
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1
    floor.materials.append(fm)
    scene.collection.objects.link(bpy.data.objects.new("floor", floor))
    cd = bpy.data.cameras.new("cam"); cd.lens = 70
    cam = bpy.data.objects.new("cam", cd); scene.collection.objects.link(cam); scene.camera = cam
    return cam


def aim(cam, centre, yaw, pitch, dist):
    a, p = math.radians(yaw), math.radians(pitch)
    cam.location = centre + Vector((math.sin(a) * math.cos(p), -math.cos(a) * math.cos(p), math.sin(p))) * dist
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()


def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


if MODE == "stills":
    res = int(OPT.get("res", 720))
    cam = setup(res, int(res * 1.15))
    os.makedirs(os.path.join(OUT, "stills"), exist_ok=True)
    for tag, show, centre_z, dist in (("flip", new, 0.6, 2.5), ("tabard", tabard, 0.6, 2.5), ("flip-close", new, 0.66, 1.35)):
        for o in (new, tabard):
            o.hide_render = o is not show
        for view, (yaw, pitch) in VIEWS.items():
            aim(cam, Vector((0.01, 0.0, centre_z)), yaw, pitch, dist)
            render(os.path.join(OUT, "stills", f"{tag}-{view}.png"))

elif MODE == "clips":
    spec = json.load(open(argv[2]))["clips"]
    if "clips" in OPT:
        spec = [c for c in spec if c["name"] in OPT["clips"].split(",")]
    os.makedirs(os.path.join(OUT, "clips"), exist_ok=True)
    tabard.hide_render = True
    cam = setup(560, 640)
    names = {g.index: g.name for g in body.vertex_groups}
    jnames = {g.index: g.name for g in jeans.vertex_groups}
    dominant = [names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "-" for v in body.data.vertices]
    jdominant = [jnames[max(v.groups, key=lambda g: g.weight).group] + " (jeans)" if v.groups else "-" for v in jeans.data.vertices]
    # The body has holes where the jeans hide it; its normals are only trusted away from those rims.
    use = {}
    for poly in body.data.polygons:
        for k in poly.edge_keys:
            use[k] = use.get(k, 0) + 1
    rim = np.zeros(len(body.data.vertices), bool)
    rim[[v for k, c in use.items() if c == 1 for v in k]] = True
    rim_face = [rim[list(poly.vertices)].any() for poly in body.data.polygons]
    AXES = [("Hips", "Spine1"), ("LeftUpLeg", "LeftLeg"), ("RightUpLeg", "RightLeg")]

    def evaluated(o):
        ev = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        M = np.array(o.matrix_world)
        co = np.array([v.co for v in me.vertices]) @ M[:3, :3].T + M[:3, 3]
        polys = [tuple(p.vertices) for p in me.polygons]
        ev.to_mesh_clear()
        return co, polys

    def on_segment(p, a, z):
        return a.lerp(z, min(max(geometry.intersect_point_line(p, a, z)[1], 0.0), 1.0))

    def measure():
        """Depth of each poncho vertex inside his skin or his jeans (0 outside) and the bone under it.
        Jeans normals are inconsistent and their rims are open, so the jeans count as a shell round the hip and thigh
        bones: fabric is inside when it is nearer the bone axis than the jeans surface is."""
        b, bp = evaluated(body)
        j, jp = evaluated(jeans)
        bvh_b, bvh_j = BVHTree.FromPolygons(b.tolist(), bp), BVHTree.FromPolygons(j.tolist(), jp)
        W_ = fa.matrix_world
        segs = [(W_ @ fa.pose.bones[cp.P + a].head, W_ @ fa.pose.bones[cp.P + z].head) for a, z in AXES]
        pc, _ = evaluated(new)
        depth, under = np.zeros(len(pc)), [""] * len(pc)
        for v, c in enumerate(pc):
            p = Vector(c)
            loc, n, face, d = bvh_b.find_nearest(p)
            if not rim_face[face] and (p - loc).dot(n) < 0 and d < 0.08:
                depth[v], under[v] = d, dominant[bp[face][0]]
            q, _, jf, dj = bvh_j.find_nearest(p)
            if dj < 0.05:
                axis = min((on_segment(p, a, z) for a, z in segs), key=lambda x: (p - x).length)
                inner = (q - axis).length - (p - axis).length
                if inner > max(depth[v], 0.003):
                    depth[v], under[v] = inner, jdominant[jp[jf][0]]
        return depth, under

    def render_views(name, f):
        centre = fa.matrix_world @ fa.pose.bones[cp.P + "Spine2"].head
        for view in ("threequarter", "gameplay"):
            yaw, pitch = VIEWS[view]
            aim(cam, Vector((centre.x, centre.y, 0.6)), yaw, pitch, 2.5)
            render(os.path.join(OUT, "clips", f"{name}-f{f:03d}-{view}.png"))

    for c in spec:
        ua, extra = cp.load_clip(c["usd"])
        lo, hi = int(scene.frame_start), int(scene.frame_end)
        per = {}
        for f in range(lo, hi + 1):
            cp.apply(fa, cp.deltas(fa, ua, f))
            depth, under = measure()
            by_bone = {}
            for v in np.where(depth > 0)[0]:
                by_bone[under[v]] = max(by_bone.get(under[v], 0), round(float(depth[v]) * 100, 1))
            per[f] = {"inside": int((depth > 0).sum()), "max_cm": round(float(depth.max()) * 100, 1),
                      "by_bone_max_cm": dict(sorted(by_bone.items(), key=lambda kv: -kv[1])[:5])}
        worst = max(per, key=lambda f: (per[f]["max_cm"], per[f]["inside"]))
        for f in sorted(set(c.get("render", [])) | ({worst} if per[worst]["inside"] else set())):
            cp.apply(fa, cp.deltas(fa, ua, f))
            render_views(c["name"], f)
        result = {"name": c["name"], "usd": c["usd"], "range": [lo, hi], "worst_frame": worst, "worst": per[worst],
                  "frames_with_fabric_inside": sum(1 for f in per if per[f]["inside"]), "frames": len(per),
                  "per_frame": per}
        json.dump(result, open(os.path.join(OUT, f"clearance-{c['name']}.json"), "w"), indent=1)
        print("CLIP", c["name"], json.dumps({k: result[k] for k in ("worst_frame", "worst", "frames_with_fabric_inside", "frames")}))
        for o in [ua] + extra:
            bpy.data.objects.remove(o)
