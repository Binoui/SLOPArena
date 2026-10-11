---
key: splash_deck
status: draft
approved_by: null
provisional_production_authorized: 2026-10-10
human_review: deferred_by_maintainer
background_revision_authorized: 2026-10-10
playable_presentation: maintainer_keep_unchanged
production_status: baked_authoritative_visual_and_gameplay_review_pending
blockout:
  - .stage-authoring-cache/splash_deck/design/barge-20261010/orbit-contact.png
  - .stage-authoring-cache/splash_deck/design/barge-20261010/recovery-090.png
---

# Moored Barge Stage Design Brief — Draft

## Personality

Four fighters turn a moored tropical cargo barge into a dockside brawl.

## Composition decisions

1. Retain the immutable `splash_deck` key, but replace the floating-boardwalk premise with a static moored barge. From the default camera, the fighting deck dominates the center; the barge itself is the hero landmark, not the distant village or a second sailing ship.
2. Preserve the current broad main-floor footprint, floor height, four spawn positions and facings, and both raised surfaces. The left and right raised surfaces read as low cargo hatches and retain two side high-ground positions; the open center remains the main exchange space. No railing, mast, cabin, cargo pile, or new obstacle occupies a fighting or recovery route.
3. Below the deck, the foreground and side edges reveal one continuous recovery-facing hull volume. Its outer walls meet the deck edge without an inset, overhang, recess, decorative opening, or narrow support arrangement that lets a recovering fighter drift beneath the floor. Every gameplay-significant hull surface must have matching baked Shared collision. This is an authoritative gameplay-shell change, not cosmetic dressing.
4. Use a broad, squared working-barge silhouette rather than a sailing yacht: an uninterrupted deck lip, substantial sides, restrained timber construction, and readable bow/stern treatment within the functional silhouette. Production must not add a protruding visual bow, keel, or gunwale that suggests collision absent from the shell.
5. The proposed recovery volume reaches the current lower death plane. Keep the current side/top/lower blast boundaries rather than silently increasing survival distance. The typed baker's validated `AuthoringAids/KillPlane` marker must coincide with the lowest hull vertex to preserve the lower plane; its default lowest-vertex-minus-ten derivation would otherwise move that plane downward.
6. The village, palm beach, and harbor become distant port context around part of the barge. From the default camera they occupy the middle distance and side thirds; the ocean-facing yaw keeps open-water gaps between distant island silhouettes under decision 14. Preserve useful existing source assemblies, but remove the giant bare sand connectors and bank aprons rather than filling every camera angle. Keep all solid background land and props outside the blast/recovery corridor.
7. Keep the existing ocean height and visual-only water exception. Below the waterline the hull still belongs to the authoritative recovery volume; water supplies no buoyancy, collision, landing surface, hazard, or swimming. Above the fight, open sky preserves visibility; farther away, quiet rooflines, palms, and harbor silhouettes establish the port.
8. Busy here: the barge's construction and the distant port clusters. Empty here: the central fight routes, space immediately outside the deck edge, and open-water gaps. The ocean-facing view gains a quiet distant island chain under decision 14, not foreground clutter. Judge all of these through a complete camera orbit, not one preferred screenshot. Leave the vendor demo scene unmodified.
9. Background-only revision approved by the maintainer: remove the generated connecting sand sheet entirely. Replace cut-out source ground patches with a coherent, volumetric harbor coast: village above the sheltered shore, lower harbor to one side, and a separate palm beach across the water. Land connections must be real sloping terrain, not flat sand fill.
10. From the default and rotated views, a few rooflines identify the village, a compact working dock explains the moored background boats, and palms follow the banks rather than filling a display forest. Reuse existing inspected stage assets, reduce duplicate ships and loose display clutter, and ground every retained prop numerically on its host terrain or dock.
11. Keep clear water immediately around the barge and every recovery side. The open-water yaw uses low distant islet silhouettes rather than another village or sailing-ship hero. Judge all intermediate yaw angles and low recovery views, not only the default view.
12. Use rolling land, exposed stone banks, narrow beaches, and submerged foundations to make shore transitions three-dimensional. Quieter background greens and distance layers support the unchanged foreground; no new asset pack or vendor-source mutation.
13. Improve the existing visual-only water surface without changing its height or adding gameplay, buoyancy, displacement collision, or new movement behavior. Preserve the complete playable presentation, canonical collision source, baked arena, spawn markers, and blast planes byte-for-byte or by protected-content fingerprint as appropriate.
14. Island-chain addition approved by the maintainer: retain the accepted harbor coast, foreground and water treatment unchanged. Across the previously sparse ocean-facing quarter-turn and intermediate views, add a second distant layer with one taller rocky island, two low palm islands, and smaller reef silhouettes between them. Varied shapes and generous water gaps establish a larger tropical bay without a second hero landmark. Keep the immediate foreground/recovery ring empty; reuse resident props/materials and judge the complete orbit, including low side/corner views. Human in-match review remains deferred under the existing provisional-production authorization.

