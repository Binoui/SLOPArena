---
key: log_off
display_name: Log Off
target: pvp
players: 2-4
variant: static
availability: accepted
competitive_intent: playful
topology: split chopping stump with two lower bracket-fungus shelves
source_scene: client/Unity/Assets/Stages/log_off/log_off.unity
baked_arena: data/arenas/log_off.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/log_off.prefab
---

# Log Off

Tiny fighters brawl on a camper's chopping stump at sunset. The axe split offers a
jump shortcut, the southern floor keeps a continuous ground route, and two broad
bracket-fungus shelves below the east and west rim offer recovery and ambush
positions. An enormous tent, fire, chair and discarded phone give the forest its
logged-off campsite identity.

The maintainer selected the layout, world and mood on 2026-10-16; the
[design brief](log_off.design.md) records that implementation scope. This replaces
the older 32-unit stump, vendor woodland and distant OFFLINE treehouse. Human
in-game visual-design and two/four-player PvP acceptance remain separate from the
automated evidence below.

## Gameplay shell

The slightly irregular stump is approximately 29 units across, with its top at
Y=0. The northern split is 3 units wide at Z=6 and tapers to a southern tip at
Z=-1.4. It stays lethal through the trunk depth. Four inward-facing spawn markers
retain X=±7, Z=±5 and center Y=0.85, ordered with diagonal opposites first.

Each fungus shelf is a closed solid with its top at Y=-1.5, reaching 4 units beyond
the bark and extending 9 units along it. Shelf lips reach X=±18.5. A full-speed
flank exit lands on the shelf; one full jump returns to the stump for each current
roster fighter. Their undersides block movement, so recovery from below must route
around them. North and south have no additional platforms. There are no hazards,
pass-through surfaces or automatic rescue mechanics.

The source scene references the existing `LogOffCollision.asset`, rebuilt from
`tools/blender/log_off/layout.py`. The normal stage baker produces 484 collision
triangles and four spawns. Shared/GameServer and client prediction load the same
`.arena`; Unity presentation does not decide collisions, landings or deaths.

| Boundary | Baked value |
| --- | --- |
| Left / right blast | X=-28.5 / +28.5 |
| South / north blast | Z≈-23.88 / +24.943 |
| Lower / upper death plane | Y=-10 / +20 |

## Presentation

Original procedural meshes and textures replace `ApprovedStump`, `WoodlandWorld`
and `WarmAfternoonSun` in the local Resources prefab. Painted wood rings follow
the exact top outline; bark grooves recess into the walls. Cream-and-russet shelf
bands describe the landable surfaces. The trunk flares into roots only below the
kill plane, meeting a forest floor approximately 24 units beneath the rim.

The north campsite has an orange dome tent, folding chair, stone-ringed campfire,
marshmallow sticks, lantern and enamel mug on a log seat, boots, backpack,
woodpile, axe and NO WI-FI / JUST LOGS sign. The phone below the southern rim reads
NO SERVICE. Giant fern fronds, grass, mushrooms, moss, leaf litter, acorns and
pinecones establish the miniature scale; towering trunks, canopy and distant
silhouettes surround the clearing while leaving the western sunset open.

The prefab owns its warm sun, three camp lights, stump fill and atmosphere. Fire
flicker and firefly travel use existing shader channels. Surface values are tuned
against native PvP captures; Training matches them, because it switches its own
scene lights off for a stage with a `Stage Lights` rig. The proposed light shafts
are represented by canopy gaps and haze rather than volumetric beam geometry.
Human visual review can accept or revise that presentation difference.

The generator checks actual background triangles against the blast corridor and
keeps elevated scenery outside a 25-unit camera envelope. Tent hem, chair feet,
boot soles and phone corners are placed using the actual rendered ground mesh,
with 48 sampled support contacts. Other scenery retains its explicit ground and
attachment hosts. The stage prefab contains no Colliders or gameplay-owning scripts.

## Rebuilding

The [generator guide](../../../tools/blender/log_off/README.md) documents the build
and native apply command. Generated FBX/textures and the Resources prefab remain
local under the project's stage-art ignore rules. The generator source, original
palette/textures, layout specification, Shared tests and editor apply command are
kept in the repository. Source/arena hashes in the export stamp must match before
art can be applied; application preserves the baked data and prefab GUID.

## Verification — 2026-10-16

- Focused Log Off and ArenaShipping coverage: **244 passed, zero failures/skips**.
  Checks cover both split crossings, the ground route, grounded spawns, lethal
  drops, shelf landings/returns and bark contact across the current roster.
- Shared and Server build successfully with `SkipUnityPluginCopy=true`, zero
  warnings and errors. No Shared/server gameplay code changes were needed.
- Native inspection passes: matching source/baked hashes, 484 collision triangles,
  four spawns, 10 renderers, 93,740 imported visual triangles, six materials,
  five local lights, zero cosmetic Colliders and valid mesh/material/shader references.
  The generator reports 98,230 triangles before import and no clearance
  violations; these are structural metrics, not measured player-build performance.
- Agent review covers all 24 elevated orbit views, eight low recovery views and a
  top view, plus all 10 composited gameplay-camera views using PvP lighting and
  representative Training views. Native inspection's six global captures are
  dominated by the distant sky bounds; the targeted orbit/top captures supply
  the useful silhouette and alignment review.
- Live Training executes 122 Shared ticks: both fighters stand on their spawn
  ground, the player jumps, then returns grounded without death. This is a narrow
  runtime smoke; the Shared tests cover shelf behavior across the full roster.
- The stage-select thumbnail is refreshed from a native render with its GUID
  preserved. The final Editor is stopped on the clean Frontend scene, original
  Play Mode/Game View options restored, with an empty current error console and
  free/settled gateway ownership. The collision scene and asset/prefab GUIDs remain
  intact; art application preserves the new baked arena.

Receipts are local under `.stage-authoring-cache/log_off/campsite-20261016/`;
full orbit images under `.ability-lab-cache/log-off-campsite-20261016/orbit/`;
final gameplay views under `tmp/log-off-views/pass-02-pvp/` and
`tmp/log-off-views/pass-02-training/`. The production report records decision
conformance and the pending light-shaft difference. Accepted by the maintainer on 2026-10-17 after the stage pass ("the maps are much
better"). Frame time and the packaged build were not measured separately.
