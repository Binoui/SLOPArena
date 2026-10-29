# Hitstun + DI System

Current hit response is implemented in [`Simulation`](../../src/Shared/Simulation.cs) and [`ServerSimulation`](../../src/Shared/ServerSimulation.cs). [ADR-0019](../adr/0019-melee-based-hit-response.md) records the Melee-based design; this reference includes the later flight law and fixed-hitstun overrides.

## Hit Sequence

```
Attack lands → Hitstop (freeze, DI/SDI decision window) → Launch (KV, DI-rotated)
            → Hitstun lock (brief pop, then gravity; no ordinary actions) → ASDI at lock expiry
            → post-hitstun drift (linear friction + flight gravity) → landing / tech
```

1. **Hitstop** (`HitstopTicks`): contact freezes the defender and, for owner-freezing melee, the attacker. The defender commits DI input and one-shot SDI here. Burst is reserved and inert.
2. **Launch**: at hitstop exit, the queued knockback velocity (KV) is applied, rotated by DI (below).
3. **Hitstun** (`ActionState.Hitstun`): the no-action lock preserves the horizontal launch impulse except at stage contact. Upward sends receive four airborne ticks of gravity grace; then flight gravity applies during the lock. Control returns at lock end with residual speed. Landing never shortens the lock.
4. **Post-hitstun flight**: ASDI shifts position at lock expiry, then the fighter is actionable. Neutral horizontal braking and flight gravity use `Simulation.FlightFriction` and `FlightGravity`. The pop clock continues across lock expiry.

The 2026-10-11 flight update replaces ADR-0019 §6's gravity-free lock; see the
[roster comparison](../generated/knockback-flight-comparison.md). DI is unchanged.

## Knockback Formula (ADR-0019 §1)

```
rawMag = (base + growth·(P/100 + 1.0) + Damage·0.1) · 200/max(0.01, W + 100)
```

