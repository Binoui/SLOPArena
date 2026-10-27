"""The giant hamster: on the desk behind the left bin, peeking in (background only).

Every face stays north of z 35.4, outside the camera's reach above the KO line; build.py
checks the actual faces.
"""
from __future__ import annotations
from stagekit import geo
import palette as P
from room import DESK_TOP, GLOW

# Its own shaded material: URP lights a renderer with at most two extra lights on High, so the
# lamp-side shading is carried by StageNeon's directional shade instead of a face light.
TOON = 'HH_Toon'

X, Z = -22.0, 49.5     # stands here, facing -Z towards the bins
K = 1.3                # giant: 1.3x the first pass, scaled about its feet


def _ell(m, dx, y, dz, radii, color, rings=7, segments=12, mat=TOON):
    geo.ellipsoid(m, (X + K * dx, DESK_TOP + K * (y - DESK_TOP), Z + K * dz), tuple(K * r for r in radii),
                  color, mat, rings=rings, segments=segments)


def build(b):
    m = b.group('Hamster', 'background', TOON)
    glow = b.group('Hamster Glints', 'background')
    _ell(m, 0, DESK_TOP + 10.5, 2.0, (9.0, 11.0, 7.5), P.FUR, rings=8, segments=14)        # body
    _ell(m, 0, DESK_TOP + 9.5, -2.5, (7.0, 9.0, 4.0), P.FUR_WHITE, rings=7, segments=12)   # belly
    for side in (-1, 1):
        _ell(m, side * 5.0, DESK_TOP + 1.2, -1.0, (2.6, 1.4, 3.4), P.PAW)                 # feet
        _ell(m, side * 3.8, DESK_TOP + 16.5, -5.5, (2.0, 1.7, 2.1), P.PAW, rings=5, segments=8)  # raised paws
    _ell(m, 0, DESK_TOP + 23.5, -2.0, (7.2, 6.2, 6.6), P.FUR, rings=8, segments=14)        # head
    for side in (-1, 1):
        _ell(m, side * 4.4, DESK_TOP + 21.6, -6.2, (3.0, 2.6, 2.2), P.FUR_WHITE, rings=6, segments=10)  # cheeks
        _ell(m, side * 5.2, DESK_TOP + 29.6, -1.5, (2.4, 2.8, 1.2), P.FUR, rings=5, segments=8)          # ears
        _ell(m, side * 5.2, DESK_TOP + 29.6, -2.5, (1.6, 2.0, 0.5), P.EAR_PINK, rings=4, segments=8)
        _ell(m, side * 3.0, DESK_TOP + 25.2, -7.0, (1.8, 1.9, 1.0), P.EYE, rings=6, segments=12)          # eyes
        # Whiskers: thin bars fanning out from the cheeks.
        for k, dy in enumerate((-0.6, 0.2, 1.0)):
            a = (X + K * side * 5.5, DESK_TOP + K * (21.8 + dy * 0.6), Z - K * 8.0)
            c = (X + K * side * 13.0, DESK_TOP + K * (21.8 + dy * 1.8), Z - K * (7.0 + k * 0.4))
            geo.cylinder_between(m, a, c, 0.07, 4, P.WHISKER, TOON, caps=False)
    _ell(m, 0, DESK_TOP + 22.9, -8.4, (0.9, 0.7, 0.6), P.NOSE, rings=4, segments=8)
    _ell(m, 0, DESK_TOP + 21.3, -8.0, (1.6, 0.6, 0.5), P.FUR_WHITE, rings=4, segments=8)  # muzzle
    with geo.state(glow, alpha=1.0):
        for side in (-1, 1):
            # Lamp glints in the eyes.
            _ell(glow, side * 3.0 + 0.6, DESK_TOP + 25.9, -7.95, (0.55, 0.55, 0.15), P.GLINT, rings=3, segments=6, mat=GLOW)
