# Replay file contract

[ADR-0060](../adr/0060-gamehost-replays-world-frames-and-view-track.md) owns the decision;
[the Replay plan](../plans/2026-11-01-replay-design.md) owns rollout. Step 1 implements
Shared capture, synchronous storage and playback. Shared creates no threads or queues.

## Membership and ownership

`ReplayCapture` belongs to one simulation on the Match thread. Its header StartTick is the
first Playing snapshot tick. Before Playing it returns Ignored; a repeated last tick also
returns Ignored. Ended, a gap, an out-of-order tick or an incorrect first tick returns Sealed
with the last accepted tick and detail. After sealing all frames are Ignored. Accepted carries
an owned packet and View record. Capture copies snapshot bytes and collection contents; placement
records and strings are immutable. Capture projects the immutable View without encoding it; it never compresses, serializes World Frames or performs IO.
The writer encodes and bounds-checks View records in Append on the worker.
An encoding or bound failure latches Stopped and retains only the last committed chunk.
The step 2 Server worker must drain that accepted prefix and call Complete synchronously.
It owns queueing, cancellation, IO errors and disposal; recording failure never changes a Match.

`ReplayWriter` borrows a writable stream at position zero; it needs no seeking. Append checks
header binding and continuity. Complete writes a conclusion, returns its committed last tick,
and is idempotent for the same conclusion; conflicting completions are misuse. Dispose abandons
pending ticks and never invents a result. Header roster collections are copied at operational
boundaries. `ReplayReader` borrows a readable seekable stream and owns a small chunk index and
at most two cached lanes. Each object has one caller at a time.

## GameHost recording

`MatchInstance.BuildWorldFrame` captures the packet before peer serialization,
including ticks without connected peers. The first Playing (GO) frame starts the
recording; the Ended frame seals it without joining the recorded range. A host-owned
`MatchReplayStore` runs one file-owning worker per recording with a bounded 600-tick
queue. The Match thread only projects and tries to enqueue; it never waits for IO,
compression, retention, or worker completion. Backlog seals `limit/backlog` and drains
the queued prefix. Stop/host cancellation/loop failure seal Cancelled with the stop
reason. Finished results project the same `MatchResultPacket` used for peers.