## Gameplay shell

The intent is a solid recovery boundary using the existing movement and ledge rules. Wall contact does not add wall climbing, wall jumps, sticking, or automatic recovery.

```text
                 far deck edge / bow
        +--------------------------------+
        |         S3           S2        |
        | [west hatch]    [east hatch]   |
        |         S1           S4        |
        +--------------------------------+
                 near deck edge / stern

        Continuous hull walls below every edge
```

The floor and raised surfaces stay in place. S1–S4 preserve the existing baked positions/facings; this diagram is a topology sketch, not a replacement coordinate source. The canonical source is now `Assets/Stages/splash_deck/splash_deck.unity`, with one closed main hull under `Stage_splash_deck/GameplayGeometry`, the two original raised volumes, four ordered spawn markers, and the validated lower kill-plane marker. The matching cosmetic hull is not the collision source.

## Negative decisions

- No thin floating main floor, cosmetic-only recovery hull, or fighter pass-through through visible solid sides.
- No sailing, rocking, moving platforms, buoyancy, Unity physics authority, water gameplay, wall climbing, or wall jumps.
- No second hero landmark, route clutter, recovery-blocking rails, decorative colliders, or visual surfaces that falsely promise safe landing.
- No solid background land or props inside the blast corridor; the visual-only ocean is the existing explicit exception.
- No sand curtains, stretched terrain aprons, or mandatory land connection between every source neighborhood.
- No wholesale vendor demo/terrain dump, new asset dependency, or vendor-scene modification.

## Palette & lighting

Warm afternoon timber, weathered working-boat surfaces, deep palm greens, muted blue ocean, and open sky. Production now embeds authored timber/paint textures and local warm fill in the stage prefab. Fighters and the deck lip should remain clearer than the distant port; this still requires the actual gameplay-camera review. Earlier gray-box renders test massing only.

## Camera vantages

- Continuous full yaw from the main floor, including four quarter-turn views and intermediate angles.
- Both cargo hatches, four spawn approaches, deck corners, and bow/stern views.
- Low recovery views along each side and around each corner, checking continuous wall-to-lip alignment and no under-deck pockets.
- Waterline and below-edge views, checking that solid hull geometry never becomes a cosmetic pass-through.
- The real gameplay camera in Play Mode remains required for human review; isolated design renders do not satisfy it.

## Current evidence and production gate

The maintainer approved the static boat direction and solid pillar-like recovery volume on 2026-10-10. This supersedes the previous cosmetic-only shoreline revision. The brief remains `draft`; approval to work is not a claim of completed in-game visual review or external PVP acceptance.

The baseline live Unity probe found a 30 × 22 deck, floor top at 0, raised tops at 1.5, lowest baked collision vertex at −0.5, and lower death plane at −12. It found no recovery walls below −0.5 and no canonical collision source. Those were pre-implementation observations, not the current stage state.

The isolated design prefab is `Assets/StageDesignPreviews/splash_deck/BargeBlockout.prefab`. It preserves the deck, raised surfaces, and four spawn markers and proposes uninterrupted hull sides down to −12. It deliberately contains zero Colliders and is not selected or loaded by the normal stage runtime. It is a functional massing study, not finished boat art or authoritative collision.

