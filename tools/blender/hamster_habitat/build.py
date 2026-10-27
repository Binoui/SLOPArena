"""Build Hamster Habitat (night bedroom, concept C) with stagekit; requires the exact baked shell.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/hamster_habitat/build.py -- \
    --out /tmp/hamster-habitat-generated --report .stage-authoring-cache/hamster_habitat/art

The playable parts reuse layout.py's collision: deep blue bins filled with shavings, the
house, bowl, deck and TP roll. The tubes and the wheel are their collision walls in clear
StageGlass under opaque hoops, and the export carries the camera's see-through anchor.
room.py and hamster.py build the background; every background face is checked against the
blast corridor and the camera's reach. --dry runs the counts and checks outside Blender.
"""
from __future__ import annotations
import json
import math
import random
import struct
import sys
import time
import zlib
from pathlib import Path
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))
from stagekit import checks, geo
from stagekit.presentation import write as write_presentation
import layout as L
import palette as P
import presentation
import room
import hamster
import details
import clutter

TRIANGLE_BUDGET = 60_000
MAT, GLASS, SHAVINGS, GLOW, TOON, POSTERS = 'HH_Lit', 'HH_Glass', 'HH_Shavings', 'HH_Glow', 'HH_Toon', 'HH_Posters'
MATERIALS = [MAT, GLASS, SHAVINGS, GLOW, TOON, POSTERS]
# Table Manners' generated crayon atlas, reused so the kid's posters match across both stages.
TM_ATLAS = L.REPO / 'client/Unity/Assets/Stages/table_manners/Presentation/CraftRoom/TableMannersAtlas.png'
ANCHORS = {'Camera See-Through Thin Walls': (0.0, 0.0, 0.0)}  # CameraObstruction.SeeThroughMarker
DEFAULT_OUT = '/tmp/hamster-habitat-generated'
DEFAULT_REPORT = L.REPO / '.stage-authoring-cache/hamster_habitat/art'
NO_AO = {'Low Tube Glass', 'High Tube Glass', 'Wheel', 'Bedroom Glow', 'Hamster Glints', 'Bedroom', 'Bedroom Clutter', 'Bins', 'Hamster'}


class Build:
    def __init__(self, data):
        self.data = data
        self.groups = {}
        self.roles = {}

    def group(self, name, role, mat=MAT):
        if name not in self.groups:
            self.groups[name] = geo.Mesh(name, [mat])
            self.groups[name].alpha = 0
            self.roles[name] = role
        return self.groups[name]

    def verify(self):
        """Background faces stay out of the blast corridor and the camera's reach (actual faces)."""
        c = self.data['corridor']
        camera = {**c, 'minX': c['minX'] - checks.CAMERA_REACH, 'maxX': c['maxX'] + checks.CAMERA_REACH,
                  'minZ': c['minZ'] - checks.CAMERA_REACH, 'maxZ': c['maxZ'] + checks.CAMERA_REACH,
                  'killY': c['killY'] - checks.CEILING_CLEARANCE, 'topY': c['topY'] + 16}
        report, problems = {}, []
        for name, m in self.groups.items():
            entry = {'role': self.roles[name], 'triangles': m.triangle_count, 'bounds': m.bounds()}
            if self.roles[name] == 'background':
                for label, box in (('blast', c), ('camera', camera)):
                    bad = 0
                    for face in m.faces:
                        pts = [m.verts[i] for i in face]
                        if any(max(p[a] for p in pts) <= lo or min(p[a] for p in pts) >= hi
                               for a, lo, hi in ((0, box['minX'], box['maxX']), (1, box['killY'], box['topY']),
                                                 (2, box['minZ'], box['maxZ']))):
                            continue
                        if checks.intersects_corridor(pts, box):
                            bad += 1
                    entry[label + 'IntersectingFaces'] = bad
                    if bad:
                        problems.append('%s: %d faces intersect the %s envelope' % (name, bad, label))
            report[name] = entry
        return report, problems


