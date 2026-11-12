# Workshop authoring fix pass

Creators need a reference and checker they can follow literally. Maintainers need those tools to preserve cooked data and wire contracts.
This pass addresses all 15 review items on `t3/workshop-fighter-authoring`, starting at `a765ef36f589702478f368fcafd10f4ac3aeed28`.
No subagents, Unity Editor, pushes, PRs, new packages or tool installs were used. Only the two authorized Unity Editor self-test files changed.

## Findings and proof

| Item | Confirmed against code | Fix | Proof |
| --- | --- | --- | --- |
| 1 | Yes. DemoSim directly included Shared C# without its resource. It was the only existing source consumer. | SharedSources.projitems carries sources and schema together. DemoSim and the native smoke import it. The web workflow runs the smoke and triggers for Shared changes on PRs too. | DemoSim publishes. The source-consumer smoke reads all 60 schemas and loads committed FightGuy. WASM loads all seven fighters and completes the canonical After Hours match. |
| 2 | Yes. Both Editor tests and testing.md used the retired generic tick path. | Each test helper carries the actual selected slot, stage and operation path into its assertions. Documentation uses the indexed form. | Repository-wide literal search found no remaining old-path consumer. Another character.presentationIds occurrence creates an unrelated Unity binding diagnostic. The Editor tests remain unrun as instructed. |
| 3 | Yes. Several cooked-loader helpers, registry admission and FlashCut snapshot limits depended on authoring metadata. | All cooked/runtime bounds now come from code. XML supplies corresponding author-facing values checked against code. Capability admission uses existing constants. | Runtime consistency tests cover 132 field/context limits, named wire constants, and all public capability versions. Byte/corpus and real cooked-loading checks pass. |
| 4 | Yes. The captured LINQ predicate allocated on admission. | Restored direct constant comparisons on the runtime path. | GC.GetAllocatedBytesForCurrentThread reports zero in each of five measured windows of 400,000 admission calls after warmup. Both admitted and rejected IDs are exercised; return counts are checked. No latency claim is made. |
| 5 | Yes. Optional readers had dead fallbacks, ContainsKey branches had separate defaults, and some bounds were inline only. | Readers get omission values from schema data. Removed placement/ushort/enum fallback arguments and the separate aim scale, anchor and spray-loop defaults. Runtime limits are code-owned and shared; source-only limits stay in schema. Context bounds cover grab capture and thrown tether. Numeric range messages read the same limits. | Mutation and default-insertion checks preserve bytes and diagnostic codes. Stage tracking and capture duration bounds appear in their rows. |
| 6 | Yes. Tether mode exclusions and null anchor omission were misrepresented. | Added IsAllowed/ForbiddenWhen and HasWritableDefault. Tables distinguish omission from writable JSON values. Placed tether fields are forbidden, and anchorDistance must be omitted or numeric. | Explicit null/forbidden-field tests preserve the original diagnostic codes. Every writable documented default has a valid byte-identical insertion context. |
| 7 | Yes. Template creation overwrote files and did not validate IDs. | Compile the template before writing, check both destinations, and use FileMode.CreateNew. | Unit tests and published CLI reject invalid IDs and preserve an existing character.json, with no package.json written. Both failures exit 2. |
| 8 | Yes. Record equality made IndexOf report the first equal operation. | Loops carry real indices. Single capability lookups use object identity. | Two identical invalid lunges report operations[0] and operations[1]. Corpus codes/order are unchanged. |
| 9 | Yes. The previous corpus and two examples covered too little. | Added deterministic mutations over all seven built-ins, both examples and a coverage kit, under both profiles. Added all-instance removal/default checks, plus phase-omission variants. | Counts and the original-main comparison are below. All 60 schema objects are reached. |
| 10 | Yes. Crouch reduction and recovery turn re-capture were omitted. | Documented settled-crouch velocity reduction without hitstun reduction, and the early recovery-turn updates to hop facing. | Read Simulation.ApplyCrouchBrace, ServerSimulation.IsCrouchBraceEligible, AimedRecoveryAbility.OnRecoveryTurn and CookedTimelineAbility's recovery-turn window. |
| 11 | Yes, including on origin/main. Its checker warns on the trickster's required dash tip cue. | Count nested capability hitbox, phase, capture, projectile and perch cues as references. | The trickster now has zero diagnostics. A test preserves a warning for a genuinely unused cue. Only presentation.unused-id is excluded from the compatibility hash. |
| 12 | Yes. Launch arithmetic did not establish a KO or typical scale. | Generated roster weights and resolved Training blast lines from committed data. Added post-hitstun rules and a worked trickster case. Added moves using Simulation.ApplyKnockback and ApplyFixedHitstun, with captures labelled as holds. | Published moves output is recorded below. Simulation-backed regression checks include ordinary and fixed hitstun. |
| 13 | Yes. Timing bases, pass-through, armor companions, names and required air slots needed explicit statements. | Corrected the parameter descriptions and added short lifecycle sections with exact constraint links. | Read ChargedProjectileAbility, CookedTimelineAbility, DashStrikeAbility, CommandGrabAbility and compiler companion rules. Both examples and the trickster compile without diagnostics. |
| 14 | Yes. Code uses capsule-center-relative fallback geometry; both examples had misplaced heads. | Corrected torso, head and leg Y positions in both examples. Their head tops are now at 0.84 m within a 0.85 m half-height. | Both compile. Tests bound every fallback capsule endpoint plus its radius against the movement-capsule half-height. Built-in data was not edited. |
| 15 | Yes. The generated rows deferred scalar facts to distant prose. | Rows show actual shared limits, storage bounds, context bounds and whole-number requirements. Coupled rules link to the exact capability paragraph. | Generated-reference drift test and documentation validation pass. No statement scraping or code-fragment headings returned. |

