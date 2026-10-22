"""Neo Mall's targets for MallCoursePresentationBuilder (client/Unity/Assets/Scripts/Editor):

  Agent      a shop mannequin Wibou dressed as an agent: black suit, white shirt, tie, shades
  AgentHang  the same agent clinging to the elevator tower's lip, hands on the ledge to its +X
  Tubeman 0-3  the phone-case kiosk's inflatable tube man: upright, slashed, deflating, collapsed
  Giant 0-3    THE AGENT, the department store's giant display mannequin: shades off and tie cut,
               an arm off, the head off
  Frank 0-3    the hot dog stand's fiberglass mascot: sliced, halved, a stub of bun
  Doug 0-3     OFFICER DOUG's cardboard cutout: slashed, the cap cut off, cut in half

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_mall/targets.py -- \\
        [--out client/Unity/Assets/Stages/wibou_mall/Presentation/Course] [--no-bake] [--preview <dir>]

Prop space: the origin is the Shared target centre, +Y up, and the side a player meets faces +Z
(the agents turn to the camera; bosses keep this facing, and AgentHang's ledge is at +X). Agent
stands with its feet at y = 0 (the builder lowers it under each target's centre). Bosses stand on
their floor at y = BOSS_FLOOR. Faces use the mall's NM_Lit and NM_Glow on NeoMallAtlas.png, so a
change to the atlas cells needs the stage art rebuilt and applied too (build.py, then
sloparena.stage.art-apply). Each object is baked apart on its own floor, then moved back.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))  # tools/blender: the shared stagekit

from stagekit import geo  # noqa: E402

import palette as P  # noqa: E402
import parts as K  # noqa: E402
import textures as T  # noqa: E402

STEM = "NeoMallTargets"
DEFAULT_OUT = os.path.join(T.REPO, "client/Unity/Assets/Stages/wibou_mall/Presentation/Course")
STAGES = 4
BOSS_FLOOR = -1.4      # layout.py: bosses stand 1.4 m under their target centre
HANG_LEDGE = -0.4      # layout.py: the lip agents' centres are 0.4 m over the tower top

MANNEQUIN = (0.95, 0.94, 0.91)
SUIT = (0.07, 0.07, 0.08)
SHIRT = (0.97, 0.97, 0.98)
SHADES = (0.02, 0.02, 0.03)
CUT = (0.86, 0.86, 0.84)     # the clean inside of a sliced mannequin
TUBE_RED = (0.92, 0.16, 0.16)
TUBE_YELLOW = (1.0, 0.82, 0.18)
BUN = (0.86, 0.60, 0.30)
BUN_DARK = (0.66, 0.40, 0.18)
SAUSAGE = (0.70, 0.24, 0.16)
MUSTARD = (1.0, 0.82, 0.10)
GLOVE = (0.98, 0.98, 0.98)
SHOE_RED = (0.86, 0.12, 0.12)
CARDBOARD = (0.72, 0.56, 0.38)
CARDBOARD_DARK = (0.50, 0.38, 0.24)
FAN = (0.25, 0.26, 0.30)


# ------------------------------------------------------------------ the agent

def _agent(m, scale=1.0, base=(0.0, 0.0, 0.0), arms="down", shades=True, tie=True, head=True, arm_l=True):
    """A mannequin in a black suit, standing at `base`, facing +Z."""
    bx, by, bz = base

    def p(x, y, z):
        return (bx + x * scale, by + y * scale, bz + z * scale)
    s = scale
    for side in (-1, 1):
        geo.box(m, p(side * 0.11, 0.04, 0.04), (0.13 * s, 0.08 * s, 0.27 * s), SUIT, K.LIT)
        geo.cylinder_between(m, p(side * 0.11, 0.08, 0.0), p(side * 0.12, 0.93, 0.0), 0.075 * s, 8, SUIT, K.LIT, radius_b=0.085 * s)
    geo.box(m, p(0.0, 0.96, 0.0), (0.36 * s, 0.16 * s, 0.22 * s), SUIT, K.LIT)
    geo.box(m, p(0.0, 1.2, 0.0), (0.42 * s, 0.5 * s, 0.24 * s), SUIT, K.LIT)
    # shirt and tie on the front
    geo.oriented(m, [p(-0.08, 1.45, 0.122), p(0.08, 1.45, 0.122), p(0.0, 1.2, 0.122)], (0, 0, 1), SHIRT, K.LIT)
    if tie:
        geo.oriented(m, [p(-0.025, 1.42, 0.125), p(0.025, 1.42, 0.125), p(0.03, 1.02, 0.125), p(0.0, 0.98, 0.125), p(-0.03, 1.02, 0.125)],
                     (0, 0, 1), SUIT, K.LIT)
    for side in (-1, 1):
        if side == -1 and not arm_l:
            continue
        geo.ellipsoid(m, p(side * 0.22, 1.41, 0.0), (0.09 * s, 0.08 * s, 0.1 * s), SUIT, K.LIT, rings=4, segments=8)
        if arms == "down":
            geo.cylinder_between(m, p(side * 0.23, 1.4, 0.0), p(side * 0.28, 0.98, 0.02), 0.06 * s, 8, SUIT, K.LIT)
            geo.ellipsoid(m, p(side * 0.285, 0.92, 0.03), (0.05 * s, 0.065 * s, 0.05 * s), MANNEQUIN, K.LIT, rings=4, segments=8)
        else:  # crossed over the chest
            geo.cylinder_between(m, p(side * 0.23, 1.38, 0.0), p(side * 0.2, 1.12, 0.16), 0.065 * s, 8, SUIT, K.LIT)
            geo.cylinder_between(m, p(side * 0.2, 1.12, 0.16), p(-side * 0.12, 1.24, 0.17), 0.06 * s, 8, SUIT, K.LIT)
            geo.ellipsoid(m, p(-side * 0.17, 1.25, 0.17), (0.05 * s, 0.05 * s, 0.06 * s), MANNEQUIN, K.LIT, rings=4, segments=8)
    if head:
        geo.cylinder_between(m, p(0.0, 1.44, 0.0), p(0.0, 1.54, 0.0), 0.05 * s, 8, MANNEQUIN, K.LIT)
        geo.ellipsoid(m, p(0.0, 1.66, 0.0), (0.1 * s, 0.13 * s, 0.11 * s), MANNEQUIN, K.LIT, rings=6, segments=12)
        if shades:
            geo.box(m, p(0.0, 1.68, 0.1), (0.21 * s, 0.05 * s, 0.03 * s), SHADES, K.GLASS)
    else:
        geo.cylinder_between(m, p(0.0, 1.44, 0.0), p(0.0, 1.5, 0.0), 0.05 * s, 8, MANNEQUIN, K.LIT)
        K.hdisc(m, *p(0.0, 1.5, 0.0), 0.05 * s, CUT, n=8)


def agent():
    m = geo.Mesh("Agent", K.MATERIALS)
    _agent(m)
    return m


def agent_hang():
    """Clinging to the tower's lip: hands on the ledge (+X), head peeking over, legs dangling."""
    m = geo.Mesh("AgentHang", K.MATERIALS)
    ledge = HANG_LEDGE
    for side in (-1, 1):
        z = side * 0.16
        hand = (0.66, ledge + 0.04, z)
        elbow = (0.42, ledge - 0.26, z * 1.3)
        shoulder = (0.16, ledge - 0.12, z * 1.4)
        geo.ellipsoid(m, hand, (0.07, 0.04, 0.05), MANNEQUIN, K.LIT, rings=4, segments=8)
        geo.cylinder_between(m, hand, elbow, 0.055, 8, SUIT, K.LIT)
        geo.cylinder_between(m, elbow, shoulder, 0.06, 8, SUIT, K.LIT)
    # torso hanging below the shoulders, leaning into the wall
    geo.cylinder_between(m, (0.18, ledge - 0.12, 0.0), (0.1, ledge - 0.72, 0.0), 0.17, 10, SUIT, K.LIT, radius_b=0.15)
    geo.cylinder_between(m, (0.1, ledge - 0.72, 0.0), (0.08, ledge - 0.86, 0.0), 0.15, 10, SUIT, K.LIT)
    for side in (-1, 1):
        geo.cylinder_between(m, (0.08, ledge - 0.86, side * 0.09), (0.0, ledge - 1.7, side * 0.12 + 0.05 * side), 0.075, 8, SUIT, K.LIT)
        geo.box(m, (0.03, ledge - 1.74, side * 0.13), (0.24, 0.07, 0.12), SUIT, K.LIT)
    geo.cylinder_between(m, (0.2, ledge - 0.1, 0.0), (0.26, ledge + 0.0, 0.0), 0.05, 8, MANNEQUIN, K.LIT)
    geo.ellipsoid(m, (0.3, ledge + 0.14, 0.0), (0.12, 0.13, 0.1), MANNEQUIN, K.LIT, rings=6, segments=12)
    geo.box(m, (0.41, ledge + 0.17, 0.0), (0.03, 0.05, 0.2), SHADES, K.GLASS)
    return m


