# Movement calibration: 3D fighters and 3D arena brawlers

Goal. The owner feels SLOPArena moves too fast (Wibou most of all), spacing is imprecise, and "run at someone, jump and hit" is hard. This file collects competitor movement numbers from traditional 3D fighters and 3D arena brawlers, normalized to body height (BH) and attack reach, so they can be compared with our numbers.

Scope note. About 26 web requests were spent. Traditional 3D fighters publish frame data (timing), almost never distances. Real distance numbers turned up only for Dead or Alive 5 (in-game meter counter), For Honor (official patch notes in meters plus a secondary stat list), and a Tekken 7 memory-reading study whose unit is not stated. Soulcalibur VI gave only frame durations. Virtua Fighter 5, Power Stone, Dragon Ball Sparking! Zero / Budokai Tenkaichi 3, Naruto Storm, Jump Force, Absolver, ARMS and Pokkén were not found or not reached within budget. No number below is invented. Each carries a label.

Labels: [source: URL] read this session; [derived: formula]; [estimate: method]; "not found".

## Normalized table

Our reference row first. Frames are 60 Hz.

| Game | Character | Walk | Run / dash | Height | BH/s (run) | Jab / poke reach | Frames to cross reach at full speed | m/s | Labels |
|---|---|---|---|---|---|---|---|---|---|
| SLOPArena | Wibou | n/a | 14 m/s run | 1.7 m capsule (visual 1.2 to 1.6 m) | 8.2 (capsule), about 10 at 1.4 m visual | 1.1 to 1.9 m from centre | 4.7 to 8.1 f | 14 | brief; derived 14/1.7, 1.1/(14/60), 1.9/(14/60) |
| SLOPArena | Bonk / Nyx | n/a | 10 m/s | 1.7 m | 5.9 | 1.1 to 1.9 m | 6.6 to 11.4 f | 10 | brief; derived |
| Real world | adult walking | 1.33 to 1.4 m/s | n/a | about 1.75 m | 0.8 | arm reach not sourced | n/a | 1.4 | [source: Wikipedia Preferred walking speed]; height [estimate: typical adult] |
| Real world | jogger | n/a | about 2.2 to 3.3 m/s | about 1.75 m | 1.3 to 1.9 | n/a | n/a | 2.2 to 3.3 | [estimate: general knowledge, no source read] |
| Real world | Usain Bolt, 9.58 s 100 m | n/a | 10.44 m/s race average, 12.42 m/s peak (60 to 80 m) | 1.956 m (6 ft 5 in) | 5.3 avg, 6.4 peak | n/a | 1 m takes 4.8 f at peak | 12.42 | [source: Wikipedia Footspeed] speeds; [source: The Conversation] height; derived |
| Dead or Alive 5 | Jacky | not found | run speed not found | not found (human scale) | not found | P (jab) connects up to 1.32 m; 2P 1.03 m; mid pokes 1.5 to 2.0 m; 66H+K 2.62 m | not computable (no run speed) | n/a | [source: freestepdodge.com thread, in-game distance counter] |
| Tekken 7 | cast average | not found | Korean backdash about 0.69 "units" per 23 f cycle, about 0.03 units/f | not found | about 1.0 if the unit is meters (unverified) | not found | not found | about 1.8 if meters | [source: cthor2.pages.dev/Backdashes-(old)]; m/s [estimate: assumes unit = meter, unconfirmed] |
| Soulcalibur VI | Azwel (system data) | not found | back step 16 f, side step 13 f total; no distances | tiers only | not found | not found | not found | n/a | [source: dustloop SCVI System Data] |
| For Honor | 29 heroes | guard-mode walk not found | sprint 5.5 m/s (Shugoki, Highlander) to 7.5 m/s (Orochi, Peacekeeper, Shinobi, others); most 7 m/s | not found (human scale, about 1.8 to 2.0 m) | 3.0 to 4.1 | special/dodge attack ranges 2.0 to 2.75 m (light poke not found) | 16 to 21 f to cross 2.0 to 2.5 m | 5.5 to 7.5 | sprint [source: gaminggorilla.com, internally inconsistent units]; ranges [source: Ubisoft patch notes 2.28.0]; BH/s [estimate: 1.85 m height] |

## Per-game detail

### Dead or Alive 5 (closest to a native meter scale)

