"""Split the stowed pole and add a right-palm phone without changing existing bones."""
import bpy
import numpy as np
from mathutils import Vector, Matrix
from common import islands, subset, select, material


def add_props(arm, backpack, meshes, out):
    groups = islands(backpack)
    pole_ids = {1, 2, 13, 23}
    assert [len(groups[i][1]) for i in sorted(pole_ids)] == [193, 208, 49, 24]
    stowed = subset(backpack, [f for i in sorted(pole_ids) for f in groups[i][0]], 'Nyx_BackpackPole')
    bag = subset(backpack, [f for i, (faces, _) in enumerate(groups) if i not in pole_ids for f in faces], 'Nyx_BackpackBody')
    meshes.remove(backpack)
    bpy.data.objects.remove(backpack, do_unlink=True)
    bag.name = bag.data.name = 'Nyx_Backpack'
    meshes.extend([bag, stowed])

    hand = arm.data.bones['mixamorig:RightHand']
    middle = arm.data.bones['mixamorig:RightHandMiddle1']
    length = (middle.head_local - hand.head_local).normalized()
    width = Vector((0, 1, 0))
    normal = width.cross(length).normalized()
    # The supplied hand is open. Seat the screen just below the palm, with its long axis along the fingers.
    center = middle.head_local - length * .010 - normal * .027
    basis = Matrix((width, length, normal)).transposed().to_4x4()
    basis.translation = center
    select([arm])
    bpy.ops.object.mode_set(mode='EDIT')
    bone = arm.data.edit_bones.new('Nyx_PhoneBone')
    bone.head = center
    bone.tail = center + length * .04
    bone.parent = arm.data.edit_bones['mixamorig:RightHand']
    bpy.ops.object.mode_set(mode='OBJECT')

    size = 512
    rng = np.random.default_rng(735155)
    pixels = np.ones((size, size, 4), dtype=np.float32)
    noise = rng.random((size, size))
    base = np.array([.72, .13, .38])
    pixels[:, :, :3] = base + (noise[:, :, None] - .5) * np.array([.20, .08, .16])
    sparkle = noise > .965
    pixels[sparkle, :3] = np.array([1.0, .66, .86])
    pixels[:, size//2:, :3] = [.012, .014, .020]
    img = bpy.data.images.new('Nyx_Phone', width=size, height=size)
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw = str(out/'textures/Nyx_Phone.png')
    img.file_format = 'PNG'
    img.save()
    pieces = []

    def slab(name, dimensions, location, radius, black=False):
        bpy.ops.mesh.primitive_cube_add(size=1)
        ob = bpy.context.object
        ob.name = name
        ob.dimensions = dimensions
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        bevel = ob.modifiers.new('Rounded corners', 'BEVEL')
        bevel.width = radius
        bevel.segments = 4
        bpy.ops.object.modifier_apply(modifier=bevel.name)
        uv = ob.data.uv_layers.active
        for loop in uv.data:
            loop.uv.x = (.54 if black else .03) + loop.uv.x * .42
            loop.uv.y = .03 + loop.uv.y * .94
        for v in ob.data.vertices:
            v.co = basis @ (v.co + Vector(location))
        pieces.append(ob)
        return ob

    slab('Case', (.075, .155, .009), (0, 0, 0), .004)
    slab('Black screen', (.067, .147, .0012), (0, 0, .0042), .003, True)
    slab('Camera island', (.023, .035, .001), (-.022, .051, -.0046), .00045)
    for y in [.043, .059]:
        slab('Camera glass', (.011, .011, .0007), (-.022, y, -.0052), .003, True)
    select(pieces)
    bpy.ops.object.join()
    phone = bpy.context.object
    phone.name = phone.data.name = 'Nyx_Phone'
    phone.vertex_groups.clear()
    phone.vertex_groups.new(name='Nyx_PhoneBone').add(list(range(len(phone.data.vertices))), 1, 'REPLACE')
    phone.modifiers.new('Armature', 'ARMATURE').object = arm
    phone.parent = arm
    phone.matrix_parent_inverse = arm.matrix_world.inverted()
    material(phone, out/'textures/Nyx_Phone.png', 'Nyx_Phone')
    meshes.append(phone)
    return bag, stowed, phone, {
        'stowed_pole_islands': sorted(pole_ids),
        'backpack_vertices': len(bag.data.vertices),
        'stowed_pole_vertices': len(stowed.data.vertices),
        'phone': {'bone': 'Nyx_PhoneBone', 'parent': hand.name, 'center_metres': list(center),
                  'dimensions_metres': [.075, .155, .009], 'width_axis': list(width), 'length_axis': list(length),
                  'screen_normal': list(normal), 'placement': 'right palm and finger landmarks; no isolated baked phone exists in the supplied open hand'},
    }