# ------------------------------------------------------------------ bosses

def _tube_bands(m, path, radius, colors, segments=10):
    for k, (a, b) in enumerate(zip(path, path[1:])):
        geo.cylinder_between(m, a, b, radius, segments, colors[k % len(colors)], K.LIT, caps=(k == 0 or k == len(path) - 2))


def _tube_face(m, top, normal_z=1.0, tilt=0.0):
    x, y, z = top
    for side in (-1, 1):
        geo.ellipsoid(m, (x + side * 0.1, y + 0.05, z + 0.24 * normal_z), (0.08, 0.1, 0.04), P.WHITE, K.LIT, rings=4, segments=8)
        geo.ellipsoid(m, (x + side * 0.1, y + 0.03, z + 0.27 * normal_z), (0.035, 0.045, 0.02), P.INK, K.LIT, rings=3, segments=6)
    geo.ellipsoid(m, (x, y - 0.18, z + 0.25 * normal_z), (0.12, 0.08, 0.04), P.INK, K.LIT, rings=4, segments=8)


def tubeman(stage):
    m = geo.Mesh("Tubeman %d" % stage, K.MATERIALS)
    f = BOSS_FLOOR
    geo.box(m, (0.0, f + 0.25, 0.0), (0.8, 0.5, 0.7), FAN, K.LIT)
    geo.box(m, (0.0, f + 0.25, 0.36), (0.5, 0.3, 0.02), P.STEEL, K.LIT)
    colors = [TUBE_RED, TUBE_YELLOW]
    if stage <= 1:
        path = [(0.08 * math.sin(i * 1.3), f + 0.5 + i * 0.38, 0.0) for i in range(9)]
        _tube_bands(m, path, 0.28, colors)
        top = path[-1]
        geo.ellipsoid(m, (top[0], top[1] + 0.12, 0.0), (0.28, 0.2, 0.28), TUBE_RED, K.LIT, rings=4, segments=10)
        for k in range(7):
            a = k * math.pi / 6
            geo.cylinder_between(m, (top[0] + 0.15 * math.cos(a), top[1] + 0.25, 0.0), (top[0] + 0.3 * math.cos(a), top[1] + 0.55, 0.05),
                                 0.04, 4, P.HACKER if k % 2 else P.PLANT_LIGHT, K.LIT)
        _tube_face(m, (top[0], top[1] - 0.1, 0.0))
        arm_y = f + 0.5 + 6 * 0.38
        lift = 0.75 if stage == 0 else 0.35
        for side in (-1, 1):
            geo.cylinder_between(m, (side * 0.25, arm_y, 0.0), (side * 0.75, arm_y + lift, 0.1), 0.12, 8, TUBE_RED, K.LIT)
            geo.cylinder_between(m, (side * 0.75, arm_y + lift, 0.1), (side * 1.0, arm_y + lift + 0.35, 0.15), 0.1, 8, TUBE_YELLOW, K.LIT)
        if stage == 1:
            # the slash: a dark diagonal tear across the front
            y0 = f + 1.6
            geo.oriented(m, [(-0.29, y0 - 0.2, 0.29), (0.29, y0 + 0.25, 0.29), (0.29, y0 + 0.33, 0.29), (-0.29, y0 - 0.12, 0.29)],
                         (0, 0, 1), P.INK, K.LIT)
    elif stage == 2:
        low = [(0.0, f + 0.5 + i * 0.38, 0.0) for i in range(5)]
        _tube_bands(m, low, 0.27, colors)
        bend = low[-1]
        hi = [(bend[0] + 0.32 * i, bend[1] + 0.12 * i - 0.06 * i * i, 0.05 * i) for i in range(5)]
        _tube_bands(m, hi, 0.24, colors[::-1])
        top = hi[-1]
        geo.ellipsoid(m, (top[0] + 0.1, top[1] - 0.05, top[2]), (0.24, 0.2, 0.24), TUBE_RED, K.LIT, rings=4, segments=10)
        _tube_face(m, (top[0] + 0.1, top[1] - 0.1, top[2]))
        for side in (-1, 1):
            geo.cylinder_between(m, (side * 0.24, bend[1] - 0.1, 0.0), (side * 0.5, bend[1] - 0.8, 0.12), 0.11, 8, TUBE_RED, K.LIT)
    else:
        for k in range(5):
            a = k * 1.3
            geo.ellipsoid(m, (0.35 * math.cos(a), f + 0.6 + 0.08 * k, 0.3 * math.sin(a)), (0.45, 0.14, 0.32),
                          colors[k % 2], K.LIT, rings=4, segments=10)
        geo.ellipsoid(m, (0.55, f + 0.62, 0.35), (0.24, 0.18, 0.24), TUBE_RED, K.LIT, rings=4, segments=10)
        _tube_face(m, (0.55, f + 0.65, 0.1))
    return m


