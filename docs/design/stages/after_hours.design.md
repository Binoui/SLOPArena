---
key: after_hours
status: draft
approved_by: null
direction_selected_by: maintainer
direction_selected: 2026-10-10
human_review: pending
editor_import: complete
provisional_production_authorized: 2026-10-10
production_status: baked_review_pending
blockout:
  - .stage-authoring-cache/after_hours/design/rooftop-20261010/orbit-030.png
  - .stage-authoring-cache/after_hours/design/rooftop-20261010/orbit-sheet.png
  - .stage-authoring-cache/after_hours/design/rooftop-20261010/recovery-sheet.png
---

# After Hours Rooftop Nightclub — Draft

## Personality

Four fighters turn an after-hours rooftop dance floor into the city's least sensible closing-time argument.

## Composition decisions

1. Retain the immutable `after_hours` key. The maintainer selected a rooftop nightclub above a city on 2026-10-10. From the study's default yaw 0°, the cross-shaped rooftop dominates the center and foreground; the club building sits in the background center, with smaller supporting silhouettes in the side thirds.
2. Preserve the current cross footprint, main-floor top, two raised surfaces, and four spawn positions/facings. The center remains the main exchange space. The west/east raised surfaces become low performance risers: two existing high-ground choices, with neither a DJ booth nor equipment blocking their usable tops. Do not add a new platform or route merely to fit the nightclub theme.
3. Implemented gameplay-shell change under the maintainer's provisional-production authorization: extend the existing cross footprint into continuous building walls down to the existing lower death plane. As with Splash Deck's hull, visible solid recovery walls have matching authoritative Shared collision. Preserve all existing blast planes. This change does not add wall jumps, climbing, adhesion, or automatic recovery.
4. Below the lower death plane, a substantial building foundation and a street level establish that the roof belongs to a city. Above the fight, preserve open sky and clear camera space. No foreground canopy, lighting truss, wire, railing, bar, or tall prop occupies the fight/recovery corridor.
5. One club building, with a rooftop setback, canopy volume and sign backing, explains the venue across the gap. At yaw 0° it is the supporting center landmark; at yaw 180° it moves behind the camera and the opposite low roofs/distant city take over. It must never obscure the arena in intermediate views. The first study iteration placed it too near the camera path; the revised study moves it beyond that path.
6. At yaw 90° and 270°, apartment and service-building silhouettes establish different city neighborhoods in the middle distance. At yaw 180°, low roof volumes leave more sky and expose the distant skyline. Vary heights and spacing; keep alley gaps instead of evenly distributing equally loud towers around a ring.
7. Busy here: a small amount of roof-edge construction and the distant club sign/window clusters. Empty here: the central cross, riser tops, spawn approaches, camera orbit and immediate recovery space. Later dressing belongs to the established building volumes and should remain visually quieter than the fighters.
8. Surface intent: one readable roof plane with restrained seams, clear edges, and two distinct risers. Future surfacing should use charcoal roof/concrete materials with subdued magenta and cyan edge accents and warm club windows. The study is neutral gray; it does not establish final material or lighting quality.
9. Reuse suitable accepted assets only after inspection, or author original stage-owned geometry. No imported environment asset is selected or introduced by this study. A new asset dependency requires the project's asset-selection workflow before production.

## Gameplay shell

```text
                  north arm
                  +-------+
                  |  S2   |
        +---------+       +---------+
        | west    |       | east    |
        | riser   |S3   S4| riser   |
        +---------+       +---------+
                  |  S1   |
                  +-------+
                  south arm

        authoritative solid building walls below the cross
```

The diagram is a topology sketch, not a coordinate source. The current `.arena` remains authoritative. The local `massing.json` records the proposed geometry and existing spawns; marker spheres in the study are location aids, not fighter capsules.

The canonical collision source is now `Assets/Stages/after_hours/after_hours.unity`, with the standard `Stage_after_hours/GameplayGeometry`, `SpawnPoints`, and `AuthoringAids` hierarchy. A single closed cross extrusion replaces the overlapping thin floor boxes. The two riser volumes and four spawn markers preserve the previous layout. `AuthoringAids/KillPlane` is at the lowest shell vertex, preserving the existing lower death plane. Native bake and inspection pass. All six blast planes, floor bounds, and spawn poses match the previous arena within numerical tolerance; the deliberate gameplay change is the new recovery wall volume.

## Negative decisions

- No new route, platform, hazard, moving geometry, or rooftop equipment that obstructs playable space.
- No cosmetic-only building wall that recovering fighters can pass through.
- No second hero landmark, identical towers in a ring, or scenery placed only for a preferred camera angle.
- No solid background inside the blast/recovery corridor or apparent safe landing surface before a blast boundary.
- No unrelated stage, vendor asset, movement tuning, or gameplay-rule change.

## Palette & lighting

Late night, charcoal roofs, muted violet city atmosphere, warm window groups, restrained cyan and magenta venue accents. Keep fighters and usable edges brighter and easier to read than the skyline. Production will need actual gameplay-camera lighting review; neutral-gray renders do not verify this intent.

## Camera vantages

- Full orbit: 24 views at 15-degree intervals, covering all quarter turns and intermediate angles.
- Low recovery: eight side/corner views at 45-degree intervals.
- Top view for the cross footprint and riser/spawn relationships.
- Later in-match review: center, both risers, four arms, concave corners, low recovery, and full yaw with actual fighters and zoom/pitch.