## Ownership of limits and versions

| Owner | Numbers or definitions |
| --- | --- |
| CharacterPackageCompiler code constants | All 14 public capability ID/version pairs. The targeted-leap and steered-charge versions are 2; other public versions remain 1, including shell-stance.v2. Source versions remain manifest 1 and authoring 4. Startup limits remain range 32 m, yaw/cone 180 degrees, pitch 90 degrees, rate 1440 degrees/s; steered turn remains 1440 degrees/s; fixed hitstun remains 240 ticks. |
| CharacterRuntimeLimits code | 132 scalar field/context definitions consumed by cooked validation and source compilation. The exact field names and numbers are in src/Shared/CharacterRuntimeLimits.cs. XML values are consistency-checked, rather than used as runtime authority. |
| FlashCutContract code | Snapshot target limit 7 and maximum hold 240 ticks. ReadSnapshot uses the code constant. |
| ProjectilePerchContract code | Turn 90 degrees/tick, perch 1200 ticks, pop stun and dive phases 240 ticks, hover height 12 m, dive speed 60 m/s, fallback distance 20 m and peck damage 20. Peck damage is whole-number only. |
| CommandGrabContract and CharacterRuntimeLimits code | Capture duration at most 30 ticks and radius at most 3 m. A thrown tether has speed at most 60 m/s, radius at most 3 m and flight at most 240 ticks. |
| CookedBudget code | Existing runtime budget ceilings remain code constants. |
| CharacterSourceSchema.xml | JSON keys, source types, requiredness, omission defaults, writable-default and conditional-field policy, units, descriptions, enum tokens, child schemas, source-only scalar limits, operation units and authoring budget costs. |

The public read-only API also exposes contextual ranges, conditional admission, writable-default status and whole-number requirements.
Identifier grammar, SemVer, resolved references and coupled geometry/lifecycle relationships retain code validation and plain reference explanations.
They are not duplicated as an executable schema language. A complete-kit compile remains the final admission check.

## Baseline and regression corpus

The initial pre-edit capture had 51,042 cases and 12,312 successful byte outputs/round trips.
The final expanded matrix uses original-main compiler sources archived in a scratch folder from `b9276e5806a3e1c8896b558b7d2079ebb2e81f48`.
It has ten source inputs under both Workshop and TrustedBuiltIn. The tenth kit supplies the four missing schema objects.
Mutation families use representative structural paths distinguished by operation kind and capability ID. They do not claim every possible field combination.
Full source JSON inputs and numeric edge definitions are frozen. Outputs are digests per input, profile and family, not tens of thousands of rows.

