"""Repaint a texture blemish on the (left-right symmetric) body by copying texels from the mirrored side.

blender -b --python-exit-code 1 <dressed.blend> --python tex_mirror_fix.py -- <texture.png> <out.png> '<json box list>'
Each box: {"lo": [x, y, z], "hi": [x, y, z]} in the dressed frame. Inside the box's faces, texels that are
orange-red or near-white (the blemish) are dilated by 3 px and replaced with the texel at the mirrored (x -> -x)
surface point.
"""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

argv = sys.argv[sys.argv.index("--") + 1:]
tex, out_png, boxes = os.path.abspath(argv[0]), os.path.abspath(argv[1]), json.loads(argv[2])
img = bpy.data.images.load(tex)
W, H = img.size
px = np.empty(W * H * 4, dtype=np.float32)
img.pixels.foreach_get(px)
px = px.reshape(H, W, 4)
body = bpy.data.objects["body"]
me = body.data
me.calc_loop_triangles()
uvl = me.uv_layers.active.data
V = [v.co.copy() for v in me.vertices]
bvh = BVHTree.FromPolygons(V, [tuple(t.vertices) for t in me.loop_triangles])
tris = list(me.loop_triangles)

def in_box(p, b):
    return all(b["lo"][i] <= p[i] <= b["hi"][i] for i in range(3))

def bad(c):
    r, g, b = c[:3]
    red = r > 0.25 and g / r < 0.68
    grey = max(r, g, b) > 0.38 and max(r, g, b) - min(r, g, b) < 0.18 * max(r, g, b) + 0.02
    return red or grey

total = 0
for box in boxes:
    sel = [t for t in tris if in_box(sum((V[i] for i in t.vertices), Vector()) / 3, box)]
    region = np.zeros((H, W), bool)
    owner = {}
    for t in sel:
        uv = [uvl[li].uv.copy() for li in t.loops]
        xs = [u.x * W for u in uv]; ys = [u.y * H for u in uv]
        x0, x1 = int(math.floor(min(xs))), int(math.ceil(max(xs)))
        y0, y1 = int(math.floor(min(ys))), int(math.ceil(max(ys)))
        a, b_, c = [Vector((xs[k], ys[k], 0)) for k in range(3)]
        P3 = [V[i] for i in t.vertices]
        for y in range(max(0, y0), min(H, y1 + 1)):
            for x in range(max(0, x0), min(W, x1 + 1)):
                q = Vector((x + 0.5, y + 0.5, 0))
                # barycentric via mathutils
                v0, v1, v2 = b_ - a, c - a, q - a
                d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
                d20, d21 = v2.dot(v0), v2.dot(v1)
                den = d00 * d11 - d01 * d01
                if abs(den) < 1e-12:
                    continue
                l1 = (d11 * d20 - d01 * d21) / den; l2 = (d00 * d21 - d01 * d20) / den; l0 = 1 - l1 - l2
                if min(l0, l1, l2) < -0.02:
                    continue
                region[y, x] = True
                owner[(x, y)] = l0 * P3[0] + l1 * P3[1] + l2 * P3[2]
    badm = np.zeros((H, W), bool)
    ys, xs = np.nonzero(region)
    for y, x in zip(ys, xs):
        if bad(px[y, x]):
            badm[y, x] = True
    n_bad = int(badm.sum())
    for _ in range(box.get("grow", 2)):
        grown = badm.copy()
        grown[1:, :] |= badm[:-1, :]; grown[:-1, :] |= badm[1:, :]; grown[:, 1:] |= badm[:, :-1]; grown[:, :-1] |= badm[:, 1:]
        badm = grown & region
    src = px.copy()
    done = 0
    left = []
    for y, x in zip(*np.nonzero(badm)):
        p = owner[(x, y)]
        if abs(p.x) < 0.006:
            left.append((y, x)); continue
        m = Vector((-p.x, p.y, p.z))
        loc, nrm, fi, d = bvh.find_nearest(m)
        t = tris[fi]
        P3 = [V[i] for i in t.vertices]
        uv = [Vector((uvl[li].uv.x, uvl[li].uv.y, 0)) for li in t.loops]
        u = barycentric_transform(loc, P3[0], P3[1], P3[2], uv[0], uv[1], uv[2])
        sx, sy = u.x * W - 0.5, u.y * H - 0.5
        ix, iy = int(math.floor(sx)), int(math.floor(sy)); fx, fy = sx - ix, sy - iy
        ix = min(max(ix, 0), W - 2); iy = min(max(iy, 0), H - 2)
        col = (src[iy, ix] * (1 - fx) * (1 - fy) + src[iy, ix + 1] * fx * (1 - fy)
               + src[iy + 1, ix] * (1 - fx) * fy + src[iy + 1, ix + 1] * fx * fy)
        if bad(col):
            left.append((y, x)); continue
        px[y, x, :3] = col[:3]
        done += 1
    # leftovers (centre line, or a bad mirror): fill from good neighbours inside the region, ring by ring
    todo = set(left)
    filled = 0
    while todo:
        ring = []
        for (y, x) in todo:
            acc, n = np.zeros(3, np.float32), 0
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
                q = (y + dy, x + dx)
                if q not in todo and 0 <= q[0] < H and 0 <= q[1] < W and region[q] and not (badm[q] and q in set(left)):
                    acc += px[q[0], q[1], :3]; n += 1
            if n:
                ring.append(((y, x), acc / n))
        if not ring:
            break
        for (q, c) in ring:
            px[q[0], q[1], :3] = c; todo.discard(q); filled += 1
        left = list(todo)
    # UV padding just outside the faces still holds the blemish and bleeds in through texture filtering:
    # give bad padding texels the colour of the nearest texel inside the faces.
    pad = region.copy()
    for _ in range(5):
        g = pad.copy()
        g[1:, :] |= pad[:-1, :]; g[:-1, :] |= pad[1:, :]; g[:, 1:] |= pad[:, :-1]; g[:, :-1] |= pad[:, 1:]
        pad = g
    pad &= ~region
    padded = 0
    for y, x in zip(*np.nonzero(pad)):
        if not bad(px[y, x]):
            continue
        best, bd = None, 99
        for dy in range(-5, 6):
            for dx in range(-5, 6):
                yy, xx = y + dy, x + dx
                if 0 <= yy < H and 0 <= xx < W and region[yy, xx] and dy * dy + dx * dx < bd:
                    best, bd = (yy, xx), dy * dy + dx * dx
        if best:
            px[y, x, :3] = px[best[0], best[1], :3]; padded += 1
    print(f"   mirror-copied {done}, neighbour-filled {filled}, unfilled {len(todo)}, padding {padded}")
    total += done + filled
    print(f"BOX {box}: faces {len(sel)} region texels {int(region.sum())} bad {n_bad} repainted {done}")
img.pixels.foreach_set(px.ravel())
img.filepath_raw = out_png
img.file_format = "PNG"
img.save()
print("WROTE", out_png, "repainted", total)
