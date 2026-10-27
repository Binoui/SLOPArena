"""Table Manners' bedroom at night, seen from the art-desk corner (Unity coordinates).

The room uses Table Manners' own scale and plan (tools/blender/table_manners/room.py: about 32
game units per real metre, walls at x +-66 and z +-72). Its floor sits TM_OFFSET below Table
Manners' floor so that its art-desk top lands under the PEANUT bin, below the KO line. Tall
furniture keeps Table Manners' places except where the camera's reach around the bins forbids
it (the bunk bed turns along the north wall, the west bookshelf is shorter, the craft table
moves south because the bins own the centre). build.py checks every face.
"""
from __future__ import annotations
import math
import random
from stagekit import geo
import palette as P

LIT, GLOW = 'HH_Lit', 'HH_Glow'
TM_OFFSET = -18.5                      # this room's y = Table Manners' y + TM_OFFSET
FLOOR_Y = -16.0 + TM_OFFSET            # -34.5
DESK_TOP, SHELF_TOP = 2.0 + TM_OFFSET, -19.5
# Table Manners' ceiling would sit at 40.5; it is raised so the camera never meets it when a
# fighter is launched to the top blast line (31.6) and the camera rides above them.
CEILING_Y = 50.0
ROOM = (-66.0, 66.0, -72.0, 72.0)      # x0, x1, z0, z1: Table Manners' walls
WAINSCOT_TOP = 11.0 + TM_OFFSET
LAMP_HEAD = (-40.0, 7.0, 49.0)
WINDOW = (-24.0, 24.0, -1.0 + TM_OFFSET, 41.0 + TM_OFFSET)   # Table Manners' window opening
DOOR = (28.0, 52.0, 46.0 + TM_OFFSET)                       # south wall: x0, x1, top


def _box(m, lo, hi, color, **kw):
    geo.box_minmax(m, lo, hi, color, LIT, **kw)


def _glow_star(m, centre, size, normal_axis, color=P.GLOW_STAR):
    """Flat five-point star facing along +/- normal_axis ('y' ceiling, 'z' north/south, 'x' east/west)."""
    cx, cy, cz = centre
    pts = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5
        r = size if k % 2 == 0 else size * 0.45
        u, v = r * math.cos(a), r * math.sin(a)
        pts.append({'y': (cx + u, cy, cz + v), 'z': (cx + u, cy + v, cz), 'x': (cx, cy + v, cz + u)}[normal_axis])
    with geo.state(m, alpha=1.0):
        for k in range(10):
            m.poly([centre, pts[k], pts[(k + 1) % 10]], color, GLOW)


def desk(m):
    """Table Manners' white desk against the west wall, long enough to hold the PEANUT bin."""
    x0, x1, z0, z1 = -65.5, -10.4, -14.0, 72.0
    _box(m, (x0, DESK_TOP - 1.2, z0), (x1, DESK_TOP, z1), P.DESK)
    for x in (x0 + 1.0, x1 - 2.6):
        for z in (z0 + 0.4, z1 - 2.0):
            _box(m, (x, FLOOR_Y, z), (x + 1.6, DESK_TOP - 1.2, z + 1.6), P.DESK_SHADE)
    # Drawer block at the south-west corner with Table Manners' coloured fronts facing the room.
    _box(m, (x0, FLOOR_Y, z0 + 0.4), (x0 + 12.0, DESK_TOP - 1.2, z0 + 11.0), P.DESK_SHADE)
    for k, color in enumerate((P.DRAWER_RED, P.DRAWER_BLUE, P.BLOCKS[2])):
        y = DESK_TOP - 2.4 - k * 5.4
        _box(m, (x0 + 12.0, y - 4.6, z0 + 0.9), (x0 + 12.4, y, z0 + 10.5), color)
        geo.ellipsoid(m, (x0 + 12.7, y - 2.3, z0 + 5.7), (0.45, 0.45, 0.45), P.KNOB, LIT, rings=4, segments=8)


