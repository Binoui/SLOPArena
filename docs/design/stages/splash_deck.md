---
key: splash_deck
display_name: Splash Deck
target: pvp
players: 2-4
variant: static
authoritative_capability: null
availability: provisional-visual-and-gameplay-review-pending
competitive_intent: mixed
topology: broad barge deck with two raised cargo hatches and continuous recovery walls
source_scene: client/Unity/Assets/Stages/splash_deck/splash_deck.unity
baked_arena: data/arenas/splash_deck.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/splash_deck.prefab
design_brief: docs/design/stages/splash_deck.design.md
human_review:
  owner: project maintainer
  status: deferred
  process: gameplay-camera orbit and recovery review, then external normal PVP at 2 and 4 players
---

# Splash Deck — Moored Barge

## Fight premise

Four fighters brawl on a static tropical cargo barge. The barge is the main landmark; the village, palm beach, and harbor are distant context rather than land attached to the fighting floor. The maintainer authorized provisional production before human review on 2026-10-10. The design brief remains draft, not visually accepted or release-ready.

## Authoritative shell

- Main floor: x −15..15, z −11..11, top y=0. Its closed hull continues to y=−12, flush with every deck edge. There is no inset support arrangement or passage beneath the main floor.
- Cargo hatches retain the original raised surfaces: x −13.5..−7.5 and 7.5..13.5, z −2.5..2.5, top y=1.5, bottom y=0.
- Four ordered spawn markers retain (−6,0.85,−5), (6,0.85,5), (−6,0.85,5), and (6,0.85,−5), including their original inward facings.
- All blast planes are preserved: lower −12, upper 21.5, X ±25, Z ±21.
- The empty `AuthoringAids/KillPlane` marker coincides with the hull's lowest vertex at −12. Without it the baker would derive a lower plane of −22; do not remove it while retaining this hull.

Shared triangle collision owns wall contact, movement, recovery, and death. This revision changes stage geometry, not movement tuning: no wall climbing, wall jumps, sticking, automatic rescue, moving boat, rocking, buoyancy, or Unity-physics authority. Water remains visual-only at its existing height. A solid side prevents under-deck drift; it does not replenish recovery resources or make every depth recoverable.

## Presentation and provenance

The Resources prefab retains the original broad deck and two raised surfaces, with authored timber deck/hatch treatment and a painted closed recovery hull. The hull's visible mesh continues from the original deck underside at −0.5 to −12; the deck itself closes the upper half-meter. Both volumes share the authoritative X/Z boundary. A local warm fill supports the deck without adding colliders or gameplay components.

The background-only revision removes the connecting sand sheet and replaces all three source terrain mats with authored rolling landforms: one connected village/headland/harbor coast, one separate palm island, and two small bay-mouth islets. Stone slopes descend through narrow beaches into submerged foundations. Four stilt houses occupy flattened terrain pads; spaced palm clusters replace the catalog-like clutter. Seven existing pier modules form a compact working dock, with two boats positioned offshore. Background-only material clones mute the imported palette; world-scaled low-contrast water ripples retain the original surface geometry and height. Existing licensed TropicalEnvironment props are reused without modifying vendor assets or adding a dependency.

The deck/hull textures, hull/coast meshes, and local materials are stage-owned subassets in the existing prefab. Editable texture inputs and local backups are retained under `.stage-authoring-cache/splash_deck/barge-production-20261010/` and `.stage-authoring-cache/splash_deck/background-20261010/`. The licensed-art-dependent Resources prefab remains local/ignored under repository policy; the collision source and arena do not reproduce its art on a checkout without that prefab and its existing dependencies. The earlier `StageDesignPreviews/splash_deck/BargeBlockout.prefab` is retained as a non-runtime massing study, not a compatibility path or authoritative source.

## Verification — 2026-10-10

Native `sloparena.stage.bake --stage splash_deck` and `sloparena.stage.inspect` passed through the project-local Unity gateway. The source and baked hashes match; the stage has 36 collision triangles and four spawns. Inspection found valid mesh/material/shader references, zero cosmetic Colliders, one local light, and no diagnostics. A separate preservation receipt checks all six blast planes, floor bounds, and spawn positions/facings. Triangle/rectangle clipping found zero solid background intersections with death/recovery space.

The permanent `SplashDeck_RecoveryHullBlocksAirbornePassThrough` regression exercises all four roster capsules at four sides and four diagonal corners, at center heights −2 and −9, and verifies that the fighter can subsequently move outward. The executable comparison observed pass-through in all 64 old-slab cases and blocked contact in all 64 new-hull cases.

A ServerSimulation executable exercised 32 return routes across the roster: four side approaches and four near-corner approaches, starting at center y=−1 and using the full authored ordinary-air-jump budget. All landed on playable surfaces without death/respawn; no automatic ledge hang was observed or claimed. FightGuy's separate two-jump attempt from center y=−2 did not reach the deck. This is a resource-limited recovery check, not an arbitrary-depth guarantee or live-input acceptance.

