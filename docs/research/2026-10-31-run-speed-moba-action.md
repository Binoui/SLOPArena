# Movement calibration: 3D action and MOBA games

Date: 2026-10-31. Slice: Supervive, Smite 1 and 2, League of Legends, Overwatch 2 (Battlerite skipped for budget).
Goal: express each game's run speed relative to body height (BH/s) and to melee reach, so SLOPArena's speeds can be compared.

Labels used: [source: URL] = read this session. [snippet: URL] = seen only in a search-result summary, page itself blocked or not fetched. [derived: formula]. [estimate: method].

## Normalized table

Frames are at 60 fps. "Cross reach" is the time to run the length of the melee reach at base speed.

| Game | Character/class | Speed (native) | Unit scale | Height | BH/s | Melee reach | Frames to cross reach | m/s | Labels |
|---|---|---|---|---|---|---|---|---|---|
| SLOPArena (context) | Wibou | 14 m/s | meters | 1.7 m capsule | 8.24 | 1.1 to 1.9 m | 4.7 to 8.1 f (0.08 to 0.14 s) | 14 | brief values; [derived: 14/1.7; reach/14*60] |
| SLOPArena (context) | Bonk, Nyx | 10 m/s | meters | 1.7 m capsule | 5.88 | 1.1 to 1.9 m | 6.6 to 11.4 f | 10 | brief values; [derived] |
| Supervive | slowest (Beebo, Bishop, Hudson) | 430 u/s | est. 1 u = 1 cm | not found (est. 1.8 m) | est. 2.39 | not found | 20.9 f for a 1.5 m reach (hypothetical) | est. 4.30 | speed [source 1]; scale, height [estimate] |
| Supervive | mean of 24 hunters | 459 u/s (median 460) | est. 1 u = 1 cm | est. 1.8 m | est. 2.55 | not found | 19.6 f for 1.5 m (hypothetical) | est. 4.59 | [derived: mean of the source 1 table] |
| Supervive | fastest (Shrike) | 490 u/s | est. 1 u = 1 cm | est. 1.8 m | est. 2.72 | not found | 18.4 f for 1.5 m (hypothetical) | est. 4.90 | [source 1] |
| Smite 2 | Athena (melee guardian) | 370 u/s | move: est. cm/s; range: shown in meters | not found (est. 2.0 m) | est. 1.85 | 1.92 m (120 deg cone) | 31.1 f (0.52 s) | est. 3.70 | speed, reach [source 3]; scale [source 4 + estimate] |
| Smite 2 | Mercury (fastest god, melee) | 383 u/s | same | est. 2.0 m | est. 1.92 | 1.92 m | 30.1 f (0.50 s) | est. 3.83 | [source 4] |
| Smite 2 | ranged basic (Athena passive / Mercury ability show the 8.8 m value) | 370 to 383 | same | est. 2.0 m | est. 1.9 | 8.8 m ranged | 2.3 s to close 8.8 m | est. 3.8 | [derived: 8.8/3.83] |
| Smite 1 | melee basic | not read | 1 range unit = 0.16 m | not found | n/a | 12 units = 1.92 m | n/a | n/a | [source 5, community post] |
| League of Legends | slowest (Anivia) | 325 u/s | 1 m ~ 165.85 u (community) | not a game stat (est. 1.8 m = 299 u) | est. 1.09 | 125 u edge + 65 u own radius = 190 u = 1.15 m from centre to target edge | 35.1 f (0.58 s) | 1.96 | speed [source 6]; scale [source 7]; radius [source 8] |
| League of Legends | fastest (Master Yi) | 355 u/s | same | est. 1.8 m | est. 1.19 | 1.15 m | 32.1 f (0.54 s) | 2.14 | [source 6]; [derived] |
| Overwatch 2 | most heroes | 5.5 m/s | meters (native) | not found (est. 1.8 m) | est. 3.06 | quick melee 2.5 m | 27.3 f (0.45 s) | 5.5 | speed [snippet 9]; melee [snippet 10]; height [estimate] |
| Overwatch 2 | Tracer, Genji | 6.0 m/s | meters | est. 1.8 m | est. 3.33 | 2.5 m | 25.0 f (0.42 s) | 6.0 | [snippet 9] |

### Reach-normalized speed

Speed divided by melee reach gives "reach lengths per second". It is the most direct proxy for "run at someone, jump and hit" spacing difficulty.

