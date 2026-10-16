# Project tools

The root [SlopArena.sln](../SlopArena.sln) is the maintained .NET build set:
Shared, Server, their tests, and the C# tools below. CI builds all of them. Tools
reuse Shared and the admitted cooked packages; editing raw package source alone
does not change a report's runtime input.

```bash
dotnet build SlopArena.sln --nologo -p:SkipUnityPluginCopy=true
dotnet test SlopArena.sln --no-build --nologo -p:SkipUnityPluginCopy=true
```

To run one tool without copying the Shared plugin into Unity:

```bash
dotnet run --project tools/BonkInvestigation -p:SkipUnityPluginCopy=true -- <output-directory>
```

Use each tool's argument parser or documented usage before running it. Supply an
ignored output directory for reports. Baking tools write content and need a
deliberately selected destination; build success does not authorize overwriting
current arena data. A tool's successful run is diagnostic evidence, not acceptance
of a fighter or a networked match.

| Project | Purpose |
| --- | --- |
| `BakeArenas.csproj` | Offline arena generation; select the output directory explicitly |
| `AssetCatalog/` | Query the offline environment catalog and prepare asset worksets |
| `MoveDataReport/` | Authored timings, simulated trajectories and contact coverage |
| `MovementReport/` | Ground/air movement measurements |
| `AerialProfileReport/` | Fighter aerial movement and attack profiles |
| `AerialApproachReport/` | Aerial approach, landing and slide-flow diagnostics |
| `AbDiffReport/` | Compare baseline and counterfactual simulation/tuning outcomes |
| `SelfPlayReport/` | Deterministic bot-match diagnostics |
| `BonkInvestigation/` | Sword contact, pose-phase and grounded move/jump recovery diagnostics |
| `SpectateView/` | Record and inspect deterministic matches |

[KnockbackVisuals](KnockbackVisuals/README.md) supplies a browser viewer for saved
trajectory comparisons. `skill-evals/` is separate TypeScript session-analysis
tooling; see its [README](skill-evals/README.md). Python documentation and adapter
checks are documented in [quality checks](../docs/contributing/quality.md).

[`check_unity_setup.py`](check_unity_setup.py) inspects installed embedded
packages, pinned managed DLLs and folder ownership, the Shared plugin, and four character catalogs'
GUID inputs without operating Unity. Run `python3 tools/check_unity_setup.py`,
optionally with `--root /path/to/checkout --json`. See
[Unity setup](../docs/contributing/unity-setup.md) for scope and provisioning.

[`unity_asset_bundle.py`](unity_asset_bundle.py) exports ignored Unity inputs and
their metadata to a private ZIP for another development machine. Its importer
verifies checksums and tracked metadata, and preserves conflicting laptop files.
See the [private installation transfer](../docs/contributing/unity-setup.md#copying-an-existing-private-asset-installation)
for commands; Git and Python 3.9 or newer are sufficient.

The historical `research/flight-sweep/` experiment has been removed; restore it
with `git checkout d98c6c26 -- research/flight-sweep` if that research becomes
relevant. Generated visual reports and archived investigations remain evidence
for their source snapshot.

Add a new maintained C# tool to `SlopArena.sln` and this inventory so CI compiles
it. Document its inputs and output directory. Keep exploratory projects outside
that build set until they have a maintained purpose.

Unity editor tools live under `client/Unity/Assets/Scripts/Editor/` and
`client/Unity/Assets/AbilityLab/Editor/`. Their supported authoring and verification
paths are described in [Unity CLI](../docs/contributing/unity-cli.md),
[Ability Lab](../docs/systems/ability-lab.md), and
[character authoring](../docs/characters/adding-a-new-character.md).
See [Unity tools](../docs/contributing/unity-tools.md) for menu groups and package
test input/output preservation.

Original procedural environment art lives under [blender/](blender/README.md).
The retained FightGuy Sanctuary generator includes reusable mesh construction,
rock/scenery generation, ambient-occlusion baking, Unity FBX export and camera
preview techniques. Its reuse guide identifies stage-specific assumptions; these
Python art tools are separate from the .NET solution above.