def lamp(m, glow):
    """Table Manners' red desk lamp at the back of the desk, head aimed at the bins."""
    bx, bz = -46.0, 51.0
    geo.cylinder_between(m, (bx, DESK_TOP, bz), (bx, DESK_TOP + 1.2, bz), 5.5, 14, P.LAMP_RED, LIT)
    elbow = (bx + 1.0, DESK_TOP + 17.0, bz + 1.0)
    geo.cylinder_between(m, (bx, DESK_TOP + 1.2, bz), elbow, 0.9, 8, P.LAMP_RED, LIT)
    geo.cylinder_between(m, elbow, LAMP_HEAD, 0.9, 8, P.LAMP_RED, LIT)
    geo.ellipsoid(m, elbow, (1.6, 1.6, 1.6), P.LAMP_DARK, LIT, rings=4, segments=8)
    tx, ty, tz = -16.0, -2.0, 4.0
    d = [tx - LAMP_HEAD[0], ty - LAMP_HEAD[1], tz - LAMP_HEAD[2]]
    n = math.sqrt(sum(v * v for v in d))
    d = [v / n for v in d]
    mouth = tuple(LAMP_HEAD[i] + d[i] * 6.0 for i in range(3))
    geo.cylinder_between(m, LAMP_HEAD, mouth, 2.2, 14, P.LAMP_RED, LIT, radius_b=5.2, caps=False)
    with geo.state(glow, alpha=1.0):
        geo.ellipsoid(glow, tuple(LAMP_HEAD[i] + d[i] * 4.6 for i in range(3)), (2.2, 2.2, 2.2), P.BULB, GLOW, rings=5, segments=10)
    geo.cylinder_between(m, (-30, DESK_TOP, 50), (-30, DESK_TOP + 9, 50), 3.0, 12, P.CUP_BLUE, LIT)
    for i, color in enumerate((P.PENCIL_R, P.PENCIL_Y, P.PENCIL_G, P.PENCIL_B)):
        a = i * 1.6
        geo.cylinder_between(m, (-30 + math.cos(a), DESK_TOP + 6, 50 + math.sin(a)),
                             (-30 + 1.8 * math.cos(a), DESK_TOP + 15 + i, 50 + 1.8 * math.sin(a)), 0.45, 6, color, LIT)
    for i, color in enumerate((P.PAINT_R, P.PAINT_Y, P.PAINT_B)):
        geo.cylinder_between(m, (-58 + i * 5.5, DESK_TOP, 46), (-58 + i * 5.5, DESK_TOP + 4, 46), 2.2, 10, color, LIT)
    for i in range(4):
        _box(m, (-63, DESK_TOP + i * 0.9, 40 + i * 0.4), (-51, DESK_TOP + (i + 1) * 0.9, 54 - i * 0.3),
             (P.PAPER if i % 2 == 0 else P.PAPER_BLUE))


def bookshelf(m):
    """Low bookshelf against the east wall under the right bin; books face the room (south)."""
    x0, x1, z0, z1 = 10.4, 64.0, -14.0, 30.0
    _box(m, (x0, SHELF_TOP - 1.4, z0), (x1, SHELF_TOP, z1), P.SHELF)
    _box(m, (x0, FLOOR_Y, z0 + 1.0), (x0 + 1.4, SHELF_TOP - 1.4, z1), P.SHELF_SHADE)
    _box(m, (x1 - 1.4, FLOOR_Y, z0 + 1.0), (x1, SHELF_TOP - 1.4, z1), P.SHELF_SHADE)
    _box(m, (x0, FLOOR_Y, z1 - 1.0), (x1, SHELF_TOP - 1.4, z1), P.SHELF_SHADE)
    _box(m, (x0 + 1.4, FLOOR_Y, z0 + 1.0), (x1 - 1.4, FLOOR_Y + 1.0, z1 - 1.0), P.SHELF)
    rng = random.Random(7)
    x = x0 + 2.0
    while x < x1 - 4:
        w = rng.uniform(1.4, 2.6)
        _box(m, (x, FLOOR_Y + 1.0, z0 + 2.0), (x + w, FLOOR_Y + 1.0 + rng.uniform(9.0, 12.0), z0 + 2.0 + rng.uniform(7, 9)),
             rng.choice(P.BOOKS))
        x += w + 0.15


