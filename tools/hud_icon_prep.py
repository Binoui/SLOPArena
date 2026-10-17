#!/usr/bin/env python3
"""Prepare a generated HUD move icon for Unity (stdlib + ImageMagick `magick`).

Generators drift: soft shading, stray colours, uneven margins. This makes every
icon mechanically consistent instead of relying on the prompt:

1. binarise alpha so edges are crisp before downscaling;
2. snap every opaque pixel to the palette: ink, paper and the fighter accent;
3. with `--solid`, fill paper carving marks with ink: normal tabs show icons at
   30 px, where white marks inside ink shapes blur to grey;
4. trim, then pad to a square so the subject's longer side fills `--fill`;
5. downscale to `--size` (Unity imports AbilityCards at 256 px max);
6. embed the exact generation prompt as the `impeccable:prompt` text chunk,
   matching the existing AbilityCards convention.

    python3 tools/hud_icon_prep.py SRC.png OUT.png --accent '#39A9FF' \
        [--solid] [--manifest manifest.json] [--fill 0.88] [--size 256]
"""
from __future__ import annotations

import argparse
import json
import shutil
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

INK = "#1B1A18"
PAPER = "#F0EEE8"


def run(*args: str) -> str:
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout.strip()


def embed_prompt(png: Path, prompt: str) -> None:
    """Insert a tEXt chunk right after IHDR (PNG keys and text are Latin-1)."""
    data = png.read_bytes()
    ihdr_end = 8 + 8 + struct.unpack(">I", data[8:12])[0] + 4
    body = b"impeccable:prompt\0" + prompt.encode("latin-1", "replace")
    chunk = struct.pack(">I", len(body)) + b"tEXt" + body
    chunk += struct.pack(">I", zlib.crc32(b"tEXt" + body) & 0xFFFFFFFF)
    png.write_bytes(data[:ihdr_end] + chunk + data[ihdr_end:])


def prompt_from_manifest(manifest: Path, src: Path) -> str | None:
    for entry in json.loads(manifest.read_text(encoding="utf-8")):
        if Path(entry.get("file", "")).name == src.name:
            return entry.get("prompt")
    return None


def prepare(src: Path, out: Path, accent: str, fill: float, size: int, prompt: str | None,
            solid: bool = False) -> None:
    if shutil.which("magick") is None:
        raise SystemExit("ImageMagick 7 (`magick`) is required.")
    with tempfile.TemporaryDirectory() as tmp:
        palette = Path(tmp, "palette.png")
        run("magick", "-size", "1x1", f"xc:{INK}", f"xc:{PAPER}", f"xc:{accent}", "+append", str(palette))
        snapped = Path(tmp, "snapped.png")
        fill_marks = ["-fill", INK, "-opaque", PAPER] if solid else []
        run("magick", str(src),
            "(", "+clone", "-alpha", "extract", "-threshold", "50%", ")",
            "(", "-clone", "0", "-alpha", "off", "+dither", "-remap", str(palette), *fill_marks, ")",
            "-delete", "0", "+swap", "-compose", "CopyOpacity", "-composite",
            "-trim", "+repage", str(snapped))
        if solid:
            # Generators sometimes leave carving marks transparent instead of paper:
            # seal hairline gaps, keep only what is reachable from outside as
            # transparent, and paint every enclosed hole ink.
            mask = Path(tmp, "mask.png")
            run("magick", str(snapped), "-alpha", "extract", "-threshold", "50%",
                "-morphology", "Close", "Disk:6",
                "-bordercolor", "black", "-border", "1",
                "-fill", "gray50", "-fuzz", "0", "-floodfill", "+0+0", "black",
                "-shave", "1x1", "-fill", "white", "-opaque", "black",
                "-fill", "black", "-opaque", "gray50", str(mask))
            run("magick", str(snapped), "-background", INK, "-alpha", "remove", "-alpha", "off",
                str(mask), "-alpha", "off", "-compose", "CopyOpacity", "-composite", str(snapped))
        w, h = (int(v) for v in run("magick", "identify", "-format", "%w %h", str(snapped)).split())
        side = round(max(w, h) / fill)
        out.parent.mkdir(parents=True, exist_ok=True)
        run("magick", str(snapped), "-background", "none", "-gravity", "center",
            "-extent", f"{side}x{side}", "-filter", "Lanczos", "-resize", f"{size}x{size}",
            "-strip", f"PNG32:{out}")
    if prompt:
        embed_prompt(out, prompt)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("src", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--accent", required=True, help="fighter accent colour, e.g. '#39A9FF'")
    parser.add_argument("--solid", action="store_true", help="fill paper carving marks with ink (normal tabs)")
    parser.add_argument("--manifest", type=Path, help="generation manifest holding the exact prompt")
    parser.add_argument("--fill", type=float, default=0.88, help="share of the canvas the longer side fills")
    parser.add_argument("--size", type=int, default=256)
    args = parser.parse_args()
    prompt = prompt_from_manifest(args.manifest, args.src) if args.manifest else None
    prepare(args.src, args.out, args.accent, args.fill, args.size, prompt, args.solid)


if __name__ == "__main__":
    main()
