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
| Numbers | Views per platform and Steam visits for posted clips |
| Scout | An inbox for links and thoughts, and the scout's reports |
| Runs | Every agent run, its state and its one-line result |

A clip's detail view plays its takes side by side and keeps the owner's and the
agents' notes as one history. Ready to post records the take on screen as the
chosen one (★).

These buttons start an agent run:

- **Propose clips** asks the designer for 5 to 7 new ideas. It reads the latest
  scout reports first.
- **Render approved** renders every idea in Approved.
- **Redo with note** sends one clip back with the note as its brief. The new
  take lands in To review beside the old ones.
- **Run scout** turns the open inbox items into a report for the designer.

Each run becomes one T3 thread named `Factory #<run>: <action>`, which the T3
phone app shows. The thread appears about two minutes after the press, because
the request travels through T3's relay. Until then the Running strip says
"waiting for T3". The relay holds requests while the desk is off and delivers
them when it is back.

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
  The numbers are typed in by hand.