DOA5 practice mode shows the distance between the fighters in meters. A community tester recorded, for Jacky (the Virtua Fighter guest), the maximum distance at which each move connects. The tester held a Hayabusa dummy still, backed off to a target distance in small steps, and tried each move. [source: https://www.freestepdodge.com/threads/intercepting-fist-jackys-spacing.3480]

- P (jab): 1.32 m. 2P: 1.03 m. K: 1.53 m. 6P: 1.6 m. 3K: 1.75 m. 4P: 1.93 m. 66K: 2.01 m. 8P: 2.19 m. 66H+K: 2.62 m in the raw data (3.33 m in the tier text, unexplained). [source: same]
- Round start is "about 2 m", described as "about one character distance". [source: same]
- The tester buckets moves by 0.4 to 0.6 m bands (under 2 m, 2 to 2.4 m, 2.4 to 3 m, over 3 m). Spacing in this game is reasoned about in tenths of a meter. [source: same]
- Run speed, walk speed, dash distance and character height were not found.
- Reach per body height. DOA characters are human scale. At an assumed 1.8 m, the jab reaches about 0.73 BH and the longest mids about 1.2 BH. [estimate: 1.32/1.8, 2.19/1.8; height assumed] Ours is 1.1 to 1.9 m on a 1.7 m capsule, that is 0.65 to 1.12 BH. [derived] Reach is comparable. The speed that eats it is not.

### Tekken 7

- The cthor study read the distance value from Tekken's process memory with a frame-perfect macro. [source: https://cthor2.pages.dev/Backdashes]
- Two idle Heihachis sit at exactly 2000.0000 on the raw value. The unit is not stated. [source: same]
- The older version reports an average Korean backdash of 0.69 units per 13-frame backdash plus 10-frame cancel (23 f cycle), 0.67 for a 12 f backdash, and about 0.03 units per frame on average. [source: https://cthor2.pages.dev/Backdashes-(old)]
- If that unit is the meter (consistent with a raw value in millimeters, 2000 = 2 m, but unverified), KBD moves about 1.8 m/s, about 1.0 BH/s for a 1.8 m fighter. [estimate: 0.69/23x60; unit and height assumed]
- Run speed, walk speed, jab reach and character height in units were not found. Community ranges are expressed relative to backdashes ("Range 1 is a single backdash away"), not in units. (Seen only in a search snippet of a Scribd document, not read.)

### Soulcalibur VI

- System data gives back step 16 f total, side step 13 f total, jump 33 f, crouch 6 f. [source: https://dustloop.com/w/User:Cyllene.!/SCVI/System_Data]
- Height is given only as Create-a-Soul tiers 1 to 5, not lengths. Round start distance only as categories (Close to Very Far). [source: same]
- No walk, 8-way run, step distance or reach numbers found.

### For Honor (3D arena brawler, real meters)

- Official patch notes state ranges and movement in meters. Examples: Berserker sprint attack range 2 m to 2.5 m; dodge forward heavy forward movement 4.75 m to 6 m; Gladiator side dodge melee range 2 m to 2.75 m; Aramusha dodge forward heavy movement 3 m to 4 m. [source: https://www.ubisoft.com/en-us/game/for-honor/news-updates/6T6aUuLTvJZ4Tj7EmZ28wf/patch-notes-2280-for-honor]
- Sprint speed per hero: 5.5 m/s (Shugoki, Highlander), 6 m/s (Conqueror, Kensei), 6.25 to 6.5 m/s (Jiang Jun, Hitokiri, Tiandi), 7 m/s (most), 7.5 m/s (Kyoshin, Shinobi, Shaman, Orochi, Peacekeeper, Gladiator). [source: https://gaminggorilla.com/for-honor-tier-list] Low confidence. The same page's km/h column contradicts these values. Its m/s column agrees with a forum quote of the community wiki (5.5 m/s Shugoki up to 8.5 m/s Shinobi super sprint), which I saw only as a search snippet (vsbattles.com returned 403).
- The 2026 patch 2.67.0 raised sprint for Conqueror, Kensei, Highlander and Sohei by 8%. No m/s values. [source: https://www.ubisoft.com/en-us/game/for-honor/news-updates/vXAUsD2IqmktyG4GDtG1U/patch-notes-2670-for-honor]
- Guard-mode (fighting) walk speed and light-attack range not found. Sprint is the out-of-fight travel mode. Fighting happens from the much slower guard walk.
- At 7 m/s a 2.5 m range takes about 21 f to cross. [derived: 2.5/(7/60)]

### Not found

Virtua Fighter 5 (wiki says walk speed differs per character, no numbers). Power Stone 1/2, Dragon Ball Sparking! Zero / Budokai Tenkaichi 3, Naruto Storm, Jump Force, Absolver, ARMS, Pokkén field phase: not reached within the request budget; no numbers.

## Human baseline

- Preferred walking speed 1.10 to 1.65 m/s; design value 1.4 m/s; Transport for London uses 1.33 m/s. [source: https://en.wikipedia.org/wiki/Preferred_walking_speed]
- Jogging about 2.2 to 3.3 m/s. [estimate: general knowledge, no source read]
- Usain Bolt, 2009 World Championships: average 37.578 km/h (10.44 m/s), peak 44.72 km/h (12.42 m/s) between 60 m and 80 m. [source: https://en.wikipedia.org/wiki/Footspeed]
- Bolt is 6 ft 5 in (1.956 m). [source: https://theconversation.com/the-end-of-my-bolt-trilogy-8537]
- Normalized: walk about 0.8 BH/s (1.4/1.75, height assumed), Bolt 5.3 BH/s average and 6.4 BH/s peak. [derived]

## Comparison with SLOPArena

- Wibou's 14 m/s is faster in absolute terms than Bolt's 12.42 m/s measured peak. Normalized, 8.2 capsule heights per second is 1.3x Bolt's peak (6.4 BH/s), and about 10 BH/s against his 1.4 m visual height. [derived]
- Our slowest runners (10 m/s, 5.9 BH/s) sit near Bolt's race average (5.3 BH/s). [derived]
- For Honor's sprint (3.0 to 4.1 BH/s, estimate) is half of Wibou's, and its fighting happens from a slower guard walk. [estimate]
- Grounded 3D fighters move in fractions of a meter. The Tekken backdash cycle covers about 0.69 units in 23 frames (about 1 BH/s if meters, unverified). DOA5 moves differ in reach by 0.1 to 0.3 m.
- At 14 m/s Wibou moves 0.233 m per frame. He crosses our whole 1.1 to 1.9 m reach band in 4.7 to 8.1 frames, and covers 2.33 m during any 10 frames of startup. [derived: 14/60, 14x10/60] So at full run the window between "too far" and "already past" is shorter than one attack's startup. That is a mechanical reason why "run at someone, jump and hit" feels hard.
