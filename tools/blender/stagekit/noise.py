"""Smooth value noise for procedural textures and terrain (numpy, pure Python callers).

The same lattice hash and fbm as Log Off's texture copy (tools/blender/log_off/textures.py
keeps its own until it switches to `from stagekit import noise`). Inputs are arrays or
scalars in lattice units; outputs lie in [0, 1].
"""
from __future__ import annotations

import numpy as np


def _hash(ix, iz, seed):
    h = (ix.astype(np.int64) * 374761393 + iz.astype(np.int64) * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float64) / 65535.0


def noise(x, z, seed, period=None):
    """Smooth value noise in [0, 1] (lattice spacing 1). `period` tiles it: a number or (px, pz)."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    x0 = np.floor(x)
    z0 = np.floor(z)
    tx = x - x0
    tz = z - z0
    tx = tx * tx * (3 - 2 * tx)
    tz = tz * tz * (3 - 2 * tz)
    ix, iz = x0.astype(np.int64), z0.astype(np.int64)
    px, pz = (period, period) if isinstance(period, (int, float)) else (period or (None, None))

    def h(a, b):
        if px:
            a = a % px
        if pz:
            b = b % pz
        return _hash(a, b, seed)

    a = h(ix, iz) + (h(ix + 1, iz) - h(ix, iz)) * tx
    b = h(ix, iz + 1) + (h(ix + 1, iz + 1) - h(ix, iz + 1)) * tx
    return a + (b - a) * tz


def fbm(x, z, seed, octaves=4, period=None):
    """Fractal sum of `noise` octaves (each doubles frequency, halves amplitude)."""
    x = np.asarray(x, dtype=np.float64)
    z = np.asarray(z, dtype=np.float64)
    total = np.zeros(np.broadcast(x, z).shape, dtype=np.float64)
    amp, norm, f = 1.0, 0.0, 1.0
    for o in range(octaves):
        if period is None:
            p = None
        elif isinstance(period, (int, float)):
            p = period * int(f)
        else:
            p = (period[0] * int(f), period[1] * int(f))
        total += amp * noise(x * f, z * f, seed + 17 * o, p)
        norm += amp
        amp *= 0.5
        f *= 2.0
    return total / norm


def fbm1(x, z, seed, octaves=4):
    """Scalar `fbm` for pure-Python geometry code."""
    return float(fbm(np.float64(x), np.float64(z), seed, octaves))


def smooth(e0, e1, x):
    """Smoothstep from e0 to e1 (either order), arrays or scalars."""
    t = np.clip((np.asarray(x, dtype=np.float64) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    """Blend colour arrays (..., 3) by a weight array (...)."""
    t = np.clip(t, 0.0, 1.0)[..., None]
    return a * (1 - t) + b * t
