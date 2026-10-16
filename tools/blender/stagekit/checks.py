"""Gameplay-clearance helpers shared by the stage generators (pure Python)."""
from __future__ import annotations


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
