"""Tibetan monastery architecture fitted to the authoritative sanctuary boxes."""
from __future__ import annotations

import math

import dressing as D
import flags
from stagekit import geo
import palette as P
import props


def fp_of(surface):
    return (surface["min"][0], surface["min"][2], surface["max"][0], surface["max"][2])


def ring_minus(outer, inner):
    """Four rectangles covering outer minus inner (both x0,z0,x1,z1)."""
    ox0, oz0, ox1, oz1 = outer
    ix0, iz0, ix1, iz1 = inner
    return [(ox0, oz0, ox1, iz0), (ox0, iz1, ox1, oz1), (ox0, iz0, ix0, iz1), (ix1, iz0, ox1, iz1)]


def eave(m, rng, fp, y0, y1):
    """Roof-slab edge: black fascia, two rows of painted rafter ends, red beam band."""
    for a, b, n, key in D.sides(fp):
        L = D.side_len(a, b)
        D.wall_quad(m, a, b, n, 0, 1, y0, y1, P.WOOD_DARK)
        D.wall_quad(m, a, b, n, 0, 1, y1 - 0.18, y1, P.BLACK, off=0.02)
        D.wall_quad(m, a, b, n, 0, 1, y1 - 0.205, y1 - 0.18, P.GOLD, off=0.022, mat="SA_Gloss")
        D.wall_quad(m, a, b, n, 0, 1, y0, y0 + 0.16, P.RED, off=0.02)
        D.wall_quad(m, a, b, n, 0, 1, y0 + 0.16, y0 + 0.2, P.OCHRE, off=0.022)
        rows = [(y0 + 0.36, 0.17, 0.14, [P.BLUE, P.GREEN]), (y1 - 0.38, 0.2, 0.2, [P.RED, P.OCHRE, P.GREEN, P.BLUE])]
        for yc, size, depth, cols in rows:
            count = max(2, int(L / (size * 2.1)))
            for i in range(count):
                s = (i + 0.5) * L / count
                D.wall_box(m, a, b, n, s, yc, size, size, depth, cols[i % len(cols)])
                D.wall_box(m, a, b, n, s, yc, size * 0.55, size * 0.55, depth + 0.012, P.WHITE if i % 2 else P.GOLD,
                           mat="SA_Stone" if i % 2 else "SA_Gloss")


def soffit(m, rng, outer, inner, y):
    """Underside of an overhanging roof: dark boards with painted rafters."""
    for r in ring_minus(outer, inner):
        x0, z0, x1, z1 = r
        if x1 - x0 < 0.05 or z1 - z0 < 0.05:
            continue
        m.poly([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], P.WOOD_DARK)
    ox0, oz0, ox1, oz1 = outer
    ix0, iz0, ix1, iz1 = inner
    k = 0
    x = ix0 + 0.3
    while x < ix1 - 0.2:
        for zz0, zz1 in ((oz0, iz0), (iz1, oz1)):
            geo.box(m, (x, y - 0.06, (zz0 + zz1) / 2), (0.12, 0.12, zz1 - zz0), P.BLUE if k % 2 else P.GREEN, skip=("top",))
        x += 0.5
        k += 1
    z = iz0 + 0.3
    while z < iz1 - 0.2:
        for xx0, xx1 in ((ox0, ix0), (ix1, ox1)):
            geo.box(m, ((xx0 + xx1) / 2, y - 0.06, z), (xx1 - xx0, 0.12, 0.12), P.BLUE if k % 2 else P.GREEN, skip=("top",))
        z += 0.5
        k += 1


