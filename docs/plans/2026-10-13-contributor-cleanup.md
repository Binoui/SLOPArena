# Contributor cleanup — 2026-10-13

The goal is to make ordinary contributions reproducible while preserving the
four-character friends demo. The October 11 audit identified local setup
assumptions, stale public entry points, and unclear ownership of tools and assets.
This plan uses current main rather than treating that audit as a permanent list
of defects.

## First batch

The implementation is prepared and verified in the working tree. Commit and
push require separate authorization.

- Document the usable .NET-only path and the full Unity prerequisites. Generate
  the Shared plugin before opening a freshly provisioned Unity project.
- Supply one public root solution for Shared, Server, tests, and maintained C#
  tools. CI and optional IDE setup use it and skip Unity plugin copying.
- Keep test and CI source visible to repository search.
- Update issue forms and add a short PR template for behavior, verification,
  and content provenance.
- Repair BonkInvestigation by removing its retired grounded Dash probe. Its
  grounded recovery measurements exercise movement and jump separately.
- Remove the obsolete OMP-only navigation solution, empty `untitled.md`, and
  accidental `cd` error log. Preserve tracked art studies and useful reports.

Acceptance: the solution restores/builds/tests using the supported .NET 8 SDK;
all maintained tool projects compile; BonkInvestigation emits current contact and
move/jump recovery data; documentation checks pass; unrelated branding edits and
Unity project settings remain untouched.

Verification used a temporary checkout at `1e8f6e9` with only this batch overlaid
and the installed .NET SDK `8.0.131` selected. The solution built all 14 projects;
Shared passed 1,813 tests with two skipped, and Server passed 34 tests. The build
reported 26 warnings and no errors. BonkInvestigation produced its four report
files and eight move/jump recovery rows. Python documentation/adapter coverage
passed 16 tests; Markdown links and JSON/YAML configuration checks passed. These
results do not establish a clean Unity import, packaged gameplay, or Windows/macOS
host compatibility.

## Second batch: Unity tools and self-tests

- Preserve exact character source/catalog bytes, metadata, cooked packages and
  roster, generated catalogs, intermediate files, and the original recook queue
  around package self-tests. Restore loaded catalog bindings after exceptions.
- Check the preservation scope with a disposable package, an injected failure,
  extra output files, an existing queue, and an unsaved asset. Verify that package
  operations preserve an unrelated unsaved draft too.
- Save only the catalog being edited in package creation, binding, generated
  catalog persistence, and Ability Lab catalog edits/undo.
- Replace historical self-test assumptions with current authored bindings and a
  disposable foreign clip. Prepare required fresh cooks inside the scope and
  restore the contributor's starting outputs afterwards.
- Group menus under Authoring, Diagnostics, Tests, Prototypes, and Legacy; retain
  script paths and GUIDs. Keep the Ability Lab as the primary package UI. Validate
  animation folders before clearing the legacy clip list and record Undo.

Acceptance: compilation, disposable restoration, the four affected package
self-tests, and native menu registration pass. Tests preserve saved package
inputs/outputs, unsaved drafts, and the original queue. See the maintained
[Unity tools guide](../contributing/unity-tools.md) for their requirements.

Verification on Unity `6000.0.78f1` passed compilation, the disposable restoration
regression, and all four affected package self-tests. Authoring was rerun after
repairing its old fixture assumptions. Each check preserved the 59 files in its
package probe and the two existing unsaved project-settings drafts. An independent
71-file inventory found no changed, missing, or extra package input/output files.
Native menu reflection confirmed 22 current paths and absence of 16 old paths.
Later Editor commands found no pending cook and a clean, stopped Frontend scene;
the Console had zero errors and the gateway released settled and free. These are
Editor checks, not clean-clone import or packaged gameplay verification.

## Third batch: dependency and asset evidence

- Inspected disk hashes, actual Unity compiler references, loaded assembly
  providers, embedded packages and asset dependency graphs without recompiling
  or changing the canonical setup. The 22 overlapping managed DLLs are identical;
  current compiler references have one provider per assembly name.