| Game | Speed / melee reach (reach lengths per second) | Reach / height |
|---|---|---|
| SLOPArena Wibou | 7.4 to 12.7 [derived: 14/1.9, 14/1.1] | 0.65 to 1.12 [derived: reach/1.7] |
| SLOPArena Bonk/Nyx | 5.3 to 9.1 [derived] | 0.65 to 1.12 |
| Supervive | not found (reach unknown) | not found |
| Smite 2 melee | 1.9 to 2.0 [derived: 3.70/1.92, 3.83/1.92] | ~0.96 [estimate: 1.92/2.0] |
| League melee | 1.7 to 1.9 [derived: 1.96/1.15, 2.14/1.15] | ~0.64 [estimate: 1.15/1.8] |
| Overwatch 2 | 2.2 to 2.4 [derived: 5.5/2.5, 6/2.5] | ~1.39 [estimate: 2.5/1.8] |

## Per-game findings

### Supervive (Theorycraft Games)

- Base move speed is a fixed per-hunter stat that "does not change with level" [source 1]. The 24-hunter table on wiki.gg ranges from 430 (Beebo, Bishop, Hudson) to 490 (Shrike); mean 458.96, median 460 [source 1; derived]. The wiki page is an archive; the main wiki describes the game in past tense (search summary only).
- Unit scale: not stated anywhere I found. Patch notes use bare numbers for distances (Wukong Dragon Slam max range 1450 to 1050; Shiv Arcslinger max distance 950 to 900; Eva orbs despawn beyond "1600 units") [source 2]. Patch Open Beta notes give Oath's hammer "a minimum travel distance of 5m" [source 11], so the designers also think in meters. [estimate: Unreal Engine default 1 uu = 1 cm, which gives 430 to 490 cm/s = 4.3 to 4.9 m/s. Plausible for a human-scale hunter. It is not confirmed by a dev or datamine; confidence low to medium.]
- Hunter height: not found. Basic-attack range: not found on wiki.gg hunter pages (Wukong, Carbine) or patch notes. Carbine's wiki notes say his first shot peaks "beyond roughly 50% range" without stating the range (search summary only).
- Speed boosts: Wukong's Black-Iron Rod flurry dash speed 1000 to 900 u/s [source 2]. That is about 2x base, or est. 9 m/s. Quickblade's Turbo Boost grants "50 movespeed that decays over 2s" after each movement ability. Big Game Hunter grants 5 movespeed per stack, max 3 stacks [source 11]. Glider and windrings exist; Windrings got "a terminal velocity" but no number was published [source 11]. Sprint speed: not found.
- Camera: top-down isometric (general knowledge, not sourced).

### Smite 1 and Smite 2 (Hi-Rez)

- Smite 2 shows ranges in meters. Athena: basic attack 1.92 m, 120 degree cone; passive 8.8 m; Preemptive Strike 8.8 m [source 3]. Mercury: basic 1.92 m; Made You Look 7.68 m; Sonic Boom max 48 m [source 4].
- Smite 2 move speed shows as a bare number: Athena 370, Mercury 383, both "+0" per level [source 3, source 4]. One search summary quoted Athena at 442, which conflicts with the page I read (370). I use 370.
- Smite 1 range units to meters: a Reddit user who says they "submitted some updates" (developer status unconfirmed) wrote that Smite 1 "16" units correspond to 2.56 m and normal basics are "12" units (1.92 m) [source 5]. This gives 1 range unit = 0.16 m. Smite 2's 8.8 m equals 55 units at that factor [derived: 8.8/0.16], consistent with Smite 1's ranged-basic range of 55 units (not verified this session).
- Move speed units: Mercury's Smite 2 passive reads "For every 1 centimeter traveled, gain a 0.05% Damage Bonus", max 500 stacks [source 4]. Travel is counted in centimeters, which supports move speed in cm/s (Unreal default) [estimate; confidence medium]. Then 370 to 383 cm/s = 3.70 to 3.83 m/s, and one Smite 1 range unit = 16 movement units. So the suspicion holds. Range units and movement units differ by about 16x, not 1x.
- God height: not found [estimate: 2.0 m, gods read slightly larger than human; low confidence]. Smite 1 speed growth (3% per level for 8 levels) and Smite 2 removing it appear only in search summaries, not verified.
- Speed caps or items: not found this session.
- Camera: third-person, over the shoulder, behind the god (general knowledge).

### League of Legends (calibration)

