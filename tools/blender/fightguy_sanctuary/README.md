# FightGuy Himalayan sanctuary art

Original procedural presentation for the existing solo course. The source scene
supplies axis-aligned surfaces and markers; these tools read it without writing
gameplay data. No downloaded art or reference-game assets are used by this pass.
The approved visual scope is decisions 22–25 in the
[locked brief](../../../docs/design/stages/fightguy_sanctuary.design.md).

This generator is retained as editable production source and a worked example
for later stages. Its mesh, AO bake, export and clearance helpers come from the
[shared kit](../README.md#shared-kit); the layout reader, rocks, architecture,
dressing, props, flags, textures and preview here are specific to this Sanctuary.

Build with the installed Blender (including its bundled Python dependencies):

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/fightguy_sanctuary/build.py -- \
  --out /tmp/sanctuary-generated \
  --report .stage-authoring-cache/fightguy_sanctuary/himalaya/build --preview
```

Output consists of an FBX, source-hash stamp, painted atlas and detail texture.
The clearance report checks surface footprints, pillar extent, backdrop faces
against the full blast box, and distance to the camera envelope. Small decorative
props and roof ornaments need visual review; the report does not establish
collision, performance or human acceptance. Blender previews approximate camera
framing; Unity screenshots establish actual rendering. The report and stamp record
a `geometryDigest`; a shared-kit change must leave it unchanged.

To apply, acquire your own bounded canonical Editor lease under the
[coordination protocol](../../../docs/contributing/unity-editor-coordination.md).
Copy the four generated files into
`Assets/Stages/fightguy_sanctuary/Presentation/Himalaya` within that lease, settle
imports, then invoke `sloparena.arcade.course.art-himalaya`. It rejects a mismatched
source stamp, replaces only the previous art roots, preserves gameplay cues and
configures the generated materials. Do not invoke the greybox prepare command.

The runtime Resources prefab is intentionally ignored. Preserve its local art
delivery with the generated assets, or reconstruct it from the course preparation,
current layout and this art command. Inspect through `sloparena.stage.inspect`
without rebaking. Use the [course replay runner](../../../docs/testing/course-replay.md)
for actual gameplay views. Keep source scene, baked arena and gameplay scene hashes
unchanged throughout the art pass. Captures/reports belong in ignored caches.
