# Solo Arcade implementation plan

**Status:** Approved; ready for implementation handoff — 2026-10-14. The maintainer
confirmed runtime scope, unified stage authoring, Slopball-first delivery and the
pilot course route. Phases 1–3 are implemented with a temporary
Editor domain-reload workaround. The full challenge release and human acceptance
remain future work.

The [feature brief](arcade.md) owns gameplay and UX. The first release contains
Slopball for all four fighters and one FightGuy Character Course. The first
working remote match remains the immediate engineering priority.

## Implementation progress

[Phase 1](arcade-phase-1.md) now supplies embedded arena metadata, Shared ball
physics/contacts and normal aim queries, PvP purpose guards, a dedicated local
runner and an original test court. Shared/Server tests, native compilation,
stage baking/inspection and live contacts for all four fighters passed. Native
reset/pause and ordinary Training smoke checks passed. Normal Play Mode domain
reload still stalls; verification temporarily disabled that reload and restored
the original Editor settings afterward. See the Phase 1 handoff for the evidence
and remaining human checks.

[Phase 2](arcade-phase-2.md) now supplies the Slopball profile, deterministic serves,
receive/redirect/clear/landing ordering, chronological floor misses, fresh/same-shot gaps,
the fighter half constraint, a neutral enclosed court and a local playable runner.
All four native cooked-kit probes produced a valid return; retry and pause passed.
The Phase 2 handoff records that slice's prototype flow.

[Phase 3](arcade-phase-3.md) adds offline main-menu entry, locked countdown admission,
stable results, full retry/selection and persistent personal bests per fighter.
Gameplay fingerprints archive incompatible bests while cosmetic changes preserve
comparison. Native menu/scored-run, pause, retry, voluntary stop, storage and roster
entry checks passed. Its handoff records layout verification and remaining human
checks. Course markers/content remain Phase 4.

## Baseline before Phase 1

[LocalSimulationBridge](../../client/Unity/Assets/Scripts/Runtime/Simulation/LocalSimulationBridge.cs)
already runs production Shared simulation without an online connection. Current
Solo is a CPU stock fight, not Arcade. New challenges can reuse this local core;
the current Training runner assumes multiple fighters and is not itself the
single-player Arcade lifecycle.

The relevant inspected boundaries are:

- [ServerSimulation](../../src/Shared/ServerSimulation.cs): fighter movement,
  target collection, accepted hits, owner side effects and respawn.
- [SpellResolver](../../src/Shared/SpellResolver.cs): contact geometry,
  per-activation hit history and projectile/explosion lifetimes.
- [MatchRule](../../src/Shared/MatchRule.cs): existing elimination/winner checks,
  without challenge objects, checkpoints, clocks or records.
- [ArenaDefinition](../../src/Shared/ArenaDefinition.cs) and
  [ArenaBinaryFormat](../../src/Shared/ArenaBinaryFormat.cs): baked geometry,
  positional versioned serialization and loading.
- [Stage authoring service](../../client/Unity/Assets/Scripts/Editor/SlopArenaStageAuthoringService.cs):
  restricted collision source, validation, baking and exact-byte freshness checks.
- [AimHandler](../../client/Unity/Assets/Scripts/Runtime/Combat/AimHandler.cs):
  normal orbit, cursor/vector and held-aim behavior.

Before Phase 1, combat/aim target collection and accepted-hit dispatch required fighter
state. Reading `LastTickHits` afterward cannot add missing object contacts.
Calling the mutating `SpellResolver.Tick` twice would move, age and consume
projectiles twice. A ball/target must enter the normal Shared contact path.

Existing local state/observation APIs are fighter-oriented; they do not provide
complete nonfighter state, reset, clone or snapshot support. The selected solo
release needs explicit object observations and complete retry behavior. It does
not automatically provide networked object prediction or reconciliation.

## Accepted architecture

| Decision | Selected approach | Alternative considered |
| --- | --- | --- |
| Runtime foundation | Focused ball/target support and Shared Arcade rules | General world-object and game-mode framework |
| Stage contract | Expand the baked stage artifact to include geometry and challenge data together | Separate cooked Arcade definition beside an unchanged arena |
| First playable challenge | Slopball across the four-character roster | FightGuy Course first |

