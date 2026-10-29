---
key: training
display_name: Training
target: training
players: 1-4
roster_entry: trainingOnly
variant: static
availability: accepted
competitive_intent: mixed
topology: Crash Pad with western drill yard, trench, low shelf and eastern recovery islands
source_scene: client/Unity/Assets/Stages/training/training.unity
baked_arena: data/arenas/training.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/training.prefab
design_brief: docs/design/stages/training.design.md
art_source: tools/blender/training
human_review:
  owner: project maintainer
  status: accepted 2026-10-29 (Slop Factory check #20, "Training map view and edge recoveries": GOOD)
  process: play Training and judge drill access, recovery, CPU reset positions, launch distances and effect readability
---

# Training Crash-Test Lab

Every punch gets crash-tested here before it ships. The [locked round-2 brief](training.design.md)
combines the Drill Yard and Recovery Range with the retained Crash Pad. Training keeps its key,
sole `trainingOnly` roster entry and default selection. The maintainer accepted it on
2026-10-29 after playing it ("Training map view and edge recoveries", GOOD).

## Gameplay shell

One table in `tools/blender/training/layout.py` owns the eleven named solids below.
The shell export, strict bake check, procedural generator and Shared tests read that table.
The trench is the void between the two floors; it is not an extra solid. Values are Unity metres.

| Solid and mesh asset | x range | z range | Top | Bottom |
| --- | --- | --- | --- | --- |
| TestPlatform, `TrainingTestPlatform` | -14 to 14 | -13 to 13 | 0 | -12 |
| WestDeck, `TrainingWestDeck` | -12.5 to -6.5 | 7 to 12 | 1.5 | 0 |
| EastDeck, `TrainingEastDeck` | 6.5 to 12.5 | 7 to 12 | 1.5 | 0 |
| Walkway, `TrainingWalkway` | -17 to -14 | -13 to -9 | 0 | -12 |
| DrillYard, `TrainingDrillYard` | -31 to -17 | -13 to 13 | 0 | -12 |
| StepDeck, `TrainingStepDeck` | -30 to -22 | 3 to 12 | 1.5 | 0 |
| StepBlock, `TrainingStepBlock` | -30 to -26 | 8 to 12 | 3 | 1.5 |
| Shelf, `TrainingShelf` | -35 to -31 | -9 to 1 | -1.5 | -2.1 |
| NearIsland, `TrainingNearIsland` | 18 to 22 | 7 to 11 | 0 | -12 |
| MidIsland, `TrainingMidIsland` | 22 to 26 | -2 to 2 | 0 | -12 |
| FarIsland, `TrainingFarIsland` | 27.5 to 31.5 | -11 to -7 | 0 | -12 |

The player spawn is (0, 0.85, -4), yaw 0. The dummy spawn is (0, 0.85, 4), yaw 180.
The step-deck spawn is (-23.5, 2.35, 7.5), yaw 90. The mid-island spawn is (24, 0.85, 0), yaw 270.
The export looks each surface up in the table, then adds the marker body half-height.
Every cooked fighter grounds using its own capsule height.

The final gaps from the Crash Pad's x=14 east edge are 4, 8 and 13.5 m. The far island
moved east 1.5 m within the approved tuning range. The actual bake derives x=-45/+41.5,
z=-23/+23, top 23 and kill -12. Collision has 132 triangles. Source and bake hashes are
recorded in `tmp/training-views/round4-report.md`; the baked `.arena` is runtime authority.

The main floor remains the clear combat space. The yard, shelf and islands supply the
explicit drills required by [Stage Concepts](../stage-concepts.md). Shared owns grounding,
collision and blast deaths. Unity owns presentation only.

## Traversal results and deviations

The cooked manifest contained seven fighters during verification, including Nyx. Coverage
reads the manifest rather than preserving the brief's earlier six-fighter assumption.
The following results use the real ServerSimulation and baked arena, ordinary full jumps,
no air dodge, and the package's E recovery. Second-jump trials cover ticks 8 through 80.
Recovery trials vary activation time and Sir Bonk's aim hold. A result requires a living,
grounded landing inside the target footprint at its top height. Full input-plan diagnostics
are in `tmp/training-views/round4-shared-tests.log`.

| Fighter | 4 m, single | 8 m, single | 8 m, double | 13.5 m, double | 13.5 m, recovery |
| --- | --- | --- | --- | --- | --- |
| FightGuy | lands | misses | lands | misses | lands |
| Manki | lands | misses | lands | lands | lands |
| Wibou | lands | misses | lands | misses | lands |
| Sir Bonk | lands | misses | lands | misses | lands |
| Bashie | lands | misses | lands | lands | lands |
| Salamander | lands | misses | lands | lands | lands |
| Nyx | lands | misses | lands | misses | lands |

DEVIATION. The far-island requirement cannot hold for every fighter within the allowed
10.5 to 13.5 m gap. Manki, Bashie and Salamander land with two jumps at both the nominal
12 m and maximum 13.5 m gaps. The maximum gap preserves the recovery requirement for the
other four fighters. These three exceptions are explicit on the far-island SPEC row and
asserted by tests. No fighter tuning changed.

Ledge grabs are disabled in the current game (`CONTEXT.md`, Recovery), and the locked
brief now says so. The trench is a recovery drill without a grab. An unattended fall dies
at the bottom boundary, and every one of the seven fighters recovers from inside the
trench back onto the Crash Pad.

## Presentation and files

The [generator guide](../../../tools/blender/training/README.md) describes the reproducible
Blender build. Every solid uses the same plated-face construction and shared floor paint
atlas. Deep platforms have braced supports below the kill plane. The shelf has a visible
underside and no structure below it. Hazard lips, depth rulers, height plates, island labels,
matching arrows and all four spawn markings are generated from the shell.

The widened hall retains the north control room/counter, west dummy bench, east crash
sled/parts rack and south camera gantry/roller door. Walls are 13 m beyond the curtains.
The foam basin remains below the kill plane, and overhead trusses and lamps stay above
the top blast plane. The light rig remains a neutral directional key and ambient fill,
so the new zones receive the same illumination as the Crash Pad.

`presentation.py` owns start, dummy, yard, range, trench, south-recovery and thumbnail
cameras. `preview.py` mirrors their vantages. The thumbnail sits inside the hall below the
trusses and includes the full island ladder. East-edge gap paint fits the strip outside the
retained east deck. There are no external environment dependencies or stage-specific C#.
Fonts are the project's OFL Archivo Black and Space Mono.

The cosmetic prefab has only `Crash-Test Lab` and `Stage Lights` roots. Art, materials and
the prefab ship in Git since acceptance: `.gitignore` lists the `CrashTestLab` art and the
prefab, and `tools/check_asset_closure.py` reports every reference shipping. The shell scene, mesh assets,
`.arena`, generator, two affected test files, production brief, roster prose and thumbnail
form the Training changes. `TESTING-UNITY.md` changes only its existing Training item.
The original grey-lab backup under `artifacts/training-greylab-20261029/` is untouched.

## Verification

The shell-apply, bake and inspect sequence passed through this builder's own gateway lease.
The strict comparison passed before art generation. The focused Shared command passed
191/191 tests in the shared checkout, including 57 Training cases across all seven fighters.
No unrelated compile error blocked this run, so the conditional throwaway-worktree fallback
was not needed and was not run. The test command used `-p:SkipUnityPluginCopy=true`.

```bash
dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true --filter 'FullyQualifiedName~Training|FullyQualifiedName~TutorialProgress|FullyQualifiedName~ArenaShipping|FullyQualifiedName~BlastZone'
```

Tests cover all spawns, derived blast lines, north/south knockback deaths, trench death and
recovery, both lips' detection and an existing hang, the standing trench jump, step/block
access, Sir Bonk's direct block jump, shelf return and the measured island ladder.
`TutorialProgressTests.RealBeelineOutOfShieldAndRecoveryAreFeasible` now starts 2.5 m beyond
the plain south edge at (0,-15.5), with its dummy 3.5 m inside at (0,-9.5). Its meaning and
assertions are preserved. The test-only `TutorialCurriculum.TrainingLab` fixture remains
outside this stage task.

The final build has 76,412 triangles against the unchanged 120,000 budget. Mesh checks
report zero flipped edges, degenerate faces or loose vertices. Actual background/curtain
faces have zero open-corridor intersections. The final stage inspection reports no
colliders, missing references or unsupported shaders. Gameplay and review-camera sessions
start successfully and report zero console errors; no Blender fallback was needed.

Delivery gameplay captures clip 1.4 to 2.1 percent, with mean brightness 134.4 to 145.4.
Review cameras clip 0.3 to 1.8 percent; the darker trench and recovery faces are intentional.
All capture passes have exposure reports under `tmp/training-views/round4-*-exposure.txt`.
The single five-second uncapped Ultra 1920x1080 Editor sample contained 1,180 frames and
measured mean 4.24 ms, median 3.65 ms, p95 6.98 ms and worst 12.89 ms. This is one Editor
sample on a shared workstation, not a packaged-player performance claim.

Final captures are `tmp/training-views/round4-delivery-orbit/` and
`tmp/training-views/round4-delivery-cameras/`. The shared thumbnail command installed that
camera pass's `thumbnail.png`. The contact sheet is `tmp/training-views/round4-contact-sheet.png`.
Parent-review JPEGs are `tmp/training-rework-20261029/r2-start.jpg`, `r2-yard.jpg`, `r2-range.jpg`
and `r2-thumbnail.jpg`. The detailed local report is `tmp/training-views/round4-report.md`;
`tmp/training-views/round4-files.tsv` inventories the touched files and their status.

These checks establish the implemented shell and presentation. The maintainer's play
accepted the map on 2026-10-29. Packaged-player performance remains unmeasured.

## Brief conformance

The quotes below are each numbered decision's title and first sentence from the locked
round-2 brief. PASS means implemented and checked as described, not human acceptance.

### Shell

| Locked decision, quoted | Implementation |
| --- | --- |
| "Crash Pad. Unchanged from round 1: one rectangular floor, 28 m east-west by 26 m north-south (x ±14, z ±13) at height 0, its walls dropping straight to the KO line at −12, with the two 1.5 m decks (6 × 5 m, x 6.5 to 12.5 east and west of the centre line, z 7 to 12) and the distance lane." | PASS. Crash Pad footprint, floor height and two deck footprints/heights are preserved. All three original mesh assets and GUIDs remain. Raised solids now have closed bottom caps in the common prism format. |
| "Drill Yard. A second floor west of the Crash Pad, x −31 to −17, z ±13, height 0, walls to −12." | PASS. Every cooked fighter jumps onto the step deck and then the block. Only Sir Bonk reaches the block directly with a single full jump; his measured apex is 3.14 m. All seven reach it with two jumps. |
| "Trench and walkway. A 3 m slot (x −17 to −14) separates the two floors from z −9 to the north rim, where it is open, and drops to the KO line." | PASS. The trench and walkway match the shell. A standing jump crosses for all seven fighters, an unattended fall dies at the bottom boundary, and all seven recover out of the trench. Ledge grabs are disabled in the current game, so the lips are recovery targets. |
| "Shelf. A 0.6 m thick shelf 1.5 m below the Drill Yard's west edge, 4 m deep (x −35 to −31, z −9 to 1), open below, like Log Off's shelves." | PASS. The shelf is 0.6 m thick with no support or collision below it. All seven fighters climb back with one jump. |
| "Recovery Range. Three 4 × 4 m islands at height 0 off the Crash Pad's east edge, walls to −12, spread north to south so no jump path crosses another island: the 4 m island (x 18 to 22, z 7 to 11), the 8 m island (x 22 to 26, z −2 to 2) and the 12 m island (x 26 to 30, z −11 to −7), named by their gap from the edge." | DEVIATION. The far island moved east by the permitted 1.5 m to a 13.5 m gap. Manki, Bashie and Salamander still reach it with a delayed double jump. Four of seven require recovery there; all seven can land with recovery. The 4 m and 8 m intents hold for all seven. |
| "Blast lines. The north and south KO lines stay at z ±23, so hits from the start towards the north keep match distance." | PASS. The bake derives west -45, east +41.5 after the allowed tuning, north/south +/-23, top 23 and kill -12. |
| "Spawns. Spawn_01, the player, at (0, −4) facing north; Spawn_02, the first CPU and the dummy spot, at (0, 4) facing south; Spawn_03 on the step deck at (−23.5, 7.5) facing east; Spawn_04 on the 8 m island at (24, 0) facing west." | PASS. All four coordinates, heights and yaws match the table and all seven fighters settle grounded at every spawn. Multi-CPU Reset Position remains a human check. |

### Composition decisions

| Locked decision, quoted | Implementation |
| --- | --- |
| "The test platform. The Crash Pad, the Drill Yard and each island are steel test platforms standing on braced lattices of legs inside a vast, bright test hall." | PASS. Every platform has a plated face and a braced lattice below the kill plane. No edge props rise above the shell. |
| "Floor paint. A mid-grey test floor with a thin 1 m grid and stronger 5 m lines on both floors, all paint." | PASS. Both floors have the metre grid, stronger 5 m lines, edge and trench hazard paint, all four start/station markings and the Drill Yard stencil. |
| "Distance lane. Unchanged: from the dummy spot down the centre line to the south edge, metre ticks with numbers 5, 10 and 15 counted from the dummy, and a teal "BEELINE 16 m" bar at z = −12." | PASS. The original dummy-to-south lane, 5/10/15 labels and teal Beeline bar are retained. |
| "Deck and rig faces. The Crash Pad's decks and the step rig have yellow-black striped sides carrying height boards, ticks every 0.5 m, to 1.5 m on the decks and to 3 m on the block." | PASS. Raised faces have stripes, half-metre height boards and one-jump/double-jump plates. The block board marks 1.5 through 3 m on its exposed upper face. |
| "Depth marks. Every plated face that drops to the KO line, including both trench walls and the island faces, carries depth marks every 2 m from the edge down, numbered at −4 and −8, with a red KO band at −12. "TRENCH 3 m" is stencilled on the Crash Pad's lip and the shelf carries a "−1.5 m" plate on its hazard nose." | PASS. Every deep face, including the trench and islands, has two-metre ticks, -4/-8 labels and the red kill band. The trench lip stencil and shelf nose plates are present. |
| "Gap labels. Each island's cap carries its gap in large paint ("4 m", "8 m", "12 m"), and a matching arrow with the same number is painted at the Crash Pad's east edge facing it." | PASS. Island caps and matching east-edge arrows carry the actual 4, 8 and 13.5 m gaps. East-edge numbers fit the clear strip beyond the retained east deck. |
| "Light curtains. Sparse, thin red light-curtain beams stand on the four side KO planes, from below deck level to a few metres above it, between slim yellow-black emitter posts." | PASS. Four thin red curtain lines and striped posts follow the derived planes from just outside the corridor. |
| "Landmark. From the start camera (yaw 0), centre and top third: the north hall wall's control-room window strip at deck height with a big "DAYS WITHOUT A KO: 0" board and the "SLOP LABS · IMPACT TESTING" sign above it." | PASS. The north control window, counter and sign remain the start-view landmark. |
| "West wall. From the start camera, left beyond the Drill Yard: a bench of crash-test dummies on a mezzanine at deck height, watching the deck." | PASS. The original dummy bench and watching dummies moved to the widened west wall. |
| "East wall. Right beyond the Recovery Range: the crash-sled track along the hall floor ending in a barrier with a wrecked test car, and a rack of spare dummy parts." | PASS. The crash sled, wreck and parts rack remain on the east apron beyond the widened corridor. |
| "South wall. Behind the start (yaw 180): a high-speed camera on a gantry aimed at the deck, light towers and a big roller door with hazard stripes." | PASS. The camera gantry, lamp towers and striped roller door remain south of the corridor. |
| "Scene world. Below: a deep pit of grey-blue foam blocks fills the hall floor under and around every platform, its surface below the KO line, so a launched fighter blasts before reaching it." | PASS. The widened foam pit stays below kill height. Trusses and lamps are above the new 23 m top line; walls close the hall 13 m beyond the curtains. |
| "Busy / empty. Busy: the hall walls and their four set pieces, the camera gantry, the curtain posts." | PASS. Playable caps and the trench contain only paint. Geometry checks report zero background/curtain faces inside the open blast corridor. |

### Negative decisions

| Locked decision, quoted | Implementation |
| --- | --- |
| "No raised props on any walkable surface. The decks, the step rig and the islands are the only raised surfaces; targets, lane, labels, arrows and stripes are paint." | PASS. Only the authorized raised shells occupy the playable caps. Targets, grid, labels, arrows and stripes are coplanar paint. |
| "No walls, railings or barriers along any edge, trench lip, shelf or island. Every edge stays open." | PASS. No wall, railing or barrier rises along a playable edge. Plated walls drop below their tops. |
| "No background geometry inside the blast corridor (x −45 to 40, z ±23, −12 to 23). Curtain beams and posts sit on or just outside its boundary." | PASS. The build clips actual background faces against the tuned open corridor and reports zero intersections. Curtains sit just outside it. |
| "No second hero landmark beside the KO board and sign." | PASS. The north counter/sign remains the single hero landmark. |
| "No saturated colour or coloured light on any floor, and nothing flashing or scrolling near the platforms that competes with move effects. The maintainer judges new moves and effects here." | PASS. The floor is neutral grey under one neutral directional key. Nothing near it flashes or scrolls. |
| "Nothing of the old grey lab remains." | PASS. The prefab has only Crash-Test Lab and Stage Lights roots. The old-map backup remains untouched. |
