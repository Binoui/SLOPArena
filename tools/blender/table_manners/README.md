# Table Manners craft room

Original procedural presentation for `table_manners`: tiny fighters on a round
gingham-covered craft table, one play-dough cake platform, and a bedroom with a
garden window, bunk bed, art desk and toys. The previous Cozy Interior room is
superseded; the generated presentation has no vendor art dependency.

The approved implementation choices and pending human reviews are recorded in the
[design brief](../../../docs/design/stages/table_manners.design.md). The
[production record](../../../docs/design/stages/table_manners.md) describes the
source/bake/presentation boundary and verification.

## Build and apply

Use the installed Blender with its Python/Pillow support. Generate outside Unity's
Assets directory; do not interrupt an interactive Blender session.

```bash
blender -b --factory-startup --python tools/blender/table_manners/build.py -- \
  --out /tmp/table-manners-generated \
  --report .stage-authoring-cache/table_manners/rebuild --preview
```

`layout.py` reads the authoritative `.arena` and verifies its collision, boundaries
and spawns against the selected shell specification. A mismatch blocks export.
`--no-bake --no-export` gives a faster geometry/clearance pass. `--spec-only` also
requires `--no-export`; it is for reviewing unbaked layout intent only.

After any changed C# has compiled, copy the four generated files below into
`client/Unity/Assets/Stages/table_manners/Presentation/CraftRoom/` within your own
bounded canonical Editor gateway lease, then refresh/import:

- `TableMannersCraftRoom.fbx`
- `TableMannersCraftRoom.layout.json`
- `TableMannersAtlas.png`
- `TableMannersDetail.png`

Apply and inspect through the gateway, keeping dependent import/application in
one batch as described by the [stage workflow](../../../skills/sloparena-stage-authoring/SKILL.md):

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.art-table-manners
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.inspect \
  --stage table_manners --output .stage-authoring-cache/table_manners/rebuild/inspection.json
```

The apply command verifies the export's source-scene and arena hashes, maps four
stage materials, configures stage-owned daylight/atmosphere, and replaces only the
superseded room/table roots and its own generated roots. It does not rebake or
write gameplay data. The generated art and Resources prefab stay local/ignored;
the source collision scene and original collision mesh assets remain versionable.
The source scene can be rebaked with `sloparena.stage.bake --stage table_manners`.

## Source files

| File | Role |
| --- | --- |
| `layout.py` | Selected table/cloth/cake shell, spawn specification, strict arena comparison and `--unity-shell` mesh/marker JSON |
| `table.py` | Gingham, border, closed drape, flush papers/stickers/glitter and cake |
| `room.py` | Supported room furniture, low floor clutter, window, garden and sky |
| `textures.py`, `font.py` | Original crayon drawings, lettering atlas and subtle detail texture |
| `palette.py` | Warm daylight palette and pink/primary-colour craft materials |
| `build.py` | Shared stagekit meshes, AO, export, geometry digest and clearance report |
| `preview.py` | Eevee quick-review views; actual appearance is judged in Unity |

The build reports its planned triangle budget separately from measured Unity
performance. Background faces are clipped against the blast corridor. Nearby
floor clutter remains below the kill plane; room furniture clears the larger
camera envelope. Table details stay within the authoritative functional silhouette.
AO excludes flush decals so the paper does not cast a false deep shadow across
the tabletop. Shader animation moves only distant scenery and soft decorative
cloth; gameplay geometry remains static.

Review actual gameplay-camera views with `sloparena.stage.views.prepare --stage
table_manners --lighting pvp`, then repeat with `--lighting scene` for Training's
additional lights. Use the workflow's full prepare/play/capture/stop/restore batch.
Agent captures and focused simulation tests do not establish human PvP acceptance.
