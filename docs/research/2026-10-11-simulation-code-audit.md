# Simulation code audit — 2026-10-11

Audit baseline: `99f1413`. Scope: Shared simulation, ability execution,
character runtime adaptation/catalog construction, collision and hit resolution,
rollback, CPU policy, and their GameServer/Unity callers. This is a source and
automated-test audit, not a Unity playtest or a performance benchmark.

The largest debt at the baseline was duplicated cooked and legacy ability handling.
The architecture already admitted cooked characters exclusively, but some runtime
consumers executed projected `AbilitySpec`/`AttackStage` events in addition to the
cooked timeline. The cleanup below removes that duplicate execution while retaining
the projections needed by input, presentation, and pose geometry.

Nilus's character implementation was retired on 2026-10-02. There are no tracked
Nilus runtime classes or packages. The remaining named files are historical
documents; `MatchStartRequestCodecTests.TryParse_RetiredCharacterClass_ReturnsNull`
appropriately checks that Nilus is rejected. Supporting mechanics from that era
had no current gameplay producers and have now been removed.

## Implemented cleanup

All eight ranked candidates were addressed in this checkout:

- Wibou E/F movement capabilities leave hitbox spawning and completion to the cooked
  timeline. Exact spawn-count tests cover ground and air variants at ticks 19, 14,
  and 60.
- Manki's bomb reads its typed throw trigger; cooked-path tests exercise 3 and 17
  ticks after release. Bomb and bazooka use their typed parameters directly. The
  bomb's sole-use `AimHoldAbility` base was folded into the bomb lifecycle.
- Shared hitbox conversion and semantic bone mapping serve the adapter, timeline,
  leap, dash, and CPU reach. A bone-anchored regression verifies that projection,
  runtime geometry, and CPU reach agree.
- Ability exits share cleanup/cooldown helpers. Movement/ability IASA, hitstun,
  shield, death, and authoritative replacement have regression coverage. Gameplay
  interruption removes melee windows while launched projectiles survive; replacement
  removes the active activation's projectiles and preserves the exact incoming state.
  FightGuy's directly spawned cyclone windows also opt into interruption cleanup.
- Direct deep copying replaces the JSON serialization round trip, and catalog entries
  are built once after sorting. Tests check nested mutation isolation and retention
  of shield, grab, grabbed, throw, and air-dodge animation fields that cloning lost.
- Dormant automatic warp and stage tracking, zone rehit/body-ignore support, direct
  yank velocity restoration, unused mine detonation and counter hooks, and status
  timers were deleted. Compiler and loader reject non-default retired targeting
  fields; default source/cooked metadata remains compatible.
- CPU selection no longer offers legacy moves that activation would reject. Dead
  ability metadata and dictionary parameter fallbacks were removed with their callers.

Protocol 6, packet layout, numeric slot positions, and the reserved `Warping` state
value are unchanged. The removed warp/status/yank state fields were local simulation
data, not packet fields. Saved input-report warp metadata remains readable and has
no gameplay effect. Historical Nilus documents and its rejection test remain.

## Ranked refactoring candidates at the baseline

Ranked approximately by removable implementation size, not by bug urgency.
The original estimates below exclude test deletions and overlap between larger
follow-up migrations. Source locations refer to the baseline, before cleanup.

