# Neo Mall (Wibou's Character Course)

Source for `wibou_mall`, Wibou's solo course through a shopping mall after closing, up to the
game store on level 4. The approved scope is the [design brief](../../../docs/design/stages/wibou_mall.design.md);
the [production contract](../../../docs/design/stages/wibou_mall.md) lists the artifacts and
checks.

The Slop Rally racing pass adds two SALE balloon decks from the podium to the east
gallery and two lit service signs from level 3 to level 4. Every roster fighter
can take either shortcut with plain jumps. `CourseFinishabilityTests` checks S1,
S2 and both together, alongside the ordinary route. The main racing line remains
the ordinary path in `racing_line.json`.

Export the measured FightGuy shortcut inputs and gameplay capture scenario with:

```bash
SLOPARENA_NEO_MALL_CAPTURE_OUTPUT="$PWD/tmp/neo-mall-scenario" \
  dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true \
  --filter FullyQualifiedName~NeoMallShortcutsFinishWithPlainJumps
bun scripts/sloparena-replay.ts --scenario tmp/neo-mall-scenario/shortcuts.json \
  --output tmp/neo-mall-gameplay --project-path client/Unity
```

The scenario settles the fighter after Go before replaying the measured inputs.
It leaves targets intact to inspect traversal, so its expected result is an
unfinished solo run with no falls. Inside an owned replay batch,
`sloparena.stage.views.cameras --stage wibou_mall --output tmp/neo-mall-overview`
captures the named top-down racing overview. Replay stop and restore remain
required for the batch.

- `layout.py` keeps the whole shell as a `SPEC`: decks (floating balcony and bridge slabs, the
  podium and the elevator tower on the floor, hanging planters and signs), the escalator's
  sloped slabs and balustrades, 1 m railings on the deck edges, the envelope walls, the start,
  31 targets (agent mannequins and four five-hit bosses), three checkpoints and the game store
  finish. `--unity-shell` writes meshes, `Spawn_01` and the course markers for
  `sloparena.stage.shell-apply`; the strict run checks the bake. `check()` also refuses a
  required target over its checkpoint's sensor.
- `build.py` builds the dressed mall and checks every part against its role before exporting:
  - `mall.py`: every walkable top as one face on the ground texture; balconies and bridges
    with fascia, LED strips and downlit soffits; glass railings over the collision railings;
    the stopped escalator; the glass elevator tower; the hanging planters and menu signs; the
    fashion podium; and the shopfronts on the envelope walls, level by level (the department
    store, the pharmacy and its pills, the food stalls, the arcade, the cinema, DICE DOJO).
  - `world.py`: the mopped ground floor and its fountain, benches, palms, wet floor signs and
    the mall cop's Segway; columns under the galleries; the glass vault with the moon; the
    giant katana sign over the Grand Court.
  - `parts.py`: shared shapes (planes, panels, atlas plates, lit signs, slabs, posts, cables).
- `targets.py` builds the agent mannequin, the agent clinging to the tower's lip and the four
  bosses in four damage stages each (the tube man, THE AGENT, FRANK and OFFICER DOUG) into
  `client/Unity/Assets/Stages/wibou_mall/Presentation/Course/NeoMallTargets.fbx`, on the mall's
  own materials and atlas. `--preview <dir>` renders every object for review.
- `textures.py` draws the ground (every top, planar, with the route paint and the RED PILL /
  BLUE PILL fork hints), the atlas (shop signs, posters, menus, the marquee, the department
  store, the night outside, Doug's cutout; a white block for plain faces and a lamp block for
  glowing ones) and detail. `palette.py` holds the colours.
- `presentation.py`: the moonlight, house fills and coloured pools, the five materials
  (`NM_Lit`, `NM_Ground`, `NM_Glass`, `NM_Glow` on StageNeon, `NM_Sky`) and shadows. Review
  captures come from the course replay runner; `sloparena.stage.views.*` refuses courses.

The mannequins, cuts, bosses, checkpoint frames, beams, finish and sounds are not in the mall
FBX. `sloparena.presentation.course.mall.build` writes them (`Resources/Courses/wibou_mall`)
from targets.py's FBX and Wibou's own materials, and `python3 tools/sfx/mall_course_clips.py`
makes the clips.

A layout change is a gameplay change:

```bash
python3 tools/blender/wibou_mall/layout.py --unity-shell > /tmp/wibou-mall-shell.json
# calibrate on the offline shell before baking (CourseShellArena mirrors the bake)
SLOPARENA_COURSE_SHELL=/tmp/wibou-mall-shell.json dotnet test tests/Shared.Tests/ \
  -p:SkipUnityPluginCopy=true --filter WibouMallRouteTests
# one gateway batch: shell-apply (add --create true for a fresh checkout), then bake
#   ["sloparena.stage.shell-apply", "--stage", "wibou_mall", "--from", "/tmp/wibou-mall-shell.json"]
#   ["sloparena.stage.bake", "--stage", "wibou_mall"]
python3 tools/blender/wibou_mall/layout.py          # strict: the bake matches SPEC
dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true --filter WibouMallRouteTests
```

Then rebuild and apply the art, the targets and the course presentation:

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_mall/build.py -- \
  --out /tmp/wibou-mall-generated --report .stage-authoring-cache/wibou_mall/mall
blender -b --factory-startup --python-exit-code 1 --python tools/blender/wibou_mall/targets.py
# one gateway batch:
#   ["sloparena.stage.art-apply", "--stage", "wibou_mall", "--from", "/tmp/wibou-mall-generated"]
#   ["sloparena.presentation.course.mall.build"]
```

Wibou's kit shapes this map (measured in `WibouMallRouteTests`): Rising Slash (E) keeps
carrying him forward for its whole duration and is once per airtime, so every E crossing and
climb lands on a deck that continues ahead, or against a wall; Dash Slash's charge stops his
drift, so it cuts rows rather than extending jumps. Move a target group with its deck, and keep
the route test's waypoints in step. After a deliberate route change, re-export the replay inputs
(`SLOPARENA_EXPORT_COURSE_INPUTS=$PWD/scenarios/arcade`) and move the scenario's capture ticks
with the events `SLOPARENA_ROUTE_LOG=1` prints.
