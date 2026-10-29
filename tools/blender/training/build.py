"""Build the Crash-Test Lab from its verified shell and original procedural art."""
from __future__ import annotations
import json
import os
from pathlib import Path
import random
import sys
import time

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
sys.path.insert(0,str(HERE))
from stagekit import checks,geo
from stagekit.presentation import write as write_presentation
import layout
import deck as platform_art
import hall
import presentation
import textures

TRIANGLE_BUDGET=120_000
MATERIALS=['TL_Lit','TL_Floor','TL_Hall','TL_Glow']
SEED=20261029


class Build:
    def __init__(self,data):
        self.data=data
        self.materials=MATERIALS
        self.rng=random.Random(SEED)
        self.groups={}
        self.roles={}
        self.outlines={}
        self.tops={}
        for solid in data['solids']:
            x0,z0,x1,z1=solid['footprint']
            self.outlines[solid['name']]=[(x0,z0),(x1,z0),(x1,z1),(x0,z1)]
            self.tops[solid['name']]=solid['top']

    def mesh(self,name,role,outline='',material='TL_Lit'):
        if name not in self.groups:
            self.groups[name]=geo.Mesh(name,[material]+[m for m in MATERIALS if m!=material])
            self.groups[name].alpha=0 if material=='TL_Hall' else 1
            self.roles[name]=(role,outline)
        return self.groups[name]

    def verify(self):
        C=self.data['corridor']; problems=[]; report={}
        for name,m in self.groups.items():
            role,outline=self.roles[name]
            entry={'role':role,'triangles':m.triangle_count,'bounds':m.bounds()}
            if role in ('surface','wall'):
                poly=self.outlines[outline]; top=self.tops[outline]
                proud=max([0]+[max((x-a[0])*n[0]+(z-a[1])*n[1] for a,c,n,length in geo.edges(poly)) for x,y,z in m.verts])
                above=max(y-top for x,y,z in m.verts)
                entry.update(proud=proud,above=above)
                if proud>.035 or above>.0001:
                    problems.append(f'{name} leaves its shell skin: proud {proud}, above {above}')
                if role=='surface' and any(abs(y-top)>.0001 for x,y,z in m.verts):
                    problems.append(f'{name} is not coplanar paint')
            else:
                crossing=sum(checks.intersects_corridor([m.verts[i] for i in f],C) for f in m.faces)
                entry['facesInsideCorridor']=crossing
                if crossing: problems.append(f'{name}: {crossing} faces in the open blast corridor')
            report[name]=entry
        return report,problems


def main():
    detailed='--rough' not in sys.argv
    if not detailed: sys.argv.remove('--rough')
    args=checks.build_args('/tmp/training-generated',layout.REPO/'.stage-authoring-cache/training/build')
    t=time.time(); data=layout.load(spec_only=args.spec_only)
    if args.spec_only and not args.no_export: raise SystemExit('Unbaked previews require --no-export.')
    b=Build(data); platform_art.build(b,detailed); hall.build(b,detailed)
    report,problems=b.verify()
    if problems: raise ValueError('\n'.join(problems))
    import bpy
    from stagekit import blender as kit
    out=Path(args.out); out.mkdir(parents=True,exist_ok=True)
    dest=Path(args.report); dest.mkdir(parents=True,exist_ok=True)
    textures.write(out)
    kit.reset_scene(MATERIALS)
    objects=kit.build_objects(b.groups,channels=True)
    # Paint keeps its authored grey; contact shade belongs on the support structure.
    ao=None if args.no_bake else kit.bake_ao(objects,args.samples,2.0,.5,1.0,
                  skip=set(objects)-{n for n in objects if n.endswith(' Plating') or n=='Braced Lattice'})
    digest,object_digests=kit.digest(objects)
    stamp={'generator':'tools/blender/training/build.py','seed':SEED,'source':data['source'],
           'sourceSha256':data['sourceSha256'],'arena':data['arena'],'arenaSha256':data['arenaSha256'],
           'blender':bpy.app.version_string,'materials':MATERIALS,'shadowed':list(presentation.SHADOWED),
           'objects':sorted(objects),'geometryDigest':digest,'problems':len(problems)}
    if not args.no_export:
        kit.export_fbx(objects,str(out/'TrainingLab.fbx'))
        (out/'TrainingLab.layout.json').write_text(json.dumps(stamp,indent=1)+'\n')
        write_presentation(presentation.PRESENTATION,str(out))
    triangles=sum(m.triangle_count for m in b.groups.values())
    summary={**stamp,'triangles':triangles,'budget':checks.budget(triangles,TRIANGLE_BUDGET),
             'meshHealth':kit.mesh_health(objects),'objectDigests':object_digests,'checks':report,
             'corridor':data['corridor'],'rough':not detailed,'aoBakeSeconds':ao,'buildSeconds':round(time.time()-t,1)}
    (dest/'build-report.json').write_text(json.dumps(summary,indent=1)+'\n')
    print('TRAINING_BUILD',json.dumps({k:summary[k] for k in ('triangles','budget','geometryDigest','meshHealth','buildSeconds')}))
    if args.preview:
        import preview
        preview.render(objects,out,dest/'preview',args.views)


if __name__=='__main__':
    main()
