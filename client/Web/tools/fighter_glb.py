"""Fighter models for the web demo: GenRA's read-only Unity rig snapshot to one .glb per package.

The snapshot (tools/genra/unity/snapshot_rigpack.cs.template, run with the gateway's eval_file) records the package's
catalog prefab as the game renders it, and every requested clip played at 60 Hz through Animancer the way
PlayerRenderer plays it. This writes glTF from it: skinned meshes with base-colour textures, weapons as nodes under
their bone (named weapon:<slot>:<prefab>), and one animation per bound semantic id whose keyframe k is clip frame k,
so the page can show exactly the frame the sim reports.

    PY=artifacts/genra-20261014/.venv/bin/python
    $PY client/Web/tools/fighter_glb.py snapshot fightguy    # Unity, own gateway lease, read-only
    $PY client/Web/tools/fighter_glb.py build fightguy       # latest snapshot -> client/Web/assets/fighters/fightguy.glb
    $PY client/Web/tools/fighter_glb.py all fightguy manki

Every build re-skins its own glTF data at the frames Unity baked and compares with Unity's skinned vertices.
"""
from __future__ import annotations

import argparse
import io
import json
import struct
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
UNITY = REPO / 'client/Unity'
SNAPSHOTS = REPO / 'artifacts/web-fighters/snapshots'
OUT = REPO / 'client/Web/assets/fighters'
FPS = 60
TEXTURE_MAX = 1024
PROP_TRIANGLES = 12000  # rigid props above this are simplified: Manki's Tripo bazooka alone is 1.36M triangles
S = np.diag([-1.0, 1.0, 1.0, 1.0])  # Unity is left-handed: mirror x into glTF's right-handed space


def bound_semantics(package: str) -> list[str]:
    bindings = json.loads((REPO / 'content-cooked' / package / 'client.bindings').read_text())
    return [a['semanticId'] for a in bindings['animations']]


def snapshot(package: str) -> Path:
    sys.path.insert(0, str(REPO / 'tools'))
    from genra.unity_rigpack import snapshot as genra_snapshot
    SNAPSHOTS.mkdir(parents=True, exist_ok=True)
    out = SNAPSHOTS / f'{package}-{time.strftime("%Y%m%d-%H%M%S")}.json'
    semantics = bound_semantics(package)
    return genra_snapshot(package, out=out, refs={s: s for s in semantics}, label='web')


def latest_snapshot(package: str) -> Path:
    found = sorted(SNAPSHOTS.glob(f'{package}-*.json'))
    if not found:
        raise SystemExit(f'No snapshot for {package}; run "snapshot {package}" first')
    return found[-1]


# ----- math --------------------------------------------------------------------------------------
def unity(flat16) -> np.ndarray:
    return np.array(flat16, float).reshape(4, 4)


def mirror(m: np.ndarray) -> np.ndarray:
    return S @ m @ S


def to_trs(m: np.ndarray):
    t = m[:3, 3].copy()
    basis = m[:3, :3]
    scale = np.linalg.norm(basis, axis=0)
    if np.linalg.det(basis) < 0:
        scale[0] = -scale[0]
    rot = basis / scale
    return t, quat(rot), scale


def quat(r: np.ndarray) -> np.ndarray:
    """Rotation matrix to (x, y, z, w)."""
    tr = r[0, 0] + r[1, 1] + r[2, 2]
    if tr > 0:
        s = np.sqrt(tr + 1.0) * 2
        q = [(r[2, 1] - r[1, 2]) / s, (r[0, 2] - r[2, 0]) / s, (r[1, 0] - r[0, 1]) / s, 0.25 * s]
    elif r[0, 0] > r[1, 1] and r[0, 0] > r[2, 2]:
        s = np.sqrt(1.0 + r[0, 0] - r[1, 1] - r[2, 2]) * 2
        q = [0.25 * s, (r[0, 1] + r[1, 0]) / s, (r[0, 2] + r[2, 0]) / s, (r[2, 1] - r[1, 2]) / s]
    elif r[1, 1] > r[2, 2]:
        s = np.sqrt(1.0 + r[1, 1] - r[0, 0] - r[2, 2]) * 2
        q = [(r[0, 1] + r[1, 0]) / s, 0.25 * s, (r[1, 2] + r[2, 1]) / s, (r[0, 2] - r[2, 0]) / s]
    else:
        s = np.sqrt(1.0 + r[2, 2] - r[0, 0] - r[1, 1]) * 2
        q = [(r[0, 2] + r[2, 0]) / s, (r[1, 2] + r[2, 1]) / s, 0.25 * s, (r[1, 0] - r[0, 1]) / s]
    q = np.array(q)
    return q / np.linalg.norm(q)


