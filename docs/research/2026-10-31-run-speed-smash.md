# Smash Bros. movement, normalized by body height and reach

Research slice for the speed-calibration investigation (2026-10-31). Goal: check
whether SLOPArena's run speeds are calibrated, by expressing Smash movement in
the character's own body heights and attack reach, which carry across unit
scales.

Labels: **[source]** a number read on the cited page, **[derived]** computed
from sourced numbers with the given formula, **[measured]** read off a sourced
image by the method in "Body height and reach method", **[estimate]** an
inference whose method is stated.

All Smash speeds are units per frame at 60 fps. 1 Melee-to-Ultimate unit is
about 0.1 m, per a developer statement and Steve's 10-unit, 1 m blocks
[source: https://www.ssbwiki.com/Unit]. Smash 64 units are about 1/3 cm, roughly
1/30 of the modern unit, so 64 speeds divide by about 30 to match later games
[source: https://www.ssbwiki.com/Unit, https://www.ssbwiki.com/Dash].

## Normalized table

BH/s = run × 60 / body height. Frames to cross reach = reach / run.
Airtime and stop distance are derived (formulas below).

| Game | Character | Run u/f | Init dash u/f | Traction | Air speed | Body height u | BH/s | Reach u | Reach/BH | Frames to cross reach | Air/run | Full-hop airtime | Stop from full run |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Ultimate | Incineroar (slowest) | 1.18 | 1.76 | 0.114 | 0.88 | 20.9 | 3.4 | 20.0 | 0.96 | 16.9 | 0.75 | 43 f (0.72 s) | 10 f, 6 u, 0.3 BH |
| Ultimate | Mario | 1.76 | 1.936 | 0.102 | 1.208 | 14.7 | 7.2 | 13.5 | 0.92 | 7.7 | 0.69 | 56 f (0.93 s) | 17 f, 15 u, 1.0 BH |
| Ultimate | Marth | 1.964 | 2.255 | 0.114 | 1.071 | 22.3 | 5.3 | 25.0 | 1.12 | 12.7 | 0.55 | 55 f (0.92 s) | 17 f, 17 u, 0.8 BH |
| Ultimate | Bowser | 1.971 | 2.255 | 0.104 | 1.155 | 24.9 | 4.7 | 20.0 | 0.80 | 10.1 | 0.59 | 45 f (0.75 s) | 19 f, 19 u, 0.8 BH |
| Ultimate | Fox | 2.402 | 2.09 | 0.115 | 1.11 | 15.6 | 9.2 | 17.6 | 1.13 | 7.3 | 0.46 | 36 f (0.60 s) | 21 f, 25 u, 1.6 BH |
| Ultimate | Captain Falcon | 2.552 | 1.98 | 0.105 | 1.218 | 21.4 | 7.2 | 19.2 | 0.90 | 7.5 | 0.48 | 48 f (0.80 s) | 24 f, 31 u, 1.4 BH |
| Ultimate | Sonic (fastest) | 3.85 | 2.31 | 0.138 | 1.208 | 12.1 | 19.1 | 18.3 | 1.51 | 4.8 | 0.31 | 53 f (0.88 s) | 28 f, 54 u, 4.4 BH |
| SSB64 (NA) | Luigi (slowest, tied with Ness) | 40 | 50 | 0.7 | 22.5 | 505 | 4.8 | 435 | 0.86 | 10.9 | 0.56 | 92 f (1.53 s) | 46 f, 914 u, 1.8 BH |
| SSB64 (NA) | Mario | 44 | 54 | 1.5 | 30 | 437 | 6.0 | 380 | 0.87 | 8.6 | 0.68 | 75 f (1.25 s) | 23 f, 516 u, 1.2 BH |
| SSB64 (NA) | Fox | 60 | 81 | 2.0 | 36 | 550 | 6.5 | 620 | 1.13 | 10.3 | 0.60 | 55 f (0.92 s) | 24 f, 720 u, 1.3 BH |
| SSB64 (NA) | Captain Falcon (fastest) | 75 | 80 | 1.8 | 31 | 674 | 6.7 | 642 | 0.95 | 8.6 | 0.41 | 64 f (1.07 s) | 33 f, 1250 u, 1.9 BH |

Labels per column:

- Ultimate run, initial dash: [source: https://www.ssbwiki.com/Dash, Ultimate rankings table].
- Ultimate traction: [source: https://www.ssbwiki.com/Traction]. Incineroar's 0.114 sits in the rowspan shared with Marth.
- Ultimate air speed: [source: https://www.ssbwiki.com/Air_speed]. Sonic's 1.208 sits in Mario's rowspan.
- SSB64 run, dash, traction, air speed, jump and gravity values: [source: https://github.com/VetriTheRetri/ssb-decomp-re, `src/relocData/203_MarioMain.c`, `209_FoxMain.c`, `221_LuigiMain.c`, `236_CaptainMain.c`]. These match the SmashWiki 64 tables [source: https://www.ssbwiki.com/Dash, https://www.ssbwiki.com/Air_speed, https://www.ssbwiki.com/Traction]. NA values are used where JP differs (Falcon run 75 NA vs 70 JP; Luigi air 22.5 NA vs 26 JP).
- Body height and reach: Ultimate [measured], SSB64 Falcon and Luigi [measured], SSB64 Mario and Fox [estimate]. See the method section.
- BH/s, frames to cross reach, reach/BH, air/run: [derived] with the formulas above.
- Full-hop airtime [derived]. Ultimate: rises to 0.55 × full-hop height in the first 4 frames ("jump initial height"), then ballistic with the listed gravity to the apex, then falls under gravity capped at fall speed. Full-hop heights, gravity and fall speed are from [source: https://www.ssbwiki.com/Jump, https://www.ssbwiki.com/Gravity, https://www.ssbwiki.com/Falling_speed]. Mario 36.33 u, g 0.087, fall 1.5. Fox 35, 0.23, 2.1. Falcon 37.31, 0.12, 1.865. Sonic 35, 0.09, 1.65. Incineroar 31.6, 0.126, 1.76. Marth 33.66, 0.075, 1.58. Bowser 33, 0.125, 1.77. SSB64: button full hop sets vertical speed = 77 × `jump_height_mul` + `jump_height_base` [source: https://github.com/mstan/SuperMarioBrosNESRecomp/blob/main/docs/falcon_movement_dependency.md, citing `ftcommonjump.c`], then a per-frame gravity sim with `tvel_base` cap. Frame-step order is approximated, so treat ±2 frames.
- Stop from full run [derived]: frames = run / traction, distance = run² / (2 × traction). SSB64 run brake uses traction × 1.25 [source: the mstan doc above]. Ultimate is treated as plain traction. Whether Ultimate scales traction above walk speed was not checked, so Ultimate stop distances may be overstated.

Jumpsquat: Ultimate 3 frames for everyone except Kazuya and Giga Bowser
[source: https://www.ssbwiki.com/Jump]. SSB64 3 frames for Mario, Fox, Luigi,
Ness, 4 for Falcon [source: https://www.ssbwiki.com/Jump; `kneebend_anim_length`
in the decomp files].

## Stage width

- Ultimate Final Destination and Battlefield forms: 16 Minecraft blocks, 16 m [source: https://www.ssbwiki.com/Final_Destination_(SSBU), trivia]. Blocks are 10 u [source: https://www.ssbwiki.com/Unit], so about 160 u [derived]. The block count is a floor, so the true width is 160 to 170 u.
- FD in Mario heights: 160 / 14.7 = 10.9 [derived].
- Time to cross FD at full run [derived, 160 u]: Incineroar 2.26 s, Mario 1.52 s, Marth and Bowser 1.35 s, Fox 1.11 s, Falcon 1.04 s, Sonic 0.69 s.
- Melee FD, SSB64 Dream Land: not found in the pages read.

## Body height and reach method

### Ultimate [measured]

SmashWiki move pages carry hitbox visualization GIFs and a hitbox table with
each hitbox's radius in units. For each fighter's forward tilt I took the GIF
and the table:

- Mario r 3.8 (`kneer`), Fox 3.4, Falcon 5.3 (`kneer`), Sonic 5.0, Incineroar 3.4 (`handl`), Marth 3.5 (sword tip), Bowser 5.5 [source: https://www.ssbwiki.com/Mario_(SSBU)/Forward_tilt and the matching Fox, Captain Falcon, Sonic, Incineroar, Marth, Bowser pages].
- Scale: the far hitbox circle's diameter in pixels = 2r units, giving pixels per unit for that GIF. Fits were checked by overlaying the fitted circle (good for six of seven; Bowser's automatic fit failed and was measured by hand at about 14.2 px/u, ±5%).
- Body height: the silhouette height, including the translucent hurtbox bubbles, in the GIF's first frame. That frame is the idle stance (Smash idles are crouched stances). It assumes the GIF zoom is constant across frames. The camera pans between frames, but per-frame heights stay within about ±10% of frame 0 except where the pose changes. Sonic uses the mean of his first (11.4) and last (12.8) frames.
- Reach: horizontal distance from the far edge of the hitbox to the torso centre (hip or chest), read against a unit ruler on the farthest active frame. Falcon has a hip hitbox at zero offset, which anchors his torso exactly. The others are read by eye at ±1.5 u.
- Independent cross-checks. The Ultimate Training stage grid has fine lines about 1 u apart (16/15 u) [source: https://www.ssbwiki.com/Unit]. In its screenshot (https://ssb.wiki.gallery/images/3/35/TrainingSSBU.jpg) Ike in a deep lunge stands about 15 to 16 u tall, consistent with the about 21 to 22 u standing Marth and Falcon. Mario at 14.7 u is about 1.47 m at 0.1 m/u.

### SSB64 [measured for Falcon and Luigi, estimate for Mario and Fox]

- The decomp gives each fighter's environment collision box: Mario top 320, Luigi 370, Fox 400, Falcon 400, Ness 250, Jigglypuff 230, Donkey Kong 500 [source: `map_coll` in the `src/relocData/*Main.c` files]. It is copied unscaled (`fp->coll_data.map_coll = attr->map_coll` in `src/ft/ftmanager.c`), while the model is scaled by `size` (Mario and Luigi 1.12 NA, Falcon 1.05). So the ECB top is below the visual head.
- Visual height came from SmashWiki's 64 forward-tilt hitbox GIFs (https://ssb.wiki.gallery/images/b/b9/Captain_Falcon_Forward_Tilt_Hitbox_Smash_64.gif, https://ssb.wiki.gallery/images/7/76/Luigi_Forward_Tilt_Hitbox_Smash_64.gif). The camera is static. 64 hitboxes draw as cubes. Assuming cube side = 2r (Luigi's two cubes, r 90 and 110, measure 77 and 94 px, ratio 0.82, which matches 90/110):
  - Falcon: cube about 48 px for r 110, idle about 147 px tall, so 674 u. The far edge is about 140 px from the torso, so a reach of 642 u.
  - Luigi: the cube face reads 94 to 105 px depending on whether the white outline counts, idle 228 px, so 478 to 534 u (505 used). Reach 410 to 460 u (435 used).
- Mario [estimate]: Luigi's height-to-ECB ratio (505 / 370) times Mario's ECB top 320 gives 437. Reach is Luigi's reach scaled by the same ratio, 380.
- Fox [estimate]: between Luigi's ratio and Falcon's (1.36 to 1.69) times ECB top 400 gives 545 to 675. Ultimate's Fox-to-Falcon height ratio (0.73) times 64 Falcon gives 491. 550 is used. Reach is Ultimate Fox's reach/BH (1.13) × 550, 620.
- Cross-check: divided by 30, 64 Falcon is 22.5 modern u (Ultimate 21.4) and 64 Mario is 14.6 (Ultimate 14.7). Bodies are about the same size in modern units across 64 and Ultimate.

### Confidence

- Ultimate body heights: ±10 to 15%. The scale comes from known hitbox radii inside the same image, so it is not a guess. The main errors are idle-pose choice, translucent hurtbox bubbles adding about 1 u, and an assumed constant GIF zoom. The Training-grid check agrees.
- Ultimate reach: ±1.5 u (about ±10%) from eyeballing the torso centre. Sonic's 18.3 u (1.5 BH) looks high and may reflect his forward lean in hit 2. Treat it as an upper bound.
- SSB64: Falcon and Luigi ±15% (cube rendering ambiguity), Mario and Fox ±20%.
- A second normalization for 64 that needs no images: run per ECB-top height per second. Mario 44 × 60 / 320 = 8.3, Fox 9.0, Falcon 11.3, Luigi 6.5 [derived]. This is comparable to our *capsule*-height metric, not to visual height.

## Melee, Brawl, Smash 4 (cheap extension)

Run and air speed [source: https://www.ssbwiki.com/Dash, https://www.ssbwiki.com/Air_speed].
Body height and reach reuse the Ultimate measurements [estimate: the unit
stayed about 0.1 m since Melee per https://www.ssbwiki.com/Unit. "Base model
sizes overall typically stay consistent from Brawl to later entries" per a user
draft, https://www.ssbwiki.com/User:SuperSqank/Size_modifiers, which also notes
Melee model scales differ (Marth 1.15× in Melee). Melee rows are the least
reliable].

| Game | Character | Run | BH/s | Frames to cross reach | Air/run |
|---|---|---|---|---|---|
| Melee | Mario | 1.5 | 6.1 | 9.0 | 0.57 |
| Melee | Fox | 2.2 | 8.5 | 8.0 | 0.38 |
| Melee | Captain Falcon | 2.3 | 6.4 | 8.3 | 0.49 |
| Melee | Marth | 1.8 | 4.8 | 13.9 | 0.50 |
| Melee | Bowser | 1.5 | 3.6 | 13.3 | 0.53 |
| Brawl | Mario | 1.5 | 6.1 | 9.0 | 0.63 |
| Brawl | Fox | 2.08 | 8.0 | 8.5 | 0.43 |
| Brawl | Captain Falcon | 2.18 | 6.1 | 8.8 | 0.54 |
| Brawl | Marth | 1.7 | 4.6 | 14.7 | 0.61 |
| Brawl | Bowser | 1.527 | 3.7 | 13.1 | 0.68 |
| Brawl | Sonic | 3.5 | 17.4 | 5.2 | 0.32 |
| Smash 4 | Mario | 1.6 | 6.5 | 8.4 | 0.72 |
| Smash 4 | Fox | 2.184 | 8.4 | 8.1 | 0.44 |
| Smash 4 | Captain Falcon | 2.32 | 6.5 | 8.3 | 0.47 |
| Smash 4 | Marth | 1.785 | 4.8 | 14.0 | 0.57 |
| Smash 4 | Bowser | 1.792 | 4.3 | 11.2 | 0.56 |
| Smash 4 | Sonic | 3.5 | 17.4 | 5.2 | 0.33 |

Slowest runners: Melee Jigglypuff and Zelda 1.1, Brawl Jigglypuff 1.1, Smash 4
Jigglypuff 1.155 (heaviest Mii Gunner 1.0621) [source: https://www.ssbwiki.com/Dash].

## SLOPArena next to these (our numbers from the brief, not researched)

[derived] from the brief's run speeds, the 1.7 m capsule (1.5 m for Manki and
Bashie), the 1.2 to 1.6 m visual height, and the 1.1 to 1.9 m reach:

| Fighter | Run m/s | Capsule BH/s | Visual BH/s | Ticks to cross 1.1 to 1.9 m reach |
|---|---|---|---|---|
| Wibou | 14 | 8.2 | 8.8 to 11.7 | 4.7 to 8.1 |
| FightGuy | 12 | 7.1 | 7.5 to 10.0 | 5.5 to 9.5 |
| Manki / Bashie / Salamander | 11.5 | 6.8 (7.7 at 1.5 m) | 7.2 to 9.6 | 5.7 to 9.9 |
| Bonk / Nyx | 10 | 5.9 | 6.2 to 8.3 | 6.6 to 11.4 |

## Takeaways

1. By body height, our whole roster sits in Smash's mid-to-fast band. Ultimate's Mario and Falcon run 7.2 BH/s, Fox 9.2, Sonic 19. Its slow tier (Incineroar, Bowser, Marth) runs 3.4 to 5.3. Smash 64 tops out at 6.7 (Falcon). Our slowest fighters (Bonk and Nyx, 6.2 to 8.3 visual BH/s) run like Ultimate's Mario. Wibou (8.8 to 11.7) is at or above Fox, the fastest fighter short of the Sonic outlier. We have no slow tier.
2. The reach-crossing figure fits the owner's complaint best. Ultimate's Mario, Fox and Falcon all need 7.3 to 7.7 frames at full run to cover their own forward-tilt reach, a tight band. SSB64 needs 8.6 to 10.9. Only Sonic gets below 5. Wibou needs 4.7 to 8.1 ticks, depending on which reach applies. With a short (1.1 to 1.3 m) move he closes his own reach as fast as Sonic. Our 4-tick jumpsquat (Smash: 3) adds a frame of drift before any aerial starts.
3. Fast Smash runners have low air speed relative to run: Fox 0.46, Falcon 0.48, Sonic 0.31, against 0.55 to 0.75 for the rest. Our air cap is 50 to 78% of run, which puts our fast fighters' air-to-run ratio at Mario's level, not Fox's. This only matters if our jump keeps run speed in the air, which this slice did not check (guess). Airtime is comparable: Ultimate full hops are 0.6 to 0.93 s against our 0.72 to 0.97 s.

## Sources read

- https://www.ssbwiki.com/Run (redirects to https://www.ssbwiki.com/Dash), https://www.ssbwiki.com/Air_speed, https://www.ssbwiki.com/Traction, https://www.ssbwiki.com/Unit, https://www.ssbwiki.com/Jump, https://www.ssbwiki.com/Gravity, https://www.ssbwiki.com/Falling_speed, https://www.ssbwiki.com/Final_Destination_(SSBU), https://www.ssbwiki.com/Battlefield_(SSBU), https://www.ssbwiki.com/User:SuperSqank/Size_modifiers
- Ultimate forward-tilt pages: https://www.ssbwiki.com/Mario_(SSBU)/Forward_tilt, https://www.ssbwiki.com/Fox_(SSBU)/Forward_tilt, https://www.ssbwiki.com/Captain_Falcon_(SSBU)/Forward_tilt, https://www.ssbwiki.com/Sonic_(SSBU)/Forward_tilt, https://www.ssbwiki.com/Incineroar_(SSBU)/Forward_tilt, https://www.ssbwiki.com/Marth_(SSBU)/Forward_tilt, https://www.ssbwiki.com/Bowser_(SSBU)/Forward_tilt, plus their GIFs on ssb.wiki.gallery
- SSB64 forward-tilt pages: https://www.ssbwiki.com/Captain_Falcon_(SSB)/Forward_tilt, https://www.ssbwiki.com/Luigi_(SSB)/Forward_tilt (GIFs linked above), https://www.ssbwiki.com/Mario_(SSB)/Forward_tilt, https://www.ssbwiki.com/Fox_(SSB)/Forward_tilt
- https://github.com/VetriTheRetri/ssb-decomp-re (`src/relocData/203_MarioMain.c`, `209_FoxMain.c`, `221_LuigiMain.c`, `236_CaptainMain.c`, `239_NessMain.c`, `233_PurinMain.c`, `213_DonkeyMain.c`; `src/ft/ftmanager.c` via code search)
- https://github.com/mstan/SuperMarioBrosNESRecomp/blob/main/docs/falcon_movement_dependency.md (64 jump-force formula and run-brake traction × 1.25)
- Not readable: https://smashboards.com/threads/character-height-a-rough-start.402759/ (HTTP 403).

## Reproduce

[`smash_derive.py`](2026-10-31-run-speed-scripts/smash_derive.py) recomputes every derived column from the sourced and measured inputs.
The hitbox GIFs, wikitext, decomp extracts and the image-measurement scripts stayed local and are not committed.
Download the GIFs from the SmashWiki URLs above to repeat the height and reach measurements.
