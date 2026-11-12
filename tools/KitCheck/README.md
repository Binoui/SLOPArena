# Kit checker

Build and publish with the repository's .NET SDK. No Unity, source parser, or additional NuGet package is needed.
Run the published output anywhere with the .NET 8 runtime. Keep its publish-directory files together.
`template` validates the package ID before writing and refuses to overwrite either source file.
`moves <folder>` prints per-contact estimates by calling Shared simulation functions. Captures are labelled as holds, and dynamic capability outcomes need play verification.

```sh
dotnet publish tools/KitCheck -p:SkipUnityPluginCopy=true -c Release -o /tmp/kitcheck
cd /tmp
dotnet kitcheck/KitCheck.dll kitcheck/example-kit
dotnet kitcheck/KitCheck.dll kitcheck/example-grappler
```

The publish directory includes `reference.md`, `example-kit`, and `example-grappler`.
To check repository source without publishing, run this from the repository root.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- docs/characters/examples/workshop-kit
```

Create a compiler template with the published checker.

```sh
dotnet kitcheck/KitCheck.dll template my-kit local.my-kit 'My kit'
```

The generator reads the public, immutable `CharacterSourceSchema` API in Shared.
Its embedded `CharacterSourceSchema.xml` defines field names, types, requiredness, defaults, units, descriptions,
source-only scalar ranges, enum tokens, child schemas, diagnostic identities, capabilities, and budget costs.
Runtime/cooked-loader scalar limits live in `CharacterRuntimeLimits` and named code constants. Capability versions and snapshot limits stay in code. Tests require the schema to agree.
Use `HasWritableDefault` to distinguish an insertable JSON default from an omission value, and `IsAllowed` for conditional fields. `ContextRanges` describes bounds specific to a containing capability.
The source reader and compiler use those definitions. Existing coupled geometry and lifecycle checks remain compiler-owned;
the reference describes those exceptions by hand. `dotnet build` fails when a schema field has no description.

Update the generated section of the [reference](../../docs/characters/workshop-authoring-reference.md) with either form.
The write form needs a repository root because it updates that repository's document. It does not read C# source.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- reference --write .
dotnet kitcheck/KitCheck.dll reference --write /path/to/SlopArena
```

`reference` prints the generated contract without needing a repository.
