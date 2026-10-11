---
key: table_manners
display_name: Table Manners
target: pvp
players: 2-4
variant: static
availability: provisional-visual-and-gameplay-review-pending
competitive_intent: mixed
topology: round tabletop with four legs above the lower death plane
source_scene: client/Unity/Assets/Stages/table_manners/table_manners.unity
baked_arena: data/arenas/table_manners.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/table_manners.prefab
design_brief: docs/design/stages/table_manners.design.md
human_review:
  owner: project maintainer
  status: pending
---

# Table Manners

A new local stage built from the installed Cozy Interior pack: tiny fighters on a round coffee table in a giant living room. See the [design brief](table_manners.design.md) for composition and review intent.

The canonical scene has one static coffee-table mesh, four tagged spawn points and an empty lower-plane marker. The 276-triangle stage-owned table mesh preserves the vendor palette UVs while flattening the raised rim into the floor at Y=0. The same slab and legs render in the runtime prefab and bake into Shared collision. The table is about 31.86 units across; its lowest point and the room floor are at Y=-15.41453. Side blast lines resolve to approximately ±25.92942 and the upper line to Y=20. Spawns sit at X/Z=±5 and Y=0.85, facing inward.

The prefab adds wood flooring, warm walls, oversized seating, a lamp, plants, a console, shelving, a clock and a curtained window. Background renderer bounds clear the blast corridor; the room floor is at the lower plane. Two stage-owned local lights and an atmosphere component establish warm room lighting. Cosmetic objects contain no Colliders. The table owns gameplay through the baked mesh rather than Unity physics.

Native bake/inspection pass with matching source/baked hashes, four spawns, 56 renderers, 26,618 cosmetic triangles, seven unique materials, two local lights and valid mesh/material/URP shader references. The focused Shared run passes 192 tests, including 49 tabletop height samples and 32 slab/under-edge checks across four roster capsules. These checks do not establish human game feel or measured rendering performance.

Authoring, inspection and preservation receipts live in `.stage-authoring-cache/table_manners/cozy-study-20261010/`. Final isolated Editor captures are in `.ability-lab-cache/table-manners-20261010/pass-03/`: 24 orbit and eight low views, with temporary posed roster rigs at package visual scales. The stage thumbnail uses the yaw-30 capture. Preview actors/cameras are removed and the original stopped scene restored after capture. Live-match, packaged-player and human PVP review remain pending.

The Cozy Interior pack, runtime Resources prefab, derived `TableMannersCoffeeTable.asset`, rendered thumbnail and local stage-preview scenes are ignored along with their metadata. The canonical authoring scene and baked `.arena` remain versionable. The baked arena provides self-contained Shared collision; rebaking the authoring scene requires the local derivative mesh. Reproducing the visuals also requires the local prefab and installed pack. No pack scene or mesh is overwritten. A hash receipt records Unity serialization of the vendor glass material during import/save; this is not used to claim whole-pack byte preservation.

## Room detail pass — 2026-10-10

The accepted basis now includes two seating rugs, three cushions, books and slippers, a forgotten controller, a tea/snack side table, filled shelves, a vintage camera and mantel clock, a teddy bear and floor toys, framed prints and photographs. The additions reuse the same inspected Cozy Interior pack and live under `OversizedLivingRoom/LivedInDetails`. Two new stage-owned material copies keep them quieter than the fight surface.

Current native inspection passes with 112 renderers, 48,439 cosmetic triangles, nine materials, two local lights, zero Colliders, and valid mesh/material/URP shader references. A persisted snapshot preserves every original object outside the new detail group, including active materials, lighting, atmosphere and spawn markers; the comparison normalizes the unused legacy URP `_Color` alias. The baked arena, canonical collision scene, table mesh and prefab GUID remain byte-for-byte unchanged. No collision rebake, gameplay code change or new Shared regression run is needed for this cosmetic pass.

Final captures are in `.ability-lab-cache/table-manners-detail-20261010/pass-03/`: 24 orbit and eight low views using temporary posed roster rigs. The thumbnail is refreshed from the yaw-30 view. Authoring, preservation and final-audit receipts live in `.stage-authoring-cache/table_manners/background-detail-20261010/`. The temporary preview is removed and the stopped Frontend restored after capture. These renders and structural checks do not replace a live PVP or performance review.

## Room polish — 2026-10-10

A further cosmetic iteration adds sage wall panelling and mouldings, layered silhouettes/sun/clouds behind the window, and a fringed rug and larger plant around the tea table. The additions use stage-owned meshes and six materials, plus the same installed pack for the rug and plant. Individual wall/material mesh batches preserve useful renderer bounds and keep all new geometry outside the fight/recovery corridor.

Current native inspection passes with 132 renderers, 51,948 cosmetic triangles, 15 materials, two local lights, zero Colliders and valid mesh/material/URP shader references. Persisted snapshots preserve the entire previously accepted prefab outside the new `OversizedLivingRoom/RoomPolish` group, with the same normalization of the unused `_Color` alias. The authoritative arena, source scene, table mesh, prefab GUID and vendor pack remain unchanged. Shared tests are not rerun for this cosmetic-only pass; native inspection, persistence and clearance checks are rerun.

Receipts live in `.stage-authoring-cache/table_manners/polish-20261010/`. All 24 orbit and eight low views, plus two wider room portraits, are reviewed in `.ability-lab-cache/table-manners-polish-20261010/pass-01/`. The stage thumbnail uses the new yaw-30 capture. Preview objects are removed and the original clean, stopped Frontend restored. Live-match, packaged-player, human PVP and performance review remain pending.
