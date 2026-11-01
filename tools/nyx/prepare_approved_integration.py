#!/usr/bin/env python3
"""Promote the owner's 24 accepted Nyx takes; stage a repeatable native Unity binding manifest.

No Editor calls and no edits to the rig/catalog. AnimationUtility in NyxAnimationIntegration
writes the three clock-remapped clips before binding. Gameplay clocks and volumes are invariant.
"""
from pathlib import Path
import json, uuid, hashlib, re, argparse, sys
ROOT=Path(__file__).resolve().parents[2];U=ROOT/'client/Unity';PKG=U/'Assets/CharacterPackages/nyx';E=ROOT/'tmp/nyx-model/integration'
sys.path.insert(0,str(ROOT/'tools/genra'))
from retime_anim import stretch
NAMESPACE=uuid.UUID('33f87629-a3ee-4f86-afd3-180d80bcaaf6')
def meta(path):
    m=Path(str(path)+'.meta')
    if not m.exists():
        guid=uuid.uuid5(NAMESPACE,str(path.relative_to(U))).hex
        m.write_text(f'fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 7400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
def prepare():
    statuses=json.loads((ROOT/'tmp/nyx-model/genra/board/status.json').read_text());timing=json.loads((ROOT/'tmp/nyx-model/genra/timing.json').read_text())['moves']
    doc=json.loads((PKG/'character.json').read_text());rows=[]
    for move,status in statuses.items():
        spec=timing[move];d=doc if spec['form']=='real' else doc['alternateForm'];slot=next((s for s in d['slots'] if s['id']==spec['slot']),None)
        total=slot['timeline']['stages'][0]['durationTicks'] if slot else spec['game']['total']
        remap={'real-e':[(0,10),(24,34),(30,40),(50,50)],'swap-golive':[(0,0),(9,48),(30,96)],'swap-logoff':[(0,0),(10,40),(33,96)]}.get(move,[])
        if remap:
            original=remap[-1][0]
            remap=[(int(a*total/original+0.5),b) for a,b in remap]
        source=U/status['unity']['asset'];dest=PKG/'Animations'/('Source' if remap else '')/f'nyx-{move}.anim'
        dest.parent.mkdir(parents=True,exist_ok=True)
        expected=source.read_bytes()
        authored=spec['authored']['total']
        if spec['kind']=='run':
            authored=spec.get('retimedClipTicks',authored)
            expected=stretch(expected.decode(),authored).encode()
        if not dest.exists():dest.write_bytes(expected)
        elif dest.read_bytes()!=expected:raise ValueError(f'Unexpected edits to promoted clip {dest}')
        meta(dest)
        output=PKG/'Animations'/f'nyx-{move}.anim'
        rows.append(dict(move=move,form=spec['form'],slot=spec['slot'],animationId=spec['semanticId'],poseTrackId=spec['semanticId'],
            source=str(dest.relative_to(U)),asset=str(output.relative_to(U)),authoredTicks=authored,durationTicks=total,
            speed=1 if remap or spec['kind']=='run' else authored/total,remap=[{'tick':a,'sourceTick':b} for a,b in remap],
            candidateSha256=hashlib.sha256(source.read_bytes()).hexdigest(),approval='Owner approved 2026-10-29 including real-a2, real-a3, real-e and fake-run check exceptions.'))
    plan={'clips':rows};(PKG/'Animations/NyxApprovedClips.json').write_text(json.dumps(plan,indent=2)+'\n');E.mkdir(parents=True,exist_ok=True);(E/'binding-plan.json').write_text(json.dumps(plan,indent=2)+'\n')
    print(f'{len(rows)} promoted source clips; {sum(bool(r["remap"]) for r in rows)} native remaps pending')
if __name__=='__main__':prepare()
