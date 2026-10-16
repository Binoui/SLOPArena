---
name: sloparena-ops
description: "Investigates SLOPArena Steam/VPS readiness, connection failures, lag and match logs. Use when the user asks for logs, reports frozen or snapping fighters, or asks whether the servers are up. Alfred is an explicitly selected historical alternative."
---

# SLOPArena operations

## Current target and authorization

Steam Playtest uses the `sloparena` VPS alias and
`https://master-test.sloparena.barakaslurp.fr`. Resolve the approved game
checkout, then use its [VPS runbook](../../deploy/vps/README.md) and
[troubleshooting guide](../../docs/systems/troubleshooting.md). Run repository
commands from that checkout. Historical releases are evidence, not live status.

A request to investigate authorizes read-only log and diagnostic requests.
Use existing access; do not ask again for an approval already given. Service
replacement belongs to [the release workflow](../sloparena-build-export/SKILL.md).
Do not restart a healthy GameHost or interrupt a match to investigate lag.

## Start with the match

1. Identify the approximate UTC time, match ID, client version and symptom.
   MasterConsole lists recent matches and their start/end times. A completed
   match can still have transport or presentation problems.
2. In the local MasterConsole (`http://127.0.0.1:4173`), choose **Matches →
   a match → Pull match logs**. Use **Download diagnostics JSON** to preserve
   the captured snapshot. Pull again to refresh an active match.
3. Inspect server tick work, scheduling delays/resets, timestamped peer RTT and
   receive/send quality, plus lifecycle/connection/error events. The simulation
   budget is 1000/60 ms per tick. Steam quality estimates are not a packet-loss
   percentage; missing samples are unknown, not healthy.
4. Correlate the snapshot with the GameHost and client logs below. Tick overruns
   suggest server work/scheduling; poor peer measurements suggest transport.
   Normal server measurements do not rule out client frame pacing, prediction or
   hit presentation. During play the GameHost logs `Inputs entity N:` lines (late,
   held and neutral ticks, minimum slack) and the client `Player.log` has `[Netplay]`
   lines (skipped/extra-step updates, lead, corrections, raw playout, frame time).

Diagnostics retain active matches and the last 50 ended matches, with 256 recent
events per match. GameHost restart loses them. A `410` means a known match has
no retained diagnostics, including matches played before this feature. `401`
means the console key is missing/wrong; `404` can mean an unknown match or a
disabled owner API. `503` means the upstream is unavailable/unprovisioned;
`502` means an invalid or oversized response. Never report an expired log as
proof that a match was clean.

For authorized automation, use the local fixed proxy
`GET /api/matches/<canonical-match-GUID>/diagnostics`, with `X-Console-Key` read
privately from `~/.local/state/sloparena-console/console.key`. Keep the key out
of command arguments, URLs, chat, artifacts and browser storage. The upstream
route is private; a public `/owner/...` 404 is expected. See the runbook's
[match diagnostic export](../../deploy/vps/README.md#match-diagnostics).

## Bounded VPS process logs

The current operator has passwordless sudo for only the following exact
commands. Use these directly for log requests; privileged `status`, arbitrary
Docker commands or changing `--tail` are not prerequisites and are not covered
by this grant.

```bash
ssh -o BatchMode=yes -o StrictHostKeyChecking=yes sloparena \
  'sudo -n -- /usr/bin/python3 -I /opt/sloparena/deploy/vps/release.py logs --target-dir /var/lib/sloparena --service game --tail 200'
ssh -o BatchMode=yes -o StrictHostKeyChecking=yes sloparena \
  'sudo -n -- /usr/bin/python3 -I /opt/sloparena/deploy/vps/release.py logs --target-dir /var/lib/sloparena --service master --tail 200'
```

Capture raw output in private local evidence and report only the relevant
sanitized lines. Look for Steam login/listener readiness, registration/heartbeat,
Bootstrap/Ready/Clock controls, countdown/GO, winner/shutdown, tick scheduling and
connection errors. Match logs may have rotated; a 200-line tail is not full
match history. Preserve client `Player.log` before another launch overwrites it.

If SSH rejects authentication, the alias already selects the systemd user agent.
Have the operator unlock the existing encrypted key in their own terminal:

```bash
SSH_AUTH_SOCK="/run/user/$(id -u)/ssh-agent.socket" ssh-add ~/.ssh/sloparena_vps
```

Keep the passphrase in that terminal. Do not start another SSH agent, disable host-key
checking, request the passphrase in chat, or widen sudo/Docker access for logs.
If a granted command is rejected, report the exact error and use MasterConsole
or an authenticated operator session for the missing evidence.

## Readiness and client evidence

```bash
curl --fail --silent --show-error --max-time 10 \
  https://master-test.sloparena.barakaslurp.fr/ready
```

`/ready` plus a fresh MasterConsole server heartbeat is useful read-only evidence.
`/health` and container uptime alone do not prove admission. The console marks a
heartbeat stale at 15 seconds. Full image/schema/backup status uses
`release.py status` in an authenticated operator sudo session; do not assume
`sudo -n status` works. **Check Playtest VPS** checks the CI tunnel/refusal
boundaries, not live application readiness; it is not a routine log-fetch step.

Client logs:

- Windows: `%USERPROFILE%\AppData\LocalLow\SLOPArena\SLOPArena\Player.log` (case-insensitive, so it also finds older installs).
- Proton: the Playtest's `steamapps/compatdata/5325920/pfx/drive_c/users/steamuser/AppData/LocalLow/SLOPArena/SLOPArena/Player.log` under the selected Steam library; installs from before the SLOPArena rename keep `LocalLow/SlopArena/SlopArena/`.
- Linux Editor: `~/.config/unity3d/Editor.log`.

Frozen before countdown: inspect startup control rejection and the full-roster
Ready barrier before changing clock estimation. Snapping during hits: inspect
hitstun/reaction replication and repeated animation restarts as well as timing
and transport. Compare the installed client, deployed host and Master protocol;
a source edit does not update a Steam installation.

## Explicit historical alternative

Only for an explicitly selected Alfred home deployment, read
[the historical home runbook](../../docs/systems/production-hosting.md) and
revalidate its paths/configuration with the current homelab instructions. Its
raw-UDP/firewall/rsync recipes do not apply to the Steam-only VPS.
