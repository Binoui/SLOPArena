# Melee Frame Data — Comparative Analysis

> What 25 characters of Melee frame data mean for SLOPArena's 8-normal / 4-special kit
> rework (FightGuy first). Full per-move tables: [`melee-frame-data.md`](melee-frame-data.md).
> Machine-readable dataset: [`melee-frame-data.json`](melee-frame-data.json).
> All values in **frames = sim ticks** (both 60Hz).

## 1. Roster-wide numbers (primary submove, 25 chars)

| Category | n | Startup min/med/max | Active len min/med/max | Total min/med/max | IASA early-out | IASA present |
|---|---|---|---|---|---|---|
| Jab (first hit) | 25 | 2/3/11 | 1/2/7 | 15/19/31 | -5/-1.5/-1 | 64% |
| Forward Tilt | 25 | 4/6/16 | 3/4/18 | 26/31/44 | -4/-2/0 | 36% |
| Up Tilt | 24 | 4/7/81 | 3/7/20 | 23/30/114 | -7/-2.5/0 | 58% |
| Down Tilt | 25 | 3/7/14 | 2/3/8 | 13/31/57 | -37/-2/0 | 68% |
| Dash Attack | 25 | 4/6/12 | 4/12/35 | 37/43/63 | -17/-3/0 | 80% |
| Forward Smash | 24 | 10/13/22 | 2/5/21 | 39/49/66 | -7/-3.5/-1 | 33% |
| Up Smash | 25 | 5/11/24 | 3/9/34 | 39/49/79 | -16/-8/0 | 68% |
| Down Smash | 25 | 4/7/20 | 2/13/22 | 34/49/74 | -21/-3/-2 | 72% |
| Neutral Air | 25 | 3/5/20 | 2/25/38 | 39/47/79 | -29/-7/-2 | 56% |
| Forward Air | 25 | 4/10/25 | 3/16/40 | 33/44/74 | -14/-4/-1 | 76% |
| Back Air | 25 | 4/6/12 | 3/12/34 | 28/39/59 | -9/-6/-1 | 68% |
| Up Air | 25 | 3/6/14 | 2/6/45 | 27/39/69 | -9/-3/0 | 80% |
| Down Air | 25 | 5/13/20 | 4/19/52 | 32/49/89 | -10/-5.5/0 | 56% |
| Neutral B | 22 | 4/17/70 | 1/26/100 | 26/48/119 | — (specials have none) | 0% |
| Side B | 21 | 5/18/60 | 2/11/142 | 29/59/92 | — (specials have none) | 0% |
| Up B | 24 | 1/8/43 | 2/24.5/121 | 32/64/193 | — (specials have none) | 0% |
| Down B | 23 | 1/15/42 | 1/19/278 | 3/72/316 | — (specials have none) | 0% |

## 2. What the numbers say

### Startup is what defines the tiers

| Tier | Startup (median) | Active (median) | Total (median) |
|---|---|---|---|
| Jab | 3 | 2 | 19 |
| Tilt | 6-7 | 3-7 | 30-31 |
| Smash | 7-13 | 5-13 | 49 |
| Aerial | 5-13 | 6-25 | 39-49 |
| Special | 8-18 | 11-26 | 48-72 |

Bands are ~2x apart — Melee players read startup, not damage, to know what beats what.
A normal's hitbox is a thin slice of its duration: **active/total ≈ 0.10-0.29 on the ground**, up to
0.52 in the air. 70-90% of every grounded move is windup + recovery you can be punished in.

### IASA — the early-out that makes Melee feel fast

- Present on **33-80% of moves** per category (jabs 64%, dash 80%, uair 80%, fair 76%, dsmash 72%).
- Typical early-out is **1-8 frames** before the animation ends: jab 21→16, fsmash 64→60.
- Jabs cancel up to 5 frames early; aerials up to 29 (gated by landing, §5).
- SLOPArena has **no IASA** — `AnimLockTicks` is full commitment. This is the single biggest
  engine gap for Melee feel. A per-stage `IasaTick` (0 = none) reproduces it.

### Roster extremes (the design space)