def door(m, a, b, n, s, y0, width=2.1, height=2.9):
    """Grand red temple door with gold studs, stacked painted lintels and a valance."""
    L = D.side_len(a, b)
    def Q(ds, y, o, w=None):
        return D.wall_point(a, b, n, (s + ds) / L, y, o)
    hw = width / 2
    m.poly([Q(-hw - 0.75, y0, 0.012), Q(-hw - 0.45, y0 + height + 0.75, 0.012), Q(hw + 0.45, y0 + height + 0.75, 0.012), Q(hw + 0.75, y0, 0.012)], P.BLACK)
    for k, col in enumerate((P.RED_DARK, P.GREEN, P.OCHRE, P.BLUE)):
        e = 0.32 - k * 0.07
        m.poly([Q(-hw - e, y0, 0.02 + k * 0.006), Q(-hw - e, y0 + height + e, 0.02 + k * 0.006), Q(hw + e, y0 + height + e, 0.02 + k * 0.006), Q(hw + e, y0, 0.02 + k * 0.006)], col)
    for side in (-1, 1):
        x0, x1 = (-hw, -0.02) if side < 0 else (0.02, hw)
        m.poly([Q(x0, y0, 0.05), Q(x0, y0 + height, 0.05), Q(x1, y0 + height, 0.05), Q(x1, y0, 0.05)], P.RED)
        for r in range(6):
            for c in range(3):
                ds = x0 + (x1 - x0) * (c + 0.5) / 3
                D.wall_box(m, a, b, n, s + ds, y0 + 0.35 + r * (height - 0.6) / 5, 0.07, 0.07, 0.05, P.GOLD, off=0.05, mat="SA_Gloss")
        ring_c = D.wall_point(a, b, n, (s + side * 0.25) / L, y0 + height * 0.48, 0.1)
        D._disc(m, ring_c, n, 0.12, P.GOLD, "SA_Gloss")
    for k, (col, depth) in enumerate(((P.GREEN, 0.18), (P.RED, 0.28), (P.BLUE, 0.38))):
        yc = y0 + height + 0.82 + k * 0.17
        count = int((width + 1.4) / 0.24)
        for i in range(count):
            ds = -hw - 0.7 + (i + 0.5) * (width + 1.4) / count
            D.wall_box(m, a, b, n, s + ds, yc, 0.15, 0.13, depth, col if i % 2 else P.OCHRE)
    stripes = [P.WHITE, P.BLUE, P.WHITE, P.RED, P.WHITE, P.GREEN, P.WHITE]
    top = y0 + height + 0.72
    for k, col in enumerate(stripes):
        yy = top - 0.07 * (k + 1)
        m.poly([Q(-hw - 0.35, yy, 0.09), Q(-hw - 0.35, yy + 0.07, 0.09), Q(hw + 0.35, yy + 0.07, 0.09), Q(hw + 0.35, yy, 0.09)], col)


