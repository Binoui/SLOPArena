---
name: social-clips
description: "Propose, stage and render SLOPArena short-form clips (TikTok, YouTube Shorts, X) through the content factory on alfred: vertical photo-mode shoots with the game's own sound and explainers, reviewed by the owner on the board and posted by hand."
metadata: {"invocation": "explicit"}
---

# Social clips

The owner posts one clip a day by hand, from the phone or the desk, finishing TikTok clips in
CapCut (music, text). This workflow keeps a ready shelf ahead of that pace. It never posts, uploads
to a platform, or messages anyone but the owner.

## The factory

Ideas, the owner's comments, takes, runs and scout reports live in the content factory on alfred
(`tools/content_factory/`, http://100.68.254.9:8765 on the tailnet). The owner reviews there, on
the phone or at the desk, not in threads. Read and write it with `python3 tools/factory.py` (its
docstring lists the commands); never keep idea state in the repo.

- An idea has `id`, `status`, `format`: `moment` (one gameplay payoff, no explanation), `fighter`
  (one distinctive interaction of one fighter), `building` (a before/after or tool moment),
  `explainer` (a feature, teaser or design decision told in shots and text plates) or `shot` (a
  short clip made for an explainer, with `"for": "<explainer id>"`); `title`, `idea` (what happens
  on screen, one or two sentences), and for clips a `spec` in `scenarios/social/specs/` plus the
  render fields at the top of `tools/social_clip.py` (`fovScale`, `camera`, `capture`, `end`, `hold`).
- Status: `proposed` → `approved` (owner) → `rendered` (a new take arrived) → `ready` (owner picked
  a take) → `posted`; `rejected` at any point. Only the owner's buttons and new takes move it:
  `factory.py set` changes fields, never the status.
- Each render is a take; earlier takes stay beside it. Comments hold the owner's dated notes; the
  newest one is the brief for a redo. Add your own with `factory.py comment` when a choice needs
  explaining.
- Every button press is a run: one T3 thread from the "Content factory run" webhook task, which
  marks it `running` and `done` or `failed` with a one-line summary. Results reach the owner on
  the board, so runs do not send attachments.

Tested landscape specs from the 2026-10-28 promo set live in `scenarios/social/specs/` (09 is the
owner's favourite). Reuse them before staging new ones.

## Propose

1. Read the latest scout reports (`factory.py reports --limit 3`) and what changed since the newest
   idea: `git log --since=<date> --stat`, new or changed packages under
   `client/Unity/Assets/CharacterPackages/`, stages, modes and recent `docs/adr/`. Skip work that
   is not playable in a PvP photo shoot.
2. Add 5 to 7 ideas with `factory.py add` (status `proposed`), mixing formats. Prefer the newest
   playable content and a clear payoff in the first second. Each idea names its fighters, stage and
   the exchange (lead-in, hit, payoff); point `spec` at an existing spec when one fits.
3. Stop. The owner approves or rejects on the board.

Show only Sir Bonk, FightGuy, Manki and Wibou unless the owner approves a reveal of a newer fighter
in that idea. Manki Mayhem is not ready to show.

## Render or redo an idea

1. Read the idea and its comments (`factory.py get <idea>`). For a redo, the newest owner comment
   says what to change and the earlier takes show what was tried.
2. If the idea has no spec, stage one in `scenarios/social/specs/<id>.json`, starting from the
   nearest tested spec. Follow "Video clips" in `docs/steam/store-screenshots.md` and the rules below.
3. `python3 tools/social_clip.py make <idea> --probe` (about 15 s of Editor time) and read
   `tmp/content/<id>/probe.sheet.jpg`. Adjust the spec, or the idea's render fields with
   `factory.py set`, and probe again. Earlier probes move aside to `probe-1`, `probe-2`, ...
4. `python3 tools/social_clip.py make <idea>` shoots the 1080x1920 60 fps clip with the game's
   sound and uploads it as the next take. Check the result, not the run's success flag: read the
   contact sheet, then `ffprobe` size, pixel format and duration. If it is wrong, fix and make again.

The gateway waits for the shared Editor by itself. Redirect its output to files (the tool does);
if status is `blocked`, stop, mark the run failed and say why. Never recover or restart the Editor.

## Explainers, teasers and design decisions

The owner wants one for each new feature, upcoming fighter and big design decision, ending on a
question that invites comments ("Fair or broken?", "Name her?", "Which would you play?").

1. Add each shot as its own `shot` idea (2.5 to 4 s, one beat each, `for` the explainer) and
   render it as above.
2. Set the explainer's `beats` with `factory.py set` (documented at the top of
   `tools/social_explainer.py`): shots, images (concept art, stills) and text cards. Frame 0 is the
   thumbnail and the hook: open on a shot with its plate at `at` 0, never on a card. Five words or
   fewer per line; a beat every 2 to 4 s; 10 to 20 s in total.
3. `python3 tools/social_explainer.py <explainer> --snapshot` and read the contact sheet under
   `tmp/content/<id>/hf/.hyperframes/artifacts/snapshots/`; then run it without `--snapshot` to
   render (60 fps, -14 LUFS, game sound only) and upload the take.

## Scout

The owner drops links and thoughts in the board's Scout inbox (`factory.py inbox`). Read each one
(open a link's page or oEmbed for its title, author and format; say so when a page cannot be read),
then write one short report for the designer with `factory.py report scout <file> --inbox <ids>`:
the hooks, formats and timings worth borrowing, each tied to a SLOPArena fighter or feature that
could do it. The scout reports; it does not add ideas.

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
