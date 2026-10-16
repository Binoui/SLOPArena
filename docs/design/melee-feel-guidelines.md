# Melee-Feel Design Guidelines — Damage, Knockback, Angles

> Consolidated numeric rules for tuning SLOPArena moves, derived from a 25-character
> analysis of Super Smash Bros. Melee (`docs/research/melee-frame-analysis.md`) and the
> Melee knockback decomp (`docs/research/melee-knockback-model.md`). Use this when
> authoring or retuning any move. Melee runs at 60fps = SLOPArena's 60 ticks/s, so
> frame numbers transfer 1:1 (see that doc for geometry caveats — 2D hitboxes don't
> transfer, timing does).

---

## 1. Commitment → damage: the core tradeoff

Damage scales with total commitment (startup + recovery), roughly **linearly**.
Linear fits across all 322 primary moves in the dataset:

| Predictor | Ground fit | r | Air fit | r |
|---|---|---|---|---|
| first active frame | `dmg ≈ 0.35·startup + 7.5` | +0.48 | `0.27·startup + 8.6` | +0.28 |
| recovery | `dmg ≈ 0.25·recovery + 4.7` | +0.45 | `0.12·recovery + 8.1` | +0.26 |
| total | `dmg ≈ 0.17·total + 3.9` | +0.47 | `0.07·total + 7.7` | +0.18 |

**But the correlation is tier-driven, not per-frame.** Within a single tier (across the
25 chars) the startup↔damage tradeoff is weak and inconsistent (r ranges −0.14 to +0.93,
mostly +0.1–0.5). The tier's role fixes the damage band; startup/recovery only fine-tune
within it.

### Damage bands (median per tier)

| Tier | first | active | total | dmg | role |
|---|---|---|---|---|---|
| jab | 3 | 2 | 19 | 3 | poke / combo glue |
| ftilt | 6 | 4 | 31 | 10 | spacing |
| utilt | 7 | 7 | 30 | 9 | anti-air launcher |
| dtilt | 7 | 3 | 31 | 10 | knockdown |
| dash attack | 6 | 12 | 43 | 9 | engage |
| fsmash | 13 | 5 | 49 | 16.5 | kill |
| usmash | 11 | 9 | 49 | 14 | kill (up) |
| dsmash | 7 | 13 | 49 | 14 | get-off-me |
| nair | 5 | 25 | 47 | 12 | aerial poke |
| fair | 10 | 16 | 44 | 12 | aerial spacing |
| bair | 6 | 12 | 39 | 11 | aerial spacing |
| uair | 6 | 6 | 39 | 12 | juggle |
| dair | 13 | 19 | 49 | 12 | spike |

### Rules of thumb

- **Pick the role → that fixes the damage band** (jab 3, tilt ~10, smash ~14, aerial ~12).
- **Within a band**, a move that is X frames slower than its band median should deal
  ≈ `band_dmg + 0.3·X` damage. Each extra startup frame ≈ +0.3 dmg; each extra recovery
  ≈ +0.2 dmg.
- **If a move is slower than the band yet doesn't hit harder → it's off-trend** (overly
  punished). This is the check to apply when tuning (see §5, the Kistu audit).
- **A grounded move's active window is a thin slice of its duration:** `active/total ≈
  0.10–0.29`. 70–90% of a grounded move is windup + recovery you can be punished in.
  Long-active moves are the exception that reads as "wall / commitment", not the norm.

---

## 2. Launch angles: role-coded, not "better angles"

Melee angles are 0–360° (90 = up, 270 = down). The single biggest feel factor is the
**Adaptive auto-angle (361)** — a dynamic angle computed at hit time from the victim's
position, not a stored constant. It's the majority default on neutral moves:

| Tier | auto(361) |
|---|---|
| bair | 92% |
| ftilt | 82% |
| fsmash | 70% |
| fair | 66% |
| nair | 59% |
| jab | 45% |

**When Melee authors a fixed angle, it's to force a role:**
- **Launcher 70–110°** → up-tilt/up-smash/up-air/dash-attack — dedicated air-combo starters.
- **Down ~270°** → dair spikes only.
- **Side 20–39°** → rare, deliberate tech-chase/knockdown sends (dtilt, dsmash).

So "some moves send at better angles" isn't a tunable — it's that most Melee neutral moves
**adapt** to where the victim is (pops an airborne target, flings a level one). Without an
auto-angle, a fixed 30° jab sends a *grounded* victim and an *airborne* victim at the same
flat angle, which is the combos-feel gap.

---

## 3. The Adaptive auto-angle in SLOPArena (implemented 2026-08-16)

SLOPArena now has an opt-in equivalent of Melee's 361° angle:

- **`KnockbackProfile.Adaptive`** (`KnockbackProfile.cs`). `KnockbackData.Resolve()` returns
  a sentinel angle (`KnockbackData.AdaptiveAngle = sbyte.MinValue`) for it, keeping the
  struct's own `BaseKnockback`/`KnockbackGrowth`.
- **`SpellResolver.ComputeAdaptiveAngle(dx, dy, dz)`** resolves the pitch at **hit time**,
  not hitbox spawn, from the hitbox→victim displacement. The vertical reference is the
  victim's **hurtbox center** `(PosY + EndY)/2`, matching Melee's center-based angle.
- **Clamped to `[0, 90]`** — never sends a victim downward. A grounded/level victim goes
  flat (clean neutral reset); a genuinely airborne victim pops up (juggle). Spikes stay
  authored with the fixed `KnockbackProfile.Spike` (−45°).
- **Wire/rollback-safe**: the geometry is already computed per hit in the collision pass;
  deterministic on both server and client sim.

### How to author with it

```csharp
// A combo/juggle tool that adapts to where the victim is.
Knockback = new() { Profile = KnockbackProfile.Adaptive,
                    BaseKnockback = 4f, KnockbackGrowth = 20f }
