# Unity contributor setup

The .NET path in [CONTRIBUTING](../../CONTRIBUTING.md) works without the local
Unity asset installation. The complete Unity client still needs separately
provisioned inputs. This guide records the inspected setup on 2026-10-13; it does
not turn local asset copies into redistributable source.

## What owns each dependency

| Input | Current owner and source | Contributor action |
| --- | --- | --- |
| Unity Editor | `ProjectSettings/ProjectVersion.txt`, `6000.0.78f1` | Install this version with the build modules needed for your target |
| Ordinary UPM dependencies | `Packages/manifest.json` and `packages-lock.json` | Let Unity resolve the pinned registry/Git packages |
| Animancer | Ignored embedded `Packages/com.kybernetik.animancer`, inspected version `8.4.0` | Provision the entitled edition and its notices separately |
| Pipeline | Tracked local fork of `com.unity.pipeline` `0.5.0-exp.1` | Preserve its ownership protocol on the canonical Editor; see the [fork proposal](../plans/2026-10-13-unity-dependency-ownership.md) |
| Shared plugin | `src/Shared/SlopArena.Shared.csproj`, `CopyToUnity` | Generate it with this project closed, or under your own gateway lease |
| Managed Unity dependencies | Tracked, pinned `Assets/Plugins/NuGet` snapshot | Preserve its inventory, checksums, notices and importer metadata; see its [README](../../client/Unity/Assets/Plugins/NuGet/README.md) |
| Original fighter models, materials and textures | Files generated for this project under `Assets/Art/Characters/`, re-included one by one in `.gitignore` | Add them from a checkout with the local art; see [shipping original art](#shipping-original-art) |
| Clips, derived effects, audio and environment inputs | Local assets referenced by catalogs, scenes and Resources | Provision exact assets and metadata from an authorized source; consult the [input inventory](../assets/contributor-inputs.md) |
| Generated character catalogs and cook intermediates | Existing character cook services | Generate through the package workflow once its inputs are provisioned |

The Shared copy target exports only `SlopArena.Shared.dll` and its PDB. NuGet
owns the project third-party managed inputs; Pipeline retains its package inputs.
The [ownership record](../plans/2026-10-13-unity-dependency-ownership.md) describes
the approved consolidation and the separate, unresolved fork publication route.

Existing local installations may still have old third-party DLLs in the ignored
Shared plugin folder. A build intentionally does not prune that folder. The
preflight reports these files. Compare each candidate's SHA-256 with its pinned
NuGet counterpart before deleting a verified redundant copy; preserve foreign
files and metadata that have not been identified. For an open canonical Editor,
use your own gateway lease and Unity's asset/importer APIs, then verify compilation
and the actual providers. Filename similarity alone is insufficient.

## Provision and check

1. Install the Editor and entitled embedded/local inputs before opening the
   complete client. Keep original `.meta` GUIDs and import settings. Importing a
   similarly named pack does not recreate edited clips or adapted prefabs.
2. With this project closed, run `dotnet build src/Shared/ --nologo` from the
   repository root. An open canonical Editor requires your own bounded lease
   under [Editor coordination](unity-editor-coordination.md).
3. Run the read-only preflight:

   ```bash
   python3 tools/check_unity_setup.py
   python3 tools/check_unity_setup.py --json
   ```

   Use `--root /path/to/checkout` to inspect an explicit checkout. The check reads
   embedded package identities/versions, the recorded NuGet DLLs and their pinned
   hashes, the Shared DLL and absence of extra DLLs in its folder,
   and GUID references in the four `CharacterAssetCatalog.asset` files. It treats
   a `.meta` without its source asset as missing and reports ambiguous GUIDs.
   The JSON output includes resolved paths and whether the fork's ownership
   implementation is present. It does not install, import, copy, save, cook or
   connect to the Editor.
4. Open `client/Unity/` through Unity Hub and inspect compilation before opening
   the tracked Frontend scene, Training, or Ability Lab. Follow [testing](../testing.md)
   for the affected workflow; use [Unity tools](unity-tools.md) for Editor checks.

The preflight checks essential catalog inputs, not the whole player. Passing it
does not establish stage/audio/UI completeness, current generated catalogs,
compilation, gameplay, or publication rights. The current provisioned checkout
passed with 134 catalog GUID references. A fixture containing only tracked Unity
files correctly reported missing Animancer, Shared, and asset inputs.

`scripts/setup-worktree-unity-packages.sh` links an existing local installation;
it does not bootstrap a new contributor. The isolated release wrapper likewise
uses an already provisioned, entitled checkout. Neither is evidence that a stock
clone has all Unity inputs.

## Copying an existing private asset installation

For another development machine, [`unity_asset_bundle.py`](../../tools/unity_asset_bundle.py)
exports the installed inputs from a provisioned checkout:

```bash
python3 tools/unity_asset_bundle.py export artifacts/device-setup/SlopArena-local-assets.zip
```

The ZIP contains ignored files under `client/Unity/Assets/` and `Packages/`,
including Animancer, adapted clips, rigs, materials, audio, stage art and their
available `.meta` files. This follows the release wrapper's local-input boundary,
with additional exclusions for original installer archives, scratch files, preview
scenes, generated content, staged server payloads and import/build caches. Tracked
source stays in Git. Companion metadata that Git does not yet track is included;
tracked metadata and character catalogs are recorded as compatibility checks.

Copy the ZIP and the generated `unity_asset_bundle.py` beside it to the laptop.
Clone the repository there, keep its Unity project closed, and run from the clone
root (adjust the archive/script paths to where you copied them):

```bash
python3 /path/to/unity_asset_bundle.py import /path/to/SlopArena-local-assets.zip --dry-run
python3 /path/to/unity_asset_bundle.py import /path/to/SlopArena-local-assets.zip
dotnet build src/Shared/ --nologo
python3 tools/check_unity_setup.py
```

The helper needs Git and Python 3.9 or newer, with no additional Python packages.
It verifies every archived file before importing, preserves identical existing
files, and stops on conflicting local assets, changed tracked inputs or symlinked
destinations. It never replaces tracked source. The adjacent `.manifest.json`
lists every asset and its SHA-256; `.zip.sha256` verifies the transferred ZIP.
The manifest records the source commit. Matching a bundle's catalog and metadata
inputs does not require an identical branch tip, so later unrelated source changes
can still use the same bundle.

Keep these bundles private: they contain the locally installed purchased assets.
They transfer an existing installation; they do not install the Editor or .NET,
rebuild Shared, or establish that the complete client compiles and plays on the
new machine. Repeat the export after local assets change.

## Shipping original art

Original models, materials and textures can move from local art into Git once
everything they reference can be redistributed. Check an asset's dependencies
from a checkout that has the local art installed, so that every reference
resolves:

```bash
python3 tools/check_asset_closure.py client/Unity/Assets/Resources/Stages/log_off.prefab
python3 tools/check_asset_closure.py --json client/Unity/Assets/Art/Characters/wibou/Models/wibou.fbx
```

The check follows GUID references through scenes, prefabs, materials, other
text-serialized assets and `.meta` files. It reports each file as tracked,
to-add, ignored (a fresh clone would miss it), registry or builtin (Unity
provides it), or dangling (the reference is already broken). It only reads files
and Git's ignore rules; it does not import, change or commit anything. It does not
follow references stored in binary assets or shader `#include` paths.

`.gitignore` keeps `Assets/Art/` local but re-includes the original fighter files
one by one near the end of the file, because the same folders also hold animation
packs, stock characters and old candidates. To ship more art, for example a stage
once its vendor models are replaced:

1. Run the check on the stage prefab or folder until no vendor files remain among
   the ignored ones.
2. Add a `!` rule for each remaining ignored project file, ending in `*` so that
   its `.meta` file ships too.
3. Run the check again: every file should be tracked or to-add. Stage those files
   with `git add -- <paths>`; never use `git add -A` in a checkout with local art.

GitHub warns above 50 MB per file and rejects files over 100 MB. Re-export large
meshes or textures, or move art to Git LFS, before adding them.

## Evidence and changes

Keep pack/source versions, creator and licence evidence in
[asset notes](../assets/credits-and-licenses.md), and keep private receipts outside
Git. Package `license: MIT` fields describe authored package metadata/gameplay;
they do not license imported art. The input inventory separates known sources
from unresolved provenance.

Managed dependency consolidation is implemented. A [public reference kit](../plans/2026-10-13-public-reference-kit.md)
for Workshop creators is deferred: contributors should author/cook characters,
preview them in Ability Lab and Training, and publish packages without building
the complete game. The maintainer may remain its only builder. The proposed
creator setup can use free Editor dependencies and a maintainer-built installed
game for distributable-package preview; its exact provisioning/import and package
loading path still needs proof. This guide and checker currently describe the
full provisioned development client, not that future ModKit.

Moving the Pipeline fork out of the tracked tree also remains proposed. Review
the concrete scope before changing the canonical runtime or source layout.
