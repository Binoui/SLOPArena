"""Optional Eevee review aid. Unity gameplay captures remain the visual evidence."""
import math
from pathlib import Path
import bpy
from stagekit.preview import B,gameplay_camera,render_views
from stagekit.presentation import load_stage
from presentation import PRESENTATION

def orbit_pose(camera):
    target=tuple(camera['target'])
    delta=[target[i]-camera['position'][i] for i in range(3)]
    distance=math.sqrt(sum(v*v for v in delta))
    return target,math.degrees(math.atan2(delta[0],delta[2])),math.degrees(math.asin(-delta[1]/distance)),distance


VIEWS={camera['name']:orbit_pose(camera) for camera in PRESENTATION['cameras']}


def render(objects,out,dest,views_filter=''):
    dest=Path(dest);dest.mkdir(parents=True,exist_ok=True)
    sc=bpy.context.scene
    sc.render.engine='BLENDER_EEVEE'
    sc.render.resolution_x,sc.render.resolution_y=1280,720
    sc.render.resolution_percentage=100
    p=load_stage('training')
    for entry in p['materials']:
        mat=bpy.data.materials[entry['slot']];mat.use_nodes=True
        nodes=mat.node_tree.nodes;nodes.clear()
        output=nodes.new('ShaderNodeOutputMaterial')
        color=nodes.new('ShaderNodeVertexColor');color.layer_name='Col'
        texture=nodes.new('ShaderNodeTexImage');texture.image=bpy.data.images.load(str(Path(out)/entry['baseMap']))
        multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
        links=mat.node_tree.links
        links.new(color.outputs['Color'],multiply.inputs[1]);links.new(texture.outputs['Color'],multiply.inputs[2])
        if entry['slot'] in ('TL_Lit','TL_Floor'):
            shader=nodes.new('ShaderNodeBsdfPrincipled');shader.inputs['Roughness'].default_value=.8
            links.new(multiply.outputs[0],shader.inputs['Base Color'])
        else:
            shader=nodes.new('ShaderNodeEmission');shader.inputs['Strength'].default_value=.85
            links.new(multiply.outputs[0],shader.inputs['Color'])
        links.new(shader.outputs[0],output.inputs['Surface'])
        mat.use_backface_culling=True
    world=bpy.data.worlds.new('Lab Ambient');sc.world=world;world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.5,.53,.56,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.8
    light=bpy.data.lights.new('Lab Key','SUN');light.energy=2
    ob=bpy.data.objects.new('Lab Key',light);sc.collection.objects.link(ob)
    ob.location=B((0,40,-8));ob.rotation_euler=(B((0,0,0))-ob.location).to_track_quat('-Z','Y').to_euler()
    cam=gameplay_camera(sc,200,.1,45)
    def camera_fov(name,target,distance):
        entry=next(c for c in p['cameras'] if c['name']==name)
        cam.data.angle_y=math.radians(entry['fov'])
    render_views(sc,cam,VIEWS,views_filter,str(dest),before=camera_fov)