def west_bookshelf(m):
    """Table Manners' tall west-wall bookshelf, kept south of the camera's reach (z < -35.4)."""
    x0, x1, z0, z1, top = -65.5, -56.0, -66.0, -36.0, 22.0 + TM_OFFSET
    _box(m, (x0, FLOOR_Y, z0), (x1, top, z0 + 1.2), P.SHELF_SHADE)
    _box(m, (x0, FLOOR_Y, z1 - 1.2), (x1, top, z1), P.SHELF_SHADE)
    _box(m, (x0, FLOOR_Y, z0), (x0 + 1.0, top, z1), P.SHELF_SHADE)
    rng = random.Random(13)
    for k in range(4):
        y = FLOOR_Y + 1.0 + k * 9.0
        _box(m, (x0, y - 1.0, z0), (x1, y, z1), P.SHELF)
        z = z0 + 1.6
        while z < z1 - 3.0:
            w = rng.uniform(1.2, 2.2)
            _box(m, (x0 + 1.2, y, z), (x0 + 1.2 + rng.uniform(5.5, 7.5), y + rng.uniform(5.5, 7.8), z + w), rng.choice(P.BOOKS))
            z += w + 0.15
    _box(m, (x0, top, z0), (x1, top + 1.0, z1), P.SHELF)


def bunk_bed(m):
    """Table Manners' bunk bed in the north-east corner, turned along the north wall to clear the
    camera's reach; the kid sleeps in the lower bunk under a starry blanket."""
    x0, x1, z0, z1 = 8.5, 65.5, 47.0, 71.5
    post = 2.2
    top_post = 36.0 + TM_OFFSET
    for x in (x0, x1 - post):
        for z in (z0, z1 - post):
            _box(m, (x, FLOOR_Y, z), (x + post, top_post, z + post), P.BED_WOOD)
            geo.ellipsoid(m, (x + post / 2, top_post + 0.6, z + post / 2), (1.3, 1.3, 1.3), P.BED_WOOD, LIT, rings=5, segments=10)
    for (yb, yt) in ((-12.0 + TM_OFFSET, -7.0 + TM_OFFSET), (11.0 + TM_OFFSET, 15.0 + TM_OFFSET)):
        _box(m, (x0 + post, yb, z0), (x1 - post, yt, z0 + 1.4), P.BED_DARK)
        _box(m, (x0 + post, yb, z1 - 1.4), (x1 - post, yt, z1), P.BED_DARK)
        _box(m, (x0, yb, z0 + post), (x0 + 1.2, yt + 6.0, z1 - post), P.BED_WOOD)
        _box(m, (x1 - 1.2, yb, z0 + post), (x1, yt + 6.0, z1 - post), P.BED_WOOD)
    for ytop, cover, stripe in ((-3.6 + TM_OFFSET, P.BLANKET, P.BLANKET_STAR), (18.6 + TM_OFFSET, P.BLANKET_GREEN, P.PILLOW)):
        _box(m, (x0 + 1.2, ytop - 3.6, z0 + 1.0), (x1 - 1.2, ytop - 1.2, z1 - 1.0), P.MATTRESS)
        _box(m, (x0 + 13.0, ytop - 1.4, z0 + 0.6), (x1 - 1.0, ytop, z1 - 1.2), cover,
             side_color=geo.scale(cover, 0.9))
        for xx in range(int(x0 + 16), int(x1 - 6), 7):
            _box(m, (xx, ytop - 1.0, z0 + 0.5), (xx + 1.6, ytop + 0.02, z1 - 1.3), stripe)
        geo.box(m, (x0 + 7.5, ytop + 1.4, (z0 + z1) / 2 - 2.0), (8.0, 3.2, 14.0), P.PILLOW, LIT)
    # Guard rail on the top bunk facing the room (south), with the ladder gap.
    for xx in range(int(x0 + post), int(x1 - post) + 1, 6):
        if 44.0 < xx < 55.0:
            continue
        _box(m, (xx - 0.5, 15.0 + TM_OFFSET, z0), (xx + 0.5, 27.0 + TM_OFFSET, z0 + 1.0), P.BED_WOOD)
    for i in range(5):
        _box(m, (44.0, -10.0 + TM_OFFSET + i * 6.0, z0 - 1.0), (55.0, -9.0 + TM_OFFSET + i * 6.0, z0 + 0.2), P.BED_WOOD)
    # The sleeping kid: head on the pillow at the west end, a lump under the blanket.
    head = (x0 + 7.0, -3.6 + TM_OFFSET + 3.4, (z0 + z1) / 2 - 2.0)
    geo.ellipsoid(m, head, (2.6, 2.5, 2.5), P.SKIN, LIT, rings=6, segments=10)
    geo.ellipsoid(m, (head[0] - 0.8, head[1] + 1.0, head[2]), (2.8, 2.3, 2.8), P.HAIR, LIT, rings=6, segments=10)
    geo.ellipsoid(m, (x0 + 26.0, -3.6 + TM_OFFSET + 1.6, (z0 + z1) / 2 - 2.0), (13.0, 2.4, 7.0), P.BLANKET, LIT,
                  rings=5, segments=14)


