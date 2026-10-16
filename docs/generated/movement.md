# Movement data sheet — measured from the real sim (issue #150)

> Generated 2026-08-18 10:08 UTC · scripted inputs on the real ServerSimulation (60 Hz tick). 
> Run: hold right from standstill. Dash: one dash press. Jump: full jump (held past the short-hop 
> window). Short hop: press + release inside the window. Double jump: jump edge at first apex. 
> Drift: stick held through the full hop. Fall: spawned airborne at 50 m (float window skipped). 
> Reversal: cruise right, then full opposite input (pivot skid + re-accel). Stop: release at cruise. 
> Stage-relative rows use the real baked <b>colosseum</b> arena (30.6 m wide). 
> Values are measured (effective behavior, incl. rush kick-off, pivot skids, caps), not authored constants.

## Comparison

| metric | Manki | FightGuy | Wibou | Nilus |
|---|---|---|---|
| Run max speed (m/s) | 12.0 (12) | 14.0 (14) | 15.0 (15) | 13.0 (13) |
| Run time-to-max | instant (rush kick-off) | instant (rush kick-off) | instant (rush kick-off) | instant (rush kick-off) |
| Run cross-stage time (s) | 2.55 | 2.19 | 2.04 | 2.35 |
| Dash duration (ticks) | 15 (15) | 20 (20) | 16 (16) | 15 (15) |
| Dash distance (m) | 4.67 | 6.33 | 6.00 | 4.90 |
| Dash % of stage | 15% | 21% | 20% | 16% |
| Dash+stop commit % of stage | 21% | 28% | 29% | 23% |
| Dash actionable (tick) | 15 | 20 | 16 | 15 |
| Jump squat (ticks) | 6 | 4 | 4 | 5 |
| Dash-dance window (ticks) | 10 (rush, on standstill / redirect) | 10 (rush, on standstill / redirect) | 10 (rush, on standstill / redirect) | 10 (rush, on standstill / redirect) |
| Jump apex (m) | 1.35 | 1.90 | 2.24 | 2.02 |
| Jump time-to-apex (ticks) | 16 | 18 | 20 | 20 |
| Jump airtime (s) | 0.53 | 0.63 | 0.68 | 0.67 |
| Full-hop drift (m) | 2.37 | 3.47 | 4.34 | 3.63 |
| Full hop % of stage | 8% | 11% | 14% | 12% |
| Running jump distance (m) | 4.23 | 5.69 | 6.63 | 5.41 |
| Short hop apex (m) | 0.00 (BROKEN — land-tolerance snap: 6 m/s impulse rises 0.09 m/tick &lt; 0.10 m PlatformLandTolerance, sim snaps it back; short hop never leaves the ground) | 0.66 | 0.78 | 0.70 |
| Short hop airtime (s) | — | 0.37 | 0.40 | 0.38 |
| Double-jump apex (m) | 2.19 | 3.09 | 3.65 | 3.29 |
| Double-jump airtime (s) | 0.82 | 0.95 | 1.05 | 1.03 |
| Air drift cap (m/s) | 6.5 (6) | 7.5 (8) | 8.5 (8) | 7.0 (7) |
| Air / run speed | 54% | 54% | 57% | 54% |
| Fall max speed (m/s) | 45 (45) | 48 (48) | 48 (48) | 46 (46) |
| Fast fall (m/s) | 54 (54) | 58 (58) | 58 (58) | 55 (55) |
| Fast fall from jump apex (s) | 0.05 | 0.05 | 0.07 | 0.07 |
| Reversal time (s) | 0.55 | 0.62 | 0.67 | 0.60 |
| Reversal distance (m) | 1.70 | 2.05 | 2.36 | 1.98 |
| Stop time (s) | 0.32 | 0.38 | 0.40 | 0.35 |
| Stop distance (m) | 1.71 | 2.38 | 2.76 | 2.03 |

## Roster read

What the numbers mean, per character (computed from the measured values above):

