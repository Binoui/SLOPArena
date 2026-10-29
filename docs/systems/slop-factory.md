# File and answer human checks

Slop Factory has **Clips** for social videos, **Checks** for human playtests, and a
**Console** link to `/console/`. Open the factory board on the phone or desk.
Checks stay in To try, Redo, Good, or Dropped until someone moves them.

## File a check

Write short Markdown steps that name where to go, what to do, and what to look
for. Merge overlapping requests. Keep automated evidence and capture logs in task
or PR reports. Attach only useful `.mp4`, `.jpg`, or `.png` previews.

In T3, call `t3_thread_configuration` without arguments to get your `threadId`.
Pass that id explicitly. The CLI looks up its title with `t3 thread list --json`.
Outside T3, choose a known thread or explicitly use `--no-thread`.

```sh
python3 tools/factory.py check add --title "Bashie walk stays grounded" \
  --steps tmp/bashie-check.md --area Bashie --thread <thread-id> \
  --clip tmp/bashie-walk.mp4 tmp/bashie-walk.jpg
python3 tools/factory.py check list --status redo
python3 tools/factory.py check get 12
python3 tools/factory.py check note 12 "The floor offset is adjusted."
python3 tools/factory.py check attach 12 tmp/after.mp4
python3 tools/factory.py check ready 12 --note "Adjusted the floor offset."
python3 tools/factory.py check drop 13 --note "Superseded by check 12."
```

`--steps -` reads stdin. Retire your own superseded checks with `check drop`.
After fixing a Redo, answer it with `check ready`; this moves it back to To try.
Only the owner records acceptance. A thread chip shows its title and copies its
id; browser links cannot open T3 threads.

## Deliver owner feedback from the desk

The owner marks Good, Redo, or Dropped on the board. Redo requires a note. Verdicts
on linked checks queue a message to the thread. Plain owner notes have a **Send
note to thread** checkbox, enabled by default when linked. The note shows
**Waiting for desk**, **Sent to thread**, or the delivery error. Agent notes stay
on the board. The owner can reopen Good and Dropped checks.

Run one relay on the desk where T3 is available. The server on alfred only holds
the outbox. Busy T3 threads receive the message as their next turn.

```sh
python3 tools/factory.py relay
python3 tools/factory.py relay --once
python3 tools/factory.py relay --interval 20
```

The relay uses `T3_CLI`, then `T3CODE_CLI_PATH`, then `t3`. Failed deliveries stay
queued for retry. Delivery is at least once; interruption after T3 accepts a
message but before its acknowledgement can repeat that message. Run only one
relay. `SLOPARENA_FACTORY` or `~/.config/sloparena/factory.json` selects the server,
as for Clips. For local verification, point the factory URL at the test server
and `T3_CLI` at a fake binary before running the relay.

## Import a prepared backlog

Review a JSON list with `title`, `steps`, `area`, nullable `thread`, and `clips`.
Each clip is an existing absolute path on the desk. Import creates new To try
checks and uploads the files. It does not deduplicate; run an import once.

```sh
python3 tools/factory.py check import tmp/checks-import/checks.json
```

The existing `<data>/factory.db` stores `checks` and `check_notes`. Attachments
live under `<data>/media/check-<id>/`. Verdict notes retain their original status
in the body so later moves cannot change an outcome waiting for the relay.

## Run it on alfred

`tools/content_factory/deploy.sh` copies the app to `~/content-factory` on alfred
and starts four containers. `front` is Caddy and owns the only published port,
`100.68.254.9:8765` on the tailnet. It sends `/console/` to the Master console and
everything else to `factory`. `console` runs the `SlopArena-console` image in hosted
mode, built on alfred from that checkout's committed `HEAD` (`CONSOLE_REPO`
overrides the path). `tunnel` holds an SSH forward to Master's private address on
the VPS under the network alias `master`, so the console's fixed
`http://master:8080` reaches it.

The console trusts only requests that carry Caddy's `X-Console-Gateway` token, and
only the console reads the Master key. Provision `~/content-factory/secrets` once,
each file mode 600 and owned by alfred's uid 1000:

- `tunnel_key` and `tunnel_key.pub`: an ed25519 key generated on alfred. Its VPS
  `authorized_keys` line is
  `restrict,port-forwarding,permitopen="<master-ip>:8080",command="/usr/sbin/nologin" ssh-ed25519 ... alfred-console-tunnel`.
- `known_hosts`: the VPS host key from the desk's `known_hosts`.
- `master_console_key`: the dedicated console key.
- `console_gateway_token` and `caddy.env` (`CONSOLE_GATEWAY_TOKEN=<same token>`):
  32 random bytes as hex.

After a VPS replacement, recheck Master's private address. Update the
`permitopen` line and set `MASTER_PRIVATE=<ip>:8080` in `~/content-factory/.env`.
