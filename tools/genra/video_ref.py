"""Visual references from video models (OpenRouter): one prompt and first frame, several models, one comparison video;
then the chosen clip's beats as a key-pose sheet for a brief.

    <genra python> tools/genra/video_ref.py generate PROMPT.txt --first-frame FRAME.jpg --models bytedance/seedance-2.5,... \
        [--duration 8] [--resolution 720p] [--out artifacts/video-refs/<name>] [--dry-run]
    <genra python> tools/genra/video_ref.py frames CLIP.mp4 --at 0.9,1.5,3.0 --labels crouch,launch,impact [--out DIR]
    <genra python> tools/genra/video_ref.py studio RENDER.png --out FRAME.jpg

The clips are storyboards for a brief: poses, silhouettes and ideas to pick a direction from and to show the animator. They
are not timing or physics (generated video floats and morphs props), and never motion capture: the owner wants hand-made,
cartoony motion. The first frame should be the character's own Unity render (unity_clip.py render --background), so the
clips show him and not a lookalike; `studio` puts a transparent render (a prop the toolkit cannot attach yet, like Sir Bonk's
hobby horse) on the same studio floor. The key comes from $OPENROUTER_API_KEY or ~/.config/sloparena/openrouter.key, never the
repository. Each clip spends real credits: check GET /api/v1/videos/models for prices first. `generate` writes one MP4 per model,
compare.mp4 (every clip in the folder tiled and labelled, the shorter ones holding their last frame) and manifest.json.
Each model gets the nearest resolution and duration it supports. `frames` writes beat-NN-<label>.png at the given seconds,
key-poses.png (the labelled sheet a brief attaches and the owner checks) and beats.json, which a brief maps to game ticks.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

API = 'https://openrouter.ai/api/v1'
KEY_FILE = Path.home() / '.config/sloparena/openrouter.key'
FONT = '/usr/share/fonts/TTF/DejaVuSans.ttf'


def key() -> str:
    value = os.environ.get('OPENROUTER_API_KEY') or (KEY_FILE.read_text().strip() if KEY_FILE.exists() else '')
    if not value:
        raise SystemExit(f'No OpenRouter key: set OPENROUTER_API_KEY or write it to {KEY_FILE} (chmod 600)')
    return value


def absolute(url: str) -> str:
    """OpenRouter returns polling and content URLs as absolute URLs, /api/v1/... paths or bare /videos/... paths."""
    if url.startswith('http'):
        return url
    return 'https://openrouter.ai' + url if url.startswith('/api/') else API + url


def call(method: str, url: str, body: dict | None = None, raw: bool = False):
    request = urllib.request.Request(absolute(url), method=method,
                                     data=json.dumps(body).encode() if body is not None else None,
                                     headers={'Authorization': f'Bearer {key()}', 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            data = response.read()
    except urllib.error.HTTPError as error:
        raise RuntimeError(f'{method} {url}: {error.code} {error.read().decode(errors="replace")[:600]}') from None
    return data if raw else json.loads(data)


def supported() -> dict:
    """Each video model's supported resolutions and durations (public endpoint, no key)."""
    with urllib.request.urlopen(API + '/videos/models', timeout=60) as response:
        return {m['id']: m for m in json.loads(response.read())['data']}


def closest(wanted, options, key=lambda v: v):
    return wanted if not options or wanted in options else min(options, key=lambda o: abs(key(o) - key(wanted)))


def height(resolution: str) -> int:
    return {'1K': 1080, '2K': 1440, '4K': 2160}.get(resolution, int(resolution.rstrip('p')) if resolution.rstrip('p').isdigit() else 720)


def request_body(model: str, prompt: str, frame: Path, duration: int, resolution: str, aspect: str) -> dict:
    mime = 'image/png' if frame.suffix.lower() == '.png' else 'image/jpeg'
    url = f'data:{mime};base64,' + base64.b64encode(frame.read_bytes()).decode()
    return {'model': model, 'prompt': prompt, 'duration': duration, 'resolution': resolution, 'aspect_ratio': aspect,
            'generate_audio': False,
            'frame_images': [{'type': 'image_url', 'image_url': {'url': url}, 'frame_type': 'first_frame'}]}


def generate(model: str, body: dict, out: Path, attempts: int = 60, wait: int = 15) -> dict:
    """Submit one job, poll it to the end and download its clip. Returns the final job (with error text on failure)."""
    try:
        job = call('POST', '/videos', body)
        for _ in range(attempts):
            if job.get('status') in ('completed', 'failed', 'cancelled', 'expired'):
                break
            time.sleep(wait)
            job = call('GET', job.get('polling_url') or f"/videos/{job['id']}")
        if job.get('status') != 'completed':
            return job | {'model': model, 'error': job.get('error') or f"ended {job.get('status')}"}
        url = absolute((job.get('unsigned_urls') or [None])[0] or f"/videos/{job['id']}/content?index=0")
        if url.startswith('https://openrouter.ai/'):
            data = call('GET', url, raw=True)
        else:  # a provider's signed URL: no OpenRouter key
            with urllib.request.urlopen(url, timeout=300) as response:
                data = response.read()
        path = out / (model.replace('/', '__') + '.mp4')
        path.write_bytes(data)
        return job | {'model': model, 'path': str(path)}
    except Exception as error:  # one failing provider must not lose the others' clips
        return {'model': model, 'status': 'error', 'error': str(error)}