| Category | Fastest startup | Longest active | Longest total |
|---|---|---|---|
| Jab (first hit) | drmario (2) | zelda (7) | — |
| Forward Tilt | drmario (4) | mrgamenwatch (18) | — |
| Up Tilt | drmario (4) | sheik (20) | — |
| Down Tilt | ness (3) | mrgamenwatch (8) | — |
| Dash Attack | bowser (4) | kirby (35) | — |
| Forward Smash | marth (10) | mrgamenwatch (21) | — |
| Up Smash | zelda (5) | younglink (34) | — |
| Down Smash | zelda (4) | peach (22) | — |
| Neutral Air | drmario (3) | mewtwo (38) | — |
| Forward Air | marth (4) | falco (40) | — |
| Back Air | falco (4) | pichu (34) | — |
| Up Air | pikachu (3) | link (45) | — |
| Down Air | bowser (5) | link (52) | — |
| Neutral B | sheik (4) | falco (100) | — |
| Side B | pichu (5) | link (142) | — |
| Up B | mrgamenwatch (1) | ness (121) | — |
| Down B | falco (1) | link (278) | — |

Outliers worth knowing:
- **Ganondorf Up Tilt**: 81-frame startup, 114 total — the slowest attack in the game, huge payback.
- **Long-active aerials**: Falco/Fox Fair (40 active, 5 windows), Link Uair (45), Link Dair (52, 64-tick sword plant).
- **Mewtwo Nair**: 38 active frames (10 windows, one every 4 frames) — a wall, not an attack.
- **Specials**: Falcon Punch 52/5/99, Ganondorf Neutral B 70/3/119, Ness Up B 19/121/193 (PK Thunder ride),
  Link Side B 27/142/45 (boomerang return).

## 3. Patterns that recur (authoring recipes)

### Softener — strong hitbox up front, weak trailing hitbox

| Move | Strong | Weak |
|---|---|---|
| Falcon Jab 3 | 8% @ 5-8 | 6% @ 9-11 |
| Falcon Dash | 10% @ 7-9 | 7% @ 10-16 |
| Falcon F-Air | 18% @ 14-16 | 6% @ 17-30 |
| Falcon D-Smash | 18% @ 19-22 | 16% @ 29-32 |
| Fox/Falco Dash, Mario/DrMario Dash | strong 4-7 | weak 8-17/25 |

Recipe: **two `HitboxEvent`s in one stage** — strong hitbox with high KB, weak one with low KB,
later TriggerTick. SLOPArena already supports this (per-event Damage/Knockback).

### Gapped multi-hit aerials

| Move | Windows |
|---|---|
| Falcon Nair | 7-12 + 20-29 |
| Marth Nair | 6-7 + 15-21 |
| Roy Nair | 7-8 + 17-20 |
| Falcon F-Air | 14-16 + 17-30 (softener) |

Two separate windows in one aerial = two HitboxEvents with different TriggerTicks. The gap is what
makes the hitbox readable and lets the second hit combo (or whiff).

### Drill kicks — evenly spaced single-frame windows

Bowser/Jigglypuff/Pichu Dair, Fox/Falco Dair, Mario/DrMario Dair, Samus Uair, Pikachu D-Smash:
one hitbox every 3-4 frames for 20-40 frames. Recipe: **multiple HitboxEvents, same offset,
TriggerTicks spaced by the rehit interval** (or a lingering zone hitbox with a rehit pulse).

### Multi-window smashes

| Move | Windows |
|---|---|
| Falcon U-Smash | 21-22 + 27-28 |
| Falcon D-Smash | 19-22 + 29-32 |
| Link U-Smash | 11-15 + 26-28 + 41-43 |
| Zelda F-Smash | 6 windows (16-26, every 2 frames) |

### Specials that hide their hitbox on a later submove

- Charge moves (Marth Shield Breaker, Samus Charge Shot): hitbox only on the `End`/`Fully Charged` submove.
- No hitbox at all: G&W Down B (bucket), Marth Down B (counter), Samus Down B (bomb). SLOPArena's
  `ChargeTicks` + release-to-fire pattern (AerosolFlame, RoundBomb) already models the charge case.

## 4. Specials across the roster

| Special | Startup med | Active med | Total med | Notable |
|---|---|---|---|---|
| Neutral B | 17 | 26 | 48 | projectile/charge moves; Ganon 70/119 |
| Side B | 18 | 11 | 59 | engage/lunge; Link boomerang 142 active |
| Up B | 8 | 25 | 64 | recovery; Ness 193 total, G&W frame-1 hitbox |
| Down B | 15 | 19 | 72 | counters/ground pounds/meteors |

Specials are long, active-dense commitments (act/tot ≈ 0.4-0.5) with **no cooldowns in Melee** —
commitment is the only gate. SLOPArena adds MOBA cooldowns on top; frame data still sets the
commitment profile.