def solid(m, tris, side, top=None, mat=MAT):
    """Collision triangles as art faces; upward faces take `top`."""
    for t in tris:
        n = L._norm(L._cross(L._sub(t[1], t[0]), L._sub(t[2], t[0])))
        m.poly(list(t), top if top is not None and n[1] > 0.7 else side, mat)


def band(m, a0, a1, b0, b1, color):
    m.poly([a0, a1, b1, b0], color, MAT)


def tube_frame(m, path, inner, wall, sides=L.TUBE_SIDES, window=None, hoop=P.TUBE, end_color=P.TUBE_END):
    """Opaque hoops and side rails over the glass walls."""
    ri, ro = L.rings(path, inner, sides), L.rings(path, inner + wall, sides)
    n = len(path)
    along, last = 0.0, -99.0
    for i in range(n):
        if i:
            along += math.dist(path[i], path[i - 1])
        end = i in (0, n - 1)
        if not end and along - last < 1.3:
            continue
        last = along
        t = L._norm(L._sub(path[min(i + 1, n - 1)], path[max(i - 1, 0)]))
        half = 0.16 if end else 0.07
        color = end_color if end else hoop
        for k in range(sides):
            k2 = (k + 1) % sides
            if window and window(L._centre([ro[i][k], ro[i][k2]]), path[i]):
                continue
            for a, b in ((ro[i][k], ro[i][k2]), (ri[i][k], ri[i][k2])):
                band(m, L._add(a, L._mul(t, -half)), L._add(b, L._mul(t, -half)),
                     L._add(a, L._mul(t, half)), L._add(b, L._mul(t, half)), color)
    for k in (sides // 4, sides // 4 * 3):
        for i in range(n - 1):
            a, b = ro[i][k], ro[i + 1][k]
            band(m, L._add(a, (0, -0.06, 0)), L._add(a, (0, 0.06, 0)),
                 L._add(b, (0, -0.06, 0)), L._add(b, (0, 0.06, 0)), end_color)


def hatch_rim(m):
    cx, cz = L.HATCH
    y = max(p[1] for p in L.low_tube_path() if abs(p[0] - cx) < 0.6) + L.TUBE_INNER + L.TUBE_WALL + 0.02
    pts = L.disc((cx, cz), L.HATCH_RADIUS + 0.15, 20)
    inner = L.disc((cx, cz), L.HATCH_RADIUS - 0.05, 20)
    for k in range(20):
        k2 = (k + 1) % 20
        m.poly([(pts[k][0], y, pts[k][1]), (inner[k][0], y, inner[k][1]),
                (inner[k2][0], y, inner[k2][1]), (pts[k2][0], y, pts[k2][1])], P.HATCH, MAT)


def bin_cage(b, name, footprint, top, support):
    """A deep storage bin filled to its rim with shavings: walls on the collision faces down to the furniture."""
    x0, x1, z0, z1 = footprint
    floor = b.group(name + ' Shavings', 'surface', SHAVINGS)
    nx, nz = 9, 9
    rng = random.Random(int(x0 * 10))
    for i in range(nx):
        for j in range(nz):
            a, c = x0 + (x1 - x0) * i / nx, x0 + (x1 - x0) * (i + 1) / nx
            d, e = z0 + (z1 - z0) * j / nz, z0 + (z1 - z0) * (j + 1) / nz
            pts = [(a, top, d), (a, top, e), (c, top, e), (c, top, d)]
            # Patchy bedding: a slight per-patch tint over the shavings texture.
            k = rng.uniform(0.88, 1.04)
            floor.poly(pts, (k, k * rng.uniform(0.97, 1.0), k * rng.uniform(0.92, 1.0)), SHAVINGS,
                       [(p[0] / 7.0, p[2] / 7.0) for p in pts])
    walls = b.group('Bins', 'wall')
    for (ax, az), (bx, bz) in (((x0, z0), (x1, z0)), ((x1, z0), (x1, z1)), ((x1, z1), (x0, z1)), ((x0, z1), (x0, z0))):
        # Dark at the furniture, plastic blue under a darker rim band.
        for y0, y1, c0, c1 in ((support, top - 3.0, P.BIN_DARK, P.BIN), (top - 3.0, top - 0.9, P.BIN, P.BIN),
                               (top - 0.9, top, P.BIN_RIM, P.BIN_RIM)):
            walls.poly([(ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az)], [c0, c0, c1, c1], MAT)
    details.bin_ribs(walls, footprint, top, support)


def house(b, tris):
    m = b.group('House', 'surface')
    solid(m, tris, P.HOUSE, P.HOUSE_ROOF)
    details.house(m)


def bowl(b, tris):
    m = b.group('Food Bowl', 'surface')
    solid(m, tris, P.BOWL, P.BOWL_INNER)
    details.bowl(m)


def generate(data):
    b = Build(data)
    c = L.collision()
    bin_cage(b, 'Desk Bin', L.SPEC['desk'], L.DESK, room.DESK_TOP)
    bin_cage(b, 'Shelf Bin', L.SPEC['shelf'], L.SHELF, room.SHELF_TOP)
    house(b, c['House'])
    bowl(b, c['FoodBowl'])
    m = b.group('Wheel Deck', 'surface')
    solid(m, c['WheelDeck'], P.DECK_EDGE, P.DECK)
    details.deck(m)
    solid(b.group('Wheel', 'surface', GLASS), c['Wheel'], P.WHEEL, mat=GLASS)
    solid(b.group('Wheel Post', 'surface'), c['WheelPost'], P.POST)
    details.tp_roll(b.group('TP Roll', 'surface'))
    solid(b.group('Junction Step', 'surface'), c['JunctionStep'], P.STEP)
    (wx, wy), (wz0, wz1) = L.SPEC['wheel']
    tube_frame(b.group('Wheel Rims', 'surface'), L.straight((wx, wy, wz0), (wx, wy, wz1)), L.WHEEL_INNER, 0.2,
               L.WHEEL_SIDES, hoop=P.WHEEL_RIM, end_color=P.WHEEL_RIM)
    details.wheel_rungs(b.groups['Wheel Rims'])
    labels = b.groups['Bins']
    details.tape_label(labels, 'PEANUT', -21.5, -3.2, L.SPEC['desk'][2], 10.5)
    details.tape_label(labels, 'GYM', 25.0, -6.0, L.SPEC['shelf'][2], 5.5)
    solid(b.group('Low Tube Glass', 'surface', GLASS), c['LowTube'], P.TUBE, mat=GLASS)
    solid(b.group('High Tube Glass', 'surface', GLASS), c['HighTube'], P.TUBE, mat=GLASS)
    m = b.group('Low Tube Frame', 'surface')
    tube_frame(m, L.low_tube_path(), L.TUBE_INNER, L.TUBE_WALL, window=L.low_tube_window)
    hatch_rim(m)
    tube_frame(b.group('High Tube Frame', 'surface'), L.high_tube_path(), L.TUBE_INNER, L.TUBE_WALL)
    room.build(b)
    clutter.build(b)
    hamster.build(b)
    return b


def write_png(path, width, height, pixel):
    rows = bytearray()
    for y in range(height):
        rows.append(0)
        for x in range(width):
            rows.extend(pixel(x, y))
    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xFFFFFFFF)
    Path(path).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(bytes(rows), 9)) + chunk(b'IEND', b''))