def compare(clips: list[tuple[str, Path]], path: Path, cols: int = 2, cell=(640, 360)) -> Path:
    """Tile clips into one labelled video; shorter clips hold their last frame."""
    inputs, filters, labels = [], [], []
    w, h = cell
    for i, (label, clip) in enumerate(clips):
        inputs += ['-i', str(clip)]
        text = label.replace(':', r'\:').replace("'", '')
        filters.append(f'[{i}:v]fps=24,scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,'
                       f"setsar=1,drawtext=fontfile={FONT}:text='{text}':x=12:y=10:fontsize=22:fontcolor=white:"
                       f'box=1:boxcolor=black@0.55:boxborderw=6,tpad=stop_mode=clone:stop_duration=20[v{i}]')
        labels.append(f'[v{i}]')
    n = len(clips)
    layout = '|'.join(f'{(i % cols) * w}_{(i // cols) * h}' for i in range(n))
    longest = max(float(subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', str(c)],
                                       capture_output=True, text=True).stdout or 0) for _, c in clips)
    graph = ';'.join(filters) + ';' + ''.join(labels) + (f'xstack=inputs={n}:layout={layout}:fill=black[out]' if n > 1
                                                         else 'null[out]')
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', *inputs, '-filter_complex', graph, '-map', '[out]',
                    '-t', f'{longest:.2f}', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', str(path)], check=True)
    return path


