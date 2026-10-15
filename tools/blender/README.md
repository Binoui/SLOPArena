# Procedural environment art

The [FightGuy Sanctuary generator](fightguy_sanctuary/README.md) is a retained,
working example of original stage presentation built through Blender Python.
Keep its construction source alongside the generated art: it records how to
rebuild, vary and repair the environment, including its Unity export conventions.

## Reuse guide

| Starting point | Useful work to reuse | Existing assumptions to check |
| --- | --- | --- |
| [geo.py](fightguy_sanctuary/geo.py) | Mesh assembly, boxes, prisms, lathes, cylinders, curved lines, vertex colours and UVs | Unity world coordinates, face winding, `SA_*` material slots, atlas white patch and vertex-alpha masks |
| [rocks.py](fightguy_sanctuary/rocks.py) | Rock supports, peaks, spires, pines, snow and clouds | Sanctuary palette and intended support/death-space relationships |
| [dressing.py](fightguy_sanctuary/dressing.py) | Paving, masonry, planks and surface treatments | Several treatments and defaults belong to the monastery style |
| [build.py](fightguy_sanctuary/build.py) | Ambient-occlusion bake, FBX export settings, footprint/corridor checks and source stamping | The `Build` composition, named surfaces, markers and scenery positions are Sanctuary-specific |
| [preview.py](fightguy_sanctuary/preview.py) | Repeatable Blender camera renders for iteration | Camera vantages, lights, atlas interpretation and marker stand-ins are stage-specific; final review uses Unity |
| [StageVertexLit shader](../../client/Unity/Assets/Shaders/SlopArenaStageVertexLit.shader) | Vertex-colour shading, atlas/detail texture, shadows, metallic-looking highlights and cosmetic wind | Vertex colour space, alpha masks and material setup must agree with the exported meshes |

Architecture, prayer props, cloth patterns, the rising-fist emblem and palette
remain part of the Sanctuary's identity. Reuse them where the approved visual
brief calls for them. Other stages can reuse the geometry/export techniques
while developing their own architecture and motifs.

For the next stage, start with this example and adapt the pieces actually needed
in a separate stage folder. Keep Sanctuary rebuilds working. If a second stage
demonstrates a useful shared implementation, propose that small extraction under
the project's architecture-change policy; a universal generator or stable library
API is not established by this example.

Follow the [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md):
author presentation against the approved gameplay shell, preserve gameplay hashes
for art-only changes, and inspect actual Unity views. The Sanctuary layout reader
supports its current axis-aligned source boxes; arbitrary scenes need deliberate
transform/mesh handling. Recheck coordinates, normals, scale, materials, actual
background faces and camera clearance when adapting the pipeline.

Generate into an explicitly selected temporary directory first. Unity imports and
application use the canonical Editor gateway and your own bounded lease. Generated
FBX/textures and the ignored Resources prefab must be provisioned or rebuilt for
another art checkout; keeping Python source alone does not provision that prefab.