- Removed six stale MCP/R3/Roslyn records from the NuGet installation marker.
  No DLL or importer changed.
- Added an [offline setup check](../../tools/check_unity_setup.py), eight focused
  tests and a [setup guide](../contributing/unity-setup.md). It passed against the
  provisioned checkout and correctly failed for a fixture containing only tracked
  Unity files, reporting 95 missing-input problems.
- Recorded the [asset input inventory](../assets/contributor-inputs.md), including
  184 dependencies absent from Git across the four character catalogs, exact-GUID
  provisioning, known source families, and unresolved provenance. Preserve the
  ten editable Blender studies until their canonical sources are selected.
- Retrieved the exact public Pipeline base for inspection and reconstructed all
  611 current fork files byte for byte in ignored scratch artifacts. This verifies
  the technical route, not source-publication rights. No package was installed.

## Fourth batch: approved managed dependency ownership

M1 is implemented in the working tree. Shared exports only its DLL/PDB and no
longer carries SignalR for Unity. The tracked NuGet snapshot owns project managed
dependencies with version inventory, 36 pinned hashes and upstream notices.
The canonical migration removed 22 verified identical copies and corrected nine
Editor importer providers under the caller's gateway lease, preserving GUIDs,
unrelated drafts and all third-party DLL versions/bytes.

The .NET build/tests passed. An isolated export produced only Shared DLL/PDB;
Unity compilation, Windows/Linux reference selection, offline lobby connection
construction and the fixture restoration regression passed. Full cold Unity
import and packaged player gameplay remain separate checks; no release was run.
The offline setup checker now detects checksum drift, unrecorded dependencies
and leftover Shared-folder copies, with 12 focused tests.

## Next: the fork route and public reference setup

The [concrete proposal](2026-10-13-unity-dependency-ownership.md) recommends the
tracked NuGet snapshot as the managed Unity dependency owner for the demo, and a
locally provisioned Pipeline base plus the project delta if its publication rights
are confirmed. It includes alternatives, licence boundaries and acceptance checks.
M1 does not resolve the fork's publication rights or remove private asset inputs.
The fork route remains proposed. The
[Workshop creator/reference target](2026-10-13-public-reference-kit.md) is Editor
first and deferred: contributors author, cook, preview in Ability Lab/Training
and publish packages; the maintainer builds the game. No Pipeline package or
presentation architecture was replaced.

Each change uses applicable verification from [testing](../testing.md), with its
own bounded [Editor lease](../contributing/unity-editor-coordination.md). A menu
click or compilation alone is not evidence of fixture restoration or gameplay.

## Unity setup and future creator scope

The maintainer selected Editor-first creator support with the existing
presentation code and deferred its implementation. A public ModKit with an
installed-game package preview follows ADR-0027; contributors do not need to
export a free standalone game. No new presentation architecture, assembly
boundary, animation replacement or canonical contributor profile was implemented.

| Option | Result | Cost and limitation |
| --- | --- | --- |
| A: document and validate the existing licensed setup first | .NET contributions work now; contributors with the exact Unity inputs can use the existing game and tools | Does not make a stock clone playable; prerequisite checks and provenance need completion |
| B: schedule a small Workshop creator reference loop | Creators author/cook a package, preview in Ability Lab and load it in prebuilt-game Training | Requires dependency/asset proof and installed-game package loading; no creator-built executable |

Continue **A while completing the friends demo**; B is deferred until scheduled.
Its first milestone is the local creator loop, followed by package publishing
preparation. Direct Steam upload versus backend staging remains an implementation
decision; Steam is the accepted final Workshop distribution target.
A public example should reuse the game implementation and
existing authoring pipeline; it must not become a second simulation, a fallback
for invalid cooked content, or a fifth roster requirement.

Any dependency or reference-example change needs a bounded proposal with the
selected assets/package ownership, effect on current imports, and validation for
both the contributor path and the existing four-character demo.
