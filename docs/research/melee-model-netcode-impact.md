# Melee Model Netcode Impact — CharacterState Fields, Packet Layout, Rollback Safety

> Feeds: final ADR ticket (#142), DI/SDI design tickets (#133/#134). Companion to
> [`melee-knockback-model.md`](melee-knockback-model.md)
> (the design source for items 1-5 below) and issue #128 (the design map). SlopArena
> file refs are relative to the repo root (`src/Shared/…`); Melee refs are relative
> to `melee-decomp/src/melee/`. Claims marked [verified] were read in the decompiled
> C; `PlCo.dat` numeric values are marked [community] (structure verified, values
> community-documented — see melee-knockback-model.md §7).
>
> **Question answered**: which new `CharacterState` fields and `CharacterStatePacket`
> changes the Melee-based feel engine requires, and which of those must enter the
> rollback snapshot. Bottom line up front: **everything fits the current wire except
> a +2-byte SDI stick-window timer pair**; no new packet version is needed (the
> protocol is append-only and has no version field); Tumble/Run/Weight/DI need zero
> wire bytes if Tumble is classified like Hitstun (Complex).

---

## 1. What the feel engine adds (design map §6, ranked)

| # | Delta (melee-knockback-model.md) | Netcode-relevant surface |
|---|---|---|
| 1 | Flight model: constant KV during hitstun + linear horizontal friction after (§3.3 Option A) | KV stays sim-local; observable velocity mirrors into `V` (already on the wire). **0 bytes.** |
| 2 | Hitstun = pure KB function, min 1 (§2) | `HitstunTicks` already exists; formula change only. **0 bytes.** |
| 3 | KB formula: weight divisor + move-damage term (§1.3) | Weight = static per-character data; result lands in KV→V. **0 bytes.** |
| 4 | DI at connect (rotate launch) + SDI per-hitstop-tick (§4.4) | DI rides existing `DIX/DIY` (off-wire, derived). **SDI needs 2 per-axis stick-window timers = +2 bytes.** |
| 5 | Weight stat (§5) | Static `CharacterDefinition` data on both sides. **0 bytes.** |
| 6 | Ground movement: dash→run→pivot (issue #128 "Not yet specified") | One new `ActionState` enum value; all run-FSM bookkeeping is already wired (D10/ADR-0011). **0 bytes.** |
| 7 | Tumble state after hitstun (issue #128; §3.3 calls it "tumble") | New `ActionState` value (0 bytes) **iff classified Complex like Hitstun**; +12 bytes if we want it Predictable (see §4.3). |

---

## 2. Ground truth today (verified sizes)

### 2.1 Wire stack

- **Client → server**: `entityId(8) + tick(4) + InputState(20)` = 32 B. `InputState.Size = 8+1+1+2+2+2+2+1+1` (`src/Shared/InputState.cs:62`).
- **Server → client per entity**: `entityId(8) + tick(4) + CharacterStatePacket(109) + hasInput(1) + InputState(20)` = 121 B envelope base / 122 B no-input / 142 B with relayed input (`src/Shared/ServerEntityPacket.cs`; `BaseSize = 8 + 4 + CharacterStatePacket.Size`).
- **CharacterStatePacket.Size = 109**, offsets 0–108 fully packed with no padding (`src/Shared/CharacterStatePacket.cs`; `Serialize` writes through `buffer[108]`). Size is locked by the packet regression tests. Any addition is a deliberate bump of the constant, tests, and size comments.
- **No version field exists.** The established growth pattern is append-only and both sides ship together, so a size bump is the protocol convention rather than a versioned layout.
- **Packet-size contract**: current code and documentation agree on 109-byte `CharacterStatePacket`, 122-byte no-input `ServerEntityPacket`, and 142-byte relayed-input `ServerEntityPacket`.

### 2.2 What "rollback snapshot" means in this codebase

There is no generic snapshot struct — the snapshot is **the wire packet** for opponents and the **full-fidelity `CharacterState` history** for the self entity:

- **LocalTrack (self)** — continuous `ServerSimulation` fed true inputs; keeps a 30-tick `(Tick, State, Input)` history of full `CharacterState` structs (`src/Shared/Rollback/LocalTrack.cs:24-25,37-47`). Correction patches only the wire fields in place via `CharacterStatePacket.ApplyTo`, which deliberately preserves every non-wire field (`CharacterStatePacket.cs:346-364`; test: `CharacterStatePacketTests.cs:160-169`), then replays forward across a Predictable suffix only (`LocalTrack.cs:57-83`).
- **PredictedTrack (Predictable opponents)** — rebuild = `packet.State.ToState()` (non-wire fields → defaults) + re-sim with held-last inputs from the relay, 30-tick cap (`src/Shared/Rollback/PredictedTrack.cs:27,37-74`). **For opponents, "in the snapshot" == "on the wire"** — anything not serialized is zeroed at every rebuild and cannot be recovered.
- **RawTrack (Complex opponents)** — latest packet only, no re-sim (`src/Shared/Rollback/RollbackSimulator.cs:32-39`).
- **Partition** — `IsPredictable` = Idle / Dashing / JumpSquat / AirDodging only (`src/Shared/Rollback/ActionStateClassifier.cs:15`). Hitstun / Attacking / Warping are Complex → RawTrack, never re-simulated client-side.
- **Input** — the server relays the *exact* `InputState` it consumed per entity per tick (`ServerEntityPacket.cs:46-60`; input buffering is tick-ordered, `src/Shared/Rollback/TickInputBuffer.cs:5-9,22-54`), so any sim state that is a deterministic function of the input sequence is re-derivable on both sides without wire carriage.

Consequence for this ticket: **"rollback-snapshot needed?" = "must the field be on the wire?"** for anything consumed while an opponent is in a Predictable state; self-side state is preserved by LocalTrack history automatically, and Complex-state fields never need the wire because RawTrack renders wire values directly.

---

## 3. Field-by-field table

| Proposed state field | Size | Serialized in packet? | Rollback-snapshot needed? | Determinism note |
|---|---|---|---|---|
| `SdiWindowTicksX`, `SdiWindowTicksY` (2 per-axis stick-window timers; Melee `x670`/`x671` [verified] `melee-decomp/src/melee/ft/chara/ftCommon/ftCo_Damage.c:575-600`) | 2 × byte = **+2 B** | **YES** — append (offsets 113–114) | **YES** — wire for opponents; LocalTrack history for self | Pure function of input history, but the value must survive the rebuild boundary: a zeroed timer on a rebuilt track accepts an SDI input the server's window rejects → predicted position diverges every hitstop tick (up to 24, `ServerSimulation.cs:1048`). Same rationale as serialized `JumpHeldTicks` (`CharacterState.cs:42`; ADR-0016). One counter would suffice for the digital 8-way stick (direction is in the current input); 2× byte is the byte-exact mirror of the decomp. |
| `DIX`, `DIY` (exist today, `CharacterState.cs:136`) | 2 × float = 8 B (existing, off-wire) | NO | NO | Derived from relayed `InputState.MoveX/MoveY` during HitstopTicks (`Simulation.cs:175-176`) and hitstun (`Simulation.cs:636-639`); consumed at expiry (today: Combo Influence `Simulation.cs:643-651`; Melee model: launch rotation at freeze exit — see §4.2). Deterministic. Precedent: the whole Queued\* launch payload is deliberately server+local-sim only (`CharacterState.cs:123-126`). |
| `HitstunTicks` (exists, `CharacterState.cs:119`) | ushort = 2 B (existing) | NO (unchanged) | NO | Pure-KB hitstun is a formula change only (`Simulation.cs:1118-1124` → `KB·0.4`, min 1 [verified] `ftCo_Damage.c:296-299`). Hitstun is Complex → RawTrack renders wire `V`; the client never re-sims it. KB cap 999 → ~399 ticks fits ushort. `HitstunLevel` (wire, `buffer[43]`, `CharacterStatePacket.cs:43`) keeps driving client anim tiers unchanged. |
| `ActionState.Tumble` (new enum value; `ActionState.cs:5-14` — append after `Aiming = 8`) | 0 B (byte enum already on wire at `buffer[28]`) | NO new field | **NO** if Complex (recommended — mirrors Hitstun); **+12 B (KVX/KVY/KVZ)** if Predictable | Residual KV + linear friction, `KVY` untouched by friction (`melee-knockback-model.md` §3.3). KV stays sim-local (`CharacterState.cs:113`); the observable drift mirrors into `V` every tick exactly like ProcessHitstun does today (`Simulation.cs:632-634`), so RawTrack renders it from the wire. A Predictable tumble would need KV on the wire: rebuilt tracks get `KV=0` and would mis-run friction and the `HasKnockback` gates (ledge snap `Simulation.cs:526`, defensive burst `Simulation.cs:153`). |
| `ActionState.Run` (new enum value) | 0 B | NO new field | NO | All run-FSM bookkeeping is already wired (D10/ADR-0011): `IsSprinting` (`buffer[95]`), `DirHoldTicks` (93–94), `TurnaroundTicks` (91–92), `LastDirX/Z` (96–103), `DashDurationTicks`/`DashDirX/Z` (75–84) (`CharacterStatePacket.cs:73-104`). Run sim = pure function of wire fields + input → can join `IsPredictable` (`ActionStateClassifier.cs:15`) with zero additions. Pivot = existing `TurnaroundTicks` (`CharacterState.cs:186`; `Simulation.cs:84-89`, `TurnaroundLagTicks = 6`); Melee's pivot is a timed sub-state with a stick-vs-facing threshold exit [verified] `ftCo_TurnRun.c:27-30` (run family: `ftCo_Run.c:81-143`, `ftCo_RunBrake.c`, `ftCo_RunDirect.c:21-59`). Run speed from static `MovementStats.SprintSpeed` (`CharacterDefinition.cs:15-35`). |
| `CharacterDefinition.Weight` (new static stat) | float = 4 B (static) | NO | NO | Static per-character data registered on both sides (same channel as `MovementStats`/ability specs, `CharacterDefinition.cs:41+`; PredictedTrack rebuild already receives defs — `PredictedTrack.cs:47-52`). Enters the KB formula (`melee-knockback-model.md` §1.3: `200/(W+100)`, normalized W=100→1.0); result lands in KV→V (wire). Hitstun is affected only *through* KB (Melee hitstun is pure KB, `ftCo_Damage.c:296` [verified]). |
| InputState changes for DI/SDI (none needed) | 0 B | NO | N/A | DI/SDI ride existing `MoveX/MoveY` (`InputState.cs:62`), which are digital 8-way (SOCD-canceled, −1/0/+1 per axis — `client/Unity/Assets/Scripts/Runtime/Input/InputController.cs:206-215`; ADR-0016 §5). The input relay already carries the exact consumed input (`ServerEntityPacket.cs:46-60`). |
| (optional) `KVX`, `KVY`, `KVZ` on the wire | 3 × float = **+12 B** | YES — only if Tumble is Predictable | YES (same condition) | Not required by the design; listed so the ADR can make the Complex-vs-Predictable tumble call explicitly (§4.3). |

---

## 4. Item-by-item detail

### 4.1 SDI — the "2 counters?" answer is: Melee uses 2, SlopArena needs 1–2

Melee's SDI gate [verified] (`ftCo_Damage.c:575-600`):

```c
if (fp->allow_sdi && VEC2_SQ_LEN(fp->input.lstick) >= SQ(p_ftCommonData->sdi_min_stick_mag) &&
    (fp->x670_timer_lstick_tilt_x < p_ftCommonData->sdi_stick_window ||
     fp->x671_timer_lstick_tilt_y < p_ftCommonData->sdi_stick_window))
{
    fp->cur_pos.x += lstick.x * sdi_pos_scale;  fp->cur_pos.y += lstick.y * sdi_pos_scale;
    fp->x670_timer_lstick_tilt_x = 254;         fp->x671_timer_lstick_tilt_y = 254;
}
```

Two **per-axis** tilt timers (`x670` X, `x671` Y), both reset to 254 on an accepted SDI; the gate is "either axis tilted within `sdi_stick_window`" (~4 frames [community]) plus `allow_sdi` and a stick-magnitude floor (`sdi_min_stick_mag`, `sdi_pos_scale` — `PlCo.dat`, values [community]).

- SlopArena's stick is digital 8-way (`MoveX/MoveY ∈ {−1,0,1}`, `InputController.cs:206-215`), so a **single `SdiWindowTicks` counter (1 byte, ticks-since-last-SDI)** reproduces the window logic exactly: any nonzero `MoveX/MoveY` is the tilt. 1 byte, not 2.
- **2 × byte is still the better mirror**: it matches the decomp field-for-field, costs 1 extra byte, and keeps the door open for a future analog controller (ADR-0016 says controllers map to the same slots later). Recommend 2 × byte; the ADR can pick either — both fit.
- The shift target (`PX/PY/PZ`) is already a wire field — **only the timer needs carriage**. The shift itself is `pos += stick · sdiScale`, deterministic.
- Hitstop length bounds the SDI budget: freeze = `2 + 2·damage`, cap 24 (ADR-0012; `ServerSimulation.cs:1048-1059` `ComputeHitstopTicks` call site). Melee scales hitlag with damage too (`ft/ftcommon.c:646-649` [verified] — cited in melee-knockback-model.md §Scope); issue #128 keeps "hitstop length model + SDI window interaction" as an open question for #133.

### 4.2 DI at connect — fields exist, consumption site changes

- Capture half is **already implemented**: `DIX/DIY` are written from `input.MoveX/MoveY` during the hitstop freeze (`Simulation.cs:175-176`, added by ADR-0012) and during hitstun (`Simulation.cs:636-639`).
- Consumption half today: additive Combo Influence at hitstun expiry — `KVX += DIX · 0.30 · launchMag` (`Simulation.cs:643-651`; ADR-0013).
- Melee model (melee-knockback-model.md §4.4.1): rotate the **launch vector** toward the stick at hitlag exit, magnitude preserved, up to `θ_max` = 18° weighted by stick²·sin² [verified] `ftCo_Damage.c:605-640` (x1A8 [community]). In SlopArena terms that is: read `DIX/DIY` at **freeze expiry** and rotate the queued launch before `ApplyKnockback` — a one-shot transition at exactly the existing queue-application point (`Simulation.cs:179-229`).
- Netcode: zero change. `DIX/DIY` are derived from the relayed input; the rotated launch lands in KV, which is mirrored into the wired `V` every tick (`Simulation.cs:632-634`). With a digital 8-way stick, sin² weighting becomes a discrete function of the 8-way direction — deterministic by construction.
- Decision to surface in #133: **drop or keep Combo Influence** (melee-knockback-model.md §4.4.3). Both applied = double drift; netcode-neutral either way.
- Note: ADR-0013's stated reason for rejecting rotational DI ("needs relative-angle math in 3D") is now moot for the keyboard floor — an 8-way discrete rotation in the launch plane is trivial; the ticket #133 grilling should re-check that rejection.

### 4.3 Hitstun model + Tumble — the one real design fork

- **Hitstun**: `HitstunTicks` (ushort) is sufficient; "pure KB function" is a server-side formula change (`Simulation.cs:1118-1124`). The current 8-tick floor and 60-tick ceiling (`8 + 0.5·kbMag`, clamp 8–60) and the per-move `StunTicks` cap are the design levers (melee-knockback-model.md §2.3) — none touch the wire. `HitstunLevel` (tier byte, wire) is unaffected.
- **Tumble**: new `ActionState` value (append to the enum, `ActionState.cs:5-14` — appending keeps existing serialized ids stable). Residual KV + linear horizontal friction (`KVXZ -= friction·TickDt`, snap below `VelocityDeadZone = 0.015` `Simulation.cs:91`), `KVY` untouched, gravity does vertical work (melee-knockback-model.md §3.3 Option A; Melee linear friction [verified] `ft/fighter.c:2172-2183`).
  - **Complex (recommended, 0 B)**: classify like Hitstun → RawTrack. The client renders the wire `V`/position directly; the sim mirrors KV→V each tick (precedent `Simulation.cs:632-634`). Cost: the post-hitstun drift phase stops being client-re-simmed (today it is Predictable-Idle with residual `V`) — a small prediction-coverage regression on the drift tail, correctness-neutral (RawTrack shows the same wire values, just without local extrapolation).
  - **Predictable (+12 B)**: would require KVX/KVY/KVZ on the wire so a rebuilt track re-derives friction and the `HasKnockback` gates correctly (ledge snap `Simulation.cs:526`, defensive burst `Simulation.cs:153`). Predictable tumble would let clients extrapolate the drift tail — nicer for feel, costs 12 bytes and one more wire invariant to keep byte-identical.
  - Melee tumble has no timer (ends on landing/wake-up action — `ftCo_DamageFall.c:44-59`, exit via `ftCo_80090984` [verified]) → **no `TumbleTicks` field needed**; end conditions are input+physics-driven, deterministic.

### 4.4 Run (dash→run→pivot)

Melee splits ground movement into Run / RunBrake / TurnRun (pivot) / RunDirect (`ftCo_Run.c:81-143`, `ftCo_RunBrake.c`, `ftCo_TurnRun.c:27-30`, `ftCo_RunDirect.c:21-59` [verified]). SlopArena can express all of it with:

- **One new `ActionState.Run` value** (0 B — the state byte is already on the wire, `buffer[28]`).
- **Existing wired bookkeeping** (D10/ADR-0011 — added precisely so Predictable re-sim is byte-identical, `CharacterStatePacket.cs:79` comment): `IsSprinting`, `DirHoldTicks`, `TurnaroundTicks`, `LastDirX/Z`, `DashDurationTicks`, `DashDirX/Z`.
- **Existing static stats**: `MovementStats.SprintSpeed`, `GroundFriction` (`CharacterDefinition.cs:15-35`).
- **Pivot input handling**: direction reversal is detected from `InputState.MoveX/MoveY` vs wire `FacingYaw` — derived from input, deterministic; the lag is the existing `TurnaroundTicks` (`CharacterState.cs:186`; `TurnaroundLagTicks = 6`, `Simulation.cs:84-89`). Melee's analog pivot threshold (`lstick.x · facing_dir ≤ x38`) becomes a discrete 8-way check.

Run can join `IsPredictable` (`ActionStateClassifier.cs:15`) with zero new wire fields.

### 4.5 Weight

Static per-character data → same transport as `MovementStats`/ability specs (`CharacterDefinition.cs:41+`): registered on both sides (`RollbackSimulator.RegisterEntity`; PredictedTrack's defs channel `PredictedTrack.cs:47-52`). **No packet impact, no snapshot, no determinism risk** — both sides hold the identical def (float, default 100, ~60–120 range per melee-knockback-model.md §5.2). The only downstream effect is the KB magnitude (and through it hitstun, which is pure KB): server-local math that lands in KV→V.

---

## 5. Rollback snapshot summary

| Field | Stateful or derived-from-input | Snapshot path | Failure mode if omitted |
|---|---|---|---|
| `SdiWindowTicksX/Y` | **Stateful** (input-window bookkeeping consumed mid-freeze) | Wire (opponents); LocalTrack history (self) | Rebuilt track accepts/rejects SDI differently than the server → position divergence compounding per hitstop tick (up to 24) |
| `DIX/DIY` | Derived (relayed input) | None | None — both sides re-derive from the relay |
| `HitstunTicks` | Stateful, but Complex-state | None (RawTrack renders wire V) | None |
| KV (tumble) | Stateful | None if Tumble Complex; wire (+12 B) if Predictable | Rebuilt track KV=0 → wrong friction + wrong `HasKnockback` gates |
| Run state | No new stateful fields | None | None |
| `Weight` | Static def | None | None |
| Queued\* launch (existing precedent) | Derived at connect, transition-only | Deliberately off-wire (`CharacterState.cs:123-126`; absence handled at `Simulation.cs:182`) | Accepted divergence absorbed by the next authoritative packet |

---

## 6. Verdict

- **Packet fits as-is** for: DI (existing off-wire `DIX/DIY`), hitstun (existing `HitstunTicks`), weight (static), Run (new enum value only), Tumble **if classified Complex** (new enum value only).
- **Needs a +2 B bump** for SDI: `SdiWindowTicksX/Y` (2 × byte) → `CharacterStatePacket.Size` 113 → **115**; envelope 125 → 127 base / 126 → 128 no-input / 146 → **148** relayed. This is the only field that *must* serialize.
- **Optional +12 B** (KVX/KVY/KVZ) only if the ADR chooses Predictable Tumble → CSP 127, envelope 139/160. Not required by the design; recommend Complex initially (mirrors Hitstun, zero delta).
- **No new packet version**: the protocol has no version field and a documented append-only growth convention (`CharacterStatePacket.cs:79`); one dev, both sides ship together. The bump is: `Size` constant + `Serialize`/`Deserialize`/`FromState`/`ToState`/`ApplyTo` (5 touchpoints in `CharacterStatePacket.cs`) + the two locked-size tests (`CharacterStatePacketTests.cs:113-129`, `ServerEntityPacketTests.cs:178-185`) + the size comments (and fix the stale "145/125" comments at `ServerEntityPacket.cs:22,38-39` while in there).
- **InputState: no change.** DI/SDI ride the existing 8-way `MoveX/MoveY`; the input relay already carries them (`ServerEntityPacket.cs:46-60`).
- **Recommended minimal set for #133/#134/#142**: SDI timers (+2 B, wire) · Tumble Complex (0 B) · Run enum value (0 B, Predictable) · Weight static (0 B) · DI rotate-at-freeze-expiry (0 B) · **total wire delta: 113 → 115 B**.

---

## 7. Sources

### SlopArena (repo root)

| Claim | File:line |
|---|---|
| Hitstop gate: DI capture, decrement, queued-launch application, early return | `src/Shared/Simulation.cs:171-230` (capture 175-176, expiry 179-229) |
| ProcessHitstun: KV decay → V mirror, DI capture, Combo Influence at expiry | `src/Shared/Simulation.cs:609-651` (V=KV 632-634, DI 636-639, influence 643-651) |
| Current KB magnitude + hitstun formula | `src/Shared/Simulation.cs:1098`, `:1118-1124` |
| HasKnockback-gated ledge snap / defensive burst | `src/Shared/Simulation.cs:526`, `:153` |
| TurnaroundLagTicks / SprintThresholdTicks / VelocityDeadZone | `src/Shared/Simulation.cs:84-93` |
| Hitstop set at hit connect + Queued\* payload | `src/Shared/ServerSimulation.cs:1048-1059` |
| HitstunLevel tier at connect | `src/Shared/ServerSimulation.cs:1030-1036` |
| CharacterState fields (KV, HitstunTicks, HitstopTicks, Queued\*, DIX/DIY, Run bookkeeping, JumpHeldTicks) | `src/Shared/CharacterState.cs:113,119,121,123-126,136,140,42,181-191` |
| ActionState enum (byte, 0-8) | `src/Shared/ActionState.cs:5-14` |
| CharacterStatePacket Size=113 + append history, offsets 0-112, ApplyTo preserves non-wire | `src/Shared/CharacterStatePacket.cs:79-81,206-316,346-364` |
| Envelope constants (125/126/146; stale 145 comments) | `src/Shared/ServerEntityPacket.cs:35-44,22,38-39` |
| InputState 20 B layout | `src/Shared/InputState.cs:62-71` |
| Predictable/Complex partition | `src/Shared/Rollback/ActionStateClassifier.cs:15` |
| LocalTrack history + wire-patch correction | `src/Shared/Rollback/LocalTrack.cs:24-25,37-47,57-83` |
| PredictedTrack rebuild from ToState + held inputs + defs channel | `src/Shared/Rollback/PredictedTrack.cs:27,37-74,47-52` |
| RawTrack / batch split | `src/Shared/Rollback/RollbackSimulator.cs:32-39` |
| Tick-ordered input buffer | `src/Shared/Rollback/TickInputBuffer.cs:5-9,22-54` |
| CharacterDefinition / MovementStats (static data channel) | `src/Shared/CharacterDefinition.cs:15-35,41+` |
| Digital 8-way stick (SOCD, ±1 per axis) | `client/Unity/Assets/Scripts/Runtime/Input/InputController.cs:206-215` |
| Size locked by tests (113 / 125) | `tests/Shared.Tests/CharacterStatePacketTests.cs:113-129,160-169`; `tests/Shared.Tests/ServerEntityPacketTests.cs:178-185` |
| Stale packet-size prose (145/112) | `docs/systems/netcode-architecture.md:137,177`; `.omp/AGENTS.md` correct at 146 |
| Design source: DI/SDI deltas, tumble Option A, weight, ranked deltas | `docs/research/melee-knockback-model.md` §4.4, §3.3, §5, §6 |

### Melee decomp (`melee-decomp/src/melee/`)

| Claim | File:line |
|---|---|
| SDI per-hitlag-frame shifts, 2 per-axis timers (x670/x671), reset 254 | `ft/chara/ftCommon/ftCo_Damage.c:575-600` |
| DI launch-angle rotation at hitlag exit (x1A8 [community] 18°) | `ftCo_Damage.c:605-640` |
| ASDI + L/R scale at exit | `ftCo_Damage.c:601-650` |
| Hitstun = KB·0.4, min 1 | `ftCo_Damage.c:296-299` |
| Hitstun gates DamageFly→tumble; tumble exit | `ftCo_Damage.c:1158-1166`, `ftCo_DamageFall.c:44-59` |
| Linear KB friction | `ft/fighter.c:2172-2183` |
| Run / pivot / brake / direct ground states | `ft/chara/ftCommon/ftCo_Run.c:81-143`, `ftCo_TurnRun.c:27-30`, `ftCo_RunBrake.c`, `ftCo_RunDirect.c:21-59` |
| Hitlag formula | `ft/ftcommon.c:646-649` |
