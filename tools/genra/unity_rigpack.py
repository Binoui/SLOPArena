"""Build a genra rig pack from the game's own Unity data, verified against Unity per vertex.

    <genra python> tools/genra/unity_rigpack.py snapshot manki      # Unity, own gateway lease, read-only
    <genra python> tools/genra/unity_rigpack.py build manki         # USD rig pack + validation
    <genra python> tools/genra/unity_rigpack.py all manki fightguy bonk wibou

`snapshot` renders unity/snapshot_rigpack.cs.template and runs it with the gateway's
eval_file: the catalog prefab is instantiated in a preview scene, its skinned meshes,
weights, bindposes, rotation constraints, runtime weapon placement and bound clips are written
to JSON, together with Unity's baked skinned vertices at a few frames. Clips are played at 60 Hz
through Animancer with root motion off, as PlayerRenderer plays them: plain clip sampling applies
root motion when the prefab enables it and can stretch legs toward IK goals.
`build` writes artifacts/genra-rigpacks/<package>-unity/: rig.usda (skeleton, meshes, UVs,
materials, first clip), references/<clip>.usda (skeleton + animation), textures/, images/,
mapping.json, validation.json and manifest.json. Unity space is left-handed and Y-up;
the pack mirrors x into a right-handed Y-up space. Constraint-driven copy bones (Manki's
skin fingers) are bound to their source joints; weapons become fixed joints under their bone.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
UNITY = REPO / 'client/Unity'
SNAPSHOTS = REPO / 'artifacts/genra-rigpacks/_snapshots'
CHARACTERS = {  # package -> {reference label: catalog semantic id}
    'manki': {'idle': 'anim.manki.idle', 'run': 'anim.manki.run', 'jump': 'anim.manki.jump', 'fall': 'anim.manki.fall'},
    'fightguy': {'idle': 'anim.idle', 'run': 'anim.run', 'jump': 'anim.jump', 'fall': 'anim.fall'},
    # Bonk's approved two-handed attacks: grip.json is measured from them, and briefs may reference them.
    'bonk': {'idle': 'anim.idle', 'run': 'anim.run', 'jump': 'anim.jump', 'fall': 'anim.fall',
             'swing': 'anim.bonk.g2', 'rising': 'anim.bonk.g3', 'double': 'anim.bonk.g4', 'charge': 'anim.bonk.gr'},
    'wibou': {'idle': 'anim.wibou.idle', 'run': 'anim.wibou.run', 'jump': 'anim.wibou.jump', 'fall': 'anim.wibou.fall'},
}
BAKE_FRAMES = [0, 10, -1]  # -1 = last frame
MIRROR = np.diag([-1.0, 1.0, 1.0])


# ----- Unity snapshot --------------------------------------------------------------------
def snapshot(package: str, out: Path | None = None) -> Path:
    clips = CHARACTERS[package]
    out = out or SNAPSHOTS / f'{package}-{time.strftime("%Y%m%d-%H%M%S")}.json'
    template = (HERE / 'unity/snapshot_rigpack.cs.template').read_text()
    code = (template.replace('{{PACKAGE}}', package).replace('{{OUTPUT}}', str(out))
            .replace('{{CLIPS}}', ', '.join(json.dumps(v) for v in clips.values()))
            .replace('{{BAKES}}', ', '.join(str(f) for f in BAKE_FRAMES)))
    script = Path('/tmp/genra-rigpacks') / f'snapshot-{package}.cs'
    script.parent.mkdir(parents=True, exist_ok=True)
    script.write_text(code)
    command = ['bun', str(REPO / 'scripts/unity-editor-gateway.ts'), '--project-path', str(UNITY), '--',
               'eval_file', '--file', str(script), '--timeout', '600000']
    result = subprocess.run(command, cwd=REPO, capture_output=True, text=True)
    log = SNAPSHOTS / f'{out.stem}.gateway.log'
    SNAPSHOTS.mkdir(parents=True, exist_ok=True)
    log.write_text(result.stdout + '\n--- stderr ---\n' + result.stderr)
    if result.returncode != 0 or not out.exists():
        raise SystemExit(f'Snapshot failed for {package}; see {log}')
    return out


def latest_snapshot(package: str) -> Path:
    found = sorted(SNAPSHOTS.glob(f'{package}-*.json'))
    if not found:
        raise SystemExit(f'No snapshot for {package}; run "snapshot {package}" first')
    return found[-1]


# ----- conversion helpers ----------------------------------------------------------------
def to_row(flat16) -> np.ndarray:
    """Unity row-major column-vector matrix -> mirrored, row-vector (USD/genra) matrix."""
    m = np.array(flat16, float).reshape(4, 4)
    out = np.eye(4)
    out[:3, :3] = MIRROR @ m[:3, :3] @ MIRROR
    out[:3, 3] = MIRROR @ m[:3, 3]
    return out.T


def points(flat) -> np.ndarray:
    return np.array(flat, float).reshape(-1, 3) * np.array([-1.0, 1.0, 1.0])


def split_rigid(m: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Row matrix m = residual @ rigid, with rigid a proper rotation + translation and residual linear-only."""
    u, sv, vt = np.linalg.svd(m[:3, :3])
    if np.linalg.det(u @ vt) < 0:
        u[:, -1] *= -1
    rigid = np.eye(4)
    rigid[:3, :3] = u @ vt
    rigid[3, :3] = m[3, :3]
    residual = np.eye(4)
    residual[:3, :3] = m[:3, :3] @ rigid[:3, :3].T
    return rigid, residual


