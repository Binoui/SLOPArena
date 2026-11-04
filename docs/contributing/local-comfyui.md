# Local references with ComfyUI

Generated references (motion clips, concept stills) can run on the maintainer's own GPU
through [ComfyUI](https://github.com/comfyanonymous/ComfyUI), for free. Try the local route
first; a paid model (Seedance through `video_ref.py`) needs the owner's OK. ComfyUI is
optional: nothing in a build, cook or test needs it.

Measured on 2026-11-04 on an RTX 4070 (12 GB) with 31 GB RAM.

## Setup

ComfyUI lives in `~/ComfyUI` with its own `venv`. Start it with `~/ComfyUI/start.sh`
(log in `/tmp/comfyui.log`, API on `http://127.0.0.1:8188`), or as a user service that
survives agent shells:

```bash
systemd-run --user -q --unit=comfyui-vfx --working-directory=$HOME/ComfyUI $HOME/ComfyUI/start.sh
systemctl --user stop comfyui-vfx      # frees the GPU
```

Video uses Wan 2.2 5B (image to video, ComfyUI's bundled "Wan 2.2 5B ti2v" template),
about 17 GB in `~/ComfyUI/models`:

| Folder | File | Size |
| --- | --- | --- |
| `diffusion_models` | `wan2.2_ti2v_5B_fp16.safetensors` | 10 GB |
| `text_encoders` | `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | 6.7 GB |
| `vae` | `wan2.2_vae.safetensors` | 1.4 GB |

Controlled video adds `diffusion_models/wan2.2_fun_control_5B_bf16.safetensors` (9.3 GB,
same repository), ComfyUI's bundled "Wan 2.2 5B Fun Control" template.

They come from `huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged` (model and VAE) and
`Comfy-Org/Wan_2.1_ComfyUI_repackaged` (text encoder), under `split_files/<folder>/`.
The larger first-and-last-frame models (Wan 2.2 14B, LTX-2.3) need 38 GB or more and do not
fit a 12 GB card comfortably. Ask before downloading models.

## Tools

| Tool | What it does |
| --- | --- |
| [`tools/vfx/comfy_video.py`](../../tools/vfx/comfy_video.py) | One first frame and a prompt → an mp4 plus a `.json` record (prompt, negative, seed, settings), so a clip can be rebuilt or varied. Its default negative suits effects on black. |
| `comfy_video.py --control BLOCKOUT.mp4` | Controlled video: the motion comes from a blockout we draw or render, so timing lands on ticks and reach matches the hitbox; the model paints the look. |
| [`tools/vfx/blockout.py`](../../tools/vfx/blockout.py) | Generates that blockout from a move's real hitboxes: per-tick capsules from `scripts/move-data.sh <pkg> --reach --json` for normals and aerials (bone hitboxes included), or fixed-offset hitboxes from `character.json`. Motifs `sweep`, `geyser`, `blast`; a three-quarter camera by default (`--view side/front/behind`, `--yaw`, `--pitch`); it frames what it actually drew, so nothing is cut; writes soft masses (the control), edges and a check video with the hitboxes in red and floor rings every metre. |
| [`tools/genra/video_ref.py`](../../tools/genra/video_ref.py) `--models local/wan2.2-5b` | Animation references from a character's render, with a negative suited to characters; mixes with paid models in one comparison. |
| [`tools/vfx/flipbook.py`](../../tools/vfx/flipbook.py) | An effect clip on black → a sprite sheet: `timeline` to pick frames, `sheet` to remove the black and pack cells, `grade` to recolour into the roster's toon bands (dark rim, body, light, core) with `--rim`, `preview` to play it. |

## What to expect

- A 5 s clip at 1280×704 takes about 7.5 minutes; 3 s about 5 minutes.
- The 5B model follows the prompt's **first beat** and drifts after it: Bashie's splash rose
  well, then held like a fountain instead of collapsing. Ask for one beat per take and chain:
  extract a frame (`video_ref.py frames`, or ffmpeg) and start the next take from it.
  Joining clips is an ffmpeg `concat`.
- Write the style into the prompt and keep the template's "style, artwork, painting" words
  out of the negative; both scripts already drop them.
- For effects: draw the first frame in the character's palette on pure black, keep the
  effect inside the frame, and name what it is not ("not white foam, not mist").
- `flipbook.py sheet`'s automatic crop takes every lit pixel in every frame; stray splashes
  at the frame edge make it huge. Pass `--box cx,cy,half-width` around the effect.
- Characters: Billy the Gill's Sombrero Copter (2026-11-04, first frame rendered from
  `client/Web/assets/fighters/salamander.glb` in headless Blender, so no Editor lease) kept his
  design in calm frames and gave usable poses (arms up on the brim, dangling legs). It skipped
  the brim grab with a jump cut, ballooned the hat instead of spinning it, and smeared his face
  in fast motion: good for poses, weak on prop mechanics.
- Controlled video (Bashie's splash, 2026-11-04): a blockout drawn by script from her splash hitbox
  (sphere 1.7 m, 3 active ticks), at 4× slow motion so a 4-tick rise spans several frames.
  Crisp outlines are followed literally: the model only colours the shapes in. Blurred masses
  (a 14 px blur, like a depth map) gave painted toon water on the hitbox's reach and ticks, but
  the blur's edge came back as a dark halo; try a lighter blur. Keep the blockout inside the
  frame, and turn it into edges or masses on the CPU: the template's GPU Canny node runs out of
  memory at 1280×704 on 12 GB.
- Generated blockouts (2026-11-04): Sir Bonk's g1 as a `sweep` from the reach report came back as
  his bucket-red smear on the blade's path and reach, from a black first frame; Bashie's splash as a
  `geyser` from `character.json` matched the hand-drawn blockout. The sweep must be a thin crescent
  on the blade's path to its outer edge: a capsule-sized smear just redraws the hitbox. It starts
  with a short lead-in so it is full size on the first active tick. The reach source covers normals
  and aerials only: projectiles, capability hitboxes (Wibou's Quick Draw and flash) and bone
  hitboxes in specials (his Rising Slash) are not generated yet.
- A clip is a reference first: it shows motion and shape changes for a concept board or an
  animation brief. Turning it into an in-game sheet is a separate, owner-approved step.

Worked example: Bashie's tucked-landing splash, 2026-11-04 (first frame drawn by script,
rise clip, a collapse clip chained from its frame 40, graded into her bands). The owner:
"this is really good reference".
