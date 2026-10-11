---
key: log_off
display_name: Log Off
target: pvp
players: 2-4
variant: static
availability: provisional-human-camera-and-pvp-review-pending
competitive_intent: playful
topology: one broad split stump with a jump shortcut and a connected ground route
source_scene: client/Unity/Assets/Stages/log_off/log_off.unity
baked_arena: data/arenas/log_off.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/log_off.prefab
human_review:
  owner: project maintainer
  status: pending
---

# Log Off

Tiny fighters turn a giant split stump into a woodland chase playground. Jump
across the northern split to intercept or escape, or stay grounded and run around
its southern tip. Both shoulders join a broad fighting floor, with four clear
inward-facing spawns and no additional platforms or hazards.

The [approved design](log_off.design.md) fixes the footprint and the 3-unit opening
at the intended crossing. Its native blockout was approved for continued
implementation on 2026-10-11. That approval carries forward while human in-game
camera and two/four-player PVP reviews remain pending.

The trunk extends from the fighting floor to the lower blast plane. The split is
open through that entire depth; it has no hidden bridge or safe cosmetic floor.
Shared owns the baked collision, grounding, movement, recovery and blast deaths.
The cosmetic shell uses the same vertex positions as the approved collision mesh,
with original cut-wood rings, darker bark, exposed split grain and flat moss tint.

A taller grove frames the left of the northern view, a smaller grove frames the
right, and the opposite view opens onto a quieter clearing. The forest ground
meets the lower blast plane. The background detail pass adds nine irregular
understory beds with leafy plants, grass, small flowers, fallen logs, rocks and
original mushroom clusters. Four gentle terrain ridges carry separated distant
tree groups. Their closed volumes meet the actual forest ground and extend below
it; every tree is grounded on its own terrain host. Every scenery prop's full
renderer bounds and every new background triangle clear the side blast planes
and immediate recovery corridor. The distant sky, ground, materials and
afternoon light belong to the presentation prefab.
The prefab has no Colliders, Rigidbody components or gameplay scripts.

The current cooked FightGuy, Manki, Wibou and Bonk definitions pass running full
jumps across the split in both directions, a fully grounded route around its tip,
stationary support at all four spawns, a lethal drop through the split and recovery
onto either shoulder with one air jump from the upper split. The recovery check
starts just below floor height; it does not promise recovery from every depth or
combat launch. These are deterministic Shared checks, not human PVP acceptance.

The focused Shared stage run passes 139 tests. Native stage inspection passes
with matching source/baked hashes and valid mesh, material and shader references.
Agent visual review covers 24 elevated orbit views, eight low views and three
route/top views. Live UI verification finds the new thumbnail in Solo stage
selection, selects Log Off for Training, loads the real stage and checks grounded
participants plus a jump and landing through the typed Training input sequence.
The HUD layout is valid and the final live check reports no console errors.
The original clean, stopped Frontend scene, Play Mode options, Game View selection
and match configuration are restored. Player-build performance and human PVP
acceptance remain pending.

The maintainer approved the woodland first pass and requested more background
detail on 2026-10-11. That cosmetic pass preserves the baked arena, collision
source, original stump mesh/materials, light and every existing scenery object.
Verification covers another full 24-angle elevated sweep, eight low views, three
route/top views and a woodland close-up, followed by actual Training and stage
selection captures. Numeric checks verify 158 new ground contacts, four closed
terrain volumes, 96 grounded terrain perimeter vertices and an empty recovery
corridor. These are structural and agent camera checks, not player acceptance or
a rendering performance result. The prior 139-test Shared result remains the
gameplay baseline; no gameplay source or arena data changed in this detail pass.

Procurement follows the bundle's Fantasy Forest pack. The inspected workset uses
Tree_01, Tree_02, Tree_03, Rock_01 and Rock_02, with catalog GUIDs checked against
Unity and native thumbnail evidence reviewed before placement. Stage-owned URP
material copies preserve the vendor palette texture and leave the vendor assets
unchanged. The workset and inspection evidence are under
`.asset-catalog-cache/log-off/`; gameplay, scene and placement receipts are under
`.stage-authoring-cache/log_off/production/`.

The additional inspected workset uses Plant_01–03, Grass_01–02, Log_01–02 and
Flower_01–02 from that same pack, reusing the accepted stage material. Existing
trees and rocks supply the other background roles. Procurement evidence is under
`.asset-catalog-cache/log-off-background/`, detail/grounding/preservation receipts
under `.stage-authoring-cache/log_off/background/`, and final native camera images
under `.ability-lab-cache/log-off-design-20261011/background/final/`. Original
terrain, ground-cover meshes and mushrooms live in
`Assets/Stages/log_off/LogOffWoodlandDetails.asset`.

The baked arena and original authoring meshes/textures are versionable. The
Resources prefab, rendered stage thumbnail and licensed forest pack are local
art assets ignored by Git. Reproducing the presentation requires that local art
checkout; the `.arena` remains self-contained.

## Test in Unity

1. Select Log Off in Training or Solo and confirm the rendered stump matches the
   fighting floor. Try each roster character.
2. Run a full jump across the split at its middle, then chase around the tip while
   staying grounded. Check takeoff and landing readability from both shoulders.
3. Rotate the normal game camera around the whole stage. Check the split, the
   unobstructed southern floor, bark recovery views and distant tree silhouettes.
4. Recover against the inner walls, then fall through the split and confirm the
   forest ground does not provide a surviving floor.
5. Play two- and four-player matches. Review spacing, pursuit shortcuts, edge
   pressure, camera visibility and combat readability before accepting the map.
