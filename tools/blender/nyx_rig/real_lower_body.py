"""Nyx's real lower body on the immutable shared T-rest skeleton."""
import math
import bpy
import bmesh
import numpy as np
from common import islands, subset, material, select
from mathutils import Vector

P = 'mixamorig:'


def replace_lower_body(real, old_dress, fake, arm, out, source_joints):
    groups = islands(real)
    lower = [i for i, (_, vs) in enumerate(groups) if max(real.data.vertices[v].co.z for v in vs) < 1.06]
    old_legs = next(i for i in lower if len(groups[i][1]) == 507)
    boot_ids = [i for i in lower if i != old_legs]
    upper = subset(real, [f for i, (fs, _) in enumerate(groups) if i not in lower for f in fs], 'Nyx_Real_Upper')
    boots = subset(real, [f for i in boot_ids for f in groups[i][0]], 'Nyx_Real_Boots')
    # Recover each retained boot vertex from the unfitted source using its UV and source position.
    # The caller stores source coordinates before the inverse skin solve, so no old limb fitting survives.
    boot_attr = boots.data.attributes['nyx_source_position']
    for v in boots.data.vertices:
        q = boot_attr.data[v.index].vector.copy()
        side = 'Left' if q.x > 0 else 'Right'
        foot = P + side + 'Foot'
        target = arm.data.bones[foot].head_local
        delta = target - source_joints[foot]
        # Preserve the sole plane while placing the ankle at the shared skeleton's ankle.
        q.z += delta.z * min(1, max(0, q.z/source_joints[foot].z))
        q.x += delta.x
        q.y += delta.y
        v.co = q
        for g in list(v.groups):
            boots.vertex_groups[g.group].remove([v.index])
        bone = P + side + ('Foot' if q.z < .23 else 'Leg')
        group = boots.vertex_groups.get(bone) or boots.vertex_groups.new(name=bone)
        group.add([v.index], 1, 'REPLACE')
    fg = islands(fake)
    tights = subset(fake, fg[3][0], 'Nyx_Real_Tights')
    assert len(tights.data.vertices) == 1144
    # The fake boot starts higher. Extend only its copied concealed shin cuff into the real boot.
    extended = 0
    for v in tights.data.vertices:
        if v.co.z < .50:
            v.co.z -= .075 * max(0, min(1, (.50 - v.co.z) / .06))
            extended += 1
    # The copied fake hips are wider than the real dress. End the tights inside it.
    original_tights_vertices = len(tights.data.vertices)
    retained_faces = [face.index for face in tights.data.polygons
                      if all(tights.data.vertices[i].co.z <= .84 for i in face.vertices)]
    trimmed = subset(tights, retained_faces, 'Nyx_Real_Tights_Trimmed')
    bpy.data.objects.remove(tights, do_unlink=True)
    tights = trimmed
    tights.name = tights.data.name = 'Nyx_Real_Tights'
    tapered = 0
    for vertex in tights.data.vertices:
        t = min(1, max(0, (vertex.co.z - .70) / .14))
        if t > 0:
            scale = 1 - .28 * t * t * (3 - 2*t)
            vertex.co.x *= scale
            vertex.co.y = -.01 + (vertex.co.y + .01) * scale
            tapered += 1
    select = bpy.ops.object.select_all
    select(action='DESELECT')
    for o in [upper, boots]:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = upper
    bpy.ops.object.join()
    bpy.data.objects.remove(real, do_unlink=True)
    upper.name = upper.data.name = 'Nyx_Real'
    skirt, skirt_report = make_skirt(old_dress, arm, out)
    skirt.name = skirt.data.name = 'Nyx_Real_Dress'
    # Close the original gap below the belt without changing the bell or chain rests.
    waist_vertices = 0
    for vertex in skirt.data.vertices:
        t = min(1, max(0, (vertex.co.z - 1.00) / .09))
        if t > 0:
            blend = t * t * (3 - 2*t)
            vertex.co.z += .014 * blend
            vertex.co.y += (.12 + .33 * vertex.co.y) * blend
            waist_vertices += 1
    final_skirt_points = np.array([list(vertex.co) for vertex in skirt.data.vertices])
    skirt_report['bounds_before_waist_repair'] = skirt_report['bounds_after']
    skirt_report['bounds_after'] = [final_skirt_points.min(0).tolist(), final_skirt_points.max(0).tolist()]
    skirt_report['waist_z_repaired'] = float(final_skirt_points[:, 2].max())
    skirt_report['waist_overlap'] = {
        'starts_z': 1.00, 'full_z': 1.09, 'maximum_raise_m': .014, 'waist_y_scale': 1.33, 'waist_y_offset_m': .12,
        'changed_vertices': waist_vertices, 'weights_and_chain_rests_unchanged': True,
    }
    tights_texture(out)
    material(tights, out/'textures/Nyx_Tights.png', 'Nyx_Tights')
    return upper, tights, skirt, {
        'removed_old_leg_vertices': len(groups[old_legs][1]),
        'removed_old_skirt_vertices': 491,
        'copied_fake_leg_vertices': len(tights.data.vertices),
        'extended_shin_cuff_vertices': extended,
        'tights_waist_repair': {
            'source_vertices': original_tights_vertices,
            'retained_vertices': len(tights.data.vertices),
            'cutoff_z': .84, 'taper_start_z': .70, 'maximum_inward_scale': .72,
            'tapered_vertices': tapered, 'retained_weights_unchanged': True,
        },
        'boot_islands': len(boot_ids),
        'boot_ankle_fit': 'rigid XY translation; Z scaled from sole to shared ankle; foot/shin weights',
        'skirt': skirt_report,
    }


