"""Small shared shapes for Neo Mall (Unity coordinates, geo.Mesh): planes, panels, atlas plates
and boards, slabs, posts and discs."""
from __future__ import annotations

import math

from stagekit import geo
import palette as P
import textures as T

GROUND = "NM_Ground"    # the planar ground texture on every walkable top
LIT = "NM_Lit"          # vertex colour x atlas (geo.WHITE_UV for plain faces)
GLASS = "NM_Glass"      # glossy vertex-coloured glass and polished stone
GLOW = "NM_Glow"        # StageNeon on the atlas: vertex alpha is the glow mask, animation channels
SKY = "NM_Sky"          # StageNeon, unlit and two-sided: the night through the dome
MATERIALS = [LIT, GROUND, GLASS, GLOW, SKY]

STEADY = (0.0, 0.0)
BLINK = (1.2, 0.0)
SLOW_BLINK = (0.5, 0.0)
PULSE = (51.5, 0.0)
FLICKER = (-1.0, 0.0)

FLOOR_Y = 0.0           # the mopped ground floor (the KO plane is 0.2 below)


class Plane:
    """A vertical plane: origin, horizontal unit `right` and outward unit `normal`.
    `facing` keeps text readable for a viewer looking against the normal."""

    def __init__(self, origin, right, normal):
        self.o, self.r, self.n = origin, right, normal

    @staticmethod
    def facing(origin, normal):
        nx, nz = normal
        L = math.hypot(nx, nz)
        nx, nz = nx / L, nz / L
        return Plane(origin, (-nz, 0.0, nx), (nx, 0.0, nz))

    def at(self, u, v, off=0.0):
        o, r, n = self.o, self.r, self.n
        return (o[0] + r[0] * u + n[0] * off, o[1] + v, o[2] + r[2] * u + n[2] * off)


def box_planes(x0, z0, x1, z1):
    """The four outward planes of a footprint, each with its u range: [(key, plane, u0, u1)]."""
    return [
        ("s", Plane.facing((0.0, 0.0, z0), (0.0, -1.0)), x0, x1),   # south: u runs west -> east
        ("n", Plane.facing((0.0, 0.0, z1), (0.0, 1.0)), -x1, -x0),  # north: u runs east -> west
        ("w", Plane.facing((x0, 0.0, 0.0), (-1.0, 0.0)), -z1, -z0),  # west: u runs north -> south
        ("e", Plane.facing((x1, 0.0, 0.0), (1.0, 0.0)), z0, z1),     # east: u runs south -> north
    ]


def panel(m, plane, u0, v0, u1, v1, color, off=0.0, mat=LIT, uvs=None):
    """Rectangle on the plane (u right, v up) `off` metres proud."""
    quv = None
    if uvs is not None:
        (a0, b0), (a1, b1) = uvs
        quv = [(a0, b0), (a1, b0), (a1, b1), (a0, b1)]
    geo.oriented(m, [plane.at(u0, v0, off), plane.at(u1, v0, off), plane.at(u1, v1, off), plane.at(u0, v1, off)],
                 plane.n, color, mat, quv)


def plate(m, plane, cell, u_center, v_center, width, off=0.012, height=None, color=(1.0, 1.0, 1.0), mat=LIT):
    """An atlas cell on the plane at its own aspect (or `height`)."""
    x, y, w, h = T.CELLS[cell]
    height = height or width * h / w
    panel(m, plane, u_center - width / 2, v_center - height / 2, u_center + width / 2, v_center + height / 2,
          color, off=off, mat=mat, uvs=T.uv(cell))
    return height


def slab(m, plane, u0, v0, u1, v1, depth, color, side=None, mat=LIT, bottom=True):
    """A box standing `depth` proud of the plane over (u0..u1, v0..v1)."""
    sc = side or geo.scale(color, 0.8)
    panel(m, plane, u0, v0, u1, v1, color, off=depth, mat=mat)
    r = plane.r
    for u, d in ((u0, (-r[0], 0.0, -r[2])), (u1, r)):
        geo.oriented(m, [plane.at(u, v0, 0), plane.at(u, v0, depth), plane.at(u, v1, depth), plane.at(u, v1, 0)], d, sc, mat)
    geo.oriented(m, [plane.at(u0, v1, 0), plane.at(u1, v1, 0), plane.at(u1, v1, depth), plane.at(u0, v1, depth)], (0, 1, 0), sc, mat)
    if bottom:
        geo.oriented(m, [plane.at(u0, v0, 0), plane.at(u1, v0, 0), plane.at(u1, v0, depth), plane.at(u0, v0, depth)], (0, -1, 0), sc, mat)


def lit_sign(m, plane, cell, u, v, width, off=0.05, depth=0.12, frame=P.INK):
    """A backlit sign box: a dark frame slab with the cell glowing on its face. Returns its height."""
    x, y, w, h = T.CELLS[cell]
    height = width * h / w
    slab(m, plane, u - width / 2 - 0.05, v - height / 2 - 0.05, u + width / 2 + 0.05, v + height / 2 + 0.05, depth, frame)
    with geo.state(m, alpha=1.0):
        plate(m, plane, cell, u, v, width, off=depth + off * 0.2, mat=GLOW)
    return height


def disc(m, plane, u, v, r, color, off=0.0, n=16, ry=None, mat=LIT):
    """A flat filled ellipse on the plane."""
    ry = r if ry is None else ry
    c = plane.at(u, v, off)
    pts = [plane.at(u + r * math.cos(2 * math.pi * k / n), v + ry * math.sin(2 * math.pi * k / n), off) for k in range(n)]
    for k in range(n):
        geo.oriented(m, [c, pts[k], pts[(k + 1) % n]], plane.n, color, mat)


def hdisc(m, x, y, z, r, color, n=16, mat=LIT, down=False):
    """A horizontal disc facing up (or down)."""
    pts = [(x + r * math.cos(2 * math.pi * k / n), y, z + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    for k in range(n):
        geo.oriented(m, [(x, y, z), pts[k], pts[(k + 1) % n]], (0, -1, 0) if down else (0, 1, 0), color, mat)


def post(m, x, z, y0, y1, w, color, mat=LIT):
    geo.box(m, (x, (y0 + y1) / 2, z), (w, y1 - y0, w), color, mat, skip=("bottom",))


def round_post(m, x, z, y0, y1, r, color, n=10, mat=LIT):
    geo.cylinder_between(m, (x, y0, z), (x, y1, z), r, n, color, mat)


def cable(m, a, b, color=P.STEEL_DARK, w=0.03):
    """A thin straight cable (a 4-sided tube)."""
    geo.cylinder_between(m, a, b, w, 4, color, LIT, caps=False)


def glow_quad(m, pts, normal, color=(1.0, 1.0, 1.0), anim=STEADY):
    """A glowing face sampling the lamp block."""
    with geo.state(m, alpha=1.0, anim=anim):
        geo.oriented(m, pts, normal, color, GLOW, [T.LAMP_UV] * len(pts))