def temple(m, rng, S, flag_mesh):
    F, H, R, C = (fp_of(S[k]) for k in ("Sanctuary Foundation", "Sanctuary Hall", "Sanctuary Roof", "Sanctuary Crown"))
    yF0, yF1 = S["Sanctuary Foundation"]["min"][1], S["Sanctuary Foundation"]["top"]
    yH1, yR1, yC1 = S["Sanctuary Hall"]["top"], S["Sanctuary Roof"]["top"], S["Sanctuary Crown"]["top"]
    yR0 = S["Sanctuary Roof"]["min"][1]

    # Foundation: dark rubble podium with a whitewashed plinth course and paved kora ledge.
    D.masonry(m, rng, F, yF0, yF1, [P.RUBBLE, P.MASONRY, P.MASONRY_DARK], course=(0.42, 0.7), block=(0.6, 1.6),
              cap=P.WHITEWASH, cap_h=0.28, band=(yF1 - 0.62, yF1 - 0.28, P.MAROON))
    D.paving(m, rng, F, yF1, [P.PAVING, P.PAVING_LIGHT, P.MASONRY], border=0.3, border_color=P.MASONRY_DARK,
             skip_rect=(H[0] - 0.02, H[1] - 0.02, H[2] + 0.02, H[3] + 0.02))
    D.bottom(m, F, yF0)

    # Hall: whitewash, two window storeys, penbe frieze below the roof. The south
    # (courtyard) face carries the festival thangka; the grand door faces the summit.
    pb0, pb1 = yH1 - 1.05, yH1 - 0.12
    D.whitewash_wall(m, rng, H, yF1, pb0 - 0.14, inset=0.0, window_rows=[yF1 + 1.85, yF1 + 4.45], door_side="e",
                     skip_sides_windows={"s": 2.4})
    for a, b, n, key in D.sides(H):
        D.wall_quad(m, a, b, n, 0, 1, pb1, yH1, P.WOOD_DARK)
        D.wall_quad(m, a, b, n, 0, 1, pb0 - 0.14, pb0, P.WHITEWASH_SHADE)
    D.penbe(m, rng, H, pb0, pb1, protrude=0.035, medallion_every=1.7)
    a, b = (H[2], H[1]), (H[2], H[3])
    door(m, a, b, (1, 0), D.side_len(a, b) / 2, yF1)

    # Kora: prayer wheels under a little eave along the west and north hall walls.
    for a, b, n, key in D.sides(H):
        if key in ("s", "e"):
            continue
        L = D.side_len(a, b)
        count = int((L - 1.2) / 0.62)
        for i in range(count):
            s = 0.6 + (i + 0.5) * (L - 1.2) / count
            c = D.wall_point(a, b, n, s / L, yF1 + 0.32, 0.36)
            props.prayer_wheel(m, c, 0.2, 0.62)
            D.wall_box(m, a, b, n, s, yF1 + 0.16, 0.5, 0.32, 0.62, P.MASONRY_DARK)
        D.wall_box(m, a, b, n, L / 2, yF1 + 1.42, L - 1.0, 0.1, 0.75, P.RED_DARK)
        D.wall_box(m, a, b, n, L / 2, yF1 + 1.5, L - 1.0, 0.06, 0.8, P.OCHRE)
    for x in (F[0] + 0.85, F[2] - 0.85):
        for z in (F[1] + 0.85, F[3] - 0.85):
            props.chorten(m, (x, yF1, z), 0.42)

    # Roof slab: painted eave, soffit rafters under the overhang, earth roof and gilded crest.
    eave(m, rng, R, yR0, yR1)
    soffit(m, rng, R, H, yR0)
    D.earth_roof(m, rng, R, yR1, border=0.5)
    corners = [(R[0] + 0.42, R[1] + 0.42), (R[2] - 0.42, R[1] + 0.42), (R[2] - 0.42, R[3] - 0.42), (R[0] + 0.42, R[3] - 0.42)]
    for x, z in corners:
        props.gyaltsen(m, (x, yR1, z), 1.55, 0.22)
    props.dharma_wheel(m, ((R[0] + R[2]) / 2, yR1 + 0.2 + 0.55 + 0.1, R[1] + 0.45), radius=0.55)

    # Crown chapel: whitewash, penbe, gold cornice; corner banners and the ganjira.
    yC0 = yR1
    D.whitewash_wall(m, rng, C, yC0, yC1 - 0.92, inset=0.0, window_rows=[yC0 + 0.55])
    D.penbe(m, rng, C, yC1 - 0.86, yC1 - 0.2, protrude=0.03, medallion_every=1.4)
    for a, b, n, key in D.sides(C):
        D.wall_quad(m, a, b, n, 0, 1, yC1 - 0.92, yC1 - 0.86, P.BLACK, off=0.01)
        D.wall_quad(m, a, b, n, 0, 1, yC1 - 0.2, yC1, P.GOLD, off=0.03, mat="SA_Gloss")
    D.earth_roof(m, rng, C, yC1, border=0.35)
    ccorners = [(C[0] + 0.3, C[1] + 0.3), (C[2] - 0.3, C[1] + 0.3), (C[2] - 0.3, C[3] - 0.3), (C[0] + 0.3, C[3] - 0.3)]
    for x, z in ccorners:
        props.gyaltsen(m, (x, yC1, z), 1.1, 0.17)
    cx, cz = (C[0] + C[2]) / 2, (C[1] + C[3]) / 2
    props.ganjira(m, (cx, yC1, cz), 2.4)

    # Festival thangka draped over the courtyard facade, with rods and gilded finials.
    tw, top = 4.5, pb0 - 0.2
    th = top - (yF1 + 0.25)
    zf = H[1] - 0.04
    hx = (H[0] + H[2]) / 2
    flags.thangka(flag_mesh, (hx - tw / 2, top, zf), (hx + tw / 2, top, zf), th, sway=0.1)
    for y in (top + 0.06, top - th - 0.04):
        geo.cylinder_between(m, (hx - tw / 2 - 0.22, y, zf - 0.07), (hx + tw / 2 + 0.22, y, zf - 0.07), 0.065, 8, P.WOOD_DARK)
        for x in (hx - tw / 2 - 0.26, hx + tw / 2 + 0.26):
            geo.ellipsoid(m, (x, y, zf - 0.07), (0.1, 0.1, 0.1), P.GOLD, "SA_Gloss", rings=4, segments=8)
    # Foundation face toward the courtyard: a row of carved mani stones under the plinth.
    a, b = (F[0], F[1]), (F[2], F[1])
    for i in range(9):
        sx = 1.6 + i * (D.side_len(a, b) - 3.2) / 8
        D.wall_box(m, a, b, (0, -1), sx, yF1 - 2.2, 0.95, 0.7, 0.05, [P.WHITE, P.OCHRE, P.TURQUOISE][i % 3], off=0.012)

    # Flags radiate from the ganjira to the roof-corner banners.
    tip = (cx, yC1 + 2.1, cz)
    for k, (x, z) in enumerate(corners):
        flags.string(flag_mesh, rng, tip, (x, yR1 + 1.45, z), 0.5, spacing=0.36, fw=0.26, fh=0.24, color_offset=k)
    return {"ganjira_tip": tip, "roof_corners": [(x, yR1 + 1.45, z) for x, z in corners]}


