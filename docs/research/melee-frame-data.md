# Melee Frame Data — Full Reference (25 characters)

> Complete per-hitbox frame data for all 25 characters of Super Smash Bros. Melee,
> scraped 2026-08-12. Primary source: [`melee-framedata.theshoemaker.de`](https://melee-framedata.theshoemaker.de)
> (per-frame graphs, IASA, per-hitbox windows + KB tables). Specials cross-fetched from
> [`meleeframedata.com`](https://meleeframedata.com/captain_falcon) (the shoemaker site's special pages are dead links).
> Machine-readable copy: [`melee-frame-data.json`](melee-frame-data.json). Analysis: [`melee-frame-analysis.md`](melee-frame-analysis.md).

## Conventions

- **Frames = sim ticks.** Melee runs 60fps; SLOPArena sims at 60 ticks/s. 1 frame = 1 tick, 1:1.
- **Active windows are per-hitbox**, color-tagged (`red`/`green`/`blue`…) from the source's
  `hitframes_colors` tables — window → hitbox → damage/BKB/KBS. Multi-hitbox moves list each hitbox's window.
- **`●` in the source graph = "hitbox changed" marker**, not a separate window; union spans here are cross-checked
  against the per-hitbox rows (both sources agree on Falcon: dash 7-16, jab3 5-11, nair 7-12 + 20-29).
- **IASA** = earliest frame you can act (interrupt) before the move's total ends. `—` = none (full commitment).
- **AC** = auto-cancel windows (aerials): landing on frames `≤before` or `≥after` produces no landing lag.
- **Landing lag** column: `lag / L-cancel lag` (L-cancel halves it).
- Charge/chargeable specials (Marth Shield Breaker, Samus Charge Shot) put their hitbox on an `End`/`Fully Charged` submove.
- Category stats in the analysis doc use the primary submove only; moves whose primary submove has no hitbox
  (G&W Up Tilt, Peach F-Smash, Pikachu Up B, G&W/Roy/Yoshi Side B, Jigglypuff Rest…) are excluded there.
- Knockback columns: `damage (BKB/KBS)`. BKB = base knockback, KBS = knockback scaling/growth (Melee convention).
  Damage listed for every hitbox the source tables expose; some tables omit windows for sweetspot variants
  (they live inside a colored window — e.g. Falcon Nair green/yellow inside 7-12 / 20-29).

---

## Bowser

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Scratch | 17 | 16 | 5-6 | 3 (8/50) | — | — |
| Jab 2 - Right Scratch | 19 | 16 | 5-6 | 3 (16/50) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 27 | — | 6-9 | 10 (8/100) | — | — |
| Angled Mid | 27 | — | 6-9 | 10 (8/100) | — | — |
| Angled Down | 27 | — | 6-9 | 10 (8/100) | — | — |

| Up Tilt - Ceiling Scratch Floor | 23 | — | 8-9 [red]; 10-14 [green] | 9 (40/120); 8 (40/120) | — | — |

| Down Tilt - Scratch | 39 | 30 | 10-12 | 10 (40/30) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Horn Charge | 39 | 39 | 4-8 [red]; 9-14 [green] | 12 (16/100); 8 (8/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 44 | — | 12-15 [red]; 16-20 [green] | 17 (10/118); 13 (6/105) | — | — |

| Up Smash - Shell Shock | 54 | 45 | 7-10 | 14 (20/110); 15 (20/110) | — | — |

| Down Smash - Buzzsaw | 54 | 48 | 9-10 | 12 (34/66) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Gyroscope | 49 | — | 6-7 [red]; 8-28 [green] | 12 (10/70); 9 (10/80) | ≤6 / ≥28 | 20 / 10 |

| Forward Air - Jump Slash | 39 | 35 | 7-8 [red]; 9-22 [yellow] | 12 (10/100); 7 (10/80); 10 (10/100) | ≤7 / ≥33 | 20 / 10 |

| Back Air - Spike Stretch | 39 | 31 | 9-12 | 12 (10/100) | ≤9 / ≥24 | 20 / 10 |

| Up Air - Horn Toss | 39 | 38 | 9-12 | 12 (30/100) | ≤9 / ≥36 | 20 / 10 |

| Down Air - Scrub Brush | 49 | — | 5-6 + 8-9 + 11-12 + 14-15 + 17-18 + 20-21 + 23-24 + 26-27 + 29-30 | 2 (20/100); 2 (10/100) | ≤5 / ≥41 | 30 / 15 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Catapult | 35 | — | 10 | 7 (40/110) | — | — |

| Back Throw - Reverse Throw | 49 | — | — | — | — | — |

| Up Throw - Blender | 41 | — | — | — | — | — |

| Down Throw - Bowser Slam | 84 | — | 10-12 [red]; 23-25 [red]; 36-38 [red]; 49-51 [red]; 62-64 [red]; 75 [green] | 1 (0/100); 3 (10/100) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 92 | — | 23-62 | 2 | — | — |

| Side B | 59 | — | 16-18 | 12 | — | — |

| Up B | 79 | — | 5-46 | 13 | — | — |

| Down B | 106 | — | 42-47 | 21 | — | — |

---

## Captain Falcon

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Jab | 21 | 16 | 3-5 | 2 (0/100) | — | — |
| Jab 2 - Straight | 19 | 18 | 4-6 | 3 (0/100) | — | — |
| Jab 3 - Knee | 31 | 22 | 5-8 [red]; 9-11 [green] | 8 (20/100); 6 (0/100) | — | — |
| Rapidjabs Start | 5 | — | — | — | — | — |
| Rapidjabs Loop | 39 | — | 4-5 | 1 (0/70) | — | — |
| Rapidjabs End | 8 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 29 | — | 9-11 | 12 (10/100); 11 (10/100) | — | — |
| Angled Mid | 29 | — | 9-11 | 11 (10/100) | — | — |
| Angled Down | 29 | — | 9-11 | 10 (0/100); 11 (10/100) | — | — |

| Up Tilt - Wheel Kick | 39 | 38 | 17-21 | 13 (50/80) | — | — |

| Down Tilt - Crouching Kick | 35 | 35 | 10-15 | 12 (25/75); 12 (25/75); 12 (25/75) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Turbo Shoulder | 39 | 38 | 7-9 [red]; 10-16 [green] | 10 (22/90); 7 (10/50) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 64 | 60 | 18-21 | 21 (24/100) | — | — |
| Angled Mid | 64 | 60 | 18-21 | 20 (24/100); 19 (24/100) | — | — |
| Angled Down | 64 | 60 | 18-21 [red]; 63 [green] | 19 (24/100); 8 (0/100); 8 (0/100); 14 (30/105); 14 (30/105) | — | — |

| Up Smash - Pinwheel Kick | 54 | 40 | 21-22 [red]; 27-28 [orange] | 8 (0/100); 13 (30/128); 8 (0/100); 14 (30/105); 14 (30/105); 13 (30/126) | — | — |

| Down Smash - Pendulum Kick | 49 | 45 | 19-22 [red]; 29-32 [green] | 18 (30/100); 16 (20/100) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Rotary Kick | 44 | — | 7-12 [red]; 20-29 [blue] | 6 (0/100); 7 (40/100); 5 (0/100); 6 (0/100) | ≤4 / ≥33 | 15 / 7 |

| Forward Air - Knee Smash | 39 | 36 | 14-16 [red]; 17-30 [green] | 18 (24/100); 6 (35/80) | ≤7 / ≥34 | 19 / 9 |

| Back Air - Reverse Knuckle | 35 | 29 | 10-13 [red]; 14-17 [yellow] | 14 (20/100); 8 (20/100); 14 (0/100); 8 (0/100) | ≤7 / ≥20 | 18 / 9 |

| Up Air - Overhead Kick | 33 | 30 | 6-10 [red]; 11-13 [yellow]; 14 [orange] | 13 (10/100); 12 (8/80); 8 (6/70); 12 (10/100); 10 (8/80); 6 (6/70) | ≤1 / ≥21 | 15 / 7 |

| Down Air - Step On It | 44 | 38 | 16-20 | 16 (40/100); 16 (40/100) | ≤4 / ≥35 | 24 / 12 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Body Blow | 39 | — | 11-17 | 5 (70/100) | — | — |

| Back Throw - Kick Back | 49 | — | 12-19 | 5 (70/100) | — | — |

| Up Throw - Rising Palm | 43 | — | 11-28 | 4 (60/100) | — | — |

| Down Throw - Throw Down | 39 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 99 | — | 52-56 | 27 | — | — |

| Side B | 79 | — | 15-34 | 7 | — | — |

| Up B | 64 | — | 13-33 | 10 | — | — |

| Down B | 64 | — | 14-32 | 15 | — | — |

---

## Donkey Kong

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Scratch | 24 | — | 5-7 | 4 (0/100) | — | — |
| Jab 2 - Right Scratch | 33 | — | 4-10 | 6 (0/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 33 | — | 8-11 | 11 (10/100) | — | — |
| Angled Mid | 33 | — | 8-11 | 10 (10/100) | — | — |
| Angled Down | 33 | — | 8-11 | 9 (10/100) | — | — |

| Up Tilt - Ceiling Scratch Floor | 39 | — | 6-11 | 9 (40/105); 10 (40/110); 11 (40/115) | — | — |

| Down Tilt - Scratch | 22 | — | 6-9 | 7 (10/80) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Horn Charge | 54 | — | 9-12 [red]; 13-20 [green] | 11 (115/15); 9 (0/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 54 | — | 22-23 | 20 (22/94); 21 (22/94); 19 (18/100); 18 (18/100) | — | — |

| Up Smash - Shell Shock | 53 | — | 14-16 | 18 (40/93) | — | — |

| Down Smash - Buzzsaw | 55 | — | 10-13 | 16 (35/100); 14 (35/100) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Gyroscope | 43 | 39 | 10-13 [red]; 14-26 [green] | 12 (0/100); 10 (0/100) | ≤10 / ≥38 | 20 / 10 |

| Forward Air - Jump Slash | 59 | — | 25-26 [red]; 27-29 [yellow] | 16 (20/100); 16 (50/80); 16 (20/100); 16 (50/80) | ≤1 / ≥59 | 30 / 15 |

| Back Air - Spike Stretch | 39 | 32 | 7-8 [red]; 9-15 [green] | 13 (10/100); 9 (0/100) | ≤7 / ≥19 | 15 / 7 |

| Up Air - Horn Toss | 44 | 38 | 6-8 | 14 (32/90) | ≤6 / ≥12 | 25 / 12 |

| Down Air - Scrub Brush | 54 | — | 18-23 | 16 (38/90); 13 (20/90) | ≤3 / ≥49 | 31 / 15 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Catapult | 19 | — | — | — | — | — |

| Back Throw - Reverse Throw | 39 | — | — | — | — | — |

| Up Throw - Blender | 43 | — | — | — | — | — |

| Down Throw - Bowser Slam | 59 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 46 | — | 17-21 | 30 | — | — |

| Side B | 59 | — | 20-21 | 5 | — | — |

| Up B | 84 | — | 3-58 | 12 | — | — |

| Down B | 60 | — | 19-31 | 11 | — | — |

---

## Dr. Mario

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Jab | 15 | — | 2-3 | 4 (0/100); 4 (0/100) | — | — |
| Jab 2 - Right Cross | 17 | — | 2-3 | 3 (0/100); 3 (0/100) | — | — |
| Jab 3 - Toe Kick | 21 | — | 4-8 | 6 (18/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 29 | — | 4-8 | 9 (11/100) | — | — |
| Angled Mid | 29 | — | 4-8 | 8 (10/100) | — | — |
| Angled Down | 29 | — | 4-8 | 7 (9/100) | — | — |

| Up Tilt - Uppercut | 30 | 30 | 4 [red]; 5-13 [green] | 10 (20/95); 8 (30/122); 8 (30/120); 8 (30/118) | — | — |

| Down Tilt - Reflex Test | 34 | — | 5-8 | 9 (20/82) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Slide | 43 | 38 | 6-9 [red]; 10-25 [red] | 9 (70/30); 8 (60/50) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 41 | — | 12-16 | 20 (30/97) | — | — |
| Angled Mid | 41 | — | 12-16 | 19 (30/97) | — | — |
| Angled Down | 41 | — | 12-16 | 18 (30/97) | — | — |

| Up Smash - Ear, Nose and Throat | 39 | — | 9-10 [red]; 11 [yellow] | 16 (35/95); 13 (35/95); 16 (35/95); 13 (35/95) | — | — |

| Down Smash - Surgical Sweep | 37 | — | 5-6 [red]; 14-15 [yellow] | 18 (45/75); 15 (40/75); 17 (45/75); 13 (40/75) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Dr. Kick | 45 | — | 3-19 [red]; 20-31 [green] | 10 (20/100); 14 (20/100) | ≤3 / ≥35 | 18 / 9 |

| Forward Air - Dr. Punch | 74 | 60 | 18-22 | 17 (50/100); 16 (40/100) | ≤3 / ≥42 | 25 / 12 |

| Back Air - Drop Kick, M.D. | 28 | — | 6-8 [red]; 9-16 [green] | 8 (43/65); 7 (20/100) | ≤6 / ≥18 | 18 / 9 |

| Up Air - Bicycle Kick | 33 | 30 | 4-9 | 10 (0/100) | ≤2 / ≥15 | 18 / 9 |

| Down Air - Bone Drill | 38 | 38 | 10-11 + 13-14 + 16-17 + 19-20 + 22-23 + 25-26 + 28-29 + 31-32 | 3 (0/100) | ≤6 / ≥35 | 24 / 12 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Routine Physical | 27 | — | — | — | — | — |

| Back Throw - Traction | 66 | — | — | — | — | — |

| Up Throw - Check Up | 39 | — | — | — | — | — |

| Down Throw - Hospital Bed | 39 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 43 | — | 14-89 | 8 | — | — |

| Side B | 35 | — | 12-14 | 12 | — | — |

| Up B | 37 | — | 3-21 | 5 | — | — |

| Down B | 79 | — | 8-39 | 4 | — | — |

---

## Falco

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Jab | 17 | 16 | 2-3 | 4 (0/100) | — | — |
| Jab 2 - Straight | 19 | 18 | 2-3 | 4 (0/100) | — | — |
| Rapidjabs Start | 6 | — | — | — | — | — |
| Rapidjabs Loop | 35 | — | 2-3 | 1 (10/80) | — | — |
| Rapidjabs End | 8 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 26 | — | 5-9 | 9 (0/100) | — | — |
| Angled Mid | 26 | — | 5-9 | 9 (0/100) | — | — |
| Angled Down | 26 | — | 5-9 | 9 (0/100) | — | — |

| Up Tilt - Back Kick | 23 | 23 | 5-11 | 9 (30/120); 9 (30/120) | — | — |

| Down Tilt - Bird Sweep | 29 | 28 | 7-9 | 13 (25/125) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Jumping Side Kick | 39 | 36 | 4-7 [red]; 8-17 [green] | 9 (35/90); 6 (20/90) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 39 | — | 12-16 [red]; 17-21 [yellow] | 17 (40/90); 14 (10/105); 17 (40/90) | — | — |

| Up Smash - Flip Kick | 43 | — | 7-10 [red]; 11-15 [green] | 14 (25/100); 12 (10/100) | — | — |

| Down Smash - Falco Split | 49 | 46 | 6-10 | 16 (20/70); 13 (20/70) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Flying Kick | 49 | 42 | 4-7 [red]; 8-31 [green] | 12 (10/100); 9 (0/100) | ≤4 / ≥36 | 15 / 7 |

| Forward Air - Cyclone Kick | 59 | 53 | 6-8 [red]; 16-18 [green]; 24-26 [yellow]; 33-35 [blue]; 43-45 [orange] | 9 (10/100); 8 (10/100); 7 (10/100); 5 (10/100); 3 (50/100) | ≤6 / ≥48 | 22 / 11 |

| Back Air - Reverse Spin Kick | 39 | 38 | 4-7 [red]; 8-19 [green] | 15 (0/100); 9 (0/100) | ≤4 / ≥22 | 20 / 10 |

| Up Air - Falco Flip | 39 | 36 | 8-9 [red]; 11-14 [yellow] | 6 (40/20); 10 (22/120); 6 (30/20); 10 (30/20) | ≤8 / ≥25 | 18 / 9 |

| Down Air - Air Drill | 49 | — | 5-14 [red]; 15-24 [green] | 12 (10/100); 9 (20/100) | ≤5 / ≥29 | 18 / 9 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Elbow Bash | 33 | — | 10 | 4 (60/180) | — | — |

| Back Throw - Skeet Blaster | 38 | — | — | — | — | — |

| Up Throw - Star Blaster | 38 | — | — | — | — | — |

| Down Throw - Floor Blaster | 43 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 57 | — | 23-122 | 3 | — | — |

| Side B | 59 | — | 18-21 | 7 | — | — |

| Up B | 84 | — | 43-64 | 16 | — | — |

| Down B | 3 | — | 1 | 8 | — | — |

---

## Fox

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Jab | 17 | 16 | 2-3 | 4 (0/100) | — | — |
| Jab 2 - Straight | 19 | 18 | 2-3 | 4 (0/100) | — | — |
| Rapidjabs Start | 6 | — | — | — | — | — |
| Rapidjabs Loop | 35 | — | 2-3 | 1 (10/80) | — | — |
| Rapidjabs End | 8 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 26 | — | 5-8 | 9 (0/100); 9 (0/100) | — | — |
| Angled Mid | 26 | — | 5-8 | 9 (0/100); 9 (0/100) | — | — |
| Angled Down | 26 | — | 5-8 | 9 (0/100); 9 (0/100) | — | — |

| Up Tilt - Back Kick | 23 | 23 | 5-11 | 12 (18/140); 9 (18/140); 9 (18/140) | — | — |

| Down Tilt - Fox Tail | 29 | 28 | 7-9 | 10 (25/125); 10 (25/125); 10 (25/125) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Jumping Side Kick | 39 | 36 | 4-7 [red]; 8-17 [green] | 7 (35/90); 5 (20/90) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 39 | — | 12-16 [red]; 17-22 [green] | 15 (10/105); 12 (2/100) | — | — |

| Up Smash - Flip Kick | 41 | — | 7-9 [red]; 10-17 [green] | 18 (30/112); 13 (10/100) | — | — |

| Down Smash - Fox Split | 49 | 46 | 6-10 | 15 (20/65); 12 (20/65) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Flying Kick | 49 | 42 | 4-7 [red]; 8-31 [green] | 12 (10/100); 9 (0/100) | ≤4 / ≥36 | 15 / 7 |

| Forward Air - Tornado Kick | 59 | 53 | 6-8 [red]; 16-18 [green]; 24-26 [yellow]; 33-35 [blue]; 43-45 [orange] | 7 (10/100); 5 (10/100); 6 (10/100); 4 (10/100); 3 (50/100) | ≤6 / ≥48 | 22 / 11 |

| Back Air - Reverse Spin Kick | 39 | 38 | 4-7 [red]; 8-19 [green] | 15 (0/100); 9 (0/100) | ≤4 / ≥22 | 20 / 10 |

| Up Air - McCloud Flip | 39 | 36 | 8-9 [red]; 11-14 [green] | 5 (0/120); 13 (40/116) | ≤8 / ≥25 | 18 / 9 |

| Down Air - Air Drill | 49 | — | 5-6 + 8-9 + 11-12 + 14-15 + 17-18 + 20-21 + 23-24 + 26-27 | 3 (0/100); 2 (0/100) | ≤5 / ≥33 | 18 / 9 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Elbow Bash | 33 | — | 10 | 4 (10/100) | — | — |

| Back Throw - Skeet Blaster | 38 | — | — | — | — | — |

| Up Throw - Star Blaster | 38 | — | — | — | — | — |

| Down Throw - Floor Blaster | 43 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 36 | — | 10-44 | 3 | — | — |

| Side B | 63 | — | 22-25 | 7 | — | — |

| Up B | 92 | — | 20-72 | 14 | — | — |

| Down B | 3 | — | 1 | 5 | — | — |

---

## Mr. Game & Watch

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Green House | 17 | 16 | 4-6 | 3 (0/100); 3 (0/100) | — | — |
| Rapidjabs Start | 3 | — | — | — | — | — |
| Rapidjabs Loop | 11 | — | 7-9 | 3 (0/100) | — | — |
| Rapidjabs End | 8 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 44 | 42 | 13-30 | 10 (10/100) | — | — |

| Up Tilt - Flat Man | 29 | — | — | 9 (30/127); 9 (30/125); 9 (30/123) | — | — |

| Down Tilt - Manhole | 29 | 26 | 6-13 | 12 (65/100); 9 (80/40) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Helmet | 37 | — | 6-29 | 9 (70/30) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 44 | 42 | 13-16 [red]; 17-33 [yellow] | 18 (44/100); 6 (44/100); 14 (44/100); 6 (44/100) | — | — |

| Up Smash - Octopus | 44 | 40 | 24-28 | 18 (40/96) | — | — |

| Down Smash - Vermin | 34 | — | 15-19 | 10 (10/50); 16 (60/90) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Parachute | 44 | — | 20-29 | 16 (20/100) | ≤3 / ≥44 | 15 / 7 |

| Forward Air - Cement Factory | 44 | — | 10-12 [red]; 13-32 [green] | 16 (30/80); 6 (10/80) | ≤3 / ≥44 | 25 / 12 |

| Back Air - Turtle Bridge | 39 | — | 10-24 | 5 (60/60) | ≤10 / ≥39 | 18 / 9 |

| Up Air - Spit Ball Sparky | 39 | — | 7-16 [red]; 21-22 [green] | 7 (12/100); 9 (55/100) | ≤7 / ≥39 | 15 / 7 |

| Down Air - Donkey Kong Jr. | 49 | — | 12 [red]; 13-38 [green] | 14 (20/100); 13 (20/100) | ≤6 / ≥49 | 20 / 10 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Forward Ball | 69 | — | — | — | — | — |

| Back Throw - Backward Ball | 69 | — | — | — | — | — |

| Up Throw - Vertical Ball | 69 | — | — | — | — | — |

| Down Throw - Drop Ball | 69 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 49 | — | 18-21 | 5 | — | — |

| Side B | — | — | — | — | — | — |

| Up B | 39 | — | 1-37 | 6 | — | — |

| Down B | 49 | — | 2-37 | 200 | — | — |

---

## Ganondorf

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Thunder Punch | 21 | 19 | 3-5 | 7 (30/100); 8 (30/100) | — | — |
| Jab 2 | 30 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 29 | — | 9-11 | 14 (20/100); 13 (20/100); 12 (20/100) | — | — |
| Angled Mid | 29 | — | 9-11 | 13 (20/100); 12 (20/100); 11 (20/100) | — | — |
| Angled Down | 29 | — | 9-11 | 12 (20/100); 11 (20/100); 10 (20/100) | — | — |

| Up Tilt - Volcano Kick | 114 | 113 | 81-83 | 27 (110/80) | — | — |

| Down Tilt - Sweeping Snake | 35 | 35 | 10-12 | 12 (30/100); 12 (30/100); 12 (30/100) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Iron Shoulder | 39 | 38 | 7-9 [red]; 10-16 [green] | 14 (40/80); 10 (25/60) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 66 | 60 | 20-24 | 24 (40/85) | — | — |
| Angled Mid | 66 | 60 | 20-24 | 22 (40/83); 20 (40/80) | — | — |
| Angled Down | 66 | 60 | 20-24 [red]; 63-65 [green] | 20 (40/80); 22 (50/80); 22 (50/80); 22 (50/80) | — | — |

| Up Smash - Tornado Kick | 54 | 40 | 21-23 [red]; 26-29 [blue] | 22 (50/80); 19 (40/80); 22 (50/80); 22 (50/80); 19 (40/80); 19 (40/80) | — | — |

| Down Smash - Leg Whip | 49 | 47 | 19-22 [red]; 29-32 [yellow] | 8 (0/100); 14 (60/110); 8 (0/100); 12 (60/110) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Swooping Keese | 44 | — | 7-8 [red]; 20-21 [blue] | 12 (30/100); 12 (50/100); 12 (30/100); 12 (30/100) | ≤4 / ≥25 | 25 / 12 |

| Forward Air - Skull Crusher | 44 | 35 | 14-19 | 17 (60/80) | ≤7 / ≥33 | 25 / 12 |

| Back Air - Hidden Gauntlet | 35 | 29 | 10-15 | 16 (30/100); 16 (10/100) | ≤7 / ≥18 | 25 / 12 |

| Up Air - Vulture Kick | 33 | 30 | 6-10 [red]; 11-13 [yellow]; 14-16 [orange] | 13 (35/100); 12 (30/80); 8 (20/70); 12 (35/100); 10 (30/80); 6 (20/70) | ≤1 / ≥21 | 25 / 12 |

| Down Air - Thunder Drop | 44 | 38 | 16-20 | 22 (50/100) | ≤4 / ≥35 | 35 / 17 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Get Punch | 39 | — | 11-17 | 5 (70/100) | — | — |

| Back Throw - Blind Mule Kick | 49 | — | 12-19 | 5 (70/100) | — | — |

| Up Throw - Jaw Breaker | 43 | — | 11-28 | 4 (60/100) | — | — |

| Down Throw - Dirt Nap | 39 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 119 | — | 70-72 | 34 | — | — |

| Side B | 79 | — | 15-34 | 17 | — | — |

| Up B | 64 | — | 13-33 | 13 | — | — |

| Down B | 77 | — | 14-34 | 15 | — | — |

---

## Jigglypuff

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Jab | 17 | 16 | 5-6 | 3 (8/50) | — | — |
| Jab 2 - Right Jab | 19 | 16 | 5-6 | 3 (16/50) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 27 | — | 6-9 | 10 (8/100) | — | — |
| Angled Mid | 27 | — | 6-9 | 10 (8/100) | — | — |
| Angled Down | 27 | — | 6-9 | 10 (8/100) | — | — |

| Up Tilt - Back Kick | 23 | — | 8-9 [red]; 10-14 [green] | — | — | — |

| Down Tilt - Trip | 39 | 30 | 10-12 | 10 (40/30) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Jiggly Ram | 39 | 39 | 4-8 [red]; 9-14 [green] | 12 (16/100); 8 (8/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 44 | — | 12-15 [red]; 16-20 [green] | 17 (10/118); 13 (6/105) | — | — |

| Up Smash - Headbutt | 54 | 45 | 7-10 | 14 (20/110); 15 (20/110) | — | — |

| Down Smash - Jiggly Split | 54 | 48 | 9-10 | 12 (34/66) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Jigglypuff Kick | 49 | — | 6-7 [red]; 8-28 [green] | 12 (10/70); 9 (10/80) | ≤6 / ≥28 | 20 / 10 |

| Forward Air - Drop Kick | 39 | 35 | 7-8 [red]; 9-22 [yellow] | 12 (10/100); 7 (10/80); 10 (10/100) | ≤7 / ≥33 | 20 / 10 |

| Back Air - Spinning Back Kick | 39 | 31 | 9-12 | 12 (10/100) | ≤9 / ≥24 | 20 / 10 |

| Up Air - Mow Down | 39 | 38 | 9-12 | 12 (30/100) | ≤9 / ≥36 | 20 / 10 |

| Down Air - Spinning Kick | 49 | — | 5-6 + 8-9 + 11-12 + 14-15 + 17-18 + 20-21 + 23-24 + 26-27 + 29-30 | 2 (20/100); 2 (10/100) | ≤5 / ≥41 | 30 / 15 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Bumper | 35 | — | 10 | 7 (40/110) | — | — |

| Back Throw - Back Buster | 49 | — | — | — | — | — |

| Up Throw - Puff Launch | 41 | — | — | — | — | — |

| Down Throw - Grinder | 84 | — | 10-12 [red]; 23-25 [red]; 36-38 [red]; 49-51 [red]; 62-64 [red]; 75 [green] | 1 (0/100); 3 (10/100) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | — | — | — | — | — | — |

| Side B | 45 | — | 12-27 | 13 | — | — |

| Up B | 179 | — | 28-125 | 0 | — | — |

| Down B | 249 | — | 1 | 28 | — | — |

---

## Kirby

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Right Punch | 17 | 16 | 3-4 | 3 (8/50) | — | — |
| Jab 2 - Left Punch | 19 | 16 | 2-3 | 4 (8/50) | — | — |
| Rapidjabs Start | 7 | — | — | — | — | — |
| Rapidjabs Loop | 19 | — | 2-3 | 1 (8/50) | — | — |
| Rapidjabs End | 9 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 32 | 28 | 5-8 | 11 (8/100) | — | — |
| Angled Mid | 32 | 28 | 5-8 | 11 (8/100) | — | — |
| Angled Down | 32 | 28 | 5-8 | 11 (8/100) | — | — |

| Up Tilt - Back Kick | 23 | — | 4 [red]; 5-7 [yellow] | 8 (40/118); 6 (40/118); 8 (40/114); 6 (40/114) | — | — |

| Down Tilt - Squish Kick | 29 | — | 4-7 | 10 (40/30) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Fire Kirby | 63 | 60 | 9-15 [red]; 16-43 [green] | 8 (70/66); 5 (50/66) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 49 | — | 13-15 [red]; 16-21 [green] | 15 (24/96); 13 (18/96) | — | — |
| Angled Mid | 49 | — | 13-15 [red]; 16-21 [green] | 15 (24/96); 13 (18/96) | — | — |
| Angled Down | 49 | — | 13-15 [red]; 16-21 [green] | 15 (24/96); 13 (18/96) | — | — |

| Up Smash - Kirby Flip Kick | 49 | — | 13 [red]; 14-15 [yellow]; 16-23 [orange] | 15 (30/118); 14 (20/100); 13 (10/50); 13 (30/110); 12 (20/100); 12 (10/50) | — | — |

| Down Smash - Propeller Kick | 55 | 50 | 7-22 | 14 (30/100); 14 (20/80) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Twinkle Star | 79 | 50 | 10 [red]; 11-17 [red]; 18-29 [green]; 30-34 [green] | 12 (10/80); 8 (10/80) | ≤10 / ≥37 | 15 / 7 |

| Forward Air - Spiral Kick | 49 | 40 | 10-11 [red]; 17-18 [red]; 25-26 [green] | 5 (30/70); 8 (30/130) | ≤10 / ≥37 | 20 / 10 |

| Back Air - Drop Kick | 43 | 36 | 6-8 [red]; 9-20 [green] | 14 (10/100); 10 (0/100) | ≤6 / ≥27 | 15 / 7 |

| Up Air - Floating Flip Kick | 39 | 36 | 11-13 | 15 (30/100) | ≤11 / ≥16 | 15 / 7 |

| Down Air - Screw Driver | 59 | 55 | 18-19 + 21-22 + 24-25 + 27-28 + 30-31 + 33-34 + 36-37 | 3 (30/100) | ≤18 / ≥47 | 20 / 10 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Power Bomb | 61 | — | — | — | — | — |

| Back Throw - Brain Buster | 49 | — | — | — | — | — |

| Up Throw - Ninja Drop | 79 | — | — | — | — | — |

| Down Throw - Victory Dance | 87 | — | 10-11 [red]; 14-15 [red]; 18-19 [red]; 22-23 [red]; 26-27 [red]; 30-31 [red]; 34-35 [red]; 38-39 [red]; 42-43 [red]; 46-47 [red]; 56 [green] | 0 (0/100); 1 (10/100) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 79 | — | 17-60 | 19 | — | — |

| Side B | 59 | — | 22-28 | 23 | — | — |

| Up B | 34 | — | 2-26 | 6 | — | — |

| Down B | 78 | — | 30-47 | 18 | — | — |

---

## Link

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Slash | 24 | 20 | 6-8 | 5 (10/60) | — | — |
| Jab 2 - Counter Slash | 21 | 17 | 6-7 | 3 (10/60) | — | — |
| Jab 3 - Stab | 49 | 32 | 6-10 | 6 (10/100) | — | — |
| Rapidjabs Start | 7 | — | — | — | — | — |
| Rapidjabs Loop | 34 | — | 2-3 | 1 (15/40); 1 (15/50); 1 (15/60) | — | — |
| Rapidjabs End | 10 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 39 | — | 16-19 | 14 (5/90); 15 (5/90); 14 (2/90); 13 (2/90) | — | — |

| Up Tilt - Half-Moon Swipe | 29 | — | 9-15 | 9 (30/122); 9 (30/130); 9 (30/124); 9 (30/123) | — | — |

| Down Tilt - Grass Cutter | 39 | 32 | 14-16 | 11 (80/50); 11 (80/50) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Running Hack | 53 | 40 | 7-12 | 9 (10/100); 12 (10/100); 11 (10/100); 11 (10/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 49 | — | 15-18 | 13 (30/83); 14 (30/85); 14 (30/85) | — | — |

| Up Smash - Triple Sword Swipe | 60 | 52 | 11-15 [red]; 26-28 [blue]; 41-43 [magenta] | 4 (40/100); 2 (0/100); 4 (40/100); 4 (40/100); 2 (0/100); 2 (0/100) | — | — |

| Down Smash - Sword Sweep | 49 | 42 | 9-11 [red]; 21-23 [blue] | 13 (26/90); 11 (20/90); 16 (26/90); 17 (26/90); 16 (20/90); 17 (20/90) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Hylian Kick | 39 | 36 | 4-5 | 9 (15/100); 11 (15/100); 8 (15/100); 7 (10/100) | ≤4 / ≥31 | 15 / 7 |

| Forward Air - Spinning Sword | 55 | — | 14-16 [red]; 30-33 [green] | 13 (5/100); 8 (0/90) | ≤1 / ≥50 | 15 / 7 |

| Back Air - Double Kick | 39 | 30 | 6-9 [red]; 18-23 [green] | 7 (0/100); 7 (15/100) | ≤1 / ≥28 | 15 / 7 |

| Up Air - Stab-Up | 69 | 60 | 5-49 | 16 (25/85) | ≤5 / ≥55 | 30 / 15 |

| Down Air - Sword Plant | 89 | 80 | 13-64 | 22 (50/80); 20 (40/80) | ≤13 / ≥64 | 50 / 25 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Kick Out | 39 | — | 12-15 | 3 (0/0) | — | — |

| Back Throw - Reverse Kick Out | 39 | — | 11-15 | 3 (0/0) | — | — |

| Up Throw - Sword Launch | 49 | — | 26 | 4 (50/100) | — | — |

| Down Throw - Flying Elbow | 49 | — | 22-23 | 2 (0/0) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 42 | — | 18-78 | 18 | — | — |

| Side B | 45 | — | 27-168 | 16 | — | — |

| Up B | 79 | — | 8-41 | 15 | — | — |

| Down B | 316 | — | 39-316 | 4 | — | — |

---

## Luigi

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Jab | 15 | — | 2-3 | 3 (0/100); 3 (0/100) | — | — |
| Jab 2 - Right Jab | 17 | — | 2-3 | 2 (0/100); 2 (0/100) | — | — |
| Jab 3 - Plumber's Rump | 29 | 22 | 4-5 | 5 (10/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 32 | 32 | 4-8 | 10 (2/100) | — | — |
| Angled Mid | 32 | 32 | 4-8 | 10 (2/100) | — | — |
| Angled Down | 32 | 32 | 4-8 | 10 (2/100) | — | — |

| Up Tilt - Cat Punch | 29 | — | 4-12 | 9 (30/127); 9 (30/125); 9 (30/123) | — | — |

| Down Tilt - Heel Kick | 34 | — | 5-8 | 9 (10/80) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Fists of Fear | 63 | 59 | 4 + 10 + 16 + 22 + 29 + 37 | 2 (2/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 41 | — | 12-13 | 14 (20/135) | — | — |
| Angled Mid | 41 | — | 12-13 | 13 (20/135) | — | — |
| Angled Down | 41 | — | 12-13 | 12 (20/135) | — | — |

| Up Smash - Lead Headbutt | 39 | — | 9-11 | 17 (35/98) | — | — |

| Down Smash - Breakdance Sweep | 37 | — | 5-6 + 14-15 | 17 (40/80) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Plumber's Boot | 45 | — | 3-6 [red]; 7-31 [green] | 15 (20/100); 8 (20/100) | ≤3 / ≥35 | 15 / 7 |

| Forward Air - Chop Chop | 34 | 33 | 7-10 | 12 (43/100) | ≤2 / ≥19 | 25 / 12 |

| Back Air - Drop Kick | 28 | — | 6-17 | 11 (12/100) | ≤6 / ≥18 | 15 / 7 |

| Up Air - Bicycle Kick | 33 | 30 | 5-7 | 13 (0/100) | ≤2 / ≥15 | 15 / 7 |

| Down Air - Screwdriver Kick | 32 | 29 | 10-14 | 16 (20/100); 16 (20/100) | ≤6 / ≥23 | 18 / 9 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Heave-Ho | 27 | — | — | — | — | — |

| Back Throw - Airplane Swing | 66 | — | — | — | — | — |

| Up Throw - Luigi Launch | 39 | — | — | — | — | — |

| Down Throw - Down the Drain | 39 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 46 | — | 17-67 | 6 | — | — |

| Side B | 92 | — | 23-62 | 25 | — | — |

| Up B | 39 | — | 5-23 | 25 | — | — |

| Down B | 79 | — | 6-43 | 12 | — | — |

---

## Mario

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Jab | 15 | — | 2-3 | 3 (0/100); 3 (0/100) | — | — |
| Jab 2 - Right Cross | 17 | — | 2-3 | 2 (0/100); 2 (0/100) | — | — |
| Jab 3 - Toe Kick | 21 | — | 4-8 | 5 (10/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 32 | — | 5-7 | 10 (6/100) | — | — |
| Angled Mid | 32 | — | 5-7 | 9 (6/100) | — | — |
| Angled Down | 32 | — | 5-7 | 8 (6/100) | — | — |

| Up Tilt - Uppercut | 30 | 30 | 4-12 | 8 (26/125); 8 (26/122); 8 (26/120) | — | — |

| Down Tilt - Leg Sweep | 34 | — | 5-8 | 8 (10/80); 9 (10/80) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Slide | 48 | 38 | 6-9 [red]; 10-25 [green] | 9 (70/50); 7 (45/30) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 41 | — | 12-16 | 19 (25/95); 15 (25/96) | — | — |
| Angled Mid | 41 | — | 12-16 | 18 (25/95); 11 (25/96); 10 (25/96) | — | — |
| Angled Down | 41 | — | 12-16 | 17 (25/95); 13 (25/96) | — | — |

| Up Smash - Lead Headbutt | 39 | — | 9-11 | 15 (32/97) | — | — |

| Down Smash - Breakdance Sweep | 37 | — | 5-6 [red]; 14 [green] | 16 (40/75); 10 (35/75); 12 (35/75) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Plumber's Boot | 45 | — | 3-6 [red]; 7-32 [green] | 12 (20/100); 8 (15/100) | ≤3 / ≥35 | 16 / 8 |

| Forward Air - Plunger | 74 | 60 | 18-22 | 15 (30/70) | ≤3 / ≥42 | 21 / 10 |

| Back Air - Drop Kick | 28 | — | 6-8 [red]; 9-17 [green] | 11 (10/100); 9 (7/100) | ≤6 / ≥18 | 15 / 7 |

| Up Air - Bicycle Kick | 33 | 30 | 4-9 | 11 (0/100) | ≤2 / ≥15 | 15 / 7 |

| Down Air - Drill Kick | 38 | 38 | 10-11 + 13-14 + 16-17 + 19-20 + 22-23 + 25-26 + 28-29 + 31-32 | 2 (0/100) | ≤6 / ≥35 | 23 / 11 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Heave-Ho | 27 | — | — | — | — | — |

| Back Throw - Airplane Swing | 66 | — | — | — | — | — |

| Up Throw - Mario Launch | 39 | — | — | — | — | — |

| Down Throw - Down the Drain | 39 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 43 | — | 14-89 | 6 | — | — |

| Side B | 35 | — | 12-14 | 10 | — | — |

| Up B | 37 | — | 3-24 | 5 | — | — |

| Down B | 79 | — | 8-39 | 3 | — | — |

---

## Marth

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Slash | 27 | 26 | 4-7 | 4 (20/50); 6 (30/60) | — | — |
| Jab 2 - Counter Slash | 27 | 26 | 4-8 | 4 (20/50); 6 (30/60) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 35 | — | 7-10 | 9 (30/70); 13 (60/70) | — | — |

| Up Tilt - Anti-Air Slash | 39 | 32 | 6-8 [red]; 9-12 [orange] | 9 (40/120); 10 (40/120); 9 (40/118); 8 (40/116); 12 (50/100); 9 (30/118) | — | — |

| Down Tilt - Low Stab | 49 | 20 | 7-9 | 9 (40/40); 8 (25/40); 8 (20/40); 10 (50/40) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Raid Chop | 49 | 40 | 12-15 | 11 (70/55); 9 (35/60); 12 (70/55) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 49 | 48 | 10-13 | 14 (60/70); 20 (80/70) | — | — |

| Up Smash - Justice Sword | 54 | 46 | 13-16 | 8 (0/100); 15 (30/80); 18 (60/80) | — | — |

| Down Smash - Whirlwind Blade | 64 | 62 | 5-7 [red]; 20-22 [red] | 11 (70/72); 11 (20/100); 11 (16/100); 16 (70/100); 11 (30/100); 11 (15/100) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Double Slash | 49 | — | 6-7 [red]; 15-21 [yellow] | 4 (30/40); 10 (50/80); 4 (30/40); 10 (50/80) | ≤6 / ≥24 | 15 / 7 |

| Forward Air - Aerial Swipe | 33 | 30 | 4-7 | 10 (30/70); 9 (20/70); 13 (42/70) | ≤1 / ≥26 | 15 / 7 |

| Back Air - About Face | 39 | 35 | 7-11 | 10 (30/70); 9 (25/70); 9 (10/70); 13 (30/70) | ≤1 / ≥31 | 24 / 12 |

| Up Air - Luna Slash | 45 | — | 5-8 | 10 (30/70); 9 (20/70); 9 (18/70); 13 (40/70) | ≤5 / ≥26 | 15 / 7 |

| Down Air - Half Moon | 59 | — | 6-9 | 10 (40/70); 9 (30/70); 9 (20/70); 13 (40/70) | ≤6 / ≥47 | 32 / 16 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Bounce | 31 | — | — | — | — | — |

| Back Throw - Throw Away | 44 | — | — | — | — | — |

| Up Throw - Emblem Toss | 44 | — | — | — | — | — |

| Down Throw - Slam | 42 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Start | 11 | — | — | — | — | — |
| Charge | 29 | — | 19-20 | 7 (30/100); 7 (30/100); 7 (34/100); 7 (40/100) | — | — |
| End | 33 | — | 5-10 | 7 (30/100); 7 (30/100); 7 (34/100); 7 (40/100) | — | — |
| End (Fully Charged) | 33 | — | 5-10 | 28 (30/100); 28 (34/100); 28 (40/100) | — | — |
| Start (Air) | 11 | — | — | — | — | — |
| Charge (Air) | 29 | — | 11-12 | 7 (30/100); 7 (30/100); 7 (34/100); 7 (40/100) | — | — |
| End (Air) | 33 | — | 5-10 [red]; 30-31 [orange] | 7 (30/100); 28 (30/100); 7 (30/100); 7 (34/100); 7 (40/100); 28 (34/100) | — | — |
| End (Fully Charged, Air) | 33 | — | 5-10 [red]; 30-31 [blue] | 28 (30/100); 4 (55/25); 28 (34/100); 28 (40/100); 4 (55/25); 4 (55/25) | — | — |

| 1st | 29 | — | 6-8 | 4 (55/25); 4 (55/25); 4 (55/25); 4 (55/25) | — | — |
| 2nd, Up | 39 | — | 11-14 | 5 (30/40); 5 (60/40); 5 (70/40); 5 (85/40) | — | — |
| 2nd, Neutral/Side/Down | 39 | — | 13-15 | 5 (16/100); 5 (16/100); 5 (16/100); 5 (16/100) | — | — |
| 3rd, Up | 45 | — | 12-16 | 6 (60/60) | — | — |
| 3rd, Neutral/Side | 45 | — | 10-13 | 10 (0/160) | — | — |
| 3rd, Down | 45 | — | 14-17 | 12 (50/100) | — | — |
| 4th, Up | 49 | — | 19-24 | 10 (40/130) | — | — |
| 4th, Neutral/Side | 49 | — | 22-25 | 14 (15/120) | — | — |
| 4th, Down | 59 | — | 12-14 [red]; 18-20 [red]; 24-26 [red]; 30-32 [red]; 36-38 [red]; 43 [yellow] | 3 (2/40); 5 (20/130); 3 (2/40); 5 (20/130) | — | — |
| 1st (Air) | 29 | — | 6-8 | 4 (55/25); 4 (55/25); 4 (55/25); 4 (55/25); 5 (30/40); 5 (60/40) | — | — |
| 2nd, Up (Air) | 39 | — | 11-14 [red]; 34 [orange] | 5 (30/40); 5 (16/100); 5 (60/40); 5 (70/40); 5 (85/40); 5 (16/100) | — | — |
| 2nd, Neutral/Side/Down (Air) | 39 | — | 13-15 [red]; 35 [orange] | 5 (16/100); 6 (60/60); 5 (16/100); 5 (16/100); 5 (16/100) | — | — |
| 3rd, Up (Air) | 45 | — | 12-16 [red]; 40 [green] | 6 (60/60); 10 (0/160) | — | — |
| 3rd, Neutral/Side (Air) | 45 | — | 10-13 [red]; 39-40 [green] | 10 (0/160); 12 (50/100); 10 (40/130) | — | — |
| 3rd, Down (Air) | 45 | — | 14-17 [red]; 37 [green]; 41 [yellow] | 12 (50/100); 10 (40/130); 14 (15/120) | — | — |
| 4th, Up (Air) | 49 | — | 19-24 [red]; 28 [green] | 10 (40/130); 14 (15/120) | — | — |
| 4th, Neutral/Side (Air) | 49 | — | 22-25 | 14 (15/120) | — | — |
| 4th, Down (Air) | 59 | — | 12-14 [red]; 18-20 [red]; 24-26 [red]; 30-32 [red]; 36-38 [red]; 43 [yellow]; 53 [orange] | 3 (2/40); 5 (20/130); 13 (80/70); 3 (2/40); 5 (20/130); 10 (60/70) | — | — |

| Ground | 39 | — | 5 [red]; 6-11 [yellow] | 13 (80/70); 7 (20/90); 10 (60/70); 7 (20/90); 6 (20/90) | — | — |
| Air | 39 | — | 5 [red]; 6-11 [yellow] | 13 (80/70); 7 (20/90); 10 (60/70); 7 (20/90); 6 (20/90) | — | — |

| Ground (No Hit) | 59 | — | — | — | — | — |
| Ground (Hit) | 35 | — | 3-9 | 7 (90/35) | — | — |
| Air (No Hit) | 59 | — | 32-33 | 7 (90/35) | — | — |
| Air (Hit) | 35 | — | 3-9 | 7 (90/35) | — | — |

---

## Mewtwo

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Dark Flash | 29 | 26 | 8 | 6 (0/100); 6 (0/100) | — | — |
| Rapidjabs Start | 7 | — | — | — | — | — |
| Rapidjabs Loop | 50 | — | 7 + 14 + 21 + 28 + 35 + 42 + 49 | 2 (0/70); 2 (0/70) | — | — |
| Rapidjabs End | 13 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 31 | 29 | 6-8 | 10 (20/100); 8 (10/100); 5 (0/100) | — | — |
| Angled Mid | 31 | 29 | 6-8 | 10 (20/100); 8 (10/100); 5 (0/100) | — | — |
| Angled Down | 31 | 29 | 6-8 | 10 (20/100); 8 (10/100); 5 (0/100) | — | — |

| Up Tilt - Flip | 31 | 28 | 6 [red]; 7-11 [orange] | 10 (0/115); 10 (60/114); 8 (70/114); 6 (70/114); 5 (0/80); 8 (60/114) | — | — |

| Down Tilt - Tail Sweep | 29 | 20 | 5-7 | 9 (40/80); 8 (40/80); 5 (40/80) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Dark Torch | 49 | 38 | 10-14 [red]; 15-29 [yellow] | 9 (80/60); 6 (40/60); 9 (80/60); 6 (40/60) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 59 | 52 | 18-19 | 12 (30/80); 20 (21/75) | — | — |

| Up Smash - Galaxy Force | 79 | 70 | 9-10 [red]; 13-14 [red]; 17-18 [red]; 21-22 [red]; 25-26 [red]; 29-30 [red]; 33-34 [red]; 37-38 [blue] | 1 (0/100); 10 (40/118); 1 (0/100); 1 (0/100); 10 (40/118) | — | — |

| Down Smash - Shadow Bomb | 59 | 38 | 20-21 | 15 (20/103); 15 (20/103) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Body Spark | 54 | 45 | 5-6 [red]; 9-10 [red]; 13-14 [red]; 17-18 [red]; 21-22 [red]; 25-26 [red]; 29-30 [red]; 33-34 [red]; 37-38 [red]; 41-42 [yellow] | 2 (20/100); 4 (70/80); 1 (20/100) | ≤5 / ≥43 | 26 / 13 |

| Forward Air - Shadow Scratch | 39 | 36 | 5-7 | 14 (40/100) | ≤1 / ≥34 | 25 / 12 |

| Back Air - Tail Flick | 31 | — | 12-15 | 13 (0/100); 11 (0/100); 9 (0/100) | ≤3 / ≥29 | 28 / 14 |

| Up Air - Somersault Kick | 37 | 35 | 9-11 | 14 (0/100); 12 (0/100); 10 (0/100) | ≤4 / ≥32 | 20 / 10 |

| Down Air - Meteor Kick | 57 | 47 | 18-21 | 16 (10/100); 15 (10/100); 14 (10/100) | ≤6 / ≥44 | 28 / 14 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Shadow Cannon | 74 | — | — | — | — | — |

| Back Throw - Telekinesis | 49 | — | — | — | — | — |

| Up Throw - Psychic Whirlwind | 69 | — | — | — | — | — |

| Down Throw - Tail Slap | 49 | — | 15-24 | 5 (40/104); 5 (40/105) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 26 | — | 17 | 25 | — | — |

| Side B | 55 | — | 12-15 | 10 | — | — |

| Up B | 32 | — | 7-17 | 0 | — | — |

| Down B | 41 | — | 15-21 | 1 | — | — |

---

## Ness

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Hook | 19 | — | 3-4 | 3 (8/50) | — | — |
| Jab 2 - Straight | 19 | — | 3-4 | 2 (8/50) | — | — |
| Jab 3 - Kick | 29 | — | 6-9 | 4 (16/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 34 | — | 7-11 | 12 (12/100) | — | — |
| Angled Mid | 34 | — | 7-11 | 11 (12/100) | — | — |
| Angled Down | 34 | — | 7-11 | 10 (12/100) | — | — |

| Up Tilt - Push Up | 39 | 32 | 5-9 | 7 (42/126) | — | — |

| Down Tilt - Squat Kick | 13 | — | 3-5 | 3 (4/50) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - PK Shove | 41 | 40 | 8 [red]; 15 [blue]; 22 [orange] | 5 (60/70); 4 (0/70); 4 (70/100); 5 (60/70); 5 (18/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 49 | — | 16-17 | 18 (50/62); 20 (50/62); 22 (50/62); 24 (50/62) | — | — |

| Up Smash - Around the World | 49 | 49 | 12-14 [red]; 15-31 [green] | 9 (80/80); 6 (60/45) | — | — |

| Down Smash - Walk the Dog | 61 | 59 | 12 [red]; 13-31 [green] | 11 (70/80); 7 (70/60); 4 (20/50) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Ness Spin | 39 | 36 | 5-12 [red]; 13-23 [green] | 11 (15/100); 8 (0/100) | ≤5 / ≥26 | 22 / 11 |

| Forward Air - Flying PK Shove | 41 | 40 | 8-10 [red]; 11-13 [red]; 14-16 [red]; 17-19 [red]; 20-22 [red]; 23-24 [yellow] | 3 (16/100); 5 (24/135); 2 (16/100) | ≤8 / ≥29 | 18 / 9 |

| Back Air - PK Drop Kick | 39 | 36 | 10-11 [red]; 12-19 [green] | 16 (16/100); 10 (0/100) | ≤10 / ≥24 | 18 / 9 |

| Up Air - Jumping Headbutt | 45 | 42 | 8-11 | 13 (13/109) | ≤8 / ≥26 | 18 / 9 |

| Down Air - Meteor Kick | 59 | — | 20-28 | 12 (90/70) | ≤20 / ≥28 | 28 / 14 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - PK Throw | 52 | — | — | — | — | — |

| Back Throw - Reverse PK Throw | 52 | — | — | — | — | — |

| Up Throw - Cowboy PK Throw | 55 | — | — | — | — | — |

| Down Throw - PK Inferno | 49 | — | 10-33 | 1 (0/100) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 109 | — | 64-77 | 36 | — | — |

| Side B | 69 | — | 20-40 | 2 | — | — |

| Up B | 193 | — | 19-139 | 25 | — | — |

| Down B | 59 | — | 10-39 | 0 | — | — |

---

## Peach

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Royal Slap | 19 | 16 | 2-3 | 3 (0/100); 3 (0/100) | — | — |
| Jab 2 - Double Royal Slap | 19 | 16 | 2-3 | 2 (30/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 41 | 37 | 6-7 [red]; 8-13 [blue] | 13 (35/85); 6 (55/50); 11 (35/85); 10 (35/85) | — | — |

| Up Tilt - Crown Bash | 39 | 37 | 9-13 | 12 (48/72) | — | — |

| Down Tilt - Elegant Sweep | 27 | 26 | 12-13 | 12 (60/100); 12 (15/100); 12 (60/100) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Lady Push | 37 | 36 | 6-8 [red]; 9-20 [yellow] | 12 (70/70); 8 (20/70); 9 (50/70); 7 (20/70) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 47 | — | — | — | — | — |

| Up Smash - Pirouette | 44 | — | 13-22 | 19 (40/100); 15 (40/100); 8 (30/100) | — | — |

| Down Smash - Double-Edged Gown | 39 | — | 5-6 + 9-10 + 13-14 + 17-18 + 21-22 + 25-26 | 14 (40/80); 12 (35/80) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Princess Twirl | 49 | 42 | 3-6 [red]; 7-23 [green] | 14 (20/100); 10 (0/100); 9 (0/100) | ≤3 / ≥35 | 17 / 8 |

| Forward Air - Crown Smack | 54 | 51 | 16-20 | 15 (60/70) | ≤16 / ≥38 | 25 / 12 |

| Back Air - Flying Hip | 44 | 38 | 6-9 [red]; 10-22 [green] | 14 (0/100); 10 (10/90) | ≤6 / ≥22 | 15 / 7 |

| Up Air - Floating High Kick | 35 | 34 | 7-11 | 14 (0/120); 12 (0/120); 11 (0/120) | ≤7 / ≥21 | 15 / 7 |

| Down Air - Stiletto Kick | 39 | — | 12-13 + 18-19 + 24-25 + 30-31 + 36-37 | 3 (16/50); 3 (12/50) | ≤12 / ≥39 | 15 / 7 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Royal Slap | 33 | — | 14 | 2 (10/200) | — | — |

| Back Throw - Iron Hip | 49 | — | 20 | 2 (10/200) | — | — |

| Up Throw - Gut Punch | 49 | — | 20-24 | 2 (0/100) | — | — |

| Down Throw - The Royal Treatment | 64 | — | 34-42 | 0 (0/100) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 64 | — | 10-30 | 3 | — | — |

| Side B | 46 | — | 21-31 | 18 | — | — |

| Up B | 40 | — | 6-29 | 5 | — | — |

| Down B | 29 | — | 29 | 0 | — | — |

---

## Pichu

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Headbutt | 21 | — | 2-3 | 2 (7/50); 2 (7/50) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 29 | — | 5-14 | 9 (10/100) | — | — |
| Angled Mid | 29 | — | 5-14 | 8 (10/100) | — | — |
| Angled Down | 29 | — | 5-14 | 7 (10/100) | — | — |

| Up Tilt - Tail Smack | 23 | — | 7-14 | 6 (20/120); 6 (25/120) | — | — |

| Down Tilt - Tail Sweep | 21 | 19 | 7-9 | 7 (12/100) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Running Headbutt | 49 | — | 5-16 | 8 (40/70) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 49 | — | 16-18 [red]; 19-21 [red]; 22-24 [red]; 25-27 [red]; 28-30 [red]; 31-33 [red]; 34-36 [blue] | 2 (10/50); 6 (90/140); 2 (10/50); 2 (10/50) | — | — |

| Up Smash - Jumping Headbutt | 43 | 41 | 9-11 | 16 (50/105); 16 (40/105) | — | — |

| Down Smash - Spinning Mouse | 54 | 51 | 7-13 | 13 (30/70) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Pichu Roll | 39 | — | 3-10 [red]; 11-28 [green] | 12 (18/100); 9 (0/100) | ≤3 / ≥34 | 12 / 6 |

| Forward Air - Electric Drill | 39 | — | 10-12 + 14-16 + 18-20 + 22-24 + 26-28 | 2 (0/100) | ≤10 / ≥37 | 15 / 7 |

| Back Air - Glider | 59 | — | 4-37 | 9 (20/100) | ≤4 / ≥49 | 18 / 9 |

| Up Air - Tail Chop | 27 | — | 4-9 | 4 (100/60) | ≤4 / ≥17 | 18 / 9 |

| Down Air - Electric Screw | 57 | 48 | 14-26 | 12 (20/100) | ≤1 / ≥38 | 26 / 13 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Electrocution | 43 | — | 10-29 | 2 (0/100) | — | — |

| Back Throw - Submission | 49 | — | — | — | — | — |

| Up Throw - Electric Skull | 43 | — | 14-19 | 5 (0/0) | — | — |

| Down Throw - Electric Slam | 47 | — | 12-19 | 5 (0/0) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 57 | — | 18-117 | 7 | — | — |

| Side B | 70 | — | 5-40 | 39 | — | — |

| Up B | 158 | — | 13-71 | 0 | — | — |

| Down B | 109 | — | 20-49 | 13 | — | — |

---

## Pikachu

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Headbutt | 21 | — | 2-3 | 2 (7/50); 2 (7/50) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 29 | — | 5-14 | 9 (10/100) | — | — |
| Angled Mid | 29 | — | 5-14 | 8 (10/100) | — | — |
| Angled Down | 29 | — | 5-14 | 7 (10/100) | — | — |

| Up Tilt - Tail Smack | 23 | — | 7-14 | 7 (40/124); 7 (45/124); 6 (45/124) | — | — |

| Down Tilt - Tail Sweep | 21 | 19 | 7-9 | 7 (12/100) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Running Headbutt | 49 | — | 5-16 | 8 (40/70) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 49 | — | 16-18 [red]; 19-21 [green]; 22-23 [blue] | 21 (25/92); 19 (25/95); 18 (22/95); 19 (25/90); 18 (22/90); 18 (22/85) | — | — |

| Up Smash - Jumping Headbutt | 43 | 41 | 8-10 [red]; 11-13 [blue]; 14-17 [orange] | 19 (40/110); 13 (30/110); 7 (5/48); 18 (40/110); 17 (40/100) | — | — |

| Down Smash - Spinning Mouse | 54 | 51 | 7-8 [red]; 10-11 [red]; 13-14 [red]; 16-17 [red]; 19-20 [red]; 22-23 [red]; 25-26 [red]; 28 [yellow] | 2 (70/30); 3 (70/170); 2 (30/30) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Pichu Roll | 39 | — | 3-10 [red]; 11-28 [green] | 12 (18/100); 9 (0/100) | ≤3 / ≥34 | 15 / 7 |

| Forward Air - Electric Drill | 39 | — | 10-12 + 14-16 + 18-20 + 22-24 + 26-28 | 2 (0/100) | ≤10 / ≥37 | 20 / 10 |

| Back Air - Glider | 59 | — | 4-7 [red]; 8-37 [green] | 12 (20/100); 9 (20/100) | ≤4 / ≥49 | 30 / 15 |

| Up Air - Tail Chop | 27 | — | 3-4 [red]; 5-6 [green]; 7-8 [yellow] | 4 (100/60); 4 (60/60); 4 (80/60) | ≤3 / ≥17 | 26 / 13 |

| Down Air - Electric Screw | 57 | 48 | 14-26 | 12 (20/100) | ≤1 / ≥38 | 40 / 20 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Electrocution | 43 | — | 10-29 | 2 (0/100) | — | — |

| Back Throw - Submission | 49 | — | — | — | — | — |

| Up Throw - Electric Skull | 43 | — | 14-19 | 5 (0/0) | — | — |

| Down Throw - Electric Slam | 47 | — | 12-19 | 5 (0/0) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 57 | — | 18-117 | 7 | — | — |

| Side B | 70 | — | 5-40 | 29 | — | — |

| Up B | 149 | — | — | 3 | — | — |

| Down B | 109 | — | 20-49 | 15 | — | — |

---

## Roy

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Slash | 31 | 26 | 4-7 | 5 (30/60); 6 (30/60); 3 (5/60) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 40 | 40 | 9-13 | 10 (60/70); 12 (60/70); 7 (30/70) | — | — |

| Up Tilt - Anti-Air Slash | 45 | 40 | 7-9 [red]; 10-13 [blue] | 8 (35/120); 6 (20/100); 9 (35/118); 10 (35/116); 8 (35/120); 9 (35/118) | — | — |

| Down Tilt - Low Stab | 57 | 20 | 8-10 | 10 (90/40); 12 (90/40); 6 (70/40) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Raid Chop | 57 | 40 | 12-15 | 12 (70/55); 6 (35/60) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 57 | 54 | 12-14 | 20 (80/65); 12 (30/65) | — | — |

| Up Smash - Flame Sword | 62 | 46 | 15-16 [red]; 17-18 [red]; 19-20 [red]; 21-22 [red]; 23-24 [red]; 25-26 [yellow] | 2 (0/100); 10 (73/90); 2 (0/100) | — | — |

| Down Smash - Whirlwind Blade | 74 | 72 | 6-8 [red]; 24-26 [yellow] | 21 (42/70); 16 (42/68); 14 (15/100); 8 (15/100) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Double Slash | 57 | 50 | 7-8 [red]; 17-20 [yellow] | 4 (30/40); 8 (50/80); 4 (30/40); 6 (50/80) | ≤7 / ≥31 | 20 / 10 |

| Forward Air - Aerial Swipe | 38 | 35 | 5-7 | 8 (30/70); 5 (10/70) | ≤1 / ≥29 | 20 / 10 |

| Back Air - About Face | 45 | 43 | 8-10 | 9 (30/70); 6 (10/70) | ≤1 / ≥33 | 24 / 12 |

| Up Air - Luna Slash | 52 | 49 | 5-10 | 9 (20/70); 6 (10/70) | ≤5 / ≥29 | 18 / 9 |

| Down Air - Half Moon | 68 | 64 | 7-10 | 9 (40/70); 9 (40/70); 6 (40/70) | ≤7 / ≥54 | 32 / 16 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Bounce | 31 | — | — | — | — | — |

| Back Throw - Throw Away | 44 | — | — | — | — | — |

| Up Throw - Emblem Toss | 44 | — | — | — | — | — |

| Down Throw - Slam | 42 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 45 | — | 16-21 | 50 | — | — |

| Side B | — | — | — | — | — | — |

| Up B | 48 | — | 9-21 | 5 | — | — |

| Down B | 59 | — | 8-20 | 0 | — | — |

---

## Samus

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Straight | 17 | — | 3-4 | 3 (0/100) | — | — |
| Jab 2 - Cannon Hammer | 29 | — | 4-6 | 7 (15/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 31 | 30 | 6-8 | 11 (10/100) | — | — |
| Angled Mid | 31 | 30 | 6-8 | 10 (10/100) | — | — |
| Angled Down | 31 | 30 | 6-8 | 9 (10/100) | — | — |

| Up Tilt - Heel Kick | 39 | 35 | 14-17 | 13 (40/100); 12 (40/100) | — | — |

| Down Tilt - Earth Blaster | 39 | — | 6-8 | 14 (80/60) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Shoulder Attack | 37 | — | 7-9 [red]; 10-16 [green] | 13 (22/105); 9 (22/105) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 43 | — | 10-13 | 15 (30/104) | — | — |
| Angled Mid | 43 | — | 10-13 | 14 (30/104) | — | — |
| Angled Down | 43 | — | 10-13 | 13 (30/104) | — | — |

| Up Smash - Cover Fire | 59 | 58 | 12-14 [red]; 16-18 [green]; 20-22 [yellow]; 24-26 [blue]; 28-29 [orange] | 4 (50/50); 4 (35/50); 4 (25/50); 5 (30/50); 6 (50/120) | — | — |

| Down Smash - Spinning Leg Sweep | 48 | 45 | 6-8 [red]; 14-16 [green] | 16 (110/50); 15 (90/40) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Chozo Kick | 49 | 40 | 5-8 [red]; 9-29 [green] | 14 (10/100); 10 (10/100) | ≤5 / ≥34 | 15 / 7 |

| Forward Air - Aerial Fire | 55 | 50 | 6-7 [red]; 13-14 [red]; 20-21 [red]; 27-28 [red]; 34-35 [red]; 39-40 [green] | 5 (20/100); 5 (20/100) | ≤1 / ≥46 | 15 / 7 |

| Back Air - Flying Back Kick | 39 | 37 | 9-12 | 10 (30/100); 14 (42/100) | ≤9 / ≥30 | 15 / 7 |

| Up Air - Drill Kick | 39 | 39 | 5-6 [red]; 8-9 [yellow]; 11-12 [yellow]; 14-15 [yellow]; 17-18 [yellow]; 20-21 [yellow]; 23-24 [orange] | 3 (0/100); 1 (0/100); 4 (40/120); 3 (0/130); 1 (0/130) | ≤5 / ≥33 | 15 / 7 |

| Down Air - Meteor Cannon | 54 | 49 | 18-22 | 16 (30/100) | ≤3 / ≥33 | 15 / 7 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Beam Throw | 41 | — | — | — | — | — |

| Back Throw - Reverse Beam Throw | 41 | — | — | — | — | — |

| Up Throw - Beam Launch | 41 | — | 13 + 15 + 17 + 19 + 21 + 23 + 25 | 0 (0/100) | — | — |

| Down Throw - Beam Slam | 41 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Start | 13 | — | — | — | — | — |
| Charge | 9 | — | — | — | — | — |
| Cancel | 7 | — | — | — | — | — |
| Shoot | 29 | — | — | — | — | — |
| Start (Air) | 13 | — | — | — | — | — |
| Shoot (Air) | 29 | — | — | — | — | — |

| Homing Missle | 59 | — | — | — | — | — |
| Super Missile | 49 | — | — | — | — | — |
| Normal Missile (Air) | 59 | — | — | — | — | — |
| Super Missile (Air) | 49 | — | — | — | — | — |

| Ground | 49 | — | 4-5 [red]; 6-7 [yellow]; 8-9 [yellow]; 10-11 [yellow]; 12-13 [yellow]; 14-15 [orange]; 16-17 [orange]; 18-19 [orange]; 20-21 [orange]; 22-23 [orange]; 24-25 [orange]; 26-27 [orange]; 28-29 [orange]; 30-31 [orange]; 32-33 [cyan] | 2 (130/100); 1 (110/100); 1 (70/100); 2 (100/100); 1 (70/100); 1 (40/100) | — | — |
| Air | 47 | — | 4-5 [red]; 6-7 [red]; 8-9 [red]; 10-11 [red]; 12-13 [red]; 14-15 [red]; 16-17 [red]; 18-19 [red]; 20-21 [red]; 22-23 [red]; 24-25 [red]; 26-27 [red]; 28-29 [red]; 30-31 [blue] | 1 (25/0); 1 (0/100); 1 (100/0); 1 (80/0) | — | — |

| Hit by Bomb | 53 | — | — | — | — | — |
| Hit by Bomb (Air) | 53 | — | — | — | — | — |
| Drop Bomb | 53 | — | — | — | — | — |
| Drop Bomb (Air) | 53 | — | — | — | — | — |

---

## Sheik

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Slicing Blade | 17 | 16 | 2-3 | 4 (0/100) | — | — |
| Jab 2 - Cutting Blade | 17 | 16 | 2-4 | 3 (0/100) | — | — |
| Rapidjabs Start | 6 | — | — | — | — | — |
| Rapidjabs Loop | 35 | — | 2-3 + 8-9 + 14-15 + 20-21 + 26-27 + 32-33 | 1 (10/80) | — | — |
| Rapidjabs End | 8 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 29 | 27 | 5-10 | 7 (40/100) | — | — |

| Up Tilt - Bow Form | 33 | 26 | 5-10 [red]; 19-24 [green] | 8 (10/120); 4 (10/140) | — | — |

| Down Tilt - Crouching Sweep | 29 | 28 | 5-8 | 8 (35/80); 8 (35/80) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Gale Form | 37 | 36 | 6 [red]; 7-12 [green] | 10 (34/100); 7 (15/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 50 | 46 | 12 [red]; 27-29 [yellow] | 5 (0/100); 10 (50/70); 5 (0/100) | — | — |

| Up Smash - Razor Wing | 47 | 40 | 12 [red]; 14-16 [green] | 17 (50/105); 13 (38/100); 13 (38/100) | — | — |

| Down Smash - Windmill | 49 | 46 | 5-9 [red]; 16-19 [green]; 22-24 [green] | 13 (35/80); 10 (35/80) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Falling Leaves | 48 | 42 | 3-6 [red]; 7-30 [yellow] | 14 (0/100); 9 (0/100); 10 (0/100) | ≤3 / ≥30 | 16 / 8 |

| Forward Air - Hatchet | 33 | — | 5-7 | 13 (0/100) | ≤5 / ≥10 | 16 / 8 |

| Back Air - Flying Swallow | 37 | — | 4-7 [red]; 8-19 [blue] | 8 (0/100); 6 (0/100); 10 (5/100); 14 (12/100); 7 (0/100); 9 (4/100) | ≤4 / ≥24 | 16 / 8 |

| Up Air - Vortex Form | 39 | 37 | 5-7 [red]; 8-20 [green] | 12 (15/120); 9 (10/120) | ≤5 / ≥29 | 24 / 12 |

| Down Air - Butcher Bird | 48 | — | 15-33 | 11 (30/90) | ≤3 / ≥48 | 20 / 10 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Battering Ram | 47 | — | 20-23 | 6 (0/0) | — | — |

| Back Throw - Backlash | 47 | — | 15-19 | 5 (0/0) | — | — |

| Up Throw - Standing Crane | 57 | — | 19-22 | 6 (0/0) | — | — |

| Down Throw - Guillotine | 57 | — | 31-35 | 5 (0/0) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 40 | — | 4-19 | 3 | — | — |

| Side B | 75 | — | 22-65 | 5 | — | — |

| Up B | 94 | — | 36-42 | 12 | — | — |

| Down B | 62 | — | 27-36 | 0 | — | — |

---

## Yoshi

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Left Kick | 17 | — | 3-5 | 3 (8/50) | — | — |
| Jab 2 - Right Kick | 19 | — | 3-5 | 5 (8/120) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 29 | — | 6-8 | 13 (40/80); 13 (40/80); 13 (40/80) | — | — |
| Angled Mid | 29 | — | 6-8 | 12 (40/80); 12 (40/80); 12 (40/80) | — | — |
| Angled Down | 29 | — | 6-8 | 11 (40/80); 11 (40/80); 11 (40/80) | — | — |

| Up Tilt - Tail Snap | 29 | — | 8-12 | 10 (72/40) | — | — |

| Down Tilt - Tail Sweep | 23 | — | 8-10 | 10 (0/100) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Noggin Knock | 43 | 42 | 10-23 | 9 (15/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 47 | 44 | 14-16 | 16 (32/94) | — | — |
| Angled Mid | 47 | 44 | 14-16 | 16 (32/94) | — | — |
| Angled Down | 47 | 44 | 14-16 | 16 (32/94) | — | — |

| Up Smash - Jumping Headbutt | 43 | 40 | 11-15 | 14 (26/108) | — | — |

| Down Smash - Double Tail Whip | 49 | — | 6-8 [red]; 21-22 [yellow] | 14 (50/75); 12 (50/75); 14 (50/60); 12 (50/80) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Yoshi's Kick | 47 | 45 | 3-6 [red]; 7-33 [green] | 14 (15/100); 10 (0/100) | ≤3 / ≥35 | 15 / 7 |

| Forward Air - Noggin Dunk | 49 | 44 | 19-21 | 17 (30/100) | ≤4 / ≥35 | 21 / 10 |

| Back Air - Tail Wag | 39 | 38 | 10-12 [red]; 16-18 [green]; 23-25 [yellow]; 28-30 [blue] | 7 (10/100); 6 (10/100); 5 (10/100); 4 (40/100) | ≤10 / ≥32 | 15 / 7 |

| Up Air - Dino Flip | 39 | 39 | 5-6 | 13 (25/100) | ≤5 / ≥32 | 19 / 9 |

| Down Air - Flutter Kick | 59 | — | 18 + 20 + 22 + 24 + 26 + 28 + 30 + 32 + 34 + 36 + 38 + 40 + 42 + 44 + 46 | 4 (5/90) | ≤16 / ≥59 | 26 / 13 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Spit Out | 39 | — | — | — | — | — |

| Back Throw - Spin 'n' Spit | 43 | — | — | — | — | — |

| Up Throw - Spit Up | 43 | — | — | — | — | — |

| Down Throw - Jump 'n' Spit | 43 | — | — | — | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 39 | — | 17-21 | 7 | — | — |

| Side B | — | — | — | — | — | — |

| Up B | 54 | — | 18-74 | 12 | — | — |

| Down B | 76 | — | 27-52 | 16 | — | — |

---

## Young Link

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Slash | 23 | 20 | 6-8 | 3 (10/60) | — | — |
| Jab 2 - Counter Slash | 21 | 17 | 6-7 | 2 (10/60) | — | — |
| Jab 3 - Stab | 49 | 32 | 6-10 | 5 (10/100) | — | — |
| Rapidjabs Start | 7 | — | — | — | — | — |
| Rapidjabs Loop | 34 | — | 2-3 | 1 (15/40); 1 (15/50); 1 (15/60) | — | — |
| Rapidjabs End | 10 | — | — | — | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 33 | — | 11-13 | 12 (5/100); 11 (5/100); 10 (2/100) | — | — |

| Up Tilt - Half-Moon Swipe | 29 | — | 9-15 | 8 (20/128); 8 (20/126); 8 (20/124); 8 (20/130) | — | — |

| Down Tilt - Grass Cutter | 39 | 32 | 14-16 | 10 (80/50); 9 (80/50); 7 (80/50) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Running Hack | 53 | 40 | 7-12 | 11 (10/100); 10 (10/100) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 49 | — | 15-17 | 10 (0/100) | — | — |

| Up Smash - Triple Sword Swipe | 60 | 52 | 11-14 [red]; 26-28 [blue]; 40-42 [magenta]; 43-44 [pink] | 3 (40/100); 2 (0/100); 3 (40/100); 3 (40/100); 2 (0/100); 2 (0/100) | — | — |

| Down Smash - Sword Sweep | 49 | 42 | 9-11 [red]; 21-23 [blue] | 13 (30/90); 12 (25/90); 13 (30/90); 7 (30/90); 12 (25/70); 6 (25/90) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Hylian Kick | 39 | 36 | 4-5 [red]; 6-27 [green] | 12 (15/100); 8 (10/100) | ≤4 / ≥31 | 15 / 7 |

| Forward Air - Spinning Sword | 55 | 47 | 14-16 [red]; 17-33 [blue] | 12 (5/100); 8 (0/90); 11 (5/100); 9 (5/100); 7 (0/90); 5 (0/90) | ≤1 / ≥46 | 15 / 7 |

| Back Air - Double Kick | 39 | 30 | 6-9 [red]; 18-23 [green] | 7 (0/100); 7 (15/100) | ≤1 / ≥28 | 15 / 7 |

| Up Air - Stab Up | 69 | 60 | 5-49 | 15 (25/85) | ≤5 / ≥55 | 30 / 15 |

| Down Air - Sword Plant | 89 | 80 | 13-64 | 14 (40/100); 16 (70/100) | ≤13 / ≥64 | 50 / 25 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Kick Out | 39 | — | 12-15 | 3 (0/0) | — | — |

| Back Throw - Reverse Kick Out | 39 | — | 11-15 | 3 (0/0) | — | — |

| Up Throw - Sword Launch | 49 | — | 26 | 4 (35/100) | — | — |

| Down Throw - Flying Elbow | 49 | — | 22-23 | 2 (0/0) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 38 | — | 13-43 | 15 | — | — |

| Side B | 45 | — | 27-168 | 7 | — | — |

| Up B | 80 | — | 8-42 | 3 | — | — |

| Down B | — | — | 16-40 | 12 | — | — |

---

## Zelda

### Ground — jabs

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Jab 1 - Short Flash | 29 | 27 | 11 + 13 + 15 + 17 | 2 (10/100) | — | — |

### Ground — tilts

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Up | 37 | 37 | 12-14 | 13 (50/88); 12 (50/88); 11 (50/88) | — | — |
| Angled Mid | 37 | 37 | 12-14 | 13 (50/88); 12 (50/88); 11 (50/88) | — | — |
| Angled Down | 37 | 37 | 12-14 | 13 (50/88); 12 (50/88); 11 (50/88) | — | — |

| Up Tilt - Protective Sweep | 43 | 40 | 10-24 | 11 (65/105) | — | — |

| Down Tilt - Trip | 31 | 30 | 5-7 | 7 (20/80); 7 (20/80); 8 (20/80) | — | — |

### Ground — dash attack

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Dash Attack - Magical Push | 37 | 36 | 6-8 [red]; 9-13 [yellow] | 13 (70/70); 8 (20/70); 9 (50/70); 7 (20/70) | — | — |

### Ground — smashes

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Angled Mid | 39 | — | 16 [red]; 18 [red]; 20 [red]; 22 [red]; 24 [red]; 26 [orange] | 1 (0/100); 14 (50/98); 1 (0/100); 1 (0/100); 1 (0/100) | — | — |

| Up Smash - Power Sweep | 56 | 51 | 5 [red]; 7 [red]; 9 [red]; 11 [red]; 13 [red]; 15 [red]; 17 [red]; 24 [red]; 26 [red]; 28 [red]; 30 [red]; 32 [red]; 34 [red]; 36 [orange] | 1 (0/100); 5 (20/210); 1 (0/100); 1 (0/100); 1 (0/100) | — | — |

| Down Smash - Compass Spin | 39 | 32 | 4-7 [red]; 13-16 [green] | 11 (20/90); 11 (20/80) | — | — |

### Aerials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral Air - Magic Spin | 49 | 42 | 6-7 [red]; 10-11 [red]; 14-15 [red]; 18-19 [red]; 22-23 [red]; 26-27 [red]; 30-31 [blue] | 2 (0/100); 5 (40/130); 3 (0/90); 3 (0/90); 5 (40/120) | ≤6 / ≥37 | 18 / 9 |

| Forward Air - Lightning Kick | 39 | 36 | 8-11 | 10 (0/80); 20 (30/96) | ≤8 / ≥24 | 18 / 9 |

| Back Air - Reverse Lightning Kick | 35 | 33 | 5-8 | 10 (0/80); 20 (30/96) | ≤5 / ≥25 | 18 / 9 |

| Up Air - Condensed Blast | 54 | — | 14-16 | 13 (0/120) | ≤14 / ≥44 | 25 / 12 |

| Down Air - Meteor Heel | 43 | 43 | 14-17 | 8 (5/100); 7 (0/80) | ≤1 / ≥39 | 24 / 12 |

### Throws

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Forward Throw - Levitation | 49 | — | — | — | — | — |

| Back Throw - Reverse Levitation | 49 | — | — | — | — | — |

| Up Throw - Levitation Launch | 49 | — | — | — | — | — |

| Down Throw - Plasma Beat | 64 | — | 24-25 + 30-31 + 36-37 + 42-43 + 48-49 | 2 (0/100) | — | — |

### Specials

| Move | Sub-move | Total | IASA | Active frames (per hitbox) | Damage (BKB/KBS) | AC | Land. lag |
|---|---|---|---|---|---|---|---|
| Neutral B | 59 | — | 12-27 | 5 | — | — |

| Side B | 77 | — | 60-62 | 13 | — | — |

| Up B | 114 | — | 10-11 | 4 | — | — |

| Down B | 69 | — | 34-43 | 0 | — | — |

---