| Family | Cases |
| --- | ---: |
| field-insertion | 314 |
| original | 20 |
| range-edges | 24,682 |
| removal | 5,418 |
| types | 27,090 |

Both original-main and pre-fix head produce 57,524 cases and 14,409 successful byte outputs and round-trip comparisons.
After this pass, every contract digest matches the pre-fix head. Every original-main contract digest also matches except two pre-existing Manki type-family digests.
Those contain ten malformed maxCharges JSON-type cases, five types under two profiles. Main throws InvalidOperationException; a765ef36 returns value.out-of-range.
This pass preserves the branch's structured diagnostics. It does not restore the main compiler's crash or claim those ten cases agreed with main.

An additional self-identity assertion was tried and rejected. The explicit-null Bonk riseAnimationId mutation changes its initial-versus-reserialized cooked bytes on both origin/main and this branch. That existing behavior is preserved. The matrix compares original and new compiler results for both stages; it does not require source self-round-trip identity.

The one intentional new diagnostic change is the existing false presentation.unused-id warning.
Its corpus count falls from 69,626 to 40,536, a removal of 29,090 false warning instances.
All other diagnostic codes, severity and order, compiled bytes, and round-trip bytes are included in the compatibility hash.
The test also rejects an increase in unused-cue warnings, and a focused test proves genuinely unused declarations still warn.

The all-instance field runner reaches 60 schemas through 1,790 objects in twelve contexts.
It compares 17,846 field removals with the original compiler's exact code sequence and cooked bytes.
All 6,077 default insertions are byte-identical to omission, and every writable documented default has a valid context.
The two original examples also keep the direct required/optional tests. Coupled requirements in built-ins are checked against exact original outcomes.
The first legacy 43-case corpus still preserves all compiled bytes and every diagnostic code except the corrected unused-cue warning.

The corpus runner is retained in tools/KitCheck/MutationContract.cs and FieldContract.cs.
tools/CharacterContractBaseline builds it against a selected scratch compiler. Reproduction commands and known differences are in the fixture README.
No content-cooked files or built-in authoring files changed. The two example bodies changed intentionally to correct their geometry.

## Source checks for the explanations

- Simulation.cs owns launch arithmetic, fixed hitstun, crouch velocity scaling, launch pop/gravity and post-hitstun flight braking.
- ServerSimulation.cs owns percent accumulation, settled-crouch eligibility and strict blast-line position tests.
- DownActionTuning.cs supplies the default crouch multiplier. CombatTuning.cs supplies ordinary launch and hitstun tuning.
- ArenaDefinition.cs owns blast-line fallback resolution. data/arenas/training.arena supplies the measured Training values.
- AimedRecoveryAbility.cs and CookedTimelineAbility.cs own hop-facing re-capture and the early three-tick recovery-turn window.
- ChargedProjectileAbility.cs resets the release clock and changes only radius with charge. ChargedStreamAbility.cs changes stream duration, push and recoil instead.
- DashStrikeAbility.cs ignores fighter pushboxes during travel, seeks tipPoseTick at the endpoint and stops into recovery on Shield block. DashStrike.cs computes ceil travel ticks.
- CommandGrabAbility.cs and CommandGrab.cs own activation-relative capture/release timing and capture-versus-launch semantics.
- HitboxGeometry.cs and ServerSimulation.cs place fallback vulnerable geometry relative to the movement-capsule center.
- CharacterPackageCompiler.cs owns the per-capability armor/companion and cue-prefix rules.

## Automated and CLI verification