def token(name: str) -> str:
    t = re.sub(r'[^A-Za-z0-9_]', '_', name)
    return t if t and not t[0].isdigit() else '_' + t


def mesh_tokens(meshes) -> list[str]:
    """Unique USD prim names for the skinned renderers, in snapshot order."""
    out, seen = [], {}
    for m in meshes:
        base = token(m['name'])
        seen[base] = seen.get(base, 0) + 1
        out.append(base if seen[base] == 1 else f'{base}_{seen[base]}')
    return out


# ----- build --------------------------------------------------------------------------------
def build(package: str, snap_path: Path | None = None, out_dir: Path | None = None) -> dict:
    from pxr import Gf, Sdf, Usd, UsdGeom, UsdShade, UsdSkel, Vt

    snap_path = snap_path or latest_snapshot(package)
    data = json.loads(snap_path.read_text())
    # Only always-visible attachments (slot 0) join the rig; slot props are listed in mapping.json.
    props = [w for w in data['weapons'] if w.get('slot') not in (0, None)]
    data['weapons'] = [w for w in data['weapons'] if w.get('slot') in (0, None)]
    out = out_dir or REPO / f'artifacts/genra-rigpacks/{package}-unity'
    if out.exists():
        shutil.rmtree(out)
    (out / 'references').mkdir(parents=True)
    bones = data['bones']
    path_index = {b['path']: i for i, b in enumerate(bones)}

    # Constraint-driven copies (single active source) are bound to their source joints.
    remap = {}
    for c in data['constraints']:
        live = [s for s in c['sources'] if s['path'] and s['weight'] > 0]
        if c['active'] and c['weight'] > 0 and len(live) == 1:
            remap[c['path']] = live[0]['path']

    def resolve(path):
        seen = set()
        while path in remap and path not in seen:
            seen.add(path)
            path = remap[path]
        return path

    # Joints: every bone carrying weight (after remapping) plus their ancestors; never the actor root.
    used = set()
    for m in data['meshes']:
        weights = np.array(m['influenceWeights'])
        for bone_index in set(np.array(m['influenceBones'])[weights > 0].tolist()):
            used.add(resolve(m['bones'][bone_index]))
    for w in data['weapons']:
        used.add(resolve(w['bone']))
    selected = set()
    for p in used:
        i = path_index[p]
        while i > 0:  # index 0 is the actor root
            selected.add(i)
            i = bones[i]['parent']
    order = sorted(selected)
    joint_of = {}
    for i in order:
        parent = bones[i]['parent']
        name = token(bones[i]['name'])
        joint_of[i] = name if parent <= 0 or parent not in joint_of else joint_of[parent] + '/' + name
    joints = [joint_of[i] for i in order]
    # Weapons: a rigid fixed joint under the attach bone; the attach matrix's non-rigid residual
    # (prefab scale, shear or mirroring) moves into the weapon meshes' geomBind. Anchors (blade hilt/tip)
    # are translation-only joints: only their positions matter.
    extra = []  # (joint path, parent joint path, constant local row matrix)
    weapon_residual = {}
    for k, w in enumerate(data['weapons']):
        bone_joint = joint_of[path_index[resolve(w['bone'])]]
        weapon_joint = f'{bone_joint}/Weapon{k}'
        rigid, residual = split_rigid(to_row(w['toBone']))
        weapon_residual[weapon_joint] = residual
        extra.append((weapon_joint, bone_joint, rigid))
        for anchor in w['anchors']:
            local = np.eye(4)
            local[3, :3] = (to_row(anchor['toWeapon']) @ residual)[3, :3]
            extra.append((f'{weapon_joint}/{token(anchor["name"])}', weapon_joint, local))
    joints += [e[0] for e in extra]
    jindex = {j: n for n, j in enumerate(joints)}
    parent_of = {}
    for i in order:
        p = bones[i]['parent']
        parent_of[joint_of[i]] = joint_of.get(p)
    for path, parent, _ in extra:
        parent_of[path] = parent

    # Uniform root scale (FBX import scale) goes onto the Skeleton prim in double precision: UsdSkel stores
    # joint scales as half floats, which costs ~0.5 mm per metre. Joint matrices are kept in skel space.
    scale = float(np.linalg.norm(to_row(bones[order[0]]['world'])[:3, :3], axis=1).mean())
    skel_to_world = np.diag([scale, scale, scale, 1.0]) if abs(scale - 1) > 1e-6 else np.eye(4)
    to_skel = np.linalg.inv(skel_to_world)
    rest_world = {joint_of[i]: to_row(bones[i]['world']) @ to_skel for i in order}

    def world_with_extra(world_of):
        full = dict(world_of)
        for path, parent, local in extra:
            full[path] = local @ full[parent]
        return full

    # Bind (world): from bindposes where skinned; rest pose elsewhere.
    bind = {}
    for m in data['meshes']:
        renderer = np.array(m['world'], float).reshape(4, 4)
        for b, pose in zip(m['bones'], m['bindposes']):
            if b is None:
                continue
            joint = joint_of.get(path_index.get(resolve(b)))
            if joint is None:
                continue
            value = to_row((renderer @ np.linalg.inv(np.array(pose, float).reshape(4, 4))).ravel()) @ to_skel
            if joint in bind and np.abs(bind[joint][3, :3] - value[3, :3]).max() > 1e-4:
                raise SystemExit(f'Conflicting bind poses for {joint}')
            bind.setdefault(joint, value)
    for j in joint_of.values():
        bind.setdefault(j, rest_world[j])
    bind = world_with_extra(bind)

    def local_channels(world_of):
        from genra.rig import decompose
        mats = []
        for j in joints:
            parent = parent_of[j]
            mats.append(world_of[j] if parent is None else world_of[j] @ np.linalg.inv(world_of[parent]))
        return decompose(np.array(mats))

    def define_skeleton(stage):
        root = UsdSkel.Root.Define(stage, '/Rig')
        UsdGeom.SetStageUpAxis(stage, UsdGeom.Tokens.y)
        UsdGeom.SetStageMetersPerUnit(stage, 1.0)
        stage.SetDefaultPrim(root.GetPrim())
        skel = UsdSkel.Skeleton.Define(stage, '/Rig/Skeleton')
        if scale != 1.0 and abs(scale - 1) > 1e-6:
            UsdGeom.Xformable(skel.GetPrim()).AddScaleOp(UsdGeom.XformOp.PrecisionDouble).Set(Gf.Vec3d(scale, scale, scale))
        skel.CreateJointsAttr(Vt.TokenArray(joints))
        skel.CreateBindTransformsAttr(Vt.Matrix4dArray([Gf.Matrix4d(*bind[j].ravel().tolist()) for j in joints]))
        rest = world_with_extra(rest_world)
        rest_local = []
        for j in joints:
            parent = parent_of[j]
            m = rest[j] if parent is None else rest[j] @ np.linalg.inv(rest[parent])
            rest_local.append(Gf.Matrix4d(*m.ravel().tolist()))
        skel.CreateRestTransformsAttr(Vt.Matrix4dArray(rest_local))
        return root, skel

    def write_animation(stage, skel, reference):
        anim = UsdSkel.Animation.Define(stage, '/Rig/Skeleton/Animation')
        anim.CreateJointsAttr(Vt.TokenArray(joints))
        UsdSkel.BindingAPI.Apply(skel.GetPrim()).CreateAnimationSourceRel().SetTargets([anim.GetPrim().GetPath()])
        tr, ro, sc = anim.CreateTranslationsAttr(), anim.CreateRotationsAttr(), anim.CreateScalesAttr()
        for frame in reference['frames']:
            world = world_with_extra({joint_of[i]: to_row(frame['world'][i]) @ to_skel for i in order})
            t, q, s = local_channels(world)
            code = Usd.TimeCode(frame['frame'])
            tr.Set(Vt.Vec3fArray([Gf.Vec3f(*map(float, v)) for v in t]), code)
            ro.Set(Vt.QuatfArray([Gf.Quatf(float(x[3]), Gf.Vec3f(float(x[0]), float(x[1]), float(x[2]))) for x in q]), code)
            sc.Set(Vt.Vec3hArray([Gf.Vec3h(*map(float, v)) for v in s]), code)
        stage.SetStartTimeCode(0)
        stage.SetEndTimeCode(reference['endFrame'])
        stage.SetTimeCodesPerSecond(reference['fps'])
        stage.SetFramesPerSecond(reference['fps'])

    textures = {}

    def material(stage, info, name):
        if not info:
            return None
        mat = UsdShade.Material.Define(stage, f'/Rig/Materials/{token(name)}')
        shader = UsdShade.Shader.Define(stage, mat.GetPath().AppendChild('Surface'))
        shader.CreateIdAttr('UsdPreviewSurface')
        color = info.get('color') or [1, 1, 1, 1]
        shader.CreateInput('roughness', Sdf.ValueTypeNames.Float).Set(0.7)
        if info.get('texture') and (UNITY / info['texture']).exists():
            src = UNITY / info['texture']
            dest = out / 'textures' / src.name
            dest.parent.mkdir(exist_ok=True)
            if not dest.exists():
                shutil.copyfile(src, dest)
            textures[info['texture']] = f'textures/{src.name}'
            reader = UsdShade.Shader.Define(stage, mat.GetPath().AppendChild('st'))
            reader.CreateIdAttr('UsdPrimvarReader_float2')
            reader.CreateInput('varname', Sdf.ValueTypeNames.Token).Set('st')
            tex = UsdShade.Shader.Define(stage, mat.GetPath().AppendChild('Texture'))
            tex.CreateIdAttr('UsdUVTexture')
            tex.CreateInput('file', Sdf.ValueTypeNames.Asset).Set(f'./textures/{src.name}')
            tex.CreateInput('st', Sdf.ValueTypeNames.Float2).ConnectToSource(reader.ConnectableAPI(), 'result')
            tex.CreateInput('scale', Sdf.ValueTypeNames.Float4).Set(Gf.Vec4f(*map(float, color)))
            shader.CreateInput('diffuseColor', Sdf.ValueTypeNames.Color3f).ConnectToSource(tex.ConnectableAPI(), 'rgb')
        else:
            shader.CreateInput('diffuseColor', Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*map(float, color[:3])))
        mat.CreateSurfaceOutput().ConnectToSource(shader.ConnectableAPI(), 'surface')
        return mat

    def define_mesh(stage, skel, name, mesh, joint_idx, joint_w, geom_bind, mat_info):
        prim_path = f'/Rig/{token(name)}'
        geo = UsdGeom.Mesh.Define(stage, prim_path)
        pts = points(mesh['vertices'])
        tris = np.concatenate([np.array(s, int).reshape(-1, 3) for s in mesh['submeshes']])[:, [0, 2, 1]]
        geo.CreatePointsAttr(Vt.Vec3fArray([Gf.Vec3f(*map(float, p)) for p in pts]))
        geo.CreateFaceVertexCountsAttr(Vt.IntArray([3] * len(tris)))
        geo.CreateFaceVertexIndicesAttr(Vt.IntArray(tris.ravel().tolist()))
        geo.CreateSubdivisionSchemeAttr(UsdGeom.Tokens.none)
        if mesh['uv']:
            uv = np.array(mesh['uv'], float).reshape(-1, 2)
            UsdGeom.PrimvarsAPI(geo).CreatePrimvar('st', Sdf.ValueTypeNames.TexCoord2fArray, UsdGeom.Tokens.vertex).Set(
                Vt.Vec2fArray([Gf.Vec2f(*map(float, u)) for u in uv]))
        binding = UsdSkel.BindingAPI.Apply(geo.GetPrim())
        binding.CreateSkeletonRel().SetTargets([skel.GetPrim().GetPath()])
        k = joint_idx.shape[1]
        binding.CreateJointIndicesPrimvar(False, k).Set(Vt.IntArray(joint_idx.ravel().tolist()))
        binding.CreateJointWeightsPrimvar(False, k).Set(Vt.FloatArray(joint_w.ravel().astype(float).tolist()))
        binding.CreateGeomBindTransformAttr(Gf.Matrix4d(*geom_bind.ravel().tolist()))
        mat = material(stage, mat_info, name)
        if mat:
            UsdShade.MaterialBindingAPI.Apply(geo.GetPrim()).Bind(mat)
        return prim_path

    stage = Usd.Stage.CreateNew(str(out / 'rig.usda'))
    _, skel = define_skeleton(stage)
    mesh_names = {}
    for m, name in zip(data['meshes'], mesh_tokens(data['meshes'])):
        counts = np.array(m['influenceCounts'], int)
        idx = np.array(m['influenceBones'], int)
        w = np.array(m['influenceWeights'], float)
        k = int(counts.max())
        ji, jw = np.zeros((len(counts), k), int), np.zeros((len(counts), k))
        starts = np.concatenate([[0], np.cumsum(counts)[:-1]])
        lookup = [jindex.get(joint_of.get(path_index.get(resolve(b)))) if b else None for b in m['bones']]
        for v, (s, c) in enumerate(zip(starts, counts)):
            for n in range(c):
                if w[s + n] <= 0:
                    continue
                target = lookup[idx[s + n]]
                if target is None:
                    raise SystemExit(f"{m['name']}: influence bone {m['bones'][idx[s + n]]} has no joint")
                ji[v, n], jw[v, n] = target, w[s + n]
        mesh_names[name] = m['path']
        define_mesh(stage, skel, name, m['mesh'], ji, jw, to_row(m['world']) @ to_skel, (m['materials'] or [None])[0])
    for k, wpn in enumerate(data['weapons']):
        weapon_joint = f"{joint_of[path_index[resolve(wpn['bone'])]]}/Weapon{k}"
        for p, part in enumerate(wpn['parts']):
            n = len(part['mesh']['vertices']) // 3
            geom_bind = to_row(part['toWeapon']) @ weapon_residual[weapon_joint] @ bind[weapon_joint]
            name = token(f'Weapon{k}_{p}_{part["mesh"]["name"]}')
            mesh_names[name] = f"{wpn['prefab']}:{part['path']}"
            define_mesh(stage, skel, name, part['mesh'], np.full((n, 1), jindex[weapon_joint]), np.ones((n, 1)), geom_bind,
                        (part['materials'] or [None])[0])
    labels = list(CHARACTERS[package])
    write_animation(stage, skel, data['references'][CHARACTERS[package][labels[0]]])
    stage.GetRootLayer().Save()
    for label in labels:
        ref = Usd.Stage.CreateNew(str(out / 'references' / f'{label}.usda'))
        _, s = define_skeleton(ref)
        write_animation(ref, s, data['references'][CHARACTERS[package][label]])
        ref.GetRootLayer().Save()

    mapping = {'package': package, 'prefab': data['prefab'], 'model': data['model'], 'avatar': data['avatar'],
               'avatarPath': data['avatarPath'], 'avatarHuman': data['avatarHuman'], 'unity': data['unity'],
               'coordinates': 'right-handed Y-up metres: Unity x mirrored', 'skeletonScale': scale,
               'actorRootWorld': bones[0]['world'], 'joints': {
                   j: next((bones[i]['path'] for i in order if joint_of[i] == j), None) for j in joints},
               'remappedConstraintCopies': remap, 'meshes': mesh_names, 'textures': textures,
               'weapons': [{k: w[k] for k in ('config', 'slot', 'bone', 'prefab', 'positionOffset', 'rotationOffset')}
                           for w in data['weapons']],
               'slotProps': [{k: w[k] for k in ('config', 'slot', 'bone', 'prefab', 'positionOffset', 'rotationOffset')}
                             for w in props],
               'references': {label: {k: data['references'][sid][k] for k in ('name', 'path', 'sourceFps', 'fps', 'endFrame',
                                                                                 'durationSeconds', 'humanMotion')} | {'semanticId': sid}
                              for label, sid in CHARACTERS[package].items()}}
    (out / 'mapping.json').write_text(json.dumps(mapping, indent=2))
    (out / 'humanoid.json').write_text(json.dumps(humanoid_muscles(data, joint_of, path_index), indent=2))
    learn_anatomy(out, labels[:4])  # idle, run, jump, fall: attack clips add twist-distribution noise to the axes
    grip = learn_grip(out, labels)
    if grip:
        (out / 'grip.json').write_text(json.dumps(grip, indent=2))
    report = validate(package, data, out, order, joint_of, labels)
    (out / 'validation.json').write_text(json.dumps(report, indent=2))
    images(out, labels[:4])
    from genra.rigpack import sha256
    files = sorted(p for p in out.rglob('*') if p.is_file() and p.name != 'manifest.json')
    (out / 'manifest.json').write_text(json.dumps({
        'notes': f'{package} rig pack from Unity snapshot {snap_path.name} ({data["prefab"]}, {data["model"]})',
        'snapshot': {'path': str(snap_path.relative_to(REPO)), 'sha256': sha256(snap_path)},
        'files': {str(p.relative_to(out)): sha256(p) for p in files}}, indent=2))
    return report


