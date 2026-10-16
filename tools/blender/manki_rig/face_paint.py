"""Repaint Manki's face on the colour map through a front projection, and upscale the map.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_rig/face_paint.py -- \
    <export_dir>/Manki2.fbx <base_colour.png> <out.png> <face_profiles/NAME.json> [size=2048] [lid=skin|red] [debug_dir]

Input is export_unity_rig.py's FBX (armature in its T node pose) and the generated colour map. The generator
painted the face with dirt specks, a smeared mask and a winking lid the same red as the mask (it read as a
swollen eye); Manki 2 also had a stitched-looking mouth and smudged nostrils. This keeps the design (angry
brows, wink, red bandit mask, smirk) and repaints it cleanly:

1. Render the head from the front (orthographic, unlit) with a z-buffer and per-pixel triangle ids.
2. Classify the face image: brows come from the brow geometry (dark triangles inside the brow zones), so the
   painted fringe around them is cleaned and their full ridge is dark; skin and mask get smooth colour fields.
3. Draw the features in face space: mouth line, nostrils, the wink's lash line and lashes, a catch-light in
   the open eye.
4. Project back: texels visible from the front take the cleaned colours (fading out as the surface turns
   away); thin features also cover the groove walls. Edits are dilated into the UV gutters so seams and mips
   do not pick up the old paint. Everything outside the face is the bicubic upscale of the original map.

Feature positions are per model, in face-image pixels of the profile's camera: see face_profiles/*.json.
`keep_zones` leave the original paint (v3's clean snout), `skin_zones` force skin before features are drawn
(Manki 2's smudged snout). Only Blender's bundled numpy is needed; the face is about 2 mm per texel at 1024,
hence the 2048 output.
"""
import bpy, sys, os, json
import numpy as np

S = 1024  # face image size
INK = np.array([22, 10, 10]) / 255
MOUTH_INK = np.array([60, 28, 24]) / 255
NOSTRIL_INK = np.array([62, 30, 24]) / 255
PROFILE = {}


def ell(e):
    return (tuple(e[0]), tuple(e[1]))


# ----------------------------------------------------------------------------------------------- image helpers
def load_rgb(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)  # byte images: stored (sRGB-encoded) values
    return px.reshape(h, w, 4)[::-1, :, :3].astype(np.float64)


def save_rgb(path, rgb):
    h, w = rgb.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., :3] = np.clip(rgb[::-1], 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def upscale2x(img):
    """Separable Keys (a = -0.5) bicubic 2x upscale, edge-clamped."""
    def axis(x, ax):
        n = x.shape[ax]
        out_pos = (np.arange(2 * n) + 0.5) / 2 - 0.5
        i0 = np.floor(out_pos).astype(int)
        t = out_pos - i0
        def w(d):
            d = np.abs(d)
            return np.where(d <= 1, 1.5 * d ** 3 - 2.5 * d ** 2 + 1, np.where(d < 2, -0.5 * d ** 3 + 2.5 * d ** 2 - 4 * d + 2, 0))
        res = 0
        for k in (-1, 0, 1, 2):
            idx = np.clip(i0 + k, 0, n - 1)
            wk = w(t - k)
            shape = [1] * x.ndim
            shape[ax] = -1
            res = res + np.take(x, idx, axis=ax) * wk.reshape(shape)
        return res
    return np.clip(axis(axis(img, 0), 1), 0, 1)


def _box(x, r, ax):
    n = x.shape[ax]
    pad = [(0, 0)] * x.ndim
    pad[ax] = (r + 1, r)
    c = np.cumsum(np.pad(x, pad, mode="edge"), axis=ax)
    return (np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=ax) - np.take(c, np.arange(n), axis=ax)) / (2 * r + 1)


def blur(x, s):
    r = max(1, int(round(np.sqrt(4 * s * s + 1) / 2 - 0.5)))
    for ax in (0, 1):
        for _ in range(3):
            x = _box(x, r, ax)
    return x


def masked_blur(img, m, s):
    w = blur(m.astype(np.float64), s)
    return blur(img * m[..., None], s) / np.maximum(w, 1e-4)[..., None], w


def ellipse(c, r, feather=0.0):
    yy, xx = np.mgrid[:S, :S]
    d = np.sqrt(((xx - c[0]) / r[0]) ** 2 + ((yy - c[1]) / r[1]) ** 2)
    if feather <= 0:
        return d <= 1
    return np.clip((1 - d) * min(r) / feather, 0, 1)