def frames(clip: Path, seconds: list[float], labels: list[str], out: Path, cell: int = 420, crop=None) -> Path:
    """The clip's frames at `seconds` as beat-NN-<label>.png, a labelled key-pose sheet and beats.json. `crop` (x, y, w,
    h in clip pixels) keeps the character, not the studio around him, when a wide shot makes him small."""
    from PIL import Image, ImageDraw, ImageFont
    out.mkdir(parents=True, exist_ok=True)
    font = ImageFont.truetype(FONT, 20) if Path(FONT).exists() else ImageFont.load_default()
    beats, cells = [], []
    for k, (at, label) in enumerate(zip(seconds, labels), 1):
        path = out / f'beat-{k:02d}-{label}.png'
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-ss', f'{at:.3f}', '-i', str(clip),
                        *(['-vf', 'crop={2}:{3}:{0}:{1}'.format(*crop)] if crop else []), '-frames:v', '1', str(path)], check=True)
        beats.append({'beat': k, 'label': label, 'seconds': at, 'image': str(path)})
        image = Image.open(path).convert('RGB')
        image = image.resize((cell, round(image.height * cell / image.width)), Image.LANCZOS)
        ImageDraw.Draw(image).text((8, 6), f'{k}. {label}  ({at:.2f} s)', font=font, fill=(255, 255, 255),
                                   stroke_width=2, stroke_fill=(0, 0, 0))
        cells.append(image)
    cols = min(4, len(cells))
    rows = -(-len(cells) // cols)
    sheet = Image.new('RGB', (cols * cell, rows * cells[0].height), (24, 28, 36))
    for k, image in enumerate(cells):
        sheet.paste(image, ((k % cols) * cell, (k // cols) * cells[0].height))
    sheet.save(out / 'key-poses.png')
    (out / 'beats.json').write_text(json.dumps({'clip': str(clip), 'beats': beats}, indent=2))
    return out / 'key-poses.png'


def studio(render: Path, out: Path, floor_drop: float = 0.06) -> Path:
    """A transparent render (RGBA, already framed) on the studio floor of the video first frames: a light grey backdrop
    whose horizon sits `floor_drop` of the height above his feet, a soft contact shadow under the feet and a fainter one
    under the rest of the silhouette."""
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
    fg = Image.open(render).convert('RGBA')
    w, h = fg.size
    alpha = np.asarray(fg)[..., 3]
    rows = np.where(alpha.max(axis=1) > 32)[0]
    if not len(rows):
        raise SystemExit(f'{render}: nothing opaque to put on the floor')
    bottom = int(rows.max())
    horizon = max(1, int(bottom - floor_drop * h))
    ys = np.arange(h, dtype=float)[:, None]
    top, mid, near, low = (np.array(c, dtype=float) for c in ((213, 216, 224), (207, 211, 218), (197, 201, 209), (183, 187, 195)))
    sky = top + (mid - top) * (ys / horizon)
    ground = near + (low - near) * ((ys - horizon) / max(h - horizon, 1))
    column = np.where(ys < horizon, sky, ground)
    backdrop = Image.fromarray(np.broadcast_to(column[:, None, :], (h, w, 3)).astype(np.uint8)).filter(
        ImageFilter.GaussianBlur(4)).convert('RGBA')
    shadow = Image.new('L', (w, h), 0)
    draw = ImageDraw.Draw(shadow)
    feet = np.where(alpha[max(0, bottom - int(0.035 * h)):bottom + 1].max(axis=0) > 32)[0]
    every = np.where(alpha.max(axis=0) > 32)[0]
    y = bottom - 0.006 * h
    wide = (every.max() - every.min()) * 0.55
    draw.ellipse(((every.min() + every.max()) / 2 - wide, y - 0.025 * h, (every.min() + every.max()) / 2 + wide, y + 0.025 * h), fill=34)
    half = (feet.max() - feet.min()) * 0.75 + 0.02 * w
    draw.ellipse(((feet.min() + feet.max()) / 2 - half, y - 0.017 * h, (feet.min() + feet.max()) / 2 + half, y + 0.017 * h), fill=96)
    shadow = shadow.filter(ImageFilter.GaussianBlur(0.012 * w))
    tint = Image.new('RGBA', (w, h), (60, 64, 74, 0))
    tint.putalpha(shadow)
    frame = Image.alpha_composite(Image.alpha_composite(backdrop, tint), fg).convert('RGB')
    out.parent.mkdir(parents=True, exist_ok=True)
    frame.save(out, quality=90)
    return out


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = parser.add_subparsers(dest='command', required=True)
    gen = sub.add_parser('generate', help='one prompt and first frame on several models, plus compare.mp4')
    gen.add_argument('prompt', help='a text file with the prompt')
    gen.add_argument('--first-frame', required=True, help="the character's own render (JPEG or PNG)")
    gen.add_argument('--models', required=True, help='comma-separated OpenRouter video model ids')
    gen.add_argument('--duration', type=int, default=8)
    gen.add_argument('--resolution', default='720p')
    gen.add_argument('--aspect', default='16:9')
    gen.add_argument('--out', help='output folder (default: next to the prompt file)')
    gen.add_argument('--dry-run', action='store_true', help='print the requests (without the image data) and stop')
    beat = sub.add_parser('frames', help="a chosen clip's beats as a key-pose sheet (no credits)")
    beat.add_argument('clip')
    beat.add_argument('--at', required=True, help='comma-separated seconds, one per beat')
    beat.add_argument('--labels', required=True, help='comma-separated beat names')
    beat.add_argument('--out', help='output folder (default: <clip>-beats)')
    beat.add_argument('--crop', help='x,y,w,h in clip pixels: keep the character when a wide shot makes him small')
    floor = sub.add_parser('studio', help='put a transparent render on the studio floor of the first frames (no credits)')
    floor.add_argument('render', help='an RGBA PNG, already framed at the video size')
    floor.add_argument('--out', required=True, help='the first frame to write (JPEG or PNG)')
    args = parser.parse_args(argv)
    if args.command == 'studio':
        print(studio(Path(args.render), Path(args.out)))
        return 0
    if args.command == 'frames':
        seconds = [float(x) for x in args.at.split(',')]
        labels = [x.strip() for x in args.labels.split(',')]
        if len(seconds) != len(labels):
            parser.error('--at and --labels need one entry per beat')
        clip = Path(args.clip)
        crop = tuple(int(v) for v in args.crop.split(',')) if args.crop else None
        print(frames(clip, seconds, labels, Path(args.out) if args.out else clip.with_name(clip.stem + '-beats'), crop=crop))
        return 0
    prompt_path, frame = Path(args.prompt), Path(args.first_frame)
    prompt = prompt_path.read_text().strip()
    out = Path(args.out) if args.out else prompt_path.parent / prompt_path.stem
    models = [m for m in args.models.split(',') if m]
    specs = supported()
    bodies = {}
    for m in models:  # each model at the nearest resolution and duration it supports
        spec = specs.get(m, {})
        bodies[m] = request_body(m, prompt, frame, closest(args.duration, spec.get('supported_durations')),
                                 closest(args.resolution, spec.get('supported_resolutions'), height), args.aspect)
    if args.dry_run:
        for m, body in bodies.items():
            shown = json.loads(json.dumps(body))
            shown['frame_images'][0]['image_url']['url'] = shown['frame_images'][0]['image_url']['url'][:40] + '...'
            print(json.dumps(shown, indent=2))
        return 0
    out.mkdir(parents=True, exist_ok=True)
    with ThreadPoolExecutor(len(models)) as pool:
        jobs = list(pool.map(lambda m: generate(m, bodies[m], out), models))
    names = {m.replace('/', '__'): m for m in specs}  # earlier runs into the same folder join the comparison
    done = [(names.get(c.stem, c.stem), c) for c in sorted(out.glob('*.mp4')) if c.name != 'compare.mp4']
    manifest = {'prompt': prompt, 'firstFrame': str(frame), 'duration': args.duration, 'resolution': args.resolution,
                'jobs': [{k: v for k, v in j.items() if k not in ('unsigned_urls',)} for j in jobs]}
    if done:
        manifest['compare'] = str(compare(done, out / 'compare.mp4'))
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2))
    for j in jobs:
        print(f"{j['model']}: {j.get('path') or j.get('error')}")
    print(out)
    return 0 if done else 1


if __name__ == '__main__':
    sys.exit(main())
