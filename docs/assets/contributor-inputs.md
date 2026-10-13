# Contributor asset input inventory

This inventory describes the canonical checkout inspected on 2026-10-13 at
`1e8f6e9`, including current working-tree assets. It supplements
[credits and licence evidence](credits-and-licenses.md); it is not a grant of
rights or a replacement for the package catalogs.

## Four-character authoring inputs

Read-only Unity `AssetDatabase.GetDependencies` traversal produced these counts.
Dependencies include scripts, rigs, clips, materials, textures and presentation
prefabs; rows overlap where inputs are shared.

| Catalog | Asset dependencies | Dependencies absent from Git |
| --- | ---: | ---: |
| FightGuy | 74 | 62 |
| Manki | 103 | 75 |
| Wibou | 50 | 44 |
| Bonk | 81 | 53 |
| Unique union | 251 | 184 |

The serialized catalogs are the binding authority. The offline
[`check_unity_setup.py`](../../tools/check_unity_setup.py) reports their exact
GUIDs and available local paths. The cook/inspect workflow validates actual
content once the client compiles. Preserve import settings and metadata when
provisioning clips; a store download alone does not restore adapted files.

## Sources and provisioning gaps

| Input family | Paths or use | Evidence and remaining work |
| --- | --- | --- |
| Character rigs, meshes and textures | `Assets/Art/Characters/{fightguy,manki,wibou,bonk}/` and shared inputs | Package ownership identifies which fighter may bind the asset, not its copyright owner. Model/texture creators, original sources and terms remain incomplete; record them before proposing source publication |
| Sword and fighter clips | Local Art exports and `Assets/~AnimationPacks/` | Grruzam products and some imported source paths are recorded in asset notes. Identify the source/version of each edited clip and additional Kevin Iglesias/Martial Art inputs; keep exact adapted files and metadata available to entitled contributors |
| Adapted character VFX | Ignored FightGuy and Manki `Presentation/` prefabs; shared VFX copies | Cartoon FX-derived files need the local adaptation backup as well as the vendor pack. The canonical `.gitignore` identifies specific restricted copies |
| Held shield | `Assets/Resources/VFX/ShieldGuard.prefab` and ignored `Assets/Art/vfx/Shared/ShieldGuard/` | The prefab references adapted Shield Shader FREE inputs. Source/pack evidence is recorded; importing another shield pack does not resolve those GUIDs |
| Environment presentation | `Assets/Art/Stages/`, JustCreate folders, FantasyForest and TropicalEnvironment | Record the actual pack/creator/version per stage. The environment catalog describes candidate packs, not an installed-input or shipping-rights guarantee |
| Input prompts and icons | `Assets/Resources/InputPrompts/` and `Icons/` | Kenney prompts are identified in existing credits. Other icons need provenance; local availability does not show that all may be committed |
| Audio | 56 local Audio assets in the Resources dependency traversal | Generated music is identified by the owner. Record generation/source terms per track and identify the separate sound effects |
| UI images and materials | Frontend backgrounds, MatchTextVFX and stage preview assets | Some source files are ignored while their metadata is tracked. Confirm authorship and source inputs before selecting narrow ignore exceptions; preserve other agents' current stage/UI work |
| Cooked pose data | Tracked `content-cooked/{fightguy,manki,wibou,bonk}/poses.bin` | The raw clips are mostly ignored, but sampled derivative data is public. Source-pack rights for this publication remain an explicit maintainer review item |
| Editable Blender studies | Ten tracked files under `art/blender/exports/`, about 37.7 MB | Preserve them while identifying the canonical Manki source and the studies worth retaining. Random export-folder names and blanket ignore rules do not show that existing files are disposable or independently redistributable |

Do not change the four package manifests' MIT fields to imply that vendor art has
been relicensed. Record source evidence at the asset/pack level, and retain
required notices when distributing permitted copies.

## Wider client inputs

The enabled build scenes, four catalogs, and all assets under Resources reached
1,053 unique Asset paths. Of those, 871 were absent from Git: 870 ignored local
inputs and one current untracked stage asset. No resolved path in this traversal
was missing locally. This broad Resources traversal includes unused/prototype
content; it is not a claim that all 871 files are required at startup or all ship
in a verified player.

Sixteen local inputs had tracked metadata without the asset file, including:

- `BonkDust`, `BonkSteelFlash`, and `BonkSteelSparks` materials;
- six MatchTextVFX materials;
- five `Original*` Log Off texture images;
- `FrontendPaperBackground.png` and `MainMenuBackground.png`.

These are concrete candidates for creator/source review. A tracked GUID does not
make a missing file recoverable, and a project-looking filename does not establish
authorship. Keep approved project-authored inputs with their metadata; keep
restricted adaptations in an authorized provisioning path. Do not broadly
unignore PNG/material files or prune licensed packs based on this traversal.

String-based Resources loads, unresolved serialized references, packed-player
dependencies, platform stripping and licence permissions need separate checks.
See [Unity setup](../contributing/unity-setup.md) and the
[ownership proposal](../plans/2026-10-13-unity-dependency-ownership.md).

Purchased animations remain local by maintainer choice. A small CC0 authoring
example is proposed in the [public reference kit plan](../plans/2026-10-13-public-reference-kit.md);
its licence and dependency checks precede importing or publishing any sample.
