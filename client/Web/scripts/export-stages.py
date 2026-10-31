#!/usr/bin/env python3
"""Export accepted generator FBXs as web GLBs, without writing to Unity.

Run: python3 client/Web/scripts/export-stages.py [stage ...]
Requires the existing Blender, Pillow, g++, and system libdraco. All geometry is preserved; Draco quantizes positions to 20 bits and verifies every
decoded point against its source. No simplification or decimation is performed.
"""
from __future__ import annotations
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[3]
DEST = ROOT / 'client/Web/assets/stages'
STAGES = ['rec_center_roof', 'after_hours', 'picnic_panic', 'industrial_rooftop',
          'last_stop', 'slop_pit', 'splash_deck', 'table_manners', 'log_off',
          'hamster_habitat', 'training', 'slopball', 'license_test']


def source(stage):
    directory = ROOT / 'client/Unity/Assets/Stages' / stage / 'Presentation'
    documents = sorted(directory.rglob('*.presentation.json'))
    if documents:
        path = documents[0]
        return path.parent, json.loads(path.read_text())
    if stage != 'slopball':
        return None, None
    # Slopball predates presentation.json: these are the values in
    # SlopArenaSlopballNightCourtCommands.cs and the accepted material assets.
    return directory / 'NightCourt', {
        'model': 'SlopballNightCourt.fbx',
        'materials': [
            {'slot': 'NC_Court', 'shader': 'SLOPArena/StageVertexLit', 'baseMap': 'SlopballNightFloor.png',
             'textures': {'_DetailMap': 'SlopballNightDetail.png'},
             'floats': {'_DetailScale': .5, '_DetailStrength': .22, '_Wrap': .2, '_Specular': .22, '_Gloss': 40},
             'colors': {'_ShadowTint': [.5, .56, .7]}},
            {'slot': 'NC_Night', 'shader': 'SLOPArena/StageNeon', 'baseMap': 'SlopballNightAtlas.png',
             'floats': {'_Base': 1.35, '_Ambient': .12, '_Shade': .15, '_Glow': 1.6, '_FogAmount': 1},
             'vectors': {'_ShadeDirection': [.2, .85, -.3]}},
            {'slot': 'NC_Beam', 'shader': 'Additive', 'floats': {'_Cull': 0}},
        ],
        'atmosphere': {'background': [.012, .014, .022], 'ambientSky': [.3, .34, .44],
                       'ambientGround': [.1, .1, .12], 'ambientIntensity': 1,
                       'fog': True, 'fogColor': [.03, .036, .052], 'fogStart': 22, 'fogEnd': 120},
        'lights': [{'name': 'Court Floods', 'type': 'directional', 'sun': {'azimuth': 8, 'elevation': 72},
                    'color': [.9, .95, 1], 'intensity': 1.25}],
    }


def texture_bytes(path, alpha, posters=()):
    from PIL import Image
    image = Image.open(path).convert('RGBA' if alpha else 'RGB')
    for poster in posters:
        # Unity adds a separate face over this atlas cell; bake the same artwork into
        # the web atlas so the approved sign survives without overlapping triangles.
        if 'Atlas' not in path.name:
            continue
        u0, u1, v0, v1 = poster['uvPixels']
        size = poster['atlasSize']
        scale = image.width / size
        bounds = tuple(round(v * scale) for v in (u0, size - v1, u1, size - v0))
        face = Image.open(path.parent / poster['texture']).convert(image.mode)
        face = face.resize((bounds[2] - bounds[0], bounds[3] - bounds[1]), Image.Resampling.LANCZOS)
        image.paste(face, bounds)
    size = 256 if 'Detail' in path.name else 1024
    image.thumbnail((size, size), Image.Resampling.LANCZOS)
    out = io.BytesIO()
    # Atlas alpha is the neon glow mask, not transparency. Keep it in PNG.
    if alpha and image.getextrema()[3][0] < 255:
        image.save(out, 'PNG', optimize=True)
        mime = 'image/png'
    else:
        image.convert('RGB').save(out, 'JPEG', quality=88, subsampling=0, optimize=True)
        mime = 'image/jpeg'
    return out.getvalue(), mime, list(image.size)


