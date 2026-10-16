# Melee Knockback Model — KB, Hitstun, Flight, DI/SDI, Weight

> Research source: byte-perfect decomp of Melee NTSC 1.02 (`doldecomp/melee`), cloned
> next to this repo at `../melee-decomp` (35MB, full depth-1). All Melee file refs
> below are relative to `melee-decomp/src/melee/`. Companion frame-data research:
> [`melee-frame-analysis.md`](melee-frame-analysis.md).
>
> **Goal**: move SLOPArena's hit-response feel from DKO-style (exponential knockback
> decay, soft expiry DI) toward a Melee base — adapted to 3D. Verified items are
> marked [verified] (read in the decompiled C). Numeric constants that live in the
> disc's `PlCo.dat` (loaded at runtime, `ft/fighter.c:186`) are marked [community] —
> their values come from community testing, the code structure is verified.

## Scope

Covers: (1) knockback formula, (2) hitstun, (3) flight dynamics, (4) DI/SDI,
(5) per-character weight. Explicitly **not** covered: stale moves (`pl/plstale.c`
— not of interest right now), frame timing (already in the frame-analysis doc),
hitlag tuning (SLOPArena has `HitstopTicks` per ADR-0012; Melee's is
`(dmg·x198 + x19C)·vibrateMult`, `ft/ftcommon.c:646`).

What transfers: **formulas, timing structure, and mechanics** — SLOPArena units are
m/s and ticks, so every constant gets retuned, but the *shapes* are the value.
What doesn't: 2D hitbox geometry, per-move data, the motion-state FSM, exact balance.

---

## 1. Knockback formula — `ft/ftcoll.c`

### 1.1 The verified code

```c
// ftcoll.c:2151 — KNOCKBACK(defense, attack, arg3, one, ftd, hit, w, inner)
((defense) * ((attack) * ((arg3) * ((0.01F * (hit)->x24 *
    ((ftd)->x11C * (((ftd)->xF8 - (((w) * (ftd)->xF8) / ((one) + (w)))) *
     (inner)) + (ftd)->x120)) + (hit)->x2C))))
```

Decoded terms [verified]:

| Term | Source | Meaning |
|---|---|---|
| `hit->x24` | hitbox field +0x24, set from `create_hitbox_3.knockback_growth` (`ft/ftaction.c:333`) | knockback growth `KBG` |
| `hit->x2C` | hitbox field +0x2C, set from `create_hitbox_4.base_knockback` (`ft/ftaction.c:340`) | base knockback `KBB` |
| `hit->x28` | hitbox field +0x28 = `weight_set_knockback` | fixed-value mode (throws): if nonzero, replaces victim % |
| `inner` | `x110·P + x114·(hits·P)` where `P` = post-hit percent (`dmg.x1830_percent + dmg.x1838_percentTemp`), `hits` = `hit->unk_count` (capsule field +0x8) | percent + damage-dealt term |
| `w` | `co_attrs.weight · ftd->xF4` | scaled victim weight |
| `defense/attack` | `Player_GetDefenseRatio/AttackRatio` | handicap ratios, normally 1.0 |
| `arg3` | `gm_8016B248()` | stage KB multiplier, normally 1.0 |
| `ftd->x108` | clamp applied after (`ftcoll.c:2187`) | KB cap |

The weight term collapses to `xF8/(1+w)` — a weight divisor of the exact
`200/(W+100)` family. Community-simplified form (SSBWiki / Magus420 testing):

$$KB = \left(\left(\frac{p}{10} + \frac{p \cdot P}{20}\right) \cdot \frac{200}{W+100} \cdot 1.4 + 18\right) \cdot KBG + KBB$$

where `p` = move damage, `P` = victim post-hit %, `W` = weight, capped at 999.

### 1.2 SLOPArena today — `src/Shared/Simulation.cs:1098`

```csharp
float magnitude = baseKB + growthKB * (s.DamagePercent * 0.01f);
```

