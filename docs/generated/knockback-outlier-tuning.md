# Knockback outliers and specials — 2026-10-11

The shared flight update passed the user's in-game feel check. This pass tunes authored outliers, keeping that flight model and DI unchanged. [Open the before/after viewer](knockback-outlier-visuals/index.html), or run `python3 tools/KnockbackVisuals/serve.py` and open http://localhost:8765/. Orange is the pre-tuning source; teal is the updated source. The [permanent viewer instructions](../../tools/KnockbackVisuals/README.md) cover both saved comparisons.

## Authored changes

- Wibou ground 3 changes from an 80° send to 60°, with growth 24 → 22. It remains a launcher with more outward travel. Air 3 also uses 60° instead of 78°, retaining its growth 22.
- Bonk air 2 becomes **Air Cleave**, replacing the 1-damage/80-growth pipeline probe with a 9-damage outward normal: angle 30°, base 6, growth 26, stun gate 16. It uses its existing animation, hitbox, startup, recovery, and landing lag, with interruptibility enabled and a 2.5 m attack-range hint.
- Wibou Rising Slash uses 60° and growth 18 instead of 45° and growth 80. Its recovery movement, damage and charge refund remain the same.
- Wibou Dash Slash's finisher uses 30°, base 8 and growth 32 instead of 45°, base 5 and growth 80. Its three damage tiers, charge thresholds and dash distances remain the same.
- Bonk Targeted Jump Slam sends at 35° instead of 55° in both variants. Its damage, knockback magnitude, travel and landing behavior remain the same.
- Manki Aerosol Inferno sends at 30° instead of 55°, retaining its damage, magnitude, timings and hit group.

FightGuy specials, projectile sends, Bonk's other specials, and Wibou's downward Skyfall finisher did not show the same excessive victim-flight issue and retain their authored values. Recovery movement is distinct from victim knockback and was not tuned in this pass.

## Dash Slash contact fix

At 120% victim damage, a fully charged Dash Slash at 5.6 m hit with its 12-damage finisher, then its 1-damage traversal hit on the next frozen tick. That weaker hit replaced the queued finisher launch, producing no upward flight. `ChargedDirectionalDashAbility` now removes traversal when its finisher begins. Earlier traversal contacts and the independent finisher remain supported; no traversal hit can subsequently overwrite the finisher during hitstop.

An actual Shared regression reproduces the lost launch before the fix and passes after it. Unity Ability Lab now records one finisher contact and a 2.21 m rise in the same scenario.

## Isolated flights at 120% pre-hit damage

| Hit component | Rise before → after | Travel before → after | Report end before → after |
| --- | ---: | ---: | ---: |
| Wibou ground 3 | 4.39 → 3.00 m | 1.05 → 3.85 m | 1.58 → 1.32 s |
| Wibou air 3 | 3.60 → 2.87 m | 1.09 → 3.72 m | 1.43 → 1.28 s |
| Bonk air 2 | 18.35 → 1.47 m | 62.34 → 8.34 m | 3.50 → 0.92 s |
| Wibou Rising Slash | 18.35 → 1.95 m | 61.74 → 2.39 m | 3.43 → 1.05 s |
| Wibou Dash Slash, tier 1 | 19.24 → 2.09 m | 64.98 → 12.13 m | 3.53 → 1.08 s |
| Wibou Dash Slash, tier 2 | 19.78 → 2.15 m | 67.20 → 12.48 m | 3.60 → 1.10 s |
| Wibou Dash Slash, tier 3 | 20.33 → 2.21 m | 69.24 → 12.89 m | 3.67 → 1.12 s |
| Bonk Targeted Jump Slam | 5.68 → 2.93 m | 10.63 → 13.93 m | 1.80 → 1.30 s |
| Manki Aerosol Inferno | 3.03 → 1.24 m | 5.38 → 6.92 m | 1.32 → 0.83 s |

The report end is the first grounded/actionable state. It may follow first ground contact when hitstun remains. Grounded carry beyond this point is excluded.

## Method and validation

`tools/MoveDataReport --knockback-audit` compiles source with the real Shared compiler, walks cooked hit components, and releases queued launch through `ServerSimulation`, preserving fixed hitstun. Passive mirror victims use 0%, 60%, 120% and 200% pre-hit damage, plus each component's rounded damage. Spikes start 3 m above the grounded stance. Exact vertical sends have floor behind the launch origin, preventing tiny negative `cos(90°)` roundoff from taking them off the heightmap. The saved viewer covers 112 components × four percents, including air aliases and charge tiers. Source hashes and recording limitations are embedded in the saved comparison.

These isolated flights exclude actual contact geometry, hitstop time, accumulated multihit damage, player inputs, self-hit hooks and attacker recovery motion. Inward pulls are shown along their launch axis rather than simulating a nearby attacker. Component paths do not establish complete special behavior or KO thresholds. Bonk air-2 damage changes on the after side deliberately.

- Wibou, Bonk and Manki pass native Unity source inspection, cook, persisted package verification and exact roster refresh. Shared builds and Editor recompilation pass; the current Unity error console is empty.
- Unity Ability Lab records actual contacts for Wibou ground 3, fully charged Dash Slash, Bonk air 2, Bonk jump-slam, and Manki flame. Their measured rises agree with the component telemetry. Grounded Rising Slash versus the grounded Lab dummy is a valid miss; a baked Shared scenario against an airborne victim confirms its real contact, upward launch, and recovery-charge refund.
- The Dash Slash regression and exact-vertical/fixed-hitstun report cases pass. Existing dash charge, traversal, shielding, obstruction, interruption and recovery coverage passes.
- Only the affected Manki Aerosol Inferno golden is regenerated. Its victim trajectory becomes lower and more outward; attacker state, damage, action and timing snapshots remain unchanged.
- The full Shared suite retains the two failures present before this task: bot inherited aerial momentum and airborne Ki Shot's expected action state. These are outside the tuning pass.

Human playtesting should check Wibou's juggle follow-ups and aerial E contacts, Bonk's air-2 contact readability and jump-slam edge pressure, Manki flame spacing, and Wibou Dash Slash's three charge tiers. Headless and Edit Mode verification do not establish feel or online match behavior.
