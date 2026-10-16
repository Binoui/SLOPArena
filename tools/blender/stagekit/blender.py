"""Blender-side steps shared by the stage generators: objects, AO bake, FBX export
and a geometry digest. Run inside `blender -b --factory-startup`."""
from __future__ import annotations

import hashlib
import os
import time

import bpy

from stagekit import geo


def reset_scene(materials):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for name in materials:
        bpy.data.materials.new(name)


def build_objects(groups, smooth=(), channels=False):
    """One Blender object per non-empty geo.Mesh group, in insertion order."""
    col = bpy.context.scene.collection
    return {name: geo.to_object(m, col, smooth=name in smooth, channels=channels)
            for name, m in groups.items() if m.faces}


def setup_cycles(samples, ao_distance):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
            if any(d.type == kind for d in prefs.devices):
                for d in prefs.devices:
                    d.use = d.type == kind
                sc.cycles.device = "GPU"
                break
        except Exception:
            continue
    sc.cycles.samples = samples
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")
    sc.world.light_settings.distance = ao_distance


def bake_ao(objects, samples, ao_distance, floor, gamma, skip=(), keep=None):
    """Multiply a Cycles AO bake into the "Col" corner colours of every object not in `skip`:
    colour *= clip(floor + (1 - floor) * ao ** gamma). `keep(mesh, rgba)` may return a
    per-corner boolean mask of corners to leave unshaded (for example glowing faces).
    Returns the bake time in seconds."""
    import numpy as np
    setup_cycles(samples, ao_distance)
    targets = [o for n, o in objects.items() if n not in skip]
    for o in objects.values():
        o.select_set(False)
    for o in targets:
        me = o.data
        ao = me.color_attributes.new("AO", "FLOAT_COLOR", "CORNER")
        me.color_attributes.active_color = ao
        o.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    t = time.time()
    bpy.context.scene.render.bake.target = "VERTEX_COLORS"
    bpy.ops.object.bake(type="AO")
    for o in targets:
        me = o.data
        n = len(me.loops)
        col = np.empty(n * 4, dtype=np.float32)
        ao = np.empty(n * 4, dtype=np.float32)
        me.color_attributes["Col"].data.foreach_get("color_srgb", col)
        me.color_attributes["AO"].data.foreach_get("color", ao)
        a = ao.reshape(-1, 4)[:, 0]
        factor = np.clip(floor + (1.0 - floor) * np.power(a, gamma), 0.0, 1.0)
        c = col.reshape(-1, 4)
        if keep is not None:
            factor = np.where(keep(me, c), 1.0, factor)
        c[:, :3] *= factor[:, None]
        me.color_attributes["Col"].data.foreach_set("color_srgb", c.ravel())
        me.color_attributes.remove(me.color_attributes["AO"])
        me.color_attributes.active_color = me.color_attributes["Col"]
    return round(time.time() - t, 1)


def loop_materials(me):
    """Material slot name for every face corner of a Blender mesh."""
    import numpy as np
    mat_index = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", mat_index)
    totals = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("loop_total", totals)
    names = np.array([s.name if s else "" for s in me.materials])
    return names[np.repeat(mat_index, totals)]


def export_fbx(objects, path):
    """Export with the project's Unity conventions (see geo.to_object)."""
    for o in bpy.context.scene.objects:
        o.select_set(o.name in objects)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE",
                             colors_type="SRGB", prioritize_active_color=True, use_mesh_modifiers=True,
                             add_leaf_bones=False, path_mode="STRIP", embed_textures=False, use_custom_props=False)


def digest(objects):
    """sha256 over the exported mesh data (positions, topology, materials, colours, UV maps),
    plus a short digest per object. Unchanged digests prove a kit refactor kept the art."""
    import numpy as np
    total = hashlib.sha256()
    per_object = {}
    for name in sorted(objects):
        me = objects[name].data
        h = hashlib.sha256()
        for items, field, dtype, width in ((me.vertices, "co", np.float32, 3), (me.loops, "vertex_index", np.int32, 1),
                                           (me.polygons, "loop_start", np.int32, 1), (me.polygons, "material_index", np.int32, 1),
                                           (me.polygons, "use_smooth", bool, 1)):
            values = np.empty(len(items) * width, dtype=dtype)
            items.foreach_get(field, values)
            h.update(values.tobytes())
        h.update("|".join(m.name if m else "" for m in me.materials).encode())
        for attr in sorted(me.color_attributes, key=lambda a: a.name):
            values = np.empty(len(attr.data) * 4, dtype=np.float32)
            attr.data.foreach_get("color_srgb", values)
            h.update(attr.name.encode())
            h.update(values.tobytes())
        for layer in me.uv_layers:
            values = np.empty(len(layer.data) * 2, dtype=np.float32)
            layer.data.foreach_get("uv", values)
            h.update(layer.name.encode())
            h.update(values.tobytes())
        per_object[name] = h.hexdigest()[:16]
        total.update(name.encode())
        total.update(h.digest())
    return total.hexdigest(), per_object
