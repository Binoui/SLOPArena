"""Rebuild thumb/index/middle/ring chains when Mixamo rigged fewer fingers (used by fix_rig.py).

Mixamo sometimes misses fingers that are there: v5's first download had only the index chain, with all three
fingertips and the thumb skinned to Hand/HandIndex*. Manki's hand is a fingerless glove (the thumb is part of the
glove) with each fingertip its own mesh island. Per hand this:
- takes the three fingertip islands skinned to the hand (the ones reaching farthest out), orders them from the thumb side: index, middle, ring
- finds the thumb: glove vertices beyond the wrist that stick out past the index side, weighted with a soft edge
- builds Mixamo-named chains <Side>Hand<Finger>1..4 under the hand (the existing index chain is moved onto its finger)
- gives each fingertip island and the thumb to its chain by position along it; every other hand vertex goes to Hand
Works in the rest mesh space of fix_rig.py (after its step 0).
"""
import bmesh
import numpy as np
from mathutils import Vector

FR = (0.0, 0.38, 0.69, 1.0, 1.18)  # joint positions along base -> tip; the 4th bone is the Mixamo end bone


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def islands(bm):
    owner, out = {}, []
    for v in bm.verts:
        if v.index in owner: continue
        stack, piece = [v], [v.index]; owner[v.index] = len(out)
        while stack:
            x = stack.pop()
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in owner: owner[y.index] = len(out); stack.append(y); piece.append(y.index)
        out.append(piece)
    return owner, out


def pca_axis(pts, hint):
    a = np.array([p[:] for p in pts]); c = a.mean(0)
    w, v = np.linalg.eigh(np.cov((a - c).T))
    d = Vector(v[:, 2])
    return Vector(c), (d if d.dot(hint) > 0 else -d)


def segment_weights(s):
    """Blend over the three deforming bones by position s (0 base .. 1 tip)."""
    out = []
    for k, (a, b) in enumerate(((-1, FR[1]), (FR[1], FR[2]), (FR[2], 9))):
        lo = smoothstep(a - 0.06, a + 0.06, s) if a > 0 else 1.0
        hi = 1 - smoothstep(b - 0.06, b + 0.06, s) if b < 9 else 1.0
        if lo * hi > 0: out.append((k, lo * hi))
    return out