The earlier isolated design pass produced 24 live Unity orbit views at 15-degree increments, four low side views, and a top view in `.stage-authoring-cache/splash_deck/design/barge-20261010/`. Its contact sheet was inspected; its report confirms production bytes were preserved during that design-only pass. Those captures do not show the subsequent production materials or prove in-match presentation.

The earlier saved-preview smoke confirmed flush hull boundaries, bottom −12, valid references, and zero Colliders. Its ready/stopped/free Editor status describes that completed design-only pass, not the final production verification attempt.

The production hull bottom is −12. The typed bake preserves the same lower plane through the validated kill-plane marker rather than deriving −22; no baker or gameplay mechanic was changed.

The maintainer explicitly authorized playable implementation while away from the PC on 2026-10-10, deferring the normal pre-production human review. This is a bounded provisional-production exception for the approved static barge and solid recovery hull, not a completed LOCK review or external PVP acceptance. Keep the brief `draft` until the maintainer reviews the actual gameplay camera and recovery behavior. Proceed with the canonical collision source, bake, matching boat presentation, stage shipping coverage, and actual wall/ledge recovery scenarios; report automated evidence separately from the deferred human 2–4-player acceptance.

## Provisional implementation evidence — 2026-10-10

- Native bake and inspect passed: 36 collision triangles, four spawns, matching source/baked hashes, valid cosmetic mesh/material/shader references, zero cosmetic Colliders, one local light, and no diagnostics.
- All six blast planes, main-floor X/Z bounds, and four spawn positions/facings are preserved. A triangle/rectangle intersection check found no solid background triangles inside the blast/recovery corridor; visual-only water remains the explicit exception.
- The real Resources prefab now contains flush painted timber hull sides from −0.5 to −12 beneath the main deck, cargo-hatch treatments, lowered distant port assemblies, and restrained shoreline banks instead of tall sandy connecting skirts.
- The old slab allowed all 64 airborne side/corner pass-through scenarios; the new hull blocks all 64. The permanent regression also verifies outward escape rather than trapping the fighter against the wall.
- An executable Shared/ServerSimulation smoke returned all four fighters to the floor on 32 side/near-corner routes, starting at center Y=−1 with the full authored ordinary-air-jump budget. Every route rejects death/respawn as a false recovery. This is not automatic ledge grabbing, wall climbing, or guaranteed recovery from arbitrary depth: FightGuy's ordinary two-jump attempt from center Y=−2 was insufficient.
- Full Shared coverage passed 1,357 tests with two skipped; the Server build passed with no warnings/errors. Shared compiled without copying unrelated plugin changes.
- Production receipts are in `.stage-authoring-cache/splash_deck/barge-production-20261010/`; the implementation contract is [splash_deck.md](splash_deck.md).

The earlier Play Mode attempt entered, then `sloparena.ui.navigate --mode Solo` timed out and left blocked/unsettled ownership. On 2026-10-10 the maintainer authorized recovery while away from the PC. Normal window close did not complete; SIGTERM exited the exact canonical Editor process, and the installed launcher reopened the same project. A fresh runtime session passed owned gateway commands and released free/settled with zero active operations. Frontend and stage source/prefab/arena hashes are unchanged; the Frontend scene is clean/stopped with no compile/reload in progress and an empty current native error console. The failing Play Mode path was not retried, so its underlying cause is not verified fixed.

Eight focused actual-prefab camera renders now exist in `.ability-lab-cache/stage-barge-20261010/`: four high quarter-turn views and four low side/corner views. Original materials were retained with stage-authored atmosphere/local light and repository-style preview directional lighting. The contact sheet was inspected; these are Edit Mode asset views, not gameplay-camera or human PVP proof. The temporary capture scene was closed and Frontend restored.

Those first production images exposed a rectangular hull silhouette and a wide flat sand apron. The maintainer subsequently accepted the playable presentation unchanged and authorized background-only decisions 9–13. The apron is removed by the following revision; the approved rectangular barge is deliberately retained. Human gameplay-camera/recovery and external 2–4-player PVP reviews remain deferred.

## Background-only implementation evidence — 2026-10-10

