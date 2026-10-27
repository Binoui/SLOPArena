"""Build Manki Mayhem with stagekit; requires the exact authoritative baked shell.

blender -b --factory-startup --python-exit-code 1 --python tools/blender/manki_mayhem/build.py -- \
    --out /tmp/manki-mayhem-generated --report tmp/manki-mayhem-stage/massing --massing

Omit --massing for the detailing pass. --dry runs numeric checks outside Blender.
"""
from __future__ import annotations
import json
import math
import os
import random
import sys
import time
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
sys.path.insert(0,str(HERE.parent))
from stagekit import checks, geo
from stagekit.presentation import write as write_presentation
import layout
import presentation
import textures
import deck
import booth
import venue
from parts import MATERIALS

TRIANGLE_BUDGET=240_000
SEED=20261027
DEFAULT_OUT='/tmp/manki-mayhem-generated'
DEFAULT_REPORT=layout.REPO/'tmp/manki-mayhem-stage/build'
NO_AO={'Concrete Deck','Audience','Audience Fences','Lighting Rig','CRT Walls','Side Identities','Venue Backlight','Warehouse Light Pools','Host Booth','Warm Light Shafts'}


class Build:
    def __init__(self,data):
        self.data=data
        self.groups={}
        self.roles={}
        self.rng=random.Random(SEED)

    def group(self,name,role):
        if name not in self.groups:
            self.groups[name]=geo.Mesh(name,MATERIALS)
            self.groups[name].alpha=1 if role in ('surface','wall') else 0
            self.roles[name]=role
        return self.groups[name]

    def verify(self):
        c=self.data['corridor']
        camera={**c,'minX':c['minX']-checks.CAMERA_REACH,'maxX':c['maxX']+checks.CAMERA_REACH,
                'minZ':c['minZ']-checks.CAMERA_REACH,'maxZ':c['maxZ']+checks.CAMERA_REACH,
                'killY':c['killY']-checks.CEILING_CLEARANCE,'topY':c['topY']+16}
        report={}; problems=[]
        for name,m in self.groups.items():
            role=self.roles[name]
            entry={'role':role,'triangles':m.triangle_count,'bounds':m.bounds()}
            if role=='surface':
                bad=sum(abs(y)>1e-5 or abs(x)>12.00001 or abs(z)>10.00001 for x,y,z in m.verts)
                if bad: problems.append('%s: %d vertices break the flat deck footprint'%(name,bad))
                entry['invalidSurfaceVertices']=bad
            elif role=='wall':
                bad=sum(y>1e-5 or abs(x)>12.02501 or abs(z)>10.02501 for x,y,z in m.verts)
                if bad: problems.append('%s: %d vertices extend beyond the 2.5 cm shell tolerance'%(name,bad))
                entry['invalidWallVertices']=bad
            else:
                # Actual convex faces, not just bounds or vertices: prevents large panels
                # or diagonal braces cutting through fighters or the camera's reach.
                for label,box in [('blast',c),('camera',camera)]:
                    bad=0
                    for face in m.faces:
                        points=[m.verts[i] for i in face]
                        # Cheap rejection before full polygon clipping.
                        if any(max(p[a] for p in points)<=lo or min(p[a] for p in points)>=hi
                               for a,lo,hi in [(0,box['minX'],box['maxX']),(1,box['killY'],box['topY']),(2,box['minZ'],box['maxZ'])]):
                            continue
                        if checks.intersects_corridor(points,box): bad+=1
                    entry[label+'IntersectingFaces']=bad
                    if bad: problems.append('%s: %d faces intersect the %s envelope'%(name,bad,label))
            report[name]=entry
        return report,problems


def generate(data,detail=True):
    b=Build(data)
    deck.build(b,detail)
    booth.build(b,detail)
    crowd=venue.build(b,detail)
    # Work lights live at show level. The support towers fade below that band;
    # their bright lattice must not compete with falling fighters near the KO line.
    for name,m in b.groups.items():
        if b.roles[name]!='background': continue
        for face,colors in zip(m.faces,m.colors):
            for j,index in enumerate(face):
                y=m.verts[index][1]
                factor=.38+.62*max(0,min(1,(y+10)/10))
                color=colors[j]
                if color[3]<.01:
                    x,_,z=m.verts[index]
                    # CRT spill is strongest on the near gantries, falls off before
                    # the crowd, and never touches the deck's separate lit material.
                    tv=max(math.exp(-((x-sx)/9)**2-((y-6)/9)**2-((z-39)/6)**2) for sx in (-22.5,22.5))
                    host=math.exp(-(x/11)**2-((y-8)/8)**2-((z-41)/7)**2)
                    spill=max(tv*.45,host*.36)
                    colors[j]=geo.mix(tuple(v*factor for v in color[:3]),(.69,.38,.16),spill)+(color[3],)
    return b,crowd