def humanoid_muscles(data, joint_of, path_index) -> dict:
    """Each mapped joint's Humanoid muscles in the pack's frame: the parent-frame axis a muscle turns about
    (toward +1), the degrees reached at -1 and +1 and the local rotation at muscle zero. Unity's x mirror turns a
    local quaternion (x, y, z, w) into (x, -y, -z, w)."""
    from scipy.spatial.transform import Rotation
    out = {}
    for entry in data.get('humanoid', []):
        i = path_index.get(entry['path'])
        if i not in joint_of:
            continue  # mapped but carries no skin (e.g. eyes, jaw)
        q = np.array(entry['localRotations'], float) * [1.0, -1.0, -1.0, 1.0]
        values = np.array(entry['values'], float)
        zero = Rotation.from_quat(q[values == 0][0])
        delta = Rotation.from_quat(q) * zero.inv()  # parent-frame rotation away from muscle zero
        vectors = delta.as_rotvec()
        axis = vectors[values == 1][0] - vectors[values == -1][0]
        if np.linalg.norm(axis) < 1e-9:
            continue  # the muscle does not move this bone (e.g. its twist goes to a neighbour)
        axis /= np.linalg.norm(axis)
        degrees = np.degrees(vectors @ axis)
        linear = np.interp(values, [-1, 0, 1], [degrees[values == -1][0], 0.0, degrees[values == 1][0]])
        out.setdefault(joint_of[i], []).append({
            'muscle': entry['muscle'], 'humanBone': entry['humanBone'], 'dof': entry['dof'],
            'axisParent': axis.round(7).tolist(), 'degreesAtMinusOne': round(float(degrees[values == -1][0]), 4),
            'degreesAtPlusOne': round(float(degrees[values == 1][0]), 4), 'zero': zero.as_quat().round(9).tolist(),
            'offAxisDegrees': round(float(np.degrees(np.linalg.norm(vectors - np.outer(vectors @ axis, axis), axis=1)).max()), 4),
            'nonlinearDegrees': round(float(np.abs(degrees - linear).max()), 4)})
    return out


