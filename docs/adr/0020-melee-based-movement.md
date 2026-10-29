# ADR-0020: Melee-Based Movement

**Status:** Accepted 2026-08-13. Ground movement amended 2026-10-27 after the owner approved the Progressive prototype, and retuned 2026-10-28 (Start 60%, Ramp 12).
**Deciders:** @Binoui
**Amends:** ADR-0001 (gravity — §3, three-phase ramp → float-window-only)
**Related:** ADR-0015 (timing-based defense), ADR-0016 (keyboard-first input), ADR-0017 (facing), ADR-0018 (target lock), ADR-0019 (hit response), ADR-0021 (frame timing)

## Context

The map's movement arc ([#128](https://github.com/Binoui/SlopArena/issues/128)): ground movement [#136](https://github.com/Binoui/SlopArena/issues/136), air movement [#137](https://github.com/Binoui/SlopArena/issues/137), ledge options [#144](https://github.com/Binoui/SlopArena/issues/144), grounded by the movement audit [#135](https://github.com/Binoui/SlopArena/issues/135) and the netcode impact scan [#140](https://github.com/Binoui/SlopArena/issues/140). Keyboard movement is digital and SOCD-canceled. Controller movement uses the same speed rule, with optional client-side 8-way stick snapping under ADR-0016. Melee is the base; 3D + free camera means the spacing game differs (ADR-0015), so movement is timing- and commitment-driven.

## Decision

### 1. Current terminology

- **Run** is the single ground locomotion tier. Progressive describes its acceleration rule, not a selectable model.
- **Turnaround** is an immediate direction reversal that retains current speed and continues acceleration. It has no skid or timing window.
- **Rush** is retired. There is no instant full-speed start, dash-dance window, or special tap-release stop.
- **Dash** names the universal homing dash and kit-authored mobility. Shield and Air Dodge own grounded and airborne defense. Dash overspeed remains separate from ordinary Run acceleration.

### 2. Universal ground movement, amended 2026-10-27

Every fighter and mode uses the same immutable constants in `GroundSpeedTuning`.
GameServer, client prediction, Training, Solo, Arcade, and replay playback all run this Shared rule.
Only the character's `RunSpeed` changes the absolute speed.

Let R be RunSpeed and S = 0.6*R. With held movement and speed below S, the first moving tick sets speed exactly to S.
Each later tick adds `(R-S)/12 = R/30`, clamped to R.
The first completed tick is therefore 60%, and completed tick 13 reaches full speed, about 217 ms after input begins at 60 Hz.
The 12-tick ramp counts the increments after the initial step.

Held turns redirect velocity immediately, retain 100% of current speed, and continue the same increment.
A reversal during coast follows the same rule. Speed below S returns to S without an extra increment on that tick.
Facing follows input. Tilt magnitude does not select a lower speed.
Keyboard stays digital. Stick 8-way snap is a persisted Controls preference, default on, applied before simulation.
Stage collision retains its tangent-slide handling so a held diagonal does not repeatedly lose its unblocked component.

Release at or below RunSpeed subtracts R/20 from the horizontal velocity magnitude each tick, preserving direction until standstill.
A full-speed release stops in 20 ticks and travels `19*R/120` metres after release.
Short taps brake too. There is no Rush stop.
Above RunSpeed, retain the shipped dash coast, component-wise release brake, and overspeed pivot to RunSpeed.
A one-millionth tolerance at the start floor prevents repeated floor ticks after a rounded direction projection. The same tolerance at the speed cap prevents diagonal rounding residue from selecting the overspeed branch.
Low posture, attack commitment, air movement, collision, and jump admission keep their existing owners.

Steam Deck playtests with non-fighting-game players on 2026-10-24 and 2026-10-25 motivated the movement comparison.
The owner tried the Training prototype on 2026-10-26/27 and approved Start 40%, Ramp 20 Linear, TurnaroundKeep 100%, Stop 20 on 2026-10-27.
Tiers, Hybrid, variable curves, and live tuning are removed after that choice.
After playing the Steam release (playtest.37) on 2026-10-28 the owner found the start too slow and picked Start 60%, Ramp 12; Stop 20 is unchanged.

Figures below are computed from the rule on a flat floor (the 40%/20 figures were MovementProbe-measured and matched the rule exactly).
Tick zero is standstill. Held distance includes all 6, 12, or 18 moving ticks. Total includes every braking tick.
FightGuy at R = 12 m/s starts at 7.2 m/s, reaches full speed on tick 13 over 2.080 m, and stops from run in 20 ticks over 1.900 m.
Its 0.1, 0.2, and 0.3 second pushes travel 0.820/1.920 m, 1.880/3.653 m, and 3.080/4.980 m while held/through standstill
(40%/20 was 0.570/1.120, 1.356/2.350 and 2.358/3.924 m).
A cruise reversal is 12 m/s in the new direction on its first tick, over -0.200 m.
These are simulation distances and timings, not hardware latency or a claim about player acceptance.

Reproduce the roster measurements with:

```sh
dotnet run --project tools/MovementReport -p:SkipUnityPluginCopy=true -- --ground-speed --repeats 5 --json tmp/movement-final-20261027/curves --out tmp/movement-final-20261027/measurements.md
```