def window(m, glow):
    """Table Manners' north window, the moon and the garden at night."""
    z = ROOM[3]
    x0, x1, y0, y1 = WINDOW
    for lo, hi in (((x0 - 2, y0 - 2, z - 1.5), (x1 + 2, y0, z)), ((x0 - 2, y1, z - 1.5), (x1 + 2, y1 + 2, z)),
                   ((x0 - 2, y0, z - 1.5), (x0, y1, z)), ((x1, y0, z - 1.5), (x1 + 2, y1, z)),
                   (((x0 + x1) / 2 - 0.8, y0, z - 1.5), ((x0 + x1) / 2 + 0.8, y1, z)),
                   ((x0, (y0 + y1) / 2 - 0.8, z - 1.5), (x1, (y0 + y1) / 2 + 0.8, z))):
        _box(m, lo, hi, P.WINDOW_FRAME)
    _box(m, (x0 - 4, y0 - 4, z - 5), (x1 + 4, y0 - 2, z), P.WINDOW_FRAME)   # sill
    with geo.state(glow, alpha=0.0):
        geo.vertex_grid(glow, (x0 - 40, y0 - 30, z + 30), (x1 - x0 + 80, 0, 0), (0, y1 - y0 + 60, 0), 4, 6,
                        lambda p: geo.mix(P.SKY_LOW, P.SKY_HIGH, max(0.0, min(1.0, (p[1] - y0 + 30) / (y1 - y0 + 60)))), GLOW)
    with geo.state(glow, alpha=1.0):
        geo.ellipsoid(glow, (-10.0, y1 - 6.0, z + 26), (4.5, 4.5, 0.5), P.MOON, GLOW, rings=6, segments=16)
    rng = random.Random(11)
    for i in range(8):
        cx = x0 - 20 + i * 12 + rng.uniform(-3, 3)
        h = rng.uniform(14, 26)
        geo.cylinder_between(m, (cx, y0 - 25, z + 22), (cx, y0 - 25 + h, z + 22), rng.uniform(5, 8), 7,
                             P.TREE, LIT, radius_b=0.5)
    for cx in (x0 - 6, x1 + 6):
        geo.ellipsoid(m, (cx, (y0 + y1) / 2 + 3, z - 3), (4.5, 26.0, 2.0), P.CURTAIN, LIT, rings=6, segments=8)


def craft_table(m):
    """Table Manners' pink craft table (radius 15, floor-length cloth) with the play-dough cake,
    south of the bins with two kids' chairs."""
    cx, cz, r = 0.0, -50.0, 15.0
    top = 0.0 + TM_OFFSET
    geo.lathe(m, (cx, FLOOR_Y, cz), [(r + 0.6, 0.0), (r + 0.4, top - FLOOR_Y - 0.2)], 48, [P.GINGHAM_DARK], LIT, cap_top=False)
    geo.lathe(m, (cx, top - 0.4, cz), [(r, 0.0), (r, 0.4)], 48, P.GINGHAM, LIT)
    geo.lathe(m, (cx, top, cz), [(4.0, 0.0), (4.0, 0.5), (3.6, 0.5), (3.6, 1.0), (3.2, 1.0), (3.2, 1.5)], 24,
              [P.CAKE_PINK, P.CAKE_PINK, P.CAKE_PURPLE, P.CAKE_PURPLE, P.CAKE_YELLOW], LIT)
    for (x, z, color) in ((-20.0, -60.0, P.CHAIR), (20.0, -60.0, P.CAKE_PINK)):
        for dx in (-3.2, 3.2):
            for dz in (-3.2, 3.2):
                _box(m, (x + dx - 0.5, FLOOR_Y, z + dz - 0.5), (x + dx + 0.5, FLOOR_Y + 9.0, z + dz + 0.5), color)
        _box(m, (x - 3.8, FLOOR_Y + 9.0, z - 3.8), (x + 3.8, FLOOR_Y + 10.2, z + 3.8), color)
        _box(m, (x - 3.8, FLOOR_Y + 10.2, z - 4.4), (x + 3.8, FLOOR_Y + 19.0, z - 3.6), color)


