"""Two-Faced Manor gray-box. Unity coordinates, +Y up and +Z toward the crypt."""
from __future__ import annotations
import json
import math
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from stagekit import arena as baked, shell

REPO = Path(__file__).resolve().parents[3]
STAGE = "nyx_manor"
SOURCE_SCENE = REPO / "client/Unity/Assets/Stages/nyx_manor/nyx_manor.unity"
ARENA = REPO / "data/arenas/nyx_manor.arena"
SIDE_BLAST_MARGIN, TOP_BLAST_MARGIN = 10., 20.
SPEC = {'water_y': -12.0,
 'spawn_body_half_height': 0.85,
 'start': (0.0, -43.0, 0.0),
 'start_piece': 'Lawn',
 'profile_id': 'nyx_manor_v1',
 'route': {'Lawn': (-24, -46, 24, -18, 0),
           'SideLawnW': (-24, -18, -16, -6, 0),
           'SideLawnE': (16, -18, 24, -6, 0),
           'S1': (-16, -18, 16, -10, 3.6),
           'S2': (-16, -10, 16, -2, 7.2),
           'S3': (-16, -2, 16, 6, 10.8),
           'Roof': (-16, 6, 16, 18, 14.4),
           'Turret': (-3, 9, 3, 15, 22),
           'ChimneyW': (-10, 11.2, -8.4, 12.8, 16.2),
           'ChimneyE': (8.4, 11.2, 10, 12.8, 16.2),
           'B1': (-20, 18, 20, 22, 10.8),
           'Aqueduct1': (-2.5, 22, 2.5, 28, 9),
           'Aqueduct2': (-2.5, 30.5, 2.5, 36, 9),
           'Aqueduct3': (-2.5, 38.5, 2.5, 44, 9),
           'SeaRock1': (6.4, 23.4, 9.6, 26.6, 4.5),
           'SeaRock2': (9.4, 28.4, 12.6, 31.6, 4.7),
           'SeaRock3': (5.4, 33.4, 8.6, 36.6, 4.9),
           'SeaRock4': (8.4, 38.3, 11.6, 41.5, 5),
           'GraveyardGateW': (-18, 46, 8, 49, 9),
           'GraveyardGateE': (12, 46, 18, 49, 9),
           'Graveyard': (-18, 49, 18, 68, 9),
           'GravestoneRow1': (-9, 52, 9, 52.8, 9.9),
           'GravestoneRow2': (-9, 57, 9, 57.8, 9.9),
           'MausoleumW': (-16, 60, -11, 65, 11.4),
           'MausoleumE': (11, 56, 16, 61, 11.4),
           'Crypt': (-3, 62, 3, 66, 12),
           'BoxW': (-5, -19.6, -3.4, -18, 1.8),
           'BoxE': (3.4, -19.6, 5, -18, 1.8),
           'EndBoxW': (-15.6, -11.6, -14, -10, 5.4),
           'EndBoxE': (14, -11.6, 15.6, -10, 5.4),
           'LedgeW': (-4, -3.5999999999999996, -2.4, -2, 9),
           'LedgeE': (2.4, -3.5999999999999996, 4, -2, 9),
           'DormerW': (-15, 4.4, -13, 6, 12.6),
           'DormerE': (13, 4.4, 15, 6, 12.6)},
 'stacked': {'Turret': 'Roof',
             'ChimneyW': 'Roof',
             'ChimneyE': 'Roof',
             'Crypt': 'Graveyard',
             'GravestoneRow1': 'Graveyard',
             'GravestoneRow2': 'Graveyard',
             'MausoleumW': 'Graveyard',
             'MausoleumE': 'Graveyard',
             'BoxW': 'Lawn',
             'BoxE': 'Lawn',
             'EndBoxW': 'S1',
             'EndBoxE': 'S1',
             'LedgeW': 'S2',
             'LedgeE': 'S2',
             'DormerW': 'S3',
             'DormerE': 'S3'},
 'ramps': {'LawnRampW': ('z', -15, -26, -11, -18, 0, 3.6, 'Lawn'),
           'LawnRampE': ('z', 11, -26, 15, -18, 0, 3.6, 'Lawn'),
           'Stair1W': ('x', -12, -14, -3, -10, 3.6, 7.2, 'S1'),
           'Stair1E': ('x', 3, -14, 12, -10, 7.2, 3.6, 'S1'),
           'Ramp2W': ('x', -15.6, -6, -7, -2, 7.2, 10.8, 'S2'),
           'Ramp2E': ('x', 7, -6, 15.6, -2, 10.8, 7.2, 'S2'),
           'Stair3W': ('x', -10, 2, -2, 6, 10.8, 14.4, 'S3'),
           'Stair3E': ('x', 2, 2, 10, 6, 14.4, 10.8, 'S3'),
           'CliffStairs': ('z', 8, 41.5, 12, 49, 5, 9, 'SeaRock4')},
 'targets': [('yard_1', 0, 1, -40, .6, 1),
             ('yard_2', 0, 1, -36.5, .6, 1),
             ('giant_bat', 0, 1.4, -32.5, 1, 5),
             ('yard_3', 0, 1, -28.5, .6, 1),
             ('flamingo', 0, 8.2, -8, 2, 5),
             ('cardboard_nyxxie', 0, 15.6, 15.8, 1, 5),
             ('roof_1', -.6, 15.4, 16.7, .6, 1),
             ('roof_2', .6, 15.4, 17.4, .6, 1),
             ('goth_balcony_1', -.6, 11.8, 19.6, .6, 1),
             ('goth_gargoyle', 0, 12, 20.4, 1, 5),
             ('goth_balcony_2', .6, 11.8, 21.2, .6, 1),
             ('goth_gate', 0, 10, 50, .6, 1),
             ('goth_grave_1', 0, 10, 51, .6, 1),
             ('goth_grave_2', 0, 10, 51.6, .6, 1),
             ('goth_grave_3', 0, 10, 58.3, .6, 1),
             ('goth_grave_4', 0, 10, 58.8, .6, 1),
             ('goth_grave_5', 0, 10, 59.3, .6, 1)],
 'checkpoints': [('checkpoint_1', (0, 2.5, -20), (16, 2.5, .5), (0, .85, -20), 0, 4),
                 ('checkpoint_2', (0, 12.3, 0), (16, 1.5, 1.5), (-12, 11.65, 0), 0, 5),
                 ('checkpoint_3', (0, 15.9, 16.5), (16, 1.5, 1), (0, 15.25, 16.5), 0, 5),
                 ('checkpoint_4', (0, 10, 47), (18, 3, 1.5), (-6, 9.85, 47), 0, 11)],
 'finish': ((0, 10.5, 61), (3, 1.5, 1))}


