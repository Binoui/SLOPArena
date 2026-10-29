# Move review recipes

Review every move from ten simultaneous cameras with one Training take per move.
Run these commands from the repository root.

```bash
python3 tools/move_review.py run nyx
python3 tools/move_review.py run nyx --moves 'real-g1,fake-*' --views behind,front --size 640x360
python3 tools/move_review.py run nyx --dry-run
python3 tools/move_review.py media tmp/moves/nyx/<run>
```

Unity parses the recipe. Python reads only `moves[].id` to select and schedule takes.
Python builds media from `plan.json`, each completed `take.json`, `frames.jsonl`, and JPGs.
A missing `take.json` means an incomplete take.

## Recipe fields

Every object rejects unknown fields, duplicate keys and incorrect JSON token types.
Paths are repository-relative below `scenarios/moves/` or `tmp/`, without symbolic links.
Outputs use fresh directories below `tmp/moves/`.

| Field | Meaning |
| --- | --- |
| `package`, `stage` | Admitted fighter package and baked Training stage key. |
| `facing` | World degrees or `"spawn"`. Default is the spawn yaw. |
| `dummy` | Defaults merged into each move. `package`, `distance` in metres from 1 to 25, `behavior` as Idle, Attack, Shield or Avoid, and integer `damage` from 0 to 300. |
| `camera` | `distance` from 2 to 40, `fov` from 5 to 120, `look` from -3 to 5 metres above the capsule centre, `spread` from 0 to 3 and `gameplayPitch` from 0 to 45 degrees. Defaults are 8, 40, 0.3, 0.6 and 35. |
| `views` | Optional array of 1 to 10 unique named views. Each has `name`, `yaw` from -360 to 360 and `pitch` from -10 to 89 degrees. |
| `size`, `quality` | Requested even `"WxH"`, default `"960x540"`, and JPG quality from 50 to 100, default 88. |
| `settle` | Exactly 30 neutral ticks after arrangement, excluded from captures. |
| `phases` | Between 1 and 6 sheet columns, each with `name` and `at`. Per-move phases replace these. |
| `moves` | Between 1 and 256 move objects. |

Default views are eight azimuths at 45-degree intervals and pitch 35, plus `low`
at pitch 8 and `high` at pitch 45. Their names in order are `behind`, `rear-left`,
`left`, `front-left`, `front`, `front-right`, `right`, `rear-right`, `low`, `high`.
Yaw zero is behind the arranged fighter, +90 is her left, and 180 is in front.
Views retain the arranged orientation when the move turns the fighter.
The gameplay camera remains enabled and pinned behind her for input and sight holes.

Each move has these fields.

| Field | Meaning |
| --- | --- |
| `id` | Unique stable lowercase id, starting with a letter or digit and containing letters, digits, dots or hyphens. Maximum 32 characters. |
| `slot` | Canonical `ground.1`, `ground.3`, `ground.4`, `ground.A`, `ground.E`, `ground.R`, `ground.F`, or the corresponding `air.*` slot. |
| `form` | Starting form 0 or 1. Default 0. The cooked package must supply the requested form and slot. |
| `label`, `facing`, `phases` | Optional label and overrides. Labels default to the cooked move name. |
| `dummy` | Overrides distance, behavior, damage and optional `input`. Package selection belongs at recipe level. |
| `input` | Semantic step array in the `sloparena.training.run` format. |
| `pad` | Alternative to `input`, using the `sloparena.input.pad-run` timeline format. |

Exactly one of `input` and `pad` is required. Take length is the sum of step ticks
or the pad timeline's `frames`, from 1 to 600. There is no separate horizon.
Semantic `moveX` and `moveY` are right and forward relative to arranged facing.
`facingYaw` and `aimYaw` are relative degrees multiplied by 100.
A semantic `targetEntityId` may be `"dummy"`.
Pad timelines go through the real Input System with the gameplay camera pinned.
Dummy `input` uses the existing target timeline format, with `ticks` equal to the
player input length. Its camera space is the arranged fighter's frame.
See [Unity CLI](../../docs/contributing/unity-cli.md) for both input formats.

Phase `at` accepts `start`, `hit`, `hit#N`, `cue:<presentationId>`, `end`, or an
integer frame, followed by an optional signed frame offset. For example, `hit-6`
and `cue:presentation.nyx.crow-pop+1`. `start` is the first observed activation
of the declared slot. Hits and cues must belong to the player. Missing anchors
are problems, with no guessed frame. Out-of-range resolved frames are clamped
and listed as problems.

The capture budget is 1400 megapixels per take. Unity reduces requested dimensions
by even steps at the same aspect ratio. It retains every view and tick. The minimum
size is 640x360. Ten views at 600 ticks fit at that size. An aspect ratio that cannot
fit above both minimum dimensions is rejected. Use 16:9 for the worst case.

## Native commands and output

`sloparena.moves.prepare --recipe R --output RUN` validates the whole recipe,
writes `plan.json`, and prepares disposable Training in stopped Edit Mode.
Optional `--moves` selects ids for the plan. Pass `--views` and `--size` to both
prepare and take so the normalized plan matches the selected capture settings.
After `editor_play`, `sloparena.moves.take --recipe R --move ID --output DIR`
arranges, settles and records a take. Optional `--views`, `--size`, `--quality`
and the Pipeline `--timeout 180` bound the request.
`sloparena.moves.test` checks parsing, coordinates, budget and phase anchors.

Use the gateway and declare restoration in this order.

```json
{"commands":[],"restore":[["sloparena.moves.abort"],["sloparena.photo.stop"],["sloparena.photo.restore"],["editor_status"]]}
```

`frames.jsonl` has one row for each frame, starting at zero. Each row carries the
Shared `matchTick`, player and dummy state, accepted hits, semantic cues, action
boxes and each camera's observed render tick. All views render after the same
input and Shared step. The final `take.json` is written only after all JPGs and
rows complete. Failed takes keep their partial evidence and cannot be overwritten.

Nyx's ground E recipes are neutral recovery takes anchored to startup rather than
hits. They have no legacy hit baseline. `real-q` deliberately includes the crow,
a form swap and the alternate follow-up to expose the perch and pop presentation.
Gameplay targeting aids retain their existing gameplay-camera orientation in v1.
