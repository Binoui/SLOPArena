"""Render matching v3 and prop candidate views from their packed build artifacts."""
import bpy
import math
import sys
from pathlib import Path
from mathutils import Matrix, Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import setup_render, shoot

root = Path(__file__).resolve().parents[3]
out = root/'tmp/nyx-model/props/review'
out.mkdir(exist_ok=True)
for phase, directory in [('before', 'spring-dress-v3'), ('after', 'props')]:
    bpy.ops.wm.open_mainfile(filepath=str(root/'tmp/nyx-model'/directory/'Nyx_rig.blend'))
    arm = bpy.data.objects['Armature']
    skins = [o for o in bpy.data.objects if o.type == 'MESH']
    for ob in skins: ob.hide_set(False)
    setup_render(900)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.30, .33, .38, 1)

    def reset():
        for b in arm.pose.bones: b.matrix_basis.identity()
        bpy.context.view_layer.update()

    def rotate(name, axis, degrees):
        b = arm.pose.bones['mixamorig:'+name]
        b.matrix = Matrix.Translation(b.head) @ Matrix.Rotation(math.radians(degrees),4,Vector(axis)) @ Matrix.Translation(-b.head) @ b.matrix
        bpy.context.view_layer.update()

    def show(form, held=False, phone=False):
        for ob in skins:
            ob.hide_render = not (ob.name.startswith('Nyx_'+form) or form == 'Fake' and ob.name == 'Nyx_Backpack'
                or form == 'Fake' and not held and ob.name == 'Nyx_BackpackPole'
                or held and ob.name == 'Nyx_PoleHeld' or phone and ob.name == 'Nyx_Phone')

    reset()
    rotate('LeftArm',(0,1,0),75)
    rotate('RightArm',(0,1,0),-75)
    for held in [False, True]:
        show('Fake',held)
        shoot(out/f'{phase}-backpack-{"held" if held else "stowed"}.png',(0,.06,1.35),1.6,155)
    for form in ['Real', 'Fake']:
        reset()
        rotate('LeftArm',(0,1,0),75)
        rotate('RightArm',(0,0,1),90)
        rotate('RightForeArm',(1,0,0),-85)
        show(form,phone=True)
        palm = (arm.pose.bones['mixamorig:RightHand'].head+arm.pose.bones['mixamorig:RightHandMiddle2'].head)*.5
        shoot(out/f'{phase}-{form.lower()}-phone.png',palm,.35,-25)
        shoot(out/f'{phase}-{form.lower()}-phone-full.png',(0,0,.95),2.2,25)
print('NYX_PROP_REVIEW', str(out))
