# Release import timing and cook investigation

This is local tooling verification on 2026-10-14, not a Steam/VPS release or
player acceptance. Base source was `9031deb23bf02dc4ec260acfa4423a103c65a9d7`,
Unity `6000.0.78f1`, Win64 Mono on the configured Linux workstation. Changed
tooling was explicitly overlaid into disposable projects for native validation.
Private logs, fixture scripts and reports are retained under ignored
`build/release-import-validation/`.

## Cook discrepancy

The canonical Editor already targeted `StandaloneWindows64`/`Player`. Its shared
`tumble_drift.fbx` artifact fingerprint was `3e238c720f90b3abba6c891db9d6af60`;
the cold and cached isolated imports reported `3a35bf0e4ea3942310663558dbc4a359`.
Source bytes, importer metadata bytes, serialized importer settings and imported
material settings matched. A bounded native reimport of that one canonical
asset changed neither source nor metadata and retained the canonical fingerprint.
Recorded FBX artifact dependency values also matched between contexts.

This refutes a simple target mismatch and a fix that merely reimports that file.
The remaining difference is in Unity's imported artifact identity; the root
cause is recorded below. Keep the cook/hash contract and native verification.
The diagnostic reimport also demonstrated that the canonical dependency queue
automatically recooks **all four** packages and can overwrite accepted cold
Windows metadata with warm Editor fingerprints. Its nine metadata-only rewrites
were restored; unrelated source changes were preserved. Isolated batch imports
now skip that automatic queue, and the wrapper refuses any implicit change to
committed cooked content. Normal authoring auto-cook behavior remains unchanged.
Cold certification separates acceptance of committed Windows cooks from a warm
Editor status. Authoring provenance now exposes the target/toolchain, while
certification retains the source, licensed fingerprint and native report.

### Root cause (2026-10-16)

The canonical artifact's dependency list included
`SourceAsset/GuidOfPathLocation` for `Assets/Art/Characters/fightguy/Textures/node_0_BaseColor.jpg`.
The FBX imported materials through its material description, and Unity resolved
the embedded `node_0_basecolor.jpg` reference by name to that unrelated local
FightGuy texture, which isolated release projects do not contain. The local
`tumble_drift.fbx.meta` now uses **Import Materials: None**, since the clip is
animation-only. Its dependencies are its own source and meta hashes, importer and
postprocessor versions and the pipeline default material, and the committed
cooks carry that fingerprint (`be7cdd0877fdefe54fcd97aafd859c68`).

## Cache trial

A full cold native player build and roster probe took **484.7 seconds**. Reuse
of a checked Library snapshot in another project passed native package/catalog/
player checks in **80.4 seconds**. An actual runtime code-edit build took
**75.2 seconds** and its new marker was found in the packaged assembly.
Cold and cached roster source/package identities and payload hashes matched.
Both independent projects ran native builds; no public artifact was uploaded.
The final snapshot implementation, excluding Editor connection/lease and advisory
cook data, passed a full native player build in **77.0 seconds**. Its snapshot
creation, byte checking and restore took **41.8 seconds**; this was measured with
an already prepared destination, so cloning/licensed staging is additional.

The initial trial's clone, snapshot creation, byte checking, restore and setup
took **68.3 seconds** in total; this is not a measurement of restore alone.
The first cached compile exposed an unsupported Unity `File.Move` overload;
the timing writer was corrected and the cached native run repeated successfully.
These measurements do not promise a full-release duration. They cover one
machine/source, and include tooling overlays. A native AnimationUtility edit
changed Manki's pose payload and source fingerprint; native release verification
refused it before producing a player. A separate cold import reproduced the
same changed source/package/payload identities. Native cook provenance and
certification passed for all four restored packages. Catalog/dependency native
self-tests passed. Exact results are retained in `validation.json` locally.

Cache defaults stay off. Compatibility intentionally excludes the source SHA
and includes Unity, host/target, package/settings and licensed input hashes.
Changed licensed inputs miss; changed tracked scripts/assets remain subject to
Unity invalidation and native verification. On 2026-10-17 local-only inputs left the
compatibility key too ([ADR amendment](../adr/0035-release-timing-and-import-cache.md#amendment--2026-10-17)):
they are reimported per file like tracked sources, and `cache.json` lists them. Snapshot corruption, symlinks,
incompatible inputs, independent writable copies and bounded replacement/retention
have focused Python coverage. Native timings also retain BuildReport steps;
their parent/child durations overlap and must not be added as independent work.
