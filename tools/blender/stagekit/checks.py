"""Gameplay-clearance helpers shared by the stage generators (pure Python)."""
from __future__ import annotations

import argparse
import sys

# The PvP orbit camera: Cinemachine radius 10 x radial zoom 1.5 reaches 15 m from the
# fighter, and the side blast lines sit 10 m beyond the outermost collision
# (ArenaCollision.SideBlastMargin). Scenery within that reach must stay low enough that
# the camera never clips into it.
CAMERA_REACH = 15.0
SIDE_BLAST_MARGIN = 10.0
CAMERA_REACH_FROM_OUTLINE = CAMERA_REACH + SIDE_BLAST_MARGIN
CEILING_CLEARANCE = 2.0  # metres below the kill plane


def build_args(default_out, default_report):
    """The generator flags every build.py accepts, read after Blender's `--`."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=default_out)
    p.add_argument("--report", default=str(default_report))
    p.add_argument("--preview", action="store_true")
    p.add_argument("--views", default="")
    p.add_argument("--no-bake", action="store_true")
    p.add_argument("--no-export", action="store_true")
    p.add_argument("--spec-only", action="store_true")
    p.add_argument("--samples", type=int, default=48)
    return p.parse_args(argv)


def intersects_corridor(vertices, corridor):
    """Clip a convex face against the open blast box, including crossing edges.

    Testing only vertices misses a large triangle spanning the whole corridor.
    The tiny inset treats contact with the boundary as outside the forbidden box.
    """
    polygon = list(vertices)
    for axis, low, high in ((0, corridor["minX"], corridor["maxX"]),
                            (1, corridor["killY"], corridor["topY"]),
                            (2, corridor["minZ"], corridor["maxZ"])):
        for plane, sign in ((low + 1e-6, 1), (high - 1e-6, -1)):
            if not polygon:
                return False
            clipped = []
            previous = polygon[-1]
            previous_distance = sign * (previous[axis] - plane)
            for current in polygon:
                distance = sign * (current[axis] - plane)
                if (distance >= 0) != (previous_distance >= 0):
                    t = previous_distance / (previous_distance - distance)
                    clipped.append(tuple(a + t * (b - a) for a, b in zip(previous, current)))
                if distance >= 0:
                    clipped.append(current)
                previous, previous_distance = current, distance
            polygon = clipped
    return bool(polygon)


def budget(triangles, planned):
    """Report entry for the stage's planned triangle count. A planning note, not a
    performance gate: the stage workflow measures performance in Unity."""
    return {"triangles": triangles, "planned": planned, "over": triangles > planned}