| Decision | Implemented evidence | Review limit |
|---|---|---|
| 9 — coherent harbor, no sand connector | Deleted `Quiet connected shoreline` and all three source terrain mats; authored one connected northern/eastern coast, separate palm island, and two distant islets. | Human composition approval remains deferred. |
| 10 — curated, grounded dressing | Four houses on blended flat pads, spaced palm clusters, ten embedded rocks, four dockside cargo props, seven pier modules, two offshore boats. Each boat footprint has 169 positions tested against the actual terrain: no terrain or terrain below the visual waterline. | No live fighter readability or performance claim. |
| 11 — full orbit and recovery clearance | Inspected 24 textured views at 15-degree increments and eight low side/corner views. Exact projected triangle/rectangle separation finds no solid-background intrusion into X ±25 / Z ±21. | Edit Mode asset-camera renders, not the running match camera. |
| 12 — real shore slopes and quieter palette | Rolling terrain descends through stone banks and narrow, contour-clipped beach bands into submerged foundations. Background-only material clones mute the existing imported palette; no vendor asset or dependency changes. | Static authored landforms; not a terrain gameplay system. |
| 13 — preserve accepted playable presentation | All 103 protected serialized objects and their linked meshes/materials/textures are unchanged; Frontend/source/arena byte hashes match. Only background water UVs, shading basis, material and original low-contrast ripple textures changed; surface vertices, triangles and height are retained. | No buoyancy, animated waves, movement changes, rebake or plugin copy. |

Final native inspection passes with 36 collision triangles, four spawns, matching source/baked hashes, 195 cosmetic Renderers, valid references/URP shaders, zero cosmetic Colliders, one local light, and no diagnostics. Final actual-prefab captures: `.ability-lab-cache/stage-background-20261010/pass-04/`. Receipts and editable original texture inputs: `.stage-authoring-cache/splash_deck/background-20261010/`. The initial `authoring.json` precedes subsequent palette/placement/contour refinement; use `final-inspection.json`, `geometry-check.json`, and `preservation.json` for final state.

The temporary capture scene is closed and clean Frontend restored, stopped; the gateway releases free/settled with zero operations. The last queried console has one earlier agent command-routing error, not an asset failure; it was not cleared. The earlier Play Mode hang remains unverified, and the brief stays `draft` until the explicitly deferred human review.

## Outer-bay island chain evidence — 2026-10-10

PASS: decision 14 — added one rocky ridge island, two low palm islands with eight resident-asset palms, and four smaller reef banks/tips. The accepted harbor, palm beach, original islets, barge, and water treatment are retained. No new material, texture, lighting, imported dependency, or runtime component was introduced; all seven new terrain meshes reuse the existing shore materials and contour-clipped beach transitions.

Preservation checks compare every original serialized object against the accepted prefab backup: 1,033 objects are byte-identical; the remaining background-parent Transform differs only by appending the new chain root to its child list. All its other fields and the original child order are unchanged. Frontend, collision-source and arena byte hashes match. The projected-triangle check tests all solid background meshes, including LODs, and finds zero recovery/blast-rectangle intersections. All eight palm bases match their actual mesh grounding heights within 0.001 m.

Inspected 24 textured 15-degree orbit views and eight low side/corner views in `.ability-lab-cache/stage-island-chain-20261010/pass-01/`. They show the new second-distance layer in the previously sparse ocean-facing views while retaining the harbor-facing composition. Native stage inspection passes with matching collision-source/baked hashes, four spawns, valid references/shaders, zero cosmetic Colliders, one local light and no diagnostics. Receipts and the accepted baseline backup are in `.stage-authoring-cache/splash_deck/island-chain-20261010/`; its `inspection.json` supersedes earlier render metrics.

Capture restoration closed the temporary scene and restored clean Frontend. Final Editor status is ready/stopped with no compile/reload; the native error query returns zero entries, and ownership is free/settled with zero operations. No console clearing, rebake, plugin copy, Play Mode retry, release build, commit or push was performed. The views remain Edit Mode asset renders with preview directional lighting; live match-camera, human PVP, packaged-player and performance reviews remain deferred. The brief remains draft.
