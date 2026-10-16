# ADR-0021: Melee-Based Frame Timing

**Status:** Accepted — 2026-08-13 (wayfinder map #128, destination)
**Deciders:** @Binoui
**Related:** ADR-0016 (keyboard-first input — the 6-tick buffer), ADR-0014 (Burst — the committal-escape exception), ADR-0019 (hit response), ADR-0020 (movement)

## Context

The map's timing arc ([#128](https://github.com/Binoui/SlopArena/issues/128)): IASA policy [#138](https://github.com/Binoui/SlopArena/issues/138), landing lag + auto-cancel [#139](https://github.com/Binoui/SlopArena/issues/139), grounded by the Melee frame analysis (`docs/research/melee-frame-analysis.md`, `melee-frame-data.md`/`.json`). **Both engines already landed before this ADR** — `IasaTicks` (issue #124) and `LandingLagTicks` + `AutoCancelBefore/AfterTicks` (issue #125) — so this ADR locks the **policy** and the two interactions the tickets surfaced. L-cancel is ruled out ("feel without tech", map #128).

## Decision

### 1. IASA — authoring policy ([#138])

- **Normals** (LMB, aerials, tilt/attack stages, attack-from-motion stages) author `IasaTicks` = **1-8 ticks before stage end** (Melee medians: jabs 64% of moves, aerials 56-80%, early-out 1-8; FightGuy stages already author 3-4t).
- **Specials / recovery / charge stages stay 0** — verified in the frame data: Melee specials have **no** IASA (0%). This is also what keeps specials' dash-cancel protection intact (see §3).
- **Dash unlocks on IASA**: `StartDash` gate becomes `AnimLockTicks == 0 || IsIasaUnlocked` — jab → IASA → dash → dash-attack is a bread-and-butter string (pairs with ADR-0020 §2's attack-from-motion). NilusEventHorizon-style +1 locks are unaffected (their stages author no IASA).
- **Input buffering already present** (verified): `InputBufferWindow = 6` (`Simulation.cs:24`, `:399-411`) holds a press within 6 ticks of unlock; the IASA interrupt consumes it when the window opens (`ServerSimulation.cs:582-602`), with the blocked-press-never-cancels guarantee. Window length (6 vs Melee's 10) is a balance-pass lever. One integration check in the ADR build: the line-316 buffer-consume path requires `State == Idle`, the IASA path handles `Attacking` — the two must not double-fire.

### 2. Cooldowns ([#138])

- **Normals = 0 cooldown — policy locked.** Commitment + IASA is the only gate (Melee-faithful: Melee normals have no cooldowns). Current data already reflects this.
- **No charge normals** — charge mechanics are **specials-only** (user: charge belongs on specials). **Post-decision (2026-08-13): the RMB is removed as an attack slot entirely** — it is the camera-lock toggle (ADR-0018), no attack. The `ChargeAttack` ability classes are kept as scaffolding for a future special; the RMB/AirRMB specs and tests are deleted.
- **Special cooldowns kept** — the deliberate SLOPA deviation (60-600 tick resource gating, keyboard-friendly; Melee specials have none).

### 3. Landing lag + auto-cancel ([#139])

- **Full Melee AC model**: every standard aerial authors both windows — `AutoCancelBeforeTicks` (startup, 4-9t) and `AutoCancelAfterTicks` (~65-80% of the stage). Land ≤before or ≥after → the aerial **ends**, the player acts immediately. The after-window is the **SHFFL-equivalent pressure tool** (short hop + fast fall, ADR-0020 §3 → land in the tail = lag-free aerial).
- **Landing lag = L-cancelled scale as base**: 6-16t, median ~9-10. Melee's base (12-50, median 19) was balanced around L-cancel existing; with L-cancel ruled out, the base *is* the cost → **L-cancel by default** (modern Melee-like convention). Authored per aerial in the balance pass.
- **The aerial always ends on landing** (Melee): AC window → free; lag zone → aerial **ends** + hard lock, act when lag expires. Today's behavior (lock rides on top of the stage's remaining recovery + IASA — double cost) gets the AC-branch cleanup applied to the lag branch.
- **No empty stages**: every air stage declares `LandingLagTicks` + AC windows by authoring policy; the all-zero "lands and keeps playing" path is a cleanup artifact, not a designed escape hatch.
- **Landing frame stays pre-lock for committal escapes — by design** (user: "burst supersedes and cancels all lag; it has a long cooldown and is committal for that reason"): the only presses that pass the other gates on the landing frame are **burst** (offensive cancel — costs the ~60s committal cooldown, ADR-0014) and **air-jump** (costs a double jump). Dash and abilities stay blocked by their own gates — no free cancels. The "known 1-frame limitation" comment (`ServerSimulation.cs:424-427`) is reworded to designed behavior.
- **IASA does NOT bypass landing lag** (Melee-correct, verified `ServerSimulation.cs:486-488`; dash likewise `Simulation.cs:375`). AC windows are strictly better than IASA on those landings; IASA keeps its mid-air role.

## Considered Options

- **IASA on specials** — rejected: Melee specials have no IASA (verified 0% in the frame-data table); specials keep full commitment (#138).
- **Charge on normals** — rejected by the user: charge is a specials mechanic; no charge normals (#138).
- **Melee base landing lag (median 19)** — rejected: balanced around L-cancel, which is ruled out; the L-cancelled scale is the base (#139).
- **Auto-cancel after-window only** — both windows adopted; the before-window costs one field and covers rising/platform landings (#139).
- **Strict locked landing frame** — rejected by design: committal escapes (burst, air-jump) may cancel on the landing frame because they cost real resources (#139).

## Consequences

- **`StartDash` gate change** — the only engine touch from #138 (`AnimLockTicks == 0 || IsIasaUnlocked`).
- **Lag-zone landing cleanup** — the lag branch ends the aerial like the AC branch (end ability + lock); reword the 1-frame comment; verify the Idle-consume / Attacking-IASA double-fire check.
- **Kit authoring pass** (balance-pass content, temp data): fill `IasaTicks` on Kistu/Manki/Nilus normals; populate `LandingLagTicks` + both AC windows on all aerials; RMB removed as an attack (charge stays specials-only, scaffolding kept).
- **Goldens shift** where IASA, dash-cancel, and aerial-landing scenarios exist → regenerate (REGENERATE_GOLDENS).
- **0B netcode** — all fields are per-stage static data or sim-internal locks.