def giant(stage):
    """THE AGENT: a 2.6 m display mannequin, arms crossed, losing a piece per stage."""
    m = geo.Mesh("Giant %d" % stage, K.MATERIALS)
    f = BOSS_FLOOR
    s = 1.45
    geo.lathe(m, (0.0, f, 0.0), [(0.55, 0.0), (0.55, 0.08), (0.12, 0.12), (0.12, 0.2)], 16, [P.STEEL_DARK, P.STEEL, P.STEEL])
    base = (0.0, f + 0.1, 0.0)
    _agent(m, scale=s, base=base, arms="crossed" if stage < 2 else "down", shades=stage == 0, tie=stage == 0,
           head=stage < 3, arm_l=stage < 2)
    if stage == 1:
        # the cut tie still hangs; the shades lie on the plinth
        geo.oriented(m, [(-0.04, f + 0.1 + 1.42 * s, 0.18), (0.04, f + 0.1 + 1.42 * s, 0.18), (0.03, f + 0.1 + 1.25 * s, 0.18),
                         (-0.03, f + 0.1 + 1.25 * s, 0.18)], (0, 0, 1), SUIT, K.LIT)
        geo.box(m, (0.38, f + 0.11, 0.35), (0.3, 0.04, 0.06), SHADES, K.GLASS, yaw=0.5)
    if stage >= 2:
        # the left arm on the floor, its clean white cut
        geo.cylinder_between(m, (-0.7, f + 0.1, 0.3), (-1.2, f + 0.1, 0.65), 0.09, 8, SUIT, K.LIT)
        geo.ellipsoid(m, (-1.25, f + 0.1, 0.7), (0.07, 0.07, 0.09), MANNEQUIN, K.LIT, rings=4, segments=8)
        K.hdisc(m, -0.32, f + 0.1 + 1.41 * s, 0.0, 0.1, CUT, n=8)
    if stage >= 3:
        geo.ellipsoid(m, (0.8, f + 0.2, 0.5), (0.15, 0.19, 0.16), MANNEQUIN, K.LIT, rings=6, segments=12)
        geo.box(m, (0.95, f + 0.22, 0.6), (0.06, 0.07, 0.3), SHADES, K.GLASS, yaw=-0.6)
    return m