def _box(x0, y0, z0, x1, y1, z1):
    """Closed axis-aligned box: 12 outward triangles."""
    return _prism(x0, z0, x1, z1, y0, y0, y1, y1)


def _prism(x0, z0, x1, z1, bottom_south, bottom_north, top_south, top_north):
    """Closed solid over the footprint whose bottom and top slope along +Z."""
    v = {}
    for i in (0, 1):
        for k in (0, 1):
            x, z = (x1 if i else x0), (z1 if k else z0)
            v[(i, 0, k)] = (x, bottom_north if k else bottom_south, z)
            v[(i, 1, k)] = (x, top_north if k else top_south, z)
    quads = [  # each CCW seen from outside, so (b - a) x (c - a) points out
        [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)],  # top +Y
        [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)],  # bottom -Y
        [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)],  # east +X
        [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)],  # west -X
        [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)],  # north +Z
        [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)],  # south -Z
    ]
    tris = []
    for q in quads:
        a, b, c, d = (v[k] for k in q)
        tris += [(a, b, c), (a, c, d)]
    return tris


def _normal(t):
    a, b, c = t
    u = [b[i] - a[i] for i in range(3)]
    w = [c[i] - a[i] for i in range(3)]
    n = (u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0])
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def _check_outward(name, tris, inside):
    for t in tris:
        n = _normal(t)
        c = [sum(p[i] for p in t) / 3 for i in range(3)]
        if sum(n[i] * (c[i] - inside[i]) for i in range(3)) <= 0:
            raise AssertionError("%s triangle %s faces into the solid" % (name, t))