Simulation protocol 23 rejects peers running the old movement rule. No ramp counter or snapshot field is added.
The `CharacterState.RushTicks` ushort remains reserved in snapshots and legacy packets, without runtime reads or updates.
The source/cooked `rushTicks` field and old acceleration coefficients remain inert compatibility data. Existing package bytes and roster pins do not need a cook.
Slopball record revision 11 and Course record revision 5 separate prior movement categories without deleting personal bests.
Master admission must move to protocol 23 with the client and GameServer at release.

### 3. Air movement ([#137], completes the walk-death from §2)

- **Per-char `AirSpeedMax`**, decoupled from WalkSpeed: Manki 6.5 / FightGuy 7.5 / Kistu 8.5 / Nilus 7.0 (≈0.55-0.65 of run).
- **`AirAccelStick` + `AirAccelBase`** (base ≈ 0.2× stick); **linear friction** replaces multiplicative `AirDrag`; per-char `AirFriction` everywhere.
- **Fast fall = set-velocity**: per-char `FastFallSpeed` ≈ 1.15-1.25× `MaxFallSpeed`, `VY = -FastFallSpeed` on Down+airborne+falling (the SHFFL enabler); per-tick gate, no latch; `FastFallGravityMultiplier` deleted.
- **Jump model**: `ShortHopForce` own stat 0.58-0.62× `JumpForce` (release-window trigger kept); **double jump = weaker additive impulse** (`VY = JumpForce·AirJumpVMultiplier` ~0.8, `VX += dir·AirJumpHMultiplier` ~0.85).
- **Gravity — Option A (user choice)**: the float window survives **only** for recovery/post-hit states; `FallRampDuration` lerp machinery **deleted**; ADR-0001 amended (§3 ramp → float-window-only). ADR-0002 (jump-arc anim) untouched.

### 4. Ledge ([#144])

- **Ledge hang adopted, full kit deferred** (user: "not sure if we want full kit yet but we probably should have some sort of ledge hang").
- New **`LedgeHang` ActionState**: grab via the existing `TryLedgeSnap` geometry (0.8m cardinal search, ≤2.5m below the edge), **invincible 4-8 ticks on grab**, **full refresh on re-grab** (Melee-faithful; no anti-plank — small arenas make planking self-defeating; an Ultimate-style refresh cooldown is a balance-pass lever).
- **No auto-getup — hang until you act** (Melee; stalling is the edgeguarder's punish window).
- **Grab gate = actionable AND not flying** (`HitstunTicks == 0 && !HasKnockback`, unchanged): SLOPA deliberately does NOT adopt Melee's tumble auto-grab ("lucky ledge grab") — in free-camera 3D a flight grab from an off-screen angle reads as random; recovery must let the flight decay (ADR-0019 §6) before the ledge is available.
- **The +5 auto-pop dies** — the hang replaces it (KV cleared, `VY = 0` on grab). Hang ≠ landing: excluded from landing-lag via the existing `VY <= 0` guard.
- **Escapes now** (all existing keys, zero new inputs): **S = drop, jump = ledge jump, W = stand**.
- **Getup attack + getup roll deferred** with the full-kit question → the landing/tech foundation (fog: missed-tech → knockdown → getup), the natural home for getup-state work.
- **Single-occupancy ledge (ledgehog) — assumed from Melee, flagged un-grilled**: one fighter per edge; a second grab fails and the would-be grabber falls past. `TryLedgeSnap` gains an occupancy check.

## Considered Options

- **Keep Melee's dash as a locomotion tier** — rejected by terminology lock: SLOPA's Dash is a *mechanic*; Run takes the locomotion slot (#136).
- **Dash-end hard stop vs friction coast** — initially friction coast (Q1=B), then **reversed in v2**: the dash became a wavedash-like burst, so the grounded dash hard-stops and only the aerial dash preserves momentum (#136).
- **Multiplicative AirDrag vs linear friction** — multiplicative rejected: asymptotic tail never settles; linear friction + dead-zone snap lands cleanly (#137).
- **Three-phase gravity ramp vs float-window-only** — the audit showed the ramp machinery mostly dead (recovery/post-hit only); Option A: float window for recovery/post-hit, ramp deleted (#137).
- **Full Melee ledge kit vs hang** — full kit not yet wanted; hang + basic escapes decided now, aggressive getups deferred (#144).

## Consequences

- **Ground cleanup, amended 2026-10-27**. `ActionState.Run` remains the only locomotion tier. Rush has no gameplay use. Its ushort layout slot remains reserved; `DashDurationTicks` stays with existing mobility content.
- **All §3 fields are MovementStats** — zero wire change (per-character static data).
- **`LedgeHang` = new ActionState value** — the state byte already exists on the wire (**0B netcode**); the rollback classifier marks it **Complex** (like AirDodging — never rebuilt from snapshots); invincibility is a server-side off-wire timer; landing-lag exclusion.
- **`TryLedgeSnap` → grab path** (state set, KV cleared, VY=0) replacing the pop, **+ occupancy check** (§4).
- Run animation playback follows actual horizontal speed with a 0.35 floor for everyone. The historical ledge animation work below is separate from this ground change.
- Behavioral tests and deterministic snapshot replay cover the new movement rule. Regenerate stored goldens only through their maintained tooling when affected.
- **Balance pass** (post-design): RunSpeed/accel, per-char air stats + friction curves, fast-fall/short-hop multipliers, weights.
- ADR-0001 amended (gravity); `docs/systems/combat-systems.md` updated.
