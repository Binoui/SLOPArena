"""Collision-shell export for `sloparena.stage.shell-apply` (pure Python).

A stage's layout.py builds its authoritative collision as named triangle lists; `document`
turns them into the JSON the Unity command reads to rebuild the source scene:

    python3 tools/blender/<stage>/layout.py --unity-shell > /tmp/<stage>-shell.json
    bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch shell.json
    # shell.json: [["sloparena.stage.shell-apply", "--stage", "<stage>", "--from", "/tmp/<stage>-shell.json"],
    #              ["sloparena.stage.bake", "--stage", "<stage>"], ["sloparena.stage.inspect", ...]]

Each mesh becomes one GameplayGeometry child backed by Assets/Stages/<stage>/<asset>.asset
(an existing asset keeps its GUID). Spawn markers and the kill plane are verified against
the layout, or moved with --spawns move. A shell change is a gameplay change: bake, run the
stage's arena tests and the layout's strict check before regenerating art.

A solo Character Course adds `course(...)`: its targets, checkpoints and finish become empty
markers under AuthoringAids with the ArcadeStageAuthoring that the bake compiles into the
arena. Course markers always follow the layout, like the meshes. `--create` builds the source
scene of a new stage (root, spawn markers, kill plane) before the first apply.
"""
from __future__ import annotations

FORMAT = 1


def indexed(triangles):
    """Share vertices between triangles (positions rounded to 1e-6); returns (vertices, indices)."""
    index = {}
    vertices, indices = [], []
    for triangle in triangles:
        for point in triangle:
            key = tuple(round(v, 6) for v in point)
            if key not in index:
                index[key] = len(vertices)
                vertices.append(list(key))
            indices.append(index[key])
    return vertices, indices


def mesh(name, asset, vertices, triangles):
    """One GameplayGeometry child: flat vertex list and triangle index list."""
    return {"name": name, "asset": asset, "vertices": [list(v) for v in vertices], "triangles": list(triangles)}


def merged(name, asset, solids):
    """Several closed solids in one mesh asset, in order, without sharing vertices between them."""
    vertices, triangles = [], []
    for solid_vertices, solid_triangles in solids:
        start = len(vertices)
        vertices.extend(list(v) for v in solid_vertices)
        triangles.extend(start + i for i in solid_triangles)
    return mesh(name, asset, vertices, triangles)


def course(profile_id, targets, checkpoints, finish, revision=1, racing_line=None):
    """An Arcade Character Course for `document(course=...)`.

    targets: [(id, (x, y, z), radius)] or [(id, (x, y, z), radius, hits)] in route order; every target is
        required. `hits` (default 1) makes a bonus target that breaks on its n-th damaging hit.
    checkpoints: [(id, (x, y, z) sensor centre, (hx, hy, hz) half size, (x, y, z) respawn, yaw degrees,
        required target count)]; counts are cumulative and the respawn sits at body height
        (surface + 0.85), like a spawn marker.
    finish: ((x, y, z) centre, (hx, hy, hz) half size).
    """
    ids = [t[0] for t in targets] + [c[0] for c in checkpoints]
    if len(ids) != len(set(ids)) or not targets:
        raise ValueError("course needs targets and unique target/checkpoint ids")
    if any(not 0 < t[2] <= 2 for t in targets):
        raise ValueError("course target radii must be in (0, 2]")
    if any(len(t) > 3 and not 1 <= t[3] <= 32 for t in targets):
        raise ValueError("course target hits must be in [1, 32]")
    counts = [c[5] for c in checkpoints]
    if counts != sorted(counts) or any(not 0 < n <= len(targets) for n in counts):
        raise ValueError("checkpoint target counts must be cumulative and within the target count")
    result = {"profileId": profile_id, "gameplayRevision": revision, "allowedPackageIds": [],
            "targets": [_target(*t) for t in targets],
            "checkpoints": [{"id": i, "centre": list(c), "halfSize": list(h), "respawn": list(r), "yaw": yaw,
                             "requiredTargets": n} for i, c, h, r, yaw, n in checkpoints],
            "finish": {"centre": list(finish[0]), "halfSize": list(finish[1])}}
    if racing_line is not None:
        result["racingLine"] = racing_line
    return result


def _target(target_id, position, radius, hits=1):
    target = {"id": target_id, "position": list(position), "radius": radius}
    if hits != 1:
        target["hits"] = hits   # single-hit targets keep the original document
    return target


def document(stage, kill_y, meshes, spawns, course=None):
    """spawns: [(x, y, z, yaw)] in Spawn_01.. order, y at the spawn marker (body centre).
    course: optional `course(...)` for a solo Character Course."""
    names = [m["name"] for m in meshes]
    if len(names) != len(set(names)):
        raise ValueError("GameplayGeometry child names must be unique")
    doc = {"format": FORMAT, "stage": stage, "killY": kill_y, "meshes": meshes,
           "spawns": [{"name": "Spawn_%02d" % (i + 1), "position": [x, y, z], "yaw": yaw}
                      for i, (x, y, z, yaw) in enumerate(spawns)]}
    if course is not None:
        doc["course"] = course
    return doc
