#!/usr/bin/env python3
import collections
import copy
import json
from pathlib import Path
import re
import shutil
import struct
import yaml
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
ASSETS = ROOT / 'client/Unity/Assets'
OUT = ROOT / 'client/Web/app/vfx'
TEXTURES = ROOT / 'client/Web/assets/vfx'
INDEX = {}
for meta in sorted(ASSETS.rglob('*.meta')):
    match = re.search(r'^guid: (\w+)', meta.read_text(errors='replace'), re.M)
    if match:
        INDEX[match[1]] = meta.with_suffix('')
CACHE = {}
MODULES = collections.Counter()
SKIPS = collections.defaultdict(set)
MATERIALS, MESHES, TEXTURE_INFO = {}, {}, {}
CONTEXT = ''
IMPORTED = json.loads((OUT / 'projectile-meshes.json').read_text()) if (OUT / 'projectile-meshes.json').exists() else {}


def skip(feature):
    SKIPS[CONTEXT].add(feature)


def docs(path):
    if path not in CACHE:
        CACHE[path] = [(int(kind), int(ident), next(iter(yaml.load(body, Loader=yaml.CSafeLoader).values())))
                       for kind, ident, body in re.findall(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |\Z)', path.read_text(errors='replace'), re.M)]
    return copy.deepcopy(CACHE[path])


def vec(value, axes='xyz', default=0):
    return [value.get(k, default) for k in axes]


def curve(value):
    if isinstance(value, dict):
        if 'minMaxState' in value:
            mode = value['minMaxState']
            if 'maxColor' in value:
                result = {'mode': mode, 'type': 'gradient'}
                for key in (['maxColor'] if mode == 0 else ['minColor', 'maxColor'] if mode == 2 else []):
                    result[key] = vec(value[key], 'rgba')
                for key in (['maxGradient'] if mode in (1, 4) else ['minGradient', 'maxGradient'] if mode == 3 else []):
                    g = value[key]
                    result[key] = {'mode': g.get('m_Mode', 0),
                                   'colors': [[g.get(f'ctime{i}', 0) / 65535, *vec(g[f'key{i}'], 'rgb')] for i in range(g['m_NumColorKeys'])],
                                   'alphas': [[g.get(f'atime{i}', 0) / 65535, g[f'key{i}']['a']] for i in range(g['m_NumAlphaKeys'])]}
                return result
            result = {'type': 'curve', 'mode': mode, 'scalar': value.get('scalar', 0)}
            if mode in (2, 3):
                result['minScalar'] = value.get('minScalar', 0)
            for key in (['maxCurve'] if mode == 1 else ['minCurve', 'maxCurve'] if mode == 2 else []):
                keys = value[key].get('m_Curve') or []
                result[key] = [{k: f.get(k, 0) for k in ('time', 'value', 'inSlope', 'outSlope')} for f in keys]
                if any(f.get('weightedMode', 0) for f in keys):
                    skip('weighted curve tangents')
                if value[key].get('m_PreInfinity', 2) != 2 or value[key].get('m_PostInfinity', 2) != 2:
                    skip('curve extrapolation outside normalized lifetime')
            return result
        return {k: curve(v) for k, v in value.items() if k != 'serializedVersion'}
    if isinstance(value, list):
        return [curve(v) for v in value]
    if isinstance(value, float) and not float('-inf') < value < float('inf'):
        return str(value)
    return value


def texture(ref):
    guid = ref.get('guid')
    if not guid:
        return None
    path = INDEX.get(guid)
    if path is None:
        skip(f'unresolved texture {guid}')
        return None
    if guid not in TEXTURE_INFO:
        if path.suffix.lower() not in ('.png', '.jpg', '.jpeg', '.tga'):
            skip(f'non-PNG texture {path.relative_to(ROOT)}')
            return None
        dest = TEXTURES / f'{path.stem}-{guid[:10]}.png'
        with Image.open(path) as im:
            original = list(im.size)
            if max(im.size) > 512:
                im.thumbnail((512, 512), Image.Resampling.LANCZOS)
                im.save(dest, optimize=True)
                resized = list(im.size)
            else:
                if path.suffix.lower() == '.png':
                    shutil.copyfile(path, dest)
                else:
                    im.save(dest, optimize=True)
                resized = original
        TEXTURE_INFO[guid] = {'file': dest.name, 'source': str(path.relative_to(ROOT)), 'original': original, 'size': resized, 'bytes': dest.stat().st_size}
    return TEXTURE_INFO[guid]['file']


def material(ref):
    guid = ref.get('guid')
    if not guid or guid not in INDEX:
        if ref.get('fileID'):
            skip(f'unresolved material {ref}')
        return None
    if guid in MATERIALS:
        return guid
    data = next(d for k, _, d in docs(INDEX[guid]) if k == 21)
    props = data['m_SavedProperties']
    floats = {k: v for row in props.get('m_Floats', []) for k, v in row.items()}
    colors = {k: v for row in props.get('m_Colors', []) for k, v in row.items()}
    tex = {k: v for row in props.get('m_TexEnvs', []) for k, v in row.items()}
    base = tex.get('_BaseMap', tex.get('_MainTex', {}))
    blend = ['alpha', 'premultiply', 'additive', 'multiply'][int(floats.get('_Blend', 0)) % 4]
    MATERIALS[guid] = {'name': data.get('m_Name'), 'texture': texture(base.get('m_Texture', {})), 'blend': blend,
                       'baseColor': vec(colors.get('_BaseColor', colors.get('_Color', dict(r=1, g=1, b=1, a=1))), 'rgba'),
                       'colorMode': floats.get('_ColorMode', 0), 'surface': floats.get('_Surface', 1),
                       'uvScale': vec(base.get('m_Scale', {}), 'xy', 1), 'uvOffset': vec(base.get('m_Offset', {}), 'xy'),
                       'queue': data.get('m_CustomRenderQueue', 3000), 'shader': 'texture', 'depthTest': floats.get('_ZTest', 4)}
    shader = data['m_Shader'].get('guid')
    if shader in INDEX and INDEX[shader].name == 'NyxPaper.shader':
        MATERIALS[guid].update(shader='nyxPaper', depthWrite=True)
    elif shader not in ('0406db5a14f94604a8c57ccfbc9f3b46', '650dd9526735d5b46b79224bc6e94025'):
        skip(f'shader {str(INDEX[shader].relative_to(ROOT)) if shader in INDEX else shader} interpreted as unlit texture')
    for key in ('_DistortionEnabled', '_SoftParticlesEnabled', '_CameraFadingEnabled', '_AlphaClip', '_EmissionEnabled'):
        if floats.get(key, 0):
            skip(f'material {data.get("m_Name")} {key}')
    if floats.get('_ColorMode', 0) != 0:
        skip(f'material {data.get("m_Name")} colorMode={floats["_ColorMode"]}')
    return guid


def mesh(ref):
    guid = ref.get('guid')
    if not guid:
        return {'builtin': ref['fileID']} if ref.get('fileID') else None
    if guid in MESHES:
        return {'id': guid}
    path = INDEX.get(guid)
    if not path or path.suffix != '.asset':
        skip(f'imported mesh {path.relative_to(ROOT) if path else guid}')
        return {'unsupported': str(path.relative_to(ROOT) if path else guid)}
    mesh_data = next((d for k, _, d in docs(path) if k == 43), None)
    if not mesh_data:
        skip(f'no mesh data {path}')
        return None
    v = mesh_data['m_VertexData']
    channels = v['m_Channels']
    if any(c['stream'] != 0 or c['format'] not in (0, 2) for c in channels if c['dimension']):
        skip(f'mesh vertex encoding {path.relative_to(ROOT)}')
        return None
    stride = max(c['offset'] + c['dimension'] * (4 if c['format'] == 0 else 1) for c in channels)
    raw = bytes.fromhex(str(v['_typelessdata']))
    result = {}
    for name, idx in [('position', 0), ('normal', 1), ('color', 3), ('uv', 4)]:
        c = channels[idx]
        if not c['dimension']:
            continue
        fmt = '<' + ('f' if c['format'] == 0 else 'B') * c['dimension']
        values = [n for i in range(v['m_VertexCount']) for n in struct.unpack_from(fmt, raw, stride * i + c['offset'])]
        result[name] = [round(n / 255 if c['format'] == 2 else n, 7) for n in values]
    raw = bytes.fromhex(str(mesh_data['m_IndexBuffer']))
    step, fmt = (2, '<H') if mesh_data.get('m_IndexFormat', 0) == 0 else (4, '<I')
    result['index'] = [struct.unpack_from(fmt, raw, i)[0] for i in range(0, len(raw), step)]
    if any(s['topology'] for s in mesh_data['m_SubMeshes']):
        skip(f'non-triangle submesh {path.relative_to(ROOT)}')
    MESHES[guid] = result
    return {'id': guid}


MODULE_MAP = {'EmissionModule': 'emission', 'ShapeModule': 'shape', 'ColorModule': 'colorOverLifetime', 'SizeModule': 'sizeOverLifetime',
              'VelocityModule': 'velocityOverLifetime', 'ClampVelocityModule': 'limitVelocity', 'RotationModule': 'rotationOverLifetime',
              'UVModule': 'textureSheet', 'CollisionModule': 'collision', 'TrailModule': 'trails', 'NoiseModule': 'noise'}
BEHAVIOURS = {'AttackBoundPresentation': 'attackBound', 'PresentationFrame': 'frame', 'SnapToFloor': 'snapToFloor',
              'YawBillboard': 'yawBillboard', 'SnapToOwnerBone': 'snapToOwnerBone', 'AttackTickWindow': 'attackTickWindow',
              'AttackEndCue': 'attackEndCue', 'FollowOwnerBone': 'followOwnerBone', 'AttackClipShow': 'attackClipShow', 'DamageScaleVariant': 'damageScaleVariant',
              'NyxPaperCue': 'paperCue', 'NyxCrowVisual': 'crow', 'PropSpin': 'spin', 'ChargeTierCues': 'chargeTiers',
              'ChargeTierVariant': 'chargeVariant', 'LingerAfterProjectile': 'linger', 'PropVisibilityPresentation': 'propVisibility',
              'FlashDissolvePresentation': 'flashDissolve', 'PlacedTetherVisual': 'placedTether', 'ChargePropFeedback': 'chargeProp'}


def system(data, renderer):
    init = data['InitialModule']
    main = {k: data.get(k) for k in ('looping', 'prewarm', 'startDelay', 'scalingMode', 'simulationSpeed', 'playOnAwake')}
    main.update(duration=data['lengthInSec'], simulationSpace='local' if data['moveWithTransform'] else 'world', maxParticles=init['maxNumParticles'])
    main.update({k: init[k] for k in ('startLifetime', 'startSpeed', 'startColor', 'startSize', 'startSizeY', 'startSizeZ',
                                   'size3D', 'startRotation', 'startRotationX', 'startRotationY', 'rotation3D', 'gravityModifier', 'randomizeRotationDirection')})
    result = {'main': curve(main)}
    omitted = sorted(k for k in data if not k.endswith('Module') and k not in main and k not in ('lengthInSec', 'moveWithTransform', 'scalingMode'))
    if omitted:
        skip('ParticleSystem serialization/control fields not played: ' + ', '.join(omitted))
    omitted = sorted(set(init) - set(main) - {'enabled', 'serializedVersion', 'maxNumParticles'})
    if omitted:
        skip('InitialModule fields not played: ' + ', '.join(omitted))
    for k, v in data.items():
        if k.endswith('Module') and isinstance(v, dict) and v.get('enabled'):
            MODULES[k] += 1
            if k in MODULE_MAP:
                result[MODULE_MAP[k]] = curve(v)
                supported = {
                    'ShapeModule': {'enabled', 'type', 'angle', 'radiusThickness', 'radius', 'arc', 'm_Position', 'm_Rotation', 'm_Scale'},
                    'CollisionModule': {'enabled', 'type', 'm_Dampen', 'm_Bounce', 'm_EnergyLossOnCollision', 'radiusScale'},
                    'UVModule': {'enabled', 'mode', 'timeMode', 'fps', 'frameOverTime', 'startFrame', 'tilesX', 'tilesY', 'cycles'},
                }.get(k)
                if supported is not None:
                    omitted = sorted(set(v) - supported - {'serializedVersion'})
                    if omitted:
                        skip(k + ' exported fields not played: ' + ', '.join(omitted))
                if k in ('NoiseModule', 'TrailModule'):

                    skip('particle NoiseModule uses coherent value noise instead of Unity Perlin' if k == 'NoiseModule' else 'particle TrailModule uses bounded 32-point ribbons; exact Unity tessellation and sorting differ')
            elif k != 'InitialModule':
                skip(f'particle module {k}')
    if data.get('moveWithCustomTransform', {}).get('fileID'):
        skip('custom simulation transform')
    r = renderer or {}
    result['renderer'] = {name: r.get('m_' + source, default) for name, source, default in [
        ('mode', 'RenderMode', 0), ('lengthScale', 'LengthScale', 2), ('velocityScale', 'VelocityScale', 0),
        ('cameraVelocityScale', 'CameraVelocityScale', 0), ('sortingFudge', 'SortingFudge', 0), ('order', 'SortingOrder', 0),
        ('alignment', 'RenderAlignment', 0), ('pivot', 'Pivot', dict(x=0, y=0, z=0)), ('flip', 'Flip', dict(x=0, y=0, z=0)),
        ('minParticleSize', 'MinParticleSize', 0), ('maxParticleSize', 'MaxParticleSize', 1), ('sortMode', 'SortMode', 0)]}
    rendered = {'RenderMode', 'LengthScale', 'VelocityScale', 'CameraVelocityScale', 'SortingFudge', 'SortingOrder', 'RenderAlignment', 'Pivot', 'Mesh', 'Materials'}
    omitted = sorted(k for k in r if k not in {'m_' + f for f in rendered})
    if omitted:
        skip('ParticleSystemRenderer fields not played: ' + ', '.join(omitted))
    result['renderer']['mesh'] = mesh(r.get('m_Mesh', {}))
    result['material'] = material((r.get('m_Materials') or [{}])[0])
    if len(r.get('m_Materials', [])) > 1:
        result['trailMaterial'] = material(r['m_Materials'][1])
    if r.get('m_RenderMode') == 5:
        skip('renderer mode 5 (none), no particle geometry')
    if r.get('m_CameraVelocityScale'):
        skip('stretched renderer cameraVelocityScale')
    if len(r.get('m_Materials', [])) > 2:
        skip('particle renderer materials beyond the particle and trail slots')
    if r.get('m_UseCustomVertexStreams'):
        skip('custom vertex streams')
    if r.get('m_SortMode'):
        skip('per-particle sort mode')
    return result


def effect(path):
    global CONTEXT
    CONTEXT = str(path.relative_to(ROOT))
    objects = docs(path)
    transforms = {ident: d for k, ident, d in objects if k == 4 and 'm_GameObject' in d}
    gameobjects = {ident: d for k, ident, d in objects if k == 1}
    by_go = collections.defaultdict(list)
    for k, ident, d in objects:
        if 'm_GameObject' in d:
            by_go[d['m_GameObject']['fileID']].append((k, ident, d))
    ids = list(transforms)
    # Unity files need not order parents before children.
    ordered = []
    def add(ident):
        if ident in ordered:
            return
        parent = transforms[ident]['m_Father']['fileID']
        if parent in transforms:
            add(parent)
        ordered.append(ident)
    for ident in ids:
        add(ident)
    node_indices = {transforms[t]['m_GameObject']['fileID']: i for i, t in enumerate(ordered)}
    references = dict(node_indices)
    for kind, ident, component in objects:
        if component.get('m_GameObject', {}).get('fileID') in node_indices:
            references[ident] = node_indices[component['m_GameObject']['fileID']]
    def local_refs(value):
        if isinstance(value, dict):
            if 'fileID' in value and not value.get('guid'):
                return references.get(value['fileID'], -1)
            return {k: local_refs(v) for k, v in value.items()}
        return [local_refs(v) for v in value] if isinstance(value, list) else value
    nodes = []
    for ident in ordered:
        t = transforms[ident]
        go = t['m_GameObject']['fileID']
        node = {'name': gameobjects.get(go, {}).get('m_Name', ''), 'parent': ordered.index(t['m_Father']['fileID']) if t['m_Father']['fileID'] in ordered else -1,
                'active': gameobjects.get(go, {}).get('m_IsActive', 1),
                'local': {'p': vec(t['m_LocalPosition']), 'q': vec(t['m_LocalRotation'], 'xyzw'), 's': vec(t['m_LocalScale'])}, 'behaviours': {}}
        components = by_go[go]
        for kind, _, data in components:
            if kind == 198:
                node['system'] = system(data, next((d for k, _, d in components if k == 199), None))
            elif kind == 114 and data.get('m_Enabled', 1):
                script = INDEX.get(data.get('m_Script', {}).get('guid'))
                name = script.stem if script else 'missing script'
                fields = {k: curve(v) for k, v in data.items() if not k.startswith('m_')}
                if name == 'PlacedTetherVisual' and fields.get('DragPrefab', {}).get('guid') in INDEX:
                    fields['DragPrefab'] = str(INDEX[fields['DragPrefab']['guid']].relative_to(ROOT))
                fields = local_refs(fields)
                node['behaviours'][BEHAVIOURS.get(name, name)] = fields or True
                if name == 'ChargePropFeedback':
                    skip('ChargePropFeedback growth/shake/heat played; motion smear, tier kick and moving cue tip not reproduced')
                if name == 'PlacedTetherVisual':
                    skip('PlacedTetherVisual partner placement played; TetherRopePresenter rope/retract lifecycle not reproduced')
                if name not in BEHAVIOURS:
                    skip(f'behaviour {name} exported but not played')
            elif kind == 23:
                mf = next((d for k, _, d in components if k == 33), {})
                node['mesh'] = {'geometry': mesh(mf.get('m_Mesh', {})), 'materials': [material(m) for m in data.get('m_Materials', [])],
                                'enabled': data.get('m_Enabled', 1), 'order': data.get('m_SortingOrder', 0)}
            elif kind == 96:
                node['trail'] = curve(data)
                parameters = data['m_Parameters']
                node['trail']['m_Parameters']['widthCurve'] = curve({'minMaxState': 1, 'scalar': 1, 'maxCurve': parameters['widthCurve']})
                node['trail']['m_Parameters']['colorGradient'] = curve({'minMaxState': 1, 'maxColor': {}, 'maxGradient': parameters['colorGradient']})
                node['trail']['material'] = material((data.get('m_Materials') or [{}])[0])
                skip('TrailRenderer uses bounded camera-facing segments; cap/corner tessellation and per-vertex sorting not reproduced')
            elif kind == 82:
                skip('AudioSource uses existing browser audio path')
            elif kind not in (1, 4, 198, 199, 33):
                skip(f'component class {kind}')
        nodes.append(node)
    imported = IMPORTED.get(CONTEXT)
    if imported:
        mapped = {}
        for index, source in enumerate(imported):
            parent = mapped.get(source['parent'], -1)
            found = next((i for i, node in enumerate(nodes) if node['name'] == source['name'] and node['parent'] == parent), None)
            if found is None:
                found = len(nodes)
                nodes.append(dict(name=source['name'], parent=parent, active=int(source['active']),
                                  local={k: source[k] for k in ('p', 'q', 's')}, behaviours={}))
            mapped[index] = found
            for sub, part in enumerate(source['parts']):
                key = part['id']
                MESHES[key] = {field: [round(x, 7) for row in part[field] for x in row]
                               for field in ('position', 'normal', 'uv') if part[field]}
                MESHES[key]['index'] = part['index']
                mat_key = part['material']
                if not any(k == 21 for k, _, _ in docs(INDEX[mat_key])):
                    mat = part['materialData']
                    MATERIALS[mat_key] = dict(name=mat['name'], texture=texture({'guid': mat['texture']}), blend='alpha',
                                             baseColor=mat['color'], colorMode=0, surface=0, uvScale=[1, 1], uvOffset=[0, 0], queue=2000,
                                             shader='texture', depthTest=4)

                nodes.append(dict(name=source['name'] + f' submesh {sub}', parent=found, active=1,
                                  local=dict(p=[0, 0, 0], q=[0, 0, 0, 1], s=[1, 1, 1]), behaviours={},
                                  mesh=dict(geometry={'id': key}, materials=[material({'guid': part['material']})], enabled=1, order=0)))
    else:
        for kind, _, data in objects:
            if kind == 1001:
                source = INDEX.get(data.get('m_SourcePrefab', {}).get('guid'))
                skip(f'prefab instance/variant {source.relative_to(ROOT) if source else data.get("m_SourcePrefab")} unresolved')
    return {'prefab': str(path.relative_to(ROOT)), 'nodes': nodes}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    TEXTURES.mkdir(parents=True, exist_ok=True)
    refs = json.loads((OUT / 'unity-references.json').read_text())
    effects = {path: effect(ROOT / path) for path in sorted({row['prefab'] for group in refs.values() for row in group.values()})}
    data = {'version': 1, 'presentations': {k: effects[v['prefab']] for k, v in sorted(refs['presentations'].items())},
            'projectiles': {k: dict(effects[v['prefab']], scale=v.get('scale', 1), orientation=v.get('orientation', 0)) for k, v in sorted(refs['projectiles'].items())},
            'materials': MATERIALS, 'meshes': MESHES}
    for key, tint in [('default-core', [.6, 1.6, 2, 1]), ('default-trail', [0, 1.05, 1.5, 1])]:
        MATERIALS[key] = dict(name=key, texture=None, blend='additive', baseColor=tint, colorMode=0, surface=1,
                              uvScale=[1, 1], uvOffset=[0, 0], queue=3000, shader='texture', depthTest=4)
    data['defaults'] = {'projectile': dict(prefab='procedural:ProjectileVFXManager.BuildProjectileVisual', scale=.5, orientation=0,
        nodes=[dict(name='Default projectile', parent=-1, active=1, local=dict(p=[0, 0, 0], q=[0, 0, 0, 1], s=[1, 1, 1]), behaviours={},
                    mesh=dict(geometry={'builtin': 10207}, materials=['default-core'], enabled=1, order=0),
                    trail=dict(m_Enabled=1, m_Time=.2, m_Emitting=1, m_MinVertexDistance=.1, m_SortingOrder=0, material='default-trail',
                               m_Parameters=dict(widthMultiplier=1, widthCurve={'type': 'curve', 'mode': 1, 'scalar': 1,
                                    'maxCurve': [dict(time=0, value=1, inSlope=-1, outSlope=-1), dict(time=1, value=0, inSlope=-1, outSlope=-1)]},
                                    colorGradient={'type': 'gradient', 'mode': 0, 'maxColor': [1, 1, 1, 1]})))])}
    (OUT / 'effects.json').write_text(json.dumps(data, separators=(',', ':'), allow_nan=False) + '\n')
    reference_modules = collections.Counter()
    for group in ('presentations', 'projectiles'):
        for effect_data in data[group].values():
            for node in effect_data['nodes']:
                if 'system' not in node:
                    continue
                reference_modules['InitialModule'] += 1
                for name, key in MODULE_MAP.items():
                    if key in node['system']:
                        reference_modules[name] += 1
    report = {'prefabReferences': sum(len(g) for g in refs.values()), 'uniquePrefabs': len(effects),
              'systems': reference_modules['InitialModule'], 'modules': dict(sorted(reference_modules.items())),
              'uniqueSystems': MODULES['InitialModule'], 'uniqueModules': dict(sorted(MODULES.items())),
              'textures': list(TEXTURE_INFO.values()), 'textureBytes': sum(v['bytes'] for v in TEXTURE_INFO.values()),
              'dataBytes': (OUT / 'effects.json').stat().st_size, 'resolvedImportedPrefabs': sorted(IMPORTED),
              'skipped': {k: sorted(v) for k, v in sorted(SKIPS.items())}}
    (OUT / 'export-coverage.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: v for k, v in report.items() if k not in ('skipped', 'textures')}, indent=2))
    for t in report['textures']:
        if t['original'] != t['size']:
            print(f"DOWNSCALED {t['source']} {t['original']} -> {t['size']}")
    for prefab, features in report['skipped'].items():
        for feature in features:
            print(f'SKIP {prefab}: {feature}')


if __name__ == '__main__':
    main()
