# Blind review of a built VFX pass

A fresh reviewer scores the "after" captures before the owner sees them. The reviewer
judges only what is on screen: it never reads the presentation builder, the concept
board captions or the author's notes, because an author reads its intent into its own
effects. The owner stays the judge; this review only catches what should not reach them.

Adapted from the independent review in
[kanaworksai-hue/effekseer-vfx-skill](https://github.com/kanaworksai-hue/effekseer-vfx-skill)
(MIT), with SLOPArena's reach, volume and two-stage checks added and screen feedback removed.

## Running it

1. Finish step 5's "after" captures and inspect the sheets yourself first.
2. Fill in the brief below and give it, unchanged, to one fresh subagent (Opus, in the
   background). Tell it nothing else about the effects.
3. When the report returns, make **one** fix pass for every move with `iterate`, recapture
   those moves, and do not run the review again.
4. Show the owner the board as usual. List any item still unresolved in one line; do not
   put the scores on the board, so the owner sees the effects first.

## Brief template

Replace each `<...>` and delete moves you did not change.

```text
You are reviewing combat VFX for <Fighter> in SLOPArena, a chunky toon 3D platform fighter.
You did not make these effects. Judge only what is visible in the images. Do not open,
search or read any code, builder, concept board or doc in the repository.

Character identity (from the owner): <one line>
Roster style: toon shapes with a dark rim, a saturated accent and a white or pale core,
alpha-blended so they read on bright and dark stages. No screen shake or impact frames.

Images (open every one with your image tool; say so if one is missing, never guess):
- Ten-view phase sheets, one per move: <run>/media/sheets/<move>.jpg
  Rows are camera views (behind, the ring, low, high); columns are phases with tick labels
  at 60 Hz; the strip under each frame is an enlarged inset.
- The same sheets with the move's real hitboxes drawn: <run>/media-hitboxes/sheets/<move>.jpg
- Bright stage and dark stage close-ups: <after-splash-deck sheet>, <after-after-hours sheet>
- Optional before/after: <before sheet> next to <after sheet>

Moves (slot, name, what it does in one line, the tick its hitbox opens):
- <slot> <name>: <one line>. Hitbox from tick <t>.

Score each move on each criterion 0-3:
0 missing or wrong · 1 present but weak, a player would not notice it · 2 clearly works ·
3 reads instantly and has a distinct identity.

- read: from the first visible frame alone, can you tell what the move is?
- timing: a short bright contact (2-5 ticks) when the hitbox opens, then a body, secondary
  motion and a fade-out? Buffs, armour and stances may fade in slowly instead; charges
  build inward before one release. Is every motif at full size on its first visible tick?
- layering: at the strongest frame, a pale core, a saturated body and a dark rim, readable
  on both the bright and the dark stage (no white wash-out, no mud)?
- shape_change: does the main shape sweep open, break up or change form, or does it only
  shrink and fade like a sticker?
- motion: do particles burst fast then slow down, and fly along the hit direction rather
  than as a symmetric ball?
- volume: does every shape hold up from all ten views, or does it turn into a flat card,
  a thin line or a strip from some angle?
- placement: do effects sit on the weapon, the body or the floor rather than floating
  beside them or hidden inside the fighter or a prop?
- reach: does the effect's shape, direction and length agree with the drawn hitbox, so a
  player could read the reach, without looking like the hitbox itself (no translucent
  hitbox-shaped bubbles)?
- tail: does the last third still fade out on purpose, rather than ending empty, cut off,
  or with a large shape still visible at the end?
- identity: would it be recognisable among the roster's other effects if turned grey, and
  does it fit the character line above?
- coherence: one palette family, one shape language and one motion vocabulary across the
  move, with no layer that looks borrowed from somewhere else?

Write one JSON object to <tmp/vfx/package/blind-review.json>:
{"moves": [{"move": "<id>", "scores": {"read": 2, "timing": 2, "layering": 2,
  "shape_change": 2, "motion": 2, "volume": 2, "placement": 2, "reach": 2, "tail": 2,
  "identity": 2, "coherence": 2}, "worst": "<criterion>", "fix": "<one change>",
  "verdict": "ship", "observed": ["<sheet>: <view> <tick>", "..."]}]}

Rules: every score is an integer 0-3. verdict is "iterate" if any score is below 2,
otherwise "ship". "worst" names a lowest-scoring criterion. "fix" is one concrete visual
change for that criterion, naming the view and ticks where you saw the problem and what
should look different. One fix only per move. "observed" lists what you actually looked at.
Then reply with a short table of moves, verdicts and fixes.
```