def compose(t, q, s) -> np.ndarray:
    x, y, z, w = q
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.asarray(s)
    m[:3, 3] = t
    return m


def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# ----- glTF writer -------------------------------------------------------------------------------
class Gltf:
    def __init__(self):
        self.json = {'asset': {'version': '2.0', 'generator': 'SLOPArena fighter_glb.py'}, 'scene': 0,
                     'scenes': [{'nodes': []}], 'nodes': [], 'meshes': [], 'materials': [], 'textures': [],
                     'images': [], 'samplers': [{'magFilter': 9729, 'minFilter': 9987}], 'accessors': [],
                     'bufferViews': [], 'buffers': [{'byteLength': 0}], 'skins': [], 'animations': []}
        self.bin = bytearray()

    def view(self, data: bytes, target: int | None = None) -> int:
        while len(self.bin) % 4:
            self.bin.append(0)
        view = {'buffer': 0, 'byteOffset': len(self.bin), 'byteLength': len(data)}
        if target:
            view['target'] = target
        self.bin += data
        self.json['bufferViews'].append(view)
        return len(self.json['bufferViews']) - 1

    def accessor(self, array: np.ndarray, kind: str, target: int | None = None, minmax: bool = False,
                 normalized: bool = False) -> int:
        component = {np.float32: 5126, np.uint16: 5123, np.uint32: 5125, np.uint8: 5121, np.int8: 5120,
                     np.int16: 5122}[array.dtype.type]
        if target == 34962 and array.dtype.itemsize * (array.shape[1] if array.ndim > 1 else 1) % 4:
            # Vertex attributes must keep 4-byte element strides: pad small integer vectors.
            pad = (-(array.dtype.itemsize * array.shape[1])) % 4 // array.dtype.itemsize
            array = np.c_[array, np.zeros((array.shape[0], pad), array.dtype)]
            view = self.view(array.tobytes(), target)
            self.json['bufferViews'][view]['byteStride'] = array.dtype.itemsize * array.shape[1]
        else:
            view = self.view(array.tobytes(), target)
        acc = {'bufferView': view, 'componentType': component, 'count': int(array.shape[0]), 'type': kind}
        if normalized:
            acc['normalized'] = True
        if minmax:
            acc['min'] = array.min(axis=0).tolist() if array.ndim > 1 else [float(array.min())]
            acc['max'] = array.max(axis=0).tolist() if array.ndim > 1 else [float(array.max())]
        self.json['accessors'].append(acc)
        return len(self.json['accessors']) - 1

    def node(self, **fields) -> int:
        self.json['nodes'].append({k: v for k, v in fields.items() if v is not None})
        return len(self.json['nodes']) - 1

    def write(self, path: Path):
        for key in [k for k, v in self.json.items() if isinstance(v, list) and not v]:
            del self.json[key]
        self.json['buffers'] = [{'byteLength': len(self.bin)}]
        self.json['extensionsUsed'] = self.json['extensionsRequired'] = ['KHR_mesh_quantization']
        text = json.dumps(self.json, separators=(',', ':')).encode()
        text += b' ' * (-len(text) % 4)
        while len(self.bin) % 4:
            self.bin.append(0)
        path.parent.mkdir(parents=True, exist_ok=True)
        with open(path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(text) + 8 + len(self.bin)))
            f.write(struct.pack('<II', len(text), 0x4E4F534A) + text)
            f.write(struct.pack('<II', len(self.bin), 0x004E4942) + bytes(self.bin))


def trs_fields(m: np.ndarray) -> dict:
    t, q, s = to_trs(m)
    out = {}
    if np.abs(t).max() > 1e-9:
        out['translation'] = t.tolist()
    if np.abs(q - [0, 0, 0, 1]).max() > 1e-9:
        out['rotation'] = q.tolist()
    if np.abs(s - 1).max() > 1e-9:
        out['scale'] = s.tolist()
    return out


