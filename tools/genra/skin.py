"""Skinned mesh, body regions and clearance queries (UsdSkel linear blend skinning)."""
from __future__ import annotations

from functools import cached_property

import numpy as np
from pxr import Gf, Usd, UsdGeom, UsdSkel, Vt
from scipy.spatial import cKDTree

from .rig import Rig

# Region pairs worth checking; anatomically adjacent pairs (hand-forearm...) are skipped.
DEFAULT_PAIRS = [('LeftHand', 'Head'), ('RightHand', 'Head'), ('LeftForeArm', 'Head'), ('RightForeArm', 'Head'),
                 ('LeftHand', 'Torso'), ('RightHand', 'Torso'), ('LeftHand', 'RightHand'),
                 ('LeftHand', 'LeftLeg'), ('LeftHand', 'RightLeg'), ('RightHand', 'LeftLeg'), ('RightHand', 'RightLeg'),
                 ('LeftLeg', 'RightLeg'), ('LeftFoot', 'RightFoot'), ('LeftFoot', 'Torso'), ('RightFoot', 'Torso'),
                 ('LeftHand', 'LeftFoot'), ('RightHand', 'RightFoot'),
                 ('Weapon', 'Head'), ('Weapon', 'Torso'), ('Weapon', 'LeftLeg'), ('Weapon', 'RightLeg'), ('Weapon', 'LeftHand')]


def region_of(rig: Rig) -> dict:
    """Joint index -> body region; unlabelled joints (helpers) take their nearest labelled ancestor's region."""
    labels = {}

    def assign(name, joints):
        for j in joints:
            labels[j] = name

    present = lambda *names: [rig.index(n) for n in names if n in rig.names]
    assign('Torso', present('Hips', 'Spine', 'Spine1', 'Spine2', 'LeftShoulder', 'RightShoulder'))
    if 'Neck' in rig.names:
        assign('Head', rig.descendants('Neck'))
    for side in ('Left', 'Right'):
        assign(side + 'UpperArm', present(side + 'Arm'))
        assign(side + 'ForeArm', present(side + 'ForeArm'))
        if side + 'Hand' in rig.names:
            assign(side + 'Hand', rig.descendants(side + 'Hand'))
        assign(side + 'Leg', present(side + 'UpLeg', side + 'Leg'))
        if side + 'Foot' in rig.names:
            assign(side + 'Foot', rig.descendants(side + 'Foot'))
    for j, name in enumerate(rig.names):
        if name.startswith('Weapon'):
            assign('Weapon', rig.descendants(j))
        elif name.lower().startswith('tail') and rig.parents[j] >= 0 and not rig.names[rig.parents[j]].lower().startswith('tail'):
            assign('Tail', rig.descendants(j))
    regions = {}
    for j in range(len(rig.names)):
        k = j
        while k >= 0 and k not in labels:
            k = int(rig.parents[k])
        regions[j] = labels.get(k, 'Torso')
    return regions


class Skin:
    def __init__(self, rig: Rig):
        self.rig = rig
        cache = UsdSkel.Cache()
        self.meshes = []
        self.influenced = set()  # joints that deform at least one vertex
        for prim in rig.stage.Traverse():
            if not prim.IsA(UsdSkel.Root):
                continue
            root = UsdSkel.Root(prim)
            cache.Populate(root, Usd.PrimDefaultPredicate)
            for binding in cache.ComputeSkelBindings(root, Usd.PrimDefaultPredicate):
                for target in binding.GetSkinningTargets():
                    mesh = UsdGeom.Mesh(target.GetPrim())
                    points = mesh.GetPointsAttr().Get(0)
                    counts = np.array(mesh.GetFaceVertexCountsAttr().Get())
                    indices = np.array(mesh.GetFaceVertexIndicesAttr().Get())
                    joint_idx, joint_w = target.ComputeJointInfluences()
                    k = target.GetNumInfluencesPerComponent()
                    joint_idx = np.array(joint_idx).reshape(-1, k)
                    joint_w = np.array(joint_w).reshape(-1, k)
                    if len(joint_idx) == 1:  # constant influence
                        joint_idx = np.repeat(joint_idx, len(points), 0)
                        joint_w = np.repeat(joint_w, len(points), 0)
                    self.influenced.update(int(j) for j in np.unique(joint_idx[joint_w > 0.01]))
                    self.meshes.append({'name': target.GetPrim().GetName(), 'query': target, 'points': points,
                                        'triangles': _triangulate(counts, indices),
                                        'dominant': joint_idx[np.arange(len(joint_idx)), joint_w.argmax(1)]})
        regions = region_of(rig)
        names = sorted(set(regions.values()))
        self.region_names = names
        for m in self.meshes:
            m['region'] = np.array([names.index(regions.get(int(j), 'Torso')) for j in m['dominant']])

    def points(self, world) -> list[np.ndarray]:
        """World-space skinned vertices per mesh for world joint transforms (N, 4, 4)."""
        rig = self.rig
        skel = world @ np.linalg.inv(rig.skel_to_world)
        xforms = Vt.Matrix4dArray([Gf.Matrix4d(*m.ravel().tolist()) for m in np.linalg.inv(rig.bind_skel) @ skel])
        out = []
        for m in self.meshes:
            pts = Vt.Vec3fArray(m['points'])
            m['query'].ComputeSkinnedPoints(xforms, pts, Usd.TimeCode.Default())
            p = np.array(pts, float)
            out.append(np.c_[p, np.ones(len(p))] @ rig.skel_to_world[:, :3])
        return out

    @cached_property
    def triangles(self) -> list[np.ndarray]:
        return [m['triangles'] for m in self.meshes]

    def regions(self, world) -> dict[str, np.ndarray]:
        """Region name -> world vertices (all meshes) for one pose."""
        pts = self.points(world)
        out = {}
        for m, p in zip(self.meshes, pts):
            for i, name in enumerate(self.region_names):
                sel = m['region'] == i
                if sel.any():
                    out.setdefault(name, []).append(p[sel])
        return {k: np.concatenate(v) for k, v in out.items()}

    def clearances(self, world, pairs=DEFAULT_PAIRS) -> dict[str, float]:
        """Minimum vertex distance in metres between body regions (0-1 cm usually means touching/intersecting)."""
        reg = self.regions(world)
        trees = {}
        out = {}
        for a, b in pairs:
            if a in reg and b in reg:
                tree = trees.setdefault(b, cKDTree(reg[b]))
                out[f'{a}-{b}'] = float(tree.query(reg[a])[0].min())
        return out


def _triangulate(counts, indices) -> np.ndarray:
    tris, start = [], 0
    for c in counts:
        face = indices[start:start + c]
        for i in range(1, c - 1):
            tris.append((face[0], face[i], face[i + 1]))
        start += c
    return np.array(tris, dtype=np.int64)
