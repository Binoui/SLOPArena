"""The harbour bay around the boat: one shared map for the sea, coast and village.

Unity world coordinates (+X east, +Z north). The boat lies at the origin with its bow
east, towards the bay mouth and the morning sun. WATER is the CCW polygon of open
water (bay plus open sea, closed far offshore in the east); everything outside it is
land whose height comes from `land_height`. The west shore is a straight stone quay
under the village; a rubble breakwater with the lighthouse shelters the inner harbour
in the north-west; olive hills and a chapel rise in the south; rocky headlands frame
the mouth. Nothing solid comes near the blast corridor: the nearest land is the quay,
about 110 m behind the transom.
"""
from __future__ import annotations

import math

import numpy as np

from stagekit.noise import fbm

WATER_Y = -11.4          # visual sea level; the KO plane (-12) lies 0.6 m under it
QUAY_TOP = WATER_Y + 1.8
QUAY_X = -125.0          # quay wall face
QUAY_BACK = -136.0       # village frontage
QUAY_Z = (-78.0, 136.0)

# Open water, CCW in map view (water on the left walking the coast).
WATER = [
    (3000.0, 2600.0), (1500.0, 1400.0), (700.0, 700.0), (400.0, 420.0), (260.0, 300.0),
    (150.0, 230.0), (90.0, 190.0), (40.0, 172.0), (-20.0, 171.0), (-70.0, 166.0),
    (-102.0, 153.0), (-118.0, 138.0), (-125.0, 120.0), (-125.0, 60.0), (-125.0, 0.0),
    (-125.0, -50.0), (-121.0, -78.0), (-105.0, -104.0), (-88.0, -122.0), (-73.0, -136.0),
    (-63.0, -151.0), (-49.0, -158.0), (-35.0, -153.0), (-25.0, -141.0), (0.0, -137.0),
    (40.0, -139.0), (80.0, -136.0), (120.0, -126.0), (160.0, -110.0), (200.0, -92.0),
    (228.0, -80.0), (238.0, -88.0), (236.0, -104.0), (260.0, -150.0), (340.0, -250.0),
    (700.0, -620.0), (1600.0, -1500.0), (3000.0, -2600.0),
]

# Rubble breakwater from the north shore to the lighthouse: it closes the bay's
# north-east side and shelters the inner harbour west of it.
MOLE = [(36.0, 174.0), (74.0, 146.0), (114.0, 119.0), (146.0, 97.0)]
MOLE_TOP = WATER_Y + 3.4
MOLE_HALF = 4.5          # half-width of the concrete walk
LIGHTHOUSE = (152.0, 92.0)
BEACH = (-49.0, -150.0)  # sandy cove in the south shore
CHAPEL = (-38.0, -232.0)
CHURCH = (-176.0, 14.0)  # bell tower in the village
SUN_AZIMUTH = 112.0      # degrees from +Z towards +X (east-south-east)
SUN_ELEVATION = 36.0

_SEGMENTS = None


def _segments():
    global _SEGMENTS
    if _SEGMENTS is None:
        a = np.array(WATER, dtype=np.float64)
        b = np.roll(a, -1, axis=0)
        _SEGMENTS = (a, b)
    return _SEGMENTS


