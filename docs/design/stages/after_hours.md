---
key: after_hours
display_name: After Hours
target: pvp
players: 2-4
variant: static
availability: provisional-visual-and-gameplay-review-pending
competitive_intent: mixed
topology: cross-shaped rooftop with two performance risers and closed recovery walls
source_scene: client/Unity/Assets/Stages/after_hours/after_hours.unity
baked_arena: data/arenas/after_hours.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/after_hours.prefab
design_brief: docs/design/stages/after_hours.design.md
human_review:
  owner: project maintainer
  status: pending
  process: gameplay-camera orbit and recovery review, then normal PVP at 2 and 4 players
---

# After Hours — Rooftop Nightclub

The cross-shaped rooftop is the main combat floor. Two existing performance risers preserve side high-ground choices, while a flush tiled dance-floor treatment emphasizes the open center. City rooftops, warm windows and a restrained neon venue sign establish the surrounding nighttime neighborhood.

The maintainer selected this direction and authorized a provisional rebuild on 2026-10-10. The [design brief](after_hours.design.md) remains draft pending human review.

## Gameplay shell

The canonical scene contains one original closed cross extrusion, two static riser volumes, four ordered/tagged spawn markers, and an empty lower kill-plane marker. The extrusion's top matches the previous cross footprint; its uninterrupted walls reach the existing lower death plane. The two risers, floor top, spawn positions/facings, floor bounds and all blast planes retain the previous layout.

The new wall volume is authoritative Shared collision. It prevents visible-solid pass-through below the floor and introduces no climbing, wall jumping, sticking, automatic rescue or movement tuning. The `AuthoringAids/KillPlane` marker must remain aligned with the lowest shell vertex when rebaking so the lower boundary stays in place.

## Presentation

The stage prefab keeps its existing GUID and stage-owned atmosphere component. The revised presentation uses original geometry, procedural concrete surfacing, small edge accents, a flush central dance-floor treatment, warm/cool local fills and distance-separated city blocks. Original window/sign meshes and local materials/texture are persisted as prefab subassets; the original cross mesh is a stage-owned source asset.

The background detail pass adds a furnished club terrace, a brick warehouse with fire escapes and a water tank, apartment balconies and a record mural, a service roof, a stepped distant tower and quieter skyline silhouettes. Sidewalks, crosswalks, parked cars and emissive street lamps give the city below a sense of scale. District/material mesh batches keep the details grouped; the stage still uses only its two original local lights.

All solid background geometry belongs beyond a side blast plane or at/below the lower plane. Small floor inlays and trim are surface treatment; no decorative obstacle or Collider owns gameplay. Original art introduces no licensed external dependency. The Resources prefab remains local/ignored under existing repository policy, so the tracked collision scene/arena does not reproduce its presentation without the local prefab.

## Verification

Native bake and inspection pass, including matching source/baked hashes, four grounded spawns, valid mesh/material/shader references and zero cosmetic Colliders. Final metrics and diagnostic receipts live in `.stage-authoring-cache/after_hours/rooftop-production-20261010/`; actual asset-camera captures live in `.ability-lab-cache/after-hours-rooftop-20261010/pass-02/`.

The Shared recovery regression covers four roster capsules, all 12 wall segments, four recessed corners and two depths. The suite also guards maximum-plane spatial-grid insertion, which this rebuild exposed and corrected. All 128 recovery cases pass. Full Shared validation passed with 1,489 passed and two existing skips; the Unity Shared DLL was updated.

These checks establish structural/simulation behavior, not human game feel or measured rendering performance. Human camera/recovery review and normal two/four-player PVP acceptance are still pending.

The later background detail pass preserves the accepted `.arena`, collision scene, source mesh and prefab GUID byte-for-byte. Persisted non-City snapshots preserve the fight roof, spawn markers, active materials, lights and atmosphere; the comparison accounts for Unity's synchronization of the unused legacy URP `_Color` alias. Current inspection passes with 223 renderers, 17,132 cosmetic triangles, 23 materials, two local lights, zero Colliders, valid mesh/material/shader references and no background bounds intersecting the blast corridor. All 24 orbit and eight low recovery views were reviewed in `.ability-lab-cache/after-hours-background-20261010/pass-03/`. Preservation and audit receipts live in `.stage-authoring-cache/after_hours/background-detail-20261010/`. No collision rebake or Shared code change was needed for this cosmetic pass.

The final Play Mode smoke stalled during domain reload before frontend navigation/map loading. It produced no runtime acceptance evidence. The Editor scene backup and log were preserved locally, the stalled process exited after a graceful termination request, and Editor restart completed. Final audit confirms a clean, stopped Frontend scene, zero cosmetic Colliders, zero background bounds overlapping the blast corridor, and free/settled ownership. The stage-selection thumbnail was refreshed from the actual Unity prefab capture. Shared and Server builds passed with no warnings/errors. See `runtime-smoke.json` in the production cache for the final recovery state.
