"""Paint FightGuy's texture changes into the base color: 肉 on the back, the right wrap up to the elbow, and a
band-aid across the nose. Each change is rasterized in UV space from 3D projections of the T-pose mesh.
python3 tools/blender/fightguy_rig/paint_texture.py <build_dir> [name=FightGuy2] [font.ttc]
Reads <build_dir>/<name>_paint.npz, <name>_bones_world_unscaled.json and textures/<name>_BaseColor_src.png
(all written by build_rig.py); writes textures/<name>_BaseColor.png and <name>_paint_report.json."""
import sys, os, json, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

build = sys.argv[1]
name = sys.argv[2] if len(sys.argv) > 2 else "FightGuy2"
font_path = sys.argv[3] if len(sys.argv) > 3 else "/usr/share/fonts/noto-cjk/NotoSerifCJK-Black.ttc"
d = np.load(os.path.join(build, f"{name}_paint.npz"))
X, tris, tri_uv, W, names = d["X"], d["tris"], d["tri_uv"], d["W"], list(d["names"])
bones = json.load(open(os.path.join(build, f"{name}_bones_world_unscaled.json")))
src_png = os.path.join(build, "textures", f"{name}_BaseColor_src.png")
src = np.asarray(Image.open(src_png).convert("RGB")).astype(np.float32) / 255.0
tex = src.copy()
H, Wd = tex.shape[:2]
rng = np.random.default_rng(7)
report = {"texture": [Wd, H]}

TP = X[tris]                                   # (n,3,3) triangle corners, T-pose metres
TN = np.cross(TP[:, 1] - TP[:, 0], TP[:, 2] - TP[:, 0])
TN /= np.linalg.norm(TN, axis=1, keepdims=True) + 1e-12
TC = TP.mean(1)


def bone(n, end=0):
    return np.array(bones["mixamorig:" + n][end])


def dominant(group_names):
    cols = [names.index("mixamorig:" + g) for g in group_names]
    return W[tris][:, :, cols].sum(2).mean(1)  # mean weight of the given groups per triangle


