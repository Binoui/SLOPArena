"""Build both Nyx skins on the fake Mixamo skeleton. Run with Blender, see README.md."""
import bpy
import bmesh
import json
import sys
import hashlib
from pathlib import Path
from collections import Counter
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import islands, subset, select, material
from real_lower_body import replace_lower_body

P = 'mixamorig:'
ROOT = Path(__file__).resolve().parents[3]
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = Path(args[0]).resolve() if args else ROOT / 'tmp/nyx-model/cloth-rebuild-v2'
OUT.mkdir(parents=True, exist_ok=True)
(OUT / 'textures').mkdir(exist_ok=True)
SOURCES = ROOT / 'tmp/nyx-model/mixamo-rig-v1'
UPLOAD = ROOT / 'tmp/nyx-model/mixamo-upload-v1'
report = {'sources': {}, 'joint_displacement_metres': [], 'weight_fixes': {}, 'limitations': []}
bpy.ops.wm.read_factory_settings(use_empty=True)


def load(form):
    path = SOURCES / f'nyx-{form}-mixamo-rig.fbx'
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_image_search=False)
    obs = set(bpy.context.scene.objects) - before
    arm = next(o for o in obs if o.type == 'ARMATURE')
    mesh = next(o for o in obs if o.type == 'MESH')
    take = arm.animation_data.action
    bpy.context.scene.frame_set(int(take.frame_range[0]))
    bpy.context.view_layer.update()
    tpose = {b.name: Matrix.LocRotScale((arm.matrix_world @ b.matrix).translation,
              (arm.matrix_world @ b.matrix).to_quaternion(), Vector((1, 1, 1))) for b in arm.pose.bones}
    arm.animation_data_clear()
    for b in arm.pose.bones:
        b.matrix_basis.identity()
    bpy.context.view_layer.update()
    joints = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    world = mesh.matrix_world.copy()
    mesh.parent = None
    mesh.matrix_world = Matrix.Identity(4)
    for v in mesh.data.vertices:
        v.co = world @ v.co
    mesh.modifiers.clear()
    select([arm])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    report['sources'][form] = {'path': str(path.relative_to(ROOT)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                               'vertices': len(mesh.data.vertices), 'bones': len(arm.data.bones)}
    return arm, mesh, joints, tpose


def norm(w):
    w = dict(sorted(((k, v) for k, v in w.items() if v > 1e-6), key=lambda p: -p[1])[:4])
    total = sum(w.values())
    assert total > 0
    return {k: v / total for k, v in w.items()}


def read_weights(mesh):
    names = {g.index: g.name for g in mesh.vertex_groups}
    return [norm({names[g.group]: g.weight for g in v.groups}) for v in mesh.data.vertices]


def set_weights(mesh, weights):
    mesh.vertex_groups.clear()
    groups = {n: mesh.vertex_groups.new(name=n) for n in sorted({n for w in weights for n in w})}
    for i, w in enumerate(weights):
        for n, v in norm(w).items():
            groups[n].add([i], v, 'REPLACE')


def smooth(a, b, v):
    t = min(1, max(0, (v-a)/(b-a)))
    return t*t*(3-2*t)


fake_arm, fake, fj, tpose = load('fake')
real_arm, real, rj, _ = load('real')
assert len(fake.data.vertices) == 23681 and len(real.data.vertices) == 25702
assert set(fj) == set(rj) and len(fj) == 65
for n in fj:
    delta = fj[n] - rj[n]
    report['joint_displacement_metres'].append({'bone': n, 'real_source': list(rj[n]), 'fake_target': list(fj[n]),
                                               'delta_xyz': list(delta), 'distance': delta.length})
original_real = np.array([list(v.co) for v in real.data.vertices])
source_attribute = real.data.attributes.new('nyx_source_position', 'FLOAT_VECTOR', 'POINT')
for vertex, entry in zip(real.data.vertices, source_attribute.data):
    entry.vector = vertex.co

# These are guarded edge-connected islands from the two supplied Mixamo downloads.
HAIR = {'fake': {1, 2}, 'real': {1, 6, 10, 22, 30, 31, 135, 148}}
DEFORM = {'fake': {0, 3, 4, 5, 6, 7, 8, 9}, 'real': {0, 3, 4, 9, 12, 15, 16, 18, 20, 24, 26}}
all_meshes = []
real_dress = None
for form, mesh in [('fake', fake), ('real', real)]:
    gs = islands(mesh)
    assert len(gs) == (235 if form == 'fake' else 246), (form, len(gs))
    weights = read_weights(mesh)
    fixes = {'hair_vertices': 0, 'jewelry_islands': [], 'trimmed_to_four': True}
    hair_ids = set()
    for i in HAIR[form]:
        assert max(mesh.data.vertices[v].co.z for v in gs[i][1]) > 1.58
        hair_ids.update(gs[i][1])
    for vi in hair_ids:
        weights[vi] = {P+'Head': 1.0}
    fixes['hair_vertices'] = len(hair_ids)
    for i, (_, vs) in enumerate(gs):
        if i in HAIR[form] or i in DEFORM[form]:
            continue
        avg = Counter()
        c = sum((mesh.data.vertices[v].co for v in vs), Vector()) / len(vs)
        for vi in vs:
            for n, w in weights[vi].items():
                avg[n] += w
        bone = avg.most_common(1)[0][0]
        if c.z > 1.48:
            bone = P+'Head'
        elif abs(c.x) < .18 and c.z > .95:
            bone = P+('Neck' if c.z > 1.4 else 'Spine2' if c.z > 1.20 else 'Spine' if c.z > 1.06 else 'Hips')
        for vi in vs:
            weights[vi] = {bone: 1.0}
        fixes['jewelry_islands'].append({'island': i, 'vertices': len(vs), 'bone': bone})
    # Keep the head rigid, and prevent skin on the torso inheriting the nearby hands.
    torso_cleaned = 0
    for vi in gs[0][1]:
        p = mesh.data.vertices[vi].co
        if p.z > 1.48 and abs(p.x) < .17:
            weights[vi] = {P+'Head': 1.0}
        elif abs(p.x) < .16 and p.z < 1.40:
            w = {n: v for n, v in weights[vi].items() if not any(s in n for s in ['Arm', 'Hand', 'Leg', 'Foot'])}
            weights[vi] = norm(w or {P+'Spine2': 1})
            torso_cleaned += 1
    fixes['torso_vertices_cleared_of_limbs'] = torso_cleaned
    if form == 'real':
        # Translational fitting preserves the face and torso. No head or torso rotation is applied.
        protected = set(hair_ids)
        fitted_once = set()
        for i, (_, vs) in enumerate(gs):
            for vi in vs:
                if vi in fitted_once:
                    continue
                fitted_once.add(vi)
                v = mesh.data.vertices[vi]
                p = v.co.copy()
                is_leg = i in {4, 15, 16, 18, 20, 24, 26} or p.z < .43
                is_arm = abs(p.x) > .23 and .65 < p.z < 1.45 and i != 3
                if is_leg or is_arm:
                    d = Vector()
                    for n, w in weights[vi].items():
                        if n in fj:
                            d += (fj[n]-rj[n])*w
                    if is_leg:
                        d.z *= smooth(0, .13, p.z)
                    elif p.z > 1.12:
                        d *= 1-smooth(1.12, 1.35, p.z)
                    v.co += d
                else:
                    protected.add(vi)
        fitted = np.array([list(v.co) for v in mesh.data.vertices])
        displacement = np.linalg.norm(fitted-original_real, axis=1)
        report['fit'] = {'method': 'weighted joint translations on limbs; protected face, hair, torso and skirt; sole Z pinned',
                         'head_rotation_degrees': 0, 'protected_vertex_count': len(protected),
                         'protected_max_displacement_m': float(displacement[list(protected)].max()),
                         'max_displacement_m': float(displacement.max()), 'mean_displacement_m': float(displacement.mean())}
        np.savez_compressed(OUT/'fit-coordinates.npz', before=original_real, after=fitted)
        # The continuous dress cannot be skinned independently to both legs without a crotch pinch.
        # Hips owns its rest shape; the importer adds presentation-only Unity Cloth and leg colliders.
        for vi in gs[3][1]:
            weights[vi] = {P+'Hips': 1.0}
        fixes['dress'] = {'island': 3, 'vertices': len(gs[3][1]), 'skin': P+'Hips', 'leg_influences': 0,
                          'runtime': 'separate renderer with Unity Cloth, waist pins and leg colliders'}
        set_weights(mesh, weights)
        real_dress = subset(mesh, gs[3][0], 'Nyx_Real_Dress')
        body_faces = [fi for i, (fs, _) in enumerate(gs) if i != 3 for fi in fs]
        body = subset(mesh, body_faces, 'Nyx_Real')
        bpy.data.objects.remove(mesh, do_unlink=True)
        real = body
        real.name = real.data.name = 'Nyx_Real'
        all_meshes.extend([body, real_dress])
    else:
        set_weights(mesh, weights)
        mesh.name = mesh.data.name = 'Nyx_Fake'
        all_meshes.append(mesh)
    report['weight_fixes'][form] = fixes
bpy.data.objects.remove(real_arm, do_unlink=True)
arm = fake_arm
arm.name = 'Armature'

before = set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(UPLOAD/'nyx-fake-backpack.fbx'), use_image_search=False)
backpack = next(o for o in set(bpy.context.scene.objects)-before if o.type == 'MESH')
world = backpack.matrix_world.copy()
backpack.parent = None
backpack.matrix_world = Matrix.Identity(4)
for v in backpack.data.vertices:
    v.co = world @ v.co
