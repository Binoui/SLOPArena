# Fight Night Pier (FightGuy's Character Course)

Source for `fightguy_pier`, FightGuy's solo course on a seaside pier the fight club took over
for title night. The approved scope is the [design brief](../../../docs/design/stages/fightguy_pier.design.md);
the [production contract](../../../docs/design/stages/fightguy_pier.md) lists the artifacts and
checks.

- `layout.py` keeps the whole shell as a `SPEC`: route pieces (boxes from the sea or stacked
  on a deck), the start, 28 targets (balloons and four eight-hit bonus targets), three
  checkpoints and the ring finish. `--unity-shell` writes meshes, `Spawn_01` and the course
  markers for `sloparena.stage.shell-apply`; the strict run checks the bake. `check()` also
  refuses a required target over its checkpoint's sensor.
- `build.py` builds the dressed pier and checks every part against its role before exporting:
  - `pier.py`: every walkable top as one face on the ground texture, and each piece's sides
    dressed as what it is (pier fascia, piles and bracing, the burnt old pier, buoys, the
    gondola pods on crates, the VIP tower, coaster platforms, the ring), plus lamps on tops.
  - `rides.py`: the Big Wheel, carousel, High Striker, shooting gallery, dunk tank, car pen,
    gate arch, the ring with crowd and light towers, the derrick, the bag frame, the coaster's
    track and loop, the collapse barrier, the booths and bulb strings.
  - `world.py`: the sea, the dusk sky and clouds, the town and its lights, the headlands and
    the lighthouse.
  - `parts.py`: shared shapes (planes, panels, atlas plates and boards, posts, lattices,
    bulbs, stars, beacons, lamp posts, pennants, awnings).
- `bonus.py` builds the four bonus targets in four damage stages each (the car, the bull
  piñata, the PUNCH-O-METER and the heavy bag) into
  `client/Unity/Assets/Stages/fightguy_pier/Presentation/Course/FightNightPierBonus.fbx`, on
  the pier's own materials and atlas. `--preview <dir>` renders every stage for review.
- `textures.py` draws the ground (every top, planar, with the route paint), the atlas (signs,
  banners, the hub portrait, awnings, fans and the bonus props' plate, marquee and ranks; a
  white block for plain faces and a lamp block for glowing ones), detail and ripples.
  `palette.py` holds the colours.
- `presentation.py`: the dusk light, the five materials (`FNP_Lit`, `FNP_Ground`, `FNP_Glow`
  on StageNeon, `FNP_Sky`, `FNP_Water`) and shadows. Review captures come from the course
  replay runner; `sloparena.stage.views.*` refuses courses.

The balloons, pops, bonus-target behaviour, checkpoint frames, spotlights, finish and sounds
are not in the pier FBX. `sloparena.presentation.course.pier.build` writes them
(`Resources/Courses/fightguy_pier`) from bonus.py's FBX and FightGuy's own materials, and
`python3 tools/sfx/pier_course_clips.py` makes the clips.

A layout change is a gameplay change:

```bash
python3 tools/blender/fightguy_pier/layout.py --unity-shell > /tmp/fightguy-pier-shell.json
# calibrate on the offline shell before baking (CourseShellArena mirrors the bake)
SLOPARENA_COURSE_SHELL=/tmp/fightguy-pier-shell.json dotnet test tests/Shared.Tests/ \
  -p:SkipUnityPluginCopy=true --filter FightGuyPierRouteTests
# one gateway batch: shell-apply (add --create for a fresh checkout), then bake
#   ["sloparena.stage.shell-apply", "--stage", "fightguy_pier", "--from", "/tmp/fightguy-pier-shell.json"]
#   ["sloparena.stage.bake", "--stage", "fightguy_pier"]
python3 tools/blender/fightguy_pier/layout.py          # strict: the bake matches SPEC
dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true --filter FightGuyPierRouteTests
```

Then rebuild and apply the art, the bonus props and the course presentation:

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_pier/build.py -- \
  --out /tmp/fightguy-pier-generated --report .stage-authoring-cache/fightguy_pier/pier
blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_pier/bonus.py
# one gateway batch:
#   ["sloparena.stage.art-apply", "--stage", "fightguy_pier", "--from", "/tmp/fightguy-pier-generated"]
#   ["sloparena.presentation.course.pier.build"]
```

Move a balloon group with its piece, and keep the route test's waypoints in step: a leg that
walks to a takeoff edge, jumps and lands is the calibration, not the picture. After a
deliberate route change, re-export the replay inputs
(`SLOPARENA_EXPORT_COURSE_INPUTS=$PWD/scenarios/arcade`) and move the scenario's capture
ticks with the events `SLOPARENA_ROUTE_LOG=1` prints.
