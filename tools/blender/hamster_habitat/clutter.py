"""Room density pass: desk hutch and things, the play corner, fairy lights, toys under the gap.

Everything above the KO line stays beyond the camera's reach (|x| > 57.5 or |z| > 35.4);
build.py checks the actual faces.
"""
from __future__ import annotations
import math
import random
from stagekit import font, geo
import palette as P
from room import DESK_TOP, FLOOR_Y, GLOW, LIT, POSTER_SLOTS, ROOM, SHELF_TOP, _slot_uv


def _box(m, lo, hi, color, **kw):
    geo.box_minmax(m, lo, hi, color, LIT, **kw)


def _text_on_south_face(m, text, cx, cy, z, height, color):
    strokes, _ = font.text_strokes(text, height, origin=(cx, cy - height / 2), tracking=1.1)
    w = height * 0.16
    for line in strokes:
        for a, b in zip(line, line[1:]):
            du, dv = b[0] - a[0], b[1] - a[1]
            n = math.hypot(du, dv) or 1.0
            pu, pv = -dv / n * w / 2, du / n * w / 2
            m.poly([(a[0] - pu, a[1] - pv, z), (b[0] - pu, b[1] - pv, z),
                    (b[0] + pu, b[1] + pv, z), (a[0] + pu, a[1] + pv, z)], color, LIT)


def desk_hutch(m):
    """A hutch against the north wall at the back of the desk, behind the hamster and lamp."""
    x0, x1, z0, z1 = -64.0, -10.4, 66.0, 71.8
    top = DESK_TOP + 34.0
    _box(m, (x0, DESK_TOP, z1 - 1.0), (x1, top, z1), P.DESK_SHADE)                 # back panel
    for x in (x0, -46.0, -28.0, x1 - 1.2):
        _box(m, (x, DESK_TOP, z0), (x + 1.2, top, z1), P.DESK)                     # uprights
    for y in (DESK_TOP + 15.0, top - 1.2):
        _box(m, (x0, y, z0), (x1, y + 1.2, z1), P.DESK)                           # shelves
    rng = random.Random(21)
    for lo, hi in ((x0 + 1.4, -46.0), (-44.8, -28.0), (-26.8, x1 - 1.4)):
        x = lo + 0.4
        while x < hi - 2.2:
            w = rng.uniform(1.4, 2.4)
            _box(m, (x, DESK_TOP + 16.2, z0 + 0.8), (x + w, DESK_TOP + 16.2 + rng.uniform(8, 12), z1 - 1.2),
                 rng.choice(P.BOOKS))
            x += w + 0.2
    for i, color in enumerate(P.JARS):
        cx = -60.0 + i * 5.5
        geo.cylinder_between(m, (cx, DESK_TOP, z0 + 2.5), (cx, DESK_TOP + 6.5, z0 + 2.5), 2.0, 10, color, LIT)
        geo.cylinder_between(m, (cx, DESK_TOP + 6.5, z0 + 2.5), (cx, DESK_TOP + 7.5, z0 + 2.5), 2.1, 10, P.JAR_LID, LIT)


def desk_things(m):
    """Peanut's food bag on the bookshelf by the wheel bin, a crayon box and a juice box on the desk."""
    # Food bag beyond the camera's reach (x > 57.5), its label facing south.
    bx, bz, base = 61.0, 8.0, SHELF_TOP
    _box(m, (bx - 3, base, bz - 2.5), (bx + 3, base + 14, bz + 2.5), P.FOOD_BAG)
    _box(m, (bx - 3.1, base + 14, bz - 1.2), (bx + 3.1, base + 15.2, bz + 1.2), P.FOOD_BAG_DARK)
    _box(m, (bx - 2.6, base + 5.5, bz - 2.55), (bx + 2.6, base + 11.5, bz - 2.5), P.FOOD_LABEL)
    _text_on_south_face(m, 'PEANUT', bx, base + 9.6, bz - 2.6, 0.75, P.MARKER)
    _text_on_south_face(m, 'CRUNCH', bx, base + 7.4, bz - 2.6, 0.75, P.DRAWER_RED)
    # Crayon box with crayons poking out.
    _box(m, (-60, DESK_TOP, 36), (-52, DESK_TOP + 6, 40), P.CRAYON_BOX)
    for i, color in enumerate((P.PENCIL_R, P.PENCIL_Y, P.PENCIL_G, P.PENCIL_B, P.PAINT_R)):
        x = -59.0 + i * 1.5
        geo.cylinder_between(m, (x, DESK_TOP + 5, 38), (x, DESK_TOP + 8.5 + (i % 2), 38), 0.55, 6, color, LIT)
    # Juice box with a straw.
    _box(m, (-63, DESK_TOP, 56), (-59, DESK_TOP + 7, 59), P.JUICE)
    geo.cylinder_between(m, (-60, DESK_TOP + 7, 57.5), (-59, DESK_TOP + 10.5, 57.0), 0.3, 6, P.STRAW, LIT)


