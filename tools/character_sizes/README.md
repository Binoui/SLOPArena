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
  stay on the body. Bones are hips-relative and scaled by `hurtboxBoneScale`; crouch
  and slide hurtboxes follow the rendered pose exactly.
- **One clip floats or sinks:** most clips bake root Y into the pose "based upon centre
  of mass at start", so a crouched first frame floats and an extended one sinks. Set
  the clip's *Root Transform Position (Y) > Offset* (`level`, `heightOffset` on an FBX
  clip). A positive offset lowers the body by `level × humanScale × visualScale`.
  Aim the planted frames at the floor, not a single-frame foot pop. Poses stay
  byte-identical (they are hips-relative); the cook only records the new clip hash.

Size targets and per-fighter values: [roster sizes](../../docs/characters/adding-a-new-character.md#roster-size).
