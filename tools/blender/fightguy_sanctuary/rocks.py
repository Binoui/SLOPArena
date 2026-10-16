"""Rock pinnacles, distant Himalayan peaks and the cloud sea."""
from __future__ import annotations

import math

from mathutils import noise, Vector

from stagekit import geo
import palette as P


def _outline(x0, z0, x1, z1, radius, ts):
    """Points on a rounded rectangle at perimeter fractions ts (CCW from the south-east)."""
    r = max(0.05, min(radius, (x1 - x0) / 2 - 0.02, (z1 - z0) / 2 - 0.02))
    w, d = (x1 - x0) - 2 * r, (z1 - z0) - 2 * r
    arc = math.pi * r / 2
    segs = [("e", d), ("ne", arc), ("n", w), ("nw", arc), ("w", d), ("sw", arc), ("s", w), ("se", arc)]
    total = sum(length for _, length in segs)
    pts = []
    for t in ts:
        s = (t % 1.0) * total
        for kind, length in segs:
            if s <= length or kind == "se":
                u = 0 if length <= 0 else min(1.0, s / length)
                break
            s -= length
        if kind == "e":
            p, n = (x1, z0 + r + d * u), (1, 0)
        elif kind == "n":
            p, n = (x1 - r - w * u, z1), (0, 1)
        elif kind == "w":
            p, n = (x0, z1 - r - d * u), (-1, 0)
        elif kind == "s":
            p, n = (x0 + r + w * u, z0), (0, -1)
        else:
            centers = {"ne": (x1 - r, z1 - r, 0), "nw": (x0 + r, z1 - r, 90), "sw": (x0 + r, z0 + r, 180), "se": (x1 - r, z0 + r, 270)}
            cx, cz, start = centers[kind]
            a = math.radians(start + 90 * u)
            n = (math.cos(a), math.sin(a))
            p = (cx + r * n[0], cz + r * n[1])
        pts.append((p, n))
    return pts


def rock_color(rng, p, ny, snow_level, depth_t, warm=0.0, seed=0.0):
    """Coherent stylised rock colour: low-frequency mottling, strata tint, ledges."""
    n1 = noise.noise(Vector((p[0] * 0.09 + seed, p[1] * 0.16, p[2] * 0.09 - seed)))
    n2 = noise.noise(Vector((p[0] * 0.31 - seed, p[1] * 0.45, p[2] * 0.31 + seed)))
    band = 0.5 + 0.5 * math.sin(p[1] * 0.55 + n1 * 2.0)
    base = geo.mix(P.ROCK, P.ROCK_WARM, min(1.0, max(0.0, warm + 0.35 * band)))
    base = geo.mix(base, P.ROCK_DARK, min(1.0, max(0.0, 0.25 + 0.35 * n1 + 0.12 * n2)))
    base = geo.mix(base, P.ROCK_DEEP, min(1.0, max(0.0, depth_t)) * 0.5)
    if ny > 0.42:
        if snow_level is not None and p[1] > snow_level:
            return geo.rgba(geo.mix(P.SNOW_SHADE, P.SNOW, min(1.0, (ny - 0.42) * 2.4)), 0.15)
        return geo.mix(base, P.LICHEN, 0.35 + 0.25 * (0.5 + 0.5 * n2))
    if ny < -0.3:
        return geo.mix(base, P.ROCK_DEEP, 0.3)
    return geo.jitter(base, rng, 0.025)


