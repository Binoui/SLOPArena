#!/usr/bin/env python3
"""Encode native gameplay-camera JPG sequences as H.264/yuv420p, and check each stream."""
from pathlib import Path
import json,subprocess,sys,hashlib
ROOT=Path(__file__).resolve().parents[2];E=ROOT/'tmp/nyx-model/integration';V=E/'videos';V.mkdir(exist_ok=True)
candidates=[]
for folder,priority in [('training',0),('training-final',1),('training-delivery',3)]:
    for p in (E/folder).glob('*'):
        if not p.is_dir() or not (p/'states.jsonl').exists():continue
        if folder=='training' and not p.name.startswith('combo-'):continue
        name=p.name.removesuffix('-final').removesuffix('-full').removesuffix('-current')
        rank=priority + (1 if p.name.endswith(('-full','-current','-final')) else 0)
        candidates.append((name,rank,p))
chosen={}
for name,rank,p in sorted(candidates,key=lambda x:(x[0],x[1],str(x[2]))):chosen[name]=p
paths=[chosen[name] for name in sorted(chosen)]
previous={x['name']:x for x in json.loads((E/'video-manifest.json').read_text())} if (E/'video-manifest.json').exists() else {}
results=[]
for p in paths:
    images=sorted(p.glob('*.jpg'))
    if not images:continue
    name=p.name.removesuffix('-final').removesuffix('-full').removesuffix('-current');out=V/(name+'.mp4')
    fingerprintHash=hashlib.sha256((p/'states.jsonl').read_bytes())
    for frame in images:
        fingerprintHash.update(frame.name.encode())
        fingerprintHash.update(frame.read_bytes())
    fingerprint=fingerprintHash.hexdigest()
    old=previous.get(name,{})
    if out.exists() and old.get('sourceFingerprint')!=fingerprint:
        superseded=V/'superseded'/old.get('sourceFingerprint','initial')[:16]
        superseded.mkdir(parents=True,exist_ok=True)
        target=superseded/out.name
        if target.exists():raise RuntimeError(f'Preserve existing superseded evidence {target}')
        out.rename(target)
    if not out.exists():
        cmd=['ffmpeg','-hide_banner','-loglevel','error','-n','-framerate','60','-start_number','0','-i',str(p/'%04d.jpg'),
             '-vf','scale=in_range=pc:out_range=tv,pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p','-an','-c:v','libx264','-threads','2','-preset','fast','-crf','20','-pix_fmt','yuv420p','-color_range','tv','-movflags','+faststart',str(out)]
        subprocess.run(cmd,check=True)
    probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-select_streams','v:0','-show_entries','stream=codec_name,pix_fmt,width,height,r_frame_rate,nb_frames','-of','json',str(out)]))['streams'][0]
    assert probe['codec_name']=='h264' and probe['pix_fmt']=='yuv420p',probe
    assert int(probe['nb_frames'])==len(images),(p,probe)
    results.append({'name':name,'sourceFingerprint':fingerprint,'source':str(p.relative_to(ROOT)),'video':str(out.relative_to(ROOT)),**probe})
    print(name,probe['nb_frames'],'frames',flush=True)
(E/'video-manifest.json').write_text(json.dumps(results,indent=2)+'\n')
