"""Eevee review-render helpers: a 40 degree vertical-FOV camera placed on a gameplay orbit.

Stage previews keep their own materials, lights and stand-ins; renders are agent
review aids only, and Unity gameplay captures remain the in-game reference.
"""
from __future__ import annotations

import math
import os

import bpy
from mathutils import Vector


def B(p):
    """Unity (x, y, z) -> Blender location (the inverse of the FBX import mapping)."""
    return Vector((-p[0], -p[2], p[1]))


def gameplay_camera(sc, clip_end, clip_start=None, fov=40.0):
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens_unit = "FOV"
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle_y = math.radians(fov)
    if clip_start is not None:
        cam_data.clip_start = clip_start
    cam_data.clip_end = clip_end
    cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def render_views(sc, cam, views, views_filter, out_dir, before=None):
    """Render `views` (name -> (target xyz in Unity space, yaw deg with 0 looking north +Z,
    pitch deg, distance)) to out_dir/<name>.png. `views_filter` is an optional comma list;
    `before(name, target, distance)` can move stand-ins before each render."""
    chosen = [v for v in views if not views_filter or v in views_filter.split(",")]
    for name in chosen:
        target, yaw, pitch, dist = views[name]
        y, p = math.radians(yaw), math.radians(pitch)
        fwd = (math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p))
        pos = tuple(target[i] - fwd[i] * dist for i in range(3))
        cam.location = B(pos)
        cam.rotation_euler = (B(target) - B(pos)).to_track_quat("-Z", "Y").to_euler()
        if before is not None:
            before(name, target, dist)
        sc.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("PREVIEW", sc.render.filepath)