def pillar(m, rng, footprint, y_top, kill_y, y_bottom=-52.0, inset=0.22, min_inset=0.06,
           taper=0.03, flare=0.28, snow_level=None, warm=0.0, seed=0.0, flute=0.55):
    """A natural rock column hanging beneath a playable slab.

    Above kill_y every vertex stays inside the slab footprint (inset >= min_inset),
    so a falling fighter never passes through rock beside the surface it fell from.
    Below kill_y (already a lost life) the column flares into the mountain massif.
    """
    x0, z0, x1, z1 = footprint
    half = min(x1 - x0, z1 - z0) / 2
    perim = 2 * ((x1 - x0) + (z1 - z0))
    n = max(12, int(perim / 1.55))
    ts = [i / n + rng.uniform(-0.15, 0.15) / n for i in range(n)]
    heights = [y_top, y_top - rng.uniform(0.35, 0.6)]
    y = heights[-1]
    while y > y_bottom:
        y -= rng.uniform(1.4, 2.8) if y > kill_y else rng.uniform(3.0, 5.0)
        heights.append(max(y, y_bottom))
    rings = []
    phase = rng.random() * 10
    for k, y in enumerate(heights):
        depth = y_top - y
        if y >= kill_y:
            d = inset + taper * depth
            # layered strata: alternating recessed bands and fuller shelves
            d += 0.55 * max(0.0, math.sin(depth * 0.42 + phase)) ** 2
            d = min(d, half - 0.4)
        else:
            d = inset + taper * (y_top - kill_y) - flare * (kill_y - y)
        d = d if k else inset * 0.6
        radius = min(half * 0.6, 0.5 + depth * 0.06)
        outline = _outline(x0 + d, z0 + d, x1 - d, z1 - d, radius, ts)
        ring = []
        for (px, pz), (nx, nz) in outline:
            v = noise.noise(Vector((px * 0.13 + seed, y * 0.08, pz * 0.13 - seed)))
            rib = noise.noise(Vector((px * 0.4 + seed * 2, y * 0.02, pz * 0.4)))
            push = (0.5 + 0.5 * v) * min(1.1, half * 0.35) + max(0.0, rib) * flute
            if k == 0:
                push *= 0.25
            px -= nx * push
            pz -= nz * push
            if y >= kill_y:
                px = min(max(px, x0 + min_inset), x1 - min_inset)
                pz = min(max(pz, z0 + min_inset), z1 - min_inset)
            ring.append(m.add((px, y, pz)))
        rings.append(ring)
    total = max(1.0, y_top - y_bottom)
    for k in range(len(rings) - 1):
        hi, lo = rings[k], rings[k + 1]
        for i in range(n):
            j = (i + 1) % n
            idx = [lo[i], hi[i], hi[j], lo[j]]
            pts = [m.verts[t] for t in idx]
            nrm = geo.normal(pts)
            c = tuple(sum(p[q] for p in pts) / 4 for q in range(3))
            m.face(idx, rock_color(rng, c, nrm[1], snow_level, (y_top - c[1]) / total * 1.5, warm, seed))
    m.face(rings[-1], P.ROCK_DEEP)  # bottom cap (CCW seen from above -> faces down)
    m.face(list(reversed(rings[0])), P.ROCK_DARK)


def massif(m, rng, corridor, extent=360.0, cell=6.0, base=-50.0, seed=1.3):
    """Rugged mountain floor deep below the course, mostly veiled by mist. It stays
    well below kill height inside the blast corridor and rises into ridges beyond.
    Colours are per vertex so snowfields blend smoothly (export this group smooth)."""
    k = int(extent / cell)
    pos = []
    for j in range(-k, k + 1):
        row = []
        for i in range(-k, k + 1):
            x, z = i * cell + rng.uniform(-1.5, 1.5), j * cell + rng.uniform(-1.5, 1.5)
            r = math.hypot(x, z)
            ridged = 1.0 - abs(noise.noise(Vector((x * 0.017 + seed, 0.3, z * 0.017 - seed))))
            h = base + 14.0 * ridged ** 2 + 4.0 * noise.noise(Vector((x * 0.06, seed, z * 0.06)))
            h += max(0.0, r - 110.0) * 0.32
            inside = corridor["minX"] - 4 < x < corridor["maxX"] + 4 and corridor["minZ"] - 4 < z < corridor["maxZ"] + 4
            if inside:
                h = min(h, corridor["killY"] - 14.0)
            row.append((x, h, z))
        pos.append(row)
    size = 2 * k + 1
    colors = []
    for j in range(size):
        row = []
        for i in range(size):
            x, h, z = pos[j][i]
            hx = pos[j][min(i + 1, size - 1)][1] - pos[j][max(i - 1, 0)][1]
            hz = pos[min(j + 1, size - 1)][i][1] - pos[max(j - 1, 0)][i][1]
            slope = math.hypot(hx, hz) / (2 * cell)
            snow_n = noise.noise(Vector((x * 0.03 - seed, 1.7, z * 0.03 + seed)))
            snow = max(0.0, min(1.0, (0.55 - slope) * 2.2 + snow_n * 0.9 + (h + 40) * 0.02))
            rock = rock_color(rng, (x, h, z), 0.0, None, 0.55, 0.15, seed)
            col = geo.mix(geo.mix(rock, P.ROCK_DEEP, 0.2), geo.mix(P.SNOW_SHADE, P.SNOW, 0.4), snow)
            row.append(geo.rgba(col, 1.0 - 0.85 * snow))
        colors.append(row)
    idx = [[m.add(pos[j][i]) for i in range(size)] for j in range(size)]
    for j in range(size - 1):
        for i in range(size - 1):
            quad = [(j, i), (j + 1, i), (j + 1, i + 1), (j, i + 1)]
            # (j, i) -> (j+1, i) is +z, -> (j+1, i+1) adds +x: normal = z x x = +y
            m.face([idx[a][b] for a, b in quad], [colors[a][b] for a, b in quad])


