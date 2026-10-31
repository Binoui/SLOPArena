# Run speed compared with other fighters

**Date:** 2026-10-31. **Decision:** [ADR-0058](../adr/0058-movement-calibration-walk-takeoff-run-speeds.md).

The owner found SLOPArena too fast for precise spacing, Wibou above all: running at someone, jumping and hitting was hard.
This record compares our movement before ADR-0058 with platform fighters, 3D action games and 3D fighters.

## Compare body heights and reach, not raw speed

Every game picks its own unit, so raw speeds don't transfer. Two ratios do.

- **Body heights per second (BH/s).** Run speed divided by the character's standing height.
- **Frames to cover your own reach.** The distance from body centre to the far edge of a close grounded attack, divided by run speed per frame. Fewer frames leave less time to choose the moment to hit.

## SLOPArena before ADR-0058

These numbers come from the real simulation on main `31805e92`.

| Fighter | Run (m/s) | BH/s, capsule | BH/s, drawn height | Reach (m) | Frames to cover reach | 0.1 s tap with its stop (m) |
|---|---:|---:|---:|---:|---:|---:|
| FightGuy | 12 | 7.1 | 7.8 | 1.30 | 6.5 | 1.92 |
| Wibou | 14 | 8.2 | 8.9 | 2.10 | 9.0 | 2.24 |
| Manki | 11.5 | 7.7 | 9.9 | 1.30 | 6.8 | 1.84 |
| Bashie | 11.5 | 7.7 | | 1.00 | 5.2 | 1.84 |
| Salamander | 11.5 | 6.8 | | 1.25 | 6.5 | 1.84 |
| Sir Bonk | 10 | 5.9 | | 2.85 | 17.1 | 1.60 |
| Nyx | 10 | 5.9 | | 2.80 | 16.8 | 1.60 |

- The capsule is the gameplay `capsuleHeight`, 1.7 m except Manki and Bashie at 1.5 m. Drawn heights are the idle-pose tops from the 2026-10-17 lineup: FightGuy 1.53 m, Wibou 1.57 m and Manki 1.16 m.
- Reach is `HitReliabilityReport`'s farthest connect distance against a standing FightGuy, minus an estimated 0.3 m for the target's half-width. Each value is the median of ground.1, ground.3 and ground.4. Nyx's three normals range from 5.1 to 25 frames.
- Every fighter reached full speed in 13 ticks and stopped from a full run in 20 ticks, over 1.58 to 2.22 m.
- An ordinary jump kept 100% of run speed at takeoff. Air control then removed `airAccelStick + airAccelBase` per second, 0.32 m/s per tick for Wibou, so he reached his 7 m/s air cap 22 ticks later. A model that matches the measured running jump within 2% puts Wibou's air.1, active on tick 6, at 12.1 m/s.
- There was no walk. Stick tilt did not change speed.
- Stages measure 28 to 41 m along X. Hamster Habitat measures 65 m.

## Competitors

| Game | Character | BH/s | Frames to cover reach | Confidence |
|---|---|---:|---:|---|
| Smash Ultimate | Sonic, fastest runner | 19.1 | 4.8 | medium-high |
| Smash Ultimate | Fox | 9.2 | 7.3 | medium-high |
| Smash Ultimate | Mario | 7.2 | 7.7 | medium-high |
| Smash Ultimate | Captain Falcon | 7.2 | 7.5 | medium-high |
| Smash Ultimate | Marth | 5.3 | 12.7 | medium-high |
| Smash Ultimate | Bowser | 4.7 | 10.1 | medium-high |
| Smash Ultimate | Incineroar, slowest runner | 3.4 | 16.9 | medium-high |
| Smash 64 | Captain Falcon, fastest runner | 6.7 | 8.6 | medium |
| Smash 64 | Fox | 6.5 | 10.3 | estimated height |
| Smash 64 | Mario | 6.0 | 8.6 | estimated height |
| Smash 64 | Luigi, slowest runner | 4.8 | 10.9 | medium |
| Rivals of Aether | Maypul, fastest runner | 7.3 | 11.7 (jab) | medium-low |
| Rivals of Aether | Zetterburn | 5.3 | 16.2 (jab) | medium-low |
| Rivals of Aether | Etalus, slowest runner | 4.1 | 21.0 (jab) | medium-low |
| For Honor | sprint | 3.0 to 4.1 | 16 to 21 | low |
| Overwatch 2 | most heroes | 3.1 to 3.3 | 25 to 27 | estimated height |
| Supervive | slowest to fastest hunter | 2.4 to 2.7 | not found | estimated unit scale |
| Smite 2 | Athena, Mercury | 1.9 | 30 to 31 | estimated unit scale |
| League of Legends | Anivia to Master Yi | 1.1 to 1.2 | 32 to 35 | estimated height |
| People | Usain Bolt at top speed | 6.4 | | high |