def _prism_x(x0,z0,x1,z1,bottom,west,east):
    # Exchanging X and Z reverses handedness, so reverse the triangle winding too.
    tris = _prism(z0,x0,z1,x1,bottom,bottom,west,east)
    return [tuple((z,y,x) for x,y,z in reversed(t)) for t in tris]


def pieces(spec=SPEC):
    return spec["route"]


def base(name,spec=SPEC):
    host = spec["stacked"].get(name)
    return spec["route"][host][4] if host else spec["water_y"]


def gravestone_row(x0,z0,x1,z1,y,bottom):
    tris=[]
    for i in range(9):
        x=x0+.45+i*2
        stone=_box(x,bottom,z0,x+1.1,y,z1)
        _check_outward('Gravestone',stone,(x+.55,(bottom+y)/2,(z0+z1)/2))
        tris.extend(stone)
    return tris


def sea_rock(x0,z0,x1,z1,y,bottom):
    cx,cz=(x0+x1)/2,(z0+z1)/2
    perimeter=[(x0,z0),(cx,z0),(x1,z0),(x1,cz),(x1,z1),(cx,z1),(x0,z1),(x0,cz)]
    rings=[]
    for height,scale in ((bottom,.43),(y-6,.67),(y,1)):
        rings.append([(cx+(x-cx)*scale,height,cz+(z-cz)*scale) for x,z in perimeter])
    tris=[]
    for k in range(8):
        j=(k+1)%8
        tris.append(((cx,y,cz),rings[2][j],rings[2][k]))
        tris.append(((cx,bottom,cz),rings[0][k],rings[0][j]))
        for ring in range(2):
            a,b,c,d=rings[ring][k],rings[ring+1][k],rings[ring+1][j],rings[ring][j]
            tris.extend(((a,b,c),(a,c,d)))
    return tris


def aqueduct(x0,z0,x1,z1,y,bottom):
    parts=[_box(x0,y-.9,z0,x1,y,z1),
           _box(x0,bottom,z0,x1,y-.9,z0+.75),
           _box(x0,bottom,z1-.75,x1,y-.9,z1)]
    start,end=z0+.75,z1-.75
    for i in range(8):
        a,b=i/8,(i+1)/8
        def underside(t):
            return y-6+3*math.sqrt(max(0,1-(2*t-1)**2))
        parts.append(_prism(x0,start+(end-start)*a,x1,start+(end-start)*b,
                            underside(a),underside(b),y-.9,y-.9))
    for tris in parts:
        vertices=[p for t in tris for p in t]
        inside=tuple(sum(p[i] for p in vertices)/len(vertices) for i in range(3))
        _check_outward('Aqueduct component',tris,inside)
    return [t for tris in parts for t in tris]


def collision(spec=SPEC):
    meshes = {}
    for name,(x0,z0,x1,z1,y) in pieces(spec).items():
        meshes[name] = (aqueduct(x0,z0,x1,z1,y,base(name,spec)) if name.startswith('Aqueduct')
                        else gravestone_row(x0,z0,x1,z1,y,base(name,spec)) if name.startswith('GravestoneRow')
                        else sea_rock(x0,z0,x1,z1,y,base(name,spec)) if name.startswith('SeaRock')
                        else _box(x0,base(name,spec),z0,x1,y,z1))
    for name,(axis,x0,z0,x1,z1,a,b,host) in spec["ramps"].items():
        bottom = spec['water_y'] if name=='CliffStairs' else spec["route"][host][4]
        meshes[name] = (_prism_x(x0,z0,x1,z1,bottom,a,b) if axis == "x"
                        else _prism(x0,z0,x1,z1,bottom,bottom,a,b))
        # Wedges have a zero-height edge; omit its degenerate triangles.
        meshes[name] = [t for t in meshes[name] if sum(v*v for v in _cross(t)) > 1e-12]
    for name,tris in meshes.items():
        if name.startswith(('Aqueduct','GravestoneRow')): continue
        vertices=[p for t in tris for p in t]
        inside=tuple(sum(p[i] for p in vertices)/len(vertices) for i in range(3))
        _check_outward(name,tris,inside)
    return meshes


