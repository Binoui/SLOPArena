# Platform fighters other than Smash: normalized movement

Research slice for the speed calibration investigation (2026-10-31). The owner's goal: decide whether SLOPArena movement (Wibou especially) is too fast for precise spacing and "run in, jump, hit", by comparing against other platform fighters normalized to body height and attack reach.

Labels: [source: URL] = read on that page; [derived: formula] = computed from sourced numbers; [estimate: method] = measured or inferred, method stated. "Not found" means I looked and found nothing citable.

## Result by game

| Game | Coverage | Normalization confidence |
|---|---|---|
| Rivals of Aether 1 | Full base-cast movement table; height and reach from a template character | Speeds high; body height and reach medium-low (one template character, not each base-cast fighter) |
| Divine Knockout | Not found | None |
| Rivals of Aether 2 | Not found | None |
| Brawlhalla | Only qualitative Speed stat description | None |
| MultiVersus | Only an unread search snippet (Batman/Taz "Max Speed 2300"); both source pages 404 | None |
| NASB2 | Not searched (budget) | None |

## Rivals of Aether 1

### Units and frame rate

- 60 fps; speeds in pixels per frame [source: https://rivalsofaether.com/?p=146, and the RoA General Stats sheet intro "Pixels per frame: Measure of speed per frame", https://docs.google.com/spreadsheets/d/14JIjL_5t81JHqnJmU6BSsRosTe2JO8sFGUysM_9tDoU].
- Space: speeds, hitbox offsets (HG_HITBOX_X, HG_WIDTH) and hurtbox sprites all live in the 2x screen space (game pixels), while much character art is drawn at 1x and scaled 2x. Evidence [estimate: measured]: in the Sandbert template (roboshyguy/MS-SANDBERT on GitHub, a workshop template that copies Zetterburn's movement stats), the idle sprite's opaque box is 37 px tall at 1x (rows 24 to 61), while the jab and ftilt hurtbox sprites are 70 to 78 px tall. Hurtbox sprites are game-space, so the art is doubled. All RoA numbers below are in game pixels (2x space).
- `char_height` (default cast 36 to 74) is only the HUD/effect anchor [source: https://rivalsofaether.com/?p=146]. It is not body height: the official Sandbert example has char_height 52 [source: github.com/noterday/FG-Converter modules/base-character-files/Sandbert-Scripts/scripts/init.gml, read via gh api] while its standing body is about 74 game px. Do not normalize by char_height.

### Per-character movement (version 2.1.8.0)

All from the RoA General Stats sheet, "extracted from devmode files" [source: https://docs.google.com/spreadsheets/d/14JIjL_5t81JHqnJmU6BSsRosTe2JO8sFGUysM_9tDoU, All Stats tab, CSV export]. Values agree with the pre-1.3.0 table at [source: https://bengfox.github.io/rivals/characters/attristage.html].

| Character | Role | Dash (run) px/f | Initial dash px/f, frames | Walk | Ground friction | Dash stop frames | Max air px/f | Jump vsp | Gravity | Max fall |
|---|---|---|---|---|---|---|---|---|---|---|
| Etalus | slowest run | 5.00 | 5.00, 14 | 3.00 | 0.30 | 6 | 4.0 | 11.00 | 0.55 | 6* |
| Kragg | slow heavy | 5.75 | 6.25, 12 | 3.00 | 0.50 | 4 | 4.0 | 12.00 | 0.60 | 11 |
| Zetterburn | standard | 6.50 | 7.00, 14 | 3.25 | 0.50 | 4 | 4.0 | 10.99 | 0.50 | 10 |
| Ranno | fast | 7.50 | 7.50, 8 | 4.00 | 0.70 | 4 | 3.0 | 11.00 | 0.45 | 10 |
| Maypul | fastest run | 9.00 | 9.00, 10 | 4.50 | 1.00 | 6 | 6.0 | 10.50 | 0.50 | 10 |

*Etalus max fall 6 in the 2.1.8.0 sheet vs 11 in the older table; the sheet marks removed stats in red, which the CSV does not carry. It only affects his airtime.

Jumpsquat: jump_start_time 5 for everyone (docs: "jumpsquat frames minus one", so 6 frames) [source: https://rivalsofaether.com/?p=146; sheet]. The initial dash starts at full dash speed or above for all five (initial ≥ dash), so there is no acceleration ramp to full speed: the run is at full speed from frame 1 of the dash.

### Body height and reach

- Body height: about 74 game px for a Zetterburn-sized fighter [estimate: Sandbert template idle opaque box 37 px at 1x × 2; hurtbox sprites 70 to 78 px tall]. Per-character heights for the base cast: not found. Etalus and Kragg are larger and Maypul smaller than this, so their BH/s below is biased (Maypul's true BH/s is higher, Etalus's lower).
- Reach (template Sandbert, which keeps Sandbert's own attacks): jab 1 hitbox centre X 75, width 60, so far edge 105 px; ftilt hitbox centre X 80, width 120, far edge 140 px [source: roboshyguy/MS-SANDBERT scripts/attacks/jab.gml and ftilt.gml, read via gh api]. Cross-check: the ftilt hurtbox sprite reaches 166 px from the strip's left edge with the ftilt origin at 34 px (17 at 1x × 2), so the extended leg is about 132 px from the origin, consistent with the 140 px hitbox [estimate: measured opaque box]. Base-cast hitbox geometry: not found (the Academy frame data sheet lists timing and knockback, not positions).
- In body heights: jab about 1.4 BH, ftilt about 1.9 BH [derived: 105/74, 140/74].

### Derived metrics (H = 74 px)

| Character | Run px/s | BH/s | Frames to cross jab reach (105) | Frames to cross ftilt reach (140) | Air/run | Full-hop airtime | Main platform 640 px crossing |
|---|---|---|---|---|---|---|---|
| Etalus | 300 | 4.05 | 21.0 | 28.0 | 0.80 | 43 f, 0.72 s | 128 f, 2.13 s |
| Kragg | 345 | 4.66 | 18.3 | 24.3 | 0.70 | 40 f, 0.67 s | 111 f, 1.86 s |
| Zetterburn | 390 | 5.27 | 16.2 | 21.5 | 0.62 | 44 f, 0.73 s | 98 f, 1.64 s |
| Ranno | 450 | 6.08 | 14.0 | 18.7 | 0.40 | 48 f, 0.80 s | 85 f, 1.42 s |
| Maypul | 540 | 7.30 | 11.7 | 15.6 | 0.67 | 41 f, 0.68 s | 71 f, 1.19 s |

Formulas [derived]: run px/s = dash × 60; BH/s = run px/s / 74; frames to cross = reach / dash; air/run = air_max_speed / dash_speed; airtime = frames from vsp = −jump_speed until back at ground, adding gravity each frame and capping fall at max_fall (no fast fall, excludes the 6-frame jumpsquat); crossing = 640 / dash. Script: [`rivals_derive.py`](2026-10-31-run-speed-scripts/rivals_derive.py).

Stage width: main platform lengths 512 to 832 game px across 12 stages, typical 640 to 768 [source: https://bengfox.github.io/rivals/characters/attristage.html; the page calls its stage data unreliable and does not name the stages]. 640 px ≈ 8.6 BH [derived: 640/74].

Stopping: with ground_friction 0.3 to 1.0 px/f², a friction-only stop from full run takes 9 to 17 frames over 33 to 42 px (0.45 to 0.57 BH) [derived: v/f frames, v²/2f distance]. The real stop also applies dash_stop_percent (0.25 to 0.5 multiplier on entering idle) and a 4 to 6 frame dash stop state, so the true stop is shorter; treat this as an upper bound.

## Divine Knockout

Not found. Two searches (datamine/run speed; patch notes "movement speed increased/reduced from") returned only percentage ability buffs (for example Thor's Spinning Spree movement bonus 25% → 15%, per search snippet of 2023 patch coverage) and no base god run speed, capsule size, or stage dimensions. No DKO wiki or datamine with movement values surfaced. A real number would need an in-engine measurement (for example, timing a run across a known-size map element in a recording).

## Rivals of Aether 2

Not found. No RoA2 stats page with run speed came up in one targeted search; RoA2 values cannot be assumed to match RoA1.

## Brawlhalla

Not found as numbers. The wiki describes Speed (3 to 9, stances 1 to 11) only qualitatively ("faster run speed, acceleration, and friction"); its chart is image-only. The only unit on the page is a Defense threshold of 96 units per frame for knockback deceleration [source: https://brawlhalla.wiki.gg/wiki/Stats].

## MultiVersus

Not found as citable data. A search snippet attributes "Max Speed 2300" to Batman and Taz (unitless, fan datamine), but both pages that carried it returned 404 or did not contain it, so I do not cite it. No character height or capsule size found, so it could not be normalized anyway.

## Comparison with SLOPArena (numbers supplied in the brief, not researched)

| | Run BH/s | Reach (BH) | Frames to cross own reach | Air/run | Full-hop airtime |
|---|---|---|---|---|---|
| SLOPArena Wibou (14 m/s, 1.7 m capsule) | 8.2 | 0.65 to 1.1 | 4.7 to 8.1 | 0.50 to 0.78 (roster range) | 0.72 to 0.97 s |
| SLOPArena Bonk/Nyx (10 m/s) | 5.9 | 0.65 to 1.1 | 6.6 to 11.4 | 0.50 to 0.78 (roster range) | 0.72 to 0.97 s |
| RoA Maypul (fastest) | 7.3 | 1.4 to 1.9 | 11.7 to 15.6 | 0.67 | 0.68 s |
| RoA Zetterburn (standard) | 5.3 | 1.4 to 1.9 | 16.2 to 21.5 | 0.62 | 0.73 s |

[derived: SLOPArena reach 1.1 to 1.9 m / 1.7 m; frames = reach / (run / 60)]. Caveat: SLOPArena reach is "from body centre" per the brief; RoA HG_HITBOX_X is measured from the character origin, which is at the feet, horizontally centred, so both are horizontal distance from body centre.

## Takeaways

1. Wibou's run in body heights (8.2 BH/s) is above RoA's fastest runner (Maypul 7.3) and 1.55× RoA's standard (Zetterburn 5.3). SLOPArena's slowest fighters (5.9 BH/s) run like RoA's fast-standard tier.
2. The bigger gap is reach, not speed. RoA tilts reach about 1.4 to 1.9 body heights; SLOPArena ground reach is about 0.65 to 1.1. So a RoA fighter at full run needs 12 to 22 frames to cross its own reach, while Wibou needs 5 to 8. That frame count is the window a player has to time a "run in, hit" input, and it is two to three times shorter in SLOPArena. Speed alone would only explain part of it.
3. Airtime (about 0.7 s) and air/run ratio (0.4 to 0.8) are in the same band as RoA, so jump arcs are not the outlier; the ground approach is.

## Confidence

- RoA speeds, air speeds, jump, gravity: high (devmode extraction, two independent tables agree).
- RoA body height (74 px): medium-low. One template character, measured from sprites; not per base-cast fighter.
- RoA reach: medium-low. Sandbert template hitboxes, cross-checked against its hurtbox sprite; not base-cast data. Even if the base cast's tilts were 30% shorter, takeaway 2 still holds (frames to cross would still be 8 to 15 vs Wibou's 5 to 8).
- RoA stage width: low (the source calls it unreliable and does not name stages).
- DKO, RoA2, Brawlhalla, MultiVersus, NASB2: no normalization possible from public sources found within budget.