def peak(m, rng, center, height, radius, snow_line, ridges=5, rings=26, segments=56, base_y=-70.0, seed=0.0, lean=(0.0, 0.0)):
    """A sharp Himalayan peak: ridged polar heightfield, faceted, snow by slope."""
    cx, cz = center
    grid = []
    for r in range(rings + 1):
        t = r / rings
        row = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            ridge = sum(abs(math.cos(a * k * 0.5 + seed * (k + 1))) ** 3 * (0.6 / k) for k in range(1, ridges + 1))
            nn = noise.noise(Vector((math.cos(a) * 2.3 + seed, t * 3.1, math.sin(a) * 2.3 - seed)))
            rr = radius * t * (0.82 + 0.28 * ridge + 0.18 * nn)
            prof = (1 - t) ** 1.55
            h = base_y + (height - base_y) * prof * (0.92 + 0.08 * nn) + ridge * 6.0 * t * (1 - t)
            x = cx + rr * math.cos(a) + lean[0] * (1 - t) * height * 0.05
            z = cz + rr * math.sin(a) + lean[1] * (1 - t) * height * 0.05
            row.append(m.add((x, h, z)))
        grid.append(row)
    for r in range(rings):
        for s in range(segments):
            s2 = (s + 1) % segments
            # ring r is above ring r+1; CCW around the axis
            idx = [grid[r + 1][s], grid[r][s], grid[r][s2], grid[r + 1][s2]]
            pts = [m.verts[i] for i in idx]
            nrm = geo.normal(pts)
            if nrm[1] < 0:
                idx = list(reversed(idx))
                nrm = tuple(-v for v in nrm)
            yc = sum(p[1] for p in pts) / 4
            snowy = yc > snow_line - 14 * rng.random() and nrm[1] > 0.42
            if snowy:
                col = geo.rgba(geo.mix(P.SNOW_SHADE, P.SNOW, min(1, (nrm[1] - 0.42) * 2.5)), 0.12)
            else:
                col = geo.mix(P.ROCK, P.ROCK_DARK, 0.25 + 0.5 * rng.random())
                if yc < snow_line - 40:
                    col = geo.mix(col, P.ROCK_DEEP, 0.3)
            m.face(idx, col)


