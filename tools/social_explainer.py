#!/usr/bin/env python3
"""Compose a 9:16 explainer or teaser from factory clips, images and text plates with HyperFrames.

An explainer is a factory idea (tools/factory.py) with `"format": "explainer"` and beats:

    {"type": "shot", "clip": "20a-beeline-ground", "from": 0.4, "to": 3.2, "text": "LOCKS ON\\nCHASES IN 3D"}
    {"type": "image", "src": "tmp/content/21-bashie-teaser/concept-1.png", "seconds": 2.0, "text": "NEW FIGHTER?"}
    {"type": "card", "seconds": 1.6, "text": "FAIR OR\\nBROKEN?"}

    {"type": "compare", "seconds": 3, "top": "<old still>", "topText": "OLD", "bottom": "<new still>", "bottomText": "NEW"}

`clip` names a rendered `shot` idea (its chosen or latest take, with its game sound); a card holds what the beat before
it showed (a shot's last frame, an image or a pair). Put the first beat's text at `at` 0: frame 0 is the thumbnail and the hook. Optional per beat: `at` (plate
delay in s), `pos` (`top`, `middle` or `bottom` of the band), `size` (font px), `focus` (0-1, which
height of a 1080x1920 clip the 1350 px gameplay band shows), `fit` (`contain` shows a whole image
on paper instead of filling the band). The look is the trailer's: paper ground,
torn gameplay band, yellow Archivo Black plates that slam in. Text stays inside the platform-safe
band (y 168-1330, left of x 930). Music is left for CapCut.

    python3 tools/social_explainer.py <explainer>              # build, check, render, upload a take
    python3 tools/social_explainer.py <explainer> --snapshot   # build + PNG frames, no render
"""
from __future__ import annotations

import argparse
import html
import json
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import factory  # noqa: E402
from social_clip import ROOT, normalize, rel  # noqa: E402

HF = "hyperframes@0.8.210"
FONT = ROOT / "client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf"
CLICK = ROOT / "client/Unity/Assets/Resources/Audio/SFX/UI/Click/UI_Button_Click_1.wav"
TONE = ROOT / "tools/social_template/halftone-corner.svg"
BAND_TOP, BAND_H = 96, 1350
TORN = ("0% 0.81%, 5.45% 1.15%, 15.03% 0.71%, 20.65% 0.49%, 25.55% 0.55%, 34.1% 0.49%, 41.88% 0.36%, 45.44% 0.38%, "
        "54.41% 0.62%, 59.89% 0.59%, 67.22% 0.66%, 72.17% 0.68%, 79.22% 0.39%, 87.96% 0.9%, 93.99% 1.08%, 100% 0.65%, "
        "100% 99.37%, 93.64% 99.34%, 85.99% 99.7%, 80.14% 99.23%, 73.95% 99.68%, 65.85% 99.7%, 59.74% 99.57%, "
        "55.2% 99.54%, 48.4% 99.57%, 39.26% 99.06%, 33.88% 99.12%, 26.77% 99.26%, 18.5% 99.43%, 11.85% 99.09%, "
        "5.93% 99.7%, 0% 99.63%")
PLATE = ("0% 1.9%, 10.36% 2.85%, 19.79% 2.02%, 30.52% 1.37%, 40.07% 2.26%, 51.76% 1.19%, 58.82% 1.18%, 71.86% 2.39%, "
         "77.25% 2.96%, 92.79% 2.31%, 100% 2.23%, 98.69% 7.09%, 97.94% 17.36%, 98.62% 28.45%, 98.94% 39.78%, "
         "98.12% 52.05%, 97.96% 60.84%, 98% 70.97%, 98.09% 78.67%, 97% 92.97%, 97.32% 100%, 88.75% 98.37%, "
         "81.62% 98.42%, 72.58% 97.47%, 60.6% 97.31%, 50.68% 97.08%, 37.92% 99%, 31.74% 97.18%, 20.18% 97.04%, "
         "10.62% 98.85%, 0% 97.74%, 2.56% 91.38%, 1.17% 81%, 2.93% 68.45%, 1.24% 61.52%, 1.2% 52.64%, 2.59% 41.93%, "
         "2.12% 30.32%, 1.38% 18.61%, 1.26% 9.14%")