- Base move speed 325 (Anivia) to 355 (Master Yi). Soft caps: raw speed from 415 to 490 is scaled to 80%, above 490 to 50% [source 6]. Speed is "game-distance units traveled per second" [source 6].
- Unit scale: "an arbitrary and explicit quantity of 'units'". A trivia note derived from the Eternals stat tracker gives "1 meter ≈ 165.85 units" [source 7]. That is community-derived, not an official definition. Confidence medium.
- Champion size: most champions have a gameplay radius of 55 to 80 units, often 65 [source 8]. Height is not a gameplay stat. I found no source for it [estimate: 1.8 m human = 299 u].
- Melee range: usually 125 to 325 units; basic attacks use edge range (550 edge = 680 centre for 65-radius champions) [source 7]. Melee "usually within 200 units" [source 12]. Smallest melee: 125 + 65 = 190 u from the attacker's centre to the target's edge = 1.15 m [derived].
- Camera: top-down isometric, click to move (general knowledge).

### Overwatch 2 (calibration)

- Base 5.5 m/s; Tracer and Genji 6.0 m/s; backwards is 90% speed; crouch 3 m/s [snippet 9: overwatch.fandom.com/wiki/Character via search summary; fetch blocked with HTTP 402]. A Blizzard forum thread confirms only relative speeds ("everyone is 10% faster but tracer and genji already were 10% faster") from players, not staff [source 13].
- Quick melee range 2.5 m, active 0.528 s, cooldown 1 s; Reinhardt's hammer 5 m [snippet 10: overwatch.fandom.com/wiki/Melee via search summary]. A workshop tool describes the hitbox as a 1 m radius sphere centred 1.5 m ahead (search summary only).
- Hero height: not found [estimate: 1.8 m].
- Camera: first person.

### Battlerite

Skipped for budget.

## Takeaways

1. Every game in this slice moves about 1 to 3.3 body heights per second. SLOPArena moves 5.9 to 8.2 capsule heights per second. That is 2.5x to 8x more relative to body size. Even Overwatch, the fastest here at about 3 BH/s, is under half of Bonk's 5.9.
2. Reach-normalized, the gap is larger. Competitors close one melee reach in 25 to 35 frames (0.42 to 0.58 s). Wibou closes his reach in 5 to 8 frames. Spacing in those games is a half-second decision; in SLOPArena it is a few frames. This matches the owner's "run at someone, jump and hit is hard" feeling [inference, not tested in game].
3. These are ground-based, non-platform games and some use top-down cameras. They set a lower bound for "precise spacing" feel, not a target. A platform fighter (Smash and similar) is the better direct comparison and belongs to another slice. Unit scale is the weakest link only for Supervive. Even at 2x its estimated scale, Supervive would be about 5 BH/s, still below every SLOPArena fighter.

## Unit-scale confidence

- Overwatch 2: high. Native meters [snippet 9]. Page itself not fetched.
- League: medium. 165.85 u/m from the Eternals trivia on the official wiki [source 7]. Height still estimated.
- Smite 2: medium. Ranges in meters [source 3, 4]; move speed in cm/s inferred from Mercury's "per centimeter traveled" passive [source 4]. Smite 1 0.16 m per range unit from one community post [source 5], consistent with both 1.92 m and 8.8 m.
- Supervive: low to medium. Unreal default cm assumed; patch notes mix bare units and meters [source 2, 11]. No dev or datamine confirmation.

## Sources

1. https://supervive.wiki.gg/wiki/List_of_Hunter_Base_Stats
2. https://games-manuals.com/updates-patchnotes/supervive-1283700/102-patch-notes---dunking-changes-buffs--nerfs-and-a-new-hunter-1808601382394244 (mirror of the 1.02 Steam patch notes)
3. https://wiki.smite2.com/w/Athena
4. https://wiki.smite2.com/w/Mercury
5. https://devtrackers.gg/smite/p/4434f0f7-awilix-auto-attacks-feel-so-off-in-smite-2
6. https://wiki.leagueoflegends.com/en-us/Movement_speed
7. https://wiki.leagueoflegends.com/en-us/Range
8. https://wiki.leagueoflegends.com/en-us/Gameplay_radius
9. (snippet) https://overwatch.fandom.com/wiki/Character. Fetch returned HTTP 402; numbers from the search-result summary only.
10. (snippet) https://overwatch.fandom.com/wiki/Melee. Same caveat.
11. https://supervive.wiki.gg/wiki/Patch_Open_Beta
12. https://wiki.leagueoflegends.com/en-us/Melee
13. https://us.forums.blizzard.com/en/overwatch/t/is-tracer-and-genji-still-faster/665705

Also read with no usable data: https://supervive.wiki.gg/wiki/Wukong, https://www.supervive.wiki/. Fandom and breezewiki mirrors for LoL, Smite 1 and Overwatch were blocked (HTTP 402 or a bot wall).