backpack.name = backpack.data.name = 'Nyx_Backpack'
pg = islands(backpack)
# The pole and ring are the connected rear islands extending above the bag, plus their collars.
pole_ids = [1, 2, 13, 23]
assert [len(pg[i][1]) for i in pole_ids] == [193, 208, 49, 24], 'Inspect changed prop topology'
assert pole_ids, 'No pole islands above bag'
pole = subset(backpack, [fi for i in pole_ids for fi in pg[i][0]], 'Nyx_PoleHeld')
report['props'] = {'backpack_vertices': len(backpack.data.vertices), 'pole_islands': pole_ids, 'pole_vertices': len(pole.data.vertices),
                   'phone': 'No independent phone island found in supplied fake mesh. No phone extracted or fabricated.'}
report['limitations'].append('The supplied fake mesh has open fingers and no separable phone. Phone presentation needs an asset.')

select([arm]); bpy.ops.object.mode_set(mode='EDIT')
for name, parent, position in [('Nyx_Backpack', P+'Spine2', Vector((0, .13, 1.22))),
                               ('Nyx_PoleHeld', P+'LeftHand', fj[P+'LeftHand'])]:
    b = arm.data.edit_bones.new(name)
    b.head = position
    b.tail = position + Vector((0, 0, .12))
    b.parent = arm.data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
