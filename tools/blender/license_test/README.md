# Fighter's License rooftop deck

Original procedural presentation for `license_test`, the Tutorial's map: the roof of the
Department of Fighting, laid out as a fighter's driving-test course, with one open edge
over the city. The approved scope is the [design brief](../../../docs/design/stages/license_test.design.md);
the [tutorial record](../../../docs/design/tutorial.md) owns the lessons. No downloaded art
is used.

It follows the [shared kit](../README.md#shared-kit) like the other stages:

- `layout.py` keeps the approved deck as a `SPEC` of closed boxes (deck and building
  block, three 12 m walls, the 1.5 m and 3 m blocks and the 5.5 m lookout tower) and
  refuses to build unless the baked `data/arenas/license_test.arena` matches it.
  `--unity-shell` writes the collision for `sloparena.stage.shell-apply`.
- `textures.py` draws the 4096 × 3072 deck (about 1 cm per pixel): asphalt, patches,
  joints and cracks, and the painted course: start bay and chequered line, parking bays,
  the jump course's approach boxes, the teal test ring with the department seal, the
  knockout box, the hazard stripe and slab lip along the open edge with the barrier's
  sheared post bases, skid marks, drains and contact shade. Also the sign atlas (road
  signs, fascia, billboards, booth, rooftop sign, windows), brick, concrete and grit.
  Fine grit comes from the material's detail map so the deck PNG stays near 2 MB.
- `deck.py`: the deck surface, the building's facades down to the street (flush with the
  collision above the kill plane, DEPT. OF FIGHTING under the open edge) and the broken
  guardrail's two torn ends.
- `walls.py`: the three walls dressed inside their 1 m thickness. North, the Test
  Center's glass front, fascia, road signs and the painted FIGHTER'S LICENSE sign; west,
  the brick party wall with the ticket booth, the shuttered exit and the cone cage
  recessed into the wall; south, the barred safety fence with billboards.
- `course.py`: the two test blocks and the examiner's lookout, flush on their boxes, with
  yellow landing edges on every top.
- `city.py`: streets 60 m below with looping traffic and a coned-off lane, the Test
  Center tower with the rooftop sign, the brick neighbour with fire escapes, low roofs
  east of the edge, mid-rise further out, a hazy skyline, hills, sky and clouds.
- `presentation.py`: materials, shadow flags, the high east-south-east sun and a sky
  fill, atmosphere and the review cameras.

Build with the installed Blender, then apply under your own bounded gateway lease:

```bash
blender -b --factory-startup --python-exit-code 1 --python tools/blender/license_test/build.py -- \
  --out /tmp/license-test-generated --report .stage-authoring-cache/license_test/roof
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- \
  sloparena.stage.art-apply --stage license_test --from /tmp/license-test-generated
```

The report checks every role against the shell: the deck paint lies on the collision
top inside the deck; facades stand at most 2 cm proud above the kill plane; wall dressing
stays inside its wall's box (at most 6 cm in front of the face, never above 12 m), so the
booth and cone cage are recesses in the wall's thickness; obstacle dressing stays flush on
its box and never rises above its top; background never enters the blast corridor and,
within 15 m of the open side where a fighter can be outside the walls, stays two metres
under the kill plane; traffic and clouds run on lanes below or far above. The broken
guardrail is the one declared exception: its torn ends stay beside the north and south
walls (|z| ≥ 12.2 m), at most 0.95 m above the deck and 0.45 m out over the drop.

A layout change is a gameplay change: edit `SPEC`, rebuild the source scene with
`python3 layout.py --unity-shell` and `sloparena.stage.shell-apply`, bake, update
`TutorialCurriculum.LicenseTest` and `tests/Shared.Tests/LicenseTestArenaTests.cs`, then
regenerate the art.
