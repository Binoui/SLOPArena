# Unity dependency ownership — 2026-10-13

**Status: M1 approved and implemented in the working tree; fork options remain
proposed.** The maintainer accepted M1 following the dependency audit. No Pipeline
package migration, assembly-boundary change or public content profile was approved.
The root [project policy](../../AGENTS.md) requires approval for those architecture
changes. The current four-character demo remains the acceptance target. Commit
and push require separate authorization.

## Evidence

Before consolidation, `Assets/Plugins/NuGet` contained 36 tracked DLLs and
`CopyToUnity` exported Shared and its restored closure into an ignored second
folder containing 23 DLLs. Their 22
overlapping files have identical SHA-256 hashes. Live `Assembly-CSharp` references
select 20 files from NuGet, ten from the Shared folder, and four from Pipeline's
Roslyn area, with no repeated assembly name among those references. Loaded
assemblies also have one location per name. For example, System.Text.Json and
SignalR.Client.Core load from NuGet, while some SignalR/logging transitives load
from Shared. There is no observed duplicate-type failure to justify indiscriminate
deletion.

The tracked NuGet installation marker still claimed six removed MCP/R3/Roslyn
packages. The audit removed only those stale records; it changed no DLL or
importer. Pipeline supplies its own current Roslyn/Immutable/Metadata inputs.

The Shared project also carried SignalR solely to export the Unity lobby client's
closure. That mixes a Unity dependency-distribution concern into the deterministic
library, and a build copies obsolete DLLs without removing them. Importer metadata
and the actual compiler provider must be part of any consolidation.

The [asset inventory](../assets/contributor-inputs.md) records 184 local-only
dependencies in the union of the four character catalogs. Licensed Animancer is
an additional ignored embedded package. DLL consolidation alone will not make
the client playable from a stock clone.

## Pipeline fork and upstream licence

The owner confirms that the embedded Pipeline package is a local fork for
concurrent-agent access. The unmodified base is obtainable from Unity's public
package registry as `com.unity.pipeline` `0.5.0-exp.1`. Inspection downloaded the
official archive into ignored audit artifacts; it was not installed into Unity.