1. **delete:** Retire the 379-line legacy character JSON serializer after replacing its sole production caller, definition cloning, with a direct deep copy or fresh cooked adaptation. Removed file: `src/Shared/CharacterContentSerializer.cs`; caller: [MatchContentCatalog.cs](../../src/Shared/MatchContentCatalog.cs) (`CloneDefinition`, line 240). Rough net cut: 300 lines.
2. **delete:** Retire dormant automatic warp/chase and legacy per-stage target tracking. All 64 admitted cooked slots have `warpRange = 0`, `useTargetLock = false`, `rotateTowardTarget = false`, and `trackingStrength = 0`. Keep the live target-lock system and authored lunge/dash/leap capabilities. [ServerSimulation.cs](../../src/Shared/ServerSimulation.cs) (lines 887, 1921, 2579), [Simulation.cs](../../src/Shared/Simulation.cs) (`ProcessWarp`, line 1868). Rough net cut: 180–220 lines.
3. **delete:** Retire unused Nilus-era zone/projectile/yank support: `RehitIntervalTicks`, `IgnoresEntities`, and the direct-velocity `QueuedKVOverride` launch restore. Current packages and runtime capabilities never produce these values; references are resolver plumbing and synthetic tests. [Hitbox.cs](../../src/Shared/Hitbox.cs) (lines 94, 118), [SpellResolver.cs](../../src/Shared/SpellResolver.cs) (line 301), [Simulation.cs](../../src/Shared/Simulation.cs) (line 244), [ServerSimulation.cs](../../src/Shared/ServerSimulation.cs) (line 2461). Rough net cut: 100–150 lines.
4. **shrink:** Finish capability migration: let Wibou's cooked timeline own authored hitbox spawning and ordinary completion; read Manki's typed parameters directly and replace old dictionary defaults with explicit engine constants where those values are not authored. [WibouRisingSlash.cs](../../src/Shared/Abilities/WibouRisingSlash.cs) (line 72), removed source: `src/Shared/Abilities/WibouUltFlurry.cs` (line 68), [MankiRoundBomb.cs](../../src/Shared/Abilities/MankiRoundBomb.cs) (line 54); removed base: `src/Shared/Abilities/AimHoldAbility.cs` (line 109). Rough net cut: 60–100 lines.
5. **reuse:** Share cooked-hitbox conversion and semantic bone mapping. Six conversion sites cover the adapter, timeline spawn/lunge, targeted leap, directional dash, and CPU reach; two bone mappings already disagree on `bone.spine`. [CookedCharacterRuntimeAdapter.cs](../../src/Shared/CookedCharacterRuntimeAdapter.cs) (line 51), [CookedTimelineAbility.cs](../../src/Shared/Abilities/CookedTimelineAbility.cs) (lines 310, 380, 518), [HeuristicBotPolicy.cs](../../src/Shared/AI/HeuristicBotPolicy.cs) (line 1159), [TargetedLeapAbility.cs](../../src/Shared/Abilities/TargetedLeapAbility.cs) (line 154), removed source: `src/Shared/Abilities/ChargedDirectionalDashAbility.cs` (line 227). Rough net cut: 50–90 lines.
6. **reuse:** Give ability cancellation/completion one owner with explicit policies for cooldown, state reset, owned hitboxes, and effects permitted to linger. Five cancellation paths currently repeat different subsets of that work. [ServerSimulation.cs](../../src/Shared/ServerSimulation.cs) (lines 178, 333, 733, 960, 2672). Rough net cut: 40–70 lines.
7. **delete:** Remove the CPU's non-cooked move-evaluation fallback once cooked definitions are enforced at that boundary. Its `AbilitySpec` path reports moves that the current activation path would reject; only the cooked branch is needed for admitted roster gameplay. [HeuristicBotPolicy.cs](../../src/Shared/AI/HeuristicBotPolicy.cs) (lines 1103–1145). Rough net cut: 40 lines.
8. **delete:** Remove dead metadata and status scaffolding: `AbilityTypeId`, `ChargedStages`, unused `HudSlotCount`, and `StatusType`/status countdown fields. The status system has no gameplay producer or effect consumer; its tests only expire manually injected flags. `src/Shared/AbilitySpec.cs` (baseline lines 67, 92; removed by the [2026-10-23 migration](../plans/2026-10-23-simulation-review-fixes.md)), [AbilitySlots.cs](../../src/Shared/AbilitySlots.cs) (line 35), [Simulation.cs](../../src/Shared/Simulation.cs) (line 831); removed file: `src/Shared/StatusType.cs`. Rough net cut: 40–60 lines.

## Behavior findings from a separate review pass

These baseline findings were verified with a temporary C# probe before changing
gameplay source. The implemented fixes now have repository regression tests.

### Wibou E/F execute authored hitboxes twice

`CookedCharacterRuntimeAdapter.AddLegacyAbilityViews` projects each
`CookedSpawnHitboxOperation` into a legacy `HitboxEvent`.
`CookedTimelineAbility.ExecuteOperations` executes the original operation.
`WibouRisingSlash.Tick` and `WibouUltFlurry.Tick` also spawn the projected event.

Running the admitted Wibou package with its baked animation, without a target or
hitstop, produced:

```text
ground.E: authored 1 hitbox at tick 19; active=2; damage=[1,1]; histories=2
ground.F: authored 1 hitbox at tick 14; active=2; damage=[4,4]; histories=2
ground.F: authored 1 hitbox at tick 60; active=2; damage=[16,16]; histories=2
```