def baked_world(bake, r) -> dict:
    """Unity BakeMesh vertices (renderer-local) to Unity world, for each candidate scale convention."""
    m = np.array(bake['rendererWorld'][r], float).reshape(4, 4)
    rot_scale, t = m[:3, :3], m[:3, 3]
    rot = rot_scale / np.linalg.norm(rot_scale, axis=0)
    out = {}
    for label, key in (('useScale', 'bakedUseScale'), ('noScale', 'bakedNoScale')):
        v = np.array(bake[key][r], float).reshape(-1, 3)
        out[label + '+matrix'] = (v @ rot_scale.T + t).ravel()
        out[label + '+rigid'] = (v @ rot.T + t).ravel()
    return out


def validate(package, data, out, order, joint_of, labels) -> dict:
    """Pack FK vs Unity bone samples; pack skinning vs Unity's baked vertices (same vertex order)."""
    from genra.clip import Clip
    from genra.rig import Rig
    from genra.skin import Skin
    rig = Rig(out / 'rig.usda')
    skin = Skin(rig)
    report = {'joints': len(rig.names), 'meshes': len(skin.meshes), 'references': {}}
    worst_fk, worst_skin = 0.0, 0.0
    names = {m['name']: i for i, m in enumerate(skin.meshes)}
    for label in labels:
        ref = data['references'][CHARACTERS[package][label]]
        clip = Clip.from_reference(Rig(out / 'references' / f'{label}.usda'))
        world = rig.fk(clip.t, clip.q, clip.s)
        fk_err = 0.0
        for frame in ref['frames']:
            f = frame['frame']
            for i in order:
                j = rig.index(joint_of[i].rsplit('/', 1)[-1])
                fk_err = max(fk_err, float(np.linalg.norm(world[f, j, 3, :3] - to_row(frame['world'][i])[3, :3])))
        skin_err = {}
        for bake in ref['bakes']:
            pts = skin.points(world[bake['frame']])
            for r, (m, prim) in enumerate(zip(data['meshes'], mesh_tokens(data['meshes']))):
                mine = pts[names[prim]]
                errors = {mode: float(np.linalg.norm(mine - points(unity), axis=1).max())
                          for mode, unity in baked_world(bake, r).items()}
                mode = min(errors, key=errors.get)
                report.setdefault('bakeConversion', {})[mode] = report.get('bakeConversion', {}).get(mode, 0) + 1
                skin_err[m['name']] = max(skin_err.get(m['name'], 0.0), errors[mode])
        report['references'][label] = {'frames': len(ref['frames']), 'maxJointErrorMetres': fk_err,
                                       'maxSkinErrorMetres': skin_err, 'bakedFrames': [b['frame'] for b in ref['bakes']]}
        worst_fk = max(worst_fk, fk_err)
        worst_skin = max([worst_skin] + list(skin_err.values()))
    report['maxJointErrorMetres'], report['maxSkinErrorMetres'] = worst_fk, worst_skin
    report['passed'] = worst_fk < 1e-4 and worst_skin < 1e-3
    return report


