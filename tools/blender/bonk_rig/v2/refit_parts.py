"""Re-attach Sir Bonk v2's rigid armour to helper bones and remove the body hidden under his pants.

blender -b --factory-startup --python-exit-code 1 --python refit_parts.py -- <sir_bonk_v2.fbx> <out_dir> [name=SirBonk]

Works on the shipped FBX (build_rig.py's output), because the Tripo kit and the Mixamo rig it was built from are
gone. The skeleton, rest pose, mesh name and material name are unchanged, so the Unity Avatar and every clip stay
valid; `refit_check.py` proves the rest pose against the input.

build_rig.py skinned each rigid part with one fixed blend of two bones. A blend of two rotations is not a rotation:
the part shrinks toward the joint and tilts as the joint bends (knee guards to 58-70% in a bent knee, the colander
to 77%), and the pot lid, 85% upper arm, slid onto the biceps with the arms down. Here every such part rides one
helper bone instead, so it stays rigid:

    KneeGuard_Left/Right   child of UpLeg at the knee,       follows Leg 50%      (knee guards)
    ElbowCap_Right         child of Arm at the elbow,         follows ForeArm 50%  (colander)
    Pauldron_Right         child of Shoulder at the shoulder, follows Arm 30%      (right pot lid)
    Pauldron_Left          the same on the left; no skin: Q's lid prop attaches to it (WeaponAttach)

A helper has its driver's rest frame (same head, tail and roll), so at rest it is the driver. In Unity a
RotationConstraint on the helper (source = the driver, weight = the share above) turns it; without the constraint it
stays rigid on its parent. `v2_refit_import.eval.cs` adds the constraints.

Also:
  - the bare legs (two islands of thigh and knee skin) are deleted: they sit wholly inside the pants and showed
    through them at a bent knee;
  - boot cuffs, and the pants inside the boots, lose their thigh weight: the pants followed the thigh out through
    the cuff when the knee bent;
  - the pants end 4 cm inside the boots. They ran down to the ankle on the shin bone, and came out through the back
    of the boot shaft, which follows the foot, whenever the ankle bent.
Writes <out_dir>/<name>.fbx and <name>_refit_report.json.
"""
import bpy, bmesh, sys, os, json
from collections import defaultdict

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = os.path.abspath(argv[0]), os.path.abspath(argv[1])
NAME = argv[2] if len(argv) > 2 else "SirBonk"
os.makedirs(OUT, exist_ok=True)
P = "mixamorig:"
SIDES = ("Left", "Right")
# helper -> (parent, driver, share of the driver's rotation). The share is applied in Unity; it is recorded here.
HELPERS = {}
for side in SIDES:
    HELPERS[f"KneeGuard_{side}"] = (side + "UpLeg", side + "Leg", 0.5)
    HELPERS[f"Pauldron_{side}"] = (side + "Shoulder", side + "Arm", 0.3)
HELPERS["ElbowCap_Right"] = ("RightArm", "RightForeArm", 0.5)
report = {"source": SRC, "helpers": {h: {"parent": a, "driver": b, "share": t} for h, (a, b, t) in HELPERS.items()}}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
me = body.data
report["input"] = {"verts": len(me.vertices), "faces": len(me.polygons), "bones": len(arm.data.bones),
                   "mesh": body.name, "materials": [m.name for m in me.materials]}

# --- islands -----------------------------------------------------------------------------------------
gn = {g.index: g.name for g in body.vertex_groups}


def vw(k):
    w = {gn[g.group]: g.weight for g in me.vertices[k].groups if g.weight > 0.01}
    s = sum(w.values()) or 1.0
    return {n.replace(P, ""): x / s for n, x in w.items()}


bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
seen, islands = set(), []
for v in bm.verts:
    if v.index in seen: continue
    comp, st = [], [v.index]; seen.add(v.index)
    while st:
        a = st.pop(); comp.append(a)
        for e in bm.verts[a].link_edges:
            b = e.other_vert(bm.verts[a]).index
            if b not in seen: seen.add(b); st.append(b)
    islands.append(comp)
bm.free()
W = [vw(k) for k in range(len(me.vertices))]
Z = [(body.matrix_world @ v.co).z for v in me.vertices]