Linear in victim %, no weight, no move-damage term, no cap, no ratio multipliers.
Melee has three extra knobs SLOPArena lacks:

1. **Weight divisor** `200/(W+100)` — heavies take less KB, curve flattens at high W
   (ΔW matters more at low weights).
2. **Move-damage term** `p/10` — strong hits carry KB even at 0%; weak jabs stay weak.
3. **Growth multiplies a floor** (`+18`) — growth never dies to zero at low %; base is
   added outside, so base-only moves don't get growth-scaled.

### 1.3 Migration delta

```csharp
// Proposal: keep the profile system, add weight + damage terms.
// weightFactor = 200/(W+100), normalized so W=100 (medium) → 1.0
float weightFactor = 200f / (def.Weight + 100f);
float magnitude = (baseKB + growthKB * (s.DamagePercent * 0.01f)
                 + damageDealt * 0.05f)   // Melee p/20 term (tune constant)
                 * weightFactor;
```

Requires a `Weight` stat on `CharacterDefinition` (§5) and the move's damage passed
into `ApplyKnockback` (currently available at the `ResolveHits` call site).

---

## 2. Hitstun — `ft/chara/ftCommon/ftCo_Damage.c`

### 2.1 The verified code

```c
// ftCo_Damage.c:296 — hitstun frames = KB × 0.4, floor 1
fp->mv.co.damage.x0 = (int)(kb_applied * p_ftCommonData->x154);  // x154 = 0.4 [community]
if (!fp->mv.co.damage.x0) fp->mv.co.damage.x0 = 1;
```

- The timer ticks down every frame (`ftCo_8008F744`, `ftCo_Damage.c:967-978`);
  while > 0 the DamageFly state cannot exit to tumble (`ftCo_DamageFly_Anim`,
  `ftCo_Damage.c:1158-1166`).
- **Hitstun is a pure function of KB.** No per-move override, no designer cap.
  KB cap 999 → max ~399 frames. Even a 1-KB jab gives 1 frame.
- Secondary effect [verified]: during hitstun (`time_since_hit < xFC`,
  `ftCo_Damage_CalcVel`, `ftCo_Damage.c:221-235`) bounces **hard-set** the KB
  velocity; after hitstun they **soft-merge** (add/keep-max per axis). This is what
  makes wall/floor bounces feel crisp during hitstun and damped after.

### 2.2 SLOPArena today — `Simulation.cs:1118-1124`

```csharp
ushort hitstunFromKB = (ushort)Math.Clamp(8 + (int)(kbMagnitude * 0.5f), 8, 60);
ushort hitstunFinal = Math.Min(hitstunFromKB, stunTicks); // per-move cap
```

### 2.3 Deltas

| Melee | SLOPArena today | Migration option |
|---|---|---|
| `(int)(KB·0.4)`, min 1 | `8 + 0.5·kbMag`, clamp 8–60 | Drop the floor 8 and the ceiling 60; keep `max(1, …)` |
| pure KB function | capped by per-move `StunTicks` | **Decision**: keep `StunTicks` as a designer valve (allows `StunTicks=0` weak-jab, a Melee-impossible move) or go pure |
| scales to 399 frames | ceiling 60 | If pure: raise ceiling (arena pacing may justify keeping ~90) |

Note the design tension: `StunTicks = 0` ("true weak jab") has no Melee analog —
Melee's minimum is 1 frame. Keeping the cap is fine, but the *shape* (linear in KB,
no hard floor) is the feel lever.

---

## 3. Flight dynamics — `ft/fighter.c` + `ftCo_Damage.c`

### 3.1 Melee: constant velocity + linear friction

- KB velocity is set once at launch (`x8c_kb_vel`). In air, **no exponential
  decay** — it holds through hitstun and beyond.
- Per-frame **linear** friction, direction-preserving, applied every frame while
  `kb_vel != 0` (`fighter.c:2172-2183`):

