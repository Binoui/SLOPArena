"""Build the Splash Deck kraken hazard: kraken.fbx (mesh, rig, every clip) and kraken.json.

    blender -b --factory-startup --python-exit-code 1 -P tools/blender/splash_deck/kraken_build.py -- \\
        [--out tmp/stage-hazards/kraken-build] [--no-bake] [--preview]

It writes only to --out (never into client/Unity): kraken.fbx, kraken.json (what the presenter
reads), kraken-report.json (counts, checks, timings) and, with --preview, Eevee key-frame renders
under preview/ plus contact-sheet.jpg. The FBX holds one take; kraken.json lists every clip's frames in it.
Every run re-imports the FBX and checks its bones, the take, the vertex colours and how far the arm tips
drift from the exact poses. An unchanged build leaves kraken.fbx byte-identical (stagekit's exporter).
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "../../.."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
sys.dont_write_bytecode = True

import bpy  # noqa: E402
import numpy as np  # noqa: E402

from stagekit import blender as kit  # noqa: E402
import bay as BAY  # noqa: E402
import kraken as K  # noqa: E402
import kraken_clips as KC  # noqa: E402
import layout as LAYOUT  # noqa: E402
from kraken_clips import apply_frame  # noqa: E402

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGV[ARGV.index("--out") + 1]) if "--out" in ARGV else os.path.join(REPO, "tmp/stage-hazards/kraken-build")
BAKE = "--no-bake" not in ARGV
PREVIEW = "--preview" in ARGV
# Every clip goes on ONE timeline, back to back, exported as a single take; kraken.json lists each clip's
# first and last frame and the importer splits the take there. 32 takes cost ~14 MB of per-take curve
# headers and connections alone (266 bones x 3 rotation curves each), whatever their keys.
# Every frame is sampled (a key every second frame let Unity's per-frame resampling interpolate Euler angles
# across a wrap between keys: tips jumped up to 54 m on the in-between frames). reduce_keys() then drops
# every key that linear interpolation (what FBX, Blender and Unity use here) rebuilds within a tolerance.
# A bone's rotation error moves everything below it in the chain, so its tolerance is KEY_TOLERANCE_M metres
# at the end of the arm it carries. The re-import measures the tip error that is left (report "keyError").
KEY_STEP = 1.0
KEY_TOLERANCE_M = float(ARGV[ARGV.index("--key-tolerance") + 1]) if "--key-tolerance" in ARGV else 0.005
MAX_FILE_BYTES = 12 * 1024 * 1024   # the repo has no LFS


def log(*a):
    print("KRAKEN", *a, flush=True)


# ---------------------------------------------------------------- keys

def key_timeline(rig, laid):
    """All clips on one action, back to back from frame 0, every bone keyed every frame. Returns
    {clip: (first, last)} frames; the scene range covers them all (the exporter's single take)."""
    rig.animation_data_create()
    names = [b.name for b in rig.data.bones]
    arm_bones = [n for n in names if n not in ("Root", "Head")]
    takes, frames = {}, []
    for clip, clip_frames in laid.items():
        takes[clip] = (len(frames), len(frames) + len(clip_frames) - 1)
        frames.extend(clip_frames)
    act = bpy.data.actions.new("kraken")
    rig.animation_data.action = act
    n = len(frames)
    fr = np.arange(n, dtype=np.float64)

    def put(path, index, values):
        fc = act.fcurve_ensure_for_datablock(rig, path, index=index, group_name=path.split('"')[1])
        fc.keyframe_points.add(n)
        fc.keyframe_points.foreach_set("co", np.stack([fr, values], 1).ravel())
        fc.keyframe_points.foreach_set("interpolation", [1] * n)   # LINEAR
        fc.update()

    for bone in ["Root"] + arm_bones:
        quats = np.array([tuple(f[bone].to_quaternion()) for f in frames])
        for i in range(1, n):   # keep the hemisphere so the curves do not flip
            if quats[i] @ quats[i - 1] < 0:
                quats[i] = -quats[i]
        for c in range(4):
            put('pose.bones["%s"].rotation_quaternion' % bone, c, quats[:, c])
        if bone == "Root":
            locs = np.array([tuple(f[bone].translation) for f in frames])
            for c in range(3):
                put('pose.bones["%s"].location' % bone, c, locs[:, c])
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 0, n - 1
    return takes


def reduce_mask(values, tol, spans):
    """Keys to keep on one sampled curve so linear interpolation stays within tol inside every span
    (first, last) of samples; both ends of every span are kept, so no clip interpolates into its neighbour."""
    keep = np.zeros(len(values), dtype=bool)
    for first, last in spans:
        keep[first] = keep[last] = True
        i = first
        while i < last:
            j = i + 2
            while j <= last:
                seg = values[i:j + 1]
                if np.abs(seg - np.linspace(seg[0], seg[-1], len(seg))).max() > tol:
                    break
                j += 1
            i = j - 1
            keep[i] = True
    return keep


def key_tolerances(arms):
    """Per bone: (rotation tolerance in degrees, translation tolerance in metres)."""
    import math
    tol = {}
    L = arms[0].length_m
    for arm in arms:
        l = arm.length_m / arm.n
        for k in range(arm.n):
            carried = (arm.n - k) * l
            tol[arm.bone(k)] = (min(0.5, max(0.01, math.degrees(KEY_TOLERANCE_M / carried))), None)
    tol["Root"] = (math.degrees(KEY_TOLERANCE_M / (L + 10.0)), 0.004)
    tol["Head"] = (math.degrees(KEY_TOLERANCE_M / 25.0), None)
    return tol


def keep_only_moving_curves(tolerances=None, spans=None):
    """The FBX exporter's per-strip takes keep every channel of every bone, even ones that never change
    (force_keep). For the kraken that meant ~52k constant translation and scale curves and a 56 MB file.
    Let translation and scale curves that never change drop out, except the Root's translation. Every
    component of every rotation, and of the Root's translation, is written in every take, even when it is
    constant there: the exporter used to drop a constant component (the lying arm's Root height, a spare
    arm's steady bend), and a player then fills it with the rest pose or the previous clip's value.
    With tolerances, reduce_keys() then thins the kept curves. Patches this Blender process only."""
    from io_scene_fbx import fbx_utils
    import re
    wrapper = fbx_utils.AnimationCurveNodeWrapper
    wrapper.kraken_tolerances = tolerances
    wrapper.kraken_spans = spans
    if getattr(wrapper, "kraken_patched", False):
        return
    simplify, final = wrapper.simplify, wrapper.get_final_data
    bone_re = re.compile(r"(Arm\d+_\d+|Root|Head)")

    def bone(node):
        found = bone_re.findall(str(node.elem_keys[0]))
        return found[-1] if found else None

    def every_component(node):
        return node.fbx_gname[0] == "R" or (node.fbx_gname[0] == "T" and bone(node) == "Root")

    def reduce_keys(self):
        """Drop keys that linear interpolation rebuilds within the bone's tolerance (see KEY_TOLERANCE_M)."""
        tol = (wrapper.kraken_tolerances or {}).get(bone(self))
        if tol is None or not wrapper.kraken_spans:
            return
        t = tol[0] if self.fbx_gname[0] == "R" else tol[1] if self.fbx_gname[0] == "T" else None
        if t is None:
            return
        for values, mask in zip(self._frame_values_array, self._frame_write_mask_array):
            if mask.any() and len(values) > 2:
                mask[:] = reduce_mask(values, t, wrapper.kraken_spans)

    def simplified(self, fac, step, force_keep=False):
        keep = force_keep or every_component(self)
        simplify(self, fac, step, keep)
        if self._frame_values_array is None:
            return
        if every_component(self):
            self._frame_write_mask_array[:, 0] = True
            self._frame_write_mask_array[:, -1] = True
            for first, last in wrapper.kraken_spans or ():
                self._frame_write_mask_array[:, first] = True
                self._frame_write_mask_array[:, last] = True
        reduce_keys(self)

    wrapper.simplify = simplified
    wrapper.get_final_data = lambda self, scene, ref_id, force_keep=False: final(
        self, scene, ref_id, force_keep or every_component(self))
    wrapper.kraken_patched = True


def export(rig, mesh, path, takes, tolerances=None):
    for o in bpy.context.scene.objects:
        o.select_set(o in (rig, mesh))
    bpy.context.view_layer.objects.active = rig
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    kit._deterministic_fbx_exporter()
    keep_only_moving_curves(tolerances, sorted(takes.values()))
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, axis_forward="-Z",
                             axis_up="Y", apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", colors_type="SRGB",
                             prioritize_active_color=True, add_leaf_bones=False, primary_bone_axis="Y",
                             secondary_bone_axis="X", use_armature_deform_only=False, bake_anim=True,
                             bake_anim_use_all_bones=False, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                             bake_anim_force_startend_keying=True, bake_anim_step=KEY_STEP, bake_anim_simplify_factor=1.0,
                             path_mode="STRIP", embed_textures=False, use_custom_props=False)


