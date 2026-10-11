# Knockback flight comparison — 2026-10-11

Visuals: [interactive playback for the whole roster](knockback-visuals/index.html), [three-move overview](knockback-visuals/overview.png), and [synchronized Double Kick animation](knockback-visuals/before-after.mp4). Orange is before; teal is after. The interactive viewer embeds both recorded datasets and works offline. The weak-hit close-up magnifies height five times; ordinary views preserve equal horizontal/vertical distance scale.

The [permanent viewer tool](../../tools/KnockbackVisuals/README.md) includes the saved comparison data, a launcher, and regeneration instructions. Run `python3 tools/KnockbackVisuals/serve.py` from the repository root; this snapshot remains fixed as gameplay code changes.

Ordinary hits should displace fighters visibly without forcing long overhead tracking. The shared flight model now gives upward launches four airborne ticks of pop grace, then applies 14 m/s² gravity during hitstun as well as after it. Neutral post-hitstun braking falls from 10 to 6 m/s² and reduces horizontal vector magnitude, preserving heading and equal braking for diagonal sends.

The initial launch impulse, damage, hitstop, hitstun formula and authored move angles are unchanged. Ground contact does not end hitstun. Downward sends receive gravity immediately; a stronger downward impulse is never slowed to the character fall-speed limit. `AirTimeTicks` carries the launch clock through existing replication and across hitstun expiry. DI is unchanged.

## Measurements

Both sides use `tools/MoveDataReport` and the real shared simulation with the same current cooked roster. Each move launches a passive mirror victim at ground level on a flat 200 × 200 m arena, with no DI, SDI, follow-up actions or simulated hitstop. Percent is pre-hit; hit damage is added before launch. Each multihit row is an isolated hit, not a complete sequence. Spikes start 3 m above the floor, so their 3 m apex is their initial altitude. Reported horizontal travel ends at landing/actionable grounding; grounded carry after that point is not included.

Baseline `Simulation.cs` SHA-256: `cf5c3a161ab7f1aaaf2a875e191a8eb96108bb0ee4140a22ecb7afd1226a1565`. This is the saved workspace before this change, including pre-existing edits, rather than repository HEAD.

Reproduction of current rows:

```bash
dotnet run --project tools/MoveDataReport -p:SkipUnityPluginCopy=true -- fightguy --pcts 0,60,120,200 --json /tmp/knockback-fightguy.json
# Repeat for manki, wibou and bonk.
```

The tables show baseline → updated values at 120% pre-hit damage.

### FightGuy

| Move / hit | Angle | Maximum rise (m) | Horizontal travel (m) | Time to land (s) |
|---|---:|---:|---:|---:|
| g1 Low Kick | 8° | 0.45 → 0.12 | 4.91 → 3.03 | 0.65 → 0.37 |
| g2 Straight Punch | 25° | 2.90 → 1.04 | 9.54 → 7.36 | 1.43 → 0.77 |
| g3 Sweeping Kick | 55° | 6.07 → 3.00 | 4.22 → 5.34 | 1.95 → 1.32 |
| g4 Double Kick | 28° | 9.41 → 3.19 | 25.95 → 21.03 | 2.60 → 1.35 |
| a1 Double Punch / hit 1 | 55° | 5.74 → 2.87 | 3.99 → 5.06 | 1.90 → 1.28 |
| a1 Double Punch / hit 2 | 45° | 7.64 → 3.43 | 8.80 → 10.33 | 2.23 → 1.40 |
| a2 Floating Kick / hit 1 | 25° | 2.92 → 1.05 | 9.62 → 7.40 | 1.43 → 0.77 |
| a2 Floating Kick / hit 2 | 20° | 0.78 → 0.32 | 3.52 → 2.55 | 0.75 → 0.42 |
| a3 High Kick | 65° | 8.19 → 4.18 | 3.24 → 3.97 | 2.28 → 1.55 |
| a4 Air Smash | 25° | 8.08 → 2.57 | 26.43 → 19.50 | 2.43 → 1.22 |

### Manki

| Move / hit | Angle | Maximum rise (m) | Horizontal travel (m) | Time to land (s) |
|---|---:|---:|---:|---:|
| g1 Monkey Punch | 8° | 0.45 → 0.12 | 4.91 → 3.03 | 0.65 → 0.37 |
| g2 Straight Punch | 25° | 2.90 → 1.04 | 9.54 → 7.36 | 1.43 → 0.77 |
| g3 Sweeping Kick | 25° | 2.90 → 1.04 | 9.54 → 7.36 | 1.43 → 0.77 |
| g4 Double Kick | 28° | 9.41 → 3.19 | 25.95 → 21.03 | 2.65 → 1.35 |
| a1 Air Kick / hit 1 | 55° | 5.74 → 2.87 | 3.99 → 5.06 | 1.90 → 1.28 |
| a1 Air Kick / hit 2 | 45° | 7.64 → 3.43 | 8.80 → 10.33 | 2.25 → 1.40 |
| a2 Air Swing / hit 1 | 25° | 2.92 → 1.05 | 9.62 → 7.40 | 1.43 → 0.77 |
| a2 Air Swing / hit 2 | 20° | 0.78 → 0.32 | 3.52 → 2.55 | 0.75 → 0.42 |
| a3 High Kick | 65° | 8.19 → 4.18 | 3.24 → 3.97 | 2.32 → 1.55 |
| a4 Air Smash | 25° | 8.08 → 2.57 | 26.43 → 19.50 | 2.45 → 1.22 |

