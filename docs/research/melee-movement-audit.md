# Melee Movement Audit — Ground Dash→Run/Pivot, Air Accel/Friction, Fast Fall, Jumps

> Reference for design tickets **#136** ("Ground movement: dash→run→pivot adoption") and
> **#137** ("Air movement: accel/friction, fast fall, jump model") on the "Melee-based feel
> engine" design map (GitHub issue #128, Binoui/SlopArena).
>
> **Sources.** SLOPArena: `src/Shared/Simulation.cs`, `src/Shared/CharacterDefinition.cs`,
> per-character data under `src/Shared/Characters/`, ADRs in `docs/adr/`, prior research in
> `docs/research/`. Melee: byte-perfect 1.02 decomp clone at `../melee-decomp`; all Melee file
> refs below are relative to `melee-decomp/src/melee/`.
>
> **Verified vs community.** `[verified]` = read in the decompiled C (code structure and how
> each `ftCo_DatAttrs` field is consumed). Numeric values live in the disc's `PlCo.dat`
> (loaded at runtime, `ft/fighter.c:186`) and are marked `[community]`; values below come
> from the SSBWiki Melee character pages read directly (Fox/Jigglypuff/Captain Falcon
> "Changes from SSB" attribute sections; Marth/Bowser "Stats" tables) and the SSBWiki
> [Air dodge] page. Derived frame counts are computed from those values and marked
> `[derived]`.
>
> **Units.** Melee constants are in the game's internal units per 60 Hz frame (`u/f`). The
> ratios and derived *frame counts* transfer directly to SLOPArena ticks; absolute unit
> conversion does not. SLOPArena values are m/s and ticks (1 tick = 1/60 s).
>
> Scope matches the tickets: ground dash→run→pivot, air accel/friction, fast fall, jump
> model, air dodge (wavedash explicitly out of scope), gravity, ground friction. Hitstun/
> knockback/DI are covered by `melee-knockback-model.md`; frame timing of attacks by
> `melee-frame-analysis.md` (§7 lists the engine deltas — IASA and landing lag — that this
> doc does not re-litigate).

---

## 0. Roster snapshot

SLOPArena's four characters (`MovementStats`, `CharacterDefinition.cs:13-37`):

| Stat | Manki | FightGuy | Kistu | Nilus |
|---|---|---|---|---|
| WalkSpeed | 9 | 10 | 11 | 10 |
| SprintSpeed | 12 | 14 | 15 | 13 |
| DashSpeed | 30 | 32 | 34 | 32 |
| AirAcceleration | 14 | 16 | 18 | 17 |
| JumpForce | 10 | 12 | 13 | 12 |
| Gravity | 35 | 36 | 36 | 34 |
| AirFloatGravity | 0 | 0 | 0 | 0 |
| DashDurationTicks | 15 | 18 | 16 | 15 |
| DashCooldownTicks | 60 | 48 | 44 | 48 |
| GroundFriction | 14 | 16 | 16 | 15 |
| AirFriction | 0.4 | 0.5 | 0.5 | 0.45 |
| MaxFallSpeed | 45 | 48 | 48 | 46 |
| MaxJumps | 2 | 2 | 2 | 2 |
| JumpSquatTicks | 6 | 4 | 4 | 5 |
| FloatWindowTicks | 30 | 35 | 35 | 40 |
| FallRampDuration | 15 | 10 | 10 | 12 |

Sources: `MankiData.cs:36-53`, `FightGuyData.cs:20-38`, `KistuData.cs:25-44`,
`NilusData.cs:35-54`. Global constants: `DashDeceleration = 80` m/s² (`Simulation.cs:64`),
`DashInvincibilityTicks = 15` (`Simulation.cs:58`), `FastFallGravityMultiplier = 3`
(`Simulation.cs:76`), `ShortHopWindowTicks = 5` / `ShortHopVelocityMultiplier = 0.7`
(`Simulation.cs:71,73`), `SprintThresholdTicks = 12` / `TurnaroundLagTicks = 6`
(`Simulation.cs:81,84`), `AirDrag = 0.2` (`Simulation.cs:47`), `VelocityDeadZone = 0.015`
(`Simulation.cs:91`).