def _cross(t):
    a,b,c=t
    u=[b[i]-a[i] for i in range(3)]; v=[c[i]-a[i] for i in range(3)]
    return (u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0])


def course(spec=SPEC):
    line=Path(__file__).with_name('racing_line.json')
    return shell.course(spec['profile_id'],[(t[0],t[1:4],t[4],t[5]) for t in spec['targets']],
                        spec['checkpoints'],spec['finish'],racing_line=json.loads(line.read_text()) if line.exists() else None)


def _footprints(spec=SPEC):
    out={n:(x0,z0,x1,z1,base(n,spec),y) for n,(x0,z0,x1,z1,y) in pieces(spec).items()}
    out.update({n:(x0,z0,x1,z1,spec['route'][host][4],max(a,b))
                for n,(axis,x0,z0,x1,z1,a,b,host) in spec['ramps'].items()})
    return out


def spec_corridor(spec=SPEC):
    spans=list(_footprints(spec).values())
    return {'minX':min(s[0] for s in spans)-10,'maxX':max(s[2] for s in spans)+10,
            'minZ':min(s[1] for s in spans)-10,'maxZ':max(s[3] for s in spans)+10,
            'killY':spec['water_y'],'topY':max(s[5] for s in spans)+20}


def target_reach_problems(spec=SPEC):
    """Every required objective must be in ordinary reach of each way, including height.

    The 0.8 m allowance is deliberately the roster's short melee reach, not a
    projectile/special range. NyxManorTests also fires the real seven kits from
    these closest points with no movement input. Points are feet, in Unity XYZ.
    """
    contract=json.loads(Path(__file__).with_name('target_ways.json').read_text())
    targets={t[0]:t for t in spec['targets']}
    seen=[]; problems=[]
    for section in contract['sections']:
        for name in section['targets']:
            seen.append(name)
            if name not in targets:
                problems.append(f'Unknown target in ways: {name}'); continue
            _,x,y,z,radius,_=targets[name]
            for way in section['ways']:
                best=math.inf
                for a,b in zip(way['points'],way['points'][1:]):
                    a=(a[0],a[1]+1,a[2]); b=(b[0],b[1]+1,b[2])
                    delta=[b[i]-a[i] for i in range(3)]; q=(x,y,z)
                    length=sum(d*d for d in delta)
                    t=max(0,min(1,sum((q[i]-a[i])*delta[i] for i in range(3))/length)) if length else 0
                    best=min(best,math.dist(q,[a[i]+delta[i]*t for i in range(3)]))
                if best>radius+contract['ordinaryReach']+1e-6:
                    problems.append(f"{name} is {best:.2f}m from {way['name']}, reach {radius+contract['ordinaryReach']:.2f}m")
    if sorted(seen)!=sorted(targets): problems.append('Ways must cover every required target exactly once')
    return problems


def check(spec=SPEC):
    problems=target_reach_problems(spec)
    spans=_footprints(spec)
    names=list(spans)
    for i,a in enumerate(names):
        for b in names[i+1:]:
            aa,bb=spans[a],spans[b]
            if all(aa[j] < bb[j+2]-1e-6 and bb[j] < aa[j+2]-1e-6 for j in (0,1)) and aa[4]<bb[5]-1e-6 and bb[4]<aa[5]-1e-6:
                problems.append(f'{a} overlaps {b}')
    for n,(axis,x0,z0,x1,z1,a,b,host) in spec['ramps'].items():
        run=x1-x0 if axis=='x' else z1-z0
        width=z1-z0 if axis=='x' else x1-x0
        if abs(a-b)/run > math.tan(math.radians(30))+1e-6 or width<4:
            problems.append(f'{n} exceeds the climb slope or width contract')
    c=course(spec)
    for cp in c['checkpoints']+[dict(c['finish'],id='finish',requiredTargets=len(c['targets']))]:
        for t in c['targets'][:cp['requiredTargets']]:
            if all(abs(t['position'][j]-cp['centre'][j]) < cp['halfSize'][j]+t['radius'] for j in range(3)):
                problems.append(f"{cp['id']} holds required target {t['id']}")
    return problems