[ADR-0037](../adr/0037-arcade-shared-objects-and-unified-stage-artifact.md) records
the runtime/stage trade-off. [ADR-0036](../adr/0036-arcade-pvp-kit-parity-and-legal-fall-resets.md)
records kit parity and legal fall resets.

Shared owns contacts, ball motion, target progress, constraints, goals, clock and
attempt state at 60 Hz. Unity owns input adapters, rendering, cameras, UI, audio
and VFX. Use the package-native fighter path; objects have separate identity/state
and are never fabricated fighters or character packages.

### Focused Shared changes

1. Add nonfighter contact/target descriptions to existing attack/projectile and
   normal aim queries. Dispatch accepted contacts during the same combat pass.
   Preserve attack provenance, per-activation hit history, ordinary owner timing,
   Hitstop and projectile/explosion behavior through the existing engine primitives.
   Object-specific responses decide ball launch or target destruction; they do not
   create a second ability execution path.
2. Add a Shared Arcade session coordinating normal fighter simulation with ball
   state, target progress, checkpoints, serve scheduling, score, course time and
   attempt phase. Pin the selected baked challenge and fighter gameplay for the run.
3. Implement deterministic moving-ball sweeps against court, net, fighter body and
   active attacks/projectiles. Order contacts within a tick so earlier floor failure
   cannot become a later return. Distinguish floor, wall, ceiling, net and half
   crossing explicitly; contact with the net's top must remain live.
4. Add a Shared fighter-only court-half constraint. Static geometry has no existing
   object-specific mask, so an ordinary invisible baked wall cannot implement a
   boundary that lets balls/projectiles through while confining the fighter.
5. Expose read-only per-tick object/challenge state and semantic events to Unity,
   including score/time, required targets, saved checkpoint and terminal result.
   Extend normal lock selection/indicators beyond fighter renderers while keeping
   current ability aim modes, ranges and current-position assistance.
6. Provide complete attempt restart with fresh simulation/challenge state and
   cleared presentation/input state, retaining selected content and personal bests.
   Clear queued explosions as well as live hitboxes, projectiles, object contacts,
   timers and target progress. Training's partial reset and live-hitbox clearing
   alone are insufficient.

Fresh/re-served balls need distinct collision identities so existing hit history
does not suppress a new ball. Collision IDs are `ulong`, while selected input
targets currently use byte IDs. Define an explicit mapping for local object
selection that handles long attempts, stale locks and identity reuse. Do not
silently widen remote protocol fields to deliver solo Arcade.

Short scheduled durations use Shared ticks. Course clocks and indefinite
Slopball counters must not wrap at the limits intended for short move durations.
A neutral re-serve preserves ordinary fighter/projectile state; a checkpoint fall
uses normal PvP fighter respawn; a full retry resets the entire attempt.

## Unified stage authoring, baking and loading

### Current contract and migration

Before Phase 1, the writer emitted `.arena` v5; the reader accepted v1–v5 and rejected newer
versions. Layout is positional/version-gated, with no existing tagged optional
challenge section. All twelve current arena artifacts are v5.

The original collision source required an identity-transform `Stage_<key>` root
with `GameplayGeometry`, `SpawnPoints` and `AuthoringAids`. Allowed components
are restricted, and authoring universally requires four empty tagged
`Spawn_01`–`Spawn_04` markers. The binary spawn list can vary, but that does not
relax the source validator or Training's existing two-fighter assumption.

Add an explicit new stage-format version and compiled Arcade section containing
its purpose/profile and mode-specific fields. Extend the maintained source
hierarchy/component rules for approved challenge markers and configuration.
Compile marker data into the same normalized stage artifact as collision; raw
authoring JSON and Unity components are not runtime gameplay contracts.

Make validation depend on stage purpose:

- Normal PvP stages retain their existing four-spawn and geometry checks.
- Arcade stages support one clean player start plus mode-specific markers,
  checkpoints and settings. A dedicated local Arcade runner starts one fighter;
  it does not add a dummy opponent to satisfy Training assumptions.
- Arcade-only stages stay outside normal PvP selection and admission. Direct
  offline loading can bypass the menu allowlist, so admission must check purpose.

