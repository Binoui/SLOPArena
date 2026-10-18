"""The court surface and the net, both on the authoritative shell.

Court Floor (StageVertexLit, the court texture): a 1 m vertex grid at y = 0 whose
colours carry only the flood pools; Unity's key light, the fighter's and the ball's
shadows do the rest. Court Net (StageNeon): cords, rows and tape inside the baked
10 cm net slab, wall to wall, under the baked lighting.
"""
from __future__ import annotations

import palette as P
import parts
from lighting import pools
from stagekit import geo


def build(b):
    half = b.spec["half"]
    floor = b.group("Court Floor", role="floor")
    with geo.state(floor, alpha=0.3):  # StageVertexLit: a little triplanar grit
        parts.panel(floor, (-half, 0.0, -half), (2 * half, 0.0, 0.0), (0.0, 0.0, 2 * half), 40, 40,
                    (0.0, 0.0, 1.0, 1.0), lambda p, n: (pools(b.floods_aim, p),) * 3, "NC_Court")

    net = b.group("Court Net", role="net")
    top, slab = b.spec["net_top"], b.spec["net_half"]
    cord, tape = P.NET_CORD, P.NET_TAPE
    t = 0.012
    k = 0
    x = -half + 0.25
    while x < half - 0.1:
        c = b.rig.shade(cord, (x, 0.5, -slab), (0.0, 0.0, -1.0))
        geo.box(net, (x, 0.55, 0.0), (t, 0.94, t), c, "NC_Night", skip=("top", "bottom"))
        x += 0.25
        k += 1
    for y in (0.08, 0.32, 0.56, 0.80):
        c = b.rig.shade(cord, (0.0, y, -slab), (0.0, 0.0, -1.0))
        geo.box(net, (0.0, y, 0.0), (2 * half, t, t), c, "NC_Night", skip=("e", "w"))
    # white tape along the top, within the slab, and a thin foot cord
    for u0 in range(-20, 20, 2):
        p = (u0 + 1.0, top - 0.04, -0.025)
        geo.box(net, (u0 + 1.0, top - 0.04, 0.0), (2.0, 0.08, 0.05), b.rig.shade(tape, p, (0.0, 0.3, -1.0)),
                "NC_Night", skip=("e", "w") if -20 < u0 < 18 else ())
    geo.box(net, (0.0, 0.015, 0.0), (2 * half, 0.03, 0.03), b.rig.shade(cord, (0, 0.02, 0), (0, 1, 0)), "NC_Night")
    return k