def components(mask):
    """4-connected component labels of a boolean image (iterative flood fill)."""
    H, W = mask.shape
    lab = np.zeros((H, W), np.int32)
    n = 0
    for y0, x0 in zip(*np.nonzero(mask)):
        if lab[y0, x0]:
            continue
        n += 1
        lab[y0, x0] = n
        stack = [(y0, x0)]
        while stack:
            y, x = stack.pop()
            for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= yy < H and 0 <= xx < W and mask[yy, xx] and not lab[yy, xx]:
                    lab[yy, xx] = n
                    stack.append((yy, xx))
    return lab, n


def catmull_rom(pts, n=12):
    p = np.array(pts, np.float64)
    P = np.vstack([p[0], p, p[-1]])
    out = []
    for i in range(1, len(P) - 2):
        a, b, c, d = P[i - 1], P[i], P[i + 1], P[i + 2]
        for t in np.linspace(0, 1, n, endpoint=False):
            out.append(0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t ** 3))
    out.append(p[-1])
    return np.array(out)


def stroke(pts, w_mid, w_end):
    """Anti-aliased tapered stroke alpha along a Catmull-Rom curve."""
    curve = catmull_rom(pts)
    m = len(curve)
    alpha = np.zeros((S, S))
    for i in range(m - 1):
        a, b = curve[i], curve[i + 1]
        u = (i + 0.5) / (m - 1)
        r = (w_end + (w_mid - w_end) * np.sin(np.pi * u) ** 0.6) / 2
        x0, x1 = int(min(a[0], b[0]) - r - 2), int(max(a[0], b[0]) + r + 3)
        y0, y1 = int(min(a[1], b[1]) - r - 2), int(max(a[1], b[1]) + r + 3)
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, S), min(y1, S)
        yy, xx = np.mgrid[y0:y1, x0:x1]
        pxy = np.stack([xx + 0.5, yy + 0.5], -1)
        ab = b - a
        t = np.clip(((pxy - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
        d = np.linalg.norm(pxy - (a + t[..., None] * ab), axis=-1)
        alpha[y0:y1, x0:x1] = np.maximum(alpha[y0:y1, x0:x1], np.clip(r - d + 0.5, 0, 1))
    return alpha


# ----------------------------------------------------------------------------------------------- projection
class Cam:
    def __init__(self):
        cam = PROFILE["camera"]
        fwd = np.array(cam["fwd"], np.float64)
        self.centre = np.array(cam["centre"], np.float64)
        self.half_width = float(cam["half_width"])
        self.view = -fwd / np.linalg.norm(fwd)
        self.right = np.cross(self.view, [0, 0, 1.0]); self.right /= np.linalg.norm(self.right)
        self.up = np.cross(self.right, self.view)

    def project(self, X):
        d = X - self.centre
        hw = self.half_width
        return ((d @ self.right / hw * 0.5 + 0.5) * S, (0.5 - d @ self.up / hw * 0.5) * S, d @ self.view)


def raster(tri2d, W, H):
    (x0, y0), (x1, y1), (x2, y2) = tri2d
    xa, xb = max(int(np.floor(min(x0, x1, x2))), 0), min(int(np.ceil(max(x0, x1, x2))), W - 1)
    ya, yb = max(int(np.floor(min(y0, y1, y2))), 0), min(int(np.ceil(max(y0, y1, y2))), H - 1)
    if xb < xa or yb < ya:
        return None
    den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
    if abs(den) < 1e-12:
        return None
    ys, xs = np.mgrid[ya:yb + 1, xa:xb + 1]
    px, py = xs + 0.5, ys + 0.5
    a = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / den
    b = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / den
    c = 1 - a - b
    ins = (a >= -1e-4) & (b >= -1e-4) & (c >= -1e-4)
    if not ins.any():
        return None
    return ys[ins], xs[ins], np.stack([a[ins], b[ins], c[ins]], 1)


def sample(tex, uv):
    H, W = tex.shape[:2]
    x = uv[:, 0] * W - 0.5
    y = (1 - uv[:, 1]) * H - 0.5
    x0 = np.clip(np.floor(x).astype(int), 0, W - 1); y0 = np.clip(np.floor(y).astype(int), 0, H - 1)
    x1 = np.clip(x0 + 1, 0, W - 1); y1 = np.clip(y0 + 1, 0, H - 1)
    fx = np.clip(x - x0, 0, 1)[:, None]; fy = np.clip(y - y0, 0, 1)[:, None]
    return tex[y0, x0] * (1 - fx) * (1 - fy) + tex[y0, x1] * fx * (1 - fy) + tex[y1, x0] * (1 - fx) * fy + tex[y1, x1] * fx * fy


def render(cam, P, UV, tex, tris):
    depth = np.full((S, S), np.inf); tid = np.full((S, S), -1); uvimg = np.zeros((S, S, 2))
    for t in tris:
        x, y, z = cam.project(P[t])
        r = raster(np.stack([x, y], 1), S, S)
        if r is None:
            continue
        ys, xs, bc = r
        zz = bc @ z
        k = zz < depth[ys, xs]
        depth[ys[k], xs[k]] = zz[k]; tid[ys[k], xs[k]] = t; uvimg[ys[k], xs[k]] = bc[k] @ UV[t]
    rgb = np.zeros((S, S, 3))
    hit = tid >= 0
    rgb[hit] = sample(tex, uvimg[hit])
    return rgb, depth, tid


def texels(P, UV, N, tris, size):
    oy, ox, op, on = [], [], [], []
    for t in tris:
        r = raster(np.stack([UV[t][:, 0] * size, (1 - UV[t][:, 1]) * size], 1), size, size)
        if r is None:
            continue
        ys, xs, bc = r
        oy.append(ys); ox.append(xs); op.append(bc @ P[t]); on.append(bc @ N[t])
    n = np.concatenate(on)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    return np.concatenate(oy), np.concatenate(ox), np.concatenate(op), n


def project(tex, rgba, cam, depth, P, UV, N, tris, depth_eps, facing_lo, facing_hi):
    size = tex.shape[0]
    ty, tx, X, Nn = texels(P, UV, N, tris, size)
    x, y, z = cam.project(X)
    xi, yi = np.clip(x.astype(int), 0, S - 1), np.clip(y.astype(int), 0, S - 1)
    vis = (x >= 0) & (x < S) & (y >= 0) & (y < S) & (z <= depth[yi, xi] + depth_eps)
    fw = np.clip((-(Nn @ cam.view) - facing_lo) / (facing_hi - facing_lo), 0, 1)
    e = sample(rgba, np.stack([x / S, 1 - y / S], 1))
    a = e[:, 3] * fw * vis
    out = tex.copy()
    out[ty, tx] = out[ty, tx] * (1 - a[:, None]) + e[:, :3] * a[:, None]
    w = np.zeros(tex.shape[:2]); np.maximum.at(w, (ty, tx), a)
    return out, w


# ----------------------------------------------------------------------------------------------- painting
def paint(A, tid, tri_dark, cam, P, lid_style):
    pr = PROFILE
    FACE, NOFUR, SCLERA, LID = ell(pr["face"]), ell(pr["nofur"]), ell(pr["sclera"]), ell(pr["lid"])
    mx, mn = A.max(2), A.min(2)
    sat = (mx - mn) / np.maximum(mx, 1e-3)
    r, g, b = A[..., 0], A[..., 1], A[..., 2]
    face = ellipse(*FACE)
    bg = tid < 0
    # brows from geometry: dark triangles whose centroid lies in a brow zone
    cx, cy, _ = cam.project(P.mean(1))
    zone = np.zeros(len(P), bool)
    for c, rr in pr["brow_zones"]:
        zone |= ((cx - c[0]) / rr[0]) ** 2 + ((cy - c[1]) / rr[1]) ** 2 <= 1
    brow_tris = np.where(zone & (tri_dark >= 0.5))[0]
    if pr.get("brows", "geometry") == "colour":
        # keep the painted brows: large grey blobs (including their lighter tops) centred in a brow zone;
        # smaller blobs are dirt
        lab, n = components((mx < 0.55) & (sat < 0.6) & face)  # grey to brownish (v3's right brow), not mask red
        sizes = np.bincount(lab.ravel(), minlength=n + 1)
        ys, xs = np.nonzero(lab)
        sx = np.bincount(lab[ys, xs], xs, n + 1) / np.maximum(sizes, 1)
        sy = np.bincount(lab[ys, xs], ys, n + 1) / np.maximum(sizes, 1)
        keep_ids = [i for i in range(1, n + 1) if sizes[i] > 1500 and any(
            ((sx[i] - c[0]) / rr[0]) ** 2 + ((sy[i] - c[1]) / rr[1]) ** 2 <= 1 for c, rr in pr["brow_zones"])]
        brow = np.isin(lab, keep_ids)
        brow = blur(brow.astype(np.float64), 1.5) > 0.15  # their anti-aliased edges too
    else:
        # paint the brow ridges: the painted fringe around them is cleaned and the whole ridge goes dark
        brow = np.isin(tid, brow_tris)
        brow = blur(brow.astype(np.float64), 1.2) > 0.5  # close single-pixel gaps between brow triangles
    brow_keep = pr.get("brows", "geometry") == "colour"
    sclera_zone = ellipse(SCLERA[0], (SCLERA[1][0] + 4, SCLERA[1][1] + 4))
    white = mn > 0.78
    dark = mx < 0.36
    fur = (r > 0.66) & (g > 0.25) & (g < 0.6) & (b < 0.12 * r) & (r > 1.6 * g) & ~ellipse(*NOFUR)
    red = (r > 0.2) & (g < 0.3) & (b < 0.25) & (r > 2.0 * g)
    keep_zone = np.zeros((S, S), bool)
    for z in pr["keep_zones"]:
        keep_zone |= ellipse(*ell(z))
    skin_zone = np.zeros((S, S), bool)
    for z in pr["skin_zones"]:
        skin_zone |= ellipse(*ell(z))
    keep = brow | (sclera_zone & (white | dark)) | fur | bg | keep_zone
    editable = face & ~keep
    lid_m = ellipse(*LID)
    mask_r = ((blur((red & editable).astype(np.float64), 5) > 0.45) | (lid_m & editable)) & editable & ~skin_zone
    skin_r = editable & ~mask_r
    skin_ok = (skin_r & ~dark & (r > 0.6) & (g > 0.4) & ~skin_zone) | (skin_zone & (r > 0.7) & (g > 0.5))
    red_ok = mask_r & red & (r > 0.45) & ~lid_m
    skin_c, sw = masked_blur(A, skin_ok, 9)
    mask_c, mw = masked_blur(A, red_ok, 5)
    skin_w, _ = masked_blur(A, skin_ok, 30)
    mask_w, _ = masked_blur(A, red_ok, 24)
    skin_c = np.where((sw > 0.05)[..., None], skin_c, skin_w)
    mask_c = np.where((mw > 0.05)[..., None], mask_c, mask_w)
    yy, xx = np.mgrid[:S, :S]
    er = np.sqrt(((xx - SCLERA[0][0]) / SCLERA[1][0]) ** 2 + ((yy - SCLERA[0][1]) / SCLERA[1][1]) ** 2)
    mask_c = mask_c * (1 - 0.45 * np.clip(1 - (er - 1.0) / 0.32, 0, 1))[..., None]  # socket shading
    brow_col = np.median(A[brow & dark], 0) if (brow & dark).any() else np.array([0.19, 0.18, 0.18])
    C = A.copy()
    C[skin_r] = skin_c[skin_r]
    C[mask_r] = mask_c[mask_r]
    if not brow_keep:
        C[brow] = brow_col
    base_a = (editable | (brow & face & ~brow_keep)).astype(np.float64)
    base = np.dstack([C, base_a])

    # features (projected also onto groove walls)
    F = np.zeros((S, S, 3)); fa = np.zeros((S, S))

    def over(rgb, a):
        nonlocal F, fa
        F = F * (1 - a[..., None]) + rgb * a[..., None]
        fa = fa + a * (1 - fa)

    lid_soft = ellipse(LID[0], LID[1], 14) * (face & ~brow)
    rad = np.sqrt(((xx - LID[0][0]) / LID[1][0]) ** 2 + ((yy - LID[0][1]) / LID[1][1]) ** 2)
    shade = 1 - 0.18 * np.clip((rad - 0.6) / 0.4, 0, 1) ** 1.5
    shade += 0.10 * np.clip((LID[0][1] - yy) / LID[1][1], 0, 1) * np.clip(1 - rad, 0, 1)
    if lid_style == "skin":
        tone = np.median(A[skin_ok & ~skin_zone], 0)
    else:
        tone = np.median(A[red_ok], 0) * np.array([1.08, 1.35, 1.35])
    over(tone * shade[..., None], lid_soft)
    over(INK, stroke(pr["lash"], 13.0, 3.0))
    for p, q in pr["lashes"]:
        over(INK, stroke([p, q], 6.0, 2.0))
    if pr.get("mouth"):
        over(MOUTH_INK, stroke(pr["mouth"], 8.0, 2.5))
    for c in pr.get("nostrils", []):
        over(NOSTRIL_INK, ellipse(c, (13, 8), 3))
    over(np.ones(3), ellipse(tuple(pr["catchlight"]), (8, 8), 2))
    features = np.dstack([np.where(fa[..., None] > 0, F / np.maximum(fa, 1e-6)[..., None], 0), fa])
    preview = C * (1 - fa[..., None]) + features[..., :3] * fa[..., None]
    return base, features, preview, {"brow_triangles": int(len(brow_tris))}


# ----------------------------------------------------------------------------------------------- main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    fbx, base_png, out_png, profile = argv[:4]
    size = int(argv[4]) if len(argv) > 4 else 2048
    lid_style = argv[5] if len(argv) > 5 else "skin"
    debug = argv[6] if len(argv) > 6 else None
    PROFILE.update(json.load(open(profile)))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    if arm.animation_data:
        arm.animation_data.action = None
    body = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    bpy.context.view_layer.update()
    ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    me.calc_loop_triangles()
    nt, nl, nv = len(me.loop_triangles), len(me.loops), len(me.vertices)
    tl = np.empty(nt * 3, np.int64); me.loop_triangles.foreach_get("loops", tl); tl = tl.reshape(nt, 3)
    lv = np.empty(nl, np.int64); me.loops.foreach_get("vertex_index", lv)
    co = np.empty(nv * 3); me.vertices.foreach_get("co", co); co = co.reshape(nv, 3)
    uv = np.empty(nl * 2); me.uv_layers.active.data.foreach_get("uv", uv); uv = uv.reshape(nl, 2)
    cn = np.array([c.vector[:] for c in me.corner_normals])
    Wm = np.array(ev.matrix_world)
    co = co @ Wm[:3, :3].T + Wm[:3, 3]
    cn = cn @ Wm[:3, :3].T
    P, UV, N = co[lv[tl]], uv[tl], cn[tl]
    ev.to_mesh_clear()

    base_tex = load_rgb(base_png)
    cam = Cam()
    head = np.where(P.mean(1)[:, 2] > PROFILE.get("head_min_z", 0.93))[0]  # world height of the neck cut
    A, depth, tid = render(cam, P, UV, base_tex, head)
    # sanity check: the open eye must be where the features were placed
    sc = PROFILE["sclera"][0]
    sclera_px = A[sc[1] - 30:sc[1] + 30, sc[0] - 70:sc[0] - 20]
    if not (sclera_px.min(2) > 0.78).mean() > 0.5:
        raise SystemExit("face_paint: the open eye is not where the profile expects; wrong profile for this export?")
    bs = np.array([[1/3, 1/3, 1/3], [.6, .2, .2], [.2, .6, .2], [.2, .2, .6], [.45, .45, .1], [.45, .1, .45], [.1, .45, .45]])
    cols = sample(base_tex, np.einsum("sk,tkd->tsd", bs, UV).reshape(-1, 2)).reshape(len(P), len(bs), 3)
    cmx, cmn = cols.max(2), cols.min(2)
    tri_dark = ((cmx < 0.36) & ((cmx - cmn) / np.maximum(cmx, 1e-3) < 0.45)).mean(1)
    base, features, preview, info = paint(A, tid, tri_dark, cam, P, lid_style)

    tex = upscale2x(base_tex) if size == 2 * base_tex.shape[0] else base_tex
    if tex.shape[0] != size:
        raise SystemExit("face_paint: size must be the base size or twice it")
    tex, w1 = project(tex, base, cam, depth, P, UV, N, head, 0.004, 0.15, 0.45)
    tex, w2 = project(tex, features, cam, depth, P, UV, N, head, 0.03, -0.25, 0.0)
    w = np.maximum(w1, w2)
    # carry edits into the UV gutters (texels no triangle owns) so seams and mips keep the new colours
    inside = np.zeros(tex.shape[:2], bool)
    oy, ox, _, _ = texels(P, UV, N, range(len(P)), size)
    inside[oy, ox] = True
    done = w > 0.01
    for _ in range(6):
        acc = np.zeros_like(tex); cnt = np.zeros(done.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx or dy:
                    sd = np.roll(np.roll(done, dy, 0), dx, 1)
                    acc += np.roll(np.roll(tex, dy, 0), dx, 1) * sd[..., None]; cnt += sd
        grow = ~done & (cnt > 0) & ~inside
        tex[grow] = acc[grow] / cnt[grow][:, None]
        done |= grow
    save_rgb(out_png, tex)
    if debug:
        os.makedirs(debug, exist_ok=True)
        save_rgb(os.path.join(debug, "face_before.png"), A)
        save_rgb(os.path.join(debug, "face_after.png"), preview)
    info.update(out=out_png, profile=os.path.basename(profile), size=size, lid=lid_style, texels_edited=int((w > 0.01).sum()))
    print("REPORT", json.dumps(info))


main()