def shore_distance(x, z):
    """Signed distance to the coastline: positive on land, negative in the water."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    a, b = _segments()
    px = x[..., None]
    pz = z[..., None]
    ex, ez = b[:, 0] - a[:, 0], b[:, 1] - a[:, 1]
    L2 = ex * ex + ez * ez
    t = np.clip(((px - a[:, 0]) * ex + (pz - a[:, 1]) * ez) / L2, 0.0, 1.0)
    dx = px - (a[:, 0] + ex * t)
    dz = pz - (a[:, 1] + ez * t)
    d = np.sqrt(dx * dx + dz * dz).min(axis=-1)
    # Even-odd test for the water polygon.
    crosses = ((a[:, 1] > pz) != (b[:, 1] > pz)) & (px < a[:, 0] + (pz - a[:, 1]) * ex / np.where(ez == 0, 1e-12, ez))
    water = (crosses.sum(axis=-1) % 2) == 1
    return np.where(water, -d, d)


def _sector(x, z):
    """Weights (west, north, south, far) that blend the regional land profiles."""
    ang = np.degrees(np.arctan2(z, x + 10.0)) % 360.0  # 0 = east, 90 = north
    r = np.hypot(x + 10.0, z)
    west = np.clip(1.0 - np.abs(ang - 180.0) / 55.0, 0.0, 1.0)
    north = np.clip(1.0 - np.abs(ang - 95.0) / 50.0, 0.0, 1.0)
    south = np.clip(1.0 - np.abs(ang - 262.0) / 52.0, 0.0, 1.0)
    total = west + north + south
    far = np.clip((r - 600.0) / 600.0, 0.0, 1.0)
    rest = np.clip(1.0 - total, 0.0, 1.0)  # headlands round the mouth
    return west, north, south, rest, far


def land_height(x, z, d=None):
    """Terrain height (arrays). Land rises from the shore by region; water is a shelf."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    if d is None:
        d = shore_distance(x, z)
    west, north, south, rest, far = _sector(x, z)
    dl = np.maximum(d, 0.0)
    # Regional profiles: long village slope (west), rocky rise (north), olive ridge (south),
    # cliffs on the headlands, broad hazy hills far away.
    # West: a flat street behind the quay, then the village slope and the hill above it.
    h_west = 1.8 + 0.27 * np.maximum(dl - 16.0, 0.0) + 52.0 * (1 - np.exp(-np.maximum(dl - 150.0, 0.0) / 220.0))
    h_north = 4.0 * (1 - np.exp(-dl / 7.0)) + 72.0 * (1 - np.exp(-dl / 150.0))
    ridge = (58.0 * np.exp(-((dl - 95.0) / 70.0) ** 2) * (1 - np.exp(-dl / 14.0))
             + 70.0 * (1 - np.exp(-np.maximum(dl - 120.0, 0.0) / 260.0)))
    h_south = 3.0 * (1 - np.exp(-dl / 5.0)) + ridge + 0.12 * dl * np.exp(-dl / 90.0)
    h_rest = 18.0 * (1 - np.exp(-dl / 8.0)) + 46.0 * (1 - np.exp(-dl / 120.0))
    h = (h_west * west + h_north * north + h_south * south + h_rest * rest) / np.maximum(west + north + south + rest, 1e-6)
    h_far = 30.0 * (1 - np.exp(-dl / 40.0)) + 120.0 * (1 - np.exp(-dl / 500.0))
    h = h * (1 - far) + h_far * far
    n = fbm(x / 160.0, z / 160.0, 401, 4) - 0.5
    m = fbm(x / 38.0, z / 38.0, 402, 3) - 0.5
    calm = 1.0 - west * (1.0 - np.clip((dl - 16.0) / 70.0, 0.0, 1.0))  # keep the village street level
    h = h + (14.0 * n + 3.0 * m) * np.clip(dl / 40.0, 0.0, 1.0) * calm
    # The sandy cove: a gentle beach that rises slowly behind the waterline.
    cove = np.clip(1.0 - np.hypot(x - BEACH[0], z - BEACH[1]) / 42.0, 0.0, 1.0) ** 0.7
    h = h * (1 - cove) + (0.09 * dl + 0.0004 * dl * dl) * cove
    # The quay platform under the village frontage is exactly flat.
    quay = (x > QUAY_BACK - 5.0) & (x <= QUAY_X + 0.01) & (z > QUAY_Z[0]) & (z < QUAY_Z[1])
    h = np.where(quay, QUAY_TOP - WATER_Y, h)
    # Underwater shelf: drop away from the coast.
    under = d < 0
    h = np.where(under, -np.minimum(-d * 0.35 + 1.5, 30.0), h)
    return WATER_Y + h


def height(x, z):
    return float(land_height(np.float64(x), np.float64(z)))


def polygon_area(poly):
    a = 0.0
    for i in range(len(poly)):
        x0, z0 = poly[i]
        x1, z1 = poly[(i + 1) % len(poly)]
        a += x0 * z1 - x1 * z0
    return a / 2


def mole_distance(x, z):
    """Distance to the breakwater centre line (scalar)."""
    best = 1e9
    for (ax, az), (bx, bz) in zip(MOLE[:-1], MOLE[1:]):
        ex, ez = bx - ax, bz - az
        t = max(0.0, min(1.0, ((x - ax) * ex + (z - az) * ez) / (ex * ex + ez * ez)))
        best = min(best, math.hypot(x - (ax + ex * t), z - (az + ez * t)))
    return best


def sun_direction():
    """Unit vector towards the sun in Unity space."""
    az, el = math.radians(SUN_AZIMUTH), math.radians(SUN_ELEVATION)
    return (math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az))
