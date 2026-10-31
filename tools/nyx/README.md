# Nyx animation and roster VFX integration

Nyx's package owns the approved clips, generated particle prefabs, palette,
and retained Hex Crow model. The effects use the shared roster toolkit.

Use the canonical Unity gateway and your own bounded lease for these commands:

- `sloparena.presentation.nyx.build` rebuilds all effects with
  `PresentationBuilderKit`, `PresentationFx`, and `ToonShapes.Bands`. It preserves
  the three phone and pole bindings, `NyxRig.prefab`, and the crow model meshes.
- The builder compiles gameplay source without requiring generated VFX bindings.
  It reads active hitbox geometry and cue windows, samples Shared geometry and
  bone sweeps against the verified pose package, and adds a small radius margin.
  Static volumes remain at their spawn position. Bone volumes follow the owner.
- Rebuild after a hitbox or timing retune. The builder synchronizes cue starts
  and minimum lifetimes to attack phases, including Selfie Grab's capture cue.
  Cook animation changes before building bone-dependent effects.
- `sloparena.presentation.nyx.verify-timing` compares saved volume samples,
  active windows, and motif lifetimes with current source. Rebuild on failure.
- `sloparena.character.cook --target nyx` and
  `sloparena.character.verify --target nyx` validate the saved presentation.

The paper VFX generator and shader are retired. Only the crow's three model
OBJs and generated model meshes remain from that asset family. Its palette is
`Presentation/Source/NyxCrowPalette.png`. Character props remain models.

`prepare_approved_integration.py` promotes the owner's 24 accepted experiment clips
into package-owned files with distinct GUIDs. It requires the retained local GenRA
status and timing handoff. It does not bind a candidate or modify the rig.
`wire_character.py` assigns the approved IDs and presentation cues. It asserts that
the non-presentation gameplay document is unchanged before writing.

`sloparena.animation.nyx.integrate` remaps Bat Scatter and both swaps to their
existing gameplay clocks and assigns one pose track per approved clip.

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

Capture every recipe move with the resolver overlay:

```bash
python3 tools/move_review.py run nyx --hitboxes
```

Review all camera-ring rows, gameplay pitch 35, and the low and high views.
`media/` remains clean. `media-hitboxes/` overlays the exact checked hitboxes
from the same take. Rebuild overlay media without Unity with
`python3 tools/move_review.py media <run-directory> --hitboxes`.
See [VFX review overlays](../../docs/systems/vfx-particles.md#move-review-hitbox-overlays).

The current comparison board and verification receipts live locally in
`tmp/nyx-coverage/`. Earlier integration and depth evidence remains under
`tmp/nyx-model/integration/` and `tmp/nyx-model/vfx-depth/`.