- `base` and `growth` are resolved per hitbox. [`KnockbackProfile`](../../src/Shared/KnockbackProfile.cs) retains Light, Medium, Launcher, Kill, Spike, Custom, and Adaptive. The Adaptive path derives pitch at contact in `SpellResolver`.
- Cooked package hitboxes project explicit angle/base/growth values through Custom in [`CookedCharacterRuntimeAdapter`](../../src/Shared/CookedCharacterRuntimeAdapter.cs). This does not remove the other Shared profiles.
- `P` is victim DamagePercent after this hit's damage; `Damage` is the hit's damage; `W` is `CharacterDefinition.Weight`.
- `CombatTuning.KnockbackScale` scales ordinary launch velocity after hitstun is calculated. Fixed-force launches omit that scale and the hitstun magnitude bonus.
- Throws exist. `ServerSimulation.UpdateCoupledInteractions` uses victim weight, percent, and combat tuning for horizontal launch, then sets vertical flight timing for the paired throw. See [ground grab and forward throw](combat-systems.md#ground-grab-held-movement-and-forward-throw).

## Hitstun (ADR-0019 §2)

```
hitstun = clamp((int)(tuning.HitstunCoefficient ·
    (rawKbMag + tuning.HitstunMagnitudeBonus)), 1, ushort.MaxValue)
```

- `rawKbMag` is the length of the resolved launch vector before velocity scaling. The ordinary damage-formula lock uses the owning simulation's [`CombatTuning`](../../src/Shared/CombatTuning.cs). Fixed-force launches use their resolved magnitude without the bonus.
- `StunTicks` is a zero/nonzero gate. Zero admits no Hitstun, including on a launch-bearing shove. A zero-magnitude launch also creates no formula lock.
- A positive authored `fixedHitstunTicks` replaces an existing formula lock through `Simulation.ApplyFixedHitstun`, up to 240 ticks. Zero or omission keeps the formula duration. The override requires a nonzero stun gate and does not change launch velocity or bypass armor.
- `HitstunLevel` derives from the applied duration, including a fixed override, rather than authored `StunTicks`.
- Burst cannot escape the lock or cancel queued launch. Its wire input bit remains reserved and inert; ADR-0014 records the earlier design.

## Hitstop (ADR-0019 §3)

```
hitstop = clamp((int)((damage/3 + 6) · mul), 1, 12)
```

`ServerSimulation.ComputeHitstopTicks` reads the ability's `hitstop_multiplier` parameter, defaulting to 1. `ResolveHits` skips freeze for a pure push with no damage and a zero stun gate. Block Hitstop uses a separate reaction path without damaging launch, DI, or SDI.

## DI (ADR-0019 §4)

- Shared retains the latest nonzero world-plane movement input in `DIX`/`DIY` during `HitstopTicks`. `Simulation.ApplyDirectionalInfluence` rotates the launch at Hitstop exit. Without Hitstop, `ServerSimulation.ResolveHits` captures and applies input on the hit tick.
- With a horizontal launch component, DI rotates only azimuth toward the input by up to `18°·sin²(angle)`. Horizontal and total magnitudes stay unchanged. Perpendicular input gives full rotation; input along the launch gives none.
- Only an effectively vertical launch tilts by 18° toward the held direction, preserving total magnitude. Angled launchers and spikes keep their vertical component and rotate only in azimuth.
- Combo Influence is retired. ADR-0019 supersedes ADR-0013's additive drift design.

## SDI + ASDI (ADR-0019 §5)

- **One-shot SDI** applies the first nonzero input during Hitstop as a position shift. `CharacterState.SdiApplied` prevents further SDI shifts during that freeze. A hit without Hitstop applies SDI immediately from the hit-tick input.
- **ASDI** applies in `Simulation.ProcessHitstun` when `HitstunTicks` reaches zero, before the transfer to actionable flight. It uses the latest nonzero input retained through Hitstun, which can replace the Hitstop direction. It does not rotate the launch again.
- Both shifts use 0.4 times the world-plane input vector in metres, not a fraction of launch magnitude. Triangle-stage displacement goes through collision. ASDI requires a Hitstun expiry; a lock-free shove does not enter that path.
- [`SnapshotCodecs`](../../src/Shared/Snapshots/SnapshotCodecs.cs) writes and restores `DIX`, `DIY`, and `SdiApplied` as rollback state. The implementation has an explicit SDI flag; do not assume a historical character-packet byte count describes the current World Snapshot.

## Knockback During Flight

- **Pop window**: the first four airborne simulation ticks of an upward launch preserve its vertical impulse. Hitstop never consumes that window. Downward and flat airborne sends receive gravity immediately.
- **Hitstun**: horizontal KV remains constant; vertical KV follows flight gravity after the pop window. Stage contact projects velocity and permits landing/grounded carry without ending hitstun.
- **Post-hitstun**: the current KV transfers to movement velocity; gravity continues without restarting the pop window or applying twice on expiry. Neutral horizontal braking reduces vector magnitude by `Simulation.FlightFriction`, preserving heading and equal braking in every horizontal direction. The victim is actionable once the lock ends. While launch momentum remains (faster than `AirSpeedMax`, or the stick points against it), the stick cannot steer it: it only brakes, from `FlightFriction` (6 m/s²) up to `Simulation.HeldFlightBrake` (8 m/s²) when held straight against the launch, and ordinary air drift resumes once the momentum is spent. Full air acceleration used to brake at 12–24 m/s² and cancelled about half of a kill launch; 8 m/s² cancels about a fifth, nearer Smash and Melee, where roughly 90% of a launch happens inside hitstun.
- **Gravity** uses `Simulation.FlightGravity` across both phases, with the victim's maximum fall speed. An already stronger downward launch is preserved rather than slowed to that limit.
- **Lock-free launch** uses the same pop/gravity law with linear horizontal braking in `Simulation.ProcessKnockback`.
- **Rollback state** includes `AirTimeTicks` for the pop clock and `InPostHitstunFlight` for flight handling. `SnapshotCodecs` preserves both across restore.

## Tuning and verification

`CombatTuning` owns launch scale, hitstun coefficient, and magnitude bonus for each simulation. Character content owns weight and per-hit launch values. `Simulation` owns `KnockbackPopTicks`, `FlightGravity`, `FlightFriction`, `HeldFlightBrake`, DI rotation, and SDI/ASDI displacement. `ServerSimulation.ComputeHitstopTicks` owns the damage-based freeze with the ability's `hitstop_multiplier` parameter.

Focused source coverage includes [`KnockbackFlightTests`](../../tests/Shared.Tests/KnockbackFlightTests.cs), [`HitstopTests`](../../tests/Shared.Tests/HitstopTests.cs), [`ComboInfluenceTests`](../../tests/Shared.Tests/ComboInfluenceTests.cs), [`ArmorCombatTests`](../../tests/Shared.Tests/ArmorCombatTests.cs), [`BurstTests`](../../tests/Shared.Tests/BurstTests.cs), and [`WorldSnapshotTests`](../../tests/Shared.Tests/WorldSnapshotTests.cs). `ComboInfluenceTests` retains its historical filename but tests current rotational DI and SDI. Protect the specified mechanic with focused assertions; whole-state golden comparison has been retired.

## Animation Tiers

Simulation derives `HitstunLevel` from the applied lock duration, including fixed overrides. Levels 0, 1, and 2 select small, medium, and hard reactions. The client maps the level to the package's semantic hit-reaction bindings and plays the resolved clip through Animancer at the server-provided duration.

Strong launches (at least 24 ticks of applied hitstun, sent at least 15° upward) play only the first 10 ticks of that clip at normal speed. Then the package's `presentation.tumble` clip takes over: a one-shot build-up, then a loop that lasts until the fighter acts or lands. This is presentation only; see the [tumble design](../design/airborne-tumble-animation.md).

## References

- [ADR-0019](../adr/0019-melee-based-hit-response.md) records the hit-response design; current flight and override behavior is described above.
- ADR-0012 records contact Hitstop, ADR-0013 records superseded Combo Influence, and ADR-0014 records the retired Burst design. ADR-0020 covers movement and ADR-0021 covers frame timing.
- [`../research/melee-knockback-model.md`](../research/melee-knockback-model.md), [`../research/melee-frame-analysis.md`](../research/melee-frame-analysis.md) — Melee research.
- Ticket lineage: [#130](https://github.com/Binoui/SlopArena/issues/130) flight, [#131](https://github.com/Binoui/SlopArena/issues/131) hitstun, [#132](https://github.com/Binoui/SlopArena/issues/132) KB formula, [#133](https://github.com/Binoui/SlopArena/issues/133) DI, [#134](https://github.com/Binoui/SlopArena/issues/134) SDI, [#143](https://github.com/Binoui/SlopArena/issues/143) hitstop.
