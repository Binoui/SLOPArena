# Troubleshooting — SlopArena Online (master + game server + client)

Operational debugging guide for the historical home setup on `alfred`. Companion
to [Production Hosting](production-hosting.md). The current Steam Playtest uses
the separate VPS: start with [Release Pipeline](release-pipeline.md) and the
[VPS runbook](../../deploy/vps/README.md), not the home public-UDP recipes below.
The historical failure entries originated in the 2026-08-02 home playtest.

## 0. Where the logs live

| Component | Where | What to look for |
|---|---|---|
| Client (Windows exe / Steam Proton) | `%USERPROFILE%\AppData\LocalLow\SlopArena\SlopArena\Player.log` — under Proton: `<steamapps>/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/SlopArena/SlopArena/Player.log` | `[ServerBrowser]`, `[LobbyClient]`, `Exception`, `Error` |
| Client (Unity Editor, Linux) | `~/.config/unity3d/Editor.log` (or the Editor console) | same markers |
| Game server | `ssh alfred 'docker compose -f /root/homelab/sloparena/docker-compose.yml logs server-1'` | `[Registration]`, `[Heartbeat]`, `[MatchControl]`, `[MatchInstance]` |
| Master server | same, `... logs master` | `Game server registered/re-registered`, `Rate limit exceeded`, `error` |
| Postgres | `ssh alfred 'docker exec sloparena-postgres psql -U sloparena -d sloparena -c "..."'` | `GameServers` rows, heartbeat freshness |

## 1. The diagnostic ladder (fastest → deepest)

```bash
# 1. Is the tunnel up? (client can reach the master API)
curl -s https://sloparena.barakaslurp.fr/health          # expect {"status":"ok",...}

# 2. Are all containers up?
ssh alfred 'cd /root/homelab/sloparena && docker compose ps'

# 3. Is the game server registered AND heartbeating (fresh = seconds old)?
ssh alfred 'docker exec sloparena-postgres psql -U sloparena -d sloparena -c \
  "SELECT \"Name\", \"IpAddress\", \"Port\", \"IsOfficial\", NOW() - \"LastHeartbeat\" AS age FROM \"GameServers\";"'
# age > 30s → heartbeat dead → server crashed or unreachable

# 4. Is UDP actually reachable? (firewall/forward issues hide here)
timeout 8 bash -c 'echo > /dev/tcp/<alfred-lan-ip>/7777' && echo "TCP OK"     # 0=OK, 124=DROP
timeout 8 bash -c 'echo > /dev/tcp/<public-ip>/7777' && echo "public OK" # hairpin through Bbox

# 5. Client log tail — the actual player experience
tail -40 ~/.local/share/Steam/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/SlopArena/SlopArena/Player.log
```

## Steam match loads but both fighters freeze before countdown

If `Player.log` repeatedly reports `[NetworkClient] Steam connection closed:
Invalid or spoofed Steam control frame` while `[PvP]` stays at `serverTick=0`,
check the Steam startup control handler before changing clock estimation.
The client sends reliable Bootstrap requests after admission until it receives
the server Clock acknowledgement. Rejecting Bootstrap as though only Ready were
valid disconnects the peer before the full-roster readiness barrier can complete.

The bound Steam handler must accept Bootstrap idempotently without marking the
player Ready; `MatchInstance` already sends baseline and clock controls to bound
peers. Ready still requires the same connection, entity and authoritative clock
validation. Unreliable, malformed, wrong-version, spoofed and client-sent Clock
frames remain invalid. The server fix uses the existing protocol and requires
a deployed GameHost update; editing this checkout does not repair a running VPS.

Run the startup regression and rejection checks with:

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj --nologo -p:SkipUnityPluginCopy=true \
  --filter 'FullyQualifiedName~SteamBootstrapBeforeReady|FullyQualifiedName~SteamStartupControls|FullyQualifiedName~SteamReadyRetains'
