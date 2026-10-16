# SLOPArena Art and Asset Conventions

This document covers character art, imported assets, animation bindings, and repository hygiene. For the game's graphic identity, follow the [Visual Language](../design/visual-language.md); for stage composition, follow [Stage Concepts](../design/stage-concepts.md). Gameplay authority belongs in [Architecture Overview](../architecture-overview.md) and [Combat Systems](../systems/combat-systems.md).

## Visual direction

Readability comes first: a fighter's silhouette, pose, weapon, and attack tell must work from the gameplay camera. The approved direction is **low-poly underground toy-fight-club, with DIY internet-game energy**: chunky simplified geometry, exaggerated readable proportions, strong character colors, and a premise that is cool at first glance and increasingly stupid on inspection.

“Toy” is a scavenged construction metaphor, not a requirement for literal toys, plastic shaders, cute proportions, or cozy worlds. Asset-pack collage is intentional: authored and licensed low-poly kits can coexist when scale, lighting, material response, palette/value hierarchy, and composition make them belong to the same fight. Curated junkyard, not a vendor demo scene.

Use each character's approved brief and preserve its identity. There is no universal pixel-art, three-tone shader, or outline requirement. Harmonization may improve a weaker existing treatment, but this approved direction does not assert that existing assets have already been restyled. Avoid realism, hyperdetail, and generic generated gloss as default goals; AI assistance is a production method, not the visual identity.

Stages share an improvised fighting premise, not one mandatory environment palette. Rooftops, industrial junk, strange pools, night cities, and a child's tabletop may all belong. Keep play space legible, background contrast subordinate, and landmarks purposeful; grime and generic graffiti alone do not create SLOPArena.

### Reusable production before bespoke replacement

Solo-development cost and authoring friction are constraints on the visual direction. Prefer reusable lighting, scale, material response, value hierarchy, and framing adjustments before replacing source assets or requiring bespoke illustration. A faithful model render can supply a portrait or marketing image; splash illustration is an option, not a character-admission requirement. Rework an asset when reusable treatment cannot preserve its identity or gameplay readability, not merely because it came from another pack.

The approved [rooftop study](../design/references/sloparena-visual-identity/visual-reference-study.png) demonstrates graphic/world coherence and a memorable landmark, not gameplay acceptance or a mandatory production template. Follow the [shared reference boundaries](../design/visual-language.md#approved-visual-board--rough-edges-clear-decisions); the direction should remain cheap to reproduce without compromising readable silhouettes or move tells.

## Character source assets

- Preserve a clean rest pose and an importable rig; validate the actual Avatar and bound clips rather than assuming every fighter uses `mixamorig:` bones.
- Keep models, textures, animations, and weapon or prop sources attributable to their original creators. Separate attachable weapons and VFX from the base mesh when the character design calls for it.
- Fix orientation, scale, root motion, and bone mapping at import or in the source asset; do not conceal a broken source transform with a gameplay-only correction.
- Keep geometry and effects legible from the gameplay camera. Verify retargeting and attachments on the actual character rig.

The [character import guide](../characters/adding-a-new-character.md) owns the current package, clip binding, inspect, and cook workflow. Do not infer rights or gameplay behavior from a source file format.

## Animation identity and bindings

Package semantic IDs such as `anim.wibou.g1` and `anim.bonk.a2` identify the intended animation. `character.json` references those IDs, and `CharacterAssetCatalog.asset` binds each to an imported clip and deterministic pose track. Filenames, Unity paths, pack names, and generated catalogs are not persisted move identities. Keep the ID and binding in sync when replacing a clip.

## Package asset ownership

Each character package under `client/Unity/Assets/CharacterPackages/<package>/` owns its authoring definition, metadata, and Unity asset catalog. Imported rigs and clips may be kept in ignored local art directories such as `Assets/Art/Characters/<name>/`; the package catalog binds the exact assets. The Unity cook writes bindings and pose data under `content-cooked/`. Cooked match content is immutable for a match, but cooking an imported asset does not change its license.

Runtime gameplay does not load raw authoring JSON. Unity presentation resolves semantic IDs through generated bindings and plays clips through Animancer. Keep vendor-dependent art out of public source when its terms do not allow redistribution; do not treat a successful cook as proof of publication rights.

## Third-party assets and credits

Thank creators in [Credits](../../CREDITS.md). Record source links, versions, license terms, asset usage, and required notices in the [maintainer asset notes](../assets/credits-and-licenses.md). The `creator`, `license`, and `attribution` fields in `package.json` describe the character package; they do not relicense imported models, clips, effects, or audio.

Keep purchased and restricted source packs out of Git, including Unity Asset Store imports and their raw art. The project also has ignored **required** local dependencies such as Animancer; a fresh checkout is not a complete visual or build environment. Check the actual build and tracked cooked artifacts before saying licensed work is absent from a distribution. See the accepted content boundary in [ADR 0022](../adr/0022-workshop-first-content-architecture.md).

The local Cartoon FX adaptations `FightGuyCycloneSpiral`, `FightGuyEmpoweredHit`, `FightGuyKiRelease`, `FightGuyPowerAura`, `FightGuyRisingFist`, `FightGuyStrikeWrap`, and `MankiNormalHit` are excluded from Git with their `.meta` files. Provision these customized prefabs under each package's `Presentation/` directory from the local asset backup, preserving the `.meta` GUIDs used by the committed catalogs. Importing the vendor pack alone does not restore these customized copies. Project-authored normal swing prefabs, meshes, and materials remain versioned.

## Readability and presentation

- Test silhouettes and move tells from the actual gameplay camera.
- Keep weapons, flames, particles, and cloth separate from the base mesh so they can be replaced or disabled cleanly.
- Prefer authored key poses and clear anticipation/recovery over extra detail.
- Keep signature colors and exaggerated proportions consistent across model, portrait, splash art, and marketing render. Illustration may amplify the fighter but must not turn it into a glossy cinematic promise of a different game.
- Curate kit differences through scale, lighting, material response, and detail hierarchy before adding texture or effects. Preserve separate fighter and gameplay colors; the graphic Action Yellow palette does not recolor the 3D roster.
- Keep imperfections in authored shapes and secondary details, not ambiguous silhouettes or unreadable attack effects.
- Ask: would someone want to main this ridiculous fighter, and can they immediately read what it is doing?
- Keep visual effects client-only; do not encode gameplay in a material, particle system, or animation callback.

## Repository and commits

- Write the game's name as **SLOPArena** (shorthand **SLOPA**, not SA) in player-facing text, docs, comments, log prefixes, Editor menus and shader names. Code identifiers keep the C# spelling `SlopArena` (namespaces, types, files, assemblies), and commands, skills and environment variables stay `sloparena`/`SLOPARENA`.
- Keep source filenames and semantic IDs consistent and descriptive.
- Do not commit generated Unity library state, local vendor assets, or transient reports unless a task explicitly makes an artifact canonical.
- Use one focused squash commit per branch and the repository's Conventional Commit format:

```text
<type>(<scope>): <imperative summary> (issue #N)
```

Use `feat`, `fix`, `refactor`, `docs`, `test`, or `chore` as appropriate. Explain the authoritative data path and verification in the pull request.

## References

- [Adding a Character](../characters/adding-a-new-character.md)
- [Character import checklist](../characters/character-import-checklist.md)
- [Character kit design principles](../characters/character-kit-design-principles.md)
- [Unity CLI](unity-cli.md)
