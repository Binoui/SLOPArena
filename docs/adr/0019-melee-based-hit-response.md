# ADR-0019: Melee-Based Hit Response

**Status:** Accepted — 2026-08-13 (wayfinder map #128, destination)
**Deciders:** @Binoui
**Supersedes:** ADR-0013 (Combo Influence — dropped, §4); the hit-response model in `docs/systems/hitstun-di.md` v1 (freeze + exponential decay + additive `DIX·3.5`)
**Related:** ADR-0012 (per-pair hitstop freeze — formula re-derived, §3), ADR-0014 (Burst — the explicit exception, unchanged), ADR-0015 (timing-based defense model), ADR-0020 (movement), ADR-0021 (frame timing)

## Context

The map's hit-response arc ([#128](https://github.com/Binoui/SlopArena/issues/128)): replace the DKO-style hit response with a **Melee-based feel engine**, adapted to 3D, keyboard-first 8-way input (ADR-0016), and the server-authoritative rollback sim. Melee is the base; Melee-exact is not the goal ("we are not melee exactly, 3D means diff gameplay"). Decisions came from grilling sessions: hitstun shape [#131](https://github.com/Binoui/SlopArena/issues/131), knockback formula [#132](https://github.com/Binoui/SlopArena/issues/132), DI [#133](https://github.com/Binoui/SlopArena/issues/133), SDI [#134](https://github.com/Binoui/SlopArena/issues/134), hitstop [#143](https://github.com/Binoui/SlopArena/issues/143), flight model [#130](https://github.com/Binoui/SlopArena/issues/130), grounded by the movement audit [#135](https://github.com/Binoui/SlopArena/issues/135), the flight sweep prototype [#141](https://github.com/Binoui/SlopArena/issues/141), and the netcode impact scan [#140](https://github.com/Binoui/SlopArena/issues/140).

## Decision

**Hit response = Melee formulas at SLOPArena scale, custom-only authoring, zero wire growth.**

### 1. Knockback formula (Melee at SLOPA scale, [#132])

```
mag = (base + growth·(P/100 + 1.0) + Damage·0.1) · 200/(W + 100)
```

- `base` / `growth` per hitbox (custom-only — see below), `P` = victim DamagePercent, `Damage` = the hit's damage, `W` = victim weight.
- **Weight divisor** from Melee: new `CharacterDefinition.Weight`, default **100** (today's tuning); per-character values → balance pass. Weight is **KB-only** — no throw/grab system exists (tether pulls ≠ grabs).
- Growth floor **f = 1.0** — growth lives at 0% victim damage (Melee's `KnockbackGrowth`).
- Damage term at Melee's raw p/10.
- **No cap** — 999 is unreachable at sane damage.
- **KnockbackProfile dropped — custom-only authoring like Melee**: values resolve pre-wire, so wire/goldens are untouched; the ~15 canned-profile hitboxes expand to explicit values; the enum/`Resolve` machinery is deleted. The 5×3 archetype table survives as a doc.

### 2. Hitstun shape (pure function, [#131])

```
hitstun = max(1, (int)(k · (kbMag + floorMag)))   // k = 0.7, floorMag = +20 (balance pass, below)
```

- **`StunTicks` survives only as the gate**: `0` → no lock for BOTH zero-KB hitboxes and KB-bearing moves (the burst shove stays lock-free). This is the one deviation from Melee's min-1.
- Cap / floor-8 / ceiling-60 all dropped.
- k = 0.5 provisional → **landed as 0.7 with a +20 magnitude floor** (2026-08-17 balance pass, see "Balance pass — melee-shape adoption" below).

### 2a. Balance pass — melee-shape adoption (2026-08-17)

Landed via the move-data tool's true-combo matrix (issue #147): the provisional 0.5 / KV×0.14 curve produced **zero true combos** on both characters at 0–150%. The matrix (real-sim verdicts, greedy chase) isolated the cause — stun and travel both derive from the same magnitude while travel ∝ KV² outruns stun ∝ KV — and validated the adoption:

```
k         = 0.7   (was 0.5)   — stun per unit launch
floorMag  = +20   (was 0)     — Melee-style "+18" damage-independent floor: real low-% stun
KbScaleFactor = 0.11 (was 0.14) — velocity-only scale; stun still derives from the UNSCALED mag
```

Matrix result (true links per %, both characters, real sim):

| char | 0% | 30% | 60% | 90% | 120% | 150% |
|---|---|---|---|---|---|---|
| FightGuy (old) | 0 | 0 | 0 | 0 | 0 | 0 |
| FightGuy (new) | **6** | **4** | **3** | **1** | 0 | 0 |
| Kistu (new) | **16** | **12** | **6** | **7** | **6** | — |

g1 Low Kick is FightGuy's combo hub (jab-jab, anti-air conversions, aerial links); Kistu's profile stays hot at high % — a per-character growth nudge is the follow-up if it feels wrong.

**Fixed tools excluded from the floor**: `ApplyKnockback(applyScale:false)`, `ApplyKnockbackForce`, and the `QueuedKVOverride` (OnHitEntity) path take `k·magnitude` WITHOUT `floorMag` — grabs/yanks are Melee's `weight_set_knockback` analog, and the floor made Nilus's yank over-pull (drag outliving the reel, carrying the victim through the caster).

The tuning knobs originally lived as statics (`Simulation.HitstunStunCoefficient`, `HitstunMagBonus`, `KbScaleFactor`). Since 2026-10-11, profiles resolve to immutable `CombatTuning` owned by each simulation; the values and formulas are unchanged. The tool still selects profiles with `scripts/move-data.sh fightguy --truecombos --kbm <model>` (`base|old|stunx18|kv70|stun16kv11|floor30|melee|melee-hot|melee-soft`). See [Combat Systems](../systems/combat-systems.md#damage-and-hit-response) for the current API.

### 2b. Balance re-landing — melee-soft (2026-08-19, issue #149)

The +20 floor delivered free true combos at 0% — against the intended "combos are earned with
damage %". A/B'd three melee-family candidates (`melee` 0.4/0.19, `melee-hot` 0.4/0.22,
`melee-soft` 0.45/0.17 — all floorMag 0) against the 0.7/+20/0.11 shipped via `AbDiffReport`.
Adopted **melee-soft**:

```
k         = 0.45  (was 0.7)   — stun per unit launch, no floor
floorMag  = 0     (was +20)   — no damage-independent floor: 0% hits barely stun
KbScaleFactor = 0.17 (was 0.11) — launch reads as a pop, not a glide
```

Effect: **all 8 FightGuy free true-combo edges at 0% are gone** (combo graph `T→F`); combos
emerge only as damage builds. `AbDiffReport` self-play telemetry shows damage/match dropping
under the candidate — but the heuristic bot does not chase combos, so that delta is a bot-
behavior artifact, not the tuning's truth (the combo-graph section is the honest read). Fixed
tools keep the no-floor rule from §2a (grabs/yanks are `weight_set_knockback` analog).

### 3. Hitstop (Melee hitlag, pure shape, [#143])

```
hitstop = min(12, (int)((damage/3 + 6) · mul))   // x198≈⅓, x19C≈6 (community), mul per-hit
```

Jabs 7, mediums 8, kills 10 ticks (was: cap-12 peg for everything ≥ 8 damage). All SLOPA rules dropped (low-damage ×2, multihit ×0.5 — multihits aren't a core SLOPA feature — floor 1). Cap 12 kept as a never-biting safety (kit max 16 dmg → 11). The six unused `hitstop_*` keys become one `hitstop_multiplier` (default 1.0).

### 4. DI (Melee angle rotation at hitstop exit, [#133])

- Capture the committed 8-way input during `HitstopTicks` (already implemented, `Simulation.cs:171-177`); **rotate at hitstop exit**; `hitstop = 0` → capture at hit tick, rotate immediately.
- Rotate the queued launch toward `(DIX, 0, DIY)` by up to **`18°·sin²(angle)`** (perpendicular = full, parallel = 0 — you can't DI along the launch), **magnitude preserved**.
- **Plane rotation** (3D adaptation): the rotation happens in the shared plane — elevation tilts toward horizontal for vertical launches (spike/launcher survival-DI works); horizontal launches degenerate to azimuth.
- **Combo Influence dropped — ADR-0013 superseded** (rotation replaces the expiry drift; both would stack to double-strength). `LaunchMagnitude` removed (server-local, never on the wire).

### 5. SDI (one-shot + ASDI, zero wire bytes, [#134])

- **One-shot SDI**: first nonzero input during `HitstopTicks` → one position shift along the 8-way hold. **ASDI**: one more shift at flight start along the committed DI direction (up to 2 shifts/hit, ~0.4× magnitude). Mashing adds nothing (user rejected mash-rewarded per-tick SDI — it corrupts DI, whose commit is latest-wins during the same window).
- **No timers/flag** — first-input tick is a pure function of replayed relayed input → **CSP stays 113**, superseding #140's +2B SDI-timer budget.
- SDI + DI share the direction and compound synergistically. Global `sdiScale` + ASDI fraction (~0.4×) → balance pass; raw position deltas; penetration resolves at flight start.

### 6. Flight law (Melee-shaped, [#130])

- **Constant KV through hitstun** (replaces exponential decay) — hitstun is the *no-input lock*, not a freeze: the victim flies at constant knockback velocity, control returns at lock end with residual speed.
- Post-hitstun: **linear horizontal-azimuth friction 10 m/s²** (KVY untouched); **flight gravity 8 m/s²** (path to 36 via §1).
- **Melee hard-set bounce** during hitstun (on stage contact during hitstun, the bounce velocity is set; soft-merge after).
- **No new ActionState** — flight is a velocity-law swap inside existing actionable Idle.
- Aerials usable post-hitstun; **no airdodge** (dead scaffolding, not adopted).
- Landing/tech flow (missed-tech bounce → knockdown → getup, ground/wall tech) deferred — foundation for the fog item, ADR-0020 §4 and the map.

### 7. Burst — the explicit exception (ADR-0014, unchanged)

Burst is the only thing that acts inside the hitstun lock (defensive: clears hitstun + knockback, invulnerable startup, shove, then Burst Recovery). Unchanged by this ADR.

## Considered Options

- **KnockbackProfile vs custom-only** — profile model rejected: Melee itself profiles KB per hitbox (fixed angle + growth + base), so the profiled model was tested and dropped; custom-only authoring wins (#132).
- **StunTicks valve vs pure function** — valve rejected: hitstun must be a pure function of the KB the victim actually takes, so authors can't double-book; `StunTicks` keeps only the zero-gate (#131).
- **Mash-rewarded per-tick SDI** — rejected by the user: corrupts DI's latest-wins commit during the shared window; one-shot + ASDI wins (#134).
- **Hybrid flight (exp decay → transfer)** — the prototype sweep (#141) showed the 3-phase decay never lands cleanly and leaves a 9+s tail; Melee-shaped constant-KV + linear friction + flight gravity wins (#130).
- **DI additive drift (DIX·3.5)** — the v1 model, superseded: angle rotation at exit with preserved magnitude is Melee's actual mechanic and interacts correctly with spike/launcher angles (#133).

## Consequences

- **`ApplyKnockback` signature gains `damage` + `weightFactor`** (the formula needs both).
- **`HitstunLevel` anim tier re-derives from applied hitstun** — currently `ServerSimulation.cs:1029-1031` derives it from authored `hit.StunTicks`; must read the applied hitstun instead.
- **`ComputeHitstopTicks` drops `beyondFirst` + six keys** → `(int)((damage/3+6) · hitstop_multiplier)` capped 12. DI window on kills: 12→10 ticks (still comfortable).
- **SDI/DI rotation math**: rotate KV toward `(DIX, 0, DIY)` by `min(18°·sin², angle)` in the shared plane at exit; `kvMag` computed locally; `LaunchMagnitude` field removed.
- **Netcode: 0 bytes** — no new wire fields (SDI needs no timer), weight is static data, flight is a velocity-law change. CSP stays 113.
- **Goldens regenerate** — movement/hitstop/DI/DI-snapshot golden tests shift (REGENERATE_GOLDENS).
- **Balance pass** (post-design): per-char weights, k (hitstun), `sdiScale` + ASDI fraction, and the user-flagged **vertical-KB lean** (steeper launch angles + magnitude compensation).
- `docs/systems/hitstun-di.md` rewritten to this model (see repo).