def learn_anatomy(out: Path, labels, min_flexion=10.0) -> dict:
    """Choose each hinge's flexion axis from the character's approved clips, with heuristic fallback.

    Knees, elbows and finger middle joints are learned when the clips agree (consistency > 0.8); they only
    flex one way. Knuckles are not learned (open hands hyperextend them): they take their finger's middle
    joint axis carried into the hand frame. reversedAllowanceDegrees records the largest backward bend the
    approved clips already use, so the checks do not flag the character's own style.
    """
    from scipy.spatial.transform import Rotation
    from genra.clip import Clip
    from genra.rig import Rig, unit
    rig = Rig(out / 'rig.usda')  # heuristic axes: anatomy.json does not exist yet
    worlds = []
    for label in labels:
        source = Clip.from_reference(Rig(out / 'references' / f'{label}.usda'))
        worlds += list(rig.fk(source.t, source.q, source.s))
    samples = {name: [s for s in (rig.hinge_axis_state(w, name) for w in worlds) if s[0] > min_flexion] for name in rig.hinges}
    hinges = {}
    for name in rig.hinges:
        if 'Knuckle' in name:
            continue
        rows, heuristic = samples[name], rig.hinges[name][4]
        entry = {'kind': rig.hinges[name][0], 'samples': len(rows), 'heuristicAxis': heuristic.tolist()}
        if len(rows) >= 10:
            axis = unit(sum(a * ax for a, ax in rows))
            consistency = float(np.mean([axis @ ax for _, ax in rows]))
            entry.update(consistency=consistency, agreementWithHeuristic=float(axis @ heuristic))
            if consistency > 0.8:
                entry.update(axis=axis.tolist(), source='references')
        entry.setdefault('axis', heuristic.tolist())
        entry.setdefault('source', 'heuristic')
        hinges[name] = entry
    for name in [n for n in rig.hinges if 'Knuckle' in n]:
        middle = hinges.get(name.replace('Knuckle', 'Middle'))
        heuristic = rig.hinges[name][4]
        entry = {'kind': 'finger', 'heuristicAxis': heuristic.tolist()}
        if middle:
            first = rig.hinges[name][2]  # <Finger>1: the frame joint of the middle hinge
            axis = unit(Rotation.from_quat(rig.bind_q[first]).apply(np.array(middle['axis'], float)))
            entry.update(axis=axis.tolist(), source='middle joint (' + middle['source'] + ')',
                         agreementWithHeuristic=float(axis @ heuristic))
        else:
            entry.update(axis=heuristic.tolist(), source='heuristic')
        hinges[name] = entry
    for name, entry in hinges.items():
        axis = np.array(entry['axis'], float)
        backward = [a for a, ax in samples[name] if ax @ axis <= -0.5]
        entry['reversedAllowanceDegrees'] = round(max(backward) + 5.0, 1) if backward else 0.0
    result = {'learnedFrom': labels, 'minimumFlexionDegrees': min_flexion, 'hinges': hinges}
    (out / 'anatomy.json').write_text(json.dumps(result, indent=2))
    return result


