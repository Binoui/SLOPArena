"""Baked night lighting for StageNeon surfaces (pure Python).

StageNeon is unlit: it shows vertex colour x atlas under a flat base level. The Night
Court therefore bakes its lighting into vertex colours: flood pools, light bounced off
the bright court, mural wash lights, sodium wall lamps and neon spill. The court floor
itself is StageVertexLit (it receives the fighter's and the ball's real shadows); it
only gets the flood pools, as a multiplier, and Unity's key light does the rest.

Values are linear-ish light levels; `shade()` divides by LIGHT_SCALE, which the apply
command restores through StageNeon's _Base, so bright pools can exceed albedo x 1.
"""
from __future__ import annotations

import math

LIGHT_SCALE = 1.35


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _norm(a):
    length = math.sqrt(_dot(a, a)) or 1e-9
    return (a[0] / length, a[1] / length, a[2] / length)


def _smooth(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


class Light:
    """Point light, or a spot when `aim` is given; smooth inverse-square falloff."""

    def __init__(self, pos, color, power, radius, aim=None, cone=60.0, soft=0.35, wrap=0.25):
        self.pos, self.color, self.power, self.radius = pos, color, power, radius
        self.dir = _norm(_sub(aim, pos)) if aim is not None else None
        self.cos_outer = math.cos(math.radians(cone / 2))
        self.cos_inner = math.cos(math.radians(cone / 2 * (1 - soft)))
        self.wrap = wrap

    def at(self, p, n):
        d = _sub(self.pos, p)
        dist = math.sqrt(_dot(d, d)) or 1e-6
        l = (d[0] / dist, d[1] / dist, d[2] / dist)
        ndl = max(0.0, (_dot(n, l) + self.wrap) / (1 + self.wrap))
        k = self.power * ndl / (1 + (dist / self.radius) ** 2)
        if self.dir is not None:
            k *= _smooth(self.cos_outer, self.cos_inner, -_dot(l, self.dir))
        return (self.color[0] * k, self.color[1] * k, self.color[2] * k)


class CourtBounce:
    """The floodlit court as a broad low emitter: light on whatever faces it, fading with distance."""

    def __init__(self, half, color, power, radius):
        self.half, self.color, self.power, self.radius = half, color, power, radius

    def at(self, p, n):
        q = (max(-self.half, min(self.half, p[0])), 0.0, max(-self.half, min(self.half, p[2])))
        d = _sub(q, p)
        dist = math.sqrt(_dot(d, d))
        if dist < 1e-4:
            return (0.0, 0.0, 0.0)
        l = (d[0] / dist, d[1] / dist, d[2] / dist)
        ndl = max(0.0, (_dot(n, l) + 0.5) / 1.5)
        k = self.power * ndl / (1 + (dist / self.radius) ** 2)
        return (self.color[0] * k, self.color[1] * k, self.color[2] * k)


class Rig:
    def __init__(self, ambient, sources):
        self.ambient = ambient
        self.sources = sources

    def at(self, p, n):
        r, g, b = self.ambient
        for s in self.sources:
            c = s.at(p, n)
            r, g, b = r + c[0], g + c[1], b + c[2]
        return (r, g, b)

    def shade(self, albedo, p, n, alpha=0.0):
        """Albedo x baked light (scaled into vertex-colour range) with StageNeon glow alpha."""
        light = self.at(p, n)
        return tuple(min(1.0, albedo[i] * light[i] / LIGHT_SCALE) for i in range(3)) + (alpha,)


def pools(floods, p, base=0.72, peak=0.4, sigma=7.5):
    """Court-floor multiplier: brighter under each flood's aim point."""
    total = base
    for x, z in floods:
        d2 = (p[0] - x) ** 2 + (p[2] - z) ** 2
        total += peak * math.exp(-d2 / (2 * sigma * sigma))
    return min(1.0, total)
