# Simulation review fixes

The owner requested fixes for the findings in the 2026-10-23 simulation review.
The review and executable counterexamples are local artifacts under
`tmp/reviews/simulation-2026-10-23/`. Implementation starts from game commit
`40d4e78439dec8eaae24dd9ec4df8b201e9d8f9f`.

## Completion contract

Each reproduced failure gets a focused behavioral regression that fails before
the fix and passes afterward. Shared and Server checks must pass without copying
the plugin into Unity. Unity-facing changes also require the canonical gateway
compile and affected runtime checks. Character package changes require the
applicable cook and verification steps. Existing scene, package, and unrelated
working-tree changes must be preserved. No commit, push, deployment, or tool
installation is authorized by this task.

## Behavioral work

1. Clear attack-owned state on interruption while preserving incoming reactions
   and velocity. Reuse one reset contract across existing interruption paths.
2. Resolve the running move's timing and pose from activation identity.
3. Preserve unseen late input edges through reordered redundant bundles.
4. Commit frame metadata and presentation effects only after snapshot acceptance.
5. Restore the exact final world on Ended and retract speculative future effects.
6. Sweep projectiles against ordinary fighter geometry and preserve first contact.
7. Remove all owned effects on permanent entity removal while retaining intentional
   projectile persistence through ordinary interruption and KO.

## Structural options requiring owner approval

Root AGENTS.md requires options and prior approval for architecture changes.
The options below concern only the remaining structural findings. The behavioral
work above proceeds under the owner's existing request.

### Option 1, bounded consolidation, recommended

- Protect admitted character definitions and baked pose data from external
  mutation. Preserve mutable authoring and isolated test fixtures. Use the
  existing content and simulation boundaries rather than introducing a new
  content framework.
- Consolidate move reads around cooked slot identity. Migrate real consumers
  before removing unused compatibility helpers, unsupported parameter paths,
  and duplicate slot mappings. Preserve shipped move behavior unless this plan
  names a correctness fix.
- Pin an exact baked arena digest in match setup and verify it before Ready.
  Coordinate the protocol and matching contract in the local
  `../SlopArena-MasterServer` repository. This is a
  source and test change only. Release activation remains separate.
- Migrate useful legacy rollback assertions into the current full-world tests,
  then retire the unused three-track runtime and its obsolete codecs. Retain
  TickPacer and other live infrastructure. Selecting this option approves this
  bounded retirement in place of waiting for the old plan's phase-7 owner gate.
- Make supported native capability parameters govern execution, or remove an
  unsupported field through an explicit compatible migration. Consolidate
  repeated retired-capability validation. Keep active Manki and Wibou execution
  and approved character stand-ins functioning.

This option fixes the ownership and compatibility causes of the review findings.
It spans Shared, clients, package tooling, and the Master match contract. Changes
must remain in independently verified units.

### Option 2, behavioral fixes only

Complete the seven behavioral units and leave the mutable-content boundary,
parallel move representations, arena digest, and retired-runtime removal as open
follow-up work. This has less integration work, but does not close the full review.

The owner selected Option 1, including bounded legacy retirement, on 2026-10-23.

### Selected implementation

Keep the existing owners. Attack fields have one reset contract, while reactions
retain their state and launch velocity. Cancellation callbacks run on a scratch
state only after a reaction or defense has taken ownership. Preserve existing
ability admission clocks; resolve their move from the activation rather than the
fighter's current ground contact. Snapshot restoration does not run cancellation
callbacks.

The predictor owns accepted frame metadata and presentation records. The session
reads that metadata after validation. A terminal frame restores its exact world
and ends the prediction window. Catalog hashes remain character-only; match hashes
also pin the arena. Existing reserved input slots and Steam message kinds remain
reserved.

Replace Wibou F's movement-only native capability with existing typed operations
only if ground and air trajectory checks, including held movement and defense
inputs, preserve its motion. Otherwise publish a versioned capability with explicit
supported timing. Either migration changes Wibou's Arcade compatibility fingerprint;
older local records remain stored but no longer compare against the new content.
Do not remove the retired movement stats that would invalidate every fighter's
Arcade records.

The owner-approved retirement replaces the old phase-7 deletion gate. It does not
claim that the pending Steam playtest or cross-runtime parity measurement occurred.

## Execution and evidence

- Blocking first steps are baseline reproduction, approval, and design selection.
- Independent workstreams are combat, netplay, and arena admission.
- Shared mutable state is isolated in worktrees, with selective serial integration.
- The smallest safe decomposition keeps content and overlapping runtime edits
  with the coordinator while separate owners handle disjoint behavior and wire
  contracts.

Use isolated worktrees for independent combat and netplay implementation.
Integrate their uncommitted patches selectively into the selected checkout.
Content, protocol, and retirement changes follow the approved option and serialize
where they touch the same contracts. Independent design candidates and the
append-only decision trail live under ignored `tmp/simulation-review-fixes/`.

The verification predicate is corrected behavior in the original counterexamples,
focused regressions, full relevant suites, and applicable Unity or package checks.
An unavailable runtime or external contract is reported explicitly. A passing
compile does not close an unverified behavioral gate.
