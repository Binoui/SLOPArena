---
name: sloparena-kit-regression-testing
description: "Use for explicit kit regression coverage, KitScenario or AssertGoldenScenario maintenance, REGENERATE_GOLDENS, and diagnosing failing golden diffs. Not for routine numerical move tuning, adding content alone, or Unity-only presentation changes; use docs/testing.md for those verification modes."
---

# SLOPArena Kit Regression Testing

`tests/Shared.Tests/` has a deterministic scenario harness (`KitScenario` + `ScenarioRunner`) for the real `ServerSimulation.Tick()`. It accepts sparse per-tick inputs, initial state, an optional arena and default-input NPC, and returns mid-run and final states. `KitScenarioTests` supports focused behavioral assertions and optional JSON goldens under `tests/Shared.Tests/Golden/`.

Coverage is explicit: adding a character or ability does not create a scenario. Extend the relevant existing test file; do not assume a class naming pattern or historical coverage count.

## Choose coverage before writing tests

- **Numerical tuning:** use the affected Ability Lab/Training path and existing move-data reports. Do not add or regenerate goldens just because a number changed. Follow [Testing and verification](../../docs/testing.md) for local versus accepted content.
- **Mechanic changes:** prefer focused assertions for observable state transitions, contacts, timing boundaries, interruption, precedence, or errors. Add coverage only where a plausible consumer-visible regression would otherwise escape.
- **Golden maintenance:** use snapshots when preserving meaningful behavior across a deliberately chosen scenario. Investigate unexpected diffs before changing expectations; generation is not correctness evidence.
- **Unity presentation:** use the affected runtime surface and visual evidence, not a Shared golden as a substitute.

Do not pin incidental implementation details or defaults. Remove such assertions instead of re-pinning them; retain or add coverage for the actual gameplay contract where needed.

## The pieces

| File | Role |
|---|---|
| `KitScenario.cs` | Scenario setup, sparse `InputSequence.Press`/`Set`, snapshot/final states, optional NPC and arena. Unspecified ticks and NPC inputs are default input; hold buttons explicitly across required windows. |
| `KitScenarioTests.cs` | `AssertScenario` calls the player's final-state assertion; `ScenarioRunner` calls `NpcAssert` when provided. `AssertGoldenScenario` compares snapshots instead of invoking the player's `Assert`; do not put a mechanic assertion there and assume it ran. |
| `GoldenSnapshot.cs` | Serialization of selected gameplay state. `KitScenarioTests` is the source of truth for compared fields and float precision. |
| `TestHelpers.cs` | Existing definitions, initial states, ground-height helpers, baked data, and test arenas. Choose fixtures appropriate to the changed contract rather than incidental roster tuning. |
| `Golden/*.json` | One file per scenario, named from `KitScenario.Name` (spaces/slashes → `_`). Renaming `Name` orphans the old file — delete it by hand. |

## Writing a scenario

Start with the nearest existing behavioral test, such as `MankiKitTests.cs`, `BonkKitTests.cs`, or `MankiKitScenarioTests.cs`. Keep setup isolated and inputs explicit.

Use `AssertScenario` for final-state contracts, `NpcAssert` for victim outcomes, or inspect `ScenarioRunner.Run()` snapshot results for a specific timing boundary. Assert the relevant result directly; do not substitute a whole-state golden for one uncertain mechanic.

For a golden, choose `SnapshotTick` from the behavior being protected: active contact, a transition boundary, or another meaningful mid-run state. Explain the choice. An arbitrary idle snapshot does not prove the move worked.

Goldens only cover their selected states and inputs. They do not prove visual contact, arbitrary match interactions, human gamefeel, or networking.

## Scoped golden workflow

Only use this workflow after deciding a golden is appropriate. Replace `<ScenarioTestClass>` with the actual class from the current tree; confirm the filtered run executes the intended tests.

1. Run `dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true --filter "FullyQualifiedName~<ScenarioTestClass>" --nologo` without regeneration. Inspect the failure or existing snapshot against the intended gameplay contract.
2. For an approved new snapshot or intentional behavior change, run `REGENERATE_GOLDENS=1 dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true --filter "FullyQualifiedName~<ScenarioTestClass>" --nologo`. Generation writes expectations; its passing result does not establish correctness.
3. Inspect every affected JSON diff against the approved behavior and simulated outcome. Fix incorrect behavior, not its expectation. Never regenerate the whole suite to clear failures.
4. Re-run the same filter without `REGENERATE_GOLDENS` and require the intended tests to pass.
5. At delivery, run the applicable build, full-suite, and runtime checks from [Testing and verification](../../docs/testing.md). Do not build/cook for a skill or documentation edit.
6. Deliver the scenario and its approved golden changes together. Commit or push only with explicit user authorization.

## Updating goldens for an intentional behavior change

When an intentional damage or timing change breaks an existing meaningful golden, first prove the new gameplay behavior, then follow the scoped workflow above. Do not move snapshot ticks or regenerate unrelated values merely to make a test pass. If the failure only pins an incidental default or implementation detail, remove that assertion rather than preserving it.

## Reference material

- `docs/plans/server-testing-implementation.md` — the original design rationale for this harness (why golden over hand-asserted, what's excluded from snapshots and why).
- Existing roster `KitScenario` tests under `tests/Shared.Tests/` are the current examples. Nilus's implementation plan is historical; its runtime and tests have been removed.
