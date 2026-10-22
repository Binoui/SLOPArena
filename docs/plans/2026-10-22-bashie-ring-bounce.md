# Bashie Ring Bounce integration

The owner accepted the actual-rig motion on 2026-10-22 ("it's probably a bit too fast but it's fine ... put it in game"). Keep the accepted 60 Hz clock: throw 4, plant 7, squash 10, launch 12, rising handoff 18. The optional cannonball is deferred.

## Movement implementation decision

The existing `setVelocity` operation writes a constant world vector. The temporary Manki jetpack samples movement input but keeps a fixed vertical speed, adds an explosion, and locks actions until the apex. Neither expresses this recovery's live launch aiming and height/reach tradeoff.

1. **Recommended: an engine-owned public `slop.ability.aimed-recovery.v1` capability.** A fixed timeline starts one bounded movement lifecycle at tick zero. It arrests falling, gives a small automatic upward setup, and samples the current movement input on the authored launch tick. Neutral gives maximum upward speed; input magnitude trades some upward speed for horizontal speed. The outer timeline ends at tick 18 and normal air movement/actions resume. It uses existing interruption, cooldown, presentation-event and rollback machinery. No character/slot dispatch or Unity gameplay authority.
2. Extend `setVelocity` with input-relative vector interpolation and add preparation operations to the timeline. This can express the launch, but broadens a general operation's schema and runtime interpretation for a short stateful movement sequence. It also needs an explicit grounded-to-air transition. Prefer the existing capability seam.

**Approved:** the owner selected "Use the Shared recovery capability (Recommended)" on 2026-10-22. Implement option 1 within the existing cooked capability seam. Runtime API 1.9.0 admits its new contract; existing content keeps its prior minimum.

## Proposed first playable values

These are initial tuning values, not additional owner-selected numbers. Retain the existing 210-tick E cooldown, shared by ground/air; do not add a new air-use rule. The automatic setup consumes/refills no ordinary jump. No hitbox, invulnerability, explosion or cannonball.

- One 18-tick timeline, launch at 12. No held-button charging or target acquisition.
- Preparation: zero horizontal momentum, cancel FastFall/JumpArc, rise gently at 0.8 m/s with gravity suppressed (about 0.16 m of automatic lift).
- Launch: neutral vertical speed 15 m/s; full directional input 13 m/s up and 8 m/s horizontally. Analog input interpolates continuously, diagonal input is clamped to unit length. Direction remains adjustable through compression and is sampled once at launch.
- After launch, ordinary gravity applies; the six-tick spring follow-through then hands back to normal air movement/actions at 18. The recovery's ordinary FloatWindow reset remains simulation-owned; launch enters the normal ballistic ascent.
- Damage/KO/cancel before launch prevents any delayed bounce. Hitstop pauses the clock. Ground and air slots share the same mechanic and accepted body clip.

## Authority and presentation

Input adapter's world-plane movement vector → Bashie canonical ground/air E package timeline → cooked typed capability parameters → Shared simulation velocity/grounded state → existing state replication and semantic presentation event → Animancer body clip plus independent package-owned ring prop. The ring follows the authored throw/compression/rebound path on the attack clock; it has no collider or gameplay ownership. It disappears after the rebound or interruption. The body rig, Avatar and unrelated Bashie moves remain intact.

## Verification

Cover neutral/directional/analog late aiming, launch timing, fall arrest, spent jumps, below-stage return, interruption, ordinary post-launch actionability, cooldown, and snapshot/replay through the actual simulation. Validate compiler/loader contract and runtime API admission. Promote accepted assets, inspect/cook/verify Bashie and refresh its roster pin. Capture the real Shared move in Unity, inspect feet/ring contact and handoff, then leave concise Training checks in `TESTING-UNITY.md`.

## Integration result

Implemented and admitted on 2026-10-22 with the clock and initial values above. The new public contract is compiled, source-serialized, cooked-serialized, validated at both trust boundaries, dispatched and snapshotted through the existing capability path. E no longer references the temporary jetpack. Ground/air E share the accepted clip; separate attack-clock ring cues handle preparation and world-space rebound.

Validation: 22 focused recovery/schema/loader tests; full Shared suite 3,246 passed / 2 existing skips; Shared and Server builds; zero native Unity compile errors; Bashie inspect/cook/verify and roster refresh; authoritative ground/air Lab runs; and real ground/air Play Mode captures on Splash Deck with no console errors. The below-stage Shared test clears a solid wall and lands on its top from 2 m below / 3 m out with no jumps or dodge left. No multiplayer session was run. Controller feel and further routes remain human Training checks.

The full-suite run also required adding Bashie to the snapshot contract's roster coverage and clearing Bonk's newer, unrelated returning-lid effect IDs from the existing targeted-leap presentation fixture. No Bonk gameplay/content was changed by these fixture repairs. Detailed local evidence: `artifacts/bashie-e-20261022/integration/`, plus `tmp/bashie-e-integrated-20261022/` for Play Mode frames.
