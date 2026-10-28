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
  (one distinctive interaction of one fighter) or `building` (a before/after or tool moment).
- `title`, `idea`: what happens on screen, one or two sentences.
- `spec`: a photo-mode spec in `scenarios/social/specs/`, plus the render fields documented at the
  top of `tools/social_clip.py` (`fovScale`, `camera`, `capture`, `end`, `hold`).

Tested landscape specs from the 2026-10-28 promo set live in `scenarios/social/specs/` (09 is the
owner's favourite). Reuse them before staging new ones.

## Propose

1. Read what changed since the newest queue entry: `git log --since=<date> --stat`, new or
   changed packages under `client/Unity/Assets/CharacterPackages/`, stages, modes and recent
   `docs/adr/`. Skip work that is not playable in a PvP photo shoot.
2. Write 5 to 7 ideas as `proposed` queue files, mixing formats. Prefer the newest playable
   content and a clear payoff in the first second. Each idea names its fighters, stage and the
   exchange (lead-in, hit, payoff); point `spec` at an existing spec when one fits.
3. Send the owner a short numbered list (title, one-line idea, format) and stop. Their reply sets
   `approved` or `rejected`.

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
- Cameras move only by following a fighter (`follow`, `move`, `ease`); there are no keyframes.
- Portrait framing from landscape specs: `fovScale` about 1.1, the look point moved toward the
  victim and up (`lookOffset`), and `ease` about 0.2 so launches stay in frame.
- Fighters read at phone size: the main fighter is about a quarter of the frame height at the
  key moment. Duels side-on; Sir Bonk hides smaller fighters from the front.

## Not supported yet

Live Slopball, course replays, HUD composites and Manki Mayhem do not go through
`social_clip.py`. Bigger pieces (a weekly reel, a fighter reveal) can reuse the HyperFrames trailer
projects in `tmp/promo-20261028/trailer*` with these clips as footage; ask first.
