# Character sizes and foot contact

Two read-only Edit Mode evals measure the roster as the game renders it. Run them through
your own gateway lease from the repository root:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/character_sizes/lineup.eval.cs"
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- eval_file --file "$PWD/tools/character_sizes/grounding.eval.cs"
```

- `lineup.eval.cs` places every package fighter in idle frame 0 and writes
  `tmp/character-sizes/<time>/lineup-front.png` (orthographic, a line every 0.25 m)
  and `lineup-threequarter.png`. It returns each fighter's `top`, `head`, `hips` and
  `lowest` (lowest skinned vertex) in metres above the floor.
- `grounding.eval.cs` reports every bound clip's lowest vertex in cm over the clip
  (min, median, max). A grounded clip's min should be near 0; positive floats,
  negative sinks. Airborne clips float by design.

- `hurtbox_fit.eval.cs` fits bone capsules, scores actual cooked hurtboxes, and renders
  wire capsules through the modes below. Direct evaluation is read-only.

Both mirror `PlayerRenderer`: the rig root takes `presentation.visualScale`, sits at
`CapsuleHeight/2 + modelYOffset` above the floor, and clips play through Playables
without root motion or foot IK, as Animancer plays them. Two Edit Mode traps they
avoid: rigs with `CullUpdateTransforms` culling do not evaluate when no camera sees
them (the evals switch the temporary instance to `AlwaysAnimate`), and humanoid
`AnimationClip.SampleAnimation` writes the root transform, so it cannot show placement.

## Fixing what they find

- **Height:** change `presentation.visualScale`. The cook samples hurtboxes at that
  scale too. Then re-solve `modelYOffset` so idle frame 0 touches the floor (lineup
  `lowest` ≈ 0), and keep `hipHeight` near the idle `hips` value so standing hurtboxes
  stay on the body. Bones are hips-relative. Hurtboxes use scale 1; body-bone attack hitboxes retain
  `hurtboxBoneScale`. Crouch and slide hurtboxes follow the rendered pose.
- **One clip floats or sinks:** most clips bake root Y into the pose "based upon centre
  of mass at start", so a crouched first frame floats and an extended one sinks. Set
  the clip's *Root Transform Position (Y) > Offset* (`level`, `heightOffset` on an FBX
  clip). A positive offset lowers the body by `level × humanScale × visualScale`.
  Aim the planted frames at the floor, not a single-frame foot pop. Poses stay
  byte-identical (they are hips-relative); the cook only records the new clip hash.

Size targets and per-fighter values: [roster sizes](../../docs/characters/adding-a-new-character.md#roster-size).


## Fit and check hurtboxes

Run from the repository root through your own gateway identity and lease.
The Python runner writes its eval request and batch under `tmp/hurtbox-fit/`.
It needs no Editor source change or tool installation.

```bash
python3 tools/character_sizes/hurtboxes.py fit --packages bonk
python3 tools/character_sizes/hurtboxes.py evaluate --packages bonk
python3 tools/character_sizes/hurtboxes.py visual --packages bonk
```

Pass several package IDs after `--packages` to run a bounded group.
Coordinate shared source windows before fitting. When Nyx has a concurrent writer,
fit her last and agree the input window first.

`fit` samples idle frame zero with humanoid mappings and no root motion or foot IK.
It assigns vertices by their dominant skin bone and fits the 85th percentile of their distances
from each segment. Left and right segments share a region but use the nearest side.
It includes Manki's weighted arm deform bones and tail chains on both tailed fighters.
It excludes skirt, cape, coat, and hat bones. Bashie's shell can add a dedicated capsule.
Missing hand-end mappings use the hand itself. The head uses a world-up end offset.

The writer re-reads `character.json` and replaces only its `hurtboxBoneDefs` array.
Unrelated whitespace, properties, and concurrent source edits survive.
A model-placement change during fitting aborts the write. An unchanged fit writes no bytes.
The asset import and explicit native package cook run in the same owned batch.

`evaluate` samples every fourth frame of every bound clip. It loads the cooked package,
resolves semantic animation aliases, and calls `ServerSimulation.BuildEntitiesFromState`.
It rejects missing runtime shapes and cooked/live bone errors above 0.1 mm.
It reports both whole-mesh and hittable-body coverage.
Body totals exclude cloth and hats. Per-region results still show those excluded surfaces.
`hurtbox_sphere_baseline.json` retains the original seven-sphere definitions and scales for comparison.
Surface area comes from the mesh triangles, not vertex counts. Coverage is not a hit-rate score.

`visual` writes six 640 x 480 PNGs per fighter under `tmp/hurtbox-fit/visual/`.
They show idle, ground 1 at 45% of its clip, and hit-hard at 45%, each from front and side.
Playables samples the model, and the capture freezes that exact CPU-skinned mesh.
This avoids stale GPU skin poses in offscreen cameras. Wires come from actual cooked Shared entities.
It reports the semantic clip, frame, and capsule count for each image.

Each mode writes `tmp/hurtbox-fit/<mode>-<package>.json`.
Run `fit` twice to check idempotence, then `evaluate` and `visual` after the final cook.
Keep a fit that makes sense visually. Do not enlarge cosmetic cloth to raise the total.

Build the local review board after capture with `python3 tools/character_sizes/hurtbox_board.py`.
It writes `tmp/hurtbox-fit/visual/board.html`, with local image paths and pose switches for phone-width review.