def learn_grip(out: Path, labels, off_axis=0.03) -> dict | None:
    """How the character's own clips hold the weapon with both hands, as the left hand's exact transform in the
    weapon joint's frame. Taken from the approved frame whose left grip point (between the hand and its middle
    knuckle) sits closest to the handle axis, beyond the hilt: animators rarely keep a second hand rigid, so one
    clean frame is a better grip than an average of loose ones. Other candidate frames are listed."""
    from genra.rig import Rig, decompose, unit
    rig = Rig(out / 'rig.usda')
    weapon = next((n for n in rig.names if n.startswith('Weapon')), None)
    knuckle = next((f'LeftHand{f}1' for f in ('Middle', 'Index') if f'LeftHand{f}1' in rig.names), None)
    if weapon is None or knuckle is None or not {'bladeHilt', 'bladeEnd'} <= set(rig.names):
        return None
    w, hilt, tip, hand, k = (rig.index(n) for n in (weapon, 'bladeHilt', 'bladeEnd', 'LeftHand', knuckle))
    candidates = []
    for label in labels:
        source = Rig(out / 'references' / f'{label}.usda')
        first, last = source.animated_range()
        world = rig.fk(*source.sample_local(range(first, last + 1)))
        axis = unit(world[:, hilt, 3, :3] - world[:, tip, 3, :3])  # toward the pommel
        rel = (world[:, hand, 3, :3] + world[:, k, 3, :3]) / 2 - world[:, hilt, 3, :3]
        along = np.sum(rel * axis, axis=1)
        off = np.linalg.norm(rel - axis * along[:, None], axis=1)
        for tick in np.nonzero((off < off_axis) & (along > 0))[0]:
            local = world[tick, hand] @ np.linalg.inv(world[tick, w])  # row convention: hand = local @ weapon
            candidates.append((float(off[tick]), label, int(tick), float(along[tick]), local))
    if not candidates:
        return None
    off, label, tick, along, local = min(candidates, key=lambda c: c[0])
    t, q, _ = decompose(local[None])
    return {'weapon': weapon, 'hand': 'LeftHand', 'source': {'clip': label, 'tick': tick},
            't': t[0].round(7).tolist(), 'q': q[0].round(9).tolist(),
            'offAxisMetres': round(off, 4), 'pastHiltMetres': round(along, 4),
            'candidates': [{'clip': c[1], 'tick': c[2], 'offAxisMetres': round(c[0], 4)} for c in sorted(candidates)]}