- Archive: [Unity's pinned package](https://download.packages.unity.com/com.unity.pipeline/-/com.unity.pipeline-0.5.0-exp.1.tgz).
- Registry SHA-1/fingerprint: `24975d51528a67809794e52185267762a9561e15`.
- Inspected SHA-256: `708757f2521d3549406aa1694eec23ad6215095a5bd6993484b03366e9974b46`.
- Upstream revision recorded by the fork: `17df0ac8d6533a8c22830c89dc85a24c01f30ade`.

Comparison found 16 modified files, two added source/test files and their metadata,
and the removed upstream signature/attestation files. The changes cover ownership
enforcement, command dispatch, asynchronous compile/test settlement, startup/reload
handling, and package-manager recovery. Replacing the fork with the official
archive would lose those changes. Modified package content must not be presented
as the unmodified signed upstream package.

An isolated reconstruction applied the local delta to the verified archive and
matched all 611 current fork files byte for byte. This establishes technical
reproducibility, not permission to distribute the delta. The comparison patch is
kept in ignored local artifacts because it includes upstream context.

The embedded `LICENSE.md` points to the Unity Package Distribution License.
Its distribution grant covers binaries integrated with project content; the
inspected terms do not establish a separate upstream-source publication grant.
The owner has not identified such a grant. Our additions need their own provenance
and licence record; changing the whole fork to MIT would not resolve the upstream
question. [Unity's licence](https://unity.com/legal/licenses/unity-package-distribution-license)

### Fork options

| Option | Result | Preconditions and cost |
| --- | --- | --- |
| F1: retain the versioned fork with explicit source-publication permission | Preserves current setup and review history | Obtain and record permission covering upstream source, modifications and bundled notices; no such evidence is recorded yet |
| F2: provision the pinned upstream base locally and apply the project delta | Keeps upstream acquisition with Unity and makes the fork reproducible without storing the whole vendor tree in future source | Confirm publication rights for original additions and any patch context; define a deterministic bootstrap, hashes, metadata and licence notices; migrate only after cold-import and ownership tests pass |
| F3: use stock Pipeline or build a separate coordination integration | Reduces or eliminates the maintained fork | Stock Pipeline loses the current lease enforcement. A separate integration needs a verified supported interception seam and its own design; it is a larger change before the demo |

Recommend **F2 as the technical target**, conditional on rights for the distributed
delta. Start by separating our original contributions and confirming the permitted
distribution route. If patch publication is not covered, use an authorized private
provisioning route while pursuing upstream support or F1; do not treat a patch as
an automatic licence exemption. The existing live fork stays in place throughout
validation. Repository-history handling is a separate maintainer decision, never
an automatic purge or rewrite.

## Managed dependency options

| Option | Ownership | Cost |
| --- | --- | --- |
| M1: retain the tracked NuGet snapshot as Unity's project dependency owner | NuGet owns the required managed third-party inputs; Shared copies only its DLL/PDB; Pipeline owns its own package inputs | Smallest migration. Pin a reviewable inventory/checksums and notices, repair importer selection, and remove only verified redundant Shared-folder files |
| M2: give a dedicated Unity restore project the managed dependency closure | Shared owns its direct pure-library dependencies; a separate restore/sync command owns Unity lobby and support packages | Adds a restore target, lock/input inventory, importer templates, licence-notice handling and bootstrap maintenance |
| M3: keep the two providers and document the current split | Current builds/imports continue | Does not resolve ownership, stale-copy cleanup or upgrade coordination |

Recommend **M1 for the demo**. Use actual compiler/target references to choose
which providers change. Remove the unused SignalR carrier from Shared only as part
of the same approved migration. Preserve Unity-provided BCL exclusions, the Shared
plugin's identity, existing runtime package pins, and Pipeline's Roslyn ownership.
Do not update package versions or invent a new gameplay execution path.

## M1 implementation and verification

`CopyToUnity` now exports only the Shared DLL/PDB. The unused SignalR carrier was
removed from Shared; its direct JSON dependency remains at `8.0.5`. The NuGet
snapshot retains all 36 DLLs at their existing versions and bytes, with matching
inventory, SHA-256 pins and upstream licence/notices. A preflight detects changes
or extra DLLs. Pipeline's five package DLLs remain unchanged.

Under the caller's bounded gateway lease, the migration backed up the ignored
Shared folder and affected metadata, checked both copies against the audit hashes,
removed exactly 22 redundant DLL assets and enabled the nine previously excluded
NuGet Editor providers. Existing importer GUIDs and unrelated drafts were retained;
no unknown local file was pruned. Shared's newly built plugin was intentionally
copied under the same lease.

- The .NET 8 solution built with `SkipUnityPluginCopy=true`: 26 warnings, zero
  errors. Shared passed 1,813 tests with two skipped; Server passed 34.
- An isolated source build with copying enabled produced exactly the Shared DLL
  and PDB, with no SignalR library in Shared's restored dependency graph.
- The live Editor compiled with zero console errors. Its project references
  select 29 NuGet DLLs, one Shared DLL and four Pipeline DLLs. Windows player
  references select 28, one and four respectively; the existing Editor-only
  difference was preserved.
- Unity's `AssemblyBuilder.defaultReferences` for Windows and Linux independently
  select 28 NuGet, one Shared and four Pipeline DLLs, with no duplicate assembly
  name. This inspected reference selection without starting a player build or
  switching the Editor's active target.
- The existing long-poll SignalR dependency path constructed and disposed an
  automatic-reconnect connection without starting network activity. This is not
  a remote login/join test.
- The package fixture restoration regression passed, preserving both existing
  project-settings drafts and the original cook queue. The gateway released
  settled and free.
- The Editor Development Content check passed against the new dependency
  providers, restoring all 71 package input/output files, both project-settings
  drafts and the original cook queue. Its temporary cook was not published.

The existing licensed Editor was verified; a full cold Unity import and packaged
Windows/Linux gameplay remain release/contributor-bootstrap checks. These have
not been claimed from reference inspection or the isolated .NET export fixture.

## Acceptance for the proposed fork migration and distributable setup

- Reconstruct the selected package/dependency inputs in an isolated checkout;
  verify pinned hashes, notices, original Unity metadata and the retained project
  delta. Keep contributor provisioning distinct from the already provisioned
  release-wrapper path.
- Build/test Shared and Server with `SkipUnityPluginCopy=true`; confirm pure-library
  references and then intentionally sync only the approved Unity outputs.
- Verify cold import/compilation and one provider per managed assembly for Editor
  and the Windows/Linux player targets. Inspect actual references rather than
  treating importer flags or filenames as sufficient evidence.
- Run focused package/restoration checks and the Pipeline ownership, settlement,
  reload/contention and package-manager recovery coverage. The existing gateway
  must still reject unowned commands and release only after actual settlement.
- Exercise the affected Frontend/lobby dependency path and produce the existing
  isolated demo build when the release scope is approved. Preserve unrelated
  source changes, asset drafts, selected checkouts and immutable cooked content.

Fork approval should name **F2**, or an alternative above, with the rights and
provisioning route selected. M1 approval does not authorize public fork publication,
a contributor content profile, commit, push or history rewriting.