- **Manki**: run 12 m/s, dash 4.7 m (15% of stage), jump 1.35 m, air/run 54%, stop 1.7 m. Best at: safest stop. Weakest at: longest dash, highest jump, fastest run, longest airtime, largest air drift, largest stage share per dash.
- **FightGuy**: run 14 m/s, dash 6.3 m (21% of stage), jump 1.90 m, air/run 54%, stop 2.4 m. Best at: longest dash, largest stage share per dash. Weakest at: most ground-dominant (lowest air/run).
- **Wibou**: run 15 m/s, dash 6.0 m (20% of stage), jump 2.24 m, air/run 57%, stop 2.8 m. Best at: highest jump, fastest run, longest airtime, largest air drift. Weakest at: safest stop.
- **Nilus**: run 13 m/s, dash 4.9 m (16% of stage), jump 2.02 m, air/run 54%, stop 2.0 m.

- **Too fast?** Run crosses 31 m in Manki 2.55 s / FightGuy 2.19 s / Wibou 2.04 s / Nilus 2.35 s. Full-hop airtime is 0.53 s / 0.63 s / 0.68 s / 0.67 s vs ~0.25 s reaction — reactable but tight (2-3×). Fast-fall from jump apex: 0.05 s / 0.05 s / 0.07 s / 0.07 s — under reaction, so a fast-fall landing cannot be reacted to; reads must come from the jump start, not the landing.
- **Broken short hop: Manki** — the short-hop impulse rises under the 0.10 m PlatformLandTolerance on the first airborne tick (no upward-velocity gate in the non-hitstun ground snap), so the sim snaps the character back down and the hop never leaves the ground. Fix candidates: raise the impulse above ~6.7 m/s, or add the hitstun-branch's `VY <= 0` gate to the snap.


## Melee comparison

Melee values: docs/research/melee-movement-audit.md (SSBWiki \[community\]; derived frame counts transfer 1:1 — 60 f/s = 60 ticks/s). Absolute speeds don't transfer (u/f vs m/s), timings and ratios do.

| metric | SLOPArena (measured) | Melee reference | read |
|---|---|---|---|
| Jump squat | 6 t–4 t–4 t–5 t | 3–8 f (Fox 3, Marth 4, Puff 5, Bowser 8) | in Melee range |
| Full-hop airtime | 0.53 s–0.63 s–0.68 s–0.67 s | Fox ~33 f, Marth 57–59 f | FG/Wibou ≈ Fox-fast, Manki ≈ Marth |
| Short-hop airtime | 0.00 s–0.37 s–0.40 s–0.38 s | Fox ~19 f, Marth 36–38 f | short hop in the fast band |
| Short/full jump force | 0.60–0.60–0.60–0.60 | ≈ 0.58 (derived) | Melee-shaped (0.7 was the pre-audit value) |
| Fast fall / fall | 1.20–1.21–1.21–1.20 | 1.14–1.26 (Fox 3.4/2.8 … Puff 1.6/1.3) | Melee-shaped, adopted (audit §3.4) |
| Air speed / run | 0.54–0.54–0.57–0.54 | 0.38 Fox – 0.5 Marth – 1.23 Puff | upper-mid band — air slower than ground, Melee norm |
| Dash speed / run | 1.56–1.36–1.50–1.51 | 0.8–1.5× (initial dash vs run) | top of Melee band |
| Stop from run | 0.32 s–0.38 s–0.40 s–0.35 s | Fox 27.5 f, Marth 30 f, Puff 12 f | SLOPA brakes faster than Fox/Marth |
| Reversal (cruise→cruise) | 0.55 s–0.62 s–0.67 s–0.60 s | dash-dance pivot ≈ 10–15 f between dashes | SLOPA pivot 2–3× slower — no dash-dance (cooldown) |
| Dash cooldown | 44–60 t | none — dash-dance is core | the big deviation (ADR-0020 kept it) |



## Manki

