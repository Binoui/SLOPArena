# Nyx's approved animation and paper-effect integration

The package owns its approved clips, source meshes, palette, shader and generated prefabs.
The 2026-10-29 local evidence and the exact gateway batches are in
`tmp/nyx-model/integration/`.

Run `python3 tools/nyx/build_paper_assets.py` to generate the 28 source OBJ meshes and
point-sampled eight-color palette. Silhouettes are closed beveled solids. Slashes, arcs,
rings, rays and the lash are flat comic strokes (`Mesh.stroke`): the bands run across the
width, so each has ink edges, a dark band, the accent and a pale core line, and crescent
widths give pointed tips. Flashes are uneven starbursts (`burst`), floor marks lie flat with
upright torn teeth, Grasp's tendrils are swept 16-facet bands, and bird wings have a
dihedral fold. The generator rejects open surfaces or any axis below 2 cm;
`NyxPaperMeshes.json` records the generated bounds in metres. This does not operate
Unity or edit the rig.

`prepare_approved_integration.py` promotes the owner's 24 accepted experiment clips
into package-owned files with distinct GUIDs. It requires the retained local GenRA
status and timing handoff. It does not bind a candidate or modify the rig.
`wire_character.py` assigns the approved IDs and presentation cues. It asserts that
the non-presentation gameplay document is unchanged before writing.

Use the canonical gateway with your own lease for these native commands:

- `sloparena.animation.nyx.integrate` remaps Bat Scatter and both swaps to existing
  gameplay clocks, assigns one pose track per approved clip and retires unused bindings.
- `sloparena.presentation.nyx.build` uses `PresentationBuilderKit` to assemble separate
  prefabs. It preserves the three phone/pole bindings. Layer ticks come from the cues in
  `character.json` (each `emitPresentation` tick and lifetime, Grasp's warning and catch,
  Bat Scatter's thrust and Ring Light Rise's startup), so rebuild after a retime. It
  force-reimports the meshes it saved, so an Editor rebuild never draws stale GPU buffers.
- `sloparena.presentation.nyx.verify-timing` fails when a paper layer falls outside its cue's
  lifetime in `character.json`. Nothing runs it automatically: run it after `retime.py --write`
  and a rebuild. Vertex
  colours use packed `Color32` because particle mesh streams misread Float32 white.
  The package shader writes depth, and contact materials keep depth testing, so the parts of
  a burst inside a fighter stay hidden. The crow pop's burst is pivoted toward the camera
  instead, so the perched crow and the victim's head cannot hide it.
- `sloparena.character.inspect --target nyx`, `sloparena.character.cook --target nyx`
  and `sloparena.character.verify --target nyx` validate the saved integration.

`encode_integration.py` converts native Training JPG sequences into H.264/yuv420p.
It converts JPEG full-range input to limited-range output explicitly and checks
codec, pixel format and frame count with ffprobe. Existing outputs are preserved.
`make_integration_sheets.py` makes one sheet per form from the same gameplay pixels,
with actor insets for readability. Neither script creates or edits effect art.

Gameplay and hitboxes remain in Shared. The Stomp-Kick contact mismatch and the
original animation brief versus the current gameplay clocks are recorded in Nyx's page.

`python3 tools/nyx/retime.py` previews the timing slowdown. Add `--write` to apply
the factor table to both authored grids and their aliases. Keep `retime-state.json`
with the script. It journals the original timing leaves before writing, rejects
conflicting timing edits, and makes reruns safe after interruption. Other gameplay
fields are preserved. The local GenRA timing handoff is refreshed when present.

Stretch the two run clips with `tools/genra/retime_anim.py <clip> --ticks <ticks>
--write`, using 24 ticks for `nyx-real-run.anim` and 26 for `nyx-fake-run.anim`.
It preserves poses and loop settings, rescales tangent slopes, and skips clips
already at the target duration. It supports uncompressed text AnimationClips.
Cook and verify Nyx through the gateway after these content changes.

Depth review evidence and exact bounded gateway batches: `tmp/nyx-model/vfx-depth/REPORT.md`.
Review all 27 effect prefabs at their full silhouette, not only the normal gameplay
shot: CameraMount allows wrapping yaw and pitch 0–45 degrees (Steady settles at
35). Include the side views at 0 degrees and a high supplemental view.
