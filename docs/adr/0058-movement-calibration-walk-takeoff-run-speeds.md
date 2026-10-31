# ADR-0058: Movement calibration: Walk, jump takeoff cap and slower run speeds

**Status:** Accepted 2026-10-31. The owner approved all four options from the run-speed comparison ("i agree with all 4 points [...] lets make them all and then i test"). Feel verdict pending.
**Deciders:** @Binoui
**Amends:** [ADR-0020](0020-melee-based-movement.md) §2 (tilt magnitude now selects Walk) and [ADR-0016](0016-keyboard-first-input-model.md) (a held Walk action)
**Evidence:** [Run speed comparison](../research/2026-10-31-run-speed-comparison.md)

## Context

The owner found the game too fast for precise spacing, Wibou most of all: running at someone, jumping and hitting was hard.
The comparison measured our roster in the real simulation and normalized competitor data by body height and attack reach.

- Per capsule height the roster ran 5.9 to 8.2 body heights per second. Smash Ultimate spans 3.4 to 9.2 (Sonic 19), Smash 64 4.8 to 6.7, Rivals of Aether 4.1 to 7.3, and every 3D game measured stayed under 4.1.
- An ordinary jump kept 100% of run speed at takeoff (`Simulation.cs`, JumpSquat expiry). Melee clamps takeoff speed to a per-character `jump_h_max_velocity`. A parameter dump of Ultimate's Terry caps takeoff at 64% of his run speed, 1.16 times his air speed. Wibou left the ground at 14 m/s with an air cap of 7 m/s, so his fastest aerial came out at about 12 m/s.
- There was no walk. A 0.1 s stick tap moved Wibou 2.24 m including the stop, about as far as his sword reaches.

## Decision

### 1. Run speeds

Every fighter's `movement.runSpeed` drops 15%. Wibou is first trimmed from 14 to 12.5 m/s, so his total cut is 24%.
Air speed, jumps, gravity and every other movement value keep their authored numbers.