The final full Shared suite passed: 5,592 tests, zero failures or skips, in 12 minutes 19 seconds.
The focused field/removal and regression checks also pass. python3 tools/check_docs.py passes across 473 Markdown files.
The first full run passed 5,591 tests and rejected the overstrong source self-round-trip assertion described above; the final run uses the corrected comparison.
Documentation validation passes. Every dotnet build, run, publish and test invocation uses -p:SkipUnityPluginCopy=true.
Direct dotnet DLL executions are runtime invocations, so they take checker arguments rather than MSBuild properties.
The published bundle runs outside the repository and includes the reference and both examples.

Published checker commands and output follow.

```text
$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll /tmp/workshop-kitcheck-review-final/example-kit
COMPILE OK (0 diagnostics)
Exit 0

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll /tmp/workshop-kitcheck-review-final/example-grappler
COMPILE OK (0 diagnostics)
Exit 0

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll /home/binoui/Documents/projects/SlopArena/tmp/workshop-spikes/phase2-fighter/item/source
COMPILE OK (0 diagnostics)
Exit 0

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll /home/binoui/Documents/projects/SlopArena/tmp/workshop-spikes/phase2-reference/fresh-agent-1/knight
COMPILE OK (0 diagnostics)
Exit 0

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll /home/binoui/Documents/projects/SlopArena/tmp/workshop-spikes/phase2-reference/fresh-agent-2/trickster
COMPILE OK (0 diagnostics)
Exit 0

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll moves /home/binoui/Documents/projects/SlopArena/tmp/workshop-spikes/phase2-reference/fresh-agent-2/trickster
COMPILE OK (0 diagnostics)
Weight 100, default combat tuning. Percent columns are BEFORE the hit. Speed is m/s; stun is ticks.
Single ordinary contact estimates. Charge tiers, captures, held hits and capability hooks may replace these results; verify those moves in play.
Hitbox | Damage | 0% speed/stun | 100% speed/stun | 150% speed/stun
character.slots[ground.1].timeline.stages[0].operations[0].hitbox | 2 | 0.324/12 | 0.358/12 | 0.375/12
character.slots[ground.1].timeline.stages[0].operations[1].hitbox | 2 | 0.324/12 | 0.358/12 | 0.375/12
character.slots[ground.1].timeline.stages[0].operations[2].hitbox | 3 | 0.906/2 | 1.076/2 | 1.161/3
character.slots[ground.2].timeline.stages[0].operations[0].hitbox | 6 | 1.312/3 | 1.652/4 | 1.822/4
character.slots[ground.3].timeline.stages[0].operations[1].hitbox | 16 | 3.747/9 | 5.277/13 | 6.042/15
character.slots[ground.3].timeline.stages[0].operations[2].hitbox | 16 | 3.747/9 | 5.277/13 | 6.042/15
character.slots[ground.A].timeline.stages[0].operations[0].parameters.lightHitbox | 3 | 0.051/14 | 0.051/14 | 0.051/14
character.slots[ground.A].timeline.stages[0].operations[0].parameters.tipHitbox | 7 | 1.333/3 | 1.673/4 | 1.843/4
character.slots[ground.R].timeline.stages[0].operations[0].parameters.projectile | 6 | 0.882/2 | 1.137/3 | 1.265/3
character.slots[ground.F].timeline.stages[0].operations[0].parameters.capture | 0 | hold; no ordinary launch | hold; no ordinary launch | hold; no ordinary launch
character.slots[ground.F].timeline.stages[0].operations[0].parameters.launch | 8 | 1.785/4 | 2.21/5 | 2.423/6
character.slots[air.1].timeline.stages[0].operations[0].hitbox | 2 | 0.324/12 | 0.358/12 | 0.375/12
character.slots[air.1].timeline.stages[0].operations[1].hitbox | 4 | 0.925/2 | 1.095/2 | 1.18/3
character.slots[air.2].timeline.stages[0].operations[0].hitbox | 6 | 1.312/3 | 1.652/4 | 1.822/4
character.slots[air.3].timeline.stages[0].operations[0].hitbox | 9 | 1.729/4 | 2.239/5 | 2.494/6
character.slots[air.3].timeline.stages[0].operations[1].hitbox | 9 | 1.729/4 | 2.239/5 | 2.494/6
character.slots[air.A].timeline.stages[0].operations[0].parameters.lightHitbox | 3 | 0.051/14 | 0.051/14 | 0.051/14
character.slots[air.A].timeline.stages[0].operations[0].parameters.tipHitbox | 7 | 1.333/3 | 1.673/4 | 1.843/4
character.slots[air.R].timeline.stages[0].operations[0].parameters.projectile | 6 | 0.882/2 | 1.137/3 | 1.265/3
character.slots[air.F].timeline.stages[0].operations[0].parameters.capture | 0 | hold; no ordinary launch | hold; no ordinary launch | hold; no ordinary launch
character.slots[air.F].timeline.stages[0].operations[0].parameters.launch | 8 | 1.785/4 | 2.21/5 | 2.423/6
Exit 0

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll template /tmp/workshop-review-cli/invalid-id INVALID ID Invalid
id.invalid manifest.packageId: ID 'INVALID ID' must be lowercase ASCII, start with a letter, contain only letters, digits, dots or hyphens, and be at most 64 characters.
Exit 2

$ dotnet /tmp/workshop-kitcheck-review-final/KitCheck.dll template /tmp/workshop-review-cli/existing local.valid Valid
Refusing to overwrite /tmp/workshop-review-cli/existing/character.json
Exit 2
```

