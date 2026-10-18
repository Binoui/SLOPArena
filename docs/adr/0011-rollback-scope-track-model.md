# ADR-0011: Rollback prediction scope — LocalTrack, PredictedTrack, RawTrack

**Being superseded by [ADR-0039](0039-full-world-rollback.md)** (accepted 2026-10-18): full-world rollback anchored on complete server snapshots. This record still describes the shipped client until the protocol 10 cutover.

**Amends:** ADR-0010 (all-entity prediction via input relay) — corrects its rejection rationale for self-only prediction and narrows its "predict all entities" mechanism.

ADR-0010 committed to one rebuild-and-replay code path for every entity, self included, justified by "opponent hit reactions must feel instant too." Auditing that mechanism against the actual wire protocol and ability architecture (grilling session, 2026-08-03) found it unimplementable as specced: `CharacterStatePacket` serializes ~24 of `CharacterState`'s ~40 fields (drops attack-elapsed ticks, knockback velocity, hitstun ticks, DI, dash/warp state), and per-ability instance fields (e.g. `NilusVoidRift._seedSpawned`, `_cachedAimYaw`) plus `SpellResolver`'s live hitbox/projectile list exist only in server process memory — never on the wire, never in `CharacterState` at all. Rebuilding an entity that's mid-attack, mid-hitstun, or has a live projectile/rift in flight from a wire snapshot cannot reproduce that state; re-sim would diverge from tick zero of the replay window, not just at the frontier.

## Decision

Split rollback into three per-entity tracks instead of one uniform mechanism:

- **LocalTrack** (self, always): a continuously-running local `ServerSimulation`, fed the player's true inputs every tick, never rebuilt from a received snapshot. Corrected only by snapping wire-serialized fields on mismatch. Sidesteps the reconstruction problem entirely — self is never destroyed and rebuilt, so fields absent from the wire never need reconstructing.
- **PredictedTrack** (opponents in a **Predictable ActionState** — `Idle`, `Dashing`, `JumpSquat`, `AirDodging`): rebuilt from `ConfirmedTick`'s snapshot, replayed via `InputRelay`. Requires widening the confirmed-base sync with the movement-resource fields these states depend on but that weren't previously on the wire: `AirTimeTicks`, `DashDurationTicks`, `DashDirX/Z`, `DashCooldownTicks`, `AirDodgesLeft`, `JumpsLeft`, `InvincibilityTicks`, `TurnaroundTicks`, `DirHoldTicks`, `IsSprinting`, `LastDirX/Z`, `WasAirborneDuringKnockback` (~12 fields, ~30B/entity).
- **RawTrack** (opponents in a **Complex ActionState** — `Attacking`, `Hitstun`, `Warping`): no re-simulation — rendered directly from the latest received packet, identical to pre-rollback (Phase 1) behavior, scoped to just this entity for just this window. Switches to PredictedTrack the tick the entity returns to a Predictable state.

## Correction to ADR-0010's rationale

ADR-0010 rejected self-only prediction because "opponent hit reactions would land one round-trip late." That distinction no longer holds: `Hitstun` is a Complex ActionState, so under RawTrack, opponent hit reactions land exactly as late as they would under pure self-only prediction. The actual value PredictedTrack adds over self-only is narrower — opponents' plain movement (walk/dash/jump/airdodge) reads smoothly at RTT instead of choppily. Kept anyway: the wire cost is trivial (~30B/entity; `MatchInstance.SendState()` already sends one UDP datagram per entity, not batched, so this is nowhere near fragmentation limits regardless of entity count), and movement smoothness matters for spacing/reads in a platform fighter independent of hit-confirm feel.

## Consequences

- `ServerAbility`-instance state and `SpellResolver`'s hitbox/projectile list stay fully out of scope — no serializable-ability-state contract, no hitbox-layer snapshotting. ADR-0010's original "full" ambition (predicting opponents' attacks too) is deferred, not abandoned; the Predictable/Complex partition makes the gap legible and reversible — promoting a state to Predictable later just means adding its dependent fields to the wire, no architecture change.
- Golden-tick determinism tests (ADR-0010's D8) now cover two mechanisms instead of one: LocalTrack correction-on-mismatch, and PredictedTrack rebuild-and-replay for the four Predictable states.
- `docs/plans/2026-08-02-rollback-netcode.md` needs updating to match this scope before implementation starts.

**Status:** accepted (grilling session 2026-08-03, implementation pending — see `docs/plans/2026-08-02-rollback-netcode.md`).
