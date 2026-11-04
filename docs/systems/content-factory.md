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
Instagram, Threads, X); saving any tick on a Ready video moves it to Posted. Agents
record the same thing with `factory.py posted <idea> <platform> [<link>]`.

Posting stays by hand; **Post everywhere** on a Ready video makes it quick. It
opens the upload page of every platform not ticked yet (X and Threads with the
caption already typed in; Threads uses the X text when the kit has no Threads
entry). On the desk it also opens `factory.py drag <idea>`: a small window
holding the clip file, to drag into each tab, since a web page cannot hand a
file to another site. The browser needs pop-ups allowed for the board once, and
the drag window needs a URL handler on the desk (personal setup, not in Git):

```ini
# ~/.local/share/applications/sloparena-post.desktop
[Desktop Entry]
Type=Application
Name=SLOPArena post
Exec=python3 /home/binoui/Documents/projects/SlopArena/tools/factory.py drag %u
MimeType=x-scheme-handler/sloparena-post;
NoDisplay=true
```

`xdg-mime default sloparena-post.desktop x-scheme-handler/sloparena-post` and
`update-desktop-database ~/.local/share/applications` register it; without the
second, Chrome's link opener says no app is available. The drag window uses GTK 4 from Python (`python-gobject`).

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

`python3 tools/factory.py views` finds every post on each platform, links new
ones to their video, and stores each linked post's numbers in `stats` (views,
likes, comments, plus `reach`, `saves`, `shares`, `reposts`, `watch_s` and
`watch_pct` where the platform gives them) and one row a day in `history`.

- **Instagram, Threads and YouTube** come from their official APIs through
  `tools/social_api.py`, once it is logged in (setup below). Until YouTube is,
  Shorts fall back to yt-dlp.
- **TikTok** comes from yt-dlp's public numbers (youtube.com/@SLOPArena/shorts
  and tiktok.com/@sloparena need no account).
- **X and Steam visits** are typed in on the Numbers tab.

A post is linked to the video whose title or caption kit for that platform
shares most of its words (Threads also tries the X text), so pasting the kit is
enough. A post it cannot match with confidence shows under **Posts to link** on
the Numbers tab, with a clip picker. **Fetch now** there asks the desk to run
`views` straight away: the relay (below) starts `sloparena-views.service`, and
the run's one-line summary appears beside the button.

#### One-time platform setup

Reading your own accounts needs no app review. Keys and tokens live in
`~/.config/sloparena/social.json` on the desk (0600, never in Git).

1. **Meta app** (developers.facebook.com → My Apps → Create app, "SLOPArena
   stats", no business portfolio). Use cases: *Manage messaging & content on
   Instagram* and *Access the Threads API*. @sloparena must be a Creator or
   Business account.
   - Instagram → API setup with Instagram login: note the **Instagram app ID
     and secret**; in Business login settings add the redirect URL
     `https://alfred.tail026bc8.ts.net/oauth`.
   - Threads → Settings: note the **Threads app ID and secret**, add the same
     redirect URL; under Permissions add `threads_manage_insights`.
   - App roles → Roles → Add people: @sloparena as **Instagram Tester** and as
     **Threads Tester**. Accept both: instagram.com → Settings → Website
     permissions → Apps and websites → Tester invites; threads.com → Settings →
     Account → Website permissions → Invites.
2. **Google Cloud project** "SLOPArena stats": enable *YouTube Data API v3* and
   *YouTube Analytics API*; Google Auth Platform → Branding (name, email),
   Audience → External and **Publish app** (a Testing app's login expires after
   7 days); Clients → Create client → **Desktop app** → download the JSON.
3. On the desk, log in once each (the browser opens; on Google, sign in with the
   channel's account and accept the "unverified app" warning, it is your own):

```sh
python3 tools/social_api.py login instagram --app-id <id> --secret-file <file>
python3 tools/social_api.py login threads --app-id <id> --secret-file <file>
python3 tools/social_api.py login youtube --client-json <downloaded.json>
python3 tools/social_api.py status
```

Meta tokens last 60 days and every `views` run refreshes them, so they only
lapse if the timer stops for two months; then log in again.

It runs on the desk every 6 hours from a systemd user timer (not in Git, since
it is personal machine setup):

```ini
# ~/.config/systemd/user/sloparena-views.service
[Unit]
Description=SLOPArena: read post numbers into the Slop Factory

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
- `tools/combo_clip.py` handles `format: "combo"` ideas with an embedded Finder combo.
  `record` verifies each camera pass and keeps a footage pack under
  `tmp/content/<idea>/footage/<orientation>/`. `edit` recuts that pack and uploads a take.
  Redo notes normally need only a recut. Content changes require a fresh Finder combo.
  See [Combo clips](../../skills/social-clips/SKILL.md#combo-clips).
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
  Every platform but X collects its own numbers (Instagram, Threads and YouTube
  once `tools/social_api.py` is logged in); X and Steam visits are typed in.
