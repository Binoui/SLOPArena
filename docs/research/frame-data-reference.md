# Frame Data Reference — DKO-style Values

> Extracted from manual frame counting of DKO gameplay footage.
> All values in frames at 60fps. Use as tuning reference for SLOPArena.
> Cross-ref: `docs/research/dko-mechanics.md`, `Shared/CharacterDefinition.cs` (SelfLockTicks, ChainWindowTicks)

---

## Movement

| Action | Total frames | Details |
|--------|-------------|---------|
| **Jump** | 35 frames | Apex at frame 16-17. Symmetric arc. Gravity ≈ 35, JumpForce ≈ 10. |
| **Dodge** | 22 frames | Invincibility frames before landing. Entirely airborne. |

---

## RMB — Heavy Attack

### RMB Uncharged

| Phase | Frames |
|-------|--------|
| **Startup** (press → hit connects) | 21 + 12 = 33f |
| **Active** (hitbox window) | ? |
| **Endlag** (after hit → can act) | ~20f |

**Total duration:** ~53 frames

### RMB Charged (max charge)

| Phase | Frames |
|-------|--------|
| **Charge hold** (press → full charge) | 45f (includes startup) |
| **Post-charge** (release → hit connects) | 17f |
| **Endlag** (after hit → can act) | ~20f |

**Total duration:** ~82 frames (45 charge + 17 startup + 20 endlag)

---

## LMB Combo (3 hits)

### LMB1 — Horizontal sword swing

| Phase | Frames (from 5m) | Frames (close range) |
|-------|------------------|---------------------|
| **Warp (approach)** | 13 | 15 |
| ├─ Startup before moving | 5 | — |
| └─ Travel to target | 8 | — |
| **Startup** (press → hit connects) | 9 | 15 |
| **Active frames** | ? | ? |
| **Endlag** (after hit → can act) | 11 | ? |

**Total LMB1 duration:** ~20 frames (from hit to recovery end)

### LMB2 — Follow-up swing

| Phase | Frames (from 5m) | Frames (close range) |
|-------|------------------|---------------------|
| **Gap after LMB1 ends** | 14 | — |
| **Startup** (press → hit connects) | 9 | 25 from LMB1 hit |
| **Endlag** (after hit → can act) | 9 | — |

**Chain window:** LMB2 must be pressed within ~14 frames after LMB1 recovery ends.

### LMB3 — Finisher swing

| Phase | Frames (from 5m) | Frames (close range) |
|-------|------------------|---------------------|
| **Gap after LMB2 ends** | 2 | — |
| **Startup** (press → hit connects) | 20 | 28 from LMB2 hit |
| **Endlag** (after hit → can act) | 20 | — |

---

## Summary Table (per hit)

| Hit | Startup | Active | Endlag | Total | Chain window |
|-----|---------|--------|--------|-------|-------------|
| LMB1 | 9f | ? | 11f | ~20f | — |
| LMB2 | 9f | ? | 9f | ~18f | 14f after LMB1 end |
| LMB3 | 20f | ? | 20f | ~40f | 2f after LMB2 end |

### SelfLockTicks → Frame conversion

At 60fps, 1 tick = 1 frame (if TickDt = 1/60 ≈ 0.01667s).

| Frames | Ticks (@ 60fps) | Suggested SelfLockTicks |
|--------|-----------------|------------------------|
| 9 (startup LMB1) | 9 | — |
| 11 (endlag LMB1) | 11 | **20** (startup + endlag combined) |
| 14 (chain window) | 14 | **15** (ChainWindowTicks) |
| 20 (startup LMB3) | 20 | **40** (startup + endlag combined) |

Note: SelfLockTicks currently covers the full attack duration (startup + active + endlag), not just endlag. The `AnimLockTicks` prevents any action during this window. The `ChainWindowTicks` is the grace period after lock expires for inputting the next combo stage.

---

## Open Questions

- What are the actual active frames (hitbox duration) per hit?
- Warp distance vs Attack range: how to split these in AttackStage data?
- Should warp be a separate phase (WarpTicks + WarpDistance) or part of SelfLockTicks?
- Does LMB3 have unique properties (launcher, more knockback)?
