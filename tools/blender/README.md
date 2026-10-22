# Procedural environment art

Stage presentation can be generated entirely from Python in headless Blender and
applied in Unity by one shared command, `sloparena.stage.art-apply`, from data the
generator writes. These generators show the approach:

- [FightGuy Sanctuary](fightguy_sanctuary/README.md): a solo course on Himalayan
  rock pillars, shaded by `SLOPArena/StageVertexLit`.
- [After Hours](after_hours/README.md): a PvP rooftop in a night city whose signs,
  windows and traffic animate in `SLOPArena/StageNeon` without scripts.
- [Log Off](log_off/README.md): a split chopping stump with fungus recovery shelves
  in an ant-scale campsite at sunset, using both stage shaders.
- [Table Manners](table_manners/README.md): a kids' craft table with a play-dough
  cake in a bright bedroom, using original drawings and both stage shaders.
- [Slop Pit](slop_pit/README.md): a raised fight ring over a pit of slop in an
  underground venue whose crowd hops in `SLOPArena/StageNeon`'s bob channel.
- [Splash Deck](splash_deck/README.md): a painted fishing boat anchored in a
  Mediterranean harbour bay on a sunny morning, with a village, lighthouse and an
  animated sea on the presentation-only `SLOPArena/StageWater` shader.
- [Slopball Night Court](slopball/README.md): the enclosed Arcade court as a floodlit
  chain-link cage in a graffiti warehouse, with night light baked into StageNeon
  vertex colours, additive light shafts and a live combo board.
