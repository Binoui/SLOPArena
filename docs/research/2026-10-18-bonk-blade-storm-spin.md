# Bonk Blade Storm (F): hip-height spin

Prepared by Claude (Opus 5.5), 18 October 2026. The owner said Bonk's Blade Storm "hits a
bit too high and misses a lot of the time" and wanted the sword lower. Of two proposals, a
full-body spin with the blade flat at hip height and a tilted twirl, the owner chose the
spin on the current clock, then approved the first candidate on the actual character ("very
good, lets go for it").

## Diagnosis

The old clip (`bonk_spell_f.FBX`, played at 1.10×) twirled the plank over his head, 2.3–3.4 m
up. The new multi-hit coverage table of the
[hit reliability report](../systems/move-data-report.md#hit-reliability) showed that its four
big hits (19.5 of the move's 22 damage) touched no bearing around a standing FightGuy or
Manki. Only the long chip hitbox landed, and only because its ends sat 0.2 and 0.5 m below
the blade. Lowering the existing curves could not fix an overhead twirl, so the move needed
a new mechanic.

## Candidate

- Brief `tools/genra/briefs/bonk-gf-blade-storm-spin.json`: 100 ticks, active 16–49, contact
  at 46, entry and exit on idle, both hands on the hilt from tick 5 to 66. Height targets keep
  the blade end and hilt within −0.08 to +0.22 m of the entry hips' height at the hit ticks.
- Run `20261018-032855-bonk-gf-blade-storm-spin`: Opus 5.5, $1.73, 13 minutes, every check
  passed. Three turns to his left in a wide, low stance with heavy steps, a faster turn into
  the finisher, a slowing stop at tick 66 and the sword back on his shoulder by tick 94.
- `unity_clip.py write` converted it within the Humanoid limits, and `render` compared it
  with the bound clip on the catalog prefab. The owner approved from that video.

## Integration

- Promoted with a fresh GUID to `Assets/CharacterPackages/bonk/Animations/bonk_gf_blade_storm.anim`
  (`329af6dd…`, 100 frames at 60 fps, all 102 curves identical to the candidate), tracked by a
  narrow `.gitignore` exception. `sloparena.character.bind` replaced only `anim.bonk.gf`,
  which `ground.F` and `air.F` share. The old FBX stays in the art tree, unbound.
- `character.json`: the chip hitbox's offsets (−0.2 and −0.5 m, in both slots) are zero, so it
  follows the visible blade. Nothing else in the move changed.
- `BonkPresentationBuilder`: the whirl orbit sits 0.2 m above the hips instead of 0.9 m. The
  rebuild changed only `BonkBladeStormWind.prefab` and `BonkAirStormWind.prefab`.
- Bonk cook, verify and roster refresh are valid with matching hashes. Only Bonk's cooked
  files and roster pin changed. The other packages' cooked diffs come from a parallel thread's
  shared tumble binding.

## Verification

- **Hit reliability** (`--slots F`): every big hit now touches 7–15 of 24 bearings at 1–1.5 m
  (0 before), the chip 24 (5–23), with no sampling loss. The table is in
  [Bonk](../characters/bonk.md#blade-storm-spin--2026-10-18).
- **Blade height** from the cooked hitbox poses: hilt and tip 1.02 m above his feet from
  tick 14 to 52.
- **Real simulation**: one press connects from all 8 directions at 1–2.5 m against all four
  fighters (before: 0–8 of 8), for 9.4–12.5 average damage (before: 0–4.4).
- **Ability Lab**: hits, blocks and whiffs for ground and air, listed in the Bonk doc. Bonk ends
  every run in Idle.
- **In game**: `tools/presentation_review.py` on Splash Deck, FightGuy 1.3 m ahead at 60%: hits
  at frames 18 (2.5%) and 55 (12%, launch). The smears whirl at waist height with the blade,
  from the side and from behind.
- **Shared**: 2976 passed, 2 existing skips.
- **Not verified**: live Training and PvP feel. Two weak points were shown at review and
  accepted: quick steps every half turn may read as choppy, and air F shows the same steps
  mid-air.

## Findings

- **This brief's front was his pelvis, not his facing.** The toolkit took front from the
  entry hips until a parallel change on 18 October made it the fighter's facing (see the
  [toolkit README](../../tools/genra/README.md)). Measured in the Lab, Bonk's idle pelvis faces
  90° to his right, so the brief's "ahead" hits at ticks 17, 33 and 46 land on his right and
  sweep into the front by the end of each window. Its review views turned too: `gameplay`
  showed his left side and `profile` his back. The owner reviewed every view, so the approval
  stands; only the brief's directions and the review descriptions were rotated. The brief
  keeps `"frame": "hips"` so it reproduces the approved clip.
- **The Ability Lab does not apply `PresentationFrame` in Edit Mode.** It has no
  `ExecuteAlways`, so in Lab previews a bone-attached frame keeps the bone's scale and
  rotation: the whirl showed 3.3 times too big and tilted with his hips. Play Mode captures
  (`presentation_review.py`) show the runtime result.

## Evidence

- Run: `artifacts/genra-runs/20261018-032855-bonk-gf-blade-storm-spin/`. Conversion and the
  approved render: `artifacts/genra-unity-clips/20261018-034335-bonk-gf-blade-storm-spin/` and
  `artifacts/genra-unity-clips/20261018-034353-render-blade-storm-spin-v1/`.
- Integration logs, backups, reports (`before/`, `after-bind/`, `after-offsets/`) and probes:
  `artifacts/bonk-f-redo-20261018/`.
- Lab captures: `.ability-lab-cache/bonk/blade-storm-20261018/`. In-game frames and sheets:
  `tmp/vfx/bonk-f-20261018/`.
