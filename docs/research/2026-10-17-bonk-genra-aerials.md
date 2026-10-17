# Bonk GenRA aerials: Whirlwind (air.1) and Meteor Cleave (air.3)

Prepared by Claude (Opus 5.5), 17 October 2026. The owner found most of Bonk's aerials
weak ("it's a bit hard as a heavy sword user to do good aerials"). Three concepts were
generated with the [GenRA toolkit](../../tools/genra/README.md). The owner chose two
("i really like the whirlwind"; the cleave needed another pass) and approved importing
both after seeing them on the actual character. Air Cleave (`air.2`) had been redone
the day before, and the owner kept the current Acrobatic Double Slice (`air.4`).

## Concepts

Each brief keeps its slot's move-data clock and builds the move from Bonk's fall pose,
with the greatsword resting on his right shoulder. All three follow one principle: in the
air there is no ground to push from, so the whole body swings the blade.

| Run | Concept | Result | Cost |
| --- | --- | --- | --- |
| `bonk-air1-whirlwind` | One flat full-body spin, blade held out (`air.1`, 88 ticks, hit 19–39) | Liked; failed in Unity (below) | $1.06 |
| `bonk-air3-meteor-cleave` | Overhead chop down his middle, deep fold, blade hangs down (`air.3`, 50 ticks, hit 21–36) | "Fine, maybe needs another pass"; blade hidden from the game camera | $0.72 |
| `bonk-air4-wheel` | Forward somersault with the blade overhead (`air.4`) | Not adopted | $0.64 |
| `bonk-air3-meteor-cleave-v2` | The chop down his right side | Blade visible; left hand went over his head; failed in Unity | $1.68 |
| `bonk-air1-whirlwind-v2` | The liked whirlwind, rebuilt within Humanoid limits | **Approved, bound to `air.1`** | $0.61 |
| `bonk-air3-meteor-cleave-v3` | v2's motion within Humanoid limits, left hand in front | **Approved, bound to `air.3`** | $1.03 |

The briefs are `tools/genra/briefs/bonk-air*.json`. Each pass after the first attached
the earlier version's key poses as an image, so the motion carried over.

## What the toolkit learned

- **Flips.** The wheel run found that a rotation key past half a turn (165° then 195°)
  splined back through zero: a 113° hip flip in one tick. `Clip.from_keys` now unwraps
  rotation keys, so any number of turns interpolates smoothly.
- **Humanoid limits.** Converting the first whirlwind showed a 72 mm joint miss and
  40 cm vertex miss in Unity. Unity does not clamp muscle values, but it plays a joint
  only through its muscles. A collarbone cannot roll about its own length, and a twist
  folds back past 90°. The two-hand grip solver had rotated collarbones freely, up to
  122° past these limits, in every two-handed Bonk run so far. Repairing the finished
  clips failed: within the limits the left hand came 23 cm off the grip. So the solver
  now keeps the spine, neck, head, collarbones, upper arms and thighs within 2.5 times
  Unity's default muscle ranges. The approved clips reach that far, with no twist past
  85°. A new `humanoid` check fails anything beyond, and both moves were regenerated
  inside the limits.
- **Twist distribution (open).** Inside the limits, Unity still splits upper-arm and
  forearm twist differently from the toolkit, and the upper-arm twist muscle crosses its
  ±90° fold. The fits miss by 50 mm (whirlwind, left fingers) and 34 mm (cleave). Same-
  framing close-ups of toolkit and Unity renders show the same sword, hands and
  silhouettes. The difference is roll inside Bonk's thick upper arms. Modelling Unity's
  muscle mapping in the toolkit is the next pipeline fix.
- **Real-character review.** `unity_clip.py render` plays a candidate on the catalog
  prefab, with materials, sword and Animancer stretched over the move's ticks. It plays
  it next to the clip it would replace, from the review views. The owner approved from
  those videos.

## Integration

The approved candidates were copied into `Assets/CharacterPackages/bonk/Animations/` with
fresh GUIDs: `bonk_air1_whirlwind.anim` (`d9b3e250…`, 88 frames) and
`bonk_air3_meteor_cleave.anim` (`0eea15e4…`, 50 frames). All 102 curves and the clip
settings match the candidates, and narrow `.gitignore` exceptions track both.
`sloparena.character.bind` replaced only `anim.bonk.a1` and `anim.bonk.a3`. Both play at
1× (baked frames equal stage ticks). The old clips are untouched.

Saving the catalog auto-cooked every package and the roster pins. The explicit Bonk
`cook`, `verify` and `roster.refresh` then produced the same hashes, all valid. Other
packages' pose payloads are byte-identical. Their bindings and runtime JSON picked up
other threads' uncommitted source edits: Manki and Bonk effects, and Wibou and Bonk
window changes.

## Verification

- **Ability Lab** (native Shared scenarios on the cooked package, opponent 1.5 m ahead):

  | Move | Idle opponent | Shield | 8 m away |
  | --- | --- | --- | --- |
  | Whirlwind | hit at tick 19, 8 damage | blocked at tick 39 | miss |
  | Meteor Cleave | hit at tick 22, 11 damage, −45° spike | blocked at tick 21 | miss |

  Bonk ends every run in Idle. Captures show the glint, slash and hit effects on the new
  blade paths.
- **Shared:** 2891 passed, 2 existing skips.
- **Not verified:** live Training, PvP, landing at different heights and human feel.

## Evidence

- Runs: `artifacts/genra-runs/20261017-*-bonk-air*/`.
- Unity conversions and real-character renders: `artifacts/genra-unity-clips/`.
  The approved renders are `*-render-whirlwind-v2/` and `*-render-meteor-cleave-v3/`.
- Integration logs and pre-cook backups: `artifacts/bonk-aerials-integration-20261017/`.
- Lab captures: `.ability-lab-cache/bonk/genra-aerials-20261017/`.