def frank(stage):
    """FRANK, the hot dog stand's mascot: fiberglass bun and sausage, a grin, gloves and shoes."""
    m = geo.Mesh("Frank %d" % stage, K.MATERIALS)
    f = BOSS_FLOOR
    for side in (-1, 1):
        geo.ellipsoid(m, (side * 0.22, f + 0.1, 0.12), (0.17, 0.1, 0.26), SHOE_RED, K.LIT, rings=4, segments=8)
        geo.cylinder_between(m, (side * 0.2, f + 0.15, 0.0), (side * 0.18, f + 0.55, 0.0), 0.06, 6, P.INK, K.LIT)
    bun_top = {0: 2.0, 1: 2.0, 2: 1.0, 3: 0.75}[stage]
    sausage_top = {0: 2.55, 1: 2.1, 2: 1.05, 3: 0.0}[stage]
    # the bun: two fat halves either side of the sausage
    for side in (-1, 1):
        geo.cylinder_between(m, (side * 0.3, f + 0.55, -0.05), (side * 0.3, f + 0.55 + bun_top - 0.5, -0.05), 0.3, 12, BUN, K.LIT)
        geo.ellipsoid(m, (side * 0.3, f + 0.55, -0.05), (0.3, 0.12, 0.3), BUN_DARK, K.LIT, rings=4, segments=12)
        if stage < 2:
            geo.ellipsoid(m, (side * 0.3, f + 0.05 + bun_top, -0.05), (0.3, 0.16, 0.3), BUN, K.LIT, rings=4, segments=12)
        else:
            K.hdisc(m, side * 0.3, f + 0.05 + bun_top, -0.05, 0.28, (0.96, 0.86, 0.62), n=12)
    if sausage_top > 0.0:
        top = f + 0.45 + sausage_top
        geo.cylinder_between(m, (0.0, f + 0.5, 0.05), (0.0, top - 0.2, 0.05), 0.26, 12, SAUSAGE, K.LIT)
        if stage == 0:
            geo.ellipsoid(m, (0.0, top - 0.2, 0.05), (0.26, 0.24, 0.26), SAUSAGE, K.LIT, rings=5, segments=12)
            for side in (-1, 1):
                geo.ellipsoid(m, (side * 0.09, top - 0.35, 0.28), (0.07, 0.09, 0.03), P.WHITE, K.LIT, rings=4, segments=8)
                geo.ellipsoid(m, (side * 0.09, top - 0.36, 0.3), (0.03, 0.04, 0.015), P.INK, K.LIT, rings=3, segments=6)
            geo.ellipsoid(m, (0.0, top - 0.55, 0.28), (0.12, 0.05, 0.02), P.INK, K.LIT, rings=3, segments=8)
        else:
            K.hdisc(m, 0.0, top - 0.2, 0.05, 0.25, (0.92, 0.54, 0.46), n=12)
        # the mustard zigzag down the front
        pts = [(0.12 * (1 if k % 2 else -1), f + 0.7 + k * 0.22, 0.31) for k in range(int((top - f - 1.0) / 0.22))]
        for a, b in zip(pts, pts[1:]):
            geo.cylinder_between(m, a, b, 0.035, 5, MUSTARD, K.LIT)
    if stage < 2:
        # gloved arms waving
        for side in (-1, 1):
            geo.cylinder_between(m, (side * 0.58, f + 1.4, 0.0), (side * 0.85, f + 1.8 + (0.2 if side > 0 else -0.1), 0.1), 0.06, 6, P.INK, K.LIT)
            geo.ellipsoid(m, (side * 0.9, f + 1.92 + (0.2 if side > 0 else -0.1), 0.12), (0.12, 0.13, 0.1), GLOVE, K.LIT, rings=4, segments=8)
    if stage == 1:
        # the sliced-off cap lying beside him
        geo.ellipsoid(m, (0.75, f + 0.15, 0.55), (0.26, 0.15, 0.26), SAUSAGE, K.LIT, rings=4, segments=10)
    if stage >= 2:
        # the upper half on the floor
        geo.cylinder_between(m, (0.6, f + 0.3, 0.4), (1.6, f + 0.3, 0.9), 0.28, 10, BUN, K.LIT)
        geo.cylinder_between(m, (0.7, f + 0.38, 0.45), (1.9, f + 0.38, 1.05), 0.24, 10, SAUSAGE, K.LIT)
        geo.ellipsoid(m, (1.95, f + 0.38, 1.08), (0.24, 0.24, 0.24), SAUSAGE, K.LIT, rings=5, segments=10)
    return m


