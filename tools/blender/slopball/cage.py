"""The floodlit cage on the four bounce walls and the ceiling plane, and the combo board.

Every surface a ball could touch lies on a baked plane: the dasher-board faces and the
chain-link at the walls, the cargo net at the ceiling. Posts, rails and board bodies
stand just outside. The combo board hangs on the sender wall, at most 2 cm proud.
"""
from __future__ import annotations

import palette as P
import parts
import textures as T
from stagekit import geo

BOARD_TOP = 1.2
CAP_TOP = 1.3
POST = 0.12
RAILS = (1.38, 7.5, 13.8, 19.93)
LINK_SPACING = 0.34
LINK_WIRE = 0.036
NET_SPACING = 0.8
NET_ROPE = 0.05

# Combo board on the north wall (u = x for the north side), right of the sender's range.
BOARD_U = (5.6, 10.2)
BOARD_Y = (1.45, 4.55)
CARD_W, CARD_Y = 1.15, (1.72, 3.42)
CARD_U = (6.55, 7.9, 9.25)
CARD_NAMES = ("Combo Hundreds", "Combo Tens", "Combo Ones")


def build(b):
    half, ceil = b.spec["half"], b.spec["ceiling"]
    shell = b.group("Cage", role="shell")
    fence = b.group("Fence", role="shell")
    white = (1.0, 1.0, 1.0)
    for side in parts.SIDES:
        n = side.inward
        # dasher boards: faces on the plane, five 8 m sponsor boards per wall
        for i in range(5):
            u0 = -half + 8 * i
            row = (i + 3 * side.k) % T.BOARD_ROWS
            parts.panel(shell, side.P(u0, 0.0, half), side.vec(8.0, 0, 0), (0, BOARD_TOP, 0), 8, 2,
                        T.board_uv(row), lambda p, nn: b.rig.shade(white, p, nn), "NC_Night")
        # board bodies and the cap rail, behind the plane (the body's court face is the board)
        body = P.STEEL_DARK
        geo.box(shell, side.P(0.0, BOARD_TOP / 2, half + 0.09), (2 * half + 0.36, BOARD_TOP, 0.18),
                b.rig.shade(body, side.P(0, 1.0, half + 0.1), (0, 1, 0)), "NC_Night", yaw=side.yaw, skip=("s",))
        for j in range(8):
            u0 = -half + 5 * j
            cp = side.P(u0 + 2.5, CAP_TOP, half + 0.12)
            geo.box(shell, side.P(u0 + 2.5, (BOARD_TOP + CAP_TOP) / 2, half + 0.12), (5.0, CAP_TOP - BOARD_TOP, 0.24),
                    b.rig.shade(P.CAP, cp, (0.0, 1.0, 0.0)), "NC_Night", yaw=side.yaw)
        # posts every 4 m and four rails, just outside the plane
        for k in range(11):
            u = -half + 4 * k
            cp = side.P(u, 12.0, half + 0.09)
            parts.beam(shell, side.P(u, CAP_TOP, half + POST / 2 + 0.005), side.P(u, ceil + 0.3, half + POST / 2 + 0.005),
                       POST, b.rig.shade(P.STEEL, cp, n), "NC_Night")
        for y in RAILS:
            for s in range(4):
                u0 = -half + 10 * s
                cp = side.P(u0 + 5, y, half + 0.06)
                parts.beam(shell, side.P(u0, y, half + 0.065), side.P(u0 + 10, y, half + 0.065), 0.1,
                           b.rig.shade(P.STEEL, cp, n), "NC_Night")
        # chain-link from the cap rail to the ceiling plane
        parts.chain_link(fence, side, -half, half, CAP_TOP, ceil, half, LINK_SPACING, LINK_WIRE,
                         lambda p, nn: b.rig.shade(P.WIRE, p, nn), "NC_Night", pieces=2.0)
    # corner posts, wholly outside both walls
    for x in (-half, half):
        for z in (-half, half):
            cx, cz = x + (0.2 if x > 0 else -0.2), z + (0.2 if z > 0 else -0.2)
            parts.beam(shell, (cx, 0.0, cz), (cx, ceil + 0.4, cz), 0.22,
                       b.rig.shade(P.STEEL, (x, 10.0, z), (0.0, 0.0, 1.0)), "NC_Night")

    # cargo net on the ceiling plane, two rope families a millimetre apart
    down = (0.0, -1.0, 0.0)
    count = int(round(2 * half / NET_SPACING))
    for i in range(count + 1):
        c = -half + i * NET_SPACING
        for s in range(10):
            a0, a1 = -half + 4 * s, -half + 4 * (s + 1)
            mid = (c, ceil, (a0 + a1) / 2)
            parts.ribbon(fence, (c, ceil + 0.001, a0), (c, ceil + 0.001, a1), NET_ROPE, down,
                         b.rig.shade(P.ROPE, mid, down), "NC_Night")
            mid = ((a0 + a1) / 2, ceil, c)
            parts.ribbon(fence, (a0, ceil + 0.002, c), (a1, ceil + 0.002, c), NET_ROPE, down,
                         b.rig.shade(P.ROPE, mid, down), "NC_Night")
    combo_board(b)


def combo_board(b):
    """Hand-flipped combo board: backing, COMBO header, three digit cards (separate objects
    whose origins sit at their centres so the runtime can flip them)."""
    half = b.spec["half"]
    north = parts.SIDES[0]
    board = b.group("Combo Board", role="shell")
    u0, u1 = BOARD_U
    y0, y1 = BOARD_Y
    n = north.inward
    plate = (0.07, 0.075, 0.085)
    parts.panel(board, north.P(u0, y0, half - 0.01), north.vec(u1 - u0, 0, 0), (0, y1 - y0, 0), 2, 2,
                T.uv("white"), lambda p, nn: b.rig.shade(plate, p, nn), "NC_Night")
    geo.box(board, north.P((u0 + u1) / 2, (y0 + y1) / 2, half + 0.07), (u1 - u0, y1 - y0, 0.14),
            b.rig.shade(plate, north.P((u0 + u1) / 2, y1, half), (0, 1, 0)), "NC_Night", skip=("s",))
    parts.panel(board, north.P(u0 + 0.6, 3.66, half - 0.015), north.vec(u1 - u0 - 1.2, 0, 0), (0, 0.6, 0), 2, 1,
                T.uv("combo"), lambda p, nn: b.rig.shade((1, 1, 1), p, nn), "NC_Night")
    # a small lamp hood over the board
    glow = b.group("Glow", role="shell")
    geo.box(board, north.P((u0 + u1) / 2, y1 + 0.32, half + 0.2), (u1 - u0 - 0.4, 0.14, 0.4),
            b.rig.shade(P.STEEL_DARK, north.P((u0 + u1) / 2, y1 + 0.4, half), (0, 1, 0)), "NC_Night")
    with geo.state(glow, alpha=1.0):
        geo.box(glow, north.P((u0 + u1) / 2, y1 + 0.24, half + 0.08), (u1 - u0 - 0.6, 0.03, 0.2),
                P.WARM, "NC_Night")
    for name, uc in zip(CARD_NAMES, CARD_U):
        card = b.group(name, role="shell", centre=True)
        parts.panel(card, north.P(uc - CARD_W / 2, CARD_Y[0], half - 0.02), north.vec(CARD_W, 0, 0),
                    (0, CARD_Y[1] - CARD_Y[0], 0), 1, 2, T.digit_uv(0),
                    lambda p, nn: b.rig.shade((1, 1, 1), p, nn), "NC_Night")