The worker completes, flushes, closes and renames `.slopreplay.part` to `.slopreplay`.
Writer/IO failures leave a readable committed prefix and diagnostics rather than
ending the Match. Shutdown bounds writer draining; reporting results to Master
never waits for it. See [VPS Replay configuration and installation](../../deploy/vps/README.md#durable-gamehost-replays)
for environment-only enablement, retention defaults and the durable named volume.

## Container v1 bytes

All integers are little-endian, floats finite IEEE754 binary32. Strings use strict UTF-8.
CRC32 is IEEE polynomial 0xedb88320 with initial and final xor 0xffffffff. Compression is
DeflateStream DEFLATE, neither ZIP nor gzip. Each lane independently uses raw bytes (encoding 0)
or DEFLATE (1), whichever is smaller. There is no alignment or interning table.

| Preamble offset | Bytes | Value |
|---|---:|---|
| 0 | 8 | ASCII `SLOPREP` followed by zero |
| 8 | 2 | container version 1 |
| 10 | 2 | View version 2 (reader accepts 1 and 2) |
| 12 | 4 | metadata byte length, at most 65,536 |
| 16 | 4 | CRC of preamble with this field zeroed plus metadata |
| 20 | 4 | reserved zero |
| 24 | variable | tagged metadata |

Metadata uses the same tagged-object rules as View. The following table fixes the tag order
within each object (1-based, in the listed order). Numeric widths are explicit in source types;
strings are field bytes, booleans one byte. Missing required identities reject the header.

| Object | Tags 1 upward |
|---|---|
| Header | MatchId string, ArenaId string, Rules object, StartTick u32, Roster list, Build object, ArenaDigest string (default empty), TickRate u16 (required 60) |
| Build | Build label string, Protocol u32, FrameFormat u32, SnapshotFormat u32, ContentHash string |
| Rules | FormatId string, FriendlyFireId string, MaxStocks u8, BallOnly bool |
| Participant | EntityId u64, ParticipantProvider string, ParticipantSubject string, Package object, FormId string, CampId string, ColourId string, DisplayName string (default empty) |
| Package | PackageId string, Version string, SourceHash string, CookedContentHash string, PackageHash string |
| Conclusion | Reason string, Detail string, Result object (zero bytes means absent) |
| Result | WinnerIds list of u64, WinningCampId string, Draw bool, Standings list, TeamReturns u64, TealScore u16, ClayScore u16, TerminalModePhaseId string |
| Standing | EntityId u64, Deaths u8, KOs u8, Score u16, Place u8, Withdrawn bool, CompletedTicks u64 |

Roster is sorted by increasing unique nonzero entity ID with 2–4 entries. Provider and subject
identify participants; Steam subject is the invariant decimal Steam ID, never a fabricated name.
Form strings are base/alternate. Camps use existing wire strings; colour IDs are cosmetic strings.
Build label is diagnostic. ArenaDigest is the SHA-256 of admitted baked bytes when available;
the environment supplies the actual digest rather than reserializing an arena.

| Chunk offset | Bytes | Meaning |
|---|---:|---|
| 0 | 4 | ASCII CHNK |
| 4 | 4 | total bytes including header, index, lanes and commit |
| 8 | 4 | first absolute tick |
| 12 | 2 | tick count 1–60 |
| 14, 15 | 1 each | Exact and View encodings |
| 16, 20 | 4 each | raw and stored Exact lengths |
| 24, 28 | 4 each | raw and stored View lengths |
| 32, 36 | 4 each | stored Exact and View CRCs |
| 40 | 4 | CRC of bytes 0–39 followed by the index |
| 44 | 16 × count | Exact offset/length then View offset/length, all u32 |
| next | variable | stored Exact then View lane |
| final 12 | 12 | ASCII CMIT, first tick u32, total bytes u32 |

Index offsets refer to inflated lanes and exactly partition them. Tick i is first+i.
Exact records are ordinary 16-byte WFRM headers with compressed bit cleared and uncompressed
bodies. The body encoder is shared with network serialization; network bytes and protocol stay
unchanged. View records begin with u32 absolute tick, then a tagged object. A short chunk is
terminal; no later chunk may follow it. There are at most 65,536 chunks.

Trailer bytes are ASCII DONE, u32 payload length (at most 16,384), payload, CRC of
DONE+length+payload, ASCII REND. Payload starts with bool has-ticks and u32 last-tick (zero
when absent), then tagged Conclusion. Reasons are finished/cancelled/limit. Last tick must
match the scanned committed range. A missing trailer means unknown outcome, never a draw.
Appended junk invalidates the conclusion and returns CorruptTail.

Flush/encoding failures latch the writer as stopped: later Append returns Stopped and
Complete returns the same result with Stopped=true, without retrying output or throwing.

The fixed cap is **64,000,000 bytes**, decimal MB. Before writing a chunk the writer reserves
16,400 bytes for the largest trailer. If candidate+current+reserve exceeds the cap, the whole
pending chunk is dropped and the file completes with limit at the preceding committed tick.
Up to 60 buffered ticks may be lost. No partial candidate is deliberately written. IO failure
can interrupt a write; the reader recovers only earlier committed chunks. Filesystem durability
remains the host's responsibility.

Open validates all chunk indexes, both stored lane CRCs and commit footers without decoding
Exact. It stops at the first bad tail and never searches for later magic. A header-only crash
is an empty Partial Replay, as is a truncated first chunk. Tail corruption before any committed
chunk fails admission; a cut inside a trailer payload is TruncatedTail.
PrefixReason distinguishes CleanEofWithoutTrailer, TruncatedTail and CorruptTail.
Records are decoded lazily; invalid inner View data returns InvalidView, never a skipped tick.
Header/record/list lengths are checked before allocation. Exact records are at most 65,552 bytes;
View records 524,288; inflated lanes at most 60 times their record bound. Fields/object: 128;
strings: 4,096 UTF-8 bytes; lists: fighters 4, shapes 4,096, mode objects/events 2,048,
hits 128, deaths 4, cooldowns 14. The closed schema nests at most four objects, below the
limit of eight; no arbitrary recursive values or CLR type names are admitted.

### ReplayParticipant display name

| Tag | Field | Codec | Default |
|---:|---|---|---|
| 8 | DisplayName | Text | `""` |

Missing tag 8 reads as an empty display name.
GameHost copies the optional match-start `displayName` into this field after stripping
Unicode control characters and limiting it to 24 Unicode scalars. It is presentation
metadata; provider and subject remain the participant identity. Older files have no name.
`ReplayLabels.ReplayParticipantLabel(participant, fighterName)` returns the recorded name,
or the fighter name with the last six Steam ID digits. Non-Steam participants without a
name use the fighter name. ReplayReport appends recorded names to participant identities.

## View fields and evolution

Each field is u16 tag, u32 byte length, value. Tags strictly increase; duplicates reject.
Lists are u32 count followed by (u32 object length, object bytes) per element. Vectors are
three f32 values. Scalars have exact widths; booleans are 0/1. Missing fields retain the defaults
below, including nested defaults. Event/mode Kind is structurally required for closed dispatch;
unknown kinds in a supported version reject. Unknown tags are skipped. Semantic strings remain
strings, including unknown action/move names that a viewer can display neutrally.

Tags, meanings and defaults are permanent. Add fields only, increment the file-wide View version,
and preserve every older decoder meaning. Never remove, reuse, rename or reinterpret a tag or
semantic string. Newer unsupported container/View versions return PleaseUpdate. View version zero
was never shipped. View version 2 adds Participant header tag 8; View record fields remain
unchanged. A new field must have a default and conformance coverage for both non-default
roundtrip and absent-tag decoding. The field test compares every DTO property with its explicit
AOT-safe field table exactly once, then exercises every field through the polymorphic read path.
A single table drives bounded encoding, byte size and decoding; runtime code uses no reflection.

Every move reference stores package, form, slot address and recorded move name. Fighter active
moves use MoveFormId; returning moves have their own identity fields. Death last-hit fields resolve
against the matching earlier authoritative hit retained by capture (victim, owner and tick), preserving
its form and airborne activation after swaps or respawns; unavailable references remain empty.
Resolve **(package, form, name) first**, then (package, form, slot). If neither exists, show neutral
pose and the recorded name. Slot IDs such as ground.1 are addresses, not durable keys. A package
alias table may arrive as an additive field. Capture resolves against the roster base definition
with ForForm(captured form), which works from either live form too.

The tables below list every tagged field. Tick is the root's fixed u32 prefix. Numeric IDs are
observed entity/activation/emission identities, never enum ordinals. Source comments refer to
capture-time simulation, frame, content or derived observed presentation state; no tuning is
persisted. All clocks are 60 Hz ticks; yaw/pitch are radians except LocalEulerDegrees.

### ReplayViewTick

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | MatchPhase / `string` | `"unknown"` | frame.MatchState, explicit stable switch |
| 2 | Fighters / `IReadOnlyList<ReplayFighter>` | `Array.Empty<ReplayFighter>()` | frame tick + live Shared projection |
| 3 | Shapes / `IReadOnlyList<ReplayShape>` | `Array.Empty<ReplayShape>()` | frame tick + live Shared projection |
| 4 | ModeObjects / `IReadOnlyList<ReplayModeObject>` | `Array.Empty<ReplayModeObject>()` | frame tick + live Shared projection |
| 5 | Events / `IReadOnlyList<ReplayEvent>` | `Array.Empty<ReplayEvent>()` | frame tick + live Shared projection |
| 6 | Hits / `IReadOnlyList<ReplayHit>` | `Array.Empty<ReplayHit>()` | frame tick + live Shared projection |
| 7 | Deaths / `IReadOnlyList<ReplayDeath>` | `Array.Empty<ReplayDeath>()` | frame tick + live Shared projection |

### ReplayFighter

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | EntityId / `ulong` | `0` | s.EntityId |
| 2 | ActionId / `string` | `"unknown"` | explicit ActionState->stable ID |
| 3 | Position / `ReplayVector` | `default` | s.PX/PY/PZ |
| 4 | Velocity / `ReplayVector` | `default` | s.VX/VY/VZ |
| 5 | KnockbackVelocity / `ReplayVector` | `default` | s.KVX/KVY/KVZ |
| 6 | FacingYaw / `float` | `0` | s.FacingYaw, radians |
| 7 | Grounded / `bool` | `false` | s.IsGrounded |
| 8 | ActionTicks / `ushort` | `0` | s.StateTicks (remaining) |
| 9 | DamagePercent / `ushort` | `0` | s.DamagePercent, observed value |
| 10 | Deaths / `byte` | `0` | s.Deaths |
| 11 | KOs / `byte` | `0` | sim.GetKOs(id) |
| 12 | JumpsLeft / `byte` | `0` | s.JumpsLeft |
| 13 | WallJumpUsed / `bool` | `false` | s.WallJumpUsed, transition cues |
| 14 | InvincibilityTicks / `ushort` | `0` | s.InvincibilityTicks |
| 15 | LandingLagTicks / `ushort` | `0` | s.LandingLagTicks |
| 16 | HitstunTicks / `ushort` | `0` | s.HitstunTicks |
| 17 | HitstopTicks / `ushort` | `0` | s.HitstopTicks |
| 18 | HitstunId / `string` | `"none"` | no reaction -> none; reacting level0=small,1=medium,2=hard |
| 19 | HitReactionSequence / `ushort` | `0` | s.HitReactionSequence |
| 20 | WasAirborneDuringKnockback / `bool` | `false` | s.WasAirborneDuringKnockback |
| 21 | PostHitstunFlight / `bool` | `false` | s.InPostHitstunFlight |
| 22 | FormId / `string` | `"base"` | s.Form.Active -> base/alternate; no cooldown banks |
| 23 | SlotId / `string` | `""` | GetActiveMove(id)?.Slot.Id; slot address, paired with package/form/name |
| 24 | ActivationId / `ulong` | `0` | GetLastActivationId, only when active move exists |
| 25 | AttackSequence / `byte` | `0` | event identity, not an activation counter |
| 26 | ActivationAirborne / `bool` | `false` | ActiveMoveView.AirborneAtStart |
| 27 | MoveFormId / `string` | `"base"` | ActiveMoveView.Slot.Form, may differ from live form |
| 28 | MoveElapsedTicks / `ushort` | `0` | s.AttackElapsedTicks |
| 29 | PoseAnimationId / `string` | `""` | ActiveMoveView.Pose.AnimationId |
| 30 | PoseElapsedTicks / `float` | `0` | Pose.ElapsedTicks, f32 may be fractional |
| 31 | PoseDurationTicks / `ushort` | `0` | Pose.DurationTicks, recorded presentation clock |
| 32 | PhaseClip / `bool` | `false` | Pose.IsPhaseClip |
| 33 | PosePitch / `float` | `0` | s.AttackPosePitch |
| 34 | ChargeTicks / `ushort` | `0` | s.ChargeTicks |
| 35 | AimAnimationId / `string` | `""` | active Slot.AimAnimationId; aiming/charge pose |
| 36 | AimMovementId / `string` | `"fixed"` | active Slot.AimMovement explicit switch |
| 37 | ReturningSlotId / `string` | `""` | owner's live returning h, h.Form, h.ActivationAirborne |
| 38 | FlightActive / `bool` | `false` | s.Flight.Active |
| 39 | FlightFuelLeft / `ushort` | `0` | s.Flight.FuelTicksLeft |
| 40 | FlightBurstLeft / `ushort` | `0` | s.Flight.BurstTicksLeft |
| 41 | FlightLiftSpeed / `float` | `0` | s.Flight.LiftSpeed, current state |
| 42 | FlightHeadingYaw / `float` | `0` | s.Flight.HeadingYaw, radians |
| 43 | FlightElapsedTicks / `float` | `0` | current parameter FuelTicks - s.Flight.FuelTicksLeft, not parameter itself |
| 44 | InteractionPhaseId / `string` | `"none"` | s.InteractionPhase explicit switch |
| 45 | InteractionId / `ulong` | `0` | s.InteractionId |
| 46 | InteractionPartnerId / `ulong` | `0` | s.InteractionPartnerId |
| 47 | CapturedYaw / `float` | `0` | s.CapturedYaw * 0.01 degrees -> radians |
| 48 | Inactive / `bool` | `false` | sim.IsEntityInactive(id), not Deaths >= today's stock tuning |
| 49 | FastFalling / `bool` | `false` | s.IsFastFalling; avoid today's FastFallSpeed comparison |
| 50 | PackageId / `string` | `""` | roster participant Package.PackageId |
| 51 | MoveName / `string` | `""` | sim.GetActiveMove(id)?.Slot.Name |
| 52 | ReturningPackageId / `string` | `""` | returning hitbox owner roster package; empty without a returning shape |
| 53 | ReturningFormId / `string` | `"base"` | returning hitbox Form via ReplaySemanticIds.Form |
| 54 | ReturningMoveName / `string` | `""` | roster base.ForForm(returning hitbox Form), returning slot Name |
| 55 | DashDirection / `ReplayVector` | `default` | s.DashDirX/Y/Z |
| 56 | AimYaw / `float` | `0` | s.AimYaw |
| 57 | AimPitch / `float` | `0` | s.AimPitch |
| 58 | LockOn / `bool` | `false` | s.LockOn |
| 59 | HomingTargetId / `ulong` | `0` | s.HomingDashTargetId |
| 60 | FlightFuelFraction / `float` | `0` | s.Flight.FuelTicksLeft / observed current form fuel duration; zero when duration is zero |
| 61 | LockTargetId / `ulong` | `0` | s.TargetEntityId |
| 62 | ChargeTier / `int` | `-1` | active charged-projectile parameters GetChargeTier(s.ChargeTicks); otherwise -1 |
| 63 | ChargeProgress / `float` | `0` | s.ChargeTicks / active charge duration, clamped to 1; zero without charge parameters |
| 64 | ChargeStepsReached / `int` | `0` | observed charge tier + 1, or stream charge progress rounded up to thirds |
| 65 | HomingCooldownLeft / `ushort` | `0` | s.HomingDashCooldownTicks |
| 66 | HomingAirUsed / `bool` | `false` | s.HomingDashAirUsed |
| 67 | Cooldowns / `IReadOnlyList<ReplayCooldown>` | `Array.Empty<ReplayCooldown>()` | current form slots with s.GetCooldown(slot) > 0; retained observed run durations |

### ReplayShape

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | EmissionId / `ulong` | `0` | h.EmissionId |
| 2 | OwnerId / `ulong` | `0` | h.OwnerId; may be Mayhem Host Entity |
| 3 | ActivationId / `ulong` | `0` | h.ActivationId |
| 4 | Kind / `string` | `"unknown"` | sphere/capsule/ring, explicit h.Shape switch |
| 5 | SourceKind / `string` | `"unknown"` | move/host-hazard/owner-window/defense |
| 6 | Start / `ReplayVector` | `default` | h.X/Y/Z |
| 7 | End / `ReplayVector` | `default` | h.EndX/Y/Z |
| 8 | Velocity / `ReplayVector` | `default` | h.VX/VY/VZ |
| 9 | Radius / `float` | `0` | active resolver Hitbox |
| 10 | PreviousRadius / `float` | `0` | active resolver Hitbox |
| 11 | RingHalfWidth / `float` | `0` | active resolver Hitbox |
| 12 | RingHalfHeight / `float` | `0` | active resolver Hitbox |
| 13 | SlotId / `string` | `""` | roster base.ForForm(h.Form) + h.AttackSlot + h.ActivationAirborne |
| 14 | FormId / `string` | `"base"` | h.Form |
| 15 | ActivationAirborne / `bool` | `false` | active resolver Hitbox |
| 16 | VisualOperationIndex / `int` | `-1` | h.VisualOperationIndex; content ordinal retained for identity only, never a lookup key |
| 17 | AttackSequence / `byte` | `0` | active resolver Hitbox |
| 18 | AgeTicks / `ushort` | `0` | h.AgeTicks |
| 19 | AttachmentBoneId / `string` | `""` | h.SourceEvent.BoneName, empty for world projectile |
| 20 | AttachedEntityId / `ulong` | `0` | h.AttachedEntityId |
| 21 | FuseTicksLeft / `ushort` | `0` | active resolver Hitbox |
| 22 | MinePhaseId / `string` | `"none"` | explicit switch h.MinePhase |
| 23 | MineTicksLeft / `ushort` | `0` | active resolver Hitbox |
| 24 | ReturnPhaseId / `string` | `"none"` | h.Return null ? none : h.ReturnLeg ? back : out |
| 25 | TracksOwner / `bool` | `false` | h.TracksBone |
| 26 | VisualKindId / `string` | `"unknown"` | derived observed h flags: melee/projectile/contact-bomb/perching/host-hazard |
| 27 | FuseProgress / `float` | `0` | observed fraction elapsed, 1-FuseLeft/max(1,StickyFuseTicks) |
| 28 | PackageId / `string` | `""` | active resolver Hitbox |
| 29 | MoveName / `string` | `""` | roster base.ForForm(h.Form), h.AttackSlot + h.ActivationAirborne, cooked Name |
| 30 | PresentationId / `string` | `""` | matching cooked emit-presentation operation ID; empty if no authored presentation is associated |

### ReplayPlacement

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | AttachmentId / `string` | `"world"` | explicit placement.AttachmentMode switch |
| 2 | BoneId / `string` | `""` | TimelinePresentationEvent.Placement |
| 3 | LocalPosition / `ReplayVector` | `default` | TimelinePresentationEvent.Placement |
| 4 | LocalEulerDegrees / `ReplayVector` | `default` | TimelinePresentationEvent.Placement |
| 5 | LocalScale / `ReplayVector` | `new(1, 1, 1)` | TimelinePresentationEvent.Placement |
| 6 | DurationTicks / `ushort` | `28` | TimelinePresentationEvent.Placement |

### ReplayEvent

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / `string` | `"unknown"` | current-tick frame mode/presentation event |

### ReplayPresentationEvent

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `presentation` | closed variant identity |
| 2 | EntityId / `ulong` | `0` | current-tick frame mode/presentation event |
| 3 | AttackSequence / `byte` | `0` | current-tick frame mode/presentation event |
| 4 | SourceId / `string` | `"timeline"` | explicit PresentationEventSource switch |
| 5 | OperationIndex / `int` | `0` | current-tick frame mode/presentation event |
| 6 | PresentationId / `string` | `""` | current-tick frame mode/presentation event |
| 7 | WorldPosition / `ReplayVector` | `default` | current-tick frame mode/presentation event |
| 8 | WorldYaw / `float` | `0` | current-tick frame mode/presentation event |
| 9 | Placement / `ReplayPlacement` | `new()` | current-tick frame mode/presentation event |

### ReplaySlopballEvent

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `slopball` | closed variant identity |
| 2 | EventId / `string` | `"unknown"` | frame.SlopballEvents[].Event.Kind explicit switch |
| 3 | BallId / `ulong` | `0` | current-tick frame mode/presentation event |
| 4 | Returns / `ulong` | `0` | current-tick frame mode/presentation event |
| 5 | ContactTime / `float` | `0` | current-tick frame mode/presentation event |
| 6 | CampId / `string` | `""` | current-tick frame mode/presentation event |

### ReplayCourseEvent

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `course` | closed variant identity |
| 2 | EventId / `string` | `"unknown"` | frame.CourseEvents[].Event.Kind explicit switch |
| 3 | EntityId / `ulong` | `0` | current-tick frame mode/presentation event |
| 4 | ObjectId / `string` | `""` | Index->arena course target/checkpoint.Id; -1 -> empty |
| 5 | ElapsedTicks / `ulong` | `0` | current-tick frame mode/presentation event |
| 6 | Sequence / `ushort` | `0` | current-tick frame mode/presentation event |

### ReplayMayhemEvent

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `mayhem` | closed variant identity |
| 2 | EventId / `string` | `"unknown"` | frame.MayhemEvents[].Kind explicit switch |
| 3 | EntityId / `ulong` | `0` | current-tick frame mode/presentation event |
| 4 | RoundIndex / `ushort` | `0` | round number is a counter, not Card identity |
| 5 | OperationIndex / `int` | `0` | current-tick frame mode/presentation event |
| 6 | Points / `ushort` | `0` | awarded result |

### ReplayHit

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Occurrence / `ushort` | `0` | ordinal among this tick's accepted contacts |
| 2 | OwnerId / `ulong` | `0` | frame.Hits owner |
| 3 | TargetId / `ulong` | `0` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 4 | ActivationId / `ulong` | `0` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 5 | AttackSequence / `byte` | `0` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 6 | SlotId / `string` | `""` | base def + hit.Form, hit.AttackSlot, hit.Airborne |
| 7 | FormId / `string` | `"base"` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 8 | ActivationAirborne / `bool` | `false` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 9 | Position / `ReplayVector` | `default` | HitX/Y/Z |
| 10 | ContactTime / `float` | `0` | [0,1] |
| 11 | AppliedDamage / `float` | `0` | hit.Damage, observed accepted outcome |
| 12 | ImpactForce / `float` | `0` | resolved result, not tuning |
| 13 | LaunchDirection / `ReplayVector` | `default` | recorded DirX/DirZ + resolved angle -> unit vector |
| 14 | HitstopTicks / `ushort` | `0` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 15 | Blocked / `bool` | `false` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 16 | ArmorProtected / `bool` | `false` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 17 | FreezesOwner / `bool` | `false` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 18 | PresentationId / `string` | `""` | optional decoration from matching sim.LastTickHits |
| 19 | SourceKind / `string` | `"unknown"` | move/host-hazard/owner-window/defense |
| 20 | Unblockable / `bool` | `false` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 21 | PackageId / `string` | `""` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 22 | MoveName / `string` | `""` | sim.LastTickHits authoritative contact, including HitPresentationId |
| 23 | ReturnPhaseId / `string` | `"none"` | sim.LastTickHits authoritative contact, including HitPresentationId |

### ReplayDeath

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | EntityId / `ulong` | `0` | sim.LastTickDeaths pre-respawn record |
| 2 | Position / `ReplayVector` | `default` | death.State.PX/PY/PZ |
| 3 | Velocity / `ReplayVector` | `default` | death.State.VX/VY/VZ |
| 4 | KnockbackVelocity / `ReplayVector` | `default` | death.State.KV* |
| 5 | BoundaryId / `string` | `""` | bottom/top/minX/maxX/minZ/maxZ pinned v1 strings |
| 6 | KillerId / `ulong` | `0` | sim.LastTickDeaths pre-respawn record |
| 7 | LastHitOwnerId / `ulong` | `0` | sim.LastTickDeaths pre-respawn record |
| 8 | LastHitTick / `uint` | `0` | sim.LastTickDeaths pre-respawn record |
| 9 | LastHitSlotId / `string` | `""` | matching earlier authoritative hit Form/Airborne + death LastHitSlot -> cooked slot Id |
| 10 | DamagePercent / `ushort` | `0` | death.State.DamagePercent |
| 11 | LastHitPackageId / `string` | `""` | death LastHitEntityId -> roster package |
| 12 | LastHitFormId / `string` | `""` | matching earlier authoritative hit Form -> semantic form ID |
| 13 | LastHitMoveName / `string` | `""` | matching earlier hit form/airborne + death LastHitSlot -> cooked Name |

### ReplayModeObject

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / `string` | `"unknown"` | current-tick frame mode/presentation event |

### ReplayBall

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `ball` | closed variant identity |
| 2 | EntityId / `ulong` | `0` | sim.ArcadeBall |
| 3 | Position / `ReplayVector` | `default` | sim.ArcadeBall |
| 4 | Velocity / `ReplayVector` | `default` | sim.ArcadeBall |
| 5 | Radius / `float` | `0` | sim.ArcadeBall |
| 6 | DamagePercent / `float` | `0` | sim.ArcadeBall |
| 7 | HitstopTicks / `ushort` | `0` | sim.ArcadeBall |
| 8 | Active / `bool` | `false` | sim.ArcadeBall |
| 9 | Curve / `float` | `0` | sim.ArcadeBall |

### ReplaySlopballRun

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `slopball-run` | closed variant identity |
| 2 | PhaseId / `string` | `"unknown"` | sim.Slopball |
| 3 | Returns / `ulong` | `0` | sim.Slopball |
| 4 | BestStreak / `ulong` | `0` | sim.Slopball |
| 5 | TotalReturns / `ulong` | `0` | sim.Slopball |
| 6 | ServeInTicks / `ushort` | `0` | sim.Slopball |
| 7 | CountdownTicks / `ushort` | `0` | sim.Slopball |
| 8 | Received / `bool` | `false` | sim.Slopball |
| 9 | ReturnArmed / `bool` | `false` | sim.Slopball |
| 10 | ClearedNet / `bool` | `false` | sim.Slopball |
| 11 | ShotId / `string` | `"unknown"` | sim.Slopball |
| 12 | Misses / `byte` | `0` | sim.Slopball |

### ReplaySlopballVersus

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `slopball-versus` | closed variant identity |
| 2 | PhaseId / `string` | `"unknown"` | sim.SlopballVersus |
| 3 | TealScore / `ushort` | `0` | sim.SlopballVersus |
| 4 | ClayScore / `ushort` | `0` | sim.SlopballVersus |
| 5 | ServingCampId / `string` | `""` | sim.SlopballVersus |
| 6 | ServerEntityId / `ulong` | `0` | sim.SlopballVersus |
| 7 | ServeInTicks / `ushort` | `0` | sim.SlopballVersus |
| 8 | WinningCampId / `string` | `""` | sim.SlopballVersus |

### ReplayCourse

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `course` | closed variant identity |
| 2 | FormatId / `string` | `"unknown"` | race/coop/scramble |
| 3 | ElapsedTicks / `ulong` | `0` | sim.OnlineCourse + sim.Scramble |
| 4 | FinishWindowTicksLeft / `ushort` | `0` | sim.OnlineCourse + sim.Scramble |
| 5 | Ended / `bool` | `false` | sim.OnlineCourse + sim.Scramble |
| 6 | WinnerId / `ulong` | `0` | sim.OnlineCourse + sim.Scramble |
| 7 | LeaderId / `ulong` | `0` | Scramble.LeaderEntityId |
| 8 | SafePosition / `ReplayVector` | `default` | Scramble.SafeGround.Position |
| 9 | SafeYaw / `float` | `0` | sim.OnlineCourse + sim.Scramble |
| 10 | SafeProgress / `float` | `0` | sim.OnlineCourse + sim.Scramble |

### ReplayCoursePlayer

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `course-player` | closed variant identity |
| 2 | EntityId / `ulong` | `0` | sim.CoursePlayers |
| 3 | PhaseId / `string` | `"unknown"` | p.Course.Phase |
| 4 | ElapsedTicks / `ulong` | `0` | sim.CoursePlayers |
| 5 | BrokenTargets / `int` | `0` | sim.CoursePlayers |
| 6 | TotalTargets / `int` | `0` | sim.CoursePlayers |
| 7 | SavedCheckpointId / `string` | `""` | arena checkpoint ID; index -1 -> empty |
| 8 | CountdownTicks / `ushort` | `0` | sim.CoursePlayers |
| 9 | Place / `byte` | `0` | sim.CoursePlayers |
| 10 | Withdrawn / `bool` | `false` | sim.CoursePlayers |
| 11 | Points / `ushort` | `0` | sim.CoursePlayers |
| 12 | LeashTicks / `ushort` | `0` | sim.CoursePlayers |
| 13 | Progress / `float` | `0` | sim.CoursePlayers |

### ReplayCourseTarget

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `course-target` | closed variant identity |
| 2 | EntityId / `ulong` | `0` | sim.CourseTargetEntityId(owner,index) |
| 3 | PerspectiveEntityId / `ulong` | `0` | owner; private race sets differ |
| 4 | TargetId / `string` | `""` | arena.Course.Targets[index].Id |
| 5 | Position / `ReplayVector` | `default` | target.Position |
| 6 | Radius / `float` | `0` | current displayed geometry, not a tuning formula |
| 7 | Broken / `bool` | `false` | sim.IsCourseTargetBroken(owner,index) |
| 8 | HitsLeft / `int` | `0` | sim.CourseTargetHitsLeft(owner,index) |
| 9 | BreakerId / `ulong` | `0` | sim.CourseTargetBreaker(index), scramble only |

### ReplayMayhem

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `mayhem` | closed variant identity |
| 2 | RoundIndex / `ushort` | `0` | sim.MankiMayhem |
| 3 | CardId / `string` | `"unknown"` | scatter/jump/break-out, explicit switch |
| 4 | PhaseId / `string` | `"unknown"` | sim.MankiMayhem |
| 5 | PhaseTick / `ushort` | `0` | sim.MankiMayhem |
| 6 | HazardsLaunched / `int` | `0` | sim.MankiMayhem |

### ReplayMayhemPlayer

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `mayhem-player` | closed variant identity |
| 2 | EntityId / `ulong` | `0` | sim.MayhemPlayers |
| 3 | Score / `ushort` | `0` | sim.MayhemPlayers |
| 4 | RoundsSurvived / `ushort` | `0` | sim.MayhemPlayers |
| 5 | Bonuses / `ushort` | `0` | sim.MayhemPlayers |
| 6 | Benched / `bool` | `false` | sim.MayhemPlayers |
| 7 | Withdrawn / `bool` | `false` | sim.MayhemPlayers |

### ReplayMayhemRing

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `mayhem-ring` | closed variant identity |
| 2 | Ordinal / `int` | `0` | sim.MayhemLandingRings |
| 3 | Position / `ReplayVector` | `default` | sim.MayhemLandingRings |
| 4 | LaunchTick / `uint` | `0` | sim.MayhemLandingRings |
| 5 | LandingTick / `uint` | `0` | sim.MayhemLandingRings |
| 6 | FuseTicks / `ushort` | `0` | observed telegraph lifetime, not recomputed tuning |

### ReplayMayhemSlam

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `mayhem-slam` | closed variant identity |
| 2 | Ordinal / `int` | `0` | sim.MayhemSlams |
| 3 | Position / `ReplayVector` | `default` | sim.MayhemSlams |
| 4 | Tick / `uint` | `0` | sim.MayhemSlams |

### ReplayMayhemWall

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | Kind / string | `mayhem-wall` | closed variant identity |
| 2 | EntityId / `ulong` | `0` | sim.MayhemWalls |
| 3 | CageEntityId / `ulong` | `0` | sim.MayhemWalls |
| 4 | CageCenter / `ReplayVector` | `default` | sim.MayhemWalls |
| 5 | Center / `ReplayVector` | `default` | sim.MayhemWalls |
| 6 | HalfExtents / `ReplayVector` | `default` | sim.MayhemWalls |
| 7 | Yaw / `float` | `0` | sim.MayhemWalls |
| 8 | HitsLeft / `byte` | `0` | sim.MayhemWalls |
| 9 | PhaseId / `string` | `"unknown"` | sim.MayhemWalls |
| 10 | PhaseTick / `ushort` | `0` | sim.MayhemWalls |

### ReplayCooldown

| Tag | Field/type | Default | Source |
|---:|---|---|---|
| 1 | PackageId / `string` | `""` | roster participant Package.PackageId |
| 2 | FormId / `string` | `"base"` | current visible s.Form.Active via semantic form mapping |
| 3 | SlotId / `string` | `""` | current form cooked slot Id |
| 4 | MoveName / `string` | `""` | current form cooked slot Name |
| 5 | TicksLeft / `ushort` | `0` | s.GetCooldown(slot), only emitted when positive |
| 6 | DurationTicks / `ushort` | `0` | maximum remaining ticks observed at the start or refresh of this cooldown run |

Only running cooldowns are present. Remaining fraction is `TicksLeft / (float)DurationTicks`;
readers use zero for an absent or zero-duration record. Duration is an observation retained for
the current run, including hitstop pauses, rather than a lookup into today's tuning.

## Presenter field audit

Audit completed before freezing v1 against the current Unity sources; the adapter remains step 4.

| Presenter | Reads | Recorded source / decision |
|---|---|---|
| PlayerRenderer | transform, movement, ground state, damage, deaths, jumps, wall jump, hitstun/reaction, hitstop, invincibility, landing lag, form, action/attack clocks, interaction/captured yaw | ReplayFighter tags 1–34, 44–49; slot/animation ordinals replaced by package/form/slot/name and semantic pose clip/clock |
| AirDodgePresentation | dash direction, position, invincibility, action and death transition | DashDirection (including Y), Position, InvincibilityTicks, ActionId, Deaths |
| FighterHitFeel | dash direction, ground/attack state, hit clocks, post-hitstun flight, motion, deaths | fighter observations + semantic pose; ViewHit applied force/direction and contact outcome |
| ChargeTierCues / ChargeTierPresentation | active slot, charge ticks, computed tier and progress | identity + ChargeTicks, ChargeTier, ChargeProgress, ChargeStepsReached; thresholds are not persisted |
| MankiFlightPresentation / flight animation | active/fuel, heading/lift, burst and elapsed clocks | FlightActive, FlightFuelFraction, FlightFuelLeft, FlightBurstLeft, FlightElapsedTicks, FlightHeadingYaw, FlightLiftSpeed |
| HUDManager | form, stocks, damage, KOs, active/returning slot, charge and cooldown display, homing air use | header MaxStocks + deaths/inactive; active and returning identity; observed cooldown list, charge progress/steps and homing state. Input bindings and chat are outside recorded Match state |
| ProjectileVFXManager / trajectory / linger | active shapes, attachment, owner, age, dimensions, velocity, fuse, mine and returning leg, aim/target | ReplayShape + fighter AimYaw/AimPitch/LockOn/LockTargetId/HomingTargetId. Trajectory prediction from tuning is not recorded; actual per-tick geometry is |
| SwordSweepTrail | pose samples, active move/phase, positions and elapsed recorded time | recorded semantic pose and clocks; current art reconstructs blade points. Renderer buffers reset on seek |
| Slopball/mode presenters | ball position/velocity/percent/curve, phase, serve/countdown, scores/returns; course targets/progress; Mayhem walls/telegraphs | all ReplayModeObject variants and current-tick mode events. No RNG or future hazard/shot tuning |
| Timeline dispatcher | presentation ID, entity, source/op/sequence, placement and lifetime | ReplayPresentationEvent and ReplayPlacement; hit records never spawn a second copy of the effect |

Today’s models, clips, materials and effects remain presentation assets. Their lookup must not
retime recorded poses from current move duration. Debug-only hurtbox/tuning previews and local
input prompts are not authoritative Replay observations. View-only playback does not load old content.

## Exact admission and traversal

Exact requires protocol, frame format, snapshot format and full match content hash equality,
then arena name/digest and all roster package identity components. Each mismatch has its own
ReplayFailureCode. Build label does not gate. Admission precedes WFRM/WorldSnapshot decoding.
All seven formats use CreateRule and the simulation constructor; only entity registration follows.
There are no Begin calls, RNG, input priming or Tick calls during playback. CourseScramble uses
course arena admission explicitly because MatchFormats.Admits currently omits it.

CreatePlayer without an environment requests View mode. With an environment it requests strict
Exact and returns a typed failure on mismatch; the caller explicitly chooses fallback. Both modes
emit events and hits from View's current tick only. Exact's historical frame windows are inspection
data. Seek admits the target before Reset, then restores/presents max(first,target−30) through target
as Warmup. Every delivery has TicksBeforeTarget. Warmup suppresses sound/shake/scoring and ages
visuals by recorded ticks. Step presents the next tick once as Live. EOF has no callbacks. A backward
seek starts a new presentation epoch. Decode failure latches until a successful seek; the cursor
never skips a failed tick. Borrowed ExactSimulation is read-only to sinks and must never be Tick'ed.

## Viewer: the display world (step 4)

`ReplayWorld` (Shared) is the viewer's only way into a Replay. `Open(stream, content, choice)` takes
today's `ReplayContent` (catalog, arena loader, stand-in package), builds the cast and picks the track:
Exact when `choice` is Best and the gate passes, else View with the gate's refusal in `ExactRefusal`.
Both tracks restore one never-ticked `ServerSimulation`, the display world, for every presented tick.
Exact uses the World Snapshot restore. View runs `ReplayViewDecoder`, which builds the simulation's own
`PreparedSnapshot` from the View record, and the existing `ApplySnapshot` writes it. Each tick's hits,
deaths, presentation events and mode events come from the View record on both tracks (`ApplyTickOutputs`).
Presenters implement `IReplayPresenter` and read `ReplayFrame.World` as they read a live simulation.

- **Tick guard.** Every Replay shell sets `ReplayDisplay`, and `Tick` throws on it. This is defense in
  depth, not a read-only world: Replay code must never call `SetState`, `ActivateAbility` or
  `TickAbilities`.
- **Exact gate content hash.** The GameHost hashes the whole roster handle map with the Match arena
  (`MultiMatchOrchestrator`). `ReplayExactEnvironment` rebuilds it from today's catalog and arena digest,
  so a build with the same roster and arena reproduces it.
- **Cast.** A recorded package today's build has is `Recorded` (same identity) or `Today` (other
  version). A missing package plays today's stand-in fighter, `StandIn`, whose moves are all Missing.
  A recorded alternate form the fighter no longer has plays its base form.
