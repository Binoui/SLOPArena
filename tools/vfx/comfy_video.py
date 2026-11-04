#!/usr/bin/env python3
"""Generate a clip locally with Wan 2.2 5B (image to video) through a running ComfyUI.

    python3 tools/vfx/comfy_video.py FIRST.png PROMPT.txt OUT.mp4 [--seconds 5] [--seed 1] [--size 1280x704]
    python3 tools/vfx/comfy_video.py FIRST.png PROMPT.txt OUT.mp4 --control BLOCKOUT.mp4   # motion from our own render

With --control the motion comes from a blockout video we draw or render (shapes sized from a hitbox, timed to ticks):
ComfyUI's "Wan 2.2 5B Fun Control" template takes its Canny edges, so timing and reach follow the blockout and the model
paints the look. It needs wan2.2_fun_control_5B_bf16 too; the clip has the blockout's length and FIRST.png starts it.
The control video should already be edges (white lines on black, like Canny output): `edges` below makes them on the CPU
from a filled-shape blockout. `--gpu-canny` runs the template's Canny node instead (out of memory on 12 GB at 1280x704).

Needs ComfyUI on --server (default http://127.0.0.1:8188) with wan2.2_ti2v_5B_fp16, umt5_xxl_fp8_e4m3fn_scaled
and wan2.2_vae in its models folders: the graph is ComfyUI's bundled "Wan 2.2 5B ti2v" template. The first
frame is the effect on pure black (see flipbook.py); the prompt should end on a black frame. Writes OUT.mp4
and OUT.json (prompt, negative, seed, settings), so a clip can be rebuilt or varied. Costs nothing per clip.
"""
from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

# The template's default negative minus 风格/作品/画作/画面 ("style, artwork, painting"), which fight a toon look.
NEGATIVE = ('过曝，静态，细节模糊不清，字幕，静止，整体发灰，最差质量，低质量，JPEG压缩残留，静止不动的画面，杂乱的背景，'
            'realistic photo, photographic water, white mist, fog, smoke, camera movement, zoom, ground, floor, '
            'reflection, character, person, text, watermark')


def upload(server: str, image: Path, mime: str = 'image/png') -> str:
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="{image.name}"\r\n'
            f'Content-Type: {mime}\r\n\r\n').encode() + image.read_bytes() + \
           f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n--{boundary}--\r\n'.encode()
    request = urllib.request.Request(f'{server}/upload/image', body,
                                     {'Content-Type': f'multipart/form-data; boundary={boundary}'})
    return json.load(urllib.request.urlopen(request))['name']


def graph(image: str, prompt: str, negative: str, width: int, height: int, frames: int, seed: int, steps: int,
          cfg: float, shift: float, prefix: str, control: str | None = None, canny: tuple[float, float] | None = None) -> dict:
    nodes = {
        '37': {'class_type': 'UNETLoader', 'inputs': {'unet_name': 'wan2.2_ti2v_5B_fp16.safetensors', 'weight_dtype': 'default'}},
        '38': {'class_type': 'CLIPLoader', 'inputs': {'clip_name': 'umt5_xxl_fp8_e4m3fn_scaled.safetensors', 'type': 'wan', 'device': 'default'}},
        '39': {'class_type': 'VAELoader', 'inputs': {'vae_name': 'wan2.2_vae.safetensors'}},
        '48': {'class_type': 'ModelSamplingSD3', 'inputs': {'model': ['37', 0], 'shift': shift}},
        '56': {'class_type': 'LoadImage', 'inputs': {'image': image}},
        '6': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['38', 0], 'text': prompt}},
        '7': {'class_type': 'CLIPTextEncode', 'inputs': {'clip': ['38', 0], 'text': negative}},
        '55': {'class_type': 'Wan22ImageToVideoLatent', 'inputs': {'vae': ['39', 0], 'width': width, 'height': height,
                                                                   'length': frames, 'batch_size': 1, 'start_image': ['56', 0]}},
        '3': {'class_type': 'KSampler', 'inputs': {'model': ['48', 0], 'positive': ['6', 0], 'negative': ['7', 0],
                                                   'latent_image': ['55', 0], 'seed': seed, 'steps': steps, 'cfg': cfg,
                                                   'sampler_name': 'uni_pc', 'scheduler': 'simple', 'denoise': 1.0}},
        '8': {'class_type': 'VAEDecode', 'inputs': {'samples': ['3', 0], 'vae': ['39', 0]}},
        '57': {'class_type': 'CreateVideo', 'inputs': {'images': ['8', 0], 'fps': 24}},
        '58': {'class_type': 'SaveVideo', 'inputs': {'video': ['57', 0], 'filename_prefix': prefix, 'format': 'auto', 'codec': 'auto'}},
    }
    if control:  # the bundled "Wan 2.2 5B Fun Control" template: our blockout's Canny edges drive the motion
        nodes['37']['inputs']['unet_name'] = 'wan2.2_fun_control_5B_bf16.safetensors'
        nodes['66'] = {'class_type': 'LoadVideo', 'inputs': {'file': control}}
        nodes['67'] = {'class_type': 'GetVideoComponents', 'inputs': {'video': ['66', 0]}}
        edges = ['67', 0]
        if canny:  # the template's GPU Canny; on a 12 GB card it runs out of memory at full size, so the default
            nodes['68'] = {'class_type': 'Canny', 'inputs': {'image': ['67', 0], 'low_threshold': canny[0], 'high_threshold': canny[1]}}
            edges = ['68', 0]
        nodes['55'] = {'class_type': 'Wan22FunControlToVideo', 'inputs': {
            'positive': ['6', 0], 'negative': ['7', 0], 'vae': ['39', 0], 'width': width, 'height': height, 'length': frames,
            'batch_size': 1, 'ref_image': ['56', 0], 'control_video': edges}}
        nodes['3']['inputs'].update({'positive': ['55', 0], 'negative': ['55', 1], 'latent_image': ['55', 2]})
    return nodes