def shavings_png(path, size=256):
    """Tileable wood-shaving chips: overlapping light curls on a warm base, wrapped at the edges."""
    rng = random.Random(9)
    base = [[[c * 0.82 for c in P.SHAVINGS] for _ in range(size)] for _ in range(size)]
    for _ in range(900):
        cx, cy = rng.uniform(0, size), rng.uniform(0, size)
        length, width, angle = rng.uniform(8, 20), rng.uniform(2.5, 5), rng.uniform(0, math.pi)
        shade = rng.uniform(0.85, 1.18)
        ca, sa = math.cos(angle), math.sin(angle)
        for u in range(int(-length), int(length) + 1):
            for v in range(int(-width), int(width) + 1):
                if (u / length) ** 2 + (v / width) ** 2 > 1:
                    continue
                x, y = int(cx + u * ca - v * sa) % size, int(cy + u * sa + v * ca) % size
                edge = 0.9 if abs(v) > width * 0.6 else 1.0
                base[y][x] = [min(1.0, c * shade * edge) for c in P.SHAVINGS]
    write_png(path, size, size, lambda x, y: bytes(int(255 * c) for c in base[y][x]))


def white_png(path, size=8):
    write_png(path, size, size, lambda x, y: b'\xff\xff\xff')


def main():
    dry = '--dry' in sys.argv
    if dry:
        sys.argv.remove('--dry')
    a = checks.build_args(DEFAULT_OUT, DEFAULT_REPORT)
    data = L.load(spec_only=a.spec_only or dry)
    b = generate(data)
    report, problems = b.verify()
    triangles = sum(m.triangle_count for m in b.groups.values())
    summary = {'triangles': triangles, 'budget': checks.budget(triangles, TRIANGLE_BUDGET),
               'groups': {n: m.triangle_count for n, m in b.groups.items()}, 'corridor': data['corridor'],
               'source': data['source'], 'sourceSha256': data['sourceSha256'], 'arenaSha256': data['arenaSha256'],
               'checks': report, 'problems': problems}
    if dry:
        print(json.dumps({k: summary[k] for k in ('triangles', 'budget', 'groups', 'problems')}, indent=1))
        if problems:
            raise SystemExit(1)
        return
    if problems:
        raise SystemExit('Clearance problems: ' + json.dumps(problems))
    if summary['budget']['over']:
        raise SystemExit('Planned triangle budget exceeded')
    Path(a.out).mkdir(parents=True, exist_ok=True)
    Path(a.report).mkdir(parents=True, exist_ok=True)
    import bpy
    from stagekit import blender as kit
    t = time.time()
    white_png(str(Path(a.out) / 'HamsterHabitatWhite.png'))
    shavings_png(str(Path(a.out) / 'HamsterHabitatShavings.png'))
    (Path(a.out) / 'HamsterHabitatPosters.png').write_bytes(TM_ATLAS.read_bytes())
    kit.reset_scene(MATERIALS)
    objects = kit.build_objects(b.groups)
    if not a.no_bake:
        summary['aoBakeSeconds'] = kit.bake_ao(objects, a.samples, ao_distance=3.0, floor=.5, gamma=1.0, skip=NO_AO)
    digest, object_digests = kit.digest(objects)
    summary.update(geometryDigest=digest, objectDigests=object_digests, meshHealth=kit.mesh_health(objects))
    if not a.no_export:
        kit.export_fbx(objects, str(Path(a.out) / 'HamsterHabitatArena.fbx'), anchors=ANCHORS)
        stamp = {'generator': 'tools/blender/hamster_habitat/build.py', 'source': data['source'],
                 'sourceSha256': data['sourceSha256'], 'arena': data['arena'], 'arenaSha256': data['arenaSha256'],
                 'blender': bpy.app.version_string, 'materials': MATERIALS, 'anchors': ANCHORS,
                 'shadowed': list(presentation.SHADOWED), 'objects': sorted(objects), 'geometryDigest': digest,
                 'problems': 0}
        (Path(a.out) / 'HamsterHabitatArena.layout.json').write_text(json.dumps(stamp, indent=1) + '\n')
        write_presentation(presentation.PRESENTATION, a.out)
    summary['buildSeconds'] = round(time.time() - t, 1)
    (Path(a.report) / 'build-report.json').write_text(json.dumps(summary, indent=1) + '\n')
    print('HAMSTER_HABITAT_BUILD', json.dumps({k: summary[k] for k in ('triangles', 'geometryDigest', 'buildSeconds', 'meshHealth', 'problems')}))


if __name__ == '__main__':
    main()