def tights_texture(out):
    n = 256
    rgba = np.ones((n, n, 4), dtype=np.float32)
    grain = np.random.default_rng(51).normal(0, .0005, (n, n))
    rgba[:, :, :3] = np.clip(.008 + grain[:, :, None], 0, 1)
    image = bpy.data.images.new('Nyx_Tights', width=n, height=n, alpha=False)
    image.colorspace_settings.name = 'sRGB'
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(out/'textures/Nyx_Tights.png')
    image.file_format = 'PNG'
    image.save()


def make_skirt(skirt, arm, out):
    """Refine the approved Tripo surface, retaining its folds, bell profile and atlas UVs."""
    source = skirt.data.attributes['nyx_source_position']
    for vertex, entry in zip(skirt.data.vertices, source.data):
        vertex.co = entry.vector
    original = np.array([list(v.co) for v in skirt.data.vertices])
    skirt.vertex_groups.clear()
    skirt.modifiers.clear()
    select([skirt])
    # Simple subdivision preserves the source triangles and interpolates their exact atlas UVs.
    sub = skirt.modifiers.new('Even fabric tessellation', 'SUBSURF')
    sub.subdivision_type = 'SIMPLE'
    sub.levels = 2
    bpy.ops.object.modifier_apply(modifier=sub.name)
    smooth = skirt.modifiers.new('Soften coarse source folds', 'SMOOTH')
    smooth.factor = .35
    smooth.iterations = 4
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    for face in skirt.data.polygons:
        face.use_smooth = True
    material(skirt, out/'textures/Nyx_Real.png', 'Nyx_Dress')
    skirt.data.materials[0].use_backface_culling = False
    points = np.array([list(v.co) for v in skirt.data.vertices])
    waist, hem = float(original[:, 2].max()), float(original[:, 2].min())
    center_y = .018
    angles = np.mod(np.arctan2(points[:, 1]-center_y, points[:, 0]), 2*math.pi)
    chain_count, segments = 12, 4
    chain_points = []
    for c in range(chain_count):
        angle = 2*math.pi*c/chain_count
        delta = np.abs(np.angle(np.exp(1j*(angles-angle))))
        chain = []
        for t in np.linspace(0, 1, segments+1):
            z = waist+(hem-waist)*t
            sector = np.where(delta < math.pi/chain_count*.9)[0]
            near = sector[np.argsort(np.abs(points[sector, 2]-z))[:80]]
            radial = points[near, 0]*math.cos(angle)+(points[near, 1]-center_y)*math.sin(angle)
            radius = float(np.median(radial))
            chain.append(Vector((radius*math.cos(angle), center_y+radius*math.sin(angle), z)))
        chain_points.append(chain)
    select([arm])
    bpy.ops.object.mode_set(mode='EDIT')
    for c, chain in enumerate(chain_points):
        parent = arm.data.edit_bones[P+'Hips']
        for j in range(segments):
            bone = arm.data.edit_bones.new(f'Nyx_Skirt_{c:02d}_{j:02d}')
            bone.head, bone.tail = chain[j], chain[j+1]
            bone.parent = parent
            bone.use_connect = j > 0
            parent = bone
    bpy.ops.object.mode_set(mode='OBJECT')
    hips = skirt.vertex_groups.new(name=P+'Hips')
    groups = {(c,j):skirt.vertex_groups.new(name=f'Nyx_Skirt_{c:02d}_{j:02d}')
              for c in range(chain_count) for j in range(segments)}
    for v, angle in zip(skirt.data.vertices, angles):
        t = min(1, max(0, (waist-v.co.z)/(waist-hem)))
        u = angle*chain_count/(2*math.pi)
        c = int(u)%chain_count
        around = u-int(u)
        around = around*around*(3-2*around)
        level = min(segments-1, t*segments)
        j = int(level)
        down = level-j
        pin = max(0, 1-t/.12)
        pin = pin*pin*(3-2*pin)
        weights = {}
        if pin > 0:
            weights[hips] = pin
            for k, f in [(c,1-around),((c+1)%chain_count,around)]:
                weights[groups[k,0]] = (1-pin)*f
        else:
            for k, f in [(c,1-around),((c+1)%chain_count,around)]:
                for l,g in [(j,1-down),(min(j+1,segments-1),down)]:
                    group = groups[k,l]
                    weights[group] = weights.get(group,0)+f*g
        total = sum(weights.values())
        for group, weight in weights.items():
            if weight > 1e-7:
                group.add([v.index],weight/total,'REPLACE')
    skirt.modifiers.new('Armature','ARMATURE').object = arm
    bm = bmesh.new(); bm.from_mesh(skirt.data)
    lengths = np.array([e.calc_length() for e in bm.edges])
    assert all(f.calc_area() > 1e-10 for f in bm.faces)
    assert not any(len(e.link_faces) > 2 for e in bm.edges)
    boundary = sum(e.is_boundary for e in bm.edges)
    bm.free()
    return skirt, {
        'source': 'approved Tripo dress, original coordinates and original atlas UVs',
        'source_vertices': len(original), 'vertices':len(points), 'faces':len(skirt.data.polygons),
        'waist_z':waist, 'hem_z':hem, 'bounds_before':[original.min(0).tolist(),original.max(0).tolist()],
        'bounds_after':[points.min(0).tolist(),points.max(0).tolist()],
        'chains':chain_count, 'segments':segments, 'added_leaf_bones':chain_count*segments,
        'boundary_edges':boundary, 'edge_metres_min_median_max':[float(lengths.min()),float(np.median(lengths)),float(lengths.max())],
        'texture':'original Nyx_Real.png atlas, unchanged UV interpolation',
        'chain_points':[[list(v) for v in chain] for chain in chain_points],
    }