### Wibou

| Move / hit | Angle | Maximum rise (m) | Horizontal travel (m) | Time to land (s) |
|---|---:|---:|---:|---:|
| g1 Quick Slash | 30° | 2.05 → 0.87 | 5.10 → 4.69 | 1.18 → 0.70 |
| g2 Double Slash | 25° | 1.00 → 0.44 | 3.35 → 2.75 | 0.83 → 0.48 |
| g3 Up Slash | 80° | 8.17 → 4.39 | 0.94 → 1.05 | 2.25 → 1.58 |
| g4 Heavy Down Slash | 8° | 1.31 → 0.25 | 14.60 → 8.65 | 1.15 → 0.62 |
| a1 Air Slash | 30° | 2.07 → 0.88 | 5.15 → 4.72 | 1.18 → 0.70 |
| a2 Reverse Slash | 40° | 4.34 → 1.93 | 6.40 → 7.06 | 1.68 → 1.05 |
| a3 Air Up Slash | 78° | 6.68 → 3.60 | 0.96 → 1.09 | 2.03 → 1.43 |
| a4 Air Heavy Down Slash | -65° | 3.00 → 3.00 | 2.39 → 2.39 | 0.50 → 0.50 |

### Bonk

| Move / hit | Angle | Maximum rise (m) | Horizontal travel (m) | Time to land (s) |
|---|---:|---:|---:|---:|
| g1 Reverse Slash | 30° | 2.09 → 0.89 | 5.19 → 4.75 | 1.18 → 0.70 |
| g2 Greatsword Swing | 35° | 6.08 → 2.45 | 11.56 → 11.45 | 2.02 → 1.18 |
| g3 Reverse Rising Slash | 35° | 6.08 → 2.45 | 11.56 → 11.45 | 2.02 → 1.18 |
| g4 Heavy Double Slice | 25° | 8.51 → 2.70 | 27.86 → 20.59 | 2.48 → 1.25 |
| a1 Reverse Air Slash | 35° | 3.75 → 1.59 | 7.13 → 7.18 | 1.58 → 0.95 |
| a2 Bonk probe air 2 | 45° | 46.05 → 18.35 | 52.88 → 62.34 | 6.90 → 3.50 |
| a3 Downward Sweep | -45° | 3.00 → 3.00 | 5.26 → 5.26 | 0.57 → 0.57 |
| a4 Acrobatic Double Slice | 25° | 7.54 → 2.43 | 24.72 → 18.38 | 2.32 → 1.18 |

## Interpretation

- The strong 25–28° sends lose roughly two thirds of their height and about half their airtime. They still carry around 18–21 m at 120%. Shorter airtime reduces their total horizontal travel despite gentler braking.
- Steep launchers retain their role but have smaller arcs; lighter braking generally increases their horizontal displacement. They can remain predominantly vertical when authored at 78–80°.
- Weak hits retain a brief pop and can settle into grounded carry before their action lock expires. FightGuy Low Kick at 0% rises 0.11 → 0.05 m and travels 1.02 → 0.77 m by the end of its eleven-tick lock. It does not turn into an instant ground hit.
- Bonk’s authored `Bonk probe air 2` remains an outlier: growth 80 yields 18.35 m rise at 120% even with the new model. This pass changes shared flight, not character authoring. The updated real-hit pipeline reproduces that trajectory; its baseline 120% parity row was unobservable.
- Human readability, combo routes, edge recovery and actual stage KO thresholds still need playtesting. The flat-arena comparison does not establish those outcomes.

## Verification

- Behavioral coverage checks vertical arcs independent of hitstun duration, short-lock transfer, weak-hit pop/grounded carry, spikes, hitstop, lock-free launches, triangle-floor landing, direction-preserving braking and replay of the replicated launch clock.
- Only the affected Manki Aerosol Inferno golden was regenerated. Its victim begins descending earlier and retains more horizontal speed; damage, action state and attacker snapshots remain unchanged.
- Real-hit pipeline checks at 0% and 120% match direct launch velocity, hitstun and apex for every measured move across all four characters.
- Full Shared suite: 1,644 passed, two skipped, and the same two failures recorded before the change (bot aerial momentum and airborne Ki Shot activation). All fifteen new flight cases pass.
- Shared build succeeds. After explicit Editor import/recompile, Unity Ability Lab reproduces Low Kick at 0% (0.048 m rise) and Double Kick at 120% (3.193 m rise), both through actual hit contact and landing. Editor compilation and console error checks are clear. This is Edit Mode scenario verification, not a human match playtest.