def unity_shell_json(spec=SPEC):
    problems=check(spec)
    if problems: raise ValueError(problems)
    x,z,yaw=spec['start']
    meshes=[shell.mesh(n,'NyxManor'+n,*shell.indexed(t)) for n,t in collision(spec).items()]
    return shell.document(STAGE,spec['water_y'],meshes,[(x,.85,z,yaw)],course=course(spec))


def _canonical(t, digits=3):
    """Rotate a triangle to start at its smallest vertex (winding preserved) and round it."""
    pts = [tuple(round(v, digits) + 0.0 for v in p) for p in t]
    k = min(range(3), key=lambda i: pts[i])
    return tuple(pts[(k + i) % 3] for i in range(3))


def verify_arena(arena: dict, spec=SPEC) -> list:
    """Return mismatches between the baked arena and SPEC (empty when they agree)."""
    problems = []
    tol = 1e-3
    expected = sorted(_canonical(t) for tris in collision(spec).values() for t in tris)
    actual = sorted(_canonical((t[0:3], t[3:6], t[6:9])) for t in arena["triangles"])
    if len(expected) != len(actual):
        problems.append("baked arena has %d collision triangles, spec %d" % (len(actual), len(expected)))
    else:
        worst = max(max(abs(a - b) for pa, pb in zip(ta, tb) for a, b in zip(pa, pb))
                    for ta, tb in zip(expected, actual))
        if worst > 2e-3:
            problems.append("baked collision differs from spec by up to %.4f m" % worst)
    c = spec_corridor(spec)
    for key, value in (("killHeight", c["killY"]), ("killTop", c["topY"]), ("killMinX", c["minX"]),
                       ("killMaxX", c["maxX"]), ("killMinZ", c["minZ"]), ("killMaxZ", c["maxZ"])):
        if abs(arena[key] - value) > tol:
            problems.append("%s %.3f differs from spec %.3f" % (key, arena[key], value))
    sx, sz, syaw = spec["start"]
    top = spec["route"][spec["start_piece"]][4]
    if len(arena["spawns"]) != 1:
        problems.append("baked arena has %d spawns, a course has one" % len(arena["spawns"]))
    else:
        x, y, z, yaw = arena["spawns"][0]
        if abs(x - sx) > tol or abs(z - sz) > tol or abs(y - (top + spec["spawn_body_half_height"])) > tol:
            problems.append("Spawn_01 at (%.2f, %.2f, %.2f) differs from spec" % (x, y, z))
        if abs((math.degrees(yaw) - syaw + 180) % 360 - 180) > 0.05:
            problems.append("Spawn_01 yaw %.2f differs from spec %.2f" % (math.degrees(yaw), syaw))
    return problems


def load(spec_only=False,spec=SPEC):
    problems=check(spec)
    if problems: raise ValueError(problems)
    data={'spec':spec,'pieces':pieces(spec),'collision':collision(spec),'corridor':spec_corridor(spec),
          'waterY':spec['water_y'],'specOnly':spec_only}
    if not spec_only:
        problems=verify_arena(baked.read_arena(ARENA),spec)
        if problems: raise ValueError(problems)
        data.update(source=str(SOURCE_SCENE.relative_to(REPO)),sourceSha256=baked.sha256(SOURCE_SCENE),
                    arena=str(ARENA.relative_to(REPO)),arenaSha256=baked.sha256(ARENA))
    return data


if __name__=='__main__':
    json.dump(unity_shell_json() if '--unity-shell' in sys.argv else load('--spec-only' in sys.argv),sys.stdout,indent=1)
    sys.stdout.write('\n')