```c
if (sqrtf(kb_vel_x*kb_vel_x + kb_vel_y*kb_vel_y) < ftd->x204_knockbackFrameDecay)
    kb_vel = 0;                                  // snap to zero below threshold
else {
    kb_vel.x -= ftd->x204_knockbackFrameDecay * cosf(kb_angle);   // ~0.051/frame [community]
    kb_vel.y -= ftd->x204_knockbackFrameDecay * sinf(kb_angle);
}
```

- Full gravity applies during flight (`ftCo_DamageFly_Phys` → `ft_80084DB0`).
- Net effect: launches travel on a clean arc, then **drift a long tail** after
  hitstun at slowly-decaying speed — the classic Melee "kept drifting to the blast
  zone" feel. Ground: slide friction with per-character `gr_friction`.

### 3.2 SLOPArena today — `Simulation.cs:614-617, 735-738`

```csharp
float decay = MathF.Exp(-KnockbackDecayRate * TickDt);   // λ = 1.8/s
s.KVX *= decay; s.KVY *= decay; s.KVZ *= decay;          // all axes, frontloaded
```

DKO-style: most travel happens immediately, tail is asymptotically dead. This is the
single biggest feel difference from Melee and the main "get away from DKO" lever.

### 3.3 Deltas (pick one)

- **Option A — Melee-shaped (recommended):** remove in-flight decay entirely.
  Constant KV through hitstun; after hitstun (tumble), apply **linear** friction:
  `KVXZ -= friction·TickDt` along the horizontal launch azimuth, `KVY` untouched
  (gravity does the vertical work). Snap below `VelocityDeadZone` (already exists,
  `Simulation.cs:91`). Friction constant to tune: ~5-15 m/s² gives Melee-like drift
  tails at SLOPArena scale.
- **Option B — hybrid:** keep decay but make it linear (constant m/s² per second)
  instead of exponential. Easier to reason about, closer to Melee than exp, but
  still kills the tail.
- **3D note:** Melee friction is 2D (XY launch plane). In SLOPArena, decay the
  horizontal (XZ) component along its direction and leave `KVY` to gravity —
  decaying all three axes is what flattens launch arcs today.

Bounces: adopt the Melee split — while in hitstun, floor/wall bounces hard-set KV
(full reflection); after hitstun, soft-merge. Currently SLOPArena clears KV on
landing (`ProcessKnockback`, `Simulation.cs:778`).

---

## 4. DI & SDI — `ftCo_Damage.c`

### 4.1 Melee DI [verified]: rotate the launch direction at hitlag exit

`ftCo_8008E5A4` (`ftCo_Damage.c:605-640`, called from `ftCo_Damage_OnExitHitlag`):

```c
float f3 = kb_y * lstick.x + (-kb_x) * lstick.y;      // -(kb × stick).z
float f30 = f3 * f3 / kb_mag;                          // = |stick|²·sin²(angle)
// sign from (kb × stick).z
float angle = atan2f(kb_y, kb_x);
angle += deg_to_rad * ftd->x1A8 * f30;                 // x1A8 = 18° [community]
kb_vel = kb_mag * (cosf(angle), sinf(angle));          // magnitude preserved
```

Properties:
- Applied **once**, at hitlag exit, **on the launch angle** — magnitude preserved.
- Rotation is **toward the stick**, up to `18°`, weighted by `|stick|²·sin²(angle
  between stick and launch)` → perpendicular stick = full effect, parallel = none.
- This is the "DI at the moment you're hit" model — inputs during hitlag, not
  during flight.

### 4.2 SDI [verified]: per-hitlag-frame position shifts

