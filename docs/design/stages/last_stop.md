---
key: last_stop
display_name: Last Stop
target: pvp
players: 2-4
variant: static
availability: provisional-visual-and-gameplay-review-pending
competitive_intent: mixed
topology: chamfered station platform with two raised baggage stacks
source_scene: client/Unity/Assets/Stages/last_stop/last_stop.unity
baked_arena: data/arenas/last_stop.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/last_stop.prefab
human_review:
  owner: project maintainer
  status: pending
---

# Last Stop

A quiet railway station at the end of the line. Fighters share an isolated platform between two tracks, with a parked passenger train, a departure clock and a large LAST STOP sign behind them. Cream paving, yellow safety lines and green/red baggage stacks distinguish the fight floor from the dark rail yard. Station buildings, waiting shelters, benches, ticket/vending machines, signals, lamps, trees and distant buildings complete the background.

The main platform spans X=±18 and Z=±9, with three-unit chamfers at its corners. Its solid foundation extends from the floor at Y=0 to the lower death plane at Y=-10. Two solid 5×3 baggage platforms sit at (-10,-3) and (10,3), with tops at Y=1.5. Four inward-facing spawns use X=±5.5, Z=±4.5 and Y=0.85. Blast limits are X=±28, Z=±19 and Y=21.5. The canonical scene and runtime prefab share the stage-owned platform mesh; source baggage boxes match the visible stacks. Baked collision contains 56 triangles and four spawns.

The rail yard floor meets the lower death plane. The surrounding tracks, train, concourses and station buildings clear the recovery corridor. The station was moved farther back after a native low-camera sweep caught train occlusion. Cosmetic objects contain no Colliders; Shared owns gameplay collision. Four-fighter movement checks cover floor seams, a single jump onto either baggage stack, and inward/outward recovery against the platform foundation.

Native inspection passes with matching source/baked hashes, 368 renderers, 58,250 cosmetic triangles, 14 materials, one local light and valid mesh/material/URP shader references. These structural counts are not measured rendering performance. The focused Shared run passes 137 tests. Native review includes 24 orbit and eight low views using temporary posed roster rigs at their package visual scales. Captures live in `.ability-lab-cache/last-stop-20261011/pass-04/`; the stage thumbnail uses yaw 30. Temporary preview actors/cameras are removed and the original stopped scene is restored. Live-match, packaged-player and human PVP review remain pending.

Authoring, inspections and preservation receipts live in `.stage-authoring-cache/last_stop/station-20261011/`. Visuals use the locally installed LowPolyFPS5 Train Station pack, with new stage-owned URP material copies. The vendor assets are preserved. The pack, runtime Resources prefab and rendered thumbnail are ignored with their metadata. The canonical scene, original `PlatformCollision.asset` mesh and self-contained baked arena remain versionable. Reproducing the visuals requires the local prefab and installed pack.

## Station district — 2026-10-11

The background now forms a complete station district around the fight. A smaller waiting hall, parcel/cafe counter and furnished station square anchor the opposite platform. Two footbridges and stairs connect the station squares across the tracks. Paired tunnel portals give both rail ends a destination; a perimeter road crosses the tunnel embankments and joins the village streets. An eastern freight siding, three wagons, warehouse, crates, generator and water tank establish a different side view. Houses, planted verges and tree clusters continue around the scene. Low maintenance rails and buffer stops explain the central rail cut, with the rails kept below the death plane. Overlapping paving and stair surfaces were separated to eliminate visible surface flicker.

The new district and connections use stage-owned mesh batches per area/material. Native inspection passes with 469 renderers, 97,144 cosmetic triangles, 19 materials, one local light, zero Colliders, valid URP references and matching source/baked hashes. Background renderer bounds clear the recovery corridor. A persisted snapshot preserves the entire playable presentation, spawn markers and atmosphere settings; the comparison excludes Unity component ownership metadata. The canonical scene, platform collision mesh, baked arena, other stage prefabs and vendor files remain unchanged. No gameplay edit, collision rebake or new Shared test run was required.

All 24 orbit and eight low native views, plus two wider district views, were reviewed in `.ability-lab-cache/last-stop-coherent-20261011/pass-03/`. The thumbnail uses the updated yaw-30 view. Receipts and reproducible authoring scripts live in `.stage-authoring-cache/last_stop/coherent-20261011/`. Temporary preview objects are removed, the original clean/stopped Ability Lab is restored, and the native error console is empty. These Edit Mode renders do not replace live-match, human PVP or measured performance review.
