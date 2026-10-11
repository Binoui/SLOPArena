---
name: sim-test
description: "Run requested SlopArena Shared.Tests with an optional filter, without copying a plugin into Unity."
metadata: {"invocation": "explicit"}
---

# Simulation tests

Run only when the user requests this workflow. Choose the applicable verification
mode in [docs/testing.md](../../docs/testing.md). `/sim-test` runs the Shared suite;
`/sim-test <filter>` runs the requested class/method substring.

Validate the selected checkout with [execution-root rules](../orient/SKILL.md#execution-root)
and set command `cwd` there. A focused run does not need full session orientation.

```bash
# Full suite; project references build the tested Shared code.
dotnet test tests/Shared.Tests/ --nologo -v q -p:SkipUnityPluginCopy=true

# Replace FILTER with the requested substring.
dotnet test tests/Shared.Tests/ --nologo -v q -p:SkipUnityPluginCopy=true --filter "FullyQualifiedName~FILTER"
```

Retain output from the original run. Report executed test count and failures.
A filter matching zero tests may exit zero: report "filter matched nothing; no
behavior verified", then resolve a requested class/name before treating it as proof.
For failures, inspect assertions and relevant source instead of rerunning solely
to recover truncated output or regenerating expectations.

These runs intentionally do not refresh Unity's plugin. If the task also requires
an Editor refresh, follow [sloparena-build](../sloparena-build/SKILL.md) under your
own bounded lease; a Shared test pass does not establish Unity compilation.

Current examples under `tests/Shared.Tests/`: `CombatMathTests.cs`,
`CombatPipelineTests.cs`, `ServerSimulationTests.cs`, `MankiKitTests.cs`,
`MankiKitScenarioTests.cs`, `SpellResolverTests.cs`, `ChargedDirectionalDashTests.cs`
and `PhysicsTests.cs`. Inspect the current tree rather than rely on historical counts.