The hit histories are separate objects. The resolver's one-hit-per-entity-per-tick
gate can mask duplication on the first contact tick, but it does not merge the
two hitboxes' hit histories. A second contact is consequently possible from
what the package authors as one event. This audit verified duplicate spawning
and independent histories; it did not measure damage in a live Unity match.

Candidate 4 addressed the duplication. Regression coverage now expects one authored
hitbox, its authored damage, and its hit group. This intentionally changes contact
behavior where duplicate hit histories previously allowed another hit.

### Manki's cooked bomb trigger is ignored

`CookedMankiRoundBombCapabilityParameters.ThrowTriggerTick` is accepted and stored,
but `MankiRoundBomb` does not read it. Its base `AimHoldAbility.Tick` reads
`GetParam(def, "throw_trigger_tick", 10f)` instead. The runtime adapter leaves
`AbilitySpec.Params` empty, so the trigger is always the fallback.

A probe changed only the typed parameter and drove the existing hold/release
lifecycle:

```text
Manki round bomb: typed ThrowTriggerTick=3; first projectile at release tick=10
Manki round bomb: typed ThrowTriggerTick=17; first projectile at release tick=10
```

The committed Manki package currently authors 10, so its current default timing
matches by coincidence. Authoring another accepted value does not change the
runtime timing. The cleanup uses the typed trigger and verifies both values through
the cooked simulation path.

### IASA cancellation has different owned-hitbox cleanup

`TryCancelGroundedNormalOnMovement` calls `RemoveOwnedHitboxes`.
The ordinary ability-to-ability IASA cancellation in `PreTickAbilities` does not.

A synthetic cooked normal with an active 20-tick hitbox and IASA at tick 4 gave:

```text
IASA movement cancel: remaining previous hitboxes=0
IASA ability cancel: remaining previous hitboxes=1
```

This is a reproduced inconsistency, not a claim that every lingering projectile
should disappear on cancellation. Candidate 6 now expresses that policy in one
place: melee windows are cancelled, launched projectiles survive gameplay exits,
and authoritative replacement discards the old callback's state writes.

## Evidence and limits for deletions

- **Legacy serializer:** all tracked source callers outside its own tests are
  `MatchContentInternals.CloneDefinition`'s serialize/load pair. That caller then
  separately repairs/copies fields and deep-clones stages. The catalog builder
  constructs each entry twice, cloning definitions and baked poses twice during
  handle assignment. Build entries once after sorting the admitted packages.
- **Dormant warp/tracking:** inspected all four source packages and all four
  committed cooked runtime documents, including aliases expanded to 64 slots.
  None opts into this mechanism. Retain the versioned wire values initially;
  retiring authoring fields needs an explicit schema/content migration rather
  than silently accepting values the runtime no longer implements.
- **Nilus support:** searched tracked source, tests, content, client assets,
  documentation, and strings. Only synthetic tests set positive rehit intervals
  or body-ignore flags. No shipped `OnHitEntity` override writes target knockback
  velocity; Wibou uses it for charge refunds, and Manki for rocket self-damage.
  `OnHitEntity` itself is therefore live and should stay. Counter interception
  likewise had only a fake-test implementation and was removed.
- **CPU fallback:** all roster definitions contain cooked slots; activation rejects
  missing cooked slots. Check diagnostic fixtures that intentionally omit cooked
  content before removing their fallback expectations.
- **Metadata/status:** searched both symbol names and dynamic/string references.
  `AbilityTypeId` and `HudSlotCount` have no consumers. `ChargedStages` is only
  copied. Status flags are only declared, counted down, and injected by tests.
  `BoneTrails` still has renderer readers and was retained. `SpecialEffectKeys`
  lost its sole reader with the CPU fallback and was removed.

## Match-owned combat tuning follow-up

Follow-up baseline: `8e813f4`, after the legacy cleanup above. The three mutable
process-wide combat values have been replaced by immutable
[CombatTuning](../../src/Shared/CombatTuning.cs), following the existing
`DownActionTuning` pattern. `ServerSimulation` owns its settings for its full
lifetime, including immediate hits, Hitstop-deferred launches, fixed-force hooks,
throw release, and hit-event impact force. Production callers retain the shipped
defaults: velocity scale 0.17, hitstun coefficient 0.45, magnitude bonus 0.