- [Fighter's License](license_test/README.md): the Tutorial's rooftop driving-test
  deck with a painted course, a barred fence see-through to the city, a booth and a
  cone cage recessed into the walls, and a broken guardrail over a 60 m drop.

Keep each generator's source with its stage: it records how to rebuild, vary and
repair the art. Once the maintainer accepts a stage, its generated FBX, textures,
materials and Resources prefab are committed too (listed in `.gitignore`), so a fresh
clone and a release build show exactly that art; this source rebuilds it.

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/<key>/build.py -- \
  --out /tmp/<key>-generated --report .stage-authoring-cache/<key>/<pass> --preview
```

Keep `--python-exit-code 1`: without it Blender exits 0 after an unexpected Python
error, and a reused `--out` folder still holds the previous run's export, which the
`sloparena.stage.art-apply` accepts because its stamp checks only the scene and arena hashes.

Character rigs follow the same rule: [manki_rig](manki_rig/README.md) rebuilds
Manki's Unity rig from a Mixamo-rigged FBX.
[wibou_coat](wibou_coat/README.md) fits, skins and binds Wibou's coat.
[bashie_rig](bashie_rig/README.md) makes Bashie's Mixamo rig Unity-ready.

## Shared kit

[`stagekit/`](stagekit/__init__.py) holds the code every generator uses. Stage
folders keep what gives a stage its identity: layout reader, palette, motifs,
textures, materials, preview views and lights. The kit is a small shared library,
not a universal generator; add to it only what a second stage actually needs.

| Module | Provides |
| --- | --- |
| [geo.py](stagekit/geo.py) | Mesh kit in Unity coordinates: boxes, prisms, lathes, tubes, grids, map outlines, vertex colour and alpha masks, StageNeon animation channels, Blender conversion |
| [blender.py](stagekit/blender.py) | Scene reset, objects, Cycles AO bake into vertex colours, FBX export with the Unity conventions (byte-repeatable: an unchanged build leaves committed art untouched), geometry digest, mesh-health counts |
| [checks.py](stagekit/checks.py) | Blast-corridor face clipping and the planned triangle budget |
| [arena.py](stagekit/arena.py) | Reading a baked `.arena` (blast corridor, spawns, collision triangles) and file hashes |
| [preview.py](stagekit/preview.py) | Eevee camera placement on a gameplay orbit |
| [noise.py](stagekit/noise.py) | Smooth value noise and fbm (numpy) for procedural textures and terrain |
| [presentation.py](stagekit/presentation.py) | The Unity presentation document (`<Stem>.presentation.json`) and a CLI that writes it without Blender |
| [shell.py](stagekit/shell.py) | The collision-shell document `sloparena.stage.shell-apply` reads: indexed meshes, mesh assets, spawn markers |

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

The [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md#a-stage-pass-in-order)
lists the whole pass; the generator side is:

1. Lock the gameplay shell first through the stage design and authoring skills. PvP
   generators keep the approved layout as a `SPEC` in `layout.py`, read the baked arena
   with `stagekit.arena` and refuse to build against a different bake; course
   generators may read the source scene, as the Sanctuary does. `layout.py --unity-shell`
   writes the collision through `stagekit.shell` for `sloparena.stage.shell-apply`.
2. Create `tools/blender/<key>/`, copy `build.py`, `layout.py`, `presentation.py`,
   `preview.py` and any motif modules from the closest example, then replace their
   content. Import the kit (`from stagekit import geo`) rather than copying it.
3. Set `TRIANGLE_BUDGET` before building and keep distant scenery cheap: use level
   of detail and skip faces that can never be seen from the arena. The 2026-10 stages
   build 60–320k triangles. The report flags an overrun; Unity measures performance.
4. Describe the Unity side in `presentation.py`: texture imports, materials by shader
   property, shadow flags per object, the `Stage Lights` rig, atmosphere, posters and
   review cameras. `build.py` writes it beside the FBX on export; nothing about the
   stage is written in C#.
5. Build the massing and key lighting, apply it with `sloparena.stage.art-apply --stage
   <key> --from /tmp/<key>-generated` and capture gameplay views before detailing. Eevee
   previews are for quick iteration only; the match camera's ACES grade and bloom
   decide how emission and colour read, and `tools/capture_exposure.py` flags clipping.
   With a `Stage Lights` rig, Training, Solo and PvP light the stage the same way.

### Pitfalls seen in generator passes

- **Vertex AO greys large flat paint.** A wall beside a floor sees half the sky, so
  a Cycles AO bake into a few corner vertices turns a whole white wall grey in Unity.
  Leave large hand-painted surfaces (decks, hulls, cabins) out of the bake and paint
  contact shade into their textures or bottom bands instead (Splash Deck's `NO_AO`).
- **Fine noise aliases on coarse grids.** Terrain vertices several metres apart
  cannot carry streaks a metre wide; per-vertex colour then smears into bright
  stripes. Keep vertex-colour noise at or below the grid frequency and add real
  geometry (outcrops, boulders) for rocky detail.
- **Text and decals have a reading side.** Unity is left-handed: facing -X your
  right is +Z. Decide where players stand when they read a sign or roof marking and
  map the atlas U from that viewer's left to right (see `boat.py`'s `_wall_decal`).
- **Opaque shaders show an atlas cell's whole rectangle.** StageVertexLit and StageNeon
  draw every texel, so a sign cell on rock or glass shows its background as a box, and a
  glow sprite shows its clear corners as a lit square. Paint that must sit straight on a
  surface is geometry (Kaboom Quarry's googly eyes, grin and spray text in stagekit's
  font); atlas cells go on boards, posters and stickers; glowing shapes are geometry that
  samples an opaque lamp block (see `manki_quarry/parts.py`).
- **Near-white sunlit tops clip.** Kaboom Quarry's first pass clipped 20–30% of every
  course capture, mostly white concrete tops and surf foam. Keep large walkable-top albedo
  around 0.5, keep foam soft and check with `tools/capture_exposure.py`.
- **Visual water inside the corridor is a declared exception.** Give it its own
  check role (flat at its level, just above the KO plane) rather than exempting the
  whole background check.
- **The gameplay camera never looks up.** The orbit pitch is clamped to 0–45°, so
  ceilings, rigs and the tops of tall walls are rarely on screen. Put what must read
  in the band the default and level views actually see, and check it in Unity
  captures; a lob-angle concept render shows views players never get.
- **An Eevee world volume absorbs sunlight.** Since Eevee Next it acts as infinite
  fog between the sun and the scene, so a sunlit preview with world haze goes dark.
  Use a local volume box for preview haze instead.

Follow the [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md)
for gameplay safety, the bake and inspection, and `sloparena.stage.views.*`
gameplay captures. Generate into an explicitly selected temporary directory; Unity
imports and application use the canonical Editor gateway and your own bounded lease.
Recheck coordinates, normals, scale, materials, background faces and camera
clearance whenever a generator reads a new kind of shell.