- **Moves.** (package, form, name) first, preferring the recorded slot address and then the recorded
  air variant among same-named moves; then (package, form, slot address); else Missing. A Missing
  active move becomes Idle with its attack fields cleared, keeping position and facing. `ReplayFrame.Moves`
  carries each fighter's recorded name and match for labels.
- **Pose clock.** The display world returns the recorded `PoseAnimationId`, `PoseElapsedTicks`,
  `PoseDurationTicks` and `PhaseClip` from `GetActiveMove`, never today's clip timing.
- **Round trip.** On a same-build recording, projecting the display world back through `ReplayCapture`
  gives the recorded View on every field of every tick, on both tracks, for two and four fighters
  (`ReplayWorldTests`). `FuseProgress` and `LaunchDirection` are inverted arithmetically and compared
  within 1e-4. Fields lost only when content changed: a Missing move's slot, clip and charge clock.

- **Summaries.** `ReplaySummaries.Read` gives a list row's header, range, conclusion and prefix reason. It
  runs the reader's own validation but hops each chunk's lanes instead of inflating and CRC-checking
  them, so lane corruption shows only when the file is opened. `Forecast` runs the Exact gate on the
  header and today's content and returns the track `Open` would choose with its reason.

### What the View cannot restore (decoder fallbacks)

