"""Shared headless Blender helpers; coordinates are metres, Z up, front -Y."""
import bpy, bmesh, math
import numpy as np
from mathutils import Vector

def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects:
        ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

def load_mesh(path, name):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    obs = [o for o in bpy.context.scene.objects if o not in before and o.type == 'MESH']
    assert len(obs) == 1, (path, len(obs))
    ob = obs[0]; select([ob])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.name = ob.data.name = name
    return ob

def vertices(ob):
    v = np.empty(len(ob.data.vertices)*3, dtype=np.float64)
    ob.data.vertices.foreach_get('co', v)
    return v.reshape(-1,3)

def islands(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    seen=set(); groups=[]
    for face in bm.faces:
        if face.index in seen: continue
        todo=[face]; seen.add(face.index); fs=[]; vs=set()
        while todo:
            f=todo.pop(); fs.append(f.index); vs.update(v.index for v in f.verts)
            for edge in f.edges:
                for g in edge.link_faces:
                    if g.index not in seen: seen.add(g.index); todo.append(g)
        groups.append((fs, sorted(vs)))
    bm.free()
    return sorted(groups,key=lambda g: -len(g[0]))

def subset(ob, faces, name):
    out=ob.copy(); out.data=ob.data.copy(); bpy.context.scene.collection.objects.link(out)
    out.name=out.data.name=name
    bm=bmesh.new(); bm.from_mesh(out.data); bm.faces.ensure_lookup_table()
    keep=set(faces)
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index not in keep],context='FACES')
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(out.data); bm.free(); out.data.update()
    return out

def material(ob, texture, name):
    img=bpy.data.images.load(str(texture),check_existing=True)
    img.colorspace_settings.name='sRGB'
    mat=bpy.data.materials.new(name); mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF')
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=img
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    bs.inputs['Roughness'].default_value=.85
    bs.inputs['Metallic'].default_value=0
    ob.data.materials.clear(); ob.data.materials.append(mat)
    for p in ob.data.polygons: p.material_index=0
    return img

def setup_render(res=1000):
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.resolution_x=scene.render.resolution_y=res
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='Standard'
    world=bpy.data.worlds.new('Nyx review'); world.use_nodes=True; scene.world=world
    world.node_tree.nodes['Background'].inputs[0].default_value=(.67,.69,.73,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.65
    for name,loc,power,size in [('key',(-3,-4,5),500,4),('fill',(3,-1,3),250,3),('back',(1,4,4),450,3)]:
        d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.shape='DISK'; d.size=size
        ob=bpy.data.objects.new(name,d); scene.collection.objects.link(ob); ob.location=loc
        ob.rotation_euler=(Vector((0,0,.9))-ob.location).to_track_quat('-Z','Y').to_euler()
    d=bpy.data.cameras.new('Review camera'); d.type='ORTHO'; d.clip_end=100
    ob=bpy.data.objects.new('Review camera',d); scene.collection.objects.link(ob); scene.camera=ob
    return ob

def shoot(path, center, scale, yaw=0, res=None):
    scene=bpy.context.scene; cam=scene.camera
    center=Vector(center); a=math.radians(yaw)
    cam.location=center+Vector((math.sin(a),-math.cos(a),0))*10
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale
    if res: scene.render.resolution_x=scene.render.resolution_y=res
    scene.render.filepath=str(path); bpy.ops.render.render(write_still=True)