def rebuild(bpy, arm, body, P="mixamorig:", finger_base_back=0.3, thumb_out=0.012):
    """Returns a report; skips (with a reason) a hand that does not look like Manki's."""
    if P + "LeftHandThumb1" in arm.data.bones:
        return {"skipped": "thumb chain present"}
    me = body.data
    Mi = body.matrix_world.inverted() @ arm.matrix_world      # armature space -> mesh space
    Ai = Mi.inverted()
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    owner, pieces = islands(bm)
    co = [v.co.copy() for v in bm.verts]
    names = {g.index: g.name for g in body.vertex_groups}
    rep = {}
    for side in ("Left", "Right"):
        names = {g.index: g.name for g in body.vertex_groups}  # the first hand adds groups
        hb = {P + side + "Hand"} | {P + side + f"HandIndex{k}" for k in range(1, 5)}
        hand = Mi @ arm.data.bones[P + side + "Hand"].head_local
        idx1 = Mi @ arm.data.bones[P + side + "HandIndex1"].head_local
        a = (idx1 - hand).normalized()
        share = {}
        for v in me.vertices:
            s = sum(g.weight for g in v.groups if names[g.group] in hb)
            if s > 0: share[v.index] = s
        # fingertip islands: small, wholly hand-skinned, out beyond the wrist
        tips = [p for p in pieces if len(p) < 1500 and all(i in share for i in p)
                and (sum((co[i] for i in p), Vector()) / len(p) - hand).dot(a) > 0.02]
        # studs on v5's scorched gauntlet are hand-skinned islands too: keep the three that reach farthest out
        tips = sorted(tips, key=lambda p: -max((co[i] - hand).dot(a) for i in p))[:3] if len(tips) > 3 else tips
        if len(tips) != 3:
            rep[side] = {"skipped": f"{len(tips)} fingertip islands, expected 3"}; continue
        cents = [sum((co[i] for i in p), Vector()) / len(p) for p in tips]
        mid = sum(cents, Vector()) / 3
        _, lat = pca_axis(cents, cents[0] - cents[2])
        lat = (lat - a * lat.dot(a)).normalized()
        lo = min((c - mid).dot(lat) for c in cents); hi = max((c - mid).dot(lat) for c in cents)
        in_tip = {i for p in tips for i in p}
        glove = [i for i in share if i not in in_tip and (co[i] - hand).dot(a) > 0.01]
        below = [i for i in glove if (co[i] - mid).dot(lat) < lo - thumb_out]
        above = [i for i in glove if (co[i] - mid).dot(lat) > hi + thumb_out]
        thumb = below if len(below) > len(above) else above
        if len(thumb) < 20:
            rep[side] = {"skipped": f"thumb not found ({len(thumb)} verts)"}; continue
        sign = -1 if thumb is below else 1
        # index first: the fingertip nearest the thumb's end of the row
        order = sorted(range(3), key=lambda k: abs((cents[k] - mid).dot(lat) - (lo if sign < 0 else hi)))
        chains = {}
        for f, k in zip(("Index", "Middle", "Ring"), order):
            c, d = pca_axis([co[i] for i in tips[k]], a)
            proj = [(co[i] - c).dot(d) for i in tips[k]]
            L = max(proj) - min(proj)
            base = c + d * (min(proj) - finger_base_back * L); tip = c + d * max(proj)
            chains[f] = (base, tip, tips[k])
        tc, td = pca_axis([co[i] for i in thumb], (sum((co[i] for i in thumb), Vector()) / len(thumb) - hand))
        proj = [(co[i] - tc).dot(td) for i in thumb]
        # The thumb is part of the glove, so its weight fades in across the glove (a hard set tore shards out of the
        # glove at the set's edge when the thumb curled): full past thumb_out beyond the finger row, none at the row's
        # edge, and none at the wrist.
        edge = lo if sign < 0 else hi
        member = {}
        for i in glove:
            m = smoothstep(0.0, thumb_out, sign * ((co[i] - mid).dot(lat) - edge))
            m *= smoothstep(0.01, 0.03, (co[i] - hand).dot(a))
            if m > 0: member[i] = m
        chains["Thumb"] = (tc + td * (min(proj) - 0.01), tc + td * max(proj), list(member))
        # bones (Mixamo names; 1-3 deform, 4 is the end bone)
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = arm; arm.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        eb = arm.data.edit_bones
        normal = Ai.to_3x3() @ a.cross(lat).normalized()
        for f, (base, tip, _) in chains.items():
            prev = eb[P + side + "Hand"]
            for k in range(4):
                n = P + side + f"Hand{f}{k + 1}"
                b = eb[n] if n in eb else eb.new(n)
                b.head, b.tail = Ai @ base.lerp(tip, FR[k]), Ai @ base.lerp(tip, FR[k + 1])
                b.parent = prev; b.use_connect = k > 0; b.use_deform = k < 3
                b.align_roll(normal)
                prev = b
        bpy.ops.object.mode_set(mode="OBJECT")
        def grp(n):
            return body.vertex_groups.get(n) or body.vertex_groups.new(name=n)
        moved = {}
        assigned = {}
        for f, (base, tip, verts) in chains.items():
            ax = tip - base; L2 = ax.length_squared
            for i in verts:
                s = max(0.0, min(1.0, (co[i] - base).dot(ax) / L2))
                w = {}
                ease = member[i] * smoothstep(0.0, 0.25, s) if f == "Thumb" else 1.0  # the thumb fades into the glove
                for k, ww in segment_weights(s):
                    w[P + side + f"Hand{f}{k + 1}"] = ww * ease
                w[P + side + "Hand"] = max(0.0, 1 - sum(w.values()))
                assigned[i] = w
            moved[f] = len(verts)
        for i, s in share.items():
            v = me.vertices[i]
            for g in list(v.groups):
                if names[g.group] in hb: body.vertex_groups[g.group].remove([i])
            for n, w in assigned.get(i, {P + side + "Hand": 1.0}).items():
                if w * s > 1e-4: grp(n).add([i], w * s, "ADD")
        rep[side] = {"verts": moved, "tip_islands": [len(p) for p in tips]}
    return rep
