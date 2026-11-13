# Portable cook byte comparison

The proof compiles the included source with `CompilePortable`, assembles the same package with
its portable bindings and poses, and requires byte equality for runtime JSON and the complete
manifest. The manifest comparison covers the payload, cooked, and package hashes.

Generate the synthetic Tumbleweed item and build the runner from the repository root.

```sh
SLOP_PORTABLE_PROOF=/tmp/portable-item dotnet test tests/Shared.Tests/Shared.Tests.csproj \
  -p:SkipUnityPluginCopy=true --filter FullyQualifiedName~TumbleweedAdmittedEndToEnd
dotnet build tools/PortableCookProof -p:SkipUnityPluginCopy=true
```

The runner targets netstandard2.1 so it can run inside Unity's Mono player as well as CoreCLR.
For CoreCLR, place this runtime configuration beside `PortableCookProof.dll` as
`PortableCookProof.runtimeconfig.json` and invoke `dotnet PortableCookProof.dll /tmp/portable-item`.

```json
{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}
```

The build-only branch used for unit 4b compiles this runner into a temporary Unity script and
calls it before scene load with `--portable-cook-proof <item-folder> <result-file>`. That script
and the builder's origin/main pin skip stay on the throwaway branch. They are not product paths.
A failed comparison writes an exception and exits with status 1. Success writes `PASS` and
exits with status 0. The feature branch has no launch option or client caller.

Running the Editor's standalone `MonoBleedingEdge/bin/mono` directly is not equivalent to the
Unity player. On this host it fails resolving `JsonDocument.Parse(ReadOnlyMemory<byte>, ...)`
before it can compare bytes. Use the player to test the actual managed assembly setup.