```

## Projectile aim diverges after camera turns

Yaw is a circular direction, not a value to saturate at the signed-short limit.
Human input must normalize both facing and aim yaw before encoding signed
centidegrees; resolve an active ability's aim override before that conversion.
For example, camera yaw 450° must arrive as 90°, not 327.67°. Pitch retains its
separate ±90° clamp, and the packet layout does not change.

Run `Tools/SlopArena/Tests/Training Sequence Parser` in the Editor for the human
input/wire regression. For visual acceptance, hold Manki R (controller LB+X /
Square), turn the aim camera through full rotations in both directions, and
release: the rocket should follow the camera, including vertical aim. Updating
source does not update the installed Steam player; use the approved release
pipeline before retesting the published build.

## 2. Failure catalog

### 2.1 "I hit Join and it just hangs" — UFW dropping game ports

**Symptom:** client log shows `[ServerBrowser] Joining server: SlopArena EU #1 (sloparena.barakaslurp.fr:7777)` then nothing; TCP probe to `<alfred-lan-ip>:7777` times out (exit 124); loopback works.

**Cause:** alfred's UFW has `INPUT policy DROP` and game ports weren't allowed. Bbox forward ≠ host firewall — both must be open.

**Fix:**
```bash
ssh alfred 'sudo ufw allow 7777/tcp && sudo ufw allow 7777:7791/udp'
sudo ufw status | grep 7777   # confirm
```
**Verify:** re-run the TCP/UDP probes (section 1 step 4) — both must pass.

### 2.2 Registration fails `400 Invalid IP address: <domain>` (master side)

**Symptom:** server logs `[Registration] Failed: BadRequest — {"error":"Invalid IP address: ..."}`.

**Cause:** master's `IsValidIpAddress` historically required an IPv4 literal; official servers register with a DNS hostname (`sloparena.barakaslurp.fr`). Fixed in master `Program.cs` (`Uri.CheckHostName is IPv4 or Dns`). If you still see it, the deployed master is stale — redeploy per runbook.

**Current binaries:** registration retries transient Master outages and recovers
from a lost server record. A missing or wrong approved-host credential in the
VPS profile fails startup rather than retrying. The legacy home deployment
needs an explicit `development` profile before upgrading either binary;
do not restart it solely because Master restarted.

### 2.3 False 429s in the master test suite

**Symptom:** `LobbyHubAuthIntegrationTests...Returns200` fails with 429 when running the full suite, passes alone.

**Cause:** `RateLimitTracker` was a static class — all parallel test factories shared one counter. Now a DI singleton (per app instance). If it regresses, check `Program.cs` registers `RateLimitTracker` and the middleware resolves it via `context.RequestServices`.

### 2.4 rsync wiped the master config

**Symptom:** master container starts, then dies / loses DB connection after a deploy; `appsettings.Production.json` missing.

**Cause:** `rsync --delete` or `--delete-excluded` on the master publish dir deletes the config that lives inside `publish/`. **Never use `--delete*` on the master rsync.** Use `--exclude 'appsettings.Production.json'` with plain `rsync -avz`. If the config is gone: rebuild it (real DB password from `/root/homelab/sloparena/.env`, fresh JWT secret via `openssl rand -base64 48`), chmod 600. Old JWTs die — acceptable, clients re-auth.

### 2.5 `MSB3030` / exploding publish output (master repo)

**Symptom:** `dotnet publish` fails with `MSB3030: Could not copy ... MasterServer.Tests/bin/Debug/...` recursing deeper each run.

**Cause:** publishing into `build/minipc-master` inside the repo — the `MasterServer.Tests` subfolder (excluded from Compile but not content globs) leaks its `bin/obj` into the output and copies itself on each publish.

**Fix:** publish to `/tmp/minipc-master` (or add `<Content Remove="MasterServer.Tests\**" />` to the csproj). Always `/tmp` for master publishes.

### 2.6 Unity player build fails `build target was unsupported` / `UnityEditor.Handles` CS0234

**Symptom:** `build-release.sh` aborts at the Unity step.

