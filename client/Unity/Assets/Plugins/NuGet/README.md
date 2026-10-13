# Managed Unity dependency snapshot

This folder owns the project's third-party managed Unity inputs. Shared copies
only `SlopArena.Shared.dll` and its PDB into its separate plugin folder. Pipeline
owns the Roslyn inputs within its embedded package. Preserve the existing Unity
importer metadata: it resolves Unity-provided BCL exclusions and the package's
override selection. Enabling every DLL for every context changes that policy.

`.nuget-installed.json` records package IDs, versions and DLL filenames. It is an
inventory of the existing snapshot, not a NuGet restore project or a promise of
automatic installation. `CHECKSUMS` pins the SHA-256 of all 36 DLLs. The inventory
retains a historical analyzer record with no exported DLL; this is not an active
Unity analyzer installation.

Run `python3 tools/check_unity_setup.py` from the repository root to detect missing,
changed, unrecorded or duplicated managed inputs alongside the asset checks.
Compiler and player reference selection still require Unity verification. See
the [setup guide](../../../../../docs/contributing/unity-setup.md).

`LICENSES.txt` preserves the MIT licence and included copyright notices of the
exact NuGet packages represented here. `THIRD-PARTY-NOTICES.txt` preserves their
bundled notices, grouped where identical. Package-specific notices were inspected
from the same pinned versions, including older packages with licence text rather
than a modern SPDX field. Keep these notices with permitted redistributed copies.

For an upgrade, inspect the complete dependency closure and upstream licence
files, update the inventory/checksums/notices together, preserve importer GUIDs,
and verify actual Editor/player providers plus the lobby path. Do not run an
unrelated installer over this folder or use a Shared build to replace its DLLs.
