#!/usr/bin/env python3
"""Wire approved Nyx clip IDs and cosmetic cues, proving gameplay is unchanged."""
from pathlib import Path
import copy,json,re
ROOT=Path(__file__).resolve().parents[2];P=ROOT/'client/Unity/Assets/CharacterPackages/nyx/character.json';E=ROOT/'tmp/nyx-model/integration'
PREFIX='presentation.nyx.'
EFFECTS={'real-g1':'shadow-claws','real-g3':'rising-hands','real-g4':'floor-lash','real-a1':'shadow-ring','real-a3':'up-claw','real-a4':'spike-fist',
 'fake-g1':'phone-slaps','fake-g3':'pole-uppercut','fake-g4':'stomp-impact','fake-a1':'spin-arc','fake-a3':'pole-overhead','fake-a4':'dropkick-spike',
 'real-q':'crow-launch','real-e':'bat-scatter','real-r':'grasp-warning','swap-golive':'going-live','fake-q':'ring-light','fake-e':'rotor-arcs','fake-r':'selfie-pop','swap-logoff':'logging-off'}
def gameplay(value):
    if isinstance(value,list):return [gameplay(x) for x in value if not isinstance(x,dict) or x.get('kind')!='emitPresentation']
    if isinstance(value,dict):return {k:gameplay(v) for k,v in value.items() if k not in ('animationIds','presentation','presentationIds','hitPresentationId','warningPresentationId','catchPresentationId','releasePresentationId','presentationId','description','aimAnimationId')}
    return value

def wire():
    before=json.loads(P.read_text());doc=copy.deepcopy(before);plan=json.loads((E/'binding-plan.json').read_text())['clips'];effects=[]
    for row in plan:
        form=doc if row['form']=='real' else doc['alternateForm'];slot=row['slot'];move=row['move']
        if slot in ('idle','run'):
            form.setdefault('presentation',{})[slot]=row['animationId'];continue
        s=next(x for x in form['slots'] if x['id']==slot);stage=s['timeline']['stages'][0]
        stage['animationIds']=[row['animationId']]
        s['description']=re.sub(r'\. FightGuy.*?placeholder, scaled to \d+ ticks\.', '.',s['description'])
        if move=='swap-golive':s['description']='A white and pink flash swaps to Nyxxie and pulls nearby enemies in.'
        if move=='swap-logoff':s['description']='A violet shard burst swaps to Nyx and pushes nearby enemies away.'
        if s.get('aimMode')=='none':s.pop('aimAnimationId',None)
        own=PREFIX+EFFECTS[move]
        def cue(tick,duration,ident=own):
            op=dict(kind='emitPresentation',tick=tick,unit='ticks',presentationId=ident,placement=dict(attachmentMode='bone',boneId='bone.hips',durationTicks=duration))
            if not any(o.get('kind')=='emitPresentation' and o.get('presentationId')==ident and o['tick']==tick for o in stage['operations']):stage['operations'].append(op)
            effects.append(dict(move=move,presentationId=ident,tick=tick,durationTicks=duration,trigger='timeline'))
        if move in ('real-g1','real-g3','real-g4','real-a1','real-a3','real-a4','fake-g1','fake-g3','fake-g4','fake-a1','fake-a3','fake-a4','fake-q'):
            windows=sorted(set((o['tick'],o['hitbox']['durationTicks']) for o in stage['operations'] if o['kind']=='spawnHitbox'))
            for t,n in windows:cue(t,n)
            s['hitPresentationId']=PREFIX+('shadow-hit' if row['form']=='real' else 'pink-hit')
        elif move=='real-q':
            cue(16,12)
            proj=next(o['projectile'] for o in stage['operations'] if o['kind']=='spawnProjectile');proj['perch']['pop']['presentationId']=PREFIX+'crow-pop'
        elif move=='real-r':
            at=next(o['parameters']['atTarget'] for o in stage['operations'] if o['kind']=='startCapability')
            at['warningPresentationId']=own;at['catchPresentationId']=PREFIX+'grasp-catch'
        elif move=='real-e':cue(0,50)
        elif move=='fake-e':cue(0,50)
        elif move=='fake-r':
            cue(44,8)
            p=next(o['parameters'] for o in stage['operations'] if o['kind']=='startCapability');p['releasePresentationId']=PREFIX+'selfie-release'
        elif move.startswith('swap-'):
            tick=next(o['tick'] for o in stage['operations'] if o['kind']=='swapForm');cue(tick,14)
            s['hitPresentationId']=PREFIX+('pink-hit' if move=='swap-golive' else 'shadow-hit')
        stage['operations'].sort(key=lambda o:o['tick'])
    extra=['grasp-catch','grasp-drag','crow','crow-pop','shadow-hit','pink-hit','selfie-release']
    doc['presentationIds']=list(dict.fromkeys(doc['presentationIds']+[PREFIX+v for v in EFFECTS.values()]+[PREFIX+v for v in extra]))
    assert gameplay(before)==gameplay(doc),'Gameplay change detected'
    P.write_text(json.dumps(doc,indent=2)+'\n');(E/'effect-plan.json').write_text(json.dumps({'effects':effects,'presentationIds':doc['presentationIds'],'gameplayUnchanged':True},indent=2)+'\n')
    print('Gameplay unchanged; 24 clip IDs; ',len(doc['presentationIds']),'presentation IDs')
if __name__=='__main__':wire()