`TuningProfiles.Get`/`TryGet` return settings instead of mutating the process.
The profile table is read-only. Self-play, A/B reports, trajectory/DI/KO analysis,
combo probes, and route replay contexts pass settings explicitly. Replay provenance
records the owning context's values and rejects a replay from a different tuning
context. Existing CLI flags and replay schema are unchanged. The DLL diagnostic
supports both the old static-field layout and the new immutable default.

Regression coverage compares complete sequential and concurrent self-play traces
and report JSON, interleaves two profile-specific hit/Hitstop traces, and checks
fixed-force hooks, throws, route launches, and replay identity rejection. Shared
test collections now run concurrently with a bounded worker count. No production
race was demonstrated at the audit baseline; this removes the shared mutation that
previously required serialized tests.

## Next structural improvements

After this cleanup, reduce `ServerSimulation.cs` (2,520 lines, down from 2,815)
around concrete responsibilities: ability
lifecycle, target acquisition/aim correction, and coupled grab interactions.
Keep the existing tick phase order explicit. `Simulation.cs` (2,024 lines, down from 2,151) can
similarly separate stage movement/collision from input/action admission. Avoid
introducing a general state-machine or ECS framework to accomplish these cuts.

The legacy 11-slot wire layout still exists alongside the canonical 8-input /
16-entry package grid. `Lmb`, `Rmb`, and `Slot5` are dormant gameplay slots, but
their numeric positions and cooldown fields are part of protocol 6. Centralize
the canonical/wire mapping now; compressing the packet belongs in a coordinated
protocol change. Reserved Dash/Burst packet fields are also not safe blind cuts.

Character-specific classes such as Manki's bomb or Wibou's recovery remain live,
admitted stateful capabilities. Their names do not make them Nilus compatibility.
The `ServerAbility` base, aim/landing capability markers, shared geometry math,
and rollback state classification serve current runtime behavior.

## Verification

Baseline commands:

```bash
dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Server.Tests/ --nologo -p:SkipUnityPluginCopy=true
```

Baseline: **Shared: 1,265 passed, 2 skipped, 0 failed. Server: 27 passed, 0 failed.**
The Shared build emitted 15 existing source warnings, mostly nullability plus
one unreachable CPU-history loop. No warning cleanup was attempted.

Local evidence is under `/tmp/sloparena-simulation-audit/`: TRX files, test logs,
and `probe/Program.cs` with `probe-output.log`. These are disposable local audit
artifacts, not committed regression coverage. `SkipUnityPluginCopy=true` avoided
replacing Unity's generated Shared assemblies while running this audit.

Post-cleanup: **Shared: 1,267 passed, 2 skipped, 0 failed. Server: 27 passed,
0 failed.** Regression coverage replaces tests whose sole purpose was to exercise
the removed mechanics. Existing skipped tests remain skipped.

Additional verification:

```bash
dotnet build src/Shared/ --nologo
python3 tools/check_docs.py
python3 -m unittest discover -s tools -p 'test_check_docs.py'
git diff --check
```

Shared builds successfully and copies its updated assembly/dependencies to this
checkout's Unity plugin folder. It has 13 remaining pre-existing source warnings
and zero errors. Documentation checks pass across 207 Markdown
files, all 7 checker tests pass, and the diff has no whitespace errors. The docs
check also exposed an existing checkout-dependent research link, which now points
to its adjacent document. No Unity Editor compilation, playtest, or performance
benchmark was run.

Post-cleanup test/build logs are under `/tmp/sloparena-simulation-refactor-*.log`.

Post-tuning follow-up: **Shared: 1,280 passed, 2 skipped, 0 failed. Server: 27
passed, 0 failed.** Shared collections ran in parallel. The Shared build and
documentation checks also pass, and the DLL diagnostic successfully inspects both
assembly layouts. Before editing, an A/B report was captured for FightGuy,
`base` versus `floor30`, percent buckets 0/60, DI, the full true-combo graph, KO
probes, and one seeded self-play match per profile. The post-refactor report is
semantically identical in every field after excluding generation timestamps only.
This verifies both the shipped and non-default calculation paths without changing
the balance values. Reports, comparison receipt, and test logs are under
`/tmp/sloparena-tuning-*` and are disposable local evidence.

The remaining structural work is migrating runtime consumers off compatibility
projections and extracting focused subsystems
without changing tick order. Packet compression remains a separate protocol change.

Legacy cleanup net: -1,237 Shared source lines, -0 dependencies. Measured against the audit baseline;
test and documentation changes are excluded.