def host_main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stages', nargs='*', choices=STAGES)
    args = parser.parse_args()
    selected = args.stages or STAGES
    DEST.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='sloparena-web-stages-') as tmp:
        tmp = Path(tmp)
        compressor = tmp / 'draco'
        subprocess.run(['g++', '-O2', '-std=c++17', str(Path(__file__).with_name('export-stages-draco.cpp')),
                        '-ldraco', '-o', str(compressor)], check=True)
        for stage in selected:
            directory, spec = source(stage)
            if spec is None:
                print(stage + ': no generator; keep collision greybox', flush=True)
                continue
            model = directory / spec['model']
            if not model.is_file():
                raise SystemExit(f'Missing accepted generator output: {model}')
            stamp = json.loads(model.with_suffix('.layout.json').read_text())
            arena_hash = hashlib.sha256((ROOT / 'data/arenas' / f'{stage}.arena').read_bytes()).hexdigest()
            if stamp.get('arenaSha256') != arena_hash:
                raise SystemExit(f'{stage}: art stamp does not match the current collision bake')
            packed = {}
            for material in spec['materials']:
                names = [(material.get('baseMap'), material['shader'].endswith('StageNeon'))]
                names += [(n, False) for n in material.get('textures', {}).values()]
                for name, alpha in names:
                    if name:
                        packed[name] = packed.get(name, False) or alpha
            textures = {}
            for index, (name, alpha) in enumerate(packed.items()):
                blob, mime, size = texture_bytes(directory / name, alpha, spec.get('posters', []))
                filename = tmp / f'{stage}-{index}.image'
                filename.write_bytes(blob)
                textures[name] = {'file': str(filename), 'mime': mime, 'size': size}
            job = {'stage': stage, 'spec': spec, 'model': str(model), 'textures': textures,
                   'compressor': str(compressor), 'work': str(tmp), 'dest': str(DEST / f'{stage}.glb')}
            jobpath = tmp / 'job.json'
            jobpath.write_text(json.dumps(job))
            subprocess.run(['/usr/bin/blender', '-b', '--factory-startup', '--python-exit-code', '1',
                            '--python', str(Path(__file__).resolve()), '--', '--job', str(jobpath)], check=True)
    # Vendor the same versioned decoder that DRACOLoader uses; published sites need no extra CDN.
    decoder = DEST / 'draco'
    decoder.mkdir(exist_ok=True)
    for name in ['draco_wasm_wrapper.js', 'draco_decoder.wasm']:
        path = decoder / name
        if not path.exists():
            urllib.request.urlretrieve('https://www.gstatic.com/draco/versioned/decoders/1.5.7/' + name, path)
    license_path = decoder / 'LICENSE'
    if not license_path.exists():
        urllib.request.urlretrieve('https://raw.githubusercontent.com/google/draco/1.5.7/LICENSE', license_path)
    reports = [json.loads(p.read_text()) for p in sorted(DEST.glob('*.report.json'))]
    manifest = {'stages': reports, 'missing': [s for s in STAGES if source(s)[1] is None],
                'totalGlbBytes': sum(r['bytes'] for r in reports)}
    (DEST / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(json.dumps(manifest, indent=2))


class Glb:
    def __init__(self):
        self.binary = bytearray()
        self.json = {'asset': {'version': '2.0', 'generator': 'SLOPArena generator art web exporter'},
                     'extensionsUsed': ['KHR_draco_mesh_compression'],
                     'extensionsRequired': ['KHR_draco_mesh_compression'], 'scene': 0,
                     'scenes': [{'nodes': []}], 'nodes': [], 'meshes': [], 'materials': [],
                     'accessors': [], 'bufferViews': [], 'images': [], 'textures': [],
                     'samplers': [{'magFilter': 9729, 'minFilter': 9987, 'wrapS': 10497, 'wrapT': 10497}]}

    def view(self, data):
        self.binary.extend(b'\0' * (-len(self.binary) % 4))
        view = {'buffer': 0, 'byteOffset': len(self.binary), 'byteLength': len(data)}
        self.binary.extend(data)
        self.json['bufferViews'].append(view)
        return len(self.json['bufferViews']) - 1

    def accessor(self, data, component=5126):
        a = {'componentType': component, 'count': len(data),
             'type': {1: 'SCALAR', 2: 'VEC2', 3: 'VEC3', 4: 'VEC4'}[data.shape[1]]}
        if component == 5126:
            a.update(min=data.min(axis=0).tolist(), max=data.max(axis=0).tolist())
        self.json['accessors'].append(a)
        return len(self.json['accessors']) - 1

    def save(self, path):
        self.json['buffers'] = [{'byteLength': len(self.binary)}]
        doc = json.dumps(self.json, separators=(',', ':')).encode()
        doc += b' ' * (-len(doc) % 4)
        self.binary.extend(b'\0' * (-len(self.binary) % 4))
        temporary = Path(path).with_suffix('.glb.tmp')
        temporary.write_bytes(struct.pack('<III', 0x46546c67, 2, 28 + len(doc) + len(self.binary)) +
                              struct.pack('<II', len(doc), 0x4e4f534a) + doc +
                              struct.pack('<II', len(self.binary), 0x004e4942) + self.binary)
        temporary.replace(path)


def blender_main(job):
    import bpy
    import numpy as np
    from mathutils import Matrix
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=job['model'], use_custom_normals=True, colors_type='SRGB')
    glb = Glb()
    spec = job['spec']
    env = {'atmosphere': spec.get('atmosphere', {}), 'lights': spec.get('lights', [])}
    glb.json['scenes'][0]['extras'] = {'environment': env, 'stageId': job['stage']}
    texture_ids = {}
    for name, texture in job['textures'].items():
        index = len(glb.json['images'])
        glb.json['images'].append({'name': name, 'bufferView': glb.view(Path(texture['file']).read_bytes()),
                                   'mimeType': texture['mime']})
        glb.json['textures'].append({'source': index, 'sampler': 0})
        texture_ids[name] = index
    slots = {}
    for surface in spec['materials']:
        surface = dict(surface)
        surface['detailTexture'] = texture_ids.get(surface.get('textures', {}).get('_DetailMap'))
        surface['rippleTexture'] = texture_ids.get(surface.get('textures', {}).get('_RippleMap'))
        material = {'name': surface['slot'], 'extras': {'stageSurface': surface},
                    'pbrMetallicRoughness': {'baseColorFactor': [1, 1, 1, 1], 'metallicFactor': 0, 'roughnessFactor': .85}}
        if surface.get('baseMap'):
            material['pbrMetallicRoughness']['baseColorTexture'] = {'index': texture_ids[surface['baseMap']]}
        slots[surface['slot']] = len(glb.json['materials'])
        glb.json['materials'].append(material)
    # FBX imports back into Blender space. Invert stagekit.geo.to_object explicitly;
    # main.js alone mirrors sim Z. No glTF axis conversion or centimetre scale is involved.
    to_sim = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
    triangle_count = 0
    primitive_count = 0
    animation_counts = [0, 0, 0]
    max_position_error = 0
    for ob in sorted(bpy.context.scene.objects, key=lambda o: o.name):
        if ob.type != 'MESH':
            continue
        mesh = ob.data
        mesh.calc_loop_triangles()
        transform = to_sim @ ob.matrix_world
        normal_transform = transform.to_3x3().inverted().transposed()
        coords = np.array([transform @ v.co for v in mesh.vertices], dtype=np.float32)
        normals = np.array([normal_transform @ n.vector for n in mesh.corner_normals], dtype=np.float32)
        normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
        color = mesh.color_attributes.active_color
        colors = np.ones((len(mesh.loops), 4), dtype=np.float32)
        if color:
            color.data.foreach_get('color', colors.ravel())
        uv = []
        for layer in mesh.uv_layers:
            values = np.empty((len(mesh.loops), 2), dtype=np.float32)
            layer.data.foreach_get('uv', values.ravel())
            uv.append(values)
        primitive_list = []
        for material_index, material in enumerate(mesh.materials):
            triangles = [t for t in mesh.loop_triangles if t.material_index == material_index]
            if not triangles:
                continue
            if material.name not in slots:
                raise ValueError(f'Unknown material {material.name} on {ob.name}')
            loops = np.array([list(reversed(t.loops)) for t in triangles], dtype=np.int32).ravel()
            vertex_ids = np.array([mesh.loops[i].vertex_index for i in loops], dtype=np.int32)
            attrs = [('POSITION', coords[vertex_ids], 0, 20),
                     ('NORMAL', normals[loops], 1, 8), ('COLOR_0', colors[loops], 2, 10)]
            if uv:
                texcoord = uv[0][loops].copy()
                texcoord[:, 1] = 1 - texcoord[:, 1]
                attrs.append(('TEXCOORD_0', texcoord, 3, 12))
            for layer, name in enumerate(['_STAGE_ANIM', '_STAGE_TRAVEL', '_STAGE_DIR']):
                if len(uv) > layer + 1 and np.any(uv[layer + 1][loops]):
                    attrs.append((name, uv[layer + 1][loops], 4, 24))
                    animation_counts[layer] += len(triangles)
            # Weld only corners with identical attributes: preserves hard edges, masks and animation seams.
            joined = np.ascontiguousarray(np.concatenate([a[1] for a in attrs], axis=1))
            _, first, inverse = np.unique(joined.view(f'V{joined.shape[1] * 4}'), return_index=True, return_inverse=True)
            attrs = [(n, np.ascontiguousarray(a[first], dtype='<f4'), t, q) for n, a, t, q in attrs]
            packet = bytearray(struct.pack('<III', len(first), len(triangles), len(attrs)))
            for name, values, kind, bits in attrs:
                packet += struct.pack('<III', kind, values.shape[1], bits) + values.tobytes()
            packet += inverse.astype('<u4').tobytes()
            work = Path(job['work'])
            (work / 'mesh.bin').write_bytes(packet)
            result = subprocess.run([job['compressor'], str(work / 'mesh.bin'), str(work / 'mesh.drc')],
                                    check=True, capture_output=True, text=True)
            count_text, error_text = result.stdout.split()
            count = int(count_text)
            max_position_error = max(max_position_error, float(error_text))
            attributes = {}
            for name, values, kind, bits in attrs:
                attributes[name] = glb.accessor(values)
                glb.json['accessors'][-1]['count'] = count
            indices = len(glb.json['accessors'])
            glb.json['accessors'].append({'componentType': 5125, 'count': len(triangles) * 3, 'type': 'SCALAR'})
            primitive_list.append({'attributes': attributes, 'indices': indices, 'material': slots[material.name],
                                   'extensions': {'KHR_draco_mesh_compression': {
                                       'bufferView': glb.view((work / 'mesh.drc').read_bytes()),
                                       'attributes': {n: i for i, (n, a, t, q) in enumerate(attrs)}}}})
            triangle_count += len(triangles)
            primitive_count += 1
        if primitive_list:
            index = len(glb.json['meshes'])
            glb.json['meshes'].append({'name': ob.name, 'primitives': primitive_list})
            node = {'name': ob.name, 'mesh': index}
            if 'shadows' in spec:
                shadows = spec['shadows']
                node['extras'] = {'stageShadows': shadows.get('objects', {}).get(ob.name,
                                  {'cast': shadows.get('cast', True), 'receive': shadows.get('receive', True)})}
            glb.json['scenes'][0]['nodes'].append(len(glb.json['nodes']))
            glb.json['nodes'].append(node)
    glb.save(job['dest'])
    report = {'stage': job['stage'], 'bytes': Path(job['dest']).stat().st_size, 'triangles': triangle_count,
              'primitives': primitive_count, 'animatedTriangles': dict(zip(['glow', 'travel', 'direction'], animation_counts)),
              'source': str(Path(job['model']).relative_to(ROOT)),
              'sourceSha256': hashlib.sha256(Path(job['model']).read_bytes()).hexdigest(),
              'arenaSha256': hashlib.sha256((ROOT / 'data/arenas' / (job['stage'] + '.arena')).read_bytes()).hexdigest(),
              'positionCompression': '20-bit Draco; every decoded position checked against source floats',
              'maxPositionErrorMetres': max_position_error,
              'textures': {n: {'size': t['size'], 'mime': t['mime']} for n, t in job['textures'].items()}}
    Path(job['dest']).with_suffix('.report.json').write_text(json.dumps(report, indent=2) + '\n')
    print('WEB_STAGE_EXPORT', json.dumps(report), flush=True)


if __name__ == '__main__':
    if '--job' in sys.argv:
        blender_main(json.loads(Path(sys.argv[sys.argv.index('--job') + 1]).read_text()))
    else:
        host_main()
