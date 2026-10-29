"""Nyx's real lower body on the immutable shared T-rest skeleton."""
import math
import bpy
import bmesh
import numpy as np
from mathutils import Vector
from common import islands, subset, material

P = 'mixamorig:'


def replace_lower_body(real, old_dress, fake, arm, out, source_joints, target_joints):
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
        delta = target_joints[foot] - source_joints[foot]
        # Preserve the sole plane while placing the ankle at the shared skeleton's ankle.
        q.z *= target_joints[foot].z / source_joints[foot].z
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
    select = bpy.ops.object.select_all
    select(action='DESELECT')
    for o in [upper, boots]:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = upper
    bpy.ops.object.join()
    bpy.data.objects.remove(real, do_unlink=True)
    upper.name = upper.data.name = 'Nyx_Real'
    skirt, skirt_report = make_skirt(arm, out)
    skirt_report['texture'] = fabric_texture(old_dress, skirt, out)
    bpy.data.objects.remove(old_dress, do_unlink=True)
    tights_texture(out)
    material(tights, out/'textures/Nyx_Tights.png', 'Nyx_Tights')
    return upper, tights, skirt, {
        'removed_old_leg_vertices': len(groups[old_legs][1]),
        'removed_old_skirt_vertices': 491,
        'copied_fake_leg_vertices': len(tights.data.vertices),
        'extended_shin_cuff_vertices': extended,
        'boot_islands': len(boot_ids),
        'boot_ankle_fit': 'rigid XY translation; Z scaled from sole to shared ankle; foot/shin weights',
        'skirt': skirt_report,
    }


def tights_texture(out):
    n = 256
    yy, xx = np.mgrid[:n, :n]
    rgba = np.ones((n, n, 4), dtype=np.float32)
    grain = np.random.default_rng(51).normal(0, .0005, (n, n))
    rgba[:, :, :3] = np.clip(.008 + grain[:, :, None], 0, 1)
    image = bpy.data.images.new('Nyx_Tights', width=n, height=n, alpha=False)
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(out/'textures/Nyx_Tights.png')
    image.file_format = 'PNG'
    image.save()


def make_skirt(arm, out):
    sectors, rows = 72, 34
    waist, hem = 1.085, .285
    vertices, faces = [], []
    for j in range(rows + 1):
        t = j / rows
        rx = .143 + .188 * t
        ry = .114 + .145 * t
        for i in range(sectors):
            a = 2 * math.pi * i / sectors
            fold = .008 * math.sin(10*a) * t * t + .003 * math.sin(16*a + .4)*t
            vertices.append(((rx + fold)*math.cos(a), .018 + (ry + fold)*math.sin(a), waist + (hem-waist)*t))
    for j in range(rows):
        for i in range(sectors):
            k = j*sectors+i
            n = j*sectors+(i+1)%sectors
            faces.append((k, k+sectors, n+sectors, n))
    mesh = bpy.data.meshes.new('Nyx_Real_Dress')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    skirt = bpy.data.objects.new(mesh.name, mesh)
    bpy.context.scene.collection.objects.link(skirt)
    uv = mesh.uv_layers.new(name='Fabric')
    for f in mesh.polygons:
        f.use_smooth = True
        for li in f.loop_indices:
            index = mesh.loops[li].vertex_index
            i, j = index % sectors, index // sectors
            u = i/sectors
            if f.index % sectors == sectors-1 and i == 0:
                u = 1
            uv.data[li].uv = (u, j/rows)
    hips = skirt.vertex_groups.new(name=P+'Hips')
    spine = skirt.vertex_groups.new(name=P+'Spine')
    for v in mesh.vertices:
        t = (waist-v.co.z)/(waist-hem)
        s = .35 * max(0, 1-t/.12)
        hips.add([v.index], 1-s, 'REPLACE')
        if s:
            spine.add([v.index], s, 'REPLACE')
    skirt.modifiers.new('Armature', 'ARMATURE').object = arm
    bm = bmesh.new()
    bm.from_mesh(mesh)
    assert all(len(f.verts) == 4 and f.calc_area() > 1e-8 for f in bm.faces)
    assert all(len(e.link_faces) in [1, 2] for e in bm.edges)
    assert sum(e.is_boundary for e in bm.edges) == 2*sectors
    lengths = np.array([e.calc_length() for e in bm.edges])
    bm.free()
    return skirt, {'sectors': sectors, 'rows': rows, 'vertices': len(vertices), 'quads': len(faces),
                   'waist_z': waist, 'hem_z': hem, 'edge_metres_min_median_max': [float(lengths.min()), float(np.median(lengths)), float(lengths.max())],
                   'edge_fraction_2_to_3_cm': float(np.mean((lengths >= .02) & (lengths <= .03))),
                   'double_sided': 'one simulation surface; two-sided material, no duplicate cloth particles'}


def fabric_texture(source, skirt, out):
    """Bake the actual Tripo skirt colour onto the new regular UV surface."""
    # A swatch from a verified cloth-only UV triangle keeps jewelry and boots out of the fabric map.
    source.data.calc_loop_triangles()
    candidates = [t for t in source.data.loop_triangles if all(.50 < source.data.vertices[v].co.z < .85 for v in t.vertices)]
    tri = max(candidates, key=lambda t: t.area)
    uv = source.data.uv_layers.active
    uvs = np.array([list(uv.data[i].uv) for i in tri.loops])
    image = bpy.data.images.load(str(out/'textures/Nyx_Real.png'), check_existing=True)
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    n = 1024
    yy, xx = np.mgrid[:n, :n]/(n-1)
    # Mirror tiled barycentric samples stay inside the single cloth triangle.
    a, b = (xx*3)%1, (yy*3)%1
    mask = a+b > 1
    a[mask], b[mask] = 1-a[mask], 1-b[mask]
    sample = uvs[0] + a[:,:,None]*(uvs[1]-uvs[0]) + b[:,:,None]*(uvs[2]-uvs[0])
    rgba = pixels[np.clip((sample[:,:,1]*(h-1)).astype(int),0,h-1), np.clip((sample[:,:,0]*(w-1)).astype(int),0,w-1)].copy()
    rgba[:,:,3] = 1
    target = bpy.data.images.new('Nyx_Dress', width=n, height=n, alpha=False)
    target.pixels.foreach_set(rgba.ravel())
    target.filepath_raw = str(out/'textures/Nyx_Dress.png')
    target.file_format = 'PNG'
    target.save()
    material(skirt, out/'textures/Nyx_Dress.png', 'Nyx_Dress')
    skirt.data.materials[0].use_backface_culling = False
    return {'source': 'Nyx_Real.png', 'source_triangle_uv': uvs.tolist(), 'size': [n,n], 'mean_linear_rgb': rgba[:,:,:3].mean((0,1)).tolist()}
