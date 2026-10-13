# Workshop creator setup and reference kit — 2026-10-13

**Status: Editor-first creator target selected; implementation deferred.**
Contributors should author Workshop characters, try them in Ability Lab and
Training, and publish a package. They do not need to build or distribute the
complete game. The maintainer may remain the only game builder. Keep the existing
presentation code; purchased vendor animations stay out of the public source.
The four-character friends demo remains the immediate release priority.

This follows the accepted [ModKit/installed-game preview](../adr/0027-open-source-modkit-preview.md),
[package identity](../adr/0025-workshop-package-architecture.md) and
[multiplayer admission](../adr/0026-workshop-multiplayer-synchronization.md)
decisions. Their existence does not establish that the full creator loop is
implemented today.

## Intended contributor workflow

1. Obtain the public Unity authoring tools and supported dependencies. Bring
   creator-owned or redistributable models, clips and presentation assets.
2. Author `package.json`, `character.json` and asset-catalog bindings through the
   existing package workflow. Edit the canonical sixteen-slot grid and public
   deterministic primitives; preview and inspect in Ability Lab.
3. Cook and validate a local package. Test the exact cooked content in Training
   through a maintainer-built installed game. Contributors cook content rather
   than rebuilding the game executable; the installed game supplies its licensed
   presentation dependencies and official assets.
4. Publish an immutable package version with hashes, pinned dependencies,
   compatibility requirements and attribution. A source attachment may accompany
   the runtime package; it is not the runtime authority.

Ability Lab remains useful for authoring iteration with the same Shared
simulation. Installed-game loading and Training must verify the distributable
package; an Editor preview does not establish that its client assets are packaged
and loadable. Online use separately requires exact content agreement and server
admission. Workshop packages cannot self-grant trusted `slop.internal.*`
capabilities.

Steam remains the accepted distribution target. Whether creators upload directly
to Steam or first submit to a project backend for staging/validation is an open
implementation decision. It does not change package identity, cooking, or
admission. A different final distribution source would need its own architecture
decision. No upload service or publishing integration is being implemented now.

## Asset choice

Free game use and public source redistribution need distinct permission. Adobe's
[Mixamo FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) permits
royalty-free use in games; it does not establish an open source-file distribution
grant. The [Adobe-hosted community licensing FAQ](https://community.adobe.com/questions-696/mixamo-faq-licensing-royalties-ownership-eula-and-tos-589400?lang=en)
explicitly excludes public distribution of raw characters/animations. That post
is community guidance, not a separately verified licence grant. Do not select
Mixamo files for the committed sample on this evidence. Local acquisition by
individual contributors is a different setup route and would not make a clone
self-contained.

Recommend the free FBX selection from Quaternius'
[Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html)
as the candidate to inspect. Its creator labels the library CC0 and identifies
a humanoid rig for Unity retargeting. Verify the selected download's licence,
contents and provenance before committing any file. The complete/source tiers
include additional paid content; a full Blender source kit is not a prerequisite
for this sample. [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
permits copying, adaptation and redistribution.

Retain the creator, original URL, exact archive/hash, selected filenames, licence
text and adaptation notes beside the sample. Preserve Unity metadata and import
settings. Keep an attribution even where CC0 does not require one.

## First implementation milestone, when scheduled

- One verified CC0 humanoid, plain materials and a small clip selection covering
  idle, movement, jump, one attack and a hit reaction, as available in the free
  download. Confirm the mesh and textures' terms as well as the clips'.
- One package-native starter and a minimal preview/Training surface. Use the existing
  package metadata, compiler, asset catalog, cook and deterministic Shared
  simulation. Preserve the canonical 16-slot contract; document simple repeated
  example behavior instead of claiming a complete character kit.
- One recipe for inspecting, editing and cooking the example, with source and
  generated-output ownership explained. Keep its selection separate from the
  four-character friends roster; it is not a fifth demo fighter.
- A creator smoke test that opens the public authoring setup without private
  assets, edits and cooks the starter, previews it in Ability Lab, then loads
  that same package into a prebuilt game for a Training movement/attack scenario.
  Include an invalid-package rejection check. No creator-built game is required.

Do not derive sample clips or cooked poses from the purchased packs. Do not add
a second gameplay simulation, an invalid-content fallback, or a new legacy
character execution path. Avoid importing the whole library for a small example.

## Dependency evidence and remaining proof

The current client compiles against the ignored Animancer `8.4.0` installation.
The author's [download page](https://kybernetik.com.au/animancer/docs/download/)
offers free Lite; its [itch.io listing](https://kybernetik.itch.io/animancer-lite)
lists `8.4.0` for Unity `6000.0`. The
[feature comparison](https://kybernetik.com.au/animancer/docs/introduction/features)
allows the required animation time/speed/custom-fade controls in the Editor and
restricts them in exported games. `PlayerRenderer` uses these controls for
scrubbing, ability playback and Hitstop. Lite is therefore a candidate for the
creator Editor; it is not a replacement for the maintainer's Pro game build.

This is documentation/source evidence, not a completed Lite compile/import test.
The Lite binary and selected Quaternius download were not obtained: their itch.io
download pages returned HTTP 403/Cloudflare security verification to this session.
No download, package install or canonical import is claimed. When implementation
is scheduled, verify the exact Lite package APIs, layout and notices in isolation
and inspect the actual CC0 archive before selecting assets. Keep third-party
tool provisioning separate from public source redistribution.

The existing Pipeline
fork's distribution route also remains unresolved in the
[dependency ownership record](2026-10-13-unity-dependency-ownership.md).

The first authoring target must also avoid incidental private Resources, fonts,
icons, audio and effects. Selecting a free rig alone is insufficient. Ability Lab
discovery and local Training package selection need an explicit path independent
of changing the four-character friends roster. A source package being visible in
Ability Lab does not prove the installed game can load its distributable assets.

Prioritize the local creator loop and installed-game loading proof when this work
is scheduled. Publication UI and the direct-Steam/backend staging choice follow
that milestone. No presentation rewrite, new assembly boundary, dependency
installation, asset import, commit, push or upstream publication is authorized
by this planning update.