- Since Melee, one Smash unit is about 0.1 m ([SmashWiki, Unit](https://www.ssbwiki.com/Unit)). On that scale Ultimate's Mario is 1.47 m tall and runs 10.6 m/s, and Fox runs 14.4 m/s. Our absolute speeds matched Ultimate's. Final Destination is about 16 m wide, so our stages were about twice as wide per body height.
- Melee clamps takeoff speed. `ftCo_Jump.c` in the Melee decomp scales ground speed by `ground_to_air_jump_momentum_multiplier`, adds the stick term, then clamps to `jump_h_max_velocity`.
- A [parameter dump of Ultimate's Terry](https://gist.github.com/benhall-7/fc8029bea991569f0cc63893ca5233d7) (`fighter_kind_dolly`) shows `run_speed_max` 1.72, `jump_speed_x_max` 1.1 and `air_speed_x_stable` 0.95. His takeoff is capped at 64% of his run speed, 1.16 times his air speed. The same dump has `walk_speed_max` 0.85, 49% of his run, and a slow-walk multiplier of 0.2.
- No usable public numbers turned up for Divine Knockout, Brawlhalla, MultiVersus or Rivals of Aether 2.

## What the comparison showed

- Per body height, the roster sat in Smash Ultimate's fast half and had no slow tier. Wibou ran like Ultimate's Fox, and our slowest fighters ran like Smash 64's Mario. Every 3D game measured was slower per body height than our slowest fighter.
- Reach changed which fighters were tight. Wibou's sword gave him 9 frames, normal for Smash. FightGuy, Manki, Bashie and Salamander needed 5 to 7 frames, fewer than Ultimate's Mario, Fox or Falcon.
- Our jump kept the full run speed, which neither Melee nor Ultimate does.
- There was no walk, and a 0.1 s tap moved Wibou 2.24 m, about as far as his sword reaches.
- Our stages were about twice Final Destination's width per body height, so a global slowdown also lengthens every crossing.
- The Steady camera looks down at 35°. A step toward or away from the camera draws at about sin 35°, 57%, of the same step sideways. This comes from the geometry and was not measured.

## Reproduce

Run these on a checkout of `31805e92` to repeat the numbers from before ADR-0058:

```sh
dotnet run --project tools/MovementReport -p:SkipUnityPluginCopy=true -- --out <dir>/movement-report.md --json <dir>/json
dotnet run --project tools/MovementReport -p:SkipUnityPluginCopy=true -- --ground-speed --out <dir>/ground-speed.md
dotnet run --project tools/HitReliabilityReport -p:SkipUnityPluginCopy=true -- --char all --out <dir>/hit-reliability
scripts/move-data.sh wibou --pcts 0 --out <dir>/move-data-wibou.md
```

## Sources

- [Smash series](2026-10-31-run-speed-smash.md). SmashWiki tables, measurements on SmashWiki's hitbox GIFs and the Smash 64 decomp.
- [Other platform fighters](2026-10-31-run-speed-platform-fighters.md). A Rivals of Aether devmode stat sheet and a workshop template.
- [3D action games and MOBAs](2026-10-31-run-speed-moba-action.md). Supervive, Smite 2, League of Legends and Overwatch 2.
- [3D fighters](2026-10-31-run-speed-3d-fighters.md). Dead or Alive 5, Tekken 7, Soulcalibur VI, For Honor and real-world sprint speeds.
- [SmashWiki, Dash](https://www.ssbwiki.com/Dash) for Ultimate's run speeds.
- The Melee decomp's `src/melee/ft/chara/ftCommon/ftCo_Jump.c`, read from a local checkout.