def _board(m, outline, depth, cell, x_range, y_range, front=True):
    """A flat cardboard shape: `outline` (x, y) CCW seen from +Z, the cell mapped over x/y ranges."""
    (u0, v0), (u1, v1) = T.uv(cell)

    def uv(x, y):
        # Seen from +Z, +X is on the viewer's left: the cell runs toward -X.
        fu = (x_range[1] - x) / (x_range[1] - x_range[0])
        fv = (y - y_range[0]) / (y_range[1] - y_range[0])
        return (u0 + (u1 - u0) * fu, v0 + (v1 - v0) * fv)
    zf, zb = depth / 2, -depth / 2
    c = (sum(p[0] for p in outline) / len(outline), sum(p[1] for p in outline) / len(outline))
    n = len(outline)
    for k in range(n):
        a, b = outline[k], outline[(k + 1) % n]
        geo.oriented(m, [(c[0], c[1], zf), (a[0], a[1], zf), (b[0], b[1], zf)], (0, 0, 1), (1.0, 1.0, 1.0), K.LIT,
                     [uv(*c), uv(*a), uv(*b)] if front else None)
        geo.oriented(m, [(c[0], c[1], zb), (b[0], b[1], zb), (a[0], a[1], zb)], (0, 0, -1), CARDBOARD_DARK, K.LIT)
        geo.oriented(m, [(a[0], a[1], zb), (b[0], b[1], zb), (b[0], b[1], zf), (a[0], a[1], zf)],
                     (b[1] - a[1], -(b[0] - a[0]), 0), CARDBOARD, K.LIT)


