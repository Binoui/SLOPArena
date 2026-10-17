---
status: accepted
---

# Release timing, cook provenance and import reuse

The user approved these improvements after playtest.19 on 2026-10-14:
persistent stage timing, investigation of authoring versus fresh Windows cooks,
and a measured cache trial. Playtest.18 failed before publication because all
four packages had a different dependency hash for `tumble_drift.fbx`.

## Options and decision

Keep cold isolated builds as the default. Add a local, opt-in snapshot of a
settled release project's Unity `Library`, with compatibility determined by
Unity version, host/target, package/toolchain settings and licensed input hashes.
The source SHA identifies each snapshot but is not its compatibility key: Unity
must detect changes to source assets in a new isolated checkout. Copy snapshots,
never share a writable Library between Editors. A lock serializes cache access;
an invalid snapshot falls back to a cold build. Bound retained snapshots.

A Unity Accelerator service is an alternative, but introduces installation and
service maintenance. Defer it pending measurements. An exact-commit-only cache
is simpler but misses ordinary source changes and is not the selected design.

Retain complete native verification on every release. Cache hits do not certify
cooks or authorize publication. Compare cold and cached native imports and
player outputs before changing the default. Exercise changed scripts, assets
and incompatible toolchain inputs; retain timings and content identities.

Keep the immutable cook/hash contract. Record the authoring cook target and
toolchain alongside advisory local status, and retain Windows verification
reports on success and failure. Investigate the observed hash discrepancy with
controlled target/import experiments before changing importer behavior.
Native experiments confirmed that even forced reimport can retain different
warm Editor and isolated artifact identities with the same recorded import
dependencies. The canonical automatic queue also overwrites all four accepted
package fingerprints on a dependency reimport. Preserve normal authoring behavior
within this scope, but disable that queue during isolated batch imports and reject
any implicit rewrite of committed cooked bytes. Certification runs before a player
build; a failed certification does not authorize replacing the accepted artifacts.

Record controller transitions as durable attempts, producer substage durations,
native cook/build steps and GitHub job timings. Resume preserves start times;
retries are distinct attempts. Report operator wait separately from native and
CI work. Historical records without timings remain readable, without fabricated
durations. Wall timestamps correlate evidence; local durations use monotonic
clocks. Controller attempts retain their monotonic start and boot identity so
same-boot resumes preserve elapsed time. After a reboot, or for an older attempt
without that start, report duration as unavailable instead of deriving it from
wall timestamps. Existing recorded durations remain readable. Source checks,
isolated builds, protocol/image/backup guards and manual
Steam activation retain their existing boundaries.

## Amendment — 2026-10-17

The owner asked for per-file reuse after playtest.27 and .28 both imported cold.
playtest.28 missed because #297 turned two local-only Last Stop files into tracked
ones, which changed the licensed input hash. Local-only inputs are source files like
tracked ones: Unity records their import dependencies, including playtest.18's
by-name texture lookup (`SourceAsset/GuidOfPathLocation`), and reimports what
changed. Every release still verifies the committed cooks natively, so a divergent
import fails the build instead of shipping.

Compatibility is now the Unity version, host/target and the `Packages` and
`ProjectSettings` hashes (schema 2). A snapshot records its local-only inputs, and
each attempt's `cache.json` lists those added, removed or changed since. The release
candidate still pins its licensed input fingerprint. Cold builds stay the default
until a cache hit has been compared with a cold build.
