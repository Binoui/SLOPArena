---
key: table_manners
status: draft
direction_selected_by: maintainer
direction_selected: 2026-10-10
human_review: pending
production_status: baked_review_pending
---

# Table Manners — Oversized Living Room

Tiny fighters settle a very large argument on a green coffee table. The maintainer proposed reusing the installed Cozy Interior pack and fighting on a table inside an oversized room.

The first pass uses one broad, round tabletop with four separated spawns. Its decorative rim is flattened into the same fighting plane; the painted ring still defines the edge. The slab and four legs share the same stage-owned mesh for presentation and authoritative collision. The room floor lies at the lower death plane, so it supplies scale without becoming a second fighting floor.

Oversized orange seating, a reading lamp, plants, a console, shelving and a curtained window frame the table. Warm wood planks replace the initially tested checker floor. The quieter room materials and brighter green table keep the fight readable. Furniture stays outside the blast corridor; the ceiling is omitted to preserve the camera orbit.

This stage reuses assets from `Assets/LP_CozyInterior_JC`, inspected in the supplied `Scenes/LP_CozyInterior_Demo.unity`. The original demo scene and source meshes are preserved. The flattened table is a separate mesh; presentation materials are stage-owned copies. The runtime prefab depends on the installed pack's meshes and palette texture.

The production preview covers 24 elevated orbit angles and eight low recovery angles. Four actual roster rigs are posed temporarily at their package visual scales to establish the miniature-fighter premise. These are isolated Editor renders, not evidence of a running match. No staged actors or preview cameras are saved in the stage prefab.

Human review should check edge readability, falling/recovery near the slab and legs, the scale and framing of the normal gameplay camera, and two/four-player play. The current broad single-floor layout intentionally leaves the tabletop clear. Moving scenery, tabletop props, additional platforms and hazards are outside this first pass.

## Room detail pass — 2026-10-10

The maintainer accepted the basis and requested more interesting details. The room now has a pink geometric sofa rug and a cream reading-chair rug, three sofa cushions, a slightly untidy book stack, slippers and a forgotten controller. A small octagonal side table carries a teapot, cup and snack. These clusters explain what someone was doing before the tiny fight started.

The opposite view gains five groups of standing books, a horizontal stack, a radio and a small plant on the existing shelves. The console holds a vintage camera and mantel clock. A pink teddy bear faces toward the arena; a spinning top, lighthouse toy and red toy car add playful scale cues. Framed prints and three photographs break up the empty walls. The additions use inspected assets from the same installed pack and quieter stage-owned material copies.

All dressing belongs to `OversizedLivingRoom/LivedInDetails`. The table, source collision, baked arena, original object transforms/materials, spawns, lights and atmosphere are preserved. Rugs sit under distant seating rather than in the recovery corridor. Native inspection and persisted-content comparison pass; all 24 elevated and eight low Editor views are reviewed in `.ability-lab-cache/table-manners-detail-20261010/pass-03/`. Receipts live in `.stage-authoring-cache/table_manners/background-detail-20261010/`. Human gameplay and performance review remain pending.

## Room polish — 2026-10-10

The next iteration connects the furnished corners with sage painted wall panels, inset moulding and a chair rail. A fringed cream rug and larger potted plant finish the tea corner. Layered garden silhouettes, a sun and clouds add a stylized view behind the existing window and curtains. The new window artwork is stage-owned mesh geometry; the rug and plant reuse the installed pack.

The new additions live under `OversizedLivingRoom/RoomPolish`. Wall meshes are grouped by material and individual wall so their renderer bounds remain outside the blast corridor. Every previously accepted object, material, spawn, light and atmosphere setting is preserved. The canonical table mesh, collision scene and baked arena are unchanged. Final previews include the full 24-angle orbit, eight low views, and two wider room portraits in `.ability-lab-cache/table-manners-polish-20261010/pass-01/`. These remain isolated Editor captures with posed fighters; live play and performance review are pending.