def spire(m, rng, center, top_y, radius, base_y=-60.0, seed=0.0, flat_top=0.0, snow_level=None, pines=None):
    """A free-standing background rock needle with ledges, snow shelves and pines."""
    cx, cz = center
    n = 16
    heights = []
    y = top_y
    while y > base_y:
        heights.append(y)
        y -= rng.uniform(1.8, 3.6)
    heights.append(base_y)
    rings = []
    phase = rng.random() * 6
    for k, y in enumerate(heights):
        depth = top_y - y
        rr = max(flat_top, radius * min(1.0, 0.45 + depth / 14.0)) + depth * 0.045
        rr *= 1.0 - 0.13 * max(0.0, math.sin(depth * 0.45 + phase)) ** 2
        ring = []
        for i in range(n):
            a = 2 * math.pi * i / n
            v = noise.noise(Vector((math.cos(a) * 1.4 + seed, y * 0.06, math.sin(a) * 1.4 - seed)))
            r = rr * (0.82 + 0.32 * (0.5 + 0.5 * v))
            ring.append(m.add((cx + r * math.cos(a), y, cz + r * math.sin(a))))
        rings.append(ring)
    total = top_y - base_y
    shelves = []
    for k in range(len(rings) - 1):
        hi, lo = rings[k], rings[k + 1]
        for i in range(n):
            j = (i + 1) % n
            idx = [lo[i], hi[i], hi[j], lo[j]]
            pts = [m.verts[t] for t in idx]
            nrm = geo.normal(pts)
            c = tuple(sum(p[q] for p in pts) / 4 for q in range(3))
            m.face(idx, rock_color(rng, c, nrm[1], snow_level, (top_y - c[1]) / total * 1.2, 0.2, seed))
            if nrm[1] > 0.3 and c[1] > base_y + 25:
                shelves.append((c, nrm))
    m.face(list(reversed(rings[0])), P.SNOW if snow_level is not None and top_y > snow_level else P.ROCK)
    if pines is not None:
        for c, nrm in shelves[:: max(1, len(shelves) // 9)]:
            pine(pines, rng, (c[0] + nrm[0] * 0.3, c[1] - 0.2, c[2] + nrm[2] * 0.3), rng.uniform(3.5, 6.5), rng.random() * 6.28)


def pine(m, rng, base, height, yaw):
    """Wind-shaped mountain pine: leaning trunk with three flattened canopy tiers."""
    lean = (math.cos(yaw) * height * 0.12, 0.0, math.sin(yaw) * height * 0.12)
    top = (base[0] + lean[0], base[1] + height, base[2] + lean[2])
    geo.cylinder_between(m, base, top, height * 0.035, 5, P.BARK, radius_b=height * 0.012)
    for t in range(3):
        f = 0.45 + t * 0.2
        c = (base[0] + lean[0] * f, base[1] + height * f, base[2] + lean[2] * f)
        r = height * (0.36 - t * 0.08)
        geo.ellipsoid(m, (c[0] + lean[0] * 0.3, c[1], c[2] + lean[2] * 0.3), (r, height * 0.1, r * 0.8), P.PINE, "SA_Foliage",
                      rings=3, segments=7, color_fn=lambda rr, s: geo.rgba(geo.mix(P.PINE_DARK, P.PINE, rr / 3), min(1.0, rr / 3 + 0.2)))


def cloud_sea(m, rng, extent=420, spacing=68, top=-31.0, cover=0.74):
    """Patchy mist pooling below kill height; gaps reveal the mountain floor."""
    k = int(extent / spacing)
    for ix in range(-k, k + 1):
        for iz in range(-k, k + 1):
            if rng.random() > cover:
                continue
            x = ix * spacing + rng.uniform(-22, 22)
            z = iz * spacing + rng.uniform(-22, 22)
            for part in range(rng.randint(2, 4)):
                rad = (rng.uniform(22, 46), rng.uniform(5, 9), rng.uniform(22, 46))
                cx, cz = x + rng.uniform(-30, 30), z + rng.uniform(-30, 30)
                y = top - rad[1] * rng.uniform(0.9, 1.6) - rng.uniform(0, 5)
                geo.ellipsoid(m, (cx, y, cz), rad, P.CLOUD, mat="SA_Cloud", rings=6, segments=12,
                              color_fn=lambda r, s: geo.rgba(geo.mix(P.CLOUD_SHADE, P.CLOUD, min(1.0, r / 4.5))))


def cloud_bank(m, rng, center, count, spread, size, y):
    for _ in range(count):
        x = center[0] + rng.uniform(-spread, spread)
        z = center[1] + rng.uniform(-spread * 0.4, spread * 0.4)
        r = (size * rng.uniform(0.7, 1.3), size * rng.uniform(0.18, 0.28), size * rng.uniform(0.5, 0.8))
        geo.ellipsoid(m, (x, y + rng.uniform(-3, 3), z), r, P.CLOUD, mat="SA_Cloud", rings=5, segments=10,
                      color_fn=lambda rr, s: geo.rgba(geo.mix(P.CLOUD_SHADE, P.CLOUD, min(1.0, rr / 4))))
