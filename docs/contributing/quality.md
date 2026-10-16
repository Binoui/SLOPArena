# Code Quality and Verification

Use these commands from the repository root. There is no repository Makefile and
no project Git hook. Do not rely on `make build`, `make check`, `make format`,
`make clean`, or `make test`.

## Applicable checks

The CI workflow runs documentation checks, builds every maintained .NET project
in the root solution (including the C# tools), and runs Shared/Server tests on
push to `main` and on pull requests. These are the available project
verification commands:

```bash
python3 -m unittest discover -s tools -p 'test_*.py'
python3 tools/check_docs.py
python3 tools/sync_agent_skills.py --check
node --test --test-concurrency=1 scripts/unity-editor-gateway.test.ts tools/skill-evals/src/import.test.ts
dotnet build SlopArena.sln --nologo -p:SkipUnityPluginCopy=true
dotnet test SlopArena.sln --no-build --nologo -p:SkipUnityPluginCopy=true
dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true
dotnet build src/Server/ --nologo -p:SkipUnityPluginCopy=true
dotnet test tests/Server.Tests/ --nologo -p:SkipUnityPluginCopy=true
```

Choose checks for the changed surface rather than treating unrelated layers as
required. See [Testing and Verification](../testing.md) for local iteration,
integrated changes, package workflows, and distributable-demo verification.
Use the root solution for repository verification or choose one affected project
for local iteration. See the [tool inventory](../../tools/README.md) for supported
tool purposes and output locations. The full offline build/test path is shared
by CI and optional IDE setup.
Shared builds run `CopyToUnity`, copying only the Shared DLL and its PDB
into `client/Unity/Assets/Plugins/SlopArena.Shared/` unless `SkipUnityPluginCopy`
is explicitly set. The pinned [NuGet snapshot](../../client/Unity/Assets/Plugins/NuGet/README.md)
owns Unity's project third-party managed DLLs; Shared does not copy that closure.
For an open Editor, coordinate affected saved-source writers, acquire
your own bounded gateway hold before that build, and let imports/compilation
settle before ending the hold. Never copy into another owner's Editor window.

The documentation checker validates Markdown links, current workflow vocabulary,
portable paths and discovery adapter freshness, plus simple executable targets in
this guide. It does not interpret shell programs or prove command success. CI runs
the documentation/adapter unit tests and a separate Node gateway job. The gateway
suite uses mock Pipeline/CLI processes; it never opens Unity or needs Orca. Node
22.18+ runs the TypeScript directly; see [Node's type stripping documentation](https://nodejs.org/api/typescript.html#type-stripping).
For live Editor or plugin refresh checks, use [Unity CLI](unity-cli.md) and the
[coordination protocol](unity-editor-coordination.md).

## EditorConfig and analyzers

`.editorconfig` configures indentation, whitespace, C# style and selected
diagnostic severities. It does not enforce rules by itself in every editor.
Analyzer severities are configured there (including selected CA, Roslynator, and
IDE diagnostics); they are not a generic promise that unused variables,
performance problems, or code smells are all detected or block commits. Build
and test results from the applicable .NET projects are the available automated
compile/test gates. Update `.editorconfig` when changing analyzer configuration.

## Suppressions

Use analyzer suppressions only when justified and scoped to the specific
diagnostic. `.editorconfig` can set a diagnostic's severity, and C# supports
`#pragma warning disable` / `restore`; these alter diagnostics, not CI workflow
or repository-wide quality checks.