# ---------------------------------------------------------------- checks on the posed mesh

def posed_verts(rig, mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(-1, 3)   # armature frame (the mesh is parented at identity)


def unity_of(spot, co):
    """Armature-frame vertices -> Unity stage coordinates around the spot."""
    F = np.array(spot.F)
    b = co @ F[:3, :3].T + F[:3, 3]
    return np.stack([-b[:, 0], b[:, 2], -b[:, 1]], 1)


def checks(kr, rig, mesh, laid, ranges):
    out = {"warnMaxHeight": {}, "slamCover": {}}
    hero = ranges[kr.hero.name]
    arms = [ranges[a.name] for a in kr.arms]
    for name, spot in kr.spots.items():
        if name == "jab":
            continue
        worst_hero, worst_any = -99.0, -99.0
        frames = laid[name + ".warn"]
        for f in frames[::3] + [laid[name + ".rise"][-1]]:
            apply_frame(rig, f)
            u = unity_of(spot, posed_verts(rig, mesh))
            worst_hero = max(worst_hero, float(u[hero[0]:hero[1], 1].max()))
            worst_any = max(worst_any, max(float(u[a:b, 1].max()) for a, b in arms))
        out["warnMaxHeight"][name] = {"heroArm": round(worst_hero, 2), "anyArm": round(worst_any, 2),
                                      "ok": worst_any <= KC.WARN_MAX_HEIGHT + 0.05}
        apply_frame(rig, laid[name + ".lie"][0])
        u = unity_of(spot, posed_verts(rig, mesh))[hero[0]:hero[1]]
        on_deck = u[(np.abs(u[:, 2]) < spot.hb - 0.5) & (u[:, 1] > -0.5)]
        lo = float(on_deck[:, 1].min()) if len(on_deck) else None
        out["slamCover"][name] = {
            "lane": spot.lane, "laneX": spot.lx,
            "armX": [round(float(on_deck[:, 0].min()), 2), round(float(on_deck[:, 0].max()), 2)],
            "armZ": [round(float(u[u[:, 1] > -0.5][:, 2].min()), 2), round(float(u[u[:, 1] > -0.5][:, 2].max()), 2)],
            "deckHalfBeam": round(spot.hb, 2), "lowestOnDeck": round(lo, 2), "topOnDeck": round(float(on_deck[:, 1].max()), 2)}
    return out


# ---------------------------------------------------------------- kraken.json

CHECKPOINTS = (("lurk", 0), ("port.mid.warn", 24), ("port.mid.slam", 8), ("port.mid.lie", 0), ("starboard.fore.warn", 12))


def checkpoints(rig, laid, names):
    """A few exact bone positions (kraken object space, Unity axes) an importer can compare its playback with."""
    out = []
    for clip, frame in CHECKPOINTS:
        apply_frame(rig, laid[clip][frame])
        heads = {n: rig.matrix_world @ rig.pose.bones[n].head for n in ["Root"] + names}
        out.append({"clip": clip, "frame": frame,
                    "bones": {n: [round(float(v), 3) for v in KC.U(p)] for n, p in heads.items()}})
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    return out


def document(kr, L, info, fbx_name, takes, points):
    variants = {}
    for name, spot in kr.spots.items():
        if name == "jab":
            continue
        root = spot.anchor + np.array((0.0, KC.LIFT, 0.0))
        variants[name] = {
            "side": name.split(".")[0], "lane": spot.lane,
            "anchor": {"position": [round(float(v), 4) for v in spot.anchor], "yaw": round(spot.yaw, 3)},
            "surfacedRoot": [round(float(v), 4) for v in root],
            "clips": {p: "%s.%s" % (name, p) for p in KC.PHASES},
        }
    jab = kr.spots["jab"]
    return {
        "version": 1,
        "fbx": fbx_name,
        "fps": KC.FPS,
        "waterY": BAY.WATER_Y,
        "units": "metres; positions in Unity stage space; yaw in degrees about +Y, 0 = local +Z (the face) looking along stage +Z",
        "placement": ("Place the kraken object at a variant's anchor (on the water) with its yaw; the clips move the Root bone, "
                      "never the object. Lurk: move the anchor along the route and loop `lurk`. rise starts and withdraw "
                      "and jab end on lurk frame 0, so the cuts are seamless."),
        "rig": {"root": "Root", "head": "Head", "bones": info["bones"], "heroArm": kr.hero.name, "jabArm": kr.jabber.name,
                "armLength": round(L, 3), "bonesPerArm": kr.arms[0].n},
        "frames": dict(KC.FRAMES),
        "takes": {"note": "the FBX holds one take; split it into these clips by frame (first and last inclusive, 30 fps)",
                  "clips": {clip: [first, last] for clip, (first, last) in takes.items()}},
        "loops": list(KC.LOOPS),
        "lurk": {"clip": "lurk", "rootHeight": {"deep": round(kr.deep, 3), "peek": round(kr.peek, 3)},
                 "clearance": round(kr.ext["front"] + KC.HEAD_CLEAR, 3),
                 "note": "the object sits `clearance` metres outside the deck outline, as at the surfacing spots"},
        "deck": {"top": LAYOUT.SPEC["top"], "sternX": LAYOUT.SPEC["hull"]["stern_x"], "bowX": LAYOUT.SPEC["hull"]["bow_x"],
                 "halfBeam": [[round(float(x), 2), round(LAYOUT.half_beam(float(x)), 3)]
                              for x in np.arange(LAYOUT.SPEC["hull"]["stern_x"], LAYOUT.SPEC["hull"]["bow_x"] + 0.01, 0.5)]},
        "jab": {"clip": "jab", "anchor": {"position": [round(float(v), 4) for v in jab.anchor], "yaw": round(jab.yaw, 3)}},
        "variants": variants,
        "lanes": {lane["name"]: {"center": lane["center"], "halfExtents": lane["halfExtents"]} for lane in LAYOUT.slam_lanes()},
        "warnMaxHeight": KC.WARN_MAX_HEIGHT,
        "checkpoints": {"note": "bone heads (the Root and each arm's last bone) at these clip frames, metres in the kraken object's own "
                        "space with Unity axes; playback should land within a few centimetres", "points": points},
    }


# ---------------------------------------------------------------- key error (exact pose vs re-imported FBX)

ERROR_CLIPS = ("lurk", "port.mid.", "starboard.fore.", "jab")


def tip_bones(arms):
    return [a.bone(a.n - 1) for a in arms]


def tip_positions(rig, names):
    return np.array([tuple(rig.matrix_world @ rig.pose.bones[n].tail) for n in names] +
                    [tuple(rig.matrix_world @ rig.pose.bones["Root"].head)])


def reference_tips(rig, laid, names):
    """Exact arm tips and Root, every frame of the clips the error check covers."""
    out = {}
    for clip, frames in laid.items():
        if clip.startswith(ERROR_CLIPS):
            out[clip] = []
            for f in frames:
                apply_frame(rig, f)
                out[clip].append(tip_positions(rig, names))
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    return out


def key_error(reference, names, takes):
    """Play the re-imported take at every frame (between keys too) and measure the tips' distance."""
    scene = bpy.context.scene
    rig = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
    act = rig.animation_data.action
    start = int(round(act.frame_range[0]))
    worst, every = {}, []
    for clip, ref in reference.items():
        first = takes[clip][0]
        errs = []
        for i, want in enumerate(ref):
            scene.frame_set(start + first + i)
            got = tip_positions(rig, names)
            errs.append(float(np.linalg.norm(got - want, axis=1).max()))
        worst[clip] = round(max(errs), 3)
        every.extend(errs)
    every = np.array(every)
    return {"maxM": round(float(every.max()), 3), "p95M": round(float(np.percentile(every, 95)), 3),
            "meanM": round(float(every.mean()), 3), "frames": int(len(every)), "perClipMaxM": worst}


# ---------------------------------------------------------------- re-import

def reimport(path, takes, expect_bones, reference=None, names=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = KC.FPS
    bpy.ops.import_scene.fbx(filepath=path)
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    actions = list(bpy.data.actions)
    last = max(f for _, f in takes.values())
    span = (actions[0].frame_range[1] - actions[0].frame_range[0]) if actions else -1
    me = meshes[0].data if meshes else None
    col = me.color_attributes.get("Col") if me else None
    colours = None
    if col is not None:
        c = np.empty(len(col.data) * 4, dtype=np.float32)
        col.data.foreach_get("color_srgb", c)
        c = c.reshape(-1, 4)
        colours = {"domain": col.domain, "type": col.data_type, "distinct": int(len(np.unique(np.round(c[:, :3], 3), axis=0))),
                   "mean": [round(float(v), 3) for v in c[:, :3].mean(0)]}
    bones = len(arms[0].data.bones) if arms else 0
    ok = len(actions) == 1 and int(round(span)) == last
    keys = sum(len(fc.keyframe_points) for a in bpy.data.actions for layer in a.layers for strip in layer.strips
               for bag in strip.channelbags for fc in bag.fcurves)
    return {"armatures": len(arms), "meshes": len(meshes), "bones": bones, "bonesOk": bones == expect_bones,
            "takes": len(actions), "takeFrames": int(round(span)) + 1 if actions else 0, "takeOk": ok,
            "vertexColours": colours, "groups": len(meshes[0].vertex_groups) if meshes else 0,
            "triangles": sum(len(p.vertices) - 2 for p in me.polygons) if me else 0, "keys": keys,
            "keyError": key_error(reference, names, takes) if reference and ok else None}


def main():
    t0 = time.time()
    os.makedirs(OUT, exist_ok=True)
    kit.reset_scene([])
    bpy.context.scene.render.fps = KC.FPS   # the takes' time base (the factory default is 24)
    bpy.context.scene.render.fps_base = 1.0
    arms = K.make_arms()
    ext = K.head_extents()
    kr = KC.Kraken(arms, ext)
    L, need = kr.arm_length()
    log("arm length %.2f m, hero %s, jab %s" % (L, kr.hero.name, kr.jabber.name))
    rig, mesh, info = K.build(arms, BAKE)
    log("mesh", info["triangles"], "triangles", info["bones"], "bones")
    t1 = time.time()
    clips = kr.all_clips(log)
    R = KC.Rig(rig, arms)
    laid = {name: [R.lay(p) for p in frames] for name, frames in clips.items()}
    t2 = time.time()
    log("clips solved in %.1f s" % (t2 - t1))
    names = tip_bones(arms)
    reference = reference_tips(rig, laid, names)
    takes = key_timeline(rig, laid)
    fbx = os.path.join(OUT, "kraken.fbx")
    export(rig, mesh, fbx, takes, key_tolerances(arms) if KEY_TOLERANCE_M > 0 else None)
    t3 = time.time()
    log("exported in %.1f s" % (t3 - t2))
    rig.animation_data.action = None
    chk = checks(kr, rig, mesh, laid, info["ranges"])
    doc = document(kr, L, info, "kraken.fbx", takes, checkpoints(rig, laid, names))
    with open(os.path.join(OUT, "kraken.json"), "w") as fh:
        json.dump(doc, fh, indent=1)
        fh.write("\n")
    preview = None
    if PREVIEW:
        import kraken_preview as KP
        preview = KP.render(kr, rig, mesh, laid, OUT, log)
    with open(fbx, "rb") as fh:
        sha = hashlib.sha256(fh.read()).hexdigest()
    re = reimport(fbx, takes, info["bones"], reference, names)
    report = {
        "fbx": fbx, "fbxBytes": os.path.getsize(fbx), "fbxSha256": sha, "keyToleranceM": KEY_TOLERANCE_M,
        "triangles": info["triangles"], "vertices": info["vertices"], "bones": info["bones"], "clips": len(clips),
        "clipFrames": {k: len(v) - 1 for k, v in clips.items()}, "armLength": round(L, 3),
        "reachNeeded": {k: round(v, 2) for k, v in sorted(need.items())},
        "head": {k: round(v, 3) for k, v in ext.items()}, "checks": chk, "reimport": re, "preview": preview,
        "seconds": {"build": round(t1 - t0, 1), "clips": round(t2 - t1, 1), "export": round(t3 - t2, 1),
                    "total": round(time.time() - t0, 1)}, "aoBakeSeconds": info["aoBakeSeconds"],
    }
    with open(os.path.join(OUT, "kraken-report.json"), "w") as fh:
        json.dump(report, fh, indent=1)
        fh.write("\n")
    log("REPORT", json.dumps({k: report[k] for k in ("triangles", "bones", "clips", "fbxBytes", "fbxSha256", "seconds")}))
    log("CHECKS", json.dumps(chk))
    log("REIMPORT", json.dumps(re))
    bad = [n for n, c in chk["warnMaxHeight"].items() if not c["ok"]]
    if not re["takeOk"] or not re["bonesOk"] or re["vertexColours"] is None:
        raise SystemExit("re-import check failed: %s" % json.dumps(re))
    if report["fbxBytes"] > MAX_FILE_BYTES:
        raise SystemExit("kraken.fbx is %.1f MB, over the %.0f MB limit" % (report["fbxBytes"] / 2**20, MAX_FILE_BYTES / 2**20))
    if bad:
        log("WARNING: warn pose above %.1f m in %s" % (KC.WARN_MAX_HEIGHT, bad))


if __name__ == "__main__":
    main()