BOOT_TOP = 0.168  # metres in the file: the boot cuffs' rim
PANTS_TUCK = 0.04  # the pants keep this much inside the boot; the rest of the leg is cut off
assign, counts = {}, defaultdict(int)   # vertex -> helper
bare_legs, cuff_verts, tucked = [], [], []
for comp in islands:
    mean = defaultdict(float)
    for k in comp:
        for n, x in W[k].items(): mean[n] += x / len(comp)
    names = {n for n, x in mean.items() if x > 0.02}
    uniform = len({tuple(sorted((n, round(x, 2)) for n, x in W[k].items())) for k in comp}) == 1
    for side in SIDES:
        up, leg, foot, toe = side + "UpLeg", side + "Leg", side + "Foot", side + "ToeBase"
        helper = None
        if uniform and names == {up, leg}: helper = f"KneeGuard_{side}"
        if uniform and names == {side + "Arm", side + "ForeArm"}: helper = f"ElbowCap_{side}"
        if uniform and names == {side + "Arm", side + "Shoulder"}: helper = f"Pauldron_{side}"
        if helper:
            assert helper in HELPERS, f"a rigid part needs {helper}, which this script does not make"
            for k in comp: assign[k] = helper
            counts[helper] += len(comp)
        # A bare leg: thigh and knee skin only (trimmed above the boot), with per-vertex weights. The pants over it
        # also carry foot weight, because they run down into the boot.
        zs = [Z[k] for k in comp]
        if (not uniform and names == {up, leg} and mean[up] > 0.5 and 200 <= len(comp) <= 350
                and min(zs) > 0.12 and max(zs) > 0.27):
            bare_legs.append(comp)
        # A boot (mostly foot, with its toe) and a pants leg (shin and thigh, running down into the boot).
        if not uniform and mean[foot] > 0.4 and toe in names and len(comp) > 400:
            cuff_verts += [k for k in comp if up in W[k]]
        if not uniform and names == {up, leg, foot} and mean[leg] > 0.5 and 100 <= len(comp) <= 250:
            cuff_verts += [k for k in comp if up in W[k] and Z[k] < BOOT_TOP + 0.02]
            tucked += [k for k in comp if Z[k] < BOOT_TOP - PANTS_TUCK]
assert set(counts) == {"KneeGuard_Left", "KneeGuard_Right", "ElbowCap_Right", "Pauldron_Right"}, dict(counts)
assert len(bare_legs) == 2, f"expected the two bare legs, found {len(bare_legs)} islands"
report["rigid_verts"] = dict(counts)
report["bare_leg_verts"] = [len(c) for c in bare_legs]
report["tucked_pants_verts_cut"] = len(tucked)
report["cuff_and_tucked_pants_verts_off_the_thigh"] = len(cuff_verts)

# --- helper bones: the driver's rest frame under the driver's parent -------------------------------------
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for h, (a, b, t) in HELPERS.items():
    src = eb[P + b]
    nb = eb.new(h)
    nb.head, nb.tail, nb.roll = src.head.copy(), src.tail.copy(), src.roll
    nb.parent = eb[P + a]; nb.use_connect = False; nb.use_deform = True
bpy.ops.object.mode_set(mode="OBJECT")
for h, (a, b, t) in HELPERS.items():
    d = (arm.data.bones[h].matrix_local.inverted() @ arm.data.bones[P + b].matrix_local)
    assert max(abs(d[i][j] - (1.0 if i == j else 0.0)) for i in range(4) for j in range(4)) < 1e-5, f"{h} rest frame differs from {b}"


# --- weights -----------------------------------------------------------------------------------------
def set_w(k, w):
    for g in list(me.vertices[k].groups): body.vertex_groups[g.group].remove([k])
    for n, x in w.items():
        (body.vertex_groups.get(n) or body.vertex_groups.new(name=n)).add([k], x, "REPLACE")


for h in HELPERS:  # every helper gets a group, so the FBX lists it as a skin bone (WeaponAttach looks bones up there)
    if body.vertex_groups.get(h) is None: body.vertex_groups.new(name=h)
for k, h in assign.items(): set_w(k, {h: 1.0})
for k in cuff_verts:  # no thigh weight at the boot top and inside the boot; back to the full share 2 cm above it
    w = {P + n: x for n, x in W[k].items()}
    side = "Left" if any("Left" in n for n in w) else "Right"
    t = min(1.0, max(0.0, (Z[k] - BOOT_TOP) / 0.02)); keep = t * t * (3 - 2 * t)
    moved = w[P + side + "UpLeg"] * (1 - keep)
    w[P + side + "UpLeg"] -= moved
    w[P + side + "Leg"] = w.get(P + side + "Leg", 0.0) + moved
    set_w(k, {n: x for n, x in w.items() if x > 1e-4})

# --- delete the bare legs and the pants deep inside the boots ------------------------------------------------
doomed = {k for comp in bare_legs for k in comp} | set(tucked)
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[k] for k in doomed], context="VERTS")
bm.to_mesh(me); bm.free(); me.update()
report["output"] = {"verts": len(me.vertices), "faces": len(me.polygons), "bones": len(arm.data.bones)}
report["vertex_groups_without_bone"] = [g.name for g in body.vertex_groups if g.name not in arm.data.bones]

# --- export, with build_rig.py's settings -------------------------------------------------------------------
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True); body.select_set(True); bpy.context.view_layer.objects.active = arm
fbx = os.path.join(OUT, f"{NAME}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="OFF", use_armature_deform_only=False, armature_nodetype="NULL")
report["fbx"] = fbx
json.dump(report, open(os.path.join(OUT, f"{NAME}_refit_report.json"), "w"), indent=1)
print("REPORT", json.dumps(report))