The local `camera-poses.json` records each offline camera pose. The study uses an elevated orbit radius of 64, height 31, and target `(0, -2, 0)`; low views use radius 37, height -2, and target `(0, -4, 0)`. Blender rendering is not the Unity match camera.

## Production evidence and remaining review

The maintainer selected the rooftop nightclub direction, then explicitly authorized continuing without the skill's approval/access restrictions on 2026-10-10. Production remains provisional; this is not human visual/PVP acceptance.

The offline study remains in `.stage-authoring-cache/after_hours/design/rooftop-20261010/`. It is superseded for presentation by the actual runtime prefab. Original meshes, a procedural concrete texture, materials, a flush dance-floor tile treatment, neon edge inlays, authored block-letter signage and window meshes form the production art; no new external asset dependency was introduced. The final facade sign is positioned below its original massing-study height to remain visible through the orbit.

Native bake and final inspection pass with matching source/baked hashes, valid URP shaders/references, four spawns, two local lights and zero cosmetic Colliders. Final report: `.stage-authoring-cache/after_hours/rooftop-production-20261010/final-inspection.json`. Preservation receipt: `gameplay-preservation.json` in the same directory. Original arena/prefab backups and reproducible authoring scripts remain there.

Actual Unity prefab captures are in `.ability-lab-cache/after-hours-rooftop-20261010/pass-02/`: 24 orbit views and eight low side/corner views. The orbit and low-view contact sheets were inspected; the floor and risers remain unobscured. These renders use an isolated temporary scene with preview directional lighting, not a human in-match review. The temporary scene was closed and the original scene/selection/lighting restored; an initial restoration compile error was corrected and restoration verified.

Recovery testing exposed a Shared collision-grid insertion bug: a wall exactly on the maximum grid plane was omitted. `ArenaDefinition.ClampRange` now clamps both cell indices into the valid range. `AfterHoursArenaTests` guards maximum X/Z grid planes and verifies inward blocking/outward freedom for four roster capsules at all 12 wall segments and four recessed corners, at two depths. All 128 recovery cases pass. Focused arena checks pass; the full Shared suite passes with 1,489 passed and two existing skips. The updated Unity Shared DLL was built and copied while holding the Editor lease.

T3 execution uses this Codex runtime's own thread/session identity through the gateway's explicit identity input, retaining acquisition, settlement and release; it does not borrow an Orca identity. No gateway source was changed.

Human gameplay-camera, two/four-player PVP, packaged-player and performance review remain pending. Keep the brief draft until actual visual review/sign-off; further routine authoring is already authorized for this provisional rebuild.

## Background detail pass — 2026-10-10

The maintainer liked the rebuilt basis and requested more memorable details, especially in the background. The accepted fighting roof remains the visual anchor. Dressing now gives the surrounding blocks distinct identities:

- A lower annex attached to the club carries a supported canopy, tables, chairs, planters, railings, warm string bulbs and a small terrace edge accent.
- The west warehouse has original procedural brick surfacing, stone bands, fire escapes and a supported water tank placed clear of the existing roof equipment.
- The east apartments have balconies and a large record mural. The opposite service roof carries ventilation equipment, a chimney and a satellite dish.
- One distant tower has vertical facade ribs and a stepped crown. Quieter irregular blocks fill skyline gaps without another bright venue sign.
- Sidewalks, crosswalks, parked cars/taxis and emissive lamp heads establish street scale below the arena. These fixtures add no real lights.

New geometry is grouped into persistent mesh batches by district and material. It introduces no external art dependency, gameplay geometry, Collider, or additional local light. The current pass receipts are in `.stage-authoring-cache/after_hours/background-detail-20261010/`; actual prefab captures are in `.ability-lab-cache/after-hours-background-20261010/pass-03/`.

Final inspection passes: 223 renderers, 17,132 cosmetic triangles, 23 materials, two local lights, no cosmetic Colliders and no missing mesh/material/shader references. All City renderer bounds clear the blast corridor. The `.arena`, collision source scene, source cross mesh and prefab GUID are byte-for-byte unchanged from the accepted basis. Persisted snapshots also preserve the non-City hierarchy, geometry, active material properties, lighting and atmosphere. Unity's import synchronizes the unused legacy `_Color` alias to `_BaseColor` on four original URP materials; the preservation comparison normalizes this alias and confirms no active-property change.

All 24 elevated orbit and eight low recovery captures were reviewed after the final material import. The new emissive materials retain their emission keyword after reimport. The stage-selection thumbnail uses the final yaw-45 capture. Captures remain isolated Editor previews; live-match and performance review remain pending. Final restoration leaves one clean, stopped Frontend scene and free/settled Editor ownership.

The final Play Mode smoke stalled during domain reload before frontend navigation/map loading. It produced no runtime acceptance evidence. The Editor scene backup and log were preserved locally, the stalled process exited after a graceful termination request, and Editor restart completed. Final audit confirms a clean, stopped Frontend scene, zero cosmetic Colliders, zero background bounds overlapping the blast corridor, and free/settled ownership. The stage-selection thumbnail was refreshed from the actual Unity prefab capture. Shared and Server builds passed with no warnings/errors. See `runtime-smoke.json` in the production cache for the final recovery state.