def main():
    massing='--massing' in sys.argv
    if massing: sys.argv.remove('--massing')
    dry='--dry' in sys.argv
    if dry: sys.argv.remove('--dry')
    a=checks.build_args(DEFAULT_OUT,DEFAULT_REPORT)
    data=layout.load(spec_only=a.spec_only)
    b,crowd=generate(data,not massing)
    report,problems=b.verify()
    triangles=sum(m.triangle_count for m in b.groups.values())
    summary={'triangles':triangles,'budget':checks.budget(triangles,TRIANGLE_BUDGET),
             'crowd':crowd,'massing':massing,'corridor':data['corridor'],
             'source':data['source'],'sourceSha256':data['sourceSha256'],'arenaSha256':data['arenaSha256'],
             'anchors':booth.HOST_ANCHOR,'hostPlatform':booth.PLATFORM,'checks':report,'problems':problems}
    if dry:
        print(json.dumps(summary,indent=1))
        if problems: raise SystemExit(1)
        return
    if a.spec_only and not a.no_export: raise SystemExit('--spec-only requires --no-export')
    if problems: raise SystemExit('Clearance problems: '+json.dumps(problems))
    if summary['budget']['over']: raise SystemExit('Planned triangle budget exceeded')
    Path(a.out).mkdir(parents=True,exist_ok=True)
    Path(a.report).mkdir(parents=True,exist_ok=True)
    import bpy
    from stagekit import blender as kit
    t=time.time()
    textures.atlas(str(Path(a.out)/'MankiMayhemAtlas.png'))
    textures.floor(str(Path(a.out)/'MankiMayhemFloor.png'))
    textures.detail(str(Path(a.out)/'MankiMayhemDetail.png'))
    kit.reset_scene(MATERIALS)
    objects=kit.build_objects(b.groups,channels=True)
    def keep_glow(me,rgba):
        import numpy as np
        return np.isin(kit.loop_materials(me),('MM_Night','MM_Screen')) & (rgba[:,3]>.01)
    if not a.no_bake:
        summary['aoBakeSeconds']=kit.bake_ao(objects,a.samples,ao_distance=2.5,floor=.45,gamma=1.05,
                                           skip=NO_AO,keep=keep_glow)
    digest,object_digests=kit.digest(objects)
    summary.update(geometryDigest=digest,objectDigests=object_digests,meshHealth=kit.mesh_health(objects))
    if not a.no_export:
        kit.export_fbx(objects,str(Path(a.out)/'MankiMayhemArena.fbx'),anchors=booth.HOST_ANCHOR)
        stamp={'generator':'tools/blender/manki_mayhem/build.py','seed':SEED,
               'source':data['source'],'sourceSha256':data['sourceSha256'],
               'arena':data['arena'],'arenaSha256':data['arenaSha256'],'blender':bpy.app.version_string,
               'materials':MATERIALS,'shadowed':list(presentation.SHADOWED),'objects':sorted(objects),
               'anchors':booth.HOST_ANCHOR,'geometryDigest':digest,'problems':len(problems)}
        (Path(a.out)/'MankiMayhemArena.layout.json').write_text(json.dumps(stamp,indent=1)+'\n')
        write_presentation(presentation.PRESENTATION,a.out)
    summary['buildSeconds']=round(time.time()-t,1)
    (Path(a.report)/'build-report.json').write_text(json.dumps(summary,indent=1)+'\n')
    print('MANKI_MAYHEM_BUILD',json.dumps({k:summary[k] for k in ('triangles','crowd','geometryDigest','buildSeconds','meshHealth','problems')}))
    if a.preview:
        import preview
        preview.render(b,objects,str(Path(a.report)/'preview'),a.out,a.views)


if __name__=='__main__': main()
