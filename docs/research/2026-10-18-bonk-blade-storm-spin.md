# Bonk Blade Storm (F): hip-height spin

Prepared by Claude (Opus 5.5), 18 October 2026. The owner said Bonk's Blade Storm "hits a
bit too high and misses a lot of the time" and wanted the sword lower. Of two proposals, a
full-body spin with the blade flat at hip height and a tilted twirl, the owner chose the
spin on the current clock, then approved the first candidate on the actual character ("very
good, lets go for it"). After an in-game review the same day, an engine hitstop fix and a
data rework followed: a [storm that holds its victims](#rework-a-storm-that-holds), steered at a
slow walk.

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
  fighters (before: 0–8 of 8), for 9.4–12.5 average damage (before: 0–4.4), 10.4–13.1 since
  the hitstop fix below.
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
- **Hitstop used up live hitboxes (engine, fixed the same day).** In the in-game capture the
  owner saw FightGuy ignore one spin pass. The resolver aged every hitbox each tick, but
  hitstop pauses the attacker's ability (ADR-0012). Replayed tick by tick in the Shared sim
  (FightGuy 1.3 m ahead at 60%): the chip hit at move tick 18 and froze Bonk for 6 ticks,
  spin hit 1 (ticks 16–19) expired during the freeze, and at tick 19 it would have touched.
  Now `SpellResolver.Tick` skips the age step of a melee hitbox (`CancelWithAbility`) whose
  owner is frozen that tick. Projectiles and explosions still age. One press of every move of
  the four fighters, 7,680 scenarios against an idle FightGuy, before and after: only Blade
  Storm changed, in 30 scenarios. 27 land one more hit; three land as many hits as before, but
  not the same ones. In one of those three, at 60%, the extra early hit pushed FightGuy out of
  the finisher's reach (17 → 7.5 damage). The Lab and in-game rows above predate the fix.
- **The spin windows close just before the blade reaches his front.** With the pelvis frame,
  spin hits 1 and 3 and the finisher end 1–2 ticks before the blade crosses straight ahead
  (around move ticks 21, 36 and 50). In the review setup, after the fix, spin hit 3 ends 8 cm
  short and the blade crosses FightGuy one tick later. Moving the three windows about 3 ticks
  later was proposed; the planted rework below replaced the windows instead.

## Rework: a storm that holds

After the hitstop fix the owner found the move "still a bit awkward even with this
animation": the hitboxes "probably not best", the knockback uncertain, and Bonk able to move
in the end lag. Measured in the Shared sim:

- **Movement.** `aimMovement: mobile` gave him his normal run, 10 m/s, for the whole spin.
  When the aim state ended at tick 75 the stick stopped steering, but attacks have no
  friction, so he kept that speed through the 25-tick recovery: 3.6 m of slide after letting
  go of the stick at tick 70.
- **Hitboxes.** The windows were built for the old twirl: a 74-tick chip that landed once,
  whenever it first touched, and three 4-tick windows half a turn apart, none following the
  clip's three turns. Hits depended on where the target stood: 3–4 in front or on his right,
  1–2 behind or on his left, and the finisher only in front or on his right.
- **Knockback.** Every small hit pushed the target away (angle 25°, base 2, growth 8), out of
  the next window.

Three directions were proposed: a planted storm that holds its victims (A), one huge swing
(B) and a hold-to-spin capability (C). The owner chose A. Only `ground.F` and `air.F` data
changed; the clip, its clock and the presentation events did not.

- `aimMovement: fixed`: the aim state zeroes horizontal speed on the ground and in the air,
  without touching gravity or facing. Removing the aim state instead would have left air F's
  momentum to slide on after landing, since specials keep running through a landing and
  absolute `setVelocity` also writes vertical speed.
- One window per turn, from the blade's measured rotation: hold hits on ticks 8–28 and
  29–42, the finisher on 43–58. The boundaries fall where the blade passes behind him (about
  ticks 29 and 43), so a target in front of him or beside him meets each window once.
- Hold hits: 2.5 damage, `towardOwner`, angle 0, base 20, growth 0, `fixedHitstunTicks` 24
  (FightGuy's Fist of Fury uses the same pull). Finisher: 12 damage, base 10, growth 36, at
  35° instead of 25°. Radii 0.35 and 0.42.

Verification:

- Local iteration on the source compiled with the cooked poses, then cook, verify and roster
  refresh: valid, source `32e69ba1…`, cooked content `6c2a208d…`, package `9f055da9…`. Only
  Bonk's cooked files and roster pin changed; zero console errors.
- Hit reliability report: each hit touches all 24 bearings at 1 and 1.5 m against FightGuy and
  Manki. Reach against FightGuy 0.30–3.50 m (3.60 m with the chip's 0.5 m radius).
- One press against the four fighters at 1–3 m, 8 directions, 0% and 60%: the full string (17
  damage) everywhere, except Manki 3 m away behind him (finisher only). Holding the stick moves
  him 0 m, ground and air.
- The review setup traced: hits at move ticks 19, 33 and 47; the pull brings FightGuy from 1.3
  to 0.7 m; the finisher sends him about 9.5 m out and 2.3 m up at 60%.
- Ability Lab and an in-game capture (Splash Deck, side and behind): the string lands at
  frames 19, 39 and 59; shields block all three hits; Bonk ends in Idle without moving.
- Shared tests 2978 passed, 2 existing skips. Two Bonk kit tests changed contract: one hold
  hit per turn then the finisher (`[2.5, 2.5, 12]`), and planted on the ground and after an
  air landing (replacing "allows movement while slashing").

Trade-offs and follow-ups:

- Nothing hits during the overhead wind-up (ticks 2–7) any more; the chip reached 2.0 m above
  his feet there, and air F now reaches 1.3 m.
- Air F started 1.2 m above standing FightGuy or Wibou lands only the finisher; the hold hits
  pass over their heads until he has fallen.
- The Tutorial's Bonk combo hint (Reverse Rising Slash → Blade Storm) relied on the mobile spin
  chasing the launched dummy (first hit at tick 72 with the stick held, none without). A search
  of every starter and follow-up found one opener for all tested distances: Reverse Rising Slash →
  jump and Reverse Air Slash, moving toward the dummy. `TutorialView.ComboOpeners` uses it now.
- Not verified: live Training feel, and whether the quick steps every half turn still read
  awkward once the hits connect. A planted-pivot clip on the same clock would keep these
  windows.

### Follow-up: steering at a walk

The owner tried the planted version in Training ("much better feel!") and wanted Bonk to move
during it, "although slower". Mobile aim always used the full run speed, so this needed an
engine option:

- `aimMovementSpeedScale` on a slot (above 0, at most 1, default 1 and then not written, so
  other packages' cooked bytes are unchanged): a mobile aim state walks the fighter at that
  fraction of its run and air speed. A scale below 1 raises the package's runtime API floor to
  `1.7.0`; the loader rejects such a package declared at `1.6.0`.
- When a timeline `setAimState` ends a mobile aim, horizontal speed stops. Attacks have no
  friction, so otherwise the walking carried through the recovery, as the old Blade Storm slid.
  Wibou's Rising Slash, the only other mobile slot, gets its aim from its capability and is
  unaffected.
- Blade Storm: `aimMovement: mobile`, scale 0.35, aim state ticks 0–65 (it ended at 74), so he
  stops walking when the spin stops.
- `ArcadeRecordCompatibility` projects the new field. Both revisions were bumped (Slopball 5,
  courses 2) for the earlier hitstop fix, a code change Arcade comparability missed.

Verification: walking at 3.50 m/s on the ground (3.85 m over the spin) and at 2.10 m/s in the
air, 0 m in the recovery and after landing. Holding the stick toward a fighter 1–4 m ahead, the
full string lands; from 5 m two hits land; walking away from a victim 2 m away drops it after
the first hit. Standing still, results match the planted version. Cook valid at runtime API
1.7.0 (source `652f3861…`, cooked `4cc0f316…`, package `9af8be33…`), zero console errors; Shared
2995 passed, Server 40 passed. The Tutorial opener stays Reverse Rising Slash → jump and Reverse
Air Slash: chasing with the slow spin lands only a late finisher.

## Evidence

- Run: `artifacts/genra-runs/20261018-032855-bonk-gf-blade-storm-spin/`. Conversion and the
  approved render: `artifacts/genra-unity-clips/20261018-034335-bonk-gf-blade-storm-spin/` and
  `artifacts/genra-unity-clips/20261018-034353-render-blade-storm-spin-v1/`.
- Integration logs, backups, reports (`before/`, `after-bind/`, `after-offsets/`) and probes:
  `artifacts/bonk-f-redo-20261018/`.
- Lab captures: `.ability-lab-cache/bonk/blade-storm-20261018/`. In-game frames and sheets:
  `tmp/vfx/bonk-f-20261018/`.
- Hitstop fix: the tick-by-tick duel before and after, the all-move scenario tables
  (`before.csv`, `after.csv`) and the rerun real-sim probe are in
  `artifacts/hitstop-age-20261018/` and `artifacts/bonk-f-redo-20261018/duel-1.3m-60.txt`.
- Rework: the source backup (`bonk-character.before-rework.json`), movement, blade-rotation,
  grid, trace, juggle and opener probes, cook and Lab logs in `artifacts/hitstop-age-20261018/`;
  in-game frames, sheets and `blade-storm-rework.mp4` in `tmp/vfx/bonk-f-rework-20261018/`.