**Causes (both hit):**
1. Missing **Windows Build Support (Mono)** module on Linux editors: `ls Editor/Data/PlaybackEngines/` lacks `WindowsStandaloneSupport`. Fix: `/opt/unityhub/unityhub-bin -- --headless install-modules --version 6000.0.78f1 -m windows-mono`.
2. Editor-only API in runtime scripts: `UnityEditor.Handles` in `OnDrawGizmos` (fixed with `#if UNITY_EDITOR`). Grep for `UnityEditor\.` in `Assets/Scripts/Runtime/` before release builds.

**Note:** `dotnet build` does not compile Unity scripts. Use the authorized native gateway build/compile path for an open Editor; standalone batchmode requires a separately authorized, confirmed-closed project. Never close another owner's Editor to clear a lock. See [Unity CLI](../contributing/unity-cli.md).

### 2.7 Saved settings / staging after a failed release build

`build-release.sh` saves the existing `ProjectSettings.asset` bytes and
StreamingAssets directories before staging. Its EXIT trap restores those saved
inputs on normal success or failure; it refuses unstaged settings edits and does
not restore settings from Git. A forced termination can prevent the trap from
running: first prove the process has stopped, then recover only that run's
`build/.release-stage.*` backup. Unity may separately re-serialize assets outside
the saved inputs. Compare against the pre-build snapshot and preserve unrelated
changes; never blanket-reset settings or delete StreamingAssets/meta files.

### 2.8 `localhost:5000` leak in the release zip

**Symptom/check:** Task 7.2 requires `localhost:5000` NOWHERE in the shipped zip.

**Cause:** the server csproj copies `server.json` (dev defaults) into publish output → shipped in `StreamingAssets/Server/`. `build-release.sh` now deletes it from both publish outputs. If it regresses: unzip, `grep -rl "localhost:5000" .`.

### 2.9 Dev machine DNS is broken (known)

The dev machine's DNS resolver is misconfigured (systemd-resolved/NetworkManager). Use `dig @1.1.1.1` and `curl --resolve HOST:443:188.114.97.2` when verifying the tunnel. Not a server fault.

## 3. Arena notes

`data/arenas/` contains 7 files; 4 are small stubs (cross/pit/sanctum/split, <500B) that fail `[ArenaRegistry] Failed to load` — expected, not a deploy bug. Playable: `training`, `colosseum`, `Island_arena`.

## 4. Rebuild-the-zip quick path

```bash
cd ~/Documents/projects/SlopArena
# editor must be CLOSED (Unity refuses a second instance on one project)
./scripts/build-release.sh 0.2.0-demo.1
# verify: unzip -l build/release/SlopArena-0.2.0-demo.1.zip | grep -c server.json   # 0
#         unzip + grep -rl "localhost:5000" .                                      # nothing
# publish: gh release create v0.2.0-demo.1 build/release/SlopArena-0.2.0-demo.1.zip --title ... --notes ...
```

## PvP jerks during hits after startup succeeds

The 2026-10-11 two-player Steam match completed normally but the opponent's
hit animation logged 440 repeated restarts without a new hitstun entry. Protocol 6
omitted HitstunTicks from CharacterStatePacket, leaving raw opponent states at zero;
the renderer's one-tick-countdown test also treated repeated/skipped snapshots as
new hits. Protocol 7 carries the timer and an authoritative HitReactionSequence.
New launches advance the sequence; rendering restarts only on entry or a changed
sequence. Replicated HitstunTicks also enables existing position smoothing.

In MasterConsole select Matches → a match → Pull match logs. Inspect tick work
against the 16.67 ms budget, scheduling delays/resets, timestamped Steam RTT and
connection-quality estimates. Download the snapshot when needed. Retention is
GameHost process-local: active matches plus 50 ended matches, 256 recent events
per match, lost on restart. A missing/expired log is not evidence of a clean match.
These are safe structured match records, not a dump of raw process/authentication
logs. Client FPS/frame time and prediction correction counts remain uncollected.

Regression coverage uses the real snapshot codec and the production presentation
policy, including repeated/skipped frames, shorter re-hits and hitstop. Release
all protocol participants together; protocol 6 clients cannot join protocol 7 hosts.
