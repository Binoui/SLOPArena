# Worked example: FightGuy Flip Kick flow pass

The script that turned FightGuy's approved Flip Kick keys (air.3, in game his second aerial)
into the shipped clip. The owner's verdict on 2026-11-01 was "really good". Its run was
`artifacts/genra-runs/20261029-211132-fightguy-a3-flip-kick`, the brief is
`tools/genra/briefs/fightguy-a3-flip-kick.json`, and the shipped clip is
`client/Unity/Assets/CharacterPackages/fightguy/Animations/fightguy_air3_flip_kick.anim`.

What the flow pass changed, measured by `./genra check`:

| | First take | Flow pass | Hand-made attacks |
|---|---:|---:|---:|
| Joint stops per second | 1.52 | 0.39 | 9 in 10 under 0.62 |
| Body stopping together | 45% | 10% | at most 27% |
| Frozen joint-ticks | 16% | 4.8% | at most 12% |

The approved keys moved 1.4 cm at most, and every pose and arc target still passed.

It is reference code from a scratch run, not a library. It expects the run's
`brief.json` and `keys/result.json` beside it. Read it for the techniques that the
[guide's Flow section](../../GUIDE.md#flow-one-shot-moves) names:

- `cr()`: Catmull-Rom tangents, so body joints pass through each key instead of stopping on it.
- `pchip(..., bounce=...)` and `hinge_track()`: knees and elbows on their own flexion curves,
  turning over at a key with a set slope instead of easing to a dead stop.
- The spine `CHAIN` loop: an arch-pike-curl-uncurl wave up the spine, each joint about a tick
  behind the one below.
- `swirl()`: a second-axis curve that turns each out-and-back drift into a small arc.
- `between_keys()`: lags that peak between keys and fall back to zero on each key, so the
  chain trails without missing an approved pose.
- The settle offsets after tick 80: the hips over-rotate about 5° first, then the chest,
  head and hands follow before everything eases into the fall pose.
- `finger_curl()`: fingers curl with the arm's speed, two ticks late.