| Lost observation | Fallback until View v2 |
|---|---|
| Perch phase and spot | A perching shape rests at its recorded position: `Perched` when attached, else `Marking` |
| Capsule end bone | Today's move supplies the end bone of the hitbox event with the same start bone |
| Applied hit stun | A hit stuns when it did damage, froze anyone, or left its target reacting that tick |
| Bonk's `MoveSpot` and challenge target | Not shown |
| Mode state (ball, course, Mayhem) | Decoded into each mode's restore snapshot; what it lacks is in [Online modes](#online-modes) |

### Live seams the viewer uses

Every seam is additive, and its live default is the behaviour before it existed.

- `ISimulationBridge.World` gives presenters the simulation they read (local, predicted, or a Replay's
  display world). Bonk's rings and the aim target read it instead of downcasting the bridge.
- `MatchBase` virtuals: `FocusEntityId` (camera framing and the quick-draw bracket), `FramingMaxStocks`,
  `CampOf`, `MusicArenaName`, `FreezesOnPause`, `DrivesControllerFeedback`, `CameraUsesUnscaledTime`,
  plus `Fighters` and a `SetupHUD(players, maxStocks, focus)` overload. The pause menu writes
  `Time.timeScale` only when it freezes the Match.
- `PresentationOutput.TryPlay()` is the one gate for sound and rumble. A `MatchBase.SilenceOutput()`
  scope makes it refuse, and counts what it skipped. The sites: `CombatSFX`, `UISFX` (click, countdown,
  finish), `PlayerRenderer` (its one-shots, which `AirDodgePresentation` and `HomingDashPresentation`
  use too), `MankiBombVisual`, `ChargeTierCues`, the controller rumble, and the mode presenters
  (`SlopballPresentation`, `CharacterCoursePresentation`, Mayhem's wall hits and show stings). Their
  delayed-sound queues drop entries while `PresentationOutput.Silent`. No camera impulse or screen shake
  exists in runtime code today, so nothing else needs the gate.
- `OnlineSlopball` and `OnlineCharacterCourse` take an optional perspective (whose camp, targets and events
  show) and their format or roster; the live defaults read `MatchConfig.LocalEntityId`, `MatchConfig.Format`
  and `ClientSession.MatchRoster`. Each mode presenter has a `Reset()` for a seek: `OnlineSlopball` (with
  `SlopballPresentation`, `SlopballCourtShow` and `SlopballView`), `OnlineCharacterCourse` and `MankiMayhemPresentation` (with
  `MankiMayhemView` and `MankiHostPresenter`).

### Recorded observations over today's tuning

Each override is optional and null in live play. A new recorded observation belongs in this table.

| Presenter | Override | Recorded source | Live default |
|---|---|---|---|
| `PlayerRenderer.ApplyServerState` | `RecordedFighterClocks.Pose` | `PoseAnimationId`, `PoseElapsedTicks`, `PoseDurationTicks` | today's `clip.At(AttackElapsedTicks)` and clip duration |
| `PlayerRenderer` weapon pose (sword trail sampling in `WeaponAttach`) | the same pose | the same fields | today's clip |
| `PlayerRenderer` flying loop | `FlightElapsedTicks` | `FlightElapsedTicks` | today's fuel capacity minus fuel left |
| `MankiFlightPresentation` fuel gauge | `FlightFuelFraction` | `FlightFuelFraction` | fuel left over today's capacity |
| `ChargeTierCues`, `ChargePropFeedback`, `ChargeScale` | `ChargeTier`, `ChargeProgress` | `ChargeTier`, `ChargeProgress` | `ChargeTiers.Of` and `Progress` on today's move |
| `HUDManager.Recorded` | cooldown run length, charge pips | `ReplayCooldown.DurationTicks`, `ChargeStepsReached` | today's `CooldownTicks`, `ChargeTiers.StepsReached` |
| `CombatFeedback.RecordedOwnPresentation` | whether a hit showed its own presentation | the hit's `PresentationId` and the tick's `hit-contact` events | today's slot `HitPresentationId` |
| `SlopballPresentation.SenderFollowsBall` (View track) | where the ball sender stands | the ball's position on each serve tick | the shot's spawn |
| `MankiMayhemPresentation.FixedTelegraphs` (View track) | landing ring size and countdown fill | each ring's `LaunchTick` and `LandingTick`; a fixed 2.5 m marker | today's blast radius and call plus flight time |
| `MankiMayhemPresentation` walls with hit budget 0 (View track) | wall look, cracks, rise and sink | `HitsLeft` only: one neutral look, upright while it stands | material and cracks from the budget, today's rise and sink time |

### Online modes

`ReplayModes.Create` builds the live mode presenter for the file's format (`OnlineSlopball`,
`OnlineCharacterCourse`, `MankiMayhemPresentation`), with the followed fighter as its perspective. It feeds
it the display world after the fighters, in PvPMatch's order. A new followed fighter re-presents the tick
from their side. On View, `ReplayViewDecoder.Modes` builds each mode's restore snapshot, and the shell runs
the recorded format's own rule (fighters only when today's arena no longer hosts it). Mode events reach the
display world from the View on both tracks. A seek resets every mode presenter before the pools are recalled.

