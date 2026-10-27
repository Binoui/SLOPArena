"""Shared tail segmentation used by the inspection render and the fix.
Works on a welded bmesh in rest (bind) space."""
import heapq
from mathutils import Vector
from mathutils.geometry import intersect_point_line

LEG = ("RightUpLeg", "RightLeg", "RightFoot", "RightToeBase", "RightToe_End",
       "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "LeftToe_End")


def seg_dist(p, segs):
    best = 9.0
    for h, t in segs:
        _, f = intersect_point_line(p, h, t)
        q = h.lerp(t, max(0.0, min(1.0, f)))
        best = min(best, (p - q).length)
    return best


def find_tail(bm, deform, groups, bones, seed_d=0.12, grow_d=0.06, root_back=0.03):
    """bones: {name: (head, tail)} in mesh space. Returns (tail vert set, root boundary verts, dist map)."""
    leg_segs = [bones[n] for n in LEG if n in bones]
    hips_h, hips_t = bones["Hips"]
    fwd = (bones["LeftToeBase"][0] - bones["LeftFoot"][0]); fwd.z = 0; fwd.normalize()

    def dom(v):
        d = v[deform]
        if not d: return None
        return groups[max(d.items(), key=lambda x: x[1])[0]]

    dleg = {v.index: seg_dist(v.co, leg_segs) for v in bm.verts}
    legish = {v.index for v in bm.verts if (dom(v) or "") in LEG}
    seeds = [v for v in bm.verts if v.index in legish and dleg[v.index] > seed_d]
    tail = {v.index for v in seeds}
    stack = list(seeds)
    while stack:
        v = stack.pop()
        for e in v.link_edges:
            o = e.other_vert(v)
            if o.index in tail: continue
            behind = (o.co - hips_h).dot(-fwd) > root_back
            ok = (o.index in legish and dleg[o.index] > grow_d) or (dom(o) == "Hips" and behind and dleg[o.index] > grow_d)
            if ok:
                tail.add(o.index); stack.append(o)
    root = [v for v in bm.verts if v.index in tail and any(e.other_vert(v).index not in tail for e in v.link_edges)]
    return tail, root, dleg


def geodesic(bm, sources, region):
    dist = {v.index: 0.0 for v in sources}
    heap = [(0.0, v.index) for v in sources]
    vs = bm.verts
    while heap:
        d, i = heapq.heappop(heap)
        if d > dist.get(i, 1e9): continue
        v = vs[i]
        for e in v.link_edges:
            o = e.other_vert(v)
            if o.index not in region: continue
            nd = d + e.calc_length()
            if nd < dist.get(o.index, 1e9):
                dist[o.index] = nd; heapq.heappush(heap, (nd, o.index))
    return dist


def find_tail_geo(bm, deform, groups, bones, ds=0.02, min_len=0.2):
    """Walk geodesic level sets from the tail tip; the tail ends where the rings suddenly fill up (the body)."""
    leg_segs = [bones[n] for n in LEG if n in bones]
    def dom(v):
        d = v[deform]
        return groups[max(d.items(), key=lambda x: x[1])[0]] if d else ""
    everything = {v.index for v in bm.verts}
    spine = bones["Spine"][0]
    centre = min(bm.verts, key=lambda v: (v.co - spine).length)
    from_centre = geodesic(bm, [centre], everything)
    cands = [v for v in bm.verts if dom(v) in ("RightUpLeg", "RightLeg", "LeftUpLeg", "LeftLeg") and seg_dist(v.co, leg_segs) > 0.12]
    detached = [v for v in cands if v.index not in from_centre]
    if detached:
        # Manki v4's mesh is a kit of separate parts and the tail is its own island: take the island with the most
        # candidates whole, from the end nearest the hips (root) to the far end (tip).
        islands = []
        for v in detached:
            if not any(v.index in isl for isl in islands): islands.append(geodesic(bm, [v], everything))
        island = max(islands, key=lambda isl: sum(v.index in isl for v in detached))
        hips = bones["Hips"][0]
        root = min((bm.verts[i] for i in island), key=lambda v: (v.co - hips).length)
        from_root = geodesic(bm, [root], everything)
        tip = bm.verts[max(island, key=lambda i: from_root[i])]
        g = geodesic(bm, [tip], everything)
        s_root = max(g.values()) + ds
        return set(g), g, s_root, tip
    tip = max(cands, key=lambda v: from_centre.get(v.index, 0))
    # When the tail end hangs within reach of a leg (Manki v3's club), it is not a candidate and the pick above
    # lands mid-tail; keep climbing away from the spine to the actual end.
    while True:
        nxt = max((e.other_vert(tip) for e in tip.link_edges), key=lambda v: from_centre.get(v.index, 0), default=None)
        if nxt is None or from_centre.get(nxt.index, 0) <= from_centre.get(tip.index, 0):
            break
        tip = nxt
    g = geodesic(bm, [tip], everything)
    bins = {}
    for i, d in g.items(): bins.setdefault(int(d / ds), []).append(bm.verts[i].co)
    stats = []
    for b in range(0, max(bins) + 1):
        pts = bins.get(b, [])
        c = sum(pts, Vector()) / len(pts) if pts else Vector()
        stats.append((len(pts), max((p - c).length for p in pts) if pts else 0.0))

    def body_like(b):
        # Many more vertices than a typical tail ring and wider than the shaft just before it. Width is compared
        # with the last few rings, not everything since the tip: a club-shaped tip tuft (Manki v3) has wide rings.
        counts = [n for n, _ in stats[2:b]]
        if not counts: return False
        recent = sorted(e for _, e in stats[max(2, b - 5):b])
        mc = sorted(counts)[len(counts) // 2]; me = recent[len(recent) // 2]
        return stats[b][0] > 3 * max(mc, 2) and stats[b][1] > 1.8 * me

    s_root = None
    for b in range(0, len(stats) - 2):
        # three rings in a row must agree, so a thick section of the tail is not mistaken for the body
        if b * ds >= min_len and all(body_like(b + k) for k in range(3)):
            s_root = b * ds; break
    tail = {i for i, d in g.items() if d < s_root}
    return tail, g, s_root, tip