def value_noise(h, w, cell, seed):
    g = np.random.default_rng(seed).random((h // cell + 2, w // cell + 2))
    y, x = np.mgrid[0:h, 0:w] / cell
    y0, x0 = y.astype(int), x.astype(int)
    fy, fx = y - y0, x - x0
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a, b = g[y0, x0], g[y0, x0 + 1]
    c, e = g[y0 + 1, x0], g[y0 + 1, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + e * fx) * fy


def rasterize(tri_ids, shade, dilate_px=1.0):
    """For every texel of the given triangles, shade(points_3d, texel_rgb, tri_id) -> (rgb, alpha)."""
    painted = 0
    for t in tri_ids:
        uv = tri_uv[t].astype(np.float64)
        px = np.stack([uv[:, 0] * Wd - 0.5, (1 - uv[:, 1]) * H - 0.5], 1)
        x0, y0 = np.floor(px.min(0) - dilate_px).astype(int)
        x1, y1 = np.ceil(px.max(0) + dilate_px).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, Wd - 1), min(y1, H - 1)
        if x1 < x0 or y1 < y0:
            continue
        M = np.array([px[1] - px[0], px[2] - px[0]]).T
        if abs(np.linalg.det(M)) < 1e-9:
            continue
        Minv = np.linalg.inv(M)
        gy, gx = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        q = np.stack([gx.ravel() - px[0, 0], gy.ravel() - px[0, 1]], 0)
        b12 = Minv @ q
        bary = np.vstack([1 - b12.sum(0), b12])
        # tolerance ~dilate_px texels in barycentric units
        tol = dilate_px / max(np.linalg.norm(M, axis=0).min(), 1e-6)
        inside = (bary >= -tol).all(0)
        if not inside.any():
            continue
        bary = np.clip(bary[:, inside], 0, 1)
        bary /= bary.sum(0, keepdims=True)
        pts = bary.T @ TP[t]
        rows, cols = gy.ravel()[inside], gx.ravel()[inside]
        rgb, a = shade(pts, src[rows, cols], t)
        if rgb is None:
            continue
        a = a[:, None]
        tex[rows, cols] = tex[rows, cols] * (1 - a) + rgb * a
        painted += int((a > 0.01).sum())
    return painted


def is_blue(c):
    return (c[:, 2] > c[:, 0] + 0.15) & (c[:, 2] > c[:, 1] + 0.08)


def is_skin(c):
    return (c[:, 0] > 0.55) & (c[:, 0] > c[:, 1] + 0.12) & (c[:, 1] > c[:, 2] + 0.05) & (c[:, 0] - c[:, 2] < 0.65)


def is_wrap_red(c):
    return (c[:, 0] > 0.45) & (c[:, 1] < 0.32) & (c[:, 2] < 0.32)


# ---------------- 1. 肉 on the back -------------------------------------------------------------
GLYPH = "肉"
gsz = 1024
font = ImageFont.truetype(font_path, int(gsz * 0.86), index=0)
gl = Image.new("L", (gsz, gsz), 0)
dr = ImageDraw.Draw(gl)
bb = dr.textbbox((0, 0), GLYPH, font=font)
dr.text(((gsz - (bb[2] - bb[0])) / 2 - bb[0], (gsz - (bb[3] - bb[1])) / 2 - bb[1]), GLYPH, fill=255, font=font)
gl = gl.rotate(-5, resample=Image.BICUBIC)               # authored, slightly crooked stamp
g = np.asarray(gl.filter(ImageFilter.GaussianBlur(5))).astype(np.float32) / 255.0
rough = value_noise(gsz, gsz, 9, 1) * 0.6 + value_noise(gsz, gsz, 3, 2) * 0.4
g = np.clip((g + (rough - 0.5) * 0.55 - 0.5) * 6 + 0.5, 0, 1)   # ragged brush edge
streak = 0.82 + 0.18 * value_noise(gsz, gsz // 1, 4, 3)[:, :]    # dry-brush density variation
glyph_alpha = g * streak

spine2, spine1, hips = bone("Spine2"), bone("Spine1"), bone("Hips")
back_tris = np.where((TN[:, 1] > 0.3) & (dominant(["Spine", "Spine1", "Spine2", "Hips", "Neck"]) > 0.6))[0]
# keep the glyph below the mullet: lowest dark (hair) texel on the back centre
hair_low = []
for t in back_tris:
    if abs(TC[t, 0]) < 0.05:
        uv = tri_uv[t].mean(0)
        c = src[int((1 - uv[1]) * H) % H, int(uv[0] * Wd) % Wd]
        if c.max() < 0.2:
            hair_low.append(TC[t, 2])
hair_bottom = min(hair_low) if hair_low else spine2[2] + 0.05
belt_top = None
for t in back_tris:
    if abs(TC[t, 0]) < 0.08:
        uv = tri_uv[t].mean(0)
        c = src[int((1 - uv[1]) * H) % H, int(uv[0] * Wd) % Wd]
        if c[0] > 0.6 and c[1] < 0.3 and c[2] < 0.3:
            belt_top = TC[t, 2] if belt_top is None else max(belt_top, TC[t, 2])
top = hair_bottom - 0.012
bottom = (belt_top if belt_top is not None else hips[2] + 0.05) + 0.012
size = min(top - bottom, 0.26)
centre = np.array([0.0, 0.0, (top + bottom) / 2])
report["back"] = {"hair_bottom_z": float(hair_bottom), "belt_top_z": None if belt_top is None else float(belt_top),
                  "glyph_size_m": float(size), "centre_z": float(centre[2])}
INK = np.array([0.96, 0.94, 0.89], np.float32)


def shade_back(pts, texel, t):
    u = (-(pts[:, 0] - centre[0])) / size + 0.5          # viewer's right = -X when seen from behind
    v = 1 - ((pts[:, 2] - centre[2]) / size + 0.5)
    ok = (u >= 0) & (u < 1) & (v >= 0) & (v < 1) & is_blue(texel)
    a = np.zeros(len(pts), np.float32)
    if ok.any():
        a[ok] = glyph_alpha[(v[ok] * (gsz - 1)).astype(int), (u[ok] * (gsz - 1)).astype(int)] * 0.97
    return np.broadcast_to(INK, (len(pts), 3)), a


report["back"]["texels"] = rasterize(back_tris, shade_back)

# ---------------- 2. Right wrap up to the elbow --------------------------------------------------
elbow, wrist = bone("RightForeArm"), bone("RightHand")
axis = wrist - elbow
alen = np.linalg.norm(axis)
axis /= alen
ref = np.array([0.0, 0.0, 1.0])
ref = ref - axis * (ref @ axis); ref /= np.linalg.norm(ref)
ref2 = np.cross(axis, ref)
fore = dominant(["RightForeArm", "RightArm", "RightHand"])
s_tri = ((TC - elbow) @ axis) / alen
r_tri = np.linalg.norm((TC - elbow) - np.outer((TC - elbow) @ axis, axis), axis=1)
arm_tris = np.where((fore > 0.6) & (s_tri > -0.1) & (s_tri < 1.0) & (r_tri < 0.08))[0]
# existing wrap: red texels per s bin -> its upper (elbow-side) edge
reds, wraps_rgb = [], []
for t in arm_tris:
    uv = tri_uv[t].mean(0)
    c = src[int((1 - uv[1]) * H) % H, int(uv[0] * Wd) % Wd]
    if is_wrap_red(c[None])[0]:
        reds.append(s_tri[t]); wraps_rgb.append(c)
wrap_edge = float(np.percentile(reds, 3)) if reds else 0.55
wrap_rgb = np.array(wraps_rgb)
base_red = np.median(wrap_rgb, 0) if len(wrap_rgb) else np.array([0.82, 0.12, 0.12])
line_red = np.percentile(wrap_rgb, 8, axis=0) if len(wrap_rgb) else base_red * 0.55
line_red = np.minimum(line_red, base_red * 0.62)
s_start = 0.06                                      # just below the elbow crease
report["wrap"] = {"existing_edge_s": wrap_edge, "start_s": s_start, "base_rgb": base_red.tolist(),
                  "line_rgb": line_red.tolist()}
PITCH = 0.016 * (alen / 0.135)                      # band spacing, scaled with forearm length


def shade_wrap(pts, texel, t):
    rel = pts - elbow
    s = (rel @ axis) / alen
    th = np.arctan2(rel @ ref2, rel @ ref)
    # wrap ends on a slight diagonal at the elbow side, overlapping the existing wrap a little
    end = s_start + 0.03 * np.sin(th)
    ok = (s > end) & (s < wrap_edge + 0.04) & is_skin(texel)
    a = np.where(ok, 1.0, 0.0).astype(np.float32)
    q = (s * alen + PITCH * 0.35 * th / (2 * math.pi)) / PITCH
    f = q - np.floor(q)
    line = np.clip(1 - np.abs(f - 0.5) / 0.09, 0, 1)    # thin darker seam between turns
    shadeband = 0.93 + 0.07 * np.cos(f * 2 * math.pi)    # each turn slightly rounded
    rgb = base_red[None] * shadeband[:, None] * (1 - line[:, None]) + line_red[None] * line[:, None]
    return rgb.astype(np.float32), a


report["wrap"]["texels"] = rasterize(arm_tris, shade_wrap)

# ---------------- 3. Band-aid across the nose ----------------------------------------------------
head, headtop = bone("Head"), bone("HeadTop_End")
hh = headtop[2] - head[2]
uvc = tri_uv.mean(1)
tri_rgb = src[((1 - uvc[:, 1]) * H).astype(int) % H, (uvc[:, 0] * Wd).astype(int) % Wd]
# nose tip = frontmost skin-coloured face triangle on the centre line (hair strands and the low
# headband are not skin, so they cannot be mistaken for it)
face = np.where((TN[:, 1] < -0.2) & (dominant(["Head"]) > 0.8) & (np.abs(TC[:, 0]) < 0.02) &
                is_skin(tri_rgb))[0]
tip = TC[face[np.argmin(TC[face, 1])]]
c_nose = np.array([0.0, tip[1], tip[2] + 0.03 * hh])     # on the nose just above the tip
BW, BH, ANG = 0.27 * hh, 0.068 * hh, math.radians(-10)
report["band_aid"] = {"nose_tip": tip.tolist(), "centre": c_nose.tolist(), "size_m": [BW, BH]}
CREAM = np.array([0.93, 0.80, 0.66], np.float32)
PAD = np.array([0.97, 0.93, 0.86], np.float32)
EDGE = np.array([0.45, 0.30, 0.22], np.float32)
nose_tris = np.where((TN[:, 1] < 0.1) & (dominant(["Head"]) > 0.8) &
                     (np.linalg.norm(TC - c_nose, axis=1) < BW))[0]


def shade_bandaid(pts, texel, t):
    x = pts[:, 0] - c_nose[0]
    z = pts[:, 2] - c_nose[2]
    u = (x * math.cos(ANG) + z * math.sin(ANG)) / (BW / 2)
    v = (-x * math.sin(ANG) + z * math.cos(ANG)) / (BH / 2)
    # rounded-rectangle distance (ends fully rounded)
    k = BH / BW
    du = np.maximum(np.abs(u) - (1 - k), 0) / k
    dist = np.sqrt(du ** 2 + v ** 2)
    inside = (dist < 1) & is_skin(texel)
    a = inside.astype(np.float32)
    rgb = np.broadcast_to(CREAM, (len(pts), 3)).copy()
    pad = (np.abs(u) < 0.28) & (np.abs(v) < 0.62)
    rgb[pad] = PAD
    # a few breathing holes on the strip ends so it reads as a plaster, not a highlight
    holes = (~pad) & (np.abs(u) < 0.8) & ((np.abs(u * 7) % 1 < 0.28) & (np.abs(v) < 0.22))
    rgb[holes] = rgb[holes] * 0.85
    rgb[dist > 0.74] = EDGE                       # crisp outline
    return rgb, a


report["band_aid"]["texels"] = rasterize(nose_tris, shade_bandaid, dilate_px=0.6)

out = os.path.join(build, "textures", f"{name}_BaseColor.png")
Image.fromarray(np.clip(tex * 255 + 0.5, 0, 255).astype(np.uint8)).save(out)
report["output"] = out
json.dump(report, open(os.path.join(build, f"{name}_paint_report.json"), "w"), indent=1, default=float)
print("PAINT", json.dumps(report, default=float))