| Mode | Exact track | View track shows | View track falls back to | Both tracks lack |
|---|---|---|---|---|
| **Slopball co-op** | Everything below, with the real shot | Ball (position, velocity, damage, curve, hitstop, live); run phase, returns, streaks, serve and countdown clocks, receive, arm and net flags, misses, next serve kind; served, received, returned, missed, re-serve and stop events | The shot's spawn, velocity and flight are not recorded: the sender goes where the recorded ball is served and waits at its stage spot until the first serve. Seeking recovers the last serve before the warmup window. Court cards, tint and return flashes rebuild from recorded ticks at 60 Hz. Seed, shot index and miss budget are 0, since only ticking reads them | Ball contacts are per-tick scratch: no bounce sounds or squash and no bank, sky or net-cord callouts; a ball's path ends at its hit's contact time |
| **Slopball versus** | Everything below | Scores, serving camp and server, serve clock, winner, phase; serve-ready, served and point events; the panel speaks for the followed fighter's camp | Serve-rotation counters are 0 (they pick the next server when ticking) | The same ball contacts |
| **Course race, co-op, scramble** | Everything below | Course clock, finish window, ended and winner; each player's phase, clock, checkpoint, place, withdrawal, points, leash and progress; each perspective's targets (hits left, broken, breaker); scramble leader and safe ground; go, target, checkpoint, respawn and completion events for the followed fighter | Targets and checkpoints are today's arena matched by recorded ID, never by order. A recorded target the arena no longer has is labelled "not on today's course" where it stood; a target the Replay never recorded keeps all its hits and is labelled "not in this Replay"; events naming a missing ID are left out. Lock-on and homing references map through the recorded target ID for their perspective; an absent lock target is cleared and labelled. Recorded hits left survive a smaller current budget; an incompatible damage model is neutral and labelled with the recorded count and current budget. The first-finish tick, scramble ground history and other racers' safe ground are not recorded | |
| **Manki Mayhem** | Everything below, with real wall budgets and today's ring styling | Round, card, phase and its clock, hazards launched; players' scores, rounds, bonuses, bench and withdrawal; landing rings, slams, walls (place, size, yaw, hits left, phase); call, wind-up, launch, bench, score, end, slam and wall events; host hazards as shapes | Walls have no hit budget: one neutral look while they stand, no cracks, no rise or sink. Landing rings draw at a fixed 2.5 m and fill from the recorded launch to the landing. The director seed is 0 | Host reactions older than the 30-tick warmup are gone after a seek until the next event; the host's throw and slam timing and the slam warning window are today's animation. The show camera frames the deck, as in live play, so orbit is off |