# Grip near the lower end, pole extending along the left forearm through the palm.
pole_points = [v.co for v in pole.data.vertices]
grip = Vector((sum(p.x for p in pole_points)/len(pole_points), sum(p.y for p in pole_points)/len(pole_points), 1.12))
for v in pole.data.vertices:
    v.co += fj[P+'LeftHand'] - grip
for mesh, bone in [(backpack, 'Nyx_Backpack'), (pole, 'Nyx_PoleHeld')]:
    set_weights(mesh, [{bone: 1.0} for _ in mesh.data.vertices])
    all_meshes.append(mesh)

for form, meshes in [('Fake', [fake, backpack, pole]), ('Real', [real, real_dress])]:
    src = UPLOAD/'textures'/f'nyx-{form.lower()}-basecolor.png'
    dst = OUT/'textures'/f'Nyx_{form}.png'
    img = bpy.data.images.load(str(src), check_existing=False)
    img.scale(2048, 2048)
    img.filepath_raw = str(dst)
    img.file_format = 'PNG'
    img.save()
    for mesh in meshes:
        material(mesh, dst, f'Nyx_{form}')

# Solve skin coordinates for the chosen T rest, then prove the fitted A pose round-trips.
rest_a = {b.name: b.matrix_local.copy() for b in arm.data.bones}
for b in sorted(arm.pose.bones, key=lambda b: len(b.parent_recursive)):
    if b.name in tpose:
        b.matrix = tpose[b.name]
        bpy.context.view_layer.update()