class Fighter:
    def __init__(self, package: str, data: dict):
        self.package, self.data = package, data
        self.g = Gltf()
        self.materials: dict[str, int] = {}
        bones = data['bones']
        self.rest = [unity(b['world']) for b in bones]
        self.paths = {b['path']: i for i, b in enumerate(bones)}
        self.node_of: dict[int, int] = {}

    # Bones keep their Unity local transforms, mirrored. The prefab root is written as identity: PlayerRenderer
    # resets the instance to zero position, no rotation and the definition's VisualScale, which the page applies.
    def skeleton(self):
        bones = self.data['bones']
        for i, b in enumerate(bones):
            parent = b['parent']
            local = np.eye(4) if parent < 0 else np.linalg.inv(self.rest[parent]) @ self.rest[i]
            self.node_of[i] = self.g.node(name=b['name'], **trs_fields(mirror(local)))
        for i, b in enumerate(bones):
            if b['parent'] >= 0:
                self.g.json['nodes'][self.node_of[b['parent']]].setdefault('children', []).append(self.node_of[i])
            else:
                self.g.json['scenes'][0]['nodes'].append(self.node_of[i])

    def material(self, m: dict) -> int:
        key = f"{m.get('path')}|{m.get('texture')}"
        if key in self.materials:
            return self.materials[key]
        color = m.get('color') or [1, 1, 1, 1]
        pbr = {'baseColorFactor': [srgb_to_linear(c) for c in color[:3]] + [color[3] if len(color) > 3 else 1.0],
               'metallicFactor': 0.0, 'roughnessFactor': 0.75}
        texture = m.get('texture')
        if texture and (UNITY / texture).exists():
            pbr['baseColorTexture'] = {'index': self.texture(UNITY / texture)}
        self.g.json['materials'].append({'name': m.get('name') or 'material', 'pbrMetallicRoughness': pbr})
        self.materials[key] = len(self.g.json['materials']) - 1
        return self.materials[key]

    def texture(self, path: Path) -> int:
        image = Image.open(path)
        alpha = image.mode in ('RGBA', 'LA') and image.getchannel('A').getextrema()[0] < 250
        image = image.convert('RGBA' if alpha else 'RGB')
        image.thumbnail((TEXTURE_MAX, TEXTURE_MAX))
        buf = io.BytesIO()
        image.save(buf, 'PNG' if alpha else 'JPEG', quality=85, optimize=True)
        view = self.g.view(buf.getvalue())
        self.g.json['images'].append({'bufferView': view, 'mimeType': 'image/png' if alpha else 'image/jpeg', 'name': path.stem})
        self.g.json['textures'].append({'source': len(self.g.json['images']) - 1, 'sampler': 0})
        return len(self.g.json['textures']) - 1

    def primitives(self, mesh: dict, materials: list, extra: dict | None = None, rigid: bool = False) -> tuple[list, np.ndarray, list]:
        v = np.array(mesh['vertices'], float).reshape(-1, 3) * [-1, 1, 1]
        uv = np.array(mesh['uv'], float).reshape(-1, 2) * [1, -1] + [0, 1] if mesh.get('uv') else None
        # Mirroring x turns Unity's front faces inside out; reverse each triangle so glTF's CCW faces point outward.
        tris = [np.array(sub, np.int64).reshape(-1, 3)[:, [0, 2, 1]] for sub in mesh['submeshes']]
        if rigid and sum(len(t) for t in tris) > PROP_TRIANGLES:
            v, uv, tris = simplify(v, uv, tris, PROP_TRIANGLES)
        normals = smooth_normals(v, np.concatenate(tris))
        attributes = {'POSITION': self.g.accessor(v.astype(np.float32), 'VEC3', 34962, minmax=True),
                      'NORMAL': self.g.accessor(np.round(normals * 127).astype(np.int8), 'VEC3', 34962, normalized=True)}
        if uv is not None:
            if uv.min() >= 0 and uv.max() <= 1:
                attributes['TEXCOORD_0'] = self.g.accessor(np.round(uv * 65535).astype(np.uint16), 'VEC2', 34962, normalized=True)
            else:
                attributes['TEXCOORD_0'] = self.g.accessor(uv.astype(np.float32), 'VEC2', 34962)
        attributes.update(extra or {})
        prims = []
        for k, t in enumerate(tris):
            index_type = np.uint16 if len(v) < 65536 else np.uint32
            prim = {'attributes': attributes, 'indices': self.g.accessor(t.ravel().astype(index_type), 'SCALAR', 34963)}
            if k < len(materials):
                prim['material'] = self.material(materials[k])
            prims.append(prim)
        return prims, v, tris

    def skinned_meshes(self) -> list:
        report = []
        for mesh in self.data['meshes']:
            joints = [self.paths[p] for p in mesh['bones']]
            counts = np.array(mesh['influenceCounts'])
            bones = np.array(mesh['influenceBones'])
            weights = np.array(mesh['influenceWeights'], float)
            n = len(counts)
            j4, w4 = np.zeros((n, 4), np.uint16), np.zeros((n, 4), np.float32)
            start = np.concatenate([[0], np.cumsum(counts)[:-1]])
            for i in range(n):
                b, w = bones[start[i]:start[i] + counts[i]], weights[start[i]:start[i] + counts[i]]
                top = np.argsort(-w)[:4]
                total = w[top].sum()
                j4[i, :len(top)] = b[top]
                w4[i, :len(top)] = w[top] / (total if total > 0 else 1)
            w8 = np.round(w4 * 255).astype(np.int32)
            w8[np.arange(n), np.argmax(w8, axis=1)] += 255 - w8.sum(axis=1)  # quantized weights still sum to one
            w4 = (w8 / 255).astype(np.float32)
            joints_type = np.uint8 if len(joints) < 256 else np.uint16
            extra = {'JOINTS_0': self.g.accessor(j4.astype(joints_type), 'VEC4', 34962),
                     'WEIGHTS_0': self.g.accessor(w8.astype(np.uint8), 'VEC4', 34962, normalized=True)}
            prims, v, tris = self.primitives(mesh['mesh'], mesh['materials'], extra)
            ibm = np.stack([mirror(unity(b)).T for b in mesh['bindposes']]).astype(np.float32)
            self.g.json['skins'].append({'joints': [self.node_of[j] for j in joints],
                                         'inverseBindMatrices': self.g.accessor(ibm.reshape(-1, 16), 'MAT4'),
                                         'skeleton': self.node_of[0]})
            self.g.json['meshes'].append({'name': mesh['name'], 'primitives': prims})
            node = self.g.node(name=mesh['name'], mesh=len(self.g.json['meshes']) - 1, skin=len(self.g.json['skins']) - 1)
            self.g.json['scenes'][0]['nodes'].append(node)
            report.append({'mesh': mesh['name'], 'joints': joints, 'v': v, 'j4': j4, 'w4': w4,
                           'ibm': [mirror(unity(b)) for b in mesh['bindposes']], 'tris': tris})
        return report

    def weapons(self):
        for w in self.data.get('weapons', []):
            bone = self.paths.get(w['bone'])
            if bone is None:
                continue
            prefab = Path(w['prefab']).stem
            weapon = self.g.node(name=f"weapon:{w['slot']}:{prefab}", **trs_fields(mirror(unity(w['toBone']))))
            self.g.json['nodes'][self.node_of[bone]].setdefault('children', []).append(weapon)
            for part in w.get('parts', []):
                prims, _, _ = self.primitives(part['mesh'], part.get('materials', []), rigid=True)
                self.g.json['meshes'].append({'name': part['mesh']['name'], 'primitives': prims})
                node = self.g.node(name=f"{prefab}:{part['mesh']['name']}", mesh=len(self.g.json['meshes']) - 1,
                                   **trs_fields(mirror(unity(part['toWeapon']))))
                self.g.json['nodes'][weapon].setdefault('children', []).append(node)

    def animations(self) -> dict:
        bones = self.data['bones']
        parents = [b['parent'] for b in bones]
        locals_by_clip = {}
        for semantic, ref in self.data['references'].items():
            frames = sorted(ref['frames'], key=lambda f: f['frame'])
            worlds = np.array([[unity(m) for m in f['world']] for f in frames])  # [frame][bone]4x4
            local = np.empty_like(worlds)
            for i, p in enumerate(parents):
                local[:, i] = worlds[:, i] if p < 0 else np.linalg.inv(worlds[:, p]) @ worlds[:, i]
            local = S @ local @ S
            times = np.array([f['frame'] for f in frames], np.float32) / FPS
            channels, samplers = [], []
            time_acc = self.g.accessor(times, 'SCALAR', minmax=True)
            for i in range(len(bones)):
                if parents[i] < 0:
                    continue
                trs = [to_trs(local[k, i]) for k in range(len(frames))]
                rest = to_trs(mirror(self.rest[i] if parents[i] < 0 else np.linalg.inv(self.rest[parents[i]]) @ self.rest[i]))
                t = np.array([x[0] for x in trs], np.float32)
                q = np.array([x[1] for x in trs], np.float32)
                s = np.array([x[2] for x in trs], np.float32)
                for k in range(1, len(q)):
                    if np.dot(q[k], q[k - 1]) < 0:
                        q[k] = -q[k]
                for path, values, kind, rest_value in (('translation', t, 'VEC3', rest[0]), ('rotation', q, 'VEC4', rest[1]),
                                                       ('scale', s, 'VEC3', rest[2])):
                    still = np.abs(values - values[0]).max() < 1e-6
                    at_rest = np.abs(values[0] - rest_value).max() < 1e-6 or (path == 'rotation' and np.abs(values[0] + rest_value).max() < 1e-6)
                    if still and at_rest:
                        continue
                    output = (self.g.accessor(np.round(values * 32767).astype(np.int16), kind, normalized=True)
                              if path == 'rotation' else self.g.accessor(values, kind))
                    samplers.append({'input': time_acc, 'output': output, 'interpolation': 'LINEAR'})
                    channels.append({'sampler': len(samplers) - 1, 'target': {'node': self.node_of[i], 'path': path}})
            self.g.json['animations'].append({'name': semantic, 'channels': channels, 'samplers': samplers})
            locals_by_clip[semantic] = (frames, local)
        return locals_by_clip

    def validate(self, skins: list, clips: dict) -> dict:
        """Re-skin the exported data at Unity's baked frames and compare with Unity's skinned vertices."""
        parents = [b['parent'] for b in self.data['bones']]
        worst, per_clip = 0.0, {}
        for semantic, ref in self.data['references'].items():
            frames, local = clips[semantic]
            index = {f['frame']: k for k, f in enumerate(frames)}
            for bake in ref.get('bakes', []):
                k = index.get(bake['frame'])
                if k is None:
                    continue
                world = [None] * len(parents)
                for i, p in enumerate(parents):
                    m = compose(*to_trs(local[k, i]))
                    world[i] = m if p < 0 else world[p] @ m
                for r, skin in enumerate(skins):
                    mats = np.stack([world[j] @ skin['ibm'][n] for n, j in enumerate(skin['joints'])])
                    v = np.c_[skin['v'], np.ones(len(skin['v']))]
                    mine = np.zeros((len(v), 3))
                    for c in range(4):
                        mine += skin['w4'][:, c:c + 1] * np.einsum('nij,nj->ni', mats[skin['j4'][:, c]], v)[:, :3]
                    m = unity(bake['rendererWorld'][r])
                    rot_scale, t = m[:3, :3], m[:3, 3]
                    best = min(
                        float(np.linalg.norm(mine - (np.array(bake[key][r], float).reshape(-1, 3) @ basis.T + t) * [-1, 1, 1], axis=1).max())
                        for key in ('bakedUseScale', 'bakedNoScale')
                        for basis in (rot_scale, rot_scale / np.linalg.norm(rot_scale, axis=0)))
                    per_clip[semantic] = max(per_clip.get(semantic, 0.0), best)
                    worst = max(worst, best)
        return {'maxSkinErrorMetres': worst, 'clips': per_clip}