// Angle field is authored as documentation but ignored at runtime.
```

Flag **combo/juggle pokes** with it (fast neutral moves). Keep **launchers** (fixed high
angle), **spikes** (fixed −45° or lower), and **kill moves** (fixed side angle) as-is —
Melee role-codes those with fixed angles too. The Kistu pilot flags g_1/a_1/a_2.

---

## 4. Knockback model

SLOPArena's magnitude (`Simulation.ApplyKnockback`):
`magnitude = (baseKB + growthKB·(damage%·0.01 + 1) + damage·0.1) · 200/(weight+100)`.

Melee adds three knobs SLOPArena historically lacked (see `melee-knockback-model.md`):

1. **Weight divisor** `200/(W+100)` — heavies take less KB, curve flattens at high W.
   Already present in SLOPArena (`CharacterDefinition.Weight`, default 100).
2. **Move-damage term** `p/20` — strong hits carry KB even at 0%; weak jabs stay weak.
3. **Hitstun is a pure function of KB** (`max(1, KB·0.4)`), not a per-move designer cap.

Feel guidance:
- **`StunTicks` is a designer valve, not a KB-purity knob** — Melee's floor is 1 frame.
  A `StunTicks=0` "true weak jab" has no Melee analog.
- Hitstun shape (linear in KB, no hard floor) is the feel lever; per-move caps just cap
  it.

---

## 5. Worked audit: the Kistu normal tier

Applying §1–§2 to Kistu's normals produced the tuning in the package source (2026-08-16):

| Move | before | after | why |
|---|---|---|---|
| g_1 Quick Slash | startup 9, dmg 4 | **startup 5** | jab band is first ~3–5; 9 was tilt-slow for a poke |
| g_2 Double Slash | total 40, hit2 6 | **total 34, hit2 7** | slower-and-weaker than ftilt band (10@31) → equal damage, 2-hit string justifies it |
| g_4 Heavy Down | startup 22, dmg 12 | **startup 14** | 22 > even fsmash (13) yet dmg 12 < fsmash 16.5 — inverted reward |
| g_1/a_1/a_2 | Custom fixed 30/30/40 | **Adaptive** | combo/juggle tools get the adaptive auto-angle |

Also flagged during the Kistu pass: **blade hitboxes** (`_weapon_hilt` → `_weapon_tip`)
must carry `EndBoneName = "_weapon_tip"` or the capsule collapses to a point and the
hitbox vanishes. The tip restore (all 9 normal hitboxes) is what makes Kistu's blades
connect.

---

## 6. Cross-references

- `docs/research/melee-frame-analysis.md` — full per-tier frame stats, IASA/landing-lag analysis, engine deltas.
- `docs/research/melee-frame-data.md` + `.json` — machine-readable per-move dataset the numbers above were computed from.
- `docs/research/melee-knockback-model.md` — KB formula, hitstun, flight, DI/SDI, weight (byte-level decomp).
- `docs/characters/character-kit-design-principles.md` — the 8 ability archetypes + keybinding/cooldown layer.
- `src/Shared/KnockbackProfile.cs`, `src/Shared/SpellResolver.cs` — the Adaptive implementation.
- `tests/Shared.Tests/AdaptiveAngleTests.cs` — unit + integration coverage for the auto-angle.