def room_shell(m, glow):
    x0, x1, z0, z1 = ROOM
    planks = 12
    for i in range(planks):
        a = x0 + (x1 - x0) * i / planks
        b = x0 + (x1 - x0) * (i + 1) / planks
        geo.top_grid(m, a, z0, b, z1, FLOOR_Y, 1, 6,
                     lambda u, v, i=i: geo.scale(P.FLOOR, 0.9 + 0.12 * ((i * 5 + v * 3) % 4) / 3), LIT)
    rings = [(18.0, P.RUG[0]), (14.0, P.RUG[1]), (10.0, P.RUG[2]), (6.5, P.RUG[3]), (3.0, P.RUG[4])]
    for k, (r, color) in enumerate(rings):
        geo.lathe(m, (0.0, FLOOR_Y + 0.05 + k * 0.02, 2.0), [(r, 0.0), (r, 0.01)], 36, color, LIT)
    # Table Manners' walls at night: beadboard wainscot under sky-blue wallpaper with clouds.
    wx0, wx1, wy0, wy1 = WINDOW[0] - 2, WINDOW[1] + 2, WINDOW[2] - 2, WINDOW[3] + 2
    dx0, dx1, dtop = DOOR
    pieces = [((x1, z0), (x1, z1), FLOOR_Y, CEILING_Y), ((x0, z1), (x0, z0), FLOOR_Y, CEILING_Y),
              ((x0, z1), (wx0, z1), FLOOR_Y, CEILING_Y), ((wx1, z1), (x1, z1), FLOOR_Y, CEILING_Y),
              ((wx0, z1), (wx1, z1), FLOOR_Y, wy0), ((wx0, z1), (wx1, z1), wy1, CEILING_Y),
              ((x1, z0), (dx1, z0), FLOOR_Y, CEILING_Y), ((dx0, z0), (x0, z0), FLOOR_Y, CEILING_Y),
              ((dx1, z0), (dx0, z0), dtop, CEILING_Y)]
    for (a, b, y0, y1) in pieces:
        if y0 < WAINSCOT_TOP < y1:
            geo.wall_grid(m, a, b, y0, WAINSCOT_TOP, 3, 1, lambda u, v: P.WAINSCOT, LIT)
            geo.wall_grid(m, a, b, WAINSCOT_TOP, y1, 3, 2, lambda u, v: P.WALL, LIT)
        else:
            color = P.WAINSCOT if y1 <= WAINSCOT_TOP else P.WALL
            geo.wall_grid(m, a, b, y0, y1, 2, 1, lambda u, v, c=color: c, LIT)
    # Chair rail on top of the wainscot.
    for lo, hi in (((x0, WAINSCOT_TOP - 0.4, z0), (x1, WAINSCOT_TOP + 0.6, z0 + 0.8)),
                   ((x0, WAINSCOT_TOP - 0.4, z1 - 0.8), (x1, WAINSCOT_TOP + 0.6, z1)),
                   ((x0, WAINSCOT_TOP - 0.4, z0), (x0 + 0.8, WAINSCOT_TOP + 0.6, z1)),
                   ((x1 - 0.8, WAINSCOT_TOP - 0.4, z0), (x1, WAINSCOT_TOP + 0.6, z1))):
        _box(m, lo, hi, P.WAINSCOT_TRIM)
    # The closed door in the south wall.
    _box(m, (dx0, FLOOR_Y, z0), (dx1, dtop, z0 + 0.4), P.DOOR)
    for lo, hi in (((dx0 - 1.2, FLOOR_Y, z0), (dx0, dtop + 1.2, z0 + 0.8)), ((dx1, FLOOR_Y, z0), (dx1 + 1.2, dtop + 1.2, z0 + 0.8)),
                   ((dx0 - 1.2, dtop, z0), (dx1 + 1.2, dtop + 1.2, z0 + 0.8))):
        _box(m, lo, hi, P.WAINSCOT_TRIM)
    geo.ellipsoid(m, (dx0 + 3.0, FLOOR_Y + 22.0, z0 + 0.9), (0.9, 0.9, 0.6), P.KNOB, LIT, rings=4, segments=8)
    # Painted clouds on the upper walls, flush.
    rng = random.Random(5)

    def cloud(centre, axis):
        cx, cy, cz = centre
        for k in range(3):
            u0 = (k - 1) * rng.uniform(3.5, 4.5)
            r = rng.uniform(2.6, 3.8)
            pts = []
            for s in range(12):
                a = 2 * math.pi * s / 12
                du, dv = u0 + r * math.cos(a), r * 0.75 * math.sin(a) + (1.0 if k == 1 else 0.0)
                pts.append((cx + du, cy + dv, cz) if axis == 'z' else (cx, cy + dv, cz + du))
            m.poly(pts, P.WALL_CLOUD, LIT)

    for _ in range(5):
        cloud((rng.uniform(-60, 20), rng.uniform(WAINSCOT_TOP + 6, CEILING_Y - 8), z0 + 0.25), 'z')
    for _ in range(3):
        x = rng.choice((rng.uniform(-60, -32), rng.uniform(32, 60)))
        cloud((x, rng.uniform(WAINSCOT_TOP + 26, CEILING_Y - 6), z1 - 0.25), 'z')
    for _ in range(4):
        cloud((x1 - 0.25, rng.uniform(WAINSCOT_TOP + 6, CEILING_Y - 8), rng.uniform(-60, 40)), 'x')
    for _ in range(3):
        cloud((x0 + 0.25, rng.uniform(WAINSCOT_TOP + 14, CEILING_Y - 8), rng.uniform(-30, 10)), 'x')
    geo.top_grid(m, x0, z0, x1, z1, CEILING_Y, 4, 4, lambda u, v: P.CEILING, LIT)
    for _ in range(22):
        _glow_star(glow, (rng.uniform(x0 + 6, x1 - 6), CEILING_Y - 0.2, rng.uniform(z0 + 6, z1 - 6)),
                   rng.uniform(1.4, 2.6), 'y')
    for _ in range(6):
        _glow_star(glow, (rng.uniform(30, 60), rng.uniform(WAINSCOT_TOP + 20, CEILING_Y - 4), z1 - 0.2), rng.uniform(1.2, 2.0), 'z')
    # Toy blocks on the floor, away from furniture.
    for _ in range(14):
        x, z = rng.uniform(-50, 50), rng.uniform(-68, 44)
        if (x < -10 and z > -16) or (10 < x and -16 < z < 32) or math.hypot(x, z + 50) < 18 or (x > 8 and z > 45):
            continue
        s = rng.uniform(2.0, 3.2)
        geo.box(m, (x, FLOOR_Y + s / 2, z), (s, s, s), rng.choice(P.BLOCKS), LIT, yaw=rng.uniform(0, 1.5))