def form_parts(prefab: str | None) -> dict | None:
    """FormPresentation's renderer lists from the prefab YAML: form 0 shows "0", form 1 shows "1", "hidden" stays off."""
    if not prefab or not (UNITY / prefab).exists():
        return None
    import re
    text = (UNITY / prefab).read_text(errors='ignore')
    if '_realForm:' not in text:
        return None
    docs = re.split(r'^--- !u!(\d+) &(-?\d+).*$', text, flags=re.M)
    names, owner = {}, {}
    for i in range(1, len(docs) - 2, 3):
        cls, fid, body = docs[i], docs[i + 1], docs[i + 2]
        name = re.search(r'm_Name: (.*)', body)
        if cls == '1' and name:
            names[fid] = name.group(1).strip()
        if '_realForm:' in body:
            owner = body
    def listed(key):
        block = owner.split(key + ':', 1)[1].split('\n  _', 1)[0]
        return [names[f] for f in re.findall(r'fileID: (-?\d+)', block) if f in names]
    return {'0': listed('_realForm'), '1': listed('_alternateForm'),
            'hidden': listed('_hiddenProps') if '_hiddenProps:' in owner else []}


def simplify(v: np.ndarray, uv: np.ndarray | None, tris: list, target: int):
    """Grid vertex clustering down to about `target` triangles. UV seams stay apart: vertices merge only when both
    their position cell and their UV cell match. Good enough for props that read small on screen."""
    total = sum(len(t) for t in tris)
    lo, hi = 1e-4, float(np.ptp(v, axis=0).max())
    best = None
    for _ in range(18):
        cell = (lo * hi) ** 0.5
        key = np.floor(v / cell).astype(np.int64)
        if uv is not None:
            key = np.c_[key, np.floor(uv * 48).astype(np.int64)]
        _, inverse = np.unique(key, axis=0, return_inverse=True)
        inverse = inverse.ravel()
        out = []
        for t in tris:
            m = inverse[t]
            keep = (m[:, 0] != m[:, 1]) & (m[:, 1] != m[:, 2]) & (m[:, 0] != m[:, 2])
            out.append(np.unique(np.sort(m[keep], axis=1), axis=0, return_index=True)[1])
            out[-1] = m[keep][np.sort(out[-1])]
        count = sum(len(t) for t in out)
        if count > target:
            lo = cell
        else:
            hi = cell
            best = (inverse, out)
        if best and abs(count - target) < target * 0.1:
            break
    if best is None:
        return v, uv, tris
    inverse, out = best
    n = inverse.max() + 1
    weight = np.bincount(inverse, minlength=n)[:, None]
    nv = np.zeros((n, 3))
    np.add.at(nv, inverse, v)
    nv /= np.maximum(weight, 1)
    nuv = None
    if uv is not None:
        nuv = np.zeros((n, 2))
        np.add.at(nuv, inverse, uv)
        nuv /= np.maximum(weight, 1)
    used = np.unique(np.concatenate(out).ravel())
    remap = np.full(n, -1)
    remap[used] = np.arange(len(used))
    print(f'  simplified a prop from {total} to {sum(len(t) for t in out)} triangles', flush=True)
    return nv[used], (nuv[used] if nuv is not None else None), [remap[t] for t in out]