`ftCo_Damage_OnEveryHitlag` (`ftCo_Damage.c:575-600`): every hitlag frame, if
`|stick| ≥ sdi_min_stick_mag` and within the stick window
(`x670/x671 < sdi_stick_window`): `pos += stick · sdi_pos_scale`, window reset.
Plus ASDI (`OnExitHitlag`): one extra shift at exit (`lstick`/`cstick · x4BC`).
(Also present but purpose unresolved: held L/R scales KB magnitude by `x1AC` at
hitlag exit — probably a leftover, x1AC may be 1.0.)

### 4.3 SLOPArena today — `Simulation.cs:632-651` (ADR-0013)

DI input is committed during hitstun, applied **at hitstun expiry** as *additive
velocity*: `KVX += DIX · 0.30 · launchMag` (horizontal only). Different timing
(expiry vs connect), different model (additive drift vs angle rotation).

### 4.4 Deltas

1. **Move DI to hit time**: capture the stick at connect (during `HitstopTicks`,
   ADR-0012 — the exact analog of Melee's hitlag) and rotate the launch vector
   toward the input direction by up to `θ_max` (~18°), weighted by `sin²`. 3D
   adaptation: rotate toward `(stickX, stickY·v, stickZ)` in the plane containing
   the launch vector; magnitude preserved. Deterministic → rollback-safe.
2. **SDI**: during `HitstopTicks`, each tick with active input shifts position by
   `stick · sdiScale`. Server-side, deterministic, reuses the existing hitstop
   freeze. Needs a small per-entity stick-window timer in `CharacterState`
   (Melee: re-input within N frames of the last SDI).
3. Keep or drop Combo Influence (ADR-0013) — with real DI at connect it's
   redundant; the expiry drift is what makes the current feel "soft".

---

## 5. Weight per character

### 5.1 Melee [verified]

- `co_attrs.weight` per character (`ft/types.h:725`), e.g. Jigglypuff 60 …
  Bowser 117.
- Enters KB via the `200/(W+100)` divisor — heavier = less KB, diminishing
  returns at high W.
- Also affects **throw animation speed**: throws play slower against heavy victims
  unless the move has the `weight_independent_throws_mask` bit
  (`ftCo_Throw.c:191-197`, `ft/types.h:772`).
- Shield pushback uses weight ratios between the two fighters (`ft/ft_07C1.c`).
- Note: weight does **not** affect hitstun (hitstun is pure KB) — heavy chars take
  less KB, so they get less hitstun *through* the formula, not directly.

### 5.2 SLOPArena delta

- Add `Weight` (float, ~60-120) to `CharacterDefinition`, default 100.
- Use the normalized divisor in §1.3: `weightFactor = 200/(W+100)` (W=100 → 1.0).
- Optionally: throw/grab anim speed by victim weight (matches the existing
  `AnimationClipConfig` speed modulation); ignore shield pushback until shields
  exist (Kistu's counter is the only defensive tool today).

---

## 6. Ranked engine deltas (to implement in order)

1. **Flight model** (§3, Option A): constant KV during hitstun + linear horizontal
   friction after, gravity does vertical work. Biggest single feel change.
   **→ LANDED (ADR-0019 §6).**
2. **Hitstun shape** (§2): pure KB function, min 1, drop floor 8; decide on
   `StunTicks` cap (keep as valve or remove).
   **→ LANDED with the melee-shape balance pass (ADR-0019 §2a): k = 0.7, +20
   magnitude floor, KV×0.11 velocity scale — matrix-validated on both characters
   (issue #147). Fixed tools (yanks/grabs) exclude the floor.**
3. **KB formula** (§1): weight divisor + move-damage term, keep profile system.
   **→ PARTIALLY LANDED (ADR-0019 §1): weight divisor `200/(W+100)` + damage term
   `Damage·0.1` live; profiles still exist (KnockbackProfile Light/Medium/Kill in
   use — the custom-only migration was deferred).**
4. **DI at connect + SDI** (§4): rotate-launch DI during hitstop, position-shift
   SDI per hitstop tick. → ADR-0019 §4-5.
5. **Weight stat** (§5): `CharacterDefinition.Weight` + normalization.
   → ADR-0019 §1 (weight 100 default, per-char values pending).
6. (deferred) IASA / landing lag / auto-cancel — already tracked in
   `melee-frame-analysis.md` §7 as the other half of Melee feel.
   → IASA + landing-lag + AC now authored per stage (FightGuy/Kistu tables);
   early-out press semantics verified by the combo matrix.

Each delta is pure sim-side (`src/Shared/`), no netcode or wire-format change
except `CharacterState` additions (SDI window timer, weight is static data).
Client rendering (KV-driven anims) is unaffected by the flight model change —
`PlayerRenderer` consumes state packets.

---

## 7. Verification & sources

### Melee (clone at `../melee-decomp`)

| Claim | File:line |
|---|---|
| KB formula macro | `src/melee/ft/ftcoll.c:2151-2159` |
| KB entry points | `ftColl_80079C70` (`ftcoll.c:2196`), `ftColl_80079AB0` (`:2161`), `ftColl_80079EA8` (`:2265`) |
| Hitbox field wiring (growth/base/weight-set) | `src/melee/ft/ftaction.c:332-343` |
| KB cap | `ftcoll.c:2187` |
| Hitstun = KB·0.4, min 1 | `src/melee/ft/chara/ftCommon/ftCo_Damage.c:296-299` |
| Hitstun timer tick | `ftCo_Damage.c:967-978` |
| Hitstun gates DamageFly→tumble | `ftCo_Damage.c:1158-1166` |
| Velocity replacement window (`xFC`) | `ftCo_Damage.c:221-235` |
| Linear KB friction | `src/melee/ft/fighter.c:2172-2183` |
| DI angle rotation | `ftCo_Damage.c:605-640` |
| SDI per hitlag frame | `ftCo_Damage.c:575-600` |
| ASDI + L/R scale at exit | `ftCo_Damage.c:601-650` |
| Hitlag formula | `src/melee/ft/ftcommon.c:646-649` |
| Weight in attrs | `src/melee/ft/types.h:725` |
| Throw speed by victim weight | `src/melee/ft/chara/ftCommon/ftCo_Throw.c:191-197` |
| Stale moves (out of scope) | `src/melee/pl/plstale.c`, `src/melee/ft/ft_0881.c:335-371` |
| `PlCo.dat` runtime load | `src/melee/ft/fighter.c:186` |

`PlCo.dat` constant values (xF4, xF8, x108, x110, x114, x11C, x120, x154, x1A8,
x204, sdi_*) are not in the repo — they ship on the disc. Structure is verified;
values marked [community]. Getting byte-exact values requires the ISO's `PlCo.dat`
(not in the clone).

### SLOPArena

| Claim | File |
|---|---|
| Current KB + hitstun | `src/Shared/Simulation.cs` `ApplyKnockback` (~:1092-1136) |
| Current DI (Combo Influence) | `Simulation.cs` `ProcessHitstun` (~:632-651) |
| Current flight decay | `Simulation.cs` `ProcessHitstun`/`ProcessKnockback` (~:614-617, 735-738) |
| Hitstop | [`../adr/0012-hitstop-per-pair-freeze.md`](../adr/0012-hitstop-per-pair-freeze.md) |
| Combo Influence decision | [`../adr/0013-combo-influence-additive-drift.md`](../adr/0013-combo-influence-additive-drift.md) |
| Existing frame-data research | [`melee-frame-analysis.md`](melee-frame-analysis.md) |

> **Stale doc note**: [`../systems/hitstun-di.md`](../systems/hitstun-di.md) describes the pre-ADR-0013 model
> (victim frozen, `KVX += DIX·3.5` at expiry). The current sim applies KB
> immediately with exponential decay and Combo Influence (0.30, launch-scaled) —
> see §4.3. That doc needs a refresh if the DI model changes again.
