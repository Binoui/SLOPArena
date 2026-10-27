# Manki Mayhem — Pirate Broadcast

Procedural presentation of the locked [Manki Mayhem design](../../../docs/design/stages/manki_mayhem.design.md):
a bare concrete fight deck, a scaffold-and-crate host booth, scavenged CRT walls,
and three fenced audience galleries inside a dark warehouse. No water, vendor
assets, live host, rounds or gameplay hazards. The stage remains unaccepted and
is intentionally absent from `Resources/Stages/roster.json`.

The generator imports `stagekit`; `slop_pit.parts` supplies its existing Side frame,
fans, chain-link and beam primitives. Roster art comes from
`client/Unity/Assets/Resources/UI/Portraits`; Archivo Black and Space Mono are the
bundled OFL fonts. Everything else is original procedural geometry and paint.

- `layout.py`: shell SPEC, outward-wound collision and strict triangle/spawn/blast check.
- `deck.py`: flat concrete top, flush paint, hoarding, inset framing and scaffold tower.
- `booth.py`: host perch, mic and decorative bomb pile, speaker stacks and CRT walls.
- `venue.py`: galleries, crowd, fences, side identities, truss/string/par rig and warehouse.
- `parts.py`, `palette.py`, `textures.py`: motifs, matte palette, paint/portraits/CRT atlas.
- `build.py`: clearance, mesh health, AO, deterministic FBX and source/arena stamp.
- `presentation.py`: Unity materials, shadow flags, complete Stage Lights, atmosphere,
  four spawn cameras, eight recovery cameras, hero/review poses and the SSE thumbnail.
- `preview.py`: optional Eevee aid; real gameplay captures decide exposure and framing.

The planned `TRIANGLE_BUDGET` is 240,000. The second art pass builds 185,508
triangles (Unity imports 174,420) and 804 fans. Each actual background polygon is
clipped against the blast corridor and a conservative camera box enlarged 15 m
horizontally, from two metres below KO to 16 m above the top blast plane. No
background enters either. Deck top vertices must be exactly y=0 and within the
24 × 20 footprint; wall dressing has Slop Pit's 2.5 cm maximum proud tolerance.

All crowd hops/fist pumps, phones/signs, bulb chase, APPLAUSE blink and CRT flicker/
raster strips run through StageNeon channels. The screen images are static show
logo, Manki portrait and signal noise. The named `MankiHostAnchor` is an empty
Transform at Unity `(0, 6.5, 41.5)`; it has no children or components beyond
Transform. The platform is 20 × 10 m at y=6.5 (x −10..10, z 37.5..47.5), with a
clear 9 × 6.5 m host bay (x −4.5..4.5, z 38.5..45) and 8 m of unobstructed height.
A future mode can attach a 3–4× fighter-height host there. The north strings split
around the bay; the mic and bombs stand beside it. The temporary fuse/bomb shapes
are presentation, with no damage/timing/physics authority.

Shell first (new source scene requires `--create`), using the gateway and your own
identity/lease as in [Editor coordination](../../../docs/contributing/unity-editor-coordination.md):

```bash
python3 tools/blender/manki_mayhem/layout.py --unity-shell > /tmp/manki-mayhem-shell.json
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.shell-apply --stage manki_mayhem --from /tmp/manki-mayhem-shell.json --create
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.bake --stage manki_mayhem
python3 tools/blender/manki_mayhem/layout.py
```

Run long jobs in a user scope. First use `--massing`, apply and inspect gameplay
views; remove that flag for the detail pass. Neither build opens an interactive
Blender session or saves a blend file.

```bash
systemd-run --user --scope blender -b --factory-startup --python-exit-code 1 \
  --python tools/blender/manki_mayhem/build.py -- \
  --out /tmp/manki-mayhem-generated --report tmp/manki-mayhem-stage/detail --samples 32
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage manki_mayhem --from /tmp/manki-mayhem-generated
python3 tools/blender/manki_mayhem/build.py --dry
```

Add `--preview --views show,thumbnail` for Eevee. `--spec-only --no-export` previews
an unbaked layout and cannot export runtime art. Material/light changes can rewrite
only the presentation JSON with `python3 tools/blender/stagekit/presentation.py
manki_mayhem --out /tmp/manki-mayhem-generated`, followed by art-apply.

Use the shared `sloparena.stage.views.*` batch from the
[stage workflow](../../../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review),
including a yaw-0 show view, intermediate yaws and named review cameras. Run
`tools/capture_exposure.py` on each capture directory. Final evidence is local:
`tmp/manki-mayhem-stage/pass2/gameplay/01-show.png` first (same yaw/pitch as pass 1),
then the other gameplay views, `pass2/review/` and `pass2/exposure.json`. All gameplay
and recovery captures pass exposure; only `warehouse-below` remains dark, below KO.
The original 15° show framing crops the top of a giant host; the additional yaw-0,
8° shot at `pass2/host-framing/01-show-host.png` and the raised `show-wide` review
camera frame the tall host bay. Future mode camera framing must accommodate him.
The deck key, fill and material remain unchanged. Warm gallery walls, analytic CRT
spill and additive URP light fins carry venue depth; a low material fill on the
separate deck-side material preserves recovery readability at limited light counts.
The deck has only its faint crown/title and small edge ticks, with no large stencils,
arrows, top hazard band or taped cable graphics. Each big screen has 26 small sets.

The human play route needs no roster entry: in stopped Unity open
`Assets/Scenes/Arena_Offline.unity`, select `TrainingMatch`, set **Arena Name
Override** to `manki_mayhem` without saving the scene, then press Play. Stop and
reopen the scene, discarding the override afterwards. For a fully restored typed
dev session use `sloparena.photo.prepare --stage manki_mayhem --fighter manki
--training true`, press Play, then Stop and `sloparena.photo.restore` through the
gateway. This prepares Training/input/camera in memory and bypasses the picker.

`MankiMayhemArenaTests` covers the shell/blast/spawns, flat deck, recovery against
all four sides and corners at two depths for all four roster fighters, and grounding
at every spawn. Run with `-p:SkipUnityPluginCopy=true`. Structural and capture proof
does not claim human acceptance, multiplayer balance or packaged-player performance.
