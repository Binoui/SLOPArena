# Procedural environment art

Stage presentation can be generated entirely from Python in headless Blender and
applied in Unity by a stage-specific command. These generators show the approach:

- [FightGuy Sanctuary](fightguy_sanctuary/README.md): a solo course on Himalayan
  rock pillars, shaded by `SLOPArena/StageVertexLit`.
- [After Hours](after_hours/README.md): a PvP rooftop in a night city whose signs,
  windows and traffic animate in `SLOPArena/StageNeon` without scripts.
- [Log Off](log_off/README.md): a split chopping stump with fungus recovery shelves
  in an ant-scale campsite at sunset, using both stage shaders.
- [Table Manners](table_manners/README.md): a kids' craft table with a play-dough
  cake in a bright bedroom, using original drawings and both stage shaders.

Keep each generator's source with its stage: it records how to rebuild, vary and
repair the art. Generated FBX/textures and the Resources prefab stay local and
ignored; another art checkout rebuilds them from this source and the apply command.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/<key>/build.py -- \
  --out /tmp/<key>-generated --report .stage-authoring-cache/<key>/<pass> --preview
```

Keep `--python-exit-code 1`: without it Blender exits 0 after an unexpected Python
error, and a reused `--out` folder still holds the previous run's export, which the
apply command accepts because its stamp checks only the scene and arena hashes.

Character rigs follow the same rule: [manki_rig](manki_rig/README.md) rebuilds
Manki's Unity rig from a Mixamo-rigged FBX.

## Shared kit

[`stagekit/`](stagekit/__init__.py) holds the code every generator uses. Stage
folders keep what gives a stage its identity: layout reader, palette, motifs,
textures, materials, preview views and lights. The kit is a small shared library,
not a universal generator; add to it only what a second stage actually needs.

| Module | Provides |
| --- | --- |
| [geo.py](stagekit/geo.py) | Mesh kit in Unity coordinates: boxes, prisms, lathes, tubes, grids, map outlines, vertex colour and alpha masks, StageNeon animation channels, Blender conversion |
| [blender.py](stagekit/blender.py) | Scene reset, objects, Cycles AO bake into vertex colours, FBX export with the Unity conventions, geometry digest, mesh-health counts |
| [checks.py](stagekit/checks.py) | Blast-corridor face clipping and the planned triangle budget |
| [arena.py](stagekit/arena.py) | Reading a baked `.arena` (blast corridor, spawns, collision triangles) and file hashes |
| [preview.py](stagekit/preview.py) | Eevee camera placement on a gameplay orbit |

Kit changes must not silently change existing art. Every build prints and records
a `geometryDigest` (in `build-report.json` and the export stamp). Rebuild every
generator before and after a kit edit and compare: a changed digest is either an
intended art change for that stage or a regression. For example, the first-corner
face normal is load-bearing because Sanctuary's rock colouring branches on it.

The report's `meshHealth` also counts defects a render can hide: neighbouring faces
wound in opposite directions (Unity culls one), zero-area faces and loose vertices.
Open and overlapping meshes are intended, so these are counts, not a gate; a new
nonzero count deserves a look. Sanctuary's backdrop has one known inverted quad at
the foot of a distant peak.

## Starting a new stage

1. Lock the gameplay shell first through the stage design and authoring skills.
   PvP generators read the baked arena with `stagekit.arena` and check it against an
   approved layout spec, as After Hours does; course generators may read the source
   scene, as the Sanctuary does.
2. Create `tools/blender/<key>/`, copy `build.py`, `preview.py` and any motif
   modules from the closest example, then replace their content. Import the kit
   (`from stagekit import geo`) rather than copying it.
3. Set `TRIANGLE_BUDGET` before building and keep distant scenery cheap: use level
   of detail and skip faces that can never be seen from the arena. Both examples
   build 250–300k triangles. The report flags an overrun; Unity measures performance.
4. Build the massing and key lighting, apply it in Unity and capture gameplay views
   before detailing. Eevee previews are for quick iteration only; the match camera's
   ACES grade and bloom decide how emission and colour read.
5. Write the stage's apply command after `SlopArenaSanctuaryHimalayaCommands.cs` or
   `SlopArenaAfterHoursArtCommands.cs`: verify the stamp against the source and arena
   hashes, configure materials, lights and atmosphere, and replace only its own roots.

Follow the [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md)
for gameplay safety, the bake and inspection, and `sloparena.stage.views.*`
gameplay captures. Generate into an explicitly selected temporary directory; Unity
imports and application use the canonical Editor gateway and your own bounded lease.
Recheck coordinates, normals, scale, materials, background faces and camera
clearance whenever a generator reads a new kind of shell.