def edges(blockout: Path, out: Path, width: int = 2) -> Path:
    """Canny-like edges (white lines on black) of a filled-shape blockout video, computed on the CPU."""
    import subprocess
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(blockout), '-vf',
                    f'format=gray,edgedetect=low=0.1:high=0.3,dilation=coordinates={255 if width > 1 else 0}',
                    '-pix_fmt', 'yuv420p', '-crf', '12', str(out)], check=True)
    return out


def files(outputs: dict):
    for node in outputs.values():
        for items in node.values():
            if isinstance(items, list):
                yield from (item for item in items if isinstance(item, dict) and 'filename' in item)


SERVER = 'http://127.0.0.1:8188'


def generate(first: Path, prompt: str, out: Path, *, server: str = SERVER, seconds: float = 5.0, size: str = '1280x704',
             seed: int = 1, steps: int = 20, cfg: float = 5.0, shift: float = 8.0, negative: str = NEGATIVE,
             control: Path | None = None, gpu_canny: bool = False) -> dict:
    """Render one clip to `out` (mp4) and write `out`.json; returns that record. Raises RuntimeError on a ComfyUI error."""
    width, height = (int(v) for v in size.split('x'))
    frames = int(round(seconds * 24 / 4)) * 4 + 1  # Wan wants 4n+1 frames
    if control:  # the clip is as long as the blockout, rounded down to 4n+1 frames
        import subprocess
        count = int(subprocess.run(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v:0', '-show_entries',
                                    'stream=nb_read_frames', '-of', 'csv=p=0', str(control)],
                                   capture_output=True, text=True, check=True).stdout.strip())
        frames = (count - 1) // 4 * 4 + 1
    try:
        image = upload(server, first)
        control_name = upload(server, control, 'video/mp4') if control else None
    except OSError as error:
        raise RuntimeError(f'ComfyUI is not reachable at {server} ({error}); start it with ~/ComfyUI/start.sh') from None
    body = graph(image, prompt, negative, width, height, frames, seed, steps, cfg, shift, f'sloparena_vfx/{out.stem}',
                 control_name, (0.1, 0.6) if gpu_canny else None)
    request = urllib.request.Request(f'{server}/prompt', json.dumps({'prompt': body}).encode(),
                                     {'Content-Type': 'application/json'})
    try:
        prompt_id = json.load(urllib.request.urlopen(request))['prompt_id']
    except urllib.error.HTTPError as error:  # e.g. a missing model file
        raise RuntimeError(f'ComfyUI rejected the graph: {error.read().decode(errors="replace")[:1500]}') from None
    started = time.time()
    while True:
        time.sleep(5)
        history = json.load(urllib.request.urlopen(f'{server}/history/{prompt_id}'))
        if prompt_id not in history:
            continue
        entry = history[prompt_id]
        status = entry.get('status', {})
        if status.get('status_str') == 'error':
            raise RuntimeError(json.dumps(status.get('messages', []))[:2000])
        found = list(files(entry.get('outputs', {})))
        if found:
            break
    item = found[0]
    query = urllib.parse.urlencode({'filename': item['filename'], 'subfolder': item.get('subfolder', ''),
                                    'type': item.get('type', 'output')})
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(urllib.request.urlopen(f'{server}/view?{query}').read())
    record = {'model': 'wan2.2_fun_control_5B_bf16' if control else 'wan2.2_ti2v_5B_fp16', 'first': str(first),
              'control': str(control) if control else None, 'prompt': prompt, 'negative': negative,
              'seed': seed, 'size': [width, height], 'frames': frames, 'steps': steps, 'cfg': cfg,
              'shift': shift, 'seconds': round(time.time() - started)}
    out.with_suffix('.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n')
    return record


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('first', type=Path)
    parser.add_argument('prompt', type=Path, help='text file')
    parser.add_argument('out', type=Path)
    parser.add_argument('--server', default=SERVER)
    parser.add_argument('--seconds', type=float, default=5.0)
    parser.add_argument('--size', default='1280x704')
    parser.add_argument('--seed', type=int, default=1)
    parser.add_argument('--steps', type=int, default=20)
    parser.add_argument('--cfg', type=float, default=5.0)
    parser.add_argument('--shift', type=float, default=8.0)
    parser.add_argument('--negative', default=NEGATIVE)
    parser.add_argument('--control', type=Path, help='edge video (see --edges) whose lines drive the motion')
    parser.add_argument('--edges', action='store_true', help='turn --control from a filled-shape blockout into edges first')
    parser.add_argument('--gpu-canny', action='store_true', help="use the template's GPU Canny node on --control instead")
    args = parser.parse_args()
    if args.control and args.edges:
        args.control = edges(args.control, args.control.with_name(args.control.stem + '-edges.mp4'))
    try:
        record = generate(args.first, args.prompt.read_text().strip(), args.out, server=args.server, seconds=args.seconds,
                          size=args.size, seed=args.seed, steps=args.steps, cfg=args.cfg, shift=args.shift,
                          negative=args.negative, control=args.control,
                          gpu_canny=args.gpu_canny)
    except RuntimeError as error:
        raise SystemExit(str(error))
    print(json.dumps({'clip': str(args.out), 'seconds': record['seconds']}))


if __name__ == '__main__':
    main()