def images(out: Path, labels):
    """Entry-pose renders (profile, front, three-quarter rear) of each reference, for briefs."""
    from genra.clip import Clip, CharFrame
    from genra.render import contact_sheet
    from genra.rig import Rig
    from genra.skin import Skin
    rig = Rig(out / 'rig.usda')
    skin = Skin(rig)
    (out / 'images').mkdir(exist_ok=True)
    for label in labels:
        source = Clip.from_reference(Rig(out / 'references' / f'{label}.usda'))
        clip = Clip(rig, source.t, source.q, source.s)
        clip.frame = CharFrame.from_pose(clip.pose(0))
        contact_sheet(clip, [0], ('profile', 'front', 'three-quarter-right'), out / 'images' / f'{label}-entry.png',
                      size=320, skin=skin)
    grip = out / 'grip.json'
    if grip.exists():  # the approved two-handed frame the grip comes from
        source = json.loads(grip.read_text())['source']
        ref = Clip.from_reference(Rig(out / 'references' / f"{source['clip']}.usda"))
        clip = Clip(rig, ref.t, ref.q, ref.s)
        tick = source['tick']
        clip.frame = CharFrame.from_pose(clip.pose(tick))
        world = clip.world()[tick]
        hands = [world[rig.index(n), 3, :3] for n in ('LeftHand', 'RightHand')]
        hand = np.linalg.norm(world[rig.index('LeftHandMiddle1'), 3, :3] - hands[0]) if 'LeftHandMiddle1' in rig.names else 0.1
        contact_sheet(clip, [tick], ('front', 'three-quarter-right', 'profile'), out / 'images' / 'two-hand-grip.png',
                      size=320, skin=skin, span=6 * hand, center=clip.frame.to_char((hands[0] + hands[1]) / 2))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('command', choices=['snapshot', 'build', 'all'])
    parser.add_argument('packages', nargs='+', choices=sorted(CHARACTERS))
    args = parser.parse_args(argv)
    sys.path.insert(0, str(HERE.parent))
    for package in args.packages:
        if args.command in ('snapshot', 'all'):
            print(package, 'snapshot ->', snapshot(package), flush=True)
        if args.command in ('build', 'all'):
            report = build(package)
            print(package, json.dumps({k: report[k] for k in ('joints', 'meshes', 'maxJointErrorMetres', 'maxSkinErrorMetres', 'passed')}),
                  flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