# Table Manners' atlas slots (tools/blender/table_manners/textures.py SLOTS, 2048 px, y down);
# copied as data so this stage does not import the other generator's modules.
ATLAS = 2048
POSTER_SLOTS = {"house": (0, 64, 512, 362), "fighters": (512, 64, 512, 362), "rainbow": (1024, 64, 512, 362), "rocket": (1536, 64, 512, 362),
                "dino": (0, 426, 512, 362), "corkboard": (512, 426, 512, 307), "map": (1024, 426, 512, 370),
                "abc": (0, 800, 512, 695), "space": (512, 800, 512, 670), "fightguy": (1024, 800, 512, 683),
                "manki": (1536, 800, 512, 652)}


def _slot_uv(slot, inset=3):
    x, y, w, h = POSTER_SLOTS[slot]
    return [((x + inset) / ATLAS, 1 - (y + h - inset) / ATLAS), ((x + w - inset) / ATLAS, 1 - (y + h - inset) / ATLAS),
            ((x + w - inset) / ATLAS, 1 - (y + inset) / ATLAS), ((x + inset) / ATLAS, 1 - (y + inset) / ATLAS)]


def posters(m):
    """Table Manners' posters in their own places (tools/blender/table_manners/room.py posters)."""
    x0, x1, z0, z1 = ROOM
    items = [  # wall, centre along the wall, Table Manners centre height, width, slot
        ("north", 42.0, 20.0, 12.0, "abc"), ("north", -44.0, 20.0, 12.0, "space"),
        ("east", -50.0, 17.0, 18.0, "map"), ("west", 32.0, 18.0, 12.0, "fightguy"),
        ("west", 4.0, 18.0, 20.0, "corkboard"), ("south", -28.0, 20.0, 26.0, "rainbow"),
        ("south", 4.0, 19.0, 11.0, "manki"),
    ]
    for wall, c, v, w, slot in items:
        v += TM_OFFSET
        _, _, sw, sh = POSTER_SLOTS[slot]
        h = w * sh / sw
        # Left-bottom, right-bottom, right-top, left-top for a viewer facing the wall (Unity is
        # left-handed: facing north your right is +X, facing east it is -Z).
        if wall == "north":
            z = z1 - 0.3
            pts = [(c - w / 2, v - h / 2, z), (c + w / 2, v - h / 2, z), (c + w / 2, v + h / 2, z), (c - w / 2, v + h / 2, z)]
        elif wall == "south":
            z = z0 + 0.3
            pts = [(c + w / 2, v - h / 2, z), (c - w / 2, v - h / 2, z), (c - w / 2, v + h / 2, z), (c + w / 2, v + h / 2, z)]
        elif wall == "east":
            x = x1 - 0.3
            pts = [(x, v - h / 2, c + w / 2), (x, v - h / 2, c - w / 2), (x, v + h / 2, c - w / 2), (x, v + h / 2, c + w / 2)]
        else:
            x = x0 + 0.3
            pts = [(x, v - h / 2, c - w / 2), (x, v - h / 2, c + w / 2), (x, v + h / 2, c + w / 2), (x, v + h / 2, c - w / 2)]
        m.poly(pts, (0.62, 0.66, 0.85), 'HH_Posters', _slot_uv(slot))