def south_wall(m, glow):
    """A star nightlight low on the south wall and a backpack dropped by the door."""
    z0 = ROOM[2]
    with geo.state(glow, alpha=1.0):
        _star(glow, (60.0, FLOOR_Y + 8.0, z0 + 0.4), 2.2, P.NIGHTLIGHT)
    geo.ellipsoid(m, (22.0, FLOOR_Y + 4.0, z0 + 6.0), (4.5, 4.0, 3.0), P.BACKPACK, LIT, rings=6, segments=12)
    geo.ellipsoid(m, (22.0, FLOOR_Y + 3.0, z0 + 3.3), (2.8, 2.2, 1.0), P.BACKPACK_POCKET, LIT, rings=4, segments=8)


def _star(m, centre, size, color):
    cx, cy, cz = centre
    pts = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5
        r = size if k % 2 == 0 else size * 0.45
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a), cz))
    for k in range(10):
        m.poly([centre, pts[k], pts[(k + 1) % 10]], color, GLOW)


def play_corner(m):
    """Table Manners' wooden train track (straight, south of the furniture) and a block castle."""
    z, xa, xb = -24.0, -40.0, 40.0
    _box(m, (xa, FLOOR_Y, z - 1.6), (xb, FLOOR_Y + 0.5, z + 1.6), P.TRACK)
    for side in (-0.8, 0.8):
        _box(m, (xa, FLOOR_Y + 0.5, z + side - 0.15), (xb, FLOOR_Y + 0.62, z + side + 0.15), P.TRAIN_DARK)
    for i, color in enumerate((P.TRAIN_RED, P.TRAIN_BLUE, P.TRAIN_YELLOW)):
        x = -6.0 + i * 4.6
        _box(m, (x - 2.0, FLOOR_Y + 0.6, z - 1.5), (x + 2.0, FLOOR_Y + 3.6, z + 1.5), color)
    geo.cylinder_between(m, (-6.0, FLOOR_Y + 3.6, z), (-6.0, FLOOR_Y + 6.0, z), 0.6, 8, P.TRAIN_DARK, LIT)
    rng = random.Random(4)
    bx, bz = -42.0, -64.0
    for level in range(3):
        for i in range(4 - level):
            for j in range(2):
                geo.box(m, (bx + (i - (3 - level) / 2) * 3.0, FLOOR_Y + 1.5 + level * 3.0, bz + (j - 0.5) * 3.0),
                        (2.9, 2.9, 2.9), rng.choice(P.BLOCKS), LIT)


def fairy_lights(m, glow):
    """Warm fairy lights strung across the north and west walls under the ceiling."""
    x0, x1, z0, z1 = ROOM
    runs = [((x0 + 3, 44.0, z1 - 1.0), (-26.0, 40.0, z1 - 1.0)), ((-26.0, 40.0, z1 - 1.0), (26.0, 45.0, z1 - 1.0)),
            ((26.0, 45.0, z1 - 1.0), (x1 - 3, 41.0, z1 - 1.0)),
            ((x0 + 1.0, 44.0, z1 - 3), (x0 + 1.0, 40.0, 0.0)), ((x0 + 1.0, 40.0, 0.0), (x0 + 1.0, 45.0, z0 + 3))]
    for a, b in runs:
        pts = geo.catenary(a, b, 4.0, 14)
        for p, q in zip(pts, pts[1:]):
            geo.cylinder_between(m, p, q, 0.08, 4, P.WIRE, LIT, caps=False)
        with geo.state(glow, alpha=1.0):
            for i, p in enumerate(pts[1:-1]):
                geo.ellipsoid(glow, (p[0], p[1] - 0.6, p[2]), (0.45, 0.6, 0.45), P.FAIRY[i % len(P.FAIRY)], GLOW,
                              rings=3, segments=6)