def doug(stage):
    """OFFICER DOUG: a 2.1 m cardboard cutout on its easel, its atlas face toward +Z."""
    m = geo.Mesh("Doug %d" % stage, K.MATERIALS)
    f = BOSS_FLOOR
    w, h = 1.05, 2.1
    x0, x1, y0, y1 = -w / 2, w / 2, f, f + h
    full = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    if stage == 0:
        _board(m, full, 0.03, "doug", (x0, x1), (y0, y1))
    elif stage == 1:
        _board(m, full, 0.03, "doug", (x0, x1), (y0, y1))
        geo.oriented(m, [(x0, y0 + 1.3, 0.017), (x1, y0 + 1.7, 0.017), (x1, y0 + 1.74, 0.017), (x0, y0 + 1.34, 0.017)], (0, 0, 1), P.INK, K.LIT)
    elif stage == 2:
        _board(m, [(x0, y0), (x1, y0), (x1, y0 + 1.75), (x0 + 0.3, y1 - 0.05), (x0, y1 - 0.2)], 0.03, "doug", (x0, x1), (y0, y1))
        # the cap on the floor
        piece = geo.Mesh("cap", K.MATERIALS)
        _board(piece, [(x0 + 0.1, y1 - 0.45), (x1 - 0.1, y1 - 0.45), (x1 - 0.1, y1 - 0.15), (x0 + 0.1, y1 - 0.15)], 0.03, "doug", (x0, x1), (y0, y1))
        piece.verts = [(x + 0.9, f + 0.02 + (z + 0.015), 0.4 + (y - (y1 - 0.3)) * 0.1) for x, y, z in piece.verts]
        m.extend(piece)
    else:
        _board(m, [(x0, y0), (x1, y0), (x1, y0 + 1.15), (x0, y0 + 0.85)], 0.03, "doug", (x0, x1), (y0, y1))
        top = geo.Mesh("top", K.MATERIALS)
        _board(top, [(x0, y0 + 0.85), (x1, y0 + 1.15), (x1, y1), (x0, y1)], 0.03, "doug", (x0, x1), (y0, y1))
        # laid face up on the floor in front
        top.verts = [(x, f + 0.03 + z, 0.3 + (y - (y0 + 0.85))) for x, y, z in top.verts]
        m.extend(top)
    # the easel behind
    geo.oriented(m, [(-0.05, f, -0.02), (0.05, f, -0.02), (0.05, f + 1.2, -0.02), (-0.05, f + 1.2, -0.02)], (0, 0, -1), CARDBOARD_DARK, K.LIT)
    geo.cylinder_between(m, (0.0, f + 1.15, -0.03), (0.0, f, -0.6), 0.03, 4, CARDBOARD_DARK, K.LIT)
    return m


def groups():
    meshes = {"Agent": agent(), "AgentHang": agent_hang()}
    for maker in (tubeman, giant, frank, doug):
        for stage in range(STAGES):
            mesh = maker(stage)
            meshes[mesh.name] = mesh
    return meshes


# Where each object's floor sits (prop space), for the bake.
FLOORS = {"Agent": 0.0, "AgentHang": HANG_LEDGE - 2.4, "Tubeman": BOSS_FLOOR, "Giant": BOSS_FLOOR, "Frank": BOSS_FLOOR, "Doug": BOSS_FLOOR}
ATLAS_PREVIEW = None