Continue reading existing v1–v5 data with its normal-stage defaults. New Arcade
stages require the new reader and explicit version handling; old binaries are
not assumed to understand them.

Freshness currently rebuilds, serializes and hashes the exact expected bytes.
Changing the writer can flag every unchanged v5 PvP stage stale. Preserve plain
stage export/freshness behavior or compare serialization versions explicitly,
with regression coverage. A blanket rebake of unrelated PvP stages is not a
prerequisite for this solo feature.

### Compiled challenge fields and validation

Each challenge needs stable identity/profile and a gameplay revision, allowed
fighter identities, clean start position/facing and its mode-specific data. The
single stage artifact bundles these with authoritative static geometry:

| Challenge | Required data |
| --- | --- |
| Character Course | Target identities/positions/shapes and required sets; ordered checkpoint locations and cumulative target prerequisites; finish volume |
| Slopball | Court halves and floor roles; net volume and player boundary; sender; fixed seed/profile; serve-gap ticks; gravity/rebounds/launch tuning; difficulty progression and maximum |

Cosmetic stage/object bindings remain Unity-owned, consistent with the existing
separation between gameplay collision and scene presentation.

Reject duplicates, malformed identities, invalid volumes, unsupported versions,
contradictory checkpoint prerequisites and incomplete profile settings before
play. Validate receive/return boundaries and floor/net distinctions. Authored
serves must enter the player's half unaided in a valid court; invalid trajectories
must not produce an endless neutral re-serve loop. Static/profile checks cannot
replace human validation of readability, reachability and feel.

## Attempt lifecycle, presentation and records

Use a dedicated main-menu Arcade entry and local runner. Local name preferences
may be reused, but entering/starting Arcade must not await Master authentication,
a connection attempt, a lobby or match allocation.

From clean fixed spawn, run a brief locked countdown. At Go, start the Course
clock or launch Slopball serve one. Existing Solo countdown presentation may be
reused, but its current input polling is not a complete locked-start gate.
Clear movement/charge/input edges so pre-Go input cannot create an advantage.

Pause stops simulation, ball motion, clock and kit timers, preserving record
eligibility. Capture terminal result once at the actual finish/miss/stop boundary;
presentation delays cannot change it. Results provide current result, compatible
personal best, improvement and quick Retry / challenge selection.

Use normal orbit and existing ability-aim cameras. Court framing, ball visibility
and wall occlusion need presentation work. Existing aim choices must keep their
camera direction control; no added predictive lead is promised. Floating targets
do not block fighter movement, and contextual mechanic tips are opt-in while
objective/route cues stay visible.

### Persistence and compatibility

Keep local records separately from settings-default resets and online identity.
Bank Slopball's verified count on failure, voluntary stop and retry. Record Course
time only at valid completion. Store compatible bests by challenge/profile and
fighter; preserve older incompatible bests in an archive. Initial rewards are
personal bests only.

Keep exact cooked hashes for provenance separately from the compatibility key.
Aggregate cooked/package hashes include client presentation, and even runtime
definitions contain presentation/identity fields. There is no existing gameplay-only
hash that can simply be adopted.

Implement a deterministic compatibility fingerprint over an explicit gameplay
projection: relevant fighter definition and deterministic poses, stage gameplay
geometry/challenge settings, fixed profile/seed and effective Shared/Arcade rules.
Exclude presentation-only fields/bindings. Test both directions: gameplay changes
separate comparable categories, while cosmetic changes retain them. The manifest's
per-payload hashes can support provenance and change detection, not replace this
policy. Changing a kit, map or rules enough to affect comparability creates a new
category and retains the old best in the archive.

## Pilot course authoring brief

Use the mountain martial-arts sanctuary selected in the feature brief. Its first
clean completion target is 1–2 minutes. The approved authoring route is:

| Area | Geometry and routing | Learning opportunity |
| --- | --- | --- |
| Training courtyard | Broad landings, practice rooftops and target lanes | Basic/charged attacks, projectile reach and jumping |
| Pole garden | Supported pole tops and static rope bridges; forgiving main route and narrower shortcut | ShortHop, air control and landing precision |
| Floating terraces | Rising island steps and separate lateral gaps | E for height, R for travel and normal cooldown pacing |
| Roof descent and finish shrine | Descending roofs/pads, remaining targets and visible finish | FastFall once ordinary descent resumes and efficient routing |