PLATE_Y = {"top": 200, "middle": 560, "bottom": 960}


def duration(path: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                         capture_output=True, text=True, check=True).stdout
    return float(out)


def sound(clip: Path) -> str:
    """Live captures carry no audio track; HyperFrames rejects a video that claims one."""
    streams = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a", "-show_entries", "stream=index",
                              "-of", "csv=p=0", str(clip)], capture_output=True, text=True, check=True).stdout
    return 'data-has-audio="true"' if streams.strip() else "muted"


def shot_clip(idea: str) -> Path:
    """A shot rendered on this desk is still in tmp/content; otherwise download its take from the factory."""
    local = ROOT / f"tmp/content/{idea}/{idea}-vertical.mp4"
    return local if local.is_file() else factory.fetch(idea, local)


def font_size(lines: list[str], size: int | None) -> int:
    # Archivo Black caps run about 0.8 em wide; the widest line must fit 850 px of the 890 px left of the rail.
    return size or max(64, min(150, int(850 / (0.8 * max(len(line) for line in lines)))))


def build(job: dict, project: Path) -> float:
    if project.exists():
        shutil.rmtree(project)
    (project / "assets/media").mkdir(parents=True)
    (project / "assets/fonts").mkdir()
    shutil.copy(FONT, project / "assets/fonts/ArchivoBlack-Regular.ttf")
    shutil.copy(TONE, project / "assets/halftone-corner.svg")
    shutil.copy(CLICK, project / "assets/media/click.wav")
    (project / "hyperframes.json").write_text(json.dumps({"paths": {"assets": "assets"}}, indent=1))
    (project / "package.json").write_text(json.dumps({"name": job["id"], "private": True, "type": "module"}, indent=1))

    media, plates, sounds, script = [], [], [], []

    def plate(pid: str, text: str, at: float, end: float, y: int, size: int | None, rest: int) -> None:
        lines = text.split("\n")
        plates.append(f'<div id="{pid}" class="clip plate-host" style="top:{y}px" data-start="{at:.4f}" '
                      f'data-duration="{end - at:.4f}" data-track-index="2">'
                      f'<div id="{pid}-pop" class="pop"><div class="plate-shadow"></div>'
                      f'<div class="plate" style="font-size:{font_size(lines, size)}px">'
                      f'<div class="plate-tone"></div>{"<br>".join(html.escape(line) for line in lines)}</div></div></div>')
        script.append(f'tl.set("#{pid}-pop", {{scale: 1.16, rotation: {rest - 2}}}, {at:.4f});')
        script.append(f'tl.to("#{pid}-pop", {{scale: 1, rotation: {rest}, duration: 0.1667, ease: "power3.out"}}, {at:.4f});')
        sounds.append(f'<audio id="click-{pid}" src="assets/media/click.wav" data-start="{at:.4f}" data-duration="0.3" '
                      f'data-volume="0.6" data-track-index="3"></audio>')

    t, held = 0.0, None
    for i, beat in enumerate(job["beats"]):
        kind = beat["type"]
        if kind == "shot":
            clip = shot_clip(beat["clip"])
            start, end = beat.get("from", 0.0), beat.get("to", duration(clip))
            length = end - start
            name = f"b{i}-{clip.name}"
            shutil.copy(clip, project / "assets/media" / name)
            top = round(BAND_H / 2 - 1920 * beat.get("focus", 0.5))
            media.append(f'<video id="m{i}" class="clip shot" src="assets/media/{name}" style="top:{top}px" '
                         f'data-start="{t:.4f}" data-duration="{length:.4f}" data-media-start="{start:.4f}" '
                         f'{sound(clip)} data-track-index="1" playsinline></video>')
            last = f"b{i}-last.png"
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{end - 3 / 60:.4f}", "-i", str(clip), "-frames:v", "1",
                            str(project / "assets/media" / last)], check=True)
            held = lambda mid, start, span, last=last, top=top: (
                f'<img id="{mid}" class="clip shot" src="assets/media/{last}" style="top:{top}px" alt="" '
                f'data-start="{start:.4f}" data-duration="{span:.4f}" data-track-index="1">')
        elif kind == "image":
            length = beat["seconds"]
            src = ROOT / beat["src"]
            name = f"b{i}-{src.name}"
            shutil.copy(src, project / "assets/media" / name)
            fit = beat.get("fit", "cover")
            held = lambda mid, start, span, name=name, fit=fit: (f'<div id="{mid}" class="clip still" data-start="{start:.4f}" '
                                                        f'data-duration="{span:.4f}" data-track-index="1">'
                                                        f'<img id="{mid}-img" src="assets/media/{name}" alt="" style="object-fit:{fit}"></div>')
            media.append(held(f"m{i}", t, length))
            script.append(f'tl.fromTo("#m{i}-img", {{scale: 1}}, {{scale: 1.06, duration: {length:.4f}, ease: "none"}}, {t:.4f});')
        elif kind == "compare":
            # Two landscape stills stacked in the band (old above, new below), each with a small label.
            length = beat["seconds"]
            images = []
            for half, (src, label) in enumerate([(beat["top"], beat.get("topText")), (beat["bottom"], beat.get("bottomText"))]):
                name = f"b{i}-{half}-{Path(src).name}"
                shutil.copy(ROOT / src, project / "assets/media" / name)
                images.append(f'<img id="m{i}-{half}" src="assets/media/{name}" alt="" style="top:{40 + half * 662}px">')
                if label:
                    at = t + beat.get("labelAt", 0.0) + half * beat.get("labelGap", 0.6)
                    plate(f"l{i}-{half}", label, at, t + length, BAND_TOP + 60 + half * 662, beat.get("labelSize", 72), -2 if half else 2)
            held = lambda mid, start, span, images=images, own=f"m{i}": (
                f'<div id="{mid}" class="clip pair" data-start="{start:.4f}" data-duration="{span:.4f}" data-track-index="1">'
                + "".join(img.replace(f'id="{own}-', f'id="{mid}-') for img in images) + "</div>")
            media.append(held(f"m{i}", t, length))
        else:
            length = beat["seconds"]
            if held:
                # A card holds what the beat before it showed instead of an empty band.
                media.append(held(f"m{i}", t, length))
            held = None
        if beat.get("text"):
            y = PLATE_Y[beat.get("pos", "middle" if kind == "card" else "top")]
            plate(f"p{i}", beat["text"], t + beat.get("at", 0.0), t + length, y, beat.get("size"), -3 if i % 2 == 0 else 2)
        t += length

    page = f"""<!doctype html>
<html lang="en" data-resolution="portrait">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=1080, height=1920" />
    <script src="https://cdn.jsdelivr.net/npm/gsap@3.14.2/dist/gsap.min.js"></script>
    <style>
      @font-face {{ font-family: "Archivo Black"; src: url("assets/fonts/ArchivoBlack-Regular.ttf") format("truetype"); font-display: block; }}
      * {{ margin: 0; padding: 0; box-sizing: border-box; }}
      html, body {{ width: 1080px; height: 1920px; overflow: hidden; background: #f0eee8; }}
      #root {{ position: relative; width: 100%; height: 100%; overflow: hidden; background: #f0eee8; }}
      .tone {{ position: absolute; background: url("assets/halftone-corner.svg") no-repeat right bottom; background-size: 100% 100%; }}
      #tone-br {{ right: 0; bottom: 0; width: 840px; height: 520px; }}
      #tone-tl {{ left: 0; top: 0; width: 420px; height: 260px; transform: scale(-1, -1); }}
      #band-shadow {{ position: absolute; left: 12px; top: {BAND_TOP + 12}px; width: 1080px; height: {BAND_H}px; background: #161513; clip-path: polygon({TORN}); }}
      #band {{ position: absolute; left: 0; top: {BAND_TOP}px; width: 1080px; height: {BAND_H}px; overflow: hidden; clip-path: polygon({TORN}); background: #161513; }}
      .shot {{ position: absolute; left: 0; width: 1080px; height: 1920px; }}
      .still {{ position: absolute; inset: 0; overflow: hidden; background: #f0eee8; }}
      .still img {{ width: 100%; height: 100%; object-fit: cover; }}
      .pair {{ position: absolute; inset: 0; background: #f0eee8; }}
      .pair img {{ position: absolute; left: 0; width: 1080px; height: 608px; object-fit: cover; }}
      .plate-host {{ position: absolute; left: 40px; z-index: 5; }}
      .pop {{ position: relative; }}
      .plate-shadow {{ position: absolute; left: 12px; top: 12px; width: 100%; height: 100%; background: #161513; clip-path: polygon({PLATE}); }}
      .plate {{ position: relative; background: #ffcc22; color: #1b1a18; font-family: "Archivo Black"; line-height: 0.92;
                letter-spacing: -0.01em; text-transform: uppercase; white-space: nowrap; padding: 0.2em 0.3em 0.13em; clip-path: polygon({PLATE}); }}
      .plate-tone {{ position: absolute; right: 0; bottom: 0; width: 420px; height: 260px; background: url("assets/halftone-corner.svg") no-repeat right bottom; }}
    </style>
  </head>
  <body>
    <div id="root" data-composition-id="main" data-start="0" data-duration="{t:.4f}" data-width="1080" data-height="1920" data-fps="60">
      <div id="tone-tl" class="tone"></div>
      <div id="tone-br" class="tone"></div>
      <div id="band-shadow"></div>
      <div id="band">
        {chr(10).join("        " + m for m in media).strip()}
      </div>
      {chr(10).join("      " + p for p in plates).strip()}
      {chr(10).join("      " + s for s in sounds).strip()}
    </div>
    <script>
      window.__timelines = window.__timelines || {{}};
      const tl = gsap.timeline({{ paused: true }});
      {chr(10).join("      " + s for s in script).strip()}
      window.__timelines["main"] = tl;
    </script>
  </body>
</html>
"""
    (project / "index.html").write_text(page)
    return t


def hf(project: Path, *args: str, capture: bool = False) -> subprocess.CompletedProcess:
    return subprocess.run(["npx", "--yes", HF, *args], cwd=project, capture_output=capture, text=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("job")
    parser.add_argument("--snapshot", action="store_true", help="build and save PNG frames instead of rendering")
    args = parser.parse_args()
    job = factory.get(Path(args.job).stem)
    out = ROOT / "tmp/content" / job["id"]
    project = out / "hf"
    seconds = build(job, project)
    check = hf(project, "check", "--json", capture=True)
    (out / "check.json").write_text(check.stdout)
    if args.snapshot:
        times = ",".join(f"{x:.2f}" for x in [0.05, *[seconds * k / 6 for k in range(1, 6)], seconds - 0.05])
        hf(project, "snapshot", "--at", times)
        print(f"{rel(project)} {seconds:.2f} s; check: {rel(out / 'check.json')}")
        return
    raw = out / "render.mp4"
    if hf(project, "render", "--fps", "60", "--quality", "delivery", "--output", str(raw), "--quiet").returncode != 0:
        raise SystemExit("render failed")
    final = out / f"{job['id']}-vertical.mp4"
    normalize(raw, final)
    factory.take(job["id"], final)


if __name__ == "__main__":
    main()
