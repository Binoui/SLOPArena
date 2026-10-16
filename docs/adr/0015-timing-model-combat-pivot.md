# ADR-0015: Timing-Model Combat — Drop Warp, Momentum-Preserve Attacks, Single Moves

**Status:** Proposed — 2026-08-10
**Superseded in part — 2026-08-13 (wayfinder map #128):** §5's endorsement of exponential KB decay (λ = 1.8/s) is replaced by [ADR-0019 §6](0019-melee-based-hit-response.md) (constant KV through hitstun → linear horizontal friction + flight gravity); §7's **Dash split** and **Landing lag** deferrals are resolved by [ADR-0020 §2](0020-melee-based-movement.md) (Run tier + friction-coast dash end) and [ADR-0021 §3](0021-melee-based-frame-timing.md) (landing lag + auto-cancel, L-cancelled scale). §1-4, §6 stand.
**Deciders:** @Binoui

## Context

The attack layer was copied from DKO: warp (auto-gap-closer at SprintSpeed, `WarpRange`/`WarpCone`), press-based auto-combo LMB chains (`StageChainAbility` chains on press, not on hit), air-attack hover (`ActivateAbility` cancels falling VY and resets `AirTimeTicks` into the zero-gravity FloatWindow, `ServerSimulation.cs:121-123`), and fat hitboxes (radii 0.9–1.5, reaches to 2.2 m) sized to paper over the warp-stop gap.

Playtest feedback (solo): the game feels *less free* since the crutch layer landed. The player wants Smash-Melee-style movement freedom (short hop, fast fall, drift, aerial commitment) and fighting-game commitment (readable neutral, whiff-punishable) instead of anime-fighter auto-strings and auto-approaches.

The genre constraint that shapes everything: **free mouse camera + 3D makes spacing unreadable** — the "spacing line" is unstable because the opponent's relative position changes with camera yaw. Footsies (Tekken-style) requires a stable camera axis; we keep the free camera. Therefore the defense model must be **timing-based, not spacing-based** — dodge/burst/parry timing, no blocking (blocking also needs a facing-line rule that is ambiguous in 3D). DKO reached the same conclusion with its parry/clash layer; this ADR goes further by removing the crutches DKO kept.

## Decision

1. **Drop warp.** Set `WarpRange = 0` on every `AttackStage` (all four characters). Initiation is fully gated on data: `ServerSimulation.cs:411` (`firstStage.WarpRange > 0`) and `:699` (`attackStage.WarpRange > 0`); `ProcessWarp` only runs while `WarpSpeed > 0`, which only those branches set. The machinery (`Warping` state, `WarpSpeed`/`WarpTargetX/Z`/`WarpAttackRange`, `_pendingWarpAttacks`, `ProcessWarpArrivals`, `ProcessWarp`) stays **dormant, not deleted** — one data flip re-enables it. Delete it in a later branch only once the direction is confirmed. Zero client impact: no Unity script references warp; warp fields are not serialized.

2. **Momentum-preserve attacks by default.** Remove three blocks: the `VY < 0` cancel in `ActivateAbility` (`ServerSimulation.cs:121-123`), the post-lunge `VX/VZ = 0` (`StageChainAbility.cs:61-64`), and the ground-friction gate during `Attacking` (`Simulation.cs:279-283`). Lunge (`LungeForce > 0`) remains the per-move movement override — moves that want imbued movement declare it. The `AirTimeTicks = 0` reset at activation is also removed (see 6). Consequence: air attacks ride your trajectory — you fall through aerials, drift carries into the attack. Whiffing is now a commitment.

   **Refinement (2026-08-12):** momentum-preserve is an **aerial** property. A **grounded** activation now zeroes the incoming horizontal velocity (`VX/VZ = 0` in `ServerSimulation.ActivateAbility`, applied **before** `OnStart` so the move's own lunge/velocity still lands) — grounded moves stop movement, aerials keep it. Per-ability opt-out: `AbilitySpec.PreserveMomentumOnStart` for dash-attack style moves. FightGuy's normal tier (keys 1-4) declares no lunge and no override — they are pure stop-move, and their air variants ride the jump/drift.

3. **One move per slot, no auto-combo chains.** LMB chains die. Each slot is a single move with a ground/air variant (the variant split already exists: `GetSlotAbility(slot-1, airborne)`). `StageChainAbility`'s chain logic (buffer, early-chain, stage transitions) is removed; `LmbCombo`/`AirLmbCombo` become single-move or are removed. Hit-confirm strings are the recorded fallback if single moves feel thin — a flag in the ability base, not a redesign.

4. **Hitbox/hurtbox discipline.** Shrink attack radii and hurtboxes (`HurtboxBoneScale` per character, currently 1.0). **Coupled retune:** warp's removal changes what `AttackRange` means — it was the warp-stop distance; it becomes the move's engage/tracking radius. The three numbers that were one system (warp stop, lunge, hitbox size) must be re-tuned together; with warp gone the gap they papered over disappears, so hitboxes can shrink without breaking contacts. Start by shrinking hurtboxes (dodge-through works, no random clipping), keep attack hitboxes medium, let soft-lock + tracking do the aim-assist.

5. **Knockback reset-to-neutral (data first).** Cut Launcher usage to rare kill tools, shorten `StunTicks` (16–44 → ~10–25 target), flatten launch angles (more horizontal sends — the `Sliding` ground state does the work). ~~Keep the `%`-scaled exponential decay (λ = 1.8/s) — correct shape.~~ **Superseded 2026-08-13 by ADR-0019 §6** — the exponential decay is replaced by constant-KV flight through hitstun, then linear horizontal-azimuth friction (10 m/s²) + flight gravity (8 m/s²). No new states in v1; a `Knockdown` state is the recorded structural option if ground resets feel mushy.

6. **Dedicated recovery move per character.** One slot per kit becomes the Smash up-B analog: upward/diagonal burst, resets the float window, long cooldown (once per life-or-death). The FloatWindow and its reset survive **only here** — normal air attacks lose the hover. Movement recovery (double jump + drift + fast fall) is the base; the dedicated move is the answer to "recovering becomes hard" when hover and chains die.

7. **Deferred (recorded, not built):**
   - **Clash system** (phase 2): symmetric commit resolution — two `Interruptible` hitboxes connecting within a few ticks resolve as mutual bounce (no damage, short clash-stun + pushback, reset to neutral) instead of a random trade. This is the timing-model substitute for whiff-punish in free-camera 3D. Until it lands, simultaneous commits trade.
   - **Dash split** — **resolved 2026-08-13 by ADR-0020 §1-2**: SLOPA Dash (Shift, i-frames + cooldown) stays as-is; the locomotion tier is a new **Run** (not a "movement dash"). Footsies live at run/jump range.
   - **Landing lag** on air attacks — **resolved 2026-08-13 by ADR-0021 §3**: landed as `LandingLagTicks` + auto-cancel windows, L-cancelled scale, aerials always end on landing.

## Considered Options

- **Spacing/footsies model** — rejected: requires a stable camera axis (Tekken) or dynamic both-in-frame framing (Smash); we keep the free mouse camera, so spacing reads are unreliable.
- **Blocking** — rejected: needs a facing-line rule (which side am I covered from?) that is ambiguous in 3D; no platform fighter uses it (Smash, DKO).
- **Keep the DKO layer, tune numbers only** — rejected: tuning doesn't fix the feel. Warp and hover mask the movement layer; the complaint is the layer itself.
- **Directional attacks (Smash model)** — rejected here; the input-model argument is ADR-0016 (chord ambiguity on keyboard + tracking makes direction redundant).

## Consequences

- **Glossary rewrite** (CONTEXT.md): `Warp`, `WarpRange`, `WarpCone` deprecated; `FloatWindow`/`FloatWindowReset` re-scoped to the recovery move only. See the inline updates.
- **ADR-0013 (Combo Influence) strengthens**: its context noted warp + tracking "erase any shift" — with warp gone, nothing erases the defender's drift; the influence becomes the primary combo-escape tool.
- **Rollback simplifies**: `Warping` leaves the Complex ActionState set; one fewer non-re-simulated state.
- `AttackRange` semantics change (warp-stop → engage radius); rename only if the code churn is worth it — the field stays as-is for now.
- **Test impact**: kit golden snapshots regenerate (`REGENERATE_GOLDENS`); warp-specific tests removed; new tests for momentum-preserve + recovery move.
- **Data churn** across all four character kits (WarpRange=0, hitbox/hurtbox sizes, StunTicks/angles, one recovery slot each).