def gap_floor_toys(m):
    """Toys on the floor under the gap: a ball, a toy car, crayons and an open book."""
    geo.ellipsoid(m, (-6.0, FLOOR_Y + 4.0, -10.0), (4.0, 4.0, 4.0), P.BALL, LIT, rings=6, segments=12,
                  color_fn=lambda r, s: P.BALL_STRIPE if s % 4 == 0 else P.BALL)
    geo.box(m, (3.0, FLOOR_Y + 2.0, 18.0), (7.0, 3.0, 4.0), P.TRAIN_BLUE, LIT, yaw=0.5)
    for dx in (-2.5, 2.5):
        for dz in (-1.8, 1.8):
            c, s = math.cos(0.5), math.sin(0.5)
            geo.cylinder_between(m, (3 + dx * c + dz * s - 0.5, FLOOR_Y + 1.1, 18 - dx * s + dz * c),
                                 (3 + dx * c + dz * s + 0.5, FLOOR_Y + 1.1, 18 - dx * s + dz * c), 1.1, 8, P.TRAIN_DARK, LIT)
    for i, color in enumerate((P.PENCIL_R, P.PENCIL_B, P.PENCIL_Y)):
        geo.cylinder_between(m, (-14.0 + i * 1.5, FLOOR_Y + 0.6, 4.0 + i * 3), (-4.0 + i * 1.5, FLOOR_Y + 0.6, 2.0 + i * 3),
                             0.6, 6, color, LIT)
    for side in (-1, 1):
        m.poly([(14.0, FLOOR_Y + 0.4, -6.0), (14.0 + side * 9.0, FLOOR_Y + 1.4, -6.5),
                (14.0 + side * 9.0, FLOOR_Y + 1.4, 5.5), (14.0, FLOOR_Y + 0.4, 6.0)], P.PAPER, LIT)


def flat_drawings(m):
    """The kid's crayon drawings lying flat on the desk and bookshelf tops: they stay below the KO
    line, so they may fill the open furniture inside the camera's reach."""
    items = [  # x, z, top, yaw, width, slot
        (-48.0, -6.0, DESK_TOP, 0.25, 12.0, "house"), (-40.0, 13.0, DESK_TOP, -0.35, 11.0, "dino"),
        (-55.0, 25.0, DESK_TOP, 0.6, 11.0, "rocket"), (-57.0, 2.0, DESK_TOP, -0.15, 10.0, "fighters"),
        (-44.0, 29.0, DESK_TOP, 0.1, 10.0, "rainbow"),
        (46.0, 18.0, SHELF_TOP, -0.4, 11.0, "fighters"), (54.0, -6.0, SHELF_TOP, 0.3, 10.0, "house"),
    ]
    for i, (x, z, top, yaw, w, slot) in enumerate(items):
        _, _, sw, sh = POSTER_SLOTS[slot]
        h = w * sh / sw
        c, s = math.cos(yaw), math.sin(yaw)
        y = top + 0.04 + i * 0.012
        corner = lambda u, v: (x + u * c + v * s, y, z - u * s + v * c)
        # Viewed from above with north up: left-bottom, right-bottom, right-top, left-top.
        m.poly([corner(-w / 2, -h / 2), corner(w / 2, -h / 2), corner(w / 2, h / 2), corner(-w / 2, h / 2)],
               (0.70, 0.72, 0.88), 'HH_Posters', _slot_uv(slot))


def build(b):
    m = b.group('Bedroom Clutter', 'background')
    glow = b.group('Bedroom Glow', 'background')
    desk_hutch(m)
    desk_things(m)
    south_wall(m, glow)
    play_corner(m)
    fairy_lights(m, glow)
    gap_floor_toys(m)
    flat_drawings(b.group('Bedroom Posters', 'background', 'HH_Posters'))