Full Shared: 1,357 passed, two skipped, zero failed. The Server build passed with zero warnings/errors. Both used `SkipUnityPluginCopy=true`; no unrelated Shared plugin refresh was performed. Evidence is retained in `inspection.json`, `preservation.json`, and `recovery-smoke.json` in the production cache.

The first live match-camera attempt stalled after `editor_play`; `sloparena.ui.navigate --mode Solo` timed out with blocked/unsettled ownership. The maintainer subsequently authorized recovery: normal window close did not exit, SIGTERM ended the canonical process, and the installed launcher reopened the same project. That recovery produced a ready/stopped Editor with an empty error console and free/settled ownership. The failing Play Mode path was not retried; its cause is not verified fixed.

The subsequent background-only pass preserves the complete linked serialized closure outside `Distant tropical port`: all 103 protected objects are byte-identical to the accepted playable prefab backup, including deck/hull meshes and textures, hatch/trim presentation, spawns, local light, and atmosphere. Frontend, canonical collision source, and baked arena file hashes are unchanged. No rebake, gameplay change, Shared plugin copy, or Play Mode operation was performed.

Final native inspection passed with 195 Renderers, 271,442 triangles across all LOD meshes, valid references and URP shaders, zero cosmetic Colliders, one local light, and no diagnostics. The separate triangle/rectangle check finds zero solid-background intersections with X ±25 / Z ±21; visual-only water is excluded. Each boat's complete renderer footprint was tested on a 13 × 13 grid against the actual terrain triangles: every position has either no terrain or terrain below the −8 waterline. The small boat's highest intersected terrain is −9.479; the large boat has no terrain intersection. These are structural checks, not measured rendering performance or human gameplay acceptance.

Final textured Unity camera renders are in `.ability-lab-cache/stage-background-20261010/pass-04/`: 24 high orbit views at 15-degree intervals and eight low side/corner views, plus inspected contact sheets. The accepted playable presentation and authored atmosphere were retained with temporary preview directional lighting. The temporary scene was closed and clean Frontend restored; ownership released free/settled with zero operations. The last console query contains one earlier agent invocation error (`execute` is not a native command), not an asset/rendering failure; the console was not cleared. The boat is still the approved rectangular barge, and water detail is static visual shading, not animated waves or buoyancy.

Evidence: `authoring.json` records the initial rebuild, `geometry-check.json` the final solid-space/boat checks, `preservation.json` the protected serialized closure and file hashes, and `final-inspection.json` the final native receipt in the background cache. These Edit Mode views supersede the earlier visible sand-apron limitation, but do not prove the live match camera, fighter readability, human 2–4-player PVP, packaged-player behavior, or measured performance. The maintainer deferred those reviews; the brief remains draft.

## Outer-bay island chain — 2026-10-10

The maintainer approved a second distant layer for the sparse ocean-facing views: one rocky ridge island, two low palm islands with eight reused palms, and four smaller reef banks/tips. These are additive cosmetic meshes under `Distant tropical port/Distant outer bay island chain`, not playable platforms. Existing harbor/coast/islets, deck/hull, water, materials, atmosphere and local lighting are unchanged. The seven new landforms reuse resident shore materials; there is no new imported dependency or runtime code.

All 1,033 original serialized objects other than the background-parent Transform are byte-identical to the accepted baseline. That Transform changes only by appending the new chain root; its original children/order and other fields remain identical. Frontend, canonical collision source and arena hashes are preserved. Numeric checks find no solid background intersection with recovery/death space and verify all eight palm bases against the actual island meshes.

Native inspect passes with matching source/baked hashes, four spawns, valid references/URP shaders, zero cosmetic Colliders, one local light and no diagnostics. Final render metrics now come from `.stage-authoring-cache/splash_deck/island-chain-20261010/inspection.json`; they supersede the preceding background-pass receipt and do not establish measured performance.

Inspected all 24 high orbit views and eight low side/corner views in `.ability-lab-cache/stage-island-chain-20261010/pass-01/`. The temporary capture scene was closed and clean Frontend restored, stopped; final native errors are zero and ownership free/settled with zero operations. These are actual Edit Mode asset-camera renders with preview directional lighting, not an in-match review. No bake, plugin copy or Play Mode retry was needed. Human gameplay-camera/PVP, packaged-player and performance acceptance remain deferred.

## Re-authoring

Run the native bake/inspect commands only through `scripts/unity-editor-gateway.ts` in the sibling workspace, targeting the approved canonical Unity project with your own runtime-issued `ORCA_TERMINAL_HANDLE`. Preserve the standard `Stage_splash_deck/GameplayGeometry`, `SpawnPoints`, and `AuthoringAids` hierarchy; gameplay MeshFilters must be static. After geometry changes, rebake and run the focused ArenaShipping regression before reviewing the actual gameplay camera. Do not hand-edit the baked arena or derive gameplay collision from boat art.