- **Run**: 12.0 m/s (authored 12) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 15 ticks, 4.67 m = 15%, actionable on tick 15 (hard stop; authored 20 m/s for 15 ticks)
- **Jump**: apex 1.35 m at 16 ticks, airtime 0.53 s, full-hop drift 2.37 m = 8%; running jump carries 4.23 m
- **Short hop**: apex 0.00 m, airtime 0.00 s (authored force 6.0 vs jump 10.0 = ratio 0.60; Melee ~0.58)
- **Double jump**: second apex 2.19 m, total airtime 0.82 s
- **Air drift**: speed cap 6.5 m/s (authored 6.5; 54% of run speed)
- **Fall** (50 m drop): max 45 m/s (authored 45), reached 1.28 s into the drop, descent 1.72 s; fast fall 54 m/s (authored 54), from jump apex 0.05 s (natural 0.53 s full hop)
- **Reversal** (cruise → opposite cruise): 0.55 s, 1.70 m covered (pivot skid + re-accel)
- **Stop** (cruise → standstill): 0.32 s, 1.71 m; dash+stop commit = 21% of stage

## FightGuy

- **Run**: 14.0 m/s (authored 14) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 20 ticks, 6.33 m = 21%, actionable on tick 20 (hard stop; authored 20 m/s for 20 ticks)
- **Jump**: apex 1.90 m at 18 ticks, airtime 0.63 s, full-hop drift 3.47 m = 11%; running jump carries 5.69 m
- **Short hop**: apex 0.66 m, airtime 0.37 s (authored force 7.2 vs jump 12.0 = ratio 0.60; Melee ~0.58)
- **Double jump**: second apex 3.09 m, total airtime 0.95 s
- **Air drift**: speed cap 7.5 m/s (authored 7.5; 54% of run speed)
- **Fall** (50 m drop): max 48 m/s (authored 48), reached 1.32 s into the drop, descent 1.67 s; fast fall 58 m/s (authored 58), from jump apex 0.05 s (natural 0.63 s full hop)
- **Reversal** (cruise → opposite cruise): 0.62 s, 2.05 m covered (pivot skid + re-accel)
- **Stop** (cruise → standstill): 0.38 s, 2.38 m; dash+stop commit = 28% of stage

## Wibou

- **Run**: 15.0 m/s (authored 15) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 16 ticks, 6.00 m = 20%, actionable on tick 16 (hard stop; authored 24 m/s for 16 ticks)
- **Jump**: apex 2.24 m at 20 ticks, airtime 0.68 s, full-hop drift 4.34 m = 14%; running jump carries 6.63 m
- **Short hop**: apex 0.78 m, airtime 0.40 s (authored force 7.8 vs jump 13.0 = ratio 0.60; Melee ~0.58)
- **Double jump**: second apex 3.65 m, total airtime 1.05 s
- **Air drift**: speed cap 8.5 m/s (authored 8.5; 57% of run speed)
- **Fall** (50 m drop): max 48 m/s (authored 48), reached 1.32 s into the drop, descent 1.67 s; fast fall 58 m/s (authored 58), from jump apex 0.07 s (natural 0.68 s full hop)
- **Reversal** (cruise → opposite cruise): 0.67 s, 2.36 m covered (pivot skid + re-accel)
- **Stop** (cruise → standstill): 0.40 s, 2.76 m; dash+stop commit = 29% of stage

## Nilus

- **Run**: 13.0 m/s (authored 13) — instant cruise — Rush kick-off sets RunSpeed on the first tick (no ramp); the soft-start accel only shows after a turnaround skid
- **Dash**: 15 ticks, 4.90 m = 16%, actionable on tick 15 (hard stop; authored 21 m/s for 15 ticks)
- **Jump**: apex 2.02 m at 20 ticks, airtime 0.67 s, full-hop drift 3.63 m = 12%; running jump carries 5.41 m
- **Short hop**: apex 0.70 m, airtime 0.38 s (authored force 7.2 vs jump 12.0 = ratio 0.60; Melee ~0.58)
- **Double jump**: second apex 3.29 m, total airtime 1.03 s
- **Air drift**: speed cap 7.0 m/s (authored 7.0; 54% of run speed)
- **Fall** (50 m drop): max 46 m/s (authored 46), reached 1.35 s into the drop, descent 1.73 s; fast fall 55 m/s (authored 55), from jump apex 0.07 s (natural 0.67 s full hop)
- **Reversal** (cruise → opposite cruise): 0.60 s, 1.98 m covered (pivot skid + re-accel)
- **Stop** (cruise → standstill): 0.35 s, 2.03 m; dash+stop commit = 23% of stage

