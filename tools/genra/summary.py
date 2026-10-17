"""Compact rig facts for the authoring prompt, so the model need not explore the rig."""
from __future__ import annotations

import numpy as np

from .brief import Brief
from .skin import Skin


def group_of(name: str) -> str:
    if name.lower().startswith('tail'):
        return 'tail'
    if name.startswith('Weapon') or name.startswith('blade'):
        return 'weapon'
    for side in ('Left', 'Right'):
        if name.startswith(side + 'Hand') and name != side + 'Hand':
            return side.lower() + ' fingers'
        if any(name.startswith(side + x) for x in ('Shoulder', 'Arm', 'ForeArm', 'Hand')):
            return side.lower() + ' arm'
        if any(name.startswith(side + x) for x in ('UpLeg', 'Leg', 'Foot', 'Toe')):
            return side.lower() + ' leg'
    return 'spine and head'


def rig_summary(brief: Brief, skin: Skin | None = None) -> str:
    rig, entry = brief.rig, brief.entry_pose
    skin = skin or Skin(rig)
    groups = {}
    for j, name in enumerate(rig.names):
        groups.setdefault(group_of(name), []).append(name + ('' if j in skin.influenced else '*'))
    lines = [f'{len(rig.names)} joints, {rig.fps:g} ticks/s. Joints marked * deform no vertex in this rig pack '
             '(renders, clearances and skinned previews cannot show them; the in-game prefab may still use them).']
    lines += [f'- {g}: {" ".join(names)}' for g, names in groups.items()]
    lines.append('Hinges (set_hinge / checks): ' + ', '.join(n for n in rig.hinges if 'Index' not in n and 'Middle' not in n
                                                         and 'Ring' not in n and 'Little' not in n)
                 + ', and <Side><Finger>Knuckle|Middle for Index, Middle, Ring, Little.')

    def length(a, b):
        return np.linalg.norm(rig.bind_positions[rig.index(b)] - rig.bind_positions[rig.index(a)])

    lines.append('Bone lengths (m): ' + ', '.join(f'{k} {length(a, b):.3f}' for k, a, b in [
        ('upper arm', 'LeftArm', 'LeftForeArm'), ('forearm', 'LeftForeArm', 'LeftHand'),
        ('hand to knuckles', 'LeftHand', 'LeftHandMiddle1'), ('thigh', 'LeftUpLeg', 'LeftLeg'),
        ('shin', 'LeftLeg', 'LeftFoot'), ('hips to chest', 'Hips', 'Spine2'), ('chest to head', 'Spine2', 'Head')]
        if a in rig.names and b in rig.names))
    weapons = [n for n in rig.names if n.startswith('Weapon') and n[6:].isdigit()]
    if weapons:
        lines.append('Weapons (fixed joints under their bone, with anchor child joints): '
                     + ', '.join(f"{w} under {rig.names[rig.parents[rig.index(w)]]} -> "
                                 + ' '.join(rig.names[c] for c in rig.children(w)) for w in weapons))
    lines.append('Entry pose, character frame [right, front, up] m from the entry hips:')
    for name, p in entry.describe(['Hips', 'Spine2', 'Neck', 'Head', 'LeftArm', 'LeftForeArm', 'LeftHand', 'RightArm',
                                   'RightForeArm', 'RightHand', 'LeftLeg', 'LeftFoot', 'RightLeg', 'RightFoot']).items():
        lines.append(f'  {name:12s} {p}')
    world = entry.world()
    flex = {n: rig.hinge_state(world, n)[0] for n in ('LeftKnee', 'RightKnee', 'LeftElbow', 'RightElbow')}
    lines.append('Entry hinge flexion (deg): ' + ', '.join(f'{k} {v:.0f}' for k, v in flex.items()))
    lines.append('Palm normals at entry: ' + ', '.join(f'{s} {np.round(entry.palm_normal(s), 2).tolist()}' for s in ('Left', 'Right')))
    for name, rel in brief['references'].items():
        clip = brief.reference(name)
        lines.append(f'Reference "{name}": ticks 0..{len(clip) - 1} (b.reference("{name}"))')
    return '\n'.join(lines)