## 5. Aerial commitment model

- **All 125 aerials** have auto-cancel (AC) windows: land on frames ≤ before or ≥ after → no landing lag.
- Landing lag: **12-50 frames (median 19)**; L-cancel halves it (**6-25, median 9**).
- Aerial commitment = air time + landing lag. The active window floats mid-air and the move is
  interruptible (IASA) the rest of the time — Melee's aggression comes from this.
- SLOPArena has no landing-lag or AC concept; aerials currently lock for the whole stage. A
  `LandingLagTicks` on air stages + AC window fields would close the gap.

## 6. Mapping to SLOPArena's 8 normals + 4 specials

Melee-native frames are 1-based; SLOPArena `HitboxEvent.TriggerTick` is 0-based →
**`TriggerTick = meleeFrame − 1`**, `DurationTicks = window length`. `AttackStage.DurationTicks = Total`
(or IASA, once the engine supports early-outs).

### Ground normals

| Slot | Role | Falcon source | TriggerTick | Duration | Stage ticks | KB idea |
|---|---|---|---|---|---|---|
| 1 | low — fast safe poke | Jab 1 | 2 | 3 | 16-19 | low hitbox, 2-3%, tiny BKB |
| 2 | medium — spacing | F-Tilt | 8 | 3-4 | 29 | 10-12%, mid BKB |
| 3 | high — anti-air | U-Tilt (or U-Smash) | 16 | 5 | 39 | 13%, launch angle up |
| 4 | AOE — get-off-me | D-Smash (2-sided) | 18 | 8-14 | 49 | 18-20%, 360°, long active |

### Air normals (same buttons, similar function)

| Slot | Role | Falcon source | TriggerTick | Duration | Stage ticks | Note |
|---|---|---|---|---|---|---|
| air 1 | fast poke | U-Air | 5 | 6 | 33 | juggle tool |
| air 2 | combo | B-Air | 9 | 8 | 35 | 2 hitboxes (14% then 8%) |
| air 3 | spike / kill | D-Air (or F-Air) | 15 | 5 | 44 | meteor; or 18% knee 14-16 |
| air 4 | AOE | N-Air | 6 + 19 | 6 + 10 | 44 | two windows: 7-12 + 20-29 |

### Specials

| Slot | Role | Source profile | TriggerTick | Duration | Stage ticks |
|---|---|---|---|---|---|
| A | projectile | any Neutral B | ~16 | 5-30 | ~48 |
| E | upward mobility / recovery | Up B (Dolphin Slash) | 12 | 21 | 64 |
| R | engage / lunge | Side B (Raptor Boost) | 14 | 20 | 79 |
| F | ult | Down B (Falcon Dive) / F-Smash | 13 | 19 | 64 |

## 7. Engine deltas required (ranked)

1. **`AttackStage.IasaTick`** (0 = none) — early-out on any action. From the data: jabs -5, tilts -1..-4,
   smashes -1..-8, aerials -29 (landing-gated). Biggest feel multiplier.
2. **Aerial landing lag + auto-cancel** — `LandingLagTicks` per air stage; landing inside the AC window
   (frames ≤ before / ≥ after) skips lag entirely. Median lag 19 → L-cancel 9.
3. **Multi-hitbox stages** — already supported (strong+weak softeners, gapped windows, drill kicks all
   author in Ability Lab as multiple HitboxEvents).
4. **Cooldowns on normals = 0** — commitment (AnimLockTicks/IASA) is the gate; save cooldowns for the
   four specials.

## 8. Caveats

- Stats use the primary submove per move (Jab = Jab 1, charge specials = first submove); submove variants
  are in the full tables + JSON.
- Per-hitbox windows/colors come from the shoemaker site's `hitframes_colors` tables; damage values are
  per hitbox row in `hitboxtable`. A handful of tables omit windows for sweetspot variants (they overlap
  a colored window — e.g. Falcon Nair's green/yellow inside 7-12/20-29).
- Melee hitboxes are 2D planes/circles with different semantics than SLOPArena's 3D spheres/capsules —
  **frame timing transfers, geometry doesn't**.
- No IASA for specials in Melee — treated as full commitment here.

---

Related: [`frame-data-reference.md`](frame-data-reference.md) (DKO manual counts),
[`ability-lab.md`](../systems/ability-lab.md) (authoring tool).