def toy_box(m):
    """Table Manners' red toy box against the east wall, its teddy kept beyond the camera's reach."""
    x0, x1, z0, z1, top = 50.0, 65.5, -38.0, -14.0, -2.0 + TM_OFFSET
    _box(m, (x0, FLOOR_Y, z0), (x1, top, z1), P.TOY_CHEST)
    _box(m, (x0 - 0.4, top, z0 - 0.4), (x1 + 0.4, top + 1.6, z1 + 0.4), P.TOY_CHEST_LID)
    tx, tz, base = 62.0, -26.0, top + 1.6
    geo.ellipsoid(m, (tx, base + 3.6, tz), (3.2, 3.8, 2.8), P.TEDDY, LIT, rings=6, segments=12)
    geo.ellipsoid(m, (tx, base + 9.0, tz), (2.7, 2.5, 2.5), P.TEDDY, LIT, rings=6, segments=12)
    for s in (-1, 1):
        geo.ellipsoid(m, (tx, base + 11.0, tz + s * 2.0), (0.6, 0.9, 0.9), P.TEDDY, LIT, rings=4, segments=8)
        geo.ellipsoid(m, (tx - 2.2, base + 9.6, tz + s * 0.9), (0.35, 0.35, 0.35), P.EYE, LIT, rings=3, segments=6)
    geo.ellipsoid(m, (tx - 2.3, base + 8.3, tz), (0.8, 0.7, 1.0), P.TEDDY_MUZZLE, LIT, rings=4, segments=8)


def build(b):
    m = b.group('Bedroom', 'background')
    glow = b.group('Bedroom Glow', 'background')
    room_shell(m, glow)
    desk(m)
    lamp(m, glow)
    bookshelf(m)
    west_bookshelf(m)
    bunk_bed(m)
    window(m, glow)
    craft_table(m)
    toy_box(m)
    posters(b.group('Bedroom Posters', 'background', 'HH_Posters'))