def roof_building(m, rng, surface, wall_height, rich=False, lhatho_corners=(0, 2), flag_mesh=None, door_side=None):
    """A whitewashed monastery house whose flat earth roof is the playable slab."""
    fp = fp_of(surface)
    yt, yb = surface["top"], surface["min"][1]
    D.earth_roof(m, rng, fp, yt, border=0.32)
    D.penbe(m, rng, fp, yb + 0.16, yt - 0.1, protrude=0.025, medallions=rich, medallion_every=1.5)
    for a, b, n, key in D.sides(fp):
        D.wall_quad(m, a, b, n, 0, 1, yt - 0.1, yt, P.BLACK, off=0.025)
        D.wall_quad(m, a, b, n, 0, 1, yb, yb + 0.16, P.WOOD_DARK, off=0.0)
        L = D.side_len(a, b)
        count = max(2, int(L / 0.36))
        for i in range(count):
            D.wall_box(m, a, b, n, (i + 0.5) * L / count, yb + 0.08, 0.13, 0.12, 0.05, P.BLUE if i % 2 else P.GREEN)
    D.bottom(m, fp, yb, P.WOOD_DARK)
    base = yb - wall_height
    D.whitewash_wall(m, rng, fp, base + 0.55, yb, inset=0.14, door_side=door_side)
    inner = (fp[0] + 0.14, fp[1] + 0.14, fp[2] - 0.14, fp[3] - 0.14)
    D.masonry(m, rng, inner, base, base + 0.55, [P.MASONRY, P.MASONRY_DARK], course=(0.25, 0.3), block=(0.4, 0.8))
    if door_side:
        for a, b, n, key in D.sides(inner):
            if key == door_side:
                L = D.side_len(a, b)
                D.window(m, a, b, n, L / 2, base + 1.55, 0.95, 1.7)
    cs = [(fp[0] + 0.45, fp[1] + 0.45), (fp[2] - 0.45, fp[1] + 0.45), (fp[2] - 0.45, fp[3] - 0.45), (fp[0] + 0.45, fp[3] - 0.45)]
    for k in lhatho_corners:
        props.lhatho(m, (cs[k][0], yt, cs[k][1]), k * 1.3)
    return base


def gate(m, rng, finish, flag_mesh):
    """Tibetan gate over the finish: red pillars, painted lintel stack, penbe and a chorten."""
    fx, fz = finish[0], finish[2]
    y = 0.0
    for side in (-1, 1):
        x = fx + side * 3.4
        geo.box(m, (x, y + 0.22, fz), (0.9, 0.44, 0.9), P.MASONRY_DARK, skip=("bottom",), top_color=P.MASONRY)
        geo.box(m, (x, y + 2.3, fz), (0.42, 3.75, 0.42), P.RED)
        geo.box(m, (x, y + 4.25, fz), (0.62, 0.16, 0.62), P.OCHRE)
        geo.box(m, (x, y + 4.42, fz), (1.2, 0.18, 0.52), P.GREEN)
        geo.box(m, (x, y + 0.6, fz), (0.46, 0.12, 0.46), P.GOLD, "SA_Gloss")
        geo.box(m, (x, y + 4.0, fz), (0.46, 0.1, 0.46), P.GOLD, "SA_Gloss")
    geo.box(m, (fx, y + 4.68, fz), (8.0, 0.34, 0.55), P.RED)
    for i in range(30):
        x = fx - 3.9 + (i + 0.5) * 7.8 / 30
        geo.box(m, (x, y + 4.95, fz), (0.15, 0.15, 0.78), [P.BLUE, P.GREEN, P.OCHRE][i % 3])
    geo.box(m, (fx, y + 5.36, fz), (8.3, 0.62, 0.86), P.PENBE, top_color=P.EARTH_ROOF)
    for side in (-1, 1):
        for x in (-2.6, 0.0, 2.6):
            c = (fx + x, y + 5.36, fz + side * 0.44)
            D._disc(m, c, (0, side), 0.17, P.GOLD, "SA_Gloss")
    geo.box(m, (fx, y + 5.71, fz), (8.4, 0.08, 0.92), P.BLACK)
    for side in (-1, 1):
        props.gyaltsen(m, (fx + side * 3.7, y + 5.75, fz), 0.95, 0.15)
    top = props.chorten(m, (fx, y + 5.75, fz), 0.32)
    # valance under the lintel (between the pillars)
    stripes = [P.WHITE, P.BLUE, P.WHITE, P.RED, P.WHITE]
    for k, col in enumerate(stripes):
        yy = y + 4.5 - 0.07 * (k + 1)
        for side in (-1, 1):
            zz = fz + side * 0.29
            pts = [(fx - 3.15, yy, zz), (fx - 3.15, yy + 0.07, zz), (fx + 3.15, yy + 0.07, zz), (fx + 3.15, yy, zz)]
            geo.oriented(m, pts, (0, 0, side), col)
    return (fx, top, fz)
