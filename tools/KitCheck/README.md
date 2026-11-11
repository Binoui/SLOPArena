# Kit checker

Requires the existing .NET 8 SDK. No Unity or extra NuGet package is needed.
The generator uses the C# parser shipped in that SDK.

Check a source folder from the repository root.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- docs/characters/examples/workshop-kit
```

Create the compiler's authoring-ready template.

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- template /tmp/my-kit local.my-kit 'My kit'
```

Update the generated section of the [reference](../../docs/characters/workshop-authoring-reference.md).

```sh
dotnet run --project tools/KitCheck -p:SkipUnityPluginCopy=true -- reference --write .
```

`reference .` prints that section to standard output. The generator reads codec syntax, compiler validation syntax,
compiler constants, and the bone registry. The Shared compiler remains the authority for admission.
