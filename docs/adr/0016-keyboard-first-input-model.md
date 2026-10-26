# ADR-0016: Keyboard-First Input — 10 Hotkey Slots, Short Hop, Fast Fall

**Status:** Accepted — 2026-08-10 (implemented as issue #116); the four-normal layout is superseded by [ADR-0042](0042-controller-layout-three-normals-homing-dash.md)
**Deciders:** @Binoui

## Context

Smash-style directional attacks assume two sticks: move one way while attacking another. Keyboard chords cannot reproduce that — `LMB+W` is mushy because W is consumed by movement; a chord can only mean "attack in my movement direction", which gives zero independent/backward options.

In 3D with soft-lock + tracking rotation (`RotateTowardTarget`), attack direction is mostly the game's job: facing (input-only) + tracking determine where the hit lands, and moves differentiate by geometry (forward poke, wide swing, overhead, dive, 360°) rather than input direction. So the directional-command model is a 2D necessity that 3D lock-on renders redundant — **hotkeys are the correct model, not a compromise**.

Target: keyboard-competitive platform fighter with the WoW hotkey layout (proven under high APM); controllers supported later but the keyboard is the balance floor. Timing tech (short-hop release windows, fast-fall taps, dodge timing) is digital-optimal — box controllers run Melee's full tech layer competitively on exactly this principle. The one analog advantage (drift finesse, analog DI) is exactly what 3D lock-on shrinks.

## Decision

1. **10 move slots: `1 2 3 4 5 A E R F` + `LMB`/`RMB`.** Each slot is one move with ground/air variants — 20 moves per character. The slot architecture already exists (`byte ActiveSlot`, `GetSlotAbility(slot-1, airborne)`, `InputController.Poll()`); this is a rekey + expand, not a new system.
2. **No directional commands.** Attack direction = facing + tracking. Facing is already input-only; `RotateTowardTarget`/`TrackingStrength` (kept, ADR-0015) handle aim during attacks.
3. **Short hop:** Space release within a window (3–5 ticks) → reduced jump velocity. Release-timing is digital-optimal (box-controller proven).
4. **Fast fall:** down (camera-relative) while airborne → gravity multiplier toward `MaxFallSpeed`. Uses the existing `Down` bit — the crouch mapping is deprecated (ADR-0014), so it is free in the air.
5. **SOCD + 8-way contract:** opposites cancel to neutral (current behavior, codified); all movement and move design targets 8 directions. No analog-only tech (no light-press moves) is ever designed.
6. **Mouse = aim only** for projectile specials (the PC identity: platform-fighter movement + FPS-aimed specials). `Space` jump, `Shift` dodge, `C` burst — all unchanged.
7. **Controllers later map to the same slots.** `InputState` stays device-agnostic; a controller's buttons map 1:1 to the 10 slots + jump/dodge/burst. Keyboard remains the competitive floor.

**Role budget for the 10 slots** (per character): poke (fast, low cooldown), heavy/launcher, recovery (the ADR-0015 up-B analog), dive (downward geometry — the fast-fall attack, no chord), projectile (mouse-aim), mobility, defensive (parry/counter), buff/wildcard, + 2 free.

## Considered Options

- **Directional attacks (Smash model)** — rejected: chord ambiguity on keyboard (W is movement), and tracking makes input direction redundant in 3D lock-on.
- **Keep 6 slots (current Q/E/R/F + mouse buttons)** — rejected: too few for the FG move budget (8 roles minimum); auto-combo chains were masking the count.
- **Controller-first** — rejected: keyboard is the competitive floor; hotkeys are frame-consistent and zero-ambiguity, and timing tech is digital-optimal.
- **Analog emulation (partial-stick via pressure)** — rejected: needs analog hardware or awkward emulation; violates the 8-way contract.

## Consequences

- **Wire change:** `InputState` (19 B) gains a short-hop bit (release-detected client-side or a held-jump flag); fast fall reuses the existing `Down` bit. InputRelay + rollback replay carry it automatically.
- **Slot constants:** hardcoded slot indices (e.g., the F = Overclock special-case at `ServerSimulation.cs:468`) become named constants before slot count changes.
- **`InputController.Poll()` remap:** one switch, plus key-remap support (keys are currently hardcoded — ZQSD is a remap away for AZERTY).
- **Ergonomics:** left hand on ZQSD + 1-5 + A/E/R/F + Space/Shift/C; right hand on mouse (aim + LMB/RMB). ~15 inputs, all WoW-proven under high APM.
- **Data expansion:** 4 × 10 move definitions (ground/air variants) replace the chain data; the auto-combo removal (ADR-0015) and this slot expansion are one data pass.
- **Feel risk:** 8-way drift is coarser than analog; accepted (Rivals of Aether precedent). The short-hop release window is the single most feel-critical constant — tune 3–5 ticks in playtest.

## Implementation notes (issue #116, 2026-08-10)

- **Slot count is 11, not 10**: the listed key set `1 2 3 4 5 A E R F` + `LMB`/`RMB` is eleven keys; `AbilitySlots` defines ActiveSlot 1-11 (the "10" in the title was a miscount — the role budget has 10 entries). E/R/F keep their historical indices (4/5/6) so kit data and tests stayed stable.
- **Q rekey (issue #117)**: the Q key is slot 11 (`AbilitySlots.A`) — the QWERTY-Q position, which is the physical "A" key on AZERTY (the Azerty preset default). The projectile slot moved there; key `1` is now part of the FG-normal tier (issue #117): keys `1`-`4` = normals, `Q E R F` = abilities, `LMB`/`RMB` = base normals — the actual 10-key layout.
- **Down key = X** (dedicated `FastFall` binding): S is backward movement, and fast-fall-on-S would fire whenever the player drifts backward — the user rejected it ("can't be S.. maybe x for now"). The `Down` input bit is now driven ONLY by this key (human path); NPC/AI input still derives it from MoveY. Crouch stays deprecated (ADR-0014).
- **Short hop is server-side** via a `JumpHeld` wire bit + a serialized `JumpHeldTicks` counter. The decision runs at JumpSquat expiry and defers one tick at a time while the player is still holding inside the window (so a release just past squat expiry still short-hops); air double jumps are always full. Window constant `Simulation.ShortHopWindowTicks = 5`, velocity multiplier 0.7 — the feel-critical constants to tune.
- **Fast fall** multiplies the current gravity by 3 (`FastFallGravityMultiplier`) toward MaxFallSpeed, gated airborne + non-hitstun, in every state.
- **Remappable bindings** via a `ScriptableObject` (`InputBindings`, CreateAssetMenu → Resources/InputBindings or the InputController field); defaults are QWERTY-position keys (the physical ZQSD keys on AZERTY — Unity `Key` is position-based). The `A` slot key is the only layout-dependent default (Key.Q on AZERTY / Key.A on QWERTY). No in-game settings UI yet — config is the asset.
- **Wire changes:** `InputState` 19 → 20 B (flags2: JumpHeld); `CharacterStatePacket` 101 → 112 B (cooldowns 0-5 → 0-10 + JumpHeldTicks); downlink envelope up to 145 B. HUD still shows 6 cooldown icons (slots 6-10 have no data yet).
- **Outstanding:** key `5` data, Manki/Kistu/Nilus kit passes (same normal-tier schema), HUD slot-icon expansion beyond 6.
