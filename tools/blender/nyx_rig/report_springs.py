"""Report hip-relative hem deformation and encode native Training frame captures."""
import json
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

out = Path(sys.argv[1]).resolve()
decoder = json.JSONDecoder()
text = (out/'play.jsonl').read_text()
receipts = []
while text.strip():
    obj, end = decoder.raw_decode(text.lstrip()); receipts.append(obj); text = text.lstrip()[end:]
runs = [r['data']['result'] for r in receipts if r.get('data',{}).get('command') == 'sloparena.training.run']
assert len(runs) == 4 and all(r['success'] for r in runs)
(out/'training-run-receipts.json').write_text(json.dumps(runs,indent=2))
rows = [json.loads(line) for line in (out/'hem-motion.jsonl').read_text().splitlines()]
points_all = np.array([r['hem'] for r in rows])
assert np.isfinite(points_all).all() and np.linalg.norm(points_all,axis=2).max() < 3, 'Invalid hem coordinates or a doubled FBX scale'
assert len(rows[0]['hem']) >= 48, 'The probe did not cover the rest hem'
names = ['idle','run-turn','jump-landing','high-kick']
windows = {name:(r['startTick']+1,r['endTick']) for name,r in zip(names,runs)}
for name,step in [('run',runs[1]['steps'][1]),('turn',runs[1]['steps'][2])]: windows[name]=(step['startTick'],step['endTick'])
reference_rows = [r for r in rows if windows['idle'][1]-20 <= r['tick'] <= windows['idle'][1]]
reference_rows = reference_rows[-20:]
reference = np.mean([r['hem'] for r in reference_rows],axis=0)
report = {'samples':len(rows),'hem_vertices':len(reference),'reference_ticks':[r['tick'] for r in reference_rows],
          'method':'Same baked mesh hem vertices in metres. Remove animated Hips translation and rotation, preserve world scale. Compare to the final 20 idle ticks, averaged. Run and turn windows come from Shared Training step receipts.', 'windows':{}}
for name,(start,end) in windows.items():
    selected=[r for r in rows if start <= r['tick'] <= end]
    assert selected,name
    points=np.array([r['hem'] for r in selected]); distances=np.linalg.norm(points-reference,axis=2)
    centroid=np.linalg.norm((points-reference).mean(1),axis=1)
    report['windows'][name]={'ticks':[start,end],'samples':len(selected),'peak_vertex_displacement_m':float(distances.max()),
       'rms_vertex_displacement_m':float(np.sqrt(np.mean(distances**2))),'peak_hem_centroid_displacement_m':float(centroid.max()),
       'p95_vertex_displacement_m':float(np.percentile(distances,95))}
(out/'motion-report.json').write_text(json.dumps(report,indent=2))
video = {}
sheet = Image.new('RGB',(1600,4*310),'#e4e6eb'); draw=ImageDraw.Draw(sheet)
for j,name in enumerate(names):
    subprocess.run(['ffmpeg','-y','-v','error','-framerate','60','-i',str(out/name/'%04d.jpg'),'-vf','scale=in_range=pc:out_range=tv,pad=ceil(iw/2)*2:ceil(ih/2)*2,format=yuv420p','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-color_range','tv','-movflags','+faststart',str(out/(name+'.mp4'))],check=True)
    video[name]=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-of','json',str(out/(name+'.mp4'))]))
    assert all(s['codec_name'] == 'h264' and s['pix_fmt'] == 'yuv420p' for s in video[name]['streams'])
    frames=sorted((out/name).glob('*.jpg'))
    for i,fraction in enumerate([.1,.35,.55,.8]):
        im=Image.open(frames[min(len(frames)-1,int(len(frames)*fraction))]).convert('RGB');im.thumbnail((400,270));sheet.paste(im,(i*400,j*310+35))
    draw.text((12,j*310+8),name,fill='black',font_size=20)
sheet.save(out/'clips-contact-sheet.jpg',quality=95)
(out/'video-verification.json').write_text(json.dumps(video,indent=2))
print(json.dumps(report,indent=2))