Reference Melee archetypes `[community]` (SSBWiki, NTSC; the four corners of the design
space we want SLOPArena's four characters to span):

| Stat (u/f) | Fox (fast) | Marth (mobile) | Jigglypuff (floaty) | Bowser (heavy) |
|---|---|---|---|---|
| Walk | 1.6 | 1.6 | 0.7 | 0.65 |
| Initial dash | 1.9 | 1.5 | 1.4 | 1.0 |
| Run (dash_run_terminal) | 2.2 | 1.8 | 1.1 | 1.5 |
| Traction (gr_friction) | 0.08 | 0.06 | 0.09 | 0.06 |
| Air speed (air_drift_max) | 0.83 | 0.9 | 1.35 | 0.8 |
| Air accel base (aerial_drift_base) | 0.08* | 0.02 | 0.28* | 0.02 |
| Air accel stick (air_drift_stick_mul) | — | 0.03 | — | 0.03 |
| Air friction (aerial_friction) | — | 0.005 | — | 0.01 |
| Gravity (grav) | 0.23 | 0.085 | 0.064 | 0.13 |
| Fall (terminal_vel) | 2.8 | 2.2 | 1.3 | 1.9 |
| Fast fall (fast_fall_velocity) | 3.4 | 2.5 | 1.6 | 2.4 |
| Jumpsquat (jump_startup_time) | 3 | 4 | 5 | 8 |
| Weight | 75 | 87 | 60 | 117 |

Sources: Fox — `ssbwiki.com/Fox_(SSBM)` ("Changes from Super Smash Bros.": walk 1.6,
initial dash 1.9, dash 2.2, traction 0.08, air speed 0.83, air accel 0.08, jump 31.28,
short hop 10.65, gravity 0.23, fall 2.8, fast fall 3.4, weight 75; jumpsquat 3 from the
Attributes prose). Marth — `Marth_(SSBM)` "Stats" table (incl. air friction 0.005 and
double jump 25.188). Jigglypuff — `Jigglypuff_(SSBM)` (walk 0.7, dash 1.1, initial dash
1.4, traction 0.09, jumpsquat 5, air speed 1.35, air accel 0.28, full hop 20.8, short hop
9.146, gravity 0.064, fall 1.3, fast fall 1.6, weight 60). Captain Falcon — `Captain_Falcon_(SSBM)`
(walk 0.85, initial dash 2.0, dash 2.3, traction 0.08, air speed 1.12, air accel 0.06,
full jump 38.52, gravity 0.13, fall 2.9, fast fall 3.5, weight 104). Bowser —
`Bowser_(SSBM)` "Stats" table (incl. air friction 0.01, double jump 28.77).
\*Fox/Jigglypuff/Falcon wiki pages give a single "air acceleration" figure (combined
stick-scaled term); Marth/Bowser pages split base/additional.

---

## 1. Ground movement: dash → run → pivot (#136)

### 1.1 Melee `[verified]` — three-tier ground FSM with two accel constants

`ftCo_DatAttrs` ground fields (`ft/types.h:691-704`): `walk_init_vel`, `walk_accel`,
`walk_max_vel`, `gr_friction`, `dash_initial_velocity`, `dash_run_acceleration_a`,
`dash_run_acceleration_b`, `dash_run_terminal_velocity`, `ground_max_horizontal_velocity`.

- **Dash entry** sets `gr_vel = facing · dash_initial_velocity` in one frame
  (`ftCo_Dash.c:63`), overwriting prior ground velocity (`ftCo_Dash.c:69-70`). During the
  dash, per frame it **accelerates** toward the run target, it does not decelerate:
  `getAccelAndTarget()` → `accel = stick·dash_run_acceleration_a ± dash_run_acceleration_b`,
  `target = stick·dash_run_terminal_velocity` (`ft/inlines.h:137-146`), applied with
  friction `gr_friction` (`ftCo_Dash.c:136-141`). The dash animation length (per-character
  dash anim, `ftCo_Dash.c:104-118`) gates the transition: after it, holding forward enters
  Run (`ftCo_Run.c:27-35`).
- **Run** uses the same accel/target pair, with accel tapered near target
  (`ftCo_Run.c:124-134`: `accel *= (1 - gr_vel/target)·x5C` while below target) — i.e. a
  smooth ease into run speed, not a snap.
- **Pivot / turnaround.** Opposite stick during dash → dash pivot into a new dash
  (`ftCo_Dash.c:29-36`, `ftCo_Turn_Enter_Smash`). Opposite stick during run → TurnRun:
  friction is applied while momentum opposes the new input, and the facing flips exactly
  when ground velocity crosses zero (`ftCo_TurnRun.c:89-115`, `:51-57`), then acceleration
  resumes the other way. Standing turn is a timed animation
  (`frames_to_change_direction_on_standing_turn`, `ftCo_Turn.c:70-75,92-104`).
- Dash-dance = the pivot between two dashes; **there is no dash cooldown** in Melee.

### 1.2 SLOPArena today `[verified]`

- Dash input (`input.Dash`, Shift) → `StartDash` (`Simulation.cs:1030-1074`): velocity set
  to `dir · DashSpeed` (30-34 m/s), full-state invincibility for 15 ticks, per-character
  `DashDurationTicks` (15-18) and **`DashCooldownTicks` (44-60)**.
- During the dash, `ProcessDash` **decelerates linearly** at `DashDeceleration = 80` m/s²
  (`Simulation.cs:786-810`); at duration expiry the character **hard-stops** to 0 velocity
  (`Simulation.cs:802-805`) — there is no run state after the dash.
- Ground locomotion is a two-speed snap: hold a direction 12 ticks (`SprintThresholdTicks`,
  `Simulation.cs:81`) → `IsSprinting`, and each tick velocity is **set, not accelerated**:
  `VX = dirX · (SprintSpeed | WalkSpeed)` (`Simulation.cs:917-921`). Direction change with
  sprint → 6 ticks of friction-only turnaround (`TurnaroundLagTicks`, `Simulation.cs:84`).
- Result: dash = huge burst (2.2-2.5× sprint speed) ending in a full stop; run = instant
  speed snap; no dash-dance possible (cooldown + hard stop); no run→dash momentum blending.

### 1.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| Dash start speed | `DashSpeed` 30-34 m/s (`Simulation.cs:1070`) | `dash_initial_velocity` 1.0-2.0 u/f (`ftCo_Dash.c:63`) |
| Dash speed vs run | 30-34 vs sprint 12-15 → **2.2-2.5×** | 1.0-2.0 vs run 1.1-2.3 → **0.8-1.5×** (Jigglypuff's initial dash is *faster* than run: 1.4 vs 1.1) `[community]` |
| Dash behavior | linear decel 80 m/s², then hard stop (`Simulation.cs:786-810`) | accel toward run target with gr_friction (`inlines.h:137-146`, `ftCo_Dash.c:136-141`) |
| Dash → run | none — dash ends in Idle + V=0 (`Simulation.cs:802-805`) | automatic at dash anim end if forward held (`ftCo_Run.c:27-35`) |
| Dash cooldown | 44-60 ticks (0.73-1.0 s) (`Simulation.cs:1047`) | none — dash-dance is core `[community]` |
| Run | instant snap to `SprintSpeed` after 12-tick hold (`Simulation.cs:911-921`) | accel `a·stick±b`, eased near target (`ftCo_Run.c:124-134`) |
| Pivot | 6-tick friction slowdown + snap (`Simulation.cs:84,900-910`) | friction through velocity zero, facing flips at crossing (`ftCo_TurnRun.c:51-57,89-115`) |

### 1.4 Melee-shaped SLOPArena sketch (for #136)

Replace the dash/sprint pair with Melee's three-tier model, all pure sim-side:

1. **Split `DashSpeed` into `DashInitialVelocity` (burst) + `RunSpeed` (sustained).**
   First-pass at SLOPArena scale: keep today's run speeds as `RunSpeed`
   (SprintSpeed 12-15), set `DashInitialVelocity ≈ 1.2-1.5 × RunSpeed` (Melee ratio
   1.16-1.5, Fox 1.9/2.2) instead of 30-34. Add `DashRunAccelerationA`/`B` (m/s²) and
   reuse `GroundFriction` as the traction term in the same
   `accel = a·dir + b`, `target = dir·RunSpeed` shape (`inlines.h:137-146`).
2. **Dash duration → dash animation length** (~15-18 ticks, keep `DashDurationTicks`);
   at expiry, holding forward transitions into Run **with velocity preserved**; release
   returns to Idle with friction. Delete the hard stop (`Simulation.cs:802-805`).
3. **Delete `DashCooldownTicks`** (or drop to 0 for the dash; the per-ability cooldown
   system is untouched). Dash-dance = pivot out of dash into a new dash, matching
   `ftCo_Dash_CheckInput` (`ftCo_Dash.c:29-36`).
4. **Turnaround = friction-through-zero pivot** (`ftCo_TurnRun.c:89-115`): replace the
   6-tick lag + snap with "apply friction until V crosses 0, flip facing at the crossing,
   then accelerate the new way". Standing turn (no momentum) keeps a timed
   `TurnaroundTicks` — that's `frames_to_change_direction_on_standing_turn`
   (`ftCo_Turn.c:70-75`), 4-8 frames per character `[community]`.
5. **Walk**: keep as the instant low-speed tier (it already reads like Melee's walk),
   or add `WalkAcceleration` for a soft start. Not required for the ticket.
6. Sprint-hold logic (`SprintThresholdTicks`, `IsSprinting`) becomes dead — remove it.

Wire/rollback impact: `IsSprinting`, `DirHoldTicks`, `TurnaroundTicks`, `DashCooldownTicks`
are already on the wire (ADR-0011); add `DashAccelA/B` to `MovementStats` only. No netcode
change required.

---

## 2. Air movement: accel/friction/drift (#137)

### 2.1 Melee `[verified]` — base + stick-scaled accel toward a drift cap

Every airborne frame (Fall, JumpAerial, AttackAir, EscapeAir-after-dodge, damage states) runs
`ft_80084DB0` (`ft_081B.c:1369-1380`): fast-fall check → gravity/terminal clamp → horizontal
drift. The drift step `ftCommon_8007D268` → `ftCommon_8007D28C` (`ftcommon.c:381-396`):

```
accel  = stick_x · air_drift_stick_mul  +  sign(stick_x) · aerial_drift_base
target = stick_x · air_drift_max
→ ftCommon_8007D174(vel, accel, target, aerial_friction)   // ftcommon.c:349-377
```

- The **base term** (`aerial_drift_base`) means air accel exists even at tiny stick
  deflections; the **stick term** scales with deflection (analog finesse).
- `air_drift_max` caps max air speed; `air_max_horizontal_velocity` is a global clamp in
  the same helper (`ftcommon.c:372-376`).
- `aerial_friction` (0.005-0.01 u/f) applies **linearly** only when accelerating past the
  target or with no input — it's a gentle decay, not a per-frame multiplier.
- Air facing is stick-based; momentum from a dash carries into the air via
  `ground_to_air_jump_momentum_multiplier` (see §4).

### 2.2 SLOPArena today `[verified]`

`ProcessAirMovement` (`Simulation.cs:955-978`):

```
target = dir · stats.WalkSpeed            // air speed == WALK speed, no separate stat
VX = MoveToward(VX, target, AirAcceleration · dt)
VX *= (1 - AirDrag · dt)                  // multiplicative drag, AirDrag = 0.2
```

- The air target speed is **WalkSpeed** (9-11 m/s) — there is no air-speed stat, so
  air/walk are coupled.
- `AirAcceleration` (14-18) is a **constant-rate** m/s² toward target; no base/stick
  split (keyboard is 8-way anyway — ADR-0016; the base term matters less than the shape).
- `AirDrag = 0.2` is a **multiplicative** per-tick drag (`v *= 1-0.2·dt` ≈ 0.33%/tick) —
  exponential decay on top of accel, unlike Melee's linear friction term.
- `AirFriction` (0.4-0.5) is only used in the fixed-aim branch (`Simulation.cs:850-863`).

### 2.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| Max air speed | `WalkSpeed` 9-11 m/s (`Simulation.cs:959-960`) | `air_drift_max` 0.8-1.35 u/f (`ftcommon.c:393`) |
| Air speed / run | ~0.75-0.85 (walk/sprint) | 0.38 (Fox) to 1.23 (Jigglypuff) `[derived]` |
| Accel model | constant `AirAcceleration` m/s² (`Simulation.cs:957`) | `stick·air_drift_stick_mul + sign·aerial_drift_base` (`ftcommon.c:384-394`) |
| Drag/friction | multiplicative `× (1-AirDrag·dt)` (`Simulation.cs:963-965`) | linear `aerial_friction` term in the accel helper (`ftcommon.c:355-370`) |
| Air speed stat | none — reuses WalkSpeed | dedicated `air_drift_max` + global `air_max_horizontal_velocity` (`types.h:721,724`) |

### 2.4 Melee-shaped SLOPArena sketch (for #137)

1. **Add `AirSpeedMax` to `MovementStats`** (m/s), decoupled from WalkSpeed. First-pass
   spread: Manki 6.5, FightGuy 7.5, Kistu 8.5, Nilus 7.0 — ratios ~0.55-0.65 of run,
   echoing Melee's air-slower-than-ground norm (Fox 0.38, Marth 0.5; Jigglypuff is the
   deliberate outlier at 1.23).
2. **Split `AirAcceleration` into `AirAccelStick` + `AirAccelBase`** with the Melee shape
   `accel = dir·AirAccelStick + sign·AirAccelBase`, target `dir·AirSpeedMax`
   (`ftcommon.c:384-394`). On an 8-way keyboard, the base term doubles as the
   neutral-stick drift killer; keep the numbers small (base ≈ 0.2 × stick).
3. **Replace multiplicative `AirDrag` with a linear friction term** in the same
   MoveToward-style helper (`ftcommon.c:349-377` shape): friction only when
   overshooting target or with no input; drop `AirDrag` (`Simulation.cs:47,963-965`).
4. Keep the per-character `AirFriction` stat and use it as Melee's `aerial_friction`
   everywhere (also in the fixed-aim branch, which already uses it — `Simulation.cs:858-862`).
5. Optional Melee lever: **momentum carry on jump** `ground_to_air_jump_momentum_multiplier`
   (`types.h:711`, consumed at `ftCo_Jump.c:105`) — SLOPArena already preserves dash
   momentum into the air (`Simulation.cs:1072-1073` keeps VX/VZ through squat), so this is
   a tuning constant, not new machinery.

---

## 3. Fast fall

### 3.1 Melee `[verified]` — velocity is **set**, per character

- Down input while airborne and falling (`ftcommon.c:517-530`, `ftCommon_CheckFallFast`)
  flips a `fall_fast` latch; while latched, every frame sets
  `VY = -fast_fall_velocity` (`ftcommon.c:488-491` `ftCommon_FallFast`, called from
  `ft_80084DB0`, `ft_081B.c:1374-1375`). It is a **set-velocity** model, not a gravity
  multiplier.
- Available in every airborne state that runs the common phys: Fall
  (`ftCo_Fall.c:209-210`), FallAerial (`ftCo_FallAerial.c:38-39`), **and during aerials**
  (`ftCo_AttackAir.c:192-193`).
- Fast fall is per-character: Fox 3.4, Marth 2.5, Jigglypuff 1.6, Bowser 2.4 u/f —
  only **1.14-1.26×** normal fall speed `[community]`.

### 3.2 SLOPArena today `[verified]`

- `input.Down` airborne (non-hitstun) multiplies **gravity** by `FastFallGravityMultiplier = 3`
  toward the `MaxFallSpeed` cap (`Simulation.cs:1244-1255`). It's a per-tick gate, not a
  latch — release immediately cancels (ADR-0016 implemented it exactly this way).
- Because it scales current gravity, fast-fall acceleration is slowest at the top of a
  jump arc (VY ≈ 0) and fastest once falling — the opposite ramp of Melee, where the
  velocity target applies instantly.

### 3.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| Mechanism | `gravity × 3` toward cap (`Simulation.cs:1249`) | `VY = -fast_fall_velocity` each frame (`ftcommon.c:488-491`) |
| Stat | none (global multiplier) | per-character `fast_fall_velocity` (`types.h:723`) |
| Relative speed | 3× gravity — **no defined terminal** (cap is shared with normal fall) | 1.14-1.26× normal fall `[community]` |
| Gate | per-tick `input.Down` (`Simulation.cs:1248`) | latched on down-press while falling, `x88`/`x8C` window (`ftcommon.c:519-529`) |
| During aerials | yes (`Simulation.cs:1245-1246`) | yes (`ftCo_AttackAir.c:192-193`) |

### 3.4 Melee-shaped SLOPArena sketch (for #137)

Add `FastFallSpeed` (m/s) to `MovementStats` (≈ 1.15-1.25 × `MaxFallSpeed`), and in
`ApplyGravity` replace the multiplier with:

```csharp
if (input.Down && s.HitstunTicks == 0 && s.VY < 0f)
    s.VY = -stats.FastFallSpeed;      // set-velocity, Melee shape
```

Remove `FastFallGravityMultiplier` (`Simulation.cs:76`). Keep the per-tick gate or add the
Melee latch — per-tick is simpler and rollback-identical; the latch is only needed if
"fast-fall persists after release" becomes a feel goal. The set-velocity shape is the
important delta: it makes fast fall frame-identical regardless of jump phase, which is what
enables Melee's SHFFL timing to transfer (`melee-frame-analysis.md` §7).

---

## 4. Jump model: squat, short hop, full hop, double jump (#137)

### 4.1 Melee `[verified]`

- **Jump squat**: `jump_startup_time` frames locked in KneeBend, then airborne
  (`ftCo_KneeBend.c:31-37`). Fox 3, Marth 4, Jigglypuff 5, Bowser 8 `[community]`.
- **Impulse** (`ftCo_Jump.c:96-144`, `ftCo_800CB110`):
  - horizontal: `VX += stick · jump_h_initial_velocity`, clamped to `jump_h_max_velocity`;
    existing momentum scaled by `ground_to_air_jump_momentum_multiplier` (`:105`).
  - vertical: full hop = `jump_v_initial_velocity`; **short hop = `hop_v_initial_velocity`**
    (a separate constant, `:117-121`). Short hop is decided by jump-button **release during
    the squat** (`ftCo_KneeBend.c:53-64`).
- **Double jump**: `VX = stick · air_jump_h_multiplier`, `VY = jump_v_initial_velocity ·
  air_jump_v_multiplier` (`ftCo_JumpAerial.c:106-109,173-185`) — a **separate, weaker
  impulse** with its own horizontal control, not a repeat of the ground jump.
- `max_jumps` per character (Jigglypuff 6 total via `types.h:716` + `ftCo_JumpAerial.c:69`).

Frame math `[derived]` from `[community]` values: Fox full-hop apex 16.5 f, air ≈ 33 f
(v ≈ 3.79 u/f from height 31.28); short-hop apex ≈ 9.6 f, air ≈ 19 f (v ≈ 2.21) → short/full
velocity ratio **0.58**. Marth full-hop air 57-59 f, short-hop air 36-38 f (wiki's own jump
frame table confirms). Bowser short/full velocity ratio 0.58.

### 4.2 SLOPArena today `[verified]`

- Squat: `JumpSquatTicks` 4-6 (`Simulation.cs:344-347`), momentum preserved through the
  squat (`Simulation.cs:348` comment).
- Short hop: release within `ShortHopWindowTicks = 5` of press → `JumpForce × 0.7`
  (`Simulation.cs:250-260`) — digital release-timing, per ADR-0016.
- Full hop: `VY = JumpForce` (10-13 m/s) at squat expiry (`Simulation.cs:260`).
- **Double jump is the ground jump repeated**: `VY = JumpForce` and `VX/VZ` **snapped to
  `dir · WalkSpeed`** (`Simulation.cs:351-360`) — kills drift momentum and reuses the full
  jump impulse.
- Derived `[derived]`: apex times Manki 17.1 t, FightGuy 20 t, Kistu 21.7 t, Nilus 21.2 t
  (JumpForce/Gravity); full air time ≈ 0.57-0.72 s. Short hop = 0.7× velocity → 0.49×
  height.

### 4.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| Squat | 4-6 t (`Simulation.cs:346`) | 3-8 f `[community]` — in range |
| Short-hop trigger | release ≤ 5 t of press (`Simulation.cs:250`) | release during squat (`ftCo_KneeBend.c:53-64`) — same shape |
| Short-hop size | 0.70 × full velocity (`Simulation.cs:73`) | separate `hop_v_initial_velocity`, ratio ≈ 0.58 `[derived]` |
| Full jump | `JumpForce`, 10-13 m/s (`Simulation.cs:260`) | `jump_v_initial_velocity` per char (`ftCo_Jump.c:117-121`) |
| Double jump | same `JumpForce` + V snapped to `dir·WalkSpeed` (`Simulation.cs:351-360`) | weaker `× air_jump_v_multiplier` + `stick·air_jump_h_multiplier`, momentum-adding (`ftCo_JumpAerial.c:106-109`) |
| Horizontal carry | momentum kept through squat, then air drift (`Simulation.cs:348`) | momentum scaled by `ground_to_air_jump_momentum_multiplier` (`ftCo_Jump.c:105`) |

### 4.4 Melee-shaped SLOPArena sketch (for #137)

1. **Add `ShortHopForce`** as its own stat instead of a multiplier; first-pass ≈ 0.58-0.62 ×
   `JumpForce` (Melee 0.58 `[derived]`; today's 0.7 makes SLOPArena short hops ~45% taller
   than Melee-relative). Keep the release-window trigger (already digital-optimal, ADR-0016).
2. **Double jump becomes a separate impulse**: add `AirJumpVMultiplier` (~0.75-0.85) and
   `AirJumpHMultiplier` (~0.85) to `MovementStats`, applied in the air-jump branch
   (`Simulation.cs:351-360`) as `VY = JumpForce·AirJumpVMultiplier` and
   `VX += dir·AirJumpHMultiplier` (additive, not a snap) — matching
   `ftCo_JumpAerial.c:106-109`. This restores drift preservation on double jump.
3. Optional: per-character `MaxJumps > 2` (Melee supports it, `types.h:716`) — the sim
   already counts `JumpsLeft`; the data files just set 2 everywhere. Jigglypuff-style
   floaty recovery would come free.
4. Keep squat + momentum carry as-is; squat is already Melee-shaped.

---

## 5. Air dodge

### 5.1 Melee `[verified]`

- Entry (`ftCo_EscapeAir.c:33-45`): momentum is **halted**; with a stick direction,
  `VX/VY = escapeair_force · (cos, sin)(stick angle)` (PlCo.dat constants: `escapeair_force`,
  `escapeair_deadzone`); neutral stick → V = 0 (hover).
- Per-frame velocity decay while intangible: `V *= escapeair_decay` (`ftCo_EscapeAir.c:103-107`).
- Animation 48-49 f, intangible frames 4-29 (26 f) for nearly the whole cast; Peach/Zelda
  4-19 (16 f); Mewtwo 39 f total `[community]` (SSBWiki Air dodge page).
- After the dodge: helpless Fall state (`ftCo_EscapeAir.c:114-117` → `ftCo_80099D70` →
  `ftCo_LandingFallSpecial_Enter` with landing lag `p_ftCommonData->x344`), landing lag
  per character `[community]`.
- Wavedash/waveland = air dodge into the ground (landing lag replaces the helpless fall) —
  **ruled out of scope** for SLOPArena per ticket #128.

### 5.2 SLOPArena today `[verified]`

`ActionState.AirDodging` **exists but is never entered**: the enum value
(`ActionState.cs:10`), a no-op `ProcessAirDodge()` (`Simulation.cs:816-820`), the
`AirDodgesLeft` resource (`CharacterState.cs:33`, reset to 1 on ground/hit,
`Simulation.cs:50,780,883`), and rollback classification (`Rollback/ActionStateClassifier.cs:15`).
No code path assigns `State = ActionState.AirDodging` or decrements `AirDodgesLeft`
(searched `src/` — zero assignments outside the classifier/state definitions). The dodge
input (Shift) is wired to `input.Dash` (ADR-0016: "Shift dodge"), which in the air executes
**`StartDash` — an air dash**: `V = dir · DashSpeed` (30-34 m/s), `VY = 0`, 15 ticks of
invincibility, decel (`Simulation.cs:1030-1074`, `ProcessDash`).

### 5.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| State exists | enum + no-op + resource (`Simulation.cs:816-820`) | full FSM (`ftCo_EscapeAir.c`) |
| Impulse | n/a (air dodge never entered; Shift = air dash at 30-34 m/s) | `escapeair_force` from stick, momentum halted (`ftCo_EscapeAir.c:33-45`) |
| Duration / intangibility | air dash: 15 t invincible (`Simulation.cs:58`) | 48-49 f, intangible 4-29 `[community]` |
| Decay | n/a | `V *= escapeair_decay` (`ftCo_EscapeAir.c:103-107`) |
| Landing | air dash → dash end hard stop | helpless Fall + landing lag x344 (`ftCo_EscapeAir.c:114-117`) |

### 5.4 Melee-shaped sketch (not a ticket priority)

If/when air dodge is implemented (#128 lists it as "exists — no wavedash"): give Shift a
separate airborne branch — `StartAirDodge` with per-character `AirDodgeImpulse` (m/s) along
the input direction, neutral → hover, ~30-45 t total with 20-26 t intangibility, per-frame
decay, and a small landing lag; keep `AirDodgesLeft = 1` per airtime (already tracked) and
the existing rollback predictability (already classified). The air dash then needs its own
input or stays as the "dash in air" behavior — a design decision for the ticket. No
wavedash.

---

## 6. Gravity (three-phase float/ramp vs single + terminal)

### 6.1 Melee `[verified]`

Single per-character `grav` (0.064-0.23 u/f²) applied every airborne frame, velocity clamped
at `terminal_vel` (`ftcommon.c:483-486`, `ft_081B.c:1376-1378`). **No float window, no
ramp** — the arc is a pure parabola from the jump impulse. Aerial actions (including
aerials) do not alter gravity.

### 6.2 SLOPArena today `[verified]`

Three-phase ramp (ADR-0001), `ApplyGravity` (`Simulation.cs:1217-1257`):
`AirTimeTicks < FloatWindowTicks` → `AirFloatGravity`; `< FW+FallRampDuration` → lerp;
else full `Gravity`. **In practice the float window only fires for recovery moves**:
- Ground jump and double jump set `AirTimeTicks = FW + FallRampDuration` at takeoff
  (`Simulation.cs:263,359`) — the first airborne tick already exceeds the ramp, so **jumps
  get full gravity immediately**.
- `AirTimeTicks = 0` only on: hit/knockback (`Simulation.cs:1131`), landing
  (`Simulation.cs:486,495,507`), and **recovery moves** (`IsRecoveryMove` →
  `ServerSimulation.cs:134-135`, per issue #115 "normal air attacks no longer hover").
- So ADR-0001's "reset on any attack/dash/jump" description is superseded: today the float
  is a **recovery-move + post-hit tool**, not a general aerial tool.
- ADR-0001's example numbers (Manki FW=5, FightGuy FW=4) are stale — data files now carry
  FW 30-40, ramp 10-15.

### 6.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| Model | 3-phase float/ramp/full (`Simulation.cs:1221-1242`) | single `grav` + `terminal_vel` clamp (`ftcommon.c:483-486`) |
| Gravity value | 34-36 m/s² | 0.064-0.23 u/f² `[community]` |
| Fall cap | `MaxFallSpeed` 45-48 m/s (`Simulation.cs:1254-1255`) | `terminal_vel` 1.3-2.9 u/f `[community]` |
| Float | recovery moves + post-hit only (`ServerSimulation.cs:134-135`, `Simulation.cs:1131`) | none |
| Aerial-action gravity | unchanged by actions (float is the exception, not the rule) | unchanged, always |

### 6.4 Melee-shaped sketch (for #137 — decision point)

Melee's shape is **single gravity + terminal cap**; SLOPArena's ramp is a deliberate
DKO-derived design (ADR-0001) that the float-reset change (issue #115) already narrowed to
recovery moves. Two options, both Melee-compatible in feel:

- **Option A (closest to Melee):** drop the ramp for normal airtime — jumps get full
  `Gravity` immediately (already true today, `Simulation.cs:263`), keep the float window
  solely as the recovery-move mechanic (already true today). This is mostly **deleting dead
  ramp machinery** — `FallRampDuration` lerp (`Simulation.cs:1232-1237`) never engages for
  normal jumps today; verify against `FloatWindowTicks` usage, then simplify.
- **Option B (keep the ramp as a character lever):** retain three-phase but give jumps a
  real float window (set `AirTimeTicks = 0` at takeoff instead of `FW+FR`), which changes
  the jump arc shape (later apex, floatier) — a feel regression risk for fast fallers.
  Recommend **A** unless a floaty character archetype needs B.
- Either way: keep per-character `Gravity` + `MaxFallSpeed` (the Melee pair), and keep the
  terminal clamp — it's already the Melee shape.

---

## 7. Ground friction / traction

### 7.1 Melee `[verified]`

`gr_friction` (0.06-0.09 u/f `[community]`) applied as a **linear per-frame reduction**:
`gr_vel += -gr_vel·x54·friction` while dashing (`ftCo_Dash.c:155-158`), and via
`ftCommon_ApplyFrictionGround` in RunBrake/TurnRun/Wait. Linear, not exponential: from run
speed it's a constant deceleration until the stop threshold. Stop time from run `[derived]`:
Fox 2.2/0.08 ≈ 27.5 f, Marth 1.8/0.06 = 30 f, Jigglypuff 1.1/0.09 ≈ 12 f.

### 7.2 SLOPArena today `[verified]`

`GroundFriction` (14-16) applied as `MoveToward(V, 0, |V| · GroundFriction · dt)`
(`Simulation.cs:846-848, 928-933`) — a **multiplicative/exponential** decay (per-tick
fraction ≈ 14-16/60 ≈ 0.23-0.27, halving every ~2.6 t). From sprint to the dead zone
(0.015, `Simulation.cs:91`) takes ≈ 22-25 t `[derived]`. Shape difference: Melee
coasts linearly (constant m/s² bleed), SLOPArena bleeds fast up front then asymptotes
(why the `VelocityDeadZone` exists, `Simulation.cs:88-91`).

### 7.3 Side-by-side

| Aspect | SLOPArena today | Melee |
|---|---|---|
| Stat | `GroundFriction` 14-16 (`Simulation.cs:929`) | `gr_friction` 0.06-0.09 u/f `[community]` |
| Shape | exponential (`\|V\|·f·dt` fraction, `Simulation.cs:929-931`) | linear per-frame (`ftCo_Dash.c:155-158`) |
| Stop from run | ≈ 22-25 t incl. tail `[derived]` | ≈ 12-30 f `[derived]` |
| Dead-zone hack | required (`VelocityDeadZone`, `Simulation.cs:88-91`) | snap below threshold in the KB path; ground friction is exact-linear |
| During attacks | deliberately disabled (issue #115: attacks preserve drift, `Simulation.cs:311-312`) | state-dependent per motion FSM |

### 7.4 Melee-shaped sketch (for #136)

Switch ground friction to **linear**: `V -= sign(V)·GroundFriction·dt`, snap to 0 below a
small threshold (keep `VelocityDeadZone` as the snap). Retune `GroundFriction` to m/s² so
stop time from run ≈ 20-30 t (≈ 6-9 m/s² at today's sprint speeds). This is what produces
Melee's signature "coast" after a dash-cancel, and it makes the TurnRun pivot
(§1.4.4) behave correctly — the pivot reads velocity-crossing-zero, which an exponential
decay makes mushy. Air friction should get the same linear treatment (§2.4.3) so the two
decays share one shape.

---

## 8. Ranked gaps (biggest first)

1. **No run state / dash hard-stop + dash cooldown** (#136). SLOPArena's dash is a
   self-contained 0.25-0.3 s burst that ends in a full stop (`Simulation.cs:802-805`) and
   locks out for 0.73-1.0 s (`Simulation.cs:1047`). Melee's dash is the *entry* to run
   (accel toward `dash_run_terminal_velocity`, `inlines.h:137-146`) with **no cooldown** —
   the entire dash-dance/pivot layer (the biggest single ground-feel delta) is impossible
   today.
2. **Dash magnitude**: 2.2-2.5× run vs Melee's 1.16-1.5× (initial dash 1.0-2.0 vs run
   1.1-2.3) `[community]`. Dash in SLOPArena is a teleport-ish burst; in Melee it's a
   smooth ramp into run.
3. **Air speed is coupled to walk + exponential drag** (#137). No `AirSpeedMax` stat
   (`Simulation.cs:959-960` reuses `WalkSpeed`), and `AirDrag` is multiplicative
   (`Simulation.cs:963-965`) vs Melee's linear `aerial_friction` (`ftcommon.c:355-370`).
   Blocks both per-character air identity and Melee-shaped drift curves.
4. **Fast fall is a gravity multiplier, not a velocity** (#137). `×3 gravity`
   (`Simulation.cs:1249`) has no defined terminal and ramps with jump phase; Melee sets
   `VY = -fast_fall_velocity` (`ftcommon.c:488-491`), 1.14-1.26× fall speed `[community]`.
   This is the SHFFL-feel lever.
5. **Short hop too tall** (#137): 0.7× multiplier (`Simulation.cs:73`) vs Melee's 0.58
   velocity ratio `[derived]`. And **double jump is the full jump repeated with a velocity
   snap** (`Simulation.cs:351-360`) — Melee uses a weaker impulse + additive horizontal
   control (`ftCo_JumpAerial.c:106-109`).
6. **Air dodge is scaffolding** — `AirDodging` never entered, Shift = air dash at 30-34 m/s
   (`Simulation.cs:816-820`, `:1030-1074`). Melee: momentum-halt + stick impulse +
   intangibility 4-29 `[community]`. (Wavedash out of scope.)
7. **Friction shape**: exponential ground decay (`Simulation.cs:929-931`) vs Melee's linear
   `gr_friction` (`ftCo_Dash.c:155-158`); the exponential tail is what forces the dead-zone
   hack (`Simulation.cs:88-91`) and would make a pivot unreadable.
8. **Float/ramp machinery mostly dead**: jumps bypass the float window
   (`Simulation.cs:263,359`); only recovery moves reset `AirTimeTicks`
   (`ServerSimulation.cs:134-135`). Either delete the ramp (Option A, §6.4) or give it a
   real job — ADR-0001's original description no longer matches the code.

Cross-cutting note: every delta in §1-§4 is pure `src/Shared/` sim + `MovementStats`
fields; the netcode already carries the movement-resource fields these states need
(ADR-0011). Nothing here requires a wire change except new `MovementStats` fields.

---

## 9. Verification & sources

### SLOPArena (repo root = `/home/binoui/Documents/projects/SlopArena`)

| Claim | File:line |
|---|---|
| `MovementStats` fields | `src/Shared/CharacterDefinition.cs:13-37` |
| Manki / FightGuy / Kistu / Nilus stats | `src/Shared/Characters/MankiData.cs:36-53`, `FightGuyData.cs:20-38`, `KistuData.cs:25-44`, `NilusData.cs:35-54` |
| Global constants (AirDrag, DashDeceleration, FastFall×3, ShortHop 5/0.7, Sprint 12, Turnaround 6, DeadZone) | `src/Shared/Simulation.cs:47,64,71-76,81-84,91` |
| Jump squat entry + short-hop decision | `Simulation.cs:239-266` |
| Ground jump / air double jump impulses | `Simulation.cs:340-360` |
| Dash state processing (decel, hard stop) | `Simulation.cs:786-810` |
| `ProcessAirDodge` no-op | `Simulation.cs:816-820` |
| Air dodge landing cleanup | `Simulation.cs:519-523` |
| Ground movement (instant speed, sprint, turnaround) | `Simulation.cs:878-953` |
| Air movement (WalkSpeed target, AirDrag) | `Simulation.cs:955-978` |
| `StartDash` (ground/air, DashSpeed impulse, invincibility) | `Simulation.cs:1030-1074` |
| Three-phase gravity + fast fall + terminal cap | `Simulation.cs:1217-1257` |
| Float reset on hit / recovery move | `Simulation.cs:1131`, `src/Shared/ServerSimulation.cs:134-135` |
| AirDodging never assigned (grep `State = ActionState.AirDodging` over `src/`) | zero hits |
| FallRamp ADR (stale example numbers) | `docs/adr/0001-fall-ramp-system.md` |
| Short hop / fast fall ADR decisions | `docs/adr/0016-keyboard-first-input-model.md` |
| Prior engine-delta analysis (IASA, landing lag) | `docs/research/melee-frame-analysis.md` §7 |
| Prior KB/flight research (same citation conventions) | `docs/research/melee-knockback-model.md` |

### Melee (decomp root = `/home/binoui/Documents/projects/melee-decomp/src/melee`)

| Claim | File:line |
|---|---|
| `ftCo_DatAttrs` struct (all movement fields) | `ft/types.h:690-756` (gr_friction 697, dash_initial_velocity 698, dash_run_acceleration_a/b 699-700, dash_run_terminal_velocity 701, jump_startup_time 708, jump_h/v_initial_velocity 709-710, ground_to_air_jump_momentum_multiplier 711, jump_h_max_velocity 712, hop_v_initial_velocity 713, air_jump_v/h_multiplier 714-715, max_jumps 716, grav 717, terminal_vel 718, air_drift_stick_mul 719, aerial_drift_base 720, air_drift_max 721, aerial_friction 722, fast_fall_velocity 723, air_max_horizontal_velocity 724, standing turn frames 727, weight 728) |
| `getAccelAndTarget` (dash/run accel pair + terminal target) | `ft/inlines.h:137-146` |
| Dash entry (initial velocity set), dash phys, dash friction | `ft/chara/ftCommon/ftCo_Dash.c:63-70,136-141,155-158` |
| Dash pivot on opposite stick | `ftCo_Dash.c:29-36` |
| Dash→run transition | `ftCo_Run.c:27-35` |
| Run phys (tapered accel) | `ftCo_Run.c:124-134` |
| Run pivot (friction through zero, facing flip at crossing) | `ftCo_TurnRun.c:51-57,89-115` |
| Standing turn frames | `ftCo_Turn.c:70-75,92-104` |
| Jump impulse (momentum carry, hop vs full, h clamp) | `ftCo_Jump.c:96-144` |
| Squat exit + short-hop release detection | `ftCo_KneeBend.c:31-37,53-64` |
| Double jump impulse | `ftCo_JumpAerial.c:106-109,173-185` |
| Common air phys (fast fall / gravity / drift) | `ft/ft_081B.c:1369-1380` |
| `ftCommon_Fall` / `FallFast` / `CheckFallFast` | `ft/ftcommon.c:483-491,517-530` |
| Air drift shape (base + stick accel, drift max, friction) | `ftcommon.c:381-396`; clamp/overshoot logic `ftcommon.c:349-377` |
| Fast fall during aerials | `ftCo_AttackAir.c:192-193`; fall `ftCo_Fall.c:209-210` |
| Air dodge impulse / decay / landing | `ftCo_EscapeAir.c:33-45,103-107,114-117` |
| PlCo.dat runtime load | `ft/fighter.c:186` |

### Community (read directly 2026-08-13)

| Claim | Source |
|---|---|
| Fox: walk 1.6, initial dash 1.9, dash 2.2, traction 0.08, air speed 0.83, air accel 0.08, full jump 31.28, short hop 10.65, gravity 0.23, fall 2.8, fast fall 3.4, weight 75 | ssbwiki.com/Fox_(SSBM) — "Changes from Super Smash Bros." + Attributes prose (jumpsquat 3) |
| Marth: weight 87, dash 1.5/1.8, walk 1.6, traction 0.06, air friction 0.005, air speed 0.9, air accel 0.02/0.03, gravity 0.085, fall 2.2/2.5, jumpsquat 4, jump 35.09/13.995, double jump 25.188, full-hop air 59 f / short-hop 38 f | ssbwiki.com/Marth_(SSBM) — "Stats" + "Jump Frame Data" tables |
| Jigglypuff: walk 0.7, initial dash 1.4, dash 1.1, traction 0.09, jumpsquat 5, air speed 1.35, air accel 0.28, full hop 20.8, short hop 9.146, gravity 0.064, fall 1.3, fast fall 1.6, weight 60 | ssbwiki.com/Jigglypuff_(SSBM) — "Changes from Super Smash Bros." |
| Captain Falcon: walk 0.85, initial dash 2.0, dash 2.3, traction 0.08, air speed 1.12, air accel 0.06, full jump 38.52, gravity 0.13, fall 2.9, fast fall 3.5, weight 104 | ssbwiki.com/Captain_Falcon_(SSBM) — "Changes from Super Smash Bros." |
| Bowser: weight 117, dash 1.0/1.5, walk 0.65, traction 0.06, air friction 0.01, air speed 0.8, air accel 0.02/0.03, gravity 0.13, fall 1.9/2.4, jumpsquat 8, jump 31.57/10.66, double jump 28.77 | ssbwiki.com/Bowser_(SSBM) — "Stats" table |
| Air dodge: anim 48-49 f (Mewtwo 39), intangible 4-29 (26 f), Peach/Zelda 4-19 (16 f); momentum halt + stick boost; helpless fall after | ssbwiki.com/Air_dodge — "In Super Smash Bros. Melee" tables |

`[derived]` values (marked in text): jump apex/air-time frames from `v = √(2·g·h)`;
stop-time frames `run/friction`; SLOPArena apex ticks `JumpForce/Gravity`; exponential
decay tick counts from `(1 - f·dt)^n`.
