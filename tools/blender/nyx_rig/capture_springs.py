"""Write a bounded gateway Training capture plan. Run the emitted JSON through the gateway."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('output', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
relative = out.relative_to(root).as_posix()
measure = (root/'tools/blender/nyx_rig/measure_springs.eval.cs').read_text()
measure = measure.replace(str(root/'tmp/nyx-model/spring-dress-v3/hem-motion.jsonl'),str(out/'hem-motion.jsonl'))
(out/'measure.eval.cs').write_text(measure)
commands = [
    ['sloparena.photo.prepare','--stage','slop_pit','--fighter','nyx','--training','true','--opponent','fightguy'],
    ['editor_play'], ['eval','--code','if (UnityEditor.EditorApplication.isPaused) Unity.Pipeline.Editor.Commands.PlayModeCommands.PausePlayMode(); return new { paused = UnityEditor.EditorApplication.isPaused };'], ['sloparena.training.arrange','--distance','4','--behavior','Idle'],
    ['eval_file','--file',str(out/'measure.eval.cs')],
]
camera = json.dumps({'pos':[1.8,1.2,2.5], 'look':[0,0,0], 'follow':'fighter', 'lookOffset':[0,0,.72], 'move':True, 'fov':30})
sequences = {
    'idle':[{'ticks':120}],
    'run-turn':[{'ticks':20},{'ticks':60,'moveX':1},{'ticks':60,'moveX':-1},{'ticks':60}],
    'jump-landing':[{'ticks':15},{'ticks':18,'jumpPressed':True,'jumpHeld':True},{'ticks':150}],
    'high-kick':[{'ticks':10},{'ticks':1,'slot':'R'},{'ticks':109}],
}
for name,steps in sequences.items():
    if name == 'high-kick':
        commands.append(['sloparena.training.arrange','--distance','4','--behavior','Idle'])
    frames = sum(step['ticks'] for step in steps)
    commands += [['sloparena.photo.live-begin','--output',relative+'/'+name,'--from','0','--to',str(frames-1),'--step','1','--camera',camera],
                 ['sloparena.training.run','--steps',json.dumps(steps)], ['sloparena.photo.live-end','--wait','0']]
commands.append(['get_console_logs','--severity','error','--limit','20'])
plan = {'commands':commands,'restore':[['sloparena.photo.live-end','--wait','0'],['editor_stop'],['sloparena.photo.restore']]}
(out/'play.json').write_text(json.dumps(plan,indent=2))
print(out/'play.json')
