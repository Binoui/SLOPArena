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
[docs/systems/content-factory.md](../../docs/systems/content-factory.md) covers the board, data
locations and deploy.

- An idea has `id`, `status`, `format`: `combo` (a Finder fight with input tapes and verified
  camera footage), `moment` (one gameplay payoff, no explanation), `fighter`
  (one distinctive interaction of one fighter), `building` (a before/after or tool moment),
  `explainer` (a feature, teaser or design decision told in shots and text plates) or `shot` (a
  short clip made for an explainer, with `"for": "<explainer id>"`); `title`, `idea` (what happens
  on screen, one or two sentences), and for clips a `spec` in `scenarios/social/specs/` plus the
  render fields at the top of `tools/social_clip.py` (`fovScale`, `camera`, `capture`, `end`, `hold`).
- Status: `proposed` → `approved` (owner) → `rendered` (a new take arrived) → `ready` (owner picked
  a take, also from `approved` when an earlier take is still wanted) → `posted`; `rejected` at any point. Only the owner's buttons and new takes move it:
  `factory.py set` changes fields, never the status.
- Each render is a take; earlier takes stay beside it. Comments hold the owner's dated notes; the
  newest one is the brief for a redo. Add your own with `factory.py comment` when a choice needs
  explaining.
- Every button press is a run: one T3 thread from the "Content factory run" webhook task, which
  marks it `running` and `done` or `failed` with a one-line summary. `done` also lists what the run
  made (`--made`: every idea it added or gave a take, shots included, and `report:<n>`), which the
  board's Runs tab links to. Results reach the owner on the board, so runs do not send attachments.

Every video meant for posting ends up on the board, also one made outside this workflow (a
trailer, a HyperFrames edit, a vertical cut asked for in a thread): file the final with
`factory.py file <id> <final.mp4> --title ... --format ... --source <project dir>` so it lands in
Ready to post and in `~/Videos/SLOPArena-social/ready/` on the desk (`factory.py sync` refreshes
that folder). A `tmp/` path or a thread attachment alone is not a delivery.
Deliver the caption kit with it (`factory.py set <idea> '{"captions": ...}'`, house style in
[the factory doc](../../docs/systems/content-factory.md#where-it-is-up-and-the-caption-kit)): every
explainer, moment or edit you give a take or file gets one.

Tested landscape specs from the 2026-10-28 promo set live in `scenarios/social/specs/` (09 is the
owner's favourite). Reuse them before staging new ones.

## Checks

Slop Factory also holds human gameplay checks beside Clips, with a Console link.
File checks with `python3 tools/factory.py check add`, retire your own superseded
checks with `check drop`, and answer redo notes with `check ready`. Pass your T3
thread id from `t3_thread_configuration` when you are in T3. Owner notes reach the
linked thread through the desk relay. See [Slop Factory checks](../../docs/systems/slop-factory.md).

## Propose

1. Read the open inbox (`factory.py inbox`). Every item that asks for a clip, a theme or a change
   of direction is an owner request, and requests come first: they are the owner's videos.
2. Read the latest scout reports (`factory.py reports --limit 3`) and what changed since the newest
   idea: `git log --since=<date> --stat`, new or changed packages under
   `client/Unity/Assets/CharacterPackages/`, stages, modes and recent `docs/adr/`. Skip work that
   is not playable in a PvP photo shoot.
3. Add 5 to 7 ideas with `factory.py add` (status `proposed`). Answer every request first, with two
   or three different takes on it when it is broad (separate clips, or an explainer with its shots);
   a request that sets a theme for the batch makes the whole batch that theme. Fill the rest with a
   mix of formats, preferring the newest playable content and a clear payoff in the first second.
   Each idea names its fighters, stage and the exchange (lead-in, hit, payoff); point `spec` at an
   existing spec when one fits. When a request cannot be shot yet (see "Not supported yet"), say so
   instead of swapping in something else.
4. Close the requests with `factory.py report designer <file> --inbox <ids>`: one line per request,
   naming the ideas that answer it. Add the report to the run's `--made`.
5. Stop. The owner approves or rejects on the board.

Show only Sir Bonk, FightGuy, Manki and Wibou unless the owner approves a reveal of a newer fighter
in that idea; an owner request that names a newer fighter counts as that approval. Manki Mayhem is
not ready to show.

## Render or redo an idea

1. Read the idea and its comments (`factory.py get <idea>`). For a redo, the newest owner comment
   says what to change and the earlier takes show what was tried.
   When the note asks for more clips of the same thing, make each as its own idea and also join
   them with the original into one explainer (below), so the owner gets both the parts and the
   longer video.
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

## Combo clips

A `combo` idea embeds a version-1 Finder result under `combo`. Its deterministic input
tapes let photo mode replay the fight from several angles without human players.
The reveal rule above still applies.

1. Find fights with `tools/ComboFinder` (`search` for one matchup, `sweep` for a shelf; its
   README lists the options). Every stage start layout is searched; edge layouts find most
   KOs. Keep each combo JSON whole. Review the landed string and KO before proposing.
2. `python3 tools/combo_clip.py propose <combo.json>...` first replays each combo in Unity
   with one tiny camera (`check`, about 20 s) and skips any whose hits or KO differ: Unity's
   Mono and the headless .NET run can part ways on some moves (a Wibou run attack turned
   0.08° apart on 2026-11-02), and only Unity's version can be filmed. Passing combos become
   proposed ideas; the summary is the title and idea text, and the owner approves on the board.
   A long sweep can run as a detached service, but run `check`, `propose` and `record` from the
   agent session (`systemd-run --user --scope`): the gateway refuses callers without an agent identity.
3. For a render run, read the idea and newest owner comment. Record with
   `python3 tools/combo_clip.py record <idea>` if
   `tmp/content/<idea>/footage/portrait/pack.json` is missing, unverified, stale, or lacks
   a requested angle. The tool asks ComboFinder for a camera plan (side, far-side, behind,
   wide, kill) and shoots one angle per gateway pass, about 3 minutes for all five.
   Any differing hit or KO fails the recording. Stop on a blocked gateway.
4. `python3 tools/combo_clip.py edit <idea>` builds and uploads the take and contact sheet.
   Default cuts show side, then kill, ending before the KO respawn with a 0.6-second hold.
   Each cut uses its own pass's game audio, normalized in two passes to -14 LUFS / -2 dBTP.
   Inspect the contact sheet and `ffprobe` result. Deliver the caption kit as above.
5. A redo means recut first. Set the idea's `edit` to a cut list, or pass
   `--cuts '[{"angle":"side","to":200},{"angle":"kill"}]'`.
   Bounds are inclusive simulation ticks. Cuts continue from the previous cut's end.
   A cut's `"speed": 0.5` (0.5 to 1) slows it, picture and sound, for a slow-motion finisher;
   split the angle into cuts around the hit to slow only that beat.
   Reuse the verified pack unless the note needs another camera or the content changed.

Portrait is the default. Use `record --orientation portrait,landscape` for both packs,
then `edit --orientation landscape` for a landscape take. A camera override file covers
one orientation only. `edit --no-upload` keeps a test local. Stale combos need a fresh
Finder search; recutting or relabeling their hashes cannot make them current.

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
could do it. The scout reports; it does not add ideas. Leave owner requests (an item asking for a
clip or a theme, not sharing something to learn from) open: Propose answers them.

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