### The viewer in Unity

`ReplayLaunch.Open(path, returnTo, track, tick, focus)` loads the generated `Replay` scene, whose
`ReplayMatch` (a `MatchBase`) opens the file with `ReplayContentSource.TryToday` (the local catalog
Training uses and the baked arenas with their digests). It presents each tick in PvP's present order,
one tick per `FixedUpdate`, and `Time.timeScale` is the speed (0.25×, 0.5×, 1×, 2×). Warmup ticks run
inside `SilenceOutput()`. Pause keeps orbit and zoom (hold the right mouse button, or a controller)
through `CameraUsesUnscaledTime` and the Cinemachine brain's `IgnoreTimeScale`. Paused steps sample
still poses with `PlayScrubbedState`. The bar shows play, steps, speed, a timeline that seeks once per
rendered frame, the followed fighter (Tab, LB/RB), the track with its reason, move names (M) and Leave.
The list, focus bar, fight cards, mode scoreboards and labels over fighters use the recorded display
name. Without one, they show the fighter's name and the last six digits of a Steam ID. Non-Steam
participants without a name show the fighter's name. Fighter labels also name a stand-in and a Missing
move. The end of the file shows the conclusion, or the partial reason.

`Scenes/Replay.unity` is generated from `Arena_Offline` by `sloparena.replays.build-scene`; rebuild it
after changing that scene. Editor commands, all through the gateway:

| Command | What it does |
|---|---|
| `sloparena.replays.record-bots --output <path> [--fighters fightguy,nyx:alternate] [--ticks 1800] [--match-format stock\|slopball-coop\|slopball-versus\|course-race\|course-coop\|course-scramble\|manki-mayhem]` | Records a same-build CPU Match with this Editor's content, so it plays Exact; each format takes its usual arena, starts and Begin call |
| `sloparena.replays.open --file <path> [--tick N] [--focus id] [--track best\|view]` | Opens a Replay in Play Mode at a tick |
| `sloparena.replays.check --script play` | Every tick at 2×, ten step-backs and step-forwards, every fighter focused, Nyx crows drawn, no console errors |
| `sloparena.replays.check --script pose` | A blade move's weapon tip follows the recorded pose clock, not today's clip time |
| `sloparena.replays.check --script orbit` | The paused camera still orbits and zooms |

Commands time out after 30 s, so check files of up to about 2,000 ticks with `play`.

### Seek hygiene

A seek must show the same thing whatever was on screen before it. `ReplayMatch.Reset` lets owners reset
themselves first: each renderer (with its flight exhaust), its blade trail (`WeaponAttach.ResetForSeek`), the
timeline, Bonk, tether and sword owners, the projectile visuals and the mode presenters. `ReplayEffects.Sweep` then recalls the pools
(`GraphicHitEffect.RecallAll`, `MovementFeedbackEffect.RecallOneShots`), stops the scene's playing
sounds, and removes roots spawned since the Replay began, except UI, pools and the camera's framing anchor
(`CameraMount.FramingAnchor`, made on the camera's first switch to an automatic style). It logs each removal:
every logged name is an effect whose owner does not reset itself yet. The pool recall includes trails and
auras whose owner has ended them, since they fade on game time and a paused seek would keep them. The
timeline dispatcher, the projectile manager, the course presentation, Manki's flight exhaust and the sword
sweep trails deactivate what they destroy, because `Destroy` waits for the frame's end and a seek
re-presents within the frame.

Warmup ticks run the ordinary present code inside `SilenceOutput()`. What a warmup tick spawns starts at
its distance from the target: particle systems are simulated to that age with a seed taken from their
place in the scene, animators are updated, trails cleared, and hit effects aged. Script timers that read
`Time.time` (hit feel, tether retract, queued mode sounds) do not age; they show at their start age.

`sloparena.replays.check --script seek-hygiene` takes 20 seeded (A, B) pairs. For each, a seek to B
straight after the first tick must equal a seek to A, 60 live ticks, then a seek to B. It compares a
census of transient roots, alive particle systems with ages and counts, hit effects, trail lengths,
blade samples, playing sounds and fighter bone positions, requires no sound during either seek, and
the same sounds on the next live tick. Measured in the Editor on 2026-11-02 (60 seeks each):

| Replay | Track | Seek median | Seek p95 |
|---|---|---|---|
| FightGuy and Nyx, 900 ticks | Exact | 9.97 ms | 17.65 ms |
| Released golden, 180 ticks | View | 8.27 ms | 13.50 ms |
| Bonk, Manki, Nyx, Bashie, 900 ticks | View | 15.70 ms | 25.69 ms |

With the mode presenters, on same-build 1,200-tick fixtures (Exact, 2026-11-03):

| Replay | Seek median | Seek p95 |
|---|---|---|
| Slopball co-op, 2 fighters | 15.2 ms | 23.9 ms |
| Slopball versus, 2 fighters | 15.2 ms | 26.0 ms |
| Course race, 2 fighters | 36.3 ms | 90.7 ms |
| Course co-op, 2 fighters | 33.4 ms | 106.8 ms |
| Course scramble, 3 fighters | 56.6 ms | 126.6 ms |
| Manki Mayhem, 4 fighters | 29.7 ms | 56.2 ms |

### Where Replays live, and the list

One folder rule, in `ReplayDirectory.Resolve` and in `tools/replay_pull.py`, both checked against
`tools/tests/replay_directory_vectors.json`:

1. `SLOPARENA_REPLAY_DIR` wins. Under Wine or Proton an absolute Unix value is reached through `Z:`.
2. Linux: `${XDG_DATA_HOME:-~/.local/share}/sloparena/replays`. Wine or Proton: the same host folder
   through `Z:` when Wine passes `XDG_DATA_HOME` or `HOME` through (best effort), else
   `%LOCALAPPDATA%\sloparena\replays`. Windows: `%LOCALAPPDATA%\sloparena\replays`. macOS:
   `~/Library/Application Support/sloparena/replays`.

The pull tool prints the folder it resolved and the Steam launch option that points the Proton build at
the same folder: `SLOPARENA_REPLAY_DIR="$HOME/.local/share/sloparena/replays" %command%`.

Home shows **Replays** only when that folder holds a `.slopreplay` or `.slopreplay.part` file. The list
opens over Home. It shows the folder it read and a Refresh button, and has one row per file, newest
first. Each row gives the mode, arena, participant labels and length, then
"Downloaded" with the file's modification time, Complete, Partial or Empty, and Exact or View with its
reason. A file from a newer build says "Made by a newer build. Update SLOPArena." `ReplayLibrary` reads
summaries on a worker, at most 500 files, cancellable, cached by path, size and modification time; the
forecast runs on the main thread. Watching is refused while a live match runs or an online room is
open. Leaving a Replay started from the list returns to Home with the list open.

`sloparena.replays.directory-selftest` runs the vectors in the Editor. `sloparena.replays.list-check
--directory tmp/<dir> --source <complete file>` writes complete, partial, newer-build and garbage files,
points this Editor process at them, and checks the four rows.

## Goldens and release policy

Every release that records Replays adds a short immutable `.slopreplay` under
`tests/Shared.Tests/Golden/Replays/`. Never regenerate or overwrite a released golden, including
when content, field defaults or codecs change. The test opens and plays **every** file through
View with no catalog/arena dependency. Add a distinct release-named file and run the full Shared
suite. Keep every previous file. Exact compatibility is deliberately not required of old goldens.

The first fixture is a 300-tick, two-fighter stock CPU-bot recording, seed 7. Its generator is:

```sh
dotnet run -c Release --project tools/ReplayBudget -p:SkipUnityPluginCopy=true -- --ticks 300 --seed 7 --golden tests/Shared.Tests/Golden/Replays/<release>.slopreplay
```

The bounded generator ends Cancelled and carries no fabricated Match winner. It verifies all
Exact restores before writing. Golden playback tests never invoke the generator.

ReplayBudget uses the real Shared capture/writer and verifies every Exact tick. Run the requested
3600-tick seed-7 command to measure both stored lane payloads in decimal MB/minute; container
framing is reported separately in total. Mode objects can change costs; stock bots do not bound
worst-case file size. The fixed byte cap remains authoritative.


### Step 1 real-schema measurement

The requested 3,600-tick seed-7 run recorded and restored all ticks with the Shared capture
and writer. Decimal MB/minute, compressed lane payloads (framing included only in Total):

| Fighters | Exact | View | Total |
|---:|---:|---:|---:|
| 2 | 0.357993 | 0.360893 | 0.780718 |
| 4 | 0.840964 | 0.751635 | 1.655029 |

**The four-fighter View result exceeds the synthesis target of about 0.6 MB/minute.**
The agreed tagged, float32 format is retained; these real-schema results replace the prototype's
size assumptions for planning. No quantization, positional layout or omitted observations were
introduced to reduce this result. The 64,000,000-byte limit still applies.

The pre-freeze fix reduced codec implementation from 2,624 lines (View codec 1,613,
validation 632, metadata 379) to 558 (View codec 385, shared field codecs 56,
metadata 117). Counts include blank lines and exclude DTO declarations.

## Pull and report

The owner-approved read-only path is GameHost control → Master owner API → console.
GameHost exposes authenticated `GET /match/replays` and `GET /match/replays/<canonical-guid>`.
The list includes up to 2000 newest retained recordings, preferring complete files and excluding
active writers. Download returns 409 while recording, 404 if absent, and 413 above 64,000,000 bytes.

Run with the local console running and its key in
`~/.local/state/sloparena-console/console.key`:

```bash
python3 tools/replay_pull.py
dotnet run --project tools/ReplayReport -p:SkipUnityPluginCopy=true -- \
  "${XDG_DATA_HOME:-$HOME/.local/share}/sloparena/replays" --out report.md --json report.json
```

The pull defaults to `http://127.0.0.1:4173`; `--base-url` also accepts the alfred console URL.
Its default destination is today's UTC date folder under the Replay folder above.
It sends the key privately as `X-Console-Key`, validates the eight magic bytes and advertised size,
and atomically renames downloads in the destination. `--dest <dir>` overrides the default UTC
date directory. Complete and partial filenames track completeness; a completed recording replaces
a previously pulled partial even when their sizes match. Failed transfers preserve existing files.

ReplayReport reads `.slopreplay` and `.slopreplay.part` recursively using View Track only.
`--since yyyy-MM-dd` selects files by UTC modification date (the format has no wall-clock start).
Files counts include failures, with reasons; minutes and statistics include only readable files.
Activations are unique per file, entity and ActivationId, including observed projectile shapes
and contacts. Landed excludes blocked contacts; damage sums recorded applied damage.
KO credit uses the recorded killer, while the move is the recorded last hit, or unknown.
Win rates use participant provider/subject and package, with ordered opponent pairs. Only finished
matches with results enter the denominator; draws count as zero wins. CPU subjects stay labeled CPU;
Steam subjects are IDs, never invented player names. The CPU report regression records a seeded
60-second fight; the checked-in golden remains the cross-build format fixture.