def preview(objects, names, spacing, out_dir):
    """Eevee renders of each object from the player's side: review aids, not the Unity look."""
    import bpy
    from mathutils import Vector

    from stagekit.preview import B

    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.world.color = (0.30, 0.32, 0.42)
    image = bpy.data.images.load(ATLAS_PREVIEW) if ATLAS_PREVIEW and os.path.exists(ATLAS_PREVIEW) else None
    if image is not None:
        image.alpha_mode = "NONE"
    for mat in bpy.data.materials:
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        col = nt.nodes.new("ShaderNodeVertexColor")
        col.layer_name = "Col"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(col.outputs["Color"], mix.inputs["A"])
        if image is not None:
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = image
            uv = nt.nodes.new("ShaderNodeUVMap")
            uv.uv_map = "UVMap"
            nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
            nt.links.new(tex.outputs["Color"], mix.inputs["B"])
        else:
            mix.inputs["B"].default_value = (1, 1, 1, 1)
        shader = nt.nodes.new("ShaderNodeBsdfDiffuse")
        nt.links.new(mix.outputs["Result"], shader.inputs["Color"])
        nt.links.new(shader.outputs[0], out.inputs["Surface"])
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sc.collection.objects.link(sun)
    sun.rotation_euler = B((0.4, -0.8, -0.6)).to_track_quat("-Z", "Y").to_euler()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    os.makedirs(out_dir, exist_ok=True)
    for i, name in enumerate(names):
        key = name.split()[0]
        target = Vector((i * spacing, FLOORS[key] + 1.4, 0.0))
        yaw, pitch = math.radians(200.0), math.radians(12.0)
        fwd = (math.sin(yaw) * math.cos(pitch), -math.sin(pitch), math.cos(yaw) * math.cos(pitch))
        pos = tuple(target[k] - fwd[k] * 12.0 for k in range(3))
        cam.location = B(pos)
        cam.rotation_euler = (B(tuple(target)) - B(pos)).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = 4.2
        sc.render.filepath = os.path.join(out_dir, name.replace(" ", "_") + ".png")
        bpy.ops.render.render(write_still=True)


def main():
    import argparse

    import bpy
    from mathutils import Matrix

    from stagekit import blender as kit

    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--samples", type=int, default=64)
    ap.add_argument("--preview", help="also render every object to <dir>/<object>.png (review aid; uses --atlas)")
    ap.add_argument("--atlas", default="/tmp/wibou-mall-generated/NeoMallAtlas.png")
    a = ap.parse_args(argv)
    global ATLAS_PREVIEW
    ATLAS_PREVIEW = a.atlas

    meshes = groups()
    spacing = 6.0
    floors = {}
    for i, (name, m) in enumerate(meshes.items()):
        ox = i * spacing
        m.verts = [(x + ox, y, z) for x, y, z in m.verts]
        floor_y = FLOORS[name.split()[0]]
        f = geo.Mesh("Floor %d" % i, K.MATERIALS)
        geo.box(f, (ox, floor_y - 0.05, 0.0), (4.5, 0.1, 4.5), P.TERRAZZO, K.LIT)
        floors[f.name] = f
    kit.reset_scene(K.MATERIALS)
    objects = kit.build_objects({**meshes, **floors}, channels=True)
    glowing = lambda me, c: kit.loop_materials(me) == K.GLOW  # noqa: E731
    bake_seconds = None if a.no_bake else kit.bake_ao(objects, a.samples, ao_distance=0.6, floor=0.6, gamma=1.0,
                                                      skip=tuple(floors), keep=glowing)
    if a.preview:
        preview(objects, list(meshes), spacing, a.preview)
    for name, ob in list(objects.items()):
        if name in floors:
            bpy.data.objects.remove(ob, do_unlink=True)
            del objects[name]
    for i, name in enumerate(meshes):
        # Unity +X is Blender -X (geo.to_object): undo the bake offset.
        objects[name].data.transform(Matrix.Translation((i * spacing, 0.0, 0.0)))
    digest, object_digests = kit.digest(objects)
    os.makedirs(a.out, exist_ok=True)
    path = os.path.join(a.out, STEM + ".fbx")
    kit.export_fbx(objects, path)
    stamp = {"generator": "tools/blender/wibou_mall/targets.py", "blender": bpy.app.version_string,
             "materials": K.MATERIALS, "atlas": "NeoMallAtlas.png",
             "atlasCells": hashlib.sha256(json.dumps(T.CELLS, sort_keys=True).encode()).hexdigest(),
             "objects": sorted(objects), "geometryDigest": digest}
    with open(os.path.join(a.out, STEM + ".layout.json"), "w") as fh:
        json.dump(stamp, fh, indent=1)
        fh.write("\n")
    tris = {n: meshes[n].triangle_count for n in meshes}
    print("WIBOU_MALL_TARGETS", json.dumps({"triangles": sum(tris.values()), "aoBakeSeconds": bake_seconds, "geometryDigest": digest}))
    print("  OBJECTS", json.dumps(tris))
    print("  HEALTH", json.dumps(kit.mesh_health(objects)))


if __name__ == "__main__":
    main()