Web verification uses these commands.

```sh
dotnet publish client/Web/sim/DemoSim.csproj -p:SkipUnityPluginCopy=true -c Release -o client/Web/publish -p:PublishTrimmed=true
dotnet run --project tools/SharedSourceSmoke -p:SkipUnityPluginCopy=true -- .
node client/Web/scripts/smoke.mjs brawl after_hours fightguy,manki
```

The publish succeeds with zero build errors. The existing SDK notes that optional wasm-tools optimizations are unavailable; no workload was installed.
The Shared-source smoke prints:

```text
Embedded schema: 60 objects
Cooked fighter loaded: fightguy
```

The real published WASM smoke prints:

```text
catalog bashie,bonk,fightguy,manki,nyx,salamander,wibou
arena {"name":"After Hours","color":"#24374d","killHeight":-12,"bounds":[-15,15,-12,12],"spawns":4,"pvp":true}
special cooldown: Ki Shot, 120 ticks -> 0
ticks 7719 in 4583 ms = 0.594 ms/tick; hits 138, deaths 5, ended true
fightguy dmg 0 deaths 3 state Idle | manki dmg 116 deaths 2 state Idle
```

An earlier Training-stage smoke loaded all seven fighters but reached the script's 10,800-tick limit without finishing the brawl.
It is not recorded as a completed-match pass. The canonical After Hours smoke above completes with exit 0.
Timing printed by the existing smoke is incidental; no performance comparison is claimed.

## Unity self-tests for the owner

These were not run. The Editor was never opened.

- Run Tools/SLOPArena/Tests/Character Package Authoring, or CharacterPackageAuthoringSelfTest.RunFightGuyAuthoringSelfTest(). Its invalid hitbox helper now returns the exact path it edited, and both Inspect and Cook assertions compare that path. Current FightGuy selects character.slots[0].timeline.stages[0].operations[1].tick. A pass logs the self-test passed message, restores its fixture and verifies invalid cooking did not alter persisted outputs.
- Run Tools/SLOPArena/Tests/Editor Development Content, or EditorDevelopmentContentSelfTest.Run(). Its invalid-source helper also returns the selected path. The failed local catalog must include value.out-of-range and that indexed path, while the persisted catalog still resolves the pinned package. Its existing restoration and byte checks remain in place.

Only those two authorized Unity files changed. docs/testing.md describes the indexed path form.

## Execution choices

The blocking first step was the original-main baseline. Source/consumer fixes and documentation were independent checks under one owner.
Shared writes stayed serialized in this checkout. One owner was the smallest safe decomposition and was explicitly required by the user.
Model the Domain shaped the code-owned field/context limit table. Build the Lever shaped the reproducible corpus and source-consumer smoke.
Both principle leaf skills were read for this work. No PR or push was made.
