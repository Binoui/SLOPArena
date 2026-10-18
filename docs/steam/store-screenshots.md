# Store screenshots

The Steam store page uses ten in-game captures made with the Editor-only photo mode
(`sloparena.photo.*`). Steam's screenshot section must show real gameplay: no concept art,
pre-rendered stills, awards or added marketing text
([Steamworks graphical assets](https://partner.steamgames.com/doc/store/assets/standard)).
Staging the fight, placing the camera and choosing the frame are fine.

## Current set

Shot 2026-10-18; 01, 02, 07 and 08 were re-shot the same day with the new portraits and the SLOPArena billboard. The upload set, with the store and library art, is built by [`tools/key_art`](../../tools/key_art/README.md). The recipes in [`scenarios/store/`](../../scenarios/store/) reproduce each
shot; `pick` records the chosen camera, tick and crop.

| # | Stage | Moment |
| --- | --- | --- |
| 01 | After Hours | Manki's Bazooka launches FightGuy; Manki's grin rises from the smoke under the SLOPArena billboard |
| 02 | Splash Deck | Bonk's ground 4 launches Wibou at 142% past the NO FIGHTING ON DECK sign, HUD on |
| 03 | Slop Pit | All four fighters: Rising Dragon on Wibou, Aerosol Inferno on Blade Storm |
| 04 | Last Stop | Manki's Jetpack Boost out of the gap beside the train, Wibou's kunai in the air |
| 05 | Log Off | Wibou's Skyfall on FightGuy in front of the NO WI-FI, JUST LOGS sign |
| 06 | Table Manners | Bonk's Jump Slam on the play-dough cake sends Manki toward the bunk bed |
| 07 | Slopball | Manki's Bazooka meets a live serve on the Night Court, HUD on |
| 08 | Sanctuary course | The last lantern breaks: ALL CLEAR!, 20/20 and the gold shrine pillar |
| 09 | Splash Deck | Wibou's Rising Slash catches Manki over the harbour town |
| 10 | Slop Pit | FightGuy's Fist of Fury finisher launches Bonk with the gold 肉 seal |

Order matters: Steam shows the first screenshots when players hover over the game in lists.
Re-shoot a recipe after a stage, fighter look, effect or HUD change; each runs in minutes.

## Re-shooting

```bash
python3 tools/photo_shoot.py batch scenarios/store/02-splash-deck-bonk.json \
    --output tmp/store/runs/02 > tmp/store/02.json
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch tmp/store/02.json
python3 tools/photo_shoot.py sheet tmp/store/runs/02 --camera p1 --hud --out tmp/store/02-sheet.jpg
python3 tools/photo_shoot.py final tmp/store/runs/02 --camera p1 --tick 108 --out tmp/store/final/02.png
```

`final` composites the HUD layer when the run has one, applies `--crop left,top,width`
(fractions, 16:9 box) and downscales to 1920×1080 by default. Live runs (Slopball and the
course) write the composited Game View to `<run>/NNNN.jpg` or `<run>/frames/`; export those
with `final --image <file>`.

## Writing a shot

**PvP stages.** `sloparena.photo.prepare --stage <key>`, `editor_play`, then
`sloparena.photo.shoot --spec <json> --output <fresh dir>`; restore with
`sloparena.photo.stop` and `sloparena.photo.restore`. The spec places up to four fighters
(`package`, `feet`, `grounded`, `yaw`, `damage`, `deaths`, `target`) and scripts their
`input` per tick (`at` or `from`/`to`, `press` a canonical slot, `aim`, `jump`, `down`,
`shield`, `grab`, `move` and `face` as world yaw in degrees). Cameras are free (`pos`,
`look`, `fov`, `roll`, `ortho`) or follow a fighter (`follow`, `lookOffset`, `move`, `ease`).
`capture` sets the tick window, step, size (up to 3840×2160) and JPG quality; `hud` renders
the real match HUD for one local fighter into a transparent layer. One tick runs per
rendered frame, so frame N is the state after N ticks. Each run also writes `states.jsonl`
and `survey.json` (collision bounds, kill lines, spawns, camera poses and the bounds of
stage objects matching `landmarks`). The shoot switches to the Ultra preset and restores
the previous one.

**Slopball.** `sloparena.photo.prepare --stage slopball --fighter <id>`, `editor_play`,
`sloparena.ui.viewport --width 3840 --height 2160`, then `sloparena.photo.live` with
`--steps` (Training step JSON) or `--intercept`, and a free `--camera` or a held
`--yaw`/`--pitch`. Serves use a new random seed every attempt, so fixed step lists miss.
`--intercept` predicts the ball's free flight with Shared's drag and gravity, walks the
fighter to a spot offset from where the ball descends, then times and aims a projectile
special at it; the capture window follows the predicted impact.

**Character Course.** Run the course replay (`sloparena.replay.*`, see
[course replay](../testing/course-replay.md)) and wrap the chunk that contains the moment
in `sloparena.photo.live-begin` / `sloparena.photo.live-end`. `photo_shoot.py batch` builds
this from a recipe with `inputs`, `window` and `camera`.

What worked: survey the stage first (a spec with no fighters and a top orthographic camera,
`"ortho": 30, "pos": [0, 150, 0], "up": [0, 0, 1]`, then `photo_shoot.py grid` draws world
X/Z lines and spawns), test at 960×540 every 2–3 ticks from three or four cameras with
contact sheets, then shoot a narrow window at 3840×2160 from the chosen cameras.

## Pitfalls

- A fighter placed in the air starts past its FloatWindow. Its zero float gravity would
  otherwise hold it in place; the shoot handles this.
- Arcade scenes ignore input after Go until they see one neutral frame, so scripted live
  input starts neutral.
- Hold-to-aim specials (Bazooka, Ki Shot, Round Bomb) fire on release: hold `aim` for the
  charge ticks.
- Keep the hero above the move deck, which sits bottom centre in HUD shots.
- Bonk hides the smaller fighters from the front; frame his duels side-on.
- Another thread's script edit makes the gateway recompile first, and a claim can be lost
  during the reload. Check `status` and rerun; nothing ran.