| Fighter | Before (m/s) | After (m/s) | Body heights/s, capsule |
|---|---:|---:|---:|
| FightGuy | 12 | 10.2 | 6.0 |
| Wibou | 14 | 10.6 | 6.2 |
| Manki | 11.5 | 9.8 | 6.5 |
| Bashie | 11.5 | 9.8 | 6.5 |
| Salamander | 11.5 | 9.8 | 5.8 |
| Sir Bonk | 10 | 8.5 | 5.0 |
| Nyx | 10 | 8.5 | 5.0 |
| Nyxxie (Nyx's `alternateForm`) | 13 | 11.1 | 6.5 |

### 2. Jump takeoff cap

An ordinary grounded jump leaves the ground with its horizontal speed clamped to `min(RunSpeed, 1.2 × AirSpeedMax)` (`Simulation.JumpTakeoffAirSpeedRatio`).
Slide jumps keep `DownActionTuning.JumpCapRatio` (1.15 × RunSpeed) and Bashie's shell hop keeps her skate speed.
Jump squat, double jumps and air drift are unchanged.

### 3. Walk

`InputState.Walk` is held intent, carried in bit `0x40` of the flags byte, which was the reserved legacy Burst bit.
The exact protocol version in byte 21 prevents an older client's meaning from reaching a newer server.
`WithoutEdges` keeps Walk because it is held, not an edge.
The World Snapshot input record stores Walk in bit `0x40` of its DashPressed byte, so the snapshot layout and version stay the same and older snapshots decode with Walk off.

While grounded locomotion has movement input and Walk is set, the target speed is W = `GroundSpeedTuning.WalkRatio` × RunSpeed, with `WalkRatio` 0.4.

- Below W, speed becomes W on that tick, in the input direction.
- Above W, speed brakes toward W by RunSpeed/20 per tick, the release rate, while the direction follows the input.
- Releasing Walk with movement held continues the ordinary Run ramp from the 60% start.
- Releasing movement uses the ordinary release rule. A walk stops in 8 ticks.

The state stays `ActionState.Run`. The Run clip already plays at horizontal speed divided by RunSpeed with a 0.35 floor, so a walk plays it at 0.4 speed.
Walk changes nothing else: DI, SDI, air movement, attacks, Shield, grabs, dashes and jumps ignore it.
Run-attack carry clamps the current speed, so an attack from a walk carries walk speed.
Slide still needs 0.65 × RunSpeed, so Down while walking crouches.

Clients set Walk while the rebindable **Walk** action is held (keyboard default Left Ctrl) or while the processed movement stick magnitude is above zero and at most 0.7.
CPU opponents, course bots and scripted inputs leave Walk false.

### 4. Records and protocol

The takeoff cap and Walk are rule changes, so `ArcadeRecordCompatibility.RulesRevision` becomes 13 and `CourseRulesRevision` becomes 7.
The run speeds are package data, which the record fingerprint already detects.
`SimulationProtocol` lists both rules and the input bit under Unreleased; the release bump numbers them.

## Considered options

- **Walk from the stick magnitude in `MoveX`/`MoveY`, with no new bit.** Rejected. DI, SDI and ASDI read the raw axes, so a held walk key would weaken DI.
- **A Walk action state or a `CharacterState` flag for a 3-tick walk stop.** Rejected for now. The release rule already stops a walk in about 0.24 m at FightGuy's new speed, and a state flag changes the World Snapshot layout.
- **A per-character takeoff maximum, as in Smash.** Deferred. One ratio of air speed is enough to judge the feel. Per-character data can follow.
- **The roster cut alone, or Wibou's trim alone.** The owner chose all four changes together.

## Consequences

- Running jumps get shorter for every fighter whose run speed exceeds 1.2 × air speed: FightGuy, Wibou, Sir Bonk, Nyx and Nyxxie. Manki, Bashie and Salamander have 9 m/s air caps, so their takeoff is limited by run speed alone.
- Course gaps, course bots and Slop Rally times were tuned to the old speeds. Recheck finishability after the owner accepts the feel, as after the 2026-10-28 retune.
- Stages take 17% to 32% longer to cross. Nyxxie is now faster than Wibou.
- A slide jump now leaves much faster than an ordinary jump, which makes it the long-jump technique. Keep or cap it after play.
- Walking reuses the slowed Run clip. Per-fighter walk clips are future work.

## Build notes

Built 2026-10-31 in the shared checkout and cooked for all seven packages through the Unity gateway. The numbers below are measured in the real simulation with `MovementReport`.

| Fighter | Run (m/s) | Walk (m/s) | 0.1 s tap, run / walk, with stop (m) | Walk stop (m) | Takeoff cap (m/s) | Running jump, before / after (m) |
|---|---:|---:|---:|---:|---:|---:|
| FightGuy | 10.2 | 4.08 | 1.63 / 0.65 | 0.24 | 8.4 | 5.67 / 5.16 |
| Wibou | 10.6 | 4.24 | 1.70 / 0.67 | 0.25 | 8.4 | 6.12 / 5.04 |
| Manki, Bashie, Salamander | 9.8 | 3.92 | 1.57 / 0.62 | 0.23 | 9.8 | 7.57 / 7.50 |
| Sir Bonk | 8.5 | 3.40 | 1.36 / 0.54 | 0.20 | 7.2 | 6.37 / 5.83 |
| Nyx | 8.5 | 3.40 | 1.36 / 0.54 | 0.20 | 8.4 | 6.93 / 6.79 |
| Nyxxie | 11.1 | 4.44 | / 0.70 | 0.26 | 9.6 | / 5.23 |

Before this change, a 0.1 s tap moved Wibou 2.24 m. A walk stops in 8 ticks, and braking from a run to walk speed takes 12.

- The full Shared suite passed, 4,564 of 4,564, on a frozen copy of the working tree, taken because other threads were cooking at the same time.
- Course finishability passed 59 of 59, with 91 of 91 route checks.
- The Unity compile was clean. Fourteen virtual-device cases on the real Training `InputController` passed: Left Ctrl with WASD, a stick at magnitude 0.5 walks, a stick at 1.0 runs, and blocked or chat input sets no Walk.
- The World Snapshot decoder rejects unknown bits in the DashPressed and Walk byte, as its other flag bytes do.
- `HumanInputRebindingSelfTest.Run` stops at its first mouse capture, where Slot1 captures nothing, before any Walk step. That capture path does not read Walk. Whether it failed before this change is unverified. The Walk persistence checks pass on their own.
