# Online map compatibility fixture

Captured by running the unmodified origin/main Server provider and Shared codec from a
temporary console project from commit `4b769871ef401ffade01918b78ce37eef95c3dd7` (PR #352 merged). The producer loads the complete roster through
`MatchContentCatalogProvider.TryBuild`, loads `slop_pit` from the actual arena bytes, binds it
with `WithArena`, and serializes schema 2. Files contain exact UTF-8 JSON and SHA-256 without
trailing newlines. The source revision is recorded in the fix-pass report.

`PackageContentAdmissionTests.OnlineGoldenUsesProviderArenaBindingAndOrchestratorHash` checks
the provider output and a real orchestrator assignment against both files. Regeneration
requires a fresh baseline capture from origin/main, never the feature's writer.