This is the approved authoring direction; coordinates and traversal need validation.
No rope climbing/swinging, pole attachment or moving platforms are needed.
FightGuy E supplies height; R holds vertical motion at zero during travel and
cannot be treated as freely descending/jumpable motion. Tune from native gameplay,
not inferred move descriptions.

Place checkpoints on safe landings with cumulative prerequisites. Save progression
advances to later eligible markers; visits remain optional. Before the first save,
the initial start is the fallback. Keep unfinished required targets reachable.
Provide traversal/target work or alternatives during normal cooldowns.
Intentional fall/cooldown-reset routes are legal and have no extra penalty.

## Delivery slices

1. **Shared contact and unified-stage foundations.** Extend the single combat/
   target path, object observations and purpose-aware stage cook/load/validation.
   Prove focused ordinary attack/projectile object contacts without changing normal
   fighter behavior. Small target fixtures may test this seam; the first playable
   Arcade challenge remains Slopball.
2. **Slopball first playable.** Build the neutral court greybox, moving ball,
   player constraint, receive/contact/clear/landing ordering, percent, rebounds, fresh/same
   shot scheduling and capped fixed profile. Play every roster kit and establish
   that returning/juggling is enjoyable before finished environment work.
3. **Arcade replay loop.** Add offline menu entry, clean countdown, pause, stable
   terminal results, quick full retry and persistent/compatible personal records.
4. **FightGuy Course.** Author the static sanctuary greybox, one-hit nonblocking
   targets, cumulative optional checkpoints, persistent progress, legal respawns,
   whole-run clock and finish. Reuse entry/retry/records and validate forgiving and
   faster creative routes.
5. **Content and human acceptance.** Polish sports-court and sanctuary identity,
   camera/occlusion, object/route cues and opt-in tips. Tune exact ball values,
   timings and course geometry within the selected rules. Ship the two-challenge
   release after applicable integrated/demo checks and matching cooked content.

Architecture preparation and shared-understanding review are complete.
Exact classes, binary field layout and tuning values are implementation decisions
under these boundaries, not another open gameplay interview.

## Verification handoff

Focused behavioral checks should cover:

- Melee/projectile object contacts and normal assistance without a second mutating
  resolver pass; unchanged normal fighter/PvP behavior and kit resources.
- Fast-ball sweeps, floor/net distinctions and contact ordering; one score per
  ball; receive-first handling of early intercepts and wall/ceiling banks.
- Passive body speed conservation and harmlessness in every stance; percent reset,
  same-shot re-serve state, continuing fighter/projectiles and fresh identities.
- Full reset of pending inputs, active/queued effects, objects and profile sequence;
  locked Go boundary; pause eligibility; stable terminal score/time.
- Course target persistence, cumulative checkpoint saves with optional visits,
  legal normal fall resets and all-targets-plus-finish completion.
- Stop/retry banking, local persistence/archive and gameplay-versus-cosmetic
  compatibility, including long-lived attempt counters and target identity mapping.
- New artifact round trips, invalid profile/markers and old arena fixtures;
  unchanged PvP spawns, selection/admission and version-aware freshness.

Existing arena coverage includes
[ArenaBlastZoneTests](../../tests/Shared.Tests/ArenaBlastZoneTests.cs) with a v4
fixture and [AfterHoursArenaTests](../../tests/Shared.Tests/AfterHoursArenaTests.cs)
round trips. Extend relevant behavioral coverage; do not replace it with assertions
that mirror the new serializer.

Use the applicable mode in [testing.md](../testing.md). Pure Shared/Server checks
pass `-p:SkipUnityPluginCopy=true`. Unity-facing implementation follows
[CLI guidance](../contributing/unity-cli.md) and
[Editor coordination](../contributing/unity-editor-coordination.md), using its own
bounded lease, and leaves the required root `TESTING-UNITY.md` checklist.

Human acceptance must confirm offline entry/retry, a readable shared profile for
all four kits, normal camera/aim usability, both Course routes and the clean
1–2 minute target. Documentation checks in this preparation session do not
substitute for runtime or playtest acceptance. Multiplayer volleyball, online
object state, a general mode framework and Workshop challenge authoring are
future scopes.
