"""Optional Eevee massing aid; gameplay captures and ACES remain the review authority."""
import os
from stagekit.preview import B, gameplay_camera, render_views

VIEWS={
    'show':((0,1.8,0),0,15,14),
    'east':((0,1.8,0),90,12,14),
    'south':((0,1.8,0),180,12,14),
    'west':((0,1.8,0),270,12,14),
    'thumbnail':((0,1,9),329,22,50),
}


def render(build,objects,out_dir,asset_dir,views_filter=''):
    import bpy
    # The existing Slop Pit node builder is an accepted visualization of the same two shaders.
    from slop_pit.preview import _material
    os.makedirs(out_dir,exist_ok=True)
    images={}
    for slot in ('MM_Night','MM_Lit','MM_Floor','MM_Screen'):
        filename='MankiMayhemFloor.png' if slot=='MM_Floor' else 'MankiMayhemAtlas.png'
        if filename not in images: images[filename]=bpy.data.images.load(os.path.join(asset_dir,filename))
        mat=_material(slot,images[filename])
        if slot in ('MM_Lit','MM_Floor'):
            # _material selects Slop Pit's lit slots by name; set our surfaces to matte Principled.
            nt=mat.node_tree; output=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL')
            emission=next(n for n in nt.nodes if n.type=='EMISSION')
            source=emission.inputs['Color'].links[0].from_socket
            bsdf=nt.nodes.new('ShaderNodeBsdfPrincipled'); bsdf.inputs['Roughness'].default_value=.9
            nt.links.new(source,bsdf.inputs['Base Color']); nt.links.new(bsdf.outputs[0],output.inputs['Surface'])
    sc=bpy.context.scene; sc.render.engine='BLENDER_EEVEE'
    sc.render.resolution_x,sc.render.resolution_y=1280,720
    sc.render.resolution_percentage=100
    sc.world=sc.world or bpy.data.worlds.new('Warehouse Void'); sc.world.use_nodes=True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.025,.017,.01,1)
    data=bpy.data.lights.new('Broadcast Key','SUN'); data.energy=2
    data.color=(1,.93,.83); obj=bpy.data.objects.new('Broadcast Key',data); sc.collection.objects.link(obj)
    obj.location=B((6,30,-12)); obj.rotation_euler=(B((0,0,0))-obj.location).to_track_quat('-Z','Y').to_euler()
    camera=gameplay_camera(sc,clip_end=300)
    render_views(sc,camera,VIEWS,views_filter,out_dir)