def smooth_normals(v: np.ndarray, tris: np.ndarray) -> np.ndarray:
    """Area-weighted normals, shared across UV seams by welding equal positions."""
    _, weld = np.unique(np.round(v, 6), axis=0, return_inverse=True)
    weld = weld.ravel()
    face = np.cross(v[tris[:, 1]] - v[tris[:, 0]], v[tris[:, 2]] - v[tris[:, 0]])
    acc = np.zeros((weld.max() + 1, 3))
    for c in range(3):
        np.add.at(acc, weld[tris[:, c]], face)
    n = acc[weld]
    length = np.linalg.norm(n, axis=1, keepdims=True)
    return np.where(length > 1e-12, n / np.maximum(length, 1e-12), [0, 1, 0])


def build(package: str, snapshot_path: Path | None = None) -> dict:
    path = snapshot_path or latest_snapshot(package)
    data = json.loads(path.read_text())
    fighter = Fighter(package, data)
    fighter.skeleton()
    skins = fighter.skinned_meshes()
    fighter.weapons()
    clips = fighter.animations()
    wanted = set(bound_semantics(package))
    missing = sorted(wanted - set(data['references']))
    fighter.g.json['extras'] = {'package': package, 'snapshot': path.name, 'fps': FPS, 'prefab': data.get('prefab'),
                                'forms': form_parts(data.get('prefab'))}
    out = OUT / f'{package}.glb'
    fighter.g.write(out)
    report = {'package': package, 'glb': str(out.relative_to(REPO)), 'bytes': out.stat().st_size,
              'clips': len(data['references']), 'missingClips': missing, **fighter.validate(skins, clips)}
    # 8-bit skin weights cost 1-2 mm against Unity's bakes; Sir Bonk's prefab space is scaled 3x.
    report['passed'] = report['maxSkinErrorMetres'] < 3e-3 and not missing
    (OUT / f'{package}.report.json').write_text(json.dumps(report, indent=1))
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['snapshot', 'build', 'all'])
    parser.add_argument('packages', nargs='+')
    parser.add_argument('--snapshot', type=Path, help='build from this snapshot instead of the latest')
    args = parser.parse_args(argv)
    for package in args.packages:
        if args.command in ('snapshot', 'all'):
            print(f'{package}: snapshot ->', snapshot(package), flush=True)
        if args.command in ('build', 'all'):
            report = build(package, args.snapshot)
            print(json.dumps({k: v for k, v in report.items() if k != 'clips' or True}, indent=1), flush=True)


if __name__ == '__main__':
    main()
