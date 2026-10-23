---
name: sloparena-kit-regression-testing
description: "Use for explicit kit regression coverage, KitScenario maintenance, and focused combat regression tests. Not for routine numerical move tuning, adding content alone, or Unity-only presentation changes; use docs/testing.md for those verification modes."
---

# SLOPArena Kit Regression Testing

`tests/Shared.Tests/` has deterministic scenarios (`KitScenario` and `ScenarioRunner`) for the real `ServerSimulation.Tick()`. A scenario supplies sparse per-tick inputs, initial state, an optional arena and default-input NPC, and focused assertions. `KitScenarioTests.AssertScenario` checks the player's final state. Use a local simulation loop when the contract concerns intermediate projectiles, contacts, launches, or recovery boundaries.

Coverage is explicit: adding a character or ability does not create a scenario. Extend the relevant existing test file; do not assume a class naming pattern or historical coverage count.

## Choose coverage before writing tests

- **Numerical tuning:** use the affected Ability Lab/Training path and existing move-data reports. Do not add or regenerate goldens just because a number changed. Follow [Testing and verification](../../docs/testing.md) for local versus accepted content.
- **Mechanic changes:** prefer focused assertions for observable state transitions, contacts, timing boundaries, interruption, precedence, or errors. Add coverage only where a plausible consumer-visible regression would otherwise escape.
- **Coverage cleanup:** remove checks that duplicate current character values or test-local copies of production logic. Replace broad state snapshots with the specific behavior they were intended to protect. Keep compiler mapping tests on explicit fixtures and real-package integration checks on the relevant roster entry.
- **Unity presentation:** use the affected runtime surface and visual evidence, not a Shared golden as a substitute.

Do not pin incidental implementation details or defaults. Remove such assertions instead of re-pinning them; retain or add coverage for the actual gameplay contract where needed.

## The pieces

| File | Role |
|---|---|
| `KitScenario.cs` | Scenario setup, sparse `InputSequence.Press`/`Set`, snapshot/final states, optional NPC and arena. Unspecified ticks and NPC inputs are default input; hold buttons explicitly across required windows. |
| `KitScenarioTests.cs` | `AssertScenario` calls the player's final-state assertion; `ScenarioRunner` calls `NpcAssert` when provided. |
| `TestHelpers.cs` | Existing definitions, initial states, ground-height helpers, baked data, and test arenas. Choose fixtures appropriate to the changed contract rather than incidental roster tuning. |

## Writing a scenario

Start with the nearest existing behavioral test, such as `MankiKitTests.cs`, `BonkKitTests.cs`, or `MankiKitScenarioTests.cs`. Keep setup isolated and inputs explicit.

Use `AssertScenario` for final-state contracts, `NpcAssert` for victim outcomes, or inspect `ScenarioRunner.Run()` snapshot results for a specific timing boundary. Assert the relevant result directly; do not substitute a whole-state golden for one uncertain mechanic.

For a snapshot observation, choose `SnapshotTick` at the boundary being protected and inspect the relevant returned fields. A final idle state alone does not prove that a projectile fired, a hit connected, or the victim launched. Count accepted contacts for sweet/sour or multi-hit behavior rather than summing mutually exclusive authored windows.

Scenarios cover their selected states and inputs. They do not prove visual contact, arbitrary match interactions, human gamefeel, or networking.

## Verification

1. Run `dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true --filter "FullyQualifiedName~<ScenarioTestClass>" --nologo`. Confirm the intended cases execute.
2. Inspect failures against the contract. Keep a meaningful newly failing assertion and diagnose it; do not replace it with a current value or unrelated final-state check.
3. For a package integration scenario, observe the compiled or admitted package through the actual simulation. Reading expected damage from its definition and checking damage applied by the simulation verifies two separate stages.
4. At delivery, run the applicable build, full-suite, and runtime checks from [Testing and verification](../../docs/testing.md). Do not build or cook for a skill-only or documentation-only edit.
5. Commit or push only with explicit user authorization.

## Reference material

- `docs/testing.md` documents the current verification modes and fixture boundaries.
- Existing roster `KitScenario` tests under `tests/Shared.Tests/` are the current examples. Nilus's implementation plan is historical; its runtime and tests have been removed.
