# Content factory

The content factory is the owner's board for short-form social clips. It runs on
alfred at `http://100.68.254.9:8765` and is reachable over Tailscale from the
phone and the desk only. It holds every idea, the owner's notes, each render
(take), the agent runs and the scout's reports. The clips themselves are made on
the studio desk, where the repository and the Unity Editor live.

Agents follow [the social-clips skill](../../skills/social-clips/SKILL.md). This
page covers using the board and operating the server.

## Using the board

| Tab | Shows |
| --- | --- |
| To review | Clips with a new take waiting for a verdict |
| Proposed | Ideas the designer suggested, not filmed yet |
| Approved | Ideas waiting for the next render run |
| Ready to post | Clips with a chosen take, each with a Download button |
| Posted, Rejected | History |
| Numbers | Views, likes and comments per platform and Steam visits for posted clips; TikTok and Shorts fill themselves |
| Scout | An inbox for links and thoughts, and the scout's reports |
| Runs | Every agent run, its state and its one-line result |

A clip's detail view plays its takes side by side and keeps the owner's and the
agents' notes as one history. Ready to post records the take on screen as the
chosen one (★).

These buttons start an agent run:

- **Propose clips** asks the designer for 5 to 7 new ideas. It builds the batch
  around your open requests in the Scout inbox ("a clip of the defensive
  options"), then reads the latest scout reports. It closes each request with a
  designer report that names the ideas answering it.
- **Render approved** renders every idea in Approved.
- **Redo with note** sends one clip back with the note as its brief. The new
  take lands in To review beside the old ones.
- **Run scout** turns the open inbox links into a report for the designer. It
  leaves requests for Propose.

Each run becomes one T3 thread named `Factory #<run>: <action>`, which the T3
phone app shows. The thread appears about two minutes after the press, because
the request travels through T3's relay. Until then the Running strip says
"waiting for T3". The relay holds requests while the desk is off and delivers
them when it is back.

A run closes with `factory.py run <run> done --made <ids>`, listing every idea it
added or gave a take and any `report:<n>` it wrote. The Runs tab links to those.

## Every posted video lives here

The board is the one place for social videos, including the ones made outside
it (trailers, HyperFrames edits, vertical cuts made in a thread). When such a
video is final, file it so it lands in Ready to post, where you download it:

```bash
python3 tools/factory.py file 39-some-edit tmp/<project>/renders/final.mp4 \
  --title "..." --format edit --source tmp/<project>
```

Add `--posted YYYY-MM-DD` when it is already up. `source` records the project
that made it, so a re-cut starts from the right files. Never hand over a final
only as a `tmp/` path or a thread attachment: `tmp/` gets pruned.

On the desk, the same videos sit as plain files in `~/Videos/SLOPArena-social/`
for uploading by hand (Instagram, TikTok, Shorts):

```
~/Videos/SLOPArena-social/
  ready/<title>.mp4            # chosen take, not posted yet
  posted/<date> <title>.mp4    # already up somewhere
```

`python3 tools/factory.py sync` refreshes it from the board (`file` runs it
too). Run it after pressing Mark posted on the phone; the clip moves from
`ready/` to `posted/`. The board stays the source: files you add to the folder
by hand are removed on the next sync.

### Where it is up, and the caption kit

A video in Ready to post or Posted shows a badge per platform on its card:
green where it is up, dashed where it is not yet. That is the checklist for
posting the same video everywhere. In the video's detail view, **Posted on**
takes a tick and an optional link per platform (TikTok, YouTube Shorts,
Instagram, X); saving any tick on a Ready video moves it to Posted. Agents
record the same thing with `factory.py posted <idea> <platform> [<link>]`.

A clip that only went out inside a longer video carries `usedIn: <idea>` and
gets no badges, no Numbers row and no file in the folder.

Under it, **Captions** holds the text to paste on each platform, with Copy
buttons. Whoever delivers a video for Ready to post also writes its kit with
`factory.py set <idea> '{"captions": {...}}'` (the shape is in the
`factory.py` docstring). House style:

- **YouTube Shorts:** `title` in the owner's style ("Manki Rocket in SLOPArena
  !"), `text` = one line on the video, then the pitch: "SLOPArena is a new F2P
  3D Platform fighter! Online rollback netcode, built 100% open source!
  Playtest now on Steam <store link>", then `#sloparena #shorts`.
- **TikTok:** one or two short lines that end on the question, then
  `#sloparena #indiegame #platformfighter #gamedev #indiedev`. TikTok captions
  have no clickable links, so the link stays out.
- **Instagram:** a hook line with one emoji, two or three lines on the video,
  the question, "Playtest on Steam: link in bio.", then the same hashtags.
- **X:** the hook and the question with no link or hashtags, and the store or
  site link in `reply`. A link in the first post hurts its reach.

Fighters shown before their reveal follow the same rules as the clip itself.

### Numbers that fill themselves

`python3 tools/factory.py views` reads the public profiles
(youtube.com/@SLOPArena/shorts and tiktok.com/@sloparena) with yt-dlp, which
needs no account or API key. It first links new posts to their video: a post
whose text shares most of its words with one video's title or caption kit gets
that link, so pasting the kit is enough. Then it stores each linked post's
views, likes and comments in `stats` and one row a day in `history`. The
Numbers tab shows those numbers read-only with a link to the post. Instagram,
X and Steam visits are still typed in.

A post it cannot match with confidence stays unlinked and is printed with the
command that links it (`factory.py posted <idea> <platform> <link>`); read
them with `journalctl --user -u sloparena-views -n 30`.

It runs on the desk every 6 hours from a systemd user timer (not in Git, since
it is personal machine setup):

```ini
# ~/.config/systemd/user/sloparena-views.service
[Service]
Type=oneshot
WorkingDirectory=/home/binoui/Documents/projects/SlopArena
Environment=PATH=/home/binoui/.local/bin:/usr/local/bin:/usr/bin:/bin
ExecStart=/usr/bin/python3 tools/factory.py views

# ~/.config/systemd/user/sloparena-views.timer
[Timer]
OnCalendar=00/6:15
Persistent=true

[Install]
WantedBy=timers.target
```

`systemctl --user enable --now sloparena-views.timer` turns it on. yt-dlp runs
as `uvx yt-dlp@latest`, so site changes that break it are fixed by upstream
releases without a reinstall.

## How it works

```
phone / desk browser ──tailnet──▶ alfred: content-factory (board, SQLite, clips)
                                        │ POST {action, run, id}
                                        ▼
                         T3 relay ──▶ desk: "Content factory run" task ──▶ one thread per run
                                                                              │
                         tools/factory.py ◀── reads ideas, uploads takes ─────┘
```

- `tools/content_factory/server.py` is the server and API (Python standard
  library). `index.html` is the board.
- `tools/factory.py` is the desk client that agents and the render tools use.
  Its docstring lists the commands.
- `tools/social_clip.py` and `tools/social_explainer.py` read an idea from the
  factory and upload each result as the next take.
- An idea moves `proposed` → `approved` → `rendered` → `ready` → `posted`, or
  to `rejected`. Only the owner's buttons and new takes change it.
- The T3 scheduled task "Content factory run" holds the agent prompt. Its
  webhook URL is a secret and stays out of the repository.

## Where things live

| What | Where |
| --- | --- |
| Container | `content-factory` on alfred, `python:3.13-alpine`, user 1000, port bound to the tailnet address only |
| Compose file and code | `~/content-factory/compose.yml` and `~/content-factory/app/` on alfred, copied from `tools/content_factory/` |
| Database | `~/content-factory/data/factory.db` on alfred |
| Clips, posters and contact sheets | `~/content-factory/data/media/<idea>/take-<n>.*` on alfred |
| Webhook URL | `~/content-factory/data/config.json` on alfred (mode 600) |
| Photo specs | `scenarios/social/specs/` in this repository |
| Desk scratch (probes, shoots, HyperFrames projects) | `tmp/content/<idea>/` in this checkout |

## Operating

Deploy after changing anything under `tools/content_factory/`:

```bash
tools/content_factory/deploy.sh
```

It copies the code and the font to alfred and recreates the container. The data
directory is never touched. Other checks:

```bash
ssh alfred 'docker logs --tail 50 content-factory'
curl -s http://100.68.254.9:8765/api/board | head -c 300
python3 tools/factory.py list
```

Docker's published ports bypass alfred's firewall, so keep the compose port
bound to `100.68.254.9`, never `0.0.0.0`.

The relay sits behind Cloudflare, which refuses Python's default User-Agent
(error 1010). The server sends its own.

## Known gaps

- Nothing backs up `~/content-factory/data` yet. It is the only copy of the
  notes, take history and clips.
- Each run also gets a T3 worktree on a new `t3/...` branch under
  `~/.t3/worktrees/SlopArena`. The agent works in the main checkout, so these
  worktrees are unused and need occasional cleanup.
- The analyst role (reading posted numbers to steer the designer) is not built.
  TikTok and Shorts numbers collect themselves; Instagram and X are typed in.