bpy.context.view_layer.update()
posed_t = {b.name: b.matrix.copy() for b in arm.pose.bones}
transforms = {n: np.array(rest_a[n] @ posed_t[n].inverted()) for n in rest_a}
expected = {}
for mesh in all_meshes:
    expected[mesh.name] = np.array([list(v.co) for v in mesh.data.vertices])
    weights = read_weights(mesh)
    for v, w in zip(mesh.data.vertices, weights):
        m = sum(weight*transforms[n] for n, weight in w.items())
        q = np.linalg.solve(m, np.array([*v.co, 1.0]))
        v.co = q[:3]/q[3]
    mesh.data.update()
select([arm]); bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode='OBJECT')
for mesh in all_meshes:
    mesh.modifiers.new('Armature', 'ARMATURE').object = arm
for b in sorted(arm.pose.bones, key=lambda b: len(b.parent_recursive)):
    b.matrix = rest_a[b.name]
    bpy.context.view_layer.update()
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
roundtrip = {}
for mesh in all_meshes:
    ev = mesh.evaluated_get(dg)
    coords = np.array([list(v.co) for v in ev.data.vertices])
    roundtrip[mesh.name] = float(np.linalg.norm(coords-expected[mesh.name], axis=1).max())
assert max(roundtrip.values()) < 2e-5, roundtrip
report['a_pose_roundtrip_max_error_m'] = roundtrip
np.savez_compressed(OUT/'a-pose.npz', **{n: np.array(m) for n,m in rest_a.items()})
for b in arm.pose.bones:
    b.matrix_basis.identity()
bpy.context.view_layer.update()
for mesh in all_meshes:
    mesh.data.name = mesh.name
    mesh.parent = arm
    mesh.matrix_parent_inverse = arm.matrix_world.inverted()
all_meshes.remove(real)
all_meshes.remove(real_dress)
real, tights, real_dress, report['real_lower_body'] = replace_lower_body(
    real, real_dress, fake, arm, OUT, rj, fj)
all_meshes.extend([real, tights, real_dress])
for mesh in [real, tights, real_dress]:
    mesh.parent = arm
    mesh.matrix_parent_inverse = arm.matrix_world.inverted()
    mesh.data.name = mesh.name
for mesh in all_meshes:
    attribute = mesh.data.attributes.get('nyx_source_position')
    if attribute:
        mesh.data.attributes.remove(attribute)
report['bones'] = len(arm.data.bones)
report['meshes'] = {o.name: {'vertices': len(o.data.vertices), 'faces': len(o.data.polygons)} for o in all_meshes}
report['textures'] = {'Nyx_Fake.png': [2048,2048], 'Nyx_Real.png': [2048,2048], 'Nyx_Dress.png': [1024,1024], 'Nyx_Tights.png': [256,256]}
select([arm]+all_meshes)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Nyx.fbx'), use_selection=True, object_types={'ARMATURE','MESH'},
    add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='OFF',
    use_armature_deform_only=False, armature_nodetype='NULL')
bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
for img in bpy.data.images:
    if img.users and img.filepath and Path(bpy.path.abspath(img.filepath)).is_file():
        img.pack()
# The blend opens on the real form. Export has every mesh; Unity controls defaults in the prefab.
fake.hide_render = True
backpack.hide_render = True
pole.hide_render = True
fake.hide_set(True); backpack.hide_set(True); pole.hide_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Nyx_rig.blend'))
report['fbx_sha256'] = hashlib.sha256((OUT/'Nyx.fbx').read_bytes()).hexdigest()
(OUT/'Nyx_report.json').write_text(json.dumps(report, indent=2))
print('NYX_BUILD_OK', json.dumps({'bones': report['bones'], 'meshes': report['meshes'], 'roundtrip': roundtrip}))
