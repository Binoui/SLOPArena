---
name: social-clips
description: "Propose, stage and render SLOPArena short-form clips (TikTok, YouTube Shorts, X) from the scenarios/social queue: vertical photo-mode shoots with the game's own sound, delivered to the owner for CapCut and manual posting."
metadata: {"invocation": "explicit"}
---

# Social clips

The owner posts one clip a day by hand, finishing TikTok clips in CapCut (music, text). This
workflow fills a ready folder ahead of that pace. It never posts, uploads to a platform, or
messages anyone but the owner. The owner triggers it ("propose clips", "render the approved
clips"); there is no schedule yet.

## The queue

One JSON file per idea in `scenarios/social/queue/<id>.json`. `python3 tools/social_clip.py list`
prints it. Status moves `proposed` → `approved` (owner picked it) → `rendered` (the tool sets it
with `clip`) → `ready` (owner accepted the clip) → `posted`; `rejected` at any point. Fields:

- `id` (file name), `status`, `format`: `moment` (one gameplay payoff, no explanation), `fighter`
  (one distinctive interaction of one fighter), `building` (a before/after or tool moment),
  `explainer` (a feature, teaser or design decision told in shots and text plates) or `shot` (a
  short clip made for an explainer, with `"for": "<explainer id>"`).
- `title`, `idea`: what happens on screen, one or two sentences.
- `spec`: a photo-mode spec in `scenarios/social/specs/`, plus the render fields documented at the
  top of `tools/social_clip.py` (`fovScale`, `camera`, `capture`, `end`, `hold`).

Tested landscape specs from the 2026-10-28 promo set live in `scenarios/social/specs/` (09 is the
owner's favourite). Reuse them before staging new ones.

## Dashboard

The owner reviews in a local web dashboard, not in threads: `python3 tools/social_dashboard.py`
(port 8765; it runs as the user service `sloparena-dashboard`). It shows the queue by status with
each clip playable, and writes the owner's verdicts and notes into the queue files: read `status`
and the dated lines in `feedback` before every round. Its "Propose clips" and "Render approved"
buttons start a T3 webhook task (URL in `~/.config/sloparena/social-dashboard.json`, never in the
repo). When a run finishes, the dashboard is where the owner sees it, so a run started from it
does not need to send attachments.

## Propose

1. Read what changed since the newest queue entry: `git log --since=<date> --stat`, new or
   changed packages under `client/Unity/Assets/CharacterPackages/`, stages, modes and recent
   `docs/adr/`. Skip work that is not playable in a PvP photo shoot.
2. Write 5 to 7 ideas as `proposed` queue files, mixing formats. Prefer the newest playable
   content and a clear payoff in the first second. Each idea names its fighters, stage and the
   exchange (lead-in, hit, payoff); point `spec` at an existing spec when one fits.
3. Stop. The owner approves or rejects in the dashboard; when asked in a thread instead, send a short
   numbered list (title, one-line idea, format).

Show only Sir Bonk, FightGuy, Manki and Wibou unless the owner approves a reveal of a newer fighter
in that idea. Manki Mayhem is not ready to show.

## Render an approved idea

1. If the idea has no spec, stage one in `scenarios/social/specs/<id>.json`, starting from the
   nearest tested spec. Follow "Video clips" in `docs/steam/store-screenshots.md` and the rules below.
2. `python3 tools/social_clip.py make <idea> --probe` (about 15 s of Editor time) and read
   `tmp/content/<id>/probe.sheet.jpg`. Adjust the spec or the idea's render fields and probe again.
   Earlier probes move aside to `probe-1`, `probe-2`, ...
3. `python3 tools/social_clip.py make <idea>` for the 1080x1920 60 fps clip with the game's
   sound. Check the result, not the run's success flag: read the contact sheet, then
   `ffprobe` size, pixel format and duration.
4. Send the MP4 to the owner's thread as a T3 attachment, with one line on what happens. The
   owner's verdict sets `ready`, a new round, or `rejected`.

The gateway waits for the shared Editor by itself. Redirect its output to files (the tool does);
if status is `blocked`, stop and report. Never recover or restart the Editor.

## Explainers, teasers and design decisions

The owner wants one for each new feature, upcoming fighter and big design decision, ending on a
question that invites comments ("Fair or broken?", "Name her?", "Which would you play?").

1. Render each shot as its own `shot` entry (2.5 to 4 s, one beat each), as above.
2. Write the explainer's `beats` (documented at the top of `tools/social_explainer.py`): shots,
   images (concept art, stills) and text cards. Frame 0 is the thumbnail and the hook: open on a
   shot with its plate at `at` 0, never on a card. Five words or fewer per line; a beat every 2 to
   4 s; 10 to 20 s in total.
3. `python3 tools/social_explainer.py <explainer> --snapshot` and read the contact sheet under
   `tmp/content/<id>/hf/.hyperframes/artifacts/snapshots/`; then run it without `--snapshot` to
   render `tmp/content/<id>/<id>-vertical.mp4` (60 fps, -14 LUFS, game sound only) and deliver as above.

## Staging rules

- One move fills about 2.5 s, so script a short exchange: lead-in, hit, payoff. Action starts
  in the first 0.5 s and nothing stands still for more than about 1 s.
- A scripted press lands only on the first tick of its input step; a press during another move
  is dropped, not buffered. Hold-to-aim specials (Bazooka, Ki Shot, Round Bomb) fire on release.
- A KO'd fighter returns to its spawn on the KO tick. Keep shooting past it (`capture.to`) so
  the audio runs on, but set `end` before the KO tick and `hold` about 0.6 s.
- Photo mode treats everyone as an enemy: fighters within 4 m turn toward each other, so
  lineups need about 4.3 m spacing.
- Manki's Aerosol Inferno turns toward the nearest fighter within 4 m and ignores `target`; his
  Bazooka homes on the nearest target and can hit Manki within about 3 m.
- Sir Bonk's sword and a pauldron float detached on tick 0: start captures at tick 1 or later.
  The BONK only cancels early on a shield press, which flashes the shield for one frame.
- Cameras move only by following (`follow`, `move`, `ease`); there are no keyframes. `follow`
  takes one fighter id or a list: a list aims at their midpoint, and `spread` (with `move`) pulls
  back by that many metres per metre between them. Use the midpoint for exchanges, where portrait
  framing otherwise loses one fighter; follow the victim alone for launches and flights.
- Portrait framing from landscape specs: `fovScale` about 1.1, the look point moved toward the
  victim and up (`lookOffset`), and `ease` about 0.2 so launches stay in frame.
- Fighters read at phone size: the main fighter is about a quarter of the frame height at the
  key moment. Duels side-on; Sir Bonk hides smaller fighters from the front.

## Not supported yet

Live Slopball, course replays, HUD composites and Manki Mayhem do not go through
`social_clip.py`. A longer reel with music beats can reuse the HyperFrames trailer projects in
`tmp/promo-20261028/trailer*` (local); ask first.
