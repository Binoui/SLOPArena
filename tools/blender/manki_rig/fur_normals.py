"""Body-like custom normals for Manki 2's fur tufts (used by fix_rig.py).

The remeshed fur is a field of small flat-shaded spikes. Under the arena key light, which is almost straight
down, every spike facet that faces down or sideways falls dark, so at gameplay distance the fur reads as dark
speckled tips (verified in Training: sharper mips and disabling shadow receiving changed nothing; these
normals removed it). Diffusing the vertex normals over the surface cancels each spike's opposite sides and
leaves the direction of the body underneath. Fur corners take that normal; skin, cloth, horns and the face
keep their own. Fur is found from the colour map (orange texels).
"""
import numpy as np


def _diffuse(values, edges, iterations):
    v = values.copy()
    a, b = edges[:, 0], edges[:, 1]
    deg = np.bincount(a, minlength=len(v)) + np.bincount(b, minlength=len(v)) + 1
    for _ in range(iterations):
        s = v.copy()
        np.add.at(s, a, v[b])
        np.add.at(s, b, v[a])
        v = s / deg[:, None]
    return v


def fur_weight(me, img_rgb):
    """Fraction of each vertex's corners whose colour-map texel is fur orange. img_rgb: (H, W, 3) sRGB, row 0 = top."""
    H, W = img_rgb.shape[:2]
    nl = len(me.loops)
    uv = np.empty(nl * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(nl, 2)
    vi = np.empty(nl, np.int64)
    me.loops.foreach_get("vertex_index", vi)
    c = img_rgb[np.clip(((1 - uv[:, 1]) * H).astype(int), 0, H - 1), np.clip((uv[:, 0] * W).astype(int), 0, W - 1)]
    mx, mn = c.max(1), c.min(1)
    sat = (mx - mn) / np.maximum(mx, 1e-3)
    r, g = c[:, 0], c[:, 1]
    orange = (mx >= 0.45) & (r > 0.5) & (sat > 0.5) & (g < 0.78 * r) & (g > 0.2 * r)
    nv = len(me.vertices)
    return np.bincount(vi, orange.astype(np.float64), nv) / np.maximum(np.bincount(vi, None, nv), 1)


def apply(me, img_rgb, iterations=40, weight_blur=3):
    """Blend each fur corner normal towards the diffused body normal (in place, as custom split normals)."""
    nv = len(me.vertices)
    co = np.empty(nv * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(nv, 3)
    tri = np.array([p.vertices[:] for p in me.polygons], np.int64)
    if tri.ndim != 2 or tri.shape[1] != 3:
        raise ValueError("fur_normals expects a triangulated mesh")
    fn = np.cross(co[tri[:, 1]] - co[tri[:, 0]], co[tri[:, 2]] - co[tri[:, 0]])  # area-weighted
    vn = np.zeros((nv, 3))
    for k in range(3):
        np.add.at(vn, tri[:, k], fn)
    ed = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ed)
    ed = ed.reshape(-1, 2)
    vn = _diffuse(vn, ed, iterations)
    vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-20)
    w = _diffuse(fur_weight(me, img_rgb)[:, None], ed, weight_blur)[:, 0]
    w = np.clip((w - 0.15) / 0.55, 0, 1)  # soft edge at fur/skin and fur/cloth borders
    cn = np.array([c.vector[:] for c in me.corner_normals])
    vi = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", vi)
    wl = w[vi][:, None]
    out = (1 - wl) * cn + wl * vn[vi]
    out /= np.maximum(np.linalg.norm(out, axis=1, keepdims=True), 1e-20)
    me.normals_split_custom_set([tuple(n) for n in out])
    return {"fur_vertices": int((w > 0.5).sum()), "vertices": nv, "iterations": iterations}
